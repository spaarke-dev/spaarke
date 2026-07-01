/**
 * TypedConfigForm — schema-driven config form renderer for PlaybookBuilder node properties.
 *
 * Reads an `ExecutorConfigSchema` (fetched from the BFF endpoint by
 * `services/executorSchemaService.ts`) and renders Fluent v9 inputs by field type:
 *
 *   | SchemaFieldType | Widget                                  |
 *   |-----------------|-----------------------------------------|
 *   | String          | Input (single-line)                     |
 *   | Number          | SpinButton                              |
 *   | Boolean         | Switch                                  |
 *   | Enum            | Dropdown (driven by `field.enumValues`) |
 *   | Object          | Textarea (JSON sub-editor, validated)   |
 *   | Array           | Textarea (JSON sub-editor, validated)   |
 *
 * Form-state syncs into `sprk_configjson` on every change per R7 FR-23 — the legacy field
 * is preserved for backward compatibility with deployed playbook records. The hand-crafted
 * forms (AiCompletionForm.tsx, CreateNotificationForm.tsx, etc.) remain untouched at this
 * task; tasks 084 (5 priority forms) and 085 (remaining 28 placeholders) replace them
 * incrementally on top of this renderer.
 *
 * Validation:
 *   - Required fields: empty string / undefined / null → "Required" error
 *   - Numbers: NaN or non-finite → "Must be a number"
 *   - Enums: value not in `enumValues` → error
 *   - Object/Array: invalid JSON → error
 * Errors render inline below each field; the form does NOT block saves (consistent with the
 * existing PlaybookBuilder model where downstream validation surfaces errors via NodeValidationBadge).
 *
 * Empty-schema handling:
 *   - `schema === undefined` → "No schema available" placeholder (paves R7 FR-27 / task 089)
 *   - `schema.fields === []`  → renders the description as a "no configuration required" hint
 *
 * @see projects/spaarke-ai-platform-unification-r7/notes/spikes/getconfigschema-design.md §4
 * @see ADR-006 — Fluent UI v9 only (no v8 patterns)
 * @see ADR-021 — Dark mode binding; semantic tokens only (no hex literals)
 */

import { memo, useCallback, useMemo } from 'react';
import {
  makeStyles,
  tokens,
  shorthands,
  Input,
  Label,
  Switch,
  SpinButton,
  Textarea,
  Dropdown,
  Option,
  Text,
} from '@fluentui/react-components';
import type {
  DropdownProps,
  OptionOnSelectData,
  SelectionEvents,
  SpinButtonChangeEvent,
  SpinButtonOnChangeData,
  SwitchOnChangeData,
} from '@fluentui/react-components';
import type { ConfigSchemaField, ExecutorConfigSchema, SchemaFieldType } from '../../services/executorSchemaService';

// ---------------------------------------------------------------------------
// Styles
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  form: {
    display: 'flex',
    flexDirection: 'column',
    ...shorthands.gap(tokens.spacingVerticalM),
  },
  header: {
    display: 'flex',
    flexDirection: 'column',
    ...shorthands.gap(tokens.spacingVerticalXS),
  },
  description: {
    color: tokens.colorNeutralForeground3,
  },
  field: {
    display: 'flex',
    flexDirection: 'column',
    ...shorthands.gap(tokens.spacingVerticalXS),
  },
  fieldHint: {
    color: tokens.colorNeutralForeground3,
    fontSize: tokens.fontSizeBase200,
  },
  fieldError: {
    color: tokens.colorPaletteRedForeground1,
    fontSize: tokens.fontSizeBase200,
  },
  jsonArea: {
    minHeight: '88px',
    fontFamily: tokens.fontFamilyMonospace,
    fontSize: tokens.fontSizeBase200,
  },
  emptyState: {
    color: tokens.colorNeutralForeground3,
    fontStyle: 'italic',
  },
});

// ---------------------------------------------------------------------------
// Props
// ---------------------------------------------------------------------------

export interface TypedConfigFormProps {
  /**
   * Stable identifier used to namespace input element IDs (e.g., `${nodeId}-${field.name}`).
   * Typically the canvas node id.
   */
  nodeId: string;
  /**
   * The schema fetched from the BFF endpoint (`executorSchemaService.getSchemaForExecutorType`).
   * `undefined` triggers the "no schema available" placeholder per R7 FR-27 prep.
   */
  schema: ExecutorConfigSchema | undefined;
  /**
   * Parsed current configuration value (i.e., `JSON.parse(node.data.configJson)`).
   * The form reads field defaults from `schema.fields[*].default` when a key is missing.
   */
  value: Record<string, unknown>;
  /**
   * Invoked with the new parsed config bag whenever any field changes. The host is expected
   * to serialize via `JSON.stringify` and write to `node.data.configJson` (FR-23 legacy field).
   */
  onChange: (newValue: Record<string, unknown>) => void;
  /**
   * If the host already serializes to `configJson` itself, it can pass `onConfigJsonChange`
   * for a more direct path. Both callbacks fire when supplied — `onChange` is the canonical
   * one for tests, `onConfigJsonChange` is convenience for the dialog wire-up.
   */
  onConfigJsonChange?: (json: string) => void;
}

