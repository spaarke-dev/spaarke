using System.Globalization;
using System.Text.RegularExpressions;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace Sprk.Bff.Api.Services.Compose;

/// <summary>
/// FR-01 / FR-03 / FR-07 (E1) — the retained-original REDLINE SYNTHESIZER. Given the retained load-time
/// original <c>.docx</c> (the E1 baseline) and the set of editor paragraphs that changed (each carrying
/// its <c>w14:paraId</c> + new settled text), it produces the redline-marked <c>.docx</c> by editing
/// EACH changed paragraph in place: a word-level diff of old→new text, emitted as native <c>w:ins</c>/
/// <c>w:del</c> revision runs, with author attribution. Every paragraph the user did not touch — and every
/// structural part (styles, numbering, headers/footers, footnotes, tables incl. nested) — is left
/// byte-untouched. This is the E1 "delta onto the retained original" engine.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why we synthesize instead of diffing whole documents (design §4.2, Option C — supersedes Approach
/// A/B)</b>: R3 originally planned to splice the edited paragraphs into a copy of the original and run the
/// Docxodus <c>WmlComparer</c> to synthesize the redline (Approach A). The NFR-09 hardening gate (task 003)
/// proved on REAL firm templates that PowerTools <c>WmlComparer</c> — in BOTH 6.4.0 (net8) and 7.1.0
/// (net10) — <b>strips <c>w14:paraId</c></b> (→ its internal <c>pt14:Unid</c>) and <b>drops unchanged
/// tables</b>, violating FR-07/NFR-07 and the E2 anchor substrate. But we do not need a general-purpose
/// differ: the client already tells us EXACTLY which paragraphs changed and their new text (paraId-keyed,
/// E2). So we synthesize the tracked-change markup ourselves, touching only the K edited paragraphs. This
/// preserves <c>w14:paraId</c> + every structural part BY CONSTRUCTION (the id is a <c>w:p</c> attribute
/// that survives a run-content rewrite) and removes the Docxodus dependency entirely. Validated on the
/// real Common Paper CSA (all 345 paraIds + all 6 tables preserved; task-003 hardening report).
/// </para>
/// <para>
/// <b>The adeu asymmetry (design §4.2)</b>: the editor produces typed TEXT edits; the engine produces
/// valid OOXML. Each edited paragraph is rewritten as its original <c>w:p</c> with its run content replaced
/// by the diff spans — unchanged words as normal runs, inserted words wrapped in <c>w:ins</c>, deleted
/// words wrapped in <c>w:del</c> (as <c>w:delText</c>, EDGE-4) — while its <c>w14:paraId</c>, <c>w:pPr</c>
/// (style/numbering/justification), and base run <c>w:rPr</c> (font/size) are PRESERVED.
/// </para>
/// <para>
/// <b>Fail-fast on an unmatched paraId (FR-12)</b>: every edited paraId is resolved against the retained
/// original via <see cref="ComposeParaIdSpliceMap"/> (task 012) BEFORE any mutation. If ANY id matches no
/// original paragraph (e.g. a client split minted an id the original never had), the whole synthesis throws
/// <see cref="ComposeRedlineException"/> and the document is left untouched — never a partial or wrong write.
/// </para>
/// <para>
/// <b>Pure — no I/O, no AI, no Graph, no external diff engine (ADR-007 / ADR-013 / NFR-05 / NFR-01)</b>:
/// operates only on the in-memory bytes the caller (task 022 <c>SaveAsync</c>) already fetched behind the
/// <c>SpeFileStore</c> facade. No <c>Microsoft.Graph</c> type, no <c>IOpenAiClient</c>, no routing type
/// (Tier-1 NetArchTest <c>ADR013_ComposeFacade</c> enforces), and — unlike the retired comparer path — no
/// NuGet dependency (<c>DocumentFormat.OpenXml</c> is already referenced; Docxodus is gone). Thread-safe
/// stateless — a shared singleton (ADR-010). Wiring into the inverted <c>SaveAsync</c> is task 022.
/// </para>
/// </remarks>
public sealed class ComposeParagraphRedlineSynthesizer
{
    // Word tokens = "non-space run + trailing whitespace" so a token-sequence concatenation reproduces the
    // source text exactly (whitespace is carried, never lost or normalized).
    private static readonly Regex WordToken = new(@"\S+\s*", RegexOptions.Compiled);

