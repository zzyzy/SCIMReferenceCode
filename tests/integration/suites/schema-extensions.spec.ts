import { describe, expect, it } from "vitest";
import {
  PATCH_APPLIED,
  SCHEMA_BULK_REQUEST,
  SCHEMA_GROUP,
  SCHEMA_PATCH,
  SCHEMA_USER,
  createGroup,
  createUser,
  memberIds,
  patchOp,
  scim,
  unique,
  userBody,
  type ScimResource,
} from "../src/client.js";

/**
 * Extension schemas the service was not compiled against, group PATCH, and the
 * remaining collection operations.
 *
 * An extension the library has no type for is carried in an untyped dictionary and has
 * to survive both directions - RFC 7643 section 3 makes a resource's schemas open, and
 * a provisioning client that sends an extension the service drops has no way to tell.
 * That path is the one converter both hosting legs register, and nothing exercised it.
 */

/**
 * An extension the library has no type for.
 *
 * Under the `urn:ietf:params:scim:schemas:extension:` prefix, because that is the only
 * shape the untyped dictionary accepts - see the test at the end of this block.
 */
const CUSTOM = "urn:ietf:params:scim:schemas:extension:example:2.0:Custom";

describe("An extension schema the service was not compiled against", () => {
  it("round-trips a flat extension on create", async () => {
    const created = await scim<ScimResource>("POST", "/Users", {
      ...userBody(),
      schemas: [SCHEMA_USER, CUSTOM],
      [CUSTOM]: { costCentre: "CC-1", building: "Block A" },
    });

    expect(created.status).toBe(201);
    expect(created.body[CUSTOM]).toMatchObject({ costCentre: "CC-1", building: "Block A" });

    const read = await scim<ScimResource>("GET", `/Users/${created.body.id}`);
    expect(read.body[CUSTOM]).toMatchObject({ costCentre: "CC-1" });
  });

  it("keeps a nested object inside an extension", async () => {
    const created = await scim<ScimResource>("POST", "/Users", {
      ...userBody(),
      schemas: [SCHEMA_USER, CUSTOM],
      [CUSTOM]: { location: { site: "SG", floor: "12" } },
    });

    expect(created.status).toBe(201);
    const read = await scim<ScimResource>("GET", `/Users/${created.body.id}`);
    expect((read.body[CUSTOM] as any)?.location).toMatchObject({ site: "SG", floor: "12" });
  });

  it("keeps an array inside an extension", async () => {
    const created = await scim<ScimResource>("POST", "/Users", {
      ...userBody(),
      schemas: [SCHEMA_USER, CUSTOM],
      [CUSTOM]: { tags: ["one", "two", "three"] },
    });

    expect(created.status).toBe(201);
    const read = await scim<ScimResource>("GET", `/Users/${created.body.id}`);
    expect((read.body[CUSTOM] as any)?.tags).toEqual(["one", "two", "three"]);
  });

  it("keeps scalars of every JSON type inside an extension", async () => {
    const created = await scim<ScimResource>("POST", "/Users", {
      ...userBody(),
      schemas: [SCHEMA_USER, CUSTOM],
      [CUSTOM]: { text: "x", number: 42, flag: true, nothing: null },
    });

    expect(created.status).toBe(201);
    const extension = (await scim<ScimResource>("GET", `/Users/${created.body.id}`)).body[
      CUSTOM
    ] as any;
    expect(extension.text).toBe("x");
    expect(extension.number).toBe(42);
    expect(extension.flag).toBe(true);
  });

  it("declares the extension in the response schemas", async () => {
    const created = await scim<ScimResource>("POST", "/Users", {
      ...userBody(),
      schemas: [SCHEMA_USER, CUSTOM],
      [CUSTOM]: { costCentre: "CC-2" },
    });

    expect(created.body.schemas).toContain(CUSTOM);
  });

  it("carries an extension on a group too", async () => {
    // RFC 7643 3.3 makes Group extensible on the same terms as User. The converter used
    // to match the user type alone, so a group extension was accepted and dropped.
    const created = await scim<ScimResource>("POST", "/Groups", {
      schemas: [SCHEMA_GROUP, CUSTOM],
      displayName: unique("extgroup"),
      [CUSTOM]: { owner: "facilities", costCentre: "CC-9" },
    });

    expect(created.status).toBe(201);
    expect(created.body[CUSTOM]).toMatchObject({ owner: "facilities", costCentre: "CC-9" });

    const read = await scim<ScimResource>("GET", `/Groups/${created.body.id}`);
    expect(read.body[CUSTOM]).toMatchObject({ owner: "facilities" });
    expect(read.body.schemas).toContain(CUSTOM);
  });

  it("keeps a group extension across a membership PATCH", async () => {
    // The extension lives on the stored resource, so an unrelated write must not drop it.
    const member = await createUser();
    const created = await scim<ScimResource>("POST", "/Groups", {
      schemas: [SCHEMA_GROUP, CUSTOM],
      displayName: unique("extgroup"),
      [CUSTOM]: { owner: "facilities" },
    });

    await scim(
      "PATCH",
      `/Groups/${created.body.id}`,
      patchOp({ op: "add", path: "members", value: [{ value: member.id }] }),
    );

    const read = await scim<ScimResource>("GET", `/Groups/${created.body.id}`);
    expect(read.body[CUSTOM]).toMatchObject({ owner: "facilities" });
    expect(await memberIds(created.body.id)).toContain(member.id);
  });

  it("keeps a group extension in a query response", async () => {
    const displayName = unique("extgroup");
    await scim("POST", "/Groups", {
      schemas: [SCHEMA_GROUP, CUSTOM],
      displayName,
      [CUSTOM]: { owner: "estates" },
    });

    const found = await scim(
      "GET",
      `/Groups?filter=${encodeURIComponent(`displayName eq "${displayName}"`)}`,
    );

    const group = (found.body.Resources as ScimResource[])[0];
    expect(group?.[CUSTOM]).toMatchObject({ owner: "estates" });
  });

  it("drops an extension whose URI is outside the SCIM extension namespace", async () => {
    // The untyped dictionary accepts only urn:ietf:params:scim:schemas:extension:*.
    // RFC 7643 3.3 does not require that, so a service wanting its own namespace has to
    // declare a typed member instead - which is what SCIM.EduPass does.
    const outside = "urn:example:params:scim:schemas:Custom";

    const created = await scim<ScimResource>("POST", "/Users", {
      ...userBody(),
      schemas: [SCHEMA_USER, outside],
      [outside]: { field: "value" },
    });

    expect(created.status).toBe(201);
    expect(created.body[outside]).toBeUndefined();
  });

  it("survives a replace that omits the extension", async () => {
    const created = await scim<ScimResource>("POST", "/Users", {
      ...userBody(),
      schemas: [SCHEMA_USER, CUSTOM],
      [CUSTOM]: { costCentre: "CC-3" },
    });

    const replaced = await scim<ScimResource>("PUT", `/Users/${created.body.id}`, {
      schemas: [SCHEMA_USER],
      id: created.body.id,
      userName: created.body.userName,
      active: true,
    });

    // A replace is a whole-resource write, so the extension going is correct. What
    // matters is that it does not fault.
    expect(replaced.status).toBe(200);
  });
});

