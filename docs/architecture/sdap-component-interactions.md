# Component Interactions Guide

> **Purpose**: Help AI coding agents understand how Spaarke components interact, so changes to one component can be evaluated for impact on others.
> **Last Updated**: December 8, 2025

---

## Quick Impact Reference

When modifying a component, check this table for potential downstream effects:

| If You Change... | Check Impact On... |
|------------------|-------------------|
| BFF API endpoints | PCF controls, tests, API documentation |
| BFF authentication | PCF auth config, Dataverse plugin auth |
| PCF control API calls | BFF endpoint contracts |
| Dataverse entity schema | BFF Dataverse queries, PCF form bindings |
| Bicep modules | Environment configs, deployment pipelines |
| Shared libraries | All consumers (search for ProjectReference) |

---

## Component Interaction Diagram

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                        USER INTERACTION LAYER                                │
│  ┌────────────────────────────────────────────────────────────────────────┐ │
│  │  Dataverse Model-Driven App (Browser)                                  │ │
│  │  └─ PCF Controls render in forms                                       │ │
│  └────────────────────────────────────────────────────────────────────────┘ │
└─────────────────────────────────────────────────────────────────────────────┘
                                    │
                    ┌───────────────┼───────────────┐
                    │               │               │
                    ▼               ▼               ▼
┌──────────────────────┐ ┌──────────────────┐ ┌──────────────────────────────┐
│  UniversalQuickCreate│ │  SpeFileViewer   │ │  UniversalDatasetGrid       │
│  PCF Control         │ │  PCF Control     │ │  PCF Control                │
│  ──────────────────  │ │  ──────────────  │ │  ────────────────────────── │
│  • Upload documents  │ │  • Preview files │ │  • Display entity records   │
│  • Extract metadata  │ │  • Office Online │ │  • Custom grid rendering    │
│  • Create records    │ │  • Download      │ │                             │
└──────────────────────┘ └──────────────────┘ └──────────────────────────────┘
           │                      │                        │
           │   MSAL.js Token      │                        │
           │   (OBO flow)         │                        │
           ▼                      ▼                        ▼
┌─────────────────────────────────────────────────────────────────────────────┐
│                          SPRK.BFF.API                                        │
│  ┌─────────────────────────────────────────────────────────────────────────┐│
│  │  Endpoints (Api/)                                                       ││
│  │  • POST /upload/file, /upload/session    ← Document uploads             ││
│  │  • GET  /api/containers/{id}/children    ← File listing                 ││
│  │  • GET  /api/documents/{id}/preview-url  ← Preview URLs                 ││
│  │  • GET  /api/navmap/{entity}/lookup      ← Metadata discovery           ││
│  └─────────────────────────────────────────────────────────────────────────┘│
│  ┌─────────────────────────────────────────────────────────────────────────┐│
│  │  Infrastructure Layer                                                   ││
│  │  • Auth/ — JWT validation, OBO token exchange                          ││
│  │  • Graph/ — ContainerOperations, DriveItemOperations, UploadManager    ││
│  │  • Dataverse/ — Web API client for metadata queries                    ││
│  │  • Resilience/ — Polly retry policies                                  ││
│  └─────────────────────────────────────────────────────────────────────────┘│
└─────────────────────────────────────────────────────────────────────────────┘
           │                                              │
           │  OBO Token                                   │  App Token
           │  (delegated)                                 │  (application)
           ▼                                              ▼
┌──────────────────────────────┐           ┌──────────────────────────────────┐
│  Microsoft Graph API         │           │  Dataverse Web API               │
│  ────────────────────────────│           │  ────────────────────────────────│
│  • FileStorageContainers     │           │  • sprk_document CRUD            │
│  • DriveItems (files)        │           │  • sprk_matter lookup            │
│  • Permissions               │           │  • Entity metadata               │
└──────────────────────────────┘           └──────────────────────────────────┘
           │                                              │
           ▼                                              ▼
