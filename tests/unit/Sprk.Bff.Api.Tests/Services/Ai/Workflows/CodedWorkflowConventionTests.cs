// FR-P0-06 (spaarke-ai-architecture-redesign-r1 task 007) — ICodedWorkflow convention tests.
//
// ADR-038 shape: these are NOT DI-registration tests (banned B3). Each test proves the
// behavior an executor (FR-P3-04's coded-composite dispatch) would observe: a workflow
// named by an Action row's sprk_workflowclass value is resolved by class reference through
// the assembly-scan convention and EXECUTED — real narrator pipeline (mocked at the
// IOpenAiClient / AnalysisActionService module boundary, same recipe as
// DailyBriefingNarratorEntityLinkTests), real collector pipeline (failure-soft empty
// channels), and the ADR-032 Null-Object kill-switch peer flowing through class-ref
// resolution unchanged.
//
// R5 task 010 (FR-A3, 2026-07-08): the narrator's per-channel LLM leg was removed —
// channel content is now built deterministically from req.Channels, so the module-boundary
// mock below only stubs the TL;DR action (the sole remaining LLM call).

using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.Ai;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Membership;
using Sprk.Bff.Api.Services.Ai.Narrators;
using Sprk.Bff.Api.Services.Ai.Nodes;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai.Workflows;

[Trait("status", "task-007-ai-redesign-r1")]
public sealed class CodedWorkflowConventionTests
{
    private const string TldrActionCode = "BRIEF-NARRATE-TLDR";
    private const string TldrSummary = "One matter needs attention today.";

    // ─── Test 1: narrator resolved by class ref through the convention and executed ───

    [Fact]
    public async Task ExecuteAsync_NarratorResolvedByClassRefThroughConvention_RunsWorkflowEndToEnd()
    {
        // Arrange — DI graph with the narrator's module-boundary deps mocked; NO explicit
        // DailyBriefingNarrator registration: construction comes from the scan's backstop,
        // proving the convention alone makes the workflow resolvable + executable.
        var provider = BuildProviderWithScan();
        using var scope = provider.CreateScope();
        var registry = scope.ServiceProvider.GetRequiredService<ICodedWorkflowRegistry>();

        // Act — resolve by the value an Action row's sprk_workflowclass would carry.
        var workflow = registry.GetWorkflow(nameof(DailyBriefingNarrator));

        // Assert — resolution.
        workflow.Should().NotBeNull(
            "a coded-kind Action row naming 'DailyBriefingNarrator' must resolve through the convention");
        workflow!.WorkflowClassRef.Should().Be(nameof(DailyBriefingNarrator));

        // Act — execute through the convention entry point with the Action-args payload.
        var argsJson = JsonSerializer.Serialize(MakeNarrateRequest());
        var result = await workflow.ExecuteAsync(
            new CodedWorkflowContext { ArgumentsJson = argsJson },
            CancellationToken.None);

        // Assert — the real narrator pipeline ran (TLDR from the mocked LLM boundary).
        var response = result.Should().BeOfType<DailyBriefingNarrateResponse>().Subject;
        response.Tldr.Summary.Should().Be(TldrSummary);
        response.ChannelNarratives.Should().HaveCount(1,
            "the narrator fans out one narrative per input channel");

        // Unknown class refs resolve to null (executor surfaces a catalog error, not a crash).
        registry.GetWorkflow("NoSuchWorkflow").Should().BeNull();
    }

    // ─── Test 2: collector resolved by class ref; user-scoped execution contract ───

    [Fact]
    public async Task ExecuteAsync_CollectorResolvedByClassRef_ExecutesUserScopedCollection()
    {
        var provider = BuildProviderWithScan();
        using var scope = provider.CreateScope();
        var registry = scope.ServiceProvider.GetRequiredService<ICodedWorkflowRegistry>();

        var workflow = registry.GetWorkflow(nameof(DailyBriefingCollector));
        workflow.Should().NotBeNull();

        // Without an acting user the collector refuses — it queries on the user's behalf.
        var missingUser = () => workflow!.ExecuteAsync(new CodedWorkflowContext(), CancellationToken.None);
        await missingUser.Should().ThrowAsync<ArgumentException>();

        // With an acting user the real collection pipeline runs; the loose Dataverse mock
        // yields the failure-soft empty briefing (per-channel degradation contract).
        var result = await workflow!.ExecuteAsync(
            new CodedWorkflowContext { UserId = Guid.NewGuid() },
            CancellationToken.None);

        var request = result.Should().BeOfType<DailyBriefingNarrateRequest>().Subject;
        request.TotalNotificationCount.Should().Be(0);
        request.Channels.Should().BeEmpty("empty channels are filtered from the payload");
    }

    // ─── Test 3: ADR-032 kill-switch Null peer flows through class-ref resolution ───

