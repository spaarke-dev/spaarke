# CLAUDE.md — Spaarke AI Platform Unification R5

> **Project-scoped AI context for R5 implementation tasks**
> **Created**: 2026-06-03 (late)
> **Sources**: `design.md`, `spec.md`, `plan.md`, `notes/insights-r2-coordination.md`
> **Purpose**: This file is loaded by AI agents executing R5 tasks. It complements the root `CLAUDE.md` (repository-wide rules) and the project's `design.md` + `spec.md` (R5-specific context).

---

## 1. Project identity

R5 ships a chat-driven "Summarize a Document" vertical slice + Insights tool integration (`insights.query`) through the SpaarkeAi three-pane shell. The R5 chat agent becomes the **Spaarke Assistant** in operational form — hosting two AI tool capabilities (Summarize + Insights) alongside the existing ad-hoc grounded-LLM mode. R5 is **configuration-first**: no new ADRs, no new event-bus channels, no new top-level DI registrations. Every new capability extends existing platform infrastructure.

**Effort**: ~2.5–3 weeks, ~36–44 tasks across 3 phases.

---

## 2. Reading order (load these before any R5 task)

1. **`spec.md`** — Formal FR/NFR/DR/PR enumeration; success criteria; owner clarifications; binding constraints. This is the source of truth for what R5 ships.
2. **`design.md`** — Vision, vertical-slice user flow, investment-surfacing map, reuse mandate (§2.11), phasing (§7). Read §4.12 for Insights tool integration scope.
3. **`plan.md`** — WBS with phase breakdown (Phase 1 / Phase 2 / Phase 3); ~36–44 deliverables mapped to FRs; parallel-execution groups; critical path; high-risk items.
4. **`notes/insights-r2-coordination.md`** — Cross-project coordination doc; §8 changelog tracks Insights r2 Wave F status; §4 documents 5 coordination touchpoints (4 resolved + 1 open).
5. **`notes/insights-engine-assistant-integration-brief.md`** — BINDING contract v1.0 for R5's `insights.query` tool consumption. Read fully before any §4.12 work.
6. **`notes/insights-engine-contract-v1.1-request.md`** — Wave F negotiation outcome (SSE + clickable citations); §0a Negotiation Outcome + §9 changelog.

---

## 3. R5-specific rules (in addition to root CLAUDE.md)

### 3.1 Reuse mandate (per spec §2.11)

Before any R5 task adds a NEW service, model, endpoint, widget, or DI registration, the implementer MUST:

1. Confirm there's no existing equivalent (search `src/server/api/Sprk.Bff.Api/Services/Ai/` + `src/client/shared/` libraries)
2. Cite the existing component in the task's design notes if reusing
3. If proposing new component: cite the gap explicitly (what existing component falls short and why)
4. Run `/conflict-check` before merge
5. For BFF additions: follow `.claude/constraints/bff-extensions.md`

