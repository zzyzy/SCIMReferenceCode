# scim-sanity probe run

**Status:** result of record for the `net48` leg against
[`scim-sanity`](https://github.com/thomaselliottbetz/scim-sanity) (`scim-sanity probe`).
**Run date:** 2026-08-27.
**Subject:** `Microsoft.SCIM.WebHostSample.Net48` over the default `InMemoryProvider`.
**Authority:** RFC 7644 (protocol), RFC 7643 (core schema).

Oracle 8 in [`scim-conformance.md`](scim-conformance.md) §7, and the third written by a third
party. It walks a seven-phase CRUD lifecycle over real HTTP rather than reading the RFC as a
checklist, and it is the only oracle here that asserts the **status code of a PATCH** rather
than the resource as read back afterwards — which is exactly the gap it found. Every PATCH
suite in `tests/integration` accepts `PATCH_APPLIED = [200, 204]`, deliberately, because what
those suites are about is whether the operation was applied. Nothing asked which of the two
the service actually sends, so nothing noticed that Users and Groups had been sending
different ones since the port.

---

## 1. Result

| | Before | After |
|---|---|---|
| Passed | 25 | **28** |
| Failed | 3 | **0** |
| Skipped | 3 | 3 |

One defect fixed, one asymmetry made deliberate.

| # | Root cause | Errors |
|---|---|---|
| D1 | `PATCH /Groups/{id}` answered 204 while `PATCH /Users/{id}` answered 200 | 3 |
| D2 | A PATCH naming `attributes` was answered 204, which cannot carry a projection | 0 — found by hand, see §4 |

The three skips are phases 4, 5 and 5a — `Agent` and `AgenticApplication` from
`draft-abbey-scim-agent-extension-00`. The probe skips them because the service does not
advertise those resource types, which is correct: they are a draft extension this
implementation does not claim.

---

## 2. Method

```bash
uvx scim-sanity probe http://localhost:5000/scim \
  --token <key> \
  --i-accept-side-effects
```

`uvx scim-sanity` works directly — the package's console script matches its name, unlike
`scim2-cli` (see [`scim2-cli-conformance.md`](scim2-cli-conformance.md) §2). Add
`--json-output` for a machine-readable result; that form is what §1's counts were read from.

`--i-accept-side-effects` is mandatory and not a formality: the probe creates, mutates and
deletes real users and groups on the target. Point it at a sample host, never at anything
holding real identities.

The host was run in API-key mode, which needs no token minting — see
[`entra-scim-validator.md`](entra-scim-validator.md) §2.1:

```bash
SCIM_API_KEY=<key> \
  Microsoft.SCIM.WebHostSample.Net48/bin/Debug/net48/Microsoft.SCIM.WebHostSample.Net48.exe \
  http://localhost:5000
```

### 2.1 This oracle is stricter than the RFC in one place

Worth knowing before reading its output as a defect list. RFC 7644 §3.5.2 says a successful
PATCH is answered *either* with 200 and the resource *or* with 204 — the service chooses.
`scim-sanity` reports 204 as a failure regardless, and its `--compat` mode does **not**
downgrade it. Its stated rationale is a client-side one: a client that relies on the PATCH
response to update local state sees stale data and must issue a redundant GET.

So D1 below was not a conformance defect. It was fixed because the *asymmetry* was a defect —
see §3 — and the fix happens to satisfy the probe as well. D2, found while investigating D1,
is the genuine RFC violation, and this oracle did not catch it.

---

## 3. D1 — Users answered 200, Groups answered 204

Both are legal. One service doing both, for no stated reason, is not: a client that has
learned to read the response of a user PATCH gets an empty body from a group PATCH, and
nothing in the schema or the discovery documents says it should expect that.

The split was inherited rather than chosen. Upstream decided the status by string-comparing
the adapter's `SchemaIdentifier` against the enterprise User URI; commit `56340bc` replaced
that with `IProviderAdapter.ReturnsResourceOnPatch` and preserved the behaviour exactly,
which meant preserving the accident. `Core2EnterpriseUserProviderAdapter` overrode it to
`true`; `Core2GroupProviderAdapter` never overrode it, so groups took the `false` default.

### 3.1 Why it could not simply be flipped

`Core2GroupProviderAdapter` is the only group adapter in the repository, and the Edupass leg
uses it too. The EduPass/FIMS interface specification requires 204 for Update Group
Membership — *"Status 204: PATCH applied"* — and
[`edupass-spec-validation-2026-08-26.md`](edupass-spec-validation-2026-08-26.md) §6 records
that 204 as conformance. Flipping the default globally would have satisfied this oracle by
breaking a downstream contract, and would have made that document wrong.

### 3.2 What was done instead

The choice became a host-level setting, because that is what it is — a deployment's contract
with its clients, not a property of the SCIM library:

```csharp
// Startup.cs (net48) / Program.cs (net10.0)
ScimServiceOptions.GroupPatchReturnsResource = !eduPass;
```

`Core2GroupProviderAdapter.ReturnsResourceOnPatch` reads it. The default is `false`, so a
consumer that upgrades and sets nothing keeps exactly the behaviour it had. Both sample hosts
set it to `true` except under `SCIM_PROVIDER=edupass`, which keeps 204.

The point is not that 200 is better than 204. It is that after this change each leg's answer
is written down at the point where the deployment is configured, instead of being whatever it
inherited from a base class it never mentioned.

---

## 4. D2 — a PATCH naming `attributes` was answered 204

**Authority:** RFC 7644 §3.5.2 — *"The server MUST return a 200 OK if the `attributes`
parameter is specified in the request."*

This is the one unambiguous violation, and the probe never sends the request that exposes it.
It surfaced from reading §3.5.2 while deciding what to do about D1:

```
PATCH /scim/Groups/{id}?attributes=displayName   ->   204, empty body
```

A 204 cannot carry a projection, so the parameter was silently pointless. `PatchAsync` now
returns 200 when the request names `attributes` or `excludedAttributes`, whichever form the
deployment chose otherwise — including on the Edupass leg, where the RFC overrides the
interface spec's 204 for this one request shape. The projection itself needed no new code:
the 200 path already delegates to `RetrieveAsync`, which reads both parameters off the
request URI and applies `ScimProjection`.

`excludedAttributes` is not named by that sentence. It is included because a 204 is equally
useless as an answer to it, and because splitting the two would mean a client could ask for a
projection two ways and get a body only one of them.

**One trap worth naming**, and the reason the key is read off the raw query string rather
than through `UriBuilder`: `UriBuilder.Query` prepends a `?` of its own, so a query that
already carried one becomes `??attributes=…`, whose first key is `?attributes` — matching
nothing. `scim2-cli-conformance.md` §3 records the same trap costing a silently unprojected
search.

---

## 5. What still fails

Nothing. 28 of 28 executed checks pass; 3 are skipped as a draft extension the service does
not claim (§1).

---

## 6. Regression cover

`tests/integration/suites/patch-response-code.spec.ts`, on both legs. It pins all three
statuses independently, because the knob now makes them independently changeable:

- the sample host answers 200 on a group PATCH and on a user PATCH;
- the Edupass host answers 204 on a group PATCH;
- both answer 200 when the request names `attributes` or `excludedAttributes`, including
  when the parameter follows another one.

This is the file the rest of the PATCH suites could not be: they assert the resource as read
back and accept either status, which is right for what they test and is why none of this was
covered before.

---

## 7. Re-running

Build, start the host in API-key mode, then run the command in §2. Results are per-run: the
probe generates fresh identifiers each time, and the in-memory store does not survive a
restart, so counts compare across runs but individual values do not.

To check the Edupass side of §6 by hand, start the host with `SCIM_PROVIDER=edupass` and
confirm a plain group PATCH still answers 204 while one carrying `?attributes=id` answers 200.
