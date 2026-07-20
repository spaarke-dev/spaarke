# Communication Workspace — R2 — AI Implementation Specification

> **Status**: Ready for Implementation
> **Created**: 2026-07-18
> **Source**: `design.md` (investigation-grounded synthesis, 5-part resource audit 2026-07-18)
> **Follows**: `messaging-communication-app-r1` (shipped, merged, deployed to `spaarke-bff-dev`, archived)
> **Coordinates with**: `ai-spaarke-ai-workspace-UI-r2` (**Complete** — Q3 resolved; R2 upgrades its shipped grid config + widget in place)

---

## Executive Summary

R2 is the **read / query / organize layer** on top of R1's messaging channel. R1 shipped transport, capture, the thread data model, and a per-thread polling Timeline. R2 makes communications **findable and organized across records and people**: a record-level threads view on all 11 regarding-family entities, a standalone all-communications view, a rich workspace widget, thread regarding-resolution, a queryable participant index, an auto-threading policy, and a richer compose form. The R1 data model already supports "conversations related to a record", "multiple threads per record", and "email+chat unified" — so R2 is **mostly read surface + UI + two schema deltas**, not a schema migration.

## Scope

### In Scope
- **Surface 1 (NEW)** — Record-level **regarding-mode Timeline** PCF placed on **all 11** regarding-family entity forms; renders a record's threads as collapsible groups → per-thread message timelines (email + chat interleaved); backed by the new `by-regarding` BFF endpoint (inherits R1 impersonation access filter). Optional VisualHost count/summary card (config-only) for at-a-glance metrics.
- **Surface 2 (thin)** — Standalone **All-Communications page** (`src/solutions/sprk_communicationspage/`, ~50-line shell copying `sprk_invoicespage/src/main.tsx`); registered in `scripts/Deploy-AllDataGridConsumers.ps1`. Reuses the existing `sprk_gridconfiguration` GUID `e1826c4c-9575-f111-ab0e-7ced8ddc4a05` (no colliding second default). **No permanent sitemap entry in R2** (reach via widget/launcher/deep link — per owner Q-B).
- **Surface 3 (upgrade-in-place)** — Upgrade the existing thin `communications-list` workspace widget to a **rich Calendar-style Pattern D** widget (card strip + filter-chip toolbar + embedded `<DataGrid>` + row-click modal), copying `CalendarWorkspaceWidget.tsx`. New shared lib **`@spaarke/communication-components`**. Keep type string `communications-list` + section id `communications`.
- **CC-1** — Thread regarding treatment: add typed `sprk_regarding{...}` lookups (all 11) + a **new Lookup discriminator** field to `sprk_communicationthread`; place existing RegardingResolver PCF (0 code) on the thread form; thread-naming "re-derive unless user-edited" logic (needs an auto-vs-edited marker). **No category/tags** (Q2).
- **CC-2** — **`sprk_communicationparticipant` junction** at **message grain**: lookup to `sprk_communication` + **two typed nullable person lookups** (`sprk_systemuser`, `sprk_contact` — exactly one set) + role choice {From, To, Cc, **Bcc**}; unresolved external addresses write a row with **no person lookup + `sprk_addresstext` (raw email) + `sprk_isresolved=false`**. Populated at capture/send reusing `ParticipantCorrelationRung` email→contact resolution. Enables the exact `participant=` facet + external-party visibility. Thread-level participation derived by rollup (no thread-grain rows — per owner Q-A).
- **CC-3** — Read endpoints: `GET /api/communications/by-regarding/{entityType}/{id}` and filtered `GET /api/communications?thread=&regarding=&channel=&from=&to=&participant=`; both extend `CommunicationThreadReadService` + `IImpersonatedCommunicationQuery` + `ICommunicationAccessFilter` (the blessed R1 impersonation read path).
- **CC-4** — Auto-threading policy: 3-tier `IThreadResolver` (subject/conversation thread → per-record default thread → per-user master catch-all) + a `default-thread` marker so messages never orphan.
- **CC-5** — Compose-form enrichment: Subject/topic field, structured recipient picker (To/Cc/Bcc resolving to `contact`/`systemuser`), expose Cc/Bcc. Feeds CC-1 naming + CC-2 participant index at capture time. Reuse existing `RecipientField` component.

