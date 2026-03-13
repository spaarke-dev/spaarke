# Production Performance Improvement R1

> **Last Updated**: 2026-03-11
> **Status**: In Progress

## Overview

The Spaarke platform requires comprehensive production-readiness improvements before beta user access. The BFF API exhibits 250ms-1,200ms response times due to missing caches, the AI analysis pipeline takes 45+ seconds due to sequential processing and no text caching, and infrastructure defaults are development-grade. Additionally, critical security gaps (unauthenticated endpoints, missing auth filters) and code quality issues (obsolete handlers, mock services) must be resolved.

This project delivers 7 domains of work across 6 phases, targeting 60-80% reduction in API response times, 40-60% reduction in AI analysis times on repeat analyses, resolution of all security gaps, and production-grade infrastructure for 15-50 concurrent beta users.

## Quick Links

| Resource | Path |
|----------|------|
| Specification | [spec.md](spec.md) |
| Implementation Plan | [plan.md](plan.md) |
| Task Index | [tasks/TASK-INDEX.md](tasks/TASK-INDEX.md) |
| AI Context | [CLAUDE.md](CLAUDE.md) |
| Current Task | [current-task.md](current-task.md) |
| Design Document | [design.md](design.md) |

## Current Status

| Phase | Description | Progress | Status |
|-------|-------------|----------|--------|
| Phase 1 | Beta Blockers | 0% | Not Started |
| Phase 2 | Quick Performance Wins | 0% | Not Started |
| Phase 3 | Core Caching | 0% | Not Started |
| Phase 4 | Code Quality & Logging | 0% | Not Started |
| Phase 5 | Infrastructure Hardening | 0% | Not Started |
| Phase 6 | CI/CD & Refactoring | 0% | Not Started |

## Problem Statement

Beta users cannot be invited until critical issues are resolved:
1. **Security**: 5 API endpoint groups are unauthenticated, 37 Office endpoints lack authorization filters
2. **Performance**: AI analysis takes 45+ seconds (sequential RAG, no text caching, no timeouts)
3. **Infrastructure**: Bicep defaults are dev-grade (B1 App Service, Basic Redis, basic AI Search)
4. **Code Quality**: ~5,500 lines of obsolete handlers in DI, 3 Workspace services return mock data
5. **Logging**: 100 files with unguarded JSON serialization in log calls, impacting performance

## Solution Summary

Address all production blockers through a phased approach: secure endpoints and fix infrastructure tiers first (Phase 1), then apply quick AI pipeline wins (Phase 2), implement core caching layers (Phase 3), clean up code quality and logging (Phase 4), harden infrastructure with VNet/autoscaling (Phase 5), and mature CI/CD pipeline (Phase 6).

## Graduation Criteria

- [ ] Zero unauthenticated endpoints in production (SC-14)
- [ ] All 37 Office authorization filters implemented (SC-15)
- [ ] Bicep defaults production-grade: App Service >= S1, Redis >= Standard C1, AI Search >= standard (SC-18)
- [ ] Thread-safety issues resolved in DataverseWebApiService (SC-10)
- [ ] File listing response time (cached) < 50ms p95 (SC-01)
- [ ] AI analysis time (repeat, cached text) < 30s p95 (SC-12)
- [ ] Graph metadata cache hit rate > 85% (SC-04)
- [ ] Document text cache hit rate > 80% (SC-19)
- [ ] Zero obsolete handlers in DI (SC-16)
- [ ] Zero unguarded JSON serialization in log calls (SC-17)
- [ ] Portfolio dashboard shows real user data (SC-21)
- [ ] All 5 to-do generation rules produce results from real data (SC-22)
- [ ] CI/CD pipeline runs tests as deployment gate (SC-09)
- [ ] Zero Console.WriteLine in production code (SC-20)

## Scope

### In Scope
- **Domain A**: BFF API caching (Graph metadata, auth data, GraphClient pooling)
- **Domain B**: Dataverse query optimization (explicit columns, $batch, thread-safety, pagination)
- **Domain C**: Azure infrastructure production readiness (tier corrections, VNet, autoscaling, slots)
- **Domain D**: CI/CD pipeline maturity (test gates, IaC, environment promotion)
- **Domain E**: AI pipeline optimization (parallel RAG, text caching, timeouts, OpenAI tuning)
- **Domain F**: Code quality (auth security, obsolete handlers, god class refactor, mock services)
- **Domain G**: Logging optimization (guard serialization, batch loop logging, log levels)

### Out of Scope
- PCF React 16 remediation (separate project)
- Full VNet hub-spoke multi-region topology
- Azure Front Door / CDN integration
- Database-level Dataverse optimization
- New feature development
- Load testing / benchmarking (follow-up project)

## Key Decisions

| Decision | Rationale | ADR |
|----------|-----------|-----|
| MSAL auth for secured endpoints | Same auth mechanism as all BFF endpoints — no new auth flow needed | ADR-008 |
| Remove obsolete handlers now | JPS replacements verified complete (217 tests passing); old approach is outdated | — |
| Implement real Dataverse queries for Workspace services | Beta users would see fake data otherwise | — |
| Redis-first caching (no L1) | Per ADR-009; no in-memory cache without profiling proof | ADR-009 |
| Cache auth data, compute decisions fresh | Per ADR-003; never cache authorization decisions across requests | ADR-003 |

## Risks & Mitigations

| Risk | Impact | Likelihood | Mitigation |
|------|--------|------------|------------|
| Auth filter changes break Office integrations | High | Medium | Test each endpoint with Office Add-in; staged rollout |
| Removing obsolete handlers breaks analysis actions | High | Medium | Verify JPS configs exist before removal; code in git history |
| Infrastructure tier upgrades increase costs ~$620/mo | Medium | High | Budget approval obtained; offsets from caching savings |
| Cache staleness causes stale file metadata | Medium | Medium | Short TTLs (2-5 min), ETag-versioned keys |
| Beta users hit OpenAI rate limits | High | Medium | Verify TPM capacity; implement retry with backoff |

## Dependencies

| Dependency | Type | Status | Notes |
|------------|------|--------|-------|
| Redis Standard C1 instance | Infrastructure | Requires upgrade | Currently Basic C0 |
| App Service S1 plan | Infrastructure | Requires upgrade | Currently B1 |
| AI Search standard tier | Infrastructure | Requires upgrade | Currently basic |
| JPS migration completeness | Code | Verified complete | 217 tests passing |
| Task 033 design (Office auth) | Code | Not started | Blocks F2 |
| ADR-009, ADR-003, ADR-008 | Architecture | Approved | Caching and auth patterns |

## Team

| Role | Name | Responsibilities |
|------|------|-----------------|
| Owner | Ralph Schroeder | Requirements, review, approval |
| Implementation | Claude Code (AI) | Code implementation via task-execute |

## Changelog

| Date | Version | Change | Author |
|------|---------|--------|--------|
| 2026-03-11 | 1.0 | Project initialized from spec.md | Claude Code |

---

*Production Performance Improvement R1 — Pre-beta readiness project*
