# Hosting the SCIM library on .NET Framework 4.8

`Microsoft.SCIM.AspNet` is the ASP.NET Web API 2 hosting layer for the SCIM reference
library. `Microsoft.SCIM.WebHostSample.Net48` self-hosts it on OWIN/HttpListener so that the
sample runs with F5 and no machine setup — that sample is a **development harness**, not a
deployment template. This document covers what changes when you host the library for real.

---

## 1. TLS is the host's responsibility

**Neither sample enables HTTPS.** Both are HTTP-only, deliberately, so that the net48 and
net10.0 legs can be diffed against each other like-for-like. The net10.0 sample previously
called `UseHsts()` and `UseHttpsRedirection()`; those calls are gone.

SCIM carries bearer tokens and full user records. Running it over plaintext HTTP is not
acceptable anywhere except localhost. Before exposing the service:

- **Under IIS** — bind an HTTPS endpoint, disable the HTTP binding (or redirect it), and
  enable HSTS at the site level.
- **Behind a reverse proxy or gateway** — terminate TLS there, refuse plaintext, and forward
  `X-Forwarded-Proto` / `X-Forwarded-Host`. On the net10.0 leg, `HttpContextRequestConverter`
  reads `Request.Scheme` and `Request.Host` after the pipeline has run, so configuring
  `UseForwardedHeaders()` is enough to make the `Location` header on create come out with the
  externally visible scheme and host. On the net48 leg, Web API reads the same values from the
  OWIN request; configure the proxy module accordingly.
- **OWIN self-host** — `WebApp.Start` can serve `https://` if you bind a certificate to the
  port with `netsh http add sslcert`. Do this rather than shipping the sample as-is.

## 2. The library is not strong-name signed

`Microsoft.SCIM.dll` carries no strong name, and this port did not introduce one. This is
unchanged from the pre-port build, but on .NET Framework it matters more than it does on .NET:

- The assembly **cannot be installed in the GAC**.
- Any consumer that is itself strong-named cannot reference it directly; you must sign it
  yourself (add `SignAssembly` and a `.snk` to `Microsoft.SCIM.Core.csproj`, and expect to
  re-sign on every upgrade).
- `Service/Friends.cs`, `Protocol/Friends.cs` and `Schemas/Friends.cs` contain
  `InternalsVisibleTo` grants with public keys. Those grants are inert while the assembly is
  unsigned; they compile, and they will start being enforced the moment you sign it.

## 3. IIS gotchas that break PUT, PATCH and DELETE

These bite every Web API deployment and are not specific to SCIM, but SCIM exercises exactly
the verbs they break. In your site's `web.config`:

```xml
<system.webServer>
  <modules runAllManagedModulesForAllRequests="false">
    <!-- WebDAV hijacks PUT and DELETE and answers them itself, usually with 405. -->
    <remove name="WebDAVModule" />
  </modules>
  <handlers>
    <!-- The default handler mapping excludes PUT/PATCH/DELETE on some templates. -->
    <remove name="WebDAV" />
    <remove name="ExtensionlessUrlHandler-Integrated-4.0" />
    <remove name="OPTIONSVerbHandler" />
    <remove name="TRACEVerbHandler" />
    <add name="ExtensionlessUrlHandler-Integrated-4.0" path="*." verb="*"
         type="System.Web.Handlers.TransferRequestHandler"
         preCondition="integratedMode,runtimeVersionv4.0" />
  </handlers>
</system.webServer>
```

Symptoms if you skip this: `POST` and `GET` work, `PUT`/`PATCH`/`DELETE` return **405 Method
Not Allowed** with no trace of the request reaching your controller. Provisioning appears to
half-work, which is worse than failing outright.

Two more:

- **Request filtering** rejects `PATCH` on some hardened configurations — check
  `<security><requestFiltering><verbs>`.
- **URL encoding** — SCIM identifiers appear in path segments. If yours can contain characters
  IIS treats as suspect (`:`, `%`), set
  `<httpRuntime requestPathInvalidCharacters="" relaxedUrlToFileSystemMapping="true" />` and
  test with real identifiers.

## 4. Wiring it up yourself

`ScimHttpConfiguration.Configure(HttpConfiguration, IServiceProvider)` does everything the
hosting layer needs:

```csharp
ServiceCollection services = new ServiceCollection();
services.AddSingleton<IProvider>(new YourProvider());
services.AddSingleton<IMonitor>(new ConsoleMonitor());
IServiceProvider serviceProvider = services.BuildServiceProvider();

HttpConfiguration configuration = new HttpConfiguration();
ScimHttpConfiguration.Configure(configuration, serviceProvider);
```

It registers `ServiceProviderDependencyResolver` (so registration code is identical to the
ASP.NET Core leg), calls `MapHttpAttributeRoutes()` with **no** conventional-route fallback,
sets `NullValueHandling.Ignore` on the JSON formatter, adds `application/scim+json` to its
supported media types, **removes the XML formatter**, and adds
`ScimExceptionFilterAttribute`.

Under `System.Web` (IIS), call it from `Global.asax`'s `Application_Start` via
`GlobalConfiguration.Configure(config => ScimHttpConfiguration.Configure(config, provider))`
instead of constructing an `HttpConfiguration` yourself.

### Why the XML formatter is removed

ASP.NET Core has no XML formatter; Web API registers one by default. Leaving it in would make
`Accept: application/xml` return XML on net48 and 406 on net10.0 — an immediate parity break
between the two legs for no benefit, since SCIM is JSON-only (RFC 7644 §3.1).

### Authentication differences you must close yourself

The net10.0 sample validates production tokens through
`JwtBearerOptions.Authority`, which discovers the issuer's signing keys over OIDC metadata.
**OWIN's JWT middleware has no discovery.** `Microsoft.SCIM.WebHostSample.Net48` therefore
validates against a key from configuration. For a real deployment of the net48 leg you must
either:

- supply the issuer's signing keys yourself and refresh them (implement
  `IIssuerSecurityKeyProvider` against your identity provider's JWKS endpoint), or
- front the service with a gateway that validates the token and pass the identity through.

Do not ship the sample's symmetric-key path.

## 5. The vendored `System.Web.Http` shim on the net10.0 leg

`Microsoft.SystemForCrossDomainIdentityManagement/Compat/WebApiCompat.cs` declares
`System.Web.Http.HttpResponseException` and is compiled **for net10.0 only** — on net48 the
real type comes from `System.Web.Http.dll`.

It exists because the shared SCIM layer signals failure by throwing that type
(`IProvider` implementations, `RootProviderAdapter`, `RequestExtensions`, and every consumer's
provider), and the package that used to supply it on ASP.NET Core —
`Microsoft.AspNetCore.Mvc.WebApiCompatShim` — ships netstandard2.0 only, drags in
`Microsoft.AspNetCore.Mvc.Core` 2.2.x, and is discontinued. Vendoring one exception type
means zero source changes anywhere else.

Consequences worth knowing:

- It **squats on the `System.Web.Http` namespace**. If a net10.0 consumer also references
  `Microsoft.AspNet.WebApi.Core`, the two definitions collide. Don't do that; on .NET there is
  no reason to.
- `Compat/` contains **only** `HttpResponseException`. There is no `FromUriAttribute` shim —
  its single use site is written per host (`[FromRoute]` on net10.0, native `[FromUri]` on
  net48).
- The shim carries no exception filter of its own. Each hosting project installs one
  (`ScimExceptionFilter` / `ScimExceptionFilterAttribute`), and both route through
  `ScimResult.FromException` so a thrown status maps to the same body on both legs. If you
  build your own host, **you must install one too**, or every 404 and 501 the library signals
  becomes a 500.

## 6. Also worth knowing

- **In-memory provider.** `InMemoryProvider` in both samples keeps everything in process
  memory. Nothing survives a restart and nothing is shared across a farm.
- **The token issuer.** `scim/token` in both samples mints bearer tokens for any anonymous
  caller, signed with a key committed to this repository. Both controllers are marked
  `[Obsolete(..., error: true)]` so they cannot be referenced from code; they are still
  reachable over HTTP because MVC and Web API discover controllers by reflection. Delete them
  or replace them.
- **`ASPNETCORE_ENVIRONMENT`** drives the configuration layering on both legs, including the
  net48 one, where the name is admittedly a lie — it was chosen so that one variable, one set
  of docs and one CI script covers both.
- **Conformance.** `docs/scim-conformance.md` is the specification both legs are held to.
  If you change hosting behaviour, change that document too.
