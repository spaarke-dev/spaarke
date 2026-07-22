using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace Sprk.Bff.Api.Services.Compose;

/// <summary>
/// C2 fix (UAT 2026-07-20) — completes the "apply minted paraIds physically to the OOXML" step that
/// <see cref="ParaIdPreParser"/> flagged in its own remark ("the map, not the bytes, carries minted ids;
/// task 020/022 apply them when a splice needs them physically") but which was never wired.
/// </summary>
/// <remarks>
/// <para>
/// <b>The bug it fixes.</b> On Load, <see cref="ParaIdPreParser"/> mints a <c>w14:paraId</c> for every
/// paragraph the source <c>.docx</c> left id-less, returns those ids in the map (stamped onto the editor
/// nodes client-side), but opens the package READ-ONLY — the minted ids never enter the bytes. The
/// retained-original baseline resolved at save (<see cref="ComposeService.ResolveSaveBaselineAsync"/>)
/// therefore lacks them. When the user edits (or accepts a redline on) such a paragraph,
/// <c>collectEditedParagraphs</c> emits its minted paraId, and
/// <see cref="ComposeParagraphRedlineSynthesizer"/> → <see cref="ComposeParaIdSpliceMap"/> cannot locate
/// it (the baseline paragraph physically carries no id) → <c>ComposeRedlineException</c>, the whole save
/// aborted. Uploaded docs are worse still: they get NO server pre-parse, so ALL their editor ids are
/// client-minted and absent from the baseline.
/// </para>
/// <para>
/// <b>The fix.</b> At save time the client sends the ordered <c>{ index, paraId, text }</c> map it built
/// from its LOAD-TIME snapshot (document order, reject-state text). This stamper walks the baseline's
/// paragraphs in the SAME document order and, for each id-LESS paragraph, stamps the client's paraId
/// IFF the baseline paragraph's text matches the snapshot text for that position (folded +
/// whitespace-normalized — <see cref="ComposeTextFold.Normalize"/>). After this the baseline physically
/// carries every id the editor uses, so the synthesizer + E2 anchoring resolve cleanly and the SAVED
/// document is self-consistent for future round-trips.
/// </para>
/// <para>
/// <b>Safety (never a wrong write).</b> (1) FILL-GAPS ONLY — a paragraph that already carries an id is
/// never touched (an existing real id is authoritative). (2) TEXT-VERIFIED — a minted id is stamped only
/// when the baseline paragraph at that position actually holds the text the client's snapshot attributes
/// to that paraId, so a document-order divergence (e.g. mammoth merging paragraphs on an uploaded doc)
/// can never stamp the id onto the wrong paragraph — it simply skips (leaving the pre-fix behavior, an
/// honest synthesis error, for that paragraph rather than a silent corruption). (3) COUNT-GATED —
/// if the baseline paragraph count differs from the map count the alignment is untrustworthy and NOTHING
/// is stamped. (4) COLLISION-CHECKED — a candidate id already present anywhere in the part is skipped.
/// (5) FAIL-OPEN — any parse/mutation error returns the baseline unchanged (the save proceeds exactly as
/// before the fix).
/// </para>
/// <para>
/// Pure OOXML, no I/O / AI / DI reach (ADR-013 / NFR-05); no package delta (DocumentFormat.OpenXml is
/// already a BFF dep). Privacy (ADR-015 Tier 3): the map's text is document content — used in-memory for
/// the equality check only, NEVER logged.
/// </para>
/// </remarks>
public sealed class ComposeBaselineParaIdStamper
{
    // The largest permitted w14:paraId (ST_LongHexNumber, 0 < x < 0x80000000) — mirrors ParaIdPreParser.
    private const uint MaxParaId = 0x80000000u;

