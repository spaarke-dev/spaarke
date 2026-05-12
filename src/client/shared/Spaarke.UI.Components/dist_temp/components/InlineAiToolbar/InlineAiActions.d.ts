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
import { InlineAiActionsProps } from './inlineAiToolbar.types';
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
export declare const InlineAiActions: React.FC<InlineAiActionsProps>;
//# sourceMappingURL=InlineAiActions.d.ts.map