using System.Net;
using System.Text;
using Azure.Core;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Sprk.Bff.Api.Api.Ai;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Services.Ai;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai;

/// <summary>
/// D-97 / PB-08: repo-deployed system playbooks are read-only for canvas-to-node sync.
/// A fake Dataverse handler records every non-GET call so "nothing was written" is asserted directly.
/// </summary>
public class NodeServiceCanvasSyncGuardTests
{
    private static readonly Guid PlaybookId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    // Deploy-shaped node config: no __canvasNodeId (what Deploy-Playbook.ps1 writes).
    private const string DeployedConfig = "{\"scope\":\"user-briefing-payload\",\"description\":\"deploy script node\"}";
    private const string DesignerConfig = "{\"__canvasNodeId\":\"canvas-1\",\"__actionType\":0}";

    private sealed class FakeDataverse : HttpMessageHandler
    {
        public string PlaybookJson = "{\"sprk_name\":\"Test\",\"sprk_issystemplaybook\":null,\"sprk_playbooktype\":0}";
        public HttpStatusCode PlaybookStatus = HttpStatusCode.OK;
        public HttpStatusCode NodesStatus = HttpStatusCode.OK;
        public List<(string? ConfigJson, Guid Id)> Nodes = [];
        public List<string> Writes { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var url = request.RequestUri!.ToString();
            if (request.Method != HttpMethod.Get)
            {
                Writes.Add($"{request.Method} {url}");
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent));
            }

            if (url.Contains("sprk_analysisplaybooks("))
                return Task.FromResult(Json(PlaybookStatus, PlaybookJson));

            if (url.Contains("sprk_playbooknodes"))
            {
                var rows = string.Join(",", Nodes.Select(n =>
                    "{\"sprk_playbooknodeid\":\"" + n.Id + "\",\"sprk_name\":\"n\",\"sprk_configjson\":"
                    + (n.ConfigJson is null ? "null" : System.Text.Json.JsonSerializer.Serialize(n.ConfigJson)) + "}"));
                return Task.FromResult(Json(NodesStatus, "{\"value\":[" + rows + "]}"));
            }

