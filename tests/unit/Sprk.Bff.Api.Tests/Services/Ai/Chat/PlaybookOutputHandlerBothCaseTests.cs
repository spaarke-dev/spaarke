using System.Reflection;
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
/// Unit tests for the <see cref="PlaybookOutputHandler"/> Both branch
/// (task 049 / FR-14d Both): routing on <see cref="DispatchResult.NodeDestination"/>
/// = <see cref="NodeDestination.Both"/>.
///
/// <para>
/// <b>Acceptance criteria covered (per task 049 POML <c>&lt;acceptance-criteria&gt;</c>)</b>:
/// <list type="bullet">
///   <item>Both-destination dispatch emits BOTH <c>workspace.tab_open</c> SSE AND a chat ack token.</item>
///   <item>Ack content: <c>"I've added a {playbookName} result to the Workspace."</c>
///         (with substituted playbook name, hardcoded English per design §1.3).</item>
///   <item>Defensive fallback: when <see cref="DispatchResult.PlaybookName"/> is null/whitespace,
///         the ack uses the literal word <c>"playbook"</c> (never <c>"null"</c> / empty).</item>
///   <item>Sequence: <c>workspace.tab_open</c> emitted BEFORE the chat ack (per POML step 3 —
///         frontend mounts widget BEFORE the chat token surfaces).</item>
/// </list>
/// </para>
///
/// <para>
/// <b>Placement</b>: co-located with sibling <see cref="PlaybookOutputHandlerWorkspaceCaseTests"/>
/// in the unit-test project (per task 048 placement convention — the integration test
/// project lacks a handler harness, and the "integration" tests run with all mocked deps
/// in the unit project).
/// </para>
///
/// <para>
/// <b>Streaming delegation (ADR-033)</b>: the handler emits the STRUCTURAL
/// <c>workspace.tab_open</c> event + the chat ack token. The per-field <c>FieldDelta</c>
/// streaming continues via the existing
/// <c>PlaybookExecutionEngine.ExecuteChatSummarizeAsync</c> SSE path on the
/// <c>/api/ai/chat/sessions/{id}/summarize</c> endpoint (consumed by
/// <c>sseToPaneEventBridge</c> on the frontend) — NOT proxied through this handler.
/// These tests therefore assert the structural + ack emission contract; the streaming
/// path is owned + tested by <see cref="PlaybookExecutionEngine"/>'s own test suite.
/// </para>
/// </summary>
public class PlaybookOutputHandlerBothCaseTests
{
    private const string TestPlaybookId = "22222222-2222-2222-2222-222222222222";
    private const string TestPlaybookName = "Summarize NDA";
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
    /// Builds a <see cref="DispatchResult"/> shaped like a Both-destination match
    /// (OutputType.Text + NodeDestination.Both). Mirrors the dispatcher state populated
    /// by task 047 from <c>NodeRoutingConfig.Parse(node.ConfigJson)</c> when a node is
    /// configured with <c>destination: "Both"</c>.
    /// </summary>
    private static DispatchResult BuildBothDispatch(
        string? playbookName = TestPlaybookName,
        string? widgetType = "structured-output-stream",
        IReadOnlyDictionary<string, string>? extractedParameters = null)
    {
        return new DispatchResult(
            Matched: true,
            PlaybookId: TestPlaybookId,
            PlaybookName: playbookName,
            Confidence: 0.92,
            OutputType: OutputType.Text,           // Both playbooks typically carry OutputType.Text
            RequiresConfirmation: false,
            ExtractedParameters: extractedParameters is null
                ? new Dictionary<string, string>()
                : new Dictionary<string, string>(extractedParameters),
            TargetPage: null,
            NodeDestination: NodeDestination.Both,
            WidgetType: widgetType);
    }

    // -------------------------------------------------------------------------
    // Tests
    // -------------------------------------------------------------------------

