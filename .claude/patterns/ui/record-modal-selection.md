# Record Modal Selection Pattern

> **Last Reviewed**: 2026-10-07 (ADR-050 amendment D-26: BrowseModal, in-app launch rule) · prior 2026-07-01 (R2 FR-16)
> **Status**: Current

## When
Use whenever a task opens a record, document, form, wizard, confirm, or preview **as a modal** from any Spaarke client surface (Code Pages, PCF, ribbon, SPAs, workspace widgets).

> **Component layer**: once you've decided the family here, BUILD it with the canonical shell — see [`modal-shell.md`](modal-shell.md) → `SprkModal` + presets + [`docs/standards/MODAL-DESIGN-SYSTEM.md`](../../../docs/standards/MODAL-DESIGN-SYSTEM.md) (ADR-050). This file is the DECISION layer (which family); that is the COMPONENT layer (how to build it).

## Read These Files
1. `docs/standards/MODAL-DECISION-CRITERIA.md` — binding standard: two-layout framing (Layout 1 canonical / Layout 2 justified exception), TL;DR decision tree, anti-patterns, verbatim MS Learn 2025-05-07 quote
2. `src/client/shared/Spaarke.UI.Components/src/components/RecordNavigationModalShell/README.md` — the dirty-check protocol and origin allow-list wired through `BrowseModal.onBeforeNavigate`
3. `src/client/shared/Spaarke.UI.Components/src/components/FilePreview/RichFilePreviewDialog.tsx` — Layout 2 reference case (document preview at `max-width: 1280px, height: 85vh`)

## Constraints
- **ADR-012** — shell components live in `@spaarke/ui-components`; do not duplicate per solution
- **ADR-021** — Fluent UI v9 exclusively; semantic tokens only
- **ADR-023** — `ChoiceDialog` is the only pattern for 2–4 rich choices
- **ADR-028** — never snapshot tokens in modal props; pass `authenticatedFetch` as function

## Key Rules
- **Layout 1 (canonical)** — entity record row-click → `Xrm.Navigation.navigateTo({pageType:"entityrecord", entityName, entityId, formId?}, {target:2, position:1, width:{value:85,unit:'%'}, height:{value:85,unit:'%'}})`. **85% × 85% for every entity — do NOT vary per-entity** (R2 FR-20 binding). The Spaarke DataGrid framework's `defaultRecordOpen` emits exactly this shape.
- **Layout 2 (justified exception)** — browse across records OR content-shaped surface (e.g., document preview) → `BrowseModal` / `PreviewModal` (the `SprkModal` family) + proprietary Fluent v9 content. Dimensions are content-driven; **do NOT resize to Layout 1's 85% × 85%**. Reference case: `RichFilePreviewDialog`.
- **Do NOT iframe-embed OOB `main.aspx`** — Microsoft docs (2025-05-07) state: "Displaying a form within an IFrame embedded in another form is not supported". Retired in R2 FR-14.
- **Do NOT rebuild "1 of N + prev/next" chrome per surface** — use `BrowseModal` (`nav` + `onBeforeNavigate`); `RecordNavigationModalShell` is only the dirty-check protocol behind `onBeforeNavigate`, never a nested envelope (ADR-050, amended 2026-10-07).
- **Launch in-app from Spaarke React surfaces**; `navigateTo` only from hostless ribbon scripts (its white platform chrome can't be themed). Wizards use `WizardShell`; yes/no confirms use `ConfirmModal`.
- **Do NOT launch OOB `navigateTo` from inside a Fluent v9 Dialog** — close the Fluent dialog first, then escalate.
