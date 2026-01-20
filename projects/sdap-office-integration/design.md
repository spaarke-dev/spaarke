# design.md — SDAP Office Integration (Outlook + Word Add-ins)

Date: 2026-01-19
Audience: Claude Code (primary), Spaarke Engineering/Product (secondary)
Scope: Outlook and Word add-ins with shared Office Integration Platform

## Project Context

This is **Project 1 of 3** in the Spaarke Office + Teams integration initiative:
1. **SDAP-office-integration** (this project) — Outlook + Word add-ins with shared platform
2. SDAP-teams-app — Teams integration (depends on APIs from this project)
3. SDAP-external-portal — Power Pages external collaboration (parallel development possible)

---

## 1. Objective

Create a unified Office Integration Platform that enables:
- **Outlook add-in**: "Save to Spaarke" (read mode) and "Share from Spaarke" (compose mode)
- **Word add-in**: "Save/Version to Spaarke", "Open from Spaarke", "Share/Grant access"
- **Shared platform**: Common task pane UI, host adapters, backend APIs, job/status tracking

This project establishes the foundation that Teams and External Portal projects will build upon.

### Consistency Requirements

Claude Code must produce a detailed spec consistent with:
- Spaarke ADR constraints (Minimal APIs, endpoint-seam auth, SDAP SpeFileStore abstraction)
- SDAP/SPE access model and UAC enforcement
- Best practices for Office add-ins as of Jan 2026

---

## 2. Design Principles and Non-Negotiables

### 2.1 DMS Centricity

- Matters and Projects are the primary "workspace" containers
- "Save to Spaarke" is a filing workflow: item must be associated to Matter/Project/Invoice (or staged in Inbox/Unfiled workspace)

### 2.2 Single Canonical File + Multi-Context Association

- A document binary has a single canonical storage location (SPE)
- The same document can be associated to multiple workspaces (Matter(s), Project(s)) via relationship records (no duplicate files)

### 2.3 Authorization is Server-Side

- Office add-ins never bypass Spaarke authorization
- All file operations are mediated by Spaarke APIs and SDAP seams (SpeFileStore), with UAC controlling access
- Add-in UI should not attempt to infer authorization beyond user-friendly warnings; server is source of truth

### 2.4 Asynchronous Processing is First-Class

- Upload, profiling, indexing, and deeper AI analysis are asynchronous
- UX is job/status-based with resumable history, not blocking modal wizards

---

## 3. Deployment Models

We maintain two deployment models:
- **Spaarke-hosted (SaaS)**: lowest-friction default; user-initiated add-in flows
- **Customer-tenant hosted**: optional high-trust automation, deeper Graph consent, shared mailbox capture, enterprise compliance

Graph usage is split into two surfaces:
1. **Graph for SPE/SDAP file ops** (core): required, but hidden behind SDAP SpeFileStore
2. **Graph for mailbox automation** (optional): only required if we implement server-side ingestion, shared mailbox automation, policy-driven capture

Architectural decision:
- Implement a reusable Graph client + token strategy "plumbing" now (common components, seams, error mapping, observability)
- Gate mailbox automation features behind configuration/feature flags to avoid forcing high-permission consent in SaaS V1

---

## 4. Unified Office Integration Platform Concept

Treat Outlook + Word as two hosts for the same Office Integration Platform:
- A shared web UI (task pane) and routing model ("Save", "Share", "Other Actions", status history)
- Host adapters (Outlook adapter, Word adapter) provide context:
    - Outlook: selected email, attachments, recipients
    - Word: current document, content, metadata, save/open context

### Platform Components

