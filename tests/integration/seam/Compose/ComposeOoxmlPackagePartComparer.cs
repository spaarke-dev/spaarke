// Task 004 (spaarkeai-compose-r4, NFR-01) — OOXML package-part byte-comparison helper. This is
// the acceptance-evidence engine for the round-trip byte-diff harness: for a given
// (original bytes, round-tripped bytes) pair, it classifies every part in the .docx OPC package
// into two buckets and compares each:
//
//   - "untouched" parts — every package part EXCEPT word/document.xml (styles, numbering,
//     headers/footers, footnotes/endnotes, theme, custom XML, embedded objects/media, core/app
//     properties, ...). MUST be byte-identical after a save, per NFR-01 + design.md §5.0
//     ("package parts never opened are copied verbatim").
//   - the EDITED part, word/document.xml — compared two ways: (a) raw byte-identity, and
//     (b) "structurally faithful" (SDK re-serialize equivalence — same paragraph set, same
//     per-paragraph OuterXml). The STRICT switch (`strictDocumentXmlByteIdentity`) selects which
//     of the two gates <see cref="ComparisonResult.Passes"/> enforces — this is the "switchable
//     hook for the stricter XML-splice byte-identity option" the task brief + spec Unresolved
//     Question ("True byte-identity within document.xml") calls for. Both bytes-identical AND
//     structurally-faithful are ALWAYS computed and exposed regardless of the switch, so a caller
//     can assert on whichever bar it needs.
//
// TODO(task 034 — ComposeShadowPatchEngine + non-empty operation log): today (task 004 scope) the
// harness only exercises the NO-OP save (empty operation log) — see
// ComposeNoOpRoundTripByteDiffSeamTests. In that case document.xml is ALSO byte-identical (the
// no-op passthrough in ComposeService.SaveAsync never re-serializes it when EditedParagraphs is
// empty), so this comparer's document.xml checks are currently a stronger-than-required proof.
// Once task 034 points a variant of this harness at the real Patch Engine with a NON-EMPTY
// operation log, document.xml will legitimately differ from the original (the edit itself) while
// every OTHER part must remain byte-identical — reuse this same comparer unchanged; only the
// document.xml assertion mode (strict vs structural) needs revisiting per the Unresolved Question.

using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace Sprk.Bff.Api.Tests.Seam.Compose;

internal static class ComposeOoxmlPackagePartComparer
{
    /// <summary>Byte-diff result for a single package part (identified by its package URI).</summary>
    public sealed record PartDiff(
        string Uri,
        bool PresentInOriginal,
        bool PresentInRoundTripped,
        bool ByteIdentical,
        int OriginalLength,
        int RoundTrippedLength);

    /// <summary>
    /// Full comparison result for one (original, round-tripped) document pair.
    /// <see cref="Passes"/> is the single boolean a test asserts on; the rest is diagnostic detail
    /// for a failure message / findings write-up.
    /// </summary>
    public sealed record ComparisonResult(
        IReadOnlyList<PartDiff> UntouchedPartDiffs,
        PartDiff DocumentXmlDiff,
        bool DocumentXmlStructurallyFaithful,
        bool StrictDocumentXmlByteIdentityRequested)
    {
        public bool AllUntouchedPartsByteIdentical => UntouchedPartDiffs.All(d => d.ByteIdentical);

        public IReadOnlyList<PartDiff> UntouchedMismatches =>
            UntouchedPartDiffs.Where(d => !d.ByteIdentical).ToList();

        /// <summary>
        /// The NFR-01 gate: untouched parts are ALWAYS required to be byte-identical; document.xml
        /// is held to the STRICT (byte-identical) bar when <see cref="StrictDocumentXmlByteIdentityRequested"/>
        /// is true, else the looser structural-faithfulness bar.
        /// </summary>
        public bool Passes => AllUntouchedPartsByteIdentical &&
            (StrictDocumentXmlByteIdentityRequested ? DocumentXmlDiff.ByteIdentical : DocumentXmlStructurallyFaithful);

        public string DescribeMismatches()
        {
            if (Passes)
            {
                return "(no mismatches)";
            }

            var lines = new List<string>();
            foreach (var m in UntouchedMismatches)
            {
                lines.Add($"untouched part '{m.Uri}' differs (present: orig={m.PresentInOriginal} roundTripped={m.PresentInRoundTripped}, len: orig={m.OriginalLength} roundTripped={m.RoundTrippedLength})");
            }

            if (StrictDocumentXmlByteIdentityRequested && !DocumentXmlDiff.ByteIdentical)
            {
                lines.Add($"document.xml is not byte-identical (strict mode) — len: orig={DocumentXmlDiff.OriginalLength} roundTripped={DocumentXmlDiff.RoundTrippedLength}");
            }
            else if (!StrictDocumentXmlByteIdentityRequested && !DocumentXmlStructurallyFaithful)
            {
                lines.Add("document.xml is not structurally faithful (paragraph set/order/content diverged)");
            }

            return string.Join("; ", lines);
        }
    }

