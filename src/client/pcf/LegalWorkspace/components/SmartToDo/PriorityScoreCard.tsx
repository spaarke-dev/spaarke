/**
 * PriorityScoreCard — Displays the priority scoring breakdown for a to-do item.
 *
 * Layout (vertical flex, contained in a Fluent v9 Card):
 *   - Card header: "PRIORITY" label
 *   - Large score display (e.g. "85")
 *   - Priority level badge: colour-coded pill (Critical/High/Medium/Low)
 *   - Factor table: 2-column table listing factor name, value, and points
 *     Rows: Overdue days, Budget utilization, Grades below C,
 *           Deadline proximity, Matter value tier, Pending invoices
 *
 * Design constraints:
 *   - ALL colours from Fluent UI v9 semantic tokens — zero hardcoded hex/rgb
 *   - makeStyles (Griffel) only for custom styles
 *   - Dark mode + high-contrast supported automatically via token system
 *   - Priority level colours:
 *       Critical → colorStatusDangerForeground1
 *       High     → colorStatusWarningForeground1
 *       Medium   → colorStatusSuccessForeground1
 *       Low      → colorNeutralForeground2
 */

import * as React from 'react';
import {
  makeStyles,
  shorthands,
  tokens,
  Text,
  Card,
  mergeClasses,
} from '@fluentui/react-components';
import type { ITodoPriorityScore } from '../../hooks/useTodoScoring';

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
  levelBadgeCritical: {
    backgroundColor: tokens.colorStatusDangerBackground1,
    color: tokens.colorStatusDangerForeground1,
  },
  levelBadgeHigh: {
    backgroundColor: tokens.colorStatusWarningBackground1,
    color: tokens.colorStatusWarningForeground1,
  },
  levelBadgeMedium: {
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
});

// ---------------------------------------------------------------------------
// Level badge sub-component
// ---------------------------------------------------------------------------

type PriorityLevel = 'Critical' | 'High' | 'Medium' | 'Low';

interface ILevelBadgeProps {
  level: PriorityLevel;
}

const LevelBadge: React.FC<ILevelBadgeProps> = ({ level }) => {
  const styles = useStyles();

  const levelClass = React.useMemo(() => {
    switch (level) {
      case 'Critical': return styles.levelBadgeCritical;
      case 'High':     return styles.levelBadgeHigh;
      case 'Medium':   return styles.levelBadgeMedium;
      case 'Low':
      default:         return styles.levelBadgeLow;
    }
  }, [level, styles]);

  return (
    <span
      className={mergeClasses(styles.levelBadge, levelClass)}
      aria-label={`Priority level: ${level}`}
    >
      {level}
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
}

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

export const PriorityScoreCard: React.FC<IPriorityScoreCardProps> = React.memo(
  ({ priority, isMockData }) => {
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

        {/* Factor breakdown table */}
        <table
          className={styles.factorTable}
          aria-label="Priority factor breakdown"
        >
          <thead>
            <tr className={styles.factorTableHeader}>
              <th className={styles.thFactor}>
                <Text size={100} className={styles.headerText}>Factor</Text>
              </th>
              <th className={styles.thValue}>
                <Text size={100} className={styles.headerText}>Value</Text>
              </th>
              <th className={styles.thPoints}>
                <Text size={100} className={styles.headerText}>Pts</Text>
              </th>
            </tr>
          </thead>
          <tbody>
            {priority.factors.map((factor) => (
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
                <Text size={200} weight="semibold">Total</Text>
              </td>
              <td className={styles.tdTotalPoints}>
                <Text size={200} weight="bold">{totalPoints}</Text>
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
