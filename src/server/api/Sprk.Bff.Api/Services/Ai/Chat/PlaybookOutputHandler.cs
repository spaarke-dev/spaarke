using System.Text.Json;
using Microsoft.Extensions.AI;
using Sprk.Bff.Api.Api.Ai;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Models.Ai.Chat;
using Sprk.Bff.Api.Services.Ai.Export;

namespace Sprk.Bff.Api.Services.Ai.Chat;

/// <summary>
/// Routes a matched <see cref="DispatchResult"/> to the appropriate output action based on
/// the five typed output types defined in the JPS DeliverOutput node — and, as of
/// chat-routing-redesign-r1 task 048 (FR-14d), also based on
/// <see cref="DispatchResult.NodeDestination"/> which is checked UPFRONT before the
/// <see cref="OutputType"/> switch (Workspace / Both / FormPrefill / SideEffect short-circuit
/// the OutputType arms).
///
/// <list type="bullet">
///   <item><b>workspace</b> (NodeDestination) — Emits a <c>workspace.tab_open</c> SSE event
///     with <c>{ tabId, widgetType }</c> and short-circuits chat-token emission. The
///     <see cref="Services.Ai.PlaybookExecutionEngine"/> streams <c>FieldDelta</c> events
///     via the existing <c>/summarize</c> SSE endpoint path — this handler does NOT proxy
///     that stream (ADR-033 streaming chat-tool side-channel preservation pattern).</item>
///   <item><b>text</b> — Streams the AI response normally through the standard chat flow.
///     Delegates to <see cref="CompoundIntentDetector"/> for plan preview gate when the
///     response is a multi-step plan.</item>
///   <item><b>dialog</b> — Emits a <c>dialog_open</c> SSE event with pre-populated fields
///     when <see cref="DispatchResult.RequiresConfirmation"/> is true (HITL path).
///     When false, executes the action autonomously and emits a confirmation message.</item>
///   <item><b>navigation</b> — Emits a <c>navigate</c> SSE event with the target URL/record
///     constructed from extracted parameters and <see cref="ChatHostContext"/>.</item>
///   <item><b>download</b> — Calls <see cref="DocxExportService"/> to generate a file and
///     emits a download link via SSE.</item>
///   <item><b>insert</b> — Emits <c>document_stream_start</c>/<c>document_stream_token</c>/
///     <c>document_stream_end</c> events for editor insertion (R2-023 write-back).</item>
/// </list>
///
/// <b>Lifetime</b>: Factory-instantiated per dispatch call. NOT registered in DI (ADR-010).
/// Created by <see cref="SprkChatAgentFactory.CreatePlaybookOutputHandlerAsync"/>.
///
/// <b>ADR-006</b>: Dialog output opens Code Page via <c>navigateTo</c> pattern — the
/// <c>dialog_open</c> event carries a <c>codePage</c> web resource name.
///
/// <b>ADR-013</b>: <see cref="ChatHostContext"/> flows through for entity-scoped operations.
///
/// <b>ADR-014</b>: Pre-populated field values are ephemeral and NOT cached.
///
/// <b>ADR-033</b>: For the Workspace destination, the structural <c>workspace.tab_open</c>
/// SSE event is emitted from this handler; the per-field token streaming continues to flow
/// via the <see cref="Services.Ai.PlaybookExecutionEngine"/> streaming surface (existing
/// <c>FieldDelta</c> path) — the handler does NOT introduce a new IPlaybookExecutionEngine
/// dependency to proxy/transform streaming. This minimises the change scope per the
/// "ADRs Are Defaults — Challenge When Warranted" operating principle.
/// </summary>
public sealed class PlaybookOutputHandler
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly CompoundIntentDetector _intentDetector;
    private readonly DocxExportService _docxExport;
    private readonly ILogger _logger;

    /// <summary>
    /// Creates a new <see cref="PlaybookOutputHandler"/>.
    /// Called exclusively by <see cref="SprkChatAgentFactory"/> (factory instantiation, ADR-010).
    /// </summary>
    /// <param name="intentDetector">Compound intent detector for text output plan preview gating.</param>
    /// <param name="docxExport">DOCX export service for download output type.</param>
    /// <param name="logger">Logger instance.</param>
    public PlaybookOutputHandler(
        CompoundIntentDetector intentDetector,
        DocxExportService docxExport,
        ILogger<PlaybookOutputHandler> logger)
    {
        _intentDetector = intentDetector ?? throw new ArgumentNullException(nameof(intentDetector));
        _docxExport = docxExport ?? throw new ArgumentNullException(nameof(docxExport));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Routes the dispatch result to the appropriate output handler.
    ///
    /// <para>
    /// <b>Routing surface — task 048 (FR-14d) update</b>: dispatch routing is two-level.
    /// <see cref="DispatchResult.NodeDestination"/> is consulted FIRST — when it is anything
    /// other than <see cref="NodeDestination.Chat"/>, the dispatch short-circuits the
    /// <see cref="OutputType"/> arms (Workspace / Both / FormPrefill / SideEffect have their
    /// own dedicated handlers). When destination is <see cref="NodeDestination.Chat"/> (the
    /// default — backward-compatible for all pre-R6 call sites), the existing
    /// <see cref="OutputType"/> switch is consulted (Text / Dialog / Navigation / Download /
    /// Insert).
    /// </para>
    ///
    /// For HITL outputs (<see cref="DispatchResult.RequiresConfirmation"/> = true), the handler
    /// emits an SSE event (dialog_open, navigate) for the frontend to present to the user.
    /// For autonomous outputs, the handler executes the action directly and emits a result.
    /// </summary>
    /// <param name="dispatch">The matched dispatch result from <see cref="PlaybookDispatcher"/>.</param>
    /// <param name="emitSseEvent">
    /// Delegate to emit SSE events to the client. Wraps <see cref="ChatEndpoints.WriteChatSSEAsync"/>.
    /// </param>
    /// <param name="hostContext">
    /// Optional host context describing where SprkChat is embedded (ADR-013: entity scoping).
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// True if the output was fully handled (caller should emit <c>done</c> and return).
    /// False if the output type is <c>text</c> (default chat destination) and the caller
    /// should proceed with normal streaming (the handler did not intercept).
    /// </returns>
    public async Task<bool> HandleOutputAsync(
        DispatchResult dispatch,
        Func<ChatSseEvent, CancellationToken, Task> emitSseEvent,
        ChatHostContext? hostContext,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dispatch, nameof(dispatch));
        ArgumentNullException.ThrowIfNull(emitSseEvent, nameof(emitSseEvent));

        if (!dispatch.Matched)
        {
            _logger.LogDebug("PlaybookOutputHandler: dispatch not matched, falling through to standard flow");
            return false;
        }

        _logger.LogInformation(
            "PlaybookOutputHandler: routing output — playbookId={PlaybookId}, destination={NodeDestination}, " +
            "outputType={OutputType}, widgetType={WidgetType}, requiresConfirmation={RequiresConfirmation}, " +
            "targetPage={TargetPage}",
            dispatch.PlaybookId, dispatch.NodeDestination, dispatch.OutputType, dispatch.WidgetType,
            dispatch.RequiresConfirmation, dispatch.TargetPage);

        // -------------------------------------------------------------------------
        // Task 048 (FR-14d) Workspace branch: route on NodeDestination FIRST.
        // -------------------------------------------------------------------------
        // The NodeDestination check sits ABOVE the OutputType switch because the
        // workspace-bound `summarize-document-for-workspace` playbook has OutputType.Text
        // (its content is text — only the destination surface differs). Routing solely
        // on OutputType would never reach the Workspace branch.
        //
        // Backward-compat: NodeDestination defaults to Chat (DispatchResult constructor
        // default; PlaybookDispatcher.Parse default; FR-14b/FR-27 binding). All pre-R6
        // call sites hit the OutputType switch below unchanged.
        //
        // Sibling tasks 049/050/051 add Both / FormPrefill / SideEffect arms here.
        switch (dispatch.NodeDestination)
        {
            case NodeDestination.Workspace:
                return await HandleWorkspaceOutputAsync(dispatch, emitSseEvent, cancellationToken);

            case NodeDestination.Both:
                return await HandleBothOutputAsync(dispatch, emitSseEvent, cancellationToken);

            // FormPrefill / SideEffect arms added by tasks 050 / 051.
            case NodeDestination.Chat:
            default:
                // Fall through to the OutputType switch below (existing pre-R6 behavior).
                break;
        }

        return dispatch.OutputType switch
        {
            OutputType.Text => await HandleTextOutputAsync(dispatch, emitSseEvent, cancellationToken),
            OutputType.Dialog => await HandleDialogOutputAsync(dispatch, emitSseEvent, cancellationToken),
            OutputType.Navigation => await HandleNavigationOutputAsync(dispatch, emitSseEvent, hostContext, cancellationToken),
            OutputType.Download => await HandleDownloadOutputAsync(dispatch, emitSseEvent, cancellationToken),
            OutputType.Insert => await HandleInsertOutputAsync(dispatch, emitSseEvent, cancellationToken),
            _ => HandleUnknownOutputType(dispatch)
        };
    }

    // =========================================================================
    // Output Type Handlers
    // =========================================================================

    #region Text Output

    /// <summary>
    /// Handles <see cref="OutputType.Text"/> — standard chat response flow.
    ///
    /// Returns false to indicate the caller should proceed with normal streaming.
    /// The <see cref="CompoundIntentDetector"/> in the calling code (ChatEndpoints)
    /// already handles plan preview gating for text responses that contain multi-step
    /// plans, so this handler does not intercept.
    /// </summary>
    private Task<bool> HandleTextOutputAsync(
        DispatchResult dispatch,
        Func<ChatSseEvent, CancellationToken, Task> emitSseEvent,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug(
            "PlaybookOutputHandler: text output — deferring to standard streaming flow, " +
            "playbook={PlaybookId}",
            dispatch.PlaybookId);

        // Text output is handled by the standard chat streaming pipeline.
        // CompoundIntentDetector handles plan_preview gating for multi-step plans.
        return Task.FromResult(false);
    }

    #endregion

    #region Dialog Output

    /// <summary>
    /// Handles <see cref="OutputType.Dialog"/> — opens a Code Page dialog or executes autonomously.
    ///
    /// HITL path (<see cref="DispatchResult.RequiresConfirmation"/> = true):
    ///   Emits a <c>dialog_open</c> SSE event with the Code Page name and pre-populated fields
    ///   extracted by <see cref="PlaybookDispatcher"/>. The frontend calls
    ///   <c>Xrm.Navigation.navigateTo</c> to open the Code Page (ADR-006).
    ///
    /// Autonomous path (<see cref="DispatchResult.RequiresConfirmation"/> = false):
    ///   Emits a confirmation message indicating the action was completed automatically.
    ///   Future iterations will execute the action directly (e.g., create record, send email).
    /// </summary>
    private async Task<bool> HandleDialogOutputAsync(
        DispatchResult dispatch,
        Func<ChatSseEvent, CancellationToken, Task> emitSseEvent,
        CancellationToken cancellationToken)
    {
        if (dispatch.RequiresConfirmation)
        {
            // HITL path: emit dialog_open for user review/completion
            var codePage = dispatch.TargetPage;
            if (string.IsNullOrWhiteSpace(codePage))
            {
                _logger.LogWarning(
                    "PlaybookOutputHandler: dialog output requires TargetPage but none provided — " +
                    "playbook={PlaybookId}. Falling back to text confirmation.",
                    dispatch.PlaybookId);

                await EmitTextResponseAsync(
                    emitSseEvent,
                    $"I've prepared the information for \"{dispatch.PlaybookName}\", but the dialog " +
                    "configuration is incomplete. Please check the playbook setup.",
                    cancellationToken);
                return true;
            }

            var dialogData = new ChatSseDialogOpenData(
                TargetPage: codePage,
                PrePopulateFields: dispatch.ExtractedParameters,
                PlaybookId: dispatch.PlaybookId ?? string.Empty,
                PlaybookName: dispatch.PlaybookName);

            await emitSseEvent(
                new ChatSseEvent("dialog_open", null, dialogData),
                cancellationToken);

            _logger.LogInformation(
                "PlaybookOutputHandler: dialog_open emitted — codePage={CodePage}, " +
                "fieldCount={FieldCount}, playbook={PlaybookId}",
                codePage, dispatch.ExtractedParameters.Count, dispatch.PlaybookId);

            return true;
        }
        else
        {
            // Autonomous path: execute directly (future: call the action tool).
            // Emit both a text message and an action_success event so the frontend shows
            // a success toast (R2-052: autonomous actions MUST execute without dialog).
            var actionName = dispatch.PlaybookName ?? "the requested action";
            await EmitTextResponseAsync(
                emitSseEvent,
                $"I've completed \"{actionName}\" automatically.",
                cancellationToken);

            // Emit action_success SSE event for frontend toast (Task R2-052).
            // Frontend handleActionSuccessEvent shows a Fluent v9 success toast.
            await emitSseEvent(
                new ChatSseEvent("action_success", null, new Dictionary<string, object>
                {
                    ["actionId"] = dispatch.PlaybookId ?? "autonomous",
                    ["message"] = $"\"{actionName}\" completed successfully."
                }),
                cancellationToken);

            _logger.LogInformation(
                "PlaybookOutputHandler: autonomous dialog action completed — playbook={PlaybookId}",
                dispatch.PlaybookId);

            return true;
        }
    }

    #endregion

    #region Navigation Output

    /// <summary>
    /// Handles <see cref="OutputType.Navigation"/> — emits a <c>navigate</c> SSE event
    /// with the target URL or Code Page and extracted parameters.
    ///
    /// The navigation URL is constructed from <see cref="DispatchResult.TargetPage"/>
    /// and <see cref="DispatchResult.ExtractedParameters"/>. When <see cref="ChatHostContext"/>
    /// is available, entity-scoped parameters are included (ADR-013).
    /// </summary>
    private async Task<bool> HandleNavigationOutputAsync(
        DispatchResult dispatch,
        Func<ChatSseEvent, CancellationToken, Task> emitSseEvent,
        ChatHostContext? hostContext,
        CancellationToken cancellationToken)
    {
        var parameters = new Dictionary<string, string>(dispatch.ExtractedParameters);

        // Inject host context parameters for entity-scoped navigation (ADR-013)
        if (hostContext is not null)
        {
            if (!parameters.ContainsKey("entityType"))
                parameters["entityType"] = hostContext.EntityType;
            if (!parameters.ContainsKey("entityId"))
                parameters["entityId"] = hostContext.EntityId;
        }

        // Build navigation URL from target page + parameters if no explicit URL provided
        var url = BuildNavigationUrl(dispatch.TargetPage, parameters);

        var navigateData = new ChatSseNavigateData(
            Url: url,
            TargetPage: dispatch.TargetPage,
            Parameters: parameters,
            PlaybookId: dispatch.PlaybookId);

        if (dispatch.RequiresConfirmation)
        {
            // Emit a brief text message before the navigate event so the user knows what's happening
            await EmitTextResponseAsync(
                emitSseEvent,
                $"I'll navigate you to {dispatch.PlaybookName ?? "the requested page"}.",
                cancellationToken);
        }

        await emitSseEvent(
            new ChatSseEvent("navigate", null, navigateData),
            cancellationToken);

        _logger.LogInformation(
            "PlaybookOutputHandler: navigate event emitted — targetPage={TargetPage}, " +
            "paramCount={ParamCount}, playbook={PlaybookId}",
            dispatch.TargetPage, parameters.Count, dispatch.PlaybookId);

        return true;
    }

    #endregion

    #region Download Output

    /// <summary>
    /// Handles <see cref="OutputType.Download"/> — generates a file via <see cref="DocxExportService"/>
    /// and emits a download link to the client.
    ///
    /// Uses the AI-extracted parameters to build an <see cref="ExportContext"/> for the export service.
    /// The generated file bytes are not cached (ADR-014: ephemeral per-request).
    /// </summary>
    private async Task<bool> HandleDownloadOutputAsync(
        DispatchResult dispatch,
        Func<ChatSseEvent, CancellationToken, Task> emitSseEvent,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "PlaybookOutputHandler: download output — generating DOCX via DocxExportService, " +
            "playbook={PlaybookId}",
            dispatch.PlaybookId);

        try
        {
            // Build export context from extracted parameters
            var exportContext = BuildExportContext(dispatch);

            // Validate before export
            var validation = _docxExport.Validate(exportContext);
            if (!validation.IsValid)
            {
                var errors = string.Join("; ", validation.Errors);
                _logger.LogWarning(
                    "PlaybookOutputHandler: export validation failed — errors={Errors}, playbook={PlaybookId}",
                    errors, dispatch.PlaybookId);

                await EmitTextResponseAsync(
                    emitSseEvent,
                    $"I couldn't generate the document: {errors}",
                    cancellationToken);
                return true;
            }

            var result = await _docxExport.ExportAsync(exportContext, cancellationToken);

            if (result.Success)
            {
                // Emit download confirmation with file metadata.
                // The frontend will use this to offer the download to the user.
                await EmitTextResponseAsync(
                    emitSseEvent,
                    $"I've generated your document: **{result.FileName}**. The download should start automatically.",
                    cancellationToken);

                await emitSseEvent(
                    new ChatSseEvent("plan_step_complete", null, new ChatSsePlanStepCompleteData(
                        StepId: "download",
                        Status: "completed",
                        Result: $"Generated {result.FileName} ({result.FileBytes?.Length ?? 0} bytes)")),
                    cancellationToken);
            }
            else
            {
                await EmitTextResponseAsync(
                    emitSseEvent,
                    $"I wasn't able to generate the document. {result.Error ?? "Please try again."}",
                    cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "PlaybookOutputHandler: download generation failed — playbook={PlaybookId}",
                dispatch.PlaybookId);

            await EmitTextResponseAsync(
                emitSseEvent,
                "An error occurred while generating the document. Please try again.",
                cancellationToken);
        }

        return true;
    }

    #endregion

    #region Insert Output

    /// <summary>
    /// Handles <see cref="OutputType.Insert"/> — emits document stream events for editor insertion.
    ///
    /// Uses the <c>document_stream_start</c>, <c>document_stream_token</c>, and
    /// <c>document_stream_end</c> event types from <see cref="DocumentStreamEvent"/>.
    /// The frontend editor (Lexical/working document) receives these events and inserts
    /// the content at the current cursor position.
    ///
    /// This output type is used by the R2-023 write-back feature.
    /// </summary>
    private async Task<bool> HandleInsertOutputAsync(
        DispatchResult dispatch,
        Func<ChatSseEvent, CancellationToken, Task> emitSseEvent,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "PlaybookOutputHandler: insert output — emitting document stream events, " +
            "playbook={PlaybookId}",
            dispatch.PlaybookId);

        var operationId = Guid.NewGuid();

        // Emit document_stream_start
        var startEvent = new DocumentStreamStartEvent(
            OperationId: operationId,
            TargetPosition: "cursor",
            OperationType: "insert");
        await emitSseEvent(
            new ChatSseEvent("document_stream_start", null, startEvent),
            cancellationToken);

        // Extract content to insert from parameters
        var insertContent = dispatch.ExtractedParameters.GetValueOrDefault("content")
            ?? dispatch.ExtractedParameters.GetValueOrDefault("text")
            ?? string.Empty;

        if (string.IsNullOrWhiteSpace(insertContent))
        {
            _logger.LogWarning(
                "PlaybookOutputHandler: insert output has no content to insert — playbook={PlaybookId}",
                dispatch.PlaybookId);

            // Emit end event with zero tokens
            var emptyEndEvent = new DocumentStreamEndEvent(
                OperationId: operationId,
                Cancelled: false,
                TotalTokens: 0);
            await emitSseEvent(
                new ChatSseEvent("document_stream_end", null, emptyEndEvent),
                cancellationToken);

            await EmitTextResponseAsync(
                emitSseEvent,
                "No content was available to insert into the document.",
                cancellationToken);

            return true;
        }

        // Emit content as token events (chunked for streaming feel)
        var chunks = ChunkContent(insertContent);
        for (var i = 0; i < chunks.Length; i++)
        {
            var tokenEvent = new DocumentStreamTokenEvent(
                OperationId: operationId,
                Token: chunks[i],
                Index: i);
            await emitSseEvent(
                new ChatSseEvent("document_stream_token", null, tokenEvent),
                cancellationToken);
        }

        // Emit document_stream_end
        var endEvent = new DocumentStreamEndEvent(
            OperationId: operationId,
            Cancelled: false,
            TotalTokens: chunks.Length);
        await emitSseEvent(
            new ChatSseEvent("document_stream_end", null, endEvent),
            cancellationToken);

        await EmitTextResponseAsync(
            emitSseEvent,
            "Content has been inserted into your document.",
            cancellationToken);

        _logger.LogInformation(
            "PlaybookOutputHandler: insert complete — {TokenCount} tokens emitted, playbook={PlaybookId}",
            chunks.Length, dispatch.PlaybookId);

        return true;
    }

    #endregion

    #region Workspace Output (Task 048 / FR-14d)

    /// <summary>
    /// Handles <see cref="NodeDestination.Workspace"/> — emits a <c>workspace.tab_open</c>
    /// SSE event with <c>{ tabId, widgetType }</c> and short-circuits chat-token emission.
    ///
    /// <para>
    /// <b>Spec FR-14d acceptance</b>: Workspace destination dispatch emits
    /// <c>workspace.tab_open</c> SSE from the handler; <see cref="Services.Ai.PlaybookExecutionEngine"/>
    /// streaming is preserved via the existing <c>FieldDelta</c> path on the
    /// <c>/api/ai/chat/sessions/{id}/summarize</c> endpoint (NOT proxied through this handler);
    /// chat sidebar stays empty (no chat tokens for Workspace destination).
    /// </para>
    ///
    /// <para>
    /// <b>ADR-033 streaming side-channel discipline</b>: this handler emits the STRUCTURAL
    /// <c>workspace.tab_open</c> event and returns true. It does NOT proxy / transform /
    /// inject IPlaybookExecutionEngine to re-stream the per-field deltas — the engine's
    /// existing <c>ExecuteChatSummarizeAsync</c> SSE stream (consumed by the
    /// <c>sseToPaneEventBridge</c> on the frontend) is the streaming surface for the
    /// Workspace tab's content. The handler's contribution is the <c>tab_open</c> structural
    /// event that tells the frontend to mount the widget BEFORE the deltas begin to arrive.
    /// </para>
    ///
    /// <para>
    /// <b>Note for sibling task 049 (NodeDestination.Both)</b>: when destination is Both,
    /// the handler will (a) call <see cref="EmitWorkspaceTabOpenAndStreamAsync"/> below to
    /// reuse the workspace-tab-open emission, then (b) emit a chat ack token. The helper
    /// is extracted as a <c>private</c> method on this class for that reason.
    /// </para>
    /// </summary>
    /// <param name="dispatch">Matched dispatch result with <see cref="NodeDestination.Workspace"/>.</param>
    /// <param name="emitSseEvent">SSE writer delegate.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Always true (handled — caller should emit <c>done</c> and return).</returns>
    private async Task<bool> HandleWorkspaceOutputAsync(
        DispatchResult dispatch,
        Func<ChatSseEvent, CancellationToken, Task> emitSseEvent,
        CancellationToken cancellationToken)
    {
        await EmitWorkspaceTabOpenAndStreamAsync(dispatch, emitSseEvent, cancellationToken);

        _logger.LogInformation(
            "PlaybookOutputHandler: workspace destination handled — tab_open emitted, " +
            "chat-token emission suppressed, playbook={PlaybookId}, widgetType={WidgetType}",
            dispatch.PlaybookId, dispatch.WidgetType);

        return true;
    }

    /// <summary>
    /// Handles <see cref="NodeDestination.Both"/> — emits a <c>workspace.tab_open</c> SSE event
    /// (same as the Workspace branch) AND a templated chat ack token so the chat sidebar
    /// shows a brief confirmation that a Workspace result was produced.
    ///
    /// <para>
    /// <b>Spec FR-14d Both acceptance</b>: Both-destination dispatch emits BOTH
    /// <c>workspace.tab_open</c> SSE AND a chat ack token
    /// (<c>"I've added a {playbookName} result to the Workspace."</c>). Streaming preserved
    /// (the per-field <c>FieldDelta</c> stream continues to flow via the existing
    /// <see cref="Services.Ai.PlaybookExecutionEngine"/> SSE path — this handler does NOT
    /// proxy/transform that stream, mirroring the Workspace branch ADR-033 discipline).
    /// </para>
    ///
    /// <para>
    /// <b>Sequence</b> (per task 049 POML step 3 — tab_open → typing_start → ack token →
    /// typing_end → streaming proceeds): the structural <c>workspace.tab_open</c> is emitted
    /// FIRST so the frontend can mount the widget BEFORE the chat ack appears, then the chat
    /// ack is emitted via the existing <see cref="EmitTextResponseAsync"/> helper which wraps
    /// the token in <c>typing_start</c>/<c>typing_end</c> markers (matching the chat surface's
    /// existing convention). Per-field streaming is the engine's responsibility and flows
    /// independently on the <c>/api/ai/chat/sessions/{id}/summarize</c> endpoint after this
    /// handler returns.
    /// </para>
    ///
    /// <para>
    /// <b>Ack template (hardcoded English per design.md §1.3)</b>:
    /// <c>"I've added a {playbookName} result to the Workspace."</c> where
    /// <c>{playbookName}</c> is <see cref="DispatchResult.PlaybookName"/> or the literal
    /// fallback word <c>"playbook"</c> when null/whitespace. Per-playbook customization is
    /// deferred until proven necessary (design.md WP3).
    /// </para>
    /// </summary>
    /// <param name="dispatch">Matched dispatch result with <see cref="NodeDestination.Both"/>.</param>
    /// <param name="emitSseEvent">SSE writer delegate.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Always true (handled — caller should emit <c>done</c> and return).</returns>
    private async Task<bool> HandleBothOutputAsync(
        DispatchResult dispatch,
        Func<ChatSseEvent, CancellationToken, Task> emitSseEvent,
        CancellationToken cancellationToken)
    {
        // (1) Structural workspace.tab_open SSE event — DRY reuse of the Workspace branch
        // helper extracted by task 048 for exactly this purpose.
        await EmitWorkspaceTabOpenAndStreamAsync(dispatch, emitSseEvent, cancellationToken);

        // (2) Templated chat ack — defensive null fallback to the literal word "playbook"
        // (design.md: hardcoded English; defer per-playbook customization until proven
        // necessary). The fallback word "playbook" is what the user sees if the dispatch
        // somehow lacks PlaybookName — never "null" / "" / "undefined".
        var playbookName = string.IsNullOrWhiteSpace(dispatch.PlaybookName)
            ? "playbook"
            : dispatch.PlaybookName;

        var ackMessage = $"I've added a {playbookName} result to the Workspace.";

        // EmitTextResponseAsync emits typing_start → token → typing_end (matches the chat
        // surface's existing convention used by Dialog/Navigation/Download/Insert ack flows).
        await EmitTextResponseAsync(emitSseEvent, ackMessage, cancellationToken);

        _logger.LogInformation(
            "PlaybookOutputHandler: both destination handled — tab_open emitted + chat ack " +
            "emitted, playbook={PlaybookId}, playbookName={PlaybookName}, widgetType={WidgetType}",
            dispatch.PlaybookId, playbookName, dispatch.WidgetType);

        return true;
    }

    /// <summary>
    /// Emits the <c>workspace.tab_open</c> SSE event carrying <c>{ tabId, widgetType }</c>.
    /// Extracted as a private helper so sibling task 049 (Both destination) can reuse the
    /// same emission alongside its chat-ack path (the DRY opportunity called out in
    /// project task 049 POML <c>notes</c>).
    ///
    /// <para>
    /// <b>tabId derivation</b>: a fresh GUID (N-format) per emission. The frontend uses this
    /// to correlate subsequent <c>field_delta</c> / <c>streaming_complete</c> events. When
    /// the dispatch carries an explicit <c>workspaceTabId</c> extracted parameter (from the
    /// playbook's <c>parameterSchema.workspaceTabId</c> — see
    /// <c>summarize-document-for-workspace.playbook.json</c>), that value is preferred so
    /// the frontend can update an existing tab; otherwise a new GUID is allocated.
    /// </para>
    ///
    /// <para>
    /// <b>widgetType fallback</b>: when <see cref="DispatchResult.WidgetType"/> is null (an
    /// edge case where a Workspace-destination playbook author forgot to set
    /// <c>widgetType</c> in the node config — caught by
    /// <see cref="NodeRoutingConfig.Validate"/> at config-author time but defended here),
    /// fall back to the canonical streaming widget key
    /// <c>"structured-output-stream"</c> (matches
    /// <c>STRUCTURED_OUTPUT_STREAM_WIDGET_TYPE</c> in
    /// <c>register-structured-output-stream-widget.ts</c>).
    /// </para>
    /// </summary>
    private static async Task EmitWorkspaceTabOpenAndStreamAsync(
        DispatchResult dispatch,
        Func<ChatSseEvent, CancellationToken, Task> emitSseEvent,
        CancellationToken cancellationToken)
    {
        var tabId = dispatch.ExtractedParameters.TryGetValue("workspaceTabId", out var existingTabId)
            && !string.IsNullOrWhiteSpace(existingTabId)
                ? existingTabId
                : Guid.NewGuid().ToString("N");

        // Fall back to the canonical streaming widget key when the node config omitted
        // widgetType. NodeRoutingConfig.Validate catches this at author time; the runtime
        // fallback keeps the surface non-fatal.
        string widgetType = string.IsNullOrWhiteSpace(dispatch.WidgetType)
            ? "structured-output-stream"
            : dispatch.WidgetType!;

        var data = new WorkspaceTabOpenSseData(
            TabId: tabId,
            WidgetType: widgetType,
            PlaybookId: dispatch.PlaybookId);

        await emitSseEvent(
            new ChatSseEvent("workspace.tab_open", null, data),
            cancellationToken);
    }

    /// <summary>
    /// Payload for the <c>workspace.tab_open</c> SSE event emitted on Workspace destination
    /// dispatches (FR-14d). Carries the tab identifier the frontend uses to mount the widget
    /// and correlate subsequent <c>field_delta</c> events from the per-field streaming
    /// (<see cref="Services.Ai.PlaybookExecutionEngine.ExecuteChatSummarizeAsync"/> →
    /// <c>sseToPaneEventBridge</c>).
    /// </summary>
    /// <param name="TabId">Workspace tab identifier; new GUID or reuse of the dispatch's <c>workspaceTabId</c> extracted parameter.</param>
    /// <param name="WidgetType">Widget registry key (kebab-case wire format) the frontend resolves at render time.</param>
    /// <param name="PlaybookId">Matched playbook ID (for frontend correlation + telemetry).</param>
    internal sealed record WorkspaceTabOpenSseData(
        string TabId,
        string WidgetType,
        string? PlaybookId);

    #endregion

    // =========================================================================
    // Private Helpers
    // =========================================================================

    /// <summary>
    /// Emits a brief text response as standard SSE token events (typing_start → token → typing_end).
    /// Used for confirmation messages after autonomous execution or error reporting.
    /// </summary>
    private static async Task EmitTextResponseAsync(
        Func<ChatSseEvent, CancellationToken, Task> emitSseEvent,
        string message,
        CancellationToken cancellationToken)
    {
        await emitSseEvent(new ChatSseEvent("typing_start", null), cancellationToken);
        await emitSseEvent(new ChatSseEvent("token", message), cancellationToken);
        await emitSseEvent(new ChatSseEvent("typing_end", null), cancellationToken);
    }

    /// <summary>
    /// Handles unknown output types by logging a warning and returning false
    /// to fall through to the standard streaming flow.
    /// </summary>
    private bool HandleUnknownOutputType(DispatchResult dispatch)
    {
        _logger.LogWarning(
            "PlaybookOutputHandler: unknown output type '{OutputType}' — falling through to " +
            "standard flow, playbook={PlaybookId}",
            dispatch.OutputType, dispatch.PlaybookId);
        return false;
    }

    /// <summary>
    /// Builds a navigation URL from a target page name and parameters.
    /// Returns null when no target page is specified (the frontend should use
    /// the parameters directly to construct the navigation).
    /// </summary>
    private static string? BuildNavigationUrl(string? targetPage, Dictionary<string, string> parameters)
    {
        if (string.IsNullOrWhiteSpace(targetPage))
            return null;

        // If parameters contain an explicit URL, use it directly
        if (parameters.TryGetValue("url", out var explicitUrl) && !string.IsNullOrWhiteSpace(explicitUrl))
            return explicitUrl;

        // Build a webresource navigation URL for Code Pages
        var queryParams = string.Join("&",
            parameters.Select(kvp => $"{Uri.EscapeDataString(kvp.Key)}={Uri.EscapeDataString(kvp.Value)}"));

        return string.IsNullOrEmpty(queryParams)
            ? null
            : null; // URL construction is deferred to the frontend via TargetPage + Parameters
    }

    /// <summary>
    /// Builds an <see cref="ExportContext"/> from the dispatch result's extracted parameters
    /// for DOCX export.
    /// </summary>
    private static ExportContext BuildExportContext(DispatchResult dispatch)
    {
        var parameters = dispatch.ExtractedParameters;

        var analysisIdStr = parameters.GetValueOrDefault("analysisId");
        var analysisId = Guid.TryParse(analysisIdStr, out var parsed) ? parsed : Guid.NewGuid();

        return new ExportContext
        {
            AnalysisId = analysisId,
            Title = parameters.GetValueOrDefault("title") ?? dispatch.PlaybookName ?? "AI Generated Document",
            Content = parameters.GetValueOrDefault("content") ?? string.Empty,
            Summary = parameters.GetValueOrDefault("summary"),
            CreatedBy = "Spaarke AI",
            CreatedAt = DateTimeOffset.UtcNow,
            SourceDocumentName = parameters.GetValueOrDefault("sourceDocumentName")
        };
    }

    /// <summary>
    /// Splits content into chunks suitable for streaming token events.
    /// Uses sentence boundaries or word boundaries at ~100 character intervals.
    /// </summary>
    private static string[] ChunkContent(string content)
    {
        if (content.Length <= 100)
            return [content];

        var chunks = new List<string>();
        var remaining = content.AsSpan();

        while (remaining.Length > 0)
        {
            if (remaining.Length <= 100)
            {
                chunks.Add(remaining.ToString());
                break;
            }

            // Try to break at a sentence boundary within the first 100 chars
            var breakIndex = FindBreakPoint(remaining, 100);
            chunks.Add(remaining[..breakIndex].ToString());
            remaining = remaining[breakIndex..];
        }

        return chunks.ToArray();
    }

    /// <summary>
    /// Finds a suitable break point near the target length, preferring sentence
    /// boundaries (period, newline) then word boundaries (space).
    /// </summary>
    private static int FindBreakPoint(ReadOnlySpan<char> text, int targetLength)
    {
        // Look for sentence boundaries near target
        for (var i = targetLength; i >= targetLength / 2; i--)
        {
            if (i < text.Length && (text[i] == '.' || text[i] == '\n'))
                return i + 1;
        }

        // Fall back to word boundary
        for (var i = targetLength; i >= targetLength / 2; i--)
        {
            if (i < text.Length && text[i] == ' ')
                return i + 1;
        }

        // No good break point found — break at target length
        return Math.Min(targetLength, text.Length);
    }
}
