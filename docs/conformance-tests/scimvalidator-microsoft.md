# Microsoft Entra SCIM Validator run

**Status:** result of record for the `net48` leg against
[scimvalidator.microsoft.com](https://scimvalidator.microsoft.com/).
**Run date:** 2026-08-25.
**Subject:** `Microsoft.SCIM.WebHostSample.Net48` (OWIN self-host) over the default
`InMemoryProvider`, exposed through an ngrok tunnel.
**Authority:** RFC 7644 (protocol), RFC 7643 (core schema).

This is oracle 6 alongside the five in [`scim-conformance.md`](../scim-conformance.md) §7. It is
the only one written by a third party, which is its whole value: it exercises the endpoint the
way Entra ID's provisioning service does, including payload shapes no test in this repository
had thought to send. It is also the only oracle that can be wrong about us — see §5.

---

## 1. Result

| | Before | After |
|---|---|---|
| Core tests | 19 / 24 | **22 / 24** |
| Preview tests | 1 / 7 | **7 / 7** |

Five defects found and fixed (§4). The two core tests still failing are a defect in the
validator, not in the endpoint (§5).

The result was then reproduced twice, once with every capability the Settings tab offers turned
on. `Supports Verbose PATCH (Preview)` is the one that changes what is sent:

| Run | Settings | Core | Preview | Failing |
|---|---|---|---|---|
| 3 | default (Deactivation, Delete ×2 on) | 22 / 24 | 7 / 7 | the two in §5 |
| 4 | default, repeat | 22 / 24 | 7 / 7 | the two in §5 |
| 5 | **all five on** — adds Verbose PATCH and Run Tests Sequentially | 22 / 24 | 7 / 7 | the two in §5 |
| 6 | all five on, after the `scim2-cli` work | 22 / 24 | 7 / 7 | the two in §5 |
| 7 | all five on, after the PATCH response-code work | 22 / 24 | 7 / 7 | the two in §5 |

Run 7 (2026-08-27) is likewise a regression check, for the PATCH response-code work in
[`scim-sanity-tests.md`](scim-sanity-tests.md). It is the run where the change is
most visible in the traffic and least visible in the result: the six group PATCHes this suite
sends answered 204 in run 6 and **200 with the resource** in run 7, and the score did not move.
That is the finding. This validator asserts the *body* of a group PATCH response only when
there is one, so a body appearing where none had been is new surface it had never inspected —
and it accepted it. Read together with `scim-sanity`, which fails a 204 outright, and
`scim2-cli`, which accepts either: the sample host now sits in all three oracles'
intersection.

Reading that traffic has a trap in it — the ngrok inspector retains captures across sessions,
so run 7's pane appeared to show the change misfiring. See §7.2.

Run 6 is a regression check rather than a new reading. The twelve defects
[`scim2-cli-tests.md`](scim2-cli-tests.md) records were fixed between runs 5 and
6, two of them changing what a response carries: `active` is now absent unless set, and an
empty `members` is omitted. Neither moved this suite — the validator always sends `active`
explicitly, and its group cases name membership by filter rather than by its emptiness.

Run 5 substitutes `Patch User - Replace Attributes **Verbose Request**` for the ordinary
`Patch User - Replace Attributes`; the test count stays 31. Verbose mode gives **every**
attribute its own explicit `path` — 53 single-attribute operations in one request, none of them
path-less — which is the exact inverse of the default shape that exposed D1:

```json
{"op":"replace","path":"name.formatted","value":"Yasmine"}
{"op":"replace","path":"name.givenName","value":"Hassan"}
…51 more
```

So the two modes bracket the problem: verbose exercises dotted and filtered paths one at a time,
default bundles everything into one path-less operation. Had the endpoint only ever been tested
in verbose mode, D1 would never have surfaced. Both shapes now pass apart from §5.

### 1.1 What was failing before

| Suite | Test | Root cause |
|---|---|---|
| Core | `POST /Users` — Create a new User | §5 (validator) |
| Core | `PATCH /Users/Id` — Patch User - Replace Attributes | D1, D2, D4 |
| Core | `PATCH /Users/Id` — Update User userName | D1 |
| Core | `PATCH /Groups/Id` — Patch Group - Replace Attributes | D3 |
| Core | `PATCH /Groups/Id` — Update Group displayName | D1 |
| Preview | `PATCH /Users/Id` — Multiple Operations on different attributes | D1 |
| Preview | `PATCH /Users/Id` — Multiple Operations on same attribute | D1 |
| Preview | `DELETE /Users/Id` — Delete a non-existent User | D5 |
| Preview | `DELETE /Users/Id` — repeat DELETE should be 404, got NoContent | D5 |
| Preview | `DELETE /Groups/Id` — Delete a non-existent Group | D5 |
| Preview | `DELETE /Groups/Id` — repeat DELETE should be 404, got NoContent | D5 |

One defect (D1) accounted for six of the eleven. D2 and D4 were reported in the first run too,
but only inside `Patch User - Replace Attributes`, where they sat among some twenty other
mismatches in one message and were initially — and wrongly — read as more of D1. They became
unambiguous only after D1 was fixed and that test's complaints narrowed to emails, phone numbers
and roles. A defect list is worth re-reading after every fix, not just at the start.

---

## 2. Methodology

### 2.1 Setup

The validator is a hosted service, so it must reach the endpoint over the public internet.

1. Build, then start the sample host with API-key authentication:
   `SCIM_API_KEY=<key>` selects the API-key mode, on either sample. Both wire the key handler
   to read `Authorization` under the `Bearer` scheme, because the validator offers no
   credential field other than a bearer token.
2. Expose it: `ngrok http 5000 --host-header=localhost`.
   `--host-header=localhost` is **required**. The OWIN host binds `http://localhost:5000`, and
   `HttpListener` answers a request whose `Host` header names the ngrok domain with 400 before
   the pipeline ever runs.
3. In the validator: *Discover Schema*, endpoint `https://<tunnel>/scim`, token `<key>`, then
   *Test Schema* from the attributes page.
4. Optionally, on the attributes page's **Settings** tab, turn on `Supports Verbose PATCH
   (Preview)` and `Run Tests Sequentially (Preview)`; the other three are on by default. Only
   the first changes the payloads — see §1. The `Required ?` checkbox on each attribute row
   changes how an attribute is asserted, not which tests run.

The tunnel is HTTPS-terminated, so the host's lack of TLS does not matter here. It does matter
everywhere else — see [`net48-hosting.md`](../net48-hosting.md).

### 2.2 Reading what actually happened

The validator reports assertions, not traffic: *"The value of `displayName` is Missing from the
fetched Resource"* names a symptom and nothing else. Every diagnosis below came instead from
**ngrok's request inspector**, which records each request and response in full:

```
curl -s "http://127.0.0.1:4040/api/requests/http?limit=500"
```

Each record's `request.raw` and `response.raw` are base64 of the complete HTTP message. Decode
them and the operation the validator sent sits next to the resource it got back. That is what
turned *"Update User userName failed"* into *"`{"op":"replace","value":{"userName":"…"}}` carries
no `path`, and both `Apply` overloads return on their first line when `Path` is null"*.

Without this step the failure list is close to unreadable: the same validator message —
*"Missing from the fetched Resource"* — was produced by four unrelated causes (D1, D2, D3, and
the validator's own bug), and one of them was not our defect at all.

### 2.3 Confirming a diagnosis

Each finding was reduced to a standalone request pair against `localhost` before any code was
changed, so that the fix could be judged without a validator round trip. Example, D4:

```bash
# three addresses, three replaces by type; only work and home landed
curl -X PATCH .../Users/$ID -d '{"schemas":["urn:ietf:params:scim:api:messages:2.0:PatchOp"],
 "Operations":[{"op":"replace","path":"emails[type eq \"other\"].value","value":"x@y.z"}]}'
