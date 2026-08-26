# Adding extension attributes to the User schema

SCIM's core `User` schema is fixed, but RFC 7643 section 3.3 makes a resource's `schemas`
list open: a service may carry attributes of its own under an extension URI, and a client
may send an extension the service was never compiled against.

This library supports both. Pick by what you need:

| | Untyped pass-through | Typed extension |
| --- | --- | --- |
| Code to write | none | a resource type, plus provider and host wiring |
| Attributes | anything the client sends | the ones you declare |
| URI namespace | `urn:ietf:params:scim:schemas:extension:*` only | any |
| `PATCH` on one attribute | rejected, 400 `invalidPath` | works, you route it |
| Validation | none | yours |
| Applies to | `/Users` and `/Groups` | the resource type you bind |

Start with the untyped path if you only need attributes to survive a round trip. Use a
typed extension if the attributes are part of your contract — Entra ID and other clients
`PATCH` single attributes, and a rejected `PATCH` fails the whole provisioning cycle.

`SCIM.EduPass` is a complete worked example of the typed path; every step below points at
the file in it that does that step.

---

## Option A — untyped pass-through

Nothing to write. A resource implements `IExtensibleResource`
(`Microsoft.SystemForCrossDomainIdentityManagement/Schemas/IExtensibleResource.cs`), which
holds extensions in a dictionary, and `SchematizedJsonConverter` — registered by `AddScim`
on both hosting legs — makes the dictionary round-trip over the wire.

```http
POST /scim/Users
{
  "schemas": ["urn:ietf:params:scim:schemas:core:2.0:User",
              "urn:ietf:params:scim:schemas:extension:example:2.0:Custom"],
  "userName": "amy",
  "urn:ietf:params:scim:schemas:extension:example:2.0:Custom": {
    "costCentre": "CC-1",
    "location": { "site": "SG", "floor": "12" },
    "tags": ["one", "two"]
  }
}
```

The extension comes back on every read, on queries, and after an unrelated `PATCH`. Nested
objects, arrays and scalars of any JSON type are kept.

### What it will not do

- **A URI outside `urn:ietf:params:scim:schemas:extension:`** is dropped silently — the
  resource is still created, the extension is not stored. `Core2UserBase.AddCustomAttribute`
  enforces the prefix. `urn:example:params:scim:schemas:Custom` will not work; a typed
  extension is the only way to use your own namespace.
- **A value that is not a JSON object** is dropped for the same reason.
- **`PATCH` on a single extension attribute** — `"path": "urn:...:Custom:costCentre"` — is
  answered 400 `invalidPath`. Only `remove` naming the whole extension URI is handled
  (`Core2EnterpriseUserExtensions.TryRemoveExtension`), which clears everything under it.
- **`/Schemas` does not advertise it.** `IProvider.Schema` is a collection you supply; the
  library derives nothing from the dictionary.
- **Storage is yours.** Your provider persists the resource; the extension travels on it.

---

## Option B — a typed extension

### Step 1 — declare the URI and the attribute names

Constants, so the schema URI, the `[DataMember]` names, the `PATCH` routing and the
`/Schemas` payload cannot drift apart.

```csharp
public static class ExampleSchemaIdentifiers
{
    public const string UserExtension = "urn:ietf:params:scim:schemas:extension:Example:2.0:User";
}

public static class ExampleAttributeNames
{
    public const string CostCentre = "costCentre";
    public const string Building = "building";
}
```

See `SCIM.EduPass/Schemas/EduPassSchemaIdentifiers.cs`.

### Step 2 — the extension type

A plain `[DataContract]` holding the attributes. Make every member optional unless your own
specification says otherwise: `IsRequired = false, EmitDefaultValue = false` keeps an unset
attribute out of the response, which is what SCIM asks for.

```csharp
[DataContract]
public class ExampleUserExtension
{
    [DataMember(Name = ExampleAttributeNames.CostCentre, IsRequired = false, EmitDefaultValue = false)]
    public virtual string CostCentre { get; set; }

    [DataMember(Name = ExampleAttributeNames.Building, IsRequired = false, EmitDefaultValue = false)]
    public virtual string Building { get; set; }
}
```

### Step 3 — the user type

