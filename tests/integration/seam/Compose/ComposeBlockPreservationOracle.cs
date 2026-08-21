// Task 020 (spaarkeai-compose-r8, spec FR-G01 + FR-G03) — THE PRESERVATION ORACLE.
//
// The question this file answers, and nothing else: after a save that edited exactly ONE block, is
// every OTHER block still the same block? `ComposeFidelityGateHarnessTests` could not ask it — its own
// header says "byte-identity is NOT asserted on the save path" — which is the hole R6's silent fidelity
// loss shipped through, and the reason this is R8 rather than R7.
//
// WHY A SIBLING FILE AND NOT THE PART COMPARER (CLAUDE.md §11 — justify every new component):
//   - Existing:   `ComposeOoxmlPackagePartComparer` answers a BINARY question over whole package parts
//                 ("is word/document.xml byte-identical?"). Different unit, different verdict shape.
//   - Extension:  It cannot be extended into this. Its `IsStructurallyFaithful` walks
//                 `body.Descendants<Paragraph>()` — the exact enumeration this task forbids, because it
//                 interleaves text-box paragraphs into the body sequence and manufactures false loss.
//                 Widening it would also conflate "package integrity" with "per-block preservation",
//                 two independent reasons-to-change. It is left untouched; it still serves the no-op
//                 byte-diff suite.
//   - Cost of not building it: the Phase-3 gate has no oracle, the corpus cannot pick the architecture,
//                 and R9 follows. That is the concrete failure, not "future flexibility".
// This is NOT a second fidelity harness (constraint): one gate [Theory], one corpus, one locator, one
// comparison engine. This is the engine — the same relationship `ComposeCorpusFixtureLocator` already has
// to the harness that drives it.
//
// THE NORMALIZATION CONTRACT (spec FR-G03; the full justification table lives in
// `projects/spaarkeai-compose-r8/notes/gate-contract.md`):
//
//   An oracle silently becomes WRONG through normalization. Too lenient passes a broken merge model and
//   ships R9; too strict fails a correct one and re-opens a settled architecture. Both are invisible — a
//   green suite looks identical either way. Therefore: EVERY normalization below carries a written
//   justification for why the difference it erases is not loss, and the DEFAULT for anything not listed
//   is that a difference IS loss.
//
// Deliberately NOT normalized (each would be a plausible-sounding way to make the numbers look better):
//   - `w:id` on `w:ins`/`w:del` — revision ids. A writer that renumbers them on every save is a finding
//     about the write path (task 042 owns it), not noise to erase. Measured via the DIAGNOSTIC-ONLY
//     third level below so the finding is visible without loosening either gate level.
//   - `w:rPr` / `w:pPr` content of any kind — that is the near tier itself.
//   - Empty paragraphs, `w:br`, `w:tab` — the R3 empty-paragraph-drift defect lives exactly here.
//   - `w:sectPr` — section breaks are one of the losses the owner reports from dev.

using System.Text;
using System.Xml.Linq;
using DocumentFormat.OpenXml.Packaging;

namespace Sprk.Bff.Api.Tests.Seam.Compose;

// Public (not internal) for the same reason `FidelityGateResultSink` and `DocumentFidelityResult` are:
// the xUnit class fixture that carries these reports must be public, and a public signature cannot
// expose an internal type. Scope is still the test assembly.
public static class ComposeBlockPreservationOracle
{
    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private static readonly XNamespace W14 = "http://schemas.microsoft.com/office/word/2010/wordml";

    /// <summary>Elements whose text content is DOCUMENT CONTENT — whitespace inside them is signal, and
    /// the whitespace normalization below must not touch it.</summary>
    private static readonly HashSet<string> TextCarryingElements =
        new(StringComparer.Ordinal) { "t", "delText", "instrText", "delInstrText" };

