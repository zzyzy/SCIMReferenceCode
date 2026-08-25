import { beforeAll, describe, expect, it } from "vitest";
import {
  PATCH_APPLIED,
  SCHEMA_ERROR,
  SCHEMA_GROUP,
  SCHEMA_USER,
  edupass,
  patchOp,
  unique,
  type ScimResource,
} from "../src/client.js";
import { EDUPASS_LOCATION } from "../src/host.js";

/**
 * The Edupass interface specification, driven through the sample host.
 *
 * These are constraints Edupass states and RFC 7643 does not - closed value sets, a
 * UIN/FIN format, a 256-character ceiling, one primary email - plus the obligations
 * the specification places on a relying party's provider, which only the provider can
 * discharge because only it knows how users and groups relate.
 *
 * The host serves them because it was started with SCIM_PROVIDER=edupass, which binds
 * /Users to EduPassUser. Sent to the core host these same requests would be answered
 * by the plain reference provider and prove nothing.
 */

const EXTENSION = "urn:ietf:params:scim:schemas:extension:Edupass:2.0:User";

/** Longer than the 256-character ceiling the specification sets. */
const TOO_LONG = "x".repeat(257);

function eduUser(overrides: Record<string, unknown> = {}): Record<string, unknown> {
  const userName = `${unique("edu")}@moe.edu.sg`;
  return {
    schemas: [SCHEMA_USER, EXTENSION],
    userName,
    // Edupass' identifier for the resource, per the specification's User schema. Every
    // example in it carries one, and a relying party may legitimately require it - it is
    // the only handle Edupass has on the resource besides the RP's own id.
    externalId: unique("edupass-id"),
    active: true,
    name: { givenName: "Given", familyName: "Family" },
    emails: [{ value: userName, type: "WOG", primary: true }],
    [EXTENSION]: {
      identityType: "Staff",
      schoolOrHq: "School",
      identitySource: "HRPS",
    },
    ...overrides,
  };
}

async function createEduUser(overrides: Record<string, unknown> = {}): Promise<ScimResource> {
  const response = await edupass<ScimResource>("POST", "/Users", eduUser(overrides));
  if (response.status !== 201) {
    throw new Error(`could not create an Edupass user: ${response.status} ${response.text}`);
  }
  return response.body;
}

/**
 * A group displayName in the format the specification mandates.
 *
 * "Application role in the format <location code>_<app code>_<role code>" - so an
 * arbitrary name is not merely unconventional, it is not an application role at all, and
 * a relying party is entitled to refuse it. The suite previously used unique("role"),
 * which has no underscores; that passed only because the in-memory provider treats
 * displayName as an opaque string.
 */
function eduRole(): string {
  return unique(`${EDUPASS_LOCATION}_app1_role`);
}

async function createEduGroup(overrides: Record<string, unknown> = {}): Promise<ScimResource> {
  const response = await edupass<ScimResource>("POST", "/Groups", {
    schemas: [SCHEMA_GROUP],
    displayName: eduRole(),
    externalId: unique("edupass-grp"),
    ...overrides,
  });
  if (response.status !== 201) {
    throw new Error(`could not create an Edupass group: ${response.status} ${response.text}`);
  }
  return response.body;
}

/** Every rejection the specification asks for is a 400 carrying this keyword. */
function expectInvalidValue(response: { status: number; body: any }): void {
  expect(response.status).toBe(400);
  expect(response.body.schemas).toContain(SCHEMA_ERROR);
  expect(response.body.scimType).toBe("invalidValue");
}

describe("Edupass: the extension schema is served", () => {
  it("advertises the extension in /Schemas", async () => {
    const response = await edupass("GET", "/Schemas");

    expect(response.status).toBe(200);
    const identifiers = (response.body.Resources as { id: string }[]).map((item) => item.id);
    expect(identifiers).toContain(EXTENSION);
  });

  it("omits uinFin from the advertised schema when the party does not store it", async () => {
    // The provider was constructed with requireUinFin false, and the schema it
    // advertises has to agree with the validation it applies - that is the point of
    // the two being driven by one flag.
    const response = await edupass("GET", "/Schemas");
    const extension = (response.body.Resources as { id: string; attributes?: { name: string }[] }[]).find(
      (item) => item.id === EXTENSION,
    );

    expect(extension).toBeDefined();
    const names = (extension?.attributes ?? []).map((attribute) => attribute.name);
    expect(names).toContain("identityType");
    expect(names).not.toContain("uinFin");
  });

  it("serves exactly one User resource type, the Edupass one", async () => {
    // The Edupass provider drops the base User type and substitutes its own, so a
    // second entry would mean both are being advertised and a client could not tell
    // which endpoint it was talking to.
    const response = await edupass("GET", "/ResourceTypes");

    expect(response.status).toBe(200);
    const users = (response.body.Resources as any[]).filter((item) => item.id === "User");
    expect(users).toHaveLength(1);
    expect(users[0].endpoint).toContain("Users");
    expect(users[0].schema).toBe(SCHEMA_USER);
  });
});

