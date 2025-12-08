# code-review

---
description: Comprehensive code review covering security, performance, style, and ADR compliance
alwaysApply: false
---

## Purpose

Performs a structured, multi-dimension code review for Spaarke codebase changes. This skill ensures code quality across security, performance, maintainability, and architectural compliance dimensions. Unlike `adr-check` (architecture-only), this is a holistic quality gate.

## When to Use

- User says "review code", "review my changes", or "code review"
- Before creating a pull request
- After completing a task to validate implementation
- Explicitly invoked with `/code-review {file-or-path}`

## Inputs Required

| Input | Required | Source |
|-------|----------|--------|
| Files to review | Yes | Explicit paths, git diff, or current selection |
| Review depth | No | Default: "standard" (can be "quick" or "thorough") |
| Focus areas | No | Default: all (can narrow to security, performance, etc.) |

## Workflow

### Step 1: Determine Scope
```
IF files explicitly specified:
  SCOPE = specified files
ELIF git has uncommitted changes:
  SCOPE = git diff --name-only (staged + unstaged)
ELIF user mentions "recent" or "last commit":
  SCOPE = git diff HEAD~1 --name-only
ELSE:
  ASK user what to review
  
CATEGORIZE files by type:
  - .cs → .NET review checklist
  - .ts/.tsx → TypeScript/PCF review checklist
  - Plugin code → Plugin review checklist
```

### Step 2: Load Context
```
LOAD relevant CLAUDE.md files for code area:
  - Root CLAUDE.md (always)
  - Module-specific CLAUDE.md if exists

FOR each file in scope:
  READ file content
  IDENTIFY: purpose, dependencies, public API
```

### Step 3: Security Review
```
CHECK for common vulnerabilities:

✓ Secrets/credentials
  - Hardcoded strings that look like tokens/passwords
  - Connection strings in code
  - API keys
  
✓ Input validation
  - User input used without validation
  - SQL/XSS injection vectors
  - Path traversal vulnerabilities

✓ Authorization
  - Missing auth checks on endpoints
  - Inconsistent permission models
  - Elevation of privilege risks

✓ Data exposure
  - Sensitive data in logs
  - PII in error messages
  - Overly permissive CORS

FLAG: Critical / Warning / Info
```

### Step 4: Performance Review
```
CHECK for performance issues:

✓ N+1 queries
  - Loops with individual database calls
  - Graph API calls in loops

✓ Missing async/await
  - Blocking calls (.Result, .Wait())
  - Sync-over-async patterns

✓ Resource management
  - Missing disposal (IDisposable)
  - Large object allocations in loops
  - Unbounded collections

✓ Caching patterns (per ADR-009)
  - Missing caching for repeated lookups
  - In-memory cache for cross-request data (should be Redis)

FLAG: Critical / Warning / Info
```

### Step 4.5: Linting Check
```
RUN automated linting before manual review:

✓ TypeScript/PCF (ESLint)
  cd src/client/pcf && npm run lint
  - Catches: unused vars, type issues, React hooks rules
  - Config: src/client/pcf/eslint.config.mjs
  - Includes: @microsoft/eslint-plugin-power-apps

✓ C# (Roslyn Analyzers)
  dotnet build --warnaserror
  - Catches: null refs, async issues, naming conventions
  - Config: Directory.Build.props (TreatWarningsAsErrors=true)
  - Nullable reference types enabled

✓ Fix common issues:
  - TypeScript: npx eslint --fix {files}
  - C#: dotnet format

FLAG: Critical (lint errors block merge) / Warning (lint warnings)
```

### Step 5: Style & Maintainability Review
```
CHECK code quality:

✓ Naming conventions (from CLAUDE.md)
  - PascalCase for C# types/methods
  - camelCase for TypeScript variables
  - Descriptive names (not single letters except loops)

✓ Code organization
  - Method length (recommend <30 lines)
  - Class responsibility (single purpose)
  - Circular dependencies

✓ Documentation
  - Public API has XML docs (.cs)
  - Complex logic has comments
  - TODO/HACK comments addressed

✓ Error handling
  - Catch blocks that swallow exceptions
  - Missing try/catch for I/O operations
  - Error messages helpful for debugging

FLAG: Warning / Info / Suggestion
```

### Step 6: ADR Compliance Check
```
RUN subset of adr-check skill:

CRITICAL ADRs to always check:
  - ADR-001: No Azure Functions
  - ADR-002: Thin plugins (<50ms, no HTTP)
  - ADR-007: Graph types isolated
  - ADR-008: Endpoint filters for auth
  
IF violations found:
  LINK to full adr-check skill for details
  FLAG: Critical

See: .claude/skills/adr-check/ for detailed ADR validation rules
```

