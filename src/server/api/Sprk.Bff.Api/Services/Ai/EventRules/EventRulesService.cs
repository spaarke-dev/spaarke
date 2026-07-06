using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Sprk.Bff.Api.Api.Ai;
using Sprk.Bff.Api.Models.Ai.Chat;
using Sprk.Bff.Api.Services.Ai.Chat;
using Sprk.Bff.Api.Services.Ai.LinearConsumers;
using Sprk.Bff.Api.Services.Ai.PublicContracts;

namespace Sprk.Bff.Api.Services.Ai.EventRules;

/// <summary>
/// Default <see cref="IEventRulesService"/> — the FR-P1-03 Event entry path.
/// </summary>
/// <remarks>
/// <para>
/// <b>Execution model</b> (canonical §7.1): the surface event resolves to ordered
/// members via <see cref="IConsumerRoutingService.ResolveEventBindingsAsync"/>
/// (the Binding table is the ONLY routing surface — ADR-039; this class carries
/// zero rule config). Each member executes as a catalog capability: Action row →
/// prompted executor (<see cref="IActionRunner"/>) → <see cref="IOutputRouter"/>
/// (universal ledger write BEFORE any event is emitted — ADR-040). The launch
/// binding is <c>document_uploaded → [chat-classify(1), chat-summarize(2)]</c>.
/// </para>
/// <para>
/// <b>Bounds (FR-P1-03, all test-proven)</b>:
/// <list type="bullet">
///   <item><b>(a) per-user daily cost cap</b> — <see cref="IEventPathUserState"/>
///   counter vs <see cref="EventRulesOptions.DailyExecutionCap"/>; cap-exceeded
///   defers with a manual-run chip + <c>eventpath.bound_denial</c> telemetry
///   (NFR-09: graceful rendered notice, never a silent drop).</item>
///   <item><b>(b) per-user opt-out</b> — checked before any execution; notice +
///   manual-run chip.</item>
///   <item><b>(c) bulk top-1</b> — a multi-file batch auto-runs against the FIRST
///   file of the batch only (deterministic: request order, which is upload order);
///   a "Summarize all files?" chip carries the full batch to the Click path.</item>
///   <item><b>(d) explicit-command supersede</b> — a typed command with the upload
///   suppresses the rule entirely (the Text path wins).</item>
///   <item><b>precondition: empty attachments</b> — the r7 guard re-homed here
///   (task 025): the rule never fires with a zero-file set.</item>
/// </list>
/// </para>
/// <para>
/// <b>M4 classify-confidence policy</b> (the surviving D1 dial, canonical §7.1/§7.4):
/// when a member's output carries the classification shape (<c>docType</c> +
/// <c>confidence</c>), the confidence gates continuation — at/above
/// <see cref="EventRulesOptions.ClassifyConfidenceThreshold"/> the next member runs
/// silently; below it, an <c>event_confirmation</c> turn is emitted, remaining
/// members are suspended, and the confirm chip resumes via the Click path. The
/// classify result is also persisted onto <see cref="ChatSessionFile.ClassifiedDocType"/> /
/// <see cref="ChatSessionFile.ClassifiedConfidence"/> (the pre-existing fields —
/// Layer 0 wiring per the migration map).
/// </para>
/// <para>
/// <b>ADR-013 placement</b>: lives in <c>Services/Ai/EventRules/</c> (in-zone AI
/// territory); MAY consume executor internals. External CRUD code stays on the
/// PublicContracts facade.
/// </para>
/// </remarks>
public sealed class EventRulesService : IEventRulesService
{
    private readonly ChatSessionManager _sessionManager;
    private readonly IConsumerRoutingService _consumerRouting;
    private readonly IScopeResolverService _scopeResolver;
    private readonly IActionRunner _actionRunner;
    private readonly ISessionFileTextSource _sessionFileTextSource;
    private readonly IOutputRouter _outputRouter;
    private readonly IEventPathUserState _userState;
    private readonly EventRulesTelemetry _telemetry;
    private readonly EventRulesOptions _options;
    private readonly ILogger<EventRulesService> _logger;

