using Spaarke.Dataverse;

namespace Spaarke.Core.Auth.Rules;

/// <summary>
/// [DEPRECATED] Legacy rule from binary Grant/Deny model.
/// In granular AccessRights model, there is no explicit "Deny" state.
/// Users either have specific rights (Read, Write, Delete, etc.) or they don't.
/// This rule now always returns Continue to defer to OperationAccessRule.
/// </summary>
/// <remarks>
/// This rule is kept for backward compatibility during migration.
/// Recommended: Remove this rule from DI registration and use OperationAccessRule instead.
/// </remarks>
[Obsolete("Use OperationAccessRule instead. Binary Grant/Deny model is deprecated.")]
public class ExplicitDenyRule : IAuthorizationRule
{
    public Task<RuleResult> EvaluateAsync(AuthorizationContext context, AccessSnapshot snapshot, CancellationToken ct = default)
    {
        // In granular model, there's no explicit Deny - just absence of required rights
        // Always continue to let OperationAccessRule check granular permissions
        return Task.FromResult(new RuleResult
        {
            Decision = AuthorizationDecision.Continue,
            ReasonCode = string.Empty
        });
    }
}
