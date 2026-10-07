using System.Reflection;
using Spaarke.Core.Auth;
using Sprk.Bff.Api.Api.Agent;
using Sprk.Bff.Api.Api.Ai;
using Sprk.Bff.Api.Infrastructure.Auth;
using Sprk.Bff.Api.Infrastructure.Authentication;
using Sprk.Bff.Api.Infrastructure.Errors;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Models.Ai.Chat;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Chat;

namespace Sprk.Bff.Api.Api.Filters;

/// <summary>
/// Extension methods for adding AI authorization to endpoints.
/// </summary>
public static class AiAuthorizationFilterExtensions
{
    /// <summary>
    /// Adds AI document authorization to an endpoint.
    /// Reads documentId from request body and verifies read access.
    /// </summary>
    /// <remarks>
    /// A filter FACTORY (task 164) so the chat-context evaluation is keyed on the handler's DECLARED request type — a
    /// route whose body is <see cref="DispatchSessionRequest"/> is recognised even when the body is null — and never on the
    /// mere presence of a <c>sessionId</c>. A handler that binds none of the chat DTOs gets exactly the filter it had.
    /// </remarks>
    public static TBuilder AddAiAuthorizationFilter<TBuilder>(
        this TBuilder builder) where TBuilder : IEndpointConventionBuilder
    {
        return builder.AddEndpointFilterFactory((factoryContext, next) =>
        {
            var chatParameter = AiAuthorizationFilter.FindChatContextParameter(factoryContext.MethodInfo);
            return invocationContext =>
            {
                var authService = invocationContext.HttpContext.RequestServices.GetRequiredService<IAiAuthorizationService>();
                var logger = invocationContext.HttpContext.RequestServices.GetService<ILogger<AiAuthorizationFilter>>();
                var filter = new AiAuthorizationFilter(authService, logger);
                return filter.InvokeAsync(invocationContext, next, chatParameter);
            };
        });
    }
}

/// <summary>
/// Authorization filter for AI endpoints.
/// Validates user has read access to the document being analyzed.
/// Extracts documentId from request body (DocumentAnalysisRequest or batch).
/// </summary>
/// <remarks>
/// <para>Follows ADR-008: Use endpoint filters for resource-level authorization.
/// Uses IAiAuthorizationService for FullUAC authorization via RetrievePrincipalAccess.</para>
/// <para><b>The chat-context evaluation</b> (unified-access-control-r2 task 164; owner rounds 9 and 16): on the five
/// routes that bind a chat request DTO — <c>POST /api/ai/chat/sessions</c> (<see cref="ChatCreateSessionRequest"/>),
/// <c>PATCH /api/ai/chat/sessions/{sessionId}/context</c> (<see cref="ChatSwitchContextRequest"/>),
/// <c>POST /api/ai/chat/sessions/{sessionId}/messages</c> (<see cref="ChatSendMessageRequest"/>),
/// <c>POST /api/ai/chat/sessions/{sessionId}/dispatch</c> (<see cref="DispatchSessionRequest"/>) and
/// <c>POST /api/agent/message</c> (<see cref="AgentMessageRequest"/>) — every record the session is, or is about to be,
/// pointed at is authorized AS THE CALLER before the handler runs, because the turn reads it app-only afterwards (the
/// document summary, the playbook definition, record memory and pinned context). The ids come from the BODY (create,
/// PATCH, the per-turn document) and from the STORED session (every turn re-authorizes what the session carries, so a
/// pre-fix session and a revoked caller are both refused).</para>
/// <para><b>By kind, no new map</b> (owner round 16 item 2): a host of a type the existing allow-list resolves (matter,
/// project, work assignment, invoice) → Read on that record; a <c>sprk_document</c> host → document Read; a
/// <c>sprk_analysis</c> host or the <c>sprk_analysisoutput</c> sentinel → task 162's analysis-read rule
/// (<see cref="AnalysisAuthorizationFilter.ResolveAnalysisReadTargetsAsync"/>: Read on every anchor of the analysis, never
/// a row Read on the analysis itself — owner round 15 item 4); a host of ANY other type is DROPPED from the session (never kept unchecked); a document id that is a GUID → document
/// Read (<see cref="IAiAuthorizationService"/>); a stored document id that is an SPE drive-item id (a Compose Path B
/// session) → the caller's own SPE read of that item, OBO (<see cref="ISpeFileOperations"/>); a playbook → the
/// playbook-use decision (<see cref="PlaybookAuthorizationFilter.IsPlaybookUseAllowedForCallerAsync"/>).</para>
/// <para>Validation answers 400 before any rights query; every denial — an id that does not exist, one the caller may not
/// use, a seam fault, a missing token — is ONE 403 (<see cref="ChatContextDenied"/>) that names no id.</para>
/// </remarks>
public class AiAuthorizationFilter : IEndpointFilter
{
    private readonly IAiAuthorizationService _authorizationService;
    private readonly ILogger<AiAuthorizationFilter>? _logger;