Derive from `Core2EnterpriseUser`, **not** from `Core2EnterpriseUserBase`: the enterprise
`PATCH` semantics live in `Core2EnterpriseUserExtensions`, which are extension methods on
that concrete type and bind statically.

```csharp
[DataContract]
public class ExampleUser : Core2EnterpriseUser
{
    public ExampleUser()
    {
        this.AddSchema(ExampleSchemaIdentifiers.UserExtension);
        this.ExampleExtension = new ExampleUserExtension();
    }

    // Schematized.OnDeserializing resets the schema list, so the constructor's URI is
    // discarded and replaced by the request's own schemas array. Re-add it, or a request
    // that omits the URI produces a response not declaring the extension it carries.
    [OnDeserialized]
    private void OnDeserialized(StreamingContext context)
    {
        this.AddSchema(ExampleSchemaIdentifiers.UserExtension);
        this.ExampleExtension = this.ExampleExtension ?? new ExampleUserExtension();
    }

    [DataMember(Name = ExampleSchemaIdentifiers.UserExtension, IsRequired = false, EmitDefaultValue = false)]
    public virtual ExampleUserExtension ExampleExtension { get; set; }
}
```

The `[DataMember]` name *is* the schema URI — that is what puts the extension object at the
top level of the JSON under its URI key. `SchematizedJsonConverter` skips a URI already
bound to a typed member, so the typed member and the untyped dictionary never collide.

See `SCIM.EduPass/Schemas/EduPassUser.cs`.

### Step 4 — route `PATCH` to the extension

#### The call flow

Where your code sits in the path a change request takes:

```text
  Client sends a change request
  PATCH /scim/Users/{id}
            |
            v
  [1] Users endpoint bound to your user type
      (registered by AddScim<ExampleUser>)
      Turns the body into a patch request object
            |
            v
  [2] Your provider  (UpdateAsync)
      Loads the stored user, copies it,
      calls  copy.Apply(request)
            |
            v
  [3] Apply  -  for each operation in the request
            |
            +--> Is this a "remove" whose path is just a schema name?
            |         yes -> clear that whole extension, next operation
            |         no  -> keep going
            v
  [4] Expand  -  turn one operation into
      one operation per single field:
            |
            |   "path": "<schema>:costCentre"          -> already one field
            |   "path": "<schema>", value is an object -> split per field
            |   no path, value keyed by schema name    -> split per field
            v
  [5] Route by the schema name on the path
            |
            +--> core user schema, or no schema  -> built-in field handling
            |
            +--> enterprise schema               -> built-in enterprise handling
            |
            +--> any other schema  ->  [6]
            v
  [6] TryPatchExtensionAttribute  (your override)
            |
            +--> returns true   -> field written on your extension object
            |
            +--> returns false  -> error: bad path, answered 400
                                   (this is what the base class always does,
                                    which is why the override is required)
            |
            v
  [7] Back in your provider
      Validate the copy, save it, return the updated user
```

Two things follow from it. Your override only ever sees one field at a time, whatever shape
the client sent, so a single `switch` on the field name is enough. And the base class always
returns false, so without the override every write to an extension field is rejected.

#### The override

The core patcher rejects a path qualified by a schema it does not know, so without this
override every `PATCH` naming your attributes answers 400 `invalidPath`. Override
`TryPatchExtensionAttribute` and return true for the paths you claim.

```csharp
protected override bool TryPatchExtensionAttribute(PatchOperation2 operation)
{
    if (!ExampleSchemaIdentifiers.UserExtension.Equals(
            operation?.Path?.SchemaIdentifier,
            StringComparison.OrdinalIgnoreCase))
    {
        return false;
    }

    this.ExampleExtension = this.ExampleExtension ?? new ExampleUserExtension();

    // RFC 7644 3.5.2.2: a remove clears the target.
    string value = OperationName.Remove == operation.Name
        ? null
        : operation.Value?.SingleOrDefault()?.Value;

    switch (operation.Path.AttributePath)
    {
        case ExampleAttributeNames.CostCentre:
            this.ExampleExtension.CostCentre = value;
            return true;

        case ExampleAttributeNames.Building:
            this.ExampleExtension.Building = value;
            return true;

        default:
            return false;   // 400 invalidPath, which is correct for an unknown attribute
    }
}
```

#### The three request shapes it has to cover

