# Dataverse Environments & Registration Provisioning — AI Implementation Specification

> **Status**: Ready for Implementation
> **Created**: 2026-04-06
> **Source**: design.md

## Executive Summary

Create a reusable `sprk_dataverseenvironment` entity in Dataverse that serves as the platform's central environment registry. The self-service registration system is the first consumer — a standard lookup field on the registration request form lets the admin explicitly select the target environment before approving. This replaces the current hardcoded environment picker in ribbon JS and Azure App Service config.

## Scope

### In Scope
- New Dataverse entity: `sprk_dataverseenvironment` (reusable platform entity)
- Entity form, views, and sitemap entry in the MDA
- Lookup field on `sprk_registrationrequest` → `sprk_dataverseenvironment` (replaces `sprk_environment` text field)
- BFF API refactoring: read environment config from Dataverse at approval time
- Ribbon JS update: remove environment picker dialog, validate lookup is set before approve
- Grid approve: fail with message if environment not selected (no default fallback)
- Migration of existing Dev environment config into the new entity
- Seed data for Dev and Demo 1 environments

### Out of Scope
- Automated Azure resource provisioning (Entra groups, Conditional Access, SPE containers) — manual setup per Azure Setup Guide
- Default environment auto-population — admin must always explicitly select
- Lookup filtering by environment type (future enhancement)
- Dataverse plugins or business rules (no plugins in this project)
- Multi-tenant support
- Environment health monitoring or connectivity testing
- Registration form changes (public website form is environment-agnostic)

### Affected Areas
- `src/server/api/Sprk.Bff.Api/Services/Registration/` — provisioning service refactored to read from Dataverse
- `src/server/api/Sprk.Bff.Api/Endpoints/RegistrationEndpoints.cs` — approve endpoint reads environment from lookup
- `src/server/api/Sprk.Bff.Api/Configuration/DemoProvisioningOptions.cs` — `Environments` array removed
- `src/client/webresources/js/sprk_registrationribbon.js` — environment picker removed, lookup validation added
- `scripts/` — new schema creation script for `sprk_dataverseenvironment`

## Requirements

### Functional Requirements

1. **FR-01**: Create `sprk_dataverseenvironment` entity with all fields defined in the data model — Acceptance: Entity exists in dev and demo Dataverse with correct schema
2. **FR-02**: Create MDA form with General, Connection, Provisioning, and Notifications tabs — Acceptance: Admin can create/edit environment records from the MDA
3. **FR-03**: Create Active Environments, All Environments, and Ready for Provisioning views — Acceptance: Views display correct columns and filters
4. **FR-04**: Add `sprk_dataverseenvironmentid` lookup field to `sprk_registrationrequest` — Acceptance: Lookup appears on registration request form, links to environment entity
5. **FR-05**: Registration request lookup defaults to blank (no default environment) — Acceptance: New registration requests have no environment pre-selected
6. **FR-06**: Ribbon JS approve (form) validates that environment lookup is set before proceeding — Acceptance: If lookup is empty, show alert "Please select a Target Environment before approving" and stop
7. **FR-07**: Ribbon JS approve (grid) fails individual records that have no environment set — Acceptance: Error message per record: "{Name}: No target environment selected"
8. **FR-08**: BFF API approve endpoint reads environment config from Dataverse using the lookup ID — Acceptance: Provisioning uses config from the linked environment record
9. **FR-09**: BFF API approve endpoint returns 400 if no environment is linked to the request — Acceptance: ProblemDetails response with clear message
10. **FR-10**: New `DataverseEnvironmentService` reads environment records from Dataverse via Web API — Acceptance: Service returns full config by record ID
11. **FR-11**: License configuration stored as JSON in `sprk_licenseconfigjson` field per environment — Acceptance: Different environments can have different license SKU sets
12. **FR-12**: If license JSON is malformed, provisioning fails with a clear error message — Acceptance: No silent failures; ProblemDetails includes "Invalid license configuration" detail
13. **FR-13**: Remove `DemoProvisioning.Environments` array and `DefaultEnvironment` from appsettings — Acceptance: All environment config comes from Dataverse records
14. **FR-14**: Seed Dev and Demo 1 environment records with current config values — Acceptance: Existing provisioning flow works unchanged after migration

### Non-Functional Requirements
- **NFR-01**: Environment config is read from Dataverse at approval time (not cached) — ensures admin changes take effect immediately
- **NFR-02**: No Dataverse plugins in this project — validation happens in the BFF API and ribbon JS only
- **NFR-03**: Entity security restricted to admin security role — non-admins cannot view/edit environment records

## Technical Constraints

### Applicable ADRs
- **ADR-001**: Minimal API pattern for any new endpoints
- **ADR-002**: No plugins — all logic in BFF API and client JS
- **ADR-008**: Endpoint filters for admin authorization on approve/reject
- **ADR-010**: DI minimalism — `DataverseEnvironmentService` registered as concrete
- **ADR-019**: ProblemDetails for all API errors

### MUST Rules
- ✅ MUST use Dataverse Web API (S2S) to read environment records — existing `RegistrationDataverseService` pattern
- ✅ MUST NOT use Dataverse plugins for validation or defaults
- ✅ MUST NOT auto-populate default environment — admin explicitly selects
- ✅ MUST validate environment is selected before approve (both JS and API)
- ✅ MUST return ProblemDetails with correlation ID for all errors
- ❌ MUST NOT cache environment config — read fresh on each approval

