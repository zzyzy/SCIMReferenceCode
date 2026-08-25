import { describe, expect, it } from "vitest";
import {
  PATCH_APPLIED,
  SCHEMA_ERROR,
  SCHEMA_USER,
  edupassUinFin,
  patchOp,
  unique,
  type ScimResource,
} from "../src/client.js";

/**
 * Edupass at a relying party that stores UIN/FIN.
 *
 * One flag governs both what `/Schemas` advertises and what validation requires, so
 * that the two cannot drift apart - a party that advertises `uinFin` and does not
 * enforce it, or enforces it and does not advertise it, would leave Edupass sending
 * either too much or too little. Both halves are only observable on a host constructed
 * with the flag on, which is why they live in their own file.
 *
 * The counterpart, a party that does not store it, is in edupass.spec.ts.
 */

const EXTENSION = "urn:ietf:params:scim:schemas:extension:Edupass:2.0:User";

function body(extension: Record<string, unknown>): Record<string, unknown> {
  const userName = `${unique("uin")}@moe.edu.sg`;
  return {
    schemas: [SCHEMA_USER, EXTENSION],
    userName,
    active: true,
    emails: [{ value: userName, type: "WOG", primary: true }],
    [EXTENSION]: extension,
  };
}

function expectInvalidValue(response: { status: number; body: any }): void {
  expect(response.status).toBe(400);
  expect(response.body.schemas).toContain(SCHEMA_ERROR);
  expect(response.body.scimType).toBe("invalidValue");
}

describe("Edupass with UIN/FIN: what the schema advertises", () => {
  it("advertises uinFin", async () => {
    const response = await edupassUinFin("GET", "/Schemas");
    const extension = (
      response.body.Resources as { id: string; attributes?: { name: string }[] }[]
    ).find((item) => item.id === EXTENSION);

    expect(extension).toBeDefined();
    expect((extension?.attributes ?? []).map((attribute) => attribute.name)).toContain("uinFin");
  });

  it("advertises the other three attributes alongside it", async () => {
    const response = await edupassUinFin("GET", "/Schemas");
    const extension = (
      response.body.Resources as { id: string; attributes?: { name: string }[] }[]
    ).find((item) => item.id === EXTENSION);

    const names = (extension?.attributes ?? []).map((attribute) => attribute.name);
    for (const expected of ["identityType", "schoolOrHq", "identitySource"]) {
      expect(names).toContain(expected);
    }
  });
});

describe("Edupass with UIN/FIN: what validation requires", () => {
  it("accepts a human identity carrying a well-formed UIN/FIN", async () => {
    const response = await edupassUinFin(
      "POST",
      "/Users",
      body({ identityType: "Staff", uinFin: "S1234567A", schoolOrHq: "School" }),
    );

    expect(response.status).toBe(201);
  });

  it.each(["Student", "Staff", "Temp", "Intern", "Vendor", "Others"])(
    "refuses a %s with no UIN/FIN",
    async (identityType) => {
      expectInvalidValue(await edupassUinFin("POST", "/Users", body({ identityType })));
    },
  );

  it("accepts a non-human identity with no UIN/FIN", async () => {
    // A service account never has one, so its absence is only an error for an identity
    // Edupass has said is a person.
    const response = await edupassUinFin(
      "POST",
      "/Users",
      body({ identityType: "Non-human", schoolOrHq: "HQ" }),
    );

    expect(response.status).toBe(201);
  });

  it("matches the non-human check without regard to case", async () => {
    const response = await edupassUinFin("POST", "/Users", body({ identityType: "Non-human" }));

    expect(response.status).toBe(201);
  });

  it("refuses a user carrying no extension at all", async () => {
    // A party that stores UIN/FIN cannot accept an identity that declares nothing about
    // itself, so the extension stops being optional.
    const userName = `${unique("uin")}@moe.edu.sg`;
    const response = await edupassUinFin("POST", "/Users", {
      schemas: [SCHEMA_USER],
      userName,
      active: true,
    });

    expectInvalidValue(response);
  });

  it("still refuses a malformed UIN/FIN", async () => {
    expectInvalidValue(
      await edupassUinFin("POST", "/Users", body({ identityType: "Staff", uinFin: "S123A" })),
    );
  });

  it("refuses a UIN/FIN that is only whitespace", async () => {
    expectInvalidValue(
      await edupassUinFin("POST", "/Users", body({ identityType: "Staff", uinFin: "   " })),
    );
  });

  it("round-trips the UIN/FIN it stores", async () => {
    const created = await edupassUinFin<ScimResource>(
      "POST",
      "/Users",
      body({ identityType: "Student", uinFin: "T7654321Z" }),
    );

    expect(created.status).toBe(201);
    const read = await edupassUinFin<ScimResource>("GET", `/Users/${created.body.id}`);
    expect((read.body[EXTENSION] as any).uinFin).toBe("T7654321Z");
  });
});

