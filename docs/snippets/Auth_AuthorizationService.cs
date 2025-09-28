public sealed class AuthorizationService : IAuthorizationService
{
    private readonly IReadOnlyList<IAuthorizationRule> _rules;
    private readonly IAccessDataSource _data;

    public AuthorizationService(IEnumerable<IAuthorizationRule> rules, IAccessDataSource data)
    { _rules = rules.OrderBy(r => r.Order).ToList(); _data = data; }

    public async Task<AuthorizationResult> AuthorizeAsync(ClaimsPrincipal user, ResourceRef resource, Operation op, CancellationToken ct)
    {
        var ctx = new AuthContext(user, resource, op);
        foreach (var rule in _rules)
        {
            if (!rule.Applies(ctx)) continue;
            var decision = await rule.EvaluateAsync(ctx, _data, ct);
            if (decision.Decision != RuleDecisionType.Indeterminate)
                return AuthorizationResult.FromRule(decision);
        }
        return AuthorizationResult.Deny("no_match", "No rule granted access");
    }
}