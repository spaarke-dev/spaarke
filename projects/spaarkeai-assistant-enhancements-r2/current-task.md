# Current Task State

> **Auto-updated by task-execute and context-handoff skills**
> **Last Updated**: 2026-08-05 (context-handoff before compaction)
> **Protocol**: [Context Recovery](../../docs/procedures/context-recovery.md)

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Task** | **023** (FR-B4) — **✅ COMPLETE** (2026-08-06, built + committed; deploy deferred to 025). **10 tasks done.** Next: **024** (FR-B6 dev trace). |
| **Step** | Done. Ready for 024. |
| **Status** | completed |
| **Next Action** | Start **task 024** (FR-B6 — dev-visible proactive-selection trace; ConvPane spine; sonnet/high; STANDARD rigor). The BFF already returns the `reason` per chip (`ChatSuggestChip.Reason`, from the Action's output schema) — 024 surfaces it in a dev-only trace view. Then 025 (deploy + verify B — deploys the accumulated 022/023/024 client changes + verifies). |

### Task 023 — DONE (2026-08-06). Deploy deferred to 025.
- Manual "Refresh suggestions" control in `transcriptFooter`, gated on `chips.hasChips` (new reactive flag on `useConsumerChips`). Click → `handleRefreshSuggestions` → `fireProactiveSuggestion(stamp, force=true)` re-runs the 022 turn for the active tab, bypassing the once-per-tab guard (the ONLY re-fire path besides first-open, NFR-02). Fluent subtle Button + `ArrowClockwiseRegular` (dark-mode safe, ADR-021).
- Files: `ConversationPane.tsx` (force param + refresh handler + control), `useConsumerChips.tsx` (`hasChips` on `ConsumerChipsController`). Tests: `ConversationPane.refresh-suggestions.e2e.test.tsx` (2). typecheck Surface-owned:0; build clean; 022 seam test still green.
- **Code page NOT redeployed** (022 is live; 023's refresh button lands with the 025 deploy, after 024).

### Task 022 — BFF DONE (2026-08-06). Seeded IDs + deployed.
- **Action** `suggest-followups` = `64505c5b-5191-f111-b8db-7ced8ddc4cc6` (kind=Prompted, tier=Fast, temp=0.2, prompt 3620ch, schema 1411ch).
- **Binding** `assistant-suggest` = `c58b1b57-5191-f111-b8db-7ced8ddc4a05` (enabled, →Action).
- BFF deployed to `spaarke-bff-dev`; publish 48.33 MB (baseline; +~1.3MB incl PDBs); healthz OK; `POST …/suggest` → 401 (route live). Build clean; 14 new unit tests pass.
- Files: `ConsumerRoutingService.FilterByContextType` + `Binding.ContextTypeTags` filter; `AssistantSuggestionService.cs` (Services/Ai/Chat); `ChatEndpoints.cs` suggest endpoint + `ChatSuggestRequest/Response/Chip`; `ConsumerTypes.AssistantSuggest`; DI in `AnalysisServicesModule.cs`; `infra/dataverse/actions/suggest-followups.action.json`; tests `ConsumerRoutingServiceContextFilterTests.cs` + `AssistantSuggestionServiceTests.cs`.

### Task 022 — LOCKED DESIGN (feasibility confirmed 2026-08-06, no escalation)
**Mechanism chosen** (refines POML's illustrative "SprkChatAgentFactory" — step mode is directional): the grounded turn is a **catalog-authored Action `suggest-followups` run via `IActionRunner`**, mirroring `CommunicationProposeAi` — NOT a SprkChatAgentFactory fork. This is the ADR-039-cleanest grounded one-shot (prompt+schema = maker-editable catalog DATA, invariant (a)+(4)). Confirmed clean vs all 3 escalation triggers: (1) no SprkChat fork; (2) no 2nd dispatch protocol — chips ride the existing Click path, suggest is a *proposer*; (3) no new store — the suggest turn consumes NO tool reads (just pre-filtered candidates + server-derived compact content), so there is nothing to ledger (ADR-040 vacuously satisfied).

**Content-specificity is REAL** (feasibility gate passed): the active tab's server-derived compact state (`SprkChatAgentFactory.TryDeriveVisibleState` + `FormatVisibleStateFields(contentVisible:true)`) yields DocumentViewer→`filename`+`mimeType`+`selectionText`, Summary→`tldr`+`summary`. Enough for NDA≠invoice chips, inside the ADR-015 Path A bounded shape. Source content server-side (NOT client `CompactState`) via `ISessionPersistenceService.LoadSessionAsync(tenantId,sessionId).Tabs` + `.ActiveTabId`.

**BFF work items:**
1. `ConsumerRoutingService`: add `internal static IReadOnlyList<Binding> FilterByContextType(candidates, contextType)` — keep bindings whose `ContextTypeTags` contain contextType OR are empty (=any). Deterministic pre-filter (ADR-039-permitted). Candidate source = REUSE existing `ListTextProjectableBindingsAsync` (the loop-projectable capability catalog) — no new Dataverse query.
2. New facade `AssistantSuggestionService` (Services/Ai/Chat) mirroring `CommunicationProposeAi`: deps `IActionResolver`, `IActionRunner`, `IConsumerRoutingService`, `ISessionPersistenceService`. Resolve consumerType `assistant-suggest` → Action; operand `{ contextType, activeTab:{widgetType,displayName,compactContent}, candidates:[{bindingId,label}] }`; `RunAsync`→JsonElement; parse `{suggestions:[{targetBindingId,label,prefillArgs?}]}` cap 3, validate targetBindingId ∈ candidate ids (drop hallucinations), best-effort (null on any failure, never throw).
3. Endpoint `POST /api/ai/chat/sessions/{sessionId}/suggest`, req `{contextType, activeContext}` (reuse `ChatActiveContext`), resp `{chips:[≤3]}`. RequireAuthorization. No transcript injection. DI + contract test.
4. **Catalog seed**: author `infra/dataverse/actions/suggest-followups.action.json` (template = `create-task-from-email.action.json`); seed the `sprk_analysisaction` row + a `sprk_playbookconsumer` Binding row (`sprk_consumertype='assistant-suggest'`, `sprk_action`→the Action, `sprk_enabled=true`) in spaarkedev1 (MCP/WebAPI, like 021).
5. Build + publish-size (≤60MB, baseline ~48.25) + `Deploy-BffApi.ps1` + /healthz.

**Client** (Explore agent mapping current seams — agent running): once-per-tab `Set<string>` ref trigger in `usePaneEvent("workspace")` handler; POST /suggest once per tabId; `chips.acceptChips(≤3)`; no re-fire on switch-back. typecheck (Surface-owned:0) + seam test. Deploy code page.

### Task 022 Option B (design locked)
Server-side contextType filtering (CapabilityDto exposure NOT needed). ADR-039 clean: deterministic pre-filter + ONE grounded turn. `useSuggestionCards` deleted (no resurrection); `SuggestionCard.tsx` retained (do not re-wire). Client seam fully mapped in POML. Large BFF+client build — consider `/compact` for a fresh context before starting.

### Seeded BindingIds (task 021)
- **Reanalyze** (created): `9c29b488-4291-f111-b8db-7ced8ddc4a05` → `document` (chat-summarize/reanalyze, reuses summarize Action `eeb05bfd-1260-f111-ab0b-70a8a59455f4`).
- **document**: `651194cd…`(Chat Summarize) · `ed92d769…`(Agreement Classify) · `121194cd…`(AI Summary).
- **compose-doc**: `30374f2f`·`32374f2f`·`b1c4d38a`·`05a7132f`·`65549e51`·`b11aaf8b`·`904f2d53`·`986799ad`·`0aa7132f`.
- Untagged (empty = any context, intentional): create-*, compose-draft-document, chat-classify, daily-briefing, matter-summary leg. Analyst-extendable, no deploy.

### Task 021 mechanism (decided)
Column = String CSV `sprk_contexttypetags` (MaxLength 200) mirroring `sprk_surfaces` exactly. BFF: add to `Columns` array (ConsumerRoutingService.cs:80-103), add `Binding.ContextTypeTags` (IReadOnlyList&lt;string&gt;), map via `ParseSurfaces` (generic CSV splitter — reuse, no dup parser). Filter = task 022, NOT here. §6.5 Path A (owner-approved). No master overlap on the two BFF files (verified).

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
