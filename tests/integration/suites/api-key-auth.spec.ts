import { describe, expect, it } from "vitest";
import { scim } from "../src/client.js";
import { API_KEY, API_KEY_BASE_URL } from "../src/host.js";

/**
 * Authentication by API key, against a host that accepts nothing else.
 *
 * The sample serves one authentication mode at a time, so none of this can be observed
 * on the hosts the other suites use - those accept a bearer token, and a host that
 * accepts both could not tell an opaque key from a token on the same header anyway.
 *
 * What is under test is the library's, not the sample's: Anacle.ApiFramework's API key
 * handler reads a key from a configurable header, and - the part these cases exist for -
 * under a configurable RFC 7235 auth-scheme. Without the scheme the whole header value
 * is the key, so pointing the handler at Authorization yielded "Bearer <key>" as the
 * key and never matched. A sample-local middleware used to strip the prefix and
 * re-present the key in X-Api-Key; the handler now does it, and that shim is gone.
 *
 * The scheme cases are the interesting ones. A prefix that is absent, or present without
 * its separating space, must not authenticate - the first because an unqualified value
 * is not a credential under a scheme, the second because "Bearer<key>" would otherwise
 * read as the key "<key>".
 */
const request = (headers: Record<string, string>) =>
  scim("GET", "/Users", undefined, { base: API_KEY_BASE_URL, anonymous: true, headers });

describe("A host configured for API keys accepts the key its store holds", () => {
  it("accepts the key presented under the configured scheme", async () => {
    const response = await request({ Authorization: `Bearer ${API_KEY}` });

    expect(response.status).toBe(200);
  });

  it("accepts the scheme in any case, because RFC 7235 makes the token case-insensitive", async () => {
    const response = await request({ Authorization: `bEaReR ${API_KEY}` });

    expect(response.status).toBe(200);
  });

  it("accepts extra space between the scheme and the key", async () => {
    const response = await request({ Authorization: `Bearer    ${API_KEY}` });

    expect(response.status).toBe(200);
  });
});

describe("A host configured for API keys refuses everything else", () => {
  it("refuses a key that is not in the store", async () => {
    const response = await request({ Authorization: "Bearer not-the-key" });

    expect(response.status).toBe(401);
  });

  it("refuses a request presenting no credential at all", async () => {
    const response = await scim("GET", "/Users", undefined, {
      base: API_KEY_BASE_URL,
      anonymous: true,
    });

    expect(response.status).toBe(401);
  });

  it("refuses the right key presented without the scheme", async () => {
    // The header is Authorization, so a bare value is not a credential under any scheme.
    const response = await request({ Authorization: API_KEY });

    expect(response.status).toBe(401);
  });

  it("refuses the scheme run together with the key", async () => {
    // The separating space is part of the prefix. Without this, "Bearer<key>" would be
    // read as the key "<key>" presented correctly.
    const response = await request({ Authorization: `Bearer${API_KEY}` });

    expect(response.status).toBe(401);
  });

  it("refuses a scheme it was not configured for", async () => {
    const response = await request({ Authorization: `ApiKey ${API_KEY}` });

    expect(response.status).toBe(401);
  });

  it("does not accept the development bearer token the other hosts take", async () => {
    // The proof that this host is running the key mode rather than the token mode, and
    // that a token is not silently resolved as a key.
    const response = await scim("GET", "/Users", undefined, { base: API_KEY_BASE_URL });

    expect(response.status).toBe(401);
  });
});

describe("A refusal from the API key handler is answered the way any other 401 is", () => {
  it("names the scheme to present, rather than the header it reads", async () => {
    // RFC 7235 4.1: a challenge is an auth-scheme token. The handler used to send its
    // own header name - "WWW-Authenticate: X-Api-Key" - which names no scheme a client
    // can present.
    const response = await scim("GET", "/Users", undefined, {
      base: API_KEY_BASE_URL,
      anonymous: true,
    });

    expect(response.status).toBe(401);
    expect(response.headers.get("www-authenticate")).toBe("Bearer");
  });

  it("carries no body, or a SCIM error - never an ASP.NET one", async () => {
    // The same rule scim-compliance.spec.ts holds the token hosts to. Asserted here too
    // because the 401 comes from a different place on this host.
    const response = await scim("GET", "/Users", undefined, {
      base: API_KEY_BASE_URL,
      anonymous: true,
    });

    expect(response.status).toBe(401);

    if (response.text.trim().length > 0) {
      expect(response.body.schemas).toEqual(["urn:ietf:params:scim:api:messages:2.0:Error"]);
    }
  });
});
