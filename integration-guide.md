# Adding SCIM to an existing ASP.NET 4.8 Web API

This guide is for teams who already have an ASP.NET Web API 2 application on .NET
Framework 4.8 and want to add a SCIM 2.0 endpoint to it, so that Microsoft Entra ID (or
another identity provider) can create, update and delete users and groups in your system.

It covers integration only. For deployment concerns — TLS, strong naming, token key
management — read [docs/net48-hosting.md](docs/net48-hosting.md) as well.

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

The route prefix `scim` is fixed. If your application already serves URLs under `/scim`,
they will conflict — host the SCIM endpoints in their own application or virtual directory.

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

You also need an `IMonitor` for logging. Implement its four methods (`Inform`, `Report`,
two `Warn` overloads) against your own logger, or copy `ConsoleMonitor` from the sample.

## Step 3 — wire it up

`ScimHttpConfiguration.Configure` does all the setup. How you call it depends on whether you
want the SCIM endpoints to share your existing Web API configuration or sit beside it.

### Option A — separate configuration (recommended)

This keeps every change the SCIM layer makes away from your existing controllers. It needs
OWIN, via the [`Microsoft.Owin.Host.SystemWeb`](https://www.nuget.org/packages/microsoft.owin.host.systemweb/)
and `Microsoft.AspNet.WebApi.Owin` packages.

`Microsoft.SCIM.WebHostSample.Iis` in this repository is a complete working example of
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
        services.AddSingleton<IMonitor>(new MyMonitor());
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

OWIN's JWT middleware has **no OIDC discovery**, unlike its ASP.NET Core equivalent. You
must supply the issuer's signing keys yourself — implement `IIssuerSecurityKeyProvider`
against your identity provider's JWKS endpoint and refresh it — or put a gateway in front
that validates the token for you.

Do not copy the sample's `TokenController` or its symmetric key. It mints valid tokens for
any anonymous caller.

## Step 5 — fix IIS for PUT, PATCH and DELETE

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

See [docs/net48-hosting.md](docs/net48-hosting.md) section 3 for the remaining IIS notes.

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

If step 3 returns 500, your provider threw something that is not an
`HttpResponseException`. If steps 6 and 7 return 405, revisit step 5.

`PostmanCollection.json` in this repository exercises the full surface, and
`Microsoft.SCIM.WebHostSample.Iis` passes all of the checks above under IIS Express.

## Common problems

| Symptom | Cause |
| --- | --- |
| 401 on every request | No authentication configured on the pipeline serving `/scim`. |
| 404 on every SCIM route | `Microsoft.SCIM.AspNet.dll` is missing from `bin`, or `MapHttpAttributeRoutes()` was never called. |
| 405 on PUT/PATCH/DELETE | IIS WebDAV module — step 5. |
| 500 instead of 404 or 409 | Provider threw a plain exception instead of `HttpResponseException`. |
| Your other controllers change behaviour | Option B side effects — switch to Option A. |
| Nothing survives a restart | You are still using `InMemoryProvider`. |
