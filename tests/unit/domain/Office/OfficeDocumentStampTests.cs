using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using FluentAssertions;
using Sprk.Bff.Api.Services.Office;
using Sprk.Bff.Api.Tests.Shared.Office;
using Xunit;

namespace Sprk.Bff.Api.Tests.Domain.Office;

/// <summary>
/// FR-02 (spaarkeai-word-add-in-r1 task 014): the server-side custom XML part stamp. Pure domain logic —
/// bytes in, bytes out, no mocks, no DI, no I/O (ADR-038 KEEP path <c>tests/unit/domain/**</c>).
/// </summary>
/// <remarks>
/// <para><b>Scope of coverage.</b> Byte fidelity of the stamped package (the escalation trigger this task
/// carried); the stamp's readability and its wire shape for the client reader (task 051); idempotence and
/// re-stamp semantics; byte-determinism, which the save spine's task-047 duplicate check depends on; and the
/// four classification outcomes — Word package, not-a-zip, readable non-Word zip, and corrupt — including the
/// negative cases where bytes MUST pass through untouched rather than fail.</para>
/// <para><b>Why byte fidelity is asserted part-by-part</b> rather than by "the document still opens": ADR-049
/// was amended three times because two prior approaches to <c>.docx</c> mutation each lost fidelity while
/// still producing openable files. "It opens" is not evidence. The assertion here is the strong one — every
/// pre-existing part's decompressed content is byte-identical, and the two parts that must change are the
/// original with one element inserted, proven by common-prefix + common-suffix reconstruction.</para>
/// </remarks>
public class OfficeDocumentStampTests
{
    private static readonly Guid DocumentId = Guid.Parse("3f2504e0-4f89-11d3-9a0c-0305e82c3301");
    private static readonly Guid OtherDocumentId = Guid.Parse("a1b2c3d4-0000-4000-8000-000000000001");

    // ── byte fidelity ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Stamp_OnAWordPackage_LeavesEveryPreExistingPartByteIdentical_ExceptTheTwoRegistrationParts()
    {
        var original = MinimalDocx.Create("Engagement letter");

        var stamped = OfficeDocumentStamp.Stamp(original, DocumentId);

        stamped.Outcome.Should().Be(OfficeDocumentStamp.StampOutcome.Stamped);

        var before = PartsOf(original);
        var after = PartsOf(stamped.Bytes);

        var registrationParts = new[] { "[Content_Types].xml", "word/_rels/document.xml.rels" };
        foreach (var (path, content) in before)
        {
            after.Should().ContainKey(path);
            if (registrationParts.Contains(path))
            {
                continue;
            }

            after[path].Should().Equal(content,
                $"'{path}' was not opened by the stamper, so its bytes must survive verbatim");
        }
    }

    [Fact]
    public void Stamp_OnAWordPackage_ChangesTheTwoRegistrationPartsByInsertionOnly()
    {
        var original = MinimalDocx.Create("Engagement letter");

        var stamped = OfficeDocumentStamp.Stamp(original, DocumentId).Bytes;

        var before = PartsOf(original);
        var after = PartsOf(stamped);

        foreach (var path in new[] { "[Content_Types].xml", "word/_rels/document.xml.rels" })
        {
            var originalPart = before[path];
            var revisedPart = after[path];

            revisedPart.Length.Should().BeGreaterThan(originalPart.Length, "an insertion only grows the part");

            // Common prefix + common suffix must reconstruct the WHOLE original: that is what "insertion only"
            // means, and it is stronger than comparing parsed XML — no byte of the original moved or changed.
            var prefix = CommonPrefixLength(originalPart, revisedPart);
            var suffix = CommonSuffixLength(originalPart, revisedPart);
            (prefix + suffix).Should().BeGreaterThanOrEqualTo(originalPart.Length,
                $"'{path}' must be the original with one element spliced in, not a re-serialization");
        }
    }

    [Fact]
    public void Stamp_OnAWordPackage_PreservesEntryOrder_AndAddsOnlyTheThreeStampParts()
    {
        var original = MinimalDocx.Create("Engagement letter");

        var stamped = OfficeDocumentStamp.Stamp(original, DocumentId).Bytes;

        var before = EntryNamesOf(original);
        var after = EntryNamesOf(stamped);

        after.Take(before.Count).Should().Equal(before, "existing entries keep their original order");
        after.Skip(before.Count).Should().BeEquivalentTo(new[]
        {
            "customXml/item1.xml",
            "customXml/itemProps1.xml",
            "customXml/_rels/item1.xml.rels",
        });
    }