    /// <summary>The NEAR TIER (spec FR-G01): character formatting, paragraph properties, indentation,
    /// tabs, footnote references, fields. These are the constructs the Phase-3 gate demands 100% on —
    /// the everyday legal-document formatting whose loss is what users actually report. A difference is
    /// classified near-tier when ANY element on its path is in this set.</summary>
    private static readonly HashSet<string> NearTierElements = new(StringComparer.Ordinal)
    {
        "rPr", "pPr",                          // character + paragraph properties (and everything under them)
        "ind",                                  // indentation
        "tabs", "tab",                          // tab stops and tab characters
        "footnoteReference", "footnoteRef",     // footnote refs
        "endnoteReference", "endnoteRef",       // endnote refs — same class of construct
        "fldSimple", "fldChar", "instrText",    // fields (both the simple and the complex three-part form)
    };

    /// <summary>
    /// The two GATE levels plus one DIAGNOSTIC. Lenient and Strict run the SAME comparison engine and
    /// differ in exactly one bit — whether `w14:paraId`/`w14:textId` are normalized away — which is the
    /// property spec FR-G03 requires and the acceptance criterion verifies by reading this file.
    /// </summary>
    public enum ComparisonLevel
    {
        /// <summary>Ignores `w14:paraId`/`w14:textId`. Detects CONTENT LOSS — a block that survived with
        /// a regenerated id still counts as preserved. This is the honest reading of "did we keep the
        /// user's document", because `paraId` is not a durable file key (Word regenerates ids on save;
        /// [MS-DOCX] permits duplicates across `mc:AlternateContent`).</summary>
        Lenient,

        /// <summary>Does NOT ignore `w14:paraId`/`w14:textId`. Detects IDENTITY DRIFT — the anchors the
        /// edit-capture mechanism depends on within a session (project CLAUDE.md invariant 4). Strictly
        /// harder than Lenient: every Lenient difference is also a Strict difference.</summary>
        Strict,

        /// <summary>DIAGNOSTIC ONLY — never a gate level. Strict, plus `w:id` on `w:ins`/`w:del`
        /// normalized. Its only purpose is to answer "how much of the measured loss is nothing but
        /// revision-id renumbering?" without loosening either real level. If this number is large, that
        /// is a finding about the write path to hand to task 042 — per the task's second escalation
        /// trigger, a normalization that cannot be justified as "not loss" is surfaced, not adopted.</summary>
        StrictIgnoringRevisionIds,
    }

    /// <summary>One block that did NOT survive the save unchanged. Each path is the element chain from
    /// the block root down to the shallowest point that differs — it is what makes a failure actionable
    /// ("p/pPr/ind" says indentation; "a block changed" says nothing).</summary>
    public sealed record BlockDifference(
        int Index,
        string BlockElement,
        string? OriginalParaId,
        string? SavedParaId,
        IReadOnlyList<string> DifferingPaths,
        bool IsNearTier);

    /// <summary>Everything the gate, the JSON sink and task 023's control measurement need from one
    /// (original, saved) pair at one comparison level.</summary>
    public sealed record PreservationReport(
        ComparisonLevel Level,
        int OriginalBlockCount,
        int SavedBlockCount,
        int EditedBlockIndex,
        int ComparedBlockCount,
        int PreservedBlockCount,
        int NearTierRelevantCount,
        int NearTierPreservedCount,
        int UnpairedOriginalCount,
        int UnpairedSavedCount,
        // True when `w14:paraId` is NOT unique across the whole document (body blocks AND opaque
        // regions). Reported so a reader knows paraId corroboration is worthless for this document and
        // the pairing rests on document order alone — never used to silently change the pairing.
        bool DuplicateParaIdsInOriginal,
        bool DuplicateParaIdsInSaved,
        int ParaIdCorroborationMismatchCount,
        IReadOnlyList<BlockDifference> Differences)
    {
        /// <summary>
        /// Percent of comparable non-edited blocks that survived byte-for-byte after normalization, or
        /// NULL when there was nothing to compare.
        ///
        /// <para>NULL RATHER THAN 100 IS LOAD-BEARING. An earlier draft of this returned 100 for an empty
        /// denominator, and the first corpus run promptly produced three documents reading "near tier:
        /// 100%" on a denominator of ZERO — a document the oracle had not measured at all, presenting as
        /// perfect. The Phase-3 gate reads these numbers to decide an architecture; "not measured" and
        /// "measured, nothing lost" must never be the same value.</para>
        ///
        /// <para>A document whose block COUNT changed reports its unpaired blocks separately — read the
        /// two together, because a dropped block also mis-pairs everything after it, and that cascade is
        /// a true statement about a damaged document rather than an artifact.</para>
        /// </summary>
        public double? OverallPreservationPercent =>
            ComparedBlockCount == 0 ? null : 100d * PreservedBlockCount / ComparedBlockCount;

        /// <summary>
        /// Percent of blocks where the NEAR TIER was in play and survived intact, or NULL when the near
        /// tier was not in play anywhere in this document (see the null rationale above).
        ///
        /// <para>"In play" means the original block carried a near-tier construct, OR the save introduced
        /// a near-tier difference. The second half matters: on the current corpus the renderer ADDS
        /// `w:pPr` to paragraphs that had none, and keying relevance off the original alone would let
        /// invented formatting escape the tier entirely.</para>
        ///
        /// <para>A block that differs only for a non-near-tier reason (say a dropped drawing) does not
        /// count against this number — the difference is classified by WHAT changed, not by which block
        /// changed.</para>
        /// </summary>
        public double? NearTierPreservationPercent =>
            NearTierRelevantCount == 0 ? null : 100d * NearTierPreservedCount / NearTierRelevantCount;

        public bool BlockCountDrifted => OriginalBlockCount != SavedBlockCount;
    }

