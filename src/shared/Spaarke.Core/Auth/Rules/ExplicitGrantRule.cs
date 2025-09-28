using Spaarke.Dataverse;

namespace Spaarke.Core.Auth.Rules;

public class ExplicitGrantRule : IAuthorizationRule
{
    public Task<RuleResult> EvaluateAsync(AuthorizationContext context, AccessSnapshot snapshot, CancellationToken ct = default)
    {
        if (snapshot.AccessLevel == AccessLevel.Grant)
        {
            return Task.FromResult(new RuleResult
            {
                Decision = AuthorizationDecision.Allow,
                ReasonCode = "sdap.access.allow.explicit"
            });
        }

        return Task.FromResult(new RuleResult
        {
            Decision = AuthorizationDecision.Continue,
            ReasonCode = string.Empty
        });
    }
}