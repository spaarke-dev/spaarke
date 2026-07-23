using System.Security.Cryptography;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace Sprk.Bff.Api.Services.Compose;

/// <summary>
/// FR-08 (task 010) — the E2 identity substrate. On Load, walks the source <c>.docx</c> body in
/// document order (INCLUDING paragraphs inside table cells and nested tables) and produces an
/// ordered <c>paraId</c> map: every body paragraph is assigned a stable <c>w14:paraId</c> — existing
/// ids are collected verbatim; paragraphs lacking one are minted an OOXML-valid
/// <c>ST_LongHexNumber</c> (<c>0 &lt; x &lt; 0x80000000</c>), collision-checked against every id
/// already present in the part.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this exists</b>: <c>paraId</c> is the splice key the E1 delta save (<see cref="ComposeParagraphRedlineSynthesizer"/>,
/// task 022) and the E2 anchoring (task 012) use to map editor paragraphs back to original OOXML
/// paragraphs. mammoth (the Load-path HTML convert, <c>docxBridge.ts</c>) DISCARDS <c>w14:paraId</c>, so
/// a server-side Open XML pre-parse must run alongside the convert to capture the ids before they are
/// lost (S2: <c>@tiptap/extension-unique-id</c> does NOT auto-assign ids to <c>setContent</c>-loaded
/// nodes, so the server pre-parse owns load-time ids while the client extension owns split/merge minting
/// in task 011). The E1 save synthesizes the redline in place (Option C — design §4.2), so an id minted
/// here survives the round-trip by construction (a rewrite of a paragraph's run content never touches its
/// <c>w:p</c> <c>w14:paraId</c> attribute). This replaced the originally-planned Docxodus
/// <c>WmlComparer</c> path, which the NFR-09 gate (task 003) proved STRIPS <c>w14:paraId</c> on real docs.
/// </para>
/// <para>
/// <b>Pure — <c>byte[]</c>/<c>ReadOnlyMemory&lt;byte&gt;</c> in / ordered record out (no I/O, no AI,
/// no DI graph reach)</b>. The SPE download hop stays behind the <c>SpeFileStore</c>/
/// <c>ISpeFileOperations</c> facade (ADR-007); this class never sees a <c>Microsoft.Graph</c> type,
/// an <c>IOpenAiClient</c>, an executor, or a routing type (ADR-013 / NFR-05 — Tier-1 NetArchTest
/// enforces). Read-only open — the source package is never mutated (the map, not the bytes, carries
/// minted ids; task 020/022 apply them when a splice needs them physically in the OOXML).
/// </para>
/// <para>
/// <b>Single pass (NFR-08)</b>: one Open XML walk collects every existing id into a seen-set, then a
/// second in-memory loop over the SAME paragraph list mints for the gaps — the document is opened and
/// walked once. <b>Thread-safe stateless</b>: no mutable instance state (the injected mint delegate is
/// readonly); safe as a shared singleton (ADR-010).
/// </para>
/// <para>
/// <b>Zero package delta (NFR-01)</b>: <c>DocumentFormat.OpenXml</c> is already a BFF dependency
/// (task 001 bumped it to 3.5.1). This component adds no <c>&lt;PackageReference&gt;</c>. It does NOT
/// touch any Docxodus <c>HtmlToWml</c>/<c>FormattingAssembler</c> path (which would re-pull SkiaSharp,
/// design §13).
/// </para>
/// </remarks>
public sealed class ParaIdPreParser
{
    // The largest permitted w14:paraId is 0x7FFFFFFF (ST_LongHexNumber, 0 < x < 0x80000000).
    private const uint MaxParaId = 0x80000000u;
    private const int MintRetryLimit = 1000;

    private readonly Func<uint> _mint;

    /// <summary>Production constructor — mints ids from a cryptographic RNG.</summary>
    public ParaIdPreParser() : this(DefaultMint) { }

    /// <summary>
    /// Test seam: inject a deterministic id generator so a forced-collision fixture can prove the
    /// mint retries past an already-present value and never assigns a duplicate. Internal — exposed to
    /// the test assembly via <c>InternalsVisibleTo</c>.
    /// </summary>
    internal ParaIdPreParser(Func<uint> mint) => _mint = mint;

    // RandomNumberGenerator.GetInt32(1, int.MaxValue) yields [1, 0x7FFFFFFE] — every value satisfies
    // 0 < x < 0x80000000, so no candidate is ever rejected purely for range in production.
    private static uint DefaultMint() => (uint)RandomNumberGenerator.GetInt32(1, int.MaxValue);

