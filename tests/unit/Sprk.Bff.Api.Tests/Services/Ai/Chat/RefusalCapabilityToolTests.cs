using System.Diagnostics.Metrics;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Sprk.Bff.Api.Infrastructure.Cache;
using Sprk.Bff.Api.Models.Ai.Chat;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Chat;
using Sprk.Bff.Api.Services.Ai.LinearConsumers;
using Sprk.Bff.Api.Services.Ai.PublicContracts;
using Sprk.Bff.Api.Telemetry;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai.Chat;

/// <summary>
/// FR-P2-04 (spaarke-ai-architecture-redesign-r1 task 033) — the honest-refusal
/// loop outcome. Covers the four acceptance behaviors:
/// (1) an off-catalog invocation renders the TENANT'S template (catalog data —
/// the Binding's prompted Action output), read back FROM the stored ledger
/// entry (render follows store, ADR-040);
/// (2) the SessionOutput ledger write PRECEDES the rendered return;
/// (3) <c>dispatch_refused</c> telemetry is emitted (identifiers only, NFR-07);
/// (4) no hardcoded refusal copy — construction requires the catalog
/// tool-description opt-in, and the relayed text is exclusively the stored
/// payload's <c>refusal</c> field.
/// </summary>
public class RefusalCapabilityToolTests
{
    private const string TenantId = "tenant-test";
    private const string SessionId = "session-refusal-1";

    private static readonly Guid RefusalActionId = Guid.NewGuid();

    private static readonly ILogger TestLogger = NullLogger.Instance;

    // ─────────────────────────────────────────────────────────────────────────
    // Projection contract — catalog data only, no hardcoded copy
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Projection_NameDescriptionAndSchema_ComeFromTheCatalogRow()
    {
        var binding = CreateRefusalBinding(
            toolDescription: "REFUSAL — maker-authored intent surface.",
            inputSchemaJson: """{"type":"object","properties":{"unsupported_request":{"type":"string"}},"required":["unsupported_request"]}""");

        var tool = new RefusalCapabilityTool(
            binding, new ServiceCollection().BuildServiceProvider(), TenantId, SessionId, TestLogger);

        tool.Name.Should().Be("capability_no_match_handler",
            "the deterministic capability-tool naming rule applies to the refusal projection (NFR-04)");
        tool.Description.Should().Be("REFUSAL — maker-authored intent surface.",
            "the maker-authored sprk_tooldescription IS the intent surface — no hardcoded refusal copy");
        tool.JsonSchema.GetProperty("properties")
            .TryGetProperty(RefusalCapabilityTool.UnsupportedRequestArgName, out _).Should().BeTrue(
            "the Action's sprk_inputschema drives the parameter schema (catalog data)");
    }

    [Fact]
    public void Construction_WithoutToolDescription_IsRejected()
    {
        var binding = CreateRefusalBinding(toolDescription: null);
        var act = () => new RefusalCapabilityTool(
            binding, new ServiceCollection().BuildServiceProvider(), TenantId, SessionId, TestLogger);

        act.Should().Throw<ArgumentException>(
            "a Binding without sprk_tooldescription has not opted into text-path projection " +
            "(ADR-039 catalog opt-in — same rule as every projected capability)");
    }

    [Fact]
    public void Construction_WithNonRefusalConsumerType_IsRejected()
    {
        var binding = CreateRefusalBinding() with { ConsumerType = ConsumerTypes.ChatSummarize };
        var act = () => new RefusalCapabilityTool(
            binding, new ServiceCollection().BuildServiceProvider(), TenantId, SessionId, TestLogger);

        act.Should().Throw<ArgumentException>(
            "the refusal tool projects ONLY the tenant's no_match_handler Binding — generic " +
            "capabilities project as BindingCapabilityTool");
    }

