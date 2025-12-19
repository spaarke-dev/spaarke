# AI Agent Playbook: Design Spec to Implementation

> **Last Updated**: December 3, 2025
>
> **Purpose**: Step-by-step instructions for AI agents to process a Detailed Design Specification into project artifacts (plan, tasks, README, code).

---

## Overview

When a **Detailed Design Specification** (.docx or .md) is ready for development, the AI agent follows this playbook to:

1. Analyze the design spec
2. Gather relevant context (ADRs, architecture, existing code)
3. Generate project artifacts (README, plan, tasks)
4. Begin implementation following the spec

---

## Phase 1: Ingest Design Specification

### Step 1.1: Locate the Design Spec

```
User provides: "Here is the design spec for {Feature Name}"
- File may be: .docx, .md, .pdf, or pasted text
- Location: Usually in docs/specs/features/{feature-name}/ or provided directly
```

### Step 1.2: Extract Key Information

Parse the design spec and extract:

| Information | Look For | Example |
|-------------|----------|---------|
| **Feature Name** | Title, "Overview" section | "Universal Quick Create PCF" |
| **Problem Statement** | "Problem", "Background", "Current State" | "Users cannot upload documents..." |
| **Solution Approach** | "Solution", "Approach", "Design" | "Build a PCF control that..." |
| **Data Model** | "Data Model", "Entities", "Schema" | Entity names, fields, relationships |
| **API Endpoints** | "API", "Endpoints", "Contracts" | Routes, methods, payloads |
| **UI Components** | "UI", "Screens", "Components" | Component names, wireframes |
| **Acceptance Criteria** | "Acceptance Criteria", "Requirements" | Testable criteria list |
| **Non-Functional Requirements** | "NFRs", "Performance", "Security" | Latency, availability targets |

### Step 1.3: Summarize Understanding

Before proceeding, output a summary:

```markdown
## Design Spec Summary

**Feature**: {Feature Name}
**Problem**: {1-2 sentence problem statement}
**Solution**: {1-2 sentence solution approach}

**Key Components**:
- {Component 1}
- {Component 2}

**Estimated Complexity**: Low / Medium / High
```

---

## Phase 2: Gather Context

### Step 2.1: Check Architecture Decision Records (ADRs)

**Location**: `docs/adr/`

**Search for relevant ADRs:**

```
Read: docs/adr/README-ADRs.md (index of all ADRs)

For each ADR, check if it applies to:
- Technology choices in the spec (e.g., PCF → ADR-006, ADR-011, ADR-012)
- Backend patterns (e.g., API → ADR-001, ADR-007, ADR-008, ADR-010)
- Plugins (e.g., Dataverse → ADR-002)
- Caching (e.g., Redis → ADR-009)
```

**Output**: List of applicable ADRs with constraints

```markdown
## Applicable ADRs

| ADR | Title | Constraint for This Project |
|-----|-------|----------------------------|
| ADR-006 | PCF over webresources | Must build as PCF, not JS webresource |
| ADR-012 | Shared component library | Reuse @spaarke/ui-components |
| ADR-008 | Endpoint filters | Use endpoint filters for auth, not middleware |
```

### Step 2.2: Check Existing Architecture

**Location**: `docs/architecture/`

**Search for:**
- System diagrams
- Component relationships
- Integration points

```
Read: docs/architecture/*.md
Look for: How this feature fits into existing system
```

### Step 2.3: Find Related Code

**Search the codebase for:**

1. **Similar implementations**:
   ```
   Search: src/client/pcf/ for similar PCF controls
   Search: src/server/api/ for similar endpoints
   ```

2. **Shared libraries to reuse**:
   ```
   Check: src/client/shared/Spaarke.UI.Components/
   Check: src/server/shared/
   ```

3. **Existing services/utilities**:
   ```
   Search for: Services that handle similar operations
   Search for: Utilities that can be reused
   ```

**Output**: List of reusable code

