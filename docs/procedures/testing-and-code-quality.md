# Testing and Code Quality Procedures

> **Purpose**: Authoritative guide for the Spaarke code quality system, covering the full quality lifecycle: pre-commit hooks, PR quality gates, nightly sweeps, weekly summaries, quarterly audits, and task-level quality gates within Claude Code.
>
> **Last Updated**: April 5, 2026
> **Last Reviewed**: 2026-04-05
> **Reviewed By**: ai-procedure-refactoring-r2
> **Status**: Current

---

## Overview

This guide explains the complete code quality system in the Spaarke development workflow. Quality assurance operates at **five levels**, from individual developer commits through quarterly audits.

**Quality Lifecycle Layers**:

| Layer | When | Tools | Blocking? |
|-------|------|-------|-----------|
| **Pre-commit** | Every `git commit` | Husky + lint-staged (Prettier, ESLint, dotnet format) | Yes |
| **Task-level** | During Claude Code task execution | code-review, adr-check, lint (Step 9.5) | Yes |
| **PR gates** | Every pull request (sdap-ci.yml) | Build, test, security scan, client quality, code quality, ADR tests | Yes |
| **Nightly** | Weeknights Mon-Fri 6 AM UTC | Full test suite, SonarCloud, Claude AI review, dependency audit | Advisory |
| **Weekly** | Fridays 10 PM UTC | Trend aggregation from nightly runs | Advisory |
| **Quarterly** | Manual trigger | Full audit runbook | Advisory |

**Key Concepts**:
- Pre-commit hooks catch formatting and lint issues **before code enters git**
- Task-level quality gates run **automatically** after code implementation (Step 9.5)
- PR gates enforce build, test, and quality checks on **every pull request**
- Nightly sweeps provide deep analysis that would be too slow for PR-time
- Weekly summaries track quality trends over time
- UI testing runs **with user confirmation** for PCF/frontend tasks (Step 9.7)
- Repository cleanup runs at **project completion** (Task 090)
- Human-in-loop at **decision points**, not execution

---

## Table of Contents