    /// <summary>
    /// Synthesizes the tracked-change redline for <paramref name="editedParagraphs"/> onto a COPY of
    /// <paramref name="retainedOriginal"/>, keyed by <c>w14:paraId</c>, and returns the redline-marked
    /// bytes. Exactly the supplied paragraphs are rewritten (old→new word diff as <c>w:ins</c>/<c>w:del</c>,
    /// paraId + pPr + base rPr preserved); every other paragraph and all structure passes through
    /// byte-untouched. An empty <paramref name="editedParagraphs"/> returns a structural round-trip of the
    /// original (no revisions).
    /// </summary>
    /// <param name="retainedOriginal">The retained load-time original <c>.docx</c> bytes (baseline).</param>
    /// <param name="editedParagraphs">The editor paragraphs that changed, each with its paraId + new text.
    /// Duplicate paraIds are rejected (an editor paragraph maps to exactly one original).</param>
    /// <param name="author">Revision author attribution (surfaces as Word's revision author).</param>
    /// <param name="revisionTimestamp">Timestamp stamped on the emitted revisions (<c>w:date</c>).</param>
    /// <exception cref="ArgumentException"><paramref name="retainedOriginal"/> is empty or
    /// <paramref name="author"/> is null/whitespace.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="editedParagraphs"/> is null.</exception>
    /// <exception cref="ComposeRedlineException">The bytes are not a readable DOCX, an edited paraId matches
    /// no original paragraph, or the same paraId is supplied twice.</exception>
    public byte[] SynthesizeRedline(
        ReadOnlyMemory<byte> retainedOriginal,
        IReadOnlyList<ComposeEditedParagraph> editedParagraphs,
        string author,
        DateTimeOffset revisionTimestamp)
    {
        if (retainedOriginal.IsEmpty)
        {
            throw new ArgumentException("retainedOriginal is required and must be non-empty.", nameof(retainedOriginal));
        }

        ArgumentNullException.ThrowIfNull(editedParagraphs);
        if (string.IsNullOrWhiteSpace(author))
        {
            throw new ArgumentException("author is required for revision attribution.", nameof(author));
        }

        // Reject duplicate edited paraIds up-front (an editor paragraph maps to exactly one original).
        var byParaId = new Dictionary<string, ComposeEditedParagraph>(StringComparer.OrdinalIgnoreCase);
        foreach (var edit in editedParagraphs)
        {
            if (string.IsNullOrEmpty(edit.ParaId))
            {
                throw new ComposeRedlineException("An edited paragraph carried no w14:paraId — every edit must be keyed by its paraId.");
            }

            if (!byParaId.TryAdd(edit.ParaId.ToUpperInvariant(), edit))
            {
                throw new ComposeRedlineException(
                    $"Duplicate edited w14:paraId '{edit.ParaId.ToUpperInvariant()}' — an editor paragraph maps to exactly one original paragraph.");
            }
        }

        using var buffer = new MemoryStream(retainedOriginal.Length);
        buffer.Write(retainedOriginal.Span);
        buffer.Position = 0;

        WordprocessingDocument doc;
        try
        {
            doc = WordprocessingDocument.Open(buffer, isEditable: true);
        }
        catch (Exception ex) when (ex is OpenXmlPackageException or FileFormatException or InvalidDataException or ArgumentOutOfRangeException)
        {
            throw new ComposeRedlineException("The supplied retained-original bytes are not a readable .docx (WordprocessingML) package.", ex);
        }

        using (doc)
        {
            var body = doc.MainDocumentPart?.Document?.Body
                ?? throw new ComposeRedlineException("The retained-original document has no body to synthesize onto.");

            // Task 012 FR-12 splice key: resolve ALL edited ids BEFORE any mutation (fail-fast — a single
            // unmatched id fails the whole synthesis so no partial/wrong write can land).
            var index = ComposeParaIdSpliceMap.BuildParagraphIndex(body);
            var resolution = ComposeParaIdSpliceMap.Resolve(index, byParaId.Keys);
            if (!resolution.IsFullyMatched)
            {
                throw new ComposeRedlineException(
                    "One or more edited paragraphs have a w14:paraId that matches no paragraph in the retained original: " +
                    string.Join(", ", resolution.Unmatched) +
                    ". The synthesis was aborted — no paragraph was modified.");
            }

            // EDGE-3: seed revision ids past any existing w:ins/w:del id so newly-emitted ids are unique.
            var nextId = SeedRevisionId(body);
            var date = revisionTimestamp.UtcDateTime;

            foreach (var (paraIdKey, paragraph) in resolution.Matched)
            {
                RewriteParagraphAsRedline(paragraph, byParaId[paraIdKey].NewText ?? string.Empty, author, date, ref nextId);
            }

            doc.MainDocumentPart!.Document.Save();
        }

        return buffer.ToArray();
    }

