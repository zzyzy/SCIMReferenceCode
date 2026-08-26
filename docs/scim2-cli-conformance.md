# scim2-cli compliance run

**Status:** result of record for the `net48` leg against
[`scim2-cli`](https://scim2-cli.readthedocs.io/en/latest/tutorial.html#perform-a-scim-compliance-test)
(`scim test`), which drives the [`scim2-tester`](https://scim2-tester.readthedocs.io) suite.
**Run date:** 2026-08-26.
**Subject:** `Microsoft.SCIM.WebHostSample.Net48` over the default `InMemoryProvider`.
**Authority:** RFC 7644 (protocol), RFC 7643 (core schema).

Oracle 7 in [`scim-conformance.md`](scim-conformance.md) §7, and the second third-party one
after the Entra validator. The two disagree usefully: Entra sends what one large client
happens to send, while scim2-tester walks the RFC. Where Entra found four defects in how
PATCH is *parsed*, this found defects in what the service *offers* — endpoints that were never
routed, and a schema that did not describe its own responses.

---

## 1. Result

| | Before | After |
|---|---|---|
| Checks passed | 66 | **106** |
| Checks failed | 41 | **4** |

Nine defects fixed. The four that remain are covered in §4; none is a parsing or protocol
fault.

| # | Root cause | Errors |
|---|---|---|
| D1 | `/Schemas/{id}` and `/ResourceTypes/{id}` were never routed | 11 |
| D2 | A whole multi-valued attribute rebuilt each entry from `value`, dropping the rest | 6 |
| D3 | `ims`, `phoneNumbers` and `addresses` had no whole-collection branch at all | 5 |
| D4 | `?attributes=` never pruned sub-attributes of a multi-valued attribute | 6 |
| D5 | A `path` naming a schema URN was 400 invalidPath | 5 |
| D6 | `name` could not be replaced or removed as a whole | 3 |
| D7 | The Group schema did not declare `$ref` or `display` on `members` | 3 |
| D8 | `remove` of `active` was ignored | 1 |
| D9 | An unknown URL under the service root was 501 | 1 |

---

## 2. Method

```bash
uvx --from scim2-cli scim \
  --url http://localhost:5000/scim \
  --header "Authorization: Bearer <key>" \
  test
```

`uvx scim2-cli` does not work: the package's executables are `scim` and `scim2`, so
`--from` is required. The host was run in API-key mode (`SCIM_API_KEY=<key>`), which needs no
token minting — see [`entra-scim-validator.md`](entra-scim-validator.md) §2.1.

**Redirecting the output to a file hangs the run.** `scim test ... > out.txt` never returns;
`scim test ... | tee out.txt` is fine. Pipe, don't redirect.

Every check was reduced to a single request against `localhost` before anything was changed,
because the tester's messages name a symptom rather than a cause — five distinct defects all
reported as `Server response payload validation error`.

### 2.1 The tester builds its model from your own schema

The most useful thing to know about this oracle. `scim2-cli` downloads `/Schemas` and
validates every response against **what the service says about itself**. So D7 surfaced as

```
members.0.ref
  Extra inputs are not permitted [type=extra_forbidden, input_value='http://localhost:5000/…']
```

— not "your `$ref` is wrong" but "you returned an attribute your schema does not declare".
The response was correct and the schema was incomplete, which no amount of staring at the
response would have shown. A payload that validates when checked against `scim2-models`
directly can still fail here, and that difference is the finding.

---

## 3. Defects fixed

### D1 — `/Schemas/{id}` and `/ResourceTypes/{id}` were never routed

**Authority:** RFC 7644 §4 — *"Individual schema definitions can be returned by appending the
schema URI to the /Schemas endpoint"*, and *"in cases where a request is for a specific
ResourceType or Schema, the single JSON object is returned in the same way that a single User
or Group is retrieved"*.

Both legs exposed only the collections. `ScimDiscoveryRequestHandler` gained `RetrieveSchema`
and `RetrieveResourceType`, returning the bare resource and 404 when the identifier names
none.

The routes are **catch-all** (`{*identifier}`). A schema URI is
`urn:ietf:params:scim:schemas:core:2.0:User`; an ordinary `{identifier}` segment stops at the
first dot, so the URI arrived truncated and matched nothing.

### D2, D3 — a whole multi-valued attribute lost everything but `value`

`{"op":"replace","path":"roles","value":[{"value":…,"display":…,"type":…,"primary":true}]}`
came back as `value` alone. Two causes: the entry builders read only `Value`, and
`OperationValue` — the type that carries one entry — had no `primary` at all, so an absent
and a false `primary` were the same thing.

`OperationValue` gained `primary` (nullable, so absent stays absent per RFC 7643 §2.4) and the
six address sub-attributes, since an address has no `value` of its own to be carried by.

Only emails had a whole-collection branch; `ims`, `phoneNumbers` and `addresses` fell through
to the per-entry patcher, which declines an operation with no value path. The three now share
one generic `PatchValueCollection<T>` over `TypedValue`, written once so they cannot drift
again — which is exactly how they had drifted.

### D4 — `?attributes=` did not reach into a multi-valued attribute

**Authority:** RFC 7644 §3.9.

`ScimProjection.RetainRequested` pruned sub-attributes of a `JObject` but never of a
`JArray`, so `attributes=members.value` returned each membership whole. The removal path
already handled both shapes; only the retain path did not.

A second fault in the same method: sub-attributes were pruned once per requested path in turn,
so `attributes=emails.value&attributes=emails.type` kept only whichever came last. They are
now gathered per attribute first, then applied once.

### D5 — a `path` naming a schema URN

`Path.TryParse` splits at the **last** colon, so a bare `urn:…:enterprise:2.0:User` parses as
schema `urn:…:enterprise:2.0` plus attribute `User` — which names nothing, hence 400
invalidPath. Such a path cannot be routed by parsing alone; it has to be recognised against
the resource's own `schemas`, which is what `Expand` now does. `add` and `replace` expand the
value into one operation per attribute of that schema; `remove` clears the extension.

Two details this turned up:

- A client that serializes an extension whole sends `schemas` inside it. That belongs to the
  resource (RFC 7643 §3), not to the extension, and expanding it named an attribute no schema
  defines. It is skipped.
- The schema separator applies only at the first level. `urn:…:User` + `manager` is a path,
  but its sub-attribute is `manager.value`, not `manager:value`.

### D6 — `name` as a whole

`PatchName` requires a value path saying which part of the name to write, so
`{"op":"replace","path":"name","value":{…}}` did nothing and `{"op":"remove","path":"name"}`
did nothing. A path naming a complex, single-valued attribute now expands into its
sub-attributes, and a remove clears it.

The expansion is gated on a named list rather than on "the value is an object", because
`members` also takes an object — and there the object is one entry of a collection, not a set
of sub-attributes.

### D7 — the schema did not describe the responses

`members` was declared with `value` and `type` only, while every membership is returned with
`$ref` and may carry `display`. RFC 7643 §4.2 defines all four. See §2.1 for why this matters
more than it looks.

### D8 — `remove` of `active`

Skipped outright, so a deactivation request reported success and left the user active.
`Active` is a non-nullable `bool`, so cleared is `false` — which is also what RFC 7643 §4.1.1
makes an absent `active` mean.

### D9 — an unknown URL under the service root

`GET /scim/<anything>` answered 501. RFC 7644 §3.12 gives 404 for *"specified resource or
endpoint does not exist"*; 501 said instead that the root retrieves resources by identifier
but has not implemented it yet, which a client cannot tell from a URL it should stop asking
for.

---

## 4. What still fails, and why it is not fixed

| Check | Reason |
|---|---|
| `search_with_attributes` ×2 | `/.search` (RFC 7644 §3.4.3) is not implemented on either leg. An absent optional feature, not a defect — it answers 405. Building it means a new endpoint, `SearchRequest` binding and POST-body filter handling on both legs. |
| `check_remove_attribute` — *"Attribute 'members' was not removed"* | `remove members` **does** clear the membership; the response carries `"members": []`. The tester wants the attribute absent. `ScimGroupMapper` deliberately always emits `members`, on the argument that an empty membership and an unreported one are different things a client cannot otherwise distinguish. A documented design decision, left alone. |
| `check_remove_attribute` — *"Attribute 'active' was not removed"* | Cleared to `false` (D8). The tester wants it absent, which needs `Core2UserBase.Active` to become `bool?` — a breaking change to a public type, across every provider and mapper. Not worth it for one check. |

---

## 5. Re-running

Build, start the host in API-key mode, then run the command in §2. Results are per-run: the
tester generates fresh identifiers each time and the in-memory store does not survive a
restart, so counts compare across runs but individual values do not.

The regression suites cover every fix above on both legs —
`discovery-by-id.spec.ts` and `patch-whole-attribute.spec.ts`.
