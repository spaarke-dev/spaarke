/**
 * AddEditPropertyDialog — dialog for creating or editing a container custom property.
 *
 * A custom property is a key-value pair with an optional isSearchable flag.
 * When isSearchable is true, the property is indexed in the SPE search index,
 * allowing administrators to filter containers by that property value.
 *
 * Used by CustomPropertyEditor for both add and edit operations.
 *
 * ADR-021: All styles use Fluent UI v9 makeStyles + design tokens — no hard-coded colors.
 * ADR-006: Code Page component — React 18 patterns, no PCF / ComponentFramework deps.
 */

import * as React from "react";
import {
  Dialog,
  DialogSurface,
  DialogTitle,
  DialogBody,
  DialogActions,
  DialogContent,
  Button,
  Field,
  Input,
  Switch,
  Text,
  makeStyles,
  tokens,
  shorthands,
} from "@fluentui/react-components";
import { Dismiss20Regular } from "@fluentui/react-icons";
import type { ContainerCustomProperty } from "../../types/spe";

// ─────────────────────────────────────────────────────────────────────────────
// Types
// ─────────────────────────────────────────────────────────────────────────────

export interface AddEditPropertyDialogProps {
  /** Whether the dialog is open */
  open: boolean;
  /**
   * The property key being edited, or null when adding a new property.
   * When provided, the key field is read-only.
   */
  editingKey: string | null;
  /** Current value of the property being edited (pre-populates the form) */
  editingValue: ContainerCustomProperty | null;
  /** Called when the user confirms the dialog — supplies the key and value */
  onConfirm: (key: string, value: ContainerCustomProperty) => void;
  /** Called when the user dismisses / cancels the dialog */
  onDismiss: () => void;
}

// ─────────────────────────────────────────────────────────────────────────────
// Styles
// ─────────────────────────────────────────────────────────────────────────────

const useStyles = makeStyles({
  surface: {
    width: "440px",
    maxWidth: "95vw",
  },

  titleRow: {
    display: "flex",
    alignItems: "center",
    justifyContent: "space-between",
    paddingRight: tokens.spacingHorizontalXS,
  },

  content: {
    display: "flex",
    flexDirection: "column",
    ...shorthands.gap(tokens.spacingVerticalL),
    paddingTop: tokens.spacingVerticalM,
    paddingBottom: tokens.spacingVerticalM,
  },

  searchableSection: {
    display: "flex",
    flexDirection: "column",
    ...shorthands.gap(tokens.spacingVerticalXS),
  },

  searchableHint: {
    color: tokens.colorNeutralForeground3,
    fontSize: tokens.fontSizeBase200,
    lineHeight: tokens.lineHeightBase200,
    paddingLeft: tokens.spacingHorizontalXXS,
  },

  actions: {
    justifyContent: "flex-end",
  },
});

// ─────────────────────────────────────────────────────────────────────────────
// Validation
// ─────────────────────────────────────────────────────────────────────────────

/** Property keys must be alphanumeric + underscores, 1-64 chars. */
function validateKey(key: string): string | null {
  const trimmed = key.trim();
  if (!trimmed) return "Property name is required.";
  if (trimmed.length > 64) return "Property name must be 64 characters or fewer.";
  if (!/^[A-Za-z0-9_]+$/.test(trimmed))
    return "Property name may only contain letters, digits, and underscores.";
  return null;
}

/** Values must be non-empty strings up to 256 chars. */
function validateValue(value: string): string | null {
  if (!value.trim()) return "Property value is required.";
  if (value.length > 256) return "Property value must be 256 characters or fewer.";
  return null;
}

// ─────────────────────────────────────────────────────────────────────────────
// Component
// ─────────────────────────────────────────────────────────────────────────────

/**
 * Modal dialog for creating or editing a single container custom property.
 *
 * - Add mode: editingKey is null, all fields start empty.
 * - Edit mode: editingKey is the existing key (read-only), form pre-populated.
 */
