using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Sprk.Bff.Api.Models.Ai.Chat;
using Sprk.Bff.Api.Services.Ai.Chat;
using Sprk.Bff.Api.Services.Ai.PublicContracts;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai.Chat;

/// <summary>
/// G-P3 UAT round-1 H1 — projection RESILIENCE through the real
/// <see cref="SprkChatAgentFactory.CreateAgentAsync"/> path: a Binding whose
/// Action carries an invalid input schema (the exact UAT payload —
/// property-level <c>"required": true</c>) is EXCLUDED from the tool
/// projection with a loud Error log, while every valid Binding still
/// projects. Before this fix the invalid schema rode into the LLM request and
/// Azure OpenAI 400-failed EVERY text-path turn
/// (<c>invalid_function_parameters</c> rejects the whole request).
/// </summary>
/// <remarks>
/// Harness pattern: same CreateAgentAsync boundary as
/// <see cref="SprkChatAgentFactoryDedupTests"/> (integration scaffold for chat
/// sessions does not exist; the factory boundary is the projection seam). The
/// projected tool set is observed through the PUBLIC capability_change SSE
/// contract (previousTurnToolNames = empty ⇒ one "available" event per
/// projected tool).
/// </remarks>
public class SprkChatAgentFactoryInvalidSchemaProjectionTests
{
    private const string TenantId = "tenant-h1";
    private const string SessionId = "session-h1";
    private const string DocumentId = "doc-h1";

    /// <summary>The exact G-P3 UAT payload (property-level "required": true).</summary>
    private const string InvalidCreateTaskSchema =
        """{"type":"object","properties":{"due_date":{"type":"string","required":true,"elicitation_prompt":"What's the due date for this task?"},"assign_to":{"type":"string","required":true}},"required":["due_date","assign_to"]}""";

    private const string ValidSummarizeSchema =
        """{"type":"object","properties":{"fileIds":{"type":"array","items":{"type":"string"}}}}""";

    [Fact]
    public async Task CreateAgentAsync_BindingWithInvalidInputSchema_IsExcludedWhileValidBindingsProject()
    {
        // Arrange — two enabled, text-projectable Bindings: one valid, one carrying
        // the exact schema that took down G-P3 round 1.
        var validBinding = MakeBinding("chat-summarize", ValidSummarizeSchema);
        var invalidBinding = MakeBinding("create-task", InvalidCreateTaskSchema);

        var routing = new Mock<IConsumerRoutingService>();
        routing
            .Setup(r => r.ListTextProjectableBindingsAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { validBinding, invalidBinding });

        var loggerMock = new Mock<ILogger<SprkChatAgentFactory>>();
        var services = BuildServiceProvider(routing.Object);
        var factory = new SprkChatAgentFactory(
            Mock.Of<IChatClient>(), services, loggerMock.Object);

        var sseEvents = new List<Sprk.Bff.Api.Api.Ai.ChatSseEvent>();

        // Act — previousTurnToolNames empty ⇒ every projected tool emits one
        // capability_change "available" event (the public projection observation seam).
        var agent = await factory.CreateAgentAsync(
            SessionId, DocumentId, playbookId: null, TenantId,
            sseWriter: (evt, _) => { sseEvents.Add(evt); return Task.CompletedTask; },
            previousTurnToolNames: Array.Empty<string>());

        // Assert — the agent exists (NFR-01: the turn still works) and the projected
        // set contains the VALID capability but NOT the invalid one.
        agent.Should().NotBeNull("one malformed catalog row must never take down the loop");

        var projectedCapabilities = sseEvents
            .Where(e => e.Type == "capability_change")
            .Select(e => JsonSerializer.Serialize(e.Data))
            .Where(json => json.Contains("\"available\""))
            .ToList();

        projectedCapabilities.Should().Contain(json => json.Contains("capability_chat-summarize"),
            "the valid Binding still projects — exclusion is per-row, never whole-catalog");
        projectedCapabilities.Should().NotContain(json => json.Contains("capability_create-task"),
            "the invalid-schema Binding is EXCLUDED so its schema never reaches OpenAI " +
            "(where it would 400 the ENTIRE request, not just its own tool)");

        // Assert — the exclusion is LOUD (Error level, names the row + the schema error).
        loggerMock.Verify(
            l => l.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((state, _) =>
                    state.ToString()!.Contains("invalid-tool-schema") &&
                    state.ToString()!.Contains("create-task") &&
                    state.ToString()!.Contains("required")),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once,
            "a silently missing capability is undiagnosable — the exclusion must log at Error " +
            "with the binding identifier and the first validation error (NFR-07: identifiers only)");
    }

