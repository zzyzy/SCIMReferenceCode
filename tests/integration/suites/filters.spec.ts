import { beforeAll, describe, expect, it } from "vitest";
import {
  SCHEMA_LIST,
  createUser,
  filterQuery,
  scim,
  unique,
  type ScimResource,
} from "../src/client.js";

/**
 * RFC 7644 3.4.2.2 lists nine comparison operators. co and sw were unimplemented in
 * the library, and the error reporting the failure named the wrong operator - which
 * is why the gap went unnoticed. All nine are exercised here.
 */
describe("Filters: the nine comparison operators", () => {
  let alice: ScimResource;
  let marker: string;

  beforeAll(async () => {
    marker = unique("flt");
    alice = await createUser({ userName: `${marker}.alice@example.sg`, title: "Teacher" });
    await createUser({ userName: `${marker}.bob@example.sg`, title: "HOD" });
  });

  it("eq matches exactly one", async () => {
    const response = await scim("GET", `/Users${filterQuery(`userName eq "${alice.userName}"`)}`);

    expect(response.status).toBe(200);
    expect(response.body.schemas).toContain(SCHEMA_LIST);
    expect(response.body.Resources).toHaveLength(1);
  });

  it("co matches a substring", async () => {
    const response = await scim("GET", `/Users${filterQuery(`userName co "${marker}.alice"`)}`);

    expect(response.status).toBe(200);
    expect(response.body.Resources).toHaveLength(1);
  });

  it("sw matches a prefix", async () => {
    const response = await scim("GET", `/Users${filterQuery(`userName sw "${marker}."`)}`);

    expect(response.status).toBe(200);
    expect(response.body.Resources.length).toBeGreaterThanOrEqual(2);
  });

  it("ew matches a suffix", async () => {
    const response = await scim("GET", `/Users${filterQuery(`userName ew "alice@example.sg"`)}`);

    expect(response.status).toBe(200);
    expect(response.body.Resources.length).toBeGreaterThanOrEqual(1);
  });

  it("ne excludes the named value", async () => {
    const response = await scim("GET", `/Users${filterQuery(`userName ne "${alice.userName}"`)}`);

    expect(response.status).toBe(200);
    const names = response.body.Resources.map((r: ScimResource) => r.userName);
    expect(names).not.toContain(alice.userName);
  });

  it.each(["gt", "ge", "lt", "le"])("%s compares meta.lastModified", async (operator) => {
    const boundary = operator === "gt" || operator === "ge" ? "2000-01-01T00:00:00Z" : "2999-01-01T00:00:00Z";
    const response = await scim(
      "GET",
      `/Users${filterQuery(`meta.lastModified ${operator} "${boundary}"`)}`,
    );

    expect(response.status).toBe(200);
    expect(response.body.Resources.length).toBeGreaterThanOrEqual(1);
  });
});

describe("Filters: grouping and rejection", () => {
  it("honours or", async () => {
    const first = await createUser();
    const second = await createUser();
    const response = await scim(
      "GET",
      `/Users${filterQuery(`userName eq "${first.userName}" or userName eq "${second.userName}"`)}`,
    );

    expect(response.status).toBe(200);
    expect(response.body.Resources).toHaveLength(2);
  });

  it("honours and", async () => {
    const created = await createUser({ title: "Teacher" });
    const response = await scim(
      "GET",
      `/Users${filterQuery(`userName eq "${created.userName}" and active eq "true"`)}`,
    );

    expect(response.status).toBe(200);
    expect(response.body.Resources).toHaveLength(1);
  });

  it("rejects a malformed filter with invalidFilter", async () => {
    const response = await scim("GET", `/Users${filterQuery("userName eq")}`);

    expect(response.status).toBe(400);
    expect(response.body.scimType).toBe("invalidFilter");
  });

  it("refuses a filter naming an attribute the schema does not define", async () => {
    // Deliberately not an unsupported *operator*: this assertion was written twice
    // against operators that later became supported, so it tracked the
    // implementation rather than the contract. An undefined attribute cannot
    // become answerable.
    const response = await scim("GET", `/Users${filterQuery('nosuchattribute eq "x"')}`);

    expect([400, 501]).toContain(response.status);
  });

  it("treats attribute names case-insensitively", async () => {
    const created = await createUser();
    const response = await scim("GET", `/Users${filterQuery(`USERNAME eq "${created.userName}"`)}`);

    expect(response.status).toBe(200);
    expect(response.body.Resources).toHaveLength(1);
  });

  it("survives quote injection in a filter value", async () => {
    const response = await scim("GET", `/Users${filterQuery('userName eq "\\" or 1=1 --"')}`);

    expect([200, 400]).toContain(response.status);
  });
});

