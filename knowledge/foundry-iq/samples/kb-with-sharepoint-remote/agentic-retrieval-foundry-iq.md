# Agentic Retrieval & Foundry IQ — Cross-Site Enterprise Search

## Table of Contents

- [Overview](#overview)
- [What Is Agentic Retrieval?](#what-is-agentic-retrieval)
- [What Is Foundry IQ?](#what-is-foundry-iq)
- [How This Project Fits In](#how-this-project-fits-in)
- [Cross-Site Search: The Enterprise Problem](#cross-site-search-the-enterprise-problem)
- [Enterprise Scenarios](#enterprise-scenarios)
  - [Scenario 1 — M&A Due Diligence (Legal + Finance + HR)](#scenario-1--ma-due-diligence-legal--finance--hr)
  - [Scenario 2 — Global Product Launch (Marketing + Engineering + Compliance)](#scenario-2--global-product-launch-marketing--engineering--compliance)
  - [Scenario 3 — Employee Onboarding Assistant (HR + IT + Operations)](#scenario-3--employee-onboarding-assistant-hr--it--operations)
  - [Scenario 4 — Incident Response (Security + Infrastructure + Compliance)](#scenario-4--incident-response-security--infrastructure--compliance)
  - [Scenario 5 — Customer Support Escalation (Support + Product + Legal)](#scenario-5--customer-support-escalation-support--product--legal)
- [Architecture: From Single-Site Sync to Multi-Site Agentic Search](#architecture-from-single-site-sync-to-multi-site-agentic-search)
- [Two Approaches to SharePoint Knowledge Sources](#two-approaches-to-sharepoint-knowledge-sources)
- [Permission Enforcement Across Sites](#permission-enforcement-across-sites)
- [Getting Started](#getting-started)

---

## Overview

This document explains how **Azure AI Search Agentic Retrieval** and **Microsoft Foundry IQ** extend the capabilities of this project beyond single-site sync — enabling cross-SharePoint-site AI search that decomposes complex questions, retrieves from multiple knowledge domains simultaneously, and enforces permissions at every step.

> **Key insight**: This project provides the secure, permission-aware indexing pipeline. Agentic Retrieval / Foundry IQ provide the intelligent multi-source query layer on top. Together, they form a complete enterprise knowledge platform.

---

## What Is Agentic Retrieval?

**Agentic Retrieval** is a multi-query pipeline in Azure AI Search (preview, API `2025-11-01-preview`) designed for complex agent-and-chat workflows. Instead of sending one query to one index, it:

1. **Query Planning** — An LLM (GPT-4o / GPT-4.1 / GPT-5) analyzes the full chat thread and decomposes a complex question into focused subqueries.
2. **Parallel Execution** — Subqueries run simultaneously against one or more **knowledge sources** (search indexes, remote SharePoint sites, Bing).
3. **Semantic Reranking** — Each subquery's results are reranked for relevance (L2 reranking).
4. **Result Merging** — The best results are combined into a unified three-part response:
   - **Grounding data** — content for the LLM to generate an answer
   - **References** — source documents with URLs and metadata for citation
   - **Activity plan** — query execution steps for transparency

### Example

A user asks:

> *"Compare the data retention policy from the Legal site with the GDPR compliance checklist on the Compliance site, and tell me if we have any gaps."*

Without agentic retrieval → a single keyword/vector search returns partial results from whichever index happens to match best.

With agentic retrieval → the LLM decomposes this into:
- **Subquery 1**: "data retention policy" → targets the Legal SharePoint knowledge source
- **Subquery 2**: "GDPR compliance checklist" → targets the Compliance SharePoint knowledge source
- **Subquery 3**: "gaps between data retention and GDPR requirements" → targets both sources

Results are merged, reranked, and the agent synthesizes a gap analysis grounded in the actual documents.

---

## What Is Foundry IQ?

**Foundry IQ** is the managed knowledge layer in Microsoft Foundry that uses agentic retrieval under the hood. It provides:

| Capability | Description |
|------------|-------------|
| **Multi-source knowledge bases** | Connect one knowledge base to multiple knowledge sources — Azure Blob (indexed), SharePoint (remote or indexed), OneLake, Bing web |
| **Automated indexing** | Auto-generates the full indexer pipeline (data source, skillset, index, indexer) from a knowledge source definition |
| **ACL synchronization** | Syncs access control lists for supported sources and honors Microsoft Purview sensitivity labels |
| **Permission-aware queries** | Runs queries under the caller's Entra identity — agents return only content the user is authorized to see |
| **MCP integration** | Exposes knowledge bases as MCP (Model Context Protocol) endpoints, so Foundry agents call them as tools |
| **Answer synthesis** | Optional mode where the knowledge base returns natural-language answers instead of raw content |

### Relationship to Other IQ Layers

| Layer | Scope | Source |
|-------|-------|--------|
| **Foundry IQ** | Enterprise data — documents, files, knowledge bases | Azure AI Search + Blob + SharePoint + OneLake |
| **Fabric IQ** | Business data — analytics, semantic models, Power BI | Microsoft Fabric / OneLake |
| **Work IQ** | Collaboration signals — meetings, chats, workflows | Microsoft 365 |

All three layers can be combined in a single agent for comprehensive organizational context.

---

## How This Project Fits In

This project — **FoundryIQ Secure Sync** — solves the hardest part of the pipeline: **secure, permission-aware ingestion from SharePoint into Azure AI Search**. Here's how it maps to the Foundry IQ architecture:

```
┌─────────────────────────────────────────────────────────────────┐
│                    Foundry IQ Knowledge Base                     │
│                                                                  │
│  ┌──────────────────┐  ┌──────────────────┐  ┌───────────────┐ │
│  │ Knowledge Source  │  │ Knowledge Source  │  │ Knowledge     │ │
│  │ (Indexed - Blob)  │  │ (Remote - SP)    │  │ Source (Bing)  │ │
│  └────────┬─────────┘  └────────┬─────────┘  └──────┬────────┘ │
│           │                      │                    │          │
└───────────┼──────────────────────┼────────────────────┼──────────┘
            │                      │                    │
    ┌───────▼───────┐    ┌────────▼────────┐    ┌──────▼──────┐
    │ Azure AI       │    │ SharePoint via  │    │ Bing Web    │
    │ Search Index   │    │ Copilot         │    │ Search API  │
    │ (THIS PROJECT  │    │ Retrieval API   │    │             │
    │  populates     │    │ (live query)    │    │             │
    │  this index)   │    │                 │    │             │
    └───────▲───────┘    └─────────────────┘    └─────────────┘
            │
    ┌───────┴───────────────────────────────────┐
    │       FoundryIQ Secure Sync Pipeline       │
    │                                            │
    │  SharePoint ──► Blob ──► AI Search Index   │
    │  + Delta sync   + ACL     + OCR/Chunking   │
    │  + Purview/RMS  metadata  + Embeddings     │
    │  + Permissions            + ACL fields     │
    └────────────────────────────────────────────┘
```

**This project = the "Indexed Blob" knowledge source**, providing:
- Deep ingestion with OCR, chunking, and vector embeddings
- Dual-layer ACL (SharePoint permissions ∩ Purview/RMS)
- Delta sync for efficiency
- Full control over index schema, skillsets, and scoring profiles

**Remote SharePoint knowledge source** = complements this project by querying SharePoint sites live (no index needed), but with limitations:
- Text only (no OCR for images/scanned PDFs)
- No custom chunking or embeddings
- Requires a Copilot license

---

## Cross-Site Search: The Enterprise Problem

In every large enterprise, knowledge is fragmented across dozens of SharePoint sites:

| Department | SharePoint Site | Content |
|------------|----------------|---------|
| Legal | `/sites/Legal` | Contracts, NDAs, compliance policies, regulatory filings |
| HR | `/sites/HumanResources` | Employee handbooks, salary bands, benefits, org charts |
| Finance | `/sites/Finance` | Budgets, forecasts, audit reports, tax documents |
| Engineering | `/sites/Engineering` | Architecture docs, runbooks, postmortems, API specs |
| Marketing | `/sites/Marketing` | Campaign plans, brand guidelines, competitor analyses |
| Compliance | `/sites/Compliance` | GDPR checklists, SOX controls, data classification policies |
| IT | `/sites/IT` | Network diagrams, security baselines, vendor evaluations |

**The problem**: When a question spans multiple domains, users have to manually search each site, mentally correlate the results, and hope they have access to the right documents. This is slow, error-prone, and completely incompatible with AI-powered workflows.

**The solution**: This project syncs each site (or multiple sites) into a unified Azure AI Search index with per-document ACLs. Foundry IQ / Agentic Retrieval then queries across all knowledge sources simultaneously, decomposes complex questions, and returns permission-trimmed answers.

---

## Enterprise Scenarios

### Scenario 1 — M&A Due Diligence (Legal + Finance + HR)

**Context**: A publicly traded company is evaluating the acquisition of a competitor. The due diligence team needs to correlate information across three normally siloed departments.

**User question (to the AI agent)**:
> *"What are the key financial risks in acquiring TargetCorp, considering our existing contractual obligations, outstanding litigation, and any potential workforce restructuring costs?"*

**Without cross-site search**: A lawyer searches the Legal site for contracts mentioning TargetCorp. A finance analyst separately searches their site for TargetCorp financials. An HR partner manually looks up headcount overlap. Three people, three searches, no correlation, days of work.

**With Agentic Retrieval + this pipeline**:

| Subquery | Knowledge Source | Results |
|----------|-----------------|---------|
| "TargetCorp contractual obligations exclusivity" | Legal site index (indexed via this pipeline) | Non-compete clause in vendor agreement expiring Q4 2026 |
| "TargetCorp litigation pending claims" | Legal site index | Patent infringement suit — estimated liability $12M |
| "TargetCorp financial projections revenue risk" | Finance site index | Revenue declining 8% YoY, 3 customer contracts up for renewal |
| "workforce restructuring costs headcount overlap" | HR site index | 340 overlapping roles, estimated severance $28M |

**Security enforcement**: The agent only returns documents the user's Entra ID groups authorize. The HR salary details are visible only to the HR VP and above — other due diligence team members see the headcount analysis but not individual compensation data.

**Purview sensitivity**: The financial projections carry a "Highly Confidential" Purview label. The pipeline computed the ACL intersection (SharePoint ∩ RMS), so only users in both the Finance SharePoint site AND the RMS policy see these documents in search results.

---

### Scenario 2 — Global Product Launch (Marketing + Engineering + Compliance)

**Context**: A SaaS company is launching a new AI feature in the EU and US simultaneously. The product manager needs to ensure alignment across three teams.

**User question**:
> *"Is our new AI feature launch-ready? Check if the technical documentation is complete, the marketing materials comply with EU AI Act requirements, and our data processing agreements cover the new data flows."*

**Agentic retrieval decomposition**:

| Subquery | Knowledge Source | What it finds |
|----------|-----------------|---------------|
| "AI feature technical documentation completeness API specs" | Engineering site index | API docs are final, but the model card hasn't been reviewed |
| "AI feature marketing materials EU AI Act compliance" | Marketing site index + Compliance site index | Campaign copy references "AI-powered decisions" — requires a human-in-the-loop disclaimer under EU AI Act Article 14 |
| "data processing agreements new data flows AI feature" | Legal site index | Current DPA with EU sub-processor doesn't cover LLM inference data — needs amendment |

**Cross-site value**: Without unified search, the compliance gap in the marketing copy and the missing DPA amendment wouldn't be discovered until a lawyer happens to review the campaign, likely post-launch. Agentic retrieval catches this proactively.

---

### Scenario 3 — Employee Onboarding Assistant (HR + IT + Operations)

**Context**: A global enterprise with 30,000 employees wants an AI-powered onboarding assistant. New hires ask natural language questions and get answers grounded in actual company documents.

**User question (from a new hire in London)**:
> *"What's the process for getting a laptop, setting up VPN, and enrolling in health benefits in the UK?"*

**Agentic retrieval decomposition**:

| Subquery | Knowledge Source | What it finds |
|----------|-----------------|---------------|
| "new hire laptop provisioning process" | IT site index | ServiceNow ticket template, 3-5 day SLA, requires manager approval |
| "VPN setup instructions remote access" | IT site index | GlobalProtect VPN guide, MFA enrollment steps |
| "UK health benefits enrollment process" | HR site index (filtered by region) | BUPA enrollment via Workday, 30-day deadline from start date, dental is opt-in |

**Permission-aware**: The new hire sees the IT setup guides (accessible to all employees) and their region's benefits info, but NOT US benefits documents, salary bands for other levels, or IT security incident runbooks.

**Multi-site scaling**: The company runs this pipeline for each of their 12 SharePoint sites. Foundry IQ consolidates all 12 indexed knowledge sources into one knowledge base. Adding a new department site is just another pipeline instance + knowledge source reference.

---

### Scenario 4 — Incident Response (Security + Infrastructure + Compliance)

**Context**: A financial services company detects unusual activity on a production database. The security team needs to correlate multiple knowledge domains in real time.

**User question (from the SOC analyst)**:
> *"What are the access controls on the customer PII database, when was the last penetration test, and what's our notification obligation under SOX if data was exfiltrated?"*

**Agentic retrieval decomposition**:

| Subquery | Knowledge Source | What it finds |
|----------|-----------------|---------------|
| "customer PII database access controls RBAC" | IT/Security site index | DB secured with AAD groups, last access review December 2025, 14 privileged accounts |
| "penetration test results customer database" | Security site index | Last pentest March 2025, 2 medium findings (patched), next scheduled June 2026 |
| "SOX notification obligation data exfiltration PII" | Compliance site index | SOX Section 302: material breach requires CFO notification within 48 hours, SEC filing if >10K records |
| "incident response runbook database breach" | IT site index | Runbook: isolate DB, preserve logs, engage DFIR team, legal hold on backups |

**Why this matters**: During an incident, every minute counts. Instead of four analysts searching four sites, one query returns everything the SOC analyst needs, correctly security-trimmed (the analyst can see the pentest summary but not the full vulnerability details, which are restricted to the AppSec team).

---

### Scenario 5 — Customer Support Escalation (Support + Product + Legal)

**Context**: A B2B SaaS company's support agent is handling an escalation from an enterprise customer threatening to churn.

**User question**:
> *"The customer Acme Corp says our SLA was breached last month. What does their contract say about SLA commitments, what were the actual uptime numbers, and do we owe them service credits?"*

**Agentic retrieval decomposition**:

| Subquery | Knowledge Source | What it finds |
|----------|-----------------|---------------|
| "Acme Corp contract SLA commitment uptime guarantee" | Legal site index | Contract #2024-AC-0891: 99.95% monthly uptime SLA, 10% service credit per 0.1% below target |
| "Acme Corp uptime metrics last month availability" | Engineering/Ops site index | February 2026 uptime: 99.91% (two incidents: 12 min + 22 min) |
| "service credit calculation SLA breach process" | Finance site index | Finance runbook: credit = (breach % ÷ 0.1) × 10% × monthly invoice. Requires VP approval over $50K |

**Resolution**: The agent calculates that 99.91% vs 99.95% = 0.04% breach = approximately 4% service credit. It surfaces the exact contract clause, the incident logs, and the finance approval process — all from three different SharePoint sites, in one response.

---

## Architecture: From Single-Site Sync to Multi-Site Agentic Search

### Phase 1: Current State (This Project)

```
SharePoint Site A ──► Sync Pipeline ──► Blob ──► AI Search Index A
                      (this project)              (ACL-secured)
```

Single site, single index, search with OData security filters.

### Phase 2: Multi-Site Indexed Search

```
SharePoint Site A ──► Sync Pipeline ──► Blob A ──► AI Search Index
SharePoint Site B ──► Sync Pipeline ──► Blob B ──►  (unified or per-site)
SharePoint Site C ──► Sync Pipeline ──► Blob C ──►
```

Run multiple pipeline instances (different `SHAREPOINT_SITE_URL`), same destination index or federated indexes.

### Phase 3: Foundry IQ Agentic Search

```
┌──────────────────────────────────────────────────────────┐
│                Foundry IQ Knowledge Base                   │
│                                                           │
│  ┌─────────────┐ ┌─────────────┐ ┌─────────────────────┐ │
│  │ KS: Legal    │ │ KS: Finance │ │ KS: SharePoint      │ │
│  │ (Indexed     │ │ (Indexed    │ │ (Remote — live       │ │
│  │  via this    │ │  via this   │ │  query via Copilot   │ │
│  │  pipeline)   │ │  pipeline)  │ │  Retrieval API)      │ │
│  └─────────────┘ └─────────────┘ └─────────────────────┘ │
│                                                           │
│              Agentic Retrieval Engine                      │
│  ┌──────────────────────────────────────────────────────┐ │
│  │ 1. LLM query planning (GPT-4o / GPT-4.1)           │ │
│  │ 2. Parallel subquery execution                       │ │
│  │ 3. Semantic reranking                                │ │
│  │ 4. Result merging with citations                     │ │
│  │ 5. Permission enforcement (Entra ID + Purview)       │ │
│  └──────────────────────────────────────────────────────┘ │
└────────────────┬─────────────────────────────────────────┘
                 │ MCP endpoint
                 ▼
        ┌────────────────┐
        │ Foundry Agent   │
        │ Service         │
        │ (or any MCP     │
        │  client)        │
        └────────────────┘
```

---

## Two Approaches to SharePoint Knowledge Sources

Agentic retrieval supports two types of SharePoint knowledge sources. This project powers the first; the second complements it:

| Feature | Indexed (via this pipeline) | Remote (live SharePoint query) |
|---------|---------------------------|-------------------------------|
| **How it works** | Files synced to Blob → indexed in AI Search | Copilot Retrieval API queries SharePoint on-the-fly |
| **Indexing required** | Yes — this pipeline handles it | No — no index needed |
| **OCR / scanned PDFs** | ✅ Full OCR via AI Search skillset | ❌ Text content only |
| **Custom chunking** | ✅ Configurable chunk size/overlap | ❌ No chunking control |
| **Vector embeddings** | ✅ text-embedding-3-large | ❌ No vector search |
| **Custom scoring profiles** | ✅ Full control | ❌ No customization |
| **ACL enforcement** | ✅ Dual-layer (SharePoint ∩ Purview/RMS) | ✅ SharePoint + Purview labels honored |
| **Latency** | Low (pre-indexed) | Higher (live query) |
| **Freshness** | Depends on sync frequency | Real-time |
| **License requirement** | Standard Azure AI Search | Microsoft 365 Copilot license |
| **Best for** | Heavy search workloads, OCR documents, full control | Quick setup, real-time content, light workloads |
| **Cross-site filtering** | Via multiple pipeline instances | Via KQL filter: `SiteID:"<guid>" OR SiteID:"<guid>"` |

### When to use which?

- **Use Indexed (this pipeline)** when you need OCR, custom chunking, vector search, or handle Purview-encrypted files with RMS permission intersection
- **Use Remote** when you need real-time freshness, quick proof-of-concept, or sites where OCR is not needed
- **Use both together** in a single knowledge base — index the critical sites with this pipeline, add remote sources for supplementary sites

---

## Permission Enforcement Across Sites

Cross-site search creates a compound permission challenge. Here's how each layer handles it:

### Layer 1: SharePoint Permissions (Per-Site)

Each SharePoint site has its own permission model. When this pipeline syncs multiple sites:
- Site A may grant access to Group-Legal
- Site B may grant access to Group-Finance
- A user in both groups sees documents from both sites
- A user in only Group-Legal sees only Legal documents

The pipeline writes `acl_user_ids` and `acl_group_ids` into blob metadata per document. AI Search filters at query time.

### Layer 2: Purview Sensitivity Labels (Cross-Site)

Purview labels are tenant-wide. A "Highly Confidential" label on a Legal document and a Finance document uses the same RMS policy. This pipeline computes the intersection:

```
Final ACL = SharePoint permissions ∩ RMS permissions
```

### Layer 3: Agentic Retrieval (Query-Time)

When a Foundry IQ knowledge base processes a retrieve request:
- **Indexed sources**: AI Search applies the OData security filter using the user's Entra ID groups
- **Remote SharePoint sources**: The Copilot Retrieval API runs under the user's identity (`x-ms-query-source-authorization` header), so only content the user can access is returned

All three layers work together. A document must pass all applicable permission checks to appear in search results.

### Layer 4: Native Purview Sensitivity Label Enforcement (Preview)

Azure AI Search now offers a **native Purview integration** (preview, `2025-11-01-preview`) that extracts sensitivity labels during indexing and enforces label-based access at query time using the user's Entra token. When GA, this adds a **fourth enforcement layer**:

- The indexer extracts `metadata_sensitivity_label` and stores it in a `sensitivityLabel` field
- At query time, AI Search checks the label against Purview policies — only users with `READ` usage rights see the document
- This is **complementary** to this pipeline's custom ACL approach: native handles label-level enforcement, the pipeline handles RMS permission intersection (which the native feature does not do)

See [purview-rms-explained.md](purview-rms-explained.md#native-purview-sensitivity-label-integration-in-azure-ai-search-preview) for a detailed comparison and the recommended hybrid approach.

---

## Getting Started

### Step 1: Deploy This Pipeline for Multiple Sites

Run the sync pipeline for each SharePoint site you want to index:

```bash
# Site 1 — Legal
SHAREPOINT_SITE_URL=https://contoso.sharepoint.com/sites/Legal \
AZURE_BLOB_PREFIX=legal/ \
python sync/main.py

# Site 2 — Finance
SHAREPOINT_SITE_URL=https://contoso.sharepoint.com/sites/Finance \
AZURE_BLOB_PREFIX=finance/ \
python sync/main.py

# Site 3 — HR
SHAREPOINT_SITE_URL=https://contoso.sharepoint.com/sites/HR \
AZURE_BLOB_PREFIX=hr/ \
python sync/main.py
```

### Step 2: Create Knowledge Sources in Azure AI Search

```python
from azure.search.documents.indexes import SearchIndexClient
from azure.identity import DefaultAzureCredential

client = SearchIndexClient(
    endpoint="https://my-search.search.windows.net",
    credential=DefaultAzureCredential()
)

# Indexed knowledge source (from this pipeline)
legal_ks = {
    "name": "legal-knowledge-source",
    "type": "azureSearchIndex",
    "indexName": "sharepoint-legal-index",
    "description": "Legal department documents — contracts, NDAs, compliance policies"
}

# Remote SharePoint knowledge source (live query, no index)
marketing_ks = {
    "name": "marketing-remote-ks",
    "type": "sharePoint",
    "description": "Marketing site — campaigns, brand guidelines",
    "filterExpression": 'SiteID:"<marketing-site-guid>"'
}
```

### Step 3: Create a Knowledge Base in Foundry IQ

```python
knowledge_base = {
    "name": "enterprise-knowledge-base",
    "description": "Cross-department enterprise knowledge",
    "knowledgeSources": [
        {"name": "legal-knowledge-source"},
        {"name": "finance-knowledge-source"},
        {"name": "hr-knowledge-source"},
        {"name": "marketing-remote-ks"}
    ],
    "models": {
        "queryPlanning": {
            "azureOpenAIModel": {
                "deploymentName": "gpt-4o",
                "endpoint": "https://my-aoai.openai.azure.com/"
            }
        }
    },
    "retrievalReasoningEffort": "medium"
}
```

### Step 4: Connect to a Foundry Agent

```python
from azure.ai.projects import AIProjectClient

project_client = AIProjectClient(
    endpoint=project_endpoint,
    credential=DefaultAzureCredential()
)

# Create agent with MCP tool pointing to knowledge base
agent = project_client.agents.create(
    model="gpt-4o",
    name="enterprise-knowledge-agent",
    instructions="""You are an enterprise knowledge assistant.
    Use the knowledge base tool to answer questions.
    Always cite source documents. Respect that some content
    may not be available due to permission restrictions.""",
    tools=[{
        "type": "mcp",
        "mcp": {
            "connectionId": kb_connection_id
        }
    }]
)
```

---

## References

- [Agentic Retrieval Overview](https://learn.microsoft.com/azure/search/agentic-retrieval-overview)
- [Foundry IQ — What Is It?](https://learn.microsoft.com/azure/ai-foundry/agents/concepts/what-is-foundry-iq)
- [Create a Knowledge Base](https://learn.microsoft.com/azure/search/agentic-retrieval-how-to-create-knowledge-base)
- [Remote SharePoint Knowledge Source](https://learn.microsoft.com/azure/search/agentic-knowledge-source-how-to-sharepoint-remote)
- [Tutorial: End-to-End Agentic Retrieval Pipeline](https://learn.microsoft.com/azure/search/agentic-retrieval-how-to-create-pipeline)
- [Purview/RMS in This Project](purview-rms-explained.md)
