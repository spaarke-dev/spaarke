using System.Security.Claims;
using System.Text.Json;
using Microsoft.Xrm.Sdk;
using Spaarke.Core.Auth;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.Auth;
using Sprk.Bff.Api.Infrastructure.Authentication;
using Sprk.Bff.Api.Infrastructure.Errors;
using Sprk.Bff.Api.Infrastructure.Exceptions;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Services.Ai.Context;
using Sprk.Bff.Api.Services.Communication;
using Sprk.Bff.Api.Services.Communication.Engine;
using Sprk.Bff.Api.Services.Communication.Models;

namespace Sprk.Bff.Api.Api.Filters;

/// <summary>
/// The communication routes whose record <see cref="CommunicationRecordAuthorizationFilter"/> authorizes. Each
/// value names ONE route: the id it authorizes, where that id comes from (route or bound body), the right it
/// requires and the answer a denial gets are all fixed by the value, never by request input.
/// </summary>
public enum CommunicationRecordRoute
{
    /// <summary><c>POST /api/communications/send</c> — every body id: attachments (Read), thread (visibility), inherit-from communication (visibility + AppendTo on each copied regarding record), associations (AppendTo).</summary>
    Send,

    /// <summary><c>POST /api/communications/send-bulk</c> — attachments (Read) and associations (AppendTo), once for the whole request.</summary>
    SendBulk,

    /// <summary><c>POST /api/communications/template/render</c> — Read on the regarding record; the template readable by the caller.</summary>
    TemplateRender,

    /// <summary><c>POST /api/communications/{id}/archive</c> — visibility, AppendTo on the communication, Create on sprk_document.</summary>
    Archive,

    /// <summary><c>POST /api/communications/{id}/suggest-associations</c> — communication visibility (the candidates are trimmed in the handler).</summary>
    SuggestAssociations,

    /// <summary><c>POST /api/communications/{id}/confirm-affinity</c> — visibility, the typed regarding lookup equals the target, Write on the communication; every deny is the 200 no-op.</summary>
    ConfirmAffinity,

    /// <summary><c>POST /api/communications/threads</c> — AppendTo on the body regarding record.</summary>
    CreateRecordThread,

    /// <summary><c>POST /api/communications/{communicationId}/create-task</c> — visibility, AppendTo on the regarding record, Create (and Assign) on sprk_event.</summary>
    CreateAdHocTask,

    /// <summary><c>POST /api/communications/proposals/{reviewLogId}/apply</c> — proposal visibility.</summary>
    ProposalApply,

    /// <summary><c>POST /api/communications/proposals/{reviewLogId}/dismiss</c> — proposal visibility.</summary>
    ProposalDismiss,

    /// <summary><c>POST /api/communications/proposals/{reviewLogId}/create-task/apply</c> — proposal visibility, AppendTo on its target, Create (and Assign) on sprk_event.</summary>
    ProposalCreateTaskApply,

    /// <summary><c>POST /api/communications/proposals/{reviewLogId}/undo</c> — proposal visibility.</summary>
    ProposalUndo,

    /// <summary><c>POST /api/communications/{communicationId}/tasks/{taskId}/undo</c> — communication visibility.</summary>
    TaskUndo,

    /// <summary><c>POST /api/communications/accounts/{id}/verify</c> — Write on the sprk_communicationaccount.</summary>
    AccountVerify,

    /// <summary><c>POST /api/communications/threads/{threadId}/rename</c> — Write on the thread.</summary>
    ThreadRename,

    /// <summary><c>PATCH /api/communications/threads/{threadId}/pin</c> — Write on the thread.</summary>
    ThreadPin,

    /// <summary><c>DELETE /api/communications/threads/{threadId}</c> — Write on the thread.</summary>
    ThreadDeactivate,

    /// <summary><c>DELETE /api/communications/{id}</c> — Write on the message.</summary>
    MessageDeactivate,

    /// <summary>
    /// <c>PATCH /api/communications/{id}/filing</c> (unified-access-control-r2 task 147 r1c, owner round 36 item 2) — the body
    /// names only filing columns (400 first, no I/O), then Write on the communication. AppendTo on each record it is moved
    /// under and F3 on a move out of a secure record are asked by the re-file core in the handler, as the caller.
    /// </summary>
    Refile,
}

/// <summary>
/// Adds <see cref="CommunicationRecordAuthorizationFilter"/> to a communication route (ADR-008: the per-record
/// decision is an endpoint filter in the route's own fluent chain, visible to RouteAuthorizationGuardTests).
/// </summary>
public static class CommunicationRecordAuthorizationFilterExtensions
{
    /// <summary>
    /// Authorizes, AS THE CALLER, the exact record(s) <paramref name="route"/> acts on before its handler runs.
    /// On routes that also carry the identity precondition <see cref="CommunicationAuthorizationFilter"/>, add this
    /// AFTER it.
    /// </summary>
    public static TBuilder AddCommunicationRecordAuthorizationFilter<TBuilder>(
        this TBuilder builder, CommunicationRecordRoute route) where TBuilder : IEndpointConventionBuilder
    {
        return builder.AddEndpointFilter(async (context, next) =>
        {
            var filter = new CommunicationRecordAuthorizationFilter(route);
            return await filter.InvokeAsync(context, next);
        });
    }
}

