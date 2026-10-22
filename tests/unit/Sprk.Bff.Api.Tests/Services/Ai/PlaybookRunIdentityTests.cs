using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Insights.Routing;
using Sprk.Bff.Api.Services.Ai.Nodes;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai;

/// <summary>
/// #2257 (owner round 123, "run as the user"): who a playbook run's record writes run AS, from the entry to the node.
/// <list type="bullet">
/// <item>Every HTTP entry is a person's run (<see cref="PlaybookOrchestrationService.ExecuteAsync"/>): the run routes pass the
/// user their filter resolved; every other HTTP entry (analysis execute / useAiSummary, Insights ask, workspace pre-fill and
/// summary, the Console's re-analysis tool, the Compose background profile) is resolved by the orchestrator the same way —
/// WhoAmI over the request's OBO token. A System marker is not honoured on that path.</item>
/// <item>A background entry (<see cref="PlaybookOrchestrationService.ExecuteAppOnlyAsync"/>) says who started it. Only a System
/// run writes app-only; a person's run there acts for <see cref="PlaybookRunRequest.RunUserId"/> alone, never for a
/// <c>userId</c> parameter.</item>
/// <item>The entries that mark System are exactly the scheduler and the Service Bus jobs (source guard).</item>
/// </list>
/// </summary>
public class PlaybookRunIdentityTests
{
    private static readonly Guid Person = Guid.Parse("22570000-0000-4000-8000-00000000a001");
    private static readonly Guid Target = Guid.Parse("22570000-0000-4000-8000-00000000a0ff");
    private static readonly Guid Matter = Guid.Parse("22570000-0000-4000-8000-00000000b001");

    private readonly Mock<INodeService> _nodes = new();
    private readonly Mock<INodeExecutorRegistry> _executors = new();
    private readonly Guid _playbookId = Guid.NewGuid();
    private NodeExecutionContext? _seen;

    private PlaybookOrchestrationService Service()
    {
        var router = new Mock<IInsightsActionRouter>();
        router
            .Setup(r => r.ResolveLayer1ActionAsync(It.IsAny<string?>(), It.IsAny<AnalysisAction>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string? _, AnalysisAction action, CancellationToken _) => action);
        router
            .Setup(r => r.ResolveLayer2ActionAsync(It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<AnalysisAction>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string? _, string? __, AnalysisAction action, CancellationToken _) => InsightsLayer2RoutingResult.PassThrough(action));
        return new PlaybookOrchestrationService(
            _nodes.Object, _executors.Object, Mock.Of<IScopeResolverService>(), Mock.Of<IAnalysisOrchestrationService>(),
            router.Object, new TemplateEngine(NullLogger<TemplateEngine>.Instance), NullLogger<PlaybookOrchestrationService>.Instance);
    }

    /// <summary>One Update Record node; the registry hands it <paramref name="executor"/>, or a capturing double.</summary>
    private void OneUpdateRecordNode(INodeExecutor? executor = null)
    {
        var node = new PlaybookNodeDto
        {
            Id = Guid.NewGuid(),
            Name = "persistEnvelope",
            ActionId = Guid.Empty,
            OutputVariable = "persistResult",
            ExecutionOrder = 1,
            DependsOn = [],
            IsActive = true,
            NodeType = NodeType.Workflow,
            SprkExecutortype = ExecutorType.UpdateRecord,
            ConfigJson = $"{{\"entityLogicalName\":\"sprk_matter\",\"recordId\":\"{Matter}\",\"fields\":{{\"sprk_performancesummary\":\"x\"}}}}",
        };
        _nodes.Setup(n => n.GetNodesAsync(_playbookId, It.IsAny<CancellationToken>())).ReturnsAsync([node]);

        if (executor is null)
        {
            var capturing = new Mock<INodeExecutor>();
            capturing.Setup(x => x.Validate(It.IsAny<NodeExecutionContext>())).Returns(NodeValidationResult.Success());
            capturing.Setup(x => x.ExecuteAsync(It.IsAny<NodeExecutionContext>(), It.IsAny<CancellationToken>()))
                .Callback<NodeExecutionContext, CancellationToken>((ctx, _) => _seen = ctx)
                .ReturnsAsync(NodeOutput.Ok(node.Id, node.OutputVariable, new { updated = true }));
            executor = capturing.Object;
        }

        _executors.Setup(r => r.GetExecutor(ExecutorType.UpdateRecord)).Returns(executor);
    }

    /// <summary>An authenticated request whose WhoAmI answers <paramref name="whoAmI"/> (null: unresolvable).</summary>
    private static HttpContext Request(Guid? whoAmI)
    {
        var services = new ServiceCollection();
        services.AddSingleton<CallerRecordAccessProbe>(new WhoAmIProbe(whoAmI));
        var context = new DefaultHttpContext { RequestServices = services.BuildServiceProvider() };
        context.Request.Headers.Authorization = "Bearer caller-token";
        return context;
    }

    private PlaybookRunRequest RunRequest() => new() { PlaybookId = _playbookId, DocumentIds = [] };

