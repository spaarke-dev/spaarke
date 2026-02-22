# Task Index — Email Communication Solution R1

> **Last Updated**: 2026-02-22
> **Total Tasks**: 55
> **Overall Status**: IN PROGRESS — Extended (52/55 complete, Phases 1-7 and 9 done, Phase 8 nearly done)

## Task Registry

### Phase 1: BFF Email Service (8 tasks, ~32h)

| ID | Title | Status | Est. | Deps | Blocks | Rigor | Group |
|----|-------|--------|------|------|--------|-------|-------|
| 001 | Create communication models, DTOs, and configuration | ✅ | 4h | none | 003, 004, 005 | STANDARD | A |
| 002 | Create approved sender validation service | ✅ | 3h | none | 003 | STANDARD | A |
| 003 | Implement CommunicationService with Graph sendMail | ✅ | 6h | 001, 002 | 004, 006, 007, 010 | FULL | — |
| 004 | Create CommunicationEndpoints (POST /send) | ✅ | 4h | 003 | 005, 006 | FULL | — |
| 005 | Create communication endpoint authorization filter | ✅ | 3h | 004 | 006 | STANDARD | B |
| 006 | Register AddCommunicationModule in Program.cs | ✅ | 3h | 003, 004, 005 | 007, 010 | FULL | — |
| 007 | Create unit tests for Phase 1 services | ✅ | 5h | 003, 006 | none | STANDARD | C |
| 008 | Rewire Create Matter wizard (matterService.ts) | ✅ | 4h | 004 | none | FULL | C |

### Phase 2: Dataverse Integration (7 tasks, ~24h)

| ID | Title | Status | Est. | Deps | Blocks | Rigor | Group |
|----|-------|--------|------|------|--------|-------|-------|
| 010 | Implement Dataverse communication record creation | ✅ | 5h | 006 | 011, 012, 015 | FULL | — |
| 011 | Implement primary association field mapping | ✅ | 4h | 010 | 014, 020 | FULL | — |
| 012 | Create GET /api/communications/{id}/status endpoint | ✅ | 3h | 010 | none | STANDARD | D |
| 013 | Implement approved sender Dataverse entity + merge logic | ✅ | 4h | 010 | 015 | STANDARD | D |
| 014 | Configure communication subgrid on Matter form | ✅ | 2h | 011 | none | MINIMAL | E |
| 015 | Create unit tests for Phase 2 | ✅ | 4h | 010, 013 | none | STANDARD | E |
| 016 | Phase 2 deployment and verification | ✅ | 2h | 010-015 | 020 | MINIMAL | — |

### Phase 3: Communication Application (6 tasks, ~20h)

| ID | Title | Status | Est. | Deps | Blocks | Rigor | Group |
|----|-------|--------|------|------|--------|-------|-------|
| 020 | Create model-driven form for sprk_communication | ✅ | 4h | 016 | 022, 025 | STANDARD | F |
| 021 | Configure AssociationResolver PCF on communication form | ✅ | 3h | 016 | 025 | STANDARD | F |
| 022 | Create Send command bar button (JS web resource + ribbon) | ✅ | 5h | 020 | 025 | FULL | — |
| 023 | Create communication views | ✅ | 2h | 016 | none | MINIMAL | G |
| 024 | Configure communication subgrids on entity forms | ✅ | 2h | 016 | none | MINIMAL | G |
| 025 | End-to-end communication form testing | ✅ | 4h | 020, 021, 022 | none | STANDARD | — |

### Phase 4: Attachments + Archival (9 tasks, ~28h)

