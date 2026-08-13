# Fix #6 — make Reconciliation a system widget (like Matter/Project/Calendar) — ROOT CAUSE + PLAN

> Operator clarified 2026-08-13: "this should be a system widget — like Matter, Project, Calendar" · "not Quick Start; and not [the build-your-own] workspace layout". DEFINITIVE mechanism found.

## The mechanism (authoritative — `docs/architecture/SPAARKEAI-DASHBOARD-AND-WIDGET-MODEL.md` §3.1)
The SpaarkeAi **workspace dropdown = "the library"** lists **`sprk_workspacelayout` rows** (`GET /api/workspace/layouts`), **NOT** the `WorkspaceWidgetRegistry`. *"Calendar / Daily Briefing appear in the dropdown because they have layout rows, not because of any registry entry."* Each single-widget system layout (`sprk_issystem=true`, `single-column`) mounts ONE **LegalWorkspace section** by id via its `sprk_sectionsjson` `rows[].sections[]`.

Confirmed live in dev (`sprk_workspacelayout`): rows exist for Calendar (`sections:["calendar"]`), Email (`["email"]`), Messages (`["communications"]`), Projects, Invoices, Documents, Work Assignments, Analysis, Compose (`["compose-editor"]`), Daily Briefing … **but NO "Reconciliation" row.** And `sectionMetadataCatalog.ts` has a section id for every one of those (`calendar`, `email`, `communications`, `matters`, …) **but no `reconciliation` section.** The `communications-reconciliation` **direct-widget registration is real but irrelevant** to the dropdown (registry ≠ dropdown).

## The fix — mirror the `email` section exactly (Pattern D dual-use)
`email` was added by email-communication-solution-r5 task 041 as the direct analog. Do the same for reconciliation:

1. **`src/solutions/LegalWorkspace/src/sections/reconciliation.registration.ts`** (NEW) — mirror `email.registration.ts`: a `ReconciliationSectionMount` React.FC that resolves Xrm adapters (`XrmDataverseClient`, `getXrm().WebApi`, `authenticatedFetch`) + the reconciliation wiring (`resolveReview` via the ADR-024 `EmailConnectionsReview` write path, `resolveRegarding` via `derivePrimaryReview`, `views: RECONCILIATION_VIEWS`) and renders `ReconciliationWorkspace` inside the bounded `calc(100vh - 200px)` scroll host. Export `reconciliationRegistration: SectionRegistration` (id `reconciliation`, label "Reconciliation", icon `TaskListSquareLtrRegular`, category `data`, defaultHeight 720px).
   - **§11 reuse:** extract `buildResolveReview` + `resolveRegarding` from `CommunicationReconciliation/src/main.tsx` into `@spaarke/communication-components` (e.g. `reconciliationResolvers.ts`) so the code page, this section, AND the SpaarkeAi widget share ONE wiring (no divergence). main.tsx switches to the shared helpers.
2. **`src/solutions/LegalWorkspace/src/sectionRegistry.ts`** — import + register `reconciliationRegistration`.
3. **`sectionMetadataCatalog.ts`** (`@spaarke/ui-components/WorkspaceShell`) — add the `reconciliation` metadata entry (mirrors `email`; the dev-mode drift guard requires catalog + registration parity).
4. **Seed a `sprk_workspacelayout` row "Reconciliation"** — `sprk_issystem=true`, `single-column`, `sprk_sortorder` ~7 (after Messages/Email), `sprk_sectionsjson = {"scope":"my","schemaVersion":1,"rows":[{"id":"row-1","columns":"1fr","columnsSmall":"1fr","sections":["reconciliation"]},{…3 empty rows…}]}`. (New idempotent seed script.)
5. **Deploy:** SpaarkeAi/LegalWorkspace bundle redeploy (the section is code) + the layout-row seed (data). The dropdown then shows "Reconciliation" as a system widget.

## Contention
`sectionRegistry.ts` + `sectionMetadataCatalog.ts` are shared with email-communication-solution-r5 (owns the `email`/`communications` sections). r5 is CLOSED (on master, not diverged on these files — verified) → edit directly, but `/conflict-check` before the PR.
