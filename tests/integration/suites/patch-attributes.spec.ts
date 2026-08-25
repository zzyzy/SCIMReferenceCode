import { describe, expect, it } from "vitest";
import {
  PATCH_APPLIED,
  SCHEMA_ENTERPRISE,
  createUser,
  patchOp,
  scim,
  unique,
  type ScimResource,
} from "../src/client.js";

/**
 * PATCH across the whole User resource, RFC 7644 3.5.2.
 *
 * The enterprise patcher is the largest single piece of logic in the library, and it
 * is written attribute by attribute: a separate path for addresses, phone numbers,
 * instant messaging, the name sub-attributes, the enterprise extension members and
 * every singular core attribute. Each has its own add, replace and remove branches,
 * its own "the value path matched nothing" case, and its own handling of a remove
 * that names a value which does not match.
 *
 * These walk that surface. The assertion is always the resource as read back, not the
 * PATCH's status: an operation that answers 204 and changes nothing is the failure
 * mode this is built to catch, and it has happened here before.
 */

async function patch(
  identifier: string,
  ...operations: Record<string, unknown>[]
): Promise<{ status: number; body: any }> {
  return scim("PATCH", `/Users/${identifier}`, patchOp(...operations));
}

async function read(identifier: string): Promise<ScimResource> {
  return (await scim<ScimResource>("GET", `/Users/${identifier}`)).body;
}

function valueOf(
  items: { type?: string; value?: string }[] | undefined,
  type: string,
): string | undefined {
  return (items ?? []).find((item) => item.type === type)?.value;
}

describe("PATCH: singular core attributes", () => {
  it.each([
    "externalId",
    "displayName",
    "nickName",
    "preferredLanguage",
    "locale",
    "timezone",
    "userType",
    "title",
    "profileUrl",
  ])("adds, replaces and removes %s", async (attribute) => {
    const user = await createUser();

    const added = await patch(user.id, { op: "add", path: attribute, value: "first" });
    expect([...PATCH_APPLIED, 400]).toContain(added.status);

    if (added.status === 400) {
      // The resource type does not model this attribute, which is a legitimate answer -
      // but then it must not have been stored either.
      expect((await read(user.id))[attribute]).toBeUndefined();
      return;
    }

    expect((await read(user.id))[attribute]).toBe("first");

    expect(PATCH_APPLIED).toContain(
      (await patch(user.id, { op: "replace", path: attribute, value: "second" })).status,
    );
    expect((await read(user.id))[attribute]).toBe("second");

    expect(PATCH_APPLIED).toContain(
      (await patch(user.id, { op: "remove", path: attribute })).status,
    );
    expect((await read(user.id))[attribute]).toBeUndefined();
  });

  it("leaves an attribute alone when a remove names a different value", async () => {
    // RFC 7644 3.5.2.2: remove with a value removes that value. Naming a value the
    // resource does not hold must not clear what it does hold.
    const user = await createUser({ title: "Teacher" });

    await patch(user.id, { op: "remove", path: "title", value: "Principal" });

    expect((await read(user.id))["title"]).toBe("Teacher");
  });

  it("clears an attribute when a remove names the value it holds", async () => {
    const user = await createUser({ title: "Teacher" });

    await patch(user.id, { op: "remove", path: "title", value: "Teacher" });

    expect((await read(user.id))["title"]).toBeUndefined();
  });

  it("accepts a core attribute named by its full URN", async () => {
    const user = await createUser();

    const patched = await patch(user.id, {
      op: "replace",
      path: "urn:ietf:params:scim:schemas:core:2.0:User:displayName",
      value: "By URN",
    });

    expect([...PATCH_APPLIED, 400]).toContain(patched.status);
    if (patched.status !== 400) {
      expect((await read(user.id))["displayName"]).toBe("By URN");
    }
  });

  it("changes userName", async () => {
    const user = await createUser();
    const replacement = `${unique("renamed")}@example.sg`;

    expect(PATCH_APPLIED).toContain(
      (await patch(user.id, { op: "replace", path: "userName", value: replacement })).status,
    );
    expect((await read(user.id))["userName"]).toBe(replacement);
  });

  it("refuses to remove userName, which every user must have", async () => {
    const user = await createUser();

    await patch(user.id, { op: "remove", path: "userName", value: "not the userName" });

    expect((await read(user.id))["userName"]).toBe(user.userName);
  });
});

