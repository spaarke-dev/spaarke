# Implementation Plan — Spaarke Environment Provisioning App

> **Project**: spaarke-environment-provisioning-app
> **Created**: 2026-04-06
> **Phases**: 5

## Architecture Context

### Discovered Resources

| Type | Resource | Path |
|------|----------|------|
| ADR | ADR-001 Minimal API | `.claude/adr/ADR-001-minimal-api.md` |
| ADR | ADR-002 Thin Plugins | `.claude/adr/ADR-002-thin-plugins.md` |
| ADR | ADR-008 Endpoint Filters | `.claude/adr/ADR-008-endpoint-filters.md` |
| ADR | ADR-010 DI Minimalism | `.claude/adr/ADR-010-di-minimalism.md` |
| ADR | ADR-019 ProblemDetails | `.claude/adr/ADR-019-problemdetails.md` |
| Pattern | Endpoint Definition | `.claude/patterns/api/endpoint-definition.md` |
| Pattern | Service Registration | `.claude/patterns/api/service-registration.md` |
| Pattern | Error Handling | `.claude/patterns/api/error-handling.md` |
| Pattern | Provisioning Pipeline | `.claude/patterns/api/provisioning-pipeline.md` |
| Pattern | Dataverse Web API Client | `.claude/patterns/dataverse/web-api-client.md` |
| Pattern | Entity Operations | `.claude/patterns/dataverse/entity-operations.md` |
| Guide | Self-Service Registration | `docs/guides/SPAARKE-SELF-SERVICE-USER-REGISTRATION.md` |
| Guide | Azure Setup Registration | `docs/guides/AZURE-SETUP-SELF-SERVICE-REGISTRATION.md` |
| Guide | Dataverse Schema How-To | `docs/guides/DATAVERSE-HOW-TO-CREATE-UPDATE-SCHEMA.md` |
| Script | Registration Schema | `scripts/Create-RegistrationRequestSchema.ps1` |

### Design Decisions

1. **No caching** — Environment config read fresh from Dataverse on each approval (NFR-01)
2. **No plugins** — All validation in BFF API and ribbon JS (NFR-02, ADR-002)
3. **Concrete DI** — `DataverseEnvironmentService` registered as singleton, no interface (ADR-010)
4. **Lookup via RelationshipDefinitions** — Dataverse API requirement for lookup columns
5. **Admin explicit selection** — No default environment auto-population

## Phase Breakdown

### Phase 1: Dataverse Schema (Foundation)

**Goal**: Create the `sprk_dataverseenvironment` entity, form, views, sitemap entry, and add lookup to registration request.

| # | Task | Deliverable |
|---|------|-------------|
| 001 | Create `Create-DataverseEnvironmentSchema.ps1` script | PowerShell script creating entity + all columns |
| 002 | Run schema script against Dev environment | Entity exists in Dataverse |
| 003 | Create entity form (General, Connection, Provisioning, Notifications tabs) | MDA form usable |
| 004 | Create views (Active, All, Ready for Provisioning) | MDA views visible |
| 005 | Add sitemap entry for environment entity | Navigable from MDA |
| 006 | Add `sprk_dataverseenvironmentid` lookup to `sprk_registrationrequest` | Lookup on registration form |
| 007 | Seed Dev and Demo 1 environment records | Two records with current config values |

### Phase 2: BFF API (Core Logic)

**Goal**: Create `DataverseEnvironmentService` and refactor approval endpoint to read from Dataverse.

| # | Task | Deliverable |
|---|------|-------------|
| 010 | Create `DataverseEnvironmentRecord` DTO and mapping | New model class |
| 011 | Create `DataverseEnvironmentService` | New service reading environments via Web API |
| 012 | Register service in `RegistrationModule.cs` | DI wired up |
| 013 | Refactor `ApproveRequest` to read environment from Dataverse lookup | Endpoint uses new service |
| 014 | Add 400 response when no environment linked | ProblemDetails for missing env |
| 015 | Handle malformed license JSON with clear error | ProblemDetails for bad JSON |
| 016 | Refactor `DemoProvisioningService` to accept environment config parameter | Service no longer reads from options |
| 017 | Update `SubmitDemoRequest` for admin email from environment record | Email notification uses env config |

### Phase 3: Ribbon JS (Client)

**Goal**: Remove environment picker dialog, add lookup validation, update approve calls.

| # | Task | Deliverable |
|---|------|-------------|
| 020 | Remove `_sprkReg_promptForEnvironment()` and environment config | Picker dialog removed |
| 021 | Add lookup validation before form approve | Alert if no environment selected |
| 022 | Add per-record validation for grid approve | Error message per record |
| 023 | Update API call to remove environment from request body | Body no longer includes environment name |

### Phase 4: Migration & Cleanup

**Goal**: Remove old config, update docs, verify backward compatibility.

| # | Task | Deliverable |
|---|------|-------------|
| 030 | Remove `Environments` array and `DefaultEnvironment` from `DemoProvisioningOptions` | Config cleaned |
| 031 | Remove environment-related appsettings entries | Appsettings simplified |
| 032 | Update `RegistrationDataverseService` URL resolution | Service reads URL from environment record |
| 033 | Update registration guide documentation | Docs reflect new workflow |
| 034 | Update Azure setup guide documentation | Docs include environment entity setup |

### Phase 5: Wrap-up

| # | Task | Deliverable |
|---|------|-------------|
| 040 | End-to-end testing checklist | All success criteria verified |
| 090 | Project wrap-up | README status updated, lessons-learned |

## Dependencies

```
001 → 002 → 003,004,005 (parallel) → 006 → 007
010,011 (parallel) → 012 → 013,014,015 (parallel) → 016 → 017
Phase 2 depends on Phase 1 (entity must exist)
020,021,022,023 can run in parallel (separate functions in same file)
Phase 3 depends on Phase 2 (API contract change)
Phase 4 depends on Phase 2+3 complete
```

## Parallel Execution Groups

| Group | Tasks | Prerequisite | Notes |
|-------|-------|--------------|-------|
| A | 003, 004, 005 | 002 complete | Independent Dataverse customizations |
| B | 010, 011 | Phase 1 complete | DTO and service can be written in parallel |
| C | 013, 014, 015 | 012 complete | Independent endpoint changes |
| D | 020, 021, 022, 023 | Phase 2 complete | Separate JS functions |
| E | 030, 031, 032 | Phase 3 complete | Independent cleanup tasks |
| F | 033, 034 | 032 complete | Documentation updates |

## Risk Items

1. **Dataverse URL resolution** — `RegistrationDataverseService` currently hardwires to default env URL at construction. Must refactor to accept URL per-call or per-environment.
2. **DemoExpirationService** — Has existing DI issue (commented out). Migration must not break this further.
3. **Lookup creation** — Must use `RelationshipDefinitions` endpoint, not `Attributes`. Integer attributes must not include `DefaultValue`.
