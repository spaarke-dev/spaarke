# Events and Workflow Automation R1 - AI Context

> **Purpose**: This file provides context for Claude Code when working on events-and-workflow-automation-r1.
> **Always load this file first** when working on any task in this project.

---

## Project Status

- **Phase**: Planning
- **Last Updated**: 2026-02-01
- **Current Task**: Not started
- **Next Action**: Run task-create to decompose plan into task files

---

## Quick Reference

### Key Files
- [`spec.md`](spec.md) - AI-optimized specification (permanent reference)
- [`design.md`](design.md) - Original human design document
- [`README.md`](README.md) - Project overview and graduation criteria
- [`plan.md`](plan.md) - Implementation plan and WBS
- [`current-task.md`](current-task.md) - **Active task state** (for context recovery)
- [`tasks/TASK-INDEX.md`](tasks/TASK-INDEX.md) - Task tracker (will be created by task-create)

### Project Metadata
- **Project Name**: events-and-workflow-automation-r1
- **Type**: PCF + API + Dataverse
- **Complexity**: High (5 PCF controls, 2 API groups, 5 tables)

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

**Example**: Task modifies 6 files (3 API endpoints + 2 PCF components + 1 shared types)
- Phase 1 (serial): SharedTypes.ts (dependency of others)
- Phase 2 (parallel): 3 subagents handle API endpoints, Component A, Component B

See [task-execute SKILL.md Step 8.0](../../.claude/skills/task-execute/SKILL.md) for complete protocol.

---

## Key Technical Constraints

### From ADRs (MUST comply):

- **ADR-001**: Must use Minimal API pattern, no Azure Functions
- **ADR-006**: Must use PCF controls for all custom UI, no legacy webresources
- **ADR-008**: Must use endpoint filters for authorization (not global middleware)
- **ADR-010**: DI minimalism (≤15 non-framework registrations)
- **ADR-012**: Must use shared component library (`@spaarke/ui-components`)
- **ADR-019**: Must use ProblemDetails for error responses
- **ADR-021**: Must use Fluent UI v9, dark mode required, design tokens only
- **ADR-022**: Must use React 16 APIs (`ReactDOM.render`), platform-provided libraries

### From Spec (owner clarifications):

- **No Dataverse Business Rules** - all validation in code (PCF/API)
- **Event Log**: State transitions only (create, complete, cancel, delete)
- **Field Mapping**: Strict mode only for R1 (Resolve mode future)
- **Sync Modes**: One-time, Manual Refresh (pull), Update Related (push)

---

## Project-Specific Patterns

### PCF Control Initialization (React 16)

```typescript
// CORRECT: React 16 pattern
import * as ReactDOM from "react-dom";  // NOT react-dom/client

public init(context, notifyOutputChanged, state, container): void {
  this.container = container;
  this.renderComponent();
}

private renderComponent(): void {
  ReactDOM.render(
    React.createElement(FluentProvider, { theme },
      React.createElement(RootComponent, { context: this.context })
    ),
    this.container
  );
}

public destroy(): void {
  ReactDOM.unmountComponentAtNode(this.container);  // NOT root.unmount()
}
```

### Entity Configuration (8 supported types)

```typescript
const ENTITY_CONFIGS: EntityConfig[] = [
  { logicalName: "sprk_matter", displayName: "Matter", regardingField: "sprk_regardingmatter", regardingRecordTypeValue: 1 },
  { logicalName: "sprk_project", displayName: "Project", regardingField: "sprk_regardingproject", regardingRecordTypeValue: 0 },
  { logicalName: "sprk_invoice", displayName: "Invoice", regardingField: "sprk_regardinginvoice", regardingRecordTypeValue: 2 },
  { logicalName: "sprk_analysis", displayName: "Analysis", regardingField: "sprk_regardinganalysis", regardingRecordTypeValue: 3 },
  { logicalName: "account", displayName: "Account", regardingField: "sprk_regardingaccount", regardingRecordTypeValue: 4 },
  { logicalName: "contact", displayName: "Contact", regardingField: "sprk_regardingcontact", regardingRecordTypeValue: 5 },
  { logicalName: "sprk_workassignment", displayName: "Work Assignment", regardingField: "sprk_regardingworkassignment", regardingRecordTypeValue: 6 },
  { logicalName: "sprk_budget", displayName: "Budget", regardingField: "sprk_regardingbudget", regardingRecordTypeValue: 7 }
];
```