┌──────────────────────────────┐           ┌──────────────────────────────────┐
│  SharePoint Embedded         │           │  Dataverse Tables                │
│  Container (SPE)             │           │  ──────────────────────────────  │
│  ────────────────────────────│           │  • sprk_document (metadata)      │
│  • File binary storage       │           │  • sprk_matter (parent record)   │
│  • Up to 250GB per container │           │  • sprk_documenttype (lookup)    │
│  • Versioning, permissions   │           │  • Custom entities               │
└──────────────────────────────┘           └──────────────────────────────────┘
```

---

## Interaction Patterns

### Pattern 1: Document Upload Flow

```
User → PCF (UniversalQuickCreate) → BFF API → Graph API → SPE Container
                                       │
                                       └──→ Dataverse API → sprk_document record
```

**Components involved:**
1. `src/client/pcf/UniversalQuickCreate/` — UI, file selection, metadata form
2. `src/server/api/Sprk.Bff.Api/Api/DocumentsEndpoints.cs` — Upload endpoints
3. `src/server/api/Sprk.Bff.Api/Infrastructure/Graph/UploadSessionManager.cs` — Chunked uploads
4. `src/server/api/Sprk.Bff.Api/Infrastructure/Dataverse/` — Metadata record creation

**Change Impact:**
| Change | Impact |
|--------|--------|
| Modify upload endpoint signature | Update PCF API client |
| Add upload validation | Add corresponding PCF-side validation |
| Change file size limits | Update both BFF config and PCF UI messaging |

### Pattern 2: Authentication Flow (OBO)

```
Dataverse Session → PCF (MSAL.js) → BFF API (validate) → OBO Exchange → Graph/Dataverse
```

**Components involved:**
1. `src/client/pcf/*/services/auth/msalConfig.ts` — MSAL configuration
2. `src/client/pcf/*/services/auth/MsalAuthProvider.ts` — Token acquisition
3. `src/server/api/Sprk.Bff.Api/Infrastructure/Auth/` — JWT validation
4. `src/server/api/Sprk.Bff.Api/Program.cs` — Auth middleware registration

**Change Impact:**
| Change | Impact |
|--------|--------|
| Modify API scopes | Update PCF msalConfig AND Entra app registration |
| Change token validation | All PCF controls affected |
| Add new authorization policy | Update endpoint decorators |

### Pattern 3: File Preview Flow

```
User → PCF (SpeFileViewer) → BFF API → Graph API → Preview URL
                                │
                                └──→ Redis Cache (URL caching)