```

### 2.4 Guarding against regressions

`tests/integration` was run on both legs before and after. Two results are worth recording:

- Removing the type guard in the email/phone patchers outright (rather than completing it) broke
  `enterprise.spec.ts > invents nothing when a value path matches no entry`. That test is
  correct and the first attempt at D4 was not; see §4.4.
- `scim-compliance.spec.ts > answers 401 with a challenge, and a SCIM body if it sends one at
  all` fails on the net48 leg. It fails identically with these changes stashed, so it is
  **pre-existing** and unrelated: Web API's `AuthorizeAttribute` answers 401 with its own
  error document. Fixed separately by `ScimUnauthorizedResponseHandler`.

---

## 3. What the validator exercises that this repository's tests did not

Worth stating plainly, because it is the reason the run was worth doing:

| Payload shape | Sent by Entra | Covered by `tests/integration` before this run |
|---|---|---|
| `add`/`replace` with no `path`, value an object of attributes | yes | no |
| Attribute keys in dotted form inside such an object (`"name.formatted"`) | yes | no |
| Extension attributes fully qualified inside such an object (`"urn:…:User:employeeNumber"`) | yes | no |
| A value filter over a **boolean** sub-attribute (`roles[primary eq true]`) | yes | no |
| `members[type eq "Group"].value` — a sub-attribute of one membership entry | yes | no |
| `emails[type eq "other"]`, `phoneNumbers[type eq "home"]` | yes | partially — only `work` and `home` for emails, `work` for phones |

---

## 4. Defects found

### D1 — a PATCH operation with no `path` was discarded

**Authority:** RFC 7644 §3.5.2.1 (add), §3.5.2.3 (replace) — *"If omitted, the target location
is assumed to be the resource itself. The `value` parameter contains a set of attributes to be
added"*.

`Core2EnterpriseUserExtensions.Apply` and `ProtocolExtensions.Apply(Core2Group, …)` both open
with a guard requiring `operation.Path`, so

```json
{"op": "replace", "value": {"userName": "lamont@thielstreich.biz"}}
```

returned 204/200 and changed nothing — the worst failure mode available, since the client is
told the write landed.

**Fix:** `ProtocolExtensions.Expand` turns each operation as it arrived into the operations to
apply. With a path it yields the one operation it always did; without one it reads the value as
a JSON object and yields one operation per member, as though the client had named that attribute
in a path. A member whose own value is an object is expanded again — joined with `:` when the
parent is a schema URN, `.` otherwise, so both `urn:…:User` + `department` and `name` +
`givenName` come out as paths the existing appliers already handle. Depth is capped
(`MaximumExpansionDepth`).

A path-less `remove` is still ignored rather than guessed at: RFC 7644 §3.5.2.2 requires `path`
for `remove`, so such an operation names nothing to remove.

### D2 — the roles patcher read every filter as `type` and every write as `value`

**Authority:** RFC 7643 §2.4 (`primary` is a Boolean; `roles` has `value`, `display`, `type`,
`primary`), RFC 7644 §3.5.2.

`PatchRoles` matched entries on `item.ItemType` regardless of what the filter named, and always
assigned `role.Value` regardless of what the value path named. So
`roles[primary eq true].display` matched nothing, appended an entry typed `"true"`, and wrote
the display name into its value:

```json
"roles": [{"value": "…", "type": "true", "primary": true}]
```

**Fix:** the filter's sub-attribute selects the entry and the value path's sub-attribute is
written; both understand `value`, `display`, `type` and `primary`. A new entry is seeded so the
filter that just failed to find it would now find it.

### D3 — `members[type eq "X"].value` replaced the whole membership

**Authority:** RFC 7643 §4.2 (`members` has `value`, `$ref`, `display`, `type`),
RFC 7644 §3.5.2.3.

The `AttributeNames.Members` case ignored `Path.ValuePath`, so an operation naming a
sub-attribute of one entry fell through to the whole-collection `Replace` case and overwrote
every member with the single value the operation carried.

**Fix:** `PatchMember`, mirroring D2. A replace whose filter matches no entry adds one, per
§3.5.2.3's rule that a target which does not exist is treated as an add.

### D4 — the email and phone type guards were incomplete

**Authority:** RFC 7643 §4.1.2 — emails: `work`, `home`, `other`; phone numbers: `work`, `home`,
`mobile`, `fax`, `pager`, `other`.

Both patchers reject a value path naming a type outside a fixed list. The lists were short:
emails admitted `home` and `work` only, phone numbers `fax`, `mobile` and `work` only. So
`emails[type eq "other"].value` and `phoneNumbers[type eq "home"].value` answered success and
changed nothing. Every constant needed was already defined on `ElectronicMailAddressBase` and
`PhoneNumberBase` and simply not referenced.

**Fix:** complete both lists. The guard itself stays. Deleting it — the first attempt — makes a
value path naming an undefined type invent an entry carrying that type, which
`enterprise.spec.ts > invents nothing when a value path matches no entry` correctly rejects.

### D5 — `DELETE` of a resource that is not there returned 204

**Authority:** RFC 7644 §3.6.

`InMemoryUserProvider.DeleteAsync` and its group counterpart removed the entry if present and
returned success either way, so a client could not tell a stale identifier from a live one.
`BaseEduPassScimProvider` already returned 404 here; the in-memory providers were the outliers.

**Fix:** `Remove` returns whether anything was removed; a false answer throws
`HttpResponseException(HttpStatusCode.NotFound)`.

The existing tests already tolerated this: `users.spec.ts > is idempotent` accepts
`[204, 404]`, and `bulk.spec.ts` asserts only that bulk and the direct endpoint agree.

---

## 5. The two remaining failures are a validator defect

Both are `roles[primary eq …]`, in `POST /Users — Create a new User` and
`PATCH /Users/Id — Patch User - Replace Attributes`. The endpoint is correct and no change is
warranted.

**The evidence.** From the captured traffic for the PATCH case:

```
asked for : roles[primary eq true].value   = OPEESESGOIXD
            roles[primary eq true].display = FGYESLCEIMOA
