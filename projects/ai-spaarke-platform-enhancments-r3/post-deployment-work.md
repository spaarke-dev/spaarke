# AI Platform Enhancements R1 — Post-Deployment Work

> **Project**: ai-spaarke-platform-enhancements-r1
> **Created**: 2026-02-25
> **Status**: Active — Phase 5 tasks ready to execute
> **Last Deployed**: 2026-02-25 (BFF API + AnalysisWorkspace PCF)

---

## Overview

Phases 1-4 are complete and deployed. This document tracks all remaining validation, evaluation, and operational work required before R1 can be considered fully closed. It also captures post-R1 operational activities that are prerequisites for production readiness.

---

## Part 1: R1 Phase 5 — End-to-End Validation (28 hours)

These tasks are defined in POML files and should be executed via `/task-execute`.

### AIPL-070: Setup Test Document Corpus (4h)

**Purpose**: Create the test document set that all subsequent evaluation depends on.

**Deliverables**:
- 10+ representative test documents covering all playbook types:
  - NDA / Non-Disclosure Agreement
  - Commercial Contract
  - Commercial Lease
  - Invoice / Financial Document
  - Service Level Agreement (SLA)
  - Employment Agreement
  - Amendment / Addendum
  - Regulatory Filing
  - Corporate Resolution / Board Minutes
  - Insurance Policy
- All documents uploaded to SPE test container
- Document manifest (ID, name, type, playbook mapping) recorded for harness

**Blocks**: AIPL-071, AIPL-072

---

### AIPL-071: Build Evaluation Harness + EvalRunner CLI (6h)

**Purpose**: Automated quality measurement for RAG retrieval and analysis output.

**Deliverables**:
- Gold dataset: manually curated question-answer pairs per document
- Scoring metrics: Recall@K and nDCG@K (K=5, K=10)
- `EvalRunner` CLI tool for reproducible evaluation runs
- `EvaluationEndpoints.cs` for API-driven evaluation
- Results output as structured JSON for baseline tracking

**Blocks**: AIPL-073, AIPL-075

---

### AIPL-072: Build E2E Tests for All 10 Playbooks (6h)

**Purpose**: Verify each playbook produces correct analysis output against test documents.

**Deliverables**:
- Automated test per playbook: upload document → execute analysis → verify output structure + citations
- Tests confirm: correct section headings, key entity extraction, citation presence
- Integration test project or script that can be rerun after any deployment

**Blocks**: AIPL-073

---

### AIPL-073: Record Quality Baseline (3h)

**Purpose**: Establish reproducible quality metrics for comparison in R2 and future iterations.

**Deliverables**:
- Baseline report with Recall@10 and nDCG@10 per playbook
- Analysis output quality scores (structure, completeness, accuracy)
- Report saved as `projects/ai-spaarke-platform-enhancents-r1/notes/quality-baseline.md`
- Numbers become the benchmark for R2 streaming write and re-analysis features

**Dependencies**: AIPL-071 + AIPL-072

---

### AIPL-074: Run Negative Test Suite (3h)

**Purpose**: Verify graceful failure handling across the AI pipeline.

**Test scenarios**:
- Missing or unconfigured skills on a playbook
- Empty knowledge sources (no RAG data)
- Handler timeout (slow LLM response)
- Malformed or corrupt document upload
- Invalid playbook ID in chat session creation
- Token budget exhaustion mid-analysis
- Network interruption during SSE streaming

**Deliverables**:
- Negative test results documented
- Any bugs discovered filed and triaged

**Dependencies**: AIPL-073

---

### AIPL-075: SprkChat Evaluation (4h)

**Purpose**: Measure SprkChat agent quality as an interactive assistant.

**Metrics**:
- **Answer accuracy**: % of responses that correctly answer gold dataset questions
- **Citation rate**: % of responses that include document citations when applicable
- **Latency**: p95 first-token time < 2 seconds
- **Tool usage**: % of queries where the agent correctly invokes search/analysis tools

