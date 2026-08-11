# Pillar E — Email Reconciliation Surface · UI/UX Requirements (authoritative)

> **Status**: Living reference. Source of truth for the reconciliation UI/UX.
> **Created**: 2026-08-10 (consolidates the prototype `App.tsx` v4 feedback list + owner requirements 2026-08-10).
> **Prototype**: `spaarke-prototype/projects/email-communication-intelligence-r2-uat` (visual reference only — mock data).
> **Production components**: `@spaarke/communication-components` — `ReconciliationGrid`, `ReconciliationBrowseShell`, `FieldUpdateReconcileTab`, `TaskReconcileTab` (tasks 050–058).
> **Governing standards**: ADR-021 (Fluent v9), ADR-050 + `docs/standards/MODAL-DESIGN-SYSTEM.md` (modal shell), ADR-012 (shared components), NFR-10 (association-gates-proposals), NFR-11 (one reader / exact citations).

Historically the only capture of these requirements was the comment header of the prototype's `App.tsx`. This document promotes that to a first-class, reviewable artifact and folds in the 2026-08-10 owner requirements.

---

## A. Layout & shell requirements (owner, 2026-08-10)

Each maps to an existing shared component (§11 reuse — no new invention).

| # | Requirement | How it's satisfied | Status |
|---|---|---|---|
| **A1** | **Use our standard modal components** — no bespoke Fluent `Dialog`. | Browse shell = `BrowseModal`/`SprkModal` preset; overlays (email/attachment preview, +New task) = `PreviewModal`/`FormModal` presets. All from `@spaarke/ui-components` `SprkModal/presets`. | Prototype uses raw `Dialog` → **change**; production browse shell must use `SprkModal`. |
| **A2** | **Use standard shared UI components** — no inline re-implementation. | Compose the production `ReconciliationGrid` + `ReconciliationBrowseShell` + `FieldUpdateReconcileTab` + `TaskReconcileTab`; do NOT re-build the inline mock versions. | Production components exist; **assembly pending**. |
| **A3** | **Fluent UI design compliant** — v9 semantic tokens, light/dark. | ADR-021: `tokens.*` only, `FluentProvider`, no hard-coded colors. Production components already conform; the host page must wrap in a theme-aware `FluentProvider`. | Components conform; **host wiring pending**. |
| **A4** | **Larger modal** — email + attachments need more real estate. | `SprkModal` `size="xl"` (or `"full"`) per the `MODAL-DESIGN-SYSTEM.md` 7-size scale. The reader (email + document pages) drives the height/width need. | Prototype ≈ `94vw/1300px` fixed → **change** to the `xl`/`full` preset size. |
| **A5** | **Modern thin scroll bar** — in the reader and tab panels. | Reuse `SprkModal/ModalScrollArea.tsx` (the shared thin-scroll convention already used across `ConversationView`, `ThreadList`, `DataGrid`, `SprkChat`). No new CSS. | Prototype uses default scrollbars → **change**. |
| **A6** | **50/50 left/right split with horizontal manual drag-resize.** | Reuse `PanelSplitter/PanelSplitter.tsx` (+ `useThreadPaneLayout` pattern) — default 50/50, draggable vertical handle, min-width clamps. | Prototype is fixed `1.2fr / 1fr`, no resize → **change** to `PanelSplitter` at 50/50. |

**A-items 4–6 are the genuinely new UI refinements**; A1–A3 restate the "production swaps" the prototype header already anticipated.

---

## B. Interaction requirements (prototype v4 feedback — owner, 2026-08-05)

Carried verbatim from `App.tsx`; each is realized by a production component.

| # | Requirement | Realized by |
|---|---|---|
| B1 | Drop the confusing "normalized reader" tag | `ReconciliationBrowseShell` reader (053) |
| B2 | Reader looks like an EMAIL (address block → subject → body) | Browse shell reader (053) |
| B3 | Attachment looks like a DOCUMENT (page card) | Browse shell reader (053) |
| B4 | "Open original" .eml + file links → overlay preview | `PreviewModal` preset (A1) |
| B5 | More breathing room in field cards | `FieldUpdateReconcileTab` styling (055) |
| B6 | Fields manually editable (override the matched value) | `FieldUpdateReconcileTab` → apply `{overrideValue}` (055/055a) |
| B7 | Tasks add Status + Completed date (create AND complete in one session) | `TaskReconcileTab` (056) |
| B8 | Tasks have an editable Description | `TaskReconcileTab` (056) |
| B9 | Assigned-to is a lookup (dropdown) | `TaskReconcileTab` (056) |
| B10 | "+ New task" → overlay create-task form (ad hoc) | `TaskReconcileTab` + ad-hoc endpoint (056/056b) |
| B11 | "Save & confirm" → explicit saved confirmation | Browse shell footer + toast |
| B12 | "Undo changes" before save (un-associate, leave on list) | Browse shell footer |
| B13 | Partial reconciliation stays on the list with a "what's left" indicator | `ReconciliationGrid` `sprk_regardingrecordname` renderer "Needs:" hint (050/052) |

---

## C. Behavioral invariants (binding)

