import { describe, expect, it } from "vitest";
import {
  SCHEMA_GROUP,
  SCHEMA_USER,
  authHeaders,
  edupass,
  patchOp,
  unique,
  type ScimResource,
} from "../src/client.js";
import { EDUPASS_BASE_URL, EDUPASS_LOCATION } from "../src/host.js";

/**
 * The Edupass conformance suite: the interface specification read as a contract.
 *
 * Distinct from edupass-test-plan.spec.ts, which walks the 25 rows of the delivered
 * test plan. That plan is a basic acceptance pass - it asks whether each endpoint can
 * be called and answers sensibly. This file asks the different question of whether the
 * response bodies are the ones the specification document actually describes, including
 * the parts the plan never inspects: what `/Schemas` and `/ResourceTypes` advertise,
 * and whether the cross-references between a User and its Groups are present and
 * resolvable.
 *
 * Scope rule: everything here is something the Edupass specification requires and
 * RFC 7643/7644 does not. A gap that is really the SCIM library's belongs in the SCIM
 * suites instead - resource-types.spec.ts, groups.spec.ts, protocol.spec.ts - because
 * fixing it there fixes it for every relying party, not only an Edupass one.
 *
 * Naming: each describe states what one clause of the specification requires, in a
 * sentence that stands on its own, rather than carrying the clause's number or its
 * heading. Both of those are the document's and the document is revised - a name built
 * on either points at nothing while still reading as though it points somewhere. The
 * same convention holds in scim-compliance.spec.ts.
 *
 * Served by the host started with SCIM_PROVIDER=edupass.
 */

const EXTENSION = "urn:ietf:params:scim:schemas:extension:Edupass:2.0:User";

interface AdvertisedAttribute {
  name: string;
  type?: string;
  multiValued?: boolean;
  returned?: string;
  mutability?: string;
  subAttributes?: AdvertisedAttribute[];
  referenceTypes?: string[];
}

interface AdvertisedSchema {
  id: string;
  name?: string;
  attributes?: AdvertisedAttribute[];
}

interface AdvertisedResourceType {
  id?: string;
  name?: string;
  endpoint?: string;
  schema?: string;
  schemaExtensions?: { schema: string; required: boolean }[];
}

async function advertisedSchemas(): Promise<AdvertisedSchema[]> {
  const response = await edupass("GET", "/Schemas");
  expect(response.status).toBe(200);
  return response.body.Resources as AdvertisedSchema[];
}

async function advertisedSchema(identifier: string): Promise<AdvertisedSchema> {
  const found = (await advertisedSchemas()).find((item) => item.id === identifier);
  expect(found, `/Schemas does not advertise ${identifier}`).toBeDefined();
  return found as AdvertisedSchema;
}

async function advertisedResourceTypes(): Promise<AdvertisedResourceType[]> {
  const response = await edupass("GET", "/ResourceTypes");
  expect(response.status).toBe(200);
  return response.body.Resources as AdvertisedResourceType[];
}

function attributeNames(schema: AdvertisedSchema): string[] {
  return (schema.attributes ?? []).map((attribute) => attribute.name);
}

function attribute(schema: AdvertisedSchema, name: string): AdvertisedAttribute {
  const found = (schema.attributes ?? []).find(
    (item) => item.name.toLowerCase() === name.toLowerCase(),
  );
  expect(found, `${schema.id} does not advertise ${name}`).toBeDefined();
  return found as AdvertisedAttribute;
}

