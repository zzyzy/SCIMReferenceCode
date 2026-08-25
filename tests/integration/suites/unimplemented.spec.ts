import { describe, expect, it } from "vitest";
import {
  SCHEMA_BULK_REQUEST,
  SCHEMA_ERROR,
  SCHEMA_GROUP,
  SCHEMA_PATCH,
  SCHEMA_USER,
  groupBody,
  unimplemented,
  userBody,
} from "../src/client.js";

/**
 * What a provider that implements nothing answers.
 *
 * `ProviderBase` throws `NotImplementedException` from every operation it does not
 * override, and the shared handlers are supposed to turn that into 501 Not Implemented.
 * The distinction matters to a client - it can retry around a 501 and cannot around a
 * 500 - and it is the one behaviour a working provider can never demonstrate, because a
 * working provider never throws it.
 *
 * These run against a host started with SCIM_PROVIDER=unimplemented. Every request here
 * is one that succeeds on the other hosts, so a failure means the handler let the
 * exception escape rather than mapping it.
 */

/** Not implemented is the answer. A 500 means the exception escaped the handler. */
function expectNotImplemented(response: { status: number; body: any }): void {
  expect(response.status).toBe(501);
  expect(response.status).not.toBe(500);
}

const identifier = "55555555-5555-5555-5555-555555555555";

describe("A provider that implements nothing: users", () => {
  it("answers 501 to a create", async () => {
    expectNotImplemented(await unimplemented("POST", "/Users", userBody()));
  });

  it("answers 501 to a read", async () => {
    expectNotImplemented(await unimplemented("GET", `/Users/${identifier}`));
  });

  it("answers 501 to a query", async () => {
    expectNotImplemented(await unimplemented("GET", "/Users"));
  });

  it("answers 501 to a filtered query", async () => {
    expectNotImplemented(
      await unimplemented("GET", `/Users?filter=${encodeURIComponent('userName eq "a@b.sg"')}`),
    );
  });

  it("answers 501 to a replace", async () => {
    expectNotImplemented(
      await unimplemented("PUT", `/Users/${identifier}`, { ...userBody(), id: identifier }),
    );
  });

  it("answers 501 to a patch", async () => {
    expectNotImplemented(
      await unimplemented("PATCH", `/Users/${identifier}`, {
        schemas: [SCHEMA_PATCH],
        Operations: [{ op: "replace", path: "title", value: "x" }],
      }),
    );
  });

  it("answers 501 to a delete", async () => {
    expectNotImplemented(await unimplemented("DELETE", `/Users/${identifier}`));
  });

  it("answers 501 to a projected read", async () => {
    expectNotImplemented(
      await unimplemented("GET", `/Users/${identifier}?attributes=userName`),
    );
  });

  it("answers 501 to a paged query", async () => {
    expectNotImplemented(await unimplemented("GET", "/Users?startIndex=1&count=10"));
  });
});

describe("A provider that implements nothing: groups", () => {
  it("answers 501 to a create", async () => {
    expectNotImplemented(await unimplemented("POST", "/Groups", groupBody()));
  });

  it("answers 501 to a read", async () => {
    expectNotImplemented(await unimplemented("GET", `/Groups/${identifier}`));
  });

  it("answers 501 to a query", async () => {
    expectNotImplemented(await unimplemented("GET", "/Groups"));
  });

  it("answers 501 to a replace", async () => {
    expectNotImplemented(
      await unimplemented("PUT", `/Groups/${identifier}`, {
        schemas: [SCHEMA_GROUP],
        id: identifier,
        displayName: "x",
      }),
    );
  });

  it("answers 501 to a membership patch", async () => {
    expectNotImplemented(
      await unimplemented("PATCH", `/Groups/${identifier}`, {
        schemas: [SCHEMA_PATCH],
        Operations: [{ op: "add", path: "members", value: [{ value: identifier }] }],
      }),
    );
  });

  it("answers 501 to a delete", async () => {
    expectNotImplemented(await unimplemented("DELETE", `/Groups/${identifier}`));
  });
});

describe("A provider that implements nothing: bulk", () => {
  it("reports each operation as not implemented rather than failing the request", async () => {
    // The per-operation mapping again: an unimplemented operation is that operation's
    // failure, not the bulk request's.
    const response = await unimplemented("POST", "/Bulk", {
      schemas: [SCHEMA_BULK_REQUEST],
      Operations: [
        { method: "POST", path: "/Users", bulkId: "a", data: userBody() },
        { method: "DELETE", path: `/Users/${identifier}`, bulkId: "b" },
      ],
    });

    expect(response.status).toBe(200);
    const operations = (response.body.Operations as { status: string }[]) ?? [];
    expect(operations).toHaveLength(2);
    for (const operation of operations) {
      expect(Number(operation.status)).toBe(501);
    }
  });
});

describe("A provider that implements nothing: discovery", () => {
  it("still serves ServiceProviderConfig, which the base class supplies", async () => {
    // Discovery is answered by ProviderBase itself, so it works where the resource
    // operations do not - which is what makes a service discoverable before it is built.
    const response = await unimplemented("GET", "/ServiceProviderConfig");

    expect(response.status).toBe(200);
    expect(response.body).toHaveProperty("patch");
  });

  it("still serves ResourceTypes", async () => {
    const response = await unimplemented("GET", "/ResourceTypes");

    expect(response.status).toBe(200);
    expect(Array.isArray(response.body.Resources)).toBe(true);
  });

  it("serves an empty Schemas collection rather than failing", async () => {
    const response = await unimplemented("GET", "/Schemas");

    expect(response.status).toBe(200);
    expect(response.body.Resources ?? []).toHaveLength(0);
  });
});

describe("A provider that implements nothing: the error body", () => {
  it("carries a SCIM error body, not an ASP.NET one", async () => {
    // RFC 7644 3.12 defines the body; a ProblemDetails payload would be a parity break
    // between the two hosting legs as well as wrong.
    const response = await unimplemented("GET", `/Users/${identifier}`);

    expect(response.status).toBe(501);
    expect(response.body.schemas).toContain(SCHEMA_ERROR);
    expect(response.body.status).toBe("501");
  });

  it("still refuses an unauthenticated request before reaching the provider", async () => {
    const response = await unimplemented("GET", "/Users", undefined, { anonymous: true });

    expect(response.status).toBe(401);
  });

  it("still refuses a malformed body before reaching the provider", async () => {
    const response = await unimplemented("POST", "/Users", undefined, { raw: "{ not json" });

    expect(response.status).toBe(400);
  });

  it("answers 501 for a schema the provider does not serve", async () => {
    expectNotImplemented(
      await unimplemented("POST", "/Users", { schemas: [SCHEMA_USER], userName: "a@b.sg" }),
    );
  });
});
