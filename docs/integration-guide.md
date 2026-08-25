# Adding SCIM to an existing ASP.NET 4.8 Web API

This guide is for teams who already have an ASP.NET Web API 2 application on .NET
Framework 4.8 and want to add a SCIM 2.0 endpoint to it, so that Microsoft Entra ID (or
another identity provider) can create, update and delete users and groups in your system.

It covers integration only. For deployment concerns — TLS, strong naming, token key
management — read [net48-hosting.md](net48-hosting.md) as well.

---

## What you get

Once wired up, your application serves these routes:

| Route | Purpose |
| --- | --- |
| `GET/POST /scim/Users`, `GET/PUT/PATCH/DELETE /scim/Users/{id}` | User provisioning |
| `GET/POST /scim/Groups`, `GET/PUT/PATCH/DELETE /scim/Groups/{id}` | Group provisioning |
| `POST /scim/Bulk` | Bulk operations |
| `GET /scim/ServiceProviderConfig`, `/scim/Schemas`, `/scim/ResourceTypes` | Discovery |
| `GET/PUT/PATCH/DELETE /scim/{id}` | Service root |

The route prefix defaults to `scim`; pass `pathPrefix` to `ScimHttpConfiguration.Configure` to
change it. URLs you already serve under that prefix will conflict.

You write one class: a provider that maps SCIM operations onto your own user store.
Everything else — routing, JSON shape, filtering, error mapping — is done for you.

---

## Step 1 — reference the library

The projects are not published as NuGet packages. Add project references, or build them and
reference the DLLs:

- `Microsoft.SCIM.Core` — the SCIM protocol and schema layer.
- `Microsoft.SCIM.AspNet` — the ASP.NET Web API hosting layer (references Core).

```xml
<ProjectReference Include="..\Microsoft.SCIM.AspNet\Microsoft.SCIM.AspNet.csproj" />
```

Your application also needs `Microsoft.Extensions.DependencyInjection`, which the hosting
layer uses to build controllers.

Controllers are found by reflection, so `Microsoft.SCIM.AspNet.dll` must end up in your
`bin` folder. A project reference does this for you.

## Step 2 — implement a provider

Derive from `ProviderBase`. It implements `IProvider` and handles the SCIM plumbing; you fill
in the storage.

Four members are abstract, so you must implement all of them:

- `CreateAsync(Resource, string correlationIdentifier)`
- `RetrieveAsync(IResourceRetrievalParameters, string correlationIdentifier)`
- `UpdateAsync(IPatch, string correlationIdentifier)` — this is PATCH
- `DeleteAsync(IResourceIdentifier, string correlationIdentifier)`

`QueryAsync` and `ReplaceAsync` (PUT) are optional — the base class throws
`NotImplementedException`, which becomes a 500. Entra ID needs `QueryAsync`, because it looks
users up by `userName` before deciding whether to create them, so implement it in practice.

```csharp
public class MyProvider : ProviderBase
{
    public override async Task<Resource> CreateAsync(Resource resource, string correlationIdentifier)
    {
        // Each method receives both users and groups; switch on the type.
        Core2EnterpriseUser user = resource as Core2EnterpriseUser;
        if (null == user)
        {
            throw new HttpResponseException(HttpStatusCode.BadRequest);
        }

        if (await this.UserNameExists(user.UserName).ConfigureAwait(false))
        {
            // 409 Conflict — the caller already created this user.
            throw new HttpResponseException(HttpStatusCode.Conflict);
        }

        user.Identifier = await this.InsertUser(user).ConfigureAwait(false);
        return user;
    }

    public override Task<Resource> RetrieveAsync(IResourceRetrievalParameters parameters, string correlationIdentifier) { ... }
    public override Task UpdateAsync(IPatch patch, string correlationIdentifier) { ... }
    public override Task DeleteAsync(IResourceIdentifier identifier, string correlationIdentifier) { ... }

    public override Task<Resource[]> QueryAsync(IQueryParameters parameters, string correlationIdentifier) { ... }
    public override Task<Resource> ReplaceAsync(Resource resource, string correlationIdentifier) { ... }
}
```

Two things to know:

- **Signal failures by throwing `HttpResponseException`** with the status you want — 404 for
  a missing resource, 409 for a duplicate, 400 for a bad request. The library turns that into
  a proper SCIM error body. Any other exception becomes a 500.
