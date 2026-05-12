/**
 * SprkChatSuggestions - Follow-up suggestion chips for SprkChat
 *
 * Renders 2-3 clickable follow-up suggestion chips below the latest
 * assistant message. Chips use Fluent UI v9 InteractionTag for a
 * pill/chip appearance with keyboard navigation (Arrow Left/Right,
 * Enter/Space to select).
 *
 * Animation: fade-in + slide-up (200ms CSS transition) controlled
 * by the `visible` prop.
 *
 * @see ADR-012 - Shared Component Library
 * @see ADR-021 - Fluent UI v9; makeStyles; design tokens; dark mode
 * @see ADR-022 - React 16 APIs only
 */
import * as React from 'react';
import { ISprkChatSuggestionsProps } from './types';
/**
 * SprkChatSuggestions - Renders clickable follow-up suggestion chips.
 *
 * @example
 * ```tsx
 * <SprkChatSuggestions
 *   suggestions={["Summarize the key points", "What are the risks?"]}
 *   onSelect={(text) => sendMessage(text)}
 *   visible={!isStreaming}
 * />
 * ```
 */
export declare const SprkChatSuggestions: React.FC<ISprkChatSuggestionsProps>;
export default SprkChatSuggestions;
//# sourceMappingURL=SprkChatSuggestions.d.ts.map