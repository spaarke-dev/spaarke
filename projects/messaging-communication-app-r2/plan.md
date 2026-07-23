# PLAN — Communication Workspace R2

> **Generated**: 2026-07-18 via `/project-pipeline`
> **Source**: [`spec.md`](spec.md) (12 FRs, 8 NFRs) · [`design.md`](design.md) (investigation-grounded)
> **Branch**: `work/messaging-communication-app-r2` (synced to latest master 2026-07-18)
> **Builds on (complete + merged)**: `messaging-communication-app-r1` (thread model + Timeline + impersonation read path) · `email-communication-solution-r4` (`Services/Communication/**` Association Engine + enrichment) · `ai-spaarke-ai-workspace-UI-r2` (grid config `e1826c4c-…` + thin `communications-list` widget)
> **Coordinates with (reserve-only)**: `spaarke-notification-spine-r1` (R2 reserves the `communication-arrived` kind; no dependency — NFR-06)

---

## 1. Objective

R2 is the **read / query / organize layer** on top of R1's messaging channel. R1 shipped transport + capture + thread model + a per-thread polling Timeline. R2 makes communications **findable and organized across records and people**: a record-level threads view on all 11 regarding-family entities, a standalone all-communications view, a rich workspace widget, thread regarding-resolution, a queryable participant index, an auto-threading policy, and a richer compose form. The R1 data model already supports "conversations related to a record", "N threads per record", and "email+chat unified" — so R2 is **mostly read surface + UI + two schema deltas**, not a schema migration.

**Graduation** = all 9 spec Success Criteria met (see [`README.md`](README.md)).

---

## 2. Architecture Context — Discovered Resources

### Applicable ADRs (from spec §Technical Constraints)

| ADR | Relevance to R2 |
|---|---|
| **ADR-024** | 11-entity regarding family — thread typed lookups MUST mirror `RegardingFieldMap.All`; MUST NOT add a second regarding mechanism. |
| **ADR-034** | `(personId, personIdType)` tuple precedent — participant junction aligns to its *intent* (typed identity, no text-name matching); R2 uses 2 typed lookups (path-C, see ADR Tensions). |
| **ADR-046** | ACS messaging channel (authored R1) — thread model + channel-agnostic resolver R2 reads over. |
| **ADR-032** | Null-Object kill-switch / `Lazy<>` — any feature-gated participant-write path uses this + symmetric registration. |
| **ADR-038** | Testing strategy — vertical-slice **seam tests** are the DoD for the new read endpoints + resolver policy (dispatch-spine changes). |
| **ADR-028** | Auth v2 — the **impersonation read path** is the blessed mechanism; MUST NOT fork it. |
| **ADR-008 / ADR-010 / ADR-019** | Endpoint filters; DI minimalism (test-seam interfaces, concrete impls); ProblemDetails. |
| **ADR-021 / ADR-022 / ADR-006 / ADR-026 / ADR-012** | Fluent v9; PCF platform libs (React 16/17); UI surface arch; Code Page standard; shared component library (new `@spaarke/communication-components`). |
| **ADR-013 / ADR-015** | AI facade (only if AI touched — R2 touches none); AI may flag never decide (N/A to R2). |

### Existing patterns / canonical implementations to follow (extend, don't rebuild)

