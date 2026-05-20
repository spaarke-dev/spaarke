/**
 * @spaarke/ai-widgets — FeedbackButtons
 *
 * Non-intrusive thumbs up / thumbs down feedback control rendered beneath each
 * completed AI message in ConversationPane. Icons are muted until hovered and
 * only appear after the SSE 'done' event — never during streaming.
 *
 * Interaction flow:
 *   Thumbs-up  → POST /api/ai/feedback { rating: 'positive' } immediately → checkmark 200ms → neutral
 *   Thumbs-down → reveal optional Textarea → Submit → POST /api/ai/feedback { rating: 'negative', comment? } → checkmark 200ms → neutral
 *
 * State machine:
 *   idle → submitting → submitted (→ idle after checkmark timeout)
 *   idle → expanded   (thumbs-down reveals textarea)
 *   expanded → submitting → submitted → idle
 *
 * Accessibility:
 *   - ARIA labels on both icon buttons (no visible text — icons only)
 *   - Textarea receives focus automatically when revealed
 *   - Keyboard: Tab between thumbs-up / thumbs-down; Enter/Space activates
 *   - Submit button appears only when textarea is visible
 *   - Role="status" on confirmation message for screen readers
 *
 * Design constraints (ADR-021):
 *   - All colours via Fluent v9 tokens — zero hard-coded hex values
 *   - Dark-mode compatible by construction
 *   - makeStyles for all styling
 *   - Icons are 16px; vertical footprint is minimal
 *
 * React 19, NOT PCF-safe.
 *
 * Task: AIPU2-092
 */

import React, { useCallback, useEffect, useRef, useState } from 'react';
import {
  Button,
  makeStyles,
  mergeClasses,
  Textarea,
  tokens,
  Tooltip,
} from '@fluentui/react-components';
import {
  CheckmarkRegular,
  ThumbDislikeRegular,
  ThumbLikeRegular,
} from '@fluentui/react-icons';
import { buildBffApiUrl } from '@spaarke/auth';
import { useAiSession } from '../providers/useAiSession';

// ---------------------------------------------------------------------------
// Types
// ---------------------------------------------------------------------------

/** Feedback rating values sent to the BFF. */
export type FeedbackRating = 'positive' | 'negative';

/** Internal state machine for the component. */
type FeedbackState = 'idle' | 'expanded' | 'submitting' | 'submitted';

/** Props for FeedbackButtons. */
export interface FeedbackButtonsProps {
  /**
   * The chat session ID — required by the BFF feedback endpoint.
   * Matches AiSessionContextValue.chatSessionId.
   */
  sessionId: string;
  /**
   * Zero-based index of the conversation turn this feedback applies to.
   * The BFF uses this to correlate ratings with specific model outputs.
   */
  turnIndex: number;
  /** Optional playbook ID — forwarded to the BFF for analytics segmentation. */
  playbookId?: string;
  /** Optional capability ID — forwarded to the BFF for analytics segmentation. */
  capabilityId?: string;
  /**
   * Optional callback invoked after a successful feedback submission.
   * Useful for parent components that want to track submission events.
   */
  onSubmit?: (rating: FeedbackRating, comment?: string) => void;
  /** Additional CSS class applied to the root element. */
  className?: string;
}

// ---------------------------------------------------------------------------
// Constants
// ---------------------------------------------------------------------------

/** Duration in ms to show the checkmark confirmation before returning to neutral. */
const CHECKMARK_DURATION_MS = 2000;

/** Maximum character length for the optional comment textarea. */
const MAX_COMMENT_LENGTH = 500;