/// <summary>
/// The per-record gate for the <c>/api/communications</c> routes (unified-access-control-r2 task 161, route
/// authorization sweep 2026-10-02, owner round 9). Each route used to check only that the caller was signed in,
/// then read or write — as the BFF's own identity — a record the caller chose. This filter asks Dataverse, AS THE
/// CALLER, about the exact record the handler is about to read, attach to or change, before the handler runs.
/// </summary>
/// <remarks>
/// <para><b>Composes existing decision seams; makes no decision of its own:</b>
/// <see cref="CommunicationThreadReadService"/> (impersonated visibility plus <c>ICommunicationAccessFilter</c>),
/// <see cref="CallerRecordAccessProbe"/> (RetrievePrincipalAccess and table privileges over OBO),
/// <see cref="AuthorizationService"/> (the document decision the download routes make) and
/// <see cref="IImpersonatedCommunicationQuery"/> (an impersonated read of the template).</para>
/// <para><b>Why the check is HERE and not in the services.</b> <c>CommunicationService.SendAsync</c> is also called
/// with no user by the registration emails and the AI email disposition, and <c>ReconstructEnvelopeAsync</c> by the
/// Job B/C apply paths. A caller check inside those services would break them or tempt an app-only fallback.</para>
/// <para><b>Unknown equals denied.</b> A route id that does not exist and one the caller may not use get the same
/// answer — the route's existing not-found body (a 403 for "exists but denied" would be an existence oracle). A
/// body id gets ONE 403 reasonCode per route, identical for unknown, denied and unparseable ids.
/// <c>confirm-affinity</c> keeps its never-fails contract: every deny is the 200 <c>RecordedSignals: 0</c> no-op.</para>
/// <para><b>Fail closed (ADR-003).</b> A missing token, an unresolved caller, a failed OBO exchange, a probe or query
/// fault, or any exception in a check denies with the route's deny answer. There is no app-only path.</para>
/// <para><b>Check as the user, write as the app (owner G5).</b> Where a handler then writes app-only because a
/// server invariant needs it, this filter has already established that the caller holds the right themselves. It
/// does not change who owns the rows the handlers create (task 146) or who a task is for (task 152).</para>
/// <para><b>Latency.</b> A denied or non-existent record pays <c>CallerRecordAccessProbe</c>'s not-found backoff
/// (~1.6 s); grants do not. None of these checks is cached.</para>
/// </remarks>
public sealed class CommunicationRecordAuthorizationFilter : IEndpointFilter
{
    // ── Entity SET names and privilege names. Each value was read from live metadata (spaarkedev1, read-only
    //    EntityDefinitions / privileges GETs, 2026-10-03, task-161 note §2) and is pinned by
    //    CommunicationRecordAuthorizationContractTests. Never derive a set by pluralizing a logical name: a wrong
    //    set fails closed as a deny indistinguishable from "no access", which hides the bug.
    public const string CommunicationEntitySet = "sprk_communications";
    public const string ThreadEntitySet = "sprk_communicationthreads";
    public const string CommunicationAccountEntitySet = "sprk_communicationaccounts";
    public const string TemplateEntitySet = "templates";

    /// <summary>The proposal table's LOGICAL name — read app-only, as the queue feed does; its row is never returned.</summary>
    public const string EmailReviewLogEntity = "sprk_emailreviewlog";

    /// <summary>Create on <c>sprk_event</c>. Schema name <c>sprk_Event</c>, so the privilege is not all lower case.</summary>
    public const string CreateEventPrivilege = "prvCreatesprk_Event";

    /// <summary>Assign on <c>sprk_event</c> — needed while <c>TaskActionCore</c> writes <c>ownerid</c> from <c>AssignedTo</c>.</summary>
    public const string AssignEventPrivilege = "prvAssignsprk_Event";

    /// <summary>Create on <c>sprk_document</c> — the archive creates sprk_document rows.</summary>
    public const string CreateDocumentPrivilege = "prvCreatesprk_Document";

    /// <summary>
    /// <c>CommunicationService.MaxAttachmentCount</c> (a private constant there; this task does not edit that
    /// file). Above it the request gets that method's 400 BEFORE any rights query; parity pinned by a test.
    /// </summary>
    public const int MaxAttachmentCount = 150;

    // ── The ONE 403 reasonCode per body-id route.
    public const string SendDenyReasonCode = "sdap.access.deny.communication.send";
    public const string SendBulkDenyReasonCode = "sdap.access.deny.communication.send_bulk";
    public const string TemplateRenderDenyReasonCode = "sdap.access.deny.communication.template_render";
    public const string ThreadCreateDenyReasonCode = "sdap.access.deny.communication.thread_create";
    public const string CreateTaskDenyReasonCode = "sdap.access.deny.communication.create_task";
    public const string CreateTaskApplyDenyReasonCode = "sdap.access.deny.communication.create_task_apply";
    public const string ArchiveDenyReasonCode = "sdap.access.deny.communication.archive";

    /// <summary>Task 147 r1c: the filing route's one deny code (unknown, invisible and unwritable alike).</summary>
    public const string RefileDenyCode = "COMMUNICATION_REFILE_FORBIDDEN";

