# Spaarke External Access Platform (R2) — Module-Host SPA Foundation + Legal Front Door

> **Last Updated**: 2026-08-06
>
> **Status**: In Progress (INITIALIZED — tasks generated; execution owner-gated wave-by-wave)

## Overview

R2 generalizes R1's single Outside-Counsel SPA into a **module-host SPA platform** serving **all
non-core (SPA) users** — a Teams-capable shell whose home is an entitlement-gated **card launcher**.
It ships the **Legal Front Door** (generic typed-intake framework + NDA workflow + Policy &
Procedures) as the second module, with Outside Counsel refactored into the first registered module.
R2 builds directly on already-merged R1 + teams-app-r1 code (dual-plane auth, `CallerPrincipalResolver`,
`ExternalCollaboration` policy) rather than rebuilding it.

## Quick Links

| Document | Description |
|----------|-------------|
| [Implementation Plan](./plan.md) | Phased WBS (P0–P4) + discovered resources |
| [Design Spec](./spec.md) | AI-optimized specification (FR-01–FR-22, NFRs, ADR Tensions) |
| [Design Brief](./design.md) | Original scoping brief |
| [UX Brief](./notes/ux-brief.md) | Locked UX north-star (gates the P0 prototype) |
| [Task Index](./tasks/TASK-INDEX.md) | Task tracker + dependency graph + parallel groups |
| [AI Context](./CLAUDE.md) | Project context for Claude Code |

## Current Status

| Metric | Value |
|--------|-------|
| **Phase** | Development (initialized) |
| **Progress** | 0% (planning complete; execution not started) |
| **Target Date** | — |
| **Completed Date** | — |
| **Owner** | Ralph Schroeder |

## Problem Statement

R1 proved the external-access pattern with **one** app (Outside Counsel on SWA + CIAM, broker-only).
But access is app-agnostic + Project-shaped, there is no module/app concept, one identity plane
(CIAM only), no admin UI, and no card launcher. Every new non-core capability would be a separate
hand-copied SPA that drifts. Internal unlicensed employees (workforce SSO) have no self-service
surface at all, and there is no way to say "this user gets these modules."

## Solution Summary

One Teams-capable module-host shell serves every SPA user (core-user-vs-SPA-user is the real axis,
not internal-vs-external). A code-side module registry + card launcher shows only the modules a user
is entitled to, resolved by a `/me` endpoint over a **two-tier** model: **Tier-1 entitlement**
(Entra App Roles for internal, per-Contact grants for external) is independent of **Tier-2
record-scope** (a pluggable per-module predicate). Identity plane (Teams SSO / workforce / CIAM) is a
sign-in detail selected at bootstrap. Legal Front Door adds a generic typed-intake framework + NDA +
Policy & Procedures over the same shell, BFF, and access model.

## Graduation Criteria

The project is considered **complete** when:

- [ ] One module-host SPA renders a card launcher showing only entitled modules; an unentitled module is neither shown nor routable (direct-route denied)
- [ ] The same URL serves both planes and installs as a Teams app (CIAM browser + workforce browser/Teams, Teams dark-mode render)
- [ ] "All employees" entitlement via one App-Role/group assignment, no per-user provisioning
- [ ] Front Door: employee submits a typed request (NDA/P&P) with app-only upload, sees only their own requests; requester Contact lazily created once
- [ ] Outside Counsel works unchanged as a registered module (R1 parity)
- [ ] Core user grants/revokes module entitlement + record access from a UI (no curl)
- [ ] Provisioner self-heals the CIAM 409 window; live-E2E wrong-issuer→401 + no email-hijack; SSPR first-run verified
- [ ] Adding a new module = register card + lazy route + entitlement (documented recipe + reviewer walkthrough)
- [ ] P0 prototype visual-approved against the UX brief before frontend build; production frontend built on `@spaarke/ui-components` (no hand-rolled UI)

## Scope

### In Scope

- Module-host shell (extends R1 `external-spa`): code-side module registry + card-launcher home + `/me`-driven visibility
- Dual identity-plane auth in one shell/deployment (Teams SSO / workforce / CIAM); workforce-plane external-app auth policy
- Teams personal-tab app packaging (Teams JS SDK init, theme bridging, CSP `frame-ancestors`)
- Two-layer access model: NEW module-entitlement layer + keep R1 `sprk_externalrecordaccess` record participation
- Lazy Contact attribution (resolve-or-create by workforce oid on first attributed action)
- Core-user admin UI (Fluent v9, dark-mode) for grant/revoke
- Legal Front Door: intake schema on `sprk_servicerequest`; generic typed-intake framework; NDA module; Policy & Procedures module; app-only SPE upload
- R1 hardening: provisioner self-healing, live-E2E, SSPR first-run
- Cleanup: dead Power Pages proxy/config; transitional `/api/v1/collab`; inert `ExternalCallerAuthorizationFilter`
- P0 UX prototype (prototype-first) on existing `spaarke-prototype` infrastructure