    // ===============================================================================================
    // Entry point
    // ===============================================================================================

    /// <summary>
    /// Compares the direct `w:body` children of <paramref name="original"/> and <paramref name="saved"/>,
    /// excluding the one block the harness deliberately edited (located by
    /// <paramref name="editMarker"/> in the SAVED document).
    /// </summary>
    public static PreservationReport Compare(
        byte[] original, byte[] saved, string editMarker, ComparisonLevel level)
    {
        var originalBlocks = ReadBodyBlocks(original);
        var savedBlocks = ReadBodyBlocks(saved);

        // `numId`/`abstractNumId` maps are DOCUMENT-scoped, built once over the whole body in document
        // order, because the whole point is that the two documents may use different raw numbers for the
        // same list. Per-block maps would defeat that.
        var originalNumbering = BuildNumberingOrdinalMap(originalBlocks);
        var savedNumbering = BuildNumberingOrdinalMap(savedBlocks);

        var editedIndex = FindEditedBlockIndex(savedBlocks, editMarker);

        var originalParaIds = originalBlocks.Select(ReadParaId).ToList();
        var savedParaIds = savedBlocks.Select(ReadParaId).ToList();

        // Scanned over the FULL subtree, not just the paired body-level blocks. The canonical duplicate
        // case is the same paraId appearing inside `mc:Choice` AND `mc:Fallback` — how Word writes every
        // text box, and precisely what task 021 authors as an R4-breaker. A body-level-only scan would
        // report `false` for it and the flag would be silent on the one case it exists for.
        var originalHasDuplicates = HasDuplicateParaIds(AllParaIds(originalBlocks));
        var savedHasDuplicates = HasDuplicateParaIds(AllParaIds(savedBlocks));

        var pairCount = Math.Min(originalBlocks.Count, savedBlocks.Count);
        var differences = new List<BlockDifference>();
        var compared = 0;
        var preserved = 0;
        var nearTierRelevant = 0;
        var nearTierPreserved = 0;
        var paraIdMismatches = 0;

        for (var i = 0; i < pairCount; i++)
        {
            if (i == editedIndex)
            {
                continue; // the one block we deliberately changed — "every OTHER block" is the requirement
            }

            compared++;

            // paraId is a CORROBORATING HINT only — it never drives pairing (it is not a durable file
            // key: duplicates are spec-legal in `mc:AlternateContent` and Word regenerates ids on save).
            // A mismatch here is reported as its own number so identity drift stays visible even at the
            // lenient level, where the id itself is normalized out of the comparison.
            if (originalParaIds[i] is { } op && savedParaIds[i] is { } sp
                && !string.Equals(op, sp, StringComparison.OrdinalIgnoreCase))
            {
                paraIdMismatches++;
            }

            // Near-tier relevance is decided from the ORIGINAL block first, then widened below if the
            // save INTRODUCED a near-tier difference — a paragraph that had no `w:pPr` and came back
            // with one has had the near tier acted on, and keying relevance off the original alone
            // would let invented formatting escape the tier.
            var carriedNearTier = ContainsNearTierConstruct(originalBlocks[i]);

            var a = Normalize(originalBlocks[i], level, originalNumbering);
            var b = Normalize(savedBlocks[i], level, savedNumbering);

            if (string.Equals(Canonicalize(a), Canonicalize(b), StringComparison.Ordinal))
            {
                preserved++;
                if (carriedNearTier)
                {
                    nearTierRelevant++;
                    nearTierPreserved++;
                }

                continue;
            }

            var paths = new List<string>();
            CollectDifferencePaths(a, b, new List<string>(), paths);
            if (paths.Count == 0)
            {
                // Canonical strings differ but the structural walk found no site — report the whole block
                // rather than silently counting it as preserved.
                paths.Add(a.Name.LocalName);
            }

            var isNearTierDifference = paths.Any(IsNearTierPath);
            if (carriedNearTier || isNearTierDifference)
            {
                nearTierRelevant++;
                if (!isNearTierDifference)
                {
                    nearTierPreserved++; // it differed, but not in the near tier
                }
            }

            differences.Add(new BlockDifference(
                Index: i,
                BlockElement: originalBlocks[i].Name.LocalName,
                OriginalParaId: originalParaIds[i],
                SavedParaId: savedParaIds[i],
                DifferingPaths: paths.Distinct(StringComparer.Ordinal).OrderBy(p => p, StringComparer.Ordinal).ToList(),
                IsNearTier: isNearTierDifference));
        }

        return new PreservationReport(
            Level: level,
            OriginalBlockCount: originalBlocks.Count,
            SavedBlockCount: savedBlocks.Count,
            EditedBlockIndex: editedIndex,
            ComparedBlockCount: compared,
            PreservedBlockCount: preserved,
            NearTierRelevantCount: nearTierRelevant,
            NearTierPreservedCount: nearTierPreserved,
            UnpairedOriginalCount: Math.Max(0, originalBlocks.Count - savedBlocks.Count),
            UnpairedSavedCount: Math.Max(0, savedBlocks.Count - originalBlocks.Count),
            DuplicateParaIdsInOriginal: originalHasDuplicates,
            DuplicateParaIdsInSaved: savedHasDuplicates,
            ParaIdCorroborationMismatchCount: paraIdMismatches,
            Differences: differences);
    }