describe("Edupass: the extension round-trips", () => {
  it("echoes every extension attribute it was given", async () => {
    const created = await createEduUser({
      [EXTENSION]: {
        identityType: "Student",
        schoolOrHq: "HQ",
        identitySource: "MIMS",
      },
    });

    expect(created.schemas).toContain(EXTENSION);
    expect(created[EXTENSION]).toMatchObject({
      identityType: "Student",
      schoolOrHq: "HQ",
      identitySource: "MIMS",
    });

    const read = await edupass<ScimResource>("GET", `/Users/${created.id}`);
    expect(read.body[EXTENSION]).toMatchObject({ identityType: "Student" });
  });

  it("declares the extension even when the request's schemas omitted it", async () => {
    // The specification's examples list it, but nothing enforces that. A response whose
    // schemas does not declare the extension it is carrying is the failure to avoid.
    const created = await edupass<ScimResource>("POST", "/Users", {
      ...eduUser(),
      schemas: [SCHEMA_USER],
    });

    expect(created.status).toBe(201);
    expect(created.body.schemas).toContain(EXTENSION);
    expect(created.body[EXTENSION]).toMatchObject({ identityType: "Staff" });
  });

  it("accepts a user carrying no extension at all", async () => {
    // Every extension member is optional, and this party does not store UIN/FIN.
    const userName = `${unique("edu")}@moe.edu.sg`;
    const created = await edupass<ScimResource>("POST", "/Users", {
      schemas: [SCHEMA_USER],
      userName,
      // Carried even though this case is about the extension: a party may legitimately
      // require externalId, and omitting it here made this case fail on that instead -
      // reporting a refusal of the extension-less user that had not happened.
      externalId: unique("edupass-id"),
      active: true,
    });

    expect(created.status).toBe(201);
  });
});

describe("Edupass: closed value sets", () => {
  it.each([
    ["identityType", "Principal"],
    ["schoolOrHq", "Ministry"],
    ["identitySource", "LDAP"],
  ])("refuses %s outside its permitted set", async (attribute, value) => {
    const response = await edupass("POST", "/Users", eduUser({ [EXTENSION]: { [attribute]: value } }));

    expectInvalidValue(response);
  });

  it.each(["Non-human", "Student", "Staff", "Temp", "Intern", "Vendor", "Others"])(
    "accepts identityType %s",
    async (identityType) => {
      const response = await edupass("POST", "/Users", eduUser({ [EXTENSION]: { identityType } }));

      expect(response.status).toBe(201);
    },
  );

  it("refuses an email type outside the permitted set", async () => {
    // RFC 7643's canonical values are advisory, so the core library treats
    // emails[].type as a free string. Edupass closes the set.
    const response = await edupass(
      "POST",
      "/Users",
      eduUser({ emails: [{ value: "a@moe.edu.sg", type: "home", primary: true }] }),
    );

    expectInvalidValue(response);
  });

  it.each(["WOG", "CES", "ICON", "OTHER"])("accepts email type %s", async (type) => {
    const userName = `${unique("edu")}@moe.edu.sg`;
    const response = await edupass(
      "POST",
      "/Users",
      eduUser({ userName, emails: [{ value: userName, type, primary: true }] }),
    );

    expect(response.status).toBe(201);
  });
});

describe("Edupass: the UIN/FIN format", () => {
  it.each(["S1234567A", "T7654321Z", "F0000000B", "G1111111C", "M2222222D"])(
    "accepts %s",
    async (uinFin) => {
      const response = await edupass(
        "POST",
        "/Users",
        eduUser({ [EXTENSION]: { identityType: "Staff", uinFin } }),
      );

      expect(response.status).toBe(201);
    },
  );

  it.each([
    ["X1234567A", "an initial letter outside STFGM"],
    ["S123456A", "too few digits"],
    ["S12345678A", "too many digits"],
    ["S1234567", "no trailing letter"],
    ["S1234567a", "a lower-case trailing letter"],
    ["s1234567A", "a lower-case initial letter"],
    [" S1234567A", "leading whitespace"],
  ])("refuses %s (%s)", async (uinFin) => {
    const response = await edupass(
      "POST",
      "/Users",
      eduUser({ [EXTENSION]: { identityType: "Staff", uinFin } }),
    );

    expectInvalidValue(response);
  });
});

