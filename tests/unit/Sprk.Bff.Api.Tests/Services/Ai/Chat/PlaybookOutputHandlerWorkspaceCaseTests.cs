using System.Reflection;
using System.Text.Json;
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
/// Unit tests for the <see cref="PlaybookOutputHandler"/> Workspace branch
/// (task 048 / FR-14d): routing on <see cref="DispatchResult.NodeDestination"/>
/// = <see cref="NodeDestination.Workspace"/>.
///
/// <para>
/// <b>Acceptance criteria covered (per task 048 POML <c>&lt;acceptance-criteria&gt;</c>)</b>:
/// <list type="bullet">
///   <item>Workspace destination dispatch emits <c>workspace.tab_open</c> SSE event from the handler.</item>
///   <item>No chat tokens produced for Workspace destination (chat sidebar stays empty).</item>
///   <item><see cref="DispatchResult.WidgetType"/> is carried on the emitted event.</item>
///   <item>Null <see cref="DispatchResult.WidgetType"/> falls back to <c>"structured-output-stream"</c>
///         (matching <c>STRUCTURED_OUTPUT_STREAM_WIDGET_TYPE</c> in
///         <c>register-structured-output-stream-widget.ts</c>).</item>
/// </list>
/// </para>
///
/// <para>
/// <b>Placement</b>: co-located with sibling <see cref="PlaybookDispatcherDestinationTests"/>
/// in the unit-test project (per task 047 placement convention — the integration test
/// project lacks a handler harness, and the "integration" tests run with all mocked deps
/// in the unit project).
/// </para>
///
/// <para>
/// <b>Note re streaming delegation (ADR-033)</b>: the handler emits the STRUCTURAL
/// <c>workspace.tab_open</c> event ONLY. The per-field
/// <c>FieldDelta</c> streaming continues via the existing
/// <c>PlaybookExecutionEngine.ExecuteChatSummarizeAsync</c> SSE path on the
/// <c>/api/ai/chat/sessions/{id}/summarize</c> endpoint (consumed by
/// <c>sseToPaneEventBridge</c> on the frontend) — NOT proxied through this handler. These
/// tests therefore assert the <c>tab_open</c> emission + chat-token suppression contract;
/// the streaming path is owned + tested by <see cref="PlaybookExecutionEngine"/>'s own
/// test suite.
/// </para>
/// </summary>
public class PlaybookOutputHandlerWorkspaceCaseTests
{
    private const string TestPlaybookId = "11111111-1111-1111-1111-111111111111";
    private const string TestPlaybookName = "summarize-document-for-workspace";
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
    /// Builds a <see cref="DispatchResult"/> shaped like the
    /// <c>summarize-document-for-workspace</c> match (OutputType.Text + NodeDestination.Workspace).
    /// Mirrors the dispatcher state populated by task 047 from
    /// <c>NodeRoutingConfig.Parse(node.ConfigJson)</c>.
    /// </summary>
    private static DispatchResult BuildWorkspaceDispatch(
        string? widgetType = "structured-output-stream",
        IReadOnlyDictionary<string, string>? extractedParameters = null)
    {
        return new DispatchResult(
            Matched: true,
            PlaybookId: TestPlaybookId,
            PlaybookName: TestPlaybookName,
            Confidence: 0.92,
            OutputType: OutputType.Text,           // Workspace playbooks carry OutputType.Text
            RequiresConfirmation: false,
            ExtractedParameters: extractedParameters is null
                ? new Dictionary<string, string>()
                : new Dictionary<string, string>(extractedParameters),
            TargetPage: null,
            NodeDestination: NodeDestination.Workspace,
            WidgetType: widgetType);
    }

    // -------------------------------------------------------------------------
    // Tests
    // -------------------------------------------------------------------------

