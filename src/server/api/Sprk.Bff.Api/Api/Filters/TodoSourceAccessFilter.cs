using Spaarke.Core.Auth;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.Auth;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Models.Office;
using Sprk.Bff.Api.Services.Office;

namespace Sprk.Bff.Api.Api.Filters;

/// <summary>
/// Extension methods for attaching <see cref="TodoSourceAccessFilter"/>.
/// </summary>
public static class TodoSourceAccessFilterExtensions
{
    /// <summary>
    /// Authorize the CALLER's Read right on every caller-supplied record id carried by a
    /// <see cref="CreateTodoRequest"/> before the handler runs. Apply after <c>AddOfficeAuthFilter()</c>.
    /// </summary>
    public static TBuilder AddTodoSourceAccessFilter<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder
    {
        return builder.AddEndpointFilter(async (context, next) =>
        {
            var services = context.HttpContext.RequestServices;
            var filter = new TodoSourceAccessFilter(
                services.GetRequiredService<CallerRecordAccessProbe>(),
                services.GetService<ILogger<TodoSourceAccessFilter>>());
            return await filter.InvokeAsync(context, next);
        });
    }
}

/// <summary>
/// spaarkeai-word-add-in-r1 task 064 — the per-resource authorization decision for
/// <c>POST /office/todo</c>'s four caller-supplied record ids (Fable review finding <b>F3</b>).
/// </summary>
/// <remarks>
/// <para><b>Why it exists.</b> <c>OfficeService.CreateTodoAsync</c> writes
/// <see cref="CreateTodoRequest.RegardingRecordId"/>, <see cref="CreateTodoRequest.DocumentId"/>,
/// <see cref="CreateTodoRequest.CommunicationId"/> and <see cref="CreateTodoRequest.AssignedToContactId"/>
/// onto a <c>sprk_todo</c> the CALLER owns, and <c>CoreAncestorResolver</c> reads the regarding target
/// <b>app-only</b> to stamp its parent matter/project onto that same row. Before this filter, any
/// authenticated caller could (a) hang a To Do they author onto records they cannot read — which FR-26
/// inheritance then surfaces to those records' members — and (b) harvest the target's parent core-record id
/// by reading their own new row back through <c>Xrm.WebApi</c>.</para>
///
/// <para><b>The oracle is the other half, and it is why the deny shape is a single constant.</b> The
/// 403-vs-201 split was itself the leak: a child-class regarding whose app-only read found no row failed
/// closed to 403, while one that found a row returned 201, so a caller learned whether an arbitrary
/// invoice / document / communication GUID existed. This filter closes it by construction rather than by
/// convention — <see cref="CallerRecordAccessProbe"/> collapses "no such record", "you may not see it",
/// "the type is not authorizable" and "the check could not run" all to
/// <see cref="AccessRights.None"/> (Dataverse reports the first two identically under OBO, by design), and
/// every one of those paths returns the SAME status, title, detail, <c>errorCode</c> and <c>reasonCode</c>
/// here. There is deliberately ONE <see cref="Deny"/> body and no per-reason branch: a reason code that
/// varied with the record would re-open the oracle one refactor later. The record id and type are logged,
/// never echoed.</para>
///
/// <para><b>Why 403 and not 404.</b> One answer has to cover both cases, and 403 is the one that asserts
/// nothing about existence — a 404 on a POST would additionally be ambiguous with "no such route", and the
/// task pane already treats this route's 403 as an ordinary create failure (see the pane note below), so no
/// client changes.</para>
///
/// <para><b>Timing.</b> Both halves of the pair travel the same path: a non-existent record and an
/// invisible one each make <c>RetrievePrincipalAccess</c> answer 404, which the probe retries on its
/// replication-lag schedule before denying. So they share a timing class as well as a body.</para>
///
/// <para><b>What is shared (CLAUDE.md §11).</b> The SAME <see cref="CallerRecordAccessProbe"/> (OBO
/// <c>RetrievePrincipalAccess</c>), the SAME <c>read</c> key in <see cref="OperationAccessPolicy"/>, the
/// SAME <c>OFFICE_009</c> code the task pane's error map keys on, and the SAME logical-name → entity-set
/// table via <see cref="EntityAccessFilter.TryResolveEntitySet"/>. The regarding types are read from
/// <see cref="OfficeService.TodoRegardingMap"/> — the very table that decides which ids get WRITTEN — so
/// the gated set cannot drift from the written set.</para>
///
/// <para><b>Why a sibling of <see cref="QuickCreateSourceAccessFilter"/> and not an extension of it.</b>
/// That filter reads a <c>QuickCreateRequest</c> and authorizes ONE id whose type the caller names as a
/// string. This route carries FOUR ids across three carrier shapes, two of whose types are fixed by the
/// request schema rather than named by the caller, and it needs a single indistinguishable deny body that
/// quick-create does not (quick-create has no existence oracle to close, so its three reason codes are
/// harmless there). Generalizing one filter over both request shapes would have produced a type-switch
/// whose only shared line is the probe call, which is already shared.</para>
///
/// <para><b>Residual.</b> Read is a RECORD-level check: an app-only read applies no column-level (field
/// level security) masking, so the core-ancestor stamp still copies a parent id the caller holds Read on
/// the child for. That is the intended FR-26 semantics, not a gap. Probes are sequential and fail fast, so
/// a caller can time WHICH of their own four ids was refused — they supplied all four, so this discloses
/// nothing they did not already know.</para>
/// </remarks>
public sealed class TodoSourceAccessFilter : IEndpointFilter
{
    /// <summary>The existing <see cref="OperationAccessPolicy"/> key for reading a record (<see cref="AccessRights.Read"/>).</summary>
    internal const string ReadOperation = "read";