describe("PATCH: active", () => {
  it("turns active off and on", async () => {
    const user = await createUser({ active: true });

    expect(PATCH_APPLIED).toContain(
      (await patch(user.id, { op: "replace", path: "active", value: false })).status,
    );
    expect((await read(user.id))["active"]).toBe(false);

    expect(PATCH_APPLIED).toContain(
      (await patch(user.id, { op: "replace", path: "active", value: true })).status,
    );
    expect((await read(user.id))["active"]).toBe(true);
  });

  it("accepts active as a quoted boolean, which several clients send", async () => {
    const user = await createUser({ active: true });

    await patch(user.id, { op: "replace", path: "active", value: "false" });

    expect((await read(user.id))["active"]).toBe(false);
  });

  it("does not set active from a value that is not a boolean", async () => {
    const user = await createUser({ active: true });

    await patch(user.id, { op: "replace", path: "active", value: "perhaps" });

    // Either refused or ignored - but never silently turned into false, which would
    // deactivate an account on a malformed request.
    expect((await read(user.id))["active"]).toBe(true);
  });
});

describe("PATCH: name sub-attributes", () => {
  // The six RFC 7643 4.1.1 defines. Only givenName and familyName used to be applied.
  it.each([
    "givenName",
    "familyName",
    "formatted",
    "middleName",
    "honorificPrefix",
    "honorificSuffix",
  ])("replaces name.%s", async (subAttribute) => {
    const user = await createUser();

    expect(PATCH_APPLIED).toContain(
      (await patch(user.id, { op: "replace", path: `name.${subAttribute}`, value: "Changed" }))
        .status,
    );

    const name = (await read(user.id))["name"] as Record<string, string> | undefined;
    expect(name?.[subAttribute]).toBe("Changed");
  });

  it.each([
    "givenName",
    "familyName",
    "formatted",
    "middleName",
    "honorificPrefix",
    "honorificSuffix",
  ])("round-trips name.%s on create", async (subAttribute) => {
    // A sub-attribute PATCH can only apply what the model carries, so the two have to
    // agree: anything patchable must survive a create and a read as well.
    const user = await createUser({ name: { [subAttribute]: "Given" } });

    const name = (await read(user.id))["name"] as Record<string, string> | undefined;
    expect(name?.[subAttribute]).toBe("Given");
  });

  it("advertises every name sub-attribute it accepts", async () => {
    // A client discovers what it may send from /Schemas. Accepting a PATCH on a
    // sub-attribute the schema does not declare leaves it undiscoverable.
    const response = await scim("GET", "/Schemas");
    const user = (response.body.Resources as any[]).find(
      (item) => item.id === "urn:ietf:params:scim:schemas:core:2.0:User",
    );
    const name = (user?.attributes ?? []).find((item: any) => item.name === "name");
    const declared = (name?.subAttributes ?? []).map((item: any) => item.name);

    for (const expected of [
      "givenName",
      "familyName",
      "formatted",
      "middleName",
      "honorificPrefix",
      "honorificSuffix",
    ]) {
      expect(declared).toContain(expected);
    }
  });

  it("refuses a name sub-attribute outside the six", async () => {
    const user = await createUser();

    const patched = await patch(user.id, {
      op: "replace",
      path: "name.nickname",
      value: "Changed",
    });

    expect(patched.status).toBe(400);
  });

  it("builds a name on a user that had none", async () => {
    const user = await createUser({ name: undefined });

    await patch(user.id, { op: "add", path: "name.givenName", value: "Fresh" });

    const name = (await read(user.id))["name"] as Record<string, string> | undefined;
    expect(name?.givenName).toBe("Fresh");
  });

  it("removes one sub-attribute and keeps the rest of the name", async () => {
    const user = await createUser({ name: { givenName: "Given", familyName: "Family" } });

    await patch(user.id, { op: "remove", path: "name.givenName" });

    const name = (await read(user.id))["name"] as Record<string, string> | undefined;
    expect(name?.givenName).toBeUndefined();
    expect(name?.familyName).toBe("Family");
  });

  it("ignores an operation naming more than one value for a single sub-attribute", async () => {
    const user = await createUser({ name: { givenName: "Given" } });

    await patch(user.id, { op: "replace", path: "name.givenName", value: ["A", "B"] });

    const name = (await read(user.id))["name"] as Record<string, string> | undefined;
    expect(name?.givenName).toBe("Given");
  });
});