    public EventRulesService(
        ChatSessionManager sessionManager,
        IConsumerRoutingService consumerRouting,
        IScopeResolverService scopeResolver,
        IActionRunner actionRunner,
        ISessionFileTextSource sessionFileTextSource,
        IOutputRouter outputRouter,
        IEventPathUserState userState,
        EventRulesTelemetry telemetry,
        IOptions<EventRulesOptions> options,
        ILogger<EventRulesService> logger)
    {
        _sessionManager = sessionManager ?? throw new ArgumentNullException(nameof(sessionManager));
        _consumerRouting = consumerRouting ?? throw new ArgumentNullException(nameof(consumerRouting));
        _scopeResolver = scopeResolver ?? throw new ArgumentNullException(nameof(scopeResolver));
        _actionRunner = actionRunner ?? throw new ArgumentNullException(nameof(actionRunner));
        _sessionFileTextSource = sessionFileTextSource ?? throw new ArgumentNullException(nameof(sessionFileTextSource));
        _outputRouter = outputRouter ?? throw new ArgumentNullException(nameof(outputRouter));
        _userState = userState ?? throw new ArgumentNullException(nameof(userState));
        _telemetry = telemetry ?? throw new ArgumentNullException(nameof(telemetry));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<ChatSseEvent> FireAsync(
        SurfaceEventRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.EventName, $"{nameof(request)}.{nameof(request.EventName)}");
        ArgumentException.ThrowIfNullOrWhiteSpace(request.TenantId, $"{nameof(request)}.{nameof(request.TenantId)}");
        ArgumentException.ThrowIfNullOrWhiteSpace(request.SessionId, $"{nameof(request)}.{nameof(request.SessionId)}");
        ArgumentException.ThrowIfNullOrWhiteSpace(request.UserOid, $"{nameof(request)}.{nameof(request.UserOid)}");

        // ── Bound (d): explicit-command supersede — the Text path wins. Checked FIRST:
        // a typed command means the user asked for something specific; the auto-rule
        // must not spend money or attention alongside it.
        if (!string.IsNullOrWhiteSpace(request.TypedCommand))
        {
            _telemetry.RecordBoundDenial(request.EventName, EventNoticeReasons.Superseded);
            _logger.LogInformation(
                "EventRules: {Event} superseded by explicit command. tenant={TenantId} session={SessionId} user={UserOid}",
                request.EventName, request.TenantId, request.SessionId, request.UserOid);
            yield return NoticeEvent(EventNoticeReasons.Superseded,
                "Automatic analysis was skipped because you gave an explicit command — your command takes precedence.");
            yield return DoneEvent();
            yield break;
        }

        // Session load — not-found surfaces as InvalidOperationException("... not found ...")
        // which the endpoint maps to 404 (same contract as SessionSummarizeOrchestrator).
        ChatSession? session = await _sessionManager
            .GetSessionAsync(request.TenantId, request.SessionId, cancellationToken)
            .ConfigureAwait(false);
        if (session is null)
        {
            throw new InvalidOperationException(
                $"Chat session '{request.SessionId}' not found for tenant '{request.TenantId}'.");
        }

        // ── Precondition: empty attachments (task 025 r7 guard re-home). Resolve the
        // batch against the session manifest; the rule never fires on a zero-file set.
        var batchFiles = ResolveBatchFiles(request.FileIds, session.UploadedFiles);
        if (batchFiles.Count == 0)
        {
            _telemetry.RecordBoundDenial(request.EventName, EventNoticeReasons.NoAttachments);
            _logger.LogInformation(
                "EventRules: {Event} precondition failed — empty attachment set. tenant={TenantId} session={SessionId}",
                request.EventName, request.TenantId, request.SessionId);
            yield return NoticeEvent(EventNoticeReasons.NoAttachments,
                "No uploaded files were available yet, so automatic analysis did not run.");
            yield return DoneEvent();
            yield break;
        }

        // ── Rule resolution — the Binding table is THE routing surface (ADR-039).
        var members = await _consumerRouting
            .ResolveEventBindingsAsync(request.EventName, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        if (members.Count == 0)
        {
            _telemetry.RecordBoundDenial(request.EventName, EventNoticeReasons.NoRule);
            _logger.LogInformation(
                "EventRules: no enabled Binding declares membership in event '{Event}'. tenant={TenantId}",
                request.EventName, request.TenantId);
            yield return NoticeEvent(EventNoticeReasons.NoRule,
                "No automatic analysis is configured for this event.");
            yield return DoneEvent();
            yield break;
        }

        // The last member is the rule's "primary" user-visible capability (summarize for the
        // launch rule) — deferral/manual-run chips target it so a bounded-out user can still
        // run the capability by explicit Click (user-initiated ≠ Event-path budget).
        var deferTarget = members[^1];
        var allFileIds = batchFiles.Select(f => f.FileId).ToList();

        // ── Bound (b): per-user opt-out.
        if (await _userState.IsOptedOutAsync(request.TenantId, request.UserOid, cancellationToken).ConfigureAwait(false))
        {
            _telemetry.RecordBoundDenial(request.EventName, EventNoticeReasons.OptedOut);
            _logger.LogInformation(
                "EventRules: {Event} skipped — user opted out. tenant={TenantId} user={UserOid}",
                request.EventName, request.TenantId, request.UserOid);
            yield return NoticeEvent(EventNoticeReasons.OptedOut,
                "Automatic analysis is turned off for your account. You can still run it on demand.",
                chips: new[] { ManualRunChip(deferTarget, allFileIds) });
            yield return DoneEvent();
            yield break;
        }

        // ── Bound (a): per-user daily Event-path budget (NFR-09 / ADR-016). The whole
        // rule (member count) must fit in the remaining budget; a cap hit DEFERS with a
        // chip (spec §7.1) and is telemetered — never a silent drop.
        var usedToday = await _userState
            .GetTodayExecutionCountAsync(request.TenantId, request.UserOid, cancellationToken)
            .ConfigureAwait(false);
        if (usedToday + members.Count > _options.DailyExecutionCap)
        {
            _telemetry.RecordBoundDenial(request.EventName, EventNoticeReasons.DailyCap);
            _logger.LogWarning(
                "EventRules: {Event} deferred — daily Event-path budget reached. tenant={TenantId} user={UserOid} usedToday={UsedToday} cap={Cap}",
                request.EventName, request.TenantId, request.UserOid, usedToday, _options.DailyExecutionCap);
            yield return NoticeEvent(EventNoticeReasons.DailyCap,
                "You've reached today's automatic-analysis limit. You can still run analysis on demand.",
                chips: new[] { ManualRunChip(deferTarget, allFileIds) });
            yield return DoneEvent();
            yield break;
        }

        // ── Bound (c): bulk top-1 — auto-run the FIRST file of the batch only; the
        // "summarize all?" chip carries the full batch to the Click path.
        var targetFile = batchFiles[0];
        var isBulk = batchFiles.Count > 1;

        _logger.LogInformation(
            "EventRules: firing {Event} with {MemberCount} member(s) (top-1 file of {BatchCount}). " +
            "tenant={TenantId} session={SessionId} user={UserOid} correlationId={CorrelationId}",
            request.EventName, members.Count, batchFiles.Count,
            request.TenantId, request.SessionId, request.UserOid, request.CorrelationId);

        // Fetch the target file's text ONCE — every member of the launch rule consumes the
        // same document. Failures surface as a standard chat error event (graceful stream UX).
        SessionFileText text = default!;
        string? fetchError = null;
        try
        {
            text = await _sessionFileTextSource
                .FetchAsync(request.TenantId, request.SessionId, new[] { targetFile }, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "EventRules: session-file text retrieval failed. tenant={TenantId} session={SessionId} fileId={FileId}",
                request.TenantId, request.SessionId, targetFile.FileId);
            fetchError = "Automatic analysis could not read the uploaded file. You can retry from the file menu.";
        }
        if (fetchError is null && string.IsNullOrWhiteSpace(text.ExtractedText))
        {
            fetchError = "The uploaded file contained no readable text, so automatic analysis did not run.";
        }
        if (fetchError is not null)
        {
            yield return ErrorEvent(fetchError);
            yield break;
        }

        // ── Execute members in declared order. Each execution: budget++ → ActionRunner →
        // OutputRouter (ledger write BEFORE the SSE event that renders it — ADR-040).
        var currentSession = session;
        Binding? lastCompleted = null;
        foreach (var member in members)
        {
            // Catalog integrity guards — operator-actionable, loud, stop the chain.
            if (member.ActionId is null || member.ActionId.Value == Guid.Empty)
            {
                yield return ErrorEvent(
                    $"Event rule member '{member.ConsumerType}' has no Action target — fix the sprk_playbookconsumer row's sprk_action lookup.");
                yield break;
            }
            if (member.ActionKind != ActionKind.Prompted)
            {
                yield return ErrorEvent(
                    $"Event rule member '{member.ConsumerType}' targets a non-prompted Action ('{member.ActionKind}'); the P1 Event path executes prompted Actions only.");
                yield break;
            }

            var action = await _scopeResolver
                .GetActionAsync(member.ActionId.Value, cancellationToken)
                .ConfigureAwait(false);
            if (action is null)
            {
                yield return ErrorEvent(
                    $"Event rule member '{member.ConsumerType}' references Action {member.ActionId.Value}, which could not be loaded from Dataverse.");
                yield break;
            }

            // Budget consumption precedes the LLM call (the spend happens regardless of outcome).
            await _userState
                .AddExecutionsAsync(request.TenantId, request.UserOid, 1, cancellationToken)
                .ConfigureAwait(false);

            JsonElement output = default;
            string? memberError = null;
            try
            {
                output = await _actionRunner
                    .RunAsync(
                        action,
                        new DocumentText
                        {
                            DocumentId = null,
                            FileName = targetFile.FileName,
                            ExtractedText = text.ExtractedText,
                        },
                        new LinearRunContext
                        {
                            ConsumerType = member.ConsumerType,
                            CorrelationId = request.CorrelationId,
                            TenantId = request.TenantId,
                        },
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "EventRules: prompted executor failed for member binding {BindingId} action {ActionId}. tenant={TenantId} session={SessionId}",
                    member.BindingId, action.Id, request.TenantId, request.SessionId);
                memberError = "Automatic analysis failed for the uploaded file. You can retry on demand.";
            }
            if (memberError is not null)
            {
                _telemetry.RecordExecution(request.EventName, member.Ucid, "failed");
                yield return ErrorEvent(memberError);
                yield break;
            }

            // M4 classification shape detection: docType + confidence on the member output.
            var classification = TryReadClassification(output);
            if (classification is { } cls)
            {
                // Layer-0 wiring: persist onto the pre-existing ChatSessionFile confidence
                // fields BEFORE routing, so the OutputRouter's single session write carries
                // the manifest enrichment together with the ledger entry.
                currentSession = WithClassifiedFile(currentSession, targetFile.FileId, cls.DocType, cls.Confidence);
            }

            // ── ADR-040 SEAM: universal ledger write BEFORE the rendering event below.
            var routed = await _outputRouter
                .RouteAsync(
                    currentSession,
                    member,
                    output,
                    sourceRefs: new[] { targetFile.FileId },
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            currentSession = routed.Session;

            if (classification is { } c)
            {
                _telemetry.RecordExecution(request.EventName, member.Ucid, "success");
                yield return new ChatSseEvent(EventRuleSseEvents.Classification, null,
                    new EventClassificationData(
                        FileId: targetFile.FileId,
                        FileName: targetFile.FileName,
                        DocType: c.DocType,
                        Confidence: c.Confidence,
                        BindingId: member.BindingId.ToString(),
                        Ucid: member.Ucid,
                        LedgerKey: routed.Entry.Key));

                // M4 policy: below-threshold confidence suspends the remaining members into
                // a confirmation turn; the confirm chip resumes via the Click path.
                if (c.Confidence < _options.ClassifyConfidenceThreshold && member != members[^1])
                {
                    _telemetry.RecordExecution(request.EventName, members[^1].Ucid, "gated");
                    _logger.LogInformation(
                        "EventRules: M4 confirmation gate fired (confidence={Confidence} < threshold={Threshold}). " +
                        "tenant={TenantId} session={SessionId} docType={DocType}",
                        c.Confidence, _options.ClassifyConfidenceThreshold,
                        request.TenantId, request.SessionId, c.DocType);
                    yield return new ChatSseEvent(EventRuleSseEvents.Confirmation, null,
                        new EventConfirmationData(
                            FileId: targetFile.FileId,
                            DocType: c.DocType,
                            Confidence: c.Confidence,
                            Threshold: _options.ClassifyConfidenceThreshold,
                            Message: $"This looks like it might be a {c.DocType} — is that correct? Confirm to get a summary.",
                            Chips: new[]
                            {
                                new EventChip(
                                    members[^1].BindingId.ToString(),
                                    $"Yes, it's a {c.DocType} — summarize it",
                                    new { fileIds = new[] { targetFile.FileId }, confirmedDocType = c.DocType }),
                            }));
                    yield return DoneEvent();
                    yield break;
                }
            }
            else
            {
                _telemetry.RecordExecution(request.EventName, member.Ucid, "success");
                // Render-follows-store: the payload comes from the STORED ledger entry.
                yield return new ChatSseEvent(EventRuleSseEvents.Output, null,
                    new EventOutputData(
                        BindingId: member.BindingId.ToString(),
                        Ucid: member.Ucid,
                        LedgerKey: routed.Entry.Key,
                        Disposition: routed.Entry.Disposition,
                        Payload: routed.Entry.Payload));
            }

            lastCompleted = member;
        }

        // ── Chips: the completed rule's next-step affordances — the last member's curated
        // sprk_chiptransitions (D4) + the bulk "Summarize all files?" chip (bound c).
        if (lastCompleted is not null)
        {
            var chips = new List<EventChip>();
            if (isBulk)
            {
                chips.Add(new EventChip(
                    lastCompleted.BindingId.ToString(),
                    $"Summarize all {batchFiles.Count} files?",
                    new { fileIds = allFileIds }));
            }
            foreach (var transition in lastCompleted.ChipTransitions)
            {
                if (!string.IsNullOrWhiteSpace(transition.TargetBindingId) &&
                    !string.IsNullOrWhiteSpace(transition.ChipLabel))
                {
                    chips.Add(new EventChip(transition.TargetBindingId!, transition.ChipLabel!));
                }
            }
            if (chips.Count > 0)
            {
                yield return new ChatSseEvent(EventRuleSseEvents.Chips, null,
                    new EventChipsData(lastCompleted.BindingId.ToString(), chips));
            }
        }

        yield return DoneEvent();
    }

    // ─── Helpers ────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Resolves the uploaded batch against the session manifest, preserving the
    /// caller's batch order (upload order — the deterministic "top-1" for bound c).
    /// Unknown ids are ignored; a null/empty batch falls back to the full manifest.
    /// </summary>
    private static IReadOnlyList<ChatSessionFile> ResolveBatchFiles(
        IReadOnlyList<string>? requestedFileIds,
        IReadOnlyList<ChatSessionFile>? uploadedFiles)
    {
        var manifest = uploadedFiles ?? Array.Empty<ChatSessionFile>();
        if (requestedFileIds is not { Count: > 0 })
        {
            return manifest;
        }

        var byId = manifest.ToDictionary(f => f.FileId, StringComparer.Ordinal);
        var resolved = new List<ChatSessionFile>(requestedFileIds.Count);
        foreach (var id in requestedFileIds)
        {
            if (byId.TryGetValue(id, out var file))
            {
                resolved.Add(file);
            }
        }
        return resolved;
    }

    /// <summary>
    /// Detects the classification output shape: a top-level object carrying
    /// <c>docType</c> (string) + <c>confidence</c> (number). This is event-rule
    /// POLICY shape detection (which member gates continuation), not capability
    /// logic — what the classifier says lives entirely in its Action prompt/schema.
    /// </summary>
    private static (string DocType, double Confidence)? TryReadClassification(JsonElement output)
    {
        if (output.ValueKind != JsonValueKind.Object)
        {
            return null;
        }
        if (!output.TryGetProperty("docType", out var docTypeEl) || docTypeEl.ValueKind != JsonValueKind.String)
        {
            return null;
        }
        if (!output.TryGetProperty("confidence", out var confidenceEl) || confidenceEl.ValueKind != JsonValueKind.Number)
        {
            return null;
        }
        var docType = docTypeEl.GetString();
        return string.IsNullOrWhiteSpace(docType)
            ? null
            : (docType!, Math.Clamp(confidenceEl.GetDouble(), 0d, 1d));
    }

    /// <summary>Returns the session with the target file's classify-confidence fields populated.</summary>
    private static ChatSession WithClassifiedFile(
        ChatSession session, string fileId, string docType, double confidence)
    {
        var files = (session.UploadedFiles ?? Array.Empty<ChatSessionFile>())
            .Select(f => f.FileId == fileId
                ? f with { ClassifiedDocType = docType, ClassifiedConfidence = confidence }
                : f)
            .ToList();
        return session with { UploadedFiles = files };
    }

    /// <summary>Chip that lets a bounded-out user run the rule's primary capability by explicit Click.</summary>
    private static EventChip ManualRunChip(Binding deferTarget, IReadOnlyList<string> fileIds)
        => new(deferTarget.BindingId.ToString(), "Summarize now", new { fileIds });

    private static ChatSseEvent NoticeEvent(string reason, string message, IReadOnlyList<EventChip>? chips = null)
        => new(EventRuleSseEvents.Notice, null, new EventNoticeData(reason, message, chips));

    private static ChatSseEvent ErrorEvent(string message)
        => new("error", message);

    private static ChatSseEvent DoneEvent()
        => new(EventRuleSseEvents.Done, null);
}