    // ===============================================================================================
    // Block enumeration — DIRECT `w:body` children only
    // ===============================================================================================

    /// <summary>
    /// The direct children of `w:body`, in document order. NEVER `Descendants&lt;Paragraph&gt;()`:
    /// descendant enumeration interleaves `w:txbxContent` paragraphs (how Word writes every text box,
    /// via `mc:AlternateContent`) into the body sequence, which mis-pairs every block after the first
    /// text box and manufactures loss that is not there. `mc:AlternateContent`, `w:txbxContent`,
    /// `mc:Choice` and `mc:Fallback` are therefore OPAQUE — compared whole, never descended into for
    /// pairing purposes.
    /// </summary>
    private static List<XElement> ReadBodyBlocks(byte[] docx)
    {
        using var stream = new MemoryStream(docx, writable: false);
        using var package = WordprocessingDocument.Open(stream, isEditable: false);
        using var partStream = package.MainDocumentPart!.GetStream(FileMode.Open, FileAccess.Read);

        var xml = XDocument.Load(partStream, LoadOptions.None);
        var body = xml.Root?.Element(W + "body");
        return body is null ? new List<XElement>() : body.Elements().ToList();
    }

    /// <summary>Locates the block carrying the harness's edit marker — the one block excluded from the
    /// preservation denominator. Returns -1 when the marker is absent, in which case every block is
    /// compared (and the harness's own edit-presence assertion is what reports the missing edit).</summary>
    private static int FindEditedBlockIndex(List<XElement> savedBlocks, string editMarker)
    {
        if (string.IsNullOrEmpty(editMarker))
        {
            return -1;
        }

        for (var i = 0; i < savedBlocks.Count; i++)
        {
            var text = string.Concat(savedBlocks[i]
                .DescendantsAndSelf()
                .Where(e => e.Name.Namespace == W && TextCarryingElements.Contains(e.Name.LocalName))
                .Select(e => e.Value));
            if (text.Contains(editMarker, StringComparison.Ordinal))
            {
                return i;
            }
        }

        return -1;
    }

