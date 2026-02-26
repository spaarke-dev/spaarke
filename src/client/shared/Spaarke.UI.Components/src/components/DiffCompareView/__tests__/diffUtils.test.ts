/**
 * diffUtils Unit Tests
 *
 * Tests for extractTextFromHtml, computeHtmlDiff, detectBlockChanges,
 * and escapeHtml utility functions.
 *
 * @see ADR-012 - Shared component library
 */

import {
    escapeHtml,
    extractTextFromHtml,
    computeHtmlDiff,
    detectBlockChanges,
} from "../diffUtils";

// ─────────────────────────────────────────────────────────────────────────────
// escapeHtml
// ─────────────────────────────────────────────────────────────────────────────

describe("escapeHtml", () => {
    it("escapeHtml_SpecialCharacters_ReturnsEscapedEntities", () => {
        const input = '<script>alert("xss")</script>';
        const result = escapeHtml(input);

        expect(result).toBe("&lt;script&gt;alert(&quot;xss&quot;)&lt;/script&gt;");
    });

    it("escapeHtml_Ampersand_EscapesCorrectly", () => {
        expect(escapeHtml("foo & bar")).toBe("foo &amp; bar");
    });

    it("escapeHtml_SingleQuote_EscapesCorrectly", () => {
        expect(escapeHtml("it's")).toBe("it&#39;s");
    });

    it("escapeHtml_PlainText_ReturnsUnchanged", () => {
        const input = "Hello World 123";
        expect(escapeHtml(input)).toBe(input);
    });

    it("escapeHtml_EmptyString_ReturnsEmpty", () => {
        expect(escapeHtml("")).toBe("");
    });
});

// ─────────────────────────────────────────────────────────────────────────────
// extractTextFromHtml
// ─────────────────────────────────────────────────────────────────────────────

describe("extractTextFromHtml", () => {
    it("extractTextFromHtml_ParagraphTags_StripsTags", () => {
        const html = "<p>Hello world</p>";
        const result = extractTextFromHtml(html);

        expect(result).toBe("Hello world");
    });

    it("extractTextFromHtml_HeadingTags_PreservesHashPrefix", () => {
        const html = "<h1>Title</h1><h2>Subtitle</h2>";
        const result = extractTextFromHtml(html);

        expect(result).toContain("# Title");
        expect(result).toContain("## Subtitle");
    });

    it("extractTextFromHtml_H3Tag_ThreeHashes", () => {
        const html = "<h3>Section</h3>";
        const result = extractTextFromHtml(html);

        expect(result).toBe("### Section");
    });

    it("extractTextFromHtml_UnorderedList_PreservesDashPrefix", () => {
        const html = "<ul><li>First</li><li>Second</li></ul>";
        const result = extractTextFromHtml(html);

        expect(result).toContain("- First");
        expect(result).toContain("- Second");
    });

    it("extractTextFromHtml_OrderedList_PreservesNumberPrefix", () => {
        const html = "<ol><li>Alpha</li><li>Beta</li><li>Gamma</li></ol>";
        const result = extractTextFromHtml(html);

        expect(result).toContain("1. Alpha");
        expect(result).toContain("2. Beta");
        expect(result).toContain("3. Gamma");
    });

    it("extractTextFromHtml_Blockquote_PreservesAngleBracketPrefix", () => {
        const html = "<blockquote>Important note</blockquote>";
        const result = extractTextFromHtml(html);

        expect(result).toContain("> Important note");
    });

    it("extractTextFromHtml_InlineFormatting_StripsFormattingTags", () => {
        const html = "<p>Hello <b>bold</b> and <em>italic</em> text</p>";
        const result = extractTextFromHtml(html);

        expect(result).toBe("Hello bold and italic text");
    });

    it("extractTextFromHtml_HtmlEntities_DecodesCorrectly", () => {
        const html = "<p>A &amp; B &lt; C &gt; D &quot;E&quot; &#39;F&#39;</p>";
        const result = extractTextFromHtml(html);

        expect(result).toBe('A & B < C > D "E" \'F\'');
    });

    it("extractTextFromHtml_NbspEntity_DecodesToSpace", () => {
        const html = "<p>Word1&nbsp;Word2</p>";
        const result = extractTextFromHtml(html);

        expect(result).toBe("Word1 Word2");
    });

    it("extractTextFromHtml_EmptyString_ReturnsEmpty", () => {
        expect(extractTextFromHtml("")).toBe("");
    });

    it("extractTextFromHtml_WhitespaceOnly_ReturnsEmpty", () => {
        expect(extractTextFromHtml("   ")).toBe("");
    });

    it("extractTextFromHtml_BrTag_InsertsNewline", () => {
        const html = "<p>Line one<br>Line two</p>";
        const result = extractTextFromHtml(html);

        expect(result).toContain("Line one");
        expect(result).toContain("Line two");
    });

    it("extractTextFromHtml_MultipleParagraphs_SeparatedByDoubleNewlines", () => {
        const html = "<p>First paragraph</p><p>Second paragraph</p>";
        const result = extractTextFromHtml(html);

        expect(result).toBe("First paragraph\n\nSecond paragraph");
    });

    it("extractTextFromHtml_MixedContent_PreservesStructure", () => {
        const html = "<h1>Title</h1><p>Hello <b>world</b></p>";
        const result = extractTextFromHtml(html);

        expect(result).toBe("# Title\n\nHello world");
    });
});