- `Services/Communication/CommunicationThreadReadService.cs` + `IImpersonatedCommunicationQuery.cs` (entity-set-agnostic) + `Access/CommunicationAccessFilter.cs` — **the blessed impersonation read path** to extend for `by-regarding` + `query` (R1 task 050/042).
- `Api/CommunicationEndpoints.cs` — existing endpoint group; add the two new routes here (no new group).
- `Services/Communication/ThreadResolver.cs` (`BuildTopic()` ~line 175) + `IThreadResolver.cs` — auto-threading policy + naming re-derive.
- `Services/Communication/Engine/RegardingFieldMap.cs` — the 11-entity → typed-lookup map to mirror for thread lookups.
- `Services/Communication/Engine/Rungs/ParticipantCorrelationRung.cs` (`QueryContactByEmailAsync`) + `Models/ParticipantReference.cs` — email→person resolution to reuse for the participant-index write.
- `src/client/shared/Spaarke.UI.Components/src/components/CommunicationTimeline/` + PCF `src/client/pcf/CommunicationTimeline/` — the R1 component + PCF to extend to regarding-mode (mirror the PCF pattern; **declare a bound `anchorField`** — R1 lesson).
- `src/client/pcf/RegardingResolver/` (entity-agnostic, FR-22) — place on the thread form, **0 code change**.
- `src/client/pcf/VisualHost/` + `sprk_chartdefinition` — count MetricCard = config-only.
- `src/client/shared/Spaarke.Events.Components/src/widgets/CalendarWorkspaceWidget/CalendarWorkspaceWidget.tsx` — Pattern D dual-use widget shape to copy into the new lib.
- `src/client/shared/Spaarke.AI.Widgets/src/widgets/workspace/register-workspace-widgets.ts` + `src/solutions/LegalWorkspace/src/sections/communications.registration.ts` — the widget registration to upgrade **in place** (keep type string `communications-list`).
- `src/solutions/sprk_invoicespage/src/main.tsx` + `scripts/Deploy-AllDataGridConsumers.ps1` — the ~50-line standalone-page shell to copy + register.
- `src/client/shared/Spaarke.UI.Components/src/components/DataGrid/` (+ `filterChips/chipDiscovery.ts`, `fetchXmlOverlay.ts`) — grid framework + chip auto-derivation + `hostFilters`.

### Applicable skills

`dataverse-create-schema` / `dataverse-deploy` (thread lookups + junction), `pcf-deploy` (regarding-mode Timeline + compose accessories), `code-page-deploy` (standalone page + widget dual-deploy), `fluent-v9-component` (widget + regarding-mode component), `bff-deploy` (BFF), `adr-check` + `code-review` (Step 9.5 gates), `conflict-check` (BFF hot-path — **mandatory before every BFF wave**), `dataverse-mcp-usage` (schema audit).

### Knowledge / constraints

- `.claude/constraints/bff-extensions.md` — **binding** BFF-addition checklist (load before every BFF task).
- `docs/standards/DATA-ACCESS-DECISION-CRITERIA.md` — `Xrm.WebApi` vs BFF (VisualHost T-1 tension; grid uses `Xrm.WebApi`, content via BFF).
- `docs/standards/MODAL-DECISION-CRITERIA.md` — widget row-click modal.
- `docs/architecture/SPAARKE-DATAGRID-FRAMEWORK-ARCHITECTURE.md` + `docs/guides/DATAGRID-FRAMEWORK-CONFIGURATION-GUIDE.md` — grid config authoring.
- `docs/guides/BUILD-A-NEW-WORKSPACE-WIDGET.md` — Pattern D dual-use archetype (Calendar canonical).
- `../messaging-communication-app-r1/notes/access-model-decision.md` — **reads = impersonation + 2-rule filter; membership-union retired 2026-07-16** (binding).

---

## 3. Hot-Path Declaration (root §10 / FR-C04)

```xml
<hot-path-declaration>
  <bff>Y</bff>                 <!-- by-regarding + query endpoints, participant-index write, auto-thread policy -->
  <spaarkeai>Y</spaarkeai>     <!-- upgrade the communications-list workspace widget -->
  <ci-workflows>N</ci-workflows>
  <skill-directives>N</skill-directives>   <!-- participant-junction schema ADR in .claude/adr/ main-session, no skill edits -->
  <root-claude-md>N</root-claude-md>
</hot-path-declaration>
```

