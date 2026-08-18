/**
 * PriorityScoreCard — Displays the priority scoring breakdown for a to-do item.
 *
 * R5 FR-01 / task 002 — hoisted host-agnostic from
 * `src/solutions/LegalWorkspace/src/components/SmartToDo/PriorityScoreCard.tsx`.
 * Only cross-folder import re-homed: `ITodoPriorityScore` now resolves from the
 * package's `../../types/todoScoringTypes` (was folder-local `./todoScoringTypes`).
 *
 * Layout (vertical flex, contained in a Fluent v9 Card):
 *   - Card header: "PRIORITY" label
 *   - Large score display (e.g. "85")
 *   - Priority level badge: colour-coded pill (Critical/High/Medium/Low)
 *   - Factor table: 2-column table listing factor name, value, and points
 *
 * Design constraints:
 *   - ALL colours from Fluent UI v9 semantic tokens — zero hardcoded hex/rgb
 *   - makeStyles (Griffel) only for custom styles
 *   - Dark mode + high-contrast supported automatically via token system
 */

import * as React from 'react';
import { makeStyles, shorthands, tokens, Text, Card, mergeClasses } from '@fluentui/react-components';
import type { ITodoPriorityScore } from '../../types/todoScoringTypes';

// ---------------------------------------------------------------------------
// Styles
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  card: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalS,
    paddingTop: tokens.spacingVerticalM,
    paddingBottom: tokens.spacingVerticalM,
    paddingLeft: tokens.spacingHorizontalM,
    paddingRight: tokens.spacingHorizontalM,
    backgroundColor: tokens.colorNeutralBackground2,
    ...shorthands.borderColor(tokens.colorNeutralStroke2),
    borderRadius: tokens.borderRadiusMedium,
    height: '100%',
  },

  // ── Section label ──────────────────────────────────────────────────────────
  sectionLabel: {
    color: tokens.colorNeutralForeground3,
    fontWeight: tokens.fontWeightSemibold,
    textTransform: 'uppercase',
    letterSpacing: '0.05em',
  },

  // ── Score row: large number + level badge ──────────────────────────────────
  scoreRow: {
    display: 'flex',
    flexDirection: 'row',
    alignItems: 'center',
    gap: tokens.spacingHorizontalS,
    flexWrap: 'wrap',
  },
  scoreValue: {
    fontSize: '36px',
    fontWeight: tokens.fontWeightBold,
    lineHeight: '1',
    color: tokens.colorNeutralForeground1,
  },

  // ── Level badge ────────────────────────────────────────────────────────────
  levelBadge: {
    display: 'inline-flex',
    alignItems: 'center',
    borderRadius: tokens.borderRadiusMedium,
    paddingTop: '2px',
    paddingBottom: '2px',
    paddingLeft: tokens.spacingHorizontalS,
    paddingRight: tokens.spacingHorizontalS,
    fontSize: tokens.fontSizeBase200,
    fontWeight: tokens.fontWeightSemibold,
    lineHeight: tokens.lineHeightBase200,
    whiteSpace: 'nowrap',
    // Base colours overridden by level-specific variants below
    backgroundColor: tokens.colorNeutralBackground4,
    color: tokens.colorNeutralForeground2,
  },
  levelBadgeUrgent: {
    backgroundColor: tokens.colorStatusDangerBackground1,
    color: tokens.colorStatusDangerForeground1,
  },
  levelBadgeHigh: {
    backgroundColor: tokens.colorStatusWarningBackground1,
    color: tokens.colorStatusWarningForeground1,
  },
  levelBadgeNormal: {
    backgroundColor: tokens.colorStatusSuccessBackground1,
    color: tokens.colorStatusSuccessForeground1,
  },
  levelBadgeLow: {
    backgroundColor: tokens.colorNeutralBackground4,
    color: tokens.colorNeutralForeground2,
  },

  // ── Factor table ───────────────────────────────────────────────────────────
  factorTable: {
    width: '100%',
    borderCollapse: 'collapse' as const,
    marginTop: tokens.spacingVerticalXS,
  },
  factorTableHeader: {
    borderBottomWidth: '1px',
    borderBottomStyle: 'solid',
    borderBottomColor: tokens.colorNeutralStroke2,
  },
  thFactor: {
    textAlign: 'left' as const,
    paddingBottom: tokens.spacingVerticalXXS,
    paddingRight: tokens.spacingHorizontalS,
  },
  thValue: {
    textAlign: 'left' as const,
    paddingBottom: tokens.spacingVerticalXXS,
    paddingRight: tokens.spacingHorizontalS,
  },
  thPoints: {
    textAlign: 'right' as const,
    paddingBottom: tokens.spacingVerticalXXS,
  },
  headerText: {
    color: tokens.colorNeutralForeground3,
    fontWeight: tokens.fontWeightSemibold,
  },
  factorRow: {
    borderBottomWidth: '1px',
    borderBottomStyle: 'solid',
    borderBottomColor: tokens.colorNeutralStroke3,
    ':last-child': {
      borderBottomWidth: '0px',
    },
  },
  tdFactor: {
    paddingTop: tokens.spacingVerticalXS,
    paddingBottom: tokens.spacingVerticalXS,
    paddingRight: tokens.spacingHorizontalS,
    color: tokens.colorNeutralForeground1,
  },
  tdValue: {
    paddingTop: tokens.spacingVerticalXS,
    paddingBottom: tokens.spacingVerticalXS,
    paddingRight: tokens.spacingHorizontalS,
    color: tokens.colorNeutralForeground2,
  },
  tdPoints: {
    paddingTop: tokens.spacingVerticalXS,
    paddingBottom: tokens.spacingVerticalXS,
    textAlign: 'right' as const,
    color: tokens.colorBrandForeground1,
    fontWeight: tokens.fontWeightSemibold,
  },

  // ── Total row ──────────────────────────────────────────────────────────────
  totalRow: {
    borderTopWidth: '2px',
    borderTopStyle: 'solid',
    borderTopColor: tokens.colorNeutralStroke1,
  },
  tdTotal: {
    paddingTop: tokens.spacingVerticalXS,
    color: tokens.colorNeutralForeground1,
    fontWeight: tokens.fontWeightSemibold,
  },
  tdTotalPoints: {
    paddingTop: tokens.spacingVerticalXS,
    textAlign: 'right' as const,
    color: tokens.colorBrandForeground1,
    fontWeight: tokens.fontWeightBold,
  },

  // ── Mock data notice ───────────────────────────────────────────────────────
  mockNotice: {
    color: tokens.colorNeutralForeground4,
    fontStyle: 'italic',
    marginTop: tokens.spacingVerticalXS,
  },

  // ── Selected-priority row (FR-02 / task 012) ───────────────────────────────
  selectedLabel: {
    color: tokens.colorNeutralForeground3,
    fontWeight: tokens.fontWeightSemibold,
  },
});

