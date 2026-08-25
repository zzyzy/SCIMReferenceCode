# SCIM Reference Code

A SCIM 2.0 (RFC 7643 / RFC 7644) provisioning endpoint you can add to a .NET application.
It is a fork of Microsoft's [SCIMReferenceCode](https://github.com/AzureAD/SCIMReferenceCode),
reworked so the same library can be hosted on **ASP.NET Core (.NET 10)** and on
**ASP.NET Web API 2 (.NET Framework 4.8)** with identical wire behaviour.

You bring a *provider* — the class that reads and writes your users and groups. The library
brings everything else: routing, JSON, filters, PATCH semantics, pagination, bulk, discovery
endpoints, error bodies and status codes.

## 1. Overview

| Endpoint | What it does |
|---|---|
| `/Users` | Create, get, list, filter, replace, patch, delete users |
| `/Groups` | Create, get, list, filter, replace, patch, delete groups |
| `/Schemas` | The attributes this service actually supports |
| `/ResourceTypes` | Which resource types this service serves |
| `/ServiceProviderConfig` | Which SCIM features this service supports |
| `/Bulk` | Batched operations |

Two hosting legs, one library:

| Leg | Library TFM | Hosting project | Sample |
|---|---|---|---|
| ASP.NET Core Web API | `net10.0` | `Microsoft.SCIM.AspNetCore` | `Microsoft.SCIM.WebHostSample` |
| ASP.NET Web API 2 | `net48` | `Microsoft.SCIM.AspNet` | `Microsoft.SCIM.WebHostSample.Net48`, `…IIS` |

Both legs answer with the same routes, status codes, JSON bodies and headers.
`docs/scim-conformance.md` is the spec both are held to.

**Pick .NET 10 unless you are stuck on .NET Framework.** Pick net48 when you must host inside
an existing .NET Framework application or IIS site that cannot move.

### Running the samples

Both samples are HTTP-only development harnesses with an in-memory provider. Neither enables
HTTPS — TLS is the host's job.

```bash
dotnet build Microsoft.SCIM.sln

# .NET 10 — http://localhost:5000
dotnet run --project Microsoft.SCIM.WebHostSample

# net48 — OWIN self-host, Windows only, http://localhost:5000
dotnet run --project Microsoft.SCIM.WebHostSample.Net48
```

Set `ASPNETCORE_ENVIRONMENT=Development` for the local dev-token flow. Building the net48
projects requires Windows.

> **Do not deploy either sample as-is.** The dev-mode `TokenValidationParameters` turn off every
> JWT check. They sit behind `#if DEBUG`, so a Release build cannot ship them, and both samples
> print a warning banner at startup. The signing key is a dummy committed to this repository.

## 2. Tech stack

| Area | Choice |
|---|---|
| Language | C#, `LangVersion` latest, .NET analyzers on |
| Target frameworks | `net48` and `net10.0` (library, auth, EduPass); hosting projects are single-TFM |
| JSON | Newtonsoft.Json 13.0.3 — both legs share one serializer config |
| Web (net10) | ASP.NET Core MVC 10, `Microsoft.AspNetCore.Mvc.NewtonsoftJson` |
| Web (net48) | ASP.NET Web API 2 (5.2.9) on OWIN (Microsoft.Owin 4.2.2) |
| Auth | `Microsoft.IdentityModel.*` 8.14, JwtBearer (net10), `Microsoft.Owin.Security.Jwt` (net48) |
| Logging | `Microsoft.Extensions.Logging.Abstractions` on both TFMs |
| DI | `Microsoft.Extensions.DependencyInjection` on both TFMs |
| Tests | Vitest + TypeScript, black-box HTTP against live hosts; `dotnet-coverage` for C# coverage |
| CI | GitHub Actions on `windows-latest` (net48 needs the targeting pack) |

## 3. Project layout

```
Microsoft.SystemForCrossDomainIdentityManagement/   the library (assembly Microsoft.SCIM)
  Schemas/     User, Group, attribute types, schema definitions
  Protocol/    filters, paths, PATCH operations, query/bulk messages
  Service/     request orchestration, providers, logging, results
  Compat/      vendored System.Web.Http.HttpResponseException (net10 only)

Microsoft.SCIM.AspNetCore/          hosting layer for ASP.NET Core (net10.0)
Microsoft.SCIM.AspNet/              hosting layer for ASP.NET Web API 2 (net48)

Anacle.ApiFramework.Authentication/ reusable JWKS + API-key auth for both legs
SCIM.EduPass/                       the EduPass profile: schema, validator, provider, auth

Microsoft.SCIM.WebHostSample/       sample on ASP.NET Core
Microsoft.SCIM.WebHostSample.Net48/ sample on OWIN self-host
Microsoft.SCIM.WebHostSample.IIS/   sample bolting SCIM onto an existing IIS Web API app

tests/integration/                  Vitest black-box suite
docs/                               integration guides and the conformance spec
```

