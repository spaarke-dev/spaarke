/**
 * EditorPanel Component
 *
 * Left panel of the AnalysisWorkspace 2-panel layout. Hosts the RichTextEditor
 * (Lexical-based) from @spaarke/ui-components for viewing and editing analysis
 * output. This is the streaming write target where SprkChat writes token-by-token.
 *
 * Task 062: Toolbar with Save, Export, Copy, Undo/Redo (replaces PH-061-A).
 * Task 064/010: Selection broadcast now uses React context (useSelectionBroadcast removed).
 * Task 065: Analysis loading and content population from BFF API.
 * Task 031: InlineAiToolbar overlay — appears on text selection; dispatches inline
 *           actions to SprkChat (chat type) or DiffReviewPanel (diff type).
 *
 * @see ADR-012 - Shared component library (import from @spaarke/ui-components)
 * @see ADR-021 - Fluent UI v9 design system
 */

import { forwardRef, useCallback, useEffect, useRef, type RefObject } from 'react';
import { makeStyles, Spinner, Text, Button, tokens } from '@fluentui/react-components';
import { Play20Regular } from '@fluentui/react-icons';
import { RichTextEditor, InlineAiToolbar } from '@spaarke/ui-components';
import type { RichTextEditorRef, InlineAiAction } from '@spaarke/ui-components';
import { AnalysisToolbar } from './AnalysisToolbar';
import type { SaveState, ExportState, ExportFormat } from '../types';
import { useInlineAiToolbarState } from '../hooks/useInlineAiToolbar';
import { useDocumentInsert } from '../hooks/useDocumentInsert';
import { useAnalysisAi } from '../context/AnalysisAiContext';

// ---------------------------------------------------------------------------
// Props
// ---------------------------------------------------------------------------

export interface EditorPanelProps {
  /** HTML content to display in the editor */
  value: string;
  /** Callback when editor content changes (HTML string) */
  onChange: (html: string) => void;
  /** Whether the editor is in read-only mode */
  readOnly?: boolean;
  /** Placeholder text when the editor is empty */
  placeholder?: string;
  /** Whether the analysis content is currently loading */
  isLoading?: boolean;
  /** Whether analysis is currently streaming (editor stays visible) */
  isStreaming?: boolean;
  /** Progress message during streaming */
  streamingMessage?: string;

  // ---- Toolbar props (task 062) ----
  /** Current auto-save state */
  saveState?: SaveState;
  /** Force save callback (Ctrl+S) */
  onForceSave?: () => void;
  /** Save error message */
  saveError?: string | null;
  /** Current export state */
  exportState?: ExportState;
  /** Export callback */
  onExport?: (format: ExportFormat) => void;
  /** Undo callback */
  onUndo?: () => void;
  /** Redo callback */
  onRedo?: () => void;
  /** Whether undo is available */
  canUndo?: boolean;
  /** Whether redo is available */
  canRedo?: boolean;
  /** History stack length */
  historyLength?: number;

  // ---- Run Analysis (moved from top toolbar) ----
  /** Callback to trigger analysis execution */
  onRunAnalysis?: () => void;
  /** Whether analysis can be triggered (has playbook/action configured) */
  canRunAnalysis?: boolean;

  // ---- Inline AI Toolbar props (task 031) ----
  /**
   * Analysis session ID — passed to useInlineAiToolbarState so dispatched
   * inline actions carry analysis context (used by SprkChatPane to correlate
   * actions with the active session). Optional; toolbar works without it.
   */
  analysisId?: string;
  /**
   * Callback invoked when a diff-type inline action is triggered (Simplify, Expand).
   * EditorPanel calls this so the parent (App.tsx) can open the DiffReviewPanel
   * via its useDiffReview hook. The selected text is passed as the initial content
   * for the diff operation.
   *
   * NOTE: Diff actions are also broadcast to SprkChat via BroadcastChannel (so
   * they appear in chat history). This callback is in ADDITION to that dispatch.
   *
   * spec-FR-05: Diff-type inline actions MUST open the existing DiffReviewPanel.
   */
  onDiffAction?: (selectedText: string) => void;
}

