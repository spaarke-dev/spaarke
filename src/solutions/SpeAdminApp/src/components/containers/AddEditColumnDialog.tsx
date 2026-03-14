/**
 * AddEditColumnDialog — dialog for creating or editing a container column definition.
 *
 * Supports all column types: text, boolean, dateTime, currency, choice, number,
 * personOrGroup, and hyperlinkOrPicture. Renders dynamic type-specific fields
 * based on the selected type.
 *
 * ADR-021: All styles use makeStyles + design tokens. No hard-coded colors.
 * ADR-012: Fluent UI v9 components exclusively.
 * ADR-006: Code Page — React 18, no PCF / ComponentFramework dependencies.
 */

import * as React from "react";
import {
  makeStyles,
  tokens,
  Text,
  Spinner,
  Button,
  Dialog,
  DialogSurface,
  DialogTitle,
  DialogBody,
  DialogContent,
  DialogActions,
  Field,
  Input,
  Textarea,
  Select,
  Switch,
  MessageBar,
  MessageBarBody,
  Badge,
  shorthands,
} from "@fluentui/react-components";
import { Add20Regular, Delete20Regular } from "@fluentui/react-icons";
import type { ColumnDefinition, ColumnDefinitionUpsert } from "../../types/spe";

// ─────────────────────────────────────────────────────────────────────────────
// Column type definitions
// ─────────────────────────────────────────────────────────────────────────────

export type ColumnEditorType =
  | "text"
  | "boolean"
  | "dateTime"
  | "currency"
  | "choice"
  | "number"
  | "personOrGroup"
  | "hyperlinkOrPicture";

interface ColumnTypeOption {
  label: string;
  value: ColumnEditorType;
  description: string;
}

const COLUMN_TYPE_OPTIONS: ColumnTypeOption[] = [
  { label: "Text", value: "text", description: "Single or multi-line text" },
  { label: "Number", value: "number", description: "Integer or decimal values" },
  { label: "Boolean", value: "boolean", description: "True / false toggle" },
  { label: "Date & Time", value: "dateTime", description: "Date and time values" },
  { label: "Choice", value: "choice", description: "Predefined list of options" },
  { label: "Currency", value: "currency", description: "Monetary amounts with currency code" },
  { label: "Person or Group", value: "personOrGroup", description: "Azure AD user or group" },
  { label: "Hyperlink or Picture", value: "hyperlinkOrPicture", description: "URL or image link" },
];

const DATETIME_FORMAT_OPTIONS = [
  { label: "Date Only", value: "dateOnly" },
  { label: "Date & Time", value: "dateTime" },
];

// ─────────────────────────────────────────────────────────────────────────────
// Props
// ─────────────────────────────────────────────────────────────────────────────

export interface AddEditColumnDialogProps {
  /** Whether the dialog is open. */
  open: boolean;
  /** Column to edit, or null when adding. */
  column: ColumnDefinition | null;
  /** Whether a save operation is in progress. */
  saving: boolean;
  /** Server-side error message (from API). */
  error: string | null;
  /**
   * Called when the user confirms the form.
   * Returns the upsert payload for create or update.
   */
  onSave: (payload: ColumnDefinitionUpsert) => Promise<void>;
  /** Called to dismiss the dialog without saving. */
  onDismiss: () => void;
}

// ─────────────────────────────────────────────────────────────────────────────
// Styles (ADR-021 — Fluent tokens only)
// ─────────────────────────────────────────────────────────────────────────────

const useStyles = makeStyles({
  dialogContent: {
    display: "flex",
    flexDirection: "column",
    ...shorthands.gap(tokens.spacingVerticalM),
    paddingTop: tokens.spacingVerticalS,
    minWidth: "440px",
    maxWidth: "540px",
  },

  typeSectionTitle: {
    color: tokens.colorNeutralForeground2,
    fontSize: tokens.fontSizeBase200,
    textTransform: "uppercase",
    letterSpacing: "0.04em",
    marginTop: tokens.spacingVerticalXS,
  },

  typeSpecificSection: {
    display: "flex",
    flexDirection: "column",
    ...shorthands.gap(tokens.spacingVerticalM),
    paddingTop: tokens.spacingVerticalS,
    paddingBottom: tokens.spacingVerticalS,
    paddingLeft: tokens.spacingHorizontalM,
    paddingRight: tokens.spacingHorizontalM,
    borderLeftWidth: "3px",
    borderLeftStyle: "solid",
    borderLeftColor: tokens.colorBrandBackground2,
    backgroundColor: tokens.colorNeutralBackground2,
    borderRadius: tokens.borderRadiusMedium,
  },

  choicesContainer: {
    display: "flex",
    flexDirection: "column",
    ...shorthands.gap(tokens.spacingVerticalXS),
  },

  choiceRow: {
    display: "flex",
    alignItems: "center",
    ...shorthands.gap(tokens.spacingHorizontalS),
  },

  choiceInput: {
    flex: "1 1 auto",
  },

  choicesBadge: {
    alignSelf: "flex-start",
  },

  infoText: {
    color: tokens.colorNeutralForeground3,
    fontSize: tokens.fontSizeBase100,
    lineHeight: tokens.lineHeightBase200,
  },
});

