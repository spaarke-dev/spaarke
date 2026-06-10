/**
 * @spaarke/ai-widgets — StructuredOutputStreamWidget
 *
 * Workspace-pane widget that renders structured AI output PROGRESSIVELY via
 * `FieldDelta` SSE events, OR statically from a pre-filled envelope. Created
 * in R5 task 017 (D2-07) as the destination for:
 *
 *   - Summarize streaming output (FR-02): TL;DR-first progressive emission
 *     from `SessionSummarizeOrchestrator` via Azure OpenAI Structured Outputs
 *     token-stream (task 006 spike: ~191 events / ~932 chars; declaration-
 *     order field arrival).
 *   - Insights playbook static rendering (FR-13 / D2-16, task 026): the same
 *     widget renders the Insights playbook envelope via `mode: 'static'` +
 *     `INSIGHTS_PLAYBOOK_SCHEMA` + `prefilledFields` — zero widget code
 *     change required. This dual-purpose reuse is the load-bearing platform-
 *     extensibility claim of R5 (risk UR-02 mitigation).
 *
 * Render contract is SCHEMA-DRIVEN: the widget accepts a list of field
 * descriptors (path + label + displayHint + order) and renders fields in
 * declaration order using Fluent UI v9 primitives. Two concrete schemas
 * (`SUMMARIZE_SCHEMA`, `INSIGHTS_PLAYBOOK_SCHEMA`) are exported as module-level
 * `const`s for cross-consumer reuse — consumers do NOT redeclare them.
 *
 * Four mutually-exclusive render states:
 *
 *   (a) STREAMING            — fields appear progressively; blinking cursor at
 *                              the tail of the most-recently-updated field;
 *                              `Skeleton` placeholders for not-yet-streamed
 *                              fields; "Streaming…" badge in header.
 *   (b) STREAMING-COMPLETE   — final formatted output with no cursor;
 *                              "Complete" indicator in header.
 *   (c) DECLINE              — Insights decline-to-find rendering: `MessageBar`
 *                              with `intent="warning"`, decline reason, and
 *                              `suggestedActions` list. Schema fields hidden.
 *   (d) EMPTY-RESULT         — muted hint "I couldn't find anything for that.
 *                              Try rephrasing or attaching files." per FR-13
 *                              / D2-16 acceptance. Schema fields hidden.
 *
 * Out-of-order delta handling: `sequence` numbers (per-field monotonic) are
 * tracked; stale deltas are dropped and logged via `console.debug`. Content
 * append is strictly per-`path` (no global ordering required).
 *
 * Correlation: each widget instance carries a `correlationId` (from
 * `widgetData.correlationId`); only events whose `streamId` matches this id
 * are consumed. This allows multiple `StructuredOutputStreamWidget` tabs
 * (FR-06: each Summarize invocation = new tab) to coexist without crosstalk.
 *
 * ADR compliance:
 *   - ADR-012: lives in `@spaarke/ai-widgets`; context-agnostic
 *   - ADR-021: Fluent v9 semantic tokens ONLY — no hex / rgba / Fluent v8
 *   - ADR-022: React 19, functional component + hooks only
 *   - ADR-028: NO BFF calls — pure subscriber of PaneEventBus events
 *   - ADR-030: subscribes to additive `workspace.streaming_*` event types added
 *              by task 016; no new channel; unknown event types ignored
 *
 * UR-02 80/20 (per spec):
 *   - v1 handles top-level paths + simple `fieldHighlights.*` list synthetic
 *     pattern for list-display hint.
 *   - DEFERRED to follow-up (documented as TODO in module body):
 *     dynamic-cardinality arrays (e.g. real `fileHighlights[N]` paths with
 *     per-element nested objects); deeply-nested paths
 *     (`parties[0].address.city`); schema versioning + migration; per-field
 *     custom renderers beyond the 5 standard display hints.
 *
 * React 19, NOT PCF-safe.
 */

import * as React from 'react';
import {
  makeStyles,
  mergeClasses,
  tokens,
  Card,
  CardHeader,
  Text,
  Badge,
  MessageBar,
  MessageBarBody,
  Skeleton,
  SkeletonItem,
  Divider,
  Button,
} from '@fluentui/react-components';
import { SparkleRegular, WarningRegular, CheckmarkCircleRegular } from '@fluentui/react-icons';
import type { WorkspaceWidgetProps } from '../../types/widget-types';
import { usePaneEvent } from '../../events/usePaneEvent';
import type { WorkspacePaneEvent } from '../../events/PaneEventTypes';

// ---------------------------------------------------------------------------
// Public schema types
// ---------------------------------------------------------------------------

/**
 * Display-hint discriminant for a schema field.
 *
 * Each hint maps to a specific Fluent v9 primitive in the renderer:
 *
 *   - `'heading'`   — `Text` with `tokens.fontSizeBase500` + semibold weight.
 *                     Use for the most prominent field (e.g. Summarize `tldr`).
 *   - `'paragraph'` — `Text` with `tokens.fontSizeBase300`, wrap, line-height
 *                     base400. Use for narrative fields (`summary`, RAG `answer`).
 *   - `'list'`      — bulleted `<ul>` of `Text` items when content is an array
 *                     OR newline-split string; also drives the synthetic
 *                     `<root>.*` pattern (dynamic-cardinality list — see TODO
 *                     on UR-02 80/20).
 *   - `'badge'`     — `Badge` per token (split on commas / whitespace) for
 *                     keywords / tags / entities.
 *   - `'callout'`   — bordered `Card` block with neutral background; use for
 *                     emphasized one-liners (e.g. cost-prediction summary).
 */
export type StructuredOutputDisplayHint = 'heading' | 'paragraph' | 'list' | 'badge' | 'callout';

// ---------------------------------------------------------------------------
// JSON Schema (action `outputSchema`) — R6 Pillar 5 (tasks 040 + 041)
// ---------------------------------------------------------------------------

/**
 * Minimal subset of JSON Schema draft-07 the widget understands for schema-
 * aware dispatch. Mirrors the shape that R6 Phase B Wave B-G2 (tasks 032 + 033)
 * populated on `sprk_analysisaction.sprk_outputschemajson` for:
 *
 *   - `SUM-CHAT@v1`: `{ tldr: string[], summary: string, keywords: string,
 *     entities: { organizations: string[], persons: string[] } }`
 *
 * The widget reads this schema to dispatch on top-level field type:
 *
 *   - Task 040: `{ type: 'array', items: { type: 'string' } }` →
 *     accumulated content JSON.parse'd → Fluent v9 `<ul><li>...</li></ul>`.
 *     Fixes R5 SC-18 TL;DR bug (Gap C, lessons-learned).
 *   - Task 041: `{ type: 'object', properties: {...} }` → labeled key-value
 *     blocks via `<SchemaAwareObjectRenderer />`; nested arrays REUSE task
 *     040's `<SchemaAwareArrayRenderer />` (no duplicate implementation per
 *     Q5 architectural decision). Fixes R5 SC-18 entities bug (Gap C).
 *     Depth limit: nested object-of-object (depth ≥ 2) is out of Phase B
 *     scope and falls back to compact JSON.stringify with a TODO marker.
 *   - All other types: fall through to the legacy displayHint-based renderer
 *     (backward compatibility for actions without `outputSchema` per NFR-11).
 *
 * The `outputSchema` is OPTIONAL on `widgetData`. When absent the widget
 * behaves as it did pre-R6 (string-only renderer with `displayHint` dispatch).
 *
 * Reuse: this is a generic JSON Schema subset; nothing here is widget-specific.
 * If another consumer needs richer JSON Schema (`oneOf`, `allOf`, `$ref`),
 * extend ADDITIVELY; do not break this contract.
 */
export interface JsonSchemaField {
  /**
   * JSON Schema field type. We narrow to the four shapes the widget renders:
   *   - `'string'`  — fall through to legacy renderer (current behaviour)
   *   - `'array'`   — paired with `items` to dispatch array-of-string (task 040)
   *   - `'object'`  — paired with `properties` to dispatch labeled blocks (task 041)
   *   - `'number'`/`'boolean'` — fall through to legacy renderer
   *
   * Typed loose so unknown variants degrade to the fallback path without
   * crashing the widget (defensive narrowing in the dispatch site).
   */
  type?: 'string' | 'number' | 'boolean' | 'array' | 'object' | string;
  /** Item schema for `type: 'array'`. Task 040 supports `items: { type: 'string' }`. */
  items?: JsonSchemaField;
  /** Nested properties for `type: 'object'`. Task 041 will dispatch on these. */
  properties?: Record<string, JsonSchemaField>;
  /** Human-readable description (carried through for future UI hints). */
  description?: string;
}

