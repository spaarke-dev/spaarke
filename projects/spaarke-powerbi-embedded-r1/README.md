# Power BI Embedded Reporting R1

> **Last Updated**: 2026-03-31
>
> **Status**: In Progress

## Overview

Embed Power BI reports and dashboards into Spaarke's MDA via a full-page "Reporting" Code Page (`sprk_reporting`), using "App Owns Data" with service principal profiles for multi-tenant isolation, Import mode with scheduled Dataverse refresh, in-browser report authoring, and 4-layer security. No end-user Power BI licensing required.

## Quick Links

| Document | Description |
|----------|-------------|
| [Project Plan](./plan.md) | Implementation plan with phases and WBS |
| [Design Spec](./design.md) | Original design specification |
| [AI Spec](./spec.md) | AI-optimized implementation specification |
| [Task Index](./tasks/TASK-INDEX.md) | Task breakdown and status |
| [Current Task](./current-task.md) | Active task state tracker |

## Current Status

| Metric | Value |
|--------|-------|
| **Phase** | Development |
| **Progress** | 0% |
| **Target Date** | — |
| **Completed Date** | — |
| **Owner** | Spaarke Dev Team |

## Problem Statement

Spaarke customers need embedded analytics and reporting capabilities within the MDA without requiring Power BI Desktop or per-user Power BI Pro/PPU licenses. Currently there is no way to view or author Power BI reports inside the Spaarke platform, forcing users to context-switch to external tools.

## Solution Summary

A new "Reporting" module consisting of a full-page Code Page (`sprk_reporting`) built with React 19 + Vite + `powerbi-client-react`, backed by BFF API endpoints that manage embed token generation via service principal profiles. Reports are stored in Power BI workspaces with Import mode refresh, cataloged in a `sprk_report` Dataverse entity, and secured through environment variable gating, security roles, workspace isolation, and business unit RLS.

## Graduation Criteria

The project is considered **complete** when:

- [ ] Reporting Code Page renders embedded Power BI reports with BU RLS filtering
- [ ] Report catalog dropdown shows all reports grouped by category
- [ ] In-browser report authoring (create, edit, save, save-as) works for Author role
- [ ] Export to PDF/PPTX works via Power BI REST API
- [ ] 5 standard product reports deployed and rendering with customer data
- [ ] Module gating via environment variable works (hidden when disabled)
- [ ] Security role enforcement works (Viewer/Author/Admin tiers)
- [ ] Token auto-refresh at 80% TTL works without page reload
- [ ] Dark mode renders correctly with transparent PBI background
- [ ] Deployment pipeline deploys .pbix templates to customer workspaces
- [ ] Customer onboarding provisions workspace + SP profile + reports
- [ ] Works across all 3 deployment models (multi-customer, dedicated, customer tenant)

## Scope

### In Scope

- `sprk_reporting` Code Page (React 19, Vite single-file build)
- BFF `ReportingEmbedService` with service principal profile management
- BFF embed token generation with Redis caching and BU RLS
- `sprk_report` Dataverse entity for report catalog
- Report selector dropdown grouped by category
- View mode: embedded report rendering with BU RLS filtering
- Edit mode: in-browser report authoring (create, edit, save, save-as)
- Module gating via `sprk_ReportingModuleEnabled` environment variable
- User access control via `sprk_ReportingAccess` security role
- Business unit RLS via EffectiveIdentity
- Token auto-refresh at 80% TTL
- Export to PDF/PPTX
- 5 standard product reports (.pbix templates)
- Report deployment pipeline script
- Report versioning in source control
- Dark mode support
- Customer onboarding scripts

### Out of Scope

- Paginated reports (RDLC-style — planned R2)
- Fabric Lakehouse / Direct Lake data source
- Real-time streaming datasets
- Dashboard tiles on entity forms (PCF)
- Power BI alerts and subscriptions
- Custom visuals marketplace integration
- Semantic model authoring by customers
- Report scheduling (email delivery)
- Capacity management admin UI
- Cross-customer analytics

## Key Decisions

| Decision | Rationale | ADR |
|----------|-----------|-----|
| App Owns Data (service principal) | No per-user PBI licensing; server-side control | — |
| Service principal profiles | Multi-tenant workspace isolation without multiple app registrations | — |
| Import mode (not DirectQuery) | Lower cost, simpler setup, sufficient freshness (4x daily) | — |
| Code Page (not PCF) | Full-page experience, React 19, bundled dependencies | [ADR-006](../../.claude/adr/ADR-006-pcf-over-webresources.md) |
| Redis-first token caching | Cross-instance cache, <500ms cached token retrieval | [ADR-009](../../.claude/adr/ADR-009-redis-caching.md) |
| Endpoint filters for auth | Per-endpoint authorization, not global middleware | [ADR-008](../../.claude/adr/ADR-008-endpoint-filters.md) |
| Vite + single-file build | Dataverse web resource deployment pattern | [ADR-026](../../.claude/adr/ADR-026-full-page-custom-page-standard.md) |
| "Reporting" naming (not "Analytics") | Avoids conflict with existing AI Analysis features | — |

## Risks & Mitigations

| Risk | Impact | Likelihood | Mitigation |
|------|--------|------------|------------|
| F-SKU capacity not provisioned | High | Low | Document prerequisites; fail gracefully with clear message |
| Service principal permissions misconfigured | High | Medium | Detailed onboarding checklist; health check endpoint |
| Power BI JavaScript SDK breaking changes | Medium | Low | Pin `powerbi-client-react` to 2.0.2; test on update |
| Large semantic models exceed Import limits | Medium | Low | Monitor data volume; plan Direct Lake migration for R2 |
| Dark mode PBI background not transparent | Medium | Medium | Test with `{ background: models.BackgroundType.Transparent }` early |

## Dependencies

| Dependency | Type | Status | Notes |
|------------|------|--------|-------|
| F-SKU capacity (F2+ for dev) | External | Pending | Must be provisioned before embed testing |
| Entra ID app registration (PBI perms) | External | Pending | `Dataset.ReadWrite.All`, `Content.Create`, `Workspace.ReadWrite.All` |
| Service principal in PBI admin settings | External | Pending | Must be enabled by tenant admin |
| Power BI REST API | External | GA | Microsoft service |
| `powerbi-client-react` 2.0.2 | External | GA | npm package |
| `Microsoft.PowerBI.Api` NuGet | External | GA | BFF dependency |
| `@spaarke/ui-components` | Internal | Production | Shared component library |
| `@spaarke/auth` | Internal | Production | Auth bootstrap for Code Pages |
| BFF API (`Sprk.Bff.Api`) | Internal | Production | Endpoint host |
| Redis cache | Internal | Production | Token caching |

## Team

| Role | Name | Responsibilities |
|------|------|------------------|
| Owner | Spaarke Dev Team | Overall accountability |
| Developer | Claude Code | Implementation |
| Reviewer | Spaarke Dev Team | Code review, design review |

## Changelog

| Date | Version | Change | Author |
|------|---------|--------|--------|
| 2026-03-31 | 1.0 | Initial project setup from spec.md | Claude Code |

---

*Generated by project-pipeline skill*
