# Secure Project & External Access Platform

> **Status**: Planning
> **Branch**: `feature/sdap-secure-project-module`
> **Created**: 2026-03-16
> **Predecessor**: N/A (greenfield)

## Quick Links

- [Implementation Plan](plan.md)
- [Task Index](tasks/TASK-INDEX.md)
- [Design Specification](spec.md)
- [Original Design](design.md)
- [AI Context](CLAUDE.md)

## Overview

Spaarke External Access enables Core Users to create controlled collaboration workspaces for external participants (outside counsel, expert witnesses, clients, vendors) who access the system through a Power Pages Code Page SPA authenticated via Microsoft Entra External ID. The system introduces a Unified Access Control (UAC) model that orchestrates three access planes — Dataverse records, SharePoint Embedded files, and Azure AI Search — through a single participation grant.

Secure Projects are the first implementation of this broader external access capability, targeting the experience set by Harvey AI's Vault and Shared Spaces.

## Problem Statement

Enterprise legal departments need to collaborate with external parties (counsel, witnesses, clients) on sensitive projects. Current solutions either lack AI capabilities for external users (HighQ, iManage, NetDocuments) or are closed ecosystems with artificial limits (Harvey AI's 10K document cap, $1K+/user/month). There is no platform that gives external users extensible, playbook-driven AI analysis within a secure, isolated workspace built on the Microsoft ecosystem.

## Proposed Solution

Build a Secure Project module on Power Pages with a React 18 Code Page SPA, leveraging:
- **Unified Access Control** — three-plane orchestration (Dataverse table permissions, SPE container membership, AI Search query-time filters) via a single `sprk_externalrecordaccess` participation record
- **Power Pages built-in tables** — web roles, table permissions, invitations, external identity mapping (no replication)
- **BFF API** — all orchestration through existing `Sprk.Bff.Api` (no plugins, no Power Automate)
- **Playbook-driven AI** — external users get toolbar-driven summaries, analysis, and semantic search with IP protection

## Scope

### In Scope
- UAC three-plane orchestration (grant/revoke)
- Secure Project creation (BU, SPE container, External Access Account provisioning)
- External user invitation (Power Pages `adx_invitation`, Entra External ID)
- Power Pages Code Page SPA (external workspace home + project page)
- Document collaboration (upload, download, versioning, AI summaries)
- AI features via toolbar (playbook-driven, IP-protected)
- Task and event management for external users
- Access level enforcement (View Only / Collaborate / Full Access)
- Email notifications via existing `sprk_communication` module
- Access revocation and project closure cascading
- Custom table: `sprk_externalrecordaccess`
- Modified table: `sprk_project` (add `sprk_issecure`, `sprk_securitybuid`, `sprk_externalaccountid`)

### Out of Scope
- SprkChat for external users (deferred to post-MVP)
- Mobile-specific UI
- Matters, e-billing, ad-hoc sharing (future phases — UAC model supports them)
- Power Automate flows / Dataverse plugins for orchestration
- Self-service registration (invitation-only)
- Real-time collaboration (co-editing)
- Teams notifications

## Graduation Criteria

1. [ ] Core User creates Secure Project; BU, SPE container, and External Access Account provisioned automatically
2. [ ] Core User invites external Contact; invitation email sent via `sprk_communication`; Contact redeems invitation and authenticates via Entra External ID
3. [ ] External workspace SPA loads within 3 seconds; shows only accessible projects
4. [ ] Project page displays documents, events, tasks, contacts — all scoped to access level
5. [ ] Document upload/download respects access level (View Only: no download/upload)
6. [ ] AI toolbar buttons invoke playbooks; results displayed as structured output; no playbook internals visible
7. [ ] Semantic search returns only accessible project documents (AI Search `project_ids` filter)
8. [ ] Access revocation cascades across all three UAC planes
9. [ ] Project closure deactivates all participation records and removes SPE access
10. [ ] Full audit trail queryable (who granted, when, to whom, what level)
11. [ ] SPA meets Fluent UI v9 standards: dark mode, high-contrast, WCAG 2.1 AA
12. [ ] All unit tests pass; integration tests cover grant/revoke/search flows
