using Spaarke.Core.Auth;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.Auth;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Models.Office;

namespace Sprk.Bff.Api.Api.Filters;

/// <summary>
/// Extension methods for attaching <see cref="QuickCreateSourceAccessFilter"/>.
/// </summary>
public static class QuickCreateSourceAccessFilterExtensions
{
    /// <summary>
    /// Authorize the CALLER's Read right on the quick-create record context
    /// (<see cref="QuickCreateRequest.SourceEntityType"/> / <see cref="QuickCreateRequest.SourceRecordId"/>) and on the
    /// Assigned To contact (<see cref="QuickCreateRequest.AssignedToContactId"/>, task 100) before the handler runs.
    /// Apply after <c>AddOfficeAuthFilter()</c>.
    /// </summary>
    public static TBuilder AddQuickCreateSourceAccessFilter<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder
    {
        return builder.AddEndpointFilter(async (context, next) =>
        {
            var services = context.HttpContext.RequestServices;
            var filter = new QuickCreateSourceAccessFilter(
                services.GetRequiredService<CallerRecordAccessProbe>(),
                services.GetService<ILogger<QuickCreateSourceAccessFilter>>());
            return await filter.InvokeAsync(context, next);
        });
    }
}

/// <summary>
/// spaarkeai-word-add-in-r1 task 030 — the per-resource authorization decision for the quick-create record context.
/// </summary>
/// <remarks>
/// <para><b>Why it exists.</b> <c>RecordCreationService</c> reads the named source record <b>app-only</b> to apply the
/// Field Mapping Framework, then writes the mapped values onto a new matter the caller OWNS. Without this filter any
/// authenticated caller could name any record id and read its mapped fields back through their own new matter. The
/// client engine never had that exposure because it reads through <c>Xrm.WebApi</c> as the user.</para>
///
/// <para><b>What is shared (CLAUDE.md §11).</b> Mechanism and tables are reused, not re-declared: the SAME
/// <see cref="CallerRecordAccessProbe"/> (OBO <c>RetrievePrincipalAccess</c>), the SAME logical-name → entity-set
/// table via <see cref="EntityAccessFilter.TryResolveEntitySet"/>, and the existing <c>read</c> key in
/// <see cref="OperationAccessPolicy"/>. The 403 carries the same Office error shape as
/// <see cref="EntityAccessFilter"/> (<c>errorCode</c> <c>OFFICE_009</c>, <c>reasonCode</c>, <c>correlationId</c>),
/// which the task pane's error map keys on. This is a variant filter in the
/// <see cref="RecordRouteAccessAuthorizationFilter"/> sense, because both existing filters are wrong here:
/// <see cref="EntityAccessFilter"/> demands AppendTo with a "file documents" message and reads a <c>SaveRequest</c>;
/// the route filter reads route values. Copying fields FROM a record costs Read.</para>
///
/// <para><b>Fail-open only where nothing is read.</b> No source context → pass (the service reads nothing, so there is
/// nothing to authorize). A half-supplied context → pass to the handler, whose validation returns 400 before the
/// service is reached. A supplied context → Read is REQUIRED; an unmapped type, a thrown probe, or insufficient
/// rights all deny 403 before any read happens. The probe itself collapses every "could not answer" to
/// <see cref="AccessRights.None"/>, so this filter inherits that fail-closed posture. A client abort propagates as
/// cancellation rather than being reported as a denial.</para>
///
/// <para><b>The Assigned To contact (task 100).</b> The same filter also requires Read on
/// <see cref="QuickCreateRequest.AssignedToContactId"/> — the second caller-named id the create acts on — with one
/// constant deny body (see <c>AuthorizeAssigneeAsync</c>). The reference ids (matter type, practice area, project type)
/// are NOT gated: they are organization-owned reference rows the pane loads from the app-only
/// <c>GET /api/office/search/{list}</c>, so they carry no per-record access, and the creation service verifies each
/// one exists (an unknown id is dropped with a warning).</para>
///
/// <para><b>Residual</b> (notes/030 §9): Read is a RECORD-level check. An app-only read does not apply column-level
/// (field-level security) masking, so a secured column named in an admin-authored profile would still be copied.</para>
/// </remarks>
public sealed class QuickCreateSourceAccessFilter : IEndpointFilter
{
    /// <summary>The existing <see cref="OperationAccessPolicy"/> key for reading a record (<see cref="AccessRights.Read"/>).</summary>
    internal const string ReadOperation = "read";

