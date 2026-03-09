# Matter Performance Assessment & KPI R1 - AI Context

> **Purpose**: This file provides context for Claude Code when working on matter-performance-KPI-r1.
> **Always load this file first** when working on any task in this project.

---

## 🎯 Project Focus: R1 MVP

**This project is R1 MVP scope** - Manual KPI entry + basic visualization (22-29 tasks, 3-4 days).

**NOT building in R1**:
- ❌ Assessment generation infrastructure
- ❌ System-calculated inputs (from invoices)
- ❌ AI integration
- ❌ Organization/person rollups
- ❌ Scheduled batch processing
- ❌ Outlook adaptive cards
- ❌ Dataverse plugins

**Full solution** (R2+) documented in `spec-full.md` and `plan-full.md` for future reference.

---

## Project Status

- **Version**: R1 MVP
- **Phase**: Planning
- **Last Updated**: 2026-02-12
- **Current Task**: Task generation in progress (7/27 complete)
- **Next Action**: Complete task generation for remaining 20 tasks (011-090)

---

## Quick Reference

### Key Files

**R1 MVP (Current)**:
- [`spec-r1.md`](spec-r1.md) - R1 MVP specification (manual entry + visualization)
- [`plan-r1.md`](plan-r1.md) - R1 implementation plan (22-29 tasks, 3-4 days)
- [`README.md`](README.md) - Project overview with R1 graduation criteria
- [`current-task.md`](current-task.md) - **Active task state** (for context recovery)
- [`tasks/TASK-INDEX.md`](tasks/TASK-INDEX.md) - Task tracker (will be created by task-create)

**Full Solution (Future Reference)**:
- [`spec-full.md`](spec-full.md) - Full solution specification (archived, 5,410 words)
- [`plan-full.md`](plan-full.md) - Full solution plan (archived, 5 phases)
- [`performance-assessment-design.md`](performance-assessment-design.md) - Original design document

### Project Metadata
- **Project Name**: matter-performance-KPI-r1
- **Version**: R1 MVP
- **Type**: Dataverse + API + VisualHost (simplified)
- **Complexity**: Low (R1 MVP)
  - 1 new entity: KPI Assessment
  - 6 fields on Matter (current + average grades)
  - 1 API endpoint: Calculator
  - JavaScript web resource trigger (no plugins)
  - Manual entry only (no automation)

---

## Context Loading Rules

When working on this project, Claude Code should:

1. **Always load this file first** when starting work on any task
2. **Check current-task.md** for active work state (especially after compaction/new session)
3. **Reference spec.md** for design decisions, requirements (42 functional, 10 non-functional), and acceptance criteria
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
- ❌ Missing ADR constraints
- ❌ No checkpointing - lost progress after compaction
- ❌ Skipped quality gates

### Parallel Task Execution

When tasks can run in parallel (no dependencies), each task MUST still use task-execute:
- Send one message with multiple Skill tool invocations
- Each invocation calls task-execute with a different task file
- Example: Tasks 020, 021, 022 in parallel → Three separate task-execute calls in one message

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
   - Files are in different modules → CAN parallelize
   - Files have no shared interfaces → CAN parallelize
   - Work is independent (no imports between files) → CAN parallelize

4. **Serialize when**:
   - Files have tight coupling (shared state, imports)
   - One file must be created before another uses it
   - Sequential logic required

**Example**: Task modifies 8 files (4 API endpoints + 2 services + 2 job handlers)
- Phase 1 (serial): Shared interfaces/contracts
- Phase 2 (parallel): 3 subagents handle endpoint group A, endpoint group B, services + handlers

See [task-execute SKILL.md Step 8.0](../../.claude/skills/task-execute/SKILL.md) for complete protocol.

---

## Key Technical Constraints (R1 MVP)

**From ADRs (Applicable to R1)**:
- **ADR-001**: MUST use Minimal API pattern for calculator endpoint (no Azure Functions)
- **ADR-008**: MUST use endpoint filters for authorization (not global middleware)
- **ADR-019**: MUST return ProblemDetails for API errors
- **ADR-021**: MUST use Fluent UI v9 exclusively (no hard-coded colors, dark mode required)

**From Spec-R1 (Key Rules)**:
- **No plugins**: Use JavaScript web resource trigger (OnSave event calls API)
- **No Power Automate**: Use web resource trigger
- **Manual entry only**: Quick Create form, no automation
- **Dual grades**: Current (latest) + Historical average (mean of all)
- **Trend graph**: Last 5 updates (not time-based)
- **Trend direction**: Linear regression (↑ ↓ →)
- **Color coding**: Blue (A-B: 0.85-1.00), Yellow (C: 0.70-0.84), Red (D-F: 0.00-0.69)
- **Contextual text**: "You have an X% in [Area] compliance"

**ADRs NOT Applicable to R1**:
- ❌ ADR-002 (thin plugins) - R1 has no plugins
- ❌ ADR-004 (job contracts) - R1 has no background jobs
- ❌ ADR-009 (Redis caching) - Optional for R1, can add if needed
- ❌ ADR-010 (DI minimalism) - R1 has 1 service (not 4)
- ❌ ADR-013 (AI architecture) - R1 has no AI integration

---

## R1 MVP Simplifications

