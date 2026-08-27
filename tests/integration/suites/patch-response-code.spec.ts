import { describe, expect, it } from "vitest";
import {
  createGroup,
  createUser,
  edupass,
  patchOp,
  scim,
  unique,
  type ScimResource,
} from "../src/client.js";

/**
 * What a successful PATCH answers, RFC 7644 3.5.2.
 *
 * That section leaves 200-with-resource and 204 to the service, so the status is a
 * deployment's contract with its clients rather than a conformance question: the sample
 * host answers 200 (ScimServiceOptions.GroupPatchReturnsResource), the Edupass leg
 * answers 204 because its interface specification says "Status 204: PATCH applied".
 * Both are pinned here because the knob makes them independently changeable.
 *
 * The one part the section does not leave open: a request naming the attributes it wants
 * back MUST be answered with 200, whichever form the deployment otherwise chose. A
 * projection is the one thing 204 cannot carry. Groups answered 204 to such a request
 * until scim-sanity's probe asked for one.
 *
 * The rest of the PATCH suites assert the resource as read back and accept either status
 * (PATCH_APPLIED), which is right for them and is why none of this was covered.
 */

// A fresh value each time: displayName is server-unique on a group, so a shared
// constant collides with 409 the second time it is used.
function rename(): Record<string, unknown> {
  return { op: "replace", path: "displayName", value: unique("patched") };
}

describe("PATCH answers the status the deployment chose", () => {
  it("returns 200 and the updated group", async () => {
    const group = await createGroup();

    const patched = await scim<ScimResource>(
      "PATCH",
      `/Groups/${group.id}`,
      patchOp(rename()),
    );

    expect(patched.status).toBe(200);
    expect(patched.body.id).toBe(group.id);
  });

  it("returns 200 and the updated user, as it always has", async () => {
    const user = await createUser();

    const patched = await scim<ScimResource>("PATCH", `/Users/${user.id}`, patchOp(rename()));

    expect(patched.status).toBe(200);
    expect(patched.body.id).toBe(user.id);
  });
});

describe("PATCH naming the attributes it wants back is answered with 200", () => {
  // RFC 7644 3.5.2, on top of 3.9's projection rules.
  it.each(["Users", "Groups"])("projects a %s PATCH response", async (endpoint) => {
    const resource = endpoint === "Users" ? await createUser() : await createGroup();

    const patched = await scim<ScimResource>(
      `PATCH`,
      `/${endpoint}/${resource.id}?attributes=id`,
      patchOp(rename()),
    );

    expect(patched.status).toBe(200);
    expect(patched.body.id).toBe(resource.id);
    expect(patched.body.displayName).toBeUndefined();
  });

  it("honours excludedAttributes the same way", async () => {
    const group = await createGroup();

    const patched = await scim<ScimResource>(
      "PATCH",
      `/Groups/${group.id}?excludedAttributes=members`,
      patchOp(rename()),
    );

    expect(patched.status).toBe(200);
    expect(patched.body.members).toBeUndefined();
  });

  it("is not confused by the parameter appearing after a filter", async () => {
    // UriBuilder.Query prepends a '?' of its own, so a rendered query string that already
    // carried one became '??attributes=' - whose first key is '?attributes', matching
    // nothing. The key is read off the raw query here for the same reason.
    const group = await createGroup();

    const patched = await scim<ScimResource>(
      "PATCH",
      `/Groups/${group.id}?excludedAttributes=members&attributes=id`,
      patchOp(rename()),
    );

    expect(patched.status).toBe(200);
  });
});

describe("The Edupass leg answers 204, which its specification requires", () => {
  it("answers a group membership PATCH with 204 and no body", async () => {
    const created = await edupass<ScimResource>("POST", "/Groups", {
      schemas: ["urn:ietf:params:scim:schemas:core:2.0:Group"],
      displayName: unique("Location_App_Role"),
    });
    expect(created.status).toBe(201);

    const patched = await edupass(
      "PATCH",
      `/Groups/${created.body.id}`,
      patchOp({ op: "add", path: "members", value: [] }),
    );

    expect(patched.status).toBe(204);
  });

  it("still answers 200 when the request names the attributes it wants back", async () => {
    const created = await edupass<ScimResource>("POST", "/Groups", {
      schemas: ["urn:ietf:params:scim:schemas:core:2.0:Group"],
      displayName: unique("Location_App_Role"),
    });

    const patched = await edupass<ScimResource>(
      "PATCH",
      `/Groups/${created.body.id}?attributes=id`,
      patchOp({ op: "add", path: "members", value: [] }),
    );

    expect(patched.status).toBe(200);
    expect(patched.body.id).toBe(created.body.id);
  });
});
