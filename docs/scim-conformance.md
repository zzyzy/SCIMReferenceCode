# SCIM conformance specification

**Status:** acceptance criterion for the `net48` + `net10.0` multi-targeting port.
**Authority:** RFC 7644 (protocol), RFC 7643 (core schema).
**Scope:** both hosting legs — `Microsoft.SCIM.AspNet` (net48 / ASP.NET Web API 2) and
`Microsoft.SCIM.AspNetCore` (net10.0 / ASP.NET Core MVC).

The port deliberately dropped the "capture the current build's behaviour first" step, so
there is no recorded pre-port baseline and **this table is the specification** instead: every
row is derived from the RFCs and cross-checked
against the assertions already present in `PostmanCollection.json` and
`SCIM Inbound.postman_collection.json`.

Both legs must satisfy every row **identically**: same status code, same headers, same JSON
body shape. Divergence between the legs is a defect regardless of which leg looks "nicer".

---

## 1. Service root and routes

All routes are **attribute routes**. Neither leg registers a conventional-route fallback
(`MapDefaultControllerRoute()` on Core, a `config.Routes.MapHttpRoute(...)` default on
net48). See §5 item 3 for why.

| Endpoint | Route template | Controller |
|---|---|---|
| Service root | `scim` | `RootController` |
| Users | `scim/Users` | `UsersController` |
| Groups | `scim/Groups` | `GroupsController` |
| Schemas | `scim/Schemas` | `SchemasController` |
| Resource types | `scim/ResourceTypes` | `ResourceTypesController` |
| Service provider config | `scim/ServiceProviderConfig` | `ServiceProviderConfigurationController` |
| Bulk | `scim/Bulk` | `BulkRequestController` |

`scim` comes from `SchemaConstants.PathInterface`; the resource segments come from
`ProtocolConstants.PathUsers` / `PathGroups` / `PathBulk` and
`ServiceConstants.PathSegment*`. Route matching is case-insensitive on both legs.

Every SCIM endpoint requires authorization (`[Authorize]` on Core,
`System.Web.Http.AuthorizeAttribute` on net48).

---

## 2. Users and Groups

Each row is run twice per leg: once against `scim/Users` with `Core2EnterpriseUser` payloads
and once against `scim/Groups` with `Core2Group` payloads.

