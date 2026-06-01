/**
 * @spaarke/ai-widgets — ConfidenceIndicator
 *
 * Per-response confidence bar rendered below an AI message. Displays a thin
 * 4px bar whose color reflects the confidence level (high / medium / low),
 * with an optional expandable detail section showing the score and rationale.
 *
 * Design decisions:
 * - Compact by default: thin bar only (4px height), no intrusive text.
 * - Expandable: clicking the bar row shows score (as %) and rationale text.
 * - Low confidence adds a persistent disclaimer below the bar.
 * - All colors via Fluent v9 semantic status tokens — dark mode compatible (ADR-021).
 * - No hard-coded colors. No Fluent v8. makeStyles + tokens only.
 *
 * Token mapping:
 *   high   → colorStatusSuccessForeground1  (green)
 *   medium → colorStatusWarningForeground1  (yellow/amber)
 *   low    → colorStatusDangerForeground1   (orange/red)
 *
 * Task: AIPU2-091
 * AC:   FR-safety-confidence-ui
 *
 * @see ADR-021 — Fluent v9 design system (makeStyles + tokens only)
 */

import React, { useState, useCallback, useId } from 'react';
import { makeStyles, tokens, Text, Tooltip, mergeClasses } from '@fluentui/react-components';
import { ChevronDownRegular, ChevronUpRegular } from '@fluentui/react-icons';

// ---------------------------------------------------------------------------
// Public prop types
// ---------------------------------------------------------------------------

/**
 * Confidence level determined by the safety pipeline (AIPU2-065).
 *
 * - `high`   — strong source support, green indicator
 * - `medium` — partial source support, yellow/amber indicator
 * - `low`    — limited source support, orange indicator + disclaimer
 */
export type ConfidenceLevel = 'high' | 'medium' | 'low';

export interface ConfidenceIndicatorProps {
  /** Categorical confidence level — drives color and disclaimer visibility. */
  level: ConfidenceLevel;
  /**
   * Numeric confidence score in the range 0–100 (inclusive).
   * When provided, the filled portion of the bar is proportional to this value.
   * When absent, the bar fills 100% for high, 60% for medium, 30% for low
   * as a visual fallback.
   */
  score?: number;
  /**
   * Optional rationale text from the safety pipeline explaining the confidence
   * assessment. Shown inside the expandable detail section and as a Tooltip
   * on the bar when collapsed.
   */
  rationale?: string;
  /** Optional root class name override. */
  className?: string;
}

// ---------------------------------------------------------------------------
// Constants
// ---------------------------------------------------------------------------

/** Human-readable label for each confidence level. */
const LEVEL_LABEL: Record<ConfidenceLevel, string> = {
  high: 'High confidence',
  medium: 'Medium confidence',
  low: 'Low confidence',
};

/**
 * Fallback fill percentages used when `score` is not provided.
 * These are intentionally conservative so "unknown medium" never looks like high.
 */
const LEVEL_FALLBACK_FILL: Record<ConfidenceLevel, number> = {
  high: 85,
  medium: 55,
  low: 25,
};

/** Disclaimer shown only for low-confidence responses. */
const LOW_CONFIDENCE_DISCLAIMER = 'This response has limited source support. Please verify independently.';

// ---------------------------------------------------------------------------
// Styles
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  // ── Root container ────────────────────────────────────────────────────────
  root: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalXXS,
    // Sits immediately below the AI message — no side padding added here;
    // the parent message bubble owns horizontal spacing.
  },

  // ── Header row (bar label + chevron toggle) ───────────────────────────────
  headerRow: {
    display: 'flex',
    flexDirection: 'row',
    alignItems: 'center',
    gap: tokens.spacingHorizontalXS,
    cursor: 'pointer',
    userSelect: 'none',
    borderRadius: tokens.borderRadiusSmall,
    padding: `${tokens.spacingVerticalXXS} 0`,
    ':hover': {
      // Subtle background on hover to indicate interactivity
      backgroundColor: tokens.colorNeutralBackground2,
    },
    ':focus-visible': {
      outline: `2px solid ${tokens.colorBrandStroke1}`,
      outlineOffset: '2px',
    },
  },

  // ── Bar track + fill ─────────────────────────────────────────────────────
  barTrack: {
    flex: '1 1 auto',
    height: '4px',
    borderRadius: tokens.borderRadiusCircular,
    backgroundColor: tokens.colorNeutralBackground4,
    overflow: 'hidden',
    minWidth: '40px',
  },

  barFill: {
    height: '100%',
    borderRadius: tokens.borderRadiusCircular,
    transition: 'width 0.4s ease',
  },

  // Level-specific fill colors — semantic status tokens (dark mode compatible)
  barFillHigh: {
    backgroundColor: tokens.colorStatusSuccessForeground1,
  },
  barFillMedium: {
    backgroundColor: tokens.colorStatusWarningForeground1,
  },
  barFillLow: {
    backgroundColor: tokens.colorStatusDangerForeground1,
  },

  // ── Label text ────────────────────────────────────────────────────────────
  label: {
    fontSize: tokens.fontSizeBase100,
    lineHeight: tokens.lineHeightBase100,
    color: tokens.colorNeutralForeground3,
    flexShrink: 0,
    whiteSpace: 'nowrap',
  },

  labelHigh: {
    color: tokens.colorStatusSuccessForeground1,
  },
  labelMedium: {
    color: tokens.colorStatusWarningForeground1,
  },
  labelLow: {
    color: tokens.colorStatusDangerForeground1,
  },

  // ── Chevron icon ─────────────────────────────────────────────────────────
  chevron: {
    color: tokens.colorNeutralForeground3,
    fontSize: tokens.fontSizeBase100,
    flexShrink: 0,
    display: 'flex',
    alignItems: 'center',
  },

  // ── Expandable detail panel ───────────────────────────────────────────────
  detailPanel: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalXXS,
    paddingLeft: tokens.spacingHorizontalXS,
    paddingTop: tokens.spacingVerticalXXS,
    paddingBottom: tokens.spacingVerticalXXS,
    borderLeft: `2px solid ${tokens.colorNeutralStroke2}`,
  },

  scoreText: {
    fontSize: tokens.fontSizeBase100,
    color: tokens.colorNeutralForeground3,
  },

  rationaleText: {
    fontSize: tokens.fontSizeBase200,
    color: tokens.colorNeutralForeground2,
    lineHeight: tokens.lineHeightBase200,
  },

  // ── Low-confidence disclaimer ─────────────────────────────────────────────
  disclaimer: {
    fontSize: tokens.fontSizeBase200,
    color: tokens.colorStatusDangerForeground1,
    lineHeight: tokens.lineHeightBase200,
    fontStyle: 'italic',
  },
});

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

