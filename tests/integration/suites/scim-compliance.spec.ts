import { describe, expect, it } from "vitest";
import {
  SCHEMA_ERROR,
  SCHEMA_GROUP,
  SCHEMA_LIST,
  SCHEMA_USER,
  createGroup,
  createUser,
  filterQuery,
  groupBody,
  scim,
  unique,
  userBody,
} from "../src/client.js";

/**
 * The interface specification's core-SCIM clauses, read as a contract.
 *
 * The pair to edupass-conformance.spec.ts. Both files walk the same document -
 * "System for Cross-domain Identity Management (SCIM)" - and the split between them is
 * the document's own: a clause that RFC 7643/7644 already mandates is proved here
 * against the core host, and a clause Edupass adds on top is proved there against the
 * Edupass one. Reading the two side by side answers separately "is this service SCIM"
 * and "is this service an Edupass relying party", which is what a party onboarding
 * against the document needs to know.
 *
 * Scope rule: everything here holds for any SCIM service provider. Nothing here may
 * name Edupass, an Edupass attribute, or an Edupass value set - an assertion that would
 * only hold at an Edupass party belongs in the other file.
 *
 * Deliberately not a second copy of the feature suites. Where a clause is already
 * proved by users.spec.ts, groups.spec.ts, filters.spec.ts, protocol.spec.ts or
 * resource-types.spec.ts, it is named in a comment and left there rather than asserted
 * twice, so that a change has one place to break.
 *
 * Naming: each describe states what one clause of the document requires, in a sentence
 * that stands on its own. Not the clause's number and not its heading - both are the
 * document's, and the document is revised: a heading gets retitled, a section moves, and
 * a name built on either points at nothing while still reading as though it points
 * somewhere. A description stays true to the clause it came from, so a reader with the
 * document open can find the clause from the test and a reader with the test output can
 * tell what broke without opening anything.
 */

/** meta's four required sub-attributes, per the specification's SCIM Resources table. */
function expectMeta(resource: any, resourceType: string): void {
  expect(resource.meta).toBeDefined();
  expect(resource.meta.resourceType).toBe(resourceType);
  expect(typeof resource.meta.created).toBe("string");
  expect(typeof resource.meta.lastModified).toBe("string");
  expect(typeof resource.meta.location).toBe("string");
  expect(Date.parse(resource.meta.created)).not.toBeNaN();
  expect(Date.parse(resource.meta.lastModified)).not.toBeNaN();
}

/** The envelope the specification's List Response table requires. */
function expectListResponse(body: any): void {
  expect(body.schemas).toEqual([SCHEMA_LIST]);
  expect(typeof body.totalResults).toBe("number");
  expect(typeof body.startIndex).toBe("number");
  expect(typeof body.itemsPerPage).toBe("number");
  expect(Array.isArray(body.Resources)).toBe(true);
}

describe("A refused request is answered with a SCIM error body naming the status and the kind of failure", () => {
  // "status | Required | The HTTP status code expressed as a JSON string". A client
  // reading the body as typed gets a number where its parser wants a string, and the
  // mismatch surfaces at the client rather than here.
  it.each([
    ["404", async () => scim("GET", `/Users/${unique("ghost")}`)],
    ["400", async () => scim("POST", "/Users", undefined, { raw: "not json at all" })],
    [
      "409",
      async () => {
        const existing = await createUser();
        return scim("POST", "/Users", { schemas: [SCHEMA_USER], userName: existing.userName });
      },
    ],
  ])("expresses status as the JSON string %s", async (expected, send) => {
    const response = await send();

    expect(String(response.status)).toBe(expected);
    expect(response.body.schemas).toEqual([SCHEMA_ERROR]);
    expect(typeof response.body.status).toBe("string");
    expect(response.body.status).toBe(expected);
  });

  it("names a scimType on a 400, which the specification marks required there", async () => {
    const response = await scim("POST", "/Users", undefined, { raw: "not json at all" });

    expect(response.status).toBe(400);
    expect(typeof response.body.scimType).toBe("string");
    expect([
      "tooMany",
      "uniqueness",
      "invalidSyntax",
      "invalidFilter",
      "invalidPath",
      "invalidValue",
      "noTarget",
      "mutability",
      "invalidVers",
      "sensitive",
    ]).toContain(response.body.scimType);
  });

  it("names invalidSyntax when a body does not conform to the request schema", async () => {
    // The specification's applicability column: "invalidSyntax ... POST, PUT requests".
    const response = await scim("POST", "/Users", undefined, {
      raw: `{"schemas":["${SCHEMA_USER}"],"userName":`,
    });

    expect(response.status).toBe(400);
    expect(response.body.scimType).toBe("invalidSyntax");
  });

  it("answers 401 with a challenge, and a SCIM body if it sends one at all", async () => {
    // The body table is prefixed "If present" - a bodiless 401 is conformant. What is
    // not conformant is a 401 carrying an ASP.NET problem document, or one omitting the
    // challenge a client needs in order to know which scheme to present.
    const response = await scim("GET", "/Users", undefined, { anonymous: true });

    expect(response.status).toBe(401);
    expect(response.headers.get("www-authenticate")).toBeTruthy();
    if (response.text.trim().length > 0) {
      expect(response.body.schemas).toEqual([SCHEMA_ERROR]);
      expect(response.body.status).toBe("401");
    }
  });
});

