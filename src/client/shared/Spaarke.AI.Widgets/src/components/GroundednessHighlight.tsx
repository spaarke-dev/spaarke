/**
 * @spaarke/ai-widgets — GroundednessHighlight
 *
 * Wraps a text string and applies visual indicators for ungrounded segments.
 * Ungrounded segments receive a subtle dashed underline and a Fluent v9 warning
 * background token. Grounded segments render as plain text.
 *
 * Design decisions:
 * - Purely additive — the original text content is never mutated.
 * - Colors use Fluent v9 semantic tokens so they automatically adapt to dark
 *   mode and high-contrast themes (ADR-021).
 * - The dashed underline is applied via inline style using `borderBottom`
 *   rather than `textDecoration` so that the color can be controlled
 *   independently of `currentColor` (required for accessibility).
 * - Tooltip: "This claim may not be fully supported by the provided sources"
 *   is shown on hover for each ungrounded span.
 *
 * Usage:
 * ```tsx
 * <GroundednessHighlight
 *   text="The contract was signed on 1 January 2025."
 *   segments={[{ start: 0, end: 20, grounded: false }]}
 * />
 * ```
 *
 * ADR-021: all colors via tokens, makeStyles (Griffel), dark-mode safe.
 * React 19. NOT PCF-safe.
 *
 * Task: AIPU2-090 (FR-402)
 */

import React from 'react';
import { makeStyles, tokens, Tooltip } from '@fluentui/react-components';

// ---------------------------------------------------------------------------
// Domain types
// ---------------------------------------------------------------------------

/**
 * A character-range segment within a message string with a grounded flag.
 *
 * `start` and `end` are byte-offset indices into the flat text string (end
 * is exclusive), matching the Azure AI Groundedness API segment map format
 * emitted by GroundednessCheckService.
 */
export interface GroundednessSegment {
  /** Start character index (inclusive). */
  start: number;
  /** End character index (exclusive). */
  end: number;
  /** Whether the text in this range is grounded by a source document. */
  grounded: boolean;
}

// ---------------------------------------------------------------------------
// Styles
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  /**
   * Wrapper for ungrounded text segments.
   *
   * colorStatusWarningBackground1 is the lightest Fluent v9 warning surface
   * token — visible enough to flag ungrounded content without overwhelming
   * the reading experience. The dashed underline adds a second channel for
   * colorblind users.
   */
  ungroundedSpan: {
    backgroundColor: tokens.colorStatusWarningBackground1,
    // Dashed underline — color matches the warning foreground token.
    borderBottomWidth: '1px',
    borderBottomStyle: 'dashed',
    borderBottomColor: tokens.colorStatusWarningForeground1,
    // Slight rounding so consecutive ungrounded spans merge visually.
    borderRadius: '2px',
    // Tight padding so the highlight does not push adjacent text.
    paddingBottom: '1px',
    cursor: 'help',
  },
});

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

/**
 * Tooltip label shown for every ungrounded span.
 * Kept in a constant to avoid recreating the string per-render.
 */
const UNGROUNDED_TOOLTIP = 'This claim may not be fully supported by the provided sources';

/**
 * Parses `text` against a set of `segments` and returns an array of React
 * nodes: plain strings for grounded content, annotated `<span>` elements
 * for ungrounded ranges.
 *
 * Segments are expected to be non-overlapping. If they overlap or extend
 * beyond the string length, this function clips gracefully.
 *
 * Gaps between segments (unlabelled ranges) are treated as grounded text
 * (rendered as plain strings).
 */
function buildNodes(text: string, segments: GroundednessSegment[], ungroundedClass: string): React.ReactNode[] {
  if (!segments || segments.length === 0) {
    return [text];
  }

  // Sort segments by start position to allow sequential scanning.
  const sorted = [...segments].sort((a, b) => a.start - b.start);

  const nodes: React.ReactNode[] = [];
  let cursor = 0;

  for (let i = 0; i < sorted.length; i++) {
    const seg = sorted[i];
    const segStart = Math.max(seg.start, cursor);
    const segEnd = Math.min(seg.end, text.length);

    if (segStart >= segEnd) {
      // Segment is out of range or already consumed — skip.
      continue;
    }

    // Emit any grounded text before this segment.
    if (segStart > cursor) {
      nodes.push(text.slice(cursor, segStart));
    }

    const segText = text.slice(segStart, segEnd);

    if (seg.grounded) {
      // Grounded segment — plain text, no annotation.
      nodes.push(segText);
    } else {
      // Ungrounded segment — annotated span wrapped in a Tooltip.
      nodes.push(
        <Tooltip key={`ungrounded-${segStart}-${segEnd}`} content={UNGROUNDED_TOOLTIP} relationship="label" withArrow>
          <span
            className={ungroundedClass}
            aria-label={`Ungrounded claim: ${segText}`}
            data-testid="ungrounded-segment"
          >
            {segText}
          </span>
        </Tooltip>
      );
    }

    cursor = segEnd;
  }

  // Emit any trailing text after the last segment.
  if (cursor < text.length) {
    nodes.push(text.slice(cursor));
  }

  // If nothing was emitted (e.g. all segments were invalid), fall back to
  // the raw text so the message is never silently empty.
  if (nodes.length === 0) {
    return [text];
  }

  return nodes;
}

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

export interface GroundednessHighlightProps {
  /**
   * The full flat message text to annotate.
   * The component does NOT modify this string — only visual overlays are added.
   */
  text: string;

  /**
   * Segment map from the safety_annotation event's groundedness payload.
   * Each entry covers a character range and carries a `grounded` flag.
   * When `undefined` or empty, the component renders `text` as plain text
   * with no annotations (graceful degradation).
   */
  segments?: GroundednessSegment[];

  /** Optional CSS class applied to the outermost wrapper element. */
  className?: string;
}

/**
 * GroundednessHighlight renders a message string with visual annotations for
 * ungrounded segments produced by the Azure AI Groundedness Detection API.
 *
 * Ungrounded segments receive:
 *   - colorStatusWarningBackground1 background fill
 *   - dashed bottom border in colorStatusWarningForeground1
 *   - Tooltip: "This claim may not be fully supported by the provided sources"
 *
 * Grounded segments and gaps between segments render as plain text with no
 * visual treatment.
 *
 * If `segments` is undefined or empty the component renders `text` unchanged
 * — no error state is shown. This ensures backward compatibility when
 * annotation data is unavailable.
 *
 * @example
 * // Full annotation with two ungrounded segments:
 * <GroundednessHighlight
 *   text="The contract was signed in 2025. The penalty clause is 5%."
 *   segments={[
 *     { start: 0, end: 31, grounded: true },
 *     { start: 32, end: 58, grounded: false },
 *   ]}
 * />
 *
 * @example
 * // No segments — renders plain text (safe default):
 * <GroundednessHighlight text="Hello world" />
 */
export const GroundednessHighlight: React.FC<GroundednessHighlightProps> = ({ text, segments, className }) => {
  const styles = useStyles();

  const nodes = React.useMemo(
    () => buildNodes(text, segments ?? [], styles.ungroundedSpan),
    [text, segments, styles.ungroundedSpan]
  );

  return (
    <span className={className} data-testid="groundedness-highlight">
      {nodes}
    </span>
  );
};

export default GroundednessHighlight;
