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
    /// (<see cref="QuickCreateRequest.SourceEntityType"/> / <see cref="QuickCreateRequest.SourceRecordId"/>) before
    /// the handler runs. Apply after <c>AddOfficeAuthFilter()</c>.
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
/// <para><b>Residual</b> (notes/030 §9): Read is a RECORD-level check. An app-only read does not apply column-level
/// (field-level security) masking, so a secured column named in an admin-authored profile would still be copied.</para>
/// </remarks>
public sealed class QuickCreateSourceAccessFilter : IEndpointFilter
{
    /// <summary>The existing <see cref="OperationAccessPolicy"/> key for reading a record (<see cref="AccessRights.Read"/>).</summary>
    internal const string ReadOperation = "read";

    /// <summary>The Office error-code taxonomy's "access denied" code — the same one <see cref="EntityAccessFilter"/> emits.</summary>
    private const string AccessDeniedErrorCode = "OFFICE_009";

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

        // Nothing named → nothing will be read → nothing to authorize. (A route without a QuickCreateRequest body has
        // no source context for the creation service to read either.)
        if (request is null
            || string.IsNullOrWhiteSpace(request.SourceEntityType)
            || request.SourceRecordId is not { } sourceRecordId
            || sourceRecordId == Guid.Empty)
        {
            return await next(context);
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

        return await next(context);
    }

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
