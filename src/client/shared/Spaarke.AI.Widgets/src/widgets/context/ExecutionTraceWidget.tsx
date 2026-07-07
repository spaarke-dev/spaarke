/**
 * @spaarke/ai-widgets — ExecutionTraceWidget
 *
 * Context-pane widget that renders the session ledger's persisted `ToolChain`
 * entries (ADR-040) as an ordered, Claude-Code-like timeline of the agent's
 * deterministic tool activity — the client face of ADR-040 decision
 * traceability (ai-architecture-redesign-r1 task 046 / FR-P3-07).
 *
 * ── Data source (ADR-040 BINDING) ─────────────────────────────────────────
 *
 * The widget subscribes to the `context` channel and consumes the
 * `tool_chain` event ONLY. Each event carries one ledger `ToolChain` segment
 * that the BFF PERSISTED to the session ledger BEFORE emitting the event
 * (storage precedes rendering — `ChatEndpoints.FlushToolChainLedgerAsync`
 * appends via `ChatSessionManager.AppendToolChainAsync`, THEN writes the
 * `context_event` SSE frame). The widget renders those persisted records
 * verbatim; it MUST NOT and does not synthesize trace data client-side.
 *
 * This replaces the legacy R6 live-telemetry trace source (the six
 * `tool_call_started` / `tool_call_completed` / … events emitted by
 * `ContextEventEmitter` from adapter call sites) as the widget's data source.
 * Those events remain on the bus vocabulary for telemetry, but the trace
 * widget no longer renders them — the ledger is the single source of truth
 * for what the agent did.
 *
 * ── NFR-07 / ADR-015 BINDING ──────────────────────────────────────────────
 *
 * Each rendered row exposes ONLY the ledger `SessionToolCall` projection:
 *
 *   - toolId        — namespaced tool id (Tier 1 safe)
 *   - argsSummary   — identifier/filter summary, redacted at RECORDING time by
 *                     `AgentTurnContract.SummarizeArguments` (free text arrives
 *                     as `<redacted:len>` markers — never verbatim content)
 *   - resultCount   — numeric result count (Tier 1 safe)
 *   - citationCount — numeric citation count (ids stay in the ledger)
 *   - durationMs    — wall-clock duration (Tier 1 safe)
 *   - turn          — 1-based session turn ordinal (Tier 1 safe)
 *   - timestamp     — ISO-8601 UTC (rendered as `HH:mm:ss`)
 *
 * The handler copies these typed fields explicitly (never spreads the event)
 * — defense in depth against a misbehaving emitter attaching extra fields.
 *
 * Standards:
 *   - ADR-012: lives in `@spaarke/ai-widgets`; Fluent v9 components.
 *   - ADR-015 / NFR-07: identifiers/counts-only rendering (see above).
 *   - ADR-021: zero hardcoded colors; Fluent v9 semantic tokens only.
 *   - ADR-022: React 19 functional component + hooks.
 *   - ADR-030: subscribes to existing `context` channel — NO new channel.
 *   - ADR-039: pure render surface — no routing/intent logic in the widget.
 *   - NFR-05:  4-channel PaneEventBus preserved.
 *
 * In-memory log:
 *   - Capped at MAX_TRACE_ENTRIES (50) rendered calls with FIFO eviction.
 *   - Auto-scrolls to the newest entry on each addition.
 *   - Live-session surface, not an audit log — the full history lives in the
 *     session ledger (Redis/Cosmos via the 3-tier session stack).
 *
 * Task: ai-architecture-redesign-r1 046 (supersedes R6-061 live-trace source).
 */

import React, { useCallback, useRef, useState, useEffect, useMemo } from 'react';
import { makeStyles, mergeClasses, tokens, Text, Divider, Spinner, Badge } from '@fluentui/react-components';
import { WrenchRegular, HistoryRegular } from '@fluentui/react-icons';

import type { ContextWidgetProps } from '../../types/widget-types';
import { usePaneEvent } from '../../events/usePaneEvent';
import type { ContextPaneEvent, TraceToolCallSummary } from '../../events/PaneEventTypes';

// ---------------------------------------------------------------------------
// Constants
// ---------------------------------------------------------------------------

/** Widget type ID under which `ExecutionTraceWidget` is registered. */
export const EXECUTION_TRACE_WIDGET_TYPE = 'execution-trace' as const;

/**
 * Maximum number of rendered tool-call rows retained in the in-memory log.
 * When a new ledger segment would push the count above this cap, the OLDEST
 * rows are dropped (FIFO). The cap is intentionally modest — the widget is a
 * live activity surface, not an audit log; the persisted `ToolChain` entries
 * live in the session ledger (ADR-040) and remain addressable there.
 */