describe("A request that would duplicate an existing resource is refused as a conflict", () => {
  // "If there are conflicts, the RP should respond with HTTP status 409 with scimType
  // error code uniqueness." users.spec.ts and groups.spec.ts assert the 409; the
  // keyword is what tells a client the 409 was a duplicate rather than a version clash.
  it("names uniqueness when a create duplicates a userName", async () => {
    const existing = await createUser();
    const again = await scim("POST", "/Users", {
      schemas: [SCHEMA_USER],
      userName: existing.userName,
    });

    expect(again.status).toBe(409);
    expect(again.body.scimType).toBe("uniqueness");
  });

  it("names uniqueness when a create duplicates a displayName", async () => {
    const existing = await createGroup();
    const again = await scim("POST", "/Groups", {
      schemas: [SCHEMA_GROUP],
      displayName: existing.displayName,
    });

    expect(again.status).toBe(409);
    expect(again.body.scimType).toBe("uniqueness");
  });

  it("names uniqueness when a replace would duplicate another userName", async () => {
    // The status table applies 409 to PUT as well as POST. A replace is the other way a
    // client can collide two users, and it has to be refused the same way.
    const first = await createUser();
    const second = await createUser();

    const response = await scim("PUT", `/Users/${second.id}`, {
      schemas: [SCHEMA_USER],
      id: second.id,
      userName: first.userName,
    });

    expect(response.status).toBe(409);
    expect(response.body.scimType).toBe("uniqueness");
  });
});

describe("A resource carries a stable server identifier, the client's own identifier, and complete metadata", () => {
  it("returns id, externalId and a complete meta on a created user", async () => {
    const externalId = unique("external");
    const response = await scim("POST", "/Users", userBody({ externalId }));

    expect(response.status).toBe(201);
    expect(response.body.schemas).toContain(SCHEMA_USER);
    expect(typeof response.body.id).toBe("string");
    expect(response.body.externalId).toBe(externalId);
    expectMeta(response.body, "User");
  });

  it("returns id, externalId and a complete meta on a created group", async () => {
    const externalId = unique("external");
    const response = await scim("POST", "/Groups", groupBody({ externalId }));

    expect(response.status).toBe(201);
    expect(response.body.schemas).toContain(SCHEMA_GROUP);
    expect(typeof response.body.id).toBe("string");
    expect(response.body.externalId).toBe(externalId);
    expectMeta(response.body, "Group");
  });

  it("keeps the same id when the resource is returned again", async () => {
    // "It must be a stable, non-reassignable identifier that does not change when the
    // same resource is returned in subsequent requests."
    const created = await createUser();
    const read = await scim("GET", `/Users/${created.id}`);
    const queried = await scim("GET", `/Users${filterQuery(`userName eq "${created.userName}"`)}`);

    expect(read.body.id).toBe(created.id);
    expect(queried.body.Resources[0].id).toBe(created.id);
  });
});

describe("A collection is returned in an envelope reporting the total and the page returned", () => {
  it.each(["/Users", "/Groups", "/Schemas", "/ResourceTypes"])(
    "returns the list envelope from %s",
    async (endpoint) => {
      // The specification requires the envelope of every list response, not only of the
      // two resource collections. A client paging /Schemas the way it pages /Users
      // needs itemsPerPage and startIndex from both.
      await createUser();
      await createGroup();
      const response = await scim("GET", endpoint);

      expect(response.status).toBe(200);
      expectListResponse(response.body);
    },
  );

  it("reports itemsPerPage as the size of the page it actually returned", async () => {
    await createUser();
    await createUser();
    const response = await scim("GET", "/Users?count=1");

    expect(response.body.itemsPerPage).toBe(1);
    expect(response.body.Resources).toHaveLength(1);
    expect(response.body.totalResults).toBeGreaterThan(1);
  });
});