    /// <summary>The Office error-code taxonomy's "access denied" code — the same one <see cref="EntityAccessFilter"/> emits.</summary>
    private const string AccessDeniedErrorCode = "OFFICE_009";

    /// <summary>The Assigned To contact's logical name (task 100) — resolved to its entity set through the shared table.</summary>
    private const string AssigneeLogicalName = "contact";

    /// <summary>The ONE reason code the assignee gate emits (task 100), for every refusal.</summary>
    internal const string AssigneeDeniedReasonCode = "assignee_inaccessible";

    /// <summary>
    /// The ONE detail the assignee gate emits. Names no contact and no id, and reads the same whether the contact is
    /// absent, invisible to the caller, or the check could not run.
    /// </summary>
    internal const string AssigneeDeniedDetail =
        "The person chosen in Assigned To is not available to you, so the record was not created. Choose someone "
        + "else, or clear Assigned To.";

    private readonly CallerRecordAccessProbe _probe;
    private readonly ILogger<QuickCreateSourceAccessFilter>? _logger;

    public QuickCreateSourceAccessFilter(
        CallerRecordAccessProbe probe,
        ILogger<QuickCreateSourceAccessFilter>? logger = null)
    {
        _probe = probe ?? throw new ArgumentNullException(nameof(probe));
        _logger = logger;
    }

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var httpContext = context.HttpContext;
        var request = context.Arguments.OfType<QuickCreateRequest>().FirstOrDefault();

        // No body → nothing named → nothing will be read or written → nothing to authorize.
        if (request is null)
        {
            return await next(context);
        }

        if (await AuthorizeSourceAsync(httpContext, request) is { } sourceDenied)
        {
            return sourceDenied;
        }

        if (await AuthorizeAssigneeAsync(httpContext, request) is { } assigneeDenied)
        {
            return assigneeDenied;
        }

