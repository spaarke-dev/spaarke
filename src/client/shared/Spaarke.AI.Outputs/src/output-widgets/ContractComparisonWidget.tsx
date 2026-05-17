/**
 * ContractComparisonWidget
 *
 * Renders a two-column comparison of contract clauses. Each clause pair is
 * shown side-by-side (left / right label). Rows where the two clause texts
 * differ are highlighted using Fluent v9 warning background tokens.
 *
 * NOT PCF-safe — requires React 19 and Fluent UI v9.
 *
 * Data is passed via props — no direct API calls inside this widget.
 *
 * @see ADR-021 — Fluent UI v9 design system (no hard-coded colors)
 * @see ADR-012 — Shared component library
 */

import * as React from 'react';
import { makeStyles, mergeClasses, shorthands, tokens, Text, Badge, Spinner } from '@fluentui/react-components';
import type { OutputWidgetProps } from '../types';

// ---------------------------------------------------------------------------
// Data types
// ---------------------------------------------------------------------------

export interface ContractClausePair {
  /** Stable identifier for this clause (e.g. "clause-2-3"). */
  id: string;
  /** Clause text from the left (original) document. */
  left: string;
  /** Clause text from the right (proposed / revised) document. */
  right: string;
  /**
   * When true, the two texts differ and the row is highlighted.
   * The AI determines this flag; the widget renders it without re-diffing.
   */
  hasDelta: boolean;
}

export interface ContractComparisonData {
  /** Label for the left column (e.g. "Original", "Counterparty Draft"). */
  leftLabel: string;
  /** Label for the right column (e.g. "Revised", "Our Markup"). */
  rightLabel: string;
  /** Ordered list of clause pairs to compare. */
  clauses: ContractClausePair[];
}

export type ContractComparisonWidgetProps = OutputWidgetProps<ContractComparisonData>;

// ---------------------------------------------------------------------------
// Styles — Fluent v9 tokens only (ADR-021)
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  root: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalM,
    padding: tokens.spacingHorizontalL,
  },
  columnHeaders: {
    display: 'grid',
    gridTemplateColumns: '1fr 1fr',
    gap: tokens.spacingHorizontalM,
    paddingBottom: tokens.spacingVerticalXS,
    ...shorthands.borderBottom('1px', 'solid', tokens.colorNeutralStroke2),
  },
  columnHeader: {
    fontWeight: tokens.fontWeightSemibold,
  },
  clauseList: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalXS,
  },
  clauseRow: {
    display: 'grid',
    gridTemplateColumns: '1fr 1fr',
    gap: tokens.spacingHorizontalM,
    padding: tokens.spacingVerticalS,
    borderRadius: tokens.borderRadiusMedium,
    ...shorthands.border('1px', 'solid', tokens.colorNeutralStroke2),
    backgroundColor: tokens.colorNeutralBackground1,
  },
  clauseRowDelta: {
    backgroundColor: tokens.colorStatusWarningBackground2,
    ...shorthands.border('1px', 'solid', tokens.colorStatusWarningBorderActive),
  },
  clauseCell: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalXXS,
  },
  clauseText: {
    color: tokens.colorNeutralForeground1,
    whiteSpace: 'pre-wrap',
    wordBreak: 'break-word',
  },
  clauseTextDelta: {
    color: tokens.colorStatusWarningForeground3,
  },
  deltaIndicator: {
    display: 'flex',
    justifyContent: 'center',
    alignItems: 'center',
    gridColumn: '1 / -1',
    paddingTop: tokens.spacingVerticalXXS,
  },
  errorText: {
    color: tokens.colorStatusDangerForeground1,
  },
  emptyText: {
    color: tokens.colorNeutralForeground3,
  },
});

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

/**
 * ContractComparisonWidget renders pairs of contract clauses side-by-side.
 * Rows with hasDelta=true are highlighted with Fluent v9 warning color tokens
 * so reviewers can quickly locate changed clauses without re-reading identical
 * content.
 */
export default function ContractComparisonWidget({
  data,
  isLoading,
  error,
  className,
}: ContractComparisonWidgetProps): React.ReactElement {
  const styles = useStyles();

  if (isLoading) {
    return (
      <div className={mergeClasses(styles.root, className)}>
        <Spinner size="medium" label="Loading contract comparison..." />
      </div>
    );
  }

  if (error) {
    return (
      <div className={mergeClasses(styles.root, className)}>
        <Text className={styles.errorText}>{error}</Text>
      </div>
    );
  }

  if (data.clauses.length === 0) {
    return (
      <div className={mergeClasses(styles.root, className)}>
        <Text className={styles.emptyText}>No clauses to compare.</Text>
      </div>
    );
  }

  return (
    <div className={mergeClasses(styles.root, className)}>
      {/* Column headers */}
      <div className={styles.columnHeaders}>
        <Text size={300} className={styles.columnHeader}>
          {data.leftLabel}
        </Text>
        <Text size={300} className={styles.columnHeader}>
          {data.rightLabel}
        </Text>
      </div>

      {/* Clause rows */}
      <div className={styles.clauseList}>
        {data.clauses.map(clause => (
          <div key={clause.id} className={mergeClasses(styles.clauseRow, clause.hasDelta && styles.clauseRowDelta)}>
            {/* Left clause cell */}
            <div className={styles.clauseCell}>
              <Text size={300} className={mergeClasses(styles.clauseText, clause.hasDelta && styles.clauseTextDelta)}>
                {clause.left}
              </Text>
            </div>

            {/* Right clause cell */}
            <div className={styles.clauseCell}>
              <Text size={300} className={mergeClasses(styles.clauseText, clause.hasDelta && styles.clauseTextDelta)}>
                {clause.right}
              </Text>
            </div>

            {/* Delta indicator badge — full-width row below the two cells */}
            {clause.hasDelta && (
              <div className={styles.deltaIndicator}>
                <Badge appearance="tint" color="warning" size="small">
                  Changed
                </Badge>
              </div>
            )}
          </div>
        ))}
      </div>
    </div>
  );
}