/**
 * The action's output-schema declaration, mirrored from
 * `sprk_analysisaction.sprk_outputschemajson`. Top-level is always
 * `{ type: 'object', properties: { ... } }` per R6 Phase B Wave B-G2 contracts.
 */
export interface JsonSchema extends JsonSchemaField {
  type?: 'object';
  properties?: Record<string, JsonSchemaField>;
}

/**
 * Schema-driven field descriptor — the contract this widget renders against.
 *
 * Consumers pass a `StructuredOutputSchema` (which is `fields: StructuredOutputField[]`)
 * via `widgetData.schema`. The widget renders each field in `order` ascending.
 *
 * UR-02 follow-up TODOs (out of R5 Phase 2 80/20 scope):
 *   - Nested JSON paths (e.g. `parties[0].name`). v1 treats `path` as an opaque
 *     top-level key; nested paths render but per-element editing is unsupported.
 *   - Dynamic-cardinality arrays (e.g. `fileHighlights[N]` with growing N).
 *     v1 falls back to a single list renderer keyed by the synthetic prefix
 *     pattern `<root>.*` when `displayHint: 'list'`.
 *   - Schema versioning: add `schemaVersion` to `StructuredOutputSchema` once
 *     two consumers ship with divergent schemas.
 */
export interface StructuredOutputField {
  /**
   * JSON path identifying the field within the streamed envelope.
   * MUST match the `fieldPath` value carried on incoming `workspace.field_delta`
   * events (R5 task 016 / D2-06 — additive event types).
   *
   * v1 supports top-level keys (e.g. `"tldr"`, `"summary"`) + a single
   * synthetic list pattern: a path ending in `.*` (e.g. `"keywords.*"`)
   * treats incoming deltas with the same prefix as list items. See widget
   * body for implementation; nested paths are TODO-deferred (UR-02).
   */
  path: string;
  /**
   * Human-readable label rendered above the field's content.
   * Empty string suppresses the label (useful when the field IS the heading).
   */
  label: string;
  /** Display-hint discriminant — drives Fluent v9 primitive selection. */
  displayHint: StructuredOutputDisplayHint;
  /**
   * Render order (ascending). Per task 006 spike, Azure OpenAI emits JSON
   * properties in DECLARATION order — schema order MUST match the BFF
   * action's output-schema property order so first-arrived field is also
   * the first-rendered field.
   */
  order: number;
}

/** Schema declaration consumed by the widget. */
export interface StructuredOutputSchema {
  /** Render plan for the structured envelope (declaration order = render order). */
  fields: StructuredOutputField[];
}

// ---------------------------------------------------------------------------
// Public widget-data type — discriminated by `mode`
// ---------------------------------------------------------------------------

/**
 * The widget's input payload (carried in `widgetData` of the `widget_load`
 * `WorkspaceWidgetLoadEvent` that mounts this widget).
 *
 * Two top-level modes:
 *
 *   - `'streaming'` — the widget subscribes to PaneEventBus
 *     `workspace.streaming_started / field_delta / streaming_complete` events
 *     matching `correlationId` and renders progressively. Used by Summarize
 *     (R5 task 020 dispatcher) and by Insights playbook streaming in Phase 2+.
 *   - `'static'` — the widget renders `prefilledFields` directly with no
 *     subscription. Used by Insights playbook static rendering (task 026 /
 *     D2-16) and by Insights RAG decline-to-find rendering when
 *     `declineState`/`emptyResultState` are set.
 *
 * Override states (`declineState`, `emptyResultState`) are mutually exclusive
 * with normal rendering and take precedence over `mode` — if either is set,
 * schema fields are hidden and the override state is rendered.
 */
export interface StructuredOutputStreamWidgetData {
  /**
   * `'streaming'` — subscribe to PaneEventBus field deltas (Summarize path,
   * future playbook-streaming path).
   * `'static'`    — render `prefilledFields` directly (Insights playbook
   * static envelope; task 026).
   */
  mode: 'streaming' | 'static';
  /**
   * Schema declaration (field plan). Required for both modes. For static
   * mode, fields without entries in `prefilledFields` render empty (no
   * Skeleton — static mode does not anticipate future data).
   */
  schema: StructuredOutputSchema;
  /**
   * Pre-rendered field content for `mode: 'static'`. Keyed by
   * `StructuredOutputField.path`. Ignored when `mode === 'streaming'`.
   */
  prefilledFields?: Record<string, string>;
  /**
   * Correlation identifier — when present, only PaneEventBus events whose
   * `streamId` matches are consumed. Required for `mode: 'streaming'` when
   * multiple stream widgets may coexist (FR-06). Optional in `mode: 'static'`.
   */
  correlationId?: string;
  /**
   * Decline-to-find render override (mutually exclusive with normal field
   * rendering). When set, the widget renders a `MessageBar intent="warning"`
   * with the reason and optional suggested actions. Used by Insights
   * decline-path (task 026) and by Summarize when the orchestrator declines.
   */
  declineState?: {
    /** Plain-text reason surfaced to the user (no stack traces, no raw LLM output). */
    reason: string;
    /**
     * Optional list of next-step suggestions rendered as Fluent `Button`s
     * below the message bar. Phase 1.5: strings only; Phase 2 may add typed
     * action verbs (see brief §6 question 3).
     */
    suggestedActions?: string[];
    /**
     * Optional callback invoked when the user clicks a suggested action.
     * v1 is hint-only: callers may attach a dispatch to PaneEventBus from
     * the calling pane if action follow-through is required. Defaults to
     * no-op (clicking the button is a no-op visually beyond hover state).
     */
    onActionClick?: (action: string) => void;
  };
  /**
   * Empty-result render override (mutually exclusive with normal field
   * rendering and `declineState`). Used by Insights RAG path when retrieval
   * returns zero hits (`citations: []` + `answer: ""`) per brief §4.4.
   */
  emptyResultState?: boolean;
  /**
   * Optional custom title shown in the card header. When absent, defaults to
   * "AI Output" + the active state badge.
   */
  title?: string;
  /**
   * Action `outputSchema` (R6 Pillar 5; populated by Phase B Wave B-G2 tasks
   * 032 + 033 on `sprk_analysisaction.sprk_outputschemajson`). When present,
   * the widget reads each top-level field's declared JSON Schema type and
   * dispatches:
   *
   *   - `array` of `string` → Fluent v9 `<ul><li>...</li></ul>` (task 040 — THIS)
   *   - `object` → labeled key-value blocks (task 041 — NEXT; same file)
   *   - everything else → legacy `displayHint`-based renderer (back-compat)
   *
   * When ABSENT (legacy actions not yet migrated), the widget falls back to
   * today's `displayHint` rendering with zero regression — this is the binding
   * NFR-11 backward-compatibility contract.
   *
   * Schema-aware parsing happens on `streaming_complete` for the streaming
   * path (mid-stream tokens cannot parse). For `mode: 'static'` the parse
   * happens immediately at render time.
   *
   * Malformed JSON in the accumulated content surfaces an inline error state
   * on the field; the widget does NOT crash and other fields render normally.
   */
  outputSchema?: JsonSchema;
}

// ---------------------------------------------------------------------------
// Module-level schema exports — reuse contract for downstream consumers
// ---------------------------------------------------------------------------

/**
 * Summarize playbook output schema (FR-02; task 010 / D1-10 deployed action
 * SUM-CHAT@v1). Declaration order matches the action's JSON-schema output
 * declaration order — per task 006 spike, this is the order Azure OpenAI
 * streams field tokens, so this is the order the UI fills in too.
 *
 * `tldr` FIRST is the binding UX requirement of FR-02 ("TL;DR populates
 * first") — do NOT reorder without a coordinated change to the BFF action's
 * output-schema property order.
 *
 * Consumed by:
 *   - Summarize chat-pane dispatcher (task 020 / D2-11)
 *   - Any future "summarize-style" action that adopts the same envelope
 */
export const SUMMARIZE_SCHEMA: StructuredOutputSchema = {
  fields: [
    {
      path: 'tldr',
      label: 'TL;DR',
      displayHint: 'heading',
      order: 10,
    },
    {
      path: 'summary',
      label: 'Summary',
      displayHint: 'paragraph',
      order: 20,
    },
    {
      path: 'keywords',
      label: 'Keywords',
      displayHint: 'badge',
      order: 30,
    },
    {
      path: 'entities',
      label: 'Entities',
      displayHint: 'list',
      order: 40,
    },
  ],
};

