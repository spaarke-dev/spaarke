# Spaarke AI Strategy & Roadmap

> **Version**: 1.0
> **Last Updated**: February 21, 2026
> **Audience**: Business stakeholders, executives, product management
> **Purpose**: Strategic overview of Spaarke's AI platform direction, competitive positioning, and roadmap
> **Supersedes**: `docs/architecture/SPAARKE-AI-STRATEGY.md` (v2.1), `docs/architecture/Spaarke-Microsoft-IQ-ADOPTION-ANALYSIS.md`, `projects/ai-document-analysis-enhancements/Spaarke_AI_Architecture_and_Roadmap.md` (v1.0)
> **Technical Reference**: See `docs/architecture/AI-ARCHITECTURE.md` for component-level architecture

---

## Executive Summary

Spaarke builds **custom AI capabilities** integrated into its SharePoint Document Access Platform (SDAP). The platform targets the legal document intelligence market with a unique approach: **no-code AI workflow composition** through our Playbook System -- something Microsoft does not offer.

Our strategy is **"custom first, adopt selectively"**: we build on the same Azure primitives as Microsoft Foundry (Azure OpenAI, Azure AI Search, Document Intelligence) but maintain full control over implementation. This gives us domain-specific capabilities, multi-tenant isolation, and a Playbook System that serves as our primary product differentiator.

**Key strategic decisions:**

- **Playbooks are the product** -- the Spaarke-specific "frontend" for creating, managing, and executing AI workflows. Domain experts build analysis workflows without developers.
- **Backend flexibility** -- AI processing can run in-process (current), via Microsoft Agent Framework (future), or via AI Foundry Agent Service (future). The backend is an implementation detail that evolves independently.
- **Scope Library is Spaarke IP** -- reusable AI primitives (Actions, Skills, Knowledge, Tools) stored in Dataverse. These are independent of any execution engine and form the foundation of our composable AI platform.
- **AI Foundry is infrastructure, not competition** -- Foundry provides model hosting and runtime services. Our scope library and playbook system sit above it.

---

## Strategic Position

### What We Build (Spaarke AI Platform)

```
┌─────────────────────────────────────────────────────────────────────┐
│  SPAARKE-UNIQUE (No Microsoft Equivalent)                           │
│                                                                     │
│  ┌───────────────────────────────────────────────────────────────┐ │
│  │  Playbook System                                              │ │
│  │  • Visual canvas for AI workflow composition                  │ │
│  │  • Drag-and-drop node-based builder                           │ │
│  │  • Domain experts create workflows without code               │ │
│  │  • 10 pre-built playbooks (contract, NDA, lease, etc.)        │ │
│  │  • Shareable, versionable, auditable                          │ │
│  └───────────────────────────────────────────────────────────────┘ │
│                                                                     │
│  ┌───────────────────────────────────────────────────────────────┐ │
│  │  Scope Library (Reusable AI Primitives)                       │ │
│  │  • 8 Actions (system prompt templates)                        │ │
│  │  • 10 Skills (specialized prompt fragments)                   │ │
│  │  • 10 Knowledge sources (RAG context)                         │ │
│  │  • 8+ Tools (executable handlers)                             │ │
│  │  • Extensible: customers create their own scopes              │ │
│  └───────────────────────────────────────────────────────────────┘ │
│                                                                     │
│  ┌───────────────────────────────────────────────────────────────┐ │
│  │  Domain-Specific UX                                           │ │
│  │  • Custom PCF controls for Power Platform                     │ │
│  │  • SprkChat (conversational AI across all surfaces)           │ │
│  │  • Playbook Builder (visual canvas in Dataverse forms)        │ │
│  │  • AI Summary panels, analysis carousels                      │ │
│  └───────────────────────────────────────────────────────────────┘ │
│                                                                     │
│  CUSTOM IMPLEMENTATIONS (Full Control)                              │
│  ┌────────────────┐ ┌────────────────┐ ┌────────────────────────┐ │
│  │ Orchestration  │ │ Tool Handlers  │ │ Multi-tenant RAG       │ │
│  │ Engine         │ │ (C# plugins)   │ │ (tenantId isolation)   │ │
│  └────────────────┘ └────────────────┘ └────────────────────────┘ │
│                                                                     │
│  AZURE PRIMITIVES (Same as Microsoft Foundry Backend)               │
│  ┌────────────────┐ ┌────────────────┐ ┌────────────────────────┐ │
│  │ Azure OpenAI   │ │ Azure AI       │ │ Document Intelligence  │ │
│  │ (GPT-4, embed) │ │ Search         │ │                        │ │
│  └────────────────┘ └────────────────┘ └────────────────────────┘ │
└─────────────────────────────────────────────────────────────────────┘
```

