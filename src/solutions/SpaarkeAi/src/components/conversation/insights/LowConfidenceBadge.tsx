/**
 * LowConfidenceBadge — Client-side low-confidence advisory for Insights
 * Assistant responses (R5 task 028 / D2-18).
 *
 * Per spec §8.2 D5 / FR-15: when the Insights response envelope carries a
 * `confidence` value below the configured threshold (default 0.6, configurable
 * via {@link setInsightsRendererConfig}), the renderer surfaces a Fluent v9
 * `Badge` with the exact text "Low confidence — verify before relying".
 *
 * Design choice: `Badge` (compact header-adjacent pill) over `MessageBar`
 * (full-width banner). Rationale:
 *
 *   - The badge is rendered at the TOP of the response container, BEFORE the
 *     case-specific sub-renderer (playbook / RAG / decline / empty). A compact
 *     Badge integrates visually with the response header without dominating
 *     vertical space — appropriate for an advisory cue, not an actionable
 *     warning.
 *   - The `MessageBar intent="warning"` slot is already used by
 *     `DeclineResponseRenderer` for playbook-decline framing. Reusing the same
 *     primitive for low-confidence would create visual ambiguity between two
 *     semantically distinct cases.
 *   - Spec FR-15 explicitly permits "Fluent v9 `Badge` OR `MessageBar`" — the
 *     Badge choice is documented here + in task-028 evidence notes.
 *
 * The badge:
 *   - Renders ONLY when `confidence` is a finite number in `[0, 1]` AND
 *     `confidence < threshold`. All other cases → `null` (no DOM node).
 *   - Uses Fluent v9 `appearance="filled" color="warning"` semantic slot —
 *     adapts to dark mode automatically per ADR-021.
 *   - Carries the exact text "Low confidence — verify before relying" with
 *     U+2014 em-dash (per spec FR-15 + SC-14; no trailing punctuation).
 *
 * ADR-021 (Fluent v9 + dark mode): semantic tokens + color slot only;
 * no hex / rgba / Fluent v8 imports.
 * ADR-022 (React 19): functional component; typed props; no `any`.
 * ADR-013 §3.5 (Zone B boundary): consumes only the discriminated-union
 * `confidence` field — no Insights-internal type referenced.
 * ADR-018 (Flag Scope Discipline): threshold is a CONFIGURATION VALUE
 * (numeric); the badge itself is unconditional UX. No feature flag.
 *
 * @see types.ts — `InsightsResponseBase.confidence`
 * @see ../../../config/insightsRendererConfig.ts — threshold default + override
 * @see projects/spaarke-ai-platform-unification-r5/spec.md FR-15 + SC-14
 */

import * as React from 'react';
import { Badge, makeStyles, tokens } from '@fluentui/react-components';
import { getInsightsRendererConfig } from '../../../config/insightsRendererConfig';

// ---------------------------------------------------------------------------
// Exported constants
// ---------------------------------------------------------------------------

/**
 * The EXACT text rendered inside the badge. Exported so tests can assert
 * `queryByText(LOW_CONFIDENCE_BADGE_TEXT)` without re-typing the string and
 * accidentally diverging on dash glyph or whitespace.
 *
 * Glyph note: the dash is U+2014 EM DASH (—), NOT U+2013 EN DASH (–) and NOT
 * U+002D HYPHEN-MINUS (-). The spec text uses em-dash for the parenthetical
 * "verify before relying" clause.
 */
export const LOW_CONFIDENCE_BADGE_TEXT = 'Low confidence — verify before relying';

/**
 * Pure predicate — returns true iff the badge should render given a
 * confidence value and threshold. Extracted for direct unit-testing (cheaper
 * than mounting the full component for boundary checks).
 *
 * Defensive contract:
 *   - `null` / `undefined` → false (no signal)
 *   - `NaN` → false
 *   - Out of `[0, 1]` range → false (treat as malformed; no signal)
 *   - `confidence === threshold` → false (badge fires strictly BELOW threshold)
 *   - `confidence < threshold` → true
 */
export function shouldShowLowConfidenceBadge(
  confidence: number | null | undefined,
  threshold: number,
): boolean {
  return (
    typeof confidence === 'number'
    && Number.isFinite(confidence)
    && confidence >= 0
    && confidence <= 1
    && confidence < threshold
  );
}

// ---------------------------------------------------------------------------
// Props
// ---------------------------------------------------------------------------

export interface LowConfidenceBadgeProps {
  /**
   * Response confidence in `[0, 1]`. Sourced from `response.confidence` on
   * the discriminated-union Insights response envelope. May be `null`,
   * `undefined`, or out of range — see {@link shouldShowLowConfidenceBadge}
   * for the defensive contract.
   */
  readonly confidence: number | null | undefined;
  /**
   * Optional threshold override. When omitted, the threshold is read from
   * {@link getInsightsRendererConfig} — default 0.6 per spec FR-15. Setting
   * a per-instance threshold is useful for tests + future operator A/B
   * scenarios; production code should prefer the config-module value.
   */
  readonly threshold?: number;
}

// ---------------------------------------------------------------------------
// Styles — Fluent v9 semantic tokens only (ADR-021)
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  /**
   * Wrapper: gives the badge a small bottom margin so it doesn't crowd the
   * sub-renderer that follows. Uses semantic spacing token only.
   */
  wrapper: {
    marginBottom: tokens.spacingVerticalS,
  },
});

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

/**
 * Renders the Fluent v9 low-confidence badge when the input confidence is
 * below the configured (or prop-supplied) threshold. Returns `null` (no DOM
 * node) otherwise.
 *
 * Threshold resolution order:
 *   1. `props.threshold` (if provided)
 *   2. `getInsightsRendererConfig().confidenceThreshold`
 *   3. `0.6` (hard-coded fallback; should be unreachable since the config
 *      module's default is also 0.6)
 */
export const LowConfidenceBadge: React.FC<LowConfidenceBadgeProps> = ({
  confidence,
  threshold,
}) => {
  const styles = useStyles();

  // Read threshold lazily — supports reactive reconfiguration via
  // `setInsightsRendererConfig` between renders.
  const resolvedThreshold = threshold
    ?? getInsightsRendererConfig().confidenceThreshold
    ?? 0.6;

  if (!shouldShowLowConfidenceBadge(confidence, resolvedThreshold)) {
    return null;
  }

  return (
    <div
      className={styles.wrapper}
      data-testid="low-confidence-badge-wrapper"
    >
      <Badge
        appearance="filled"
        color="warning"
        size="medium"
        data-testid="low-confidence-badge"
        aria-label={LOW_CONFIDENCE_BADGE_TEXT}
      >
        {LOW_CONFIDENCE_BADGE_TEXT}
      </Badge>
    </div>
  );
};

export default LowConfidenceBadge;
