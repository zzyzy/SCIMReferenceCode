import { describe, expect, it } from "vitest";
import {
  PATCH_APPLIED,
  SCHEMA_ENTERPRISE,
  SCHEMA_USER,
  createUser,
  patchOp,
  readUser,
  scim,
  unique,
} from "../src/client.js";

function withExtension(extension: Record<string, unknown>): Record<string, unknown> {
  const userName = `${unique("ent")}@example.sg`;
  return {
    schemas: [SCHEMA_USER, SCHEMA_ENTERPRISE],
    userName,
    active: true,
    name: { givenName: "E", familyName: "X" },
    [SCHEMA_ENTERPRISE]: extension,
  };
}

function extensionOf(resource: Record<string, unknown>): Record<string, unknown> {
  return (resource[SCHEMA_ENTERPRISE] as Record<string, unknown> | undefined) ?? {};
}

describe("Enterprise extension", () => {
  it("round-trips on create and declares its URN", async () => {
    const response = await scim(
      "POST",
      "/Users",
      withExtension({
        employeeNumber: "E1001",
        department: "Engineering",
        costCenter: "CC-7",
        division: "Platform",
        organization: "Acme",
        manager: { value: "mgr-1", displayName: "The Manager" },
      }),
    );

    expect(response.status).toBe(201);
    expect(response.body.schemas).toContain(SCHEMA_ENTERPRISE);

    const extension = extensionOf(response.body);
    expect(extension["employeeNumber"]).toBe("E1001");
    expect(extension["department"]).toBe("Engineering");
    // RFC 7643 4.3: manager is complex, not a bare string.
    expect(extension["manager"]).toMatchObject({ value: "mgr-1" });
  });

  it.each([
    ["department", "Finance"],
    ["costCenter", "CC-9"],
    ["division", "Retail"],
    ["organization", "Globex"],
    ["employeeNumber", "E2002"],
  ])("patches %s", async (attribute, value) => {
    const created = (await scim("POST", "/Users", withExtension({ department: "Start" }))).body;
    const response = await scim(
      "PATCH",
      `/Users/${created.id}`,
      patchOp({ op: "replace", path: `${SCHEMA_ENTERPRISE}:${attribute}`, value }),
    );

    expect(PATCH_APPLIED).toContain(response.status);
    expect(extensionOf(await readUser(created.id))[attribute]).toBe(value);
  });

  it("patches manager with a complex value", async () => {
    // Regression: an operation value arrives as an array of complex values, one
    // complex value, or a bare scalar, and only the first and last were read. A
    // single complex value - what replacing manager carries - failed to
    // deserialize and produced a value whose own value was null, so manager was
    // emptied instead of set.
    const created = (await scim("POST", "/Users", withExtension({ manager: { value: "mgr-1" } })))
      .body;

    const response = await scim(
      "PATCH",
      `/Users/${created.id}`,
      patchOp({
        op: "replace",
        path: `${SCHEMA_ENTERPRISE}:manager`,
        value: { value: "mgr-2", displayName: "Another" },
      }),
    );

    expect(PATCH_APPLIED).toContain(response.status);
    expect(extensionOf(await readUser(created.id))["manager"]).toMatchObject({ value: "mgr-2" });
  });

  it("patches manager through its sub-attribute path", async () => {
    const created = (await scim("POST", "/Users", withExtension({ manager: { value: "mgr-1" } })))
      .body;
    const response = await scim(
      "PATCH",
      `/Users/${created.id}`,
      patchOp({ op: "replace", path: `${SCHEMA_ENTERPRISE}:manager.value`, value: "mgr-3" }),
    );

    expect(PATCH_APPLIED).toContain(response.status);
    expect(extensionOf(await readUser(created.id))["manager"]).toMatchObject({ value: "mgr-3" });
  });

  it("clears an extension attribute on a valueless remove", async () => {
    const created = (await scim("POST", "/Users", withExtension({ department: "Engineering" })))
      .body;
    const response = await scim(
      "PATCH",
      `/Users/${created.id}`,
      patchOp({ op: "remove", path: `${SCHEMA_ENTERPRISE}:department` }),
    );

    expect(PATCH_APPLIED).toContain(response.status);
    expect(extensionOf(await readUser(created.id))["department"]).toBeFalsy();
  });

  it("replaces the extension wholesale on PUT", async () => {
    const created = (await scim(
      "POST",
      "/Users",
      withExtension({ employeeNumber: "E1", costCenter: "CC-1" }),
    )).body;

    const response = await scim("PUT", `/Users/${created.id}`, {
      schemas: [SCHEMA_USER, SCHEMA_ENTERPRISE],
      id: created.id,
      userName: created.userName,
      active: true,
      [SCHEMA_ENTERPRISE]: { employeeNumber: "E2" },
    });

    expect(response.status).toBe(200);
    const extension = extensionOf(response.body);
    expect(extension["employeeNumber"]).toBe("E2");
    expect(extension["costCenter"]).toBeFalsy();
  });
});

