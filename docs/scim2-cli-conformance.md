# scim2-cli compliance run

**Status:** result of record for the `net48` leg against
[`scim2-cli`](https://scim2-cli.readthedocs.io/en/latest/tutorial.html#perform-a-scim-compliance-test)
(`scim test`), which drives the [`scim2-tester`](https://scim2-tester.readthedocs.io) suite.
**Run date:** 2026-08-26.
**Subject:** `Microsoft.SCIM.WebHostSample.Net48` over the default `InMemoryProvider`, and —
see §6 — over the SQLite-backed `DatabaseProvider`.
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
| Checks passed | 66 | **112** |
| Checks failed | 41 | **0** |

Eleven defects fixed and one absent feature built.

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
| D10 | `/.search` was not implemented — POST querying, RFC 7644 §3.4.3 | 2 |
| D11 | `active` could not be absent, so it could not be removed | 1 |
| D12 | An empty membership was reported as `[]`, so a removal could not be seen | 1 |

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

### D8, D11 — `remove` of `active`

Skipped outright, so a deactivation request reported success and left the user active.

Honouring it needed the model to change as well. `Core2UserBase.Active` was a plain `bool`, in
which false and unset are the same value, so there was nothing a remove could write. RFC 7643
§4.1.1 makes `active` optional, so it is now `bool?` — absent when never set, absent after a
remove, and `false` when a client actually says false. The sample's `UserEntity.IsActive`
follows, since a store that cannot hold "absent" cannot round-trip it either.

### D12 — an empty membership was reported as `[]`

So a client could not see that `remove members` had removed anything. The mapper emitted the
empty array deliberately, arguing that a known-empty membership and an unreported one are
different things. RFC 7643 §2.5 settles it the other way: *"unassigned attributes, the null
value, or empty array (in the case of a multi-valued attribute) SHALL be considered to be
equivalent in 'state'"*. There is no difference to report, and omitting it is the rule the
user's own multi-valued attributes already followed.

### D10 — `/.search`

**Authority:** RFC 7644 §3.4.3, and §3.2's endpoint table, which names the base endpoint's
POST query *"search from system"*.

Not a defect but an absent feature: the endpoint answered 405. It now exists on `/Users`,
`/Groups` and the service root, on both legs.

The body is rendered back into the query string that would have carried the same query on a
GET, and answered by the code that already serves GET. RFC 7644 §3.4.3 says a POST query is
answered *"as specified in Section 3.4.2"* — it is the same query arriving differently — so a
second parser would only be a second place for filter handling, attribute notation and paging
to disagree with themselves. One test asserts the two produce identical bodies.

`.search` is its own route rather than a branch inside `POST`, because the two take different
bodies and letting the binder choose by shape would make a malformed creation read as a search
of everything. A body not naming the SearchRequest URN is refused: §3.4.3 says query requests
*MUST* be identified by it.

The root search queries every resource type and concatenates, then pages once over the whole —
paging each type separately returns the first page of each rather than the first page of the
result set. Two details:

- A type that cannot answer the filter contributes nothing rather than failing the search.
  Groups have no `userName`, and there is no reading of "search everything" under which that
  is an error rather than an empty match.
- The type to query is read out of `/ResourceTypes`, trying the type's own schema and then the
  extensions it declares. This provider dispatches `/Users` on `Core2EnterpriseUser`, whose
  identifier is the *enterprise extension's* — not the core User schema the resource type
  advertises — so assuming either one convention finds only half the service.

**One trap worth naming:** `UriBuilder.Query` prepends a `?` of its own. A rendered query
string that already carried one became `??attributes=…`, whose first key is `?attributes` —
matching nothing, so the parameter was silently ignored while the filter still worked. The
symptom was a search that returned the right resources unprojected.

### D9 — an unknown URL under the service root

`GET /scim/<anything>` answered 501. RFC 7644 §3.12 gives 404 for *"specified resource or
endpoint does not exist"*; 501 said instead that the root retrieves resources by identifier
but has not implemented it yet, which a client cannot tell from a URL it should stop asking
for.

---

## 4. What still fails

Nothing. The suite passes 112 of 112.

Two of these were carried for a round as "not defects" before being fixed: `active` was read as
a model limitation not worth a breaking change, and the empty `members` array as a deliberate
design decision. Both readings were wrong in the same way — each treated a limitation of this
implementation as though it were a property of SCIM, and neither had been checked against what
the RFC actually says. §2.5 and §4.1.1 answer them outright.

---

## 5. Re-running

Build, start the host in API-key mode, then run the command in §2. Results are per-run: the
tester generates fresh identifiers each time, so counts compare across runs but individual
values do not. Whether anything survives a restart depends on the provider — see §6.

The regression suites cover every fix above on both legs —
`discovery-by-id.spec.ts`, `patch-whole-attribute.spec.ts` and `search.spec.ts`.

---

## 6. The same suite against the SQLite provider

**Run date:** 2026-08-26. **Subject:** the same `net48` host started with
`SCIM_PROVIDER=database`, so the two resource types are served out of SQLite through Dapper
rather than out of a dictionary.

| Provider | Passed | Failed |
|---|---|---|
| `InMemoryProvider` (the §1 baseline, re-run) | 112 | 0 |
| `DatabaseProvider` (SQLite) | **112** | **0** |

No defects, and nothing to fix. The point of running it was that the two providers are meant
to differ only in where the rows live: the domain entities, the mappers and every rule about
uniqueness, replacement and patching are shared, and the oracle that walks the RFC agreeing
across both is what makes that claim checkable rather than merely asserted.

The host log carries no exception for either run. That matters more than the count, because a
store can answer a check correctly while failing underneath it — the tester reads status codes
and bodies, not logs.

### 6.1 What a persistent store makes newly testable

The in-memory provider forgets everything on restart, so two things could not previously be
asked of this oracle:

- **A second run against a store the first one dirtied.** Re-running without deleting the
  database also passes 112 of 112. Worth checking rather than assuming: this service refuses a
  duplicate `userName` with 409 and a duplicate `displayName` likewise, so a tester that reused
  a fixed name would have passed on an empty store and failed on a populated one. It generates
  fresh identifiers, and does not.
- **That the run leaves nothing behind.** After two full runs every table is empty — including
  the five child tables of a user and the group membership join table, none of which the tester
  ever addresses directly. They are emptied by `ON DELETE CASCADE` when it deletes the resource
  that owns them, which is the only evidence here that the cascade is wired correctly, and the
  kind of thing an in-memory dictionary cannot get wrong in the first place.

### 6.2 Re-running this leg

```bash
SCIM_PROVIDER=database SCIM_API_KEY=<key> \
  Microsoft.SCIM.WebHostSample.Net48.exe http://localhost:5000
```

then the command in §2. `SCIM_DATABASE` sets where the file goes; unset, it is `scim.db` beside
the executable. Delete it to start from empty — but as above, a run does not require it.
