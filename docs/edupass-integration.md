# Edupass SCIM integration

How to serve the Edupass SCIM interface with this codebase.

Two assemblies are involved:

- **`Microsoft.SCIM`** — RFC 7643 / 7644 and nothing beyond it.
- **`SCIM.EduPass`** — the Edupass User extension, its validation, the discovery payloads that
  advertise it, and Edupass JWT authentication. Multi-targets `net48;net10.0` like the core
  library, and references the hosting project for whichever leg it is compiled for.

Anything Edupass asks for that RFC 7643/7644 also asks for went into the core library. Anything
Edupass asks for that the RFCs do not went into `SCIM.EduPass`. That is the whole rule.

---

## 1. Authentication

Edupass identifies itself with a short-lived ES256-signed JWT and publishes its signing keys at
`/.well-known/keys`. There is no API key or shared secret alternative.

`ASP.NET Core`:

```csharp
EduPassAuthenticationOptions edupass =
    new EduPassAuthenticationOptions
    {
        Issuer = EduPassAuthenticationOptions.PreproductionIssuer,
        Audience = "<your Edupass application code>",
    };

builder.Services.AddEduPassAuthentication(edupass);
```

`net48` / IIS, in your OWIN `Startup`:

```csharp
app.UseEduPassAuthentication(edupass);
```

Both call `EduPassKeySetRetriever`, which wraps the JWKS in an `OpenIdConnectConfiguration` so
that `ConfigurationManager<OpenIdConnectConfiguration>` can be used — bringing caching,
background refresh and last-known-good handling rather than a hand-rolled key cache.

**One asymmetry to be aware of.** On ASP.NET Core, `RefreshOnIssuerKeyNotFound` re-fetches the
key set the first time a token arrives with an unrecognised `kid`, which is exactly the rotation
behaviour the specification asks for. OWIN's JWT middleware does not report *why* validation
failed, so the net48 leg cannot do the same; it relies on `AutomaticRefreshInterval` instead.
Set that interval shorter than the window Edupass allows between publishing a key and signing
with it, or put a validating gateway in front.

**Delete `TokenController` from any host you deploy.** It mints HS256 tokens for anonymous
callers. It is a development convenience and has no place in front of a real Edupass endpoint.

---

## 2. The User resource

`EduPassUser` derives from `Core2EnterpriseUser` and carries the extension as a real
`[DataMember]`:

```csharp
public class EduPassUser : Core2EnterpriseUser
{
    [DataMember(Name = EduPassSchemaIdentifiers.UserExtension, ...)]
    public virtual EduPassUserExtension EduPassExtension { get; set; }
}
```

### Typed member or the untyped dictionary?

Both work. Pick by whether you know the schema at compile time.

`SchematizedJsonConverter` is registered in both legs' Newtonsoft settings and makes
`Core2UserBase.CustomExtension` round-trip: an extension namespace the service was never
compiled against survives a POST and comes back on a GET. It does not go through
`ToJson()` — that method exists and works, but it runs `DataContractJsonSerializer` into a
stream and re-parses the result, so making it the response path would add a
serialize-then-reparse round trip to every response and leave two serializers to keep aligned.
Newtonsoft stays the only serializer; the converter just adds the members the default contract
cannot see.

**Prefer the typed member** for the Edupass extension, which is what `EduPassUser` does. You get
compile-time properties for `EduPassValidator` to check, and `AttributeScheme` generation that
follows the type rather than being written by hand. The dictionary is weakly typed —
`AddCustomAttribute` accepts only `Dictionary<string, object>` values.

**The two coexist safely.** On read, a schema URI already bound to a typed member is not also
captured into the dictionary; on write, the dictionary never overwrites a typed member. So an
`EduPassUser` carrying both its own extension and some unrelated one emits each exactly once.
`ResourceCloner` carries the converter too, so an atomic PATCH does not silently strip an
untyped extension.

### Replacing the `/Users` controller

A controller's generic parameter is its model-binding type — `[FromBody] T resource` — so the
built-in `UsersController` binds a `Core2EnterpriseUser` and drops the extension. Name your
resource type at registration and the hosting layer does the rest:

```csharp
// ASP.NET Core
builder.Services.AddScim<EduPassUser>(provider);

// net48
ScimHttpConfiguration.Configure<EduPassUser>(httpConfiguration, serviceProvider);
```

Each suppresses `Microsoft.SCIM.UsersController` and registers `ScimUsersController<EduPassUser>`
(`ScimUsersApiController<EduPassUser>` on net48) in its place. **`SCIM.EduPass` contains no
controller and no provider adapter** — the routes, the verb surface, the status codes and the
error bodies all come from the hosting layer and `ScimRequestHandler<T>`.

If you also need to decorate the controller — a rate-limiting filter, say — derive your own
type from `ScimUsersController<EduPassUser>` and register that instead of the closed generic.
Note that this path has not been exercised here: on net48 route attributes are inherited only
because `ScimDirectRouteProvider` makes them so, and the equivalent on ASP.NET Core has not been
tested. Verify routing before relying on it.