    /// <summary>
    /// Compares every OOXML package part between <paramref name="original"/> and
    /// <paramref name="roundTripped"/>, classifying <c>word/document.xml</c> separately from every
    /// other part.
    /// </summary>
    public static ComparisonResult Compare(byte[] original, byte[] roundTripped, bool strictDocumentXmlByteIdentity)
    {
        using var originalDoc = WordprocessingDocument.Open(new MemoryStream(original, writable: false), isEditable: false);
        using var roundTrippedDoc = WordprocessingDocument.Open(new MemoryStream(roundTripped, writable: false), isEditable: false);

        var documentXmlUri = originalDoc.MainDocumentPart!.Uri.OriginalString;

        var originalParts = EnumerateAllParts(originalDoc)
            .ToDictionary(p => p.Uri.OriginalString, p => p, StringComparer.Ordinal);
        var roundTrippedParts = EnumerateAllParts(roundTrippedDoc)
            .ToDictionary(p => p.Uri.OriginalString, p => p, StringComparer.Ordinal);

        var untouched = new List<PartDiff>();
        var allUris = originalParts.Keys
            .Union(roundTrippedParts.Keys, StringComparer.Ordinal)
            .Where(u => !string.Equals(u, documentXmlUri, StringComparison.Ordinal))
            .OrderBy(u => u, StringComparer.Ordinal);

        foreach (var uri in allUris)
        {
            var hasOriginal = originalParts.TryGetValue(uri, out var originalPart);
            var hasRoundTripped = roundTrippedParts.TryGetValue(uri, out var roundTrippedPart);

            if (!hasOriginal || !hasRoundTripped)
            {
                var originalLen = hasOriginal ? ReadPartBytes(originalPart!).Length : -1;
                var roundTrippedLen = hasRoundTripped ? ReadPartBytes(roundTrippedPart!).Length : -1;
                untouched.Add(new PartDiff(uri, hasOriginal, hasRoundTripped, ByteIdentical: false, originalLen, roundTrippedLen));
                continue;
            }

            var originalBytes = ReadPartBytes(originalPart!);
            var roundTrippedBytes = ReadPartBytes(roundTrippedPart!);
            var identical = originalBytes.AsSpan().SequenceEqual(roundTrippedBytes);
            untouched.Add(new PartDiff(uri, true, true, identical, originalBytes.Length, roundTrippedBytes.Length));
        }

        var originalDocXmlBytes = ReadPartBytes(originalDoc.MainDocumentPart!);
        var roundTrippedDocXmlBytes = ReadPartBytes(roundTrippedDoc.MainDocumentPart!);
        var docXmlByteIdentical = originalDocXmlBytes.AsSpan().SequenceEqual(roundTrippedDocXmlBytes);
        var docXmlDiff = new PartDiff(
            documentXmlUri, true, true, docXmlByteIdentical, originalDocXmlBytes.Length, roundTrippedDocXmlBytes.Length);

        var structurallyFaithful = docXmlByteIdentical || IsStructurallyFaithful(originalDoc, roundTrippedDoc);

        return new ComparisonResult(untouched, docXmlDiff, structurallyFaithful, strictDocumentXmlByteIdentity);
    }

    /// <summary>
    /// SDK re-serialize equivalence for <c>word/document.xml</c>: the same paraId-ordered
    /// paragraph set, each with identical OuterXml. This is the "structurally faithful" bar (looser
    /// than raw byte-identity — tolerant of e.g. attribute-order/whitespace differences a
    /// re-serializing writer might introduce, while still catching any real content/structure
    /// divergence).
    /// </summary>
    private static bool IsStructurallyFaithful(WordprocessingDocument original, WordprocessingDocument roundTripped)
    {
        var originalParagraphs = original.MainDocumentPart!.Document!.Body!.Descendants<Paragraph>()
            .Select(p => p.OuterXml)
            .ToList();
        var roundTrippedParagraphs = roundTripped.MainDocumentPart!.Document!.Body!.Descendants<Paragraph>()
            .Select(p => p.OuterXml)
            .ToList();

        return originalParagraphs.SequenceEqual(roundTrippedParagraphs, StringComparer.Ordinal);
    }

    /// <summary>
    /// Recursively enumerates EVERY part reachable from the package root (not just the
    /// direct-child parts <see cref="OpenXmlPackage.Parts"/> exposes) — headers/footers/footnotes/
    /// endnotes/numbering/styles/theme/embedded-objects/media/core+app-properties/custom XML, etc.
    /// Deliberately generic (walks the part graph rather than enumerating typed SDK properties like
    /// <c>HeaderParts</c>/<c>FooterParts</c>) so no future part TYPE needs a comparer code change.
    /// </summary>
    private static IEnumerable<OpenXmlPart> EnumerateAllParts(OpenXmlPackage package)
    {
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var queue = new Queue<OpenXmlPart>(package.Parts.Select(p => p.OpenXmlPart));

        while (queue.Count > 0)
        {
            var part = queue.Dequeue();
            if (!visited.Add(part.Uri.OriginalString))
            {
                continue;
            }

            yield return part;

            foreach (var child in part.Parts)
            {
                queue.Enqueue(child.OpenXmlPart);
            }
        }
    }

    private static byte[] ReadPartBytes(OpenXmlPart part)
    {
        using var stream = part.GetStream(FileMode.Open, FileAccess.Read);
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return ms.ToArray();
    }
}
