// Task 032 (spaarkeai-compose-r6, FR-05 / Success Criterion 3) — through-the-wire seam slice for the
// NEW apply-template endpoint:
//
//   POST /api/compose/documents/{documentSpeId}/apply-template
//
// WHAT THIS PROVES (production code — the REAL ComposeEndpoints route through the REAL auth +
// rate-limit pipeline, the REAL ComposeService.ApplyTemplateAsync, and the REAL 030
// ComposeTemplatePartMergeEngine; ONLY the external module boundaries are doubled — ADR-038
// "mock at module boundaries", the SAME ComposeFidelitySeamFixture the Compose seam suite
// established, no new fixture class per CLAUDE.md §11):
//
//   (1) APPLY — the endpoint resolves the firm/matter template via IComposeTemplateSource (the
//       task-031 ADR-013 PublicContracts facade), downloads the document's CURRENT bytes at the
//       user-OBO SPE boundary, runs the REAL part-merge, and persists via
//       ReplaceFileContentAsUserAsync. The PERSISTED bytes (captured at the facade boundary) carry
//       the TEMPLATE's chrome (header part + landscape sectPr) AND the DOCUMENT's body text —
//       Success Criterion 3 reachable from the wire. The 200 response carries the new versionId /
//       templateName the client re-mounts on.
//   (2) TEMPLATE NOT FOUND — a null resolve (unknown name / no attachment) yields 404 with a
//       ProblemDetails, and NO SPE write happens.
//   (3) NEGATIVE AUTHORIZATION — an unauthenticated caller gets 401 (the group's
//       RequireAuthorization, ADR-008) and NEITHER the template source NOR any SPE method is
//       reached (acceptance criterion 4: rejected by the endpoint-filter auth, not an error).
//
// Banned-pattern compliance (ADR-038 §4 + tests/CLAUDE.md): NO Mock<HttpMessageHandler>; NO
// DI-registration test; NO ctor-null test. Mocks live ONLY at the ISpeFileOperations /
// IComposeTemplateSource module boundaries (+ the fixture's static fake TokenCredential — the
// endpoint mints an app-only Dataverse token before the resolve; templates are org-shared assets,
// ADR-028 server-outbound, mirroring CommunicationTemplateEndpoints).

using System.Net;
using System.Net.Http.Json;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Moq;
using Sprk.Bff.Api.Models;
using Sprk.Bff.Api.Services.Ai.PublicContracts;
using Sprk.Bff.Api.Tests.Seam.Ai;
using Xunit;

namespace Sprk.Bff.Api.Tests.Seam.Compose;

public sealed class ComposeApplyTemplateSeamTests : IClassFixture<ComposeFidelitySeamFixture>
{
    private readonly ComposeFidelitySeamFixture _fixture;

    public ComposeApplyTemplateSeamTests(ComposeFidelitySeamFixture fixture) => _fixture = fixture;

    private const string DriveId = "drive-032-apply-template-seam";
    private const string ItemId = "spe-item-032-apply-template-seam";
    private const string FirmHeaderText = "FIRM HEADER — Apply Template Seam LLP";
    private const string TemplateBoilerplate = "TEMPLATE BOILERPLATE — must be dropped";
    private const string DocumentBodyText = "The receiving party shall hold all Confidential Information in strict confidence.";
    private const string TemplateTitle = "Firm Standard";

    // ── real OOXML builders (the engine under the route is the REAL 030 engine) ──────────────────

