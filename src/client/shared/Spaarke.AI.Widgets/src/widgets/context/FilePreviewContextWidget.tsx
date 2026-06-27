/**
 * @spaarke/ai-widgets — FilePreviewContextWidget
 *
 * Context-pane widget that renders an INLINE (non-modal) preview of one or
 * more files uploaded into the current chat session (`ChatSession.UploadedFiles`,
 * spec NFR-02, hard cap = 20). Mounted under the Context pane during the
 * R5 chat-driven Summarize vertical slice (spec FR-08).
 *
 * Modes:
 *   - Single-file mode (`files.length === 1`) — renders the file inline via
 *     the extracted `RichFilePreview` renderer (task 013 / D2-08). Prev/Next
 *     controls are hidden (only one file). The renderer's title-bar 3-dot
 *     menu carries the canonical 12-action set (`DocumentRowMenu`).
 *   - Multi-file mode (`files.length > 1`) — renders a vertical list of
 *     compact file cards ABOVE the active preview. Clicking a card swaps the
 *     active file AND dispatches a `context.file_selected` event on the
 *     `context` PaneEventBus channel (additive event type added by task 016).
 *   - Empty (`files.length === 0`) — renders an empty state ("No files
 *     attached") instead of a blank pane.
 *   - Loading (`isLoading === true`) — Fluent v9 Skeleton.
 *   - Error (`error` truthy) — Fluent v9 MessageBar (intent="error").
 *
 * Reuse mandate (R5 CLAUDE.md §3.1):
 *   - WRAPS the extracted `RichFilePreview` renderer (task 013). Does NOT
 *     rebuild iframe / metadata / prev-next / menu UI.
 *   - The per-card 3-dot menu reuses `DocumentRowMenu` (12 actions in the
 *     FR-DOC-01 canonical order) via the `RichFilePreview` renderer (single
 *     active file) and directly per card in multi-file mode.
 *   - NO parallel file-preview component to `RichFilePreviewDialog`.
 *
 * Event-bus discipline (ADR-030):
 *   - Uses the existing `context` channel ONLY. No new channel introduced.
 *   - Dispatches the additive `context.file_selected` discriminant (R5 task
 *     016 / D2-06). Existing `context` subscribers (entity-info, findings,
 *     citation-highlight) ignore unknown types and continue to function.
 *
 * Per-file "Summarize this only" affordance (task 021 / D2-12):
 *   - Each file card AND the single-file inline preview header render a
 *     prominent "Summarize this only" `Button` adjacent to the 3-dot
 *     `DocumentRowMenu` (Decision B1 from task 021 POML — keep BOTH
 *     surfaces; button = primary, menu `aiSummary` = alternative discovery).
 *   - Click handler dispatches via PaneEventBus ONLY (Decision A1 — no
 *     direct HTTP call from this widget). Two events are emitted in
 *     sequence per file click:
 *       (a) `workspace.widget_load` mounts a NEW Workspace tab with the
 *           `structured-output-stream` widget (FR-06 additive — prior
 *           tabs preserved). `widgetData.correlationId` carries a fresh
 *           `crypto.randomUUID()` so concurrent streams disambiguate per
 *           task 017 widget contract.
 *       (b) `workspace.streaming_started` carries `streamId === correlationId`
 *           to flip the mounted widget's reducer from `idle` → `streaming`.
 *           Subsequent `field_delta` + `streaming_complete` events come
 *           from the BFF SSE consumer (task 020 chat-pane orchestration).
 *   - The dispatch is encapsulated in a local `useSummarizeOnly(fileId,
 *     sessionId, fileName)` hook so both the button AND the existing
 *     `DocumentRowMenu` `aiSummary` action route through the SAME code
 *     path (FR-05 + FR-08 dual-surface discovery).
 *
 * Standards:
 *   - ADR-012: lives in `@spaarke/ai-widgets`; consumes `@spaarke/ui-components`
 *     for `RichFilePreview` + `DocumentRowMenu` (component library boundary).
 *   - ADR-021: Fluent v9 semantic tokens only. Zero hard-coded hex/rgb.
 *     Dark-mode parity by construction (tokens adapt to the host theme).
 *   - ADR-022: React 19 functional component. Hooks only.
 *   - ADR-018: no new feature flags; registered unconditionally.
 *   - ADR-030: additive event-type only. Existing subscribers unaffected.
 *
 * React 19, NOT PCF-safe (this widget runs in the SpaarkeAi Code Page shell).
 *
 * Task: R5-018 (D2-09).
 */

import React, { useCallback, useMemo, useState } from 'react';
import {
  makeStyles,
  mergeClasses,
  tokens,
  Card,
  CardHeader,
  Text,
  Tag,
  Skeleton,
  SkeletonItem,
  MessageBar,
  MessageBarBody,
  MessageBarTitle,
  Button,
  Tooltip,
  Spinner,
} from '@fluentui/react-components';
import {
  DocumentRegular,
  DocumentPdfRegular,
  DocumentTextRegular,
  DocumentTableRegular,
  SparkleRegular,
} from '@fluentui/react-icons';

import { RichFilePreview, DocumentRowMenu, type DocumentRowAction } from '@spaarke/ui-components';
import type { ContextWidgetProps } from '../../types/widget-types';
import { useDispatchPaneEvent, type DispatchPaneEvent } from '../../events/useDispatchPaneEvent';
import { usePaneEvent } from '../../events/usePaneEvent';
import type { WorkspacePaneEvent } from '../../events/PaneEventTypes';
import {
  SUMMARIZE_SCHEMA,
  SUM_CHAT_OUTPUT_SCHEMA,
  type StructuredOutputStreamWidgetData,
} from '../workspace/StructuredOutputStreamWidget';
import { STRUCTURED_OUTPUT_STREAM_WIDGET_TYPE } from '../workspace/register-structured-output-stream-widget';

// ---------------------------------------------------------------------------
// Public data types
// ---------------------------------------------------------------------------

/**
 * Per-file descriptor consumed by the widget. The shape MIRRORS the BFF
 * `ChatSessionFile` record (`src/server/api/Sprk.Bff.Api/Models/Ai/Chat/ChatSession.cs`)
 * but lives in the frontend domain to keep the widget self-contained.
 *
 * The `previewUrl` field is OPTIONAL because the iframe `src` is fetched
 * lazily via `onFetchPreviewUrl` (per-file callback). This matches the
 * extracted renderer's `fetchPreviewUrl: () => Promise<string | null>`
 * contract — the host owns the URL-fetch closure so the widget stays
 * agnostic of authentication / SAS-token / OBO concerns (ADR-028).
 */
