/**
 * The inputs of one rule type's form (ebilling-r1 task 084c): a number input, a dropdown, a set of checkboxes or a code list per field of
 * the type's schema, composed of ordinary Fluent v9 inputs (ADR-021; no new shared component). Presentational: the wizard owns the values,
 * the problems (its own checks and the server's refusal) and the published values the hint compares with.
 */

import * as React from 'react';
import { Checkbox, Dropdown, Field, Input, Option, Textarea, makeStyles, tokens } from '@fluentui/react-components';
import { fieldText, isShown } from './body';
import type { FormField, FormValues, RuleFormDef } from './types';

const useStyles = makeStyles({
  form: { display: 'flex', flexDirection: 'column', rowGap: tokens.spacingVerticalL },
  checks: { display: 'flex', flexDirection: 'column', rowGap: tokens.spacingVerticalXXS },
});

export interface RuleValuesFormProps {
  def: RuleFormDef;
  values: FormValues;
  onChange: (key: string, value: string | string[]) => void;
  /** Problems by field key (the wizard's checks, or the server's refusal). */
  errors: Readonly<Record<string, string | undefined>>;
  /** The published rule's body: a field whose value differs from it says what the published rule has. Null when it could not be read. */
  published: Record<string, unknown> | null;
}

/** What a field currently holds, as the same kind of text `fieldText` gives for a body, so the two can be compared. */
function valueText(f: FormField, values: FormValues): string {
  const v = values[f.key];
  if (f.kind === 'multi') return (f.options ?? []).filter(o => Array.isArray(v) && v.includes(o.value)).map(o => o.label).join(', ');
  if (f.kind === 'codes') return typeof v === 'string' ? v.split(/[\s,;]+/).filter(Boolean).join(', ') : '';
  if (f.kind === 'choice') return f.options?.find(o => o.value === v)?.label ?? '';
  return typeof v === 'string' ? v.trim() : '';
}

export const RuleValuesForm: React.FC<RuleValuesFormProps> = ({ def, values, onChange, errors, published }) => {
  const s = useStyles();
  return (
    <div className={s.form} data-testid={`rule-form-${def.ruleType}`}>
      {def.fields
        .filter(f => isShown(f, values))
        .map(f => {
          const err = errors[f.key];
          const publishedText = published ? fieldText(f, published) : '';
          const differs = published !== null && publishedText !== valueText(f, values);
          const refused = (f.options ?? []).filter(o => o.refused);
          const hint = [f.help, differs ? `Published rule: ${publishedText || 'not set'}.` : null, refused.length > 0 ? refused[0].refused : null]
            .filter(Boolean)
            .join(' ');
          const common = { label: f.label, hint, required: f.required === true, validationMessage: err, validationState: err ? ('error' as const) : ('none' as const) };
          const v = values[f.key];

          if (f.kind === 'choice') {
            const selected = f.options?.find(o => o.value === v);
            return (
              <Field key={f.key} {...common}>
                <Dropdown
                  value={selected?.label ?? ''}
                  selectedOptions={typeof v === 'string' && v ? [v] : []}
                  onOptionSelect={(_, d) => onChange(f.key, d.optionValue ?? '')}
                  data-testid={`rule-field-${f.key}`}
                >
                  {(f.options ?? []).map(o => (
                    <Option key={o.value} value={o.value} text={o.label} disabled={!!o.refused}>
                      {o.refused ? `${o.label} (not available yet)` : o.label}
                    </Option>
                  ))}
                </Dropdown>
              </Field>
            );
          }

          if (f.kind === 'multi') {
            const picked = Array.isArray(v) ? v : [];
            return (
              <Field key={f.key} {...common}>
                <div className={s.checks} role="group" aria-label={f.label} data-testid={`rule-field-${f.key}`}>
                  {(f.options ?? []).map(o => (
                    <Checkbox
                      key={o.value}
                      label={o.label}
                      checked={picked.includes(o.value)}
                      onChange={(_, d) => onChange(f.key, d.checked ? [...picked, o.value] : picked.filter(x => x !== o.value))}
                    />
                  ))}
                </div>
              </Field>
            );
          }

          if (f.kind === 'codes') {
            return (
              <Field key={f.key} {...common}>
                <Textarea
                  value={typeof v === 'string' ? v : ''}
                  onChange={(_, d) => onChange(f.key, d.value)}
                  resize="vertical"
                  data-testid={`rule-field-${f.key}`}
                />
              </Field>
            );
          }

          return (
            <Field key={f.key} {...common}>
              <Input
                type="text"
                inputMode={f.integer ? 'numeric' : 'decimal'}
                value={typeof v === 'string' ? v : ''}
                onChange={(_, d) => onChange(f.key, d.value)}
                data-testid={`rule-field-${f.key}`}
              />
            </Field>
          );
        })}
    </div>
  );
};
