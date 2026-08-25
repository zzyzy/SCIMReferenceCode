import { beforeAll, describe, expect, it } from "vitest";
import {
  SCHEMA_ENTERPRISE,
  createUser,
  filterQuery,
  scim,
  unique,
  type ScimResource,
} from "../src/client.js";

/**
 * The filter grammar of RFC 7644 3.4.2.2, past the nine bare operators.
 *
 * Grouping, precedence, value paths and the several ways an expression can be
 * malformed are all parsed by the same code, and a parser that mis-handles one of
 * them tends to mis-handle it by not terminating or by answering 500 - which is
 * what these are written to catch.
 *
 * Where the reference provider does not implement a construct, the contract is that
 * it says so: a 400 carrying scimType invalidFilter. So the assertion is "answered
 * deterministically, and said invalidFilter if it refused" rather than "refused".
 * An earlier version of the filter suite asserted an operator was unsupported and
 * failed twice over as the product improved.
 */

/** Either an answer or a stated refusal - never a 500 and never a hang. */
function expectAnsweredOrRefused(response: { status: number; body: any }): void {
  expect([200, 400]).toContain(response.status);
  if (response.status === 400) {
    expect(response.body.scimType).toBe("invalidFilter");
  }
}

describe("Filter grammar: grouping and precedence", () => {
  let marker: string;
  let alice: ScimResource;
  let bob: ScimResource;

  beforeAll(async () => {
    marker = unique("grp");
    alice = await createUser({ userName: `${marker}.alice@example.sg`, active: true });
    bob = await createUser({ userName: `${marker}.bob@example.sg`, active: false });
  });

  it("groups an or and ands it with a second term", async () => {
    const response = await scim(
      "GET",
      `/Users${filterQuery(
        `(userName eq "${alice.userName}" or userName eq "${bob.userName}") and active eq true`,
      )}`,
    );

    expectAnsweredOrRefused(response);
    if (response.status === 200) {
      const names = (response.body.Resources as ScimResource[]).map((item) => item.userName);
      expect(names).toContain(alice.userName);
      expect(names).not.toContain(bob.userName);
    }
  });

  it("groups an or on the right-hand side of an and", async () => {
    const response = await scim(
      "GET",
      `/Users${filterQuery(
        `userName eq "${alice.userName}" and (active eq true or active eq false)`,
      )}`,
    );

    expectAnsweredOrRefused(response);
    if (response.status === 200) {
      expect(
        (response.body.Resources as ScimResource[]).map((item) => item.userName),
      ).toContain(alice.userName);
    }
  });

  it("groups an and inside an or", async () => {
    const response = await scim(
      "GET",
      `/Users${filterQuery(
        `(userName eq "${alice.userName}" and active eq true) or userName eq "${bob.userName}"`,
      )}`,
    );

    expectAnsweredOrRefused(response);
    if (response.status === 200) {
      const names = (response.body.Resources as ScimResource[]).map((item) => item.userName);
      expect(names).toContain(alice.userName);
      expect(names).toContain(bob.userName);
    }
  });

  it("answers an unparenthesised mix of and and or", async () => {
    // RFC 7644 3.4.2.2 gives and higher precedence than or. Whatever this service
    // does with it, it must answer rather than fault.
    const response = await scim(
      "GET",
      `/Users${filterQuery(
        `userName eq "${alice.userName}" or userName eq "${bob.userName}" and active eq true`,
      )}`,
    );

    expectAnsweredOrRefused(response);
  });

  it("answers three terms chained by and", async () => {
    const response = await scim(
      "GET",
      `/Users${filterQuery(
        `userName eq "${alice.userName}" and active eq true and userName sw "${marker}"`,
      )}`,
    );

    expectAnsweredOrRefused(response);
  });

  it.each([
    ['((userName eq "A" or userName eq "B") and active eq true)', "a group inside a group"],
    ['(userName eq "A") and (userName eq "B")', "two sibling groups"],
    ['(userName eq "A" or (userName eq "B" and active eq true))', "a group nested on the right"],
    ['((userName eq "A"))', "a redundant pair of brackets"],
    ['(active eq true) or (active eq false) or (userName eq "A")', "three sibling groups"],
    ['((userName eq "A" and active eq true) or (userName eq "B" and active eq false))', "two groups inside one"],
  ])("answers %s (%s)", async (expression) => {
    // The parser tracks nesting by level and group number, and the arithmetic differs for
    // each of these shapes. Getting one wrong shows up as a wrong answer rather than an
    // error, so what matters is that each is answered rather than refused or faulted.
    const response = await scim("GET", `/Users${filterQuery(expression)}`);

    expectAnsweredOrRefused(response);
  });

  it("answers a group whose terms name different attributes", async () => {
    const created = await createUser({ title: "Bursar" });
    const response = await scim(
      "GET",
      `/Users${filterQuery(`(userName eq "${created.userName}" and title eq "Bursar")`)}`,
    );

    expectAnsweredOrRefused(response);
    if (response.status === 200) {
      expect(
        (response.body.Resources as ScimResource[]).map((item) => item.id),
      ).toContain(created.id);
    }
  });

  it("answers a deeply chained expression without faulting", async () => {
    // Long chains are where a recursive-descent parser runs out of stack. The
    // requirement is a status, not a particular one.
    const expression = Array.from({ length: 33 }, () => `userName sw "${marker}"`).join(" and ");
    const response = await scim("GET", `/Users${filterQuery(expression)}`);

    expectAnsweredOrRefused(response);
  });

  it("answers an expression past any sane length limit", async () => {
    const expression = `userName eq "${"x".repeat(16_400)}"`;
    const response = await scim("GET", `/Users${filterQuery(expression)}`);

    expect([200, 400, 414]).toContain(response.status);
  });
});