export interface FilePreviewContextFile {
  /** Stable session-scoped file identifier (matches `ChatSessionFile.FileId`). */
  fileId: string;
  /** Original filename (e.g. "Contract.pdf"). */
  fileName: string;
  /**
   * Optional document type label (e.g. "Contract", "PDF"). Drives the Tag
   * chip in the active-file preview and the card-level type label. When
   * absent, the widget falls back to a humanised content-type derivation.
   */
  documentType?: string;
  /** Optional MIME content-type used for icon selection. */
  contentType?: string;
  /** Optional file size in bytes (informational only). */
  sizeBytes?: number | null;
  /** Optional "Created by" display name. */
  createdBy?: string | null;
  /** Optional ISO date string for the "Created" detail row. */
  createdOn?: string | null;
}

/**
 * Data payload delivered to the widget via `ContextWidgetProps.data`.
 *
 * `files` lists every file uploaded into the active chat session
 * (`ChatSession.UploadedFiles`). The list is bounded by the per-session
 * cap (spec NFR-02; 20 files). Order is the upload order, preserved.
 *
 * `activeFileId` is OPTIONAL — when present, that file is initially
 * selected; otherwise the first file in `files` is selected. Subsequent
 * user clicks override the active file locally via component state.
 */
export interface FilePreviewContextData {
  files: FilePreviewContextFile[];
  activeFileId?: string;
  /**
   * Active chat session identifier. Used by the per-file "Summarize this only"
   * affordance (task 021 / D2-12) to anchor the dispatched Workspace tab to
   * the correct session. When absent, the affordance still renders but the
   * dispatched `widget_load` payload carries `sessionId: ''` — downstream
   * consumers (BFF SSE consumer in task 020 / D2-11) MUST treat an empty
   * `sessionId` as a no-op and surface a UX hint.
   *
   * Additive (optional) — does NOT break existing single-/multi-file callers
   * who only need the preview surface (task 018 baseline behaviour).
   */
  sessionId?: string;
}

/**
 * Per-file action callback dispatched by the 3-dot row-action menu.
 *
 * The host is responsible for resolving the action (e.g. routing
 * `aiSummary` to the Summarize playbook, `findSimilar` to RAG, etc.).
 *
 * NOTE: `toggleWorkspace` is handled IN-WIDGET (dispatched as a
 * `workspace.widget_load` event with `widgetType: 'document-viewer'`) so
 * the canonical Workspace-pane plumbing is reused without host glue.
 * Hosts may still override by handling `toggleWorkspace` via this callback
 * — see the implementation note in `handleFileAction` below.
 */
export type FilePreviewFileActionHandler = (action: DocumentRowAction, fileId: string) => void;

/**
 * Optional renderer-binding callbacks. Hosts supply these to wire the
 * widget into the surrounding chat-pane / SpaarkeAi shell context. The
 * widget intentionally does NOT call BFF endpoints directly — every
 * I/O concern is host-owned so the widget remains library-pure and
 * testable in isolation.
 */
export interface FilePreviewContextRenderProps {
  /**
   * Fetch the preview iframe URL for a given file. Forwarded directly to
   * the extracted `RichFilePreview` renderer's `fetchPreviewUrl` prop.
   * Resolving to `null` triggers the renderer's "Preview not available"
   * fallback state.
   */
  onFetchPreviewUrl: (fileId: string) => Promise<string | null>;
  /**
   * Open the file in desktop or web app (forwarded to RichFilePreview).
   */
  onOpenFile?: (fileId: string, mode: 'desktop' | 'web') => void;
  /**
   * Open the Dataverse record in a new tab (forwarded to RichFilePreview).
   */
  onOpenRecord?: (fileId: string) => void;
  /**
   * Open the email-document dialog (forwarded to RichFilePreview).
   */
  onEmailDocument?: (fileId: string) => void;
  /**
   * Copy the document link to clipboard (forwarded to RichFilePreview).
   */
  onCopyLink?: (fileId: string) => void;
  /**
   * Per-file action handler — bubbles ALL menu actions (including
   * `aiSummary`, `findSimilar`, etc.) so the host can route them. Hosts
   * that omit this handler get a no-op (defensive fallback — no errors,
   * but no behavior). Task 021 will deepen the `aiSummary` wiring.
   */
  onFileAction?: FilePreviewFileActionHandler;
  /**
   * Optional override of the per-file 3-dot menu's hidden actions. When
   * absent, the widget hides `'preview'` always (the widget IS the preview)
   * and, if `onFileAction` is not provided, additionally hides actions that
   * have no destination (defensive). Hosts may pass an explicit array to
   * tailor the menu per session.
   */
  disabledActions?: DocumentRowAction[];
}

// ---------------------------------------------------------------------------
// Combined Props
// ---------------------------------------------------------------------------

/**
 * The widget's full prop surface — composes the base
 * `ContextWidgetProps<FilePreviewContextData>` with the optional
 * host-binding callbacks. The combined shape remains assignable to
 * `ContextWidgetComponent` at the registry boundary (host-binding
 * callbacks are read off the props at render time; absent callbacks
 * degrade gracefully).
 */
export type FilePreviewContextWidgetProps = ContextWidgetProps<FilePreviewContextData> &
  Partial<FilePreviewContextRenderProps>;

// ---------------------------------------------------------------------------
// Constants
// ---------------------------------------------------------------------------

/**
 * The widget type ID under which `FilePreviewContextWidget` is registered.
 * Exported so dispatchers / hosts can reference the string symbolically
 * instead of repeating the literal.
 */