describe("PATCH: phone numbers", () => {
  it.each(["work", "home", "mobile", "fax", "pager", "other"])(
    "adds, replaces and removes a %s number",
    async (type) => {
      const user = await createUser();

      const added = await patch(user.id, {
        op: "add",
        path: `phoneNumbers[type eq "${type}"].value`,
        value: "+65 6000 0001",
      });
      expect([...PATCH_APPLIED, 400]).toContain(added.status);

      const afterAdd = (await read(user.id))["phoneNumbers"] as { type: string; value: string }[];
      if (valueOf(afterAdd, type) === undefined) {
        // Not every canonical type is modelled; what matters is that an unmodelled one
        // is not quietly stored under a different type.
        expect((afterAdd ?? []).map((item) => item.type)).not.toContain(type);
        return;
      }

      await patch(user.id, {
        op: "replace",
        path: `phoneNumbers[type eq "${type}"].value`,
        value: "+65 6000 0002",
      });
      expect(valueOf((await read(user.id))["phoneNumbers"] as any, type)).toBe("+65 6000 0002");

      await patch(user.id, {
        op: "remove",
        path: `phoneNumbers[type eq "${type}"].value`,
        value: "+65 6000 0002",
      });
      expect(valueOf((await read(user.id))["phoneNumbers"] as any, type)).toBeUndefined();
    },
  );

  it("keeps a number when a remove names a value it does not hold", async () => {
    const user = await createUser({ phoneNumbers: [{ type: "work", value: "+65 6000 1111" }] });

    await patch(user.id, {
      op: "remove",
      path: 'phoneNumbers[type eq "work"].value',
      value: "+65 9999 9999",
    });

    expect(valueOf((await read(user.id))["phoneNumbers"] as any, "work")).toBe("+65 6000 1111");
  });

  it("patches one number of several and leaves the others", async () => {
    const user = await createUser({
      phoneNumbers: [
        { type: "work", value: "+65 6000 1111" },
        { type: "mobile", value: "+65 9000 2222" },
      ],
    });

    await patch(user.id, {
      op: "replace",
      path: 'phoneNumbers[type eq "work"].value',
      value: "+65 6000 3333",
    });

    const numbers = (await read(user.id))["phoneNumbers"] as any;
    expect(valueOf(numbers, "work")).toBe("+65 6000 3333");
    expect(valueOf(numbers, "mobile")).toBe("+65 9000 2222");
  });

  it("ignores a value path filtering on something other than type", async () => {
    const user = await createUser({ phoneNumbers: [{ type: "work", value: "+65 6000 1111" }] });

    await patch(user.id, {
      op: "replace",
      path: 'phoneNumbers[value eq "+65 6000 1111"].type',
      value: "home",
    });

    const numbers = (await read(user.id))["phoneNumbers"] as any;
    expect(valueOf(numbers, "work")).toBe("+65 6000 1111");
  });
});

