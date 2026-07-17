// FR-08 (task 010, E2) — unit tests for ParaIdPreParser: the load-time w14:paraId collect/mint
// substrate. Each test names a concrete production behavior that breaks if deleted (FR-08 acceptance):
//   - every body paragraph (incl. table-cell + nested-table) gets a unique paraId
//   - existing ids are collected verbatim; only gaps are minted
//   - minted ids are OOXML-valid ST_LongHexNumber (0 < x < 0x80000000)
//   - a forced collision retries and never assigns a duplicate within the part
//
// Banned-pattern compliance (tests/CLAUDE.md B1-B17): pure domain logic over real in-memory .docx
// fixtures (Open XML SDK) — no Mock<HttpMessageHandler> (B1), no DI/ctor tests (B3/B4), no
// getter/mirror tests (B6/B16). The forced-collision test uses the component's internal
// deterministic-generator seam (InternalsVisibleTo), not reflection (B8).

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using FluentAssertions;
using Sprk.Bff.Api.Services.Compose;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Compose;

public sealed class ParaIdPreParserTests
{
    // ── in-memory .docx fixture builders ───────────────────────────────────────────────────────
    private static Paragraph Para(string? paraId, string text)
    {
        var p = new Paragraph(new Run(new Text(text)));
        if (paraId is not null)
        {
            p.ParagraphId = new HexBinaryValue(paraId);
        }
        return p;
    }

    private static byte[] BuildDocx(params OpenXmlElement[] bodyChildren)
    {
        using var ms = new MemoryStream();
        using (var doc = WordprocessingDocument.Create(ms, WordprocessingDocumentType.Document))
        {
            var main = doc.AddMainDocumentPart();
            var body = new Body();
            foreach (var child in bodyChildren)
            {
                body.Append(child);
            }
            main.Document = new Document(body);
            main.Document.Save();
        }
        return ms.ToArray(); // MemoryStream.ToArray() is valid after the package disposes/flushes.
    }

    // 6 body paragraphs in document order:
    //   0 top-with-id (existing "1A2B3C4D"), 1 top-without-id,
    //   2 table cell A paragraph, 3 nested-table cell paragraph, 4 cell B trailing paragraph,
    //   5 after-table paragraph.
    private static byte[] MixedDocxWithTables() => BuildDocx(
        Para("1A2B3C4D", "top with id"),
        Para(null, "top without id"),
        new Table(
            new TableRow(
                new TableCell(Para(null, "cell A")),
                new TableCell(
                    new Table(new TableRow(new TableCell(Para(null, "nested cell")))),
                    Para(null, "cell B trailing")))),
        Para(null, "after table"));

    [Fact]
    public void Parse_MixedDocument_AssignsUniqueParaIdToEveryBodyParagraphInclTableCells()
    {
        var sut = new ParaIdPreParser();

        var result = sut.Parse(MixedDocxWithTables());

        result.Entries.Should().HaveCount(6,
            "every body paragraph — incl. table-cell + nested-table paragraphs — is mapped (FR-08)");
        result.Entries.Select(e => e.Index).Should().Equal(0, 1, 2, 3, 4, 5);
        result.Entries.Select(e => e.ParaId).Should().OnlyHaveUniqueItems();
        result.Entries.Should().OnlyContain(e => !string.IsNullOrEmpty(e.ParaId));
    }

    [Fact]
    public void Parse_ExistingParaId_IsCollectedVerbatim()
    {
        var sut = new ParaIdPreParser();

        var result = sut.Parse(MixedDocxWithTables());

        result.Entries[0].ParaId.Should().Be("1A2B3C4D");
        result.Entries[0].IsMinted.Should().BeFalse();
    }

    [Fact]
    public void Parse_MissingParaIds_AreMintedInValidOoxmlRange()
    {
        var sut = new ParaIdPreParser();

        var result = sut.Parse(MixedDocxWithTables());

        var minted = result.Entries.Where(e => e.IsMinted).ToList();
        minted.Should().HaveCount(5, "the 5 id-less paragraphs are minted; the 1 with an id is not");
        foreach (var entry in minted)
        {
            entry.ParaId.Should().MatchRegex("^[0-9A-F]{8}$", "an ST_LongHexNumber is 8 uppercase hex digits");
            var value = uint.Parse(entry.ParaId, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            value.Should().BeGreaterThan(0u).And.BeLessThan(0x80000000u, "0 < w14:paraId < 0x80000000");
        }
    }

    [Fact]
    public void Parse_ForcedCollision_RetriesAndNeverAssignsADuplicate()
    {
        // The part already contains id 10000000. The injected generator returns THAT colliding value
        // first, then a fresh one — the mint must skip the collision and assign the fresh id.
        var docx = BuildDocx(
            Para("10000000", "has id"),
            Para(null, "needs id"));
        var candidates = new Queue<uint>(new uint[] { 0x10000000, 0x20000000 });
        var sut = new ParaIdPreParser(() => candidates.Dequeue());

        var result = sut.Parse(docx);

        result.Entries.Should().HaveCount(2);
        result.Entries[0].ParaId.Should().Be("10000000");
        result.Entries[1].IsMinted.Should().BeTrue();
        result.Entries[1].ParaId.Should().Be("20000000", "the mint retried past the colliding 10000000");
        result.Entries.Select(e => e.ParaId).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void Parse_EmptyInput_ThrowsArgumentException()
    {
        var sut = new ParaIdPreParser();

        var act = () => sut.Parse(ReadOnlyMemory<byte>.Empty);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Parse_MalformedBytes_ThrowsInvalidOperationException()
    {
        var sut = new ParaIdPreParser();

        // A truncated ZIP local-file header — not a readable WordprocessingML package.
        var act = () => sut.Parse(new byte[] { 0x50, 0x4B, 0x03, 0x04 });

        act.Should().Throw<InvalidOperationException>();
    }
}
