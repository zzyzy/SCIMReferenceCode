import { describe, expect, it } from "vitest";
import { SCHEMA_ENTERPRISE, SCHEMA_GROUP, SCHEMA_USER, scim } from "../src/client.js";

/**
 * Retrieving one schema or one resource type, rather than the whole list.
 *
 * RFC 7644 section 4: "Individual schema definitions can be returned by appending the schema
 * URI to the /Schemas endpoint", and "in cases where a request is for a specific ResourceType
 * or Schema, the single JSON object is returned in the same way that a single User or Group
 * is retrieved". Only the collection endpoints existed, so every such request was a 404 with
 * an empty body.
 *
 * The routes are catch-all on both legs. A schema URI is
 * "urn:ietf:params:scim:schemas:core:2.0:User" - dots and colons throughout - and a plain
 * {identifier} route parameter stops at the first dot, so the URI arrives truncated.
 */
describe("A single schema is retrievable by its URI", () => {
  it.each([SCHEMA_USER, SCHEMA_GROUP, SCHEMA_ENTERPRISE])("returns %s", async (id) => {
    const response = await scim("GET", `/Schemas/${id}`);

    expect(response.status).toBe(200);
    expect(response.body.id).toBe(id);
  });

  it("returns the bare resource, not a ListResponse of one", async () => {
    const response = await scim("GET", `/Schemas/${SCHEMA_USER}`);

    expect(response.body.Resources).toBeUndefined();
    expect(response.body.totalResults).toBeUndefined();
    expect(Array.isArray(response.body.attributes)).toBe(true);
  });

  it("answers 404 for a schema the service does not define", async () => {
    const response = await scim("GET", "/Schemas/urn:ietf:params:scim:schemas:core:2.0:Widget");

    expect(response.status).toBe(404);
  });

  it("still lists every schema at the collection", async () => {
    const response = await scim("GET", "/Schemas");

    expect(response.status).toBe(200);
    expect(response.body.Resources.length).toBeGreaterThan(0);
  });
});

describe("A single resource type is retrievable by its identifier", () => {
  it.each(["User", "Group"])("returns %s", async (id) => {
    const response = await scim("GET", `/ResourceTypes/${id}`);

    expect(response.status).toBe(200);
    expect(response.body.id).toBe(id);
    expect(response.body.endpoint).toBe(`/${id}s`);
  });

  it("answers 404 for a resource type the service does not serve", async () => {
    const response = await scim("GET", "/ResourceTypes/Widget");

    expect(response.status).toBe(404);
  });
});

describe("The advertised schema matches what the service actually returns", () => {
  // A client that validates a response against /Schemas - which is what the endpoint is for -
  // rejects a body carrying an attribute the schema does not declare. The Group schema
  // declared members as value and type only, while every membership is returned with $ref.
  it("declares every sub-attribute of members that a group carries", async () => {
    const response = await scim("GET", `/Schemas/${SCHEMA_GROUP}`);
    const members = (response.body.attributes as Record<string, any>[]).find(
      (attribute) => attribute["name"] === "members",
    );

    const declared = (members?.["subAttributes"] as Record<string, string>[]).map(
      (sub) => sub["name"],
    );

    expect(declared).toEqual(expect.arrayContaining(["value", "$ref", "type", "display"]));
  });
});
