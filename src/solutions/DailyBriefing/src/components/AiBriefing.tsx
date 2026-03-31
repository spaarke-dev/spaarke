/**
 * AiBriefing — renders the AI-generated briefing summary at the top of
 * the Daily Digest. Shows a 3-4 sentence narrative with priority action items.
 *
 * States:
 *   - Loading: skeleton placeholder with shimmer
 *   - Success: briefing narrative + priority items + "AI Insight" badge
 *   - Unavailable: graceful fallback message (AI service down)
 *   - Error: inline error message
 *   - No data: hidden (when notifications not yet loaded)
 *
 * Constraints:
 *   - MUST label AI-generated content clearly (project constraint)
 *   - MUST use Fluent v9 tokens for styling (ADR-021)
 *   - MUST support dark mode via semantic tokens
 */

import * as React from "react";
import {
  makeStyles,
  tokens,
  Body1,
  Text,
  Caption1,
  Badge,
  Card,
  CardHeader,
  Skeleton,
  SkeletonItem,
} from "@fluentui/react-components";
import {
  SparkleRegular,
  InfoRegular,
} from "@fluentui/react-icons";
import type { UseAiBriefingResult } from "../hooks/useAiBriefing";

// ---------------------------------------------------------------------------
// Styles (Fluent v9 tokens — no hard-coded colors)
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  card: {
    backgroundColor: tokens.colorNeutralBackground2,
    borderRadius: tokens.borderRadiusMedium,
    padding: tokens.spacingVerticalM,
    marginBottom: tokens.spacingVerticalM,
  },
  headerRow: {
    display: "flex",
    alignItems: "center",
    gap: tokens.spacingHorizontalS,
    marginBottom: tokens.spacingVerticalS,
  },
  sparkleIcon: {
    color: tokens.colorBrandForeground1,
    fontSize: "20px",
  },
  aiBadge: {
    marginLeft: tokens.spacingHorizontalXS,
  },
  briefingText: {
    color: tokens.colorNeutralForeground1,
    lineHeight: tokens.lineHeightBase400,
  },
  timestamp: {
    color: tokens.colorNeutralForeground3,
    marginTop: tokens.spacingVerticalXS,
  },
  fallbackContainer: {
    display: "flex",
    alignItems: "center",
    gap: tokens.spacingHorizontalS,
    color: tokens.colorNeutralForeground3,
    padding: `${tokens.spacingVerticalS} 0`,
  },
  fallbackIcon: {
    fontSize: "16px",
    color: tokens.colorNeutralForeground3,
  },
  errorText: {
    color: tokens.colorPaletteRedForeground1,
  },
  // Skeleton styles
  skeletonContainer: {
    display: "flex",
    flexDirection: "column",
    gap: tokens.spacingVerticalS,
    padding: tokens.spacingVerticalM,
  },
  skeletonHeader: {
    display: "flex",
    alignItems: "center",
    gap: tokens.spacingHorizontalS,
  },
});

// ---------------------------------------------------------------------------
// Props
// ---------------------------------------------------------------------------

export interface AiBriefingProps {
  /** Result from useAiBriefing hook. */
  briefingResult: UseAiBriefingResult;
  /** Loading state of the underlying notification data. */
  dataLoading: boolean;
}

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

/**
 * AI Briefing card component for the Daily Digest.
 * Renders at the top of the digest page above channel cards.
 */
export const AiBriefing: React.FC<AiBriefingProps> = ({
  briefingResult,
  dataLoading,
}) => {
  const styles = useStyles();

  const { briefing, isLoading, isUnavailable, unavailableReason, error } =
    briefingResult;

  // Don't render anything while notification data is still loading
  // (the briefing depends on notification data)
  if (dataLoading) {
    return <AiBriefingSkeleton />;
  }

  // Loading state: show skeleton
  if (isLoading) {
    return <AiBriefingSkeleton />;
  }

  // Error state: show inline error
  if (error) {
    return (
      <Card className={styles.card}>
        <div className={styles.fallbackContainer}>
          <InfoRegular className={styles.fallbackIcon} />
          <Caption1 className={styles.errorText}>
            Unable to generate AI briefing. Your notifications are shown below.
          </Caption1>
        </div>
      </Card>
    );
  }

  // Unavailable state: graceful fallback
  if (isUnavailable) {
    return (
      <Card className={styles.card}>
        <div className={styles.fallbackContainer}>
          <InfoRegular className={styles.fallbackIcon} />
          <Caption1>
            {unavailableReason ?? "AI briefing is temporarily unavailable."}
            {" "}Your notifications are shown below.
          </Caption1>
        </div>
      </Card>
    );
  }

  // No briefing data (shouldn't happen if not loading/error/unavailable, but guard)
  if (!briefing) {
    return null;
  }

  // Success state: render briefing
  const generatedAt = new Date(briefing.generatedAtUtc);
  const timeString = generatedAt.toLocaleTimeString(undefined, {
    hour: "2-digit",
    minute: "2-digit",
  });

  return (
    <Card className={styles.card}>
      <CardHeader
        image={<SparkleRegular className={styles.sparkleIcon} />}
        header={
          <div className={styles.headerRow}>
            <Text weight="semibold" size={400}>Your Daily Briefing</Text>
            <Badge
              className={styles.aiBadge}
              appearance="tint"
              color="brand"
              size="small"
            >
              AI Insight
            </Badge>
          </div>
        }
      />
      <Body1 className={styles.briefingText}>{briefing.briefing}</Body1>
      <Caption1 className={styles.timestamp}>
        Generated at {timeString}
        {briefing.categoryCount > 0 &&
          ` from ${briefing.categoryCount} categories`}
        {briefing.priorityItemCount > 0 &&
          ` and ${briefing.priorityItemCount} priority items`}
      </Caption1>
    </Card>
  );
};

// ---------------------------------------------------------------------------
// Loading skeleton sub-component
// ---------------------------------------------------------------------------

const AiBriefingSkeleton: React.FC = () => {
  const styles = useStyles();

  return (
    <Card className={styles.card}>
      <Skeleton aria-label="Loading AI briefing">
        <div className={styles.skeletonContainer}>
          <div className={styles.skeletonHeader}>
            <SkeletonItem shape="circle" size={20} />
            <SkeletonItem size={16} style={{ width: "160px" }} />
          </div>
          <SkeletonItem size={16} style={{ width: "100%" }} />
          <SkeletonItem size={16} style={{ width: "95%" }} />
          <SkeletonItem size={16} style={{ width: "70%" }} />
          <SkeletonItem size={12} style={{ width: "200px" }} />
        </div>
      </Skeleton>
    </Card>
  );
};