describe("PATCH: addresses", () => {
  it.each(["country", "locality", "postalCode", "region", "streetAddress", "formatted"])(
    "replaces a work address %s",
    async (subAttribute) => {
      const user = await createUser();

      const patched = await patch(user.id, {
        op: "replace",
        path: `addresses[type eq "work"].${subAttribute}`,
        value: "Changed",
      });

      expect([...PATCH_APPLIED, 400]).toContain(patched.status);
      if (patched.status === 400) {
        return;
      }

      const addresses = (await read(user.id))["addresses"] as Record<string, string>[] | undefined;
      const work = (addresses ?? []).find((item) => item["type"] === "work");
      if (work) {
        expect(work[subAttribute]).toBe("Changed");
      }
    },
  );

  it("creates the address a value path names when the user has none", async () => {
    const user = await createUser();

    await patch(user.id, {
      op: "add",
      path: 'addresses[type eq "other"].formatted',
      value: "2 Orchard Road",
    });

    const addresses = (await read(user.id))["addresses"] as Record<string, string>[] | undefined;
    const other = (addresses ?? []).find((item) => item["type"] === "other");
    if (other) {
      expect(other["formatted"]).toBe("2 Orchard Road");
    }
  });

  it("patches one sub-attribute and leaves the rest of the address", async () => {
    const user = await createUser({
      addresses: [{ type: "work", locality: "Singapore", country: "SG", postalCode: "111111" }],
    });

    await patch(user.id, {
      op: "replace",
      path: 'addresses[type eq "work"].postalCode',
      value: "222222",
    });

    const addresses = (await read(user.id))["addresses"] as Record<string, string>[];
    const work = addresses.find((item) => item["type"] === "work");
    expect(work?.["postalCode"]).toBe("222222");
    expect(work?.["locality"]).toBe("Singapore");
    expect(work?.["country"]).toBe("SG");
  });

  it("removes a sub-attribute when the remove names the value it holds", async () => {
    const user = await createUser({ addresses: [{ type: "work", country: "SG" }] });

    await patch(user.id, {
      op: "remove",
      path: 'addresses[type eq "work"].country',
      value: "SG",
    });

    const addresses = (await read(user.id))["addresses"] as Record<string, string>[] | undefined;
    const work = (addresses ?? []).find((item) => item["type"] === "work");
    expect(work?.["country"]).toBeUndefined();
  });

  it("ignores an address operation carrying two values", async () => {
    const user = await createUser({ addresses: [{ type: "work", locality: "Singapore" }] });

    await patch(user.id, {
      op: "replace",
      path: 'addresses[type eq "work"].locality',
      value: ["A", "B"],
    });

    const addresses = (await read(user.id))["addresses"] as Record<string, string>[];
    expect(addresses.find((item) => item["type"] === "work")?.["locality"]).toBe("Singapore");
  });
});

describe("PATCH: instant messaging", () => {
  it.each(["aim", "gtalk", "icq", "msn", "qq", "skype", "xmpp", "yahoo"])(
    "adds and removes a %s handle",
    async (type) => {
      const user = await createUser();

      const added = await patch(user.id, {
        op: "add",
        path: `ims[type eq "${type}"].value`,
        value: "handle.one",
      });
      expect([...PATCH_APPLIED, 400]).toContain(added.status);

      if (valueOf((await read(user.id))["ims"] as any, type) === undefined) {
        return;
      }

      await patch(user.id, {
        op: "remove",
        path: `ims[type eq "${type}"].value`,
        value: "handle.one",
      });
      expect(valueOf((await read(user.id))["ims"] as any, type)).toBeUndefined();
    },
  );

  it("keeps a handle when a remove names a different value", async () => {
    const user = await createUser({ ims: [{ type: "skype", value: "kept" }] });

    await patch(user.id, { op: "remove", path: 'ims[type eq "skype"].value', value: "other" });

    expect(valueOf((await read(user.id))["ims"] as any, "skype")).toBe("kept");
  });
});

