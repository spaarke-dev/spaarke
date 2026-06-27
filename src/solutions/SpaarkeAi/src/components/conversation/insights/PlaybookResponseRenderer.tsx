/**
 * PlaybookResponseRenderer — Playbook-inference path renderer for Insights
 * Assistant responses (R5 task 026 / D2-16).
 *
 * REUSE: this sub-renderer consumes task 017's `StructuredOutputStreamWidget`
 * (via `INSIGHTS_PLAYBOOK_SCHEMA`) per R5 CLAUDE.md §3.1 reuse mandate. The
 * widget renders the playbook envelope in static mode (v1.0) or streaming
 * mode (v1.1 SSE) — ZERO widget code change required (UR-02 evidence; see
 * `projects/spaarke-ai-platform-unification-r5/notes/task-017-widget-evidence.md`
 * §10 reusability handshake).
 *
 * Two integration paths supported:
 *
 *   (a) Direct in-chat rendering: this renderer mounts the
 *       `StructuredOutputStreamWidget` inline below the assistant message via
 *       a direct `<StructuredOutputStreamWidget>` JSX element. Used when the
 *       chat surface wants the structured output to appear in the conversation
 *       pane itself (no workspace tab spawned).
 *
 *   (b) Workspace-tab dispatch: alternative integration where this renderer
 *       dispatches `workspace.widget_load` with `widgetType:
 *       STRUCTURED_OUTPUT_STREAM_WIDGET_TYPE` so the playbook envelope opens
 *       as a new Workspace pane tab. Controlled via the `dispatchToWorkspace`
 *       prop (default: `false` → inline render).
 *
 * Both paths use the same `widgetData` payload — only the mount mechanism
 * differs. The contract is verified by tests:
 *   - Direct mount: renders `<StructuredOutputStreamWidget>` with
 *     `INSIGHTS_PLAYBOOK_SCHEMA` and prefilled fields.
 *   - Workspace dispatch: emits `workspace.widget_load` with the
 *     `STRUCTURED_OUTPUT_STREAM_WIDGET_TYPE` + `INSIGHTS_PLAYBOOK_SCHEMA` payload.
 *
 * ADR-021 (Fluent v9 + dark mode): no hex/rgba/Fluent v8 — semantic tokens
 * only via `makeStyles`. The widget itself owns its theming.
 * ADR-022 (React 19): functional component + hooks.
 * ADR-013 §3.5 (Zone B boundary): no Insights server-internal types imported.
 *
 * @see src/client/shared/Spaarke.AI.Widgets/src/widgets/workspace/StructuredOutputStreamWidget.tsx (task 017)
 * @see projects/spaarke-ai-platform-unification-r5/notes/task-017-widget-evidence.md §10
 */

import * as React from 'react';
import { makeStyles, tokens } from '@fluentui/react-components';
import {
  StructuredOutputStreamWidget,
  STRUCTURED_OUTPUT_STREAM_WIDGET_TYPE,
  INSIGHTS_PLAYBOOK_SCHEMA,
  useDispatchPaneEvent,
  type StructuredOutputStreamWidgetData,
} from '@spaarke/ai-widgets';
import type { PlaybookInferenceResponse } from './types';

// ---------------------------------------------------------------------------
// Props
// ---------------------------------------------------------------------------

export interface PlaybookResponseRendererProps {
  /** Discriminated-union response narrowed to the playbook-inference variant. */
  readonly response: PlaybookInferenceResponse;
  /**
   * When true (Wave F v1.1 SSE active), the widget mounts in `streaming` mode
   * and subscribes to PaneEventBus `workspace.field_delta` events. When false
   * (v1.0 single-shot), the widget mounts in `static` mode with prefilled
   * fields from the response envelope. Default: false (v1.0 graceful
   * fallback per NFR-11).
   */
  readonly streamingEnabled?: boolean;
  /**
   * When true, dispatch `workspace.widget_load` to open the structured output
   * in a new Workspace pane tab instead of rendering inline below the message.
   * The receiving subscriber lives in
   * `src/solutions/SpaarkeAi/src/components/workspace/WorkspacePane.tsx`.
   * Default: false (inline render — the chat surface owns the response).
   */
  readonly dispatchToWorkspace?: boolean;
}

