# CLAUDE.md — SDAP SPE Admin App

> **Project**: sdap-SPE-admin-app
> **Type**: Code Page HTML + BFF API Extension
> **Last Updated**: 2026-03-13

## Project Context

This project builds a SharePoint Embedded (SPE) administration application as a Dataverse Code Page. The admin selects a Business Unit to scope operations to the correct container type and containers.

### Key Architecture Decisions

- **Code Page HTML** (not PCF, not legacy JS) — ADR-006
- **React 18 + Fluent UI v9** bundled in single HTML file via Vite + viteSingleFile — ADR-021, ADR-022
- **BFF proxy pattern** — all Graph API calls through `/api/spe/*` endpoints — ADR-001
- **Endpoint filters** for authorization — ADR-008
- **DI feature module** (`AddSpeAdminModule()`) — ADR-010
- **Shared library reuse** — @spaarke/ui-components, @spaarke/auth, @spaarke/sdap-client — ADR-012
- **Single app registration** for Phase 1 (app-only tokens); OBO extensibility architected but not implemented
- **Background sync** for dashboard metrics (BackgroundService, not Azure Functions) — ADR-001
- **Reuse existing admin role** (no new Dataverse security role)

### Owner Clarifications

| Topic | Decision |
|-------|----------|
| Security role | Reuse existing admin role (System Administrator) |
| Token flow (Phase 1) | Single app registration, app-only tokens. Architect for OBO later. |
| Phase 1 scope | All 3 tables included (sprk_speenvironment, sprk_specontainertypeconfig, sprk_speauditlog) |
| Dashboard data | Background sync + manual refresh button |

## Applicable ADRs

| ADR | Key Constraint |
|-----|---------------|
| ADR-001 | Minimal API; no Azure Functions; BackgroundService for async |
| ADR-006 | Code Page for standalone UI; no legacy JS |
| ADR-008 | Endpoint filters for auth; no global middleware |
| ADR-010 | DI minimalism; ≤15 registrations; concrete types |
| ADR-012 | Reuse @spaarke/ui-components; Fluent v9 only |
| ADR-019 | ProblemDetails for all API errors |
| ADR-021 | Fluent v9 exclusively; dark mode; makeStyles; design tokens |
| ADR-022 | Code Page bundles React 18 + createRoot(); PCF uses platform React 16 |

## File Locations

### Code Page (Frontend)
```
src/solutions/SpeAdminApp/          # NEW — Code Page solution
├── src/
│   ├── main.tsx                    # Entry: createRoot()
│   ├── App.tsx                     # Main app with routing
│   ├── components/                 # UI components by section
│   ├── contexts/                   # BuContext, ThemeContext
│   ├── hooks/                      # useSpeApi, useContainers, etc.
│   ├── services/speApiClient.ts    # authenticatedFetch wrapper
│   ├── types/spe.ts                # TypeScript interfaces
│   └── utils/validators.ts         # GUID validation
├── index.html
├── vite.config.ts
└── dist/speadmin.html              # Deployable output
```

### BFF API (Backend)
```
src/server/api/Sprk.Bff.Api/
├── Endpoints/SpeAdmin/             # NEW — Endpoint groups
│   └── SpeAdminEndpoints.cs
├── Services/SpeAdmin/              # NEW — SPE admin services
│   ├── SpeAdminGraphService.cs     # Multi-config Graph client
│   ├── SpeAuditService.cs          # Audit logging
│   └── SpeDashboardSyncService.cs  # Background metrics sync
├── Api/Filters/
│   └── SpeAdminAuthorizationFilter.cs  # NEW — Admin auth filter
└── Infrastructure/DI/
    └── SpeAdminModule.cs           # NEW — Feature module
```

### Dataverse Tables
- `sprk_speenvironment` — Environment configs (tenant, endpoints)
- `sprk_specontainertypeconfig` — BU → CT mapping with auth params
- `sprk_speauditlog` — Operation audit trail

## Reference Patterns

| Pattern | Reference File |
|---------|---------------|
| Code Page entry | `src/solutions/LegalWorkspace/src/main.tsx` |
| Vite config | `src/solutions/LegalWorkspace/vite.config.ts` |
| Endpoint group | `src/server/api/Sprk.Bff.Api/Api/CommunicationEndpoints.cs` |
| Auth filter | `src/server/api/Sprk.Bff.Api/Api/Filters/DocumentAuthorizationFilter.cs` |
| BackgroundService | `src/server/api/Sprk.Bff.Api/Services/Communication/DailySendCountResetService.cs` |
| Graph client | `src/server/api/Sprk.Bff.Api/Infrastructure/Graph/GraphClientFactory.cs` |
| DI module | `src/server/api/Sprk.Bff.Api/Infrastructure/DI/CommunicationModule.cs` |

## Knowledge Files (Load as Needed)

| Resource | Path | When to Load |
|----------|------|-------------|
| Code Page template | `.claude/patterns/webresource/full-page-custom-page.md` | Scaffolding tasks |
| Endpoint definition | `.claude/patterns/api/endpoint-definition.md` | API endpoint tasks |
| Endpoint filters | `.claude/patterns/api/endpoint-filters.md` | Auth filter task |
| Error handling | `.claude/patterns/api/error-handling.md` | API endpoint tasks |
| Service registration | `.claude/patterns/api/service-registration.md` | DI module task |
| Background workers | `.claude/patterns/api/background-workers.md` | Dashboard sync task |
| OAuth scopes | `.claude/patterns/auth/oauth-scopes.md` | Auth tasks |
| Graph SDK v5 | `.claude/patterns/auth/graph-sdk-v5.md` | Graph service task |
| API constraints | `.claude/constraints/api.md` | All API tasks |
| Auth constraints | `.claude/constraints/auth.md` | Auth tasks |
| Web resource constraints | `.claude/constraints/webresource.md` | Code Page tasks |
| Shared UI guide | `docs/guides/SHARED-UI-COMPONENTS-GUIDE.md` | UI component tasks |

## 🚨 MANDATORY: Task Execution Protocol

**ALL tasks in this project MUST be executed via the `task-execute` skill.**

When you see "work on task X", "continue", "next task", or similar:
1. Find the task POML file in `projects/sdap-SPE-admin-app/tasks/`
2. Invoke `task-execute` skill with the task file path
3. Do NOT read the POML file and implement manually

See root CLAUDE.md for full task execution protocol.

---

*Generated by project-pipeline. Updated: 2026-03-13*
