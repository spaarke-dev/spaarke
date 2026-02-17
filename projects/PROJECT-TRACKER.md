# Spaarke Project Tracker

> **Last Updated**: 2026-02-17
>
> **Purpose**: Management overview of all development projects — scope, status, and deliverables.

---

## Project Portfolio Summary

| # | Project | Status | Started | Completed | Progress |
|---|---------|--------|---------|-----------|----------|
| 1 | [MDA Dark Mode Theme Toggle](#1-mda-dark-mode-theme-toggle) | Complete | Dec 2025 | Dec 2025 | 100% |
| 2 | [PCF React 16 Remediation](#2-pcf-react-16-remediation) | Planned | Dec 2025 | — | 0% |
| 3 | [Visualization Framework R2](#3-visualization-framework-r2) | In Progress | Jan 2026 | — | ~75% |
| 4 | [Email-to-Document Automation R2](#4-email-to-document-automation-r2) | Complete | Jan 2026 | Jan 2026 | 95% (wrap-up pending) |
| 5 | [AI RAG Document Ingestion Pipeline](#5-ai-rag-document-ingestion-pipeline) | In Progress | Jan 2026 | — | 93% |
| 6 | [AI Chat Playbook Builder R2](#6-ai-chat-playbook-builder-r2) | Complete | Jan 2026 | Jan 2026 | 100% |
| 7 | [AI Playbook Assistant Completion R3](#7-ai-playbook-assistant-completion-r3) | Complete | Jan 2026 | Jan 2026 | 100% |
| 8 | [SDAP Office Integration](#8-sdap-office-integration) | Complete | Jan 2026 | Jan 2026 | 100% |
| 9 | [AI Semantic Search Foundation R1](#9-ai-semantic-search-foundation-r1) | Complete | Jan 2026 | Jan 2026 | 100% |
| 10 | [AI Semantic Search UI R2](#10-ai-semantic-search-ui-r2) | In Progress | Jan 2026 | — | 90% |
| 11 | [AI Scope Resolution Enhancements](#11-ai-scope-resolution-enhancements) | In Progress | Jan 2026 | — | 90% |
| 12 | [Events and Workflow Automation R1](#12-events-and-workflow-automation-r1) | Complete | Feb 2026 | Feb 2026 | 100% |
| 13 | [Events Workspace Apps UX R1](#13-events-workspace-apps-ux-r1) | Near Complete | Feb 2026 | — | 99% (1 task remaining) |
| 14 | [Matter Performance Assessment & KPI R1](#14-matter-performance-assessment--kpi-r1) | Complete | Feb 2026 | Feb 2026 | 100% |
| 15 | [Financial Intelligence Module R1](#15-financial-intelligence-module-r1) | Planning | — | — | Design complete |
| 16 | [AI Document Intelligence R5](#16-ai-document-intelligence-r5) | Planning | — | — | Design complete |
| 17 | [AI Document Analysis Enhancements](#17-ai-document-analysis-enhancements) | Backlog | — | — | Requirements gathering |
| 18 | [AI Playbook Scope Editor PCF](#18-ai-playbook-scope-editor-pcf) | Planning | — | — | Design complete |
| 19 | [AI Document Relationship Visuals](#19-ai-document-relationship-visuals) | Planning | — | — | Design complete |
| 20 | [SDAP Teams App](#20-sdap-teams-app) | Planning | — | — | Spec complete |
| 21 | [SDAP External Portal](#21-sdap-external-portal) | Planning | — | — | Spec complete |
| 22 | [Unified Access Control](#22-unified-access-control) | Planning | — | — | Design complete |
| 23 | [SDAP File Upload Error Fix](#23-sdap-file-upload-error-fix) | Planning | — | — | Investigation started |
| 24 | [Production Performance Improvement R1](#24-production-performance-improvement-r1) | Planning | — | — | Design complete |

---

## Completed Projects

---

### 1. MDA Dark Mode Theme Toggle

| Field | Detail |
|-------|--------|
| **Status** | Complete |
| **Dates** | Dec 2025 |
| **Tasks** | 10 |

**Overview**: Added a Theme flyout menu to the Spaarke model-driven app command bar, allowing users to switch between Light, Dark, and Auto (system default) themes. The selected preference persists across sessions and applies immediately to all controls without a page refresh.

**Deliverables**:
- Theme flyout menu on the app command bar (Auto / Light / Dark)
- Shared theme utilities in the Spaarke UI component library
- All existing PCF controls updated to respect the selected theme
- Cross-tab theme synchronization

**Components Impacted**: Model-driven app command bar, SpeFileViewer, UniversalDatasetGrid, UniversalQuickCreate, Spaarke UI Components shared library

---

### 2. PCF React 16 Remediation

| Field | Detail |
|-------|--------|
| **Status** | Planned (High Priority) |
| **Dates** | Identified Dec 2025 — not yet started |
| **Tasks** | Not yet created |

**Overview**: A platform performance initiative to standardize all PCF controls on the Microsoft-provided React 16 library instead of bundling React 18 individually. Currently, a typical Dataverse form with 5 PCF controls could load up to 50 MB of duplicate JavaScript. This change reduces bundle sizes by approximately 20x and significantly improves form load times.

**Deliverables**:
- All PCF controls migrated to platform-provided React 16
- Bundle sizes reduced from ~10 MB to under 1 MB per control
- Shared component library updated for React 16 compatibility

**Components Impacted**: All PCF controls (UniversalDatasetGrid, UniversalQuickCreate, AnalysisBuilder, AnalysisWorkspace, DrillThroughWorkspace, SpeDocumentViewer, SpeFileViewer)

---

### 3. Visualization Framework R2

| Field | Detail |
|-------|--------|
| **Status** | In Progress |
| **Dates** | Jan 2026 — ongoing |
| **Tasks** | ~18 of 24 complete |

**Overview**: Enhances the VisualHost PCF control to support configuration-driven click actions and new visual card types. This enables interactive dashboard components that respond to user clicks (open forms, navigate to pages, open side panes) and introduces event due date card visuals for displaying upcoming deadlines on matter and project records.

**Deliverables**:
- Click action configuration on chart definitions (open record, open side pane, navigate to page, open grid)
- Event Due Date Card visual types (single card and card list)
- View-driven data fetching with context filtering
- Custom FetchXML support with parameter substitution

**Components Impacted**: VisualHost PCF control, Spaarke UI Components shared library, Dataverse chart definition entity

---

### 4. Email-to-Document Automation R2

| Field | Detail |
|-------|--------|
| **Status** | Implementation Complete (wrap-up pending) |
| **Dates** | Jan 2026 |
| **Tasks** | 21 of 22 complete |

**Overview**: Enhanced the existing email-to-document automation pipeline with several key improvements: fixed user access to files uploaded by the system, added automatic attachment extraction as child documents, enabled background AI analysis of email content, and added toolbar buttons for processing existing and sent emails from within the Dataverse interface.

**Deliverables**:
- API-proxied download endpoint for app-uploaded files
- Attachment extraction service (filters out signature images, tracking pixels, calendar files)
- Background AI analysis service (runs without user context)
- Email analysis playbook (extract, combine, analyze)
- Ribbon toolbar buttons for processing existing emails and sent items

**Components Impacted**: BFF API endpoints, email processing pipeline, document upload flow, Dataverse ribbon commands

---

### 5. AI RAG Document Ingestion Pipeline

| Field | Detail |
|-------|--------|
| **Status** | In Progress |
| **Dates** | Jan 2026 — ongoing |
| **Tasks** | 25 of 27 complete |

**Overview**: Implements a unified document ingestion pipeline for Retrieval-Augmented Generation (RAG), enabling automatic indexing of documents to Azure AI Search. The pipeline supports three entry points — user upload, email automation, and document events — all converging to a single shared chunking, embedding, and indexing pipeline so documents are searchable regardless of how they enter the system.

**Deliverables**:
- Unified file indexing service as single source of truth for RAG indexing
- Three entry points (user upload, email automation, document events)
- Shared text chunking service
- New API endpoint for file indexing
- Background job handler for async processing
- Email pipeline integration for automatic indexing

**Components Impacted**: BFF API, Azure AI Search indexes, email automation pipeline, document upload PCF controls, background job infrastructure

---

### 6. AI Chat Playbook Builder R2

| Field | Detail |
|-------|--------|
| **Status** | Complete |
| **Dates** | Jan 2026 |
| **Tasks** | 48 of 48 complete |

**Overview**: Added conversational AI assistance to the Playbook Builder, enabling users to build AI playbooks through natural language while seeing results update in real-time on the visual canvas. Users can describe what they want in plain English and the system translates that into playbook configuration — nodes, connections, scope settings, and execution parameters.

**Deliverables**:
- Natural language playbook creation with real-time canvas updates
- Streaming AI responses via server-sent events
- Intelligent scope management (save as, extend, create new)
- Test execution modes (mock, quick, production)
- Tiered AI model selection

**Components Impacted**: PlaybookBuilderHost PCF control, BFF API AI endpoints, AI scope infrastructure

---

### 7. AI Playbook Assistant Completion R3

| Field | Detail |
|-------|--------|
| **Status** | Complete |
| **Dates** | Jan 2026 |
| **Tasks** | 25 of 25 complete |

**Overview**: Completed the AI Assistant implementation in the Playbook Builder by replacing rule-based intent parsing with AI-powered classification and adding full Dataverse integration for scope operations. This enables end-to-end playbook creation through conversation — from describing the playbook to deploying scope records.

**Deliverables**:
- AI-powered intent classification (replacing rule-based parsing)
- Full scope CRUD operations with Dataverse integration
- Scope search and suggestion with semantic matching
- 23 builder-specific scope seed records deployed
- Test execution wiring (mock, quick, production modes)
- Frontend enhancements (scope browser, save as dialog)

**Components Impacted**: PlaybookBuilderHost PCF control, BFF API, Dataverse scope entities, Azure OpenAI integration

---

### 8. SDAP Office Integration

| Field | Detail |
|-------|--------|
| **Status** | Complete |
| **Dates** | Jan 2026 |
| **Tasks** | 56 of 56 complete |

**Overview**: Built a unified Office integration platform enabling Outlook and Word add-ins to save content to the Spaarke document management system and share documents from Spaarke. The platform uses a shared user interface with host-specific adapters, supporting both New Outlook and Word on desktop and web. This is Project 1 of 3 in the Office + Teams Integration Initiative.

**Deliverables**:
- Outlook add-in: Save emails/attachments to Spaarke, share documents from Spaarke
- Word add-in: Save documents, share, version management
- Shared React task pane UI with host-specific adapters
- Nested App Authentication (NAA) with MSAL.js 3.x
- Backend API endpoints for Office operations
- Background workers (upload, profile, index, analysis)
- Real-time job status updates via server-sent events
- Duplicate email detection
- Dark mode and WCAG 2.1 AA accessibility

**Components Impacted**: New Outlook, Word (desktop + web), BFF API, Dataverse schema, background job infrastructure

---

### 9. AI Semantic Search Foundation R1

| Field | Detail |
|-------|--------|
| **Status** | Complete |
| **Dates** | Jan 2026 |
| **Tasks** | 22 (21 complete, 1 deferred) |

**Overview**: Established the foundational API infrastructure for AI-powered semantic search across the document management system. Delivers a reusable search service that combines vector similarity with keyword matching to find relevant documents, with the ability to scope results by entity (Matter, Project, Invoice, etc.) and filter by document type, date range, and tags.

**Deliverables**:
- Semantic search service with hybrid search (vector + keyword)
- Entity-scoped filtering (Matter, Project, Invoice, Account, Contact)
- Search filter builder (document types, file types, tags, date range)
- AI tool handler for copilot integration
- Search index schema extensions
- Authorization filter for search results
- 82 unit tests

**Components Impacted**: BFF API, Azure AI Search, AI tool framework

---

### 10. AI Semantic Search UI R2

| Field | Detail |
|-------|--------|
| **Status** | In Progress |
| **Dates** | Jan 2026 — ongoing |
| **Tasks** | 45 of 50 complete |

**Overview**: Building the user-facing search interface for AI-powered semantic document search. The search control can be deployed on command bars (via dialog), form sections (scoped to a specific matter or project), and dedicated search pages. Users can search using natural language, apply filters, and browse results with similarity scores and highlighted snippets.

**Deliverables**:
- SemanticSearchControl PCF control
- Natural language search input with filters
- Document result cards with similarity scores and highlighted snippets
- Dynamic filter panel from Dataverse metadata
- Multiple deployment modes (dialog, form section, full page)
- Full dark mode support
- Deployable Dataverse solution package

**Components Impacted**: New PCF control, Dataverse solution, Custom Pages, form ribbon commands

---

### 11. AI Scope Resolution Enhancements

| Field | Detail |
|-------|--------|
| **Status** | In Progress |
| **Dates** | Jan 2026 — ongoing |
| **Tasks** | 26 of 29 complete |

**Overview**: Addresses a core architectural issue where AI tool and skill configurations were hard-coded in the application rather than loaded dynamically from the database. This project enables fully configuration-driven AI extensibility — administrators can add new AI tools, skills, and actions through Dataverse records without requiring code deployment.

**Deliverables**:
- Three-tier scope resolution architecture (configuration, generic execution, custom handlers)
- Elimination of hard-coded tool/skill registrations
- Generic analysis handler for configuration-driven tool execution
- Handler discovery API
- Runtime handler registration fix

**Components Impacted**: BFF API AI services, Dataverse scope entities, AI tool framework, job processing infrastructure

---

### 12. Events and Workflow Automation R1

| Field | Detail |
|-------|--------|
| **Status** | Complete (form configuration pending) |
| **Dates** | Feb 2026 |
| **Tasks** | 46 of 46 complete |

**Overview**: Implemented a centralized event management system for tracking deadlines, reminders, and scheduled activities across all entity types (Matters, Projects, Invoices, etc.). The project also delivered two reusable platform capabilities: an Association Resolver Framework (solving Dataverse polymorphic lookup limitations) and a Field Mapping Framework (admin-configurable field inheritance between parent and child records).

**Deliverables**:
- Event, Event Type, and Event Log Dataverse tables
- 5 PCF controls: AssociationResolver, RegardingLink, EventFormController, UpdateRelatedButton, FieldMappingAdmin
- Event API endpoints (CRUD, complete, cancel)
- Field Mapping API endpoints (profiles, validation, push)
- Field Mapping Service with type validation and cascading support
- Event Log state transition tracking
- All controls deployed to Dataverse; BFF API deployed to Azure

**Components Impacted**: Dataverse schema (3 new entities + 2 mapping entities), BFF API, 5 new PCF controls, model-driven app forms and views

**Note**: Controls are deployed but manual Dataverse form configuration is still needed to add controls to entity forms.

---

### 13. Events Workspace Apps UX R1

| Field | Detail |
|-------|--------|
| **Status** | Near Complete (1 task remaining) |
| **Dates** | Feb 2026 |
| **Tasks** | 86 of 87 complete |

**Overview**: Delivered a suite of interconnected UX components that transform how users interact with Events in the platform. Provides context-preserving navigation, visual date-based filtering via a calendar, and Event Type-aware editing. Users can view upcoming due dates on overview tabs, filter events by date on dedicated tabs, and edit event details in a side pane without leaving the current record.

**Deliverables**:
- EventCalendarFilter PCF control (multi-month calendar with date/range selection)
- UniversalDatasetGrid enhancement (calendar sync, hyperlink-to-sidepane, column filters)
- EventDetailSidePane Custom Page (Event Type-aware editing, security role awareness)
- DueDatesWidget PCF control (card-based upcoming events on overview tabs)
- Events Custom Page (system-level events view replacing out-of-box view)
- EventTypeService shared service
- Full dark mode support and WCAG 2.1 AA accessibility

**Components Impacted**: 3 new PCF controls, 2 Custom Pages, 1 shared service, UniversalDatasetGrid (enhanced), model-driven app forms

**Post-Project Enhancements Designed** (not yet implemented):
- Event completion workflow (Complete, Cancel, Reschedule, Reassign ribbon commands)
- Memo entity for cross-entity notes
- Event Type-specific side pane forms

---

### 14. Matter Performance Assessment & KPI R1

| Field | Detail |
|-------|--------|
| **Status** | Complete |
| **Dates** | Feb 2026 |
| **Tasks** | 27 of 27 complete |

**Overview**: Delivered a manual KPI assessment system for matter records. Users can quickly assess matter performance across three areas — Guidelines Compliance, Budget Management, and Outcomes Quality — using a streamlined entry form. The system automatically calculates grades and displays color-coded report cards with trend visualization directly on the matter record.

**Deliverables**:
- KPI Assessment Dataverse entity with grade calculation
- 6 grade fields on Matter entity (current + average for 3 areas)
- Quick Create form for rapid assessment entry
- Grade calculator API endpoint
- 3 VisualHost metric cards on matter main tab (color-coded)
- Report Card tab with trend cards, sparkline graphs, and linear regression trend indicators
- KPI assessment history subgrid
- 44 unit tests

**Components Impacted**: Dataverse schema (new entity + matter fields), BFF API, VisualHost PCF control, Matter main form, JavaScript web resource

**Deployment**: Verified end-to-end on 2026-02-16 (Dataverse entities, BFF API, PCF controls, web resources, form configuration).

---

## In Progress Projects

| Project | Progress | Remaining Work |
|---------|----------|----------------|
| [Visualization Framework R2](#3-visualization-framework-r2) | ~75% | Testing and deployment of new visual types |
| [AI RAG Pipeline](#5-ai-rag-document-ingestion-pipeline) | 93% | 2 remaining tasks |
| [AI Semantic Search UI R2](#10-ai-semantic-search-ui-r2) | 90% | 5 remaining tasks (testing/deployment) |
| [AI Scope Resolution Enhancements](#11-ai-scope-resolution-enhancements) | 90% | 3 remaining tasks (user testing) |
| [Events Workspace Apps UX R1](#13-events-workspace-apps-ux-r1) | 99% | 1 task: final OOB visual parity testing |

---

## Planned Projects (Design/Spec Complete)

---

### 15. Financial Intelligence Module R1

| Field | Detail |
|-------|--------|
| **Status** | Design Complete — Ready for Project Pipeline |

**Overview**: Deliver a Finance Intelligence MVP that ingests emails and attachments into the document management system, uses AI to classify and identify invoice candidates, supports a human review queue for confirmation, extracts financial facts to create billing events, and produces spend snapshots with budget variance and anomaly signals. Confirmed invoices are indexed for semantic search.

**Planned Deliverables**:
- AI classification playbook (invoice candidate identification)
- Invoice review queue (human confirmation gate)
- Invoice extraction playbook (billing facts, BillingEvent creation)
- Spend snapshot and signal generation
- Matter/Project finance intelligence UI
- Integration with existing email and RAG pipelines

**Components Expected**: BFF API, new Dataverse entities (Invoice, BillingEvent, SpendSnapshot, SpendSignal), new AI playbooks, PCF controls for finance UI

---

### 16. AI Document Intelligence R5

| Field | Detail |
|-------|--------|
| **Status** | Planning — Requires R4 (Playbook Scope System) Complete |

**Overview**: Implements the automated RAG pipeline connecting document analysis output to searchable knowledge indexes. Enables two capabilities: Knowledge Base for Analysis (curated reference content provides context during AI analysis) and Document Discovery (semantic search to find similar documents across the repository).

**Planned Deliverables**:
- Document chunking service
- Indexing pipeline connecting analysis output to RAG indexes
- Two-index architecture (Knowledge Base for curated content + Discovery for all documents)
- Knowledge Base administration UI
- "Find Similar" document search feature

**Components Expected**: BFF API, Azure AI Search indexes, background job handlers, administration UI

---

### 17. AI Document Analysis Enhancements

| Field | Detail |
|-------|--------|
| **Status** | Backlog — Requirements Gathering |

**Overview**: A collection of 12 enhancement requests for the AI Document Analysis features, including chat context switching, predefined prompts, prompt library management, deviation detection and scoring, and seed data for AI tools, skills, and knowledge sources. Items are being prioritized for future implementation.

---

### 18. AI Playbook Scope Editor PCF

| Field | Detail |
|-------|--------|
| **Status** | Design Complete — Ready for Project Pipeline |

**Overview**: Create a unified PCF control that provides rich editing and validation for all AI playbook scope configurations (Tools, Skills, Knowledge, Actions). The control adapts its editor interface based on entity type and provides real-time validation against backend capabilities, enabling administrators to configure AI features without code deployment.

**Planned Deliverables**:
- Unified scope configuration editor PCF control
- Adaptive editors for different scope types (JSON, Markdown)
- Handler discovery and validation via API
- Entity type auto-detection

**Components Expected**: New PCF control, BFF API handler discovery endpoint

---

### 19. AI Document Relationship Visuals

| Field | Detail |
|-------|--------|
| **Status** | Design Complete — Ready for Project Pipeline |

**Overview**: Enhance document relationship visualization with two new display modes: a List View with sortable columns and Excel/CSV export, and a compact Card View for dashboards that shows related document counts and opens a full viewer on click. These address user feedback for more flexible ways to view and export related document data beyond the existing graph visualization.

**Planned Deliverables**:
- List View mode for DocumentRelationshipViewer (sortable, exportable)
- DocumentRelationshipCard PCF control (compact dashboard card)
- CSV/Excel export capability

**Components Expected**: DocumentRelationshipViewer PCF (enhanced), new DocumentRelationshipCard PCF

---

### 20. SDAP Teams App

| Field | Detail |
|-------|--------|
| **Status** | Spec Complete — Ready for Project Pipeline |
| **Dependency** | Requires SDAP Office Integration (complete) |

**Overview**: Build a Microsoft Teams app that positions Spaarke as the document management front door within Teams. The app provides four surfaces: a Personal App for full document browsing, configurable Tabs for workspace-specific views, a Messaging Extension for inserting document cards in conversations, and a Message Action for saving attachments directly to Spaarke. Project 2 of 3 in the Office + Teams Integration Initiative.

**Planned Deliverables**:
- Personal App (pinned "Spaarke" app in Teams)
- Configurable Tab (Team/Channel-specific workspace views)
- Messaging Extension (search and insert document cards)
- Message Action ("Save to Spaarke" from messages)
- Teams-specific backend API endpoints

**Components Expected**: Teams app manifest, React UI, Bot Framework, BFF API endpoints, Dataverse tab configuration

---

### 21. SDAP External Portal

| Field | Detail |
|-------|--------|
| **Status** | Spec Complete — Ready for Project Pipeline |
| **Dependency** | Defines entitlement model consumed by Office and Teams integrations |

**Overview**: Build a Power Pages-based external collaboration portal enabling outside counsel and external partners to access shared Matters, Projects, and Documents without requiring full Dataverse licenses. Uses invitation-based access with Microsoft Entra External ID for authentication. Project 3 of 3 in the Office + Teams Integration Initiative.

**Planned Deliverables**:
- Entra External ID configuration
- Power Pages portal (dashboard, workspace pages, document viewer, invitation redemption)
- Entitlement model in Dataverse (ExternalUser, Invitation, AccessGrant, ExternalAccessLog)
- Backend API endpoints for external access
- Audit logging (all external actions)

**Components Expected**: Power Pages, Dataverse schema (4 new entities), BFF API endpoints, Entra ID configuration

---

### 22. Unified Access Control

| Field | Detail |
|-------|--------|
| **Status** | Design Complete — Required Before Production |
| **Priority** | Medium (Security) |

**Overview**: Addresses a security gap in the file access architecture where the BFF API validates that a document exists but does not verify that the calling user has permission to access that specific document record. The project implements defense-in-depth security by combining Azure AD security groups with BFF-level Dataverse permission validation.

**Planned Deliverables**:
- BFF-level Dataverse permission validation for file access
- Azure AD security group integration
- Defense-in-depth architecture for document access

**Components Expected**: BFF API authorization layer, Azure AD configuration

---

### 23. SDAP File Upload Error Fix

| Field | Detail |
|-------|--------|
| **Status** | Investigation Started |

**Overview**: A bug fix project to diagnose and resolve file upload errors in the UniversalDocumentUpload PCF control's Custom Page dialog flow. Initial diagnostic tracing has been captured but no formal specification or fix has been implemented yet.

**Components Expected**: UniversalDocumentUpload PCF control, Custom Page dialog

---

### 24. Production Performance Improvement R1

| Field | Detail |
|-------|--------|
| **Status** | Design Complete — Ready for Project Pipeline |
| **Priority** | High (Production Readiness) |

**Overview**: Addresses slow response times across BFF API interactions with SharePoint Embedded, Azure AI services, and Dataverse. Analysis identified architectural gaps in caching, connection management, query optimization, and infrastructure configuration that will persist into production without targeted intervention. Targets 60-80% reduction in typical API response times and establishes the infrastructure foundation for production deployment.

**Four Domains**:

| Domain | Focus | Key Deliverables |
|--------|-------|-----------------|
| **A. BFF API Caching** | Graph metadata cache, authorization data snapshots, connection pooling | Redis caching for Graph responses (ADR-009), auth data caching, GraphServiceClient pooling, debug endpoint removal |
| **B. Dataverse Optimization** | Query efficiency, batching, thread-safety | Replace `ColumnSet(true)` with explicit columns, add `$batch` for multi-query ops, fix thread-unsafe token refresh, add pagination |
| **C. Azure Infrastructure** | Network isolation, scaling, hardening | VNet + private endpoints for all services, App Service autoscaling, deployment slots, Redis persistence, Key Vault rotation |
| **D. CI/CD Pipeline** | Deployment quality and automation | Re-enable tests, Bicep infrastructure deployment, environment promotion (dev → staging → prod), slot-based zero-downtime deploys |

**Expected Impact**:

| Scenario | Current | Projected | Improvement |
|----------|---------|-----------|-------------|
| File listing (cached) | 250-1,000ms | 15-25ms | 90-97% |
| Document download | 250-1,000ms | 110-520ms | 45-55% |
| Dataverse entity read | 80-150ms | 40-80ms | ~50% |
| Form load (5 PCF controls) | 500-2,000ms | 200-500ms | 60-75% |

**Components Expected**: BFF API caching layer, Dataverse service clients, Azure VNet + private endpoints, App Service autoscale + deployment slots, CI/CD pipeline

---

## Archived Projects (Superseded)

The following projects have been superseded by newer iterations or consolidated into other work. They are prefixed with `x-` in the repository.

| Project | Superseded By | Notes |
|---------|---------------|-------|
| x-ai-document-intelligence-r1 | R2/R3/R4/R5 | Early AI document analysis iterations |
| x-ai-document-intelligence-r2 | R3/R4/R5 | Superseded by later releases |
| x-ai-document-intelligence-r3 | R4/R5 | Superseded by later releases |
| x-ai-document-intelligence-r4 | R5 | Superseded by R5 planning |
| x-ai-azure-search-module | ai-semantic-search-foundation-r1 | Consolidated into semantic search |
| x-ai-document-summary | ai-document-analysis-enhancements | Consolidated into analysis enhancements |
| x-ai-file-entity-metadata-extraction | ai-document-intelligence series | Absorbed into document intelligence |
| x-ai-node-playbook-builder | ai-playbook-node-builder-r2/r3 | Early playbook builder iteration |
| x-ai-summary-and-analysis-enhancements | ai-summary-and-analysis-enhancements | Replaced by non-x version |
| x-document-checkout-viewer | — | Partially complete; .eml viewer support pending |
| x-email-to-document-automation | email-to-document-automation-r2 | Superseded by R2 |
| x-sdap-fileviewer-enhancements-r1 | — | Completed and archived |

---

## Initiative Groupings

### Office + Teams Integration Initiative (3 projects)

| Order | Project | Status |
|-------|---------|--------|
| 1 | [SDAP Office Integration](#8-sdap-office-integration) | Complete |
| 2 | [SDAP Teams App](#20-sdap-teams-app) | Planned |
| 3 | [SDAP External Portal](#21-sdap-external-portal) | Planned |

### AI Platform Evolution

| Area | Project | Status |
|------|---------|--------|
| Search Foundation | [AI Semantic Search Foundation R1](#9-ai-semantic-search-foundation-r1) | Complete |
| Search UI | [AI Semantic Search UI R2](#10-ai-semantic-search-ui-r2) | In Progress (90%) |
| RAG Pipeline | [AI RAG Pipeline](#5-ai-rag-document-ingestion-pipeline) | In Progress (93%) |
| Scope Resolution | [AI Scope Resolution Enhancements](#11-ai-scope-resolution-enhancements) | In Progress (90%) |
| Playbook Builder | [AI Chat Playbook Builder R2](#6-ai-chat-playbook-builder-r2) | Complete |
| Playbook Completion | [AI Playbook Assistant R3](#7-ai-playbook-assistant-completion-r3) | Complete |
| Document Intelligence | [AI Document Intelligence R5](#16-ai-document-intelligence-r5) | Planning |
| Analysis Enhancements | [AI Document Analysis Enhancements](#17-ai-document-analysis-enhancements) | Backlog |
| Scope Editor | [AI Playbook Scope Editor PCF](#18-ai-playbook-scope-editor-pcf) | Planning |
| Relationship Visuals | [AI Document Relationship Visuals](#19-ai-document-relationship-visuals) | Planning |

### Events & Workflow

| Project | Status |
|---------|--------|
| [Events and Workflow Automation R1](#12-events-and-workflow-automation-r1) | Complete |
| [Events Workspace Apps UX R1](#13-events-workspace-apps-ux-r1) | Near Complete |

### Platform & Infrastructure

| Project | Status |
|---------|--------|
| [MDA Dark Mode Theme Toggle](#1-mda-dark-mode-theme-toggle) | Complete |
| [PCF React 16 Remediation](#2-pcf-react-16-remediation) | Planned |
| [Visualization Framework R2](#3-visualization-framework-r2) | In Progress |
| [Unified Access Control](#22-unified-access-control) | Planning (security) |
| [Production Performance Improvement R1](#24-production-performance-improvement-r1) | Planning (production readiness) |

---

*This document is maintained manually. Update status and dates as projects progress.*
