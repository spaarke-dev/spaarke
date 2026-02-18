import * as React from "react";
import { Text, makeStyles, tokens } from "@fluentui/react-components";
import { ActionCard } from "./ActionCard";
import { QuickSummaryCard } from "./QuickSummaryCard";
import { ACTION_CARD_CONFIGS, IActionCardConfig } from "./getStartedConfig";
import { IQuickSummary } from "../../types";

export interface IGetStartedRowProps {
  /**
   * Portfolio quick-summary data from the BFF briefing endpoint.
   * Passed directly to QuickSummaryCard.
   */
  summary?: IQuickSummary;
  /** True while BFF data is loading. */
  isSummaryLoading?: boolean;
  /** Error message from the BFF fetch. */
  summaryError?: string;
  /**
   * Opens the full briefing dialog (wired in task 021).
   * Forwarded to QuickSummaryCard's onOpenBriefing prop.
   */
  onOpenBriefing?: () => void;
  /**
   * Map of card id → click handler.
   *
   * Keys must match the `id` field in ACTION_CARD_CONFIGS.
   * Tasks 024/025 populate this map; until then cards fire a no-op.
   */
  onCardClick?: Partial<Record<string, () => void>>;
  /**
   * Set of card ids that should be rendered in a disabled state.
   * Useful to disable a card while its action is in-flight.
   */
  disabledCards?: ReadonlySet<string>;
}

const useStyles = makeStyles({
  section: {
    display: "flex",
    flexDirection: "column",
    gap: tokens.spacingVerticalS,
  },
  sectionTitle: {
    color: tokens.colorNeutralForeground1,
  },
  /**
   * Outer scroll container. overflow-x: auto so the horizontal scrollbar
   * appears only when the content overflows. Scrollbar visibility is further
   * refined by the scrollbarGutter + hover trick below.
   */
  scrollContainer: {
    overflowX: "auto",
    overflowY: "hidden",
    // Reserve space for scrollbar so layout does not shift when it appears.
    scrollbarGutter: "stable",
    // Smooth momentum scrolling on touch devices (iOS)
    WebkitOverflowScrolling: "touch",
    // Hide scrollbar when not hovered; show on hover for discoverability
    "&:not(:hover)": {
      scrollbarWidth: "none",
      // WebKit fallback
      "::-webkit-scrollbar": {
        display: "none",
      },
    },
    "&:hover": {
      scrollbarWidth: "thin",
      scrollbarColor: `${tokens.colorNeutralStroke1} transparent`,
    },
    borderRadius: tokens.borderRadiusMedium,
    paddingBottom: tokens.spacingVerticalXS,
  },
  /**
   * Inner flex row. min-width: max-content prevents the row from wrapping
   * so cards stay on a single line and trigger horizontal scroll instead.
   */
  row: {
    display: "flex",
    flexDirection: "row",
    alignItems: "stretch",
    gap: tokens.spacingHorizontalM,
    minWidth: "max-content",
    paddingBottom: tokens.spacingVerticalXS,
  },
  /**
   * Vertical separator between QuickSummaryCard and action cards.
   * Uses a thin neutral border to keep visual grouping without hardcoding a color.
   */
  separator: {
    width: "1px",
    alignSelf: "stretch",
    backgroundColor: tokens.colorNeutralStroke2,
    flexShrink: 0,
  },
  actionCards: {
    display: "flex",
    flexDirection: "row",
    gap: tokens.spacingHorizontalM,
    alignItems: "stretch",
  },
});

/**
 * GetStartedRow — Block 1 of the Legal Operations Workspace.
 *
 * Renders:
 *   1. A section title ("Get Started")
 *   2. A horizontal scrollable row containing:
 *      - QuickSummaryCard (fixed 300px) — portfolio health at a glance
 *      - Vertical separator
 *      - 7 ActionCards for the most common legal-ops tasks
 *
 * The row scrolls horizontally when the viewport is narrower than the total
 * card width. The scrollbar is hidden by default and appears on hover for
 * a cleaner visual appearance on desktop.
 *
 * All click handlers are passed via onCardClick — stubs until tasks 024/025
 * wire the Create Matter dialog and Analysis Builder integrations.
 */
export const GetStartedRow: React.FC<IGetStartedRowProps> = ({
  summary,
  isSummaryLoading = false,
  summaryError,
  onOpenBriefing,
  onCardClick = {},
  disabledCards = new Set<string>(),
}) => {
  const styles = useStyles();

  return (
    <section className={styles.section} aria-label="Get Started">
      <Text size={400} weight="semibold" className={styles.sectionTitle}>
        Get Started
      </Text>

      <div className={styles.scrollContainer}>
        <div className={styles.row}>
          {/* Quick Summary card — fixed width, sits at the left */}
          <QuickSummaryCard
            summary={summary}
            isLoading={isSummaryLoading}
            error={summaryError}
            onOpenBriefing={onOpenBriefing}
          />

          {/* Visual separator */}
          <div className={styles.separator} aria-hidden="true" />

          {/* 7 action cards */}
          <div className={styles.actionCards} role="group" aria-label="Quick actions">
            {ACTION_CARD_CONFIGS.map((config: IActionCardConfig) => (
              <ActionCard
                key={config.id}
                icon={config.icon}
                label={config.label}
                ariaLabel={config.ariaLabel}
                onClick={onCardClick[config.id]}
                disabled={disabledCards.has(config.id)}
              />
            ))}
          </div>
        </div>
      </div>
    </section>
  );
};

GetStartedRow.displayName = "GetStartedRow";
