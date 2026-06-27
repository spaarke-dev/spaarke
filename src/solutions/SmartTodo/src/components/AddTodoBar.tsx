/**
 * AddTodoBar — Input bar for creating new manual to-do items (Task 015).
 *
 * Layout (flexbox row, left-to-right):
 *   [AddRegular icon] [Input field — flex-grow] [Add button]
 *
 * Behaviour:
 *   - Enter key in input triggers the same action as the Add button
 *   - Validates that the title is non-empty before calling onAdd
 *   - Clears the input field after a successful add
 *   - Shows an inline validation error when submit is attempted with empty input
 *   - Disables controls while the parent is processing the add (isLoading prop)
 *
 * Design constraints:
 *   - ALL colours from Fluent UI v9 semantic tokens — zero hardcoded hex/rgb
 *   - makeStyles (Griffel) only for custom styles
 *   - Dark mode + high-contrast supported automatically via token system
 *
 * ── Launch-context contract (smart-todo-decoupling-r3 task 032 / FR-16) ──────
 *
 * AddTodoBar is the kanban "Add To Do" entry point. Per FR-16, when this entry
 * point opens the full `CreateTodoWizard` (today: a quick title-only create path
 * via `onAdd`; future: a wizard launch path on richer-add), it MUST pass
 * `initialRegarding={undefined}` — the kanban context has no implicit parent
 * record. The user picks a regarding (or skips) inside the wizard.
 *
 * The constant `KANBAN_WIZARD_INITIAL_REGARDING` is exported as the binding
 * value for any future wiring; tests assert wizard launches via this constant
 * remain undefined.
 *
 * Sibling entry-point contracts (per the launch-context note):
 *   - Parent-form ribbon (task 040)  → wizard launched with the launch record triple
 *   - Outlook "Create To Do" (task 070) → wizard launched with the sprk_communication triple
 *
 * See: projects/smart-todo-decoupling-r3/notes/createtodo-launch-contract.md
 */

import * as React from "react";
import {
  makeStyles,
  tokens,
  Input,
  Button,
} from "@fluentui/react-components";
import { AddRegular } from "@fluentui/react-icons";

// ---------------------------------------------------------------------------
// Launch-context contract export (FR-16 — kanban entry has NO regarding pre-fill)
// ---------------------------------------------------------------------------

/**
 * Binding value for the kanban "Add To Do" launch context.
 *
 * Per FR-16, the kanban entry has no parent record context — when the kanban
 * opens the `CreateTodoWizard`, it MUST pass `initialRegarding` as this value
 * (i.e. `undefined`). This makes the contract explicit at the call site rather
 * than relying on prop omission.
 *
 * Future kanban wizard wiring should use:
 * ```tsx
 * import { KANBAN_WIZARD_INITIAL_REGARDING } from './AddTodoBar';
 * <CreateTodoWizard
 *   open={open}
 *   onClose={onClose}
 *   initialRegarding={KANBAN_WIZARD_INITIAL_REGARDING}
 *   // ...other props
 * />
 * ```
 */
export const KANBAN_WIZARD_INITIAL_REGARDING: undefined = undefined;

// ---------------------------------------------------------------------------
// Styles
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  bar: {
    display: "flex",
    flexDirection: "row",
    alignItems: "center",
    gap: tokens.spacingHorizontalS,
    paddingTop: tokens.spacingVerticalS,
    paddingBottom: tokens.spacingVerticalS,
    paddingLeft: tokens.spacingHorizontalM,
    paddingRight: tokens.spacingHorizontalM,
    borderBottomWidth: "1px",
    borderBottomStyle: "solid",
    borderBottomColor: tokens.colorNeutralStroke2,
    backgroundColor: tokens.colorNeutralBackground1,
    flexShrink: 0,
  },

  addIcon: {
    display: "flex",
    alignItems: "center",
    color: tokens.colorNeutralForeground3,
    flexShrink: 0,
  },

  inputWrapper: {
    flex: "1 1 0",
    minWidth: 0,
    display: "flex",
    flexDirection: "column",
    gap: tokens.spacingVerticalXXS,
  },

  input: {
    width: "100%",
  },

  validationMessage: {
    color: tokens.colorPaletteRedForeground1,
    fontSize: tokens.fontSizeBase100,
    lineHeight: tokens.lineHeightBase100,
  },
});

// ---------------------------------------------------------------------------
// Props
// ---------------------------------------------------------------------------

export interface IAddTodoBarProps {
  /**
   * Called when the user submits a new to-do title.
   * The parent component handles Dataverse creation + optimistic list update.
   * Returns a promise that resolves when the operation is complete.
   */
  onAdd: (title: string) => Promise<void>;
  /**
   * Disable the bar while the parent is processing a create operation.
   * Prevents double-submits during in-flight requests.
   */
  isAdding?: boolean;
}

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

export const AddTodoBar: React.FC<IAddTodoBarProps> = React.memo(
  ({ onAdd, isAdding = false }) => {
    const styles = useStyles();
    const [value, setValue] = React.useState<string>("");
    const [validationError, setValidationError] = React.useState<string | null>(null);
    const inputRef = React.useRef<HTMLInputElement>(null);

    // -----------------------------------------------------------------------
    // Handlers
    // -----------------------------------------------------------------------

    const handleAdd = React.useCallback(async () => {
      const trimmed = value.trim();
      if (!trimmed) {
        setValidationError("To-do title cannot be empty.");
        inputRef.current?.focus();
        return;
      }

      setValidationError(null);
      // Clear the input immediately for optimistic feel
      setValue("");

      try {
        await onAdd(trimmed);
      } catch {
        // Parent is responsible for error reporting (MessageBar etc.)
        // Restore the title so the user can retry without retyping
        setValue(trimmed);
      }
    }, [value, onAdd]);

    const handleInputChange = React.useCallback(
      (_ev: React.ChangeEvent<HTMLInputElement>, data: { value: string }) => {
        setValue(data.value);
        if (validationError && data.value.trim()) {
          setValidationError(null);
        }
      },
      [validationError]
    );

    const handleKeyDown = React.useCallback(
      (ev: React.KeyboardEvent<HTMLInputElement>) => {
        if (ev.key === "Enter" && !isAdding) {
          ev.preventDefault();
          void handleAdd();
        }
      },
      [handleAdd, isAdding]
    );

    // -----------------------------------------------------------------------
    // Render
    // -----------------------------------------------------------------------

    return (
      <div className={styles.bar} role="group" aria-label="Add to-do item">
        {/* Leading + icon */}
        <div className={styles.addIcon} aria-hidden="true">
          <AddRegular fontSize={16} />
        </div>

        {/* Input field with inline validation */}
        <div className={styles.inputWrapper}>
          <Input
            ref={inputRef}
            className={styles.input}
            size="small"
            placeholder="Add a new to-do…"
            value={value}
            onChange={handleInputChange}
            onKeyDown={handleKeyDown}
            disabled={isAdding}
            aria-label="New to-do item title"
            aria-invalid={validationError ? "true" : "false"}
            aria-describedby={validationError ? "add-todo-error" : undefined}
          />
          {validationError && (
            <span
              id="add-todo-error"
              role="alert"
              className={styles.validationMessage}
            >
              {validationError}
            </span>
          )}
        </div>

        {/* Add button */}
        <Button
          appearance="primary"
          size="small"
          icon={<AddRegular />}
          onClick={() => void handleAdd()}
          disabled={isAdding}
          aria-label="Add to-do item"
        >
          Add
        </Button>
      </div>
    );
  }
);

AddTodoBar.displayName = "AddTodoBar";
