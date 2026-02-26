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

import * as React from "react";
import type { RichTextEditorRef } from "../components/RichTextEditor";

// ─────────────────────────────────────────────────────────────────────────────
// Types
// ─────────────────────────────────────────────────────────────────────────────

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

// ─────────────────────────────────────────────────────────────────────────────
// Constants
// ─────────────────────────────────────────────────────────────────────────────

const DEFAULT_MAX_VERSIONS = 20;

// ─────────────────────────────────────────────────────────────────────────────
// Hook Implementation
// ─────────────────────────────────────────────────────────────────────────────

/**
 * Version stack hook for undo/redo of AI-initiated document changes.
 *
 * @param editorRef - Ref to the RichTextEditor instance (getHtml / setHtml)
 * @param maxVersions - Maximum number of snapshots to retain (default 20, per FR-07)
 * @returns Undo/redo controls and status flags
 */
export function useDocumentHistory(
  editorRef: React.RefObject<RichTextEditorRef | null>,
  maxVersions: number = DEFAULT_MAX_VERSIONS
): UseDocumentHistoryReturn {
  // Version stack stored as ref to avoid re-renders on every push
  const stackRef = React.useRef<string[]>([]);
  // Current position in the version stack (-1 means empty/no versions)
  const indexRef = React.useRef<number>(-1);

  // State flags that trigger re-renders for consumers
  const [canUndo, setCanUndo] = React.useState(false);
  const [canRedo, setCanRedo] = React.useState(false);
  const [historyLength, setHistoryLength] = React.useState(0);

  // Synchronize boolean flags from refs
  const updateFlags = React.useCallback(() => {
    const idx = indexRef.current;
    const len = stackRef.current.length;
    setCanUndo(idx > 0);
    setCanRedo(idx >= 0 && idx < len - 1);
    setHistoryLength(len);
  }, []);

  const pushVersion = React.useCallback(() => {
    const html = editorRef.current?.getHtml();
    if (html === undefined || html === null) return;

    const stack = stackRef.current;
    const idx = indexRef.current;

    // If we're not at the top of the stack, truncate forward history
    if (idx < stack.length - 1) {
      stack.splice(idx + 1);
    }

    // Push the new snapshot
    stack.push(html);

    // If stack exceeds max, remove the oldest entry
    if (stack.length > maxVersions) {
      stack.shift();
      // Index stays at stack.length - 1 (top)
    }

    indexRef.current = stack.length - 1;
    updateFlags();
  }, [editorRef, maxVersions, updateFlags]);

  const undo = React.useCallback(() => {
    const idx = indexRef.current;
    if (idx <= 0) return;

    indexRef.current = idx - 1;
    const html = stackRef.current[indexRef.current];
    editorRef.current?.setHtml(html);
    updateFlags();
  }, [editorRef, updateFlags]);

  const redo = React.useCallback(() => {
    const idx = indexRef.current;
    const stack = stackRef.current;
    if (idx >= stack.length - 1) return;

    indexRef.current = idx + 1;
    const html = stack[indexRef.current];
    editorRef.current?.setHtml(html);
    updateFlags();
  }, [editorRef, updateFlags]);

  return {
    pushVersion,
    undo,
    redo,
    canUndo,
    canRedo,
    historyLength,
  };
}
