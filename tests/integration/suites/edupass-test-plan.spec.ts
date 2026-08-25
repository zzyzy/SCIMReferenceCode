import { afterAll, expect, it } from "vitest";
import { PATCH_APPLIED, SCHEMA_ERROR, SCHEMA_GROUP, SCHEMA_LIST, SCHEMA_PATCH, SCHEMA_USER, devToken, unique, type ScimResource } from "../src/client.js";
import { DEV_AUDIENCE, DEV_ISSUER, EDUPASS_STRICT_BASE_URL } from "../src/host.js";
import { beginCase, call, endCase, summary, writeResults } from "../src/test-plan-recorder.js";

/**
 * The two Edupass plans, run against the Edupass host and written back out as a CSV in the
 * plan's own shape:
 *
 * - the 25 cases of test-plan.xlsx, numbered 1 to 25 as the sheet numbers them;
 * - the cases of "M2-SCIM RP Testcases", the suite Edupass itself runs against a relying
 *   party, prefixed `RP-` and otherwise keeping the document's own labels (`RP-JWT-1` to
 *   `RP-JWT-5` for its JWT Authentication tests, `RP-1a`, `RP-1`, `RP-2a` and so on for its
 *   SCIM Operation tests, with `RP-0` for the setup and pre-clean step it describes).
 *
 * The prefix is only to keep the two numbering schemes apart in one CSV; nothing else about
 * the RP cases departs from the document.
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
 *
 * ## Where the RP cases send their requests
 *
 * The SCIM Operation cases go to the ordinary Edupass host, like every case above them.
 * The JWT Authentication cases go to a second Edupass host started with
 * SCIM_ENFORCE_JWT=1, because the sample turns issuer, audience, lifetime and
 * signing-key validation off in Development - and Development is the only environment the
 * harness can start a host in, the Release branch resolving its keys over OIDC metadata.
 * Without that host, three of the five cases would be checking a bypass rather than a
 * rejection. Both hosts run the same provider; the CSV records the full URL whenever a
 * case leaves the ordinary one.
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

/* ---------------------------------------------------------------------------------------
 * "M2-SCIM RP Testcases" - the suite Edupass runs against a relying party.
 * ------------------------------------------------------------------------------------- */

/**
 * The plan's `<APPCODE>` - the relying party's app code, which its group names embed.
 *
 * The reference host has no app code of its own (the sample's JWT audience is
 * `Microsoft.Security.Bearer`, which is not one), so a fixed stand-in is used. The plan's
 * own example is `RBS`; only the shape of the name matters to the cases.
 */
const APP_CODE = "SCIM";

const USER_ONE = "999900000001";
const USER_TWO = "999900000002";
const USER_DUPLICATE = "999900000099";

const GROUP_ONE = `X_${APP_CODE}_TEST1`;
const GROUP_TWO = `X_${APP_CODE}_TEST2`;
const GROUP_DUPLICATE = `X_${APP_CODE}_DUP`;

/** Common to the JWT cases: which host the request went to, and why it had to. */
const StrictHost =
  "Sent to the Edupass host started with SCIM_ENFORCE_JWT=1. The sample disables issuer, " +
  "audience, lifetime and signing-key validation in Development, so on the ordinary host " +
  "this token would be accepted; that bypass is what the second host exists to lift.";

/**
 * The plan's resources, carried between the plan's cases.
 *
 * The RP plan is one ordered sequence - it creates in RP-1 and deletes in RP-7 - so the ids
 * have to outlive a single case. `fileParallelism: false` and vitest's in-file ordering are
 * what make that safe.
 */
const rp: { userOne?: string; userTwo?: string; groupOne?: string; groupTwo?: string } = {};

function required(value: string | undefined, what: string): string {
  if (!value) {
    throw new Error(`${what} was not captured: the case that creates it did not pass`);
  }

  return value;
}

function filterFor(expression: string): string {
  return `?filter=${encodeURIComponent(expression)}`;
}

function resourcesOf(body: ScimResource): ScimResource[] {
  return (body["Resources"] as ScimResource[] | undefined) ?? [];
}

/** The ids of the groups a user is projected into. */
async function groupIdsOf(identifier: string): Promise<string[]> {
  const read = await call<ScimResource>("GET", `/Users/${identifier}`);
  const groups = (read.body["groups"] as { value: string }[] | undefined) ?? [];
  return groups.map((item) => item.value);
}