describe("A page size of zero or less returns no resources while the total is still reported", () => {
  // "A negative value is interpreted as 0." A startIndex below one is already proved by
  // filters.spec.ts; count is the half that was not.
  it.each([-5, 0])(
    "reads count %i as an empty page while still reporting the total",
    async (count) => {
      await createUser();
      const response = await scim("GET", `/Users?count=${count}`);

      expect(response.status).toBe(200);
      expect(response.body.Resources).toHaveLength(0);
      expect(response.body.itemsPerPage).toBe(0);
      expect(response.body.totalResults).toBeGreaterThan(0);
    },
  );
});

describe("A search whose criterion matches nothing returns an empty collection, not a failure", () => {
  // "Resources will be an empty list if there are no matching resources." A service
  // answering 404 instead sends the client down the create path for a user it already
  // has, and the create then fails 409 - which is the loop this clause exists to stop.
  it("answers an empty list, not a 404, when no user has that userName", async () => {
    const response = await scim("GET", `/Users${filterQuery(`userName eq "${unique("nobody")}"`)}`);

    expect(response.status).toBe(200);
    expectListResponse(response.body);
    expect(response.body.totalResults).toBe(0);
    expect(response.body.Resources).toHaveLength(0);
  });

  it("answers an empty list, not a 404, when no group has that displayName", async () => {
    const response = await scim(
      "GET",
      `/Groups${filterQuery(`displayName eq "${unique("norole")}"`)}`,
    );

    expect(response.status).toBe(200);
    expectListResponse(response.body);
    expect(response.body.totalResults).toBe(0);
    expect(response.body.Resources).toHaveLength(0);
  });
});

describe("A caller can read a group while asking for its membership to be left out", () => {
  // "RPs should implement the excludedAttributes query parameter for GET operations, so
  // that the query parameter excludedAttributes=members excludes the members attribute
  // from the response." Asserted at the core host because a group with many members is
  // every service's problem, not only an Edupass one.
  it("omits members from a single group that has them", async () => {
    const user = await createUser();
    const group = await createGroup({ members: [{ value: user.id }] });

    const response = await scim("GET", `/Groups/${group.id}?excludedAttributes=members`);

    expect(response.status).toBe(200);
    expect(response.body.id).toBe(group.id);
    expect(response.body.displayName).toBe(group.displayName);
    expect(response.body.members).toBeUndefined();
  });

  it("omits members from every group of a list response", async () => {
    const user = await createUser();
    await createGroup({ members: [{ value: user.id }] });

    const response = await scim("GET", "/Groups?excludedAttributes=members");

    expect(response.status).toBe(200);
    expect((response.body.Resources as unknown[]).length).toBeGreaterThan(0);
    for (const group of response.body.Resources as any[]) {
      expect(group.members).toBeUndefined();
      expect(group.id).toBeDefined();
    }
  });
});

describe("A replacement overwrites a resource's attributes without resetting its identity", () => {
  it("keeps meta.created and moves meta.lastModified", async () => {
    // "This should overwrite all attributes" - the resource's attributes, not its
    // identity. A replace that resets created makes every resource look new to a client
    // reconciling on it.
    const created = await createUser();

    const replaced = await scim("PUT", `/Users/${created.id}`, {
      schemas: [SCHEMA_USER],
      id: created.id,
      userName: created.userName,
      title: "Principal",
    });

    expect(replaced.status).toBe(200);
    expect(replaced.body.meta.created).toBe(created.meta!.created);
    expect(Date.parse(replaced.body.meta.lastModified)).toBeGreaterThanOrEqual(
      Date.parse(created.meta!.created!),
    );
    expect(replaced.body.title).toBe("Principal");
  });
});

describe("A deletion is acknowledged with no body, and the resource is afterwards gone", () => {
  it.each([
    ["/Users", async () => (await createUser()).id],
    ["/Groups", async () => (await createGroup()).id],
  ])("answers 204 with no body when %s deletes", async (collection, create) => {
    const id = await create();

    const response = await scim("DELETE", `${collection}/${id}`);

    expect(response.status).toBe(204);
    expect(response.text).toBe("");
    expect((await scim("GET", `${collection}/${id}`)).status).toBe(404);
  });
});