describe("Edupass: the 256-character ceiling", () => {
  it("refuses an over-long userName", async () => {
    expectInvalidValue(await edupass("POST", "/Users", eduUser({ userName: TOO_LONG })));
  });

  it("refuses an over-long externalId", async () => {
    expectInvalidValue(await edupass("POST", "/Users", eduUser({ externalId: TOO_LONG })));
  });

  it("refuses an over-long name.formatted", async () => {
    expectInvalidValue(
      await edupass("POST", "/Users", eduUser({ name: { formatted: TOO_LONG } })),
    );
  });

  it("refuses an over-long title", async () => {
    expectInvalidValue(await edupass("POST", "/Users", eduUser({ title: TOO_LONG })));
  });

  it("refuses an over-long email value", async () => {
    expectInvalidValue(
      await edupass("POST", "/Users", eduUser({ emails: [{ value: TOO_LONG, type: "WOG" }] })),
    );
  });

  it("accepts a userName of exactly the maximum length", async () => {
    // The ceiling is inclusive; an off-by-one here would refuse a legal value.
    //
    // Padded to length rather than written as a literal. A constant userName is unique
    // only on a party that forgets between runs: against one that stores users the
    // first run creates it and every later run gets a genuine 409, which reads as a
    // broken ceiling and is nothing of the kind.
    const filler = unique("edu");
    const userName = `${filler}${"a".repeat(245 - filler.length)}@moe.edu.sg`;
    expect(userName).toHaveLength(256);

    const response = await edupass("POST", "/Users", eduUser({ userName }));
    expect(response.status).toBe(201);
  });

  it("refuses an over-long group displayName", async () => {
    // displayName encodes the application role, so it is bound by the same ceiling.
    const response = await edupass("POST", "/Groups", {
      schemas: [SCHEMA_GROUP],
      displayName: TOO_LONG,
    });

    expect(response.status).toBe(400);
  });
});

describe("Edupass: required and ambiguous values", () => {
  it("refuses a user with no userName", async () => {
    const body = eduUser();
    delete body["userName"];

    expectInvalidValue(await edupass("POST", "/Users", body));
  });

  it("refuses a user whose userName is only whitespace", async () => {
    expectInvalidValue(await edupass("POST", "/Users", eduUser({ userName: "   " })));
  });

  it("refuses two primary email addresses", async () => {
    // RFC 7643 2.4 forbids it, and the primary address is the notification email, so
    // more than one is ambiguous rather than merely untidy.
    const response = await edupass(
      "POST",
      "/Users",
      eduUser({
        emails: [
          { value: "one@moe.edu.sg", type: "WOG", primary: true },
          { value: "two@moe.edu.sg", type: "CES", primary: true },
        ],
      }),
    );

    expectInvalidValue(response);
  });

  it("accepts one primary address alongside others", async () => {
    const response = await edupass(
      "POST",
      "/Users",
      eduUser({
        emails: [
          { value: "one@moe.edu.sg", type: "WOG", primary: true },
          { value: "two@moe.edu.sg", type: "CES" },
        ],
      }),
    );

    expect(response.status).toBe(201);
  });

  it("refuses a group with no displayName", async () => {
    const response = await edupass("POST", "/Groups", { schemas: [SCHEMA_GROUP] });

    expect(response.status).toBe(400);
  });

  it("refuses a second group with the same displayName", async () => {
    const first = await createEduGroup();
    const response = await edupass("POST", "/Groups", {
      schemas: [SCHEMA_GROUP],
      displayName: first.displayName,
    });

    expect(response.status).toBe(409);
  });

  it("refuses a second user with the same userName", async () => {
    const first = await createEduUser();
    const response = await edupass("POST", "/Users", eduUser({ userName: first.userName }));

    expect(response.status).toBe(409);
  });
});

