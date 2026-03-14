# CLAUDE.md — Code Quality and Assurance R1

> **Project**: code-quality-and-assurance-r1
> **Branch**: `feature/code-quality-and-assurance-r1`
> **Status**: In Progress

## Project Purpose

Establish a comprehensive code quality assurance system combining an initial audit with ongoing automated quality enforcement across 4 layers (PR-time, nightly, on-demand, quarterly).

## Key Decisions

| Decision | Choice | Rationale |
|----------|--------|-----------|
| AI PR review tools | CodeRabbit + Claude Code Action (both) | Different perspectives: CodeRabbit for line-by-line, Claude for architecture |
| Static analysis | SonarCloud | Industry standard, AI Code Assurance feature, free tier available |
| Pre-commit hooks | Husky + lint-staged | Standard Node.js toolchain, runs only on staged files |
| PowerShell linting | PSScriptAnalyzer | Microsoft's official PowerShell linter |
| Enforcement strategy | Graduated (advisory → blocking) | Avoids friction; Phase 1-2 advisory, Phase 3 blocking |
| Nightly review | Claude Code headless mode | Uses existing Claude Code capabilities, no new infrastructure |

## Applicable ADRs

| ADR | Constraint | Impact on This Project |
|-----|-----------|----------------------|
| ADR-001 | No Azure Functions | NetArchTest validates — preserve existing test |
| ADR-010 | ≤15 DI registrations, concretes by default | Audit checks interface-to-impl ratio |
| ADR-019 | ProblemDetails for all errors | Code review validates error patterns |
| ADR-020 | SemVer for client packages | CI can detect breaking changes |
| ADR-021 | Fluent UI v9 only, no hard-coded colors | ESLint rules to enforce |
| ADR-022 | PCF: React 16 APIs only | ESLint detects React 18 imports in PCF |
| ADR-026 | Code Pages: Vite + singlefile | Build validation in CI |

## Constraints

- PR-time quality pipeline MUST complete < 5 minutes
- Nightly pipeline MUST complete < 15 minutes
- Pre-commit hooks MUST complete < 10 seconds
- AI reviews are advisory-only until Phase 3 graduation
- All tool configs MUST be version-controlled
- Monthly tool costs MUST stay < $100
- MUST NOT break existing sdap-ci.yml — extend, don't restructure
- MUST preserve existing 6 NetArchTest suites

## Key Files

| File | Purpose |
|------|---------|
| `.github/workflows/sdap-ci.yml` | Primary CI pipeline — extend with new quality jobs |
| `.github/workflows/adr-audit.yml` | Weekly ADR audit — model for nightly workflow |
| `tests/Spaarke.ArchTests/` | Architecture tests (6 ADR test classes) |
| `.claude/skills/code-review/SKILL.md` | Code review skill — enhance |
| `.claude/skills/adr-check/SKILL.md` | ADR check skill |
| `.claude/constraints/testing.md` | Testing MUST/MUST NOT rules |
| `.claude/patterns/testing/` | Testing patterns (4 files) |
| `config/coverlet.runsettings` | Coverage configuration |
| `.editorconfig` | C# formatting rules |
| `docs/procedures/testing-and-code-quality.md` | Quality procedures |
| `docs/procedures/ci-cd-workflow.md` | CI/CD procedures |

## Task Execution Notes

- Phase 1 (audit) tasks are mostly read-only analysis — MINIMAL rigor
- Phase 2 (tooling) tasks create new config files — STANDARD rigor
- Phase 3 (automation) tasks modify workflows and skills — FULL rigor
- Phase 4 (remediation) tasks modify source code — FULL rigor
- Phase 5 (enforcement) tasks modify CI pipeline gates — FULL rigor

---

## 🚨 MANDATORY: Task Execution Protocol

**ABSOLUTE RULE**: When executing tasks, Claude Code MUST invoke the `task-execute` skill. DO NOT read POML files directly and implement manually.

The task-execute skill ensures:
- Knowledge files loaded (ADRs, constraints, patterns)
- Context tracked in current-task.md
- Proactive checkpointing every 3 steps
- Quality gates run at Step 9.5
- Progress recoverable after compaction

**Trigger phrases**: "work on task X", "continue", "next task", "resume task X"
