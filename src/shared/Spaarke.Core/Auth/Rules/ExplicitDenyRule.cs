using Spaarke.Dataverse;

namespace Spaarke.Core.Auth.Rules;

public class ExplicitDenyRule : IAuthorizationRule
{
    public Task<RuleResult> EvaluateAsync(AuthorizationContext context, AccessSnapshot snapshot, CancellationToken ct = default)
    {
        if (snapshot.AccessLevel == AccessLevel.Deny)
        {
            return Task.FromResult(new RuleResult
            {
                Decision = AuthorizationDecision.Deny,
                ReasonCode = "sdap.access.deny.explicit"
            });
        }

        return Task.FromResult(new RuleResult
        {
            Decision = AuthorizationDecision.Continue,
            ReasonCode = string.Empty
        });
    }
}