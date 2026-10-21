using Spaarke.Core.Auth;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Services.Ai.Context;
using Sprk.Bff.Api.Services.Identity;

namespace Sprk.Bff.Api.Api.Filters;

/// <summary>
/// unified-access-control-r2 task 188 round 3 slice B (security pass 1): THE external document narrowing on the
/// internal plane's document routes — every route behind <see cref="DocumentAuthorizationFilter"/>, the bulk download
/// (<see cref="BulkDownloadAuthorizationFilter"/>) and the two container list routes
/// (<c>DataverseDocumentsEndpoints.ListDocumentsAsync</c> / <c>ListContainerDocumentsAsync</c>). It runs AFTER the root
/// grants allowed (the routes' own authorization is unchanged) and can only remove documents.
/// </summary>
/// <remarks>
/// <para><b>Who is narrowed.</b> Every caller who is not a resolved INTERNAL systemuser
/// (the rule of <c>ExternalCallerCommunicationGuard.IsExternalCallerAsync</c>: flagged <c>sprk_isexternal</c>, unresolved,
/// or no principal). A lookup that FAILS counts as external, so the narrowing applies: a transient fault costs an internal user
/// only the documents it hides, never every request (the slice A rule). Internal systemusers are not narrowed and nothing is
/// read for them.</para>
/// <para><b>What decides.</b> The injected <see cref="InternalRouteDocumentNarrowing"/> (registered by
/// <c>AddExternalAccess</c>; the same app-only read and decision as <see cref="AppOnlyDocumentNarrowing"/>, logged as
/// <c>plane=internal-routes</c>): the documents are re-read BY ID under the BFF's app-only identity and decided by
/// <see cref="ExternalDocumentNarrowing"/>; nothing of the rule is copied here. ONE call per request, over every id the
/// request names. A document the narrowing does not return as visible is hidden (a fault hides every one).</para>
/// <para><b>Component Justification (CLAUDE.md §11).</b> (1) Existing — the externality rule
/// (<c>ExternalCallerCommunicationGuard.IsExternalCallerAsync</c>, applied over its own two resolvers, see
/// <see cref="IsExternalOrUnknownCallerAsync"/>) and the narrowing seam
/// (<see cref="ExternalDataService.ReadAndNarrowDocumentsAsync"/>, through <see cref="InternalRouteDocumentNarrowing"/>) are
/// reused as they are; <c>RetrievalAccessTrim</c> (slice A) applies the same two
/// privately to AI retrieval keys. (2) Extension — the three surfaces here are an endpoint filter, a bulk filter and two
/// route handlers with no common service to extend, so one static helper carries the fault policy once instead of four
/// times; no DI registration, no interface. (3) Cost of doing nothing — a systemuser flagged external with a share on a
/// matter downloads, previews, edits or lists the .eml of an internal-only email, its attachments, or a Restricted
/// document through the internal plane's document routes (security pass 1, slice B).</para>
/// <para>ADR-015: logs counts and reason codes only, never ids.</para>
/// </remarks>
internal static class InternalPlaneDocumentNarrowing
{
    /// <summary>
    /// The reason code a hidden document is refused with on a single-document route: the one
    /// <c>OperationAccessRule</c> gives a caller who lacks the operation's right, so a hidden document is indistinguishable
    /// from one the caller may not touch (no new status code, policy or reason).
    /// </summary>
    internal const string HiddenReasonCode = "sdap.access.deny.insufficient_rights";

    /// <summary>
    /// Which of <paramref name="documentIds"/> the request's caller may see. For an internal systemuser nothing is
    /// narrowed (<see cref="CallerDocumentVisibility.Narrowed"/> false, no read). For any other caller, one
    /// <see cref="InternalRouteDocumentNarrowing"/> call over the distinct GUID ids decides; an id that is not a GUID is not shown;
    /// a narrowing that faults shows none. An empty set makes no lookup and no read.
    /// </summary>
    /// <param name="httpContext">The request (its caller and its services).</param>
    /// <param name="documentIds">The document ids the request names (sprk_documentid values).</param>
    /// <param name="surface">A fixed label for the log line (no ids).</param>
    /// <param name="logger">Optional logger.</param>
    internal static async Task<CallerDocumentVisibility> ForCallerAsync(
        HttpContext httpContext, IEnumerable<string?> documentIds, string surface, ILogger? logger)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        ArgumentNullException.ThrowIfNull(documentIds);
        var ct = httpContext.RequestAborted;

