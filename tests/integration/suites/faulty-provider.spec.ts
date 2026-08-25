import { describe, expect, it } from "vitest";
import {
  SCHEMA_BULK_REQUEST,
  SCHEMA_ERROR,
  SCHEMA_GROUP,
  SCHEMA_PATCH,
  faulty,
  groupBody,
  userBody,
} from "../src/client.js";

/**
 * What the service answers when the provider behind it faults.
 *
 * Every provider call is wrapped, discovery properties included, and what escapes has
 * to come back as the RFC 7644 section 3.12 error body - not an ASP.NET
 * ProblemDetails payload, not an empty response, and not a stack trace. A working
 * provider can never show this, and a merely unimplemented one throws an exception the
 * handlers map to 501 instead.
 *
 * These run against a host started with SCIM_PROVIDER=faulty.
 */

const identifier = "66666666-6666-6666-6666-666666666666";

/** A SCIM error body, and nothing of the provider's internals in it. */
function expectScimServerError(response: { status: number; body: any; text: string }): void {
  expect(response.status).toBe(500);
  expect(response.body.schemas).toContain(SCHEMA_ERROR);
  expect(response.body.status).toBe("500");

  // A leaked stack trace tells an attacker the framework, the file layout and the
  // line numbers. The detail may name the failure; it may not carry the trace.
  expect(response.text).not.toContain("   at ");
  expect(response.text).not.toContain(".cs:line");
}

describe("A provider that faults: resource operations", () => {
  it("answers a SCIM 500 to a create", async () => {
    expectScimServerError(await faulty("POST", "/Users", userBody()));
  });

  it("answers a SCIM 500 to a read", async () => {
    expectScimServerError(await faulty("GET", `/Users/${identifier}`));
  });

  it("answers a SCIM 500 to a query", async () => {
    expectScimServerError(await faulty("GET", "/Users"));
  });

  it("answers a SCIM 500 to a filtered query", async () => {
    expectScimServerError(
      await faulty("GET", `/Users?filter=${encodeURIComponent('userName eq "a@b.sg"')}`),
    );
  });

  it("answers a SCIM 500 to a replace", async () => {
    expectScimServerError(
      await faulty("PUT", `/Users/${identifier}`, { ...userBody(), id: identifier }),
    );
  });

  it("answers a SCIM 500 to a patch", async () => {
    expectScimServerError(
      await faulty("PATCH", `/Users/${identifier}`, {
        schemas: [SCHEMA_PATCH],
        Operations: [{ op: "replace", path: "title", value: "x" }],
      }),
    );
  });

  it("answers a SCIM 500 to a delete", async () => {
    expectScimServerError(await faulty("DELETE", `/Users/${identifier}`));
  });

  it("answers a SCIM 500 across the group endpoints too", async () => {
    expectScimServerError(await faulty("POST", "/Groups", groupBody()));
    expectScimServerError(await faulty("GET", "/Groups"));
    expectScimServerError(await faulty("GET", `/Groups/${identifier}`));
    expectScimServerError(await faulty("DELETE", `/Groups/${identifier}`));
    expectScimServerError(
      await faulty("PUT", `/Groups/${identifier}`, {
        schemas: [SCHEMA_GROUP],
        id: identifier,
        displayName: "x",
      }),
    );
    expectScimServerError(
      await faulty("PATCH", `/Groups/${identifier}`, {
        schemas: [SCHEMA_PATCH],
        Operations: [{ op: "add", path: "members", value: [{ value: identifier }] }],
      }),
    );
  });
});

describe("A provider that faults: discovery", () => {
  /** Whatever the provider threw, a SCIM error body comes back and no trace leaks. */
  function expectScimError(response: { status: number; body: any; text: string }, status: number) {
    expect(response.status).toBe(status);
    expect(response.body.schemas).toContain(SCHEMA_ERROR);
    expect(response.text).not.toContain("   at ");
    expect(response.text).not.toContain(".cs:line");
  }

  it("answers a SCIM 500 for ServiceProviderConfig, which faulted unexpectedly", async () => {
    expectScimServerError(await faulty("GET", "/ServiceProviderConfig"));
  });

  it("answers 501 for ResourceTypes, which said it was not implemented", async () => {
    // NotImplementedException from a discovery property means the same as it does from a
    // resource operation, and must map the same way.
    expectScimError(await faulty("GET", "/ResourceTypes"), 501);
  });

  it("answers 501 for Schemas, which said it was not supported", async () => {
    expectScimError(await faulty("GET", "/Schemas"), 501);
  });
});