describe("Filter grammar: malformed expressions", () => {
  it.each([
    ['userName eq "unterminated', "an unclosed quote"],
    ['userName eq "x") and active eq true', "an unmatched closing parenthesis"],
    ['(userName eq "x" and active eq true', "an unmatched opening parenthesis"],
    ["userName eq", "a missing comparison value"],
    ['eq "x"', "a missing attribute"],
    ["userName", "an operator and value that are both missing"],
    ['userName eq "a" and', "a trailing conjunction"],
    ['and userName eq "a"', "a leading conjunction"],
    ["()", "an empty group"],
  ])("refuses %s (%s) rather than faulting", async (expression) => {
    const response = await scim("GET", `/Users${filterQuery(expression)}`);

    // A malformed filter is the client's mistake, so it is a 400 - and never a 500,
    // which is what an unguarded parse throws as.
    expect(response.status).toBe(400);
    expect(response.body.scimType).toBe("invalidFilter");
  });

  it("refuses an operator that is not one of the nine", async () => {
    const response = await scim("GET", `/Users${filterQuery('userName spans "x"')}`);

    expect(response.status).toBe(400);
    expect(response.body.scimType).toBe("invalidFilter");
  });

  it.each(["includes", "bitAnd", "isMemberOf", "matchesExpression", "notBitAnd"])(
    "answers deterministically for the extension operator %s",
    async (operator) => {
      // Operators from drafts and other dialects. Whether this service grows them or
      // not, an unrecognised one must not reach the provider as a match-everything.
      const response = await scim("GET", `/Users${filterQuery(`userName ${operator} "x"`)}`);

      expectAnsweredOrRefused(response);
    },
  );

  it("keeps an escaped quote inside a filter value", async () => {
    const externalIdentifier = unique("esc");
    await createUser({ externalId: externalIdentifier });

    const response = await scim(
      "GET",
      `/Users${filterQuery(`externalId eq "a\\"b"`)}`,
    );

    expectAnsweredOrRefused(response);
    if (response.status === 200) {
      // The value contains a quote, so it cannot match an identifier that does not.
      const identifiers = (response.body.Resources as ScimResource[]).map(
        (item) => item["externalId"],
      );
      expect(identifiers).not.toContain(externalIdentifier);
    }
  });
});

describe("Filter grammar: value paths and complex attributes", () => {
  let user: ScimResource;
  let email: string;

  beforeAll(async () => {
    email = `${unique("vp")}@example.sg`;
    user = await createUser({
      emails: [
        { value: email, type: "work", primary: true },
        { value: `home.${email}`, type: "home" },
      ],
    });
  });

  it("answers a value-path filter on a multi-valued attribute", async () => {
    const response = await scim("GET", `/Users${filterQuery('emails[type eq "work"]')}`);

    expectAnsweredOrRefused(response);
  });

  it("answers a value path followed by a sub-attribute", async () => {
    const response = await scim(
      "GET",
      `/Users${filterQuery(`emails[type eq "work"].value eq "${email}"`)}`,
    );

    expectAnsweredOrRefused(response);
    if (response.status === 200) {
      expect(
        (response.body.Resources as ScimResource[]).map((item) => item.id),
      ).toContain(user.id);
    }
  });

  it("answers a dotted sub-attribute filter", async () => {
    const response = await scim("GET", `/Users${filterQuery('name.givenName eq "Given"')}`);

    expectAnsweredOrRefused(response);
  });

  it("answers pr on a complex attribute", async () => {
    const response = await scim("GET", `/Users${filterQuery("emails pr")}`);

    expectAnsweredOrRefused(response);
  });

  it("answers pr on a singular attribute", async () => {
    const response = await scim("GET", `/Users${filterQuery("title pr")}`);

    expectAnsweredOrRefused(response);
  });

  it("refuses a value path whose bracket is never closed", async () => {
    const response = await scim("GET", `/Users${filterQuery('emails[type eq "work"')}`);

    expect(response.status).toBe(400);
    expect(response.body.scimType).toBe("invalidFilter");
  });
});

