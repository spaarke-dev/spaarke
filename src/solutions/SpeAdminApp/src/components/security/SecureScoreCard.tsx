import * as React from "react";
import {
  makeStyles,
  tokens,
  Text,
  ProgressBar,
  Skeleton,
  SkeletonItem,
  Badge,
  Tooltip,
} from "@fluentui/react-components";
import { ShieldCheckmark20Regular, Info20Regular } from "@fluentui/react-icons";
import type { SecureScore } from "../../types/spe";

// ─────────────────────────────────────────────────────────────────────────────
// Styles
// ─────────────────────────────────────────────────────────────────────────────

const useStyles = makeStyles({
  /**
   * Card container — elevated surface with border, consistent with other admin cards.
   * Background and border use semantic design tokens (ADR-021, dark mode safe).
   */
  card: {
    display: "flex",
    flexDirection: "column",
    gap: tokens.spacingVerticalM,
    padding: tokens.spacingVerticalL,
    backgroundColor: tokens.colorNeutralBackground2,
    borderWidth: "1px",
    borderStyle: "solid",
    borderColor: tokens.colorNeutralStroke2,
    borderRadius: tokens.borderRadiusMedium,
  },

  /** Card header row: shield icon + title + info tooltip */
  cardHeader: {
    display: "flex",
    flexDirection: "row",
    alignItems: "center",
    gap: tokens.spacingHorizontalS,
  },

  cardIcon: {
    color: tokens.colorBrandForeground1,
    flexShrink: 0,
    display: "flex",
    alignItems: "center",
  },

  cardTitle: {
    flex: "1 1 auto",
    color: tokens.colorNeutralForeground1,
  },

  /** Score display row: large score number + denominator + percentage badge */
  scoreRow: {
    display: "flex",
    flexDirection: "row",
    alignItems: "baseline",
    gap: tokens.spacingHorizontalS,
  },

  scoreValue: {
    color: tokens.colorNeutralForeground1,
    fontWeight: tokens.fontWeightSemibold,
  },

  scoreDivider: {
    color: tokens.colorNeutralForeground3,
  },

  scoreMax: {
    color: tokens.colorNeutralForeground2,
  },

  /** Progress bar row */
  progressRow: {
    display: "flex",
    flexDirection: "column",
    gap: tokens.spacingVerticalXS,
  },

  progressLabel: {
    display: "flex",
    flexDirection: "row",
    justifyContent: "space-between",
  },

  progressLabelText: {
    color: tokens.colorNeutralForeground3,
  },

  /** Control scores breakdown — shown only when available */
  controlsSection: {
    display: "flex",
    flexDirection: "column",
    gap: tokens.spacingVerticalXS,
    borderTopWidth: "1px",
    borderTopStyle: "solid",
    borderTopColor: tokens.colorNeutralStroke2,
    paddingTop: tokens.spacingVerticalM,
  },

  controlsHeader: {
    color: tokens.colorNeutralForeground2,
  },

  controlRow: {
    display: "flex",
    flexDirection: "row",
    alignItems: "center",
    gap: tokens.spacingHorizontalS,
  },

  controlName: {
    flex: "1 1 auto",
    overflow: "hidden",
    textOverflow: "ellipsis",
    whiteSpace: "nowrap",
    color: tokens.colorNeutralForeground1,
  },

  controlScore: {
    color: tokens.colorNeutralForeground2,
    flexShrink: 0,
    whiteSpace: "nowrap",
  },

  /** Skeleton loading state */
  skeletonCard: {
    display: "flex",
    flexDirection: "column",
    gap: tokens.spacingVerticalM,
    padding: tokens.spacingVerticalL,
    backgroundColor: tokens.colorNeutralBackground2,
    borderWidth: "1px",
    borderStyle: "solid",
    borderColor: tokens.colorNeutralStroke2,
    borderRadius: tokens.borderRadiusMedium,
  },
});

// ─────────────────────────────────────────────────────────────────────────────
// Helpers
// ─────────────────────────────────────────────────────────────────────────────

/**
 * Determine ProgressBar color based on percentage.
 * Low scores use warning/danger semantic colors via the appearance prop pattern.
 */
function scoreColor(pct: number): "brand" | "warning" | "error" {
  if (pct >= 70) return "brand";
  if (pct >= 40) return "warning";
  return "error";
}

/**
 * Map score percentage to a Badge color token.
 * Uses Fluent v9 semantic colors — adapts automatically to dark mode.
 */
function scoreBadgeColor(pct: number): "brand" | "warning" | "danger" {
  if (pct >= 70) return "brand";
  if (pct >= 40) return "warning";
  return "danger";
}

/**
 * Format a number to one decimal place.
 */
function fmt(n: number): string {
  return Number.isInteger(n) ? String(n) : n.toFixed(1);
}

// ─────────────────────────────────────────────────────────────────────────────
// Props
// ─────────────────────────────────────────────────────────────────────────────

export interface SecureScoreCardProps {
  /** The secure score data to display. Null while loading. */
  score: SecureScore | null;
  /** Whether data is currently loading. Shows skeleton when true. */
  isLoading: boolean;
}

// ─────────────────────────────────────────────────────────────────────────────
// SecureScoreCard Component
// ─────────────────────────────────────────────────────────────────────────────