response  : [{"display":"FGYESLCEIMOA","value":"OPEESESGOIXD","primary":true}]
```

The response satisfies the filter, and the validator still reports both sub-attributes
*"Missing from the fetched Resource"*. For the POST case the validator sends

```json
"roles": [{"primary": "true", "value": "GAFWPFFKZHAD", "display": "EHEAYDIHTRYM"}]
```

— `primary` as a JSON **string**, which RFC 7643 §2.4 defines as a Boolean. We parse it to the
Boolean our `/Schemas` advertises, echo it correctly, and its verifier — still holding the
string — cannot match its own filter against a real Boolean. Every other value filter in the
suite selects on `type`, a string, and passes; the only Boolean one fails, in both the `true`
and `"true"` literal forms.

**Not a newer specification.** RFC 7643 and RFC 7644 are still current. RFC 9865 (cursor
pagination), RFC 9944 (device schema) and RFC 9967 (Security Event Tokens) update them but
touch nothing here; `draft-ietf-scim-roles-entitlements` is an expired Internet-Draft. No SCIM
version, at any date, types `primary` as a string.

**Acknowledged by Microsoft.** Danny Zollner (Microsoft), on a report whose payload has the same
shape and the same generated test values this run produced:

> This appears to be a bug in the SCIM Validator. We're planning some improvements and fixes for
> the validator early next year and will look into correcting this when we start that work.

— [Microsoft Q&A 2123904](https://learn.microsoft.com/en-gb/answers/questions/2123904/in-scim-validator-tool-in-request-at-attribute-rol).
A separate thread has a Microsoft moderator conceding the same of the live provisioning service.

**Do not work around it.** Emitting `primary` as a string would turn both tests green. The same
report records that Entra ID then **fails to deserialize its own subsequent `GET`**, breaking
the provisioning cycle — so matching the validator's bug would pass the test and break real
provisioning.
[Microsoft Q&A 5912249](https://learn.microsoft.com/en-us/answers/questions/5912249/scim-provisioning-bugs-roles-mapping-sends-empty-o).

---

## 6. What this oracle is blind to

- **The provider, not the library.** The run exercises `InMemoryProvider`. D1–D4 are library
  defects and apply to every provider; D5 was a sample-provider defect. The Edupass provider was
  not exercised at all.
- **One leg.** net48 only. D1–D4 are in `Microsoft.SCIM.Core` and so are shared, but the net10.0
  leg was verified by `tests/integration`, not by the validator.
- **Its own correctness.** §5 is the standing example. A validator failure is a lead, not a
  verdict; confirm it against the captured traffic and the RFC before changing anything.
- **Everything outside its 31 tests.** No bulk, no pagination, no `attributes` /
  `excludedAttributes` projection, no ETag concurrency, no authentication negatives.
  [`scim-conformance.md`](../scim-conformance.md) remains the specification; this is one reading
  against it.

---

## 7. Re-running

```bash
dotnet build Microsoft.SCIM.sln