## 3. Your provider

`ProviderBase` is unchanged in shape. Four things are yours to get right:

**Return `EduPassUser` instances.** `IProvider` is typed in terms of `Resource`, so the runtime
subtype has to be preserved through create, retrieve, query and replace.

**Populate `groups`.** `Core2UserBase.Groups` now exists (RFC 7643 §4.1.2) but nothing can fill
it for you — only your store knows how users and groups relate. Edupass requires it on Create
User, PUT, Get All Users and Get User By ID:

```csharp
user.Groups =
    this.GroupsContaining(user.Identifier)
        .Select(group => new UserGroup
        {
            Value = group.Identifier,
            Reference = groupUri.ToString(),
            Display = group.DisplayName,
        })
        .ToArray();
```

**Validate on write.** Call `EduPassValidator.Validate(user)` from `CreateAsync` and
`ReplaceAsync`. It enforces the closed value sets, the UIN/FIN format, the 256-character ceiling
and the single-primary-email rule, and throws `ScimTypedException` with `invalidValue` — which
the handler turns into a 400 carrying that `scimType`.

**Advertise the extension.** Add to `IProvider.Schema` and `IProvider.ResourceTypes`:

```csharp
schema.Add(EduPassTypeSchemes.CreateUserExtensionTypeScheme(includeUinFin: true));
userTypeScheme.AddAttribute(EduPassTypeSchemes.CreateGroupsAttributeScheme());
resourceTypes.Add(EduPassTypeSchemes.CreateUserResourceType());
```

Pass `includeUinFin: false` if you do not store UIN/FIN — that is how the specification says to
opt out, and Edupass will stop sending it.

### PATCH must be atomic

RFC 7644 §3.5.2 and the Edupass Update Group Membership section both require all-or-nothing.
`ProtocolExtensions.Apply` mutates operation by operation, so patch a copy and publish it only
on success:

```csharp
Core2Group candidate = ResourceCloner.Clone(group);
candidate.Apply(patchRequest);
this.store[identifier] = candidate;
```

A provider over a real database should use a transaction instead.

---

## 4. Logging

`IMonitor` and the `Notification`/`*NotificationFactory` types are gone. The core library takes
`Microsoft.Extensions.Logging.ILogger`; controllers resolve `ILogger<T>`, so your existing
logging configuration applies with no SCIM-specific wiring. The former
`ServiceNotificationIdentifiers` constants are now `ScimEventIds`, with their numeric values
carried over so anything filtering on them keeps working.

`RequestLoggingMiddleware` (net48) redacts `Authorization`, `Proxy-Authorization` and `Cookie`.
The middleware it replaced logged the whole header dictionary, so bearer tokens were written to
the log on every request.

If you enable body logging — `UseHttpLogging` on ASP.NET Core — remember that SCIM bodies carry
`uinFin`, which the interface specification classifies Sensitive-Normal or above, plus names and
email addresses. Add `MediaTypeOptions.AddText("application/scim+json")` or you will get
`[Unknown media type]` instead of bodies, and redact through `IHttpLoggingInterceptor`.

---

## 5. The route prefix

`scim` is now a default, not a constant. Configure it once at startup:

```csharp
builder.Services.AddScim(provider, pathPrefix: "identity");
// or ScimHttpConfiguration.Configure(config, services, pathPrefix: "identity");
```

`ScimPath` holds the value process-wide and refuses to change after the routing or URI layers
have read it. That guard is the point: routing, `Location` headers and `meta.location` all read
the same value, and a host that routed on one segment while emitting URIs built from another
would hand out links that 404.

Rewriting happens through `ScimRouteConvention` (an `IApplicationModelConvention`) on ASP.NET
Core and `ScimDirectRouteProvider.GetRoutePrefix` on net48. No `[Route]` attribute changes.

---

## 6. Still outstanding

**Rate limiting.** The specification asks for 429 with `Retry-After` on create, update and
delete. Nothing in this codebase implements it — it is neither SCIM nor Edupass-specific, so it
belongs in your host. On ASP.NET Core use `AddRateLimiter`/`UseRateLimiter`; on IIS a request
filtering rule or a gateway is usually simpler than middleware.

**TLS 1.2.** Both samples are HTTP-only development harnesses by design. TLS is the host's
concern — see `docs/net48-hosting.md`.

**Verification.** The solution builds clean on both target frameworks, but none of this has been
exercised against a live Edupass endpoint, and there are no automated tests in this repository.
Before onboarding, at minimum: run the two Postman collections against both legs, confirm a real
Edupass token validates (and that a rotated `kid` is picked up), and check the `/Schemas` and
`/ResourceTypes` payloads against what Edupass expects. `docs/scim-conformance.md` is the row-by-
row specification both legs are held to.