    [Fact]
    public void Stamp_OnAWordPackage_LeavesTheBodyTextUnchanged()
    {
        var original = MinimalDocx.Create("Acme v. Beta — settlement terms");

        var stamped = OfficeDocumentStamp.Stamp(original, DocumentId).Bytes;

        MinimalDocx.ReadBodyText(stamped).Should().Be("Acme v. Beta — settlement terms");
    }

    // ── the stamp itself, and the contract task 051 reads ─────────────────────────────────────────

    [Fact]
    public void TryReadStamp_AfterStamping_ReturnsTheCanonicalBareLowercaseId()
    {
        var stamped = OfficeDocumentStamp.Stamp(MinimalDocx.Create("Brief"), DocumentId).Bytes;

        OfficeDocumentStamp.TryReadStamp(stamped).Should().Be(DocumentId);

        // ADR-044: the stored text is bare and lowercase, so the reader needs no normalisation.
        StampPartText(stamped).Should().Contain(DocumentId.ToString("D"))
            .And.NotContain("{").And.NotContain(DocumentId.ToString("D").ToUpperInvariant());
    }

    [Fact]
    public void Stamp_WritesAnExplicitDefaultNamespaceOnTheRoot_SoTheClientCanFindItByNamespace()
    {
        // Task 019 condition 2: CustomXMLPart.namespaceUri is populated ONLY when the top-level element
        // carries the xmlns attribute, and the client looks the part up BY namespace. Omitting it would be a
        // silent total failure — the client would never find the part the server just wrote.
        var stamped = OfficeDocumentStamp.Stamp(MinimalDocx.Create("Brief"), DocumentId).Bytes;

        var root = XDocument.Parse(StampPartText(stamped)!).Root!;

        root.Name.NamespaceName.Should().Be(OfficeDocumentStamp.StampNamespace);
        root.Name.LocalName.Should().Be("documentIdentity");
        root.Attribute("xmlns").Should().NotBeNull("the namespace must be declared as an explicit default xmlns");
    }

    [Fact]
    public void Stamp_RelatesTheStampFromTheMainDocumentPart_AndGivesItAnItemPropsSibling()
    {
        // The data-store shape the Office.js Common API enumerates: related from word/document.xml with an
        // itemProps part whose schemaRef names the stamp namespace. A part related only from the package root
        // may never reach the client's data store.
        var stamped = OfficeDocumentStamp.Stamp(MinimalDocx.Create("Brief"), DocumentId).Bytes;
        var parts = PartsOf(stamped);

        var mainRels = XDocument.Parse(Text(parts["word/_rels/document.xml.rels"]));
        var relationship = mainRels.Root!.Elements()
            .Should().ContainSingle(element =>
                (string?)element.Attribute("Type")
                == "http://schemas.openxmlformats.org/officeDocument/2006/relationships/customXml").Subject;
        ((string?)relationship.Attribute("Target")).Should().Be("../customXml/item1.xml",
            "a relative target, matching the shape that survived four Word save cycles");

        var props = XDocument.Parse(Text(parts["customXml/itemProps1.xml"]));
        props.Root!.Name.LocalName.Should().Be("datastoreItem");
        props.Descendants().Should().Contain(element =>
            element.Name.LocalName == "schemaRef"
            && element.Attributes().Any(a => a.Value == OfficeDocumentStamp.StampNamespace));

        Text(parts["[Content_Types].xml"]).Should().Contain("/customXml/itemProps1.xml",
            "the properties part needs its own content-type Override");
    }

    // ── idempotence, re-stamp, determinism ───────────────────────────────────────────────────────

    [Fact]
    public void Stamp_WhenAlreadyCarryingTheSameId_ReturnsTheInputBytesUnchanged()
    {
        var once = OfficeDocumentStamp.Stamp(MinimalDocx.Create("Brief"), DocumentId).Bytes;

        var twice = OfficeDocumentStamp.Stamp(once, DocumentId);

        twice.Outcome.Should().Be(OfficeDocumentStamp.StampOutcome.AlreadyStamped);
        twice.Bytes.Should().BeSameAs(once, "a re-stamp with the same id is a true no-op, not a rewrite");
    }

