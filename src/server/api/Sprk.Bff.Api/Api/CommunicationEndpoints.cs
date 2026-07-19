using System.Collections.Concurrent;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Microsoft.Xrm.Sdk;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.Filters;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Infrastructure.Exceptions;
using Sprk.Bff.Api.Services.Ai.Context;
using Sprk.Bff.Api.Services.Communication;
using Sprk.Bff.Api.Services.Communication.Access;
using Sprk.Bff.Api.Services.Communication.Models;
using Sprk.Bff.Api.Services.Jobs;

namespace Sprk.Bff.Api.Api;

/// <summary>
/// Communication endpoints for sending emails via Graph API.
/// POST /send: Single email send. POST /send-bulk: Bulk send to multiple recipients.
/// GET /{id}/status: Communication status lookup.
/// POST /accounts/{id}/verify: Mailbox verification.
/// POST /incoming-webhook: Graph change notification receiver for inbound emails.
/// </summary>
public static class CommunicationEndpoints
{
    /// <summary>
    /// Job type for processing incoming email notifications from Graph webhooks.
    /// </summary>
    private const string JobTypeIncomingCommunication = "IncomingCommunication";

    /// <summary>
    /// In-memory deduplication cache for Graph notification IDs.
    /// Prevents processing the same notification twice when Graph retries delivery.
    /// Entries expire after 10 minutes (Graph retry window is typically under 5 minutes).
    /// </summary>
    private static readonly ConcurrentDictionary<string, DateTimeOffset> _recentNotifications = new();

    /// <summary>
    /// How long to keep notification IDs in the deduplication cache.
    /// </summary>
    private static readonly TimeSpan DeduplicationWindow = TimeSpan.FromMinutes(10);

    public static IEndpointRouteBuilder MapCommunicationEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/communications")
            .RequireAuthorization()
            .WithTags("Communications");

        group.MapPost("/send", SendCommunicationAsync)
            .AddEndpointFilter<CommunicationAuthorizationFilter>()
            .WithName("SendCommunication")
            .WithDescription("Send an email communication via Microsoft Graph API")
            .Produces<SendCommunicationResponse>(StatusCodes.Status200OK)
            .Produces<ProblemDetails>(StatusCodes.Status400BadRequest)
            .Produces<ProblemDetails>(StatusCodes.Status403Forbidden)
            .Produces<ProblemDetails>(StatusCodes.Status500InternalServerError);

        group.MapPost("/send-bulk", SendBulkCommunicationAsync)
            .AddEndpointFilter<CommunicationAuthorizationFilter>()
            .WithName("SendBulkCommunication")
            .WithDescription("Send an email communication to multiple recipients via Microsoft Graph API")
            .Produces<BulkSendResponse>(StatusCodes.Status200OK)
            .Produces<BulkSendResponse>(207)
            .Produces<ProblemDetails>(StatusCodes.Status400BadRequest)
            .Produces<ProblemDetails>(StatusCodes.Status403Forbidden)
            .Produces<ProblemDetails>(StatusCodes.Status500InternalServerError);

        group.MapGet("/{id:guid}/status", GetCommunicationStatusAsync)
            .WithName("GetCommunicationStatus")
            .WithDescription("Get the status of a sent communication")
            .Produces<CommunicationStatusResponse>(StatusCodes.Status200OK)
            .Produces<ProblemDetails>(StatusCodes.Status404NotFound);

        // POST /threads/direct — start (or reuse) a 1:1 direct thread with another Spaarke user (task 043 / FR-09).
        // Not anchored to a record (no ADR-024 regarding); membership is the EXPLICIT two-party list (thread
        // ownership + a POA "Manage access" share to the other participant) — see IDirectThreadAccessService.
        // Ordered-pair dedup: starting a 1:1 with the same person twice reuses the SAME thread.
        group.MapPost("/threads/direct", StartDirectThreadAsync)
            .AddEndpointFilter<CommunicationAuthorizationFilter>()
            .WithName("StartDirectThread")
            .WithDescription("Start (or reuse) a 1:1 direct thread with another Spaarke user. Not record-anchored; membership is the explicit two-party list.")
            .Produces<StartDirectThreadResponse>(StatusCodes.Status200OK)
            .Produces<ProblemDetails>(StatusCodes.Status400BadRequest)
            .Produces<ProblemDetails>(StatusCodes.Status403Forbidden);

