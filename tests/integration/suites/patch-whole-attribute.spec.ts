import { describe, expect, it } from "vitest";
import { createGroup, createUser, patchOp, scim } from "../src/client.js";

/**
 * PATCH operations whose path names a whole attribute rather than one part of one.
 *
 * The sub-attribute patchers all begin by requiring a value path - `emails[type eq "work"]
 * .value` says which address to touch - and declined anything without one. So an operation
 * naming the collection itself either did nothing, or rebuilt each entry from its `value`
 * alone and dropped every other sub-attribute the client sent. Both answered success.
 *
 * Found by scim2-cli's compliance suite; see docs/scim-conformance.md section 7, oracle 7.
 */
const readUser = async (id: string) => (await scim("GET", `/Users/${id}`)).body;

describe("Replacing a whole multi-valued attribute keeps every sub-attribute", () => {
  it("carries value, type and primary across for emails", async () => {
    const created = await createUser();

    await scim(
      "PATCH",
      `/Users/${created.id}`,
      patchOp({
        op: "replace",
        path: "emails",
        value: [{ value: "kept@example.sg", type: "home", primary: true }],
      }),
    );

    expect((await readUser(created.id)).emails).toEqual([
      expect.objectContaining({ value: "kept@example.sg", type: "home", primary: true }),
    ]);
  });

  it("carries value, display, type and primary across for roles", async () => {
    const created = await createUser();

    await scim(
      "PATCH",
      `/Users/${created.id}`,
      patchOp({
        op: "replace",
        path: "roles",
        value: [{ value: "admin", display: "Administrator", type: "role", primary: true }],
      }),
    );

    expect((await readUser(created.id)).roles).toEqual([
      expect.objectContaining({
        value: "admin",
        display: "Administrator",
        type: "role",
        primary: true,
      }),
    ]);
  });

  it("applies to ims, which had no whole-collection branch at all", async () => {
    const created = await createUser();

    await scim(
      "PATCH",
      `/Users/${created.id}`,
      patchOp({ op: "replace", path: "ims", value: [{ value: "handle", type: "skype" }] }),
    );

    expect((await readUser(created.id)).ims).toEqual([
      expect.objectContaining({ value: "handle", type: "skype" }),
    ]);
  });

  it("applies to phoneNumbers, which had none either", async () => {
    const created = await createUser();

    await scim(
      "PATCH",
      `/Users/${created.id}`,
      patchOp({ op: "replace", path: "phoneNumbers", value: [{ value: "555", type: "mobile" }] }),
    );

    expect((await readUser(created.id)).phoneNumbers).toEqual([
      expect.objectContaining({ value: "555", type: "mobile" }),
    ]);
  });

  it("carries an address across, which has no value of its own", async () => {
    const created = await createUser();

    await scim(
      "PATCH",
      `/Users/${created.id}`,
      patchOp({
        op: "replace",
        path: "addresses",
        value: [{ type: "work", formatted: "1 Example Way", locality: "Singapore" }],
      }),
    );

    expect((await readUser(created.id)).addresses).toEqual([
      expect.objectContaining({
        type: "work",
        formatted: "1 Example Way",
        locality: "Singapore",
      }),
    ]);
  });
});

describe("A path naming a complex attribute carries its sub-attributes", () => {
  it("replaces name from an object rather than one sub-attribute at a time", async () => {
    const created = await createUser();

    await scim(
      "PATCH",
      `/Users/${created.id}`,
      patchOp({
        op: "replace",
        path: "name",
        value: { formatted: "Ada Lovelace", givenName: "Ada", familyName: "Lovelace" },
      }),
    );

    expect((await readUser(created.id)).name).toEqual(
      expect.objectContaining({
        formatted: "Ada Lovelace",
        givenName: "Ada",
        familyName: "Lovelace",
      }),
    );
  });

  it("removes name whole", async () => {
    const created = await createUser({ name: { givenName: "Ada", familyName: "Lovelace" } });

    await scim("PATCH", `/Users/${created.id}`, patchOp({ op: "remove", path: "name" }));

    expect((await readUser(created.id)).name).toBeUndefined();
  });
});

