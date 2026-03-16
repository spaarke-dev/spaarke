# Secure Project & External Access Platform — Implementation Plan

> **Version**: 1.1
> **Created**: 2026-03-16
> **Completed**: 2026-03-16
> **Source**: spec.md
> **Status**: All phases complete

---

## Executive Summary

### Purpose
Build a Secure Project module that enables controlled external collaboration through a Power Pages Code Page SPA. External participants authenticate via Entra External ID and access project documents, AI-powered analysis, tasks, and events through a Unified Access Control model that orchestrates Dataverse records, SPE files, and AI Search in a single participation grant.

### Scope
Phase 1 delivers Secure Projects as the first external access capability. The UAC model is designed for future expansion (matters, e-billing, document sharing). All orchestration goes through the BFF API — no plugins, no Power Automate.

### Estimated Effort
30–45 task units across 7 phases. Complex project spanning Dataverse schema, BFF API, Power Pages SPA, SPE integration, AI Search, and playbook invocation.

---

## Architecture Context

### System Architecture

```
┌──────────────────────────────────────────────────────────────┐
│  INTERNAL (Core User)                                        │
│  Model-Driven App (Power Apps)                               │
│  ├── Create Project Wizard (Code Page)                       │
│  ├── Manage Participants (subgrid + dialog)                  │
│  └── Monitor Audit (sprk_externalrecordaccess views)         │
└────────────────────────┬─────────────────────────────────────┘
                         │
                    BFF API (Sprk.Bff.Api)
                    ├── External Access Endpoints
                    ├── Authorization Filters
                    ├── SPE Container Membership (Graph)
                    └── AI Playbook Invocation
                         │
┌────────────────────────┴─────────────────────────────────────┐
│  EXTERNAL (External User)                                     │
│  Power Pages Code Page SPA (React 18 + Fluent v9)            │
│  ├── Workspace Home (My Projects, Activity, Tasks)           │
│  ├── Project Page (Docs, Events, Tasks, Contacts)            │
│  ├── Document Library (Upload, Download, AI Summaries)       │
│  └── AI Toolbar (Playbook-driven analysis, search)           │
│                                                               │
│  Auth: Entra External ID → Portal Session + OAuth Token      │
│  APIs: Power Pages Web API (/_api/) + BFF API (Bearer)       │
└──────────────────────────────────────────────────────────────┘

UAC Three-Plane Orchestration:
  Plane 1: Dataverse → Power Pages table permissions (parent-chain)
  Plane 2: SPE → Container membership via Graph API
  Plane 3: AI Search → Query-time project_ids filter
```

### Discovered Resources

#### Applicable ADRs

| ADR | Title | Applies To |
|-----|-------|-----------|
| ADR-001 | Minimal API + BackgroundService | All new BFF endpoints |
| ADR-002 | Thin Dataverse plugins | No plugins for orchestration |
| ADR-003 | Authorization seams | IAccessDataSource + SpeFileStore |
| ADR-006 | PCF vs Code Pages | SPA is a Code Page (React 18, bundled) |
| ADR-007 | SpeFileStore facade | SPE container membership operations |
| ADR-008 | Endpoint filters | External caller authorization |
| ADR-009 | Redis-first caching | Access data caching (CachedAccessDataSource) |
| ADR-010 | DI minimalism | Feature module registrations |
| ADR-012 | Shared component library | Reuse @spaarke/ui-components in SPA |
| ADR-013 | AI Architecture | Playbook invocation for external users |
| ADR-021 | Fluent UI v9 | SPA UI framework, dark mode required |
| ADR-022 | PCF platform libraries | Code Page: React 18 bundled |
| ADR-026 | Full-page custom page | Vite + React 18 + viteSingleFile |

#### Applicable Skills

| Skill | Purpose |
|-------|---------|
| adr-aware | Load ADR constraints per task |
| code-review | Quality gate at step 9.5 |
| adr-check | Validate compliance |
| dataverse-deploy | Deploy solution with new table/fields |
| code-page-deploy | Deploy Power Pages SPA |
| azure-deploy | BFF API deployment after endpoint changes |