### Why Custom vs Managed Services

| Aspect | Spaarke Custom | Microsoft Foundry (Managed) |
|--------|---------------|-----------------------------|
| **Playbook System** | Unique differentiator | No equivalent |
| **Multi-tenant RAG** | Custom tenantId filtering | Unknown/limited |
| **Domain-specific UI** | Custom PCF controls | Generic Copilot chat |
| **Chunking strategy** | Full control | Pre-built strategies |
| **Caching** | Redis with SHA256 keys | Managed (opaque) |
| **Cost optimization** | Direct control | Abstraction overhead |
| **Dataverse integration** | Deep entity integration | Limited |
| **Debugging** | Full observability | Black box |

### Microsoft Alignment

We use the **same Azure services** that Microsoft packages into Foundry:

| Component | Microsoft Service | Spaarke Usage | Alignment |
|-----------|------------------|---------------|-----------|
| Vector DB | Azure AI Search | Azure AI Search | **Identical** |
| Embeddings | Azure OpenAI | Azure OpenAI | **Identical** |
| LLM | Azure OpenAI (GPT-4) | Azure OpenAI (GPT-4) | **Identical** |
| Doc Processing | Document Intelligence | Document Intelligence | **Identical** |
| Identity | Entra ID | Entra ID | **Identical** |
| Hosting | Azure App Service | Azure App Service | **Identical** |

This alignment means migration to any future Foundry managed service is a **refactor, not a rewrite**.

---

## Four-Tier Architecture (Summary)

| Tier | What | Audience |
|------|------|---------|
| **1. Scope Library** | Reusable AI primitives (Actions, Skills, Knowledge, Tools) | Domain experts, admins |
| **2. Composition Patterns** | Playbooks, SprkChat, standalone API, background jobs | End users, developers |
| **3. Execution Runtime** | In-process engine today; Agent Framework tomorrow | Engineers |
| **4. Azure Infrastructure** | Azure OpenAI, AI Search, Doc Intel, Redis, Foundry | Infrastructure |

**Key insight**: Tiers 1-2 are Spaarke IP and product differentiators. Tiers 3-4 are pluggable infrastructure that can evolve without affecting the product.

> For detailed technical architecture, see `docs/architecture/AI-ARCHITECTURE.md`.

---

## Playbook System: The Product Differentiator

### What Playbooks Enable

Legal teams, compliance officers, and domain experts create AI workflows **without developers**:

```
Domain Expert creates "NDA Review" Playbook:

  ┌─────────┐     ┌─────────────┐     ┌───────────┐     ┌──────────┐
  │ Extract │────▶│ Analyze     │────▶│ Detect    │────▶│ Generate │
  │ Parties │     │ Scope       │     │ Risks     │     │ Report   │
  └─────────┘     └─────────────┘     └───────────┘     └──────────┘

  + Knowledge: "Standard NDA Terms"
  + Skills: "NDA Review", "Risk Assessment"
  + Output: Structured risk report
```

### Pre-Built Playbooks (Ship with Product)

| Playbook | Target | Complexity | Execution Time |
|----------|--------|-----------|----------------|
| Quick Document Review | Any document | Low | 30-60 sec |
| Full Contract Analysis | Contracts, amendments | High | 3-5 min |
| NDA Review | Non-disclosure agreements | Medium | 2-3 min |
| Lease Review | Commercial/residential leases | High | 3-4 min |
| Employment Contract Review | Employment agreements | Medium | 2-3 min |
| Invoice Validation | Invoices | Low | 15-30 sec |
| SLA Analysis | Service level agreements | Medium | 2-3 min |
| Due Diligence Review | Any (M&A context) | Medium | 1-2 min/doc |
| Compliance Review | Policies, contracts | Medium | 2-3 min |
| Risk-Focused Scan | Contracts, NDAs, leases | Low | 30-60 sec |

### Customer Customization

