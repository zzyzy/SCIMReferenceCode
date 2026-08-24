import { describe, expect, it } from "vitest";
import {
  SCHEMA_USER,
  createUser,
  filterQuery,
  patchOp,
  readUser,
  scim,
  unique,
  userBody,
} from "../src/client.js";

describe("Hostile and malformed input", () => {
  it("survives a 10,000 character userName", async () => {
    const response = await scim("POST", "/Users", {
      schemas: [SCHEMA_USER],
      userName: `${"x".repeat(10_000)}@example.sg`,
      active: true,
    });

    expect([201, 400, 413]).toContain(response.status);
  });

  it("survives 200 levels of nesting", async () => {
    // Deep nesting is a classic parser denial of service; the concern is a stack
    // overflow taking the process down, not the status code.
    const body: Record<string, unknown> = userBody();
    let node = body;
    for (let depth = 0; depth < 200; depth += 1) {
      const nested: Record<string, unknown> = {};
      node["nested"] = nested;
      node = nested;
    }

    const response = await scim("POST", "/Users", body);
    expect([201, 400, 413]).toContain(response.status);
  });

  it("survives a 500-element multi-valued attribute", async () => {
    const response = await scim(
      "POST",
      "/Users",
      userBody({
        emails: Array.from({ length: 500 }, (_unused, index) => ({
          value: `e${index}@x.sg`,
          type: "work",
        })),
      }),
    );

    expect([201, 400, 413]).toContain(response.status);
  });

  it("round-trips non-ASCII unchanged", async () => {
    const displayName = "你好 مرحبا \u{1f600}";
    const created = await createUser({ displayName });

    expect(created.displayName).toBe(displayName);
    expect((await readUser(created.id)).displayName).toBe(displayName);
  });

  it("refuses a traversal-shaped identifier", async () => {
    const response = await scim("GET", `/Users/${encodeURIComponent("../../etc/passwd")}`);
    expect([400, 404]).toContain(response.status);
  });

  it("stores a repeated schema URN only once", async () => {
    const response = await scim("POST", "/Users", {
      schemas: [SCHEMA_USER, SCHEMA_USER],
      userName: `${unique("dup")}@example.sg`,
      active: true,
    });

    expect([201, 400]).toContain(response.status);
    if (response.status === 201) {
      const schemas = response.body.schemas as string[];
      expect(new Set(schemas).size).toBe(schemas.length);
    }
  });

  it("tolerates a body with no schemas array", async () => {
    const response = await scim("POST", "/Users", {
      userName: `${unique("noschema")}@example.sg`,
      active: true,
    });

    expect([201, 400]).toContain(response.status);
  });

  it("tolerates a body declaring the wrong schema", async () => {
    const response = await scim("POST", "/Users", {
      schemas: ["urn:ietf:params:scim:schemas:core:2.0:Group"],
      userName: `${unique("wrong")}@example.sg`,
      active: true,
    });

    expect([201, 400]).toContain(response.status);
  });

  it("tolerates a patch with no Operations, and one with none at all", async () => {
    const created = await createUser();

    expect([200, 204, 400]).toContain(
      (await scim("PATCH", `/Users/${created.id}`, { schemas: [patchOp().schemas as never].flat(), Operations: [] })).status,
    );
    expect([200, 204, 400]).toContain(
      (await scim("PATCH", `/Users/${created.id}`, { schemas: (patchOp().schemas as string[]) })).status,
    );
  });

  it("tolerates a patch operation with no path", async () => {
    const created = await createUser();
    const response = await scim(
      "PATCH",
      `/Users/${created.id}`,
      patchOp({ op: "replace", value: "no path at all" }),
    );

    expect([200, 204, 400]).toContain(response.status);
  });
});

describe("Concurrency", () => {
  async function inParallel<T>(count: number, work: () => Promise<T>): Promise<T[]> {
    return Promise.all(Array.from({ length: count }, () => work()));
  }

  it("creates one resource when many callers race on one userName", async () => {
    // Regression, and the one that needed repetition to find: the provider tested
    // uniqueness with Any(...) then called Dictionary.Add, unsynchronised. Ten
    // simultaneous creates produced up to three 201s, and the runtime reported the
    // dictionary's state corrupted - which reached the caller as 500. A single
    // attempt passed by luck; only repeated trials exposed it.
    const trials = 4;

    for (let trial = 0; trial < trials; trial += 1) {
      const userName = `${unique("race")}@example.sg`;
      const statuses = await inParallel(10, async () => {
        const response = await scim("POST", "/Users", {
          schemas: [SCHEMA_USER],
          userName,
          active: true,
        });
        return response.status;
      });

      expect(statuses).not.toContain(500);
      expect(statuses.filter((status) => status === 201)).toHaveLength(1);

      const found = await scim("GET", `/Users${filterQuery(`userName eq "${userName}"`)}`);
      expect(found.body.Resources).toHaveLength(1);
    }
  });

  it("answers every request when many callers patch one resource", async () => {
    const created = await createUser();
    const statuses = await inParallel(10, async () => {
      const response = await scim(
        "PATCH",
        `/Users/${created.id}`,
        patchOp({ op: "replace", path: "title", value: unique("T") }),
      );
      return response.status;
    });

    for (const status of statuses) {
      expect([200, 204]).toContain(status);
    }
  });

  it("answers every read taken while writes are in flight", async () => {
    const writes = inParallel(6, async () => (await createUser()).id);
    const reads = await inParallel(10, async () => (await scim("GET", "/Users")).status);
    await writes;

    for (const status of reads) {
      expect(status).toBe(200);
    }
  });
});

describe("Soak", () => {
  it("runs 300 mixed operations without a failure and leaves nothing behind", async () => {
    const marker = unique("soak");
    const created: string[] = [];

    for (let index = 0; index < 75; index += 1) {
      const post = await scim("POST", "/Users", {
        schemas: [SCHEMA_USER],
        userName: `${marker}.${index}@example.sg`,
        active: true,
      });
      expect(post.status).toBe(201);
      created.push(post.body.id);

      const patched = await scim(
        "PATCH",
        `/Users/${post.body.id}`,
        patchOp({ op: "replace", path: "title", value: `T${index}` }),
      );
      expect([200, 204]).toContain(patched.status);

      expect((await scim("GET", `/Users/${post.body.id}`)).status).toBe(200);
    }

    for (const id of created) {
      expect((await scim("DELETE", `/Users/${id}`)).status).toBe(204);
    }

    const leftover = await scim("GET", `/Users${filterQuery(`userName sw "${marker}"`)}`);
    expect(leftover.body.Resources).toHaveLength(0);
  });
});
