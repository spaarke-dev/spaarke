# Record Modal Selection Pattern

> **Last Reviewed**: 2026-07-01
> **Status**: Current

## When
Use whenever a task asks you to open a record, document, form, wizard, confirm, or preview **as a modal** from any Spaarke client surface. Applies to Code Pages, PCF controls, ribbon commands, SPAs, and workspace widgets. Do NOT use for non-modal panels, inline forms, or side-drawers.

## Read These Files
1. `docs/standards/MODAL-DECISION-CRITERIA.md` — the binding standard: 3 modal families (OOB `navigateTo` / Proprietary Fluent v9 / Proprietary + `RecordNavigationModalShell`), TL;DR decision tree, 5 dimensions, worked examples, anti-patterns
2. `src/client/shared/Spaarke.UI.Components/src/components/RecordNavigationModalShell/README.md` — authoritative component reference for the browse shell: props, cross-frame dirty-check protocol, iframe-side contract, origin allow-list
3. `src/client/shared/Spaarke.UI.Components/src/components/FilePreview/RichFilePreviewDialog.tsx` — canonical Family 3 consumer (composes the shell when nav props supplied)
4. `src/client/shared/Spaarke.UI.Components/src/components/ChoiceDialog/ChoiceDialog.tsx` — Family 2 canonical (ADR-023 pattern; 2–4 rich choices)

## Constraints
- **ADR-012**: Shell components live in `@spaarke/ui-components`, not duplicated per solution
- **ADR-021**: Fluent UI v9 exclusively; no v8; semantic tokens only
- **ADR-023**: `ChoiceDialog` is the ONLY family-2 pattern for 2–4 rich options; do not rebuild
- **ADR-028**: Never snapshot tokens in modal props; pass `authenticatedFetch` as function dependency

## Key Rules
- **Full OOB main form edit needed?** → Family 1 (`Xrm.Navigation.navigateTo` with `target: 2`). No browse.
- **Browse across a collection?** → Family 3 (proprietary Fluent v9 Dialog + `RecordNavigationModalShell`). Mandatory when user pages through records.
- **Single preview / confirm / picker?** → Family 2 (proprietary Fluent v9 Dialog: `RichFilePreviewDialog`, `ChoiceDialog`, or bespoke).
- **Hybrid (browse + escalate to full edit)?** → Family 3 with "Open full form" button in shell `actionBar` that calls `navigateTo` on click.
- **Do NOT** iframe-embed OOB `main.aspx` as a standard pattern — Microsoft does not officially support it (see MODAL-DECISION-CRITERIA anti-pattern #4).
- **Do NOT** rebuild the "1 of N + prev/next" chrome per surface — compose `RecordNavigationModalShell`.
- **Do NOT** launch OOB `navigateTo` from inside a Fluent v9 dialog — close the Fluent dialog first, then escalate.
