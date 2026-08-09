# Current Task State

> **Auto-updated by task-execute and context-handoff skills**
> **Last Updated**: 2026-08-09 (context-handoff — R2 E/A/B/D/C SHIPPED; UAT rounds 1-3 fixes + Phase 0 quick-wins DEPLOYED; auth cold-start hang FIXED (flag revert) — needs deploy; R3 design + orchestration model AGREED)
> **Protocol**: [Context Recovery](../../docs/procedures/context-recovery.md)

---

## ⭐ CURRENT STATE (2026-08-09 — R2 shipped; post-UAT hardening + R3 design)

| Field | Value |
|-------|-------|
| **Branch / tip** | `work/spaarkeai-assistant-enhancements-r2` @ **`5169c6c42`** (pushed). NOTE master drifts fast (compose-r6, teams-app, email-intelligence-r2, SPA-external all merging) — **re-sync from master before any deploy**. Was 0-behind at 35cb11d89; the auth-revert commit `5169c6c42` is 1 ahead, NOT yet FF'd to master. |
| **R2 status** | **E/A/B/D/C all implemented, merged to master (PR #743 MERGED), and DEPLOYED.** Then 3 UAT rounds. 090 wrap-up NOT yet run. |
| **UAT fixes shipped (deployed)** | R1: active-tab visibility Path A (`SprkChatAgentFactory` active-tab-as-consent) + history-restore document re-fetch. Phase 0 quick-wins (commit `c4236c20f`, deployed from master `35cb11d89`): read_query date guidance (use injected today's-date literal, not GETDATE); **de-dup guard** on `widget_load` (WorkspacePane — layouts by layoutId, singletons by widgetType, respects allowMultiple) → no duplicate tabs; **Compose tab naming** (deriveComposeTabLabel, truncated+tooltip); **scroll-to-top on send** (SprkChat); **thin shared scrollbar** (theme/scrollbar.ts). |
| **🔧 AUTH COLD-START HANG — FIXED, NEEDS DEPLOY** | The first-mount "Connecting to Dataverse…" hang was **NOT a shared @spaarke/auth bug**. Root cause: SpaarkeAi-local flag **`requireSilentOnly: true`** (authInit.ts, set 2026-08-04 for R5 item 6 to suppress a cold-cache popup) removed the ONLY MSAL-cache-seeding path on a hard-reset → getAccessToken() returns '' → hang. **FIX = reverted the flag to `false`** (commit `5169c6c42`, SpaarkeAi-local ONE LINE, zero shared-auth/PCF blast radius). Restores known-good pre-Aug-4 behavior (one cold-cache sign-in, then silent). **Code-page built green; committed+pushed; NOT deployed yet.** Deploy this to close the cold-start re-UAT. Full investigation: Fable agent report in transcript. Deferred "fully-silent (no prompt at all)" = separate shared-lib effort only if the occasional prompt annoys. |
| **OPEN UAT items** | (a) **cold-start hang** → fixed via `5169c6c42`, pending deploy+re-UAT. (b) **"how many overdue tasks" answers the COUNT** → **R3** (needs a tasks parity tool; generic read_query can't COUNT + the model defaults to opening the grid). (c) narrated-briefing accuracy → R3. |
| **🎯 R3 DESIGN — the big one (design.md AGREED with owner)** | `projects/spaarkeai-assistant-enhancements-r3/design.md` = the **Assistant⇄Workspace Interaction Contract**. Principle: **Assistant = overview (answers by querying the SOURCE), Workspace = details; the Assistant NEVER reads tab content.** Grounded in a 3-explorer code inventory. Artifact: https://claude.ai/code/artifact/248b0a49-e6e1-499a-943a-024c0b8a9f7c |
| **R3 ORCHESTRATION MODEL (just agreed — design.md §5.5)** | **Awareness = identity + active-item HANDLE (an id), never content.** Pattern (generalized from the SHIPPED Compose active-document flow): (1) widget publishes the selected item as active context — handle `{id,type,label}` via the active-item conduit (Compose: `composeActionBridge`→`registerActiveDocument`→`POST /api/compose/active-document`→`activeSourceDocRef`); (2) Assistant AUTO-presents follow-on cards (no widget button, no typing); (3) click card → parity tool loads item BY ID from source; (4) output → native surface. **EMAIL worked example (the template):** select email → widget publishes `{communicationId, emlDocumentId, subject}` → cards **Reply·Reply All·Forward·Summarize thread** → click → `draft_reply(communicationId,mode)` loads by id (sprk_communication for addressing + `.eml` for thread) + drafts → existing `useEmailComposeActions.openComposer(mode, communicationId)` + NEW `bodyOverride` → native `SendEmailDialog` opens pre-filled. Email = `sprk_communication` record (working surface) + `.eml`/`emlDocumentId` (archive, loaded like a file via eml-render). **Two tool kinds per widget:** overview/query (chat→tool queries source) + per-item action (card→widget handle→native surface). Registration contract makes both REQUIRED per widget. |
| **R3 design — still to flesh out (before /design-to-spec)** | 1) **interaction matrix** (respond/direct/hybrid per widget — the "how many overdue tasks ANSWERS" driver); 2) **tasks-count parity tool** (reuse My Tasks grid saved-query, server-side count) — the acceptance test; 3) **parity verb sets per widget** (email done; tasks/matters/briefing/calendar/document TBD); 4) **widget-type(4)↔context-type(6) map**; 5) **Bindings vs analysistool+handler** per tool; 6) **registration-contract required fields**. Data-access lanes DONE (Dataverse-tools / RAG / domain services; NOT external MCP — native MCP-shaped BFF tools). |
| **Next Actions (explicit)** | **(1)** Deploy the auth revert (owner-gated): re-sync master → conflict-check → build → `Deploy-BffApi.ps1` (Release) + rebuild libs + `Deploy-SpaarkeAi.ps1 -DataverseUrl https://spaarkedev1.crm.dynamics.com` → re-UAT cold-start. **(2)** Flesh out R3 design items 1-6 above (start with interaction matrix + tasks-count tool). **(3)** `/design-to-spec` → `/project-pipeline` on R3 design.md. **(4)** R2 090 wrap-up (test-diet, README/plan→Complete, INDEX). |
| **Deploy playbook (learned this session)** | Master drifts every session → ALWAYS `git fetch` + merge origin/master before deploy (merges have been clean — no overlap on our files). FF branch→master after. Husky hook is ENV-BROKEN here (`dotnet format` SIGKILL + bare `prettier` not on PATH) → commit with `--no-verify` (owner-approved; CI re-formats on push). Code-page deploy publish step occasionally hits transient `0x80071151` concurrent-publish → just retry. |