The three things worth knowing:

- **Controllers live in the hosting projects, not the library.** The library has no
  framework-specific types outside `Compat/`.
- **All request orchestration lives in `Service/ScimRequestHandler.cs` and
  `ScimDiscoveryRequestHandler.cs`.** This is what keeps the two legs identical.
- **`Microsoft.SCIM.Core.csproj` produces the assembly `Microsoft.SCIM`** — the folder name is
  historical.

## 4. Architectural decisions

### 4.1 One shared handler, two thin hosting legs

A controller on either leg does three things: take the bound model, call a
`ScimRequestHandler<T>`, and turn the returned `ScimResult` into its framework's action result.
Nothing else.

`ScimResult` (`Service/ScimResult.cs`) is a hosting-neutral `{ StatusCode, Payload, Location,
ContentType }`. `ScimActionResult` (net48) and `ScimControllerBase.ToActionResult` (net10)
serialise it as `application/scim+json`.

Why: any behaviour written in a controller would have to be written twice and would drift.
Putting it in the handler means a conformance fix lands on both legs at once. There is
deliberately **no conventional-route fallback** on either leg, because Web API's default route
shape does not match ASP.NET Core's and a fallback is the largest single source of drift.

### 4.2 Logging

The library logs **one thing**: a failed SCIM operation, with the request that caused it.

Logging every request and response is the host's job — `UseHttpLogging` on ASP.NET Core, IIS
logging or Application Insights on net48 — and none of it is SCIM-specific. What a host
*cannot* do is log a SCIM failure, because the handlers convert a provider exception into a
`ScimResult` rather than letting it escape; by the time middleware sees the response there is
nothing left to catch.

- `ScimLoggerExtensions.LogScimFailure` writes at `Error`: correlation id, `EventId`, the
  exception, and the request's method, URI, headers and body.
- `ScimEventIds` names the events (`PostException`, `PatchNotSupportedException`, …) so you can
  filter on them.
- `ScimLogging` holds the process-wide knobs: `MaximumBodyLength` (default 10 MB) and
  `RedactedHeaders` (`Authorization`, `Proxy-Authorization`, `Cookie`, `Set-Cookie`). Bodies are
  logged verbatim — reproducing a failure means seeing what was sent — but credentials in
  headers are replaced.
- The logger is **yours**. Controllers resolve `ILogger<T>` from the container and hand it to
  the handler. Register Serilog, NLog, console, whatever you already use. No SCIM-specific
  adapter.

The body has to be buffered before model binding for this to work, which is what
`ScimRequestBufferingFilter` (net10) and `ScimRequestBufferingHandler` (net48) do.

*(This replaces Microsoft's `IMonitor` / `Notification` / `MonitoringMiddleware` machinery,
which was a private logging framework a consumer had to adapt to.)*

### 4.3 Global exception handling

Failures never reach the client as a stack trace or a bare 500.

1. **Inside the handler.** Each operation in `ScimRequestHandler<T>` catches and maps:
   `ArgumentException` → 400, `NotImplementedException` → 501, `NotSupportedException` → 501
   (400 with `invalidFilter` on query), and anything else → a 500 carrying a SCIM error body.
   `HttpResponseException` is respected where the provider meant a specific status (404, 409).
2. **`ScimTypedException`** lets a provider add the RFC 7644 §3.12 `scimType` keyword —
   `invalidPath`, `invalidFilter`, `uniqueness` — so the client learns *which* mistake it made,
   not just that it made one.
3. **The exception filter** (`ScimExceptionFilter` on net10,
   `ScimExceptionFilterAttribute` on net48) is the backstop for an `HttpResponseException`
   thrown outside a handler. Without it, a 404 or 501 signalled by throwing would surface as a
   500. Both filters must map identically — see `docs/scim-conformance.md` §4, X1.
4. **The body** is always a `Core2Error`
   (`urn:ietf:params:scim:api:messages:2.0:Error`) with `status`, `detail` and `scimType`.

On ASP.NET Core, `SuppressModelStateInvalidFilter` and `SuppressMapClientErrors` are on. Without
them `[ApiController]` would rewrite SCIM error bodies into RFC 9110 `ProblemDetails`, which
net48 has no equivalent for.

