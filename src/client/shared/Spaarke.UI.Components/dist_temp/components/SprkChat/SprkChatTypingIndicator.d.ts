/**
 * SprkChatTypingIndicator - Animated three-dot typing indicator
 *
 * Shows an animated typing indicator (three dots with staggered fade/scale animation)
 * while the AI is processing a request and no content has arrived yet.
 *
 * Displayed between the `typing_start` SSE event and the first `token` event.
 * Hidden once content begins streaming.
 *
 * @see ADR-021 - Fluent v9 design tokens for animation timing; no hard-coded values
 * @see ADR-022 - React 16 APIs only (no hooks beyond useState/useEffect/useRef/useCallback/useMemo)
 */
import * as React from 'react';
/**
 * Animated three-dot typing indicator.
 *
 * @example
 * ```tsx
 * {isTyping && !streamedContent && <SprkChatTypingIndicator />}
 * ```
 */
export declare const SprkChatTypingIndicator: React.FC;
export default SprkChatTypingIndicator;
//# sourceMappingURL=SprkChatTypingIndicator.d.ts.map