# Spaarke External Access Platform R4

> **Portfolio**: [Project #2680](https://github.com/spaarke-dev/spaarke/issues/2680) · [Board #2](https://github.com/users/spaarke-dev/projects/2) · Epic [#2099 EXTERNAL ACCESS SPA](https://github.com/spaarke-dev/spaarke/issues/2099) — _registered 2026-11-10 by `/project-pipeline`_

> **Last Updated**: 2026-11-11
>
> **Status**: In Progress — Phase 1 executing (tasks 001–005 done; next 006)

## Overview

R4 turns the external SPA's record-detail modal into a config-driven **record template** for all six SPA record types (Matter, Project, Work Assignment, Invoice, Service Request, Document). Users **act where they read**, by each record's rights, through a shared `RecordCommandBar`. R4 also closes the R3 and ebilling backlog handed to it, in four staged phases. Each phase is deployed to dev and proved by E2E.

## Quick Links

| Document | Description |
|----------|-------------|
| [Project Plan](./plan.md) | Phases, WBS, risks |
| [Spec](./spec.md) | AI implementation specification (source of truth for scope) |
| [Design](./design.md) | Owner-reviewed design (§9.0 decisions, §7.2 R3 carry-over review) |
| [Tasks](./tasks/TASK-INDEX.md) | Task breakdown and status |
| [Project CLAUDE.md](./CLAUDE.md) | Operating manual for AI sessions |

## Current Status

| Metric | Value |
|--------|-------|
| **Phase** | Development (not started; owner review of plan and tasks first) |
| **Progress** | 0% |
| **Target Date** | — |
| **Completed Date** | — |
| **Owner** | Spaarke product owner |

## Problem Statement

R3 left the external SPA with one record-detail modal that stacks sections and is read-only for events and to-dos. That is true even for callers who have write on the record, because two ViewOnly hard-codes override the per-record rights the BFF already returns. Matters and work assignments have no event POST route. Partners cannot see the invoice review state, the information fields they need, or document previews. A backlog of R3 and ebilling follow-ups (grids, messages, sign-in, workforce contacts, Teams mobile, binder faults) is open against the SPA and its BFF surface.

## Solution Summary

A `RecordTemplateConfig` per record type drives one modal on SprkModal `lg`: a fixed toolbar, header and optional tabs, with two scrolling columns. A shared `RecordCommandBar` and one icon map render each record's actions, hiding what the caller may not do and naming the caller's access. The BFF gains two event routes (copied from the project route), a ProblemDetails delete-409, allow-list columns under UAC review, 503s for retryable binder faults, and log hygiene. Phases P2–P4 add invoice review detail and files, preview, grid persistence, message threads, workforce-contact tabs, sign-in fixes, carry-over issues and Teams mobile.

## Graduation Criteria

The project is complete when:

- [ ] All six record types open in the template; R3's modal states and deep links still work (spec SC-1).
- [ ] A caller with write can upload, post, add a to-do and add an event on matters, projects and work assignments; a View-only caller sees none of these; no offered action answers 403 (SC-2).
- [ ] Every added field is on the allow-list with UAC review recorded; an unlisted column is still refused (SC-3).
- [ ] PDF and image documents ≤ 25 MB preview in place; others download (SC-4).
- [ ] Invoice Files and Review detail render as specified, including the must-not-fire test (SC-5, SC-6).
- [ ] The issues listed in spec SC-7 are closed by the described behaviour.
- [ ] Themes, focus and widths pass (SC-8); each phase deployed to dev with E2E, BFF deploys uac-r2-gated with 0 new 5xx (SC-9).
- [ ] All tests pass; `/test-diet` run at wrap-up.

## Scope

### In Scope

- P1: record template, `RecordCommandBar`, icon map, per-record access, matter/WA event routes, delete-409 ProblemDetails, Submit invoice action.
- P2: Review detail (082c), held-Accepted badge, invoice Files, inline preview, documents without a file, Documents on the shared DataGrid, shared EmptyState.
- P3: Information fields (allow-list + UAC review), grid persistence, Money sort/export, messages refresh/indicator, message threads, matter-event task type and assignee.
- P4: workforce-contact tabs, licensed first sign-in, email texts and checks, binder-fault 503 + SPA retry, test warm-up, contact-store log hygiene, the acme fault, Teams mobile.
- Closing: standards proposals and inherited first-E2E checks.

### Out of Scope

- Editing core record fields; the invoice submission wizard (ebilling); ebilling's BFF changes.
- Auth-plane changes beyond #2269's status code and #2192's first-sign-in path.
- Per-user cross-device grid settings.
- #2270/#2278 (→ unified-access-control-r2), #1840/#1737 (deferred), #2534 (→ spaarke-auth-system-of-record-r1).
- Reading `sprk_signal` from any external surface.

## Key Decisions

| Decision | Rationale | ADR |
|----------|-----------|-----|
| Shared `RecordCommandBar` in `@spaarke/ui-components` | Record action bar is a core cross-surface UX pattern (owner 2026-11-08) | ADR-012 (Path C) |
| Child dialogs mount inside the parent SprkModal | Same chrome family; focus test guards it | ADR-050 (Path C) |
| Staged delivery, one dev deploy + E2E per phase | Owner 2026-11-09 | — |
| Binder faults → 503 + SPA auto-retry 3× | Indeterminate access is never a 403 | ADR-028 |

## Risks & Mitigations

| Risk | Impact | Likelihood | Mitigation |
|------|--------|------------|------------|
| ebilling routes / 082c-2 deploy slip | Med | Med | P2 ships without FR-11/FR-13; they move to a later deploy |
| Shared BFF surface collisions (uac-r2, ebilling) | High | Med | `/conflict-check` before every BFF PR; deploys only via uac-r2 |
| ADR-012 review rejects shared `RecordCommandBar` | Low | Low | Fall back to SPA-local; owner told |
| Live-metadata check drops Information fields | Low | Med | Drop, never invent; owner told per type |

## Dependencies

| Dependency | Type | Status | Notes |
|------------|------|--------|-------|
| ebilling-r1 082c-2 deploy + `/invoices/{id}/files` routes | External | Pending | Gates FR-11, FR-13 |
| unified-access-control-r2 | External | Ready | Dev BFF deploys; UAC reviews FR-08/18/24/28/31 |
| spaarke-ontology-platform-r1 | External | Pending | `AssignTaskRecipientGate.cs` agreement; D-199 for #2445 |
| Dataset-grid framework owners | Internal | Pending | DataGrid persistence and Money options |

## Changelog

| Date | Version | Change | Author |
|------|---------|--------|--------|
| 2026-11-10 | 1.0 | Initialized from spec.md via `/project-pipeline` | Claude Code |
