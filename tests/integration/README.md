# SCIM integration tests

Vitest + `fetch` + TypeScript, run against a live sample host. No mocks: every test
sends real HTTP to a real process, which is the only way most of what these cover
can be observed at all.

## Running

```bash
pnpm install

pnpm test              # net10.0 leg
pnpm run test:net48    # net48 / OWIN leg
pnpm run test:coverage # net10.0 leg with dotnet-coverage
pnpm run typecheck
```

Build the solution first — the tests start the sample host from `bin/Debug` and fail
with a clear message if it is not there:

```bash
dotnet build Microsoft.SCIM.sln
```

`SCIM_HOST_LOG=1` forwards the host's stdout. `SCIM_BASE_URL=http://host/scim` tests
an already-running endpoint instead of starting one.

### Testing a relying party's own Edupass endpoint

`SCIM_BASE_URL` overrides `BASE_URL`, which is the *core* host - and the core host binds
`/Users` to `Core2User`, so the `edupass*` suites do not follow it. They address
`EDUPASS_BASE_URL`, a separate sample process, because two providers cannot share one
`/Users` route inside one host.

So to point the Edupass suites at your own implementation, set
`SCIM_EDUPASS_BASE_URL` instead:

```bash
SCIM_EDUPASS_BASE_URL=http://your-host/scim pnpm vitest run suites/edupass.spec.ts suites/edupass-conformance.spec.ts
```

It falls back to `SCIM_BASE_URL` when unset, since a real relying party serves the
Edupass resource types at its one base address. Either variable suppresses host startup.

A real relying party will not accept a token minted from the committed development key,
so supply its own credential too:

```bash
SCIM_AUTH_HEADER=x-dsapi-key SCIM_AUTH_VALUE=<key>   SCIM_EDUPASS_BASE_URL=http://your-host/scim pnpm vitest run suites/edupass.spec.ts
```

It is applied only where a test has not set an `Authorization` header itself, so the
negative authentication cases still present the bad credential they were written to
present rather than being handed a valid one.

`pnpm run test:fims` is that command for the FIMS relying party. It sets the address and
the header name but **not** `SCIM_AUTH_VALUE`, which is a secret and has to come from the
environment:

```bash
SCIM_AUTH_VALUE=<key> pnpm run test:fims
```

**These suites write.** They create and delete users and groups, and against a relying
party backed by a real system that means real rows and, depending on the implementation,
real notification e-mail to administrators. Point them at a disposable database.

Two limits are worth knowing before reading a green run as full conformance:

- `edupass-test-plan.spec.ts` is **not** included, because part of it addresses
  `EDUPASS_STRICT_BASE_URL` - a host built with JWT validation enforced over a committed
  symmetric key. No external endpoint can stand in for that, and the cases that use it
  will fail against one.
- `edupass-uinfin.spec.ts` needs a host constructed with `requireUinFin: true`. A relying
  party that does not store UIN/FIN cannot satisfy it and should not try.

The other four sample hosts - unimplemented, faulty, uinFin and strict-JWT - stay
in-memory. They are the contract tests for the shared library, not for a relying party,
and each needs a provider that only a purpose-built host can supply.

## Six hosts, not one

Most of what these tests cover needs a particular provider behind the endpoints, and a
provider is chosen at startup - so the run starts one host per behaviour, all from the
same sample, selected with `SCIM_PROVIDER`:

| Port (net10 / net48) | `SCIM_PROVIDER` | Provider | What only it can show |
|---|---|---|---|
| 5180 / 5181 | *(unset)* | `InMemoryProvider` | RFC 7643/7644 over a working provider |
| 5183 / 5184 | `edupass` | `InMemoryEduPassProvider` | the Edupass extension, its closed value sets, its provider obligations |
| 5185 / 5186 | `unimplemented` | `UnimplementedProvider` | that an unimplemented operation is 501, not 500 |
| 5187 / 5188 | `faulty` | `FaultyProvider` | that a provider fault becomes a SCIM error body, not a stack trace |
| 5189 / 5190 | `edupass` + `SCIM_EDUPASS_REQUIRE_UINFIN=1` | `InMemoryEduPassProvider(true)` | UIN/FIN required, and advertised as required |
| 5191 / 5192 | `edupass` + `SCIM_ENFORCE_JWT=1` | `InMemoryEduPassProvider` | that an expired token, a wrong issuer or a wrong audience is refused |

