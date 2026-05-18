/**
 * @spaarke/ai-widgets — CitationBadge
 *
 * Inline badge component that shows the verification status of a legal citation
 * produced by CitationVerificationService (FR-403).
 *
 * Three variants:
 *   - verified  — CheckmarkCircle icon, colorPaletteGreenForeground1 (green)
 *   - unverified — Warning icon, colorStatusWarningForeground1 (orange/yellow)
 *   - partial    — ArrowSwap icon, colorPaletteBlueForeground2 (blue)
 *
 * Each badge is wrapped in a Tooltip that shows the verification provider name
 * and confidence level. For 'unverified' citations the tooltip also carries the
 * "Not found in available sources" message (FR-403 AC).
 *
 * Fluent v9 `Badge` component is used as the base with `appearance="tint"` so
 * that background and foreground both derive from the semantic palette, making
 * the badge dark-mode safe without any hard-coded colors (ADR-021).
 *
 * React 19. NOT PCF-safe.
 *
 * Task: AIPU2-090 (FR-403)
 */

import React from 'react';
import { Badge, Tooltip, makeStyles, tokens } from '@fluentui/react-components';
import {
  CheckmarkCircleRegular,
  WarningRegular,
  ArrowSwapRegular,
} from '@fluentui/react-icons';

// ---------------------------------------------------------------------------
// Domain types
// ---------------------------------------------------------------------------

/**
 * Verification status for a single citation.
 *
 * - `verified`   — the citation was found in an authoritative source index.
 * - `unverified` — the citation could NOT be found in any available source.
 * - `partial`    — the citation was found but with low confidence or missing
 *                  detail (e.g. case name matched but reporter/year diverge).
 */
export type CitationVerificationStatus = 'verified' | 'unverified' | 'partial';

/**
 * Data for a single citation verification result, matching the shape emitted
 * by CitationVerificationService in the safety_annotation SSE event.
 */
export interface CitationVerificationResult {
  /**
   * Stable citation identifier. Typically the [N] index from the message text
   * (e.g. `"1"` for `[1]`), but may be a slug for named citations.
   */
  id: string;
  /** Verification outcome. */
  status: CitationVerificationStatus;
  /**
   * Name of the IVerificationProvider that produced this result.
   * Shown in the badge tooltip (e.g. `"InternalIndexProvider"`).
   */
  providerName: string;
  /**
   * Confidence tier returned by the provider.
   * One of: `"high"` | `"medium"` | `"low"`.
   */
  confidence: 'high' | 'medium' | 'low';
  /**
   * Optional URL to the authoritative source found by the provider.
   * Shown in the tooltip when available.
   */
  sourceUrl?: string;
}

// ---------------------------------------------------------------------------
// Styles
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  /**
   * Verified badge: Fluent v9 green palette foreground token.
   * colorPaletteGreenForeground1 maps to the Fluent success-green in both
   * light and dark themes.
   */
  verifiedIcon: {
    color: tokens.colorPaletteGreenForeground1,
  },

  /**
   * Unverified badge: Fluent v9 warning foreground token.
   * colorStatusWarningForeground1 is orange in light mode, amber in dark —
   * conveys caution without being as severe as colorPaletteRedForeground1.
   */
  unverifiedIcon: {
    color: tokens.colorStatusWarningForeground1,
  },

  /**
   * Partial badge: Fluent v9 blue palette foreground token.
   * Represents "informational / uncertain" without implying error or success.
   */
  partialIcon: {
    color: tokens.colorPaletteBlueForeground2,
  },

  /** Cursor hint that the badge carries interactive tooltip content. */
  badgeWrapper: {
    cursor: 'help',
    display: 'inline-flex',
    alignItems: 'center',
    verticalAlign: 'middle',
    marginLeft: '2px',
  },
});

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

/** Static tooltip suffix for unverified citations (FR-403 AC). */
const UNVERIFIED_SUFFIX = 'Not found in available sources';

/**
 * Builds the tooltip content string for a citation result.
 * Format: "<status> · <provider> · Confidence: <level> [· UNVERIFIED_SUFFIX]"
 */
