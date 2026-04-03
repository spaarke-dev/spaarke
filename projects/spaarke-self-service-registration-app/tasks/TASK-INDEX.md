# Task Index — Spaarke Self-Service Registration App

## Execution Strategy

Tasks are structured for **maximum parallel execution**. Each phase completes before the next begins. Within each phase, tasks in the same parallel group run simultaneously via concurrent Claude Code agents.

## Task Registry

| Task | Title | Phase | Parallel Group | Status | Dependencies |
|------|-------|-------|---------------|--------|-------------|
| 001 | Foundation: Models, Configuration, DI Module | 0 | — (serial) | ✅ | none |
| 010 | Graph User Service: Entra ID User Management | 1 | P1 | 🔲 | 001 |
| 011 | Dataverse Registration Service: CRUD + User Sync | 1 | P1 | 🔲 | 001 |
| 012 | Dataverse Schema: Table, Views, Form, Sitemap | 1 | P1 | 🔲 | 001 |
| 013 | Entra ID Setup Scripts | 1 | P1 | 🔲 | 001 |
| 014 | Email Templates and Registration Email Service | 1 | P1 | 🔲 | 001 |
| 020 | Provisioning Orchestrator Service | 2 | P2 | 🔲 | 010, 011, 014 |
| 021 | Registration API Endpoints | 2 | P2 | 🔲 | 010, 011, 014 |
| 022 | Website: Request Early Access Form | 2 | P2 | 🔲 | 001 |
| 030 | Demo Expiration BackgroundService | 3 | P3 | 🔲 | 020 |
| 031 | Ribbon Buttons and JS Webresource | 3 | P3 | 🔲 | 012, 021 |
| 040 | DI Wiring, Configuration, Program.cs Integration | 4 | — (serial) | 🔲 | 020, 021, 030 |
| 041 | Deploy BFF API, Dataverse Solution, Entra Scripts | 4 | — (serial) | 🔲 | 040, 012, 013 |
| 042 | End-to-End Testing and Verification | 4 | — (serial) | 🔲 | 041 |
| 050 | Project Wrap-Up | 5 | — (serial) | 🔲 | 042 |

## Parallel Execution Groups

| Group | Tasks | Prerequisite | Max Agents | Notes |
|-------|-------|--------------|------------|-------|
| P1 | 010, 011, 012, 013, 014 | 001 complete | 5 | Fully independent streams — no shared files |
| P2 | 020, 021, 022 | P1 complete | 3 | 022 (website) is in separate repo, fully independent |
| P3 | 030, 031 | P2 complete | 2 | No shared files between expiration and ribbon |

## Critical Path

```
001 → 010 → 020 → 030 → 040 → 041 → 042 → 050
       011 ↗      021 ↗      031 ↗
       014 ↗      022        012 ↗
       012                   013 ↗
       013
```

**Longest path**: 001 → 010 → 020 → 040 → 041 → 042 → 050 (7 serial dependencies)

## Dependency Graph

```
Phase 0:  [001] ─────────────────────────────────────┐
                                                      │
Phase 1:  [010] [011] [012] [013] [014]  ← all from 001
            │     │           │     │
            ▼     ▼           │     ▼
Phase 2:  [020] [021]       [022]  │
            │     │                 │
            ▼     ▼                 │
Phase 3:  [030] [031] ← 012+021    │
            │                       │
            ▼                       │
Phase 4:  [040] ← 020+021+030      │
            │                       │
            ▼                       │
          [041] ← 040+012+013 ─────┘
            │
            ▼
          [042]
            │
            ▼
Phase 5:  [050]
```

## High-Risk Items

| Task | Risk | Mitigation |
|------|------|------------|
| 010 | Graph API permissions may not be granted yet | Run 013 (Entra scripts) first or in parallel |
| 020 | Orchestrator depends on 3 services | Each service has unit tests; integration tested in 042 |
| 041 | Deployment to live Demo environment | Test locally first; deployment scripts are idempotent |
| 042 | E2E requires all components working together | Each component tested individually first |

## Progress Summary

- **Total tasks**: 15
- **Completed**: 1
- **In Progress**: 0
- **Remaining**: 14
- **Max parallel agents**: 5 (Phase 1)