describe("Projection", () => {
  it("narrows a single resource to the requested attributes", async () => {
    const created = await createUser({ title: "Teacher" });
    const response = await scim("GET", `/Users/${created.id}?attributes=userName`);

    expect(response.status).toBe(200);
    expect(response.body).toHaveProperty("userName");
    expect(response.body).toHaveProperty("id");
    expect(response.body).not.toHaveProperty("title");
  });

  it("drops an excluded attribute", async () => {
    const created = await createUser({ title: "Teacher" });
    const response = await scim("GET", `/Users/${created.id}?excludedAttributes=title`);

    expect(response.status).toBe(200);
    expect(response.body).not.toHaveProperty("title");
    expect(response.body).toHaveProperty("userName");
  });

  it("projects a sub-attribute", async () => {
    const created = await createUser();
    const response = await scim("GET", `/Users/${created.id}?attributes=name.givenName`);

    expect(response.status).toBe(200);
    expect(response.body.name).toHaveProperty("givenName");
    expect(response.body.name).not.toHaveProperty("familyName");
  });

  it("applies to every resource of a list response", async () => {
    await createUser({ title: "Teacher" });
    const response = await scim("GET", "/Users?attributes=userName");

    expect(response.status).toBe(200);
    for (const resource of response.body.Resources as ScimResource[]) {
      expect(resource).toHaveProperty("userName");
      expect(resource).not.toHaveProperty("title");
    }
  });

  it("keeps id and schemas however hard they are excluded", async () => {
    // RFC 7644 3.9: an attribute with returned=always is returned regardless.
    const created = await createUser();
    const response = await scim("GET", `/Users/${created.id}?excludedAttributes=id,schemas`);

    expect(response.status).toBe(200);
    expect(response.body).toHaveProperty("id");
    expect(response.body).toHaveProperty("schemas");
  });

  it("still returns id when asked for an attribute that does not exist", async () => {
    const created = await createUser();
    const response = await scim("GET", `/Users/${created.id}?attributes=nosuchattribute`);

    expect(response.status).toBe(200);
    expect(response.body).toHaveProperty("id");
  });
});

describe("Pagination", () => {
  it("honours startIndex and count", async () => {
    await createUser();
    await createUser();
    const response = await scim("GET", "/Users?startIndex=2&count=1");

    expect(response.status).toBe(200);
    expect(response.body.startIndex).toBe(2);
    expect(response.body.Resources).toHaveLength(1);
  });

  it("reads a startIndex below one as one", async () => {
    const response = await scim("GET", "/Users?startIndex=0");

    expect(response.status).toBe(200);
    expect(response.body.startIndex).toBe(1);
  });

  it("returns an empty page past the end while still reporting the total", async () => {
    await createUser();
    const response = await scim("GET", "/Users?startIndex=99999&count=10");

    expect(response.status).toBe(200);
    expect(response.body.Resources).toHaveLength(0);
    expect(response.body.totalResults).toBeGreaterThan(0);
  });

  it("rejects a non-numeric page parameter rather than failing", async () => {
    // Regression: int.Parse on the raw query value threw FormatException, which
    // nothing catches, so this answered 500.
    const response = await scim("GET", "/Users?count=abc&startIndex=xyz");

    expect(response.status).toBe(400);
  });

  it("walks the whole collection without repeating or skipping a resource", async () => {
    const created: string[] = [];
    for (let index = 0; index < 25; index += 1) {
      created.push((await createUser()).id);
    }

    const seen: string[] = [];
    for (let start = 1; ; start += 10) {
      const page = await scim("GET", `/Users?startIndex=${start}&count=10`);
      const resources = page.body.Resources as ScimResource[];
      if (resources.length === 0) {
        break;
      }
      seen.push(...resources.map((resource) => resource.id));
      if (start > 5000) {
        throw new Error("paging did not terminate");
      }
    }

    expect(new Set(seen).size).toBe(seen.length);
    for (const id of created) {
      expect(seen).toContain(id);
    }
  });
});