### Step 7: Technology-Specific Checks

#### For .NET Code (.cs)
```
✓ Minimal API patterns
  - Endpoint groups properly organized
  - Result pattern for error handling
  - Dependency injection registration minimal (ADR-010)

✓ Nullable reference types
  - Proper null checks
  - No null-forgiving (!) without justification

✓ Modern C# patterns
  - Using file-scoped namespaces
  - Using records for DTOs where appropriate
```

#### For TypeScript/PCF (.ts, .tsx)
```
✓ React patterns
  - Proper hook usage (rules of hooks)
  - Memoization where appropriate
  - Key prop in lists

✓ TypeScript strictness
  - No 'any' types without justification
  - Proper interface definitions
  - Null/undefined handling

✓ Fluent UI usage (per ADR)
  - Using v9 components (not v8)
  - Consistent styling approach
```

#### For Plugin Code
```
✓ Plugin constraints (ADR-002)
  - No HttpClient usage
  - No external service calls
  - Execution time estimation <50ms
  - Code size <200 LoC
```

### Step 8: Generate Review Report
```markdown
## Code Review Report

**Files Reviewed:** {count} files
**Review Depth:** {quick|standard|thorough}
**Date:** {timestamp}

### 🔴 Critical Issues (Block Merge)

{List critical security, performance, or ADR violations}

### 🟡 Warnings (Should Address)

{List warnings that should be fixed but aren't blockers}

### 🔵 Suggestions (Consider)

{List style improvements and optional enhancements}

### ✅ What's Good

{Highlight positive patterns observed}

### Summary by Category

| Category | Critical | Warning | Suggestion |
|----------|----------|---------|------------|
| Security | 0 | 1 | 0 |
| Performance | 0 | 0 | 2 |
| Style | 0 | 3 | 5 |
| ADR Compliance | 0 | 0 | 0 |

### Recommended Actions

1. {First priority fix}
2. {Second priority fix}
...

### Auto-Fixable Issues

Run these commands to fix some issues automatically:
- `dotnet format` - Fix C# formatting
- ADR check: `/adr-check` for full compliance scan
```

## Conventions

### Severity Levels
| Level | Meaning | Action Required |
|-------|---------|-----------------|
| 🔴 Critical | Security/correctness issue | Must fix before merge |
| 🟡 Warning | Quality concern | Should fix, discuss if not |
| 🔵 Suggestion | Enhancement | Optional, at author's discretion |

### Review Depth
| Depth | Focus | Use When |
|-------|-------|----------|
| quick | Critical issues only | Small changes, hotfixes |
| standard | Critical + warnings | Normal development |
| thorough | All levels + suggestions | Major features, refactors |

### Code Smells to Flag
- Methods >50 lines
- Classes >500 lines  
- Files with >10 imports
- Nested conditionals >3 levels
- Commented-out code
- Magic numbers without constants

## Resources

### Reference Files
- Root `CLAUDE.md` - Coding standards and conventions
- `docs/ai-knowledge/architecture/` - Architecture patterns
- `.claude/skills/adr-check/references/` - ADR validation rules

### Related Skills
- **adr-check**: Deep-dive on architecture compliance
- **spaarke-conventions**: Detailed coding standards enforcement
- **task-create**: Include code review as task deliverable
- **push-to-github**: Linting runs as pre-flight check before commits

## Examples

### Example 1: Review Git Changes
**Trigger**: "Review my changes before I commit"

**Process**:
1. Run `git diff --name-only` to get changed files
2. Read each file, apply review checklist
3. Output categorized findings

### Example 2: Focused Security Review
**Trigger**: "Do a security review of the auth endpoints"

**Process**:
1. Find files related to authentication
2. Focus on security checklist items
3. Report security-specific findings only

### Example 3: Thorough Pre-PR Review
**Trigger**: "Thorough code review for PR"

**Process**:
1. Get all changed files vs main branch
2. Run all checklist items at thorough depth
3. Include suggestions for improvement
4. Run full adr-check

### Example 4: Quick Hotfix Review
**Trigger**: "/code-review quick src/api/fix.cs"

**Process**:
1. Review single file
2. Focus on critical issues only
3. Skip style suggestions

## Validation Checklist

Before completing code review, verify:
- [ ] All files in scope were reviewed
- [ ] Critical issues have clear descriptions
- [ ] Warnings include specific locations (file:line)
- [ ] ADR compliance was checked
- [ ] Positive patterns were acknowledged
- [ ] Next steps are actionable
