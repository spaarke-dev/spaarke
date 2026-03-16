# Lessons Learned — Secure Project & External Access Platform

> **Project**: sdap-secure-project-module
> **Date**: 2026-03-16
> **Status**: Final

---

## What Went Well

### UAC Three-Plane Model Architecture
Designing the Unified Access Control model as a single participation record (`sprk_externalrecordaccess`) driving three planes (Dataverse, SPE, AI Search) proved to be the right abstraction. It keeps grant/revoke atomic from the caller's perspective and will extend naturally to matters, e-billing, and ad-hoc sharing without schema changes.

### Power Pages Built-In Tables (No Replication)
Leveraging `mspp_webrole`, `mspp_entitypermission`, and `adx_invitation` directly instead of creating custom equivalents saved significant effort. Power Pages' built-in invitation flow (email link + redemption) handled Entra External ID onboarding without custom code.

### BFF API Endpoint Filter Pattern
Applying the `ExternalCallerAuthorizationFilter` as an endpoint filter (ADR-008) rather than middleware kept internal and external auth paths fully separate. The filter resolves Contact identity from the bearer token and injects `ExternalCallerContext` cleanly.

### Code Page SPA with Vite + viteSingleFile
The ADR-026 pattern (Vite + React 18 + viteSingleFile) worked smoothly for the Power Pages SPA. A single self-contained HTML file with inlined JS/CSS simplified PAC CLI deployment considerably.

### Parallel Task Execution
Tasks in parallel groups (G: document library, events, tasks, contacts; H: E2E tests) executed efficiently. The parallel-group annotations in TASK-INDEX.md were useful for planning concurrent work.

---

## Challenges and How They Were Resolved

### Power Pages Table Permission Parent-Chain Depth
Initial design had a 3-level chain (externalrecordaccess → project → sub-tables). After reviewing Power Pages docs, the practical limit is 4-5 levels, so the design stayed within bounds. However, configuring this correctly required careful ordering of site settings and took more iteration than expected.

### Entra External ID Identity Provider Setup
Configuring Entra External ID as a Power Pages IDP required specific site settings for OAuth implicit grant flow, invitation redemption, and token claims mapping. Documentation was sparse; the working configuration is fully captured in `notes/phase3-task020-entra-external-id-config.md` for future reference.

### AI Search `project_ids` Filter Performance
The `search.in` filter on `project_ids` (a collection field) performed well in dev. For production scale (500+ projects), the guidance in the deployment runbook recommends benchmarking and potentially adding a composite index. This was flagged in the risk register and remains a watch item.

### SPE External Sharing Override
The SPE external sharing override (`Set-SPOApplication -OverrideTenantSharingCapability`) requires a tenant administrator. This was identified early as a risk and the requirement was documented in the deployment runbook. It is a one-time tenant-level configuration, not per-project.

### adx_invitation N:N Web Role Join Table
The join table `adx_invitation_mspp_webrole_powerpagecomponent` for the invitation-web-role N:N relationship is not surfaced in Maker Portal. It requires FetchXML with `intersect="true"` link-entity to query. This pattern is documented in the session notes and in the E2E test files.

---

## Architectural Decisions That Should Propagate

### ExternalCallerAuthorizationFilter Pattern
The pattern of a dedicated endpoint filter for external (Contact-based) callers, separate from the existing SystemUser-based filter, is reusable for any future Power Pages SPA feature that calls the BFF API. The filter signature and `ExternalCallerContext` DI injection pattern should be added to `.claude/patterns/api/endpoint-filters.md` as a second example.

### Power Pages SPA Auth Module
The `portalAuth.ts` module (CSRF token extraction, session validation, Power Pages Web API client with `X-Requested-With` header) is a reusable foundation for any future Power Pages SPA. Consider extracting it to `@spaarke/ui-components` or a shared SPA utilities package.

### UAC Grant/Revoke as Idempotent Operations
Designing grant and revoke to be idempotent (safe to call multiple times) simplified error handling and retry logic significantly. All three planes (Dataverse deactivate, SPE membership removal, AI Search filter update) are idempotent. This should be a standard pattern for future orchestration endpoints.

---

## Deferred Items

| Item | Reason Deferred | Where Documented |
|------|----------------|-----------------|
| Task 062b — External User Invitation Step in Create Project Wizard | Owner confirmed as post-MVP; direct invitation via project participant subgrid is sufficient for MVP | TASK-INDEX.md (marked ⏭️) |
| SprkChat for external users | Significant additional work; AI toolbar via playbooks is sufficient for MVP | README.md Out of Scope |
| Teams notifications | Low priority; email via `sprk_communication` covers MVP | README.md Out of Scope |
| AI Search performance benchmarking at scale | Requires production data volume | Deployment runbook, risk register |

---

## ADR Compliance Notes

All implemented code was verified against applicable ADRs:

| ADR | Compliance Status | Notes |
|-----|------------------|-------|
| ADR-001 Minimal API | Compliant | All endpoints use MapGet/MapPost with endpoint filters |
| ADR-002 Thin plugins | Compliant | No plugins created; all orchestration in BFF API |
| ADR-006 PCF vs Code Pages | Compliant | SPA is a Code Page (React 18, bundled), no PCF |
| ADR-007 SpeFileStore facade | Compliant | All SPE operations go through SpeFileStore |
| ADR-008 Endpoint filters | Compliant | ExternalCallerAuthorizationFilter on all external endpoints |
| ADR-009 Redis-first caching | Compliant | CachedAccessDataSource used for access level lookups |
| ADR-010 DI minimalism | Compliant | Module registers 6 services (<15 limit) |
| ADR-012 Shared component library | Compliant | SPA imports from @spaarke/ui-components |
| ADR-013 AI Architecture | Compliant | AI toolbar invokes existing playbook endpoints; no separate AI service |
| ADR-021 Fluent UI v9 | Compliant | All SPA UI uses FluentProvider + Fluent v9 tokens; dark mode supported |
| ADR-022 PCF platform libraries | Compliant | Code Page uses React 18 bundled (not platform-provided) |
| ADR-026 Full-page custom page | Compliant | Vite + viteSingleFile produces single self-contained HTML |

---

## Repository Hygiene

- `notes/debug/`, `notes/drafts/`, `notes/handoffs/`, `notes/spikes/` — all empty, kept as structure
- `notes/phase*` files — deployment and configuration guides retained as permanent reference (not ephemeral)
- No test artifacts or temporary files left in the project folder

---

*This document is the final project retrospective for sdap-secure-project-module.*
