/**
 * RecordTemplateHeader — the record header of the R4 record template (spaarke-SPA-external-access-platform-r4 task 003,
 * FR-02): the record name on the left and up to three key facts on the right, value over label, plus an optional
 * "Your access" fact (task 006 supplies it, FR-07).
 *
 * WHY NOT `RecordHeaderShell` (task 003 step 5). The shared shell is the model-driven form header: a `HeaderToolbar` row
 * (form-section title at body size plus an icon strip) over a `FieldGrid` body whose cells are form-input styled, at a
 * fixed 2 or 3 columns with no narrow-width layout. Reusing it unchanged would give an empty second icon strip under the
 * record toolbar, and the facts would still be built here (the field renderers do not stack at 375px, NFR-03). The header
 * is therefore built from Fluent primitives in the SPA, following the prototype the owner accepted (2026-11-08).
 *
 * The facts wrap onto a new line on narrow screens instead of scrolling sideways. Read-only by construction.
 *
 * ADR-021: Fluent v9 semantic tokens via `makeStyles` only; light, dark, high-contrast and Teams themes.
 */
import * as React from 'react';
import { makeStyles, tokens } from '@fluentui/react-components';

export interface RecordTemplateHeaderFact {
  label: string;
  value: string;
}

export interface RecordTemplateHeaderProps {
  /** The record's display name (the heading). */
  name: string;
  /** Singular type label, for the facts list's accessible name ("Matter summary"). */
  typeLabel: string;
  /** Up to three facts, already formatted. */
  facts: readonly RecordTemplateHeaderFact[];
  /** The caller's right on this record (task 006, FR-07). Absent = no "Your access" fact. */
  access?: React.ReactNode;
}

const useStyles = makeStyles({
  header: {
    display: 'flex',
    flexWrap: 'wrap',
    alignItems: 'center',
    justifyContent: 'space-between',
    columnGap: tokens.spacingHorizontalXXL,
    rowGap: tokens.spacingVerticalS,
    paddingBlock: tokens.spacingVerticalM,
    paddingInline: tokens.spacingHorizontalL,
    backgroundColor: tokens.colorNeutralBackground1,
    borderBottom: `${tokens.strokeWidthThin} solid ${tokens.colorNeutralStroke2}`,
  },
  name: {
    margin: 0,
    flexGrow: 1,
    flexShrink: 1,
    flexBasis: '240px',
    minWidth: 0,
    fontSize: tokens.fontSizeBase500,
    lineHeight: tokens.lineHeightBase500,
    fontWeight: tokens.fontWeightSemibold,
    color: tokens.colorNeutralForeground1,
    overflow: 'hidden',
    textOverflow: 'ellipsis',
    whiteSpace: 'nowrap',
  },
  facts: {
    display: 'flex',
    flexWrap: 'wrap',
    rowGap: tokens.spacingVerticalS,
    margin: 0,
    minWidth: 0,
  },
  fact: {
    // Value over label; `column-reverse` keeps the <dt> before its <dd> for screen readers.
    display: 'flex',
    flexDirection: 'column-reverse',
    justifyContent: 'flex-end',
    gap: tokens.spacingVerticalXXS,
    minWidth: 0,
    paddingInline: tokens.spacingHorizontalL,
    borderLeft: `${tokens.strokeWidthThin} solid ${tokens.colorNeutralStroke2}`,
  },
  factLabel: {
    margin: 0,
    fontSize: tokens.fontSizeBase200,
    lineHeight: tokens.lineHeightBase200,
    color: tokens.colorNeutralForeground3,
  },
  factValue: {
    margin: 0,
    fontSize: tokens.fontSizeBase300,
    lineHeight: tokens.lineHeightBase300,
    fontWeight: tokens.fontWeightSemibold,
    color: tokens.colorNeutralForeground1,
    overflowWrap: 'anywhere',
  },
});

export const RecordTemplateHeader: React.FC<RecordTemplateHeaderProps> = ({ name, typeLabel, facts, access }) => {
  const styles = useStyles();
  return (
    <div className={styles.header} data-testid="record-template-header">
      <h2 className={styles.name} title={name}>
        {name}
      </h2>
      {(facts.length > 0 || access !== undefined) && (
        <dl className={styles.facts} aria-label={`${typeLabel} summary`}>
          {facts.map(fact => (
            <div key={fact.label} className={styles.fact}>
              <dt className={styles.factLabel}>{fact.label}</dt>
              <dd className={styles.factValue}>{fact.value}</dd>
            </div>
          ))}
          {access !== undefined && (
            <div className={styles.fact} data-testid="record-template-access">
              <dt className={styles.factLabel}>Your access</dt>
              <dd className={styles.factValue}>{access}</dd>
            </div>
          )}
        </dl>
      )}
    </div>
  );
};

export default RecordTemplateHeader;
