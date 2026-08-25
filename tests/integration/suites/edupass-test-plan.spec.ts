import { afterAll, expect, it } from "vitest";
import { PATCH_APPLIED, SCHEMA_ERROR, SCHEMA_GROUP, SCHEMA_LIST, SCHEMA_PATCH, SCHEMA_USER, unique, type ScimResource } from "../src/client.js";
import { beginCase, call, endCase, summary, writeResults } from "../src/test-plan-recorder.js";

/**
 * The 25 cases of test-plan.xlsx, run against the Edupass host and written back out as a
 * CSV in the plan's own shape.
 *
 * Every request goes through the recorder, so the Input and Output columns are the run's
 * actual traffic rather than a description of it. `call` sends to the Edupass host and
 * nothing else: these cases are about the Edupass code path, and the same request answered
 * by the plain reference provider would prove nothing about it.
 *
 * ## What these can and cannot show
 *
 * The plan is written for FIMS, and much of what it expects happens inside FIMS: a UPA is
 * created and routed to a user admin, positions are added or overwritten on approval, a
 * user is banned or unbanned, a notification is triggered. None of that is SCIM, and none
 * of it is visible at this endpoint - the relying party does it after the call returns.
 *
 * What is checkable here is the protocol contract Edupass depends on: the status, the
 * resource, and the state the next request observes. Each case therefore asserts the SCIM
 * half and records the FIMS half in Remarks, so a row that says Pass says what it passed.
 *
 * ## Locations are groups
 *
 * The plan's "Location" is a group whose displayName encodes the location code, as the
 * plan's own sample data shows: `1001_app1_admin`. Adding a user to a location is adding
 * them to that group.
 */

const EXTENSION = "urn:ietf:params:scim:schemas:extension:Edupass:2.0:User";

/** Common to every case: what the endpoint cannot be asked to demonstrate. */
const FimsScope =
  "FIMS-side behaviour (UPA creation and approval, position add/overwrite, banning, " +
  "notification) happens after the call returns and is not visible at the SCIM endpoint. " +
  "The protocol contract is what is checked here.";

function locationGroup(code: string, role = "admin"): string {
  return `${code}_app1_${role}_${unique("g").slice(-6)}`;
}

function userBody(overrides: Record<string, unknown> = {}): Record<string, unknown> {
  const userName = `${unique("edu")}@moe.edu.sg`;
  return {
    schemas: [SCHEMA_USER, EXTENSION],
    userName,
    externalId: `edupass-${userName}`,
    active: true,
    displayName: "Test Teacher",
    name: { givenName: "Test", familyName: "Teacher" },
    title: "Teacher",
    emails: [{ value: userName, type: "WOG", primary: true }],
    [EXTENSION]: { identityType: "Staff", schoolOrHq: "School", identitySource: "HRPS" },
    ...overrides,
  };
}

async function createUser(overrides: Record<string, unknown> = {}): Promise<ScimResource> {
  const response = await call<ScimResource>("POST", "/Users", userBody(overrides));
  expect(response.status).toBe(201);
  return response.body;
}

async function createGroup(displayName: string, members?: { value: string }[]): Promise<ScimResource> {
  const response = await call<ScimResource>("POST", "/Groups", {
    schemas: [SCHEMA_GROUP],
    displayName,
    externalId: `edupass-grp-${displayName}`,
    ...(members ? { members } : {}),
  });
  expect(response.status).toBe(201);
  return response.body;
}

function patch(...operations: Record<string, unknown>[]): Record<string, unknown> {
  return { schemas: [SCHEMA_PATCH], Operations: operations };
}

/** The location groups a user is currently projected into, by displayName. */
async function locationsOf(identifier: string): Promise<string[]> {
  const read = await call<ScimResource>("GET", `/Users/${identifier}`);
  const groups = (read.body["groups"] as { display?: string }[] | undefined) ?? [];
  return groups.map((item) => item.display ?? "").sort();
}