    /// <summary>The host-context type of a document host (a chat embedded on a document).</summary>
    public const string DocumentHostEntityType = "sprk_document";

    /// <summary>The host-context type of an analysis host (the Analysis form embed).</summary>
    public const string AnalysisHostEntityType = "sprk_analysis";

    /// <summary>The ONE detail of the chat family's uniform 403. It names no id.</summary>
    public const string ChatContextAccessDeniedDetail =
        "You do not have access to one or more of the records this conversation uses.";

    /// <summary>The 400 detail for a request document id that is not a document id (GUID).</summary>
    public const string InvalidDocumentIdDetail = "A document id must be a document record id (GUID).";

    /// <summary>The 400 detail for a host context of an authorizable type whose id is not a record id (GUID).</summary>
    public const string InvalidHostEntityIdDetail = "HostContext.EntityId must be a record id (GUID).";

    private const string HostContextDroppedItemKey = "Sprk.AiAuthorizationFilter.HostContextDropped";

    /// <summary>The chat request DTOs the chat-context evaluation is keyed on (nothing else triggers it).</summary>
    private static readonly Type[] ChatContextRequestTypes =
    [
        typeof(ChatCreateSessionRequest),
        typeof(ChatSwitchContextRequest),
        typeof(ChatSendMessageRequest),
        typeof(DispatchSessionRequest),
        typeof(AgentMessageRequest),
    ];

    public AiAuthorizationFilter(IAiAuthorizationService authorizationService, ILogger<AiAuthorizationFilter>? logger = null)
    {
        _authorizationService = authorizationService ?? throw new ArgumentNullException(nameof(authorizationService));
        _logger = logger;
    }

    /// <summary>
    /// True when the chat-context evaluation DROPPED the request's host context on this request (an unauthorizable host
    /// type): the create / PATCH handler then stores no host context. Owner round 16 item 2.
    /// </summary>
    public static bool IsHostContextDropped(HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        return httpContext.Items.TryGetValue(HostContextDroppedItemKey, out var value) && value is true;
    }

    /// <summary>
    /// The chat family's uniform 403 (status, title, detail, reasonCode): identical for an id that does not exist, an id
    /// the caller may not use, a seam fault and a missing token; no detail or extension carries an id.
    /// </summary>
    public static IResult ChatContextDenied(HttpContext httpContext) =>
        ProblemDetailsHelper.Forbidden(
            FinanceAuthorizationFilter.InsufficientRightsReasonCode,
            ChatContextAccessDeniedDetail,
            httpContext.TraceIdentifier);

    /// <summary>The handler parameter (index, declared type) that is a chat request DTO, or null — used by the filter factory.</summary>
    internal static (int Index, Type Type)? FindChatContextParameter(MethodInfo method)
    {
        var parameters = method.GetParameters();
        for (var i = 0; i < parameters.Length; i++)
        {
            if (ChatContextRequestTypes.Contains(parameters[i].ParameterType))
            {
                return (i, parameters[i].ParameterType);
            }
        }

        return null;
    }