// ---------------------------------------------------------------------------
// Styles
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  root: {
    display: 'inline-flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalXS,
    // Align with the left edge of the message content
    alignSelf: 'flex-start',
  },

  buttonRow: {
    display: 'inline-flex',
    flexDirection: 'row',
    alignItems: 'center',
    gap: tokens.spacingHorizontalXS,
  },

  // Icon button base — icon-only, no visible border, minimal size
  iconButton: {
    // Override Fluent Button defaults for a compact, icon-only appearance
    minWidth: 'unset',
    height: '24px',
    width: '24px',
    padding: '4px',
    // Muted until hovered — icons are subtle by default
    color: tokens.colorNeutralForeground4,
    ':hover': {
      color: tokens.colorNeutralForeground2,
      backgroundColor: tokens.colorNeutralBackground1Hover,
    },
    ':focus-visible': {
      outlineStyle: 'solid',
      outlineWidth: tokens.strokeWidthThick,
      outlineColor: tokens.colorBrandStroke1,
      outlineOffset: '1px',
    },
    // Icon size target — 16px icons inside 24px button
    '& svg': {
      fontSize: '16px',
      width: '16px',
      height: '16px',
    },
  },

  // Active (selected) state — brand color for the chosen rating
  iconButtonActive: {
    color: tokens.colorBrandForeground1,
    ':hover': {
      color: tokens.colorBrandForeground1,
      backgroundColor: tokens.colorBrandBackground2,
    },
  },

  // Dimmed state — the unchosen rating after selection
  iconButtonDimmed: {
    color: tokens.colorNeutralForeground4,
    opacity: '0.4',
    ':hover': {
      color: tokens.colorNeutralForeground3,
      opacity: '1',
    },
  },

  // Checkmark confirmation icon
  checkmarkIcon: {
    fontSize: '16px',
    width: '16px',
    height: '16px',
    color: tokens.colorPaletteGreenForeground1,
    flexShrink: 0,
    // Brief pop-in animation
    animationName: {
      from: { transform: 'scale(0.6)', opacity: '0' },
      to: { transform: 'scale(1)', opacity: '1' },
    },
    animationDuration: '120ms',
    animationTimingFunction: 'ease-out',
    animationFillMode: 'both',
  },

  // Textarea expansion area
  expandedArea: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalXS,
    // Constrain width so the textarea does not span the whole message width
    maxWidth: '320px',
  },

  textarea: {
    resize: 'vertical',
    fontSize: tokens.fontSizeBase200,
    lineHeight: tokens.lineHeightBase200,
    minHeight: '64px',
  },

  characterCount: {
    fontSize: tokens.fontSizeBase100,
    color: tokens.colorNeutralForeground3,
    textAlign: 'right',
  },

  characterCountWarning: {
    color: tokens.colorPaletteRedForeground1,
  },

  actionRow: {
    display: 'flex',
    flexDirection: 'row',
    gap: tokens.spacingHorizontalXS,
    justifyContent: 'flex-end',
  },

  errorText: {
    fontSize: tokens.fontSizeBase100,
    color: tokens.colorPaletteRedForeground1,
    lineHeight: tokens.lineHeightBase200,
  },
});

// ---------------------------------------------------------------------------
// FeedbackButtons
// ---------------------------------------------------------------------------

/**
 * FeedbackButtons — thumbs up / thumbs down rating control for AI responses.
 *
 * Place this component beneath each completed AI message (not during streaming).
 * The parent is responsible for gating render on `streamingState.isStreaming === false`.
 *
 * @example
 * // Inside ConversationPane, after each completed message:
 * {!isStreaming && (
 *   <FeedbackButtons
 *     sessionId={chatSessionId}
 *     turnIndex={turnIndex}
 *     playbookId={playbookId}
 *   />
 * )}
 *
 * Spaarke Auth v2 §H-4: this component reads `bffBaseUrl` and
 * `authenticatedFetch` from `useAiSession()` internally — no token / no URL
 * crosses a component boundary as props. Must be rendered inside an
 * AiSessionProvider tree.
 */
