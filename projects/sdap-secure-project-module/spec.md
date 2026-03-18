# Secure Project & External Access Platform — AI Implementation Specification

> **Status**: Ready for Implementation
> **Created**: 2026-03-16
> **Source**: projects/sdap-secure-project-module/design.md
> **Project**: sdap-secure-project-module

---

## Executive Summary

Spaarke External Access enables **Core Users** (licensed Dataverse users) to create controlled collaboration workspaces for **external participants** (outside counsel, expert witnesses, clients, vendors) who access the system through a **Power Pages Code Page SPA** authenticated via **Microsoft Entra External ID**.

The design introduces a **Unified Access Control (UAC) model** that orchestrates three access planes — Dataverse records, SharePoint Embedded files, and Azure AI Search — through a single participation grant (`sprk_externalrecordaccess`). When a Core User adds a Contact to a project, the BFF API automatically provisions access across all three planes.

**Secure Projects** are the first implementation of this broader external access capability, targeting the experience set by **Harvey AI's Vault and Shared Spaces**. The UAC model is designed to support future expansion to matters, e-billing, and ad-hoc document sharing.

---

## Scope

### In Scope

- **Unified Access Control model** — three-plane orchestration (Dataverse, SPE, AI Search) via single participation grant
- **Secure Project creation** — extension to existing Create Project wizard with "Is Secure" flag, BU/container provisioning
- **External user invitation** — invitation flow using Power Pages built-in `adx_invitation`, Entra External ID onboarding
- **Power Pages Code Page SPA** — React 18 external workspace (home page + project page)
- **Document collaboration** — upload, download, versioning via SPE, AI summaries from existing playbooks
- **AI features via toolbar** — playbook-driven summaries, analysis, semantic search (reusing existing playbook infrastructure)
- **Task and event management** — external users create/manage tasks and events on accessible projects
- **Access level enforcement** — View Only / Collaborate / Full Access with distinct capabilities
- **Notification integration** — email notifications via existing `sprk_communication` module
- **Access revocation and project closure** — cascading revocation across all three planes
- **Custom table: `sprk_externalrecordaccess`** — participation junction table
- **Modified table: `sprk_project`** — add `sprk_issecure`, `sprk_securitybuid`, `sprk_externalaccountid` fields
- **BFF API endpoints** — external caller authorization, SPE container membership management, invitation orchestration

### Out of Scope

- **SprkChat for external users** — deferred to post-MVP; AI features delivered via toolbar/buttons only
- **Mobile-specific UI** — desktop-first responsive SPA; dedicated mobile app is future
- **Matters, e-billing, ad-hoc sharing** — UAC model supports these but implementation is future phases
- **Power Automate flows** — not used in Spaarke (AD5)
- **Dataverse plugins for orchestration** — not used in Spaarke (AD5, ADR-002)
- **Self-service registration** — invitation-only access
- **Real-time collaboration** (co-editing) — external users work asynchronously
- **Teams notifications** — future integration, not in MVP
- **Converting standard projects to secure** — immutable flag at creation

### Affected Areas

- `src/server/api/Sprk.Bff.Api/` — New external access endpoints, authorization filters, SPE container membership management
- `src/client/code-pages/` — New Power Pages Code Page SPA (external workspace)
- `src/client/shared/` — Shared UI components reused in external SPA
- `src/solutions/` — Dataverse solution: new `sprk_externalrecordaccess` table, `sprk_project` field additions
- Power Pages site configuration — table permissions, web roles, site settings, CSP/CORS
- Azure AI Search — index field additions (`project_ids`, `business_unit_id`)
- Existing Create Project wizard — extension for Secure Project provisioning

---

## Requirements

### Functional Requirements

#### FR-01: Secure Project Creation
A Core User creates a Secure Project using the existing Create Project wizard (Code Page dialog):
1. Standard project creation steps (name, description, matter association)
2. "Is this a Secure Project?" toggle — **immutable flag set at creation only**
3. If secure, wizard calls BFF API to:
   - Create a child Business Unit (`SP-{ProjectRef}`)
   - Provision a dedicated SPE container (via BU creation trigger)
   - Create an External Access Account owned by the BU
   - Store container ID and BU reference on the project record
