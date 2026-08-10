# Task 055 — Field-update reconcile tab (Job B, FR-E4 + NFR-10) — COMPLETE (2026-08-07)

**Rigor**: FULL · frontend (Spaarke.Communication.Components) · Sonnet-tier work done by Opus session. **Depends on 052, 053, 055a, 055b.**
**Result**: shared-lib `tsc` build 0 errors; package jest **29 suites / 206 tests green** (11 new for 055); Step 9.5 + /conflict-check clean.

## What shipped
`src/client/shared/Spaarke.Communication.Components/src/components/ReconcileTabs/`:
- **`FieldUpdateReconcileTab.tsx`** — the Fields reconcile tab. For the CONFIRMED record only, fetches `GET /api/communications/queue-feed?regarding={entityType}:{guid}` (via injected `authenticatedFetch`, mirroring `EmailBodyView`), filters to THIS communication's `pending-proposal` items, and renders one editable card per proposal: `sprk_targetfield` · current `oldValue` → **editable** matched `newValue` (`Input`) · confidence badge · clickable citation · reason · **Accept / Reject / Hold**.
- **`FieldUpdateReconcileModal`** (same file) — wraps the tab body in the ADR-050 `FormModal` preset for the email-record-form dual-use mount.
- `ReconcileTabs/index.ts` barrel + wired into `components/index.ts`.

### Decision map (spec FR-E4 / project constraint)
- **Accept** → `POST /api/communications/proposals/{reviewLogId}/apply` body `{ overrideValue: <edited-or-matched value> }` (055a). The edited value always flows as `overrideValue`; the server treats override==stored as a plain `Applied` (so no client-side "did it change" branching). Row leaves Proposed; `onProposalResolved(id,'applied')`.
- **Reject** → `POST /api/communications/proposals/{reviewLogId}/dismiss` (055b). Terminal-dismiss; row leaves Proposed; `onProposalResolved(id,'rejected')`.
- **Hold** → **NO API call** ("leave Proposed"). Client-only skip from the current review; the proposal deliberately reappears on the next queue-feed load; `onProposalResolved(id,'held')`.

### NFR-10 gating + re-scope
- No `regarding` (unconfirmed association) ⇒ the tab renders "Confirm the related record first" and **fetches nothing** — no proposal is actionable.
- The fetch is keyed strictly on the confirmed scope `{entityType}:{recordId}`. When the association is **overridden** (the host passes a new `regarding`), the list **re-fetches for the new record** and the old record's proposals are dropped — a proposal is never applied against an unconfirmed/overridden record (the apply/dismiss endpoints also re-gate server-side; this is the UI half of the invariant).

### Dual-use (ADR-022 / AC5)
- **Browse-shell right pane (task 053)**: `FieldUpdateReconcileTab` renders its cards INLINE (not a modal), so a citation click highlights the VISIBLE left reader. Wired via the shell's `renderTabs` slot; the host lifts `onCitationClick` → the shell's `activeCitation` (task 054). Proven end-to-end by `FieldUpdateReconcileTab.browse-mount.test.tsx` (the 053+054+055 seam: proposal in the right pane → citation click → `citation-highlight-mark` in the left reader).
- **Email record form**: `FieldUpdateReconcileModal` (FormModal). Satisfies AC1 "in a FormModal" + ADR-050.

## §6.5 / directional-mode deviation (documented)
The POML said "Build the tab as a `SprkModal` `FormModal` preset." A single FormModal can't be the browse-pane presentation: in the two-pane browse shell the reconcile UI sits in the RIGHT pane beside the reader, and a citation click must highlight the reader — a modal over the reader would cover it. So the tab renders **inline cards** for the browse-pane mount, and a **`FieldUpdateReconcileModal` FormModal wrapper** provides the form-mount presentation. FormModal (ADR-050) is used exactly where a modal is the surface (the form, which has no reader pane). This mirrors the 053 SprkModal-vs-BrowseModal-preset deviation and keeps ADR-050 satisfied. Both are exported from the POML's single named output file.

## Tests (11 new — `ReconcileTabs/__tests__/`)
`FieldUpdateReconcileTab.test.tsx` (10): NFR-10 gate (no fetch); current→matched + editable + citation + confidence; Accept POSTs `/apply {overrideValue: edited}`; Reject POSTs `/dismiss`; Hold = no write; **re-scope on override** (re-fetch, list swap); feed filtered to this communication's pending-proposal items; citation click → `onCitationClick`; empty state; FormModal mount + dark-mode (ADR-021) no console errors.
`FieldUpdateReconcileTab.browse-mount.test.tsx` (1): the 053+054+055 seam (right-pane mount → citation → reader highlight).

## Step 9.5 / hygiene
- code-review (self): ADR-012 (context-agnostic; fetches a KNOWN BFF endpoint via injected `authenticatedFetch` — the `EmailBodyView` precedent), ADR-021 (Fluent v9 tokens; dark-mode tested), ADR-022 (`React.FC` + hooks; deep-imports `../../logic/citations`), ADR-050 (FormModal preset; no hand-rolled dialog), ADR-028 (no raw auth headers / accessToken props). No AI smells. `authenticatedFetch` in the `load` effect deps matches the `EmailBodyView` contract (hosts pass a stable ref / omit it → the stable `@spaarke/auth` default).
- §11: no existing Fields reconcile tab; reuses FormModal + citations logic + EmailBody types (no fork). `FieldUpdateReconcileModal` is a thin form-mount wrapper.
- No BFF files in this task (055a/055b carried the backend) → no §10 for the frontend.
- /conflict-check clean (ReconcileTabs/ is net-new; no overlap with any of the 22 open PRs, incl. the r5-owned Spaarke.Communication.Components surface).

## Coordination owed (task 058 — r5 BINDING contract)
r5's code page must wire the new exports: mount `FieldUpdateReconcileTab` in the browse-shell `renderTabs` slot (lifting `onCitationClick` → `activeCitation`) AND `FieldUpdateReconcileModal` on the email record form. Plus the already-owed 052 `onCreateNewRecord` extension + 055a apply-override + 055b dismiss endpoints. The host supplies `regarding` (the confirmed association from task 052's `onConfirmed` handshake) and re-supplies it on override.
