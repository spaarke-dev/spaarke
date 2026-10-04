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
///
/// <para><b>The TARGET table's Create privilege (unified-access-control-r2 task 166, sweep finding S-69; owner G5).</b>
/// Matter, project and invoice are created APP-ONLY and team-owned (the server invariant G5 keeps), so until this
/// task a caller who could not create a matter in the Dataverse UI could create one here. After the source half
/// passes, a request for <c>matter</c>, <c>project</c> or <c>invoice</c> now also requires the CALLER to hold that
/// table's live Create privilege (<see cref="CreatePrivilegeFor"/>), asked through
/// <see cref="CallerRecordAccessProbe.CallerHoldsPrivilegeAsync"/> (OBO WhoAmI +
/// <c>RetrieveUserSetOfPrivilegesByNames</c>, uncached, fail closed) — the task 130 pattern. Not held, a throw and
/// a missing token are one 403 (<c>OFFICE_009</c>, reason <c>insufficient_privilege</c>). <c>account</c>,
/// <c>contact</c> and an unparseable type are not created by this route (the service returns null / the handler
/// 400s), so they pass through unchanged with no privilege query. The source half runs FIRST and is unchanged:
/// an unreadable named source is refused before any privilege is asked.</para>
/// </remarks>
public sealed class QuickCreateSourceAccessFilter : IEndpointFilter
{
    /// <summary>The existing <see cref="OperationAccessPolicy"/> key for reading a record (<see cref="AccessRights.Read"/>).</summary>
    internal const string ReadOperation = "read";

    /// <summary>The route value naming the entity type to create.</summary>
    internal const string EntityTypeRouteValue = "entityType";

    /// <summary>
    /// The Dataverse Create privilege for <c>sprk_matter</c>, by its exact live name — read from spaarkedev1
    /// <c>privileges</c> metadata 2026-10-03 (read-only; task 166 note §live facts). Pinned by a test.
    /// </summary>
    internal const string CreateMatterPrivilege = "prvCreatesprk_Matter";

    /// <summary>
    /// The Dataverse Create privilege for <c>sprk_project</c>, by its exact live name — read from spaarkedev1
    /// <c>privileges</c> metadata 2026-10-03 (read-only; task 166 note §live facts). Pinned by a test.
    /// </summary>
    internal const string CreateProjectPrivilege = "prvCreatesprk_Project";

    /// <summary>
    /// The Create privilege a quick-create of <paramref name="entityType"/> costs, or <see langword="null"/> when this
    /// route creates no row of that type (account, contact). Invoice REUSES
    /// <see cref="FinanceAuthorizationFilter.CreateInvoicePrivilege"/> — one spelling of one privilege.
    /// </summary>
    internal static string? CreatePrivilegeFor(QuickCreateEntityType entityType) => entityType switch
    {
        QuickCreateEntityType.Matter => CreateMatterPrivilege,
        QuickCreateEntityType.Project => CreateProjectPrivilege,
        QuickCreateEntityType.Invoice => FinanceAuthorizationFilter.CreateInvoicePrivilege,
        _ => null,
    };

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

        // Half 1 — the SOURCE record (task 030), unchanged and FIRST.
        var sourceDenied = await AuthorizeSourceAsync(context, httpContext);
        if (sourceDenied is not null)
        {
            return sourceDenied;
        }

        // Half 2 — the TARGET table's Create privilege (task 166).
        var privilegeDenied = await AuthorizeCreatePrivilegeAsync(httpContext);
        if (privilegeDenied is not null)
        {
            return privilegeDenied;
        }

        return await next(context);
    }

    /// <summary>
    /// Task 166 (S-69): for matter / project / invoice, the CALLER must hold the table's Create privilege. Returns the
    /// 403 to return, or <see langword="null"/> to proceed. An unparseable type and account / contact pass through
    /// (the handler answers them exactly as before, and this route creates no such row).
    /// </summary>
    private async Task<IResult?> AuthorizeCreatePrivilegeAsync(HttpContext httpContext)
    {
        var rawEntityType = httpContext.Request.RouteValues.TryGetValue(EntityTypeRouteValue, out var raw)
            ? raw?.ToString()
            : null;

        if (string.IsNullOrWhiteSpace(rawEntityType)
            || !QuickCreateFieldRequirements.TryParse(rawEntityType, out var entityType)
            || CreatePrivilegeFor(entityType) is not { } privilege)
        {
            return null;
        }

        bool holds;
        try
        {
            holds = await _probe.CallerHoldsPrivilegeAsync(
                TokenHelper.ExtractBearerTokenOrNull(httpContext), privilege, httpContext.RequestAborted);
        }
        catch (OperationCanceledException) when (httpContext.RequestAborted.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // The DECISION only — never next().
            _logger?.LogError(ex,
                "[QUICKCREATE-PRIVILEGE] The privilege check {Privilege} threw; denying. CorrelationId: {CorrelationId}",
                privilege, httpContext.TraceIdentifier);
            holds = false;
        }

        if (holds)
        {
            return null;
        }

        _logger?.LogWarning(
            "[QUICKCREATE-PRIVILEGE] Denied: caller does not hold {Privilege} for a {EntityType} quick-create. "
            + "CorrelationId: {CorrelationId}",
            privilege, entityType, httpContext.TraceIdentifier);

        return Deny(
            httpContext,
            "insufficient_privilege",
            "You do not have permission to create this type of record.");
    }

    /// <summary>Task 030's source-record Read check — returns the 403 to return, or <see langword="null"/>.</summary>
    private async Task<IResult?> AuthorizeSourceAsync(EndpointFilterInvocationContext context, HttpContext httpContext)
    {
        var request = context.Arguments.OfType<QuickCreateRequest>().FirstOrDefault();

        // Nothing named → nothing will be read → nothing to authorize for the SOURCE half. (A route without a
        // QuickCreateRequest body has no source context for the creation service to read either.) Since task 166 this
        // pass-through applies to the source half only: the Create-privilege half still runs.
        if (request is null
            || string.IsNullOrWhiteSpace(request.SourceEntityType)
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