| # | Request | Expected status | Expected headers | Expected body | Authority |
|---|---|---|---|---|---|
| U1 | `POST` valid resource | **201** | `Location` = absolute resource URI, exactly once; `Content-Type: application/scim+json` | created resource, with `id` and `meta` | RFC 7644 §3.3 |
| U2 | `POST` duplicate `userName` / `displayName` | **409** | — | `Core2Error` with `detail` | RFC 7644 §3.3, §3.12 |
| U3 | `POST` malformed / unparseable body | **400** | — | `Core2Error` or empty problem body | RFC 7644 §3.12 |
| U4 | `POST` body missing the required `userName` | **400** | — | `Core2Error` | RFC 7643 §4.1.1 |
| U5 | `GET /{id}` for an existing resource | **200** | `Content-Type: application/scim+json` | the resource; `id` equals the requested id | RFC 7644 §3.4.1 |
| U6 | `GET /{unknown-id}` | **404** | — | `Core2Error` whose `detail` names the identifier | RFC 7644 §3.4.1 |
| U7 | `GET` collection, store empty | **200** | — | `ListResponse` with `totalResults: 0`, `Resources: []` | RFC 7644 §3.4.2 |
| U8 | `GET` collection, store populated | **200** | — | `ListResponse` with `totalResults`, `itemsPerPage`, `startIndex` | RFC 7644 §3.4.2 |
| U9 | `GET ?filter=<attr> eq "<value>"` | **200** | — | `ListResponse` containing only matching resources | RFC 7644 §3.4.2.2 |
| U10 | `GET ?filter=` one case per member of `Schemas/ComparisonOperator.cs` (`eq`, `ne`, `co`, `sw`, `ew`, `gt`, `ge`, `lt`, `le`) | **200** | — | `ListResponse`; operator semantics per RFC | RFC 7644 §3.4.2.2 — all nine now answered by the reference provider |
| U11 | `GET ?filter=<a> eq <x> and (<b> co <y> or <b> co <z>)` | **200** | — | `ListResponse`; grouping and precedence honoured | RFC 7644 §3.4.2.2 |
| U12 | `GET ?filter=<malformed>` | **400** | — | `Core2Error` | RFC 7644 §3.4.2.2, §3.12 |
| U13 | `GET ?attributes=userName,emails` | **200** | — | only the requested attributes plus always-returned ones (`id`, `schemas`, `meta`) | RFC 7644 §3.9 |
| U14 | `GET ?attributes=emails[type eq "work"]` | **200** | — | complex-attribute projection honoured | RFC 7644 §3.9, §3.10 |
| U15 | `GET /{id}?excludedAttributes=members` | **200** | — | the named attribute absent from the body | RFC 7644 §3.9 |
| U16 | `GET ?startIndex=1&count=2` | **200** | — | `itemsPerPage` = 2, `startIndex` = 1, `totalResults` = full match count | RFC 7644 §3.4.2.4 |
| U17 | `PATCH /{id}` add / replace / remove, non-EnterpriseUser resource (e.g. a Group) | **204** | — | empty | RFC 7644 §3.5.2 |
| U18 | `PATCH /{id}` on a `Core2EnterpriseUser` | **200** | `Content-Type: application/scim+json` | the patched resource | RFC 7644 §3.5.2 (server MAY return the resource) |
| U19 | `PATCH` a complex value path (`emails[type eq "work"].value`) | **204** / **200** per U17/U18 | — | change applied | RFC 7644 §3.5.2 |
| U20 | `PATCH /{unknown-id}` | **404** | — | empty or `Core2Error` | RFC 7644 §3.5.2 |
| U21 | `PATCH` with an empty / null body | **400** | — | — | RFC 7644 §3.5.2 |
| U22 | `PUT /{id}` valid replacement | **200** | `Content-Type: application/scim+json` | the replaced resource | RFC 7644 §3.5.1 — **intentional change, see §5 item 1** |
| U23 | `PUT /{unknown-id}` | **404** | — | `Core2Error` naming the identifier | RFC 7644 §3.5.1 |
| U24 | `PUT /{id}` body missing `userName` | **400** | — | `Core2Error` | RFC 7643 §4.1.1 |
| U25 | `PUT /{id}` body with an unrecognized attribute name | **200** | — | unknown attribute silently absent from the stored resource | RFC 7644 §3.5.1 |
| U26 | `DELETE /{id}` | **204** | — | empty | RFC 7644 §3.6 |
| U27 | `GET /{id}` after `DELETE` | **404** | — | `Core2Error` | RFC 7644 §3.6 |
| U28 | `DELETE /{unknown-id}` | **204** | — | empty | see note below |
| U29 | Groups only: `PATCH` add a `members` value, then `GET` | **204** then **200** | — | the member present in `members` | RFC 7643 §4.2 |
| U30 | Groups only: `PATCH` remove a `members` value, then `GET` | **204** then **200** | — | the member absent from `members` | RFC 7643 §4.2 |
| U31 | Groups only: `PATCH` replace `members`, then `GET` | **204** then **200** | — | `members` holds exactly the operation's values | RFC 7644 §3.5.2.3 — **fixed, see §5 item 12** |
| U32 | `PATCH` remove with `path` and no `value`, singular attribute | **204** / **200** per U17/U18 | — | the named attribute cleared | RFC 7644 §3.5.2.2 — **fixed, see §5 item 12** |
| U32a | `PATCH` remove with `path` and no `value`, a multi-valued sub-path such as `emails[type eq "work"].value` | **204** / **200** per U17/U18 | — | that entry removed, the others retained | RFC 7644 §3.5.2.2 — **fixed, see §5 item 12** |
| U33 | `PATCH` a path the resource type does not model | **400** | — | `Core2Error` with `scimType` `invalidPath` | RFC 7644 §3.5.2, §3.12 — **behaviour change, see §5 item 13** |
| U34 | `GET ?filter=<attr> co "<value>"` and `sw` | **200** | — | `ListResponse` of matching resources | RFC 7644 §3.4.2.2 — **fixed, see §5 item 14** |
| U35 | `PUT /{unknown-id}` with no `id` in the body | **404** | — | `Core2Error` naming the identifier | RFC 7644 §3.5.1 — **fixed, see §5 item 15** |
| U36 | `PUT /{id}` whose body `id` names a different resource | **400** | — | `Core2Error` with `scimType` `mutability` | RFC 7644 §3.5.1, §3.12 |
| U37 | `PUT` / `PATCH` / `DELETE` on a collection URI (`scim/Users`) | **405** | — | — | **parity fix, see §5 item 16** |
| U38 | `GET scim/ServiceProviderConfig` | **200** | — | includes `etag`, spelled in lower case | RFC 7643 §5 — **fixed, see §5 item 14** |