**⚠️ Hot-path overlap (from `projects/INDEX.md`)**:
- **`Services/Communication/` (BFF)** — `email-communication-solution-r4` is the broad owner (Association Engine + enrichment). **Per owner (2026-07-18): email-r4 is wrapping up and its BFF work is already merged to master** (this worktree is synced). R2 builds **additively** on top: new `by-regarding`/`query` methods on `CommunicationThreadReadService`, new participant-write, an `IThreadResolver` policy add. **Tasks 050 (participant write) + 070 (auto-threading) edit the shared persist/resolver path → `parallel-safe: false` + `/conflict-check` before their PRs.**
- **SpaarkeAi widget** — `spaarke-dataset-grid-framework-r2` (`@spaarke/legal-workspace` extraction + section-registry) and widget-registry touchers. R2's new `@spaarke/communication-components` lib + `register-workspace-widgets.ts` edit + open PR **#508** (Events.Components package boundary, which R2 copies from) → **merge-order coordination on the widget task (030)**.
- **Run `/conflict-check` at project start and before every BFF wave.**

### Placement Justification (root §10)

New read endpoints extend the existing `CommunicationEndpoints` group + `CommunicationThreadReadService` (the blessed impersonation read path) — no new endpoint group, no new access mechanism. The participant index is a Dataverse schema add + a capture/send-time write reusing existing `ParticipantCorrelationRung` resolution — **no new AI dependency, no new external SDK, no new NuGet**. Auto-threading is an `IThreadResolver` policy change. **Publish-size impact expected ≈0**; the ≤60 MB ceiling applies and is verified per BFF task. Cite `.claude/constraints/bff-extensions.md` on every BFF PR. Baseline: **~46.99 MB** (R1 peak) / ceiling ≤60 MB.

---

## 4. Phase Breakdown (Wave WBS)

Baseline BFF publish size: **~46.99 MB** compressed (post-R1). Ceiling ≤60 MB. Report absolute + delta on every BFF-touching task.

### W0 — Schema + Foundation + ADR
Verify current state, add the two schema deltas, author the participant-junction ADR. (Q2: **no category/tags**.)
- **001** Phase-0 audit (spike): live `sprk_communicationthread`/`sprk_communication` schema; confirm category/tags absent (Q2); verify email-r4 `Services/Communication` merged state on this worktree; plan `sprk_role` choice-integer assignment.
- **002** Schema: `sprk_communicationthread` typed-regarding **all-11** lookups (mirror `RegardingFieldMap.All`) + **new Lookup discriminator** field + naming-edited marker (boolean) + default-thread marker (CC-1/CC-4 support) (FR-06/07/09)
- **003** Schema: `sprk_communicationparticipant` junction — 6 fields (communication lookup, `sprk_systemuser`, `sprk_contact`, `sprk_role` choice {From,To,Cc,Bcc}, `sprk_addresstext`, `sprk_isresolved`) (CC-2) (FR-08)
- **004** Author the participant-junction **schema ADR** (concise + full; ADR-034 path-C tension; INDEX) — *main-session (`.claude/`)* (FR-08 support)

### W1 — BFF Reads (CC-3) — additive extensions
Extend the impersonation read path. Cover all 11 regarding entity-sets.
- **010** `GET /api/communications/by-regarding/{entityType}/{id}` + `ReadByRegardingAsync` on `CommunicationThreadReadService` (reuse `IImpersonatedCommunicationQuery` + `ICommunicationAccessFilter`) (FR-01)
- **011** `GET /api/communications?thread=&regarding=&channel=&from=&to=` filtered query (facets reuse read path; `participant=` stubbed until W5) (FR-02)

### W2 — Record Threads View (Surface 1) — NEW
Regarding-mode Timeline on all 11 forms + optional summary card.
- **020** Regarding-mode extension to `CommunicationTimeline` component (threads-as-collapsible-groups → per-thread interleaved timelines; calls `by-regarding`) (FR-03)
- **021** Regarding-mode PCF variant (mirror R1 `CommunicationTimeline` PCF + regarding-resolution path; **bound `anchorField`**) + solution pack (FR-04) — *pcf, deploy, e2e-test*
- **022** Place the regarding-mode PCF on **all 11** entity forms + form config (FR-04 all-11) — *dataverse, deploy*
- **023** VisualHost summary count MetricCard config (optional, config-only; drill-through to Surface 1; count-only per T-1) (FR-05)

