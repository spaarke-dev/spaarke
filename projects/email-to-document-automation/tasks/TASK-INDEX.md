# Task Index - Email-to-Document Automation

> **Project**: Email-to-Document Automation
> **Last Updated**: 2025-01-09
> **Total Tasks**: 34

---

## Status Legend

| Symbol | Status |
|--------|--------|
| 🔲 | Not Started |
| 🔄 | In Progress |
| ✅ | Complete |
| ⏸️ | Blocked |
| 🚫 | Cancelled |

---

## Phase 1: Core Conversion Infrastructure (Week 1-2)

| ID | Task | Status | Est. | Dependencies |
|----|------|--------|------|--------------|
| 001 | Extend sprk_document entity with email fields | ✅ | 3h | None |
| 002 | Create sprk_emailsaverule entity | ✅ | 2h | None |
| 003 | Add alternate key for idempotency | ✅ | 1h | 001 |
| 004 | Implement IEmailToEmlConverter service | ✅ | 4h | None |
| 005 | Create POST /api/emails/convert-to-document endpoint | ✅ | 3h | 001, 004 |
| 006 | Unit tests for EmailToEmlConverter | ✅ | 3h | 004 |
| **009** | **Phase 1 Deploy** | ✅ | 2h | 001-006 |

---

## Phase 2: Hybrid Trigger & Filtering (Week 3-4)

| ID | Task | Status | Est. | Dependencies |
|----|------|--------|------|--------------|
| 010 | Implement webhook endpoint /api/emails/webhook-trigger | ✅ | 3h | 005 |
| 011 | Create EmailPollingBackupService | ✅ | 3h | 005 |
| 012 | Implement EmailToDocumentJobHandler | ✅ | 4h | 004, 005 |
| 013 | Create IEmailFilterService with rules engine | ✅ | 4h | 002 |
| 014 | Seed default exclusion rules | ✅ | 2h | 002, 013 |
| 015 | Add Application Insights custom events | ✅ | 2h | 012 |
| 016 | Register Dataverse webhook (Service Endpoint + Step) | ✅ | 2h | 010 |
| **019** | **Phase 2 Deploy** | ✅ | 2h | 010-016 |

---

## Phase 3: Association & Attachments (Week 5-6)

| ID | Task | Status | Est. | Dependencies |
|----|------|--------|------|--------------|
| 020 | Implement IEmailAssociationService | 🔲 | 6h | 012 |
| 021 | Add tracking token matching | 🔲 | 2h | 020 |
| 022 | Implement IEmailAttachmentProcessor | 🔲 | 4h | 012 |
| 023 | Create GET /api/emails/association-preview endpoint | 🔲 | 3h | 020 |
| 024 | Unit tests for association methods | 🔲 | 3h | 020, 021 |
| **029** | **Phase 3 Deploy** | 🔲 | 2h | 020-024 |

---

## Phase 4: UI Integration & AI Processing (Week 7-8)

| ID | Task | Status | Est. | Dependencies |
|----|------|--------|------|--------------|
| 030 | Extend TextExtractorService for .eml | 🔲 | 2h | 004 |
| 031 | Create Email form ribbon button | 🔲 | 4h | 005 |
| 032 | Implement sprk_emailactions.js web resource | 🔲 | 3h | 031 |
| 033 | Integrate AI processing enqueue | 🔲 | 2h | 012, 030 |
| 034 | Create admin monitoring PCF control | 🔲 | 6h | 015 |
| **039** | **Phase 4 Deploy** | 🔲 | 2h | 030-034 |

---

## Phase 5: Batch Processing & Production (Week 9-10)

| ID | Task | Status | Est. | Dependencies |
|----|------|--------|------|--------------|
| 040 | Implement POST /api/emails/batch-process endpoint | 🔲 | 3h | 012 |
| 041 | Create GET /api/emails/batch-process/{jobId}/status | 🔲 | 2h | 040 |
| 042 | Implement BatchProcessEmailsJob handler | 🔲 | 3h | 040 |
| 043 | Add DLQ handling and re-drive tooling | 🔲 | 3h | 042 |
| 044 | Performance tuning and load testing | 🔲 | 4h | All |
| **049** | **Phase 5 Deploy** | 🔲 | 2h | 040-044 |

---

## Wrap-up

| ID | Task | Status | Est. | Dependencies |
|----|------|--------|------|--------------|
| 090 | Project wrap-up and documentation | 🔲 | 3h | All phases |

---

## Critical Path

```
001 (Entity) ─┬─► 003 (Alt Key) ─► 005 (API) ─┬─► 010 (Webhook)
              │                               │
004 (EML) ────┴─────────────────────────────►─┤
                                              │
                                              ├─► 012 (Job Handler) ─► 020 (Association)
                                              │
002 (Rules Entity) ─► 013 (Filter) ──────────►┘
```

---

## High-Risk Items

| Task | Risk | Mitigation |
|------|------|------------|
| 004 | RFC 5322 compliance | Use MimeKit, extensive tests |
| 020 | Association accuracy | Multiple methods, confidence scoring |
| 044 | Performance at scale | Load testing, Redis caching |

---

## Knowledge Files by Task

| Task Range | Required Knowledge |
|------------|-------------------|
| 001-006 | ADR-001, ADR-004, SPEC.md sections 2, 3.1 |
| 010-016 | ADR-001, ADR-004, SPEC.md section 3.2, ServiceBusJobProcessor.cs |
| 020-024 | SPEC.md section 3.1, Appendix B |
| 030-034 | RIBBON-WORKBENCH-HOW-TO-ADD-BUTTON.md, SPAARKE-AI-ARCHITECTURE.md |
| 040-044 | ADR-017, SPEC.md section 7 (NFRs) |

---

*For Claude Code: Use `/task-execute {task-id}` to start a task. Always load CLAUDE.md and referenced knowledge files first.*