export const FILE_PREVIEW_CONTEXT_WIDGET_TYPE = 'file-preview' as const;

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
  },

  // Header — title + subtitle for the widget itself (multi-file mode
  // renders this; single-file mode hides it to maximise preview real
  // estate, since RichFilePreview has its own title bar).
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

  // Multi-file card list — scrolls vertically when N is large (cap = 20).
  // For N <= 20 a plain flex list is acceptable (NFR-02: no virtualization).
  cardList: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalS,
    paddingLeft: tokens.spacingHorizontalM,
    paddingRight: tokens.spacingHorizontalM,
    paddingBottom: tokens.spacingVerticalS,
    overflowY: 'auto',
    flexShrink: 0,
    // Cap the card list at ~40% of the pane so the active preview gets
    // the majority of the vertical real estate.
    maxHeight: '40%',
  },

  // Per-file card — mirrors PlaybookGalleryWidget hover/selected affordances.
  card: {
    cursor: 'pointer',
    display: 'flex',
    flexDirection: 'row',
    alignItems: 'center',
    gap: tokens.spacingHorizontalS,
    paddingTop: tokens.spacingVerticalS,
    paddingBottom: tokens.spacingVerticalS,
    paddingLeft: tokens.spacingHorizontalM,
    paddingRight: tokens.spacingHorizontalS,
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
    borderTopLeftRadius: tokens.borderRadiusMedium,
    borderTopRightRadius: tokens.borderRadiusMedium,
    borderBottomLeftRadius: tokens.borderRadiusMedium,
    borderBottomRightRadius: tokens.borderRadiusMedium,
    transition: 'box-shadow 0.12s ease, border-color 0.12s ease',
    ':hover': {
      borderTopColor: tokens.colorBrandStroke1,
      borderRightColor: tokens.colorBrandStroke1,
      borderBottomColor: tokens.colorBrandStroke1,
      borderLeftColor: tokens.colorBrandStroke1,
      boxShadow: tokens.shadow4,
    },
    ':focus-within': {
      borderTopColor: tokens.colorBrandStroke1,
      borderRightColor: tokens.colorBrandStroke1,
      borderBottomColor: tokens.colorBrandStroke1,
      borderLeftColor: tokens.colorBrandStroke1,
      outlineStyle: 'none',
    },
  },
  cardSelected: {
    borderTopColor: tokens.colorBrandStroke1,
    borderRightColor: tokens.colorBrandStroke1,
    borderBottomColor: tokens.colorBrandStroke1,
    borderLeftColor: tokens.colorBrandStroke1,
    borderTopWidth: tokens.strokeWidthThick,
    borderRightWidth: tokens.strokeWidthThick,
    borderBottomWidth: tokens.strokeWidthThick,
    borderLeftWidth: tokens.strokeWidthThick,
    backgroundColor: tokens.colorBrandBackground2,
  },
  cardIcon: {
    color: tokens.colorBrandForeground1,
    fontSize: tokens.fontSizeBase500,
    flexShrink: 0,
  },
  cardBody: {
    display: 'flex',
    flexDirection: 'column',
    minWidth: 0,
    flex: 1,
    gap: tokens.spacingVerticalXXS,
  },
  cardTitle: {
    fontWeight: tokens.fontWeightSemibold,
    fontSize: tokens.fontSizeBase300,
    color: tokens.colorNeutralForeground1,
    overflow: 'hidden',
    textOverflow: 'ellipsis',
    whiteSpace: 'nowrap',
  },
  cardMetaRow: {
    display: 'flex',
    flexDirection: 'row',
    alignItems: 'center',
    gap: tokens.spacingHorizontalXS,
    flexWrap: 'wrap',
  },
  cardMetaText: {
    fontSize: tokens.fontSizeBase200,
    color: tokens.colorNeutralForeground3,
  },

  // Per-card "Summarize this only" affordance row (task 021 / D2-12).
  // Contains a prominent `Button` (label + icon, brand-tinted) and the
  // existing 3-dot DocumentRowMenu. The button is the primary affordance;
  // the menu's `aiSummary` action is alternative discovery (FR-05).
  cardActionRow: {
    display: 'flex',
    flexDirection: 'row',
    alignItems: 'center',
    gap: tokens.spacingHorizontalXS,
    flexShrink: 0,
  },
  summarizeButton: {
    // Subtle appearance keeps the card visually quiet; brand icon foreground
    // signals AI provenance. Disabled state (in-flight) reduces opacity via
    // Fluent v9's built-in disabled tokens — no override needed.
    flexShrink: 0,
  },
  // Single-file mode header overlay (positioned absolutely above the
  // RichFilePreview title bar so the button is visible without rewriting
  // the renderer's markup — wrap-and-overlay composition).
  singleFileHeaderOverlay: {
    position: 'relative',
    flex: 1,
    minHeight: 0,
    display: 'flex',
    flexDirection: 'column',
  },
  singleFileActionBar: {
    display: 'flex',
    flexDirection: 'row',
    alignItems: 'center',
    justifyContent: 'flex-end',
    gap: tokens.spacingHorizontalS,
    paddingTop: tokens.spacingVerticalS,
    paddingBottom: tokens.spacingVerticalS,
    paddingLeft: tokens.spacingHorizontalM,
    paddingRight: tokens.spacingHorizontalM,
    backgroundColor: tokens.colorNeutralBackground2,
    borderTopLeftRadius: tokens.borderRadiusMedium,
    borderTopRightRadius: tokens.borderRadiusMedium,
    flexShrink: 0,
  },
  singleFileActionLabel: {
    fontSize: tokens.fontSizeBase200,
    color: tokens.colorNeutralForeground3,
    marginRight: 'auto',
  },

  // Active preview slot — fills remaining vertical space.
  previewSlot: {
    flex: 1,
    minHeight: 0,
    display: 'flex',
    flexDirection: 'column',
    overflow: 'hidden',
  },

  // Skeleton state.
  skeletonRoot: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalM,
    padding: tokens.spacingHorizontalM,
    flex: 1,
    minHeight: 0,
  },
  skeletonCard: {
    paddingTop: tokens.spacingVerticalS,
    paddingBottom: tokens.spacingVerticalS,
    paddingLeft: tokens.spacingHorizontalM,
    paddingRight: tokens.spacingHorizontalM,
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
    borderTopLeftRadius: tokens.borderRadiusMedium,
    borderTopRightRadius: tokens.borderRadiusMedium,
    borderBottomLeftRadius: tokens.borderRadiusMedium,
    borderBottomRightRadius: tokens.borderRadiusMedium,
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalS,
  },

  // Empty + error states.
  centerState: {
    display: 'flex',
    flexDirection: 'column',
    alignItems: 'center',
    justifyContent: 'center',
    gap: tokens.spacingVerticalM,
    padding: tokens.spacingHorizontalXL,
    flex: 1,
    minHeight: 0,
    textAlign: 'center',
  },
  emptyStateIcon: {
    color: tokens.colorNeutralForeground4,
    fontSize: '48px',
  },
  emptyStateTitle: {
    fontWeight: tokens.fontWeightSemibold,
    fontSize: tokens.fontSizeBase400,
    color: tokens.colorNeutralForeground2,
  },
  emptyStateBody: {
    fontSize: tokens.fontSizeBase200,
    color: tokens.colorNeutralForeground3,
    maxWidth: '240px',
  },
  errorBar: {
    margin: tokens.spacingHorizontalM,
    flexShrink: 0,
  },
});

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

