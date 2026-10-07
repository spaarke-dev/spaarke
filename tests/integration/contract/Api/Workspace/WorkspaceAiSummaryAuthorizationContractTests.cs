using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.Xrm.Sdk;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Tests.Api.Ai;
using Xunit;

namespace Sprk.Bff.Api.Tests.Api.Workspace;

/// <summary>
/// <b>POST /api/workspace/ai/summary authorization</b> — unified-access-control-r2 task 163 (sweep
/// finding #46). Drives the REAL <c>MapWorkspaceAiEndpoints</c> through
/// <see cref="RouteSweepAuthorizationFixture"/>. The route used to read any matter / project / event /
/// document app-only for any signed-in caller and return an AI summary of it; now the declaration filter
/// asks Dataverse, AS THE CALLER, for Read on exactly that record first, and an unreadable, an absent and a
/// deleted-between-check-and-fetch record all get the same uniform 404.
/// </summary>
[Trait("category", "authorization")]
public sealed class WorkspaceAiSummaryAuthorizationContractTests : IClassFixture<RouteSweepAuthorizationFixture>
{
    private readonly RouteSweepAuthorizationFixture _fixture;

    private static readonly Guid Readable = Guid.Parse("16300000-0000-0000-0000-0000000e0001");
    private static readonly Guid Unreadable = Guid.Parse("16300000-0000-0000-0000-0000000e0002");
    private static readonly Guid NonExistent = Guid.Parse("16300000-0000-0000-0000-0000000effff");

    public WorkspaceAiSummaryAuthorizationContractTests(RouteSweepAuthorizationFixture fixture)
    {
        _fixture = fixture;
        _fixture.ResetBoundaries();
        _fixture.Access.Grant(Readable, AccessRights.Read);

        _fixture.Entities
            .Setup(e => e.RetrieveAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string logicalName, Guid id, string[] _, CancellationToken _) => new Entity(logicalName, id));
        _fixture.Documents
            .Setup(d => d.GetDocumentAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string id, CancellationToken _) => new DocumentEntity { Id = id, Name = "brief.pdf" });
    }

    [Theory]
    [InlineData("sprk_event")]
    [InlineData("sprk_matter")]
    [InlineData("sprk_project")]
    [InlineData("sprk_document")]
    [InlineData("SPRK_MATTER")]
    public async Task UnreadableAndAbsentRecords_AreTheIdenticalUniform404_AndNothingIsRead(string entityType)
    {
        var unreadable = await SummarizeAsync(entityType, Unreadable);
        var absent = await SummarizeAsync(entityType, NonExistent);

        await RagEndpointsAuthorizationContractTests.AssertUniformNotFoundAsync(unreadable);
        (await RouteSweepAuthorizationFixture.NormalizedProblemAsync(absent))
            .Should().Be(await RouteSweepAuthorizationFixture.NormalizedProblemAsync(unreadable));
        (await unreadable.Content.ReadAsStringAsync()).Should().NotContain(Unreadable.ToString());

        VerifyNoDataverseRead();
    }

    [Theory]
    [InlineData("sprk_event", "sprk_events")]
    [InlineData("sprk_matter", "sprk_matters")]
    [InlineData("sprk_project", "sprk_projects")]
    [InlineData("sprk_document", "sprk_documents")]
    public async Task TheRightsQuestionNamesTheRecordsEntitySet_AsTheCaller(string entityType, string entitySet)
    {
        await SummarizeAsync(entityType, Unreadable);

        _fixture.Access.RecordChecks.Should().ContainSingle()
            .Which.Should().Be((RouteSweepAuthorizationFixture.CallerObjectId, entitySet, Unreadable, RouteSweepAuthorizationFixture.BearerToken));
    }

    [Theory]
    [InlineData("sprk_event")]
    [InlineData("sprk_matter")]
    [InlineData("sprk_project")]
    [InlineData("sprk_document")]
    public async Task AReader_GetsTodays200(string entityType)
    {
        var response = await SummarizeAsync(entityType, Readable);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task ADocumentDeletedBetweenTheCheckAndTheFetch_GetsTheSameUniform404_WithNoIdentifiersInTheBody()
    {
        _fixture.Documents
            .Setup(d => d.GetDocumentAsync(Readable.ToString(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((DocumentEntity?)null);

        var deleted = await SummarizeAsync("sprk_document", Readable);
        var unreadable = await SummarizeAsync("sprk_document", Unreadable);

        await RagEndpointsAuthorizationContractTests.AssertUniformNotFoundAsync(deleted);
        var body = await deleted.Content.ReadAsStringAsync();
        body.Should().NotContain(Readable.ToString()).And.NotContain("entityType").And.NotContain("entityId");
        (await RouteSweepAuthorizationFixture.NormalizedProblemAsync(deleted))
            .Should().Be(await RouteSweepAuthorizationFixture.NormalizedProblemAsync(unreadable));
    }

    [Fact]
    public async Task AnUnsupportedEntityType_IsTodays400_WithNoRightsQuery()
    {
        var response = await SummarizeAsync("account", Readable);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("is not supported for AI summary");
        _fixture.Access.RecordChecks.Should().BeEmpty();
        VerifyNoDataverseRead();
    }

    [Theory]
    [InlineData("no-bearer")]
    [InlineData("seam-throws")]
    public async Task FailsClosed(string mode)
    {
        _fixture.Access.ThrowOnCheck = mode == "seam-throws";

        var response = await SummarizeAsync("sprk_matter", Readable, _fixture.CreateCallerClient(withBearer: mode != "no-bearer"));

        await RagEndpointsAuthorizationContractTests.AssertUniformNotFoundAsync(response);
        VerifyNoDataverseRead();
    }

    [Fact]
    public async Task WithNoOid_Is401_AndNothingIsRead()
    {
        var response = await SummarizeAsync("sprk_matter", Readable, _fixture.CreateCallerClient(withOid: false));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        VerifyNoDataverseRead();
    }

    private Task<HttpResponseMessage> SummarizeAsync(string entityType, Guid entityId, HttpClient? client = null) =>
        (client ?? _fixture.CreateCallerClient()).PostAsJsonAsync("/api/workspace/ai/summary",
            new { entityType, entityId, context = (string?)null });

    private void VerifyNoDataverseRead()
    {
        _fixture.Entities.Verify(
            e => e.RetrieveAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _fixture.Documents.Verify(d => d.GetDocumentAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
