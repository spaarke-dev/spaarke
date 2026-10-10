# Task Index — Spaarke External Access Platform R3

> **Status**: Initialized 2026-10-10 by `/project-pipeline`; execution not started.
> **Total**: 34 tasks (P0: 3 · P1: 3 · P2: 1 · P3: 10 · P4: 4 · P5: 7 · P6: 1 · P7: 4 · wrap-up: 1).
> **Source**: `spec.md` (FR-01–FR-26). Plan: `plan.md`. Operating manual: `CLAUDE.md`.

Legend: Tier S = sonnet, O = opus · Effort h = high, x = xhigh, m = medium · status tokens `[open]` `[wip]` `[done]` `[escalated]` `[blocked]` (the ASCII token is mandatory; FAILURE-MODES G-16).

| ID | Title | Phase | Status | Deps | Tier/Eff | Rigor | Parallel |
|----|-------|-------|--------|------|----------|-------|----------|
| 001 | ADR-028 amendment for R3 (A2, A3 :124, A1 :55) | P0 | 🔲 [open] | none | O/h | FULL | — (.claude/, main session; owner approval) |
| 002 | Grid columns per owner list (FR-09) | P0 | 🔲 [open] | none (owner list) | S/m | STANDARD | Wave 1 when the list arrives |
| 003 | Spike: Entra B2B self-service sign-up vs custom join (FR-12) | P0 | 🔲 [open] | none | O/h | STANDARD | Wave 1 |
| 010 | Ciam audience forms + default-scheme CIAM guard (FR-22, FR-23) | P1 | 🔲 [open] | none | O/h | FULL | Wave 1 |
| 011 | Modules by user type + partner service-request switch (FR-19, FR-06) | P1 | 🔲 [open] | 001 | O/h | FULL | Wave 2 |
| 012 | Member-test branch for registered guests (FR-15) | P1 | 🔲 [open] | 001 | O/h | FULL | Wave 2 |
| 020 | R3 schema: sprk_createdbycontact + intake columns (FR-25, FR-05) | P2 | 🔲 [open] | none | S/h | FULL | Wave 1 (owner-gated import) |
| 030 | Creator stamping on every external create (FR-25) | P3 | 🔲 [open] | 020 | O/h | FULL | Wave 2 |
| 031 | Document routes for Matter/WA/Invoice/Service Request (FR-24) | P3 | 🔲 [open] | 030 | O/h | FULL | Wave 3 |
| 032 | DELETE own documents/to-dos/events/service requests (FR-26) | P3 | 🔲 [open] | 031 | O/h | FULL | Wave 4 |
| 033 | External message read endpoint (FR-04) | P3 | 🔲 [open] | 011 | O/h | FULL | Wave 3 |
| 034 | External message send endpoint (FR-11) | P3 | 🔲 [open] | 030, 033 | O/h | FULL | Wave 4 |
| 035 | Intake submit endpoint (FR-05, FR-06) | P3 | 🔲 [open] | 011, 020, 033 | O/h | FULL | Wave 5 |
| 036 | Email on subsequent grant + message notify (FR-07) | P3 | 🔲 [open] | 034 + UAC-r2 PR #1583 merged | S/x | FULL | Wave 5 (if #1583 merged) |
| 037 | Notification feed + last-seen (FR-08) | P3 | 🔲 [open] | 034, 035 | O/h | FULL | Wave 6 |
| 038 | Deploy R3 BFF to dev + live probes | P3 | 🔲 [open] | 010, 011, 012, 032, 035, 036, 037, 039 | S/h | STANDARD | — (live deploy, owner-gated) |
| 039 | Event reads for Matter and Work Assignment (FR-03 server) | P3 | 🔲 [open] | 032, 037 | S/h | FULL | Wave 7 |
| 040 | Workforce client switch (FR-16) | P4 | 🔲 [open] | 001 (+ owner client registration) | O/h | FULL | Wave 2 |
| 041 | Run-time backend selection (FR-17) | P4 | 🔲 [open] | 040 | O/h | FULL | Wave 3 |
| 042 | Out-of-plane pages + no production mock (FR-18) | P4 | 🔲 [open] | 041 | O/h | FULL | Wave 4 |
| 043 | Production build + Teams package in CI (FR-20) | P4 | 🔲 [open] | 040 | S/h | STANDARD | Wave 3 |
| 050 | Record detail view: fields, docs/invoices, events/tasks (FR-01–03) | P5 | 🔲 [open] | none | S/h | FULL | Wave 1 |
| 051 | Detail messages section: read + send (FR-04/FR-11 client) | P5 | 🔲 [open] | 050, 034 | S/h | FULL | Wave 5 |
| 052 | Intake wizards: wiring + NDA + Policy Question (FR-05) | P5 | 🔲 [open] | 035 | S/h | FULL | Wave 6 |
| 053 | Intake wizards: Invention + Trademark (FR-05) | P5 | 🔲 [open] | 052 | S/h | FULL | Wave 7 |
| 054 | Detail documents + delete-own actions (FR-24/FR-26 client) | P5 | 🔲 [open] | 051, 032 | S/h | FULL | Wave 6 |
| 055 | Notification bell + deep-link record open (FR-08/FR-07 client) | P5 | 🔲 [open] | 054, 037, 041 | S/h | FULL | Wave 7 |
| 056 | Join page (FR-13), flagged until 060 | P5 | 🔲 [open] | 003, 041 | O/h | FULL | Wave 8 |
| 060 | Registration service: owner host decision, then build or R4 hand-off (FR-14) | P6 | 🔲 [open] | 003 + owner decision | O/h | FULL | — (blocked on owner) |
| 070 | Deploy SPA + dev Teams package to dev | P7 | 🔲 [open] | 038, 042, 043, 053, 055, 056 | S/h | STANDARD | — (live, owner-gated) |
| 071 | Teams live check with the test guest (FR-21) | P7 | 🔲 [open] | 070 | S/m | STANDARD | — (owner-driven) |
| 072 | Both-plane E2E on dev, success criteria 1–12 + FR-10 | P7 | 🔲 [open] | 002, 070, 071 | S/h | STANDARD | — (live) |
| 073 | Production deploy to external.spaarke.com (gated) | P7 | 🔲 [open] | 072 + T240c, T240d, DNS, owner go | S/h | STANDARD | — (blocked until gates) |
| 090 | Project wrap-up | Wrap-up | 🔲 [open] | all | S/h | FULL | — (serial) |