// ─────────────────────────────────────────────────────────────────────────────
// Utility: derive ColumnEditorType from an existing ColumnDefinition
// ─────────────────────────────────────────────────────────────────────────────

function getColumnType(col: ColumnDefinition | null): ColumnEditorType {
  if (!col) return "text";
  if (col.boolean !== undefined) return "boolean";
  if (col.dateTime !== undefined) return "dateTime";
  if (col.choice !== undefined) return "choice";
  if (col.number !== undefined) return "number";
  if (col.text !== undefined) return "text";
  // personOrGroup and currency/hyperlink may not have explicit facets in the
  // read model — fall back to the columnGroup hint if present.
  const group = col.columnGroup?.toLowerCase() ?? "";
  if (group.includes("person") || group.includes("user")) return "personOrGroup";
  if (group.includes("currency")) return "currency";
  if (group.includes("hyperlink") || group.includes("picture")) return "hyperlinkOrPicture";
  return "text";
}

// ─────────────────────────────────────────────────────────────────────────────
// Utility: build ColumnDefinitionUpsert from form state
// ─────────────────────────────────────────────────────────────────────────────

interface FormState {
  name: string;
  displayName: string;
  description: string;
  required: boolean;
  columnType: ColumnEditorType;
  // Number type-specific
  numberMinimum: string;
  numberMaximum: string;
  // DateTime type-specific
  dateTimeFormat: string;
  // Choice type-specific
  choices: string[];
  allowTextEntry: boolean;
  // Currency type-specific
  currencyLocale: string;
}

function buildUpsertPayload(form: FormState): ColumnDefinitionUpsert {
  const payload: ColumnDefinitionUpsert = {
    name: form.name.trim(),
    displayName: form.displayName.trim(),
    description: form.description.trim() || undefined,
    required: form.required || undefined,
  };

  switch (form.columnType) {
    case "text":
      payload.text = {};
      break;

    case "number": {
      const numProps: Record<string, unknown> = {};
      const min = parseFloat(form.numberMinimum);
      const max = parseFloat(form.numberMaximum);
      if (!isNaN(min)) numProps.minimum = min;
      if (!isNaN(max)) numProps.maximum = max;
      payload.number = numProps;
      break;
    }

    case "boolean":
      payload.boolean = {};
      break;

    case "dateTime":
      payload.dateTime = { format: form.dateTimeFormat || "dateTime" };
      break;

    case "choice":
      payload.choice = {
        choices: form.choices.filter((c) => c.trim() !== ""),
        allowTextEntry: form.allowTextEntry || undefined,
        displayAs: "dropDownMenu",
      };
      break;

    case "currency":
      // Currency is represented as a number facet with a locale
      payload.number = { currencyLocale: form.currencyLocale || "USD" };
      break;

    case "personOrGroup":
      // personOrGroup uses a lookup facet (not directly editable in Graph columns API
      // — surface it as a text column with the right columnGroup annotation)
      payload.text = {};
      break;

    case "hyperlinkOrPicture":
      // hyperlinkOrPicture uses a lookup/text facet
      payload.text = {};
      break;
  }

  return payload;
}

// ─────────────────────────────────────────────────────────────────────────────
// Type-specific field sub-components
// ─────────────────────────────────────────────────────────────────────────────

interface NumberFieldsProps {
  minimum: string;
  maximum: string;
  disabled: boolean;
  onChangeMinimum: (value: string) => void;
  onChangeMaximum: (value: string) => void;
}

const NumberFields: React.FC<NumberFieldsProps> = ({
  minimum,
  maximum,
  disabled,
  onChangeMinimum,
  onChangeMaximum,
}) => (
  <>
    <Field label="Minimum Value">
      <Input
        type="number"
        value={minimum}
        onChange={(_, data) => onChangeMinimum(data.value)}
        placeholder="No minimum"
        disabled={disabled}
      />
    </Field>
    <Field label="Maximum Value">
      <Input
        type="number"
        value={maximum}
        onChange={(_, data) => onChangeMaximum(data.value)}
        placeholder="No maximum"
        disabled={disabled}
      />
    </Field>
  </>
);