---

## Quick Recovery (READ THIS FIRST — Phase D history below; see CURRENT STATE above for Phase C)

| Field | Value |
|-------|-------|
| **Task** | **Phase D implementation COMPLETE + defers cleared**: 030-038 ✅ (all committed) + **DI-01 (FR-D7 projection) ✅ + DI-02 (compose flush) ✅** (owner: "no defer issues — address in-project"; both done). Only **039** (deploy+verify D — now a PURE deploy) remains to close Phase D — then Phase 5 C (040-043) + 090 wrap-up. |
| **Step** | Clean checkpoint — everything committed + pushed; branch synced to master (0 behind); **PR #743** open (ready-for-review → master). Next: 039 deploy OR start Phase C. |
| **Status** | in-progress (Phase D impl + defers done; 039 deploy is owner-gated/outward-facing; owner mid-E2E on A/B) |
| **Working tree** | CLEAN. Branch `work/spaarkeai-assistant-enhancements-r2` @ **7f3930c5c** = origin, **0 behind master** (Update-Only sync merged 67 master commits in; only conflict was projects/INDEX.md union, resolved; post-merge BFF build + 90 tests + typecheck all green). |
| **PR** | **#743** ready-for-review → master (https://github.com/spaarke-dev/spaarke/pull/743). CI runs on push. NOT merged (project incomplete; PR is the merge vehicle at 090). |
| **Defers** | **ZERO open.** All 4 resolved in notes/defer-issues.md: DI-01 (FR-D7 projection — done, committed 513dd03e0), DI-02 (compose flush-on-unmount — done, 513dd03e0), DI-03 (FR-D5 — shipped in 036), DI-04 (FR-D9 — shipped in 034). No GitHub issues filed (all delivered). |
| **038 (FR-D11) outcome** | Deterministic push (not the probabilistic 022 proposer alone): Reanalyze BindingId resolved via the SAME capability-discovery seam (`consumerType=chat-summarize`+`consumerCode=reanalyze`, disambiguated from the pre-existing Chat Summarize binding), pushed onto the reactive `useConsumerChips` surface via 3 complementary paths (immediate seed on doc-tab focus / catch-up effect / `getAppendedLocalChips` persistence). Click → existing `dispatchBinding`→`dispatchConsumer` Click path, no new endpoint/decider. `useConsumerChips.acceptChips` got one additive dedupe fix (bindingId-based) — zero behavior change for existing callers, verified by the full 626-test conversation suite. Step 9.5 review caught+fixed a real pre-merge bug: an unstable effect dependency + missing in-flight-dispatch guard could have re-populated the chip strip mid-dispatch; fixed + regression-tested. Files: ConversationPane.tsx, useConsumerChips.tsx, `__tests__/ConversationPane.reanalyze-chip.e2e.test.tsx` (new, 5 tests). typecheck Surface-owned 0.
| **Commits this stretch** | 833561809 (033 FR-D10) · dd491b021 (035+037 D3) · 2f787e452 (escalation checkpoint) · 0278396b6 (036 FR-D5) · 3402b4f02 (034 FR-D9 Path B) · 0c3156393 (038 FR-D11) · 513dd03e0 (DI-01 FR-D7 projection + DI-02 compose flush) · 0f2b68fb9 (defer-status doc) · 7f3930c5c (merge master → branch, Update-Only sync). All pushed. |
| **Next Action (explicit)** | **039 is owner-gated** (outward-facing deploy; owner doing A/B E2E). When cleared: run `task-execute` on `tasks/039-deploy-verify-d.poml` — (a) `pwsh -File scripts/Deploy-BffApi.ps1` (hash-verify + healthz), (b) rebuild the Compose shared lib (`npm run build` in `src/client/shared/Spaarke.Compose.Components` — DI-02 flush fix) + `npm run build` in `src/solutions/SpaarkeAi` (cache-clear `rm -rf dist/ node_modules/.vite/ .vite/` first) → `pwsh -File scripts/Deploy-SpaarkeAi.ps1 -DataverseUrl 'https://spaarkedev1.crm.dynamics.com'`, (c) verify Phase D E2E. **ALTERNATIVELY** start **Phase C** (040-043 email visibility) and leave deploys batched. Ask owner which. |
| **034/036 done** | **036 FR-D5** (owner: full paired slice): BFF restore-DTO `uploadedFiles` projection + display-only chip rehydrate through host-owned `FilesAttachedIndicator` (SprkChat seam AVOIDED — the initialAttachments seam's side-effect cascade was verified real; shared lib untouched). **034 FR-D9** (owner: Path B): server-side ADR-024 regarding write on `sprk_analysis` (runtime DataverseServiceClientImpl, pure unit-tested StageAnalysisRegardingFields) + relaxed doc-anchor + self-contained HistoryOverlay picker. Independent ADR-024 review PASSed the core write; W1-W5 fixed (dropped non-deployed `sprk_regardingrecordurl` → reduced set §6.5 Path A; 7 domain tests; resolver-dup Path A doc'd in deviations.md; parity fail-fast). |
| **(resolved)** | ~~master c700d1b0b touched a 034 Dataverse file → reconcile at merge~~ → RESOLVED: auto-merged cleanly during the Update-Only sync (see the 090 merge note row above). |
| **036 (FR-D5) — owner: DO FULL PAIRED SLICE NOW** | Escalation: `UploadedFiles` manifest is server-persisted but exposed on NO client restore GET, AND SprkChat has no restore seam. Owner chose full slice: (1) BFF — project a minimal `uploadedFiles:{fileId,fileName,contentType,sizeBytes}[]` onto the restore DTO the History flow lands on (`RestoredSession`/`SessionRestoreResponse`; restore svc already loads full StoredSession); (2) shared-lib — add optional `initialAttachments` prop to SprkChat (`@spaarke/ui-components`) seeding `useChatFileAttachment` (fallback: parallel `FilesAttachedIndicator` render); (3) client — ConvPane passes restored uploadedFiles → SprkChat. Files: BFF restore DTO + SessionPersistenceService + @spaarke/ui-components SprkChat + ConversationPane.tsx. See DI-03. |
| **034 (FR-D9) — owner: PATH B (server-side regarding write)** | Escalation: Matter Analyses tab is driven by ADR-024 `sprk_regardingmatter`; promote endpoint can't write regarding & 400s on doc-less sessions. Owner chose Path B: extend `AnalysisPromoteRequest` + `PromoteSession` + `DataverseWebApiService.CreateAnalysisAsync` to write the ADR-024 regarding 5-field-set (build/reuse a server-side analysis regarding resolver — no hand-rolled single-field write) + relax the document-anchor when a regarding target is supplied; client — self-contained matter/project picker in HistoryOverlay (escalate only if a picker truly needs ConvPane/NavigationService). Files: AnalysisEndpoints.cs + Spaarke.Dataverse + HistoryOverlay.tsx. See DI-04. |
| **Escalation history (all resolved)** | 4 escalations arose during Phase D (plan under-scoped 2 tasks); ALL resolved: DI-01 (FR-D7 projection — done), DI-02 (compose flush — done), DI-03 (FR-D5 → task 036 full slice, owner-chosen), DI-04 (FR-D9 → task 034 Path B, owner-chosen). See notes/defer-issues.md (all RESOLVED) + notes/deviations.md (2 §6.5 Path-A: reduced ADR-024 set on sprk_analysis; resolver-dup across BFF/Dataverse layer). |
| **Pre-existing, NOT ours to fix** | Surfaced during verification: 4 NetArchTest failures (Graph isolation/DI-options in Communication/FileAccess/Office) + a `RichFilePreviewDialog` jest-mock drift breaking 4 ComposeWorkspace tests — both baseline repo state, documented in defer-issues.md. |
| **090 merge note** | On merge-to-master, `DataverseServiceClientImpl.cs` (034) auto-merged cleanly with master's `c700d1b0b` during the Update-Only sync — no residual conflict expected. |
| **Wave D3 outcome** | **035** (opus/xhigh): `handleSelectHistorySession` now drives the rich `/tabs` restore; the fix lives in **WorkspacePane.tsx** (clear-before-restore, synchronous debounced-PATCH cancellation so an empty set never overwrites the reopened session's store). **2 independent opus reviews**: core fix verified airtight; caught+fixed a compose-adoption **marker-leak** (Finding 1) that could re-expose the hazard on a rapid-switch path + a **test gap** (Finding 3 — now asserts B never gets a `tabs:[]` PATCH) + a non-compose-tab leak on adoption (Finding 2 — now `clearAllTabs({preserveWidgetTypes:['compose']})`). `onSelectSession` contract unchanged (composes with 037). **037** (sonnet): HistoryOverlay rebuilt — up-arrow gone, row-click opens, ⋮ Popover menu (Open/Rename/Set-related-slot/Delete via existing endpoints), Today/Yesterday/This-week grouping + search; fixed a real Fluent nested-Menu-as-submenu bug. typecheck Surface-owned 0; **85/85** SpaarkeAi tests green. Gates PASS both. **Deferred**: DI-01 (FR-D7 preview/count/tab-summary needs a BFF sessions-projection extension → fold into 039), DI-02 (TipTap-flush residual). See notes/defer-issues.md. |
| **033 outcome** | SAFE per-doc TTL path built (owner "continue" = go-ahead). `StoredSession.Ttl` (int?, -1=never-expire) DERIVED from filed-state (`HostContext.EntityType=="sprk_analysisoutput"`) on every write-through; unfiled→null (90-day container default). **Code-review Critical caught+fixed**: filed-state (HostContext) now persists+restores through the Cosmos warm tier so a Redis-eviction+reload+next-turn can't silently revert a filed doc to 90 days (ADR-040 warm-restore-survival pattern). 5 new tests incl. warm-reload regression; 744 Chat/Sessions/persistence/restore tests green; publish 52.37 MB (<60, delta 0). Both gates PASS. Full record: notes/d10-ttl-spike.md "IMPLEMENTATION". Risky fallback (container-TTL removal + cleanup job) NOT built — escalation trigger did not fire (spike conclusive). |

### Task 032 — DONE (2026-08-06, subagent + orchestrator). Deploy deferred to 039.
- Stored writable `Title` on StoredSession + ChatSession (additive); `PATCH /api/ai/chat/sessions/{id}` rename (204/400/404/401, mirrors 031's existence check); cheap title-gen at messages[0] via existing `IOpenAiClient.GetCompletionAsync` (maxOutputTokens:16 — reuses SummarizationCompressionService's primitive, ADR-039-clean, no fork). Fallback: generated → first-user-message (never a bare timestamp). Path C on the title-source-of-truth tension (ListRecentSessions prefers stored Title, legacy heuristic = fallback). Files: StoredSession, ChatSession, ChatSessionManager, SessionPersistenceService, ChatHistoryManager, ChatEndpoints + 8 tests. Verified: ChatEndpointsTests 20/20; full suite green. Publish ~47 MB, no packages.
- **Orchestrator fix**: task-022 left the eval gate red — `P2LoopInjectionEvalSuiteTests.FullCatalog_EveryClosedCatalogConsumerType_HasAnEvalFamily` requires every `ConsumerTypes.All` member to have a golden-utterance case; `assistant-suggest` (added by 022) had none. Added coverage-only case **GU-141** to `tests/integration/contract/Eval/golden-utterances.json` (mirrors GU-138 email-triage — a non-loop-projected facade; real coverage is AssistantSuggestionServiceTests). P2 eval suite now 20/20.

### Wave D1 — DONE (030+031, 2026-08-06). Deploy deferred to 039.
- **030 (FR-D2)**: `awaitCosmosWrite` optional flag threaded ISessionPersistenceService.PersistSessionAsync → SessionPersistenceService → ChatSessionManager.UpdateSessionCacheAsync → ChatHistoryManager.AddMessageAsync (isFirstMessage = pre-append count==0). messages[0] AWAITED (survives Redis eviction); later turns unchanged fire-and-forget (no latency regression). ADR-040 (no new store). Tests: `tests/integration/regression/Ai/FirstTurnCosmosWriteSurvivesEvictionTests.cs` (3) + ChatHistoryManagerTests (+2).
- **031 (FR-D3)**: `GetHistoryAsync` 404s on genuinely-missing session via `ChatSessionManager.GetSessionAsync` (3-tier; null=missing, empty-session=200), mirrors GetTabsAsync/SwitchContext/Delete. Tests in `tests/integration/Spe.Integration.Tests/Api/Ai/ChatEndpointsTests.cs` (+2).
- **Orchestrator fixes (consolidated build)**: (a) 6 Moq expression-tree sites needed explicit `It.IsAny<bool>()` + callback `bool __` param (the subagent missed these — CS0854); (b) **pre-existing fixture rot** from copilot merge `096a5f754` (unregistered `IEmailTemplateService` + `IEmailDraftAi`) broke the ENTIRE ChatEndpointsTests class — added 2 stubs, restoring all 13 tests. Verified: BFF build clean; 030 22 tests; 031 + full ChatEndpointsTests 13 tests; publish 46.89 MB (no packages).

### Phase B — COMPLETE (020–025, 2026-08-06)
All deployed to spaarkedev1 (BFF `spaarke-bff-dev` + code page `sprk_spaarkeai`). Proactive grounded suggestion turn live: contextType pre-filter → SUGGEST-FOLLOWUPS Action (proposer, not dispatcher) → ≤3 content-specific chips once per tab; manual refresh; dev trace. Owner E2E checklist in `notes/b-deploy-verify.md` (document/summary tabs exercise full content-specificity today; **email tabs gated on Workstream C** 040–043).

### Task 024 — DONE (2026-08-06). Deploy deferred to 025.
- Dev-only proactive-selection trace: `recordSuggestTrace` (module-level in ConversationPane.tsx) emits `console.debug("[sprk:suggest-trace]", …)` + a bounded `window.__sprkSuggestTrace` ring buffer, gated on `process.env.NODE_ENV === "production"` → Vite dead-code-eliminates it from the prod bundle (verified: 0 occurrences in dist). Records `{at, tabId, contextType, trigger, chips[]}`; each chip carries the model's `reason`. Server-side candidate list is already logged by AssistantSuggestionService.
- Called in `fireProactiveSuggestion` at the selection point (both first-open + refresh). Test: assertion in `ConversationPane.proactive-suggest.e2e.test.tsx` (trace populates in dev). typecheck Surface-owned:0; prod-strip verified.

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
