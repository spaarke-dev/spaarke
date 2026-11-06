// spaarke-SPA-external-access-platform-r3 task 033 (2026-10-10) — FR-04 external-plane message read.
//
// GET /api/v1/external/{root}/{id}/messages returns the communication thread filed regarding one record the caller may
// read. Mounted UNDER the shared /api/v1/external group (ExternalAccessEndpoints), so it inherits the ExternalCollaboration
// dual-scheme policy AND the group-level CallerPrincipalAuthorizationFilter; the handler receives a plane-agnostic
// CallerPrincipal and never branches on plane, scheme, iss or tid (NFR-01).
//
// Record gate (fail-closed, before any message read). The module registry is the authority, so this route answers
// exactly what the module read seam answers for the same record:
//   - projects / matters / workassignments — the record must be in the caller's Read-bearing accessible set for that
//     root (module.IsRecordAccessible; the sets are composed by the one evaluator on both planes);
//   - invoices — a child: the invoice's own parent lookups (sprk_matter / sprk_project) are read app-only and the row
//     must survive the invoices module's ScopeRows (rolls up to an accessible root);
//   - servicerequests — requester scope: the request's sprk_requestedby is read app-only and must survive the
//     service-requests module's ScopeRows (the caller's own contact; CIAM only when the partner switch is on — task 011).
// Outside the set, a record that does not exist, and a caller with no accessible record of that kind all get the SAME
// 403 (no existence oracle). An unsupported root is a 404 — including documents, because sprk_communication has no
// regarding lookup to a document (RegardingFieldMap; SecureChildLineage's sprk_communication filing lookups), so no
// message is ever "regarding" a document.
//
// Message read. App-only (no OBO, no impersonation: a contact has no systemuser), keyed on the gated record through the
// typed regarding lookup, via CommunicationThreadReadService.ReadRegardingAppOnlyAsync — the existing read model's
// parsing and the SAME shared internal-only filter, not a fork. Internal-only messages reach only an internal systemuser
// principal; a CIAM contact and a workforce contact never receive them. Every returned row is re-checked against the
// gated record there. The response is an explicit allow-list DTO (ExternalMessageDto), newest last.
//
// Task 034 (FR-11) — POST /api/v1/external/{root}/{id}/messages posts a message to the same record's thread as the caller.
// Same group, same roots, same record gate (so the 403 is the same, with no existence oracle), PLUS Create on the record:
// projects / matters / workassignments → the caller's rights on that root; invoices → Create on a parent root the invoice
// is filed under; servicerequests → the requester (the gate's requester scope) may answer on their own request. A
// ViewOnly caller can read the thread but gets 403 insufficient_rights on a post. The body is the text only (≤ 10,000
// chars, stored as PLAIN TEXT so markup is never rendered), attachments are 400, the sender is the resolved principal
// (an unattributable one is 403 before any read), and the write is CommunicationService.PostToRecordThreadAsync: one
// app-only sprk_communication through the shared filing step — no email, no Graph, no OBO, never internal-only.

using Sprk.Bff.Api.Api.ExternalAccess.Dtos;
using Sprk.Bff.Api.Infrastructure.Errors;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Services.Communication;
using Sprk.Bff.Api.Services.Dataverse;

namespace Sprk.Bff.Api.Api.ExternalAccess;

/// <summary>
/// The external-plane message read (FR-04): <c>GET /api/v1/external/{root}/{id}/messages</c>. See the file header for the
/// record gate and the read.
/// </summary>
public static class ExternalMessageEndpoints
{
    /// <summary>Deny code for a record outside the caller's accessible set, a missing record, and an empty set alike.</summary>
    public const string DenyRecordNotAccessible = "sdap.external.messages.record_not_accessible";

    /// <summary>Error code for a root segment that has no message thread on this surface.</summary>
    public const string ErrorRootNotSupported = "EXT_MESSAGES_ROOT_NOT_SUPPORTED";

    /// <summary>Deny code for a post by a caller who can read the record but holds no Create on it (e.g. ViewOnly).</summary>
    public const string DenyInsufficientRights = "sdap.access.deny.insufficient_rights";

    /// <summary>Deny code for a post by a principal that is neither a systemuser nor a contact (no sender to stamp).</summary>
    public const string DenySenderUnattributable = "sdap.external.messages.sender_unattributable";

    /// <summary>Error code for a post body that is missing, blank or too long.</summary>
    public const string ErrorInvalidBody = "EXT_MESSAGES_INVALID_BODY";

    /// <summary>Error code for a post that carries attachments (FR-11: thread posts have none).</summary>
    public const string ErrorAttachmentsNotSupported = "EXT_MESSAGES_ATTACHMENTS_NOT_SUPPORTED";

    /// <summary>One supported root segment: the Dataverse entity it names (also the regarding family member).</summary>
    internal sealed record MessageRoot(string Segment, string EntityLogicalName);

