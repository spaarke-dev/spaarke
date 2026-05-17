/**
 * CrossPaneLink
 *
 * An inline interactive element that, when clicked or activated via keyboard,
 * dispatches a cross-pane link CustomEvent on `document`. Source pane widgets
 * subscribe to this event to navigate to and highlight the referenced range.
 *
 * The component intentionally has NO knowledge of the source pane — it only
 * fires the event. This keeps the output pane and source pane fully decoupled.
 *
 * Accessibility:
 *   - Renders as an inline <span> with role="button" and tabIndex={0}.
 *   - Handles onKeyDown for Enter (key code 13) and Space (key code 32).
 *   - Focus ring is applied via Fluent v9 tokens.
 *
 * Styling:
 *   - makeStyles with Fluent v9 design tokens only (ADR-021).
 *   - Uses colorBrandForeground1 for the link colour so it adapts to
 *     FluentProvider theme switching (dark mode supported automatically).
 *
 * @see cross-pane-events.ts — event definition and dispatch helper
 * @see useCrossPane.ts — React hooks wrapping this event
 *
 * NOT PCF-safe — requires React 19 and Fluent UI v9.
 */

import * as React from 'react';
import { makeStyles, mergeClasses, tokens } from '@fluentui/react-components';
import { dispatchCrossPaneLink } from './cross-pane-events';
import type { CrossPaneLinkEvent } from './cross-pane-events';

// ---------------------------------------------------------------------------
// Props
// ---------------------------------------------------------------------------

export interface CrossPaneLinkProps {
  /** Identifier for the citation or reference (passed through to the event). */
  citationId: string;

  /** Identifier of the source widget that should handle the highlight. */
  sourceWidgetId: string;

  /** Start offset (inclusive) of the highlight range in the source. */
  highlightStart: number;

  /** End offset (exclusive) of the highlight range in the source. */
  highlightEnd: number;

  /** The inline content to wrap — typically a citation marker or text span. */
  children: React.ReactNode;

  /** Optional additional class name applied to the root span. */
  className?: string;
}

// ---------------------------------------------------------------------------
// Styles — Fluent v9 tokens only (ADR-021)
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  root: {
    display: 'inline',
    cursor: 'pointer',
    color: tokens.colorBrandForeground1,
    textDecorationLine: 'underline',
    textDecorationStyle: 'dotted',
    // Focus ring via Fluent v9 tokens (adapts to high-contrast / dark mode)
    outlineColor: 'transparent',
    outlineWidth: tokens.strokeWidthThick,
    outlineStyle: 'solid',
    outlineOffset: '2px',
    borderRadius: tokens.borderRadiusSmall,
    // Transition for subtle interactivity feedback
    transitionProperty: 'color, opacity',
    transitionDuration: tokens.durationFaster,
    transitionTimingFunction: tokens.curveEasyEase,
    ':hover': {
      color: tokens.colorBrandForeground2,
      textDecorationStyle: 'solid',
    },
    ':focus-visible': {
      outlineColor: tokens.colorStrokeFocus2,
    },
    ':active': {
      opacity: '0.7',
    },
  },
});

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

/**
 * CrossPaneLink wraps inline content (citations, footnote markers, document
 * references) and makes them interactive. When activated, it dispatches a
 * `spaarke:cross-pane-link` CustomEvent on `document` carrying the citation
 * and highlight metadata. The source pane widget receives this event and
 * scrolls to / highlights the referenced range.
 *
 * @example
 * <CrossPaneLink
 *   citationId="ref-42"
 *   sourceWidgetId="doc-viewer-1"
 *   highlightStart={1024}
 *   highlightEnd={1200}
 * >
 *   [42]
 * </CrossPaneLink>
 */
export function CrossPaneLink({
  citationId,
  sourceWidgetId,
  highlightStart,
  highlightEnd,
  children,
  className,
}: CrossPaneLinkProps): React.ReactElement {
  const styles = useStyles();

  const payload: CrossPaneLinkEvent = {
    citationId,
    sourceWidgetId,
    highlightStart,
    highlightEnd,
  };

  const handleClick = (): void => {
    dispatchCrossPaneLink(payload);
  };

  const handleKeyDown = (e: React.KeyboardEvent<HTMLSpanElement>): void => {
    if (e.key === 'Enter' || e.key === ' ') {
      e.preventDefault();
      dispatchCrossPaneLink(payload);
    }
  };

  return (
    <span
      role="button"
      tabIndex={0}
      className={mergeClasses(styles.root, className)}
      onClick={handleClick}
      onKeyDown={handleKeyDown}
      aria-label={`Navigate to citation ${citationId}`}
    >
      {children}
    </span>
  );
}

export default CrossPaneLink;
