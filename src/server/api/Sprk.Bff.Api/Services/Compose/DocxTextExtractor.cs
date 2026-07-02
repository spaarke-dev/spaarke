using System.Text;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace Sprk.Bff.Api.Services.Compose;

/// <summary>
/// Default <see cref="IDocxTextExtractor"/> using the DocumentFormat.OpenXml SDK to
/// walk a WordprocessingDocument's main body and concatenate paragraph text.
/// </summary>
/// <remarks>
/// <para>
/// Project: spaarkeai-compose-r1 · Phase 8 · task 094.
/// </para>
/// <para>
/// Extraction strategy:
/// <list type="number">
///   <item>Open the incoming stream as a read-only <see cref="WordprocessingDocument"/>.</item>
///   <item>Walk the main document part's <see cref="Body"/> descendants for
///   <see cref="Paragraph"/> nodes (top-level prose blocks).</item>
///   <item>For each paragraph, concatenate the inner <see cref="Text"/> runs into a
///   line, then join lines with a single newline.</item>
///   <item>Truncate at <paramref>maxCharacters</paramref> with the documented suffix
///   when the concatenated buffer exceeds the bound.</item>
/// </list>
/// </para>
/// <para>
/// Intentionally NOT extracted (per POML 094):
/// <list type="bullet">
///   <item>Header / footer parts — noise for AI context; would inflate prompt budget.</item>
///   <item>Comments (<c>word/comments.xml</c>) — annotations, not prose.</item>
///   <item>Revision marks (tracked changes) — R1 does not preserve; R3 fidelity work.</item>
///   <item>Tables of contents fields — auto-generated navigation, not authored prose.</item>
///   <item>Text boxes / SmartArt / embedded objects — out-of-band content.</item>
/// </list>
/// The R3 Word-fidelity project (<c>projects/spaarkeai-compose-r3/</c>) may enrich
/// this extractor for advanced summarize/analysis consumers.
/// </para>
/// </remarks>
public sealed class DocxTextExtractor : IDocxTextExtractor
{
    /// <inheritdoc />
    public Task<string> ExtractPlainTextAsync(
        Stream docxBytes,
        int maxCharacters = 100_000,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(docxBytes);
        if (maxCharacters < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxCharacters),
                maxCharacters,
                "maxCharacters must be at least 1.");
        }

        cancellationToken.ThrowIfCancellationRequested();

        // OpenXml SDK throws FileFormatException (or its base InvalidOperationException /
        // OpenXmlPackageException) on malformed input. Normalize to InvalidDataException
        // per the interface contract so callers have a single documented exception type
        // to catch at the endpoint layer.
        WordprocessingDocument doc;
        try
        {
            doc = WordprocessingDocument.Open(docxBytes, isEditable: false);
        }
        catch (Exception ex) when (ex is FileFormatException
            or OpenXmlPackageException
            or InvalidOperationException
            or InvalidDataException)
        {
            throw new InvalidDataException(
                "The provided stream is not a valid Open XML WordprocessingML (DOCX) document.",
                ex);
        }

        using (doc)
        {
            var body = doc.MainDocumentPart?.Document?.Body;
            if (body is null)
            {
                return Task.FromResult(string.Empty);
            }

            // Materialize paragraph texts once so we can walk them twice without
            // re-descending the OpenXml tree. Empty paragraphs are still preserved so
            // the truncation overshoot counts them accurately.
            var paragraphTexts = new List<string>();
            foreach (var paragraph in body.Descendants<Paragraph>())
            {
                cancellationToken.ThrowIfCancellationRequested();
                paragraphTexts.Add(ConcatenateParagraphText(paragraph));
            }

            var builder = new StringBuilder();
            var truncatedAt = -1;
            var truncatedParagraphResidue = 0;

            for (var i = 0; i < paragraphTexts.Count; i++)
            {
                var paragraphText = paragraphTexts[i];
                if (paragraphText.Length == 0)
                {
                    continue;
                }

                if (builder.Length > 0)
                {
                    // Paragraph separator: single newline. Consumers can further split
                    // on \n\n if they need visual spacing; the AI orchestrator does
                    // its own prompt-shaping.
                    builder.Append('\n');
                }

                var remaining = maxCharacters - builder.Length;
                if (paragraphText.Length <= remaining)
                {
                    builder.Append(paragraphText);
                    continue;
                }

                if (remaining > 0)
                {
                    builder.Append(paragraphText, 0, remaining);
                }
                truncatedAt = i;
                truncatedParagraphResidue = paragraphText.Length - Math.Max(remaining, 0);
                break;
            }

            if (truncatedAt >= 0)
            {
                // Sum residue of the paragraph that overran + all remaining
                // paragraphs' characters (with 1 char per paragraph break) so the
                // suffix conveys the full delta. Helps operators size prompt budgets
                // from telemetry.
                var overshoot = truncatedParagraphResidue;
                for (var j = truncatedAt + 1; j < paragraphTexts.Count; j++)
                {
                    var text = paragraphTexts[j];
                    if (text.Length == 0)
                    {
                        continue;
                    }
                    overshoot += text.Length + 1; // +1 for the paragraph-break newline
                }

                builder.Append(" [TRUNCATED — ")
                    .Append(overshoot)
                    .Append(" characters more]");
            }

            return Task.FromResult(builder.ToString());
        }
    }

    /// <summary>
    /// Concatenates all <see cref="Text"/> runs inside a <see cref="Paragraph"/> in
    /// document order. Skips text carried by <see cref="Comments"/> or header/footer
    /// parts because those are separate document parts — walking <c>Body.Descendants</c>
    /// naturally excludes them.
    /// </summary>
    private static string ConcatenateParagraphText(Paragraph paragraph)
    {
        var builder = new StringBuilder();
        foreach (var text in paragraph.Descendants<Text>())
        {
            builder.Append(text.Text);
        }
        return builder.ToString();
    }
}
