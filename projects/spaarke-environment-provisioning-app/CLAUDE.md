# CLAUDE.md — spaarke-environment-provisioning-app

> **Project**: Dataverse Environment Registry + Registration Provisioning Refactor
> **Status**: In Progress
> **Branch**: `work/spaarke-environment-provisioning-app`

## What This Project Does

Creates `sprk_dataverseenvironment` as a reusable platform entity and refactors the registration approval flow to read environment config from Dataverse instead of `DemoProvisioningOptions` in appsettings.

## Applicable ADRs

| ADR | Constraint |
|-----|-----------|
| ADR-001 | Minimal API, BackgroundService, ProblemDetails |
| ADR-002 | No plugins — all logic in BFF API and ribbon JS |
| ADR-008 | Endpoint filters for admin auth on approve/reject |
| ADR-010 | Concrete DI, no interfaces, ≤15 registrations |
| ADR-019 | RFC 7807 ProblemDetails with correlationId |

## Key Patterns to Follow

| Pattern | Exemplar |
|---------|----------|
| Dataverse Web API client | `Services/Registration/RegistrationDataverseService.cs` |
| Endpoint definition | `.claude/patterns/api/endpoint-definition.md` |
| Service registration | `Infrastructure/DI/RegistrationModule.cs` |
| Error handling | `Infrastructure/Errors/ProblemDetailsHelper.cs` |
| Schema creation script | `scripts/Create-RegistrationRequestSchema.ps1` |
| Provisioning pipeline | `Services/Registration/DemoProvisioningService.cs` |

## MUST Rules

- MUST read environment config fresh from Dataverse on each approval (no caching)
- MUST NOT use plugins for any validation or defaults
- MUST NOT auto-populate default environment on registration requests
- MUST validate environment selected before approve (both JS and API)
- MUST return ProblemDetails with correlationId for all errors
- MUST create lookup via RelationshipDefinitions, not Attributes endpoint
- MUST NOT include DefaultValue in integer attribute definitions

## Data Model

### `sprk_dataverseenvironment` (new)
16 columns: sprk_name, sprk_environmenttype, sprk_dataverseurl, sprk_appid, sprk_description, sprk_isactive, sprk_isdefault, sprk_setupstatus, sprk_accountdomain, sprk_businessunitname, sprk_teamname, sprk_specontainerid, sprk_securitygroupid, sprk_defaultdurationdays, sprk_licenseconfigjson, sprk_adminemails

### `sprk_registrationrequest` (modified)
New lookup: `sprk_dataverseenvironmentid` → `sprk_dataverseenvironment`
Deprecated: `sprk_environment` text field (remove after migration)

## 🚨 MANDATORY: Task Execution Protocol

All tasks MUST be executed via the `task-execute` skill. DO NOT read POML files and implement manually. See root CLAUDE.md for the full protocol.
