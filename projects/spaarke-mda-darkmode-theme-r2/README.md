# MDA Dark Mode Theme R2 — Unified Theme Consistency

> **Portfolio**: [Project #1224](https://github.com/spaarke-dev/spaarke/issues/1224) · [Board #2](https://github.com/users/spaarke-dev/projects/2) — _auto-registered 2026-10-04 (existence only; Epic/Task Count/Start Date not yet set)_

> **Status**: Code Complete (pending deployment)
> **Branch**: `work/spaarke-mda-darkmode-theme-r2`
> **Created**: 2026-03-30

## Overview

Fix inconsistent light/dark mode rendering across Spaarke surfaces by consolidating three theme utility files into a single authoritative module, removing OS `prefers-color-scheme` fallback, deploying theme flyout to all entities, and adding Dataverse-backed cross-device persistence.

## Graduation Criteria

- [ ] Single theme utility module (no `codePageTheme.ts`, no inline duplicates)
- [ ] OS dark mode + Spaarke "Auto" → ALL surfaces render light
- [ ] No `prefers-color-scheme` in production code
- [ ] Theme flyout on ALL entity forms and grids
- [ ] Cross-device theme persistence via Dataverse
- [ ] Theme protocol documented

## Key Files

| File | Purpose |
|------|---------|
| [spec.md](spec.md) | AI implementation specification |
| [design.md](design.md) | Full design with component inventory |
| [plan.md](plan.md) | Implementation plan |
| [CLAUDE.md](CLAUDE.md) | AI context |
| [tasks/TASK-INDEX.md](tasks/TASK-INDEX.md) | Task registry |
