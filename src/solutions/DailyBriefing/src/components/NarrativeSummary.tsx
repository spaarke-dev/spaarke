import * as React from "react";
import {
  makeStyles,
  tokens,
  Body1,
  Caption1,
  mergeClasses,
} from "@fluentui/react-components";
import type {
  ChannelGroup,
  NotificationCategory,
} from "../types/notifications";

// ---------------------------------------------------------------------------
// Styles (Fluent v9 design tokens — ADR-021)
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  root: {
    display: "flex",
    flexDirection: "column",
    gap: tokens.spacingVerticalXS,
    padding: tokens.spacingVerticalS,
    paddingLeft: tokens.spacingHorizontalM,
    paddingRight: tokens.spacingHorizontalM,
    backgroundColor: tokens.colorNeutralBackground2,
    borderRadius: tokens.borderRadiusMedium,
  },
  narrative: {
    color: tokens.colorNeutralForeground1,
    lineHeight: tokens.lineHeightBase300,
  },
  highlight: {
    fontWeight: tokens.fontWeightSemibold,
    color: tokens.colorBrandForeground1,
  },
  timestamp: {
    color: tokens.colorNeutralForeground3,
  },
});

// ---------------------------------------------------------------------------
// Template-based narrative generation
// ---------------------------------------------------------------------------

/** Template configuration for each notification category. */
interface ChannelTemplate {
  /** Singular noun for this channel (e.g., "task overdue"). */
  singular: string;
  /** Plural noun for this channel (e.g., "tasks overdue"). */
  plural: string;
}

/**
 * Template strings per channel type. These control how each category
 * appears in the narrative sentence.
 */
const CHANNEL_TEMPLATES: Record<NotificationCategory, ChannelTemplate> = {
  "tasks-overdue": { singular: "task overdue", plural: "tasks overdue" },
  "tasks-due-soon": { singular: "task due soon", plural: "tasks due soon" },
  "new-documents": { singular: "new document", plural: "new documents" },
  "new-emails": { singular: "new email", plural: "new emails" },
  "new-events": { singular: "upcoming event", plural: "upcoming events" },
  "matter-activity": {
    singular: "matter update",
    plural: "matter updates",
  },
  "work-assignments": {
    singular: "new assignment",
    plural: "new assignments",
  },
  system: { singular: "system notice", plural: "system notices" },
};

/**
 * Build the count-based phrase for a single channel.
 * E.g., "3 tasks overdue" or "1 new document".
 */
function buildChannelPhrase(count: number, template: ChannelTemplate): string {
  return `${count} ${count === 1 ? template.singular : template.plural}`;
}

/**
 * Count the distinct matters (regarding records) across all channel groups.
 * Returns 0 if no regarding info is present.
 */
function countDistinctMatters(groups: ChannelGroup[]): number {
  const matterIds = new Set<string>();
  for (const group of groups) {
    for (const item of group.items) {
      if (item.regardingId) {
        matterIds.add(item.regardingId);
      }
    }
  }
  return matterIds.size;
}

/**
 * Join an array of strings with commas and "and" before the last item.
 * E.g., ["a", "b", "c"] => "a, b, and c"
 */
function joinWithAnd(parts: string[]): string {
  if (parts.length === 0) return "";
  if (parts.length === 1) return parts[0];
  if (parts.length === 2) return `${parts[0]} and ${parts[1]}`;
  return `${parts.slice(0, -1).join(", ")}, and ${parts[parts.length - 1]}`;
}

/**
 * Generate the full narrative TL;DR string from channel groups.
 *
 * Examples:
 *   - "You have 3 tasks overdue and 5 new documents across 2 matters."
 *   - "You have 1 new email."
 *   - "You have 2 tasks overdue, 3 new documents, and 1 upcoming event across 4 matters."
 *   - "You're all caught up — no new notifications."
 */
export function generateNarrative(groups: ChannelGroup[]): string {
  // Filter to groups that actually have items, sorted by channel order
  const activeGroups = groups
    .filter((g) => g.items.length > 0)
    .sort((a, b) => a.meta.order - b.meta.order);

  if (activeGroups.length === 0) {
    return "You\u2019re all caught up \u2014 no new notifications.";
  }

  // Build channel phrases
  const phrases = activeGroups.map((g) => {
    const template = CHANNEL_TEMPLATES[g.meta.category];
    return buildChannelPhrase(g.items.length, template);
  });

  const channelSentence = `You have ${joinWithAnd(phrases)}`;

  // Count distinct matters for the "across N matters" suffix
  const matterCount = countDistinctMatters(activeGroups);
  if (matterCount > 1) {
    return `${channelSentence} across ${matterCount} matters.`;
  }
  if (matterCount === 1) {
    return `${channelSentence} across 1 matter.`;
  }

  return `${channelSentence}.`;
}

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

export interface NarrativeSummaryProps {
  /** Channel groups from the notification data hook. */
  groups: ChannelGroup[];
  /** Optional CSS class name for the root element. */
  className?: string;
}

/**
 * NarrativeSummary renders a Level 1 template-based TL;DR at the top
 * of the Daily Digest view. It summarizes notification counts per channel
 * into a human-readable sentence.
 *
 * ADR-021: Uses Fluent v9 tokens for all styling; supports dark mode.
 */
export const NarrativeSummary: React.FC<NarrativeSummaryProps> = ({
  groups,
  className,
}) => {
  const styles = useStyles();
  const narrative = React.useMemo(() => generateNarrative(groups), [groups]);

  const now = new Date();
  const timeLabel = now.toLocaleTimeString(undefined, {
    hour: "numeric",
    minute: "2-digit",
  });

  return (
    <div className={mergeClasses(styles.root, className)}>
      <Body1 className={styles.narrative}>{narrative}</Body1>
      <Caption1 className={styles.timestamp}>As of {timeLabel}</Caption1>
    </div>
  );
};