    private static byte[] BuildTemplateDotx()
    {
        using var stream = new MemoryStream();
        using (var doc = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Template, autoSave: true))
        {
            var main = doc.AddMainDocumentPart();
            main.Document = new Document();
            var body = main.Document.AppendChild(new Body());
            body.AppendChild(new Paragraph(new Run(new Text(TemplateBoilerplate))));

            var stylesPart = main.AddNewPart<StyleDefinitionsPart>();
            stylesPart.Styles = new Styles(
                new Style(new StyleName { Val = "Normal" })
                { Type = StyleValues.Paragraph, StyleId = "Normal", Default = true });

            var headerPart = main.AddNewPart<HeaderPart>();
            headerPart.Header = new Header(new Paragraph(new Run(new Text(FirmHeaderText))));

            body.AppendChild(new SectionProperties(
                new HeaderReference { Type = HeaderFooterValues.Default, Id = main.GetIdOfPart(headerPart) },
                new PageSize { Width = 15840, Height = 12240, Orient = PageOrientationValues.Landscape },
                new PageMargin { Top = 720, Right = 720, Bottom = 720, Left = 720, Header = 360, Footer = 360, Gutter = 0 }));

            main.Document.Save();
        }
        return stream.ToArray();
    }

    private static byte[] BuildDocumentBytes()
    {
        using var stream = new MemoryStream();
        using (var doc = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document))
        {
            var main = doc.AddMainDocumentPart();
            main.Document = new Document(new Body(
                new Paragraph(new Run(new Text(DocumentBodyText))),
                new SectionProperties(new PageSize { Width = 12240, Height = 15840 })));
            main.Document.Save();
        }
        return stream.ToArray();
    }

    private static FileHandleDto ReplacedDriveItem() => new(
        Id: "7.0",
        Name: "merged-seam.docx",
        ParentId: null,
        Size: 12345,
        CreatedDateTime: DateTimeOffset.UtcNow,
        LastModifiedDateTime: DateTimeOffset.UtcNow,
        ETag: "\"etag-seam-merged-032\"",
        IsFolder: false,
        WebUrl: "https://spe/web/merged-seam",
        DriveId: DriveId);

    // ═══════════════════════════════════════════════════════════════════════════════════════════
    // (1) APPLY — 200; persisted bytes carry the template's chrome + the document's body.
    // ═══════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ApplyTemplate_MergesTemplateChromeOntoPersistedBytes_AndReturnsNewVersion()
    {
        _fixture.ResetBoundaries();

        // Template resolution boundary (task 031 facade): the resolved firm .dotx.
        _fixture.TemplateSourceMock
            .Setup(t => t.ResolveAsync(
                TemplateTitle, It.IsAny<Dictionary<string, object?>?>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ComposeResolvedTemplate(
                TemplateBytes: BuildTemplateDotx(),
                TemplateName: TemplateTitle,
                FileName: "firm-standard.dotx",
                VariablesRendered: false));

        // SPE boundary: current persisted bytes; capture the merged replace.
        _fixture.SpeMock
            .Setup(s => s.DownloadFileAsUserAsync(It.IsAny<HttpContext>(), DriveId, ItemId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new MemoryStream(BuildDocumentBytes()));

        byte[]? persisted = null;
        _fixture.SpeMock
            .Setup(s => s.ReplaceFileContentAsUserAsync(
                It.IsAny<HttpContext>(), DriveId, ItemId, It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .Callback<HttpContext, string, string, Stream, CancellationToken>((_, _, _, stream, _) =>
            {
                using var buffer = new MemoryStream();
                stream.CopyTo(buffer);
                persisted = buffer.ToArray();
            })
            .ReturnsAsync(ReplacedDriveItem());

        using var client = _fixture.CreateAuthenticatedClient();
        var response = await client.PostAsJsonAsync(
            $"/api/compose/documents/{ItemId}/apply-template",
            new { driveId = DriveId, templateIdOrName = TemplateTitle });

        response.StatusCode.Should().Be(HttpStatusCode.OK,
            $"the apply-template route must merge + persist — body: {await response.Content.ReadAsStringAsync()}");

        var payload = await response.Content.ReadFromJsonAsync<ApplyTemplateSeamResponse>();
        payload.Should().NotBeNull();
        payload!.DocumentSpeId.Should().Be(ItemId);
        payload.VersionId.Should().Be("7.0", "the new SPE version rides the response for the client re-mount");
        payload.TemplateName.Should().Be(TemplateTitle);

        // ── The persisted bytes are the PROOF: template chrome + document body (Success Criterion 3). ──
        persisted.Should().NotBeNull("the merged document must be persisted at the SPE facade boundary");
        using var doc = WordprocessingDocument.Open(new MemoryStream(persisted!, writable: false), isEditable: false);
        var main = doc.MainDocumentPart!;
        var body = main.Document!.Body!;

        body.InnerText.Should().Contain(DocumentBodyText, "the DOCUMENT's body is the content source");
        body.InnerText.Should().NotContain(TemplateBoilerplate, "the template's boilerplate body is dropped");

        var sectPr = body.Elements<SectionProperties>().Last();
        sectPr.GetFirstChild<PageSize>()!.Orient!.Value.Should().Be(PageOrientationValues.Landscape,
            "the merged sectPr is the TEMPLATE's page chrome");
        var headerRef = sectPr.GetFirstChild<HeaderReference>();
        headerRef.Should().NotBeNull();
        ((HeaderPart)main.GetPartById(headerRef!.Id!.Value!)).Header!.InnerText.Should().Contain(FirmHeaderText,
            "the template's header part travels with its sectPr reference — chrome provenance on the persisted bytes");

        doc.DocumentType.Should().Be(WordprocessingDocumentType.Document,
            "the output is a plain document (the template package re-typed), never a .dotx");
    }

    // ═══════════════════════════════════════════════════════════════════════════════════════════
    // (2) TEMPLATE NOT FOUND — 404 ProblemDetails; NO SPE write.
    // ═══════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ApplyTemplate_TemplateNotFound_Returns404_AndNeverWrites()
    {
        _fixture.ResetBoundaries();

        _fixture.TemplateSourceMock
            .Setup(t => t.ResolveAsync(
                It.IsAny<string>(), It.IsAny<Dictionary<string, object?>?>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ComposeResolvedTemplate?)null);

        using var client = _fixture.CreateAuthenticatedClient();
        var response = await client.PostAsJsonAsync(
            $"/api/compose/documents/{ItemId}/apply-template",
            new { driveId = DriveId, templateIdOrName = "No Such Template" });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound,
            "an unresolvable template is an honest 404, never a merge against nothing");
        (await response.Content.ReadAsStringAsync()).Should().Contain("Template",
            "the ProblemDetails names the template failure so the dialog can surface it");

        _fixture.SpeMock.Verify(
            s => s.ReplaceFileContentAsUserAsync(It.IsAny<HttpContext>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()),
            Times.Never, "a failed resolve must never reach the SPE write");
        _fixture.SpeMock.Verify(
            s => s.DownloadFileAsUserAsync(It.IsAny<HttpContext>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never, "the document is not even downloaded when the template cannot resolve");
    }

    // ═══════════════════════════════════════════════════════════════════════════════════════════
    // (3) NEGATIVE AUTHORIZATION — 401 before ANY boundary is reached (acceptance criterion 4).
    // ═══════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task UnauthenticatedCaller_Returns401_AndReachesNoBoundary()
    {
        _fixture.ResetBoundaries();

        // No Authorization header → the group's RequireAuthorization (ADR-008) challenges.
        using var client = _fixture.CreateClient();
        var response = await client.PostAsJsonAsync(
            $"/api/compose/documents/{ItemId}/apply-template",
            new { driveId = DriveId, templateIdOrName = TemplateTitle });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "an unauthenticated call is rejected by the endpoint auth, not an unhandled error");

        _fixture.TemplateSourceMock.Verify(
            t => t.ResolveAsync(It.IsAny<string>(), It.IsAny<Dictionary<string, object?>?>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never, "the template source must never be reached unauthenticated");
        _fixture.SpeMock.Verify(
            s => s.DownloadFileAsUserAsync(It.IsAny<HttpContext>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never, "the SPE boundary must never be reached unauthenticated");
    }

    /// <summary>Deserialization target for the 200 response (the fields this seam asserts).</summary>
    private sealed record ApplyTemplateSeamResponse(
        string DocumentSpeId,
        string? DriveId,
        string TemplateName,
        string VersionId,
        string? ETag,
        long? Size,
        string CorrelationId);
}
