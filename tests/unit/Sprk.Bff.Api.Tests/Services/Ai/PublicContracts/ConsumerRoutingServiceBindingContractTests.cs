using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Ai.PublicContracts;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai.PublicContracts;

/// <summary>
/// FR-P0-03 (spaarke-ai-architecture-redesign-r1 task 004) — contract tests for
/// <see cref="IConsumerRoutingService.ResolveBindingAsync"/>: a fully-populated
/// <c>sprk_playbookconsumer</c> row maps EVERY field of the full
/// <see cref="Binding"/> contract (incl. choice → enum mapping and JSON-in-memo
/// parsing); legacy pre-task-003 rows with null new columns resolve without
/// error to documented safe defaults; the existing Guid resolve methods are
/// behaviorally unchanged (covered by <see cref="ConsumerRoutingServiceTests"/>,
/// which pass unmodified against the extended implementation).
/// </summary>
public sealed class ConsumerRoutingServiceBindingContractTests
{
    private static readonly Guid BindingRowId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid PlaybookA = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid ActionA = Guid.Parse("44444444-4444-4444-4444-444444444444");

    private readonly Mock<IGenericEntityService> _entityServiceMock = new();
    private readonly IMemoryCache _cache = new MemoryCache(new MemoryCacheOptions());
    private readonly Mock<IHostEnvironment> _envMock = new();
    private readonly Mock<ILogger<ConsumerRoutingService>> _loggerMock = new();

    public ConsumerRoutingServiceBindingContractTests()
    {
        _envMock.SetupGet(e => e.EnvironmentName).Returns("dev");
    }

    private ConsumerRoutingService CreateService() =>
        new(_entityServiceMock.Object, _cache, _envMock.Object, _loggerMock.Object);