function describeError(error: unknown): string {
  return error instanceof Error ? error.message : String(error);
}

/**
 * One plan case. The S/N and title are the plan's, so a row here and a row there are the
 * same case; the outcome is recorded either way and the failure still fails the run.
 */
function testCase(
  serialNumber: string,
  title: string,
  remarks: string[],
  body: () => Promise<void>,
): void {
  it(`${serialNumber}. ${title}`, async () => {
    beginCase(serialNumber, title, ...remarks);

    try {
      await body();
    } catch (error) {
      endCase("Fail", describeError(error));
      throw error;
    }

    endCase("Pass");
  });
}

afterAll(() => {
  const destination = writeResults();
  const counts = summary();
  console.log(
    `\ntest plan: ${counts.passed}/${counts.total} passed, ${counts.failed} failed -> ${destination}`,
  );
});

testCase(
  "1",
  "Create Group",
  [
    "Group created with a server-assigned id; a second group with the same displayName is refused 409.",
    "The FIMS group membership table is a provider concern; the reference provider holds membership on the group itself.",
  ],
  async () => {
    const displayName = locationGroup("1001");
    const created = await createGroup(displayName);

    expect(created.id).toBeTruthy();
    expect(created.displayName).toBe(displayName);
    expect(created.meta?.location).toContain(created.id);

    const duplicate = await call("POST", "/Groups", {
      schemas: [SCHEMA_GROUP],
      displayName,
    });
    expect(duplicate.status).toBe(409);
  },
);

testCase(
  "2",
  "Get Group",
  ["A valid id returns the group; an unknown id returns 404 carrying a SCIM error body."],
  async () => {
    const group = await createGroup(locationGroup("1002"));

    const found = await call<ScimResource>("GET", `/Groups/${group.id}`);
    expect(found.status).toBe(200);
    expect(found.body.id).toBe(group.id);

    const missing = await call("GET", "/Groups/00000000-0000-0000-0000-000000000000");
    expect(missing.status).toBe(404);
    expect(missing.body.schemas).toContain(SCHEMA_ERROR);
  },
);

testCase(
  "3",
  "Get Groups",
  [
    "ListResponse envelope with totalResults; startIndex and count honoured; displayName eq filters.",
    "Edupass specifies the eq operator on displayName and nothing else, which is what the provider answers.",
  ],
  async () => {
    const displayName = locationGroup("1003");
    await createGroup(displayName);

    const all = await call("GET", "/Groups");
    expect(all.status).toBe(200);
    expect(all.body.schemas).toContain(SCHEMA_LIST);
    expect(typeof all.body.totalResults).toBe("number");

    const paged = await call("GET", "/Groups?startIndex=1&count=1");
    expect(paged.status).toBe(200);
    expect(paged.body.Resources).toHaveLength(1);
    expect(paged.body.itemsPerPage).toBe(1);
    expect(paged.body.startIndex).toBe(1);

    const filtered = await call(
      "GET",
      `/Groups?filter=${encodeURIComponent(`displayName eq "${displayName}"`)}`,
    );
    expect(filtered.status).toBe(200);
    expect(filtered.body.Resources).toHaveLength(1);
  },
);

testCase(
  "4",
  "Create User",
  [
    "User created with a server-assigned id, attributes echoed back including the Edupass extension; a duplicate userName is refused 409.",
    "UPA creation and assignment to a user admin are FIMS-side and follow the call.",
  ],
  async () => {
    const body = userBody();
    const created = await call<ScimResource>("POST", "/Users", body);

    expect(created.status).toBe(201);
    expect(created.body.id).toBeTruthy();
    expect(created.body["userName"]).toBe(body["userName"]);
    expect(created.body["externalId"]).toBe(body["externalId"]);
    expect(created.body[EXTENSION]).toMatchObject({
      identityType: "Staff",
      schoolOrHq: "School",
      identitySource: "HRPS",
    });
    expect(created.body.schemas).toContain(EXTENSION);

    const duplicate = await call("POST", "/Users", userBody({ userName: body["userName"] }));
    expect(duplicate.status).toBe(409);
  },
);