`Compat/WebApiCompat.cs` vendors `System.Web.Http.HttpResponseException` for the net10 leg so
shared code can throw one type on both TFMs. On net48 the real type comes from
`System.Web.Http.dll`, and the `Compat` folder is excluded from the build.

### 4.4 Authentication

SCIM does not mandate an auth scheme, so the library mandates none: every controller carries a
bare `[Authorize]` and defers to the host's default scheme.

`Anacle.ApiFramework.Authentication` is a reusable helper that targets both TFMs and offers two
modes:

| Mode | ASP.NET Core | ASP.NET Web API 2 / OWIN |
|---|---|---|
| JWT via JWKS | `AddJsonWebKeySetAuthentication(...)` | `UseJsonWebKeySetAuthentication(...)` |
| API key | `AddApiKey(...)` / `AddApiKeyAuthentication(...)` | `UseApiKeyAuthentication(...)` |

- **JWKS.** `JsonWebKeySetRetriever` fetches and caches signing keys from a plain
  `/.well-known/keys` document. OWIN's JWT middleware has no OIDC discovery of its own, so this
  is what makes a real issuer usable on net48.
- **API key.** Default header `X-Api-Key`. `IApiKeyStore` resolves a key to an
  `ApiKeyIdentity`; the supplied `HashedApiKeyStore` holds SHA-256 hashes and compares in fixed
  time.

`SCIM.EduPass` layers a profile on top: `EduPassAuthenticationModes` is a `[Flags]` enum of
`Jwt` and `ApiKey`, and `AddEduPassAuthentication` / `UseEduPassAuthentication` register what
you asked for. With both modes on, ASP.NET Core gets a combined `EduPass` scheme that
dispatches on request shape — API-key header present means the API-key handler, otherwise
bearer. A present-but-invalid API key fails; it does not fall through to a bearer token. OWIN
has no scheme registry, so there both middlewares simply run in order.

The samples deliberately do not use any of this; they wire plain JWT bearer so the dev flow
stays simple. See `docs/edupass-integration.md` §1 for a real setup.

### 4.5 Consuming the library

**On ASP.NET Core**, in `Program.cs`:

```csharp
builder.Services.AddScim(new MyProvider());        // or AddScim<MyUser>(new MyProvider())
...
app.MapScim();
```

`AddScim` registers your provider as a singleton, adds the SCIM controllers as an application
part, and installs the exception filter, the buffering filter and the route convention.
`pathPrefix` (default `scim`) changes the URL segment the endpoints live under. Pass an empty
string to serve them at the application root, for example `AddScim(provider, pathPrefix: "")`.

**On ASP.NET Web API 2**, give SCIM its own `HttpConfiguration`:

```csharp
var services = new ServiceCollection();
services.AddSingleton<IProvider>(new MyProvider());
services.AddLogging(b => b.AddConsole());

var scimConfiguration = new HttpConfiguration();
ScimHttpConfiguration.Configure(scimConfiguration, services.BuildServiceProvider());
app.UseWebApi(scimConfiguration);
```

`ScimHttpConfiguration.Configure` is the counterpart of `AddScim` + `MapScim`. It swaps the
dependency resolver (`ServiceProviderDependencyResolver`, bridging Web API to
`IServiceProvider`), the controller activator and the controller selector, maps attribute
routes only, matches the net10 JSON settings, removes the XML formatter, and adds the exception
filter and buffering handler.

**Adding to an existing Web API app vs. a new one.** Give SCIM its *own* `HttpConfiguration`
rather than sharing `GlobalConfiguration.Configuration`. `Configure` replaces the dependency
resolver, the controller activator and the controller selector, removes the XML formatter and
changes JSON null handling — on a shared configuration all of that would hit your existing
controllers too. Under `Microsoft.Owin.Host.SystemWeb`, a request matching no SCIM route falls
through to your normal `System.Web` handler, so your existing API keeps working.
`Microsoft.SCIM.WebHostSample.IIS` is a worked example; `docs/integration-guide.md` is the
step-by-step.

**The extensibility seams**, in the order you are likely to need them:

| Seam | Use it to |
|---|---|
| `IProvider` / `ProviderBase` | supply your data — the one thing you must write |
| `AddScim<T>` / `ScimHttpConfiguration.Configure<T>` | bind `/Users` to your own User type |
| `ScimUsersController<T>` / `ScimUsersApiController<T>` | derive if you also need to decorate the controller |
| `suppressedControllerTypes` | replace a built-in controller with your own on the same route |
| `pathPrefix` | serve SCIM under another segment, or at the application root with `""` |
| `ScimTypedException` | return a precise `scimType` from your provider |
| `ScimLogging.MaximumBodyLength` / `RedactedHeaders` | tune what failure logs contain |
| `IEduPassStore` | plug storage into `BaseEduPassScimProvider` without rewriting its rules |