function base64Url(value: string): string {
  return Buffer.from(value)
    .toString("base64")
    .replace(/=+$/u, "")
    .replace(/\+/gu, "-")
    .replace(/\//gu, "_");
}

/** The plan's token: signed with the Edupass key, and its stated 900s TTL. */
function planToken(overrides: Record<string, unknown> = {}): string {
  const now = Math.floor(Date.now() / 1000);
  return devToken({ nbf: now, exp: now + 900, ...overrides });
}

/**
 * The plan's expired token.
 *
 * An hour past expiry rather than a second, so that the five-minute clock skew the JWT
 * middleware allows by default cannot make a genuinely expired token look current.
 */
function expiredToken(): string {
  const now = Math.floor(Date.now() / 1000);
  return devToken({ nbf: now - 7200, exp: now - 3600 });
}

/** The plan's `alg: none` token: a well-formed JWT with the signature taken off. */
function unsignedToken(): string {
  const now = Math.floor(Date.now() / 1000);
  const header = base64Url(JSON.stringify({ alg: "none", typ: "JWT" }));
  const payload = base64Url(
    JSON.stringify({ iss: DEV_ISSUER, aud: DEV_AUDIENCE, nbf: now, exp: now + 900 }),
  );
  return `${header}.${payload}.`;
}

function bearer(token: string): Record<string, string> {
  return { Authorization: `Bearer ${token}` };
}

/** The plan's user body. */
function planUserBody(userName: string, identitySource: string): Record<string, unknown> {
  return {
    schemas: [SCHEMA_USER, EXTENSION],
    userName,
    externalId: userName,
    name: { formatted: `Test User ${userName.slice(-3)}` },
    title: "Test Title",
    emails: [{ primary: true, type: "WOG", value: `test${userName.slice(-3)}@test.gov.sg` }],
    // uinFin is omitted deliberately, by the plan's own rule that a field the RP does not
    // declare at GET /Schemas is left out: this host's extension advertises identityType,
    // schoolOrHq and identitySource and nothing else. RP-0 checks that is still true.
    [EXTENSION]: { identitySource, identityType: "Staff", schoolOrHq: "School" },
  };
}

function planGroupBody(displayName: string): Record<string, unknown> {
  return { schemas: [SCHEMA_GROUP], externalId: displayName, displayName };
}

/** The plan's pre-clean: delete whatever a previous interrupted run left behind. */
async function preclean(): Promise<void> {
  for (const userName of [USER_ONE, USER_TWO, USER_DUPLICATE]) {
    const found = await call<ScimResource>(
      "GET",
      `/Users${filterFor(`userName eq "${userName}"`)}`,
    );
    expect(found.status).toBe(200);

    for (const resource of resourcesOf(found.body)) {
      expect((await call("DELETE", `/Users/${resource.id}`)).status).toBe(204);
    }
  }

  for (const displayName of [GROUP_ONE, GROUP_TWO, GROUP_DUPLICATE]) {
    const found = await call<ScimResource>(
      "GET",
      `/Groups${filterFor(`displayName eq "${displayName}"`)}`,
    );
    expect(found.status).toBe(200);

    for (const resource of resourcesOf(found.body)) {
      expect((await call("DELETE", `/Groups/${resource.id}`)).status).toBe(204);
    }
  }
}

/**
 * Everything the service holds under `attribute`, starting from the plan's own bare list.
 *
 * The plan asks whether one resource is absent and another still present, and puts that
 * question to GET /Groups and GET /Users. A single page can answer it only if the page is
 * the whole collection - and it need not be: this host caps a page at 200, and the rest of
 * the integration suite leaves far more than that behind on the shared Edupass host. So the
 * plan's request goes first, and is recorded, and then the remaining pages are followed
 * before the question is answered. Absent from a truncated page is not absent.
 */
async function everyValueOf(collection: string, attribute: string): Promise<string[]> {
  const first = await call<ScimResource>("GET", `/${collection}`);
  expect(first.status).toBe(200);

  const total = first.body["totalResults"] as number;
  const values = resourcesOf(first.body).map((resource) => String(resource[attribute]));

  while (values.length < total) {
    const page = await call<ScimResource>(
      "GET",
      `/${collection}?startIndex=${values.length + 1}&count=200`,
    );
    expect(page.status).toBe(200);

    const resources = resourcesOf(page.body);
    // A page that came back empty would otherwise loop for ever.
    expect(resources.length).toBeGreaterThan(0);

    values.push(...resources.map((resource) => String(resource[attribute])));
  }

  return values;
}

/**
 * Exactly one of two simultaneous creates succeeded and exactly one was refused 409.
 *
 * The plan's wording, and the point of it: not both accepted, which would mean a duplicate
 * got in, and not both refused, which would mean neither did.
 */
function expectOneWinner(responses: { status: number }[]): void {
  expect(responses.filter((item) => item.status >= 200 && item.status < 300)).toHaveLength(1);
  expect(responses.filter((item) => item.status === 409)).toHaveLength(1);
}

testCase(
  "RP-0",
  "Setup - read advertised schemas, pre-clean the plan's fixed identifiers",
  [
    "The Group schema is advertised, so the plan's group cases run; uinFin is not, so the " +
      "plan's rule omits it from every body. The plan's fixed identifiers are left clear.",
    "A valid token goes to the strict host as well, so that the five rejections in RP-JWT-1 " +
      "to RP-JWT-5 are rejections of the token and not of the host.",
  ],
  async () => {
    const schemas = await call<ScimResource>("GET", "/Schemas");
    expect(schemas.status).toBe(200);
    expect(schemas.body.schemas).toContain(SCHEMA_LIST);

    const advertised = resourcesOf(schemas.body);
    const ids = advertised.map((resource) => resource.id);
    expect(ids).toContain(SCHEMA_USER);
    expect(ids).toContain(SCHEMA_GROUP);
    expect(ids).toContain(EXTENSION);

    const extension = advertised.find((resource) => resource.id === EXTENSION);
    const attributes = (extension?.["attributes"] as { name: string }[] | undefined) ?? [];
    expect(attributes.map((attribute) => attribute.name)).not.toContain("uinFin");

    await preclean();

    const accepted = await call("GET", "/Users", undefined, {
      base: EDUPASS_STRICT_BASE_URL,
      headers: bearer(planToken()),
    });
    expect(accepted.status).toBe(200);
  },
);

testCase(
  "RP-JWT-1",
  "JWT Authentication - no auth token",
  ["401: the request carries no Authorization header at all.", StrictHost],
  async () => {
    const refused = await call("GET", "/Users", undefined, {
      base: EDUPASS_STRICT_BASE_URL,
      anonymous: true,
    });
    expect(refused.status).toBe(401);
  },
);

testCase(
  "RP-JWT-2",
  "JWT Authentication - unsigned JWT",
  [
    "401: a well-formed token declaring alg: none with an empty signature is refused.",
    "The one case of the five the ordinary Edupass host refuses as well: a signature is " +
      "required whatever the validation flags say.",
  ],
  async () => {
    const refused = await call("GET", "/Users", undefined, {
      base: EDUPASS_STRICT_BASE_URL,
      headers: bearer(unsignedToken()),
    });
    expect(refused.status).toBe(401);
  },
);

testCase(
  "RP-JWT-3",
  "JWT Authentication - expired token",
  ["401: correctly signed, but its exp is an hour in the past.", StrictHost],
  async () => {
    const refused = await call("GET", "/Users", undefined, {
      base: EDUPASS_STRICT_BASE_URL,
      headers: bearer(expiredToken()),
    });
    expect(refused.status).toBe(401);
  },
);

testCase(
  "RP-JWT-4",
  "JWT Authentication - invalid issuer",
  ["401: correctly signed, but iss is not the Edupass issuer.", StrictHost],
  async () => {
    const refused = await call("GET", "/Users", undefined, {
      base: EDUPASS_STRICT_BASE_URL,
      headers: bearer(planToken({ iss: "https://not-the-edupass-issuer.example" })),
    });
    expect(refused.status).toBe(401);
  },
);

testCase(
  "RP-JWT-5",
  "JWT Authentication - invalid audience",
  ["401: correctly signed, but aud is not the RP's appCode.", StrictHost],
  async () => {
    const refused = await call("GET", "/Users", undefined, {
      base: EDUPASS_STRICT_BASE_URL,
      headers: bearer(planToken({ aud: `NOT_${APP_CODE}` })),
    });
    expect(refused.status).toBe(401);
  },
);

testCase(
  "RP-1a",
  "Concurrent duplicate user",
  [
    "Two simultaneous creates of the same user: exactly one 2xx and exactly one 409. " +
      "Neither both accepted nor both refused. The winner is deleted again.",
    "What this is for, in the plan's words: whether the RP's server is safe under " +
      "concurrent duplicate requests, which a network retry can produce.",
  ],
  async () => {
    const body = planUserBody(USER_DUPLICATE, "HRPS");
    const responses = await Promise.all([
      call<ScimResource>("POST", "/Users", body),
      call<ScimResource>("POST", "/Users", body),
    ]);

    expectOneWinner(responses);

    const winner = responses.find((item) => item.status >= 200 && item.status < 300)!;
    expect((await call("DELETE", `/Users/${winner.body.id}`)).status).toBe(204);
  },
);

testCase(
  "RP-1",
  "Create User",
  [
    "Both users are created, and each is then found by a userName eq filter as exactly one " +
      "result carrying the matching externalId.",
    "The two differ in identitySource - HRPS and MIMS - as the plan's two bodies do.",
  ],
  async () => {
    const one = await call<ScimResource>("POST", "/Users", planUserBody(USER_ONE, "HRPS"));
    expect(one.status).toBe(201);
    rp.userOne = one.body.id;

    const two = await call<ScimResource>("POST", "/Users", planUserBody(USER_TWO, "MIMS"));
    expect(two.status).toBe(201);
    rp.userTwo = two.body.id;

    for (const userName of [USER_ONE, USER_TWO]) {
      const found = await call<ScimResource>(
        "GET",
        `/Users${filterFor(`userName eq "${userName}"`)}`,
      );
      expect(found.status).toBe(200);

      const resources = resourcesOf(found.body);
      expect(resources).toHaveLength(1);
      expect(resources[0]!["externalId"]).toBe(userName);
    }
  },
);

testCase(
  "RP-2",
  "Update User",
  ["The replace takes, and the next read returns the new name.formatted."],
  async () => {
    const id = required(rp.userOne, "user 1");

    const replaced = await call<ScimResource>("PUT", `/Users/${id}`, {
      ...planUserBody(USER_ONE, "HRPS"),
      id,
      name: { formatted: "Test User 001 Updated" },
    });
    expect(replaced.status).toBe(200);

    const read = await call<ScimResource>("GET", `/Users/${id}`);
    expect(read.status).toBe(200);
    expect((read.body["name"] as { formatted?: string }).formatted).toBe("Test User 001 Updated");
  },
);

testCase(
  "RP-2a",
  "Get non-existent User",
  ["404 carrying a SCIM error body, for an identifier that is not a server-assigned id."],
  async () => {
    const missing = await call("GET", "/Users/non-existent-user-id");
    expect(missing.status).toBe(404);
    expect(missing.body.schemas).toContain(SCHEMA_ERROR);
  },
);

testCase(
  "RP-2b",
  "Update non-existent User",
  [
    "404: the id in the path is what is not found, and it is looked up before the body is " +
      "considered.",
    "The body carries a userName no user holds, so that a 409 on a duplicate userName " +
      "cannot be mistaken for the 404 the case is about.",
  ],
  async () => {
    const missing = await call("PUT", "/Users/non-existent-user-id", {
      ...planUserBody("999900000098", "HRPS"),
      id: "non-existent-user-id",
    });
    expect(missing.status).toBe(404);
    expect(missing.body.schemas).toContain(SCHEMA_ERROR);
  },
);

testCase(
  "RP-3a",
  "Concurrent duplicate group",
  [
    "Two simultaneous creates of the same group: exactly one 2xx and exactly one 409. The " +
      "winner is deleted again.",
    "The group half of RP-1a, and for the same reason.",
  ],
  async () => {
    const body = planGroupBody(GROUP_DUPLICATE);
    const responses = await Promise.all([
      call<ScimResource>("POST", "/Groups", body),
      call<ScimResource>("POST", "/Groups", body),
    ]);

    expectOneWinner(responses);

    const winner = responses.find((item) => item.status >= 200 && item.status < 300)!;
    expect((await call("DELETE", `/Groups/${winner.body.id}`)).status).toBe(204);
  },
);

testCase(
  "RP-3",
  "Create Group",
  [
    "Both groups are created, and each is then found by a displayName eq filter as exactly " +
      "one result carrying the matching externalId.",
  ],
  async () => {
    const one = await call<ScimResource>("POST", "/Groups", planGroupBody(GROUP_ONE));
    expect(one.status).toBe(201);
    rp.groupOne = one.body.id;

    const two = await call<ScimResource>("POST", "/Groups", planGroupBody(GROUP_TWO));
    expect(two.status).toBe(201);
    rp.groupTwo = two.body.id;

    for (const displayName of [GROUP_ONE, GROUP_TWO]) {
      const found = await call<ScimResource>(
        "GET",
        `/Groups${filterFor(`displayName eq "${displayName}"`)}`,
      );
      expect(found.status).toBe(200);

      const resources = resourcesOf(found.body);
      expect(resources).toHaveLength(1);
      expect(resources[0]!["externalId"]).toBe(displayName);
    }
  },
);

testCase("RP-3b", "Get non-existent Group", ["404 carrying a SCIM error body."], async () => {
  const missing = await call("GET", "/Groups/non-existent-group-id");
  expect(missing.status).toBe(404);
  expect(missing.body.schemas).toContain(SCHEMA_ERROR);
});

testCase(
  "RP-3c",
  "Update memberships for non-existent Group",
  [
    "404: a membership patch names the group in the path, so an unknown group is not found " +
      "rather than silently accepted.",
  ],
  async () => {
    const missing = await call(
      "PATCH",
      "/Groups/non-existent-group-id",
      patch({ op: "add", path: "members", value: [{ value: required(rp.userOne, "user 1") }] }),
    );
    expect(missing.status).toBe(404);
  },
);

testCase(
  "RP-4",
  "Add Group membership",
  [
    "Both users are added in one patch, and the result is verified from both ends: each " +
      "user's groups carries the group, and the group's members carries both users.",
    "Verifying only one side would pass on a provider that wrote the membership but could " +
      "not project it, which is the failure Edupass would meet first.",
  ],
  async () => {
    const groupId = required(rp.groupOne, "group 1");
    const userOne = required(rp.userOne, "user 1");
    const userTwo = required(rp.userTwo, "user 2");

    const added = await call(
      "PATCH",
      `/Groups/${groupId}`,
      patch({ op: "add", path: "members", value: [{ value: userOne }, { value: userTwo }] }),
    );
    expect(PATCH_APPLIED).toContain(added.status);

    const group = await call<ScimResource>("GET", `/Groups/${groupId}`);
    const members = ((group.body["members"] as { value: string }[] | undefined) ?? []).map(
      (member) => member.value,
    );
    expect(members).toContain(userOne);
    expect(members).toContain(userTwo);

    expect(await groupIdsOf(userOne)).toContain(groupId);
    expect(await groupIdsOf(userTwo)).toContain(groupId);
  },
);

testCase(
  "RP-5",
  "Remove Group membership",
  [
    "A value-filtered remove path takes user 1 out and leaves user 2 in, on both the group " +
      "and the user. User 2 is then removed as well.",
    'The plan\'s path is members[value eq "{id}"], not a members value list: dropping the ' +
      "other member is the failure this case is looking for.",
  ],
  async () => {
    const groupId = required(rp.groupOne, "group 1");
    const userOne = required(rp.userOne, "user 1");
    const userTwo = required(rp.userTwo, "user 2");

    const removed = await call(
      "PATCH",
      `/Groups/${groupId}`,
      patch({ op: "remove", path: `members[value eq "${userOne}"]` }),
    );
    expect(PATCH_APPLIED).toContain(removed.status);

    const group = await call<ScimResource>("GET", `/Groups/${groupId}`);
    const members = ((group.body["members"] as { value: string }[] | undefined) ?? []).map(
      (member) => member.value,
    );
    expect(members).not.toContain(userOne);
    expect(members).toContain(userTwo);

    expect(await groupIdsOf(userOne)).not.toContain(groupId);
    expect(await groupIdsOf(userTwo)).toContain(groupId);

    const removedTwo = await call(
      "PATCH",
      `/Groups/${groupId}`,
      patch({ op: "remove", path: `members[value eq "${userTwo}"]` }),
    );
    expect(PATCH_APPLIED).toContain(removedTwo.status);
  },
);

testCase(
  "RP-6",
  "Delete Group",
  [
    "Group 1 is deleted and is absent from the next list, while group 2 is still there. " +
      "Group 2 is then deleted too, so the run leaves nothing behind.",
    "The plan's list is followed to its last page before absence is concluded: on the " +
      "shared host the first page need not be the whole collection.",
  ],
  async () => {
    const groupOne = required(rp.groupOne, "group 1");
    const groupTwo = required(rp.groupTwo, "group 2");

    expect((await call("DELETE", `/Groups/${groupOne}`)).status).toBe(204);

    const names = await everyValueOf("Groups", "displayName");
    expect(names).not.toContain(GROUP_ONE);
    expect(names).toContain(GROUP_TWO);

    expect((await call("DELETE", `/Groups/${groupTwo}`)).status).toBe(204);
  },
);

testCase(
  "RP-7",
  "Delete User",
  [
    "User 1 is deleted and is absent from the next list, while user 2 is still there. User " +
      "2 is then deleted too, so the run leaves nothing behind.",
    "The plan's list is followed to its last page before absence is concluded: on the " +
      "shared host the first page need not be the whole collection.",
  ],
  async () => {
    const userOne = required(rp.userOne, "user 1");
    const userTwo = required(rp.userTwo, "user 2");

    expect((await call("DELETE", `/Users/${userOne}`)).status).toBe(204);

    const names = await everyValueOf("Users", "userName");
    expect(names).not.toContain(USER_ONE);
    expect(names).toContain(USER_TWO);

    expect((await call("DELETE", `/Users/${userTwo}`)).status).toBe(204);
  },
);