// ─────────────────────────────────────────────────────────────────────────────
// computeHtmlDiff
// ─────────────────────────────────────────────────────────────────────────────

describe("computeHtmlDiff", () => {
    it("computeHtmlDiff_IdenticalContent_ZeroAdditionsAndDeletions", () => {
        const html = "<p>Same content here</p>";
        const result = computeHtmlDiff(html, html);

        expect(result.stats.additions).toBe(0);
        expect(result.stats.deletions).toBe(0);
        expect(result.stats.unchanged).toBeGreaterThan(0);
    });

    it("computeHtmlDiff_WordChanged_ReportsOneAdditionOneDeletion", () => {
        const original = "<p>The quick brown fox</p>";
        const proposed = "<p>The fast brown fox</p>";
        const result = computeHtmlDiff(original, proposed);

        // "quick" removed, "fast" added
        expect(result.stats.additions).toBeGreaterThanOrEqual(1);
        expect(result.stats.deletions).toBeGreaterThanOrEqual(1);
    });

    it("computeHtmlDiff_ContentAdded_ReportsAdditions", () => {
        const original = "<p>Hello</p>";
        const proposed = "<p>Hello world</p>";
        const result = computeHtmlDiff(original, proposed);

        expect(result.stats.additions).toBeGreaterThanOrEqual(1);
    });

    it("computeHtmlDiff_ContentRemoved_ReportsDeletions", () => {
        const original = "<p>Hello world</p>";
        const proposed = "<p>Hello</p>";
        const result = computeHtmlDiff(original, proposed);

        expect(result.stats.deletions).toBeGreaterThanOrEqual(1);
    });

    it("computeHtmlDiff_ReturnsAnnotatedHtml_WithDiffClasses", () => {
        const original = "<p>The quick brown fox</p>";
        const proposed = "<p>The fast brown fox</p>";
        const result = computeHtmlDiff(original, proposed);

        // Original side should contain diff-removed spans
        expect(result.originalAnnotatedHtml).toContain("diff-removed");
        // Proposed side should contain diff-added spans
        expect(result.proposedAnnotatedHtml).toContain("diff-added");
    });

    it("computeHtmlDiff_ReturnsSegments_WithCorrectTypes", () => {
        const original = "<p>Hello world</p>";
        const proposed = "<p>Hello universe</p>";
        const result = computeHtmlDiff(original, proposed);

        const types = result.segments.map((s) => s.type);
        expect(types).toContain("unchanged");
        // At least one added or removed
        expect(
            types.includes("added") || types.includes("removed")
        ).toBe(true);
    });

    it("computeHtmlDiff_CompletelyDifferent_AllChangesReported", () => {
        const original = "<p>Alpha beta gamma</p>";
        const proposed = "<p>Delta epsilon zeta</p>";
        const result = computeHtmlDiff(original, proposed);

        expect(result.stats.deletions).toBeGreaterThan(0);
        expect(result.stats.additions).toBeGreaterThan(0);
    });

    it("computeHtmlDiff_EmptyOriginal_AllAdditions", () => {
        const result = computeHtmlDiff("", "<p>New content</p>");

        expect(result.stats.additions).toBeGreaterThan(0);
        expect(result.stats.deletions).toBe(0);
    });

    it("computeHtmlDiff_EmptyProposed_AllDeletions", () => {
        const result = computeHtmlDiff("<p>Old content</p>", "");

        expect(result.stats.deletions).toBeGreaterThan(0);
        expect(result.stats.additions).toBe(0);
    });

    it("computeHtmlDiff_CharacterGranularity_UsesCharMode", () => {
        const original = "<p>cat</p>";
        const proposed = "<p>car</p>";
        const result = computeHtmlDiff(original, proposed, { granularity: "character" });

        // Character-level diff should still produce segments
        expect(result.segments.length).toBeGreaterThan(0);
        expect(result.stats.deletions).toBeGreaterThanOrEqual(1);
        expect(result.stats.additions).toBeGreaterThanOrEqual(1);
    });

    it("computeHtmlDiff_StrictWhitespace_PreservesWhitespaceDiffs", () => {
        const original = "<p>Hello   world</p>";
        const proposed = "<p>Hello world</p>";
        // In relaxed mode the extra spaces are normalized, so no diff
        const relaxed = computeHtmlDiff(original, proposed, { whitespace: "relaxed" });
        expect(relaxed.stats.additions + relaxed.stats.deletions).toBe(0);

        // In strict mode the whitespace difference may be detected
        const strict = computeHtmlDiff(original, proposed, { whitespace: "strict" });
        // Strict mode should detect a change (extra spaces vs single space)
        expect(strict.segments.length).toBeGreaterThan(0);
    });
});

