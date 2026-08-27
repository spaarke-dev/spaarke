// -----------------------------------------------------------------------------
// ArmOperatorKvRbacBootstrapper.cs
//
// HANDLER-09 (Wave 2 pre-dispatch remediation 2026-08-27) — F15 + F18 verbatim.
// Production <see cref="IOperatorKvRbacBootstrapper"/> impl. Wave 2 scaffold:
// logs the intended grant + returns Success unconditionally. The LIVE
// Azure.ResourceManager.Authorization PUT (or `az rest --method put` per
// F15b fallback) lands in a follow-on incremental change. Operator manually
// grants "Key Vault Secrets Officer" on both KVs once until the incremental
// change lands (SESSION 2 procedure verbatim).
//
// See sibling <see cref="PacRequiredApplicationsInstaller"/> file header
// for the same scaffold-to-production trajectory rationale.
// -----------------------------------------------------------------------------

namespace Sprk.Provisioning.ControlPlane.Handlers.KvSecretsPopulation;

/// <summary>
/// Production <see cref="IOperatorKvRbacBootstrapper"/> — Wave 2 scaffold.
/// </summary>
public sealed class ArmOperatorKvRbacBootstrapper : IOperatorKvRbacBootstrapper
{
    private readonly ILogger<ArmOperatorKvRbacBootstrapper> _logger;

    /// <summary>Constructs the bootstrapper.</summary>
    public ArmOperatorKvRbacBootstrapper(ILogger<ArmOperatorKvRbacBootstrapper> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
    }

    /// <inheritdoc/>
    public Task<OperatorKvRbacBootstrapOutcome> EnsureGrantedAsync(
        OperatorKvRbacBootstrapRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        _logger.LogInformation(
            "HANDLER-09 scaffold: KV RBAC bootstrap requested for vault '{Vault}' principal '{Principal}' role '{Role}'. " +
            "Wave 2 scaffold returns Success — operator must manually grant Key Vault Secrets Officer on the vault " +
            "(SESSION 2 procedure) until the incremental change lands.",
            request.KeyVaultName, request.PrincipalObjectId, request.RoleDefinitionId);

        return Task.FromResult<OperatorKvRbacBootstrapOutcome>(
            new OperatorKvRbacBootstrapOutcome.Success(WasFreshlyGranted: false));
    }
}