testCase(
  "5",
  "Get User by ID",
  ["A valid id returns the user; an unknown id returns 404 carrying a SCIM error body."],
  async () => {
    const user = await createUser();

    const found = await call<ScimResource>("GET", `/Users/${user.id}`);
    expect(found.status).toBe(200);
    expect(found.body.id).toBe(user.id);

    const missing = await call("GET", "/Users/00000000-0000-0000-0000-000000000000");
    expect(missing.status).toBe(404);
    expect(missing.body.schemas).toContain(SCHEMA_ERROR);
  },
);

testCase(
  "6",
  "Get All Users",
  [
    "ListResponse envelope with totalResults; startIndex and count honoured; userName eq filters.",
    "An attribute Edupass does not specify is refused with invalidFilter rather than answered as a match-everything.",
  ],
  async () => {
    const user = await createUser();

    const all = await call("GET", "/Users");
    expect(all.status).toBe(200);
    expect(all.body.schemas).toContain(SCHEMA_LIST);
    expect(typeof all.body.totalResults).toBe("number");

    const paged = await call("GET", "/Users?startIndex=1&count=1");
    expect(paged.status).toBe(200);
    expect(paged.body.Resources).toHaveLength(1);

    const filtered = await call(
      "GET",
      `/Users?filter=${encodeURIComponent(`userName eq "${user.userName}"`)}`,
    );
    expect(filtered.status).toBe(200);
    expect(filtered.body.Resources).toHaveLength(1);

    const unsupported = await call(
      "GET",
      `/Users?filter=${encodeURIComponent('displayName eq "anything"')}`,
    );
    expect(unsupported.status).toBe(400);
    expect(unsupported.body.scimType).toBe("invalidFilter");
  },
);

testCase(
  "7",
  "Update User informational fields - PUT",
  [
    "200 with the updated resource; informational attributes changed; group membership untouched.",
    "Membership is held on the group and derived onto the user on read, so a user-level replace cannot disturb it.",
  ],
  async () => {
    const user = await createUser();
    const displayName = locationGroup("1007");
    await createGroup(displayName, [{ value: user.id }]);

    expect(await locationsOf(user.id)).toEqual([displayName]);

    const replacement = `renamed.${unique("r")}@moe.edu.sg`;
    const replaced = await call<ScimResource>("PUT", `/Users/${user.id}`, {
      ...userBody({ userName: replacement }),
      id: user.id,
      displayName: "Renamed Teacher",
      title: "Senior Teacher",
    });

    expect(replaced.status).toBe(200);
    expect(replaced.body["userName"]).toBe(replacement);
    expect(replaced.body["displayName"]).toBe("Renamed Teacher");
    expect(replaced.body["title"]).toBe("Senior Teacher");

    expect(await locationsOf(user.id)).toEqual([displayName]);
  },
);

testCase(
  "8",
  "Update User informational fields - PATCH",
  [
    "The patch applies to the named attributes only; group membership untouched.",
    "Membership is held on the group and derived onto the user on read, so a user-level patch cannot disturb it.",
  ],
  async () => {
    const user = await createUser();
    const displayName = locationGroup("1008");
    await createGroup(displayName, [{ value: user.id }]);

    const patched = await call(
      "PATCH",
      `/Users/${user.id}`,
      patch(
        { op: "replace", path: "displayName", value: "Patched Teacher" },
        { op: "replace", path: "title", value: "Head of Department" },
      ),
    );
    expect(PATCH_APPLIED).toContain(patched.status);

    const read = await call<ScimResource>("GET", `/Users/${user.id}`);
    expect(read.body["displayName"]).toBe("Patched Teacher");
    expect(read.body["title"]).toBe("Head of Department");

    expect(await locationsOf(user.id)).toEqual([displayName]);
  },
);

