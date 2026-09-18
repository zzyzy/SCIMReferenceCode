# AGENTS.md

## Project structure

- Core libraries
	- Microsoft.SystemForCrossDomainIdentityManagement (core SCIM, RFC compliant, no vendor specific extensions)
	- Microsoft.SCIM.AspNet (add-ons for ASP.NET 4.8)
	- Microsoft.SCIM.AspNetCore (add-ons for ASP.NET Core)

- API host shared logic
	- Anacle.ApiFramework.Authentication

- Sample API host for testing 
	- Microsoft.SCIM.WebHostSample
	- Microsoft.SCIM.WebHostSample.IIS
	- Microsoft.SCIM.WebHostSample.Net48

- SCIM vendor extensions 
	- SCIM.EduPass (MOE EduPass SCIM extensions)

## Conventions

- Keep code comments short and concise
- Keep commit messages short and concise