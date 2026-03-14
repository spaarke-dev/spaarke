# Code Quality and Assurance R1 — Design Document

> **Version**: 1.0
> **Author**: Ralph Schroeder + Claude Code
> **Created**: 2026-03-11
> **Status**: Draft

---

## 1. Problem Statement

Spaarke's codebase is 100% AI-assisted (Claude Code). While we have strong architectural governance (ADRs, NetArchTest, skills), we lack systematic quality assurance at the code level. The known criticisms of AI-generated code — over-abstraction, unnecessary verbosity, brittle patterns, defensive overkill — are real risks in our codebase.

**Current state**:
- 525 C# files in BFF API, 955 TypeScript files in PCF/Code Pages, 114 PowerShell scripts
- 96 TODO/FIXME comments across 35 files (concentrated in Office/Auth modules)
- Program.cs at 1,940 lines (monolithic DI configuration)
- Endpoint files ranging from 223 to 1,265 lines with no size enforcement
- ESLint rules mostly set to WARN or OFF (no-unused-vars disabled)
- No pre-commit hooks, no Prettier, no code coverage thresholds
- CI tests marked `continue-on-error: true` (don't block PRs)
- No nightly quality automation
- No AI-powered code review on PRs

**Goal**: Establish a code quality baseline, remediate critical gaps, and implement an ongoing quality assurance system that runs autonomously — so the codebase would earn an A+ from an independent code audit.

---

## 2. Design Principles

1. **Automate ruthlessly, but be pragmatic** — Not every PR needs a 30-minute quality pipeline. Layer checks by cost and value.
2. **The audit informs the system** — Initial audit findings directly shape what the ongoing system checks for.
3. **AI reviews AI** — Use AI agents to review AI-generated code, but with structured prompts grounded in our ADRs and conventions.
4. **Nightly deep, instant shallow** — Fast checks on PR (seconds), deep analysis nightly (minutes), comprehensive audit quarterly.
5. **Quality is measurable** — Track metrics over time. If we can't measure it, we can't claim it improved.

---

## 3. Architecture Overview

```
┌─────────────────────────────────────────────────────────────────────┐
│                    CODE QUALITY ASSURANCE SYSTEM                     │
├─────────────────────────────────────────────────────────────────────┤
│                                                                     │
│  LAYER 1: PR-TIME (seconds)           LAYER 2: NIGHTLY (minutes)   │
│  ┌─────────────────────────┐          ┌──────────────────────────┐ │
│  │ GitHub Actions on PR    │          │ Scheduled GitHub Actions │ │
│  │ ─────────────────────── │          │ ──────────────────────── │ │
│  │ • dotnet build -w.a.e.  │          │ • Claude Code headless   │ │
│  │ • dotnet format verify  │          │   - Dead code detection  │ │
│  │ • ESLint (strict)       │          │   - Pattern consistency  │ │
│  │ • Architecture tests    │          │   - ADR compliance sweep │ │
│  │ • CodeRabbit AI review  │          │   - TODO/FIXME aging     │ │
│  │ • Dependency vuln check │          │ • SonarCloud analysis    │ │
│  │                         │          │ • Coverage trend report  │ │
│  │ Target: < 5 min total   │          │ • Issue auto-creation    │ │
│  └─────────────────────────┘          │                          │ │
│                                       │ Target: < 15 min total   │ │
│  LAYER 3: ON-DEMAND                   └──────────────────────────┘ │
│  ┌─────────────────────────┐                                       │
│  │ Claude Code Skills      │          LAYER 4: QUARTERLY           │
│  │ ─────────────────────── │          ┌──────────────────────────┐ │
│  │ • /code-review (manual) │          │ Comprehensive Audit      │ │
│  │ • /adr-check (manual)   │          │ ──────────────────────── │ │
│  │ • task-execute Step 9.5 │          │ • Full codebase sweep    │ │
│  │   (auto quality gate)   │          │ • Metric comparison      │ │
│  │ • Claude Code hooks     │          │ • Tech debt inventory    │ │
│  │   (post-edit lint)      │          │ • Dependency audit       │ │
│  └─────────────────────────┘          │ • Architecture review    │ │
│                                       └──────────────────────────┘ │
├─────────────────────────────────────────────────────────────────────┤
│  METRICS DASHBOARD                                                  │
│  • Code coverage trend  • ADR violation count  • TODO/FIXME count  │
│  • PR review time       • Build success rate   • Dependency health │
└─────────────────────────────────────────────────────────────────────┘
```

---

## 4. Domain A: Initial Code Quality Audit

### Purpose
Baseline the current codebase quality, identify and remediate critical issues, and produce findings that inform the ongoing quality system.

### Audit Scope

| Area | Files | Key Concerns |
|------|-------|--------------|
| **BFF API (.NET)** | ~525 .cs files | Program.cs bloat (1,940 lines), endpoint size variance, 96 TODOs, defensive over-engineering |
| **PCF Controls (TypeScript)** | ~955 .ts/.tsx files | Disabled lint rules, no formatting enforcement, component size outliers |
| **PowerShell Scripts** | ~114 .ps1 files | No PSScriptAnalyzer, duplicated auth patterns, no centralized utilities |
| **Shared Libraries** | src/server/shared/, src/client/shared/ | Interface-to-implementation ratio, unused abstractions |
| **Test Suite** | ~206 test files | Coverage gaps, test quality (testing behavior vs implementation) |
| **Configuration** | .editorconfig, tsconfig, ESLint, Directory.Build.props | Consistency across projects, rule strictness |

### Audit Checklist (per area)

1. **Dead code elimination** — Unused classes, methods, imports, files, variables
2. **Abstraction audit** — Interfaces with single implementations (ADR-010 compliance), unused generics, premature factories
3. **Pattern consistency** — Do all endpoints follow the same structure? All PCF controls? All services?
4. **Error handling audit** — Same error caught/logged multiple times? Defensive checks on non-nullable types?
5. **Comment quality** — Remove comments that restate code. Keep "why" comments. Resolve or track TODOs.
6. **Size analysis** — Files over 500 lines flagged for review. Methods over 50 lines flagged.
7. **Naming audit** — Consistency with conventions in .editorconfig and CLAUDE.md
8. **Test quality** — Tests that test implementation details vs behavior. Missing edge case coverage.
9. **Dependency health** — Outdated packages, known vulnerabilities (MimeKit 4.14.0), pre-release dependencies
10. **Script consistency** — Error handling patterns, parameter documentation, idempotency

### Audit Deliverables

- **Audit report** per area (findings, severity, remediation)
- **Remediation tasks** created as POML files for execution
- **Quality metrics baseline** (coverage %, TODO count, lint violations, dependency health)
- **"Code Quality Scorecard"** — overall grade per area (A-F scale)

---

## 5. Domain B: PR-Time Quality Checks (Layer 1)

### Design Constraint
Full CI/CD pipeline is not practical for every PR — it takes too long and creates friction. PR-time checks must complete in **under 5 minutes total**.

### PR Quality Pipeline

```yaml
# Runs on: pull_request (opened, synchronize)
# Target: < 5 minutes total

jobs:
  # Job 1: Fast static checks (< 2 min)
  static-checks:
    - dotnet build -warnaserror          # C# compilation + Roslyn analyzers
    - dotnet format --verify-no-changes  # Formatting compliance
    - npm run lint                       # ESLint (PCF controls)
    - dotnet test Spaarke.ArchTests      # ADR architecture tests

  # Job 2: AI-assisted review (< 3 min, parallel)
  ai-review:
    - CodeRabbit automatic review        # Line-by-line AI comments
    # OR
    - anthropics/claude-code-action      # Claude-powered review with CLAUDE.md context
```

### Tool Selection: CodeRabbit vs Claude Code Action

| Capability | CodeRabbit | Claude Code Action |
|-----------|------------|-------------------|
| PR review comments | Line-by-line, automatic | Configurable via prompt |
| Context awareness | Code graph analysis | Full repo + CLAUDE.md + ADRs |
| Learning | Adapts from dismissed comments | No learning (prompt-based) |
| Custom rules | Organization rules | System prompt with our ADRs |
| Cost | Free (OSS) / paid (private) | Anthropic API usage |
| Setup effort | 2-click GitHub install | Workflow YAML + API key |
| Our ADR awareness | Requires rule configuration | Native — reads our CLAUDE.md |

**Recommendation**: Use **both**. CodeRabbit for always-on line-by-line review (low effort, learns from team). Claude Code Action for architecture-aware review using our CLAUDE.md and ADRs as system prompt.

### What NOT to Run on PR

- Full test suite with coverage (run nightly)
- SonarCloud deep analysis (run nightly)
- Dead code detection (run nightly)
- Performance profiling (run on-demand)

---

## 6. Domain C: Nightly Quality Automation (Layer 2)

### Purpose
Deep quality analysis that runs while the team is not working. Reviews both open PRs and recently merged code. Creates GitHub issues for findings.

### Nightly Pipeline Design

```yaml
name: Nightly Code Quality
on:
  schedule:
    - cron: '0 6 * * 1-5'  # 6 AM UTC (midnight MST) weeknights
  workflow_dispatch:         # Manual trigger

jobs:
  # Job 1: Full test suite with coverage
  test-coverage:
    - dotnet test --collect:"XPlat Code Coverage"
    - Generate coverage report
    - Compare against previous night's baseline
    - Flag if coverage dropped > 2%

  # Job 2: SonarCloud analysis
  sonar-analysis:
    - Full SonarCloud scan (quality gate, security, duplication)
    - AI Code Assurance (detect AI-generated code patterns)

  # Job 3: Claude Code deep review (headless)
  ai-deep-review:
    - claude -p "{comprehensive review prompt}" --output-format json
    - Analyze recent commits (last 24h or since last run)
    - Check for:
      - Dead code and unused imports
      - Pattern inconsistencies across similar files
      - ADR compliance drift
      - TODO/FIXME items older than 30 days
      - Over-abstraction (interfaces with single implementations)
      - Error handling anti-patterns
      - Comment quality (restating code)
    - Create/update GitHub issue with findings

  # Job 4: Dependency health
  dependency-audit:
    - dotnet list --vulnerable
    - npm audit (across all PCF projects)
    - Flag new vulnerabilities since last run
```

### Claude Code Nightly Review Prompt

The nightly review uses Claude Code headless mode with a comprehensive prompt that references our CLAUDE.md, ADRs, and conventions. The prompt is stored as a file in the repository so it can evolve with the codebase.

**Key prompt sections:**
1. **Recent changes review** — `git log --since="24 hours ago"` → review each changed file
2. **Pattern consistency** — Compare similar files (all endpoint files, all PCF controls) for structural consistency
3. **ADR compliance** — Full sweep against all 22 ADRs (not just the 6 in NetArchTest)
4. **Technical debt tracking** — Count and categorize TODO/FIXME, flag items > 30 days old
5. **Dead code detection** — Unused exports, unreferenced files, orphaned test fixtures
6. **Output format** — Structured JSON that the workflow parses into a GitHub issue

### Issue Management

- Nightly findings create/update a single rolling GitHub issue: "Nightly Code Quality Report — {date}"
- Critical findings create separate issues with `code-quality` label
- Findings are deduplicated across runs (don't re-report known issues)
- Resolved items are automatically removed from the next report

---

## 7. Domain D: Claude Code Quality Hooks and Skills

### Claude Code Hooks (`.claude/settings.json`)

```json
{
  "hooks": {
    "PostToolUse": [
      {
        "matcher": "Edit",
        "command": "scripts/quality/post-edit-lint.sh",
        "description": "Run linter on edited files"
      }
    ],
    "TaskCompleted": [
      {
        "command": "scripts/quality/task-quality-gate.sh",
        "description": "Run quality checks after task completion"
      }
    ]
  }
}
```

**Post-edit hook**: After any file edit, run the appropriate linter:
- `.cs` files → `dotnet format --include {file}`
- `.ts/.tsx` files → `npx eslint {file}`
- `.ps1` files → `Invoke-ScriptAnalyzer -Path {file}`

**Task-completed hook**: After a task finishes, run a lightweight quality check:
- Build verification (`dotnet build -warnaserror`)
- Changed file linting
- Architecture test (if code files changed)

### Enhanced Code Review Skill

Update the existing `/code-review` skill to include:
- **Quantitative metrics** alongside qualitative review (file size, method count, cyclomatic complexity)
- **Before/after comparison** — did this change improve or worsen quality?
- **Specific AI code smell detection**:
  - Interfaces with single implementations
  - Try/catch blocks that only log and rethrow
  - Null checks on non-nullable types
  - Comments that restate the code
  - Methods that do too many things (> 3 responsibilities)

---

## 8. Domain E: Multi-Model Quality Strategy

### The Problem with AI Reviewing Its Own Code
Claude Code writes 100% of our code. Claude Code also runs our code review skill. This creates a blind spot — the same model may not catch its own systematic biases.

### Multi-Perspective Review Strategy

| Perspective | Model/Tool | Focus | When |
|-------------|-----------|-------|------|
| **Line-by-line review** | CodeRabbit | Bugs, style, security | Every PR |
| **Architecture review** | Claude Code (Opus) | ADR compliance, patterns, design | Every PR + nightly |
| **Static analysis** | SonarCloud | Duplication, complexity, security (SAST) | Nightly |
| **Dependency security** | Trivy + npm audit + dotnet list | Vulnerabilities | PR + nightly |
| **Human oversight** | Developer review | Architecture-level sense check | PR approval |

### Why Multiple Tools Matter

- **CodeRabbit** catches bugs Claude Code may miss (different training, different approach)
- **SonarCloud** catches rules-based issues (duplication, complexity thresholds) that AI reviewers often skip
- **Trivy** catches security vulnerabilities in dependencies
- **Claude Code** catches architecture-level issues other tools can't understand (ADR compliance, domain-specific patterns)

Together they cover:
- Logic errors (CodeRabbit + Claude)
- Security (SonarCloud SAST + Trivy + CodeRabbit)
- Style/consistency (ESLint + dotnet format + SonarCloud)
- Architecture (Claude + NetArchTest)
- Dependencies (Trivy + npm audit + dotnet list --vulnerable)

### Future Consideration: Agent Teams for Code Review

When Claude Code agent teams mature, consider:
- Spawn 3 teammates with different review personas (security, performance, architecture)
- Each reviews the same PR independently
- Lead synthesizes findings and deduplicates
- This mimics a human code review panel

---

## 9. Domain F: Quality Metrics and Reporting

### Metrics to Track

| Metric | Source | Baseline (audit) | Target |
|--------|--------|-------------------|--------|
| **Code coverage (C#)** | Coverlet + CI | TBD (audit) | ≥ 70% new code, ≥ 50% overall |
| **ADR violation count** | NetArchTest + nightly | TBD (audit) | 0 violations |
| **TODO/FIXME count** | Nightly grep | 96 known | < 20 (remainder tracked as issues) |
| **Lint violations** | ESLint + dotnet format | TBD (audit) | 0 in new code |
| **Dependency vulnerabilities** | Trivy + npm audit | MimeKit known | 0 critical/high |
| **File size outliers** | Nightly analysis | Program.cs 1,940 lines | No file > 500 lines without justification |
| **PR review time** | GitHub metrics | N/A | < 24h for AI review |
| **Build success rate** | GitHub Actions | TBD | > 95% |
| **SonarCloud quality gate** | SonarCloud | TBD | Pass |

### Reporting

- **Weekly summary** — Auto-generated GitHub issue with metric trends
- **PR badge** — SonarCloud quality gate badge on PRs
- **README badge** — Coverage and quality gate badges on repository README

---

## 10. Domain G: Tooling Setup

### Tools to Integrate

| Tool | Purpose | Cost | Setup Effort |
|------|---------|------|-------------|
| **CodeRabbit** | AI PR review | Free (OSS) / $15/user/mo (private) | 2-click GitHub install |
| **SonarCloud** | Static analysis, security, duplication | Free (OSS) / paid (private) | GitHub Action + config file |
| **Claude Code Action** | Architecture-aware AI review | Anthropic API usage (~$5-20/mo) | Workflow YAML + API key |
| **Prettier** | TypeScript formatting | Free | npm install + config |
| **PSScriptAnalyzer** | PowerShell linting | Free | PowerShell module + CI step |
| **Husky + lint-staged** | Pre-commit hooks | Free | npm install + config |

### Integration Order

1. **Week 1**: Prettier + ESLint strictening + Husky (pre-commit hooks)
2. **Week 2**: CodeRabbit install + Claude Code Action workflow
3. **Week 3**: SonarCloud integration + nightly workflow
4. **Week 4**: Claude Code hooks + enhanced code review skill

---

## 11. Domain H: Ongoing Quality Enforcement

### Quality Gates (What Blocks a PR)

| Check | Blocks PR? | Rationale |
|-------|-----------|-----------|
| `dotnet build -warnaserror` | Yes | Compilation errors are non-negotiable |
| `dotnet format --verify-no-changes` | Yes | Formatting is automated, no excuse |
| `npm run lint` (ESLint errors) | Yes | Type safety and basic quality |
| `dotnet test Spaarke.ArchTests` | Yes | ADR compliance is architectural law |
| CodeRabbit critical findings | No (advisory) | AI can have false positives |
| Claude Code Action findings | No (advisory) | AI can have false positives |
| SonarCloud quality gate | No initially → Yes after baseline | Need baseline before enforcing |
| Test failures | No initially → Yes after hardening | Currently `continue-on-error: true` |

### Graduation Plan

| Phase | Timeline | Change |
|-------|----------|--------|
| **Phase 1** | Weeks 1-4 | Audit + tooling setup. Non-blocking quality checks. |
| **Phase 2** | Weeks 5-8 | Remediate audit findings. Tighten lint rules. Add coverage thresholds. |
| **Phase 3** | Weeks 9-12 | Enforce quality gates on PRs. SonarCloud gate required. Tests must pass. |
| **Phase 4** | Ongoing | Nightly automation, quarterly audits, metric tracking. |

---

## 12. Phases & Estimated Work

### Phase 1: Audit & Tooling Foundation (Weeks 1-2)
- Run initial code quality audit across all areas
- Install and configure tooling (Prettier, Husky, CodeRabbit, SonarCloud)
- Create PR quality workflow
- Establish metric baselines

### Phase 2: Remediation (Weeks 3-4)
- Fix critical audit findings (Program.cs refactor, lint rule strictening, TODO cleanup)
- Enable nightly quality automation
- Configure Claude Code hooks
- Resolve dependency vulnerabilities

### Phase 3: Enforcement (Weeks 5-6)
- Enable PR-blocking quality gates
- Enable SonarCloud quality gate enforcement
- Switch tests to blocking (`continue-on-error: false`)
- Add coverage thresholds

### Phase 4: Ongoing Operations (Continuous)
- Nightly quality runs
- Quarterly comprehensive audits
- Metric tracking and trend analysis
- Continuous rule refinement based on findings

---

## 13. Risks

| Risk | Likelihood | Impact | Mitigation |
|------|-----------|--------|------------|
| CodeRabbit/SonarCloud costs for private repo | Medium | Low | Evaluate free tiers first; budget ~$50/mo |
| AI review false positives create noise | High | Medium | Start advisory-only; tune rules based on dismissal patterns |
| Nightly Claude Code costs via API | Medium | Low | Scope prompts tightly; use Sonnet for routine checks, Opus for deep review |
| Developer friction from new quality gates | Low | Medium | Phase in gradually; start non-blocking |
| Too many tools creating alert fatigue | Medium | Medium | Consolidate into single nightly issue; deduplicate across tools |

---

## 14. Success Criteria

1. [ ] Code quality audit completed with scorecard for each area
2. [ ] All critical audit findings remediated
3. [ ] PR-time quality checks running in < 5 minutes
4. [ ] Nightly quality automation running weeknights
5. [ ] CodeRabbit or equivalent AI reviewer active on all PRs
6. [ ] SonarCloud quality gate passing
7. [ ] Code coverage ≥ 70% on new code
8. [ ] Zero ADR violations
9. [ ] TODO/FIXME count < 20
10. [ ] No critical/high dependency vulnerabilities
11. [ ] Quality metrics tracked and trending positive over 3 months

---

*Design document for code-quality-and-assurance-r1. Ready for review before proceeding to `/design-to-spec`.*