describe("Group PATCH: the attributes beyond members", () => {
  it("replaces and removes externalId", async () => {
    const group = await createGroup({ externalId: unique("gext") });

    expect(PATCH_APPLIED).toContain(
      (
        await scim("PATCH", `/Groups/${group.id}`, patchOp({
          op: "replace",
          path: "externalId",
          value: "changed",
        }))
      ).status,
    );
    expect((await scim<ScimResource>("GET", `/Groups/${group.id}`)).body["externalId"]).toBe(
      "changed",
    );

    await scim("PATCH", `/Groups/${group.id}`, patchOp({ op: "remove", path: "externalId" }));
    expect(
      (await scim<ScimResource>("GET", `/Groups/${group.id}`)).body["externalId"],
    ).toBeUndefined();
  });

  it("keeps externalId when a remove names a different value", async () => {
    const external = unique("gext");
    const group = await createGroup({ externalId: external });

    await scim(
      "PATCH",
      `/Groups/${group.id}`,
      patchOp({ op: "remove", path: "externalId", value: "something else" }),
    );

    expect((await scim<ScimResource>("GET", `/Groups/${group.id}`)).body["externalId"]).toBe(
      external,
    );
  });

  it("keeps displayName when a remove names a different value", async () => {
    // Clearing displayName would leave a group the service cannot describe, and the
    // reference provider refuses to store one without it.
    const group = await createGroup();

    await scim(
      "PATCH",
      `/Groups/${group.id}`,
      patchOp({ op: "remove", path: "displayName", value: "not the display name" }),
    );

    expect((await scim<ScimResource>("GET", `/Groups/${group.id}`)).body["displayName"]).toBe(
      group.displayName,
    );
  });

  it("refuses a group PATCH naming an attribute the schema does not define", async () => {
    const group = await createGroup();

    const patched = await scim(
      "PATCH",
      `/Groups/${group.id}`,
      patchOp({ op: "replace", path: "invented", value: "x" }),
    );

    expect(patched.status).toBe(400);
  });
});

describe("Whole-collection operations on emails", () => {
  it("appends with add", async () => {
    const user = await createUser();

    const patched = await scim(
      "PATCH",
      `/Users/${user.id}`,
      patchOp({ op: "add", path: "emails", value: [{ value: "extra@example.sg" }] }),
    );

    expect(PATCH_APPLIED).toContain(patched.status);
    const emails = (await scim<ScimResource>("GET", `/Users/${user.id}`)).body[
      "emails"
    ] as { value: string }[];
    expect(emails.map((item) => item.value)).toContain("extra@example.sg");
  });

  it("removes a named address", async () => {
    const user = await createUser();
    await scim(
      "PATCH",
      `/Users/${user.id}`,
      patchOp({ op: "add", path: "emails", value: [{ value: "doomed@example.sg" }] }),
    );

    await scim(
      "PATCH",
      `/Users/${user.id}`,
      patchOp({ op: "remove", path: "emails", value: [{ value: "doomed@example.sg" }] }),
    );

    const emails = ((await scim<ScimResource>("GET", `/Users/${user.id}`)).body["emails"] ??
      []) as { value: string }[];
    expect(emails.map((item) => item.value)).not.toContain("doomed@example.sg");
  });

  it("clears the collection on a remove with no value", async () => {
    const user = await createUser();

    await scim("PATCH", `/Users/${user.id}`, patchOp({ op: "remove", path: "emails" }));

    const emails = ((await scim<ScimResource>("GET", `/Users/${user.id}`)).body["emails"] ??
      []) as unknown[];
    expect(emails).toHaveLength(0);
  });
});

