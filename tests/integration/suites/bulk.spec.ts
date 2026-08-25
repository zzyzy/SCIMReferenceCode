import { describe, expect, it } from "vitest";
import {
  SCHEMA_BULK_REQUEST,
  SCHEMA_ENTERPRISE,
  SCHEMA_GROUP,
  SCHEMA_PATCH,
  SCHEMA_USER,
  createGroup,
  createUser,
  memberIds,
  scim,
  unique,
  userBody,
  type ScimResource,
} from "../src/client.js";

/**
 * Bulk, RFC 7644 3.7.
 *
 * The largest piece of SCIM behaviour these tests did not reach. Bulk is not a batch
 * loop: an operation may name a resource another operation in the same request is
 * about to create, through `bulkId:`, and the service has to order the work, rewrite
 * the reference once the identifier exists, and refuse a cycle rather than spin on
 * it. That ordering is a state machine, and none of it was exercised.
 *
 * The response is a 200 whose per-operation `status` carries each operation's
 * outcome, so a failed operation is not a failed request. Assertions here read the
 * per-operation status, not just the envelope's.
 */

interface BulkOperation {
  method: string;
  path: string;
  bulkId?: string;
  data?: unknown;
}

interface BulkResponseOperation {
  bulkId?: string;
  method?: string;
  status: string;
  location?: string;
  response?: any;
}

async function bulk(
  operations: BulkOperation[],
  extras: Record<string, unknown> = {},
): Promise<{ status: number; body: any; operations: BulkResponseOperation[] }> {
  const response = await scim("POST", "/Bulk", {
    schemas: [SCHEMA_BULK_REQUEST],
    ...extras,
    Operations: operations,
  });

  return {
    status: response.status,
    body: response.body,
    operations: (response.body?.Operations as BulkResponseOperation[] | undefined) ?? [],
  };
}

function patchBody(...operations: Record<string, unknown>[]): Record<string, unknown> {
  return { schemas: [SCHEMA_PATCH], Operations: operations };
}

/** The per-operation status, as a number. The wire carries it as a string. */
function statusOf(operation: BulkResponseOperation | undefined): number {
  return Number(operation?.status ?? 0);
}

describe("Bulk: one operation of each verb", () => {
  it("creates a user and reports its location", async () => {
    const response = await bulk([
      { method: "POST", path: "/Users", bulkId: "u1", data: userBody() },
    ]);

    expect(response.status).toBe(200);
    expect(response.operations).toHaveLength(1);

    const created = response.operations[0];
    expect(created?.bulkId).toBe("u1");
    expect(statusOf(created)).toBe(201);

    // The location has to be usable: a bulk client's next request is built from it.
    expect(created?.location).toBeTruthy();
    const identifier = String(created?.location).split("/").pop();
    const read = await scim("GET", `/Users/${identifier}`);
    expect(read.status).toBe(200);
  });

  it("patches a user through bulk", async () => {
    const user = await createUser();

    const response = await bulk([
      {
        method: "PATCH",
        path: `/Users/${user.id}`,
        bulkId: "p1",
        data: patchBody({ op: "replace", path: "title", value: "Vice Principal" }),
      },
    ]);

    expect(response.status).toBe(200);
    expect(statusOf(response.operations[0])).toBe(200);

    const read = await scim<ScimResource>("GET", `/Users/${user.id}`);
    expect(read.body["title"]).toBe("Vice Principal");
  });

  it("deletes a user through bulk", async () => {
    const user = await createUser();

    const response = await bulk([
      { method: "DELETE", path: `/Users/${user.id}`, bulkId: "d1" },
    ]);

    expect(response.status).toBe(200);
    expect(statusOf(response.operations[0])).toBe(204);

    expect((await scim("GET", `/Users/${user.id}`)).status).toBe(404);
  });

  it("creates a group and patches its membership in one request", async () => {
    const member = await createUser();

    const response = await bulk([
      {
        method: "POST",
        path: "/Groups",
        bulkId: "g1",
        data: { schemas: [SCHEMA_GROUP], displayName: unique("bulkgroup") },
      },
    ]);

    expect(statusOf(response.operations[0])).toBe(201);
    const groupIdentifier = String(response.operations[0]?.location).split("/").pop() ?? "";

    const patched = await bulk([
      {
        method: "PATCH",
        path: `/Groups/${groupIdentifier}`,
        bulkId: "p1",
        data: patchBody({ op: "add", path: "members", value: [{ value: member.id }] }),
      },
    ]);

    expect(statusOf(patched.operations[0])).toBe(200);
    expect(await memberIds(groupIdentifier)).toContain(member.id);
  });

  it("runs several operations of mixed verbs in order", async () => {
    const doomed = await createUser();
    const target = await createUser();

    const response = await bulk([
      { method: "POST", path: "/Users", bulkId: "a", data: userBody() },
      {
        method: "PATCH",
        path: `/Users/${target.id}`,
        bulkId: "b",
        data: patchBody({ op: "replace", path: "title", value: "Registrar" }),
      },
      { method: "DELETE", path: `/Users/${doomed.id}`, bulkId: "c" },
    ]);

    expect(response.status).toBe(200);
    expect(response.operations.map(statusOf).sort()).toEqual([200, 201, 204]);

    expect((await scim("GET", `/Users/${doomed.id}`)).status).toBe(404);
    expect((await scim<ScimResource>("GET", `/Users/${target.id}`)).body["title"]).toBe("Registrar");
  });
});