**What R1 Removes** (compared to full solution):
1. **No Assessment Infrastructure**: Manual entry via Quick Create, no triggers/generation
2. **No System-Calculated Inputs**: User enters all grades manually
3. **No AI Integration**: No playbook, no AI-derived inputs
4. **No Rollups**: Just matter-level grades (no org/person aggregation)
5. **No Scheduled Jobs**: Immediate calculation on save (no batch processing)
6. **No Plugins**: JavaScript web resource trigger instead
7. **No Power Automate**: Direct API call from web resource

---

## Decisions Made (R1 MVP)

<!-- Log key architectural/implementation decisions here as project progresses -->
<!-- Format: Date, Decision, Rationale, Who -->

| Date | Decision | Rationale | Who |
|------|----------|-----------|-----|
| 2026-02-12 | R1 MVP Scope | Manual entry + visualization only, defer automation to R2+ | Planning |
| 2026-02-12 | Web Resource Trigger | No plugins/Power Automate available → JavaScript OnSave event | Planning |
| 2026-02-12 | Dual Grades (Current + Average) | Show both latest and historical performance | Planning |
| 2026-02-12 | Last 5 Updates for Trend | Time-agnostic, consistent data points for sparkline | Planning |
| 2026-02-12 | Linear Regression for Trend | Simple, interpretable trend direction (↑ ↓ →) | Planning |
| 2026-02-12 | Quick Create Form | Standard Dataverse form, no custom PCF panel for R1 | Planning |

*Add new decisions as implementation progresses*

---

## Implementation Notes (R1 MVP)

<!-- Add notes about gotchas, workarounds, or important learnings during implementation -->

### Data Model (R1)
- **New Entity**: `sprk_kpiassessment` with 6 fields (matter, area, KPI name, criteria, grade, notes)
- **Matter Extension**: 6 decimal fields for grades (current + average × 3 areas)
- **Choice Fields**: Performance Area (3 options), Grade (10 options with decimal values)

### Calculator Logic (R1)
- **Current Grade**: Query `SELECT TOP 1 sprk_grade WHERE sprk_performancearea = X ORDER BY createdon DESC`
- **Historical Average**: Query `SELECT AVG(sprk_grade) WHERE sprk_performancearea = X`
- **Trend Data**: Query last 5 assessments for sparkline + linear regression

### Trigger Mechanism (R1)
- **Web Resource**: `sprk_kpiassessment_quickcreate.js` on Quick Create form
- **Event**: `addOnPostSave()` ensures save completes before API call
- **API Call**: `fetch('/api/matters/{matterId}/recalculate-grades', { method: 'POST' })`
- **Parent Refresh**: `window.parent.Xrm.Page.data.refresh(false)` to show updated grades

### VisualHost Cards (R1)
- **Report Card Metric Card**: May need new card type or extend existing (research task in Phase 5)
- **Color Coding**: Blue (0.85-1.00), Yellow (0.70-0.84), Red (0.00-0.69)
- **Contextual Text**: Template substitution: "You have an {grade}% in {area} compliance"

*Add new notes as implementation progresses*

---

## Resources

### Applicable ADRs
- [ADR-001: Minimal API + BackgroundService](../../.claude/adr/ADR-001-minimal-api.md)
- [ADR-002: Thin Dataverse Plugins](../../.claude/adr/ADR-002-thin-plugins.md)
- [ADR-004: Async Job Contract](../../.claude/adr/ADR-004-job-contract.md)
- [ADR-008: Endpoint Filters for Authorization](../../.claude/adr/ADR-008-endpoint-filters.md)
- [ADR-009: Redis-First Caching](../../.claude/adr/ADR-009-redis-caching.md)
- [ADR-010: DI Minimalism](../../.claude/adr/ADR-010-di-minimalism.md)
- [ADR-011: Dataset PCF Controls](../../.claude/adr/ADR-011-dataset-pcf.md)
- [ADR-012: Shared Component Library](../../.claude/adr/ADR-012-shared-components.md)
- [ADR-013: AI Architecture](../../.claude/adr/ADR-013-ai-architecture.md)
- [ADR-017: Job Status Pattern](../../.claude/adr/ADR-017-job-status.md)
- [ADR-019: ProblemDetails Error Responses](../../.claude/adr/ADR-019-problemdetails.md)
- [ADR-021: Fluent UI v9 Design System](../../.claude/adr/ADR-021-fluent-design-system.md)

### Patterns
- [Endpoint Definition Pattern](../../.claude/patterns/api/endpoint-definition.md)
- [Background Workers Pattern](../../.claude/patterns/api/background-workers.md)
- [Plugin Structure Pattern](../../.claude/patterns/dataverse/plugin-structure.md)
- [Distributed Cache Pattern](../../.claude/patterns/caching/distributed-cache.md)

### Related Projects
- Financial Intelligence R1 (provides `sprk_invoice` entity)
- AI Playbook Infrastructure (provides `AnalysisOrchestrationService`)

### External Documentation
- [Microsoft Graph Actionable Messages](https://learn.microsoft.com/en-us/outlook/actionable-messages/)
- [Azure OpenAI Service](https://learn.microsoft.com/en-us/azure/ai-services/openai/)
- [Azure Service Bus](https://learn.microsoft.com/en-us/azure/service-bus-messaging/)
- [Azure Cache for Redis](https://learn.microsoft.com/en-us/azure/azure-cache-for-redis/)

---

*This file should be kept updated throughout project lifecycle. All team members working on this project should review this file before starting work.*
