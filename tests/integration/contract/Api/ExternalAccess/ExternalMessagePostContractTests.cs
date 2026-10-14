// spaarke-SPA-external-access-platform-r3 task 034 (review fix round) - pipeline-level contract tests for
// POST /api/v1/external/{root}/{id}/messages.
//
// ExternalMessageSendTests drive PostMessageCoreAsync with a hand-bound request, so they never prove what the real
// pipeline (routing, the group's authorization policy, the CallerPrincipal filter, minimal-API body binding, the
// System.Text.Json [JsonExtensionData] capture) does with the raw HTTP body. These do, through the same in-process BFF
// fixture the sibling external contract tests use. Like them they assert only the layer that runs BEFORE any Dataverse
// write (request validation answers first), so no live ServiceClient is needed; the success path is covered by
// ExternalMessageSendTests over the real CommunicationService.
//
// KEEP-path classification (ADR-038 section 2 / tests/CLAUDE.md): endpoint-contract. Names are
// {Method}_{Scenario}_{ExpectedResult}.

using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Sprk.Bff.Api.Api.ExternalAccess;
using Xunit;

namespace Sprk.Bff.Api.Tests.Api.ExternalAccess;

public sealed class ExternalMessagePostContractTests : IClassFixture<ExternalAccessContractFixture>
{
    private static readonly Guid ProjectA = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private readonly ExternalAccessContractFixture _fixture;

    public ExternalMessagePostContractTests(ExternalAccessContractFixture fixture)
    {
        _fixture = fixture;
        _fixture.Reset();
    }

    private static string MessagesPath(Guid projectId) => $"/api/v1/external/projects/{projectId}/messages";

    private static StringContent Json(string json) => new(json, Encoding.UTF8, "application/json");

    private static async Task<string?> ErrorCode(HttpResponseMessage response)
    {
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.TryGetProperty("errorCode", out var code) ? code.GetString() : null;
    }

    [Fact]
    public async Task PostMessage_WhenUnauthenticated_Returns401()
    {
        using var client = _fixture.CreateUnauthenticatedClient();

        using var response = await client.PostAsync(MessagesPath(ProjectA), Json("""{ "sprk_body": "hello" }"""));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Theory]
    [InlineData("""{ "sprk_body": "see attached", "attachments": [ { "name": "a.pdf" } ] }""")]
    [InlineData("""{ "sprk_body": "see attached", "attachmentDocumentIds": [ "a" ] }""")]
    [InlineData("""{ "sprk_body": "see attached", "Files": [ "x" ] }""")]
    public async Task PostMessage_WithAnAttachmentsKey_IsRefused400_ThroughTheRealBodyBinding(string json)
    {
        using var client = _fixture.CreateAuthenticatedClient(accessibleProjects: new[] { ProjectA });

        using var response = await client.PostAsync(MessagesPath(ProjectA), Json(json));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ErrorCode(response)).Should().Be(ExternalMessageEndpoints.ErrorAttachmentsNotSupported,
            "the extension-data capture must see the key on the wire, not only on a hand-bound DTO");
    }

    [Fact]
    public async Task PostMessage_AsMultipartFormData_IsRefused415_NothingIsBound()
    {
        using var client = _fixture.CreateAuthenticatedClient(accessibleProjects: new[] { ProjectA });
        using var form = new MultipartFormDataContent
        {
            { new StringContent("hello"), "sprk_body" },
            { new ByteArrayContent("x"u8.ToArray()), "file", "a.txt" },
        };

        using var response = await client.PostAsync(MessagesPath(ProjectA), form);

        response.StatusCode.Should().Be(HttpStatusCode.UnsupportedMediaType,
            "the route accepts application/json only; a file part can never arrive through a form body");
    }

    [Fact]
    public async Task PostMessage_WithAnEmptyBody_Returns400_InvalidBody()
    {
        using var client = _fixture.CreateAuthenticatedClient(accessibleProjects: new[] { ProjectA });

        using var response = await client.PostAsync(MessagesPath(ProjectA), Json(""));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ErrorCode(response)).Should().Be(ExternalMessageEndpoints.ErrorInvalidBody);
    }

    [Fact]
    public async Task PostMessage_WithABlankText_Returns400_InvalidBody()
    {
        using var client = _fixture.CreateAuthenticatedClient(accessibleProjects: new[] { ProjectA });

        using var response = await client.PostAsync(MessagesPath(ProjectA), Json("""{ "sprk_body": "   " }"""));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ErrorCode(response)).Should().Be(ExternalMessageEndpoints.ErrorInvalidBody);
    }
}