> **Note on U31-U33.** All three previously answered success while doing nothing. U31 meant a
> full Group membership sync silently applied no change; U32 meant no attribute could be removed
> by path alone; U33 meant a malformed operation could never fail its request, so the atomicity
> §3.5.2 requires could not be enforced. `nickName`, `locale`, `timezone`, `userType` and `ims`
> were also modelled and advertised but absent from the patcher, and are now applied. U32 was two
> bugs: the singular attributes, and then the multi-valued ones (U32a), whose guard read the
> now-empty value collection as "wrong number of values" and returned.

> **Note on U13-U15.** `attributes` and `excludedAttributes` were parsed and passed to the
> provider but never applied to a response body; the sample providers ignored them and no
> projection code existed. `ScimProjection` now applies both in the shared handler, on the
> serialized JSON - nulling properties cannot express omitting a non-nullable member such as
> `active`, nor reach a sub-attribute such as `name.formatted`. The Postman assertion for
> `excludedAttributes=members` passed previously only because the sample group had no members.

> **Note on U16.** `startIndex` was ignored: the sample user provider applied `Take(count)` and
> `ProviderBase.PaginateQueryAsync` reported `startIndex: 1` and `itemsPerPage: totalResults`
> unconditionally. The window is now applied in `PaginateQueryAsync`, which implies the contract
> that `QueryAsync` returns every match - a provider that pages in its store overrides
> `PaginateQueryAsync` instead.

> **Note on U12.** A malformed filter returned **500**, not 400: `ResourceQuery.ParseFilters`
> threw 406 and `QueryAsync` converted every non-404 `HttpResponseException` into a 500. It now
> throws `ScimTypedException(BadRequest, invalidFilter)`, and `QueryAsync` preserves the thrown
> status.

> **Note on X10.** Error bodies now carry `scimType` (RFC 7644 section 3.12 requires it on a
> 400), and `status` is a JSON string rather than a number, as that section specifies.

> **Note on U28.** RFC 7644 §3.6 calls for 404 when deleting a resource that does not exist.
> The sample providers (`InMemoryUserProvider.DeleteAsync`, `InMemoryGroupProvider.DeleteAsync`)
> treat a missing identifier as a no-op and return success, so both legs answer **204**. This is
> pre-port sample-provider behaviour, not a hosting-layer decision, and it is identical on both
> legs. A real provider should throw
> `new HttpResponseException(HttpStatusCode.NotFound)`, which the hosting layer already maps to
> a 404 with a `Core2Error` body. Changing the sample provider was out of scope for this port.

---

## 3. Discovery and bulk

