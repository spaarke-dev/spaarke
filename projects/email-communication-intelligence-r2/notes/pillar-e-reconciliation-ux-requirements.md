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

*Update this doc when UX requirements change. The production build (`pillar-e-mount-build-plan.md`) and any prototype refinement both trace to the items above.*