### 4.6 Provider implementation

`IProvider` is the contract; `ProviderBase` is the base class you should actually derive from —
it implements discovery (`Schema`, `ResourceTypes`, `Configuration`), bulk fan-out, and returns
501 for anything you have not overridden.

You override the eight operations:

```csharp
Task<Resource>          CreateAsync(IRequest<Resource> request);
Task<Resource>          RetrieveAsync(IRequest<IResourceRetrievalParameters> request);
Task<Resource[]>        QueryAsync(IRequest<IQueryParameters> request);
Task<QueryResponseBase> PaginateQueryAsync(IRequest<IQueryParameters> request);
Task<Resource>          ReplaceAsync(IRequest<Resource> request);
Task                    UpdateAsync(IRequest<IPatch> request);       // PATCH
Task                    DeleteAsync(IRequest<IResourceIdentifier> request);
Task<BulkResponse2>     ProcessAsync(IRequest<BulkRequest2> request);
```

Rules the library cannot enforce for you, and that the handlers expect you to follow:

- Throw `HttpResponseException(NotFound)` for a resource that is not there — do not return null.
- Throw `NotImplementedException` for an operation you do not offer; it becomes a 501, not a 500.
- Make PATCH atomic. A multi-operation PATCH either lands whole or not at all.
- Keep users and groups consistent in both directions if you project `groups` onto users.

`BaseEduPassScimProvider` shows the pattern for a profile with real obligations: it holds the
rules (membership lives on the group, deleting a group strips that role from everyone, an
unresolvable member is refused) and delegates *storage only* to an `IEduPassStore`. Swap the
store for a database and the rules come along unchanged. `InMemoryEduPassProvider` is the
in-memory store; `InMemoryUserProvider` / `InMemoryGroupProvider` in the sample are the plain
non-profile equivalents.

Those two store **domain entities rather than SCIM resources**: `UserEntity` and `GroupEntity`
in `Microsoft.SCIM.WebHostSample/Domain`, with `ScimUserMapper` / `ScimGroupMapper` translating
by hand at the edge. That is the arrangement a database-backed relying party needs — the wire
format and the stored model change independently — and it is what the sample demonstrates for
you to copy. See `docs/integration-guide.md` step 2.

### 4.7 Schema extensions

To add attributes to `/Users`:

1. Derive from `Core2EnterpriseUser` (it is unsealed) and add a `[DataMember]` for your
   extension object — see `EduPassUser`.
2. Declare your schema URN and advertise it from `/Schemas` and `/ResourceTypes` — see
   `EduPassSchemaIdentifiers` and `EduPassTypeSchemes`. Advertise only what you actually store,
   from the same flag your validation reads, so the two cannot drift.
3. Register with `AddScim<MyUser>(provider)` (or `Configure<MyUser>`). That closes
   `ScimUsersController<T>` over your type and suppresses the built-in `UsersController`, which
   would otherwise contend for the same route. You write no controller.

A controller's generic parameter is its model-binding type (`[FromBody] T`), which is why a
custom User type needs a controller closed over it — and why the library provides one rather
than making you restate the routes and verbs.

`SchematizedJsonConverter` carries schema extensions the service was never compiled against, in
both directions, so an untyped extension is not silently dropped.

### 4.8 Configuration and DI

Both legs use `Microsoft.Extensions.*` for configuration and DI, including on net48.

- `appsettings.json` layered with `appsettings.{ASPNETCORE_ENVIRONMENT}.json`, then environment
  variables. All three samples read the same shape, including the `Logging` section.