/**
 * SecureScoreCard — displays the tenant's Microsoft Secure Score as a progress
 * bar with percentage badge and optional per-control breakdown.
 *
 * Secure Score is a Microsoft 365 security measurement (0 to maxScore).
 * Higher scores indicate a stronger security posture.
 *
 * ADR compliance:
 *   - ADR-021: makeStyles + design tokens only; no hard-coded colors; dark mode safe
 *   - ADR-012: Fluent v9 Badge, ProgressBar, Skeleton — no custom UI library
 */
export const SecureScoreCard: React.FC<SecureScoreCardProps> = ({
  score,
  isLoading,
}) => {
  const styles = useStyles();

  // ── Loading state — skeleton placeholder ───────────────────────────────
  if (isLoading) {
    return (
      <div className={styles.skeletonCard}>
        <Skeleton>
          <SkeletonItem size={16} style={{ width: "200px" }} />
          <SkeletonItem size={48} style={{ width: "120px", marginTop: tokens.spacingVerticalM }} />
          <SkeletonItem size={8} style={{ marginTop: tokens.spacingVerticalS }} />
        </Skeleton>
      </div>
    );
  }

  // ── No data ────────────────────────────────────────────────────────────
  if (!score) {
    return (
      <div className={styles.card}>
        <div className={styles.cardHeader}>
          <span className={styles.cardIcon}>
            <ShieldCheckmark20Regular />
          </span>
          <Text size={400} weight="semibold" className={styles.cardTitle}>
            Secure Score
          </Text>
        </div>
        <Text size={300} style={{ color: tokens.colorNeutralForeground2 }}>
          Secure Score data is not available for the selected configuration.
        </Text>
      </div>
    );
  }

  const pct = Math.round(score.percentage);
  const color = scoreColor(pct);
  const badgeColor = scoreBadgeColor(pct);

  // Top 5 control scores (sorted by name alphabetically for deterministic order)
  const topControls = score.controlScores
    ? [...score.controlScores]
        .sort((a, b) => a.controlName.localeCompare(b.controlName))
        .slice(0, 5)
    : [];

  // ── Main render ────────────────────────────────────────────────────────
  return (
    <div className={styles.card}>
      {/* Card header */}
      <div className={styles.cardHeader}>
        <span className={styles.cardIcon}>
          <ShieldCheckmark20Regular />
        </span>
        <Text size={400} weight="semibold" className={styles.cardTitle}>
          Secure Score
        </Text>
        <Tooltip
          content="Microsoft Secure Score measures your organization's security posture. A higher score indicates better security controls."
          relationship="description"
        >
          <span style={{ display: "flex", alignItems: "center", color: tokens.colorNeutralForeground3 }}>
            <Info20Regular />
          </span>
        </Tooltip>
      </div>

      {/* Score numbers row */}
      <div className={styles.scoreRow}>
        <Text size={800} weight="semibold" className={styles.scoreValue}>
          {fmt(score.currentScore)}
        </Text>
        <Text size={400} className={styles.scoreDivider}>/</Text>
        <Text size={400} className={styles.scoreMax}>
          {fmt(score.maxScore)}
        </Text>
        <Badge
          color={badgeColor}
          appearance="filled"
          size="medium"
          style={{ marginLeft: tokens.spacingHorizontalS }}
        >
          {pct}%
        </Badge>
      </div>

      {/* Progress bar */}
      <div className={styles.progressRow}>
        <div className={styles.progressLabel}>
          <Text size={200} className={styles.progressLabelText}>
            Security posture
          </Text>
          <Text size={200} className={styles.progressLabelText}>
            As of {new Date(score.createdDateTime).toLocaleDateString()}
          </Text>
        </div>
        <ProgressBar
          value={score.currentScore / score.maxScore}
          color={color}
          thickness="large"
          aria-label={`Secure Score: ${pct}% (${fmt(score.currentScore)} of ${fmt(score.maxScore)})`}
        />
      </div>

      {/* Control scores breakdown (when available) */}
      {topControls.length > 0 && (
        <div className={styles.controlsSection}>
          <Text size={200} weight="semibold" className={styles.controlsHeader}>
            Top Controls
          </Text>
          {topControls.map((control) => {
            const controlPct =
              control.maxScore > 0
                ? Math.round((control.score / control.maxScore) * 100)
                : 0;
            return (
              <div key={control.controlName} className={styles.controlRow}>
                <Tooltip
                  content={control.description ?? control.controlName}
                  relationship="description"
                >
                  <Text size={200} className={styles.controlName}>
                    {control.controlName}
                  </Text>
                </Tooltip>
                <Text size={200} className={styles.controlScore}>
                  {fmt(control.score)} / {fmt(control.maxScore)}
                </Text>
                <Badge
                  color={scoreBadgeColor(controlPct)}
                  appearance="tint"
                  size="small"
                >
                  {controlPct}%
                </Badge>
              </div>
            );
          })}
          {(score.controlScores?.length ?? 0) > 5 && (
            <Text size={200} style={{ color: tokens.colorNeutralForeground3 }}>
              +{(score.controlScores?.length ?? 0) - 5} more controls in Microsoft 365 Security Center
            </Text>
          )}
        </div>
      )}
    </div>
  );
};
