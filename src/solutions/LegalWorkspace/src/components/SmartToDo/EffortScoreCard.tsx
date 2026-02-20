/**
 * EffortScoreCard — Displays the effort scoring breakdown for a to-do item.
 *
 * Layout (vertical flex, contained in a Fluent v9 Card):
 *   - Card header: "EFFORT" label
 *   - Large score display (e.g. "72")
 *   - Effort level badge: colour-coded pill (High/Med/Low)
 *   - Base effort value display
 *   - Complexity multiplier checklist: each row shows icon + label + multiplier value
 *     Icons: CheckmarkCircleRegular (applied=true), CircleRegular (applied=false)
 *     Multipliers: Multiple parties 1.3x, Cross-jurisdiction 1.2x, Regulatory 1.1x,
 *                  High value 1.2x, Time-sensitive 1.3x
 *
 * Design constraints:
 *   - ALL colours from Fluent UI v9 semantic tokens — zero hardcoded hex/rgb
 *   - makeStyles (Griffel) only for custom styles
 *   - Dark mode + high-contrast supported automatically via token system
 *   - Effort level colours:
 *       High → colorStatusDangerForeground1
 *       Med  → colorStatusWarningForeground1
 *       Low  → colorStatusSuccessForeground1
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
import {
  CheckmarkCircleRegular,
  CircleRegular,
} from '@fluentui/react-icons';
import type { ITodoEffortScore } from '../../hooks/useTodoScoring';

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
    backgroundColor: tokens.colorNeutralBackground4,
    color: tokens.colorNeutralForeground2,
  },
  levelBadgeHigh: {
    backgroundColor: tokens.colorStatusDangerBackground1,
    color: tokens.colorStatusDangerForeground1,
  },
  levelBadgeMed: {
    backgroundColor: tokens.colorStatusWarningBackground1,
    color: tokens.colorStatusWarningForeground1,
  },
  levelBadgeLow: {
    backgroundColor: tokens.colorStatusSuccessBackground1,
    color: tokens.colorStatusSuccessForeground1,
  },

  // ── Base effort row ────────────────────────────────────────────────────────
  baseEffortRow: {
    display: 'flex',
    flexDirection: 'row',
    alignItems: 'center',
    gap: tokens.spacingHorizontalXS,
  },
  baseEffortLabel: {
    color: tokens.colorNeutralForeground3,
  },
  baseEffortValue: {
    color: tokens.colorNeutralForeground1,
    fontWeight: tokens.fontWeightSemibold,
  },

  // ── Multipliers section ────────────────────────────────────────────────────
  multipliersSection: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalXXS,
    marginTop: tokens.spacingVerticalXS,
  },
  multipliersLabel: {
    color: tokens.colorNeutralForeground3,
    fontWeight: tokens.fontWeightSemibold,
    marginBottom: tokens.spacingVerticalXXS,
  },

  // ── Single multiplier row ──────────────────────────────────────────────────
  multiplierRow: {
    display: 'flex',
    flexDirection: 'row',
    alignItems: 'center',
    gap: tokens.spacingHorizontalXS,
    paddingTop: tokens.spacingVerticalXXS,
    paddingBottom: tokens.spacingVerticalXXS,
  },
  multiplierIconApplied: {
    color: tokens.colorStatusSuccessForeground1,
    fontSize: '16px',
    flexShrink: 0,
  },
  multiplierIconNotApplied: {
    color: tokens.colorNeutralForeground4,
    fontSize: '16px',
    flexShrink: 0,
  },
  multiplierName: {
    flex: '1 1 0',
    minWidth: 0,
  },
  multiplierNameApplied: {
    color: tokens.colorNeutralForeground1,
  },
  multiplierNameNotApplied: {
    color: tokens.colorNeutralForeground4,
  },
  multiplierValue: {
    flexShrink: 0,
    fontWeight: tokens.fontWeightSemibold,
  },
  multiplierValueApplied: {
    color: tokens.colorBrandForeground1,
  },
  multiplierValueNotApplied: {
    color: tokens.colorNeutralForeground4,
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

type EffortLevel = 'High' | 'Med' | 'Low';

interface IEffortLevelBadgeProps {
  level: EffortLevel;
}

const EffortLevelBadge: React.FC<IEffortLevelBadgeProps> = ({ level }) => {
  const styles = useStyles();

  const levelClass = React.useMemo(() => {
    switch (level) {
      case 'High': return styles.levelBadgeHigh;
      case 'Med':  return styles.levelBadgeMed;
      case 'Low':
      default:     return styles.levelBadgeLow;
    }
  }, [level, styles]);

  return (
    <span
      className={mergeClasses(styles.levelBadge, levelClass)}
      aria-label={`Effort level: ${level}`}
    >
      {level}
    </span>
  );
};

// ---------------------------------------------------------------------------
// Multiplier row sub-component
// ---------------------------------------------------------------------------

interface IMultiplierRowProps {
  name: string;
  value: number;
  applied: boolean;
}

const MultiplierRow: React.FC<IMultiplierRowProps> = React.memo(
  ({ name, value, applied }) => {
    const styles = useStyles();

    return (
      <div
        className={styles.multiplierRow}
        role="listitem"
        aria-label={`${name} ${value}x — ${applied ? 'applied' : 'not applied'}`}
      >
        {/* Icon: checkmark if applied, circle outline if not */}
        {applied ? (
          <CheckmarkCircleRegular
            className={styles.multiplierIconApplied}
            aria-hidden="true"
          />
        ) : (
          <CircleRegular
            className={styles.multiplierIconNotApplied}
            aria-hidden="true"
          />
        )}

        {/* Multiplier name */}
        <Text
          size={200}
          className={mergeClasses(
            styles.multiplierName,
            applied ? styles.multiplierNameApplied : styles.multiplierNameNotApplied
          )}
        >
          {name}
        </Text>

        {/* Multiplier value */}
        <Text
          size={200}
          className={mergeClasses(
            styles.multiplierValue,
            applied ? styles.multiplierValueApplied : styles.multiplierValueNotApplied
          )}
        >
          {value.toFixed(1)}x
        </Text>
      </div>
    );
  }
);

