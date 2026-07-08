/**
 * LinearRunProgressList
 *
 * Presenter for {@link LinearRunEvent}s emitted by {@link useLinearRunProgress}.
 * Renders the server-sent progress messages as an honest, scrolling list —
 * NO client-side step interpretation, NO hardcoded numbered visual, NO
 * assumption about which steps exist.
 *
 * This is the shared substrate that lets multiple client surfaces (Document
 * Upload wizard, Summarize Files wizard, SprkChat Context pane, future
 * consumers) render Linear AI Consumer streams the same way.
 *
 * Per ADR-021, all colors, spacing, and typography come from Fluent v9
 * semantic tokens so the component works in both light and dark themes.
 *
 * @see useLinearRunProgress.ts
 */
import * as React from 'react';
import { Spinner, Text, makeStyles, tokens } from '@fluentui/react-components';
import type { LinearRunEvent } from '../../hooks/useLinearRunProgress';

// ─────────────────────────────────────────────────────────────────────────────
// Props
// ─────────────────────────────────────────────────────────────────────────────

export interface ILinearRunProgressListProps {
  /** Ordered append-only event history from {@link useLinearRunProgress}. */
  events: LinearRunEvent[];
  /**
   * Layout:
   * - `scrolling-text` (default): full history as a scrolling list, each row
   *   showing the server's `content` string with the wall-clock timestamp
   *   on the right. Matches the Document Upload wizard pattern.
   * - `compact`: collapses to a single line showing the most recent progress
   *   message, useful for embedding in small chrome (badges, headers).
   */
  mode?: 'scrolling-text' | 'compact';
  /** Optional CSS class merged with the root container. */
  className?: string;
  /**
   * When true, shows a small spinner alongside the latest row while the run
   * is still active. Callers pass `status === 'running'` from the hook.
   */
  isStreaming?: boolean;
  /**
   * Custom empty-state text shown when no progress events have arrived yet.
   * Defaults to "Waiting for progress updates…".
   */
  emptyText?: string;
}

// ─────────────────────────────────────────────────────────────────────────────
// Styles
// ─────────────────────────────────────────────────────────────────────────────

const useStyles = makeStyles({
  scrollingRoot: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalXS,
    padding: tokens.spacingVerticalM,
    backgroundColor: tokens.colorNeutralBackground2,
    border: `1px solid ${tokens.colorNeutralStroke2}`,
    borderRadius: tokens.borderRadiusMedium,
    maxHeight: '320px',
    overflowY: 'auto',
    // Ensure predictable width in constrained wizard steps.
    minWidth: 0,
  },
  compactRoot: {
    display: 'flex',
    flexDirection: 'row',
    alignItems: 'center',
    gap: tokens.spacingHorizontalS,
    padding: `${tokens.spacingVerticalXS} ${tokens.spacingHorizontalM}`,
    color: tokens.colorNeutralForeground2,
  },
  row: {
    display: 'flex',
    flexDirection: 'row',
    alignItems: 'baseline',
    gap: tokens.spacingHorizontalM,
    paddingTop: tokens.spacingVerticalXXS,
    paddingBottom: tokens.spacingVerticalXXS,
  },
  content: {
    flex: 1,
    minWidth: 0,
    color: tokens.colorNeutralForeground1,
    wordBreak: 'break-word',
  },
  contentMuted: {
    flex: 1,
    minWidth: 0,
    color: tokens.colorNeutralForeground3,
    wordBreak: 'break-word',
  },
  timestamp: {
    flexShrink: 0,
    color: tokens.colorNeutralForeground3,
    fontVariantNumeric: 'tabular-nums',
  },
  streamingIndicator: {
    display: 'flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalS,
    paddingTop: tokens.spacingVerticalS,
    color: tokens.colorNeutralForeground2,
  },
  empty: {
    color: tokens.colorNeutralForeground3,
    fontStyle: 'italic',
  },
});

// ─────────────────────────────────────────────────────────────────────────────
// Helpers
// ─────────────────────────────────────────────────────────────────────────────

/**
 * Formats a wall-clock time as HH:MM:SS. Uses the browser locale so digits
 * follow the user's preference (e.g. 24h vs 12h).
 */
function formatTimestamp(date: Date): string {
  try {
    return date.toLocaleTimeString(undefined, {
      hour: '2-digit',
      minute: '2-digit',
      second: '2-digit',
    });
  } catch {
    return date.toISOString().substring(11, 19);
  }
}

/**
 * Filters `events` down to the visible-in-list subset:
 * - `progress` chunks are always shown (the primary signal).
 * - `error` chunks are shown so a failed run is visible to the user.
 * - `metadata`, `chunk`, `result`, `done` chunks are hidden by default —
 *   they carry structured / token / lifecycle data that's not user-facing
 *   in a scrolling progress list. Callers that need those events consume
 *   the hook directly (e.g. reading `state.result`).
 */
function selectDisplayEvents(events: LinearRunEvent[]): LinearRunEvent[] {
  return events.filter(e => e.kind === 'progress' || e.kind === 'error');
}

// ─────────────────────────────────────────────────────────────────────────────
// Component
// ─────────────────────────────────────────────────────────────────────────────

/**
 * Renders the Linear AI Consumer event history. See {@link ILinearRunProgressListProps}.
 */
export const LinearRunProgressList: React.FC<ILinearRunProgressListProps> = ({
  events,
  mode = 'scrolling-text',
  className,
  isStreaming = false,
  emptyText = 'Waiting for progress updates…',
}) => {
  const styles = useStyles();
  const scrollRef = React.useRef<HTMLDivElement | null>(null);

  const displayEvents = React.useMemo(() => selectDisplayEvents(events), [events]);

  // Auto-scroll to the bottom on new events so the freshest message stays in view.
  React.useEffect(() => {
    if (mode !== 'scrolling-text') return;
    const el = scrollRef.current;
    if (el) {
      el.scrollTop = el.scrollHeight;
    }
  }, [displayEvents.length, mode]);

  // ── Compact mode: single line showing the most recent progress event ──
  if (mode === 'compact') {
    const latest = displayEvents[displayEvents.length - 1];
    return (
      <div className={[styles.compactRoot, className].filter(Boolean).join(' ')} role="status" aria-live="polite">
        {isStreaming && <Spinner size="tiny" />}
        <Text size={200} className={latest?.kind === 'error' ? styles.contentMuted : styles.content}>
          {latest?.content ?? emptyText}
        </Text>
      </div>
    );
  }

  // ── Scrolling-text mode: full history + streaming indicator ────────────
  return (
    <div
      ref={scrollRef}
      className={[styles.scrollingRoot, className].filter(Boolean).join(' ')}
      role="log"
      aria-live="polite"
      aria-relevant="additions"
    >
      {displayEvents.length === 0 ? (
        <Text size={200} className={styles.empty}>
          {emptyText}
        </Text>
      ) : (
        displayEvents.map((event, idx) => (
          <div key={idx} className={styles.row}>
            <Text size={200} className={event.kind === 'error' ? styles.contentMuted : styles.content}>
              {event.content ?? ''}
            </Text>
            <Text size={100} className={styles.timestamp}>
              {formatTimestamp(event.timestamp)}
            </Text>
          </div>
        ))
      )}
      {isStreaming && (
        <div className={styles.streamingIndicator}>
          <Spinner size="tiny" />
          <Text size={200}>Working…</Text>
        </div>
      )}
    </div>
  );
};

export default LinearRunProgressList;