describe("Projection into nested attributes", () => {
  it("excludes a sub-attribute of a complex attribute", async () => {
    const user = await createUser({ name: { givenName: "Given", familyName: "Family" } });

    const response = await scim<ScimResource>(
      "GET",
      `/Users/${user.id}?excludedAttributes=name.givenName`,
    );

    expect(response.status).toBe(200);
    const name = response.body["name"] as Record<string, string> | undefined;
    expect(name?.["givenName"]).toBeUndefined();
    expect(name?.["familyName"]).toBe("Family");
  });

  it("excludes a sub-attribute inside a multi-valued attribute", async () => {
    const user = await createUser();

    const response = await scim<ScimResource>(
      "GET",
      `/Users/${user.id}?excludedAttributes=emails.type`,
    );

    expect(response.status).toBe(200);
    for (const email of (response.body["emails"] ?? []) as Record<string, unknown>[]) {
      expect(email["type"]).toBeUndefined();
    }
  });

  it("excludes several attributes at once", async () => {
    const user = await createUser();

    const response = await scim<ScimResource>(
      "GET",
      `/Users/${user.id}?excludedAttributes=title,name.givenName,emails`,
    );

    expect(response.status).toBe(200);
    expect(response.body["title"]).toBeUndefined();
    expect(response.body["emails"]).toBeUndefined();
  });

  it("requests a sub-attribute of a multi-valued attribute", async () => {
    const user = await createUser();

    const response = await scim<ScimResource>(
      "GET",
      `/Users/${user.id}?attributes=emails.value`,
    );

    expect(response.status).toBe(200);
    expect(response.body["title"]).toBeUndefined();
  });

  it("tolerates a blank entry in a projection list", async () => {
    const user = await createUser();

    const response = await scim<ScimResource>(
      "GET",
      `/Users/${user.id}?attributes=,userName&excludedAttributes=,title`,
    );

    expect(response.status).toBe(200);
    expect(response.body["userName"]).toBe(user.userName);
  });
});

describe("Bulk: a creation whose subordinate operations must complete first", () => {
  it("creates a group with several members named by bulkId", async () => {
    // A creation carrying members becomes a create plus a membership patch, and the
    // creation is not finished until that patch is. Several members means several
    // subordinate operations, all of which have to complete before the response for
    // the creation is written.
    const displayName = unique("bulkmulti");

    const response = await scim("POST", "/Bulk", {
      schemas: [SCHEMA_BULK_REQUEST],
      Operations: [
        {
          method: "POST",
          path: "/Groups",
          bulkId: "g1",
          data: {
            schemas: [SCHEMA_GROUP],
            displayName,
            members: [{ value: "bulkId:u1" }, { value: "bulkId:u2" }],
          },
        },
        {
          method: "POST",
          path: "/Users",
          bulkId: "u1",
          data: { schemas: [SCHEMA_USER], userName: `${unique("m1")}@example.sg`, active: true },
        },
        {
          method: "POST",
          path: "/Users",
          bulkId: "u2",
          data: { schemas: [SCHEMA_USER], userName: `${unique("m2")}@example.sg`, active: true },
        },
      ],
    });

    expect(response.status).toBe(200);

    const found = await scim(
      "GET",
      `/Groups?filter=${encodeURIComponent(`displayName eq "${displayName}"`)}`,
    );
    const group = (found.body.Resources as ScimResource[])[0];
    expect(group).toBeDefined();

    for (const member of await memberIds(group!.id)) {
      expect(member).not.toMatch(/^bulkId:/u);
    }
  });

  it("patches a group and a user in one request", async () => {
    const group = await createGroup();
    const user = await createUser();

    const response = await scim("POST", "/Bulk", {
      schemas: [SCHEMA_BULK_REQUEST],
      Operations: [
        {
          method: "PATCH",
          path: `/Users/${user.id}`,
          bulkId: "p1",
          data: {
            schemas: [SCHEMA_PATCH],
            Operations: [{ op: "replace", path: "title", value: "Bursar" }],
          },
        },
        {
          method: "PATCH",
          path: `/Groups/${group.id}`,
          bulkId: "p2",
          data: {
            schemas: [SCHEMA_PATCH],
            Operations: [{ op: "add", path: "members", value: [{ value: user.id }] }],
          },
        },
      ],
    });

    expect(response.status).toBe(200);
    expect((await scim<ScimResource>("GET", `/Users/${user.id}`)).body["title"]).toBe("Bursar");
    expect(await memberIds(group.id)).toContain(user.id);
  });
});