### Out of Scope

- E-billing module → R3
- Deep Legal Front Door workflows beyond R2 first cut (approval-to-publish, trademark, invention disclosure); e-signature/automated filing
- Self-service CIAM public sign-up / registration router
- Core-user MDA experience; E-3 direct-Office boundary (ADR-028 A1)
- Dataverse-driven module catalog (`sprk_module`) — modules are code-registered in R2
- Formal market/user research (owner decision: internal precedent only)

## Key Decisions

| Decision | Rationale | ADR |
|----------|-----------|-----|
| INITIALIZE-ONLY pipeline run | Heavy BFF coordination + owner-gated teams-app-r1 dependency; matches sibling-project pattern | — |
| Author ADR-028 **Amendment A3** (not A2) | A2 already exists (teams-app-r1, Teams host); A3 ratifies R2's dual-plane module-framework generalization | [ADR-028](../../.claude/adr/ADR-028-spaarke-auth-architecture.md) |
| Prototype-first P0 UX phase | Many net-new surfaces; validate visually before production build | — |
| Build on `@spaarke/ui-components` + `SprkModal` (no hand-rolled UI) | §11 default-to-reuse; consistency + dark-mode/Teams-theme correctness | [ADR-050](../../.claude/adr/ADR-050-canonical-modal-shell.md), ADR-021 |
| Lift + generalize FR-22 (delivered by teams-app-r1) | Reuse tested `CallerPrincipalResolver` + `ExternalCollaboration`; third-plane seam ready | [ADR-008](../../.claude/adr/ADR-008-endpoint-filters.md), ADR-034 |

## Risks & Mitigations

| Risk | Impact | Likelihood | Mitigation |
|------|--------|------------|------------|
| Merge collision on external-access BFF surface with teams-app-r1 | High | Med | `/conflict-check` before EVERY BFF PR; `parallel-safe:false` on shared files; coordinate merge order |
| teams-app-r1 operator-gated BFF redeploy + live Teams E2E not done | Med | Med | P1 prerequisite; sequence P1 auth work after that deploy |
| Module-entitlement schema shape undecided | Med | Low | Resolved in P2 (deferred-by-owner to resource discovery); `dataverse-create-schema` |
| Publish size creep on BFF | Med | Low | ≤60 MB ceiling; report delta per BFF task (baseline 46.90 MB incl PDBs) |
| UX drift across many net-new screens | Med | Med | P0 prototype + locked UX brief; production tasks cite them |

## Dependencies

| Dependency | Type | Status | Notes |
|------------|------|--------|-------|
| R1 shipped + live | Internal | Ready | Done (DI-028-01 resolved) |
| teams-app-r1 BFF redeploy + live Teams E2E | Internal | Blocked | Operator-gated shared-infra ops (P1 prereq) |
| Entra App Role defs + test group | External | Blocked | Ops/portal — `FrontDoorUser` role + "All Employees" group |
| CIAM test user (live-E2E + SSPR) | External | Blocked | FR-19 / FR-20 |
| Legal Front Door intake schema sign-off | Internal | Blocked | Request types + status model |
| Azure Static Web Apps resource + deploy token | External | Ready | R1 `deploy-external-spa.yml` exists |

## Team

| Role | Name | Responsibilities |
|------|------|------------------|
| Owner | Ralph Schroeder | Overall accountability + wave gating |
| Developer | Claude Code | Implementation (task-execute per wave) |
| Reviewer | Ralph Schroeder | Code review, design review, visual approval |

## Changelog

| Date | Version | Change | Author |
|------|---------|--------|--------|
| 2026-08-06 | 1.0 | Project initialized via `/project-pipeline` (INITIALIZE-ONLY) | Claude Code |
| 2026-08-06 | 1.1 | P0 prototype complete + signed off; foundation pivoted card-launcher → **workspace shell**; FR-23–27 added; P&P = `sprk_document`+category (no `sprk_policy`); Ask Legal bounded; 2 spikes; tasks re-decomposed to 40 (see `design.md` §12) | Claude Code |

---

*Generated by `/project-pipeline` (INITIALIZE-ONLY). Source: `spec.md` + `design.md`. Plan: `plan.md`.*
