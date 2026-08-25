import { randomUUID } from "node:crypto";
import { describe, expect, it } from "vitest";
import {
  PATCH_APPLIED,
  SCHEMA_GROUP,
  createGroup,
  createUser,
  filterQuery,
  groupBody,
  memberIds,
  patchOp,
  readGroup,
  scim,
  unique,
} from "../src/client.js";

describe("Groups: lifecycle", () => {
  it("creates, reads and deletes", async () => {
    const created = await createGroup();

    expect((await scim("GET", `/Groups/${created.id}`)).status).toBe(200);
    expect((await scim("DELETE", `/Groups/${created.id}`)).status).toBe(204);
    expect((await scim("GET", `/Groups/${created.id}`)).status).toBe(404);
  });

  it("refuses a duplicate displayName", async () => {
    const created = await createGroup();
    const again = await scim("POST", "/Groups", {
      schemas: [SCHEMA_GROUP],
      displayName: created.displayName,
    });

    expect(again.status).toBe(409);
  });

  it("refuses a body with no displayName", async () => {
    expect((await scim("POST", "/Groups", { schemas: [SCHEMA_GROUP] })).status).toBe(400);
  });

  it("filters by displayName, which is how a client finds a group it already created", async () => {
    const created = await createGroup();
    const response = await scim(
      "GET",
      `/Groups${filterQuery(`displayName eq "${created.displayName}"`)}`,
    );

    expect(response.status).toBe(200);
    expect(response.body.Resources).toHaveLength(1);
    expect(response.body.Resources[0].displayName).toBe(created.displayName);
  });
});

describe("Groups: membership", () => {
  it("adds a member and reads it back", async () => {
    const group = await createGroup();
    const user = await createUser();
    const response = await scim(
      "PATCH",
      `/Groups/${group.id}`,
      patchOp({ op: "add", path: "members", value: [{ value: user.id }] }),
    );

    expect(PATCH_APPLIED).toContain(response.status);
    expect(await memberIds(group.id)).toContain(user.id);
  });

  it("treats a repeated add as a no-op rather than duplicating", async () => {
    const group = await createGroup();
    const user = await createUser();
    const add = patchOp({ op: "add", path: "members", value: [{ value: user.id }] });

    await scim("PATCH", `/Groups/${group.id}`, add);
    const second = await scim("PATCH", `/Groups/${group.id}`, add);

    expect(PATCH_APPLIED).toContain(second.status);
    expect(await memberIds(group.id)).toEqual([user.id]);
  });

  it("removes one member by filter and leaves the rest", async () => {
    const group = await createGroup();
    const kept = await createUser();
    const removed = await createUser();
    await scim(
      "PATCH",
      `/Groups/${group.id}`,
      patchOp({ op: "add", path: "members", value: [{ value: kept.id }, { value: removed.id }] }),
    );

    const response = await scim(
      "PATCH",
      `/Groups/${group.id}`,
      patchOp({ op: "remove", path: `members[value eq "${removed.id}"]` }),
    );

    expect(PATCH_APPLIED).toContain(response.status);
    expect(await memberIds(group.id)).toEqual([kept.id]);
  });

  it("replaces the membership wholesale, which is the full sync", async () => {
    // Regression: the members case handled only Add and Remove, so a Replace fell
    // through the switch while the service still answered 204 - a sync that
    // silently did nothing.
    const group = await createGroup();
    const before = await createUser();
    const after = await createUser();
    await scim(
      "PATCH",
      `/Groups/${group.id}`,
      patchOp({ op: "add", path: "members", value: [{ value: before.id }] }),
    );

    const response = await scim(
      "PATCH",
      `/Groups/${group.id}`,
      patchOp({ op: "replace", path: "members", value: [{ value: after.id }] }),
    );

    expect(PATCH_APPLIED).toContain(response.status);
    expect(await memberIds(group.id)).toEqual([after.id]);
  });

  it("removes every member on a valueless remove", async () => {
    const group = await createGroup();
    const user = await createUser();
    await scim(
      "PATCH",
      `/Groups/${group.id}`,
      patchOp({ op: "add", path: "members", value: [{ value: user.id }] }),
    );

    const response = await scim(
      "PATCH",
      `/Groups/${group.id}`,
      patchOp({ op: "remove", path: "members" }),
    );

    expect(PATCH_APPLIED).toContain(response.status);
    expect(await memberIds(group.id)).toEqual([]);
  });

  it("treats removing a user who is not a member as a no-op success", async () => {
    const group = await createGroup();
    // A well-formed identifier that simply is not a member. unique() returns a dotted
    // token, which makes the path filter itself malformed - that would test the path
    // parser rather than the no-op the specification asks for.
    const stranger = randomUUID();
    const response = await scim(
      "PATCH",
      `/Groups/${group.id}`,
      patchOp({ op: "remove", path: `members[value eq "${stranger}"]` }),
    );

    expect(PATCH_APPLIED).toContain(response.status);
  });

  it("answers 404 for a PATCH against a group that does not exist", async () => {
    const user = await createUser();
    const response = await scim(
      "PATCH",
      `/Groups/${unique("ghost")}`,
      patchOp({ op: "add", path: "members", value: [{ value: user.id }] }),
    );

    expect(response.status).toBe(404);
  });

  it("takes many members in one operation and removes one of them", async () => {
    const group = await createGroup();
    const users: string[] = [];
    for (let index = 0; index < 20; index += 1) {
      users.push((await createUser()).id);
    }

    const add = await scim(
      "PATCH",
      `/Groups/${group.id}`,
      patchOp({ op: "add", path: "members", value: users.map((value) => ({ value })) }),
    );
    expect(PATCH_APPLIED).toContain(add.status);
    expect(await memberIds(group.id)).toHaveLength(20);

    await scim(
      "PATCH",
      `/Groups/${group.id}`,
      patchOp({ op: "remove", path: `members[value eq "${users[0]}"]` }),
    );
    expect(await memberIds(group.id)).toHaveLength(19);
  });
});

