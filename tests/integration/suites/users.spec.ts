import { describe, expect, it } from "vitest";
import {
  PATCH_APPLIED,
  SCHEMA_ERROR,
  SCHEMA_LIST,
  SCHEMA_USER,
  createUser,
  filterQuery,
  patchOp,
  readUser,
  scim,
  unique,
  userBody,
} from "../src/client.js";

describe("Users: create", () => {
  it("answers 201 with a server-assigned id, a Location header and meta", async () => {
    const body = userBody();
    const response = await scim("POST", "/Users", body);

    expect(response.status).toBe(201);
    expect(response.body.id).toBeTruthy();
    expect(response.body.userName).toBe(body["userName"]);
    expect(response.headers.get("Content-Type")).toContain("application/scim+json");
    expect(response.headers.get("Location")).toContain(`/Users/${response.body.id}`);
    expect(response.body.meta).toMatchObject({ resourceType: "User" });
  });

  it("refuses a duplicate userName with 409", async () => {
    const created = await createUser();
    const again = await scim("POST", "/Users", {
      schemas: [SCHEMA_USER],
      userName: created.userName,
      active: true,
    });

    expect(again.status).toBe(409);
  });

  it("refuses a body with no userName", async () => {
    const response = await scim("POST", "/Users", { schemas: [SCHEMA_USER], active: true });
    expect(response.status).toBe(400);
  });

  it("refuses a client-supplied id, which the server assigns", async () => {
    const response = await scim("POST", "/Users", userBody({ id: "client-chosen" }));
    expect(response.status).toBe(400);
  });
});

describe("Users: read", () => {
  it("returns the resource for a known id", async () => {
    const created = await createUser();
    const response = await scim("GET", `/Users/${created.id}`);

    expect(response.status).toBe(200);
    expect(response.body.id).toBe(created.id);
  });

  it("answers 404 with a SCIM error body for an unknown id", async () => {
    const response = await scim("GET", `/Users/${unique("ghost")}`);

    expect(response.status).toBe(404);
    expect(response.body.schemas).toContain(SCHEMA_ERROR);
    expect(String(response.body.status)).toBe("404");
  });

  it("returns a ListResponse envelope for the collection", async () => {
    await createUser();
    const response = await scim("GET", "/Users");

    expect(response.status).toBe(200);
    expect(response.body.schemas).toContain(SCHEMA_LIST);
    expect(response.body).toHaveProperty("totalResults");
    expect(Array.isArray(response.body.Resources)).toBe(true);
  });
});

describe("Users: replace", () => {
  it("answers 200 and applies the replacement", async () => {
    const created = await createUser({ title: "Teacher" });
    const response = await scim("PUT", `/Users/${created.id}`, {
      schemas: [SCHEMA_USER],
      id: created.id,
      userName: created.userName,
      active: true,
      displayName: "Replaced",
    });

    expect(response.status).toBe(200);
    expect(response.body.displayName).toBe("Replaced");
  });

  it("clears an attribute the replacement body omits", async () => {
    const created = await createUser({ title: "Teacher" });
    const response = await scim("PUT", `/Users/${created.id}`, {
      schemas: [SCHEMA_USER],
      id: created.id,
      userName: created.userName,
      active: true,
    });

    // RFC 7644 3.5.1: PUT replaces the resource, so an omitted mutable attribute goes.
    expect(response.status).toBe(200);
    expect(response.body.title).toBeFalsy();
  });

  it("drops an attribute the schema does not define rather than echoing it", async () => {
    const created = await createUser();
    const response = await scim("PUT", `/Users/${created.id}`, {
      schemas: [SCHEMA_USER],
      id: created.id,
      userName: created.userName,
      active: true,
      nosuchattribute: "ignored",
    });

    expect(response.status).toBe(200);
    expect(response.body).not.toHaveProperty("nosuchattribute");
  });

  it("answers 404 for an unknown id even when the body carries no id", async () => {
    // Regression: id is read-only, so a client may omit it. The provider then saw an
    // unidentified resource and answered 400 before it could look anything up.
    const response = await scim("PUT", `/Users/${unique("ghost")}`, {
      schemas: [SCHEMA_USER],
      userName: `${unique("ghost")}@example.sg`,
      active: true,
    });

    expect(response.status).toBe(404);
  });

  it("refuses a body whose id names a different resource", async () => {
    const created = await createUser();
    const response = await scim("PUT", `/Users/${created.id}`, {
      schemas: [SCHEMA_USER],
      id: unique("other"),
      userName: created.userName,
      active: true,
    });

    expect(response.status).toBe(400);
    expect(response.body.scimType).toBe("mutability");
  });
});

