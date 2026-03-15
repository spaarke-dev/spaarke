# Code Quality and Assurance R2

> **Status**: Active
> **Branch**: `feature/code-quality-and-assurance-r2`
> **Created**: 2026-03-14
> **Predecessor**: code-quality-and-assurance-r1 (PR #227)

## Overview

Structural code quality improvements for the Spaarke repository. Addresses God classes, memory leaks, dead code, interface bloat, no-op tests, and ADR violations discovered during deep codebase audit.

**Target**: B (85/100) → A- (98/100)

## Scope

| Phase | Focus | Items |
|-------|-------|-------|
| Phase 1 | Quick Wins | Memory leaks, HttpClient, arch tests, dead code + logger |
| Phase 2 | Backend Decomposition | OfficeService, AnalysisOrchestrationService, IDataverseService |
| Phase 3 | Frontend Decomposition | AnalysisWorkspaceApp.tsx (29 useState → 7 hooks) |
| Phase 4 | Architecture Compliance | ADR-022 React 18→16 fix, BaseProxyPlugin assessment |

## Key Metrics

| Metric | Before | Target |
|--------|--------|--------|
| OfficeService.cs lines | 2,907 | <500 |
| AnalysisOrchestrationService DI deps | 20 | ≤10 |
| IDataverseService methods (1 interface) | 63 | 9 focused interfaces |
| AnalysisWorkspaceApp.tsx lines | 1,564 | <400 |
| Unbounded static dictionaries | 3 | 0 |
| Dead MsalAuthProvider copies | 3 | 0 |
| No-op arch tests | 2 | 0 |
| ADR-022 violations | 3 | 0 |

## Quick Links

- [Design Document](design.md)
- [Specification](spec.md)
- [Implementation Plan](plan.md)
- [Task Index](tasks/TASK-INDEX.md)
- [Project Context](CLAUDE.md)

## Graduation Criteria

- [ ] All 12 success criteria in spec.md met
- [ ] `dotnet build` passes with zero errors
- [ ] `dotnet test` passes (all existing tests unchanged)
- [ ] PCF `npm run build` passes with zero errors
- [ ] Overall quality grade ≥ A- (98/100)