// ---------------------------------------------------------------------------
// Level badge sub-component
// ---------------------------------------------------------------------------

type PriorityLevel = 'Urgent' | 'High' | 'Normal' | 'Low';

interface ILevelBadgeProps {
  level: PriorityLevel;
}

const LevelBadge: React.FC<ILevelBadgeProps> = ({ level }) => {
  const styles = useStyles();

  const levelClass = React.useMemo(() => {
    switch (level) {
      case 'Urgent':
        return styles.levelBadgeUrgent;
      case 'High':
        return styles.levelBadgeHigh;
      case 'Normal':
        return styles.levelBadgeNormal;
      case 'Low':
      default:
        return styles.levelBadgeLow;
    }
  }, [level, styles]);

  return (
    <span className={mergeClasses(styles.levelBadge, levelClass)} aria-label={`Priority level: ${level}`}>
      {level}
    </span>
  );
};

// ---------------------------------------------------------------------------
// Selected-priority badge (FR-02 / task 012)
// ---------------------------------------------------------------------------

/**
 * Pure value→label lookup for the raw `sprk_priority` Choice field (task
 * 010). Exported (unlike `PriorityChoiceBadge` below) specifically so it is
 * unit-testable without a React renderer — this package ships type-check
 * only today (`tsc --noEmit`, no Jest — task 040 wires Jest in). Returns
 * `undefined` for an unset/unrecognised value (neutral no-op, never a
 * misleading default).
 */
export function priorityChoiceLabel(value: number | null | undefined): string | undefined {
  switch (value) {
    case 100000000:
      return 'Urgent';
    case 100000001:
      return 'High';
    case 100000002:
      return 'Medium';
    case 100000003:
      return 'Low';
    default:
      return undefined;
  }
}

/**
 * Small badge surfacing the raw `sprk_priority` Choice selection (task 010:
 * Urgent=100000000, High=100000001, Medium=100000002, Low=100000003) — a
 * DIFFERENT data source from `priority.level` above (which is derived from
 * the 0-100 `priority.score`, not the raw Choice field). Reuses the
 * existing Urgent/High/Normal/Low tone classes (colours) from `LevelBadge`
 * above, only swapping the "Normal" tone's display text to "Medium" (the
 * Choice option label) — no new CSS palette is introduced. Kept internal to
 * this file (not exported) per task 012's acceptance criterion "no new
 * component created for this surface — extended in place".
 */