function eduUser(overrides: Record<string, unknown> = {}): Record<string, unknown> {
  const userName = `${unique("conf")}@moe.edu.sg`;
  return {
    schemas: [SCHEMA_USER, EXTENSION],
    userName,
    externalId: unique("edupass-id"),
    active: true,
    title: "Teacher",
    name: { formatted: "Conformance Test User" },
    emails: [{ value: userName, type: "WOG", primary: true }],
    [EXTENSION]: { identityType: "Staff", schoolOrHq: "School", identitySource: "HRPS" },
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

async function createEduGroup(members?: { value: string }[]): Promise<ScimResource> {
  const response = await edupass<ScimResource>("POST", "/Groups", {
    schemas: [SCHEMA_GROUP],
    displayName: unique(`${EDUPASS_LOCATION}_app1_role`),
    externalId: unique("edupass-grp"),
    ...(members === undefined ? {} : { members }),
  });
  if (response.status !== 201) {
    throw new Error(`could not create an Edupass group: ${response.status} ${response.text}`);
  }
  return response.body;
}

interface GroupsEntry {
  value?: string;
  $ref?: string;
  display?: string;
}

function groupsOf(user: ScimResource): GroupsEntry[] | undefined {
  return user["groups"] as GroupsEntry[] | undefined;
}

// ---------------------------------------------------------------------------
// The response body against what schemas declares
// ---------------------------------------------------------------------------

describe("A response declares every schema whose attributes it carries", () => {
  // "Each URN identifies a schema that defines the structure of the attributes present
  // in the JSON response", and the specification's table of possible values for schemas
  // lists only the core User schema and the Edupass extension. EduPassUser derives from
  // Core2EnterpriseUser for its PATCH semantics, which used to put an empty
  // "urn:...:enterprise:2.0:User" member on every response - an attribute keyed by a
  // schema neither the response nor /Schemas declares.
  it("carries no attribute keyed by a schema the response does not declare", async () => {
    const created = await createEduUser();
    const declared = new Set(created["schemas"] as string[]);

    for (const key of Object.keys(created)) {
      if (!key.startsWith("urn:")) {
        continue;
      }

      expect(declared, `${key} is in the body but not in schemas`).toContain(key);
    }
  });

  it("advertises every extension schema its responses carry", async () => {
    const created = await createEduUser();
    const advertised = new Set(
      ((await edupass<{ Resources: AdvertisedSchema[] }>("GET", "/Schemas")).body.Resources ?? [])
        .map((schema) => schema.id),
    );

    for (const key of Object.keys(created).filter((name) => name.startsWith("urn:"))) {
      expect(advertised, `${key} is returned but not advertised at /Schemas`).toContain(key);
    }
  });
});

// ---------------------------------------------------------------------------
// What the published schemas describe
// ---------------------------------------------------------------------------

describe("The published schemas describe the identity and role attributes Edupass sends", () => {
  it("advertises the core User schema", async () => {
    // "This should minimally include the core User schema
    // (urn:ietf:params:scim:schemas:core:2.0:User)." Advertising only the Edupass
    // extension tells Edupass the relying party supports no core attribute at all -
    // not even userName, which the same section names as required.
    const user = await advertisedSchema(SCHEMA_USER);
    expect(attributeNames(user)).toContain("userName");
  });

  it("advertises the core Group schema, because it manages roles as Groups", async () => {
    // "For RPs that support the Group resource, this must include the core Group
    // schema (urn:ietf:params:scim:schemas:core:2.0:Group)."
    const group = await advertisedSchema(SCHEMA_GROUP);
    expect(attributeNames(group)).toContain("displayName");
  });

  it("declares every core User attribute the specification's User Schema table lists", async () => {
    const user = await advertisedSchema(SCHEMA_USER);
    const names = attributeNames(user).map((name) => name.toLowerCase());

    for (const expected of ["externalid", "username", "name", "emails", "title", "active"]) {
      expect(names).toContain(expected);
    }
  });

  it("declares the groups attribute on the core User schema", async () => {
    // "For RPs with roles managed by Edupass, the groups attribute should be part of
    // the SCIM Core User Schema with returned property set as default." Edupass reads
    // /Schemas to decide what it can expect back, so an attribute the service returns
    // but does not advertise is one Edupass will not look for.
    const groups = attribute(await advertisedSchema(SCHEMA_USER), "groups");

    expect(groups.type).toBe("complex");
    expect(groups.multiValued).toBe(true);
    expect(groups.returned).toBe("default");
    expect(groups.mutability).toBe("readOnly");
  });

  it("declares value, $ref and display beneath groups", async () => {
    const groups = attribute(await advertisedSchema(SCHEMA_USER), "groups");
    const names = (groups.subAttributes ?? []).map((item) => item.name);

    expect(names).toContain("value");
    expect(names).toContain("$ref");
    expect(names).toContain("display");
  });

  it("says what the groups $ref points at, as a reference attribute must", async () => {
    // RFC 7643 section 7 makes referenceTypes required on a reference attribute, and
    // the specification's own schema example carries "referenceTypes": ["Group"].
    const groups = attribute(await advertisedSchema(SCHEMA_USER), "groups");
    const reference = (groups.subAttributes ?? []).find((item) => item.name === "$ref");

    expect(reference?.type).toBe("reference");
    expect(reference?.referenceTypes).toEqual(["Group"]);
  });

  it("says what the members $ref points at", async () => {
    const members = attribute(await advertisedSchema(SCHEMA_GROUP), "members");
    const reference = (members.subAttributes ?? []).find((item) => item.name === "$ref");

    expect(reference?.type).toBe("reference");
    expect(reference?.referenceTypes).toEqual(["User"]);
  });

  it("declares members on the core Group schema", async () => {
    // The specification's Members Attribute section: "a SCIM Core Group Schema
    // attribute with returned property set to default".
    const members = attribute(await advertisedSchema(SCHEMA_GROUP), "members");

    expect(members.multiValued).toBe(true);
    expect(members.returned).toBe("default");
  });

  it("still advertises the Edupass extension alongside the core schemas", async () => {
    const identifiers = (await advertisedSchemas()).map((item) => item.id);

    expect(identifiers).toContain(EXTENSION);
    expect(identifiers).toContain(SCHEMA_USER);
    expect(identifiers).toContain(SCHEMA_GROUP);
  });
});

// ---------------------------------------------------------------------------
// What the published resource types declare
// ---------------------------------------------------------------------------

describe("The published resource types declare the identity extension and the role resource", () => {
  it("advertises the Group resource type", async () => {
    // The specification's Get All Resource Types response carries both User and Group.
    // A relying party with Edupass-managed roles serves /Groups, so omitting the entry
    // contradicts the endpoint it actually exposes.
    const group = (await advertisedResourceTypes()).find((item) => item.name === "Group");

    expect(group, "/ResourceTypes does not advertise a Group resource type").toBeDefined();
    expect(group?.endpoint).toContain("Groups");
    expect(group?.schema).toBe(SCHEMA_GROUP);
  });

  it("declares the Edupass extension in the User resource type's schemaExtensions", async () => {
    // The specification's example carries
    //   "schemaExtensions": [{ "schema": "...:extension:Edupass:2.0:User", "required": true }]
    // which is how Edupass learns the extension is part of the User resource rather
    // than a schema the party merely happens to publish.
    const user = (await advertisedResourceTypes()).find((item) => item.name === "User");

    expect(user).toBeDefined();
    const extensions = user?.schemaExtensions ?? [];
    const edupassExtension = extensions.find((item) => item.schema === EXTENSION);

    expect(edupassExtension, "the User resource type does not declare the extension").toBeDefined();
    expect(typeof edupassExtension?.required).toBe("boolean");
  });

  it("keeps the User resource type pointed at the core User schema", async () => {
    const user = (await advertisedResourceTypes()).find((item) => item.name === "User");

    expect(user?.schema).toBe(SCHEMA_USER);
    expect(user?.endpoint).toContain("Users");
  });
});

// ---------------------------------------------------------------------------
// The groups attribute on User responses
// ---------------------------------------------------------------------------

describe("A user is returned with the roles it holds, each resolvable to the role resource", () => {
  it("returns groups on the Create User response", async () => {
    // "For RPs with roles returned by Edupass, the groups attribute should be
    // included", and the specification's 201 example carries `"groups": []`. Edupass
    // reads the create response to learn the identifier and the roles the party
    // already holds for the identity; an absent attribute is not the same answer as
    // an empty one.
    const created = await createEduUser();

    expect(groupsOf(created)).toEqual([]);
  });

  it("returns an empty groups array rather than omitting it for a user with no roles", async () => {
    const created = await createEduUser();
    const read = await edupass<ScimResource>("GET", `/Users/${created.id}`);

    expect(read.status).toBe(200);
    expect(groupsOf(read.body)).toEqual([]);
  });

  it("carries value, display and a resolvable $ref for each role held", async () => {
    // The specification's User examples give every groups entry a $ref:
    //   { "value": "...", "$ref": "https://.../scim/Groups/...", "display": "..." }
    // Without it Edupass has an identifier and no address to fetch it from.
    const user = await createEduUser();
    const group = await createEduGroup([{ value: user.id }]);

    const read = await edupass<ScimResource>("GET", `/Users/${user.id}`);
    const entries = groupsOf(read.body) ?? [];

    expect(entries).toHaveLength(1);
    expect(entries[0]?.value).toBe(group.id);
    expect(entries[0]?.display).toBe(group["displayName"]);
    expect(entries[0]?.$ref, "the groups entry carries no $ref").toBeDefined();
    // The whole URI, not toContain: a $ref missing the /scim prefix still contains
    // "/Groups/{id}", which is how a prefix-less reference passed this for so long.
    expect(entries[0]?.$ref).toBe(`${EDUPASS_BASE_URL}/Groups/${group.id}`);
  });

  it("addresses the group its $ref names", async () => {
    // A reference that does not resolve is worse than none: it tells Edupass the role
    // is fetchable when it is not.
    const user = await createEduUser();
    const group = await createEduGroup([{ value: user.id }]);

    const read = await edupass<ScimResource>("GET", `/Users/${user.id}`);
    const reference = (groupsOf(read.body) ?? [])[0]?.$ref ?? "";
    expect(reference).not.toBe("");

    // Fetched as given. Rebuilding it against the known-good base - which is what this
    // did before - repairs the very defect the test exists to catch.
    const fetched = await fetch(reference, { headers: authHeaders() });
    expect(fetched.status).toBe(200);
    expect(((await fetched.json()) as ScimResource).id).toBe(group.id);
  });

  it("agrees with meta.location on the group it names", async () => {
    const user = await createEduUser();
    const group = await createEduGroup([{ value: user.id }]);

    const read = await edupass<ScimResource>("GET", `/Users/${user.id}`);
    const reference = (groupsOf(read.body) ?? [])[0]?.$ref;

    const readGroup = await edupass<ScimResource>("GET", `/Groups/${group.id}`);
    expect(reference).toBe((readGroup.body["meta"] as { location: string }).location);
  });

  it("returns the same groups shape from Get All Users", async () => {
    const user = await createEduUser();
    const group = await createEduGroup([{ value: user.id }]);

    const listed = await edupass("GET", `/Users?filter=${encodeURIComponent(`userName eq "${user["userName"]}"`)}`);
    expect(listed.status).toBe(200);

    const entries = groupsOf((listed.body.Resources as ScimResource[])[0] as ScimResource) ?? [];
    expect(entries).toHaveLength(1);
    expect(entries[0]?.value).toBe(group.id);
    expect(entries[0]?.$ref).toContain(`/Groups/${group.id}`);
  });

  it("returns the groups attribute on the PUT response", async () => {
    // "Update User - PUT ... For RPs with roles returned by Edupass, the groups
    // attribute should be included."
    const user = await createEduUser();
    const group = await createEduGroup([{ value: user.id }]);

    const replaced = await edupass<ScimResource>("PUT", `/Users/${user.id}`, {
      ...eduUser({ userName: user["userName"] }),
      id: user.id,
    });

    expect(replaced.status).toBe(200);
    const entries = groupsOf(replaced.body) ?? [];
    expect(entries.map((entry) => entry.value)).toEqual([group.id]);
    expect(entries[0]?.$ref).toContain(`/Groups/${group.id}`);
  });

  it("does not let a client write the read-only groups attribute", async () => {
    // groups is derived from Group membership. Echoing back a value the client
    // supplied would report a role the party does not actually hold.
    const invented = "00000000-0000-0000-0000-000000000000";
    const created = await createEduUser({
      groups: [{ value: invented, display: "invented" }],
    });

    expect(groupsOf(created)).toEqual([]);
  });
});

// ---------------------------------------------------------------------------
// The members attribute on Group responses
// ---------------------------------------------------------------------------

describe("A role is returned with the users holding it, each resolvable to the user resource", () => {
  it("carries a resolvable $ref for each member", async () => {
    // Every members entry in the specification's Group examples carries a $ref
    // alongside the value.
    const user = await createEduUser();
    const group = await createEduGroup();

    // value only, no $ref: a reference the client supplied is preserved verbatim, so
    // supplying one would test the echo rather than what the service composes.
    const patched = await edupass("PATCH", `/Groups/${group.id}`, patchOp({
      op: "add",
      path: "members",
      value: [{ value: user.id }],
    }));
    expect([200, 204]).toContain(patched.status);

    const read = await edupass<ScimResource>("GET", `/Groups/${group.id}`);
    const members = (read.body["members"] as { value: string; $ref?: string }[]) ?? [];

    expect(members).toHaveLength(1);
    expect(members[0]?.value).toBe(user.id);
    expect(members[0]?.$ref, "the members entry carries no $ref").toBeDefined();
    expect(members[0]?.$ref).toBe(`${EDUPASS_BASE_URL}/Users/${user.id}`);
  });

  it("carries $ref on members supplied at create time", async () => {
    const user = await createEduUser();
    const group = await createEduGroup([{ value: user.id }]);

    const read = await edupass<ScimResource>("GET", `/Groups/${group.id}`);
    const members = (read.body["members"] as { value: string; $ref?: string }[]) ?? [];

    expect(members).toHaveLength(1);
    expect(members[0]?.$ref).toBe(`${EDUPASS_BASE_URL}/Users/${user.id}`);
  });

  it("addresses the user its $ref names", async () => {
    const user = await createEduUser();
    const group = await createEduGroup([{ value: user.id }]);

    const read = await edupass<ScimResource>("GET", `/Groups/${group.id}`);
    const reference = (read.body["members"] as { $ref?: string }[])[0]?.$ref ?? "";

    const fetched = await fetch(reference, { headers: authHeaders() });
    expect(fetched.status).toBe(200);
    expect(((await fetched.json()) as ScimResource).id).toBe(user.id);
  });

  it("returns members as [] on a group that has none", async () => {
    // The specification's Create Group response carries "members": [], and /Schemas
    // advertises members with returned=default. Omitting it says something different:
    // absent reads as "this service does not report membership", empty as "this group
    // has none". A group whose membership had never been written omitted it entirely.
    const group = await createEduGroup();
    expect(group["members"], "members absent from the create response").toEqual([]);

    const read = await edupass<ScimResource>("GET", `/Groups/${group.id}`);
    expect(read.body["members"], "members absent from Get Group by ID").toEqual([]);

    const listed = await edupass("GET", "/Groups");
    const found = (listed.body.Resources as ScimResource[]).find((r) => r.id === group.id);
    expect(found?.["members"], "members absent from Get All Groups").toEqual([]);
  });

  it("still honours excludedAttributes=members", async () => {
    // "RPs should implement the excludedAttributes query parameter for GET
    // operations, so that excludedAttributes=members excludes the members attribute."
    const user = await createEduUser();
    const group = await createEduGroup([{ value: user.id }]);

    const read = await edupass<ScimResource>(
      "GET",
      `/Groups/${group.id}?excludedAttributes=members`,
    );

    expect(read.status).toBe(200);
    expect(read.body).not.toHaveProperty("members");
    expect(read.body.id).toBe(group.id);
  });
});

// ---------------------------------------------------------------------------
// The metadata the extension schema carries
// ---------------------------------------------------------------------------

describe("The published identity extension declares only the attributes Edupass defines, with the metadata it prints", () => {
  /** The four sub-attributes the specification's User Schema table defines. */
  const DEFINED = ["uinFin", "schoolOrHq", "identityType", "identitySource"];

  it("advertises nothing beyond the four sub-attributes the specification defines", async () => {
    // A party is free to omit one - "the uinFin can be omitted if indicated at
    // onboarding and in the Get All Schemas endpoint" - but not to invent a fifth,
    // because Edupass has no way to learn what it means.
    const extension = await advertisedSchema(EXTENSION);

    for (const name of attributeNames(extension)) {
      expect(DEFINED, `${name} is not an attribute the specification defines`).toContain(name);
    }
  });

  it.each(DEFINED)(
    "gives %s the metadata the specification's example carries, when it advertises it",
    async (name) => {
      // The specification prints the extension schema in full, and every attribute in
      // it is a readWrite, default-returned, non-unique, optional, case-insensitive
      // string. A party advertising uinFin as readOnly is telling Edupass it cannot
      // send the value that the User Schema table says it will send.
      const extension = await advertisedSchema(EXTENSION);
      if (!attributeNames(extension).includes(name)) {
        return;
      }

      const advertised = attribute(extension, name);

      expect(advertised.type).toBe("string");
      expect(advertised.multiValued).toBe(false);
      expect(advertised.mutability).toBe("readWrite");
      expect(advertised.returned).toBe("default");
    },
  );

  it("carries the extension on every User it returns while it declares it required", async () => {
    // RFC 7643 6: required true means "a resource of this type MUST include this schema
    // extension". The specification's own schemaExtensions example carries required:
    // true while its User Schema table marks the extension Optional, and the party
    // resolves that by accepting a create without one - so the obligation lands on the
    // response instead. A resource type that declares the extension required and then
    // returns a User without it contradicts the model a client generated from it.
    const user = (await advertisedResourceTypes()).find((item) => item.name === "User");
    const declared = (user?.schemaExtensions ?? []).find((item) => item.schema === EXTENSION);
    expect(declared, "the User resource type does not declare the extension").toBeDefined();
    if (declared?.required !== true) {
      return;
    }

    const created = await edupass<ScimResource>("POST", "/Users", {
      schemas: [SCHEMA_USER],
      userName: `${unique("noext")}@moe.edu.sg`,
      externalId: unique("edupass-id"),
    });
    expect(created.status).toBe(201);
    expect(created.body.schemas).toContain(EXTENSION);
    expect(created.body).toHaveProperty(EXTENSION);

    const read = await edupass<ScimResource>("GET", `/Users/${created.body.id}`);
    expect(read.body.schemas).toContain(EXTENSION);
    expect(read.body).toHaveProperty(EXTENSION);
  });
});

// ---------------------------------------------------------------------------
// What the Group schema advertises against how the endpoint behaves
// ---------------------------------------------------------------------------

describe("The published role schema agrees with how the role endpoint behaves", () => {
  it("refuses to rename a group", async () => {
    // displayName is the application role and is advertised immutable: Edupass creates a
    // Group per role and deletes it when the role is deprecated, never renaming one.
    const group = await createEduGroup();

    const patched = await edupass("PATCH", `/Groups/${group.id}`, patchOp({
      op: "replace",
      path: "displayName",
      value: unique(`${EDUPASS_LOCATION}_app1_renamed`),
    }));
    expect(patched.status).toBe(400);
    expect(patched.body["scimType"]).toBe("mutability");

    const put = await edupass("PUT", `/Groups/${group.id}`, {
      ...group,
      displayName: unique(`${EDUPASS_LOCATION}_app1_replaced`),
    });
    expect(put.status).toBe(400);
    expect(put.body["scimType"]).toBe("mutability");

    const read = await edupass<ScimResource>("GET", `/Groups/${group.id}`);
    expect(read.body["displayName"]).toBe(group["displayName"]);
  });

  it("advertises a displayName no client may rewrite, which is how it behaves", async () => {
    // The specification prints displayName as immutable and server-unique. The suite
    // already proves the endpoint refuses a rename and refuses a duplicate; this is the
    // other half - that /Schemas says so, so a client can know before it tries.
    const advertised = attribute(await advertisedSchema(SCHEMA_GROUP), "displayName");

    expect(["immutable", "readOnly"]).toContain(advertised.mutability);
    expect(advertised.type).toBe("string");
    expect(advertised.multiValued).toBe(false);
  });
});

// ---------------------------------------------------------------------------
// What ServiceProviderConfig promises Edupass specifically
// ---------------------------------------------------------------------------

describe("The published configuration promises the behaviours Edupass provisioning depends on", () => {
  // The specification's Service Provider Configuration table marks three of the seven
  // members as things Edupass needs, and is explicit that it does not care about the
  // rest. scim-compliance.spec.ts proves the members are well-formed for any client;
  // these three are the ones an Edupass onboarding actually reads.
  it("supports PATCH, which every role assignment goes through", async () => {
    const response = await edupass("GET", "/ServiceProviderConfig");

    expect(response.status).toBe(200);
    expect((response.body.patch as { supported: boolean }).supported).toBe(true);
  });

  it("supports filter, which is how Edupass finds a resource it already created", async () => {
    const response = await edupass("GET", "/ServiceProviderConfig");

    expect((response.body.filter as { supported: boolean }).supported).toBe(true);
  });

  it("offers the oauthbearertoken scheme Edupass presents", async () => {
    // "Edupass requires the oauthbearertoken scheme to be supported." Edupass sends a
    // signed JWT in the Authorization header and nothing else; a party advertising only
    // httpbasic has told it there is no way in.
    const response = await edupass("GET", "/ServiceProviderConfig");
    const schemes = response.body.authenticationSchemes as { type: string }[];

    expect(schemes.map((scheme) => scheme.type)).toContain("oauthbearertoken");
  });
});
