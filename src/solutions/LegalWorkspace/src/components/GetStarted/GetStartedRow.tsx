import * as React from "react";
import { makeStyles, tokens } from "@fluentui/react-components";
import { ActionCard } from "./ActionCard";
import { ACTION_CARD_CONFIGS, IActionCardConfig } from "./getStartedConfig";

export interface IGetStartedRowProps {
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
  /** Maximum number of action cards to display in the row. Default: show all. */
  maxVisible?: number;
}

const useStyles = makeStyles({
  /** Flex row — cards share space equally via flex: 1 1 0 on each card. */
  actionCards: {
    display: "flex",
    flexDirection: "row",
    gap: tokens.spacingHorizontalL,
    alignItems: "stretch",
    flex: "1 1 0",
    minWidth: 0,
  },
});

/**
 * GetStartedRow — Block 1 of the Legal Operations Workspace.
 *
 * Renders:
 *   1. A section title ("Get Started")
 *   2. A horizontal scrollable row containing 7 ActionCards for the most
 *      common legal-ops tasks
 *
 * The row scrolls horizontally when the viewport is narrower than the total
 * card width. The scrollbar is hidden by default and appears on hover for
 * a cleaner visual appearance on desktop.
 *
 * All click handlers are passed via onCardClick — stubs until tasks 024/025
 * wire the Create Matter dialog and Analysis Builder integrations.
 */
export const GetStartedRow: React.FC<IGetStartedRowProps> = ({
  onCardClick = {},
  disabledCards = new Set<string>(),
  maxVisible,
}) => {
  const styles = useStyles();

  const visibleConfigs = maxVisible
    ? ACTION_CARD_CONFIGS.slice(0, maxVisible)
    : ACTION_CARD_CONFIGS;

  return (
    <div className={styles.actionCards} role="group" aria-label="Quick actions">
      {visibleConfigs.map((config: IActionCardConfig) => (
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
  );
};

GetStartedRow.displayName = "GetStartedRow";