describe("Edupass: PATCH against the extension", () => {
  it("replaces an extension attribute named by its full URN path", async () => {
    const user = await createEduUser({ [EXTENSION]: { identityType: "Staff" } });

    const patched = await edupass(
      "PATCH",
      `/Users/${user.id}`,
      patchOp({ op: "replace", path: `${EXTENSION}:identityType`, value: "Vendor" }),
    );

    expect(PATCH_APPLIED).toContain(patched.status);
    const read = await edupass<ScimResource>("GET", `/Users/${user.id}`);
    expect((read.body[EXTENSION] as any).identityType).toBe("Vendor");
  });

  it("adds an extension attribute that was absent", async () => {
    const user = await createEduUser({ [EXTENSION]: { identityType: "Staff" } });

    const patched = await edupass(
      "PATCH",
      `/Users/${user.id}`,
      patchOp({ op: "add", path: `${EXTENSION}:identitySource`, value: "SC" }),
    );

    expect(PATCH_APPLIED).toContain(patched.status);
    const read = await edupass<ScimResource>("GET", `/Users/${user.id}`);
    expect((read.body[EXTENSION] as any).identitySource).toBe("SC");
  });

  it("removes an extension attribute", async () => {
    const user = await createEduUser({
      [EXTENSION]: { identityType: "Staff", schoolOrHq: "School" },
    });

    const patched = await edupass(
      "PATCH",
      `/Users/${user.id}`,
      patchOp({ op: "remove", path: `${EXTENSION}:schoolOrHq` }),
    );

    expect(PATCH_APPLIED).toContain(patched.status);
    const read = await edupass<ScimResource>("GET", `/Users/${user.id}`);
    expect((read.body[EXTENSION] as any)?.schoolOrHq).toBeUndefined();
  });

  it("refuses a PATCH that would leave the resource invalid", async () => {
    const user = await createEduUser({ [EXTENSION]: { identityType: "Staff" } });

    const patched = await edupass(
      "PATCH",
      `/Users/${user.id}`,
      patchOp({ op: "replace", path: `${EXTENSION}:identityType`, value: "Principal" }),
    );

    expect(patched.status).toBe(400);
  });

  it("leaves the stored resource untouched when a PATCH is refused", async () => {
    // The patch is applied to a copy and committed only once it validates. Applying it
    // to the stored resource and validating afterwards left a rejected request's
    // changes in place, which is the opposite of what the specification requires.
    const user = await createEduUser({ [EXTENSION]: { identityType: "Staff" } });

    await edupass(
      "PATCH",
      `/Users/${user.id}`,
      patchOp(
        { op: "replace", path: "title", value: "Head of Department" },
        { op: "replace", path: `${EXTENSION}:identityType`, value: "Principal" },
      ),
    );

    const read = await edupass<ScimResource>("GET", `/Users/${user.id}`);
    expect((read.body[EXTENSION] as any).identityType).toBe("Staff");
    expect(read.body["title"]).not.toBe("Head of Department");
  });

  it("refuses a core attribute PATCH that breaks the ceiling", async () => {
    const user = await createEduUser();

    const patched = await edupass(
      "PATCH",
      `/Users/${user.id}`,
      patchOp({ op: "replace", path: "title", value: TOO_LONG }),
    );

    expect(patched.status).toBe(400);
  });
});

describe("Edupass: the provider's obligations over users and groups", () => {
  let user: ScimResource;
  let group: ScimResource;

  beforeAll(async () => {
    user = await createEduUser();
    group = await createEduGroup({ members: [{ value: user.id }] });
  });

  it("projects group membership onto the user that is read", async () => {
    // Edupass requires a relying party whose roles it manages to report them on the
    // user. Membership is held once, on the group, and derived here on read, so the
    // two cannot disagree.
    const read = await edupass<ScimResource>("GET", `/Users/${user.id}`);

    const groups = (read.body["groups"] as { value: string; display: string }[] | undefined) ?? [];
    expect(groups.map((item) => item.value)).toContain(group.id);
    expect(groups.map((item) => item.display)).toContain(group.displayName);
  });

  it("projects membership onto a queried user too", async () => {
    const response = await edupass(
      "GET",
      `/Users?filter=${encodeURIComponent(`userName eq "${user.userName}"`)}`,
    );

    expect(response.status).toBe(200);
    const found = (response.body.Resources as ScimResource[])[0];
    const groups = (found?.["groups"] as { value: string }[] | undefined) ?? [];
    expect(groups.map((item) => item.value)).toContain(group.id);
  });

  it("refuses a membership naming an identifier that resolves to no user", async () => {
    // Stored and handed back to Edupass on the next read is the failure to avoid.
    const response = await edupass("POST", "/Groups", {
      schemas: [SCHEMA_GROUP],
      displayName: eduRole(),
      externalId: unique("edupass-grp"),
      members: [{ value: "00000000-0000-0000-0000-000000000000" }],
    });

    expect(response.status).toBe(400);
  });

  it("refuses a PATCH that adds an unresolvable member", async () => {
    const target = await createEduGroup();

    const response = await edupass(
      "PATCH",
      `/Groups/${target.id}`,
      patchOp({
        op: "add",
        path: "members",
        value: [{ value: "11111111-1111-1111-1111-111111111111" }],
      }),
    );

    expect(response.status).toBe(400);
  });

  it("removes the role from its members when a group is deleted", async () => {
    const member = await createEduUser();
    const role = await createEduGroup({ members: [{ value: member.id }] });

    expect(await edupass("DELETE", `/Groups/${role.id}`)).toMatchObject({ status: 204 });

    const read = await edupass<ScimResource>("GET", `/Users/${member.id}`);
    const groups = (read.body["groups"] as { value: string }[] | undefined) ?? [];
    expect(groups.map((item) => item.value)).not.toContain(role.id);
  });

  it("removes a deleted user from every group that listed them", async () => {
    const member = await createEduUser();
    const first = await createEduGroup({ members: [{ value: member.id }] });
    const second = await createEduGroup({ members: [{ value: member.id }] });

    expect(await edupass("DELETE", `/Users/${member.id}`)).toMatchObject({ status: 204 });

    for (const identifier of [first.id, second.id]) {
      const read = await edupass<ScimResource>("GET", `/Groups/${identifier}`);
      const members = (read.body["members"] as { value: string }[] | undefined) ?? [];
      expect(members.map((item) => item.value)).not.toContain(member.id);
    }
  });

  it("answers 404 for a user that was deleted", async () => {
    const doomed = await createEduUser();
    await edupass("DELETE", `/Users/${doomed.id}`);

    expect((await edupass("GET", `/Users/${doomed.id}`)).status).toBe(404);
    expect((await edupass("DELETE", `/Users/${doomed.id}`)).status).toBe(404);
  });
});

