// Task 044 FR-A08 (spaarkeai-compose-r8) — AN AUTHORED DOCUMENT HAS NO ORIGINAL TO LOSE AGAINST.
//
// Born-in-editor, AI-drafted and PDF-sourced documents are our file: the content model IS the document, not
// a lossy view of some prior .docx. "Some formatting was simplified when saving" on one of those describes
// no loss, because there is nothing it could be a loss RELATIVE TO.
//
// The two tests below are a matched pair on purpose. Suppression that is only ever asserted POSITIVELY is
// indistinguishable from a save that would not have warned anyway, so the Imported case runs the SAME
// document and the SAME edit and asserts the warning IS there. One of them failing means the suppression is
// either not working or working too well.
//
// MAINTAIN-class (tests/integration/seam/** KEEP path per ADR-038).

using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Sprk.Bff.Api.Models;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Compose;
using Sprk.Bff.Api.Tests.Seam.Ai;
using Xunit;
using Xunit.Abstractions;

namespace Sprk.Bff.Api.Tests.Seam.Compose;

public sealed class ComposeAuthoredWarningSuppressionTests : IClassFixture<ComposeFidelitySeamFixture>
{
    // The corpus document whose edited paragraph genuinely loses w:br soft breaks — the one document that
    // produces a REAL fidelity warning after task 044's shortfall reporting. Using the document that warns
    // is what makes the Authored assertion mean something.
    // Task 046 re-levered this fixture. It was "Engagement Letter.docx", whose only loss was two dropped
    // soft breaks — and task 046 taught soft breaks to round-trip, so that document now loses NOTHING and
    // this test would have been proving suppression against a save that would not have warned anyway. The
    // lever moved to a still-lossy construct (a complex field) rather than the assertion being weakened.
    private const string WarningFixtureFileName = "ref-cross-references.docx";
    private const string EditMarker = " [A08-SUPPRESSION]";

    private readonly ComposeFidelitySeamFixture _fixture;
    private readonly ITestOutputHelper _output;

    public ComposeAuthoredWarningSuppressionTests(ComposeFidelitySeamFixture fixture, ITestOutputHelper output)
    {
        _fixture = fixture;
        _output = output;
    }

    [Fact]
    public async Task ImportedDocument_WithARealFidelityLoss_StillWarns()
    {
        var warnings = await SaveWithPersistedOriginAsync(ComposeOrigin.Imported);

        warnings.Should().NotBeNull(
            "an IMPORTED document has an original to lose against, and this edit genuinely drops soft " +
            "breaks — suppressing that would be exactly the silence this project exists to end");
        warnings!.Should().Contain(
            w => w.Contains("field-flattened", StringComparison.OrdinalIgnoreCase),
            "the warning must name what was lost");
    }

    [Fact]
    public async Task AuthoredDocument_WithTheSameEdit_EmitsNoFidelityWarning()
    {
        var warnings = await SaveWithPersistedOriginAsync(ComposeOrigin.Authored);

        warnings.Should().BeNull(
            "an AUTHORED document has NO original to lose against — the content model IS the document. The " +
            "Imported test above proves this same edit DOES warn, so this is suppression, not a save that " +
            "would have been quiet anyway");
    }