- **Set `Identifier` on create.** That value becomes the resource `id` and the `Location`
  header, and the identity provider uses it for every later call.

`InMemoryProvider` in `Microsoft.SCIM.WebHostSample/Provider` is a complete worked example.
It is a reference, not a starting point — it stores everything in process memory.

If you serve both `/Users` and `/Groups`, `InMemoryEduPassProvider` in `SCIM.EduPass/Provider`
also shows how to keep the two consistent — see `edupass-integration.md` §3.

Logging needs no SCIM-specific wiring: the library takes `ILogger` and controllers resolve
`ILogger<T>`.

## Step 3 — wire it up

`ScimHttpConfiguration.Configure` does all the setup. How you call it depends on whether you
want the SCIM endpoints to share your existing Web API configuration or sit beside it.

### Option A — separate configuration (recommended)

This keeps every change the SCIM layer makes away from your existing controllers. It needs
OWIN, via the [`Microsoft.Owin.Host.SystemWeb`](https://www.nuget.org/packages/microsoft.owin.host.systemweb/)
and `Microsoft.AspNet.WebApi.Owin` packages.

`Microsoft.SCIM.WebHostSample.IIS` in this repository is a complete working example of
everything in this section — an application with its own `api/inventory` endpoint that gains
SCIM without a single edit to `Global.asax`.

Add an OWIN `Startup` class:

```csharp
[assembly: OwinStartup(typeof(MyApp.ScimStartup))]

public class ScimStartup
{
    public void Configuration(IAppBuilder app)
    {
        ServiceCollection services = new ServiceCollection();
        services.AddSingleton<IProvider>(new MyProvider());
        services.AddLogging();
        IServiceProvider serviceProvider = services.BuildServiceProvider();

        // Authenticate before the SCIM endpoints run — see step 4.
        ConfigureAuthentication(app);

        HttpConfiguration scim = new HttpConfiguration();
        ScimHttpConfiguration.Configure(scim, serviceProvider);
        app.UseWebApi(scim);
    }
}
```

`Microsoft.Owin.Host.SystemWeb` runs this pipeline inside IIS, ahead of your application's own
handler. Requests that match no SCIM route fall through to that handler, so your existing
application keeps working unchanged — including its own JSON settings.

Three things catch people out here:

- **Read configuration from `HttpRuntime.BinDirectory`, not
  `AppDomain.CurrentDomain.BaseDirectory`.** Under ASP.NET, `BaseDirectory` is the application
  root — the folder holding `Web.config` — while `appsettings.json` is built into `bin\`.
  Getting this wrong loads no configuration at all, and the first symptom is an
  `ArgumentNullException` from the JWT middleware while the application is starting.
- **Binding redirects must live in `Web.config`.** `AutoGenerateBindingRedirects` writes an
  `<AssemblyName>.dll.config`, which a web application never reads. Without them the JWT
  middleware fails to load, because `Microsoft.Owin.Security.Jwt` was built against an older
  `System.IdentityModel.Tokens.Jwt` than the rest of the graph resolves to.
- **Keep your own controllers on conventional routes if you can.** `MapHttpAttributeRoutes()`
  scans every loaded assembly, not just the one the configuration came from, so your own
  attribute-routed controllers are mapped into the SCIM configuration too — where the SCIM
  container tries to construct them. Conventional routes are not scanned that way. If you
  already use attribute routing, give the SCIM configuration its own
  `IHttpControllerTypeResolver` that returns only the `Microsoft.SCIM` controllers.

### Option B — shared configuration

Call it from `Application_Start` in `Global.asax`:

```csharp
GlobalConfiguration.Configure(config => ScimHttpConfiguration.Configure(config, serviceProvider));
```

Simpler, but it changes behaviour for **all** of your controllers. `Configure` will:

- replace the dependency resolver, the controller activator and the controller selector;
- remove the XML formatter, so `Accept: application/xml` starts returning 406 everywhere;
- set `NullValueHandling.Ignore` on the JSON formatter, so null properties disappear from
  every response;
- add a global exception filter;
- call `MapHttpAttributeRoutes()`.

If your application uses XML, relies on nulls in its JSON, or has its own dependency
resolver, use Option A instead.

Either way, call `ScimHttpConfiguration.Configure` **before** any of your own
`config.Routes.Map...` calls, and do not call `MapHttpAttributeRoutes()` twice.

## Step 4 — authentication

Every SCIM controller is marked `[Authorize]`. If your application has no authentication
configured for that pipeline, every request returns 401.

Entra ID sends a bearer token. Validate it with OWIN JWT middleware:

```csharp
app.UseOAuthBearerAuthentication(
    new OAuthBearerAuthenticationOptions
    {
        AccessTokenFormat = new JwtFormat(validationParameters, keyProvider)
    });
```

OWIN's JWT middleware has **no OIDC discovery**, so the issuer's signing keys are yours to
supply. For an issuer publishing a bare JWKS, `Anacle.ApiFramework.Authentication` ships a key
provider rather than you hand-rolling one — `app.UseJsonWebKeySetAuthentication(options)`. Set
`ValidAlgorithms`, or the handler accepts any algorithm the key material supports. See
`edupass-integration.md` §1. A validating gateway in front also works.

Do not copy the samples' symmetric signing key - it is a committed dummy, and anyone reading
this repository can mint a token with it.

## Step 5 — logging

The SCIM layer writes to **your** `ILogger`. The controllers resolve `ILogger<T>` from the
container and hand it to the request handlers, so whatever provider you have registered
receives every SCIM event — nothing else to wire up:

```csharp
services.AddLogging(builder => builder.AddSerilog());   // or NLog, or your own provider
```

Pass no logger and the handlers stay silent; a null logger is tolerated throughout.

### What the library logs, and what it does not

The library logs **failed operations only**. Logging every request and response is yours to
configure — IIS logging, Application Insights, Serilog request logging, or an OWIN middleware
of your own — and the library deliberately stays out of it. Nothing about that logging is
SCIM-specific, and its retention and privacy policy is yours, not a library's.

What you *cannot* do from outside is log a SCIM failure. The handlers turn a provider's
exception into a SCIM error response rather than letting it escape, so by the time any
middleware sees the response there is nothing left to catch. So the library logs those, and
logs them with the request that caused them:

```
SCIM operation failed. Correlation: 90c74a93-… POST https://host/scim/Users
Headers: Content-Type: application/scim+json; Authorization: <redacted>; …
Body: {"schemas":["urn:ietf:params:scim:schemas:core:2.0:User"],"userName":"a@b.example",…}
```

A status and a stack trace rarely say what a client actually sent, and Entra ID will not tell
you either.

**This is always on.** There is no switch, because an operator who quietens the routine
logging has not asked to be told less about the thing that broke. The body is written
**verbatim** — including anything the client put in `password` — so treat these entries as
carrying whatever your callers provision. Header values for `Authorization`,
`Proxy-Authorization`, `Cookie` and `Set-Cookie` are replaced with `<redacted>`; an earlier
version wrote the whole header dictionary and put the caller's bearer token in the log on
every request.

The one setting is the ceiling, set during startup next to your `Configure` call:

```csharp
ScimLogging.MaximumBodyLength = 64 * 1024;   // a tighter ceiling than the 10 MB default
```

A body longer than the ceiling is written up to it and marked `<truncated, …>`, so a cut-off
body is never mistaken for the whole one.

The library buffers the request body so that it is still readable after model binding — that
is the mechanism this needs, not a feature, and it is why the ceiling is the only knob.

**Buffering is skipped when nothing would write the entry.** Before copying anything, the
library asks your logger whether `Error` is enabled for the category the failure would be
logged under, and does nothing if it is not. Silence the SCIM logging and you pay none of the
cost:

```json
{ "Logging": { "LogLevel": { "Microsoft.SCIM": "None" } } }
```

On ASP.NET Core the category checked is the controller's own, so silencing one SCIM endpoint
and not another gives the right answer for each. On ASP.NET 4.8 the check happens before Web
API has selected a controller, so there is no controller type to name yet and the check is
made against `Microsoft.SCIM` — set the level there on that leg, not on an individual
controller beneath it.

One thing to know on ASP.NET 4.8: a `Logging` section in `appsettings` reaches
`IConfiguration` but not the logging builder unless you say so. The samples do:

```csharp
services.AddLogging(builder =>
{
    builder.AddConfiguration(configuration.GetSection("Logging"));   // easy to leave out
    builder.AddConsole();
});
```

Without that line the section is read and ignored, and every level you set there has no
effect on this leg while having every effect on the ASP.NET Core one.

### Logging requests and responses yourself

On ASP.NET Core that is `UseHttpLogging`, and `Microsoft.SCIM.WebHostSample/Program.cs` shows
it wired up — including the fields, the body limits and the header allow-list:

```csharp
builder.Services.AddHttpLogging(options =>
{
    options.LoggingFields = HttpLoggingFields.RequestMethod | HttpLoggingFields.RequestPath
        | HttpLoggingFields.RequestHeaders | HttpLoggingFields.ResponseStatusCode
        | HttpLoggingFields.RequestBody | HttpLoggingFields.ResponseBody;
    options.RequestBodyLogLimit = 64 * 1024;
    options.MediaTypeOptions.AddText("application/scim+json");   // or bodies are skipped
});

app.UseHttpLogging();
```

Two things that catch people out: `HttpLogging` logs at `Information` under
`Microsoft.AspNetCore.HttpLogging`, which a blanket `"Microsoft": "Warning"` filter hides; and
it will not log a body whose media type it has not been told is text, which
`application/scim+json` is not by default.

On ASP.NET 4.8 there is no built-in equivalent — use IIS logging, Application Insights, or an
OWIN middleware of your own.

## Step 6 — fix IIS for PUT, PATCH and DELETE

IIS blocks these verbs by default, and SCIM needs all of them. Symptom: `GET` and `POST`
work, everything else returns 405 without reaching your code.

Add to `web.config`:

```xml
<system.webServer>
  <modules runAllManagedModulesForAllRequests="false">
    <remove name="WebDAVModule" />
  </modules>
  <handlers>
    <remove name="WebDAV" />
    <remove name="ExtensionlessUrlHandler-Integrated-4.0" />
    <add name="ExtensionlessUrlHandler-Integrated-4.0" path="*." verb="*"
         type="System.Web.Handlers.TransferRequestHandler"
         preCondition="integratedMode,runtimeVersionv4.0" />
  </handlers>
</system.webServer>
```

See [net48-hosting.md](net48-hosting.md) section 3 for the remaining IIS notes.

---

## Check it works

With a valid bearer token, in this order:

1. `GET /scim/ServiceProviderConfig` → 200.
2. `GET /scim/Users` with no token → 401.
3. `POST /scim/Users` with a user body → 201, a `Location` header, and an `id` in the body.
4. `GET /scim/Users/{id}` → 200.
5. `GET /scim/Users?filter=userName eq "someone@example.com"` → 200 with `totalResults` 1.
6. `PATCH /scim/Users/{id}` → the change is visible on the next `GET`.
7. `DELETE /scim/Users/{id}` → 204, and the next `GET` returns 404.
8. `POST` the same `userName` twice → 409.
9. `PATCH /scim/Groups/{id}` `replace` on `members` → membership is exactly what you sent.
10. `PATCH` a `remove` with a `path` and no `value` → the attribute is cleared.
11. `PATCH` a path your resource type does not model → 400 `invalidPath`, nothing applied.

If step 3 returns 500, your provider threw something that is not an
`HttpResponseException`. If steps 6 and 7 return 405, revisit step 6. Steps 9 to 11 used to
answer success while doing nothing, so check them explicitly — the old failure mode was
silence.

`PostmanCollection.json` in this repository exercises the full surface, and
`Microsoft.SCIM.WebHostSample.IIS` passes all of the checks above under IIS Express.

## Common problems

| Symptom | Cause |
| --- | --- |
| 401 on every request | No authentication configured on the pipeline serving `/scim`. |
| 404 on every SCIM route | `Microsoft.SCIM.AspNet.dll` is missing from `bin`, or `MapHttpAttributeRoutes()` was never called. |
| 405 on PUT/PATCH/DELETE | IIS WebDAV module — step 6. |
| 500 instead of 404 or 409 | Provider threw a plain exception instead of `HttpResponseException`. The log carries the request that caused it — step 5. |
| Your other controllers change behaviour | Option B side effects — switch to Option A. |
| Nothing survives a restart | You are still using `InMemoryProvider`. |
