# Spaarke Daily Update Service R4 — AI Context

> **Purpose**: This file provides context for Claude Code when working on `spaarke-daily-update-service-r4`.
> **Always load this file first** when working on any task in this project.

---

## Project Status

- **Phase**: Development (artifacts scaffolded; task 001 pending)
- **Last Updated**: 2026-06-25
- **Current Task**: Not started
- **Next Action**: Run `/task-execute` with task 001 (after `/project-pipeline` Step 3 completes task-create)

---

## Quick Reference

### Key Files
- [`spec.md`](spec.md) - AI-optimized specification (20 FRs, 6 NFRs)
- [`design.md`](design.md) - Original design document (UAT-driven architectural review 2026-06-25)
- [`README.md`](README.md) - Project overview and graduation criteria
- [`plan.md`](plan.md) - Implementation plan with 5-PR WBS
- [`current-task.md`](current-task.md) - **Active task state** (for context recovery)
- [`tasks/TASK-INDEX.md`](tasks/TASK-INDEX.md) - Task tracker with parallel groups
- [`notes/risks.md`](notes/risks.md) - R3 PR #451 file overlap + spec path correction note

### Project Metadata
- **Project Name**: spaarke-daily-update-service-r4
- **Type**: BFF API + PCF/shared-lib React + Dataverse data deployment (JPS Action rows + Playbook rows)
- **Complexity**: High — 3 coordinated workstreams, 5 phased PRs, ~45 tasks, ~65 engineering hours
- **Branch**: `work/spaarke-daily-update-service-r4`

---

## Context Loading Rules

When working on this project, Claude Code should:

1. **Always load this file first** when starting work on any task
2. **Check current-task.md** for active work state (especially after compaction/new session)
3. **Reference spec.md** for design decisions, requirements, and acceptance criteria
4. **Load the relevant task file** from `tasks/` based on current work
5. **Apply ADRs** relevant to the technologies used (loaded automatically via adr-aware)

**Context Recovery**: If resuming work, see [Context Recovery Protocol](../../docs/procedures/context-recovery.md)

---

## 🚨 MANDATORY: Task Execution Protocol

**ABSOLUTE RULE**: All task work MUST use the `task-execute` skill. DO NOT read POML files directly and implement manually.

### Auto-Detection Rules (Trigger Phrases)

When you detect these phrases from the user, invoke task-execute skill:

| User Says | Required Action |
|-----------|-----------------|
| "work on task X" | Execute task X via task-execute |
| "continue" | Execute next pending task (check TASK-INDEX.md for next 🔲) |
| "continue with task X" | Execute task X via task-execute |
| "next task" | Execute next pending task via task-execute |
| "keep going" | Execute next pending task via task-execute |
| "resume task X" | Execute task X via task-execute |
| "pick up where we left off" | Load current-task.md, invoke task-execute |

### Why This Matters

The task-execute skill ensures:
- ✅ Knowledge files are loaded (ADRs, constraints, patterns)
- ✅ Context is properly tracked in current-task.md
- ✅ Proactive checkpointing occurs every 3 steps
- ✅ Quality gates run (code-review + adr-check) at Step 9.5
- ✅ Progress is recoverable after compaction
- ✅ BFF Hygiene (§10) publish-size + CVE checks run per task

**Bypassing this skill leads to**: missing ADR constraints, no checkpointing, skipped quality gates.

### Parallel Task Execution

