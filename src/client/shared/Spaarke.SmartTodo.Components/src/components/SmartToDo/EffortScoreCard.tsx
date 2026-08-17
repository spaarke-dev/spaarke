/**
 * EffortScoreCard — Displays the effort scoring breakdown for a to-do item.
 *
 * R5 FR-01 / task 002 — hoisted host-agnostic from
 * `src/solutions/LegalWorkspace/src/components/SmartToDo/EffortScoreCard.tsx`.
 * Only cross-folder import re-homed: `ITodoEffortScore` now resolves from the
 * package's `../../types/todoScoringTypes` (was folder-local `./todoScoringTypes`).
 *
 * Layout (vertical flex, contained in a Fluent v9 Card):
 *   - Card header: "EFFORT" label
 *   - Large score display (e.g. "72")
 *   - Effort level badge: colour-coded pill (High/Med/Low)
 *   - Base effort value display
 *   - Complexity multiplier checklist
 *
 * Design constraints:
 *   - ALL colours from Fluent UI v9 semantic tokens — zero hardcoded hex/rgb
 *   - makeStyles (Griffel) only for custom styles
 *   - Dark mode + high-contrast supported automatically via token system
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
import type { ITodoEffortScore } from '../../types/todoScoringTypes';

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

  // ── Selected-effort choice tones not covered by the 3 score-derived tiers
  //    above (FR-03 / task 012: sprk_effort has 5 options — None/Very High/
  //    High/Medium/Low — vs. the 3-tier score-derived High/Med/Low here) ────
  levelBadgeChoiceHigh: {
    backgroundColor: tokens.colorPaletteDarkOrangeBackground1,
    color: tokens.colorPaletteDarkOrangeForeground1,
  },
  levelBadgeChoiceNone: {
    backgroundColor: tokens.colorNeutralBackground4,
    color: tokens.colorNeutralForeground2,
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
// Selected-effort badge (FR-03 / task 012)
// ---------------------------------------------------------------------------

/**
 * Pure value→label lookup for the raw `sprk_effort` Choice field (task
 * 010). Exported (unlike `EffortChoiceBadge` below) specifically so it is
 * unit-testable without a React renderer — this package ships type-check
 * only today (`tsc --noEmit`, no Jest — task 040 wires Jest in). Returns
 * `undefined` for an unset/unrecognised value (neutral no-op, never a
 * misleading default).
 */
export function effortChoiceLabel(value: number | null | undefined): string | undefined {
  switch (value) {
    case 100000000: return 'None';
    case 100000001: return 'Very High';
    case 100000002: return 'High';
    case 100000003: return 'Medium';
    case 100000004: return 'Low';
    default: return undefined;
  }
}

/**
 * Small badge surfacing the raw `sprk_effort` Choice selection (task 010:
 * None=100000000, Very High=100000001, High=100000002, Medium=100000003,
 * Low=100000004) — a DIFFERENT data source from `effort.level` above (which
 * is derived from the 0-100 `effort.score`, not the raw Choice field, and
 * only has 3 tiers vs. this field's 5). Reuses the existing High/Med/Low
 * tone classes from `EffortLevelBadge` above for "Very High"/"Medium"/"Low"
 * (colours match the same danger/warning/success vocabulary), adding only
 * 2 new tones ("High", "None") not already covered. Kept internal to this
 * file (not exported) per task 012's acceptance criterion "no new component
 * created for this surface — extended in place".
 */
const EffortChoiceBadge: React.FC<{ value: number }> = ({ value }) => {
  const styles = useStyles();

  const display = React.useMemo((): { label: string; toneClass: string } | undefined => {
    const label = effortChoiceLabel(value);
    if (!label) {
      return undefined; // Unrecognised value — neutral no-op (no badge rendered).
    }
    switch (value) {
      case 100000000: return { label, toneClass: styles.levelBadgeChoiceNone };
      case 100000001: return { label, toneClass: styles.levelBadgeHigh };
      case 100000002: return { label, toneClass: styles.levelBadgeChoiceHigh };
      case 100000003: return { label, toneClass: styles.levelBadgeMed };
      case 100000004: return { label, toneClass: styles.levelBadgeLow };
      default: return undefined;
    }
  }, [value, styles]);

  if (!display) {
    return null;
  }

  return (
    <span
      className={mergeClasses(styles.levelBadge, display.toneClass)}
      aria-label={`Selected effort: ${display.label}`}
    >
      {display.label}
    </span>
  );
};

// ---------------------------------------------------------------------------
// Props
// ---------------------------------------------------------------------------

export interface IEffortScoreCardProps {
  /** Effort scoring data to display */
  effort: ITodoEffortScore;
  /** Whether the data came from mock (shows notice when true) */
  isMockData: boolean;
  /**
   * Raw `sprk_effort` Choice value (task 010: None=100000000, Very
   * High=100000001, High=100000002, Medium=100000003, Low=100000004) —
   * optional, presentation-only surface of the user-selected label
   * alongside the existing SCORE-derived level badge above. Distinct data
   * source from `effort.level` (derived from `effort.score`, not this raw
   * Choice field). Added by task 012 (FR-03). Unset/unrecognised → no row
   * rendered (neutral no-op, not a misleading default).
   */
  effortChoice?: number | null;
}

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

export const EffortScoreCard: React.FC<IEffortScoreCardProps> = React.memo(
  ({ effort, isMockData, effortChoice }) => {
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

        {/* Selected-effort row (FR-03 / task 012) — the raw sprk_effort
            Choice selection, distinct from the score-derived level above. */}
        {typeof effortChoice === 'number' && (
          <div className={styles.scoreRow}>
            <Text size={100} className={styles.baseEffortLabel}>Selected:</Text>
            <EffortChoiceBadge value={effortChoice} />
          </div>
        )}

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
