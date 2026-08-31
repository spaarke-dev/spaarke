import * as React from "react";
import {
  makeStyles,
  tokens,
  Text,
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

  /**
   * Score numbers, now INSIDE the header row (UAT 2026-08-28: "move the score number so it is
   * aligned with the header"). Baseline alignment keeps "114.8" and "/ 265" sitting on one line
   * despite the large size difference.
   */
  scoreRow: {
    display: "flex",
    flexDirection: "row",
    alignItems: "baseline",
    gap: tokens.spacingHorizontalXS,
    flexShrink: 0,
  },

  /** Donut + caption block, replacing the old linear bar and the duplicate % badge. */
  gaugeRow: {
    display: "flex",
    flexDirection: "row",
    alignItems: "center",
    gap: tokens.spacingHorizontalL,
  },

  gaugeCaption: {
    display: "flex",
    flexDirection: "column",
    gap: tokens.spacingVerticalXXS,
  },

  gaugeCaptionText: {
    color: tokens.colorNeutralForeground2,
  },

  gaugeSubText: {
    color: tokens.colorNeutralForeground3,
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

/**
 * Resolve the arc colour to a Fluent semantic token. These are CSS custom properties, so they are
 * valid SVG `stroke` values AND they re-resolve on theme change — the donut follows dark mode for
 * free, which a hard-coded hex would not (ADR-021).
 */
function arcStroke(pct: number): string {
  if (pct >= 70) return tokens.colorPaletteGreenForeground1;
  if (pct >= 40) return tokens.colorPaletteYellowForeground1;
  return tokens.colorPaletteRedForeground1;
}

/**
 * ScoreDonut — the percentage as a ring with the number in the middle.
 *
 * Replaces the old linear ProgressBar + separate "43%" Badge. UAT 2026-08-28 asked for a chart and
 * noted the two were redundant ("replace the 43% or combine the two"), which they were: the bar, the
 * badge and the "Security posture" caption were three renderings of one number.
 *
 * Hand-rolled SVG on purpose — the app has no chart library, and a ~30-line ring is not worth a new
 * dependency (CLAUDE.md §11: default to reuse, and justify new surface).
 *
 * Accessibility: the ring is aria-hidden decoration; the accessible value lives on the role="img"
 * wrapper, so a screen reader hears one sentence instead of stray numbers.
 */
const ScoreDonut: React.FC<{ pct: number; current: number; max: number }> = ({
  pct,
  current,
  max,
}) => {
  const SIZE = 108;
  const STROKE = 12;
  const radius = (SIZE - STROKE) / 2;
  const circumference = 2 * Math.PI * radius;
  // Clamp: a Graph anomaly (current > max) must not draw an arc wrapping past 12 o'clock, which
  // would read as a LOWER score than it is.
  const safePct = Math.max(0, Math.min(100, pct));
  const filled = (safePct / 100) * circumference;

  return (
    <div
      role="img"
      aria-label={`Secure Score ${safePct}% — ${fmt(current)} of ${fmt(max)} points`}
      style={{ flexShrink: 0, lineHeight: 0 }}
    >
      <svg width={SIZE} height={SIZE} viewBox={`0 0 ${SIZE} ${SIZE}`} aria-hidden="true" focusable="false">
        {/* Track */}
        <circle
          cx={SIZE / 2}
          cy={SIZE / 2}
          r={radius}
          fill="none"
          stroke={tokens.colorNeutralStroke2}
          strokeWidth={STROKE}
        />
        {/* Value arc — rotated so it starts at 12 o'clock rather than 3 o'clock */}
        <circle
          cx={SIZE / 2}
          cy={SIZE / 2}
          r={radius}
          fill="none"
          stroke={arcStroke(safePct)}
          strokeWidth={STROKE}
          strokeLinecap="round"
          strokeDasharray={`${filled} ${circumference - filled}`}
          transform={`rotate(-90 ${SIZE / 2} ${SIZE / 2})`}
        />
        {/* Centre label */}
        <text
          x="50%"
          y="50%"
          textAnchor="middle"
          dominantBaseline="central"
          fill={tokens.colorNeutralForeground1}
          style={{
            fontSize: tokens.fontSizeBase600,
            fontWeight: tokens.fontWeightSemibold,
            fontFamily: tokens.fontFamilyBase,
          }}
        >
          {safePct}%
        </text>
      </svg>
    </div>
  );
};

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

  // Derive the percentage instead of reading `score.percentage`. The endpoint's SecureScoreDto
  // carries only currentScore, maxScore and averageComparativeScores — it has never sent a
  // `percentage`, so reading one produced Math.round(undefined) = NaN, rendered to operators as the
  // "NaN%" badge seen in UAT 2026-08-25. The value is fully derivable from two fields we do get.
  const pct =
    score.maxScore > 0
      ? Math.round((score.currentScore / score.maxScore) * 100)
      : 0;

  // Top 5 control scores (sorted by name alphabetically for deterministic order)
  const topControls = score.controlScores
    ? [...score.controlScores]
        .sort((a, b) => a.controlName.localeCompare(b.controlName))
        .slice(0, 5)
    : [];

  // ── Main render ────────────────────────────────────────────────────────
  return (
    <div className={styles.card}>
      {/*
        ONE header row. It previously duplicated the section header SecurityPage already renders
        above this card, so "Secure Score" appeared twice on screen (UAT 2026-08-28). SecurityPage
        now renders no section header for this card, and the score numbers moved up here so the
        header reads: [icon] Secure Score — 114.8 / 265.
      */}
      <div className={styles.cardHeader}>
        <span className={styles.cardIcon}>
          <ShieldCheckmark20Regular />
        </span>
        <Text size={400} weight="semibold">
          Secure Score
        </Text>
        <div className={styles.scoreRow}>
          <Text size={600} weight="semibold" className={styles.scoreValue}>
            {fmt(score.currentScore)}
          </Text>
          <Text size={400} className={styles.scoreDivider}>/</Text>
          <Text size={400} className={styles.scoreMax}>
            {fmt(score.maxScore)}
          </Text>
        </div>
        <span className={styles.cardTitle} />
        <Tooltip
          content="Microsoft Secure Score measures your organization's security posture. A higher score indicates better security controls."
          relationship="description"
        >
          <span style={{ display: "flex", alignItems: "center", color: tokens.colorNeutralForeground3 }}>
            <Info20Regular />
          </span>
        </Tooltip>
      </div>

      {/*
        The donut carries the percentage. It replaces BOTH the old linear ProgressBar and the
        separate "43%" Badge — per UAT those were the same number rendered twice, under a
        "Security posture" label that did not say what it measured. The caption now explains the
        measure instead of just naming it.
      */}
      <div className={styles.gaugeRow}>
        <ScoreDonut pct={pct} current={score.currentScore} max={score.maxScore} />
        <div className={styles.gaugeCaption}>
          <Text size={300} weight="semibold" className={styles.gaugeCaptionText}>
            Security posture
          </Text>
          <Text size={200} className={styles.gaugeSubText}>
            {fmt(score.currentScore)} of {fmt(score.maxScore)} available security points earned
            across this tenant&apos;s Microsoft 365 controls.
          </Text>
          {/*
            The endpoint does not return a snapshot date, so this rendered "As of Invalid Date" —
            a caption that looks like a broken timestamp rather than an absent one. Show the label
            only when a date actually arrives; say nothing otherwise.
          */}
          {score.createdDateTime &&
          !Number.isNaN(new Date(score.createdDateTime).getTime()) ? (
            <Text size={200} className={styles.gaugeSubText}>
              As of {new Date(score.createdDateTime).toLocaleDateString()}
            </Text>
          ) : null}
        </div>
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
