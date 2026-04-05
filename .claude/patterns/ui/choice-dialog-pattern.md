# Choice Dialog Pattern

> **Last Reviewed**: 2026-04-05
> **Reviewed By**: ai-procedure-refactoring-r2
> **Status**: Verified

## When
Use for dialogs presenting 2-4 mutually exclusive options where each needs an icon, title, and description. Do NOT use for simple yes/no confirmations (use `ConfirmDialog`) or 5+ options (use `Select`/`Combobox`).

## Read These Files
1. `src/client/shared/Spaarke.UI.Components/src/components/ChoiceDialog/ChoiceDialog.tsx` — component implementation
2. `src/client/shared/Spaarke.UI.Components/src/components/ChoiceDialog/index.ts` — exports: `ChoiceDialog`, `IChoiceDialogOption`
3. `docs/adr/ADR-023-choice-dialog-pattern.md` — full design specs, accessibility requirements, alternatives analysis

## Constraints
- **ADR-021**: Use semantic Fluent v9 color tokens (`colorBrandForeground1` for icons); no hard-coded colors

## Key Rules
- Import: `import { ChoiceDialog, IChoiceDialogOption } from "@spaarke/ui-components"`
- Each option requires: `id`, `icon` (24px Fluent icon), `title` (semibold), `description`
- Stack options vertically — never horizontal
- Use `Button appearance="outline"` for option buttons
- Provide Cancel in `DialogActions`; no auto-selection — force conscious choice
- Max 4 options per dialog
