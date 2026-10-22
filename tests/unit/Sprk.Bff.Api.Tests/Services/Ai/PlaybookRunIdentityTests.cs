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
    private static HttpContext Request(Guid? whoAmI, bool probeFaults = false)
    {
        var services = new ServiceCollection();
        services.AddSingleton<CallerRecordAccessProbe>(new WhoAmIProbe(whoAmI, probeFaults));
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
    public async Task AnHttpRun_WhoseWhoAmIProbeThrows_StillCompletesAReadOnlyRun()
    {
        // Fix round 1 (pass 2 K2): the central resolution is new on the entries that carried no user (analysis execute,
        // Insights ask, workspace pre-fill and summary, the Console's re-analysis tool). A WhoAmI fault there must not fail a
        // run that writes nothing: the run goes on as a person's run with no user, and only a writing node refuses.
        OneUpdateRecordNode(); // the capturing double writes nothing

        var events = new List<PlaybookStreamEvent>();
        await foreach (var e in Service().ExecuteAsync(RunRequest(), Request(whoAmI: Person, probeFaults: true), CancellationToken.None))
        {
            events.Add(e);
        }

        events.Select(e => e.Type).Should().Contain(PlaybookEventType.RunCompleted)
            .And.NotContain(PlaybookEventType.RunFailed)
            .And.NotContain(PlaybookEventType.NodeFailed);
        _seen!.StartedBy.Should().Be(PlaybookRunStartedBy.Person);
        _seen.UserId.Should().BeNull("a faulted WhoAmI is an unresolved user, never app-only");
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
    // Fix round 1 (K4): per call site, not per file. Every place that SETS the marker is one of the four listed entries, each
    // exactly once; nothing casts, parses or reflects a value into the enum; and no marker value travels through a variable
    // the guard cannot see (a local, field or parameter of the enum type, or a StartedBy assigned anything but a literal
    // marker or one of the listed copies of an already-decided marker).

    /// <summary>The files allowed to mark a run System — exactly one call site each — with why no person is in it.</summary>
    private static readonly IReadOnlyDictionary<string, string> SystemEntries = new Dictionary<string, string>
    {
        ["Services/Ai/PlaybookSchedulerJob.cs"] = "scheduled notification runs",
        ["Services/Ai/AppOnlyAnalysisService.cs"] = "Service Bus document-profile / email-analysis jobs (routed playbook)",
        ["Services/Ai/Insights/InsightsOrchestrator.cs"] = "Service Bus insights-ingest job (routed universal-ingest)",
        ["Services/Jobs/Handlers/InvoiceExtractionJobHandler.cs"] = "Service Bus invoice-extraction job (configured playbook)",
    };

    /// <summary>The enum and THE rule (<see cref="PlaybookRunWriteIdentity"/>): the only file that declares a parameter of the type.</summary>
    private const string MarkerDefinitionFile = "Services/Ai/IPlaybookOrchestrationService.cs";

    /// <summary>
    /// The only non-literal values a StartedBy may be given: copies of a marker an earlier step already decided (the
    /// background request onto its run context; the run context onto each node's context).
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> MarkerCopies = new Dictionary<string, string>
    {
        ["Services/Ai/PlaybookOrchestrationService.cs"] = "request.StartedBy",
        ["Services/Ai/PlaybookRunContext.cs"] = "StartedBy",
    };

    private static readonly Regex MarkerCarrier = new(
        @"\b(?:PlaybookRunStartedBy|PlaybookRunRequest|PlaybookRunContext|NodeExecutionContext|PlaybookExecutionContext)\b");

    private static readonly Regex SystemMarker = new(@"\bPlaybookRunStartedBy\s*\.\s*System\b");

    private static readonly Regex ComparisonBefore = new(@"(?:==|!=|\bis(?:\s+not)?|\bcase|\bor|\band)\s*$");

    /// <summary>Fix round 2 (K2): a marker on the LEFT of a comparison, or a switch arm's pattern, is a read.</summary>
    private static readonly Regex ComparisonAfter = new(@"\G\s*(?:==|!=|=>)");

    /// <summary>
    /// Fix round 2 (K3): a carrier built from JSON (System.Text.Json, Newtonsoft, HttpClient / HttpRequest JSON helpers, or a
    /// <c>typeof</c> handed to one of them) can carry a System marker the scan cannot see.
    /// </summary>
    private static readonly Regex CarrierFromJson = new(
        @"\b(?:Deserialize\w*|ReadFromJsonAsync|GetFromJsonAsync|ToObject|PopulateObject)\s*<\s*(?:global::)?(?:\w+\s*\.\s*)*"
        + @"(?:PlaybookRunRequest|PlaybookRunContext|NodeExecutionContext|PlaybookExecutionContext)\s*\??\s*>"
        + @"|\b(?:Deserialize\w*|ReadFromJsonAsync|ToObject|PopulateObject)\s*\([^;]*\btypeof\s*\(\s*(?:global::)?(?:\w+\s*\.\s*)*"
        + @"(?:PlaybookRunRequest|PlaybookRunContext|NodeExecutionContext|PlaybookExecutionContext)\s*\)");

    private static readonly Regex IntoTheEnum = new(
        @"\(\s*(?:global::)?(?:\w+\s*\.\s*)*PlaybookRunStartedBy\s*\??\s*\)"   // a cast
        + @"|\bas\s+(?:\w+\.)*PlaybookRunStartedBy\b"                              // an as-conversion
        + @"|[<,]\s*(?:\w+\.)*PlaybookRunStartedBy\s*\??\s*[>,]"                     // a generic argument: Enum.Parse<>, GetValues<>, Unsafe.As<>
        + @"|\btypeof\s*\(\s*(?:\w+\.)*PlaybookRunStartedBy\s*\)"                 // reflection: Enum.Parse(typeof(...)), ToObject
        + @"|(?m:^\s*using\s+(?!var\b)[^;(]*\bPlaybookRunStartedBy\s*;)");           // a using static / alias directive

    private static readonly Regex MarkerVariable = new(@"\bPlaybookRunStartedBy\s*\??\s+[A-Za-z_]\w*\s*(?:[=;,)]|=>)");

    private static readonly Regex StartedByAssignment = new(@"\bStartedBy\s*=(?![=>])\s*([^,;}\r\n]+)");

    /// <summary>
    /// The problems in one source file (<paramref name="relative"/> to the BFF project): every System call site outside the
    /// allow-list, a listed entry that does not set it exactly once, a conversion into the enum, a marker held in a
    /// variable, and a StartedBy given anything but a literal marker or a listed copy.
    /// </summary>
    private static List<string> MarkerProblems(string relative, string source)
    {
        var problems = new List<string>();
        var code = StripCommentsAndStrings(source);
        if (!MarkerCarrier.IsMatch(code))
        {
            return problems;
        }

        var setSites = SystemMarker.Matches(code)
            .Count(m => !ComparisonBefore.IsMatch(code[..m.Index]) && !ComparisonAfter.IsMatch(code, m.Index + m.Length));
        if (SystemEntries.ContainsKey(relative))
        {
            if (setSites != 1)
                problems.Add($"{relative}: a listed System entry must set the marker at exactly one call site; it sets it at {setSites}");
        }
        else if (setSites > 0)
        {
            problems.Add($"{relative}: sets PlaybookRunStartedBy.System at {setSites} call site(s) but is not a listed System entry");
        }

        foreach (System.Text.RegularExpressions.Match fromJson in CarrierFromJson.Matches(code))
            problems.Add($"{relative}: builds a run carrier from JSON ('{fromJson.Value.Trim()}'), which can carry any marker");

        foreach (System.Text.RegularExpressions.Match conversion in IntoTheEnum.Matches(code))
            problems.Add($"{relative}: converts a value into PlaybookRunStartedBy ('{conversion.Value.Trim()}')");

        if (relative != MarkerDefinitionFile)
        {
            foreach (System.Text.RegularExpressions.Match variable in MarkerVariable.Matches(code))
                problems.Add($"{relative}: holds a PlaybookRunStartedBy in a variable ('{variable.Value.Trim()}')");
        }

        foreach (System.Text.RegularExpressions.Match assignment in StartedByAssignment.Matches(code))
        {
            var value = Regex.Replace(assignment.Groups[1].Value, @"\s+", string.Empty);
            var allowed = value is "PlaybookRunStartedBy.Person" or "PlaybookRunStartedBy.System"
                || (MarkerCopies.TryGetValue(relative, out var copy) && value == copy);
            if (!allowed)
                problems.Add($"{relative}: StartedBy is given '{assignment.Groups[1].Value.Trim()}', not a literal marker or a listed copy");
        }

        return problems;
    }

    [Fact]
    public void OnlyTheListedEntries_MarkARunSystem_AtOneCallSiteEach_AndEveryBackgroundRunSaysWhoStartedIt()
    {
        var bff = Path.Combine(RepoRoot(), "src", "server", "api", "Sprk.Bff.Api");
        var problems = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var unmarkedBackgroundCallers = new List<string>();
        foreach (var file in Directory.EnumerateFiles(bff, "*.cs", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(bff, file).Replace('\\', '/');
            if (relative.StartsWith("obj/", StringComparison.Ordinal) || relative.StartsWith("bin/", StringComparison.Ordinal))
                continue;

            seen.Add(relative);
            var source = File.ReadAllText(file);
            problems.AddRange(MarkerProblems(relative, source));

            var code = StripCommentsAndStrings(source);
            if (Regex.IsMatch(code, @"\.ExecuteAppOnlyAsync\(") && !code.Contains("StartedBy = PlaybookRunStartedBy."))
            {
                unmarkedBackgroundCallers.Add(relative);
            }
        }

        problems.AddRange(SystemEntries.Keys.Concat(MarkerCopies.Keys).Append(MarkerDefinitionFile)
            .Where(listed => !seen.Contains(listed))
            .Select(listed => $"{listed}: listed but no such file exists"));

        problems.Should().BeEmpty(
            "a run writes app-only only when no person is in it; a new System entry is added here with its reason (#2257)");
        unmarkedBackgroundCallers.Should().BeEmpty("every background run says who started it");
    }

    public static TheoryData<string, string, bool> GuardFixtures() => new()
    {
        // Must fire.
        { "Services/Ai/Foo.cs", "var r = new PlaybookRunRequest { StartedBy = PlaybookRunStartedBy.System };", true },
        { "Services/Ai/Foo.cs", "var r = new PlaybookRunRequest { StartedBy = (PlaybookRunStartedBy)1 };", true },
        { "Services/Ai/Foo.cs", "var r = new PlaybookRunRequest { StartedBy = (Sprk.Bff.Api.Services.Ai.PlaybookRunStartedBy) 1 };", true },
        { "Services/Ai/PlaybookSchedulerJob.cs",
            "var a = new PlaybookRunRequest { StartedBy = PlaybookRunStartedBy.System };\n"
            + "var b = new PlaybookRunRequest { StartedBy = PlaybookRunStartedBy.System };", true },
        { "Services/Ai/PlaybookSchedulerJob.cs",
            "var a = new PlaybookRunRequest { StartedBy = PlaybookRunStartedBy.System };\n"
            + "var b = a with { StartedBy = (PlaybookRunStartedBy)1 };", true },
        { "Services/Ai/PlaybookSchedulerJob.cs", "var a = new PlaybookRunRequest();", true },
        { "Services/Ai/Foo.cs", "var m = request.StartedBy; var r = new PlaybookRunRequest { StartedBy = m };", true },
        { "Services/Ai/Foo.cs", "PlaybookRunStartedBy m = default; var r = new PlaybookRunRequest();", true },
        { "Services/Ai/Foo.cs", "var m = PlaybookRunStartedBy.System; var r = new PlaybookRunRequest();", true },
        { "Services/Ai/Foo.cs", "var r = new PlaybookRunRequest { StartedBy = Enum.Parse<PlaybookRunStartedBy>(\"System\") };", true },
        { "Services/Ai/Foo.cs", "var r = new PlaybookRunRequest { StartedBy = cond ? PlaybookRunStartedBy.Person : PlaybookRunStartedBy.Person };", true },
        { "Services/Ai/Foo.cs", "using static Sprk.Bff.Api.Services.Ai.PlaybookRunStartedBy;\nvar r = new PlaybookRunRequest();", true },
        { "Services/Ai/Foo.cs", "using Marker = Sprk.Bff.Api.Services.Ai.PlaybookRunStartedBy;\nvar r = new PlaybookRunRequest();", true },
        { "Services/Ai/PlaybookRunContext.cs", "var c = new NodeExecutionContext { StartedBy = request.StartedBy };", true },
        // Fix round 2, K1: a "//" inside a string on the same line no longer hides the marker after it.
        { "Services/Ai/Foo.cs", "var u = \"https://x\"; var r = new PlaybookRunRequest { StartedBy = PlaybookRunStartedBy.System };", true },
        { "Services/Ai/Foo.cs", "var u = @\"C:\\x\\\"; var r = new PlaybookRunRequest { StartedBy = PlaybookRunStartedBy.System };", true },
        { "Services/Ai/Foo.cs", "var u = $\"{a}//{b}\"; var r = new PlaybookRunRequest { StartedBy = PlaybookRunStartedBy.System };", true },
        { "Services/Ai/Foo.cs", "var c = '\"'; var r = new PlaybookRunRequest { StartedBy = PlaybookRunStartedBy.System };", true },
        // Fix round 2, K3: a carrier built from JSON.
        { "Services/Ai/Foo.cs", "var r = JsonSerializer.Deserialize<PlaybookRunRequest>(json);", true },
        { "Services/Ai/Foo.cs", "var r = JsonConvert.DeserializeObject<Sprk.Bff.Api.Services.Ai.NodeExecutionContext>(json);", true },
        { "Services/Ai/Foo.cs", "var r = await body.ReadFromJsonAsync<PlaybookExecutionContext>(ct);", true },
        { "Services/Ai/Foo.cs", "var r = (PlaybookRunContext)JsonSerializer.Deserialize(json, typeof(PlaybookRunContext))!;", true },
        // Must not fire.
        { "Services/Ai/PlaybookSchedulerJob.cs", "var a = new PlaybookRunRequest { StartedBy = PlaybookRunStartedBy.System };", false },
        { "Services/Ai/Foo.cs", "if (request.StartedBy == PlaybookRunStartedBy.System) { } var r = new PlaybookRunRequest();", false },
        { "Services/Ai/Foo.cs", "if (request.StartedBy is not PlaybookRunStartedBy.System) { } var r = new PlaybookRunRequest();", false },
        { "Services/Ai/Foo.cs", "// StartedBy = PlaybookRunStartedBy.System\nvar r = new PlaybookRunRequest();", false },
        { "Services/Ai/Foo.cs", "/* PlaybookRunStartedBy.System */ var r = new PlaybookRunRequest();", false },
        { "Services/Ai/Foo.cs", "var r = new PlaybookRunRequest { StartedBy = PlaybookRunStartedBy.Person };", false },
        { "Services/Ai/Foo.cs", "_log.Info(\"StartedBy={StartedBy}\", PlaybookRunRequest.X);", false },
        { "Services/Ai/PlaybookOrchestrationService.cs", "var c = new PlaybookRunContext { StartedBy = request.StartedBy };", false },
        { MarkerDefinitionFile, "public static bool TryResolve(PlaybookRunStartedBy startedBy, Guid? id) => startedBy == PlaybookRunStartedBy.System;", false },
        { "Services/SpeAdmin/BulkOperationService.cs", "var op = new BulkOperation { StartedBy = startedBy };", false },
        { "Services/Ai/Foo.cs", "using (var scope = Open()) { var r = new PlaybookRunRequest { StartedBy = PlaybookRunStartedBy.Person }; }", false },
        // Fix round 2, K2: reads are not call sites.
        { "Services/Ai/Foo.cs", "if (PlaybookRunStartedBy.System == request.StartedBy) { } var r = new PlaybookRunRequest();", false },
        { "Services/Ai/Foo.cs", "if (PlaybookRunStartedBy.System != request.StartedBy) { } var r = new PlaybookRunRequest();", false },
        { "Services/Ai/Foo.cs", "var label = request.StartedBy switch { PlaybookRunStartedBy.System => \"system\", _ => \"person\" }; var r = new PlaybookRunRequest();", false },
        // Fix round 2, K1: a marker inside a string, a verbatim string or a raw string is not code.
        { "Services/Ai/Foo.cs", "var s = @\"StartedBy = PlaybookRunStartedBy.System\"\"\"; var r = new PlaybookRunRequest();", false },
        { "Services/Ai/Foo.cs", "var s = \"\"\"\n  StartedBy = PlaybookRunStartedBy.System\n  \"\"\"; var r = new PlaybookRunRequest();", false },
        { "Services/Ai/Foo.cs", "var s = \"a // StartedBy = PlaybookRunStartedBy.System\"; var r = new PlaybookRunRequest();", false },
    };

    [Theory]
    [MemberData(nameof(GuardFixtures))]
    public void TheSourceGuard_FiresPerCallSite(string relative, string code, bool fires) =>
        MarkerProblems(relative, code).Any().Should().Be(fires, code);

    /// <summary>
    /// Fix round 2 (K1): ONE left-to-right pass that drops comments and empties string and char literals, so the <c>//</c>
    /// inside <c>"https://x"</c> never starts a comment (which ate the rest of its line, and any marker on it), and a quote in
    /// a comment never starts a string. Raw (<c>"""</c>), verbatim (<c>@""</c>, <c>$@""</c>, <c>@$""</c>, with <c>""</c>
    /// escapes) and regular or interpolated strings are emptied, so a log template such as "StartedBy={StartedBy}" is not
    /// read as code. Limit: an interpolation hole holding a quoted string ends the outer string early (as before).
    /// </summary>
    private static readonly Regex StringOrComment = new(
        """"//[^\n]*|/\*[\s\S]*?\*/|"{3}[\s\S]*?"{3}|(?:\$@|@\$|@)"(?:[^"]|"")*"|"(?:[^"\\\n]|\\.)*"|'(?:[^'\\\n]|\\.)'"""");

    private static string StripCommentsAndStrings(string code) =>
        StringOrComment.Replace(code, m => m.Value[0] switch
        {
            '/' => " ",
            '\'' => "' '",
            _ => "\"\"",
        });

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

    /// <summary>WhoAmI over the caller's token answers a fixed systemuserid (or nothing), or faults.</summary>
    private sealed class WhoAmIProbe(Guid? whoAmI, bool faults = false)
        : CallerRecordAccessProbe(new HttpClient(), new ConfigurationBuilder().Build(), NullLogger<CallerRecordAccessProbe>.Instance)
    {
        public override Task<Guid?> GetCallerSystemUserIdAsync(string? callerBearerToken, CancellationToken ct = default) =>
            faults
                ? throw new HttpRequestException("WhoAmI faulted")
                : Task.FromResult(callerBearerToken is null ? null : whoAmI);
    }
}
