// Task 050 (spaarkeai-compose-r7, FR-06 — PDF import parity, server half) — the async ProjectForMount
// PDF-fork vertical-slice seam. Before this task, ProjectForMount was synchronous / no-I/O and had NO
// PDF fork (only LoadAsync did), so a PDF opened via the Browse-project / Assistant-upload doors mounted
// fail-closed (read-only) instead of becoming an editable Compose document. Task 050 gives ProjectForMount
// the SAME IsPdfSource → ProjectPdfToDocxAsync fork LoadAsync has (@502), making it async — a documented
// ADR-007/013 contract change (NFR-04, ADR Tensions path A); the docx path STAYS synchronous-fast.
//
// What this file proves THROUGH THE WIRE (ADR-038 vertical-slice-seam KEEP category — a unit test of
// ComposeService.ProjectForMount alone would not catch a broken DI wire between ComposeEndpoints.Project
// and the now-async service, nor prove the honest 503/422 endpoint mapping the new PDF throw site needs):
//   1. A %PDF- source POSTed to the REAL POST /api/compose/project door forks onto the intake leg
//      (ProjectPdfToDocxAsync → ComposePdfModelProjector → SynthesizeDocument) and returns an EDITABLE
//      projection with sourceFormat:"pdf", the counted pdf-intake-* honest-lossiness warnings, and the
//      SYNTHESIZED docx bytes echoed back (a PK zip — NOT the PDF the caller sent), so the client can
//      save the PDF-sourced doc as a docx (the 051 flow). Parity with the Load door's PDF round-trip.
//   2. The DOCX path is UNCHANGED / synchronous-fast: a native .docx browse returns sourceFormat:null,
//      an editable projection, and NEVER touches the PDF intake source (zero intake calls) — the async
//      contract change added no I/O to the docx mount.
//   3. Intake unavailability (the compound-gate-OFF / parse-service-failure boundary) fails the mount
//      door LOUDLY with the honest 503 ProblemDetails — the SAME typed mapping the Load door has, never a
//      silent empty mount and never a generic 500 (the new ComposePdfIntakeException catch in Project).
//   4. FR-11 end-to-end: a cause-discriminated intake FAILURE (Corrupt) surfaces the CAUSE-SPECIFIC
//      message + the correct 422 (not-retryable) status through the mount door — not one collapsed
//      "corrupt or unavailable" (task 073 shipped the discrimination; task 050 wires it to the user).
//
// REUSES (root CLAUDE.md §11 — extend, don't duplicate): ComposeFidelitySeamFixture (real
// ComposeService/ComposeEndpoints + real ComposePdfModelProjector/renderer; module-boundary mocks only —
// SPE/Dataverse/indexing + the IComposePdfIntakeSource PublicContracts seam = the Azure DI call). The NDA
// layout helpers mirror ComposePdfIntakeRoundTripSeamTests (the Load-door twin) per this project's
// per-file-fixture convention.
//
// Banned-pattern compliance (ADR-038 §7 / tests/CLAUDE.md): NO Mock<HttpMessageHandler>, NO DI-registration
// test, NO ctor-null test.

using System.Net;
using System.Net.Http.Json;
using System.Text;
using FluentAssertions;
using Sprk.Bff.Api.Api;
using Sprk.Bff.Api.Services.Ai.PublicContracts;
using Sprk.Bff.Api.Tests.Seam.Ai;
using Moq;
using Xunit;

namespace Sprk.Bff.Api.Tests.Seam.Compose;

public sealed class ComposeMountPdfProjectionSeamTests : IClassFixture<ComposeFidelitySeamFixture>
{
    private readonly ComposeFidelitySeamFixture _fixture;

    public ComposeMountPdfProjectionSeamTests(ComposeFidelitySeamFixture fixture) => _fixture = fixture;

    /// <summary>Minimal real %PDF- header bytes — enough for IsPdfSource's bytes-first detection to route
    /// the mount onto the intake branch (mirrors ComposePdfIntakeRoundTripSeamTests.PdfBytes).</summary>
    private static readonly byte[] PdfBytes = Encoding.ASCII.GetBytes(
        "%PDF-1.7\n1 0 obj\n<< /Type /Catalog >>\nendobj\ntrailer\n<< >>\n%%EOF\n");

    private static DocumentLayoutBlock Para(string text, DocumentLayoutParagraphRole role = DocumentLayoutParagraphRole.Body)
        => new() { Paragraph = new DocumentLayoutParagraph(text, role, 1) };