const PriorityChoiceBadge: React.FC<{ value: number }> = ({ value }) => {
  const styles = useStyles();

  const display = React.useMemo((): { label: string; toneClass: string } | undefined => {
    const label = priorityChoiceLabel(value);
    if (!label) {
      return undefined; // Unrecognised value — neutral no-op (no badge rendered).
    }
    switch (value) {
      case 100000000:
        return { label, toneClass: styles.levelBadgeUrgent };
      case 100000001:
        return { label, toneClass: styles.levelBadgeHigh };
      case 100000002:
        return { label, toneClass: styles.levelBadgeNormal };
      case 100000003:
        return { label, toneClass: styles.levelBadgeLow };
      default:
        return undefined;
    }
  }, [value, styles]);

  if (!display) {
    return null;
  }

  return (
    <span
      className={mergeClasses(styles.levelBadge, display.toneClass)}
      aria-label={`Selected priority: ${display.label}`}
    >
      {display.label}
    </span>
  );
};

// ---------------------------------------------------------------------------
// Props
// ---------------------------------------------------------------------------

export interface IPriorityScoreCardProps {
  /** Priority scoring data to display */
  priority: ITodoPriorityScore;
  /** Whether the data came from mock (shows notice when true) */
  isMockData: boolean;
  /**
   * Raw `sprk_priority` Choice value (task 010: Urgent=100000000,
   * High=100000001, Medium=100000002, Low=100000003) — optional,
   * presentation-only surface of the user-selected label alongside the
   * existing SCORE-derived level badge above. Distinct data source from
   * `priority.level` (derived from `priority.score`, not this raw Choice
   * field). Added by task 012 (FR-02). Unset/unrecognised → no row rendered
   * (neutral no-op, not a misleading default).
   */
  priorityChoice?: number | null;
}

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

export const PriorityScoreCard: React.FC<IPriorityScoreCardProps> = React.memo(
  ({ priority, isMockData, priorityChoice }) => {
    const styles = useStyles();

    // Total points across all factors
    const totalPoints = priority.factors.reduce((sum, f) => sum + f.points, 0);

    return (
      <Card className={styles.card} aria-label={`Priority score: ${priority.score}, level ${priority.level}`}>
        {/* Section header */}
        <Text size={100} className={styles.sectionLabel}>
          Priority
        </Text>

        {/* Large score + level badge */}
        <div className={styles.scoreRow}>
          <span className={styles.scoreValue} aria-hidden="true">
            {priority.score}
          </span>
          <LevelBadge level={priority.level} />
        </div>

        {/* Selected-priority row (FR-02 / task 012) — the raw sprk_priority
            Choice selection, distinct from the score-derived level above. */}
        {typeof priorityChoice === 'number' && (
          <div className={styles.scoreRow}>
            <Text size={100} className={styles.selectedLabel}>
              Selected:
            </Text>
            <PriorityChoiceBadge value={priorityChoice} />
          </div>
        )}

        {/* Factor breakdown table */}
        <table className={styles.factorTable} aria-label="Priority factor breakdown">
          <thead>
            <tr className={styles.factorTableHeader}>
              <th className={styles.thFactor}>
                <Text size={100} className={styles.headerText}>
                  Factor
                </Text>
              </th>
              <th className={styles.thValue}>
                <Text size={100} className={styles.headerText}>
                  Value
                </Text>
              </th>
              <th className={styles.thPoints}>
                <Text size={100} className={styles.headerText}>
                  Pts
                </Text>
              </th>
            </tr>
          </thead>
          <tbody>
            {priority.factors.map(factor => (
              <tr key={factor.name} className={styles.factorRow}>
                <td className={styles.tdFactor}>
                  <Text size={200}>{factor.name}</Text>
                </td>
                <td className={styles.tdValue}>
                  <Text size={200}>{factor.value}</Text>
                </td>
                <td className={styles.tdPoints}>
                  <Text size={200}>+{factor.points}</Text>
                </td>
              </tr>
            ))}
            {/* Total row */}
            <tr className={styles.totalRow}>
              <td className={styles.tdTotal} colSpan={2}>
                <Text size={200} weight="semibold">
                  Total
                </Text>
              </td>
              <td className={styles.tdTotalPoints}>
                <Text size={200} weight="bold">
                  {totalPoints}
                </Text>
              </td>
            </tr>
          </tbody>
        </table>

        {isMockData && (
          <Text size={100} className={styles.mockNotice}>
            Preview data — connect to BFF for live scoring
          </Text>
        )}
      </Card>
    );
  }
);

PriorityScoreCard.displayName = 'PriorityScoreCard';