describe("Edupass with UIN/FIN: changing it later", () => {
  it("replaces the UIN/FIN through PATCH", async () => {
    const created = await edupassUinFin<ScimResource>(
      "POST",
      "/Users",
      body({ identityType: "Staff", uinFin: "S1234567A" }),
    );

    expect(PATCH_APPLIED).toContain(
      (
        await edupassUinFin(
          "PATCH",
          `/Users/${created.body.id}`,
          patchOp({ op: "replace", path: `${EXTENSION}:uinFin`, value: "F0000000B" }),
        )
      ).status,
    );

    const read = await edupassUinFin<ScimResource>("GET", `/Users/${created.body.id}`);
    expect((read.body[EXTENSION] as any).uinFin).toBe("F0000000B");
  });

  it("refuses a PATCH that would remove the UIN/FIN of a person", async () => {
    // The clone is validated before it is committed, so a PATCH that would leave the
    // resource invalid is refused and the stored one is untouched.
    const created = await edupassUinFin<ScimResource>(
      "POST",
      "/Users",
      body({ identityType: "Staff", uinFin: "S1234567A" }),
    );

    const patched = await edupassUinFin(
      "PATCH",
      `/Users/${created.body.id}`,
      patchOp({ op: "remove", path: `${EXTENSION}:uinFin` }),
    );

    expect(patched.status).toBe(400);

    const read = await edupassUinFin<ScimResource>("GET", `/Users/${created.body.id}`);
    expect((read.body[EXTENSION] as any).uinFin).toBe("S1234567A");
  });

  it("allows removing the UIN/FIN of a non-human identity", async () => {
    const created = await edupassUinFin<ScimResource>(
      "POST",
      "/Users",
      body({ identityType: "Non-human", uinFin: "S1234567A" }),
    );

    const patched = await edupassUinFin(
      "PATCH",
      `/Users/${created.body.id}`,
      patchOp({ op: "remove", path: `${EXTENSION}:uinFin` }),
    );

    expect(PATCH_APPLIED).toContain(patched.status);
  });

  it("refuses a PATCH that makes a non-human identity human without a UIN/FIN", async () => {
    const created = await edupassUinFin<ScimResource>(
      "POST",
      "/Users",
      body({ identityType: "Non-human" }),
    );

    const patched = await edupassUinFin(
      "PATCH",
      `/Users/${created.body.id}`,
      patchOp({ op: "replace", path: `${EXTENSION}:identityType`, value: "Staff" }),
    );

    expect(patched.status).toBe(400);
  });

  it("refuses a replace that drops the UIN/FIN of a person", async () => {
    const created = await edupassUinFin<ScimResource>(
      "POST",
      "/Users",
      body({ identityType: "Staff", uinFin: "S1234567A" }),
    );

    const replaced = await edupassUinFin("PUT", `/Users/${created.body.id}`, {
      schemas: [SCHEMA_USER, EXTENSION],
      id: created.body.id,
      userName: created.body.userName,
      active: true,
      [EXTENSION]: { identityType: "Staff" },
    });

    expect(replaced.status).toBe(400);
  });
});