    /// <summary>An NDA-shaped structured layout — the same shape the Load-door twin uses.</summary>
    private static DocumentLayout NdaLayout() => new()
    {
        PageCount = 2,
        Blocks = new[]
        {
            Para("CONFIDENTIAL", DocumentLayoutParagraphRole.PageHeader),
            Para("MUTUAL NON-DISCLOSURE AGREEMENT", DocumentLayoutParagraphRole.Title),
            Para("1. Confidential Information", DocumentLayoutParagraphRole.SectionHeading),
            Para("Each party agrees to hold in confidence all Confidential Information disclosed by the other party."),
            Para("2. Term", DocumentLayoutParagraphRole.SectionHeading),
            Para("This Agreement remains in effect for the period stated above."),
            Para("Page 1 of 2", DocumentLayoutParagraphRole.PageNumber),
        },
    };

    // ═══════════════════════════════════════════════════════════════════════════════════════════
    // (1) A PDF browsed via the mount door opens EDITABLE — the async ProjectForMount PDF fork.
    // ═══════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Project_PdfSource_ForksToIntake_ReturnsEditableSynthesizedDocx_WithHonestLossinessData()
    {
        _fixture.ResetBoundaries();

        // ── Boundary: the intake seam yields the NDA-shaped layout (the Azure DI double). Task 050
        //    (FR-11): ComposeService consumes ParseWithDiagnosticsAsync (cause-discriminated). ─────
        _fixture.PdfIntakeSourceMock
            .Setup(p => p.ParseWithDiagnosticsAsync(It.IsAny<byte[]>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(PdfIntakeParseResult.Success(NdaLayout()));

        using var client = _fixture.CreateAuthenticatedClient();

        var response = await client.PostAsJsonAsync("/api/compose/project",
            new { content = PdfBytes, fileName = "Corteva NDA (signed).pdf" });

        response.StatusCode.Should().Be(HttpStatusCode.OK,
            "a PDF browsed into Compose must OPEN via the mount fork — a fail-closed read-only mount is the exact gap task 050 closes");
        var project = await response.Content.ReadFromJsonAsync<ComposeProjectResponse>();
        project.Should().NotBeNull();

        // Honest-lossiness DATA contract (drives the 051 client banner + save-as-docx flow):
        project!.SourceFormat.Should().Be("pdf",
            "the mount door surfaces the PDF-source marker exactly as the Load door does (server parity)");
        var warningCodes = (project.ContentModelWarnings ?? Array.Empty<ComposeProjectionWarningResponse>())
            .Select(w => w.Code).ToList();
        warningCodes.Should().Contain("pdf-intake-fixed-layout-reflowed",
            "the fixed-layout reflow fact is ALWAYS surfaced — a PDF projection never claims to be identical to source");

        // The projection is EDITABLE and carries the NDA content:
        project.Projection.Should().NotBeNull();
        project.Projection.Status.Should().NotBe("failed", "the synthesized docx projects successfully");
        project.Projection.CanEdit.Should().BeTrue("a PDF mount is editable — the whole point of FR-06 parity");
        project.Projection.Html.Should().Contain("MUTUAL NON-DISCLOSURE AGREEMENT")
            .And.Contain("Confidential Information");

        // The echoed Content is the SYNTHESIZED DOCX (PK zip), NOT the PDF the caller sent — so the client
        // can adopt it as its retained baseline and save-as-docx (051). This is the FR-06 correctness the
        // Minted-only echo would have dropped (the renderer pre-mints, so MintAndPersist is a no-op here).
        project.Content.Should().NotBeNull("a PDF mount MUST return the synthesized docx — the caller only holds the PDF");
        project.Content!.Take(4).Should().Equal(new byte[] { 0x50, 0x4B, 0x03, 0x04 },
            "the intake synthesizes a first-class docx carrier from the canonical model");
        project.Content.Should().NotEqual(PdfBytes, "the returned bytes are the docx, not the source PDF");
    }

    // ═══════════════════════════════════════════════════════════════════════════════════════════
    // (2) The DOCX path is UNCHANGED / synchronous-fast — the async change added no I/O for docx.
    // ═══════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Project_DocxSource_StaysSynchronousFast_NeverTouchesPdfIntake()
    {
        _fixture.ResetBoundaries();

        var docx = BuildMinimalDocx("A native Word document browsed into Compose.");

        using var client = _fixture.CreateAuthenticatedClient();

        var response = await client.PostAsJsonAsync("/api/compose/project",
            new { content = docx, fileName = "brief.docx" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var project = await response.Content.ReadFromJsonAsync<ComposeProjectResponse>();
        project.Should().NotBeNull();

        project!.SourceFormat.Should().BeNull("a native docx browse is not a PDF — no source-format marker");
        project.Projection.CanEdit.Should().BeTrue();
        project.Projection.Html.Should().Contain("A native Word document browsed into Compose.");

        // The docx path added NO I/O: the intake source was NEVER consulted (the fork's await is reached
        // only on the PDF branch — this is the "docx path stays synchronous-fast" acceptance criterion).
        _fixture.PdfIntakeSourceMock.Verify(
            p => p.ParseWithDiagnosticsAsync(It.IsAny<byte[]>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "the docx mount path must never touch the PDF intake source — the async change is PDF-branch-only");
    }

    // ═══════════════════════════════════════════════════════════════════════════════════════════
    // (3) Intake unavailability fails the mount door LOUDLY — honest 503, not a silent mount / 500.
    // ═══════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Project_PdfSource_WhenIntakeUnavailable_Returns503WithHonestDetail()
    {
        _fixture.ResetBoundaries();

        // The intake seam fails with a service-side / transient cause → the retryable 503. Task 050 (FR-11):
        // ComposeService consumes the discriminated result (a non-Corrupt cause maps to unavailable/503).
        _fixture.PdfIntakeSourceMock
            .Setup(p => p.ParseWithDiagnosticsAsync(It.IsAny<byte[]>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(PdfIntakeParseResult.Failure(
                PdfIntakeFailureCause.Unknown,
                "PDF intake failed: the document layout could not be extracted. The service is unavailable."));

        using var client = _fixture.CreateAuthenticatedClient();

        var response = await client.PostAsJsonAsync("/api/compose/project",
            new { content = PdfBytes, fileName = "unreadable.pdf" });

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable,
            "intake unavailability maps to an honest 503 ProblemDetails via the mount door too, never a generic 500 or a silent empty mount");
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("PDF intake failed", "the real, user-presentable reason crosses the wire (parity with the Load door)");
    }

    // ═══════════════════════════════════════════════════════════════════════════════════════════
    // (4) FR-11 end-to-end — a cause-discriminated failure surfaces the SPECIFIC message + status.
    // ═══════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Project_PdfSource_WhenCorrupt_Returns422WithCauseSpecificMessage_NotTheCollapsedText()
    {
        _fixture.ResetBoundaries();

        // Task 073's classifier resolved a CORRUPT cause (the document itself is the problem). FR-11: the
        // cause-specific message + the NOT-retryable 422 must reach the user — not the generic 503/"corrupt
        // or unavailable" the pre-FR-11 collapsed null boundary produced for every failure alike.
        const string corruptMessage =
            "PDF intake for 'damaged.pdf' failed: the file appears to be corrupt or in an unsupported format.";
        _fixture.PdfIntakeSourceMock
            .Setup(p => p.ParseWithDiagnosticsAsync(It.IsAny<byte[]>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(PdfIntakeParseResult.Failure(PdfIntakeFailureCause.Corrupt, corruptMessage));

        using var client = _fixture.CreateAuthenticatedClient();

        var response = await client.PostAsJsonAsync("/api/compose/project",
            new { content = PdfBytes, fileName = "damaged.pdf" });

        // Corrupt = the document is the problem → 422 (retrying won't help), NOT the 503 an unavailable
        // service gets — the cause drives the status, proving discrimination reaches the endpoint.
        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity,
            "a Corrupt cause is not retryable — it maps to 422, distinct from the 503 a transient/unavailable cause gets");
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("appears to be corrupt or in an unsupported format",
            "the CAUSE-SPECIFIC message crosses the wire (FR-11) — not the collapsed 'corrupt or unavailable' catch-all");
    }

    /// <summary>Minimal in-memory .docx — a single body paragraph. Mirrors the sibling seam files' local
    /// BuildDocx convention (separate copy per file).</summary>
    private static byte[] BuildMinimalDocx(string text)
    {
        using var ms = new MemoryStream();
        using (var doc = DocumentFormat.OpenXml.Packaging.WordprocessingDocument.Create(
                   ms, DocumentFormat.OpenXml.WordprocessingDocumentType.Document))
        {
            var main = doc.AddMainDocumentPart();
            var body = new DocumentFormat.OpenXml.Wordprocessing.Body(
                new DocumentFormat.OpenXml.Wordprocessing.Paragraph(
                    new DocumentFormat.OpenXml.Wordprocessing.Run(
                        new DocumentFormat.OpenXml.Wordprocessing.Text(text)))
                {
                    ParagraphId = new DocumentFormat.OpenXml.HexBinaryValue("CCCC0001"),
                },
                new DocumentFormat.OpenXml.Wordprocessing.SectionProperties());
            main.Document = new DocumentFormat.OpenXml.Wordprocessing.Document(body);
            main.Document.Save();
        }
        return ms.ToArray();
    }
}