/**
 * Insights playbook output schema (FR-13 / brief §4.1 playbook path).
 *
 * Renders the `InsightArtifact` envelope returned by the Insights
 * `/api/insights/assistant/query` endpoint when `path === 'playbook'`:
 *
 *   - `answer`         — plain-text summary (heading-sized in UI)
 *   - `playbookId`     — small badge ("predict-matter-cost@v1") for telemetry
 *   - `inferenceBody`  — full inference paragraph (envelope-derived prose)
 *   - `evidenceList`   — `EvidenceRefs[]` rendered as a list (each ref's source)
 *
 * This schema is the REUSE PROOF POINT for R5 risk UR-02: task 026 (D2-16)
 * imports this constant and passes it via `mode: 'static'` + `prefilledFields`
 * — zero widget code change required.
 *
 * Phase 2 deferral (UR-02 TODO): a richer envelope schema may include nested
 * objects (e.g. `comparableMatters[].caseId`); v1 renders flat top-level
 * paths only. Decline rendering for the playbook path uses `declineState` on
 * `widgetData`, not a schema field.
 */
export const INSIGHTS_PLAYBOOK_SCHEMA: StructuredOutputSchema = {
  fields: [
    {
      path: 'answer',
      label: 'Insight',
      displayHint: 'heading',
      order: 10,
    },
    {
      path: 'playbookId',
      label: 'Playbook',
      displayHint: 'badge',
      order: 20,
    },
    {
      path: 'inferenceBody',
      label: 'Reasoning',
      displayHint: 'paragraph',
      order: 30,
    },
    {
      path: 'evidenceList',
      label: 'Evidence',
      displayHint: 'list',
      order: 40,
    },
  ],
};

/**
 * SUM-CHAT@v1 action `outputSchema` mirror — exported so dispatchers (e.g.
 * `dispatchSummarizeOnly` in `FilePreviewContextWidget.tsx`) can attach the
 * schema-aware classifier contract to the widget payload WITHOUT redeclaring
 * the shape at the call site.
 *
 * R6 Hotfix Wave B-G9a (2026-06-10): production walkthrough showed `tldr`
 * (declared `array of string`) rendering as a bold paragraph and `entities`
 * (declared `object`) rendering as comma-split bullets — i.e. the legacy
 * `displayHint` path was running because `outputSchema` was ABSENT on
 * `widgetData`. Tasks 040 + 041 added the schema-aware dispatch but
 * `dispatchSummarizeOnly` did not pass `outputSchema`, so `classifySchemaField()`
 * returned `'legacy'` for every field. The unit tests passed because they
 * always set `outputSchema`. This constant + the dispatcher update closes the
 * data-flow gap.
 *
 * SHAPE: mirrors `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/Playbooks/
 * summarize-document-for-chat.playbook.json` `actions[0].outputSchema`
 * (Phase B Wave B-G2 task 032). The widget reads each top-level field's
 * declared JSON Schema type and dispatches:
 *
 *   - `tldr`     → `array` of `string` → `<SchemaAwareArrayRenderer />` (bulleted list)
 *   - `summary`  → `string`            → legacy `displayHint: 'paragraph'` path
 *   - `keywords` → `string`            → legacy `displayHint: 'badge'` path
 *   - `entities` → `object` w/ props   → `<SchemaAwareObjectRenderer />` (labeled blocks)
 *
 * Reuse: if a sibling action declares the SAME output envelope shape, it MAY
 * reuse this constant verbatim. If it declares a divergent shape, mirror the
 * new shape in a sibling constant — do NOT mutate this constant.
 */
export const SUM_CHAT_OUTPUT_SCHEMA: JsonSchema = {
  type: 'object',
  properties: {
    tldr: { type: 'array', items: { type: 'string' } },
    summary: { type: 'string' },
    keywords: { type: 'string' },
    entities: {
      type: 'object',
      properties: {
        organizations: { type: 'array', items: { type: 'string' } },
        persons: { type: 'array', items: { type: 'string' } },
      },
    },
  },
};

// ---------------------------------------------------------------------------
// Internal reducer — append-only progressive rendering by JSON path
// ---------------------------------------------------------------------------

/**
 * Per-path render state — `content` is the accumulated string, `lastSequence`
 * is the highest `sequence` we've seen for this path (used for out-of-order
 * detection).
 */
interface FieldState {
  content: string;
  lastSequence: number;
}

/** Phase machine for the streaming state. */
type StreamPhase = 'idle' | 'streaming' | 'complete';

interface StreamReducerState {
  phase: StreamPhase;
  /**
   * Path → accumulated content. Map preserves insertion order (= delta-
   * arrival order), used to identify the most-recently-updated path for
   * cursor positioning.
   */
  fields: Map<string, FieldState>;
  /**
   * JSON path of the field that most recently received a delta. The cursor
   * animation renders at the tail of this field while `phase === 'streaming'`.
   * Cleared on `streaming_complete`.
   */
  mostRecentPath: string | null;
}

type StreamReducerAction =
  | { type: 'streaming_started' }
  | { type: 'field_delta'; path: string; content: string; sequence: number }
  | { type: 'streaming_complete' }
  | { type: 'reset' };

const INITIAL_REDUCER_STATE: StreamReducerState = {
  phase: 'idle',
  fields: new Map(),
  mostRecentPath: null,
};

function streamReducer(state: StreamReducerState, action: StreamReducerAction): StreamReducerState {
  switch (action.type) {
    case 'streaming_started':
      // Fresh start. Clear any prior content from a previous run.
      return {
        phase: 'streaming',
        fields: new Map(),
        mostRecentPath: null,
      };

    case 'field_delta': {
      const { path, content, sequence } = action;
      const prior = state.fields.get(path);

      // Out-of-order detection: drop stale deltas (sequence less than or
      // equal to the highest seen) and log for telemetry. Equal sequence is
      // also dropped — duplicate deltas should never apply twice.
      if (prior !== undefined && sequence <= prior.lastSequence) {
        // eslint-disable-next-line no-console
        console.debug(
          `[StructuredOutputStreamWidget] dropped stale delta path="${path}" sequence=${sequence} lastSequence=${prior.lastSequence}`
        );
        return state;
      }

      const nextFields = new Map(state.fields);
      nextFields.set(path, {
        content: (prior?.content ?? '') + content,
        lastSequence: sequence,
      });
      return {
        // First delta also flips phase to streaming if a stream began without
        // an explicit `streaming_started` (defensive — should not happen, but
        // makes the widget robust to a missing prelude).
        phase: state.phase === 'idle' ? 'streaming' : state.phase,
        fields: nextFields,
        mostRecentPath: path,
      };
    }

    case 'streaming_complete':
      return {
        phase: 'complete',
        fields: state.fields,
        mostRecentPath: null,
      };

    case 'reset':
      return INITIAL_REDUCER_STATE;

    default:
      return state;
  }
}