## Parallel Execution Plan

Max 6 agents per wave. Tasks that touch `.claude/` (001, and 090 if it promotes lessons) run in the main session only.

| Wave | Tasks | Prerequisite | Files touched (no overlap inside a wave) | goal-eligible |
|---|---|---|---|---|
| 1 | 003, 010, 020, 050 (+ 002 once the owner list arrives); 001 in the main session alongside | none | notes/spikes · AuthorizationModule.cs · Dataverse solution · external-spa detail view · grid config | NO (auth, schema, owner gates) |
| 2 | 011, 012, 030, 040 | 001 (011, 012, 040); 020 (030) | ModuleEntitlementResolver/ExternalAccessModule/options · WorkforceIdentityOptions/binder/resolver · ExternalDataService + ExternalProjectDataEndpoints · external-spa auth | NO (auth) |
| 3 | 031, 033, 041, 043 | 030; 011; 040; 040 | ExternalProjectDataEndpoints + ExternalDataService · new ExternalMessageEndpoints + ExternalAccessEndpoints · SPA auth/backend-selection · workflows + prod manifest | NO (auth) |
| 4 | 032, 034, 042 | 031; 030 + 033; 041 | ExternalProjectDataEndpoints + ExternalDataService · ExternalMessageEndpoints + CommunicationService · SPA pages | NO (auth) |
| 5 | 035, 036, 051 | 011 + 020 + 033; 034 + #1583; 050 + 034 | new ExternalIntakeEndpoints + ExternalAccessEndpoints + ExternalDataService · GrantAccessNotifier + ExternalMessageEndpoints · SPA RecordMessages | NO (auth) |
| 6 | 037, 052, 054 | 034 + 035; 035; 051 + 032 | new ExternalNotificationFeedEndpoints + ExternalAccessEndpoints + ExternalDataService · SPA intake + QuickStartPane · SPA detail + DocumentLibrary | NO (auth) |
| 7 | 039, 053, 055 | 032 + 037; 052; 054 + 037 + 041 | ExternalProjectDataEndpoints + ExternalDataService · SPA intake · SPA shell + msal-auth + App.tsx | NO (auth) |
| 8 | 038, 056 | Wave 7 + 010/012/036; 003 + 041 | live BFF deploy · SPA join page + App.tsx | NO (deploy, auth) |
| 9 | 070 → 071 → 072 → 073 → 090 | sequential | live deploys and verification | NO (deploy, live) |
| — | 060 | owner decision | registration service or R4 hand-off | NO (blocked) |

**No wave is `/goal`-eligible.** Every wave includes auth or security-sensitive work, a live deploy, or an owner gate (task-create Step 3.85).

## Critical Path

```
001 → 011 → 033 → 034 → 035 → 037 → 039 → 038 → 070 → 071 → 072 → 073 → 090
001 → 040 → 041 → 042 ──────────────────────────────┘
050 → 051 → 054 → 055 ──────────────────────────────┘
020 → 030 → 031 → 032 ┘
```

## Shared-file chains (why some tasks are sequential)

| File | Order |
|---|---|
| `Api/ExternalAccess/ExternalProjectDataEndpoints.cs` | 030 → 031 → 032 → 039 |
| `Infrastructure/ExternalAccess/ExternalDataService.cs` | 030 → 031 → 032 → 035 → 037 → 039 |
| `Api/ExternalAccess/ExternalAccessEndpoints.cs` (route registration) | 033 → 035 → 037 |
| `Api/ExternalAccess/ExternalMessageEndpoints.cs` | 033 → 034 → 036 |
| `external-spa/src/components/detail/RecordDetailModal.tsx` | 050 → 051 → 054 → 055 |
| `external-spa/src/components/shell/QuickStartPane.tsx` | 052 → 053 |
| `external-spa/src/auth/*`, `App.tsx` | 040 → 041 → 042 → 055 → 056 |

## High-risk and coordination

- **010, 012, 032, 033, 034, 037:** security guards. Each needs a seeding proof and two adversarial passes for auth/tenant isolation (root CLAUDE.md §8.5).
- **036:** waits for UAC-r2 PR #1583 (`GrantAccessNotifier`).
- **Shared surfaces:**
  - `/conflict-check` before every BFF PR (UAC-r2 PRs #1583 and #1586 touch the same files);
  - PR #1494 touches `Services/Communication`;
  - dependabot PR #909 touches both SPA workflows.
- **Owner gates:** 001 (ADR edit), 002 (column list), 020 (schema import), 040 (client registration), 060 (host decision), and every deploy (038, 070, 073).
- **060:** if the owner does not decide in time, FR-14 moves to R4 with a written path and 056 stays flagged off.

## How to execute

1. Run `dotnet build src/server/api/Sprk.Bff.Api/` before any BFF wave; build checks between waves per `project-pipeline` Step 5.
2. Run `task-execute` per task. For a parallel wave, send one message with several task-execute agents (≤ 6), each with its POML's `<model-tier>` and `<effort>`.
3. 001 is main-session-only.
