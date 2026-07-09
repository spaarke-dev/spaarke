using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Models.Ai.Chat;
using Sprk.Bff.Api.Services.Ai.PublicContracts;

namespace Sprk.Bff.Api.Services.Ai.Chat;

/// <summary>
/// Executes a CONFIRMED suspended typed-handler invocation — the confirm-RESUME leg of
/// the ONE Confirmation Gate for non-Binding tools (FR-P3-03,
/// <c>spaarke-ai-architecture-redesign-r1</c> task 042).
/// </summary>
/// <remarks>
/// <para>
/// <b>The seam this closes</b>: <see cref="SideEffectGateAIFunction"/> (task 037)
/// suspends every loop-invoked typed-handler tool whose <c>sprk_analysistool</c> row
/// DECLARES a side-effecting class (<c>write</c> / <c>communicate</c>) into the unified
/// pending store. Reject worked end-to-end from day one; Confirm closed the gate with
/// the honest interim status <c>confirmed-unexecutable</c> (G-P2 UAT finding 6) because
/// no execution seam existed. This class IS that seam: on confirm, the suspended
/// invocation executes through the SAME adapter/handler stack the loop would have used
/// (<c>ValidateChat</c> → <c>ExecuteChatAsync</c>), under the CONFIRMING USER's OBO
/// security context, with the result ledger-written BEFORE any rendering (ADR-040).
/// First consumers: <c>dataverse.create_record</c> (create-task, FR-P3-03) and
/// <c>email.draft</c> (draft-correspondence, FR-P3-02 — task 041 integration note 1).
/// </para>
/// <para>
/// <b>Component Justification (CLAUDE.md §11)</b>:
/// (1) <i>Existing</i> — <see cref="SessionDispatchOrchestrator"/> executes catalog
/// BINDINGS by id (prompted-executor stack; P1 args contract forwards <c>fileIds</c>
/// only) and <see cref="ToolHandlerToAIFunctionAdapter"/> executes typed handlers from
/// INSIDE a live agent turn — neither can run a resumed
/// <see cref="PendingInvocation"/> that has a ToolId but no BindingId and no LLM turn.
/// (2) <i>Extension</i> — extending the orchestrator would fork its Binding-shaped
/// request contract (bindingId is <c>required</c> on <see cref="SessionDispatchRequest"/>);
/// extending the adapter would give a projection-time bridge an HTTP-request lifetime it
/// deliberately does not have.
/// (3) <i>Cost-of-doing-nothing</i> — every confirmed typed-handler write stays
/// <c>confirmed-unexecutable</c> (422 <c>gate.no-binding-target</c>): FR-P3-03
/// create-task and FR-P3-02 email.draft confirm legs are dead ends and walkthrough
/// steps 13-14 cannot pass in the browser.
/// </para>
/// <para>
/// <b>ADR-039</b>: resolution keys EXCLUSIVELY on catalog declarations — the suspended
/// <see cref="PendingInvocation.ToolId"/> (the LLM function name derived from
/// <c>sprk_name</c>) is matched back to its <c>sprk_analysistool</c> row via the same
/// <see cref="ToolHandlerToAIFunctionAdapter.SanitiseToolName"/> transform the
/// projection used, and the handler resolves by the row's declared
/// <c>sprk_handlerclass</c>. No tool-name allow-list, no routing config outside the
/// catalog. Rows that are not chat-available or have no registered handler are
/// UNSUPPORTED — the caller keeps the honest <c>confirmed-unexecutable</c> path.
/// </para>
/// <para>
/// <b>User-OBO (spec MUST)</b>: the handler is resolved from the gate-resolve HTTP
/// request's DI scope, so scoped dependencies (<c>IDataverseUserClient</c> reading the
/// request's bearer token via <c>IHttpContextAccessor</c>) execute under the confirming
/// user's exchanged token — a create the user lacks privileges for fails with the
/// user's own access error. No app-only path is reachable from this class.
/// </para>
/// <para>
/// <b>ADR-040 (storage precedes rendering)</b>: on execution success the seam writes
/// (a) an addressable <c>loop@t{n}</c> <see cref="SessionOutput"/> (disposition
/// <c>record</c> — the payload documents an ALREADY-EXECUTED record side effect;
/// identifiers + handler result data) and (b) a <see cref="SessionToolChain"/> audit
/// entry (identifiers/counts only per NFR-07 — args summarized via
/// <see cref="AgentTurnContract.SummarizeArguments"/>, never verbatim) BEFORE the
/// result returns to the endpoint for client rendering. Note this entry does NOT ride
/// <see cref="IOutputRouter"/>: that seam routes Binding-declared dispositions and its
/// <c>record</c> leg (rendering a Binding output INTO a record) is a different,
/// not-yet-landed contract; here the record write already happened in the handler and
/// the stored entry is its addressable ledger evidence (reserved binding id
/// <c>"loop"</c> per ADR-040).
/// </para>
/// <para>
/// <b>DI posture (ADR-010 / ADR-032)</b>: NOT registered in DI. The endpoint constructs
/// it per-request via <see cref="TryCreate"/> from services that already exist behind
/// the compound-AI gate (<see cref="AnalysisToolService"/>, <see cref="IToolHandlerRegistry"/>,
/// <see cref="ChatSessionManager"/>). When any is unavailable (kill switch OFF),
/// <see cref="TryCreate"/> returns null and the endpoint keeps the honest
/// <c>confirmed-unexecutable</c> fallback — fail-honest, no Null-Object peer needed
/// because there is no registration to gate.
/// </para>
/// </remarks>
public sealed class TypedHandlerResumeExecutor
{
    private readonly AnalysisToolService _toolService;
    private readonly IToolHandlerRegistry _handlerRegistry;
    private readonly ChatSessionManager _sessionManager;
    private readonly ILogger _logger;
    private readonly string? _dataverseEnvironmentUrl;

