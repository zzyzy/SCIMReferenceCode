# Multi-Targeting Plan: .NET Framework 4.8 (ASP.NET Web API) + .NET 10.0 (ASP.NET Core Web API)

**Status:** Approved — revision 3. All open items closed; no outstanding decisions.
**Date:** 2026-08-05
**Branch:** `dev-lzz-master-target-net48-aspnet-webapi`
**Baseline commit:** `70d3f4a`

---

## 1. Goal

Ship the SCIM reference library and sample so that they run on **both**:

| Leg | TFM | Hosting stack |
|---|---|---|
| Framework | `net48` | ASP.NET Web API 2 (`System.Web.Http`), OWIN self-host |
| Core | `net10.0` | ASP.NET Core Web API (`Microsoft.AspNetCore.Mvc`) |

Both legs must expose **identical SCIM wire behaviour**: same routes, same status codes, same JSON bodies, same headers.

`netcoreapp3.1` is dropped. This change therefore bundles a **3.1 → 10.0 upgrade** with the net48 addition — the repo is not currently on a supported .NET version.

---

## 2. Baseline: what the code looks like today

Verified against the working tree at `70d3f4a`.

### 2.1 Projects

```
Microsoft.SCIM.sln
├── Microsoft.SystemForCrossDomainIdentityManagement/Microsoft.SCIM.csproj   (netcoreapp3.1, Sdk)
└── Microsoft.SCIM.WebHostSample/Microsoft.SCIM.WebHostSample.csproj         (netcoreapp3.1, Sdk.Web)
```

No test projects. No `launchSettings.json`. No strong-name signing (`no .snk`, no `SignAssembly`). No NuGet packaging (`no .nuspec`, no `GeneratePackageOnBuild`, no publish workflow). `Microsoft.SCIM.LogicAppValidationTemplate/` is PowerShell + Logic App JSON only.

### 2.2 The critical dependency: `Microsoft.AspNetCore.Mvc.WebApiCompatShim` 2.2.0

This single package is the linchpin of the whole port. It currently supplies:

| Type used | Where |
|---|---|
| `System.Web.Http.HttpResponseException` | thrown/caught throughout `Service/`, `Service/Controllers/`, and both `InMemory*Provider` files |
| `System.Web.Http.FromUriAttribute` | `ControllerTemplate.cs:288` |
| `HttpRequestMessageFeature` | `ControllerTemplate.ConvertRequest()`, `ControllerTemplate.cs:55-60` |
| (transitively) `Microsoft.AspNet.WebApi.Client` 5.2.6 → `System.Net.Http.Formatting` | `SchematizedMediaTypeFormatter`, `ProtocolExtensions` (`JsonMediaTypeFormatter`) |

The package ships **netstandard2.0 only** and pulls `Microsoft.AspNetCore.Mvc.Core` **2.2.x**, which conflicts with the ASP.NET Core 10 shared framework. It is unsupported and unusable on .NET 10. **It must be replaced.**

It also installed an **exception filter** that converted `HttpResponseException` into its status code. Nothing in this repo does that job explicitly. See R2 — this is the sharpest risk in the port.

### 2.3 Favourable finding: the domain layer is already classic-Web-API-shaped

The provider/extension contracts are typed on `System.Net.Http.HttpRequestMessage`, not on `HttpContext`:

- `IProvider` / `IProviderAdapter<T>` / `ProviderAdapterTemplate` / `Core2*ProviderAdapter`
- `IExtension.Supports(HttpRequestMessage)` — `Protocol/IExtension.cs:18`
- `BulkRequest`, `CreationRequest`, `QueryRequest`, `RetrievalParameters`, … (`SystemForCrossDomainIdentityManagementRequest`)
- `ProtocolExtensions` — ~10 `Compose*Request` methods returning `HttpRequestMessage`

Providers signal failure by throwing `System.Web.Http.HttpResponseException` (e.g. `InMemoryUserProvider.cs:226,244,254,295,338`).

**Consequence:** the shared layer is natively net48-compatible. The work concentrates on the **.NET 10 side** (replace the shim) and the **controller layer** (two MVC frameworks) — not on the SCIM protocol/schema logic, which is ~132 files that need no changes at all.

### 2.4 ASP.NET Core coupling is narrow

Only 12 files reference `Microsoft.AspNetCore.*`:

| File | Coupling | Disposition |
|---|---|---|
| `Service/Controllers/*.cs` (8 files, 1280 lines) | `ControllerBase`, `IActionResult`, `ActionResult<T>`, `[ApiController]`, `[Route]`, `[Authorize]`, `[FromBody]` | **Split per host** |
| `Service/IProvider.cs:803` | `using Microsoft.AspNetCore.Builder;` | **Dead** — used only by a commented-out `StartupBehavior` member (line 815). Delete the `using`. |
| `WebHostSample/Startup.cs`, `Program.cs`, `Controllers/TokenController.cs` | host wiring | Per-sample |

### 2.5 Cross-target-safe APIs (no work needed)

| API | net48 | net10.0 |
|---|---|---|
| `System.Web.HttpUtility.ParseQueryString` / `UrlEncode` / `UrlDecode` — `Query.cs:68,97`, `Filter.cs`, `ProtocolExtensions.cs:1597`, `IReadOnlyCollectionExtensions.cs:20` | `System.Web.dll` (explicit `<Reference>`) | `System.Web.HttpUtility.dll`, in the shared framework |
| `System.Net.Http.Formatting` (`MediaTypeFormatter`, `JsonMediaTypeFormatter`) | `Microsoft.AspNet.WebApi.Client` | same package (netstandard2.0) |
| `System.Configuration.ConfigurationManager` — `ConfigurationSectionFactory.cs:44` | framework or package | package |
| `Microsoft.Extensions.*` (DI, Configuration) | packages | shared framework |
| `Newtonsoft.Json` | package | package |
| `System.IdentityModel.Tokens.Jwt` 8.x | `net462` asset | `netstandard2.0`/`net8.0` asset |

`HttpUtility.ParseQueryString` returns a writable `HttpValueCollection` whose `ToString()` URL-encodes — there is no `WebUtility` equivalent. **Keep `HttpUtility`; do not "modernize" those four files.**

### 2.6 Dead code (verified by solution-wide reference search)

| Item | Finding |
|---|---|
| `Service/HttpResponseExceptionFactory.cs` | `internal`, **never called**. Also buggy: `CreateException` assigns `result = null` before `return result`, so it always returns null, and the `finally` disposes the `HttpResponseMessage` the exception would own. |
| `Service/HttpStringResponseExceptionFactory.cs` | `internal`, never called |
| `Service/HttpResponseMessageFactory.cs` | `internal`, referenced only by the two above |
| `Service/HttpStringResponseMessageFactory.cs` | `internal`, referenced only by the above |
| `Service/SchematizedMediaTypeFormatter.cs` | **`public`**, only self-references |
| `Service/SampleProvider.cs`, `Service/ISampleProvider.cs` | **`public`**, never referenced (the sample uses its own `InMemoryProvider`) |
| `ProtocolExtensions.SerializeAsync` (both overloads) | **`public`**, no callers — the two overloads only call each other. Retained (not part of D13), but see §2.9: it is the only code in Core that reads `HttpRequestMessage.Content`. |
| `Service/MonitoringMiddleware.cs` | 100% commented out. Complete OWIN `OwinMiddleware` implementation — i.e. exactly the net48 middleware this port needs. **Revive.** |
| `Service/DependencyResolverDecorator.cs` | 100% commented out `System.Web.Http.Dependencies.IDependencyResolver`. **Delete**, superseded by D9. |
| `PackageReference Json.Net 1.0.18` | Never referenced by any `.cs` file |
| `Microsoft.AspNetCore.Identity.UI` 3.1.0, `Microsoft.AspNetCore.Server.Kestrel` 2.2.0 | Unused |
| `Microsoft.CodeAnalysis.FxCopAnalyzers` 2.9.8 | Deprecated by Microsoft |
| `.config/dotnet-tools.json` → `dotnet-ef` 3.1.2 | No EF usage anywhere |

Deleting `SchematizedMediaTypeFormatter` does **not** remove the `Microsoft.AspNet.WebApi.Client` dependency — `ProtocolExtensions` uses `JsonMediaTypeFormatter` in four `Compose*Request` methods.