testCase(
  "9",
  "Update Group Membership (Patch) - School User Added to School",
  [
    "The member is added and the user's groups attribute reports the location on the next read.",
    FimsScope,
  ],
  async () => {
    const user = await createUser();
    const displayName = locationGroup("1009");
    const group = await createGroup(displayName);

    const added = await call(
      "PATCH",
      `/Groups/${group.id}`,
      patch({ op: "add", path: "members", value: [{ value: user.id }] }),
    );
    expect(PATCH_APPLIED).toContain(added.status);

    expect(await locationsOf(user.id)).toEqual([displayName]);
  },
);

testCase(
  "10",
  "Update Group Membership (Patch) - School User Added to Multiple Schools",
  [
    "Both locations are reported on the user; adding the second does not displace the first.",
    FimsScope,
  ],
  async () => {
    const user = await createUser();
    const first = locationGroup("1010");
    const second = locationGroup("2010");

    const groupOne = await createGroup(first);
    const groupTwo = await createGroup(second);

    for (const group of [groupOne, groupTwo]) {
      const added = await call(
        "PATCH",
        `/Groups/${group.id}`,
        patch({ op: "add", path: "members", value: [{ value: user.id }] }),
      );
      expect(PATCH_APPLIED).toContain(added.status);
    }

    expect(await locationsOf(user.id)).toEqual([first, second].sort());
  },
);

testCase(
  "11",
  "Update Group Membership (Patch) - School User Changes School",
  [
    "After the move only the new location is reported; the old membership is gone.",
    "The specification's 'positions overwrite' is FIMS-side. At the endpoint a move is a remove from one group and an add to another.",
  ],
  async () => {
    const user = await createUser();
    const from = locationGroup("1011");
    const to = locationGroup("2011");

    const groupFrom = await createGroup(from, [{ value: user.id }]);
    const groupTo = await createGroup(to);

    expect(await locationsOf(user.id)).toEqual([from]);

    const removed = await call(
      "PATCH",
      `/Groups/${groupFrom.id}`,
      patch({ op: "remove", path: "members", value: [{ value: user.id }] }),
    );
    expect(PATCH_APPLIED).toContain(removed.status);

    const added = await call(
      "PATCH",
      `/Groups/${groupTo.id}`,
      patch({ op: "add", path: "members", value: [{ value: user.id }] }),
    );
    expect(PATCH_APPLIED).toContain(added.status);

    expect(await locationsOf(user.id)).toEqual([to]);
  },
);

testCase(
  "12",
  "Update User (Patch) - School User Added to School",
  [
    "Refused 400 invalidPath: groups is read-only on the User resource.",
    "RFC 7643 4.1.2 makes groups readOnly - membership is written on the Group and derived onto the User. " +
      "Edupass must send this as a Group membership patch (case 9), which is what the provider then reflects.",
  ],
  async () => {
    const user = await createUser();
    const group = await createGroup(locationGroup("1012"));

    const refused = await call(
      "PATCH",
      `/Users/${user.id}`,
      patch({ op: "add", path: "groups", value: [{ value: group.id }] }),
    );

    expect(refused.status).toBe(400);
    expect(refused.body.scimType).toBe("invalidPath");
    expect(await locationsOf(user.id)).toEqual([]);
  },
);

testCase(
  "13",
  "Update User (Patch) - School User Added to Multiple Schools",
  [
    "Refused 400 invalidPath, as for a single location: groups is read-only on the User resource.",
    "The multi-location case adds nothing at the endpoint - the path is refused before the values are looked at.",
  ],
  async () => {
    const user = await createUser();
    const groupOne = await createGroup(locationGroup("1013"));
    const groupTwo = await createGroup(locationGroup("2013"));

    const refused = await call(
      "PATCH",
      `/Users/${user.id}`,
      patch({
        op: "add",
        path: "groups",
        value: [{ value: groupOne.id }, { value: groupTwo.id }],
      }),
    );

    expect(refused.status).toBe(400);
    expect(refused.body.scimType).toBe("invalidPath");
    expect(await locationsOf(user.id)).toEqual([]);
  },
);