// ---------------------------------------------------------------------------
// Validation
// ---------------------------------------------------------------------------

interface FieldValidation {
  hasError: boolean;
  message?: string;
}

function isEmpty(value: unknown): boolean {
  if (value === null || value === undefined) return true;
  if (typeof value === 'string') return value.trim() === '';
  if (Array.isArray(value)) return value.length === 0;
  return false;
}

function validateField(field: ConfigSchemaField, value: unknown): FieldValidation {
  if (field.required && isEmpty(value)) {
    return { hasError: true, message: 'Required' };
  }

  if (isEmpty(value)) {
    // Optional + empty → valid by definition.
    return { hasError: false };
  }

  switch (field.type) {
    case 'Number': {
      const n = typeof value === 'number' ? value : Number(value);
      if (!Number.isFinite(n)) {
        return { hasError: true, message: 'Must be a number' };
      }
      return { hasError: false };
    }
    case 'Enum': {
      if (typeof value !== 'string') {
        return { hasError: true, message: 'Must be one of the allowed values' };
      }
      if (Array.isArray(field.enumValues) && field.enumValues.length > 0 && !field.enumValues.includes(value)) {
        return { hasError: true, message: `Must be one of: ${field.enumValues.join(', ')}` };
      }
      return { hasError: false };
    }
    case 'Object':
    case 'Array': {
      if (typeof value === 'string') {
        try {
          const parsed = JSON.parse(value);
          if (field.type === 'Array' && !Array.isArray(parsed)) {
            return { hasError: true, message: 'Must be a JSON array' };
          }
          if (field.type === 'Object' && (typeof parsed !== 'object' || parsed === null || Array.isArray(parsed))) {
            return { hasError: true, message: 'Must be a JSON object' };
          }
          return { hasError: false };
        } catch {
          return { hasError: true, message: 'Invalid JSON' };
        }
      }
      // Non-string object/array stored directly — assume host already validated.
      return { hasError: false };
    }
    case 'Boolean':
    case 'String':
    default:
      return { hasError: false };
  }
}

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

/**
 * Resolve the value to display for a given field, falling back to the schema default
 * when the parsed config bag does not yet contain a key for this field.
 */
function resolveDisplayValue(field: ConfigSchemaField, value: Record<string, unknown>): unknown {
  if (Object.prototype.hasOwnProperty.call(value, field.name)) {
    return value[field.name];
  }
  return field.default;
}

function fieldKindFromType(type: SchemaFieldType): SchemaFieldType {
  // Switch statements with default cases require something to switch on — this helper
  // exists so the renderer's switch is exhaustive against the union type.
  return type;
}

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

export const TypedConfigForm = memo(function TypedConfigForm({
  nodeId,
  schema,
  value,
  onChange,
  onConfigJsonChange,
}: TypedConfigFormProps) {
  const styles = useStyles();

  const update = useCallback(
    (fieldName: string, newFieldValue: unknown) => {
      const next: Record<string, unknown> = { ...value, [fieldName]: newFieldValue };
      onChange(next);
      if (onConfigJsonChange) {
        try {
          onConfigJsonChange(JSON.stringify(next));
        } catch (err) {
          // Should be impossible with the validated bag, but never throw out of an onChange.
          console.warn('[TypedConfigForm] failed to serialize config bag', err);
        }
      }
    },
    [value, onChange, onConfigJsonChange]
  );

  const fieldValidations = useMemo(() => {
    if (!schema) return new Map<string, FieldValidation>();
    const map = new Map<string, FieldValidation>();
    for (const field of schema.fields) {
      map.set(field.name, validateField(field, resolveDisplayValue(field, value)));
    }
    return map;
  }, [schema, value]);

  // -------------------------------------------------------------------------
  // Empty-schema placeholder (paves R7 FR-27 / task 089)
  // -------------------------------------------------------------------------
  if (!schema) {
    return (
      <div className={styles.form}>
        <Text className={styles.emptyState}>
          No schema available for this executor type. Use the Configuration tab JSON editor as a fallback.
        </Text>
      </div>
    );
  }

  // -------------------------------------------------------------------------
  // Placeholder schema (executor declares Empty() — design doc §4)
  // -------------------------------------------------------------------------
  if (schema.fields.length === 0) {
    return (
      <div className={styles.form}>
        <div className={styles.header}>
          <Text weight="semibold">{schema.executorTypeName}</Text>
          <Text className={styles.description}>{schema.description || 'No configuration required.'}</Text>
        </div>
      </div>
    );
  }

  // -------------------------------------------------------------------------
  // Typed renderer
  // -------------------------------------------------------------------------
  return (
    <div className={styles.form}>
      {schema.description && (
        <div className={styles.header}>
          <Text className={styles.description}>{schema.description}</Text>
        </div>
      )}

      {schema.fields.map(field => {
        const displayValue = resolveDisplayValue(field, value);
        const validation = fieldValidations.get(field.name) ?? { hasError: false };
        const inputId = `${nodeId}-${field.name}`;

        return (
          <div key={field.name} className={styles.field}>
            <Label htmlFor={inputId} size="small" required={field.required}>
              {field.name}
            </Label>

            <TypedField
              field={field}
              inputId={inputId}
              displayValue={displayValue}
              onCommit={next => update(field.name, next)}
              isInvalid={validation.hasError}
              jsonAreaClassName={styles.jsonArea}
            />

            {field.description && !validation.hasError && <Text className={styles.fieldHint}>{field.description}</Text>}
            {validation.hasError && validation.message && (
              <Text role="alert" className={styles.fieldError}>
                {validation.message}
              </Text>
            )}
          </div>
        );
      })}
    </div>
  );
});