export const FeedbackButtons: React.FC<FeedbackButtonsProps> = ({
  sessionId,
  turnIndex,
  playbookId,
  capabilityId,
  onSubmit,
  className,
}) => {
  const styles = useStyles();
  const { bffBaseUrl, authenticatedFetch } = useAiSession();

  // ── State machine ────────────────────────────────────────────────────────
  const [feedbackState, setFeedbackState] = useState<FeedbackState>('idle');
  const [selectedRating, setSelectedRating] = useState<FeedbackRating | null>(null);
  const [comment, setComment] = useState<string>('');
  const [errorMessage, setErrorMessage] = useState<string | null>(null);

  // Prevent double-submit: track in-flight request
  const isSubmittingRef = useRef<boolean>(false);

  // For auto-focus on textarea when expanded
  const textareaRef = useRef<HTMLTextAreaElement>(null);

  // For clearing the checkmark timeout on unmount
  const checkmarkTimerRef = useRef<ReturnType<typeof setTimeout> | null>(null);

  // Clean up timer on unmount to avoid setState on unmounted component
  useEffect(() => {
    return () => {
      if (checkmarkTimerRef.current !== null) {
        clearTimeout(checkmarkTimerRef.current);
      }
    };
  }, []);

  // Auto-focus textarea when the expanded area appears
  useEffect(() => {
    if (feedbackState === 'expanded' && textareaRef.current) {
      textareaRef.current.focus();
    }
  }, [feedbackState]);

  // ── API submission ───────────────────────────────────────────────────────

  const submitFeedback = useCallback(
    async (rating: FeedbackRating, commentText?: string): Promise<void> => {
      if (isSubmittingRef.current) return;
      isSubmittingRef.current = true;

      setFeedbackState('submitting');
      setErrorMessage(null);

      try {
        const url = buildBffApiUrl(bffBaseUrl, '/api/ai/feedback');
        const body: Record<string, unknown> = {
          sessionId,
          turnIndex,
          rating,
        };
        if (commentText && commentText.trim().length > 0) {
          body.comment = commentText.trim();
        }
        if (playbookId) body.playbookId = playbookId;
        if (capabilityId) body.capabilityId = capabilityId;

        // authenticatedFetch attaches Bearer header internally; no token
        // ever crosses a component boundary (Spaarke Auth v2 §H-4).
        const response = await authenticatedFetch(url, {
          method: 'POST',
          headers: {
            'Content-Type': 'application/json',
          },
          body: JSON.stringify(body),
        });

        if (!response.ok) {
          throw new Error(`Feedback API returned ${response.status}`);
        }

        // Success path — show checkmark then return to neutral
        setFeedbackState('submitted');
        setComment('');
        onSubmit?.(rating, commentText?.trim());

        checkmarkTimerRef.current = setTimeout(() => {
          setFeedbackState('idle');
          setSelectedRating(null);
          setErrorMessage(null);
          isSubmittingRef.current = false;
        }, CHECKMARK_DURATION_MS);
      } catch (err) {
        // Error path — return to pre-submission state, show inline error
        console.warn('[FeedbackButtons] Feedback submission failed:', err);
        setErrorMessage('Feedback could not be submitted. Please try again.');
        // Return to the appropriate pre-submission state
        setFeedbackState(selectedRating === 'negative' ? 'expanded' : 'idle');
        isSubmittingRef.current = false;
      }
    },
    // authenticatedFetch is a stable module-level reference in @spaarke/auth;
    // omitting it from deps avoids re-creating the callback every render.
    // eslint-disable-next-line react-hooks/exhaustive-deps
    [bffBaseUrl, capabilityId, onSubmit, playbookId, selectedRating, sessionId, turnIndex]
  );

  // ── Interaction handlers ─────────────────────────────────────────────────

  const handleThumbsUp = useCallback((): void => {
    if (feedbackState === 'submitting' || feedbackState === 'submitted') return;
    setSelectedRating('positive');
    setFeedbackState('idle'); // collapse textarea if it was open
    setComment('');
    setErrorMessage(null);
    void submitFeedback('positive');
  }, [feedbackState, submitFeedback]);

  const handleThumbsDown = useCallback((): void => {
    if (feedbackState === 'submitting' || feedbackState === 'submitted') return;
    setSelectedRating('negative');
    // Toggle: if already expanded, collapse; otherwise expand
    setFeedbackState((prev) => (prev === 'expanded' ? 'idle' : 'expanded'));
    setErrorMessage(null);
  }, [feedbackState]);

  const handleCommentChange = useCallback(
    (e: React.ChangeEvent<HTMLTextAreaElement>): void => {
      const value = e.target.value;
      if (value.length <= MAX_COMMENT_LENGTH) {
        setComment(value);
      }
    },
    []
  );

  const handleSubmitComment = useCallback((): void => {
    if (feedbackState === 'submitting') return;
    void submitFeedback('negative', comment);
  }, [comment, feedbackState, submitFeedback]);

  const handleCancel = useCallback((): void => {
    setFeedbackState('idle');
    setSelectedRating(null);
    setComment('');
    setErrorMessage(null);
  }, []);

  const handleCommentKeyDown = useCallback(
    (e: React.KeyboardEvent<HTMLTextAreaElement>): void => {
      // Ctrl+Enter / Cmd+Enter submits the comment
      if ((e.ctrlKey || e.metaKey) && e.key === 'Enter') {
        e.preventDefault();
        handleSubmitComment();
      }
      // Escape cancels and collapses
      if (e.key === 'Escape') {
        handleCancel();
      }
    },
    [handleCancel, handleSubmitComment]
  );

  // ── Derived state for rendering ──────────────────────────────────────────

  const isDisabled = feedbackState === 'submitting' || feedbackState === 'submitted';
  const isExpanded = feedbackState === 'expanded';
  const isSubmitted = feedbackState === 'submitted';
  const isSubmitting = feedbackState === 'submitting';

  const thumbsUpActive = selectedRating === 'positive' && !isSubmitted;
  const thumbsDownActive = selectedRating === 'negative' && !isSubmitted;
  const thumbsUpDimmed = selectedRating !== null && selectedRating !== 'positive' && !isSubmitted;
  const thumbsDownDimmed = selectedRating !== null && selectedRating !== 'negative' && !isSubmitted;

  const charsRemaining = MAX_COMMENT_LENGTH - comment.length;
  const isNearLimit = charsRemaining <= 50;

  // ── Render ───────────────────────────────────────────────────────────────

  return (
    <div className={mergeClasses(styles.root, className)}>
      {/* Icon button row */}
      <div className={styles.buttonRow}>
        {/* Thumbs up */}
        <Tooltip content="Rate this response as helpful" relationship="label" positioning="above">
          <button
            type="button"
            className={mergeClasses(
              styles.iconButton,
              thumbsUpActive && styles.iconButtonActive,
              thumbsUpDimmed && styles.iconButtonDimmed
            )}
            onClick={handleThumbsUp}
            disabled={isDisabled}
            aria-label="Rate this response as helpful"
            aria-pressed={thumbsUpActive}
          >
            <ThumbLikeRegular />
          </button>
        </Tooltip>

        {/* Thumbs down */}
        <Tooltip content="Rate this response as not helpful" relationship="label" positioning="above">
          <button
            type="button"
            className={mergeClasses(
              styles.iconButton,
              thumbsDownActive && styles.iconButtonActive,
              thumbsDownDimmed && styles.iconButtonDimmed
            )}
            onClick={handleThumbsDown}
            disabled={isDisabled}
            aria-label="Rate this response as not helpful"
            aria-pressed={thumbsDownActive}
            aria-expanded={isExpanded}
          >
            <ThumbDislikeRegular />
          </button>
        </Tooltip>

        {/* Checkmark confirmation — visible for CHECKMARK_DURATION_MS after submission */}
        {isSubmitted && (
          <span
            className={styles.checkmarkIcon}
            role="status"
            aria-label="Feedback submitted"
            aria-live="polite"
          >
            <CheckmarkRegular />
          </span>
        )}
      </div>

      {/* Optional comment area — only shown after thumbs-down */}
      {isExpanded && (
        <div className={styles.expandedArea}>
          <Textarea
            ref={textareaRef}
            className={styles.textarea}
            placeholder="What could be improved? (optional)"
            value={comment}
            onChange={handleCommentChange}
            onKeyDown={handleCommentKeyDown}
            disabled={isSubmitting}
            aria-label="Additional feedback (optional)"
            aria-describedby="feedback-char-count"
          />

          {/* Character count */}
          <span
            id="feedback-char-count"
            className={mergeClasses(
              styles.characterCount,
              isNearLimit && styles.characterCountWarning
            )}
            aria-live="polite"
          >
            {charsRemaining} characters remaining
          </span>

          {/* Inline error message */}
          {errorMessage && (
            <span className={styles.errorText} role="alert">
              {errorMessage}
            </span>
          )}

          {/* Action buttons */}
          <div className={styles.actionRow}>
            <Button
              appearance="subtle"
              size="small"
              onClick={handleCancel}
              disabled={isSubmitting}
              aria-label="Cancel feedback"
            >
              Cancel
            </Button>
            <Button
              appearance="primary"
              size="small"
              onClick={handleSubmitComment}
              disabled={isSubmitting}
              aria-label="Submit feedback"
            >
              {isSubmitting ? 'Submitting…' : 'Submit'}
            </Button>
          </div>
        </div>
      )}

      {/* Inline error when thumbs-up fails (no textarea visible) */}
      {!isExpanded && errorMessage && (
        <span className={styles.errorText} role="alert">
          {errorMessage}
        </span>
      )}
    </div>
  );
};

export default FeedbackButtons;
