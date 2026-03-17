/**
 * InlineAiToolbar - Barrel Exports
 *
 * Re-exports all public surface for the InlineAiToolbar component group:
 *   - InlineAiToolbar        (floating container, absolutely positioned)
 *   - InlineAiActions        (horizontal button row)
 *   - Types and interfaces   (InlineAiAction, InlineAiToolbarProps, etc.)
 *   - DEFAULT_INLINE_ACTIONS (default action set)
 *
 * Consumers import from '@spaarke/ui-components':
 *   import { InlineAiToolbar, DEFAULT_INLINE_ACTIONS } from '@spaarke/ui-components';
 *
 * @see ADR-012 - Shared Component Library (no Xrm/ComponentFramework imports)
 * @see ADR-021 - Fluent UI v9
 */

export { InlineAiToolbar } from './InlineAiToolbar';
export { InlineAiActions } from './InlineAiActions';
export type {
  InlineAiActionType,
  InlineAiAction,
  InlineAiToolbarProps,
  InlineAiActionsProps,
  InlineActionBroadcastEvent,
} from './inlineAiToolbar.types';
export { DEFAULT_INLINE_ACTIONS } from './inlineAiToolbar.types';
