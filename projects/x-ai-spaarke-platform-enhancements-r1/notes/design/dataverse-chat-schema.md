# Dataverse Chat + Evaluation Entity Schema

> **Task**: AIPL-001 — Define and Create Dataverse Chat + Evaluation Entities
> **Created**: 2026-02-23
> **Status**: Schema Defined — Entities Created (see [Entity Creation](#entity-creation-status) below)
> **Blocks**: AIPL-051 (ChatSessionManager), AIPL-071 (evaluation harness)

---

## Overview

Four new Dataverse entities required by Workstream C (SprkChat) and Workstream D (Evaluation):

| Entity | Purpose | Workstream |
|--------|---------|------------|
| `sprk_aichatmessage` | Persistent storage of individual chat messages | C (SprkChat) |
| `sprk_aichatsummary` | Conversation session metadata and summaries | C (SprkChat) |
| `sprk_aievaluationrun` | Aggregate evaluation run records (Recall@K, nDCG@K) | D (Evaluation) |
| `sprk_aievaluationresult` | Per-query evaluation results within a run | D (Evaluation) |

All entities use the `sprk_` publisher prefix. No AI processing occurs in plugins — entities are data structures only (ADR-002).

---

## NFR Context

| NFR | Constraint | Schema Impact |
|-----|-----------|---------------|
| NFR-07 | Chat session Redis hot cache expires after 24h idle | `sprk_aichatsummary.sprk_sessionid` is the cache key; Dataverse is cold storage |
| NFR-09 | Tenant isolation enforced on all indexes, cache keys, and chat sessions | `sprk_aichatsummary.sprk_tenantid` required field |
| NFR-12 | Chat history supports 50 messages per session before archiving | `sprk_aichatsummary.sprk_messagecount` tracks messages; archive trigger at 50 |

**Caching strategy**: Redis is the hot path (24h TTL). When a session is loaded from cold storage, `ChatSessionManager` reads all `sprk_aichatmessage` records for the session and reconstructs the in-memory `ChatHistory`. Dataverse is the audit trail.

---

## Entity 1: sprk_aichatmessage

**Purpose**: Persistent cold storage for individual chat messages. Redis holds the hot copy; Dataverse is the write-through store for audit and session recovery.

**Display Name**: AI Chat Message | **Plural**: AI Chat Messages
**Ownership**: Organization-owned (tenant isolation via sprk_tenantid on parent sprk_aichatsummary)
**Primary Name Field**: `sprk_name` (auto-generated, e.g., "MSG-000001")

### Fields

| Field Schema Name | Display Name | Type | Required | Max Length | Description |
|-------------------|--------------|------|----------|------------|-------------|
| `sprk_name` | Message ID | Single Line of Text | Required | 200 | Auto-number primary name: `MSG-{SEQNUM:6}` |
| `sprk_role` | Role | Choice | Required | — | Who authored the message. See option values below. |
| `sprk_content` | Content | Multiple Lines of Text | Required | 10000 | The message text. For assistant messages, this is the full streamed response. |
| `sprk_tokencount` | Token Count | Whole Number | Optional | — | Number of tokens in this message (for cost tracking). Min: 0, Max: 100000. |
| `sprk_sessionid` | Session ID | Single Line of Text | Required | 100 | Foreign key to the chat session (matches `sprk_aichatsummary.sprk_sessionid`). Indexed for query. |
| `sprk_sequencenumber` | Sequence Number | Whole Number | Optional | — | Message order within the session. Min: 0, Max: 999999. Used to reconstruct ordered history. |
| `sprk_toolcallsjson` | Tool Calls JSON | Multiple Lines of Text | Optional | 5000 | JSON array of tool calls made during this message turn (assistant messages only). |
| `createdon` | Created On | Date and Time | Auto | — | Standard Dataverse audit field. Provided automatically. |
| `modifiedon` | Modified On | Date and Time | Auto | — | Standard Dataverse audit field. Provided automatically. |

### Choice Field: sprk_role

| Value | Label | Description |
|-------|-------|-------------|
| 726490000 | User | Message authored by the end user |
| 726490001 | Assistant | Message authored by the AI agent (SprkChatAgent) |
| 726490002 | System | System prompt message (typically one per session, at start) |

**Global Option Set Name**: `sprk_aichatrole`

### Relationship

| Relationship | Type | Description |
|-------------|------|-------------|
| `sprk_aichatsummary_aichatmessage_summaryid` | N:1 to `sprk_aichatsummary` | Optional. Messages belong to a session/summary. Lookup field: `sprk_summaryid`. Delete behavior: Cascade. |

### Indexing Notes

- `sprk_sessionid` is the primary query key — `ChatHistoryManager` queries `sprk_aichatmessage` by session ID to reconstruct history.
- Query pattern: `GET /sprk_aichatmessages?$filter=sprk_sessionid eq '{sessionId}'&$orderby=sprk_sequencenumber asc`
- Dataverse does not have explicit user-defined indexes, but the `sprk_sessionid` field should be marked as `IsValidForAdvancedFind = true` and used in filtered views.

---

## Entity 2: sprk_aichatsummary

**Purpose**: Session-level metadata for a chat conversation. One record per chat session. Tracks session state, tenant isolation, playbook context, and a rolling summary of older messages.

**Display Name**: AI Chat Summary | **Plural**: AI Chat Summaries
**Ownership**: Organization-owned
**Primary Name Field**: `sprk_name` (auto-generated, e.g., "SES-000001")

### Fields

| Field Schema Name | Display Name | Type | Required | Max Length | Description |
|-------------------|--------------|------|----------|------------|-------------|
| `sprk_name` | Session Name | Single Line of Text | Required | 200 | Auto-number primary name: `SES-{SEQNUM:6}` |
| `sprk_sessionid` | Session ID | Single Line of Text | Required | 100 | Unique session GUID from the BFF (`ChatSessionManager`). Indexed. This is also the Redis cache key suffix. |
| `sprk_tenantid` | Tenant ID | Single Line of Text | Required | 100 | Power Platform tenant ID for multi-tenant isolation. All session queries filter by this field. |
| `sprk_summary` | Conversation Summary | Multiple Lines of Text | Optional | 20000 | Rolling summary of archived messages. Populated by `ChatHistoryManager` when message count exceeds 15. Empty for new sessions. |
| `sprk_messagecount` | Message Count | Whole Number | Optional | — | Total messages in this session (hot + archived). Default: 0. Archiving triggers at 50 (NFR-12). Min: 0, Max: 999999. |
| `sprk_playbookid` | Playbook ID | Single Line of Text | Optional | 100 | `sprk_aiplaybook` logical ID for the playbook this session uses. Drives tool availability in the agent. |
| `sprk_documentid` | Document ID | Single Line of Text | Optional | 100 | SPE document ID for document-context sessions (DocumentContext mode). |
| `sprk_analysisid` | Analysis ID | Single Line of Text | Optional | 100 | Analysis record ID for analysis-context sessions (AnalysisContext mode). |
| `sprk_contextmode` | Context Mode | Choice | Optional | — | Current context mode for the chat session. See option values below. |
| `sprk_isarchived` | Is Archived | Two Options | Optional | — | True when session has been archived (older than retention period or manually archived). |
| `createdon` | Created On | Date and Time | Auto | — | Standard Dataverse audit field. |
| `modifiedon` | Modified On | Date and Time | Auto | — | Standard Dataverse audit field. |

### Choice Field: sprk_contextmode

**Global Option Set Name**: `sprk_aichatcontextmode`

| Value | Label | Description |
|-------|-------|-------------|
| 726490000 | Document | Chat is grounded in a specific document (DocumentSearchTools active) |
| 726490001 | Analysis | Chat is grounded in an analysis result (AnalysisQueryTools active) |
| 726490002 | Hybrid | Both document and analysis context active |
| 726490003 | Knowledge | Chat uses knowledge base only (KnowledgeRetrievalTools active) |

### Boolean Field: sprk_isarchived

| True Label | False Label |
|------------|-------------|
| Archived | Active |

### Indexing Notes

- `sprk_sessionid` must support fast lookup — this is the primary key for session resumption.
- `sprk_tenantid` is filtered on every query for tenant isolation.
- Unique constraint: the combination of `sprk_sessionid` + `sprk_tenantid` should be unique (enforced in BFF layer, not at Dataverse schema level).

---

## Entity 3: sprk_aievaluationrun

**Purpose**: Aggregate record for a complete evaluation run. Stores overall metrics (Recall@K, nDCG@K) and metadata about the evaluation environment.

**Display Name**: AI Evaluation Run | **Plural**: AI Evaluation Runs
**Ownership**: Organization-owned
**Primary Name Field**: `sprk_name` (auto-number, e.g., "EVAL-000001")

### Fields

| Field Schema Name | Display Name | Type | Required | Max Length | Description |
|-------------------|--------------|------|----------|------------|-------------|
| `sprk_name` | Evaluation Run ID | Single Line of Text | Required | 200 | Auto-number primary name: `EVAL-{SEQNUM:6}` |
| `sprk_rundate` | Run Date | Date and Time | Required | — | When the evaluation started. Format: DateAndTime, behavior: UserLocal. |
| `sprk_environment` | Environment | Single Line of Text | Optional | 50 | Target environment name (e.g., "dev", "staging", "prod"). |
| `sprk_modelversion` | Model Version | Single Line of Text | Optional | 100 | Azure OpenAI model deployment name used (e.g., "gpt-4o-mini-2024-07-18"). |
| `sprk_corpusversion` | Corpus Version | Single Line of Text | Optional | 50 | Test corpus version identifier (e.g., "v1.0", "2026-02"). |
| `sprk_indexstate` | Index State | Single Line of Text | Optional | 100 | Description of the AI Search index state at run time (e.g., "knowledge-v3, discovery-v2"). |
| `sprk_recallatk` | Recall@K | Decimal Number | Optional | — | Aggregate Recall@K score for this run. Range: 0.0–1.0. Precision: 4 decimal places. |
| `sprk_ndcgatk` | nDCG@K | Decimal Number | Optional | — | Aggregate normalized Discounted Cumulative Gain@K score. Range: 0.0–1.0. Precision: 4 decimal places. |
| `sprk_status` | Status | Choice | Required | — | Evaluation run status. See option values below. |
| `sprk_evaluationtype` | Evaluation Type | Single Line of Text | Optional | 50 | Category of evaluation (e.g., "rag_eval", "chat_eval", "playbook_eval"). |
| `sprk_resultcount` | Result Count | Whole Number | Optional | — | Total number of sprk_aievaluationresult records in this run. Min: 0, Max: 999999. |
| `sprk_passedbcount` | Passed Count | Whole Number | Optional | — | Number of sprk_aievaluationresult records where sprk_passed = true. Min: 0, Max: 999999. |
| `sprk_reportjson` | Report JSON | Multiple Lines of Text | Optional | 1048576 | Full evaluation report in JSON format. Generated at completion. |
| `createdon` | Created On | Date and Time | Auto | — | Standard Dataverse audit field. |
| `modifiedon` | Modified On | Date and Time | Auto | — | Standard Dataverse audit field. |

### Choice Field: sprk_status

**Global Option Set Name**: `sprk_aievaluationrunstatus`

| Value | Label | Description |
|-------|-------|-------------|
| 726490000 | Pending | Run created but not yet started |
| 726490001 | Running | Evaluation actively in progress |
| 726490002 | Complete | Evaluation finished successfully |
| 726490003 | Failed | Evaluation failed; check logs |

---

## Entity 4: sprk_aievaluationresult

**Purpose**: Per-query evaluation result within an evaluation run. One record per question in the gold dataset. Stores the query, expected output, actual response, and per-query scores.

**Display Name**: AI Evaluation Result | **Plural**: AI Evaluation Results
**Ownership**: Organization-owned
**Primary Name Field**: `sprk_name` (auto-number, e.g., "RES-000001")

### Fields

| Field Schema Name | Display Name | Type | Required | Max Length | Description |
|-------------------|--------------|------|----------|------------|-------------|
| `sprk_name` | Result ID | Single Line of Text | Required | 200 | Auto-number primary name: `RES-{SEQNUM:6}` |
| `sprk_query` | Query | Multiple Lines of Text | Required | 1000 | The evaluation question posed to the system. |
| `sprk_expectedkeywords` | Expected Keywords | Multiple Lines of Text | Optional | 2000 | JSON array of expected keyword strings that should appear in the response (e.g., `["indemnification", "limitation of liability"]`). |
| `sprk_actualresponse` | Actual Response | Multiple Lines of Text | Optional | 10000 | The full response returned by the system for this query. |
| `sprk_retrievedchunksjson` | Retrieved Chunks JSON | Multiple Lines of Text | Optional | 20000 | JSON array of the retrieved RAG chunks that informed the response (for Recall@K calculation). |
| `sprk_recallatk` | Recall@K | Decimal Number | Optional | — | Per-query Recall@K score. Range: 0.0–1.0. Precision: 4 decimal places. |
| `sprk_ndcgatk` | nDCG@K | Decimal Number | Optional | — | Per-query nDCG@K score. Range: 0.0–1.0. Precision: 4 decimal places. |
| `sprk_passed` | Passed | Two Options | Optional | — | True if this query passed the evaluation threshold. |
| `sprk_failurereason` | Failure Reason | Single Line of Text | Optional | 500 | If sprk_passed = false, a brief description of why the query failed. |
| `sprk_playbookid` | Playbook ID | Single Line of Text | Optional | 100 | `sprk_aiplaybook` logical ID this query targets (e.g., "PB-002"). |
| `sprk_latencyms` | Latency (ms) | Whole Number | Optional | — | Response latency in milliseconds. Min: 0, Max: 999999. |
| `sprk_citationcount` | Citation Count | Whole Number | Optional | — | Number of citations in the actual response. Min: 0, Max: 999. |
| `createdon` | Created On | Date and Time | Auto | — | Standard Dataverse audit field. |
| `modifiedon` | Modified On | Date and Time | Auto | — | Standard Dataverse audit field. |

### Choice Field: sprk_passed (Boolean)

| True Label | False Label |
|------------|-------------|
| Passed | Failed |

### Relationship

| Relationship | Type | Description |
|-------------|------|-------------|
| `sprk_aievaluationrun_aievaluationresult_runid` | N:1 to `sprk_aievaluationrun` | Required. Results belong to a run. Lookup field: `sprk_runid`. Delete behavior: Cascade. |

---

## Relationship Diagram

```
sprk_aichatsummary (1)
│
│  sprk_sessionid (text, unique per tenant)
│  sprk_tenantid  (required — tenant isolation)
│  sprk_messagecount (counter, archive at 50 — NFR-12)
│  sprk_summary   (rolling summary of archived messages)
│  sprk_playbookid (which playbook this session uses)
│  sprk_contextmode (Document | Analysis | Hybrid | Knowledge)
│
└──(1:N)──► sprk_aichatmessage (N)
              sprk_sessionid    (text, indexed, join key)
              sprk_role         (User | Assistant | System)
              sprk_content      (max 10000 chars)
              sprk_tokencount   (optional, for cost tracking)
              sprk_sequencenumber (ordering within session)
              sprk_summaryid    (lookup to sprk_aichatsummary, optional)


sprk_aievaluationrun (1)
│
│  sprk_status     (Pending | Running | Complete | Failed)
│  sprk_recallatk  (aggregate score)
│  sprk_ndcgatk    (aggregate score)
│  sprk_evaluationtype (rag_eval | chat_eval | playbook_eval)
│  sprk_reportjson (full JSON report at completion)
│
└──(1:N)──► sprk_aievaluationresult (N)
              sprk_query            (the evaluation question)
              sprk_expectedkeywords (JSON array of expected terms)
              sprk_actualresponse   (what the system returned)
              sprk_recallatk        (per-query score)
              sprk_ndcgatk          (per-query score)
              sprk_passed           (bool — threshold met?)
              sprk_runid            (lookup to sprk_aievaluationrun, required)
```

---

## Global Option Sets Required

These must be created before the entities (Dataverse requirement):

| Option Set Name | Used By | Values |
|----------------|---------|--------|
| `sprk_aichatrole` | `sprk_aichatmessage.sprk_role` | 726490000=User, 726490001=Assistant, 726490002=System |
| `sprk_aichatcontextmode` | `sprk_aichatsummary.sprk_contextmode` | 726490000=Document, 726490001=Analysis, 726490002=Hybrid, 726490003=Knowledge |
| `sprk_aievaluationrunstatus` | `sprk_aievaluationrun.sprk_status` | 726490000=Pending, 726490001=Running, 726490002=Complete, 726490003=Failed |

**Note on option values**: Dataverse publisher prefix `sprk_` reserves the value range starting at 726490000. All custom option values use this base.

---

## Deployment Script

The PowerShell script to create these entities is located at:

```
projects/ai-spaarke-platform-enhancements-r1/scripts/Deploy-ChatEvaluationSchema.ps1
```

### Deployment Order

1. Create global option sets (`sprk_aichatrole`, `sprk_aichatcontextmode`, `sprk_aievaluationrunstatus`)
2. Create `sprk_aichatsummary` entity (parent — must exist before relationship)
3. Create `sprk_aichatmessage` entity
4. Create `sprk_aievaluationrun` entity (parent — must exist before relationship)
5. Create `sprk_aievaluationresult` entity
6. Create lookup relationship: `sprk_aichatmessage` → `sprk_aichatsummary` (sprk_summaryid)
7. Create lookup relationship: `sprk_aievaluationresult` → `sprk_aievaluationrun` (sprk_runid)
8. Add additional attributes to entities (those not included in entity creation body)
9. Publish customizations

### Running the Script

```powershell
# Prerequisites: Azure CLI logged in with account that has System Customizer role
az login

# Run the script
pwsh projects/ai-spaarke-platform-enhancements-r1/scripts/Deploy-ChatEvaluationSchema.ps1

# Verify with pac CLI (optional)
pac org who
```

---

## Entity Creation Status

| Entity | Schema Documented | Script Created | Deployed to spaarkedev1 |
|--------|------------------|----------------|------------------------|
| `sprk_aichatmessage` | YES (2026-02-23) | YES (2026-02-23) | Requires `az login` + manual script run |
| `sprk_aichatsummary` | YES (2026-02-23) | YES (2026-02-23) | Requires `az login` + manual script run |
| `sprk_aievaluationrun` | YES (2026-02-23) | YES (2026-02-23) | Requires `az login` + manual script run |
| `sprk_aievaluationresult` | YES (2026-02-23) | YES (2026-02-23) | Requires `az login` + manual script run |

**Note**: The Dataverse Web API requires an authenticated Azure CLI session. The `pac` CLI does not support `table create` or `column create` commands (as of v1.46+). A human operator must run `az login` then execute the deployment script. The script is idempotent — safe to run multiple times.

---

## ChatSessionManager Implementation Notes

This schema resolves the unresolved question in spec.md. The `ChatSessionManager` (AIPL-051) should implement the following pattern:

```csharp
// Session lookup by sessionId + tenantId
// Entity: sprk_aichatsummary
// Query: sprk_sessionid eq '{sessionId}' and sprk_tenantid eq '{tenantId}'

// Message persistence
// Entity: sprk_aichatmessage
// Ordered by: sprk_sequencenumber asc
// Filtered by: sprk_sessionid eq '{sessionId}'

// Summarization trigger: when sprk_aichatsummary.sprk_messagecount >= 15
// Archive trigger: when sprk_aichatsummary.sprk_messagecount >= 50 (NFR-12)

// Redis cache key pattern (ADR-014):
// "chat:session:{tenantId}:{sessionId}"
// TTL: 24 hours from last access (NFR-07)
```

---

## ADR Compliance Notes

| ADR | Constraint | How Schema Complies |
|-----|-----------|---------------------|
| ADR-002 | No AI processing in plugins | These are pure data entities — no plugin logic attached |
| ADR-009 | Redis-first caching | `sprk_aichatsummary.sprk_sessionid` is the Redis key; Dataverse is cold storage |
| ADR-014 | Tenant-scoped cache keys | `sprk_aichatsummary.sprk_tenantid` enables tenant isolation in cache keys |

---

*Schema designed for AIPL-001. Created 2026-02-23.*