    [Fact]
    public async Task HandleOutputAsync_BothDestination_EmitsTabOpenSseEvent()
    {
        // Arrange
        var handler = CreateHandler();
        var emittedEvents = new List<ChatSseEvent>();
        Task EmitSse(ChatSseEvent evt, CancellationToken _) { emittedEvents.Add(evt); return Task.CompletedTask; }
        var dispatch = BuildBothDispatch();

        // Act
        var handled = await handler.HandleOutputAsync(
            dispatch,
            EmitSse,
            hostContext: null,
            CancellationToken.None);

        // Assert
        handled.Should().BeTrue("Both destination is fully handled — caller emits 'done' and returns");
        emittedEvents.Should().ContainSingle(e => e.Type == WorkspaceTabOpenEventType,
            "FR-14d Both acceptance: handler emits exactly one workspace.tab_open SSE event " +
            "(same as the Workspace branch — DRY reuse of EmitWorkspaceTabOpenAndStreamAsync)");

        var tabOpenEvent = emittedEvents.Single(e => e.Type == WorkspaceTabOpenEventType);
        tabOpenEvent.Content.Should().BeNull("workspace.tab_open carries structured Data, not Content");
        tabOpenEvent.Data.Should().NotBeNull("payload must carry { tabId, widgetType, playbookId }");

        var (tabId, widgetType, playbookId) = ExtractTabOpenPayload(tabOpenEvent.Data!);
        tabId.Should().NotBeNullOrWhiteSpace("FR-14d: tabId is required for frontend mount correlation");
        widgetType.Should().Be("structured-output-stream",
            "FR-14d: widgetType is propagated from DispatchResult.WidgetType (same contract as Workspace branch)");
        playbookId.Should().Be(TestPlaybookId, "playbookId is included for frontend correlation + telemetry");
    }

    [Fact]
    public async Task HandleOutputAsync_BothDestination_EmitsChatAckToken()
    {
        // Arrange
        var handler = CreateHandler();
        var emittedEvents = new List<ChatSseEvent>();
        Task EmitSse(ChatSseEvent evt, CancellationToken _) { emittedEvents.Add(evt); return Task.CompletedTask; }
        var dispatch = BuildBothDispatch();

        // Act
        await handler.HandleOutputAsync(
            dispatch,
            EmitSse,
            hostContext: null,
            CancellationToken.None);

        // Assert — FR-14d Both: a chat ack token MUST be emitted (chat sidebar shows the ack)
        emittedEvents.Should().ContainSingle(e => e.Type == ChatTokenEventType,
            "FR-14d Both acceptance: handler emits exactly one chat ack token (templated English string)");
        emittedEvents.Should().ContainSingle(e => e.Type == TypingStartEventType,
            "EmitTextResponseAsync wraps the ack token in typing_start / typing_end markers");
        emittedEvents.Should().ContainSingle(e => e.Type == TypingEndEventType,
            "EmitTextResponseAsync wraps the ack token in typing_start / typing_end markers");

        var tokenEvent = emittedEvents.Single(e => e.Type == ChatTokenEventType);
        tokenEvent.Content.Should().NotBeNullOrWhiteSpace(
            "the templated ack string is carried on ChatSseEvent.Content");
        tokenEvent.Content.Should().Contain(TestPlaybookName,
            "the ack must include the substituted playbook name (no GUIDs / code names)");
    }

    [Fact]
    public async Task HandleOutputAsync_BothDestination_AckContainsPlaybookNameSubstitution()
    {
        // Arrange — exact-template assertion (design §1.3 hardcoded English)
        var handler = CreateHandler();
        var emittedEvents = new List<ChatSseEvent>();
        Task EmitSse(ChatSseEvent evt, CancellationToken _) { emittedEvents.Add(evt); return Task.CompletedTask; }
        var dispatch = BuildBothDispatch(playbookName: "Summarize NDA");

        // Act
        await handler.HandleOutputAsync(
            dispatch,
            EmitSse,
            hostContext: null,
            CancellationToken.None);

        // Assert — exact templated string per design.md WP3
        var tokenEvent = emittedEvents.Single(e => e.Type == ChatTokenEventType);
        tokenEvent.Content.Should().Be(
            "I've added a Summarize NDA result to the Workspace.",
            "design.md §1.3 hardcoded English template: " +
            "\"I've added a {playbookName} result to the Workspace.\" — exact substitution required");
    }