describe("A path naming a schema targets the extension it names", () => {
  const ENTERPRISE = "urn:ietf:params:scim:schemas:extension:enterprise:2.0:User";

  it("replaces the extension's attributes from one object", async () => {
    // Path.TryParse splits a bare schema URN at its last colon, so this could never be routed
    // by path alone and was answered 400 invalidPath.
    const created = await createUser();

    const response = await scim(
      "PATCH",
      `/Users/${created.id}`,
      patchOp({
        op: "replace",
        path: ENTERPRISE,
        value: { department: "Research", costCenter: "CC-1" },
      }),
    );

    expect([200, 204]).toContain(response.status);
    expect((await readUser(created.id))[ENTERPRISE]).toEqual(
      expect.objectContaining({ department: "Research", costCenter: "CC-1" }),
    );
  });

  it("ignores a schemas member inside the extension's value", async () => {
    // A client that serializes an extension whole sends "schemas" with it. That belongs to the
    // resource, not to the extension, and expanding it named an attribute no schema defines.
    const created = await createUser();

    const response = await scim(
      "PATCH",
      `/Users/${created.id}`,
      patchOp({
        op: "replace",
        path: ENTERPRISE,
        value: { schemas: [ENTERPRISE], department: "Research" },
      }),
    );

    expect([200, 204]).toContain(response.status);
    expect((await readUser(created.id))[ENTERPRISE]).toEqual(
      expect.objectContaining({ department: "Research" }),
    );
  });

  it("reaches a sub-attribute of a complex extension attribute", async () => {
    // "urn:...:User" + "manager" is a path; its sub-attribute is "manager.value", not
    // "manager:value". Joining with the schema separator twice produced an invalid path.
    const manager = await createUser();
    const created = await createUser();

    const response = await scim(
      "PATCH",
      `/Users/${created.id}`,
      patchOp({ op: "replace", path: ENTERPRISE, value: { manager: { value: manager.id } } }),
    );

    expect([200, 204]).toContain(response.status);
    expect((await readUser(created.id))[ENTERPRISE].manager.value).toBe(manager.id);
  });

  it("removes the extension whole", async () => {
    const created = await createUser();
    await scim(
      "PATCH",
      `/Users/${created.id}`,
      patchOp({ op: "replace", path: ENTERPRISE, value: { department: "Research" } }),
    );

    const response = await scim(
      "PATCH",
      `/Users/${created.id}`,
      patchOp({ op: "remove", path: ENTERPRISE }),
    );

    expect([200, 204]).toContain(response.status);
    expect((await readUser(created.id))[ENTERPRISE]?.department).toBeUndefined();
  });
});

describe("Removing an attribute that is not a string", () => {
  it("clears active", async () => {
    // active is a non-nullable bool on the resource, so cleared is false - which is also what
    // RFC 7643 4.1.1 makes an absent "active" mean. It used to be ignored outright.
    const created = await createUser({ active: true });

    await scim("PATCH", `/Users/${created.id}`, patchOp({ op: "remove", path: "active" }));

    expect((await readUser(created.id)).active).toBe(false);
  });

  it("empties a group's membership", async () => {
    const member = await createUser();
    const created = await createGroup({ members: [{ value: member.id }] });

    await scim("PATCH", `/Groups/${created.id}`, patchOp({ op: "remove", path: "members" }));

    expect((await scim("GET", `/Groups/${created.id}`)).body.members).toEqual([]);
  });
});

describe("attributes= reaches inside a multi-valued attribute", () => {
  it("returns only the sub-attributes asked for", async () => {
    // The projection pruned sub-attributes of a complex attribute but never of an array, so
    // attributes=members.value returned each membership whole, $ref included.
    const member = await createUser();
    const created = await createGroup({ members: [{ value: member.id }] });

    const response = await scim("GET", `/Groups/${created.id}?attributes=members.value`);

    expect(response.body.members).toEqual([{ value: member.id }]);
  });

  it("honours two sub-attributes of the same attribute", async () => {
    // Pruning once per path in turn left only whichever came last.
    const created = await createUser({
      emails: [{ value: "both@example.sg", type: "work", primary: true }],
    });

    const response = await scim(
      "GET",
      `/Users/${created.id}?attributes=emails.value&attributes=emails.type`,
    );

    expect(response.body.emails).toEqual([{ value: "both@example.sg", type: "work" }]);
  });
});

describe("A URL under the service root that names nothing", () => {
  it("is a 404, not a 501", async () => {
    // Resources live under /Users and /Groups. 501 said instead that the service root
    // retrieves by identifier but has not implemented it yet.
    const response = await scim("GET", "/9c4e2b1a-0000-0000-0000-000000000000");

    expect(response.status).toBe(404);
  });
});
