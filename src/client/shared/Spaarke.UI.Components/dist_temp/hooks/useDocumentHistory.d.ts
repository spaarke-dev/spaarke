/**
 * useDocumentHistory - Version stack hook for undo/redo of AI-initiated document changes
 *
 * Maintains a stack of HTML snapshots (max 20 by default) enabling undo/redo
 * for AI-initiated document modifications. Each AI operation (e.g., a streaming
 * write) is captured as a single undo step, regardless of how many tokens were
 * emitted. Cancel mid-stream allows undo to the pre-stream state.
 *
 * Works with the RichTextEditor ref API (getHtml / setHtml).
 *
 * @see ADR-012 - Shared Component Library
 * @see FR-06 - Cancel mid-stream must allow undo to pre-stream state
 * @see FR-07 - Max 20 snapshots in version stack
 *
 * @example
 * ```tsx
 * const editorRef = useRef<RichTextEditorRef>(null);
 * const history = useDocumentHistory(editorRef);
 *
 * // Before an AI operation modifies the document:
 * history.pushVersion();
 *
 * // User clicks undo:
 * history.undo();
 *
 * // User clicks redo:
 * history.redo();
 * ```
 */
import * as React from 'react';
import type { RichTextEditorRef } from '../components/RichTextEditor';
export interface UseDocumentHistoryReturn {
    /** Snapshot the current editor HTML and push onto the version stack */
    pushVersion: () => void;
    /** Undo to the previous version (no-op if nothing to undo) */
    undo: () => void;
    /** Redo to the next version (no-op if nothing to redo) */
    redo: () => void;
    /** Whether an undo operation is possible */
    canUndo: boolean;
    /** Whether a redo operation is possible */
    canRedo: boolean;
    /** Number of versions currently in the stack */
    historyLength: number;
}
/**
 * Version stack hook for undo/redo of AI-initiated document changes.
 *
 * @param editorRef - Ref to the RichTextEditor instance (getHtml / setHtml)
 * @param maxVersions - Maximum number of snapshots to retain (default 20, per FR-07)
 * @returns Undo/redo controls and status flags
 */
export declare function useDocumentHistory(editorRef: React.RefObject<RichTextEditorRef | null>, maxVersions?: number): UseDocumentHistoryReturn;
//# sourceMappingURL=useDocumentHistory.d.ts.map