4. Alternate: if Core User selects an existing BU/Account (umbrella model), project inherits existing container

**Acceptance**: Core User can create a Secure Project; BU, container, and Account are provisioned; project record shows `sprk_issecure = true` with BU and Account references.

#### FR-02: External User Invitation
Core Users invite external users during or after project creation:
1. Enter external email address (or select existing Contact)
2. Set access level: View Only / Collaborate / Full Access
3. System creates `sprk_externalrecordaccess` record (no two-step approval needed)
4. System creates `adx_invitation` (Power Pages built-in invitation table):
   - Single-recipient invitation code
   - Auto-assigns "Secure Project Participant" web role on redemption
   - Configurable expiry date
   - Maximum redemptions: 1
5. System sends invitation email via existing `sprk_communication` module (Graph-based)
6. External user clicks link → completes Entra External ID signup/verification → invitation redeemed → web role assigned → access active

**Acceptance**: Core User can invite a Contact; invitation email is sent; external user can redeem invitation and access the Secure Project.

#### FR-03: Access Level Enforcement
Three access levels with distinct capabilities:

| Capability | View Only | Collaborate | Full Access |
|-----------|-----------|-------------|-------------|
| View project metadata | Yes | Yes | Yes |
| View documents | Yes | Yes | Yes |
| Download documents | No | Yes | Yes |
| Upload documents | No | Yes | Yes |
| Create/edit tasks & events | No | Yes | Yes |
| Run AI analysis (toolbar) | No | Yes | Yes |
| Semantic search | Yes (results only) | Yes | Yes |
| View AI summaries (pre-computed) | Yes | Yes | Yes |
| Invite other external users | No | No | Yes |

**Acceptance**: Each access level enforces the correct set of capabilities; View Only cannot download or trigger AI; Full Access can invite others.

#### FR-04: Three-Plane Access Orchestration (Grant)
When a Core User adds a Contact with External Record Access, the BFF API orchestrates:
1. Create `sprk_externalrecordaccess` record (Contact → Project, AccessLevel, GrantedBy, GrantedDate)
2. Ensure Contact has "Secure Project Participant" web role (check `mspp_webrole` N:N, add if missing)
3. Add Contact's Entra External ID to SPE container (Graph API: Reader for View Only, Writer for Collaborate/Full Access)
4. If Contact is new → create `adx_invitation` (auto-assigns web role on redemption)

**Acceptance**: After granting access, external user can see the project in the SPA, access documents in SPE, and semantic search returns project documents.

#### FR-05: Three-Plane Access Orchestration (Revoke)
When a Core User revokes access:
1. Set `sprk_externalrecordaccess.statecode = Inactive`
2. BFF removes Contact from SPE container membership
3. If Contact has no other active participation records → remove "Secure Project Participant" web role
4. Deactivate `adx_invitation` if still pending

**Acceptance**: After revoking access, external user can no longer see the project, access documents, or get search results for that project.

#### FR-06: External Workspace — Home Page (Power Pages Code Page SPA)
External users see a workspace home with:
- My Secure Projects (all projects the contact has access to)
- Recent document activity across projects (pre-computed summaries from Document Profile playbook)
- Upcoming events and tasks
- Notification feed (project updates, new documents, task assignments)

**Acceptance**: External user sees only their accessible projects; home page loads pre-computed data (no real-time AI calls).

#### FR-07: External Workspace — Project Page
Each project page displays:
- **Project metadata**: reference number, name, description, organizations, contacts, participants
- **Document library**: upload, view, download (per access level), versioning (SPE-native), AI summaries
- **Events calendar**: view and create events (Collaborate/Full Access)
- **Smart To-Do list**: view and create tasks (Collaborate/Full Access)
- **Activity timeline**: notifications of project activity

**Acceptance**: Project page shows all sections; CRUD operations respect access level; document library shows AI summaries.

#### FR-08: Document Collaboration
External users with appropriate access:
- Upload files (reuse existing file upload Code Page dialog pattern — drag & drop, progress)
- View and download documents (download restricted by access level)
- Access file versions (SPE-native versioning)
- View AI-generated summaries and analysis results (pre-computed by Document Profile playbook)