// ---------------------------------------------------------------------------
// Styles — Fluent v9 semantic tokens only (ADR-021)
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  root: {
    display: 'flex',
    flexDirection: 'column',
    height: '100%',
    gap: tokens.spacingVerticalM,
    padding: tokens.spacingHorizontalM,
    backgroundColor: tokens.colorNeutralBackground1,
    overflowY: 'auto',
    minHeight: 0,
  },
  card: {
    flex: 1,
    display: 'flex',
    flexDirection: 'column',
    minHeight: 0,
  },
  headerIcon: {
    color: tokens.colorBrandForeground1,
    fontSize: tokens.fontSizeBase600,
    flexShrink: 0,
  },
  headerTitle: {
    fontWeight: tokens.fontWeightSemibold,
    color: tokens.colorNeutralForeground1,
    overflow: 'hidden',
    textOverflow: 'ellipsis',
    whiteSpace: 'nowrap',
  },
  headerBadgeRow: {
    display: 'flex',
    flexDirection: 'row',
    alignItems: 'center',
    gap: tokens.spacingHorizontalS,
  },
  fieldsContainer: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalL,
    padding: tokens.spacingHorizontalM,
  },
  fieldBlock: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalXS,
    // Reserve a minimum vertical slot so the layout does not jitter when
    // content arrives. Skeleton placeholders fill this space while streaming.
    minHeight: '40px',
  },
  fieldLabel: {
    fontSize: tokens.fontSizeBase200,
    color: tokens.colorNeutralForeground3,
    fontWeight: tokens.fontWeightSemibold,
    textTransform: 'uppercase',
    letterSpacing: tokens.strokeWidthThin,
  },
  fieldHeading: {
    fontSize: tokens.fontSizeBase500,
    fontWeight: tokens.fontWeightSemibold,
    color: tokens.colorNeutralForeground1,
    lineHeight: tokens.lineHeightBase500,
    margin: 0,
  },
  fieldParagraph: {
    fontSize: tokens.fontSizeBase300,
    color: tokens.colorNeutralForeground1,
    lineHeight: tokens.lineHeightBase400,
    margin: 0,
    whiteSpace: 'pre-wrap',
    wordBreak: 'break-word',
  },
  fieldCallout: {
    fontSize: tokens.fontSizeBase300,
    color: tokens.colorNeutralForeground1,
    lineHeight: tokens.lineHeightBase400,
    padding: tokens.spacingHorizontalM,
    backgroundColor: tokens.colorNeutralBackground2,
    border: `1px solid ${tokens.colorNeutralStroke2}`,
    borderRadius: tokens.borderRadiusMedium,
    margin: 0,
    whiteSpace: 'pre-wrap',
    wordBreak: 'break-word',
  },
  badgeRow: {
    display: 'flex',
    flexDirection: 'row',
    flexWrap: 'wrap',
    gap: tokens.spacingHorizontalS,
  },
  list: {
    margin: 0,
    paddingLeft: tokens.spacingHorizontalXL,
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalXS,
    color: tokens.colorNeutralForeground1,
  },
  listItem: {
    fontSize: tokens.fontSizeBase300,
    lineHeight: tokens.lineHeightBase400,
    color: tokens.colorNeutralForeground1,
  },
  // Cursor animation — Fluent brand foreground token, opacity pulse via CSS keyframes.
  // Renders as an inline `▋` glyph at the tail of the most-recently-updated field.
  // Removed on `streaming_complete`.
  cursor: {
    display: 'inline-block',
    marginLeft: '2px',
    color: tokens.colorBrandForeground1,
    fontWeight: tokens.fontWeightBold,
    animationDuration: '900ms',
    animationIterationCount: 'infinite',
    animationName: {
      '0%': { opacity: 1 },
      '50%': { opacity: 0 },
      '100%': { opacity: 1 },
    },
  },
  skeletonPlaceholder: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalS,
    paddingTop: tokens.spacingVerticalXS,
  },
  declineContainer: {
    padding: tokens.spacingHorizontalM,
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalM,
  },
  suggestedActionRow: {
    display: 'flex',
    flexDirection: 'row',
    flexWrap: 'wrap',
    gap: tokens.spacingHorizontalS,
  },
  emptyResultContainer: {
    flex: 1,
    display: 'flex',
    flexDirection: 'column',
    alignItems: 'center',
    justifyContent: 'center',
    gap: tokens.spacingVerticalS,
    padding: tokens.spacingHorizontalXXL,
    textAlign: 'center',
  },
  emptyResultText: {
    color: tokens.colorNeutralForeground3,
    fontSize: tokens.fontSizeBase300,
  },
  errorText: {
    color: tokens.colorPaletteRedForeground1,
    fontSize: tokens.fontSizeBase300,
  },
  // R6 task 041 — schema-aware object renderer (labeled key-value blocks).
  // Top-level container: vertical stack of nested labeled rows, each row
  // containing a small uppercase label and the rendered value below.
  schemaObjectContainer: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalM,
  },
  schemaObjectRow: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalXXS,
  },
  schemaObjectKey: {
    // Reuses the same look as `fieldLabel` (small, uppercase, neutral 3) so the
    // nested block visually nests under the top-level field label without
    // competing for visual weight.
    fontSize: tokens.fontSizeBase200,
    color: tokens.colorNeutralForeground3,
    fontWeight: tokens.fontWeightSemibold,
    textTransform: 'capitalize',
  },
  schemaObjectValueText: {
    fontSize: tokens.fontSizeBase300,
    color: tokens.colorNeutralForeground1,
    lineHeight: tokens.lineHeightBase400,
    margin: 0,
    whiteSpace: 'pre-wrap',
    wordBreak: 'break-word',
  },
  // Empty-state hint shown when a nested array property parses to `[]`.
  schemaObjectEmptyHint: {
    fontSize: tokens.fontSizeBase200,
    color: tokens.colorNeutralForeground3,
    fontStyle: 'italic',
  },
  // Compact-JSON fallback for depth-≥-2 nested objects (Phase B constraint).
  // Renders as a monospaced single-line block with a discreet TODO marker so
  // future phases can locate the deferred case.
  schemaObjectDeepFallback: {
    fontSize: tokens.fontSizeBase200,
    color: tokens.colorNeutralForeground2,
    fontFamily: tokens.fontFamilyMonospace,
    backgroundColor: tokens.colorNeutralBackground2,
    border: `1px solid ${tokens.colorNeutralStroke2}`,
    borderRadius: tokens.borderRadiusSmall,
    padding: tokens.spacingHorizontalS,
    whiteSpace: 'pre-wrap',
    wordBreak: 'break-all',
  },
});

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

/**
 * Defensive narrowing of the incoming `data` payload. Subscribers may pass
 * `unknown` through the `widget_load` event boundary; we validate before use.
 */
function isStreamWidgetData(value: unknown): value is StructuredOutputStreamWidgetData {
  if (value === null || typeof value !== 'object') return false;
  const obj = value as Record<string, unknown>;
  if (obj.mode !== 'streaming' && obj.mode !== 'static') return false;
  if (typeof obj.schema !== 'object' || obj.schema === null) return false;
  const schema = obj.schema as Record<string, unknown>;
  if (!Array.isArray(schema.fields)) return false;
  // outputSchema is OPTIONAL (back-compat per NFR-11). When present it must be
  // an object; further shape validation happens lazily at dispatch time so a
  // partially-malformed schema degrades to legacy rendering, not a crash.
  if (obj.outputSchema !== undefined && (typeof obj.outputSchema !== 'object' || obj.outputSchema === null)) {
    return false;
  }
  return true;
}

/**
 * Schema-aware dispatch result for a single top-level field.
 *
 * - `'array-of-string'` — outputSchema declares `{ type: 'array', items: { type: 'string' } }`;
 *   widget renders as bulleted `<ul>` (task 040). Final-state only — mid-stream
 *   tokens are not parseable JSON so they continue to render via the legacy
 *   skeleton/cursor path until `streaming_complete`.
 * - `'object'` — outputSchema declares `{ type: 'object', properties: { ... } }`;
 *   widget renders as labeled key-value blocks via `<SchemaAwareObjectRenderer />`
 *   (task 041, fixes R5 SC-18 entities bug). Nested arrays REUSE task 040's path.
 * - `'legacy'` — no outputSchema, or field type doesn't match a schema-aware
 *   case; widget renders via the existing `displayHint` path.
 *
 * Pure function — no side effects, safe to call per-render.
 */
function classifySchemaField(
  outputSchema: JsonSchema | undefined,
  fieldPath: string
): 'array-of-string' | 'object' | 'legacy' {
  if (outputSchema === undefined) return 'legacy';
  const properties = outputSchema.properties;
  if (properties === undefined || typeof properties !== 'object') return 'legacy';
  const fieldSchema = properties[fieldPath];
  if (fieldSchema === undefined || fieldSchema === null) return 'legacy';

  // Array-of-string dispatch (task 040 — THIS task).
  if (fieldSchema.type === 'array' && fieldSchema.items?.type === 'string') {
    return 'array-of-string';
  }

  // Object dispatch (R6 task 041 — fixes R5 SC-18 entities bug, Gap C).
  // When the outputSchema declares a field as `{ type: 'object', properties: {...} }`,
  // we parse the accumulated content as JSON and render it as labeled
  // key-value blocks (one per nested property), recursing into `renderValue`
  // for each nested value (strings → text; arrays-of-strings → bulleted list
  // REUSING the task 040 SchemaAwareArrayRenderer code path; deeper nested
  // objects → compact JSON fallback per the Phase B depth-≥-2 constraint).
  if (fieldSchema.type === 'object' && fieldSchema.properties) {
    return 'object';
  }

  return 'legacy';
}