    [Fact]
    public async Task HandleOutputAsync_BothDestination_UsesPlaybookFallback_WhenPlaybookNameIsNull()
    {
        // Arrange — defensive fallback: when PlaybookName is null, the ack uses the literal
        // word "playbook" (never "null" / "undefined" / empty string).
        var handler = CreateHandler();
        var emittedEvents = new List<ChatSseEvent>();
        Task EmitSse(ChatSseEvent evt, CancellationToken _) { emittedEvents.Add(evt); return Task.CompletedTask; }
        var dispatch = BuildBothDispatch(playbookName: null);

        // Act
        await handler.HandleOutputAsync(
            dispatch,
            EmitSse,
            hostContext: null,
            CancellationToken.None);

        // Assert
        var tokenEvent = emittedEvents.Single(e => e.Type == ChatTokenEventType);
        tokenEvent.Content.Should().Be(
            "I've added a playbook result to the Workspace.",
            "defensive null-fallback: when PlaybookName is null, the ack must use the " +
            "literal word \"playbook\" — never \"null\" / empty / \"undefined\"");
    }

    [Fact]
    public async Task HandleOutputAsync_BothDestination_EmitsTabOpenBeforeChatAck()
    {
        // Arrange — sequence assertion per POML step 3: workspace.tab_open → typing_start →
        // ack token → typing_end → streaming proceeds. The frontend mounts the widget BEFORE
        // the chat token surfaces so the user does not see "I've added X" before the X exists.
        var handler = CreateHandler();
        var emittedEvents = new List<ChatSseEvent>();
        Task EmitSse(ChatSseEvent evt, CancellationToken _) { emittedEvents.Add(evt); return Task.CompletedTask; }
        var dispatch = BuildBothDispatch();

        // Act
        await handler.HandleOutputAsync(
            dispatch,
            EmitSse,
            hostContext: null,
            CancellationToken.None);

        // Assert — order verification
        var tabOpenIndex = emittedEvents.FindIndex(e => e.Type == WorkspaceTabOpenEventType);
        var typingStartIndex = emittedEvents.FindIndex(e => e.Type == TypingStartEventType);
        var tokenIndex = emittedEvents.FindIndex(e => e.Type == ChatTokenEventType);
        var typingEndIndex = emittedEvents.FindIndex(e => e.Type == TypingEndEventType);

        tabOpenIndex.Should().BeGreaterOrEqualTo(0, "workspace.tab_open must be emitted");
        typingStartIndex.Should().BeGreaterOrEqualTo(0, "typing_start must be emitted");
        tokenIndex.Should().BeGreaterOrEqualTo(0, "chat ack token must be emitted");
        typingEndIndex.Should().BeGreaterOrEqualTo(0, "typing_end must be emitted");

        tabOpenIndex.Should().BeLessThan(typingStartIndex,
            "POML step 3 sequence: workspace.tab_open emitted BEFORE chat ack typing_start so " +
            "the frontend mounts the widget BEFORE the chat token surfaces (the user must not " +
            "see \"I've added X\" before the X is mounted)");
        typingStartIndex.Should().BeLessThan(tokenIndex,
            "EmitTextResponseAsync sequence: typing_start emitted BEFORE the ack token");
        tokenIndex.Should().BeLessThan(typingEndIndex,
            "EmitTextResponseAsync sequence: ack token emitted BEFORE typing_end");
    }

    // -------------------------------------------------------------------------
    // Payload extraction helper — the WorkspaceTabOpenSseData record is internal,
    // accessed via reflection so the test does not need an InternalsVisibleTo
    // attribute for this single test file. Mirrors the helper in
    // PlaybookOutputHandlerWorkspaceCaseTests for consistency.
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
