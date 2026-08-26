// -----------------------------------------------------------------------------
// ArmSecretFreeMarkerApplier.cs
//
// Row A38a (task 205a, 2026-08-25) — production ISecretFreeMarkerApplier.
//
// TAG HALF — check-then-apply (idempotent):
//   Reads the vault's current tags via ArmClient GenericResource (the core
//   Azure.ResourceManager package already referenced by this project — NO new
//   Azure.ResourceManager.KeyVault PackageReference needed for a single tag
//   operation; §11 minimal-dependency choice) and only issues AddTag when the
//   `spaarke-secret-free-identity=true` tag is absent. A re-run is a no-op
//   read.
//
// REGISTRY HALF — value-idempotent PATCH:
//   Resolves the environment row via IDataverseEnvironmentRegistryClient.
//   LookupByTenantIdAsync (sprk_tenantid is the registry's unique key; H4/
//   H4-shared always carry tenantId per §4D I1), then PATCHes
//   sprk_credentialmode="secret-free" via UpdateCredentialModeAsync (A38a
//   extension of the task-112 registry client — reuse over duplicating the
//   Path-X MI-native Dataverse plumbing per CLAUDE.md §11). PATCHing the same
//   value twice yields the same row state. A missing registry row is a
//   FAIL-LOUD Failure (silence is the anti-pattern §9.1 exists to prevent).
//   NOTE (schema prerequisite, coord item on row A38a): the
//   sprk_credentialmode column (single-line text) must exist on the admin
//   env's sprk_dataverseenvironment table BEFORE any environment sets
//   RequireSecretFreeIdentity=true; a missing column surfaces as a loud
//   Failure diagnostic from the PATCH (HTTP 400 naming the property).
//
// CLEARTEXT: no secret values exist anywhere near this class — tags + a
// non-secret mode string only (ADR-028 posture preserved trivially).
//
// NOT under CI unit test as a live path (real ARM + real Dataverse) — the
// idempotency DECISION is covered as a pure function (IsVaultTagAlreadyApplied)
// and the handler-side behavior via stub appliers, parity with the
// DataverseEnvironmentRegistryClient "shape logic as pure functions" posture.
// -----------------------------------------------------------------------------

using Azure;
using Azure.Core;
using Azure.ResourceManager;
using Sprk.Provisioning.ControlPlane.Registry;

namespace Sprk.Provisioning.ControlPlane.Handlers.KvSecretsPopulation;

/// <inheritdoc cref="ISecretFreeMarkerApplier"/>
public sealed class ArmSecretFreeMarkerApplier : ISecretFreeMarkerApplier
{
    private readonly ArmClient _armClient;
    private readonly IDataverseEnvironmentRegistryClient _registryClient;
    private readonly ILogger<ArmSecretFreeMarkerApplier> _logger;

