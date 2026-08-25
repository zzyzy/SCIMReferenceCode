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

Authentication is not the SCIM library's concern. `Microsoft.SCIM` puts `[Authorize]` on its
controllers and stops there; which mode satisfies it is the host's decision.

`Anacle.ApiFramework.Authentication` supplies the modes, one per sub-namespace, for both
hosting frameworks. It knows nothing about SCIM and does not reference it.

Edupass identifies itself with a short-lived ES256-signed JWT and publishes its signing keys at
`/.well-known/keys`. There is no API key or shared secret alternative on the Edupass side, so
Edupass itself always arrives on the `Jwt` mode. `SCIM.EduPass` contributes only the issuers,
the key set path and the algorithm.

Which modes the endpoint accepts is one switch on `EduPassAuthenticationSettings`. `Modes` is a
flags enum, so `Jwt`, `ApiKey`, or both may be enabled; both enabled means the two run
concurrently and a request satisfies either.

```csharp
EduPassAuthenticationSettings settings =
    new EduPassAuthenticationSettings
    {
        Modes = EduPassAuthenticationModes.Jwt | EduPassAuthenticationModes.ApiKey,
        Issuer = EduPassAuthentication.PreproductionIssuer,
        ApplicationCode = "<your Edupass application code>",
        ApiKeyStore = new HashedApiKeyStore(keys),
    };

// ASP.NET Core
builder.Services.AddEduPassAuthentication(settings);

// net48 / IIS, in your OWIN Startup
app.UseEduPassAuthentication(settings);
```

`Validate()` runs on wiring: no mode enabled is an error rather than an endpoint that rejects
everyone, `Jwt` without an application code is an error, and `ApiKey` without a store is an
error.

On ASP.NET Core, both modes enabled registers a third scheme — `EduPassAuthentication.CombinedScheme`
— as the default. It forwards on the shape of the request: a request carrying the API key
header goes to the key scheme, everything else to `Bearer`. Both schemes stay addressable by
name, so an endpoint can still pin one with `[Authorize(AuthenticationSchemes = ...)]`.

On net48 there is no scheme registry. The two middlewares run in turn, both passive, and
whichever recognises the request sets the principal; a request that satisfies neither reaches
the endpoint anonymous for `[Authorize]` to reject. Per-endpoint mode selection is not available
on that leg.

Wiring a single mode directly still works — `AddJsonWebKeySetAuthentication(options)` /
`UseJsonWebKeySetAuthentication(options)` with `EduPassAuthentication.CreateOptions(...)`.

The bearer leg on both frameworks goes through `JsonWebKeySetRetriever`, which wraps the key set in an
`OpenIdConnectConfiguration` so that `ConfigurationManager` can be used — bringing caching,
background refresh and last-known-good handling rather than a hand-rolled key cache.

`ValidAlgorithms` is required rather than optional. Left unset, the token handler accepts any
algorithm the key material supports, which is what an algorithm-substitution attempt relies on:
a token signed with HS256 using the published public key as the shared secret. Both legs are
tested against exactly that attack.

**One asymmetry to be aware of.** On ASP.NET Core, `RefreshOnIssuerKeyNotFound` re-fetches the
key set the first time a token arrives with an unrecognised `kid`, which is the rotation
behaviour the specification asks for. OWIN's JWT middleware does not report *why* validation
failed, so the net48 leg cannot do the same; it relies on `AutomaticRefreshInterval` instead.
Set that interval shorter than the window Edupass allows between publishing a key and signing
with it, or put a validating gateway in front.

### The API key mode

`Anacle.ApiFramework.Authentication.ApiKey` reads a key from a request header — `X-Api-Key` by
default, `ApiKeyHeaderName` to change it — and resolves it through an `IApiKeyStore` you
implement. Edupass never uses it; it is there for a relying party's *other* callers, such as an
internal admin tool or a monitoring probe, that need the same endpoint.

Where keys live is deliberately yours to decide. The supplied `HashedApiKeyStore` holds SHA-256
hashes rather than plaintext and compares them in fixed time — an ordinary string comparison
returns as soon as two bytes differ, which leaks a key one character at a time to a caller who
can measure it.

The samples no longer ship a `scim/token` issuer - it minted HS256 tokens for anonymous
callers, which has no place in a reference implementation. README.md has the recipe for minting
a development token from the committed dummy key.

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
compiled against survives a POST and comes back on a GET. **Only under
`urn:ietf:params:scim:schemas:extension:`** — the converter captures a property as an extension
by that prefix, so a vendor URN in some other namespace is dropped without complaint. It does
not go through
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
untyped extension — which is the reason to clone with `ResourceCloner.Clone` rather than a
hand-rolled JSON round trip.

### PATCH against your extension

