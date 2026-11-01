using System.Collections.Concurrent;
using System.Net;
using FluentAssertions;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Tests.AccessControl;
using Xunit;

namespace Sprk.Bff.Api.Tests.Integration.DataMutation.RecordOwnership;

/// <summary>
/// field-mapping-server-write-path-r1 task 039 (FR-38; part E) — <c>PUT /api/v1/documents/{id}</c>, the REAL mapped route: a
/// document is a search-indexed table, so a body that names <c>searchIndexName</c> (the typed request's only index column; the
/// lookup is not on the request) has it dropped, never refused, and the rest of the update is made. The route's own indexing
/// stamps are server writers that do not pass through here.
/// </summary>
public sealed class DocumentPutSearchIndexTests : IClassFixture<DocumentRefileTargetNarrowingFixture>
{
    private const string AllRights = "ReadAccess,WriteAccess,AppendToAccess";

    private readonly DocumentRefileTargetNarrowingFixture _fixture;
    private readonly ConcurrentBag<UpdateDocumentRequest> _updates = new();

    public DocumentPutSearchIndexTests(DocumentRefileTargetNarrowingFixture fixture)
    {
        _fixture = fixture;
        _fixture.Reset();
        _fixture.OnDocumentUpdated = (_, request) => _updates.Add(request);
    }

    private async Task<HttpResponseMessage> PutAsync(object body)
    {
        using var client = _fixture.CreateClient(AllRights, NarrowingCallerMode.Internal);
        return await client.PutAsync($"/api/v1/documents/{DocumentRefileTargetNarrowingFixture.Refiled}",
            new StringContent(System.Text.Json.JsonSerializer.Serialize(body), System.Text.Encoding.UTF8, "application/json"));
    }

    [Fact]
    public async Task Put_ADocumentNamingASearchIndexName_HasItDropped_AndTheRestOfTheUpdateIsMade()
    {
        var response = await PutAsync(new { description = "Corrected", searchIndexName = "client-index" });

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var update = _updates.Should().ContainSingle().Subject;
        update.Description.Should().Be("Corrected");
        update.SearchIndexName.Should().BeNull("a client never sets a document's search index (FR-38)");
    }

    [Fact]
    public async Task Put_ADocumentNamingNoSearchIndexName_IsUnchanged()
    {
        var response = await PutAsync(new { description = "Corrected" });

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        _updates.Should().ContainSingle().Subject.Description.Should().Be("Corrected");
    }
}
