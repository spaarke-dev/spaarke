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

### DEF-004 — Verify + remove vestigial `sprk_capabilities` field on `sprk_analysisplaybook` (CONFIRMED vestigial 2026-06-28)

| Field | Value |
|---|---|
| **Status** | In Progress — verification done, schema-delete awaiting user confirmation |
| **Urgency** | next-round (upgraded from someday — verification surfaced a silent-breakage in production) |
| **Filed** | 2026-06-26 |
| **Verified** | 2026-06-28 (R6 closeout sprint) |
| **Source** | `r6-deliverables-audit.md` Pillar 2 open question; task 092 POML stub |
| **GitHub Issue** | [#476](https://github.com/spaarke-dev/spaarke/issues/476) |

**Verification findings (2026-06-28)**

| Finding | Detail |
|---|---|
| Schema reality | `sprk_analysisplaybook.sprk_capabilities` is a CHOICE (single-select, NOT text as guessed), 7 options Title Case (`Search`/`Analyze`/etc). `sprk_playbookcapabilities` is a CHOICE with the same 7 options, lowercase labels (`search`/`analyze`/etc). Both single-select per schema describe (Dataverse may display multi-select UX via global option-set; production code parses `[N, M, -1]` array format from `sprk_playbookcapabilities`). |
| Code references on `sprk_analysisplaybook` | **ZERO production references** to `sprk_capabilities` on the playbook table. All 15+ prod-code refs use `sprk_playbookcapabilities` (`PlaybookService.cs:182, 204, 393, 503, 668, 763, 1205, 1209`; `AnalysisChatContextResolver.cs:51, 54, 313, 479, 489`; `SprkChatAgentFactory.cs:1291, 2148`; `PlaybookCapabilities.cs`; `AnalysisChatContextResponse.cs`; `DefaultPlaybookConstants.cs:24, 83`; `HandlerRegistrationConventions.md:79`). The 4 `sprk_capabilities` refs (`DynamicCommandResolver.cs:24, 79, 353, 369, 386, 488, 496, 501`; `AnalysisChatContextResolver.cs:582`; `CommandEntry.cs:9`) all target the `sprk_scope` table multi-select, NOT the playbook. |
| Live data on vestigial field | **3 of 20 playbooks** in spaarke-dev populate `sprk_capabilities`: (a) `Daily Briefing Narrate` BRIEF-NRRT → Summarize (100000006); (b) `summarize-document-for-chat@v1` → Search (100000000); (c) `Create New Project Pre-Fill` → Summarize (100000006). |
| Maker confusion | 2 of those 3 ALSO populate `sprk_playbookcapabilities` (b: `[-1,100000000,100000001,100000002,-1]`; c: `[-1,100000006,-1]`) — proving makers are uncertain which field is canonical. |
| Silent breakage | `Daily Briefing Narrate` writes ONLY to vestigial field (`sprk_playbookcapabilities` is `null`) → its "Summarize" capability is INVISIBLE to production code paths (`PlaybookService` SELECT clauses + `AnalysisChatContextResolver` queries + `SprkChatAgentFactory.ExtractCapabilities` all read the live field only). The "Daily Briefing Narrate" playbook therefore has no chat-capability surface effect from this attribute. |

**Separate bug surfaced** — `projects/spaarke-daily-update-service/notes/playbooks/daily-briefing-narrate.json` lines 16 and 28 write `"sprk_capabilities": 100000006` instead of `"sprk_playbookcapabilities": 100000006`. That project's `Deploy-Playbook.ps1` perpetuates the silent-breakage on each deploy. **Filed as separate GitHub issue (see ISS-003 below)** — they own the JSON fix.

**Recommended remediation sequence** (awaiting user confirmation for the schema-delete step):

1. Wait for `spaarke-daily-update-service` to fix `daily-briefing-narrate.json` (their issue) — otherwise their next deploy fails after we delete the column.
2. Migrate the 3 affected playbook rows in spaarke-dev:
   - Daily Briefing Narrate (7b5a6ed3-0271-...): write `sprk_playbookcapabilities` = `[-1,100000006,-1]` (Summarize). Then clear `sprk_capabilities`.
   - summarize-document-for-chat@v1 (44285d15-1360-...): `sprk_playbookcapabilities` is already `[-1,100000000,100000001,100000002,-1]`. Just clear `sprk_capabilities`.
   - Create New Project Pre-Fill (fc343e9c-3460-...): `sprk_playbookcapabilities` is already `[-1,100000006,-1]`. Just clear `sprk_capabilities`.
3. **User action (maker portal)** — delete `sprk_capabilities` column from `sprk_analysisplaybook` table:
   - make.powerapps.com → Tables → `sprk_analysisplaybook` → Columns → `sprk_capabilities` → ... → Delete column
   - Power Apps will prompt about dependent views/forms. Confirm.
   - Publish all customizations.
4. Verify in spaarke-prod (if R6 has been promoted there): same field state should be checked + cleaned before deletion.
5. Close GitHub #476 with PR/audit link.

**Entry-points**

- `infra/dataverse/sprk_analysistool*.json` (NO references — verified clean)
- Schema delete: make.powerapps.com → `sprk_analysisplaybook` table → Columns
- Sister project bug: `projects/spaarke-daily-update-service/notes/playbooks/daily-briefing-narrate.json:16,28`

**Estimated effort**: ~30 min user maker-portal task (after sister project fixes their JSON)
**Blockers**: (a) sister project's daily-briefing-narrate.json fix; (b) user maker-portal access for schema delete
**Related**: ISS-003 (sister project JSON bug), audit references in `r6-deliverables-audit.md` Pillar 2

---

### ISS-003 — `spaarke-daily-update-service` `daily-briefing-narrate.json` writes to vestigial `sprk_capabilities` field

| Field | Value |
|---|---|
| **Status** | Open |
| **Urgency** | next-round |
| **Filed** | 2026-06-28 (DEF-004 verification surfaced this) |
| **Source** | DEF-004 verification — confirmed Daily Briefing Narrate playbook's "Summarize" capability is invisible to production code because deploy script writes to vestigial field |
| **GitHub Issue** | [#510](https://github.com/spaarke-dev/spaarke/issues/510) |

**Description**

`projects/spaarke-daily-update-service/notes/playbooks/daily-briefing-narrate.json` lines 16 and 28 write `"sprk_capabilities": 100000006` (Title Case "Summarize"). The production-code path that reads playbook capabilities (`PlaybookService.cs`, `AnalysisChatContextResolver.cs`, `SprkChatAgentFactory.ExtractCapabilities`) only reads `sprk_playbookcapabilities` (lowercase). The deployed Daily Briefing Narrate playbook (row `7b5a6ed3-0271-f111-ab0e-000d3a13a4cd` in spaarke-dev) therefore has `sprk_playbookcapabilities = null` and its "Summarize" capability is invisible — the playbook does not appear in any chat capability inline action chip or capability-routed dispatch.

Concrete failure mode: user opens chat → CapabilityRouter / inline-action chips do NOT surface a "Daily Briefing Narrate" option for matching intents, even though the playbook is Active in Dataverse. The grounded-narration playbook is effectively dormant for capability-routed UX.

**Entry-points**

- `projects/spaarke-daily-update-service/notes/playbooks/daily-briefing-narrate.json:16` (lookupKey `_dataverseRow.fields.sprk_capabilities`)
- `projects/spaarke-daily-update-service/notes/playbooks/daily-briefing-narrate.json:28` (top-level `playbook.sprk_capabilities`)
- `projects/spaarke-daily-update-service/` → `Deploy-Playbook.ps1` (deploys the JSON)
- Dataverse row to remediate: `sprk_analysisplaybookid = 7b5a6ed3-0271-f111-ab0e-000d3a13a4cd`

**Suggested fix** (if known)

Rename the field in `daily-briefing-narrate.json` from `sprk_capabilities` to `sprk_playbookcapabilities`. Note: the value format may need adjusting — the live field stores `[-1,100000006,-1]` array format for multi-select; verify upsert path handles single-int vs array.

**Estimated effort**: ~1h (their project's deploy lane)
**Blockers**: their project's deploy cycle
**Related**: DEF-004 (this R6 entry), `spaarke-daily-update-service` project owners

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

> Pulled back into R6 scope 2026-06-28 — user confirmed "expected to be done". Status mirrored on GitHub issues via `wip` label.

- **DEF-001** (#471) — BFF `context_event` SSE emission. Code complete (`229f30ef9`, pushed); awaiting Azure BFF redeploy + UAT.
- **DEF-002** (#473) — Persona resolution. **2026-06-29 update**: user added `sprk_playbookpersona` lookup column to `sprk_analysisplaybook` + added to main form. BFF wiring code complete (commit pending): `AnalysisPersonaService.GetEffectivePersonaAsync` now fetches the playbook's `_sprk_playbookpersona_value` and resolves the persona by PK (the prior code stubbed this layer — it filtered only on `scopetype=PlaybookAttached`, returning the FIRST such row regardless of which playbook was bound). PlaybookBuilder dropdown UI skipped — main form already exposes the field; an in-Builder dropdown can ship as a future enhancement.
- **DEF-003** (#474) — Builder UI destination + widgetType. Code complete (`c98fe7f85`, pushed); awaiting maker portal upload + UAT.

---

---

## Done

### DEF-004 — Vestigial `sprk_capabilities` field removed from `sprk_analysisplaybook` (closed 2026-06-29)

| Field | Value |
|---|---|
| **Closed** | 2026-06-29 |
| **GitHub Issue** | [#476](https://github.com/spaarke-dev/spaarke/issues/476) (closed) |

User removed the column via maker portal after R6 closeout verification confirmed: (a) zero production-code references to `sprk_analysisplaybook.sprk_capabilities`; (b) production code reads `sprk_playbookcapabilities` exclusively for chat-capability surface; (c) the 3 affected playbook rows are either dual-populated (live field is the source of truth) or were already silently broken (Daily Briefing Narrate's "Summarize" capability was invisible to the LLM regardless). Sister project bug ([ISS-003 / #510](https://github.com/spaarke-dev/spaarke/issues/510)) urgency bumped to `now` — their next `Deploy-Playbook.ps1` will fail until `daily-briefing-narrate.json` is updated.

---

---

## Won't Fix

*None.*

---

## Superseded

*None.*
