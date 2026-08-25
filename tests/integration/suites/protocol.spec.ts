import { describe, expect, it } from "vitest";
import { BASE_URL } from "../src/host.js";
import {
  SCHEMA_BULK_REQUEST,
  SCHEMA_ERROR,
  SCHEMA_LIST,
  SCHEMA_USER,
  createUser,
  devToken,
  edupass,
  scim,
  unique,
  userBody,
} from "../src/client.js";

describe("Discovery", () => {
  it("advertises every ServiceProviderConfig member RFC 7643 5 requires", async () => {
    const response = await scim("GET", "/ServiceProviderConfig");

    expect(response.status).toBe(200);
    for (const member of [
      "patch",
      "bulk",
      "filter",
      "changePassword",
      "sort",
      // Spelled "etag", not "eTag" - a client looking for the RFC's name found nothing.
      "etag",
      "authenticationSchemes",
    ]) {
      expect(response.body).toHaveProperty(member);
    }
  });

  it("names only authentication scheme types the RFC permits", async () => {
    const response = await scim("GET", "/ServiceProviderConfig");
    const schemes = response.body.authenticationSchemes as { type: string }[];

    expect(schemes.length).toBeGreaterThan(0);
    for (const scheme of schemes) {
      expect([
        "oauth",
        "oauth2",
        "oauthbearertoken",
        "httpbasic",
        "httpdigest",
      ]).toContain(scheme.type);
    }
  });

  it("lists the resource types and the core User schema", async () => {
    const types = await scim("GET", "/ResourceTypes");
    expect(types.status).toBe(200);
    expect((types.body.Resources as unknown[]).length).toBeGreaterThanOrEqual(2);

    const schemas = await scim("GET", "/Schemas");
    expect(schemas.status).toBe(200);
    expect(schemas.body.schemas).toContain(SCHEMA_LIST);
    const ids = (schemas.body.Resources as { id: string }[]).map((resource) => resource.id);
    expect(ids.some((id) => id.includes("core:2.0:User"))).toBe(true);
  });
});

describe("Authorization", () => {
  it("refuses an unauthenticated request", async () => {
    expect((await scim("GET", "/Users", undefined, { anonymous: true })).status).toBe(401);
  });

  it("refuses a malformed token", async () => {
    const response = await scim("GET", "/Users", undefined, {
      headers: { Authorization: "Bearer not.a.token" },
    });

    expect(response.status).toBe(401);
  });

  it("refuses a token signed with the wrong key", async () => {
    const forged = `${devToken().split(".").slice(0, 2).join(".")}.tampered`;
    const response = await scim("GET", "/Users", undefined, {
      headers: { Authorization: `Bearer ${forged}` },
    });

    expect(response.status).toBe(401);
  });
});

describe("Errors", () => {
  it("returns a SCIM error body on 404", async () => {
    const response = await scim("GET", `/Users/${unique("ghost")}`);

    expect(response.status).toBe(404);
    expect(response.body.schemas).toContain(SCHEMA_ERROR);
  });

  it("rejects an unparseable body", async () => {
    const response = await scim("POST", "/Users", undefined, { raw: "not json at all" });
    expect(response.status).toBe(400);
  });

  it("rejects a truncated body", async () => {
    const response = await scim("POST", "/Users", undefined, {
      raw: `{"schemas":["${SCHEMA_USER}"],"userName":`,
    });
    expect(response.status).toBe(400);
  });

  it("rejects an empty body on create and on patch", async () => {
    expect((await scim("POST", "/Users", undefined, { raw: "" })).status).toBe(400);

    const created = await createUser();
    expect((await scim("PATCH", `/Users/${created.id}`, undefined, { raw: "" })).status).toBe(400);
  });
});