The plan flags 5 parallel groups (A in PR 1, B in PR 1, C in PR 2, D in PR 3, E in PR 5 — sub-waves). When tasks in a group are independent (file surfaces don't overlap), send ONE message with MULTIPLE Skill tool invocations (each calling task-execute for one task). See [task-execute SKILL.md](../../.claude/skills/task-execute/SKILL.md) for the protocol.

**Max concurrency: 6 agents per wave.** Tasks touching `.claude/` paths MUST be sequential (main-session-only — sub-agents cannot write `.claude/`).

### 🚨 MUST: Multi-File Work Decomposition

For tasks modifying 4+ files (e.g., task 020 enrich customData, task 045 three-dot overflow menu), use the Multi-File Work Decomposition protocol — see [task-execute SKILL.md Step 8.0](../../.claude/skills/task-execute/SKILL.md).

---

## Key Technical Constraints

### Binding ADRs (from spec.md §Technical Constraints)

- **ADR-013** (BFF AI Architecture) — `/narrate` playbook dispatch follows `AnalysisOrchestrationService` pattern; no new AI endpoints beyond JPS-composed primitives. All AI work stays in BFF.
- **ADR-021** (Fluent v9 Design System) — three-dot overflow menu uses Fluent v9 `Menu` / `MenuItem` / icons; dark-mode required; semantic tokens ONLY (no raw hex).
- **ADR-024** (sprk_todo Polymorphic Resolver) — preserve `useInlineTodoCreate` + `TODO_REGARDING_CATALOG` in overflow menu's "Add to To Do" action.
- **ADR-027** (Subscription Isolation) — `appnotification` is CORE entity. `sprk_briefingstate` Choice column (R3) preserved.
- **ADR-028** (Spaarke Auth v2) — Contact ↔ SystemUser cross-ref via `azureactivedirectoryobjectid` is canonical mapping.
- **ADR-034** (User-record Membership) — `LookupUserMembership` ActionType 52 is canonical primitive. R4 deploys its missing Action row.

### Additionally Relevant ADRs

- **ADR-001** (Minimal API) — `/narrate` wrapper follows existing endpoint-filter convention
- **ADR-010** (DI Minimalism) — NodeExecutor registration via focused module extension
- **ADR-029** (BFF Publish Hygiene) — publish-size + CVE verification on every BFF-touching task
- **ADR-037** (Multinode Output Composition) — `DAILY-BRIEFING-NARRATE` playbook node graph

### CLAUDE.md §10 BFF Hygiene (Binding Per Task)

Every BFF-touching task MUST:
1. Load `.claude/constraints/bff-extensions.md` before designing the addition
2. State placement decision explicitly (cite decision criteria)
3. Use `Services/Ai/PublicContracts/` facade for CRUD→AI cross-references
4. Verify publish-size impact via `dotnet publish -c Release src/server/api/Sprk.Bff.Api/ -o deploy/api-publish/` — ≤60 MB ceiling, ≤+5 MB single-task delta
5. Verify no new HIGH-severity CVE via `dotnet list package --vulnerable --include-transitive`
6. Update tests in `tests/unit/Sprk.Bff.Api.Tests/`

### Spec-Specific MUST Rules (from spec.md §MUST Rules)

**JPS Data Layer (W0)**:
- ✅ MUST deploy `sprk_analysisaction` rows to spaarkedev1 for every new ActionType
- ✅ MUST follow `jps-action-create` / `jps-playbook-design` / `jps-playbook-audit` skills
- ✅ MUST run `jps-scope-refresh` after W0 deployments
- ❌ MUST NOT skip JPS deployment ("code is shipped" is not sufficient — code + data both required)
- ❌ MUST NOT merge PR 2 before PR 1 propagates to spaarkedev1

**Producer (W1)**:
- ✅ MUST enrich customData backward-compatibly (null/missing fields gracefully handled)
- ✅ MUST dual-write `sprk_category` column (Dataverse OData does NOT support `$filter` on nested JSON)
- ✅ MUST log structured `member_skipped` warning for Contact-only members
- ❌ MUST NOT silently drop Contact-only members
- ✅ MUST preserve idempotency check (`CheckForDuplicateNotificationAsync`)

**Consumer (W2)**:
- ✅ MUST replace hardcoded prompt strings with playbook dispatch
- ✅ MUST use temperature 0 in BRIEF-NARRATE Action JPS prompts
- ✅ MUST add explicit grounding instruction ("use ONLY names from input")
- ✅ MUST scrub LLM output via `EntityNameValidator` Tool
- ❌ MUST NOT bake legal-genre example names (Acme Corp, etc.) into JPS prompts
- ✅ MUST use server-side `$filter` for `disabledChannels` (not just UI hide)
- ✅ MUST remove `minConfidence` references from all layers (UI, types, persistence)
- ❌ MUST NOT preserve inline 5-icon row

---

## Decisions Made

### 2026-06-25 — `EntityNameValidator = 141` ExecutorActionType
- **Reason**: Slots into post-LLM cluster (Sanitization=130, ObservationEmit=140). Confirmed by owner.

### 2026-06-25 — Use `sprk_category` column (not `customData.category`) for query filters
- **Reason**: Dataverse OData does NOT support `$filter` on nested JSON. FR-17c uses `sprk_category not in (…)`. Producer dual-writes column + JSON per FR-6 AC-6d.

### 2026-06-25 — Repo JSON files = canonical source-of-truth for playbook reconciliation
- **Reason**: Easier audit + version control than deployed Dataverse rows. W0.4 reads deployed → compares against repo → redeploys from repo if divergent.

### 2026-06-25 — Develop in parallel with R3 PR #451 (don't block on R3 merge)
- **Reason**: Spec line 305 explicit author intent — "R3 PR #451 stays in draft and merges separately (R4 is independent; can be developed in parallel and merged in any order)." Document file overlaps in `notes/risks.md`; run `conflict-check` per W2 PR.

### 2026-06-25 — Spec path correction: TTL fix lives at `CreateNotificationNodeExecutor.cs:490` (not `NotificationService.cs`)
- **Reason**: Pre-flight verification showed `Services/Ai/NotificationService.cs` doesn't exist. The TTL fix (`ttlinseconds = 604800`) actually lives in `BuildNotificationEntity` at `CreateNotificationNodeExecutor.cs:490`. Tasks reference the correct path.

### 2026-06-26 — Canonical-truth loop steps 1-3 complete; 4 new canonical docs live
- **Reason**: R4 UAT defects traced to per-task-assumption drift on playbook runtime semantics + config-bag boundary. Loop step 1 (code archaeology) + step 2 (docs survey) + step 3 (write canonical docs) executed sequentially.
- **Outputs**: `docs/architecture/ai-architecture-playbook-runtime.md` (load-bearing runtime contract), `ai-architecture-consumer-routing.md` (Path A.5 triangle), `ai-architecture-actions-nodes-scopes.md` (config-bag boundary decision tree), `docs/guides/ai-guide-playbook-deploy-recipe.md` (Deploy-Playbook.ps1 contract). `.claude/constraints/bff-extensions.md` §G added + checklist item 6. `playbook-architecture.md` now redirects; `AI-ARCHITECTURE.md` + `JPS-AUTHORING-GUIDE.md` + `PLAYBOOK-AUTHOR-GUIDE.md` differentiated; JPS §§6/9/10/14 stripped to skills.
- **Next**: step 4 (JPS skill alignment), step 5 (R4 playbook deploy from canonical foundation).

### 🚨 2026-06-25 — Spaarke entity architecture (BINDING for every R4 task touching playbook entities)
- **Owner clarification (verbatim)**: "we do not use OOB tasks / activities or email — our corresponding entities are Events (with event type = tasks, for tasks; but we track other event types too) and communications (with type = email). I'm surprised this has come up since this has been a core part of the design from the very beginning."
- **Rule**:
  - ❌ NEVER use OOB `task` / `email` / `appointment` activity entities
  - ✅ Tasks → `sprk_event` with event-type discriminator = task
  - ✅ Emails → `sprk_communication` with type discriminator = email
  - ✅ General events → `sprk_event` with appropriate discriminator
  - ✅ `appnotification` REMAINS the one OOB entity R4 touches (Microsoft notification surface; customData JSON evolves but column stays OOB)
- **Spec impact (errors to correct as we go)**:
  - FR-7 "FetchXml union ... Dedupe by activityid" → should be `sprk_eventid` (sprk_event is the target entity, not OOB task)
  - PR 3 W1 tasks 022/023 (migrate `notification-tasks-{overdue,due-soon}.json`) → repo JSON files target OOB `task`; deployed playbooks target `sprk_event`. Repo files need REWRITE to match `sprk_event`, then add membership-scope union.
  - PB-016 (Emails) → deployed targets `sprk_communication`; repo JSON targets OOB `email` — repo needs rewrite.
  - PB-019 (Events) → deployed targets `sprk_event`; repo JSON targets OOB `appointment` — repo needs rewrite.
  - "Repo JSON files = canonical source-of-truth" decision (2026-06-25) is REVISED: deployed entity choices are canonical; repo JSON files were authored against a wrong OOB assumption and must be corrected before W0/W1 redeploy work.
- **Application**: every R4 task touching playbook JSON, NodeExecutor FetchXml, or any Dataverse query — verify the entity name is `sprk_event` / `sprk_communication` / `sprk_workassignment` / `sprk_matter` / `sprk_project` / `sprk_document` (NOT OOB equivalents). When in doubt, query the deployed playbook config via MCP `read_query` to confirm the actual entity in use.
- **Memory pointer**: `~/.claude/projects/.../memory/spaarke-entity-architecture.md` — project-tier memory persisted across sessions.

---

## Implementation Notes

### Canonical Code Analogs (Direct Templates)

For the new `EntityNameValidator` triplet:
- Executor: model on `src/server/api/Sprk.Bff.Api/Services/Ai/Nodes/LookupUserMembershipNodeExecutor.cs`
- Form: mirror `src/client/code-pages/PlaybookBuilder/src/components/properties/LookupUserMembershipForm.tsx`
- Tests: pattern from `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/Nodes/LookupUserMembershipNodeExecutorTests.cs`

For the W1 customData enrichment:
- Target: `CreateNotificationNodeExecutor.cs` lines 471–546 (`BuildNotificationEntity` method)
- Test model: `CreateNotificationNodeExecutorTests.cs`

For the W2 widget changes:
- Reference handler pattern: `DailyBriefingApp.tsx` lines 238–287 (`handleAddToTodo`)
- Visual reference for three-dot menu: semantic-search PCF list component (user-cited)
- Fluent v9 primitives: `Menu`, `MenuTrigger`, `MenuList`, `MenuItem`, `MoreHorizontalRegular` icon

### Playbook JSON Source-of-Truth

The 7 canonical JSON files live at `projects/spaarke-daily-update-service/notes/playbooks/`:
- `notification-new-documents.json` (PB-016 reference example — already membership-aware)
- `notification-new-emails.json` (PB-018)
- `notification-new-events.json` (PB-019)
- `notification-tasks-due-soon.json` (PB-020 — needs W1 migration to membership-scope)
- `notification-tasks-overdue.json` (PB-021 — needs W1 migration)
- `notification-matter-activity.json` (PB-017 — stub, W1 implements)
- `notification-work-assignments.json` (PB-022 — stub, W1 implements)

### `sprk_playbookconsumer` Dispatch Investigation (FR-12 / Task 030)

The `sprk_playbookconsumer` entity + service was built in `work/spaarke-ai-platform-chat-routing-redesign-r1` Phase 1R (entity is present and used in `WorkspaceFileEndpoints` routing). R4 task 030 evaluates whether the daily-briefing widget can be registered as a consumer dispatching to `DAILY-BRIEFING-NARRATE`. Fallback path: direct `AnalysisOrchestrationService` invocation with degenerate playbook. Decision + rationale captured in task 030 notes per AC-12c.

### R3 Deliverables to Preserve (DO NOT MODIFY)

- `sprk_briefingstate` Choice column on `appnotification`
- `ttlinseconds = 604800` at `CreateNotificationNodeExecutor.cs:490`
- Read-state derivation logic in widget (uses `sprk_briefingstate`)
- 3 R3 action functions (Check, Remove, Keep) — preserved, MOVED INTO overflow menu (visual presentation changes; behavior unchanged)

---

## Resources

### Applicable ADRs
- ADR-001 Minimal API
- ADR-010 DI Minimalism
- ADR-013 BFF AI Architecture
- ADR-021 Fluent v9 Design System
- ADR-024 sprk_todo Polymorphic Resolver
- ADR-027 Subscription Isolation
- ADR-028 Spaarke Auth v2
- ADR-029 BFF Publish Hygiene
- ADR-034 User-record Membership Resolution
- ADR-037 Multinode Output Composition

### Skills (auto-loaded by task-execute based on task tags)
- W0: `jps-action-create`, `jps-playbook-design`, `jps-playbook-audit`, `jps-validate`, `jps-scope-refresh`, `dataverse-deploy`, `dataverse-mcp-usage`
- W1: `bff-deploy`, `dataverse-deploy`
- W2: `fluent-v9-component`, `bff-deploy`, `ui-test`, `code-page-deploy`
- All phases: `code-review`, `adr-check`, `conflict-check`, `merge-to-master`

### Patterns
- `.claude/patterns/ai/node-executor-authoring.md`
- `.claude/patterns/ui/fluent-v9-component-authoring.md`
- `.claude/patterns/api/endpoint-definition.md`
- `.claude/patterns/testing/unit-test-structure.md`
- `.claude/patterns/dataverse/polymorphic-resolver.md`

### Constraints (binding)
- `.claude/constraints/bff-extensions.md` — §A checklist + §F test obligation + §F.1/F.2/F.3 asymmetric-registration
- `.claude/constraints/azure-deployment.md` — publish-size + CVE baseline
- `.claude/constraints/ai.md` — AI feature governance
- `.claude/constraints/testing.md` — xUnit + Jest expectations

### Guides
- `docs/guides/JPS-AUTHORING-GUIDE.md`
- `docs/guides/AI-DEPLOYMENT-GUIDE.md`
- `docs/guides/AI-MONITORING-DASHBOARD.md`
- `docs/standards/DATA-ACCESS-DECISION-CRITERIA.md`

### Related Projects
- [`projects/spaarke-daily-update-service/`](../spaarke-daily-update-service/) — R1/R2 base (producer + widget + 7 playbooks)
- [`projects/spaarke-daily-update-service-r2/`](../spaarke-daily-update-service-r2/) — R2 widget framework migration
- [`projects/spaarke-daily-update-service-r3/`](../spaarke-daily-update-service-r3/) — R3 read-state + TTL + 3 actions (PR #451 OPEN; merges independently)
- [`projects/spaarke-platform-foundations-r3/`](../spaarke-platform-foundations-r3/) — `MembershipResolverService` + `LookupUserMembershipNodeExecutor` (shipped; R4 deploys missing Action row)
- [`projects/spaarke-ai-platform-chat-routing-redesign-r1/`](../spaarke-ai-platform-chat-routing-redesign-r1/) — `sprk_playbookconsumer` infrastructure (R4 task 030 evaluates extension)

### External Documentation
- Azure OpenAI (existing `IOpenAiClient` wiring) — no new deployment requirements
- Microsoft `appnotification` OOB entity — `customData` Memo column (1MB cap; R4 stays <10KB)
- Fluent UI v9 [`Menu`](https://react.fluentui.dev/?path=/docs/components-menu--default) docs for overflow-menu pattern

---

*This file should be kept updated throughout project lifecycle.*
