/**
 * @spaarke/ai-widgets — SafetyAnnotationOverlay
 *
 * React component that subscribes to the 'safety' PaneEventBus channel and
 * applies retroactive safety annotations to completed AI message content.
 *
 * Lifecycle:
 *  1. Renders children (the message content) immediately — no blocking.
 *  2. Subscribes to 'safety' channel via usePaneEvent.
 *  3. When a `safety_annotation` event arrives for `turnId`, schedules a
 *     200 ms setTimeout (cleared on unmount).
 *  4. After 200 ms: parses groundedness segments and citation verification
 *     results, then replaces the raw content render with annotated output:
 *       - GroundednessHighlight for ungrounded text segments.
 *       - CitationBadge inline next to each [N] citation marker.
 *
 * Design principles:
 *  - Purely additive: no message text is ever modified; only overlays/badges
 *    are added on top of the original content.
 *  - If the annotation event is missing or malformed, the component silently
 *    falls back to rendering the original content as plain text. No error
 *    state is shown (FR-402 AC: "Missing or failed annotation leaves message
 *    text unchanged").
 *  - The 200 ms delay avoids jarring mid-stream annotation appearance per
 *    D-03: "stream + retroactive annotation".
 *  - All colors use Fluent v9 semantic tokens (ADR-021, dark mode safe).
 *
 * React 19. NOT PCF-safe.
 *
 * Task: AIPU2-090 (FR-402, FR-403)
 */

import React, { useState, useEffect, useRef, useCallback } from 'react';
import { usePaneEvent } from '../events/usePaneEvent';
import type { SafetyPaneEvent } from '../events/PaneEventTypes';
import { GroundednessHighlight } from './GroundednessHighlight';
import type { GroundednessSegment } from './GroundednessHighlight';
import { CitationBadge } from './CitationBadge';
import type { CitationVerificationResult, CitationVerificationStatus } from './CitationBadge';

// ---------------------------------------------------------------------------
// Raw payload types (from safety_annotation SSE event)
// ---------------------------------------------------------------------------

/**
 * Raw groundedness payload emitted by GroundednessCheckService, delivered as
 * `SafetyPaneEvent.groundedness`. Matches the Azure AI Content Safety
 * Groundedness Detection API response shape.
 */
interface RawGroundednessPayload {
  /** Overall groundedness score (0.0–1.0). */
  score?: number;
  /**
   * Array of text segments with grounded/ungrounded flags.
   * `text` is the exact substring; `start`/`end` are character offsets.
   */
  ungrounded_segments?: Array<{
    start: number;
    end: number;
    text?: string;
    grounded?: boolean;
  }>;
}

/**
 * Raw citation verification entry emitted by CitationVerificationService,
 * delivered as a value in `SafetyPaneEvent.citations` (keyed by claim ID).
 */
interface RawCitationEntry {
  id?: string;
  status?: string;
  providerName?: string;
  confidence?: string;
  sourceUrl?: string;
}

// ---------------------------------------------------------------------------
// Parsed annotation state
// ---------------------------------------------------------------------------

/**
 * Parsed and normalised safety annotation ready for the render layer.
 * Stored in component state after the 200 ms delay fires.
 */
interface AnnotationState {
  segments: GroundednessSegment[];
  citationResults: Map<string, CitationVerificationResult>;
}

// ---------------------------------------------------------------------------
// Parse helpers
// ---------------------------------------------------------------------------

/**
 * Parses the raw `groundedness` object from a safety_annotation event into
 * typed GroundednessSegment[].
 *
 * Only segments with explicit `start` and `end` values are included.
 * Segments without a `grounded` field default to `true` (safe default —
 * unlabelled segments are assumed grounded).
 *
 * Failures are swallowed and return an empty array so the component can
 * always render without crashing.
 */
