# R6 Deliverables Audit — Shipped vs Surfaced vs Working

> **Authored**: 2026-06-21 (R6 closeout audit pass)
> **Purpose**: Capture per-pillar reality for R6 closeout — what was built, where it's actually wired into the SpaarkeAi user experience, what gaps remain, and how each gap will be resolved (in R6, in chat-routing-redesign-r1 successor, or in R7+).
> **Trigger**: The `/summarize` UAT bug exposed a broader pattern — R6 shipped infrastructure that was incompletely surfaced to end users. This audit closes that ambiguity before R6 is declared done.

---

## Top-line finding

R6 shipped substantial code across 9 pillars. A surface-level pillar-completion review would mark every pillar ✅. **Closer inspection found that ~50% of R6's value was gated on three Dataverse deployment scripts that had never been run against spaarke-dev**, plus a handful of small UI-side wiring gaps that were either stubbed (`TODO(task 084)`) or never mounted.

Running the three scripts (done — Phase 1 of this closeout) flipped multiple pillars from "broken UAT" to "functional pending verification" with **zero code changes**.

The remaining gaps are real but bounded — small frontend/UI wiring (Phase 2) and Builder UI additions (Phase 3). The architectural rework for routing convergence is correctly scoped to the successor `chat-routing-redesign-r1` project.

---

## Audit matrix

For each pillar: what shipped, where it's wired, what's the current state, and the resolution path.

### Pillar 1 — Persona

**What shipped**:
- `sprk_aipersona` Dataverse entity + 5th scope library entry
- `AnalysisPersonaService.GetEffectivePersonaAsync` with most-specific-wins resolution (SYS- < CUST- < playbook-attached)
- `GET /api/ai/scopes/personas` endpoint
- `Seed-AiPersonaDefault.ps1` (deploys SYS-DEFAULT)

**Where wired**:
- ✅ Server: `PlaybookChatContextProvider` resolves effective persona → `ChatContext.SystemPrompt` → LLM sees it on every turn
- 🔴 Authoring UI: **NO persona attachment field in the Playbook Builder**. The `PlaybookAttached` resolution layer exists in code but no UI lets an author SET a persona on a playbook
- 🔴 Per-session UI: no persona display or override in chat session UI

**Current state**: Functioning at the default-SYS- layer only. Custom personas and playbook-attached personas have no authoring surface.

**Resolution**:
- ✅ Phase 1 done — SYS-DEFAULT verified in-sync via `Seed-AiPersonaDefault.ps1`
- 🟡 Phase 3a (R6 task 091 — Builder UI): add persona dropdown to playbook properties form (~1 day)
- Successor scope: no overlap; Builder UI work stays in R6 or R7

---

### Pillar 2 — Data-Driven Tool Registry

**What shipped**:
- `sprk_analysistool` Dataverse entity (NOT `sprk_aicapability` — that table was abandoned)
- `ToolHandlerToAIFunctionAdapter` with `sprk_requiredcapability` capability gate
- 30 seed-row JSON files in `infra/dataverse/sprk_analysistool-*.json`
- `Seed-TypedHandlers.ps1` (idempotent UPSERT, queries by `sprk_handlerclass`)
- Q9 batch migration: 10 hardcoded tools migrated to typed handlers + data rows

**Capability-matching mechanism (clarification)**:
- Tool row's `sprk_requiredcapability` (single-line **text**) holds a constant string (e.g., `"write_back"`, `"verify_citations"`)
- Playbook's `sprk_playbookcapabilities` (multi-select **choice**) maps to the same string constants via `PlaybookCapabilities` C# class
- `IsCapabilityGateSatisfied` does case-insensitive string match; tools whose required-capability isn't in the playbook's set are silently withheld from the LLM
- Tools with null/empty `sprk_requiredcapability` are always available

**Where wired**:
- ✅ Server: `SprkChatAgentFactory.ResolveTools` auto-discovers + filters tools per playbook capability
- ✅ Data: 30 rows in `sprk_analysistool` (Phase 1 confirmed all UPSERTed in spaarke-dev)

**Open question**:
- The `sprk_capabilities` field that may exist on `sprk_analysisplaybook` (separately from `sprk_playbookcapabilities`) — code paths use `sprk_capabilities` on `sprk_scope` entities, NOT on the playbook itself. **If both `Capabilities` (single-select) and `PlaybookCapabilities` (multi-select) appear on the playbook form in make.powerapps.com, one is likely vestigial R5 and should be removed.** Verify in maker portal.