// ---------------------------------------------------------------------------
// Styles — Fluent v9 semantic tokens only (ADR-021)
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  container: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalS,
    /**
     * Constrain the embedded widget so it does not bleed past the chat
     * message bubble. The widget's own root sets its own padding; we just
     * cap the outer height so the conversation pane stays scrollable.
     */
    minHeight: '240px',
    maxHeight: '480px',
    width: '100%',
  },
});

// ---------------------------------------------------------------------------
// Envelope projection — playbook envelope → widget prefilledFields
// ---------------------------------------------------------------------------

/**
 * Stringify an arbitrary value into a single string for the widget's
 * `prefilledFields` payload (a `Record<string, string>`). Strings pass through
 * unchanged; arrays are joined with newlines (the widget's list-display hint
 * splits on newlines per task 017 widget §6); other values are JSON-stringified.
 *
 * Pure function exported for testability.
 */
export function stringifyEnvelopeField(value: unknown): string {
  if (value === null || value === undefined) {
    return '';
  }
  if (typeof value === 'string') {
    return value;
  }
  if (typeof value === 'number' || typeof value === 'boolean') {
    return String(value);
  }
  if (Array.isArray(value)) {
    // Newline join — the widget's list renderer splits on newlines.
    return value
      .map(item => (typeof item === 'string' ? item : JSON.stringify(item)))
      .join('\n');
  }
  // Objects + nested structures — JSON.stringify for now. Per UR-02 80/20:
  // nested-object editing is a future enhancement; v1 just surfaces the
  // serialized form so the user at least sees the data.
  try {
    return JSON.stringify(value);
  } catch {
    return String(value);
  }
}

/**
 * Project the `PlaybookInferenceResponse` into the `prefilledFields` shape
 * expected by `INSIGHTS_PLAYBOOK_SCHEMA`. The schema's four field paths are:
 *
 *   - `answer`         → outer `response.answer`
 *   - `playbookId`     → outer `response.playbookId`
 *   - `inferenceBody`  → envelope-derived reasoning string
 *   - `evidenceList`   → envelope-derived evidence (or `citations` fallback)
 *
 * Per task 017 evidence §2, the reusability handshake convention is that
 * task 026 (this renderer) flattens the envelope into the four schema fields.
 * The exact envelope keys vary per playbook — we use a forgiving lookup
 * strategy that tolerates both camelCase and PascalCase.
 *
 * Pure function exported for testability.
 */
export function envelopeToFields(
  response: PlaybookInferenceResponse,
): Record<string, string> {
  const envelope = response.structuredResult.envelope;

  // Forgiving key lookup — playbook envelopes vary; tolerate camelCase +
  // PascalCase variants without forcing a single shape.
  const lookup = (...keys: readonly string[]): unknown => {
    for (const key of keys) {
      if (key in envelope) {
        return envelope[key];
      }
    }
    return undefined;
  };

  const inferenceBody = lookup('inferenceBody', 'InferenceBody', 'inference', 'Inference');
  const evidenceList = lookup('evidenceList', 'EvidenceList', 'evidenceRefs', 'EvidenceRefs');

  // If the envelope doesn't carry an explicit evidence list, derive one from
  // the outer `citations` array (uniform shape per brief §4.3 — works for
  // both playbook and RAG paths).
  const evidenceFallback = response.citations.length > 0
    ? response.citations.map(c => `[${c.n}] ${c.source} — ${c.excerpt}`).join('\n')
    : '';

  return {
    answer: response.answer,
    playbookId: response.playbookId,
    inferenceBody: stringifyEnvelopeField(inferenceBody),
    evidenceList: evidenceList !== undefined
      ? stringifyEnvelopeField(evidenceList)
      : evidenceFallback,
  };
}