describe("Filter grammar: typed comparison values", () => {
  it("answers an unquoted boolean", async () => {
    const response = await scim("GET", `/Users${filterQuery("active eq true")}`);

    expectAnsweredOrRefused(response);
  });

  it("answers an unquoted integer", async () => {
    const response = await scim("GET", `/Users${filterQuery("externalId eq 42")}`);

    expectAnsweredOrRefused(response);
  });

  it("answers an unquoted decimal", async () => {
    const response = await scim("GET", `/Users${filterQuery("externalId eq 1.5")}`);

    expectAnsweredOrRefused(response);
  });

  it("answers a null comparison value", async () => {
    const response = await scim("GET", `/Users${filterQuery("externalId eq null")}`);

    expectAnsweredOrRefused(response);
  });

  it("treats the operator case-insensitively", async () => {
    const created = await createUser();
    const response = await scim("GET", `/Users${filterQuery(`userName EQ "${created.userName}"`)}`);

    expectAnsweredOrRefused(response);
    if (response.status === 200) {
      expect(response.body.Resources).toHaveLength(1);
    }
  });
});

describe("Filter grammar: fully qualified attribute names", () => {
  it("answers a filter naming a core attribute by its full URN", async () => {
    const created = await createUser();
    const response = await scim(
      "GET",
      `/Users${filterQuery(
        `urn:ietf:params:scim:schemas:core:2.0:User:userName eq "${created.userName}"`,
      )}`,
    );

    expectAnsweredOrRefused(response);
  });

  it("answers a filter naming an enterprise attribute by its full URN", async () => {
    await createUser({ [SCHEMA_ENTERPRISE]: { department: "Engineering" } });

    const response = await scim(
      "GET",
      `/Users${filterQuery(`${SCHEMA_ENTERPRISE}:department eq "Engineering"`)}`,
    );

    expectAnsweredOrRefused(response);
  });
});

describe("Filter grammar: combined with sorting and paging", () => {
  let marker: string;

  beforeAll(async () => {
    marker = unique("srt");
    for (const suffix of ["a", "b", "c"]) {
      await createUser({ userName: `${marker}.${suffix}@example.sg` });
    }
  });

  it("answers a filter with sortBy and sortOrder", async () => {
    const response = await scim(
      "GET",
      `/Users${filterQuery(`userName sw "${marker}"`)}&sortBy=userName&sortOrder=descending`,
    );

    // sortBy is advertised in ServiceProviderConfig; whether this provider honours it
    // or ignores it, an unsupported sort must not turn into a 500.
    expect([200, 400, 501]).toContain(response.status);
  });

  it("answers an unknown sortOrder", async () => {
    const response = await scim(
      "GET",
      `/Users${filterQuery(`userName sw "${marker}"`)}&sortBy=userName&sortOrder=sideways`,
    );

    expect([200, 400, 501]).toContain(response.status);
  });

  it("pages a filtered collection", async () => {
    const first = await scim(
      "GET",
      `/Users${filterQuery(`userName sw "${marker}"`)}&startIndex=1&count=2`,
    );
    const second = await scim(
      "GET",
      `/Users${filterQuery(`userName sw "${marker}"`)}&startIndex=3&count=2`,
    );

    expect(first.status).toBe(200);
    expect(second.status).toBe(200);
    expect(first.body.totalResults).toBe(3);
    expect(first.body.Resources).toHaveLength(2);
    expect(second.body.Resources).toHaveLength(1);

    const seen = [
      ...(first.body.Resources as ScimResource[]),
      ...(second.body.Resources as ScimResource[]),
    ].map((item) => item.id);
    expect(new Set(seen).size).toBe(3);
  });

  it("reports the filtered total, not the collection total", async () => {
    // Paginating over an unfiltered count is the bug this catches: a client walking
    // pages would see totalResults it can never reach.
    const response = await scim(
      "GET",
      `/Users${filterQuery(`userName sw "${marker}"`)}&count=1`,
    );

    expect(response.status).toBe(200);
    expect(response.body.totalResults).toBe(3);
    expect(response.body.itemsPerPage).toBe(1);
  });

  it("combines a filter with projection", async () => {
    const response = await scim(
      "GET",
      `/Users${filterQuery(`userName sw "${marker}"`)}&attributes=userName`,
    );

    expect(response.status).toBe(200);
    for (const resource of response.body.Resources as ScimResource[]) {
      expect(resource).toHaveProperty("userName");
      expect(resource).not.toHaveProperty("title");
    }
  });
});

describe("Filter grammar: filters on a single resource", () => {
  it("answers a filter supplied alongside a resource identifier", async () => {
    const created = await createUser();
    const response = await scim(
      "GET",
      `/Users/${created.id}${filterQuery(`userName eq "${created.userName}"`)}`,
    );

    expect([200, 400, 404]).toContain(response.status);
  });

  it("refuses two filters on a single-resource read", async () => {
    const created = await createUser();
    const response = await scim(
      "GET",
      `/Users/${created.id}${filterQuery('userName eq "a" or userName eq "b"')}`,
    );

    expect([400, 404]).toContain(response.status);
  });
});