**Acceptance**: File upload works via existing pattern; versioning displays correctly; AI summaries appear on document records.

#### FR-09: AI Features (Playbook-Driven, Toolbar)
AI features delivered via toolbar buttons (Collaborate/Full Access only):
- **On document upload (automatic, background)**: Document Profile playbook triggers → generates summary, extracts key concepts; results stored on document record
- **On-demand (toolbar buttons)**: Summarize document, summarize project content, run analysis playbook
- **Semantic search**: Scoped to project documents via AI Search `project_ids` filter; natural language queries return documents with excerpts
- **IP protection**: External users see playbook outputs only; playbook definitions, prompt configurations, and JPS schemas are never exposed

**Acceptance**: Toolbar buttons invoke playbooks; results display as structured output; search returns only accessible project documents; no playbook internals are visible.

#### FR-10: Email Notifications
Notifications sent via existing `sprk_communication` module (Graph-based email):
- New document uploads on accessible projects
- Task assignments
- Project updates (status changes, new participants)
- Invitation emails

**Acceptance**: Email notifications fire for specified events; emails are sent via existing communication infrastructure (no new email service needed).

#### FR-11: Project Lifecycle — Closure
When a Secure Project is set to Inactive:
- All associated `sprk_externalrecordaccess` records are deactivated
- BFF removes all external contacts from the SPE container
- SPE container preserved (archived) for retention/compliance
- AI Search documents remain indexed but excluded from queries (project marked inactive)

**Acceptance**: Setting project inactive cascades access revocation; container is preserved; search excludes closed project documents.

#### FR-12: Security & Audit
- Invitation-based access only (no self-service registration)
- Identity verification via Entra External ID
- Per-project access control (participation record is the grant)
- Isolation of project content (dedicated SPE container, AI Search filtering)
- Prevention of link sharing (single-use invitation codes, identity-bound access)
- Full audit logging (who granted access, when, to whom, what level)
- Access expiry support (optional expiry date on participation records)
- Project closure cascading revocation

**Acceptance**: All security requirements enforced; audit trail is complete and queryable.

### Non-Functional Requirements

- **NFR-01**: External workspace SPA must load within 3 seconds on broadband connection
- **NFR-02**: BFF API authorization checks for external callers must complete within 100ms (use CachedAccessDataSource with Redis)
- **NFR-03**: SPE container membership operations (grant/revoke) must complete within 5 seconds
- **NFR-04**: AI Search queries with `search.in` filter must return within 1 second
- **NFR-05**: Power Pages SPA must support 50+ concurrent external users per project
- **NFR-06**: WCAG 2.1 AA accessibility compliance for external workspace
- **NFR-07**: Dark mode and high-contrast support (Fluent UI v9 design tokens)

---

## Technical Constraints

### Applicable ADRs

| ADR | Relevance |
|-----|-----------|
| **ADR-001** | BFF API endpoints: Minimal API, ProblemDetails, BackgroundService for async work |
| **ADR-002** | No plugins for orchestration; thin validation only if any |
| **ADR-006** | Code Page SPA for external workspace; no legacy JS webresources |
| **ADR-007** | All SPE operations through SpeFileStore facade; no Graph SDK leaks |
| **ADR-008** | Endpoint filters for external caller authorization; no global auth middleware |
| **ADR-009** | Redis-first caching for access data (CachedAccessDataSource pattern) |
| **ADR-010** | Concrete DI registrations; feature module extensions; ≤15 non-framework lines |
| **ADR-012** | Shared `@spaarke/ui-components` for external SPA; Fluent v9 only |
| **ADR-013** | AI endpoints follow Minimal API; use SpeFileStore for file access; rate limiting |
| **ADR-021** | Fluent UI v9 exclusively; semantic tokens; dark mode required; no hard-coded colors |
| **ADR-022** | Code Page SPA: React 18 bundled (not platform-provided); `createRoot` entry |

### MUST Rules