    // ═══════════════════════════════════════════════════════════════════════════════════════════════
    // NOT TESTED HERE, and said plainly rather than left to look covered.
    //
    // The third FR-A08 criterion — "an Authored document STILL receives save-outcome warnings" — has NO
    // end-to-end test in this file. Two levers were tried and neither fired through the wire: a mixed
    // contentModel+operationLog request (rejected as an unsupported op shape before reaching the warning)
    // and a live-eTag change between load and save (the concurrency warning did not trigger from the
    // metadata mock alone). Rather than keep a test that passes without exercising the path, the gap is
    // recorded.
    //
    // What DOES hold the property is structural: the suppression removes only the warning INSTANCES
    // captured from the render call (`renderProvenanceWarnings`, compared by reference). Every
    // save-outcome warning — op-log-ignored, comment anchoring, concurrent-external-change,
    // document-metadata-stale — is constructed after that capture and cannot be in the set. That is an
    // argument from the code, not evidence from a run, and the difference matters.
    //
    // Carried to the task-045 residual list.
    // ═══════════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Drives one load → edit → save round trip through the wire with the durable
    /// <c>sprk_composeorigin</c> marker set to <paramref name="persistedOrigin"/>, and returns the
    /// degradation-warning codes the save reported (null when it reported none).
    /// </summary>
    private async Task<List<string>?> SaveWithPersistedOriginAsync(ComposeOrigin persistedOrigin)
    {
        var speId = $"spe-item-044-{persistedOrigin}".ToLowerInvariant();
        const string driveId = "drive-044-a08";
        var tenant = ComposeFidelitySeamFixture.TestTenantId;
        var sourceBytes = LoadCorpus(WarningFixtureFileName);

        _fixture.ResetBoundaries();

        _fixture.SpeMock
            .Setup(s => s.GetFileMetadataAsUserAsync(It.IsAny<HttpContext>(), driveId, speId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildFileHandle(speId, driveId, sourceBytes.Length, "\"v1\""));
        _fixture.SpeMock
            .Setup(s => s.DownloadFileAsUserAsync(It.IsAny<HttpContext>(), driveId, speId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new MemoryStream(sourceBytes.ToArray()));
        _fixture.SpeMock
            .Setup(s => s.GetCurrentVersionIdAsUserAsync(It.IsAny<HttpContext>(), driveId, speId, It.IsAny<CancellationToken>()))
            .ReturnsAsync("1.0");
        _fixture.SpeMock
            .Setup(s => s.ReplaceFileContentAsUserAsync(
                It.IsAny<HttpContext>(), driveId, speId, It.IsAny<Stream>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildFileHandle(speId, driveId, sourceBytes.Length, "\"v2\""));

        // The document-record lookup the save's promotion step performs. Without this the save 500s long
        // before it reaches the origin decision — the blanket RetrieveAsync stub an earlier draft used
        // answered EVERY lookup with a bare entity and broke the ones that expect real columns.
        var documentRecordId = Guid.NewGuid();
        _fixture.DataverseMock
            .Setup(d => d.RetrieveByAlternateKeyAsync(
                It.IsAny<string>(), It.IsAny<KeyAttributeCollection>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Entity("sprk_document", documentRecordId));
        _fixture.IndexingMock
            .Setup(i => i.EnqueueIfApplicableAsync(
                It.IsAny<PostUploadIndexingRequest>(), It.IsAny<HttpContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(PostUploadIndexingResult.Submitted(Guid.NewGuid()));

        // The durable marker the save reads to decide what the document IS. Scoped to the request that
        // ASKS for the origin column, so no other lookup is answered with this stub.
        _fixture.DataverseMock
            .Setup(d => d.RetrieveAsync(
                It.IsAny<string>(),
                It.IsAny<Guid>(),
                It.Is<string[]>(columns => columns.Contains("sprk_composeorigin")),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                var entity = new Entity("sprk_document", documentRecordId);
                entity["sprk_composeorigin"] = new OptionSetValue((int)persistedOrigin);
                return entity;
            });

        using var client = _fixture.CreateAuthenticatedClient();

        var loadResponse = await client.GetAsync(
            $"/api/compose/documents/{speId}?driveId={driveId}&tenantId={tenant}");
        var loadBody = await loadResponse.Content.ReadAsStringAsync();
        loadResponse.StatusCode.Should().Be(HttpStatusCode.OK, $"load must succeed — body: {loadBody}");

        var loadRoot = JsonNode.Parse(loadBody)!.AsObject();
        var loadedModel = loadRoot["contentModel"];
        loadedModel.Should().NotBeNull("the fixture must project into a canonical model");

        // Edit the LAST block with text — the fixture's field lives in its final paragraph, and an edit
        // that misses the construct measures a save that had nothing to report.
        var editedModel = loadedModel!.DeepClone()!.AsObject();
        var edited = false;
        foreach (var block in editedModel["blocks"]!.AsArray().Reverse())
        {
            foreach (var run in block!["runs"]?.AsArray() ?? new JsonArray())
            {
                var text = run?["text"]?.GetValue<string>();
                if (string.IsNullOrEmpty(text))
                {
                    continue;
                }

                run!["text"] = text + EditMarker;
                edited = true;
                break;
            }

            if (edited)
            {
                break;
            }
        }

        edited.Should().BeTrue("the fixture must expose an editable run");

        var saveBody = new JsonObject
        {
            ["sessionId"] = loadRoot["sessionId"]!.GetValue<string>(),
            ["tenantId"] = tenant,
            ["driveId"] = driveId,
            ["content"] = loadRoot["content"]!.DeepClone(),
            ["contentModel"] = editedModel,
        };

        var saveResponse = await client.PostAsync(
            $"/api/compose/documents/{speId}/save",
            new StringContent(saveBody.ToJsonString(), Encoding.UTF8, "application/json"));
        var saveResponseBody = await saveResponse.Content.ReadAsStringAsync();
        saveResponse.IsSuccessStatusCode.Should().BeTrue(
            $"the save must succeed — never a refusal (ADR-049 invariant 1). body: {saveResponseBody}");

        var saveRoot = JsonNode.Parse(saveResponseBody)!.AsObject();
        var codes = saveRoot["degradationWarnings"]?.AsArray()
            .Select(w => w!["code"]!.GetValue<string>())
            .ToList();

        _output.WriteLine(
            $"persistedOrigin={persistedOrigin} outcome={saveRoot["outcome"]} " +
            $"warnings=[{string.Join(", ", codes ?? new List<string>())}]");

        return codes is { Count: > 0 } ? codes : null;
    }

    private static byte[] LoadCorpus(string fileName)
    {
        var path = ComposeCorpusFixtureLocator.EnumerateDocumentPaths()
            .Single(p => string.Equals(Path.GetFileName(p), fileName, StringComparison.OrdinalIgnoreCase));
        return ComposeCorpusFixtureLocator.LoadVerifiedBytes(path);
    }

    private static FileHandleDto BuildFileHandle(string speId, string driveId, int size, string eTag) =>
        new(Id: speId, Name: WarningFixtureFileName, ParentId: null, Size: size,
            CreatedDateTime: DateTimeOffset.UtcNow, LastModifiedDateTime: DateTimeOffset.UtcNow,
            ETag: eTag, IsFolder: false, WebUrl: null, DriveId: driveId);
}