/**
 * Attempt to JSON-parse accumulated streaming content as a string[] for a
 * field whose outputSchema declares `{ type: 'array', items: { type: 'string' } }`.
 *
 * Returns the parsed array on success. Returns `{ error: string }` on:
 *   - JSON.parse throwing (malformed JSON — common mid-stream, EXPECTED until
 *     `streaming_complete` fires)
 *   - parse result NOT being an array
 *   - parse result array containing non-string items
 *
 * Callers MUST treat parse errors during `phase: 'streaming'` as expected
 * (the content is incomplete) and DEFER schema-aware rendering until
 * `phase: 'complete'`. Only post-complete parse failures surface as user-
 * visible errors.
 */
function parseArrayOfString(content: string): { items: string[] } | { error: string } {
  const trimmed = content.trim();
  if (trimmed.length === 0) return { items: [] };
  try {
    const parsed = JSON.parse(trimmed) as unknown;
    if (!Array.isArray(parsed)) {
      return { error: 'Expected JSON array; received non-array value' };
    }
    const items: string[] = [];
    for (const v of parsed) {
      if (typeof v !== 'string') {
        return { error: 'Array contains non-string item' };
      }
      items.push(v);
    }
    return { items };
  } catch (e) {
    const message = e instanceof Error ? e.message : String(e);
    return { error: `Malformed JSON: ${message}` };
  }
}

/**
 * Split a list-shaped field's accumulated text into items. v1 supports two
 * shapes per UR-02 80/20:
 *   - JSON array literal (when the stream produces a `[...]` envelope and the
 *     final accumulated content parses as a string array)
 *   - Newline-separated string (when the stream produces a list as text)
 *
 * Falls back to `[content]` (single-item list) when neither shape applies.
 */
function splitListContent(content: string): string[] {
  const trimmed = content.trim();
  if (trimmed.length === 0) return [];

  // Try JSON-array parse first (common from Structured Outputs streaming).
  if (trimmed.startsWith('[') && trimmed.endsWith(']')) {
    try {
      const parsed = JSON.parse(trimmed) as unknown;
      if (Array.isArray(parsed)) {
        return parsed.filter((v): v is string => typeof v === 'string');
      }
    } catch {
      // Intermediate JSON state is invalid (per task 006 spike). Fall through
      // to newline split — the parsed JSON path applies on the final state.
    }
  }

  // Newline split (Insights playbook static prefill convention).
  if (trimmed.includes('\n')) {
    return trimmed
      .split(/\r?\n/)
      .map(line => line.replace(/^[-*•]\s*/, '').trim())
      .filter(line => line.length > 0);
  }

  // Comma-separated fallback for short fields.
  if (trimmed.includes(',')) {
    return trimmed
      .split(',')
      .map(s => s.trim())
      .filter(s => s.length > 0);
  }

  return [trimmed];
}

/**
 * Split a badge-shaped field's content into individual badge tokens.
 * v1 splits on commas first, then whitespace as a fallback for partial
 * streamed content. Empty entries are dropped.
 */