- MUST use Minimal API pattern for all new BFF endpoints
- MUST use endpoint filters for external caller authorization (not global middleware)
- MUST route all SPE operations through `SpeFileStore` facade
- MUST use Redis-first caching for external access data
- MUST use Fluent UI v9 exclusively in external SPA
- MUST support dark mode and high-contrast
- MUST use `@spaarke/ui-components` for shared layouts (WizardDialog, SidePanel, DataGrid)
- MUST use React 18 `createRoot` in Code Page SPA (bundled, not platform-provided)
- MUST NOT create Dataverse plugins for orchestration
- MUST NOT use Power Automate flows
- MUST NOT expose Graph SDK types above SpeFileStore facade
- MUST NOT use legacy JS webresources
- MUST NOT use polymorphic lookups (use field resolver)
- MUST NOT hard-code colors or use Fluent v8 components
- MUST NOT create separate AI microservice

### Existing Patterns to Follow

- See `src/server/api/Sprk.Bff.Api/` for BFF endpoint patterns
- See `src/server/api/Sprk.Bff.Api/Authorization/` for `AuthorizationService`, `CachedAccessDataSource`, endpoint filters
- See `src/client/code-pages/` for React 18 Code Page dialog patterns
- See `src/client/shared/` for `@spaarke/ui-components` shared library
- See `src/solutions/LegalWorkspace/` for Corporate Workspace SPA pattern (home page layout)
- See existing Create Project wizard for wizard extension pattern
- See `sprk_communication` module for email notification integration
- See `docs/architecture/uac-access-control.md` for UAC three-plane model
- See `docs/architecture/power-pages-spa-guide.md` for Power Pages SPA development
- See `docs/architecture/power-pages-access-control.md` for Power Pages security configuration

---

## UAC Data Model

### New Custom Table

**`sprk_externalrecordaccess`** — Participation junction table (single source of truth for external access):

| Field | Type | Description |
|-------|------|-------------|
| `sprk_externalrecordaccessid` | PK | Primary key |
| `sprk_name` | String | Auto-generated display name |
| `sprk_contactid` | Lookup → Contact | External contact being granted access |
| `sprk_projectid` | Lookup → sprk_project | Project being accessed (nullable for future expansion) |
| `sprk_matterid` | Lookup → sprk_matter | Future: matter-level access (nullable) |
| `sprk_accesslevel` | Choice | View Only (100000000) / Collaborate (100000001) / Full Access (100000002) |
| `sprk_grantedby` | Lookup → SystemUser | Core User who granted access |
| `sprk_granteddate` | DateTime | When access was granted |
| `sprk_expirydate` | DateTime | Optional expiration |
| `sprk_accountid` | Lookup → Account | External Access Account |
| `statecode` | State | Active / Inactive |

### Modified Tables

**`sprk_project`** — Add fields:
| Field | Type | Description |
|-------|------|-------------|
| `sprk_issecure` | Boolean | Immutable flag set at creation |
| `sprk_securitybuid` | Lookup → BusinessUnit | Child BU for this secure project |
| `sprk_externalaccountid` | Lookup → Account | External Access Account |

### Power Pages Table Permission Chain

```
Level 0: sprk_externalrecordaccess
         Scope: Contact (contact sees their own participation records)

  └── Level 1: sprk_project
               Scope: Parent (via sprk_projectid on participation record)

        ├── Level 2: sprk_document     — Scope: Parent (via ProjectId)
        ├── Level 2: sprk_event        — Scope: Parent (via ProjectId)
        ├── Level 2: contact           — Scope: Parent (via ProjectId relationship)
        └── Level 2: sprk_organization — Scope: Parent (via ProjectId relationship)
```

### Power Pages Web Roles

| Role | Purpose |
|------|---------|
| `Secure Project Participant` | Assigned on invitation redemption; associated with table permission chain above |

---

## Success Criteria