### W3 — Workspace Widget Upgrade (Surface 3) — upgrade-in-place
- **030** New `@spaarke/communication-components` lib + rich Pattern D widget (copy `CalendarWorkspaceWidget`); upgrade `communications-list` **in place** (keep type string + section id); **dual-deploy** LegalWorkspace + SpaarkeAi (FR-12) — *frontend, fluent-ui, spaarke-ai*

### W4 — Standalone Page + Grid Polish (Surface 2) — ship it
- **040** Standalone `src/solutions/sprk_communicationspage/` shell (copy `sprk_invoicespage`; reuse config `e1826c4c-…`; **widget/launcher only — no sitemap entry** per Q-B); register in `Deploy-AllDataGridConsumers.ps1` (FR-11)
- **041** Curate grid views/columns on config `e1826c4c-…` so chips auto-derive (channel/person/date/regarding) (FR-11 support) — *dataverse config*

### W5 — Participant Index + Person Filter (CC-2)
- **050** Participant-index **write** at capture/send — populate junction rows (message grain, reuse `ParticipantCorrelationRung`; unresolved external → `isresolved=false` + `addresstext`) (FR-08) — *bff, communication; edits shared persist path → `parallel-safe: false`, `/conflict-check`*
- **051** `participant=` facet on the filtered `query` endpoint (join the junction) (FR-02 completion)

### W6 — Compose-Form Enrichment (CC-5)
- **060** Compose enrichment: Subject/topic + Cc/Bcc + structured recipient picker (reuse `RecipientField`, emit resolved record ids feeding 050); meaningful `sprk_name`/`sprk_subject`; free-text fallback (FR-10) — *pcf, frontend, communication*

### W7 — Auto-Threading + Thread Naming (CC-4 + CC-1)
- **070** Auto-threading policy: 3-tier `IThreadResolver` (subject → per-record default → per-user master) + `default-thread` marker; every message resolves to a non-null thread (FR-09) — *bff, communication; edits shared `ThreadResolver.cs` → `parallel-safe: false`, `/conflict-check`, characterization-test existing flows first*
- **071** Thread naming re-derive (`BuildTopic` re-derives unless user-edited, marker-gated) + place RegardingResolver PCF on the thread form (0 code) (FR-07) — *bff + pcf config*

### W8 — Tests + Docs + Wrap
- **080** Vertical-slice **seam tests**: `by-regarding` + `query` + `participant=` + auto-threading tiers; **11-entity pass**; access-parity (private-thread-hidden + internal-only); preserve existing email/messaging characterization (NFR-03) — *testing*
- **081** Architecture doc: extend communication architecture with the Workspace read endpoints + participant index + regarding-mode + widget (FR support) — *docs*
- **090** Project wrap-up (README Complete, lessons-learned, `/test-diet`, archive) — *main-session*

---

## 5. Critical Path

```
001 → 002 → 010 → 020 → 021 → 022 → 080 → 090
001 → 003 → 050 → 051 → 080          (participant index feeds person filter)
002 → 070 → 071                       (auto-threading + naming on shared resolver)
```

The genuine serial spine: schema (002/003) → BFF reads (010) → regarding-mode Timeline (020/021/022) → tests (080). The participant track (003 → 050 → 051) and the auto-threading track (002 → 070) run in parallel after schema. The widget (030) and standalone page (040) are UI-only tracks parallel after W1 exists (they read via grid/`Xrm.WebApi`, not the new endpoints). 050 + 070 are the shared-`Services/Communication/`-path edits (serial, `/conflict-check`).

## 6. Parallel Execution Groups