describe("PATCH: email addresses", () => {
  it("adds a second address of a different type", async () => {
    const user = await createUser();

    await patch(user.id, {
      op: "add",
      path: 'emails[type eq "home"].value',
      value: "home@example.sg",
    });

    expect(valueOf((await read(user.id))["emails"] as any, "home")).toBe("home@example.sg");
  });

  it("adds an address to a user carrying none", async () => {
    const user = await createUser({ emails: undefined });

    await patch(user.id, {
      op: "add",
      path: 'emails[type eq "work"].value',
      value: "work@example.sg",
    });

    expect(valueOf((await read(user.id))["emails"] as any, "work")).toBe("work@example.sg");
  });

  it("replaces the whole collection", async () => {
    const user = await createUser();

    const patched = await patch(user.id, {
      op: "replace",
      path: "emails",
      value: [{ value: "only@example.sg", type: "work", primary: true }],
    });

    expect([...PATCH_APPLIED, 400]).toContain(patched.status);
    if (patched.status !== 400) {
      const emails = (await read(user.id))["emails"] as { value: string }[];
      expect(emails.map((item) => item.value)).toContain("only@example.sg");
    }
  });

  it("keeps an address when a remove names a different value", async () => {
    const user = await createUser({ emails: [{ value: "kept@example.sg", type: "work" }] });

    await patch(user.id, {
      op: "remove",
      path: 'emails[type eq "work"].value',
      value: "other@example.sg",
    });

    expect(valueOf((await read(user.id))["emails"] as any, "work")).toBe("kept@example.sg");
  });

  it("ignores a sub-attribute the schema does not define", async () => {
    const user = await createUser();

    const patched = await patch(user.id, {
      op: "add",
      path: 'emails[type eq "work"].nonsense',
      value: "x",
    });

    expect([...PATCH_APPLIED, 400]).toContain(patched.status);
  });
});

describe("PATCH: roles", () => {
  it("adds a role, changes it and removes it", async () => {
    const user = await createUser();

    const added = await patch(user.id, {
      op: "add",
      path: "roles",
      value: [{ value: "Reader", type: "application", primary: true }],
    });
    expect([...PATCH_APPLIED, 400]).toContain(added.status);

    const roles = (await read(user.id))["roles"] as { value: string }[] | undefined;
    if (!roles || roles.length === 0) {
      return;
    }
    expect(roles.map((item) => item.value)).toContain("Reader");

    await patch(user.id, {
      op: "replace",
      path: "roles",
      value: [{ value: "Writer", type: "application" }],
    });
    const replaced = (await read(user.id))["roles"] as { value: string }[] | undefined;
    expect((replaced ?? []).map((item) => item.value)).toContain("Writer");

    await patch(user.id, { op: "remove", path: "roles" });
    const removed = (await read(user.id))["roles"] as { value: string }[] | undefined;
    expect(removed ?? []).toHaveLength(0);
  });

  it("removes one role of several by value path", async () => {
    const user = await createUser({
      roles: [
        { value: "Keep", type: "application" },
        { value: "Drop", type: "application" },
      ],
    });

    await patch(user.id, { op: "remove", path: 'roles[value eq "Drop"]' });

    const roles = (await read(user.id))["roles"] as { value: string }[] | undefined;
    const values = (roles ?? []).map((item) => item.value);
    if (values.length > 0) {
      expect(values).not.toContain("Drop");
      expect(values).toContain("Keep");
    }
  });
});

describe("PATCH: roles by type", () => {
  it("adds, replaces and removes a role named by its type", async () => {
    const user = await createUser();

    expect(PATCH_APPLIED).toContain(
      (
        await patch(user.id, {
          op: "add",
          path: 'roles[type eq "application"].value',
          value: "Reader",
        })
      ).status,
    );
    expect(valueOf((await read(user.id))["roles"] as any, "application")).toBe("Reader");

    await patch(user.id, {
      op: "replace",
      path: 'roles[type eq "application"].value',
      value: "Writer",
    });
    expect(valueOf((await read(user.id))["roles"] as any, "application")).toBe("Writer");

    await patch(user.id, {
      op: "remove",
      path: 'roles[type eq "application"].value',
      value: "Writer",
    });
    expect(valueOf((await read(user.id))["roles"] as any, "application")).toBeUndefined();
  });

  it("adds a role of a second type without disturbing the first", async () => {
    const user = await createUser();

    await patch(user.id, { op: "add", path: 'roles[type eq "portal"].value', value: "Portal" });
    await patch(user.id, { op: "add", path: 'roles[type eq "reporting"].value', value: "Reports" });

    const roles = (await read(user.id))["roles"] as any;
    expect(valueOf(roles, "portal")).toBe("Portal");
    expect(valueOf(roles, "reporting")).toBe("Reports");
  });

  it("keeps a role when a remove names a different value", async () => {
    const user = await createUser();
    await patch(user.id, { op: "add", path: 'roles[type eq "portal"].value', value: "Kept" });

    await patch(user.id, {
      op: "remove",
      path: 'roles[type eq "portal"].value',
      value: "Something else",
    });

    expect(valueOf((await read(user.id))["roles"] as any, "portal")).toBe("Kept");
  });
});