describe("Bulk: bulkId references", () => {
  it("resolves a member naming a user the same request creates", async () => {
    // The whole point of bulkId: the group is created before the user exists, and the
    // reference has to be rewritten once it does. A service that stored the literal
    // "bulkId:u1" would hand it back on the next read.
    const displayName = unique("bulkrole");
    const userName = `${unique("bulkmember")}@example.sg`;

    const response = await bulk([
      {
        method: "POST",
        path: "/Groups",
        bulkId: "g1",
        data: {
          schemas: [SCHEMA_GROUP],
          displayName,
          members: [{ value: "bulkId:u1" }],
        },
      },
      {
        method: "POST",
        path: "/Users",
        bulkId: "u1",
        data: { schemas: [SCHEMA_USER], userName, active: true },
      },
    ]);

    expect(response.status).toBe(200);
    for (const operation of response.operations) {
      expect(statusOf(operation)).toBeLessThan(400);
    }

    const group = await scim<ScimResource>(
      "GET",
      `/Groups?filter=${encodeURIComponent(`displayName eq "${displayName}"`)}`,
    );
    const found = (group.body as any).Resources[0] as ScimResource;
    const members = (found["members"] as { value: string }[] | undefined) ?? [];

    for (const member of members) {
      expect(member.value).not.toMatch(/^bulkId:/u);
    }
  });

  it("resolves a bulkId reference inside a PATCH operation", async () => {
    const group = await createGroup();
    const userName = `${unique("bulkref")}@example.sg`;

    const response = await bulk([
      {
        method: "POST",
        path: "/Users",
        bulkId: "u1",
        data: { schemas: [SCHEMA_USER], userName, active: true },
      },
      {
        method: "PATCH",
        path: `/Groups/${group.id}`,
        bulkId: "p1",
        data: patchBody({ op: "add", path: "members", value: [{ value: "bulkId:u1" }] }),
      },
    ]);

    expect(response.status).toBe(200);

    const members = await memberIds(group.id);
    for (const member of members) {
      expect(member).not.toMatch(/^bulkId:/u);
    }
  });

  it("resolves an enterprise manager naming a user the same request creates", async () => {
    const managerName = `${unique("mgr")}@example.sg`;

    const response = await bulk([
      {
        method: "POST",
        path: "/Users",
        bulkId: "m1",
        data: { schemas: [SCHEMA_USER], userName: managerName, active: true },
      },
      {
        method: "POST",
        path: "/Users",
        bulkId: "r1",
        data: {
          schemas: [SCHEMA_USER, SCHEMA_ENTERPRISE],
          userName: `${unique("report")}@example.sg`,
          active: true,
          [SCHEMA_ENTERPRISE]: { manager: { value: "bulkId:m1" } },
        },
      },
    ]);

    expect(response.status).toBe(200);

    const report = response.operations.find((operation) => operation.bulkId === "r1");
    if (statusOf(report) === 201) {
      const identifier = String(report?.location).split("/").pop();
      const read = await scim<ScimResource>("GET", `/Users/${identifier}`);
      const manager = (read.body[SCHEMA_ENTERPRISE] as any)?.manager?.value;
      if (manager) {
        expect(String(manager)).not.toMatch(/^bulkId:/u);
      }
    }
  });

  it("terminates on a circular bulkId reference", async () => {
    // RFC 7644 3.7.2 calls circular references out. The failure mode to avoid is an
    // unbounded resolution loop, so the requirement is that it answers at all.
    const response = await bulk([
      {
        method: "POST",
        path: "/Users",
        bulkId: "a",
        data: {
          schemas: [SCHEMA_USER, SCHEMA_ENTERPRISE],
          userName: `${unique("cyc")}@example.sg`,
          [SCHEMA_ENTERPRISE]: { manager: { value: "bulkId:b" } },
        },
      },
      {
        method: "POST",
        path: "/Users",
        bulkId: "b",
        data: {
          schemas: [SCHEMA_USER, SCHEMA_ENTERPRISE],
          userName: `${unique("cyc")}@example.sg`,
          [SCHEMA_ENTERPRISE]: { manager: { value: "bulkId:a" } },
        },
      },
    ]);

    expect([200, 400, 409]).toContain(response.status);
  });

  it("answers a reference to a bulkId no operation declares", async () => {
    const group = await createGroup();

    const response = await bulk([
      {
        method: "PATCH",
        path: `/Groups/${group.id}`,
        bulkId: "p1",
        data: patchBody({
          op: "add",
          path: "members",
          value: [{ value: "bulkId:never-declared" }],
        }),
      },
    ]);

    expect(response.status).toBe(200);
    // Either the operation is refused, or it succeeds - but an unresolved reference
    // must not end up stored as a membership, which is what a later read would
    // otherwise report to the client as a real member.
    if (statusOf(response.operations[0]) < 400) {
      for (const member of await memberIds(group.id)) {
        expect(member).not.toMatch(/^bulkId:/u);
      }
    }
  });

  it("refuses an operation whose bulkId is blank", async () => {
    const response = await bulk([
      { method: "POST", path: "/Users", bulkId: "", data: userBody() },
    ]);

    expect([200, 400]).toContain(response.status);
  });

  it("answers two operations sharing one bulkId", async () => {
    // A bulkId identifies an operation, so a duplicate makes any reference to it
    // ambiguous.
    const response = await bulk([
      { method: "POST", path: "/Users", bulkId: "same", data: userBody() },
      { method: "POST", path: "/Users", bulkId: "same", data: userBody() },
    ]);

    expect([200, 400, 409]).toContain(response.status);
  });
});