    internal TypedHandlerResumeExecutor(
        AnalysisToolService toolService,
        IToolHandlerRegistry handlerRegistry,
        ChatSessionManager sessionManager,
        ILogger logger,
        string? dataverseEnvironmentUrl = null)
    {
        _toolService = toolService ?? throw new ArgumentNullException(nameof(toolService));
        _handlerRegistry = handlerRegistry ?? throw new ArgumentNullException(nameof(handlerRegistry));
        _sessionManager = sessionManager ?? throw new ArgumentNullException(nameof(sessionManager));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _dataverseEnvironmentUrl = string.IsNullOrWhiteSpace(dataverseEnvironmentUrl)
            ? null
            : dataverseEnvironmentUrl.TrimEnd('/');
    }

    /// <summary>
    /// Builds an executor from the REQUEST-scoped provider (user-OBO discipline: handlers
    /// and their <c>IDataverseUserClient</c> must come from the confirming request's
    /// scope). Returns null when the compound-AI services are not registered (kill
    /// switch OFF / degraded environment) — the caller keeps the honest
    /// <c>confirmed-unexecutable</c> path.
    /// </summary>
    public static TypedHandlerResumeExecutor? TryCreate(IServiceProvider requestServices, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(requestServices);
        var toolService = requestServices.GetService<AnalysisToolService>();
        var registry = requestServices.GetService<IToolHandlerRegistry>();
        var sessionManager = requestServices.GetService<ChatSessionManager>();
        if (toolService is null || registry is null || sessionManager is null)
        {
            return null;
        }

        // R4-3 (2026-07-07): the Dataverse environment URL is the server-known half of
        // the MDA deep link. Optional — link composition degrades to "no link" when the
        // options are not registered/populated (never blocks the resume).
        var environmentUrl = requestServices.GetService<IOptions<DataverseOptions>>()?.Value?.EnvironmentUrl;

        return new TypedHandlerResumeExecutor(toolService, registry, sessionManager, logger, environmentUrl);
    }

    /// <summary>
    /// A resolved (catalog row, handler) pair for a suspended tool id — resolution is
    /// PEEK-ONLY so the caller can decide supportability BEFORE consuming the gate.
    /// </summary>
    public sealed record ResumeResolution(AnalysisTool Tool, IToolHandler Handler);