```markdown
## Reusable Code

| Component | Location | How to Reuse |
|-----------|----------|--------------|
| SdapApiClient | src/client/pcf/.../services/ | Import for API calls |
| DataGrid | @spaarke/ui-components | Import for list UI |
| SpeFileStore | src/server/api/.../Services/ | Use for file operations |
```

### Step 2.4: Check Knowledge Base

**Location**: `docs/knowledge-base/` or `docs/KM-*.md`

**Search for relevant guides:**
```
If PCF: Read KM-PCF-*.md files
If Dataverse: Read KM-DATAVERSE-*.md files
If Auth: Read KM-*-OAUTH*.md, KM-*-MSAL*.md files
```

---

## Phase 3: Generate Project Artifacts

### Step 3.1: Create Project Folder

```
Location: docs/projects/{feature-name}/
```

### Step 3.2: Generate README.md

**Use template**: `docs/templates/project-README.template.md`

**Fill in from design spec**:
- Overview → from spec's "Overview" section
- Problem Statement → from spec's "Problem" / "Background"
- Solution Summary → from spec's "Solution" / "Approach"
- Scope → from spec's "Scope" / "Requirements"
- Graduation Criteria → from spec's "Acceptance Criteria"

**Add from context gathering**:
- Key Decisions → link to applicable ADRs
- Dependencies → from code analysis
- Risks → infer from complexity and dependencies

### Step 3.3: Generate plan.md

**Use template**: `docs/templates/project-plan.template.md`

**Fill in from design spec**:
- Executive Summary → from spec overview
- Solution Overview → from spec's technical design
- Scope Definition → from spec's requirements
- Acceptance Criteria → from spec's acceptance criteria

**Estimate work breakdown**:

| Spec Section | Generates Tasks For |
|--------------|---------------------|
| Data Model | Entity creation, schema setup |
| API Endpoints | Endpoint implementation, tests |
| UI Components | Component development, styling |
| Integration | Wiring components together |
| Security | Auth implementation, testing |

**Apply estimation heuristics**:
- Simple CRUD endpoint: 0.5-1 day
- Complex endpoint with auth: 1-2 days
- PCF component (simple): 2-3 days
- PCF component (complex with state): 3-5 days
- Integration/wiring: 1-2 days
- Testing (unit): 0.5 day per component
- Testing (integration): 1-2 days
- Documentation: 0.5-1 day

### Step 3.4: Generate tasks.md

**Create granular task list from plan.md WBS**:

```markdown
# Tasks: {Feature Name}

## Status Legend
- ⬜ Not Started
- 🔄 In Progress  
- ✅ Complete
- ⏸️ Blocked

## Tasks

### Phase 1: Setup
- [ ] Create project folder structure
- [ ] Set up development environment
- [ ] Review ADRs and constraints

### Phase 2: Data Model
- [ ] {Task from spec's data model section}
- [ ] {Task from spec's data model section}

### Phase 3: API Development
- [ ] {Task from spec's API section}
- [ ] {Task from spec's API section}

### Phase 4: UI Development
- [ ] {Task from spec's UI section}
- [ ] {Task from spec's UI section}

### Phase 5: Integration
- [ ] Wire UI to API
- [ ] End-to-end testing

### Phase 6: Documentation
- [ ] Update user documentation
- [ ] Update admin guide
```

---

## Phase 4: Validate Before Implementation

### Step 4.1: Cross-Reference Checklist

Before starting implementation, verify:

- [ ] **ADR Compliance**: Does the plan comply with all applicable ADRs?
- [ ] **Reuse Maximized**: Are we reusing existing components where possible?
- [ ] **Scope Clear**: Is scope clearly defined with in/out boundaries?
- [ ] **Acceptance Testable**: Can each acceptance criterion be tested?
- [ ] **Dependencies Identified**: Are all dependencies listed and available?
- [ ] **Risks Mitigated**: Are high risks addressed with mitigation plans?