export const MAX_TRACE_ENTRIES = 50;

// ---------------------------------------------------------------------------
// Public data types
// ---------------------------------------------------------------------------

/**
 * Data payload delivered to the widget via `ContextWidgetProps.data`.
 *
 * The widget is self-contained for live ledger events — it subscribes to the
 * PaneEventBus directly and does not require any data prop.
 */
export interface ExecutionTraceData {
  /**
   * Optional session filter — when set AND an incoming `tool_chain` event
   * carries a `sessionId`, only matching events are retained. Events without
   * a sessionId are accepted (the SSE relay is per-request/per-session, so
   * cross-session bleed cannot occur on the transport).
   */
  sessionId?: string;
}

/**
 * One rendered row: a persisted ledger `SessionToolCall` plus the segment
 * metadata it arrived with (turn + persisted-at timestamp). Built by explicit
 * per-field copy from the `tool_chain` event — never a spread.
 */
interface TraceEntry {
  /** Per-entry monotonic ID — used as React list key. */
  id: number;
  /** 1-based session turn the persisted segment belongs to. */
  turn: number;
  /** ISO-8601 UTC timestamp the segment was persisted/emitted. */
  timestamp: string;
  /** Ledger `SessionToolCall.ToolId`. */
  toolId: string;
  /** Ledger `SessionToolCall.ArgsSummary` (redacted at recording time). */
  argsSummary?: string;
  /** Ledger `SessionToolCall.ResultCount`. */
  resultCount?: number;
  /** Count of ledger citation ids (ids themselves stay in the ledger). */
  citationCount?: number;
  /** Ledger `SessionToolCall.DurationMs`. */
  durationMs?: number;
}

export type ExecutionTraceWidgetProps = ContextWidgetProps<ExecutionTraceData>;