    /// <summary>
    /// Rewrites <paramref name="paragraph"/> as a tracked-change redline of its current text → <paramref
    /// name="newText"/>: PRESERVES the paragraph's <c>w14:paraId</c> (an attribute — untouched by child
    /// edits), its <c>w:pPr</c>, and the base run <c>w:rPr</c>; replaces the run content with the word-diff
    /// spans (unchanged → normal run, inserted → <c>w:ins</c>, deleted → <c>w:del</c>/<c>w:delText</c>).
    /// </summary>
    private static void RewriteParagraphAsRedline(Paragraph paragraph, string newText, string author, DateTime date, ref int nextId)
    {
        var oldText = paragraph.InnerText;
        var pPr = paragraph.GetFirstChild<ParagraphProperties>()?.CloneNode(true) as ParagraphProperties;
        var baseRunProps = paragraph.Descendants<Run>().FirstOrDefault()?.GetFirstChild<RunProperties>()?.CloneNode(true) as RunProperties;

        var spans = WordDiff(oldText, newText);

        // Clear only child content — the w14:paraId is an ATTRIBUTE on the w:p and survives this.
        paragraph.RemoveAllChildren();
        if (pPr is not null)
        {
            paragraph.AppendChild(pPr);
        }

        foreach (var (op, text) in spans)
        {
            switch (op)
            {
                case DiffOp.Equal:
                    paragraph.AppendChild(TextRun(text, baseRunProps));
                    break;
                case DiffOp.Insert:
                    paragraph.AppendChild(new InsertedRun(TextRun(text, baseRunProps))
                    {
                        Id = (nextId++).ToString(CultureInfo.InvariantCulture),
                        Author = author,
                        Date = date,
                    });
                    break;
                case DiffOp.Delete:
                    paragraph.AppendChild(new DeletedRun(DeletedTextRun(text, baseRunProps))
                    {
                        Id = (nextId++).ToString(CultureInfo.InvariantCulture),
                        Author = author,
                        Date = date,
                    });
                    break;
            }
        }
    }

    private static Run TextRun(string text, RunProperties? baseRunProps)
    {
        var run = new Run();
        if (baseRunProps is not null)
        {
            run.AppendChild((RunProperties)baseRunProps.CloneNode(true));
        }

        run.AppendChild(new Text(text) { Space = SpaceProcessingModeValues.Preserve });
        return run;
    }

    private static Run DeletedTextRun(string text, RunProperties? baseRunProps)
    {
        var run = new Run();
        if (baseRunProps is not null)
        {
            run.AppendChild((RunProperties)baseRunProps.CloneNode(true));
        }

        // EDGE-4 (DocxAnnotationWriter): inside w:del the text element MUST be w:delText, not w:t.
        run.AppendChild(new DeletedText(text) { Space = SpaceProcessingModeValues.Preserve });
        return run;
    }

    private enum DiffOp
    {
        Equal,
        Insert,
        Delete,
    }

