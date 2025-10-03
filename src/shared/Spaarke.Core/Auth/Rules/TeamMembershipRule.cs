using Spaarke.Dataverse;

namespace Spaarke.Core.Auth.Rules;

public class TeamMembershipRule : IAuthorizationRule
{
    public Task<RuleResult> EvaluateAsync(AuthorizationContext context, AccessSnapshot snapshot, CancellationToken ct = default)
    {
        // Simple team membership check - if user has any team memberships, allow read operations
        if (context.Operation == "read" && snapshot.TeamMemberships.Any())
        {
            return Task.FromResult(new RuleResult
            {
                Decision = AuthorizationDecision.Allow,
                ReasonCode = "sdap.access.allow.team_member"
            });
        }

        return Task.FromResult(new RuleResult
        {
            Decision = AuthorizationDecision.Continue,
            ReasonCode = string.Empty
        });
    }
}
