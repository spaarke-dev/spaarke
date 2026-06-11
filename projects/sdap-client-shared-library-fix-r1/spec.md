# sdap-client-shared-library-fix-r1 — Spec

> **Status**: Backlog (queued behind `spaarke-multi-container-multi-index-r1`)
> **Created**: 2026-06-08
> **Origin**: Architectural audit during `spaarke-multi-container-multi-index-r1` (file-indexing fix). The follow-on landed `IndexFileOperation` in `@spaarke/sdap-client` and a `TokenProvider` adapter bridge — surfacing the broader misfit that warrants this project.

---

## 1. Problem Statement

`@spaarke/sdap-client` was designed (October 2025) to be the canonical TypeScript library for BFF API operations across PCFs, code pages, Office Add-ins, and any web SPA. It was built, tested, documented — then **never integrated**. Wizards and code pages instead rolled their own BFF API clients inside `@spaarke/ui-components/services/`, producing 17 service files (most unrelated to UI) and ~300-400 lines of duplicated HTTP/auth logic per consumer.

Consequence today:
- **`@spaarke/ui-components` is a misfit bucket** — houses Dataverse clients, BFF clients, configuration services, commands, telemetry. Only ~5 of 22 service files are actually UI-related (`ColumnRenderer`, `SprkChatBridge`, etc.).
- **`@spaarke/sdap-client` is orphaned** — built but unused. Its own `TokenProvider` predates ADR-028 (Spaarke Auth v2) and doesn't integrate with `@spaarke/auth`.
- **Duplicated HTTP/auth logic** scattered across PCFs, code pages, wizards. Each consumer re-implements `fetch + Bearer token + error handling`.
- **No dedicated Dataverse-client library** — Dataverse logic (FetchXml, views, fields, polymorphic resolvers, privilege checks) lives in `ui-components/services/`.

## 2. Scope

### 2.1 Migrate canonical operations into `@spaarke/sdap-client`

Move from `@spaarke/ui-components/services/` → `@spaarke/sdap-client`:

| From | To | Notes |
|---|---|---|
| `EntityCreationService.uploadFilesToSpe` | `sdap-client.UploadOperation` (already exists) | Migrate consumers; delete from ui-components |
| `BffDataverseClient` | `sdap-client/operations/DataverseOperation.ts` (new) | Or move to new `@spaarke/dataverse-client` |
| `ConfigurationService` (BFF config endpoints) | `sdap-client/operations/ConfigOperation.ts` (new) | |
| AppInsightsService (BFF telemetry calls) | `sdap-client/operations/TelemetryOperation.ts` (new) | Or new `@spaarke/telemetry` |

### 2.2 Replace `sdap-client`'s `TokenProvider` with `@spaarke/auth`

- Retire `Spaarke.SdapClient/src/auth/TokenProvider.ts`
- All operations accept an injected `authenticatedFetch` from `@spaarke/auth` (or the v2 `useAuth().getAccessToken()` contract)
- Conforms to ADR-028 invariants INV-1..INV-8
- Removes the temporary adapter shim added in tonight's session

### 2.3 Extract Dataverse-specific services into a new shared library

Create `@spaarke/dataverse-client`:
- `EntityConfigurationService`
- `EventTypeService`
- `FetchXmlService`
- `FieldMappingService`
- `FieldSecurityService`
- `IDataverseClient` (interface) + `XrmDataverseClient` (impl)
- `PolymorphicResolverService`
- `PrivilegeService` (or move auth-adjacent parts to `@spaarke/auth`)
- `ViewService`
- The Dataverse-record-creation portion of `EntityCreationService` (after BFF API portions extracted to `sdap-client`)

### 2.4 Extract command system into a new shared library

Create `@spaarke/commands` (or merge with existing):
- `CommandExecutor`
- `CommandRegistry`
- `CustomCommandFactory`

### 2.5 Reduce `@spaarke/ui-components` to its name

After 2.1-2.4 the library contains only what its name promises:
- React components (DataGrid, FileUpload, wizard composables, dialogs)
- Hooks
- Fluent UI v9 wrappers
- Theme
- Formatters/utils that are UI-adjacent

## 3. Out of Scope

- Server-side library reorganization (Sprk.Bff.Api internal structure)
- Office Add-in adoption of `@spaarke/sdap-client` (separate effort — Office Add-in has its own constraints)
- PCF-by-PCF migration to `@spaarke/sdap-client` for non-upload operations (separate project; this one focuses on the **library structure**, consumers migrate at their own pace)

## 4. Success Criteria

- ✅ `@spaarke/sdap-client` consumes `@spaarke/auth` — no `TokenProvider` of its own
- ✅ `EntityCreationService.uploadFilesToSpe` is gone; all callers use `sdap-client.UploadOperation`
- ✅ New `@spaarke/dataverse-client` library exists, exports all Dataverse-adjacent services
- ✅ `@spaarke/ui-components/services/` contains ≤ 5 files, all UI-adjacent
- ✅ No regression in any consumer (5 Create wizards, DocumentUploadWizard, all PCFs, code pages)
- ✅ Migration documented; `@spaarke/sdap-client` README updated to reflect adopted status
- ✅ ADR added (or existing ADR-012 amended) codifying the library boundaries

## 5. Dependencies

- `spaarke-multi-container-multi-index-r1` must merge first (it lands the bridge adapter + `IndexFileOperation` that this project builds on)
- Need consumer audit — every PCF, code page, wizard that imports from `@spaarke/ui-components/services/`
- Coordinate with any active branches touching these services

## 6. Risk

- **Medium-high**: many consumers, each migration is a small change but the aggregate touches dozens of files
- **Mitigation**: phased migration — service-by-service, with adapter layers during transition; consumers can adopt at their own pace
- **Rollback**: keep old services in place as `@deprecated` re-exports during transition window

## 7. Estimated Effort

- 1-2 weeks of focused work
- ~50-80 files touched
- 5-10 commits / phase

## 8. References

- [ADR-012 — Shared UI Components](../../.claude/adr/ADR-012-shared-ui-components.md) — current scope (needs amendment)
- [ADR-028 — Spaarke Auth Architecture](../../.claude/adr/ADR-028-spaarke-auth-architecture.md) — auth contract sdap-client must conform to
- [`@spaarke/sdap-client` migration briefing](../../src/client/shared/Spaarke.SdapClient/SDAP-CLIENT-V2-FUTURE-PROJECT-BRIEFING.md) — original integration plan from October 2025
- [`@spaarke/sdap-client` integration guide](../../src/client/shared/Spaarke.SdapClient/SDAP-CLIENT-V2-INTEGRATION-GUIDE.md)
- This project's parent: [`spaarke-multi-container-multi-index-r1`](../spaarke-multi-container-multi-index-r1/) — the file-indexing fix that surfaced the misfit