**Specifically prohibited** (R5 MUST NOT rebuild):
- Parallel orchestrators to `AnalysisOrchestrationService` or `InsightsOrchestrator`
- Parallel RAG search service to `RagService`
- Parallel session-management layer to `ChatSessionManager`
- Parallel chat agent to `SprkChatAgent`
- Parallel file-preview component to `RichFilePreviewDialog` (extract renderer, don't rebuild)
- Parallel SSE envelope to `AnalysisChunk` (R5 EXTENDS with `FieldDelta` variant; does not introduce a new envelope)
- New prompt-bearing Dataverse entity (`sprk_analysisaction.sprk_systemprompt` IS the primitive)
- New playbook orchestration layer paralleling `PlaybookExecutionEngine`
- New PaneEventBus channel (closed at 4 per ADR-030 — additive event types only)

### 3.2 R5 introduces NO new feature flags (per ADR-018 Flag Scope Discipline)

R5 sub-services are unconditionally registered. Kill-switch coverage inherits from existing `Analysis:Enabled` / `Chat:Enabled` flags via `NullSprkChatAgentFactory`. ADR-032 Null-Object patterns are NOT applicable to R5 services. If a new feature flag becomes truly necessary, follow ADR-018 §"Flag Scope Discipline" — flag at capability boundary, not service boundary.

### 3.3 DI registration discipline (ADR-010 + R5-specific)

- All new R5 services register inside existing `AnalysisServicesModule.cs` feature module (or extend with `AddR5SessionFilesModule()` if cohesion warrants)
- ZERO new top-level `services.AddXxx()` lines in `Program.cs`
- Concrete classes by default; `virtual` methods only where Null-Object subclassing is required (none expected for R5)
- The 265-baseline-registration count grows additively — acknowledged per ADR-010 Phase 5 baseline note

### 3.4 PaneEventBus event-type additions (ADR-030)

R5 adds these additive event types within existing channels:
- `workspace.streaming_started`
- `workspace.field_delta`
- `workspace.streaming_complete`
- `context.files_staged`
- `context.file_selected`

Existing subscribers ignore unknown discriminants. R5 MUST NOT add a 5th channel.

### 3.5 Insights tool integration governance

- R5 consumes `POST /api/insights/assistant/query` per binding contract v1.0 + v1.1 (Wave F in flight)
- R5 NEVER injects `IInsightsAi`, `IRagService` Insights-specific extensions, or other Insights-internal types directly (per refined ADR-013 §3.5 facade boundary; R5 is Zone B consumer of HTTP contract only)
- R5 lead MUST sign off on `projects/ai-spaarke-insights-engine-r2/design-e3-tool-call-contract.md` v1.0 + record 6 contract-review decisions (D1–D6 per spec.md §8.2) in §10 review log BEFORE Phase 2 §4.12 work begins
- All 12 binding error codes from integration brief §5.1 MUST be handled with appropriate UX per brief column 4
- v1.1 SSE consumption: graceful fallback to v1.0 single-shot if Wave F deploys later than R5 Phase 2 need
- v1.1 `citations[].href`: graceful fallback to display-name-only rendering if `href: null` or absent

### 3.6 BFF publish-size discipline (CLAUDE.md §10 + ADR-029)

Each R5 task touching BFF code MUST:
1. Run `dotnet publish -c Release src/server/api/Sprk.Bff.Api/ -o deploy/api-publish/`
2. Measure compressed output
3. Report absolute size + delta vs prior baseline in task notes / PR description
4. Verify ceiling: ≤60 MB compressed (NFR-01 per `.claude/constraints/azure-deployment.md`)
5. Current baseline ~45.65 MB; R5 budget ≤ +1 MB (target +0.5 MB)
6. ≥+5 MB single-task delta → escalation required

### 3.7 Test obligation per task (per CLAUDE.md §10 F-test-update)

PRs modifying `src/server/api/Sprk.Bff.Api/Services/` MUST add/update tests in `tests/unit/Sprk.Bff.Api.Tests/`. Endpoints mapping unconditionally MUST have unconditional service registration (per F.1 sub-mechanism). R5 services are unconditionally registered per §3.2 above — no asymmetric-registration risk.

### 3.8 ChatSession.UploadedFiles per-session cap

Hard limit: 20 files per session. Files < 500 tokens skip chunking (single-chunk index). Aggressive cleanup-on-session-end (don't wait for scheduled sweep). Per NFR-02.

---

## 4. Task execution protocol

Every R5 task MUST be executed via the `task-execute` skill at **FULL rigor**. Per CLAUDE.md §4:

> **ABSOLUTE RULE**: When executing project tasks, Claude Code MUST invoke the `task-execute` skill. DO NOT read POML files directly and implement manually.

Trigger phrases:
- "work on task X" → invoke task-execute with task X POML
- "continue" / "next task" → read `tasks/TASK-INDEX.md`, find first 🔲, invoke task-execute
- "continue with task X" → invoke task-execute with task X POML
- "pick up where we left off" → load `current-task.md`, invoke task-execute

R5 rigor level: **FULL** for all tasks (BFF code, frontend widgets, Dataverse seed deploys, RAG infrastructure — all qualify for FULL per CLAUDE.md §8 decision tree).

### 4.1 Mandatory rigor declaration

At task start, the task-execute skill outputs:
```
🔒 RIGOR LEVEL: FULL
📋 REASON: [Why FULL applies — e.g., "BFF code change + AI infrastructure + new service registration"]
📖 PROTOCOL STEPS TO EXECUTE: [Per task-execute SKILL.md Step 0.5]
Proceeding with Step 0...
```

This declaration is non-negotiable per CLAUDE.md §8.

### 4.2 Quality gates at Step 9.5

FULL-rigor tasks run `code-review` + `adr-check` skills at Step 9.5 per task-execute. Both must pass before task is marked ✅.

### 4.3 PCF version bumping + deployment

R5 has NO PCF controls. PCF deployment skill (`pcf-deploy`) is NOT applicable. Code-page deployment (`code-page-deploy`) IS applicable for SpaarkeAi shell + LegalWorkspace updates if R5 task touches those.

---

## 5. Context management

Per CLAUDE.md §5:

| Context usage | Action |
|---|---|
| < 60% | Proceed normally |
| 60–70% | Run `context-handoff` (proactive save), then continue |
| > 70% | STOP. Run `context-handoff`, request `/compact` |
| > 85% | EMERGENCY. Run `context-handoff`, stop immediately |

**Proactive checkpointing** (MANDATORY):
- After every 3 completed task steps → silent checkpoint ("✅ Checkpoint.")
- After modifying 5+ files → checkpoint
- After any deployment → checkpoint
- Before a complex step → checkpoint
- Context > 60% → verbose checkpoint report
- Context > 70% → checkpoint + STOP + `/compact`

All work state recoverable from files alone: `projects/spaarke-ai-platform-unification-r5/current-task.md` + `tasks/TASK-INDEX.md` + `notes/`.

---

## 6. Human escalation triggers

MUST request human input for:
- Insights v1.1 contract divergence (Wave F deploys with semantics different from contract v1.1 spec)
- ADR conflicts (any R5 task that surfaces a conflict with binding ADR — escalate per CLAUDE.md §6)
- Tool routing disambiguation surprises during Phase 2 (UR-01)
- BFF publish-size delta exceeding +1 MB single-task delta (escalation per ADR-029)
- Coordination touchpoints with Insights team (Wave F status changes; integration brief updates)
- Scope expansion beyond R5 spec FRs

---

## 7. Cross-project awareness

R5 ships in parallel with Insights Engine R2 Wave F (operator-approved 2026-06-03 late; Insights team executing via Claude Code on branch `work/ai-spaarke-insights-engine-r2-wave-f`). Coordination protocol:

- `notes/insights-r2-coordination.md` §8 changelog tracks Wave F status — UPDATE this when Wave F starts / spike completes / deploys / ships
- Wave F deploys to Spaarke Dev between R5 Phase 1 close and Phase 2 W3 (R5 Phase 2 Insights consumption starts W3+)
- If Wave F slips: R5 Phase 2 ships consuming v1.0 (graceful fallback per NFR-11)
- If Wave F ships ahead: R5 Phase 2 consumes v1.1 from launch

---

## 8. Key file paths (R5 implementation surface)

**Spec / design / plan**:
- `projects/spaarke-ai-platform-unification-r5/spec.md`
- `projects/spaarke-ai-platform-unification-r5/design.md`
- `projects/spaarke-ai-platform-unification-r5/plan.md`

**Backend (BFF) — files R5 modifies or creates**:
- `src/server/api/Sprk.Bff.Api/Services/Ai/RagService.cs` (extend with `sessionId` filter)
- `src/server/api/Sprk.Bff.Api/Services/Ai/RagSearchOptions.cs` (additive parameter)
- `src/server/api/Sprk.Bff.Api/Services/Ai/RagIndexingPipeline.cs` (parameterize for session-files writes)
- `src/server/api/Sprk.Bff.Api/Models/Ai/ChatSession.cs` (extend with `UploadedFiles[]`)
- `src/server/api/Sprk.Bff.Api/Models/Ai/AnalysisChunk.cs` (additive `FieldDelta` variant)
- `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/SessionSummarizeOrchestrator.cs` (NEW)
- `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/SessionFilesCleanupJob.cs` (NEW)
- `src/server/api/Sprk.Bff.Api/Infrastructure/DI/AnalysisServicesModule.cs` (extend with new registrations)
- `src/server/api/Sprk.Bff.Api/Api/Chat/SummarizeSessionEndpoint.cs` (NEW)
- Chat tool registration site for `InvokeSummarizePlaybookTool` + `InsightsQueryToolHandler`

**Frontend — files R5 modifies or creates**:
- `src/client/shared/Spaarke.AI.Widgets/src/widgets/workspace/StructuredOutputStreamWidget.tsx` (NEW)
- `src/client/shared/Spaarke.AI.Widgets/src/widgets/workspace/register-structured-output-stream-widget.ts` (NEW)
- `src/client/shared/Spaarke.AI.Widgets/src/widgets/context/FilePreviewContextWidget.tsx` (NEW)
- `src/client/shared/Spaarke.AI.Widgets/src/registry/register-context-widgets.ts` (extend)
- `src/client/shared/Spaarke.UI.Components/src/components/FilePreview/RichFilePreview.tsx` (NEW — extracted renderer)
- `src/client/shared/Spaarke.UI.Components/src/components/FilePreview/RichFilePreviewDialog.tsx` (refactor to use extracted renderer)
- `src/client/shared/Spaarke.AI.Widgets/src/widgets/workspace/DocumentViewerWidget.tsx` (upgrade R4 stub)
- `src/client/shared/Spaarke.AI.Widgets/src/events/PaneEventTypes.ts` (additive event types)
- `src/client/shared/Spaarke.UI.Components/src/components/SlashCommandMenu/slashCommandMenu.types.ts` (`/summarize` description extension)
- `src/solutions/SpaarkeAi/src/components/conversation/ConversationPane.tsx` (chat-pane file UX)
- `src/solutions/SpaarkeAi/src/components/context/ContextPaneController.tsx` (file preview dispatch)
- Frontend `InsightsResponseRenderer` component (NEW)

**Infrastructure**:
- `infra/ai-search/spaarke-session-files.index.json` (NEW)
- Bicep module extensions for session-files index provisioning

**Dataverse data**:
- New `sprk_analysisaction` seed: "Summarize Document for Chat" (config; deployed via `scripts/Deploy-Playbook.ps1`)
- New `sprk_analysisplaybook` row linking the action

**Tests**:
- `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/` (multiple new test files)
- `tests/integration/` (Summarize end-to-end + Insights tool consumption)
- Frontend test paths (matching widget locations)

---

## 9. Applicable ADRs (R5-specific summary)

| ADR | R5 implication |
|---|---|
| ADR-001 | R5 endpoints in BFF only; cleanup job is `IHostedService` |
| ADR-006 | SpaarkeAi shell is Code Page; new widgets React 19 |
| ADR-007 | All file ops via `SpeFileStore` facade or `DocumentCheckoutService.GetPreviewUrlAsync` for previews |
| ADR-008 | New endpoint adds `[EndpointFilter(typeof(AuthorizationFilter))]` |
| ADR-009 | Session file manifest in Redis hot tier (24h TTL) |
| **ADR-010** | All new services register inside `AnalysisServicesModule`; zero new top-level lines |
| ADR-012 | New widgets in `@spaarke/ai-widgets` + `@spaarke/ui-components` |
| **ADR-013** | All R5 endpoints in BFF; agents via factory; R5 is Zone B consumer of Insights contract |
| ADR-014 | `spaarke-session-files` index documents MUST carry `tenantId` AND `sessionId` |
| ADR-016 | R5 inherits `ai-context` rate-limit policy |
| **ADR-018** | R5 introduces NO new feature flags; inherit from existing capability flags |
| ADR-019 | R5 endpoint errors use ProblemDetails with stable `errorCode` extension |
| ADR-021 | New widgets Fluent v9 semantic tokens; dark-mode tested |
| ADR-022 | All new R5 widgets React 19 |
| ADR-026 | No build-tooling change |
| **ADR-028** | New endpoint inherits Auth v2; no token snapshots |
| ADR-029 | Per-task publish-size verification |
| **ADR-030** | Add additive event types only; zero new channels |
| ADR-031 | R5 UI respects `useShellStage()` |
| **ADR-032** | NOT applicable to R5 (no conditional registrations per ADR-018 Flag Scope Discipline) |

Bold = highest-leverage / most-likely-to-bite-you. Read these in full before implementation.

---

## 10. Common gotchas + anti-patterns

- **Don't snapshot tokens**: chat agent uses fresh token per request (ADR-028). Test scenario: token expires after 80min idle — token must be re-fetched, not reused from a closure.
- **Don't add new Dataverse entity for prompts**: `sprk_analysisaction.sprk_systemprompt` IS the JPS prompt-bearing primitive (per Insights r2 explicit lock-in). New playbooks add new ACTION rows, not new entities.
- **Don't inject Insights internals**: R5 calls `POST /api/insights/assistant/query` as HTTP. Never `services.AddScoped<IInsightsAi>` or directly inject Insights services into R5 frontend / chat agent. Zone B consumer only.
- **Don't add a 5th PaneEventBus channel**: closed at 4 per ADR-030. R5 adds event types WITHIN existing channels.
- **Don't bundle Insights `NullInsightsAi` cleanup with R5 work**: per Insights team feedback #6 + spec §8.2 — separate ticket.
- **Don't run `dotnet publish` without measurement**: every BFF-touching task MUST report size + delta per CLAUDE.md §10. Skipping the verification is a defect.
- **Don't proceed past task without `task-execute` invocation**: bypassing the skill loses ADR loading + checkpoint discipline + quality gates. Per CLAUDE.md §4: absolute rule.

---

## 11. Pointers to related project artifacts

- **Active task state**: `current-task.md` (updated by task-execute per task)
- **Task registry + status**: `tasks/TASK-INDEX.md` (all R5 tasks + parallel groups + critical path)
- **Insights coordination**: `notes/insights-r2-coordination.md` (cross-project)
- **Lessons-learned (final)**: `notes/lessons-learned.md` (created at Phase 3 wrap-up)
- **R6 backlog**: section within lessons-learned.md (captured at wrap-up)

---

*Maintained by: R5 project owner + task-execute skill. To extend this file: follow rules in `.claude/skills/ai-procedure-maintenance/SKILL.md`. Cross-cutting rules that apply to all repo work: root `CLAUDE.md`.*