### Out of Scope
- **Category / tags / description** on threads (Q2 — revisit post-UAT). Threads organized by regarding + name only.
- **Thread-grain participant rows** (Q-A — message grain only; thread participation derived).
- **Permanent sitemap/app navigation** for the All-Communications page (Q-B — deferred to a later config pass).
- **A second reads access mechanism** or **membership-union on reads** (retired 2026-07-16 — reads stay impersonation + 2-rule filter).
- **A second `sprk_gridconfiguration` default** for `sprk_communication` (reuse `e1826c4c-…`).
- **A second workspace widget** (upgrade the existing `communications-list` in place).
- **Rebuilding** the DataGrid framework, VisualHost, RegardingResolver PCF, or the Timeline component — extend/reuse only.
- **Retyping** the thread's existing Text `sprk_regardingrecordtype` (breaking — add a new Lookup discriminator instead).
- New AI dependency, new external SDK, or new NuGet package (publish-size impact expected ≈0).

### Affected Areas
- `src/server/api/Sprk.Bff.Api/Services/Communication/CommunicationThreadReadService.cs` — add `ReadByRegardingAsync` + filtered query.
- `src/server/api/Sprk.Bff.Api/.../IImpersonatedCommunicationQuery.cs`, `Access/CommunicationAccessFilter.cs` — reuse (entity-set-agnostic).
- `src/server/api/Sprk.Bff.Api/.../CommunicationEndpoints.cs` — register `by-regarding` + `query` in the existing endpoint group.
- `src/server/api/Sprk.Bff.Api/Services/Communication/Engine/ThreadResolver.cs` — auto-threading policy + `BuildTopic` re-derive.
- `src/server/api/Sprk.Bff.Api/Services/Communication/**` — participant-index write at capture/send (reuse `ParticipantCorrelationRung.QueryContactByEmailAsync`).
- `src/client/shared/Spaarke.UI.Components/src/components/CommunicationTimeline/` — regarding mode.
- `src/client/pcf/CommunicationTimeline/` — regarding-mode PCF variant (mirror R1 pattern, add regarding-resolution path).
- `src/client/pcf/RegardingResolver/`, `src/client/pcf/VisualHost/` — placement/config only, no code change.
- `src/client/shared/Spaarke.Communication.Components/` (**NEW** lib `@spaarke/communication-components`) — rich widget content.
- `src/client/shared/Spaarke.AI.Widgets/src/widgets/workspace/register-workspace-widgets.ts` + `src/solutions/LegalWorkspace/src/sections/communications.registration.ts` — widget upgrade wiring (keep type string).
- `src/solutions/sprk_communicationspage/` (**NEW** shell) + `scripts/Deploy-AllDataGridConsumers.ps1`.
- Dataverse schema: `sprk_communicationthread` (typed regarding lookups ×11, Lookup discriminator, naming-edited marker, default-thread marker), `sprk_communicationparticipant` (**NEW** junction).
- Compose UI: `TimelineComposeBox` + `RecipientField` (Subject/Cc/Bcc/structured recipients).

## Requirements

### Functional Requirements

