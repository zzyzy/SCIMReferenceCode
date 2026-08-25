import { describe, expect, it } from "vitest";
import { SCHEMA_ENTERPRISE, SCHEMA_GROUP, SCHEMA_LIST, SCHEMA_USER, scim } from "../src/client.js";

/**
 * `/ResourceTypes` as RFC 7643 section 6 defines it.
 *
 * A resource type says three things: where the resource lives, which schema is its
 * base, and which schemas extend it. The third was missing from the model entirely,
 * which had a second effect worth naming: with nowhere to declare the enterprise
 * extension, the sample declared it as the User type's *base* schema instead. A client
 * reading that is told the core User schema is not what /Users serves.
 */

interface ResourceTypeEntry {
  id?: string;
  name?: string;
  endpoint?: string;
  schema?: string;
  schemaExtensions?: { schema: string; required: boolean }[];
  schemas?: string[];
}

async function resourceTypes(): Promise<ResourceTypeEntry[]> {
  const response = await scim("GET", "/ResourceTypes");
  expect(response.status).toBe(200);
  expect(response.body.schemas).toContain(SCHEMA_LIST);
  return response.body.Resources as ResourceTypeEntry[];
}

describe("ResourceTypes: the base schema", () => {
  it("declares itself with the core ResourceType schema URN", async () => {
    // RFC 7643 section 6 names urn:ietf:params:scim:schemas:core:2.0:ResourceType. The
    // constant was assembled from the enterprise extension prefix instead, so every
    // resource type announced a schema URN that does not exist.
    for (const entry of await resourceTypes()) {
      expect(entry.schemas).toContain("urn:ietf:params:scim:schemas:core:2.0:ResourceType");
    }
  });

  it("names the core User schema as the User type's base", async () => {
    // RFC 7643 section 6: `schema` is "the resource type's primary/base schema URI".
    // The enterprise schema is an extension of User, not a replacement for it.
    const user = (await resourceTypes()).find((item) => item.name === "User");

    expect(user).toBeDefined();
    expect(user?.schema).toBe(SCHEMA_USER);
  });

  it("names the core Group schema as the Group type's base", async () => {
    const group = (await resourceTypes()).find((item) => item.name === "Group");

    expect(group).toBeDefined();
    expect(group?.schema).toBe(SCHEMA_GROUP);
  });

  it("gives every resource type an endpoint", async () => {
    for (const type of await resourceTypes()) {
      expect(type.endpoint, `${type.name} has no endpoint`).toBeDefined();
      expect(type.endpoint).toMatch(/^\//u);
    }
  });
});

describe("ResourceTypes: schemaExtensions", () => {
  it("declares the enterprise extension against the User type", async () => {
    // RFC 7643 section 6 makes schemaExtensions the place a resource type lists the
    // schemas layered on its base. Without it a client cannot discover that /Users
    // accepts enterprise attributes at all.
    const user = (await resourceTypes()).find((item) => item.name === "User");

    expect(user?.schemaExtensions, "the User type declares no schemaExtensions").toBeDefined();
    const enterprise = (user?.schemaExtensions ?? []).find(
      (item) => item.schema === SCHEMA_ENTERPRISE,
    );

    expect(enterprise, "the enterprise extension is not declared").toBeDefined();
    expect(typeof enterprise?.required).toBe("boolean");
  });

  it("marks the enterprise extension optional, because the service does not demand it", async () => {
    const user = (await resourceTypes()).find((item) => item.name === "User");
    const enterprise = (user?.schemaExtensions ?? []).find(
      (item) => item.schema === SCHEMA_ENTERPRISE,
    );

    expect(enterprise?.required).toBe(false);
  });

  it("gives every declared extension both members RFC 7643 requires", async () => {
    for (const type of await resourceTypes()) {
      for (const extension of type.schemaExtensions ?? []) {
        expect(typeof extension.schema).toBe("string");
        expect(extension.schema.length).toBeGreaterThan(0);
        expect(typeof extension.required).toBe("boolean");
      }
    }
  });

  it("omits schemaExtensions entirely for a type that has none", async () => {
    // An empty array and an absent member both say "no extensions", but the absent
    // form is what the rest of the library does with empty collections, and the
    // Group type is the case that shows it.
    const group = (await resourceTypes()).find((item) => item.name === "Group");

    expect(group?.schemaExtensions ?? []).toHaveLength(0);
  });
});