    /// <summary>
    /// Parses <paramref name="docx"/> and returns an ordered map in which EVERY body paragraph
    /// (incl. table-cell + nested-table paragraphs) has a unique <c>w14:paraId</c>. A document with no
    /// body returns an empty (never null) map.
    /// </summary>
    /// <param name="docx">Source <c>.docx</c> bytes (a valid WordprocessingML OPC package).</param>
    /// <exception cref="ArgumentException"><paramref name="docx"/> is empty.</exception>
    /// <exception cref="InvalidOperationException"><paramref name="docx"/> is not a readable DOCX.</exception>
    public ParaIdPreParseResult Parse(ReadOnlyMemory<byte> docx)
    {
        if (docx.IsEmpty)
        {
            throw new ArgumentException("docx is required and must be non-empty.", nameof(docx));
        }

        using var buffer = new MemoryStream(docx.Length);
        buffer.Write(docx.Span);
        buffer.Position = 0;

        WordprocessingDocument doc;
        try
        {
            // Read-only open — the pre-parse never mutates the source package.
            doc = WordprocessingDocument.Open(buffer, isEditable: false);
        }
        catch (Exception ex) when (ex is OpenXmlPackageException or FileFormatException or InvalidDataException or ArgumentOutOfRangeException)
        {
            throw new InvalidOperationException(
                "The supplied bytes are not a readable .docx (WordprocessingML) package.", ex);
        }

        using (doc)
        {
            var body = doc.MainDocumentPart?.Document?.Body;
            if (body is null)
            {
                return new ParaIdPreParseResult(Array.Empty<ParaIdMapEntry>());
            }

            // Descendants<Paragraph>() is RECURSIVE and document-ordered: it already covers paragraphs
            // inside table cells and nested tables (mirrors DocxAnnotationReader's Body.Descendants
            // flattening, EDGE-R4) — no special-case table descent needed.
            var paragraphs = body.Descendants<Paragraph>().ToList();

            // Pass 1: collect EVERY existing id first, so a mint for an id-less paragraph collision-checks
            // against ids that appear both before AND after it in document order.
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in paragraphs)
            {
                var id = p.ParagraphId?.Value;
                if (!string.IsNullOrEmpty(id))
                {
                    seen.Add(id!);
                }
            }

            // Pass 2 (in-memory over the same list): emit the ordered map — existing ids verbatim,
            // minted ids for the gaps.
            var entries = new List<ParaIdMapEntry>(paragraphs.Count);
            for (var i = 0; i < paragraphs.Count; i++)
            {
                var existing = paragraphs[i].ParagraphId?.Value;
                if (!string.IsNullOrEmpty(existing))
                {
                    entries.Add(new ParaIdMapEntry(i, existing!.ToUpperInvariant(), IsMinted: false));
                    continue;
                }

                var minted = MintUnique(seen);
                seen.Add(minted);
                entries.Add(new ParaIdMapEntry(i, minted, IsMinted: true));
            }

            return new ParaIdPreParseResult(entries);
        }
    }

    /// <summary>
    /// Mints an OOXML-valid <c>w14:paraId</c> (<c>0 &lt; x &lt; 0x80000000</c>, 8-hex uppercase) not
    /// already in <paramref name="seen"/>. Reject-and-retry: the 32-bit space vs realistic paragraph
    /// counts makes a collision astronomically unlikely, but the loop guarantees uniqueness and bounds
    /// a pathological injected generator.
    /// </summary>
    private string MintUnique(HashSet<string> seen)
    {
        for (var attempt = 0; attempt < MintRetryLimit; attempt++)
        {
            var candidate = _mint();
            // Enforce the ST_LongHexNumber range even if an (injected) generator returns out-of-range.
            if (candidate == 0 || candidate >= MaxParaId)
            {
                continue;
            }

            var hex = candidate.ToString("X8");
            if (!seen.Contains(hex))
            {
                return hex;
            }
        }

        throw new InvalidOperationException(
            $"Unable to mint a unique w14:paraId after {MintRetryLimit} attempts.");
    }
}

/// <summary>
/// One entry in the ordered paraId map: the paragraph's zero-based document-order
/// <paramref name="Index"/>, its <paramref name="ParaId"/> (<c>w14:paraId</c>, 8-hex uppercase), and
/// whether it was <paramref name="IsMinted"/> at Load (true) or collected verbatim from the source
/// (false). Serializes to <c>{ index, paraId, isMinted }</c> — the shape the client contract mirrors
/// (task 011 consumes it as hidden node attributes).
/// </summary>
public sealed record ParaIdMapEntry(int Index, string ParaId, bool IsMinted);

/// <summary>
/// FR-08 result: the ordered <c>paraId</c> map for a loaded document, one <see cref="ParaIdMapEntry"/>
/// per body paragraph in document order (empty, never null, for a document with no body).
/// </summary>
public sealed record ParaIdPreParseResult(IReadOnlyList<ParaIdMapEntry> Entries);