1. **FR-01 — by-regarding read endpoint.** `GET /api/communications/by-regarding/{entityType}/{id}` returns all threads + messages for a regarding record, entity-set-agnostic across all 11 families. **Acceptance**: extends `CommunicationThreadReadService`/`IImpersonatedCommunicationQuery`/`ICommunicationAccessFilter` (no new access logic); returns the same DTO shape as R1 reads; applies impersonation + 2-rule (privacy + internal-only) filter; seam test covers ≥3 of the 11 entity-sets and a private-thread-hidden case.
2. **FR-02 — filtered query endpoint.** `GET /api/communications?thread=&regarding=&channel=&from=&to=&participant=` returns a filtered communication list. **Acceptance**: thread/regarding/channel/date reuse the read path; `participant=` resolves against the `sprk_communicationparticipant` junction (FR-08); access filter applied; unknown/empty filters degrade gracefully; seam test per facet.
3. **FR-03 — regarding-mode Timeline component.** Extend `CommunicationTimeline` with a regarding mode taking `(entityType, id)`, rendering threads as collapsible groups each expanding to its interleaved email+chat timeline. **Acceptance**: reuses the R1 component rendering; calls `by-regarding` (FR-01); collapsed group shows thread name + message count; email+chat interleave by timestamp; no client-side access logic (server-filtered).
4. **FR-04 — regarding-mode Timeline PCF on all 11 forms.** A form-bound PCF variant (mirror R1 `CommunicationTimeline` PCF + regarding-resolution path) placed on all 11 regarding-family entity forms. **Acceptance**: resolves the host record's `(entityType, id)`; renders FR-03; deployed + form-placed on all 11; declares a bound `anchorField` (R1 lesson — a code component only appears in the form component library if it binds a field).
5. **FR-05 — VisualHost summary card (optional).** A `sprk_chartdefinition` config for a count/unread MetricCard on the record form, drill-through to the threads view. **Acceptance**: config-only (no code); shows count; drill-through targets Surface 1; **content is NOT rendered via VisualHost** (client-fetch honors RLS but not `internal-only` — T-1). Count-only.
6. **FR-06 — thread typed-regarding lookups + Lookup discriminator.** Add typed `sprk_regarding{...}` lookups (all 11, mirroring `RegardingFieldMap.All`) + a **new Lookup discriminator** field to `sprk_communicationthread`; keep the existing Text `sprk_regardingrecordtype` as the denormalized copy. **Acceptance**: non-breaking (ThreadResolver + membership derivation + timeline filters still read the Text field); RegardingResolver PCF binds the new Lookup; no retype of the Text field.
7. **FR-07 — RegardingResolver on the thread form + naming re-derive.** Place the existing RegardingResolver PCF (`entity="sprk_communicationthread"`, 0 code change) on the thread form; add "re-derive `sprk_name` unless user-edited" logic to `ThreadResolver.BuildTopic()` gated by a new auto-vs-edited marker field. **Acceptance**: resolver writes typed lookups + discriminator; name re-derives on regarding change only while the marker says "auto"; user edits set the marker to "edited" and are preserved.
8. **FR-08 — `sprk_communicationparticipant` junction (message grain).** New junction with concrete schema (locked 2026-07-18):
   - `sprk_communication` — Lookup → `sprk_communication` (**required**, message-grain parent).
   - `sprk_systemuser` — Lookup → `systemuser` (nullable).
   - `sprk_contact` — Lookup → `contact` (nullable). **Exactly one of `sprk_systemuser`/`sprk_contact` is set for a resolved person; both null for an unresolved external address.**
   - `sprk_role` — Choice: `From` / `To` / `Cc` / `Bcc`.
   - `sprk_addresstext` — Text (raw email; populated for unresolved external parties and retained for resolved ones as provenance).
   - `sprk_isresolved` — Boolean (false when no person lookup is set; enables back-fill when a `contact` is later created).
   - `sprk_name` — primary Text (e.g. `"{personDisplay|address} — {role}"`).

   Populated at capture/send reusing `ParticipantCorrelationRung.QueryContactByEmailAsync`. **Acceptance**: schema ADR authored (§11 + the ADR-034 path-C tension below); one row per (message × person/address × role); unresolved external addresses write a row (`sprk_isresolved=false`, `sprk_addresstext` set) so `participant=` still surfaces them; powers `participant=` (FR-02); thread-level participation derivable by rollup (no thread-grain rows).