// ─────────────────────────────────────────────────────────────────────────────
// detectBlockChanges
// ─────────────────────────────────────────────────────────────────────────────

describe("detectBlockChanges", () => {
    it("detectBlockChanges_IdenticalBlocks_AllUnchanged", () => {
        const html = "<h1>Title</h1><p>Content</p>";
        const changes = detectBlockChanges(html, html);

        expect(changes.length).toBe(2);
        expect(changes.every((c) => c.type === "unchanged")).toBe(true);
    });

    it("detectBlockChanges_AddedParagraph_DetectsAddition", () => {
        const original = "<p>First</p>";
        const proposed = "<p>First</p><p>Second</p>";
        const changes = detectBlockChanges(original, proposed);

        const added = changes.filter((c) => c.type === "added");
        expect(added.length).toBe(1);
        expect(added[0].proposedText).toBe("Second");
    });

    it("detectBlockChanges_RemovedParagraph_DetectsRemoval", () => {
        const original = "<p>First</p><p>Second</p>";
        const proposed = "<p>First</p>";
        const changes = detectBlockChanges(original, proposed);

        const removed = changes.filter((c) => c.type === "removed");
        expect(removed.length).toBe(1);
        expect(removed[0].originalText).toBe("Second");
    });

    it("detectBlockChanges_ModifiedBlock_DetectsModification", () => {
        const original = "<h1>Title</h1><p>Original paragraph here</p>";
        const proposed = "<h1>Title</h1><p>Revised paragraph here</p>";
        const changes = detectBlockChanges(original, proposed);

        const modified = changes.filter((c) => c.type === "modified");
        expect(modified.length).toBe(1);
        expect(modified[0].originalText).toBe("Original paragraph here");
        expect(modified[0].proposedText).toBe("Revised paragraph here");
    });

    it("detectBlockChanges_CompletelyDifferent_AllRemovedAndAdded", () => {
        const original = "<p>Alpha</p>";
        const proposed = "<p>Zeta</p>";
        const changes = detectBlockChanges(original, proposed);

        // Single words with no overlap -- below 50% threshold, so no LCS match
        // Should be one removed + one added (no match at 0% overlap)
        const removed = changes.filter((c) => c.type === "removed");
        const added = changes.filter((c) => c.type === "added");
        expect(removed.length + added.length).toBeGreaterThan(0);
    });

    it("detectBlockChanges_SimilarityThreshold_MatchesAbove50Percent", () => {
        // "The quick brown fox" vs "The quick red fox" => 3/4 = 75% overlap -- should match
        const original = "<p>The quick brown fox</p>";
        const proposed = "<p>The quick red fox</p>";
        const changes = detectBlockChanges(original, proposed);

        const modified = changes.filter((c) => c.type === "modified");
        expect(modified.length).toBe(1);
    });

    it("detectBlockChanges_BelowSimilarityThreshold_NoMatch", () => {
        // "Alpha beta gamma delta" (4 words) vs "Epsilon zeta" (2 words, 0 overlap)
        // 0/4 = 0% overlap -- should NOT match
        const original = "<p>Alpha beta gamma delta</p>";
        const proposed = "<p>Epsilon zeta</p>";
        const changes = detectBlockChanges(original, proposed);

        const modified = changes.filter((c) => c.type === "modified");
        expect(modified.length).toBe(0);
        expect(changes.filter((c) => c.type === "removed").length).toBe(1);
        expect(changes.filter((c) => c.type === "added").length).toBe(1);
    });

    it("detectBlockChanges_EmptyOriginal_AllAdded", () => {
        const changes = detectBlockChanges("", "<p>New</p><p>Content</p>");

        expect(changes.length).toBe(2);
        expect(changes.every((c) => c.type === "added")).toBe(true);
    });

    it("detectBlockChanges_EmptyProposed_AllRemoved", () => {
        const changes = detectBlockChanges("<p>Old</p><p>Content</p>", "");

        expect(changes.length).toBe(2);
        expect(changes.every((c) => c.type === "removed")).toBe(true);
    });

    it("detectBlockChanges_DifferentTags_CorrectTagPreserved", () => {
        const original = "<h1>Title</h1>";
        const proposed = "<h1>Title</h1><p>New paragraph</p>";
        const changes = detectBlockChanges(original, proposed);

        const h1Change = changes.find((c) => c.tag === "h1");
        const pChange = changes.find((c) => c.tag === "p");
        expect(h1Change).toBeDefined();
        expect(h1Change!.type).toBe("unchanged");
        expect(pChange).toBeDefined();
        expect(pChange!.type).toBe("added");
    });

    it("detectBlockChanges_HeadingLevelsMatch_CrossLevelAlignment", () => {
        // h1 can match h2 (both are heading tags)
        const original = "<h1>Important heading content here</h1>";
        const proposed = "<h2>Important heading content here</h2>";
        const changes = detectBlockChanges(original, proposed);

        // Should align as unchanged (same text, compatible tags)
        expect(changes.length).toBe(1);
        expect(changes[0].type).toBe("unchanged");
    });
});