### 2.7 Behavioural quirks in the controller layer

1. **`ConfigureResponse` sets 201 Created unconditionally** (`ControllerTemplate.cs:34`), including on the `Put` path (line 682) which then returns `Ok(result)`. The on-the-wire status is dependent on ASP.NET Core result-execution ordering. RFC 7644 §3.5.1 requires 200 for a successful PUT replace. **See D15.**
2. **`Post` writes `Location` twice** — manually in `ConfigureResponse` (line 51) and again via `CreatedAtAction(nameof(Post), result)` (line 580), which derives a URI from MVC routing. `CreatedAtAction` has no Web API equivalent producing the same URI. **See D15.**
3. **`RootController` has no `[Route]` and no `[ApiController]`** (`Service/Controllers/RootController.cs`). It is reachable only via `MapDefaultControllerRoute()`, i.e. `/Root/{action}`. Web API's default route is `api/{controller}/{id}`, so conventional routing will not port identically. **Resolved by D14a — the route becomes `/scim`.**
4. `SchemasController.Get()`, `ResourceTypesController.Get()`, and `ServiceProviderConfigurationController.Get()` return domain types directly (`QueryResponseBase`, `ServiceConfigurationBase`) rather than an action result, and throw `HttpResponseException` on failure — so their error responses depend entirely on the exception filter of R2.

### 2.8 Security posture of the sample (context for D18/D19)

Three things interlock, and all three are already documented in-code:

- `Startup.cs` — `#if DEBUG` + `IsDevelopment()` branch disabling **all** JWT validation (issuer, audience, lifetime, signing key).
- `Controllers/TokenController.cs` — anonymously reachable token issuer, marked `[Obsolete(..., error: true)]` with a "DO NOT USE IN PRODUCTION" header.
- `appsettings.Development.json` — committed symmetric signing key `A1B2C3D4E5F6A1B2C3D4E5F6`, issuer/audience `Microsoft.Security.Bearer`.

The port must not weaken this, and must extend the same guards to the new net48 sample.

`README.md:77` already documents all three. That note must be extended to name the net48 sample too (Phase 8).

### 2.9 Request-body reads in Core (resolved — was O3)

Traced exhaustively. Core touches `HttpRequestMessage.Content` in three places:

| Site | Direction | Verdict |
|---|---|---|
| `ProtocolExtensions.cs:454,509,565,621` (`ComposePatch/Put/PostRequest`) | **sets** Content on outbound requests Core constructs itself | irrelevant to inbound conversion |
| `ProtocolExtensions.cs:1631-1633` (`HttpRequestMessageWriter`) | **reads** Content | reachable only from `SerializeAsync`, which has **no callers** (§2.6) |
| `HttpResponseMessageFactory.cs:22` | sets Content | deleted by D13 |

**Conclusion:** nothing in the request pipeline reads the body from `HttpRequestMessage`. MVC model binding already produces the typed body, and the shared code only reads URI/headers/method (`ResourceQuery(request.RequestUri)`, `TryGetRequestIdentifier`, `IExtension.Supports`). **`HttpContextRequestConverter` does not need to buffer the request body.**

**Residual caveat:** `SerializeAsync` is `public`. An external consumer calling it on an inbound request would, on the net10 leg, receive a serialization with an empty body line, because the converted `HttpRequestMessage` carries no Content. Accepted under D13a. If this ever matters, the fix is `HttpContext.Request.EnableBuffering()` plus copying the body into the converted message — localized to `HttpContextRequestConverter`.

---

## 3. Decisions

### 3.1 Round 1 — structure

| # | Decision | Rationale |
|---|---|---|
| D1 | **Three projects: `Microsoft.SCIM.Core` + `Microsoft.SCIM.AspNet` + `Microsoft.SCIM.AspNetCore`** | Explicit layering; no `#if` in the 761-line `ControllerTemplate`. |
| D2 | **`Microsoft.SCIM.Core` multi-targets `net48;net10.0`** (not netstandard2.0) | Core itself needs `HttpResponseException`. A netstandard2.0 Core would have to vendor that type unconditionally, colliding with the real `System.Web.Http.dll` on the net48 leg. Multi-targeting Core lets net48 use the genuine Web API type and net10.0 use the vendored shim. |
| D3 | **Vendor a minimal `System.Web.Http` shim, compiled for `net10.0` only** | Zero source changes to `IProvider`, `IExtension`, throw/catch sites, or any consumer's provider implementation. Cost: namespace squatting, confined to one file. |
| D4 | **`AssemblyName`/`RootNamespace` of Core stay `Microsoft.SCIM`** | Preserves the shipping assembly identity, the namespace, and the `InternalsVisibleTo` grant to `Microsoft.Graph.Provisioning` (`Service/Friends.cs`). |
| D5 | **Newtonsoft.Json on both legs, direct reference, bumped to 13.0.3** | Identical serialization is a hard requirement for parity. 12.0.2 has known advisories. No changes to the ~12 serialization files. |
| D6 | **Sample split**: existing sample stays `net10.0` (`Sdk.Web`); new `Microsoft.SCIM.WebHostSample.Net48` is an OWIN self-host console app, sharing `Provider/` and `Resources/` via linked `<Compile>` | Runnable with F5, no IIS dependency, provider logic single-sourced. |
| D7 | **net48 auth: OWIN JWT bearer middleware** (`Microsoft.Owin.Security.Jwt`) | Standard net48 pattern; makes `[Authorize]` work via the OWIN identity. |
| D8 | **Configuration: `appsettings.json` on both legs** via `Microsoft.Extensions.Configuration.Json` | Works on net48. One config file, one key schema, one set of docs. |
| D9 | **net48 DI: bridge MEDI → `System.Web.Http.Dependencies.IDependencyResolver`** | One DI model across both legs; registration code identical in both samples. |
| D10 | **Minimal modernization only** — Core sample moves to `WebApplicationBuilder`; obsolete refs dropped; no nullable rollout, no STJ migration, no records | Keeps the diff reviewable. |
| D11 | **`Directory.Build.props` + `windows-latest` CI; `EnableNETAnalyzers` replaces FxCopAnalyzers; `TreatWarningsAsErrors=false`** | The .NET 10 analyzer set will surface a large volume of findings; blocking the port on them is not worth it. |

### 3.2 Round 2 — parity and cleanup

| # | Decision | Rationale |
|---|---|---|
| D12 | **Extract `ScimRequestHandler<T>` + `ScimResult` into Core.** `ControllerTemplate`'s orchestration, exception→status mapping, and monitor calls move to Core verbatim; each hosting project gets ~80 lines of result mapping. | With no automated tests, duplicating 761 lines across two assemblies guarantees drift. This makes parity structural rather than something a checklist has to catch. |
| D13 | **Delete all six dead types**: the four-class exception/message-factory cluster, `SchematizedMediaTypeFormatter`, `SampleProvider`/`ISampleProvider`. Remove the two `ServiceNotificationIdentifiers.SchematizedMediaTypeFormatter*` constants with the class. | Smallest surface to port and maintain across two stacks; removes the always-null bug rather than fixing uncalled code. |
| D13a | **Treat the `InternalsVisibleTo` grant to `Microsoft.Graph.Provisioning` as best-effort.** Execute D13 without waiting on that team; keep `Service/Friends.cs` so the grant still compiles; flag the possible break in PR 2's description. | The README states this repo is a reference sample with no guarantee of active maintenance or support, and nothing in it tracks the Graph Provisioning relationship. Blocking a port on an external team's response time is disproportionate to the risk. Same reasoning covers the `SerializeAsync` caveat in §2.9. |
| D14 | **Leave the library unsigned**; document as a known net48 limitation. | Matches today's behaviour exactly. Introducing signing mid-port adds key management and a class of load-time failures. |
| D15 | **Fix PUT→200 and POST→201 deliberately.** Encode both in `ScimResult`; delete `ConfigureResponse`'s unconditional 201; drop `CreatedAtAction` in favour of an explicit `Location` from `GetBaseResourceIdentifier()`/`GetResourceIdentifier()`. | RFC 7644 §3.5.1 (PUT→200) and §3.3 (POST→201+Location). Removes the ordering dependence, which has no net48 equivalent to copy. Recorded as an intentional behaviour change. |
| D16 | **No NuGet packages.** Set `IsPackable=false`/`GeneratePackageOnBuild=false` explicitly. | No packaging exists today; this is a reference sample consumed by cloning. Settles the question rather than leaving it latent. |
| D17 | **Reuse `ASPNETCORE_ENVIRONMENT` on both samples**, layering `appsettings.{env}.json` over `appsettings.json`. | One variable name, identical docs and CI scripts across legs. Slightly misleading on a host containing no ASP.NET Core; familiarity wins. |