#### Knowledge Documents

| Document | Purpose |
|----------|---------|
| `docs/architecture/uac-access-control.md` | UAC three-plane model, authorization service |
| `docs/architecture/power-pages-spa-guide.md` | Power Pages SPA development guide |
| `docs/architecture/power-pages-access-control.md` | Table permissions, web roles, invitation flow |
| `docs/architecture/sdap-auth-patterns.md` | Authentication flows (OBO, S2S, MSAL) |
| `docs/architecture/communication-service-architecture.md` | Email notification integration |
| `.claude/constraints/auth.md` | Authorization MUST/MUST NOT rules |
| `.claude/constraints/api.md` | API endpoint rules |
| `.claude/patterns/auth/uac-access-control.md` | Permission checking patterns |
| `.claude/patterns/api/endpoint-definition.md` | Minimal API endpoint structure |
| `.claude/patterns/api/endpoint-filters.md` | Authorization filter patterns |
| `.claude/patterns/api/send-email-integration.md` | Email send patterns |
| `.claude/patterns/webresource/full-page-custom-page.md` | React 18 + Vite Code Page template |
| `.claude/patterns/auth/graph-sdk-v5.md` | Graph SDK v5 (SPE membership) |
| `.claude/patterns/dataverse/relationship-navigation.md` | N:N and parent-child patterns |

#### Scripts

| Script | Purpose |
|--------|---------|
| `scripts/Invite-DemoUsers.ps1` | Reference for external user onboarding |
| `scripts/Test-SdapBffApi.ps1` | API validation after deployment |
| `scripts/Deploy-Playbook.ps1` | Playbook deployment for external features |

#### Existing Code References

| Code | Purpose |
|------|---------|
| `src/server/api/Sprk.Bff.Api/Authorization/` | AuthorizationService, CachedAccessDataSource, endpoint filters |
| `src/server/api/Sprk.Bff.Api/Infrastructure/Graph/` | GraphClientFactory (OBO), token cache |
| `src/server/api/Sprk.Bff.Api/Api/Documents/` | Document/container CRUD endpoints |
| `src/client/code-pages/` | Existing Code Page examples (AnalysisWorkspace, SemanticSearch) |
| `src/client/shared/Spaarke.UI.Components/` | Shared Fluent v9 component library |
| `src/solutions/LegalWorkspace/` | Corporate Workspace SPA (layout reference) |
| `src/solutions/DocumentUploadWizard/` | Document upload Code Page reference |

---

## Phase Breakdown (WBS)

### Phase 1: Data Model & Dataverse Configuration
**Objective**: Create the `sprk_externalrecordaccess` table, add fields to `sprk_project`, configure option sets, and prepare the Dataverse solution.

**Deliverables**:
1. Create `sprk_externalrecordaccess` table with all fields (PK, contact lookup, project lookup, matter lookup, access level choice, granted-by, granted-date, expiry, account lookup, statecode)
2. Add fields to `sprk_project`: `sprk_issecure` (boolean), `sprk_securitybuid` (lookup → BU), `sprk_externalaccountid` (lookup → Account)
3. Create Access Level option set (View Only: 100000000, Collaborate: 100000001, Full Access: 100000002)
4. Create views for `sprk_externalrecordaccess` (Active, By Project, By Contact)
5. Add `sprk_externalrecordaccess` subgrid to project form
6. Deploy Dataverse solution to dev environment

**Dependencies**: None (foundation phase)

### Phase 2: BFF API — External Access Endpoints
**Objective**: Build the BFF API endpoints for external access orchestration: grant, revoke, invitation, container membership, and external caller authorization.