        // GET /threads/{threadId}/messages — the polling timeline's thread-read (task 050 / FR-11). Returns the
        // caller's READABLE sprk_communication rows in the thread, impersonated (Dataverse row-level security) +
        // the shared internal-only/privilege filter (task 042). Optional ?since=<iso> for incremental polls;
        // ?top=<n> pages. NO ACS call (Dataverse is the record). "No visible messages" returns an empty 200 (never
        // 404) so a private thread's existence is not leaked (NFR-06).
        group.MapGet("/threads/{threadId:guid}/messages", GetThreadMessagesAsync)
            .AddEndpointFilter<CommunicationAuthorizationFilter>()
            .WithName("GetThreadMessages")
            .WithDescription("Read a thread's messages for the polling timeline (access-filtered; impersonated). Optional ?since=<iso8601> and ?top=<n>.")
            .Produces<ThreadReadResult>(StatusCodes.Status200OK)
            .Produces<ProblemDetails>(StatusCodes.Status400BadRequest)
            .Produces<ProblemDetails>(StatusCodes.Status403Forbidden);

        // GET /threads/{threadId}/unread-count — the unread indicator's poll (task 050 / FR-11). Count of READABLE
        // messages newer than the caller's last-seen marker (?since=<iso>; omitted = all). Same access filter as
        // thread-read — a message the caller cannot read is never counted (NFR-06). Projected + bounded (NFR-07).
        group.MapGet("/threads/{threadId:guid}/unread-count", GetThreadUnreadCountAsync)
            .AddEndpointFilter<CommunicationAuthorizationFilter>()
            .WithName("GetThreadUnreadCount")
            .WithDescription("Count a thread's unread (readable) messages since the caller's last-seen marker (?since=<iso8601>).")
            .Produces<UnreadCountResult>(StatusCodes.Status200OK)
            .Produces<ProblemDetails>(StatusCodes.Status400BadRequest)
            .Produces<ProblemDetails>(StatusCodes.Status403Forbidden);

        // GET /api/communications/by-regarding/{entityType}/{id} — ALL of a regarding record's threads + their
        // messages for the record-level regarding-mode Timeline (R2 task 010 / FR-01, Surface 1). Entity-set-agnostic
        // across all 11 ADR-024 regarding families (matter, contact, …). Impersonated (Dataverse row-level security)
        // + the SAME internal-only/privilege access filter as the thread-read — private/internal-only content never
        // leaks (NFR-03). A bad entityType → 400 ProblemDetails (ADR-019). Same DTO shape as the R1 thread-read.
        group.MapGet("/by-regarding/{entityType}/{id:guid}", GetCommunicationsByRegardingAsync)
            .AddEndpointFilter<CommunicationAuthorizationFilter>()
            .WithName("GetCommunicationsByRegarding")
            .WithDescription("Read ALL of a regarding record's threads + messages for the regarding-mode Timeline (access-filtered; impersonated; entity-set-agnostic across the 11 ADR-024 families).")
            .Produces<RegardingReadResult>(StatusCodes.Status200OK)
            .Produces<ProblemDetails>(StatusCodes.Status400BadRequest)
            .Produces<ProblemDetails>(StatusCodes.Status403Forbidden);

        // GET /api/communications?thread=&regarding=&channel=&from=&to=&participant= — filtered cross-record
        // communication query (R2 task 011 / FR-02; `participant=` wired in task 051) backing the global grid +
        // workspace widget. thread/regarding/channel/date/participant facets all compose onto the SAME
        // impersonation read path + access filter (NFR-03). `participant=` joins the sprk_communicationparticipant
        // junction (003/050) on its typed person lookups (role-exact, FK-backed) or, for an unresolved external
        // party, an exact match on its address column — never a text-LIKE scan. Unknown/empty/malformed filters
        // degrade gracefully to a 400 ProblemDetails (ADR-019) — never a 500, never an unfiltered dump.
        group.MapGet("/", QueryCommunicationsAsync)
            .AddEndpointFilter<CommunicationAuthorizationFilter>()
            .WithName("QueryCommunications")
            .WithDescription("Filtered cross-record communication query (thread/regarding/channel/date/participant facets; access-filtered; impersonated).")
            .Produces<CommunicationQueryResult>(StatusCodes.Status200OK)
            .Produces<ProblemDetails>(StatusCodes.Status400BadRequest)
            .Produces<ProblemDetails>(StatusCodes.Status403Forbidden);

        group.MapPost("/{id:guid}/archive", ArchiveCommunicationAsync)
            .AddEndpointFilter<CommunicationAuthorizationFilter>()
            .WithName("ArchiveCommunication")
            .WithDescription("Archive an existing communication to SharePoint on demand (.eml Document + a Document per attachment). Idempotent — a communication already archived returns AlreadyArchived without duplicating.")
            .Produces<ArchiveCommunicationResult>(StatusCodes.Status200OK)
            .Produces<ProblemDetails>(StatusCodes.Status404NotFound)
            .Produces<ProblemDetails>(StatusCodes.Status500InternalServerError);