function buildTooltipContent(result: CitationVerificationResult): string {
  const statusLabel =
    result.status === 'verified'
      ? 'Verified'
      : result.status === 'partial'
      ? 'Partially verified'
      : 'Unverified';

  const parts = [
    statusLabel,
    `Source: ${result.providerName}`,
    `Confidence: ${result.confidence}`,
  ];

  if (result.status === 'unverified') {
    parts.push(UNVERIFIED_SUFFIX);
  }

  if (result.sourceUrl) {
    parts.push(`URL: ${result.sourceUrl}`);
  }

  return parts.join(' · ');
}

// ---------------------------------------------------------------------------
// Icon sub-component
// ---------------------------------------------------------------------------

interface BadgeIconProps {
  status: CitationVerificationStatus;
  styles: ReturnType<typeof useStyles>;
}

const BadgeIcon: React.FC<BadgeIconProps> = ({ status, styles }) => {
  switch (status) {
    case 'verified':
      return (
        <CheckmarkCircleRegular
          className={styles.verifiedIcon}
          fontSize={12}
          aria-hidden="true"
        />
      );
    case 'unverified':
      return (
        <WarningRegular
          className={styles.unverifiedIcon}
          fontSize={12}
          aria-hidden="true"
        />
      );
    case 'partial':
      return (
        <ArrowSwapRegular
          className={styles.partialIcon}
          fontSize={12}
          aria-hidden="true"
        />
      );
  }
};

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

export interface CitationBadgeProps {
  /**
   * Verification result for this specific citation.
   * The `id` is used as the React key when rendering a list of badges.
   */
  result: CitationVerificationResult;

  /** Optional CSS class applied to the outermost badge wrapper. */
  className?: string;
}

/**
 * CitationBadge renders a small inline Fluent v9 Badge for a single citation's
 * verification status.
 *
 * Badge variants by status:
 * - `verified`   → CheckmarkCircleRegular, green token, "success" appearance
 * - `unverified` → WarningRegular, orange token, "warning" appearance,
 *                  tooltip includes "Not found in available sources"
 * - `partial`    → ArrowSwapRegular, blue token, "informative" appearance
 *
 * All colors are sourced from Fluent v9 semantic palette tokens — no
 * hard-coded values — so badges are automatically correct in dark mode and
 * high-contrast themes.
 *
 * @example
 * // Verified citation:
 * <CitationBadge
 *   result={{
 *     id: "1",
 *     status: "verified",
 *     providerName: "InternalIndexProvider",
 *     confidence: "high",
 *   }}
 * />
 *
 * @example
 * // Unverified — tooltip includes "Not found in available sources":
 * <CitationBadge
 *   result={{
 *     id: "2",
 *     status: "unverified",
 *     providerName: "InternalIndexProvider",
 *     confidence: "low",
 *   }}
 * />
 */
export const CitationBadge: React.FC<CitationBadgeProps> = ({
  result,
  className,
}) => {
  const styles = useStyles();

  const tooltipContent = React.useMemo(
    () => buildTooltipContent(result),
    [result]
  );

  // Map status → Fluent Badge appearance token.
  // 'tint' uses the palette background/foreground tokens directly,
  // which is dark-mode safe.
  const badgeAppearance = 'tint' as const;
  const badgeColor =
    result.status === 'verified'
      ? 'success'
      : result.status === 'unverified'
      ? 'warning'
      : 'informative';

  const ariaLabel =
    result.status === 'verified'
      ? `Citation ${result.id} verified`
      : result.status === 'unverified'
      ? `Citation ${result.id} unverified — not found in available sources`
      : `Citation ${result.id} partially verified`;

  return (
    <Tooltip
      content={tooltipContent}
      relationship="label"
      withArrow
    >
      <span
        className={`${styles.badgeWrapper}${className ? ` ${className}` : ''}`}
        data-testid={`citation-badge-${result.id}`}
        data-status={result.status}
      >
        <Badge
          appearance={badgeAppearance}
          color={badgeColor}
          size="small"
          icon={<BadgeIcon status={result.status} styles={styles} />}
          aria-label={ariaLabel}
        />
      </span>
    </Tooltip>
  );
};

export default CitationBadge;
