/**
 * diffUtils - HTML-aware diff algorithm utilities
 *
 * Provides functions to extract plain text from HTML, compute word-level diffs,
 * and map diff operations back to styled HTML spans while preserving heading
 * and list structure.
 *
 * Key design decisions:
 * - NO DOM dependencies (no document.createElement) -- pure string parsing only.
 *   This ensures the module works in Node.js test environments.
 * - All user content is HTML-escaped to prevent XSS when rendered via dangerouslySetInnerHTML.
 * - Preserves semantic block structure (headings, paragraphs, lists, blockquotes).
 * - Uses the jsdiff library (diffWords / diffChars) for the core diff algorithm.
 *
 * Standards: ADR-012 (shared component library)
 */
import type { DiffOptions, DiffResult, BlockChange } from './DiffCompareView.types';
/**
 * Escapes HTML special characters in a string to prevent XSS.
 * This is applied to all user-supplied text content before it is
 * embedded in annotated HTML output.
 */
export declare function escapeHtml(text: string): string;
/**
 * Extracts plain text from an HTML string, preserving structural cues.
 *
 * - Paragraph boundaries become double newlines.
 * - Headings are prefixed with "#" markers matching their level (e.g. "## " for h2).
 * - Unordered list items are prefixed with "- ".
 * - Ordered list items are prefixed with sequential "1. ", "2. ", etc.
 * - Blockquotes are prefixed with "> ".
 * - Inline formatting (bold, italic, etc.) is stripped.
 * - HTML entities (&amp;, &lt;, &gt;, &quot;, &#39;, &nbsp;) are decoded.
 *
 * @param html - The HTML string to extract text from.
 * @returns Plain text representation preserving block structure.
 *
 * @example
 * ```ts
 * const text = extractTextFromHtml("<h1>Title</h1><p>Hello <b>world</b></p>");
 * // "# Title\n\nHello world"
 * ```
 */
export declare function extractTextFromHtml(html: string): string;
/**
 * Computes a diff between two HTML strings and produces annotated HTML output.
 *
 * The function:
 * 1. Extracts plain text from both HTML inputs.
 * 2. Optionally normalizes whitespace (relaxed mode).
 * 3. Runs jsdiff (word or character level) on the extracted text.
 * 4. Maps diff operations back to annotated HTML with styled `<span>` elements.
 * 5. Reconstructs block structure (paragraphs, headings, lists) in the output.
 *
 * The annotated HTML output uses CSS classes for styling:
 * - `diff-added`: Content added in the proposed version.
 * - `diff-removed`: Content removed from the original version.
 *
 * All text content is HTML-escaped before embedding to prevent XSS.
 *
 * @param originalHtml - The original HTML content.
 * @param proposedHtml - The proposed (revised) HTML content.
 * @param options - Optional diff configuration (whitespace mode, granularity).
 * @returns DiffResult with annotated HTML, segments, and statistics.
 *
 * @example
 * ```ts
 * const result = computeHtmlDiff(
 *     "<p>The quick brown fox</p>",
 *     "<p>The fast brown fox jumps</p>",
 * );
 * // result.originalAnnotatedHtml contains highlighted removals
 * // result.proposedAnnotatedHtml contains highlighted additions
 * // result.stats = { additions: 2, deletions: 1, unchanged: 3 }
 * ```
 */
export declare function computeHtmlDiff(originalHtml: string, proposedHtml: string, options?: Partial<DiffOptions>): DiffResult;
/**
 * Detects block-level changes between two HTML documents.
 *
 * Parses both documents into a list of block elements (paragraphs, headings,
 * list items, blockquotes), then compares them pairwise to identify which
 * blocks were added, removed, modified, or left unchanged.
 *
 * Uses a simple Longest Common Subsequence (LCS) approach on block text
 * content for alignment, which handles insertions and deletions gracefully.
 *
 * @param originalHtml - The original HTML document.
 * @param proposedHtml - The proposed HTML document.
 * @returns Array of block changes in document order.
 *
 * @example
 * ```ts
 * const changes = detectBlockChanges(
 *     "<h1>Title</h1><p>First paragraph</p>",
 *     "<h1>Title</h1><p>First paragraph revised</p><p>New paragraph</p>",
 * );
 * // [
 * //   { type: "unchanged", tag: "h1", originalText: "Title", proposedText: "Title" },
 * //   { type: "modified", tag: "p", originalText: "First paragraph", proposedText: "First paragraph revised" },
 * //   { type: "added", tag: "p", originalText: "", proposedText: "New paragraph" },
 * // ]
 * ```
 */
export declare function detectBlockChanges(originalHtml: string, proposedHtml: string): BlockChange[];
//# sourceMappingURL=diffUtils.d.ts.map