/**
 * StatTiles — at-a-glance KPI row for the Daily Briefing (R5 task 021 redesign).
 *
 * A compact row of count tiles ("Open items / Overdue / Critical / New matters")
 * that answers "how much is on my plate" before the reader scans the sections.
 *
 * Component justification (root CLAUDE.md §11):
 *   - Existing: none — no KPI/stat-tile surface exists in
 *     `Spaarke.DailyBriefing.Components` (grep: no StatTile/MetricCard component).
 *   - Extension: the briefing had no summary-count surface to extend; the header
 *     showed only a single "N items" string.
 *   - Cost-of-doing-nothing: the operator asked for a glanceable count row; no
 *     current component renders per-category totals.
 *
 * DETERMINISM (FR-A4 posture): every `value` is a count computed deterministically
 * upstream from the source records (see `DailyBriefingApp`) — this component only
 * renders the numbers it is given; it derives nothing and calls no LLM.
 *
 * Constraints:
 *   - ADR-021: Fluent v9 semantic tokens only; dark-mode via tokens.
 */

import * as React from 'react';
import { makeStyles, tokens, mergeClasses, Text } from '@fluentui/react-components';

export type StatTone = 'neutral' | 'danger' | 'warning' | 'brand';

export interface StatTile {
  /** Short label, e.g. "Overdue". */
  label: string;
  /** Deterministic count. */
  value: number;
  /** Emphasis color for the number (default neutral). */
  tone?: StatTone;
}

const useStyles = makeStyles({
  row: {
    display: 'grid',
    gridTemplateColumns: 'repeat(auto-fit, minmax(116px, 1fr))',
    gap: tokens.spacingHorizontalM,
    marginBottom: tokens.spacingVerticalL,
  },
  tile: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalXXS,
    backgroundColor: tokens.colorNeutralBackground1,
    borderTopWidth: '1px',
    borderRightWidth: '1px',
    borderBottomWidth: '1px',
    borderLeftWidth: '1px',
    borderTopStyle: 'solid',
    borderRightStyle: 'solid',
    borderBottomStyle: 'solid',
    borderLeftStyle: 'solid',
    borderTopColor: tokens.colorNeutralStroke2,
    borderRightColor: tokens.colorNeutralStroke2,
    borderBottomColor: tokens.colorNeutralStroke2,
    borderLeftColor: tokens.colorNeutralStroke2,
    borderTopLeftRadius: tokens.borderRadiusMedium,
    borderTopRightRadius: tokens.borderRadiusMedium,
    borderBottomLeftRadius: tokens.borderRadiusMedium,
    borderBottomRightRadius: tokens.borderRadiusMedium,
    paddingTop: tokens.spacingVerticalM,
    paddingBottom: tokens.spacingVerticalM,
    paddingLeft: tokens.spacingHorizontalM,
    paddingRight: tokens.spacingHorizontalM,
  },
  label: {
    color: tokens.colorNeutralForeground3,
  },
  value: {
    fontSize: tokens.fontSizeHero700,
    lineHeight: tokens.lineHeightHero700,
    fontWeight: tokens.fontWeightBold,
  },
  valueNeutral: { color: tokens.colorNeutralForeground1 },
  valueDanger: { color: tokens.colorStatusDangerForeground1 },
  valueWarning: { color: tokens.colorStatusWarningForeground1 },
  valueBrand: { color: tokens.colorBrandForeground1 },
});

const TONE_CLASS: Record<StatTone, keyof ReturnType<typeof useStyles>> = {
  neutral: 'valueNeutral',
  danger: 'valueDanger',
  warning: 'valueWarning',
  brand: 'valueBrand',
};

export interface StatTilesProps {
  tiles: StatTile[];
}

export const StatTiles: React.FC<StatTilesProps> = ({ tiles }) => {
  const styles = useStyles();
  if (!Array.isArray(tiles) || tiles.length === 0) return null;

  return (
    <div className={styles.row} role="group" aria-label="Briefing summary counts">
      {tiles.map((tile, idx) => (
        <div key={`${tile.label}-${idx}`} className={styles.tile}>
          <Text size={200} className={styles.label}>
            {tile.label}
          </Text>
          <span className={mergeClasses(styles.value, styles[TONE_CLASS[tile.tone ?? 'neutral']])}>{tile.value}</span>
        </div>
      ))}
    </div>
  );
};
