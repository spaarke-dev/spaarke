/**
 * intakeFormParts - the small Fluent v9 form rows the intake wizards share (r3 task 052).
 *
 * Every row is a Fluent `Field` around a Fluent control, so labels, required marks, hints and the error text come from
 * the library, and colours come from semantic tokens only (ADR-021). Rows take their error as a plain string and show it
 * only when `showError` is true (the field was left or the step was tried), so a fresh form does not open covered in red.
 */
import * as React from 'react';
import {
  Checkbox,
  Field,
  FieldContextProvider,
  Input,
  Radio,
  RadioGroup,
  Select,
  Text,
  Textarea,
  makeStyles,
  tokens,
} from '@fluentui/react-components';
import { CheckmarkCircleFilled } from '@fluentui/react-icons';
import type { CodedOption } from './ndaModel';

const useStyles = makeStyles({
  rows: { display: 'flex', flexDirection: 'column', gap: tokens.spacingVerticalM },
  group: { display: 'flex', flexDirection: 'column', gap: tokens.spacingVerticalXS },
  review: {
    display: 'grid',
    gridTemplateColumns: 'minmax(120px, 30%) 1fr',
    columnGap: tokens.spacingHorizontalL,
    rowGap: tokens.spacingVerticalS,
    margin: 0,
  },
  reviewLabel: { color: tokens.colorNeutralForeground3 },
  reviewValue: { margin: 0, color: tokens.colorNeutralForeground1, overflowWrap: 'anywhere', whiteSpace: 'pre-wrap' },
  successIcon: { color: tokens.colorPaletteGreenForeground1, fontSize: '48px', display: 'flex' },
  successBody: { display: 'flex', flexDirection: 'column', gap: tokens.spacingVerticalS },
});

/** Stacks a step's rows with the library's vertical rhythm. */
export const FormRows: React.FC<{ children: React.ReactNode }> = ({ children }) => {
  const s = useStyles();
  return <div className={s.rows}>{children}</div>;
};

interface RowBase {
  label: string;
  /** The error for this field, or undefined when valid. */
  error?: string;
  /** Show `error` (the field was left, or the user tried to go on). */
  showError: boolean;
  required?: boolean;
  hint?: string;
}

const validation = (p: RowBase): { validationState: 'error' | 'none'; validationMessage?: string } =>
  p.showError && p.error ? { validationState: 'error', validationMessage: p.error } : { validationState: 'none' };

export interface TextRowProps extends RowBase {
  value: string;
  onChange: (value: string) => void;
  onBlur: () => void;
  maxLength: number;
  multiline?: boolean;
  rows?: number;
  type?: 'text' | 'email' | 'date';
  min?: string;
  max?: string;
}

export const TextRow: React.FC<TextRowProps> = p => (
  <Field label={p.label} required={p.required} hint={p.hint} {...validation(p)}>
    {p.multiline ? (
      <Textarea
        value={p.value}
        rows={p.rows ?? 4}
        resize="vertical"
        maxLength={p.maxLength}
        onChange={(_e, d) => p.onChange(d.value)}
        onBlur={p.onBlur}
      />
    ) : (
      <Input
        type={p.type ?? 'text'}
        value={p.value}
        maxLength={p.type === 'date' ? undefined : p.maxLength}
        min={p.min}
        max={p.max}
        onChange={(_e, d) => p.onChange(d.value)}
        onBlur={p.onBlur}
      />
    )}
  </Field>
);

export interface ChoiceRowProps extends RowBase {
  options: readonly CodedOption[];
  value: string;
  onChange: (code: string) => void;
  onBlur: () => void;
}

/** A single choice as radio buttons (for short lists). */
export const RadioRow: React.FC<ChoiceRowProps> = p => (
  <Field label={p.label} required={p.required} hint={p.hint} {...validation(p)}>
    <RadioGroup value={p.value} onChange={(_e, d) => p.onChange(d.value)} onBlur={p.onBlur}>
      {p.options.map(o => (
        <Radio key={o.code} value={o.code} label={o.label} />
      ))}
    </RadioGroup>
  </Field>
);

