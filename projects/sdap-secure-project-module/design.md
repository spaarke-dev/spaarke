# Spaarke External Access & Secure Project Module — Design Document

> **Project**: sdap-secure-project-module
> **Status**: Design
> **Last Updated**: March 16, 2026

---

## Purpose of this Document

This document provides design guidance for generating a full product and functional specification for **Spaarke External Access** — a platform capability that enables controlled collaboration with external participants who are not licensed Power Apps users.

The **Secure Project** is the first implementation of this capability, modeled after Harvey AI's Vault and Shared Spaces experience. However, the underlying Unified Access Control model is designed to support external access across all Spaarke features (e.g., matters, e-billing, document collaboration) as the platform evolves.

The resulting specification should describe product behavior, requirements, use cases, and user experience. It should NOT include code or low-level implementation details. Claude Code should infer technical architecture from the existing repository and platform patterns.

---

## Executive Summary

Spaarke External Access enables a **Core User** (licensed Dataverse / Power Apps user) to create controlled collaboration workspaces for **external participants** (outside counsel, expert witnesses, clients, vendors) who access the system through a **Power Pages Code Page SPA** authenticated via **Microsoft Entra External ID**.

The design introduces a **Unified Access Control (UAC) model** that orchestrates three access planes — Dataverse records, SharePoint Embedded files, and Azure AI Search — through a single participation grant. When a Core User adds a Contact to a record with "External Record Access," the system automatically provisions access across all three planes.

Secure Projects are the flagship use case, targeting the experience set by **Harvey AI's Vault and Shared Spaces** — giving external users access to project documents, AI-powered summaries and analysis, semantic search, task management, and collaboration, all within a secure, isolated environment.

---

## Market Context & Competitive Benchmark

### Harvey AI Vault — The Target Experience

Harvey AI's Vault is the primary benchmark. It gets high marks for:

- **Project-based containers** supporting up to 10,000 documents per Vault project
- **Review Tables** — structured extraction across thousands of documents (27-32 fields per document type, 97-99% recall) with one-click pre-built workflows for Merger Agreements, Leases, Court Opinions, etc.
- **Shared Spaces** (December 2025, GA March 2026) — firms create a shared environment spanning organizational boundaries; clients get Guest Accounts (no Harvey subscription needed); both sides control what's shared; clients can **run Workflows and Playbooks but cannot see underlying prompts** (IP protection)
- **Agentic Search** — iterative analysis combining 200+ legal knowledge sources, web search, and internal Vault data
- **94.8% accuracy** on document Q&A tasks (VLAIR benchmark)
- **Unified experience** — chat, drafting, and revision in a single thread with inline citations
- **Mobile access** — iOS/Android with Vault access, voice prompting, document camera scanning

**Harvey Shared Spaces key design principles:**
- Mutual admin approval before cross-org collaboration begins
- Shared content includes: Vaults, Review Tables, Threads, Workflows, Playbooks, Artifacts
- IP protection: clients run workflows but can't see prompts
- Data never leaves the host workspace's infrastructure
- Full audit trail on every security-critical action

### Competitive Landscape

| Platform | External AI Access? | Key Strength | Key Gap |
|----------|-------------------|--------------|---------|
| **Harvey AI** | Yes — Workflows, Playbooks, Q&A via Shared Spaces | AI-native, client collaboration with AI | 10K doc cap, $1K+/user/month, Shared Spaces in early access |
| **HighQ** (Thomson Reuters) | No — CoCounsel AI is internal only | Mature VDR, Q&A module, 5:1 external user ratio, deep TR ecosystem | Older UX, AI not exposed to externals |
| **iManage Share** | No — RAVN AI is internal only | Deep DMS integration, Closing Folders for deal management | No AI for externals, fragmented product lines |
| **NetDocuments CollabSpace** | No — ndMAX is internal only | DMS-integrated, Teams/SharePoint bridge via netDocShare | No AI for externals, functional but undifferentiated portal |
| **Relativity** | Workspace-permissioned (possible) | Unmatched e-discovery scale, aiR Privilege is unique | Not a portal/deal room tool, admin-heavy onboarding |

### Spaarke Differentiator