### 3.3 Round 3 — verification, dependencies, delivery

| # | Decision | Rationale |
|---|---|---|
| D18 | **No `netcoreapp3.1` baseline capture. Derive expected behaviour from RFC 7644/7643**, the two Postman collections' existing assertions, and cross-host diffing. | Chosen scope. Removes the 3.1-SDK/Windows prerequisite from the critical path. **This substantially changes the verification model and raises R1 — see §9 and §10.** |
| D19 | **JWT: unify on `System.IdentityModel.Tokens.Jwt` 8.x across both TFMs**, with `Microsoft.IdentityModel.JsonWebTokens` 8.x as an explicit direct reference to pin the unification. | Both target `net462`+`netstandard2.0`, so one version serves both legs. See §7.1 — a full move *off* `System.IdentityModel.Tokens.Jwt` onto `Microsoft.IdentityModel.JsonWebTokens` alone is **out of scope**: `JwtHeader`/`JwtPayload`/`JwtSecurityToken` have no equivalents there, and Core's EventToken cluster plus the public `IEventToken.Header` property depend on them. |
| D20 | **Both samples are HTTP-only dev harnesses.** Remove `UseHsts()`/`UseHttpsRedirection()` from the Core sample so the two legs match. | Chosen for symmetry, so the parity comparison is like-for-like. **This weakens a sample that people copy into production SCIM endpoints** — mitigated by D21, a startup banner in both hosts, and an explicit "TLS is the host's responsibility" section in `docs/net48-hosting.md`. |
| D21 | **Keep the committed dev signing key; strengthen the warnings.** Add a `_comment` next to `IssuerSigningKey` in `appsettings.Development.json`, and print a DEV-ONLY banner at startup in both samples. | The value is an obvious dummy in a file named `Development`; removing it breaks one-command local startup for everyone following the wiki. Carries D20's TLS warning in the same banner. |
| D22 | **Stacked PRs, one per phase, onto a long-lived integration branch.** | Each PR is independently buildable and reviewable; the Phase 2 net48 build gates all controller work. Merge to `master` once Phase 9 passes. |

### 3.4 Round 4 — closing the open list

| # | Decision | Rationale |
|---|---|---|
| D14a | **Confirmed: `RootController` gets an explicit `[Route("scim")]` on both legs.** Both legs use attribute routing exclusively — no conventional-route fallback. | `SchemaConstants.PathInterface` is already `"scim"`, so `/scim` is the natural service root and is consistent with `scim/Users`, `scim/Groups`, `scim/Schemas`. The current `/Root/{action}` is an accident of `MapDefaultControllerRoute()`. Supersedes the earlier "preserve exactly" answer, which D18 made unknowable. |
| D23 | **`Version` / `AssemblyVersion` / `FileVersion` = 2.0.0.** | The assembly gains a new TFM set, loses public types (D13), and changes PUT/POST status codes (D15). A major bump is the honest signal even without packaging (D16), and costs nothing since there is nothing to stay compatible with. |
| D24 | **No `FromUriAttribute` shim.** `Compat/` contains only `HttpResponseException`. | Exactly one use site (`ControllerTemplate.cs:287`), rewritten by Phase 3/4 regardless: `[FromRoute]` on net10, native `[FromUri]` on net48. |
| D25 | **Automated tests remain out of scope; R1 is a closed, accepted risk rather than an open question.** | Scope set by D12/D18. §10's four oracles each declare their blind spots, and D12's single-sourced handler minimises what can diverge. |

---

## 4. Consequences of D18 that need attention

Dropping the baseline capture is the single most consequential decision in this revision, and it collides with D14's sibling answer about `RootController`.

**The collision:** "preserve `RootController`'s current route exactly" (round 2) is **not achievable** without a baseline (D18) — with no capture, what "exactly" means is unknowable, and its route today is an accident of `MapDefaultControllerRoute()`. The plan resolves this the only way consistent with D18:

> **D14a — `RootController` gets an explicit `[Route("scim")]` on both legs**, derived from the RFC-driven approach of D18 rather than from observed behaviour. Both legs then use attribute routing exclusively — `MapControllers()` / `MapHttpAttributeRoutes()`, no conventional-routing fallback — which removes the largest single source of route drift between the two frameworks.

**Confirmed** (§3.4). `SchemaConstants.PathInterface` is already `"scim"`, so this makes the service root consistent with every sibling route rather than inventing one.

**The verification model changes shape.** With no "before" recording, §10 rests on four oracles instead of one:

1. **RFC 7644/7643 conformance** — an explicit table of endpoint → required status/headers/body shape, checked per leg.
2. **The two Postman collections' existing assertions** — already in the repo; they encode real expectations and must pass on both hosts.
3. **Cross-host byte diff** — net48 vs net10 responses compared against *each other*. Still fully available and now the primary parity oracle. It cannot detect a fault present in both legs, only divergence.
4. **Logic App validation templates** — the closest thing to an Entra-realistic end-to-end check.

**What is lost:** the ability to detect a behaviour the current build has that both new legs get wrong in the same way. Oracle 3 is blind to that by construction, and oracles 1/2/4 only cover what they assert. This is the residual exposure of D18 + no tests, and it is accepted knowingly.

---

## 5. Target architecture

```
Microsoft.SCIM.sln
│
├── Directory.Build.props                         shared TFMs, LangVersion, analyzers, IsPackable=false
│
├── Microsoft.SystemForCrossDomainIdentityManagement/     → Microsoft.SCIM.Core.csproj
│   │   TargetFrameworks: net48;net10.0
│   │   AssemblyName / RootNamespace: Microsoft.SCIM      (unchanged, D4)
│   ├── Schemas/          shared, unchanged            (74 files)
│   ├── Protocol/         shared, unchanged            (58 files)
│   ├── Service/          shared, minus Controllers/
│   ├── Service/ScimResult.cs           NEW  (Phase 3, D12)
│   ├── Service/ScimRequestHandler.cs   NEW  (Phase 3, D12)
│   ├── Service/Friends.cs              InternalsVisibleTo stays here (D4)
│   └── Compat/WebApiCompat.cs          NEW, net10.0 only (D3)
│
├── Microsoft.SCIM.AspNet/                        NEW — net48
│   ├── Controllers/                              ApiController-based, thin
│   ├── ScimHttpConfiguration.cs                  routes, formatters, exception filter
│   ├── ServiceProviderDependencyResolver.cs      MEDI → IDependencyResolver (D9)
│   └── MonitoringMiddleware.cs                   revived OWIN middleware (§2.6)
│
├── Microsoft.SCIM.AspNetCore/                    NEW — net10.0
│   ├── Controllers/                              ControllerBase-based, thin
│   ├── HttpContextRequestConverter.cs            HttpContext → HttpRequestMessage
│   ├── ScimExceptionFilter.cs                    HttpResponseException → response (R2)
│   └── ScimServiceCollectionExtensions.cs        AddScim() / MapScim()
│
├── Microsoft.SCIM.WebHostSample/                 net10.0, Sdk.Web  (existing, upgraded)
└── Microsoft.SCIM.WebHostSample.Net48/           NEW — net48, Sdk, Exe, OWIN self-host
```

### 5.1 Reference graph

```
WebHostSample (net10.0) ──► Microsoft.SCIM.AspNetCore (net10.0) ──► Microsoft.SCIM.Core [net10.0]
WebHostSample.Net48      ──► Microsoft.SCIM.AspNet     (net48)   ──► Microsoft.SCIM.Core [net48]
```

`Microsoft.SCIM.Core` references **no** `Microsoft.AspNetCore.*` package. Verify by building its `net48` leg — any ASP.NET Core leak fails the compile immediately. **That build is the architectural guard rail, and it is the cheapest check in the whole plan.**