    /// <summary>
    /// Word-level LCS diff of <paramref name="oldText"/> → <paramref name="newText"/>, returned as merged
    /// spans (consecutive same-op tokens collapse to one span for a minimal run count). Word granularity
    /// matches how Word itself represents tracked changes on prose.
    /// </summary>
    private static List<(DiffOp Op, string Text)> WordDiff(string oldText, string newText)
    {
        var a = Tokenize(oldText);
        var b = Tokenize(newText);
        int n = a.Count, m = b.Count;

        // LCS length table (suffix DP).
        var dp = new int[n + 1, m + 1];
        for (var i = n - 1; i >= 0; i--)
        {
            for (var j = m - 1; j >= 0; j--)
            {
                dp[i, j] = string.Equals(a[i], b[j], StringComparison.Ordinal)
                    ? dp[i + 1, j + 1] + 1
                    : Math.Max(dp[i + 1, j], dp[i, j + 1]);
            }
        }

        var raw = new List<(DiffOp, string)>();
        int x = 0, y = 0;
        while (x < n && y < m)
        {
            if (string.Equals(a[x], b[y], StringComparison.Ordinal))
            {
                raw.Add((DiffOp.Equal, a[x]));
                x++;
                y++;
            }
            else if (dp[x + 1, y] >= dp[x, y + 1])
            {
                raw.Add((DiffOp.Delete, a[x]));
                x++;
            }
            else
            {
                raw.Add((DiffOp.Insert, b[y]));
                y++;
            }
        }

        while (x < n)
        {
            raw.Add((DiffOp.Delete, a[x]));
            x++;
        }

        while (y < m)
        {
            raw.Add((DiffOp.Insert, b[y]));
            y++;
        }

        // Merge consecutive same-op tokens into a single span.
        var merged = new List<(DiffOp, string)>();
        foreach (var (op, tok) in raw)
        {
            if (merged.Count > 0 && merged[^1].Item1 == op)
            {
                merged[^1] = (op, merged[^1].Item2 + tok);
            }
            else
            {
                merged.Add((op, tok));
            }
        }

        return merged;
    }

    private static List<string> Tokenize(string text) =>
        string.IsNullOrEmpty(text)
            ? new List<string>()
            : WordToken.Matches(text).Select(m => m.Value).ToList();

    /// <summary>EDGE-3: seed past the max numeric <c>w:id</c> on any existing revision so new ids are unique.</summary>
    private static int SeedRevisionId(Body body)
    {
        var max = 0;
        foreach (var id in body.Descendants<InsertedRun>().Select(i => i.Id?.Value)
                     .Concat(body.Descendants<DeletedRun>().Select(d => d.Id?.Value)))
        {
            if (int.TryParse(id, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) && value > max)
            {
                max = value;
            }
        }

        return max + 1;
    }
}

/// <summary>
/// FR-01 input: one editor paragraph that changed — its <paramref name="ParaId"/> (<c>w14:paraId</c>, the
/// E2 splice key mapping it to exactly one original OOXML paragraph) and its rebuilt <paramref
/// name="NewText"/> (the paragraph's new settled text; the "typed text edit" side of the adeu asymmetry —
/// the synthesizer produces the valid tracked-change OOXML). Reuses the E2 paraId identity rather than a
/// parallel schema.
/// </summary>
public sealed record ComposeEditedParagraph(string ParaId, string NewText);

/// <summary>
/// Raised when the paraId-keyed redline synthesis cannot proceed: the retained-original bytes are
/// unreadable, an edited paraId matches no original paragraph, or a paraId was supplied twice. Distinct
/// from <see cref="DocxAnnotationException"/> (the annotation-write path) so the caller (task 022) can
/// surface a synthesis-specific failure. FR-12: an unmatched paraId is a HANDLED error, never a silent no-op.
/// </summary>
public sealed class ComposeRedlineException : Exception
{
    public ComposeRedlineException(string message) : base(message) { }

    public ComposeRedlineException(string message, Exception innerException) : base(message, innerException) { }
}