/**
 * Pick a Fluent v9 icon for the per-file card based on content-type. Falls
 * back to a generic document icon when content-type is absent or unknown.
 */
function pickFileIcon(contentType?: string): React.ReactElement {
  if (!contentType) return <DocumentRegular />;
  if (contentType === 'application/pdf') return <DocumentPdfRegular />;
  if (contentType.startsWith('text/')) return <DocumentTextRegular />;
  if (
    contentType === 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet' ||
    contentType === 'text/csv' ||
    contentType === 'application/vnd.ms-excel'
  ) {
    return <DocumentTableRegular />;
  }
  return <DocumentRegular />;
}

/** Short human-readable size (e.g. "1.5 MB"). Returns null when absent. */
function formatFileSize(bytes?: number | null): string | null {
  if (typeof bytes !== 'number' || !Number.isFinite(bytes) || bytes < 0) return null;
  if (bytes === 0) return '0 B';
  if (bytes < 1024) return `${bytes} B`;
  const units = ['KB', 'MB', 'GB', 'TB'];
  let size = bytes / 1024;
  let i = 0;
  while (size >= 1024 && i < units.length - 1) {
    size /= 1024;
    i += 1;
  }
  return `${size.toFixed(1)} ${units[i]}`;
}

// ---------------------------------------------------------------------------
// Shared dispatch helper + hook: useSummarizeOnly (task 021 / D2-12)
// ---------------------------------------------------------------------------

/**
 * Result of `dispatchSummarizeOnly` — pure (no-hook) dispatch helper. The
 * generated `correlationId` is returned so callers (including the
 * `DocumentRowMenu` `aiSummary` action callback site) can track the
 * in-flight stream without subscribing to PaneEventBus themselves.
 */
export interface DispatchSummarizeOnlyResult {
  correlationId: string;
}

/**
 * Pure dispatch helper for the per-file "Summarize this only" flow. Both
 * the `SummarizeOnlyButton` component AND the `DocumentRowMenu.aiSummary`
 * action callback route through this function so the dispatch shape is
 * IDENTICAL across both surfaces (FR-05 dual-surface mandate; task 021
 * Step 5 Test 4 acceptance criterion).
 *
 * Emits two PaneEventBus events in sequence (no HTTP call):
 *
 *   (a) `workspace.widget_load` mounts a NEW `structured-output-stream`
 *       Workspace tab (FR-06 additive). The widget receives
 *       `widgetData: { mode: 'streaming', schema: SUMMARIZE_SCHEMA,
 *                       correlationId, title, sessionId, fileIds }`.
 *
 *   (b) `workspace.streaming_started` carrying `streamId === correlationId`
 *       to flip the mounted widget's reducer phase to `streaming`.
 *
 * The function is intentionally pure (no closures over React state) so it
 * can be called from non-component contexts (e.g. a `useCallback`'d menu
 * action handler) without violating the rules of hooks.
 *
 * @internal — exported for unit testing and for the parent widget's
 * `handleFileAction` callback.
 */
export function dispatchSummarizeOnly(
  fileId: string,
  sessionId: string,
  fileName: string,
  dispatch: DispatchPaneEvent
): DispatchSummarizeOnlyResult {
  // `crypto.randomUUID()` is available in modern browsers (Chromium 92+,
  // Firefox 95+, Safari 15.4+) and Node 14+; React 19's targets cover this
  // surface. No polyfill required for the SpaarkeAi Code Page host.
  const correlationId =
    typeof crypto !== 'undefined' && typeof crypto.randomUUID === 'function'
      ? crypto.randomUUID()
      : `req-${Date.now()}-${Math.random().toString(36).slice(2)}`;

  // (a) Mount a NEW Workspace tab with the structured-output-stream widget.
  // FR-06 additive: existing tabs are NOT touched. The Workspace pane
  // subscriber pushes a new tab onto its strip; FIFO eviction at
  // MAX_WORKSPACE_TABS applies if the cap is reached (handled by the
  // Workspace pane, not this widget).
  const widgetData: StructuredOutputStreamWidgetData & {
    // Additive hint fields carried inside the polymorphic `widgetData`
    // payload — NOT a new top-level event field. Consumers (e.g. task
    // 020's BFF SSE consumer) narrow to read these; the widget itself
    // ignores unknown fields per ADR-030.
    sessionId?: string;
    fileIds?: string[];
  } = {
    mode: 'streaming',
    schema: SUMMARIZE_SCHEMA,
    // R6 Hotfix Wave B-G9a (2026-06-10): `outputSchema` is REQUIRED for the
    // schema-aware dispatch added by tasks 040 (array-of-string) + 041
    // (object). Without it, `tldr` renders as a bold paragraph (legacy
    // `displayHint: 'heading'`) and `entities` renders as comma-split bullets
    // (legacy `displayHint: 'list'` + `splitListContent` comma fallback) —
    // both of which were the production symptoms observed during the Phase B
    // walkthrough. The constant mirrors SUM-CHAT@v1's action `outputSchema`
    // verbatim (see `summarize-document-for-chat.playbook.json` actions[0]).
    outputSchema: SUM_CHAT_OUTPUT_SCHEMA,
    correlationId,
    title: `Summary: ${fileName}`,
    sessionId,
    fileIds: [fileId],
  };

  dispatch('workspace', {
    type: 'widget_load',
    widgetType: STRUCTURED_OUTPUT_STREAM_WIDGET_TYPE,
    widgetData,
    displayName: `Summary: ${fileName}`,
  });

  // (b) Flip the mounted widget's reducer to `streaming` phase. The
  // `streamId` MUST equal `widgetData.correlationId` so the widget's
  // correlation gate accepts the event (task 017 subscriber contract).
  dispatch('workspace', {
    type: 'streaming_started',
    streamId: correlationId,
  });

  return { correlationId };
}

// ---------------------------------------------------------------------------

