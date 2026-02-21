# Task Index — Email Communication Solution R1

> **Last Updated**: 2026-02-21
> **Total Tasks**: 35
> **Overall Status**: COMPLETE (35/35)

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

### Wrap-up (1 task, ~4h)

| ID | Title | Status | Est. | Deps | Blocks | Rigor | Group |
|----|-------|--------|------|------|--------|-------|-------|
| 090 | Project wrap-up | ✅ | 4h | 043 | none | MINIMAL | — |

## Summary

| Phase | Tasks | FULL | STANDARD | MINIMAL | Est. Hours |
|-------|-------|------|----------|---------|------------|
| 1: BFF Email Service | 8 | 4 | 3 | 0 | 32h |
| 2: Dataverse Integration | 7 | 3 | 2 | 2 | 24h |
| 3: Communication App | 6 | 1 | 3 | 2 | 20h |
| 4: Attachments + Archival | 9 | 4 | 2 | 3 | 28h |
| 5: Playbook Integration | 4 | 1 | 2 | 0 | 12h |
| Wrap-up | 1 | 0 | 0 | 1 | 4h |
| **Total** | **35** | **13** | **12** | **8** | **~120h** |

## Dependency Graph

```
Phase 1:
  001 ──┐
        ├──→ 003 ──→ 004 ──→ 005 ──┐
  002 ──┘              │            ├──→ 006 ──→ 007
                       │            │         └──→ 010 (Phase 2)
                       └──→ 008     │
                                    └──→ 040 (Phase 5)

Phase 2:
  006 ──→ 010 ──→ 011 ──→ 014
              │        └──→ 020 (Phase 3)
              ├──→ 012
              ├──→ 013 ──→ 015
              └──→ 034 (Phase 4)
  (all) ──→ 016

Phase 3:
  016 ──→ 020 ──→ 022 ──┐
      ├──→ 021           ├──→ 025
      ├──→ 023           │
      └──→ 024           │

Phase 4:
  016 ──→ 030 ──→ 031 ──┐
              ├──→ 032 ──┼──→ 033 ──→ 036
              │          │
              │          ├──→ 037
              │          └──→ 038
  010 ──→ 034 ──→ 035

Phase 5:
  006 ──→ 040 ──→ 041 ──┐
              └──→ 042 ──┼──→ 043 ──→ 090
```

## Critical Path

```
001 → 003 → 004 → 005 → 006 → 010 → 011 → (Phase 3/4 parallel) → 043 → 090
                                                                     = ~56h
```

## Parallel Execution Groups

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

## Cross-Phase Parallelism

These tasks from different phases can run simultaneously:

| Combination | Tasks | Why They're Safe |
|-------------|-------|-----------------|
| Phase 1 wizard + Phase 1 tests | 008 + 007 | Different codebases (workspace vs BFF) |
| Phase 2 Dataverse + Phase 5 AI tool | 010-015 + 040-042 | 040 only needs 006 done, not Phase 2 |
| Phase 3 views + Phase 4 archival | 023, 024 + 034, 035 | Dataverse config vs BFF code |
| Phase 3 form + Phase 4 attachment code | 020, 021 + 032 | Form config vs BFF service code |

## High-Risk Tasks

| Task | Risk | Mitigation |
|------|------|-----------|
| 003 | Graph sendMail integration — needs Mail.Send permission + shared mailbox | Verify prerequisites before starting |
| 008 | matterService.ts in separate worktree — potential merge conflicts with PR #186 | Coordinate timing with workspace PR |
| 032 | Attachment size handling — Graph API limits | Validate before send, test with large files |
| 022 | Ribbon/command bar customization — Dataverse deployment complexity | Use ribbon-edit skill |

---

*Generated by project-pipeline on 2026-02-20*