describe("Groups: rename", () => {
  it("renames through PATCH", async () => {
    const group = await createGroup();
    const name = unique("renamed");
    const response = await scim(
      "PATCH",
      `/Groups/${group.id}`,
      patchOp({ op: "replace", path: "displayName", value: name }),
    );

    expect(PATCH_APPLIED).toContain(response.status);
    expect((await readGroup(group.id)).displayName).toBe(name);
  });

  it("refuses a rename onto another group's displayName", async () => {
    // Regression: create and replace enforced uniqueness, PATCH did not - so two
    // groups could end up sharing the name a relying party uses to identify a role.
    const first = await createGroup();
    const second = await createGroup();

    const response = await scim(
      "PATCH",
      `/Groups/${second.id}`,
      patchOp({ op: "replace", path: "displayName", value: first.displayName }),
    );

    expect(response.status).toBe(409);
    expect((await readGroup(second.id)).displayName).toBe(second.displayName);
  });

  it("applies a displayName change and a membership change in one request", async () => {
    const group = await createGroup();
    const user = await createUser();
    const name = unique("both");

    const response = await scim(
      "PATCH",
      `/Groups/${group.id}`,
      patchOp(
        { op: "replace", path: "displayName", value: name },
        { op: "add", path: "members", value: [{ value: user.id }] },
      ),
    );

    expect(PATCH_APPLIED).toContain(response.status);
    const after = await readGroup(group.id);
    expect(after.displayName).toBe(name);
    expect(await memberIds(group.id)).toEqual([user.id]);
  });

  it("replaces a group through PUT", async () => {
    const group = await createGroup();
    const response = await scim("PUT", `/Groups/${group.id}`, groupBody({ id: group.id }));

    expect(response.status).toBe(200);
  });

  it("projects a group response", async () => {
    const group = await createGroup();
    const response = await scim("GET", `/Groups/${group.id}?attributes=displayName`);

    expect(response.status).toBe(200);
    expect(response.body).toHaveProperty("displayName");
    expect(response.body).not.toHaveProperty("members");
  });
});