// ---------------------------------------------------------------------------
// Field-level renderer (one per SchemaFieldType)
// ---------------------------------------------------------------------------

interface TypedFieldProps {
  field: ConfigSchemaField;
  inputId: string;
  displayValue: unknown;
  onCommit: (next: unknown) => void;
  isInvalid: boolean;
  jsonAreaClassName: string;
}

function TypedField({ field, inputId, displayValue, onCommit, isInvalid, jsonAreaClassName }: TypedFieldProps) {
  switch (fieldKindFromType(field.type)) {
    case 'String': {
      const stringValue = typeof displayValue === 'string' ? displayValue : (displayValue ?? '').toString();
      return (
        <Input
          id={inputId}
          size="small"
          value={stringValue}
          onChange={(_, data) => onCommit(data.value)}
          aria-invalid={isInvalid || undefined}
        />
      );
    }

    case 'Number': {
      const numericValue = typeof displayValue === 'number' && Number.isFinite(displayValue) ? displayValue : 0;
      const handleNumberChange = (_e: SpinButtonChangeEvent, data: SpinButtonOnChangeData) => {
        if (data.value !== undefined && data.value !== null && Number.isFinite(data.value)) {
          onCommit(data.value);
          return;
        }
        if (typeof data.displayValue === 'string') {
          const parsed = Number(data.displayValue);
          if (Number.isFinite(parsed)) {
            onCommit(parsed);
          }
        }
      };
      return (
        <SpinButton
          id={inputId}
          size="small"
          value={numericValue}
          onChange={handleNumberChange}
          aria-invalid={isInvalid || undefined}
        />
      );
    }

    case 'Boolean': {
      const boolValue = Boolean(displayValue);
      const handleSwitchChange = (_e: React.ChangeEvent<HTMLInputElement>, data: SwitchOnChangeData) => {
        onCommit(data.checked);
      };
      return <Switch id={inputId} checked={boolValue} onChange={handleSwitchChange} />;
    }

    case 'Enum': {
      const enumValues = Array.isArray(field.enumValues) ? field.enumValues : [];
      const selectedValue = typeof displayValue === 'string' ? displayValue : '';
      const handleOptionSelect: DropdownProps['onOptionSelect'] = (
        _event: SelectionEvents,
        data: OptionOnSelectData
      ) => {
        onCommit(data.optionValue ?? '');
      };
      return (
        <Dropdown
          id={inputId}
          size="small"
          value={selectedValue}
          selectedOptions={selectedValue ? [selectedValue] : []}
          onOptionSelect={handleOptionSelect}
          aria-invalid={isInvalid || undefined}
        >
          {enumValues.map(option => (
            <Option key={option} value={option} text={option}>
              {option}
            </Option>
          ))}
        </Dropdown>
      );
    }

    case 'Object':
    case 'Array': {
      // Edit JSON as text — primary view per design doc §5 (Monaco deferred).
      // The bag value may be a parsed object/array; render the prettified form for editing,
      // and store the raw text back on every keystroke to preserve in-progress edits.
      let textValue: string;
      if (typeof displayValue === 'string') {
        textValue = displayValue;
      } else if (displayValue === undefined || displayValue === null) {
        textValue = field.type === 'Array' ? '[]' : '{}';
      } else {
        try {
          textValue = JSON.stringify(displayValue, null, 2);
        } catch {
          textValue = '';
        }
      }
      return (
        <Textarea
          id={inputId}
          size="small"
          className={jsonAreaClassName}
          value={textValue}
          onChange={e => onCommit(e.target.value)}
          aria-invalid={isInvalid || undefined}
          resize="vertical"
        />
      );
    }

    default: {
      // Forward-compat: server added a new SchemaFieldType the canvas hasn't shipped yet.
      // Mirrors design doc §7 wire-contract guidance ("unsupported field type" affordance).
      return (
        <Input
          id={inputId}
          size="small"
          value={typeof displayValue === 'string' ? displayValue : JSON.stringify(displayValue ?? '')}
          onChange={(_, data) => onCommit(data.value)}
          aria-invalid={isInvalid || undefined}
          placeholder="Unsupported field type — update PlaybookBuilder Code Page"
        />
      );
    }
  }
}