describe("The service publishes which optional behaviours it supports and how it is authenticated", () => {
  // protocol.spec.ts proves the seven members are present. The specification goes
  // further and tabulates what each one holds, which is what a client reads.
  it("gives patch, filter, sort, etag and changePassword a boolean supported", async () => {
    const response = await scim("GET", "/ServiceProviderConfig");

    for (const member of ["patch", "filter", "sort", "etag", "changePassword"]) {
      expect(typeof response.body[member].supported, member).toBe("boolean");
    }
  });

  it("gives bulk both the integers the specification requires alongside supported", async () => {
    const response = await scim("GET", "/ServiceProviderConfig");
    const bulk = response.body.bulk as {
      supported: boolean;
      maxOperations: number;
      maxPayloadSize: number;
    };

    expect(typeof bulk.supported).toBe("boolean");
    expect(typeof bulk.maxOperations).toBe("number");
    expect(typeof bulk.maxPayloadSize).toBe("number");
    if (!bulk.supported) {
      // "If supported is false, this should be 0."
      expect(bulk.maxOperations).toBe(0);
      expect(bulk.maxPayloadSize).toBe(0);
    }
  });

  it("says how many resources a filtered response can return", async () => {
    const response = await scim("GET", "/ServiceProviderConfig");
    const filter = response.body.filter as { supported: boolean; maxResults?: number };

    expect(filter.supported).toBe(true);
    expect(typeof filter.maxResults).toBe("number");
    expect(filter.maxResults).toBeGreaterThan(0);
  });

  it("gives every authentication scheme the three members the specification requires", async () => {
    const response = await scim("GET", "/ServiceProviderConfig");
    const schemes = response.body.authenticationSchemes as Record<string, unknown>[];

    expect(schemes.length).toBeGreaterThan(0);
    for (const scheme of schemes) {
      expect(typeof scheme["type"]).toBe("string");
      expect(typeof scheme["name"]).toBe("string");
      expect(typeof scheme["description"]).toBe("string");
    }
  });
});

describe("The service publishes the shape of every resource it serves, attribute by attribute", () => {
  async function attributesOf(schemaId: string): Promise<any[]> {
    const response = await scim("GET", "/Schemas");
    const schema = (response.body.Resources as any[]).find((item) => item.id === schemaId);
    expect(schema, `/Schemas does not advertise ${schemaId}`).toBeDefined();
    return schema.attributes as any[];
  }

  it("advertises the core User schema with userName on it", async () => {
    const attributes = await attributesOf(SCHEMA_USER);

    expect(attributes.map((attribute) => attribute.name)).toContain("userName");
  });

  it("advertises the core Group schema with displayName and members on it", async () => {
    const names = (await attributesOf(SCHEMA_GROUP)).map((attribute) => attribute.name);

    expect(names).toContain("displayName");
    expect(names).toContain("members");
  });

  it.each([SCHEMA_USER, SCHEMA_GROUP])(
    "gives every attribute of %s the metadata the specification marks required",
    async (schemaId) => {
      // name, type, multiValued, mutability, required, returned and uniqueness are all
      // "Required" in the specification's attribute table. A client generating its own
      // model from /Schemas cannot fill in a gap it was never told about.
      const walk = (attributes: any[], path: string): void => {
        for (const attribute of attributes) {
          const where = `${path}.${attribute.name}`;
          expect(typeof attribute.name, where).toBe("string");
          expect(typeof attribute.type, where).toBe("string");
          expect(typeof attribute.multiValued, where).toBe("boolean");
          expect(typeof attribute.required, where).toBe("boolean");
          expect(["readOnly", "readWrite", "immutable", "writeOnly"], where).toContain(
            attribute.mutability,
          );
          expect(["always", "never", "default", "request"], where).toContain(attribute.returned);
          expect(["none", "server", "global"], where).toContain(attribute.uniqueness);

          if (attribute.type === "string") {
            expect(typeof attribute.caseExact, where).toBe("boolean");
          }
          if (attribute.type === "reference") {
            expect(Array.isArray(attribute.referenceTypes), where).toBe(true);
          }
          if (attribute.type === "complex") {
            expect(Array.isArray(attribute.subAttributes), where).toBe(true);
            walk(attribute.subAttributes, where);
          }
        }
      };

      walk(await attributesOf(schemaId), schemaId);
    },
  );
});

describe("The service publishes which resources it serves and where each one is addressed", () => {
  it("gives every resource type name, endpoint and schema", async () => {
    const response = await scim("GET", "/ResourceTypes");
    const types = response.body.Resources as any[];

    expect(types.length).toBeGreaterThan(0);
    for (const type of types) {
      expect(typeof type.name).toBe("string");
      expect(type.endpoint).toMatch(/^\//);
      expect(type.schema).toMatch(/^urn:ietf:params:scim:schemas:/);
    }
  });

  it("serves the User resource type, which every relying party must support", async () => {
    // "RPs must minimally support the User resource."
    const response = await scim("GET", "/ResourceTypes");
    const user = (response.body.Resources as any[]).find((type) => type.name === "User");

    expect(user).toBeDefined();
    expect(user.endpoint).toBe("/Users");
    expect(user.schema).toBe(SCHEMA_USER);
  });
});