**Deliverables**:
- SprkChat evaluation report
- Recommendations for prompt tuning based on results

**Dependencies**: AIPL-073

---

### AIPL-090: Project Wrap-Up (2h)

**Purpose**: Close the R1 project cleanly.

**Deliverables**:
- Update README.md status to "Complete"
- Document lessons learned
- Archive ephemeral project files (notes, spikes)
- Run `/repo-cleanup` for repository hygiene
- Update TASK-INDEX.md with all tasks marked complete

**Dependencies**: All Phase 5 tasks

---

## Part 2: Operational Prerequisites for Production

These are **not POML tasks** — they are operational activities required before the AI platform is production-ready. They span beyond the R1 codebase project.

### OPS-01: Production Document Ingestion Pipeline

**Status**: Not started
**Owner**: DevOps / Platform team

**Description**: Establish a repeatable process for ingesting client documents into the Azure AI Search RAG index.

**Activities**:
- Define ingestion workflow: SPE upload → Document Intelligence extraction → chunking → embedding → index
- Determine batch vs. real-time ingestion strategy
- Build or configure ingestion pipeline (Azure Data Factory, custom BackgroundService, or AI Foundry Prompt Flow)
- Set up monitoring for ingestion failures and index health
- Define retention and re-indexing policies

**Current state**: Documents are indexed ad-hoc during development. No automated production pipeline exists.

---

### OPS-02: Knowledge Source Curation

**Status**: Not started
**Owner**: Content / Legal / Subject Matter Experts

**Description**: Create and maintain knowledge base entries that feed the RAG system beyond client documents.

**Knowledge source types needed**:
- Legal frameworks and regulatory references (jurisdiction-specific)
- Industry standards and best practices
- Internal firm policies and procedures
- Template libraries (clause banks, standard provisions)
- Glossary of terms and definitions

**Activities**:
- Identify priority knowledge domains per playbook
- Source or author knowledge content
- Upload to Azure AI Search as knowledge source records
- Map knowledge sources to playbooks via Dataverse relationships
- Establish review cadence (quarterly? per regulatory change?)

**Current state**: Some test knowledge sources exist. No systematic curation process.

---

### OPS-03: Playbook Prompt Refinement

**Status**: Blocked on AIPL-073/075 (quality baselines needed first)
**Owner**: AI / Product team

**Description**: Iterate on playbook system prompts based on evaluation results from Phase 5.

**Activities**:
- Review quality baseline metrics per playbook (from AIPL-073)
- Review SprkChat accuracy metrics (from AIPL-075)
- Identify underperforming playbooks
- Refine system prompts: adjust instruction clarity, output structure, citation guidance
- Re-run evaluation to measure improvement
- Document prompt versions and A/B test results

**Current state**: Prompts are v1 authored during development. No iteration based on measured quality.

---

### OPS-04: Index Tuning and Optimization

**Status**: Not started
**Owner**: AI / Platform team

**Description**: Optimize RAG retrieval quality through index configuration tuning.

**Activities**:
- Evaluate chunk size (current: default). Test 512, 1024, 2048 token chunks
- Evaluate embedding model (current: Azure OpenAI text-embedding-ada-002). Consider text-embedding-3-large
- Evaluate re-ranking strategy (semantic ranker, cross-encoder re-ranker)
- Test hybrid search (keyword + vector) vs. pure vector search
- Measure impact on Recall@K and nDCG@K metrics (using AIPL-071 harness)
- Document optimal configuration

**Current state**: Default Azure AI Search configuration. No tuning performed.

---

### OPS-05: Dataverse Analysis Persistence

**Status**: Deferred from R1 (was Task 032)
**Owner**: Development team

**Description**: Persist analysis results to Dataverse instead of in-memory storage.

**Activities**:
- Implement `WorkingDocumentService` (currently stubbed — logging only)
- Write analysis output to Dataverse `sprk_analysisresult` entity
- Write chat history to Dataverse session records
- Implement read-back for session resume across App Service restarts
- Test with App Service restart/scale scenarios