        group.MapPost("/{id:guid}/suggest-associations", SuggestAssociationsAsync)
            .AddEndpointFilter<CommunicationAuthorizationFilter>()
            .WithName("SuggestCommunicationAssociations")
            .WithDescription("Preview the Association Engine's regarding suggestions for a stored communication (target(s) + confidence + provenance). READ-ONLY — evaluates the rungs on demand WITHOUT writing to the record. Auth-scoped via the endpoint filter (NFR-07); AI-flagged privilege is surfaced as a signal, never decided (ADR-015).")
            .Produces<SuggestAssociationsResponse>(StatusCodes.Status200OK)
            .Produces<ProblemDetails>(StatusCodes.Status404NotFound)
            .Produces<ProblemDetails>(StatusCodes.Status500InternalServerError);

        group.MapPost("/accounts/{id:guid}/verify", VerifyCommunicationAccountAsync)
            .AddEndpointFilter<CommunicationAuthorizationFilter>()
            .WithName("VerifyCommunicationAccount")
            .WithDescription("Verify a communication account's mailbox capabilities (send and/or read)")
            .Produces<VerificationResult>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);

        // POST /api/communications/incoming-webhook - Graph webhook receiver (AllowAnonymous + HMAC + clientState)
        // Registered on app (not group) to avoid RequireAuthorization from the group.
        // Defense-in-depth (task 044):
        //   1. WebhookSignatureFilter validates X-Hub-Signature-256 (HMAC-SHA256 over body)
        //      using Communication:WebhookSigningKey. Subscription-validation handshakes
        //      (?validationToken=...) bypass HMAC since Graph does not sign that probe.
        //   2. Handler validates the body-level clientState in constant time against
        //      Communication:WebhookClientState (the Graph-native shared secret).
        // Both checks are mandatory — there is no DEVELOPMENT_MODE bypass anywhere.
        app.MapPost("/api/communications/incoming-webhook", HandleIncomingWebhookAsync)
            .AllowAnonymous()
            .RequireWebhookSignature(
                signatureHeader: WebhookSignatureFilter.DefaultSignatureHeader,
                signingKeyAccessor: sp => sp.GetRequiredService<IOptions<CommunicationOptions>>().Value.WebhookSigningKey,
                filterName: "Communication")
            .RequireRateLimiting("webhook-graph") // Task AUTHV2-049 — 600/min per source IP (defense in depth)
            .WithName("CommunicationIncomingWebhook")
            .WithTags("Communications")
            .WithDescription("Receive Microsoft Graph change notifications for new inbound emails (HMAC-signed)")
            .Produces<IncomingWebhookResponse>(StatusCodes.Status202Accepted)
            .Produces(StatusCodes.Status200OK)
            .Produces<ProblemDetails>(StatusCodes.Status400BadRequest)
            .Produces<ProblemDetails>(StatusCodes.Status401Unauthorized)
            .Produces<ProblemDetails>(StatusCodes.Status429TooManyRequests)
            .Produces<ProblemDetails>(StatusCodes.Status500InternalServerError);

        return app;
    }

    private static async Task<IResult> SendCommunicationAsync(
        SendCommunicationRequest request,
        CommunicationService communicationService,
        ILogger<CommunicationService> logger,
        HttpContext context,
        CancellationToken ct)
    {
        var response = await communicationService.SendAsync(request, context, ct);
        return TypedResults.Ok(response);
    }

    /// <summary>
    /// Starts (or reuses) a 1:1 direct thread with another Spaarke user (task 043 / FR-09). Resolves the
    /// caller server-side (never client-supplied — a caller cannot start a thread "as" someone else) and
    /// delegates find-or-create to <see cref="IDirectThreadAccessService"/>. Exactly-two-participant only —
    /// N-party group threads are deferred (root project scope; NOT built here).
    /// </summary>
    private static async Task<IResult> StartDirectThreadAsync(
        StartDirectThreadRequest request,
        IDirectThreadAccessService directThreadAccess,
        ICallerSystemUserResolver callerResolver,
        HttpContext context,
        CancellationToken ct)
    {
        var resolution = await callerResolver.ResolveAsync(context.User, ct);
        if (!resolution.IsResolved || !Guid.TryParse(resolution.SystemUserId, out var callerId) || callerId == Guid.Empty)
        {
            throw new SdapProblemException(
                code: "SENDER_NOT_RESOLVED",
                title: "Sender Not Resolved",
                detail: "The caller could not be resolved to a Dataverse systemuser; cannot start a direct thread.",
                statusCode: 403);
        }

        if (request.OtherParticipantSystemUserId == Guid.Empty)
        {
            throw new SdapProblemException(
                code: "VALIDATION_ERROR",
                title: "Validation Error",
                detail: "otherParticipantSystemUserId is required.",
                statusCode: 400);
        }

        if (request.OtherParticipantSystemUserId == callerId)
        {
            throw new SdapProblemException(
                code: "VALIDATION_ERROR",
                title: "Validation Error",
                detail: "Cannot start a direct thread with yourself.",
                statusCode: 400);
        }

        var threadId = await directThreadAccess.FindOrCreateDirectThreadAsync(callerId, request.OtherParticipantSystemUserId, ct);

        return TypedResults.Ok(new StartDirectThreadResponse
        {
            ThreadId = threadId,
            CallerSystemUserId = callerId,
            OtherParticipantSystemUserId = request.OtherParticipantSystemUserId,
        });
    }

    /// <summary>
    /// Maximum number of recipients allowed in a single bulk send request.
    /// </summary>
    private const int MaxBulkRecipients = 50;

    /// <summary>
    /// Delay in milliseconds between sequential sends for Graph API rate awareness.
    /// </summary>
    private const int InterSendDelayMs = 100;

    private static async Task<IResult> SendBulkCommunicationAsync(
        BulkSendRequest request,
        CommunicationService communicationService,
        ILogger<CommunicationService> logger,
        HttpContext context,
        CancellationToken ct)
    {
        // Validate request
        if (request.Recipients is not { Length: > 0 })
        {
            throw new SdapProblemException(
                code: "VALIDATION_ERROR",
                title: "Validation Error",
                detail: "At least one recipient is required.",
                statusCode: 400);
        }

        if (request.Recipients.Length > MaxBulkRecipients)
        {
            throw new SdapProblemException(
                code: "VALIDATION_ERROR",
                title: "Validation Error",
                detail: $"Maximum {MaxBulkRecipients} recipients allowed per bulk request. Received {request.Recipients.Length}.",
                statusCode: 400);
        }

        if (string.IsNullOrWhiteSpace(request.Subject))
        {
            throw new SdapProblemException(
                code: "VALIDATION_ERROR",
                title: "Validation Error",
                detail: "Subject is required.",
                statusCode: 400);
        }

        if (string.IsNullOrWhiteSpace(request.Body))
        {
            throw new SdapProblemException(
                code: "VALIDATION_ERROR",
                title: "Validation Error",
                detail: "Body is required.",
                statusCode: 400);
        }

        logger.LogInformation(
            "Starting bulk send | RecipientCount: {RecipientCount}, Subject: {Subject}",
            request.Recipients.Length,
            request.Subject);

        var results = new List<BulkSendResult>(request.Recipients.Length);

        for (var i = 0; i < request.Recipients.Length; i++)
        {
            var recipient = request.Recipients[i];

            // Build a SendCommunicationRequest for this individual recipient
            var individualRequest = new SendCommunicationRequest
            {
                To = new[] { recipient.To },
                Cc = recipient.Cc,
                Subject = request.Subject,
                Body = request.Body,
                BodyFormat = request.BodyFormat,
                FromMailbox = request.FromMailbox,
                CommunicationType = request.CommunicationType,
                AttachmentDocumentIds = request.AttachmentDocumentIds,
                ArchiveToSpe = request.ArchiveToSpe,
                Associations = request.Associations,
                SendMode = request.SendMode
            };

            try
            {
                var sendResponse = await communicationService.SendAsync(individualRequest, httpContext: context, ct);

                results.Add(new BulkSendResult
                {
                    RecipientEmail = recipient.To,
                    Status = "sent",
                    CommunicationId = sendResponse.CommunicationId
                });

                logger.LogDebug(
                    "Bulk send {Index}/{Total} succeeded | Recipient: {Recipient}",
                    i + 1, request.Recipients.Length, recipient.To);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                results.Add(new BulkSendResult
                {
                    RecipientEmail = recipient.To,
                    Status = "failed",
                    Error = ex.Message
                });

                logger.LogWarning(
                    ex,
                    "Bulk send {Index}/{Total} failed | Recipient: {Recipient}",
                    i + 1, request.Recipients.Length, recipient.To);
            }

            // Graph API rate awareness: delay between sends (skip after last)
            if (i < request.Recipients.Length - 1)
            {
                await Task.Delay(InterSendDelayMs, ct);
            }
        }

        var succeeded = results.Count(r => r.Status == "sent");
        var failed = results.Count(r => r.Status == "failed");

        var bulkResponse = new BulkSendResponse
        {
            TotalRecipients = request.Recipients.Length,
            Succeeded = succeeded,
            Failed = failed,
            Results = results.ToArray()
        };

        logger.LogInformation(
            "Bulk send completed | Total: {Total}, Succeeded: {Succeeded}, Failed: {Failed}",
            bulkResponse.TotalRecipients, succeeded, failed);

        // 200 if all succeeded, 207 Multi-Status if partial success/failure
        if (failed == 0)
        {
            return TypedResults.Ok(bulkResponse);
        }

        return Results.Json(bulkResponse, statusCode: 207);
    }

    private static async Task<IResult> GetCommunicationStatusAsync(
        Guid id,
        IGenericEntityService dataverseService,
        ILogger<CommunicationService> logger,
        CancellationToken ct)
    {
        Entity entity;
        try
        {
            entity = await dataverseService.RetrieveAsync(
                "sprk_communication",
                id,
                new[] { "statuscode", "sprk_graphmessageid", "sprk_sentat", "sprk_from" },
                ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Communication {CommunicationId} not found in Dataverse", id);
            throw new SdapProblemException(
                code: "COMMUNICATION_NOT_FOUND",
                title: "Communication not found",
                detail: $"Communication with ID '{id}' does not exist.",
                statusCode: 404);
        }

        var statusCodeValue = entity.GetAttributeValue<OptionSetValue>("statuscode")?.Value ?? 1;
        var status = (CommunicationStatus)statusCodeValue;

        var sentAtDateTime = entity.GetAttributeValue<DateTime?>("sprk_sentat");
        DateTimeOffset? sentAt = sentAtDateTime.HasValue
            ? new DateTimeOffset(sentAtDateTime.Value, TimeSpan.Zero)
            : null;

        var response = new CommunicationStatusResponse
        {
            CommunicationId = id,
            Status = status,
            GraphMessageId = entity.GetAttributeValue<string>("sprk_graphmessageid"),
            SentAt = sentAt,
            From = entity.GetAttributeValue<string>("sprk_from")
        };

        return TypedResults.Ok(response);
    }

    private static async Task<IResult> ArchiveCommunicationAsync(
        Guid id,
        CommunicationService communicationService,
        CancellationToken ct)
    {
        var result = await communicationService.ArchiveExistingAsync(id, ct);
        return TypedResults.Ok(result);
    }

    /// <summary>
    /// Thread-read for the polling timeline (task 050 / FR-11). Parses the optional <c>?since</c> (ISO-8601) +
    /// <c>?top</c>, resolves the caller server-side (never client-supplied), and delegates to the impersonated,
    /// access-filtered read. A malformed <c>since</c> is a 400 ProblemDetails (ADR-019).
    /// </summary>
    private static async Task<IResult> GetThreadMessagesAsync(
        Guid threadId,
        CommunicationThreadReadService readService,
        HttpContext context,
        [FromQuery] string? since,
        [FromQuery] int? top,
        CancellationToken ct)
    {
        var sinceValue = ParseSince(since);
        var result = await readService.ReadThreadAsync(threadId, context.User, sinceValue, top, ct);
        return TypedResults.Ok(result);
    }

    /// <summary>
    /// Unread-count for the polling indicator (task 050 / FR-11). Parses the optional <c>?since</c> (the caller's
    /// last-seen marker), resolves the caller server-side, and delegates to the impersonated, access-filtered count.
    /// </summary>
    private static async Task<IResult> GetThreadUnreadCountAsync(
        Guid threadId,
        CommunicationThreadReadService readService,
        HttpContext context,
        [FromQuery] string? since,
        CancellationToken ct)
    {
        var sinceValue = ParseSince(since);
        var result = await readService.GetUnreadCountAsync(threadId, context.User, sinceValue, ct);
        return TypedResults.Ok(result);
    }

    /// <summary>
    /// Parses an optional ISO-8601 <c>since</c> query value. Null/blank → null (no lower bound); a non-parseable
    /// value → 400 ProblemDetails (ADR-019). Round-trip kind so an offset (or trailing Z) is honored.
    /// </summary>
    private static DateTimeOffset? ParseSince(string? since)
    {
        if (string.IsNullOrWhiteSpace(since))
            return null;

        if (!DateTimeOffset.TryParse(since, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed))
        {
            throw new SdapProblemException(
                code: "VALIDATION_ERROR",
                title: "Validation Error",
                detail: "'since' must be an ISO-8601 timestamp (e.g. 2026-07-16T10:00:00Z).",
                statusCode: 400);
        }

        return parsed;
    }

    /// <summary>
    /// By-regarding read for the regarding-mode Timeline (R2 task 010 / FR-01). Resolves the caller server-side
    /// (never client-supplied) and delegates to the impersonated, access-filtered by-regarding read. An unsupported
    /// <paramref name="entityType"/> is a 400 ProblemDetails (ADR-019).
    /// </summary>
    private static async Task<IResult> GetCommunicationsByRegardingAsync(
        string entityType,
        Guid id,
        CommunicationThreadReadService readService,
        HttpContext context,
        CancellationToken ct)
    {
        var result = await readService.ReadByRegardingAsync(entityType, id, context.User, ct);
        return TypedResults.Ok(result);
    }

    /// <summary>
    /// Filtered cross-record communication query (R2 task 011 / FR-02; `participant` wired in task 051). Resolves
    /// the caller server-side and delegates to the impersonated, access-filtered query. thread/regarding/channel/
    /// date/participant facets are all composed onto the shared read path; malformed/empty filters return a 400
    /// ProblemDetails (ADR-019 graceful degradation).
    /// </summary>
    private static async Task<IResult> QueryCommunicationsAsync(
        CommunicationThreadReadService readService,
        HttpContext context,
        [FromQuery] string? thread,
        [FromQuery] string? regarding,
        [FromQuery] string? channel,
        [FromQuery] string? from,
        [FromQuery] string? to,
        [FromQuery] string? participant,
        CancellationToken ct)
    {
        var result = await readService.QueryCommunicationsAsync(
            thread, regarding, channel, from, to, participant, context.User, ct);
        return TypedResults.Ok(result);
    }

    /// <summary>
    /// On-demand Association Engine suggestion preview (task 074, Path C). Loads the stored
    /// <c>sprk_communication</c> (404 if missing), reconstructs the normalized envelope + context, runs the
    /// engine's evaluate-only path, and projects the decision into <see cref="SuggestAssociationsResponse"/>.
    /// READ-ONLY: it never writes the record (that is <see cref="CommunicationService.ArchiveExistingAsync"/> /
    /// the inbound <c>ResolveAsync</c> path) — the point is a preview of what the engine would suggest.
    /// </summary>
    private static async Task<IResult> SuggestAssociationsAsync(
        Guid id,
        CommunicationService communicationService,
        IncomingAssociationResolver associationResolver,
        CancellationToken ct)
    {
        var (message, context) = await communicationService.ReconstructEnvelopeAsync(id, ct);
        var decision = await associationResolver.EvaluateAsync(message, context, ct);
        return TypedResults.Ok(SuggestAssociationsResponse.FromDecision(id, decision));
    }

    private static async Task<IResult> VerifyCommunicationAccountAsync(
        Guid id,
        MailboxVerificationService verificationService,
        CancellationToken ct)
    {
        var result = await verificationService.VerifyAsync(id, ct);

        if (result is null)
        {
            throw new SdapProblemException(
                code: "ACCOUNT_NOT_FOUND",
                title: "Communication account not found",
                detail: $"Communication account with ID '{id}' does not exist.",
                statusCode: 404);
        }

        return TypedResults.Ok(result);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Incoming Webhook Handler (Graph Change Notifications)
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Handle Microsoft Graph change notification webhook.
    /// Two request types:
    ///   1. Subscription validation: Graph sends validationToken query parameter during subscription creation.
    ///      Must return 200 OK with the token as text/plain.
    ///   2. Change notification: Graph sends a JSON body with notification array.
    ///      Must validate clientState, enqueue jobs, and return 202 Accepted quickly.
    /// </summary>
    private static async Task<IResult> HandleIncomingWebhookAsync(
        HttpRequest request,
        JobSubmissionService jobSubmissionService,
        Services.Communication.GraphSubscriptionManager subscriptionManager,
        IOptions<CommunicationOptions> communicationOptions,
        ILogger<CommunicationService> logger,
        CancellationToken ct)
    {
        var traceId = request.HttpContext.TraceIdentifier;
        var correlationId = Guid.NewGuid().ToString();

        try
        {
            // ─── Step 1: Handle Graph subscription validation ───
            // When creating a subscription, Graph POSTs with ?validationToken=<token>
            // and expects 200 OK with the token echoed back as text/plain.
            if (request.Query.TryGetValue("validationToken", out var validationToken)
                && !string.IsNullOrEmpty(validationToken))
            {
                logger.LogInformation(
                    "Received Graph subscription validation request, returning validationToken, " +
                    "TraceId={TraceId}",
                    traceId);

                return Results.Text(validationToken!, "text/plain", statusCode: 200);
            }

            // ─── Step 2: Read notification body ───
            request.EnableBuffering();
            using var reader = new StreamReader(request.Body, Encoding.UTF8, leaveOpen: true);
            var requestBody = await reader.ReadToEndAsync(ct);

            if (string.IsNullOrWhiteSpace(requestBody))
            {
                logger.LogWarning("Empty webhook payload received, TraceId={TraceId}", traceId);
                return Results.Problem(
                    title: "Invalid Payload",
                    detail: "Webhook payload is empty",
                    statusCode: StatusCodes.Status400BadRequest);
            }

            // ─── Step 3: Parse notifications ───
            GraphChangeNotificationCollection? notifications;
            try
            {
                notifications = JsonSerializer.Deserialize<GraphChangeNotificationCollection>(requestBody);
            }
            catch (JsonException ex)
            {
                logger.LogError(ex,
                    "Failed to parse Graph notification payload, TraceId={TraceId}", traceId);
                return Results.Problem(
                    title: "Invalid Payload",
                    detail: $"Failed to parse notification payload: {ex.Message}",
                    statusCode: StatusCodes.Status400BadRequest);
            }

            if (notifications?.Value is not { Length: > 0 })
            {
                logger.LogWarning(
                    "Webhook payload contains no notifications, TraceId={TraceId}", traceId);
                return Results.Problem(
                    title: "Invalid Payload",
                    detail: "Notification payload contains no notifications",
                    statusCode: StatusCodes.Status400BadRequest);
            }

            // ─── Step 4: Validate clientState on each notification (constant-time) ───
            // Fail-closed: clientState is required in every environment. The HMAC
            // signature check ran in the endpoint filter; the body-level clientState
            // is the second layer of defense (task 044). DEVELOPMENT_MODE bypass removed.
            var expectedClientState = communicationOptions.Value.WebhookClientState;
            if (string.IsNullOrEmpty(expectedClientState))
            {
                logger.LogError(
                    "Communication:WebhookClientState not configured — rejecting webhook batch. TraceId={TraceId}",
                    traceId);
                return Results.Problem(
                    title: "Server Misconfigured",
                    detail: "Webhook clientState validation is not configured on this server.",
                    statusCode: StatusCodes.Status500InternalServerError);
            }

            var expectedClientStateBytes = Encoding.UTF8.GetBytes(expectedClientState);
            var enqueued = 0;
            var lifecycleHandled = 0;

            foreach (var notification in notifications.Value)
            {
                // Constant-time clientState comparison (prevents timing side channels).
                // Reject the entire batch if any notification has a mismatched clientState
                // (per Graph webhook spec).
                var providedClientStateBytes = Encoding.UTF8.GetBytes(notification.ClientState ?? string.Empty);
                if (providedClientStateBytes.Length != expectedClientStateBytes.Length
                    || !CryptographicOperations.FixedTimeEquals(providedClientStateBytes, expectedClientStateBytes))
                {
                    logger.LogWarning(
                        "Invalid clientState on notification for subscription {SubscriptionId}, " +
                        "rejecting, TraceId={TraceId}",
                        notification.SubscriptionId, traceId);

                    return Results.Problem(
                        title: "Unauthorized",
                        detail: "Invalid clientState in notification",
                        statusCode: StatusCodes.Status401Unauthorized);
                }

                // ─── Step 4.5: Lifecycle notifications (FR-24) ───
                // Lifecycle notifications carry a `lifecycleEvent` (reauthorizationRequired /
                // subscriptionRemoved / missed) instead of a changed message. Route them to the
                // subscription manager, which renews/recreates the subscription or triggers delta
                // reconciliation. Handling is non-fatal and must not block the fast 202 response.
                if (!string.IsNullOrEmpty(notification.LifecycleEvent))
                {
                    logger.LogInformation(
                        "Received Graph lifecycle notification | LifecycleEvent={Event}, " +
                        "SubscriptionId={SubscriptionId}, CorrelationId={CorrelationId}",
                        notification.LifecycleEvent, notification.SubscriptionId, correlationId);

                    try
                    {
                        await subscriptionManager.HandleLifecycleNotificationAsync(
                            notification.LifecycleEvent, notification.SubscriptionId, notification.Resource, ct);
                        lifecycleHandled++;
                    }
                    catch (Exception ex)
                    {
                        // Non-fatal: log and acknowledge; the periodic management cycle is the backstop.
                        logger.LogWarning(ex,
                            "Lifecycle notification handling failed (non-fatal) | LifecycleEvent={Event}, " +
                            "SubscriptionId={SubscriptionId}",
                            notification.LifecycleEvent, notification.SubscriptionId);
                    }

                    continue;
                }

                // ─── Step 5: Deduplication ───
                // Build a dedup key from the message ID to catch both retries AND duplicate
                // notifications from multiple subscriptions monitoring the same mailbox.
                // ResourceData.Id or the last segment of Resource is the Graph message ID.
                var notificationMessageId = notification.ResourceData?.Id ?? ExtractLastSegment(notification.Resource ?? "");
                var dedupKey = $"msg:{notificationMessageId}:{notification.ChangeType}";

                // Prune expired entries periodically (every time we process a batch)
                PruneExpiredNotifications();

                if (!_recentNotifications.TryAdd(dedupKey, DateTimeOffset.UtcNow))
                {
                    logger.LogDebug(
                        "Duplicate notification skipped | SubscriptionId={SubscriptionId}, " +
                        "Resource={Resource}, DedupKey={DedupKey}",
                        notification.SubscriptionId, notification.Resource, dedupKey);
                    continue;
                }

                // ─── Step 6: Extract mailbox and messageId from resource path ───
                // Resource format: "users/{mailbox}/mailFolders/{folder}/messages/{messageId}"
                //               or "users/{mailbox}/messages/{messageId}"
                var resource = notification.Resource ?? string.Empty;
                var messageId = notification.ResourceData?.Id ?? ExtractLastSegment(resource);

                logger.LogInformation(
                    "Processing Graph notification | SubscriptionId={SubscriptionId}, " +
                    "ChangeType={ChangeType}, Resource={Resource}, MessageId={MessageId}, " +
                    "CorrelationId={CorrelationId}",
                    notification.SubscriptionId, notification.ChangeType,
                    resource, messageId, correlationId);

                // ─── Step 7: Enqueue IncomingCommunicationJob ───
                var jobPayload = JsonDocument.Parse(JsonSerializer.Serialize(new
                {
                    SubscriptionId = notification.SubscriptionId,
                    Resource = resource,
                    MessageId = messageId,
                    ChangeType = notification.ChangeType,
                    TenantId = notification.TenantId,
                    TriggerSource = "GraphWebhook"
                }));

                var job = new JobContract
                {
                    JobType = JobTypeIncomingCommunication,
                    SubjectId = messageId ?? notification.SubscriptionId ?? "unknown",
                    CorrelationId = correlationId,
                    IdempotencyKey = $"Communication:{messageId}:Process",
                    Payload = jobPayload,
                    MaxAttempts = 3
                };

                await jobSubmissionService.SubmitCommunicationJobAsync(job, ct);
                enqueued++;

                logger.LogInformation(
                    "Enqueued IncomingCommunicationJob {JobId} to communication queue | SubscriptionId={SubscriptionId}, " +
                    "MessageId={MessageId}, IdempotencyKey={IdempotencyKey}",
                    job.JobId, notification.SubscriptionId, messageId, job.IdempotencyKey);
            }

            // ─── Step 8: Return 202 Accepted quickly (Graph requires fast response) ───
            logger.LogInformation(
                "Webhook processed: {Total} notifications received, {Enqueued} enqueued, " +
                "{Lifecycle} lifecycle events handled, CorrelationId={CorrelationId}",
                notifications.Value.Length, enqueued, lifecycleHandled, correlationId);

            return Results.Accepted(
                value: new IncomingWebhookResponse
                {
                    Accepted = true,
                    NotificationsReceived = notifications.Value.Length,
                    NotificationsEnqueued = enqueued,
                    CorrelationId = correlationId
                });
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Error processing incoming webhook, TraceId={TraceId}", traceId);
            return Results.Problem(
                title: "Internal Server Error",
                detail: "An unexpected error occurred processing the webhook",
                statusCode: StatusCodes.Status500InternalServerError,
                extensions: new Dictionary<string, object?> { ["traceId"] = traceId });
        }
    }

    /// <summary>
    /// Extracts the last path segment from a Graph resource path.
    /// E.g., "users/user@domain.com/mailFolders/Inbox/messages/AAMkAGI2" -> "AAMkAGI2"
    /// </summary>
    private static string? ExtractLastSegment(string resourcePath)
    {
        if (string.IsNullOrEmpty(resourcePath))
            return null;

        var lastSlash = resourcePath.LastIndexOf('/');
        return lastSlash >= 0 && lastSlash < resourcePath.Length - 1
            ? resourcePath[(lastSlash + 1)..]
            : null;
    }

    /// <summary>
    /// Removes expired entries from the notification deduplication cache.
    /// Called during webhook processing to prevent unbounded memory growth.
    /// </summary>
    private static void PruneExpiredNotifications()
    {
        var cutoff = DateTimeOffset.UtcNow.Subtract(DeduplicationWindow);

        foreach (var kvp in _recentNotifications)
        {
            if (kvp.Value < cutoff)
            {
                _recentNotifications.TryRemove(kvp.Key, out _);
            }
        }
    }
}