MultiplierRow.displayName = 'MultiplierRow';

// ---------------------------------------------------------------------------
// Props
// ---------------------------------------------------------------------------

export interface IEffortScoreCardProps {
  /** Effort scoring data to display */
  effort: ITodoEffortScore;
  /** Whether the data came from mock (shows notice when true) */
  isMockData: boolean;
}

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

export const EffortScoreCard: React.FC<IEffortScoreCardProps> = React.memo(
  ({ effort, isMockData }) => {
    const styles = useStyles();

    return (
      <Card className={styles.card} aria-label={`Effort score: ${effort.score}, level ${effort.level}`}>
        {/* Section header */}
        <Text size={100} className={styles.sectionLabel}>
          Effort
        </Text>

        {/* Large score + level badge */}
        <div className={styles.scoreRow}>
          <span className={styles.scoreValue} aria-hidden="true">
            {effort.score}
          </span>
          <EffortLevelBadge level={effort.level} />
        </div>

        {/* Base effort display */}
        <div className={styles.baseEffortRow}>
          <Text size={200} className={styles.baseEffortLabel}>
            Base effort:
          </Text>
          <Text size={200} className={styles.baseEffortValue}>
            {effort.baseEffort} pts
          </Text>
        </div>

        {/* Complexity multipliers checklist */}
        <div className={styles.multipliersSection}>
          <Text size={100} className={styles.multipliersLabel}>
            Complexity multipliers
          </Text>
          <div role="list" aria-label="Complexity multipliers">
            {effort.multipliers.map((multiplier) => (
              <MultiplierRow
                key={multiplier.name}
                name={multiplier.name}
                value={multiplier.value}
                applied={multiplier.applied}
              />
            ))}
          </div>
        </div>

        {isMockData && (
          <Text size={100} className={styles.mockNotice}>
            Preview data — connect to BFF for live scoring
          </Text>
        )}
      </Card>
    );
  }
);

EffortScoreCard.displayName = 'EffortScoreCard';