    /// <summary>Constructs the applier over the shared UAMI-pinned credential's ArmClient + the task-112 registry client.</summary>
    public ArmSecretFreeMarkerApplier(
        ArmClient armClient,
        IDataverseEnvironmentRegistryClient registryClient,
        ILogger<ArmSecretFreeMarkerApplier> logger)
    {
        ArgumentNullException.ThrowIfNull(armClient);
        ArgumentNullException.ThrowIfNull(registryClient);
        ArgumentNullException.ThrowIfNull(logger);
        _armClient = armClient;
        _registryClient = registryClient;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<SecretFreeMarkerApplyOutcome> ApplyAsync(
        SecretFreeMarkerApplyRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.SubscriptionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ResourceGroupName);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.KeyVaultName);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.TenantId);

        // ---- (1) KV resource tag — check-then-apply (idempotent). ----
        bool tagAlreadyPresent;
        try
        {
            var vaultRid = new ResourceIdentifier(
                $"/subscriptions/{request.SubscriptionId}/resourceGroups/{request.ResourceGroupName}" +
                $"/providers/Microsoft.KeyVault/vaults/{request.KeyVaultName}");
            var vault = _armClient.GetGenericResource(vaultRid);
            var current = await vault.GetAsync(cancellationToken).ConfigureAwait(false);
            tagAlreadyPresent = IsVaultTagAlreadyApplied(current.Value.Data.Tags);

            if (tagAlreadyPresent)
            {
                _logger.LogInformation(
                    "A38a marker: vault tag {Tag} already present on '{Vault}' — idempotent no-op " +
                    "(customerId={CustomerId} runId={RunId})",
                    SecretFreeMarker.VaultTagName, request.KeyVaultName,
                    request.CustomerIdForLog, request.RunIdForLog);
            }
            else
            {
                await vault.AddTagAsync(
                    SecretFreeMarker.VaultTagName, SecretFreeMarker.VaultTagValue, cancellationToken)
                    .ConfigureAwait(false);
                _logger.LogInformation(
                    "A38a marker: applied vault tag {Tag}={Value} to '{Vault}' " +
                    "(customerId={CustomerId} runId={RunId}) — fleet-consistency audit anchor",
                    SecretFreeMarker.VaultTagName, SecretFreeMarker.VaultTagValue,
                    request.KeyVaultName, request.CustomerIdForLog, request.RunIdForLog);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (RequestFailedException ex)
        {
            return new SecretFreeMarkerApplyOutcome.Failure(
                $"A38a marker: vault tag read/apply failed for '{request.KeyVaultName}' " +
                $"(HTTP {ex.Status} {ex.ErrorCode}). Verify the vault RID (sub/rg/name) and the " +
                "L2 UAMI's tag-write permission (Key Vault Contributor or Tag Contributor scope).");
        }
        catch (Exception ex)
        {
            return new SecretFreeMarkerApplyOutcome.Failure(
                $"A38a marker: vault tag infrastructure fault for '{request.KeyVaultName}': " +
                $"{ex.GetType().Name}: {ex.Message}");
        }

        // ---- (2) Registry state field — lookup + value-idempotent PATCH. ----
        try
        {
            var snapshot = await _registryClient
                .LookupByTenantIdAsync(request.TenantId, cancellationToken).ConfigureAwait(false);
            if (snapshot is null)
            {
                return new SecretFreeMarkerApplyOutcome.Failure(
                    $"A38a marker: no sprk_dataverseenvironment registry row found for tenantId " +
                    $"(customerId={request.CustomerIdForLog}) — cannot record sprk_credentialmode=" +
                    $"{SecretFreeMarker.CredentialModeSecretFree}. FAIL-LOUD per §9.1: an unrecorded " +
                    "secret-free migration is the §5.3 fleet-consistency gap. Create/repair the " +
                    "registry row, then resume.");
            }

            var outcome = await _registryClient.UpdateCredentialModeAsync(
                new RegistryCredentialModeUpdate(
                    EnvironmentId: snapshot.EnvironmentId,
                    CredentialMode: SecretFreeMarker.CredentialModeSecretFree,
                    CustomerIdForLog: request.CustomerIdForLog,
                    RunIdForLog: request.RunIdForLog),
                cancellationToken).ConfigureAwait(false);

            switch (outcome)
            {
                case RegistryUpdateOutcome.Success:
                    _logger.LogInformation(
                        "A38a marker: sprk_credentialmode={Mode} recorded on registry row {EnvironmentId} " +
                        "(customerId={CustomerId} runId={RunId})",
                        SecretFreeMarker.CredentialModeSecretFree, snapshot.EnvironmentId,
                        request.CustomerIdForLog, request.RunIdForLog);
                    return new SecretFreeMarkerApplyOutcome.Applied(tagAlreadyPresent);

                case RegistryUpdateOutcome.NotFound notFound:
                    return new SecretFreeMarkerApplyOutcome.Failure(
                        $"A38a marker: registry row {snapshot.EnvironmentId} vanished between lookup and " +
                        $"PATCH: {notFound.Diagnostic}");

                case RegistryUpdateOutcome.Failure failure:
                    return new SecretFreeMarkerApplyOutcome.Failure(
                        $"A38a marker: sprk_credentialmode PATCH rejected for row {snapshot.EnvironmentId}: " +
                        $"{failure.Diagnostic}");

                default:
                    return new SecretFreeMarkerApplyOutcome.Failure(
                        $"A38a marker: unrecognized registry outcome '{outcome.GetType().Name}'.");
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new SecretFreeMarkerApplyOutcome.Failure(
                $"A38a marker: registry half infrastructure fault (customerId={request.CustomerIdForLog}): " +
                $"{ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>
    /// Pure idempotency decision for the tag half — exposed internal so the
    /// check-then-apply contract ("2nd invocation is a no-op") is unit-tested
    /// without a live ARM call. Value comparison is case-insensitive ("True"
    /// vs "true" from portal edits must not cause a redundant write).
    /// </summary>
    internal static bool IsVaultTagAlreadyApplied(IDictionary<string, string>? tags)
        => tags is not null
           && tags.TryGetValue(SecretFreeMarker.VaultTagName, out var value)
           && string.Equals(value, SecretFreeMarker.VaultTagValue, StringComparison.OrdinalIgnoreCase);
}