    [Fact]
    public async Task HandleOutputAsync_WorkspaceDestination_EmitsTabOpenSseEvent()
    {
        // Arrange
        var handler = CreateHandler();
        var emittedEvents = new List<ChatSseEvent>();
        Task EmitSse(ChatSseEvent evt, CancellationToken _) { emittedEvents.Add(evt); return Task.CompletedTask; }
        var dispatch = BuildWorkspaceDispatch();

        // Act
        var handled = await handler.HandleOutputAsync(
            dispatch,
            EmitSse,
            hostContext: null,
            CancellationToken.None);

        // Assert
        handled.Should().BeTrue("Workspace destination is fully handled — caller emits 'done' and returns");
        emittedEvents.Should().ContainSingle(e => e.Type == WorkspaceTabOpenEventType,
            "FR-14d acceptance: handler emits exactly one workspace.tab_open SSE event");

        var tabOpenEvent = emittedEvents.Single(e => e.Type == WorkspaceTabOpenEventType);
        tabOpenEvent.Content.Should().BeNull("workspace.tab_open carries structured Data, not Content");
        tabOpenEvent.Data.Should().NotBeNull("payload must carry { tabId, widgetType, playbookId }");

        var (tabId, widgetType, playbookId) = ExtractTabOpenPayload(tabOpenEvent.Data!);
        tabId.Should().NotBeNullOrWhiteSpace("FR-14d: tabId is required for frontend mount correlation");
        widgetType.Should().Be("structured-output-stream",
            "FR-14d: widgetType is propagated from DispatchResult.WidgetType");
        playbookId.Should().Be(TestPlaybookId, "playbookId is included for frontend correlation + telemetry");
    }

    [Fact]
    public async Task HandleOutputAsync_WorkspaceDestination_DoesNotEmitChatTokens()
    {
        // Arrange
        var handler = CreateHandler();
        var emittedEvents = new List<ChatSseEvent>();
        Task EmitSse(ChatSseEvent evt, CancellationToken _) { emittedEvents.Add(evt); return Task.CompletedTask; }
        var dispatch = BuildWorkspaceDispatch();

        // Act
        await handler.HandleOutputAsync(
            dispatch,
            EmitSse,
            hostContext: null,
            CancellationToken.None);

        // Assert — chat sidebar stays empty for Workspace destination
        emittedEvents.Should().NotContain(e => e.Type == ChatTokenEventType,
            "FR-14d acceptance: no chat tokens for Workspace destination — chat sidebar stays empty");
        emittedEvents.Should().NotContain(e => e.Type == TypingStartEventType,
            "FR-14d acceptance: no typing_start for Workspace destination (chat surface silent)");
        emittedEvents.Should().NotContain(e => e.Type == TypingEndEventType,
            "FR-14d acceptance: no typing_end for Workspace destination (chat surface silent)");
    }

    [Fact]
    public async Task HandleOutputAsync_WorkspaceDestination_NullWidgetType_UsesDefaultStreamingWidget()
    {
        // Arrange — defensive fallback: a Workspace-destination playbook with null WidgetType
        // (an edge case that NodeRoutingConfig.Validate catches at author time, but the
        // runtime handler MUST degrade gracefully).
        var handler = CreateHandler();
        var emittedEvents = new List<ChatSseEvent>();
        Task EmitSse(ChatSseEvent evt, CancellationToken _) { emittedEvents.Add(evt); return Task.CompletedTask; }
        var dispatch = BuildWorkspaceDispatch(widgetType: null);

        // Act
        var handled = await handler.HandleOutputAsync(
            dispatch,
            EmitSse,
            hostContext: null,
            CancellationToken.None);

        // Assert
        handled.Should().BeTrue();
        var tabOpenEvent = emittedEvents.Single(e => e.Type == WorkspaceTabOpenEventType);
        var (_, widgetType, _) = ExtractTabOpenPayload(tabOpenEvent.Data!);
        widgetType.Should().Be("structured-output-stream",
            "task 048 runtime fallback: null WidgetType defaults to the canonical streaming widget key " +
            "(matches STRUCTURED_OUTPUT_STREAM_WIDGET_TYPE in register-structured-output-stream-widget.ts)");
    }

    [Fact]
    public async Task HandleOutputAsync_WorkspaceDestination_PrefersExtractedWorkspaceTabIdParameter()
    {
        // Arrange — the playbook's parameterSchema.workspaceTabId is populated by the LLM
        // Stage 2 refinement (e.g., user references an existing tab they want updated). The
        // handler MUST honor this so the frontend updates the existing tab rather than
        // opening a new one (per summarize-document-for-workspace.playbook.json:44-47).
        var handler = CreateHandler();
        var emittedEvents = new List<ChatSseEvent>();
        Task EmitSse(ChatSseEvent evt, CancellationToken _) { emittedEvents.Add(evt); return Task.CompletedTask; }

        const string ExistingTabId = "existing-tab-abc123";
        var dispatch = BuildWorkspaceDispatch(
            extractedParameters: new Dictionary<string, string>
            {
                ["workspaceTabId"] = ExistingTabId,
            });

        // Act
        await handler.HandleOutputAsync(
            dispatch,
            EmitSse,
            hostContext: null,
            CancellationToken.None);

        // Assert
        var tabOpenEvent = emittedEvents.Single(e => e.Type == WorkspaceTabOpenEventType);
        var (tabId, _, _) = ExtractTabOpenPayload(tabOpenEvent.Data!);
        tabId.Should().Be(ExistingTabId,
            "the dispatch's workspaceTabId extracted parameter MUST be reused so the frontend " +
            "updates the existing tab rather than allocating a fresh GUID");
    }