describe("Groups: the members attribute keeps what RFC 7643 4.2 defines", () => {
  it("keeps the $ref a client supplied with a member", async () => {
    // RFC 7643 section 4.2 gives members a $ref sub-attribute, and a client that
    // sends one has said where the member lives. Rebuilding the entry from `value`
    // alone throws that away silently: the write succeeds and the reference is gone.
    const group = await createGroup();
    const user = await createUser();
    const reference = `http://localhost/scim/Users/${user.id}`;

    const patched = await scim(
      "PATCH",
      `/Groups/${group.id}`,
      patchOp({ op: "add", path: "members", value: [{ value: user.id, $ref: reference }] }),
    );
    expect(PATCH_APPLIED).toContain(patched.status);

    const members = ((await readGroup(group.id)).members ?? []) as {
      value: string;
      $ref?: string;
    }[];

    expect(members).toHaveLength(1);
    expect(members[0]?.$ref).toBe(reference);
  });

  it("keeps a member's display alongside its value", async () => {
    const group = await createGroup();
    const user = await createUser();

    expect(PATCH_APPLIED).toContain(
      (
        await scim(
          "PATCH",
          `/Groups/${group.id}`,
          patchOp({
            op: "add",
            path: "members",
            value: [{ value: user.id, display: "Ada Lovelace", type: "User" }],
          }),
        )
      ).status,
    );

    const members = ((await readGroup(group.id)).members ?? []) as {
      value: string;
      display?: string;
      type?: string;
    }[];

    expect(members[0]?.display).toBe("Ada Lovelace");
    expect(members[0]?.type).toBe("User");
  });

  it("keeps the $ref through a replace, which is a full membership sync", async () => {
    const group = await createGroup();
    const user = await createUser();
    const reference = `http://localhost/scim/Users/${user.id}`;

    expect(PATCH_APPLIED).toContain(
      (
        await scim(
          "PATCH",
          `/Groups/${group.id}`,
          patchOp({ op: "replace", path: "members", value: [{ value: user.id, $ref: reference }] }),
        )
      ).status,
    );

    const members = ((await readGroup(group.id)).members ?? []) as { $ref?: string }[];
    expect(members[0]?.$ref).toBe(reference);
  });

  it("removes a member whose identifier differs only in case", async () => {
    // Adding is case-insensitive and the membership projection is case-insensitive,
    // so removing must be too. Otherwise a client that round-trips an identifier
    // through anything that changes its case can add a member it can never remove.
    const group = await createGroup();
    const user = await createUser();

    expect(PATCH_APPLIED).toContain(
      (
        await scim(
          "PATCH",
          `/Groups/${group.id}`,
          patchOp({ op: "add", path: "members", value: [{ value: user.id }] }),
        )
      ).status,
    );
    expect(await memberIds(group.id)).toEqual([user.id]);

    expect(PATCH_APPLIED).toContain(
      (
        await scim(
          "PATCH",
          `/Groups/${group.id}`,
          patchOp({ op: "remove", path: "members", value: [{ value: user.id.toUpperCase() }] }),
        )
      ).status,
    );

    expect(await memberIds(group.id)).toEqual([]);
  });

  it("does not add the same member twice when the case differs", async () => {
    const group = await createGroup();
    const user = await createUser();

    for (const value of [user.id, user.id.toUpperCase()]) {
      expect(PATCH_APPLIED).toContain(
        (
          await scim(
            "PATCH",
            `/Groups/${group.id}`,
            patchOp({ op: "add", path: "members", value: [{ value }] }),
          )
        ).status,
      );
    }

    expect((await readGroup(group.id)).members as unknown[]).toHaveLength(1);
  });
});

describe("Groups: a member entry that leads with $ref", () => {
  // JSON property order carries no meaning, but Newtonsoft reads a leading $ref as
  // reference metadata rather than as the SCIM attribute. The Edupass specification
  // writes both its membership examples that way, so these are the exact bodies the
  // service has to accept - and the failure they guard against was a silent one: 204,
  // and the membership unchanged.
  it("adds the member", async () => {
    const group = await createGroup();
    const user = await createUser();

    expect(PATCH_APPLIED).toContain(
      (
        await scim(
          "PATCH",
          `/Groups/${group.id}`,
          patchOp({
            op: "add",
            path: "members",
            value: [{ $ref: `/Users/${user.id}`, value: user.id }],
          }),
        )
      ).status,
    );

    expect(await memberIds(group.id)).toEqual([user.id]);
  });

  it("replaces the membership", async () => {
    const group = await createGroup();
    const [first, second] = [await createUser(), await createUser()];

    for (const user of [first, second]) {
      expect(PATCH_APPLIED).toContain(
        (
          await scim(
            "PATCH",
            `/Groups/${group.id}`,
            patchOp({
              op: "replace",
              path: "members",
              value: [{ $ref: `/Users/${user.id}`, value: user.id }],
            }),
          )
        ).status,
      );

      expect(await memberIds(group.id)).toEqual([user.id]);
    }
  });
});
