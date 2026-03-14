# SDAP SPE Admin App

> **Status**: 🟡 In Progress
> **Branch**: `feature/sdap-SPE-admin-app`
> **Started**: 2026-03-13

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

- [ ] Admin can create/manage container types from Dataverse
- [ ] Admin can create/activate/manage containers without Postman/PowerShell
- [ ] Admin can browse files, upload, manage metadata and permissions
- [ ] Admin can search across containers with actionable results
- [ ] All operations audit-logged
- [ ] Configuration managed through Dataverse tables
- [ ] Dark mode and accessibility support
- [ ] All Graph API calls proxy through BFF
- [ ] BU picker scopes operations correctly
- [ ] Dashboard shows cached metrics with manual refresh

## Quick Links

- [Specification](spec.md)
- [Design Document](design.md)
- [Implementation Plan](plan.md)
- [Task Index](tasks/TASK-INDEX.md)
- [Project Context](CLAUDE.md)