Edupass needs its own process rather than its own route: it binds `/Users` to
`EduPassUser`, and two providers cannot serve one route. The others need their own
because a provider is a singleton chosen at startup.

`src/client.ts` has one send helper per host - `scim`, `edupass`, `unimplemented`,
`faulty`, `edupassUinFin` - so a test names the host it means. The strict-JWT host is the
one exception: it is the same provider as 5183, so a case reaches it by passing
`base: EDUPASS_STRICT_BASE_URL` rather than through a helper of its own.

### Why the strict-JWT host exists

The sample disables issuer, audience, lifetime and signing-key validation when
`ASPNETCORE_ENVIRONMENT=Development`, and Development is the only environment these tests
can start a host in - the Release branch resolves its signing keys over OIDC metadata, so
it needs a live authority. On the ordinary hosts an expired token, a token from another
issuer and a token for another audience are therefore all accepted, and a test asserting
401 would be asserting the bypass rather than the check.

`SCIM_ENFORCE_JWT=1` turns those four checks back on over the same committed symmetric key,
which is what makes the rejection observable. It only ever makes validation stricter, so it
is safe to leave in the sample: the `#if DEBUG` guard that keeps the bypass out of a Release
build is untouched.

## Coverage

```bash
dotnet tool install --global dotnet-coverage
pnpm run test:coverage
```

The collector runs in server mode and each host connects to it, so all five land in one
report:

```
dotnet-coverage collect --server-mode --session-id scim-integration \
  --settings coverage.settings.xml --output-format cobertura \
  --output integration-coverage.xml
dotnet-coverage connect scim-integration dotnet Microsoft.SCIM.WebHostSample.dll
```

**The session id matters.** `dotnet-coverage` writes its report when collection ends
cleanly, so teardown calls `dotnet-coverage shutdown scim-integration`. The first
version of this killed the process tree instead, and produced no report at all - which
is why `global-setup.ts` says so out loud if the file is missing rather than letting a
silent absence read as "coverage ran and found nothing".

Coverage measures the **hosts**, not the tests. A recent run: 80% of lines overall, 91%
of the sample host, 90% of `Microsoft.SCIM.AspNetCore`, 77% of `Microsoft.SCIM` and 77%
of `SCIM.EduPass`.

### What the number counts

`coverage.settings.xml` keeps plumbing out of the denominator, and says next to each
exclusion which of three reasons it is there for: **dead** (nothing in this repository
calls it, on either leg), **client** (only an outbound SCIM client runs it), or
**plumbing** (serialization, configuration or utility scaffolding, not SCIM rules). The
classification came from tracing callers from both hosting layers, so a member that only
Bulk reaches is still counted.

Without it the same run reports 46%, and the difference is almost entirely
`ProtocolExtensions`' client-side request builders, a Newtonsoft deserializer family both
hosts bypass, and the SET/event-token types no host wires up.

What is *not* excluded, and is the bulk of what remains uncovered: the per-verb
`catch` arms in `ScimRequestHandler` and `ScimDiscoveryRequestHandler`. Reaching every
one means a provider that throws a chosen exception type from a chosen operation, and
past the four provider behaviours above that stops being a test of anything.

Only the net10.0 leg is collected. The net48 leg runs the same suites - `pnpm run
test:net48`, and it must stay green - but its report would have to be merged rather than
collected alongside.

## Layout

