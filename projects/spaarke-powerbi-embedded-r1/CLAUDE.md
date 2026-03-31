# Power BI Embedded Reporting R1 — Project Context

## Project Summary
Reporting module using Power BI Embedded ("App Owns Data") with service principal profiles, Import mode, in-browser report authoring, and 4-layer security.

## Module Naming
- Use "Reporting" everywhere — NOT "Analytics" or "Analysis" (those are AI features)
- Code Page: `sprk_reporting`
- Endpoints: `/api/reporting/*`
- Security role: `sprk_ReportingAccess`
- Env var: `sprk_ReportingModuleEnabled`

## Key Rules
- App Owns Data pattern only — never user auth for PBI
- Service principal profiles for multi-tenant workspace isolation
- Import mode with scheduled Dataverse refresh (not Lakehouse/Direct Lake in R1)
- BU RLS via EffectiveIdentity in embed tokens
- Redis-cached embed tokens with 80% TTL auto-refresh
- .pbix templates versioned in source control (`reports/` folder)
- All PBI config via environment variables (BYOK-compatible)

## Applicable ADRs
- ADR-001: Minimal API for reporting endpoints
- ADR-006: Code Page for standalone reporting page
- ADR-008: Endpoint filters for authorization
- ADR-009: Redis caching for embed tokens
- ADR-010: DI minimalism (2 new registrations)
- ADR-012: Shared components
- ADR-021: Fluent v9, dark mode
- ADR-026: Vite single-file build

## New Dependencies (npm)
- `powerbi-client-react` ^2.0.2 (React 18+ only)
- `powerbi-client` ^2.23.0 (JS SDK)

## New Dependencies (NuGet)
- `Microsoft.PowerBI.Api` (PBI REST API client)

## 🚨 MANDATORY: Task Execution Protocol
When executing tasks in this project, Claude Code MUST invoke the `task-execute` skill.
DO NOT read POML files directly and implement manually.
See root CLAUDE.md for full protocol and trigger phrases.