`Core2EnterpriseUserExtensions.Apply(PatchRequest2)` normalizes every shape into
per-attribute operations before your override sees them, so one `switch` on
`Path.AttributePath` covers all three:

| Request | What `ProtocolExtensions.Expand` does |
| --- | --- |
| `"path": "urn:...:Example:2.0:User:costCentre"` | passes it through as one operation, schema identifier and attribute path already split |
| `"path": "urn:...:Example:2.0:User"`, value an object of attributes | the path matches one of the resource's `schemas`, so the value object is split into one operation per attribute, each re-qualified with the URI |
| no `path`, value keyed by the schema URI | walks the object; a top-level member under the SCIM schema prefix opens a schema, and its members take the schema separator. `schemas` itself is skipped |

The second shape only expands when the URI is in the resource's own `schemas` list — that
is the only way to tell a bare schema URN from an attribute path, since both are
colon-separated. It is also why Step 3 re-adds the URI in `[OnDeserialized]`.

A per-attribute `remove` arrives here with no value, so `value` is null and the assignment
clears the attribute — that is the shape RFC 7644 section 3.5.2.2 asks for.

A `remove` naming the **whole** extension URI never reaches this method.
`Core2EnterpriseUserExtensions.TryRemoveExtension` answers it first, and it knows only two
targets: the enterprise extension, which it resets, and an entry in the untyped
`CustomExtension` dictionary, which it clears. A typed member of your own matches neither,
so the operation is reported as applied and nothing is cleared. If that shape matters to
you, clear the extension in your provider before calling `Apply`, or hold the extension in
the untyped dictionary instead.

#### A worked example: scalar, complex and multi-valued

Take an extension with one of each:

```csharp
[DataContract]
public class ExampleUserExtension
{
    [DataMember(Name = "costCentre", IsRequired = false, EmitDefaultValue = false)]
    public virtual string CostCentre { get; set; }

    [DataMember(Name = "location", IsRequired = false, EmitDefaultValue = false)]
    public virtual ExampleLocation Location { get; set; }

    [DataMember(Name = "tags", IsRequired = false, EmitDefaultValue = false)]
    public virtual IList<string> Tags { get; set; }
}

[DataContract]
public class ExampleLocation
{
    [DataMember(Name = "site", IsRequired = false, EmitDefaultValue = false)]
    public virtual string Site { get; set; }

    [DataMember(Name = "floor", IsRequired = false, EmitDefaultValue = false)]
    public virtual string Floor { get; set; }
}
```

The override reads three parts of the operation: `Path.AttributePath` for the attribute,
`Path.ValuePath` for a sub-attribute of a complex one, and `Path.SubAttributes` for the
filter in `tags[value eq "one"]`.

```csharp
protected override bool TryPatchExtensionAttribute(PatchOperation2 operation)
{
    if (!ExampleSchemaIdentifiers.UserExtension.Equals(
            operation?.Path?.SchemaIdentifier,
            StringComparison.OrdinalIgnoreCase))
    {
        return false;   // not ours; some other extension may claim it
    }

    bool removing = OperationName.Remove == operation.Name;
    string value = removing ? null : operation.Value?.SingleOrDefault()?.Value;

    this.ExampleExtension = this.ExampleExtension ?? new ExampleUserExtension();

    switch (operation.Path.AttributePath)
    {
        // "urn:...:Example:2.0:User:costCentre"
        case "costCentre":
            this.ExampleExtension.CostCentre = value;
            return true;

        // "urn:...:Example:2.0:User:location.site" - the sub-attribute is the value path.
        // A remove naming location alone drops the whole object.
        case "location":
            if (null == operation.Path.ValuePath)
            {
                if (!removing)
                {
                    return false;   // see the note below on object values
                }

                this.ExampleExtension.Location = null;
                return true;
            }

            ExampleLocation location = this.ExampleExtension.Location ?? new ExampleLocation();

            switch (operation.Path.ValuePath.AttributePath)
            {
                case "site":  location.Site = value; break;
                case "floor": location.Floor = value; break;
                default: return false;
            }

            this.ExampleExtension.Location = location;
            return true;

        // "urn:...:Example:2.0:User:tags", value [{ "value": "one" }]
        case "tags":
            IList<string> tags = this.ExampleExtension.Tags ?? new List<string>();

            // "tags[value eq \"one\"]" - remove just that entry.
            string filtered =
                operation.Path.SubAttributes?
                    .FirstOrDefault(item =>
                        string.Equals(item.AttributePath, "value", StringComparison.OrdinalIgnoreCase))?
                    .ComparisonValue;

            if (removing)
            {
                if (null == filtered)
                {
                    tags.Clear();
                }
                else
                {
                    tags.Remove(filtered);
                }
            }
            else
            {
                if (OperationName.Replace == operation.Name)
                {
                    tags.Clear();
                }

                foreach (OperationValue entry in operation.Value ?? Enumerable.Empty<OperationValue>())
                {
                    if (!string.IsNullOrWhiteSpace(entry.Value) && !tags.Contains(entry.Value))
                    {
                        tags.Add(entry.Value);
                    }
                }
            }

            this.ExampleExtension.Tags = tags;
            return true;

        default:
            return false;   // 400 invalidPath
    }
}
```

