// -----------------------------------------------------------------------------
// ArmKeyVaultRefProbe.cs
//
// Production <see cref="IArmKeyVaultRefProbe"/> — task 123 (Wave G-2) SDK
// port of the retired <c>AzCliArmKeyVaultRefProbe</c> (shelled out to
// `az webapp show` / `az webapp deployment slot show`). Reads
// <c>keyVaultReferenceIdentity</c> on the customer BFF App Service's
// production + staging slots via Azure.ResourceManager.AppService — the T1
// silent-fail trap post-condition H2a owns (design.md §4B).
//
// H4 (task 125) REUSES this class via the SAME IArmKeyVaultRefProbe DI
// registration (Worker/Program.cs H4 registration comment: "single source of
// truth for the T1 trap") — the interface + registration key are unchanged
// by this port, so H4's consumption is transparent.
// -----------------------------------------------------------------------------

using Azure.ResourceManager;
using Azure.ResourceManager.AppService;

namespace Sprk.Provisioning.ControlPlane.Handlers.BicepInfraDeploy;

/// <summary>
/// Reads <c>keyVaultReferenceIdentity</c> on the customer App Service's
/// production + staging slots via <see cref="WebSiteResource"/> /
/// <see cref="WebSiteSlotResource"/>. Constructed with an <see cref="ArmClient"/>
/// so tests can inject one built against a fake HTTP transport — the ARM
/// SDK's own request/response marshaling runs unmodified, only the HTTP
/// boundary is faked (parity with <c>ArmSubscriptionReadinessProbe</c>, task 121).
/// </summary>
public sealed class ArmKeyVaultRefProbe : IArmKeyVaultRefProbe
{
    private readonly ArmClient _armClient;
    private readonly ILogger<ArmKeyVaultRefProbe> _logger;

    /// <summary>Constructs the probe. Production DI reuses the shared UAMI-pinned ArmClient factory pattern.</summary>
    public ArmKeyVaultRefProbe(ArmClient armClient, ILogger<ArmKeyVaultRefProbe> logger)
    {
        ArgumentNullException.ThrowIfNull(armClient);
        ArgumentNullException.ThrowIfNull(logger);
        _armClient = armClient;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<ArmKeyVaultRefProbeResult> VerifyKeyVaultReferenceIdentityAsync(
        ArmKeyVaultRefProbeInput input,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentException.ThrowIfNullOrWhiteSpace(input.SubscriptionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(input.ResourceGroupName);
        ArgumentException.ThrowIfNullOrWhiteSpace(input.AppServiceName);
        ArgumentException.ThrowIfNullOrWhiteSpace(input.StagingSlotName);

        var siteResourceId = WebSiteResource.CreateResourceIdentifier(
            input.SubscriptionId, input.ResourceGroupName, input.AppServiceName);
        var siteResource = _armClient.GetWebSiteResource(siteResourceId);

        // RequestFailedException is intentionally NOT caught here — it is a
        // genuine infra fault (per IArmKeyVaultRefProbe's contract: "only
        // genuine infra faults throw so the handler can classify per §4C")
        // and H2aBicepInfraDeployHandler's own catch around this probe call
        // already logs with fuller context (runId + customerId); a
        // catch-log-rethrow here would just duplicate that log entry at a
        // lower information level.
        var prodResponse = await siteResource.GetAsync(cancellationToken).ConfigureAwait(false);
        var prodIdentity = NormalizeIdentity(prodResponse.Value.Data.KeyVaultReferenceIdentity);

        var stagingResponse = await siteResource
            .GetWebSiteSlotAsync(input.StagingSlotName, cancellationToken)
            .ConfigureAwait(false);
        var stagingIdentity = NormalizeIdentity(stagingResponse.Value.Data.KeyVaultReferenceIdentity);

        var expected = input.ExpectedUserAssignedIdentityResourceId;
        var prodMatch = string.Equals(prodIdentity, expected, StringComparison.OrdinalIgnoreCase);
        var stagingMatch = string.Equals(stagingIdentity, expected, StringComparison.OrdinalIgnoreCase);

        _logger.LogInformation(
            "T1 probe: appService={AppService} prodMatch={ProdMatch} stagingMatch={StagingMatch}",
            input.AppServiceName, prodMatch, stagingMatch);

        return prodMatch && stagingMatch
            ? new ArmKeyVaultRefProbeResult.Match()
            : new ArmKeyVaultRefProbeResult.Mismatch(prodIdentity, stagingIdentity);
    }

    /// <summary>
    /// ARM returns an empty string (not null) when <c>keyVaultReferenceIdentity</c>
    /// is unset — normalize to null so the comparison + diagnostic mirror the
    /// retired az-CLI probe's "None"/blank handling exactly.
    /// </summary>
    private static string? NormalizeIdentity(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value;
}
