# Current Task State

> **Auto-updated by task-execute and context-handoff skills**
> **Last Updated**: 2026-08-05 (context-handoff before compaction)
> **Protocol**: [Context Recovery](../../docs/procedures/context-recovery.md)

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Task** | Next = **021** (FR-B2 context-type tags) — **RE-SCOPED to Option C** (new column + BFF), owner-approved |
| **Step** | Not started |
| **Status** | not-started (7 tasks done + deployed; awaiting 021 build) |
| **Next Action** | Re-scope 021 POML to Option C, then build it. See "Task 021 re-scope" below + `notes/deviations.md` §"Task 021 Option C". |

### Files Modified This Session (all COMMITTED + PUSHED)
Branch `work/spaarkeai-assistant-enhancements-r2` @ `9aacda4bf` (pushed). Commits this session:
- `de94ebba4` pipeline init (artifacts + 27 POMLs) · `cdbc5e48f` overlap-warning correction
- `fdbe5755e` task 001 (FR-E1 banner removal) · `43bdec027` wave A1 (010+012) · `2c6eb02fd` task 011
- `b679f6410` deploy E+A to spaarkedev1 · `419c13768`/`9aacda4bf` task 020 (contextType set)

### Critical Context
5 phases E→A→B→D→C. `ConversationPane.tsx` is a **sequential spine** (E/A/B/D edit it). No live cross-worktree overlap (spine-r1 + analysis-hub-r1 merged to master). Phasing stays E→B→D→C (owner: "continue B as planned" — did NOT reorder C forward).

---

## Progress — 7 tasks DONE

| Task | What | Status |
|---|---|---|
| 001 (FR-E1) | Remove spine suggestion surface (banner). **Deviation:** kept `SuggestionCard.tsx` (reused by `useRerunFullAnalysisCard`) — see `notes/deviations.md`. | ✅ deployed |
| 002 | Deploy+verify E | ✅ deployed (banner gone — owner UAT ✓) |
| 010 (FR-A1) | ConversationPane `active_widget_changed` subscriber → `activeTabFocusRef`; new `activeTabFocusStamp.ts` | ✅ deployed |
| 011 (FR-A2) | `activeContext` on outbound body via decorate seam | ✅ deployed |
| 012 (FR-A3/A4) | Server `ChatActiveContext` DTO; prefer focus-stamp over UpdatedAt; ADR-015 active=compact/background=metadata | ✅ deployed |
| 013 | Deploy+verify A (BFF `spaarke-bff-dev` + code page `sprk_spaarkeai` @ spaarkedev1) | ✅* (owner E2E pending) |
| 020 (FR-B1/C3) | Closed `WidgetContextType` set on WidgetMetadata; email→'email'; wired through WorkspacePane broadcast → activeTabFocusStamp | ✅ (not yet deployed) |

**Owner UAT (2026-08-05):** banner gone ✓; "summarize this → email" does NOT work yet — **EXPECTED**: email visibility is Workstream C (040/041/042, not built). A only makes the server know *which* tab is focused; the email widget contributes no content until C. Owner chose to keep C last.

---

## Task 021 re-scope (DO THIS FIRST when resuming) — Option C, owner-approved

FR-B2 "no deploy" is **overridden** (§6.5 Path A) — no context-type field exists today, so a column is needed. Full rationale + work items in `notes/deviations.md` §"Task 021 Option C". Summary:
1. Edit `tasks/021-catalog-context-tags.poml`: rigor STANDARD→**FULL**; tags += `bff-api, dataverse`; rewrite steps for the column+BFF+seed+deploy work; add the §6.5 Path A note.
2. New Dataverse column `sprk_contexttypetags` (CSV/multi of the closed set) on `sprk_playbookconsumer` via `dataverse-create-schema` (target: spaarkedev1).
3. `Binding.cs` new `ContextTypeTags` field + `ConsumerRoutingService.cs` maps it (attribute read ~line 857) + candidate-filter logic.
4. Seed tag values on relevant Bindings + author the **Reanalyze** Binding (FR-D11 data).
5. BFF redeploy (`Deploy-BffApi.ps1`, publish ≤60 MB — baseline currently ~48.25 MB).

Then 022 (FR-B3/B5, **opus/xhigh**, ConversationPane spine): proactive suggestion turn cached per tabId, filters candidate Bindings by active-tab `contextType` (via the new field), renders ≤3 chips through the **reactive** `useConsumerChips` surface (NOT the removed useSuggestionCards). Then 023, 024, 025 (deploy B).

---

## Environment / deploy facts
- **BFF**: App Service `spaarke-bff-dev` / RG `rg-spaarke-dev`; deploy `pwsh -File <abs>\scripts\Deploy-BffApi.ps1`; health https://spaarke-bff-dev.azurewebsites.net/healthz. Baseline publish ~48.25 MB.
- **Code page**: `sprk_spaarkeai` on **spaarkedev1** (`https://spaarkedev1.crm.dynamics.com`); deploy `pwsh -File <abs>\scripts\Deploy-SpaarkeAi.ps1 -DataverseUrl 'https://spaarkedev1.crm.dynamics.com'` (needs a pre-built `dist/spaarkeai.html` — run `npm run build` in `src/solutions/SpaarkeAi` after `rm -rf dist/ node_modules/.vite/ .vite/`).
- **Auth**: `az` logged into Spaarke Dev subscription; PAC active = SPAARKE DEV 1.
- **Verify gates**: SpaarkeAi `npm run typecheck` must show "Surface-owned: 0" (pre-existing shared-lib errors OK). BFF `dotnet build src/server/api/Sprk.Bff.Api/`.
- **Parallel-execution note**: BFF file overlaps — 012&041 both edit `SprkChatAgentFactory.cs`; 012&031 both edit `ChatEndpoints.cs`; 030&033 both edit `SessionPersistenceService.cs`; 037&034 both edit `HistoryOverlay.tsx`. Don't run those pairs concurrently.

---

## Blockers
**Status**: None. (021 design fork RESOLVED → Option C, owner-approved.)

---

## Recovery Instructions
1. Read Quick Recovery + Progress above.
2. Read `notes/deviations.md` (001 SuggestionCard retention; 021 Option C).
3. Resume: re-scope `tasks/021-catalog-context-tags.poml` to Option C, then execute via task-execute.
4. Dispatch pattern: subagents per task at `<model-tier>`/`<effort>`; build-verify between waves; commit per task; update TASK-INDEX + this file.

**Commands**: `/project-continue` · "where was I?" · `work on task 021`