testCase(
  "14",
  "Update User (Patch) - School User Changes School",
  [
    "Refused 400 invalidPath, and the existing membership is left exactly as it was.",
    "A refused patch that had nonetheless dropped the old location would be the worst outcome; it does not.",
  ],
  async () => {
    const user = await createUser();
    const from = locationGroup("1014");
    await createGroup(from, [{ value: user.id }]);
    const groupTo = await createGroup(locationGroup("2014"));

    const refused = await call(
      "PATCH",
      `/Users/${user.id}`,
      patch({ op: "replace", path: "groups", value: [{ value: groupTo.id }] }),
    );

    expect(refused.status).toBe(400);
    expect(refused.body.scimType).toBe("invalidPath");
    expect(await locationsOf(user.id)).toEqual([from]);
  },
);

testCase(
  "15",
  "Update Group Membership (Patch) - PATCH Replace Multiple Location Codes",
  [
    "Removing one location leaves the other in place; only the named membership goes.",
    "FIMS removes the positions tagged to the removed location code; at the endpoint the observable half is which memberships survive.",
  ],
  async () => {
    const user = await createUser();
    const kept = locationGroup("1015");
    const dropped = locationGroup("2015");

    await createGroup(kept, [{ value: user.id }]);
    const groupDropped = await createGroup(dropped, [{ value: user.id }]);

    expect(await locationsOf(user.id)).toEqual([kept, dropped].sort());

    const removed = await call(
      "PATCH",
      `/Groups/${groupDropped.id}`,
      patch({ op: "remove", path: "members", value: [{ value: user.id }] }),
    );
    expect(PATCH_APPLIED).toContain(removed.status);

    expect(await locationsOf(user.id)).toEqual([kept]);
  },
);

testCase(
  "16",
  "Update Group Membership (Patch) - School User Leaves MOE (Single Location)",
  [
    "The last location is removed and the user reports no groups; marking the user inactive is a separate, explicit write.",
    "The provider does not deactivate a user for having no memberships - SCIM defines no such rule, and inferring it would " +
      "deactivate accounts on an ordinary membership edit. Edupass sends active=false, which is checked here.",
  ],
  async () => {
    const user = await createUser();
    const displayName = locationGroup("1016");
    const group = await createGroup(displayName, [{ value: user.id }]);

    expect(await locationsOf(user.id)).toEqual([displayName]);

    const removed = await call(
      "PATCH",
      `/Groups/${group.id}`,
      patch({ op: "remove", path: "members", value: [{ value: user.id }] }),
    );
    expect(PATCH_APPLIED).toContain(removed.status);
    expect(await locationsOf(user.id)).toEqual([]);

    const deactivated = await call(
      "PATCH",
      `/Users/${user.id}`,
      patch({ op: "replace", path: "active", value: false }),
    );
    expect(PATCH_APPLIED).toContain(deactivated.status);

    const read = await call<ScimResource>("GET", `/Users/${user.id}`);
    expect(read.body["active"]).toBe(false);
  },
);

testCase(
  "17",
  "Update Group Membership (Patch) - User Reactivation",
  [
    "A user with no location is added to one and re-activated; both the membership and active are observable afterwards.",
    "Unbanning and the position add on UPA approval are FIMS-side.",
  ],
  async () => {
    const user = await createUser({ active: false });
    expect(await locationsOf(user.id)).toEqual([]);

    const displayName = locationGroup("1017");
    const group = await createGroup(displayName);

    const added = await call(
      "PATCH",
      `/Groups/${group.id}`,
      patch({ op: "add", path: "members", value: [{ value: user.id }] }),
    );
    expect(PATCH_APPLIED).toContain(added.status);

    const reactivated = await call(
      "PATCH",
      `/Users/${user.id}`,
      patch({ op: "replace", path: "active", value: true }),
    );
    expect(PATCH_APPLIED).toContain(reactivated.status);

    const read = await call<ScimResource>("GET", `/Users/${user.id}`);
    expect(read.body["active"]).toBe(true);
    expect(await locationsOf(user.id)).toEqual([displayName]);
  },
);