    private void SetupQueryResponse(params Entity[] entities)
    {
        var collection = new EntityCollection(entities.ToList());
        _entityServiceMock
            .Setup(s => s.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(collection);
    }

    /// <summary>
    /// A legacy row exactly as it existed before the task-003 schema extension:
    /// only the original routing columns populated; every new column absent.
    /// </summary>
    private static Entity BuildLegacyEntity(
        Guid playbookId,
        string? consumerCode = "default",
        string? environment = "*",
        int priority = 500)
    {
        var entity = new Entity("sprk_playbookconsumer");
        entity["sprk_playbookconsumerid"] = BindingRowId;
        entity["sprk_consumertype"] = "chat-summarize";
        entity["sprk_consumercode"] = consumerCode;
        entity["sprk_environment"] = environment;
        entity["sprk_priority"] = priority;
        entity["sprk_enabled"] = true;
        entity["sprk_playbook"] = new EntityReference("sprk_analysisplaybook", playbookId);
        return entity;
    }

    /// <summary>
    /// A row with every task-003 column populated, plus the aliased Action-side
    /// columns as the left-outer <c>action</c> link entity projects them.
    /// </summary>
    private static Entity BuildFullyPopulatedEntity()
    {
        var entity = BuildLegacyEntity(PlaybookA);
        entity["sprk_matchconditions"] = """{ "mimeType": "application/pdf" }""";
        entity["sprk_action"] = new EntityReference("sprk_analysisaction", ActionA);

        // task-003 Binding columns (§6.2)
        entity["sprk_ucid"] = "UC-A-1";
        entity["sprk_tooldescription"] = "Summarize the current document for the user.";
        entity["sprk_disposition"] = new OptionSetValue(100000001);      // Work Product
        entity["sprk_chiptransitions"] =
            """[{"target_binding_id":"55555555-5555-5555-5555-555555555555","chip_label":"Send welcome email"},{"target_binding_id":"66666666-6666-6666-6666-666666666666","chip_label":"Save to matter"}]""";
        entity["sprk_risk"] = new OptionSetValue(100000002);             // Always Confirm
        entity["sprk_capturemode"] = new OptionSetValue(100000001);      // Modal
        entity["sprk_oneventbindings"] =
            """[{"event":"document_uploaded","order":2}]""";
        entity["sprk_surfaces"] = "assistant, record-form,office";
        entity["sprk_modeltieroverride"] = new OptionSetValue(100000002); // Reasoning

        // Action-side execution fields (§6.1) — aliased via the action link
        entity["action.sprk_kind"] = new AliasedValue(
            "sprk_analysisaction", "sprk_kind", new OptionSetValue(100000001)); // Coded
        entity["action.sprk_workflowclass"] = new AliasedValue(
            "sprk_analysisaction", "sprk_workflowclass", "Sprk.Bff.Api.Services.Ai.Workflows.UploadCompositeWorkflow");
        entity["action.sprk_inputschema"] = new AliasedValue(
            "sprk_analysisaction", "sprk_inputschema",
            """{"args":[{"name":"documentId","type":"string","required":true}]}""");
        entity["action.sprk_modeltier"] = new AliasedValue(
            "sprk_analysisaction", "sprk_modeltier", new OptionSetValue(100000000)); // Fast
        return entity;
    }

    // ── (a) Fully-populated row maps every contract field ───────────────────

    [Fact]
    public async Task ResolveBindingAsync_FullyPopulatedRow_MapsEveryContractField()
    {
        SetupQueryResponse(BuildFullyPopulatedEntity());
        var sut = CreateService();

        var binding = await sut.ResolveBindingAsync(
            "chat-summarize",
            context: new RoutingContext { MimeType = "application/pdf" });

        binding.Should().NotBeNull();

        // Identity + original routing columns
        binding!.BindingId.Should().Be(BindingRowId);
        binding.ConsumerType.Should().Be("chat-summarize");
        binding.ConsumerCode.Should().Be("default");
        binding.Environment.Should().Be("*");
        binding.Priority.Should().Be(500);
        binding.MatchConditionsJson.Should().Be("""{ "mimeType": "application/pdf" }""");
        binding.PlaybookId.Should().Be(PlaybookA);
        binding.ActionId.Should().Be(ActionA);

        // task-003 Binding columns
        binding.Ucid.Should().Be("UC-A-1");
        binding.ToolDescription.Should().Be("Summarize the current document for the user.");
        binding.Disposition.Should().Be(BindingDisposition.WorkProduct);
        binding.ChipTransitions.Should().HaveCount(2);
        binding.ChipTransitions[0].TargetBindingId.Should().Be("55555555-5555-5555-5555-555555555555");
        binding.ChipTransitions[0].ChipLabel.Should().Be("Send welcome email");
        binding.ChipTransitions[1].ChipLabel.Should().Be("Save to matter");
        binding.Risk.Should().Be(BindingRisk.AlwaysConfirm);
        binding.CaptureMode.Should().Be(BindingCaptureMode.Modal);
        binding.OnEventBindings.Should().ContainSingle();
        binding.OnEventBindings[0].Event.Should().Be("document_uploaded");
        binding.OnEventBindings[0].Order.Should().Be(2);
        binding.Surfaces.Should().Equal("assistant", "record-form", "office");
        binding.ModelTierOverride.Should().Be(AiModelTier.Reasoning);

        // Action-side execution fields
        binding.ActionKind.Should().Be(ActionKind.Coded);
        binding.WorkflowClass.Should().Be("Sprk.Bff.Api.Services.Ai.Workflows.UploadCompositeWorkflow");
        binding.InputSchemaJson.Should().Be("""{"args":[{"name":"documentId","type":"string","required":true}]}""");
        binding.ActionModelTier.Should().Be(AiModelTier.Fast);

        // Binding override wins over Action default
        binding.EffectiveModelTier.Should().Be(AiModelTier.Reasoning);
    }

    // ── (b) Legacy row with null new columns resolves with safe defaults ────

    [Fact]
    public async Task ResolveBindingAsync_LegacyRowWithNullNewColumns_ResolvesWithSafeDefaults()
    {
        SetupQueryResponse(BuildLegacyEntity(PlaybookA));
        var sut = CreateService();

        var binding = await sut.ResolveBindingAsync("chat-summarize");

        binding.Should().NotBeNull("pre-task-003 rows MUST resolve without error");
        binding!.BindingId.Should().Be(BindingRowId);
        binding.PlaybookId.Should().Be(PlaybookA);
        binding.ActionId.Should().BeNull("legacy pure-engine rows have no sprk_action");

        // Documented safe defaults for null new columns
        binding.Ucid.Should().BeNull();
        binding.ToolDescription.Should().BeNull();
        binding.Disposition.Should().Be(BindingDisposition.Informational, "null disposition defaults to informational (canonical §5)");
        binding.ChipTransitions.Should().BeEmpty();
        binding.Risk.Should().Be(BindingRisk.None);
        binding.CaptureMode.Should().Be(BindingCaptureMode.LoopElicitation, "per column dictionary: treat null as Loop Elicitation");
        binding.OnEventBindings.Should().BeEmpty();
        binding.Surfaces.Should().BeEmpty("empty surfaces = offered on all surfaces");
        binding.ModelTierOverride.Should().BeNull("null = use Action default");

        // Action-side defaults when the left-outer link produced no row
        binding.ActionKind.Should().Be(ActionKind.Prompted, "per column dictionary: treat null kind as Prompted");
        binding.WorkflowClass.Should().BeNull();
        binding.InputSchemaJson.Should().BeNull();
        binding.ActionModelTier.Should().BeNull();
        binding.EffectiveModelTier.Should().BeNull("null tiers = platform default (ModelSelector decides)");
    }

    // ── JSON tolerance: malformed maker JSON must not break routing ─────────

    [Theory]
    [InlineData("""[{"target_binding_id": "x" """)] // truncated
    [InlineData("not json at all")]
    [InlineData("""{"target_binding_id":"x"}""")]   // object, not array
    public async Task ResolveBindingAsync_MalformedChipTransitionsJson_ResolvesWithEmptyChips(string chipJson)
    {
        var entity = BuildLegacyEntity(PlaybookA);
        entity["sprk_chiptransitions"] = chipJson;
        SetupQueryResponse(entity);
        var sut = CreateService();

        var binding = await sut.ResolveBindingAsync("chat-summarize");

        binding.Should().NotBeNull("maker data-entry errors must not break routing");
        binding!.ChipTransitions.Should().BeEmpty();
    }

    [Fact]
    public async Task ResolveBindingAsync_MalformedOnEventBindingsJson_ResolvesWithEmptyList()
    {
        var entity = BuildLegacyEntity(PlaybookA);
        entity["sprk_oneventbindings"] = """[{"event": """; // truncated
        SetupQueryResponse(entity);
        var sut = CreateService();

        var binding = await sut.ResolveBindingAsync("chat-summarize");

        binding.Should().NotBeNull();
        binding!.OnEventBindings.Should().BeEmpty();
    }

    // ── Choice tolerance: unknown future option values fall back safely ─────

    [Fact]
    public async Task ResolveBindingAsync_UnknownChoiceValues_FallBackToDefaults()
    {
        var entity = BuildLegacyEntity(PlaybookA);
        entity["sprk_disposition"] = new OptionSetValue(100000099);      // not in the option set
        entity["sprk_risk"] = new OptionSetValue(100000099);
        entity["sprk_capturemode"] = new OptionSetValue(100000099);
        entity["sprk_modeltieroverride"] = new OptionSetValue(100000099);
        SetupQueryResponse(entity);
        var sut = CreateService();

        var binding = await sut.ResolveBindingAsync("chat-summarize");

        binding.Should().NotBeNull("unknown option values must degrade, not throw");
        binding!.Disposition.Should().Be(BindingDisposition.Informational);
        binding.Risk.Should().Be(BindingRisk.None);
        binding.CaptureMode.Should().Be(BindingCaptureMode.LoopElicitation);
        binding.ModelTierOverride.Should().BeNull();
    }

    // ── Model tier precedence ───────────────────────────────────────────────

    [Fact]
    public async Task ResolveBindingAsync_NoOverride_EffectiveModelTierUsesActionDefault()
    {
        var entity = BuildLegacyEntity(PlaybookA);
        entity["sprk_action"] = new EntityReference("sprk_analysisaction", ActionA);
        entity["action.sprk_modeltier"] = new AliasedValue(
            "sprk_analysisaction", "sprk_modeltier", new OptionSetValue(100000001)); // Standard
        SetupQueryResponse(entity);
        var sut = CreateService();

        var binding = await sut.ResolveBindingAsync("chat-summarize");

        binding!.ModelTierOverride.Should().BeNull();
        binding.ActionModelTier.Should().Be(AiModelTier.Standard);
        binding.EffectiveModelTier.Should().Be(AiModelTier.Standard);
    }

    // ── Resolution semantics parity with the Guid methods ───────────────────

    [Fact]
    public async Task ResolveBindingAsync_NoRecords_ReturnsNull()
    {
        SetupQueryResponse();
        var sut = CreateService();

        var binding = await sut.ResolveBindingAsync("chat-summarize");

        binding.Should().BeNull();
    }

    [Fact]
    public async Task ResolveBindingAsync_RowWithNeitherTargetLookup_ReturnsNull()
    {
        var entity = BuildLegacyEntity(PlaybookA);
        entity["sprk_playbook"] = null; // neither playbook nor action → admin error, skipped
        SetupQueryResponse(entity);
        var sut = CreateService();

        var binding = await sut.ResolveBindingAsync("chat-summarize");

        binding.Should().BeNull();
    }

    [Fact]
    public async Task ResolveBindingAsync_ActionOnlyRow_Resolves()
    {
        var entity = BuildLegacyEntity(PlaybookA);
        entity["sprk_playbook"] = null;
        entity["sprk_action"] = new EntityReference("sprk_analysisaction", ActionA);
        SetupQueryResponse(entity);
        var sut = CreateService();

        var binding = await sut.ResolveBindingAsync("chat-summarize");

        binding.Should().NotBeNull("a Binding qualifies with EITHER target lookup populated");
        binding!.PlaybookId.Should().BeNull();
        binding.ActionId.Should().Be(ActionA);
    }

    [Fact]
    public async Task ResolveBindingAsync_TwoMatches_LowerPriorityWins()
    {
        var low = BuildLegacyEntity(PlaybookA, priority: 300);
        low["sprk_playbookconsumerid"] = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        var high = BuildLegacyEntity(PlaybookA, priority: 700);
        SetupQueryResponse(high, low);
        var sut = CreateService();

        var binding = await sut.ResolveBindingAsync("chat-summarize");

        binding!.BindingId.Should().Be(
            Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
            "the full-contract path uses the same FR-1R-03 selection algorithm as the Guid methods");
        binding.Priority.Should().Be(300);
    }

    // ── Query shape: link entity + full column set ──────────────────────────

    [Fact]
    public async Task ResolveBindingAsync_QueriesAllBindingColumnsAndActionLink()
    {
        SetupQueryResponse(BuildFullyPopulatedEntity());
        var sut = CreateService();

        await sut.ResolveBindingAsync(
            "chat-summarize",
            context: new RoutingContext { MimeType = "application/pdf" });

        _entityServiceMock.Verify(
            s => s.RetrieveMultipleAsync(
                It.Is<QueryExpression>(q =>
                    q.ColumnSet.Columns.Contains("sprk_ucid") &&
                    q.ColumnSet.Columns.Contains("sprk_tooldescription") &&
                    q.ColumnSet.Columns.Contains("sprk_disposition") &&
                    q.ColumnSet.Columns.Contains("sprk_chiptransitions") &&
                    q.ColumnSet.Columns.Contains("sprk_risk") &&
                    q.ColumnSet.Columns.Contains("sprk_capturemode") &&
                    q.ColumnSet.Columns.Contains("sprk_oneventbindings") &&
                    q.ColumnSet.Columns.Contains("sprk_surfaces") &&
                    q.ColumnSet.Columns.Contains("sprk_modeltieroverride") &&
                    q.LinkEntities.Count == 1 &&
                    q.LinkEntities[0].LinkToEntityName == "sprk_analysisaction" &&
                    q.LinkEntities[0].JoinOperator == JoinOperator.LeftOuter &&
                    q.LinkEntities[0].Columns.Columns.Contains("sprk_kind") &&
                    q.LinkEntities[0].Columns.Columns.Contains("sprk_workflowclass") &&
                    q.LinkEntities[0].Columns.Columns.Contains("sprk_inputschema") &&
                    q.LinkEntities[0].Columns.Columns.Contains("sprk_modeltier")),
                It.IsAny<CancellationToken>()),
            Times.Once,
            "the Dataverse query is the wire contract: every task-003 column plus the left-outer Action link must be projected");
    }

    // ── Cache behavior (Binding contract cached like the Guid results) ──────

    [Fact]
    public async Task ResolveBindingAsync_SecondCallWithinTtl_UsesCache()
    {
        SetupQueryResponse(BuildLegacyEntity(PlaybookA));
        var sut = CreateService();

        var first = await sut.ResolveBindingAsync("chat-summarize");
        var second = await sut.ResolveBindingAsync("chat-summarize");

        first!.BindingId.Should().Be(BindingRowId);
        second!.BindingId.Should().Be(BindingRowId);

        _entityServiceMock.Verify(
            s => s.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()),
            Times.Once,
            "the cache should serve the second call without re-querying Dataverse");
    }