---

## 6. Detailed work

### Phase 0 — Gates and specification (replaces baseline capture)

D18 removes the capture step but not the need for an oracle. There are **no blocking gates** — D13a settled the only one. Before code changes:

1. **Write `docs/scim-conformance.md`** — the RFC-derived specification table that replaces the baseline. One row per endpoint × case: HTTP verb, path, precondition, expected status, expected headers, expected body shape, RFC citation. Derive from RFC 7644 (protocol) and RFC 7643 (schema), cross-checked against the assertions already present in `PostmanCollection.json` and `SCIM Inbound.postman_collection.json`. **This document is the acceptance criterion for Phases 4–7.**
2. **Inventory the Postman assertions** — enumerate what the two collections actually assert today, so §10 can state coverage honestly rather than implying the collections are exhaustive.
3. **Record the D15 and D14a behaviour changes** in `docs/scim-conformance.md` as intentional deviations from the current build, with RFC citations.

### Phase 1 — Build infrastructure

`Directory.Build.props` at the repo root:

```xml
<Project>
  <PropertyGroup>
    <LangVersion>latest</LangVersion>
    <NeutralLanguage>en</NeutralLanguage>
    <EnableNETAnalyzers>true</EnableNETAnalyzers>
    <AnalysisLevel>latest</AnalysisLevel>
    <TreatWarningsAsErrors>false</TreatWarningsAsErrors>
    <GenerateDocumentationFile>false</GenerateDocumentationFile>
    <IsPackable>false</IsPackable>                      <!-- D16 -->
    <GeneratePackageOnBuild>false</GeneratePackageOnBuild>
    <Version>2.0.0</Version>                            <!-- D23 -->
    <AssemblyVersion>2.0.0.0</AssemblyVersion>
    <FileVersion>2.0.0.0</FileVersion>
  </PropertyGroup>
</Project>
```

Also: clean the committed `obj/`/`bin/` artefacts for `netcoreapp3.1` currently in the working tree, and confirm `.gitignore` covers them.

### Phase 2 — Convert the library to multi-targeted `Microsoft.SCIM.Core`

Rename `Microsoft.SCIM.csproj` → `Microsoft.SCIM.Core.csproj`, keeping the directory name to preserve git history and the `.resx`/designer wiring.

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFrameworks>net48;net10.0</TargetFrameworks>
    <AssemblyName>Microsoft.SCIM</AssemblyName>
    <RootNamespace>Microsoft.SCIM</RootNamespace>
  </PropertyGroup>

  <!-- Shared -->
  <ItemGroup>
    <PackageReference Include="Newtonsoft.Json" Version="13.0.3" />
    <PackageReference Include="Microsoft.AspNet.WebApi.Client" Version="5.2.9" />
    <PackageReference Include="System.Configuration.ConfigurationManager" Version="10.0.0" />
    <PackageReference Include="System.IdentityModel.Tokens.Jwt" Version="8.*" />          <!-- D19 -->
    <PackageReference Include="Microsoft.IdentityModel.JsonWebTokens" Version="8.*" />    <!-- D19 -->
    <PackageReference Include="Microsoft.Extensions.DependencyInjection.Abstractions" Version="10.0.0" />
  </ItemGroup>

  <!-- net48 only: real Web API types + GAC assemblies -->
  <ItemGroup Condition="'$(TargetFramework)' == 'net48'">
    <PackageReference Include="Microsoft.AspNet.WebApi.Core" Version="5.2.9" />
    <Reference Include="System.Web" />
    <Reference Include="System.Net.Http" />
    <Reference Include="System.Configuration" />
  </ItemGroup>

  <!-- net10.0 only: vendored System.Web.Http shim (D3) -->
  <ItemGroup Condition="'$(TargetFramework)' == 'net48'">
    <Compile Remove="Compat\**" />
  </ItemGroup>

  <ItemGroup>
    <Compile Remove="Service\Controllers\**" />         <!-- moves to the hosting projects -->
  </ItemGroup>

  <!-- existing .resx / Designer.cs ItemGroups: keep verbatim -->
</Project>
```

Edits inside Core:

1. `Service/IProvider.cs` — delete `using Microsoft.AspNetCore.Builder;` (§2.4). Then confirm zero `Microsoft.AspNetCore.*` references remain in the shared tree.
2. Delete `Service/DependencyResolverDecorator.cs`.
3. Execute D13 (unblocked by D13a): delete `HttpResponseExceptionFactory.cs`, `HttpStringResponseExceptionFactory.cs`, `HttpResponseMessageFactory.cs`, `HttpStringResponseMessageFactory.cs`, `SchematizedMediaTypeFormatter.cs`, `SampleProvider.cs`, `ISampleProvider.cs`, plus the two `ServiceNotificationIdentifiers.SchematizedMediaTypeFormatter*` constants. Keep `Service/Friends.cs`. Note the possible `Microsoft.Graph.Provisioning` break in PR 2's description.
4. Keep `Service/MonitoringMiddleware.cs` commented out here; it moves in Phase 5.
5. Remove package references: `Json.Net`, `Microsoft.AspNetCore.Identity.UI`, `Microsoft.AspNetCore.Server.Kestrel`, `Microsoft.AspNetCore.Mvc.NewtonsoftJson`, `Microsoft.AspNetCore.Authentication.JwtBearer`, `Microsoft.AspNetCore.Mvc.WebApiCompatShim`, `Microsoft.CodeAnalysis.FxCopAnalyzers`.
6. **Verify the EventToken cluster compiles against IdentityModel 8.x on both legs** (D19, §7.1). `EventToken.cs` uses `JwtHeader`, `JwtPayload`, `JwtSecurityToken(string)`, `JwtSecurityToken(JwtHeader, JwtPayload)`, `JwtSecurityTokenHandler`; `IEventToken.Header` is `JwtHeader`. The 5.x→8.x jump is three majors — expect at minimum obsoletion warnings around `JwtSecurityTokenHandler`.
7. Verify the three `.resx` + `Designer.cs` pairs still generate for both TFMs, preserving that `ServiceResources` uses `PublicResXFileCodeGenerator` while the other two use `ResXFileCodeGenerator`.

`Compat/WebApiCompat.cs` (net10.0 only) — the minimum surface the shared code and consumer providers require:

```csharp
// Compiled for net10.0 only. On net48 this type comes from System.Web.Http.dll
// (Microsoft.AspNet.WebApi.Core). It replaces the discontinued
// Microsoft.AspNetCore.Mvc.WebApiCompatShim. See MULTI-TARGET-PLAN.md D3.
namespace System.Web.Http
{
    using System;
    using System.Net;
    using System.Net.Http;

    public class HttpResponseException : Exception
    {
        public HttpResponseException(HttpStatusCode statusCode)
            : this(new HttpResponseMessage(statusCode)) { }

        public HttpResponseException(HttpResponseMessage response) =>
            this.Response = response ?? throw new ArgumentNullException(nameof(response));

