/**
 * DocumentStreamBridge - Self-contained integration module for SprkChatBridge
 * document streaming in the AnalysisWorkspace Code Page.
 *
 * This component encapsulates the complete bridge-to-editor streaming pipeline:
 *   SprkChatBridge → useDocumentStreaming → RichTextEditor ref
 *
 * It is designed to be plugged into the AnalysisWorkspace layout without
 * modifying App.tsx directly. Task 061 (2-panel layout) may be running
 * concurrently — this module is independent and composable.
 *
 * Usage:
 * ```tsx
 * const editorRef = useRef<RichTextEditorRef>(null);
 *
 * <DocumentStreamBridge
 *     context={analysisId}
 *     editorRef={editorRef}
 *     enabled={!!analysisId}
 * />
 * <RichTextEditor ref={editorRef} ... />
 * ```
 *
 * The component renders only the StreamingIndicator overlay — all streaming
 * logic is encapsulated in useDocumentStreaming. The overlay animates in/out
 * based on streaming state.
 *
 * Keyboard support: Escape key cancels active streaming.
 *
 * @see useDocumentStreaming - Integration hook
 * @see StreamingIndicator - Visual streaming state
 * @see ADR-012 - Shared Component Library
 */

import { useEffect, useCallback } from 'react';
import type { RichTextEditorRef } from '@spaarke/ui-components/components/RichTextEditor';
import { useDocumentStreaming, type UseDocumentStreamingResult } from '../hooks/useDocumentStreaming';
import { useAnalysisAi } from '../context/AnalysisAiContext';
import { StreamingIndicator } from './StreamingIndicator';

// ─────────────────────────────────────────────────────────────────────────────
// Types
// ─────────────────────────────────────────────────────────────────────────────

export interface DocumentStreamBridgeProps {
  /**
   * Context identifier for the BroadcastChannel (typically the analysis session ID).
   * Channel becomes: sprk-workspace-{context}
   */
  context: string;

  /**
   * Ref to the RichTextEditor providing streaming + HTML APIs.
   */
  editorRef: React.RefObject<RichTextEditorRef | null>;

  /**
   * Whether the bridge should be active. Defaults to true.
   * Set to false when editor is not mounted or context is not ready.
   */
  enabled?: boolean;

  /**
   * Optional callback to receive streaming state changes.
   * Useful for parent components that need to react to streaming state
   * (e.g., disable other controls during streaming).
   */
  onStreamingStateChange?: (state: DocumentStreamingState) => void;
}

/** Streaming state exposed to parent components */
export interface DocumentStreamingState {
  isStreaming: boolean;
  isReplacing: boolean;
  tokenCount: number;
  operationId: string | null;
}

// ─────────────────────────────────────────────────────────────────────────────
// Component
// ─────────────────────────────────────────────────────────────────────────────

export function DocumentStreamBridge({
  context,
  editorRef,
  enabled = true,
  onStreamingStateChange,
}: DocumentStreamBridgeProps): JSX.Element {
  const streaming: UseDocumentStreamingResult = useDocumentStreaming({
    context,
    editorRef,
    enabled,
  });

  // ── Task 007: Merge context-based streaming state ──────────────────────
  //
  // In the unified workspace, document stream events bypass BroadcastChannel
  // and flow through AnalysisAiContext callbacks directly to the editor ref.
  // The streaming state is tracked in AnalysisAiContext.streamingState.
  // We merge both sources so the StreamingIndicator works regardless of
  // which path is active (BroadcastChannel or context-direct).
  const { streamingState: contextStreamingState } = useAnalysisAi();

  // Merge: either source being active means we're streaming
  const mergedIsStreaming = streaming.isStreaming || contextStreamingState.isStreaming;
  const mergedTokenCount = streaming.isStreaming
    ? streaming.tokenCount
    : contextStreamingState.tokenCount;

  // ─────────────────────────────────────────────────────────────────────
  // Notify parent of streaming state changes
  // ─────────────────────────────────────────────────────────────────────

  useEffect(() => {
    onStreamingStateChange?.({
      isStreaming: mergedIsStreaming,
      isReplacing: streaming.isReplacing,
      tokenCount: mergedTokenCount,
      operationId: streaming.operationId,
    });
  }, [
    mergedIsStreaming,
    streaming.isReplacing,
    mergedTokenCount,
    streaming.operationId,
    onStreamingStateChange,
  ]);

  // ─────────────────────────────────────────────────────────────────────
  // Escape key handler for cancel
  // ─────────────────────────────────────────────────────────────────────

  const handleKeyDown = useCallback(
    (event: KeyboardEvent) => {
      if (event.key === 'Escape' && mergedIsStreaming) {
        event.preventDefault();
        event.stopPropagation();
        streaming.cancelStream();
      }
    },
    [mergedIsStreaming, streaming.cancelStream]
  );

  useEffect(() => {
    if (mergedIsStreaming) {
      document.addEventListener('keydown', handleKeyDown, true);
      return () => {
        document.removeEventListener('keydown', handleKeyDown, true);
      };
    }
  }, [mergedIsStreaming, handleKeyDown]);

  // ─────────────────────────────────────────────────────────────────────
  // Render streaming indicator
  // ─────────────────────────────────────────────────────────────────────

  return (
    <StreamingIndicator
      isStreaming={mergedIsStreaming}
      tokenCount={mergedTokenCount}
      isReplacing={streaming.isReplacing}
      onCancel={streaming.cancelStream}
    />
  );
}