| # | Request | Expected status | Expected body | Authority |
|---|---|---|---|---|
| D1 | `GET scim/Schemas` | **200** | `ListResponse` of `TypeScheme`; `totalResults` = `itemsPerPage` = count, `startIndex` = 1 | RFC 7644 §4, RFC 7643 §7 |
| D2 | `GET scim/ResourceTypes` | **200** | `ListResponse` of `Core2ResourceType`; the User resource type's `endpoint` is `/Users` | RFC 7644 §4, RFC 7643 §6 |
| D3 | `GET scim/ServiceProviderConfig` | **200** | `ServiceConfigurationBase`; `patch.supported` is `true` | RFC 7644 §4 |
| D4 | `POST scim/Bulk` with a valid operation set | **200** | `BulkResponse2` with one `Operations` entry per request operation | RFC 7644 §3.7 |
| D5 | `POST scim/Bulk` with a mix of valid and failing operations | **200** | per-operation `status` and `response` reflect each outcome | RFC 7644 §3.7.3 |
| D6 | `POST scim/Bulk` with a null / unparseable body | **400** | `Core2Error` | RFC 7644 §3.7 |
| D7 | `GET scim` (service root) | **501** | `Core2Error` | see note below |
| D8 | `GET /Root/Get` | **404** | — | the pre-port accidental route is gone; **intentional change, see §5 item 3** |
| D9 | `DELETE scim/{anything}` (any verb but GET on the service root) | **501** | `Core2Error` | `RootProviderAdapter` implements nothing but query |

> **Note on D7.** The service root is reachable and authorized, but `RootProviderAdapter`
> inherits its query from `ProviderAdapterTemplate<Resource>`, and the sample provider has no
> handling for the `None` schema, so the query surfaces a `NotImplementedException` and the
> handler answers **501**. Identical on both legs. A provider that wants a populated service
> root implements the root schema; the hosting layer imposes nothing.

Discovery endpoints report a provider failure by throwing `System.Web.Http.HttpResponseException`
rather than returning a result. §4 covers the mapping.

---

## 4. Cross-cutting requirements

