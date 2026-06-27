# Defer / Issue Tracking — spaarke-ai-platform-unification-r6

> **Source of truth** for deferred work + newly-discovered issues in this project.
> Each entry has a paired GitHub Issue. See `/project-defer-issue-tracking` skill for the protocol.
>
> **Rollup view**: `gh issue list --label spaarke-ai-platform-unification-r6` (visible to whole team via portfolio board)
> **CLAUDE.md §11 rule**: every entry MUST name a concrete behavior or contract that fails without it. "For future flexibility" / "improve testability" / "separation of concerns" = NOT a valid deferral reason — refuse to file.

---

## Open (in priority order)

### ISS-001 — `SYS-Recall_Session_File` repeatedly fails ("not present in this session") in chat (TIER-C-B)

| Field | Value |
|---|---|
| **Status** | Open |
| **Urgency** | now |
| **Filed** | 2026-06-26 |
| **Source** | UAT screenshot 2026-06-25 + `notes/tier-c-b-recall-session-file-diagnostic.md` |
| **GitHub Issue** | [#470](https://github.com/spaarke-dev/spaarke/issues/470) |

**Description**

User UAT on 2026-06-25 showed the LLM invoking `SYS-Recall_Session_File` 7+ times in a single chat turn, each returning the literal message "It appears the document 'X' is not currently present in this session" for files like `US12271583B2_FileUpload.pdf`, `SEC FORM 4_2.pdf`, etc. The `RecallSessionFileHandler` IS deployed and Active in `sprk_analysistool` (verified via MCP). The handler's behavior is correct (`session.UploadedFiles` doesn't contain the requested `fileId`), so the gap is upstream of the handler.

The diagnostic at `notes/tier-c-b-recall-session-file-diagnostic.md` enumerates 3 candidate root causes (session boundary — files uploaded in a previous session; LLM tool-selection — picks recall when knowledge-base-search would be correct; system-prompt leak — non-session filenames showing in prompt). Determining which scenario is reproducible requires fresh UAT in a known-state session.

This is **outside R6's responsibility** — `RecallSessionFileHandler` was added by chat-routing-redesign-r1 Phase 4 task 085. R6 owns deployment of the row (done); upstream behavior is successor scope.

**Entry-points**

- `src/server/api/Sprk.Bff.Api/Services/Ai/Handlers/RecallSessionFileHandler.cs:435-471` (lookup + "not found" return)
- `src/server/api/Sprk.Bff.Api/Api/Ai/ChatDocumentEndpoints.cs:520-548` (upload path that writes UploadedFiles[])
- `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/Chat/ChatSessionContinuityTests.cs` (FR-56 binding tests — passing)
- `projects/spaarke-ai-platform-unification-r6/notes/tier-c-b-recall-session-file-diagnostic.md` (full 3-scenario analysis + UAT test plan)

**Suggested fix** (if known)

