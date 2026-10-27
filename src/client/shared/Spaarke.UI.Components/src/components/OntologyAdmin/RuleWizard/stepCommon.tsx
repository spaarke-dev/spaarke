/**
 * stepCommon - what the rule wizard's steps share (spaarke-ontology-platform-r1 task 102): the props every step takes, the field
 * wrapper that shows a BFF refusal at the field it concerns, and the step layout styles.
 *
 * Fluent v9 tokens only (ADR-021).
 */

import * as React from 'react';
import { Field, makeStyles, tokens } from '@fluentui/react-components';
import type { AuthoringCatalog, FieldErrors } from './authoringService';
import type { RuleDraft } from './ruleDraft';

export interface StepBaseProps {
  catalog: AuthoringCatalog;
  draft: RuleDraft;
  /** Change the draft. Every change goes through here so the wizard can tell the test no longer matches. */
  onChange: (change: (draft: RuleDraft) => RuleDraft) => void;
  /** The BFF's refusals that are still current for this draft, by field. */
  errors: FieldErrors;
  /** The wizard is finished (saved or published): nothing can be edited any more. */
  locked: boolean;
}

export const useStepStyles = makeStyles({
  step: {
    display: 'flex',
    flexDirection: 'column',
    rowGap: tokens.spacingVerticalL,
    minWidth: 0,
  },
  intro: {
    color: tokens.colorNeutralForeground3,
  },
  group: {
    display: 'flex',
    flexDirection: 'column',
    rowGap: tokens.spacingVerticalS,
    minWidth: 0,
  },
  inline: {
    display: 'flex',
    flexWrap: 'wrap',
    alignItems: 'flex-start',
    columnGap: tokens.spacingHorizontalS,
    rowGap: tokens.spacingVerticalXS,
  },
  muted: {
    color: tokens.colorNeutralForeground3,
  },
  error: {
    color: tokens.colorPaletteRedForeground1,
  },
  mono: {
    fontFamily: tokens.fontFamilyMonospace,
    overflowWrap: 'anywhere',
  },
  facts: {
    display: 'grid',
    gridTemplateColumns: 'max-content minmax(0, 1fr)',
    columnGap: tokens.spacingHorizontalL,
    rowGap: tokens.spacingVerticalXXS,
    margin: 0,
  },
  fact: {
    margin: 0,
  },
});

/** The messages of a field as one validation message, or undefined when there are none. */
export function errorMessage(errors: FieldErrors, field: string): React.ReactElement | undefined {
  const messages = field in errors ? errors[field] : [];
  if (messages.length === 0) {
    return undefined;
  }
  return <span data-testid={`field-error-${field}`}>{messages.join(' ')}</span>;
}

export interface RefusableFieldProps {
  label: string;
  field: string;
  /** The BFF field whose refusal shows here, when it is not `field` (the quiet window is refused as part of the rule body). */
  errorField?: string;
  errors: FieldErrors;
  /** A problem the wizard itself found (an input that cannot be written into the rule), shown when the BFF has said nothing. */
  localProblem?: string | null;
  required?: boolean;
  hint?: string;
  children: React.ReactElement;
}

/**
 * A labelled control that shows the BFF's refusal for `field` beneath it. The refusal is the BFF's own message, so a code the BFF
 * refuses (a column outside the authorable list, a duplicate action) reads the same here whether it came from validate or from save.
 */
export const RefusableField: React.FC<RefusableFieldProps> = ({ label, field, errorField, errors, localProblem, required, hint, children }) => {
  const server = errorMessage(errors, errorField === undefined ? field : errorField);
  const message = server ?? (localProblem ? <span data-testid={`field-problem-${field}`}>{localProblem}</span> : undefined);
  return (
    <Field
      label={label}
      required={required}
      hint={hint}
      validationState={message === undefined ? 'none' : 'error'}
      validationMessage={message}
    >
      {children}
    </Field>
  );
};

RefusableField.displayName = 'RefusableField';

/** A definition list of label and value pairs, as the rule page shows its facts. */
export const Facts: React.FC<{ rows: ReadonlyArray<readonly [string, React.ReactNode]> }> = ({ rows }) => {
  const styles = useStepStyles();
  return (
    <dl className={styles.facts}>
      {rows.map(([term, value]) => (
        <React.Fragment key={term}>
          <dt className={styles.fact}>
            <span className={styles.muted}>{term}</span>
          </dt>
          <dd className={styles.fact}>{value}</dd>
        </React.Fragment>
      ))}
    </dl>
  );
};