9. **FR-09 — auto-threading policy.** 3-tier `IThreadResolver`: subject/conversation thread → per-record default thread (one per record) → per-user master catch-all; add a `default-thread` marker. **Acceptance**: every inbound/outbound message resolves to a non-null thread; per-record default created lazily; master is per-user; policy is a resolver change (no new access path); unit/seam coverage of all 3 tiers.
10. **FR-10 — compose-form enrichment.** Add Subject/topic, structured recipient picker (To/Cc/Bcc → resolved `contact`/`systemuser`), and expose Cc/Bcc to `TimelineComposeBox`. **Acceptance**: Subject populates `sprk_subject` + a meaningful `sprk_name` (no more "(No Subject)"); structured recipients drive the participant index (FR-08); free-text fallback retained for external addresses; reuses `RecipientField` (no new picker); `sprk_cc`/`sprk_bcc` (already in the DTO) are populated.
11. **FR-11 — standalone All-Communications page.** `src/solutions/sprk_communicationspage/` ~50-line shell copying `sprk_invoicespage/src/main.tsx`, bound to grid config `e1826c4c-…`; registered in `Deploy-AllDataGridConsumers.ps1`. **Acceptance**: renders the DataGrid with auto-derived channel/person/date/regarding chips; no second default config; **no permanent sitemap entry** (Q-B); reachable via widget/launcher/deep link.
12. **FR-12 — rich workspace widget upgrade (in place).** Upgrade `communications-list` to the rich Pattern D widget (copy `CalendarWorkspaceWidget.tsx`) in the new `@spaarke/communication-components` lib. **Acceptance**: keeps type string `communications-list` + section id `communications` (dispatch unbroken); card strip + filter-chip toolbar + embedded `<DataGrid hostFilters onRecordOpen>` + row-click modal; **dual-deploy** (rebuild LegalWorkspace + SpaarkeAi); no second widget registration.

### Non-Functional Requirements
- **NFR-01 — BFF publish size.** Every BFF-touching task verifies compressed publish size; R1 baseline ~46.99 MB, ceiling **≤60 MB**. No new package expected (target Δ ≈ 0). Report absolute + diff per BFF task.
- **NFR-02 — CVE gate.** No new HIGH-severity CVE from `dotnet list package --vulnerable --include-transitive` on BFF-touching tasks.
- **NFR-03 — Access parity.** All new read surfaces (FR-01/02/03/04) enforce R1's impersonation + 2-rule filter (privacy + internal-only). No message content may bypass the BFF access filter (rules out VisualHost content rendering — FR-05 is count-only).
- **NFR-04 — Non-breaking schema.** CC-1 additive only (new Lookup discriminator + typed lookups + markers); the existing Text `sprk_regardingrecordtype` retype is forbidden (would break ThreadResolver + membership + timeline filters).
- **NFR-05 — No duplication (§11).** No second grid config default, no second widget, no second access mechanism, no rebuilt framework component. Extend/reuse only.
- **NFR-06 — Notification-spine: reserve alignment, no hard dependency.** `spaarke-notification-spine-r1` is still in **draft design** (spine not built), so R2 takes **NO dependency** on it. R2 ships **BFF-polling** live updates (the R1 pattern) and only **reserves** the spine's `communication-arrived` kind + envelope contract (already carries `threadId` + `regardingRecordId` per spine design §5A.3, co-authored by messaging-r1). R2 MUST NOT build a parallel push/fan-out mechanism; a later increment swaps poll→push when the spine lands. This is a "don't fork" constraint, not a build item.
- **NFR-07 — Dual-deploy discipline.** Widget/shared-lib changes rebuild BOTH LegalWorkspace and SpaarkeAi (the R1/prior dual-deploy trap).
- **NFR-08 — Deploy config parity.** Any env running this BFF MUST have `Communication__Acs__Endpoint` set (R1 binding; boot-safe since the ACS Lazy fix, but SEND requires it).

## Technical Constraints