testCase(
  "18",
  "Update User (Patch) - PATCH Replace Multiple Location Codes",
  [
    "Refused 400 invalidPath; both existing memberships survive untouched.",
    "As for cases 12 to 14: location changes are written on the Group, not on the User.",
  ],
  async () => {
    const user = await createUser();
    const first = locationGroup("1018");
    const second = locationGroup("2018");
    await createGroup(first, [{ value: user.id }]);
    const groupSecond = await createGroup(second, [{ value: user.id }]);

    const refused = await call(
      "PATCH",
      `/Users/${user.id}`,
      patch({ op: "replace", path: "groups", value: [{ value: groupSecond.id }] }),
    );

    expect(refused.status).toBe(400);
    expect(refused.body.scimType).toBe("invalidPath");
    expect(await locationsOf(user.id)).toEqual([first, second].sort());
  },
);

testCase(
  "19",
  "Update User (Patch) - School User Leaves MOE (Single Location)",
  [
    "active=false applies on the User; removing the location does not, and is refused 400 invalidPath.",
    "The two halves of 'leaves MOE' land on different resources: the flag on the User, the membership on the Group.",
  ],
  async () => {
    const user = await createUser();
    const displayName = locationGroup("1019");
    await createGroup(displayName, [{ value: user.id }]);

    const refused = await call(
      "PATCH",
      `/Users/${user.id}`,
      patch({ op: "remove", path: "groups" }),
    );
    expect(refused.status).toBe(400);
    expect(refused.body.scimType).toBe("invalidPath");
    expect(await locationsOf(user.id)).toEqual([displayName]);

    const deactivated = await call(
      "PATCH",
      `/Users/${user.id}`,
      patch({ op: "replace", path: "active", value: false }),
    );
    expect(PATCH_APPLIED).toContain(deactivated.status);

    const read = await call<ScimResource>("GET", `/Users/${user.id}`);
    expect(read.body["active"]).toBe(false);
  },
);

testCase(
  "20",
  "Update User (Patch) - User Reactivation",
  [
    "active=true applies on the User; the location add is refused 400 invalidPath and must go to the Group.",
    "Unbanning and the position add on UPA approval are FIMS-side.",
  ],
  async () => {
    const user = await createUser({ active: false });
    const group = await createGroup(locationGroup("1020"));

    const refused = await call(
      "PATCH",
      `/Users/${user.id}`,
      patch({ op: "add", path: "groups", value: [{ value: group.id }] }),
    );
    expect(refused.status).toBe(400);

    const reactivated = await call(
      "PATCH",
      `/Users/${user.id}`,
      patch({ op: "replace", path: "active", value: true }),
    );
    expect(PATCH_APPLIED).toContain(reactivated.status);

    const read = await call<ScimResource>("GET", `/Users/${user.id}`);
    expect(read.body["active"]).toBe(true);
  },
);

testCase(
  "21",
  "Delete User",
  [
    "204 on delete; the user is gone on the next read and is removed from every group that listed them.",
    "UPA cancellation, position clearing and the notification are FIMS-side.",
  ],
  async () => {
    const user = await createUser();
    const displayName = locationGroup("1021");
    const group = await createGroup(displayName, [{ value: user.id }]);

    expect(await locationsOf(user.id)).toEqual([displayName]);

    const deleted = await call("DELETE", `/Users/${user.id}`);
    expect(deleted.status).toBe(204);

    const missing = await call("GET", `/Users/${user.id}`);
    expect(missing.status).toBe(404);

    // The membership must go with the user: a group still naming a deleted user would
    // hand that identifier back to Edupass on the next read.
    const read = await call<ScimResource>("GET", `/Groups/${group.id}`);
    const members = (read.body["members"] as { value: string }[] | undefined) ?? [];
    expect(members.map((item) => item.value)).not.toContain(user.id);
  },
);