        var asked = documentIds.ToList();
        if (asked.Count == 0 || !await IsExternalOrUnknownCallerAsync(httpContext, surface, logger, ct).ConfigureAwait(false))
        {
            return CallerDocumentVisibility.NotNarrowed;
        }

        return await NarrowForExternalCallerAsync(httpContext, asked, surface, logger).ConfigureAwait(false);
    }

    /// <summary>The rule name a document the narrowing hides is denied under (logs only; the reason code is
    /// <see cref="HiddenReasonCode"/>).</summary>
    internal const string HiddenRuleName = "ExternalDocumentNarrowing";

    /// <summary>
    /// Task 188 round 4b (F1-2): THE document-resource decision for every site outside
    /// <see cref="DocumentAuthorizationFilter"/>. The root-grant decision (<see cref="IAuthorizationService.AuthorizeAsync"/>,
    /// the caller's own rights, one call per id as before) and then ONE <see cref="ForCallerAsync"/> call over every id it
    /// allowed. The first id the root grants deny is returned as-is (the site's deny shape is unchanged); a document the
    /// narrowing hides (or a narrowing fault) answers a denial with <see cref="HiddenReasonCode"/> — the reason
    /// <c>OperationAccessRule</c> gives a caller without the operation's right — so a hidden document cannot be told from an
    /// unreadable one. Internal systemusers: no narrowing call and no app-only read. An empty list is allowed with no call.
    /// </summary>
    /// <remarks>Enforced by the ArchTest <c>EveryDocumentResourceGrant_GoesThroughTheNarrowingHelper</c>: a direct
    /// document-resource <c>AuthorizeAsync</c> anywhere else in the BFF fails it.</remarks>
    internal static async Task<AuthorizationResult> AuthorizeDocumentsAsync(
        HttpContext httpContext,
        IAuthorizationService authorizationService,
        string userId,
        IReadOnlyCollection<Guid> documentIds,
        string operation,
        string? userAccessToken,
        string surface,
        ILogger? logger,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        ArgumentNullException.ThrowIfNull(authorizationService);
        ArgumentNullException.ThrowIfNull(documentIds);

        AuthorizationResult? allowed = null;
        foreach (var documentId in documentIds)
        {
            var result = await authorizationService.AuthorizeAsync(new AuthorizationContext
            {
                UserId = userId,
                ResourceId = documentId.ToString(),
                Operation = operation,
                CorrelationId = httpContext.TraceIdentifier,
                UserAccessToken = userAccessToken,
            }, ct).ConfigureAwait(false);
            if (!result.IsAllowed)
            {
                return result;
            }

            allowed ??= result;
        }

        if (allowed is null)
        {
            return new AuthorizationResult { IsAllowed = true, ReasonCode = "sdap.access.allow.no_documents", RuleName = HiddenRuleName };
        }

        var visibility = await ForCallerAsync(
            httpContext, documentIds.Select(id => (string?)id.ToString()), surface, logger).ConfigureAwait(false);
        if (documentIds.All(id => visibility.IsShown(id.ToString())))
        {
            return allowed;
        }

        return new AuthorizationResult { IsAllowed = false, ReasonCode = HiddenReasonCode, RuleName = HiddenRuleName };
    }

    /// <summary>
    /// The narrowing step of <see cref="ForCallerAsync"/> for a caller ALREADY known not to be an internal systemuser
    /// (<see cref="IsExternalOrUnknownCallerAsync"/> answered true): one <see cref="InternalRouteDocumentNarrowing"/> call
    /// over the distinct GUID ids; a non-GUID id is not shown; a fault shows none. Task 188 round 4a: for a surface that
    /// must read the document ids app-only only after it knows the caller is narrowed (the analysis routes).
    /// </summary>
    internal static async Task<CallerDocumentVisibility> NarrowForExternalCallerAsync(
        HttpContext httpContext, IEnumerable<string?> documentIds, string surface, ILogger? logger)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        ArgumentNullException.ThrowIfNull(documentIds);
        var ct = httpContext.RequestAborted;
        var asked = documentIds.ToList();

        var ids = new HashSet<Guid>();
        foreach (var raw in asked)
        {
            if (Guid.TryParse(raw, out var id) && id != Guid.Empty)
                ids.Add(id);
        }

        if (ids.Count == 0)
        {
            return CallerDocumentVisibility.Shows(new HashSet<Guid>());
        }

        DocumentNarrowingResult narrowing;
        try
        {
            var narrow = httpContext.RequestServices.GetRequiredService<InternalRouteDocumentNarrowing>();
            narrowing = await narrow(ids, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex,
                "[DOC-NARROWING] {Surface}: the external document narrowing failed for {Count} document(s); hiding all of "
                + "them (fail closed)",
                surface, ids.Count);
            return CallerDocumentVisibility.Shows(new HashSet<Guid>());
        }

        var visible = ids.Where(narrowing.IsVisible).ToHashSet();
        if (visible.Count != ids.Count)
        {
            logger?.LogInformation(
                "[DOC-NARROWING] {Surface}: {Hidden} of {Count} document(s) hidden from a caller who is not an internal "
                + "systemuser ({Reasons})",
                surface, ids.Count - visible.Count, ids.Count, narrowing.HiddenSummary(ids));
        }

        return CallerDocumentVisibility.Shows(visible);
    }

    /// <summary>
    /// True unless the request's caller is a resolved INTERNAL systemuser: the externality rule of
    /// <c>ExternalCallerCommunicationGuard.IsExternalCallerAsync</c> (flagged <c>sprk_isexternal</c>, unresolved, or no
    /// principal is external), applied over the same two resolvers. It is not called from here because non-AI code may not
    /// depend on <c>Services.Ai.Handlers.Dataverse</c> (ArchTest D-66). Under this helper's fault policy the guard's
    /// "lookup-failed throws" and "unresolved is external" give the same answer, so a lookup that fails (an unresolved
    /// <c>lookup-failed</c>, a throw, or a resolver that cannot be resolved) counts as external and the narrowing applies.
    /// </summary>
    internal static async Task<bool> IsExternalOrUnknownCallerAsync(
        HttpContext httpContext, string surface, ILogger? logger, CancellationToken ct)
    {
        try
        {
            var services = httpContext.RequestServices;
            var resolution = await services.GetRequiredService<ICallerSystemUserResolver>()
                .ResolveAsync(httpContext.User, ct).ConfigureAwait(false);
            if (!resolution.IsResolved || !Guid.TryParse(resolution.SystemUserId, out var systemUserId))
            {
                return true;
            }

            return await services.GetRequiredService<ISystemUserIdentityResolver>()
                .IsExternalAsync(systemUserId, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex,
                "[DOC-NARROWING] {Surface}: the caller's external flag could not be read; applying the external document "
                + "narrowing (fail closed: only documents it hides are withheld)",
                surface);
            return true;
        }
    }
}