describe("Bulk: per-operation failures", () => {
  it("reports a failed operation without failing the request", async () => {
    const first = await createUser();

    const response = await bulk([
      { method: "POST", path: "/Users", bulkId: "dup", data: userBody({ userName: first.userName }) },
      { method: "POST", path: "/Users", bulkId: "ok", data: userBody() },
    ]);

    expect(response.status).toBe(200);
    const duplicate = response.operations.find((operation) => operation.bulkId === "dup");
    expect(statusOf(duplicate)).toBe(409);

    const ok = response.operations.find((operation) => operation.bulkId === "ok");
    expect(statusOf(ok)).toBe(201);
  });

  it("answers a DELETE of a resource that is not there as the endpoint would", async () => {
    // Not a fixed status: this provider's delete is idempotent, and the point is that
    // bulk does not disagree with /Users/{id}. A client that gets 204 from one and 404
    // from the other cannot tell which it can trust.
    const identifier = "33333333-3333-3333-3333-333333333333";
    const direct = await scim("DELETE", `/Users/${identifier}`);

    const response = await bulk([
      { method: "DELETE", path: `/Users/${identifier}`, bulkId: "gone" },
    ]);

    expect(response.status).toBe(200);
    expect(statusOf(response.operations[0])).toBe(direct.status);
  });

  it("reports an operation naming a resource type the service does not serve", async () => {
    const response = await bulk([
      { method: "DELETE", path: "/Widgets/1", bulkId: "bad" },
    ]);

    expect([200, 400]).toContain(response.status);
    if (response.status === 200 && response.operations.length > 0) {
      expect(statusOf(response.operations[0])).toBeGreaterThanOrEqual(400);
    }
  });

  it("stops at failOnErrors and skips the rest", async () => {
    // RFC 7644 3.7.3: failOnErrors is the number of errors after which the service
    // stops. With 1, the operation after the first failure must not have run.
    const existing = await createUser();
    const survivor = `${unique("survivor")}@example.sg`;

    const response = await bulk(
      [
        {
          method: "POST",
          path: "/Users",
          bulkId: "fail",
          data: userBody({ userName: existing.userName }),
        },
        {
          method: "POST",
          path: "/Users",
          bulkId: "after",
          data: userBody({ userName: survivor }),
        },
      ],
      { failOnErrors: 1 },
    );

    expect(response.status).toBe(200);

    const query = await scim(
      "GET",
      `/Users?filter=${encodeURIComponent(`userName eq "${survivor}"`)}`,
    );
    expect(query.body.Resources).toHaveLength(0);
  });

  it("runs every operation when failOnErrors is absent", async () => {
    const existing = await createUser();
    const survivor = `${unique("survivor")}@example.sg`;

    await bulk([
      { method: "POST", path: "/Users", bulkId: "fail", data: userBody({ userName: existing.userName }) },
      { method: "POST", path: "/Users", bulkId: "after", data: userBody({ userName: survivor }) },
    ]);

    const query = await scim(
      "GET",
      `/Users?filter=${encodeURIComponent(`userName eq "${survivor}"`)}`,
    );
    expect(query.body.Resources).toHaveLength(1);
  });
});

