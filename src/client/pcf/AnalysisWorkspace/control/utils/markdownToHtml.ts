/**
 * Markdown to HTML Conversion Utility
 *
 * Converts markdown content from AI responses to HTML for display
 * in the RichTextEditor (which uses Lexical and expects HTML input).
 */

import { parse as markedParse } from "marked";

/**
 * Convert markdown string to HTML string.
 *
 * @param markdown - The markdown content to convert
 * @returns HTML string suitable for RichTextEditor
 */
export function markdownToHtml(markdown: string): string {
    if (!markdown || markdown.trim() === "") {
        return "";
    }

    try {
        // marked.parse with GFM options for GitHub-style markdown
        const result = markedParse(markdown, { gfm: true, breaks: true });
        return result;
    } catch (error) {
        console.error("[markdownToHtml] Failed to convert markdown:", error);
        // Return the original markdown wrapped in pre tag as fallback
        return `<pre>${escapeHtml(markdown)}</pre>`;
    }
}

/**
 * Escape HTML special characters for safe display.
 * Used as fallback when markdown parsing fails.
 */
function escapeHtml(text: string): string {
    const escapeMap: Record<string, string> = {
        "&": "&amp;",
        "<": "&lt;",
        ">": "&gt;",
        '"': "&quot;",
        "'": "&#39;"
    };
    return text.replace(/[&<>"']/g, (char) => escapeMap[char] || char);
}

/**
 * Check if a string appears to be markdown (vs plain text or HTML).
 * Useful for deciding whether to run conversion.
 *
 * @param content - Content to check
 * @returns true if content appears to be markdown
 */
export function isMarkdown(content: string): boolean {
    if (!content) return false;

    // Common markdown patterns
    const markdownPatterns = [
        /^#{1,6}\s+/m,          // Headers: # Header
        /^\*{1,3}[^*]+\*{1,3}/m, // Bold/italic: *text*, **text**, ***text***
        /^[-*+]\s+/m,           // Unordered lists: - item, * item, + item
        /^\d+\.\s+/m,           // Ordered lists: 1. item
        /\[.+\]\(.+\)/,         // Links: [text](url)
        /^>\s+/m,               // Blockquotes: > text
        /^```/m,                // Code blocks: ```
        /`[^`]+`/               // Inline code: `code`
    ];

    return markdownPatterns.some(pattern => pattern.test(content));
}
