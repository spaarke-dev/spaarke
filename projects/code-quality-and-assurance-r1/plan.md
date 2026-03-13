# Code Quality and Assurance R1 - Implementation Plan

> **Version**: 1.0
> **Created**: 2026-03-11
> **Source**: spec.md

---

## Architecture Context

### Quality System Architecture

```
┌─────────────────────────────────────────────────────────────────────┐
│                    CODE QUALITY ASSURANCE SYSTEM                     │
├─────────────────────────────────────────────────────────────────────┤
│  LAYER 1: PR-TIME (<5 min)          LAYER 2: NIGHTLY (<15 min)     │
│  ┌─────────────────────────┐        ┌──────────────────────────┐   │
│  │ GitHub Actions on PR    │        │ Scheduled GitHub Actions │   │
│  │ • dotnet build -w.a.e.  │        │ • Claude Code headless   │   │
│  │ • dotnet format verify  │        │ • SonarCloud analysis    │   │
│  │ • ESLint (strict)       │        │ • Coverage trend report  │   │
│  │ • Architecture tests    │        │ • Dependency audit       │   │
│  │ • CodeRabbit AI review  │        │ • Auto-issue creation    │   │
│  │ • Claude Code Action    │        │                          │   │
│  └─────────────────────────┘        └──────────────────────────┘   │
│                                                                     │
│  LAYER 3: ON-DEMAND                 LAYER 4: QUARTERLY             │
│  ┌─────────────────────────┐        ┌──────────────────────────┐   │
│  │ Claude Code Skills      │        │ Comprehensive Audit      │   │
│  │ • /code-review          │        │ • Full codebase sweep    │   │
│  │ • /adr-check            │        │ • Metric comparison      │   │
│  │ • Claude Code hooks     │        │ • Architecture review    │   │
│  └─────────────────────────┘        └──────────────────────────┘   │
└─────────────────────────────────────────────────────────────────────┘
```

### Discovered Resources

**Applicable ADRs:**
| ADR | Relevance |
|-----|-----------|
| ADR-001 | NetArchTest validates no Azure Functions — preserve and extend |
| ADR-010 | DI minimalism (<=15 registrations) — audit checks interface-to-impl ratio |
| ADR-019 | ProblemDetails for errors — code review validates error patterns |
| ADR-020 | SemVer compliance — CI detects breaking API changes |
| ADR-021 | Fluent UI v9 only — ESLint rules enforce, no hard-coded colors |
| ADR-022 | PCF React 16 only — ESLint detects React 18 imports in PCF |
| ADR-026 | Code Pages Vite + singlefile — build validation in CI |

**Applicable Skills:**
| Skill | Purpose |
|-------|---------|
| code-review | Enhanced with quantitative metrics (FR-10) |
| adr-check | Validate architecture compliance |
| ci-cd | GitHub Actions workflow management |
| spaarke-conventions | Always-apply naming and patterns |
| script-aware | Discover existing scripts before creating new ones |

**Existing Infrastructure:**
| Resource | Path |
|----------|------|
| Primary CI pipeline | `.github/workflows/sdap-ci.yml` |
| ADR audit workflow | `.github/workflows/adr-audit.yml` |
| Architecture tests | `tests/Spaarke.ArchTests/` (6 test classes) |
| Code review skill | `.claude/skills/code-review/SKILL.md` |
| ADR check skill | `.claude/skills/adr-check/SKILL.md` |
| Testing constraints | `.claude/constraints/testing.md` |
| Testing patterns | `.claude/patterns/testing/` (4 files) |
| Quality procedures | `docs/procedures/testing-and-code-quality.md` |
| CI/CD procedures | `docs/procedures/ci-cd-workflow.md` |
| Coverage config | `config/coverlet.runsettings` |
| EditorConfig | `.editorconfig` |

---

## Phase Breakdown

### Phase 1: Initial Code Quality Audit (Tasks 001-008)

**Objective**: Baseline codebase quality, identify critical issues, produce scorecard.

| # | Deliverable | Estimate |
|---|-------------|----------|
| 001 | Audit: BFF API (.NET) — dead code, abstraction audit, Program.cs analysis, pattern consistency | 4h |
| 002 | Audit: TypeScript/PCF — ESLint analysis, component size, import patterns, Fluent compliance | 4h |
| 003 | Audit: PowerShell scripts — PSScriptAnalyzer setup, pattern consistency, error handling | 3h |
| 004 | Audit: Test suite — coverage measurement, test quality assessment, gap analysis | 3h |
| 005 | Audit: Dependencies — vulnerability scan, outdated packages, pre-release dependencies | 2h |
| 006 | Audit: Configuration — .editorconfig, ESLint, tsconfig consistency across projects | 2h |
| 007 | Generate quality scorecard and baseline metrics | 2h |
| 008 | Create remediation task list from audit findings | 2h |

