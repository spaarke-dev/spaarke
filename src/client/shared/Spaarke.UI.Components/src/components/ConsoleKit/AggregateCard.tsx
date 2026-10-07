/**
 * AggregateCard — "a count and a link": points at work that lives on another surface (e.g. Email Review).
 * It is not a work item and is never counted in lane counts. A null count reads Missing, not 0 (H-5).
 *
 * Not a MetricCard: MetricCard is a square, count-only tile with no link sentence; the non-square option
 * is task 052's. Task: spaarke-ontology-platform-r1, task 057.
 */

import * as React from 'react';
import { Link, Text, makeStyles, mergeClasses, tokens } from '@fluentui/react-components';

export interface AggregateCardProps {
  /** Count of items elsewhere; null / undefined renders "Missing". */
  count: number | null | undefined;
  /** Sentence after the count, e.g. "emails waiting for review". */
  label: string;
  /** Link text, e.g. "Open Email Review". The trailing arrow is added by the card. */
  linkLabel: string;
  onOpen: () => void;
  /** Optional leading icon element (a Fluent icon). */
  icon?: React.ReactElement;
  className?: string;
}

const useStyles = makeStyles({
  root: {
    display: 'flex',
    alignItems: 'center',
    columnGap: tokens.spacingHorizontalM,
    backgroundColor: tokens.colorNeutralBackground1,
    borderTopWidth: tokens.strokeWidthThin,
    borderRightWidth: tokens.strokeWidthThin,
    borderBottomWidth: tokens.strokeWidthThin,
    borderLeftWidth: tokens.strokeWidthThin,
    borderTopStyle: 'solid',
    borderRightStyle: 'solid',
    borderBottomStyle: 'solid',
    borderLeftStyle: 'solid',
    borderTopColor: tokens.colorNeutralStroke2,
    borderRightColor: tokens.colorNeutralStroke2,
    borderBottomColor: tokens.colorNeutralStroke2,
    borderLeftColor: tokens.colorNeutralStroke2,
    borderRadius: tokens.borderRadiusMedium,
    paddingTop: tokens.spacingVerticalS,
    paddingBottom: tokens.spacingVerticalS,
    paddingLeft: tokens.spacingHorizontalM,
    paddingRight: tokens.spacingHorizontalM,
  },
  icon: { display: 'flex', flexShrink: 0, color: tokens.colorBrandForeground1, fontSize: tokens.fontSizeBase500 },
  text: { display: 'flex', flexDirection: 'column', minWidth: 0 },
  count: { color: tokens.colorNeutralForeground1 },
  missing: { color: tokens.colorStatusWarningForeground1 },
});

export const AggregateCard: React.FC<AggregateCardProps> = ({ count, label, linkLabel, onOpen, icon, className }) => {
  const styles = useStyles();
  const missing = count === null || count === undefined;
  return (
    <div className={mergeClasses(styles.root, className)} data-testid="aggregate-card">
      {icon && (
        <span className={styles.icon} aria-hidden>
          {icon}
        </span>
      )}
      <div className={styles.text}>
        <Text weight="semibold" className={missing ? styles.missing : styles.count} data-testid="aggregate-count">
          {missing ? 'Missing' : `${count} ${label}`}
        </Text>
        {missing && <Text size={200}>{label}</Text>}
        <Link onClick={onOpen}>{linkLabel} ›</Link>
      </div>
    </div>
  );
};

AggregateCard.displayName = 'AggregateCard';
