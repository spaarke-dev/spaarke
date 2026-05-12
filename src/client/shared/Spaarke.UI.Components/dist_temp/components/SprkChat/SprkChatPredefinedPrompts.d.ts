/**
 * SprkChatPredefinedPrompts - Predefined prompt suggestion buttons
 *
 * Displays a set of clickable prompt buttons shown before the user sends their first message.
 * Clicking a prompt inserts the full prompt text as a message.
 *
 * @see ADR-021 - Fluent UI v9; makeStyles; design tokens; dark mode
 * @see ADR-022 - React 16 APIs only
 */
import * as React from 'react';
import { ISprkChatPredefinedPromptsProps } from './types';
/**
 * SprkChatPredefinedPrompts - Shows clickable prompt suggestions.
 *
 * @example
 * ```tsx
 * <SprkChatPredefinedPrompts
 *   prompts={[
 *     { key: "summary", label: "Summarize this document", prompt: "Summarize..." },
 *     { key: "review", label: "Review for issues", prompt: "Review..." }
 *   ]}
 *   onSelect={(prompt) => sendMessage(prompt)}
 * />
 * ```
 */
export declare const SprkChatPredefinedPrompts: React.FC<ISprkChatPredefinedPromptsProps>;
export default SprkChatPredefinedPrompts;
//# sourceMappingURL=SprkChatPredefinedPrompts.d.ts.map