| ID | Title | Status | Est. | Deps | Blocks | Rigor | Group |
|----|-------|--------|------|------|--------|-------|-------|
| 030 | Add attachment fields to sprk_communication entity | ✅ | 1h | 016 | 031, 032, 036 | MINIMAL | — |
| 031 | Document sprk_communicationattachment entity schema | ✅ | 2h | 030 | 033 | MINIMAL | H |
| 032 | Implement attachment download and Graph sendMail payload | ✅ | 5h | 030 | 033, 038 | FULL | H |
| 033 | Create sprk_communicationattachment records | ✅ | 3h | 031, 032 | 036 | STANDARD | — |
| 034 | Implement .eml generation service | ✅ | 4h | 010 | 035 | FULL | I |
| 035 | Implement .eml archival to SPE | ✅ | 4h | 034 | 036 | FULL | — |
| 036 | Add document attachment picker to communication form | ✅ | 3h | 030, 031, 033 | none | STANDARD | — |
| 037 | Implement POST /api/communications/send-bulk endpoint | ✅ | 4h | 032 | none | FULL | J |
| 038 | Create unit/integration tests for Phase 4 | ✅ | 4h | 032 | none | STANDARD | J |

### Phase 5: Playbook Integration (4 tasks, ~12h)

| ID | Title | Status | Est. | Deps | Blocks | Rigor | Group |
|----|-------|--------|------|------|--------|-------|-------|
| 040 | Create SendCommunicationToolHandler | ✅ | 4h | 006 | 041, 042 | FULL | — |
| 041 | Verify tool registration and discovery | ✅ | 2h | 040 | none | STANDARD | K |
| 042 | Test playbook email scenarios | ✅ | 3h | 040 | 043 | STANDARD | K |
| 043 | End-to-end integration testing | ✅ | 3h | 041, 042 | 090 | STANDARD | — |

### Phase 6: Communication Account Management (6 tasks, ~16h)

| ID | Title | Status | Est. | Deps | Blocks | Rigor | Group |
|----|-------|--------|------|------|--------|-------|-------|
| 050 | Create CommunicationAccountService with Dataverse query | ✅ | 4h | none | 051, 052, 055 | FULL | — |
| 051 | Update ApprovedSenderValidator to use CommunicationAccountService | ✅ | 3h | 050 | 055 | FULL | — |
| 052 | Create sprk_communicationaccount admin form and views | ✅ | 3h | 050 | 055 | STANDARD | L |
| 053 | Configure appsettings.json and document Exchange setup | ✅ | 2h | 050 | 055 | MINIMAL | L |
| 054 | Unit tests for account service and validator updates | ✅ | 2h | 050, 051 | none | STANDARD | — |
| 055 | End-to-end outbound shared mailbox testing | ✅ | 2h | 050-054 | 060, 070, 080 | STANDARD | — |

### Phase 7: Individual User Outbound (5 tasks, ~12h)

| ID | Title | Status | Est. | Deps | Blocks | Rigor | Group |
|----|-------|--------|------|------|--------|-------|-------|
| 060 | Add SendMode to DTOs and branch CommunicationService for OBO | ✅ | 4h | 055 | 061, 064 | FULL | — |
| 061 | Update communication web resource with send mode selection | ✅ | 3h | 060 | 064 | FULL | M |
| 062 | Update Communication form with send mode UX | ✅ | 2h | 060 | 064 | STANDARD | M |
| 063 | Unit tests for individual send path | ✅ | 2h | 060 | none | STANDARD | — |
| 064 | End-to-end individual send testing | ✅ | 1h | 060-063 | 080 | STANDARD | — |

### Phase 8: Inbound Shared Mailbox Monitoring (8 tasks, ~24h)

| ID | Title | Status | Est. | Deps | Blocks | Rigor | Group |
|----|-------|--------|------|------|--------|-------|-------|
| 070 | Create GraphSubscriptionManager BackgroundService | ✅ | 5h | 055 | 071, 077 | FULL | — |
| 071 | Create incoming webhook endpoint | ✅ | 4h | 070 | 072, 077 | FULL | — |
| 072 | Create IncomingCommunicationProcessor job handler | ✅ | 5h | 071 | 077 | FULL | — |
| 073 | Implement backup polling for missed webhooks | ✅ | 3h | 070 | 077 | FULL | N |
| 074 | Update sprk_communicationaccount form with inbound fields | ✅ | 1h | 070 | none | MINIMAL | N |
| 075 | Create incoming communication views | ✅ | 2h | 072 | none | MINIMAL | O |
| 076 | Unit tests for inbound pipeline | ✅ | 3h | 070-073 | none | STANDARD | O |
| 077 | End-to-end inbound monitoring testing | 🔲 | 2h | 070-076 | 080 | STANDARD | — |