### Step 4.2: Output Summary for User

```markdown
## Ready for Development

**Project**: {Feature Name}
**Documents Created**:
- ✅ docs/projects/{feature-name}/README.md
- ✅ docs/projects/{feature-name}/plan.md
- ✅ docs/projects/{feature-name}/tasks.md

**Applicable ADRs**: ADR-006, ADR-008, ADR-012

**Reusable Components Identified**:
- @spaarke/ui-components (DataGrid, StatusBadge)
- SdapApiClient (API calls)

**Estimated Effort**: {X} days

**Ready to begin?** Confirm to start Phase 5 (Implementation).
```

---

## Phase 5: Implementation

**For each task, follow the Task Execution Protocol**: `docs/templates/task-execution.template.md`

### Step 5.0: Context Management (CRITICAL)

**Before starting ANY task and after EACH subtask:**

```
CONTEXT CHECK:
- If context > 70%: STOP → Create handoff summary → Request new chat
- If context < 70%: Proceed
```

See `task-execution.template.md` for full Context Reset Protocol.

### Step 5.1: Follow Task List

Work through tasks in order, using the Task Execution Protocol for each:

1. **Context Check** - Verify < 70% context usage
2. **Review Progress** - Check project status and dependencies
3. **Gather Resources** - ADRs, knowledge articles, code patterns
4. **Execute** - Break into subtasks, implement with checks
5. **Verify** - Test, build, acceptance criteria
6. **Document** - Update task status, completion report

### Step 5.2: For Each Task (Summary)

1. **Read the relevant spec section**
2. **Check ADR constraints** before writing code
3. **Look for reusable code** before writing new
4. **Write tests** alongside implementation
5. **Update task status** when complete
6. **Check context** - reset if > 70%

### Step 5.3: Code Patterns to Follow

**For PCF Controls**:
```
Read: src/client/pcf/CLAUDE.md
Follow: Patterns in existing controls (UniversalQuickCreate)
Use: @spaarke/ui-components for UI
```

**For API Endpoints**:
```
Read: src/server/api/Spe.Bff.Api/CLAUDE.md
Follow: SpeFileStore facade pattern (ADR-007)
Use: Endpoint filters for auth (ADR-008)
```

**For Dataverse**:
```
Read: src/server/shared/CLAUDE.md
Follow: Thin plugin pattern (ADR-002)
```

---

## Quick Reference: Where to Find Things

| What You Need | Where to Look |
|---------------|---------------|
| Architecture rules | `docs/adr/` |
| Coding standards | `CLAUDE.md` (root and module-level) |
| Task execution protocol | `docs/templates/task-execution.template.md` |
| Existing PCF controls | `src/client/pcf/` |
| Shared UI components | `src/client/shared/Spaarke.UI.Components/` |
| API services | `src/server/api/Spe.Bff.Api/` |
| Shared .NET code | `src/server/shared/` |
| Knowledge articles | `docs/KM-*.md` or `docs/knowledge-base/` |
| Project templates | `docs/templates/` |

## Quick Reference: Context Thresholds

| Context % | Action |
|-----------|--------|
| < 50% | ✅ Proceed normally |
| 50-70% | ⚠️ Monitor, consider wrapping up current subtask |
| > 70% | 🛑 STOP - Create handoff summary and reset |
| > 85% | 🚨 CRITICAL - Immediately create handoff, may lose context |

---

## Prompts for User

When starting this workflow, the AI agent should ask:

1. **"Please provide the design specification document."**
   - Accept: .docx, .md, .pdf, or pasted text

2. **"Is this a new feature or enhancement to existing?"**
   - If enhancement: Ask which existing component

3. **"What is the target completion date?"**
   - Use for timeline in plan.md

4. **"Who is the project owner/reviewer?"**
   - Use for team section in plan.md

---

*Template version: 1.0 | Based on Spaarke development lifecycle*