| Path | Holds |
|---|---|
| `src/host.ts` | starting and stopping the five hosts, and the coverage session |
| `src/client.ts` | the `fetch` wrapper, dev-token minting, per-host send helpers, resource helpers |
| `src/global-setup.ts` | one set of hosts for the whole run |
| `coverage.settings.xml` | what the coverage number counts, and why |
| `SPEC-TRACEABILITY.md` | every clause of the interface specification, core or Edupass, and the test that proves it |
| `suites/users.spec.ts` | create, read, replace, patch, delete, `externalId` |
| `suites/groups.spec.ts` | membership, full sync, rename and conflicts |
| `suites/filters.spec.ts` | the nine operators, projection, pagination |
| `suites/filter-grammar.spec.ts` | grouping, precedence, value paths, malformed expressions |
| `suites/enterprise.spec.ts` | the enterprise extension, complex and multi-valued attributes |
| `suites/patch-attributes.spec.ts` | PATCH across every attribute the User resource models |
| `suites/schema-extensions.spec.ts` | untyped extensions, group PATCH, nested projection |
| `suites/bulk.spec.ts` | RFC 7644 3.7 - verbs, `bulkId` references, `failOnErrors`, malformed operations |
| `suites/protocol.spec.ts` | discovery, authorization, errors, headers, verbs |
| `suites/robustness.spec.ts` | hostile input, concurrency, a soak |
| `suites/edupass.spec.ts` | the Edupass specification at a party that does not store UIN/FIN |
| `suites/edupass-uinfin.spec.ts` | the same at a party that does |
| `suites/edupass-test-plan.spec.ts` | the 25 numbered cases of `test-plan.xlsx` and the `RP-` cases of `M2-SCIM RP Testcases`, and the CSV they produce |
| `suites/edupass-conformance.spec.ts` | the Edupass specification read as a contract - discovery payloads, `groups`, `$ref` |
| `suites/scim-compliance.spec.ts` | the same specification's core-SCIM clauses, against the core host |
| `suites/resource-types.spec.ts` | RFC 7643 6 - base schema and `schemaExtensions` |
| `suites/unimplemented.spec.ts` | what a provider that implements nothing answers |
| `suites/faulty-provider.spec.ts` | what a provider that throws answers |

## The Edupass test plan

`suites/edupass-test-plan.spec.ts` runs two plans against the Edupass host and writes them
back out as `edupass-test-plan-results.<leg>.csv` at the repository root — one per leg, since
the leg is what the run was against:

- the 25 cases of `test-plan.xlsx`, numbered 1 to 25 as the sheet numbers them;
- the cases of `M2-SCIM RP Testcases`, the suite Edupass itself runs against a relying party,
  prefixed `RP-` and otherwise keeping the document's own labels — `RP-JWT-1` to `RP-JWT-5`
  for its JWT Authentication tests, `RP-1a`, `RP-1`, `RP-2a` and so on for its SCIM Operation
  tests, and `RP-0` for the setup and pre-clean step it describes. The prefix is only there to
  keep the two numbering schemes apart in one CSV.

The CSV columns are the plan's own:

| S/N | Datetime | Input | Output | Status | Remarks |
|---|---|---|---|---|---|

Input and Output keep the plan's own layout — the request line, then the body indented under
it — so a row here reads next to a row in the plan's execution sheet without translation. They
are the run's actual traffic: every request a case makes goes through `src/test-plan-recorder.ts`,
which records it and returns the response to the case.

**The xlsx plan is written for FIMS, and much of it is not SCIM.** A UPA created and routed to a
user admin, positions added or overwritten on approval, a user banned or unbanned, a
notification triggered — none of that is visible at this endpoint, because the relying party
does it after the call returns. Each case therefore asserts the protocol half and records the
FIMS half in Remarks, so a row that says Pass says what it passed.

Two mappings the cases make explicit, because the plan leaves them implicit:

- **A Location is a Group** whose `displayName` encodes the location code, as the plan's own
  sample data shows (`1001_app1_admin`). Adding a user to a location is adding them to that
  group.
- **`groups` is read-only on the User.** Cases 12–14 and 18–20 ask for a location change
  through `PATCH /Users`, and RFC 7643 4.1.2 makes that impossible: membership is written on
  the Group and derived onto the User. Those cases assert the `400 invalidPath` and record what
  Edupass should send instead. They also check the refusal changed nothing — a rejected patch
  that had dropped the old location would be the worst outcome.

The `RP-` cases need none of that qualifying: `M2-SCIM RP Testcases` is written against a SCIM
endpoint throughout, so every one of its expectations is checkable here. Three points about how
they run:

- **Its fixed identifiers are used as written** — `999900000001`, `X_<APPCODE>_TEST1` — because
  the document chooses them deliberately, so a resource is recognisable in the RP's logs. `RP-0`
  performs the pre-clean the document describes, and `RP-6` and `RP-7` delete everything, so a
  completed run leaves nothing behind and an interrupted one cannot block the next. `<APPCODE>`
  has no counterpart on the reference host, so the cases use a fixed stand-in.
- **`uinFin` is omitted from every body**, by the document's own rule that a field the RP does
  not declare at `GET /Schemas` is left out. `RP-0` asserts that this host still does not
  declare it, so the omission cannot go stale.
- **The JWT cases go to the strict-JWT host**, for the reason given under *Why the strict-JWT
  host exists* above. `RP-0` also sends a valid token there, so that the five 401s that follow
  are rejections of the token rather than of the host. The CSV records the full URL and the
  Authorization header for these rows — without the header, five different requests would all
  read as `GET /Users`.

## The Edupass conformance suite

`suites/edupass-conformance.spec.ts` asks a different question from the test plan. The plan is
basic acceptance — can each endpoint be called, does it answer sensibly. The conformance suite
asks whether the response body is the one the specification document actually describes, which
means the parts the plan never inspects: the `/Schemas` and `/ResourceTypes` payloads, the
`groups` attribute on every User response that should carry it, and the `$ref` cross-references
between a User and its Groups.

**The other half of the same document.** `suites/scim-compliance.spec.ts` walks the same
specification for the clauses RFC 7643/7644 already mandates — the error body's `schemas`,
string `status` and `scimType`; the list envelope; `count` and `startIndex`; a filter that
matches nothing answering an empty list rather than a 404; `excludedAttributes=members`; the
`ServiceProviderConfig`, `/Schemas` and `/ResourceTypes` payloads — and proves them against the
core host. The two files are the split the goal names: read `scim-compliance` to answer "is this
service SCIM", read `edupass-conformance` to answer "is this service an Edupass relying party".
Neither may assert the other's half, and `scim-compliance` may not name Edupass at all.

**Where a finding goes.** Everything in that file is something the Edupass specification
requires and RFC 7643/7644 does not. A gap that turns out to be the SCIM library's belongs in a
SCIM suite instead — `resource-types.spec.ts`, `groups.spec.ts`, `protocol.spec.ts`,
`filters.spec.ts` — because fixing it there fixes it for every relying party rather than only
an Edupass one. Several of the suite's original failures moved that way: `schemaExtensions` on a
resource type, `$ref` on `members`, `specUri`, and the `filter.maxResults` cap are all RFC
requirements, so they are tested against the core host and only their Edupass consequences are
asserted here.

**Rate limiting and TLS are not in it.** Both are the host's responsibility rather than the
library's; see section 6 of `docs/edupass-integration.md`.

## Two things worth knowing before changing these

**Files do not run in parallel.** Each host holds one in-memory store, and the tests
that count resources or walk pages cannot tolerate another file mutating it
underneath them. `fileParallelism: false` in `vitest.config.ts` is load-bearing.

**A test names its host.** A request sent with `scim` goes to the core sample, and one
sent with `edupass` goes to the Edupass one. Sending an Edupass request to the core host
gets it answered by the plain reference provider, which proves nothing - the assertion
still passes or fails, but about the wrong service.

**Assert against the contract, not the implementation.** An earlier version of the
filter suite asserted that an unsupported operator is refused — written against `co`,
then repointed at `ne`, and both became supported, so the test failed each time the
product improved. It now asserts on an attribute the schema does not define, which
cannot become answerable. If a test starts failing because something got better, the
test was measuring the wrong thing.

## The dev token

The samples no longer ship a `/scim/token` endpoint. `client.ts` mints the same HS256
token from the development key committed to `appsettings.Development.json` — which is
the point that key makes: anyone holding it can mint one. The key is duplicated in
`host.ts` rather than read from the file, so that changing the sample's key breaks
these tests loudly instead of having them quietly follow along.

## Known noise

Vitest reports `close timed out` and "something prevents Vite server from exiting"
after a successful run, costing about ten seconds. The `hanging-process` reporter
attributes it to file handles with no stack trace — vitest's own, not anything here.
The exit code is 0, so CI is unaffected.