    /// <summary>The argument (index, runtime type) that is a chat request DTO, or null — for a directly invoked filter.</summary>
    private static (int Index, Type Type)? FindChatContextArgument(IList<object?> arguments)
    {
        for (var i = 0; i < arguments.Count; i++)
        {
            if (arguments[i] is { } argument && ChatContextRequestTypes.Contains(argument.GetType()))
            {
                return (i, argument.GetType());
            }
        }

        return null;
    }

    public ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next) =>
        InvokeAsync(context, next, FindChatContextArgument(context.Arguments));

    internal async ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext context, EndpointFilterDelegate next, (int Index, Type Type)? chatParameter)
    {
        var httpContext = context.HttpContext;
        var user = httpContext.User;

        // Extract Azure AD Object ID from claims.
        // DataverseAccessDataSource requires the 'oid' claim to lookup user in Dataverse.
        // Fallback chain matches other authorization filters in the codebase.
        var userId = CallerResolution.ResolveObjectId(user);

        if (string.IsNullOrEmpty(userId))
        {
            return Results.Problem(
                statusCode: 401,
                title: "Unauthorized",
                detail: "User identity not found",
                type: "https://tools.ietf.org/html/rfc7235#section-3.1");
        }

        if (chatParameter is { } chat)
        {
            return await AuthorizeChatContextAsync(context, next, userId, chat.Index, chat.Type);
        }

        // Extract document IDs from request arguments. If none are present (e.g. session-scoped
        // endpoints where the session id is the authorization scope), pass through to the next
        // filter.
        //
        // This comment used to end "— the endpoint handler performs its own tenant/session
        // ownership checks." That was false for two years and is the reason nobody added one: the
        // handlers checked TENANT and nothing else, so within a tenant any user could read, rename,
        // post into or delete any session (issue #863). Session ownership is now enforced by
        // AddSessionOwnershipFilter on every {sessionId} route, NOT by the handlers and NOT here.
        // If you are reading this because you are adding a session-scoped route, attach that filter
        // — SessionOwnershipGuardTests will fail the build if you forget.
        var documentIds = ExtractDocumentIds(context);
        if (documentIds.Count == 0)
        {
            return await next(context);
        }

        try
        {
            var result = await _authorizationService.AuthorizeAsync(
                user,
                documentIds,
                httpContext,
                httpContext.RequestAborted);

            if (!result.Success)
            {
                _logger?.LogWarning(
                    "[AI-AUTH-FILTER] Document access DENIED: DocumentCount={Count}, Reason={Reason}",
                    documentIds.Count,
                    result.Reason);

                return Results.Problem(
                    statusCode: 403,
                    title: "Forbidden",
                    detail: result.Reason ?? "Access denied to one or more documents",
                    type: "https://tools.ietf.org/html/rfc7231#section-6.5.3");
            }

            _logger?.LogDebug(
                "[AI-AUTH-FILTER] Document access GRANTED: DocumentCount={Count}",
                documentIds.Count);

            return await next(context);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "[AI-AUTH-FILTER] Authorization check failed with exception");
            return Results.Problem(
                statusCode: 500,
                title: "Internal Server Error",
                detail: "Authorization check failed",
                type: "https://tools.ietf.org/html/rfc7231#section-6.6.1");
        }
    }

    /// <summary>
    /// Extract document IDs from request arguments.
    /// Supports both single DocumentAnalysisRequest and batch IEnumerable&lt;DocumentAnalysisRequest&gt;.
    /// </summary>
    private static List<Guid> ExtractDocumentIds(EndpointFilterInvocationContext context)
    {
        var documentIds = new List<Guid>();

        foreach (var argument in context.Arguments)
        {
            switch (argument)
            {
                case DocumentAnalysisRequest request:
                    documentIds.Add(request.DocumentId);
                    break;

                case IEnumerable<DocumentAnalysisRequest> requests:
                    documentIds.AddRange(requests.Select(r => r.DocumentId));
                    break;

                case Guid documentId when documentId != Guid.Empty:
                    documentIds.Add(documentId);
                    break;
            }
        }

        return documentIds.Distinct().ToList();
    }

    // =====================================================================================================
    // The chat-context evaluation (task 164)
    // =====================================================================================================

    /// <summary>How a host context is authorized (owner round 16 item 2).</summary>
    private enum HostKind
    {
        /// <summary>No host context.</summary>
        None,

        /// <summary>A record of an authorizable entity set: Read as the caller.</summary>
        Record,

        /// <summary>A <c>sprk_document</c> host: document Read as the caller.</summary>
        Document,

        /// <summary>A <c>sprk_analysis</c> host or the <c>sprk_analysisoutput</c> sentinel: the analysis-read rule.</summary>
        Analysis,

        /// <summary>A type outside the authorizable kinds: the host context is dropped from the session.</summary>
        Unsupported,

        /// <summary>An authorizable type whose id is not a GUID.</summary>
        Invalid,
    }

    /// <summary>The checks one chat request needs, collected before any of them runs.</summary>
    private sealed class ChatContextChecks
    {
        public HashSet<Guid> Documents { get; } = [];

        public List<(string EntitySet, Guid RecordId, string Source)> Records { get; } = [];

        public List<(string DriveId, string ItemId)> SpeItems { get; } = [];

        public HashSet<Guid> Analyses { get; } = [];

        public HashSet<Guid> Playbooks { get; } = [];

        /// <summary>A stored id that cannot be decided (no drive for an SPE item, a malformed stored host): deny.</summary>
        public bool Undecidable { get; set; }

        /// <summary>The request's host context is dropped (create / PATCH).</summary>
        public bool DropRequestHost { get; set; }

        /// <summary>The stored session's host context is dropped (turn routes).</summary>
        public ChatSession? DropStoredHostOf { get; set; }
    }

    /// <summary>
    /// Classifies a host context by kind. The allow-list is the existing
    /// <see cref="SemanticSearchAuthorizationFilter.TryResolveAuthorizableEntitySet"/> (no new map); the document and
    /// analysis kinds are the two named types above plus the analysis sentinel.
    /// </summary>
    private static (HostKind Kind, string? EntitySet, Guid RecordId) Classify(ChatHostContext? host)
    {
        if (host is null)
        {
            return (HostKind.None, null, Guid.Empty);
        }

        var entityType = host.EntityType?.Trim();
        if (string.IsNullOrEmpty(entityType))
        {
            return (HostKind.Unsupported, null, Guid.Empty);
        }

        HostKind kind;
        string? entitySet = null;
        if (SemanticSearchAuthorizationFilter.TryResolveAuthorizableEntitySet(entityType, out var resolved))
        {
            kind = HostKind.Record;
            entitySet = resolved;
        }
        else if (string.Equals(entityType, DocumentHostEntityType, StringComparison.OrdinalIgnoreCase))
        {
            kind = HostKind.Document;
            entitySet = FinanceAuthorizationFilter.DocumentEntitySet;
        }
        else if (string.Equals(entityType, AnalysisHostEntityType, StringComparison.OrdinalIgnoreCase)
                 || string.Equals(entityType, ChatSessionManager.AnalysisHostContextEntityType, StringComparison.OrdinalIgnoreCase))
        {
            kind = HostKind.Analysis;
        }
        else
        {
            return (HostKind.Unsupported, null, Guid.Empty);
        }

        return Guid.TryParse(host.EntityId?.Trim(), out var id) && id != Guid.Empty
            ? (kind, entitySet, id)
            : (HostKind.Invalid, entitySet, Guid.Empty);
    }

    /// <summary>
    /// Collects the checks for one chat request, answers the 400s (before any rights query), runs every check as the
    /// caller, and either denies with <see cref="ChatContextDenied"/> or applies the host-context drop and continues.
    /// </summary>
    private async ValueTask<object?> AuthorizeChatContextAsync(
        EndpointFilterInvocationContext context, EndpointFilterDelegate next, string userId, int index, Type dtoType)
    {
        var httpContext = context.HttpContext;
        var body = index < context.Arguments.Count ? context.Arguments[index] : null;
        var checks = new ChatContextChecks();

        IResult? rejection;
        ChatSession? storedSession = null;
        try
        {
            switch (body)
            {
                case ChatCreateSessionRequest create:
                    rejection = AddRequestDocument(create.DocumentId, checks)
                        ?? AddRequestHost(create.HostContext, checks)
                        ?? AddPlaybook(create.PlaybookId, checks);
                    break;

                case ChatSwitchContextRequest switchContext:
                    rejection = switchContext.AdditionalDocumentIds is { Count: > ChatKnowledgeScope.MaxAdditionalDocuments }
                        ? BadRequest($"AdditionalDocumentIds cannot exceed {ChatKnowledgeScope.MaxAdditionalDocuments} entries.")
                        : AddRequestDocument(switchContext.DocumentId, checks)
                          ?? AddRequestDocuments(switchContext.AdditionalDocumentIds, checks)
                          ?? AddRequestHost(switchContext.HostContext, checks)
                          ?? AddPlaybook(switchContext.PlaybookId, checks);
                    break;

                case ChatSendMessageRequest send:
                    rejection = AddRequestDocument(send.DocumentId, checks);
                    if (rejection is null)
                    {
                        storedSession = await ReadRouteSessionAsync(httpContext);
                        if (storedSession is null)
                        {
                            return Deny(httpContext, userId, "the session could not be read");
                        }

                        AddStoredContext(storedSession, checks, includeDocument: string.IsNullOrWhiteSpace(send.DocumentId), includePlaybook: true);
                    }

                    break;

                case DispatchSessionRequest:
                    storedSession = await ReadRouteSessionAsync(httpContext);
                    if (storedSession is null)
                    {
                        return Deny(httpContext, userId, "the session could not be read");
                    }

                    // The dispatch path reads the host record's memory and pinned context; the playbook is the Binding's.
                    AddStoredContext(storedSession, checks, includeDocument: true, includePlaybook: false);
                    rejection = null;
                    break;

                case AgentMessageRequest agent:
                    if (agent.DocumentId is { } agentDocument && agentDocument != Guid.Empty)
                    {
                        checks.Documents.Add(agentDocument);
                    }

                    storedSession = await ReadResumedAgentSessionAsync(httpContext, agent.ConversationReference, userId);
                    if (storedSession is not null)
                    {
                        AddStoredContext(
                            storedSession, checks,
                            includeDocument: agent.DocumentId is not { } supplied || supplied == Guid.Empty,
                            includePlaybook: true);
                    }

                    rejection = null;
                    break;

                case null when dtoType == typeof(DispatchSessionRequest):
                    // The handler's own 400 (same errorCode and detail), answered here so a null body is never passed
                    // through unchecked.
                    return Results.Problem(
                        statusCode: StatusCodes.Status400BadRequest,
                        title: "Bad Request",
                        detail: DispatchSessionEndpoint.BindingRequiredDetail,
                        type: "https://tools.ietf.org/html/rfc7231#section-6.5.1",
                        extensions: new Dictionary<string, object?>
                        {
                            ["errorCode"] = DispatchSessionEndpoint.ErrorCodeBindingRequired,
                            ["correlationId"] = httpContext.TraceIdentifier,
                        });

                default:
                    // A chat route whose body this filter cannot read: never a pass-through (ADR-003).
                    return Deny(httpContext, userId, "the chat request body could not be read");
            }
        }
        catch (OperationCanceledException) when (httpContext.RequestAborted.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "[AI-AUTH-FILTER] Chat-context collection faulted for caller {UserId}; denying (fail closed)", userId);
            return ChatContextDenied(httpContext);
        }

        if (rejection is not null)
        {
            return rejection;
        }

        if (!await AreChatContextChecksAllowedAsync(httpContext, checks, userId))
        {
            return ChatContextDenied(httpContext);
        }

        // Every check passed. Apply the drops of unauthorizable host types (owner round 16 item 2).
        if (checks.DropRequestHost)
        {
            httpContext.Items[HostContextDroppedItemKey] = true;
        }

        if (checks.DropStoredHostOf is { } sessionToClean)
        {
            try
            {
                // The turn routes re-read the session (and the dispatch orchestrator reads it itself), so the drop is
                // persisted: from this turn on the session carries no host that was never checked.
                var sessionManager = httpContext.RequestServices.GetRequiredService<ChatSessionManager>();
                await sessionManager.UpdateSessionCacheAsync(sessionToClean with { HostContext = null }, httpContext.RequestAborted);
                _logger?.LogInformation(
                    "[AI-AUTH-FILTER] Session {SessionId}: host context of an unauthorizable type dropped", sessionToClean.SessionId);
            }
            catch (OperationCanceledException) when (httpContext.RequestAborted.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "[AI-AUTH-FILTER] Dropping an unauthorizable host context faulted; denying (fail closed)");
                return ChatContextDenied(httpContext);
            }
        }

        return await next(context);
    }

    private IResult Deny(HttpContext httpContext, string userId, string why)
    {
        _logger?.LogWarning("[AI-AUTH-FILTER] Chat-context DENIED for caller {UserId}: {Why}", userId, why);
        return ChatContextDenied(httpContext);
    }

    private static IResult BadRequest(string detail) =>
        Results.Problem(
            statusCode: StatusCodes.Status400BadRequest,
            title: "Bad Request",
            detail: detail,
            type: "https://tools.ietf.org/html/rfc7231#section-6.5.1");

    /// <summary>A BODY document id: empty → absent; a GUID → document Read; anything else → 400.</summary>
    private static IResult? AddRequestDocument(string? documentId, ChatContextChecks checks)
    {
        if (string.IsNullOrWhiteSpace(documentId))
        {
            return null;
        }

        if (!Guid.TryParse(documentId.Trim(), out var id) || id == Guid.Empty)
        {
            return BadRequest(InvalidDocumentIdDetail);
        }

        checks.Documents.Add(id);
        return null;
    }

    private static IResult? AddRequestDocuments(IReadOnlyList<string>? documentIds, ChatContextChecks checks)
    {
        if (documentIds is null)
        {
            return null;
        }

        foreach (var documentId in documentIds)
        {
            var rejection = AddRequestDocument(documentId, checks);
            if (rejection is not null)
            {
                return rejection;
            }
        }

        return null;
    }

    /// <summary>A BODY host context: Record / Document / Analysis → its check; Unsupported → dropped; Invalid → 400.</summary>
    private static IResult? AddRequestHost(ChatHostContext? host, ChatContextChecks checks)
    {
        var (kind, entitySet, recordId) = Classify(host);
        switch (kind)
        {
            case HostKind.Record:
                checks.Records.Add((entitySet!, recordId, "body.hostContext"));
                return null;
            case HostKind.Document:
                checks.Documents.Add(recordId);
                return null;
            case HostKind.Analysis:
                checks.Analyses.Add(recordId);
                return null;
            case HostKind.Unsupported:
                checks.DropRequestHost = true;
                return null;
            case HostKind.Invalid:
                return BadRequest(InvalidHostEntityIdDetail);
            default:
                return null;
        }
    }

    private static IResult? AddPlaybook(Guid? playbookId, ChatContextChecks checks)
    {
        if (playbookId is { } id && id != Guid.Empty)
        {
            checks.Playbooks.Add(id);
        }

        return null;
    }

    /// <summary>
    /// The STORED session context a turn will use: the document (a GUID → document Read; an SPE drive-item id → the
    /// caller's SPE read, which needs the recorded drive), every additional document, the host (an unsupported type is
    /// dropped and the drop persisted; a malformed one cannot be decided) and the playbook.
    /// </summary>
    private static void AddStoredContext(ChatSession session, ChatContextChecks checks, bool includeDocument, bool includePlaybook)
    {
        if (includeDocument)
        {
            AddStoredDocument(session.DocumentId, session.DocumentDriveId, checks);
        }

        foreach (var additional in session.AdditionalDocumentIds ?? [])
        {
            // An additional document is always a sprk_document id; a non-GUID one cannot be decided.
            AddStoredDocument(additional, driveId: null, checks);
        }

        var (kind, entitySet, recordId) = Classify(session.HostContext);
        switch (kind)
        {
            case HostKind.Record:
                checks.Records.Add((entitySet!, recordId, "session.hostContext"));
                break;
            case HostKind.Document:
                checks.Documents.Add(recordId);
                break;
            case HostKind.Analysis:
                checks.Analyses.Add(recordId);
                break;
            case HostKind.Unsupported:
                checks.DropStoredHostOf = session;
                break;
            case HostKind.Invalid:
                checks.Undecidable = true;
                break;
        }

        if (includePlaybook && session.PlaybookId is { } playbookId && playbookId != Guid.Empty)
        {
            checks.Playbooks.Add(playbookId);
        }
    }

    private static void AddStoredDocument(string? documentId, string? driveId, ChatContextChecks checks)
    {
        if (string.IsNullOrWhiteSpace(documentId))
        {
            return;
        }

        if (Guid.TryParse(documentId.Trim(), out var id))
        {
            // An all-zero id names no record (every read of it finds nothing), so there is nothing to authorize.
            if (id != Guid.Empty)
            {
                checks.Documents.Add(id);
            }

            return;
        }

        if (!string.IsNullOrWhiteSpace(driveId))
        {
            checks.SpeItems.Add((driveId.Trim(), documentId.Trim()));
            return;
        }

        checks.Undecidable = true;
    }

    /// <summary>The session named by the route's <c>sessionId</c>: the one SessionOwnershipFilter read, else a fresh read.</summary>
    private static async Task<ChatSession?> ReadRouteSessionAsync(HttpContext httpContext)
    {
        if (httpContext.Items.TryGetValue(SessionOwnershipFilterExtensions.OwnedSessionItemKey, out var shared)
            && shared is ChatSession ownedSession)
        {
            return ownedSession;
        }

        var tenantId = TenantResolution.ResolveTenantId(httpContext.User);
        var sessionId = httpContext.Request.RouteValues.TryGetValue("sessionId", out var raw) ? raw as string : null;
        if (string.IsNullOrEmpty(tenantId) || string.IsNullOrWhiteSpace(sessionId))
        {
            return null;
        }

        var sessionManager = httpContext.RequestServices.GetRequiredService<ChatSessionManager>();
        return await sessionManager.GetSessionAsync(tenantId, sessionId, httpContext.RequestAborted);
    }

    /// <summary>
    /// The session an agent turn RESUMES: the one the conversation reference names, when the caller owns it (the handler
    /// resumes only then; any other reference mints a fresh session that carries only the body document).
    /// </summary>
    private static async Task<ChatSession?> ReadResumedAgentSessionAsync(HttpContext httpContext, string? conversationReference, string callerOid)
    {
        var tenantId = TenantResolution.ResolveTenantId(httpContext.User);
        if (string.IsNullOrWhiteSpace(conversationReference) || string.IsNullOrEmpty(tenantId))
        {
            return null;
        }

        var sessionManager = httpContext.RequestServices.GetRequiredService<ChatSessionManager>();
        var session = await sessionManager.GetSessionAsync(tenantId, conversationReference, httpContext.RequestAborted);
        return session is not null && string.Equals(session.OwnerOid, callerOid, StringComparison.Ordinal) ? session : null;
    }

    /// <summary>
    /// Runs every collected check as the caller, through the cached seams: documents via
    /// <see cref="IAiAuthorizationService"/>, records via <see cref="AuthorizationService.GetCallerRecordAccessAsync"/>
    /// (Read), analysis hosts via the analysis-read rule (<see cref="IsAnalysisReadableAsync"/>), SPE items via the
    /// caller's own OBO read (<see cref="ISpeFileOperations.GetFileMetadataAsUserAsync"/>), playbooks via the playbook-use
    /// decision. Any denial, an undecidable id, a missing service or token, or a fault
    /// answers false.
    /// </summary>
    private async Task<bool> AreChatContextChecksAllowedAsync(HttpContext httpContext, ChatContextChecks checks, string userId)
    {
        var ct = httpContext.RequestAborted;
        try
        {
            if (checks.Undecidable)
            {
                _logger?.LogWarning("[AI-AUTH-FILTER] Chat-context DENIED for caller {UserId}: a stored id cannot be decided", userId);
                return false;
            }

            if (checks.Documents.Count > 0)
            {
                var documents = await _authorizationService.AuthorizeAsync(
                    httpContext.User, checks.Documents.ToList(), httpContext, ct);
                if (!documents.Success)
                {
                    _logger?.LogWarning(
                        "[AI-AUTH-FILTER] Chat-context DENIED for caller {UserId}: {Count} document(s) not readable",
                        userId, checks.Documents.Count);
                    return false;
                }
            }

            if (checks.Records.Count > 0 || checks.Analyses.Count > 0)
            {
                var authorizationService = httpContext.RequestServices.GetService<AuthorizationService>();
                if (authorizationService is null)
                {
                    _logger?.LogWarning("[AI-AUTH-FILTER] Chat-context DENIED for caller {UserId}: no record-rights service", userId);
                    return false;
                }

                var token = TokenHelper.ExtractBearerTokenOrNull(httpContext);
                foreach (var (entitySet, recordId, source) in checks.Records)
                {
                    var snapshot = await authorizationService.GetCallerRecordAccessAsync(userId, entitySet, recordId, token, ct);
                    if (!OperationAccessPolicy.HasRequiredRights(snapshot.AccessRights, "read"))
                    {
                        _logger?.LogWarning(
                            "[AI-AUTH-FILTER] Chat-context DENIED for caller {UserId}: no Read on {EntitySet}({RecordId}) [{Source}]",
                            userId, entitySet, recordId, source);
                        return false;
                    }
                }

                foreach (var analysisId in checks.Analyses)
                {
                    if (!await IsAnalysisReadableAsync(httpContext, authorizationService, analysisId, userId, token, ct))
                    {
                        _logger?.LogWarning(
                            "[AI-AUTH-FILTER] Chat-context DENIED for caller {UserId}: analysis host {AnalysisId} not readable", userId, analysisId);
                        return false;
                    }
                }
            }

            if (checks.SpeItems.Count > 0)
            {
                var files = httpContext.RequestServices.GetService<ISpeFileOperations>();
                if (files is null)
                {
                    _logger?.LogWarning("[AI-AUTH-FILTER] Chat-context DENIED for caller {UserId}: no SPE file service", userId);
                    return false;
                }

                foreach (var (driveId, itemId) in checks.SpeItems)
                {
                    // The caller's OWN read of the item (OBO): a null (not found / no access) or a fault denies.
                    if (await files.GetFileMetadataAsUserAsync(httpContext, driveId, itemId, ct) is null)
                    {
                        _logger?.LogWarning(
                            "[AI-AUTH-FILTER] Chat-context DENIED for caller {UserId}: SPE item not readable as the caller", userId);
                        return false;
                    }
                }
            }

            foreach (var playbookId in checks.Playbooks)
            {
                if (!await PlaybookAuthorizationFilter.IsPlaybookUseAllowedForCallerAsync(httpContext, playbookId, "chat.playbookId"))
                {
                    _logger?.LogWarning(
                        "[AI-AUTH-FILTER] Chat-context DENIED for caller {UserId}: playbook {PlaybookId} not usable", userId, playbookId);
                    return false;
                }
            }

            return true;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "[AI-AUTH-FILTER] Chat-context decision faulted for caller {UserId}; denying (fail closed)", userId);
            return false;
        }
    }

    /// <summary>
    /// The analysis host, decided by task 162's analysis-read rule and evaluated by task 162 f1's ONE shared evaluation
    /// (<see cref="AnalysisAuthorizationFilter.IsAnalysisReadableAsync"/>): the same per-route evaluator on every path the
    /// rule emits — Document, Record and the PERSONAL creator's Privilege path (owner round 15 item 4) — so a chat on an
    /// analysis and <c>GET /api/ai/analysis/{id}</c> can never disagree (owner round 25 item 4). Any rejection, no check,
    /// or fault is a denial. Integrated at the sweep merge per task 162 note §14.3; the unused parameters stay so the
    /// caller is unchanged.
    /// </summary>
    private async Task<bool> IsAnalysisReadableAsync(
        HttpContext httpContext, AuthorizationService authorizationService, Guid analysisId, string userId, string? token, CancellationToken ct)
        => await AnalysisAuthorizationFilter.IsAnalysisReadableAsync(httpContext, analysisId, _logger);
}
