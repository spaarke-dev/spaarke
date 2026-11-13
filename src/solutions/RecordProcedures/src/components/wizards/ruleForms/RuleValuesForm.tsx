/**
 * The inputs of one rule type's form (ebilling-r1 task 084c): a number input, a dropdown, a set of checkboxes or a code list per field of
 * the type's schema, composed of ordinary Fluent v9 inputs (ADR-021; no new shared component). Presentational: the wizard owns the values,
 * the problems (its own checks and the server's refusal) and the published values the hint compares with.
 */

import * as React from 'react';
import {
  Checkbox,
  Dropdown,
  Field,
  FieldContextProvider,
  Input,
  Option,
  Textarea,
  makeStyles,
  tokens,
  useFieldContext_unstable,
} from '@fluentui/react-components';
import { fieldDiffers, fieldText, isShown } from './body';
import type { FormValues, RuleFormDef } from './types';

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
  /** Fields this change may not edit, with the reason (084c fix round 1, item 9): shown read-only, the reason in the hint. */
  locked?: Readonly<Record<string, string>>;
}

/**
 * A set of checkboxes inside one Field (084c fix round 1). A Field hands its control id to every control under it, so several checkboxes
 * would share one id and none would be named by its own label. The group takes the Field's label, hint and message; each checkbox, outside
 * the Field's context, keeps its own id and its own label.
 */
const CheckGroup: React.FC<{ className: string; label: string; testId: string; children: React.ReactNode }> = ({ className, label, testId, children }) => {
  const field = useFieldContext_unstable();
  const describedBy = [field?.validationMessageId, field?.hintId].filter(Boolean).join(' ') || undefined;
  return (
    <div
      className={className}
      role="group"
      aria-label={field?.labelId ? undefined : label}
      aria-labelledby={field?.labelId}
      aria-describedby={describedBy}
      aria-invalid={field?.validationState === 'error' ? true : undefined}
      data-testid={testId}
    >
      <FieldContextProvider value={undefined}>{children}</FieldContextProvider>
    </div>
  );
};

export const RuleValuesForm: React.FC<RuleValuesFormProps> =({ def, values, onChange, errors, published, locked = {} }) => {
  const s = useStyles();
  return (
    <div className={s.form} data-testid={`rule-form-${def.ruleType}`}>
      {def.fields
        .filter(f => isShown(f, values))
        .map(f => {
          const err = errors[f.key];
          const publishedText = published ? fieldText(f, published) : '';
          // A multi-choice field compares as a set (084c fix round 1, F1-3): the same picks in another order are not a difference.
          const differs = published !== null && fieldDiffers(f, values, published);
          const lockedReason = locked[f.key];
          const refused = (f.options ?? []).filter(o => o.refused);
          const hint = [f.help, lockedReason ?? null, differs ? `Published rule: ${publishedText || 'not set'}.` : null, refused.length > 0 ? refused[0].refused : null]
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
                  disabled={!!lockedReason}
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
                <CheckGroup className={s.checks} label={f.label} testId={`rule-field-${f.key}`}>
                  {(f.options ?? []).map(o => (
                    <Checkbox
                      key={o.value}
                      label={o.label}
                      checked={picked.includes(o.value)}
                      disabled={!!lockedReason}
                      onChange={(_, d) => onChange(f.key, d.checked ? [...picked, o.value] : picked.filter(x => x !== o.value))}
                    />
                  ))}
                </CheckGroup>
              </Field>
            );
          }

          if (f.kind === 'codes') {
            return (
              <Field key={f.key} {...common}>
                <Textarea
                  value={typeof v === 'string' ? v : ''}
                  onChange={(_, d) => onChange(f.key, d.value)}
                  disabled={!!lockedReason}
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
                disabled={!!lockedReason}
                data-testid={`rule-field-${f.key}`}
              />
            </Field>
          );
        })}
    </div>
  );
};
