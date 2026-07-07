/**
 * JsonField — labeled monospace textarea for a JSON catalog column with
 * live authoring-time validation (FR-P4-04).
 *
 * The `validate` prop is one of the `schemaValidation.ts` validators (the
 * client twin of the server `OpenAiFunctionSchemaValidator`). Errors render
 * inline via Fluent `Field` validation state so a BA sees the problem at the
 * point of authoring — never at loop-projection time.
 *
 * ADR-021: Fluent v9 tokens only; no hardcoded colors.
 */

import { Field, Textarea, makeStyles, tokens } from '@fluentui/react-components';

const useStyles = makeStyles({
  textarea: {
    width: '100%',
    fontFamily: tokens.fontFamilyMonospace,
    fontSize: tokens.fontSizeBase200,
  },
});

export interface JsonFieldProps {
  id: string;
  label: string;
  hint?: string;
  value: string;
  placeholder?: string;
  rows?: number;
  /** Returns null when valid, otherwise the authoring error to render. */
  validate: (raw: string) => string | null;
  /** Error injected from the form-level validation pass (overrides live check). */
  externalError?: string;
  onChange: (value: string) => void;
}

export function JsonField({
  id,
  label,
  hint,
  value,
  placeholder,
  rows = 8,
  validate,
  externalError,
  onChange,
}: JsonFieldProps): JSX.Element {
  const styles = useStyles();
  const liveError = validate(value);
  const error = externalError ?? liveError ?? undefined;

  return (
    <Field
      label={label}
      hint={hint}
      validationState={error ? 'error' : 'none'}
      validationMessage={error}
    >
      <Textarea
        id={id}
        className={styles.textarea}
        value={value}
        placeholder={placeholder}
        resize="vertical"
        rows={rows}
        onChange={(_ev, data) => onChange(data.value)}
        aria-label={label}
      />
    </Field>
  );
}
