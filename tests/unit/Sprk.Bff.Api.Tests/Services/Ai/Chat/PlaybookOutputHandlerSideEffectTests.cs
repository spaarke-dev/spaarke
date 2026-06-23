using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Sprk.Bff.Api.Api.Ai;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Models.Ai.Chat;
using Sprk.Bff.Api.Services.Ai.Chat;
using Sprk.Bff.Api.Services.Ai.Export;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai.Chat;

/// <summary>
/// Unit tests for the <see cref="PlaybookOutputHandler"/> SideEffect branch
/// (task 051 / FR-14d SideEffect): routing on <see cref="DispatchResult.NodeDestination"/>
/// = <see cref="NodeDestination.SideEffect"/>.
///
/// <para>
/// <b>Acceptance criteria covered (per task 051 POML <c>&lt;acceptance-criteria&gt;</c>)</b>:
/// <list type="bullet">
///   <item>SideEffect case emits telemetry with deterministic IDs + names ONLY (no user-content fields).</item>
///   <item>No SSE event emitted for SideEffect dispatch.</item>
///   <item>No chat token emitted for SideEffect dispatch.</item>
///   <item><b>ADR-015 tier-1 safety</b>: log payload MUST NOT contain
///         <c>userMessage</c> / <c>fileContent</c> / <c>userPrompt</c>.</item>
/// </list>
/// </para>
///
/// <para>
/// <b>Telemetry path</b>: the handler emits the side-effect telemetry via
/// <see cref="ILogger{T}"/> with structured properties (the established telemetry path
/// used by the Dialog / Navigation / Download / Insert / Workspace / Both branches of
/// this handler). No bespoke <c>ITelemetryClient</c> is injected — the ILogger pattern
/// is the de-facto telemetry surface for <c>PlaybookOutputHandler</c>. The mock here
/// captures <c>Log</c> invocations to assert the event name + tier-1-safe properties.
/// </para>
///
/// <para>
/// <b>Placement</b>: co-located with sibling
/// <see cref="PlaybookOutputHandlerWorkspaceCaseTests"/> +
/// <see cref="PlaybookOutputHandlerBothCaseTests"/> +
/// <see cref="PlaybookOutputHandlerFormPrefillTests"/>.
/// </para>
/// </summary>
public class PlaybookOutputHandlerSideEffectTests
{
    private const string TestPlaybookId = "44444444-4444-4444-4444-444444444444";
    private const string TestPlaybookName = "enqueue-document-indexing";
    private const string TestWidgetType = "structured-output-stream";
    private const string SideEffectEventName = "playbook.side_effect_dispatched";
    private const string ChatTokenEventType = "token";
    private const string TypingStartEventType = "typing_start";
    private const string TypingEndEventType = "typing_end";

    // -------------------------------------------------------------------------
    // System under test
    // -------------------------------------------------------------------------

    /// <summary>
    /// Builds the handler with a real (Null) intent detector + docx export, but a
    /// captured <see cref="Mock{T}"/> for the logger so tests can verify telemetry
    /// invocations on the SideEffect arm.
    /// </summary>
    private static (PlaybookOutputHandler Handler, Mock<ILogger<PlaybookOutputHandler>> LoggerMock) CreateHandlerWithMockLogger()
    {
        var intentLogger = NullLogger<CompoundIntentDetector>.Instance;
        var intentDetector = new CompoundIntentDetector(intentLogger);

        var docxLogger = NullLogger<DocxExportService>.Instance;
        var docxOptions = Options.Create(new AnalysisOptions());
        var docxExport = new DocxExportService(docxLogger, docxOptions);

        var loggerMock = new Mock<ILogger<PlaybookOutputHandler>>();
        // Default IsEnabled to true so LogInformation invocations are recorded.
        loggerMock.Setup(l => l.IsEnabled(It.IsAny<LogLevel>())).Returns(true);

        var handler = new PlaybookOutputHandler(intentDetector, docxExport, loggerMock.Object);
        return (handler, loggerMock);
    }

    /// <summary>
    /// Builds a <see cref="DispatchResult"/> shaped like a SideEffect-destination match
    /// (OutputType.Text + NodeDestination.SideEffect). Mirrors the dispatcher state
    /// populated by task 047 from <c>NodeRoutingConfig.Parse(node.ConfigJson)</c> when a
    /// node is configured with <c>destination: "side-effect"</c>.
    /// </summary>
    private static DispatchResult BuildSideEffectDispatch(
        string? playbookId = TestPlaybookId,
        string? playbookName = TestPlaybookName,
        string? widgetType = TestWidgetType)
    {
        return new DispatchResult(
            Matched: true,
            PlaybookId: playbookId,
            PlaybookName: playbookName,
            Confidence: 0.91,
            OutputType: OutputType.Text,
            RequiresConfirmation: false,
            ExtractedParameters: new Dictionary<string, string>(),
            TargetPage: null,
            NodeDestination: NodeDestination.SideEffect,
            WidgetType: widgetType);
    }

    // -------------------------------------------------------------------------
    // Tests
    // -------------------------------------------------------------------------

