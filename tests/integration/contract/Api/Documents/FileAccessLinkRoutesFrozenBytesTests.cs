// CONTRACT (frozen bytes) - GET /api/documents/{id}/preview-url and GET /api/documents/{id}/open-links
// (spaarkeai-mcp-bff-api-r2 task 023). KEEP path: endpoint-contract.
//
// WHY: task 023 extracts the link logic of these two routes into FileAccessEndpoints.MintPreviewUrlAsync and
// FileAccessEndpoints.ResolveOpenLinksAsync so the MCP tool spaarke_get_document calls the same code (no copy). The POML
// requires the REST bytes be frozen BEFORE the refactor and unchanged after: this file was written and run green against the
// unrefactored routes, then the extraction was made, and it still passes.
//
// WHAT IS FROZEN: the response body of each route for the allowed caller, byte for byte, except the per-request
// correlationId (the request's TraceIdentifier), which is normalised to a placeholder after being asserted present. Also
// frozen: the refusal answers of the same routes for a document whose pointer the container check refuses (409
// document_storage_unverified) and for a caller without Read (403), because the extraction moved the pointer check.
//
// HOST: FileAccessBrokerTestFixture (rights stated by the bearer token; the app-only SPE facade is a recording double that
// answers fixed values), the host task 171's identity tests use.

using System.Net;
using System.Text.RegularExpressions;
using FluentAssertions;
using Sprk.Bff.Api.Tests.AccessControl;
using Xunit;

namespace Sprk.Bff.Api.Tests.Api.Documents;

public sealed class FileAccessLinkRoutesFrozenBytesTests : IClassFixture<FileAccessBrokerTestFixture>
{
    private const string DocumentId = "17130000-0000-4000-8000-000000000001";
    private readonly FileAccessBrokerTestFixture _fixture;

    public FileAccessLinkRoutesFrozenBytesTests(FileAccessBrokerTestFixture fixture)
    {
        _fixture = fixture;
        _fixture.AppOnlyCalls.Clear();
    }

    /// <summary>The body with the per-request correlation id replaced by a fixed token (asserted present first).</summary>
    private static string Normalised(string body)
    {
        Regex.IsMatch(body, "\"correlationId\":\"[^\"]+\"").Should().BeTrue("the route writes the request's correlation id: " + body);
        return Regex.Replace(body, "\"correlationId\":\"[^\"]+\"", "\"correlationId\":\"<id>\"");
    }

    [Fact(DisplayName = "Task 023 frozen bytes: preview-url answers the same body for an allowed caller")]
    public async Task PreviewUrl_AllowedCaller_BodyIsFrozen()
    {
        using var client = _fixture.CreateClientWithRights("ReadAccess");

        var response = await client.GetAsync($"/api/documents/{DocumentId}/preview-url");

        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, body);
        Normalised(body).Should().Be(FrozenPreviewUrlBody);
    }

    [Fact(DisplayName = "Task 023 frozen bytes: open-links answers the same body for an allowed caller")]
    public async Task OpenLinks_AllowedCaller_BodyIsFrozen()
    {
        using var client = _fixture.CreateClientWithRights("ReadAccess");

        var response = await client.GetAsync($"/api/documents/{DocumentId}/open-links");

        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, body);
        body.Should().Be(FrozenOpenLinksBody);
    }

    [Theory(DisplayName = "Task 023 frozen bytes: a refused pointer is still 409 document_storage_unverified with no Graph call")]
    [InlineData("preview-url")]
    [InlineData("open-links")]
    public async Task LinkRoute_RefusedPointer_Is409_AndMakesNoGraphCall(string route)
    {
        using var client = _fixture.CreateClientWithRights("ReadAccess");

        var response = await client.GetAsync($"/api/documents/{FileAccessBrokerTestFixture.RoguePointerDocumentId}/{route}");

        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.Conflict, body);
        body.Should().Contain("document_storage_unverified");
        _fixture.AppOnlyCalls.Should().BeEmpty();
    }

    [Theory(DisplayName = "Task 023 frozen bytes: a caller without Read is still 403 on both link routes")]
    [InlineData("preview-url")]
    [InlineData("open-links")]
    public async Task LinkRoute_CallerWithoutRead_Is403(string route)
    {
        using var client = _fixture.CreateClientWithRights("");

        var response = await client.GetAsync($"/api/documents/{DocumentId}/{route}");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        _fixture.AppOnlyCalls.Should().BeEmpty();
    }

    // Captured from the routes BEFORE the extraction (the placeholders below were replaced by the observed bytes).
    private const string FrozenPreviewUrlBody =
        """{"previewUrl":"https://example.invalid/embed.aspx?token=redacted&nb=true","documentInfo":{"name":"Brief.docx","fileExtension":"docx","size":null,"lastModified":"0001-01-01T00:00:00.0000000"},"checkoutStatus":null,"correlationId":"<id>"}""";

    private const string FrozenOpenLinksBody =
        """{"desktopUrl":"ms-word:https://contoso.sharepoint.com/contentstorage/x/_layouts/15/Doc.aspx?sourcedoc=1","webUrl":"https://contoso.sharepoint.com/contentstorage/x/_layouts/15/Doc.aspx?sourcedoc=1","mimeType":"application/vnd.openxmlformats-officedocument.wordprocessingml.document","fileName":"Brief.docx"}""";
}
