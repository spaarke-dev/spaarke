/**
 * SprkChatSuggestions - Follow-up suggestion chips for SprkChat
 *
 * Renders up to 3 clickable follow-up suggestion chips below the latest
 * assistant message as ONE chip family with two learnable variants, driven by
 * the structural `kind` (NOT by phrasing — keyword heuristics on the label are
 * banned by ASSISTANT-UI-ELEMENT-CRITERIA):
 *
 *   - CAPABILITY / ACTION — "does something" (dispatches a Binding, or opens the
 *     upload/search/select shortcut). Rendered bordered + a trailing arrow (→)
 *     so the user learns "arrow = acts".
 *   - QUESTION — "asks the assistant something", answered right here by
 *     re-entering the grounded loop. Rendered as a lighter pill with NO arrow so
 *     the user learns "no arrow = asks".
 *
 * The label grammar (authored server-side by the SUGGEST-FOLLOWUPS Action) agrees
 * with the affordance: imperative for capability/action, interrogative for
 * question — so the look and the words always match.
 *
 * A bare/untyped item never reaches this component (the SSE parser drops anything
 * without a valid `kind`), so a dead-end promise is structurally impossible.
 *
 * Chips use Fluent UI v9 InteractionTag for a pill/chip appearance with keyboard
 * navigation (Arrow Left/Right between chips, Enter/Space to select).
 *
 * Animation: fade-in + slide-up (200ms CSS transition) controlled by `visible`.
 *
 * @see spaarkeai-assistant-enhancements-r4 task 021b — grounded, capability-backed
 *      suggestions (design delta §5 / §5a / §9a)
 * @see docs/standards/ASSISTANT-UI-ELEMENT-CRITERIA.md — action-chip vs question-chip
 * @see ADR-012 - Shared Component Library (context-agnostic)
 * @see ADR-021 - Fluent UI v9; makeStyles; design tokens; dark mode
 * @see ADR-022 - React 16 APIs only
 */

import * as React from 'react';
import { makeStyles, shorthands, tokens, InteractionTag, InteractionTagPrimary } from '@fluentui/react-components';
import { ISprkChatSuggestionsProps, ISprkChatFollowup } from './types';

// ─────────────────────────────────────────────────────────────────────────────
// Constants
// ─────────────────────────────────────────────────────────────────────────────

/** Maximum number of suggestion chips displayed at once. */
const MAX_SUGGESTIONS = 3;

/** Maximum character length before text is truncated with ellipsis. */
const MAX_TEXT_LENGTH = 50;

// ─────────────────────────────────────────────────────────────────────────────
// Styles
// ─────────────────────────────────────────────────────────────────────────────

const useStyles = makeStyles({
  root: {
    display: 'flex',
    flexDirection: 'row',
    flexWrap: 'wrap',
    ...shorthands.gap(tokens.spacingHorizontalS),
    ...shorthands.padding(tokens.spacingVerticalXS, '0px'),
    alignItems: 'center',
    transitionProperty: 'opacity, transform',
    transitionDuration: '200ms',
    transitionTimingFunction: 'ease-out',
  },
  visible: {
    opacity: 1,
    transform: 'translateY(0)',
  },
  hidden: {
    opacity: 0,
    transform: 'translateY(8px)',
    pointerEvents: 'none',
  },
  chip: {
    cursor: 'pointer',
    maxWidth: '280px',
  },
  chipLabel: {
    display: 'inline-flex',
    alignItems: 'center',
    ...shorthands.gap(tokens.spacingHorizontalXXS),
    minWidth: 0,
  },
  chipText: {
    overflow: 'hidden',
    textOverflow: 'ellipsis',
    whiteSpace: 'nowrap',
    display: 'block',
  },
  /**
   * The trailing "acts" arrow on capability / action chips. Colour is inherited
   * from the InteractionTag (token-driven) — no hardcoded colour (ADR-021).
   */
  actsArrow: {
    flexShrink: 0,
    fontSize: tokens.fontSizeBase200,
    lineHeight: tokens.lineHeightBase200,
    opacity: 0.9,
  },
});

// ─────────────────────────────────────────────────────────────────────────────
// Helpers
// ─────────────────────────────────────────────────────────────────────────────

/**
 * Truncate text to `maxLen` characters, adding ellipsis if needed.
 */
function truncateText(text: string, maxLen: number): string {
  if (text.length <= maxLen) {
    return text;
  }
  return text.slice(0, maxLen - 1).trimEnd() + '…';
}

