using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.PublicContracts;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai.PublicContracts;

/// <summary>
/// FR-P0-04 boot reconciliation tests (spaarke-ai-architecture-redesign-r1
/// task 005): seeded catalog drift of each class must report
/// <see cref="HealthStatus.Unhealthy"/> with named diagnostics; a healthy
/// catalog, a disabled AI subsystem, and a transient Dataverse error must
/// never report Unhealthy. Mocks sit at the module boundary only
/// (<see cref="IGenericEntityService"/> = the Dataverse boundary, per
/// ADR-038); the handler registry is the REAL <see cref="ToolHandlerRegistry"/>
/// fed with in-memory handler doubles so the bijection is exercised through
/// the same abstraction task 008/009 handlers register through.
/// </summary>
public sealed class RoutingConsumerTypeHealthCheckTests
{
    private readonly Mock<IGenericEntityService> _entityServiceMock = new();

    // ── Row builders ────────────────────────────────────────────────────────

    private static Entity BindingRow(string consumerType)
    {
        var entity = new Entity("sprk_playbookconsumer");
        entity["sprk_consumertype"] = consumerType;
        entity["sprk_enabled"] = true;
        return entity;
    }

    private static Entity[] BindingRowsForAllConstants() =>
        ConsumerTypes.All.Select(BindingRow).ToArray();

    /// <summary>
    /// The <see cref="FakeToolHandler"/>'s compiled Metadata.Description. Single-row tool
    /// rows default their live sprk_description to this so the FR-A-01 description-parity
    /// dimension (AIR2-020) is satisfied — tests targeting OTHER dimensions stay Healthy.
    /// A test override with a different value exercises the parity drift path.
    /// </summary>
    private const string FakeHandlerDescription = "Reconciliation test double";

    private static Entity ToolRow(string name, string? handlerClass, string? toolId = null, string? description = FakeHandlerDescription)
    {
        var entity = new Entity("sprk_analysistool");
        entity["sprk_name"] = name;
        entity["sprk_handlerclass"] = handlerClass;
        entity["sprk_toolid"] = toolId;
        entity["sprk_description"] = description;
        return entity;
    }

    // ── Mock plumbing ───────────────────────────────────────────────────────

