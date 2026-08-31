using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Spaarke.Dataverse;

namespace Spaarke.Core.Auth;

/// <summary>
/// Evaluates authorization requests against an ordered chain of IAuthorizationRule policies.
/// Queries user access data from Dataverse via IAccessDataSource.
/// Implements comprehensive audit logging for security compliance.
/// </summary>
public class AuthorizationService : IAuthorizationService
{
    private readonly IAccessDataSource _accessDataSource;
    private readonly IEnumerable<IAuthorizationRule> _rules;
    private readonly ILogger<AuthorizationService> _logger;

    public AuthorizationService(
        IAccessDataSource accessDataSource,
        IEnumerable<IAuthorizationRule> rules,
        ILogger<AuthorizationService> logger)
    {
        _accessDataSource = accessDataSource ?? throw new ArgumentNullException(nameof(accessDataSource));
        _rules = rules ?? throw new ArgumentNullException(nameof(rules));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<AuthorizationResult> AuthorizeAsync(AuthorizationContext context, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        using var activity = new Activity("AuthorizationCheck").Start();
        activity.SetTag("userId", context.UserId);
        activity.SetTag("resourceId", context.ResourceId);
        activity.SetTag("operation", context.Operation);
        activity.SetTag("correlationId", context.CorrelationId);

        var stopwatch = Stopwatch.StartNew();

        try
        {
            _logger.LogDebug("Evaluating authorization for user {UserId} on resource {ResourceId} operation {Operation}",
                context.UserId, context.ResourceId, context.Operation);

            // FAIL CLOSED (task 004 / FR-02, finding A-2): a caller-scoped evaluation with no caller
            // token must DENY. It must never degrade to app-only evaluation, because app-only always
            // answers "yes" on the SPA/Teams surface (reads are app-only there, so Dataverse row-level
            // security is inert) — which is precisely the disclosure A-2 describes.
            //
            // Every consumer of this service runs inside an HTTP request (all six call-sites verified
            // 2026-08-21: notes/task-004-callsite-classification.md — there are ZERO app-only
            // consumers), so an absent token here means the credential could not be read, not that
            // app-only was intended. Denying is correct in both readings.
            if (string.IsNullOrWhiteSpace(context.UserAccessToken))
            {
                stopwatch.Stop();
                activity.SetTag("result", "Deny");
                activity.SetTag("reason", "no_caller_token");

                _logger.LogWarning(
                    "AUTHORIZATION DENIED (no caller token): User {UserId} on {ResourceId} operation " +
                    "{Operation} — a caller-scoped evaluation requires the caller's bearer token; " +
                    "refusing to fall back to app-only evaluation (fail closed). Duration: {DurationMs}ms",
                    context.UserId, context.ResourceId, context.Operation, stopwatch.ElapsedMilliseconds);

                return new AuthorizationResult
                {
                    IsAllowed = false,
                    ReasonCode = "sdap.access.deny.no_caller_token",
                    RuleName = nameof(AuthorizationService)
                };
            }

            // Evaluate AS THE CALLER. Routed through GetCallerAccessAsync so that this service has
            // exactly ONE place that touches the access data source — see that method's remarks.
            var accessSnapshot = await GetCallerAccessAsync(
                context.UserId,
                context.ResourceId,
                context.UserAccessToken,
                ct);

            activity.SetTag("accessRights", accessSnapshot.AccessRights.ToString());
            activity.SetTag("teamCount", accessSnapshot.TeamMemberships.Count());

            // Evaluate rules in order
            foreach (var rule in _rules)
            {
                var result = await rule.EvaluateAsync(context, accessSnapshot, ct);
                if (result.Decision != AuthorizationDecision.Continue)
                {
                    stopwatch.Stop();
                    activity.SetTag("result", result.Decision.ToString());
                    activity.SetTag("ruleName", rule.GetType().Name);
                    activity.SetTag("durationMs", stopwatch.ElapsedMilliseconds);

                    var authResult = new AuthorizationResult
                    {
                        IsAllowed = result.Decision == AuthorizationDecision.Allow,
                        ReasonCode = result.ReasonCode,
                        RuleName = rule.GetType().Name
                    };

                    // Audit log
                    if (authResult.IsAllowed)
                    {
                        _logger.LogInformation(
                            "AUTHORIZATION GRANTED: User {UserId} granted {Operation} on {ResourceId} by {RuleName} - Reason: {Reason} (AccessRights: {AccessRights}, Duration: {DurationMs}ms)",
                            context.UserId, context.Operation, context.ResourceId, authResult.RuleName, authResult.ReasonCode, accessSnapshot.AccessRights, stopwatch.ElapsedMilliseconds);
                    }
                    else
                    {
                        _logger.LogWarning(
                            "AUTHORIZATION DENIED: User {UserId} denied {Operation} on {ResourceId} by {RuleName} - Reason: {Reason} (AccessRights: {AccessRights}, Duration: {DurationMs}ms)",
                            context.UserId, context.Operation, context.ResourceId, authResult.RuleName, authResult.ReasonCode, accessSnapshot.AccessRights, stopwatch.ElapsedMilliseconds);
                    }

                    return authResult;
                }
            }

            // No rule made a decision - default deny (fail-closed)
            stopwatch.Stop();
            activity.SetTag("result", "DefaultDeny");
            activity.SetTag("durationMs", stopwatch.ElapsedMilliseconds);

            _logger.LogWarning(
                "AUTHORIZATION DENIED: No rule made a decision for user {UserId} on resource {ResourceId} operation {Operation} - Defaulting to DENY (Duration: {DurationMs}ms)",
                context.UserId, context.ResourceId, context.Operation, stopwatch.ElapsedMilliseconds);

            return new AuthorizationResult
            {
                IsAllowed = false,
                ReasonCode = "sdap.access.deny.no_rule",
                RuleName = "DefaultDeny"
            };
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            activity.SetTag("result", "Error");
            activity.SetTag("durationMs", stopwatch.ElapsedMilliseconds);

            _logger.LogError(
                exception: ex,
                message: "AUTHORIZATION ERROR: Failed to evaluate authorization for user {UserId} on resource {ResourceId} operation {Operation} - Fail-closed: DENY (Duration: {DurationMs}ms)",
                context.UserId, context.ResourceId, context.Operation, stopwatch.ElapsedMilliseconds);

            // Fail-closed: Deny on errors
            return new AuthorizationResult
            {
                IsAllowed = false,
                ReasonCode = "sdap.access.error.system_failure",
                RuleName = "SystemError"
            };
        }
    }

    /// <summary>
    /// Resolves the CALLER-scoped access snapshot for one resource — "what rights does THIS CALLER
    /// hold on this record?" — without deciding any single operation.
    /// </summary>
    /// <param name="userId">The caller's Entra object id (<c>oid</c>).</param>
    /// <param name="resourceId">The record being asked about.</param>
    /// <param name="userAccessToken">
    /// The caller's bearer token. Deliberately has <b>no default value</b> — see the remarks.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// The caller's snapshot, or a snapshot carrying <see cref="AccessRights.None"/> when no caller
    /// token is available. Never an app-only snapshot.
    /// </returns>
    /// <remarks>
    /// <para><b>Why this exists.</b> <see cref="AuthorizeAsync"/> answers "may this caller do X?" and
    /// returns a boolean. Capability/affordance consumers need the other question — the rights
    /// themselves — so they can project many operations from ONE snapshot. Before
    /// unified-access-control-r2 task 006, the only way to ask that was to call
    /// <see cref="IAccessDataSource.GetUserAccessAsync"/> directly, and
    /// <c>Api/PermissionsEndpoints.cs</c> did exactly that with <c>userAccessToken: null</c> — reporting
    /// what the APPLICATION could do to any authenticated caller (finding A-4, spec FR-05).</para>
    ///
    /// <para><b>Why the token parameter has no default.</b> The A-4 defect was not a missing null check;
    /// it was the <c>= null</c> <i>default</i> on <see cref="IAccessDataSource.GetUserAccessAsync"/>,
    /// which let a new direct caller inherit app-only evaluation by simply not thinking about it. A
    /// mandatory positional parameter cannot be called without stating intent — the same forcing
    /// function task 004 applied to <see cref="AuthorizationContext.UserAccessToken"/> via
    /// <c>required</c>, in the shape available to a method signature.</para>
    ///
    /// <para><b>Single source of truth.</b> This is the ONLY member of this class that calls
    /// <see cref="IAccessDataSource"/>; <see cref="AuthorizeAsync"/> routes through it. That makes
    /// "capabilities derive from the same snapshot as enforcement" (FR-05 acceptance) verifiable by
    /// grepping for <c>_accessDataSource</c> rather than something a reviewer has to take on trust.</para>
    ///
    /// <para><b>Fail closed.</b> An absent token yields <see cref="AccessRights.None"/> and the data
    /// source is not consulted at all. Passing the null through would be strictly worse than denying:
    /// on the SPA/Teams surface reads are app-only, so Dataverse row-level security is inert and
    /// app-only answers "yes" — which is precisely the disclosure this method exists to prevent.</para>
    /// </remarks>
    public async Task<AccessSnapshot> GetCallerAccessAsync(
        string userId,
        string resourceId,
        string? userAccessToken,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId, nameof(userId));
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceId, nameof(resourceId));