describe("Users: patch", () => {
  it("applies every operation of a multi-operation request", async () => {
    const created = await createUser();
    const response = await scim(
      "PATCH",
      `/Users/${created.id}`,
      patchOp(
        { op: "replace", path: "displayName", value: "Patched" },
        { op: "replace", path: "title", value: "HOD" },
      ),
    );

    expect(PATCH_APPLIED).toContain(response.status);
    const after = await readUser(created.id);
    expect(after.displayName).toBe("Patched");
    expect(after.title).toBe("HOD");
  });

  it("clears an attribute on a remove that carries no value", async () => {
    // Regression, twice over: the absent value first threw, and then the substitute
    // value object made the remove look like the removal of some other value.
    const created = await createUser({ title: "HOD" });
    const response = await scim(
      "PATCH",
      `/Users/${created.id}`,
      patchOp({ op: "remove", path: "title" }),
    );

    expect(PATCH_APPLIED).toContain(response.status);
    expect((await readUser(created.id)).title).toBeFalsy();
  });

  it.each(["nickName", "locale", "timezone", "userType"])(
    "applies a patch to %s, which the patcher used to ignore",
    async (attribute) => {
      const created = await createUser();
      const response = await scim(
        "PATCH",
        `/Users/${created.id}`,
        patchOp({ op: "replace", path: attribute, value: "changed" }),
      );

      expect(PATCH_APPLIED).toContain(response.status);
      expect((await readUser(created.id))[attribute]).toBe("changed");
    },
  );

  it("rejects a path the resource type does not model", async () => {
    const created = await createUser();
    const response = await scim(
      "PATCH",
      `/Users/${created.id}`,
      patchOp({ op: "replace", path: "nosuchattribute", value: "x" }),
    );

    expect(response.status).toBe(400);
    expect(response.body.scimType).toBe("invalidPath");
  });

  it("applies nothing when one operation of several is invalid", async () => {
    const created = await createUser({ title: "Original" });
    const response = await scim(
      "PATCH",
      `/Users/${created.id}`,
      patchOp(
        { op: "replace", path: "title", value: "Applied" },
        { op: "replace", path: "nosuchattribute", value: "x" },
      ),
    );

    expect(response.status).toBe(400);
    expect((await readUser(created.id)).title).toBe("Original");
  });

  it("rejects an unrecognised op verb", async () => {
    const created = await createUser();
    const response = await scim(
      "PATCH",
      `/Users/${created.id}`,
      patchOp({ op: "frobnicate", path: "title", value: "x" }),
    );

    expect(response.status).toBe(400);
  });

  it("cannot change id", async () => {
    const created = await createUser();
    await scim("PATCH", `/Users/${created.id}`, patchOp({
      op: "replace",
      path: "id",
      value: unique("hijack"),
    }));

    expect((await readUser(created.id)).id).toBe(created.id);
  });
});

describe("Users: delete", () => {
  it("answers 204 and the resource is gone", async () => {
    const created = await createUser();

    expect((await scim("DELETE", `/Users/${created.id}`)).status).toBe(204);
    expect((await scim("GET", `/Users/${created.id}`)).status).toBe(404);
  });

  it("is idempotent", async () => {
    const created = await createUser();
    await scim("DELETE", `/Users/${created.id}`);
    const again = await scim("DELETE", `/Users/${created.id}`);

    expect([204, 404]).toContain(again.status);
  });
});

describe("Users: externalId", () => {
  it("round-trips and is filterable", async () => {
    const externalId = unique("ext");
    const created = await createUser({ externalId });

    expect(created.externalId).toBe(externalId);

    const found = await scim("GET", `/Users${filterQuery(`externalId eq "${externalId}"`)}`);
    expect(found.status).toBe(200);
    expect(found.body.Resources).toHaveLength(1);
  });

  it("is not required to be unique", async () => {
    // RFC 7643 3.1 scopes externalId to the provisioning domain and does not ask the
    // service to enforce uniqueness on it.
    const externalId = unique("shared");
    await createUser({ externalId });
    const second = await scim("POST", "/Users", userBody({ externalId }));

    expect([201, 409]).toContain(second.status);
  });
});
