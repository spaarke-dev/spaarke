# Spaarke Compose (R1) — AI Context

> **Purpose**: This file provides context for Claude Code when working on `spaarkeai-compose-r1`.
> **Always load this file first** when working on any task in this project.

---

## Project Status

- **Phase**: Phase 0 (Spikes) — ready to start
- **Last Updated**: 2026-06-29
- **Current Task**: Not started — 37 tasks generated in `tasks/`; Wave 0 (parallel 4-agent dispatch of Spikes #1–#4) is the entry point
- **Next Action**: Say `"work on task 001"` (or `"continue"` to start Wave 0). All task execution MUST go through `task-execute` skill per CLAUDE.md §4.

---

## Quick Reference

### Key Files
- [`spec.md`](spec.md) — AI implementation specification (authoritative; FRs/NFRs/success criteria)
- [`design.md`](design.md) — working design (full vision + R1 scope narrowing + resolved decisions + spike plan)
- [`Spaarke-AI-Document-Workspace-Solution-Concept.md`](Spaarke-AI-Document-Workspace-Solution-Concept.md) — original concept (preserved)
- [`README.md`](README.md) — project overview + graduation criteria
- [`plan.md`](plan.md) — implementation plan + WBS + phase dependencies
- [`current-task.md`](current-task.md) — **Active task state** (for context recovery)
- [`tasks/TASK-INDEX.md`](tasks/TASK-INDEX.md) — task tracker (created by `task-create`)

### Project Metadata
- **Project Name**: spaarkeai-compose-r1
- **Type**: BFF endpoints + SpaarkeAi UI surface + Dataverse rows + JPS scopes + shared library
- **Complexity**: High (cross-cutting; 8 phases; spike-gated; touches BFF + SpaarkeAi hot paths)
- **Hot-path declaration**: BFF=Y · SpaarkeAi=Y · ci-workflows=N · skill-directives=N · root-CLAUDE.md=N

---

## Context Loading Rules

When working on this project, Claude Code should:

1. **Always load this file first** when starting work on any task
2. **Check `current-task.md`** for active work state (especially after compaction / new session)
3. **Reference `spec.md`** for design decisions, requirements, and acceptance criteria
4. **Reference `design.md` §13** for the spike plan and §14 for resolved decisions
5. **Load the relevant task file** from `tasks/` based on current work
6. **Apply ADRs** relevant to the technologies used (loaded automatically via `adr-aware`)
7. **For any BFF-touching task: load [`.claude/constraints/bff-extensions.md`](../../.claude/constraints/bff-extensions.md) BEFORE designing the addition** (per root CLAUDE.md §10)

**Context Recovery**: If resuming work, see [Context Recovery Protocol](../../docs/procedures/context-recovery.md)

---

## 🚨 MANDATORY: Task Execution Protocol

**ABSOLUTE RULE**: All task work MUST use the `task-execute` skill. DO NOT read POML files directly and implement manually.

### Auto-Detection Rules (Trigger Phrases)

When you detect these phrases from the user, invoke `task-execute` skill:

| User Says | Required Action |
|-----------|-----------------|
| "work on task X" | Execute task X via task-execute |
| "continue" | Execute next pending task (check TASK-INDEX.md for next 🔲) |
| "continue with task X" | Execute task X via task-execute |
| "next task" | Execute next pending task via task-execute |
| "keep going" | Execute next pending task via task-execute |
| "resume task X" | Execute task X via task-execute |
| "pick up where we left off" | Load current-task.md, invoke task-execute |

**Implementation**: When user triggers task work, invoke `Skill` tool with `skill="task-execute"` and task file path.

### Why This Matters

The `task-execute` skill ensures:
- ✅ Knowledge files are loaded (ADRs, constraints, patterns)
- ✅ Context is properly tracked in `current-task.md`
- ✅ Proactive checkpointing occurs every 3 steps
- ✅ Quality gates run (`code-review` + `adr-check`) at Step 9.5
- ✅ Progress is recoverable after compaction
- ✅ Tests are added/updated per CLAUDE.md §10 #6 obligation

**Bypassing this skill leads to**: missing ADR constraints, lost progress after compaction, skipped quality gates, manual errors.

### Parallel Task Execution

When tasks can run in parallel (no dependencies between them), each task MUST still use `task-execute`. Pattern: ONE message with MULTIPLE Skill tool invocations (one per task). Sequential invocations waste parallelism.

See [task-execute SKILL.md](../../.claude/skills/task-execute/SKILL.md) for the complete protocol.

### 🚨 MUST: Multi-File Work Decomposition

**For tasks modifying 4+ files, Claude Code MUST**:

1. **Decompose into dependency graph**: group files by module/component; identify dependencies; separate parallel-safe vs sequential work
2. **Delegate to subagents in parallel where safe**: `Agent` with `subagent_type="general-purpose"`; ONE message with MULTIPLE Agent calls
3. **Parallelize when**: files in different modules · no shared interfaces · no imports between files
4. **Serialize when**: tight coupling (shared state, imports) · sequential creation dependency · ordered logic required
5. **Note BFF hot-path coordination**: tasks touching `.claude/` paths MUST be sequential (sub-agent write boundary per root CLAUDE.md §3)

**Example for this project (Phase 2 — BFF endpoints + services, 4+ files)**:
- Phase 1 serial: `ConsumerTypes.cs` (additive constant, no risk)
- Phase 2 parallel: `ComposeService.cs`, `ComposeDocumentService.cs`, `ComposeSessionService.cs` (independent modules)
- Phase 3 serial: `ComposeEndpoints.cs` (consumes the 3 services)
- Phase 4 serial: `Program.cs` DI registration (depends on all)

See [task-execute SKILL.md Step 8.0](../../.claude/skills/task-execute/SKILL.md) for complete protocol.

---

## Key Technical Constraints

Extracted from `spec.md` + applicable ADRs + CLAUDE.md §10/§11:

### MUST Rules

- ✅ MUST use minimal API pattern (`MapGroup` + `MapGet`/`MapPost` + endpoint filters) — **ADR-001**
- ✅ MUST require authorization on every Compose endpoint (`RequireAuthorization()`) — **ADR-008, ADR-028**
- ✅ MUST inject AI capabilities via `Services/Ai/PublicContracts/` facade — NOT direct AI internal types — **refined ADR-013 (2026-05-20)**
- ✅ MUST reuse existing `ChatSession` + Redis/Cosmos/Dataverse three-tier; NOT create new session entity
- ✅ MUST reuse existing `IConsumerRoutingService` + `IInvokePlaybookAi` for AI dispatch
- ✅ MUST reuse existing `GET /api/documents/{id}/open-links` for Word handoff — NOT create new endpoint
- ✅ MUST extract `useDocumentActions` to shared lib BEFORE Compose consumes it
- ✅ MUST measure publish-size impact on every BFF-touching task (per root CLAUDE.md §10 #4)
- ✅ MUST add unit tests for every new BFF service (per root CLAUDE.md §10 #6)
- ✅ MUST follow ADR-038 testing rules (integration-heavy; 6 KEEP categories; mock-boundary)
- ✅ MUST group Compose endpoints under `/api/compose/` route prefix — **ADR-019**
- ✅ MUST make new Dataverse rows org-owned — **ADR-010**

### MUST NOT Rules

- ❌ MUST NOT extend `sprk_analysis.sprk_chathistory` (superseded; use `ChatSession` model)
- ❌ MUST NOT inject `IOpenAiClient`, `IPlaybookService`, or other AI internals into Compose CRUD code
- ❌ MUST NOT build custom integrations for advanced DOCX features outside TipTap OOB (tracked changes, footnotes, fields, equations, SmartArt)
- ❌ MUST NOT store comments as Word `<w:comment>` elements in R1 (use ChatSession annotations in R2+)
- ❌ MUST NOT support tracked-changes round-trip with Word (out of architecture)
- ❌ MUST NOT extend `HostContext` in R1 (transient state → JPS scope inputs)
- ❌ MUST NOT use banned test patterns: `Mock<HttpMessageHandler>`, DI-registration tests, ctor null-check tests — **ADR-038**

### Hot-Path Coordination

- **BFF** touch: 14 other active projects in `projects/INDEX.md` touch BFF — coordinate via [`.claude/constraints/bff-extensions.md`](../../.claude/constraints/bff-extensions.md) §§ F.1–F.3, G
- **SpaarkeAi** touch: 8 other active projects touch SpaarkeAi — align with workspace-layout pattern (Calendar precedent)
- Hot-path declaration registered in [`projects/INDEX.md`](../INDEX.md) at pipeline Step 4

---

## Decisions Made

<!-- Log key architectural/implementation decisions here as project progresses -->
<!-- Format: Date · Decision · Rationale · Who -->

- **2026-06-29 · Locked decisions** (from `design.md` §14):
  - **DOCX subset**: TipTap OOB only — no custom integration; OOB inventory validated in Spike #1
  - **HostContext**: NOT extended in R1 — transient state goes in JPS scope inputs
  - **Path A entry UX**: Modal with full-screen toggle — reuses existing SpaarkeAi pattern
  - **Multi-tab**: Per-user single-session lock via SPE check-out + conflict UX
  - **R1 default-open**: Empty state with Browse + Search options
  - **First consumer type**: `compose-summarize` → existing Document Summary playbook (id `47686eb1-9916-f111-8343-7c1e520aa4df`)
  - **ChatSession reuse**: NO new entity; bind via `DocumentId`
  - **Open-in-Desktop**: required; reuse existing components; extract to `@spaarke/document-operations`
  - **BFF placement**: all R1 endpoints in `Sprk.Bff.Api` (no microservice)
  - **DOCX strategy**: constrained subset defined by TipTap OOB
  - **Co-editing**: NO multi-user co-editing in R1 (CRDT deferred to R5+)
  - **Document promotion**: on first Save (idempotent)

---

## Implementation Notes

<!-- Add notes about gotchas, workarounds, or important learnings during implementation -->

- **Spike-gated**: Phase 0 (Spikes #1–#4) is a blocking gate. Do NOT begin Phase 1+ tasks until spike outputs are locked artifacts in `notes/spikes/`.
- **Shared lib extraction precedence**: Refactor SemanticSearch to consume from `@spaarke/document-operations` FIRST (verify tests green) BEFORE Compose consumes. Avoids dual-source-of-truth risk during the refactor.
- **AI facade enforcement (refined ADR-013, 2026-05-20)**: When wiring `compose-summarize` dispatch, Compose CRUD code MUST inject `IConsumerRoutingService` + `IInvokePlaybookAi` — NEVER `IOpenAiClient`, `IPlaybookService`, or other AI internals. Verify in code-review.
- **ChatSession binding strategy**: `DocumentId` is SPE drive-item id for ephemeral docs (Path B); becomes `sprk_documentid` after first-Save promotion. Idempotency on promotion is critical (multiple Save clicks before/after must not create duplicate Document records).
- **Three-pane data contracts (Spike #2)**: Lock the six TypeScript interfaces FIRST. Receivers can be stubs in R1 — the contract is the deliverable, not the runtime behavior.

---

## Deferrals & Issues — tracking obligation

This project tracks deferred work + newly-discovered issues in TWO places, kept in sync:

1. **`notes/defer-issues.md`** — source of truth (full context, links, traceability)
2. **GitHub Issues** on the portfolio board (visibility — others can see + claim)

### When to file

| Situation | Use |
|---|---|
| Spec scope item dropped to keep this project shippable | DEF-{NNN} |
| Refactor / cleanup > 2hr that's not in current spec | DEF-{NNN} |
| Production / dev bug uncovered outside this project's responsibility | ISS-{NNN} |
| Telemetry / monitoring gap requiring follow-up | ISS-{NNN} |
| Failure mode discovered + worked around (not fixed) | ISS-{NNN} |

### How to file

Invoke `/project-defer-issue-tracking` (alias `/defer`) — it writes to BOTH places in one step.

NEVER add an entry only to `notes/defer-issues.md` and skip the GitHub Issue. The whole point of this protocol is visibility. The `push-to-github` skill scans for entries without GitHub URLs and blocks push until they're filed.

### CLAUDE.md §11 rule applies

Every entry must name a concrete behavior or contract that fails without the work. "For future flexibility" / "improve testability" / "separation of concerns" = NOT a valid reason — the skill refuses to file.

### Status

See `notes/defer-issues.md` for the current rollup. Use `gh issue list --label spaarkeai-compose-r1` for the team-visible view.

---

## ADR Conflict Resolution (CLAUDE.md §6.5)

`spec.md §ADR Tensions` declares **"No ADR tensions surfaced at design time."** Foundational projects like R1 typically have few or no tensions because they reuse existing patterns. **If tensions emerge during implementation**, MUST surface via the three-path resolution protocol — Path A (project-scoped exception), Path B (ADR amendment), or Path C (pivot to comply). Silent ADR violation is forbidden.

Spec section is required to be updated if tensions emerge. See root CLAUDE.md §6.5 for the full protocol + required output format.

---

## Resources

### Applicable ADRs

| ADR | Concise | Full | Relevance |
|-----|---------|------|-----------|
| ADR-001 | [.claude/adr/](../../.claude/adr/) | [docs/adr/](../../docs/adr/) | Minimal API — Compose endpoint pattern |
| ADR-008 | [.claude/adr/](../../.claude/adr/) | [docs/adr/](../../docs/adr/) | Endpoint filters for authorization |
| ADR-010 | [.claude/adr/](../../.claude/adr/) | [docs/adr/](../../docs/adr/) | Org-owned Dataverse default |
| ADR-013 (refined 2026-05-20) | [.claude/adr/](../../.claude/adr/) | [docs/adr/](../../docs/adr/) | BFF AI extraction — PublicContracts facade only |
| ADR-015 | [.claude/adr/](../../.claude/adr/) | [docs/adr/](../../docs/adr/) | Multi-tenant isolation Tier 3 |
| ADR-019 | [.claude/adr/](../../.claude/adr/) | [docs/adr/](../../docs/adr/) | Endpoint conventions (`/api/compose/`) |
| ADR-028 | [.claude/adr/](../../.claude/adr/) | [docs/adr/](../../docs/adr/) | Spaarke Auth v2 |
| ADR-032 | [.claude/adr/](../../.claude/adr/) | [docs/adr/](../../docs/adr/) | BFF Null-Object Kill-Switch (applies if feature-gated; R1 default = no gates) |
| ADR-038 | — | [docs/adr/ADR-038-testing-strategy.md](../../docs/adr/ADR-038-testing-strategy.md) | Testing strategy (standalone) |

### Cross-cutting constraints

- [`.claude/constraints/bff-extensions.md`](../../.claude/constraints/bff-extensions.md) — binding pre-merge checklist for BFF additions; §§ F.1–F.3 + § G hot-path declaration

### Knowledge docs (load when relevant to current task)

- [`docs/architecture/SPAARKEAI-WORKSPACE-ARCHITECTURE.md`](../../docs/architecture/SPAARKEAI-WORKSPACE-ARCHITECTURE.md) — cold-load → widget render pipeline
- [`docs/architecture/SPAARKEAI-DASHBOARD-AND-WIDGET-MODEL.md`](../../docs/architecture/SPAARKEAI-DASHBOARD-AND-WIDGET-MODEL.md) — two-wrapper architecture (authoritative)
- [`docs/architecture/SPAARKEAI-COMPONENT-MODEL.md`](../../docs/architecture/SPAARKEAI-COMPONENT-MODEL.md) — `@spaarke/*` package inventory
- [`docs/architecture/SPAARKEAI-COMPONENTIZATION-AUDIT.md`](../../docs/architecture/SPAARKEAI-COMPONENTIZATION-AUDIT.md) — Calendar Pattern D canonical precedent
- [`docs/architecture/LEGALWORKSPACE-EMBEDDED-MODE-CONTRACT.md`](../../docs/architecture/LEGALWORKSPACE-EMBEDDED-MODE-CONTRACT.md) — 21 testable MUSTs for embedded hosts
- [`docs/guides/BUILD-A-NEW-WORKSPACE-WIDGET.md`](../../docs/guides/BUILD-A-NEW-WORKSPACE-WIDGET.md) — five-archetype decision tree + Calendar worked example
- [`docs/guides/HOW-TO-ADD-A-CONSUMER-ROUTING-TYPE.md`](../../docs/guides/HOW-TO-ADD-A-CONSUMER-ROUTING-TYPE.md) — exact steps to add `compose-summarize`
- [`docs/standards/CHAT-ATTACHMENT-POLICY.md`](../../docs/standards/CHAT-ATTACHMENT-POLICY.md) — applies for Assistant upload path
- [`docs/standards/DATA-ACCESS-DECISION-CRITERIA.md`](../../docs/standards/DATA-ACCESS-DECISION-CRITERIA.md) — `Xrm.WebApi` vs BFF
- [`docs/standards/TEST-ARCHITECTURE.md`](../../docs/standards/TEST-ARCHITECTURE.md) — test pyramid + 6 KEEP categories

### Related Projects

- `spaarke-ai-platform-unification-r6` — handler registry; coordinate consumer routing handler addition
- `spaarke-ai-platform-chat-routing-redesign-r1` — PlaybookDispatcher/CapabilityRouter unification; verify ChatSession contract shape lands first
- `smart-todo-r4` — workspace widget rebuild + Office endpoints; reference for Compose workspace layout pattern + Word handoff convention
- `ai-spaarke-insights-engine-widgets-r1` — Matter Health widget pattern; shell convention reference

### External Documentation

- TipTap (ProseMirror): https://tiptap.dev (StarterKit + standard open-source extensions only; specific version chosen in Spike #1)
- DOCX bridge candidate: `prosemirror-docx` or equivalent open-source converter (chosen in Spike #1)
- Microsoft Graph SDK — SPE container + drive-item operations
- Spaarke Auth v2 — see [`docs/architecture/auth-azure-resources.md`](../../docs/architecture/auth-azure-resources.md)

---

*This file should be kept updated throughout project lifecycle. Last refresh: 2026-06-29 by `/project-pipeline` Step 2.*