        if (string.IsNullOrWhiteSpace(userAccessToken))
        {
            _logger.LogWarning(
                "ACCESS SNAPSHOT DENIED (no caller token): User {UserId} on {ResourceId} — a " +
                "caller-scoped snapshot requires the caller's bearer token; refusing to fall back to " +
                "app-only evaluation (fail closed). Returning AccessRights.None.",
                userId, resourceId);

            return new AccessSnapshot
            {
                UserId = userId,
                ResourceId = resourceId,
                AccessRights = AccessRights.None
            };
        }

        // Forward the caller's token so DataverseAccessDataSource takes its OBO path
        // (GetUserAccessAsync:159-227) and queries Dataverse AS THE USER rather than as the app.
        return await _accessDataSource.GetUserAccessAsync(userId, resourceId, userAccessToken, ct);
    }

    /// <summary>
    /// Resolves the caller's rights on a record of an ARBITRARY entity type — the sibling of
    /// <see cref="GetCallerAccessAsync"/>, which is document-only.
    /// </summary>
    /// <param name="userId">The caller's Entra object id (<c>oid</c>).</param>
    /// <param name="entitySetName">
    /// The Dataverse entity SET (plural) name of the target — e.g. <c>sprk_matters</c>. Callers pass a
    /// value from an explicit allow-list; nothing here pluralizes or guesses a logical name.
    /// </param>
    /// <param name="recordId">The target record's id.</param>
    /// <param name="userAccessToken">
    /// The caller's bearer token. No default, for the same forcing-function reason as
    /// <see cref="GetCallerAccessAsync"/>: app-only evaluation must be an explicit, reviewable choice
    /// at the call site rather than something a new caller inherits by omission (finding A-4).
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    /// <remarks>
    /// <para><b>Why (unified-access-control-r2 task 070).</b> <see cref="GetCallerAccessAsync"/> resolves
    /// through a Dataverse call whose target is hard-coded to <c>sprk_documents</c>, so it cannot answer
    /// "may this caller read this <i>matter</i>?". <c>POST /api/ai/search</c> with <c>scope=entity</c>
    /// must answer exactly that before returning a matter's documents — that check is what makes
    /// "access flows from the parent" an enforced property rather than a stated intention.</para>
    ///
    /// <para><b>Not a second policy.</b> Same authority, same evaluation-as-the-caller, wider reach.
    /// Fail-closed behaviour is identical: an absent token yields <see cref="AccessRights.None"/> and
    /// the data source is never consulted app-only.</para>
    /// </remarks>
    public async Task<AccessSnapshot> GetCallerRecordAccessAsync(
        string userId,
        string entitySetName,
        Guid recordId,
        string? userAccessToken,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId, nameof(userId));
        ArgumentException.ThrowIfNullOrWhiteSpace(entitySetName, nameof(entitySetName));

        if (string.IsNullOrWhiteSpace(userAccessToken))
        {
            _logger.LogWarning(
                "RECORD ACCESS DENIED (no caller token): User {UserId} on {EntitySet}({RecordId}) — a " +
                "caller-scoped snapshot requires the caller's bearer token; refusing to fall back to " +
                "app-only evaluation (fail closed). Returning AccessRights.None.",
                userId, entitySetName, recordId);

            return new AccessSnapshot
            {
                UserId = userId,
                ResourceId = recordId.ToString(),
                AccessRights = AccessRights.None
            };
        }

        return await _accessDataSource.GetRecordAccessAsync(
            userId, entitySetName, recordId, userAccessToken, ct);
    }
}