        public HttpResponseMessage Response { get; }
    }
}
```

`Compat/` contains **only** this type. There is no `FromUriAttribute` shim (D24).

**Exit criteria:** `dotnet build -f net10.0` succeeds; `dotnet build -f net48` succeeds on Windows; zero `Microsoft.AspNetCore` references in the net48 leg.

### Phase 3 — Extract hosting-neutral request handling into Core (D12)

New files in `Service/`:

```csharp
public sealed class ScimResult
{
    public HttpStatusCode StatusCode { get; }
    public object Payload { get; }          // Resource, QueryResponseBase, Core2Error, or null
    public Uri Location { get; }            // set on Create only
    public static ScimResult Ok(object payload);
    public static ScimResult Created(Resource resource, Uri location);
    public static ScimResult NoContent();
    public static ScimResult Error(HttpStatusCode code, string message);   // wraps Core2Error
}
```

`ScimRequestHandler<T> where T : Resource` takes `(IProvider, IMonitor, Func<IProvider, IProviderAdapter<T>>)` and exposes:

| Method | Ported verbatim from |
|---|---|
| `DeleteAsync(HttpRequestMessage, string identifier)` | `ControllerTemplate.Delete`, lines 95–182 |
| `QueryAsync(HttpRequestMessage)` | `ControllerTemplate.Get()`, 184–284 |
| `RetrieveAsync(HttpRequestMessage, string identifier)` | `ControllerTemplate.Get(id)`, 286–441 |
| `PatchAsync(HttpRequestMessage, string identifier, PatchRequest2)` | `ControllerTemplate.Patch`, 443–557 |
| `CreateAsync(HttpRequestMessage, T resource)` | `ControllerTemplate.Post`, 559–655 |
| `ReplaceAsync(HttpRequestMessage, T resource, string identifier)` | `ControllerTemplate.Put`, 657–760 |

Plus non-generic handlers for the three discovery endpoints and `Bulk`, ported from `SchemasController`, `ResourceTypesController`, `ServiceProviderConfigurationController`, `BulkRequestController`.

Rules for this phase:

- **Move code; do not rewrite it.** Every `try`/`catch`, every `ServiceNotificationIdentifiers.*` value, every `monitor.Report` call, and every status-code choice is preserved. Diff each method against its origin line range in review.
- **Except** where D15 applies: `ConfigureResponse`'s unconditional 201 is deleted; PUT returns `ScimResult.Ok`; POST returns `ScimResult.Created` with an explicit `Location` computed from `ConvertRequest().GetBaseResourceIdentifier()` + `resource.GetResourceIdentifier(...)`.
- Several branches currently **rethrow** rather than return — `ControllerTemplate.cs:138,180,540,555,653` and all of `SchemasController`/`ResourceTypesController`/`ServiceProviderConfigurationController` (§2.7 item 4). Keep them as throws from the handler; each host maps unhandled `HttpResponseException` via its own exception filter. **This is R2 — the filter is not optional.**
- The `Patch` → `Get` re-entry for EnterpriseUser (`ControllerTemplate.cs:472-475`) becomes `PatchAsync` calling `RetrieveAsync` internally.

**Exit criteria:** Core builds for both TFMs; `Service/Controllers/` is gone from Core.

### Phase 4 — `Microsoft.SCIM.AspNetCore` (net10.0)

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup>
  <ItemGroup>
    <FrameworkReference Include="Microsoft.AspNetCore.App" />
    <PackageReference Include="Microsoft.AspNetCore.Mvc.NewtonsoftJson" Version="10.0.0" />
    <ProjectReference Include="..\Microsoft.SystemForCrossDomainIdentityManagement\Microsoft.SCIM.Core.csproj" />
  </ItemGroup>
</Project>
```

1. **`HttpContextRequestConverter`** — replaces `HttpRequestMessageFeature`. Builds an `HttpRequestMessage` from `HttpContext`: method, absolute `RequestUri` (scheme/host/path/query, honouring forwarded headers), and request headers. **No body buffering** — §2.9 traced every `Content` access in Core and confirmed none is reachable from the request pipeline.
2. **`ScimExceptionFilter`** (R2) — `IExceptionFilter` mapping unhandled `HttpResponseException` to `Response.StatusCode` + a `Core2Error` body. Without it, today's 404/501 responses become 500s. Every status code thrown anywhere in Core must be covered.
3. **Controllers** — thin adapters preserving routes and attributes:
   ```csharp
   [Route(ServiceConstants.RouteUsers)] [Authorize] [ApiController]
   public sealed class UsersController : ScimControllerBase<Core2EnterpriseUser> { ... }
   ```
   `ScimControllerBase<T> : ControllerBase` holds the `ScimResult` → `IActionResult` mapping and the `ConvertRequest()` call. Verb attributes and `[FromBody]` bindings carry over from `ControllerTemplate`.
4. **`RootController`** gets `[Route("scim")]` + `[ApiController]` per D14a.
5. **`ScimServiceCollectionExtensions`** — `AddScim(this IServiceCollection, IProvider, IMonitor)`, registering the singletons, `AddControllers().AddNewtonsoftJson(o => o.SerializerSettings.NullValueHandling = NullValueHandling.Ignore)`, the exception filter, and `AddApplicationPart(typeof(UsersController).Assembly)`. **Controllers no longer live in the entry assembly — without the application part, every SCIM route 404s** (R3).

**Exit criteria:** conforms to `docs/scim-conformance.md`; both Postman collections green.

### Phase 5 — `Microsoft.SCIM.AspNet` (net48)

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><TargetFramework>net48</TargetFramework></PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.AspNet.WebApi.Core" Version="5.2.9" />
    <PackageReference Include="Microsoft.AspNet.WebApi.Owin" Version="5.2.9" />
    <PackageReference Include="Microsoft.Extensions.DependencyInjection" Version="10.0.0" />
    <ProjectReference Include="..\Microsoft.SystemForCrossDomainIdentityManagement\Microsoft.SCIM.Core.csproj" />
  </ItemGroup>
</Project>
```

1. **Controllers** — `ScimApiControllerBase<T> : ApiController`, returning `IHttpActionResult`. Attribute and API translations:

   | ASP.NET Core | ASP.NET Web API |
   |---|---|
   | `Mvc.RouteAttribute` | `System.Web.Http.RouteAttribute` |
   | `Authorization.AuthorizeAttribute` | `System.Web.Http.AuthorizeAttribute` |
   | `[ApiController]` | no equivalent — omit; reproduce explicitly any behaviour it implied (automatic 400 on model-state failure) |
   | `[HttpGet("{identifier}")]` | `[HttpGet]` + `[Route("{identifier}")]` — Web API verb attributes take no template |
   | `[FromBody]`, `[FromUri]` | `System.Web.Http` equivalents, native |
   | `this.ConvertRequest()` | `this.Request` — already an `HttpRequestMessage`, no conversion needed |
   | `NoContent()` | `StatusCode(HttpStatusCode.NoContent)` |
   | `StatusCode(int, object)` | `Content(HttpStatusCode, object)` |
   | `CreatedAtAction(...)` | `Created(location, content)` — the explicit Location from D15 |
   | `Ok(obj)`, `BadRequest()`, `NotFound()`, `Conflict()` | same names on `ApiController` |

2. **`ScimHttpConfiguration.Configure(HttpConfiguration, IServiceProvider)`**:
   - `config.MapHttpAttributeRoutes()` — **no conventional route** (D14a)
   - `config.DependencyResolver = new ServiceProviderDependencyResolver(services)`
   - Newtonsoft formatter with `NullValueHandling.Ignore`, matching D5
   - `config.Formatters.Remove(config.Formatters.XmlFormatter)` — **the Core leg has no XML formatter; leaving Web API's in is an immediate parity break** (R7)
   - `IExceptionFilter` mapping `HttpResponseException` identically to Phase 4 item 2
3. **`ServiceProviderDependencyResolver`** (D9) — `IDependencyResolver` + `IDependencyScope` over `IServiceProvider`. `BeginScope()` returns a scoped provider; `GetService` returns **`null`** for unregistered types (Web API requires null, not a throw); `GetServices` returns an empty enumerable.
4. **`MonitoringMiddleware`** — uncomment `Service/MonitoringMiddleware.cs` and move it here. Verify `context.Request.Identify()` still exists in Core's extensions and fix the call if not.

**Exit criteria:** builds on Windows; conforms to `docs/scim-conformance.md`; both Postman collections green.

### Phase 6 — Upgrade `Microsoft.SCIM.WebHostSample` to net10.0

- `<TargetFramework>net10.0</TargetFramework>`, `Sdk.Web` retained; `ProjectReference` → `Microsoft.SCIM.AspNetCore`.
- Merge `Program.cs` + `Startup.cs` into `WebApplicationBuilder` minimal hosting (D10). Preserve exactly:
  - the `#if DEBUG` + `IsDevelopment()` guard around the JWT-validation bypass, **including the explanatory comment block** — this is a deliberate security control;
  - `NullValueHandling.Ignore`;
  - the `AuthenticationFailed` handler;
  - `UseAuthentication()` before `UseAuthorization()`.
