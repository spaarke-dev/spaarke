/**
 * EvidenceLine — one evidence line: tier (Fact / Interpretation / Missing), text, and a quieter second
 * line with source, as-of and the clause it tested (or "context, not tested").
 *
 * Contract: FR-48 / HANDOFF section 1.4. A null fact renders as Missing, never as zero or omitted (H-5,
 * D-50). There is NO confidence prop: the classifier exposes no percentage (#5), so none is shown.
 * Fluent v9 semantic tokens only (ADR-021). Context-agnostic (ADR-012).
 *
 * Task: spaarke-ontology-platform-r1, task 057.
 */

import * as React from 'react';
import { Caption1, Text, makeStyles, mergeClasses, tokens } from '@fluentui/react-components';

/**
 * `Observation` is the tier name stored in `sprk_evidencerefs` today; it displays as Interpretation.
 */
export type EvidenceTier = 'Fact' | 'Interpretation' | 'Observation' | 'Missing';

export interface EvidenceLineProps {
  /** Stored tier. Ignored (forced to Missing) when `text` is null, undefined or empty. */
  tier: EvidenceTier;
  /** The value, already formatted by the caller. A real 0 is a fact and renders as "0"; null is Missing. */
  text: string | number | null | undefined;
  /** What is missing, shown when the value is null (e.g. "Spend snapshot"). */
  subject?: string;
  /** Where the value came from. */
  source?: string;
  /** When the value was true (ISO 8601); formatted for display. */
  asOf?: string | null;
  /** The clause this evidence tested. Absent or empty reads "context, not tested". */
  clause?: string | null;
  className?: string;
}

const useStyles = makeStyles({
  root: {
    display: 'grid',
    gridTemplateColumns: '112px minmax(0, 1fr)',
    columnGap: tokens.spacingHorizontalS,
    paddingTop: tokens.spacingVerticalXS,
    paddingBottom: tokens.spacingVerticalXS,
  },
  rootMissing: {
    backgroundColor: tokens.colorStatusWarningBackground1,
    borderRadius: tokens.borderRadiusMedium,
    paddingLeft: tokens.spacingHorizontalXS,
    paddingRight: tokens.spacingHorizontalXS,
  },
  tier: { color: tokens.colorNeutralForeground3 },
  tierFact: { color: tokens.colorBrandForeground1 },
  tierInterpretation: { color: tokens.colorPaletteBlueForeground2 },
  tierMissing: { color: tokens.colorStatusWarningForeground1, fontWeight: tokens.fontWeightSemibold },
  body: { display: 'flex', flexDirection: 'column', minWidth: 0 },
  detail: { color: tokens.colorNeutralForeground3 },
});

function isBlank(text: EvidenceLineProps['text']): boolean {
  return text === null || text === undefined || (typeof text === 'string' && text.trim() === '');
}

function formatAsOf(asOf: string | null | undefined): string | null {
  if (!asOf) return null;
  const d = new Date(asOf);
  return Number.isNaN(d.getTime()) ? asOf : d.toLocaleDateString();
}

export const EvidenceLine: React.FC<EvidenceLineProps> = ({ tier, text, subject, source, asOf, clause, className }) => {
  const styles = useStyles();
  const missing = isBlank(text);
  const shown: 'Fact' | 'Interpretation' | 'Missing' = missing
    ? 'Missing'
    : tier === 'Fact'
      ? 'Fact'
      : 'Interpretation';
  const tierClass =
    shown === 'Fact' ? styles.tierFact : shown === 'Interpretation' ? styles.tierInterpretation : styles.tierMissing;

  const bodyText = missing ? (subject ? `${subject}: no value recorded` : 'No value recorded') : String(text);
  const asOfText = formatAsOf(asOf);
  const detail = [
    source,
    asOfText ? `as of ${asOfText}` : null,
    clause?.trim() ? `tested: ${clause.trim()}` : 'context, not tested',
  ]
    .filter((p): p is string => !!p)
    .join(' · ');

  return (
    <div
      className={mergeClasses(styles.root, missing && styles.rootMissing, className)}
      data-testid="evidence-line"
      data-tier={shown}
    >
      <Caption1 className={mergeClasses(styles.tier, tierClass)}>{shown}</Caption1>
      <div className={styles.body}>
        <Text>{bodyText}</Text>
        <Caption1 className={styles.detail}>{detail}</Caption1>
      </div>
    </div>
  );
};

EvidenceLine.displayName = 'EvidenceLine';