interface DateTimeFieldsProps {
  format: string;
  disabled: boolean;
  onChange: (value: string) => void;
}

const DateTimeFields: React.FC<DateTimeFieldsProps> = ({ format, disabled, onChange }) => (
  <Field label="Date Format">
    <Select value={format} onChange={(_, data) => onChange(data.value)} disabled={disabled}>
      {DATETIME_FORMAT_OPTIONS.map((opt) => (
        <option key={opt.value} value={opt.value}>
          {opt.label}
        </option>
      ))}
    </Select>
  </Field>
);

interface ChoiceFieldsProps {
  choices: string[];
  allowTextEntry: boolean;
  disabled: boolean;
  onChoicesChange: (choices: string[]) => void;
  onAllowTextEntryChange: (value: boolean) => void;
}

const ChoiceFields: React.FC<ChoiceFieldsProps> = ({
  choices,
  allowTextEntry,
  disabled,
  onChoicesChange,
  onAllowTextEntryChange,
}) => {
  const styles = useStyles();

  const handleAddChoice = React.useCallback(() => {
    onChoicesChange([...choices, ""]);
  }, [choices, onChoicesChange]);

  const handleChangeChoice = React.useCallback(
    (index: number, value: string) => {
      const updated = choices.map((c, i) => (i === index ? value : c));
      onChoicesChange(updated);
    },
    [choices, onChoicesChange]
  );

  const handleRemoveChoice = React.useCallback(
    (index: number) => {
      onChoicesChange(choices.filter((_, i) => i !== index));
    },
    [choices, onChoicesChange]
  );

  return (
    <>
      <Field
        label={
          <span>
            Choices{" "}
            <Badge
              size="small"
              appearance="tinted"
              color="brand"
              className={styles.choicesBadge}
            >
              {choices.filter((c) => c.trim() !== "").length}
            </Badge>
          </span>
        }
      >
        <div className={styles.choicesContainer}>
          {choices.map((choice, index) => (
            <div key={index} className={styles.choiceRow}>
              <Input
                className={styles.choiceInput}
                value={choice}
                onChange={(_, data) => handleChangeChoice(index, data.value)}
                placeholder={`Choice ${index + 1}`}
                disabled={disabled}
                aria-label={`Choice ${index + 1}`}
              />
              <Button
                size="small"
                appearance="subtle"
                icon={<Delete20Regular />}
                onClick={() => handleRemoveChoice(index)}
                disabled={disabled}
                aria-label={`Remove choice ${index + 1}`}
              />
            </div>
          ))}
          <Button
            size="small"
            appearance="secondary"
            icon={<Add20Regular />}
            onClick={handleAddChoice}
            disabled={disabled}
            style={{ alignSelf: "flex-start" }}
          >
            Add Choice
          </Button>
        </div>
      </Field>

      <Field label="Allow Custom Text">
        <Switch
          checked={allowTextEntry}
          onChange={(_, data) => onAllowTextEntryChange(data.checked)}
          disabled={disabled}
          label={allowTextEntry ? "Yes — users can enter free text" : "No — only predefined choices"}
        />
      </Field>
    </>
  );
};

interface CurrencyFieldsProps {
  currencyLocale: string;
  disabled: boolean;
  onChange: (value: string) => void;
}

const CurrencyFields: React.FC<CurrencyFieldsProps> = ({ currencyLocale, disabled, onChange }) => {
  const styles = useStyles();
  return (
    <>
      <Field
        label="Currency Code"
        hint="ISO 4217 currency code, e.g. USD, EUR, GBP"
      >
        <Input
          value={currencyLocale}
          onChange={(_, data) => onChange(data.value.toUpperCase())}
          placeholder="USD"
          disabled={disabled}
          maxLength={3}
        />
      </Field>
      <Text className={styles.infoText}>
        Currency columns store numeric values. The currency code is used for display formatting.
      </Text>
    </>
  );
};

// ─────────────────────────────────────────────────────────────────────────────
// AddEditColumnDialog (main component)
// ─────────────────────────────────────────────────────────────────────────────

/**
 * Dialog for adding a new column definition or editing an existing one.
 *
 * The dialog adapts its type-specific fields section based on the selected
 * column type. Controlled externally via open/column/saving/error/onSave/onDismiss.
 */
