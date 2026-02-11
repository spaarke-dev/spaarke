# Finance Intelligence Module R1

> **Status**: In Progress
> **Phase**: Phase 1 — Foundation
> **Created**: 2026-02-11
> **Branch**: `work/financial-intelligence-module-r1`

## Quick Links

- [Implementation Plan](plan.md)
- [AI Context](CLAUDE.md)
- [Task Index](tasks/TASK-INDEX.md)
- [Design Specification](spec.md)
- [Original Design](Spaarke_Finance_Intelligence_MVP_Design%202.md)

## Overview

Finance Intelligence MVP that extends the existing email-to-document pipeline with AI-powered invoice classification, human review queue, structured billing fact extraction, pre-computed spend analytics (snapshots and signals), invoice-specific semantic search, and a Finance Intelligence PCF panel on the Matter form.

## Problem Statement

Corporate legal departments need visibility into legal spend but lack automated tools for invoice intake and financial analysis. Invoices arrive as email attachments and require manual classification, data extraction, and association with matters/vendors before spend data becomes actionable.

## Proposed Solution

Build an end-to-end invoice intelligence pipeline:
1. AI classifies email attachments as invoice candidates (Playbook A — gpt-4o-mini)
2. Human reviewers confirm invoices and link to matters/vendors via Dataverse view + BFF endpoint
3. AI extracts structured billing facts from confirmed invoices (Playbook B — gpt-4o)
4. System computes spend snapshots with budget variance and velocity metrics
5. Threshold-based signals alert on budget exceedance, spend velocity spikes, anomalies
6. Confirmed invoices indexed in dedicated AI Search index with semantic search
7. Finance Intelligence PCF panel on Matter form displays spend dashboard

## Scope

### In Scope
- Attachment classification with AI (gpt-4o-mini) + entity matching
- Invoice Review Queue (Dataverse filtered view)
- Invoice confirmation/rejection BFF endpoints
- Invoice extraction with AI (gpt-4o) structured output
- BillingEvent creation with deterministic VisibilityState
- SpendSnapshot generation (Month + ToDate, MoM velocity)
- SpendSignal detection (budget exceeded/warning, velocity spike)
- Dedicated invoice AI Search index with contextual metadata enrichment
- Invoice search endpoint (hybrid: keyword + vector + semantic)
- Finance summary endpoint (Redis-cached)
- Finance Intelligence PCF panel (budget gauge, spend timeline, signals, invoice history)
- `GetStructuredCompletionAsync<T>` platform capability on `IOpenAiClient`
- 6 new Dataverse entities + 13 fields on existing `sprk_document`

### Out of Scope
- Generalized Work Item framework
- Full e-billing/LEDES/payment processing
- Time entry capture / TMS replacement
- External invoice submission portal
- Firm-side VisibilityState (InternalWIP, PreBill)
- Multi-currency conversion
- PCF-based review queue (Dataverse view only for R1)
- Quarter/Year snapshot periods and QoQ/YoY velocity (post-MVP)

## Graduation Criteria

### Functional
- [ ] Email ingestion creates Document records with SPE files linked
- [ ] Classification populates classification + confidence + hints on attachments
- [ ] Invoice Review Queue view shows candidates/unknowns for review
- [ ] Reviewer confirms invoice via BFF endpoint, triggers extraction
- [ ] Extraction creates BillingEvents with VisibilityState = Invoiced
- [ ] Snapshot generation computes budget variance + MoM velocity
- [ ] Signal evaluation detects budget/velocity threshold breaches
- [ ] Invoices indexed with typed financial metadata + contextual enrichment
- [ ] Finance summary endpoint returns cached spend data
- [ ] Finance Intelligence PCF panel renders on Matter form
- [ ] Rejected candidates retained (never deleted)
- [ ] All async operations return 202 + jobId + statusUrl

### Quality
- [ ] Unit tests for SpendSnapshot aggregation (deterministic)
- [ ] Unit tests for signal evaluation rules
- [ ] Integration tests for end-to-end pipeline
- [ ] All new code follows ADR constraints (17 applicable ADRs)

### Performance
- [ ] Finance summary endpoint responds < 200ms from cache
- [ ] Classification completes < 10s per attachment
- [ ] Snapshot generation < 5s per matter

## Key Decisions

| Decision | Choice | Rationale |
|----------|--------|-----------|
| AI output enforcement | Structured output (`response_format: json_schema`) | Constrained decoding at token level — model physically cannot violate schema |
| BillingEvent alternate key | `sprk_invoiceid` + `sprk_linesequence` | Supports idempotent re-extraction (correlationId excluded) |
| SpendSnapshot alternate key | Composite 5-field key | Enables idempotent upsert on snapshot re-generation |
| Snapshot periods (MVP) | Month + ToDate only | Quarter/Year additive post-MVP; velocity hardcoded to MoM |
| Classification model | gpt-4o-mini | Fast, cost-effective for high-volume classification |
| Extraction model | gpt-4o | High accuracy for complex invoice extraction |
| Invoice search index | Dedicated (not general RAG index) | Typed financial fields for range queries; independent scaling |
| Review queue UX | Dataverse view (not PCF) | Simplest MVP; PCF Dataset control is future upgrade |
| Finance PCF panel | In scope for R1 | Owner requested full panel (budget gauge, timeline, signals, history) |
| Entity matching | Reuse existing `IRecordMatchService` | Multi-signal matching already built; add invoice-specific signals |

## Related Documentation

- [Spaarke AI Architecture](../../docs/guides/SPAARKE-AI-ARCHITECTURE.md)
- [RAG Architecture](../../docs/guides/RAG-ARCHITECTURE.md)
- [Email-to-Document Architecture](../../docs/guides/EMAIL-TO-DOCUMENT-ARCHITECTURE.md)
- [AI Playbook Architecture](../../docs/architecture/AI-PLAYBOOK-ARCHITECTURE.md)
- [Azure AI Resources](../../docs/architecture/auth-AI-azure-resources.md)