    private readonly CommunicationRecordRoute _route;

    public CommunicationRecordAuthorizationFilter(CommunicationRecordRoute route) => _route = route;

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var gate = new Gate(context, _route);

        // The request's SHAPE first: a regarding type outside the live-verified catalogue is a 400 that depends on
        // neither the caller nor any record, so it is answered before ANY Dataverse call — including the
        // caller-resolution lookup below (an app-only systemuser query).
        // Then the id-INDEPENDENT preconditions, so their answer cannot vary with whether a record exists: the
        // caller's own bearer token (every rights question is asked with it) and a resolvable Dataverse systemuser
        // (every impersonated question is asked as it). Either missing denies with the route's own deny answer.
        var denial = gate.CheckRequestShape() ?? await gate.CheckCallerPreconditionsAsync() ?? _route switch
        {
            CommunicationRecordRoute.Send => await gate.AuthorizeSendAsync(),
            CommunicationRecordRoute.SendBulk => await gate.AuthorizeSendBulkAsync(),
            CommunicationRecordRoute.TemplateRender => await gate.AuthorizeTemplateRenderAsync(),
            CommunicationRecordRoute.SuggestAssociations => await gate.AuthorizeVisibleCommunicationAsync("id"),
            CommunicationRecordRoute.TaskUndo => await gate.AuthorizeVisibleCommunicationAsync("communicationId"),
            CommunicationRecordRoute.Archive => await gate.AuthorizeArchiveAsync(),
            CommunicationRecordRoute.ConfirmAffinity => await gate.AuthorizeConfirmAffinityAsync(),
            CommunicationRecordRoute.CreateRecordThread => await gate.AuthorizeCreateRecordThreadAsync(),
            CommunicationRecordRoute.CreateAdHocTask => await gate.AuthorizeCreateAdHocTaskAsync(),
            CommunicationRecordRoute.ProposalApply
                or CommunicationRecordRoute.ProposalDismiss
                or CommunicationRecordRoute.ProposalUndo => await gate.AuthorizeVisibleProposalAsync(),
            CommunicationRecordRoute.ProposalCreateTaskApply => await gate.AuthorizeProposalCreateTaskApplyAsync(),
            CommunicationRecordRoute.AccountVerify => await gate.AuthorizeAccountVerifyAsync(),
            CommunicationRecordRoute.ThreadRename
                or CommunicationRecordRoute.ThreadPin
                or CommunicationRecordRoute.ThreadDeactivate => await gate.AuthorizeThreadWriteAsync(),
            CommunicationRecordRoute.MessageDeactivate
                or CommunicationRecordRoute.Refile => await gate.AuthorizeMessageWriteAsync(),
            _ => Denial.Problem(new SdapProblemException("COMMUNICATION_ROUTE_UNDECLARED", "Forbidden", "Access denied", 403)),
        };

        if (denial is null)
        {
            return await next(context);
        }

        // A route-id deny reproduces the route's EXISTING not-found body. Throwing the same SdapProblemException the
        // handler or service throws makes the global handler render it identically (only the correlation id differs).
        if (denial.Exception is not null)
        {
            throw denial.Exception;
        }