    /// <summary>
    /// Returns <paramref name="baseline"/> with the client's minted paraIds stamped onto its id-less
    /// paragraphs (see class remarks for the safety contract). Returns the baseline UNCHANGED when the
    /// map is empty, the counts disagree, no paragraph qualifies, or the bytes are not a readable docx.
    /// </summary>
    public byte[] Stamp(ReadOnlyMemory<byte> baseline, IReadOnlyList<ComposeBaselineParaId>? map)
    {
        if (baseline.IsEmpty || map is null || map.Count == 0)
        {
            return baseline.ToArray();
        }

        using var buffer = new MemoryStream(baseline.Length + 4096);
        buffer.Write(baseline.Span);
        buffer.Position = 0;

        WordprocessingDocument doc;
        try
        {
            doc = WordprocessingDocument.Open(buffer, isEditable: true);
        }
        catch (Exception ex) when (ex is OpenXmlPackageException or FileFormatException or InvalidDataException or ArgumentOutOfRangeException)
        {
            // Not a readable docx (e.g. a born-in-editor render path that never reaches here, or a
            // corrupt baseline) — fail open, let the downstream synthesizer/writer surface the real error.
            return baseline.ToArray();
        }

        using (doc)
        {
            var body = doc.MainDocumentPart?.Document?.Body;
            if (body is null)
            {
                return baseline.ToArray();
            }

            // Same recursive, document-ordered walk ParaIdPreParser + ComposeParaIdSpliceMap use.
            var paragraphs = body.Descendants<Paragraph>().ToList();

            // Count gate: the client's map is one entry per editor paragraph in document order. If the
            // baseline paragraph count differs, index alignment is untrustworthy — stamp nothing.
            if (paragraphs.Count != map.Count)
            {
                return baseline.ToArray();
            }

            // Collision set: every id physically present anywhere in the part.
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in paragraphs)
            {
                var id = p.ParagraphId?.Value;
                if (!string.IsNullOrEmpty(id))
                {
                    seen.Add(id!);
                }
            }

            var mutated = false;
            foreach (var entry in map)
            {
                if (entry.Index < 0 || entry.Index >= paragraphs.Count)
                {
                    continue;
                }

                var paragraph = paragraphs[entry.Index];

                // (1) Fill-gaps only — never overwrite an existing (authoritative) id.
                if (!string.IsNullOrEmpty(paragraph.ParagraphId?.Value))
                {
                    continue;
                }

                // Validate the candidate id: OOXML-shaped, non-empty, not already used.
                var candidate = NormalizeParaId(entry.ParaId);
                if (candidate is null || seen.Contains(candidate))
                {
                    continue;
                }

                // (2) Text-verified — only stamp when the baseline paragraph actually holds the text the
                // client's snapshot attributes to this paraId (folded + whitespace-normalized). This makes
                // a document-order divergence a SKIP, never a wrong-paragraph stamp.
                if (!TextMatches(paragraph, entry.Text))
                {
                    continue;
                }

                paragraph.ParagraphId = new HexBinaryValue(candidate);
                seen.Add(candidate);
                mutated = true;
            }

            if (!mutated)
            {
                return baseline.ToArray();
            }

            doc.MainDocumentPart!.Document.Save();
        }

        return buffer.ToArray();
    }

    /// <summary>True when the paragraph's settled text equals the client snapshot text under the shared
    /// fold + whitespace normalization. A null/empty snapshot text is treated as "no assertion" and does
    /// NOT authorize a stamp (return false) — we only stamp on a positive text match.</summary>
    private static bool TextMatches(Paragraph paragraph, string? snapshotText)
    {
        if (string.IsNullOrEmpty(snapshotText))
        {
            return false;
        }

        return string.Equals(
            ComposeTextFold.Normalize(paragraph.InnerText),
            ComposeTextFold.Normalize(snapshotText),
            StringComparison.Ordinal);
    }

    /// <summary>Uppercase 8-hex ST_LongHexNumber form of a candidate paraId, or null if it is not a valid
    /// in-range OOXML id. Mirrors ParaIdPreParser's range rule (0 &lt; x &lt; 0x80000000).</summary>
    private static string? NormalizeParaId(string? candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return null;
        }

        var trimmed = candidate.Trim();
        if (!uint.TryParse(trimmed, System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out var value))
        {
            return null;
        }

        if (value == 0 || value >= MaxParaId)
        {
            return null;
        }

        return value.ToString("X8", System.Globalization.CultureInfo.InvariantCulture);
    }
}

/// <summary>
/// One entry of the client's load-time paraId map, sent on save so the server can stamp minted ids
/// physically into the retained-original baseline (C2). <paramref name="Index"/> is the paragraph's
/// zero-based document-order position; <paramref name="ParaId"/> is the editor's <c>w14:paraId</c>;
/// <paramref name="Text"/> is the paragraph's LOAD-TIME reject-state settled text (the verification key —
/// document content, in-memory only, never logged).
/// </summary>
public sealed record ComposeBaselineParaId(int Index, string ParaId, string? Text);