Requests it answers, all against `PATCH /scim/Users/{id}` with
`"schemas": ["urn:ietf:params:scim:schemas:api:messages:2.0:PatchOp"]`:

```jsonc
// a scalar
{ "op": "replace", "path": "urn:ietf:params:scim:schemas:extension:Example:2.0:User:costCentre",
  "value": "CC-2" }

// clearing it - no value, per RFC 7644 3.5.2.2
{ "op": "remove", "path": "urn:ietf:params:scim:schemas:extension:Example:2.0:User:costCentre" }

// a sub-attribute of a complex attribute
{ "op": "replace", "path": "urn:ietf:params:scim:schemas:extension:Example:2.0:User:location.site",
  "value": "SG" }

// one entry of a multi-valued attribute
{ "op": "add", "path": "urn:ietf:params:scim:schemas:extension:Example:2.0:User:tags",
  "value": [ { "value": "one" } ] }

{ "op": "remove", "path": "urn:ietf:params:scim:schemas:extension:Example:2.0:User:tags[value eq \"one\"]" }

// the extension named whole - expanded into one operation per attribute before it reaches you
{ "op": "replace", "path": "urn:ietf:params:scim:schemas:extension:Example:2.0:User",
  "value": { "costCentre": "CC-3", "location": { "site": "SG", "floor": "12" } } }

// no path at all - same expansion
{ "op": "add",
  "value": { "urn:ietf:params:scim:schemas:extension:Example:2.0:User": { "costCentre": "CC-4" } } }
```

One shape is **not** expanded: a path naming your complex attribute with an object value,
`"path": "...:User:location", "value": { "site": "SG" }`. `ProtocolExtensions.Expand` splits
an object value only for a path that names a schema, or for the core `name` attribute — its
`complexAttributes` list. Handle the dotted path and the schema-URI path above and every
conforming client is covered; Entra ID sends the dotted form.

### Step 5 — bind the endpoint to your type

A controller's generic parameter is its model-binding type, so `/Users` has to be bound to
your class or the extension is lost during model binding. Both legs suppress the built-in
`UsersController` for you.

ASP.NET Core (`Microsoft.SCIM.WebHostSample/Program.cs`):

```csharp
builder.Services.AddScim<ExampleUser>(new MyProvider(), pathPrefix);
```

ASP.NET 4.8 Web API (`Microsoft.SCIM.WebHostSample.Net48/Startup.cs`):

```csharp
ScimHttpConfiguration.Configure<ExampleUser>(httpConfiguration, serviceProvider, pathPrefix);
```

### Step 6 — handle the type in your provider

The provider receives `Resource`; switch on your type so the extension is validated and
persisted rather than silently ignored.

```csharp
public override async Task<Resource> CreateAsync(Resource resource, string correlationIdentifier)
{
    switch (resource)
    {
        case ExampleUser user:
            ExampleValidator.Validate(user);
            // ... persist
            return user;

        case Core2Group group:
            // ...
    }
}
```

Filtering and `attributes`/`excludedAttributes` projection over extension attributes are the
provider's to implement — the library parses the query, it does not know your store.

See `SCIM.EduPass/Provider/BaseEduPassScimProvider.cs`.

### Step 7 — advertise it on `/Schemas` and `/ResourceTypes`