// ---------------------------------------------------------------------------
// Styles — Fluent v9 semantic tokens only (ADR-021)
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  root: {
    display: 'flex',
    flexDirection: 'column',
    height: '100%',
    minHeight: 0,
    boxSizing: 'border-box',
    backgroundColor: tokens.colorNeutralBackground1,
  },
  header: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalXXS,
    paddingTop: tokens.spacingVerticalM,
    paddingBottom: tokens.spacingVerticalS,
    paddingLeft: tokens.spacingHorizontalM,
    paddingRight: tokens.spacingHorizontalM,
    flexShrink: 0,
  },
  headerTitle: {
    fontWeight: tokens.fontWeightSemibold,
    fontSize: tokens.fontSizeBase400,
    color: tokens.colorNeutralForeground1,
  },
  headerSubtitle: {
    fontSize: tokens.fontSizeBase200,
    color: tokens.colorNeutralForeground3,
  },

  // Scrollable timeline area. Auto-scrolls on new entries.
  scrollContainer: {
    flex: 1,
    minHeight: 0,
    overflowY: 'auto',
    paddingLeft: tokens.spacingHorizontalM,
    paddingRight: tokens.spacingHorizontalM,
    paddingBottom: tokens.spacingVerticalM,
  },
  list: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalS,
  },

  // Turn separator row (renders between segments of different turns).
  turnDivider: {
    display: 'flex',
    flexDirection: 'row',
    alignItems: 'center',
    gap: tokens.spacingHorizontalS,
    paddingTop: tokens.spacingVerticalXS,
  },
  turnDividerLabel: {
    fontSize: tokens.fontSizeBase200,
    color: tokens.colorNeutralForeground3,
    fontWeight: tokens.fontWeightSemibold,
    whiteSpace: 'nowrap',
  },
  turnDividerLine: {
    flex: 1,
  },

  // Per-call row. Card surface keeps each entry visually distinct.
  entry: {
    display: 'flex',
    flexDirection: 'row',
    alignItems: 'flex-start',
    gap: tokens.spacingHorizontalS,
    paddingTop: tokens.spacingVerticalS,
    paddingBottom: tokens.spacingVerticalS,
    paddingLeft: tokens.spacingHorizontalM,
    paddingRight: tokens.spacingHorizontalM,
    backgroundColor: tokens.colorNeutralBackground2,
    borderTopLeftRadius: tokens.borderRadiusMedium,
    borderTopRightRadius: tokens.borderRadiusMedium,
    borderBottomLeftRadius: tokens.borderRadiusMedium,
    borderBottomRightRadius: tokens.borderRadiusMedium,
    borderTopWidth: tokens.strokeWidthThin,
    borderRightWidth: tokens.strokeWidthThin,
    borderBottomWidth: tokens.strokeWidthThin,
    borderLeftWidth: tokens.strokeWidthThin,
    borderTopStyle: 'solid',
    borderRightStyle: 'solid',
    borderBottomStyle: 'solid',
    borderLeftStyle: 'solid',
    borderTopColor: tokens.colorNeutralStroke2,
    borderRightColor: tokens.colorNeutralStroke2,
    borderBottomColor: tokens.colorNeutralStroke2,
    borderLeftColor: tokens.colorNeutralStroke2,
  },
  entryIcon: {
    color: tokens.colorBrandForeground1,
    fontSize: tokens.fontSizeBase400,
    flexShrink: 0,
    marginTop: '2px',
  },
  entryBody: {
    display: 'flex',
    flexDirection: 'column',
    minWidth: 0,
    flex: 1,
    gap: tokens.spacingVerticalXXS,
  },
  entryHeaderRow: {
    display: 'flex',
    flexDirection: 'row',
    alignItems: 'center',
    gap: tokens.spacingHorizontalXS,
    flexWrap: 'wrap',
  },
  entryLabel: {
    fontWeight: tokens.fontWeightSemibold,
    fontSize: tokens.fontSizeBase300,
    color: tokens.colorNeutralForeground1,
    overflow: 'hidden',
    textOverflow: 'ellipsis',
    whiteSpace: 'nowrap',
  },
  entryTimestamp: {
    fontSize: tokens.fontSizeBase200,
    color: tokens.colorNeutralForeground3,
    fontFamily: tokens.fontFamilyMonospace,
    marginLeft: 'auto',
    flexShrink: 0,
  },
  entryDetail: {
    fontSize: tokens.fontSizeBase200,
    color: tokens.colorNeutralForeground2,
    fontFamily: tokens.fontFamilyMonospace,
    overflow: 'hidden',
    textOverflow: 'ellipsis',
    whiteSpace: 'nowrap',
  },
  entryMetaRow: {
    display: 'flex',
    flexDirection: 'row',
    alignItems: 'center',
    gap: tokens.spacingHorizontalXS,
    flexWrap: 'wrap',
  },
  entryMetaText: {
    fontSize: tokens.fontSizeBase200,
    color: tokens.colorNeutralForeground3,
  },

  // Empty state — never a blank pane.
  centerState: {
    display: 'flex',
    flexDirection: 'column',
    alignItems: 'center',
    justifyContent: 'center',
    gap: tokens.spacingVerticalM,
    paddingTop: tokens.spacingVerticalXL,
    paddingBottom: tokens.spacingVerticalXL,
    paddingLeft: tokens.spacingHorizontalXL,
    paddingRight: tokens.spacingHorizontalXL,
    flex: 1,
    minHeight: 0,
    textAlign: 'center',
  },
  emptyIcon: {
    color: tokens.colorNeutralForeground4,
    fontSize: '48px',
  },
  emptyTitle: {
    fontWeight: tokens.fontWeightSemibold,
    fontSize: tokens.fontSizeBase400,
    color: tokens.colorNeutralForeground2,
  },
  emptyBody: {
    fontSize: tokens.fontSizeBase200,
    color: tokens.colorNeutralForeground3,
    maxWidth: '280px',
  },
});

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

/**
 * Format an ISO-8601 timestamp as a short `HH:mm:ss` clock string. Returns
 * the original string if parsing fails (defensive — never throw at render
 * time). Time-only (no date) is intentional — the widget is a live activity
 * surface where the date is implicit.
 */
function formatTimestamp(iso: string): string {
  try {
    const d = new Date(iso);
    if (Number.isNaN(d.getTime())) return iso;
    const hh = String(d.getUTCHours()).padStart(2, '0');
    const mm = String(d.getUTCMinutes()).padStart(2, '0');
    const ss = String(d.getUTCSeconds()).padStart(2, '0');
    return `${hh}:${mm}:${ss}`;
  } catch {
    return iso;
  }
}

/**
 * Format a millisecond duration as a short human-readable string. Returns
 * `''` when undefined / invalid.
 */
function formatDuration(ms?: number): string {
  if (typeof ms !== 'number' || !Number.isFinite(ms) || ms < 0) return '';
  if (ms < 1000) return `${Math.round(ms)} ms`;
  return `${(ms / 1000).toFixed(2)} s`;
}

/**
 * Narrow one wire call record to the typed identifiers-only shape. Explicit
 * per-field copy (never a spread) — any unexpected fields on the incoming
 * record are physically excluded (NFR-07 defense in depth).
 */