| Invariant | Rule |
|---|---|
| **NFR-10** | Field/Task proposals are actionable ONLY after a Related-to record is confirmed; re-scope on override. (Prototype `GateMsg`.) |
| **NFR-11** | ONE normalized reader over email body + attachment text; citations resolve via Compose `CitationResolver` (no second citation mechanism). |
| **Apply routing** | Field Accept → apply `{overrideValue}` (055a); Reject → dismiss (055b); Task unchanged → 034 apply; Task edited-identity → ad-hoc (056b) + dismiss (055b); +New task → 056b. |
| **Routing (057)** | Category→team `ownerid` on the additive triage update; per-team filtered grid view via `membershipFilter`. |

---

## D. Material deltas: prototype → production (review callouts)

What the owner will see change between the :5176 prototype and the shipped page:

1. **Per-team routing views (057)** — *net-new, not in the prototype.* A per-team filtered grid dimension.
2. **Real reader + `CitationResolver` (053/054)** — replaces mock `docFor()` proposals.
3. **`SprkModal`/`BrowseModal` shell at `xl`/`full` + `PanelSplitter` 50/50 + thin scroll (A1/A4/A5/A6)** — replaces the raw fixed `Dialog`.
4. **`EmailConnectionsReview` for Related-to (052)** — replaces the inline candidate list; single write path + `onCreateNewRecord` tile.
5. **Real apply/dismiss/ad-hoc endpoint routing** — replaces the prototype's local acc/rej/hold state.

---

## E. UAT round-2 (owner, 2026-08-11)

Reviewed against the refined prototype (`localhost:5177`). "P" = mock in the prototype for review; "B" = production build requirement.

### E.1 Related-to tab
| # | Requirement | P | B |
|---|---|---|---|
| E1a | Rename the `Create new & link` button → **`New record`**. | ✅ | ✅ |
| E1b | `New record` must have a real action: open the **Quick Start modal** → user selects a wizard → the wizard's FINAL step creates the new record → that new record is **added to the Related-to candidate list** → the user then uses the Related-to **`Confirm`** to associate it. This is **modal-on-modal**; when the record/wizard modal closes, the review (browse-shell) modal stays open. | ✅ mock the flow | ✅ real Quick Start wizard integration + add-created-record-to-candidates |

### E.2 Fields tab
| # | Requirement | P | B |
|---|---|---|---|
| E2a | Rename the `Accept & write` button → **`Accept`**. | ✅ | ✅ |
| E2b | The editable field control must match the **field's real type** — date fields use a date picker; **lookup fields use the OOB advanced-lookup side pane**; option-sets use a dropdown; etc. | partial (date pickers; note lookups) | ✅ type-correct controls incl. OOB advanced-lookup side pane |
| E2c | Add a full-width **`Update other fields`** button at the bottom of the Fields tab → opens the **confirmed Related-to record's form** so the user can edit other fields on that record. **Modal-on-modal**; review modal stays open on close. | ✅ mock a record-form modal | ✅ open the real record form (OOB `navigateTo` form or a record modal) for the confirmed regarding |

### E.3 Tasks tab
| # | Requirement | P | B |
|---|---|---|---|
| E3a | Rename the proposal `Confirm & create` button → **`Create`**. | ✅ | ✅ |
| E3b | In the New-task modal, **`Assigned to`** uses the standard **OOB advanced-find side-pane lookup** (systemuser/team). **Build note only — do NOT mock in the prototype.** | — (note only) | ✅ OOB advanced-lookup side pane for Assigned-to |

### Cross-cutting note — modal-on-modal
E1b + E2c both stack a record/wizard modal ON the open review modal. This is an established pattern here (the browse shell already opens a `PreviewModal` overlay for "Open original", and "+ New task" now uses a `FormModal`). Production uses `SprkModal`-family surfaces; on close, the underlying review modal remains open (controlled `open` state per surface).

### Production follow-up
E1b (Quick Start integration), E2b/E3b (OOB advanced-lookup side pane), and E2c (record-form modal) are **new build work** beyond the 061/062 mount — track as follow-on tasks (063+) after prototype sign-off. Label changes (E1a/E2a/E3a) are trivial and can ship into the 055/056/052 components directly.

### REUSE existing components (BINDING §11 — owner 2026-08-11)
These behaviors MUST reuse the shipped components, not rebuild them — full table in [`pillar-e-mount-build-plan.md` §7.5](pillar-e-mount-build-plan.md). Key targets: **Quick Start** = `QuickStartModal` (`src/solutions/SpaarkeAi/src/components/conversation/QuickStartModal.tsx`); **wizards** = the `Create*Wizard` code pages launched via the Assistant surface-launch mechanism; **"Update other fields"** = OOB `navigateTo` record form / `RecordNavigationModalShell`; **modal chrome** = `SprkModal` presets (`FormModal`/`WizardModal`/`BrowseModal`/`PreviewModal`); **lookups/Assigned-to** = OOB advanced-lookup side pane. The prototype's stand-ins are for review only; carried into the 063+ POMLs as explicit reuse `<constraint>`s.

### Button-label consistency (E-cross, owner 2026-08-11)
The action buttons that open a create/add surface use the **`+ {verb}`** pattern (Add icon + text), consistent with **`+ New task`**: **`+ New record`** (Related-to) and **`+ Update other fields`** (Fields).

---

*Update this doc when UX requirements change. The production build (`pillar-e-mount-build-plan.md`) and any prototype refinement both trace to the items above.*