Per the diagnostic, first run UAT in a fresh session with explicit upload-then-recall flow to distinguish scenarios. If session-boundary (#1): add UX clarity to surface "files in THIS session" vs "files in matter". If tool-selection (#2): tune `sprk_promptdescription` on RECALL-SESSION-FILE + DOCUMENT-SEARCH rows. If prompt-leak (#3): audit per-turn prompt assembly in `SprkChatAgentFactory.cs:346-367` and `PlaybookChatContextProvider.cs:152`.

**Estimated effort**: ~half day (depending on which scenario reproduces)
**Blockers**: Fresh UAT in spaarke-dev to distinguish 3 candidates
**Related**: chat-routing-redesign-r1 task 085 (handler), ChatSessionContinuityTests (FR-56)

---

### DEF-001 — Task 095 Phase 3: BFF emit `context_event` SSE so `ExecutionTraceWidget` lights up

| Field | Value |
|---|---|
| **Status** | Open |
| **Urgency** | next-round |
| **Filed** | 2026-06-26 |
| **Source** | Commit `0a5bc7e05` — task 095 phased delivery |
| **GitHub Issue** | [#471](https://github.com/spaarke-dev/spaarke/issues/471) |

**Description**

R6 task 095 shipped the widget mount + frontend SSE bridge (commit `0a5bc7e05`): `IChatSseEventData` carries 14 typed context_event fields, `useSseStream` dispatches the `context_event` SSE event type, `ConversationPane` forwards each payload to the `context` PaneEventBus channel where `ExecutionTraceWidget` renders. Frontend contract is **complete and tested** (361/361 tests pass).

BFF emission is **NOT YET WIRED**: `ContextEventEmitter.cs` writes the 6 typed events to OpenTelemetry counters + structured logs but does NOT publish to a per-request SSE writer. Result: the widget mounts (Tier D UAT passes "widget renders") but stays empty in spaarke-dev — no events arrive.

Concrete failure mode: open SpaarkeAi chat in spaarke-dev → invoke any chat tool (e.g., `summarize`) → `ExecutionTraceWidget` shows empty state. Without this fix, the trace widget is "infrastructure-correct empty" rather than the Claude-Code-like activity surface R6 Pillar 6c promised.

**Entry-points**

- `src/server/api/Sprk.Bff.Api/Services/Ai/Telemetry/ContextEventEmitter.cs:125+` (6 emitter methods — add per-call SSE write)
- `src/server/api/Sprk.Bff.Api/Services/Ai/Telemetry/IContextEventEmitter.cs` (add `SetSseWriter` or similar contract)
- `src/server/api/Sprk.Bff.Api/Api/Ai/ChatEndpoints.cs:753-940` (the chat SSE stream region — set the writer at stream start, clear on end)
- Reference for design: commit message of `0a5bc7e05` documents the contract `IChatSseEventData` expects

**Suggested fix** (if known)

Add per-request `Channel<ContextSseEventDto>` sink to `ContextEventEmitter` (or `IContextSseRelay` scoped service injected via `IHttpContextAccessor`). `ChatEndpoints` resolves the relay at SSE stream start, attaches a consumer task that reads from the channel and writes `new ChatSseEvent("context_event", null, data)`. On stream end, detach + complete the channel. Filter by sessionId to avoid cross-request leakage. Use the SetSseWriter ambient pattern from ADR-033 streaming side-channel as precedent.

**Estimated effort**: ~2h
**Blockers**: none
**Related**: ADR-030 (PaneEventBus additive event types), ADR-033 (streaming side-channel pattern), commit `0a5bc7e05`

---

### DEF-005 — Slash commands focused project (Tier A/B slashes non-functional)

| Field | Value |
|---|---|
| **Status** | Open |
| **Urgency** | next-round |
| **Filed** | 2026-06-26 |
| **Source** | User UAT 2026-06-25: "defer in this project for now" |
| **GitHub Issue** | [#472](https://github.com/spaarke-dev/spaarke/issues/472) |

**Description**

UAT 2026-06-25 confirmed 10 slash commands non-functional or partially-functional at deploy time: `/clear`, `/new-session`, `/help` (hard slashes — partial); `/export`, `/save-to-matter`, `/pin` (hard slashes — broken or stubbed); `/summarize`, `/draft`, `/extract-entities`, `/analyze` (soft slashes — routing via `commandIntent` partially-wired). Pillar 8 backend (CommandRouter parser + soft-slash CapabilityRouter Layer 0.5) shipped in R6; user-facing wiring partially shipped (097a/097c done, 097b done in 095-adjacent commit `c076d2898`).

Per user direction the broader slash-UX work is **out of R6 closeout scope** — it's entangled with chat-routing-redesign-r1's `commandIntent` → `intentHint` rename + successor's tool-filtering replacement that retired CapabilityRouter.

Concrete failure mode: user types `/summarize` with no document open → LLM responds with conversational text, no Workspace tab opens (the playbook routing path requires the destination-aware chat handler chain that successor still owns). `/export` produces no markdown until 097b ships (done as of `c076d2898` 2026-06-25, awaiting UAT confirmation in this re-deploy).

**Entry-points**

- `src/solutions/SpaarkeAi/src/components/conversation/HardSlashExecutor.ts:1-` (hard slash dispatch surface)
- `src/solutions/SpaarkeAi/src/components/conversation/SoftSlashRouter.ts:1-` (soft slash dispatch surface)
- `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/PlaybookDispatcher.cs:760-800` (successor's chat-side routing — replaces CapabilityRouter)
- `projects/spaarke-ai-platform-unification-r6/notes/r6-deliverables-audit.md` — Pillar 8 section

**Suggested fix** (if known)

Scope a focused mini-project (`spaarke-slash-uat-completion-r1` or similar) — review each slash end-to-end against the new chat-routing-redesign-r1 dispatch path; fix per-slash gaps. Or roll into chat-routing-redesign-r1 task 142+ (already retiring SOFT_SLASH_TO_INTENT dict).

**Estimated effort**: unknown — needs spike
**Blockers**: chat-routing-redesign-r1 stability (CapabilityRouter retired, PlaybookDispatcher in progress)
**Related**: chat-routing-redesign-r1 PR #409 + Phase 7 (CapabilityRouter retirement), R6 task 080-088 (CommandRouter parser + hard/soft slash executors)

---

### DEF-002 — Builder UI 091: Persona dropdown on playbook properties form (Pillar 1 surface)

| Field | Value |
|---|---|
| **Status** | Open |
| **Urgency** | next-round |
| **Filed** | 2026-06-26 |
| **Source** | `r6-deliverables-audit.md` Pillar 1; task 091 POML stub created |
| **GitHub Issue** | [#473](https://github.com/spaarke-dev/spaarke/issues/473) |

**Description**

R6 Pillar 1 shipped data-driven persona resolution (`sprk_aipersona` entity + `AnalysisPersonaService.GetEffectivePersonaAsync` with most-specific-wins SYS- < CUST- < playbook-attached). `SprkChatAgentFactory.CreateAgentAsync` consumes the effective persona. SYS-DEFAULT is seeded in spaarke-dev.

Concrete failure mode: a maker wanting to author a custom persona (CUST- prefix) or attach a persona to a specific playbook has **no UI**. The Playbook Builder code-page has no persona dropdown. The CUST- and playbook-attached resolution layers exist in C# code paths but cannot be reached from the maker portal.

**Entry-points**

- `src/client/code-pages/PlaybookBuilder/src/components/properties/NodePropertiesForm.tsx` (add persona dropdown)
- `src/server/api/Sprk.Bff.Api/Services/Ai/PersonaResolver.cs` (the resolver already exists)
- `infra/dataverse/sprk_aipersona-sys-default-row.json` (seed precedent)
- `projects/spaarke-ai-platform-unification-r6/tasks/091-builder-ui-persona-dropdown.poml` (full task spec)

**Suggested fix** (if known)

Add a persona lookup field to the playbook properties form. Wire to `/api/ai/scopes/personas` endpoint (already exists) for type-ahead. Save selected persona ID on `sprk_analysisplaybook.sprk_personaid`. Display "Effective: SYS-DEFAULT" or the selected persona in the form header.

**Estimated effort**: ~1 day
**Blockers**: confirm `sprk_personaid` lookup field exists on `sprk_analysisplaybook` (or add via dataverse-create-schema)
**Related**: tasks 001-005 (R6 Pillar 1 backend), `notes/r6-deliverables-audit.md` Pillar 1

---

### DEF-003 — Builder UI 093: `destination` + `widgetType` fields on node properties form (Pillar 5 surface)

| Field | Value |
|---|---|
| **Status** | Open |
| **Urgency** | next-round |
| **Filed** | 2026-06-26 |
| **Source** | `r6-deliverables-audit.md` Pillar 5; task 093 POML stub created |
| **GitHub Issue** | [#474](https://github.com/spaarke-dev/spaarke/issues/474) |

**Description**

R6 Pillar 5 (Q5 re-shape) shipped `outputSchema` on `sprk_analysisaction` and `NodeRoutingConfig` JSON on `sprk_analysisplaybooknode.sprk_routingconfigjson`. `PlaybookOutputHandler` reads `destination` + `widgetType` from this routing config to route playbook outputs to chat / workspace / form-prefill / side-effect.

Concrete failure mode: a maker authoring a playbook node has **no UI** to set `destination` (enum: chat / workspace / form-prefill / side-effect) or `widgetType` (when destination=workspace). Result: every playbook node routes via the default codepath (chat); workspace-rendered playbooks (like `summarize-document-for-workspace@v1`) require manual JSON editing of the `sprk_routingconfigjson` column.

**Note**: Successor `chat-routing-redesign-r1` WP3 introduced FR-23 (`NodeRoutingConfig` non-optional structural wiring into `DispatchResult`). Verify whether successor's structural changes obviate this UI work before scheduling.

**Entry-points**

- `src/client/code-pages/PlaybookBuilder/src/components/properties/NodePropertiesForm.tsx` (add destination dropdown + conditional widgetType input)
- `src/server/api/Sprk.Bff.Api/Models/Ai/NodeRoutingConfig.cs` (the C# contract)
- `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/PlaybookOutputHandler.cs:149-188` (NodeDestination switch — consumer)
- `projects/spaarke-ai-platform-unification-r6/tasks/093-builder-ui-destination-widgettype-fields.poml` (full task spec)

**Suggested fix** (if known)

Add destination dropdown (enum 4 values) to NodePropertiesForm. When destination=workspace, conditionally show widgetType text input. Persist as `NodeRoutingConfig` JSON to `sprk_routingconfigjson` field. Validation: warn if widgetType set but destination != workspace.

**Estimated effort**: ~1 day
**Blockers**: verify successor `chat-routing-redesign-r1` WP3 FR-23 doesn't obviate this UI need
**Related**: tasks 030-031 (R6 Pillar 5 backend), `notes/r6-deliverables-audit.md` Pillar 5

---

### ISS-002 — Chat ↔ Workspace write-side unification (handler vs playbook output divergence)

| Field | Value |
|---|---|
| **Status** | Open |
| **Urgency** | next-round |
| **Filed** | 2026-06-26 |
| **Source** | TIER-C diagnostic + architectural review 2026-06-25; handoff doc filed in successor wt |
| **GitHub Issue** | [#475](https://github.com/spaarke-dev/spaarke/issues/475) |

**Description**

The SpaarkeAi shell has **two parallel code paths that mutate workspace tabs**, sharing the same `IWorkspaceStateService` backing store but diverging in how they notify the frontend. Chat-handler path (`SendWorkspaceArtifactHandler`, `UpdateWorkspaceTabHandler`, `CloseWorkspaceTabHandler`, `GetWorkspaceTabContentHandler`) writes state and returns — no SSE push. Playbook-output path (`PlaybookOutputHandler.HandleWorkspaceOutputAsync` line 581+) writes state AND emits `workspace.tab_open` SSE event so the frontend mounts widgets immediately.

The chat-handler comments cite ADR-030 as justification for not emitting SSE. After re-reading ADR-030 verbatim, this citation is **misleading** — ADR-030 forbids adding a 6th channel but explicitly allows additive event types on existing channels. The architectural divergence is implementation-driven, not constraint-driven.

Concrete failure mode: user sees inconsistent UX. When the LLM says "I've added that to your workspace" via chat tool → user waits for refetch tick (~500ms-2s) to see the tab appear. When a playbook completes with `destination=workspace` → tab appears immediately. Same destination, two latency profiles, no user-visible reason for the difference.

This is **outside R6's responsibility** — successor `chat-routing-redesign-r1` has the architectural context (Phase 5R task 118b added `GetWorkspaceTabContentHandler` — read-side unification precedent). Full handoff document already filed in successor wt: `c:\code_files\spaarke-wt-spaarke-ai-platform-chat-routing-redesign-r1\projects\spaarke-ai-platform-chat-routing-redesign-r1\notes\chat-workspace-write-side-unification-r6-handoff.md`.

**Entry-points**

- `src/server/api/Sprk.Bff.Api/Services/Ai/Handlers/SendWorkspaceArtifactHandler.cs`
- `src/server/api/Sprk.Bff.Api/Services/Ai/Handlers/UpdateWorkspaceTabHandler.cs:91` (the misleading ADR-030 comment)
- `src/server/api/Sprk.Bff.Api/Services/Ai/Handlers/CloseWorkspaceTabHandler.cs`
- `src/server/api/Sprk.Bff.Api/Services/Ai/Handlers/GetWorkspaceTabContentHandler.cs`
- `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/PlaybookOutputHandler.cs:581+` (sibling — emits SSE)
- `.claude/adr/ADR-030-pane-event-bus.md` (actual constraint — re-read)

**Suggested fix** (if known)

Per the handoff doc — shared `IWorkspaceMutationPipeline` service: validate → write state → emit `workspace.<event>` on PaneEventBus (additive ADR-030 events) → return. Both chat handlers and `PlaybookOutputHandler.HandleWorkspaceOutputAsync` route through the pipeline. Chat handlers gain SSE writer delegate via ADR-033 pattern on `ChatInvocationContext`. Frontend subscribers unchanged.

**Estimated effort**: ~1-2 days (per successor handoff doc)
**Blockers**: chat-routing-redesign-r1 Phase 7 wrap-up timing
**Related**: ADR-030 (PaneEventBus 4-channel — additive events allowed), ADR-033 (streaming side-channel), successor handoff doc

---

### DEF-004 — Verify + remove vestigial `sprk_capabilities` field on `sprk_analysisplaybook` (if confirmed)

| Field | Value |
|---|---|
| **Status** | Open |
| **Urgency** | someday |
| **Filed** | 2026-06-26 |
| **Source** | `r6-deliverables-audit.md` Pillar 2 open question; task 092 POML stub |
| **GitHub Issue** | [#476](https://github.com/spaarke-dev/spaarke/issues/476) |

**Description**

The R6 deliverables audit (Pillar 2 section) flagged a possible vestigial Dataverse field. The `sprk_capabilities` (single-line text, single-value) field may exist on `sprk_analysisplaybook` alongside `sprk_playbookcapabilities` (multi-select choice). Code paths use `sprk_capabilities` on `sprk_scope` entities (intentional, in production), NOT on the playbook itself. If both appear on the playbook form in make.powerapps.com, the single-select on the playbook is likely R5-vestigial and confuses authoring intent.

Concrete failure mode (if confirmed): a maker authoring a playbook sees TWO capability-like fields on the form (`sprk_capabilities` text + `sprk_playbookcapabilities` multi-select); chooses incorrectly; their choice is silently ignored by the routing pipeline (which only reads `sprk_playbookcapabilities`). Verification + cleanup eliminates the ambiguity.

**Entry-points**

- make.powerapps.com → `sprk_analysisplaybook` table → Columns
- `grep -rn "sprk_capabilities" src/server/api/Sprk.Bff.Api/Services/Ai/` to confirm code references only the scope-side field
- `projects/spaarke-ai-platform-unification-r6/tasks/092-verify-remove-vestigial-sprk-capabilities.poml`

**Suggested fix** (if known)

Per task 092: open maker portal, verify presence of both fields. If `sprk_capabilities` exists on `sprk_analysisplaybook` separately from `sprk_playbookcapabilities`, confirm no production code references the playbook-side field, then delete via Dataverse maker portal. Document outcome.

**Estimated effort**: ~1h
**Blockers**: maker portal access
**Related**: `r6-deliverables-audit.md` Pillar 2 open question, task 092 POML

---

## In Progress

*None.*

---

## Done

*None.*

---

## Won't Fix

*None.*

---

## Superseded

*None.*