    /// <summary>
    /// The supported root segments (case-insensitive). Each entity is an ADR-024 regarding family member with a typed
    /// <c>sprk_regarding*</c> lookup on <c>sprk_communication</c> AND a registered external module whose Tier-2 predicate
    /// gates it. Anything else — documents included (no regarding lookup) — is a 404.
    /// </summary>
    internal static readonly IReadOnlyDictionary<string, MessageRoot> Roots =
        new[]
        {
            new MessageRoot("projects", "sprk_project"),
            new MessageRoot("matters", "sprk_matter"),
            new MessageRoot("workassignments", "sprk_workassignment"),
            new MessageRoot("invoices", "sprk_invoice"),
            new MessageRoot("servicerequests", "sprk_servicerequest"),
        }.ToDictionary(r => r.Segment, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The workforce ceiling for a message route (unified-access-control-r2 task 186): the root comes from the route's
    /// <c>{root}</c> value. An unsupported root resolves to nothing, and the handler answers its 404 before any read.
    /// </summary>
    internal static Func<EndpointFilterInvocationContext, IReadOnlyList<WorkforceCeilingRequirement>?> MessageCeiling(bool post) =>
        context => context.HttpContext.Request.RouteValues.TryGetValue("root", out var value)
                   && value is string segment
                   && Roots.TryGetValue(segment, out var root)
            ? post
                ? WorkforceRoleCeilingGate.PostMessageOn(root.EntityLogicalName)
                : WorkforceRoleCeilingGate.ReadMessagesOn(root.EntityLogicalName)
            : null;

    /// <summary>
    /// Registers the message read on the shared external collaboration group. The group's policy and
    /// CallerPrincipalAuthorizationFilter are inherited; this adds the route only.
    /// </summary>
    public static RouteGroupBuilder MapExternalMessageEndpoints(this RouteGroupBuilder externalGroup)
    {
        // GET /api/v1/external/{root}/{id}/messages — the record's thread, newest last.
        externalGroup.MapGet("/{root}/{id:guid}/messages", GetMessagesAsync)
            .WithName("GetExternalRecordMessages")
            .RequireWorkforceCeiling(MessageCeiling(post: false))
            .WithSummary("Read the message thread regarding a record the caller can access")
            .WithDescription(
                "Returns the messages filed regarding a project, matter, work assignment, invoice or (own) service " +
                "request, oldest first. 403 unless the record is in the caller's accessible set (service requests: the " +
                "caller's own); 404 for any other root. Internal-only messages are returned only to internal systemusers.")
            .Produces<ExternalCollectionResponse<ExternalMessageDto>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        // POST /api/v1/external/{root}/{id}/messages — post to the record's thread as the caller (task 034, FR-11).
        externalGroup.MapPost("/{root}/{id:guid}/messages", PostMessageAsync)
            .WithName("PostExternalRecordMessage")
            .RequireWorkforceCeiling(MessageCeiling(post: true))
            .WithSummary("Post a message to the thread of a record the caller can access")
            .WithDescription(
                "Posts a plain-text message (sprk_body, at most 10,000 characters, no attachments) to the thread of a " +
                "project, matter, work assignment, invoice or (own) service request, as the caller. No email is sent. 403 " +
                "unless the record is in the caller's accessible set with Create (service requests: the caller's own); 404 " +
                "for any other root.")
            // A write path: per-user throttle on the caller key (oid/sub, present on a CIAM token as on a workforce one).
            .RequireRateLimiting("dataverse-query")
            .Accepts<PostExternalMessageRequest>("application/json")
            .Produces<ExternalMessageDto>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status415UnsupportedMediaType)
            .ProducesProblem(StatusCodes.Status429TooManyRequests)
            .ProducesProblem(StatusCodes.Status500InternalServerError)
            .ProducesProblem(StatusCodes.Status502BadGateway)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        return externalGroup;
    }

    private static Task<IResult> PostMessageAsync(
        string root,
        Guid id,
        PostExternalMessageRequest? request,
        HttpContext httpContext,
        ExternalModuleRegistry registry,
        RecordService recordService,
        CommunicationService communicationService,
        ExternalDataService dataService,
        CallerRecordAccessProbe probe,
        [Microsoft.AspNetCore.Mvc.FromServices] ExternalInvoiceHiding? hideInvoices,
        ILogger<Program> logger,
        CancellationToken ct)
    {
        if (httpContext.Items[CallerPrincipal.HttpContextItemsKey] is not CallerPrincipal principal)
        {
            return Task.FromResult(Results.Problem(
                statusCode: StatusCodes.Status500InternalServerError,
                title: "Internal Server Error",
                detail: "Authentication context not available — ensure AddCallerPrincipalAuthorizationFilter is applied"));
        }

        // Task 187 D4: the post gate is the read gate, so it takes the same secure-flag and live-parent readers.
        // Task 036 (FR-20): and the Dataverse rights reader, for a caller whose access Dataverse decided.
        return PostMessageCoreAsync(
            root, id, request, principal, registry, recordService.GetRecordAsync, communicationService, logger, ct,
            dataService.GetNonSecureRootsAsync, dataService.GetLiveParentRootsAsync,
            ExternalProjectDataEndpoints.CallerRightsReader(probe, httpContext),
            ExternalProjectDataEndpoints.CallerPrivilegeReader(probe, httpContext), hideInvoices);
    }

    /// <summary>
    /// The Dataverse Create privilege on <c>sprk_communication</c>, the table a post writes (app-only), by its live name
    /// as the SpaarkeMaster role definitions carry it (<c>Spaarke Basic User.xml</c>, <c>Spaarke Core User.xml</c>). Task 036
    /// fix round 1 (F1): a Dataverse-decided caller's post needs it besides AppendTo on the record. Pinned by a test.
    /// </summary>
    internal const string CreateMessagePrivilege = "prvCreatesprk_Communication";

    /// <summary>
    /// The entity set a post on <paramref name="root"/> is confirmed against Dataverse on (task 036, FR-20): the record
    /// itself for a project, matter, work assignment or invoice. <c>null</c> for a service request: its rights are the
    /// requester scope (the caller's own request), not an access grant and not the membership constant, so it is not
    /// confirmed (an open question for the owner, recorded in the task 036 notes).
    /// </summary>
    private static string? DataverseConfirmedEntitySet(MessageRoot root) => root.EntityLogicalName switch
    {
        "sprk_project" => ExternalDataService.GetDocumentParentBinding(ExternalDataService.DocumentParentKind.Project).EntitySet,
        "sprk_matter" => ExternalDataService.GetDocumentParentBinding(ExternalDataService.DocumentParentKind.Matter).EntitySet,
        "sprk_workassignment" =>
            ExternalDataService.GetDocumentParentBinding(ExternalDataService.DocumentParentKind.WorkAssignment).EntitySet,
        "sprk_invoice" => ExternalDataService.GetDocumentParentBinding(ExternalDataService.DocumentParentKind.Invoice).EntitySet,
        _ => null,
    };

