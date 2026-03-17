/**
 * InlineAiActions - Horizontal action button row for the InlineAiToolbar.
 *
 * Renders a row of Fluent UI v9 Button components (appearance="subtle") for
 * each inline AI action. Each button is wrapped in a Tooltip showing the
 * action description.
 *
 * CRITICAL: Uses `onMouseDown` (NOT `onClick`) to prevent the browser from
 * moving focus and collapsing the text selection before the action fires.
 * `event.preventDefault()` is called in the handler to suppress focus transfer.
 * The selected text is captured at mousedown time via `window.getSelection()`.
 *
 * @see InlineAiToolbar - parent container that receives positioning props
 * @see inlineAiToolbar.types.ts - shared type definitions
 * @see ADR-021 - Fluent UI v9; makeStyles; design tokens; dark mode
 * @see ADR-012 - Shared Component Library (no Xrm/ComponentFramework imports)
 */

import * as React from 'react';
import {
  Button,
  Tooltip,
  makeStyles,
  tokens,
} from '@fluentui/react-components';
import { InlineAiActionsProps, InlineAiAction } from './inlineAiToolbar.types';

// ─────────────────────────────────────────────────────────────────────────────
// Styles
// ─────────────────────────────────────────────────────────────────────────────

const useStyles = makeStyles({
  actionsRow: {
    display: 'flex',
    flexDirection: 'row',
    alignItems: 'center',
    gap: tokens.spacingHorizontalXXS,
  },
  actionButton: {
    // Keep buttons compact — icon + label in a subtle appearance
    minWidth: 'auto',
    height: '28px',
    paddingLeft: tokens.spacingHorizontalS,
    paddingRight: tokens.spacingHorizontalS,
    fontSize: tokens.fontSizeBase200,
    fontWeight: tokens.fontWeightRegular,
    color: tokens.colorNeutralForeground1,
    ':hover': {
      color: tokens.colorBrandForeground1,
    },
  },
  actionButtonIcon: {
    color: tokens.colorBrandForeground1,
  },
});

// ─────────────────────────────────────────────────────────────────────────────
// Helper — capture selected text at the point of mousedown
// ─────────────────────────────────────────────────────────────────────────────

function captureSelectedText(): string {
  return window.getSelection()?.toString() ?? '';
}

// ─────────────────────────────────────────────────────────────────────────────
// Component
// ─────────────────────────────────────────────────────────────────────────────

/**
 * InlineAiActions renders a horizontal row of action buttons for the
 * InlineAiToolbar. Each button uses `onMouseDown` to fire before the browser
 * collapses the text selection, preserving the user's highlighted text.
 *
 * @example
 * ```tsx
 * <InlineAiActions
 *   actions={DEFAULT_INLINE_ACTIONS}
 *   onAction={(action, text) => handleAction(action, text)}
 * />
 * ```
 */
export const InlineAiActions: React.FC<InlineAiActionsProps> = ({ actions, onAction }) => {
  const styles = useStyles();

  const handleMouseDown = React.useCallback(
    (action: InlineAiAction) => (event: React.MouseEvent<HTMLButtonElement>) => {
      // Prevent the browser from transferring focus to the button,
      // which would collapse the text selection before the action fires.
      event.preventDefault();

      // Capture selected text at the moment of mousedown — this is the last
      // moment the browser's Selection API still reflects the user's highlight.
      const selectedText = captureSelectedText();

      onAction(action, selectedText);
    },
    [onAction]
  );

  return (
    <div className={styles.actionsRow} role="group" aria-label="Inline AI actions">
      {actions.map(action => (
        <Tooltip
          key={action.id}
          content={action.description ?? action.label}
          relationship="description"
          positioning="below"
        >
          <Button
            appearance="subtle"
            size="small"
            className={styles.actionButton}
            icon={
              <span className={styles.actionButtonIcon} aria-hidden="true">
                {action.icon}
              </span>
            }
            onMouseDown={handleMouseDown(action)}
            aria-label={action.description ?? action.label}
            data-action-id={action.id}
            data-action-type={action.actionType}
          >
            {action.label}
          </Button>
        </Tooltip>
      ))}
    </div>
  );
};