A PATCH path the core patcher cannot place is rejected with 400 `invalidPath`. It knows nothing
of your schema, so **a derived user type must claim its own attributes or every PATCH against
them fails.** Override the hook:

```csharp
protected override bool TryPatchExtensionAttribute(PatchOperation2 operation)
{
    if (!EduPassSchemaIdentifiers.UserExtension.Equals(
            operation?.Path?.SchemaIdentifier, StringComparison.OrdinalIgnoreCase))
    {
        return false;       // not ours - let the core reject it
    }

    string value = OperationName.Remove == operation.Name
        ? null
        : operation.Value?.SingleOrDefault()?.Value;

    switch (operation.Path.AttributePath)
    {
        case EduPassAttributeNames.SchoolOrHq:
            this.EduPassExtension.SchoolOrHq = value;
            return true;
        // ... one case per attribute
        default:
            return false;   // an attribute we do not model is still an error
    }
}
```

`EduPassUser` does this already. Returning false is not "ignore" — it is "I cannot place this",
and the caller gets a 400 saying so. That is deliberate: an operation that cannot fail cannot
fail its request either, and the specification requires a multi-operation PATCH to be atomic.

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
type and register that instead of the closed generic:

```csharp
[EnableRateLimiting("scim-writes")]
public sealed class EduPassUsersController : ScimUsersController<EduPassUser>   // ScimUsersApiController<T> on net48
{
    public EduPassUsersController(IProvider provider, ILogger<EduPassUsersController> logger)
        : base(provider, logger) { }
}
```

On ASP.NET Core, add its assembly as an application part; on net48 it is found by assembly
scanning. It inherits the routes, so it moves with the configured `pathPrefix` like any other
SCIM controller, and its own filters run. Both legs are tested for this, at the default prefix
and a custom one.

## 3. Your provider

`InMemoryEduPassProvider` in `SCIM.EduPass/Provider` is a complete worked example of everything
below, and is what the conformance runs execute against. It holds state in memory and is not a
production store, but the obligations it discharges are the ones your provider inherits.

`ProviderBase` is unchanged in shape. These are yours to get right:

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

### Keep users and groups consistent

Four obligations follow from the fact that only you know how the two relate. None of them can
live in the shared library, and all four are easy to miss because nothing fails loudly:

- **Deleting a Group removes the application role from everyone who held it.** The specification
  says so explicitly. A store that deletes only the group row leaves its members holding a role
  that no longer exists.
- **Deleting a User removes them from every group that listed them.** Otherwise `members` keeps
  handing Edupass an identifier that resolves to nothing.
- **A membership naming an unknown user is refused**, not stored. Accepting it means returning a
  dangling reference on the next read.
- **A duplicate Group `displayName` is a 409.** `displayName` *is* the application role.

The cheapest way to get the first three right is not to enforce them at all: hold membership in
one place — on the group — and derive the user's `groups` from it on read. Then they cannot
disagree, and deleting either side is the removal. That is what `InMemoryEduPassProvider` does,
and why its `groups` projection and its delete paths are three lines each rather than
bookkeeping you have to remember at every write.

### PATCH must be atomic

RFC 7644 §3.5.2 and the Edupass Update Group Membership section both require all-or-nothing.
`ProtocolExtensions.Apply` mutates operation by operation, so patch a copy and publish it only
on success:

```csharp
Core2Group candidate = ResourceCloner.Clone(group);
candidate.Apply(patchRequest);
this.RequireResolvableMembers(candidate);   // validate the candidate, not the stored resource
this.store[identifier] = candidate;
```

Validate the *candidate*. Applying to the stored resource and validating afterwards leaves a
rejected request's changes in place — the caller gets a 400 and the write lands anyway, which is
the exact opposite of what atomicity means.

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

**Verification.** The 25-row Edupass test plan has been executed end to end against three hosts —
the net10.0 sample, the net48 sample, and `InMemoryEduPassProvider` — with identical status codes
on all three. See the `Test Execution` and `Provider Obligations` sheets in `test-plan.xlsx` for
the request and response captured for every row. That run is what surfaced the three PATCH
defects now fixed (`docs/scim-conformance.md` §5 items 12 and 13).

Two limits on what that proves. **The harnesses are not in this repository** — they are scripts
run against a live host, so nothing re-checks these behaviours when the code next changes; the
`replace`-on-`members` defect survived this long precisely because nothing was watching. And
**none of it has touched a live Edupass endpoint.** Every FIMS-internal expectation in the plan —
UPA creation, user-admin approval, position tables, notifications — has no counterpart here and
was not exercised.

Before onboarding, at minimum: run the two Postman collections against both legs, confirm a real
Edupass token validates (and that a rotated `kid` is picked up), and check the `/Schemas` and
`/ResourceTypes` payloads against what Edupass expects. `docs/scim-conformance.md` is the row-by-
row specification both legs are held to.
