# Code Patterns — Pointer-Based Reference

> **Last Reviewed**: 2026-04-05
> **Reviewed By**: ai-procedure-refactoring-r2
> **Status**: Verified

> **Format**: Each pattern file is max 25 lines pointing to canonical source code.
> **Structure**: When (context) → Read These Files (source paths) → Constraints (ADRs) → Key Rules
> **Principle**: Code is the source of truth — patterns point to it, not describe it.

---

## Pattern Subdirectories

| Directory | Patterns | Domain | Last Reviewed | Status |
|-----------|----------|--------|---------------|--------|
| [api/](api/INDEX.md) | 8 | BFF API endpoints, filters, errors, jobs, resilience, DI, email, provisioning | 2026-04-05 | Verified (1 Current) |
| [auth/](auth/INDEX.md) | 13 | OAuth, OBO, MSAL, scopes, Graph SDK, webhooks, access control, BFF URL | 2026-04-05 | Verified (2 Current) |
| [caching/](caching/INDEX.md) | 3 | Redis distributed cache, request cache, token cache | 2026-04-05 | Verified |
| [dataverse/](dataverse/INDEX.md) | 5 | Plugins, Web API, entity CRUD, relationships, polymorphic resolver | 2026-04-05 | Verified |
| [pcf/](pcf/INDEX.md) | 7 | Control lifecycle, errors, themes, queries, dialogs, **Fluent v9 modern theming**, **Canvas-vs-MDA disabled** | 2026-05-26 | Verified + 2 Current |
| [ai/](ai/INDEX.md) | 3 | Streaming endpoints, text extraction, analysis scopes | 2026-04-05 | Verified |
| [testing/](testing/INDEX.md) | 3 | Unit tests, mocking, integration/arch tests | 2026-04-05 | Verified |
| [webresource/](webresource/INDEX.md) | 4 | Code Pages, wizard wrappers, custom dialogs, subgrid rollup | 2026-04-05 | Verified |
| [ui/](ui/INDEX.md) | 5 | Choice dialog + **Fluent v9 component authoring / theming / portal-gotcha / React-version boundaries** | 2026-05-26 | Verified + 4 Current |

**Total**: 51 pointer files across 9 subdirectories (6 added 2026-05-26 for Fluent v9)

---

## Loading Strategy

Load specific pattern files when implementing related features:

| Task Type | Load These Patterns |
|-----------|---------------------|
| Creating BFF endpoint | `api/endpoint-definition.md` + `api/error-handling.md` |
| Adding authorization | `api/endpoint-filters.md` + `auth/uac-access-control.md` |
| Implementing auth | `auth/oauth-scopes.md` + `auth/obo-flow.md` |
| Creating PCF control | `pcf/control-initialization.md` + `pcf/theme-management.md` + `pcf/fluent-v9-modern-theming.md` |
| Authoring/modifying Fluent v9 UI (any surface) | `ui/fluent-v9-component-authoring.md` + `ui/fluent-v9-theming.md` (+ `ui/fluent-v9-portal-gotcha.md` if portal components) |
| PCF shipped to both Canvas + MDA | `pcf/fluent-v9-canvas-vs-mda-disabled.md` |
| Authoring in `Spaarke.UI.Components` (cross-surface) | `ui/fluent-v9-react-version-boundaries.md` + `ui/fluent-v9-component-authoring.md` |
| Writing plugin | `dataverse/plugin-structure.md` |
| Adding caching | `caching/distributed-cache.md` |
| Writing tests | `testing/unit-test-structure.md` + `testing/mocking-patterns.md` |
| Background jobs | `api/background-workers.md` |
| HTTP resilience | `api/resilience.md` |
| Email integration | `api/send-email-integration.md` |
| Graph API features | `auth/graph-sdk-v5.md` + `auth/graph-endpoints-catalog.md` |
| Code Page auth | `auth/spaarke-sso-binding.md` (canonical: INV-1..INV-8 + v2 contract) + `auth/xrm-webapi-vs-bff-auth.md` + [ADR-028](../adr/ADR-028-spaarke-auth-architecture.md) |
| Building Code Page SPA | `webresource/full-page-custom-page.md` |
| Wizard dialog | `webresource/code-page-wizard-wrapper.md` |
| Polymorphic associations | `dataverse/polymorphic-resolver.md` |
| Lookup relationships | `dataverse/relationship-navigation.md` |

---

## Related Constraint Files

Patterns show **how** — constraints define **what**:

| Pattern Domain | Constraint File |
|----------------|-----------------|
| api/ | `.claude/constraints/api.md` |
| auth/ | `.claude/constraints/auth.md` |
| pcf/ | `.claude/constraints/pcf.md` |
| dataverse/ | `.claude/constraints/plugins.md` |
| caching/ | `.claude/constraints/data.md` |
| ai/ | `.claude/constraints/ai.md` |
| testing/ | `.claude/constraints/testing.md` |
| webresource/, ui/ | `.claude/constraints/webresource.md` |