describe("Headers and negotiation", () => {
  it("agrees between the Location header and meta.location, and the URI resolves", async () => {
    const created = await scim("POST", "/Users", userBody());
    const header = created.headers.get("Location");
    const meta = (created.body.meta as { location?: string }).location;

    expect(header).toBeTruthy();
    expect(meta).toBe(header);

    const followed = await fetch(meta as string, {
      headers: { Authorization: `Bearer ${devToken()}` },
    });
    expect(followed.status).toBe(200);
  });

  it("keeps meta.location stable across a read", async () => {
    const created = await scim("POST", "/Users", userBody());
    const read = await scim("GET", `/Users/${created.body.id}`);

    expect((read.body.meta as { location?: string }).location).toBe(
      (created.body.meta as { location?: string }).location,
    );
  });

  it("answers scim+json even when XML is asked for", async () => {
    const created = await createUser();
    const response = await scim("GET", `/Users/${created.id}`, undefined, {
      headers: { Accept: "application/xml" },
    });

    expect(response.status).toBe(200);
    expect(response.headers.get("Content-Type")).toContain("application/scim+json");
  });

  it("accepts application/json on a write", async () => {
    const response = await scim("POST", "/Users", userBody(), {
      contentType: "application/json",
    });

    expect([201, 415]).toContain(response.status);
  });

  it("matches routes case-insensitively", async () => {
    expect((await scim("GET", "/users")).status).toBe(200);
  });
});

describe("Query parameters", () => {
  it("ignores an unknown parameter", async () => {
    expect((await scim("GET", "/Users?nosuchparameter=1")).status).toBe(200);
  });

  it("tolerates an empty filter value", async () => {
    expect([200, 400]).toContain((await scim("GET", "/Users?filter=")).status);
  });

  it("tolerates a repeated filter parameter", async () => {
    const response = await scim(
      "GET",
      `/Users?filter=${encodeURIComponent('userName eq "a"')}&filter=${encodeURIComponent('userName eq "b"')}`,
    );

    expect([200, 400]).toContain(response.status);
  });
});

describe("HTTP verbs", () => {
  it("answers 405 to HEAD on both legs", async () => {
    // Regression, net48 only: Web API matched HEAD against the GET action, the
    // action produced a body, and the OWIN adapter failed writing it - so the
    // caller got a closed socket and no HTTP response at all.
    const response = await fetch(`${BASE_URL}/Users`, {
      method: "HEAD",
      headers: { Authorization: `Bearer ${devToken()}` },
    });

    expect(response.status).toBe(405);
  });

  it("refuses TRACE", async () => {
    const response = await fetch(`${BASE_URL}/Users`, {
      method: "TRACE",
      headers: { Authorization: `Bearer ${devToken()}` },
    }).catch(() => undefined);

    // fetch may refuse to send TRACE at all, which is itself an acceptable outcome.
    if (response) {
      expect([400, 404, 405, 501]).toContain(response.status);
    }
  });

  it("refuses a write verb on a collection URI", async () => {
    // The service root routes at the prefix, so its {identifier} template has the
    // same shape as /Users. It used to win the match for verbs the Users controller
    // does not define, answering 415 on net10 and 405 on net48.
    const response = await scim("PUT", "/Users", { schemas: [SCHEMA_USER] });

    expect(response.status).toBe(405);
  });

  it("answers the service root without failing", async () => {
    expect([200, 404, 501]).toContain((await scim("GET", "")).status);
  });
});

describe("Bulk", () => {
  it("behaves as ServiceProviderConfig advertises", async () => {
    const config = await scim("GET", "/ServiceProviderConfig");
    const supported = (config.body.bulk as { supported?: boolean }).supported === true;

    const response = await scim("POST", "/Bulk", {
      schemas: [SCHEMA_BULK_REQUEST],
      Operations: [
        { method: "POST", path: "/Users", bulkId: "one", data: userBody() },
      ],
    });

    expect(supported ? [200, 201] : [200, 201, 501]).toContain(response.status);
  });

  it("does not loop on a circular bulkId reference", async () => {
    // RFC 7644 3.7.2 calls circular references out. The failure mode to avoid is an
    // unbounded resolution loop, not any particular status.
    const response = await scim("POST", "/Bulk", {
      schemas: [SCHEMA_BULK_REQUEST],
      Operations: [
        { method: "POST", path: "/Users", bulkId: "a", data: userBody({ manager: { value: "bulkId:b" } }) },
        { method: "POST", path: "/Users", bulkId: "b", data: userBody({ manager: { value: "bulkId:a" } }) },
      ],
    });

    expect([200, 400, 409, 501]).toContain(response.status);
  });

  it("handles an operation count far beyond the advertised maximum", async () => {
    const response = await scim("POST", "/Bulk", {
      schemas: [SCHEMA_BULK_REQUEST],
      Operations: Array.from({ length: 200 }, (_unused, index) => ({
        method: "POST",
        path: "/Users",
        bulkId: `x${index}`,
        data: userBody(),
      })),
    });

    expect([200, 400, 413, 501]).toContain(response.status);
  });

  it("tolerates an empty or absent Operations member", async () => {
    expect([200, 400, 501]).toContain(
      (await scim("POST", "/Bulk", { schemas: [SCHEMA_BULK_REQUEST], Operations: [] })).status,
    );
    expect([200, 400, 501]).toContain(
      (await scim("POST", "/Bulk", { schemas: [SCHEMA_BULK_REQUEST] })).status,
    );
  });
});

