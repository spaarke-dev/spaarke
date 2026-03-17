/**
 * QuickActionChips - Row of chip buttons above the SprkChat input bar.
 *
 * Displays up to 4 quick-action chips derived from the context mapping
 * response's inline actions. Chips are hidden when the pane is narrower
 * than 350px (NFR-04) via ResizeObserver on the provided containerRef.
 *
 * Each chip is a Fluent v9 Button (appearance="outline", size="small")
 * with an optional icon and label. Clicking a chip fires onChipClick
 * with the corresponding InlineAiAction.
 *
 * @see ADR-012 - Shared Component Library (no Xrm/ComponentFramework imports)
 * @see ADR-021 - Fluent UI v9; makeStyles; design tokens; dark mode required
 */

import * as React from 'react';
import { makeStyles, shorthands, tokens, Button, Divider } from '@fluentui/react-components';
import type { InlineAiAction } from '../InlineAiToolbar/inlineAiToolbar.types';

// ─────────────────────────────────────────────────────────────────────────────
// Constants
// ─────────────────────────────────────────────────────────────────────────────

/** Minimum pane width (px) required for chips to be visible. Per spec NFR-04. */
const MIN_PANE_WIDTH_FOR_CHIPS = 350;

/** Maximum number of chips to display at once. Per spec constraint. */
const MAX_CHIPS = 4;

// ─────────────────────────────────────────────────────────────────────────────
// Props
// ─────────────────────────────────────────────────────────────────────────────

/** Props for the QuickActionChips component. */
export interface IQuickActionChipsProps {
  /**
   * Ordered list of inline actions to render as chip buttons.
   * Up to MAX_CHIPS (4) are displayed; excess entries are ignored.
   */
  actions: InlineAiAction[];
  /**
   * Callback fired when the user clicks a chip.
   * Receives the full InlineAiAction for the clicked chip.
   */
  onChipClick: (action: InlineAiAction) => void;
  /**
   * Ref to the container element whose width is observed.
   * When the container is narrower than 350px, chips hide automatically
   * to preserve space for the chat input. The ref element must be
   * in the DOM when this component mounts.
   */
  containerRef: React.RefObject<HTMLElement>;
  /** Whether chips are interactable. Pass true while a stream is in progress. */
  disabled?: boolean;
  /** Optional CSS class applied to the chip row wrapper. */
  className?: string;
}

// ─────────────────────────────────────────────────────────────────────────────
// Styles
// ─────────────────────────────────────────────────────────────────────────────

const useStyles = makeStyles({
  root: {
    display: 'flex',
    flexDirection: 'column',
    backgroundColor: tokens.colorNeutralBackground1,
  },
  divider: {
    // Thin rule above the chip row to separate from the message list
    ...shorthands.padding(0, tokens.spacingHorizontalM),
  },
  chipRow: {
    display: 'flex',
    flexDirection: 'row',
    flexWrap: 'nowrap',
    alignItems: 'center',
    overflowX: 'auto',
    ...shorthands.gap(tokens.spacingHorizontalXS),
    ...shorthands.padding(tokens.spacingVerticalXS, tokens.spacingHorizontalM),
    // Hide scrollbar visually while keeping scrollability (cross-browser)
    scrollbarWidth: 'none',
    '::-webkit-scrollbar': {
      display: 'none',
    },
  },
  chip: {
    // Prevent chips from shrinking below their natural size on narrow viewports
    flexShrink: 0,
    // Ensure chips don't overflow their text label
    maxWidth: '160px',
    // Clip overflowing label text gracefully
    overflow: 'hidden',
    textOverflow: 'ellipsis',
    whiteSpace: 'nowrap',
  },
});

// ─────────────────────────────────────────────────────────────────────────────
// Component
// ─────────────────────────────────────────────────────────────────────────────

/**
 * QuickActionChips renders a horizontal row of chip buttons above the
 * SprkChat input bar, populated from the context mapping response's
 * inline actions. Hidden when the container pane is narrower than 350px.
 *
 * @example
 * ```tsx
 * const containerRef = React.useRef<HTMLDivElement>(null);
 *
 * <div ref={containerRef} style={{ height: '100%' }}>
 *   <QuickActionChips
 *     actions={contextActions.slice(0, 4)}
 *     onChipClick={(action) => handleChipAction(action)}
 *     containerRef={containerRef}
 *     disabled={isStreaming}
 *   />
 *   <SprkChatInput onSend={handleSend} />
 * </div>
 * ```
 */
export const QuickActionChips: React.FC<IQuickActionChipsProps> = ({
  actions,
  onChipClick,
  containerRef,
  disabled = false,
  className,
}) => {
  const styles = useStyles();

  // Track whether the pane is wide enough to show chips (NFR-04)
  const [isVisible, setIsVisible] = React.useState<boolean>(true);

  // Watch container width via ResizeObserver; hide chips if pane < 350px
  React.useEffect(() => {
    const element = containerRef.current;
    if (!element) {
      return;
    }

    const observer = new ResizeObserver((entries) => {
      for (const entry of entries) {
        const width = entry.contentRect.width;
        setIsVisible(width >= MIN_PANE_WIDTH_FOR_CHIPS);
      }
    });

    observer.observe(element);

    // Run once immediately to set initial state before the first resize fires
    const initialWidth = element.getBoundingClientRect().width;
    setIsVisible(initialWidth >= MIN_PANE_WIDTH_FOR_CHIPS);

    return () => {
      observer.disconnect();
    };
  }, [containerRef]);

  // When not visible or no actions, render nothing — no DOM footprint
  if (!isVisible || actions.length === 0) {
    return null;
  }

  // Limit to MAX_CHIPS (4) — per spec constraint
  const visibleActions = actions.slice(0, MAX_CHIPS);

  return (
    <div className={className ? `${styles.root} ${className}` : styles.root} data-testid="quick-action-chips-root">
      {/* Thin divider above chip row to visually separate from message list */}
      <Divider className={styles.divider} />

      {/* Horizontally scrollable chip row */}
      <div className={styles.chipRow} role="toolbar" aria-label="Quick actions" data-testid="quick-action-chips-row">
        {visibleActions.map((action) => (
          <Button
            key={action.id}
            className={styles.chip}
            appearance="outline"
            size="small"
            icon={action.icon}
            onClick={() => onChipClick(action)}
            disabled={disabled}
            title={action.description || action.label}
            aria-label={action.description || action.label}
            data-testid={`quick-action-chip-${action.id}`}
          >
            {action.label}
          </Button>
        ))}
      </div>
    </div>
  );
};

export default QuickActionChips;
