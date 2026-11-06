/**
 * Typed inputs for a rule's parameters (ebilling-r1 task 077): a bounded number, a single choice, a set of choices from a closed
 * vocabulary, or a list of codes, per rule type (`services/ruleTypes.ts`). Plain Fluent v9 inputs, semantic tokens only (ADR-021).
 */

import * as React from 'react';
import { Checkbox, Dropdown, Field, Input, Option, Textarea, makeStyles, tokens } from '@fluentui/react-components';
import type { ParamDef, ParamValues, RuleTypeDef } from '../services/ruleTypes';

const useStyles = makeStyles({
  root: { display: 'flex', flexDirection: 'column', gap: tokens.spacingVerticalM },
  checks: { display: 'flex', flexWrap: 'wrap', columnGap: tokens.spacingHorizontalL },
});

export interface ParamEditorProps {
  def: RuleTypeDef;
  values: ParamValues;
  errors: Record<string, string>;
  disabled?: boolean;
  onChange: (key: string, value: string | string[]) => void;
}

const ParamInput: React.FC<{
  param: ParamDef; value: string | string[] | undefined; error?: string; disabled?: boolean;
  onChange: (value: string | string[]) => void;
}> = ({ param, value, error, disabled, onChange }) => {
  const s = useStyles();
  const common = {
    label: param.label,
    hint: param.help,
    validationState: error ? ('error' as const) : ('none' as const),
    validationMessage: error,
    required: param.kind === 'number' || param.kind === 'codes' ? param.required : param.kind === 'multi' || param.kind === 'choice',
  };

  switch (param.kind) {
    case 'number':
      return (
        <Field {...common}>
          <Input
            type="number"
            inputMode="decimal"
            step={param.integer ? 1 : 'any'}
            value={typeof value === 'string' ? value : ''}
            disabled={disabled}
            onChange={(_, d) => onChange(d.value)}
            data-testid={`param-${param.key}`}
          />
        </Field>
      );
    case 'choice': {
      const current = typeof value === 'string' ? value : '';
      return (
        <Field {...common}>
          <Dropdown
            value={current}
            selectedOptions={current ? [current] : []}
            disabled={disabled}
            onOptionSelect={(_, d) => onChange(d.optionValue ?? '')}
            data-testid={`param-${param.key}`}
          >
            {param.options.map(o => (
              <Option key={o} value={o}>
                {o}
              </Option>
            ))}
          </Dropdown>
        </Field>
      );
    }
    case 'multi': {
      const picked = Array.isArray(value) ? value : [];
      return (
        <Field {...common}>
          <div className={s.checks} data-testid={`param-${param.key}`}>
            {param.options.map(o => (
              <Checkbox
                key={o}
                label={o}
                checked={picked.includes(o)}
                disabled={disabled}
                onChange={(_, d) => onChange(d.checked ? [...picked, o] : picked.filter(x => x !== o))}
              />
            ))}
          </div>
        </Field>
      );
    }
    case 'codes':
      return (
        <Field {...common}>
          <Textarea
            resize="vertical"
            value={typeof value === 'string' ? value : ''}
            disabled={disabled}
            onChange={(_, d) => onChange(d.value)}
            data-testid={`param-${param.key}`}
          />
        </Field>
      );
  }
};

export const ParamEditor: React.FC<ParamEditorProps> = ({ def, values, errors, disabled, onChange }) => {
  const s = useStyles();
  if (def.params.length === 0) return null;
  return (
    <div className={s.root} data-testid="param-editor">
      {def.params.map(p => (
        <ParamInput
          key={p.key}
          param={p}
          value={values[p.key]}
          error={errors[p.key]}
          disabled={disabled}
          onChange={v => onChange(p.key, v)}
        />
      ))}
    </div>
  );
};