| Component | Responsibility |
|-----------|----------------|
| Shared Task Pane UI | React components for filing, sharing, status |
| Host Adapter Interface | Abstract context retrieval from Office host |
| Outlook Adapter | Email context, attachments, recipients |
| Word Adapter | Document context, content, versioning |
| API Client | Typed client for /office/* endpoints |
| Job Status Manager | Polling/SSE for job progress |

---

## 5. UX Patterns — Outlook Add-in

### 5.1 Placement and Command Model

- Add-in provides a **Spaarke dropdown menu** on relevant command surfaces:
    - Read: message toolbar / reading pane context
    - Compose: compose ribbon / toolbar
- Menu contains stable "forever" actions + "Other Actions":
    - Stable actions: Save to Spaarke, Insert Spaarke Link, Attach Copy, Create Matter/Project/Invoice
    - "Other Actions" opens task pane route with configurable/expandable action catalog

### 5.2 Read-Mode: Save to Spaarke (Filing Workflow)

Task pane flow:
1. **Select destination**: Matter / Project / Invoice (tabs or segmented control)
    - Search/typeahead, Recent, Favorites
    - "Create new..." inline Quick Create (minimal required fields only)
2. **Select what to save**:
    - Email body (default on)
    - Attachments (default on; allow per-attachment toggle)
3. **Processing defaults and toggles** (policy-driven):
    - Profile summary: default on
    - RAG index: default on where policy allows
    - Deep analysis: optional (cost/sensitivity dependent)
4. **Submit**
5. **Show Job Status view immediately** (see Section 7)

### 5.3 Compose-Mode: Share from Spaarke

User chooses either:
- Insert Spaarke link(s) (default)
- Attach copy from Spaarke (explicit export)

Compose task pane flow:
1. Identify recipients from To/CC; classify internal vs external (best-effort)
2. Select document(s) from Spaarke:
   - Matter-first search, then global search
3. Choose share mode per doc:
    - Link
    - Attach copy
4. "Grant access to recipients who lack access" (checkbox / policy-controlled)
5. On submit:
    - Insert links and/or attachments into compose window
    - If Grant Access selected: create invitations/grants for recipients and include registration/access instructions in email template

---

## 6. UX Patterns — Word Add-in

Word add-in parallels Outlook but uses document context:
- Stable dropdown + task pane

### 6.1 Core Actions

- **Save to Spaarke**: Associate this document to Matter/Project/Invoice; create new workspace if needed
- **Save new version to Spaarke**: If doc already linked, update version lineage
- **Open from Spaarke**: Search and open document into Word
- **Share / Insert link / Attach copy**: Same model as Outlook compose
- **Grant access**: For external collaboration (creates invitation via External Portal project)

### 6.2 Versioning Behavior

- Word add-in aligns to Spaarke's version model:
    - If file originated from Spaarke: "Save version" updates the existing document version lineage
    - If file is local/non-Spaarke: "Save to Spaarke" creates new document record, then subsequent saves version it

---

## 7. Job/Status UX and Processing Model

### 7.1 Backend Processing Stages (User-Meaningful)

Implement a small set of user-visible stages:
1. Records created
2. File uploaded to SPE
3. Profile summary completed
4. Indexed for search/RAG
5. Deep analysis completed (optional)

Each stage has: Pending / In progress / Completed / Needs attention

### 7.2 Immediate Acknowledgement Contract

- API returns quickly with:
    - Document ID (or pending document stub ID)
    - Job ID and current status
- Task pane shows:
    - Stage-based status
    - Links: Open Document, Open Matter/Project/Invoice, View Analysis Results (enabled when ready)
    - "Recent activity" list for resumability

### 7.3 Failure Modes

- If upload fails: provide retry and/or "download package" fallback only if policy allows
- If analysis fails: document still exists; allow re-run analysis from Spaarke (not required in add-in UI v1)

---

## 8. Security and Authorization

### 8.1 Authorization Seams

- All public endpoints enforce authN/authZ at endpoint boundaries (filters/policies)
- SpeFileStore isolates SPE/Graph operations (Graph types do not leak to callers)
- Introduce MailGraphStore facade if/when mailbox automation exists (same isolation pattern)

### 8.2 Token and Consent Strategy

- Office add-ins authenticate user and call Spaarke APIs
- Spaarke APIs perform OBO where needed for Graph-to-SPE operations (per SDAP approach)
- Mailbox automation (if enabled) may use app-only Graph consent in customer-tenant scenarios; keep this capability isolated behind config/feature flags

### 8.3 Auditability

- Log: user, action, object IDs, recipient emails (where permitted), timestamps, correlation IDs, environment/tenant

---

## 9. Backend Component Architecture

### 9.1 Core Services/Modules

| Module | Responsibility |
|--------|----------------|
| OfficeIntegration | API endpoints + orchestration for add-ins |
| SDAP | SpeFileStore, Graph client factory, SPE upload/download |
| AI Pipeline | Profile summary, indexing, analysis jobs |
| UAC/AuthZ | Central authorization service |

### 9.2 Worker Model

Background workers handle long-running tasks:
- Upload finalization
- Profile summary
- Indexing
- Deep analysis

### 9.3 Data Model (High-Level Entities)

Claude Code must produce a detailed Dataverse schema spec. Core entities for this project:
- Matter
- Project
- Invoice
- Document
- DocumentVersion
- DocumentWorkspaceLink (Document ↔ Matter/Project associations)
- EmailArtifact (email metadata/body snapshot)
- AttachmentArtifact (source attachment metadata)
- ProcessingJob (job id, stage statuses, timestamps, retries)
- AnalysisRun (type, status, outputs)

Note: ExternalUser, Invitation, AccessGrant entities are defined in SDAP-external-portal project but referenced here for "Grant access" feature.

---

## 10. API Surface

### 10.1 Office Add-in APIs

| Endpoint | Purpose |
|----------|---------|
| POST /office/save | Submit email/doc for filing; returns documentId, jobId |
| GET /office/jobs/{jobId} | Get job stage statuses + links |
| POST /office/share/links | Generate share links; optionally create invitations |
| POST /office/share/attach | Get attachment package for compose insertion |
| POST /office/quickcreate/{entityType} | Create Matter/Project/Invoice with minimal fields |
| GET /office/search/workspaces | Search Matters/Projects for destination picker |
| GET /office/search/documents | Search documents for share picker |
| GET /office/recent | Recent items for quick access |

Request/response DTOs, idempotency rules, and error models to be detailed in spec.

---

## 11. Packaging and Deployment

### 11.1 Manifest Strategy

- Use **add-in-only XML manifest** for Word + Outlook for production stability
- Unified manifest is production for Outlook but still preview for Word; plan future conversion when Word support is GA

### 11.2 IT Deployment

- Centralized deployment via Microsoft 365 admin processes
- Group-based assignment
- Update cadence expectations
- Tenant policy constraints that may hide/disable add-ins or restrict sideloading

---

## 12. Non-Functional Requirements

### 12.1 Performance

- Save action returns quickly (target: seconds) with job tracking; heavy work async
- Task pane should remain responsive; progressive disclosure for large files

### 12.2 Reliability

- Idempotent save/share endpoints (dedupe by message id + attachment ids + target workspace)
- Robust retry policy for downstream Graph/SPE operations via worker queue

### 12.3 Security

- Least-privilege permissions for add-in actions
- No direct exposure of SPE item IDs or Graph tokens to clients beyond time-limited viewer links
- External access requires authentication; no "anyone with link" by default

### 12.4 Observability

- Correlation IDs across add-in → API → worker steps
- Structured logs + metrics per stage

---

## 13. Required Deliverables

Claude Code must produce for this project:

1. **Detailed UX Spec**
    - Outlook read/compose flows, Word flows
    - Screen-by-screen task pane states and validation rules
    - Copywriting for user warnings (attach vs link, access not granted, etc.)

2. **Detailed Architecture Spec**
    - Component diagrams (textual)
    - Module boundaries and responsibilities
    - Worker/job orchestration design

3. **Dataverse Schema Spec**
    - Tables, columns, relationships, indexes
    - Security role strategy
    - Row-level access patterns aligned with UAC

4. **API Contract Spec**
    - Endpoints, DTOs, idempotency keys, error models
    - AuthN/AuthZ requirements per endpoint
    - Link generation and token/expiry model

5. **ALM/Deployment Spec**
    - Manifest strategy, environments, app registrations
    - Tenant/customer-hosted variance
    - IT admin deployment checklist

6. **Test Plan**
    - Client matrix (Outlook web/new Outlook/classic, Word desktop/web)
    - Permissions scenarios
    - Failure/retry paths

---

## 14. Reference Pointers

Office add-in commands and menu controls:
- https://learn.microsoft.com/en-us/office/dev/add-ins/develop/create-addin-commands
- https://learn.microsoft.com/en-us/office/dev/add-ins/design/add-in-commands

Manifest strategy (XML vs unified) and support status:
- https://learn.microsoft.com/en-us/office/dev/add-ins/develop/add-in-manifests
- https://learn.microsoft.com/en-us/office/dev/add-ins/develop/json-manifest-overview

---

## 15. Dependencies and Integration Points

### Provides to Other Projects

| Artifact | Consumer |
|----------|----------|
| /office/* APIs | Teams app (similar patterns) |
| Document, ProcessingJob entities | Teams app, External Portal |
| Job status components | Teams app |
| SpeFileStore facade | All projects |

### Requires from Other Projects

| Artifact | Provider |
|----------|----------|
| ExternalUser, Invitation, AccessGrant entities | SDAP-external-portal |
| /external/invitations API | SDAP-external-portal |

Note: "Grant access" feature in compose mode creates invitations via External Portal APIs. This can be stubbed initially and integrated when External Portal is ready.

---

**EOF**
