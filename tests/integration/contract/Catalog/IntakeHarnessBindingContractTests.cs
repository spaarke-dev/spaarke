using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Ai.PublicContracts;
using Sprk.Bff.Api.Services.Intake.Harnesses;
using Xunit;

namespace Sprk.Bff.Api.Tests.Contract.Catalog;

/// <summary>
/// KEEP-path contract test for the committed <c>intake-harness</c> Binding row
/// (<c>infra/dataverse/sprk_playbookconsumer-rows.json</c>; spaarke-intake-pipeline-r1 task 012, FR-08a).
/// It feeds the row exactly as committed through the real <see cref="ConsumerRoutingService"/> and
/// <see cref="IntakeHarnessRegistry"/>, so an edit to the row (consumer code, match conditions, a dropped
/// target) that would stop <c>nda</c> requests reaching the <c>nda</c> harness fails here, not in a live run.
/// </summary>
/// <remarks>
/// <b>Component Justification (CLAUDE.md §11)</b>: (1) <i>Existing</i> — <c>IntakeHarnessRegistryTests</c>
/// covers the registry over hand-built rows; nothing reads the committed row. (2) <i>Extension</i> — one
/// narrowly-scoped class in the Catalog contract folder, beside the other Binding-row contract tests.
/// (3) <i>Cost of doing nothing</i> — a row edit that changes the harness key or the requestType condition
/// silently sends every NDA request to manual triage.
/// </remarks>
public class IntakeHarnessBindingContractTests
{
    [Fact]
    public void ConsumerTypesKey_IsListedInAll_SoTheHealthCheckSeesTheRow()
    {
        ConsumerTypes.IntakeHarness.Should().Be("intake-harness");
        ConsumerTypes.All.Should().Contain(ConsumerTypes.IntakeHarness,
            "RoutingConsumerTypeHealthCheck reports a Binding row whose consumer type is not in All as Unhealthy");
    }

    [Fact]
    public async Task CommittedNdaRow_RoutesRequestTypeNda_ToTheHarnessRegisteredUnderKeyNda()
    {
        var nda = new KeyedHarness("nda");
        var sut = CreateRegistry(LoadIntakeHarnessRows(), nda);

        var result = await sut.ResolveAsync("nda", documentType: "nda");

        result.Outcome.Should().Be(IntakeHarnessResolutionOutcome.Resolved);
        result.Harness.Should().BeSameAs(nda);
    }

    [Fact]
    public async Task CommittedRows_RouteAnUncataloguedRequestType_ToNoHarness()
    {
        var sut = CreateRegistry(LoadIntakeHarnessRows(), new KeyedHarness("nda"));

        var result = await sut.ResolveAsync("invention", documentType: null);

        result.HasHarness.Should().BeFalse("a request type with no Binding goes to manual triage (FR-10)");
        result.Outcome.Should().Be(IntakeHarnessResolutionOutcome.NoBinding);
    }

    [Fact]
    public void CommittedNdaRow_IsNotProjectableOrDispatchableByTheAssistant()
    {
        var row = LoadMirrorRows().Single(r =>
            r.GetProperty("consumerType").GetString() == ConsumerTypes.IntakeHarness
            && r.GetProperty("consumerCode").GetString() == "nda");

        foreach (var column in new[] { "toolDescription", "surfaces", "chipTransitions", "onEventBindings" })
        {
            row.GetProperty(column).ValueKind.Should().Be(JsonValueKind.Null,
                $"'{column}' would expose harness selection to the chat loop or a chip; it is routing data only");
        }

        File.Exists(Path.Combine(FindRepoRoot(), "infra", "dataverse", "actions",
                $"{row.GetProperty("actionCode").GetString()}.action.json"))
            .Should().BeTrue("the target Action must be catalogued, or the seed cannot bind the row and routing skips it");
    }

    // ── Plumbing ───────────────────────────────────────────────────────────────────────────────────

    private static IntakeHarnessRegistry CreateRegistry(Entity[] rows, params IIntakeHarness[] harnesses)
    {
        var dataverse = new Mock<IGenericEntityService>();
        dataverse
            .Setup(s => s.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EntityCollection(rows.ToList()));
        var environment = new Mock<IHostEnvironment>();
        environment.SetupGet(e => e.EnvironmentName).Returns("dev");

        var routing = new ConsumerRoutingService(
            dataverse.Object, new MemoryCache(new MemoryCacheOptions()), environment.Object,
            NullLogger<ConsumerRoutingService>.Instance);
        return new IntakeHarnessRegistry(harnesses, routing, NullLogger<IntakeHarnessRegistry>.Instance);
    }

    /// <summary>The mirror's intake-harness rows as Dataverse returns them (lookups resolve per environment, so any id).</summary>
    private static Entity[] LoadIntakeHarnessRows() =>
        LoadMirrorRows()
            .Where(r => r.GetProperty("consumerType").GetString() == ConsumerTypes.IntakeHarness)
            .Select(r =>
            {
                var entity = new Entity("sprk_playbookconsumer");
                entity["sprk_playbookconsumerid"] = Guid.NewGuid();
                entity["sprk_consumertype"] = r.GetProperty("consumerType").GetString();
                entity["sprk_consumercode"] = r.GetProperty("consumerCode").GetString();
                entity["sprk_environment"] = r.GetProperty("environment").GetString();
                entity["sprk_priority"] = r.GetProperty("priority").GetInt32();
                entity["sprk_enabled"] = r.GetProperty("enabled").GetBoolean();
                entity["sprk_matchconditions"] = r.GetProperty("matchConditions").GetString();
                if (r.GetProperty("actionCode").GetString() is not null)
                {
                    entity["sprk_action"] = new EntityReference("sprk_analysisaction", Guid.NewGuid());
                }
                if (r.GetProperty("playbookName").GetString() is not null)
                {
                    entity["sprk_playbook"] = new EntityReference("sprk_analysisplaybook", Guid.NewGuid());
                }
                return entity;
            })
            .ToArray();

    private static JsonElement[] LoadMirrorRows()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(
            Path.Combine(FindRepoRoot(), "infra", "dataverse", "sprk_playbookconsumer-rows.json")));
        return doc.RootElement.GetProperty("rows").EnumerateArray().Select(r => r.Clone()).ToArray();
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Spaarke.sln")))
                return dir.FullName;
            dir = dir.Parent;
        }
        throw new InvalidOperationException("Could not locate the repo root (Spaarke.sln).");
    }

    private sealed class KeyedHarness(string key) : IIntakeHarness
    {
        public string Key { get; } = key;

        public bool CanHandle(IntakeHarnessRoute route) => true;

        public IReadOnlyList<IntakeHarnessStep> Steps { get; } = Array.Empty<IntakeHarnessStep>();

        public Task<IntakeHarnessStepOutcome> RunStepAsync(
            IntakeHarnessStep step,
            IntakeHarnessRequest request,
            IReadOnlyDictionary<string, IntakeHarnessResultReference> completedSteps,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException("The registry never runs a step.");

        public Task<IntakeHarnessPersistResult> PersistAsync(
            IntakeHarnessRequest request,
            IReadOnlyDictionary<string, IntakeHarnessResultReference> completedSteps,
            CancellationToken cancellationToken) =>
            Task.FromResult(new IntakeHarnessPersistResult());
    }
}