    [Fact]
    public void Stamp_WhenCarryingADifferentId_RewritesIt_AndLeavesExactlyOneStampPart()
    {
        // A copy of another Spaarke document, or the "save as new document" override of an identified one.
        var sourceStamped = OfficeDocumentStamp.Stamp(MinimalDocx.Create("Brief"), OtherDocumentId).Bytes;

        var restamped = OfficeDocumentStamp.Stamp(sourceStamped, DocumentId);

        restamped.Outcome.Should().Be(OfficeDocumentStamp.StampOutcome.Stamped);
        OfficeDocumentStamp.TryReadStamp(restamped.Bytes).Should().Be(DocumentId);
        EntryNamesOf(restamped.Bytes).Should().BeEquivalentTo(EntryNamesOf(sourceStamped),
            "the existing part is rewritten in place — no second stamp part accumulates");
    }

    [Fact]
    public void Stamp_CalledTwiceOnTheSameInput_ProducesByteIdenticalOutput()
    {
        // Load-bearing, not cosmetic: OfficeService's task-047 duplicate check stamps the incoming request and
        // compares it against the stored bytes. If stamping were not reproducible, every identical version-save
        // retry would write a redundant SPE version.
        var original = MinimalDocx.Create("Brief");

        var first = OfficeDocumentStamp.Stamp(original, DocumentId).Bytes;
        var second = OfficeDocumentStamp.Stamp(original, DocumentId).Bytes;

        second.Should().Equal(first);
    }

    [Fact]
    public void Stamp_OnAPackageWhoseMainPartHasNoRelationshipPart_CreatesIt()
    {
        var original = MinimalDocx.Create("Brief", withDocumentRels: false);
        EntryNamesOf(original).Should().NotContain("word/_rels/document.xml.rels", "precondition");

        var stamped = OfficeDocumentStamp.Stamp(original, DocumentId);

        stamped.Outcome.Should().Be(OfficeDocumentStamp.StampOutcome.Stamped);
        OfficeDocumentStamp.TryReadStamp(stamped.Bytes).Should().Be(DocumentId);
        EntryNamesOf(stamped.Bytes).Should().Contain("word/_rels/document.xml.rels");
    }

    [Fact]
    public void Stamp_WhenItem1IsOccupiedByAnotherCustomXmlPart_UsesTheLowestFreeItemNumber()
    {
        // Real documents carry foreign custom XML (a bibliography, Google Docs metadata). The stamp must not
        // displace them.
        var withForeignPart = WithExtraPart(
            MinimalDocx.Create("Brief"),
            "customXml/item1.xml",
            """<?xml version="1.0"?><b:Sources xmlns:b="http://example.test/bibliography"/>""");

        var stamped = OfficeDocumentStamp.Stamp(withForeignPart, DocumentId);

        EntryNamesOf(stamped.Bytes).Should().Contain("customXml/item2.xml");
        OfficeDocumentStamp.TryReadStamp(stamped.Bytes).Should().Be(DocumentId);
        PartsOf(stamped.Bytes)["customXml/item1.xml"]
            .Should().Equal(PartsOf(withForeignPart)["customXml/item1.xml"], "the foreign part is untouched");
    }

    [Fact]
    public void TryReadStamp_OnAnUnstampedWordPackage_ReturnsNull()
    {
        OfficeDocumentStamp.TryReadStamp(MinimalDocx.Create("Brief")).Should().BeNull();
    }

    [Fact]
    public void TryReadStamp_OnAForeignCustomXmlPart_ReturnsNull()
    {
        var withForeignPart = WithExtraPart(
            MinimalDocx.Create("Brief"),
            "customXml/item1.xml",
            """<?xml version="1.0"?><b:Sources xmlns:b="http://example.test/bibliography"/>""");

        OfficeDocumentStamp.TryReadStamp(withForeignPart).Should().BeNull(
            "identification is an exact namespace match, so other custom XML is ignored");
    }

    // ── classification: what is stamped, what passes through, what is refused ────────────────────