export const AddEditColumnDialog: React.FC<AddEditColumnDialogProps> = ({
  open,
  column,
  saving,
  error,
  onSave,
  onDismiss,
}) => {
  const styles = useStyles();
  const isEditing = column !== null;

  // ── Form state ────────────────────────────────────────────────────────────

  const [name, setName] = React.useState("");
  const [displayName, setDisplayName] = React.useState("");
  const [description, setDescription] = React.useState("");
  const [required, setRequired] = React.useState(false);
  const [columnType, setColumnType] = React.useState<ColumnEditorType>("text");

  // Type-specific state
  const [numberMinimum, setNumberMinimum] = React.useState("");
  const [numberMaximum, setNumberMaximum] = React.useState("");
  const [dateTimeFormat, setDateTimeFormat] = React.useState("dateTime");
  const [choices, setChoices] = React.useState<string[]>([]);
  const [allowTextEntry, setAllowTextEntry] = React.useState(false);
  const [currencyLocale, setCurrencyLocale] = React.useState("USD");

  // Validation
  const [nameError, setNameError] = React.useState<string | null>(null);
  const [displayNameError, setDisplayNameError] = React.useState<string | null>(null);

  // ── Populate form when dialog opens ──────────────────────────────────────

  React.useEffect(() => {
    if (!open) return;

    if (column) {
      // Editing existing column
      setName(column.name);
      setDisplayName(column.displayName);
      setDescription(column.description ?? "");
      setRequired(column.required ?? false);

      const detectedType = getColumnType(column);
      setColumnType(detectedType);

      // Number type-specific
      const numFacet = column.number as Record<string, unknown> | undefined;
      setNumberMinimum(numFacet?.minimum !== undefined ? String(numFacet.minimum) : "");
      setNumberMaximum(numFacet?.maximum !== undefined ? String(numFacet.maximum) : "");

      // DateTime type-specific
      const dtFacet = column.dateTime as Record<string, unknown> | undefined;
      setDateTimeFormat((dtFacet?.format as string) ?? "dateTime");

      // Choice type-specific
      setChoices(column.choice?.choices ?? []);
      setAllowTextEntry(column.choice?.allowTextEntry ?? false);

      // Currency
      const currFacet = column.number as Record<string, unknown> | undefined;
      setCurrencyLocale((currFacet?.currencyLocale as string) ?? "USD");
    } else {
      // Adding new column — reset to defaults
      setName("");
      setDisplayName("");
      setDescription("");
      setRequired(false);
      setColumnType("text");
      setNumberMinimum("");
      setNumberMaximum("");
      setDateTimeFormat("dateTime");
      setChoices([]);
      setAllowTextEntry(false);
      setCurrencyLocale("USD");
    }

    setNameError(null);
    setDisplayNameError(null);
  }, [open, column]);

  // ── Auto-populate displayName from name when adding ───────────────────────

  const handleNameChange = React.useCallback(
    (value: string) => {
      setName(value);
      setNameError(null);
      // When adding a new column, auto-fill displayName if it hasn't been manually edited
      if (!isEditing && displayName === "") {
        setDisplayName(value);
      }
    },
    [isEditing, displayName]
  );

  // ── Validation ────────────────────────────────────────────────────────────

  const validate = React.useCallback((): boolean => {
    let valid = true;

    const trimmedName = name.trim();
    if (!trimmedName) {
      setNameError("Column name is required.");
      valid = false;
    } else if (!/^[A-Za-z_][A-Za-z0-9_]*$/.test(trimmedName)) {
      setNameError("Name must start with a letter or underscore and contain only letters, digits, and underscores.");
      valid = false;
    } else {
      setNameError(null);
    }

    const trimmedDisplayName = displayName.trim();
    if (!trimmedDisplayName) {
      setDisplayNameError("Display name is required.");
      valid = false;
    } else {
      setDisplayNameError(null);
    }

    return valid;
  }, [name, displayName]);

  // ── Submit ────────────────────────────────────────────────────────────────

  const handleConfirm = React.useCallback(async () => {
    if (!validate()) return;

    const formState: FormState = {
      name,
      displayName,
      description,
      required,
      columnType,
      numberMinimum,
      numberMaximum,
      dateTimeFormat,
      choices,
      allowTextEntry,
      currencyLocale,
    };

    const payload = buildUpsertPayload(formState);
    await onSave(payload);
  }, [
    validate,
    name,
    displayName,
    description,
    required,
    columnType,
    numberMinimum,
    numberMaximum,
    dateTimeFormat,
    choices,
    allowTextEntry,
    currencyLocale,
    onSave,
  ]);

  // ── Render ────────────────────────────────────────────────────────────────

  const selectedTypeOption = COLUMN_TYPE_OPTIONS.find((o) => o.value === columnType);
  const showTypeSpecificFields =
    columnType === "number" ||
    columnType === "dateTime" ||
    columnType === "choice" ||
    columnType === "currency";

  return (
    <Dialog open={open} onOpenChange={(_, data) => !data.open && onDismiss()}>
      <DialogSurface>
        <DialogBody>
          <DialogTitle>
            {isEditing ? "Edit Column" : "Add Column"}
          </DialogTitle>

          <DialogContent className={styles.dialogContent}>
            {error && (
              <MessageBar intent="error">
                <MessageBarBody>{error}</MessageBarBody>
              </MessageBar>
            )}

            {/* Core fields */}
            <Field
              label="Column Name (internal)"
              required
              hint="Used internally — letters, digits, underscores only. Cannot be changed after creation."
              validationState={nameError ? "error" : "none"}
              validationMessage={nameError ?? undefined}
            >
              <Input
                value={name}
                onChange={(_, data) => handleNameChange(data.value)}
                placeholder="e.g. CaseNumber"
                disabled={saving || isEditing}
                aria-required="true"
              />
            </Field>

            <Field
              label="Display Name"
              required
              hint="Label shown to users in the UI."
              validationState={displayNameError ? "error" : "none"}
              validationMessage={displayNameError ?? undefined}
            >
              <Input
                value={displayName}
                onChange={(_, data) => { setDisplayName(data.value); setDisplayNameError(null); }}
                placeholder="e.g. Case Number"
                disabled={saving}
                aria-required="true"
              />
            </Field>

            <Field label="Description" hint="Optional tooltip or help text for administrators.">
              <Textarea
                value={description}
                onChange={(_, data) => setDescription(data.value)}
                placeholder="Optional description…"
                disabled={saving}
                rows={2}
              />
            </Field>

            <Field label="Required">
              <Switch
                checked={required}
                onChange={(_, data) => setRequired(data.checked)}
                disabled={saving}
                label={required ? "Yes — this field must have a value" : "No — optional field"}
              />
            </Field>

            <Field label="Column Type" required>
              <Select
                value={columnType}
                onChange={(_, data) => setColumnType(data.value as ColumnEditorType)}
                disabled={saving || isEditing}
              >
                {COLUMN_TYPE_OPTIONS.map((opt) => (
                  <option key={opt.value} value={opt.value}>
                    {opt.label}
                  </option>
                ))}
              </Select>
            </Field>

            {selectedTypeOption && (
              <Text className={styles.infoText}>{selectedTypeOption.description}</Text>
            )}

            {/* Type-specific fields */}
            {showTypeSpecificFields && (
              <div className={styles.typeSpecificSection}>
                <Text className={styles.typeSectionTitle}>
                  {selectedTypeOption?.label ?? columnType} Options
                </Text>

                {columnType === "number" && (
                  <NumberFields
                    minimum={numberMinimum}
                    maximum={numberMaximum}
                    disabled={saving}
                    onChangeMinimum={setNumberMinimum}
                    onChangeMaximum={setNumberMaximum}
                  />
                )}

                {columnType === "dateTime" && (
                  <DateTimeFields
                    format={dateTimeFormat}
                    disabled={saving}
                    onChange={setDateTimeFormat}
                  />
                )}

                {columnType === "choice" && (
                  <ChoiceFields
                    choices={choices}
                    allowTextEntry={allowTextEntry}
                    disabled={saving}
                    onChoicesChange={setChoices}
                    onAllowTextEntryChange={setAllowTextEntry}
                  />
                )}

                {columnType === "currency" && (
                  <CurrencyFields
                    currencyLocale={currencyLocale}
                    disabled={saving}
                    onChange={setCurrencyLocale}
                  />
                )}
              </div>
            )}

            {(columnType === "personOrGroup" || columnType === "hyperlinkOrPicture") && (
              <Text className={styles.infoText}>
                {columnType === "personOrGroup"
                  ? "Person or Group columns reference Azure AD users or groups. The column value is stored as text (user ID or UPN)."
                  : "Hyperlink or Picture columns store a URL as text. Use this for link or image reference fields."}
              </Text>
            )}
          </DialogContent>

          <DialogActions>
            <Button
              appearance="primary"
              onClick={handleConfirm}
              disabled={saving}
              icon={saving ? <Spinner size="tiny" /> : undefined}
            >
              {saving
                ? isEditing ? "Saving…" : "Adding…"
                : isEditing ? "Save Changes" : "Add Column"}
            </Button>
            <Button appearance="secondary" onClick={onDismiss} disabled={saving}>
              Cancel
            </Button>
          </DialogActions>
        </DialogBody>
      </DialogSurface>
    </Dialog>
  );
};