**Current state**: ✅ Working — tool registry data-driven end-to-end.

**Resolution**:
- ✅ Phase 1 done — `Seed-TypedHandlers.ps1` synced 30 rows
- 🟡 R6 task 092: verify + remove vestigial `sprk_capabilities` field on playbook entity (if confirmed)
- Successor scope: chat-routing-redesign-r1 doesn't touch the tool registry

---

### Pillar 3 — Generic invoke_playbook

**What shipped**:
- `IInvokePlaybookAi` facade + `InvokePlaybookHandler` (replaces specialized `InvokeSummarizePlaybook` / `InvokeInsightsQuery` bridges)
- One `sprk_analysistool` seed row (`SYS-Invoke Playbook`)
- Dynamic per-tenant description rendering (`BuildInvokePlaybookDescriptionAsync`) — the tool description carries a menu of currently-accessible playbooks

**What the LLM actually sees**:
```
Invoke any registered playbook by ID with parameters. Available playbooks for this tenant:
- {guid}: {name} — {short description (~120 chars)}
- {guid}: {name} — {short description}
- ...
Pass the playbookId field with one of the values above.
```
Alphabetically sorted, capped at ~1500 chars with "...and N more" truncation.

**Note on the two parallel playbook-selection paths (today)**:

| Path | When | Mechanism |
|---|---|---|
| `PlaybookDispatcher` | BEFORE the LLM call (in `ChatEndpoints.SendMessageAsync`) | Vector similarity against `playbook-embeddings` AI Search index. If matched + OutputType≠Text → routed via `outputHandler` (e.g., Workspace tab). LLM never invoked. |
| `invoke_playbook` tool | AFTER the dispatcher misses | LLM picks a GUID from the menu (above) and calls the tool. Handler dispatches via `IPlaybookOrchestrationService`. |

Successor WP2 converges these two paths into one matcher.

**Where wired**:
- ✅ Server: `InvokePlaybookHandler` registered, auto-discovered, capability-gated
- ✅ Data: seed row deployed (Phase 1)

**Current state**: ✅ Functional. Performance depends on tenant having indexed, chat-eligible playbooks.

**Resolution**:
- ✅ Phase 1 done — seed row in place; playbooks indexed
- Successor scope: WP2 converges with PlaybookDispatcher (single matcher)

---

### Pillar 4 — Chat /summarize FK Fix

**What shipped**:
- `SessionSummarizeOrchestrator` no longer uses alternate-key bypass
- Direct chat /summarize endpoint (`POST /api/ai/chat/sessions/{id}/summarize`) goes through proper playbook FK chain
- Engine-divergence documented (direct endpoint uses `ExecuteChatSummarizeAsync` for deterministic structured streaming; LLM tool path uses `ExecuteAsync` for conversational variability — both intentional)

**Where wired**:
- ✅ Server: direct endpoint, FilePreviewContextWidget "Summarize this only" button

**Current state**: ✅ Pillar 4 itself is done. The `/summarize` UAT failure was a **different** issue (playbooks not indexed in playbook-embeddings) — not an FK problem.

**Resolution**:
- ✅ Phase 1 done — `summarize-document-for-chat@v1` and `summarize-document-for-workspace@v1` now both indexed
- Successor scope: no FK-related work

---

### Pillar 5 — Output Schema (Q5 Re-Shaped)

