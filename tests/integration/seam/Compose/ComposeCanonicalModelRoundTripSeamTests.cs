// Task 020 (spaarkeai-compose-r6, FR-03) — the CANONICAL-MODEL round-trip seam slice: the Phase-2
// anchor's acceptance verifier for "docx projects into the single canonical ComposeContentModel and
// the model renders back out without hard-fail" (see
// projects/spaarkeai-compose-r6/notes/020-canonical-hub-design.md).
//
// SCOPE (explicit — read before extending): this slice proves the MODEL half of the render-on-save
// pivot over every corpus doc:
//   1. docx → ComposeDocxProjectionBuilder.BuildContentModel — TOTAL: never throws, never Failed on a
//      valid corpus doc (flatten warnings allowed; that is the ADR-049 Path-B accept-flatten posture).
//   2. model → ComposeDocumentRenderer.SynthesizeDocument — renders a valid, openable OOXML package.
//   3. rendered docx → BuildContentModel again — structural STABILITY: the top-level block-kind
//      sequence survives a full model→docx→model cycle (the hub is a fixed point, not a lossy funnel).
// Per-feature fidelity oracles (numbering golden labels, tables, headers/footers, tracked-changes,
// hard-tier warning surface) are tasks 021–026 + the 027 fidelity suite; the THROUGH-THE-WIRE save
// assertions (NDA saves via POST /save with no 422 on the shipped path) are tasks 010/013 — this
// slice deliberately tests the pure components the same way ComposeReadFidelityHarnessSeamTests
// drives the projection builder directly with no WebApplicationFactory.
//
// The NDA-specific facts pin the 422 root cause is neutralized BY CONSTRUCTION on this path: the
// fixture's duplicate w14:paraId + text-box signature block (task 004's empirically confirmed
// breakers: 7 mc:AlternateContent pairs, 12 w:txbxContent, 3 duplicate w14:paraId) project with
// counted warnings — never a refusal — and the rendered output carries a UNIQUE paraId on every
// paragraph (AssignParaIds dedup), so the count-gate's mismatch condition cannot exist here.
//
// NEGATIVE (ADR-038 banned patterns): NO Mock<HttpMessageHandler>, NO DI-registration test, NO
// ctor-null test anywhere in this file.
//
// Corpus: EVERY `.docx` under `tests/fixtures/compose-corpus/`, enumerated via
// ComposeCorpusFixtureLocator (glob — an owner-supplied worst-offender dropped in later is
// automatically covered).

using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using FluentAssertions;
using Sprk.Bff.Api.Services.Compose;
using Xunit;

namespace Sprk.Bff.Api.Tests.Seam.Compose;

public sealed class ComposeCanonicalModelRoundTripSeamTests
{
    private const string NdaFixtureName = "AppligentNDA_Signed.docx";

    /// <summary>xUnit [MemberData] source — every corpus `.docx`, glob-enumerated (not hardcoded).</summary>
    public static IEnumerable<object[]> CorpusDocuments() =>
        ComposeCorpusFixtureLocator.EnumerateDocumentPaths().Select(path => new object[] { path });

    [Theory]
    [MemberData(nameof(CorpusDocuments))]
    public void EveryCorpusDoc_ProjectsToCanonicalModel_AndRendersBack_WithoutHardFail(string corpusDocPath)
    {
        var original = ComposeCorpusFixtureLocator.LoadVerifiedBytes(corpusDocPath);
        var docName = Path.GetFileName(corpusDocPath);
        var builder = new ComposeDocxProjectionBuilder();

        // 1. docx → canonical model: total, never Failed on a valid corpus doc.
        var projection = builder.BuildContentModel(original);
        projection.Status.Should().NotBe(ComposeProjectionStatus.Failed,
            $"'{docName}' is a valid corpus doc — the canonical-model projection is TOTAL and must not fail-close on it " +
            $"(warnings: {string.Join(", ", projection.Warnings.Select(w => $"{w.Code}×{w.Count}"))})");
        projection.Model.Blocks.Should().NotBeEmpty(
            $"'{docName}' contains prose — a zero-block projection would make the rest of this round-trip vacuous (review finding 020-R16)");

        // 2. model → docx: the renderer authors a valid, openable package from the projected model.
        var rendered = new ComposeDocumentRenderer().SynthesizeDocument(projection.Model, author: "seam-test");
        rendered.Should().NotBeNullOrEmpty($"'{docName}' must render from its canonical model");
        using (var doc = WordprocessingDocument.Open(new MemoryStream(rendered, writable: false), isEditable: false))
        {
            doc.MainDocumentPart.Should().NotBeNull($"'{docName}' rendered output must be a valid OOXML package");
            doc.MainDocumentPart!.Document?.Body.Should().NotBeNull($"'{docName}' rendered output must carry a body");
        }

        // 3. rendered docx → canonical model again: the top-level block-kind sequence is STABLE across
        //    a full model→docx→model cycle (the hub round-trips its own output — a fixed point).
        var reprojection = builder.BuildContentModel(rendered);
        reprojection.Status.Should().NotBe(ComposeProjectionStatus.Failed,
            $"'{docName}' rendered output must re-project (round-trip stability)");
        reprojection.Model.Blocks.Select(b => b.Kind).Should().Equal(
            projection.Model.Blocks.Select(b => b.Kind),
            $"'{docName}' top-level block-kind sequence must survive the model→docx→model cycle");
    }