    private static async Task DrainAsync(IAsyncEnumerable<PlaybookStreamEvent> run)
    {
        await foreach (var _ in run)
        {
        }
    }

    // ── HTTP entries: always a person's run ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AnHttpRun_ThatCarriesTheRunUser_ActsForThem()
    {
        OneUpdateRecordNode();

        await DrainAsync(Service().ExecuteAsync(RunRequest() with { RunUserId = Person }, Request(whoAmI: null), CancellationToken.None));

        _seen!.StartedBy.Should().Be(PlaybookRunStartedBy.Person);
        _seen.UserId.Should().Be(Person);
    }

    [Fact]
    public async Task AnHttpRun_ThatCarriesNoRunUser_IsResolvedFromTheRequestsOwnToken()
    {
        // analysis execute (useAiSummary), Insights ask, workspace pre-fill / summary, the Console's re-analysis tool and the
        // Compose background profile build their run request without RunUserId.
        OneUpdateRecordNode();

        await DrainAsync(Service().ExecuteAsync(RunRequest(), Request(whoAmI: Person), CancellationToken.None));

        _seen!.StartedBy.Should().Be(PlaybookRunStartedBy.Person);
        _seen.UserId.Should().Be(Person);
    }

    [Fact]
    public async Task AnHttpRun_WhoseUserCannotBeResolved_StaysAPersonsRunWithNoUser()
    {
        OneUpdateRecordNode();

        await DrainAsync(Service().ExecuteAsync(RunRequest(), Request(whoAmI: null), CancellationToken.None));

        _seen!.StartedBy.Should().Be(PlaybookRunStartedBy.Person, "an unresolved person is never turned into a system run");
        _seen.UserId.Should().BeNull();
    }

    [Fact]
    public async Task AnHttpRun_MarkedSystem_IsStillAPersonsRun()
    {
        OneUpdateRecordNode();

        await DrainAsync(Service().ExecuteAsync(
            RunRequest() with { StartedBy = PlaybookRunStartedBy.System }, Request(whoAmI: Person), CancellationToken.None));

        _seen!.StartedBy.Should().Be(PlaybookRunStartedBy.Person);
        _seen.UserId.Should().Be(Person);
    }

    // ── End to end through the real executor: the write runs AS the person, or not at all ────────────────────────────

    [Fact]
    public async Task AnHttpRun_WithoutRunUser_WritesTheRecordAsTheResolvedPerson()
    {
        var writes = new Mock<IFieldMappingDataverseService>();
        OneUpdateRecordNode(RealExecutor(writes));

        await DrainAsync(Service().ExecuteAsync(RunRequest(), Request(whoAmI: Person), CancellationToken.None));

        writes.Verify(w => w.UpdateRecordFieldsAsync(
            "sprk_matter", Matter, It.IsAny<Dictionary<string, object?>>(), It.IsAny<CancellationToken>(), Person), Times.Once);
        writes.Verify(w => w.UpdateRecordFieldsAsync(
            It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<Dictionary<string, object?>>(), It.IsAny<CancellationToken>(), null),
            Times.Never, "a person's run never writes app-only");
    }

    [Fact]
    public async Task AnHttpRun_WhoseUserCannotBeResolved_WritesNothing()
    {
        var writes = new Mock<IFieldMappingDataverseService>();
        OneUpdateRecordNode(RealExecutor(writes));

        await DrainAsync(Service().ExecuteAsync(RunRequest(), Request(whoAmI: null), CancellationToken.None));

        writes.Invocations.Should().BeEmpty();
    }

    [Fact]
    public async Task ASystemRun_WritesTheRecordAppOnly()
    {
        var writes = new Mock<IFieldMappingDataverseService>();
        OneUpdateRecordNode(RealExecutor(writes));
        var request = RunRequest() with
        {
            StartedBy = PlaybookRunStartedBy.System,
            Parameters = new Dictionary<string, string> { ["userId"] = Target.ToString() },
        };

        await DrainAsync(Service().ExecuteAppOnlyAsync(request, "tenant", CancellationToken.None));

        writes.Verify(w => w.UpdateRecordFieldsAsync(
            "sprk_matter", Matter, It.IsAny<Dictionary<string, object?>>(), It.IsAny<CancellationToken>(), null), Times.Once);
    }

    // ── Background entries: they say who started them ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task ASystemRun_TargetsItsUserParameter_ForQueries_ButIsMarkedSystem()
    {
        OneUpdateRecordNode();
        var request = RunRequest() with
        {
            StartedBy = PlaybookRunStartedBy.System,
            Parameters = new Dictionary<string, string> { ["userId"] = Target.ToString() },
        };

        await DrainAsync(Service().ExecuteAppOnlyAsync(request, "tenant", CancellationToken.None));

        _seen!.StartedBy.Should().Be(PlaybookRunStartedBy.System);
        _seen.UserId.Should().Be(Target);
    }

