/**
 * RichTextEditor Component
 *
 * A reusable WYSIWYG rich text editor built with Lexical.
 * Designed for use in PCF controls across Model-Driven Apps,
 * Power Apps Custom Pages, and standalone web applications.
 *
 * Features:
 * - Full rich text editing (bold, italic, underline, strikethrough)
 * - Headings (H1, H2, H3)
 * - Lists (ordered, unordered)
 * - Links
 * - Undo/Redo
 * - HTML import/export
 * - Fluent UI v9 styling
 * - Dark mode support
 *
 * Standards: ADR-012 (shared component library)
 */
import * as React from 'react';
import type { StreamingInsertHandle } from './plugins/StreamingInsertPlugin';
export interface IRichTextEditorProps {
    /** Initial HTML content */
    value: string;
    /** Callback when content changes (HTML string) */
    onChange: (html: string) => void;
    /** Placeholder text when empty */
    placeholder?: string;
    /** Read-only mode */
    readOnly?: boolean;
    /** Use dark theme */
    isDarkMode?: boolean;
    /** Disable the toolbar */
    hideToolbar?: boolean;
    /** Minimum height in pixels */
    minHeight?: number;
    /** Maximum height in pixels (scrolls beyond) */
    maxHeight?: number;
}
export interface RichTextEditorRef {
    /** Focus the editor */
    focus: () => void;
    /** Get current HTML content */
    getHtml: () => string;
    /** Set HTML content programmatically */
    setHtml: (html: string) => void;
    /** Clear all content */
    clear: () => void;
    /**
     * Insert content at the current cursor position or replace the current selection.
     *
     * For plain text (`contentType='text'`): inserts as a Lexical TextNode.
     * For HTML (`contentType='html'`): parses via DOMParser and inserts using $generateNodesFromDOM.
     *
     * Uses `discrete: true` in editor.update() so the insert is a separate undo history entry
     * that can be undone independently with Ctrl+Z.
     *
     * Behaviour:
     * - If the editor has a collapsed (cursor-only) selection: inserts at cursor position.
     * - If the editor has a non-collapsed (range) selection: replaces the selection with
     *   the new content (Lexical's insertNodes / insertText handles this automatically when
     *   a RangeSelection is active).
     * - If no selection exists: appends to end of document.
     *
     * @param content - The text or HTML string to insert.
     * @param contentType - 'text' for plain text, 'html' for HTML markup.
     *
     * @see spec-2D - Insert-to-Editor phase requirements
     * @see IDocumentInsertEvent - The BroadcastChannel event shape that drives this method
     */
    insertAtCursor: (content: string, contentType: 'text' | 'html') => void;
    /**
     * Begin a streaming insertion operation at the given position.
     * Returns a StreamingInsertHandle for token delivery.
     *
     * @param position - 'cursor' to insert at current cursor, 'end' to append at end
     * @returns A handle for controlling the streaming operation
     */
    beginStreamingInsert: (position: 'cursor' | 'end') => StreamingInsertHandle;
    /**
     * Append a single token to the active streaming operation.
     *
     * @param handle - The handle returned by beginStreamingInsert
     * @param token - The text token to append
     */
    appendStreamToken: (handle: StreamingInsertHandle, token: string) => void;
    /**
     * End the streaming insertion operation.
     * Restores the editor to its normal editable state.
     *
     * @param handle - The handle returned by beginStreamingInsert
     * @param cancelled - If true, removes all inserted content (default false)
     */
    endStreamingInsert: (handle: StreamingInsertHandle, cancelled?: boolean) => void;
}
export declare const RichTextEditor: React.ForwardRefExoticComponent<IRichTextEditorProps & React.RefAttributes<RichTextEditorRef>>;
export default RichTextEditor;
//# sourceMappingURL=RichTextEditor.d.ts.map