SCIM_API_KEY=<key> ASPNETCORE_ENVIRONMENT=Development \
  Microsoft.SCIM.WebHostSample.Net48/bin/Debug/net48/Microsoft.SCIM.WebHostSample.Net48.exe

ngrok http 5000 --host-header=localhost
```

Then *Discover Schema* → endpoint `https://<tunnel>/scim`, token `<key>` → *Test Schema*.
Keep the ngrok inspector (`http://127.0.0.1:4040`) for §2.2; it is the part of this method that
does the work.

Results are per-run: the validator generates fresh identifiers and values each time, and the
in-memory store does not survive a restart, so counts are comparable across runs but individual
values are not.

### 7.1 Driving the validator with `playwright-cli`

Run 7 was driven this way. The validator has no API, so the browser *is* the interface; this
automates the clicking, not the judging. Every command below is PowerShell on Windows.

**Why attach rather than open.** The site requires a signed-in Microsoft account.
`playwright-cli attach --extension=chrome` joins the Chrome you are already signed into, so
there is no credential to script and none to leak into this repository. A fresh
`playwright-cli open` would land on a login page.

**1. Host and tunnel.** As in §7, but with the tunnel's URL read back programmatically:

```powershell
# background: the host
$env:SCIM_API_KEY="<key>"; $env:ASPNETCORE_ENVIRONMENT="Development"
& ".\Microsoft.SCIM.WebHostSample.Net48\bin\Debug\net48\Microsoft.SCIM.WebHostSample.Net48.exe" http://localhost:5000

# background: the tunnel
ngrok http 5000 --host-header=localhost --log=stdout

# foreground: what the tunnel is called, and whether it reaches the host
$url = ((Invoke-RestMethod "http://127.0.0.1:4040/api/tunnels").tunnels |
         Where-Object proto -eq https).public_url
(Invoke-WebRequest "$url/scim/ServiceProviderConfig" `
   -Headers @{Authorization="Bearer <key>"} -UseBasicParsing).StatusCode   # expect 200
