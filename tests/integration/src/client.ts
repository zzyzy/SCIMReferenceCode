import { createHmac } from "node:crypto";
import {
  BASE_URL,
  DEV_AUDIENCE,
  DEV_ISSUER,
  DEV_SIGNING_KEY,
  EDUPASS_BASE_URL,
  EDUPASS_UINFIN_BASE_URL,
  EXTERNAL_AUTH,
  FAULTY_BASE_URL,
  UNIMPLEMENTED_BASE_URL,
} from "./host.js";

export const SCHEMA_USER = "urn:ietf:params:scim:schemas:core:2.0:User";
export const SCHEMA_GROUP = "urn:ietf:params:scim:schemas:core:2.0:Group";
export const SCHEMA_ENTERPRISE = "urn:ietf:params:scim:schemas:extension:enterprise:2.0:User";
export const SCHEMA_PATCH = "urn:ietf:params:scim:api:messages:2.0:PatchOp";
export const SCHEMA_LIST = "urn:ietf:params:scim:api:messages:2.0:ListResponse";
export const SCHEMA_ERROR = "urn:ietf:params:scim:api:messages:2.0:Error";
export const SCHEMA_BULK_REQUEST = "urn:ietf:params:scim:api:messages:2.0:BulkRequest";

export const SCIM_CONTENT_TYPE = "application/scim+json";