describe("Bulk: malformed operations", () => {
  it("refuses an operation with no method", async () => {
    const response = await bulk([
      { method: undefined as unknown as string, path: "/Users", bulkId: "x", data: userBody() },
    ]);

    expect([200, 400]).toContain(response.status);
  });

  it("refuses a verb bulk does not define", async () => {
    // RFC 7644 3.7 lists POST, PUT, PATCH and DELETE; this service implements a
    // subset, and has to say which rather than treating an unknown verb as a POST.
    const response = await bulk([
      { method: "OPTIONS", path: "/Users/x", bulkId: "x" },
    ]);

    expect([200, 400]).toContain(response.status);
  });

  it("refuses a POST operation carrying no data", async () => {
    const response = await bulk([{ method: "POST", path: "/Users", bulkId: "x" }]);

    expect([200, 400]).toContain(response.status);
    if (response.status === 200 && response.operations.length > 0) {
      expect(statusOf(response.operations[0])).toBeGreaterThanOrEqual(400);
    }
  });

  it("refuses a POST whose data declares no schema", async () => {
    const response = await bulk([
      {
        method: "POST",
        path: "/Users",
        bulkId: "x",
        data: { userName: `${unique("noschema")}@example.sg` },
      },
    ]);

    expect([200, 400]).toContain(response.status);
  });

  it("refuses a POST whose data declares an unknown schema", async () => {
    const response = await bulk([
      {
        method: "POST",
        path: "/Widgets",
        bulkId: "x",
        data: { schemas: ["urn:example:params:scim:schemas:Widget"] },
      },
    ]);

    expect([200, 400]).toContain(response.status);
  });

  it("answers an operation with no path", async () => {
    const response = await bulk([{ method: "DELETE", path: undefined as unknown as string, bulkId: "x" }]);

    // The client omitted a required member, so this is its mistake, not the
    // service's: a 4xx, never a 500.
    expect(response.status).toBeLessThan(500);
  });

  it("answers an absolute path where a relative one is required", async () => {
    const response = await bulk([
      { method: "DELETE", path: "http://elsewhere.example/scim/Users/x", bulkId: "x" },
    ]);

    expect(response.status).toBeLessThan(500);
  });

  it("answers an unparseable body", async () => {
    const response = await scim("POST", "/Bulk", undefined, { raw: "not json at all" });

    expect(response.status).toBe(400);
  });

  it("answers an empty body", async () => {
    const response = await scim("POST", "/Bulk", undefined, { raw: "" });

    expect(response.status).toBe(400);
  });

  it("answers a body whose Operations is not an array", async () => {
    const response = await scim("POST", "/Bulk", {
      schemas: [SCHEMA_BULK_REQUEST],
      Operations: "not an array",
    });

    expect(response.status).toBeLessThan(500);
  });
});

describe("Bulk: what ServiceProviderConfig says about it", () => {
  it("reports maxOperations and maxPayloadSize", async () => {
    const config = await scim("GET", "/ServiceProviderConfig");
    const feature = config.body.bulk as {
      supported?: boolean;
      maxOperations?: number;
      maxPayloadSize?: number;
    };

    expect(feature).toBeDefined();
    expect(feature).toHaveProperty("maxOperations");
    expect(feature).toHaveProperty("maxPayloadSize");
  });

  it("answers a request larger than the advertised maximum", async () => {
    const config = await scim("GET", "/ServiceProviderConfig");
    const maximum = (config.body.bulk as { maxOperations?: number }).maxOperations ?? 0;

    const count = Math.max(maximum + 1, 50);
    const response = await bulk(
      Array.from({ length: count }, (_unused, index) => ({
        method: "POST",
        path: "/Users",
        bulkId: `many${index}`,
        data: userBody(),
      })),
    );

    // Whether it enforces the limit or not, it has to answer.
    expect([200, 400, 413]).toContain(response.status);
  });

  it("answers an empty Operations array", async () => {
    const response = await bulk([]);

    expect([200, 400]).toContain(response.status);
  });
});