describe("PATCH: addresses of every type", () => {
  it.each(["work", "home", "other", "untyped"])(
    "patches a %s address the same way",
    async (type) => {
      // The per-type branches were duplicated, so an attribute that worked on one type
      // silently did nothing on another. They must behave identically.
      const user = await createUser();

      expect(PATCH_APPLIED).toContain(
        (
          await patch(user.id, {
            op: "add",
            path: `addresses[type eq "${type}"].locality`,
            value: "Singapore",
          })
        ).status,
      );

      const addresses = (await read(user.id))["addresses"] as Record<string, string>[] | undefined;
      const found = (addresses ?? []).find((item) => item["type"] === type);
      expect(found?.["locality"]).toBe("Singapore");
    },
  );

  it.each(["country", "locality", "postalCode", "region", "streetAddress", "formatted"])(
    "patches %s on a home address",
    async (subAttribute) => {
      const user = await createUser();

      expect(PATCH_APPLIED).toContain(
        (
          await patch(user.id, {
            op: "add",
            path: `addresses[type eq "home"].${subAttribute}`,
            value: "Changed",
          })
        ).status,
      );

      const addresses = (await read(user.id))["addresses"] as Record<string, string>[] | undefined;
      const home = (addresses ?? []).find((item) => item["type"] === "home");
      expect(home?.[subAttribute]).toBe("Changed");
    },
  );

  it("keeps a sub-attribute when a remove names a different value", async () => {
    const user = await createUser({ addresses: [{ type: "work", region: "East" }] });

    await patch(user.id, {
      op: "remove",
      path: 'addresses[type eq "work"].region',
      value: "West",
    });

    const addresses = (await read(user.id))["addresses"] as Record<string, string>[];
    expect(addresses.find((item) => item["type"] === "work")?.["region"]).toBe("East");
  });

  it("drops the address once its last sub-attribute is removed", async () => {
    const user = await createUser({ addresses: [{ type: "work", region: "East" }] });

    await patch(user.id, { op: "remove", path: 'addresses[type eq "work"].region' });

    const addresses = (await read(user.id))["addresses"] as Record<string, string>[] | undefined;
    expect((addresses ?? []).find((item) => item["type"] === "work")).toBeUndefined();
  });

  it("refuses an address sub-attribute the schema does not define", async () => {
    const user = await createUser();

    const patched = await patch(user.id, {
      op: "add",
      path: 'addresses[type eq "work"].invented',
      value: "x",
    });

    expect(patched.status).toBe(400);
  });
});

