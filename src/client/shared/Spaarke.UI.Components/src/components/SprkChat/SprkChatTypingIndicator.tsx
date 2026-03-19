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
import { makeStyles, tokens } from '@fluentui/react-components';

// ---------------------------------------------------------------------------
// Styles — ADR-021: Fluent v9 tokens for all timing, colors, spacing
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  '@keyframes typingBounce': {
    '0%, 80%, 100%': {
      opacity: 0.3,
      transform: 'scale(0.7)',
    },
    '40%': {
      opacity: 1,
      transform: 'scale(1)',
    },
  },
  container: {
    display: 'flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalXS,
    paddingTop: tokens.spacingVerticalXS,
    paddingBottom: tokens.spacingVerticalXS,
    paddingLeft: tokens.spacingHorizontalS,
    paddingRight: tokens.spacingHorizontalS,
  },
  dot: {
    width: '8px',
    height: '8px',
    borderRadius: tokens.borderRadiusCircular,
    backgroundColor: tokens.colorNeutralForeground3,
    animationName: 'typingBounce',
    // ADR-021: use Fluent v9 motion tokens for animation timing
    animationDuration: tokens.durationUltraSlow,
    animationTimingFunction: tokens.curveEasyEase,
    animationIterationCount: 'infinite',
  },
  dot1: {
    animationDelay: '0ms',
  },
  dot2: {
    // Stagger by ~200ms using Fluent normal duration token
    animationDelay: tokens.durationNormal,
  },
  dot3: {
    // Stagger by ~400ms (2x normal duration)
    animationDelay: tokens.durationSlow,
  },
});

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

/**
 * Animated three-dot typing indicator.
 *
 * @example
 * ```tsx
 * {isTyping && !streamedContent && <SprkChatTypingIndicator />}
 * ```
 */
export const SprkChatTypingIndicator: React.FC = () => {
  const styles = useStyles();

  return (
    <div
      className={styles.container}
      role="status"
      aria-label="AI is thinking"
      data-testid="typing-indicator"
    >
      <span className={`${styles.dot} ${styles.dot1}`} />
      <span className={`${styles.dot} ${styles.dot2}`} />
      <span className={`${styles.dot} ${styles.dot3}`} />
    </div>
  );
};

export default SprkChatTypingIndicator;