### Applicable ADRs
- **ADR-024** — 11-entity regarding family; typed lookups must mirror `RegardingFieldMap.All`.
- **ADR-034** — Membership resolver + `(personId, personIdType)` tuple precedent; the participant junction MUST align to this tuple (rejects text-index + polymorphic-lookup approaches).
- **ADR-032** — Null-Object kill-switch / `Lazy<>` injection; any feature-gated service (e.g. participant-index write behind a flag, if gated) uses this pattern with symmetric registration.
- **ADR-046** — Forbids a second regarding mechanism (thread regarding uses RegardingResolver, not CommunicationConnections or Field Mapping Framework).
- **ADR-038** — Testing strategy: seam / vertical-slice tests are the DoD for dispatch-spine changes (the new read endpoints + resolver policy).
- **ADR-028** — Spaarke Auth v2; the impersonation read path is the blessed mechanism (do not fork).
- **BFF hygiene (root §10)** — load `.claude/constraints/bff-extensions.md`; Placement Justification; publish-size + CVE per task; use `Services/Ai/PublicContracts/` facades only if AI capability is needed (R2 needs none).

### MUST Rules
- ✅ MUST extend `CommunicationThreadReadService` + `IImpersonatedCommunicationQuery` + `ICommunicationAccessFilter` for all new reads.
- ✅ MUST add a **new Lookup discriminator** to the thread; MUST keep the existing Text `sprk_regardingrecordtype`.
- ✅ MUST populate the participant junction at **message grain** reusing `ParticipantCorrelationRung` resolution.
- ✅ MUST keep the widget type string `communications-list` + section id `communications`.
- ✅ MUST reuse grid config `e1826c4c-…` (single default).
- ✅ MUST declare a bound `anchorField` on any new form-placed PCF.
- ✅ MUST dual-deploy (LegalWorkspace + SpaarkeAi) on widget/shared-lib changes.
- ❌ MUST NOT reintroduce membership-union on reads (retired 2026-07-16).
- ❌ MUST NOT add a second access mechanism, second grid default, or second widget.
- ❌ MUST NOT retype the Text `sprk_regardingrecordtype` (breaking).
- ❌ MUST NOT render message content via VisualHost client-fetch (bypasses `internal-only`).
- ❌ MUST NOT use CommunicationConnections PCF or Field Mapping Framework for thread regarding.

### Existing Patterns to Follow
- Read path: `Services/Communication/CommunicationThreadReadService.cs`, `IImpersonatedCommunicationQuery.cs`, `Access/CommunicationAccessFilter.cs`.
- Regarding map: `Services/Communication/Engine/RegardingFieldMap.cs`; resolver: `ThreadResolver.cs` (`BuildTopic` ~line 175).
- Participant resolution: `ParticipantCorrelationRung.cs` (`QueryContactByEmailAsync`); reference type `Models/ParticipantReference.cs`.
- Widget shape: `Spaarke.Events.Components/src/widgets/CalendarWorkspaceWidget/CalendarWorkspaceWidget.tsx`; contract `Spaarke.AI.Widgets/src/types/widget-types.ts` (`WorkspaceWidgetComponent<TData>`).
- Grid: `Spaarke.UI.Components/src/components/DataGrid/` (+ `DataGridPageShell.tsx`); chip discovery `filterChips/chipDiscovery.ts`; overlays `fetchXmlOverlay.ts` (`hostFilters`).
- Standalone page: `src/solutions/sprk_invoicespage/src/main.tsx`; deploy `scripts/Deploy-AllDataGridConsumers.ps1`.
- VisualHost: `ConfigurationLoader.ts`, `ChartRenderer.tsx`, `sprk_chartdefinition` (count MetricCard = config-only).

## Placement & New Components (per CLAUDE.md §10 / §11)

### Hot-Path Declaration

```xml
<hot-path-declaration>
  <bff>Y</bff>                 <!-- new read endpoints + participant-index write + auto-thread policy -->
  <spaarkeai>Y</spaarkeai>     <!-- upgrade the communications-list workspace widget -->
  <ci-workflows>N</ci-workflows>
  <skill-directives>N</skill-directives>
  <root-claude-md>N</root-claude-md>
</hot-path-declaration>
```

