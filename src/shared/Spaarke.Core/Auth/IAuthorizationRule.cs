using Spaarke.Dataverse;

namespace Spaarke.Core.Auth;

public interface IAuthorizationRule
{
    Task<RuleResult> EvaluateAsync(AuthorizationContext context, AccessSnapshot snapshot, CancellationToken ct = default);
}

public class RuleResult
{
    public required AuthorizationDecision Decision { get; init; }
    public required string ReasonCode { get; init; }
}

public enum AuthorizationDecision
{
    Continue = 0,
    Allow = 1,
    Deny = 2
}