```

**Components involved:**
1. `src/client/pcf/SpeFileViewer/` — Preview UI component
2. `src/server/api/Sprk.Bff.Api/Api/DocumentsEndpoints.cs` — Preview endpoint
3. `src/server/api/Sprk.Bff.Api/Infrastructure/Graph/DriveItemOperations.cs` — Graph calls

**Change Impact:**
| Change | Impact |
|--------|--------|
| Modify preview URL structure | Update SpeFileViewer iframe handling |
| Change caching TTL | Consider Graph API rate limits |
| Add new preview types | Update both BFF and PCF |

---

## Shared Dependencies

### Shared Configuration

| Config Key | Used By | Location |
|------------|---------|----------|
| `BffApiBaseUrl` | All PCF controls | PCF environment config |
| `ContainerTypeId` | Upload, listing endpoints | appsettings.json |
| `Redis:InstanceName` | BFF caching | appsettings.json |
| `AzureAd:*` | BFF auth, PCF MSAL | appsettings.json, msalConfig.ts |

### Shared Types/Contracts

| Contract | Producer | Consumers |
|----------|----------|-----------|
| `ContainerDto` | BFF API | PCF controls (TypeScript interface) |
| `FileHandleDto` | BFF API | SpeFileViewer, UniversalQuickCreate |
| `UploadSessionDto` | BFF API | UniversalQuickCreate |
| Policy names (`graph-write`, etc.) | BFF Program.cs | BFF endpoints |

**Rule:** When modifying a DTO in the BFF, update the corresponding TypeScript interface in PCF controls.

---

## Cross-Cutting Concerns

### Error Handling Chain

```
Graph ODataError → BFF ProblemDetailsHelper → HTTP Response → PCF Error Handler
```

| Layer | Error Handling |
|-------|----------------|
| BFF Infrastructure | Catch `ODataError`, map to `ProblemDetails` |
| BFF Endpoints | Return `IResult` with proper status codes |
| PCF Controls | Parse `ProblemDetails`, show user-friendly message |

**Change Impact:** Modifying `ProblemDetailsHelper` affects all PCF error displays.

### Telemetry/Logging

```
PCF (console) → BFF (Serilog + App Insights) → Azure Monitor
```

| Layer | Telemetry |
|-------|-----------|
| PCF Controls | Browser console, optional App Insights |
| BFF API | Serilog structured logging, OpenTelemetry metrics |
| Infrastructure | Azure Monitor, Log Analytics |

**Change Impact:** Adding new telemetry dimensions requires BFF and Azure configuration.

---

## Dependency Direction Rules

### Allowed Dependencies

```
PCF Controls ──────────→ BFF API (HTTP)
BFF API ───────────────→ Graph API (SDK)
BFF API ───────────────→ Dataverse API (HTTP)
BFF API ───────────────→ Azure Services (SDK)
Tests ─────────────────→ Any src/ code
```

### Prohibited Dependencies

```
BFF API ──────────✗────→ PCF Controls (no reverse dependency)
Graph Infrastructure ─✗─→ Dataverse Infrastructure (isolated)
src/ ─────────────✗────→ tests/ (no test code in production)
```

---

## Change Checklist by Component

### BFF API Endpoint Changes

- [ ] Update endpoint in `Api/*.cs`
- [ ] Update tests in `tests/unit/Sprk.Bff.Api.Tests/`
- [ ] Update PCF API client if contract changed
- [ ] Update API documentation/comments
- [ ] Verify rate limiting policies still apply

### PCF Control Changes

- [ ] Update control source in `src/client/pcf/{Control}/`
- [ ] Run `npm run build` in pcf directory
- [ ] Test in Dataverse environment
- [ ] Update control manifest if properties changed
- [ ] Increment version in `ControlManifest.Input.xml`

### Infrastructure (Bicep) Changes

- [ ] Update module in `infrastructure/bicep/modules/`
- [ ] Update stack references in `infrastructure/bicep/stacks/`
- [ ] Update parameter files if new parameters added
- [ ] Test with `az deployment group what-if`
- [ ] Update `AZURE-RESOURCE-NAMING-CONVENTION.md` if naming affected

### Dataverse Schema Changes

- [ ] Update solution in `src/dataverse/solutions/`
- [ ] Update BFF Dataverse queries if fields changed
- [ ] Update PCF form bindings if bound fields changed
- [ ] Update any related documentation

---

## Quick Lookup: Component Locations

| Component | Primary Location | Test Location |
|-----------|------------------|---------------|
| BFF Endpoints | `src/server/api/Sprk.Bff.Api/Api/` | `tests/unit/Sprk.Bff.Api.Tests/` |
| BFF Graph Operations | `src/server/api/Sprk.Bff.Api/Infrastructure/Graph/` | Same test project |
| BFF Auth | `src/server/api/Sprk.Bff.Api/Infrastructure/Auth/` | Same test project |
| PCF UniversalQuickCreate | `src/client/pcf/UniversalQuickCreate/` | Manual testing |
| PCF SpeFileViewer | `src/client/pcf/SpeFileViewer/` | Manual testing |
| PCF Shared Auth | `src/client/pcf/*/services/auth/` | — |
| Bicep Modules | `infrastructure/bicep/modules/` | `what-if` validation |
| Dataverse Plugins | `src/dataverse/plugins/` | `tests/unit/` |

---

## See Also

- `/docs/ai-knowledge/architecture/sdap-overview.md` — System overview
- `/docs/ai-knowledge/architecture/sdap-bff-api-patterns.md` — BFF patterns
- `/docs/ai-knowledge/architecture/sdap-pcf-patterns.md` — PCF patterns
- `/docs/reference/architecture/SPAARKE-REPOSITORY-ARCHITECTURE.md` — Full repo structure