```

Check that 200 before touching the browser. It separates "the tunnel is wrong" from "the
validator is unhappy", which the validator's own error message will not do for you.

**2. Attach, and open the site if it is not already open:**

```powershell
playwright-cli attach --extension=chrome
playwright-cli -s=chrome goto https://scimvalidator.microsoft.com/
playwright-cli -s=chrome snapshot --depth=25
```

The first attach raises the extension's connection dialog and a tab picker. To skip both on
later runs, set the extension's auth token in the environment — the CLI forwards it as
`?token=` on the connect URL, and the extension connects straight through when it matches:

```powershell
$env:PLAYWRIGHT_MCP_EXTENSION_TOKEN = "<token>"
```

The token is shown in the **Auth Token** section of that dialog. It is a credential for your
whole browser, not for this repository: the dialog's own warning is that allowing the
connection *"exposes the entire browser to the client, including any signed-in sessions,
cookies, and content in other tabs and windows"*. Keep it in your environment and out of
version control. To revoke, regenerate it in the dialog **and restart the browser** —
regenerating alone does not drop a live connection.

Tabs reachable by the client are the ones in Chrome's **Playwright** tab group, which the
extension creates and which you can drag further tabs into. It is worth keeping between runs,
since it is what spares you the tab picker: `detach` leaves it alone, and only closing its
last tab destroys it. Never `close` an attached session and never `delete-data` against one —
both act on the browser you attached to, which here is your own daily Chrome profile.

Refs (`e70`, `e95`, …) are assigned per snapshot and change as the page changes. Re-snapshot
after every navigation rather than reusing a ref across steps; `playwright-cli find "<text>"`
re-locates one element without paying for a whole snapshot.

**3. Discover Schema:**

```powershell
playwright-cli -s=chrome eval "el => el.click()" "getByTestId('getstartedview-discover-schema-button-id')"
playwright-cli -s=chrome snapshot e78          # the form
playwright-cli -s=chrome fill e95 "$url/scim"  # SCIM Endpoint
playwright-cli -s=chrome fill e98 "<key>"      # Token
playwright-cli -s=chrome eval "el => el.click()" e101   # Discover Schema (enabled once both are filled)
```

`Bearer Token` is the default of the two auth radios, which is the one API-key mode wants.
The *Discover Schema* button stays `[disabled]` until both fields are non-empty, so a snapshot
showing it disabled means a `fill` silently missed rather than that the site is broken.

**4. Settings, to reproduce run 5–7 rather than the default three:**

```powershell
playwright-cli -s=chrome eval "el => el.click()" e118    # Settings tab
playwright-cli -s=chrome snapshot e114
playwright-cli -s=chrome eval "el => el.click()" e1254   # Run Tests Sequentially (Preview)
playwright-cli -s=chrome eval "el => el.click()" e1288   # Supports Verbose PATCH (Preview)

