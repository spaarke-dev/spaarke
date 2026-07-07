/**
 * SchemaJsonEditor
 *
 * Validated editor for the two Action schema columns on the record form
 * (task 053 / FR-P4-04):
 *   sprk_analysisaction.sprk_inputschema      → typed-argument schema
 *   sprk_analysisaction.sprk_outputschemajson → Structured-Outputs schema
 *
 * Enforces the OpenAI function-parameters subset at authoring time — the
 * exact G-P3 round-1 outage shape (property-level "required": true) renders
 * an inline error instead of being savable silently.
 *
 * ADR-021: makeStyles + Fluent v9 tokens only. ADR-022: React 16 APIs.
 */

import * as React from 'react';
import { makeStyles, tokens, Field, Label, Text, Textarea, Badge } from '@fluentui/react-components';
import { validateSchemaForAuthoring } from '../utils/openAiSchemaValidator';

export interface ISchemaJsonEditorProps {
  /** Logical name of the bound column (drives the label/guidance). */
  boundAttributeName: string;
  /** Current schema JSON text. */
  value: string;
  /** Callback when value changes — propagates to PCF output. */
  onChange: (value: string) => void;
  /** Whether the editor is read-only. */
  readOnly?: boolean;
}

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
    fontFamily: tokens.fontFamilyMonospace,
    fontSize: tokens.fontSizeBase200,
  },
});

export const SchemaJsonEditor: React.FC<ISchemaJsonEditorProps> = ({
  boundAttributeName,
  value,
  onChange,
  readOnly = false,
}) => {
  const styles = useStyles();
  const isInput = boundAttributeName.toLowerCase() === 'sprk_inputschema';

  const label = isInput ? 'Input schema (typed capability args)' : 'Output schema (Structured Outputs)';
  const guidance = isInput
    ? 'OpenAI function-parameters subset. Required-ness goes ONLY in the object-level \'required\' array — ' +
      'property-level "required": true is invalid JSON Schema and previously took down every assistant turn ' +
      '(G-P3 round 1). Author mirrors first: infra/dataverse/inputschemas/.'
    : 'Structured-Outputs JSON Schema. Property declaration order = streaming emission order.';

  const error = validateSchemaForAuthoring(value);

  return (
    <div className={styles.container} data-testid="schema-json-editor">
      <div className={styles.labelRow}>
        <Label weight="semibold">{label}</Label>
        <Badge appearance="tint" size="small" color={error ? 'danger' : 'success'} data-testid="schema-validity-badge">
          {error ? 'Invalid' : 'Valid'}
        </Badge>
      </div>
      <Text className={styles.guidance}>{guidance}</Text>

      <Field validationState={error ? 'error' : 'none'} validationMessage={error ?? undefined}>
        <Textarea
          className={styles.textarea}
          value={value}
          disabled={readOnly}
          resize="vertical"
          rows={12}
          placeholder='{"type": "object", "properties": { ... }, "required": ["..."]}'
          onChange={(_ev, data) => onChange(data.value)}
          aria-label={label}
        />
      </Field>
    </div>
  );
};
