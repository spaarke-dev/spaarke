# Code Quality and Assurance R1

> **Status**: In Progress
> **Branch**: `feature/code-quality-and-assurance-r1`
> **Created**: 2026-03-11

## Problem Statement

Spaarke's codebase is 100% AI-generated via Claude Code. While architectural governance is strong (ADRs, NetArchTest, skills), systematic code-level quality assurance is lacking. Known risks include over-abstraction, unnecessary verbosity, brittle patterns, and defensive overkill. Program.cs is 1,940 lines, ESLint rules are mostly WARN/OFF, 96 TODOs exist across 35 files, and CI tests don't block PRs.

## Solution

A comprehensive 4-layer code quality system:

1. **PR-Time** (<5 min): Build validation, format checks, architecture tests, AI-powered PR review (CodeRabbit + Claude Code Action)
2. **Nightly** (<15 min): Full test coverage, SonarCloud analysis, Claude Code headless deep review, dependency audit
3. **On-Demand**: Enhanced `/code-review` skill, Claude Code hooks (PostToolUse lint, TaskCompleted quality gate)
4. **Quarterly**: Full codebase audit with scorecard

Combined with an initial code quality audit to baseline and remediate critical findings.

## Scope

### In Scope
- Initial code quality audit across C# (525 files), TypeScript (955 files), PowerShell (114 files)
- PR-time quality pipeline (< 5 min) with CodeRabbit + Claude Code Action
- Nightly quality automation (Claude Code headless + SonarCloud)
- Claude Code hooks and enhanced code-review skill
- Prettier, Husky, PSScriptAnalyzer setup
- Quality metrics tracking and weekly reports
- Graduation plan: advisory (Phase 1) to blocking (Phase 3)

### Out of Scope
- Custom AI model training
- Custom quality dashboard web app
- Performance profiling (separate project)
- Security penetration testing
- Writing new unit tests (only auditing coverage gaps)

## Graduation Criteria

- [ ] Code quality audit completed with A-F scorecard per area
- [ ] All critical audit findings remediated
- [ ] PR-time quality checks running in < 5 minutes
- [ ] Nightly quality automation running weeknights
- [ ] AI reviewer (CodeRabbit/Claude Code Action) active on all PRs
- [ ] SonarCloud quality gate configured and passing
- [ ] Code coverage >= 70% on new code
- [ ] Zero ADR violations in NetArchTest
- [ ] TODO/FIXME count < 20
- [ ] No critical/high dependency vulnerabilities
- [ ] Quality metrics trending positive over 3 months

## Quick Links

- [Implementation Plan](plan.md)
- [AI Context](CLAUDE.md)
- [Task Index](tasks/TASK-INDEX.md)
- [Design Document](design.md)
- [Specification](spec.md)