    private void SetupBindingRows(params Entity[] rows) =>
        _entityServiceMock
            .Setup(s => s.RetrieveMultipleAsync(
                It.Is<QueryExpression>(q => q.EntityName == "sprk_playbookconsumer"),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EntityCollection(rows.ToList()));

    private void SetupToolRows(params Entity[] rows) =>
        _entityServiceMock
            .Setup(s => s.RetrieveMultipleAsync(
                It.Is<QueryExpression>(q => q.EntityName == "sprk_analysistool"),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EntityCollection(rows.ToList()));

    private RoutingConsumerTypeHealthCheck CreateSut(
        IEnumerable<string>? handlerIds = null,
        Dictionary<string, string?>? configValues = null,
        bool registerEntityService = true,
        bool registerRegistry = true)
    {
        var services = new ServiceCollection();

        if (registerEntityService)
        {
            services.AddSingleton(_entityServiceMock.Object);
        }

        if (registerRegistry)
        {
            var handlers = (handlerIds ?? Array.Empty<string>())
                .Select(id => (IToolHandler)new FakeToolHandler(id));
            var registry = new ToolHandlerRegistry(
                handlers,
                Options.Create(new ToolFrameworkOptions()),
                NullLogger<ToolHandlerRegistry>.Instance);
            services.AddSingleton<IToolHandlerRegistry>(registry);
        }

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(configValues ?? new Dictionary<string, string?>())
            .Build();

        return new RoutingConsumerTypeHealthCheck(
            services.BuildServiceProvider(),
            configuration,
            NullLogger<RoutingConsumerTypeHealthCheck>.Instance);
    }

    private static Task<HealthCheckResult> CheckAsync(RoutingConsumerTypeHealthCheck sut) =>
        sut.CheckHealthAsync(new HealthCheckContext
        {
            Registration = new HealthCheckRegistration(
                "ai-catalog-reconciliation", sut, failureStatus: null, tags: null),
        });

    // ── Healthy paths ───────────────────────────────────────────────────────

    [Fact]
    public async Task CheckHealthAsync_HealthyCatalog_ReturnsHealthy()
    {
        SetupBindingRows(BindingRowsForAllConstants());
        SetupToolRows(
            ToolRow("Alpha Tool", "AlphaHandler", "dataverse.alpha"),
            ToolRow("Beta Tool", "BetaHandler", "dataverse.beta"));
        var sut = CreateSut(handlerIds: new[] { "AlphaHandler", "BetaHandler" });

        var result = await CheckAsync(sut);

        result.Status.Should().Be(HealthStatus.Healthy);
        result.Description.Should().Contain("bijection");
    }

    [Fact]
    public async Task CheckHealthAsync_AiSubsystemDisabled_ReturnsHealthyWithoutQueryingDataverse()
    {
        var sut = CreateSut(
            handlerIds: new[] { "AlphaHandler" },
            configValues: new Dictionary<string, string?> { ["Analysis:Enabled"] = "false" });

        var result = await CheckAsync(sut);

        result.Status.Should().Be(HealthStatus.Healthy);
        result.Description.Should().Contain("disabled");
        _entityServiceMock.Verify(
            s => s.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task CheckHealthAsync_ToolFrameworkDisabled_SkipsBijectionAndStaysHealthy()
    {
        // Only the binding query should run; the tool query is never issued and
        // an empty registry must NOT be reported as orphan-row drift.
        SetupBindingRows(BindingRowsForAllConstants());
        var sut = CreateSut(
            handlerIds: Array.Empty<string>(),
            configValues: new Dictionary<string, string?> { ["ToolFramework:Enabled"] = "false" });

        var result = await CheckAsync(sut);

        result.Status.Should().Be(HealthStatus.Healthy);
        result.Description.Should().Contain("skipped");
        _entityServiceMock.Verify(
            s => s.RetrieveMultipleAsync(
                It.Is<QueryExpression>(q => q.EntityName == "sprk_analysistool"),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task CheckHealthAsync_EntityServiceNotRegistered_ReturnsHealthySkipped()
    {
        var sut = CreateSut(registerEntityService: false);

        var result = await CheckAsync(sut);

        result.Status.Should().Be(HealthStatus.Healthy);
        result.Description.Should().Contain("skipped");
    }

    // ── Drift class (a): constants ↔ Binding rows ──────────────────────────

    [Fact]
    public async Task CheckHealthAsync_ConstantWithoutBindingRow_ReturnsDegradedNamingConstant()
    {
        // E-40/E-42: a constant ahead of its row (mirror-first seam) is a benign
        // forward-declaration → Degraded, not Unhealthy. (A row WITHOUT a constant is
        // the real admin-typo break and stays Unhealthy — see the sibling test.)
        var rows = ConsumerTypes.All
            .Where(t => t != ConsumerTypes.MatterPreFill)
            .Select(BindingRow)
            .ToArray();
        SetupBindingRows(rows);
        SetupToolRows(ToolRow("Alpha Tool", "AlphaHandler"));
        var sut = CreateSut(handlerIds: new[] { "AlphaHandler" });

        var result = await CheckAsync(sut);

        result.Status.Should().Be(HealthStatus.Degraded);
        result.Description.Should().Contain(ConsumerTypes.MatterPreFill);
        result.Description.Should().Contain("ahead of their Binding row");
    }

    [Fact]
    public async Task CheckHealthAsync_BindingRowWithoutConstant_ReturnsUnhealthyNamingRowValue()
    {
        // The 2026-06-24 UAT-2 incident shape: an admin typo on the Dataverse side.
        var rows = BindingRowsForAllConstants()
            .Append(BindingRow("matter-pre-fil"))
            .ToArray();
        SetupBindingRows(rows);
        SetupToolRows(ToolRow("Alpha Tool", "AlphaHandler"));
        var sut = CreateSut(handlerIds: new[] { "AlphaHandler" });

        var result = await CheckAsync(sut);

        result.Status.Should().Be(HealthStatus.Unhealthy);
        result.Description.Should().Contain("matter-pre-fil");
        result.Description.Should().Contain("without ConsumerTypes constant");
    }

    [Fact]
    public async Task CheckHealthAsync_EmptyBindingTable_ReturnsDegradedListingEveryConstant()
    {
        // E-40/E-42: an unseeded environment = every constant forward-declared (no rows
        // yet) → Degraded, and still lists every constant so an operator sees what to seed.
        // (Contrast: a row WITHOUT a constant is Unhealthy — that's a typo, not an unseeded env.)
        SetupBindingRows();
        SetupToolRows(ToolRow("Alpha Tool", "AlphaHandler"));
        var sut = CreateSut(handlerIds: new[] { "AlphaHandler" });

        var result = await CheckAsync(sut);

        result.Status.Should().Be(HealthStatus.Degraded);
        foreach (var constant in ConsumerTypes.All)
        {
            result.Description.Should().Contain(constant);
        }
    }

    // ── Compose-r2 parity (AIR2-E42, compose-r2 handoff B5): the 5 compose
    //    constants registered by this task must genuinely participate in the
    //    SAME constants-↔-rows parity dimension as every pre-existing consumer
    //    type — no special-cased pass-through. (create-matter's constant is NOT
    //    registered here — it lands with its live row at task 049 / DEF-003, so
    //    registering it ahead of the row would trip both this parity check and
    //    the golden-utterance planned-vs-existing coherence guard.) ──────

    public static IEnumerable<object[]> NewlyRegisteredConsumerTypes()
    {
        yield return new object[] { ConsumerTypes.ComposeExplainClause };
        yield return new object[] { ConsumerTypes.ComposeCompareToPlaybook };
        yield return new object[] { ConsumerTypes.ComposeSummarizeWordChanges };
        yield return new object[] { ConsumerTypes.ComposeDefinedTerms };
        yield return new object[] { ConsumerTypes.ComposeDraftAlternative };
    }

    [Fact]
    public void ConsumerTypesAll_IncludesComposeR2Constants()
    {
        // Acceptance criterion: ConsumerTypes carries the 5 compose-r2 consumer
        // types (compose-explain-clause / compose-compare-to-playbook /
        // compose-summarize-word-changes / compose-defined-terms /
        // compose-draft-alternative — infra/dataverse/sprk_playbookconsumer-rows.json,
        // compose-r2 tasks 040-044) so the health-check parity dimension has
        // something to reconcile deployed rows against.
        ConsumerTypes.All.Should().Contain(new[]
        {
            "compose-explain-clause",
            "compose-compare-to-playbook",
            "compose-summarize-word-changes",
            "compose-defined-terms",
            "compose-draft-alternative",
        });
    }

    [Theory]
    [MemberData(nameof(NewlyRegisteredConsumerTypes))]
    public async Task CheckHealthAsync_NewConsumerTypeMissingBindingRow_ReturnsDegradedNamingIt(string consumerType)
    {
        // A deployed row for every OTHER constant, but this one's Binding row is
        // absent — the exact pre-deploy state today (compose-r2 task 047 rows not yet
        // live). E-40/E-42: this is a benign FORWARD-DECLARATION (mirror-first seam —
        // the whole point of registering the constant ahead of the row) → Degraded, NOT
        // Unhealthy. It is NOT special-cased as exempt: it still participates in the
        // parity dimension and is named at /healthz so an operator knows to seed the row.
        var rows = ConsumerTypes.All
            .Where(t => t != consumerType)
            .Select(BindingRow)
            .ToArray();
        SetupBindingRows(rows);
        SetupToolRows(ToolRow("Alpha Tool", "AlphaHandler"));
        var sut = CreateSut(handlerIds: new[] { "AlphaHandler" });

        var result = await CheckAsync(sut);

        result.Status.Should().Be(HealthStatus.Degraded);
        result.Description.Should().Contain(consumerType);
        result.Description.Should().Contain("ahead of their Binding row");
    }

    [Theory]
    [MemberData(nameof(NewlyRegisteredConsumerTypes))]
    public async Task CheckHealthAsync_NewConsumerTypeRowPresent_ParticipatesInHealthyBijection(string consumerType)
    {
        // Once the catalog row IS deployed (task 047 / the DEF-003 seed), the
        // constant must resolve cleanly — a deployed row must NOT flip
        // Unhealthy (the acceptance criterion this task exists to satisfy).
        SetupBindingRows(BindingRowsForAllConstants());
        SetupToolRows(ToolRow("Alpha Tool", "AlphaHandler"));
        var sut = CreateSut(handlerIds: new[] { "AlphaHandler" });

        var result = await CheckAsync(sut);

        result.Status.Should().Be(HealthStatus.Healthy);
        ConsumerTypes.All.Should().Contain(consumerType);
    }

    [Fact]
    public async Task CheckHealthAsync_DataverseSideTypoOnNewComposeConsumerType_ReturnsUnhealthyNamingRowValue()
    {
        // The 2026-06-24 UAT-2 incident shape (matter-pre-fil), reproduced against
        // one of the NEW compose-r2 types: an admin/seed typo on the Dataverse side
        // for a type this task registered must be caught exactly like a
        // pre-existing constant's typo would be.
        var rows = BindingRowsForAllConstants()
            .Append(BindingRow("compose-draft-alternate")) // missing trailing "ive"
            .ToArray();
        SetupBindingRows(rows);
        SetupToolRows(ToolRow("Alpha Tool", "AlphaHandler"));
        var sut = CreateSut(handlerIds: new[] { "AlphaHandler" });

        var result = await CheckAsync(sut);

        result.Status.Should().Be(HealthStatus.Unhealthy);
        result.Description.Should().Contain("compose-draft-alternate");
        result.Description.Should().Contain("without ConsumerTypes constant");
    }

    // ── Drift class (b): tool row ↔ handler bijection ──────────────────────

    [Fact]
    public async Task CheckHealthAsync_ToolRowWithoutRegisteredHandler_ReturnsUnhealthyNamingHandlerClass()
    {
        SetupBindingRows(BindingRowsForAllConstants());
        SetupToolRows(
            ToolRow("Alpha Tool", "AlphaHandler"),
            ToolRow("Ghost Tool", "GhostHandler", "dataverse.ghost"));
        var sut = CreateSut(handlerIds: new[] { "AlphaHandler" });

        var result = await CheckAsync(sut);

        result.Status.Should().Be(HealthStatus.Unhealthy);
        result.Description.Should().Contain("GhostHandler");
        result.Description.Should().Contain("Ghost Tool");
        result.Description.Should().Contain("without a registered handler");
    }

    [Fact]
    public async Task CheckHealthAsync_HandlerWithoutToolRow_ReturnsUnhealthyNamingOrphanHandler()
    {
        // FR-P2-01 catalog-projection cutover (task 030, 2026-07-06): the closed
        // catalog is now the ONLY tool projection, so a registered handler with no
        // sprk_analysistool row is dead code or a missing seed — full drift,
        // Unhealthy. (Was Degraded-by-design between FR-P0-04 and this cutover —
        // gate-014 deferral resolved.)
        SetupBindingRows(BindingRowsForAllConstants());
        SetupToolRows(ToolRow("Alpha Tool", "AlphaHandler"));
        var sut = CreateSut(handlerIds: new[] { "AlphaHandler", "OrphanHandler" });

        var result = await CheckAsync(sut);

        result.Status.Should().Be(HealthStatus.Unhealthy);
        result.Description.Should().Contain("OrphanHandler");
        result.Description.Should().Contain("registered handlers without a sprk_analysistool row");
    }

    [Fact]
    public async Task CheckHealthAsync_MultipleToolRowsPerHandler_IsHealthy()
    {
        // Row = tool, handler = implementation: one handler serving several
        // named tool rows is the catalog's legitimate shape (e.g.
        // TextRefinementHandler serves Summary/KeyPoints/Refinement).
        SetupBindingRows(BindingRowsForAllConstants());
        SetupToolRows(
            ToolRow("Alpha Tool", "AlphaHandler", "dataverse.alpha"),
            ToolRow("Alpha Tool Sibling", "AlphaHandler", "dataverse.alpha_sibling"));
        var sut = CreateSut(handlerIds: new[] { "AlphaHandler" });

        var result = await CheckAsync(sut);

        result.Status.Should().Be(HealthStatus.Healthy);
    }

    // ── Drift class (d): description parity (FR-A-01 triple-twin hoist, AIR2-020) ──

    [Fact]
    public async Task CheckHealthAsync_SingleRowLiveDescriptionDivergesFromHandlerMetadata_ReturnsUnhealthy()
    {
        // The LLM-facing description is authored ONCE in the seed-row JSON and PATCHed
        // live by Seed-TypedHandlers.ps1; the compiled handler Metadata.Description is the
        // in-code mirror. A hand-edit to the LIVE row that the seed would silently revert
        // must surface as drift (Unhealthy), not pass unnoticed.
        SetupBindingRows(BindingRowsForAllConstants());
        SetupToolRows(ToolRow(
            "Alpha Tool", "AlphaHandler", "dataverse.alpha",
            description: "a hand-edited live description that drifts from the compiled mirror"));
        var sut = CreateSut(handlerIds: new[] { "AlphaHandler" });

        var result = await CheckAsync(sut);

        result.Status.Should().Be(HealthStatus.Unhealthy);
        result.Description.Should().Contain("diverges from the compiled handler Metadata.Description");
        result.Description.Should().Contain("Alpha Tool");
    }

    [Fact]
    public async Task CheckHealthAsync_MultiRowHandlerWithVaryingDescriptions_IsExemptFromParity()
    {
        // A handler serving several rows has ONE combined Metadata.Description that cannot
        // mirror N method-specific rows; those are authored per-row in JSON and are exempt.
        SetupBindingRows(BindingRowsForAllConstants());
        SetupToolRows(
            ToolRow("Alpha Tool", "AlphaHandler", "dataverse.alpha", description: "row-specific description A"),
            ToolRow("Alpha Tool Sibling", "AlphaHandler", "dataverse.alpha_sibling", description: "row-specific description B"));
        var sut = CreateSut(handlerIds: new[] { "AlphaHandler" });

        var result = await CheckAsync(sut);

        result.Status.Should().Be(HealthStatus.Healthy,
            "multi-row handlers are outside the single-row description-parity scope");
    }

    [Fact]
    public async Task CheckHealthAsync_DuplicateToolIdAcrossRows_ReturnsUnhealthyNamingToolId()
    {
        // Tool identity is sprk_toolid: two active rows claiming the same id
        // is the real duplicate-drift class (gate-014 semantic correction).
        SetupBindingRows(BindingRowsForAllConstants());
        SetupToolRows(
            ToolRow("Alpha Tool", "AlphaHandler", "dataverse.alpha"),
            ToolRow("Alpha Tool Copy", "BetaHandler", "dataverse.alpha"));
        var sut = CreateSut(handlerIds: new[] { "AlphaHandler", "BetaHandler" });

        var result = await CheckAsync(sut);

        result.Status.Should().Be(HealthStatus.Unhealthy);
        result.Description.Should().Contain("dataverse.alpha (2 rows)");
        result.Description.Should().Contain("tool identity violated");
    }

    [Fact]
    public async Task CheckHealthAsync_ToolRowMissingHandlerClass_ReturnsUnhealthyNamingRow()
    {
        SetupBindingRows(BindingRowsForAllConstants());
        SetupToolRows(
            ToolRow("Alpha Tool", "AlphaHandler"),
            ToolRow("Untethered Tool", handlerClass: null));
        var sut = CreateSut(handlerIds: new[] { "AlphaHandler" });

        var result = await CheckAsync(sut);

        result.Status.Should().Be(HealthStatus.Unhealthy);
        result.Description.Should().Contain("Untethered Tool");
        result.Description.Should().Contain("missing sprk_handlerclass");
    }

    [Fact]
    public async Task CheckHealthAsync_MultipleDriftClasses_ReportsEveryClassInOneDescription()
    {
        // Multiple REAL (Unhealthy) drift classes in one description. NOTE (E-40/E-42):
        // ConstantsWithoutRows is no longer a drift class — it is a Degraded forward-declaration
        // and is asserted separately; this test covers the row-without-constant + tool-bijection
        // drift classes that remain Unhealthy.
        var rows = BindingRowsForAllConstants()
            .Append(BindingRow("typo-consumer"))
            .ToArray();
        SetupBindingRows(rows);
        SetupToolRows(
            ToolRow("Ghost Tool", "GhostHandler"),
            ToolRow("Dup A", "AlphaHandler", "dataverse.dup"),
            ToolRow("Dup B", "AlphaHandler", "dataverse.dup"));
        var sut = CreateSut(handlerIds: new[] { "AlphaHandler" });

        var result = await CheckAsync(sut);

        result.Status.Should().Be(HealthStatus.Unhealthy);
        result.Description.Should().Contain("typo-consumer");
        result.Description.Should().Contain("GhostHandler");
        result.Description.Should().Contain("dataverse.dup (2 rows)");
    }

    // ── Fail-soft: transient errors are not drift ───────────────────────────

    [Fact]
    public async Task CheckHealthAsync_DataverseUnavailable_ReturnsDegradedNotUnhealthy()
    {
        _entityServiceMock
            .Setup(s => s.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Dataverse unreachable"));
        var sut = CreateSut(handlerIds: new[] { "AlphaHandler" });

        var result = await CheckAsync(sut);

        result.Status.Should().Be(HealthStatus.Degraded);
    }

    // ── G-P3 UAT round-1 H1: invalid authored schemas → Degraded (never Unhealthy)

    /// <summary>The exact UAT payload — property-level "required": true.</summary>
    private const string InvalidInputSchema =
        """{"type":"object","properties":{"due_date":{"type":"string","required":true}},"required":["due_date"]}""";

    private static Entity ActionRow(string actionCode, string? inputSchema)
    {
        var entity = new Entity("sprk_analysisaction");
        entity["sprk_actioncode"] = actionCode;
        entity["sprk_name"] = actionCode;
        entity["sprk_inputschema"] = inputSchema;
        return entity;
    }

    private void SetupActionRows(params Entity[] rows) =>
        _entityServiceMock
            .Setup(s => s.RetrieveMultipleAsync(
                It.Is<QueryExpression>(q => q.EntityName == "sprk_analysisaction"),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EntityCollection(rows.ToList()));

    [Fact]
    public async Task CheckHealthAsync_ActionRowWithInvalidInputSchema_ReturnsDegradedNamingTheRow()
    {
        // The G-P3 incident shape: catalog otherwise healthy; ONE action row's
        // sprk_inputschema is invalid OpenAI function parameters. The projection
        // excludes the row (the platform still functions) — the health check must
        // surface it as Degraded naming the row, NOT Unhealthy.
        SetupBindingRows(BindingRowsForAllConstants());
        SetupToolRows(ToolRow("Alpha Tool", "AlphaHandler", "dataverse.alpha"));
        SetupActionRows(
            ActionRow("CREATE-TASK@v1", InvalidInputSchema),
            ActionRow("SUM-CHAT@v1", """{"type":"object","properties":{"fileIds":{"type":"array","items":{"type":"string"}}}}"""));
        var sut = CreateSut(handlerIds: new[] { "AlphaHandler" });

        var result = await CheckAsync(sut);

        result.Status.Should().Be(HealthStatus.Degraded,
            "an invalid schema costs one excluded tool, not the platform — Degraded, never Unhealthy");
        result.Description.Should().Contain("CREATE-TASK@v1", "the finding names the offending row");
        result.Description.Should().Contain("required", "the finding carries the first keyword-path error");
        result.Description.Should().NotContain("SUM-CHAT@v1", "valid rows are not findings");
    }

    [Fact]
    public async Task CheckHealthAsync_ToolRowWithInvalidJsonSchema_ReturnsDegradedNamingTheRow()
    {
        SetupBindingRows(BindingRowsForAllConstants());
        var badTool = ToolRow("Alpha Tool", "AlphaHandler", "dataverse.alpha");
        badTool["sprk_jsonschema"] = """{"type":"object","properties":{"ids":{"type":"array"}}}"""; // array without items
        SetupToolRows(badTool);
        SetupActionRows();
        var sut = CreateSut(handlerIds: new[] { "AlphaHandler" });

        var result = await CheckAsync(sut);

        result.Status.Should().Be(HealthStatus.Degraded);
        result.Description.Should().Contain("dataverse.alpha");
        result.Description.Should().Contain("items");
    }

    [Fact]
    public async Task CheckHealthAsync_ValidAndLegacySchemas_StayHealthy()
    {
        SetupBindingRows(BindingRowsForAllConstants());
        SetupToolRows(ToolRow("Alpha Tool", "AlphaHandler", "dataverse.alpha"));
        SetupActionRows(
            ActionRow("REF-CHAT@v1", """{"type":"object","properties":{"unsupported_request":{"type":"string"}},"required":["unsupported_request"]}"""),
            // Legacy args wrapper: tolerated (unknown keyword — OpenAI projects a permissive schema).
            ActionRow("LEGACY@v1", """{"args":[{"name":"fileIds","type":"array","required":false}]}"""),
            ActionRow("NO-SCHEMA@v1", inputSchema: null));
        var sut = CreateSut(handlerIds: new[] { "AlphaHandler" });

        var result = await CheckAsync(sut);

        result.Status.Should().Be(HealthStatus.Healthy,
            "valid, legacy-args, and schema-less rows are all projectable — nothing to surface");
    }

    [Fact]
    public async Task CheckHealthAsync_DriftAndInvalidSchemaTogether_UnhealthyWins()
    {
        // Severity ordering: drift (catalog broken) outranks degraded (one excluded row).
        SetupBindingRows(BindingRowsForAllConstants().Append(BindingRow("typo-consumer")).ToArray());
        SetupToolRows(ToolRow("Alpha Tool", "AlphaHandler", "dataverse.alpha"));
        SetupActionRows(ActionRow("CREATE-TASK@v1", InvalidInputSchema));
        var sut = CreateSut(handlerIds: new[] { "AlphaHandler" });

        var result = await CheckAsync(sut);

        result.Status.Should().Be(HealthStatus.Unhealthy,
            "drift is the deploy gate — schema findings never mask it");
    }

    // ── In-memory handler double (module-boundary only; never executed) ─────

    private sealed class FakeToolHandler : IToolHandler
    {
        public FakeToolHandler(string handlerId) => HandlerId = handlerId;

        public string HandlerId { get; }

        public ToolHandlerMetadata Metadata { get; } = new(
            Name: "Fake Tool Handler",
            Description: FakeHandlerDescription,
            Version: "1.0.0",
            SupportedInputTypes: Array.Empty<string>(),
            Parameters: Array.Empty<ToolParameterDefinition>());

        public IReadOnlyList<ToolType> SupportedToolTypes { get; } = new[] { ToolType.Custom };

        public ToolValidationResult Validate(ToolExecutionContext context, AnalysisTool tool) =>
            ToolValidationResult.Success();

        public Task<ToolResult> ExecuteAsync(
            ToolExecutionContext context,
            AnalysisTool tool,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException("Not exercised by reconciliation tests.");
    }
}