**Every competitor except Harvey treats external portals as "share a folder."** Harvey is the only platform giving externals AI capabilities. Spaarke's plan to include playbook-driven AI summaries and analysis for external users from the start puts it in Harvey's category while incumbents are still folder-sharing.

**Additional Spaarke advantages over Harvey:**
- No artificial document cap (SPE has no per-container cost)
- Playbook-driven analysis is extensible by admins (not Harvey's closed model)
- Power Pages + Dataverse integration provides a full collaboration platform (tasks, events, contacts) — not just documents
- External access model is designed for expansion beyond projects (matters, e-billing, etc.)

---

## Product Concept Overview

### Scope: External Access Platform

Secure Projects are the **first implementation** of a broader external access capability. The Unified Access Control model is designed to support:

| Phase | External Access For | Use Cases |
|-------|-------------------|-----------|
| **Phase 1 (this project)** | Secure Projects | Outside counsel collaboration, deal rooms, litigation workspaces, due diligence |
| **Future** | Matters | External matter participants, client visibility into matter status |
| **Future** | E-Billing | Vendor invoice submission, approval workflows |
| **Future** | Document Collaboration | Ad-hoc document sharing with external parties |

### Secure Project Collaboration Scenarios

- Outside counsel collaboration
- Expert witness collaboration
- Litigation or investigation workspaces
- Transaction or deal workspaces (M&A, financing)
- Client collaboration environments
- IP project collaboration
- Regulatory matters
- Due diligence workspaces

---

## User Types

### Core User

A **Core User** is an internal Spaarke user with a Dataverse license and access to the Power Apps model-driven environment.

Capabilities:
- Create Secure Projects (via Create Project wizard)
- Invite external users
- Manage project participants and access levels
- Manage documents and files
- Manage project metadata
- Review and run AI summaries and analysis
- Configure access permissions
- View full audit trail of external access

Core Users manage Secure Projects from the **same project form** as standard projects, with an "Is Secure" flag and related external access metadata.

### External User

An **External User** is a person invited to collaborate on a Secure Project who does **not have a Power Apps license**. External users authenticate via **Microsoft Entra External ID** and access the system through a **Power Pages Code Page SPA**.

Capabilities:
- View Secure Projects to which they are invited
- Upload and view documents (full document/file access when permission granted)
- File versioning consistent with SPE behavior
- Create and manage tasks and events
- View project contacts and organizations
- Run AI summaries and analysis on project documents (playbook-driven, from toolbar/buttons)
- Semantic search across project documents
- Download documents (if permitted by access level)

External users **cannot**:
- Create new projects or matters
- Access internal system data
- Access projects they were not invited to
- See playbook definitions or AI prompt configurations (IP protection)
- Access SprkChat (deferred to post-MVP; AI features delivered via toolbar/buttons)

---

## Architecture Decisions

### AD1: Power Pages + Code Page SPA + BFF API

The external user experience is delivered as a **Code Page SPA** hosted in Power Pages:
- Power Pages provides **contact-based authentication** with Entra External ID
- Code Page SPA delivers the React 18 workspace experience (consistent with internal Code Pages)
- **BFF API** (`Sprk.Bff.Api`) serves as the backend for both internal and external users
- No separate API layer — the BFF handles authorization context to distinguish internal vs. external callers

### AD2: Business Unit as Container Anchor

Business Units are used not for user security (which is handled by Dataverse security roles for internal users and Power Pages table permissions for external users) but as an **organizational abstraction** that maps to SPE containers:

- Existing pattern: BU → SPE Container (already established in Spaarke)
- Secure Project creation triggers child BU creation → which provisions a dedicated SPE container
- Records owned by the BU inherit the BU's container ID for file storage
- The custom `sprk_organization` table can organize BUs across organizations

**Container granularity is a user decision:**

| Granularity | BU Represents | Container Scope | When to Use |
|-------------|---------------|-----------------|-------------|
| BU per project | Single collaboration scope | Full file isolation per project | Maximum document segregation |
| Umbrella BU (multiple projects) | A collaboration relationship | Projects share a container | Simpler management, cross-project file access |
| BU per matter | A legal matter | All projects under a matter share a container | When matter is the natural boundary |

Since SPE has no per-container cost, the granularity choice is driven by security requirements, not cost.

### AD3: External Access Account

An **External Access Account** serves as the access boundary for external contacts:
- Account is owned by the project's BU → inherits the BU's container ID
- External contacts are assigned to the Account
- Power Pages table permissions use the **Account access type** → contacts see records tied to their Account
- Multiple projects can be associated to the same External Access Account (umbrella model)
- Account/Contact records are owned at the parent BU level, so Core Users in the model-driven app can see them to associate with projects

Multiple External Access Accounts can exist under a single BU when different external organizations need different access levels within the same project scope (e.g., outside counsel sees privileged docs, expert witness does not).

### AD4: Shared AI Search Index with Project Filtering

- Single shared Azure AI Search index (not per-project indexes)
- Documents indexed with `project_id` and `business_unit_id` as `Collection(Edm.String)` filterable fields
- BFF constructs `search.in` filter at query time based on the contact's accessible projects
- No per-index cost; avoids hitting tier limits (S1: 50, S2: 200 indexes max)
- Scalable: `search.in` is optimized for multi-value matching, subsecond even with hundreds of values
- Future path: Azure AI Search native Entra-based document-level security (2025 preview) could replace manual filters

### AD5: No Plugins, No Power Automate

Consistent with Spaarke's core product architecture:
- No Dataverse plugins for orchestration
- No Power Automate flows
- All orchestration goes through the **BFF API**, triggered by web resources (Code Page dialogs, wizard steps)
- The Create Project wizard (existing Code Page dialog) serves as the BFF API harness for provisioning

### AD6: No Polymorphic Fields

Consistent with Spaarke conventions:
- No polymorphic lookups in the data model
- Use Spaarke's field resolver feature for cross-entity references
- Avoids Power Pages table permission limitations (polymorphic lookups not supported in parent-child chains)

### AD7: AI Features via Toolbar, Not Chat (MVP)

For the initial release:
- AI summaries and analysis are delivered via **toolbar buttons** that invoke playbooks
- Playbooks are the core AI orchestrator — running explicitly (user clicks "Summarize") or triggered in background (document upload → Document Profile playbook)
- External users see playbook **outputs** (summaries, analysis results, extracted data) but never see playbook definitions or prompt configurations (IP protection, consistent with Harvey's model)
- **SprkChat** integration is deferred to post-MVP

---

## Unified Access Control (UAC) Model

### The Three Access Planes

When a Contact is granted external access to a record, three independent access planes must be orchestrated:

```
PLANE 1: Dataverse Records
  Mechanism: Power Pages table permissions (parent-child chain)
  Cascade: AUTOMATIC — parent scope chains handle related records
  Built-in tables: mspp_webrole, mspp_entitypermission

PLANE 2: SharePoint Embedded Files
  Mechanism: SPE container membership via Microsoft Graph API
  Cascade: NOT AUTOMATIC — BFF must add contact to container
  Role options: Reader, Writer, Manager, Owner

PLANE 3: Azure AI Search
  Mechanism: Query-time filter on project/BU IDs
  Cascade: AUTOMATIC at query time — BFF reads contact's
           participation records to build filter
```

### Power Pages Built-In Tables (Leverage, Don't Replicate)

Power Pages provides built-in tables for access management:

| Table | Logical Name | Purpose | Spaarke Usage |
|-------|-------------|---------|---------------|
| **Web Role** | `mspp_webrole` | Defines role with associated table permissions | Create "Secure Project Participant" role |
| **Table Permission** | `mspp_entitypermission` | Schema-level CRUD rules with scope (Global/Contact/Account/Parent/Self) | Configure parent-chain for project → documents → events → tasks |
| **Invitation** | `adx_invitation` | Invitation with code, expiry, max redemptions, auto web role assignment on redemption | Use for external user onboarding |
| **External Identity** | `adx_externalidentity` | Maps Contact to Entra External ID login | Used automatically by Power Pages authentication |
| **Invite Redemption** | `adx_inviteredemption` | Activity record tracking each redemption event | Audit trail for invitations |

**What does NOT exist (custom table needed):**
- A **participation/membership table** (`sprk_externalrecordaccess`) linking a Contact to a specific record (project, matter) with access level, granted-by, granted-date, and approval fields

### Table Permission Chain (Parent Scope Cascading)

Power Pages table permissions support **parent-child chains** that automatically cascade access to related records:

```
Level 0: sprk_externalrecordaccess
         Scope: Contact (contact sees their own participation records)

  └── Level 1: sprk_project
               Scope: Parent (via ProjectId lookup on participation record)
               Contact sees Projects they're linked to

        ├── Level 2: sprk_document
        │            Scope: Parent (via ProjectId on document)
        │            Contact sees Documents belonging to accessible Projects
        │
        ├── Level 2: sprk_event
        │            Scope: Parent (via ProjectId on event)
        │            Contact sees Events/Tasks belonging to accessible Projects
        │
        ├── Level 2: sprk_contact (project contacts)
        │            Scope: Parent (via ProjectId relationship)
        │            Contact sees other Contacts on accessible Projects
        │
        └── Level 2: sprk_organization
                     Scope: Parent (via ProjectId relationship)
                     Contact sees Organizations on accessible Projects
```

**Key points:**
- 2-3 levels of depth is well within the practical limit (~4-5 levels)
- No polymorphic lookups needed (each parent relationship is a direct lookup)
- No per-record permission records needed — the schema-level rules cascade automatically
- Lookup/reference tables (e.g., option set values, jurisdictions) can use Global read-only scope

### Custom Table: sprk_externalrecordaccess

The one custom table needed to bridge participation and access:

| Field | Type | Description |
|-------|------|-------------|
| `sprk_externalrecordaccessid` | PK | Primary key |
| `sprk_name` | String | Auto-generated display name |
| `sprk_contactid` | Lookup → Contact | The external contact being granted access |
| `sprk_projectid` | Lookup → sprk_project | The project being accessed (nullable for future: matter, etc.) |
| `sprk_matterid` | Lookup → sprk_matter | Future: matter-level access (nullable) |
| `sprk_accesslevel` | Choice | View Only / Collaborate (view + upload) / Full Access |
| `sprk_grantedby` | Lookup → SystemUser | Core User who granted access |
| `sprk_granteddate` | DateTime | When access was granted |
| `sprk_approvedby` | Lookup → SystemUser | Core User who approved document/file access |
| `sprk_approveddate` | DateTime | When document/file access was approved |
| `sprk_expirydate` | DateTime | Optional expiration |
| `sprk_accountid` | Lookup → Account | External Access Account |
| `statecode` | State | Active / Inactive |

This table serves as the **single source of truth** for "who has external access to what." It is the anchor for the Power Pages table permission chain (Level 0) and the trigger for BFF orchestration.

### Orchestration Flow: Granting External Access

When a Core User adds a Contact with External Record Access:

```
Core User action: Add Contact to project with "External Record Access"
  (via Contact subgrid, wizard step, or dedicated dialog)
         │
         ▼
Step 1: Create sprk_externalrecordaccess record
        (Contact → Project, AccessLevel, GrantedBy, GrantedDate)
         │
         ▼
Step 2: BFF API call (triggered by wizard dialog / web resource)
        │
        ├── 2a: Ensure Contact has web role "Secure Project Participant"
        │       (check mspp_webrole N:N, add if missing)
        │
        ├── 2b: Add Contact's Entra External ID to SPE container
        │       (Graph API: container membership as Reader or Writer
        │        based on AccessLevel)
        │
        └── 2c: If Contact is new to system → create adx_invitation
                (built-in invitation table, auto-assigns web role
                 on redemption, single-use code, expiry date)
         │
         ▼
RESULT: All three access planes are now active
  Plane 1: Table permission chain auto-cascades (no further action)
  Plane 2: SPE container membership granted
  Plane 3: Next AI Search query will include this project in filter
```

### Orchestration Flow: Revoking External Access

```
Core User action: Remove external access (deactivate participation record)
         │
         ▼
Step 1: Set sprk_externalrecordaccess.statecode = Inactive
         │
         ▼
Step 2: BFF API call
        │
        ├── 2a: Remove Contact from SPE container membership
        │
        ├── 2b: Check if Contact has other active participation records
        │       If none → remove "Secure Project Participant" web role
        │
        └── 2c: Deactivate adx_invitation if still pending
         │
         ▼
RESULT: Access revoked across all three planes
  Plane 1: Inactive participation record breaks the parent chain
  Plane 2: SPE container membership removed
  Plane 3: Next AI Search query excludes this project
```

### Project Lifecycle: Closing a Secure Project

When a Secure Project is set to Inactive:
- All associated `sprk_externalrecordaccess` records are deactivated
- External users lose access through the parent chain
- BFF removes all external contacts from the SPE container
- SPE container preserved (archived) for retention/compliance
- AI Search documents remain indexed but excluded from queries (project marked inactive)

---

## Key Product Requirements

### Requirement 1: Secure Project Creation

A Core User creates a Secure Project using the **Create Project wizard** (existing Code Page dialog):

1. Standard project creation steps (name, description, matter association, etc.)
2. "Is this a Secure Project?" toggle — **this is an immutable flag set at creation**
3. If yes, wizard calls BFF API to:
   - Create a child Business Unit (`SP-{ProjectRef}`)
   - Provision a dedicated SPE container (via BU creation trigger)
   - Create an External Access Account owned by the BU
   - Store container ID and BU reference on the project record

Secure Projects cannot be converted from standard projects after creation.

**Alternate: Umbrella BU** — if the Core User selects an existing BU/Account (e.g., "Kirkland & Ellis Collaboration"), the project inherits the existing container instead of provisioning a new one.

### Requirement 2: External User Invitation

Core Users invite external users during or after project creation:

1. Enter external email address (or select existing Contact)
2. Set access level: View Only / Collaborate / Full Access
3. System creates `sprk_externalrecordaccess` record
4. System creates `adx_invitation` (built-in Power Pages invitation table):
   - Single-recipient invitation code
   - Auto-assigns "Secure Project Participant" web role on redemption
   - Configurable expiry date
   - Maximum redemptions: 1
5. System sends secure invitation email with single-use access link
6. External user clicks link → completes Entra External ID signup/verification → `adx_invitation` redeemed → web role assigned → access active

**Identity validation:** The `adx_externalidentity` table (built-in) maps the Entra External ID login to the Contact record. The system validates that the authenticated identity matches the invited email.

### Requirement 3: Secure Project Workspace (Power Pages Code Page SPA)

External users access the system through a **Power Pages site** hosting a **Code Page SPA** (React 18).

#### Workspace Home Page

The external workspace home page provides:

- My Secure Projects (all projects the contact has access to)
- Recent document activity across projects
- Upcoming events and tasks
- AI-generated summaries and insights
- Notifications (new documents, task assignments, project updates)

Layout resembles the Corporate Workspace but is scoped to external access and emphasizes project collaboration.

#### Secure Project Page

Each project page displays:

**Project metadata:**
- Reference number, project name, description
- Organizations and contacts (scoped to what the external user can see)
- Participants list

**Collaboration components:**
- Document library with upload, versioning (consistent with SPE), and AI summaries
- Events calendar
- Smart To-Do list (external users can create and update tasks)
- Activity timeline (notifications of project activity)

All information respects the UAC model — external users see only records that cascade through their participation grant.

### Requirement 4: Document Collaboration

External users with appropriate access level can:
- Upload files (single and multi-file)
- View and download documents
- Access file versions (SPE-native versioning)
- View AI-generated summaries and analysis results for each document

Files are stored in the project's SPE container. Documents are represented as Dataverse records with a lookup to the project. Document access follows the UAC model (parent chain from participation → project → documents).

### Requirement 5: AI Features (Playbook-Driven)

AI features are delivered via **toolbar buttons** that invoke playbooks:

**On document upload (automatic, background):**
- Document Profile playbook triggers → generates summary, extracts key concepts
- Results stored on the document record and visible to external users

**On-demand (user-initiated via toolbar/buttons):**
- Summarize document or selection
- Summarize entire project content
- Run analysis playbook on document(s) — e.g., NDA review, contract extraction
- Results displayed as structured output (not raw chat)

**IP protection:**
- External users see playbook **outputs** only
- Playbook definitions, prompt configurations, and JPS schemas are never exposed
- Consistent with Harvey's model: clients run workflows but can't see prompts

**Semantic search:**
- Scoped to the project's documents (via AI Search project_id filter)
- Natural language queries return relevant documents with excerpts
- Search results respect the UAC model

### Requirement 6: Notifications

External users receive notifications through:

| Channel | Content |
|---------|---------|
| **Workspace** | Events widget, notification feed on home page |
| **Email** | New document uploads, task assignments, project updates |
| **Teams** | Optional Teams notifications (future integration) |

### Requirement 7: Security Requirements

The system must enforce:

- Invitation-based access only (no self-service registration without invitation)
- Identity verification via Entra External ID
- Per-project access control (participation record is the grant)
- Isolation of project content (dedicated SPE container, AI Search filtering)
- Prevention of link sharing (single-use invitation codes, identity-bound access)
- Full audit logging (who granted access, when, to whom, what level)
- Access expiry support (optional expiry date on participation records)
- Revocation flow (deactivate participation → cascading access removal)
- Project closure flow (set to inactive → all external access revoked)

---

## Data Isolation Model

### SPE Container Segregation

Each Secure Project (or umbrella scope) provisions a **dedicated SPE container**:

```
Business Unit: "SP-ProjectRef-001"
  └── SPE Container: {containerId}
        ├── /documents/          (project documents)
        ├── /analysis-output/    (AI-generated artifacts)
        └── /uploads/            (pending processing)
```

- Container isolation ensures files from one project are never commingled with another
- SPE billing is pay-as-you-go (storage, API transactions, egress) — **no per-container cost**
- Container membership is managed by BFF via Graph API
- External user sharing is enabled via tenant-level SPE override (`Set-SPOApplication -OverrideTenantSharingCapability`)

### AI Search Segregation

Shared index with security filtering:

- Documents indexed with `project_ids: Collection(Edm.String)` field (filterable, not retrievable)
- Optionally include `business_unit_id` for broader BU-level filtering
- BFF constructs filter at query time: `project_ids/any(p:search.in(p, 'proj-123,proj-456'))`
- External user's accessible projects determined from active `sprk_externalrecordaccess` records
- No per-index cost implications; avoids tier limits

**Future path:** Azure AI Search native Entra-based document-level security (2025 preview) could replace manual filter construction when GA.

---

## UAC Data Model Summary

### Existing Tables Leveraged (Do Not Replicate)

| Table | Logical Name | Usage |
|-------|-------------|-------|
| Web Role | `mspp_webrole` | "Secure Project Participant" role definition |
| Table Permission | `mspp_entitypermission` | Parent-chain rules for cascading access |
| Invitation | `adx_invitation` | Secure invitation with code, expiry, auto role assignment |
| Invite Redemption | `adx_inviteredemption` | Audit of invitation redemptions |
| External Identity | `adx_externalidentity` | Entra External ID → Contact mapping |
| Contact | `contact` | External user identity record |
| Account | `account` | External Access Account (access boundary) |
| Business Unit | `businessunit` | Container provisioning anchor |

### New Custom Table

| Table | Logical Name | Purpose |
|-------|-------------|---------|
| External Record Access | `sprk_externalrecordaccess` | Participation grant linking Contact to Project (or Matter, etc.) with access level, audit fields, and approval tracking |

### Modified Existing Tables

| Table | Changes |
|-------|---------|
| `sprk_project` | Add: `sprk_issecure` (boolean, immutable), `sprk_securitybuid` (lookup → BU), `sprk_externalaccountid` (lookup → Account) |
| `sprk_document` | No changes — parent chain via ProjectId handles access |
| `sprk_event` | No changes — parent chain via ProjectId handles access |

---

## Specification Deliverable

Claude Code should produce a complete specification containing:

- System overview and vision (external access platform, not just Secure Projects)
- Personas (Core User, External User by role: outside counsel, expert witness, client)
- User journeys (project creation, invitation, onboarding, collaboration, access revocation)
- Functional requirements (all requirements above, detailed)
- Workspace UX behavior (home page, project page, document library, tasks)
- Document collaboration flows (upload, versioning, download, AI processing)
- AI feature behavior (playbook-driven summaries, analysis, semantic search, IP protection)
- Unified Access Control model (three planes, orchestration flows, parent-chain configuration)
- Security model (invitation, identity, isolation, audit, revocation, closure)
- Data isolation model (SPE containers, AI Search filtering)
- Governance considerations (audit trails, compliance, retention)
- Notification model (workspace, email, Teams)
- Future extensibility (matters, e-billing, ad-hoc document sharing)

The specification should reflect the expectations of **enterprise legal collaboration platforms** used by corporate legal departments and law firms. The tone should be **product-management oriented** rather than engineering-centric.

---

## Appendix: Market Research Sources

### Harvey AI
- [Harvey AI — Vault Platform](https://www.harvey.ai/platform/vault)
- [Introducing the Next Version of Vault](https://www.harvey.ai/blog/introducing-the-next-version-of-vault)
- [Harvey Shared Spaces and Collaboration](https://www.harvey.ai/blog/shared-spaces-and-collaboration-in-harvey)
- [Harvey Secure Collaboration Solutions](https://www.harvey.ai/solutions/collaboration)
- [Harvey's Top 5 Product Releases of 2025](https://www.harvey.ai/blog/top-5-product-releases-of-2025)
- [Harvey Launches Shared Spaces — Artificial Lawyer](https://www.artificiallawyer.com/2025/12/04/harvey-launches-shared-spaces-for-collaboration/)
- [Scaling Harvey's Document Systems: Vault File Upload and Management](https://www.harvey.ai/blog/scaling-harveys-document-systems-vault-file-upload-and-management)

### Thomson Reuters HighQ
- [HighQ Client Portal Features](https://www.thomsonreuters.com.hk/en/products-services/legal-overview/highq/client-portal.html)
- [HighQ Virtual Data Room — datarooms.org](https://datarooms.org/highq-vdr/)
- [CoCounsel + HighQ Integration — LegalTech.ca](https://legaltech.ca/2025/10/15/thomson-reuters-expands-cocounsel-legal-with-deep-research-and-highq-integration/)

### iManage
- [iManage Share Product Page](https://imanage.com/imanage-products/document-email-management/share/)
- [iManage Closing Folders](https://imanage.com/resources/resource-center/blog/how-imanage-closing-folders-helps-streamline-complex-deal-closing-processes/)

### Relativity
- [RelativityOne aiR for Review](https://www.relativity.com/data-solutions/air/review/)
- [Relativity aiR for Case Strategy Launch](https://www.prnewswire.com/news-releases/relativity-launches-air-for-case-strategy-bringing-generative-ai-to-case-intelligence-302658042.html)

### NetDocuments
- [NetDocuments CollabSpace — Affinity Consulting](https://www.affinityconsulting.com/collabspaces-in-netdocuments/)
- [NetDocuments ndMAX Smart Answers](https://www.netdocuments.com/company-news/smart-answers/)
- [netDocShare — Teams/SharePoint Bridge](https://netdocshare.com/)

### Platform Documentation
- [Power Pages Security Overview](https://learn.microsoft.com/en-us/power-pages/security/power-pages-security)
- [Power Pages Table Permissions](https://learn.microsoft.com/en-us/power-pages/security/table-permissions)
- [Modernized Business Units Security](https://learn.microsoft.com/en-us/power-platform/admin/modernized-business-units-security)
- [SharePoint Embedded Auth and Permissions](https://learn.microsoft.com/en-us/sharepoint/dev/embedded/development/auth)
- [SharePoint Embedded Billing Meters](https://learn.microsoft.com/en-us/sharepoint/dev/embedded/administration/billing/meters)
- [Azure AI Search: Multitenancy and Content Isolation](https://learn.microsoft.com/en-us/azure/search/search-modeling-multitenant-saas-applications)
- [Azure AI Search: Security Filter Pattern](https://learn.microsoft.com/en-us/azure/search/search-security-trimming-for-azure-search)
- [Azure AI Search: Document-Level Access Control](https://learn.microsoft.com/en-us/azure/search/search-document-level-access-overview)
- [Azure AI Search: Service Limits by Tier](https://learn.microsoft.com/en-us/azure/search/search-limits-quotas-capacity)
