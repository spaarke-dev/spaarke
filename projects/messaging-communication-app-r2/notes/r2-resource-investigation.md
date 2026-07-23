# R2 Resource Investigation — findings archive (2026-07-18)

Five parallel read-only audits to ground `design.md` and prevent duplication. Key file paths + reuse/duplication guidance below.

## 1. Regarding + thread model
- **RegardingResolver PCF** (`src/client/pcf/RegardingResolver/`) is entity-agnostic (`entity` prop, FR-22) — attaches to any form, **zero code change**. Writes typed `sprk_regarding*` lookups + denormalized set via shared `applyResolverFields` / `PolymorphicResolverService.ts`.
- **`RegardingFieldMap.cs`** (`Services/Communication/Engine/`) = the 11-entity → typed-lookup map (`sprk_matter→sprk_regardingmatter`, … `contact→sprk_regardingperson`). `sprk_communication`'s regarding is written **server-side** by the association engine (`IncomingAssociationResolver.cs`, `CommunicationService.cs`), NOT the PCF.
- **⚠️ GOTCHA**: `sprk_regardingrecordtype` = **Lookup** on `sprk_communication` but **Text** on `sprk_communicationthread`. RegardingResolver needs a Lookup binding → thread needs a NEW Lookup discriminator field (don't reuse/retype the Text field — breaking).
- **Thread schema as-built** (`notes/messaging-schema-spec.md`, verified live): `sprk_name`, `sprk_threadtype`, `sprk_privacystate`, `sprk_privacyeffectivefrom`, denormalized regarding text quartet. **No typed lookups, no category/tag/description.** Naming = one-shot `ThreadResolver.BuildTopic()` (line 175) at create; `sprk_name` user-editable.
- **Do NOT**: reuse `CommunicationConnections` (AI-provenance-specific) for thread regarding; use Field Mapping Framework (client-wizard-only) for thread anchor (already done in `ThreadResolver.cs`); add a 2nd regarding mechanism (ADR-046 forbids).

## 2. VisualHost record-bound PCF
- Config entity **`sprk_chartdefinition`**; loader `src/client/pcf/VisualHost/control/services/ConfigurationLoader.ts`; dispatcher `ChartRenderer.tsx`. Matter Health/Budget/Tasks = pure config records (no code).
- Summary count/unread MetricCard = **config-only**. Thread-preview LIST w/ drill-through = **new self-managed visual type** (copy `DueDateCardList.tsx` pattern + presentational component in `@spaarke/visuals` + `VisualType` enum + `ChartRenderer` case).
- **⚠️ TENSION**: VisualHost containers fetch **client-side via `context.webAPI` FetchXML**, never the BFF. R1 reads are BFF-mediated for the `internal-only` access rule. Client-fetch honors Dataverse RLS natively (private threads hidden) but not `sprk_isinternalonly`. → Use VisualHost for count cards only; render content via the BFF-backed regarding-mode Timeline.
- Drill-through: `sprk_drillthroughtarget` → `handleExpandClick` (`VisualHostRoot.tsx` ~585-745).
- **Do NOT** rebuild a standalone Communications panel PCF — VisualHost provides config/chrome/drill/theme.

## 3. DataGrid framework — **Communications grid ALREADY BUILT**
- Config record `sprk_gridconfiguration` GUID **`e1826c4c-9575-f111-ab0e-7ced8ddc4a05`** ("Active Communications"), entity `sprk_communication` (from `ai-spaarke-ai-workspace-UI-r2`, spec at `projects/ai-spaarke-ai-workspace-UI-r2/notes/communications-config-record.md`).
- Framework: `src/client/shared/Spaarke.UI.Components/src/components/DataGrid/` (+ `DataGridPageShell.tsx`); config schema `types/DataGridConfiguration.ts` (v1.0); adapters `XrmDataverseClient` / `BffDataverseClient` (`services/IDataverseClient.ts`).
- **Views + filter chips auto-derived** (`filterChips/chipDiscovery.ts`): Choice→optionset, Lookup→lookup, DateTime→daterange. Channel (`sprk_communicationtype`), person (`sprk_sentby`, `sprk_regardingperson`), date (`sprk_sentat`), regarding all supported **OOB** — only appear if column is in the view's layoutXml.
- Standalone page = ~50-line shell (copy `src/solutions/sprk_invoicespage/src/main.tsx`); register in `scripts/Deploy-AllDataGridConsumers.ps1`.
- Advanced filters via `hostFilters` prop (`fetchXmlOverlay.ts`); order base→parentContext→hostFilters→chips.
- **Do NOT** build a new grid or create a colliding 2nd `sprk_isdefault` config for `sprk_communication`.

## 4. SpaarkeAi widget — **thin `communications-list` widget ALREADY EXISTS**
- Registered both wrappers: direct `communications-list` (`register-workspace-widgets.ts` ~729-744), section shim `src/solutions/LegalWorkspace/src/sections/communications.registration.ts`, `sectionRegistry.ts` line 120, `scripts/system-layouts.json` (sortOrder 6). Today = bare `<DataGrid>` via `DataverseEntityViewWidget` (no cards/filters/chrome).
- Rich upgrade = **Pattern D dual-use**, copy `src/client/shared/Spaarke.Events.Components/src/widgets/CalendarWorkspaceWidget/CalendarWorkspaceWidget.tsx` (filter-chip toolbar + card strip + embedded `<DataGrid hostFilters=… onRecordOpen=…>`). Contract: `WorkspaceWidgetComponent<TData>` (`Spaarke.AI.Widgets/src/types/widget-types.ts`); register via `registerWorkspaceWidget`.
- **Recommend new lib** `@spaarke/communication-components` (`src/client/shared/Spaarke.Communication.Components/`) — Events lib is entity-coupled; ai-widgets is thin-generic layer.
- **Do NOT**: add a 2nd widget (upgrade in place, keep type string `communications-list`); fork CalendarSection (already triplicated); forget the dual-deploy trap (rebuild LegalWorkspace + SpaarkeAi).
- No BFF changes for the widget (uses `Xrm.WebApi` via `XrmDataverseClient`).

## 5. Membership/person + read endpoints
- **Membership service** (`Services/Communication/Membership/ThreadMembershipDerivationService.cs`) = *record→authorized-users* (reverse ADR-034), write-side/per-thread. **Does NOT** answer person→communications. Reusable primitive: `ParticipantReference` (`Models/ParticipantReference.cs`).
- ADR-034 `MembershipResolverService` (`Services/Ai/Membership/`) + junction `sprk_userentityassociation` = person→**business records** (not communications).
- **⚠️ Participant data NOT queryable**: `sprk_from/to/cc` = `;`-separated TEXT. No `sprk_communicationparticipant` junction. `ParticipantCorrelationRung.cs` resolves email→contact→memberships at capture time but persists nothing queryable.
- **Person filter needs a NEW junction** `sprk_communicationparticipant` (message/thread ↔ resolved systemuser/contact + role), populated at capture/send (reuse `QueryContactByEmailAsync`). Align w/ ADR-034 `(personId, personIdType)` tuple. Schema ADR + §10/§11 required. Interim: lookup-only (`sprk_sentby`, `sprk_regardingperson`).
- **Read endpoints**: existing surface is thread-id-scoped only (`CommunicationEndpoints.cs`). Extend `CommunicationThreadReadService` + `IImpersonatedCommunicationQuery` (entity-set-agnostic) + `ICommunicationAccessFilter` for `by-regarding` (thread lookup by regarding → message `or`-filter) + filtered `query` (thread/regarding/channel/date reuse; `participant=` blocked on junction).
- **⚠️ Do NOT** reintroduce membership-union on reads — retired 2026-07-16 (`notes/access-model-decision.md`); reads = impersonation + 2-rule filter only.

## Cross-cutting: coordinate with `ai-spaarke-ai-workspace-UI-r2`
That project owns the shipped Communications grid config + thin widget. R2 upgrades in place — confirm coordination before forking the config record or widget registration.