| Group | Tasks | Prerequisite | Notes |
|---|---|---|---|
| **W0-A** | 001, 004 | — | Audit spike + ADR (004 main-session `.claude/`) |
| **W0-B** | 002, 003 | 001 | Two schema deltas (distinct entities, parallel) |
| **W1** | 010, 011 | 002 (+003 for 011's `participant=` stub) | Additive read endpoints; parallel |
| **W2** | 020 → {021 → 022}, 023 | 010 | Component → PCF → 11-form placement; 023 config parallel |
| **W3** | 030 | 010 (grid data) + PR #508 coord | Widget upgrade (dual-deploy); merge-order vs dataset-grid-r2 |
| **W4** | 040, 041 | (config exists) | Standalone page + grid curation (parallel) |
| **W5** | **050 (serial)** → 051 | 003 | 050 shared-path write; 051 facet after |
| **W6** | 060 | 050 (participant contract) | Compose enrichment feeds the index |
| **W7** | **070 (serial)** → 071 | 002 | Auto-threading shared-resolver edit; naming after |
| **W8** | 080, 081 | W1–W7 substantially complete | Seam tests + doc |

**Max concurrency**: 6 agents/wave. `.claude/`-touching (004) + wrap-up (090) run main-session, sequential. **050 + 070 are `parallel-safe: false`** (shared `Services/Communication/` edits — never concurrent with each other or other BFF writers; `/conflict-check` before each).

**Model tiers (per CLAUDE.md §8.5)**: default **sonnet @ high**. **opus** on: **004** (ADR authoring), **050** (participant write on shared persist path), **070** (auto-threading on shared `ThreadResolver`). **effort: xhigh** on: **050**, **070** (shared-path edits over frozen email/messaging flows), **080** (seam-test composition + access-parity). All others sonnet @ high.

## 7. High-Risk / Watch Items

- **050 (participant write)** + **070 (auto-threading)** — edit shared `Services/Communication/` (persist path + `ThreadResolver`). Characterization-test existing email/messaging flows and keep green BEFORE extending. `parallel-safe: false`; `/conflict-check` before PR.
- **010/011 access parity (NFR-03)** — the new read surfaces MUST apply R1's impersonation + 2-rule filter; MUST NOT reintroduce membership-union (retired 2026-07-16). Explicit access-parity seam tests in 080.
- **022 (11-form placement)** — larger deploy/test matrix; `by-regarding` is entity-set-agnostic so server cost is flat, but all 11 forms must be placed + smoke-tested.
- **030 (widget upgrade)** — dual-deploy trap (rebuild LegalWorkspace + SpaarkeAi); keep type string `communications-list`; merge-order vs `spaarke-dataset-grid-framework-r2` + PR #508.
- **T-1 (VisualHost)** — 023 is count-only; message content renders via the BFF-backed Timeline, never VisualHost client-fetch.
- Every BFF-touching task: `/conflict-check` before PR + publish-size + CVE report (root §10).

## 8. FR Coverage

FR-01→010 · FR-02→011,051 · FR-03→020 · FR-04→021,022 · FR-05→023 · FR-06→002 · FR-07→071 · FR-08→003,004,050 · FR-09→070 · FR-10→060 · FR-11→040,041 · FR-12→030. NFRs distributed (NFR-01/02 every BFF task; NFR-03→080; NFR-05 §11 gate; NFR-06 reserve-only; NFR-07 widget dual-deploy; NFR-08 deploy runbook).

## 9. References

- [`spec.md`](spec.md) · [`design.md`](design.md) · [`notes/r2-resource-investigation.md`](notes/r2-resource-investigation.md)
- Root `CLAUDE.md` §10 (BFF Hygiene) + §11 (Component Justification) + §6.5 (ADR Conflict Resolution)
- `.claude/constraints/bff-extensions.md` · `docs/standards/DATA-ACCESS-DECISION-CRITERIA.md`
- `projects/INDEX.md` (hot-path registry) · siblings `projects/messaging-communication-app-r1/` + `projects/email-communication-solution-r4/`