        return denial.Result;
    }

    // =============================================================================================
    // Deny answers
    // =============================================================================================

    /// <summary>A denial: either a result to return, or the route's existing problem exception to rethrow.</summary>
    private sealed record Denial(IResult? Result, SdapProblemException? Exception)
    {
        public static Denial Of(IResult result) => new(result, null);

        public static Denial Problem(SdapProblemException exception) => new(null, exception);
    }

    /// <summary>The body <c>/archive</c> and <c>/suggest-associations</c> return for a missing communication (the service's own).</summary>
    internal static SdapProblemException CommunicationNotFound(Guid id) => new(
        code: "COMMUNICATION_NOT_FOUND",
        title: "Communication not found",
        detail: $"Communication with ID '{id}' does not exist.",
        statusCode: 404);

    /// <summary>The body every proposal route returns for an unknown <c>reviewLogId</c>.</summary>
    internal static SdapProblemException ProposalNotFound(Guid reviewLogId) => new(
        code: "PROPOSAL_NOT_FOUND",
        title: "Proposal Not Found",
        detail: $"No sprk_emailreviewlog proposal was found for id {reviewLogId}.",
        statusCode: 404);

    /// <summary>The body the verify route returns for a missing account.</summary>
    internal static SdapProblemException AccountNotFound(Guid id) => new(
        code: "ACCOUNT_NOT_FOUND",
        title: "Communication account not found",
        detail: $"Communication account with ID '{id}' does not exist.",
        statusCode: 404);

    /// <summary>The 400 <c>SendAsync</c> returns above <see cref="MaxAttachmentCount"/> attachments.</summary>
    internal static SdapProblemException AttachmentLimitExceeded(int requested) => new(
        code: "ATTACHMENT_LIMIT_EXCEEDED",
        title: "Too Many Attachments",
        detail: $"Maximum {MaxAttachmentCount} attachments allowed. Received {requested}.",
        statusCode: 400);

    private static SdapProblemException ValidationError(string detail) =>
        new(code: "VALIDATION_ERROR", title: "Validation Error", detail: detail, statusCode: 400);

    // =============================================================================================
    // The per-request evaluation
    // =============================================================================================

    /// <summary>One request's evaluation: resolves the seams lazily and fails closed when one is missing.</summary>
    private sealed class Gate
    {
        private readonly EndpointFilterInvocationContext _context;
        private readonly CommunicationRecordRoute _route;
        private readonly HttpContext _http;
        private readonly IServiceProvider _services;
        private readonly ILogger _logger;
        private readonly string? _token;
        private Guid? _callerSystemUserId;

        public Gate(EndpointFilterInvocationContext context, CommunicationRecordRoute route)
        {
            _context = context;
            _route = route;
            _http = context.HttpContext;
            _services = _http.RequestServices;
            _logger = _services.GetService<ILogger<CommunicationRecordAuthorizationFilter>>()
                      ?? (ILogger)Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance;
            _token = TokenHelper.ExtractBearerTokenOrNull(_http);
        }

        private ClaimsPrincipal User => _http.User;

        private CancellationToken Ct => _http.RequestAborted;

        // ── Preconditions that do not depend on the record ───────────────────────────────────────

        /// <summary>
        /// The 400 a request gets for its shape alone — a missing regarding, or a regarding type with no live-verified
        /// entity set. It depends on neither the caller nor any record, so it is decided with NO Dataverse call.
        /// </summary>
        public Denial? CheckRequestShape() => _route switch
        {
            CommunicationRecordRoute.CreateRecordThread => RecordThreadTarget().Invalid,
            CommunicationRecordRoute.TemplateRender => TemplateRegardingTarget().Invalid,
            CommunicationRecordRoute.Refile => FilingShape(),
            _ => null,
        };

        /// <summary>Task 147 r1c: the filing route's body — only the regarding lookups and resolver fields (the shared rule).</summary>
        private Denial? FilingShape() =>
            ChildRecordEndpoints.FilingShapeProblem(
                    _context.Arguments.OfType<JsonElement>().FirstOrDefault(), "sprk_communication") is { } problem
                ? Denial.Of(problem)
                : null;

        /// <summary>The thread's regarding entity set, or the 400 the request gets for its shape.</summary>
        private (string? EntitySet, Denial? Invalid) RecordThreadTarget()
        {
            var request = Bound<CreateRecordThreadRequest>();
            var type = request?.RegardingEntityType?.Trim();
            if (string.IsNullOrWhiteSpace(type) || request!.RegardingRecordId == Guid.Empty)
            {
                // The handler's own 400, raised here so no Dataverse call is spent on an incomplete request.
                return (null, Denial.Problem(ValidationError("regardingEntityType and a non-empty regardingRecordId are required.")));
            }

            var entitySet = RegardingNameFields.EntitySetName(type.ToLowerInvariant());
            return entitySet is null
                ? (null, Denial.Problem(ValidationError($"'{type}' is not a supported regarding record type.")))
                : (entitySet, null);
        }

        /// <summary>
        /// The template's regarding entity set (null when the request names no regarding, or no template — the
        /// handler's 400), or the 400 a regarding type outside the catalogue gets.
        /// </summary>
        private (string? EntitySet, Denial? Invalid) TemplateRegardingTarget()
        {
            var request = Bound<CommunicationTemplateRenderRequest>();
            if (request is null
                || request.TemplateId == Guid.Empty
                || string.IsNullOrWhiteSpace(request.RegardingEntityType)
                || request.RegardingRecordId is not { } rid
                || rid == Guid.Empty)
            {
                return (null, null);
            }

            var type = request.RegardingEntityType.Trim();
            var entitySet = RegardingNameFields.EntitySetName(type.ToLowerInvariant());
            return entitySet is null
                ? (null, Denial.Problem(ValidationError($"'{type}' is not a supported regarding record type.")))
                : (entitySet, null);
        }

        /// <summary>
        /// No bearer token, or a caller that does not resolve to a Dataverse systemuser, denies — with the route's
        /// own deny answer, before any record is looked at, so the answer is the same for every id.
        /// </summary>
        public async Task<Denial?> CheckCallerPreconditionsAsync()
        {
            if (!string.IsNullOrWhiteSpace(_token)
                && await Safely("caller resolution", async () => await CallerSystemUserIdAsync() is not null))
            {
                return null;
            }

            _logger.LogWarning(
                "Communication record authorization DENIED on {Route}: no bearer token or no resolvable caller (fail closed)",
                _route);
            return RouteDenial();
        }

        /// <summary>The route's deny answer for a reason that does not depend on the record.</summary>
        private Denial RouteDenial() => _route switch
        {
            CommunicationRecordRoute.Send => Forbidden(SendDenyReasonCode),
            CommunicationRecordRoute.SendBulk => Forbidden(SendBulkDenyReasonCode),
            CommunicationRecordRoute.TemplateRender => Forbidden(TemplateRenderDenyReasonCode),
            CommunicationRecordRoute.CreateRecordThread => Forbidden(ThreadCreateDenyReasonCode),
            CommunicationRecordRoute.ConfirmAffinity => Denial.Of(TypedResults.Ok(RecordAffinityConfirmationResult.None)),
            CommunicationRecordRoute.Archive
                or CommunicationRecordRoute.SuggestAssociations => Denial.Problem(CommunicationNotFound(RouteGuid("id"))),
            CommunicationRecordRoute.CreateAdHocTask
                or CommunicationRecordRoute.TaskUndo => Denial.Problem(CommunicationNotFound(RouteGuid("communicationId"))),
            CommunicationRecordRoute.ProposalApply
                or CommunicationRecordRoute.ProposalDismiss
                or CommunicationRecordRoute.ProposalCreateTaskApply
                or CommunicationRecordRoute.ProposalUndo => Denial.Problem(ProposalNotFound(RouteGuid("reviewLogId"))),
            CommunicationRecordRoute.AccountVerify => Denial.Problem(AccountNotFound(RouteGuid("id"))),
            CommunicationRecordRoute.ThreadRename => ThreadWriteDenial("THREAD_RENAME_FORBIDDEN"),
            CommunicationRecordRoute.ThreadPin => ThreadWriteDenial("THREAD_PIN_FORBIDDEN"),
            CommunicationRecordRoute.ThreadDeactivate => ThreadWriteDenial("THREAD_DELETE_FORBIDDEN"),
            CommunicationRecordRoute.Refile => RefileDenial(),
            _ => MessageWriteDenial(),
        };

        // ── Route families ───────────────────────────────────────────────────────────────────────

        public async Task<Denial?> AuthorizeVisibleCommunicationAsync(string routeKey)
        {
            var id = RouteGuid(routeKey);
            return await CanSeeCommunicationAsync(id) ? null : Denial.Problem(CommunicationNotFound(id));
        }

        public async Task<Denial?> AuthorizeArchiveAsync()
        {
            var id = RouteGuid("id");
            if (!await CanSeeCommunicationAsync(id))
                return Denial.Problem(CommunicationNotFound(id));

            // The archive creates sprk_document rows that carry sprk_relatedcommunication (AppendTo on the
            // communication) — app-only, so the caller must be able to create a document themselves (G5).
            if (!await HasRightsAsync(CommunicationEntitySet, id, AccessRights.AppendTo)
                || !await HoldsPrivilegeAsync(CreateDocumentPrivilege))
                return Forbidden(ArchiveDenyReasonCode);

            return null;
        }

        public async Task<Denial?> AuthorizeConfirmAffinityAsync()
        {
            // NFR-04: this route never fails the user's confirmation. Every deny is EXACTLY what a non-existent
            // communication id answers today — 200 with zero signals recorded — so AffinityStore is never reached.
            var noOp = Denial.Of(TypedResults.Ok(RecordAffinityConfirmationResult.None));

            var id = RouteGuid("id");
            var request = Bound<RecordAffinityConfirmationRequest>();
            var field = string.IsNullOrWhiteSpace(request?.TargetEntityType)
                ? null
                : RegardingFieldMap.FieldFor(request.TargetEntityType.Trim());
            if (field is null || !Guid.TryParse(request!.TargetRecordId, out var targetId) || targetId == Guid.Empty)
                return noOp;

            // The learning signal is only honest when the human's regarding write really happened: the caller's
            // own read of the communication must show the typed lookup for that type set to that record.
            var lookupColumn = $"_{field}_value";
            var row = await ReadVisibleCommunicationAsync(id, new[] { lookupColumn });
            if (row is null || ReadGuid(row, lookupColumn) != targetId)
                return noOp;

            return await HasRightsAsync(CommunicationEntitySet, id, AccessRights.Write) ? null : noOp;
        }

        public async Task<Denial?> AuthorizeCreateRecordThreadAsync()
        {
            // The shape was checked before the caller preconditions (CheckRequestShape); re-derived, never assumed.
            var (entitySet, invalid) = RecordThreadTarget();
            if (invalid is not null || entitySet is null)
                return invalid ?? Forbidden(ThreadCreateDenyReasonCode);

            return await HasRightsAsync(entitySet, Bound<CreateRecordThreadRequest>()!.RegardingRecordId, AccessRights.AppendTo)
                ? null
                : Forbidden(ThreadCreateDenyReasonCode);
        }

        public async Task<Denial?> AuthorizeCreateAdHocTaskAsync()
        {
            var communicationId = RouteGuid("communicationId");
            if (!await CanSeeCommunicationAsync(communicationId))
                return Denial.Problem(CommunicationNotFound(communicationId));

            var request = Bound<CreateAdHocTaskRequest>();
            var regardingEntity = request?.RegardingEntity?.Trim();
            if (request is null
                || string.IsNullOrWhiteSpace(regardingEntity)
                || request.RegardingRecordId is not { } regardingId
                || regardingId == Guid.Empty)
            {
                // Nothing to attach to: the handler (400) and the service (422) refuse before any write.
                return null;
            }

            return await AuthorizeTaskCreateAsync(regardingEntity, regardingId, request.AssignedTo, CreateTaskDenyReasonCode);
        }

        public async Task<Denial?> AuthorizeVisibleProposalAsync()
        {
            var reviewLogId = RouteGuid("reviewLogId");
            var proposal = await LoadProposalAsync(reviewLogId);
            return proposal is not null && await CanSeeCommunicationAsync(proposal.CommunicationId)
                ? null
                : Denial.Problem(ProposalNotFound(reviewLogId));
        }

        public async Task<Denial?> AuthorizeProposalCreateTaskApplyAsync()
        {
            var reviewLogId = RouteGuid("reviewLogId");
            var proposal = await LoadProposalAsync(reviewLogId);
            if (proposal is null || !await CanSeeCommunicationAsync(proposal.CommunicationId))
                return Denial.Problem(ProposalNotFound(reviewLogId));

            if (string.IsNullOrWhiteSpace(proposal.TargetEntity)
                || !Guid.TryParse(proposal.TargetRecordId, out var targetId)
                || targetId == Guid.Empty)
            {
                // A malformed proposal names no record: the service refuses it (422) before any write.
                return null;
            }

            var request = Bound<ApplyCreateTaskRequest>();
            return await AuthorizeTaskCreateAsync(proposal.TargetEntity, targetId, request?.AssignedTo, CreateTaskApplyDenyReasonCode);
        }

        public async Task<Denial?> AuthorizeAccountVerifyAsync()
        {
            // The verify overwrites the account's verification columns and acts on its mailbox — Write on the record.
            var id = RouteGuid("id");
            return await HasRightsAsync(CommunicationAccountEntitySet, id, AccessRights.Write)
                ? null
                : Denial.Problem(AccountNotFound(id));
        }

        public async Task<Denial?> AuthorizeThreadWriteAsync()
        {
            // A rename, pin or deactivate writes the SHARED thread record: Write on it (owner D1), not just Read.
            var threadId = RouteGuid("threadId");
            return await HasRightsAsync(ThreadEntitySet, threadId, AccessRights.Write) ? null : RouteDenial();
        }

        public async Task<Denial?> AuthorizeMessageWriteAsync()
        {
            var id = RouteGuid("id");
            return await HasRightsAsync(CommunicationEntitySet, id, AccessRights.Write) ? null : RouteDenial();
        }

        /// <summary>The thread handlers' own visibility-deny body, so "cannot write" and "cannot see / does not exist" read alike.</summary>
        private static Denial ThreadWriteDenial(string code) => Denial.Problem(new SdapProblemException(
            code: code,
            title: "Forbidden",
            detail: "The thread does not exist or is not visible to the caller.",
            statusCode: 403));

        /// <summary>Task 147 r1c: the filing route's deny — the same words for a communication that does not exist, one the
        /// caller cannot see and one they cannot write (no existence oracle).</summary>
        private static Denial RefileDenial() => Denial.Problem(new SdapProblemException(
            code: RefileDenyCode,
            title: "Forbidden",
            detail: "The communication does not exist or is not visible to the caller, or the caller may not change it.",
            statusCode: 403));

        /// <summary>The message-deactivate handler's own visibility-deny body.</summary>
        private static Denial MessageWriteDenial() => Denial.Problem(new SdapProblemException(
            code: "MESSAGE_DELETE_FORBIDDEN",
            title: "Forbidden",
            detail: "The message does not exist or is not visible to the caller.",
            statusCode: 403));

        public async Task<Denial?> AuthorizeTemplateRenderAsync()
        {
            var request = Bound<CommunicationTemplateRenderRequest>();
            if (request is null || request.TemplateId == Guid.Empty)
            {
                // The handler's 400 — nothing to authorize.
                return null;
            }

            // The type allow-list was checked before the caller preconditions (CheckRequestShape); re-derived here.
            var (entitySet, invalid) = TemplateRegardingTarget();
            if (invalid is not null)
                return invalid;

            if (entitySet is not null
                && !await HasRightsAsync(entitySet, request.RegardingRecordId!.Value, AccessRights.Read))
                return Forbidden(TemplateRenderDenyReasonCode);

            // The template body is fetched app-only inside EmailTemplateService (G5: checked as the user, read as the
            // app). A template the caller cannot read gets EXACTLY the answer a non-existent one gets.
            return await CanReadTemplateAsync(request.TemplateId)
                ? null
                : Denial.Of(CommunicationTemplateEndpoints.TemplateNotFound(request.TemplateId));
        }

        public async Task<Denial?> AuthorizeSendAsync()
        {
            var request = Bound<SendCommunicationRequest>();
            if (request is null)
                return null; // nothing bound — the framework / handler reject the request

            return await AuthorizeSendBodyAsync(
                request.AttachmentDocumentIds,
                request.ThreadId,
                request.InheritRegardingFromCommunicationId,
                request.Associations,
                SendDenyReasonCode);
        }

        public async Task<Denial?> AuthorizeSendBulkAsync()
        {
            var request = Bound<BulkSendRequest>();
            if (request is null)
                return null;

            // Once for the WHOLE request, before the first per-recipient send: a deny is one 403, never a 207 with
            // per-recipient failures (which would still have sent to the recipients before the failing one).
            return await AuthorizeSendBodyAsync(
                request.AttachmentDocumentIds, threadId: null, inheritFrom: null, request.Associations, SendBulkDenyReasonCode);
        }

        // ── Shared checks ───────────────────────────────────────────────────────────────────────

        private async Task<Denial?> AuthorizeSendBodyAsync(
            string[]? attachmentIds,
            Guid? threadId,
            Guid? inheritFrom,
            CommunicationAssociation[]? associations,
            string reasonCode)
        {
            // SendAsync's own cap, BEFORE any rights query: 151 ids must not cost 151 Dataverse round trips.
            if (attachmentIds is { Length: > MaxAttachmentCount })
                return Denial.Problem(AttachmentLimitExceeded(attachmentIds.Length));

            // (1) Attachments: the decision the document download route makes (operation "read"). A reader can
            //     already download the bytes, so emailing them adds no capability. An unparseable id is denied with
            //     the same body.
            foreach (var raw in (attachmentIds ?? Array.Empty<string>()).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (!Guid.TryParse(raw, out var documentId) || documentId == Guid.Empty
                    || !await CanReadDocumentAsync(documentId))
                    return Forbidden(reasonCode);
            }

            // (2) The thread the message is stamped into (and its participants granted access to).
            if (threadId is { } tid && !await CanSeeThreadAsync(tid))
                return Forbidden(reasonCode);

            // (3) Associations and every regarding record copied from the inherit-from communication: AppendTo.
            var targets = new List<(string EntitySet, Guid RecordId)>();
            foreach (var association in associations ?? Array.Empty<CommunicationAssociation>())
            {
                var set = string.IsNullOrWhiteSpace(association?.EntityType)
                    ? null
                    : RegardingNameFields.EntitySetName(association.EntityType.Trim().ToLowerInvariant());
                if (set is null || association!.EntityId == Guid.Empty)
                    return Forbidden(reasonCode);
                targets.Add((set, association.EntityId));
            }

            if (inheritFrom is { } sourceId)
            {
                var lookupColumns = RegardingFieldMap.All.Select(x => $"_{x.RegardingField}_value").ToArray();
                var source = await ReadVisibleCommunicationAsync(sourceId, lookupColumns);
                if (source is null)
                    return Forbidden(reasonCode);

                foreach (var (entityLogicalName, field) in RegardingFieldMap.All)
                {
                    if (ReadGuid(source, $"_{field}_value") is not { } copiedId || copiedId == Guid.Empty)
                        continue;

                    var set = RegardingNameFields.EntitySetName(entityLogicalName);
                    if (set is null)
                        return Forbidden(reasonCode); // a copied regarding we cannot address is never guessed
                    targets.Add((set, copiedId));
                }
            }

            return await HaveRightsOnAllAsync(targets, AccessRights.AppendTo) ? null : Forbidden(reasonCode);
        }

        /// <summary>AppendTo on the regarding record, Create on sprk_event, and Assign when the owner is someone else.</summary>
        private async Task<Denial?> AuthorizeTaskCreateAsync(
            string regardingEntity, Guid regardingId, Guid? assignedTo, string reasonCode)
        {
            // A type outside the live-verified catalogue (e.g. sprk_communication, sprk_recordtype_ref, which
            // TaskActionCore also accepts) is denied, never guessed.
            var entitySet = RegardingNameFields.EntitySetName(regardingEntity.Trim().ToLowerInvariant());
            if (entitySet is null
                || !await HasRightsAsync(entitySet, regardingId, AccessRights.AppendTo)
                || !await HoldsPrivilegeAsync(CreateEventPrivilege))
                return Forbidden(reasonCode);

            // Naming SOMEONE ELSE as the task's owner is an Assign. Kept at the sweep integration with task 146: 146 owns
            // a task filed under an ownership parent by that parent's team (the supplied owner is ignored), but under a
            // regarding that is NOT an ownership parent (a contact, an account, an organization) TaskActionCore still
            // takes the supplied owner's business unit for the owning team — so the supplied owner still decides where
            // the row lives. Naming oneself needs no Assign privilege.
            if (assignedTo is { } owner)
            {
                var caller = await CallerSystemUserIdAsync();
                if (caller is null)
                    return Forbidden(reasonCode);
                if (owner != caller.Value && !await HoldsPrivilegeAsync(AssignEventPrivilege))
                    return Forbidden(reasonCode);
            }

            return null;
        }

        private Denial Forbidden(string reasonCode)
        {
            _logger.LogWarning(
                "Communication record authorization DENIED on {Route} for caller {UserId}: {ReasonCode}",
                _route, CallerResolution.ResolveObjectId(User), reasonCode);
            return Denial.Of(ProblemDetailsHelper.Forbidden(reasonCode, traceId: _http.TraceIdentifier));
        }

        // ── Seams, each fail-closed: a missing seam or any fault answers "no" ────────────────────

        private Task<bool> CanSeeCommunicationAsync(Guid id) =>
            Safely("communication visibility", async () => await ReadVisibleCommunicationCoreAsync(id, null) is not null);

        private async Task<IReadOnlyDictionary<string, JsonElement>?> ReadVisibleCommunicationAsync(
            Guid id, IReadOnlyCollection<string> columns)
        {
            IReadOnlyDictionary<string, JsonElement>? row = null;
            await Safely("communication read", async () =>
            {
                row = await ReadVisibleCommunicationCoreAsync(id, columns);
                return row is not null;
            });
            return row;
        }

        private Task<IReadOnlyDictionary<string, JsonElement>?> ReadVisibleCommunicationCoreAsync(
            Guid id, IReadOnlyCollection<string>? columns) =>
            Required<CommunicationThreadReadService>().ReadVisibleCommunicationAsync(id, User, columns, Ct);

        private Task<bool> CanSeeThreadAsync(Guid threadId) =>
            Safely("thread visibility", () => Required<CommunicationThreadReadService>().CanCallerSeeThreadAsync(threadId, User, Ct));

        private Task<bool> HasRightsAsync(string entitySet, Guid recordId, AccessRights required) =>
            Safely($"rights on {entitySet}", async () =>
            {
                if (recordId == Guid.Empty)
                    return false;
                var rights = await Required<CallerRecordAccessProbe>().GetCallerRightsAsync(_token, entitySet, recordId, Ct);
                return (rights & required) == required;
            });

        private Task<bool> HaveRightsOnAllAsync(IReadOnlyList<(string EntitySet, Guid RecordId)> targets, AccessRights required) =>
            Safely("rights on the body records", async () =>
            {
                var distinct = targets.Distinct().ToList();
                if (distinct.Count == 0)
                    return true;
                var rights = await Required<CallerRecordAccessProbe>().GetCallerRightsForRecordsAsync(_token, distinct, Ct);
                return rights.Count == distinct.Count && rights.All(r => (r & required) == required);
            });

        private Task<bool> HoldsPrivilegeAsync(string privilegeName) =>
            Safely($"privilege {privilegeName}", () =>
                Required<CallerRecordAccessProbe>().CallerHoldsPrivilegeAsync(_token, privilegeName, Ct));

        private Task<bool> CanReadDocumentAsync(Guid documentId) =>
            Safely("document read", async () =>
            {
                var userId = CallerResolution.ResolveObjectId(User);
                if (string.IsNullOrEmpty(userId))
                    return false;

                var result = await Required<AuthorizationService>().AuthorizeAsync(new AuthorizationContext
                {
                    UserId = userId,
                    ResourceId = documentId.ToString(),
                    Operation = "read",
                    CorrelationId = _http.TraceIdentifier,
                    UserAccessToken = _token,
                }, Ct);
                return result.IsAllowed;
            });

        private Task<bool> CanReadTemplateAsync(Guid templateId) =>
            Safely("template read", async () =>
            {
                var caller = await CallerSystemUserIdAsync();
                if (caller is null)
                    return false;

                var rows = await Required<IImpersonatedCommunicationQuery>().QueryAsync(
                    TemplateEntitySet,
                    $"$select=templateid&$filter=templateid eq {templateId}&$top=1",
                    caller.Value,
                    Ct);
                return rows.Count > 0;
            });

        private async Task<Guid?> CallerSystemUserIdAsync()
        {
            if (_callerSystemUserId is { } known)
                return known;

            var resolution = await Required<ICallerSystemUserResolver>().ResolveAsync(User, Ct);
            if (resolution.IsResolved && Guid.TryParse(resolution.SystemUserId, out var id) && id != Guid.Empty)
            {
                _callerSystemUserId = id;
                return id;
            }

            return null;
        }

        /// <summary>The proposal's communication and target, read app-only (as the queue feed does); never returned.</summary>
        private async Task<ProposalRow?> LoadProposalAsync(Guid reviewLogId)
        {
            ProposalRow? proposal = null;
            await Safely("proposal load", async () =>
            {
                var row = await Required<IGenericEntityService>().RetrieveAsync(
                    EmailReviewLogEntity,
                    reviewLogId,
                    new[] { "sprk_communication", "sprk_targetentity", "sprk_targetrecordid" },
                    Ct);

                var communication = row.GetAttributeValue<EntityReference>("sprk_communication");
                if (communication is null || communication.Id == Guid.Empty)
                    return false;

                proposal = new ProposalRow(
                    communication.Id,
                    row.GetAttributeValue<string>("sprk_targetentity")?.Trim(),
                    row.GetAttributeValue<string>("sprk_targetrecordid")?.Trim());
                return true;
            });
            return proposal;
        }

        private sealed record ProposalRow(Guid CommunicationId, string? TargetEntity, string? TargetRecordId);

        /// <summary>Runs one check; a missing seam or any fault (other than the request being aborted) is a "no".</summary>
        private async Task<bool> Safely(string what, Func<Task<bool>> check)
        {
            try
            {
                return await check();
            }
            catch (OperationCanceledException) when (Ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Communication record authorization: {Check} faulted on {Route}; denying (fail closed)", what, _route);
                return false;
            }
        }

        private T Required<T>() where T : notnull => _services.GetRequiredService<T>();

        private T? Bound<T>() where T : class => _context.Arguments.OfType<T>().FirstOrDefault();

        private Guid RouteGuid(string key) =>
            _http.Request.RouteValues.TryGetValue(key, out var raw) && Guid.TryParse(raw?.ToString(), out var id)
                ? id
                : Guid.Empty;

        private static Guid? ReadGuid(IReadOnlyDictionary<string, JsonElement> row, string key) =>
            row.TryGetValue(key, out var v) && v.ValueKind == JsonValueKind.String && Guid.TryParse(v.GetString(), out var g)
                ? g
                : null;
    }
}
