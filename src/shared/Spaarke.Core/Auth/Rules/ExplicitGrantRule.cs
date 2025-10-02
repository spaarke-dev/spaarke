using Spaarke.Dataverse;

namespace Spaarke.Core.Auth.Rules;

/// <summary>
/// [DEPRECATED] Legacy rule from binary Grant/Deny model.
/// In granular AccessRights model, access is determined by operation-specific rights.
/// This rule now always returns Continue to defer to OperationAccessRule.
/// </summary>
/// <remarks>
/// This rule is kept for backward compatibility during migration.
/// Recommended: Remove this rule from DI registration and use OperationAccessRule instead.
/// </remarks>
[Obsolete("Use OperationAccessRule instead. Binary Grant/Deny model is deprecated.")]
public class ExplicitGrantRule : IAuthorizationRule
{
    public Task<RuleResult> EvaluateAsync(AuthorizationContext context, AccessSnapshot snapshot, CancellationToken ct = default)
    {
        // In granular model, we don't grant blanket access
        // Always continue to let OperationAccessRule check specific operation requirements
        return Task.FromResult(new RuleResult
        {
            Decision = AuthorizationDecision.Continue,
            ReasonCode = string.Empty
        });
    }
}
