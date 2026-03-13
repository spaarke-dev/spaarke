# Task Index — Production Environment Setup R1

> **Total Tasks**: 31
> **Phases**: 5
> **Parallel Groups**: 17
> **Max Concurrent Agents**: 6

## Task Status Overview

| Status | Count |
|--------|-------|
| 🔲 Not Started | 15 |
| 🔄 In Progress | 0 |
| ✅ Complete | 16 |
| ⛔ Blocked | 0 |

---

## Phase 1: Foundation — Bicep Templates & Naming Standard

| Task | Title | Status | Parallel Group | Dependencies | Est. |
|------|-------|--------|----------------|--------------|------|
| 001 | Create platform.bicep from existing modules | ✅ | P1-A | None | 4h |
| 002 | Create customer.bicep from existing modules | ✅ | P1-A | None | 3h |
| 003 | Create production parameter files | ✅ | P1-A | None | 2h |
| 004 | Finalize naming convention to Adopted status | ✅ | P1-B | None | 2h |
| 005 | Create appsettings.Production.json with Key Vault refs | ✅ | P1-B | None | 3h |
| 006 | Validate Bicep templates with what-if | ✅ | P1-C | 001, 002, 003 | 2h |

## Phase 2: Deployment Scripts

| Task | Title | Status | Parallel Group | Dependencies | Est. |
|------|-------|--------|----------------|--------------|------|
| 010 | Create Deploy-Platform.ps1 | ✅ | P2-A | 001 | 4h |
| 011 | Parameterize Deploy-BffApi.ps1 | ✅ | P2-A | 005 | 3h |
| 012 | Create Deploy-DataverseSolutions.ps1 | ✅ | P2-A | None | 4h |
| 013 | Create Test-Deployment.ps1 (smoke tests) | ✅ | P2-A | None | 3h |
| 014 | Create Provision-Customer.ps1 | ✅ | P2-B | 010, 012, 013 | 6h |
| 015 | Create Decommission-Customer.ps1 | ✅ | P2-B | 002 | 3h |
| 016 | Create Rotate-Secrets.ps1 | ✅ | P2-C | None | 3h |

## Phase 3: Platform & Demo Deployment

| Task | Title | Status | Parallel Group | Dependencies | Est. |
|------|-------|--------|----------------|--------------|------|
| 020 | Deploy shared platform | ✅ | P3-A | 010 | 4h |
| 021 | Create Entra ID app registrations | ✅ | P3-A | None | 3h |
| 022 | Configure custom domain api.spaarke.com + SSL | ✅ | P3-B | 020 | 2h |
| 023 | Deploy BFF API to production | ✅ | P3-B | 020, 021 | 3h |
| 024 | Provision demo customer | 🔲 | P3-C | 023, 014 | 4h |
| 025 | Load sample data into demo | 🔲 | P3-D | 024 | 3h |
| 026 | Configure demo user access (B2B) | 🔲 | P3-D | 024 | 2h |
| 027 | Run full smoke test suite | 🔲 | P3-E | 025, 026 | 3h |

## Phase 4: CI/CD Pipelines

| Task | Title | Status | Parallel Group | Dependencies | Est. |
|------|-------|--------|----------------|--------------|------|
| 030 | Create deploy-platform.yml | 🔲 | P4-A | 010 | 3h |
| 031 | Create deploy-bff-api.yml | 🔲 | P4-A | 011 | 4h |
| 032 | Create provision-customer.yml | 🔲 | P4-A | 014 | 3h |
| 033 | Configure GitHub environment protection | 🔲 | P4-B | 030, 031, 032 | 2h |
| 034 | Test CI/CD pipeline end-to-end | 🔲 | P4-C | 033 | 3h |

## Phase 5: Documentation & Wrap-up

| Task | Title | Status | Parallel Group | Dependencies | Est. |
|------|-------|--------|----------------|--------------|------|
| 040 | Write production deployment guide | 🔲 | P5-A | 020-027 | 4h |
| 041 | Write customer onboarding runbook | 🔲 | P5-A | 014, 024 | 3h |
| 042 | Write incident response procedures | 🔲 | P5-A | 027 | 3h |
| 043 | Write secret rotation procedures | 🔲 | P5-A | 016 | 2h |
| 044 | Write monitoring and alerting setup guide | 🔲 | P5-A | 020 | 3h |
| 045 | Provision + decommission second test customer | 🔲 | P5-B | 014, 015 | 4h |
| 090 | Project wrap-up | 🔲 | P5-C | All | 2h |

