using Spaarke.Dataverse;
using Microsoft.Extensions.Logging;

namespace Spaarke.Core.Auth.Rules;

/// <summary>
/// Authorization rule that checks if user has required AccessRights for a specific operation.
/// Uses OperationAccessPolicy to determine required permissions and performs bitwise checks.
/// </summary>
/// <remarks>
/// This is the primary authorization rule for the granular permissions model.
/// Replaces ExplicitGrantRule and ExplicitDenyRule with operation-level security.
///
/// Example flow:
/// 1. User requests to download file (operation: "download_file")
/// 2. OperationAccessPolicy says download_file requires AccessRights.Write
/// 3. User's AccessSnapshot shows AccessRights.Read only
/// 4. Rule returns Deny (user lacks Write permission)
/// </remarks>
public class OperationAccessRule : IAuthorizationRule
{
    private readonly ILogger<OperationAccessRule> _logger;

    public OperationAccessRule(ILogger<OperationAccessRule> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public Task<RuleResult> EvaluateAsync(AuthorizationContext context, AccessSnapshot snapshot, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(snapshot);

        // Check if operation is supported
        if (!OperationAccessPolicy.IsOperationSupported(context.Operation))
        {
            _logger.LogWarning(
                "Unknown operation '{Operation}' for user {UserId} on resource {ResourceId}. Denying access.",
                context.Operation, context.UserId, context.ResourceId);

            return Task.FromResult(new RuleResult
            {
                Decision = AuthorizationDecision.Deny,
                ReasonCode = "sdap.access.deny.unknown_operation"
            });
        }

        // Check if user has required rights
        if (OperationAccessPolicy.HasRequiredRights(snapshot.AccessRights, context.Operation))
        {
            var requiredRights = OperationAccessPolicy.GetRequiredRights(context.Operation);

            _logger.LogDebug(
                "User {UserId} has required rights {RequiredRights} for operation {Operation} on resource {ResourceId}",
                context.UserId, requiredRights, context.Operation, context.ResourceId);

            return Task.FromResult(new RuleResult
            {
                Decision = AuthorizationDecision.Allow,
                ReasonCode = $"sdap.access.allow.operation.{context.Operation}"
            });
        }

        // User lacks required rights - calculate what's missing
        var missing = OperationAccessPolicy.GetMissingRights(snapshot.AccessRights, context.Operation);
        var required = OperationAccessPolicy.GetRequiredRights(context.Operation);

        _logger.LogWarning(
            "User {UserId} lacks required rights for operation {Operation} on resource {ResourceId}. " +
            "Required: {RequiredRights}, Has: {UserRights}, Missing: {MissingRights}",
            context.UserId, context.Operation, context.ResourceId,
            required, snapshot.AccessRights, missing);

        return Task.FromResult(new RuleResult
        {
            Decision = AuthorizationDecision.Deny,
            ReasonCode = "sdap.access.deny.insufficient_rights"
        });
    }
}