/// <summary>
/// The answer of <see cref="InternalPlaneDocumentNarrowing.ForCallerAsync"/>: either not narrowed (an internal systemuser),
/// or the set of document ids the narrowing shows.
/// </summary>
internal sealed class CallerDocumentVisibility
{
    private readonly IReadOnlySet<Guid> _visible;

    private CallerDocumentVisibility(bool narrowed, IReadOnlySet<Guid> visible)
    {
        Narrowed = narrowed;
        _visible = visible;
    }

    /// <summary>An internal systemuser: every document the root grants allow is shown.</summary>
    internal static CallerDocumentVisibility NotNarrowed { get; } = new(false, new HashSet<Guid>());

    /// <summary>A narrowed caller: only <paramref name="visible"/> is shown.</summary>
    internal static CallerDocumentVisibility Shows(IReadOnlySet<Guid> visible) => new(true, visible);

    /// <summary>True when the narrowing applied to this caller.</summary>
    internal bool Narrowed { get; }

    /// <summary>
    /// Whether the document <paramref name="documentId"/> is shown: always when not narrowed; otherwise only a GUID id the
    /// narrowing returned as visible.
    /// </summary>
    internal bool IsShown(string? documentId) =>
        !Narrowed || (Guid.TryParse(documentId, out var id) && _visible.Contains(id));
}