---

## Parallel Execution Groups

| Group | Tasks | Prerequisite | File Conflict Risk | Agent Count |
|-------|-------|-------------|-------------------|-------------|
| P1-A | 001, 002, 003 | None | None — different Bicep files | 3 |
| P1-B | 004, 005 | None | None — docs vs config | 2 |
| P1-C | 006 | P1-A complete | Reads Bicep files (no write) | 1 |
| P2-A | 010, 011, 012, 013 | Phase 1 (partial) | None — different script files | 4 |
| P2-B | 014, 015 | 010, 012, 013 / 002 | None — different scripts | 2 |
| P2-C | 016 | None | None — independent utility | 1 |
| P3-A | 020, 021 | Phase 2 | None — Azure vs Entra ID | 2 |
| P3-B | 022, 023 | 020 | None — DNS vs app deploy | 2 |
| P3-C | 024 | 023, 014 | Sequential | 1 |
| P3-D | 025, 026 | 024 | None — data vs users | 2 |
| P3-E | 027 | P3-D complete | Sequential validation | 1 |
| P4-A | 030, 031, 032 | Phase 2 scripts | None — different workflow files | 3 |
| P4-B | 033 | P4-A complete | Sequential | 1 |
| P4-C | 034 | 033 | Sequential validation | 1 |
| P5-A | 040, 041, 042, 043, 044 | Phase 3 | None — different doc files | 5 |
| P5-B | 045 | 014, 015 | None — Azure operations | 1 |
| P5-C | 090 | All | Final wrap-up | 1 |

---

## Critical Path

```
P1-A (001, 002, 003)
  → P1-C (006)
    → P2-A (010, 011)
      → P2-B (014)
        → P3-A (020) → P3-B (023) → P3-C (024) → P3-D (025, 026) → P3-E (027)
          → P5-A (040-044) → P5-C (090)
```

**Longest path**: 001 → 006 → 010 → 014 → 020 → 023 → 024 → 025 → 027 → 040 → 090

---

## Execution Order Recommendation

### Wave 1 (max 5 agents)
Start immediately — no dependencies:
- **P1-A**: Tasks 001, 002, 003 (3 agents)
- **P1-B**: Tasks 004, 005 (2 agents)

### Wave 2 (max 5 agents)
After P1-A completes:
- **P1-C**: Task 006 (1 agent)
- **P2-A**: Tasks 010, 011, 012, 013 (4 agents) — 012, 013 have no Phase 1 deps
- **P2-C**: Task 016 (1 agent)

### Wave 3 (max 2 agents)
After P2-A completes:
- **P2-B**: Tasks 014, 015 (2 agents)

### Wave 4 (max 2 agents)
After Phase 2 completes:
- **P3-A**: Tasks 020, 021 (2 agents)

### Wave 5 (max 2 agents)
After P3-A completes:
- **P3-B**: Tasks 022, 023 (2 agents)

### Wave 6 (1 agent)
After P3-B completes:
- **P3-C**: Task 024

### Wave 7 (max 2 agents)
After P3-C completes:
- **P3-D**: Tasks 025, 026 (2 agents)

### Wave 8 (1 agent)
After P3-D completes:
- **P3-E**: Task 027

### Wave 9 (max 6 agents)
After Phase 3 + Phase 2 scripts:
- **P4-A**: Tasks 030, 031, 032 (3 agents)
- **P5-A**: Tasks 040, 041, 042, 043, 044 (5 agents) — can overlap with P4-A
- **P5-B**: Task 045 (1 agent)

Note: P4-A and P5-A/P5-B can run in parallel since they touch different files.

### Wave 10 (1 agent)
After P4-A completes:
- **P4-B**: Task 033

### Wave 11 (1 agent)
After P4-B completes:
- **P4-C**: Task 034

### Wave 12 (1 agent)
After all tasks complete:
- **P5-C**: Task 090

---

## High-Risk Tasks

| Task | Risk | Mitigation |
|------|------|------------|
| 014 | Power Platform Admin API limitations | Document manual fallbacks |
| 020 | Azure quota insufficient | Request increases pre-deployment |
| 024 | Managed solution import failures | Test import order in dev first |
| 022 | DNS propagation delays | Use Azure URL initially |

---

*Generated by project-pipeline. Updated as tasks are executed.*
