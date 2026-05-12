/**
 * SprkChatInput - Chat input area with send button and slash command support
 *
 * Features:
 * - Textarea with send button
 * - Ctrl+Enter to send (Enter for newline)
 * - Disabled during streaming
 * - Character count indicator
 * - [/] button in toolbar opens SlashCommandMenu
 * - Typing '/' as the first character also opens SlashCommandMenu with type-ahead filtering
 * - Esc closes the menu; selecting a command writes the trigger into the input
 *
 * @see ADR-021 - Fluent UI v9; makeStyles; design tokens; dark mode
 * @see ADR-022 - React 16 APIs only
 * @see spec-FR-09 - Slash command menu with type-ahead filtering and keyboard nav
 */
import * as React from 'react';
import { ISprkChatInputProps } from './types';
/**
 * SprkChatInput - Textarea with send button, Ctrl+Enter shortcut, character counter,
 * and integrated slash command menu (opened by typing '/' or clicking the [/] button).
 *
 * @example
 * ```tsx
 * <SprkChatInput
 *   onSend={(msg) => handleSend(msg)}
 *   disabled={isStreaming}
 *   maxCharCount={2000}
 * />
 * ```
 */
export declare const SprkChatInput: React.FC<ISprkChatInputProps>;
export default SprkChatInput;
//# sourceMappingURL=SprkChatInput.d.ts.map