The library derives nothing here: `IProvider.Schema` and `IProvider.ResourceTypes` are
collections your provider supplies, and `ProviderBase` returns empty ones. A client that
reads discovery to learn what you support sees only what you put there.

```csharp
public static TypeScheme CreateUserExtensionTypeScheme()
{
    TypeScheme scheme =
        new TypeScheme
        {
            Identifier = ExampleSchemaIdentifiers.UserExtension,
            Name = "ExampleUser",
            Description = "Example User Extension",
        };

    scheme.AddAttribute(
        new AttributeScheme(ExampleAttributeNames.CostCentre, AttributeDataType.@string, plural: false)
        {
            Description = "The cost centre the user is charged to.",
            Returned = Returned.@default,
        });

    return scheme;
}

public static Core2ResourceType CreateUserResourceType()
{
    Core2ResourceType resourceType =
        new Core2ResourceType
        {
            Identifier = Types.User,
            Endpoint = new Uri(ServiceConstants.SeparatorSegments + ProtocolConstants.PathUsers, UriKind.Relative),
            Schema = SchemaIdentifiers.Core2User,
        };

    resourceType.AddSchemaExtension(ExampleSchemaIdentifiers.UserExtension, required: true);
    return resourceType;
}
```

Then override both collections on the provider, replacing the base's own `User` entries
rather than appending to them:

```csharp
public override IReadOnlyCollection<TypeScheme> Schema =>
    base.Schema
        .Where(item => !string.Equals(item.Identifier, SchemaIdentifiers.Core2User, StringComparison.OrdinalIgnoreCase))
        .Concat(new[] { CreateUserTypeScheme(), CreateUserExtensionTypeScheme() })
        .ToArray();

public override IReadOnlyCollection<Core2ResourceType> ResourceTypes =>
    base.ResourceTypes
        .Where(item => !string.Equals(item.Identifier, Types.User, StringComparison.OrdinalIgnoreCase))
        .Concat(new[] { CreateUserResourceType() })
        .ToArray();
```

`/Schemas` should still carry the core `User` schema — a payload holding only the extension
says you support no core attribute at all, not even `userName`. Use `AddCanonicalValues` on
an `AttributeScheme` for a closed value set, and `AddSubAttribute` for a complex attribute.

See `SCIM.EduPass/Schemas/EduPassTypeSchemes.cs`.

### Step 8 — validate

SCIM canonical values are advisory, so the library treats them as free strings. Constraints
of your own — closed value sets, formats, lengths — go in the provider, thrown as a
`ScimTypedException` so the shared handler answers 400 with the right `scimType`:

```csharp
throw new ScimTypedException(
    HttpStatusCode.BadRequest,
    ScimTypes.InvalidValue,
    "'costCentre' must be one of: CC-1, CC-2.");
```

See `SCIM.EduPass/Schemas/EduPassValidator.cs`.

---

## Extending the enterprise extension

`ExtensionAttributeEnterpriseUser2` is sealed and `Core2EnterpriseUserBase.EnterpriseExtension`
is typed to it, so an attribute cannot be added to
`urn:ietf:params:scim:schemas:extension:enterprise:2.0:User` without changing the library —
and a non-standard attribute under the standard enterprise URI is not conformant anyway.
Declare your own extension URI instead, as above. A user can carry both: the enterprise
extension is untouched and keeps its own `PATCH` handling.

---

## Checklist

Common ways an extension half-works:

| Symptom | Cause |
| --- | --- |
| Extension accepted but dropped from the response | URI outside `urn:ietf:params:scim:schemas:extension:` and no typed member; or `/Users` still bound to the built-in controller |
| `PATCH` answers 400 `invalidPath` | `TryPatchExtensionAttribute` not overridden, or it does not claim that attribute path |
| Response `schemas` omits the extension URI | no `[OnDeserialized]` re-adding it — `OnDeserializing` clears the list |
| Client never sends the extension | `/Schemas` or `/ResourceTypes` does not advertise it |
| Attributes arrive but are not stored | the provider's `switch` has no case for the derived type |
| Core attributes disappear from `/Schemas` | the provider replaced the core `User` scheme instead of extending it |

## Tests

- `tests/integration/suites/schema-extensions.spec.ts` — the untyped path, on users and groups.
- `tests/integration/suites/edupass.spec.ts` — a typed extension end to end.

Run them as described in `tests/integration/README.md`.