/**
 * Result of the {@link useSummarizeOnly} hook.
 *
 * - `onClick` — fire-and-forget dispatcher for the "Summarize this only" flow.
 *   Emits two PaneEventBus events in sequence (no HTTP call):
 *     (a) `workspace.widget_load` mounts a NEW `structured-output-stream`
 *         Workspace tab (FR-06 additive — prior tabs preserved). The
 *         widget receives `widgetData: { mode: 'streaming', schema:
 *         SUMMARIZE_SCHEMA, correlationId, title: "Summary: <fileName>",
 *         sessionId, fileIds: [fileId] }` so its subscriber can correlate
 *         incoming `field_delta` events.
 *     (b) `workspace.streaming_started` carrying `streamId === correlationId`
 *         to flip the mounted widget's reducer phase to `streaming`.
 *
 * - `isInFlight` — local indicator while the dispatched stream is in flight.
 *   Set `true` on click, cleared when a matching
 *   `workspace.streaming_complete` event arrives (subscribed at the parent
 *   widget level — see `FilePreviewContextWidget`). The button renders a
 *   `Spinner` and is disabled while in flight.
 *
 * - `lastRequestId` — the most-recently-issued `correlationId` for this
 *   `(fileId, sessionId)` tuple. Exposed for test assertions + telemetry; the
 *   component itself does not depend on this value.
 */
export interface UseSummarizeOnlyResult {
  onClick: () => void;
  isInFlight: boolean;
  lastRequestId: string | null;
}

/**
 * Shared per-file Summarize dispatcher hook. Used by:
 *   - the per-card "Summarize this only" `Button` (multi-file mode)
 *   - the single-file inline preview header "Summarize this only" `Button`
 *   - the existing `DocumentRowMenu.aiSummary` action (alternative
 *     discovery; FR-05 dual-surface mandate)
 *
 * All three surfaces share a single dispatch shape so behaviour is
 * indistinguishable across surfaces (FR-08 multi-turn refinement).
 *
 * The hook accepts an explicit `dispatch` argument (a `DispatchPaneEvent`
 * obtained via `useDispatchPaneEvent` at the consuming component) rather
 * than calling `useDispatchPaneEvent` internally; this keeps the hook
 * trivially testable (pass a `vi.fn()` for the dispatcher) and avoids
 * subscribing the parent component to an unrelated context.
 *
 * Per ADR-030: emits only existing PaneEventBus event types
 * (`workspace.widget_load` from R2 origin; `workspace.streaming_started`
 * added by task 016). NO new channels and NO new discriminants.
 *
 * @internal — exported only for unit testing.
 */
export function useSummarizeOnly(
  fileId: string,
  sessionId: string,
  fileName: string,
  dispatch: DispatchPaneEvent
): UseSummarizeOnlyResult {
  const [isInFlight, setInFlight] = React.useState(false);
  const [lastRequestId, setLastRequestId] = React.useState<string | null>(null);

  // Track in-flight requestIds so the parent's `streaming_complete`
  // subscription can resolve them via a stable ref. The ref is keyed off
  // `lastRequestId` so subscribers can match by id.
  const requestIdRef = React.useRef<string | null>(null);
  React.useEffect(() => {
    requestIdRef.current = lastRequestId;
  }, [lastRequestId]);

  const onClick = useCallback(() => {
    const { correlationId } = dispatchSummarizeOnly(fileId, sessionId, fileName, dispatch);
    setLastRequestId(correlationId);
    setInFlight(true);
  }, [dispatch, fileId, fileName, sessionId]);

  // Subscribe to `streaming_complete` so the in-flight indicator resets when
  // the matching requestId completes. Unknown event types are ignored per
  // ADR-030.
  usePaneEvent('workspace', (event: WorkspacePaneEvent) => {
    if (event.type !== 'streaming_complete') return;
    if (!requestIdRef.current) return;
    if (event.streamId !== requestIdRef.current) return;
    setInFlight(false);
  });

  return { onClick, isInFlight, lastRequestId };
}

// ---------------------------------------------------------------------------
// Sub-component: SummarizeOnlyButton (rendered per file card + inline header)
// ---------------------------------------------------------------------------

interface SummarizeOnlyButtonProps {
  fileId: string;
  fileName: string;
  sessionId: string;
  dispatch: DispatchPaneEvent;
  /**
   * When `true`, render the button as icon-only with a `Tooltip` (compact
   * mode used in the per-card layout where horizontal space is constrained).
   * When `false`, render label + icon (used in the single-file action bar
   * where the button is the focal action).
   */
  compact: boolean;
  styles: ReturnType<typeof useStyles>;
  /**
   * Optional sibling-disabled mode — used by the multi-file card to disable
   * the button when there is no `sessionId` (the host has not yet bound the
   * widget to a session). Mirrors the menu's `disabledActions` discipline.
   */
  disabled?: boolean;
}

const SummarizeOnlyButton: React.FC<SummarizeOnlyButtonProps> = ({
  fileId,
  fileName,
  sessionId,
  dispatch,
  compact,
  styles,
  disabled,
}) => {
  const { onClick, isInFlight } = useSummarizeOnly(fileId, sessionId, fileName, dispatch);

  const ariaLabel = `Summarize ${fileName} only`;
  const isDisabled = disabled === true || isInFlight;

  // Icon-only compact variant (per-card surface).
  if (compact) {
    return (
      <Tooltip content="Summarize this file only" relationship="label" withArrow>
        <Button
          className={styles.summarizeButton}
          appearance="subtle"
          size="small"
          aria-label={ariaLabel}
          disabled={isDisabled}
          icon={isInFlight ? <Spinner size="extra-tiny" /> : <SparkleRegular />}
          onClick={(e: React.MouseEvent) => {
            // Stop the card click handler from firing — selecting the file
            // is NOT the intent of the summarize affordance.
            e.stopPropagation();
            onClick();
          }}
          data-testid="summarize-only-button-compact"
          data-file-id={fileId}
          data-in-flight={isInFlight ? 'true' : 'false'}
        />
      </Tooltip>
    );
  }

  // Label + icon variant (single-file action bar surface).
  return (
    <Button
      className={styles.summarizeButton}
      appearance="primary"
      size="small"
      aria-label={ariaLabel}
      disabled={isDisabled}
      icon={isInFlight ? <Spinner size="extra-tiny" /> : <SparkleRegular />}
      onClick={onClick}
      data-testid="summarize-only-button"
      data-file-id={fileId}
      data-in-flight={isInFlight ? 'true' : 'false'}
    >
      Summarize this only
    </Button>
  );
};

