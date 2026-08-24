---
page_type: sample
languages:
- csharp
products:
- dotnet
- dotnetcore
description: SCIM provisioning reference code  
urlFragment: "update-this-to-unique-url-stub"
---

# SCIM Reference Code

<!-- 
Guidelines on README format: https://review.docs.microsoft.com/help/onboard/admin/samples/concepts/readme-template?branch=master

Guidance on onboarding samples to docs.microsoft.com/samples: https://review.docs.microsoft.com/help/onboard/admin/samples/process/onboarding?branch=master

Taxonomies for products and languages: https://review.docs.microsoft.com/new-hope/information-architecture/metadata/taxonomies?branch=master
-->

The reference code provided in this repository will help you get started building a [SCIM](https://docs.microsoft.com/azure/active-directory/manage-apps/use-scim-to-provision-users-and-groups) endpoint. It contains guidance on how to implement basic requirements for CRUD operations on a user and group object (also known as resources in SCIM) and optional features of the standard such as filtering and pagination. Use the repository **[Wiki](https://github.com/AzureAD/SCIMReferenceCode/wiki)** for guidance on how to use this reference.

> **[NOTE]**
> This code is intended to help you get started building your SCIM endpoint and is provided "AS IS." It is intended as a reference and there is no guarantee of it being actively maintained or supported. [Contributions](https://github.com/AzureAD/SCIMReferenceCode/wiki/Contributing-Overview) from the community are welcome to help build and maintain the repo.

## Capabilities 

|Endpoint|Description|
|---|---|
|/Users|**Perform CRUD operations on a user resource:** <br/> 1. Create <br/> 2. Update <br/> 3. Delete <br/> 4. Get <br/> 5. List <br/> 6. Filter|
|/Groups|**Perform CRUD operations on a group resource:** <br/> 1. Create <br/> 2. Update <br/> 3. Delete <br/> 4. Get <br/> 5. List <br/> 6. Filter |
|/Schemas|**Retrieve one or more supported schemas.**<br/>The set of attributes of a resource supported by each service provider can vary. (e.g. Service Provider A supports “name”, “title”, and “emails” while Service Provider B supports “name”, “title”, and “phoneNumbers” for users).|
|/ResourceTypes|**Retrieve supported resource types.**<br/>The number and types of resources supported by each service provider can vary. (e.g. Service Provider A supports users while Service Provider B supports users and groups).|
|/ServiceProviderConfig|**Retrieve service provider's SCIM configuration**<br/>The SCIM features supported by each service provider can vary. (e.g. Service Provider A supports Patch operations while Service Provider B supports Patch Operations and Schema Discovery).|

## Getting Started

The `Microsoft.SystemForCrossDomainIdentityManagement` project (assembly `Microsoft.SCIM`) contains the code base for building a SCIM API. It multi-targets **.NET Framework 4.8** and **.NET 10.0**, and one of two hosting projects sits on top of it depending on which stack you are running:

| Leg | Library TFM | Hosting project | Sample |
|---|---|---|---|
| ASP.NET Core Web API | `net10.0` | `Microsoft.SCIM.AspNetCore` | `Microsoft.SCIM.WebHostSample` |
| ASP.NET Web API 2 | `net48` | `Microsoft.SCIM.AspNet` | `Microsoft.SCIM.WebHostSample.Net48` |

Both legs expose identical SCIM wire behaviour - same routes, same status codes, same JSON bodies, same headers - because all request orchestration lives in the shared library (`ScimRequestHandler<T>`) and each hosting project only translates the result into its own framework's action result. `docs/scim-conformance.md` is the specification both are held to.

A step by step guide for starting up with the project can be found [here](https://github.com/AzureAD/SCIMReferenceCode/wiki).

### Choosing a leg

Pick **net10.0 / ASP.NET Core** unless you are constrained to .NET Framework - it is the supported, actively maintained stack. Pick **net48 / ASP.NET Web API 2** if you must host inside an existing .NET Framework application or IIS site that cannot move.

### Running the samples

Both samples are **HTTP-only development harnesses** with an in-memory provider. Neither enables HTTPS; see `docs/net48-hosting.md`.

```bash
# net10.0 - listens on http://localhost:5000
dotnet run --project Microsoft.SCIM.WebHostSample

# net48 - OWIN self-host, Windows only, listens on http://localhost:5000
#         (pass a URL as the first argument, or set SCIM_SAMPLE_URL, to change it)
dotnet run --project Microsoft.SCIM.WebHostSample.Net48
```

Both read `ASPNETCORE_ENVIRONMENT` and layer `appsettings.{environment}.json` over `appsettings.json`. Set it to `Development` for the local dev-token flow. Building the net48 projects requires Windows.

## Navigating the reference code

This reference code implements SCIM provisioning as a web API on two hosting stacks: ASP.NET Core MVC (net10.0) and ASP.NET Web API 2 (net48). The SCIM protocol and schema logic is shared; only the controller layer is per-stack. Inside `Microsoft.SystemForCrossDomainIdentityManagement` the three main folders are Schemas, Service, and Protocol.

1. The **Schemas** folder includes:
    * The models for the User and Group resources along with some abstract classes like Schematized for shared functionality.
    * An Attributes folder which contains the class definitions for complex attributes of Users and Groups such as addresses.
2. The **Service** folder contains logic for actions relating to the way resources are queried and updated.
    * The reference code has services to return users and groups.
    * `ScimRequestHandler<T>` and `ScimDiscoveryRequestHandler` hold all SCIM request orchestration - argument validation, provider calls, exception-to-status mapping and monitor reporting - and return a hosting-neutral `ScimResult`. This is what keeps the two hosting legs behaving identically.
    * `Compat/WebApiCompat.cs` vendors `System.Web.Http.HttpResponseException` for the net10.0 leg only; on net48 the real type comes from `System.Web.Http.dll`. See `docs/net48-hosting.md`.
    * The **controllers** live in the hosting projects, not here: `Microsoft.SCIM.AspNetCore/Controllers` and `Microsoft.SCIM.AspNet/Controllers`. Resource controllers expose the HTTP verbs for CRUD on a resource (GET, POST, PUT, PATCH, DELETE) and delegate straight to the shared handlers.
3. The **Protocol** folder contains logic for actions relating to the way resources are returned according to the SCIM RFC such as:
    * Returning multiple resources as a list.
    * Returning only specific resources based on a filter.
    * Turning a query into a list of linked lists of single filters.
    * Turning a PATCH request into an operation with attributes pertaining to the value path. 
    * Defining the type of operation that can be used to apply changes to resource objects.

### Contents

| File/folder       | Description                                |
|-------------------|--------------------------------------------|
| `Microsoft.SystemForCrossDomainIdentityManagement`| The SCIM library (assembly `Microsoft.SCIM`), multi-targeting `net48` and `net10.0`.|
| `Microsoft.SCIM.AspNetCore`| ASP.NET Core Web API hosting layer for the library (`net10.0`).|
| `Microsoft.SCIM.AspNet`| ASP.NET Web API 2 hosting layer for the library (`net48`).|
| `Microsoft.SCIM.WebHostSample`| Sample implementation on ASP.NET Core (`net10.0`).|
| `Microsoft.SCIM.WebHostSample.Net48`| Sample implementation on ASP.NET Web API 2, OWIN self-hosted (`net48`).|
| `docs/scim-conformance.md`| The RFC-derived specification both hosting legs are verified against.|
| `docs/net48-hosting.md`| Hosting the library on .NET Framework: TLS, IIS, signing, the compat shim.|
| `.gitignore`      | Define what to ignore at commit time.      |
| `CHANGELOG.md`    | List of changes to the sample.             |
| `CONTRIBUTING.md` | Guidelines for contributing to the sample. |
| `README.md`       | This README file.                          |
| `LICENSE`         | The license for the sample.                |

## Authorization

The SCIM standard leaves authentication and authorization relatively open. You could use cookies, basic authentication, TLS client authentication, or any of the other methods listed [here](https://tools.ietf.org/html/rfc7644#section-2). You should take into consideration security and industry best practices when choosing an authentication/authorization method. Avoid insecure methods such as username and password in favor of more secure methods such as OAuth. Azure AD supports long-lived bearer tokens (for gallery and non-gallery applications) as well as the OAuth authorization grant (for applications published in the app gallery). Review the [wiki](https://github.com/AzureAD/SCIMReferenceCode/wiki/Authorization) for more details about the current authorization support that this reference code provides.   

> **[NOTE]**
> These authorization methods provided by this repo are solely for testing. When integrating with Azure AD, review the authorization guidance provided [here](https://docs.microsoft.com/azure/active-directory/app-provisioning/use-scim-to-provision-users-and-groups#authorization-for-provisioning-connectors-in-the-application-gallery). 

> **⚠️ [DO NOT USE IN PRODUCTION]**
> **This applies to both samples.** The dev-mode `TokenValidationParameters` - in `Program.cs.ConfigureJwtBearerOptons` on the net10.0 sample and in `Startup.cs.ConfigureAuthentication` on the net48 sample - disable every JWT validation check (issuer, audience, lifetime, signing key). Both are guarded by `#if DEBUG` so Release builds physically cannot ship the bypass, and both samples print a DEV-ONLY banner at startup saying so. The signing key is a dummy committed to this repository, so anyone reading it can mint a token the samples accept.
>
> **Neither sample enables HTTPS.** They are HTTP-only development harnesses, deliberately, so the two hosting legs can be compared like-for-like. TLS is the host's responsibility - see `docs/net48-hosting.md`.
>
> **Before deploying any SCIM endpoint derived from either sample to a non-sample environment**, delete the dev-mode branch, wire a properly authenticated, audience-scoped OAuth issuer, and terminate TLS.

### Getting a token for the samples

The samples no longer ship a `/scim/token` endpoint - it was an anonymously reachable JWT issuer, which is not something a reference implementation should teach. Mint a development token yourself from the dummy key in `appsettings.Development.json`:

```bash
python -c "
import base64, hmac, hashlib, json, time
key = b'A1B2C3D4E5F6A1B2C3D4E5F6A1B2C3D4'
def seg(d): return base64.urlsafe_b64encode(json.dumps(d, separators=(',',':')).encode()).rstrip(b'=')
body = seg({'alg':'HS256','typ':'JWT'}) + b'.' + seg({
    'iss':'Microsoft.Security.Bearer', 'aud':'Microsoft.Security.Bearer',
    'nbf':int(time.time()), 'exp':int(time.time())+7200})
print((body + b'.' + base64.urlsafe_b64encode(hmac.new(key, body, hashlib.sha256).digest()).rstrip(b'=')).decode())
"
```

Paste the result into Postman's `{{token}}` variable, or send it as `Authorization: Bearer <token>`. For a real endpoint, use a real OAuth authority instead - `Anacle.ApiFramework.Authentication` wires one for both hosting legs, see `docs/edupass-integration.md` §1.


## Contributing to the reference code

This project welcomes contributions and suggestions! Like other open source contributions, you will need to agree to a Contributor License Agreement (CLA) declaring that you have the right to, and actually do, grant us the rights to use your contribution. For details, visit https://cla.opensource.microsoft.com.

When submitting a pull request, a CLA bot will automatically determine whether you need to provide a CLA and decorate the PR appropriately (e.g. status check, comment). Simply follow the instructions provided by the bot. You will only need to do this once across all repos using our CLA.

This project has adopted the [Microsoft Open Source Code of Conduct](https://opensource.microsoft.com/codeofconduct/). For more information see the [Code of Conduct FAQ](https://opensource.microsoft.com/codeofconduct/faq/) or contact [opencode@microsoft.com](mailto:opencode@microsoft.com) with any additional questions or comments.

## Trademarks 
This project may contain trademarks or logos for projects, products, or services. Authorized use of Microsoft trademarks or logos is subject to and must follow Microsoft’s Trademark & Brand Guidelines. Use of Microsoft trademarks or logos in modified versions of this project must not cause confusion or imply Microsoft sponsorship. Any use of third-party trademarks or logos are subject to those third-party’s policies.
