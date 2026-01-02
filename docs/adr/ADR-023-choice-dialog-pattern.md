# ADR-023: Choice Dialog Pattern

| Field | Value |
|-------|-------|
| Status | **Accepted** |
| Date | 2026-01-02 |
| Authors | Spaarke Engineering |

---

## Related AI Context

**AI-Optimized Versions** (load these for efficient context):
- [ADR-023 Concise](../../.claude/adr/ADR-023-choice-dialog-pattern.md) - ~120 lines, decision + constraints
- [ADR-021 Fluent Design System](./ADR-021-fluent-ui-design-system.md) - Parent design system

**When to load this full ADR**: Understanding rationale, alternatives considered, accessibility details

---

## Context

### Problem Statement

Spaarke applications frequently need to present users with choices between 2-4 mutually exclusive options. Common scenarios include:

- **Resume vs Start Fresh**: When opening an existing analysis with chat history
- **Export Format Selection**: Choosing between PDF, DOCX, Email
- **Mode Selection**: Create new vs Open existing
- **Destructive Action Confirmation**: Delete permanently vs Move to trash

### Previous Approaches and Issues

Before this pattern was standardized, dialogs used various inconsistent approaches:

1. **Radio Button Lists**: Required two actions (select + confirm), poor visual hierarchy
2. **Button Rows**: Horizontal buttons with just labels, no room for explanatory text
3. **Dropdown Selections**: Hidden options, poor discoverability
4. **Simple Confirm Dialogs**: Just "Yes/No" without explaining consequences

These approaches suffered from:
- **Cognitive load**: Users uncertain about consequences of each choice
- **Accessibility issues**: Insufficient visual distinction between options
- **Inconsistent patterns**: Each dialog implemented differently
- **Poor mobile experience**: Small touch targets

### Design Inspiration

The pattern is inspired by:
- **Microsoft Settings dialogs**: Windows 11 settings use rich option cards
- **Fluent UI v9 compound buttons**: Multi-line buttons with descriptions
- **iOS action sheets**: Clear, vertically stacked action choices
- **Material Design selection patterns**: Cards with icons and descriptions

---

## Decision

**Adopt a rich option button pattern for dialogs presenting 2-4 mutually exclusive choices.**

### Visual Structure

```
┌─────────────────────────────────────────────────┐
│  Dialog Title                               [X] │
├─────────────────────────────────────────────────┤
│                                                 │
│  Contextual message explaining the situation... │
│                                                 │
│  ┌─────────────────────────────────────────┐   │
│  │ [Icon]  Option Title                    │   │
│  │         Description of what happens...  │   │
│  └─────────────────────────────────────────┘   │
│                                                 │
│  ┌─────────────────────────────────────────┐   │
│  │ [Icon]  Option Title                    │   │
│  │         Description of what happens...  │   │
│  └─────────────────────────────────────────┘   │
│                                                 │
├─────────────────────────────────────────────────┤
│                                    [ Cancel ]   │
└─────────────────────────────────────────────────┘
```

### Key Design Decisions

| Decision | Rationale |
|----------|-----------|
| **Vertical stack layout** | Better scanning, works on narrow screens |
| **Full-width buttons** | Large touch targets, clear visual hierarchy |
| **Icon + Title + Description** | Quick visual identification + detailed explanation |
| **Outline appearance** | Distinguishes from primary actions, feels like choices not actions |
| **Cancel in footer** | Consistent with Fluent dialog patterns |
| **No default selection** | Forces conscious decision, avoids accidental selection |

---

## Constraints

### MUST Requirements

| Requirement | Rationale |
|-------------|-----------|
| Use `Dialog` from `@fluentui/react-components` | Fluent v9 compliance (ADR-021) |
| Use `Button appearance="outline"` for options | Visual consistency, feels like selection not action |
| Include icon, title, AND description | Each serves different cognitive purpose |
| Provide Cancel action | Allow user to dismiss without choosing |
| Use semantic color tokens | Dark mode and theme compatibility |
| Use 24px icons | Consistent visual weight, good touch target |
| Stack options vertically | Works on all screen sizes |

### MUST NOT Constraints

| Constraint | Rationale |
|------------|-----------|
| Don't hard-code colors | Breaks dark mode, theme customization |
| Don't use more than 4 options | Too many choices causes decision paralysis |
| Don't mix button appearances | Confuses visual hierarchy |
| Don't use for simple yes/no | Over-designed for binary confirmation |
| Don't auto-select options | Forces conscious decision |
| Don't use horizontal layout | Poor mobile experience |

### SHOULD Recommendations