### Phase 2: Tooling Foundation (Tasks 010-017)

**Objective**: Install, configure, and verify all quality tools.

| # | Deliverable | Estimate |
|---|-------------|----------|
| 010 | Configure Prettier for TypeScript/React formatting | 2h |
| 011 | Stricten ESLint rules (WARN/OFF → ERROR for key rules) | 3h |
| 012 | Configure Husky + lint-staged for pre-commit hooks | 2h |
| 013 | Install and configure CodeRabbit for AI PR review | 2h |
| 014 | Configure Claude Code Action (anthropics/claude-code-action) workflow | 3h |
| 015 | Configure SonarCloud integration (sonar-project.properties + workflow) | 3h |
| 016 | Install and configure PSScriptAnalyzer for PowerShell | 2h |
| 017 | Verify all tools working together on test PR | 2h |

### Phase 3: Automation Pipelines (Tasks 020-025)

**Objective**: Create nightly quality workflow, Claude Code hooks, and enhanced skills.

| # | Deliverable | Estimate |
|---|-------------|----------|
| 020 | Create nightly quality GitHub Actions workflow (nightly-quality.yml) | 4h |
| 021 | Create nightly Claude Code headless review prompt | 3h |
| 022 | Implement Claude Code PostToolUse hook (post-edit lint) | 2h |
| 023 | Implement Claude Code TaskCompleted hook (quality gate) | 2h |
| 024 | Enhance /code-review skill with quantitative metrics and AI code smell detection | 3h |
| 025 | Implement weekly quality summary issue auto-generation | 2h |

### Phase 4: Remediation (Tasks 030-036)

**Objective**: Fix critical audit findings, apply quality improvements.

| # | Deliverable | Estimate |
|---|-------------|----------|
| 030 | Refactor Program.cs into feature modules (target: < 500 lines) | 4h |
| 031 | Resolve TODO/FIXME items (target: < 20 remaining) | 3h |
| 032 | Fix dependency vulnerabilities (critical/high priority) | 2h |
| 033 | Apply ESLint rule strictening to existing codebase (fix violations) | 3h |
| 034 | Apply Prettier formatting to existing TypeScript codebase | 2h |
| 035 | Update sdap-ci.yml to integrate new quality checks | 3h |
| 036 | Add README badges (coverage, SonarCloud quality gate) | 1h |

### Phase 5: Enforcement & Documentation (Tasks 040-045, 090)

**Objective**: Enable blocking quality gates, document processes, verify system.

| # | Deliverable | Estimate |
|---|-------------|----------|
| 040 | Enable blocking quality gates (SonarCloud, test failures, coverage thresholds) | 3h |
| 041 | Update CI/CD documentation with new quality pipeline | 2h |
| 042 | Create code quality onboarding guide for new contributors | 2h |
| 043 | Run end-to-end quality system verification (PR + nightly + hooks) | 3h |
| 044 | Create quarterly audit runbook | 2h |
| 045 | Update testing-and-code-quality.md procedures with new tools | 2h |
| 090 | Project wrap-up (README status, lessons learned, archive) | 2h |

---

## Risks and Mitigations

| Risk | Likelihood | Impact | Mitigation |
|------|-----------|--------|------------|
| CodeRabbit/SonarCloud costs for private repo | Medium | Low | Evaluate free tiers first; budget ~$100/mo |
| AI review false positives create noise | High | Medium | Start advisory-only; tune based on dismissal patterns |
| Nightly Claude Code API costs | Medium | Low | Scope prompts tightly; use Sonnet for routine checks |
| Program.cs refactoring introduces regressions | Medium | High | Comprehensive testing before and after; incremental approach |
| Too many tools creating alert fatigue | Medium | Medium | Consolidate into single nightly issue; deduplicate |

---

## References

- [spec.md](spec.md) — Full specification
- [design.md](design.md) — Original design document
- [Testing procedures](../../docs/procedures/testing-and-code-quality.md)
- [CI/CD procedures](../../docs/procedures/ci-cd-workflow.md)
- [Testing constraints](../../.claude/constraints/testing.md)
- [Architecture tests](../../tests/Spaarke.ArchTests/)
- [sdap-ci.yml](../../.github/workflows/sdap-ci.yml)
