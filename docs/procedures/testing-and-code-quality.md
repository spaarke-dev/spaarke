# Testing and Code Quality Procedures

> **Purpose**: Guide for code quality and testing workflows in Claude Code, including automated quality gates, UI testing, and human checkpoints.
>
> **Last Updated**: January 6, 2026

---

## Overview

This guide explains the testing and code quality process in the Spaarke development workflow. Quality assurance is built into **every task execution** through automated quality gates, with optional browser-based UI testing for frontend work.

**Key Concepts**:
- Quality gates run **automatically** after code implementation (Step 9.5)
- UI testing runs **with user confirmation** for PCF/frontend tasks (Step 9.7)
- Repository cleanup runs at **project completion** (Task 090)
- Human-in-loop at **decision points**, not execution

---

## Table of Contents

1. [Quality Gate Overview](#quality-gate-overview)
2. [Automated vs Human-in-Loop](#automated-vs-human-in-loop)
3. [Code Review (Step 9.5)](#code-review-step-95)
4. [ADR Compliance Check (Step 9.5)](#adr-compliance-check-step-95)
5. [Linting (Step 9.5)](#linting-step-95)
6. [UI Testing (Step 9.7)](#ui-testing-step-97)
7. [Repository Cleanup (Task 090)](#repository-cleanup-task-090)
8. [Complete Quality Flow](#complete-quality-flow)
9. [Skill Reference](#skill-reference)

---

## Quality Gate Overview

Quality gates are checkpoints that run during task execution to ensure code meets standards before completion.

### When Quality Gates Run

```
task-execute workflow:
  │
  ├─ Steps 1-8: Implementation
  │     └─→ Write code, build, test
  │
  ├─ Step 9: Verify Acceptance Criteria
  │     └─→ Check task requirements met
  │
  ├─ Step 9.5: Quality Gates (AUTOMATED)  ← 🔒 Mandatory
  │     ├─→ code-review
  │     ├─→ adr-check
  │     └─→ lint
  │
  ├─ Step 9.7: UI Testing (PROMPTED)      ← 👤 User confirms
  │     └─→ ui-test (if PCF/frontend)
  │
  └─ Step 10: Task Complete
```

### Quality Gate Types

| Gate | When | Blocking | Automation |
|------|------|----------|------------|
| **Code Review** | After implementation | Critical issues block | Fully automated |
| **ADR Check** | After implementation | Violations block | Fully automated |
| **Linting** | After implementation | Errors block | Fully automated |
| **UI Testing** | After deployment | Issues reported | User confirms start |
| **Repo Cleanup** | Project end | None (informational) | User approves deletions |

---

## Automated vs Human-in-Loop

### What's Fully Automated

| Operation | Skill | Human Action |
|-----------|-------|--------------|
| Code review execution | code-review | None - runs automatically |
| ADR validation | adr-check | None - runs automatically |
| Lint execution | npm/dotnet | None - runs automatically |
| Issue detection | All | None - issues reported |
| Fix suggestions | code-review | None - suggestions provided |

### What Requires Human Decision

| Checkpoint | Skill | Human Decides |
|------------|-------|---------------|
| Fix warnings now vs later | code-review | "Fix warnings now or proceed?" |
| Start UI testing | ui-test | "Run browser-based testing? [Y/n]" |
| Login/CAPTCHA | ui-test | Manual authentication |
| Approve file deletions | repo-cleanup | Review report, approve removals |
| Skip quality gate | All | Must document reason |

### Decision Points in task-execute

```
Step 9.5: Quality Gates
  │
  ├─ Code Review runs automatically
  │     │
  │     ├─ IF critical issues: MUST fix (no choice)
  │     │
  │     └─ IF warnings only:
  │           👤 USER DECIDES: "Fix warnings now or proceed?"
  │
  ├─ ADR Check runs automatically
  │     │
  │     └─ IF violations: MUST fix (no choice)
  │
  └─ Lint runs automatically
        │
        └─ IF errors: MUST fix (no choice)

Step 9.7: UI Testing (PCF/frontend tasks)
  │
  👤 USER DECIDES: "Run browser-based testing? [Y/n]"
  │
  ├─ IF yes:
  │     ├─ Claude navigates browser automatically
  │     ├─ 👤 USER: Login if prompted
  │     └─ Claude executes tests automatically
  │
  └─ IF no:
        └─ Reason documented, continue to Step 10
```

---

## Code Review (Step 9.5)

### What It Checks

The code-review skill performs multi-dimensional analysis:

| Category | Checks | Severity |
|----------|--------|----------|
| **Security** | Hardcoded secrets, SQL injection, XSS, auth gaps | Critical |
| **Performance** | N+1 queries, blocking calls, missing async | Warning |
| **Style** | Naming conventions, method length, complexity | Suggestion |
| **ADR Compliance** | Architecture patterns (delegated to adr-check) | Critical |

### How It Works

```
1. GET files modified in this task
   → From current-task.md "Files Modified" section

2. CATEGORIZE files by type
   → .cs → .NET review checklist
   → .ts/.tsx → TypeScript/PCF review checklist
   → Plugin code → Plugin constraints

3. RUN security checks
   → Secrets detection
   → Input validation
   → Authorization patterns

4. RUN performance checks
   → Async patterns
   → Query patterns
   → Resource management

5. RUN style checks
   → Naming conventions
   → Code organization
   → Documentation

6. GENERATE report
   → Critical (must fix)
   → Warnings (should fix)
   → Suggestions (optional)
```

### Example Output

```markdown
## Code Review Report

**Files Reviewed:** 5 files
**Review Depth:** standard

### 🔴 Critical Issues (Block Merge)

1. **Hardcoded connection string** in `src/server/api/Services/DataService.cs:45`
   - Issue: Connection string contains credentials
   - Fix: Move to configuration/Key Vault

### 🟡 Warnings (Should Address)

1. **Missing null check** in `src/client/pcf/Panel/index.ts:78`
   - Issue: `data.items` accessed without null check
   - Fix: Add optional chaining `data?.items`

### 🔵 Suggestions (Consider)

1. Method `ProcessData` is 65 lines - consider splitting

### Recommended Actions

1. [Critical] Move connection string to appsettings.json
2. [Warning] Add null check for data.items
```

### Invoking Manually

```bash
# Review all uncommitted changes
/code-review

# Review specific files
/code-review src/server/api/

# Review with focus area
"Do a security review of the auth endpoints"
```

---

## ADR Compliance Check (Step 9.5)

### What It Checks

The adr-check skill validates code against Architecture Decision Records:

| ADR | Constraint | Violation Example |
|-----|------------|-------------------|
| ADR-001 | No Azure Functions | Using `[FunctionName]` attribute |
| ADR-002 | Thin plugins (<50ms, no HTTP) | HttpClient in plugin |
| ADR-006 | PCF over webresources | Creating legacy .js webresource |
| ADR-007 | Graph types isolated | GraphServiceClient in controller |
| ADR-008 | Endpoint filters for auth | Global middleware for auth |
| ADR-021 | Fluent UI v9, no hard-coded colors | Using `#ffffff` instead of tokens |

### How It Works

```
1. IDENTIFY resource types in modified files
   → API endpoint → ADR-001, ADR-008, ADR-010
   → PCF control → ADR-006, ADR-011, ADR-012, ADR-021
   → Plugin → ADR-002
   → Caching → ADR-009

2. LOAD applicable ADRs
   → .claude/adr/ADR-XXX-*.md (concise versions)

3. CHECK each constraint
   → Pattern matching
   → Code analysis

4. REPORT violations
   → Violation description
   → ADR reference
   → Fix guidance
```

### Example Output

```markdown
## ADR Compliance Report

### 🔴 Violations Found

**ADR-002: Thin Dataverse Plugins**
- File: `src/solutions/Plugins/ValidateContact.cs:34`
- Violation: HttpClient instantiation in plugin
- Constraint: "No HTTP/Graph calls from plugins"
- Fix: Move HTTP call to BFF API, call via action

**ADR-021: Fluent UI v9 Design System**
- File: `src/client/pcf/Panel/styles.ts:12`
- Violation: Hard-coded color `#ffffff`
- Constraint: "Use semantic tokens, no hard-coded colors"
- Fix: Replace with `tokens.colorNeutralBackground1`

### ✅ Compliant Areas

- ADR-001: Minimal API patterns ✓
- ADR-008: Endpoint filter usage ✓
```

### Invoking Manually

```bash
# Check all changes
/adr-check

# Check specific path
/adr-check src/client/pcf/
```

---

## Linting (Step 9.5)

### TypeScript/PCF Linting

```bash
# Runs automatically in Step 9.5
cd src/client/pcf && npm run lint

# Auto-fix available issues
npx eslint --fix {files}
```

**Config**: `src/client/pcf/eslint.config.mjs`

**Catches**:
- Unused variables
- Type issues
- React hooks rules
- Power Apps specific rules (@microsoft/eslint-plugin-power-apps)

### C# Linting (Roslyn Analyzers)

```bash
# Runs automatically in Step 9.5
dotnet build --warnaserror

# Auto-fix formatting
dotnet format
```

**Config**: `Directory.Build.props` (TreatWarningsAsErrors=true)

**Catches**:
- Null reference issues
- Async patterns
- Naming conventions
- Code style

---

## UI Testing (Step 9.7)

### Requirements

| Requirement | Check |
|-------------|-------|
| Claude Code 2.0.73+ | `claude --version` |
| Google Chrome | Not Edge/Brave |
| Claude in Chrome extension 1.0.36+ | Chrome extensions |
| Claude Code started with `--chrome` | `claude --chrome` |

### When UI Testing Triggers

```
IF ALL conditions met:
  ✓ Task tags include: pcf, frontend, fluent-ui, e2e-test
  ✓ Claude Code has Chrome integration
  ✓ Deployment completed
  ✓ Task has UI tests or UI acceptance criteria

THEN:
  👤 PROMPT: "UI tests defined. Run browser-based testing? [Y/n]"
```

### What Claude Can Do Autonomously

| Action | Automated | Example |
|--------|-----------|---------|
| Navigate | ✅ Yes | Open D365 form |
| Click | ✅ Yes | Click buttons, menus |
| Type | ✅ Yes | Fill form fields |
| Read | ✅ Yes | Check text, DOM |
| Console | ✅ Yes | Detect errors |
| Screenshot | ✅ Yes | Capture states |
| Record GIF | ✅ Yes | Demo flows |
| Login | ❌ Manual | User authenticates |
| CAPTCHA | ❌ Manual | User solves |
| MFA | ❌ Manual | User completes |

### Defining UI Tests

**In Task POML**:
```xml
<ui-tests>
  <test name="Component Renders">
    <url>https://org.crm.dynamics.com/main.aspx?...</url>
    <steps>
      <step>Verify control is visible</step>
      <step>Check console for errors</step>
    </steps>
    <expected>Control renders without errors</expected>
  </test>

  <test name="Dark Mode (ADR-021)">
    <steps>
      <step>Toggle dark mode</step>
      <step>Verify colors adapt</step>
    </steps>
    <expected>All colors use semantic tokens</expected>
  </test>
</ui-tests>
```

**In Project CLAUDE.md**:
```markdown
## UI Testing Context

### Environment URLs
| Environment | URL |
|-------------|-----|
| Dev | https://spaarke-dev.crm.dynamics.com |
| Staging | https://spaarke-staging.crm.dynamics.com |

### Common Navigation
- Account form: `/main.aspx?appid={id}&pagetype=entityrecord&etn=account`
```

### Example UI Test Flow

```
Claude: "UI tests defined for this task. Run browser-based testing? [Y/n]"

User: "y"

Claude: "Opening https://spaarke-dev.crm.dynamics.com/...
         Please log in if prompted."

[User logs in]

User: "logged in"

Claude: "✅ Logged in. Starting tests...

1. Navigating to Account form...
   ✅ AISummaryPanel control visible

2. Checking console...
   ✅ No errors detected

3. Testing dark mode...
   → Toggling dark mode setting
   ✅ Background adapted
   ✅ Text colors adapted
   ✅ No hard-coded colors

4. Testing refresh button...
   ✅ Loading indicator appeared
   ✅ Data refreshed in 1.2s

UI Test Results: ✅ All 4 tests passed"
```

### Invoking Manually

```bash
# Start session with Chrome
claude --chrome

# Run UI tests
/ui-test

# Or natural language
"Test the PCF control in the browser"
"Check dark mode compliance"
"Record a demo of the upload flow"
```

---

## Repository Cleanup (Task 090)

### When It Runs

Repository cleanup is part of the **mandatory project wrap-up task** (Task 090):

```
Task 090: Project Wrap-up
  │
  ├─ Step 1: Run final quality gates
  │     ├─ /code-review on all project code
  │     └─ /adr-check on all project code
  │
  ├─ Step 2: Run repository cleanup  ← repo-cleanup skill
  │     ├─ /repo-cleanup projects/{project-name}
  │     ├─ Review cleanup report
  │     └─ 👤 USER: Approve removals
  │
  ├─ Steps 3-6: Update documentation
  │
  └─ Complete project
```

### What Gets Cleaned

| Location | Action | Human Approval |
|----------|--------|----------------|
| `notes/debug/` | Remove | Yes |
| `notes/spikes/` | Remove | Yes |
| `notes/drafts/` | Remove | Yes |
| `notes/scratch.md` | Remove | Yes |
| `notes/handoffs/` | Archive to `.archive/` | Yes |
| `notes/lessons-learned.md` | Keep | N/A |

### What's Preserved

| Location | Reason |
|----------|--------|
| `spec.md` | Original design intent |
| `README.md` | Project documentation |
| `plan.md` | Implementation record |
| `CLAUDE.md` | AI context |
| `tasks/*.poml` | Task history |
| `notes/lessons-learned.md` | Knowledge capture |

### Example Cleanup Report

```markdown
## Repository Cleanup Report

**Scope**: projects/ai-doc-summary
**Mode**: Project Completion

### Summary
| Category | Found | Auto-Fixable |
|----------|-------|--------------|
| Ephemeral Files | 8 | 8 |
| Structure | 0 | 0 |

### Ephemeral Files (Safe to Remove)
| File/Directory | Reason | Size |
|----------------|--------|------|
| notes/debug/api-trace.md | Debug session | 12KB |
| notes/spikes/embedding-test.ts | Exploratory | 4KB |
| notes/drafts/design-v1.md | Superseded | 8KB |

### Recommended Actions
1. Remove 8 ephemeral files (24KB total)
2. Archive notes/handoffs/ to .archive/

Proceed with cleanup? (y/n)
```

### Invoking Manually

```bash
# Project completion cleanup
/repo-cleanup projects/{project-name}

# Full repository audit
/repo-cleanup

# Pre-merge check
/repo-cleanup --mode=pre-merge
```

---

## Complete Quality Flow

### Task-Level Flow (Every Task)

```
┌─────────────────────────────────────────────────────────────────┐
│                    TASK EXECUTION FLOW                          │
└─────────────────────────────────────────────────────────────────┘

Steps 1-8: Implementation
  │ Claude writes code, builds, runs tests
  │ Updates current-task.md with progress
  ▼
Step 9: Verify Acceptance Criteria
  │ Check task requirements met
  ▼
Step 9.5: Quality Gates [AUTOMATED]
  │
  ├─► code-review ─────────────────────────────────────────────┐
  │     • Security checks                                       │
  │     • Performance checks                                    │
  │     • Style checks                                          │
  │                                                             │
  │     IF critical issues → MUST FIX ──────────────────────►──┤
  │     IF warnings → 👤 "Fix now or proceed?" ─────────────►──┤
  │                                                             │
  ├─► adr-check ───────────────────────────────────────────────┤
  │     • ADR compliance validation                             │
  │                                                             │
  │     IF violations → MUST FIX ───────────────────────────►──┤
  │                                                             │
  └─► lint (npm/dotnet) ───────────────────────────────────────┤
        • TypeScript: ESLint                                    │
        • C#: Roslyn analyzers                                  │
                                                                │
        IF errors → MUST FIX ───────────────────────────────►──┘
  │
  ▼
Step 9.7: UI Testing [PROMPTED - PCF/Frontend only]
  │
  │ 👤 "Run browser-based testing? [Y/n]"
  │
  ├─► IF yes:
  │     • Claude opens browser
  │     • 👤 User logs in if prompted
  │     • Claude runs tests automatically
  │     • Reports results
  │
  └─► IF no:
        • Reason documented
        • Continue to completion
  │
  ▼
Step 10: Task Complete
  │ Update task status
  │ Update TASK-INDEX.md
  ▼
Step 10.6: Conflict Sync Check
  │ Check for master updates
  │ Recommend rebase if needed
  ▼
Step 11: Transition to Next Task
```

### Project-Level Flow (Project Wrap-up)

```
┌─────────────────────────────────────────────────────────────────┐
│                  PROJECT WRAP-UP (Task 090)                     │
└─────────────────────────────────────────────────────────────────┘

Step 1: Final Quality Gates
  │
  ├─► /code-review on entire project
  │     • All project files reviewed
  │     • Critical issues must be fixed
  │
  └─► /adr-check on entire project
        • Full ADR compliance validation
  │
  ▼
Step 2: Repository Cleanup
  │
  ├─► /repo-cleanup projects/{name}
  │     • Identifies ephemeral files
  │     • Generates cleanup report
  │
  └─► 👤 User reviews and approves
        • Approve file deletions
        • Archive handoffs
  │
  ▼
Steps 3-6: Documentation Updates
  │
  ├─► Update README.md
  │     • Status: Complete
  │     • Progress: 100%
  │
  ├─► Update plan.md
  │     • All milestones ✅
  │
  └─► Create lessons-learned.md (if notable)
  │
  ▼
Step 7: Final Verification
  │
  ├─► All tasks completed in TASK-INDEX.md
  ├─► No critical code-review issues
  └─► Repository cleanup completed
  │
  ▼
Project Complete ✅
```

---

## Skill Reference

### Quality Skills Summary

| Skill | Trigger | Auto/Manual | When |
|-------|---------|-------------|------|
| **code-review** | Step 9.5, `/code-review` | Automated | After implementation |
| **adr-check** | Step 9.5, `/adr-check` | Automated | After implementation |
| **ui-test** | Step 9.7, `/ui-test` | User confirms | After deployment |
| **repo-cleanup** | Task 090, `/repo-cleanup` | User approves | Project end |

### Related Skills

| Skill | Role in Quality |
|-------|-----------------|
| **dataverse-deploy** | Deploys PCF before UI testing |
| **push-to-github** | Runs code-review/adr-check pre-commit |
| **task-execute** | Orchestrates all quality gates |
| **task-create** | Includes wrap-up task template |

### Slash Commands Quick Reference

```bash
# Code quality
/code-review              # Review changed files
/adr-check               # Check ADR compliance

# UI testing (requires --chrome)
/ui-test                 # Run browser tests
/chrome                  # Check Chrome connection

# Repository cleanup
/repo-cleanup            # Full repo audit
/repo-cleanup projects/X # Project-specific cleanup
```

---

## Best Practices

### For Code Review

1. **Fix critical issues immediately** - They block completion
2. **Address warnings before PR** - Avoid accumulating debt
3. **Document skipped suggestions** - Explain why in task notes

### For UI Testing

1. **Start Claude with `--chrome`** when working on PCF/frontend
2. **Define tests in task POML** for specific, repeatable tests
3. **Include dark mode testing** for all Fluent UI components

### For Repository Cleanup

1. **Run at project end** - Not during active development
2. **Review before approving** - Check nothing important is flagged
3. **Archive handoffs** - Don't delete, move to `.archive/`

### For Parallel Sessions

1. **Run quality gates per-task** - Don't batch
2. **Rebase before PR ready** - Avoid merge conflicts
3. **Sequential merge** - One PR at a time

---

## Troubleshooting

| Issue | Cause | Solution |
|-------|-------|----------|
| Code review not running | Not in task-execute | Run `/code-review` manually |
| UI test skipped automatically | No pcf/frontend tags | Add tags to task or run `/ui-test` |
| Chrome not connected | Missing flag | Start with `claude --chrome` |
| Login keeps timing out | Session expired | Re-authenticate, continue |
| ADR check finds false positive | Outdated pattern | Update `.claude/adr/` files |
| Cleanup flagging needed files | Wrong scope | Use project-specific path |

---

## Summary

**Quality is built into every task through automated gates:**

1. **Step 9.5** - Automated code-review, adr-check, lint (mandatory)
2. **Step 9.7** - UI testing with user confirmation (PCF/frontend)
3. **Task 090** - Repo cleanup with user approval (project end)

**Human decisions are at checkpoints, not execution:**
- Fix warnings now vs later
- Start UI testing
- Approve file deletions

**Start Claude Code with `--chrome` for UI testing:**
```bash
claude --chrome
```

---

## Related Documentation

- [Parallel Claude Code Sessions](parallel-claude-sessions.md) - Multi-session workflow
- [Context Recovery Procedure](context-recovery.md) - Resuming work
- [code-review Skill](.claude/skills/code-review/SKILL.md) - Full skill documentation
- [ui-test Skill](.claude/skills/ui-test/SKILL.md) - Browser testing details
- [repo-cleanup Skill](.claude/skills/repo-cleanup/SKILL.md) - Cleanup procedures

---

*Last updated: January 6, 2026*