    [Fact]
    public void NdaFixture_ProjectsWithAcceptFlattenWarnings_NeverRefused()
    {
        var path = CorpusPathOf(NdaFixtureName);
        var projection = new ComposeDocxProjectionBuilder()
            .BuildContentModel(ComposeCorpusFixtureLocator.LoadVerifiedBytes(path));

        // The NDA's hard-tier constructs (task 004: text-box signature block inside w:drawing runs)
        // FLATTEN with counted warnings — the accept-flatten posture — never a hard-fail/refusal.
        projection.Status.Should().Be(ComposeProjectionStatus.Partial,
            "the NDA carries hard-tier constructs that must flatten with warnings, not fail");
        projection.Warnings.Should().Contain(w => w.Code == "complex-object-dropped",
            "the NDA's 12 w:txbxContent text boxes live inside drawing/AlternateContent runs — dropped with a counted warning until task 026 widens the surface");
        projection.Model.Blocks.Should().NotBeEmpty("the NDA's editable prose must survive projection");
    }

    [Fact]
    public void NdaFixture_RenderedFromCanonicalModel_CarriesUniqueParaIdOnEveryParagraph()
    {
        // The 422's root cause was the count-gate refusing the NDA's DUPLICATE w14:paraId (3 dupes in
        // the signature block). On the render-on-save path that condition cannot exist: the model
        // carries the source ids (dupes included) and the renderer's AssignParaIds dedup pass mints
        // fresh ids for collisions — every rendered paragraph ends up uniquely addressable.
        var path = CorpusPathOf(NdaFixtureName);
        var builder = new ComposeDocxProjectionBuilder();
        var projection = builder.BuildContentModel(ComposeCorpusFixtureLocator.LoadVerifiedBytes(path));
        var rendered = new ComposeDocumentRenderer().SynthesizeDocument(projection.Model, author: "seam-test");

        using var doc = WordprocessingDocument.Open(new MemoryStream(rendered, writable: false), isEditable: false);
        var paraIds = doc.MainDocumentPart!.Document!.Body!
            .Descendants<Paragraph>()
            .Select(p => p.ParagraphId?.Value?.ToUpperInvariant())
            .ToList();

        paraIds.Should().NotBeEmpty();
        paraIds.Should().NotContainNulls("every rendered paragraph must carry a w14:paraId (E2 substrate)");
        paraIds.Should().OnlyHaveUniqueItems(
            "AssignParaIds dedups source-carried duplicates — the count-gate's mismatch condition cannot exist on this path");
    }

    [Fact]
    public void UnreadableSource_FailsClosed_NeverThrows()
    {
        var builder = new ComposeDocxProjectionBuilder();

        var garbage = builder.BuildContentModel(new byte[] { 0x00, 0x01, 0x02, 0x03 });
        garbage.Status.Should().Be(ComposeProjectionStatus.Failed);
        garbage.Model.Blocks.Should().BeEmpty("a failed projection must not offer a renderable model over a non-empty original");

        var empty = builder.BuildContentModel(ReadOnlyMemory<byte>.Empty);
        empty.Status.Should().Be(ComposeProjectionStatus.Failed);
    }

    private static string CorpusPathOf(string fileName)
    {
        var path = ComposeCorpusFixtureLocator.EnumerateDocumentPaths()
            .FirstOrDefault(p => Path.GetFileName(p).Equals(fileName, StringComparison.OrdinalIgnoreCase));
        path.Should().NotBeNull($"corpus fixture '{fileName}' must exist (task 004 added it — run `git lfs pull` if missing)");
        return path!;
    }
}