1. [ ] Core User can create a Secure Project with BU, SPE container, and External Access Account provisioned — Verify: create project wizard E2E test
2. [ ] Core User can invite external Contact; invitation email sent via `sprk_communication` — Verify: invitation flow E2E test
3. [ ] External user can redeem invitation, authenticate via Entra External ID, and access SPA — Verify: onboarding E2E test
4. [ ] External workspace home page shows only accessible projects with pre-computed summaries — Verify: SPA loads, data scoped correctly
5. [ ] Project page shows documents, events, tasks, contacts scoped to access level — Verify: View Only vs Collaborate vs Full Access tested
6. [ ] Document upload/download respects access level (View Only: no download, no upload) — Verify: access level enforcement test
7. [ ] AI toolbar buttons invoke playbooks; results display as structured output; no playbook internals visible — Verify: playbook execution + IP protection test
8. [ ] Semantic search returns only accessible project documents — Verify: search with `project_ids` filter
9. [ ] Access revocation cascades across all three planes (Dataverse, SPE, AI Search) — Verify: revoke flow E2E test
10. [ ] Project closure deactivates all participation records and removes SPE access — Verify: closure flow E2E test
11. [ ] Full audit trail: who granted, when, to whom, what level — Verify: query audit data
12. [ ] SPA meets Fluent UI v9 standards: dark mode, high-contrast, WCAG 2.1 AA — Verify: accessibility audit

---

## Dependencies

### Prerequisites

- Power Pages site provisioned with Code Page SPA support (site version 9.8.1.x+)
- Entra External ID tenant configured for the Power Pages site
- PAC CLI 1.44.x+ installed for SPA deployment
- `.js` unblocked in Dataverse Privacy + Security settings
- Existing BFF API infrastructure (Azure App Service, Redis, Azure AI Search)
- Existing `sprk_communication` module wired to Graph for email
- Existing playbook infrastructure (Document Profile, analysis playbooks)

### External Dependencies

- Microsoft Entra External ID — identity provider for external users
- Power Pages — Code Page SPA hosting (GA February 2026)
- SharePoint Embedded — container provisioning and file storage
- Azure AI Search — semantic search with `project_ids` filtering
- PAC CLI — SPA deployment to Power Pages

---

## Owner Clarifications

| Topic | Question | Answer | Impact |
|-------|----------|--------|--------|
| Access levels | What can each level do? Can View Only run AI? | View Only: no AI triggers, no download. Collaborate: full CRUD + AI. Full Access: can invite others (mainly for Core Users accessing SPA). | Determines table permission CRUD config and BFF authorization checks per level |
| Approval flow | Is there a two-step grant/approve flow? | No — single step, access is immediate on grant | No deferred SPE provisioning; `approvedby`/`approveddate` fields removed from table design |
| Email mechanism | What sends notification emails? | Existing `sprk_communication` module (Graph-based via BFF API) | No new email infrastructure; hook into existing service |
| Download permissions | Which levels can download? | View Only: no download. Collaborate and Full Access: yes | SPE role mapping: Reader for View Only, Writer for Collaborate/Full |
| Multi-file upload | Build new or reuse? | Reuse existing file upload Code Page dialog pattern | Extend existing pattern; no new upload UX |
| Home page AI | Real-time AI on home page? | No — show pre-computed summaries from Document Profile playbook | Home page reads stored data; no live AI calls |

---

## Assumptions

- **SPE container provisioning** is triggered by BU creation (existing Spaarke pattern) — no new provisioning mechanism needed
- **Corporate Workspace SPA** (`src/solutions/LegalWorkspace/`) serves as the layout reference for the external workspace
- **Existing playbooks** (Document Profile, analysis) work with external caller context — BFF handles authorization before invoking
- **AI Search index** can be extended with `project_ids` field without re-indexing all existing documents (additive schema change)
- **Power Pages table permissions** support 2-level parent chains reliably (well within 4-5 level practical limit)
- **`sprk_communication`** module supports sending to external Contact email addresses (not just internal users)
- **`approvedby`/`approveddate` fields removed** from `sprk_externalrecordaccess` per owner confirmation that no two-step approval is needed

---

## Unresolved Questions

- [ ] **SPE external sharing override**: Has `Set-SPOApplication -OverrideTenantSharingCapability` been enabled in the dev tenant? — Blocks: SPE container membership for external users
- [ ] **Entra External ID tenant**: Is the B2C/External ID tenant already provisioned, or does this project need to set it up? — Blocks: Power Pages identity provider configuration
- [ ] **Power Pages site**: Does a Power Pages site already exist, or will one be created as part of this project? — Blocks: SPA deployment target
- [ ] **AI Search index schema**: Can `project_ids` be added as a filterable field to the existing index without full re-index? — Blocks: search integration timeline

---

*AI-optimized specification. Original design: projects/sdap-secure-project-module/design.md*