### Phase 9: Verification & Admin UX (3 tasks, ~8h)

| ID | Title | Status | Est. | Deps | Blocks | Rigor | Group |
|----|-------|--------|------|------|--------|-------|-------|
| 080 | Create mailbox verification endpoint and service | ✅ | 3h | 055 | 082 | FULL | — |
| 081 | Update sprk_communicationaccount form with verification UI | ✅ | 2h | 080 | 082 | STANDARD | — |
| 082 | Admin documentation and deployment guide updates | ✅ | 3h | 080, 081 | 090 | MINIMAL | — |

### Wrap-up (1 task, ~4h) — RESET

| ID | Title | Status | Est. | Deps | Blocks | Rigor | Group |
|----|-------|--------|------|------|--------|-------|-------|
| 090 | Project wrap-up (extended scope) | 🔲 | 4h | 082 | none | MINIMAL | — |

## Summary

| Phase | Tasks | FULL | STANDARD | MINIMAL | Est. Hours | Status |
|-------|-------|------|----------|---------|------------|--------|
| 1: BFF Email Service | 8 | 4 | 3 | 0 | 32h | ✅ |
| 2: Dataverse Integration | 7 | 3 | 2 | 2 | 24h | ✅ |
| 3: Communication App | 6 | 1 | 3 | 2 | 20h | ✅ |
| 4: Attachments + Archival | 9 | 4 | 2 | 3 | 28h | ✅ |
| 5: Playbook Integration | 4 | 1 | 2 | 0 | 12h | ✅ |
| 6: Communication Accounts | 6 | 2 | 2 | 1 | 16h | ✅ |
| 7: Individual User Outbound | 5 | 2 | 2 | 0 | 12h | ✅ |
| 8: Inbound Monitoring | 8 | 4 | 1 | 2 | 24h | 🔨 (7/8) |
| 9: Verification & Admin | 3 | 1 | 1 | 1 | 8h | ✅ |
| Wrap-up (reset) | 1 | 0 | 0 | 1 | 4h | 🔲 |
| **Total** | **57** | **22** | **18** | **12** | **~180h** | **52/57** |

## Dependency Graph

```
Phase 1 (COMPLETE):
  001 ──┐
        ├──→ 003 ──→ 004 ──→ 005 ──┐
  002 ──┘              │            ├──→ 006 ──→ 007
                       │            │         └──→ 010 (Phase 2)
                       └──→ 008     │
                                    └──→ 040 (Phase 5)

Phase 2 (COMPLETE):
  006 ──→ 010 ──→ 011 ──→ 014
              │        └──→ 020 (Phase 3)
              ├──→ 012
              ├──→ 013 ──→ 015
              └──→ 034 (Phase 4)
  (all) ──→ 016

Phase 3 (COMPLETE):
  016 ──→ 020 ──→ 022 ──┐
      ├──→ 021           ├──→ 025
      ├──→ 023           │
      └──→ 024           │

Phase 4 (COMPLETE):
  016 ──→ 030 ──→ 031 ──┐
              ├──→ 032 ──┼──→ 033 ──→ 036
              │          │
              │          ├──→ 037
              │          └──→ 038
  010 ──→ 034 ──→ 035

Phase 5 (COMPLETE):
  006 ──→ 040 ──→ 041 ──┐
              └──→ 042 ──┼──→ 043

Phase 6 (Communication Accounts):
  050 ──→ 051 ──→ 055 (E2E test)
    ├──→ 052 ──┘       │
    ├──→ 053 ──┘       │
    └──→ 054           │
                       ↓
              ┌────────┼────────┐
              ↓        ↓        ↓
Phase 7:    060      070      080 (Phase 9)
              ↓        ↓
            061      071
              ↓        ↓
            062      072
              ↓        ↓
            063      073, 074
              ↓        ↓
            064      075, 076
                       ↓
                     077 ──→ 080 (Phase 9)

Phase 9:
  080 ──→ 081 ──→ 082 ──→ 090
```