**Deliverables**:
1. External caller authorization filter (Contact-based, not SystemUser)
2. Grant access endpoint — creates participation record, adds web role, provisions SPE container membership
3. Revoke access endpoint — deactivates participation, removes SPE membership, conditionally removes web role
4. Invitation endpoint — creates `adx_invitation`, triggers email via `sprk_communication`
5. SPE container membership service — add/remove external user (Graph API via SpeFileStore)
6. External user context endpoint — returns current contact's accessible projects, access levels
7. Project closure endpoint — bulk deactivate participation, bulk remove SPE membership
8. Unit tests for all new endpoints and services
9. Integration tests for grant/revoke/search flows

**Dependencies**: Phase 1 (table must exist for participation records)

### Phase 3: Power Pages Configuration
**Objective**: Configure the Power Pages site for external access: identity provider, web roles, table permissions, site settings, CSP/CORS.

**Deliverables**:
1. Configure Entra External ID as identity provider
2. Create "Secure Project Participant" web role (`mspp_webrole`)
3. Configure table permission parent-chain (externalrecordaccess → project → documents/events/tasks/contacts/orgs)
4. Enable Web API site settings for required tables (sprk_project, sprk_document, sprk_event, etc.)
5. Configure CSP and CORS for BFF API domain
6. Configure OAuth implicit grant flow site settings (client ID, token expiry)
7. Configure invitation site settings

**Dependencies**: Phase 1 (tables must exist), Phase 2 (BFF endpoints for token validation)

### Phase 4: Power Pages Code Page SPA — Foundation
**Objective**: Build the React 18 SPA foundation: project scaffolding, auth layer, API clients, routing, and shared layout.

**Deliverables**:
1. SPA project scaffolding (Vite + React 18 + TypeScript + Fluent v9)
2. Portal auth module (session management, CSRF token, user context)
3. BFF API client module (OAuth token acquisition, Bearer auth)
4. Power Pages Web API client module (OData proxy calls)
5. App shell with FluentProvider, routing, error boundary
6. Shared layout components (AppHeader, Navigation, PageContainer)
7. Dark mode and high-contrast theme support

**Dependencies**: Phase 3 (Power Pages site configured)

### Phase 5: Power Pages Code Page SPA — Features
**Objective**: Build the workspace home page, project page, document library, tasks/events, and AI toolbar.

**Deliverables**:
1. Workspace Home page (My Projects grid, Recent Activity, Upcoming Events/Tasks, Notifications)
2. Project Page layout (metadata, participants, tabbed sections)
3. Document Library component (list, upload, download, versioning, AI summaries display)
4. Events calendar component (view, create for Collaborate/Full Access)
5. Smart To-Do list component (view, create for Collaborate/Full Access)
6. Project Contacts and Organizations view
7. AI Toolbar (Summarize, Analyze buttons — invoke playbooks via BFF)
8. Semantic Search component (scoped to project, natural language queries)
9. Invite External User dialog (Full Access level only)
10. Access level enforcement throughout all components

**Dependencies**: Phase 4 (SPA foundation), Phase 2 (BFF endpoints)

### Phase 6: Create Project Wizard Extension
**Objective**: Extend the existing Create Project wizard with Secure Project provisioning (BU creation, SPE container, External Access Account).

**Deliverables**:
1. "Is Secure Project?" toggle step in wizard
2. BU provisioning logic (create child BU `SP-{ProjectRef}`)
3. SPE container provisioning (via BU creation trigger)
4. External Access Account creation (owned by BU)
5. Umbrella BU selection option (use existing BU/Account)
6. Store references on project record (`sprk_issecure`, `sprk_securitybuid`, `sprk_externalaccountid`)

**Dependencies**: Phase 1 (fields exist), Phase 2 (BFF provisioning endpoints)

### Phase 7: Testing, Deployment & Wrap-Up
**Objective**: End-to-end testing, SPA deployment to Power Pages, documentation, and project wrap-up.

**Deliverables**:
1. E2E test: Secure Project creation flow
2. E2E test: External user invitation and onboarding
3. E2E test: Access level enforcement (View Only vs Collaborate vs Full Access)
4. E2E test: Access revocation across three UAC planes
5. E2E test: Project closure cascading
6. Deploy SPA to Power Pages via PAC CLI
7. Deploy BFF API updates to Azure
8. Deploy Dataverse solution to dev
9. Update architecture documentation if needed
10. Project wrap-up (lessons learned, README status update)