| # | Requirement | Authority |
|---|---|---|
| X1 | Every `HttpResponseException` thrown anywhere in `Microsoft.SCIM` surfaces as **that** status code, never as 500. See the enumeration below. | RFC 7644 §3.12 |
| X2 | No `Authorization` header → **401** | RFC 7644 §2 |
| X3 | Malformed bearer token → **401** | RFC 7644 §2 |
| X4 | Expired bearer token → **401** (Release builds; the sample's `#if DEBUG` + development branch deliberately disables lifetime validation for local testing) | RFC 7644 §2 |
| X5 | Success responses carry `Content-Type: application/scim+json` | RFC 7644 §3.1 |
| X6 | Requests with `Content-Type: application/json` are accepted | RFC 7644 §3.1 |
| X7 | `Accept: application/xml` behaves **identically on both legs**: **200 with a `application/scim+json` body**, not 406 and never XML. Web API's XML formatter is removed in `ScimHttpConfiguration`, and both legs pin the response media type rather than negotiating it. | port requirement (no XML on ASP.NET Core) |
| X8 | `null` properties are omitted from every response body (`NullValueHandling.Ignore` on both legs) | port requirement |
| X9 | `ConsoleMonitor` emits an entry for each request and for every exception path | port requirement |
| X10 | Error bodies are `Core2Error` — `schemas` contains `urn:ietf:params:scim:api:messages:2.0:Error`, plus `detail` and `status` | RFC 7644 §3.12 |

### X1 — exception-status enumeration

Both hosting legs install an exception filter (`ScimExceptionFilter` on Core,
`ScimExceptionFilterAttribute` on net48) that converts an unhandled `HttpResponseException`
into its own status code plus a `Core2Error` body. Without that filter these all become 500s;
this is the single sharpest regression risk in the port.

Statuses reachable by an unhandled throw out of the request handlers:

| Status | Thrown from |
|---|---|
| **400** Bad Request | `SchemasController` / `ResourceTypesController` / `ServiceProviderConfigurationController` / `Bulk` on `ArgumentException`; `RequestExtensions.Relate`, `RequestExtensions.Enlist` on an unsupported bulk method |
| **404** Not Found | provider implementations (e.g. `InMemoryUserProvider`, `InMemoryGroupProvider`) for unknown identifiers |
| **409** Conflict | provider implementations on duplicate `userName` / `displayName` |
| **500** Internal Server Error | `TryGetRequestIdentifier` failure; a null `IProvider` on the discovery and bulk paths |
| **501** Not Implemented | `RootProviderAdapter` (every verb except query); handler `NotImplementedException` / `NotSupportedException` paths on delete, patch and create |

---

## 5. Deliberate deviations from the pre-port build

Each of these is an intentional behaviour change, not a regression. Reviewers comparing
against the `netcoreapp3.1` build should expect exactly these differences and no others.

1. **`PUT` success status is 200, not 201.** The old `ConfigureResponse` set
   `Response.StatusCode = 201` unconditionally, including on the `PUT` path which then
   returned `Ok(result)`; the status actually observed on the wire depended on ASP.NET Core's
   result-execution ordering, and had no net48 equivalent to copy. RFC 7644 §3.5.1 requires
   200 for a successful replace. `PostmanCollection.json` already asserts 200 for both
   "User 2 replace test" and "group put".
2. **`POST` writes `Location` exactly once.** The old code wrote it manually in
   `ConfigureResponse` *and* again via `CreatedAtAction(nameof(Post), result)`, which derived
   a second URI from MVC routing. Web API has no `CreatedAtAction` equivalent producing the
   same URI. Both legs now emit one explicit `Location`, computed from
   `HttpRequestMessage.GetBaseResourceIdentifier()` + `Resource.GetResourceIdentifier(...)`.
   RFC 7644 §3.3.
3. **`RootController` is at `scim`, not `/Root/{action}`.** It previously carried no
   `[Route]` and was reachable only through `MapDefaultControllerRoute()`. Web API's default
   route shape (`api/{controller}/{id}`) differs, so conventional routing could not port
   identically. `SchemaConstants.PathInterface` is already `"scim"`, which makes `scim` the
   natural service root and consistent with `scim/Users`, `scim/Groups`, `scim/Schemas`.
   Consequence: `GET /Root/Get` now 404s (row D8).
4. **Attribute routing only, on both legs.** No `MapDefaultControllerRoute()` on Core, no
   conventional default route on net48. Removes the largest single source of route drift
   between the two frameworks.
5. **No XML content negotiation on net48.** Web API registers an XML formatter by default;
   ASP.NET Core does not. `ScimHttpConfiguration` removes it so `Accept: application/xml`
   behaves the same on both legs (row X7).
6. **No HSTS and no HTTPS redirect in either sample.** Both samples are HTTP-only dev
   harnesses, so the parity comparison is like-for-like. TLS is the host's responsibility —
   see `docs/net48-hosting.md`. Both samples print a DEV-ONLY startup banner saying so.
7. **Three public types removed:** `SchematizedMediaTypeFormatter`, `SampleProvider`,
   `ISampleProvider`, along with four unreferenced internal factory classes and the two
   `ServiceNotificationIdentifiers.SchematizedMediaTypeFormatter*` constants.
8. **`ProtocolExtensions.SerializeAsync` on an inbound request serializes an empty body line
   on the net10 leg.** The converted `HttpRequestMessage` carries no `Content`, because
   nothing in the request pipeline reads the body from it (MVC model binding produces the
   typed body instead). `SerializeAsync` has no callers in this repo.
9. **Assembly version is 2.0.0** (was unset, i.e. 1.0.0).
10. **Failure statuses carry a `Core2Error` body, never an RFC 9110 `ProblemDetails` one.**
    `ControllerBase.BadRequest()` / `NotFound()` / `Conflict()` return an
    `IStatusCodeActionResult`, which `[ApiController]` rewrites into a `ProblemDetails` body —
    a shape ASP.NET Web API cannot reproduce, so the two legs would answer identically-failing
    requests with different bodies. `ScimResult.Status` therefore attaches a `Core2Error`, and
    the Core leg sets `SuppressMapClientErrors` and `SuppressModelStateInvalidFilter`. The
    result is the error shape RFC 7644 §3.12 asks for on both legs, and it makes the
    `PostmanCollection.json` assertion that a 409 body contains `detail` pass rather than
    accidentally fail.
11. **The committed development signing key is 32 characters, up from 24.** IdentityModel 8.x
    enforces HS256's 256-bit minimum key size; the previous 192-bit value makes token
    validation fail outright with `IDX10720`. The value is still an obvious dummy in a
    file named `Development` — see the `_comment_IssuerSigningKey` next to it. Forced by the
    IdentityModel 5.6.0 → 8.x upgrade, not chosen.
12. **Three PATCH operations that answered success now take effect** (rows U31, U32, and the
    five unpatched attributes). `Apply(Core2Group, PatchOperation2)` handled only `Add` and
    `Remove` under `members`, so a `Replace` fell through the inner switch; the user patcher
    deserialized an absent `value` without a null guard and then substituted a value object
    whose own value was null, which every singular-attribute case reads as the removal of some
    *other* value and ignores. The multi-valued patchers - emails, phone numbers, addresses,
    roles and now ims - then had the same fault in a different shape: their guard demanded
    exactly one value, and an omitted value is an empty collection. Nothing about these was
    deliberate.
13. **A PATCH path the resource type does not model is now rejected with 400 `invalidPath`**
    (row U33), where it was previously discarded silently. This is the one change here that can
    break a working client: anything sending a path this library does not model will start
    seeing failures instead of no-ops. It is what RFC 7644 §3.5.2 requires, and without it the
    atomicity the Edupass specification demands of a multi-operation PATCH cannot be enforced —
    a malformed operation that cannot fail cannot fail its request either. `Core2EnterpriseUser`
    gained a virtual `TryPatchExtensionAttribute` so that a derived type carrying a schema
    extension can claim its own paths before the core patcher rejects them; a type that does not
    override it will see 400 on every PATCH against its extension.
14. **`co` and `sw` now work, and `etag` is spelled as the RFC spells it** (rows U34, U38).
    `FilterExpression`'s regex parsed both operators, but its mapping had no case for either and
    `ComparisonOperator` had no `Contains` or `StartsWith` member to map them to, so a filter
    using them threw. Worse, the `default` branch that reported the failure read `this.Operator`
    - the property's own getter, holding the previous value, which defaults to zero and so to
    `bitAnd` - instead of the incoming `value`, so every unmapped operator was reported as
    `bitAnd`. That is why the gap went unnoticed. The two enum members are appended rather than
    inserted, because the values are ordinal. Separately, `ServiceProviderConfig` emitted its
    entity-tag feature as `eTag`; RFC 7643 §5 spells it `etag`, so a client looking for it found
    nothing.
15. **`PUT` to an unknown identifier answers 404 whether or not the body carries `id`** (rows
    U35, U36). `id` is read-only, so a client may legitimately omit it - and then the provider
    received an unidentified resource and answered 400 before it could look anything up. The
    request URI is authoritative per RFC 7644 §3.5.1, so the handler now sets the identifier from
    the route, and refuses a body that names a *different* resource with `scimType` `mutability`.
    The 400 body also no longer hands back `"Exception of type '...' was thrown."` as its detail.
16. **A collection URI answers 405 to `PUT`, `PATCH` and `DELETE` on both legs** (row U37). The
    service root routes at the prefix, so its `{identifier}` template has the same shape as
    `scim/Users`. For a verb the Users controller does not define, the parameterised route was
    the last candidate standing on ASP.NET Core, so `PUT scim/Users` reached the service root as
    a resource named "Users" and answered 415 or 400 where net48 answered 405. The service
    root's identifier is now constrained so that it cannot match a collection segment.

---

## 6. Postman assertion inventory

Stated so that coverage is honest rather than assumed. Neither collection was written as a
regression suite.

### `PostmanCollection.json` ("SCIM Tests") — 89 requests

What it **does** assert:

- Status codes: 200 on every discovery and read endpoint; 201 on every `POST`; 200 on `PUT`;
  204 on `PATCH` (Users and Groups) and on every `DELETE`; 400 on `POST` with no `userName`
  and on `POST` of junk; 400 on `PUT` with no `userName`; 409 on duplicate `POST` (twice).
- Bodies: `ResourceTypes[0].endpoint == "/Users"`; `patch.supported == true`;
  `Schemas` contains `"User Account"`; round-trip of `id` after create; `userName`,
  `active`, `name.formatted`, `displayName` after patch/replace; `totalResults` and
  `itemsPerPage` for pagination; `totalResults` for `eq`/`and`/`or`, `sw` and `gt` filters;
  `attributes=emails[type eq "work"]` and `attributes=emails[value eq …]` projections;
  `excludedAttributes=members`; `Resources` empty after teardown; a misspelled `PUT`
  attribute is not echoed back.
- Error shape: only that a 409 body contains the string `detail`.

What it does **not** assert:

- Any response **header** — including `Location` on create and `Content-Type` on success.
  Rows U1 and X5 are therefore unchecked by Postman and must be verified by hand.
- 401 on missing / malformed / expired tokens (the collection always sends a token).
- 404 on any endpoint, including `GET`/`PUT`/`PATCH`/`DELETE` of an unknown id.
- 501 from `RootController`, or the service root `GET scim` at all.
- The `Bulk` endpoint — no request touches it.
- `ne`, `co` (alone), `ew`, `ge`, `lt`, `le` filter operators.
- The full `Core2Error` shape (`schemas`, `status`), only the `detail` substring.
- `PATCH` on a `Core2EnterpriseUser` returning **200 + body** rather than 204 (row U18).
- Note: "Get ServiceProviderConfig" targets `/serviceConfiguration`, which is not the route
  (`scim/ServiceProviderConfig`). That request cannot pass as written on either leg.

### `SCIM Inbound.postman_collection.json` ("SCIM Inbound") — 17 requests

Entra-shaped inbound-provisioning flow: create employee / contractor / manager chains,
on-demand lookup by `id eq`, full sync with `active eq true and (meta.lastModified ge … and
meta.lastModified le …)` plus `count`/`startIndex` paging, three `PATCH` updates
(lastname, active, manager), delta sync on a watermark, delete.

What it **does** assert: 201 on each create (and captures the `id`), 200 on the full-sync
pages and on delta sync, 204 on delete, and that delta sync's body contains a known id.

What it does **not** assert: any header; any status on `Test Connection`, `On Demand`,
the three `PATCH` requests, `Get User by ID`, or `Get all users` — those requests carry no
test script at all. Its `Get Token` request targets Entra, not the sample's token endpoint.

---

## 7. How the rows get checked

| Oracle | Covers | Blind to |
|---|---|---|
| 1 — this document, run per leg | every row above | anything the RFC leaves to the implementation, and any pre-port behaviour not written down here |
| 2 — the two Postman collections, run against both hosts | §6's "does assert" list | §6's "does not assert" list |
| 3 — cross-host byte diff of every §2/§3 request, net48 vs net10 | any divergence between the legs, headers included | a fault both legs share — this is the port's core accepted exposure |
| 4 — `Microsoft.SCIM.LogicAppValidationTemplate/StandardLogicApp` against both hosts | Entra-realistic end-to-end provisioning | whatever the templates do not exercise |

There are no automated tests in this repository; that is a deliberate scope decision.
Oracles 1 and 3 are manual. Read the four together, not individually.

### Status of oracle 1 as of the port

Run against the **net10.0** leg on a development host. Verified passing: the route table in §1;
U1 (201 + a single `Location` + `application/scim+json`), U2, U3, U5, U6, U7, U8, U9, U16,
U17, U18, U20, U22 (200, the D15 change), U26, U27, U28 (as amended above); D1, D2, D3, D6,
D7, D8 (`/Root/Get` now 404s), D9; X1 for 400/404/409/501, X2, X5, X7, X8, X10.

Both the hosting-layer exception filter paths were exercised deliberately rather than inferred: `DELETE scim/x` (a rethrown 501 out of
`ScimRequestHandler.DeleteAsync`) and `POST scim/Bulk` with a null body (a throw out of
`ScimDiscoveryRequestHandler`) each return the right status with a `Core2Error` body rather
than a 500. That is risk R2 discharged by test.

**Not yet run:** the net48 leg in full (requires Windows — see the CI workflow), oracle 3's
byte diff, oracle 4, and the U10–U15/U19–U21/U23–U25/U29/U30 rows. Those remain outstanding
verification work, not established results.