**Placement Justification (§10):** New read endpoints extend the existing `CommunicationEndpoints` group + `CommunicationThreadReadService` (the blessed impersonation read path) — no new endpoint group, no new access mechanism. The participant index is a Dataverse schema add + a capture/send-time write reusing existing `ParticipantCorrelationRung` resolution — no new AI dependency, no new external SDK. Auto-threading is an `IThreadResolver` policy change. Publish-size impact expected ≈0 (no new packages); the ≤60 MB ceiling applies and is verified per BFF task.

### New Components (§11 three-question gate)

| New component | Existing overlap (grep) | Can extend instead? | Cost-of-doing-nothing (concrete failure) |
|---|---|---|---|
| `by-regarding` endpoint (FR-01) | `CommunicationEndpoints.cs` is thread-id-scoped only | No — new route, but reuses read service/filter | A record's threads cannot be listed; Surface 1 has no data source |
| filtered `query` endpoint (FR-02) | same, thread-id-scoped | No — new route, reuses read path | Global grid / widget cannot filter by person/channel/date across records |
| `sprk_communicationparticipant` junction (FR-08) — 1 table + 6 fields | `sprk_from/to/cc` are `;`-joined TEXT; `ThreadMembershipDerivationService` is record→people, not person→comms | No — no queryable person→communications structure exists | `participant=` filter impossible; person filter degrades to text-LIKE (wrong results, no role precision); external parties invisible |
| thread Lookup discriminator (FR-06) | thread's `sprk_regardingrecordtype` is **Text**, not bindable by RegardingResolver | No — RegardingResolver requires a Lookup binding | RegardingResolver cannot attach to the thread form; thread regarding stays manual/denormalized |
| `@spaarke/communication-components` lib (FR-12) | Events lib is entity-coupled; ai-widgets is thin-generic | No — neither is the right home for rich comm widget content | Rich widget content has no cohesive home; forces coupling into Events lib or bloats ai-widgets |
| `sprk_communicationspage` shell (FR-11) | no communications standalone page exists | No — new ~50-line shell (copies invoicespage pattern) | No standalone All-Communications entry point (req 4 grid-half) |
| auto-vs-edited + default-thread markers (FR-07/09) | no such flags on `sprk_communicationthread` | No — new boolean/marker fields | Name re-derive clobbers user edits; messages orphan with no default thread |

*Extend-only (no §11 justification needed): regarding-mode Timeline (FR-03/04) extends `CommunicationTimeline`; RegardingResolver + VisualHost placements are config-only; read service methods extend `CommunicationThreadReadService`; compose enrichment extends `TimelineComposeBox`/`RecipientField`; widget upgrade extends the existing `communications-list` registration; grid page reuses config `e1826c4c-…`.*

## ADR Tensions (per CLAUDE.md §6.5)

| ADR | Rule challenged | Conflict | Path | Rationale |
|---|---|---|---|---|
| ADR-034 | "person↔record indexing uses the `(personId, personIdType)` tuple; no polymorphic parent lookups" | The junction models person identity; ADR-034 used a Guid+type tuple | **C (comply-with-intent)** | ADR-034's tuple exists to avoid an awkward **6-target** polymorphic lookup and to forbid fuzzy text-name matching. R2's junction has only **2** targets (systemuser, contact), so it uses **two typed nullable lookups** (`sprk_systemuser`/`sprk_contact`, exactly one set) — which honors ADR-034's *intent* (typed identity, no text-name matching), adds FK integrity + DataGrid chip auto-derivation the tuple can't, and needs no polymorphic lookup. Message parent is a single typed lookup to `sprk_communication` (message grain, Q-A). Owner-approved 2026-07-18. |
| ADR-046 | "no second regarding mechanism" | Thread needs regarding-resolution like `sprk_communication` | **C (comply)** | Reuse the existing RegardingResolver PCF (0 code) on the thread form; add a Lookup discriminator so it can bind. No new mechanism introduced. |
| T-1 (VisualHost vs access filter) | R1 access rule: message content flows only through the BFF `internal-only` filter | VisualHost fetches client-side (RLS-honoring but not `internal-only`) | **C (comply)** | Restrict VisualHost to a count/summary MetricCard (FR-05); render all content via the BFF-backed regarding-mode Timeline (FR-03). No content bypasses the filter. |