/**
 * A capability or action chip "does something" → it gets the bordered + arrow
 * affordance. A question chip "asks" → lighter pill, no arrow. The distinction is
 * STRUCTURAL (the item's `kind`), never a guess from the label text.
 */
function isActsChip(item: ISprkChatFollowup): boolean {
  return item.kind === 'capability' || item.kind === 'action';
}

// ─────────────────────────────────────────────────────────────────────────────
// Component
// ─────────────────────────────────────────────────────────────────────────────

/**
 * SprkChatSuggestions - Renders clickable follow-up suggestion chips.
 *
 * @example
 * ```tsx
 * <SprkChatSuggestions
 *   suggestions={[
 *     { kind: 'capability', label: 'Prioritize my tasks', targetBindingId: '<id>', actionId: null },
 *     { kind: 'question', label: 'What are the risks in section 3?', targetBindingId: null, actionId: null },
 *   ]}
 *   onSelect={(item) => handleSuggestionSelect(item)}
 *   visible={!isStreaming}
 * />
 * ```
 */
export const SprkChatSuggestions: React.FC<ISprkChatSuggestionsProps> = ({ suggestions, onSelect, visible }) => {
  const styles = useStyles();
  const containerRef = React.useRef<HTMLDivElement>(null);

  // Limit to MAX_SUGGESTIONS chips. Server authors the order (actions, then
  // capabilities, then questions) — render in received order.
  const displaySuggestions = React.useMemo(() => suggestions.slice(0, MAX_SUGGESTIONS), [suggestions]);

  // Keyboard navigation: Arrow Left/Right between chips, Enter/Space to select
  const handleKeyDown = React.useCallback((event: React.KeyboardEvent<HTMLDivElement>) => {
    const container = containerRef.current;
    if (!container) {
      return;
    }

    const focusable = Array.from(container.querySelectorAll<HTMLElement>('[data-suggestion-chip]'));
    const currentIndex = focusable.indexOf(event.target as HTMLElement);

    if (currentIndex === -1) {
      return;
    }

    let nextIndex = -1;

    if (event.key === 'ArrowRight') {
      event.preventDefault();
      nextIndex = (currentIndex + 1) % focusable.length;
    } else if (event.key === 'ArrowLeft') {
      event.preventDefault();
      nextIndex = (currentIndex - 1 + focusable.length) % focusable.length;
    }

    if (nextIndex >= 0) {
      focusable[nextIndex].focus();
    }
  }, []);

  if (displaySuggestions.length === 0) {
    return null;
  }

  const rootClassName = `${styles.root} ${visible ? styles.visible : styles.hidden}`;

  return (
    <div
      ref={containerRef}
      className={rootClassName}
      role="group"
      aria-label="Follow-up suggestions"
      onKeyDown={handleKeyDown}
      data-testid="sprkchat-suggestions"
    >
      {displaySuggestions.map((item, index) => {
        // The label is authored server-side per kind (imperative for
        // capability/action, interrogative for question) — no client-side
        // stripping/parsing (the legacy "[action:*]" prefix is retired; the
        // routing datum is the structural `actionId`/`targetBindingId`).
        const label = item.label;
        const displayText = truncateText(label, MAX_TEXT_LENGTH);
        const isTruncated = label.length > MAX_TEXT_LENGTH;
        const acts = isActsChip(item);

        return (
          <InteractionTag
            key={`suggestion-${index}`}
            className={styles.chip}
            // Capability/action chips get the solid "brand" fill + arrow so they
            // read as "does something"; question chips get the lighter "outline"
            // pill so they read as "asks" (design delta §5a). Both are tokens-only.
            appearance={acts ? 'brand' : 'outline'}
            shape="circular"
            size="small"
          >
            <InteractionTagPrimary
              role="button"
              aria-label={isTruncated ? label : undefined}
              title={isTruncated ? label : undefined}
              onClick={() => onSelect(item)}
              data-suggestion-chip=""
              data-suggestion-kind={item.kind}
              data-testid={`suggestion-chip-${index}`}
            >
              <span className={styles.chipLabel}>
                <span className={styles.chipText}>{displayText}</span>
                {acts && (
                  <span className={styles.actsArrow} aria-hidden="true">
                    {'→'}
                  </span>
                )}
              </span>
            </InteractionTagPrimary>
          </InteractionTag>
        );
      })}
    </div>
  );
};

export default SprkChatSuggestions;