describe("Edupass: the query surface it specifies", () => {
  it("answers eq on userName", async () => {
    const created = await createEduUser();
    const response = await edupass(
      "GET",
      `/Users?filter=${encodeURIComponent(`userName eq "${created.userName}"`)}`,
    );

    expect(response.status).toBe(200);
    expect(response.body.Resources).toHaveLength(1);
  });

  it("answers eq on externalId", async () => {
    const externalIdentifier = unique("ext");
    await createEduUser({ externalId: externalIdentifier });

    const response = await edupass(
      "GET",
      `/Users?filter=${encodeURIComponent(`externalId eq "${externalIdentifier}"`)}`,
    );

    expect(response.status).toBe(200);
    expect(response.body.Resources).toHaveLength(1);
  });

  it("answers eq on a group displayName", async () => {
    const created = await createEduGroup();
    const response = await edupass(
      "GET",
      `/Groups?filter=${encodeURIComponent(`displayName eq "${created.displayName}"`)}`,
    );

    expect(response.status).toBe(200);
    expect(response.body.Resources).toHaveLength(1);
  });

  it("refuses a filter on an attribute Edupass does not specify", async () => {
    // Edupass requires eq on userName and nothing else; the provider says so rather
    // than silently returning everything, which a caller would read as a match.
    const response = await edupass(
      "GET",
      `/Users?filter=${encodeURIComponent('title eq "Teacher"')}`,
    );

    expect(response.status).toBe(400);
    expect(response.body.scimType).toBe("invalidFilter");
  });

  it("refuses an operator other than eq", async () => {
    const response = await edupass(
      "GET",
      `/Users?filter=${encodeURIComponent('userName co "moe"')}`,
    );

    expect(response.status).toBe(400);
    expect(response.body.scimType).toBe("invalidFilter");
  });
});

describe("Edupass: replace", () => {
  it("replaces a user and keeps the created timestamp", async () => {
    const created = await createEduUser();

    const replaced = await edupass<ScimResource>("PUT", `/Users/${created.id}`, {
      ...eduUser({ userName: created.userName }),
      id: created.id,
    });

    expect(replaced.status).toBe(200);
    expect(replaced.body.meta?.created).toBe(created.meta?.created);
  });

  it("refuses a replace that violates the specification", async () => {
    const created = await createEduUser();

    const replaced = await edupass("PUT", `/Users/${created.id}`, {
      ...eduUser({ userName: created.userName, title: TOO_LONG }),
      id: created.id,
    });

    expect(replaced.status).toBe(400);
  });

  it("refuses a replace that would duplicate another userName", async () => {
    const first = await createEduUser();
    const second = await createEduUser();

    const replaced = await edupass("PUT", `/Users/${second.id}`, {
      ...eduUser({ userName: first.userName }),
      id: second.id,
    });

    expect(replaced.status).toBe(409);
  });

  it("answers 404 replacing a user that does not exist", async () => {
    const identifier = "22222222-2222-2222-2222-222222222222";
    const replaced = await edupass("PUT", `/Users/${identifier}`, {
      ...eduUser(),
      id: identifier,
    });

    expect(replaced.status).toBe(404);
  });
});