    [Fact]
    public async Task HandleOutputAsync_WorkspaceDestination_AllocatesFreshTabIdWhenParameterAbsent()
    {
        // Arrange
        var handler = CreateHandler();
        var emittedEvents = new List<ChatSseEvent>();
        Task EmitSse(ChatSseEvent evt, CancellationToken _) { emittedEvents.Add(evt); return Task.CompletedTask; }
        var dispatch = BuildWorkspaceDispatch();   // empty parameters

        // Act
        await handler.HandleOutputAsync(
            dispatch,
            EmitSse,
            hostContext: null,
            CancellationToken.None);

        // Assert — a fresh GUID (N format) is allocated when the dispatch lacks workspaceTabId
        var tabOpenEvent = emittedEvents.Single(e => e.Type == WorkspaceTabOpenEventType);
        var (tabId, _, _) = ExtractTabOpenPayload(tabOpenEvent.Data!);
        tabId.Should().NotBeNullOrWhiteSpace();
        Guid.TryParseExact(tabId, "N", out _).Should().BeTrue(
            "FR-14d default: fresh GUID in N format when the dispatch has no workspaceTabId parameter");
    }

    [Fact]
    public async Task HandleOutputAsync_ChatDestination_StillFallsThroughToOutputTypeSwitch()
    {
        // Arrange — backward-compat: a Chat-destination + Text-output match returns false so
        // the caller continues to standard streaming. The task 048 NodeDestination switch
        // MUST NOT regress this pre-R6 flow.
        var handler = CreateHandler();
        var emittedEvents = new List<ChatSseEvent>();
        Task EmitSse(ChatSseEvent evt, CancellationToken _) { emittedEvents.Add(evt); return Task.CompletedTask; }
        var chatDispatch = new DispatchResult(
            Matched: true,
            PlaybookId: TestPlaybookId,
            PlaybookName: "summarize-document-for-chat",
            Confidence: 0.93,
            OutputType: OutputType.Text,
            RequiresConfirmation: false,
            ExtractedParameters: new Dictionary<string, string>(),
            TargetPage: null,
            NodeDestination: NodeDestination.Chat,
            WidgetType: null);

        // Act
        var handled = await handler.HandleOutputAsync(
            chatDispatch,
            EmitSse,
            hostContext: null,
            CancellationToken.None);

        // Assert
        handled.Should().BeFalse(
            "FR-14b backward-compat: Chat destination + Text output falls through to standard streaming " +
            "(handler returns false — caller proceeds with normal token emission)");
        emittedEvents.Should().BeEmpty("HandleTextOutputAsync emits nothing on the Text branch");
    }

    // -------------------------------------------------------------------------
    // Payload extraction helper — the WorkspaceTabOpenSseData record is internal,
    // accessed via reflection so the test does not need an InternalsVisibleTo
    // attribute for this single test file.
    // -------------------------------------------------------------------------

    private static (string TabId, string WidgetType, string? PlaybookId) ExtractTabOpenPayload(object payload)
    {
        var payloadType = payload.GetType();
        payloadType.Name.Should().Be("WorkspaceTabOpenSseData",
            "the workspace.tab_open event payload MUST be the typed WorkspaceTabOpenSseData record");

        string ReadStringProp(string name)
        {
            var prop = payloadType.GetProperty(name, BindingFlags.Instance | BindingFlags.Public);
            prop.Should().NotBeNull($"WorkspaceTabOpenSseData must declare {name}");
            var value = prop!.GetValue(payload);
            return value as string ?? string.Empty;
        }

        string? ReadNullableStringProp(string name)
        {
            var prop = payloadType.GetProperty(name, BindingFlags.Instance | BindingFlags.Public);
            prop.Should().NotBeNull($"WorkspaceTabOpenSseData must declare {name}");
            return prop!.GetValue(payload) as string;
        }

        return (
            TabId: ReadStringProp("TabId"),
            WidgetType: ReadStringProp("WidgetType"),
            PlaybookId: ReadNullableStringProp("PlaybookId"));
    }
}