// ---------------------------------------------------------------------------
// Correlation-id helper
// ---------------------------------------------------------------------------

/**
 * Generate a fresh correlation id. Used when the response's
 * `diagnostics.conversationId` is absent (which is the v1.0 norm). Prefer
 * `crypto.randomUUID` when available; fall back to a pseudo-random id for
 * older test environments.
 *
 * Mirrors `newCorrelationId` in `insightsQueryClient.ts` — kept as a private
 * helper here so the renderer has no runtime dependency on the client.
 */
function generateCorrelationId(): string {
  if (typeof crypto !== 'undefined' && typeof crypto.randomUUID === 'function') {
    return crypto.randomUUID();
  }
  return `insights-${Math.random().toString(36).slice(2, 10)}-${Date.now().toString(36)}`;
}

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

/**
 * Renders a playbook-inference response via task 017's
 * `StructuredOutputStreamWidget`.
 *
 * Per R5 CLAUDE.md §3.1 reuse mandate: we DO NOT build a parallel structured
 * renderer. The widget owns rendering; this component just constructs the
 * `widgetData` payload (mode + schema + prefilledFields + correlationId) and
 * either mounts the widget inline or dispatches `workspace.widget_load` to
 * open it as a Workspace pane tab.
 */
export const PlaybookResponseRenderer: React.FC<PlaybookResponseRendererProps> = ({
  response,
  streamingEnabled = false,
  dispatchToWorkspace = false,
}) => {
  const styles = useStyles();
  const dispatch = useDispatchPaneEvent();

  // Stable correlation id per render cycle. Memoised on the response identity
  // so retries / re-mounts surface as new ids; the widget uses this to filter
  // stream events when multiple structured-output widgets coexist (FR-06).
  const correlationId = React.useMemo(
    () => response.diagnostics.conversationId ?? generateCorrelationId(),
    [response.diagnostics.conversationId],
  );

  const widgetData = React.useMemo<StructuredOutputStreamWidgetData>(
    () => ({
      mode: streamingEnabled ? 'streaming' : 'static',
      schema: INSIGHTS_PLAYBOOK_SCHEMA,
      prefilledFields: streamingEnabled
        ? undefined
        : envelopeToFields(response),
      correlationId,
      title: `Insight · ${response.playbookId}`,
    }),
    [response, streamingEnabled, correlationId],
  );

  // Workspace-tab dispatch path. Fires once per response identity — the
  // `useEffect` dependency includes the response so a new turn yields a new
  // dispatch. We rely on `workspace.widget_load`'s allowMultiple semantics
  // (FR-06; register-structured-output-stream-widget.ts) for tab management.
  React.useEffect(() => {
    if (!dispatchToWorkspace) {
      return;
    }
    dispatch('workspace', {
      type: 'widget_load',
      widgetType: STRUCTURED_OUTPUT_STREAM_WIDGET_TYPE,
      widgetData,
      displayName: `Insight · ${response.playbookId}`,
    });
  }, [dispatch, dispatchToWorkspace, widgetData, response.playbookId]);

  if (dispatchToWorkspace) {
    // The widget mounts in the Workspace pane via the dispatch above; this
    // sub-renderer renders nothing in the chat surface.
    return null;
  }

  return (
    <div
      className={styles.container}
      data-testid="playbook-response-renderer"
      data-render-mode={streamingEnabled ? 'streaming' : 'static'}
    >
      {/*
        REUSE: task 017's StructuredOutputStreamWidget renders the playbook
        envelope via INSIGHTS_PLAYBOOK_SCHEMA. ZERO widget code change required
        per R5 CLAUDE.md §3.1 + task 017 reusability handshake (UR-02 evidence).
      */}
      <StructuredOutputStreamWidget
        data={widgetData}
        widgetType={STRUCTURED_OUTPUT_STREAM_WIDGET_TYPE}
      />
    </div>
  );
};

export default PlaybookResponseRenderer;