    /// <summary>
    /// Terminal outcome of a resumed typed-handler execution.
    /// </summary>
    /// <param name="Success">Whether the handler executed successfully.</param>
    /// <param name="Summary">MODEL-facing summary (may contain instruction-to-model text).</param>
    /// <param name="Error">Handler-reported failure detail (instructive, user-safe).</param>
    /// <param name="LedgerKey">The <c>loop@t{n}</c> ledger key of the stored output.</param>
    /// <param name="UserSummary">
    /// USER-facing outcome sentence (R4-6, from <see cref="ToolResultMetadataKeys.UserSummary"/>).
    /// Null when the handler emits none — callers fall back to <paramref name="Summary"/>.
    /// </param>
    /// <param name="RecordEntityLogicalName">Created/updated record's table logical name (R4-3).</param>
    /// <param name="RecordId">Created/updated record's GUID (R4-3).</param>
    /// <param name="RecordUrl">
    /// Server-composed MDA deep link (R4-3):
    /// <c>{Dataverse:EnvironmentUrl}/main.aspx?pagetype=entityrecord&amp;etn={logicalName}&amp;id={guid}</c>.
    /// SEAM DECISION (documented per operator ask): the URL is composed SERVER-side because the
    /// transcript ✅ message is PERSISTED server-side (ChatEndpoints.PersistGateOutcomeMessageAsync)
    /// and must carry the clickable link durably across reloads — and because the model needs a
    /// real link IN ITS HISTORY to relay honestly. The server does not know the client's appid
    /// (it lives in the MDA session), so the link omits it — <c>main.aspx?pagetype=entityrecord</c>
    /// without appid resolves in the user's current app. A with-appid upgrade would thread the
    /// appid through session-create HostContext (client reads Xrm.Utility.getCurrentAppProperties)
    /// — recorded as a follow-up candidate, not built here.
    /// </param>
    /// <param name="Outcome">
    /// The Completion Engine's <see cref="OutcomeCard"/> for this gated outcome (task 035 /
    /// FR-A1-06). Present on the success path (composed FROM the stored <c>loop@t{n}</c> ledger
    /// output — store-before-render, ADR-040), carrying the audience-split summary + the
    /// server-composed record link; null on failure/validation legs (nothing durably completed,
    /// so there is no stored outcome to render — that "nothing happened" surface stays on the
    /// honest markdown refusal channel).
    /// </param>
    public sealed record ResumeOutcome(
        bool Success,
        string? Summary,
        string? Error,
        string? LedgerKey,
        string? UserSummary = null,
        string? RecordEntityLogicalName = null,
        Guid? RecordId = null,
        string? RecordUrl = null,
        OutcomeCard? Outcome = null);

    /// <summary>
    /// Resolves the suspended tool id back to its catalog row + registered handler.
    /// Returns null when the invocation is genuinely unsupported from this surface
    /// (no matching chat-available row, no registered handler, or handler without chat
    /// support) — the caller then closes the gate <c>confirmed-unexecutable</c>.
    /// </summary>
    public async Task<ResumeResolution?> TryResolveAsync(string toolId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(toolId))
        {
            return null;
        }

        // Same catalog read the projection uses (page size covers the whole small
        // chat-tool registry; the FR-P0-04 health check owns duplicate-claim drift).
        var listResult = await _toolService
            .ListToolsAsync(new ScopeListOptions { Page = 1, PageSize = 200 }, ct)
            .ConfigureAwait(false);

        foreach (var row in listResult.Items)
        {
            var availability = row.AvailableInContexts ?? ToolAvailabilityContext.Playbook;
            var isChatAvailable = availability is ToolAvailabilityContext.Chat or ToolAvailabilityContext.Both;
            if (!isChatAvailable || string.IsNullOrWhiteSpace(row.Name))
            {
                continue;
            }

            // The suspended ToolId is the LLM function name the gate wrapper preserved
            // verbatim from the projection — invert it with the SAME transform.
            if (!string.Equals(
                    ToolHandlerToAIFunctionAdapter.SanitiseToolName(row.Name),
                    toolId,
                    StringComparison.Ordinal))
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(row.HandlerClass))
            {
                _logger.LogWarning(
                    "[FR-P3-03][gate-resume] tool row '{ToolName}' (id={ToolRowId}) matches suspended tool " +
                    "'{ToolId}' but declares no HandlerClass — unsupported.",
                    row.Name, row.Id, toolId);
                return null;
            }

            var handler = _handlerRegistry.GetHandler(row.HandlerClass);
            if (handler is null)
            {
                _logger.LogWarning(
                    "[FR-P3-03][gate-resume] HandlerClass '{HandlerClass}' for suspended tool '{ToolId}' " +
                    "is not registered — unsupported.",
                    row.HandlerClass, toolId);
                return null;
            }