function parseGroundednessSegments(raw: object | undefined): GroundednessSegment[] {
  if (!raw) return [];
  try {
    const payload = raw as RawGroundednessPayload;
    if (!Array.isArray(payload.ungrounded_segments)) return [];
    const segments: GroundednessSegment[] = [];
    for (const seg of payload.ungrounded_segments) {
      if (typeof seg.start !== 'number' || typeof seg.end !== 'number') continue;
      segments.push({
        start: seg.start,
        end: seg.end,
        // If `grounded` is explicitly false → mark ungrounded; otherwise safe.
        grounded: seg.grounded !== false,
      });
    }
    return segments;
  } catch {
    return [];
  }
}

/**
 * Parses the raw `citations` object from a safety_annotation event into a
 * Map<string, CitationVerificationResult>.
 *
 * Entries with missing id, status, or providerName are silently skipped.
 * Invalid status values default to `'unverified'`.
 *
 * Returns an empty Map on any failure so the component can always render.
 */
function parseCitationResults(raw: object | undefined): Map<string, CitationVerificationResult> {
  const map = new Map<string, CitationVerificationResult>();
  if (!raw) return map;
  try {
    const entries = Object.entries(raw as Record<string, unknown>);
    for (const [, value] of entries) {
      const entry = value as RawCitationEntry;
      const id = entry.id;
      const providerName = entry.providerName;
      if (!id || !providerName) continue;

      const rawStatus = (entry.status ?? '').toLowerCase();
      const status: CitationVerificationStatus =
        rawStatus === 'verified' ? 'verified' : rawStatus === 'partial' ? 'partial' : 'unverified';

      const rawConfidence = (entry.confidence ?? '').toLowerCase();
      const confidence: 'high' | 'medium' | 'low' =
        rawConfidence === 'high' ? 'high' : rawConfidence === 'medium' ? 'medium' : 'low';

      map.set(id, {
        id,
        status,
        providerName,
        confidence,
        sourceUrl: entry.sourceUrl,
      });
    }
  } catch {
    // Return empty map — component renders without citation badges.
  }
  return map;
}

// ---------------------------------------------------------------------------
// Citation marker regex (matches [1], [2], [12] etc.)
// ---------------------------------------------------------------------------

/**
 * Matches citation markers of the form [N] in message text.
 * Used by AnnotatedMessageContent to split text at citation boundaries so that
 * CitationBadges can be inserted inline after each [N] marker.
 *
 * This is a module-level constant (not inside the component) so the RegExp
 * object is created once. Callers MUST reset `.lastIndex = 0` before each use
 * because the `g` flag causes state to persist across `.exec()` calls.
 */
const CITATION_MARKER_REGEX = /\[(\d+)\]/g;

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

/** Annotation delay after the last streaming token (milliseconds). */
const ANNOTATION_DELAY_MS = 200;

export interface SafetyAnnotationOverlayProps {
  /**
   * The AI turn identifier that this overlay tracks.
   * Only `safety_annotation` events whose implicit payload matches this turn
   * are applied. In the current SSE contract, all events on the 'safety'
   * channel apply to the most recent turn, so the overlay stores the latest
   * annotation and applies it once the 200 ms timer fires.
   */
  turnId: string;

  /**
   * The completed message text to annotate.
   * The component renders this text directly until the annotation is applied,
   * at which point it renders GroundednessHighlight + CitationBadge overlays.
   */
  messageText: string;

  /** Optional CSS class applied to the outermost wrapper element. */
  className?: string;
}

/**
 * SafetyAnnotationOverlay applies retroactive safety annotations to a
 * completed AI message ~200 ms after the last streaming token.
 *
 * Rendering behaviour:
 * 1. **Before annotation arrives** — renders `messageText` as plain text.
 * 2. **200 ms after safety_annotation event** — replaces with annotated view:
 *    - GroundednessHighlight wraps the text, highlighting ungrounded segments.
 *    - CitationBadge is rendered inline after each [N] citation marker.
 * 3. **If annotation is missing or malformed** — renders plain text, no error.
 *
 * @example
 * // In ConversationPane — subscribe to safety channel and render per-turn:
 * <SafetyAnnotationOverlay
 *   turnId={message.turnId}
 *   messageText={message.content}
 * />
 */
