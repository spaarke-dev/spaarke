# ADRs - Concise Versions (AI Context)

> **Purpose**: Concise versions of ADRs optimized for AI context loading
> **Target**: 100-150 lines per ADR
> **Full versions**: See `docs/adr/` for complete ADRs

## About This Directory

This directory contains AI-optimized versions of Architecture Decision Records. Each file focuses on:
- **Decision**: What was decided
- **Constraints**: MUST/MUST NOT rules
- **Key patterns**: Code examples
- **Rationale**: Brief why (1-2 sentences)

**Omitted from concise versions**:
- Verbose context/background
- Historical discussion
- Detailed alternatives analysis
- Long examples

## ADR Index

| ADR | Title | Key Constraint | Status |
|-----|-------|----------------|--------|
| ADR-001 | Minimal API + BackgroundService | No Azure Functions | Accepted |
| ADR-002 | Thin Dataverse plugins | No HTTP/Graph calls in plugins | Accepted |
| ADR-006 | UI Surface Architecture | Code Pages are default for new UI; PCF only for form binding | Accepted (Revised 2026-03-19) |
| ADR-007 | SpeFileStore facade | No Graph SDK types leak above facade | Accepted |
| ADR-008 | Endpoint filters for auth | No global auth middleware | Accepted |
| ADR-010 | DI minimalism | ≤15 non-framework DI registrations | Accepted |
| ADR-012 | Shared component library | `@spaarke/ui-components` as single source of truth; abstracted services via `IDataService` | Accepted (Revised 2026-03-19) |
| ADR-013 | AI Architecture | AI Tool Framework; extend BFF | Accepted |
| ADR-021 | Fluent UI v9 Design System | All UI uses Fluent v9; React 19 for Code Pages; dark mode required | Accepted |
| ADR-022 | PCF Platform Libraries | PCF uses React 16/17 platform-provided; Code Pages use React 19 bundled | Accepted |
| ADR-023 | ~~Choice Dialog Pattern~~ | _Superseded — demoted to pattern_ | Superseded (2026-03-19) |
| ADR-026 | Code Page Build Standard | Vite + `vite-plugin-singlefile` + React 19 for all Code Pages | Accepted (Revised 2026-03-19) |
| ADR-027 | Subscription Isolation & Dataverse Solution Mgmt | Managed solutions for prod; env-separated subscriptions | Accepted |

---

## Usage by AI Agents

Load concise ADRs proactively when creating new components:
- Creating API → Load ADR-001, ADR-008, ADR-010
- Creating PCF → Load ADR-006, ADR-012, ADR-022 (React 16 compatibility)
- Creating Code Page (dialog, wizard, full page) → Load ADR-006, ADR-026, ADR-021 (React 19)
- Creating Plugin → Load ADR-002
- Working with auth → Load ADR-004, ADR-016
- Working with SPE → Load ADR-007, ADR-019
- Working with UI/UX → Load ADR-021, ADR-022
- Working with shared components → Load ADR-012 (service architecture, portability tiers)
- Deploying to production → Load ADR-027 (subscription isolation, Dataverse solution management)
- Working with Dataverse solutions → Load ADR-027 (managed vs unmanaged, import order)

Full ADRs in `docs/adr/` should be loaded only when:
- Need historical context
- Debugging architectural decisions
- Proposing changes to architecture