        return await next(context);
    }

    /// <summary>
    /// The task-030 gate: Read on the record context the creation service copies fields FROM. Returns the deny result,
    /// or <see langword="null"/> to continue (also when no complete context is named — nothing is read then).
    /// </summary>
    private async Task<IResult?> AuthorizeSourceAsync(HttpContext httpContext, QuickCreateRequest request)
    {
        // Nothing named → nothing will be read → nothing to authorize. A half-supplied context passes to the
        // handler, whose validation returns 400 before the service is reached.
        if (string.IsNullOrWhiteSpace(request.SourceEntityType)
            || request.SourceRecordId is not { } sourceRecordId
            || sourceRecordId == Guid.Empty)
        {
            return null;
        }

        var sourceEntityType = request.SourceEntityType.Trim();

        // A MISS DENIES (the RecordRouteAccessAuthorizationFilter posture): a type whose per-record access this
        // codebase cannot evaluate is a type whose fields it must not copy. The caller's value is logged, not echoed.
        if (!EntityAccessFilter.TryResolveEntitySet(sourceEntityType, out var entitySet))
        {
            _logger?.LogWarning(
                "[QUICKCREATE-SOURCE-AUTH] Denying: source entity type '{EntityType}' is not in EntityAccessFilter's "
                + "logical-name -> entity-set table. CorrelationId: {CorrelationId}",
                sourceEntityType, httpContext.TraceIdentifier);

            return Deny(
                httpContext,
                "entity_type_not_authorizable",
                "Access to the related record cannot be evaluated for its record type, so this request is refused.");
        }

        AccessRights rights;
        try
        {
            rights = await _probe.GetCallerRightsAsync(
                TokenHelper.ExtractBearerTokenOrNull(httpContext),
                entitySet,
                sourceRecordId,
                httpContext.RequestAborted);
        }
        catch (OperationCanceledException) when (httpContext.RequestAborted.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Covers the AUTHORIZATION DECISION only — never next(), so downstream faults are not relabelled.
            _logger?.LogError(ex,
                "[QUICKCREATE-SOURCE-AUTH] The caller-rights probe threw for {EntitySet}({RecordId}). Denying. "
                + "CorrelationId: {CorrelationId}",
                entitySet, sourceRecordId, httpContext.TraceIdentifier);

            return Deny(
                httpContext,
                "access_check_failed",
                "Access to the related record could not be verified, so this request is refused.");
        }

        if (!OperationAccessPolicy.HasRequiredRights(rights, ReadOperation))
        {
            _logger?.LogWarning(
                "[QUICKCREATE-SOURCE-AUTH] Denied: caller cannot read {EntitySet}({RecordId}). Holds {Rights}. "
                + "CorrelationId: {CorrelationId}",
                entitySet, sourceRecordId, rights, httpContext.TraceIdentifier);

            return Deny(
                httpContext,
                "insufficient_rights",
                "You do not have permission to read the related record this new record would be created from.");
        }

        return null;
    }

    /// <summary>
    /// Task 100: Read on the Assigned To contact (<see cref="QuickCreateRequest.AssignedToContactId"/>), which the
    /// create writes onto a record the caller's team owns. Returns the deny result, or <see langword="null"/> to
    /// continue (also when none is named — the server's default is then the maker's OWN linked contact, or none).
    /// </summary>
    /// <remarks>
    /// Unlike the source gate, every refusal here is ONE constant body (<see cref="AssigneeDeniedReasonCode"/>,
    /// <see cref="AssigneeDeniedDetail"/>) — the <c>TodoSourceAccessFilter</c> posture for its own assignee. A probe
    /// that throws, a contact that does not exist and one the caller may not read are indistinguishable, so the route
    /// is not a contact-existence oracle. Without this gate the create, which writes app-only, would attach any
    /// contact GUID — and Dataverse's fault on a missing one (500) against the 201 for an existing one would have been
    /// that oracle.
    /// </remarks>
    private async Task<IResult?> AuthorizeAssigneeAsync(HttpContext httpContext, QuickCreateRequest request)
    {
        if (request.AssignedToContactId is not { } contactId || contactId == Guid.Empty)
        {
            return null;
        }

        // The shared logical-name → entity-set table (the same one /office/todo resolves its contact through).
        if (!EntityAccessFilter.TryResolveEntitySet(AssigneeLogicalName, out var entitySet))
        {
            _logger?.LogError(
                "[QUICKCREATE-ASSIGNEE-AUTH] Denying: '{EntityType}' has no entity-set mapping. CorrelationId: {CorrelationId}",
                AssigneeLogicalName, httpContext.TraceIdentifier);
            return DenyAssignee(httpContext);
        }

        AccessRights rights;
        try
        {
            rights = await _probe.GetCallerRightsAsync(
                TokenHelper.ExtractBearerTokenOrNull(httpContext),
                entitySet,
                contactId,
                httpContext.RequestAborted);
        }
        catch (OperationCanceledException) when (httpContext.RequestAborted.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex,
                "[QUICKCREATE-ASSIGNEE-AUTH] The caller-rights probe threw for {EntitySet}({RecordId}). Denying. "
                + "CorrelationId: {CorrelationId}",
                entitySet, contactId, httpContext.TraceIdentifier);
            return DenyAssignee(httpContext);
        }

        if (!OperationAccessPolicy.HasRequiredRights(rights, ReadOperation))
        {
            _logger?.LogWarning(
                "[QUICKCREATE-ASSIGNEE-AUTH] Denied: caller cannot read {EntitySet}({RecordId}). Holds {Rights}. "
                + "CorrelationId: {CorrelationId}",
                entitySet, contactId, rights, httpContext.TraceIdentifier);
            return DenyAssignee(httpContext);
        }

        return null;
    }

    /// <summary>The ONE refusal for the assignee gate — no reason varies with the contact (see <see cref="AuthorizeAssigneeAsync"/>).</summary>
    private static IResult DenyAssignee(HttpContext httpContext)
        => Deny(httpContext, AssigneeDeniedReasonCode, AssigneeDeniedDetail);

    /// <summary>A 403 in the Office ProblemDetails shape (<see cref="EntityAccessFilter"/>'s: errorCode + reasonCode + correlationId).</summary>
    private static IResult Deny(HttpContext httpContext, string reasonCode, string detail)
        => Results.Problem(
            statusCode: StatusCodes.Status403Forbidden,
            title: "Forbidden",
            detail: detail,
            type: "https://tools.ietf.org/html/rfc7231#section-6.5.3",
            extensions: new Dictionary<string, object?>
            {
                ["errorCode"] = AccessDeniedErrorCode,
                ["reasonCode"] = reasonCode,
                ["correlationId"] = httpContext.TraceIdentifier
            });
}
