# SDAP SPE Admin App

> **Portfolio**: [Project #1190](https://github.com/spaarke-dev/spaarke/issues/1190) · [Board #2](https://github.com/users/spaarke-dev/projects/2) — _auto-registered 2026-10-04 (existence only; Epic/Task Count/Start Date not yet set)_

> **Status**: ✅ Complete
> **Branch**: `feature/sdap-SPE-admin-app`
> **Started**: 2026-03-13
> **Completed**: 2026-03-14

## Overview

SharePoint Embedded (SPE) administrative application delivered as a Dataverse Code Page HTML web resource. Provides a single-pane-of-glass admin experience for managing all SPE resources — container types, containers, files, permissions, custom properties, columns, search, recycle bin, and security — directly from within the Dataverse model-driven app.

### Key Capabilities

- **Business Unit Scoping** — Select a Dataverse BU to scope operations to the correct container type and containers
- **Container Type Management** — Create, register, configure, manage SPE container types via Graph API
- **Container Management** — Full lifecycle: create, activate, lock/unlock, permissions, columns, custom properties
- **File Browser** — Browse, upload, download, preview, manage file metadata and versions
- **Search** — Cross-container and cross-item search with actionable result grids
- **Audit Logging** — All mutating operations logged with full context
- **Configuration** — Manage environments and container type configs in Dataverse tables

## Architecture

```
Code Page (React 18 + Fluent v9)
    │ /api/spe/*
    ▼
Sprk.Bff.Api (Minimal API)
    │
    ├── Microsoft Graph API (SPE operations)
    ├── Azure Key Vault (client secrets)
    └── Dataverse (config + audit tables)
```

## Tech Stack

| Layer | Technology |
|-------|-----------|
| Frontend | React 18, Fluent UI v9, Vite + viteSingleFile |
| Shared UI | @spaarke/ui-components, @spaarke/auth, @spaarke/sdap-client |
| Backend | .NET 8 Minimal API (Sprk.Bff.Api) |
| Storage | Dataverse (config/audit), Azure Key Vault (secrets) |
| External API | Microsoft Graph (v1.0 + beta), SharePoint REST API |

## Dataverse Tables

| Table | Purpose |
|-------|---------|
| `sprk_speenvironment` | Azure/SPE environment configurations |
| `sprk_specontainertypeconfig` | BU → Container Type mapping with auth parameters |
| `sprk_speauditlog` | Operation audit trail |

## Phasing

| Phase | Scope |
|-------|-------|
| **Phase 1 (MVP)** | All 3 tables, BU picker, containers, files, dashboard, settings, audit |
| **Phase 2** | Container types, columns, custom properties, search, security, recycle bin |
| **Phase 3** | eDiscovery, retention labels, multi-tenant, bulk operations |

## Graduation Criteria

- [x] Admin can create/manage container types from Dataverse
- [x] Admin can create/activate/manage containers without Postman/PowerShell
- [x] Admin can browse files, upload, manage metadata and permissions
- [x] Admin can search across containers with actionable results
- [x] All operations audit-logged
- [x] Configuration managed through Dataverse tables
- [x] Dark mode and accessibility support
- [x] All Graph API calls proxy through BFF
- [x] BU picker scopes operations correctly
- [x] Dashboard shows cached metrics with manual refresh

## Project Completion Summary

**Completed**: 2026-03-14 | **All 4176 tests passing (0 failures)**

### What Was Built

| Phase | Deliverables |
|-------|-------------|
| **Phase 1 (MVP)** | 3 Dataverse tables, BFF DI module, Graph service, audit service, auth filter, 25+ API endpoints, 10 Code Page UI components, full deployment pipeline |
| **Phase 2 (Full Features)** | Container type endpoints + UI, column/custom property editors, cross-container search, recycle bin, security alerts — all deployed |
| **Phase 3 (Advanced)** | Multi-tenant consuming tenant management, bulk operations (batch delete, batch permission assignment), multi-app registration — deployed and integration tested |

### Final Deployment State

| Component | Endpoint / Location | Status |
|-----------|-------------------|--------|
| BFF API | `https://spe-api-dev-67e2xz.azurewebsites.net` | Deployed — Healthy |
| Code Page | `sprk_speadmin` web resource in Dataverse | Deployed (1.24 MB) |
| Dataverse Tables | `sprk_speenvironment`, `sprk_specontainertypeconfig`, `sprk_speauditlog` | Created in spaarkedev1 |

### Skipped Items

| Task | Reason |
|------|--------|
| SPE-080 (eDiscovery dashboard) | Deferred by user — Graph eDiscovery API in beta, limited availability |
| SPE-081 (Retention label management) | Deferred by user — depends on SPE-080 infrastructure |

## Quick Links

- [Specification](spec.md)
- [Design Document](design.md)
- [Implementation Plan](plan.md)
- [Task Index](tasks/TASK-INDEX.md)
- [Project Context](CLAUDE.md)
