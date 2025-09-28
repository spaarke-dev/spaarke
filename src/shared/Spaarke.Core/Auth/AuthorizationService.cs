using Microsoft.Extensions.Logging;
using Spaarke.Dataverse;

namespace Spaarke.Core.Auth;

public class AuthorizationService
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
        _logger.LogDebug("Evaluating authorization for user {UserId} on resource {ResourceId} operation {Operation}",
            context.UserId, context.ResourceId, context.Operation);

        var accessSnapshot = await _accessDataSource.GetUserAccessAsync(context.UserId, context.ResourceId, ct);

        foreach (var rule in _rules)
        {
            var result = await rule.EvaluateAsync(context, accessSnapshot, ct);
            if (result.Decision != AuthorizationDecision.Continue)
            {
                _logger.LogInformation("Authorization {Decision} by rule {RuleType} for user {UserId}: {Reason}",
                    result.Decision, rule.GetType().Name, context.UserId, result.ReasonCode);

                return new AuthorizationResult
                {
                    IsAllowed = result.Decision == AuthorizationDecision.Allow,
                    ReasonCode = result.ReasonCode,
                    RuleName = rule.GetType().Name
                };
            }
        }

        _logger.LogWarning("No authorization rule made a decision for user {UserId} on resource {ResourceId}",
            context.UserId, context.ResourceId);

        return new AuthorizationResult
        {
            IsAllowed = false,
            ReasonCode = "sdap.access.deny.no_rule",
            RuleName = "DefaultDeny"
        };
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