| Recommendation | Context |
|----------------|---------|
| Place primary/recommended option first | Users tend to scan top-to-bottom |
| Use verbs in titles ("Resume", "Start Fresh") | Action-oriented language |
| Keep descriptions under 80 characters | Scannability, mobile display |
| Include relevant context in message | "X messages in history" not just "has history" |
| Use icons from same visual family | Visual consistency |

---

## Implementation

### Style Definitions

```typescript
import { makeStyles, tokens } from "@fluentui/react-components";

const useChoiceDialogStyles = makeStyles({
    content: {
        display: "flex",
        flexDirection: "column",
        gap: tokens.spacingVerticalM
    },
    optionsContainer: {
        display: "flex",
        flexDirection: "column",
        gap: tokens.spacingVerticalS,
        marginTop: tokens.spacingVerticalM
    },
    optionButton: {
        display: "flex",
        alignItems: "center",
        justifyContent: "flex-start",
        gap: tokens.spacingHorizontalM,
        padding: tokens.spacingVerticalM,
        width: "100%",
        textAlign: "left",
        minHeight: "64px" // Accessibility: minimum touch target
    },
    optionIcon: {
        fontSize: "24px",
        color: tokens.colorBrandForeground1,
        flexShrink: 0 // Prevent icon from shrinking
    },
    optionText: {
        display: "flex",
        flexDirection: "column",
        gap: tokens.spacingVerticalXXS,
        overflow: "hidden" // Handle long text
    },
    optionTitle: {
        fontWeight: tokens.fontWeightSemibold,
        color: tokens.colorNeutralForeground1
    },
    optionDescription: {
        color: tokens.colorNeutralForeground2,
        fontSize: tokens.fontSizeBase200,
        lineHeight: tokens.lineHeightBase200
    }
});
```

### Component Interface

```typescript
/**
 * Represents a single choice option in the dialog.
 */
export interface IChoiceDialogOption {
    /** Unique identifier for this option */
    id: string;
    /** Icon component (24px recommended) */
    icon: React.ReactNode;
    /** Short, action-oriented title */
    title: string;
    /** Explanation of what happens when chosen */
    description: string;
    /** Optional: disable this option */
    disabled?: boolean;
}

/**
 * Props for the ChoiceDialog component.
 */
export interface IChoiceDialogProps {
    /** Whether the dialog is open */
    open: boolean;
    /** Dialog title */
    title: string;
    /** Contextual message explaining the situation */
    message: string | React.ReactNode;
    /** Array of 2-4 choice options */
    options: IChoiceDialogOption[];
    /** Callback when user selects an option */
    onSelect: (optionId: string) => void;
    /** Callback when dialog is dismissed */
    onDismiss: () => void;
    /** Optional: text for cancel button (default: "Cancel") */
    cancelText?: string;
}
```

### Full Component Implementation

```tsx
import * as React from "react";
import {
    Dialog,
    DialogSurface,
    DialogTitle,
    DialogBody,
    DialogActions,
    DialogContent,
    Button,
    Text,
    makeStyles,
    tokens
} from "@fluentui/react-components";

export const ChoiceDialog: React.FC<IChoiceDialogProps> = ({
    open,
    title,
    message,
    options,
    onSelect,
    onDismiss,
    cancelText = "Cancel"
}) => {
    const styles = useChoiceDialogStyles();

    const handleOptionClick = React.useCallback((optionId: string) => {
        onSelect(optionId);
    }, [onSelect]);

    return (
        <Dialog open={open} onOpenChange={(_, data) => !data.open && onDismiss()}>
            <DialogSurface>
                <DialogBody>
                    <DialogTitle>{title}</DialogTitle>
                    <DialogContent className={styles.content}>
                        {typeof message === "string" ? <Text>{message}</Text> : message}

                        <div className={styles.optionsContainer}>
                            {options.map((option) => (
                                <Button
                                    key={option.id}
                                    appearance="outline"
                                    className={styles.optionButton}
                                    disabled={option.disabled}
                                    onClick={() => handleOptionClick(option.id)}
                                    aria-describedby={`option-desc-${option.id}`}
                                >
                                    <span className={styles.optionIcon}>{option.icon}</span>
                                    <div className={styles.optionText}>
                                        <span className={styles.optionTitle}>{option.title}</span>
                                        <span
                                            id={`option-desc-${option.id}`}
                                            className={styles.optionDescription}
                                        >
                                            {option.description}
                                        </span>
                                    </div>
                                </Button>
                            ))}
                        </div>
                    </DialogContent>
                    <DialogActions>
                        <Button appearance="secondary" onClick={onDismiss}>
                            {cancelText}
                        </Button>
                    </DialogActions>
                </DialogBody>
            </DialogSurface>
        </Dialog>
    );
};
```