testCase(
  "22",
  "Delete Group",
  [
    "204 on delete; the group is gone on the next read and no longer appears on its members.",
    "Deleting the group is what removes the application role it encodes from everyone who held it.",
  ],
  async () => {
    const user = await createUser();
    const displayName = locationGroup("1022");
    const group = await createGroup(displayName, [{ value: user.id }]);

    expect(await locationsOf(user.id)).toEqual([displayName]);

    const deleted = await call("DELETE", `/Groups/${group.id}`);
    expect(deleted.status).toBe(204);

    const missing = await call("GET", `/Groups/${group.id}`);
    expect(missing.status).toBe(404);

    expect(await locationsOf(user.id)).toEqual([]);
  },
);

testCase(
  "23",
  "Invalid Group Membership",
  [
    "Adding to a group that does not exist is 404; removing a user who is not a member succeeds as a no-op.",
    "The asymmetry is deliberate and correct: the first names a resource that is not there, the second asks for a state that already holds.",
  ],
  async () => {
    const user = await createUser();

    const missingGroup = await call(
      "PATCH",
      "/Groups/00000000-0000-0000-0000-000000000000",
      patch({ op: "add", path: "members", value: [{ value: user.id }] }),
    );
    expect(missingGroup.status).toBe(404);

    const group = await createGroup(locationGroup("1023"));
    const removeNonMember = await call(
      "PATCH",
      `/Groups/${group.id}`,
      patch({ op: "remove", path: "members", value: [{ value: user.id }] }),
    );
    expect(PATCH_APPLIED).toContain(removeNonMember.status);

    const read = await call<ScimResource>("GET", `/Groups/${group.id}`);
    const members = (read.body["members"] as { value: string }[] | undefined) ?? [];
    expect(members).toHaveLength(0);
  },
);

testCase(
  "24",
  "User Update with Existing Location Code (No Change)",
  [
    "A patch that sets the values already held succeeds and leaves the resource as it was.",
    "FIMS detects no change and skips UPA creation. SCIM has no equivalent - a no-op write is still a successful write - " +
      "so what is checked here is that it neither fails nor alters anything.",
  ],
  async () => {
    const user = await createUser();
    const displayName = locationGroup("1024");
    await createGroup(displayName, [{ value: user.id }]);

    const before = await call<ScimResource>("GET", `/Users/${user.id}`);

    const unchanged = await call(
      "PATCH",
      `/Users/${user.id}`,
      patch(
        { op: "replace", path: "title", value: before.body["title"] },
        { op: "replace", path: `${EXTENSION}:schoolOrHq`, value: "School" },
      ),
    );
    expect(PATCH_APPLIED).toContain(unchanged.status);

    const after = await call<ScimResource>("GET", `/Users/${user.id}`);
    expect(after.body["title"]).toBe(before.body["title"]);
    expect((after.body[EXTENSION] as any).schoolOrHq).toBe("School");
    expect(await locationsOf(user.id)).toEqual([displayName]);
  },
);

testCase(
  "25",
  "User Update with Existing Group (No Change)",
  [
    "Adding a member who is already a member succeeds and does not duplicate the membership.",
    "FIMS detects no change and skips UPA creation. At the endpoint the observable requirement is idempotence: " +
      "one membership, not two.",
  ],
  async () => {
    const user = await createUser();
    const displayName = locationGroup("1025");
    const group = await createGroup(displayName, [{ value: user.id }]);

    const again = await call(
      "PATCH",
      `/Groups/${group.id}`,
      patch({ op: "add", path: "members", value: [{ value: user.id }] }),
    );
    expect(PATCH_APPLIED).toContain(again.status);

    const read = await call<ScimResource>("GET", `/Groups/${group.id}`);
    const members = (read.body["members"] as { value: string }[] | undefined) ?? [];
    expect(members.filter((item) => item.value === user.id)).toHaveLength(1);

    expect(await locationsOf(user.id)).toEqual([displayName]);
  },
);