- **Remove `UseHsts()` and `UseHttpsRedirection()`** (D20). Add the D21 startup banner.
- `MapDefaultControllerRoute()` → `MapControllers()` (D14a).
- `services.AddScim(new InMemoryProvider(), new ConsoleMonitor())`.
- Add `Microsoft.AspNetCore.Authentication.JwtBearer` 10.0.* (a NuGet package, not in the shared framework).
- Add a `launchSettings.json` with an explicit HTTP URL — there is none today, and D20 removes the HTTPS redirect that previously papered over it.
- `TokenController` is `[Obsolete(..., error: true)]`; it compiles today only because nothing references it. Preserve that arrangement and its full header comment.
- Remove `.config/dotnet-tools.json` (dead `dotnet-ef`).

### Phase 7 — New `Microsoft.SCIM.WebHostSample.Net48`

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net48</TargetFramework>
    <OutputType>Exe</OutputType>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.AspNet.WebApi.OwinSelfHost" Version="5.2.9" />
    <PackageReference Include="Microsoft.Owin.Security.Jwt" Version="4.2.2" />
    <PackageReference Include="Microsoft.Extensions.Configuration.Json" Version="10.0.0" />
    <PackageReference Include="Microsoft.Extensions.DependencyInjection" Version="10.0.0" />
    <ProjectReference Include="..\Microsoft.SCIM.AspNet\Microsoft.SCIM.AspNet.csproj" />
  </ItemGroup>

  <!-- Provider + Resources single-sourced from the net10 sample (D6) -->
  <ItemGroup>
    <Compile Include="..\Microsoft.SCIM.WebHostSample\Provider\**\*.cs"  Link="Provider\%(Filename)%(Extension)" />
    <Compile Include="..\Microsoft.SCIM.WebHostSample\Resources\**\*.cs" Link="Resources\%(Filename)%(Extension)" />
    <None Include="..\Microsoft.SCIM.WebHostSample\appsettings*.json" Link="%(Filename)%(Extension)"
          CopyToOutputDirectory="PreserveNewest" />
  </ItemGroup>
</Project>
```

- `Program.cs` — `WebApp.Start<Startup>(url)`; print the D21 banner and the listening address; block until Ctrl+C.
- `Startup.Configuration(IAppBuilder app)`:
  1. Read `ASPNETCORE_ENVIRONMENT` (D17), layer `appsettings.{env}.json` over `appsettings.json` (D8).
  2. `app.UseJwtBearerAuthentication(...)` (D7) with `AllowedAudiences` = `Token:TokenAudience` and a `SymmetricKeyIssuerSecurityKeyProvider` over `Token:TokenIssuer`/`Token:IssuerSigningKey`. Mirror the Core sample's dev/prod split with the same `#if DEBUG` guard. **Note:** OWIN's JWT middleware does not expose per-check toggles the way `TokenValidationParameters` does; reproduce the dev bypass with a custom `JwtFormat` carrying explicit `TokenValidationParameters` rather than approximating it.
  3. Build the MEDI container — `AddSingleton<IProvider>(new InMemoryProvider())`, `AddSingleton<IMonitor>(new ConsoleMonitor())`, identical lines to the Core sample.
  4. `new HttpConfiguration()` → `ScimHttpConfiguration.Configure(config, provider)` → `app.UseWebApi(config)`.
  5. Optionally `app.Use<MonitoringMiddleware>(monitor)`.
- Port the `/scim/token` test issuer as a net48 controller, carrying the full `[Obsolete]` + DO-NOT-USE-IN-PRODUCTION treatment. Do not weaken it.
- **Validate D19's residual risk here** (the former O2, now a Phase 7 task): `Microsoft.Owin.Security.Jwt` 4.2.2 declares a *minimum* of `System.IdentityModel.Tokens.Jwt` 5.1.4, so NuGet unifies to 8.x. Expect to need `bindingRedirect` entries in `App.config`. If OWIN misbehaves against 8.x, fall back to a conditional per-TFM version — **pre-authorized, no further approval needed** — and record which way it went in §7.1.

### Phase 8 — CI and documentation

`.github/workflows/build.yml`:

```yaml
name: Build
on: [push, pull_request]
jobs:
  build:
    runs-on: windows-latest        # required for the net48 targeting pack
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with: { dotnet-version: '10.0.x' }
      - run: dotnet restore Microsoft.SCIM.sln
      - run: dotnet build Microsoft.SCIM.sln -c Release --no-restore
```

Docs — specific edits, verified against the current file:
- `README.md:38` — "This reference code was developed as a .Net core MVC web API" and the two-project description must become the four-project layout and both hosting legs.
- `README.md:61-62` — the Contents table lists only `Microsoft.SystemForCrossDomainIdentityManagement` and `Microsoft.SCIM.WebHostSample`; add `Microsoft.SCIM.AspNet`, `Microsoft.SCIM.AspNetCore`, `Microsoft.SCIM.WebHostSample.Net48`.
- `README.md:77` — the existing `TokenController`/dev-mode security note must be extended to name the net48 sample's token controller and dev branch too (§2.8), and to carry D20's "neither sample enables HTTPS" statement.
- `README.md` front-matter — `products: [dotnetcore]` no longer describes the repo; add `dotnet`.
- Add: how to choose a leg, and how to run each sample.
- `docs/scim-conformance.md` — the Phase 0 specification table (the acceptance criterion).
- `docs/net48-hosting.md` — hosting `Microsoft.SCIM.AspNet` under IIS/`System.Web` for production, since the OWIN self-host is a dev harness. Must cover:
  - **TLS is the host's responsibility** — neither sample enables HTTPS (D20);
  - the library is **not strong-name signed** (D14), so GAC/strong-name consumers must re-sign;
  - the `<system.webServer>` `WebDAVModule` removal and `runAllManagedModulesForAllRequests` gotchas that break PUT/PATCH/DELETE on IIS;
  - the vendored `System.Web.Http` shim on the net10 leg (D3) and why it exists.

### Phase 9 — Verification (§10)

---

## 7. Package matrix

| Package | net48 | net10.0 | Note |
|---|---|---|---|
| `Newtonsoft.Json` | 13.0.3 | 13.0.3 | D5; direct ref, was transitive at 12.0.2 |
| `Microsoft.AspNet.WebApi.Client` | 5.2.9 | 5.2.9 | `System.Net.Http.Formatting`; netstandard2.0, valid on both. **Still required after D13** — `ProtocolExtensions` uses `JsonMediaTypeFormatter` |
| `Microsoft.AspNet.WebApi.Core` | 5.2.9 | — | real `System.Web.Http` on net48 |
| `Microsoft.AspNet.WebApi.Owin` | 5.2.9 | — | `app.UseWebApi` |
| `Microsoft.AspNet.WebApi.OwinSelfHost` | 5.2.9 | — | sample only |
| `Microsoft.Owin.Security.Jwt` | 4.2.2 | — | D7 |
| `Microsoft.AspNetCore.Mvc.NewtonsoftJson` | — | 10.0.* | |
| `Microsoft.AspNetCore.Authentication.JwtBearer` | — | 10.0.* | not in the shared framework |
| `Microsoft.Extensions.DependencyInjection` | 10.0.* | (shared fx) | D9 |
| `Microsoft.Extensions.Configuration.Json` | 10.0.* | (shared fx) | D8 |
| `System.IdentityModel.Tokens.Jwt` | 8.* | 8.* | D19 |
| `Microsoft.IdentityModel.JsonWebTokens` | 8.* | 8.* | D19, explicit pin |
| `System.Configuration.ConfigurationManager` | 10.0.* | 10.0.* | `ConfigurationSectionFactory` |
| **Removed** | `Json.Net`, `Microsoft.AspNetCore.Identity.UI`, `Microsoft.AspNetCore.Server.Kestrel`, `Microsoft.AspNetCore.Mvc.WebApiCompatShim`, `Microsoft.CodeAnalysis.FxCopAnalyzers` | | §2.6 |

### 7.1 On D19 and `Microsoft.IdentityModel.JsonWebTokens`

`Microsoft.IdentityModel.JsonWebTokens` supplies `JsonWebToken` and `JsonWebTokenHandler`. It does **not** supply `JwtHeader`, `JwtPayload`, or `JwtSecurityToken` — those live in `System.IdentityModel.Tokens.Jwt`, which depends on it.