### Type Compatibility (Strict Mode)

```typescript
const STRICT_COMPATIBLE: Record<FieldType, FieldType[]> = {
  [FieldType.Lookup]: [FieldType.Lookup, FieldType.Text],
  [FieldType.Text]: [FieldType.Text, FieldType.Memo],
  [FieldType.Memo]: [FieldType.Text, FieldType.Memo],
  [FieldType.OptionSet]: [FieldType.OptionSet, FieldType.Text],
  [FieldType.Number]: [FieldType.Number, FieldType.Text],
  [FieldType.DateTime]: [FieldType.DateTime, FieldType.Text],
  [FieldType.Boolean]: [FieldType.Boolean, FieldType.Text],
};
```

---

## Decisions Made

<!-- Log key architectural/implementation decisions here as project progresses -->
<!-- Format: Date, Decision, Rationale, Who -->

| Date | Decision | Rationale | Who |
|------|----------|-----------|-----|
| 2026-01-31 | Two-PCF approach (AssociationResolver + EventFormController) | Clear separation of concerns | Owner |
| 2026-01-31 | No Dataverse Business Rules | Keep validation in code per owner preference | Owner |
| 2026-01-31 | Strict mode only for R1 | Reduce complexity, add Resolve mode later | Owner |
| 2026-01-31 | Three sync modes (one-time, pull, push) | Comprehensive sync options without scheduled jobs | Owner |
| 2026-02-01 | Field Mapping Framework in Events R1 scope | Critical for event creation UX | Owner |

---

## Implementation Notes

<!-- Add notes about gotchas, workarounds, or important learnings during implementation -->

*No notes yet - add as implementation progresses*

---

## Resources

### Applicable ADRs

| ADR | Title | Relevance |
|-----|-------|-----------|
| ADR-001 | Minimal API | BFF API pattern |
| ADR-006 | PCF over Webresources | All 5 PCF controls |
| ADR-008 | Endpoint Filters | API authorization |
| ADR-010 | DI Minimalism | Service registration |
| ADR-011 | Dataset PCF | Grid control patterns |
| ADR-012 | Shared Components | FieldMappingService |
| ADR-019 | ProblemDetails | API error handling |
| ADR-021 | Fluent UI v9 | Dark mode, design tokens |
| ADR-022 | PCF Platform Libraries | React 16 APIs |

### Applicable Patterns

| Pattern | Purpose |
|---------|---------|
| `.claude/patterns/pcf/control-initialization.md` | PCF lifecycle |
| `.claude/patterns/pcf/theme-management.md` | Dark mode |
| `.claude/patterns/pcf/dataverse-queries.md` | WebAPI calls |
| `.claude/patterns/api/endpoint-definition.md` | Minimal API |
| `.claude/patterns/api/endpoint-filters.md` | Authorization |
| `.claude/patterns/api/error-handling.md` | ProblemDetails |
| `.claude/patterns/testing/integration-tests.md` | API testing |

### Scripts Available

| Script | Purpose |
|--------|---------|
| `scripts/Deploy-PCFWebResources.ps1` | Deploy PCF controls |
| `scripts/Test-SdapBffApi.ps1` | Test API endpoints |

### Related Projects

- None currently

### External Documentation

- [Fluent UI v9 Documentation](https://react.fluentui.dev/)
- [PCF Documentation](https://docs.microsoft.com/en-us/power-apps/developer/component-framework/overview)
- [Dataverse WebAPI](https://docs.microsoft.com/en-us/power-apps/developer/data-platform/webapi/overview)

---

*This file should be kept updated throughout project lifecycle*