    /// <summary>
    /// The message post pipeline (task 034, FR-11): root lookup → request validation → attributable sender → record gate
    /// with Create (<see cref="GetRecordRightsAsync"/>) → <see cref="CommunicationService.PostToRecordThreadAsync"/>.
    /// Nothing is read before the request and the sender are known to be valid, and nothing is written unless the gate
    /// granted Create. Separated from the route handler, as the read is, so it can be driven with a test double for
    /// <paramref name="readRecord"/>.
    /// </summary>
    internal static async Task<IResult> PostMessageCoreAsync(
        string root,
        Guid id,
        PostExternalMessageRequest? request,
        CallerPrincipal principal,
        ExternalModuleRegistry registry,
        Func<string, Guid, string[]?, CancellationToken, Task<IReadOnlyDictionary<string, object?>>> readRecord,
        CommunicationService communicationService,
        ILogger logger,
        CancellationToken ct,
        NonSecureRootReader? readNonSecureRoots = null,
        LiveParentRootReader? readLiveParents = null,
        Func<string, Guid, CancellationToken, Task<Spaarke.Dataverse.AccessRights?>>? readCallerRights = null,
        Func<string, CancellationToken, Task<bool?>>? readCallerPrivilege = null,
        ExternalInvoiceHiding? hideInvoices = null)
    {
        if (string.IsNullOrWhiteSpace(root) || !Roots.TryGetValue(root, out var messageRoot))
        {
            return Results.Problem(
                statusCode: StatusCodes.Status404NotFound, title: "Not Found",
                detail: "Messages are not available for this kind of record.",
                extensions: new Dictionary<string, object?> { ["errorCode"] = ErrorRootNotSupported });
        }

        // FR-11: no attachments. A property that names one is refused, not dropped, so the caller is not told it was sent.
        if (request?.Other?.Keys.FirstOrDefault(NamesAttachment) is { } attachmentKey)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status400BadRequest, title: "Bad Request",
                detail: $"Messages cannot carry attachments ('{attachmentKey}').",
                extensions: new Dictionary<string, object?> { ["errorCode"] = ErrorAttachmentsNotSupported });
        }

        var body = request?.SprkBody;
        if (string.IsNullOrWhiteSpace(body) || body.Length > CommunicationService.MaxThreadPostBodyLength)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status400BadRequest, title: "Bad Request",
                detail: $"sprk_body is required and may be at most {CommunicationService.MaxThreadPostBodyLength} characters.",
                extensions: new Dictionary<string, object?> { ["errorCode"] = ErrorInvalidBody });
        }

        // FR-11/FR-25: the sender is the resolved principal. One that cannot be stamped is refused before any read.
        if (!ExternalDataService.CanStampCreator(principal))
        {
            logger.LogWarning(
                "[EXT-MESSAGES] Refused post to {Entity} {RecordId}: the caller resolves to neither a systemuser nor a contact.",
                messageRoot.EntityLogicalName, id);
            return ProblemDetailsHelper.Forbidden(
                DenySenderUnattributable, "Your identity could not be attributed, so the message cannot be posted.");
        }

        try
        {
            var rights = await GetRecordRightsAsync(
                    messageRoot, id, principal, registry, readRecord, logger, ct, readNonSecureRoots, readLiveParents,
                    hideInvoices)
                .ConfigureAwait(false);

            if (!rights.HasFlag(Spaarke.Dataverse.AccessRights.Read))
            {
                logger.LogWarning(
                    "[EXT-MESSAGES] DENY post on {Entity} {RecordId} for contact {ContactId} / systemuser {SystemUserId}: not in " +
                    "the caller's accessible set (or absent).",
                    messageRoot.EntityLogicalName, id, principal.ContactId, principal.SystemUserId);
                return ProblemDetailsHelper.Forbidden(DenyRecordNotAccessible);
            }

            // Task 036 (FR-20, owner round 59 item 2): a caller whose access Dataverse decided is confirmed against Dataverse
            // on the record (AppendTo, read as Create) AND, since fix round 1 (F1), on their own Create privilege on
            // sprk_communication — the message row is written app-only. No reader (a test drive of the core) adds nothing:
            // fail closed. A question Dataverse gave no answer to is a retry-able 503, not a denial (fix round 4, F1).
            if (DataverseConfirmedEntitySet(messageRoot) is { } entitySet)
            {
                var confirmed = await ExternalProjectDataEndpoints.WithDataverseRightsAsync(
                    principal, rights, entitySet, id, readCallerRights, logger, ct,
                    CreateMessagePrivilege, readCallerPrivilege).ConfigureAwait(false);
                if (confirmed.IsUnavailableFor(Spaarke.Dataverse.AccessRights.Create))
                {
                    logger.LogWarning(
                        "[EXT-MESSAGES] Post on {Entity} {RecordId} for systemuser {SystemUserId} unavailable: Dataverse gave no " +
                        "answer for the caller's Create; nothing posted.",
                        messageRoot.EntityLogicalName, id, principal.SystemUserId);
                    return ExternalProjectDataEndpoints.DataverseRightsUnavailable();
                }

                rights = confirmed.Rights;
            }

            if (!rights.HasFlag(Spaarke.Dataverse.AccessRights.Create))
            {
                logger.LogWarning(
                    "[EXT-MESSAGES] DENY post on {Entity} {RecordId} for contact {ContactId} / systemuser {SystemUserId}: the " +
                    "access level lacks Create.",
                    messageRoot.EntityLogicalName, id, principal.ContactId, principal.SystemUserId);
                return ProblemDetailsHelper.Forbidden(
                    DenyInsufficientRights, "Your access level does not permit posting messages on this record.");
            }

            var posted = await communicationService
                .PostToRecordThreadAsync(messageRoot.EntityLogicalName, id, body, principal, ct)
                .ConfigureAwait(false);

            return Results.Created(
                $"/api/v1/external/{messageRoot.Segment}/{id:D}/messages",
                new ExternalMessageDto
                {
                    SprkCommunicationid = posted.CommunicationId.ToString("D"),
                    SprkSubject = posted.Subject,
                    SprkBody = posted.Body,
                    SprkBodyformat = (int)posted.BodyFormat,
                    SprkDirection = (int)posted.Direction,
                    // The display name is Dataverse's (the stamped lookup's name); the thread read returns it.
                    SenderName = null,
                    Createdon = posted.PostedAt,
                    // The caller is, by construction, the author of the row just created.
                    IsMine = true,
                });
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Sprk.Bff.Api.Infrastructure.Exceptions.SdapProblemException ex)
        {
            // The shared filing step's own answers: 409 owner refusal, 502 core-ancestor failure, 400 validation.
            logger.LogWarning(ex, "[EXT-MESSAGES] Post on {Entity} {RecordId} refused by the communication pipeline ({Code}).",
                messageRoot.EntityLogicalName, id, ex.Code);
            return Results.Problem(statusCode: ex.StatusCode, title: ex.Title, detail: ex.Detail,
                extensions: new Dictionary<string, object?> { ["errorCode"] = ex.Code });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[EXT-MESSAGES] Record gate or message post failed for {Entity} {RecordId}.",
                messageRoot.EntityLogicalName, id);
            return Results.Problem(
                statusCode: StatusCodes.Status500InternalServerError, title: "Internal Server Error",
                detail: "An unexpected error occurred posting the message.",
                extensions: new Dictionary<string, object?> { ["errorCode"] = "EXT_MESSAGES_INTERNAL_ERROR" });
        }
    }

    /// <summary>Whether a request property names an attachment (any casing): <c>attachments</c>,
    /// <c>attachmentDocumentIds</c>, <c>sprk_hasattachments</c>, <c>files</c> …</summary>
    internal static bool NamesAttachment(string? key) =>
        !string.IsNullOrWhiteSpace(key)
        && (key.Contains("attach", StringComparison.OrdinalIgnoreCase)
            || key.Trim().Equals("file", StringComparison.OrdinalIgnoreCase)
            || key.Trim().Equals("files", StringComparison.OrdinalIgnoreCase));

    private static Task<IResult> GetMessagesAsync(
        string root,
        Guid id,
        HttpContext httpContext,
        ExternalModuleRegistry registry,
        RecordService recordService,
        CommunicationThreadReadService threadReadService,
        ExternalDataService dataService,
        [Microsoft.AspNetCore.Mvc.FromServices] ExternalInvoiceHiding? hideInvoices,
        ILogger<Program> logger,
        CancellationToken ct)
    {
        if (httpContext.Items[CallerPrincipal.HttpContextItemsKey] is not CallerPrincipal principal)
        {
            return Task.FromResult(Results.Problem(
                statusCode: StatusCodes.Status500InternalServerError,
                title: "Internal Server Error",
                detail: "Authentication context not available — ensure AddCallerPrincipalAuthorizationFilter is applied"));
        }

        return GetMessagesCoreAsync(
            root, id, principal, registry, recordService.GetRecordAsync, threadReadService, dataService.GetNonSecureRootsAsync,
            logger, ct, dataService.GetLiveParentRootsAsync, hideInvoices);
    }

    /// <summary>
    /// The message read pipeline: root lookup → record gate (<see cref="IsRootRecordReadableAsync"/>) → app-only read →
    /// allow-list projection. Separated from the route handler only so it can be driven with a test double for
    /// <paramref name="readRecord"/> (the shared <see cref="RecordService"/> needs a live ServiceClient); the handler
    /// passes <c>RecordService.GetRecordAsync</c> unchanged.
    /// </summary>
    internal static async Task<IResult> GetMessagesCoreAsync(
        string root,
        Guid id,
        CallerPrincipal principal,
        ExternalModuleRegistry registry,
        Func<string, Guid, string[]?, CancellationToken, Task<IReadOnlyDictionary<string, object?>>> readRecord,
        CommunicationThreadReadService threadReadService,
        NonSecureRootReader readNonSecureRoots,
        ILogger logger,
        CancellationToken ct,
        LiveParentRootReader? readLiveParents = null,
        ExternalInvoiceHiding? hideInvoices = null)
    {
        ArgumentNullException.ThrowIfNull(readNonSecureRoots);
        if (string.IsNullOrWhiteSpace(root) || !Roots.TryGetValue(root, out var messageRoot))
        {
            return Results.Problem(
                statusCode: StatusCodes.Status404NotFound, title: "Not Found",
                detail: "Messages are not available for this kind of record.",
                extensions: new Dictionary<string, object?> { ["errorCode"] = ErrorRootNotSupported });
        }

        try
        {
            // The gate is INSIDE the try: a Dataverse fault on its app-only record read (invoices / service requests) is
            // this endpoint's coded 500, not the global handler's. RecordNotFoundException is handled inside the gate
            // and stays a 403 (no existence oracle); no message is read unless the gate returned true.
            if (!await IsRootRecordReadableAsync(
                        messageRoot, id, principal, registry, readRecord, logger, ct, readNonSecureRoots, readLiveParents,
                        hideInvoices)
                    .ConfigureAwait(false))
            {
                logger.LogWarning(
                    "[EXT-MESSAGES] DENY {Entity} {RecordId} for contact {ContactId} / systemuser {SystemUserId}: not in the " +
                    "caller's accessible set (or absent).",
                    messageRoot.EntityLogicalName, id, principal.ContactId, principal.SystemUserId);
                return ProblemDetailsHelper.Forbidden(DenyRecordNotAccessible);
            }

            // Internal-only reaches a SYSTEMUSER principal only (the read service also asks IsExternalAsync). A contact —
            // CIAM or workforce — passes null, so the shared filter hides every internal-only row from it.
            var read = await threadReadService
                .ReadRegardingAppOnlyAsync(
                    messageRoot.EntityLogicalName, id, principal.SystemUserId,
                    RootsReadableFor(messageRoot, id, principal, readNonSecureRoots, logger, readLiveParents, hideInvoices), ct)
                .ConfigureAwait(false);

            return Results.Ok(new ExternalCollectionResponse<ExternalMessageDto>
            {
                Value = read.Messages.Select(m => ToExternalDto(m, principal)).ToList(),
                Truncated = read.Truncated,
            });
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[EXT-MESSAGES] Record gate or message read failed for {Entity} {RecordId}.",
                messageRoot.EntityLogicalName, id);
            return Results.Problem(
                statusCode: StatusCodes.Status500InternalServerError, title: "Internal Server Error",
                detail: "An unexpected error occurred reading the messages.",
                extensions: new Dictionary<string, object?> { ["errorCode"] = "EXT_MESSAGES_INTERNAL_ERROR" });
        }
    }

    /// <summary>
    /// Which messages of a thread the caller may see by the core roots they are stamped with (fix round for r3 task 039;
    /// the one rule is <see cref="ExternalMultiRootRule"/>, owner decision 2026-10-12, option B): a message also filed to a
    /// SECURE project, matter or work assignment the caller cannot read (or one whose secure state cannot be read) is
    /// withheld, so a secure matter's message does not show on a project thread. The secure flags are read once per thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Invoice threads match the secure-family rule</b> (unified-access-control-r2 task 187, fix round 1, owner
    /// round 100 decisions D1 and D4). An invoice is not a root: its thread is reached through the UNION of the caller's
    /// rights on its parents (owner decision 2026-10-11, option A "inherit"), and the gate
    /// (<see cref="IsRootRecordReadableAsync"/>) refuses an invoice whose LIVE roots include a SECURE root the caller cannot
    /// read (D4, as the invoices list withholds it, D2). Each message is then decided like any other thread's: its stamps
    /// and the live roots of its OTHER parents (a related event, document, budget ...; the route invoice is the gate's
    /// decision and is not walked again, D1). So a message also filed under secure X is withheld from a caller who cannot
    /// read X, and an ordinary message stays readable to a caller who reaches the invoice through one parent.</para>
    /// <para>Service-request threads take the same rule (requester scope).</para>
    /// </remarks>
    internal static MessageRootsFilter RootsReadableFor(
        MessageRoot root, Guid id, CallerPrincipal principal, NonSecureRootReader readNonSecureRoots, ILogger logger,
        LiveParentRootReader? readLiveParents = null, ExternalInvoiceHiding? hideInvoices = null) =>
        SecureRootsFilter(
            principal, readNonSecureRoots, logger, readLiveParents,
            string.Equals(root.EntityLogicalName, "sprk_invoice", StringComparison.OrdinalIgnoreCase)
                ? (root.EntityLogicalName, id)
                : null,
            hideInvoices);

    /// <summary>
    /// The multi-root rule as a message-read filter (<see cref="MessageRootsFilter"/>): the secure flags of the candidate
    /// rows' unreadable stamps are read in ONE batch, then each row is decided by
    /// <see cref="ExternalMultiRootRule.SecureRootsReadable(CallerPrincipal, SecureRootFlags, string?, string?, string?)"/>.
    /// Shared by the thread read and the notification feed.
    /// </summary>
    /// <remarks>Task 187 (owner round 99, D-123; #1857): the stamps are a COPY, so the live roots of each row's other
    /// parents (derived from <see cref="MessageRootLookups.Row"/>) are resolved in one bounded batch first
    /// (<paramref name="readLiveParents"/>; none given resolves nothing, so a row naming a parent is withheld) and the rule
    /// decides over the union (<see cref="ExternalMultiRootRule.ChildRowReadable"/>). <paramref name="authorizedParent"/>
    /// is the one parent the route has already decided (an invoice thread's own invoice, fix round 1 D1): not walked
    /// again.</remarks>
    internal static MessageRootsFilter SecureRootsFilter(
        CallerPrincipal principal, NonSecureRootReader readNonSecureRoots, ILogger logger,
        LiveParentRootReader? readLiveParents = null, (string Table, Guid Id)? authorizedParent = null,
        ExternalInvoiceHiding? hideInvoices = null) =>
        async (candidates, ct) =>
        {
            var live = await ExternalMultiRootRule.ReadLiveParentsAsync(
                readLiveParents,
                candidates.Select(r => ExternalMultiRootRule.Except(
                    ExternalMultiRootRule.LiveParentsOf(CommunicationTable, r.Row), authorizedParent)),
                logger, ct).ConfigureAwait(false);
            var flags = await ExternalMultiRootRule.ReadFlagsAsync(
                readNonSecureRoots, principal,
                candidates.Select(r => (IEnumerable<(ExternalGrantRootType, string?)>)RuleInput(r, live, authorizedParent)),
                logger, ct).ConfigureAwait(false);
            // ebilling-r1 task 098 (uac-r2 2026-10-21): a message about an invoice reaches an external caller only when that
            // invoice passes (a') too (the 065 gate is the read service's). The route's own invoice is the gate's decision.
            var invoices = await RegardingInvoicesVisibleAsync(principal, candidates, authorizedParent, hideInvoices, logger, ct)
                .ConfigureAwait(false);
            return r => ExternalMultiRootRule.ChildRowReadable(principal, flags, RuleInput(r, live, authorizedParent))
                && invoices(r);
        };

    /// <summary>The invoice a message regards (raw lookup values; at most one in practice).</summary>
    private static IEnumerable<string?> RegardingInvoicesOf(MessageRootLookups row) =>
        ExternalMultiRootRule.LiveParentsOf(CommunicationTable, row.Row)
            .Where(p => string.Equals(p.Table, ExternalInvoiceVisibility.InvoiceTable, StringComparison.OrdinalIgnoreCase))
            .Select(p => p.LookupValue);

    /// <summary>
    /// Task 098: the per-message (a') term of <see cref="SecureRootsFilter"/>. Reads the predicate ONCE for every invoice the
    /// candidates regard (none: no read). A message regarding an invoice the predicate hides, a lookup that is not a GUID, and
    /// any invoice when no predicate is given (fail closed) are withheld; the route's own invoice
    /// (<paramref name="authorizedParent"/>) was decided by the gate and is not asked again.
    /// </summary>
    private static async Task<Func<MessageRootLookups, bool>> RegardingInvoicesVisibleAsync(
        CallerPrincipal principal, IReadOnlyCollection<MessageRootLookups> candidates, (string Table, Guid Id)? authorizedParent,
        ExternalInvoiceHiding? hideInvoices, ILogger logger, CancellationToken ct)
    {
        bool IsAuthorized(string? value) =>
            authorizedParent is { } parent
            && string.Equals(parent.Table, ExternalInvoiceVisibility.InvoiceTable, StringComparison.OrdinalIgnoreCase)
            && Guid.TryParse(value, out var id) && id == parent.Id;

        var asked = candidates
            .SelectMany(RegardingInvoicesOf)
            .Where(v => !IsAuthorized(v))
            .Select(v => Guid.TryParse(v, out var id) ? id : Guid.Empty)
            .Where(id => id != Guid.Empty)
            .Distinct()
            .ToList();
        InvoiceVisibilityResult? decision = null;
        if (asked.Count > 0 && hideInvoices is not null)
        {
            decision = await hideInvoices(principal, asked, ct).ConfigureAwait(false);
            if (decision.Hidden.Count > 0)
            {
                logger.LogInformation(
                    "[EXT-MESSAGES] {Hidden} of {Count} regarded invoice(s) hidden by the (a') predicate ({Reasons}); their messages are withheld.",
                    decision.Hidden.Count, asked.Count, decision.HiddenSummary());
            }
        }
        else if (asked.Count > 0)
        {
            logger.LogError(
                "[EXT-MESSAGES] No invoice (a') predicate is configured; messages regarding {Count} invoice(s) are withheld (fail closed).",
                asked.Count);
        }

        return row => RegardingInvoicesOf(row).All(value =>
            IsAuthorized(value)
            || (Guid.TryParse(value, out var id) && id != Guid.Empty && decision is not null && decision.IsVisible(id)));
    }

    /// <summary>The table every message row is read from.</summary>
    private const string CommunicationTable = "sprk_communication";

    /// <summary>Task 187: one message row's rule input, its stamps and its parents' live roots.</summary>
    private static ChildRowRoots RuleInput(MessageRootLookups row, LiveParentRoots live, (string Table, Guid Id)? authorizedParent) =>
        ChildRowRoots.Of(
            CommunicationTable, row.Row, ExternalMultiRootRule.Stamps(row.Project, row.Matter, row.WorkAssignment), live,
            authorizedParent);

    /// <summary>
    /// The record gate. Fail-closed: no registered module, an empty accessible set, a missing record and a record outside
    /// the set are all <c>false</c>.
    /// </summary>
    /// <remarks>
    /// A root module (its scope attribute is the record's own id) answers from the principal alone
    /// (<see cref="ExternalModuleDescriptor.IsRecordAccessible"/>). A child or requester-scoped module (invoices: parent
    /// lookups; service requests: <c>sprk_requestedby</c>) needs the record's scope attributes, read app-only by id, and
    /// then the module's own <see cref="ExternalModuleDescriptor.ScopeRows"/> — the same predicate the module grid applies.
    /// The read happens only when the caller has SOME accessible id in that module, so a caller with none never causes a
    /// Dataverse read.
    /// <para>Task 187, fix round 1, owner decision D4: for a module with
    /// <see cref="ExternalModuleDescriptor.RequireReadOnSecureRoots"/> (the invoices module, D2) the gate reads every
    /// attribute the rule needs (<see cref="ExternalModuleDescriptor.RootRuleAttributes"/>: the invoice's matter, project and
    /// agreement), resolves the live roots of its parents (<paramref name="readLiveParents"/>) and the secure flags
    /// (<paramref name="readNonSecureRoots"/>), and applies the predicate the module grid applies. So an invoice whose live
    /// roots include a SECURE root the caller cannot read is refused, as the list withholds it. A reader not given, or a
    /// read that could not decide, refuses (fail closed).</para>
    /// </remarks>
    internal static async Task<bool> IsRootRecordReadableAsync(
        MessageRoot root,
        Guid id,
        CallerPrincipal principal,
        ExternalModuleRegistry registry,
        Func<string, Guid, string[]?, CancellationToken, Task<IReadOnlyDictionary<string, object?>>> readRecord,
        ILogger logger,
        CancellationToken ct,
        NonSecureRootReader? readNonSecureRoots = null,
        LiveParentRootReader? readLiveParents = null,
        ExternalInvoiceHiding? hideInvoices = null) =>
        (await EvaluateRecordGateAsync(
                root, id, principal, registry, readRecord, logger, ct, readNonSecureRoots, readLiveParents, hideInvoices)
            .ConfigureAwait(false))
            .Readable;

    /// <summary>
    /// The caller's rights on the record for a POST (task 034): <see cref="Spaarke.Dataverse.AccessRights.None"/> unless
    /// the read gate (<see cref="IsRootRecordReadableAsync"/>, the same evaluation) admits it; then:
    /// <list type="bullet">
    /// <item>project / matter / work assignment — the caller's stored rights on that root
    /// (<see cref="CallerPrincipal.RightsOn"/>, the dispatch the to-do and document routes use);</item>
    /// <item>invoice — the union of the caller's rights on the parent roots the invoice is filed under (the lookups the
    /// gate read), so Create on any parent the caller can reach admits a post, as Read on any admits the read;</item>
    /// <item>service request — Read and Create: the gate's requester scope is the caller's own request, and FR-11 lets the
    /// requester answer on it (the same answer the service-request document routes give).</item>
    /// </list>
    /// </summary>
    internal static async Task<Spaarke.Dataverse.AccessRights> GetRecordRightsAsync(
        MessageRoot root,
        Guid id,
        CallerPrincipal principal,
        ExternalModuleRegistry registry,
        Func<string, Guid, string[]?, CancellationToken, Task<IReadOnlyDictionary<string, object?>>> readRecord,
        ILogger logger,
        CancellationToken ct,
        NonSecureRootReader? readNonSecureRoots = null,
        LiveParentRootReader? readLiveParents = null,
        ExternalInvoiceHiding? hideInvoices = null)
    {
        var gate = await EvaluateRecordGateAsync(
                root, id, principal, registry, readRecord, logger, ct, readNonSecureRoots, readLiveParents, hideInvoices)
            .ConfigureAwait(false);
        return RightsFromGate(root, id, principal, gate).Rights;
    }

    /// <summary>
    /// <see cref="GetRecordRightsAsync"/> that also says whether a refusal was UNDECIDED: the gate refused only because a read
    /// it needed failed (the live-parent walk faulted, or the secure-flag read failed or was cut short), so the record may be
    /// readable. A caller with a "could not check" answer (the invoice document routes' 503, task 187 fix round 2 F1) uses it;
    /// the thread routes keep failing closed to 403. <c>Undecided</c> is <c>false</c> whenever the gate admits.
    /// </summary>
    internal static async Task<(Spaarke.Dataverse.AccessRights Rights, bool Undecided)> GetRecordRightsDecisionAsync(
        MessageRoot root,
        Guid id,
        CallerPrincipal principal,
        ExternalModuleRegistry registry,
        Func<string, Guid, string[]?, CancellationToken, Task<IReadOnlyDictionary<string, object?>>> readRecord,
        ILogger logger,
        CancellationToken ct,
        NonSecureRootReader? readNonSecureRoots = null,
        LiveParentRootReader? readLiveParents = null,
        ExternalInvoiceHiding? hideInvoices = null)
    {
        var gate = await EvaluateRecordGateAsync(
                root, id, principal, registry, readRecord, logger, ct, readNonSecureRoots, readLiveParents, hideInvoices)
            .ConfigureAwait(false);
        return RightsFromGate(root, id, principal, gate);
    }

    /// <summary>The rights a record-gate evaluation grants (see <see cref="GetRecordRightsAsync"/>), and whether a refusal
    /// was undecided.</summary>
    private static (Spaarke.Dataverse.AccessRights Rights, bool Undecided) RightsFromGate(
        MessageRoot root,
        Guid id,
        CallerPrincipal principal,
        (bool Readable, IReadOnlyDictionary<string, object?>? ScopeRow, bool Undecided) gate)
    {
        if (!gate.Readable)
        {
            return (Spaarke.Dataverse.AccessRights.None, gate.Undecided);
        }

        var scopeRow = gate.ScopeRow;
        return (root.EntityLogicalName switch
        {
            "sprk_project" => principal.RightsOn(ExternalGrantRootType.Project, id),
            "sprk_matter" => principal.RightsOn(ExternalGrantRootType.Matter, id),
            "sprk_workassignment" => principal.RightsOn(ExternalGrantRootType.WorkAssignment, id),
            "sprk_invoice" => RightsThroughParents(principal, scopeRow),
            "sprk_servicerequest" => Spaarke.Dataverse.AccessRights.Read | Spaarke.Dataverse.AccessRights.Create,
            _ => Spaarke.Dataverse.AccessRights.None,
        }, false);
    }

    /// <summary>The caller's rights on an invoice: the union over its parent roots (<c>sprk_matter</c>,
    /// <c>sprk_project</c>) as the gate read them; <see cref="Spaarke.Dataverse.AccessRights.None"/> with none.</summary>
    private static Spaarke.Dataverse.AccessRights RightsThroughParents(
        CallerPrincipal principal, IReadOnlyDictionary<string, object?>? scopeRow)
    {
        var rights = Spaarke.Dataverse.AccessRights.None;
        if (scopeRow is null)
        {
            return rights;
        }

        if (scopeRow.TryGetValue("sprk_matter", out var matter) && matter is Guid matterId)
        {
            rights |= principal.RightsOn(ExternalGrantRootType.Matter, matterId);
        }

        if (scopeRow.TryGetValue("sprk_project", out var project) && project is Guid projectId)
        {
            rights |= principal.RightsOn(ExternalGrantRootType.Project, projectId);
        }

        return rights;
    }

    /// <summary>
    /// The one record-gate evaluation behind <see cref="IsRootRecordReadableAsync"/> and <see cref="GetRecordRightsAsync"/>:
    /// whether the record is readable, and — for a child or requester-scoped root — the scope row the module predicate was
    /// applied to (the record's scope attributes, read app-only). <c>ScopeRow</c> is null for a root module. For a module
    /// with <see cref="ExternalModuleDescriptor.RequireReadOnSecureRoots"/> the rule's attributes, live parents and secure
    /// flags are read too (task 187 D4, see <see cref="IsRootRecordReadableAsync"/>).
    /// </summary>
    private static async Task<(bool Readable, IReadOnlyDictionary<string, object?>? ScopeRow, bool Undecided)> EvaluateRecordGateAsync(
        MessageRoot root,
        Guid id,
        CallerPrincipal principal,
        ExternalModuleRegistry registry,
        Func<string, Guid, string[]?, CancellationToken, Task<IReadOnlyDictionary<string, object?>>> readRecord,
        ILogger logger,
        CancellationToken ct,
        NonSecureRootReader? readNonSecureRoots,
        LiveParentRootReader? readLiveParents,
        ExternalInvoiceHiding? hideInvoices = null)
    {
        if (id == Guid.Empty)
        {
            return (false, null, false);
        }

        var module = registry.FindByEntity(root.EntityLogicalName);
        if (module is null)
        {
            logger.LogWarning("[EXT-MESSAGES] No external module is registered for {Entity}; refusing.", root.EntityLogicalName);
            return (false, null, false);
        }

        var dimensions = module.EffectiveDimensions;
        var primaryIdAttribute = root.EntityLogicalName + "id";
        if (dimensions.All(d => string.Equals(d.Attribute, primaryIdAttribute, StringComparison.OrdinalIgnoreCase)))
        {
            return (module.IsRecordAccessible(principal, id), null, false);
        }

        if (dimensions.All(d => d.AccessibleIds(principal).Count == 0))
        {
            return (false, null, false);
        }

        // Task 187 D4: a module with the secure-roots rule reads every attribute the rule reads, not only the dimensions.
        var attributes = (module.RequireReadOnSecureRoots
                ? dimensions.Select(d => d.Attribute).Concat(module.RootRuleAttributes)
                : dimensions.Select(d => d.Attribute))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        IReadOnlyDictionary<string, object?> record;
        try
        {
            record = await readRecord(root.EntityLogicalName, id, attributes, ct).ConfigureAwait(false);
        }
        catch (RecordNotFoundException)
        {
            return (false, null, false);
        }

        var row = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["@logicalName"] = root.EntityLogicalName,
        };
        foreach (var attribute in attributes)
        {
            if (!record.TryGetValue(attribute, out var value) || value is null)
                continue;
            if (LookupId(value) is { } parentId)
                row[attribute] = parentId;
            else if (module.RequireReadOnSecureRoots)
                row[attribute] = value; // present but not a lookup id: the rule reads it as malformed (fail closed)
        }

        if (!module.RequireReadOnSecureRoots)
            return (module.ScopeRows(principal, new[] { row }).Count > 0, row, false);

        var live = await ExternalMultiRootRule.ReadLiveParentsAsync(readLiveParents, new[] { module.LiveParents(row) }, logger, ct)
            .ConfigureAwait(false);
        var flags = readNonSecureRoots is null
            ? SecureRootFlags.None
            : await ExternalMultiRootRule.ReadFlagsAsync(
                readNonSecureRoots, principal,
                new[] { (IEnumerable<(ExternalGrantRootType, string?)>)module.RuleInput(row, live) }, logger, ct)
                .ConfigureAwait(false);
        var readable = module.ScopeRows(principal, new[] { row }, flags, live).Count > 0;
        // Fix round 2 (F1): a refusal that rests on a read that failed is UNDECIDED, not "behind the wall".
        if (!readable)
            return (false, row, live.Faulted || !flags.IsComplete);

        // ebilling-r1 task 098 (model 9.1, (a')): an invoice whose Submit Invoice source is isolated is refused like a missing
        // one, for a caller who is not an internal systemuser. No predicate, a hidden invoice and a failed read all refuse with
        // the SAME answer as a missing record (Undecided false: a fault must not be distinguishable from "hidden").
        if (string.Equals(root.EntityLogicalName, ExternalInvoiceVisibility.InvoiceTable, StringComparison.OrdinalIgnoreCase))
        {
            if (hideInvoices is null)
            {
                logger.LogError("[EXT-MESSAGES] No invoice (a') predicate is configured; refusing invoice {RecordId} (fail closed).", id);
                return (false, row, false);
            }

            var decision = await hideInvoices(principal, new[] { id }, ct).ConfigureAwait(false);
            if (!decision.IsVisible(id))
            {
                logger.LogInformation(
                    "[EXT-MESSAGES] Invoice {RecordId} hidden by the (a') predicate ({Reasons}); refused like a missing record.",
                    id, decision.HiddenSummary());
                return (false, row, false);
            }
        }

        return (true, row, false);
    }

    /// <summary>
    /// A lookup value as <see cref="RecordService"/> projects it (a <c>{ id, logicalName, name }</c> map), or a bare id;
    /// <c>null</c> for anything else (an unset lookup).
    /// </summary>
    private static Guid? LookupId(object? value) => value switch
    {
        Guid g when g != Guid.Empty => g,
        IReadOnlyDictionary<string, object?> map when map.TryGetValue("id", out var raw) && raw is Guid g && g != Guid.Empty => g,
        _ => null,
    };

    /// <summary>
    /// Whether <paramref name="principal"/> authored <paramref name="message"/>, decided by the same rule the post stamp
    /// uses (<see cref="ExternalDataService.StampCreator(Microsoft.Xrm.Sdk.Entity, CallerPrincipal)"/>): a systemuser
    /// principal authors as its systemuser (<c>sprk_sentby</c>), any other as its contact
    /// (<c>sprk_createdbycontact</c>). An empty id never matches.
    /// </summary>
    internal static bool IsAuthoredBy(ThreadMessageDto message, CallerPrincipal principal) =>
        principal.SystemUserId is { } systemUserId
            ? systemUserId != Guid.Empty && message.SentBy == systemUserId
            : principal.ContactId is { } contactId && contactId != Guid.Empty && message.CreatedByContactId == contactId;

    private static ExternalMessageDto ToExternalDto(ThreadMessageDto message, CallerPrincipal principal) => new()
    {
        SprkCommunicationid = message.MessageId.ToString("D"),
        SprkSubject = message.Subject,
        SprkBody = message.Body,
        SprkBodyformat = message.BodyFormat,
        SprkDirection = message.Direction,
        SenderName = message.SentByName,
        Createdon = message.CreatedOn,
        IsMine = IsAuthoredBy(message, principal),
    };
}
