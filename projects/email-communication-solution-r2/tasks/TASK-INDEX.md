# Task Index — Email Communication Solution R2

> **Project**: email-communication-solution-r2
> **Total Tasks**: 22
> **Created**: 2026-03-09

## Status Legend

| Icon | Status |
|------|--------|
| 🔲 | Not Started |
| 🔄 | In Progress |
| ✅ | Complete |
| ⛔ | Blocked |

## Phase 1: Communication Account Entity (Phase A) — Foundation

| # | Task | Status | Depends On | Rigor |
|---|------|--------|------------|-------|
| 001 | Assess R1 Communication Account State | ✅ | — | STANDARD |
| 002 | Complete Communication Account Admin UX | ✅ | 001 | STANDARD |
| 003 | Complete ApprovedSenderValidator Migration | ✅ | 001 | FULL |
| 004 | Document Exchange Access Policy Setup | ✅ | 001 | MINIMAL |
| 005 | End-to-End Outbound Shared Mailbox Test | ✅ | 003 | STANDARD |

## Phase 2: Individual User Outbound (Phase B)

| # | Task | Status | Depends On | Rigor |
|---|------|--------|------------|-------|
| 010 | Assess R1 OBO Send Implementation | ✅ | 005 | STANDARD |
| 011 | Complete OBO Individual Send Path | ✅ | 010 | FULL |
| 012 | Communication Form Send Mode UX | ✅ | 011 | FULL |
| 013 | End-to-End Individual Send Test | ✅ | 012 | STANDARD |

## Phase 3: Inbound Shared Mailbox Monitoring (Phase C)

| # | Task | Status | Depends On | Rigor |
|---|------|--------|------------|-------|
| 020 | Assess R1 Inbound Pipeline | ✅ | 005 | STANDARD |
| 021 | Complete Graph Subscription Lifecycle | ✅ | 020 | FULL |
| 022 | Complete Webhook Endpoint | ✅ | 020 | FULL |
| 023 | Complete Incoming Communication Processor | ✅ | 022 | FULL |
| 024 | Implement Association Resolution | ✅ | 023 | FULL |
| 025 | Complete Backup Polling Service | ✅ | 023 | FULL |
| 026 | Incoming Communication Views | ✅ | 023 | MINIMAL |
| 027 | End-to-End Inbound Monitoring Test | ✅ | 024, 025, 026 | STANDARD |

## Phase 4: Email-to-Document Migration (Phase E)

| # | Task | Status | Depends On | Rigor |
|---|------|--------|------------|-------|
| 030 | Create GraphMessageToEmlConverter | 🔲 | 027 | FULL |
| 031 | Create GraphAttachmentAdapter | 🔲 | 027 | FULL |
| 032 | Update sprk_document Entity Schema | 🔲 | 027 | STANDARD |
| 033 | Integrate Inbound Document Archival | 🔲 | 030, 031, 032 | FULL |
| 034 | Enhance Outbound Archival | 🔲 | 032 | FULL |
| 035 | Delete Retired Components | 🔲 | 033, 034 | FULL |
| 036 | Rename Telemetry, Consolidate Config | 🔲 | 035 | STANDARD |
| 037 | End-to-End Archival Test | 🔲 | 036 | STANDARD |

## Phase 5: Verification & Admin UX (Phase D)

| # | Task | Status | Depends On | Rigor |
|---|------|--------|------------|-------|
| 040 | Create Verification Endpoint | 🔲 | 037 | FULL |
| 041 | Daily Send Count Tracking | 🔲 | 037 | STANDARD |
| 042 | Admin Form Enhancements | 🔲 | 040, 041 | STANDARD |
| 043 | Update Admin Documentation | 🔲 | 042 | MINIMAL |

## Wrap-Up

| # | Task | Status | Depends On | Rigor |
|---|------|--------|------------|-------|
| 090 | Project Wrap-Up | 🔲 | 043 | MINIMAL |

## Parallel Execution Groups

| Group | Tasks | Prerequisite | Notes |
|-------|-------|--------------|-------|
| A | 002, 003, 004 | 001 complete | Independent Phase A tasks |
| B | 010, 020 | 005 complete | Phase B + C assessments can run in parallel |
| C | 021, 022 | 020 complete | Subscription + webhook independent |
| D | 024, 025, 026 | 023 complete | Association, polling, views independent |
| E | 030, 031, 032 | 027 complete | Converter, adapter, schema independent |
| F | 033, 034 | 032 complete (034 needs 032 only) | Inbound + outbound archival partially parallel |
| G | 040, 041 | 037 complete | Verification + send count independent |

## Critical Path

```
001 → 003 → 005 → 020 → 022 → 023 → 024/025 → 027 → 030/031/032 → 033 → 035 → 036 → 037 → 040/041 → 042 → 043 → 090
```

**Longest chain**: 17 tasks (through inbound → archival → admin)

## Risk Items

| Task | Risk | Mitigation |
|------|------|------------|
| 021 | Graph subscription API changes/limits | Follow Microsoft docs, test in dev |
| 022 | Webhook response time constraint (<3s) | No sync processing, enqueue only |
| 023 | Deduplication race conditions | Redis lock + Dataverse uniqueness |
| 033 | Archival failure cascading | Best-effort pattern, isolated failures |
| 035 | Dangling references after deletion | Thorough grep after delete |
