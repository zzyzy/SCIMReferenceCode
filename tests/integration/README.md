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

## Coverage

```bash
dotnet tool install --global dotnet-coverage
pnpm run test:coverage
```

The host is launched by the collector rather than directly:

```
dotnet-coverage collect "dotnet Microsoft.SCIM.WebHostSample.dll" \
  --session-id scim-integration \
  --output-format cobertura \
  --output integration-coverage.xml
```

**The session id matters.** `dotnet-coverage` writes its report when collection ends
cleanly, so teardown calls `dotnet-coverage shutdown scim-integration`. The first
version of this killed the process tree instead, and produced no report at all —
which is why `global-setup.ts` says so out loud if the file is missing rather than
letting a silent absence read as "coverage ran and found nothing".

Coverage measures the **host**, not the tests. A recent run: 46% of lines overall,
91% of the sample host, 74% of `Microsoft.SCIM.AspNetCore`, 37% of `Microsoft.SCIM`
— the last is low because the library carries a bulk implementation, a client-side
request builder and several deserializer paths that no HTTP request reaches.

Only the net10.0 leg is collected. The net48 sample is a framework executable, not a
`dotnet <dll>` launch, so it needs a different collector invocation.

## Layout

| Path | Holds |
|---|---|
| `src/host.ts` | starting and stopping the host, and the coverage session |
| `src/client.ts` | the `fetch` wrapper, dev-token minting, resource helpers |
| `src/global-setup.ts` | one host for the whole run |
| `suites/users.spec.ts` | create, read, replace, patch, delete, `externalId` |
| `suites/groups.spec.ts` | membership, full sync, rename and conflicts |
| `suites/filters.spec.ts` | the nine operators, projection, pagination |
| `suites/enterprise.spec.ts` | the enterprise extension, complex and multi-valued attributes |
| `suites/protocol.spec.ts` | discovery, authorization, errors, headers, verbs, Bulk |
| `suites/robustness.spec.ts` | hostile input, concurrency, a soak |

## Two things worth knowing before changing these

**Files do not run in parallel.** One host means one in-memory store, and the tests
that count resources or walk pages cannot tolerate another file mutating it
underneath them. `fileParallelism: false` in `vitest.config.ts` is load-bearing.

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
