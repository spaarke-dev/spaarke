/**
 * useDocumentInsert - BroadcastChannel handler for document_insert events
 *
 * Subscribes to the `sprk-document-insert` BroadcastChannel and handles
 * insert events dispatched by SprkChat when the user clicks the "Insert"
 * button on an AI response message.
 *
 * Supports:
 * - Plain text insertion (`contentType='text'`): calls insertAtCursor with text
 * - HTML insertion (`contentType='html'`): calls insertAtCursor with parsed HTML
 * - Cursor-position insert (`insertAt='cursor'`): inserts at current cursor
 * - Selection replace (`insertAt='selection'`): replaces current selection
 *   (Lexical handles this automatically — same insertAtCursor call, behaviour
 *   is driven by editor selection state at the time of insertion)
 * - Full undo support via Lexical's discrete update (Ctrl+Z removes the insert)
 *
 * Data flow:
 *   SprkChat side pane → BroadcastChannel('sprk-document-insert') →
 *   useDocumentInsert → RichTextEditorRef.insertAtCursor() → Lexical editor
 *
 * SECURITY: Auth tokens MUST NOT be transmitted via BroadcastChannel.
 * This hook only reads content to insert — no auth data flows through here.
 *
 * @param editorRef - Ref to the RichTextEditor public imperative handle.
 *                    Must have an insertAtCursor method (added in task 051).
 *
 * @see IDocumentInsertEvent - The event shape emitted by SprkChat (task 050)
 * @see RichTextEditorRef.insertAtCursor - Lexical cursor/selection insert impl
 * @see ADR-012 - No Xrm/PCF imports in shared library; this hook is Code Page only
 * @see spec-2D - Insert-to-Editor phase requirements
 */

import { type RefObject, useEffect, useRef } from 'react';
import type { RichTextEditorRef, IDocumentInsertEvent } from '@spaarke/ui-components';

// ─────────────────────────────────────────────────────────────────────────────
// Constants
// ─────────────────────────────────────────────────────────────────────────────

/**
 * BroadcastChannel name for document insert events.
 * Must match the channel name used in SprkChat (task 050).
 */
const DOCUMENT_INSERT_CHANNEL = 'sprk-document-insert';

// ─────────────────────────────────────────────────────────────────────────────
// Type Guards
// ─────────────────────────────────────────────────────────────────────────────

/**
 * Type guard: verifies that a MessageEvent payload is a valid IDocumentInsertEvent.
 * Guards against malformed events from other tabs or future event type additions.
 */
function isDocumentInsertEvent(data: unknown): data is IDocumentInsertEvent {
  if (typeof data !== 'object' || data === null) {
    return false;
  }
  const event = data as Record<string, unknown>;
  return (
    event['type'] === 'document_insert' &&
    typeof event['content'] === 'string' &&
    (event['contentType'] === 'text' || event['contentType'] === 'html') &&
    (event['insertAt'] === 'cursor' || event['insertAt'] === 'selection') &&
    typeof event['timestamp'] === 'number'
  );
}

// ─────────────────────────────────────────────────────────────────────────────
// Hook
// ─────────────────────────────────────────────────────────────────────────────

/**
 * Options for useDocumentInsert.
 */
export interface UseDocumentInsertOptions {
  /**
   * Ref to the RichTextEditor imperative handle.
   * When null/undefined, the hook is dormant (no channel subscription active).
   */
  editorRef: RefObject<RichTextEditorRef | null>;

  /**
   * Whether the hook is active. Set to false to disable without unmounting.
   * Defaults to true.
   */
  enabled?: boolean;
}

/**
 * useDocumentInsert
 *
 * Sets up a BroadcastChannel subscription for document_insert events and
 * drives content insertion into the Lexical editor via RichTextEditorRef.
 *
 * @example
 * ```tsx
 * const editorRef = useRef<RichTextEditorRef>(null);
 *
 * // Wire in the component:
 * useDocumentInsert({ editorRef });
 *
 * // The RichTextEditor ref receives insert events automatically:
 * <RichTextEditor ref={editorRef} ... />
 * ```
 */
export function useDocumentInsert(options: UseDocumentInsertOptions): void {
  const { editorRef, enabled = true } = options;

  // Keep a stable ref to enabled so the channel message handler always
  // sees the current value without re-subscribing on every render.
  const enabledRef = useRef(enabled);
  useEffect(() => {
    enabledRef.current = enabled;
  }, [enabled]);

  useEffect(() => {
    if (!enabled) {
      return;
    }

    // BroadcastChannel is not available in all environments (e.g., some SSR scenarios).
    // Guard defensively to avoid crashes in test environments.
    if (typeof BroadcastChannel === 'undefined') {
      console.warn('[useDocumentInsert] BroadcastChannel is not available in this environment.');
      return;
    }

    const channel = new BroadcastChannel(DOCUMENT_INSERT_CHANNEL);

    const handleMessage = (event: MessageEvent): void => {
      if (!enabledRef.current) {
        return;
      }

      const { data } = event;

      // Validate the event shape before processing
      if (!isDocumentInsertEvent(data)) {
        console.warn('[useDocumentInsert] Received unexpected message on sprk-document-insert channel:', data);
        return;
      }

      const editor = editorRef.current;
      if (!editor) {
        console.warn('[useDocumentInsert] Editor ref is null; cannot handle document_insert event.');
        return;
      }

      // Delegate to the RichTextEditor imperative method.
      //
      // insertAtCursor uses Lexical's $getSelection() internally:
      //   - If a range selection exists (non-collapsed) → replaces it
      //   - If cursor only (collapsed selection) → inserts at cursor
      //   - If no selection → appends to end of document
      //
      // This correctly handles both insertAt='cursor' and insertAt='selection'
      // since the editor's current selection state determines the behaviour.
      //
      // The discrete: true flag in the Lexical update ensures the insert
      // creates its own undo history entry (supports Ctrl+Z undo per spec-2D).
      editor.insertAtCursor(data.content, data.contentType);
    };

    channel.addEventListener('message', handleMessage);

    return () => {
      channel.removeEventListener('message', handleMessage);
      channel.close();
    };
  }, [editorRef, enabled]);
}