Customers can:
- Create custom playbooks using the visual builder
- Create custom scopes (actions, skills, knowledge, tools)
- Extend system scopes (SaveAs pattern with `BasedOnId`)
- Share playbooks across their organization
- Configure knowledge sources with their own reference materials

System scopes (SYS-*) are immutable Spaarke IP. Customer scopes (CUST-*) are fully editable.

---

## Deployment Models

### Model 1: Spaarke-Hosted

- AI resources in Spaarke's Azure subscription
- Multi-tenant with logical isolation (tenantId filtering)
- Spaarke meters usage and bills customer
- Guest Entra ID for customer users

### Model 2: Customer-Hosted (BYOK)

- AI resources in customer's Azure subscription
- Physical isolation (dedicated resources)
- Customer pays Azure directly
- Internal Entra ID for users
- Customer manages models via AI Foundry portal

Both models have **identical feature parity**. The deployment model is a billing/governance decision, not a capability decision.

---

## Microsoft Foundry Adoption Strategy

### Current Approach: "Custom First, Adopt Selectively"

```
┌─────────────────────────────────────────────────────────────────────┐
│                     Adoption Timeline                                │
├─────────────────────────────────────────────────────────────────────┤
│                                                                     │
│   Now ──────────────── June 2026 ──────────────── Post-GA          │
│    │                      │                          │              │
│    │  Build custom        │  Launch with             │  Evaluate    │
│    │  (proven, works)     │  custom stack            │  Foundry     │
│    │                      │                          │  adoption    │
│    │                      │                          │              │
│    └──────────────────────┴──────────────────────────┘              │
│                                                                     │
│   Key Principle: Build for flexibility, not for migration           │
│   Our architecture already uses Azure AI Search (same backend       │
│   as Foundry IQ), so migration is a refactor, not a rewrite.       │
└─────────────────────────────────────────────────────────────────────┘
```

### Foundry Service Adoption Decisions

| Foundry Service | Adoption | Rationale |
|-----------------|----------|-----------|
| **Foundry IQ** (managed RAG) | Evaluate post-GA (mid-2026) | May simplify RAG for simple queries; keep custom for advanced |
| **Agent Service** (runtime) | Evaluate post-GA (2026+) | Optional runtime for AI nodes; Playbooks remain as config layer |
| **Model Router** | Evaluate when GA | Cost optimization without changing architecture |
| **Control Plane** | Consider early | Governance/compliance without code changes |
| **Foundry Tools (MCP)** | Evaluate | May expose SPE/Dataverse as MCP tools |

**Principle**: Adopt managed services only when they **add clear value** without sacrificing our differentiators.

### M365 Copilot Integration (Model 2 Only)

For customers with M365 Copilot licenses:
- **Graph Connector**: Index SPE documents to Microsoft Search
- **Declarative Agent**: Spaarke-specific agent in customer's Copilot
- **Plugin**: Expose Spaarke AI as Copilot plugin

This is optional and customer-driven. Spaarke AI features work independently of Copilot.

---

## Architecture Component Blocks

The platform is organized into nine functional blocks:

### 1. AI Infrastructure & Core Services
Azure OpenAI, Azure AI Search, Document Intelligence, Redis, Service Bus, Entra ID, Key Vault.

### 2. Retrieval & Indexing Pipeline
Clause/section-aware chunking, metadata enrichment, hybrid retrieval (BM25 + vector + semantic reranking), multi-vector similarity.

### 3. Scope Library
Reusable AI primitives: Actions, Skills, Knowledge, Tools, Outputs. Canonical schemas, output contracts, versioning, customer extensibility.

### 4. Proprietary Knowledge Packs
Spaarke-owned legal intelligence: clause taxonomy, risk taxonomy, contract review templates, writing style guides, golden example outputs.

### 5. Client Ontology & Configuration
Client-specific accuracy overlays: standards intake templates, clause mapping, escalation rules, approval workflows, Draft-Review-Publish lifecycle.

### 6. Orchestration Runtime
Playbook Execution Engine: node execution by type (AI, Rule, Calculation, Workflow), structured outputs with validators, citation enforcement, cost controls.

### 7. Workflow & Systems Integration
Operationalize AI outputs: Dataverse updates, task creation, approval routing, notifications, document annotations, email delivery.

### 8. Evaluation & Telemetry
Continuous improvement: gold datasets, retrieval metrics (Recall@K, nDCG@K), output validation scoring, implicit feedback, A/B testing, regression testing.