// ---------------------------------------------------------------------------
// Styles
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  root: {
    display: 'flex',
    flexDirection: 'column',
    height: '100%',
    overflow: 'hidden',
    backgroundColor: tokens.colorNeutralBackground1,
  },
  header: {
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'space-between',
    paddingTop: tokens.spacingVerticalS,
    paddingBottom: tokens.spacingVerticalS,
    paddingLeft: tokens.spacingHorizontalM,
    paddingRight: tokens.spacingHorizontalM,
    borderBottom: `1px solid ${tokens.colorNeutralStroke1}`,
    backgroundColor: tokens.colorNeutralBackground3,
    minHeight: '40px',
    flexShrink: 0,
  },
  headerTitle: {
    display: 'flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalS,
  },
  toolbar: {
    display: 'flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalXS,
  },
  editorContainer: {
    flex: 1,
    overflow: 'auto',
    padding: tokens.spacingHorizontalM,
    // MUST be position:relative so the absolutely-positioned InlineAiToolbar
    // overlay resolves its top/left coordinates relative to this container.
    // (spec-2B, InlineAiToolbar component requirement)
    position: 'relative',
  },
  loadingContainer: {
    display: 'flex',
    flexDirection: 'column',
    alignItems: 'center',
    justifyContent: 'center',
    flex: 1,
    gap: tokens.spacingVerticalM,
  },
  streamingBar: {
    display: 'flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalS,
    paddingTop: tokens.spacingVerticalXS,
    paddingBottom: tokens.spacingVerticalXS,
    paddingLeft: tokens.spacingHorizontalM,
    paddingRight: tokens.spacingHorizontalM,
    backgroundColor: tokens.colorNeutralBackground4,
    borderBottom: `1px solid ${tokens.colorNeutralStroke1}`,
    flexShrink: 0,
  },
});

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

