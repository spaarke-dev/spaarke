/**
 * SprkChat - Main reusable chat component
 *
 * Composes all SprkChat sub-components into a complete chat experience:
 * - Context selector (document + playbook)
 * - Predefined prompt suggestions
 * - Message list with streaming support
 * - Input area with Ctrl+Enter
 * - Highlight-and-refine on text selection
 *
 * Integrates with ChatEndpoints SSE API for real-time streaming responses.
 *
 * @see ADR-012 - Shared Component Library
 * @see ADR-021 - Fluent UI v9; makeStyles; design tokens; dark mode
 * @see ADR-022 - React 16 APIs only
 */
import * as React from 'react';
import { ISprkChatProps } from './types';
export declare const SprkChat: React.FC<ISprkChatProps>;
export default SprkChat;
//# sourceMappingURL=SprkChat.d.ts.map