            if ((handler.SupportedInvocationContexts & InvocationContextKind.Chat) == 0)
            {
                _logger.LogWarning(
                    "[FR-P3-03][gate-resume] handler '{HandlerClass}' does not support chat invocation — unsupported.",
                    row.HandlerClass);
                return null;
            }

            return new ResumeResolution(row, handler);
        }

        _logger.LogInformation(
            "[FR-P3-03][gate-resume] no chat-available catalog row matches suspended tool '{ToolId}' — unsupported.",
            toolId);
        return null;
    }

    /// <summary>
    /// Executes the resumed invocation through the typed-handler stack and ledger-writes
    /// the outcome (SessionOutput + ToolChain) BEFORE returning (ADR-040 — the endpoint's
    /// JSON response is the rendering). Ledger-write failures after a successful side
    /// effect degrade to a loud log (the side effect happened; hiding it behind a 5xx
    /// would be dishonest), but the normal path stores first, returns second.
    /// </summary>
    /// <param name="resolution">The pre-resolved (row, handler) pair from <see cref="TryResolveAsync"/>.</param>
    /// <param name="invocation">The resumed pending invocation (payload already taken from the store).</param>
    /// <param name="userObjectId">The confirming principal's <c>oid</c> claim (deterministic id; null when absent).</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task<ResumeOutcome> ExecuteAsync(
        ResumeResolution resolution,
        PendingInvocation invocation,
        string? userObjectId,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(resolution);
        ArgumentNullException.ThrowIfNull(invocation);

        var context = new ChatInvocationContext
        {
            ChatSessionId = Guid.TryParse(invocation.SessionId, out var sessionGuid) ? sessionGuid : Guid.Empty,
            TenantId = invocation.TenantId,
            UserId = string.IsNullOrWhiteSpace(userObjectId) ? null : userObjectId,
            RequestedToolName = resolution.Tool.Name,
            ToolArgumentsJson = string.IsNullOrWhiteSpace(invocation.ArgsJson) ? "{}" : invocation.ArgsJson,
        };

        // NFR-07: identifiers only — never ArgsJson.
        _logger.LogInformation(
            "[FR-P3-03][gate-resume] executing confirmed invocation — gateId={GateId} tool={ToolId} " +
            "handler={HandlerClass} decisionId={DecisionId} session={SessionId} tenant={TenantId}",
            invocation.GateId, invocation.ToolId, resolution.Tool.HandlerClass,
            context.DecisionId, invocation.SessionId, invocation.TenantId);

        var startedAt = DateTimeOffset.UtcNow;
        ToolResult result;
        try
        {
            // FR-A1-05 (task 034) de-dupe note: task 034 added a PRE-SUSPEND ValidateChat at the
            // gate (SideEffectGateAIFunction), so a provably-doomed call is rejected with an honest
            // ❌ BEFORE it ever suspends and reaches this resume leg. This resume-side ValidateChat
            // is NOT redundant and is deliberately RETAINED: it is a behavior-PRESERVING re-check
            // for any call that passed pre-suspend (deterministic validation over the frozen Tier-3
            // args yields the same PASS), AND it is the ONLY validation that runs under the
            // CONFIRMING USER's OBO scope at execution time (this class resolves the handler +
            // IDataverseUserClient from the gate-resolve request scope — see the class remarks) —
            // it guards the suspend→confirm window against state drift (TOCTOU) that the
            // loop-context pre-suspend pass cannot see. Removing it would be a security regression,
            // not a de-dupe. It changes behavior only for a call that BECAME invalid after suspend,
            // which is exactly the case it must still catch.
            var validation = resolution.Handler.ValidateChat(context, resolution.Tool);
            if (!validation.IsValid)
            {
                var validationError = string.Join("; ", validation.Errors);
                _logger.LogWarning(
                    "[FR-P3-03][gate-resume] validation failed — gateId={GateId} tool={ToolId} errorCount={ErrorCount}",
                    invocation.GateId, invocation.ToolId, validation.Errors.Count);
                await AppendToolChainAsync(invocation, startedAt, citations: null, ct).ConfigureAwait(false);
                return new ResumeOutcome(false, null, validationError, null);
            }

            result = await resolution.Handler.ExecuteChatAsync(context, resolution.Tool, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "[FR-P3-03][gate-resume] handler threw — gateId={GateId} tool={ToolId} exceptionType={ExceptionType}",
                invocation.GateId, invocation.ToolId, ex.GetType().Name);
            await AppendToolChainAsync(invocation, startedAt, citations: null, ct).ConfigureAwait(false);
            return new ResumeOutcome(false, null, $"The confirmed action failed unexpectedly ({ex.GetType().Name}).", null);
        }

        var citations = ExtractCitationIds(result);
        if (!result.Success)
        {
            _logger.LogWarning(
                "[FR-P3-03][gate-resume] handler reported failure — gateId={GateId} tool={ToolId} errorCode={ErrorCode}",
                invocation.GateId, invocation.ToolId, result.ErrorCode);
            await AppendToolChainAsync(invocation, startedAt, citations, ct).ConfigureAwait(false);
            return new ResumeOutcome(false, null, result.ErrorMessage ?? "The confirmed action failed.", null);
        }

        // ── ADR-040: SessionOutput + ToolChain land BEFORE the result renders ─────────
        var ledgerKey = await AppendSessionOutputAsync(invocation, result, citations, ct).ConfigureAwait(false);
        await AppendToolChainAsync(invocation, startedAt, citations, ct).ConfigureAwait(false);

        _logger.LogInformation(
            "[FR-P3-03][gate-resume] confirmed invocation executed — gateId={GateId} tool={ToolId} " +
            "ledgerKey={LedgerKey} session={SessionId} tenant={TenantId}",
            invocation.GateId, invocation.ToolId, ledgerKey, invocation.SessionId, invocation.TenantId);

        // R4-6: prefer the handler's USER-facing outcome sentence for transcript rendering.
        // R4-3: compose the MDA deep link from the handler's record reference (see the
        // ResumeOutcome.RecordUrl doc for the server-side seam decision).
        var userSummary = ExtractUserSummary(result);
        var record = ExtractCreatedRecord(result);
        var recordUrl = record is not null ? BuildRecordUrl(record.EntityLogicalName, record.RecordId) : null;

        // ── Completion Engine (task 035 / FR-A1-06): compose the OutcomeCard FROM the stored
        // ledger output (store-before-render — ADR-040). Only when the ledger write succeeded
        // (non-null key): the card renders a STORED outcome by contract. The server-composed
        // record link (recordUrl) becomes the card's clickable link; the audience split rides
        // the user-facing sentence (UserSummary) over the model-facing Summary.
        var card = string.IsNullOrWhiteSpace(ledgerKey)
            ? null
            : CompletionEngine.ComposeForGateResume(
                ledgerOutputKey: ledgerKey!,
                userFacing: userSummary ?? result.Summary ?? "Completed.",
                internalDetail: result.Summary,
                link: recordUrl is null
                    ? null
                    : OutcomeCardLink.ServerComposed(recordUrl, "Open record", "record"));

        return new ResumeOutcome(
            true, result.Summary, null, ledgerKey,
            UserSummary: userSummary,
            RecordEntityLogicalName: record?.EntityLogicalName,
            RecordId: record?.RecordId,
            RecordUrl: recordUrl,
            Outcome: card);
    }

    /// <summary>
    /// Tolerantly reads the handler's user-facing outcome sentence
    /// (<see cref="ToolResultMetadataKeys.UserSummary"/>). Null when absent/malformed.
    /// </summary>
    internal static string? ExtractUserSummary(ToolResult result)
    {
        if (result.Metadata is null ||
            !result.Metadata.TryGetValue(ToolResultMetadataKeys.UserSummary, out var value))
        {
            return null;
        }

        return value switch
        {
            string s when !string.IsNullOrWhiteSpace(s) => s,
            JsonElement { ValueKind: JsonValueKind.String } el when el.GetString() is { Length: > 0 } s => s,
            _ => null,
        };
    }

    /// <summary>
    /// Tolerantly reads the handler's primary-record reference
    /// (<see cref="ToolResultMetadataKeys.CreatedRecord"/>). Null when absent/malformed —
    /// record-link enrichment is best-effort and must never fail the resume.
    /// </summary>
    internal static ToolCreatedRecord? ExtractCreatedRecord(ToolResult result)
    {
        if (result.Metadata is null ||
            !result.Metadata.TryGetValue(ToolResultMetadataKeys.CreatedRecord, out var value) ||
            value is null)
        {
            return null;
        }

        if (value is ToolCreatedRecord typed)
        {
            return string.IsNullOrWhiteSpace(typed.EntityLogicalName) || typed.RecordId == Guid.Empty
                ? null
                : typed;
        }

        try
        {
            JsonElement el;
            if (value is JsonElement pre)
            {
                el = pre;
            }
            else
            {
                using var doc = JsonDocument.Parse(JsonSerializer.SerializeToUtf8Bytes(value));
                el = doc.RootElement.Clone();
            }

            if (el.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            string? entity = null;
            if ((el.TryGetProperty("entityLogicalName", out var e1) || el.TryGetProperty("EntityLogicalName", out e1)) &&
                e1.ValueKind == JsonValueKind.String)
            {
                entity = e1.GetString();
            }

            Guid recordId = Guid.Empty;
            if ((el.TryGetProperty("recordId", out var r1) || el.TryGetProperty("RecordId", out r1)) &&
                r1.ValueKind == JsonValueKind.String)
            {
                Guid.TryParse(r1.GetString(), out recordId);
            }

            return string.IsNullOrWhiteSpace(entity) || recordId == Guid.Empty
                ? null
                : new ToolCreatedRecord(entity, recordId);
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// Composes the MDA deep link for a record (R4-3). Null when the Dataverse environment
    /// URL is unavailable in this process (link enrichment degrades, resume proceeds).
    /// </summary>
    internal string? BuildRecordUrl(string entityLogicalName, Guid recordId) =>
        _dataverseEnvironmentUrl is null || string.IsNullOrWhiteSpace(entityLogicalName) || recordId == Guid.Empty
            ? null
            : $"{_dataverseEnvironmentUrl}/main.aspx?pagetype=entityrecord&etn={entityLogicalName}&id={recordId:D}";

    /// <summary>
    /// Appends the executed result as an addressable <c>loop@t{n}</c> ledger output
    /// (disposition <c>record</c>): later turns reference the created record via the
    /// session-context block, and the create-task provenance chain stays auditable.
    /// Turn allocation mirrors <see cref="OutputRouter"/>'s monotonic output ordinal.
    /// </summary>
    private async Task<string?> AppendSessionOutputAsync(
        PendingInvocation invocation,
        ToolResult result,
        IReadOnlyList<string>? citations,
        CancellationToken ct)
    {
        try
        {
            var session = await _sessionManager
                .GetSessionAsync(invocation.TenantId, invocation.SessionId, ct)
                .ConfigureAwait(false);
            if (session is null)
            {
                _logger.LogWarning(
                    "[FR-P3-03][gate-resume] session vanished before ledger output write — gateId={GateId} session={SessionId}",
                    invocation.GateId, invocation.SessionId);
                return null;
            }

            var turn = (session.Outputs is { Count: > 0 } ? session.Outputs.Max(o => o.Turn) : 0) + 1;
            var payload = result.Data
                ?? JsonSerializer.SerializeToElement(new { summary = result.Summary ?? string.Empty });

            // ADR-040 inline size-cap ENFORCEMENT (task 055) — same rule as OutputRouter:
            // over-cap payloads store a deterministic truncation marker, never raw bulk.
            var capped = SessionLedger.CapInlinePayload(payload.Clone());
            if (capped.Truncated)
            {
                // NFR-07: sizes + identifiers only.
                _logger.LogWarning(
                    "[FR-P3-03][gate-resume] ledger output payload was {PayloadBytes} bytes (> cap {CapBytes}) — " +
                    "stored as a truncation marker (ADR-040) — gateId={GateId} session={SessionId}",
                    capped.OriginalBytes, SessionLedger.InlinePayloadCapBytes, invocation.GateId, invocation.SessionId);
            }

            var entry = new SessionOutput
            {
                Key = SessionLedger.BuildOutputKey("loop", turn),
                BindingId = "loop",
                UcId = invocation.ToolId,
                Turn = turn,
                Disposition = "record",
                Payload = capped.Payload,
                SourceRefs = citations is { Count: > 0 } ? citations : null,
                CreatedAt = DateTimeOffset.UtcNow,
            };

            var appended = new List<SessionOutput>(session.Outputs ?? Array.Empty<SessionOutput>()) { entry };
            await _sessionManager.UpdateSessionCacheAsync(session with { Outputs = appended }, ct).ConfigureAwait(false);

            _logger.LogInformation(
                "[FR-P3-03][gate-resume] ledger output stored — key={Key} ucid={UcId} disposition=record " +
                "session={SessionId} tenant={TenantId}",
                entry.Key, entry.UcId, invocation.SessionId, invocation.TenantId);

            return entry.Key;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // The side effect ALREADY executed — degrade loudly rather than masking a
            // real record write behind a storage 5xx (the ToolChain append below and the
            // endpoint response still surface the execution).
            _logger.LogError(ex,
                "[FR-P3-03][gate-resume] ledger output write failed AFTER execution — gateId={GateId} session={SessionId}",
                invocation.GateId, invocation.SessionId);
            return null;
        }
    }

    /// <summary>
    /// Appends the resumed call's ToolChain audit entry (NFR-07: tool id + redaction-safe
    /// args summary + outcome-bearing citations/duration only; never verbatim args).
    /// </summary>
    private async Task AppendToolChainAsync(
        PendingInvocation invocation,
        DateTimeOffset startedAt,
        IReadOnlyList<string>? citations,
        CancellationToken ct)
    {
        try
        {
            var entry = new SessionToolChain
            {
                Turn = invocation.Turn,
                Calls = new[]
                {
                    new SessionToolCall
                    {
                        ToolId = invocation.ToolId,
                        ArgsSummary = SummarizeArgsJson(invocation.ArgsJson),
                        ResultCount = null,
                        Citations = citations is { Count: > 0 } ? citations : null,
                        DurationMs = (long)(DateTimeOffset.UtcNow - startedAt).TotalMilliseconds,
                    },
                },
                CreatedAt = DateTimeOffset.UtcNow,
            };

            await _sessionManager
                .AppendToolChainAsync(invocation.TenantId, invocation.SessionId, entry, ct)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "[FR-P3-03][gate-resume] ToolChain append failed — gateId={GateId} session={SessionId}",
                invocation.GateId, invocation.SessionId);
        }
    }

    /// <summary>
    /// Rebuilds the redaction-safe args summary from the stored ArgsJson using THE one
    /// summarizer (<see cref="AgentTurnContract.SummarizeArguments"/> — identifier-shaped
    /// values kept, free text and content-bearing names redacted). Null on empty/malformed.
    /// </summary>
    internal static string? SummarizeArgsJson(string? argsJson)
    {
        if (string.IsNullOrWhiteSpace(argsJson))
        {
            return null;
        }

        try
        {
            var dict = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(argsJson);
            if (dict is null || dict.Count == 0)
            {
                return null;
            }

            var arguments = new AIFunctionArguments();
            foreach (var kvp in dict)
            {
                arguments[kvp.Key] = kvp.Value;
            }

            return AgentTurnContract.SummarizeArguments(arguments);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Tolerantly extracts deterministic citation ids (<c>chunkId</c>) from the handler's
    /// citations metadata — ids only, never excerpts (NFR-07).
    /// </summary>
    internal static IReadOnlyList<string>? ExtractCitationIds(ToolResult result)
    {
        if (result.Metadata is null ||
            !result.Metadata.TryGetValue(ToolResultMetadataKeys.Citations, out var citationsObj) ||
            citationsObj is null)
        {
            return null;
        }

        try
        {
            JsonElement arrayElement;
            if (citationsObj is JsonElement pre)
            {
                arrayElement = pre;
            }
            else if (citationsObj is string jsonText)
            {
                using var doc = JsonDocument.Parse(jsonText);
                arrayElement = doc.RootElement.Clone();
            }
            else
            {
                using var doc = JsonDocument.Parse(JsonSerializer.SerializeToUtf8Bytes(citationsObj));
                arrayElement = doc.RootElement.Clone();
            }

            if (arrayElement.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            var ids = new List<string>();
            foreach (var item in arrayElement.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                if ((item.TryGetProperty("chunkId", out var idEl) || item.TryGetProperty("ChunkId", out idEl)) &&
                    idEl.ValueKind == JsonValueKind.String &&
                    idEl.GetString() is { Length: > 0 } id)
                {
                    ids.Add(id);
                }
            }

            return ids.Count > 0 ? ids : null;
        }
        catch (Exception)
        {
            // Runs AFTER a real side effect — citation extraction is best-effort audit
            // enrichment and must never fail the resume (mirrors the adapter's tolerant
            // metadata post-processing).
            return null;
        }
    }
}