export const SafetyAnnotationOverlay: React.FC<SafetyAnnotationOverlayProps> = ({ turnId, messageText, className }) => {
  // -------------------------------------------------------------------------
  // State
  // -------------------------------------------------------------------------

  /**
   * Null until 200 ms after a safety_annotation event arrives for this turn.
   * Once set, the component switches from plain-text to annotated rendering.
   */
  const [annotation, setAnnotation] = useState<AnnotationState | null>(null);

  // -------------------------------------------------------------------------
  // Refs
  // -------------------------------------------------------------------------

  /**
   * Holds the raw safety event payload received from PaneEventBus while the
   * 200 ms delay timer is running. Stored in a ref (not state) so the timer
   * callback always reads the latest event without causing extra renders.
   */
  const pendingEventRef = useRef<SafetyPaneEvent | null>(null);

  /**
   * The active 200 ms delay timer handle. Cleared on unmount and whenever a
   * newer event supersedes the pending one.
   */
  const timerRef = useRef<ReturnType<typeof setTimeout> | null>(null);

  // Store turnId in a ref so the timer callback can reference it without
  // triggering effect re-runs.
  const turnIdRef = useRef(turnId);
  turnIdRef.current = turnId;

  // -------------------------------------------------------------------------
  // Timer cleanup on unmount
  // -------------------------------------------------------------------------

  useEffect(() => {
    return () => {
      if (timerRef.current !== null) {
        clearTimeout(timerRef.current);
        timerRef.current = null;
      }
    };
  }, []);

  // -------------------------------------------------------------------------
  // Apply annotation after delay
  // -------------------------------------------------------------------------

  /**
   * Schedules the annotation application after ANNOTATION_DELAY_MS.
   * If a timer is already running, it is cancelled first so only the most
   * recent event is applied.
   */
  const scheduleAnnotation = useCallback((event: SafetyPaneEvent): void => {
    // Store the latest event payload for the timer callback.
    pendingEventRef.current = event;

    // Cancel any in-flight timer so we don't double-apply.
    if (timerRef.current !== null) {
      clearTimeout(timerRef.current);
      timerRef.current = null;
    }

    timerRef.current = setTimeout(() => {
      timerRef.current = null;
      const pending = pendingEventRef.current;
      if (!pending) return;

      const segments = parseGroundednessSegments(pending.groundedness);
      const citationResults = parseCitationResults(pending.citations);

      // Only update state if there is meaningful annotation data. If both are
      // empty, leave the plain-text render (missing annotation AC).
      if (segments.length > 0 || citationResults.size > 0) {
        setAnnotation({ segments, citationResults });
      }
    }, ANNOTATION_DELAY_MS);
  }, []);

  // -------------------------------------------------------------------------
  // Subscribe to safety channel
  // -------------------------------------------------------------------------

  usePaneEvent('safety', (event: SafetyPaneEvent) => {
    if (event.type !== 'safety_annotation') return;
    scheduleAnnotation(event);
  });

  // -------------------------------------------------------------------------
  // Render: plain text (before annotation or missing annotation)
  // -------------------------------------------------------------------------

  if (!annotation) {
    return (
      <span className={className} data-testid="safety-annotation-overlay" data-annotated="false" data-turn-id={turnId}>
        {messageText}
      </span>
    );
  }

  // -------------------------------------------------------------------------
  // Render: annotated view
  //
  // Delegates to AnnotatedMessageContent which performs a single unified pass:
  //   - Splits text by [N] citation markers.
  //   - Wraps each text chunk in GroundednessHighlight (segment colouring).
  //   - Renders CitationBadge inline after each [N] marker that has a result.
  //
  // This avoids double-rendering the text and keeps the DOM clean.
  // -------------------------------------------------------------------------

  const { segments, citationResults } = annotation;

  return (
    <span className={className} data-testid="safety-annotation-overlay" data-annotated="true" data-turn-id={turnId}>
      <AnnotatedMessageContent messageText={messageText} segments={segments} citationResults={citationResults} />
    </span>
  );
};