export const EditorPanel = forwardRef<RichTextEditorRef, EditorPanelProps>(function EditorPanel(
  {
    value,
    onChange,
    readOnly = false,
    placeholder = 'Analysis output will appear here...',
    isLoading = false,
    isStreaming = false,
    streamingMessage = '',
    // Toolbar props (task 062)
    saveState = 'idle',
    onForceSave,
    saveError,
    exportState = 'idle',
    onExport,
    onUndo,
    onRedo,
    canUndo = false,
    canRedo = false,
    historyLength = 0,
    // Run Analysis
    onRunAnalysis,
    canRunAnalysis = false,
    // Inline AI Toolbar props (task 031)
    analysisId,
    onDiffAction,
  },
  ref
): JSX.Element {
  const styles = useStyles();

  // ── Task 005: Wire editorRef from AnalysisAiContext ──────────────────
  //
  // Expose the Lexical editor's insert and getContent methods through the
  // shared AnalysisAiContext editorRef. This enables the full callback chain:
  //   ChatPanel → SprkChat → onInsertToEditor → editorRef.current.insert()
  //     → RichTextEditorRef.insertAtCursor() → Lexical editor
  //
  // insertAtCursor uses Lexical's $getSelection() internally:
  //   - Range selection (non-collapsed) → replaces selected text
  //   - Cursor only (collapsed) → inserts at cursor position
  //   - No selection → appends to end of document
  //
  // The discrete:true flag in the Lexical update ensures each insert creates
  // its own undo history entry (Ctrl+Z support preserved).
  const { editorRef: contextEditorRef, setEditorSelection } = useAnalysisAi();

  // Stable ref to latest `value` prop so the getContent fallback never
  // causes the useEffect to re-run (value changes on every keystroke).
  const valueRef = useRef(value);
  useEffect(() => {
    valueRef.current = value;
  }, [value]);

  useEffect(() => {
    contextEditorRef.current = {
      insert: (content: string) => {
        if (ref && typeof ref === 'object' && ref.current) {
          ref.current.insertAtCursor(content, 'html');
        }
      },
      getContent: () => {
        if (ref && typeof ref === 'object' && ref.current) {
          return ref.current.getHtml();
        }
        return valueRef.current;
      },
    };

    return () => {
      // Clean up on unmount so stale ref doesn't get called
      contextEditorRef.current = null;
    };
  }, [contextEditorRef, ref]);

  // Determine if toolbar should render (all required callbacks provided)
  const hasToolbar = !!(onForceSave && onExport && onUndo && onRedo);

  /**
   * Get current editor HTML via the ref. Used by the Copy button.
   */
  const getEditorHtml = (): string => {
    if (ref && typeof ref === 'object' && ref.current) {
      return ref.current.getHtml();
    }
    return value;
  };

  // ── Task 031: Inline AI Toolbar ─────────────────────────────────────
  //
  // Ref scoping: editorContainerRef is attached to the scrollable div that
  // wraps the RichTextEditor. The InlineAiToolbar is rendered as a sibling
  // child inside this div, and uses absolute positioning relative to it
  // (the div has position:relative via makeStyles). The shared hook
  // useInlineAiToolbarState listens for selectionchange events scoped to
  // this container and returns top/left pixel offsets relative to the same
  // container, so the toolbar floats directly above the selection.
  //
  // CRITICAL: onMouseDown (NOT onClick) is used by InlineAiActions to prevent
  // the text selection from collapsing before the action fires.
  // DO NOT change to onClick — this would lose the selection.
  const editorContainerRef = useRef<HTMLDivElement>(null);

  const {
    visible: toolbarVisible,
    position: toolbarPosition,
    selectedText,
    actions: toolbarActions,
    onAction: dispatchInlineAction,
  } = useInlineAiToolbarState({ editorContainerRef, analysisId });

  // ── Task 006: Sync editor selection to AnalysisAiContext ────────────
  //
  // Debounce the selectedText value from the inline toolbar hook before
  // writing it to context. This prevents excessive re-renders during
  // rapid drag-select operations while keeping the context up-to-date
  // for SprkChat's highlight-refine feature (editorSelection prop).
  //
  // The 150ms debounce keeps context updates responsive without causing
  // excessive re-renders during rapid drag-select operations.
  useEffect(() => {
    const timer = setTimeout(() => {
      setEditorSelection(selectedText);
    }, 150);

    return () => {
      clearTimeout(timer);
    };
  }, [selectedText, setEditorSelection]);

  // ── Task 051: document_insert BroadcastChannel handler ──────────────
  //
  // Subscribes to 'sprk-document-insert' BroadcastChannel events dispatched
  // by SprkChat when the user clicks the "Insert" button on an AI response.
  // Disabled when the editor is in read-only mode (loading/streaming) to
  // prevent inserts conflicting with active streaming operations.
  //
  // The `ref` here is the public RichTextEditorRef forwarded from the parent
  // (App.tsx) via useRef<RichTextEditorRef>(null). useDocumentInsert calls
  // ref.current.insertAtCursor() which delegates to the Lexical update API
  // with discrete:true — ensuring full undo support via Ctrl+Z.
  useDocumentInsert({
    editorRef: ref as RefObject<RichTextEditorRef | null>,
    enabled: !readOnly && !isStreaming,
  });

  /**
   * Handle inline AI action from the toolbar.
   *
   * For 'diff' actions (Simplify, Expand): notify the parent to open
   * DiffReviewPanel AND dispatch to SprkChat via BroadcastChannel so the
   * action appears in chat history (spec-FR-04, spec-FR-05).
   *
   * For 'chat' actions (Summarize, Fact-Check, Ask): dispatch to SprkChat
   * only via BroadcastChannel.
   */
  const handleInlineAction = useCallback(
    (action: InlineAiAction, text: string): void => {
      if (action.actionType === 'diff' && onDiffAction) {
        // Open DiffReviewPanel with the selected text as the initial content.
        // The AI will propose a revised version when SprkChat processes the action.
        onDiffAction(text);
      }

      // Always dispatch to SprkChat via BroadcastChannel (spec-FR-04).
      // For diff actions this causes the action to appear in chat history;
      // for chat actions this triggers the streaming response.
      dispatchInlineAction(action, text);
    },
    [onDiffAction, dispatchInlineAction]
  );

  return (
    <div className={styles.root}>
      {/* Panel header with toolbar */}
      <div className={styles.header}>
        <div className={styles.headerTitle}>
          <Text weight="semibold">ANALYSIS OUTPUT</Text>
        </div>
        <div className={styles.toolbar}>
          {onRunAnalysis && (
            <Button
              appearance="subtle"
              icon={isStreaming ? <Spinner size="tiny" /> : <Play20Regular />}
              onClick={onRunAnalysis}
              disabled={isStreaming || !canRunAnalysis}
              title={isStreaming ? 'Running analysis...' : 'Run Analysis'}
              aria-label="Run Analysis"
            />
          )}
          {hasToolbar ? (
            <AnalysisToolbar
              saveState={saveState}
              onForceSave={onForceSave}
              saveError={saveError}
              exportState={exportState}
              onExport={onExport}
              getEditorHtml={getEditorHtml}
              onUndo={onUndo}
              onRedo={onRedo}
              canUndo={canUndo}
              canRedo={canRedo}
              historyLength={historyLength}
            />
          ) : null}
        </div>
      </div>

      {/* Loading state (task 065) */}
      {isLoading ? (
        <div className={styles.loadingContainer}>
          <Spinner size="medium" label="Loading analysis..." />
        </div>
      ) : (
        <>
          {/* Streaming progress indicator (visible during execution) */}
          {isStreaming && (
            <div className={styles.streamingBar}>
              <Spinner size="tiny" />
              <Text size={200}>{streamingMessage || 'Running analysis...'}</Text>
            </div>
          )}
          {/*
           * Editor area — position:relative (via makeStyles) so that the
           * absolutely-positioned InlineAiToolbar overlay resolves its
           * top/left coordinates correctly (spec-2B).
           */}
          <div ref={editorContainerRef} className={styles.editorContainer}>
            <RichTextEditor
              ref={ref}
              value={value}
              onChange={onChange}
              readOnly={readOnly || isStreaming}
              placeholder={placeholder}
            />
            {/*
             * InlineAiToolbar stays mounted (display:none when hidden) to
             * avoid layout thrash on every selection change. It is rendered
             * INSIDE the position:relative container so absolute positioning
             * is relative to the editor scroll area.
             *
             * CRITICAL: InlineAiActions uses onMouseDown + preventDefault()
             * to fire actions WITHOUT collapsing the text selection. Do NOT
             * move this outside the container or change to onClick.
             */}
            <InlineAiToolbar
              visible={toolbarVisible}
              position={toolbarPosition}
              actions={toolbarActions}
              onAction={action => handleInlineAction(action, selectedText)}
            />
          </div>
        </>
      )}
    </div>
  );
});
