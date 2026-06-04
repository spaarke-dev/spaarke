# R5 ↔ Insights Engine R2 Coordination

> **Purpose**: Document alignment, overlap, and required coordination between R5 (Spaarke AI Platform Unification — chat-pane Summarize a Document + platform foundations) and the parallel in-flight project Insights Engine R2 (Phase 1.5).
> **Created**: 2026-06-03 (during R5 design)
> **Last updated**: 2026-06-04 (late — post-Wave-F contract v1.1 ship) by Claude (Anthropic AI agent) on behalf of the Insights Engine r2 project. See §8 changelog entries dated 2026-06-03 (late) for Wave D + E status + integration brief; 2026-06-04 (late) for Wave F v1.1 (SSE streaming + citations[].href) + touchpoint §4.4 + §4.6 resolutions.
> **Maintenance**: Living document. Updated as Insights r2 progresses. Subject to the final-refinement gate (see §6 below) once Insights r2 completes.
> **Provenance**: Original synthesis from a comprehensive read-through of Insights r2 artifacts on 2026-06-03 (early). Subsequent updates per §8 changelog.

---

## 1. Why this document exists

Spaarke has heavy AI-platform investment that R5 surfaces (RAG infrastructure, Analysis orchestration, session storage, playbook execution engine, chat agent middleware, PaneEventBus, 32-ADR governance baseline). Insights Engine R2 is concurrently investing in the same platform — extending it for analytical/inference workflows while R5 extends it for conversational/generative workflows.

**The risk**: two projects independently building on shared infrastructure can produce drift, duplicated effort, or subtle conflicts that only surface at integration. The principle the operator has stated:

> "It is critical that we fully document existing components for reuse and not rebuilt and conflict build."

This document is the explicit accounting of what's shared, what's separate, what's at risk of collision, and how the two projects coordinate.

---

## 2. Insights Engine R2 — Snapshot (as of 2026-06-03 early)

