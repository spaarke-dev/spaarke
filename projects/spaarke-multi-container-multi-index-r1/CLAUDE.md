# Spaarke Multi-Container Multi-Index Routing — AI Context

> **Purpose**: This file provides context for Claude Code when working on `spaarke-multi-container-multi-index-r1`.
> **Always load this file first** when working on any task in this project.

---

## Project Status

- **Phase**: Planning (Ready for Tasks)
- **Last Updated**: 2026-06-07
- **Current Task**: Not started
- **Next Action**: Run task-execute on `tasks/001-*.poml` (Phase A.5 — operator BU value setup)

---

## Quick Reference

### Key Files
- [`spec.md`](spec.md) — AI-optimized specification (4,293 words, FR/NFR enumerated)
- [`design.md`](design.md) — Original design (6,325 words, 4 review rounds, single source of truth for invariants INV-1..INV-8)
- [`README.md`](README.md) — Project overview + graduation criteria
- [`plan.md`](plan.md) — Implementation plan + WBS (8 phases)
- [`current-task.md`](current-task.md) — **Active task state** (for context recovery)
- [`tasks/TASK-INDEX.md`](tasks/TASK-INDEX.md) — Task tracker with parallel groups (created by task-create)

### Project Metadata
- **Project Name**: spaarke-multi-container-multi-index-r1
- **Type**: Multi-component (BFF API + PCF + Code Page + Wizards + PowerShell backfill + Operator runbook)
- **Complexity**: Medium-High (8 phases, ~45 tasks, strict deploy ordering)
- **Branch**: `work/spaarke-multi-container-multi-index-r1` (succeeds PR #363 / v1.1.73)

---

## Context Loading Rules

When working on this project, Claude Code should:

1. **Always load this file first** when starting work on any task
2. **Check current-task.md** for active work state (especially after compaction/new session)
3. **Reference spec.md and design.md** for design decisions, requirements, and invariants — design.md is the single source of truth for INV-1..INV-8
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

**Implementation**: When user triggers task work, invoke Skill tool with `skill="task-execute"` and task file path.

### Why This Matters

The task-execute skill ensures:
- ✅ Knowledge files are loaded (ADRs, constraints, patterns)
- ✅ Context is properly tracked in current-task.md
- ✅ Proactive checkpointing occurs every 3 steps
- ✅ Quality gates run (code-review + adr-check) at Step 9.5
- ✅ Progress is recoverable after compaction

**Bypassing this skill leads to**:
- ❌ Missing ADR constraints (this project depends on 12 ADRs)
- ❌ No checkpointing — lost progress after compaction
- ❌ Skipped quality gates
- ❌ INV-1..INV-8 violations (this project has 8 binding invariants)

### Parallel Task Execution

When tasks can run in parallel (marked `parallel-safe: true` in TASK-INDEX.md), each task MUST still use task-execute:
- Send one message with multiple Skill tool invocations
- Each invocation calls task-execute with a different task file
- Example: Tasks 010, 011, 012 (wizards on independent code-page projects, after shared-lib lands) → Three separate task-execute calls in one message

See [task-execute SKILL.md](../../.claude/skills/task-execute/SKILL.md) for complete protocol.

### 🚨 MUST: Multi-File Work Decomposition

**For tasks modifying 4+ files, Claude Code MUST:**

1. **Decompose into dependency graph**:
   - Group files by module/component
   - Identify which changes depend on others
   - Separate parallel-safe work from sequential work

2. **Delegate to subagents in parallel where safe**:
   - Use Task tool with `subagent_type="general-purpose"`
   - Send ONE message with MULTIPLE Task tool calls for independent work
   - Each subagent handles one module/component
   - Provide each subagent with specific files and constraints

3. **Parallelize when**:
   - Files are in different modules → CAN parallelize (e.g., 5 independent wizard code-pages)
   - Files have no shared interfaces → CAN parallelize
   - Work is independent (no imports between files) → CAN parallelize

4. **Serialize when**:
   - Files have tight coupling (shared state, imports) — e.g., `Spaarke.UI.Components/src/services/EntityCreationService.ts` is the contract; wizards depend on it
   - One file must be created before another uses it
   - Sequential logic required (e.g., BFF interface change BEFORE impl change BEFORE thread-through)

**Example for this project**: Phase A wizards = shared-lib change first (serial), then 5 wizards in parallel after shared-lib lands.

See [task-execute SKILL.md Step 8.0](../../.claude/skills/task-execute/SKILL.md) for complete protocol.

---

## Key Technical Constraints

**From spec.md + ADRs** (binding):

- **MUST use `@spaarke/auth.authenticatedFetch`** for all BFF calls (ADR-028) — no `Bearer ${token}` literals, no `new PublicClientApplication`
- **MUST use Fluent UI v9 + `tokens.*`** for all styling (ADR-021) — no hex, no `var(--…)`, no rgb literals
- **MUST use ProblemDetails** for BFF error responses (ADR-019) — `400 INDEX_NOT_ALLOWED`
- **MUST follow existing wizard patterns** — extension, not rewrite (`matterService.ts:216` is canonical reference)
- **MUST validate `searchIndexName`** against `appsettings.AiSearch.AllowedIndexes` before resolving SearchClient
- **MUST never overwrite** explicit field values during backfill (INV-5)
- **MUST halt loud** on unmapped container during backfill (no silent default)
- **MUST follow PCF v1.1.74 5-location version bump** per `/pcf-deploy` skill
- **MUST clean-rebuild** `@spaarke/auth` + `@spaarke/ui-components` `dist/` BEFORE PCF + code-page builds (saved lesson `feedback_stale-shared-lib-dist-poisons-codepage-bundle`)
- **MUST cap PCF bundle at ≤ 1 MB** in production mode
- **MUST verify BFF publish size ≤ 60 MB** compressed after Phase B (CLAUDE.md §10 bullet 4; baseline ~45.65 MB)

**MUST NOT**:
- ❌ Introduce Dataverse plugins
- ❌ Introduce Power Automate flows
- ❌ Modify existing OOB Dataverse attributemaps (security_bu + name remain untouched)
- ❌ Extend `sprk_fieldmappingprofile` / create new profile records
- ❌ Populate `sprk_containerid` on `sprk_document` (canonical field is `sprk_graphdriveid`)
- ❌ Add container filter to BFF search OData (multiple containers can share one index)
- ❌ Introduce BU-change auto-sync (INV-3 — coexistence is the design)
- ❌ Use `@fluentui/react` v8
- ❌ Use React 18-only APIs in PCF code (ADR-022: PCF stays React 16)

---

## Decisions Made

<!-- Log key architectural/implementation decisions here as project progresses -->
<!-- Format: Date, Decision, Rationale, Who -->

- **2026-06-07**: BFF allow-list lives in static appsettings (not dynamic config entity) — tighter blast radius; ops simpler. Per spec.md §Owner Clarifications row 2.
- **2026-06-07**: Backfill derives container from evidence (mode of child docs' `sprk_graphdriveid`), not from BU — BU has been changed. Per spec.md §Owner Clarifications row 10.
- **2026-06-07**: Document container field is `sprk_graphdriveid` only — `sprk_containerid` stays NULL on documents. Per spec.md §Owner Clarifications row 12.
- **2026-06-07**: PCF v1.1.74 strictly succeeds PR #363 v1.1.73; sequential PRs (not parallel branches on PCF) — spec.md §Implementation Notes.

---

## Implementation Notes

<!-- Add notes about gotchas, workarounds, or important learnings during implementation -->

- **Phase A.5 is a prerequisite, not a phase to defer** — BU values must be set BEFORE Phase A wizards deploy, or wizards inherit empty strings.
- **PR #363 must land before this project's PR** — both touch SemanticSearchControl PCF. Plan to rebase post-#363-merge.
- **Backfill scripts halt loud on unmapped container** — operator must extend §5.1 map in script when a third SPE container appears. Documented in Phase G operator runbook.
- **No filter-parity drift across phases** — Phase D.1 must remain folded into D; the envelope sender (PCF) and parser (code page) are paired.

---

## Resources

### Applicable ADRs

| ADR | Relevance |
|---|---|
| ADR-001 | Minimal API pattern (BFF endpoint changes) |
| ADR-006 | PCF over webresources; Code Page as full-page Power Apps surface |
| ADR-008 | Endpoint-filter authorization (existing filter unchanged) |
| ADR-010 | DI minimalism (BFF resolver registration) |
| ADR-012 | Shared component library `@spaarke/ui-components` (wizards live there) |
| ADR-013 | AI architecture / semantic search |
| ADR-019 | ProblemDetails on BFF errors (`400 INDEX_NOT_ALLOWED`) |
| ADR-021 | Fluent UI v9 + dark mode + tokens (PCF + Code Page) |
| ADR-022 | React version boundaries (PCF: 16, Code Page: 18) |
| ADR-026 | Full-page Code Page standard (`sprk_semanticsearch`) |
| ADR-028 | Spaarke Auth v2 (`@spaarke/auth.authenticatedFetch`, `initAuth`, `useAuth`) |
| ADR-029 | BFF publish hygiene (framework-dependent, transitive CVE override, size baseline) |

### Discovered Constraints + Patterns

- `.claude/constraints/bff-extensions.md` — **binding** pre-merge checklist for Phase B (BFF additions)
- `.claude/constraints/api.md` — endpoint definition conventions
- `.claude/constraints/pcf.md` — PCF coding constraints
- `.claude/constraints/auth.md` — Spaarke Auth v2 client contract
- `.claude/patterns/pcf/control-initialization.md` — PCF init lifecycle
- `.claude/patterns/pcf/theme-management.md` — ADR-021 dark-mode token usage
- `.claude/patterns/api/endpoint-definition.md` — Minimal-API pattern
- `.claude/patterns/auth/spaarke-sso-binding.md` — Spaarke Auth v2 canonical binding

### Codebase Patterns to Follow

- Wizard sets container — [`matterService.ts:216`](../../src/client/shared/Spaarke.UI.Components/src/components/CreateMatterWizard/matterService.ts#L216)
- Container resolution chain — [`AssociateToStep.tsx:147-163`](../../src/solutions/DocumentUploadWizard/src/components/AssociateToStep.tsx#L147-L163)
- Document payload — [`DocumentRecordService.ts:268-293`](../../src/client/shared/Spaarke.UI.Components/src/services/document-upload/DocumentRecordService.ts#L268-L293)
- BFF index resolver — [`IKnowledgeDeploymentService.cs`](../../src/server/api/Sprk.Bff.Api/Services/Ai/IKnowledgeDeploymentService.cs)
- BFF OData filter (no container clause exists) — [`SearchFilterBuilder.cs`](../../src/server/api/Sprk.Bff.Api/Services/Ai/SemanticSearch/SearchFilterBuilder.cs)

### Skills Used by This Project

- `task-execute` — mandatory for every task
- `adr-aware` — auto-loads ADR constraints
- `pcf-deploy` — Phase D
- `code-page-deploy` — Phase E
- `bff-deploy` — Phase B
- `dataverse-mcp-usage` — Phase A.5 + all UAT MCP verification
- `ui-test` — Phase D + E UI tests
- `code-review` — quality gate at task Step 9.5
- `adr-check` — quality gate at task Step 9.5

### Saved Lessons

- `feedback_stale-shared-lib-dist-poisons-codepage-bundle` — mandatory clean rebuild of `@spaarke/auth` + `@spaarke/ui-components` `dist/` BEFORE PCF + code-page builds (NFR-10/11)
- `feedback_deploy-asks-follow-skill-no-openended-questions` — invoke deploy skill verbatim; no open-ended questions during deploy

### Related Projects

- PR #363 (`work/semantic-search-pcf-ui-tweaks-2026-06-05`) — PCF v1.1.73 baseline; this project succeeds it as v1.1.74

### External Documentation

- Azure AI Search REST API (for resolver URL formation in Phase B)
- SharePoint Embedded container IDs (already provisioned per spec §Dependencies)

---

*This file should be kept updated throughout project lifecycle*