    [Fact]
    public async Task HandleOutputAsync_SideEffectDestination_EmitsNoSseEvent()
    {
        // Arrange — SideEffect MUST NOT emit any SSE event (no workspace.tab_open, no
        // dialog_open, no navigate, no download metadata, no document_stream_*). The
        // dispatch is observable only via the structured telemetry log event.
        var (handler, _) = CreateHandlerWithMockLogger();
        var emittedEvents = new List<ChatSseEvent>();
        Task EmitSse(ChatSseEvent evt, CancellationToken _) { emittedEvents.Add(evt); return Task.CompletedTask; }
        var dispatch = BuildSideEffectDispatch();

        // Act
        var handled = await handler.HandleOutputAsync(
            dispatch,
            EmitSse,
            hostContext: null,
            CancellationToken.None);

        // Assert
        handled.Should().BeTrue(
            "SideEffect destination is fully handled — caller emits 'done' and returns " +
            "(matches the Workspace / Both / FormPrefill arms' 'handled' contract)");
        emittedEvents.Should().BeEmpty(
            "FR-14d SideEffect: handler emits NO SSE event of any kind — telemetry-only branch");
    }

    [Fact]
    public async Task HandleOutputAsync_SideEffectDestination_EmitsNoChatToken()
    {
        // Arrange — SideEffect MUST NOT produce any chat-surface token (no typing_start,
        // no token, no typing_end). The chat sidebar stays empty.
        var (handler, _) = CreateHandlerWithMockLogger();
        var emittedEvents = new List<ChatSseEvent>();
        Task EmitSse(ChatSseEvent evt, CancellationToken _) { emittedEvents.Add(evt); return Task.CompletedTask; }
        var dispatch = BuildSideEffectDispatch();

        // Act
        await handler.HandleOutputAsync(
            dispatch,
            EmitSse,
            hostContext: null,
            CancellationToken.None);

        // Assert — chat sidebar stays empty for SideEffect destination
        emittedEvents.Should().NotContain(e => e.Type == ChatTokenEventType,
            "FR-14d SideEffect: no chat tokens — chat sidebar stays empty");
        emittedEvents.Should().NotContain(e => e.Type == TypingStartEventType,
            "FR-14d SideEffect: no typing_start — chat surface silent");
        emittedEvents.Should().NotContain(e => e.Type == TypingEndEventType,
            "FR-14d SideEffect: no typing_end — chat surface silent");
    }

    [Fact]
    public async Task HandleOutputAsync_SideEffectDestination_EmitsTelemetryWithTier1SafeFieldsOnly()
    {
        // Arrange — ADR-015 binding: the side-effect telemetry log event MUST carry only
        // deterministic IDs + names (playbookId, playbookName, widgetType). It MUST NOT
        // carry userMessage / fileContent / userPrompt / recall results / JSON payloads.
        // This test captures the formatted log message + structured state and asserts
        // both the presence of the safe fields AND the absence of the forbidden ones.
        var (handler, loggerMock) = CreateHandlerWithMockLogger();
        var emittedEvents = new List<ChatSseEvent>();
        Task EmitSse(ChatSseEvent evt, CancellationToken _) { emittedEvents.Add(evt); return Task.CompletedTask; }
        var dispatch = BuildSideEffectDispatch();

        // Capture the rendered log strings (Moq.It.IsAnyType is the established BFF
        // test-suite pattern for verifying structured ILogger invocations — see
        // DataverseAllowedIndexesProviderTests / DocumentParserRouterTests).
        var capturedLogStates = new List<string>();
        loggerMock
            .Setup(l => l.Log(
                It.IsAny<LogLevel>(),
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()))
            .Callback(new InvocationAction(invocation =>
            {
                var state = invocation.Arguments[2];
                var formatter = invocation.Arguments[4];
                // Invoke the formatter to materialize the structured state into its
                // rendered string. This matches what an ILogger sink would observe.
                var formatted = formatter.GetType()
                    .GetMethod("Invoke")!
                    .Invoke(formatter, new[] { state, null }) as string;
                if (!string.IsNullOrEmpty(formatted))
                    capturedLogStates.Add(formatted);
            }));

        // Act
        await handler.HandleOutputAsync(
            dispatch,
            EmitSse,
            hostContext: null,
            CancellationToken.None);

        // Assert — the side-effect telemetry event was emitted
        var sideEffectLog = capturedLogStates.FirstOrDefault(s => s.Contains(SideEffectEventName));
        sideEffectLog.Should().NotBeNull(
            "task 051 acceptance: handler emits a structured log event named " +
            $"'{SideEffectEventName}' for SideEffect destination dispatches");

        // Assert — tier-1-safe fields are PRESENT in the rendered log
        sideEffectLog!.Should().Contain(TestPlaybookId,
            "ADR-015 tier-1: playbookId (deterministic identifier) is required");
        sideEffectLog.Should().Contain(TestPlaybookName,
            "ADR-015 tier-1: playbookName (deterministic display name) is required");

        // Assert — forbidden tier-2+ fields are ABSENT (ADR-015 binding)
        sideEffectLog.Should().NotContain("userMessage",
            "ADR-015 tier-1 forbids logging userMessage in side-effect telemetry");
        sideEffectLog.Should().NotContain("fileContent",
            "ADR-015 tier-1 forbids logging fileContent in side-effect telemetry");
        sideEffectLog.Should().NotContain("userPrompt",
            "ADR-015 tier-1 forbids logging userPrompt in side-effect telemetry");
    }
}