Core's EventToken cluster is bound to the latter: `EventToken.cs` has eight `Parse*(JwtPayload)` methods and constructs `JwtSecurityToken(JwtHeader, JwtPayload)`; `EventTokenFactory`, `UnsecuredEventTokenFactory`, `EventTokenDecorator`, and `SingularEventToken` all traffic in `JwtHeader`; and **`IEventToken.Header` is a `public` `JwtHeader` property**. `JsonWebToken` is read-only, so an equivalent implementation would use `SecurityTokenDescriptor` for issuance and `TryGetPayloadValue<T>` for reading — roughly 500 lines rewritten plus a public API break.

**Therefore D19 unifies the *version* (8.x on both TFMs) and pins `Microsoft.IdentityModel.JsonWebTokens` explicitly so the unification is deliberate, but retains `System.IdentityModel.Tokens.Jwt`.** Migrating off it entirely is tracked as a separate follow-up, not part of this port.

---

## 8. File-by-file disposition

| Path | Action |
|---|---|
| `Schemas/**` (74 files) | **Unchanged**, except verifying the EventToken cluster against IdentityModel 8.x (Phase 2 item 6) |
| `Protocol/**` (58 files) | **Unchanged** |
| `Service/**` except `Controllers/` | Move to Core. Edits: `IProvider.cs` dead `using`; delete `DependencyResolverDecorator.cs`; D13 deletions; `MonitoringMiddleware.cs` relocates to `Microsoft.SCIM.AspNet` |
| `Service/Controllers/ControllerTemplate.cs` (761 L) | **Split** — orchestration → `Core/Service/ScimRequestHandler.cs`; result mapping → `ScimControllerBase<T>` per host |
| `Service/Controllers/UsersController.cs` (30 L) | Two thin copies |
| `Service/Controllers/GroupsController.cs` (31 L) | Two thin copies |
| `Service/Controllers/RootController.cs` (27 L) | Two thin copies **+ explicit `[Route("scim")]`** (D14a) |
| `Service/Controllers/SchemasController.cs` (109 L) | Body → Core handler; two thin copies |
| `Service/Controllers/ResourceTypesController.cs` (109 L) | Body → Core handler; two thin copies |
| `Service/Controllers/ServiceProviderConfigurationController.cs` (101 L) | Body → Core handler; two thin copies |
| `Service/Controllers/BulkRequestController.cs` (112 L) | Body → Core handler; two thin copies |
| `*.resx` + `*.Designer.cs` (3 pairs) | Unchanged; verify generation on both TFMs |
| `Service/Friends.cs` | **Stays in Core** — the IVT grant must remain on the `Microsoft.SCIM` assembly (D4) |
| `Protocol/Friends.cs`, `Schemas/Friends.cs` | Same; verify no duplicate `InternalsVisibleTo` after the move |
| `GlobalSuppressions.cs` (3 files) | Review after the analyzer swap (D11) |
| `WebHostSample/Provider/**`, `Resources/**` | Unchanged; linked into the net48 sample (D6) |
| `WebHostSample/Startup.cs`, `Program.cs` | Merged into minimal hosting; HSTS/HTTPS-redirect removed (D20) |
| `WebHostSample/appsettings.Development.json` | Add the D21 warning comment; key value unchanged |
| `WebHostSample/Controllers/TokenController.cs` | Unchanged semantics; net48 counterpart added |
| `Microsoft.SCIM.LogicAppValidationTemplate/**` | Unchanged |
| `PostmanCollection.json`, `SCIM Inbound.postman_collection.json` | Unchanged; oracle 2 in §10 |
| `.github/workflows/standardlogicapp-*.yml` | Unchanged |

---

## 9. Risks

| # | Risk | Severity | Mitigation |
|---|---|---|---|
| R1 | **No automated tests (D25) *and* no before-baseline (D18).** A hosting-layer rewrite of every endpoint with no recording of current behaviour. A fault that both new legs share is undetectable by cross-host diffing and invisible to anything `docs/scim-conformance.md` and the Postman collections do not explicitly assert. | **High — ACCEPTED** | §4 and §10: four oracles instead of one. D12's single-sourced handler keeps the two legs' divergence surface to ~80 lines each. The Phase 0 conformance table must be written before, not after, the code. Closed as a deliberate scope decision (D25), not an unresolved question. This is the plan's largest accepted exposure. |
| R2 | **Losing the WebApiCompatShim exception filter** silently converts today's 404/501 responses into 500s. The three discovery controllers and `Bulk` rely on it entirely (§2.7 item 4). | **High** | Phase 4 item 2 / Phase 5 item 2 make it an explicit deliverable on both legs. Enumerate every `HttpResponseException` status code thrown in Core and assert each in §10. |
| R3 | **Controllers move out of the entry assembly.** No `AddApplicationPart` → every net10 route 404s; no `MapHttpAttributeRoutes` → same on net48. | Medium | Explicit in Phase 4 item 5 / Phase 5 item 2. Caught by the first smoke test. |
| R4 | **D13 deletes three `public` types and four `internal` ones visible to `Microsoft.Graph.Provisioning`**, whose source we cannot inspect. Public `SerializeAsync` also behaves differently on the net10 leg (§2.9). | Medium — **ACCEPTED** | D13a: proceed as best-effort, keep `Service/Friends.cs` so the grant compiles, flag in PR 2. Rationale is the README's own no-support statement. If a break surfaces, restoring any individual type is a small, isolated change. |
| R5 | **net48 cannot be built or run on the dev machine** (macOS, SDK 10.0.301, Mono msbuild only). | Medium | Phase 8 CI on `windows-latest` is the build oracle. Do Phases 5/7 on Windows or accept CI round-trips. |
| R6 | **IdentityModel 5.6.0 → 8.x is three majors**, and `Microsoft.Owin.Security.Jwt` 4.2.2 was built against 5.x. | Medium | Phase 2 item 6 validates Core; Phase 7 validates OWIN and adds binding redirects. Fallback to per-TFM versions is pre-authorized. |
| R7 | **Content-negotiation divergence** — Web API ships an XML formatter, ASP.NET Core does not. `Accept: application/xml` would yield XML from net48 and 406 from net10. | Medium | Phase 5 item 2 removes it; §10 asserts it. |
| R8 | **D20 removes HTTPS from the sample** that people copy into production SCIM endpoints, and D21 keeps a committed signing key in the repo. | Medium | Startup banner in both hosts, `_comment` in `appsettings.Development.json`, and a TLS section in `docs/net48-hosting.md`. The `#if DEBUG` guard and `[Obsolete(error: true)]` on the token issuer remain untouched. |
| R9 | **`InternalsVisibleTo` across the new project boundary.** `ControllerTemplate`'s `internal` constructors and `internal readonly` fields are exactly the kind of member a consumer might touch, and they are moving out of `Microsoft.SCIM`. | Medium — **ACCEPTED** | D4 preserves the `Microsoft.SCIM` assembly identity and the grant. Per D13a, the consumer's actual usage is not established before proceeding; a break would surface at that consumer's compile time, not ours. |
| R10 | **D14a changes `RootController`'s route** from the accidental `/Root/{action}` to `/scim`. An undiscovered consumer probing the old route would 404. | Low | Confirmed in §3.4 with `PathInterface == "scim"` as the justification. Recorded as an intentional change in `docs/scim-conformance.md` (Phase 0 item 3). |
| R11 | **Newtonsoft 12.0.2 → 13.0.3** could alter edge-case serialization (date handling, `TypeNameHandling` defaults). | Low | `JsonNormalizer`/`TrustedJsonFactory` set options explicitly. Cross-host diff catches divergence but not a shared change from today. |
| R12 | **Namespace squatting on `System.Web.Http`** (D3) may confuse maintainers, or collide if a net10 consumer also references `Microsoft.AspNet.WebApi.Core`. | Low | One file, prominent header comment, documented in `docs/net48-hosting.md`. |
| R13 | .NET 10 analyzer wave on a codebase written to 3.1 conventions. | Low | D11: warnings not errors; triage separately. |
| R14 | Committed `obj/`/`bin/` `netcoreapp3.1` artefacts in the working tree may confuse restore. | Low | Phase 1 cleanup. |

---

## 10. Verification (D18 model)

No before-baseline exists, so verification rests on four oracles. **Each has stated blind spots; read them together, not individually.**

### Oracle 1 — RFC conformance (`docs/scim-conformance.md`)

Run per leg. Every row cites RFC 7644 or 7643.

