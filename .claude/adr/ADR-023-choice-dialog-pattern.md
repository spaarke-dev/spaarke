# ADR-023: Choice Dialog Pattern

| Field | Value |
|-------|-------|
| Status | **Accepted** |
| Date | 2026-01-02 |
| Full ADR | [`docs/adr/ADR-023-choice-dialog-pattern.md`](../../docs/adr/ADR-023-choice-dialog-pattern.md) |

---

## Decision

Use a **rich option button pattern** for dialogs requiring user choice between 2-4 mutually exclusive options.

Each option is a full-width button with:
- **Icon** (24px, brand color) - visual identifier
- **Title** (semibold) - action name
- **Description** (caption text) - explains consequences

---

## When to Use

| Use Case | Example |
|----------|---------|
| Resume vs Start Fresh | "Resume previous session?" with history count |
| Export Format | "Export as PDF, DOCX, or Email?" |
| Destructive Actions | "Delete permanently or move to trash?" |
| Mode Selection | "Create new or open existing?" |

**Don't use** for simple confirmations ("Are you sure?") - use standard `ConfirmDialog`.

---

## Constraints

**MUST:**
- Use `Dialog` from `@fluentui/react-components`
- Use `Button appearance="outline"` for option buttons
- Include icon, title, and description for each option
- Provide Cancel action in `DialogActions`
- Use semantic color tokens (icons use `colorBrandForeground1`)

**MUST NOT:**
- Hard-code colors
- Use more than 4 options (consider different pattern)
- Mix button appearances within options
- Use this for simple yes/no confirmations

---

## Implementation Pattern

```tsx
import {
    Dialog, DialogSurface, DialogTitle, DialogBody,
    DialogActions, DialogContent, Button, Text, makeStyles, tokens
} from "@fluentui/react-components";
import { HistoryRegular, DocumentAddRegular } from "@fluentui/react-icons";

const useStyles = makeStyles({
    optionButton: {
        display: "flex",
        alignItems: "center",
        justifyContent: "flex-start",
        gap: tokens.spacingHorizontalM,
        padding: tokens.spacingVerticalM,
        width: "100%",
        textAlign: "left"
    },
    optionIcon: {
        fontSize: "24px",
        color: tokens.colorBrandForeground1
    },
    optionText: {
        display: "flex",
        flexDirection: "column",
        gap: tokens.spacingVerticalXXS
    },
    optionTitle: { fontWeight: tokens.fontWeightSemibold },
    optionDescription: {
        color: tokens.colorNeutralForeground2,
        fontSize: tokens.fontSizeBase200
    }
});

// Usage
<Dialog open={open} onOpenChange={(_, data) => !data.open && onDismiss()}>
    <DialogSurface>
        <DialogBody>
            <DialogTitle>Choose an Option</DialogTitle>
            <DialogContent>
                <Text>Description of the situation...</Text>
                <div className={styles.optionsContainer}>
                    <Button appearance="outline" className={styles.optionButton} onClick={onOption1}>
                        <Icon1 className={styles.optionIcon} />
                        <div className={styles.optionText}>
                            <span className={styles.optionTitle}>Option 1</span>
                            <span className={styles.optionDescription}>What happens if chosen</span>
                        </div>
                    </Button>
                    {/* More options... */}
                </div>
            </DialogContent>
            <DialogActions>
                <Button appearance="secondary" onClick={onDismiss}>Cancel</Button>
            </DialogActions>
        </DialogBody>
    </DialogSurface>
</Dialog>
```

---

## Component Location

`@spaarke/ui-components` exports `ChoiceDialog` for reuse:

```tsx
import { ChoiceDialog, IChoiceDialogOption } from "@spaarke/ui-components";

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

## Related

- **ADR-021**: Fluent UI v9 Design System (parent design system)
- **ADR-012**: Shared Component Library (component organization)