describe("ServiceProviderConfig: the member names RFC 7643 5 defines", () => {
  it("names an authentication scheme's specification URL specUri", async () => {
    // RFC 7643 section 5 spells the sub-attribute "specUri". "specUrl" is a member
    // no client is looking for, so a scheme's specification link is invisible.
    const response = await scim("GET", "/ServiceProviderConfig");
    const schemes = response.body.authenticationSchemes as Record<string, unknown>[];

    expect(schemes.length).toBeGreaterThan(0);
    for (const scheme of schemes) {
      expect(scheme).not.toHaveProperty("specUrl");
      if (scheme["specUri"] !== undefined) {
        expect(typeof scheme["specUri"]).toBe("string");
      }
    }
  });

  it("gives every authentication scheme the three members the RFC requires", async () => {
    const response = await scim("GET", "/ServiceProviderConfig");

    for (const scheme of response.body.authenticationSchemes as Record<string, unknown>[]) {
      for (const member of ["type", "name", "description"]) {
        expect(scheme, `a scheme is missing ${member}`).toHaveProperty(member);
      }
    }
  });

  it("spells any service-level documentation link documentationUri", async () => {
    const response = await scim("GET", "/ServiceProviderConfig");

    expect(response.body).not.toHaveProperty("documentationUrl");
  });
});

describe("ServiceProviderConfig: filter.maxResults is a promise, not a note", () => {
  // Exercised against the Edupass host so that filling a store past the advertised
  // ceiling cannot disturb a core-host suite that walks an unfiltered collection.
  it("returns no more resources than it advertises", async () => {
    // RFC 7643 section 5 defines filter.maxResults as "the maximum number of
    // resources returned in a response". Advertising 200 and then returning every
    // resource in the store contradicts the service's own configuration, and a
    // client sizing its buffers from the config is the one that pays for it.
    const config = await edupass("GET", "/ServiceProviderConfig");
    const maxResults = (config.body.filter as { maxResults?: number }).maxResults ?? 0;
    expect(maxResults).toBeGreaterThan(0);

    const before = await edupass("GET", "/Users");
    const existing = before.body.totalResults as number;

    for (let index = existing; index <= maxResults + 2; index += 1) {
      const userName = `${unique("cap")}@moe.edu.sg`;
      const created = await edupass("POST", "/Users", {
        schemas: [SCHEMA_USER],
        userName,
        active: true,
      });
      expect(created.status).toBe(201);
    }

    const response = await edupass("GET", "/Users");

    expect(response.status).toBe(200);
    expect(response.body.totalResults).toBeGreaterThan(maxResults);
    expect((response.body.Resources as unknown[]).length).toBeLessThanOrEqual(maxResults);
    expect(response.body.itemsPerPage).toBeLessThanOrEqual(maxResults);
  });

  it("clamps a count larger than the advertised maximum", async () => {
    const config = await edupass("GET", "/ServiceProviderConfig");
    const maxResults = (config.body.filter as { maxResults?: number }).maxResults ?? 0;

    const response = await edupass("GET", `/Users?count=${maxResults * 10}`);

    expect(response.status).toBe(200);
    expect((response.body.Resources as unknown[]).length).toBeLessThanOrEqual(maxResults);
  });

  it("still honours a count below the maximum", async () => {
    const response = await edupass("GET", "/Users?count=3");

    expect(response.status).toBe(200);
    expect((response.body.Resources as unknown[]).length).toBeLessThanOrEqual(3);
  });

  it("still reports the true total above the ceiling", async () => {
    // The cap limits the page, not the count. A client paging through has to be able
    // to see how far it has to go.
    const response = await edupass("GET", "/Users?count=1");

    expect(response.body.itemsPerPage).toBe(1);
    expect(response.body.totalResults).toBeGreaterThan(1);
  });
});