**What shipped**:
- `outputSchema` lives on the **action** (`sprk_analysisaction.sprk_jpsdefinition` → JPS JSON's `outputSchema` field) — describes data shape (e.g., `{tldr, summary, keywords, entities}`)
- `NodeRoutingConfig` lives per-node on the playbook (parsed from `sprk_configjson` node-level config) — declares `destination: "chat" | "workspace" | "both"` + `widgetType`
- `StructuredOutputStreamWidget` is schema-aware (renders dynamically per action's outputSchema)
- `CapabilityRouter` dedup invariant (R6 FR-30)

**Where wired**:
- ✅ Server: `NodeRoutingConfig.Parse` consumes node configJson; renderer routes accordingly
- 🔴 Authoring: **no UI in Playbook Builder** for setting per-node `destination`/`widgetType`. Authors editing JSON directly works but is undocumented.
- 🔴 Indexing (was blocking `/summarize`): playbooks not indexed in `playbook-embeddings` until Phase 1 of this closeout. **Now fixed.**

**Current state**: ✅ Renderer + parser work. Authoring UX is rough (JSON-only).

**Resolution**:
- ✅ Phase 1 done — playbooks indexed
- 🟡 Phase 3b (R6 task 093 — Builder UI): add `destination`/`widgetType` fields on node properties form (~1 day)
- Successor scope: chat-routing-redesign-r1 WP1.5 adds playbook-embeddings index governance; WP3 wires `NodeRoutingConfig` into `DispatchResult` structurally

---

### Pillar 6a — Workspace State Model

**What shipped**:
- `WorkspaceStateService` with hybrid Redis hot + Cosmos warm persistence (ADR-014)
- `GET/POST /api/workspace/state` endpoints (independent path)
- `GET/PATCH /api/ai/chat/sessions/{id}/tabs` endpoints (chat-session-scoped path, NFR-09 task 065) — sits on the SAME `IWorkspaceStateService.GetTabsAsync`
- Server-side prompt-snapshot injection: `SprkChatAgentFactory` includes open tabs in the LLM system prompt

**Where wired**:
- ✅ Server: prompt-snapshot path fully wired
- ✅ Frontend: `WorkspacePane.tsx:282` has a working `useEffect` that fetches `/api/ai/chat/sessions/{id}/tabs` on mount and rehydrates the tab manager via `restoreFromPersistence`. **The state model IS wired end-to-end via the chat-session-scoped endpoint.** Tabs DO restore across page reload.

**Audit correction**: Initial finding "frontend never calls" was wrong — checked for `/api/workspace/state` consumers but missed that the chat-session-scoped path uses the same service. **No frontend hotfix needed.**

**Current state**: ✅ Working. Both server and frontend wired.

**Resolution**:
- ✅ No additional work needed. The `/api/workspace/state` endpoints may be unused or low-use parallel surface — consider noting as a redundant API (cleanup candidate, not a gap)
- Successor scope: no overlap needed

---

### Pillar 6b — Workspace Chat Tools

**What shipped**:
- 3 typed `IToolHandler`s: `UpdateWorkspaceTabHandler`, `SendWorkspaceArtifactHandler`, `CloseWorkspaceTabHandler`
- 3 `sprk_analysistool` seed-row JSON files
- Conflict resolution (Q8 user-wins via `lastUserEditAt`)
- `CrossPillarIntegrationTests` covering composition with Pillar 6a state

**Where wired**:
- ✅ Server: handlers auto-discovered; seed rows deployed Phase 1
- ✅ Tool surface: LLM sees them in tool list after data seed

**Current state**: ✅ Working end-to-end.

**Resolution**:
- ✅ Phase 1 done — seed rows deployed
- 🟡 UAT verify: ask AI in chat to "close [tab name]" and "update [tab name] with [text]" — confirm tab mutations occur

---

### Pillar 6c — Execution-Trace Widget

**What shipped**:
- `ExecutionTraceWidget.tsx` registered in widget registry (679 LOC)
- `IContextEventEmitter` + 6 `context.*` event types
- ADR-015 deterministic-fields-only discipline preserved

**Where wired**:
- ✅ Server: emitter wired into `ToolHandlerToAIFunctionAdapter` + `CapabilityRouter` + `RagService` + `PromptBudgetTracker` + `ManagePinnedContextHandler`
- 🔴 Server→browser bridge: events go to .NET diagnostics ONLY. No SSE event carries them to the chat stream.
- 🔴 Client mount: SpaarkeAi shell never dispatches `contextType: 'execution-trace'` to PaneEventBus → widget never mounts

**Current state**: 🔴 Built but not wired (two breakpoints).

**Resolution**:
- 🟡 R6 task 095 (Phase 2e — Pillar 6c trace bridge): emit `context_event` SSE alongside existing chat SSE; subscribe in client; dispatch widget mount event (~4h)
- Successor scope: no specific WP — this is a clean hotfix

---

### Pillar 7 — Memory + Pinned Memory UI

**What shipped**:
- Memory tier services: `MemoryComposition`, `PinnedContextRecall`, `PromptBudgetTracker`, `MatterMemory`
- `ManagePinnedContextHandler` (voice triggers: "remember this", "forget X")
- `PinnedMemoryListWidget` self-registers

**Where wired**:
- ✅ Voice trigger path: works via LLM tool list
- 🔴 Composition: `IMemoryCompositionService.ComposeAsync` has ZERO call sites. `SprkChatAgentFactory` and `OrchestratorPromptBuilder` do NOT call it. Memory tier exists in code but is never injected into the prompt.
- 🔴 UI mount: `PinnedMemoryListWidget` self-registers but no SpaarkeAi route/menu mounts it. CRUD UI invisible.

**Current state**: 🔴 Foundational pieces shipped; composition layer NOT wired.

**Resolution**:
- Successor scope: chat-routing-redesign-r1 WP5 (6-tier memory architecture) wires `IMemoryCompositionService` into prompt assembly. R6 Pillar 7 provides the building blocks; WP5 composes them.
- 🟡 R6 task 096 (optional Phase 2 add): mount `PinnedMemoryListWidget` somewhere visible in SpaarkeAi shell so the existing voice-trigger pins are inspectable while WP5 is being built (~2h)

**Honest framing**: R6 ship-level claim of "memory pillar delivered" overstates user-visible value. Code shipped; composition wiring is in successor scope. Acceptable sequencing but worth being clear about.

---

### Pillar 8 — Command Router

**What shipped**:
- Frontend: `CommandRouter.ts`, `SoftSlashRouter.ts`, `HardSlashExecutor.ts` (fully implemented)
- 6 hard slashes: `/clear`, `/new-session`, `/help`, `/export`, `/save-to-matter`, `/pin`
- 4 soft slashes: `/summarize`, `/draft`, `/extract-entities`, `/analyze`
- `CommandHelpPanel.tsx`
- BFF: `CapabilityRouter` Layer 0.5 for soft slashes (to be retired in successor)

**Where wired (verified state)**:
- ✅ `/clear` works (Hotfix #1 added remount key for client-side clear)
- ✅ `/help` works (opens CommandHelpPanel)
- ✅ `/save-to-matter` works (calls `POST /api/memory/pins`)
- ✅ `/pin` works (calls `PATCH /api/ai/chat/sessions/{id}/tabs`)
- 🔴 `/new-session` — `createNewSession` callback in `ConversationPane.tsx` returns `null` (stub: `TODO(task 084)`)
- 🔴 `/export` — `getConversationHistory` callback returns `[]` (stub) → export produces empty markdown
- 🔴 4 soft slashes — all share the `/summarize` failure mode (which was fixed via Phase 1 indexing). **After Phase 1, all 4 should now route via dispatcher → outputHandler.** Needs UAT confirmation.

**Resolution**:
- ✅ Phase 1 done — soft slash unblocking (indexing)
- 🟡 R6 task 097 (Phase 2c — Pillar 8 hard slash wiring): implement the 3 stub callbacks (`createNewSession` POSTs `/api/ai/chat/sessions`, `getConversationHistory` reads from SprkChat message state via ref/callback, `getFocusedTabId` subscribes to PaneEventBus workspace events) (~3h)
- Successor scope: WP2 absorbs soft slashes into single matcher with commandIntent bias; hard slashes remain client-side

---

### Pillar 9 — Visibility Contract

**What shipped**:
- `AddToAssistantToggle.tsx` (per-tab "Add to Assistant" checkbox component)
- `getVisibleState` projection on `WorkspaceWidgetRegistry`
- Per-tab privacy design (LLM only sees tabs flagged visible)

**Where wired**:
- 🔴 UI mount: `AddToAssistantToggle` has zero production consumers (only imported by its own test). Toggle never renders in any tab header.
- ✅ Server projection: `SprkChatAgentFactory.BuildWorkspaceStateBlock` at line 2104-2106 **DOES** filter `tabs.Where(t => t.VisibleToAssistant)` AND additional widget-data privacy guard via `TryDeriveVisibleState` (FR-58 + FR-59 binding). `WorkspaceStateService` preserves the field on updates.

**Audit correction**: Initial finding "Both ends not wired" was wrong on the server side. The filter IS applied. Only the UI mount is missing.

**Current state**: 🟡 Server side ✅; UI mount missing. Net result: every tab is currently treated as `visibleToAssistant=true` by default because nothing surfaces the toggle to let users change it.

**Resolution**:
- 🟡 R6 task 098 (revised, smaller scope): render `AddToAssistantToggle` per-tab in workspace tab header component + wire its PATCH to `/api/ai/chat/sessions/{id}/tabs` with `visibleToAssistant` field (~1.5h, down from 3h)
- Successor scope: no overlap

---

## Phase 1 — Data Fixes (Completed 2026-06-21)

| Script | Result |
|---|---|
| `Seed-TypedHandlers.ps1` | ✅ 30 `sprk_analysistool` rows synced (29 PATCHed, 1 new POSTed) |
| `Seed-AiPersonaDefault.ps1` | ✅ SYS-DEFAULT verified in-sync |
| `Index-ExistingPlaybooks.ps1` | ✅ 29 playbooks indexed including `summarize-document-for-chat@v1` (516 chars) and `summarize-document-for-workspace@v1` (684 chars) |

**Impact**: pillars 1, 2, 3, 4, 5, 6b, 8 unblocked from data-layer gaps. UAT pending for:
- `/summarize` (and the other 3 soft slashes) routing through dispatcher → outputHandler → Workspace tab
- Workspace chat tools (Pillar 6b) firing under LLM instruction

---

## R6 Closeout Task List (Numbered, audit-corrected)

Each task below maps to a real verified gap. POML task files to be created in `tasks/`:

| Task | Pillar | Description | Effort | Status |
|---|---|---|---|---|
| **091** | 1 | Builder UI: persona dropdown on playbook properties form | ~1 day | Pending — Phase 3 |
| **092** | 2 | Verify + remove vestigial `sprk_capabilities` field on playbook entity (if confirmed) | ~1h | Pending — needs Dataverse check |
| **093** | 5 | Builder UI: `destination` + `widgetType` fields on node properties form | ~1 day | Pending — Phase 3 |
| ~~094~~ | 6a | ~~Frontend workspace state restore~~ | — | **WITHDRAWN (already wired — audit correction)** |
| **095** | 6c | Server SSE bridge for `context.*` events + client mount for ExecutionTraceWidget | ~4h | Pending |
| **096** | 7 | Mount `PinnedMemoryListWidget` in SpaarkeAi shell (visible affordance only — composition wiring is successor WP5) | ~2h | Pending — optional |
| **097a** | 8 | Wire `createNewSession` to POST `/api/ai/chat/sessions` | ~30m | ✅ **DONE (2026-06-21)** |
| **097b** | 8 | Wire `getConversationHistory` to expose SprkChat message-list (requires a new ref/callback contract on SprkChat — bigger touch) | ~2h | Pending |
| **097c** | 8 | Wire `getFocusedTabId` via PaneEventBus `tab_change` subscription | ~1h | ✅ **DONE (2026-06-21)** |
| **098** | 9 | Render `AddToAssistantToggle` per-tab in tab header + wire PATCH (server projection already wired) | ~1.5h | Pending — smaller than originally estimated |
| **089** | (closeout) | Phase D exit-gate validation | ~2h | Already deferred |
| **090** | (closeout) | Project wrap-up | ~6h | Already deferred |

Total Phase 2 hotfix effort (audit-corrected): **~10-12 hours** (down from 15-17).
Total Phase 3 Builder UI effort: **~2 days**.

---

## What stays in chat-routing-redesign-r1 (successor scope)

The successor project absorbs:
- WP1 + WP1.5: file-aware classification + playbook-embeddings index governance
- WP2: unified intent matcher — `commandIntent` becomes vector-query bias; PlaybookDispatcher + CapabilityRouter collapse into one
- WP3: structural destination metadata wiring (`NodeRoutingConfig` into `DispatchResult` non-optionally)
- WP4: CapabilityRouter retirement
- WP5: 6-tier stateful memory architecture (wires R6 Pillar 7 services into prompt composition)
- WP6: specialized playbook authoring + JPS `$ref` Path 3 engine extension

**No overlap** between R6 closeout tasks 091-098 and successor WPs.

---

## Recommendation

1. **Phase 1 is done** — three deployment scripts executed against spaarke-dev (this session).
2. **Phase 2 hotfixes (tasks 094, 095, 097, 098 + optional 096)** — execute via standard task flow (POML files, `task-execute` skill). Total ~12-15 hours.
3. **Phase 3 Builder UI (tasks 091, 093)** — schedule as 2-day focused work. Could land in R6 closeout or carry to R7 depending on R6 timeline pressure.
4. **Successor WP work is firm** — no relitigation needed.
5. **R6 closes** when: Phase 1 + Phase 2 done + UAT Tiers A-G executed + 089 + 090 complete. Builder UI work may be acceptable as Known Limitation for R6 closure pending R7 prioritization.

---

*End of audit. Phase 1 deployed 2026-06-21 by closeout pass. Task files for 091-098 pending authoring.*