describe("A provider that faults: bulk", () => {
  it("reports the failure per operation rather than losing the whole request", async () => {
    const response = await faulty("POST", "/Bulk", {
      schemas: [SCHEMA_BULK_REQUEST],
      Operations: [
        { method: "POST", path: "/Users", bulkId: "a", data: userBody() },
        { method: "DELETE", path: `/Users/${identifier}`, bulkId: "b" },
      ],
    });

    // Either the request itself fails cleanly or each operation reports its own
    // failure. What it must not do is answer 200 with operations that claim success.
    expect([200, 500]).toContain(response.status);

    if (response.status === 200) {
      const operations = (response.body.Operations as { status: string }[]) ?? [];
      for (const operation of operations) {
        expect(Number(operation.status)).toBeGreaterThanOrEqual(400);
      }
    }
  });
});

describe("A provider that faults: what still works without it", () => {
  it("still refuses an unauthenticated request", async () => {
    // Authentication runs before the provider is reached, so a broken provider must not
    // turn into an open endpoint.
    const response = await faulty("GET", "/Users", undefined, { anonymous: true });

    expect(response.status).toBe(401);
  });

  it("still refuses a malformed body", async () => {
    const response = await faulty("POST", "/Users", undefined, { raw: "{ not json" });

    expect(response.status).toBe(400);
  });

  it("still answers 405 for a verb the endpoint does not define", async () => {
    const response = await faulty("HEAD", "/Users");

    expect(response.status).toBe(405);
  });
});

describe("A provider that faults: the status it chose", () => {
  // A provider is entitled to answer with a specific SCIM status - 403 for a caller
  // the store will not serve, 501 for an operation it does not offer, 429 for one it
  // is shedding. The handlers used to flatten those on three verbs: an item GET
  // turned anything but 404 into 500, and POST and PUT turned anything but
  // 404/409 into 400. The provider's answer never reached the client.
  //
  // FaultyProvider throws the status named in the request, so each verb can be asked
  // the same question. Requests without a marker still fault the ordinary way.
  const marker = (status: number): string => `status-${status}`;

  it.each([403, 429, 501])("preserves %i from a create", async (status) => {
    const response = await faulty("POST", "/Users", userBody({ userName: `${marker(status)}@x.sg` }));

    expect(response.status).toBe(status);
    expect(response.body.schemas).toContain(SCHEMA_ERROR);
    expect(response.body.status).toBe(String(status));
  });

  it.each([403, 429, 501])("preserves %i from an item read", async (status) => {
    const response = await faulty("GET", `/Users/${marker(status)}`);

    expect(response.status).toBe(status);
    expect(response.body.schemas).toContain(SCHEMA_ERROR);
  });

  it.each([403, 429, 501])("preserves %i from a replace", async (status) => {
    const response = await faulty(
      "PUT",
      `/Users/${marker(status)}`,
      userBody({ id: marker(status) }),
    );

    expect(response.status).toBe(status);
    expect(response.body.schemas).toContain(SCHEMA_ERROR);
  });

  it.each([403, 429])("preserves %i from a delete", async (status) => {
    const response = await faulty("DELETE", `/Users/${marker(status)}`);

    expect(response.status).toBe(status);
  });

  it.each([403, 429])("preserves %i from a patch", async (status) => {
    const response = await faulty("PATCH", `/Users/${marker(status)}`, {
      schemas: [SCHEMA_PATCH],
      Operations: [{ op: "replace", path: "title", value: "x" }],
    });

    expect(response.status).toBe(status);
  });

  it("preserves 403 from a collection query", async () => {
    const response = await faulty("GET", `/Users?filter=${encodeURIComponent('userName eq "status-403"')}`);

    expect(response.status).toBe(403);
  });

  it("still answers 500 when the provider faults for a reason it did not name", async () => {
    // The point of the marker is that it is opt-in: an unmarked request has to keep
    // producing the catch-all, or this change would have hidden every genuine fault.
    expectScimServerError(await faulty("POST", "/Users", userBody()));
    expectScimServerError(await faulty("GET", `/Users/${identifier}`));
  });
});
