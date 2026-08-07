# Task 056 — Task/deadline reconcile tab (Job C, FR-E5) — COMPLETE (2026-08-07)

**Rigor**: FULL · frontend (Spaarke.Communication.Components) · done by Opus session. **Depends on 034, 052, 053, 056b.**
**Result**: shared-lib `tsc` build 0 errors; package jest **30 suites / 219 tests green** (13 new for 056); Step 9.5 + /conflict-check clean.

## What shipped
`ReconcileTabs/TaskReconcileTab.tsx`:
- **`TaskReconcileTab`** — the Tasks reconcile tab. For the CONFIRMED record, fetches `queue-feed?regarding=` (injected `authenticatedFetch`), filters to this communication's `kind:"create-task"` items, and renders each as an **editable 8-field task form** (name · description · base/due/final/completed dates · status Choice · assigned-to) with **Accept / Reject / Hold**, plus a **"+ New task"** ad-hoc form.
- **`TaskReconcileModal`** — the ADR-050 `FormModal` wrapper for the email-record-form mount (mirrors 055's `FieldUpdateReconcileModal`).

### Accept routing (the 034 / 056b / 055b contract)
The 034 apply request (`ApplyCreateTaskRequest`) carries only the FR-E5 fields (+ a `dueDate` override) — **NOT** subject/description (those come from the extraction). So editing the name/description of a proposal has nowhere to land via 034. The tab routes intelligently:
- **Proposal, name+description UNCHANGED** → `POST /proposals/{reviewLogId}/create-task/apply` (034) with the FR-E5 fields. The apply closes the proposal. **Create-and-complete** = set status=Completed + completed date inline (034 PATCHes them on the created task in the SAME audited apply).
- **Proposal, name OR description EDITED** → `POST /{communicationId}/create-task` (056b ad-hoc) with the full edited form, THEN best-effort `POST /proposals/{reviewLogId}/dismiss` (055b) on the original. **Create-first** so a dismiss failure leaves the task created + the proposal re-appearing (no data loss; no dropped edits).
- **"+ New task"** (ad-hoc) → `POST /{communicationId}/create-task` (056b). No reviewLogId, no dismiss.
- **Reject** → `POST /proposals/{reviewLogId}/dismiss` (055b). **Hold** → NO API call (leave Proposed).

### ADR-015 (no auto-finalize) + NFR-10
- A task is created ONLY on an explicit Accept — the tab never auto-POSTs (asserted). Nothing deadline-bearing finalizes without confirmation.
- No `regarding` ⇒ gate ("confirm the related record first"), no fetch. The list re-scopes (re-fetches) when the confirmed record changes; an ad-hoc task always attaches to the confirmed record (556b requires it, 422 otherwise).

### Dual-use (ADR-022) + citation (054)
Inline cards for the browse-shell right pane (citation click → visible reader highlight, via `onCitationClick` → the shell's `activeCitation`); FormModal for the email form. Same pattern as 055.

## §6.5 / directional-mode deviations (documented)
1. **Inline cards vs FormModal** — same as 055: the browse-pane mount is inline (citation must highlight the reader beside it); the FormModal wrapper serves the form mount. ADR-050 satisfied where a modal is the surface.
2. **Edited-proposal → ad-hoc + dismiss routing** — because 034's apply body has no subject/description, an edited-identity proposal is routed through the ad-hoc endpoint (056b) + a dismiss (055b) rather than silently dropping the edit. All through blessed endpoints; loses nothing.
3. **Assigned-to** rendered as a user-id text input (not a full people-picker) — a richer lookup is a host concern (the shared lib is data-source-agnostic, ADR-012); the field is present + editable per the AC. Notable as an enhancement seam, not a gap.

## Tests (13 new — `ReconcileTabs/__tests__/TaskReconcileTab.test.tsx`)
NFR-10 gate (no fetch); editable form with all 8 fields; **create-and-complete** (status=2 + completed date → 034 apply body); unchanged proposal → 034 apply (not ad-hoc); **edited-name proposal → 056b ad-hoc create + 055b dismiss** (subject/regarding asserted); **"+ New task" → 056b ad-hoc**; ADR-015 no-auto-finalize (render fires no POST); Reject → dismiss; Hold → no write; re-scope on override; feed filtered to this communication's create-task items; citation → onCitationClick; FormModal mount + dark-mode (ADR-021) no console errors.

## Step 9.5 / hygiene
- code-review + adr-check (self): ADR-012 (context-agnostic; injected `authenticatedFetch`), ADR-015 (no auto-finalize), ADR-021 (Fluent tokens; dark-mode tested), ADR-022 (`React.FC`+hooks; deep-imports), ADR-050 (FormModal), ADR-028 (no raw auth). No AI smells. §11: no task-lifecycle reconcile surface existed (POML justification); reuses FormModal + the 055 patterns (`ReconcileRegarding`/`ProposalOutcome` shared).
- No BFF files (034/056b/055b carried the backend) → no §10 for the frontend.
- /conflict-check clean (ReconcileTabs/ net-new; no overlap with the 22 open PRs).

## Coordination (task 058) — COORD-058-01 updated
Added the 056b endpoint + the `TaskReconcileTab`/`TaskReconcileModal` exports + the Accept-routing contract (so r5 does NOT re-implement the edited-proposal→ad-hoc+dismiss logic).

## Remaining Pillar E
057 (routing category→team; dep 050), 058 (r5 coordination contract), 059 (deploy — update `NEEDS_REVIEW_CONFIG_ID`).
