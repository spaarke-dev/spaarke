# Procedures

> **Purpose**: Process documentation and operational procedures for Spaarke development
> **Audience**: Developers, AI coding agents, operations

## Overview

This directory contains process documentation covering CI/CD, testing, code review, dependency management, and session/context recovery procedures.

## Procedures Index

### CI/CD & Quality

- [ci-cd-workflow.md](ci-cd-workflow.md) - GitHub Actions workflow inventory, slot-swap deployment, quality gates, promotion flow
- [testing-and-code-quality.md](testing-and-code-quality.md) - Testing strategy, coverage standards, test patterns by module (API, PCF, plugins)
- **[DEPENDENCY-MANAGEMENT.md](DEPENDENCY-MANAGEMENT.md)** - Dependency update cadence, security patching process, version pinning conventions, Dependabot configuration
- **[CODE-REVIEW-BY-MODULE.md](CODE-REVIEW-BY-MODULE.md)** - Module-specific code review checklists for BFF API, PCF controls, plugins, Code Pages, shared libraries

### Development Workflow

- [context-recovery.md](context-recovery.md) - Full protocol for resuming work after session compaction or new session
- [parallel-claude-sessions.md](parallel-claude-sessions.md) - Running multiple Claude Code sessions in parallel via worktrees

## For AI Agents

**Loading strategy**: Load specific procedures when the task requires them (e.g., load `testing-and-code-quality.md` when writing tests, `DEPENDENCY-MANAGEMENT.md` when updating packages).