- Under IIS, build the configuration from `HttpRuntime.BinDirectory`, not
  `AppDomain.CurrentDomain.BaseDirectory` — ASP.NET's base directory is the application root,
  while `appsettings.json` is dropped into `bin\`. Getting this wrong loads no configuration at
  all.
- On net48, `ServiceProviderDependencyResolver` bridges Web API's `IDependencyResolver` to
  `IServiceProvider`, and `ScimControllerActivator` builds controllers per request with
  `ActivatorUtilities` — so SCIM controllers take constructor dependencies exactly as they do
  on ASP.NET Core.
- `ScimPath` (route prefix) and `ScimLogging` (log limits) are process-wide statics rather than
  injected, because the request handlers run with no container in scope.

### 4.9 Testing

Black-box HTTP against live hosts, in `tests/integration/` — Vitest, TypeScript, `fetch`, no
mocks. Most of what matters here (status codes, headers, connection behaviour, serialisation)
cannot be observed any other way.

```bash
pnpm install
pnpm test              # .NET 10 leg
pnpm run test:net48    # net48 leg
pnpm run test:coverage # with dotnet-coverage
```

The suite starts **five hosts**, not one, because a provider is a singleton chosen at startup
and each behaviour needs its own: the normal in-memory provider, EduPass, EduPass with UIN/FIN
required, an `UnimplementedProvider` (proves unimplemented is 501, not 500), and a
`FaultyProvider` (proves a provider fault becomes a SCIM error body, not a stack trace). Every
suite runs against both legs, which is how the two are kept identical.

`tests/integration/README.md` has the details. Coverage counts only code a request can actually
reach.

## 5. Documentation

| Document | What it covers |
|---|---|
| [`docs/integration-guide.md`](docs/integration-guide.md) | **Adding SCIM to an existing ASP.NET 4.8 Web API** — step by step, with the IIS fixes |
| [`docs/edupass-integration.md`](docs/edupass-integration.md) | The EduPass profile: auth, the User extension, provider obligations |
| [`docs/net48-hosting.md`](docs/net48-hosting.md) | Hosting on .NET Framework: TLS, IIS, signing, the compat shim |
| [`docs/scim-conformance.md`](docs/scim-conformance.md) | The RFC-derived spec both legs are verified against |
| [`tests/integration/README.md`](tests/integration/README.md) | The test suite and its five hosts |

## 6. Changes from the Microsoft reference code

Forked at upstream commit `70d3f4a`. The largest changes, by theme:

**Multi-targeting and hosting**
- The library multi-targets `net48` and `net10.0`; it was ASP.NET Core only.
- Controllers moved out of the library into two hosting projects,
  `Microsoft.SCIM.AspNetCore` and `Microsoft.SCIM.AspNet`.
- All orchestration moved into shared `ScimRequestHandler<T>` /
  `ScimDiscoveryRequestHandler`, so both legs behave identically.
- `Compat/WebApiCompat.cs` replaces the discontinued `WebApiCompatShim`.
- Two new samples: OWIN self-host (`…Net48`) and an existing-IIS-app integration (`…IIS`).

**Logging rewritten**
- `IMonitor`, the `Notification` hierarchy, its factories and `MonitoringMiddleware` are gone.
- Replaced by `ILogger`-based failure logging: `ScimLogging`, `ScimLoggerExtensions`,
  `ScimEventIds`. The host's logging configuration is used as-is.
- A failed operation now logs the request that caused it, with credential headers redacted and
  the body length bounded.

**Error handling**
- A provider fault becomes a SCIM error body instead of being rethrown as a 500 stack trace.
- `ScimTypedException` carries the RFC 7644 `scimType` keyword to the response.
- `ScimResult` replaces per-controller response building; `ProblemDetails` rewriting suppressed
  on ASP.NET Core for parity with net48.

**SCIM conformance fixes** (each verified by the integration suite)
- PATCH: many defects fixed — remove no longer writes the value it was told to remove, remove
  without a value on multi-valued attributes, `ims` handling, extension attributes, atomicity.
- Filters: all nine operators answered by the reference provider; unbalanced brackets rejected;
  a full filter-grammar suite.
- Bulk: six defects fixed.
- Robustness: non-numeric `page` rejected; HEAD answers 405 instead of closing the connection;
  store corruption under concurrent writes fixed; parser recursion and input length capped.
- Groups carry extensions; `name.middleName` supported.

**Security**
- The anonymous `/scim/token` issuer endpoints were removed from both samples — a reference
  implementation should not teach that.
- Dev-mode JWT bypasses are behind `#if DEBUG` and announce themselves at startup.
- `Newtonsoft.Json` and `Microsoft.Owin` pinned against known advisories.

**New projects**
- `Anacle.ApiFramework.Authentication` — JWKS and API-key auth for both legs.
- `SCIM.EduPass` — the EduPass profile: schema, validator, provider rules, storage interface,
  authentication.

**Tests and docs**
- A Vitest integration suite (~16 spec files) running against five provider hosts on both legs,
  with `dotnet-coverage` collection and CSV test-plan output.
- `docs/` added: conformance spec, net48 hosting notes, and two integration guides.
- CI builds the whole solution on `windows-latest`.

## License

MIT, inherited from the Microsoft reference code. See [`LICENSE`](LICENSE),
[`CODE_OF_CONDUCT.md`](CODE_OF_CONDUCT.md) and [`SECURITY.md`](SECURITY.md).
