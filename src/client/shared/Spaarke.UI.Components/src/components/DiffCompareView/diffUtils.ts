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

import { diffWords, diffChars } from "diff";
import type {
    DiffOptions,
    DiffResult,
    DiffStats,
    IDiffSegment,
    BlockChange,
    BlockChangeType,
} from "./DiffCompareView.types";

// ─────────────────────────────────────────────────────────────────────────────
// Constants
// ─────────────────────────────────────────────────────────────────────────────

/** Block-level HTML tags that should be treated as structural boundaries */
const BLOCK_TAGS = new Set([
    "p", "h1", "h2", "h3", "h4", "h5", "h6",
    "li", "ul", "ol", "blockquote", "div", "br",
]);

/** Default diff options */
const DEFAULT_OPTIONS: DiffOptions = {
    whitespace: "relaxed",
    granularity: "word",
};

// ─────────────────────────────────────────────────────────────────────────────
// HTML Escaping (XSS prevention)
// ─────────────────────────────────────────────────────────────────────────────

/**
 * Escapes HTML special characters in a string to prevent XSS.
 * This is applied to all user-supplied text content before it is
 * embedded in annotated HTML output.
 */
export function escapeHtml(text: string): string {
    return text
        .replace(/&/g, "&amp;")
        .replace(/</g, "&lt;")
        .replace(/>/g, "&gt;")
        .replace(/"/g, "&quot;")
        .replace(/'/g, "&#39;");
}

// ─────────────────────────────────────────────────────────────────────────────
// HTML Tag Parsing (string-based, no DOM)
// ─────────────────────────────────────────────────────────────────────────────

/** Represents a parsed token from an HTML string */
interface HtmlToken {
    /** "tag" for opening/closing/self-closing tags; "text" for text content */
    kind: "tag" | "text";
    /** The raw string content (including angle brackets for tags) */
    raw: string;
    /** For tags: the tag name (lowercase), e.g. "p", "h1", "li" */
    tagName?: string;
    /** For tags: true if this is a closing tag (e.g. </p>) */
    isClosing?: boolean;
    /** For tags: true if this is a self-closing tag (e.g. <br/>) */
    isSelfClosing?: boolean;
}

/**
 * Tokenizes an HTML string into a sequence of tag and text tokens.
 * Uses regex-based parsing (no DOM) so it works in Node.js without jsdom.
 */
function tokenizeHtml(html: string): HtmlToken[] {
    const tokens: HtmlToken[] = [];
    // Match HTML tags (including attributes) and text between them.
    // Group 1: full tag content between < and >
    const tagRegex = /<(\/?)(\w+)([^>]*?)(\/?)>/g;
    let lastIndex = 0;
    let match: RegExpExecArray | null;

    while ((match = tagRegex.exec(html)) !== null) {
        // Text between the previous tag and this one
        if (match.index > lastIndex) {
            const textContent = html.slice(lastIndex, match.index);
            if (textContent) {
                tokens.push({ kind: "text", raw: textContent });
            }
        }

        const isClosing = match[1] === "/";
        const tagName = match[2].toLowerCase();
        const isSelfClosing = match[4] === "/" || tagName === "br";

        tokens.push({
            kind: "tag",
            raw: match[0],
            tagName,
            isClosing,
            isSelfClosing,
        });

        lastIndex = match.index + match[0].length;
    }

    // Trailing text after the last tag
    if (lastIndex < html.length) {
        const textContent = html.slice(lastIndex);
        if (textContent) {
            tokens.push({ kind: "text", raw: textContent });
        }
    }

    return tokens;
}

// ─────────────────────────────────────────────────────────────────────────────
// extractTextFromHtml
// ─────────────────────────────────────────────────────────────────────────────

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
export function extractTextFromHtml(html: string): string {
    if (!html || !html.trim()) {
        return "";
    }

    const tokens = tokenizeHtml(html);
    const parts: string[] = [];

    // Track ordered list counters (stack for nested lists)
    const olCounterStack: number[] = [];
    let inOrderedList = false;
    let inBlockquote = false;

    for (const token of tokens) {
        if (token.kind === "tag") {
            const tag = token.tagName ?? "";

            if (token.isClosing) {
                // Closing tags
                if (tag === "p" || tag === "div") {
                    parts.push("\n\n");
                } else if (/^h[1-6]$/.test(tag)) {
                    parts.push("\n\n");
                } else if (tag === "li") {
                    parts.push("\n");
                } else if (tag === "ul") {
                    parts.push("\n");
                } else if (tag === "ol") {
                    olCounterStack.pop();
                    inOrderedList = olCounterStack.length > 0;
                    parts.push("\n");
                } else if (tag === "blockquote") {
                    inBlockquote = false;
                    parts.push("\n");
                }
            } else if (token.isSelfClosing || tag === "br") {
                parts.push("\n");
            } else {
                // Opening tags
                if (/^h([1-6])$/.test(tag)) {
                    const level = parseInt(tag.charAt(1), 10);
                    const prefix = "#".repeat(level) + " ";
                    parts.push(prefix);
                } else if (tag === "li") {
                    if (inBlockquote) {
                        parts.push("> ");
                    }
                    if (inOrderedList && olCounterStack.length > 0) {
                        const counter = olCounterStack[olCounterStack.length - 1];
                        olCounterStack[olCounterStack.length - 1] = counter + 1;
                        parts.push(`${counter}. `);
                    } else {
                        parts.push("- ");
                    }
                } else if (tag === "ol") {
                    olCounterStack.push(1);
                    inOrderedList = true;
                } else if (tag === "ul") {
                    // Track that we're in an unordered list
                    // (li items default to "- " prefix)
                } else if (tag === "blockquote") {
                    inBlockquote = true;
                    parts.push("> ");
                }
            }
        } else {
            // Text token -- decode HTML entities
            const decoded = decodeHtmlEntities(token.raw);
            parts.push(decoded);
        }
    }

    // Clean up the result: collapse excessive newlines, trim
    let result = parts.join("");
    result = result.replace(/\n{3,}/g, "\n\n");
    result = result.trim();

    return result;
}

/**
 * Decodes common HTML entities to their character equivalents.
 * Handles named entities and numeric character references.
 */
function decodeHtmlEntities(text: string): string {
    return text
        .replace(/&nbsp;/g, " ")
        .replace(/&amp;/g, "&")
        .replace(/&lt;/g, "<")
        .replace(/&gt;/g, ">")
        .replace(/&quot;/g, '"')
        .replace(/&#39;/g, "'")
        .replace(/&#x27;/g, "'")
        .replace(/&#(\d+);/g, (_match, code) => String.fromCharCode(parseInt(code, 10)))
        .replace(/&#x([0-9a-fA-F]+);/g, (_match, hex) => String.fromCharCode(parseInt(hex, 16)));
}

// ─────────────────────────────────────────────────────────────────────────────
// Whitespace normalization
// ─────────────────────────────────────────────────────────────────────────────

/**
 * Normalizes whitespace in a text string for relaxed comparison.
 * Collapses runs of whitespace to single spaces and trims each line.
 */
function normalizeWhitespace(text: string): string {
    return text
        .split("\n")
        .map((line) => line.replace(/\s+/g, " ").trim())
        .join("\n")
        .replace(/\n{3,}/g, "\n\n")
        .trim();
}

// ─────────────────────────────────────────────────────────────────────────────
// computeHtmlDiff
// ─────────────────────────────────────────────────────────────────────────────

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
export function computeHtmlDiff(
    originalHtml: string,
    proposedHtml: string,
    options?: Partial<DiffOptions>,
): DiffResult {
    const opts: DiffOptions = { ...DEFAULT_OPTIONS, ...options };

    // Step 1: Extract plain text from HTML
    let originalText = extractTextFromHtml(originalHtml);
    let proposedText = extractTextFromHtml(proposedHtml);

    // Step 2: Normalize whitespace if relaxed mode
    if (opts.whitespace === "relaxed") {
        originalText = normalizeWhitespace(originalText);
        proposedText = normalizeWhitespace(proposedText);
    }

    // Step 3: Compute diff using jsdiff
    const diffFn = opts.granularity === "character" ? diffChars : diffWords;
    const changes = diffFn(originalText, proposedText);

    // Step 4: Build segments and compute stats
    const segments: IDiffSegment[] = [];
    let additions = 0;
    let deletions = 0;
    let unchanged = 0;

    for (const change of changes) {
        const segType: IDiffSegment["type"] = change.added
            ? "added"
            : change.removed
                ? "removed"
                : "unchanged";

        segments.push({ type: segType, value: change.value });

        // Count words for stats
        const wordCount = change.value.trim().split(/\s+/).filter(Boolean).length;
        if (change.added) {
            additions += wordCount;
        } else if (change.removed) {
            deletions += wordCount;
        } else {
            unchanged += wordCount;
        }
    }

    const stats: DiffStats = { additions, deletions, unchanged };

    // Step 5: Build annotated HTML for both sides
    const originalAnnotatedHtml = buildAnnotatedHtml(
        originalHtml,
        segments,
        "original",
    );
    const proposedAnnotatedHtml = buildAnnotatedHtml(
        proposedHtml,
        segments,
        "proposed",
    );

    return {
        originalAnnotatedHtml,
        proposedAnnotatedHtml,
        segments,
        stats,
    };
}

// ─────────────────────────────────────────────────────────────────────────────
// Annotated HTML Builder
// ─────────────────────────────────────────────────────────────────────────────

/**
 * Builds annotated HTML by walking through the original HTML structure and
 * overlaying diff highlights on text content.
 *
 * For the "original" side: shows unchanged + removed content (removed is highlighted).
 * For the "proposed" side: shows unchanged + added content (added is highlighted).
 *
 * @param html - The source HTML (original or proposed).
 * @param segments - The diff segments computed from extractTextFromHtml output.
 * @param side - Which side to annotate ("original" shows removals, "proposed" shows additions).
 */
function buildAnnotatedHtml(
    html: string,
    segments: IDiffSegment[],
    side: "original" | "proposed",
): string {
    // Filter segments to those relevant to this side
    const relevantSegments = segments.filter((seg) => {
        if (side === "original") {
            return seg.type === "unchanged" || seg.type === "removed";
        }
        return seg.type === "unchanged" || seg.type === "added";
    });

    // Build a flat text from relevant segments for alignment
    const flatText = relevantSegments.map((s) => s.value).join("");

    // Tokenize the source HTML
    const tokens = tokenizeHtml(html);

    // We'll walk through the HTML tokens and the flat diff text in parallel.
    // For each text token in the HTML, we consume the equivalent text from
    // the diff segments, wrapping added/removed portions in styled spans.
    const output: string[] = [];
    let diffOffset = 0; // Current position in flatText

    for (const token of tokens) {
        if (token.kind === "tag") {
            // Pass through structural tags as-is (they define block structure)
            output.push(token.raw);
        } else {
            // Text token: decode entities for matching, then re-encode with diff markup
            const decodedText = decodeHtmlEntities(token.raw);
            const annotated = annotateTextWithDiff(
                decodedText,
                relevantSegments,
                diffOffset,
                flatText,
                side,
            );
            output.push(annotated.html);
            diffOffset = annotated.newOffset;
        }
    }

    return output.join("");
}

/**
 * Result of annotating a text fragment with diff highlighting.
 */
interface AnnotateResult {
    /** The annotated HTML string */
    html: string;
    /** The updated diff offset after consuming this text */
    newOffset: number;
}

/**
 * Annotates a text fragment by mapping it against the diff segments.
 *
 * Walks through the diff segments' flat text starting at `diffOffset`,
 * consuming characters that match the input text, and wrapping
 * added/removed portions in `<span>` elements.
 *
 * Characters from diff segments that don't correspond to the source text
 * (e.g. heading markers "# " or list markers "- ") are skipped.
 */
function annotateTextWithDiff(
    text: string,
    segments: IDiffSegment[],
    startOffset: number,
    flatText: string,
    side: "original" | "proposed",
): AnnotateResult {
    if (!text.trim()) {
        return { html: escapeHtml(text), newOffset: startOffset };
    }

    const parts: string[] = [];
    let textIdx = 0;
    let diffOffset = startOffset;

    // We need to consume characters from the flatText that correspond to
    // characters in our source text. Structural markers (like "# ", "- ")
    // that were inserted by extractTextFromHtml appear in flatText but not
    // in the source HTML text, so we skip those.
    while (textIdx < text.length && diffOffset < flatText.length) {
        const textChar = text[textIdx];
        const diffChar = flatText[diffOffset];

        if (textChar === diffChar) {
            // Characters match: find which segment we're in and apply styling
            const { segment } = findSegmentAtOffset(segments, diffOffset);
            if (segment) {
                // Gather a run of matching characters from the same segment
                let run = textChar;
                let ti = textIdx + 1;
                let di = diffOffset + 1;

                while (ti < text.length && di < flatText.length) {
                    const seg2 = findSegmentAtOffset(segments, di);
                    if (seg2.segment !== segment) break;
                    if (text[ti] !== flatText[di]) break;
                    run += text[ti];
                    ti++;
                    di++;
                }

                // Wrap the run in a span if it's a diff change
                const escaped = escapeHtml(run);
                if (segment.type === "removed" && side === "original") {
                    parts.push(`<span class="diff-removed">${escaped}</span>`);
                } else if (segment.type === "added" && side === "proposed") {
                    parts.push(`<span class="diff-added">${escaped}</span>`);
                } else {
                    parts.push(escaped);
                }

                textIdx = ti;
                diffOffset = di;
            } else {
                parts.push(escapeHtml(textChar));
                textIdx++;
                diffOffset++;
            }
        } else {
            // Diff text has a character not present in source (structural marker).
            // Skip it in the diff.
            diffOffset++;
        }
    }

    // Any remaining source text that wasn't matched
    if (textIdx < text.length) {
        parts.push(escapeHtml(text.slice(textIdx)));
    }

    return { html: parts.join(""), newOffset: diffOffset };
}

/**
 * Finds which diff segment contains the character at the given offset
 * in the flat concatenated text.
 */
function findSegmentAtOffset(
    segments: IDiffSegment[],
    offset: number,
): { segment: IDiffSegment | null; localOffset: number } {
    let runningOffset = 0;
    for (const segment of segments) {
        const segLen = segment.value.length;
        if (offset < runningOffset + segLen) {
            return { segment, localOffset: offset - runningOffset };
        }
        runningOffset += segLen;
    }
    return { segment: null, localOffset: 0 };
}

// ─────────────────────────────────────────────────────────────────────────────
// detectBlockChanges
// ─────────────────────────────────────────────────────────────────────────────

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
export function detectBlockChanges(
    originalHtml: string,
    proposedHtml: string,
): BlockChange[] {
    const originalBlocks = extractBlocks(originalHtml);
    const proposedBlocks = extractBlocks(proposedHtml);

    // Use LCS on block text content to align blocks
    const lcs = computeBlockLcs(originalBlocks, proposedBlocks);
    const changes: BlockChange[] = [];

    let oi = 0;
    let pi = 0;
    let li = 0;

    while (oi < originalBlocks.length || pi < proposedBlocks.length) {
        if (li < lcs.length) {
            const lcsItem = lcs[li];

            // Emit removed blocks before the LCS match
            while (oi < lcsItem.origIdx) {
                changes.push({
                    type: "removed",
                    tag: originalBlocks[oi].tag,
                    originalText: originalBlocks[oi].text,
                    proposedText: "",
                });
                oi++;
            }

            // Emit added blocks before the LCS match
            while (pi < lcsItem.propIdx) {
                changes.push({
                    type: "added",
                    tag: proposedBlocks[pi].tag,
                    originalText: "",
                    proposedText: proposedBlocks[pi].text,
                });
                pi++;
            }

            // Emit the matched block (check if modified or unchanged)
            const origBlock = originalBlocks[oi];
            const propBlock = proposedBlocks[pi];
            const changeType: BlockChangeType =
                origBlock.text === propBlock.text ? "unchanged" : "modified";
            changes.push({
                type: changeType,
                tag: propBlock.tag || origBlock.tag,
                originalText: origBlock.text,
                proposedText: propBlock.text,
            });

            oi++;
            pi++;
            li++;
        } else {
            // Past LCS -- remaining blocks are removals or additions
            while (oi < originalBlocks.length) {
                changes.push({
                    type: "removed",
                    tag: originalBlocks[oi].tag,
                    originalText: originalBlocks[oi].text,
                    proposedText: "",
                });
                oi++;
            }
            while (pi < proposedBlocks.length) {
                changes.push({
                    type: "added",
                    tag: proposedBlocks[pi].tag,
                    originalText: "",
                    proposedText: proposedBlocks[pi].text,
                });
                pi++;
            }
        }
    }

    return changes;
}

// ─────────────────────────────────────────────────────────────────────────────
// Block extraction helpers
// ─────────────────────────────────────────────────────────────────────────────

/** A block element extracted from HTML */
interface HtmlBlock {
    /** The tag name (e.g. "p", "h1", "li") */
    tag: string;
    /** The plain text content of the block */
    text: string;
}

/**
 * Extracts top-level block elements from an HTML string.
 * Each block is identified by its tag name and text content.
 */
function extractBlocks(html: string): HtmlBlock[] {
    const blocks: HtmlBlock[] = [];
    const tokens = tokenizeHtml(html);

    let currentTag = "";
    let depth = 0;
    let textParts: string[] = [];

    for (const token of tokens) {
        if (token.kind === "tag") {
            const tag = token.tagName ?? "";

            if (token.isSelfClosing) {
                // Self-closing tags like <br/> don't create blocks
                if (depth > 0 && tag === "br") {
                    textParts.push("\n");
                }
                continue;
            }

            if (token.isClosing) {
                if (isBlockTag(tag) && tag === currentTag && depth === 1) {
                    // End of a top-level block
                    const text = textParts.join("").trim();
                    if (text) {
                        blocks.push({ tag: currentTag, text });
                    }
                    currentTag = "";
                    depth = 0;
                    textParts = [];
                } else if (depth > 1 && isBlockTag(tag)) {
                    depth--;
                }
            } else {
                // Opening tag
                if (isBlockTag(tag) && depth === 0) {
                    currentTag = tag;
                    depth = 1;
                    textParts = [];
                } else if (depth > 0 && isBlockTag(tag)) {
                    depth++;
                }
            }
        } else if (depth > 0) {
            textParts.push(decodeHtmlEntities(token.raw));
        }
    }

    // Handle trailing text that wasn't wrapped in a block
    const trailing = textParts.join("").trim();
    if (trailing && currentTag) {
        blocks.push({ tag: currentTag, text: trailing });
    }

    return blocks;
}

/** Returns true if the tag is a block-level HTML element */
function isBlockTag(tag: string): boolean {
    return BLOCK_TAGS.has(tag);
}

// ─────────────────────────────────────────────────────────────────────────────
// LCS for block alignment
// ─────────────────────────────────────────────────────────────────────────────

/** An entry in the LCS result mapping original and proposed indices */
interface LcsEntry {
    origIdx: number;
    propIdx: number;
}

/**
 * Computes the Longest Common Subsequence of blocks by text similarity.
 *
 * Two blocks "match" for LCS purposes if they share the same tag and
 * their text content shares >= 50% of words (allowing for modifications).
 */
function computeBlockLcs(
    original: HtmlBlock[],
    proposed: HtmlBlock[],
): LcsEntry[] {
    const m = original.length;
    const n = proposed.length;

    // Standard LCS dynamic programming
    const dp: number[][] = Array.from({ length: m + 1 }, () =>
        Array(n + 1).fill(0),
    );

    for (let i = 1; i <= m; i++) {
        for (let j = 1; j <= n; j++) {
            if (blocksMatch(original[i - 1], proposed[j - 1])) {
                dp[i][j] = dp[i - 1][j - 1] + 1;
            } else {
                dp[i][j] = Math.max(dp[i - 1][j], dp[i][j - 1]);
            }
        }
    }

    // Backtrack to find the LCS
    const result: LcsEntry[] = [];
    let i = m;
    let j = n;
    while (i > 0 && j > 0) {
        if (blocksMatch(original[i - 1], proposed[j - 1])) {
            result.unshift({ origIdx: i - 1, propIdx: j - 1 });
            i--;
            j--;
        } else if (dp[i - 1][j] > dp[i][j - 1]) {
            i--;
        } else {
            j--;
        }
    }

    return result;
}

/**
 * Determines if two blocks "match" for alignment purposes.
 * Blocks match if they share the same tag type and at least 50% of words.
 */
function blocksMatch(a: HtmlBlock, b: HtmlBlock): boolean {
    // Tags must be compatible (same tag, or both heading tags)
    if (a.tag !== b.tag) {
        // Allow matching between heading levels (h1 can match h2)
        const aIsHeading = /^h[1-6]$/.test(a.tag);
        const bIsHeading = /^h[1-6]$/.test(b.tag);
        if (!aIsHeading || !bIsHeading) {
            return false;
        }
    }

    // Check text similarity: at least 50% word overlap
    const aWords = new Set(a.text.toLowerCase().split(/\s+/).filter(Boolean));
    const bWords = new Set(b.text.toLowerCase().split(/\s+/).filter(Boolean));

    if (aWords.size === 0 && bWords.size === 0) {
        return true;
    }

    let overlap = 0;
    for (const word of aWords) {
        if (bWords.has(word)) {
            overlap++;
        }
    }

    const maxSize = Math.max(aWords.size, bWords.size);
    return overlap / maxSize >= 0.5;
}