function base64Url(input: Buffer | string): string {
  return Buffer.from(input)
    .toString("base64")
    .replace(/=+$/u, "")
    .replace(/\+/gu, "-")
    .replace(/\//gu, "_");
}

/**
 * Mints the same HS256 token the sample host accepts in Development.
 *
 * The samples no longer ship a token endpoint, so the tests mint their own from
 * the committed development key - which is exactly the point the README makes
 * about that key: anyone holding it can mint one.
 */
export function devToken(overrides: Record<string, unknown> = {}): string {
  const now = Math.floor(Date.now() / 1000);
  const header = base64Url(JSON.stringify({ alg: "HS256", typ: "JWT" }));
  const payload = base64Url(
    JSON.stringify({
      iss: DEV_ISSUER,
      aud: DEV_AUDIENCE,
      nbf: now,
      exp: now + 7200,
      ...overrides,
    }),
  );
  const signed = `${header}.${payload}`;
  const signature = base64Url(createHmac("sha256", DEV_SIGNING_KEY).update(signed).digest());
  return `${signed}.${signature}`;
}

export interface ScimResponse<T = unknown> {
  readonly status: number;
  readonly body: T;
  readonly text: string;
  readonly headers: Headers;
}

export interface ScimRequestOptions {
  /** Sent verbatim, bypassing JSON serialization. For malformed-input tests. */
  readonly raw?: string;
  readonly contentType?: string | null;
  readonly headers?: Record<string, string>;
  /** Omit the Authorization header entirely. */
  readonly anonymous?: boolean;
  /** Which host to send to. Defaults to the core sample. */
  readonly base?: string;
}

export async function scim<T = any>(
  method: string,
  path: string,
  body?: unknown,
  options: ScimRequestOptions = {},
): Promise<ScimResponse<T>> {
  const headers = new Headers(options.headers ?? {});

  if (!options.anonymous && !headers.has("Authorization")) {
    if (EXTERNAL_AUTH) {
      // An external relying party has its own credential; the dev bearer token means
      // nothing to it. Only set when the test has not named this header itself, so a
      // case that presents a deliberately bad credential still presents it.
      if (!headers.has(EXTERNAL_AUTH.header)) {
        headers.set(EXTERNAL_AUTH.header, EXTERNAL_AUTH.value);
      }
    } else {
      headers.set("Authorization", `Bearer ${devToken()}`);
    }
  }

  const hasBody = options.raw !== undefined || body !== undefined;
  if (hasBody && options.contentType !== null && !headers.has("Content-Type")) {
    headers.set("Content-Type", options.contentType ?? SCIM_CONTENT_TYPE);
  }

  const response = await fetch(`${options.base ?? BASE_URL}${path}`, {
    method,
    headers,
    body: options.raw !== undefined ? options.raw : hasBody ? JSON.stringify(body) : undefined,
  });

  const text = await response.text();
  let parsed: unknown = undefined;
  if (text.trim().length > 0) {
    try {
      parsed = JSON.parse(text);
    } catch {
      parsed = text;
    }
  }

  return { status: response.status, body: parsed as T, text, headers: response.headers };
}

/**
 * Sends to the Edupass host instead of the core one.
 *
 * A separate process, not a separate route: Edupass binds `/Users` to its own
 * resource type, and two providers cannot serve one route.
 *
 * An explicit `base` still wins, so a caller can send the same request to the
 * strict-JWT Edupass host instead.
 */
export async function edupass<T = any>(
  method: string,
  path: string,
  body?: unknown,
  options: ScimRequestOptions = {},
): Promise<ScimResponse<T>> {
  return scim<T>(method, path, body, { ...options, base: options.base ?? EDUPASS_BASE_URL });
}

/** Sends to the host whose provider implements nothing. */
export async function unimplemented<T = any>(
  method: string,
  path: string,
  body?: unknown,
  options: ScimRequestOptions = {},
): Promise<ScimResponse<T>> {
  return scim<T>(method, path, body, { ...options, base: UNIMPLEMENTED_BASE_URL });
}

/** Sends to the Edupass host that stores UIN/FIN and therefore requires it. */
export async function edupassUinFin<T = any>(
  method: string,
  path: string,
  body?: unknown,
  options: ScimRequestOptions = {},
): Promise<ScimResponse<T>> {
  return scim<T>(method, path, body, { ...options, base: EDUPASS_UINFIN_BASE_URL });
}

/** Sends to the host whose provider throws from everything. */
export async function faulty<T = any>(
  method: string,
  path: string,
  body?: unknown,
  options: ScimRequestOptions = {},
): Promise<ScimResponse<T>> {
  return scim<T>(method, path, body, { ...options, base: FAULTY_BASE_URL });
}

/** A value unique to this run, so suites can share a host without colliding. */
export function unique(prefix: string): string {
  return `${prefix}.${Math.random().toString(36).slice(2, 8)}${Date.now().toString(36).slice(-4)}`;
}

export interface ScimResource {
  id: string;
  schemas?: string[];
  meta?: { location?: string; created?: string; lastModified?: string; resourceType?: string };
  [key: string]: unknown;
}

export function patchOp(...operations: Record<string, unknown>[]): Record<string, unknown> {
  return { schemas: [SCHEMA_PATCH], Operations: operations };
}

export function userBody(overrides: Record<string, unknown> = {}): Record<string, unknown> {
  const userName = `${unique("user")}@example.sg`;
  return {
    schemas: [SCHEMA_USER],
    userName,
    active: true,
    title: "Teacher",
    name: { givenName: "Given", familyName: "Family" },
    emails: [{ value: userName, type: "work", primary: true }],
    ...overrides,
  };
}

export function groupBody(overrides: Record<string, unknown> = {}): Record<string, unknown> {
  return { schemas: [SCHEMA_GROUP], displayName: unique("group"), ...overrides };
}

/** Creates a user and fails the test if the service refused it. */
export async function createUser(
  overrides: Record<string, unknown> = {},
): Promise<ScimResource> {
  const response = await scim<ScimResource>("POST", "/Users", userBody(overrides));
  if (response.status !== 201) {
    throw new Error(`could not create a user: ${response.status} ${response.text}`);
  }
  return response.body;
}

export async function createGroup(
  overrides: Record<string, unknown> = {},
): Promise<ScimResource> {
  const response = await scim<ScimResource>("POST", "/Groups", groupBody(overrides));
  if (response.status !== 201) {
    throw new Error(`could not create a group: ${response.status} ${response.text}`);
  }
  return response.body;
}

export async function readUser(id: string): Promise<ScimResource> {
  return (await scim<ScimResource>("GET", `/Users/${id}`)).body;
}

export async function readGroup(id: string): Promise<ScimResource> {
  return (await scim<ScimResource>("GET", `/Groups/${id}`)).body;
}

export async function memberIds(groupId: string): Promise<string[]> {
  const group = await readGroup(groupId);
  const members = (group.members as { value: string }[] | undefined) ?? [];
  return members.map((member) => member.value).sort();
}

export function filterQuery(expression: string): string {
  return `?filter=${encodeURIComponent(expression)}`;
}

/** The 200-or-204 pair RFC 7644 3.5.2 leaves to the service. */
export const PATCH_APPLIED = [200, 204];