### Usage Example

```tsx
import { ChoiceDialog, IChoiceDialogOption } from "@spaarke/ui-components";
import { HistoryRegular, DocumentAddRegular } from "@fluentui/react-icons";

const ResumeSessionExample: React.FC = () => {
    const [open, setOpen] = React.useState(true);
    const chatMessageCount = 12;

    const options: IChoiceDialogOption[] = [
        {
            id: "resume",
            icon: <HistoryRegular />,
            title: "Resume Session",
            description: "Continue with your previous conversation history"
        },
        {
            id: "fresh",
            icon: <DocumentAddRegular />,
            title: "Start Fresh",
            description: "Begin a new conversation (previous history will be cleared)"
        }
    ];

    const handleSelect = (optionId: string) => {
        if (optionId === "resume") {
            // Load chat history
        } else {
            // Clear history and start fresh
        }
        setOpen(false);
    };

    return (
        <ChoiceDialog
            open={open}
            title="Resume Previous Session?"
            message={
                <>
                    This analysis has an existing conversation with{" "}
                    <strong>{chatMessageCount} messages</strong>.
                </>
            }
            options={options}
            onSelect={handleSelect}
            onDismiss={() => setOpen(false)}
        />
    );
};
```

---

## Accessibility Considerations

### Keyboard Navigation

| Key | Behavior |
|-----|----------|
| Tab | Move between option buttons and Cancel |
| Enter/Space | Activate focused button |
| Escape | Dismiss dialog (same as Cancel) |

### Screen Reader Support

- **Dialog role**: Fluent `Dialog` handles ARIA dialog role
- **Title announcement**: `DialogTitle` provides accessible name
- **Description linking**: `aria-describedby` connects button to description
- **Focus management**: Focus trapped within dialog while open

### Visual Accessibility

- **Color contrast**: Semantic tokens ensure 4.5:1 minimum contrast
- **Focus indicators**: Fluent buttons include visible focus rings
- **Touch targets**: Minimum 64px height for option buttons
- **Icons with text**: Icons are decorative (text provides meaning)

---

## When NOT to Use This Pattern

| Scenario | Better Alternative |
|----------|-------------------|
| Simple yes/no confirmation | Standard `ConfirmDialog` with primary/secondary buttons |
| More than 4 options | `Select` or `Combobox` dropdown |
| Non-mutually exclusive options | Checkbox list |
| Quick inline selections | `RadioGroup` or `ToggleButton` group |
| Navigation choices | `Menu` or `TabList` |

---

## Alternatives Considered

### 1. Radio Button Dialog

```
[ ] Option 1
[ ] Option 2
        [Cancel] [Confirm]
```

**Rejected because:**
- Requires two actions (select + confirm)
- Less visually impactful
- No room for descriptions without clutter

### 2. Button Group Dialog

```
[Option 1] [Option 2] [Cancel]
```

**Rejected because:**
- Poor mobile experience (horizontal overflow)
- No room for descriptions
- Inconsistent with Fluent dialog patterns

### 3. Card-based Selection

```
┌─────────┐  ┌─────────┐
│  Card 1 │  │  Card 2 │
└─────────┘  └─────────┘
```

**Rejected because:**
- Horizontal layout issues on mobile
- Over-designed for simple choices
- Inconsistent with Fluent component library

---

## Component Location

The reusable `ChoiceDialog` component is available in the shared UI library:

```typescript
// Import from shared library
import { ChoiceDialog, IChoiceDialogOption } from "@spaarke/ui-components";
```

**File location:** `src/client/shared/Spaarke.UI.Components/src/components/ChoiceDialog/ChoiceDialog.tsx`

---

## References

- [Fluent UI v9 Dialog](https://react.fluentui.dev/?path=/docs/components-dialog--docs)
- [Fluent UI v9 Button](https://react.fluentui.dev/?path=/docs/components-button--docs)
- [WCAG 2.1 Dialog Requirements](https://www.w3.org/WAI/ARIA/apg/patterns/dialog-modal/)
- [Nielsen Norman Group: Presenting Options](https://www.nngroup.com/articles/radio-buttons-default-selection/)

---

## Revision History

| Date | Version | Changes | Author |
|------|---------|---------|--------|
| 2026-01-02 | 1.0 | Initial ADR creation based on ResumeSessionDialog implementation | Spaarke Engineering |