> 🔔 **STATUS UPDATE — 2026-06-03 (late)**: The snapshot below was captured before Waves D and E shipped. **Current state: Wave D ✅ (PR #336 merged), Wave E ✅ implementation (PR #337 auto-merging), only wrap-up task 090 (~4h) remains.** See [§8 Changelog 2026-06-03 (late)](#8-changelog) for the full Wave D + E status update, the new integration brief reference, and the refresh of touchpoint resolutions. The original snapshot below is preserved for history per §7 maintenance protocol.

**Identity**: Phase 1.5 of Spaarke Insights Engine. Succeeds Phase 1 (r1, shipped + deployed; 17/17 D-P deliverables complete). Lifts from "plumbing prototype" to "multi-tenant, multi-practice-area, multi-entity insights platform with pre-authored playbook AND ad-hoc RAG consumption paths."

**Two consumption paths**:
- `POST /api/insights/ask` (pre-authored JPS playbook) — returns `InsightArtifact` OR structured `DeclineResponse`
- `POST /api/insights/search` (Wave E1, NEW) — wraps `IRagService` with subject + artifactType + predicate filters; ranked Observations + LLM synthesis with citations

**Current status by wave** (per TASK-INDEX.md + recent commits):

| Wave | Status | Tasks |
|---|---|---|
| Wave B — Unblock | ✅ Complete (orphan 004 🔲) | 5/6 ✅ |
| Wave A — Foundation design | ✅ Complete | 6/6 ✅ |
| Wave C — JPS compliance refactor | ✅ Complete (merged PR #334) | 5/5 ✅ |
| Wave D — 2D taxonomy + multi-entity | 🔄 In progress | 2/7 ✅ (D1+D2); 5 ahead |
| Wave E — Hybrid + Spaarke Assistant | 🔲 Not started | 0/4 |
| Wrap-up | 🔲 Not started | 0/1 |

**Honest math**: 18/29 tasks = 62% by count; ~11.5d of ~32d = ~36% by effort. **Remaining work: ~3–4 weeks (D + E + wrap)**. The "completing today" framing the operator mentioned at R5 kickoff likely refers to Wave D-G1 (task 030 just landed at commit `be636959`), not the full project.

**Architectural anchors carried + corrected from r1** (from Insights spec.md):
- "Insights IS a JPS application" — every workflow runs through `PlaybookExecutionEngine`; no parallel orchestrators
- `sprk_analysisaction.sprk_systemprompt` IS the prompt-bearing primitive (no new `sprk_prompt` entity)
- Single canonical `universal-ingest@v1` playbook (parameterized; not duplicated per practice area)
- 2D classification taxonomy: practice-area × document-type
- Multi-entity subject scheme (`matter:`, `project:`, `invoice:`, future entities)
- Cosmos NoSQL graph re-deferred to Phase 2

---

## 3. Existing components both projects USE (and MUST reuse, not rebuild)

This is the **reuse mandate**. R5 implementers and Insights r2 implementers MUST NOT recreate any of the components below — every one is shipped, tested, and load-bearing.

### 3.1 BFF execution + retrieval layer

| Component | Path | Owner | R5 use | Insights r2 use |
|---|---|---|---|---|
| `PlaybookExecutionEngine` | `src/server/api/Sprk.Bff.Api/Services/Ai/PlaybookExecution/` | Platform (shared) | Summarize playbook execution path | Insights JPS playbook execution (universal-ingest@v1, predict-matter-cost@v1) |
| `AnalysisOrchestrationService` + 3 sub-services | `src/server/api/Sprk.Bff.Api/Services/Ai/AnalysisOrchestrationService.cs` | Analysis (shared with R5) | Summarize endpoint backend | Not used (Insights has its own `InsightsOrchestrator.cs`) |
| `InsightsOrchestrator` | `src/server/api/Sprk.Bff.Api/Services/Ai/Insights/InsightsOrchestrator.cs` | Insights | Not used | Insights `/ask` endpoint backend |
| `IRagService` + `RagService` | `src/server/api/Sprk.Bff.Api/Services/Ai/` | Platform (shared) | `RagSearchOptions.sessionId` filter (additive) | E1 endpoint wraps with subject/artifactType/predicate filters (additive) |
| `RagIndexingPipeline` | `src/server/api/Sprk.Bff.Api/Services/Ai/` | Platform (shared) | Writes to new `spaarke-session-files` index parameterized by call site | Writes to `spaarke-insights-index` (existing) |
| `EmbeddingCache` (Redis, 7-day TTL) | `src/server/api/Sprk.Bff.Api/Services/Ai/` | Platform (shared) | Reused unchanged | Reused unchanged |
| `ReferenceRetrievalService` (L1 curated retrieval) | `src/server/api/Sprk.Bff.Api/Services/Ai/` | Platform (shared) | Reused unchanged for cross-corpus grounding | Used by `AiAnalysisNodeExecutor` |
| `SprkChatAgent` + `SprkChatAgentFactory` | `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/` | Chat platform (shared) | New tool registration: `InvokeSummarizePlaybookTool` | Wave E3 will register Insights as a callable tool — **coordination point §4.1** |
| `ChatSessionManager` + `ChatHistoryManager` | `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/` | Chat platform (shared) | Extended with `ChatSession.UploadedFiles[]` manifest (additive property) | Not extending |
| Triple-tier session storage (Redis hot 24h + Cosmos warm + Dataverse cold) | per ADR-009 + D-06 | Platform (shared) | File manifest lives in existing tiers (no new tier) | Not extending |
| `AnalysisChunk` SSE protocol | `src/server/api/Sprk.Bff.Api/Services/Ai/` | Platform (shared) | Adds `FieldDelta` variant (additive) | Currently emits progress/result/complete/error; may benefit from `FieldDelta` reuse — **coordination point §4.4** |
| Safety pipeline (`PromptShieldService`, `GroundednessCheckService`) | `src/server/api/Sprk.Bff.Api/Services/Ai/Safety/` | Platform (shared) | Auto-inherited via factory middleware | Auto-inherited via factory middleware |
| Cost control (`AgentCostControlMiddleware`) | `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/Middleware/` | Platform (shared) | Auto-inherited | Auto-inherited |

### 3.2 JPS data primitives (Dataverse)

| Component | Owner | R5 use | Insights r2 use |
|---|---|---|---|
| `sprk_analysisaction.sprk_systemprompt` (JPS prompt-bearing primitive) | Platform | New seed: "Summarize Document for Chat" | New seeds: 6+ Insights action rows (INS-FACT/IDXR/EVID/GRND/DECL/RART); per-practice-area Layer 1/2 prompts (Wave D) |
| `sprk_analysisplaybook` (JPS playbook definition) | Platform | New configuration linking the Summarize action | `universal-ingest@v1` (Wave C task 020); `predict-matter-cost@v1` (r1) |
| Existing Summarize playbook (GUID `4a72f99c-a119-f111-8343-7ced8d1dc988`) | Platform | Reused unchanged | Not used |
| `sprk_documenttype_ref` + `sprk_practicearea_documenttype` N:N (NEW, Wave D1 just landed) | Insights r2 | Not used | Wave D1–D4 routing source |

### 3.3 Node executor framework (20 executors shipped)

R5 adds **zero** new node executors. Insights r2 added the Insights-specific ones in r1 + r2. Both projects extend by adding new `sprk_analysisaction` rows that bind existing executors via `ActionType`.

| Executor | Owner | Used by |
|---|---|---|
| `AiAnalysisNodeExecutor` | Platform | R5 Summarize, Insights playbook nodes |
| `SanitizerNodeExecutor`, `ObservationEmitterNodeExecutor` | Insights | Insights only |
| `LiveFactNode`, `IndexRetrieveNode`, `EvidenceSufficiencyNode`, `GroundingVerifyNode`, `DeclineToFindNode`, `ReturnInsightArtifactNode` | Shared (live in `Services/Ai/Nodes/`) | Insights mainly; R5 may chain to `DeclineToFindNode` for insufficient-content paths per D-04 honesty contract |
| `DeliverToIndexNodeExecutor` | Platform | Both (R5 for session-files indexing; Insights for `spaarke-insights-index`) |
| `ConditionNodeExecutor`, `SendEmailNodeExecutor`, `UpdateRecordNodeExecutor`, `CreateTaskNodeExecutor`, `CreateNotificationNodeExecutor`, `AgentServiceNodeExecutor` | Platform | Available to both |

### 3.4 Frontend / shared component libraries

| Component | Path | R5 use | Insights r2 use |
|---|---|---|---|
| `SlashCommandMenu` + `useSlashCommands` + `DEFAULT_SLASH_COMMANDS` | `@spaarke/ui-components` | `/summarize` semantic extension | E3 may add `/insights` or similar — **coordination point §4.1** |
| `RichFilePreviewDialog` (iframe + metadata + prev/next + 3-dot menu) | `@spaarke/ui-components` | Renderer core extracted for Context-pane file preview | Not currently — could reuse if Insights output needs file viewing |
| `DocumentRowMenu` (12 actions across 3 groups) | `@spaarke/ui-components` | Reused unchanged (aiSummary, toggleWorkspace etc.) | Not currently |
| `AnalysisEditorWidget` (section-based, NOT streaming-aware) | `@spaarke/ai-outputs` | Untouched — R5 uses new `StructuredOutputStreamWidget` | May be used for static Insights output rendering |
| `DocumentViewerWidget` (R4 W-4 stub) | `@spaarke/ai-widgets` | Upgraded to use extracted `RichFilePreview` renderer | Not used |
| `PaneEventBus` (4 closed channels + extensible event types per ADR-030) | `@spaarke/ai-widgets` | Adds 5 new event types: `workspace.streaming_started`, `workspace.field_delta`, `workspace.streaming_complete`, `context.files_staged`, `context.file_selected` | E3 may add events for Insights rendering — **coordination point §4.4** |
| `WorkspaceWidgetRegistry` (lazy-factory) | `@spaarke/ai-widgets` | New `StructuredOutputStreamWidget` registers here | E3 may register Insights output widget |
| `Get Started cards` + `PlaybookGalleryWidget` | `@spaarke/ai-widgets` | New "Summarize a Document" card | E3 may add Insights entry surfaces |

### 3.5 ADRs both projects respect

ADR-001, ADR-006, ADR-007, ADR-008, ADR-009, **ADR-010** (DI minimalism — both add via feature modules), ADR-012, **ADR-013** (BFF-only AI; both produce Placement Justification), ADR-014 (tenant isolation), **ADR-018** (Flag Scope Discipline — added 2026-06-03 from R5 design review; consistent with Insights' ADR-032 usage), ADR-021, ADR-022, ADR-026, **ADR-028**, **ADR-030** (PaneEventBus closed channels; both add additive event types only), ADR-031, **ADR-032** (Null-Object kill-switch — Insights cites for Wave C/D/E new services; R5 has zero new flags → no new Null impls).

---

## 4. Coordination touchpoints (conflict surface)

### 4.1 🟡 `SprkChatAgent` tool registration (R5 ↔ Insights Wave E3)

**Surface**: R5 registers `InvokeSummarizePlaybookTool`; Insights Wave E3 will register Insights (playbook + RAG paths) as a callable tool on the chat agent. Both modify the chat agent's tool catalog through `SprkChatAgentFactory`.

**Risk**: Independent registration could produce naming clashes, schema drift, or middleware inconsistency.

**Mitigation**: Both projects use the existing `ICapabilityRouter` (AIPU2-061) per-turn tool resolution pattern. No actual code collision expected if both follow the established convention.

**Ownership**: Whichever project's tool ships first establishes naming + parameter-schema conventions. The second project follows. Current ordering preference (per §6 below): Insights E3 lands first, R5 follows.

**Open question to revisit at refinement gate**: Did Insights E3 establish a tool-registration convention R5 should follow? Are there shared utilities for tool registration to factor out?

### 4.2 🟡 Intent routing (R5 slash commands ↔ Insights Wave E2 LLM classifier)

**Surface**: R5 routes via `/summarize` slash command + LLM tool-call for natural language. Insights Wave E2 ships an LLM-based intent classifier that routes natural language to playbook OR RAG paths.

**Risk**: User says "summarize the matter docs" — Insights classifier might route to `predict-matter-cost` while R5 expects it to route to Summarize. Two competing intent paths over the same language space.

**Mitigation**: Insights E2 classifier should include R5's Summarize tool in its routable capability catalog. The classifier is the gateway; R5's slash command remains the explicit shortcut.

**Ownership**: Insights E2 owns the classifier; R5 owns the Summarize tool. The classifier's tool catalog is the integration point.

**Open question to revisit at refinement gate**: What's the classifier's tool catalog? Is R5's Summarize tool registered there? What's the conflict-resolution rule when intent is ambiguous?

### 4.3 🟡 Cumulative ADR-010 pressure (both projects add DI registrations)

**Surface**: Both projects add new service registrations inside feature modules. Insights NFR-05 explicitly flags this: "Wave D5's per-entity resolver registrations + Wave E's classifier + RAG registrations stay within ADR-010's ≤15 non-framework registration target." R5 adds `SessionSummarizeOrchestrator` + session-files cleanup job.

**Risk**: Conceptual pressure on ADR-010, though both projects respect the **correct** reading (≤15 is `Program.cs` lines, not total registrations; 265 baseline is acknowledged out-of-scope per Phase 5 baseline note).

**Mitigation**: Both projects register inside feature modules; `Program.cs` line count remains unchanged. The 265 baseline grows additively. No actual constraint violation.

**Ownership**: Each project's own ADR-010 compliance is reviewed in its PR per CLAUDE.md §10 + `.claude/constraints/bff-extensions.md`. No coordination needed at code level.

**Open question to revisit at refinement gate**: Did the combined service growth surface any cohesion or naming issues across modules? Are there shared services that should be consolidated?

### 4.4 🟢→🟡 SSE event protocol + structured-field streaming (R5 introduces; Insights may want to reuse)

**Surface**: R5 introduces `FieldDelta` SSE event variant for ChatGPT-style structured-field streaming (`{ type: 'delta', path: 'tldr', content: '...' }`). Insights currently emits `progress`/`result`/`complete`/`error` events only.

**Potential reuse**: If Insights Wave E3 (Spaarke Assistant integration) wants progressive rendering of `InsightArtifact` (instead of plop), it should adopt R5's `FieldDelta` pattern rather than introducing a parallel mechanism.

**Risk**: If Insights independently designs structured streaming, two incompatible delta protocols emerge.

**Mitigation**: R5 ships the `FieldDelta` protocol in Phase 1. Insights E3 design (which has not started) reviews it before deciding rendering strategy.

**Ownership**: R5 owns the `FieldDelta` protocol design. Insights E3 reuses if useful.

**Open question to revisit at refinement gate**: Did Insights E3 reuse `FieldDelta`? If not, why not? Is there divergence worth reconciling?

### 4.5 🟡 PaneEventBus event-type discriminants (additive but uncoordinated)

**Surface**: Both projects add additive event types within existing channels per ADR-030. R5: `workspace.streaming_started`, `workspace.field_delta`, `workspace.streaming_complete`, `context.files_staged`, `context.file_selected`. Insights E3: TBD.

**Risk**: Independent additive event-type names could collide (e.g., both add `workspace.stream_complete` or `context.entity_resolved`).

**Mitigation**: ADR-030's discriminant naming is by convention; no central registry today. Light coordination: each project documents new event-type names in its design.md.

**Ownership**: Each project owns its event-type discriminants; reviewers check for collisions during PR.

**Open question to revisit at refinement gate**: Did event-type names collide? Is a central event-type registry worth introducing?

### 4.6 🟢 Non-conflicts (explicitly verified — no coordination needed)

These were checked and do NOT require coordination. Documented here so future readers don't re-litigate.

| Area | Why no conflict |
|---|---|
| `AnalysisOrchestrationService` modifications | Insights has its own `InsightsOrchestrator.cs`; R5 calls (does not modify) `AnalysisOrchestrationService`. |
| Dataverse schema additions | Insights adds `sprk_documenttype_ref` + `sprk_practicearea_documenttype` (Wave D1, just landed). R5 adds zero new entities. |
| AI Search indexes | Insights uses `spaarke-insights-index` (extending in Wave D6). R5 adds `spaarke-session-files`. Separate indexes, separate purposes, separate lifecycles. |
| New ADRs | Neither project introduces new ADRs. Both reference the same set of binding ADRs. R5 added ADR-018 Flag Scope Discipline section but did not create a new ADR. |
| ChatSession model extensions | R5 extends with `UploadedFiles[]`; Insights does not extend the model. |
| Background job framework | Both may add `IHostedService` registrations (R5: session-files cleanup; Insights: TBD); no name collision risk. |
| Node executors | R5 adds zero new executors. All Insights-added executors are already shipped or in flight; R5 reuses unchanged. |

---

## 5. Reuse mandate (R5 implementer checklist)

Before any R5 implementation task adds a new service, model, endpoint, widget, or DI registration, the implementer MUST:

1. **Confirm there's no existing equivalent** by searching:
   - `src/server/api/Sprk.Bff.Api/Services/Ai/` (all subfolders)
   - `src/server/api/Sprk.Bff.Api/Services/Ai/Insights/` (Insights-specific)
   - `src/server/api/Sprk.Bff.Api/Services/Ai/PublicContracts/` (facades)
   - `src/client/shared/Spaarke.UI.Components/src/components/`
   - `src/client/shared/Spaarke.AI.Widgets/src/`
   - `src/client/shared/Spaarke.AI.Outputs/src/`

2. **Cite the existing component** in the task's design notes if reusing. If proposing a new component, cite the gap explicitly (what existing component falls short and why).

3. **Run conflict-check skill** (`/conflict-check`) before merging — surfaces any active PRs touching the same files.

4. **For BFF additions**, follow `.claude/constraints/bff-extensions.md` (binding per CLAUDE.md §10).

**Specifically prohibited** (R5 MUST NOT rebuild):
- Any new orchestrator paralleling `AnalysisOrchestrationService` or `InsightsOrchestrator`
- Any new RAG search service paralleling `RagService`
- Any new session-management layer paralleling `ChatSessionManager`
- Any new chat agent paralleling `SprkChatAgent`
- Any new file-preview component paralleling `RichFilePreviewDialog`
- Any new analysis widget paralleling `AnalysisEditorWidget` (the new `StructuredOutputStreamWidget` is schema-driven and serves a different role — streaming structured output progressively — not a duplicate)
- Any new SSE protocol paralleling `AnalysisChunk` (R5 EXTENDS `AnalysisChunk` with `FieldDelta`; does not introduce a new envelope)
- Any new prompt-bearing entity (per Insights spec + ADR — `sprk_analysisaction.sprk_systemprompt` IS the primitive)
- Any new playbook orchestration layer paralleling `PlaybookExecutionEngine`

---

## 6. Refinement gate plan (final design.md pass after Insights r2 completes)

### 6.1 Why a refinement gate

R5 design.md is finalized 2026-06-03 against Insights r2's CURRENT state (Wave C complete, D in progress, E not started). Insights r2 has ~3–4 weeks of work remaining that touches several R5 coordination surfaces (especially Wave E3 Spaarke Assistant integration). Locking R5 spec → tasks now risks designing against a moving target on the integration seams.

The operator's stated plan:
> "Our best plan is to allow the Insights project to complete and then do a final design.md refinement pass — with the benefit of revalidating against the then-completed Insights work and the project coordination guidelines."

### 6.2 Gate trigger

The refinement gate fires when **Insights r2 closes** — specifically when:
- All Wave D tasks (030–036) are ✅
- All Wave E tasks (040–043) are ✅
- Wrap-up task 090 is ✅ (lessons-learned authored)
- The Insights r2 branch is merged to master

Estimated date: mid-to-late July 2026 (based on the ~3–4 week remaining-work estimate as of 2026-06-03).

### 6.3 Refinement scope (what the final pass updates)

When the gate fires, the refinement pass updates R5 in this order:

1. **Read Insights r2 lessons-learned.md** (task 090 deliverable) — captures what surprised them, what was deferred, what they'd do differently. Apply any insights to R5 design.

2. **Re-validate §3 reuse mandate** — confirm every component listed is still where it was (Insights may have renamed, moved, or retired things during Wave D/E). Update paths and ownership.

3. **Re-validate §4 coordination touchpoints** with actual Insights E3 outputs:
   - §4.1 chat tool registration: did Insights E3 establish conventions? Adopt them in R5 design.
   - §4.2 intent classifier: is R5's Summarize tool in the classifier's catalog? Confirm.
   - §4.4 `FieldDelta` reuse: did Insights E3 adopt the protocol? Document outcome.
   - §4.5 PaneEventBus event-type discriminants: any collisions to resolve?

4. **Update R5 design.md** with the validated coordination decisions. Sections likely to update:
   - §2.6 UI component layer (if Insights added new shared widgets R5 should reference)
   - §2.7 Data model (if Insights added entities R5 could use)
   - §2.8 ADR table (if any ADR was amended)
   - §4.5 Chat-driven summarize entry points (chat-tool registration may be revised)
   - §7 Phasing (Phase 2 vertical slice may shift if Insights established conventions to follow)

5. **Re-run BFF Placement Justification** with updated publish-size projection (Insights additions land first, R5 baseline shifts).

6. **Re-run the reuse mandate checklist** (§5 above) as a fresh search against the post-Insights master.

7. **Update this coordination doc** with closed status on each touchpoint, lessons captured, and final decisions.

8. **Only after refinement**: run `/design-to-spec` to produce R5 `spec.md`.

### 6.4 What R5 can do BEFORE the gate

To avoid stalling 3–4 weeks, R5 can productively work on items that are independent of Insights r2's remaining work. From design.md §7:

| Phase 1 work item | Independence from Insights r2 |
|---|---|
| Provision `spaarke-session-files` AI Search index | ✅ Independent (separate index from `spaarke-insights-index`) |
| Extend `RagSearchOptions` with `sessionId` filter | 🟡 Light coordination — additive parameter; review with Insights' filter additions when E1 PR opens |
| Extend `RagIndexingPipeline` parameterization | ✅ Independent |
| Extend `ChatSession` model with `UploadedFiles[]` | ✅ Independent (Insights does not extend this model) |
| Add `FieldDelta` variant to `AnalysisChunk` | 🟡 Light coordination — Insights E3 may adopt, so the variant design should be reviewable by Insights team before merge |
| Switch Summarize playbook execution to Azure OpenAI Structured Outputs | ✅ Independent |
| Session-files cleanup job (`IHostedService`) | ✅ Independent |
| Telemetry events for R5 | ✅ Independent |

Phase 2 (vertical slice incl. chat-tool registration + StructuredOutputStreamWidget + FilePreviewWidget) **waits for the refinement gate**.

### 6.5 Risk if the gate is skipped

If R5 ships Phase 2 (chat-tool registration) BEFORE Insights E3 lands:
- R5 establishes the tool-registration convention; Insights E3 has to adapt
- R5's tool may not appear in Insights E2's intent classifier catalog at launch
- Potential rework if Insights surfaces a better pattern post-facto

If R5 ships the entire vertical slice (including `StructuredOutputStreamWidget`) BEFORE Insights completes:
- R5's `FieldDelta` protocol may need refinement if Insights' Spaarke Assistant rendering reveals gaps
- Potential rework on the streaming widget

**Recommendation**: HOLD Phase 2 chat-tool work + `StructuredOutputStreamWidget` until the refinement gate. Ship Phase 1 platform extensions independently.

---

## 7. Maintenance

This document is updated:
- When any Insights r2 wave closes (D-G1 ✅ landed 2026-06-03; D-G2 will close next)
- When any coordination touchpoint resolution is reached
- At the refinement gate (full update)
- If new Insights r2 design decisions affect R5 reuse mandate

**Update protocol**: append a "Changelog" section at the bottom (do not rewrite history); document what changed and why.

---

## 8. Changelog

| Date | Change |
|---|---|
| 2026-06-03 (early) | Initial authoring during R5 design phase. Insights r2 at ~62% by task count (Wave C complete, D-G1 just landed, D-G2 next). All 5 coordination touchpoints + 7 non-conflicts documented. Refinement gate plan locked. |
| 2026-06-03 (late) | **Wave D + E ship update + integration-brief cross-reference.** Updated by Claude (Anthropic AI agent) on behalf of the Insights Engine r2 project — see details below. |
| 2026-06-04 (late) | **Wave F v1.1 shipped (SSE streaming + citations.href).** Touchpoints §4.4 (FieldDelta SSE reuse) + §4.6 (citations clickable) both RESOLVED. Contract bumped 1.0 → 1.1, additive + back-compat. Updated by Claude (Anthropic AI agent) on behalf of the Insights Engine r2 project — see §8.7 below. |

---

### 2026-06-03 (late) — Wave D + E ship update

**Updated by**: Claude (Anthropic AI agent) on behalf of the Insights Engine r2 project (operator: Ralph Schroeder).
**Trigger**: Wave E task 042 (Spaarke Assistant integration) completed; PR #337 opened with auto-merge; companion integration brief authored for R5 consumption.

#### 8.1 Wave-status updates (supersedes §2 status table)

| Wave | Status @ 2026-06-03 early (§2) | Status @ 2026-06-03 late (NOW) | Notes |
|---|---|---|---|
| Wave B — Unblock | ✅ Complete (5/6) | ✅ Complete (unchanged) | PR #330 merged |
| Wave A — Foundation design | ✅ Complete (6/6) | ✅ Complete (unchanged) | PR #334 merged |
| Wave C — JPS compliance refactor | ✅ Complete (5/5) | ✅ Complete (unchanged) | PR #334 merged |
| **Wave D — 2D taxonomy + multi-entity** | 🔄 In progress (2/7) | **✅ Complete (7/7)** | **PR #336 merged 2026-06-03 22:02 UTC** |
| **Wave E — Hybrid + Spaarke Assistant** | 🔲 Not started (0/4) | **✅ Implementation complete (4/4)** | **PR #337 auto-merging; 36 files +5883/−146 LOC; 0 errors, 15 pre-existing warnings, 6,072 tests pass + 1 flaky timing test fixed in follow-up commit** |
| Wrap-up | 🔲 Not started (0/1) | 🔲 Pending (task 090, ~4h) | Lessons-learned + Phase 2 outline + archive; runs after PR #337 merges to master |

**Honest math now**: 28/29 tasks ✅ (97% by count); only 4h of wrap-up remaining. **Insights r2 is effectively complete** subject to PR #337 merge + task 090.

#### 8.2 Coordination touchpoint resolutions (supersedes §4 open questions)

| Touchpoint | §4 open question @ early | Resolution @ late |
|---|---|---|
| **§4.1 SprkChatAgent tool registration** | "Did Insights E3 establish a tool-registration convention R5 should follow?" | ✅ YES. Wave E3 ships `POST /api/insights/assistant/query` as a **unified tool surface** (single endpoint, single response shape — `path: "playbook" | "rag"` discriminates the rendering). The BFF does routing internally via Wave E2 intent classifier. R5 should follow this pattern: **one endpoint per tool capability, not subtools**. Canonical contract: `projects/ai-spaarke-insights-engine-r2/design-e3-tool-call-contract.md` v1.0. |
| **§4.2 Intent classifier catalog** | "Is R5's Summarize tool in the classifier's catalog? What's the conflict-resolution rule?" | 🟡 STILL OPEN. Wave E2 classifier ships with playbook/RAG routing for **Insights-only** queries; R5's Summarize tool is NOT in the catalog. **Decision needed at R5 refinement gate**: extend the Insights classifier to be a global router, OR keep classifiers per-tool and let `SprkChatAgent` orchestrate at a higher layer. Current contract supports `forceMode` override so R5 can bypass classification when invoking via slash command. |
| **§4.3 ADR-010 DI pressure** | "Did combined service growth surface cohesion issues?" | ✅ NO. Wave E added 4 new services (classifier + NullClassifier + AssistantToolCallHandler + handler-internal helpers) all inside `AnalysisServicesModule`; ADR-010 compliance unchanged (modules pattern works as designed). |
| **§4.4 `FieldDelta` SSE reuse** | "Did Insights E3 reuse `FieldDelta`?" | ✅ NO — and intentionally so. Wave E3 chose **single-shot, non-streaming** for Phase 1.5 (per contract §11 — streaming is a Phase 2 deferral). Insights E3 response shape does NOT include `FieldDelta` events. **No protocol fragmentation**: R5's `FieldDelta` remains R5-specific until Phase 2 of Insights, where SSE adoption will be re-evaluated. If R5 ships `FieldDelta` first, Insights Phase 2 can adopt the existing protocol. |
| **§4.5 PaneEventBus event-type collisions** | "Did event-type names collide?" | ✅ NO. Wave E3 emits zero new PaneEventBus events (the contract is HTTP-only; rendering is the Assistant's responsibility). No coordination needed at code level. R5 retains exclusive ownership of `workspace.*` and `context.*` discriminants in scope. |
| **§4.6 Non-conflicts** | All verified | ✅ Still verified. Wave D added the 2 promised Dataverse entities (`sprk_documenttype_ref` + `sprk_practicearea_documenttype` N:N); no other entity additions. AI Search index split (`spaarke-insights-index` vs R5's planned `spaarke-session-files`) unchanged. |

#### 8.3 Refinement gate trigger conditions (per §6.2)

Original trigger required 4 conditions. Status now:

| Condition | Status |
|---|---|
| All Wave D tasks (030–036) ✅ | ✅ DONE (PR #336 merged) |
| All Wave E tasks (040–043) ✅ | ✅ Implementation done (PR #337 auto-merging) |
| Wrap-up task 090 ✅ (lessons-learned authored) | 🔲 Pending — ~4h estimated, runs after PR #337 merges |
| Insights r2 branch merged to master | 🔄 In flight (PR #337) |

**Estimated gate-fire window**: Within hours of PR #337 merging (assumes CI passes on the flake-fix re-run). Original estimate was "mid-to-late July 2026" — actual is dramatically faster due to parallel-subagent execution acceleration in Waves D + E.

#### 8.4 New companion document for R5

A focused **integration brief** has been authored for R5 implementation use:

> **`notes/insights-engine-assistant-integration-brief.md`** (this folder)

The brief is self-contained (cross-worktree references mirrored inline) and covers:

- Endpoint URL + auth + rate-limit + content-type
- Full request schema with `forceMode` semantics + sample payload
- Full response schema with per-path field semantics + worked examples (playbook + RAG + decline + empty-results)
- All 12 binding error codes + recommended Assistant action per code
- 6 open questions R5 owns (to record in contract review log)
- R5 scope-of-work (9 required items) + Phase 2 deferrals
- 10-item acceptance criteria checklist + Wave D7 synthetic test entity GUIDs
- 3 sample curl commands for smoke testing
- Coordination protocol (bug routing matrix)
- References to canonical artifacts + 8 relevant ADRs

**How the two docs relate**:

- `insights-r2-coordination.md` (this doc) → R5 **architecture / planning**: what's shared, what we MUST NOT rebuild, when to refine R5 design
- `insights-engine-assistant-integration-brief.md` (new) → R5 **implementation**: what endpoint to call, what payload to send, what to render, what errors to handle

Both docs are required reading for R5 implementers touching Insights consumption.

#### 8.5 What R5 should do next

1. **HOLD Phase 2 chat-tool work** per §6.5 risk guidance UNTIL PR #337 merges + task 090 lessons-learned authored. Estimated gate-fire: hours, not weeks.
2. When the refinement gate fires, run the §6.3 refinement pass — re-validate §3 reuse mandate + §4 touchpoint resolutions above + update R5 design.md.
3. Schedule R5 lead review of `design-e3-tool-call-contract.md` v1.0 (sub-task A.5 of Insights task 042 — formally PENDING until R5 records review). Decisions on the 6 open questions in `insights-engine-assistant-integration-brief.md` §6 belong in the contract's §10 review log.
4. Adopt the **single-endpoint-per-tool-capability** convention established by Wave E3 (per §8.2 §4.1 resolution above) when designing R5's Summarize tool registration.

#### 8.6 Provenance of this update

- Wave E3 author: Claude (Anthropic AI agent) running task-execute skill at FULL rigor, dispatched as sub-agent by main session
- Companion integration brief author: same agent (different turn)
- This changelog entry author: same agent (this turn)
- Operator approval: Ralph Schroeder (project owner)
- Verifiable via: PR #337 commits `7411a9c5` (Wave E impl) + `015445ea` (test tolerance fix); BFF source diff in `src/server/api/Sprk.Bff.Api/Services/Ai/Insights/`

---

### 2026-06-04 (late) — Wave F v1.1 shipped (SSE streaming + citations.href)

**Updated by**: Claude (Anthropic AI agent) on behalf of the Insights Engine r2 project (operator: Ralph Schroeder).
**Trigger**: Wave F (contract v1.1) implementation complete; tasks 050 (F1 spike) + 051 (F2 SSE) + 052 (F3 citations href) + 053 (F4 docs) all ✅. Pending main-session batch commit + PR open + auto-merge.

#### 8.7 Wave F status summary

| Wave F task | Wave-item | Deliverable | Status |
|---|---|---|---|
| 050 | F1 | Streaming surface + citation ID flow spike → `notes/spikes/wave-f-streaming-citation-spike.md` (6 sections A–F); binding scope decision: **SHIP FULL** (both observation + document citation href; R5 escape hatch NOT triggered — plumbing cost Small) | ✅ |
| 051 | F2 | `IInsightsAi.AssistantQueryStreamAsync` + `AssistantQueryChunk` Zone-B DTO + endpoint `Accept`-header negotiation (`application/json` → single-shot; `text/event-stream` → SSE) + 8 new tests | ✅ |
| 052 | F3 | `AssistantQueryCitation.Href` optional field (JSON key lowercase `href`) + `AssistantCitationHrefOptions` config (`Insights:CitationHref:BffBaseUrl`) + URL pattern `{bffBaseUrl}/api/documents/{sprk_document-guid}/preview` + 17 new tests | ✅ |
| 053 | F4 | Contract v1.1 docs (`design-e3-tool-call-contract.md` bumped + §3.5 SSE + §4.6 href + §12 changelog; integration brief amended; this changelog entry) | ✅ |

**Effort**: ~4.5d total (matches mini-plan estimate). **Wall clock**: ~4 days end-to-end with 051+052 parallel sub-agent dispatch (per mini-plan §4.1 Round 2 parallelism). 0 stuck-agent incidents (`feedback-detect-stuck-subagents` lesson held). Tests across both new features: 25 added (17 citation-href + 8 SSE) — all passing. Publish size 44.13 MB compressed (Wave E baseline was 44.10 MB; +0.03 MB delta, well under +5 MB per-task threshold). `dotnet format whitespace Spaarke.sln --verify-no-changes` clean. §3.5 facade-boundary grep clean.

#### 8.8 §4.4 touchpoint (FieldDelta SSE reuse) — **RESOLVED**

| Open question @ 2026-06-03 late | Resolution @ 2026-06-04 late |
|---|---|
| "Did Insights E3 reuse `FieldDelta`?" — answered NO at 2026-06-03 late (Insights E3 single-shot only) | **ANSWERED YES (with intentional protocol parity)** in v1.1. Insights chose the `AssistantQueryChunk` variant — same shape family as R5's `AnalysisChunk` (Type / Step / Path / Content / Sequence / Result / Error). R5's chat agent's existing `AnalysisChunk`-aware SSE parser CAN consume `insights.query` streams with no protocol divergence — Type discriminates (`progress` | `delta` | `result` | `error`), Path tags the streaming field (`"answer"` on RAG deltas), Sequence orders deltas 1-based, terminator is `data: [DONE]\n\n`. Naming differs by tool (`AssistantQueryChunk` vs `AnalysisChunk`) but the wire-shape is intentionally aligned so a single R5 parser handles both. |

**Decision summary**: protocol parity NOT a literal `FieldDelta` import. R5's `FieldDelta` design and Insights' `AssistantQueryChunk` design are siblings in the same shape family, not a single shared type. This preserves Zone A/B facade boundaries (each tool owns its DTO) while delivering R5-side parser reuse. If a future cleanup phase chooses to factor the shape into a shared `Spaarke.Core.Streaming.SseChunk<TResult>` generic, both projects can adopt it back-compatibly. **No protocol fragmentation. No R5 design rework required.**

#### 8.9 §4.6 touchpoint (citations clickable) — **RESOLVED**

| Open question @ original | Resolution @ 2026-06-04 late |
|---|---|
| "Citation display — display names enough? Or clickable URLs needed?" (R5-side §6 question 4 in the integration brief) | **ANSWERED with citations[].href shipped in v1.1 (Full scope)**. `href` is an optional lowercase JSON key on each citation. Present on both observation (RAG) and document (playbook) citations per F1 spike §F binding decision — R5 escape hatch NOT triggered because plumbing cost was Small (≤0.5d, ~3.5h actual). |

**URL pattern**:

```
{Insights:CitationHref:BffBaseUrl}/api/documents/{sprk_document-guid}/preview
```

**Required configuration** (R5 ops): the BFF needs `Insights:CitationHref:BffBaseUrl` set per environment. Dev: `https://spaarke-bff-dev.azurewebsites.net`. Staging/Production: matching environment URLs. When unset (or empty), the BFF emits `href: null` for ALL citations as a safe fallback — no broken URLs ever emitted. R5 deployment checklist: verify the config is set on the BFF App Service Configuration BEFORE smoke-testing the v1.1 contract end-to-end.

**AIPU2-027 enforcement**: the `/api/documents/{id}/preview` endpoint enforces ACL via existing OBO; if the user lacks access, the endpoint returns 403/404 naturally — R5's chat agent handles as an opaque error per the existing R5 §1 response (iframe-render fails gracefully). **No URL signing, no token embedding** — the citation href is opaque-from-client and authorization-checked at click-time on the BFF preview endpoint.

**v1.2 deferred subset**: when the playbook `EvidenceRef.Ref` is a `spe://drive/X/item/Y` URI (instead of a bare sprk_document Guid), v1.1 emits `href: null` for that citation. F1 spike empirically confirmed `spe://` is the dominant production emit pattern from `FilesIndexIngestDocumentSource.cs:166` — but the playbook citations through `predict-matter-cost@v1` predominantly hit the bare-Guid path OR non-document evidence types, so the v1.1 `null` fallback covers the minority case. v1.2 will add an async `driveId+itemId → sprk_document` lookup (the existing `DataverseObservationMirror.ResolveDocumentIdAsync` pattern), making citation projection async. Empirically a minority case — explicit per-spike trade-off to keep v1.1 synchronous.

#### 8.10 R5 next actions (post-v1.1)

1. **Optionally consume `Accept: text/event-stream`** when invoking `POST /api/insights/assistant/query` from R5 chat-pane code paths where progressive rendering matters (RAG synthesis token streaming + playbook node-progress UX). Single-shot remains the default for tool-call contexts that don't benefit from streaming. R5's existing `AnalysisChunk` SSE parser handles `AssistantQueryChunk` events with no code changes (same shape family).
2. **Render `citations[].href` as iframe-target / clickable link** when non-null; fall back to display-name-only when null. The `/preview` URL is iframe-loadable (same surface as R5's `FilePreviewContextWidget` already uses).
3. **Verify `Insights:CitationHref:BffBaseUrl` is set** on the target BFF environment before R5 smoke-tests v1.1 features. Ops coordination with Insights team. Documented in §8.9 above.
4. **No R5 design rework** for the §4.4 touchpoint — Insights' `AssistantQueryChunk` is shape-compatible with R5's `AnalysisChunk`. R5's existing SSE parser reuse path holds.

#### 8.11 Provenance of this update

- Wave F task 053 (F4) author: Claude (Anthropic AI agent) running task-execute skill at MINIMAL rigor (docs-only update), dispatched as sub-agent by main session
- This changelog entry author: same agent (this turn)
- Operator approval: Ralph Schroeder (project owner)
- Verifiable via: Wave F branch `work/ai-spaarke-insights-engine-r2-wave-f`; tasks 050 + 051 + 052 + 053 in `projects/ai-spaarke-insights-engine-r2/tasks/`; spike + mini-plan in `projects/ai-spaarke-insights-engine-r2/notes/spikes/` + `projects/ai-spaarke-insights-engine-r2/notes/`; design contract diff in `projects/ai-spaarke-insights-engine-r2/design-e3-tool-call-contract.md` (v1.0 → v1.1)