    [Fact]
    public async Task CreateAgentAsync_AllBindingSchemasValid_NothingExcluded()
    {
        var routing = new Mock<IConsumerRoutingService>();
        routing
            .Setup(r => r.ListTextProjectableBindingsAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[]
            {
                MakeBinding("chat-summarize", ValidSummarizeSchema),
                MakeBinding("chat-classify", inputSchemaJson: null), // schema-less → default schema
            });

        var loggerMock = new Mock<ILogger<SprkChatAgentFactory>>();
        var services = BuildServiceProvider(routing.Object);
        var factory = new SprkChatAgentFactory(
            Mock.Of<IChatClient>(), services, loggerMock.Object);

        var sseEvents = new List<Sprk.Bff.Api.Api.Ai.ChatSseEvent>();

        await factory.CreateAgentAsync(
            SessionId, DocumentId, playbookId: null, TenantId,
            sseWriter: (evt, _) => { sseEvents.Add(evt); return Task.CompletedTask; },
            previousTurnToolNames: Array.Empty<string>());

        var available = sseEvents
            .Where(e => e.Type == "capability_change")
            .Select(e => JsonSerializer.Serialize(e.Data))
            .ToList();
        available.Should().Contain(json => json.Contains("capability_chat-summarize"));
        available.Should().Contain(json => json.Contains("capability_chat-classify"),
            "a null schema projects the safe default schema — valid, not an exclusion");

        loggerMock.Verify(
            l => l.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((state, _) => state.ToString()!.Contains("invalid-tool-schema")),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Never,
            "valid catalogs must not produce exclusion noise");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // G-P3 UAT round-1 H6(a) — side-effect honesty directive
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateAgentAsync_ToolsProjected_AppendsSideEffectHonestyDirective()
    {
        // Incident (2026-07-07, session b3c5340c…): the model role-played the full
        // create-task flow ("drafted" → "has now been created") WITHOUT invoking any
        // tool — no dialog, no record. The H6 directive pins the honesty contract
        // whenever tools project.
        var routing = new Mock<IConsumerRoutingService>();
        routing
            .Setup(r => r.ListTextProjectableBindingsAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { MakeBinding("chat-summarize", ValidSummarizeSchema) });

        var services = BuildServiceProvider(routing.Object);
        var factory = new SprkChatAgentFactory(
            Mock.Of<IChatClient>(), services, Mock.Of<ILogger<SprkChatAgentFactory>>());

        var agent = await factory.CreateAgentAsync(
            SessionId, DocumentId, playbookId: null, TenantId);

        agent.Context.SystemPrompt.Should().Contain("## Action Honesty",
            "tool-bearing sessions must carry the H6 honesty contract");
        agent.Context.SystemPrompt.Should().Contain(
            "NEVER state that a record, task, draft, email, or any change was created",
            "the fabricated-write ban is the directive's core clause");
        agent.Context.SystemPrompt.Should().Contain(
            "does NOT create anything by itself",
            "a user's 'yes create it' on a conversational proposal must still route through the tool");

        // G-P3 UAT round-2 extensions (2026-07-07):
        agent.Context.SystemPrompt.Should().Contain(
            "only GENERATE draft content",
            "R2-B: every round-2 fabrication correlated with a capability_* DRAFTING call — " +
            "the directive must pin the generation/execution split");
        agent.Context.SystemPrompt.Should().Contain(
            "tab, view, editor, workspace, or dialog",
            "R2-D: the model claimed UI surfaces were opened without any confirming tool result");

        // G-P3 UAT round-3 extensions (2026-07-07):
        agent.Context.SystemPrompt.Should().Contain(
            "AT MOST ONCE per action",
            "R3-1: the model confirm-looped four times in chat without ever invoking the write " +
            "tool — the directive must cap chat confirmation at one and bridge confirmed→invoke");
        agent.Context.SystemPrompt.Should().Contain(
            "NEVER re-run a capability_* drafting tool instead of invoking the write tool",
            "R3-1: every round-3 confirm-loop turn re-invoked capability_create-task (drafting) " +
            "instead of dataverse.create_record");
        agent.Context.SystemPrompt.Should().Contain(
            "resolve each reference to its record GUID FIRST",
            "R3-2: the model proposed a create carrying an unresolved person lookup — lookups " +
            "must be resolved BEFORE proposing, not discovered as failures after the user confirms");

        // G-P3 UAT round-4 extension (2026-07-07):
        agent.Context.SystemPrompt.Should().Contain(
            "NEVER compose, guess, or reconstruct record URLs",
            "R4-3(b): asked 'do you have a link?', the model invented a /WebResources/tables/… " +
            "URL — confirmed actions now carry a real [Open record] link in the transcript; the " +
            "directive pins relay-only");

        // G-P3 UAT round-5 extension (2026-07-07):
        agent.Context.SystemPrompt.Should().Contain(
            "WITHOUT naming the record type, do NOT guess the table",
            "R5-E: 'create a record from this document' — the model guessed sprk_document (the " +
            "user meant a matter) and created an orphan fileless row; entity-ambiguous create " +
            "requests are clarified in the same turn, never guessed");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // G-P3 UAT round-5 R5-A — current-date context (2026-07-07)
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateAgentAsync_SystemPrompt_CarriesCurrentDateContext()
    {
        // Incident: "due date tomorrow" produced 6/13/2024 — the model has no clock
        // and hallucinated the YEAR. The date line is UNCONDITIONAL (relative dates
        // matter with or without tools) and sits at a stable end-of-prompt position.
        var services = BuildServiceProvider(routingService: null);
        var factory = new SprkChatAgentFactory(
            Mock.Of<IChatClient>(), services, Mock.Of<ILogger<SprkChatAgentFactory>>());

        var agent = await factory.CreateAgentAsync(
            SessionId, DocumentId, playbookId: null, TenantId);

        agent.Context.SystemPrompt.Should().Contain("## Current Date");
        agent.Context.SystemPrompt.Should().Contain("Today's date is ");
        agent.Context.SystemPrompt.Should().Contain("never guess the year");
    }

    // Task 053 (FR-B-04): BuildCurrentDateDirective_FormatsUtcDateDeterministically moved to
    // ContextSliceProducersTests (the current-date directive moved to
    // ContextSliceProducers.EnvironmentFactsProducer). The CreateAgentAsync integration assertion
    // above still pins that the directive is appended to the live system prompt (bytes unchanged).

    [Fact]
    public async Task CreateAgentAsync_ZeroToolsProjected_OmitsSideEffectHonestyDirective()
    {
        // No routing service and no tool catalog registered → zero tools → the
        // directive (which instructs the model to invoke tools) must not render.
        var services = BuildServiceProvider(routingService: null);
        var factory = new SprkChatAgentFactory(
            Mock.Of<IChatClient>(), services, Mock.Of<ILogger<SprkChatAgentFactory>>());

        var agent = await factory.CreateAgentAsync(
            SessionId, DocumentId, playbookId: null, TenantId);

        agent.Context.SystemPrompt.Should().NotContain("## Action Honesty",
            "a zero-tool conversational session has no tools the directive could refer to");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────────

    private static Binding MakeBinding(string consumerType, string? inputSchemaJson) => new()
    {
        BindingId = Guid.NewGuid(),
        ConsumerType = consumerType,
        ActionId = Guid.NewGuid(),
        ToolDescription = $"Maker-authored intent surface for {consumerType}.",
        Surfaces = new[] { "assistant" },
        InputSchemaJson = inputSchemaJson,
    };

    private static ServiceProvider BuildServiceProvider(IConsumerRoutingService? routingService)
    {
        var services = new ServiceCollection();

        var ctx = new ChatContext(
            SystemPrompt: "You are an analyst.",
            DocumentSummary: null,
            AnalysisMetadata: null,
            PlaybookId: null);
        var contextProvider = new Mock<IChatContextProvider>();
        contextProvider
            .Setup(p => p.GetContextAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Guid?>(),
                It.IsAny<ChatHostContext?>(), It.IsAny<IReadOnlyList<string>?>(),
                It.IsAny<IReadOnlyList<ChatSessionFile>?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ctx);

        services.AddSingleton(Mock.Of<IChatClient>());
        services.AddScoped(_ => contextProvider.Object);
        if (routingService is not null)
        {
            services.AddScoped(_ => routingService);
        }
        services.AddLogging();

        return services.BuildServiceProvider();
    }
}