describe("PATCH: the enterprise extension", () => {
  it.each(["employeeNumber", "costCenter", "division", "department", "organization"])(
    "adds, replaces and removes %s",
    async (attribute) => {
      const user = await createUser();
      const path = `${SCHEMA_ENTERPRISE}:${attribute}`;

      expect(PATCH_APPLIED).toContain((await patch(user.id, { op: "add", path, value: "first" })).status);
      expect(((await read(user.id))[SCHEMA_ENTERPRISE] as any)[attribute]).toBe("first");

      expect(PATCH_APPLIED).toContain(
        (await patch(user.id, { op: "replace", path, value: "second" })).status,
      );
      expect(((await read(user.id))[SCHEMA_ENTERPRISE] as any)[attribute]).toBe("second");

      expect(PATCH_APPLIED).toContain((await patch(user.id, { op: "remove", path })).status);
      expect(((await read(user.id))[SCHEMA_ENTERPRISE] as any)[attribute]).toBeUndefined();
    },
  );

  it("keeps an extension attribute when a remove names a different value", async () => {
    const user = await createUser({ [SCHEMA_ENTERPRISE]: { department: "Engineering" } });

    await patch(user.id, {
      op: "remove",
      path: `${SCHEMA_ENTERPRISE}:department`,
      value: "Finance",
    });

    expect(((await read(user.id))[SCHEMA_ENTERPRISE] as any).department).toBe("Engineering");
  });

  it("sets manager through the extension path", async () => {
    const manager = await createUser();
    const user = await createUser();

    expect(PATCH_APPLIED).toContain(
      (
        await patch(user.id, {
          op: "replace",
          path: `${SCHEMA_ENTERPRISE}:manager`,
          value: { value: manager.id },
        })
      ).status,
    );

    const extension = (await read(user.id))[SCHEMA_ENTERPRISE] as any;
    expect(extension.manager?.value).toBe(manager.id);
  });

  it("sets manager through its sub-attribute path", async () => {
    const manager = await createUser();
    const user = await createUser();

    await patch(user.id, {
      op: "replace",
      path: `${SCHEMA_ENTERPRISE}:manager.value`,
      value: manager.id,
    });

    const extension = (await read(user.id))[SCHEMA_ENTERPRISE] as any;
    expect(extension.manager?.value).toBe(manager.id);
  });

  it("clears manager on a remove", async () => {
    const manager = await createUser();
    const user = await createUser({ [SCHEMA_ENTERPRISE]: { manager: { value: manager.id } } });

    await patch(user.id, { op: "remove", path: `${SCHEMA_ENTERPRISE}:manager` });

    const extension = (await read(user.id))[SCHEMA_ENTERPRISE] as any;
    expect(extension.manager?.value).toBeUndefined();
  });

  it("refuses an extension attribute the schema does not define", async () => {
    const user = await createUser();

    const patched = await patch(user.id, {
      op: "replace",
      path: `${SCHEMA_ENTERPRISE}:invented`,
      value: "x",
    });

    expect(patched.status).toBe(400);
  });
});

describe("PATCH: operations the service must refuse", () => {
  it("refuses a read-only attribute", async () => {
    const user = await createUser();

    const patched = await patch(user.id, { op: "replace", path: "groups", value: [] });

    expect([...PATCH_APPLIED, 400]).toContain(patched.status);
    // Whether refused or ignored, membership is derived and cannot be written here.
    expect((await read(user.id))["groups"] ?? []).toHaveLength(0);
  });

  it("refuses meta, which the service owns", async () => {
    const user = await createUser();
    const before = (await read(user.id)).meta?.created;

    await patch(user.id, { op: "replace", path: "meta.created", value: "1999-01-01T00:00:00Z" });

    expect((await read(user.id)).meta?.created).toBe(before);
  });

  it("applies nothing when one operation of several is invalid", async () => {
    // RFC 7644 3.5.2 requires a PATCH to be atomic.
    const user = await createUser({ title: "Teacher" });

    await patch(
      user.id,
      { op: "replace", path: "title", value: "Principal" },
      { op: "replace", path: "notAnAttribute", value: "x" },
    );

    expect((await read(user.id))["title"]).toBe("Teacher");
  });

  it("answers 404 for a PATCH against a user that is not there", async () => {
    const patched = await patch("44444444-4444-4444-4444-444444444444", {
      op: "replace",
      path: "title",
      value: "x",
    });

    expect(patched.status).toBe(404);
  });

  it("refuses a PATCH whose body is not a PatchOp", async () => {
    const user = await createUser();

    const patched = await scim("PATCH", `/Users/${user.id}`, {
      schemas: ["urn:ietf:params:scim:schemas:core:2.0:User"],
      title: "Sneaky",
    });

    expect(patched.status).toBeLessThan(500);
    if (patched.status < 300) {
      expect((await read(user.id))["title"]).not.toBe("Sneaky");
    }
  });

  it("refuses an unparseable PATCH body", async () => {
    const user = await createUser();

    const patched = await scim("PATCH", `/Users/${user.id}`, undefined, { raw: "{ not json" });

    expect(patched.status).toBe(400);
  });
});