## Critical Path

### Original (COMPLETE)
```
001 → 003 → 004 → 005 → 006 → 010 → 011 → (Phase 3/4 parallel) → 043
                                                                     = ~56h
```

### Extension
```
050 → 051 → 055 → 070 → 071 → 072 → 077 → 080 → 081 → 082 → 090
                                                                = ~36h
```

## Parallel Execution Groups

### Original (COMPLETE)

| Group | Tasks | Prerequisite | Notes |
|-------|-------|--------------|-------|
| **A** | 001, 002 | none | Models + sender validation — fully independent |
| **B** | 005, 006 | 004 | Auth filter + module registration (005 feeds 006) |
| **C** | 007, 008 | 003+006, 004 | Unit tests + wizard rewire — independent codebases |
| **D** | 012, 013 | 010 | Status endpoint + approved sender merge — independent |
| **E** | 014, 015 | 011, 010+013 | Matter subgrid + Phase 2 tests — independent |
| **F** | 020, 021 | 016 | Form + AssociationResolver config — same form, coordinate |
| **G** | 023, 024 | 016 | Views + subgrids — fully independent |
| **H** | 031, 032 | 030 | Attachment entity docs + download code — independent |
| **I** | 034, 035 | 010, 034 | .eml generation + archival — 035 depends on 034 |
| **J** | 037, 038 | 032 | Bulk send + Phase 4 tests — independent |
| **K** | 041, 042 | 040 | Tool verification + playbook tests — independent |

### Extension (NEW)

| Group | Tasks | Prerequisite | Notes |
|-------|-------|--------------|-------|
| **L** | 052, 053 | 050 | Admin form + appsettings config — independent |
| **M** | 061, 062 | 060 | Web resource + form UX — coordinate on send mode |
| **N** | 073, 074 | 070 | Backup polling + inbound form fields — independent |
| **O** | 075, 076 | 072 | Incoming views + unit tests — independent |

## Cross-Phase Parallelism (Extension)

| Combination | Tasks | Why They're Safe |
|-------------|-------|-----------------|
| Phase 7 + Phase 8 | 060-064 + 070-077 | Both depend on 055, but modify different services and endpoints |
| Phase 6 admin form + Phase 6 tests | 052 + 054 | Dataverse config vs BFF tests |
| Phase 8 views + Phase 8 tests | 075 + 076 | Dataverse config vs BFF tests |

## High-Risk Tasks

### Original (COMPLETE)

| Task | Risk | Mitigation |
|------|------|-----------|
| 003 | Graph sendMail integration — needs Mail.Send permission + shared mailbox | ✅ Verified |
| 008 | matterService.ts in separate worktree — potential merge conflicts | ✅ Resolved |
| 032 | Attachment size handling — Graph API limits | ✅ Validated |
| 022 | Ribbon/command bar customization — Dataverse deployment complexity | ✅ Deployed |

### Extension (NEW)

| Task | Risk | Mitigation |
|------|------|-----------|
| 050 | Actual Dataverse field names differ from design (sprk_sendenableds, sprk_subscriptionid) | Spec has full mapping; task includes field name verification |
| 060 | OBO token expiry during individual send | Clear error message; existing token caching (55-min TTL) |
| 070 | Graph subscription webhook reliability | Backup polling (task 073) as resilience layer |
| 071 | Graph subscription validation timing (propagation delay) | Document 30-min Exchange policy propagation |
| 072 | Association resolution temptation — out of scope | Spec explicitly excludes; AI project handles this |

---

*Generated by project-pipeline on 2026-02-20*
*Extended with Phases 6-9 on 2026-02-22*