**Dependencies**: All prior phases

---

## Dependencies

### External Dependencies
- Microsoft Entra External ID tenant provisioned
- Power Pages site with Code Page SPA support (site version 9.8.1.x+)
- PAC CLI 1.44.x+ installed
- `.js` unblocked in Dataverse Privacy + Security settings
- SPE external sharing override enabled (`Set-SPOApplication -OverrideTenantSharingCapability`)

### Internal Dependencies
- Existing BFF API infrastructure (Azure App Service, Redis, AI Search)
- Existing `sprk_communication` module for email
- Existing playbook infrastructure (Document Profile, analysis playbooks)
- Existing Create Project wizard (Code Page)
- Existing `@spaarke/ui-components` shared library
- Corporate Workspace SPA (`src/solutions/LegalWorkspace/`) as layout reference

---

## Testing Strategy

### Unit Tests
- BFF API: authorization filters, grant/revoke logic, SPE membership service, invitation service
- SPA: component rendering, access level enforcement, auth module

### Integration Tests
- Grant access → verify all three planes provisioned
- Revoke access → verify all three planes cleaned up
- Search with `project_ids` filter → returns correct results
- Invitation redemption → web role assigned, access active

### E2E Tests
- Full Secure Project lifecycle: create → invite → collaborate → revoke → close
- Access level matrix: View Only / Collaborate / Full Access tested against all capabilities

---

## Risk Register

| Risk | Likelihood | Impact | Mitigation |
|------|-----------|--------|------------|
| Power Pages Code Page SPA is GA but new (Feb 2026) — potential undocumented limitations | Medium | High | Research thoroughly; have fallback to traditional Liquid pages for critical paths |
| SPE external sharing override may need tenant admin approval | Medium | Medium | Identify admin contact early; document the requirement |
| Entra External ID configuration complexity | Medium | Medium | Follow Power Pages docs closely; test in dev first |
| Table permission parent-chain depth limit | Low | High | Design stays within 2 levels (well under 4-5 practical limit) |
| AI Search `project_ids` filter performance at scale | Low | Medium | `search.in` is optimized; benchmark with 500+ projects |

---

## Acceptance Criteria

See [README.md Graduation Criteria](README.md#graduation-criteria) for the complete checklist.

---

## Phase Completion Record

| Phase | Completed | Key Milestone |
|-------|-----------|---------------|
| Phase 1: Data Model & Dataverse Configuration | 2026-03-16 | `sprk_externalrecordaccess` table + `sprk_project` fields deployed (tasks 001-004) |
| Phase 2: BFF API — External Access Endpoints | 2026-03-16 | All 7 BFF endpoints implemented + unit + integration tests (tasks 010-019) |
| Phase 3: Power Pages Configuration | 2026-03-16 | Entra External ID, web roles, table permissions, CSP/CORS configured (tasks 020-023) |
| Phase 4: SPA Foundation | 2026-03-16 | React 18 SPA scaffolded with auth, routing, Fluent v9, dark mode (tasks 030-036) |
| Phase 5: SPA Features | 2026-03-16 | All workspace pages, AI toolbar, semantic search, invite dialog implemented (tasks 040-050) |
| Phase 6: Wizard Extension | 2026-03-16 | Secure Project toggle + provisioning + CloseProjectDialog (tasks 060-062) |
| Phase 7: Testing & Wrap-Up | 2026-03-16 | E2E tests, final deployment, wrap-up complete (tasks 070-090) |

## Project Complete

All planned phases delivered. Deferred items (task 062b — wizard invitation step, SprkChat for external users) documented in README.md for the next iteration. See [notes/phase7-task075-final-deployment-validation.md](notes/phase7-task075-final-deployment-validation.md) for deployment runbook.
