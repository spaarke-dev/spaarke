> **Portfolio**: GitHub Issue [#1850](https://github.com/spaarke-dev/spaarke/issues/1850) · Epic: [#1847](https://github.com/spaarke-dev/spaarke/issues/1847) · Status: In Progress · [Portfolio board view](https://github.com/users/spaarke-dev/projects/2)

# eBilling R1

> **Status**: Ready for Tasks · **Branch**: `work/ebilling-r1` · **Worktree**: `c:/code_files/spaarke-wt-ebilling-r1` · **Started**: 2026-10-14

## Overview

eBilling R1 delivers one complete outside-counsel invoice loop on Spaarke:

1. A law firm submits a PDF invoice in the partner SPA.
2. A Content Understanding custom analyzer reads it.
3. Each line is coded to UTBMS and checked against published billing rules that cite the clause they come from.
4. The system rejects only reliably read blocking failures. Everything else goes to an eligible approver for line review and approval.
5. The firm is told the outcome, with the guideline text behind each adjustment.
6. Approved invoices are exported to AP and later marked Paid.

The project builds on the existing Finance Intelligence pipeline and migrates it to the Action + Binding AI approach (ADR-039 / ADR-043).

## Quick Links

| Document | Purpose |
|---|---|
| [spec.md](spec.md) | **Authoritative** specification: 50 FRs, NFRs, MUST rules, §11 table, owner clarifications |
| [design.md](design.md) | Design narrative and rationale |
| [plan.md](plan.md) | Phases, WBS, discovered resources, risks |
| [CLAUDE.md](CLAUDE.md) | Project operating manual: binding rules, decisions, coordination, gotchas |
| [current-task.md](current-task.md) | Current execution state |
| [tasks/TASK-INDEX.md](tasks/TASK-INDEX.md) | 78 tasks: status, dependencies, parallel waves |
| [notes/decisions-log.md](notes/decisions-log.md) | Owner decisions D-01 – D-66 |
| [notes/solution-overview.md](notes/solution-overview.md) | Market research, repo state, MVP vs R2/R3 framing |
| [notes/owner-requirements-2026-10-14.md](notes/owner-requirements-2026-10-14.md) | Owner's initial requirements, as stated |
| [notes/review-screen-prototype-brief.md](notes/review-screen-prototype-brief.md) | Brief for the review-screen prototype |
| [notes/research/](notes/research/) | Market, LEDES/UTBMS/OCG, Content Understanding vs Document Intelligence, test data sources, repo inventories |
| Prototype | `c:\code_files\spaarke-prototype\projects\ebilling.r1` (v1 built; README answers the brief's layout questions) |

## Current Status

| Phase | Tasks | Status |
|---|---|---|
| 0 Baseline and AI foundations (WP0) | 001–005 | 🔲 |
| 1 Data model (WP1) | 010–029 | 🔲 |
| 2 Submission and extraction (WP2) | 040–053 | 🔲 |
| 3 Rules (WP3) | 070–079 | 🔲 |
| 4 Review and approval (WP4) | 100–112 | 🔲 |
| 5 Firm feedback (WP5) | 130–136 | 🔲 |
| 6 Export and index (WP6) | 150–153 | 🔲 |
| 7 Evaluation and rollout (WP7) | 160–163 | 🔲 |
| Wrap-up | 190 | 🔲 |

## Problem Statement

Law departments review outside-counsel invoices by hand against billing guidelines that sit in PDFs. Spaarke already extracts invoice facts (Finance Intelligence), but it has no submission channel, rules, review or approval workflow, firm feedback, or AP export. Its extraction uses a playbook lookup by name, and its models retire in April 2027.

## Solution Summary

- **Submission:** SPA and internal wizard entry points share one server-side operation, followed by an `IJobHandler` processing job.
- **Extraction:** the Content Understanding custom analyzer `sprk_invoice_vN` (GA `2025-11-01`) returns per-field confidence and page and bounding-box positions.
- **Coding:** UTBMS coding is the Action `INVOICE-LINE-CODE@v1`, restricted to the resolved code set.
- **Rules:** an in-memory evaluator runs 11 billing rule types from published, immutable policy versions. These share the rule system with Ontology (#1111).
- **Rejection:** only Reject-consequence, reject-eligible rules whose fields were read at ≥ 0.90 combined confidence can reject. AI never rejects.
- **Review:** the Invoice Review code page is built from the owner's prototype. It covers line decisions, corrections captured as data, adjustments, approval, Reject / Return / Override, a context panel and related work product.
- **Firm feedback:** an SPA invoice detail view, a Billing Guidelines view, an AI-drafted notice, a PDF report, and email and feed notifications.
- **AP:** CSV and JSON export batches go to the E-Billing Output Files container. Invoices move Sent to AP → Paid, and an invoice index is maintained.

## Graduation Criteria

The project is complete when all 12 spec Success Criteria are met (see [spec.md](spec.md#success-criteria)), including:

- [ ] An SPA submission produces an Accepted invoice or a Rejected submission with cited findings.
- [ ] A reviewed invoice approves with I-1 totals and one Decision Record.
- [ ] The firm sees the outcome, clause quotes and Billing Guidelines.
- [ ] Approved invoices export to AP and move to Paid.
- [ ] NFR-04 – NFR-06 accuracy targets are met on the evaluation set, or an owner decision is recorded.
- [ ] The feature flag is in place, with dev enabled and UAT signed off.

## Scope

**In:** see spec "In Scope", which covers WP0–WP7.

**Out (R1):**
- invoice-to-record matching and mismatch detection (D-62, D-65);
- the AI tuning platform (D-61; separate project `spaarkeai-tuning-and-feedback-r1`, not a dependency);
- firm access to guideline documents (D-63, D-66 — firms see clause quotes and the Billing Guidelines view instead);
- LEDES ingestion, a mailbox channel and multi-portion approval.

## Key Decisions

See [CLAUDE.md §3](CLAUDE.md) for binding one-liners and [notes/decisions-log.md](notes/decisions-log.md) for full rationale.

## Risks

See [plan.md §8](plan.md).