**Current state**: Analysis results stored in-memory only. App Service restart loses all analysis data.

**Impact**: Critical for production — any App Service restart or scaling event loses all active analyses.

---

### OPS-06: Cost Monitoring and Budget Enforcement

**Status**: Partially implemented (CostControl middleware exists)
**Owner**: AI / Platform team

**Description**: Establish production cost monitoring and alerting for AI services.

**Activities**:
- Configure Azure OpenAI usage alerts (token consumption thresholds)
- Configure AI Search query volume alerts
- Set per-tenant or per-user token budgets in CostControl middleware
- Build usage dashboard (Azure Monitor or custom)
- Define cost escalation process (who gets alerted, at what thresholds)
- Review and adjust token budgets based on actual usage patterns

**Current state**: CostControl middleware enforces per-session budget. No aggregate monitoring or alerting.

---

### OPS-07: Content Safety and Compliance Review

**Status**: Partially implemented (ContentSafety middleware exists)
**Owner**: Legal / Compliance / AI team

**Description**: Validate content safety configuration for production use.

**Activities**:
- Review Azure OpenAI content filtering settings (categories, severity levels)
- Test PII detection and redaction in chat responses
- Validate that client-confidential document content is not leaked across tenants
- Review data residency compliance (where embeddings and chat history are stored)
- Document AI usage policy for end users
- Establish incident response process for AI safety events

**Current state**: ContentSafety middleware active with default Azure filters. No formal compliance review.

---

## Part 3: R2 Handoff Items

These items from R1 directly feed into the R2 project (ai-spaarke-platform-enhancents-r2).

| Item | R1 State | R2 Dependency |
|------|----------|---------------|
| Quality baseline metrics | Produced by AIPL-073 | R2 uses as comparison benchmark for streaming write quality |
| SprkChat eval metrics | Produced by AIPL-075 | R2 uses as baseline for action menu / suggestion accuracy |
| Test document corpus | Created by AIPL-070 | R2 reuses for Code Page migration validation |
| Evaluation harness | Built by AIPL-071 | R2 extends with streaming write and diff compare metrics |
| Prompt refinement results | From OPS-03 | R2 playbook capability declarations build on tuned prompts |
| Dataverse persistence | From OPS-05 | R2 streaming writes need persistent storage (not in-memory) |

---

## Priority and Sequencing

### Immediate (can start now)

| ID | Activity | Est. | Rationale |
|----|----------|------|-----------|
| AIPL-070 | Test document corpus | 4h | Unblocks all Phase 5 work |
| AIPL-071 | Evaluation harness | 6h | Unblocks quality measurement |
| AIPL-072 | E2E playbook tests | 6h | Unblocks quality measurement |

### After baselines established

| ID | Activity | Est. | Rationale |
|----|----------|------|-----------|
| AIPL-073 | Quality baseline | 3h | Required before prompt tuning or R2 comparison |
| AIPL-074 | Negative tests | 3h | Required before production |
| AIPL-075 | SprkChat evaluation | 4h | Required before R2 feature work |
| OPS-03 | Prompt refinement | Ongoing | Uses baseline data to improve |

### Before production deployment

| ID | Activity | Est. | Rationale |
|----|----------|------|-----------|
| OPS-01 | Ingestion pipeline | TBD | No automated document indexing exists |
| OPS-02 | Knowledge curation | TBD | RAG quality depends on knowledge base quality |
| OPS-05 | Dataverse persistence | TBD | In-memory storage not viable for production |
| OPS-06 | Cost monitoring | TBD | No visibility into AI spend |
| OPS-07 | Safety/compliance review | TBD | Legal requirement before client-facing use |

### Ongoing / background

| ID | Activity | Rationale |
|----|----------|-----------|
| OPS-04 | Index tuning | Iterative; measure → tune → measure |
| OPS-03 | Prompt refinement | Iterative; based on usage feedback |

---

*This document should be reviewed and updated as items are completed. Use `/task-execute` for AIPL-* tasks. OPS-* items require coordination beyond the development workflow.*