> All other listed ADRs (ADR-024, ADR-032, ADR-038, ADR-028, BFF hygiene) apply without exception. This section may be updated if tensions emerge during implementation.

## Success Criteria

1. [ ] A Matter (and each of the other 10 regarding entities) shows its threads-as-groups → message timelines on the form. **Verify**: open a record with ≥2 threads across ≥2 months; confirm Jun/Feb/Jul-style grouping with email+chat interleaved.
2. [ ] `by-regarding` + filtered `query` endpoints return access-filtered results across all 11 entity-sets. **Verify**: seam tests (≥3 entity-sets + private-thread-hidden + per-facet); manual 401/200 check.
3. [ ] Person filter (`participant=`) returns exact sender/recipient/Cc matches. **Verify**: send a message with structured To/Cc; query `participant={personId}`; confirm role-correct rows from the junction (not text-LIKE).
4. [ ] Thread regarding resolves via RegardingResolver on the thread form; name re-derives on regarding change but preserves user edits. **Verify**: change regarding → name updates while marker=auto; edit name → marker=edited → subsequent regarding change preserves the edit.
5. [ ] New messages always land in a sensible thread (subject → record-default → master). **Verify**: send with/without subject, with/without an existing thread; confirm non-null thread each tier.
6. [ ] Compose form captures Subject + structured To/Cc/Bcc; no message persists "(No Subject)". **Verify**: send via enriched form; confirm `sprk_subject`, meaningful `sprk_name`, populated participant rows, `sprk_cc`/`sprk_bcc`.
7. [ ] Standalone All-Communications page renders the grid with channel/person/date/regarding chips (config `e1826c4c-…`, no second default). **Verify**: launch page; confirm chips auto-derived; confirm single default config.
8. [ ] Rich `communications-list` widget renders in both LegalWorkspace and SpaarkeAi; dispatch unbroken. **Verify**: dual-deploy; open both surfaces; confirm card strip + chips + grid + row modal; type string unchanged.
9. [ ] BFF publish size ≤60 MB; no new HIGH CVE. **Verify**: `dotnet publish` size report + `dotnet list package --vulnerable` on every BFF task.

## Dependencies

### Prerequisites
- R1 shipped + deployed (done) — thread model, Timeline, impersonation read path, `ParticipantCorrelationRung`, `RegardingFieldMap`.
- `ai-spaarke-ai-workspace-UI-r2` **Complete** (Q3 resolved) — R2 owns/upgrades its grid config `e1826c4c-…` + `communications-list` widget in place.
- `Communication__Acs__Endpoint` set on any target env (dev is set).

### External Dependencies
- Dataverse schema deploy (thread lookups + discriminator + markers; participant junction + its 6 fields) — requires solution import.
- **Deploy prerequisite — app-user privileges (specified, owner executes at deploy):** the BFF application user MUST have, on the target env:
  - **Create + Read + Append/AppendTo** on the new `sprk_communicationparticipant` table (capture/send writes junction rows; reads join them for `participant=`).
  - **Read** on `systemuser` + `contact` (already present) for person resolution.
  - Carried over from R1 (still open on some envs): **Share (`prvShareCommunication…`)** on `sprk_communication` + `sprk_communicationthread` for the R1 write-side membership/private-thread sharing path. Not newly required by R2's read/participant work, but list it so a fresh env is fully provisioned. Document alongside `Communication__Acs__Endpoint` in the deploy runbook.
- Alignment touchpoint (reserve-only, no dependency) with `spaarke-notification-spine-r1` on the `communication-arrived` kind + envelope — see NFR-06.

## Owner Clarifications

