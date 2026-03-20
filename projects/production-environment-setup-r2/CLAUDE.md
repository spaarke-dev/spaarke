# CLAUDE.md — Production Environment Setup R2

> Project-specific AI context for environment-agnostic configuration

## Project Summary
Make all Spaarke components environment-agnostic by removing hardcoded dev URLs/IDs and replacing them with runtime configuration resolution.

## Applicable ADRs
| ADR | Relevance |
|-----|-----------|
| ADR-001 | Minimal API patterns — use Options pattern for env config |
| ADR-006 | PCF vs Code Page distinction affects config resolution strategy |
| ADR-007 | SpeFileStore facade — no Graph SDK types outside facade |
| ADR-008 | Endpoint filters — relevant for parameterized endpoints |
| ADR-010 | DI minimalism — Options pattern with ValidateOnStart() |
| ADR-012 | Shared component library — no hardcoded URLs in shared code |
| ADR-021 | Fluent UI v9 — theming tokens not env-specific |
| ADR-022 | PCF platform libraries — React 16 PCF vs React 18 Code Pages |

## Key Architecture Decisions
1. **Runtime resolution via Dataverse Environment Variables** — Code pages query sprk_BffApiBaseUrl etc. at runtime via REST API
2. **resolveRuntimeConfig() in @spaarke/auth** — New shared function handles Dataverse query + caching
3. **MSAL Client ID from Xrm context** — getGlobalContext().getClientUrl() available before auth
4. **Fail loudly** — Remove dev defaults; throw clear errors if config missing
5. **5 canonical values** — TenantId, BffApiUrl, BffApiAppId, MsalClientId, DataverseUrl

## File Inventory (What Changes)

### BFF API (C#)
- `src/server/api/Sprk.Bff.Api/Services/Office/OfficeDocumentPersistence.cs` — Remove hardcoded DataverseUrl + AppId
- `src/server/api/Sprk.Bff.Api/Services/Office/OfficeService.cs` — Remove hardcoded DataverseUrl (L1112) + share link URL (L983)
- `src/server/api/Sprk.Bff.Api/appsettings.template.json` — Add new tokens
- `src/server/api/Sprk.Bff.Api/appsettings.Production.json` — Fix CORS if present

### Shared Libraries (TypeScript)
- `src/client/shared/Spaarke.Auth/src/config.ts` — Remove DEFAULT_CLIENT_ID, DEFAULT_BFF_SCOPE, add resolveRuntimeConfig()
- `src/client/pcf/shared/utils/environmentVariables.ts` — Remove dev fallback defaults

### Code Pages (9 total)
- AnalysisWorkspace, PlaybookBuilder, SprkChatPane, LegalWorkspace, DocumentUploadWizard, SpeAdminApp, DocumentRelationshipViewer, SemanticSearch, External SPA

### PCF Controls (7+ total)
- UniversalQuickCreate, DocumentRelationshipViewer, SemanticSearchControl, RelatedDocumentCount, UniversalDatasetGrid, EmailProcessingMonitor, AssociationResolver, ScopeConfigEditor

### Legacy JS Webresources
- sprk_subgrid_parent_rollup.js, sprk_emailactions.js, sprk_DocumentOperations.js, sprk_communication_send.js, sprk_aichatcontextmap_ribbon.js

### Office Add-ins
- shared/auth/authConfig.ts, outlook/manifest.json

### Scripts (30+)
- Multiple Deploy-*.ps1, Check-*.ps1, debug/*.ps1 scripts with hardcoded spaarkedev1

## Parallel Execution Strategy
Tasks are designed for maximum parallelism using concurrent Claude Code agents:
- Phase 1 (Foundation): 3 parallel groups — BFF API, Infrastructure Scripts, Dataverse Solution
- Phase 2 (Shared Libs): Sequential — blocks Phase 3, 4, 5
- Phase 3 (Code Pages): ALL 9 code pages can migrate in parallel
- Phase 4 (PCF Controls): ALL 7+ controls can migrate in parallel
- Phase 5 (Legacy JS + Add-ins): ALL parallel
- Phase 6 (Validation): Sequential — depends on all prior phases

## 🚨 MANDATORY: Task Execution Protocol for Claude Code
When executing tasks in this project, Claude Code MUST invoke the task-execute skill. See root CLAUDE.md for complete protocol.
