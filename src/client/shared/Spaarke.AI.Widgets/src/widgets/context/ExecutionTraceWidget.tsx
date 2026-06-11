/**
 * @spaarke/ai-widgets — ExecutionTraceWidget
 *
 * Context-pane widget that renders an ordered, Claude-Code-like timeline of
 * the chat agent's deterministic activity. Subscribes to the six `context.*`
 * trace event types added by R6 task 059 and renders them as a vertical list
 * with the OLDEST entry at the top and the NEWEST at the bottom.
 *
 * Subscribed event types (R6 task 059):
 *   - `tool_call_started`       — agent invoked a registered tool
 *   - `tool_call_completed`     — tool invocation returned
 *   - `knowledge_retrieved`     — knowledge-source retrieval produced a hit
 *   - `playbook_node_executing` — a playbook node has started executing
 *   - `playbook_node_completed` — a playbook node has finished
 *   - `decision_made`           — agent made an enumerated decision
 *
 * ── ADR-015 BINDING (CRITICAL) ────────────────────────────────────────────
 *
 * The widget renders ONLY the typed enumerated fields from each event payload:
 *
 *   - timestamp        — ISO-8601 UTC (rendered as `HH:mm:ss`)
 *   - toolName         — registered tool name (Tier 1 safe)
 *   - durationMs       — wall-clock duration (Tier 1 safe)
 *   - success          — boolean outcome (Tier 1 safe)
 *   - knowledgeSourceId — registered source ID (Tier 1 safe)
 *   - relevanceScore   — numeric 0..1 (Tier 1 safe)
 *   - playbookId       — config ID (Tier 1 safe)
 *   - nodeId           — config ID (Tier 1 safe)
 *   - decision         — short enum-like string (Tier 1 safe)
 *   - decisionReason   — machine summary (Tier 1 safe; emitter responsibility)
 *
 * The widget NEVER renders `contextData`, `contextType`, `selectionRef`, or
 * any extra free-form fields that may have been attached to the event by a
 * misbehaving emitter. Each row builds its display string from the typed
 * fields above ONLY — defense in depth against accidental user-content leak.
 *
 * Standards:
 *   - ADR-012: lives in `@spaarke/ai-widgets`; Fluent v9 components.
 *   - ADR-015: typed-field-only rendering (see above).
 *   - ADR-021: zero hardcoded colors; Fluent v9 semantic tokens only.
 *   - ADR-022: React 19 functional component + hooks.
 *   - ADR-030: subscribes to existing `context` channel — NO new channel.
 *   - NFR-05:  4-channel PaneEventBus preserved.
 *
 * In-memory event log:
 *   - Capped at MAX_TRACE_ENTRIES (50) with FIFO eviction (oldest dropped).
 *   - Auto-scrolls to the newest entry on each addition.
 *
 * Task: R6-061 (D-C-14, Pillar 6c).
 */

import React, { useCallback, useRef, useState, useEffect, useMemo } from 'react';
import {
  makeStyles,
  mergeClasses,
  tokens,
  Text,
  Divider,
  Spinner,
} from '@fluentui/react-components';
import {
  WrenchRegular,
  CheckmarkCircleRegular,
  ErrorCircleRegular,
  BookSearchRegular,
  FlowRegular,
  BranchRegular,
  HistoryRegular,
} from '@fluentui/react-icons';

import type { ContextWidgetProps } from '../../types/widget-types';
import { usePaneEvent } from '../../events/usePaneEvent';
import type { ContextPaneEvent } from '../../events/PaneEventTypes';

// ---------------------------------------------------------------------------
// Constants
// ---------------------------------------------------------------------------

/** Widget type ID under which `ExecutionTraceWidget` is registered. */
export const EXECUTION_TRACE_WIDGET_TYPE = 'execution-trace' as const;

/**
 * Maximum number of trace entries retained in the in-memory event log. When a
 * new entry would push the count above this cap, the OLDEST entry is dropped
 * (FIFO). The cap is intentionally modest — the widget is a live activity
 * surface, not an audit log; older entries live in the BFF audit log
 * accessible via `correlationId` per ADR-015.
 */
export const MAX_TRACE_ENTRIES = 50;

/**
 * The six discriminants from R6 task 059 that this widget renders. The
 * widget subscribes to the `context` channel (existing — no new channel per
 * ADR-030 / NFR-05) and filters incoming events to this enumerated set.
 */