    [Fact]
    public void PreFilter_RefusalTool_FiltersOnCatalogSurfacesColumn()
    {
        var assistantTool = CreateTool(CreateRefusalBinding(surfaces: new[] { "assistant" }),
            new ServiceCollection().BuildServiceProvider());
        var recordFormOnlyTool = CreateTool(CreateRefusalBinding(surfaces: new[] { "record-form" }),
            new ServiceCollection().BuildServiceProvider());
        var allSurfacesTool = CreateTool(CreateRefusalBinding(surfaces: Array.Empty<string>()),
            new ServiceCollection().BuildServiceProvider());

        var context = new AgentToolFilterContext(
            AgentToolFilterContext.AssistantSurface,
            HasSessionFiles: false, HasActiveDocument: false, HasAnalysisBinding: false);

        var survivors = AgentToolProjection
            .PreFilter(new AIFunction[] { assistantTool, recordFormOnlyTool, allSurfacesTool }, context)
            .ToList();

        survivors.Should().Contain(assistantTool, "sprk_surfaces contains the session surface");
        survivors.Should().Contain(allSurfacesTool, "empty surfaces = offered on ALL surfaces (column dictionary rule)");
        survivors.Should().NotContain(recordFormOnlyTool,
            "the refusal projection obeys the same deterministic catalog-column pre-filter as every capability tool");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // The refusal outcome — template render + ledger-before-render + telemetry
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task InvokeAsync_OffCatalogRequest_RelaysTenantTemplateFromTheStoredLedgerEntry()
    {
        var harness = new Harness();
        // The STORED entry's payload intentionally differs from the raw executor output:
        // proving the relayed text is read back FROM the ledger entry (render follows
        // store — ADR-040), never from pre-store state.
        harness.StoredPayloadJson = """{"refusal":"STORED tenant refusal template text."}""";
        var tool = harness.CreateTool();

        var result = await tool.InvokeAsync(new AIFunctionArguments
        {
            [RefusalCapabilityTool.UnsupportedRequestArgName] = "document translation",
        });

        var text = result!.ToString()!;
        text.Should().Contain("STORED tenant refusal template text.",
            "the relayed refusal is the tenant's template read from the STORED SessionOutput");
        text.Should().Contain(harness.StoredKey, "the return names the addressable ledger key");
        text.Should().Contain("VERBATIM", "the model is instructed to relay the template as-is (ADR-039: renders as declared)");
        text.Should().NotContain("RAW executor output", "pre-store state must never render");
    }

    [Fact]
    public async Task InvokeAsync_LedgerWrite_PrecedesTheRenderedReturn()
    {
        var harness = new Harness();
        var tool = harness.CreateTool();

        await tool.InvokeAsync(new AIFunctionArguments
        {
            [RefusalCapabilityTool.UnsupportedRequestArgName] = "translation",
        });

        harness.CallSequence.Should().ContainInOrder("actionRunner.Run", "outputRouter.Route");
        harness.CallSequence.Should().Contain("outputRouter.Route",
            "the refusal SessionOutput is written to the ledger BEFORE the tool returns any renderable text (ADR-040)");
        harness.RoutedBinding!.BindingId.Should().Be(harness.Binding.BindingId,
            "the ledger entry is keyed on the no_match_handler Binding ({bindingId}@t{n})");
        harness.RoutedSession!.SessionId.Should().Be(SessionId);
    }

    [Fact]
    public async Task InvokeAsync_PassesTheRequestLabelToThePromptedRender_ButNeverRequiresFiles()
    {
        var harness = new Harness();
        var tool = harness.CreateTool();

        await tool.InvokeAsync(new AIFunctionArguments
        {
            [RefusalCapabilityTool.UnsupportedRequestArgName] = "document translation",
        });

        harness.RunDocumentText!.ExtractedText.Should().Contain("document translation",
            "the model's short label is the prompted render's input so the template can acknowledge the request");
        harness.RunAction!.Id.Should().Be(RefusalActionId,
            "the render executes the Binding's target Action (REF-CHAT@v1 — catalog data)");
        harness.SessionFiles.Should().BeEmpty(
            "the refusal works in a FILE-LESS session — the reason it cannot ride SessionDispatchOrchestrator's " +
            "session-file dispatch envelope");
    }

    [Fact]
    public async Task InvokeAsync_EmitsDispatchRefusedTelemetry_IdentifiersOnly()
    {
        var harness = new Harness();
        var tool = harness.CreateTool();

        using var collector = new DispatchRefusedCollector();
        await tool.InvokeAsync(new AIFunctionArguments
        {
            [RefusalCapabilityTool.UnsupportedRequestArgName] = "translate my NDA into Spanish please",
        });

        var m = collector.Measurements.Should().ContainSingle(
            "one refusal outcome emits exactly one dispatch_refused event (the refusal-backlog signal)").Subject;
        m.Value.Should().Be(1);
        m.Tags.Should().ContainKey("render_status").WhoseValue.Should().Be("rendered");
        m.Tags.Should().ContainKey("tenant.id").WhoseValue.Should().Be(TenantId);
        m.Tags.Values.Select(v => v?.ToString()).Should().NotContain(
            s => s != null && s.Contains("translate my NDA"),
            "NFR-07: the event carries identifiers only — never utterance content");
    }

    [Fact]
    public async Task InvokeAsync_TemplateRenderFails_ReturnsHonestFailureInstruction_AndStillCountsTheRefusal()
    {
        var harness = new Harness { ActionRunnerThrows = true };
        var tool = harness.CreateTool();

        using var collector = new DispatchRefusedCollector();
        var result = await tool.InvokeAsync(new AIFunctionArguments
        {
            [RefusalCapabilityTool.UnsupportedRequestArgName] = "translation",
        });

        var text = result!.ToString()!;
        text.Should().Be(RefusalCapabilityTool.BuildRenderFailureInstruction(),
            "a failed template render degrades to the deterministic honest-decline instruction — " +
            "never a fabricated answer, never silence (ADR-039)");
        text.Should().Contain("do NOT invent capabilities",
            "the failure path forbids improvised alternatives");

        var m = collector.Measurements.Should().ContainSingle(
            "the turn still ended in a refusal — the backlog signal is still counted").Subject;
        m.Tags.Should().ContainKey("render_status").WhoseValue.Should().Be("render_failed");
        harness.CallSequence.Should().NotContain("outputRouter.Route",
            "no ledger entry is written when the template did not render");
    }

    [Fact]
    public async Task InvokeAsync_MissingServices_DegradesHonestly()
    {
        var binding = CreateRefusalBinding();
        var tool = new RefusalCapabilityTool(
            binding, new ServiceCollection().BuildServiceProvider(), TenantId, SessionId, TestLogger);

        var result = await tool.InvokeAsync(new AIFunctionArguments());

        result!.ToString().Should().Be(RefusalCapabilityTool.BuildRenderFailureInstruction(),
            "compound-AI-OFF environments degrade to the honest-decline instruction, never a crash or fabrication");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // NFR-07 — the request label never reaches the ToolChain audit
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void SummarizeArguments_UnsupportedRequestLabel_IsAlwaysRedactedByName()
    {
        var summary = AgentTurnContract.SummarizeArguments(new AIFunctionArguments
        {
            [RefusalCapabilityTool.UnsupportedRequestArgName] = "translation",
        });

        summary.Should().Be($"{RefusalCapabilityTool.UnsupportedRequestArgName}=<redacted:11>",
            "the label paraphrases the user's utterance — even a single identifier-shaped token is " +
            "redacted by NAME in the ToolChain ledger audit (NFR-07 defense in depth)");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Shape-drift tolerance helpers
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void ExtractRefusalText_ReadsTheSchemaField_AndDegradesToRawPayloadOnDrift()
    {
        using var conforming = JsonDocument.Parse("""{"refusal":"The tenant's message."}""");
        RefusalCapabilityTool.ExtractRefusalText(conforming.RootElement)
            .Should().Be("The tenant's message.");

        using var drifted = JsonDocument.Parse("""{"other":"shape"}""");
        RefusalCapabilityTool.ExtractRefusalText(drifted.RootElement)
            .Should().Be("""{"other":"shape"}""",
                "schema drift degrades to the raw STORED payload — the model still receives only stored content");
    }

    [Fact]
    public void ReadUnsupportedRequestLabel_MissingOrWrongShape_DegradesToNeutralPlaceholder()
    {
        RefusalCapabilityTool.ReadUnsupportedRequestLabel(null)
            .Should().Be("(unspecified request)");
        RefusalCapabilityTool.ReadUnsupportedRequestLabel(new AIFunctionArguments
        {
            [RefusalCapabilityTool.UnsupportedRequestArgName] = 42,
        }).Should().Be("(unspecified request)", "arg-shape drift never throws");
        RefusalCapabilityTool.ReadUnsupportedRequestLabel(new AIFunctionArguments
        {
            [RefusalCapabilityTool.UnsupportedRequestArgName] = "  translation  ",
        }).Should().Be("translation");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Harness
    // ─────────────────────────────────────────────────────────────────────────

    private static Binding CreateRefusalBinding(
        string? toolDescription = "REFUSAL — maker-authored intent surface.",
        string? inputSchemaJson = null,
        string[]? surfaces = null) => new()
        {
            BindingId = Guid.NewGuid(),
            ConsumerType = ConsumerTypes.NoMatchHandler,
            ActionId = RefusalActionId,
            ToolDescription = toolDescription,
            InputSchemaJson = inputSchemaJson,
            Surfaces = surfaces ?? new[] { "assistant" },
            Ucid = "L4-REFUSAL",
        };

    private static RefusalCapabilityTool CreateTool(Binding binding, IServiceProvider services) =>
        new(binding, services, TenantId, SessionId, TestLogger);

    /// <summary>
    /// Module-boundary harness (ADR-038): real tool, mocked IActionRunner /
    /// IOutputRouter / IScopeResolverService seams, Moq'd concrete
    /// ChatSessionManager (public-virtual GetSessionAsync — the
    /// RecallSessionFileHandlerTests precedent), REAL AiTelemetry (its meter is
    /// observed via MeterListener — no telemetry mock).
    /// </summary>
    private sealed class Harness
    {
        public Binding Binding { get; } = CreateRefusalBinding();
        public string StoredPayloadJson { get; set; } = """{"refusal":"Default stored refusal."}""";
        public string StoredKey { get; private set; } = string.Empty;
        public bool ActionRunnerThrows { get; init; }

        public List<string> CallSequence { get; } = new();
        public Binding? RoutedBinding { get; private set; }
        public ChatSession? RoutedSession { get; private set; }
        public AnalysisAction? RunAction { get; private set; }
        public DocumentText? RunDocumentText { get; private set; }
        public IReadOnlyList<ChatSessionFile> SessionFiles { get; } = Array.Empty<ChatSessionFile>();

        public RefusalCapabilityTool CreateTool()
        {
            var session = new ChatSession(
                SessionId, TenantId, DocumentId: null, PlaybookId: null,
                CreatedAt: DateTimeOffset.UtcNow, LastActivity: DateTimeOffset.UtcNow,
                Messages: Array.Empty<Sprk.Bff.Api.Models.Ai.Chat.ChatMessage>(),
                UploadedFiles: SessionFiles);

            var sessionManager = new Mock<ChatSessionManager>(
                Mock.Of<ITenantCache>(),
                Mock.Of<IChatDataverseRepository>(),
                NullLogger<ChatSessionManager>.Instance,
                null!,
                null!)
            { CallBase = false };
            sessionManager
                .Setup(m => m.GetSessionAsync(TenantId, SessionId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(session);

            var scopeResolver = new Mock<IScopeResolverService>();
            scopeResolver
                .Setup(s => s.GetActionAsync(RefusalActionId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new AnalysisAction
                {
                    Id = RefusalActionId,
                    Name = "Honest Refusal for Chat",
                    SystemPrompt = "{jps}",
                    OutputSchemaJson = """{"type":"object"}""",
                });

            var actionRunner = new Mock<IActionRunner>();
            actionRunner
                .Setup(r => r.RunAsync(
                    It.IsAny<AnalysisAction>(), It.IsAny<DocumentText>(),
                    It.IsAny<LinearRunContext>(), It.IsAny<CancellationToken>()))
                .Returns((AnalysisAction action, DocumentText text, LinearRunContext _, CancellationToken _) =>
                {
                    CallSequence.Add("actionRunner.Run");
                    RunAction = action;
                    RunDocumentText = text;
                    if (ActionRunnerThrows)
                    {
                        throw new InvalidOperationException("executor failure (test)");
                    }

                    using var doc = JsonDocument.Parse("""{"refusal":"RAW executor output"}""");
                    return Task.FromResult(doc.RootElement.Clone());
                });

            var outputRouter = new Mock<IOutputRouter>();
            outputRouter
                .Setup(r => r.RouteAsync(
                    It.IsAny<ChatSession>(), It.IsAny<Binding>(), It.IsAny<JsonElement>(),
                    It.IsAny<IReadOnlyList<string>?>(), It.IsAny<CancellationToken>()))
                .Returns((ChatSession s, Binding b, JsonElement _, IReadOnlyList<string>? _, CancellationToken _) =>
                {
                    CallSequence.Add("outputRouter.Route");
                    RoutedBinding = b;
                    RoutedSession = s;
                    StoredKey = SessionLedger.BuildOutputKey(b.BindingId.ToString(), 1);
                    using var doc = JsonDocument.Parse(StoredPayloadJson);
                    var entry = new SessionOutput
                    {
                        Key = StoredKey,
                        BindingId = b.BindingId.ToString(),
                        UcId = b.Ucid ?? b.ConsumerType,
                        Turn = 1,
                        Disposition = "informational",
                        Payload = doc.RootElement.Clone(),
                        CreatedAt = DateTimeOffset.UtcNow,
                    };
                    return Task.FromResult(new RoutedOutput
                    {
                        Entry = entry,
                        Session = s with { Outputs = new[] { entry } },
                    });
                });

            var services = new ServiceCollection();
            services.AddSingleton(sessionManager.Object);
            services.AddSingleton(scopeResolver.Object);
            services.AddSingleton(actionRunner.Object);
            services.AddSingleton(outputRouter.Object);
            services.AddSingleton<AiTelemetry>();

            return new RefusalCapabilityTool(
                Binding, services.BuildServiceProvider(), TenantId, SessionId, TestLogger);
        }
    }

    /// <summary>
    /// Collects <c>dispatch_refused</c> counter measurements from the
    /// <c>Sprk.Bff.Api.Ai</c> meter — asserting the REAL instrument, not a mock.
    /// </summary>
    private sealed class DispatchRefusedCollector : IDisposable
    {
        public sealed record Measurement(long Value, Dictionary<string, object?> Tags);

        private readonly MeterListener _listener = new();

        public List<Measurement> Measurements { get; } = new();

        public DispatchRefusedCollector()
        {
            _listener.InstrumentPublished = (instrument, listener) =>
            {
                if (instrument.Meter.Name == "Sprk.Bff.Api.Ai" && instrument.Name == "dispatch_refused")
                {
                    listener.EnableMeasurementEvents(instrument);
                }
            };
            _listener.SetMeasurementEventCallback<long>((instrument, value, tags, state) =>
            {
                var dict = new Dictionary<string, object?>();
                foreach (var tag in tags)
                {
                    dict[tag.Key] = tag.Value;
                }

                lock (Measurements)
                {
                    Measurements.Add(new Measurement(value, dict));
                }
            });
            _listener.Start();
        }

        public void Dispose() => _listener.Dispose();
    }
}