            return Task.FromResult(Json(HttpStatusCode.OK, "{\"value\":[]}"));
        }

        private static HttpResponseMessage Json(HttpStatusCode code, string body) =>
            new(code) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    }

    private static NodeService CreateService(FakeDataverse handler)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Dataverse:ServiceUrl"] = "https://org.example.crm.dynamics.com" })
            .Build();
        var credential = new Mock<TokenCredential>();
        credential
            .Setup(c => c.GetTokenAsync(It.IsAny<TokenRequestContext>(), It.IsAny<CancellationToken>()))
            .Returns(new ValueTask<AccessToken>(new AccessToken("t", DateTimeOffset.UtcNow.AddHours(1))));
        return new NodeService(new HttpClient(handler), config, credential.Object, NullLogger<NodeService>.Instance);
    }

    private static CanvasLayoutDto Canvas() => new()
    {
        Nodes = [new CanvasNodeDto { Id = "canvas-1", Type = "aiAnalysis" }],
        Edges = []
    };

    // ---- Refusals (negative tests: zero writes) ----

    [Fact]
    public async Task Sync_SystemFlagTrue_IsRefusedAndWritesNothing()
    {
        var h = new FakeDataverse
        {
            PlaybookJson = "{\"sprk_name\":\"matter-health-single\",\"sprk_issystemplaybook\":true,\"sprk_playbooktype\":0}",
            Nodes = [(DeployedConfig, Guid.NewGuid()), (DeployedConfig, Guid.NewGuid())]
        };

        var act = () => CreateService(h).SyncCanvasToNodesAsync(PlaybookId, Canvas());

        (await act.Should().ThrowAsync<ProtectedPlaybookCanvasSyncException>())
            .Which.Reason.Should().Be(ProtectedPlaybookReason.SystemFlag);
        h.Writes.Should().BeEmpty();
    }

    [Fact]
    public async Task Sync_NotificationTypeWithNullFlag_IsRefusedAndWritesNothing()
    {
        // New Work Assignments / Matter Activity Summary shape: flag NULL, type 2, deploy-shaped nodes.
        var h = new FakeDataverse
        {
            PlaybookJson = "{\"sprk_name\":\"New Work Assignments\",\"sprk_issystemplaybook\":null,\"sprk_playbooktype\":2}",
            Nodes = [(DeployedConfig, Guid.NewGuid())]
        };

        var act = () => CreateService(h).SyncCanvasToNodesAsync(PlaybookId, Canvas());

        (await act.Should().ThrowAsync<ProtectedPlaybookCanvasSyncException>())
            .Which.Reason.Should().Be(ProtectedPlaybookReason.NotificationType);
        h.Writes.Should().BeEmpty();
    }

    [Fact]
    public async Task Sync_NotFlaggedButOneNodeLacksCanvasId_IsRefusedAndWritesNothing()
    {
        var h = new FakeDataverse
        {
            Nodes = [(DesignerConfig, Guid.NewGuid()), (DeployedConfig, Guid.NewGuid())]
        };

        var act = () => CreateService(h).SyncCanvasToNodesAsync(PlaybookId, Canvas());

        (await act.Should().ThrowAsync<ProtectedPlaybookCanvasSyncException>())
            .Which.Reason.Should().Be(ProtectedPlaybookReason.RepoDeployedNodes);
        h.Writes.Should().BeEmpty();
    }

    [Fact]
    public async Task Sync_NodeWithNullConfigJson_CountsAsRepoDeployed()
    {
        var h = new FakeDataverse { Nodes = [(null, Guid.NewGuid())] };

        var act = () => CreateService(h).SyncCanvasToNodesAsync(PlaybookId, Canvas());

        await act.Should().ThrowAsync<ProtectedPlaybookCanvasSyncException>();
        h.Writes.Should().BeEmpty();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Sync_ReadFails_FailsClosedAndWritesNothing(bool playbookReadFails)
    {
        var h = new FakeDataverse { Nodes = [(DesignerConfig, Guid.NewGuid())] };
        if (playbookReadFails) h.PlaybookStatus = HttpStatusCode.InternalServerError;
        else h.NodesStatus = HttpStatusCode.InternalServerError;

        var act = () => CreateService(h).SyncCanvasToNodesAsync(PlaybookId, Canvas());

        (await act.Should().ThrowAsync<ProtectedPlaybookCanvasSyncException>())
            .Which.Reason.Should().Be(ProtectedPlaybookReason.Unverifiable);
        h.Writes.Should().BeEmpty();
    }

    // ---- Regression: user-authored playbooks sync exactly as before ----

    [Fact]
    public async Task Sync_UserAuthoredPlaybookWithAllCanvasIds_StillSyncs()
    {
        var existing = Guid.NewGuid();
        var h = new FakeDataverse { Nodes = [(DesignerConfig, existing)] };

        await CreateService(h).SyncCanvasToNodesAsync(PlaybookId, Canvas());

        h.Writes.Should().Contain(w => w.StartsWith("PATCH") && w.Contains(existing.ToString()),
            "the existing canvas node is updated in place");
    }

    [Fact]
    public async Task Sync_UserAuthoredPlaybookWithNoNodes_StillCreatesNodes()
    {
        var h = new FakeDataverse();

        await CreateService(h).SyncCanvasToNodesAsync(PlaybookId, Canvas());

        h.Writes.Should().Contain(w => w.StartsWith("POST") && w.Contains("sprk_playbooknodes"));
    }

    // ---- Designer round trip through the real endpoint (sweep section 10.1) ----

    [Theory]
    [InlineData("matter-health-single", "true", "0")]
    [InlineData("New Work Assignments", "null", "2")]
    public async Task SaveCanvasLayout_ForRepoDeployedPlaybook_Returns409_PersistsNothing_LeavesNodesUntouched(
        string name, string flag, string type)
    {
        var h = new FakeDataverse
        {
            PlaybookJson = "{\"sprk_name\":\"" + name + "\",\"sprk_issystemplaybook\":" + flag + ",\"sprk_playbooktype\":" + type + "}",
            Nodes = Enumerable.Range(0, 9).Select(_ => ((string?)DeployedConfig, Guid.NewGuid())).ToList()
        };
        var playbookService = new Mock<IPlaybookService>(MockBehavior.Strict); // any call fails the test

        var result = await PlaybookEndpoints.SaveCanvasLayout(
            PlaybookId,
            new SaveCanvasLayoutRequest { Layout = Canvas() },
            playbookService.Object,
            CreateService(h),
            NullLoggerFactory.Instance,
            CancellationToken.None);

        var problem = result.Should().BeOfType<ProblemHttpResult>().Subject;
        problem.StatusCode.Should().Be(StatusCodes.Status409Conflict);
        problem.ProblemDetails.Detail.Should().Contain(name).And.Contain("read-only");
        h.Writes.Should().BeEmpty("no node may be deleted, created or updated");
        playbookService.VerifyNoOtherCalls(); // canvas JSON not persisted either
    }

    [Fact]
    public async Task SaveCanvasLayout_ForUserAuthoredPlaybook_PersistsAndSyncs()
    {
        var h = new FakeDataverse { Nodes = [(DesignerConfig, Guid.NewGuid())] };
        var playbookService = new Mock<IPlaybookService>();
        playbookService
            .Setup(p => p.SaveCanvasLayoutAsync(PlaybookId, It.IsAny<CanvasLayoutDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CanvasLayoutResponse { PlaybookId = PlaybookId });

        var result = await PlaybookEndpoints.SaveCanvasLayout(
            PlaybookId,
            new SaveCanvasLayoutRequest { Layout = Canvas() },
            playbookService.Object,
            CreateService(h),
            NullLoggerFactory.Instance,
            CancellationToken.None);

        result.Should().BeOfType<Ok<CanvasLayoutResponse>>();
        playbookService.Verify(p => p.SaveCanvasLayoutAsync(PlaybookId, It.IsAny<CanvasLayoutDto>(), It.IsAny<CancellationToken>()), Times.Once);
        h.Writes.Should().NotBeEmpty();
    }
}