    [Fact]
    public async Task ABackgroundRunThatSaysNothing_IsAPersonsRun_AndAUserParameterNeverBecomesItsIdentity()
    {
        OneUpdateRecordNode();
        var request = RunRequest() with { Parameters = new Dictionary<string, string> { ["userId"] = Target.ToString() } };

        await DrainAsync(Service().ExecuteAppOnlyAsync(request, "tenant", CancellationToken.None));

        _seen!.StartedBy.Should().Be(PlaybookRunStartedBy.Person);
        _seen.UserId.Should().BeNull("only RunUserId is a person's identity; a parameter is caller data");
    }

    [Fact]
    public async Task ABackgroundPersonsRun_ActsForItsRunUser()
    {
        OneUpdateRecordNode();

        await DrainAsync(Service().ExecuteAppOnlyAsync(RunRequest() with { RunUserId = Person }, "tenant", CancellationToken.None));

        _seen!.StartedBy.Should().Be(PlaybookRunStartedBy.Person);
        _seen.UserId.Should().Be(Person);
    }

    // ── Source guard: the System marker is set only by the entries with no person in the run ────────────────────────

    /// <summary>The files allowed to mark a run System, each with why no person is in it.</summary>
    private static readonly IReadOnlyDictionary<string, string> SystemEntries = new Dictionary<string, string>
    {
        ["Services/Ai/PlaybookSchedulerJob.cs"] = "scheduled notification runs",
        ["Services/Ai/AppOnlyAnalysisService.cs"] = "Service Bus document-profile / email-analysis jobs (routed playbook)",
        ["Services/Ai/Insights/InsightsOrchestrator.cs"] = "Service Bus insights-ingest job (routed universal-ingest)",
        ["Services/Jobs/Handlers/InvoiceExtractionJobHandler.cs"] = "Service Bus invoice-extraction job (configured playbook)",
    };

    [Fact]
    public void OnlyTheListedEntries_MarkARunSystem_AndEveryBackgroundRunSaysWhoStartedIt()
    {
        var bff = Path.Combine(RepoRoot(), "src", "server", "api", "Sprk.Bff.Api");
        var markers = new SortedSet<string>(StringComparer.Ordinal);
        var unmarkedBackgroundCallers = new List<string>();
        foreach (var file in Directory.EnumerateFiles(bff, "*.cs", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(bff, file).Replace('\\', '/');
            if (relative.StartsWith("obj/", StringComparison.Ordinal) || relative.StartsWith("bin/", StringComparison.Ordinal))
                continue;

            var code = StripComments(File.ReadAllText(file));
            if (code.Contains("PlaybookRunStartedBy.System") && relative != "Services/Ai/IPlaybookOrchestrationService.cs"
                && relative != "Services/Ai/PlaybookRunContext.cs" && relative != "Services/Ai/PlaybookOrchestrationService.cs")
            {
                markers.Add(relative);
            }

            if (Regex.IsMatch(code, @"\.ExecuteAppOnlyAsync\(") && !code.Contains("StartedBy = PlaybookRunStartedBy."))
            {
                unmarkedBackgroundCallers.Add(relative);
            }
        }

        markers.Should().BeEquivalentTo(SystemEntries.Keys,
            "a run writes app-only only when no person is in it; a new System entry is added here with its reason (#2257)");
        unmarkedBackgroundCallers.Should().BeEmpty("every background run says who started it");
    }

    [Theory]
    [InlineData("var r = new PlaybookRunRequest { StartedBy = PlaybookRunStartedBy.System };", true)]
    [InlineData("// StartedBy = PlaybookRunStartedBy.System", false)]
    [InlineData("/* PlaybookRunStartedBy.System */ var x = 1;", false)]
    public void TheSourceGuard_SeesAMarkerInCode_AndNotInAComment(string code, bool seen) =>
        StripComments(code).Contains("PlaybookRunStartedBy.System").Should().Be(seen);

    private static string StripComments(string code) =>
        Regex.Replace(code, @"/\*.*?\*/|//[^\n]*", string.Empty, RegexOptions.Singleline);

    private static UpdateRecordNodeExecutor RealExecutor(Mock<IFieldMappingDataverseService> writes) =>
        new(new TemplateEngine(NullLogger<TemplateEngine>.Instance), writes.Object,
            new Mock<IServiceScopeFactory>(MockBehavior.Strict).Object, NullLogger<UpdateRecordNodeExecutor>.Instance);

    private static string RepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null)
        {
            var git = Path.Combine(dir, ".git");
            if (Directory.Exists(git) || File.Exists(git))
                return dir;
            dir = Directory.GetParent(dir)?.FullName;
        }

        throw new InvalidOperationException("Repository root (.git) not found above " + AppContext.BaseDirectory);
    }

    /// <summary>WhoAmI over the caller's token answers a fixed systemuserid (or nothing).</summary>
    private sealed class WhoAmIProbe(Guid? whoAmI)
        : CallerRecordAccessProbe(new HttpClient(), new ConfigurationBuilder().Build(), NullLogger<CallerRecordAccessProbe>.Instance)
    {
        public override Task<Guid?> GetCallerSystemUserIdAsync(string? callerBearerToken, CancellationToken ct = default) =>
            Task.FromResult(callerBearerToken is null ? null : whoAmI);
    }
}
