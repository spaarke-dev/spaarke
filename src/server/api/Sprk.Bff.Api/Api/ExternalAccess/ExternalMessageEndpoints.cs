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
    /// Registers the message read on the shared external collaboration group. The group's policy and
    /// CallerPrincipalAuthorizationFilter are inherited; this adds the route only.
    /// </summary>
    public static RouteGroupBuilder MapExternalMessageEndpoints(this RouteGroupBuilder externalGroup)
    {
        // GET /api/v1/external/{root}/{id}/messages — the record's thread, newest last.
        externalGroup.MapGet("/{root}/{id:guid}/messages", GetMessagesAsync)
            .WithName("GetExternalRecordMessages")
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

        return externalGroup;
    }

    private static Task<IResult> GetMessagesAsync(
        string root,
        Guid id,
        HttpContext httpContext,
        ExternalModuleRegistry registry,
        RecordService recordService,
        CommunicationThreadReadService threadReadService,
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
            root, id, principal, registry, recordService.GetRecordAsync, threadReadService, logger, ct);
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
        ILogger logger,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(root) || !Roots.TryGetValue(root, out var messageRoot))
        {
            return Results.Problem(
                statusCode: StatusCodes.Status404NotFound, title: "Not Found",
                detail: "Messages are not available for this kind of record.",
                extensions: new Dictionary<string, object?> { ["errorCode"] = ErrorRootNotSupported });
        }

        if (!await IsRootRecordReadableAsync(messageRoot, id, principal, registry, readRecord, logger, ct)
                .ConfigureAwait(false))
        {
            logger.LogWarning(
                "[EXT-MESSAGES] DENY {Entity} {RecordId} for contact {ContactId} / systemuser {SystemUserId}: not in the " +
                "caller's accessible set (or absent).",
                messageRoot.EntityLogicalName, id, principal.ContactId, principal.SystemUserId);
            return ProblemDetailsHelper.Forbidden(DenyRecordNotAccessible);
        }

        try
        {
            // Internal-only reaches a SYSTEMUSER principal only (the read service also asks IsExternalAsync). A contact —
            // CIAM or workforce — passes null, so the shared filter hides every internal-only row from it.
            var read = await threadReadService
                .ReadRegardingAppOnlyAsync(messageRoot.EntityLogicalName, id, principal.SystemUserId, ct)
                .ConfigureAwait(false);

            return Results.Ok(new ExternalCollectionResponse<ExternalMessageDto>
            {
                Value = read.Messages.Select(ToExternalDto).ToList(),
                Truncated = read.Truncated,
            });
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[EXT-MESSAGES] Message read failed for {Entity} {RecordId}.",
                messageRoot.EntityLogicalName, id);
            return Results.Problem(
                statusCode: StatusCodes.Status500InternalServerError, title: "Internal Server Error",
                detail: "An unexpected error occurred reading the messages.",
                extensions: new Dictionary<string, object?> { ["errorCode"] = "EXT_MESSAGES_INTERNAL_ERROR" });
        }
    }

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
    /// </remarks>
    internal static async Task<bool> IsRootRecordReadableAsync(
        MessageRoot root,
        Guid id,
        CallerPrincipal principal,
        ExternalModuleRegistry registry,
        Func<string, Guid, string[]?, CancellationToken, Task<IReadOnlyDictionary<string, object?>>> readRecord,
        ILogger logger,
        CancellationToken ct)
    {
        if (id == Guid.Empty)
        {
            return false;
        }

        var module = registry.FindByEntity(root.EntityLogicalName);
        if (module is null)
        {
            logger.LogWarning("[EXT-MESSAGES] No external module is registered for {Entity}; refusing.", root.EntityLogicalName);
            return false;
        }

        var dimensions = module.EffectiveDimensions;
        var primaryIdAttribute = root.EntityLogicalName + "id";
        if (dimensions.All(d => string.Equals(d.Attribute, primaryIdAttribute, StringComparison.OrdinalIgnoreCase)))
        {
            return module.IsRecordAccessible(principal, id);
        }

        if (dimensions.All(d => d.AccessibleIds(principal).Count == 0))
        {
            return false;
        }

        IReadOnlyDictionary<string, object?> record;
        try
        {
            record = await readRecord(
                    root.EntityLogicalName, id, dimensions.Select(d => d.Attribute).Distinct().ToArray(), ct)
                .ConfigureAwait(false);
        }
        catch (RecordNotFoundException)
        {
            return false;
        }

        var row = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["@logicalName"] = root.EntityLogicalName,
        };
        foreach (var dimension in dimensions)
        {
            if (record.TryGetValue(dimension.Attribute, out var value) && LookupId(value) is { } parentId)
            {
                row[dimension.Attribute] = parentId;
            }
        }

        return module.ScopeRows(principal, new[] { row }).Count > 0;
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

    private static ExternalMessageDto ToExternalDto(ThreadMessageDto message) => new()
    {
        SprkCommunicationid = message.MessageId.ToString("D"),
        SprkSubject = message.Subject,
        SprkBody = message.Body,
        SprkBodyformat = message.BodyFormat,
        SprkDirection = message.Direction,
        SenderName = message.SentByName,
        Createdon = message.CreatedOn,
    };
}
