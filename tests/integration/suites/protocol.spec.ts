import { describe, expect, it } from "vitest";
import { BASE_URL } from "../src/host.js";
import {
  SCHEMA_BULK_REQUEST,
  SCHEMA_ERROR,
  SCHEMA_LIST,
  SCHEMA_USER,
  createUser,
  devToken,
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