# confirm: the five settings checkboxes are the unlabelled ones at the end
playwright-cli -s=chrome --raw eval "() => [...document.querySelectorAll('input[type=checkbox]')].map(c => c.getAttribute('aria-label') + '=' + c.checked).join('; ')"
```

The attribute rows are checkboxes too and there are hundreds of them, so read that list from
the end. Only `Supports Verbose PATCH` changes what is sent — see §1.

**5. Run, and read the result:**

```powershell
playwright-cli -s=chrome eval "el => el.click()" e1248   # Test Schema
Start-Sleep -Seconds 25

playwright-cli -s=chrome --raw eval "() => { const m = document.body.innerText.match(/Passed \d+\/\d+|Failed \d+\/\d+|Preview \d+\/\d+/g); const bad = [...document.querySelectorAll('.css-154n8c7')].map(e => (e.closest('div')?.parentElement?.innerText ?? '').replace(/\s+/g,' ').slice(0,80)); return JSON.stringify({summary: m, failing: [...new Set(bad)]}, null, 1) }"
playwright-cli -s=chrome screenshot --filename=entra-validator-run<N>.png
```

The failing tests carry a distinct icon class — `css-154n8c7` at the time of run 7. It is a
generated Chakra class and will not survive a redesign of the site, so if that selector
returns `[]` while the summary says something failed, expand each *Show Details* and read
them instead of trusting the empty list.

**6. Clean up.** `detach`, not `close` — the Chrome being driven is the user's own:

```powershell
playwright-cli -s=chrome detach
Get-Process ngrok, Microsoft.SCIM.WebHostSample.Net48 -ErrorAction SilentlyContinue | Stop-Process -Force
```

### 7.2 Three traps this method has already cost

- **`click` times out on every button.** The page animates continuously, so Playwright's
  actionability check never sees an element "stable" and gives up after 5s — on a button that
  is perfectly clickable. `eval "el => el.click()"` dispatches the click directly and skips
  that check. Reach for it as soon as a `click` reports *"waiting for element to be visible,
  enabled and stable"* against an element the log shows it already resolved.
- **The ngrok inspector retains captures across sessions.** Run 7's pane showed six group
  PATCHes at 204 and six at 200, which read as the change misfiring; the 204s were run 6's,
  from the previous day. Sort by `start` before concluding anything:
  ```powershell
  (Invoke-RestMethod "http://127.0.0.1:4040/api/requests/http?limit=200").requests |
    Where-Object { $_.request.method -eq "PATCH" } |
    Select-Object start, @{n='status';e={$_.response.status}}, @{n='uri';e={$_.request.uri}} |
    Sort-Object start
  ```
- **`--host-header=localhost` is not optional**, and its absence looks like a validator fault
  rather than a tunnel one — §2.1 has the reason.