| Topic | Question | Answer | Impact |
|-------|----------|--------|--------|
| Coordination (Q3) | Upgrade `ai-spaarke-ai-workspace-UI-r2`'s shipped grid config + widget in place, or fork? | **Upgrade in place** (that project verified Complete: commit `8ba30b9a6`) | No fork; R2 owns config `e1826c4c-…` + `communications-list` widget. W0 gate cleared. |
| Participant grain (Q-A) | Write junction rows at message, thread, or both grains? | **Message-level** (`sprk_communication` lookup only) | Single typed parent lookup (ADR-034 compliant); `participant=` is message-exact with role; thread participation derived by rollup; ~½ the write volume of "both". |
| Page navigation (Q-B) | Where does the standalone All-Communications page live? | **Widget/launcher only for R2** — no permanent sitemap entry | Ship the shell + `Deploy-AllDataGridConsumers.ps1` registration; defer permanent sitemap/app placement to a later config pass. |
| Participant index (Q1) | Junction now vs lookup-only interim? | **Build junction in R2** | W5 mandatory; no interim. |
| Category/tags (Q2) | Reuse taxonomy vs new choice set? | **None in R2** | Drop from CC-1 + thread schema; regarding + name only. |
| Standalone page (Q4) | Ship Surface 2 page? | **Ship it** | FR-11 in scope. |
| Entity scope (Q5) | Matter-first vs all 11? | **All 11 from day one** | FR-04 on all 11 forms; W1/W4 test matrix ×11. |
| Junction identity (Q-C) | Two typed lookups vs ADR-034 Guid+type tuple? | **Two typed nullable lookups** (`sprk_systemuser`/`sprk_contact`) | FK integrity + DataGrid person-chip auto-derivation; ADR-034 path-C comply-with-intent (only 2 targets, no polymorphic lookup needed). |
| Unresolved address (Q-D) | Write unresolved row vs skip external-only? | **Write unresolved row** (`sprk_isresolved=false` + `sprk_addresstext`) | External parties stay filterable + back-fillable; `participant=` never silently omits them. |
| Spine sequencing (Q-E) | Stay polling vs hard-depend on unbuilt spine? | **Stay polling, reserve alignment** | No dependency on `notification-spine-r1`; R2 unblocked; poll→push deferred to a later increment (NFR-06). |

## Assumptions

- **Thread-participation queries** (who is in a thread) are answered by rollup/aggregation over message-grain junction rows — no separate thread-grain rows written (per Q-A). If a hard thread-grain facet emerges, revisit as a schema follow-up.
- **VisualHost summary card (FR-05)** is optional and count-only; if a count MetricCard is deemed unnecessary at design review it may be dropped without affecting Surface 1.
- **Naming re-derive marker** is a new boolean on `sprk_communicationthread` (auto vs user-edited); **default-thread marker** is a new field/flag identifying a record's default thread. Both additive.
- **Standalone page** copies `sprk_invoicespage`'s build/deploy pattern verbatim except for the config GUID and title.

## Unresolved Questions

*All design-level open questions were resolved 2026-07-18 (see Owner Clarifications). Remaining items are execution-time confirmations, none blocking `/project-pipeline`:*

- [x] ~~CC-2 junction schema~~ — **RESOLVED**: fields + roles {From,To,Cc,Bcc} + unresolved-address handling locked in FR-08. The W0/W5 schema ADR now *documents* the locked design (it is no longer a design gate). The one residual is exact Choice **integer values** for `sprk_role` (assigned at schema-authoring time via `dataverse-create-schema`) — a mechanical detail, not a decision.
- [x] ~~Notification-spine taxonomy~~ — **RESOLVED**: R2 reserves the `communication-arrived` kind + envelope, takes no spine dependency, stays polling (NFR-06). No cross-owner sign-off needed for R2 (the reservation already lives in the spine design, co-authored by messaging-r1).
- [x] ~~App-user Share privilege~~ — **RESOLVED**: specified as a deploy prerequisite (see Dependencies → External). Owner executes at deploy; not a design gate.
- [ ] **Notification-spine taxonomy drift watch (advisory)** — if `notification-spine-r1` changes the envelope/kind names during its own spec, R2's reservation should track it. Non-blocking; monitored via `/conflict-check`.

---

*AI-optimized specification. Original design: `design.md`. Next step after review: `/project-pipeline projects/messaging-communication-app-r2`.*