describe("Complex and multi-valued attributes", () => {
  it("patches an address sub-attribute and leaves the rest of the address", async () => {
    const created = await createUser({
      addresses: [
        { type: "work", streetAddress: "1 Road", locality: "Town", postalCode: "12345" },
      ],
    });

    const response = await scim(
      "PATCH",
      `/Users/${created.id}`,
      patchOp({ op: "replace", path: 'addresses[type eq "work"].locality', value: "City" }),
    );

    expect(PATCH_APPLIED).toContain(response.status);
    const address = ((await readUser(created.id)).addresses as Record<string, unknown>[])[0];
    expect(address?.["locality"]).toBe("City");
    expect(address?.["streetAddress"]).toBe("1 Road");
  });

  it("patches one email of several by type", async () => {
    const created = await createUser({
      emails: [
        { value: "work@x.sg", type: "work", primary: true },
        { value: "home@x.sg", type: "home" },
      ],
    });

    const response = await scim(
      "PATCH",
      `/Users/${created.id}`,
      patchOp({ op: "replace", path: 'emails[type eq "work"].value', value: "new@x.sg" }),
    );

    expect(PATCH_APPLIED).toContain(response.status);
    const emails = (await readUser(created.id)).emails as Record<string, string>[];
    const byType = Object.fromEntries(emails.map((mail) => [mail["type"], mail["value"]]));
    expect(byType["work"]).toBe("new@x.sg");
    expect(byType["home"]).toBe("home@x.sg");
  });

  it("removes one email by type without a value", async () => {
    // Regression: the guard demanded exactly one value, and an omitted value is an
    // empty collection - so a remove naming only a path did nothing.
    const created = await createUser({
      emails: [
        { value: "work@x.sg", type: "work" },
        { value: "home@x.sg", type: "home" },
      ],
    });

    const response = await scim(
      "PATCH",
      `/Users/${created.id}`,
      patchOp({ op: "remove", path: 'emails[type eq "work"].value' }),
    );

    expect(PATCH_APPLIED).toContain(response.status);
    const emails = (await readUser(created.id)).emails as Record<string, string>[];
    expect(emails.map((mail) => mail["type"])).toEqual(["home"]);
  });

  it("invents nothing when a value path matches no entry", async () => {
    const created = await createUser({ emails: [{ value: "a@x.sg", type: "work" }] });
    const response = await scim(
      "PATCH",
      `/Users/${created.id}`,
      patchOp({ op: "replace", path: 'emails[type eq "nosuchtype"].value', value: "x@x.sg" }),
    );

    expect([...PATCH_APPLIED, 400]).toContain(response.status);
    expect((await readUser(created.id)).emails).toHaveLength(1);
  });

  it("adds and removes a whole role entry", async () => {
    // Regression: roles could only be reached through a sub-attribute path, so
    // adding a role or removing one by filter answered success and changed nothing.
    const created = await createUser({ roles: [{ value: "admin", type: "role" }] });

    const added = await scim(
      "PATCH",
      `/Users/${created.id}`,
      patchOp({ op: "add", path: "roles", value: [{ value: "auditor", type: "role" }] }),
    );
    expect(PATCH_APPLIED).toContain(added.status);
    let roles = ((await readUser(created.id)).roles as Record<string, string>[]).map(
      (role) => role["value"],
    );
    expect(roles).toContain("auditor");
    expect(roles).toContain("admin");

    const removed = await scim(
      "PATCH",
      `/Users/${created.id}`,
      patchOp({ op: "remove", path: 'roles[value eq "admin"]' }),
    );
    expect(PATCH_APPLIED).toContain(removed.status);
    roles = ((await readUser(created.id)).roles as Record<string, string>[]).map(
      (role) => role["value"],
    );
    expect(roles).not.toContain("admin");
  });

  it("patches ims, which the patcher used to reject outright", async () => {
    const created = await createUser();

    const added = await scim(
      "PATCH",
      `/Users/${created.id}`,
      patchOp({ op: "add", path: 'ims[type eq "skype"].value', value: "handle" }),
    );
    expect(PATCH_APPLIED).toContain(added.status);

    const ims = (await readUser(created.id)).ims as Record<string, string>[];
    expect(ims.find((entry) => entry["type"] === "skype")?.["value"]).toBe("handle");
  });

  it("accepts an ims type outside the canonical list", async () => {
    // RFC 7643 7 makes canonical values a recommendation. Refusing an unlisted one
    // would mean discarding the operation silently.
    const created = await createUser();
    const response = await scim(
      "PATCH",
      `/Users/${created.id}`,
      patchOp({ op: "add", path: 'ims[type eq "teams"].value', value: "handle" }),
    );

    expect(PATCH_APPLIED).toContain(response.status);
    const ims = (await readUser(created.id)).ims as Record<string, string>[];
    expect(ims.find((entry) => entry["type"] === "teams")?.["value"]).toBe("handle");
  });

  it("removes a name sub-attribute", async () => {
    const created = await createUser();
    const response = await scim(
      "PATCH",
      `/Users/${created.id}`,
      patchOp({ op: "remove", path: "name.givenName" }),
    );

    expect(PATCH_APPLIED).toContain(response.status);
    expect((await readUser(created.id)).name).not.toHaveProperty("givenName");
  });

  it("refuses two primary values in one multi-valued attribute", async () => {
    // RFC 7643 2.4 allows primary no more than once. Two claims leave every
    // consumer to pick one arbitrarily, and two consumers need not pick alike.
    const response = await scim("POST", "/Users", {
      schemas: [SCHEMA_USER],
      userName: `${unique("primary")}@example.sg`,
      active: true,
      emails: [
        { value: "a@x.sg", type: "work", primary: true },
        { value: "b@x.sg", type: "home", primary: true },
      ],
    });

    expect(response.status).toBe(400);
    expect(response.body.scimType).toBe("invalidValue");
  });
});
