using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Sprk.Bff.Api.Api.Ai;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Models.Ai.Chat;
using Sprk.Bff.Api.Services.Ai.Chat;
using Sprk.Bff.Api.Services.Ai.Export;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai.Chat;

/// <summary>
/// Unit tests for the <see cref="PlaybookOutputHandler"/> FormPrefill branch
/// (task 050 / FR-14d FormPrefill): routing on <see cref="DispatchResult.NodeDestination"/>
/// = <see cref="NodeDestination.FormPrefill"/>.
///
/// <para>
/// <b>Acceptance criteria covered (per task 050 POML <c>&lt;acceptance-criteria&gt;</c>)</b>:
/// <list type="bullet">
///   <item>FormPrefill case explicitly handled (not default fallthrough).</item>
///   <item>No SSE event emitted for FormPrefill dispatch.</item>
///   <item>No chat token emitted for FormPrefill dispatch.</item>
/// </list>
/// The pre-fill flow regression (<c>useAiPrefill</c> end-to-end) is owned by its own test
/// suite — this file asserts the handler-side no-op contract per NFR-07 (the handler MUST
/// NOT modify the pre-fill flow; the pre-fill flow is the consumer that produces the
/// form-prefill output via its own pipeline).
/// </para>
///
/// <para>
/// <b>Placement</b>: co-located with sibling
/// <see cref="PlaybookOutputHandlerWorkspaceCaseTests"/> +
/// <see cref="PlaybookOutputHandlerBothCaseTests"/> in the unit-test project (per
/// task 048/049 placement convention — the integration test project lacks a handler
/// harness).
/// </para>
/// </summary>
public class PlaybookOutputHandlerFormPrefillTests
{
    private const string TestPlaybookId = "33333333-3333-3333-3333-333333333333";
    private const string TestPlaybookName = "matter-pre-fill";
    private const string ChatTokenEventType = "token";
    private const string TypingStartEventType = "typing_start";
    private const string TypingEndEventType = "typing_end";
    private const string WorkspaceTabOpenEventType = "workspace.tab_open";

    // -------------------------------------------------------------------------
    // System under test
    // -------------------------------------------------------------------------

    private static PlaybookOutputHandler CreateHandler()
    {
        var intentLogger = NullLogger<CompoundIntentDetector>.Instance;
        var intentDetector = new CompoundIntentDetector(intentLogger);

        var docxLogger = NullLogger<DocxExportService>.Instance;
        var docxOptions = Options.Create(new AnalysisOptions());
        var docxExport = new DocxExportService(docxLogger, docxOptions);

        var handlerLogger = NullLogger<PlaybookOutputHandler>.Instance;
        return new PlaybookOutputHandler(intentDetector, docxExport, handlerLogger);
    }

    /// <summary>
    /// Builds a <see cref="DispatchResult"/> shaped like a FormPrefill-destination match
    /// (OutputType.Text + NodeDestination.FormPrefill). Mirrors the dispatcher state
    /// populated by task 047 from <c>NodeRoutingConfig.Parse(node.ConfigJson)</c> when a
    /// node is configured with <c>destination: "form-prefill"</c>.
    /// </summary>
    private static DispatchResult BuildFormPrefillDispatch()
    {
        return new DispatchResult(
            Matched: true,
            PlaybookId: TestPlaybookId,
            PlaybookName: TestPlaybookName,
            Confidence: 0.94,
            OutputType: OutputType.Text,
            RequiresConfirmation: false,
            ExtractedParameters: new Dictionary<string, string>(),
            TargetPage: null,
            NodeDestination: NodeDestination.FormPrefill,
            WidgetType: null);
    }

    // -------------------------------------------------------------------------
    // Tests
    // -------------------------------------------------------------------------

    [Fact]
    public async Task HandleOutputAsync_FormPrefillDestination_EmitsNoSseEvent()
    {
        // Arrange — FormPrefill is intentionally a no-op preserve per NFR-07. The pre-fill
        // flow (MatterPreFillService / ProjectPreFillService) is the consumer; this handler
        // MUST NOT produce any SSE event (the pre-fill flow has its own pipeline).
        var handler = CreateHandler();
        var emittedEvents = new List<ChatSseEvent>();
        Task EmitSse(ChatSseEvent evt, CancellationToken _) { emittedEvents.Add(evt); return Task.CompletedTask; }
        var dispatch = BuildFormPrefillDispatch();

        // Act
        var handled = await handler.HandleOutputAsync(
            dispatch,
            EmitSse,
            hostContext: null,
            CancellationToken.None);

        // Assert
        handled.Should().BeTrue(
            "FormPrefill destination is fully handled — caller emits 'done' and returns. " +
            "(The arm exists for switch completeness; without it the dispatch would fall " +
            "through to the OutputType arms or hit the default branch — both incorrect.)");
        emittedEvents.Should().BeEmpty(
            "FR-14d FormPrefill + NFR-07: handler emits NO SSE event of any kind. " +
            "Pre-fill flow (MatterPreFillService / ProjectPreFillService) is the consumer.");
        emittedEvents.Should().NotContain(e => e.Type == WorkspaceTabOpenEventType,
            "FormPrefill MUST NOT emit workspace.tab_open — that belongs to the Workspace / Both arms");
    }

    [Fact]
    public async Task HandleOutputAsync_FormPrefillDestination_EmitsNoChatToken()
    {
        // Arrange — FormPrefill MUST NOT produce any chat-surface token (no typing_start,
        // no token, no typing_end). The chat sidebar stays empty.
        var handler = CreateHandler();
        var emittedEvents = new List<ChatSseEvent>();
        Task EmitSse(ChatSseEvent evt, CancellationToken _) { emittedEvents.Add(evt); return Task.CompletedTask; }
        var dispatch = BuildFormPrefillDispatch();

        // Act
        await handler.HandleOutputAsync(
            dispatch,
            EmitSse,
            hostContext: null,
            CancellationToken.None);

        // Assert — chat sidebar stays empty for FormPrefill destination
        emittedEvents.Should().NotContain(e => e.Type == ChatTokenEventType,
            "FR-14d FormPrefill: no chat tokens — chat sidebar stays empty");
        emittedEvents.Should().NotContain(e => e.Type == TypingStartEventType,
            "FR-14d FormPrefill: no typing_start — chat surface silent");
        emittedEvents.Should().NotContain(e => e.Type == TypingEndEventType,
            "FR-14d FormPrefill: no typing_end — chat surface silent");
    }
}