1. [Pre-Commit Hooks (Husky + lint-staged)](#pre-commit-hooks-husky--lint-staged)
2. [Task-Level Quality Gates (Step 9.5)](#task-level-quality-gates-step-95)
3. [Code Review (Step 9.5)](#code-review-step-95)
4. [ADR Compliance Check (Step 9.5)](#adr-compliance-check-step-95)
5. [Linting (Step 9.5)](#linting-step-95)
6. [PR Quality Gates (sdap-ci.yml)](#pr-quality-gates-sdap-ciyml)
7. [Nightly Quality Pipeline](#nightly-quality-pipeline)
8. [Weekly Quality Summary](#weekly-quality-summary)
9. [Quarterly Audit](#quarterly-audit)
10. [UI Testing (Step 9.7)](#ui-testing-step-97)
11. [Claude Code Hooks](#claude-code-hooks)
12. [AI-Assisted PR Reviews](#ai-assisted-pr-reviews)
13. [Repository Cleanup (Task 090)](#repository-cleanup-task-090)
14. [Module-Specific Test Guidance](#module-specific-test-guidance)
15. [Test Selection Matrix](#test-selection-matrix)
16. [Coverage Targets](#coverage-targets)
17. [Architecture Test Enforcement](#architecture-test-enforcement)
18. [Complete Quality Flow](#complete-quality-flow)
19. [Skill Reference](#skill-reference)

---

## Pre-Commit Hooks (Husky + lint-staged)

Pre-commit hooks are the first quality layer, catching formatting and lint issues before code enters git history.

### Configuration

| Component | Config File | Purpose |
|-----------|------------|---------|
| Husky | `.husky/pre-commit` | Runs `npx lint-staged` on every commit |
| lint-staged | `.lintstagedrc.mjs` | Defines per-filetype commands for staged files |
| Prettier | `.prettierrc.json` | TypeScript/JSON/YAML formatting rules |
| ESLint | `src/client/pcf/eslint.config.mjs` (+ per-control configs) | TypeScript/React linting rules |
| dotnet format | `.editorconfig` | C# formatting rules |

### What Runs on Commit

| File Type | Tool | Action |
|-----------|------|--------|
| `*.ts`, `*.tsx` | Prettier + ESLint | Format, then lint (from nearest eslint.config directory) |
| `*.json`, `*.yaml`, `*.yml` | Prettier | Format |
| `*.cs` | dotnet format | Format (scoped to staged files) |

### Performance Target

Pre-commit hooks MUST complete in **< 10 seconds** (lint-staged runs only on staged files, not the full codebase).

### Skipping Hooks (Emergency Only)

```bash
# Skip pre-commit hooks (use sparingly — CI will catch issues)
git commit --no-verify -m "fix(api): emergency hotfix — skipping hooks, justification: [reason]"
```

Hooks are automatically skipped in CI environments (`$CI=true`).

**Important**: When skipping hooks, document the justification in the commit message. CI will still enforce the same checks -- skipping hooks only defers the feedback.

### Updating Hooks

If you modify `.lintstagedrc.mjs` or add a new ESLint config directory, test the hooks locally:

```bash
# Verify Husky is installed
npx husky --version

# Re-install hooks after cloning or after package.json changes
npx husky install

# Test lint-staged without committing
npx lint-staged --verbose
```

### Husky Troubleshooting

| Issue | Cause | Fix |
|-------|-------|-----|
| "command not found: npx" | Node.js not in PATH when hook runs | Ensure Node.js is installed and in system PATH; on Windows, restart terminal after Node install |
| Hook not firing on commit | Husky not initialized or `.husky/pre-commit` missing | Run `npx husky install` to re-initialize; verify `.husky/pre-commit` exists and contains `npx lint-staged` |
| "permission denied" on `.husky/pre-commit` (macOS/Linux) | Hook file not executable | Run `chmod +x .husky/pre-commit` |
| lint-staged hangs or times out | ESLint running on too many files or missing node_modules | Run `npm ci` in the relevant directory; check `.lintstagedrc.mjs` for correct glob patterns |
| "Cannot find module" errors from lint-staged | Dependencies not installed | Run `npm ci` at repo root and `npm ci` in `src/client/pcf/` |
| Hook runs but doesn't catch formatting | Files not staged (only staged files are checked) | Stage files with `git add` before committing; lint-staged only processes staged files |

---

## Task-Level Quality Gates (Step 9.5)

Quality gates are checkpoints that run during Claude Code task execution to ensure code meets standards before completion.

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

### Prettier (TypeScript/JSON/YAML Formatting)

**What it does**: Enforces consistent code formatting for TypeScript, JSON, and YAML files across the repository.

**When it runs**: Pre-commit (via Husky/lint-staged), PR (client-quality job in sdap-ci.yml), Step 9.5 quality gates.

**Configuration**: `.prettierrc.json` at repository root:
```json
{
  "printWidth": 120,
  "tabWidth": 2,
  "useTabs": false,
  "semi": true,
  "singleQuote": true,
  "trailingComma": "es5",
  "bracketSpacing": true,
  "arrowParens": "avoid",
  "endOfLine": "crlf"
}
```

**Run Locally**:
```bash
# Check formatting (reports violations without modifying files)
npx prettier --check "src/client/**/*.{ts,tsx}"

# Auto-fix formatting
npx prettier --write "src/client/**/*.{ts,tsx}"

# Check all supported file types
npx prettier --check "src/client/**/*.{ts,tsx,json,yaml,yml}"
```

**How to interpret results**: Prettier outputs a list of files that do not match the expected format. If the command exits with code 1, formatting violations exist.

**Troubleshooting**:

| Issue | Cause | Fix |
|-------|-------|-----|
| "No parser could be inferred" error | File extension not recognized by Prettier | Check `.prettierrc.json` and ensure the file type is supported; add `--parser typescript` if needed |
| Prettier conflicts with ESLint | Prettier and ESLint disagree on formatting | Prettier runs first (formatting), then ESLint (logic). lint-staged handles this order automatically |
| CRLF/LF mismatch | `endOfLine` setting doesn't match git config | Ensure `.prettierrc.json` has `"endOfLine": "crlf"` (Windows) and git `autocrlf` is set appropriately |

### ESLint (TypeScript/React Linting)

**What it does**: Enforces TypeScript/React coding standards, catches type issues, and validates Power Apps component framework rules.

**When it runs**: Pre-commit (via Husky/lint-staged), PR (client-quality job with `--max-warnings 0`), Step 9.5 quality gates.

**Configuration**: `src/client/pcf/eslint.config.mjs` (flat config format). Key rule sets:
- `@eslint/js` recommended
- `typescript-eslint` recommended + stylistic
- `eslint-plugin-promise` flat/recommended
- `@microsoft/eslint-plugin-power-apps` paCheckerHosted

Per-control overrides exist in `src/client/pcf/{ControlName}/eslint.config.mjs` for controls with specific needs.

**Strictened rules** (added in this project):
- `@typescript-eslint/no-explicit-any`: warn
- `@typescript-eslint/no-empty-function`: warn
- `@typescript-eslint/array-type`: warn
- `@typescript-eslint/consistent-generic-constructors`: warn
- `@typescript-eslint/consistent-indexed-object-style`: warn
- `promise/always-return`: warn
- `promise/catch-or-return`: warn

**Run Locally**:
```bash
# Run ESLint strict check (same as CI)
cd src/client/pcf && npx eslint . --max-warnings 0

# Auto-fix available issues
cd src/client/pcf && npx eslint . --fix

# Check a specific control
cd src/client/pcf && npx eslint UniversalDatasetGrid/ --max-warnings 0
```

**How to interpret results**: ESLint outputs violations grouped by file. Each violation shows the rule name, severity (error/warning), line number, and message. With `--max-warnings 0`, any warning causes a non-zero exit code.

**Justified suppressions**: When a rule violation is intentional and correct, suppress it inline with a comment explaining why:
```typescript
// eslint-disable-next-line @typescript-eslint/no-explicit-any -- Dataverse SDK returns untyped data
const rawData: any = context.parameters.dataset;
```

**Troubleshooting**:

| Issue | Cause | Fix |
|-------|-------|-----|
| "Cannot find module 'eslint-plugin-promise'" | ESLint dependencies not installed in pcf directory | Run `cd src/client/pcf && npm ci` |
| Flat config not loading | ESLint < 9 or wrong config file name | Ensure ESLint 9+ and file is named `eslint.config.mjs` |
| PA Checker rules producing false positives | Power Apps rules don't apply to all controls | Rules are set to `"off"` in base config; enable per-control if needed |

### C# Formatting and Linting (Roslyn Analyzers + dotnet format)

**What it does**: Enforces C# code formatting via EditorConfig rules and catches code quality issues via Roslyn analyzers.

**When it runs**: Pre-commit (via Husky/lint-staged for staged .cs files), PR (code-quality job), Step 9.5 quality gates.

**Configuration**: `.editorconfig` (formatting rules), `Directory.Build.props` (TreatWarningsAsErrors).

**Run Locally**:
```bash
# Check formatting without modifying files (same as CI)
dotnet format --verify-no-changes --verbosity diagnostic

# Auto-fix formatting
dotnet format

# Build with warnings as errors (catches Roslyn analyzer issues)
dotnet build --warnaserror
```

**Troubleshooting**:

| Issue | Cause | Fix |
|-------|-------|-----|
| "dotnet format" changes files unexpectedly | `.editorconfig` rules differ from IDE settings | Run `dotnet format` and commit the changes; ensure `.editorconfig` is authoritative |
| Build fails with CS8600 (nullable reference) | Nullable reference type analysis | Add null checks or use `!` operator with comment if safe |

### PSScriptAnalyzer (PowerShell Linting)

**What it does**: Analyzes PowerShell scripts for security issues, best practice violations, and code quality problems.

**When it runs**: Nightly quality pipeline (ai-code-review job indirectly reviews scripts). Can be run locally on demand.

**Configuration**: `PSScriptAnalyzerSettings.psd1` at repository root. Key rule categories:
- **Error severity** (security): `PSAvoidUsingPlainTextForPassword`, `PSAvoidUsingInvokeExpression`, credential-related rules
- **Warning severity** (best practice): `PSUseDeclaredVarsMoreThanAssignments`, `PSAvoidGlobalVars`, `PSAvoidUsingEmptyCatchBlock`, `PSAvoidUsingCmdletAliases`
- **Excluded**: `PSAvoidUsingWriteHost` (intentional in deployment scripts)

**Run Locally**:
```powershell
# Install PSScriptAnalyzer (if not already installed)
Install-Module -Name PSScriptAnalyzer -Scope CurrentUser -Force

# Analyze all scripts with project settings
Invoke-ScriptAnalyzer -Path scripts/ -Settings PSScriptAnalyzerSettings.psd1 -Recurse

# Analyze a specific script
Invoke-ScriptAnalyzer -Path scripts/Deploy-BffApi.ps1 -Settings PSScriptAnalyzerSettings.psd1

# Show only errors (security issues)
Invoke-ScriptAnalyzer -Path scripts/ -Settings PSScriptAnalyzerSettings.psd1 -Recurse -Severity Error
```

**How to interpret results**: Each finding shows the rule name, severity (Error/Warning/Information), file, line, and message. Error-severity findings (security issues) should be fixed immediately. Warning-severity findings should be addressed during remediation.

**Troubleshooting**:

| Issue | Cause | Fix |
|-------|-------|-----|
| "PSScriptAnalyzer module not found" | Module not installed | Run `Install-Module -Name PSScriptAnalyzer -Scope CurrentUser` |
| False positive on `Write-Host` | Rule not excluded | Verify `PSAvoidUsingWriteHost` is in `ExcludeRules` in `PSScriptAnalyzerSettings.psd1` |
| Too many warnings on legacy scripts | Scripts written before standards were established | Fix incrementally; focus on Error-severity findings first |

### SonarCloud (Deep Static Analysis)

**What it does**: Provides deep static analysis including code smells, bugs, vulnerabilities, security hotspots, and code coverage tracking. Includes AI Code Assurance for LLM-generated code detection.

**When it runs**: Nightly quality pipeline (sonarcloud-analysis job, depends on test-and-coverage for coverage data).

**Configuration**: `sonar-project.properties` at repository root. Key settings:
- **Project**: `spaarke-dev_spaarke` (organization: `spaarke-dev`)
- **Sources**: `src/` (excludes `src/solutions/`, `node_modules/`, `dist/`, `bin/`, `obj/`)
- **Coverage**: Reads OpenCover reports from `**/coverage.opencover.xml`
- **Duplicate detection**: Excludes test files

**Dashboard**: Access at `https://sonarcloud.io/project/overview?id=spaarke-dev_spaarke`

**Quality Gate Conditions** (default SonarCloud gate):
- New code coverage >= 80% (or project-configured threshold)
- No new bugs with severity > minor
- No new vulnerabilities
- No new security hotspots (reviewed)
- Code smells on new code within threshold

**Run Locally** (requires SonarCloud token):
```bash
# Install SonarScanner (if not already installed)
dotnet tool install --global dotnet-sonarscanner

# Begin analysis
dotnet sonarscanner begin /k:"spaarke-dev_spaarke" /o:"spaarke-dev" /d:sonar.token="$SONAR_TOKEN" /d:sonar.host.url="https://sonarcloud.io"

# Build
dotnet build

# End analysis (uploads to SonarCloud)
dotnet sonarscanner end /d:sonar.token="$SONAR_TOKEN"
```

**How to interpret results**: Visit the SonarCloud dashboard. The "Overall Code" tab shows cumulative metrics. The "New Code" tab shows metrics for code changed since the last analysis period. Focus on "New Code" metrics -- these reflect the quality of recent changes.

**Troubleshooting**:

| Issue | Cause | Fix |
|-------|-------|-----|
| "Coverage not updating" on dashboard | Coverage reports not uploaded or wrong path | Check `sonar.cs.opencover.reportsPaths` in `sonar-project.properties`; verify Coverlet generates OpenCover format |
| Quality gate stuck on "Computing" | Analysis still processing | Wait 2-3 minutes; SonarCloud processes asynchronously |
| "Project not found" error | Wrong project key or missing SONAR_TOKEN | Verify `sonar.projectKey` matches SonarCloud project; ensure `SONAR_TOKEN` secret is set in GitHub |
| Local scan fails with authentication error | Token expired or incorrect | Generate a new token at `https://sonarcloud.io/account/security` |

---

## PR Quality Gates (sdap-ci.yml)

The `sdap-ci.yml` workflow runs on every pull request and push to `master`. All jobs must pass before a PR can merge.

### CI Jobs

| Job | Runner | What It Checks | Blocking? |
|-----|--------|---------------|-----------|
| **Security Scan** | ubuntu-latest | Trivy filesystem vulnerability scan | Yes |
| **Build & Test** | windows-latest | `dotnet build`, `dotnet test` with coverage | Yes |
| **Client Quality** | ubuntu-latest | Prettier format check, ESLint strict check | Yes |
| **Code Quality** | ubuntu-latest | `dotnet format --verify-no-changes`, ADR architecture tests (NetArchTest), plugin size validation, dependency audit | Yes |
| **Integration Readiness** | windows-latest | Build for deployment, package artifacts, environment readiness | Yes |
| **ADR Violations Report** | ubuntu-latest | Parses NetArchTest results and comments on PR | Advisory |
| **CI Summary** | ubuntu-latest | Aggregates all job results into step summary | Advisory |

### Key Commands Run in CI

```bash
# Client Quality job
npx prettier --check .                    # Format verification
cd src/client/pcf && npx eslint .         # ESLint strict check

# Code Quality job
dotnet format --verify-no-changes         # C# format verification
dotnet test tests/Spaarke.ArchTests/      # ADR architecture tests (NetArchTest)
```

### Performance Target

The full PR pipeline MUST complete in **< 5 minutes**. The `client-quality` job runs in parallel with `build-test` and `security-scan` to stay within budget.

---

## Nightly Quality Pipeline

The `nightly-quality.yml` workflow runs a comprehensive quality sweep on weeknights (Mon-Fri, 6 AM UTC / midnight MST). For trigger details, manual dispatch options, and troubleshooting, see the [CI/CD Workflow Guide - Nightly Quality Workflow](ci-cd-workflow.md#nightly-quality-workflow).

### Jobs

| Job | Depends On | What It Does |
|-----|-----------|-------------|
| **test-and-coverage** | — | Full test suite with Coverlet coverage collection |
| **sonarcloud-analysis** | test-and-coverage | SonarCloud deep analysis (uses coverage artifacts) |
| **ai-code-review** | — | Claude Code headless review using `scripts/quality/nightly-review-prompt.md` |
| **dependency-audit** | — | `dotnet list --vulnerable` + `npm audit` |
| **report-results** | All above | Aggregates findings into a rolling GitHub issue (label: `nightly-quality`) |

### Performance Target

The nightly pipeline MUST complete in **< 15 minutes**.

### Manual Trigger

```bash
# Run nightly quality on-demand via GitHub CLI
gh workflow run nightly-quality.yml

# Run without SonarCloud
gh workflow run nightly-quality.yml -f run_sonarcloud=false

# Run without AI review
gh workflow run nightly-quality.yml -f run_ai_review=false
```

---

## Weekly Quality Summary

The `weekly-quality.yml` workflow runs every Friday at 10 PM UTC (4 PM MST). It aggregates metrics from the week's nightly runs into a trend table.

### Metrics Tracked

| Metric | Source |
|--------|--------|
| Test coverage % | `coverage.cobertura.xml` artifact |
| New violations count | `ai-review-results.json` artifact |
| TODO/FIXME count | `ai-review-results.json` artifact |
| Vulnerable dependency count | `dependency-audit-results` artifact |
| Build warnings | Test run exit status |

### Output

Creates/updates a GitHub issue labeled `weekly-quality-summary` with a trend table showing the week's quality trajectory.

---

## Quarterly Audit

Quarterly audits are a manual, comprehensive review of the entire quality system.

**Future:** A detailed quarterly audit runbook will be created as task 044 in the code-quality-and-assurance-r1 project. The runbook will cover:
- Full codebase audit against all ADRs
- Tool configuration review (are thresholds still appropriate?)
- Dependency health deep dive
- Quality trend analysis from weekly summaries
- Process improvement recommendations

---

## Claude Code Hooks

Claude Code hooks provide real-time quality feedback during AI-assisted development sessions. Configured in `.claude/settings.json`.

### Active Hooks

| Hook Event | Matcher | Script | Purpose |
|-----------|---------|--------|---------|
| `PostToolUse` | `Edit` | `scripts/quality/post-edit-lint.sh` | Runs lint checks after every file edit |
| `TaskCompleted` | (all) | `scripts/quality/task-quality-gate.sh` | Runs quality gate when a task completes |

### How They Work

- **PostToolUse (Edit)**: After Claude edits any file, the post-edit lint hook runs relevant linters on the changed file. This provides immediate feedback without waiting for Step 9.5.
- **TaskCompleted**: When Claude marks a task as complete, the quality gate hook runs a final check. This complements the Step 9.5 quality gates in the task-execute protocol.

---

## AI-Assisted PR Reviews

Two AI review tools provide automated PR feedback:

### CodeRabbit

- **Trigger**: Automatically reviews every PR
- **Focus**: Line-by-line code review, bug detection, style suggestions
- **Config**: `.coderabbit.yaml` (if configured)
- **Status**: Advisory (comments on PR, does not block merge)

### Claude Code Action

- **Trigger**: Automatically reviews every PR via GitHub Actions
- **Workflow**: `.github/workflows/claude-code-review.yml`
- **Focus**: Architecture-level review, ADR compliance, design patterns
- **Status**: Advisory (comments on PR, does not block merge)

Both tools complement each other: CodeRabbit focuses on code-level details while Claude Code Action provides higher-level architectural analysis.

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

## Module-Specific Test Guidance

This section defines **which tests to run when you modify code in a specific module**. Each subsection lists the relevant test projects, run commands, and what the tests validate.

### Test Project Inventory

| Test Project | Path | Framework | Covers |
|-------------|------|-----------|--------|
| **Sprk.Bff.Api.Tests** | `tests/unit/Sprk.Bff.Api.Tests/` | xUnit + NSubstitute + Moq + FluentAssertions + WireMock.Net | BFF API endpoints, services, filters, infrastructure |
| **Spaarke.Core.Tests** | `tests/unit/Spaarke.Core.Tests/` | xUnit + FluentAssertions | Shared .NET library (Spaarke.Core) |
| **Spaarke.Plugins.Tests** | `tests/unit/Spaarke.Plugins.Tests/` | xUnit + FluentAssertions + Moq + CRM SDK | Dataverse plugin validation and projection logic |
| **Spe.Integration.Tests** | `tests/integration/Spe.Integration.Tests/` | xUnit + FluentAssertions + Moq + Mvc.Testing | End-to-end API integration (auth, AI, reporting, RAG) |
| **Spaarke.ArchTests** | `tests/Spaarke.ArchTests/` | xUnit + NetArchTest.Rules | ADR compliance via architecture reflection tests |
| **E2E (Playwright)** | `tests/e2e/` | Playwright + TypeScript | Browser-based PCF control and add-in testing |

### For BFF API Endpoints

**When you modify**: `src/server/api/Sprk.Bff.Api/Api/` (endpoint definitions, route handlers)

**Run these tests**:
```bash
# Unit tests for endpoint logic
dotnet test tests/unit/Sprk.Bff.Api.Tests/ --filter "FullyQualifiedName~Api"

# Integration tests for endpoint behavior (auth, HTTP pipeline)
dotnet test tests/integration/Spe.Integration.Tests/

# Architecture tests (ADR-001 Minimal API, ADR-008 endpoint filters)
dotnet test tests/Spaarke.ArchTests/
```

**Key test files**:
- `tests/unit/Sprk.Bff.Api.Tests/EndpointGroupingTests.cs` -- endpoint registration and grouping
- `tests/unit/Sprk.Bff.Api.Tests/HealthAndHeadersTests.cs` -- health check and response headers
- `tests/unit/Sprk.Bff.Api.Tests/AuthorizationTests.cs` -- auth filter behavior
- `tests/unit/Sprk.Bff.Api.Tests/CorsAndAuthTests.cs` -- CORS and authentication
- `tests/unit/Sprk.Bff.Api.Tests/Api/` -- endpoint-specific tests (Agent, AI, ExternalAccess, Reporting, etc.)
- `tests/integration/Spe.Integration.Tests/AuthorizationIntegrationTests.cs` -- authorization end-to-end

**ADR constraints validated**: ADR-001 (Minimal API), ADR-008 (endpoint filters for auth), ADR-010 (DI minimalism)

### For AI Pipeline

**When you modify**: `src/server/api/Sprk.Bff.Api/Services/Ai/` (orchestration, tools, playbooks, RAG, semantic search)

**Run these tests**:
```bash
# Unit tests for AI services (60+ test files)
dotnet test tests/unit/Sprk.Bff.Api.Tests/ --filter "FullyQualifiedName~Services.Ai"

# Integration tests for AI endpoints and tool framework
dotnet test tests/integration/Spe.Integration.Tests/ --filter "FullyQualifiedName~Analysis|FullyQualifiedName~ToolFramework|FullyQualifiedName~Playbook|FullyQualifiedName~Rag|FullyQualifiedName~SemanticSearch"
```

**Key test areas** (under `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/`):
- `AnalysisOrchestrationServiceTests.cs` -- core AI orchestration
- `PlaybookExecutionEngineTests.cs` -- playbook engine execution
- `PlaybookOrchestrationServiceTests.cs` -- playbook scheduling and dispatch
- `Chat/` -- chat agent, context resolution, session management, streaming, middleware
- `Chat/Tools/` -- tool handler tests (document search, web search, working document, analysis execution)
- `Nodes/` -- execution graph node executors (AI analysis, condition, email, task, notification, record update)
- `Tools/` -- Dataverse update tool, communication tool handlers
- `Rag*.cs` -- RAG indexing pipeline, query builder, service
- `SemanticSearch/` -- search filter builder, semantic search service
- `Visualization/` -- visualization service rendering
- `PromptSchema*.cs` -- prompt template rendering and override merging

**Integration test files**:
- `tests/integration/Spe.Integration.Tests/AnalysisEndpointsIntegrationTests.cs` -- analysis streaming and enqueue
- `tests/integration/Spe.Integration.Tests/ToolFrameworkIntegrationTests.cs` -- tool handler resolution
- `tests/integration/Spe.Integration.Tests/PlaybookExecutionIntegrationTests.cs` -- playbook end-to-end
- `tests/integration/Spe.Integration.Tests/RagDedicatedDeploymentTests.cs` -- RAG with dedicated index
- `tests/integration/Spe.Integration.Tests/RagSharedDeploymentTests.cs` -- RAG with shared index
- `tests/integration/Spe.Integration.Tests/SemanticSearch/` -- semantic search integration
- `tests/integration/Spe.Integration.Tests/Api/Ai/` -- chat endpoints, knowledge base, re-analysis, upload integration

**ADR constraints validated**: ADR-013 (AI tool framework; extend BFF, not separate service)

### For PCF Controls

**When you modify**: `src/client/pcf/{ControlName}/` (TypeScript/React PCF controls)

**Run these tests**:
```bash
# Lint and format check (mandatory before commit)
cd src/client/pcf && npx eslint . --max-warnings 0
npx prettier --check "src/client/pcf/**/*.{ts,tsx}"

# E2E tests for specific controls (requires Playwright and environment setup)
cd tests/e2e && npx playwright test --project=edge specs/spe-file-viewer/
cd tests/e2e && npx playwright test --project=edge specs/universal-dataset-grid/
```

**Available E2E test specs** (under `tests/e2e/specs/`):
- `spe-file-viewer/` -- SpeFileViewer control (document viewing)
- `universal-dataset-grid/` -- UniversalDatasetGrid control (dataset display)
- `outlook-addins/` -- Outlook add-in save and share flows
- `word-addins/` -- Word add-in task pane
- `secure-project/` -- Secure project access, invitation, revocation, closure
- `secure-project-creation/` -- Secure project creation flow
- `quickcreate-flow.spec.ts` -- Quick create dialog

**Page objects** (under `tests/e2e/pages/`):
- `BasePCFPage.ts` -- base page for all PCF control tests
- `controls/SpeFileViewerPage.ts` -- SpeFileViewer page object
- `controls/UniversalDatasetGridPage.ts` -- UniversalDatasetGrid page object
- `addins/OutlookTaskPanePage.ts`, `WordTaskPanePage.ts` -- Office add-in page objects

**Playwright configuration**: `tests/e2e/config/playwright.config.ts` (Edge primary, 60s timeout, Power Apps URLs)

**PCF controls in the codebase**: AIMetadataExtractor, AssociationResolver, DocumentRelationshipViewer, DrillThroughWorkspace, EmailProcessingMonitor, RelatedDocumentCount, ScopeConfigEditor, SemanticSearchControl, SpaarkeGridCustomizer, ThemeEnforcer, UniversalDatasetGrid, UniversalQuickCreate, UpdateRelatedButton, VisualHost

**ADR constraints validated**: ADR-006 (PCF over webresources), ADR-012 (shared component library), ADR-021 (Fluent UI v9, dark mode), ADR-022 (PCF uses React 16 platform libraries)

### For Dataverse Plugins

**When you modify**: `src/server/plugins/Spaarke.Plugins/` (Dataverse plugin classes)

**Run these tests**:
```bash
# Plugin unit tests (validation and projection logic)
dotnet test tests/unit/Spaarke.Plugins.Tests/

# Architecture tests (ADR-002 thin plugin constraints)
dotnet test tests/Spaarke.ArchTests/ --filter "DisplayName~ADR-002"
```

**Key test files**:
- `tests/unit/Spaarke.Plugins.Tests/ValidationPluginTests.cs` -- field validation logic
- `tests/unit/Spaarke.Plugins.Tests/ProjectionPluginTests.cs` -- projection/mapping logic
- `tests/Spaarke.ArchTests/ADR002_PluginTests.cs` -- enforces no HTTP/Graph calls, plugin size constraints

**ADR constraints validated**: ADR-002 (thin plugins, <50ms, no HTTP/Graph calls)

### For Shared Libraries (Spaarke.Core, Spaarke.Dataverse)

**When you modify**: `src/server/shared/Spaarke.Core/` or `src/server/shared/Spaarke.Dataverse/`

**Run these tests**:
```bash
# Core library unit tests
dotnet test tests/unit/Spaarke.Core.Tests/

# BFF API tests (depend on Core and Dataverse libraries)
dotnet test tests/unit/Sprk.Bff.Api.Tests/

# Architecture tests (validate DI and Graph isolation)
dotnet test tests/Spaarke.ArchTests/
```

**Key test files**:
- `tests/unit/Spaarke.Core.Tests/DesktopUrlBuilderTests.cs` -- URL construction utilities
- `tests/Spaarke.ArchTests/ADR007_GraphIsolationTests.cs` -- Graph SDK types don't leak above facade
- `tests/Spaarke.ArchTests/ADR009_CachingTests.cs` -- Redis-first caching

**ADR constraints validated**: ADR-007 (Graph isolation in facade), ADR-009 (Redis-first caching), ADR-010 (DI minimalism)

### For Infrastructure and Auth

**When you modify**: `src/server/api/Sprk.Bff.Api/Infrastructure/` (DI modules, Graph auth, resilience, streaming)

**Run these tests**:
```bash
# Infrastructure-specific unit tests
dotnet test tests/unit/Sprk.Bff.Api.Tests/ --filter "FullyQualifiedName~Infrastructure"

# Auth and DI architecture tests
dotnet test tests/Spaarke.ArchTests/ --filter "DisplayName~ADR-008|DisplayName~ADR-010"

# Integration tests (auth pipeline, CORS)
dotnet test tests/integration/Spe.Integration.Tests/ --filter "FullyQualifiedName~Authorization"
```

**Key test files**:
- `tests/unit/Sprk.Bff.Api.Tests/Infrastructure/DataverseWebApiThreadSafetyTests.cs` -- thread safety
- `tests/unit/Sprk.Bff.Api.Tests/Infrastructure/Resilience/` -- circuit breaker and retry policies
- `tests/unit/Sprk.Bff.Api.Tests/Infrastructure/Streaming/` -- SSE streaming
- `tests/unit/Sprk.Bff.Api.Tests/Infrastructure/Json/` -- JSON serialization
- `tests/Spaarke.ArchTests/ADR008_AuthorizationTests.cs` -- endpoint filter auth enforcement
- `tests/Spaarke.ArchTests/ADR010_DITests.cs` -- DI registration patterns, singleton enforcement, options POCO validation

### For Background Jobs and Workers

**When you modify**: `src/server/api/Sprk.Bff.Api/Services/Jobs/` or `src/server/api/Sprk.Bff.Api/BackgroundServices/`

**Run these tests**:
```bash
# Job processor and worker tests
dotnet test tests/unit/Sprk.Bff.Api.Tests/ --filter "FullyQualifiedName~Job|FullyQualifiedName~Worker"
```

**Key test files**:
- `tests/unit/Sprk.Bff.Api.Tests/Services/Jobs/` -- job processing logic
- `tests/unit/Sprk.Bff.Api.Tests/Workers/` -- background worker tests

---

## Test Selection Matrix

Given a changed file path, this matrix determines which test projects to execute.

| Changed File Pattern | Unit Tests | Integration Tests | Arch Tests | E2E Tests |
|---------------------|-----------|-------------------|-----------|-----------|
| `src/server/api/Sprk.Bff.Api/Api/**` | `Sprk.Bff.Api.Tests` (filter: `Api`) | `Spe.Integration.Tests` | `Spaarke.ArchTests` | -- |
| `src/server/api/Sprk.Bff.Api/Services/Ai/**` | `Sprk.Bff.Api.Tests` (filter: `Services.Ai`) | `Spe.Integration.Tests` (filter: `Analysis\|ToolFramework\|Playbook\|Rag\|SemanticSearch`) | -- | -- |
| `src/server/api/Sprk.Bff.Api/Services/**` (non-AI) | `Sprk.Bff.Api.Tests` (filter: `Services`) | `Spe.Integration.Tests` | -- | -- |
| `src/server/api/Sprk.Bff.Api/Infrastructure/**` | `Sprk.Bff.Api.Tests` (filter: `Infrastructure`) | `Spe.Integration.Tests` (filter: `Authorization`) | `Spaarke.ArchTests` (filter: `ADR-008\|ADR-010`) | -- |
| `src/server/api/Sprk.Bff.Api/Program.cs` | `Sprk.Bff.Api.Tests` | `Spe.Integration.Tests` | `Spaarke.ArchTests` | -- |
| `src/server/shared/Spaarke.Core/**` | `Spaarke.Core.Tests` + `Sprk.Bff.Api.Tests` | -- | `Spaarke.ArchTests` (filter: `ADR-007\|ADR-009`) | -- |
| `src/server/shared/Spaarke.Dataverse/**` | `Sprk.Bff.Api.Tests` | `Spe.Integration.Tests` | `Spaarke.ArchTests` (filter: `ADR-007`) | -- |
| `src/server/plugins/Spaarke.Plugins/**` | `Spaarke.Plugins.Tests` | -- | `Spaarke.ArchTests` (filter: `ADR-002`) | -- |
| `src/client/pcf/**` | -- | -- | -- | `tests/e2e/` (matching control spec) |
| `src/client/code-pages/**` | -- | -- | -- | -- (manual or UI test via Step 9.7) |
| `src/client/shared/**` | -- | -- | -- | `tests/e2e/` (all control specs, since shared components affect all) |

### How to Use This Matrix

1. Identify which files your task modifies (from `current-task.md` or `git diff`)
2. Look up each file pattern in the matrix
3. Run the **union** of all required test projects
4. When in doubt, run the full suite: `dotnet test`

### Quick Reference Commands

```bash
# Run ALL .NET tests (safest option)
dotnet test

# Run all tests with coverage collection
dotnet test --collect:"XPlat Code Coverage" --settings config/coverlet.runsettings

# Run a specific test project
dotnet test tests/unit/Sprk.Bff.Api.Tests/
dotnet test tests/unit/Spaarke.Core.Tests/
dotnet test tests/unit/Spaarke.Plugins.Tests/
dotnet test tests/integration/Spe.Integration.Tests/
dotnet test tests/Spaarke.ArchTests/

# Run tests matching a filter
dotnet test --filter "FullyQualifiedName~Services.Ai"
dotnet test --filter "DisplayName~ADR-002"

# Run E2E tests (requires Playwright setup)
cd tests/e2e && npx playwright test --project=edge

# Run E2E for a specific control
cd tests/e2e && npx playwright test --project=edge specs/universal-dataset-grid/

# Generate coverage report
dotnet test --collect:"XPlat Code Coverage" --settings config/coverlet.runsettings
reportgenerator -reports:**/coverage.cobertura.xml -targetdir:coverage-report
```

---

## Coverage Targets

Coverage is collected via Coverlet (configured in `config/coverlet.runsettings`). The `Include` filter covers: `[Sprk.Bff.Api]*`, `[Spaarke.Plugins]*`, `[Spaarke.Core]*`, `[Spaarke.Dataverse]*`. Test assemblies and `Program.cs` are excluded.

### Per-Module Coverage Targets

| Module | Source Path | Target | Rationale |
|--------|-----------|--------|-----------|
| **Core services** (SpeFileStore, auth, caching) | `src/server/shared/Spaarke.Core/` | **80%+** | Critical shared infrastructure; high reuse surface |
| **BFF API endpoints** | `src/server/api/Sprk.Bff.Api/Api/` | **70%+** | Route handlers; integration tests cover remaining paths |
| **AI pipeline services** | `src/server/api/Sprk.Bff.Api/Services/Ai/` | **70%+** | Complex orchestration; some paths require live AI services |
| **Dataverse plugins** | `src/server/plugins/Spaarke.Plugins/` | **80%+** | Thin, testable logic; must be exhaustively validated |
| **Infrastructure** (DI, auth, resilience) | `src/server/api/Sprk.Bff.Api/Infrastructure/` | **60%+** | Framework glue code; some paths only exercised at runtime |
| **Dataverse abstractions** | `src/server/shared/Spaarke.Dataverse/` | **60%+** | Thin wrappers over SDK; mocking limited by CRM SDK |
| **Utility/helper classes** | (scattered) | **90%+** | Pure functions; easy to test exhaustively |
| **PCF controls** (TypeScript) | `src/client/pcf/` | **Lint + E2E** | No unit test framework configured; quality enforced via ESLint strict mode + Playwright E2E |
| **Code Pages** (React 18) | `src/client/code-pages/` | **Lint + manual** | Quality enforced via ESLint/Prettier + UI testing (Step 9.7) |

### Checking Coverage

```bash
# Run tests with coverage
dotnet test --collect:"XPlat Code Coverage" --settings config/coverlet.runsettings

# Generate HTML report
reportgenerator -reports:**/coverage.cobertura.xml -targetdir:coverage-report

# Open report
start coverage-report/index.html
```

SonarCloud tracks coverage trends automatically during nightly runs. Dashboard: `https://sonarcloud.io/project/overview?id=spaarke-dev_spaarke`

---

## Architecture Test Enforcement

Architecture tests (`tests/Spaarke.ArchTests/`) use **NetArchTest.Rules** to enforce ADR compliance at build time. These tests run in CI (code-quality job) and block PR merges on failure.

### Architecture Test Inventory

| Test File | ADR | What It Enforces |
|-----------|-----|-----------------|
| `ADR001_MinimalApiTests.cs` | ADR-001 | No Azure Functions packages or attributes; Minimal API + BackgroundService only |
| `ADR002_PluginTests.cs` | ADR-002 | Plugin assembly has no HTTP/Graph dependencies; plugins stay thin |
| `ADR007_GraphIsolationTests.cs` | ADR-007 | Graph SDK types do not leak above the SpeFileStore facade layer |
| `ADR008_AuthorizationTests.cs` | ADR-008 | Authorization uses endpoint filters, not global middleware |
| `ADR009_CachingTests.cs` | ADR-009 | Redis-first caching; no hybrid L1 cache without profiling justification |
| `ADR010_DITests.cs` | ADR-010 | Expensive resources are Singleton; 1:1 interface ceiling enforced; feature modules use extension methods; Options classes are POCOs |

### How Architecture Tests Work

Architecture tests reflect over compiled assemblies (primarily `Sprk.Bff.Api.dll`) and validate structural constraints:

1. **Dependency checks** -- verify that forbidden namespaces are not referenced (e.g., no `Microsoft.Azure.WebJobs` per ADR-001)
2. **Type checks** -- verify type relationships (e.g., no 1:1 interfaces growing unchecked per ADR-010)
3. **Source scanning** -- some tests read source files to validate patterns (e.g., DI registration lifetimes in ADR-010)
4. **Ceiling assertions** -- some constraints use ceiling values that must not increase (e.g., `knownOneToOneCeiling = 76` for 1:1 interfaces)

### When Architecture Tests Fail

| Failure | Meaning | Resolution |
|---------|---------|------------|
| "Found dependency on forbidden namespace" | Code references a banned package | Remove the dependency; use the ADR-approved alternative |
| "1:1 interface mapping count increased" | New interface added without justification | Either register concrete per ADR-010 or update the ceiling with documentation |
| "Expensive resource registered as Scoped/Transient" | Singleton-required service has wrong lifetime | Change to `AddSingleton<>()` in DI module |
| "Directly instantiates HttpClient" | Bypasses IHttpClientFactory | Inject `IHttpClientFactory` instead of `new HttpClient()` |

### Running Architecture Tests

```bash
# Run all architecture tests
dotnet test tests/Spaarke.ArchTests/

# Run tests for a specific ADR
dotnet test tests/Spaarke.ArchTests/ --filter "DisplayName~ADR-001"
dotnet test tests/Spaarke.ArchTests/ --filter "DisplayName~ADR-002"
dotnet test tests/Spaarke.ArchTests/ --filter "DisplayName~ADR-010"

# Run as part of CI code-quality check
dotnet test tests/Spaarke.ArchTests/ --logger "trx;LogFileName=arch-test-results.trx"
```

### Adding New Architecture Tests

When a new ADR is created that has enforceable structural constraints:

1. Create `tests/Spaarke.ArchTests/ADR{NNN}_{Name}Tests.cs`
2. Reference the BFF API assembly: `typeof(Program).Assembly`
3. Use `NetArchTest.Rules.Types.InAssembly()` for dependency and type checks
4. Use source-file scanning for pattern checks (see `ADR010_DITests.cs` for example)
5. Add the test to the inventory table above
6. Architecture tests run in CI automatically -- no workflow changes needed

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

- [CI/CD Workflow Guide](ci-cd-workflow.md) - GitHub Actions, commits, PRs, deployments
- [Code Quality Onboarding Guide](../guides/code-quality-onboarding.md) - **Future:** Quick-start guide for new developers (task 042)
- [Parallel Claude Code Sessions](parallel-claude-sessions.md) - Multi-session workflow
- [Context Recovery Procedure](context-recovery.md) - Resuming work
- [code-review Skill](../../.claude/skills/code-review/SKILL.md) - Full skill documentation
- [ui-test Skill](../../.claude/skills/ui-test/SKILL.md) - Browser testing details
- [repo-cleanup Skill](../../.claude/skills/repo-cleanup/SKILL.md) - Cleanup procedures
- [ci-cd Skill](../../.claude/skills/ci-cd/SKILL.md) - CI/CD pipeline management

---

*Last updated: April 5, 2026*
