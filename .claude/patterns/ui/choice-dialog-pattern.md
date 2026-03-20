# Pattern: Choice Dialog (Rich Option Buttons)

> **Component**: `ChoiceDialog` from `@spaarke/ui-components`
> **Previously**: ADR-023 (demoted — UI pattern, not architectural decision)

---

## When to Use

Use for dialogs presenting **2-4 mutually exclusive options** where each option needs explanation.

| Use Case | Example |
|----------|---------|
| Resume vs Start Fresh | "Resume previous session?" with history count |
| Export Format | "Export as PDF, DOCX, or Email?" |
| Destructive Actions | "Delete permanently or move to trash?" |
| Mode Selection | "Create new or open existing?" |

**Don't use** for simple yes/no confirmations — use standard `ConfirmDialog`.
**Don't use** for 5+ options — use `Select` or `Combobox`.

---

## Usage

```tsx
import { ChoiceDialog, IChoiceDialogOption } from "@spaarke/ui-components";
import { HistoryRegular, DocumentAddRegular } from "@fluentui/react-icons";

const options: IChoiceDialogOption[] = [
    { id: "resume", icon: <HistoryRegular />, title: "Resume", description: "Continue where you left off" },
    { id: "fresh", icon: <DocumentAddRegular />, title: "Start Fresh", description: "Begin new session" }
];

<ChoiceDialog
    open={open}
    title="Resume Session?"
    message="This analysis has existing history."
    options={options}
    onSelect={(id) => handleSelection(id)}
    onDismiss={() => setOpen(false)}
/>
```

---

## Design Rules

- Use `Button appearance="outline"` for option buttons
- Include icon (24px), title (semibold), and description for each option
- Stack options vertically (not horizontal)
- Provide Cancel in `DialogActions`
- Use semantic color tokens (`colorBrandForeground1` for icons)
- No more than 4 options
- No auto-selection — force conscious choice

---

## Full Documentation

See [docs/adr/ADR-023-choice-dialog-pattern.md](../../../docs/adr/ADR-023-choice-dialog-pattern.md) for complete implementation details, accessibility considerations, and alternatives analysis.