    /// <summary>The Office error-code taxonomy's "access denied" code — the same one <see cref="EntityAccessFilter"/> emits.</summary>
    private const string AccessDeniedErrorCode = "OFFICE_009";

    /// <summary>
    /// The ONE reason code this filter emits, for every refusal. See the class remarks: a reason code that
    /// varied with the record would be the existence oracle rebuilt.
    /// </summary>
    private const string DeniedReasonCode = "source_record_inaccessible";

    /// <summary>
    /// The ONE detail sentence this filter emits. Names no record, no id and no type, and says the same
    /// thing whether the record is absent, invisible, of an unauthorizable type, or simply not readable.
    /// </summary>
    private const string DeniedDetail =
        "You do not have permission to create a To Do against one or more of the records it refers to, "
        + "or those records are not available. Open the record you want the To Do filed against and try "
        + "again, or ask its owner to share it with you.";

    /// <summary>
    /// The two carrier types <see cref="EntityAccessFilter.TryResolveEntitySet"/> deliberately does not
    /// carry, supplied here rather than added to that table.
    /// </summary>
    /// <remarks>
    /// That table does double duty: besides naming an entity's collection it is also
    /// <see cref="EntityAccessFilter"/>'s ALLOW-LIST of legal <c>/office/save</c> association targets (its
    /// own remarks refuse <c>sprk_todo</c> on exactly that ground — no <c>sprk_document</c> lookup column
    /// exists for it). Adding <c>sprk_document</c> / <c>sprk_communication</c> there to serve this route
    /// would silently widen which types a document may be filed against on a different route. Two entries
    /// consulted only after the shared table misses is the smaller cost; the shared table is still asked
    /// first, so the three regarding types this route shares with <c>/office/save</c> are resolved in
    /// exactly one place.
    /// </remarks>
    private static readonly IReadOnlyDictionary<string, string> CarrierEntitySets =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["sprk_document"] = "sprk_documents",
            ["sprk_communication"] = "sprk_communications",
        };

    private readonly CallerRecordAccessProbe _probe;
    private readonly ILogger<TodoSourceAccessFilter>? _logger;

    public TodoSourceAccessFilter(
        CallerRecordAccessProbe probe,
        ILogger<TodoSourceAccessFilter>? logger = null)
    {
        _probe = probe ?? throw new ArgumentNullException(nameof(probe));
        _logger = logger;
    }

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var httpContext = context.HttpContext;
        var request = context.Arguments.OfType<CreateTodoRequest>().FirstOrDefault();

        if (request is null)
        {
            // No body to read ids out of → the service will read nothing → nothing to authorize.
            return await next(context);
        }

        var sources = CollectSourceRecords(request);
        if (sources.Count == 0)
        {
            // A standalone To Do names no record. Same posture as QuickCreateSourceAccessFilter: where
            // nothing is read, there is nothing to authorize.
            return await next(context);
        }

        var callerToken = TokenHelper.ExtractBearerTokenOrNull(httpContext);

        foreach (var (logicalName, recordId) in sources)
        {
            // A MISS DENIES: a type whose per-record access this codebase cannot evaluate is a type whose
            // id it must not write onto a row the caller owns.
            if (!TryResolveSourceEntitySet(logicalName, out var entitySet))
            {
                _logger?.LogWarning(
                    "[TODO-SOURCE-AUTH] Denying: source entity type '{EntityType}' has no entity-set mapping. "
                    + "CorrelationId: {CorrelationId}",
                    logicalName, httpContext.TraceIdentifier);

                return Deny(httpContext);
            }

            AccessRights rights;
            try
            {
                rights = await _probe.GetCallerRightsAsync(
                    callerToken, entitySet, recordId, httpContext.RequestAborted);
            }
            catch (OperationCanceledException) when (httpContext.RequestAborted.IsCancellationRequested)
            {
                // A client abort is cancellation, not a denial.
                throw;
            }
            catch (Exception ex)
            {
                // Covers the AUTHORIZATION DECISION only — never next(), so downstream faults are not
                // relabelled as access denials.
                _logger?.LogError(ex,
                    "[TODO-SOURCE-AUTH] The caller-rights probe threw for {EntitySet}({RecordId}). Denying. "
                    + "CorrelationId: {CorrelationId}",
                    entitySet, recordId, httpContext.TraceIdentifier);

                return Deny(httpContext);
            }

            if (!OperationAccessPolicy.HasRequiredRights(rights, ReadOperation))
            {
                _logger?.LogWarning(
                    "[TODO-SOURCE-AUTH] Denied: caller cannot read {EntitySet}({RecordId}). Holds {Rights}. "
                    + "CorrelationId: {CorrelationId}",
                    entitySet, recordId, rights, httpContext.TraceIdentifier);

                return Deny(httpContext);
            }
        }

        return await next(context);
    }

    /// <summary>
    /// Every record id this request would cause to be written, paired with its target's logical name.
    /// </summary>
    /// <remarks>
    /// Mirrors <c>OfficeService.CreateTodoAsync</c> branch for branch, and reads the regarding types out of
    /// <see cref="OfficeService.TodoRegardingMap"/> so the two cannot diverge. An id the service would
    /// ignore (an unrecognized <see cref="CreateTodoRequest.RegardingEntityType"/>, an empty GUID) is
    /// deliberately absent: it is never written and never read, so there is nothing to authorize, and
    /// refusing it would turn a silently-ignored field into a 403.
    /// </remarks>
    private static List<(string LogicalName, Guid RecordId)> CollectSourceRecords(CreateTodoRequest request)
    {
        var sources = new List<(string, Guid)>(4);

        if (!string.IsNullOrWhiteSpace(request.RegardingEntityType)
            && request.RegardingRecordId is { } regardingId
            && regardingId != Guid.Empty
            // No Trim(), deliberately: OfficeService.CreateTodoAsync looks the type up un-trimmed, and the
            // two lookups have to be the SAME lookup for the shared table to be a forcing function. A
            // padded type name is written by neither and gated by neither.
            && OfficeService.TodoRegardingMap.TryGetValue(request.RegardingEntityType!, out var regarding))
        {
            sources.Add((regarding.LogicalName, regardingId));
        }

        if (request.DocumentId is { } documentId
            && documentId != Guid.Empty
            && OfficeService.TodoRegardingMap.TryGetValue("Document", out var documentCarrier))
        {
            sources.Add((documentCarrier.LogicalName, documentId));
        }

        if (request.CommunicationId is { } communicationId
            && communicationId != Guid.Empty
            && OfficeService.TodoRegardingMap.TryGetValue("Communication", out var communicationCarrier))
        {
            sources.Add((communicationCarrier.LogicalName, communicationId));
        }

        // sprk_assignedto is written as a bare EntityReference("contact", …) with no table behind it, so
        // the logical name is a literal here too — the same literal, in the only other place it appears.
        if (request.AssignedToContactId is { } contactId && contactId != Guid.Empty)
        {
            sources.Add(("contact", contactId));
        }

        return sources;
    }

    /// <summary>
    /// Logical name → Dataverse entity SET, asking the shared table first and the two-entry carrier
    /// supplement second. See <see cref="CarrierEntitySets"/> for why the supplement exists.
    /// </summary>
    private static bool TryResolveSourceEntitySet(string logicalName, out string entitySet)
    {
        if (EntityAccessFilter.TryResolveEntitySet(logicalName, out entitySet))
        {
            return true;
        }

        if (CarrierEntitySets.TryGetValue(logicalName, out var carrierEntitySet))
        {
            entitySet = carrierEntitySet;
            return true;
        }

        entitySet = string.Empty;
        return false;
    }

    /// <summary>
    /// The one refusal this filter can produce, in the Office ProblemDetails shape
    /// (<see cref="EntityAccessFilter"/>'s: errorCode + reasonCode + correlationId).
    /// </summary>
    /// <remarks>
    /// Takes no reason and no record: every caller-visible field except <c>correlationId</c> is a constant,
    /// which is what makes "denied" and "not found" indistinguishable rather than merely similar.
    /// </remarks>
    private static IResult Deny(HttpContext httpContext)
        => Results.Problem(
            statusCode: StatusCodes.Status403Forbidden,
            title: "Forbidden",
            detail: DeniedDetail,
            type: "https://tools.ietf.org/html/rfc7231#section-6.5.3",
            extensions: new Dictionary<string, object?>
            {
                ["errorCode"] = AccessDeniedErrorCode,
                ["reasonCode"] = DeniedReasonCode,
                ["correlationId"] = httpContext.TraceIdentifier
            });
}
