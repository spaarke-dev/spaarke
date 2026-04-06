# Procedures

> **Purpose**: Process documentation and operational procedures for Spaarke development
> **Audience**: Developers, AI coding agents, operations

## Overview

This directory contains process documentation covering CI/CD, testing, code review, dependency management, and session/context recovery procedures.

## Procedures Index

### AI Coding Procedures (Start Here)

| Document | Description | Last Updated | Last Reviewed | Status |
|----------|-------------|--------------|---------------|--------|
| **[AI-CODING-PROCEDURES-GUIDE.md](AI-CODING-PROCEDURES-GUIDE.md)** | Scenario-based quick reference for all Claude Code skills and workflows — new projects, task execution, code review, documentation, deployment, maintenance | 2026-04-05 | 2026-04-05 | New |

### CI/CD & Quality

| Document | Description | Last Updated | Last Reviewed | Status |
|----------|-------------|--------------|---------------|--------|
| [ci-cd-workflow.md](ci-cd-workflow.md) | GitHub Actions workflow inventory, slot-swap deployment, quality gates, promotion flow | 2026-04-05 | 2026-04-05 | Current |
| [testing-and-code-quality.md](testing-and-code-quality.md) | Testing strategy, coverage standards, test patterns by module (API, PCF, plugins) | 2026-04-05 | 2026-04-05 | Current |
| **[DEPENDENCY-MANAGEMENT.md](DEPENDENCY-MANAGEMENT.md)** | Dependency update cadence, security patching process, version pinning conventions, Dependabot configuration | 2026-04-05 | 2026-04-05 | New |
| **[CODE-REVIEW-BY-MODULE.md](CODE-REVIEW-BY-MODULE.md)** | Module-specific code review checklists for BFF API, PCF controls, plugins, Code Pages, shared libraries | 2026-04-05 | 2026-04-05 | New |

### Development Workflow

| Document | Description | Last Updated | Last Reviewed | Status |
|----------|-------------|--------------|---------------|--------|
| [context-recovery.md](context-recovery.md) | Full protocol for resuming work after session compaction or new session | 2026-01-04 | 2026-04-05 | Verified |
| [parallel-claude-sessions.md](parallel-claude-sessions.md) | Running multiple Claude Code sessions in parallel via worktrees | 2026-01-06 | 2026-04-05 | Verified |

## For AI Agents

**Loading strategy**: Load specific procedures when the task requires them (e.g., load `testing-and-code-quality.md` when writing tests, `DEPENDENCY-MANAGEMENT.md` when updating packages).