    [Fact]
    public async Task ExecuteAsync_WhenCompoundGateOffNullPeerRegistered_ThrowsFeatureDisabledThroughConvention()
    {
        // Arrange — mirror AnalysisServicesModule's compound-OFF branch: the concrete types
        // are registered as their Null-Object peers BEFORE the scan runs; the scan's
        // deferring factories must surface the Null peers under the SAME class refs.
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddTransient<DailyBriefingNarrator>(sp =>
            new NullDailyBriefingNarrator(sp.GetRequiredService<ILogger<DailyBriefingNarrator>>()));
        services.AddScoped<DailyBriefingCollector>(sp =>
            new NullDailyBriefingCollector(sp.GetRequiredService<ILogger<DailyBriefingCollector>>()));
        services.AddCodedWorkflowsFromAssembly(typeof(DailyBriefingNarrator).Assembly);

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var registry = scope.ServiceProvider.GetRequiredService<ICodedWorkflowRegistry>();

        // Null peers keep the base's workflow identity — no duplicate/extra registrations.
        var narrator = registry.GetWorkflow(nameof(DailyBriefingNarrator));
        narrator.Should().NotBeNull(
            "class-ref resolution must keep working when the gate is OFF (kill-switch contract)");

        // Act + Assert — executing through the convention surfaces the ADR-032 contract.
        var argsJson = JsonSerializer.Serialize(MakeNarrateRequest());
        var act = () => narrator!.ExecuteAsync(
            new CodedWorkflowContext { ArgumentsJson = argsJson }, CancellationToken.None);
        await act.Should().ThrowAsync<FeatureDisabledException>(
            "the Null-Object peer's fail-fast behavior must flow through class-ref invocation");
    }

    // ─── Arrange helpers ───

    /// <summary>
    /// Builds a DI provider containing the narrator/collector module-boundary mocks and the
    /// assembly scan — but NO explicit workflow registrations (the convention's backstop
    /// constructs them). Mock recipe mirrors DailyBriefingNarratorTldrChainingTests.
    /// </summary>
    private static ServiceProvider BuildProviderWithScan()
    {
        var actions = new Mock<AnalysisActionService>(MockBehavior.Loose,
            new HttpClient { BaseAddress = new Uri("https://example.crm.dynamics.com/api/data/v9.2/") },
            BuildTestConfiguration(),
            new TestNoopTokenCredential(),
            NullLogger<AnalysisActionService>.Instance);
        actions.Setup(s => s.GetActionByCodeAsync(TldrActionCode, It.IsAny<CancellationToken>()))
               .ReturnsAsync(MakeAction(TldrActionCode,
                   """{ "type":"object", "properties": { "summary":{"type":"string"}, "keyTakeaways":{"type":"array","items":{"type":"string"}}, "topAction":{"type":"string"} }, "required":["summary","keyTakeaways","topAction"], "additionalProperties":false }"""));

        // Strict — the TL;DR action is the ONLY LLM call the narrator makes post-R5 task
        // 010; any other invocation (e.g. a resurrected per-channel call) throws and fails
        // the test.
        var llm = new Mock<IOpenAiClient>(MockBehavior.Strict);
        llm.Setup(c => c.GetStructuredCompletionRawAsync(
                It.IsAny<string>(), It.IsAny<BinaryData>(), TldrActionCode.Replace('-', '_'),
                It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<float?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(JsonSerializer.Serialize(new
            {
                summary = TldrSummary,
                keyTakeaways = new[] { "Acme contract overdue" },
                topAction = "Review the Acme engagement letter."
            }));

        var scrubber = new Mock<IEntityNameScrubber>(MockBehavior.Loose);
        scrubber.Setup(s => s.Scrub(It.IsAny<string>(), It.IsAny<IReadOnlyList<string>>()))
                .Returns(new EntityNameScrubResult { ScrubbedText = "x", RemovedTerms = Array.Empty<string>() });

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(actions.Object);
        services.AddSingleton(llm.Object);
        services.AddSingleton(scrubber.Object);
        // Collector deps — loose mocks: every Dataverse query degrades failure-soft to empty.
        services.AddSingleton(Mock.Of<IGenericEntityService>());
        services.AddSingleton(Mock.Of<IMembershipResolverService>());

        services.AddCodedWorkflowsFromAssembly(typeof(DailyBriefingNarrator).Assembly);
        return services.BuildServiceProvider();
    }

    private static AnalysisAction MakeAction(string code, string outputSchemaJson) => new()
    {
        Id = Guid.NewGuid(),
        Name = code,
        SystemPrompt = $"You are the {code} action.",
        OutputSchemaJson = outputSchemaJson,
        SortOrder = 0,
        ExecutorType = ExecutorType.AiAnalysis,
        OwnerType = ScopeOwnerType.System,
        Temperature = 0.0m
    };

    private static DailyBriefingNarrateRequest MakeNarrateRequest() => new()
    {
        Categories = [new NotificationCategoryDto { Name = "Tasks", Count = 1, UnreadCount = 1 }],
        PriorityItems = [new PriorityItemDto { Category = "Tasks", Title = "Review engagement letter" }],
        TotalNotificationCount = 1,
        Channels =
        [
            new ChannelNarrationInput
            {
                Category = "tasks",
                Label = "Tasks",
                Items =
                [
                    new ChannelItemDto
                    {
                        Id = "item-1",
                        Title = "Acme contract renewal",
                        RegardingName = "Acme Corp",
                        RegardingEntityType = "sprk_matter",
                        RegardingId = Guid.NewGuid().ToString()
                    }
                ]
            }
        ]
    };

    private static IConfiguration BuildTestConfiguration() =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Dataverse:ServiceUrl"] = "https://example.crm.dynamics.com/api/data/v9.2/"
            })
            .Build();

    private sealed class TestNoopTokenCredential : Azure.Core.TokenCredential
    {
        public override Azure.Core.AccessToken GetToken(Azure.Core.TokenRequestContext requestContext, CancellationToken cancellationToken)
            => new("test-token", DateTimeOffset.UtcNow.AddHours(1));

        public override ValueTask<Azure.Core.AccessToken> GetTokenAsync(Azure.Core.TokenRequestContext requestContext, CancellationToken cancellationToken)
            => new(new Azure.Core.AccessToken("test-token", DateTimeOffset.UtcNow.AddHours(1)));
    }
}
