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

            // Fetch user access snapshot from Dataverse
            // Note: SDAP authorization uses service principal auth (no user token available)
            // For AI authorization with OBO, see AiAuthorizationService
            var accessSnapshot = await _accessDataSource.GetUserAccessAsync(
                context.UserId,
                context.ResourceId,
                userAccessToken: null, // Service principal auth
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
}

public class AuthorizationResult
{
    public required bool IsAllowed { get; init; }
    public required string ReasonCode { get; init; }
    public required string RuleName { get; init; }
}