/**
 * AnnotatedMessageContent renders the complete annotated message:
 * groundedness highlights + inline citation badges in a single pass.
 *
 * This is the primary render path used when annotation state is present.
 * Exported separately so it can be tested and composed without the full
 * SafetyAnnotationOverlay (which requires PaneEventBusProvider).
 *
 * @internal Use SafetyAnnotationOverlay in application code.
 */
export interface AnnotatedMessageContentProps {
  messageText: string;
  segments: GroundednessSegment[];
  citationResults: Map<string, CitationVerificationResult>;
  className?: string;
}

/**
 * AnnotatedMessageContent renders groundedness highlights and citation badges
 * for a completed message in a single unified pass.
 *
 * The text is split by [N] citation markers. Between markers, text is wrapped
 * in GroundednessHighlight. After each marker, if a CitationVerificationResult
 * exists for that citation id, a CitationBadge is rendered inline.
 *
 * This component is stateless and has no PaneEventBus dependency — it only
 * transforms text + annotation data into React nodes. Preferred over
 * SafetyAnnotationOverlay when the annotation state is already available.
 *
 * @example
 * <AnnotatedMessageContent
 *   messageText="See [1] for the regulation text."
 *   segments={[{ start: 0, end: 4, grounded: true }, { start: 4, end: 30, grounded: false }]}
 *   citationResults={new Map([["1", { id: "1", status: "verified", ... }]])}
 * />
 */
export const AnnotatedMessageContent: React.FC<AnnotatedMessageContentProps> = ({
  messageText,
  segments,
  citationResults,
  className,
}) => {
  /**
   * Split the message text by [N] markers, building alternating text + badge
   * nodes. Each text chunk is wrapped in a GroundednessHighlight with the
   * relevant sub-segment slice.
   *
   * Segment offsets are relative to the full messageText string, so we pass
   * them unchanged and let GroundednessHighlight clip out-of-range segments
   * for each chunk. This preserves offset correctness without re-computing.
   */
  const nodes = React.useMemo((): React.ReactNode[] => {
    if (citationResults.size === 0) {
      // No citation badges — render GroundednessHighlight over the full text.
      return [<GroundednessHighlight key="full" text={messageText} segments={segments} />];
    }

    const result: React.ReactNode[] = [];
    let lastIndex = 0;
    let chunkIndex = 0;
    CITATION_MARKER_REGEX.lastIndex = 0;

    let match: RegExpExecArray | null;
    while ((match = CITATION_MARKER_REGEX.exec(messageText)) !== null) {
      const citationId = match[1];

      // Text chunk before the [N] marker — wrap in GroundednessHighlight.
      if (match.index > lastIndex) {
        const chunk = messageText.slice(lastIndex, match.index);
        result.push(
          <GroundednessHighlight
            key={`chunk-${chunkIndex++}`}
            text={chunk}
            segments={segments.map(s => ({
              // Remap segment offsets to be relative to this chunk.
              start: s.start - lastIndex,
              end: s.end - lastIndex,
              grounded: s.grounded,
            }))}
          />
        );
      }

      // The [N] marker itself as plain text.
      result.push(<span key={`marker-${citationId}-${match.index}`}>{match[0]}</span>);

      // Citation badge (if a result exists for this citation).
      const citationResult = citationResults.get(citationId);
      if (citationResult) {
        result.push(<CitationBadge key={`badge-${citationId}-${match.index}`} result={citationResult} />);
      }

      lastIndex = match.index + match[0].length;
    }

    // Trailing text after the last [N] marker.
    if (lastIndex < messageText.length) {
      const trailing = messageText.slice(lastIndex);
      result.push(
        <GroundednessHighlight
          key={`chunk-${chunkIndex++}`}
          text={trailing}
          segments={segments.map(s => ({
            start: s.start - lastIndex,
            end: s.end - lastIndex,
            grounded: s.grounded,
          }))}
        />
      );
    }

    return result;
  }, [messageText, segments, citationResults]);

  return (
    <span className={className} data-testid="annotated-message-content">
      {nodes}
    </span>
  );
};

export default SafetyAnnotationOverlay;
