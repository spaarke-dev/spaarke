/**
 * BindingConfigEditor
 *
 * The `sprk_playbookconsumer` (Binding) variant of ScopeConfigEditor
 * (task 053 / FR-P4-04). The Binding table is THE only routing surface on the
 * platform (ADR-039); this editor gives the record form validated authoring
 * of the Binding contract columns the control can be bound to:
 *
 *   sprk_chiptransitions   → chip-transitions JSON (pinned shape, D4 / Click path)
 *   sprk_oneventbindings   → event memberships JSON (canonical §7.1)
 *   sprk_matchconditions   → flat predicate JSON
 *   sprk_tooldescription   → intent-surface text with authoring guidance
 *   anything else          → generic validated JSON editor
 *
 * Validation twins the server `OpenAiFunctionSchemaValidator` family — an
 * invalid shape shows an inline error at the point of authoring (the G-P3
 * round-1 class of outage can no longer be typed into the form silently).
 *
 * ADR-021: makeStyles + Fluent v9 tokens only. ADR-022: React 16 APIs.
 */

import * as React from 'react';
import { makeStyles, tokens, Field, Label, Text, Textarea, Badge } from '@fluentui/react-components';
import {
  validateChipTransitionsJson,
  validateMatchConditionsJson,
  validateOnEventBindingsJson,
} from '../utils/openAiSchemaValidator';

// ─────────────────────────────────────────────────────────────────────────────
// Types
// ─────────────────────────────────────────────────────────────────────────────

export interface IBindingConfigEditorProps {
  /** Logical name of the bound column (e.g. "sprk_chiptransitions"). */
  boundAttributeName: string;
  /** Current field value from the bound property. */
  value: string;
  /** Callback when value changes — propagates to PCF output. */
  onChange: (value: string) => void;
  /** Whether the editor is read-only. */
  readOnly?: boolean;
}

interface ColumnSpec {
  label: string;
  guidance: string;
  placeholder: string;
  validate: (raw: string) => string | null;
  monospace: boolean;
}

// ─────────────────────────────────────────────────────────────────────────────
// Column registry (the Binding contract columns this control can bind to)
// ─────────────────────────────────────────────────────────────────────────────

function validateGenericJson(raw: string): string | null {
  if (raw.trim() === '') return null;
  try {
    JSON.parse(raw);
    return null;
  } catch (err) {
    return `not valid JSON: ${err instanceof Error ? err.message : 'parse error'}`;
  }
}

const COLUMN_SPECS: Record<string, ColumnSpec> = {
  sprk_chiptransitions: {
    label: 'Chip transitions',
    guidance:
      'Curated next-step chips (Click path). Array of {target_binding_id, chip_label, bulk_chip_label?, ' +
      'requires_attachments?, prefill_slots?}. Malformed maker JSON degrades to NO chips at runtime — ' +
      'this editor blocks the malformed shape instead.',
    placeholder:
      '[{"target_binding_id": "<binding guid>", "chip_label": "Summarize this document", ' +
      '"bulk_chip_label": "Summarize", "requires_attachments": true}]',
    validate: validateChipTransitionsJson,
    monospace: true,
  },
  sprk_oneventbindings: {
    label: 'On-event bindings',
    guidance:
      'Event-path memberships: array of {event, order}. Closed platform vocabulary (e.g. document_uploaded); ' +
      'lower order runs first within the event composite.',
    placeholder: '[{"event": "document_uploaded", "order": 1}]',
    validate: validateOnEventBindingsJson,
    monospace: true,
  },
  sprk_matchconditions: {
    label: 'Match conditions',
    guidance: 'Flat JSON predicate (key → string | string[]). Empty always matches.',
    placeholder: '{"entityType": "sprk_matter"}',
    validate: validateMatchConditionsJson,
    monospace: true,
  },
  sprk_tooldescription: {
    label: 'Tool description',
    guidance:
      'The intent surface the agent loop sees when this Binding projects as a capability tool. Describe WHEN ' +
      'to use the capability in plain language — the loop matches user intent against this text.',
    placeholder: 'Summarize the files uploaded to this chat session into TL;DR bullets and a narrative summary.',
    validate: () => null,
    monospace: false,
  },
};

const FALLBACK_SPEC: ColumnSpec = {
  label: 'Binding column',
  guidance: 'Validated JSON editor for a sprk_playbookconsumer column.',
  placeholder: '',
  validate: validateGenericJson,
  monospace: true,
};

// ─────────────────────────────────────────────────────────────────────────────
// Styles
// ─────────────────────────────────────────────────────────────────────────────

const useStyles = makeStyles({
  container: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalS,
    width: '100%',
    boxSizing: 'border-box',
  },
  labelRow: {
    display: 'flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalS,
  },
  guidance: {
    color: tokens.colorNeutralForeground3,
    fontSize: tokens.fontSizeBase200,
  },
  textarea: {
    width: '100%',
    fontSize: tokens.fontSizeBase200,
  },
  monospace: {
    fontFamily: tokens.fontFamilyMonospace,
  },
});

// ─────────────────────────────────────────────────────────────────────────────
// Component
// ─────────────────────────────────────────────────────────────────────────────

export const BindingConfigEditor: React.FC<IBindingConfigEditorProps> = ({
  boundAttributeName,
  value,
  onChange,
  readOnly = false,
}) => {
  const styles = useStyles();
  const attribute = boundAttributeName.toLowerCase();
  const spec = COLUMN_SPECS[attribute] ?? FALLBACK_SPEC;

  const error = spec.validate(value);

  return (
    <div className={styles.container} data-testid="binding-config-editor">
      <div className={styles.labelRow}>
        <Label weight="semibold">{spec.label}</Label>
        <Badge appearance="tint" size="small" color={error ? 'danger' : 'success'} data-testid="binding-validity-badge">
          {error ? 'Invalid' : 'Valid'}
        </Badge>
      </div>
      <Text className={styles.guidance}>{spec.guidance}</Text>

      <Field validationState={error ? 'error' : 'none'} validationMessage={error ?? undefined}>
        <Textarea
          className={spec.monospace ? `${styles.textarea} ${styles.monospace}` : styles.textarea}
          value={value}
          disabled={readOnly}
          resize="vertical"
          rows={8}
          placeholder={spec.placeholder}
          onChange={(_ev, data) => onChange(data.value)}
          aria-label={spec.label}
        />
      </Field>
    </div>
  );
};
