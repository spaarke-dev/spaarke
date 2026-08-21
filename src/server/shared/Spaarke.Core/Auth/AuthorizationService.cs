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

            // Evaluate AS THE CALLER: forward the caller's token so DataverseAccessDataSource takes its
            // OBO path (GetUserAccessAsync:159-227) and queries Dataverse as the user, rather than
            // app-only. Mirrors AiAuthorizationService.cs:176-180, which has always done this and was
            // the only genuinely caller-scoped path before this task.
            var accessSnapshot = await _accessDataSource.GetUserAccessAsync(
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