function narrowCall(raw: TraceToolCallSummary): Omit<TraceEntry, 'id' | 'turn' | 'timestamp'> | null {
  if (typeof raw !== 'object' || raw === null) return null;
  if (typeof raw.toolId !== 'string' || raw.toolId.length === 0) return null;
  return {
    toolId: raw.toolId,
    argsSummary: typeof raw.argsSummary === 'string' ? raw.argsSummary : undefined,
    resultCount: typeof raw.resultCount === 'number' ? raw.resultCount : undefined,
    citationCount: typeof raw.citationCount === 'number' ? raw.citationCount : undefined,
    durationMs: typeof raw.durationMs === 'number' ? raw.durationMs : undefined,
  };
}

// ---------------------------------------------------------------------------
// Sub-component: TraceRow
// ---------------------------------------------------------------------------

interface TraceRowProps {
  entry: TraceEntry;
  styles: ReturnType<typeof useStyles>;
}

/**
 * One persisted tool call in the timeline. Renders tool id + timestamp +
 * optional args summary + counts/duration meta. Stateless / pure.
 */
const TraceRow: React.FC<TraceRowProps> = ({ entry, styles }) => {
  const ts = formatTimestamp(entry.timestamp);
  const dur = formatDuration(entry.durationMs);
  const metaParts: string[] = [];
  if (typeof entry.resultCount === 'number') {
    metaParts.push(`${entry.resultCount} result${entry.resultCount === 1 ? '' : 's'}`);
  }
  if (typeof entry.citationCount === 'number' && entry.citationCount > 0) {
    metaParts.push(`${entry.citationCount} citation${entry.citationCount === 1 ? '' : 's'}`);
  }
  if (dur) {
    metaParts.push(dur);
  }

  return (
    <div
      className={styles.entry}
      role="listitem"
      data-testid="execution-trace-row"
      data-tool-id={entry.toolId}
      data-turn={entry.turn}
      data-entry-id={entry.id}
    >
      <WrenchRegular className={styles.entryIcon} aria-hidden="true" />
      <div className={styles.entryBody}>
        <div className={styles.entryHeaderRow}>
          <Text className={styles.entryLabel} title={entry.toolId}>
            {entry.toolId}
          </Text>
          <Text className={styles.entryTimestamp} title={entry.timestamp}>
            {ts}
          </Text>
        </div>
        {entry.argsSummary !== undefined && entry.argsSummary.length > 0 && (
          <Text className={styles.entryDetail} title={entry.argsSummary} data-testid="execution-trace-args">
            {entry.argsSummary}
          </Text>
        )}
        {metaParts.length > 0 && (
          <div className={styles.entryMetaRow}>
            <Text className={styles.entryMetaText} data-testid="execution-trace-meta">
              {metaParts.join(' · ')}
            </Text>
          </div>
        )}
      </div>
    </div>
  );
};

// ---------------------------------------------------------------------------
// Sub-component: ExecutionTraceEmpty
// ---------------------------------------------------------------------------

const ExecutionTraceEmpty: React.FC<{ styles: ReturnType<typeof useStyles> }> = ({ styles }) => (
  <div className={styles.centerState} role="status" aria-label="No execution trace yet">
    <HistoryRegular className={styles.emptyIcon} aria-hidden="true" />
    <Text className={styles.emptyTitle}>No execution trace yet</Text>
    <Text className={styles.emptyBody}>
      Tool activity will appear here as the agent works — each entry is read from the session ledger after it is
      recorded.
    </Text>
  </div>
);

// ---------------------------------------------------------------------------
// ExecutionTraceWidget
// ---------------------------------------------------------------------------

/**
 * ExecutionTraceWidget — Context-pane render surface for the session ledger's
 * persisted `ToolChain` entries (ADR-040).
 *
 * Subscribes to the `context` channel and consumes `tool_chain` events only.
 * Maintains an in-memory FIFO log capped at `MAX_TRACE_ENTRIES` and renders
 * calls in chronological order (OLDEST first; NEWEST at bottom), grouped by
 * session turn. Auto-scrolls to the newest entry.
 *
 * @see ContextWidgetProps
 * @see ExecutionTraceData
 */
