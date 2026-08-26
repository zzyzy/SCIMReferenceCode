import { describe, expect, it } from "vitest";
import { SCHEMA_LIST, createGroup, createUser, scim, unique } from "../src/client.js";

/**
 * Querying with POST to a "/.search" endpoint, per RFC 7644 section 3.4.3.
 *
 * The same parameters section 3.4.2 defines for the query string, carried in a body instead:
 * a filter long enough to overflow a URL, or one a client would rather not have written to
 * every access log along the way, has nowhere else to go.
 *
 * The service answers a search by rendering the body back into a query string and handing it
 * to the code that already serves GET, so the cases that matter here are the ones where the
 * two could disagree - the projection, the filter and the paging - plus the endpoint's own
 * rules about what is and is not a search request.
 */
const SEARCH = "urn:ietf:params:scim:api:messages:2.0:SearchRequest";

const search = (path: string, body: Record<string, unknown> = {}) =>
  scim("POST", path, { schemas: [SEARCH], ...body });

describe("A search returns what the equivalent GET returns", () => {
  it("answers with a ListResponse", async () => {
    await createUser();
    const response = await search("/Users/.search");

    expect(response.status).toBe(200);
    expect(response.body.schemas).toEqual([SCHEMA_LIST]);
    expect(typeof response.body.totalResults).toBe("number");
  });

  it("applies a filter from the body", async () => {
    const userName = `${unique("search")}@example.sg`;
    await createUser({ userName });

    const response = await search("/Users/.search", { filter: `userName eq "${userName}"` });

    expect(response.status).toBe(200);
    expect(response.body.totalResults).toBe(1);
    expect(response.body.Resources[0].userName).toBe(userName);
  });

  it("projects exactly as the query string does", async () => {
    // The body is rendered back into a query string, so a difference here means the two have
    // drifted. They had: UriBuilder.Query adds a "?" of its own, so a rendered string that
    // already carried one produced "??attributes=…" and the parameter was silently ignored.
    const userName = `${unique("search")}@example.sg`;
    await createUser({ userName });
    const filter = `userName eq "${userName}"`;

    const viaGet = await scim(
      "GET",
      `/Users?filter=${encodeURIComponent(filter)}&attributes=userName`,
    );
    const viaSearch = await search("/Users/.search", { filter, attributes: ["userName"] });

    expect(viaSearch.body.Resources).toEqual(viaGet.body.Resources);
  });

  it("honours excludedAttributes", async () => {
    const userName = `${unique("search")}@example.sg`;
    await createUser({ userName, displayName: "Excluded" });

    const response = await search("/Users/.search", {
      filter: `userName eq "${userName}"`,
      excludedAttributes: ["displayName"],
    });

    expect(response.body.Resources[0].displayName).toBeUndefined();
  });

  it("pages from the body", async () => {
    await createUser();
    await createUser();

    const response = await search("/Users/.search", { startIndex: 1, count: 1 });

    expect(response.body.itemsPerPage).toBe(1);
    expect(response.body.startIndex).toBe(1);
    expect(response.body.Resources).toHaveLength(1);
    expect(response.body.totalResults).toBeGreaterThanOrEqual(2);
  });

  it("searches groups too", async () => {
    const created = await createGroup();

    const response = await search("/Groups/.search", {
      filter: `displayName eq "${created.displayName}"`,
    });

    expect(response.status).toBe(200);
    expect(response.body.totalResults).toBe(1);
  });
});

describe("A search of the service root covers every resource type", () => {
  // RFC 7644 3.4.2 permits a query "against the service provider Base URI", and 3.2 names the
  // base endpoint's POST query "search from system".
  it("returns users and groups together", async () => {
    await createUser();
    await createGroup();

    const response = await search("/.search");

    expect(response.status).toBe(200);

    const types = new Set(
      (response.body.Resources as Record<string, any>[]).map(
        (resource) => resource["meta"]["resourceType"],
      ),
    );

    expect(types).toContain("User");
    expect(types).toContain("Group");
  });

  it("answers a filter only one type can satisfy", async () => {
    // Groups have no userName. A type that cannot answer the filter contributes nothing,
    // rather than failing the whole search.
    const userName = `${unique("root")}@example.sg`;
    await createUser({ userName });
    await createGroup();

    const response = await search("/.search", { filter: `userName eq "${userName}"` });

    expect(response.status).toBe(200);
    expect(response.body.totalResults).toBe(1);
    expect(response.body.Resources[0].meta.resourceType).toBe("User");
  });

  it("pages over the merged set rather than over each type", async () => {
    await createUser();
    await createGroup();

    const response = await search("/.search", { count: 1 });

    expect(response.body.itemsPerPage).toBe(1);
    expect(response.body.Resources).toHaveLength(1);
    expect(response.body.totalResults).toBeGreaterThanOrEqual(2);
  });
});

describe("What is not a search request", () => {
  it("refuses a body that does not name the SearchRequest schema", async () => {
    // RFC 7644 3.4.3: "Query requests MUST be identified using the following URI". Answering
    // one that is not would let a client's mistake read as a successful search of everything.
    const response = await scim("POST", "/Users/.search", {
      schemas: ["urn:ietf:params:scim:schemas:core:2.0:User"],
    });

    expect(response.status).toBe(400);
  });

  it("refuses a malformed filter, as the query string does", async () => {
    const response = await search("/Users/.search", { filter: "this is not a filter" });

    expect(response.status).toBe(400);
  });

  it("does not shadow creation on the collection endpoint", async () => {
    // ".search" is its own route. If it had been a branch inside POST, a creation whose body
    // failed to bind could have been answered as a search.
    const response = await scim("POST", "/Users", {
      schemas: ["urn:ietf:params:scim:schemas:core:2.0:User"],
      userName: `${unique("create")}@example.sg`,
    });

    expect(response.status).toBe(201);
  });
});