export const AddEditPropertyDialog: React.FC<AddEditPropertyDialogProps> = ({
  open,
  editingKey,
  editingValue,
  onConfirm,
  onDismiss,
}) => {
  const styles = useStyles();
  const isEditing = editingKey !== null;

  // ── Form state ─────────────────────────────────────────────────────────────

  const [key, setKey] = React.useState("");
  const [value, setValue] = React.useState("");
  const [isSearchable, setIsSearchable] = React.useState(false);

  // ── Validation state ───────────────────────────────────────────────────────

  const [keyError, setKeyError] = React.useState<string | null>(null);
  const [valueError, setValueError] = React.useState<string | null>(null);

  // ── Populate form when dialog opens or editing target changes ─────────────

  React.useEffect(() => {
    if (open) {
      setKey(editingKey ?? "");
      setValue(editingValue?.value ?? "");
      setIsSearchable(editingValue?.isSearchable ?? false);
      setKeyError(null);
      setValueError(null);
    }
  }, [open, editingKey, editingValue]);

  // ── Handlers ───────────────────────────────────────────────────────────────

  const handleKeyChange = React.useCallback((e: React.ChangeEvent<HTMLInputElement>) => {
    const v = e.target.value;
    setKey(v);
    // Clear error on change so user isn't nagged while typing
    if (keyError) setKeyError(null);
  }, [keyError]);

  const handleValueChange = React.useCallback((e: React.ChangeEvent<HTMLInputElement>) => {
    const v = e.target.value;
    setValue(v);
    if (valueError) setValueError(null);
  }, [valueError]);

  const handleConfirm = React.useCallback(() => {
    const trimmedKey = key.trim();
    const trimmedValue = value.trim();

    const kErr = isEditing ? null : validateKey(trimmedKey);
    const vErr = validateValue(trimmedValue);

    setKeyError(kErr);
    setValueError(vErr);

    if (kErr || vErr) return;

    onConfirm(isEditing ? editingKey! : trimmedKey, {
      value: trimmedValue,
      isSearchable,
    });
  }, [key, value, isSearchable, isEditing, editingKey, onConfirm]);

  const handleKeyDown = React.useCallback(
    (e: React.KeyboardEvent) => {
      if (e.key === "Enter") {
        e.preventDefault();
        handleConfirm();
      }
    },
    [handleConfirm],
  );

  // ── Render ─────────────────────────────────────────────────────────────────

  return (
    <Dialog open={open} onOpenChange={(_e, data) => !data.open && onDismiss()}>
      <DialogSurface className={styles.surface}>
        <DialogBody>
          <DialogTitle
            action={
              <Button
                appearance="subtle"
                aria-label="Close"
                icon={<Dismiss20Regular />}
                onClick={onDismiss}
              />
            }
          >
            {isEditing ? "Edit Custom Property" : "Add Custom Property"}
          </DialogTitle>

          <DialogContent>
            <div className={styles.content} onKeyDown={handleKeyDown}>
              {/* Property key (name) */}
              <Field
                label="Property Name"
                required
                validationState={keyError ? "error" : "none"}
                validationMessage={keyError ?? undefined}
                hint={
                  isEditing
                    ? "Property name cannot be changed after creation."
                    : "Alphanumeric characters and underscores only. Used as the property key."
                }
              >
                <Input
                  value={key}
                  onChange={handleKeyChange}
                  disabled={isEditing}
                  placeholder="e.g. ProjectCode"
                  autoFocus={!isEditing}
                  maxLength={64}
                  aria-label="Property name"
                />
              </Field>

              {/* Property value */}
              <Field
                label="Value"
                required
                validationState={valueError ? "error" : "none"}
                validationMessage={valueError ?? undefined}
              >
                <Input
                  value={value}
                  onChange={handleValueChange}
                  placeholder="e.g. PROJ-001"
                  autoFocus={isEditing}
                  maxLength={256}
                  aria-label="Property value"
                />
              </Field>

              {/* Searchable toggle */}
              <div className={styles.searchableSection}>
                <Switch
                  checked={isSearchable}
                  onChange={(_e, data) => setIsSearchable(data.checked)}
                  label="Searchable"
                  aria-label="Make property searchable"
                />
                <Text className={styles.searchableHint}>
                  When enabled, this property is indexed in the SPE search index. Administrators
                  can use it to filter and discover containers by this metadata value.
                </Text>
              </div>
            </div>
          </DialogContent>

          <DialogActions className={styles.actions}>
            <Button appearance="secondary" onClick={onDismiss}>
              Cancel
            </Button>
            <Button appearance="primary" onClick={handleConfirm}>
              {isEditing ? "Save" : "Add"}
            </Button>
          </DialogActions>
        </DialogBody>
      </DialogSurface>
    </Dialog>
  );
};
