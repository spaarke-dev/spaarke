# Record Modal Selection Pattern

> **Last Reviewed**: 2026-07-01 (R2 FR-16 sharpening: two-layout framing)
> **Status**: Current

## When
Use whenever a task opens a record, document, form, wizard, confirm, or preview **as a modal** from any Spaarke client surface (Code Pages, PCF, ribbon, SPAs, workspace widgets).

## Read These Files
1. `docs/standards/MODAL-DECISION-CRITERIA.md` — binding standard: two-layout framing (Layout 1 canonical / Layout 2 justified exception), TL;DR decision tree, anti-patterns, verbatim MS Learn 2025-05-07 quote
2. `src/client/shared/Spaarke.UI.Components/src/components/RecordNavigationModalShell/README.md` — Layout 2 shell reference: props, dirty-check protocol, origin allow-list
3. `src/client/shared/Spaarke.UI.Components/src/components/FilePreview/RichFilePreviewDialog.tsx` — Layout 2 reference case (document preview at `max-width: 1280px, height: 85vh`)

## Constraints
- **ADR-012** — shell components live in `@spaarke/ui-components`; do not duplicate per solution
- **ADR-021** — Fluent UI v9 exclusively; semantic tokens only
- **ADR-023** — `ChoiceDialog` is the only pattern for 2–4 rich choices
- **ADR-028** — never snapshot tokens in modal props; pass `authenticatedFetch` as function

## Key Rules
- **Layout 1 (canonical)** — entity record row-click → `Xrm.Navigation.navigateTo({pageType:"entityrecord", entityName, entityId, formId?}, {target:2, position:1, width:{value:85,unit:'%'}, height:{value:85,unit:'%'}})`. **85% × 85% for every entity — do NOT vary per-entity** (R2 FR-20 binding). The Spaarke DataGrid framework's `defaultRecordOpen` emits exactly this shape.
- **Layout 2 (justified exception)** — browse across records OR content-shaped surface (e.g., document preview) → `RecordNavigationModalShell` + proprietary Fluent v9 content. Dimensions are content-driven; **do NOT resize to Layout 1's 85% × 85%**. Reference case: `RichFilePreviewDialog`.
- **Do NOT iframe-embed OOB `main.aspx`** — Microsoft docs (2025-05-07) state: "Displaying a form within an IFrame embedded in another form is not supported". Retired in R2 FR-14.
- **Do NOT rebuild "1 of N + prev/next" chrome per surface** — compose `RecordNavigationModalShell`.
- **Do NOT launch OOB `navigateTo` from inside a Fluent v9 Dialog** — close the Fluent dialog first, then escalate.