// ---------------------------------------------------------------------------
// Sub-component: FilePreviewCard (multi-file mode)
// ---------------------------------------------------------------------------

interface FilePreviewCardProps {
  file: FilePreviewContextFile;
  isSelected: boolean;
  onSelect: (fileId: string) => void;
  onAction: (action: DocumentRowAction, fileId: string) => void;
  disabledActions: DocumentRowAction[];
  styles: ReturnType<typeof useStyles>;
  /**
   * PaneEventBus dispatcher forwarded to the per-card "Summarize this only"
   * button (task 021 / D2-12). Passed down from the parent widget to keep
   * the card stateless + trivially testable.
   */
  dispatch: DispatchPaneEvent;
  /**
   * Active session id. When absent (empty string), the "Summarize this only"
   * button is hidden — there is no session anchor to bind the new tab to.
   */
  sessionId: string;
}

/**
 * Compact card for the multi-file list. Renders an icon, the filename, a
 * small meta row (type + size) and the 3-dot DocumentRowMenu. Click on
 * the card body selects the file; clicks on the menu trigger are stopped
 * by DocumentRowMenu so they do not bubble to the card click handler.
 *
 * Composable — task 021 (D2-12) can extend this with a per-card
 * "Summarize this only" affordance without rewriting the markup.
 */
const FilePreviewCard: React.FC<FilePreviewCardProps> = ({
  file,
  isSelected,
  onSelect,
  onAction,
  disabledActions,
  styles,
  dispatch,
  sessionId,
}) => {
  const handleClick = useCallback(() => {
    onSelect(file.fileId);
  }, [file.fileId, onSelect]);

  const handleKeyDown = useCallback(
    (e: React.KeyboardEvent) => {
      if (e.key === 'Enter' || e.key === ' ') {
        e.preventDefault();
        onSelect(file.fileId);
      }
    },
    [file.fileId, onSelect]
  );

  const handleAction = useCallback(
    (action: DocumentRowAction) => {
      onAction(action, file.fileId);
    },
    [file.fileId, onAction]
  );

  const sizeLabel = formatFileSize(file.sizeBytes);
  const typeLabel = file.documentType ?? file.contentType ?? null;

  return (
    <div
      className={mergeClasses(styles.card, isSelected && styles.cardSelected)}
      onClick={handleClick}
      onKeyDown={handleKeyDown}
      role="button"
      tabIndex={0}
      aria-pressed={isSelected}
      aria-label={`${file.fileName}${isSelected ? ', selected' : ''}`}
      data-file-id={file.fileId}
      data-testid="file-preview-card"
    >
      <span className={styles.cardIcon} aria-hidden="true">
        {pickFileIcon(file.contentType)}
      </span>
      <div className={styles.cardBody}>
        <Text className={styles.cardTitle} title={file.fileName}>
          {file.fileName}
        </Text>
        <div className={styles.cardMetaRow}>
          {typeLabel ? (
            <Tag appearance="filled" shape="rounded" size="small">
              {typeLabel}
            </Tag>
          ) : null}
          {sizeLabel ? <Text className={styles.cardMetaText}>{sizeLabel}</Text> : null}
        </div>
      </div>
      <div className={styles.cardActionRow}>
        {/* Per-card "Summarize this only" affordance (task 021 / D2-12).
            Hidden when no session anchor exists (no dispatch target). */}
        {sessionId !== '' && (
          <SummarizeOnlyButton
            fileId={file.fileId}
            fileName={file.fileName}
            sessionId={sessionId}
            dispatch={dispatch}
            compact={true}
            styles={styles}
          />
        )}
        <DocumentRowMenu
          document={{ id: file.fileId, name: file.fileName, documentType: file.documentType }}
          onAction={handleAction}
          disabledActions={disabledActions}
        />
      </div>
    </div>
  );
};

// ---------------------------------------------------------------------------
// Sub-components: Skeleton + Empty
// ---------------------------------------------------------------------------

const FilePreviewSkeleton: React.FC<{ styles: ReturnType<typeof useStyles> }> = ({ styles }) => (
  <div className={styles.skeletonRoot} aria-busy="true" aria-label="Loading files">
    {Array.from({ length: 3 }, (_, i) => (
      <div key={i} className={styles.skeletonCard}>
        <Skeleton>
          <SkeletonItem size={12} style={{ width: '60%' }} />
          <SkeletonItem size={8} style={{ width: '40%', marginTop: tokens.spacingVerticalXS }} />
        </Skeleton>
      </div>
    ))}
  </div>
);

const FilePreviewEmpty: React.FC<{ styles: ReturnType<typeof useStyles> }> = ({ styles }) => (
  <div className={styles.centerState} role="status" aria-label="No files attached">
    <DocumentRegular className={styles.emptyStateIcon} />
    <Text className={styles.emptyStateTitle}>No files attached</Text>
    <Text className={styles.emptyStateBody}>
      Files you upload into this chat session will appear here for inline preview.
    </Text>
  </div>
);

// ---------------------------------------------------------------------------
// FilePreviewContextWidget
// ---------------------------------------------------------------------------

/**
 * FilePreviewContextWidget — Context-pane file-preview widget for R5's
 * chat-driven Summarize vertical slice.
 *
 * @see ContextWidgetProps
 * @see FilePreviewContextData
 */