public class AuthorizationContext
{
    public required string UserId { get; init; }
    public required string ResourceId { get; init; }
    public required string Operation { get; init; }
    public string? CorrelationId { get; init; }

    /// <summary>
    /// The caller's bearer token, forwarded to <see cref="IAccessDataSource"/> so access is evaluated
    /// AS THE CALLER (OBO) rather than as the application. <c>null</c> selects app-only evaluation and
    /// is <b>denied</b> by <see cref="AuthorizationService"/> — see the remarks.
    /// </summary>
    /// <remarks>
    /// unified-access-control-r2 task 004 (FR-02, finding A-2). Before this, AuthorizationService
    /// hard-coded <c>userAccessToken: null</c>, so every "user permission check" actually answered
    /// *can the application see this record* — which on the SPA/Teams surface is always yes, because
    /// reads there are app-only and Dataverse row-level security is inert. The check was structurally
    /// incapable of isolating one caller from another.
    ///
    /// <para><b>Why this lives on the context rather than being injected.</b> The token originates in
    /// <c>HttpContext</c>, which lives in the BFF; <c>Spaarke.Core</c> has no ASP.NET Core dependency
    /// (see Spaarke.Core.csproj — Extensions.Abstractions only) and must not acquire one, both for
    /// layering and because <c>LayerDependencyTests</c> guards that boundary. Injecting
    /// <c>IHttpContextAccessor</c> here would add a web dependency to a non-web library; adding a
    /// second authorization service would violate ADR-010. Carrying the token on the existing context
    /// object extends the existing seam, which is what ADR-010 asks for.</para>
    ///
    /// <para><b>Why <c>required</c> on a nullable property.</b> It is deliberately not optional-with-
    /// a-default. <c>required</c> forces every construction site to state its intent, so app-only
    /// evaluation becomes a visible, reviewable <c>UserAccessToken = null</c> at the call-site rather
    /// than a silent omission — which is exactly how A-2 survived as long as it did. A future
    /// background caller that genuinely needs app-only evaluation writes that null explicitly and
    /// gets caught in review, instead of inheriting it by forgetting.</para>
    /// </remarks>
    public required string? UserAccessToken { get; init; }
}

public class AuthorizationResult
{
    public required bool IsAllowed { get; init; }
    public required string ReasonCode { get; init; }
    public required string RuleName { get; init; }
}