function splitBadgeContent(content: string): string[] {
  const trimmed = content.trim();
  if (trimmed.length === 0) return [];

  // JSON array variant (Structured Outputs final shape for `keywords`).
  if (trimmed.startsWith('[') && trimmed.endsWith(']')) {
    try {
      const parsed = JSON.parse(trimmed) as unknown;
      if (Array.isArray(parsed)) {
        return parsed.filter((v): v is string => typeof v === 'string');
      }
    } catch {
      // Intermediate state — fall through.
    }
  }

  if (trimmed.includes(',')) {
    return trimmed
      .split(',')
      .map(s => s.replace(/["[\]]/g, '').trim())
      .filter(s => s.length > 0);
  }

  return [trimmed];
}

// ---------------------------------------------------------------------------
// Sub-renderers — one per displayHint
// ---------------------------------------------------------------------------

interface FieldRendererProps {
  field: StructuredOutputField;
  content: string;
  showCursor: boolean;
  styles: ReturnType<typeof useStyles>;
}

const HeadingRenderer: React.FC<FieldRendererProps> = ({ content, showCursor, styles }) => (
  <h2 className={styles.fieldHeading} data-display-hint="heading">
    {content}
    {showCursor && (
      <span className={styles.cursor} aria-hidden="true">
        ▋
      </span>
    )}
  </h2>
);

const ParagraphRenderer: React.FC<FieldRendererProps> = ({ content, showCursor, styles }) => (
  <p className={styles.fieldParagraph} data-display-hint="paragraph">
    {content}
    {showCursor && (
      <span className={styles.cursor} aria-hidden="true">
        ▋
      </span>
    )}
  </p>
);

const CalloutRenderer: React.FC<FieldRendererProps> = ({ content, showCursor, styles }) => (
  <div className={styles.fieldCallout} data-display-hint="callout">
    {content}
    {showCursor && (
      <span className={styles.cursor} aria-hidden="true">
        ▋
      </span>
    )}
  </div>
);

const BadgeRenderer: React.FC<FieldRendererProps> = ({ content, showCursor, styles }) => {
  const tokens_ = splitBadgeContent(content);
  return (
    <div className={styles.badgeRow} data-display-hint="badge">
      {tokens_.map((tok, i) => (
        <Badge key={`${tok}-${i}`} appearance="tint" size="medium">
          {tok}
        </Badge>
      ))}
      {showCursor && (
        <span className={styles.cursor} aria-hidden="true">
          ▋
        </span>
      )}
    </div>
  );
};

const ListRenderer: React.FC<FieldRendererProps> = ({ content, showCursor, styles }) => {
  const items = splitListContent(content);
  return (
    <ul className={styles.list} data-display-hint="list">
      {items.map((item, i) => (
        <li key={`${item}-${i}`} className={styles.listItem}>
          {item}
          {showCursor && i === items.length - 1 && (
            <span className={styles.cursor} aria-hidden="true">
              ▋
            </span>
          )}
        </li>
      ))}
      {items.length === 0 && showCursor && (
        <span className={styles.cursor} aria-hidden="true">
          ▋
        </span>
      )}
    </ul>
  );
};

/** Skeleton placeholder rendered for a field that has no content yet during streaming. */
const FieldSkeleton: React.FC<{ styles: ReturnType<typeof useStyles>; displayHint: StructuredOutputDisplayHint }> = ({
  styles,
  displayHint,
}) => {
  // Different skeleton shapes for different hints so the placeholder hints at
  // the final shape and prevents layout jitter when content arrives.
  if (displayHint === 'badge') {
    return (
      <div className={styles.badgeRow}>
        <Skeleton>
          <SkeletonItem shape="rectangle" size={20} style={{ width: '64px' }} />
        </Skeleton>
        <Skeleton>
          <SkeletonItem shape="rectangle" size={20} style={{ width: '80px' }} />
        </Skeleton>
        <Skeleton>
          <SkeletonItem shape="rectangle" size={20} style={{ width: '56px' }} />
        </Skeleton>
      </div>
    );
  }
  if (displayHint === 'list') {
    return (
      <div className={styles.skeletonPlaceholder}>
        <Skeleton>
          <SkeletonItem size={12} style={{ width: '80%' }} />
        </Skeleton>
        <Skeleton>
          <SkeletonItem size={12} style={{ width: '65%' }} />
        </Skeleton>
        <Skeleton>
          <SkeletonItem size={12} style={{ width: '72%' }} />
        </Skeleton>
      </div>
    );
  }
  // heading / paragraph / callout — wide multi-line skeleton.
  return (
    <div className={styles.skeletonPlaceholder}>
      <Skeleton>
        <SkeletonItem size={displayHint === 'heading' ? 16 : 12} style={{ width: '85%' }} />
      </Skeleton>
      <Skeleton>
        <SkeletonItem size={12} style={{ width: '70%' }} />
      </Skeleton>
    </div>
  );
};

/**
 * Schema-aware bulleted-list renderer for `array: string` fields (R6 task 040).
 *
 * Renders the parsed `items` array as a Fluent v9 `<ul><li>...</li></ul>` styled
 * via `tokens.colorNeutralForeground1` + `tokens.spacingHorizontalXL` (consistent
 * with the legacy `ListRenderer`). On parse failure, renders an inline error
 * surface that does NOT crash the widget or leak across other fields.
 *
 * Per ADR-021: zero hard-coded colors; all styling via Fluent v9 semantic
 * tokens (reuses `styles.list`, `styles.listItem`, `styles.errorText`).
 */
interface SchemaAwareArrayRendererProps {
  styles: ReturnType<typeof useStyles>;
  items: string[] | null;
  errorMessage: string | null;
  fieldPath: string;
}

const SchemaAwareArrayRenderer: React.FC<SchemaAwareArrayRendererProps> = ({
  styles,
  items,
  errorMessage,
  fieldPath,
}) => {
  if (errorMessage !== null) {
    return (
      <div data-display-hint="schema-array-error" data-field-path={fieldPath}>
        <Text className={styles.errorText}>{errorMessage}</Text>
      </div>
    );
  }
  if (items === null || items.length === 0) {
    // Empty array — render an empty <ul> so structure is consistent (assertive
    // for tests / accessibility tools) but no <li> children appear.
    return <ul className={styles.list} data-display-hint="schema-array" data-field-path={fieldPath} data-empty="true" />;
  }
  return (
    <ul className={styles.list} data-display-hint="schema-array" data-field-path={fieldPath}>
      {items.map((item, i) => (
        <li key={`${item}-${i}`} className={styles.listItem}>
          {item}
        </li>
      ))}
    </ul>
  );
};

/**
 * Attempt to JSON-parse accumulated streaming content as an object for a field
 * whose outputSchema declares `{ type: 'object', properties: {...} }` (R6 task 041).
 *
 * Returns the parsed object on success. Returns `{ error: string }` on:
 *   - JSON.parse throwing (malformed JSON — common mid-stream, EXPECTED until
 *     `streaming_complete` fires; callers gate on phase per the same contract
 *     as `parseArrayOfString`)
 *   - parse result NOT being a plain object (e.g., null, array, primitive)
 *
 * Symmetry with `parseArrayOfString` is deliberate: same discriminated-union
 * shape, same defensive try/catch, same caller responsibilities. Treat parse
 * errors during `phase: 'streaming'` as expected (content incomplete) and
 * DEFER schema-aware rendering until `phase: 'complete'`.
 */
function parseObject(content: string): { value: Record<string, unknown> } | { error: string } {
  const trimmed = content.trim();
  if (trimmed.length === 0) {
    // Empty input is treated as an empty object (renderer shows empty labeled
    // blocks per schema declaration) — consistent with parseArrayOfString
    // returning `{ items: [] }` for empty input.
    return { value: {} };
  }
  try {
    const parsed = JSON.parse(trimmed) as unknown;
    if (parsed === null || typeof parsed !== 'object' || Array.isArray(parsed)) {
      return { error: 'Expected JSON object; received non-object value' };
    }
    return { value: parsed as Record<string, unknown> };
  } catch (e) {
    const message = e instanceof Error ? e.message : String(e);
    return { error: `Malformed JSON: ${message}` };
  }
}

/**
 * Humanize a camelCase / snake_case JSON property key for display as a label.
 *
 *   - `organizations` → `Organizations`
 *   - `firstName`     → `First Name`
 *   - `case_id`       → `Case Id`
 *
 * v1 deliberately simple — locale-aware tokenization deferred to future phases
 * if international schemas land. Keeps the renderer's UX consistent without
 * introducing an i18n dependency.
 */
function prettyName(key: string): string {
  if (key.length === 0) return key;
  // Split on camelCase boundaries first, then on underscores / hyphens.
  const spaced = key
    .replace(/([a-z0-9])([A-Z])/g, '$1 $2')
    .replace(/[_-]+/g, ' ')
    .trim();
  // Capitalize the first letter of each word.
  return spaced
    .split(/\s+/)
    .filter(w => w.length > 0)
    .map(w => w.charAt(0).toUpperCase() + w.slice(1))
    .join(' ');
}

/**
 * Schema-aware object renderer for `{ type: 'object', properties: {...} }`
 * fields (R6 task 041 — fixes R5 SC-18 entities bug, Gap C in lessons-learned).
 *
 * Renders the parsed object as a vertical stack of LABELED key-value blocks:
 * one row per nested property declared on the schema, with the property name
 * humanized via `prettyName()` and the value rendered recursively via
 * `renderObjectValue()` — which dispatches to:
 *
 *   - `array` of `string` → REUSES `<SchemaAwareArrayRenderer />` (task 040 path,
 *     no duplicate implementation per Q5 architectural decision)
 *   - `string` → plain text via `tokens.colorNeutralForeground1`
 *   - `object` (depth ≥ 2) → compact JSON.stringify fallback per the Phase B
 *     depth-limit constraint (out of scope for Phase B; documented inline for
 *     future-phase pickup)
 *   - any other shape → compact JSON fallback (defensive)
 *
 * Iteration order matches the schema's `properties` declaration order — JSON
 * Schema does not formally order keys, but the action contracts (`SUM-CHAT@v1`,
 * etc.) declare properties in a deliberate UI-presentation order and we honor
 * it. Properties present in the parsed value but absent from the schema are
 * IGNORED (out-of-band data does not appear in the labeled blocks); properties
 * present in the schema but absent from the parsed value render as an empty
 * em-dash placeholder so the layout remains stable.
 *
 * Errors: a single inline error surface on parse failure (mirrors
 * `<SchemaAwareArrayRenderer />`); the widget does NOT crash and sibling fields
 * continue to render normally.
 *
 * Per ADR-021: zero hard-coded colors; all styling via Fluent v9 semantic
 * tokens (`schemaObjectKey`, `schemaObjectValueText`, `schemaObjectDeepFallback`,
 * etc.).
 */
interface SchemaAwareObjectRendererProps {
  styles: ReturnType<typeof useStyles>;
  value: Record<string, unknown> | null;
  errorMessage: string | null;
  fieldPath: string;
  fieldSchema: JsonSchemaField;
}

/**
 * Render a single nested value within a `SchemaAwareObjectRenderer` row.
 *
 * Depth is tracked by the caller — task 041 / Phase B supports exactly ONE
 * level of object nesting (top-level field is object; its properties may be
 * strings or arrays-of-strings). Any deeper nested object (depth ≥ 2 from
 * the top-level object field) falls back to compact JSON with a documented
 * TODO marker. This bound matches the R6 spec FR-29 + Phase B constraint
 * carried in the POML (see <constraints> "Nested object-of-object (depth ≥ 2)
 * is OUT of scope for Phase B").
 *
 * Pure function — no side effects.
 */
function renderObjectValue(
  styles: ReturnType<typeof useStyles>,
  value: unknown,
  schema: JsonSchemaField | undefined,
  fieldPath: string,
  propKey: string,
  depth: number
): React.ReactNode {
  // Defensive: missing value (schema declares key but parsed object lacks it).
  if (value === undefined) {
    return <span className={styles.schemaObjectEmptyHint} data-empty="true">—</span>;
  }

  const propType = schema?.type;

  // Array property — REUSE task 040's `<SchemaAwareArrayRenderer />` so the
  // bulleted-list rendering is identical to top-level array fields. This is the
  // Q5 architectural decision: do NOT duplicate task 040's path.
  if (propType === 'array' && schema?.items?.type === 'string') {
    if (!Array.isArray(value)) {
      // Schema says array but parsed value isn't — defensive fallback.
      return (
        <Text className={styles.errorText} data-display-hint="schema-object-prop-error">
          Expected array; got {typeof value}
        </Text>
      );
    }
    // Coerce items to string defensively.
    const items: string[] = [];
    for (const v of value) {
      if (typeof v !== 'string') {
        return (
          <Text className={styles.errorText} data-display-hint="schema-object-prop-error">
            Array contains non-string item
          </Text>
        );
      }
      items.push(v);
    }
    return (
      <SchemaAwareArrayRenderer
        styles={styles}
        items={items}
        errorMessage={null}
        fieldPath={`${fieldPath}.${propKey}`}
      />
    );
  }

  // Plain-string property.
  if (propType === 'string') {
    if (typeof value !== 'string') {
      return (
        <Text className={styles.errorText} data-display-hint="schema-object-prop-error">
          Expected string; got {typeof value}
        </Text>
      );
    }
    return (
      <Text
        className={styles.schemaObjectValueText}
        data-display-hint="schema-object-string"
        data-field-path={`${fieldPath}.${propKey}`}
      >
        {value}
      </Text>
    );
  }

  // Nested object property — Phase B depth limit.
  //
  // Depth semantics: `depth` = how many object levels we've descended through
  // BEFORE reaching this property. `SchemaAwareObjectRenderer` itself
  // (rendering the top-level object field) sits at "depth 1"; it invokes
  // `renderObjectValue` with `depth = 1`. If the property's value is ALSO an
  // object, rendering it as labeled blocks would mean depth 2 — out of scope
  // for Phase B (POML constraint: "Nested object-of-object (depth ≥ 2) is
  // OUT of scope for Phase B").
  //
  // Therefore: if `propType === 'object'` we ALWAYS bail to compact JSON
  // fallback at this point — Phase B does not render nested object levels
  // as labeled blocks. The `data-depth` attribute reports `depth + 1` so the
  // value reflects the level at which the deep value WOULD have been rendered
  // (the more-intuitive UI-facing depth). Future phases lift this restriction
  // by recursing into a nested `<SchemaAwareObjectRenderer />` here when
  // `depth + 1 <= MAX_OBJECT_DEPTH`.
  if (propType === 'object' && schema?.properties) {
    const renderedDepth = depth + 1;
    // eslint-disable-next-line no-console
    console.debug(
      `[StructuredOutputStreamWidget] depth-≥-2 nested object fallback path="${fieldPath}.${propKey}" depth=${renderedDepth}`
    );
    let compact: string;
    try {
      compact = JSON.stringify(value);
    } catch {
      compact = '[unserializable]';
    }
    return (
      <pre
        className={styles.schemaObjectDeepFallback}
        data-display-hint="schema-object-deep-fallback"
        data-field-path={`${fieldPath}.${propKey}`}
        data-depth={String(renderedDepth)}
      >
        {/* TODO(phase-c): Lift depth-≥-2 limit when richer schemas land. */}
        {compact}
      </pre>
    );
  }

  // Schema declares an unrecognised type (number, boolean, or omitted) — render
  // a stringified value to surface SOMETHING without crashing. Future phases
  // may add explicit dispatch for number/boolean if the contracts require it.
  let display: string;
  if (typeof value === 'string') {
    display = value;
  } else {
    try {
      display = JSON.stringify(value);
    } catch {
      display = String(value);
    }
  }
  return (
    <Text
      className={styles.schemaObjectValueText}
      data-display-hint="schema-object-fallback"
      data-field-path={`${fieldPath}.${propKey}`}
    >
      {display}
    </Text>
  );
}

const SchemaAwareObjectRenderer: React.FC<SchemaAwareObjectRendererProps> = ({
  styles,
  value,
  errorMessage,
  fieldPath,
  fieldSchema,
}) => {
  if (errorMessage !== null) {
    return (
      <div data-display-hint="schema-object-error" data-field-path={fieldPath}>
        <Text className={styles.errorText}>{errorMessage}</Text>
      </div>
    );
  }
  const properties = fieldSchema.properties;
  // Properties absent from schema — render empty labeled-block container so
  // the layout is consistent with the array empty-state convention.
  if (!properties || Object.keys(properties).length === 0) {
    return (
      <div
        className={styles.schemaObjectContainer}
        data-display-hint="schema-object"
        data-field-path={fieldPath}
        data-empty="true"
      />
    );
  }
  const parsed: Record<string, unknown> = value ?? {};
  return (
    <div
      className={styles.schemaObjectContainer}
      data-display-hint="schema-object"
      data-field-path={fieldPath}
    >
      {Object.entries(properties).map(([propKey, propSchema]) => (
        <div
          key={propKey}
          className={styles.schemaObjectRow}
          data-prop-key={propKey}
          data-field-path={`${fieldPath}.${propKey}`}
        >
          <Text className={styles.schemaObjectKey}>{prettyName(propKey)}</Text>
          {renderObjectValue(styles, parsed[propKey], propSchema, fieldPath, propKey, 1)}
        </div>
      ))}
    </div>
  );
};

/** Dispatch to the right sub-renderer based on `displayHint`. */
function renderFieldByHint(props: FieldRendererProps): React.ReactNode {
  switch (props.field.displayHint) {
    case 'heading':
      return <HeadingRenderer {...props} />;
    case 'paragraph':
      return <ParagraphRenderer {...props} />;
    case 'callout':
      return <CalloutRenderer {...props} />;
    case 'badge':
      return <BadgeRenderer {...props} />;
    case 'list':
      return <ListRenderer {...props} />;
    default: {
      // Exhaustiveness guard — should never hit at runtime; widens defensively.
      const fallback: never = props.field.displayHint;
      // eslint-disable-next-line no-console
      console.warn(`[StructuredOutputStreamWidget] unknown displayHint: ${String(fallback)}`);
      return <ParagraphRenderer {...props} />;
    }
  }
}

// ---------------------------------------------------------------------------
// Main component
// ---------------------------------------------------------------------------

const StructuredOutputStreamWidget: React.FC<WorkspaceWidgetProps<StructuredOutputStreamWidgetData>> = ({
  data,
  widgetType,
  isLoading,
  error,
  className,
}) => {
  const styles = useStyles();
  const isValid = isStreamWidgetData(data);

  // Defensive defaults when payload is malformed; render an error state.
  const mode = isValid ? data.mode : 'static';
  const schema = isValid ? data.schema : { fields: [] };
  const prefilledFields = isValid ? data.prefilledFields : undefined;
  const correlationId = isValid ? data.correlationId : undefined;
  const declineState = isValid ? data.declineState : undefined;
  const emptyResultState = isValid ? data.emptyResultState === true : false;
  const title = isValid ? data.title : undefined;
  const outputSchema = isValid ? data.outputSchema : undefined;

  const [streamState, dispatch] = React.useReducer(streamReducer, INITIAL_REDUCER_STATE);

  // ── PaneEventBus subscription ──────────────────────────────────────────
  //
  // We subscribe unconditionally (hooks rule) but only react when:
  //   - mode === 'streaming' (static mode ignores all stream events)
  //   - the event's `streamId` matches our `correlationId` (when present)
  //   - the event's `type` is one of the three streaming discriminants
  //
  // Per ADR-030: unknown `event.type` values MUST be ignored to preserve
  // back-compat with future additive types.
  //
  // Per usePaneEvent semantics: the handler ref is stabilized internally; we
  // do not need useCallback. The hook is always called (no conditional hooks).
  usePaneEvent('workspace', (event: WorkspacePaneEvent) => {
    if (mode !== 'streaming') return;

    // Correlation gate — when both sides carry an id, match; otherwise tolerate
    // for development convenience but log so missing ids surface in telemetry.
    if (correlationId !== undefined && event.streamId !== undefined && event.streamId !== correlationId) {
      return; // event belongs to a different widget instance
    }

    switch (event.type) {
      case 'streaming_started':
        dispatch({ type: 'streaming_started' });
        return;
      case 'field_delta': {
        // Required fields per PaneEventTypes contract: fieldPath, fieldContent, sequence.
        // Defensive: drop event if any are missing.
        if (
          typeof event.fieldPath !== 'string' ||
          typeof event.fieldContent !== 'string' ||
          typeof event.sequence !== 'number'
        ) {
          // eslint-disable-next-line no-console
          console.debug('[StructuredOutputStreamWidget] dropped malformed field_delta event');
          return;
        }
        dispatch({
          type: 'field_delta',
          path: event.fieldPath,
          content: event.fieldContent,
          sequence: event.sequence,
        });
        return;
      }
      case 'streaming_complete':
        dispatch({ type: 'streaming_complete' });
        return;
      default:
        // Unknown event types — IGNORE per ADR-030.
        return;
    }
  });

  // ── Render-state matrix (four mutually-exclusive states) ───────────────
  //
  // Precedence (highest first):
  //   1. error           — top-level error prop set (host signalled failure)
  //   2. declineState    — Insights / orchestrator decline (warning UI)
  //   3. emptyResultState — RAG zero-hit case (muted hint UI)
  //   4. isLoading       — host signalled pre-mount fetch in progress
  //   5. streaming/static — normal field rendering
  // ────────────────────────────────────────────────────────────────────────

  const sortedFields = React.useMemo(() => [...schema.fields].sort((a, b) => a.order - b.order), [schema.fields]);

  // Header state badge — derived from current phase + override states.
  const headerBadge = (() => {
    if (declineState) {
      return (
        <Badge appearance="filled" color="warning" icon={<WarningRegular />} data-state="decline">
          Declined
        </Badge>
      );
    }
    if (emptyResultState) {
      return (
        <Badge appearance="tint" color="informative" data-state="empty">
          No results
        </Badge>
      );
    }
    if (mode === 'static') {
      return (
        <Badge appearance="filled" color="success" icon={<CheckmarkCircleRegular />} data-state="static">
          Complete
        </Badge>
      );
    }
    // streaming mode
    if (streamState.phase === 'complete') {
      return (
        <Badge appearance="filled" color="success" icon={<CheckmarkCircleRegular />} data-state="complete">
          Complete
        </Badge>
      );
    }
    if (streamState.phase === 'streaming') {
      return (
        <Badge appearance="tint" color="brand" data-state="streaming">
          Streaming…
        </Badge>
      );
    }
    return (
      <Badge appearance="ghost" color="subtle" data-state="idle">
        Waiting
      </Badge>
    );
  })();

  // Effective per-field content resolver — picks streaming reducer state or
  // static `prefilledFields` depending on mode.
  const contentForPath = (path: string): string | undefined => {
    if (mode === 'streaming') {
      return streamState.fields.get(path)?.content;
    }
    return prefilledFields?.[path];
  };

  // Determine whether to show a cursor for a given field. Only one cursor
  // visible at a time — at the tail of `mostRecentPath` while streaming.
  const showCursorForPath = (path: string): boolean => {
    if (mode !== 'streaming') return false;
    if (streamState.phase !== 'streaming') return false;
    return streamState.mostRecentPath === path;
  };

  return (
    <div
      className={mergeClasses(styles.root, className)}
      data-widget-type={widgetType}
      data-testid="structured-output-stream-widget"
      data-render-state={
        error
          ? 'error'
          : declineState
            ? 'decline'
            : emptyResultState
              ? 'empty'
              : isLoading
                ? 'loading'
                : mode === 'streaming'
                  ? streamState.phase
                  : 'static'
      }
    >
      <Card className={styles.card}>
        <CardHeader
          image={<SparkleRegular className={styles.headerIcon} />}
          header={<Text className={styles.headerTitle}>{title ?? 'AI Output'}</Text>}
          description={<div className={styles.headerBadgeRow}>{headerBadge}</div>}
        />

        {/* Error state — top-level host error takes precedence over everything. */}
        {error && (
          <div className={styles.declineContainer}>
            <Text className={styles.errorText}>{error}</Text>
          </div>
        )}

        {/* (c) Decline state — MessageBar warning + optional suggested actions. */}
        {!error && declineState && (
          <div className={styles.declineContainer} data-testid="decline-state">
            <MessageBar intent="warning">
              <MessageBarBody>{declineState.reason}</MessageBarBody>
            </MessageBar>
            {declineState.suggestedActions && declineState.suggestedActions.length > 0 && (
              <>
                <Divider />
                <Text className={styles.fieldLabel}>Suggested next steps</Text>
                <div className={styles.suggestedActionRow}>
                  {declineState.suggestedActions.map((action, i) => (
                    <Button
                      key={`${action}-${i}`}
                      appearance="secondary"
                      size="small"
                      onClick={() => declineState.onActionClick?.(action)}
                    >
                      {action}
                    </Button>
                  ))}
                </div>
              </>
            )}
          </div>
        )}

        {/* (d) Empty-result state — muted hint, no field rendering. */}
        {!error && !declineState && emptyResultState && (
          <div className={styles.emptyResultContainer} data-testid="empty-result-state">
            <Text className={styles.emptyResultText}>
              I couldn’t find anything for that. Try rephrasing or attaching files.
            </Text>
          </div>
        )}

        {/* Loading state — host pre-mount fetch (NOT the same as streaming). */}
        {!error && !declineState && !emptyResultState && isLoading && (
          <div className={styles.emptyResultContainer}>
            <Text className={styles.emptyResultText}>Loading…</Text>
          </div>
        )}

        {/* (a) Streaming + (b) Streaming-complete + static rendering — schema fields. */}
        {!error && !declineState && !emptyResultState && !isLoading && (
          <div className={styles.fieldsContainer}>
            {sortedFields.length === 0 && <Text className={styles.emptyResultText}>(No schema fields declared.)</Text>}
            {sortedFields.map((field, idx) => {
              const content = contentForPath(field.path);
              const hasContent = typeof content === 'string' && content.length > 0;
              const showCursor = hasContent && showCursorForPath(field.path);

              // ── Schema-aware dispatch (R6 task 040 array; task 041 object) ────
              //
              // When the action's `outputSchema` declares this field as an
              // `array` of `string` (e.g., `tldr: string[]` on SUM-CHAT@v1),
              // attempt to JSON.parse the accumulated content and render as
              // a Fluent v9 bulleted list. The dispatch is GATED on the
              // streaming phase: mid-stream tokens are not parseable JSON, so
              // the legacy skeleton/cursor path continues to render until
              // `streaming_complete` fires (or `mode === 'static'`).
              //
              // Per NFR-11 backward compatibility: when `outputSchema` is
              // absent or the field's schema is not recognised, `classification`
              // is `'legacy'` and the existing `renderFieldByHint` path runs
              // unchanged.
              const classification = classifySchemaField(outputSchema, field.path);
              const schemaAwareReady = hasContent && (mode === 'static' || streamState.phase === 'complete');

              let schemaAwareNode: React.ReactNode | null = null;
              if (schemaAwareReady && classification === 'array-of-string') {
                const parseResult = parseArrayOfString(content ?? '');
                if ('error' in parseResult) {
                  // eslint-disable-next-line no-console
                  console.warn(
                    `[StructuredOutputStreamWidget] schema-aware parse failure path="${field.path}" error="${parseResult.error}"`
                  );
                  schemaAwareNode = (
                    <SchemaAwareArrayRenderer
                      styles={styles}
                      items={null}
                      errorMessage={parseResult.error}
                      fieldPath={field.path}
                    />
                  );
                } else {
                  schemaAwareNode = (
                    <SchemaAwareArrayRenderer
                      styles={styles}
                      items={parseResult.items}
                      errorMessage={null}
                      fieldPath={field.path}
                    />
                  );
                }
              } else if (schemaAwareReady && classification === 'object') {
                // R6 task 041 — object dispatch (fixes R5 SC-18 entities bug).
                // We parse the accumulated content as JSON and hand it +
                // the schema's properties subtree to `SchemaAwareObjectRenderer`,
                // which renders labeled key-value blocks (one per nested
                // property) and recursively reuses task 040's bulleted-list
                // path for nested array-of-string properties (e.g.,
                // `entities.organizations`).
                const fieldSchema = outputSchema?.properties?.[field.path];
                if (fieldSchema === undefined) {
                  // Should not happen: classification === 'object' implies the
                  // field's schema exists with type 'object'. Defensive fallback.
                  schemaAwareNode = null;
                } else {
                  const parseResult = parseObject(content ?? '');
                  if ('error' in parseResult) {
                    // eslint-disable-next-line no-console
                    console.warn(
                      `[StructuredOutputStreamWidget] schema-aware object parse failure path="${field.path}" error="${parseResult.error}"`
                    );
                    schemaAwareNode = (
                      <SchemaAwareObjectRenderer
                        styles={styles}
                        value={null}
                        errorMessage={parseResult.error}
                        fieldPath={field.path}
                        fieldSchema={fieldSchema}
                      />
                    );
                  } else {
                    schemaAwareNode = (
                      <SchemaAwareObjectRenderer
                        styles={styles}
                        value={parseResult.value}
                        errorMessage={null}
                        fieldPath={field.path}
                        fieldSchema={fieldSchema}
                      />
                    );
                  }
                }
              }

              return (
                <React.Fragment key={field.path}>
                  {idx > 0 && <Divider />}
                  <div className={styles.fieldBlock} data-field-path={field.path}>
                    {field.label && <Text className={styles.fieldLabel}>{field.label}</Text>}
                    {schemaAwareNode !== null ? (
                      // Schema-aware path took over (task 040 array; task 041 object).
                      schemaAwareNode
                    ) : hasContent ? (
                      renderFieldByHint({ field, content: content ?? '', showCursor, styles })
                    ) : mode === 'streaming' && streamState.phase !== 'complete' ? (
                      // Skeleton while streaming and this field has not started yet
                      <FieldSkeleton styles={styles} displayHint={field.displayHint} />
                    ) : (
                      // Static mode (or completed streaming) with no content for this path
                      <Text className={styles.emptyResultText}>—</Text>
                    )}
                  </div>
                </React.Fragment>
              );
            })}
          </div>
        )}
      </Card>
    </div>
  );
};

export default StructuredOutputStreamWidget;