/** A single choice as a drop-down (for longer lists). */
export const SelectRow: React.FC<ChoiceRowProps> = p => (
  <Field label={p.label} required={p.required} hint={p.hint} {...validation(p)}>
    <Select value={p.value} onChange={(_e, d) => p.onChange(d.value)} onBlur={p.onBlur}>
      <option value="">Select…</option>
      {p.options.map(o => (
        <option key={o.code} value={o.code}>
          {o.label}
        </option>
      ))}
    </Select>
  </Field>
);

export interface CheckboxRowProps extends RowBase {
  options: readonly CodedOption[];
  value: readonly string[];
  onChange: (codes: string[]) => void;
}

/**
 * Several choices as checkboxes. The checkboxes are taken OUT of the Field's control context: a Fluent `Checkbox` inside a
 * `Field` otherwise takes the Field's one control id and `aria-labelledby`, so every checkbox would share an id (a label
 * click toggles the first box) and be named by the group label instead of its own.
 */
export const CheckboxRow: React.FC<CheckboxRowProps> = p => {
  const s = useStyles();
  return (
    <Field label={p.label} required={p.required} hint={p.hint} {...validation(p)}>
      <div role="group" aria-label={p.label} className={s.group}>
        <FieldContextProvider value={undefined}>
          {p.options.map(o => (
            <Checkbox
              key={o.code}
              label={o.label}
              checked={p.value.includes(o.code)}
              onChange={(_e, d) =>
                p.onChange(
                  d.checked ? [...p.value.filter(c => c !== o.code), o.code] : p.value.filter(c => c !== o.code)
                )
              }
            />
          ))}
        </FieldContextProvider>
      </div>
    </Field>
  );
};

export interface ConfirmRowProps {
  /** The statement the requester agrees to (the checkbox's label). */
  label: string;
  checked: boolean;
  onChange: (checked: boolean) => void;
  error?: string;
  showError: boolean;
}

/** One checkbox that must be ticked to go on (a confirmation statement). */
export const ConfirmRow: React.FC<ConfirmRowProps> = p => (
  <Field {...validation({ label: p.label, error: p.error, showError: p.showError })}>
    <Checkbox label={p.label} checked={p.checked} onChange={(_e, d) => p.onChange(d.checked === true)} />
  </Field>
);

/** The read-only summary on a wizard's last step. */
export const ReviewList: React.FC<{ rows: Array<{ label: string; value: string }> }> = ({ rows }) => {
  const s = useStyles();
  return (
    <dl className={s.review} aria-label="Summary of your answers">
      {rows.map(r => (
        <React.Fragment key={r.label}>
          <dt className={s.reviewLabel}>
            <Text size={200}>{r.label}</Text>
          </dt>
          <dd className={s.reviewValue}>
            <Text>{r.value}</Text>
          </dd>
        </React.Fragment>
      ))}
    </dl>
  );
};

/** The check mark on a success screen. */
export const SuccessIcon: React.FC = () => {
  const s = useStyles();
  return (
    <span className={s.successIcon}>
      <CheckmarkCircleFilled />
    </span>
  );
};

/** Paragraph stack for a success screen's body. */
export const SuccessBody: React.FC<{ children: React.ReactNode }> = ({ children }) => {
  const s = useStyles();
  return <div className={s.successBody}>{children}</div>;
};

/**
 * Which fields have been left. A field's error is shown once it is `touched` (the Next button stays disabled until the
 * step is valid, so the message beside the field is what tells the requester why).
 */
export function useTouched(): { shows: (key: string) => boolean; touch: (key: string) => void } {
  const [touched, setTouched] = React.useState<ReadonlySet<string>>(() => new Set());
  const touch = React.useCallback(
    (key: string) => setTouched(prev => (prev.has(key) ? prev : new Set(prev).add(key))),
    []
  );
  const shows = React.useCallback((key: string) => touched.has(key), [touched]);
  return { shows, touch };
}