const FilePreviewContextWidget: React.FC<FilePreviewContextWidgetProps> = ({
  data,
  isLoading,
  error,
  className,
  onFetchPreviewUrl,
  onOpenFile,
  onOpenRecord,
  onEmailDocument,
  onCopyLink,
  onFileAction,
  disabledActions: disabledActionsProp,
}) => {
  const styles = useStyles();
  const dispatch = useDispatchPaneEvent();

  const files = data?.files ?? [];
  const sessionId = data?.sessionId ?? '';
  const isMulti = files.length > 1;

  // Active file selection — host's `activeFileId` seeds the initial value;
  // subsequent user clicks override locally. We re-seed when the host
  // changes the prop OR when the files list changes shape.
  const [selectedFileId, setSelectedFileId] = useState<string | null>(() => {
    if (data?.activeFileId && files.some(f => f.fileId === data.activeFileId)) {
      return data.activeFileId;
    }
    return files.length > 0 ? files[0].fileId : null;
  });

  // Re-seed when host changes activeFileId or when the file list shape
  // changes such that the currently-selected id is no longer present.
  React.useEffect(() => {
    const hostActive = data?.activeFileId;
    if (hostActive && files.some(f => f.fileId === hostActive) && hostActive !== selectedFileId) {
      setSelectedFileId(hostActive);
      return;
    }
    if (selectedFileId && !files.some(f => f.fileId === selectedFileId)) {
      setSelectedFileId(files.length > 0 ? files[0].fileId : null);
    } else if (!selectedFileId && files.length > 0) {
      setSelectedFileId(files[0].fileId);
    }
    // selectedFileId is intentionally NOT in the dep list — including it
    // would cause an oscillation when the user picks a card. We only want
    // to re-seed when the host or the file list changes shape.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [data?.activeFileId, files]);

  const activeFile = useMemo(
    () => files.find(f => f.fileId === selectedFileId) ?? files[0] ?? null,
    [files, selectedFileId]
  );

  // Compute the effective per-card disabled action set. We ALWAYS hide
  // `preview` (the widget IS the preview surface). Hosts may pass an
  // explicit list to override; otherwise the default also hides `rename`
  // when no host handler is wired (defensive — `rename` has no destination
  // without a host callback, so showing it would be misleading).
  const effectiveDisabledActions = useMemo<DocumentRowAction[]>(() => {
    if (disabledActionsProp) {
      // Ensure 'preview' is always hidden — defensive.
      return disabledActionsProp.includes('preview') ? [...disabledActionsProp] : [...disabledActionsProp, 'preview'];
    }
    const defaults: DocumentRowAction[] = ['preview'];
    // `aiSummary` is now handled IN-WIDGET (task 021 / D2-12) via
    // `dispatchSummarizeOnly`, regardless of whether the host wires
    // `onFileAction`. It stays visible whenever a session anchor exists.
    // When no session anchor is present, the menu item is hidden — there
    // is no dispatch target and an inert menu item would be misleading.
    if (sessionId === '') {
      defaults.push('aiSummary');
    }
    if (!onFileAction) {
      // No host handler → hide actions whose destinations require host
      // routing. `toggleWorkspace` is handled in-widget so it stays
      // visible; `findSimilar` needs host glue.
      defaults.push('findSimilar', 'rename');
    } else {
      // Host handler available — only hide `rename` defensively (it's
      // commonly host-unimplemented). Hosts can re-enable via prop.
      defaults.push('rename');
    }
    return defaults;
  }, [disabledActionsProp, onFileAction, sessionId]);

  // -------------------------------------------------------------------------
  // Selection + event dispatch
  // -------------------------------------------------------------------------

  const handleCardSelect = useCallback(
    (fileId: string) => {
      if (fileId === selectedFileId) return;
      setSelectedFileId(fileId);
      const file = files.find(f => f.fileId === fileId);
      // Dispatch additive `context.file_selected` event (R5 task 016 / D2-06).
      // Existing `context` subscribers ignore unknown discriminants.
      dispatch('context', {
        type: 'file_selected',
        selectedFileId: fileId,
        selectionSource: 'context-card',
        // Carry the file id as a contextType hint so subscribers that filter
        // by entity type can route correctly.
        contextType: 'chat-session-file',
        contextData: file ? { fileId: file.fileId, fileName: file.fileName } : { fileId },
      });
    },
    [dispatch, files, selectedFileId]
  );

  // -------------------------------------------------------------------------
  // Per-file 3-dot menu action dispatch
  // -------------------------------------------------------------------------

  const handleFileAction = useCallback(
    (action: DocumentRowAction, fileId: string) => {
      switch (action) {
        case 'toggleWorkspace': {
          // Reuse the canonical Workspace-pane mount path: dispatch
          // `workspace.widget_load` with the document-viewer widget type
          // so the file opens as a new workspace tab. Mirrors the R4
          // task 042 Assistant → Workspace demo path.
          const file = files.find(f => f.fileId === fileId);
          dispatch('workspace', {
            type: 'widget_load',
            widgetType: 'document-viewer',
            displayName: file?.fileName ?? 'Document Viewer',
            widgetData: file
              ? {
                  fileId: file.fileId,
                  filename: file.fileName,
                  contentType: file.contentType ?? '',
                  sizeBytes: file.sizeBytes ?? undefined,
                }
              : { fileId },
          });
          // Also bubble to the host so it can update its own UI state
          // (e.g. toggle a pin icon). No-op when no handler.
          onFileAction?.(action, fileId);
          return;
        }
        case 'aiSummary': {
          // FR-05 + FR-08 dual-surface mandate: the `aiSummary` menu action
          // and the per-card / inline "Summarize this only" Button MUST
          // route through the same dispatch shape so behaviour is
          // indistinguishable across surfaces (task 021 / D2-12).
          //
          // When no session anchor is present we fall through to the host
          // callback — the host may render its own surface (e.g. an
          // upload-files prompt) instead.
          const file = files.find(f => f.fileId === fileId);
          if (sessionId !== '' && file !== undefined) {
            dispatchSummarizeOnly(file.fileId, sessionId, file.fileName, dispatch);
          }
          // Always bubble to the host so it can attach side-effects (e.g.
          // analytics, toast). No-op when no handler.
          onFileAction?.(action, fileId);
          return;
        }
        case 'findSimilar':
        case 'openFile':
        case 'openRecord':
        case 'email':
        case 'copyLink':
        case 'download':
        case 'pinToTop':
        case 'delete':
        case 'rename':
        case 'preview':
        default: {
          // All other actions bubble to the host. The host owns routing
          // (Summarize playbook for aiSummary, RAG flow for findSimilar,
          // etc.) so the widget stays library-pure and side-effect-free.
          onFileAction?.(action, fileId);
          return;
        }
      }
    },
    [dispatch, files, onFileAction, sessionId]
  );

  // -------------------------------------------------------------------------
  // Active-file renderer plumbing (RichFilePreview integration)
  // -------------------------------------------------------------------------

  // The extracted RichFilePreview takes a closure-based `fetchPreviewUrl`.
  // We bind the host-supplied per-file fetcher to the active file id so
  // the renderer can re-fetch when `documentId` changes (multi-file
  // navigation).
  const fetchActivePreviewUrl = useCallback((): Promise<string | null> => {
    if (!activeFile || !onFetchPreviewUrl) return Promise.resolve(null);
    return onFetchPreviewUrl(activeFile.fileId);
  }, [activeFile, onFetchPreviewUrl]);

  // Bind per-action callbacks to the active file id.
  const handleActiveOpenFile = useCallback(
    (mode: 'desktop' | 'web') => {
      if (!activeFile) return;
      onOpenFile?.(activeFile.fileId, mode);
    },
    [activeFile, onOpenFile]
  );
  const handleActiveOpenRecord = useCallback(() => {
    if (!activeFile) return;
    onOpenRecord?.(activeFile.fileId);
  }, [activeFile, onOpenRecord]);
  const handleActiveEmail = useCallback(() => {
    if (!activeFile) return;
    onEmailDocument?.(activeFile.fileId);
  }, [activeFile, onEmailDocument]);
  const handleActiveCopyLink = useCallback(() => {
    if (!activeFile) return;
    onCopyLink?.(activeFile.fileId);
  }, [activeFile, onCopyLink]);

  // The renderer dispatches its own onToggleWorkspace etc. via its menu.
  // For active-file menu actions we route through `handleFileAction` so
  // the widget's dispatch logic (toggleWorkspace → widget_load) stays
  // centralized. We override the renderer's disabledActions to expose the
  // same effective set as the card-level menu.
  const handleActiveToggleWorkspace = useCallback(() => {
    if (!activeFile) return;
    handleFileAction('toggleWorkspace', activeFile.fileId);
  }, [activeFile, handleFileAction]);
  const handleActiveFindSimilar = useCallback(() => {
    if (!activeFile) return;
    handleFileAction('findSimilar', activeFile.fileId);
  }, [activeFile, handleFileAction]);

  // For the renderer's `disabledActions` prop we want the SAME effective
  // set as the per-card menu, EXCEPT we never want to hide
  // `toggleWorkspace` at the active-file surface (toggling makes sense
  // there). The renderer's default also auto-hides `aiSummary` because
  // its native popover entry point is the sparkle column — we want the
  // menu item visible so the host can route it.
  const activeRendererDisabledActions = useMemo<DocumentRowAction[]>(() => {
    const fromCard = effectiveDisabledActions.filter(a => a !== 'toggleWorkspace');
    // Always include 'preview' (defensive).
    if (!fromCard.includes('preview')) fromCard.push('preview');
    return fromCard;
  }, [effectiveDisabledActions]);

  // -------------------------------------------------------------------------
  // Render
  // -------------------------------------------------------------------------

  // Loading state — render skeleton (mirrors PlaybookGalleryWidget pattern).
  if (isLoading) {
    return (
      <div className={mergeClasses(styles.root, className)} role="region" aria-label="File preview">
        <FilePreviewSkeleton styles={styles} />
      </div>
    );
  }

  // Error state — render MessageBar inline (never a blank pane).
  if (error) {
    return (
      <div className={mergeClasses(styles.root, className)} role="region" aria-label="File preview">
        <MessageBar intent="error" className={styles.errorBar} data-testid="file-preview-error">
          <MessageBarBody>
            <MessageBarTitle>Unable to load files</MessageBarTitle>
            {error}
          </MessageBarBody>
        </MessageBar>
      </div>
    );
  }

  // Empty state — never a blank pane.
  if (!activeFile || files.length === 0) {
    return (
      <div className={mergeClasses(styles.root, className)} role="region" aria-label="File preview">
        <FilePreviewEmpty styles={styles} />
      </div>
    );
  }

  return (
    <div className={mergeClasses(styles.root, className)} role="region" aria-label="File preview">
      {/* Multi-file mode — show a file card list above the active preview. */}
      {isMulti && (
        <>
          <div className={styles.header}>
            <Text className={styles.headerTitle}>Session Files</Text>
            <Text className={styles.headerSubtitle}>{files.length} files attached. Click a file to preview it.</Text>
          </div>
          <div className={styles.cardList} role="list" aria-label="Session files">
            {files.map(file => (
              <FilePreviewCard
                key={file.fileId}
                file={file}
                isSelected={file.fileId === activeFile.fileId}
                onSelect={handleCardSelect}
                onAction={handleFileAction}
                disabledActions={effectiveDisabledActions}
                styles={styles}
                dispatch={dispatch}
                sessionId={sessionId}
              />
            ))}
          </div>
        </>
      )}

      {/* Single-file action bar (task 021 / D2-12). Rendered above the
          RichFilePreview when there is exactly one file. The bar is the
          PRIMARY surface for the "Summarize this only" affordance in
          single-file mode where there is no per-card surface; in multi-file
          mode the bar still renders above the ACTIVE file so the per-active
          file action is unambiguous. Hidden when no session anchor exists. */}
      {sessionId !== '' && (
        <div className={styles.singleFileActionBar} data-testid="single-file-action-bar">
          <Text className={styles.singleFileActionLabel}>
            {isMulti ? `Active file:` : `File:`} <strong>{activeFile.fileName}</strong>
          </Text>
          <SummarizeOnlyButton
            fileId={activeFile.fileId}
            fileName={activeFile.fileName}
            sessionId={sessionId}
            dispatch={dispatch}
            compact={false}
            styles={styles}
          />
        </div>
      )}

      {/* Active-file preview — extracted RichFilePreview renderer (task 013). */}
      <div className={styles.previewSlot} data-testid="file-preview-active-slot">
        <RichFilePreview
          // documentId change triggers the renderer's effect to refetch
          // the preview URL and reset iframe state — exactly what we want
          // when the user clicks a different card in multi-file mode.
          documentId={activeFile.fileId}
          documentName={activeFile.fileName}
          documentType={activeFile.documentType}
          createdBy={activeFile.createdBy}
          createdAt={activeFile.createdOn}
          fileSize={activeFile.sizeBytes}
          fetchPreviewUrl={fetchActivePreviewUrl}
          onOpenFile={handleActiveOpenFile}
          onOpenRecord={handleActiveOpenRecord}
          onEmailDocument={handleActiveEmail}
          onCopyLink={handleActiveCopyLink}
          onToggleWorkspace={handleActiveToggleWorkspace}
          onFindSimilar={handleActiveFindSimilar}
          disabledActions={activeRendererDisabledActions}
          // Single-file mode hides prev/next; multi-file mode also hides
          // prev/next inside the renderer because we already render a
          // card list above for selection. The renderer's nav is opt-in
          // via navigationTotal — we omit it here intentionally.
        />
      </div>
    </div>
  );
};

FilePreviewContextWidget.displayName = 'FilePreviewContextWidget';

export default FilePreviewContextWidget;