    [Fact]
    public async Task ResolveBindingAsync_DoesNotShareCacheWithGuidMethods()
    {
        SetupQueryResponse(BuildLegacyEntity(PlaybookA));
        var sut = CreateService();

        var playbookId = await sut.ResolveAsync("chat-summarize");
        var binding = await sut.ResolveBindingAsync("chat-summarize");

        playbookId.Should().Be(PlaybookA);
        binding!.PlaybookId.Should().Be(PlaybookA);

        _entityServiceMock.Verify(
            s => s.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()),
            Times.Exactly(2),
            "target kinds are namespaced in the cache key; a Guid-path result must not satisfy a full-contract lookup");
    }

    // ── Graceful degrade ────────────────────────────────────────────────────

    [Fact]
    public async Task ResolveBindingAsync_DataverseThrows_ReturnsNullWithoutPropagating()
    {
        _entityServiceMock
            .Setup(s => s.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("simulated Dataverse failure"));
        var sut = CreateService();

        var binding = await sut.ResolveBindingAsync("chat-summarize");

        binding.Should().BeNull();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ResolveBindingAsync_NullOrEmptyConsumerType_Throws(string? consumerType)
    {
        var sut = CreateService();

        var act = async () => await sut.ResolveBindingAsync(consumerType!);

        await act.Should().ThrowAsync<ArgumentException>().WithParameterName("consumerType");
    }
}