**Users and Groups** (each row run against both `/scim/Users` and `/scim/Groups`)
- [ ] `POST` valid resource → **201**, `Location` header = resource URI, body = created resource (§3.3)
- [ ] `POST` duplicate `userName`/`displayName` → **409** + `Core2Error` (§3.3)
- [ ] `POST` malformed body → **400** + `Core2Error`
- [ ] `GET /{id}` → **200** + resource (§3.4.1)
- [ ] `GET /{unknown}` → **404** + `Core2Error` (§3.4.1)
- [ ] `GET` collection → **200**, `ListResponse` with `totalResults`, `itemsPerPage`, `startIndex` (§3.4.2)
- [ ] `GET ?filter=` — one case per `ComparisonOperator` in `Schemas/ComparisonOperator.cs` (§3.4.2.2)
- [ ] `GET ?filter=<malformed>` → **400** + `Core2Error`
- [ ] `GET ?attributes=` / `?excludedAttributes=` → projection honoured (§3.9)
- [ ] `GET ?startIndex=2&count=1` → pagination honoured (§3.4.2.4)
- [ ] `PATCH` add / replace / remove → per §3.5.2; **204** or **200**-with-body per the EnterpriseUser branch (`ControllerTemplate.cs:472`)
- [ ] `PATCH` complex value path (`emails[type eq "work"].value`) (§3.5.2)
- [ ] `PATCH` on unknown id → **404**
- [ ] `PUT /{id}` → **200** + replaced resource (§3.5.1) — **intentional change from the current build, D15**
- [ ] `PUT /{unknown}` → **404**
- [ ] `DELETE /{id}` → **204**, then `GET` → **404** (§3.6)
- [ ] Groups only: `members` add/remove via PATCH

**Discovery and bulk**
- [ ] `GET /scim/Schemas` → **200**, all supported schemas (§4)
- [ ] `GET /scim/ResourceTypes` → **200** (§4)
- [ ] `GET /scim/ServiceProviderConfig` → **200** (§4)
- [ ] `POST /scim/Bulk` → success and partial-failure shapes (§3.7)
- [ ] `GET /scim` (`RootController`, D14a) → **200**; and `GET /Root/Get` → **404** (the old accidental route is gone)

**Cross-cutting**
- [ ] **Every `HttpResponseException` status thrown in Core maps to that status, not 500** (R2) — enumerate the throw sites and check each
- [ ] No token → **401**; malformed → **401**; expired → **401**
- [ ] `Content-Type: application/scim+json` on success responses
- [ ] `Content-Type: application/json` request accepted
- [ ] `Accept: application/xml` → identical on both legs (R7)
- [ ] `null` properties omitted from every body (`NullValueHandling.Ignore`)
- [ ] `ConsoleMonitor` emits for requests and for each exception path

> **Blind spot:** covers only what the table asserts. Anything the RFC leaves to the implementation, and any current behaviour not written down in Phase 0, is unchecked.

### Oracle 2 — Postman collections

- [ ] `PostmanCollection.json` — full newman run green against the net10 host
- [ ] `PostmanCollection.json` — full newman run green against the net48 host
- [ ] `SCIM Inbound.postman_collection.json` — both hosts
- [ ] The Phase 0 assertion inventory is attached, so coverage is stated rather than assumed

> **Blind spot:** these collections were not written as a regression suite; their assertion depth is unknown until the Phase 0 inventory exists.

### Oracle 3 — Cross-host parity diff (primary parity check)

- [ ] Capture every Oracle 1 request against **both** hosts to `docs/parity/net48/` and `docs/parity/net10/`
- [ ] Byte-diff the pairs; status line, all headers, and body
- [ ] Every difference is listed and justified in this document

> **Blind spot:** by construction, cannot detect a fault the two legs share. This is R1's core exposure.

### Oracle 4 — Logic App validation

- [ ] `Microsoft.SCIM.LogicAppValidationTemplate/StandardLogicApp` run against the net10 host
- [ ] Same against the net48 host
- [ ] Both runs pass identically

### Build gates

- [ ] `dotnet build Microsoft.SCIM.sln -c Release` clean on Windows — four projects, both TFMs
- [ ] Core's `net48` leg builds with **zero** `Microsoft.AspNetCore.*` references (the D1/D2 guard rail)
- [ ] net10 sample starts; net48 self-host starts and prints its D21 banner and listening URL
- [ ] `GET /scim/token` issues a usable token on both

---

## 11. Delivery (D22)

```
master
 └─ feature/multitarget-net48-net10          (integration branch)
     ├─ PR 1  Phase 0-1   conformance spec, build infra
     ├─ PR 2  Phase 2     Core → net48;net10.0 + shim + D13 deletions
     ├─ PR 3  Phase 3     ScimRequestHandler / ScimResult extraction
     ├─ PR 4  Phase 4-5   both hosting projects
     ├─ PR 5  Phase 6-7   both samples
     └─ PR 6  Phase 8     CI + docs
```

PR 2 is the layering gate — no controller work starts until Core's net48 leg builds clean. Merge the integration branch to `master` only after Phase 9's four oracles pass.

---

## 12. Open items — all closed

No decisions remain. Nothing blocks Phase 0.

| # | Item | Resolution |
|---|---|---|
| **O1** | Confirm D14a — `RootController`'s route | **Closed by decision.** `[Route("scim")]` on both legs, confirmed in §3.4. `SchemaConstants.PathInterface == "scim"` makes the service root consistent with every sibling route. |
| **G1** | `Microsoft.Graph.Provisioning` consumption of deleted internals | **Closed by decision — D13a.** Proceed as best-effort; keep `Service/Friends.cs`; flag in PR 2. No longer a gate. |
| **O2** | `Microsoft.Owin.Security.Jwt` 4.2.2 vs IdentityModel 8.x | **Closed as a Phase 7 task, not a decision.** Try unified 8.x with binding redirects; per-TFM fallback pre-authorized; record the outcome in §7.1. |
| **O3** | Does Core read `HttpRequestMessage.Content`? | **Closed by investigation — §2.9.** No. Three `Content` sites traced: four are outbound sets, one is reachable only from callerless `SerializeAsync`, one is deleted by D13. `HttpContextRequestConverter` does not buffer. |
| **O4** | Is a `FromUriAttribute` shim needed? | **Closed by investigation — D24.** No. One use site, rewritten by Phase 3/4 anyway. `Compat/` holds only `HttpResponseException`. |
| **O5** | `ServiceNotificationIdentifiers.SchematizedMediaTypeFormatter*` constants | **Closed by decision.** Removed with the class (D13). |
| **O6** | Automated tests | **Closed by decision — D25.** Out of scope. R1 is marked ACCEPTED in §9 rather than left open. |
| — | Assembly versioning | **Closed by decision — D23.** 2.0.0 across `Version`/`AssemblyVersion`/`FileVersion`. |

### 12.1 Deliberate behaviour changes from the current build

Every intentional deviation, to be recorded in `docs/scim-conformance.md` (Phase 0 item 3):

| Change | From | To | Authority |
|---|---|---|---|
| PUT success status | 201 (via `ConfigureResponse`, ordering-dependent) | **200** | D15, RFC 7644 §3.5.1 |
| POST `Location` header | written twice — manually and by `CreatedAtAction` | **once**, explicit, from `GetBaseResourceIdentifier()`/`GetResourceIdentifier()` | D15, RFC 7644 §3.3 |
| `RootController` route | `/Root/{action}` (conventional-routing accident) | **`/scim`** | D14a |
| Routing model | attribute routes + `MapDefaultControllerRoute()` fallback | **attribute routes only**, both legs | D14a |
| XML content negotiation | n/a on 3.1; Web API would add it by default | **removed on net48** so both legs behave alike | R7 |
| HSTS / HTTPS redirect | enabled in the sample | **removed**, both samples HTTP-only dev harnesses | D20 |
| Public API | 3 public types present | **removed**: `SchematizedMediaTypeFormatter`, `SampleProvider`, `ISampleProvider` | D13 |
| `SerializeAsync` on an inbound request | full body serialized | **empty body line on the net10 leg** | §2.9, accepted under D13a |
| Assembly version | unset (1.0.0) | **2.0.0** | D23 |