const ExecutionTraceWidget: React.FC<ExecutionTraceWidgetProps> = ({ data, isLoading, className }) => {
  const styles = useStyles();
  const sessionFilter = data?.sessionId ?? '';

  const [entries, setEntries] = useState<TraceEntry[]>([]);
  // Monotonic ID source for the React list keys.
  const nextIdRef = useRef<number>(1);

  // Auto-scroll target — the sentinel sits at the BOTTOM of the list and is
  // scrolled into view whenever a new entry is appended.
  const scrollEndRef = useRef<HTMLDivElement | null>(null);

  // Handler for `context.tool_chain` events (the ledger bridge — ADR-040).
  //
  // CRITICAL: entries are built from the typed identifier fields of the
  // incoming event ONLY (explicit per-field copy via `narrowCall` — never a
  // spread). Per NFR-07: identifiers / filters / counts / durations only.
  const handleContextEvent = useCallback(
    (event: ContextPaneEvent) => {
      if (event.type !== 'tool_chain') return;
      // Session filter (defense in depth): only applied when BOTH sides carry
      // a session id — the SSE transport is already per-session.
      if (sessionFilter !== '' && typeof event.sessionId === 'string' && event.sessionId !== sessionFilter) {
        return;
      }
      const calls = event.toolChainCalls;
      if (!Array.isArray(calls) || calls.length === 0) return;
      const turn = typeof event.turn === 'number' ? event.turn : 0;
      const timestamp =
        typeof event.timestamp === 'string' && event.timestamp.length > 0 ? event.timestamp : new Date().toISOString();

      const appended: TraceEntry[] = [];
      for (const raw of calls) {
        const narrowed = narrowCall(raw);
        if (narrowed === null) continue;
        const id = nextIdRef.current;
        nextIdRef.current = id + 1;
        appended.push({ id, turn, timestamp, ...narrowed });
      }
      if (appended.length === 0) return;

      setEntries(prev => {
        const merged = [...prev, ...appended];
        return merged.length > MAX_TRACE_ENTRIES ? merged.slice(merged.length - MAX_TRACE_ENTRIES) : merged;
      });
    },
    [sessionFilter]
  );

  usePaneEvent('context', handleContextEvent);

  // Auto-scroll to the newest entry on each addition.
  useEffect(() => {
    if (entries.length === 0) return;
    const target = scrollEndRef.current;
    if (target && typeof target.scrollIntoView === 'function') {
      target.scrollIntoView({ block: 'end', behavior: 'smooth' });
    }
  }, [entries.length]);

  const subtitle = useMemo(() => {
    if (entries.length === 0) return 'Waiting for activity';
    const count = entries.length;
    const noun = count === 1 ? 'tool call' : 'tool calls';
    return `${count} ${noun} from the session ledger`;
  }, [entries.length]);

  // Loading shim — the widget itself doesn't load any data, but the prop is
  // honoured for consistency with the ContextWidgetProps contract.
  if (isLoading) {
    return (
      <div
        className={mergeClasses(styles.root, className)}
        role="region"
        aria-label="Execution trace"
        data-testid="execution-trace-widget"
      >
        <div className={styles.centerState} role="status" aria-busy="true" aria-label="Loading">
          <Spinner size="small" />
        </div>
      </div>
    );
  }

  return (
    <div
      className={mergeClasses(styles.root, className)}
      role="region"
      aria-label="Execution trace"
      data-testid="execution-trace-widget"
    >
      <div className={styles.header}>
        <Text className={styles.headerTitle}>Execution Trace</Text>
        <Text className={styles.headerSubtitle}>{subtitle}</Text>
      </div>
      <Divider appearance="subtle" />

      {entries.length === 0 ? (
        <ExecutionTraceEmpty styles={styles} />
      ) : (
        <div className={styles.scrollContainer} data-testid="execution-trace-scroll">
          <div className={styles.list} role="list" aria-label="Agent tool activity">
            {entries.map((entry, idx) => {
              const isNewTurn = entry.turn > 0 && (idx === 0 || entries[idx - 1].turn !== entry.turn);
              return (
                <React.Fragment key={entry.id}>
                  {isNewTurn && (
                    <div className={styles.turnDivider} data-testid="execution-trace-turn" data-turn={entry.turn}>
                      <Badge appearance="tint" color="brand" size="small">
                        Turn {entry.turn}
                      </Badge>
                      <Divider appearance="subtle" className={styles.turnDividerLine} />
                    </div>
                  )}
                  <TraceRow entry={entry} styles={styles} />
                </React.Fragment>
              );
            })}
            {/* Sentinel: target for auto-scroll. */}
            <div ref={scrollEndRef} aria-hidden="true" />
          </div>
        </div>
      )}
    </div>
  );
};

ExecutionTraceWidget.displayName = 'ExecutionTraceWidget';

export default ExecutionTraceWidget;