    [Fact]
    public void Classify_OnAWordPackage_ReportsWordProcessing()
    {
        OfficeDocumentStamp.Classify(MinimalDocx.Create("Brief"))
            .Should().Be(OfficeDocumentStamp.PackageKind.WordProcessing);
    }

    [Theory]
    [InlineData("%PDF-1.7\nnot really a pdf")]
    [InlineData("From: a@b.test\r\nSubject: filed\r\n\r\nbody")]
    [InlineData("just some bytes")]
    public void Stamp_OnANonOoxmlPayload_PassesTheBytesThroughUntouched(string payload)
    {
        var bytes = Encoding.UTF8.GetBytes(payload);

        var result = OfficeDocumentStamp.Stamp(bytes, DocumentId);

        result.Outcome.Should().Be(OfficeDocumentStamp.StampOutcome.PassedThrough);
        result.Bytes.Should().BeSameAs(bytes, "a PDF, an EML or an arbitrary binary is stored exactly as sent");
        OfficeDocumentStamp.Classify(bytes).Should().Be(OfficeDocumentStamp.PackageKind.NotAZip);
    }

    [Fact]
    public void Stamp_OnAReadableZipThatIsNotWordprocessingMl_PassesTheBytesThroughUntouched()
    {
        // An .xlsx sent as a Document, or a plain .zip: readable, but nothing to stamp. Detection is by
        // CONTENT (package relationships + declared content type), never by file name.
        using var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = zip.CreateEntry("readme.txt");
            using var stream = entry.Open();
            stream.Write("not an office package"u8);
        }

        var bytes = buffer.ToArray();

