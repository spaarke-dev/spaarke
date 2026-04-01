/**
 * TldrSection -- renders the AI-generated TL;DR summary at the top of the
 * Daily Briefing redesign.
 *
 * States:
 *   - Loading: 5 shimmer skeleton lines
 *   - Success: briefing narrative, optional top action, footer metadata
 *   - Unavailable: info icon + reason text
 *   - Error: inline error message
 *
 * Constraints:
 *   - ADR-021: Fluent v9 tokens only, dark mode via semantic tokens
 *   - AI-generated content labelled with "AI Insight" badge
 */

import * as React from "react";
import {
  makeStyles,
  tokens,
  Text,
  Badge,
  Skeleton,
  SkeletonItem,
} from "@fluentui/react-components";
import { InfoRegular } from "@fluentui/react-icons";

// ---------------------------------------------------------------------------
// Styles (Fluent v9 semantic tokens only -- ADR-021)
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  card: {
    backgroundColor: tokens.colorNeutralBackground2,
    borderRadius: tokens.borderRadiusLarge,
    paddingLeft: tokens.spacingHorizontalXL,
    paddingRight: tokens.spacingHorizontalXL,
    paddingTop: tokens.spacingVerticalL,
    paddingBottom: tokens.spacingVerticalL,
    position: "relative",
  },
  heading: {
    marginTop: "0",
    marginBottom: tokens.spacingVerticalM,
  },
  aiBadge: {
    position: "absolute",
    top: tokens.spacingVerticalL,
    right: tokens.spacingHorizontalXL,
  },
  briefingText: {
    color: tokens.colorNeutralForeground1,
    lineHeight: tokens.lineHeightBase400,
    display: "block",
    marginBottom: tokens.spacingVerticalS,
  },
  topAction: {
    display: "block",
    marginBottom: tokens.spacingVerticalS,
  },
  footer: {
    color: tokens.colorNeutralForeground3,
    display: "flex",
    gap: tokens.spacingHorizontalM,
    marginTop: tokens.spacingVerticalS,
  },
  fallbackContainer: {
    display: "flex",
    alignItems: "center",
    gap: tokens.spacingHorizontalS,
    color: tokens.colorNeutralForeground3,
    paddingTop: tokens.spacingVerticalS,
    paddingBottom: tokens.spacingVerticalS,
  },
  fallbackIcon: {
    fontSize: "16px",
    color: tokens.colorNeutralForeground3,
    flexShrink: 0,
  },
  errorText: {
    color: tokens.colorPaletteRedForeground1,
  },
  skeletonContainer: {
    display: "flex",
    flexDirection: "column",
    gap: tokens.spacingVerticalS,
  },
});

// ---------------------------------------------------------------------------
// Props
// ---------------------------------------------------------------------------

export interface TldrSectionProps {
  tldr: {
    briefing: string;
    topAction: string;
    categoryCount: number;
    priorityItemCount: number;
  } | null;
  isLoading: boolean;
  isUnavailable: boolean;
  unavailableReason: string | null;
  error: string | null;
  /** ISO timestamp of when the TL;DR was generated. */
  generatedAt: string | null;
}

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

function formatRelativeTime(isoTimestamp: string): string {
  const now = Date.now();
  const generated = new Date(isoTimestamp).getTime();
  const diffMs = now - generated;
  const diffMin = Math.floor(diffMs / 60_000);

  if (diffMin < 1) return "just now";
  if (diffMin < 60) return `${diffMin}m ago`;
  const diffHrs = Math.floor(diffMin / 60);
  if (diffHrs < 24) return `${diffHrs}h ago`;
  const diffDays = Math.floor(diffHrs / 24);
  return `${diffDays}d ago`;
}

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

export const TldrSection: React.FC<TldrSectionProps> = ({
  tldr,
  isLoading,
  isUnavailable,
  unavailableReason,
  error,
  generatedAt,
}) => {
  const styles = useStyles();

  // Loading state: skeleton with 5 shimmer lines
  if (isLoading) {
    return (
      <div className={styles.card}>
        <Text
          as="h2"
          size={500}
          weight="semibold"
          className={styles.heading}
        >
          TL;DR
        </Text>
        <Skeleton aria-label="Loading TL;DR summary">
          <div className={styles.skeletonContainer}>
            <SkeletonItem size={16} style={{ width: "100%" }} />
            <SkeletonItem size={16} style={{ width: "95%" }} />
            <SkeletonItem size={16} style={{ width: "90%" }} />
            <SkeletonItem size={16} style={{ width: "85%" }} />
            <SkeletonItem size={16} style={{ width: "60%" }} />
          </div>
        </Skeleton>
      </div>
    );
  }

  // Error state
  if (error) {
    return (
      <div className={styles.card}>
        <Text
          as="h2"
          size={500}
          weight="semibold"
          className={styles.heading}
        >
          TL;DR
        </Text>
        <div className={styles.fallbackContainer}>
          <InfoRegular className={styles.fallbackIcon} />
          <Text size={200} className={styles.errorText}>
            {error}
          </Text>
        </div>
      </div>
    );
  }

  // Unavailable state
  if (isUnavailable) {
    return (
      <div className={styles.card}>
        <Text
          as="h2"
          size={500}
          weight="semibold"
          className={styles.heading}
        >
          TL;DR
        </Text>
        <div className={styles.fallbackContainer}>
          <InfoRegular className={styles.fallbackIcon} />
          <Text size={200}>
            {unavailableReason ?? "AI summary is temporarily unavailable."}{" "}
            Your notifications are shown below.
          </Text>
        </div>
      </div>
    );
  }

  // No data guard
  if (!tldr) {
    return null;
  }

  // Success state
  return (
    <div className={styles.card}>
      <Text
        as="h2"
        size={500}
        weight="semibold"
        className={styles.heading}
      >
        TL;DR
      </Text>
      <Badge
        className={styles.aiBadge}
        appearance="tint"
        color="brand"
        size="small"
      >
        AI Insight
      </Badge>
      <Text size={300} className={styles.briefingText}>
        {tldr.briefing}
      </Text>
      {tldr.topAction && (
        <Text size={300} weight="semibold" className={styles.topAction}>
          {tldr.topAction}
        </Text>
      )}
      <div className={styles.footer}>
        {generatedAt && (
          <Text size={200}>Generated {formatRelativeTime(generatedAt)}</Text>
        )}
        <Text size={200}>
          {tldr.categoryCount} categories, {tldr.priorityItemCount} priority
          items
        </Text>
      </div>
    </div>
  );
};