const TRACE_EVENT_TYPES = [
  'tool_call_started',
  'tool_call_completed',
  'knowledge_retrieved',
  'playbook_node_executing',
  'playbook_node_completed',
  'decision_made',
] as const;

type TraceEventType = (typeof TRACE_EVENT_TYPES)[number];

// ---------------------------------------------------------------------------
// Public data types
// ---------------------------------------------------------------------------

/**
 * Data payload delivered to the widget via `ContextWidgetProps.data`.
 *
 * The widget is fully self-contained for live trace events — it subscribes
 * to the PaneEventBus directly and does not require any data prop. The data
 * field is optional and reserved for future use (e.g. a server-pre-seeded
 * historical trace replay). When absent, the widget starts from an empty
 * trace and accumulates events from the live subscription.
 */
export interface ExecutionTraceData {
  /**
   * Optional session filter — when set, the widget only retains and renders
   * trace events whose `sessionId` matches this value. When absent (or
   * empty), all trace events are accepted regardless of session.
   *
   * Used by the SpaarkeAi shell to scope the trace widget to the active
   * chat session — preventing trace bleed-through across sessions.
   */
  sessionId?: string;
}

/**
 * One entry in the in-memory trace log. Carries ONLY the typed enumerated
 * fields necessary to render the row. NEVER carries free-form content fields
 * from the raw `ContextPaneEvent` (such as `contextData`, `contextType`,
 * `selectionRef`, `selectedFileId`, etc.) — defense in depth per ADR-015.
 *
 * The `id` is a per-entry monotonic identifier used as the React list key;
 * it is allocated by the widget when the entry is appended.
 */
interface TraceEntry {
  /** Per-entry monotonic ID — used as React list key. */
  id: number;
  /** Type discriminant (one of the six R6 task 059 event types). */
  type: TraceEventType;
  /** ISO-8601 UTC timestamp from the event payload (`timestamp`). */
  timestamp: string;
  /** Optional correlation ID (BFF trace identifier). */
  correlationId?: string;
  // Per-type typed fields (subset of `ContextPaneEvent`). Each field is a
  // Tier 1 safe value per ADR-015 — see the JSDoc on `ContextPaneEvent` for
  // the binding rationale.
  toolName?: string;
  durationMs?: number;
  success?: boolean;
  knowledgeSourceId?: string;
  relevanceScore?: number;
  playbookId?: string;
  nodeId?: string;
  decision?: string;
  decisionReason?: string;
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