/** Returns the fill percentage (0–100) for the bar. */
function resolveFillPercent(score: number | undefined, level: ConfidenceLevel): number {
  if (score !== undefined) {
    // Clamp to [0, 100] in case the pipeline sends out-of-range values.
    return Math.max(0, Math.min(100, score));
  }
  return LEVEL_FALLBACK_FILL[level];
}

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

/**
 * ConfidenceIndicator — compact per-response confidence bar.
 *
 * Renders as a thin colored bar below an AI message. Clicking the bar row
 * expands a detail section showing the numeric score and rationale text.
 *
 * Low confidence responses always show a disclaimer below the bar regardless
 * of the expanded state, to ensure users are appropriately cautious.
 *
 * @example
 * // High confidence, no details
 * <ConfidenceIndicator level="high" score={92} />
 *
 * @example
 * // Low confidence with rationale
 * <ConfidenceIndicator
 *   level="low"
 *   score={28}
 *   rationale="Only 1 of 4 cited sources directly supports this claim."
 * />
 */
export const ConfidenceIndicator: React.FC<ConfidenceIndicatorProps> = ({ level, score, rationale, className }) => {
  const styles = useStyles();
  const headerId = useId();

  // Expanded state — clicking toggles the detail section.
  const [expanded, setExpanded] = useState(false);
  const toggle = useCallback(() => setExpanded(prev => !prev), []);

  // Computed values
  const fillPercent = resolveFillPercent(score, level);
  const label = LEVEL_LABEL[level];
  const hasDetails = score !== undefined || !!rationale;

  // ── Bar fill class by level ──
  const barFillClass = mergeClasses(
    styles.barFill,
    level === 'high' && styles.barFillHigh,
    level === 'medium' && styles.barFillMedium,
    level === 'low' && styles.barFillLow
  );

  // ── Label class by level ──
  const labelClass = mergeClasses(
    styles.label,
    level === 'high' && styles.labelHigh,
    level === 'medium' && styles.labelMedium,
    level === 'low' && styles.labelLow
  );

  // ── The bar track + fill (reused with / without tooltip) ──
  const barTrack = (
    <div className={styles.barTrack} aria-hidden="true">
      <div className={barFillClass} style={{ width: `${fillPercent}%` }} />
    </div>
  );

  return (
    <div className={mergeClasses(styles.root, className)} data-testid="confidence-indicator" data-level={level}>
      {/* ── Header row: bar + label + optional chevron ── */}
      <div
        className={styles.headerRow}
        role={hasDetails ? 'button' : undefined}
        aria-expanded={hasDetails ? expanded : undefined}
        aria-controls={hasDetails ? headerId : undefined}
        aria-label={`${label}${score !== undefined ? `, score ${score}%` : ''}`}
        tabIndex={hasDetails ? 0 : undefined}
        onClick={hasDetails ? toggle : undefined}
        onKeyDown={
          hasDetails
            ? e => {
                if (e.key === 'Enter' || e.key === ' ') {
                  e.preventDefault();
                  toggle();
                }
              }
            : undefined
        }
      >
        {/* Wrap bar in tooltip showing rationale when collapsed */}
        {rationale && !expanded ? (
          <Tooltip content={rationale} relationship="description" withArrow>
            {barTrack}
          </Tooltip>
        ) : (
          barTrack
        )}

        <Text className={labelClass}>{label}</Text>

        {/* Chevron only when there is expandable content */}
        {hasDetails && (
          <span className={styles.chevron} aria-hidden="true">
            {expanded ? <ChevronUpRegular fontSize={12} /> : <ChevronDownRegular fontSize={12} />}
          </span>
        )}
      </div>

      {/* ── Expanded detail panel ── */}
      {hasDetails && expanded && (
        <div id={headerId} className={styles.detailPanel} data-testid="confidence-detail">
          {score !== undefined && <Text className={styles.scoreText}>Score: {score}%</Text>}
          {rationale && <Text className={styles.rationaleText}>{rationale}</Text>}
        </div>
      )}

      {/* ── Low-confidence disclaimer (always visible for level === 'low') ── */}
      {level === 'low' && (
        <Text className={styles.disclaimer} role="note" aria-live="polite" data-testid="confidence-disclaimer">
          {LOW_CONFIDENCE_DISCLAIMER}
        </Text>
      )}
    </div>
  );
};

export default ConfidenceIndicator;