    private static string? ReadParaId(XElement block) => block.Attribute(W14 + "paraId")?.Value;

    /// <summary>Every `w14:paraId` anywhere under the given blocks, INCLUDING inside opaque regions
    /// (`mc:AlternateContent`, `w:txbxContent`). Opaque means "not descended into for PAIRING" — it does
    /// not mean invisible for reporting, and duplicate detection needs the full picture.</summary>
    private static IEnumerable<string?> AllParaIds(IEnumerable<XElement> blocks) =>
        blocks.SelectMany(b => b.DescendantsAndSelf())
            .Select(e => e.Attribute(W14 + "paraId")?.Value);

    private static bool HasDuplicateParaIds(IEnumerable<string?> paraIds)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        return paraIds.Where(id => !string.IsNullOrEmpty(id)).Any(id => !seen.Add(id!));
    }

    // ===============================================================================================
    // Normalization — every entry justified; the default for anything NOT here is "a difference is loss"
    // ===============================================================================================

    private static XElement Normalize(
        XElement source, ComparisonLevel level, IReadOnlyDictionary<string, int> numberingOrdinals)
    {
        var clone = new XElement(source);

        // (1) `w:rsid*` — REVISION SAVE IDs. Word stamps these to track which editing session produced
        //     which run; it regenerates them on essentially every save and they are explicitly optional.
        //     They carry no document content: two files differing only in rsids render identically and
        //     print identically. Not loss.
        // (5) ATTRIBUTE ORDER is handled at serialization time (Canonicalize sorts) — XML attribute order
        //     is not information; the writer chooses it.
        // (6) NAMESPACE PREFIXES likewise: Canonicalize emits `{namespace-uri}local`, so a `w:` vs `w1:`
        //     binding difference cannot register as loss. The URI is the semantic part.
        foreach (var attribute in clone.DescendantsAndSelf().Attributes().ToList())
        {
            if (attribute.Name.Namespace == W && attribute.Name.LocalName.StartsWith("rsid", StringComparison.Ordinal))
            {
                attribute.Remove();
            }
        }

        // (2) `w:proofErr` — spell/grammar proofing MARKERS. Transient editor state that Word rebuilds on
        //     open; they bracket text without changing it. Not loss.
        foreach (var proofErr in clone.DescendantsAndSelf(W + "proofErr").ToList())
        {
            proofErr.Remove();
        }

        // (3) BOOKMARK IDs — `w:bookmarkStart/@w:id` + `w:bookmarkEnd/@w:id` are arbitrary LOCAL handles
        //     that pair a start with its end; only `@w:name` is semantic and it is deliberately KEPT.
        //     A writer that renumbers 0,1,2 to 7,8,9 has lost nothing. Dropping the NAME would be loss —
        //     and that still registers, because the name is not normalized.
        foreach (var bookmark in clone.DescendantsAndSelf()
                     .Where(e => e.Name == W + "bookmarkStart" || e.Name == W + "bookmarkEnd")
                     .ToList())
        {
            bookmark.Attribute(W + "id")?.Remove();
        }

        // (4) `numId` / `abstractNumId` — legitimately REMAPPED when numbering definitions merge, so raw
        //     inequality is not loss. Crucially this does NOT delete them: each distinct value is
        //     rewritten to its first-appearance ORDINAL in the document. Deleting would erase the ability
        //     to see a list association dropped entirely; the ordinal keeps "these blocks are in the same
        //     list, in this order" as signal while tolerating renumbering.
        foreach (var numbered in clone.DescendantsAndSelf()
                     .Where(e => e.Name == W + "numId" || e.Name == W + "abstractNumId")
                     .ToList())
        {
            var val = numbered.Attribute(W + "val");
            if (val is not null
                && numberingOrdinals.TryGetValue(NumberingKey(numbered.Name.LocalName, val.Value), out var ordinal))
            {
                val.Value = $"#{ordinal}";
            }
        }

        // (7) INSIGNIFICANT WHITESPACE between elements — pure serialization/indentation artifact of
        //     whichever writer produced the part. Whitespace INSIDE `w:t`/`w:delText`/`w:instrText` is
        //     document content and is deliberately untouched (leading/trailing spaces in a run are real,
        //     which is exactly why OOXML has `xml:space="preserve"`).
        foreach (var text in clone.DescendantsAndSelf().Nodes().OfType<XText>().ToList())
        {
            var parent = text.Parent;
            if (parent is null)
            {
                continue;
            }

            var parentIsTextCarrying = parent.Name.Namespace == W && TextCarryingElements.Contains(parent.Name.LocalName);
            if (!parentIsTextCarrying && string.IsNullOrWhiteSpace(text.Value))
            {
                text.Remove();
            }
        }

        // LEVEL SWITCH — the ONLY difference between the two gate levels. Lenient erases the session
        // anchors so content loss shows through unmasked; Strict keeps them so identity drift shows.
        if (level == ComparisonLevel.Lenient)
        {
            foreach (var attribute in clone.DescendantsAndSelf().Attributes()
                         .Where(a => a.Name == W14 + "paraId" || a.Name == W14 + "textId")
                         .ToList())
            {
                attribute.Remove();
            }
        }

        // DIAGNOSTIC LEVEL ONLY — never a gate level. See the ComparisonLevel doc comment.
        if (level == ComparisonLevel.StrictIgnoringRevisionIds)
        {
            foreach (var revision in clone.DescendantsAndSelf()
                         .Where(e => e.Name == W + "ins" || e.Name == W + "del")
                         .ToList())
            {
                revision.Attribute(W + "id")?.Remove();
            }
        }

        return clone;
    }

    private static string NumberingKey(string elementLocalName, string value) => $"{elementLocalName}:{value}";

    /// <summary>Maps every distinct `numId`/`abstractNumId` value to the ordinal of its FIRST appearance
    /// in document order — the canonical form that makes legitimate remapping invisible while keeping
    /// list grouping and ordering as signal.</summary>
    private static Dictionary<string, int> BuildNumberingOrdinalMap(IEnumerable<XElement> blocks)
    {
        var map = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var block in blocks)
        {
            foreach (var element in block.DescendantsAndSelf()
                         .Where(e => e.Name == W + "numId" || e.Name == W + "abstractNumId"))
            {
                var val = element.Attribute(W + "val")?.Value;
                if (val is null)
                {
                    continue;
                }

                var key = NumberingKey(element.Name.LocalName, val);
                if (!map.ContainsKey(key))
                {
                    map[key] = map.Count;
                }
            }
        }

        return map;
    }

    // ===============================================================================================
    // Canonical serialization + structural difference walk
    // ===============================================================================================

    /// <summary>
    /// Prefix-free, attribute-order-free canonical text for an element subtree. Written by hand rather
    /// than via <c>XElement.ToString()</c> precisely because <c>ToString()</c> preserves whichever
    /// prefixes the source declared — comparing its output would let a prefix rebinding register as
    /// document loss.
    /// </summary>
    private static string Canonicalize(XElement element)
    {
        var builder = new StringBuilder();
        Write(element, builder);
        return builder.ToString();

        static void Write(XElement el, StringBuilder sb)
        {
            sb.Append('<').Append(el.Name.NamespaceName).Append('}').Append(el.Name.LocalName);
            foreach (var attribute in el.Attributes()
                         .Where(a => !a.IsNamespaceDeclaration)
                         .OrderBy(a => a.Name.NamespaceName, StringComparer.Ordinal)
                         .ThenBy(a => a.Name.LocalName, StringComparer.Ordinal))
            {
                sb.Append(' ').Append(attribute.Name.NamespaceName).Append('}').Append(attribute.Name.LocalName)
                  .Append("=\"").Append(attribute.Value).Append('"');
            }

            sb.Append('>');
            foreach (var node in el.Nodes())
            {
                switch (node)
                {
                    case XElement child:
                        Write(child, sb);
                        break;
                    case XText text:
                        sb.Append(text.Value);
                        break;
                }
            }

            sb.Append("</").Append(el.Name.LocalName).Append('>');
        }
    }

    /// <summary>
    /// Walks two NORMALIZED subtrees and records the shallowest points at which they diverge, as
    /// `/`-joined element paths. The path is what makes a gate failure actionable — "p/pPr/ind" names
    /// indentation; "a block changed" names nothing.
    /// </summary>
    private static void CollectDifferencePaths(XElement a, XElement b, List<string> path, List<string> sink)
    {
        if (a.Name != b.Name)
        {
            sink.Add(JoinPath(path, $"{a.Name.LocalName}|{b.Name.LocalName}"));
            return;
        }

        path.Add(a.Name.LocalName);
        try
        {
            if (!AttributesEqual(a, b) || !DirectTextEqual(a, b))
            {
                sink.Add(JoinPath(path, null));
            }

            var aChildren = a.Elements().ToList();
            var bChildren = b.Elements().ToList();

            if (aChildren.Count != bChildren.Count)
            {
                // Child-set divergence: name the children that appear on only one side, so a DROPPED
                // construct is reported by name rather than as an anonymous count mismatch.
                var aNames = aChildren.Select(c => c.Name.LocalName).ToList();
                var bNames = bChildren.Select(c => c.Name.LocalName).ToList();
                var asymmetric = aNames.Except(bNames, StringComparer.Ordinal)
                    .Union(bNames.Except(aNames, StringComparer.Ordinal), StringComparer.Ordinal)
                    .ToList();
                foreach (var name in asymmetric)
                {
                    sink.Add(JoinPath(path, name));
                }

                if (asymmetric.Count == 0)
                {
                    // Same element NAMES on both sides but a different COUNT — a repeated construct was
                    // duplicated or dropped. Name the parent so it is still reported.
                    sink.Add(JoinPath(path, null));
                }
            }

            for (var i = 0; i < Math.Min(aChildren.Count, bChildren.Count); i++)
            {
                if (!string.Equals(Canonicalize(aChildren[i]), Canonicalize(bChildren[i]), StringComparison.Ordinal))
                {
                    CollectDifferencePaths(aChildren[i], bChildren[i], path, sink);
                }
            }
        }
        finally
        {
            path.RemoveAt(path.Count - 1);
        }
    }

    private static bool AttributesEqual(XElement a, XElement b)
    {
        static string Key(XElement e) => string.Join('|', e.Attributes()
            .Where(x => !x.IsNamespaceDeclaration)
            .OrderBy(x => x.Name.NamespaceName, StringComparer.Ordinal)
            .ThenBy(x => x.Name.LocalName, StringComparer.Ordinal)
            .Select(x => $"{x.Name.NamespaceName}}}{x.Name.LocalName}={x.Value}"));

        return string.Equals(Key(a), Key(b), StringComparison.Ordinal);
    }

    private static bool DirectTextEqual(XElement a, XElement b)
    {
        static string Direct(XElement e) => string.Concat(e.Nodes().OfType<XText>().Select(t => t.Value));
        return string.Equals(Direct(a), Direct(b), StringComparison.Ordinal);
    }

    private static string JoinPath(List<string> path, string? leaf) =>
        leaf is null ? string.Join('/', path) : string.Join('/', path.Append(leaf));

    /// <summary>A path is near-tier when ANY element on it is in the near-tier set. A segment of the form
    /// `a|b` records "the original had `a` here, the save had `b`" — BOTH sides must be inspected: the
    /// renderer replacing a `w:r` with a `w:pPr` (42 occurrences on the current corpus, path `p/r|pPr`)
    /// is a near-tier difference, and reading only the left side would classify it as anything but.</summary>
    private static bool IsNearTierPath(string path) =>
        path.Split('/').SelectMany(segment => segment.Split('|')).Any(NearTierElements.Contains);

    private static bool ContainsNearTierConstruct(XElement block) =>
        block.DescendantsAndSelf().Any(e => e.Name.Namespace == W && NearTierElements.Contains(e.Name.LocalName));
}