  // Per-entry row. Card surface keeps each entry visually distinct.
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
  entryIconSuccess: {
    color: tokens.colorPaletteGreenForeground1,
  },
  entryIconError: {
    color: tokens.colorPaletteRedForeground1,
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
    overflow: 'hidden',
    textOverflow: 'ellipsis',
    whiteSpace: 'nowrap',
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
 * Type-guard: returns `true` when the event's `type` field is one of the six
 * trace discriminants from R6 task 059. Used by the subscription handler to
 * filter incoming `context.*` events.
 */
function isTraceEventType(type: string): type is TraceEventType {
  return (TRACE_EVENT_TYPES as readonly string[]).includes(type);
}

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
 * `''` when undefined.
 */
function formatDuration(ms?: number): string {
  if (typeof ms !== 'number' || !Number.isFinite(ms) || ms < 0) return '';
  if (ms < 1000) return `${Math.round(ms)} ms`;
  return `${(ms / 1000).toFixed(2)} s`;
}

/**
 * Format a numeric relevance score as a percentage / fixed-decimal label.
 * Returns `''` when undefined.
 */
function formatScore(score?: number): string {
  if (typeof score !== 'number' || !Number.isFinite(score)) return '';
  // Clamp to [0, 1] to defend against emitter-side normalization bugs.
  const clamped = Math.max(0, Math.min(1, score));
  return clamped.toFixed(2);
}

/**
 * Pick a Fluent v9 icon for a trace entry based on its event type and (for
 * completion events) success outcome.
 */
function pickIcon(
  entry: TraceEntry,
  styles: ReturnType<typeof useStyles>
): React.ReactElement {
  switch (entry.type) {
    case 'tool_call_started':
      return <WrenchRegular className={styles.entryIcon} aria-hidden="true" />;
    case 'tool_call_completed':
      return entry.success === false ? (
        <ErrorCircleRegular
          className={mergeClasses(styles.entryIcon, styles.entryIconError)}
          aria-hidden="true"
        />
      ) : (
        <CheckmarkCircleRegular
          className={mergeClasses(styles.entryIcon, styles.entryIconSuccess)}
          aria-hidden="true"
        />
      );
    case 'knowledge_retrieved':
      return <BookSearchRegular className={styles.entryIcon} aria-hidden="true" />;
    case 'playbook_node_executing':
      return <FlowRegular className={styles.entryIcon} aria-hidden="true" />;
    case 'playbook_node_completed':
      return entry.success === false ? (
        <ErrorCircleRegular
          className={mergeClasses(styles.entryIcon, styles.entryIconError)}
          aria-hidden="true"
        />
      ) : (
        <CheckmarkCircleRegular
          className={mergeClasses(styles.entryIcon, styles.entryIconSuccess)}
          aria-hidden="true"
        />
      );
    case 'decision_made':
      return <BranchRegular className={styles.entryIcon} aria-hidden="true" />;
    default: {
      // Exhaustiveness check — defensive. Unreachable under the type-guard.
      const _exhaustive: never = entry.type;
      void _exhaustive;
      return <HistoryRegular className={styles.entryIcon} aria-hidden="true" />;
    }
  }
}

/**
 * Build the primary label (one-line summary) for a trace entry. ONLY the
 * typed enumerated fields from the event payload contribute — defense in
 * depth per ADR-015.
 */
function buildLabel(entry: TraceEntry): string {
  switch (entry.type) {
    case 'tool_call_started':
      return `Tool: ${entry.toolName ?? '(unknown)'}`;
    case 'tool_call_completed': {
      const outcome = entry.success === false ? ' (failed)' : '';
      return `Tool: ${entry.toolName ?? '(unknown)'}${outcome}`;
    }
    case 'knowledge_retrieved':
      return `Knowledge: ${entry.knowledgeSourceId ?? '(unknown)'}`;
    case 'playbook_node_executing':
      return `Node: ${entry.nodeId ?? '(unknown)'}`;
    case 'playbook_node_completed': {
      const outcome = entry.success === false ? ' (failed)' : '';
      return `Node: ${entry.nodeId ?? '(unknown)'}${outcome}`;
    }
    case 'decision_made':
      return `Decision: ${entry.decision ?? '(unknown)'}`;
    default: {
      const _exhaustive: never = entry.type;
      void _exhaustive;
      return 'Event';
    }
  }
}

/**
 * Build the secondary detail (subline) for a trace entry. ONLY the typed
 * enumerated fields from the event payload contribute. Returns `null` when
 * there is no useful subline.
 */
function buildDetail(entry: TraceEntry): string | null {
  switch (entry.type) {
    case 'tool_call_started':
      return null;
    case 'tool_call_completed': {
      const dur = formatDuration(entry.durationMs);
      return dur || null;
    }
    case 'knowledge_retrieved': {
      const score = formatScore(entry.relevanceScore);
      return score ? `Relevance: ${score}` : null;
    }
    case 'playbook_node_executing':
      return entry.playbookId ? `Playbook: ${entry.playbookId}` : null;
    case 'playbook_node_completed': {
      const dur = formatDuration(entry.durationMs);
      const pb = entry.playbookId ? `Playbook: ${entry.playbookId}` : '';
      if (dur && pb) return `${pb} · ${dur}`;
      return dur || pb || null;
    }
    case 'decision_made':
      return entry.decisionReason ?? null;
    default: {
      const _exhaustive: never = entry.type;
      void _exhaustive;
      return null;
    }
  }
}

// ---------------------------------------------------------------------------
// Sub-component: TraceRow
// ---------------------------------------------------------------------------

interface TraceRowProps {
  entry: TraceEntry;
  styles: ReturnType<typeof useStyles>;
}

/**
 * One row in the trace timeline. Renders icon + label + timestamp + optional
 * detail subline. Stateless / pure.
 */
const TraceRow: React.FC<TraceRowProps> = ({ entry, styles }) => {
  const label = buildLabel(entry);
  const detail = buildDetail(entry);
  const ts = formatTimestamp(entry.timestamp);

  return (
    <div
      className={styles.entry}
      role="listitem"
      data-testid="execution-trace-row"
      data-event-type={entry.type}
      data-entry-id={entry.id}
    >
      {pickIcon(entry, styles)}
      <div className={styles.entryBody}>
        <div className={styles.entryHeaderRow}>
          <Text className={styles.entryLabel} title={label}>
            {label}
          </Text>
          <Text className={styles.entryTimestamp} title={entry.timestamp}>
            {ts}
          </Text>
        </div>
        {detail !== null && (
          <Text className={styles.entryDetail} title={detail}>
            {detail}
          </Text>
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
      Agent activity will appear here as tools run, knowledge is retrieved, and decisions are made.
    </Text>
  </div>
);

// ---------------------------------------------------------------------------
// ExecutionTraceWidget
// ---------------------------------------------------------------------------

/**
 * ExecutionTraceWidget — Context-pane Claude-Code-like execution trace.
 *
 * Subscribes to the `context` channel and filters for the six trace event
 * types added by R6 task 059. Maintains an in-memory FIFO log capped at
 * `MAX_TRACE_ENTRIES` and renders the entries in chronological order
 * (OLDEST first; NEWEST at bottom). Auto-scrolls to the newest entry.
 *
 * @see ContextWidgetProps
 * @see ExecutionTraceData
 */
const ExecutionTraceWidget: React.FC<ExecutionTraceWidgetProps> = ({
  data,
  isLoading,
  className,
}) => {
  const styles = useStyles();
  const sessionFilter = data?.sessionId ?? '';

  const [entries, setEntries] = useState<TraceEntry[]>([]);
  // Monotonic ID source for the React list keys.
  const nextIdRef = useRef<number>(1);

  // Auto-scroll target — the sentinel sits at the BOTTOM of the list and is
  // scrolled into view whenever a new entry is appended.
  const scrollEndRef = useRef<HTMLDivElement | null>(null);

  // Capture handler for `context.*` events.
  //
  // CRITICAL: the handler builds the in-memory `TraceEntry` from the typed
  // enumerated fields of the incoming `ContextPaneEvent` ONLY. We deliberately
  // do NOT spread the event payload — that would risk smuggling
  // `contextData`, `contextType`, `selectionRef`, or other fields into the
  // entry. Per ADR-015: tool name + decision + timestamp ONLY.
  const handleContextEvent = useCallback(
    (event: ContextPaneEvent) => {
      if (!isTraceEventType(event.type)) return;
      // Defense in depth: events without a timestamp cannot be ordered and
      // are dropped — emitters are contractually required to attach one.
      if (typeof event.timestamp !== 'string' || event.timestamp.length === 0) return;
      // Session filter (defense in depth): when the host supplies a
      // sessionId in data, only retain events with the matching sessionId.
      if (sessionFilter !== '' && event.sessionId !== sessionFilter) return;

      const id = nextIdRef.current;
      nextIdRef.current = id + 1;

      // Build the entry from the typed enumerated fields ONLY. We use an
      // explicit per-field copy (NOT a spread) so any unexpected fields on
      // the incoming event are physically excluded.
      const entry: TraceEntry = {
        id,
        type: event.type,
        timestamp: event.timestamp,
        correlationId: event.correlationId,
        toolName: event.toolName,
        durationMs: event.durationMs,
        success: event.success,
        knowledgeSourceId: event.knowledgeSourceId,
        relevanceScore: event.relevanceScore,
        playbookId: event.playbookId,
        nodeId: event.nodeId,
        decision: event.decision,
        decisionReason: event.decisionReason,
      };

      setEntries(prev => {
        const next = prev.length >= MAX_TRACE_ENTRIES ? prev.slice(1) : prev;
        return [...next, entry];
      });
    },
    [sessionFilter]
  );

  usePaneEvent('context', handleContextEvent);

  // Auto-scroll to the newest entry on each addition. `scrollIntoView` with
  // `block: 'end'` keeps the scroll pinned at the bottom unless the user has
  // scrolled away (browsers preserve user scroll intent).
  useEffect(() => {
    if (entries.length === 0) return;
    const target = scrollEndRef.current;
    if (target && typeof target.scrollIntoView === 'function') {
      target.scrollIntoView({ block: 'end', behavior: 'smooth' });
    }
  }, [entries.length]);

  // Static label used as the widget aria-label / sub-title.
  const subtitle = useMemo(() => {
    if (entries.length === 0) return 'Waiting for activity';
    const count = entries.length;
    const noun = count === 1 ? 'event' : 'events';
    return `${count} ${noun} captured`;
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
          <div className={styles.list} role="list" aria-label="Agent activity">
            {entries.map(entry => (
              <TraceRow key={entry.id} entry={entry} styles={styles} />
            ))}
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