        OfficeDocumentStamp.Classify(bytes).Should().Be(OfficeDocumentStamp.PackageKind.NotWordProcessing);
        var result = OfficeDocumentStamp.Stamp(bytes, DocumentId);
        result.Outcome.Should().Be(OfficeDocumentStamp.StampOutcome.PassedThrough);
        result.Bytes.Should().BeSameAs(bytes);
    }

    [Fact]
    public void Stamp_OnAZipSignatureThatIsNotAReadableArchive_ReportsCorrupt_AndReturnsTheBytesUnwritten()
    {
        // The shape every Office save fixture used to be: a zip signature and nothing behind it.
        var bytes = new byte[] { 0x50, 0x4B, 0x03, 0x04, 0x11 };

        OfficeDocumentStamp.Classify(bytes).Should().Be(OfficeDocumentStamp.PackageKind.Corrupt);

        var result = OfficeDocumentStamp.Stamp(bytes, DocumentId);
        result.Outcome.Should().Be(OfficeDocumentStamp.StampOutcome.Corrupt);
        result.Bytes.Should().BeSameAs(bytes, "a refusal never produces rewritten bytes");
        result.Reason.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void Stamp_OnATruncatedWordPackage_ReportsCorrupt()
    {
        var truncated = MinimalDocx.Create("Brief")[..40];

        OfficeDocumentStamp.Stamp(truncated, DocumentId).Outcome
            .Should().Be(OfficeDocumentStamp.StampOutcome.Corrupt);
    }

    [Fact]
    public void TryReadStamp_OnCorruptOrEmptyBytes_ReturnsNull()
    {
        OfficeDocumentStamp.TryReadStamp(new byte[] { 0x50, 0x4B, 0x03, 0x04, 0x11 }).Should().BeNull();
        OfficeDocumentStamp.TryReadStamp(Array.Empty<byte>()).Should().BeNull();
        OfficeDocumentStamp.TryReadStamp(null).Should().BeNull();
    }

    [Fact]
    public void Stamp_WhenAPackagePartExceedsTheSizeBound_PassesTheBytesThroughUntouched_AndDoesNotThrow()
    {
        // ZIP-BOMB GUARD. These bytes are an untrusted upload, and the stamper is the first thing in the save
        // path that decompresses them. A zip expands at roughly 1000:1, so an upload well inside the request
        // limit can declare a part of many gigabytes — here ~9 MB of repeating text, which compresses to a few
        // KB. The required behaviour is to degrade to "unstamped" (always a recoverable state) rather than to
        // allocate without bound; failing the user's save would be the worse outcome.
        var oversize = WithReplacedPart(
            MinimalDocx.Create("Brief"), "[Content_Types].xml", new string('x', 9 * 1024 * 1024));

        var result = OfficeDocumentStamp.Stamp(oversize, DocumentId);

        result.Outcome.Should().BeOneOf(
            OfficeDocumentStamp.StampOutcome.PassedThrough,
            OfficeDocumentStamp.StampOutcome.Unsupported);
        result.Bytes.Should().BeSameAs(oversize, "a package that cannot be stamped is stored exactly as sent");
        OfficeDocumentStamp.TryReadStamp(oversize).Should().BeNull("the reader is bounded on the same terms");
    }

    [Fact]
    public void Stamp_WithAnEmptyDocumentId_Throws()
    {
        // A stamp naming Guid.Empty would resolve to no record — worse than no stamp at all.
        var act = () => OfficeDocumentStamp.Stamp(MinimalDocx.Create("Brief"), Guid.Empty);

        act.Should().Throw<ArgumentException>();
    }

    // ── helpers ──────────────────────────────────────────────────────────────────────────────────

    /// <summary>Every part's DECOMPRESSED content, keyed by part path.</summary>
    private static Dictionary<string, byte[]> PartsOf(byte[] package)
    {
        using var stream = new MemoryStream(package, writable: false);
        using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
        var parts = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (var entry in zip.Entries)
        {
            using var part = entry.Open();
            using var buffer = new MemoryStream();
            part.CopyTo(buffer);
            parts[entry.FullName] = buffer.ToArray();
        }

        return parts;
    }

    private static List<string> EntryNamesOf(byte[] package)
    {
        using var stream = new MemoryStream(package, writable: false);
        using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
        return zip.Entries.Select(entry => entry.FullName).ToList();
    }

    private static string? StampPartText(byte[] package) =>
        PartsOf(package)
            .Where(part => part.Key.StartsWith("customXml/item", StringComparison.Ordinal)
                && !part.Key.Contains("Props", StringComparison.Ordinal))
            .Select(part => Text(part.Value))
            .FirstOrDefault(text => text.Contains(OfficeDocumentStamp.StampNamespace, StringComparison.Ordinal));

    private static string Text(byte[] bytes) => new UTF8Encoding(false).GetString(bytes).TrimStart('﻿');

    /// <summary>Appends one extra part to an existing package, so a foreign custom XML part can be simulated.</summary>
    private static byte[] WithExtraPart(byte[] package, string path, string content)
    {
        var buffer = new MemoryStream(package.Length + content.Length + 512);
        buffer.Write(package, 0, package.Length);
        buffer.Position = 0;
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Update, leaveOpen: true))
        {
            var entry = zip.CreateEntry(path, CompressionLevel.Optimal);
            entry.LastWriteTime = new DateTimeOffset(1980, 1, 1, 0, 0, 0, TimeSpan.Zero);
            using var stream = entry.Open();
            var payload = new UTF8Encoding(false).GetBytes(content);
            stream.Write(payload, 0, payload.Length);
        }

        return buffer.ToArray();
    }

    /// <summary>Replaces an existing part's content, so an oversize or malformed part can be simulated.</summary>
    private static byte[] WithReplacedPart(byte[] package, string path, string content)
    {
        var buffer = new MemoryStream(package.Length + 4096);
        buffer.Write(package, 0, package.Length);
        buffer.Position = 0;
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Update, leaveOpen: true))
        {
            zip.GetEntry(path)?.Delete();
            var entry = zip.CreateEntry(path, CompressionLevel.Optimal);
            entry.LastWriteTime = new DateTimeOffset(1980, 1, 1, 0, 0, 0, TimeSpan.Zero);
            using var stream = entry.Open();
            var payload = new UTF8Encoding(false).GetBytes(content);
            stream.Write(payload, 0, payload.Length);
        }

        return buffer.ToArray();
    }

    private static int CommonPrefixLength(byte[] left, byte[] right)
    {
        var length = 0;
        while (length < left.Length && length < right.Length && left[length] == right[length])
        {
            length++;
        }

        return length;
    }

    private static int CommonSuffixLength(byte[] left, byte[] right)
    {
        var length = 0;
        while (length < left.Length && length < right.Length
            && left[left.Length - 1 - length] == right[right.Length - 1 - length])
        {
            length++;
        }

        return length;
    }
}