### 9. Governance, Security & Tenant Control
Enterprise readiness: version control, deployment automation (IaC), audit logging, policy enforcement, tenant isolation.

---

## Phased Roadmap

### Phase 1: Retrieval Foundation
- Clause-aware chunking (section boundaries, paragraph-level)
- Metadata enrichment (client, contract type, governing law)
- Hybrid retrieval with semantic reranking
- Retrieval instrumentation and logging

### Phase 2: Similarity & Graph Accuracy
- Multi-vector document similarity
- Threshold filtering and confidence scoring
- Evidence-backed relationship graph
- Document comparison capabilities

### Phase 3: Structured Orchestration
- Canonical output schemas per playbook type
- Validator loop (structured output validation)
- Deterministic node execution with retry logic
- Agent Framework integration (IChatClient, AIAgent)

### Phase 4: Client Overlay System
- Standards pack intake templates
- Rule overlay binding (client rules on top of system rules)
- Client-specific evaluation datasets
- SprkChat with client-context awareness

### Phase 5: Enterprise Productization
- Deployment automation (IaC with Bicep)
- Telemetry dashboards (AI usage, quality metrics)
- Security hardening (PII handling, content filtering)
- Upgrade lifecycle management
- Evaluate Foundry IQ and Agent Service adoption

---

## Acceptance Gates

### Mid-Tier Capability (Target: June 2026 Launch)
- Hybrid retrieval with measurable improvement over keyword-only
- Clause-level similarity with explainable evidence
- Structured playbook outputs with citation enforcement
- 10 pre-built playbooks shipping with product
- Initial evaluation harness deployed
- Both deployment models operational

### Top-Tier Capability (Target: Post-Launch)
- Client-specific overlays active
- Regression testing automated with gold datasets
- Similarity graph accuracy validated
- Deployment automation to customer tenants
- Full telemetry and audit trail
- SprkChat deployed across all surfaces
- Agent Framework integration (backend flexibility)

---

## Cost Model

### Azure OpenAI Pricing (Estimated per Query)

| Operation | Tokens | Est. Cost |
|-----------|--------|-----------|
| RAG query (GPT-4 input) | ~4,000 | ~$0.04 |
| RAG response (GPT-4 output) | ~500 | ~$0.015 |
| Document embedding | ~1,000 | ~$0.0001 |
| **Total per RAG query** | | **~$0.055** |

### Azure AI Search

| SKU | Monthly | Document Capacity |
|-----|---------|------------------|
| S1 | ~$250 | 2M chunks |
| S2 | ~$1,000 | 10M chunks |

### Model 1 Cost Recovery Options

| Approach | Description |
|----------|-------------|
| **Bundled** | AI included in subscription tier |
| **Metered** | Track per-query, bill monthly |
| **Hybrid** | Included queries + overage charges |

---

## Technology Watch

| Technology | Interest | Notes |
|------------|----------|-------|
| Foundry IQ GA | High | Evaluate for RAG simplification |
| Foundry Agent Service GA | High | Optional runtime for AI nodes |
| Microsoft Agent Framework GA | High | Runtime evolution path |
| GPT-5+ / o3 | High | Performance improvements |
| Anthropic Claude (via Foundry) | Medium | Alternative for specific tasks |
| Model Router | Medium | Automatic cost optimization |
| LlamaParse | High | Superior document parsing (dual-parser router) |

---

## References

- [Azure AI Foundry Documentation](https://learn.microsoft.com/azure/ai-foundry/)
- [Microsoft Agent Framework](https://github.com/microsoft/agentframework)
- [Azure OpenAI Documentation](https://learn.microsoft.com/en-us/azure/ai-services/openai/)
- [Azure AI Search Documentation](https://learn.microsoft.com/en-us/azure/search/)
- [Azure Document Intelligence](https://learn.microsoft.com/en-us/azure/ai-services/document-intelligence/)

---

## Changelog

| Date | Version | Change |
|------|---------|--------|
| 2026-02-21 | 1.0 | Created from consolidation of SPAARKE-AI-STRATEGY.md (v2.1), Spaarke-Microsoft-IQ-ADOPTION-ANALYSIS.md, and Spaarke_AI_Architecture_and_Roadmap.md (v1.0). Added four-tier architecture summary, playbooks-as-frontend positioning, backend flexibility model, updated competitive landscape. |
