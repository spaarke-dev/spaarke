# SDAP BFF API & Performance Enhancement (R1)

> **Status**: In Progress
> **Branch**: `work/sdap-bff-api-and-performance-enhancement-r1`
> **Created**: 2026-03-04
> **Estimated Effort**: 124-170 hours (~6 weeks)

## Overview

Internal improvements to the Sprk.Bff.Api across 7 workstreams targeting architecture decomposition, Redis caching, Dataverse query optimization, resilience, AI pipeline performance, Azure network isolation, and CI/CD hardening. **No new features. No breaking API changes.**

## Workstreams

| ID | Workstream | Items | Phase |
|----|-----------|-------|-------|
| A | Architecture Foundation | A1-A2 | 1-2 |
| B | Caching & Performance | B1-B5 | 1-2 |
| C | Dataverse Optimization | C1-C4 | 1, 3 |
| D | Resilience & Operations | D1-D3 | 3 |
| E | AI Pipeline Performance | E1-E2 | 3 |
| F | Azure Infrastructure | F1-F8 | 4 |
| G | CI/CD Pipeline | G1-G4 | 5 |

## Key Performance Targets

| Operation | Current | Target | Improvement |
|-----------|---------|--------|-------------|
| File listing (cached) | 250-1,000ms | < 50ms p95 | 90-97% |
| Chat message (2 tools) | 750-2,530ms | < 1,500ms p95 | 50-55% |
| Dataverse entity read | 80-150ms | < 100ms p95 | ~50% |
| Analysis after restart | **Lost** | < 50ms recovery | N/A → working |

## Graduation Criteria

- [ ] SC-01: Program.cs < 200 lines
- [ ] SC-02: File listing (cached) < 50ms p95
- [ ] SC-03: Document download < 600ms p95
- [ ] SC-04: Chat message (2 tools) < 1,500ms p95
- [ ] SC-05: Graph cache hit rate > 85%
- [ ] SC-06: Dataverse entity read < 100ms p95
- [ ] SC-07: Zero `ColumnSet(true)` in production
- [ ] SC-08: Zero thread-safety issues in Dataverse client
- [ ] SC-09: Analysis survives App Service restart
- [ ] SC-10: Critical job pickup < 30s
- [ ] SC-11: Bulk jobs don't starve critical jobs
- [ ] SC-12: All Azure services behind private endpoints
- [ ] SC-13: Autoscaling configured and tested
- [ ] SC-14: Deployment slots operational
- [ ] SC-15: CI/CD runs tests before deployment
- [ ] SC-16: Disable any domain via config → 503, no crash

## Quick Links

- [Spec](spec.md) | [Plan](plan.md) | [Project CLAUDE.md](CLAUDE.md)
- [Task Index](tasks/TASK-INDEX.md) | [Design](design.md)
- [BFF API CLAUDE.md](../../src/server/api/Sprk.Bff.Api/CLAUDE.md)