### Existing Patterns to Follow
- See `src/server/api/Sprk.Bff.Api/Services/Registration/RegistrationDataverseService.cs` for Dataverse Web API pattern
- See `src/server/api/Sprk.Bff.Api/Infrastructure/DI/RegistrationModule.cs` for DI registration pattern
- See `scripts/Create-RegistrationRequestSchema.ps1` for entity creation script pattern

## Data Model

### Entity: `sprk_dataverseenvironment`

| Schema Name | Display Name | Type | Required | Notes |
|-------------|-------------|------|----------|-------|
| `sprk_name` | Name | Text (200) | Yes | Primary name: "Demo 1", "Dev", "Partner Trial" |
| `sprk_environmenttype` | Environment Type | Choice | Yes | Development=0, Demo=1, Sandbox=2, Trial=3, Partner=4, Training=5, Production=6 |
| `sprk_dataverseurl` | Dataverse URL | URL | Yes | e.g., `https://spaarke-demo.crm.dynamics.com` |
| `sprk_appid` | App ID | Text (100) | No | MDA app GUID for deep links |
| `sprk_description` | Description | Multiline (2000) | No | Admin notes |
| `sprk_isactive` | Active | Boolean | Yes | Default: Yes |
| `sprk_isdefault` | Default Environment | Boolean | Yes | Default: No. Admin responsibility to manage |
| `sprk_setupstatus` | Setup Status | Choice | No | Not Started=0, In Progress=1, Ready=2, Issue=3 |
| `sprk_accountdomain` | Account Domain | Text (200) | No | UPN domain: `demo.spaarke.com` |
| `sprk_businessunitname` | Business Unit | Text (200) | No | Target Dataverse business unit |
| `sprk_teamname` | Security Team | Text (200) | No | Team with inherited security role |
| `sprk_specontainerid` | SPE Container ID | Text (500) | No | SharePoint Embedded container |
| `sprk_securitygroupid` | Users Security Group | Text (100) | No | Entra ID security group |
| `sprk_defaultdurationdays` | Default Duration (Days) | Integer | No | Default: 14 |
| `sprk_licenseconfigjson` | License Configuration | Multiline (4000) | No | JSON with license SKU IDs |
| `sprk_adminemails` | Admin Notification Emails | Multiline (1000) | No | Comma-separated |

### Registration Request: New Lookup

| Schema Name | Display Name | Type | Notes |
|-------------|-------------|------|-------|
| `sprk_dataverseenvironmentid` | Target Environment | Lookup → `sprk_dataverseenvironment` | Blank by default. Admin must select before approve. |

Replaces: `sprk_environment` (text field — to be removed after migration)

## Success Criteria

1. [ ] Create environment record in Dataverse — verify form layout and all fields
2. [ ] Open registration request — verify Target Environment lookup is blank by default
3. [ ] Approve without environment selected (form) — verify alert message blocks approval
4. [ ] Approve without environment selected (grid) — verify per-record error message
5. [ ] Select environment and approve — verify BFF reads config from Dataverse and provisions correctly
6. [ ] Deactivate environment — verify it no longer appears in lookup
7. [ ] Enter malformed license JSON — verify provisioning fails with clear error
8. [ ] Provision into Dev environment — verify existing flow works after migration
9. [ ] Provision into Demo 1 environment — verify correct config used (URL, team, domain, etc.)
10. [ ] Remove `DemoProvisioning__Environments__*` from Azure — verify system still works via Dataverse
11. [ ] Bulk approve from grid — verify each record uses its own lookup; records without environment fail individually

## Dependencies

### Prerequisites
- Dataverse Web API access via S2S auth (already in place)
- Registration request entity exists (already in place)
- BFF API deployed with current registration services (already in place)

### External Dependencies
- Admin must manually configure Azure resources (Entra group, Conditional Access, SPE container) per environment before marking Setup Status = Ready

## Owner Clarifications

| Topic | Question | Answer | Impact |
|-------|----------|--------|--------|
| License config | Store as JSON field or separate fields? | JSON field per environment | `sprk_licenseconfigjson` stores SKU IDs as JSON; different envs can have different licenses |
| Default enforcement | Enforce single default via plugin? | No — admin responsibility (Option B) | No plugins; `sprk_isdefault` is informational, not enforced programmatically |
| Default on create | Auto-populate default environment on new requests? | No — always blank, admin must explicitly select | Ribbon JS validates lookup is set; API returns 400 if missing |
| Bulk approve no env | Fail or use default for records missing environment? | Fail that record with message | Grid approve reports per-record error: "No target environment selected" |
| Lookup type filter | Filter lookup by environment type? | Out of scope — handle after project | Admin can select any active environment from lookup |
| Validation approach | Use plugins for validation? | No plugins in this project | All validation in BFF API (server) and ribbon JS (client) |

## Assumptions

- **License JSON format**: Assuming same structure as current appsettings: `{"PowerAppsPlan2TrialSkuId":"...","FabricFreeSkuId":"...","PowerAutomateFreeSkuId":"..."}`
- **Single tenant**: All environments are within the same Entra tenant (shared Graph API auth)
- **Entity security**: Admin-only access enforced via Dataverse security role (not a separate security model)

## Unresolved Questions

None — all blocking questions resolved via owner clarification.

---

*AI-optimized specification. Original: design.md*
