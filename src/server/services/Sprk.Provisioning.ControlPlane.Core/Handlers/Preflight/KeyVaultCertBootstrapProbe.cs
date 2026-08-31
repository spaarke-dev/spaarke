// -----------------------------------------------------------------------------
// KeyVaultCertBootstrapProbe.cs
//
// Production <see cref="IPreflightQuotaProbe"/> implementation — SDK port of
// scripts/preflight/Test-SpeCertBootstrap.ps1 (task 120, Wave G-2, Option D
// hybrid per DS-1b §1 H0 row). Replaces the `az keyvault secret show
// --vault-name <vault> --name <secret>` shell-out with
// Azure.Security.KeyVault.Secrets.SecretClient.GetSecretAsync (verified via
// reflection against the installed 4.11.0 package: SecretProperties exposes
// CreatedOn as DateTimeOffset? — the same field the PS script reads via
// attributes.created).
//
// THRESHOLD LOGIC: ported verbatim from Test-SpeCertBootstrap.ps1 (see
// <see cref="Evaluate"/>):
//   - Secret not found (404) -> Fail, "SPE cert-bootstrap MISSING".
//   - Secret found but CreatedOn missing -> Fail, "attributes shape drifted".
//   - ageHours = now - CreatedOn; Pass iff ageHours >= MinAgeHours (24h
//     default per FR-11 T6 — the Microsoft-documented SPE replication window).
//
// ADR-038 TEST-BOUNDARY DESIGN: same fake-transport pattern as
// ArmCognitiveServicesTpmProbe.cs / ArmComputeVCpuProbe.cs / task 121's
// ArmSubscriptionReadinessProbe (see that file's header for the full
// rationale). <see cref="SecretClient"/> is constructed per-call (the vault
// name is a per-run parameter, unlike ArmClient's subscription-agnostic
// construction), with an optional <see cref="SecretClientOptions"/> test
// seam so unit tests supply a fake <see cref="Azure.Core.Pipeline.HttpClientTransport"/>
// and exercise the SDK's real request/response marshaling — never a
// Mock&lt;HttpMessageHandler&gt;, never a wrapper mock of SecretClient itself.
//
// AUTH: reuses the shared <see cref="TokenCredential"/> singleton (UAMI-
// pinned per ADR-028) — targets the SPAARKE PLATFORM Key Vault (Model 2's
// per-customer stamp still lives under the platform UAMI's KV data-plane
// RBAC per H4's own posture).
// -----------------------------------------------------------------------------

using System.Text.Json;
using Azure;
using Azure.Core;
using Azure.Security.KeyVault.Secrets;

namespace Sprk.Provisioning.ControlPlane.Handlers.Preflight;

/// <summary>
/// SDK-backed <see cref="IPreflightQuotaProbe"/> for SPE cert-bootstrap
/// readiness. Reads <c>keyVaultName</c> from
/// <see cref="PreflightProbeInput.NonSecretParameters"/> (same key convention
/// as H3/H4/H8/H14's KeyVaultNameParameterKey).
/// </summary>
public sealed class KeyVaultCertBootstrapProbe : IPreflightQuotaProbe
{
    /// <summary>Run-parameter key for the target Key Vault name — matches H3/H4/H8/H14's convention.</summary>
    public const string KeyVaultNameParameterKey = "keyVaultName";

    /// <summary>Secret name — matches the T6 helper convention (Test-SpeCertBootstrap.ps1 default).</summary>
    public const string CertSecretName = "spe-owner-cert-pfx";

    /// <summary>Minimum secret age for SPE replication to have completed (FR-11 T6).</summary>
    public const int MinAgeHours = 24;

    private readonly TokenCredential _credential;
    private readonly SecretClientOptions? _clientOptions;
    private readonly ILogger<KeyVaultCertBootstrapProbe> _logger;
    private readonly TimeProvider _timeProvider;

    /// <inheritdoc/>
    public string CheckName => PreflightCheckNames.SpeCertBootstrap;

    /// <summary>Constructs the production probe bound to the shared platform <see cref="TokenCredential"/>.</summary>
    public KeyVaultCertBootstrapProbe(TokenCredential credential, ILogger<KeyVaultCertBootstrapProbe> logger)
        : this(credential, clientOptions: null, logger, TimeProvider.System)
    {
    }

    /// <summary>Test seam constructor — injects a fake-transport <see cref="SecretClientOptions"/> + <see cref="TimeProvider"/>.</summary>
    internal KeyVaultCertBootstrapProbe(
        TokenCredential credential, SecretClientOptions? clientOptions, ILogger<KeyVaultCertBootstrapProbe> logger, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(credential);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(timeProvider);
        _credential = credential;
        _clientOptions = clientOptions;
        _logger = logger;
        _timeProvider = timeProvider;
    }

    /// <inheritdoc/>
    public async Task<PreflightCheckResult> CheckAsync(PreflightProbeInput input, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);

        if (!input.NonSecretParameters.TryGetValue(KeyVaultNameParameterKey, out var keyVaultName) || string.IsNullOrWhiteSpace(keyVaultName))
        {
            return ConfigError($"Run parameter '{KeyVaultNameParameterKey}' is required by {CheckName}.");
        }

        var certUri = $"https://{keyVaultName}.vault.azure.net/secrets/{CertSecretName}";
        _logger.LogInformation(
            "{CheckName} querying Azure.Security.KeyVault.Secrets: vault={KeyVault} secret={Secret}",
            CheckName, keyVaultName, CertSecretName);

        var vaultUri = new Uri($"https://{keyVaultName}.vault.azure.net/");
        var client = _clientOptions is null
            ? new SecretClient(vaultUri, _credential)
            : new SecretClient(vaultUri, _credential, _clientOptions);

        SecretCreatedOnResult metadata;
        try
        {
            var response = await client.GetSecretAsync(CertSecretName, version: null, cancellationToken).ConfigureAwait(false);
            metadata = response.Value.Properties.CreatedOn is { } createdOn
                ? new SecretCreatedOnResult.Found(createdOn)
                : new SecretCreatedOnResult.MissingCreatedOn();
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            metadata = new SecretCreatedOnResult.NotFound();
        }

        return Evaluate(keyVaultName, CertSecretName, certUri, MinAgeHours, _timeProvider.GetUtcNow(), metadata);
    }

    /// <summary>
    /// Pure threshold-comparison logic — ported from Test-SpeCertBootstrap.ps1's
    /// age computation. Exposed internal so unit tests exercise the exact
    /// evaluation function the production path calls.
    /// </summary>
    internal static PreflightCheckResult Evaluate(
        string keyVaultName,
        string certSecretName,
        string certUri,
        int minAgeHours,
        DateTimeOffset now,
        SecretCreatedOnResult metadata)
    {
        if (metadata is SecretCreatedOnResult.NotFound)
        {
            var headroom = JsonSerializer.SerializeToElement(new Dictionary<string, object?>
            {
                ["keyVault"] = keyVaultName,
                ["secretName"] = certSecretName,
                ["certUri"] = certUri,
                ["minAgeHours"] = minAgeHours,
                ["observed"] = "not-found",
            });
            return new PreflightCheckResult(
                CheckName: PreflightCheckNames.SpeCertBootstrap,
                Passed: false,
                Headroom: headroom,
                Diagnostic:
                    $"SPE cert-bootstrap MISSING: no secret '{certSecretName}' in vault '{keyVaultName}' (URI: {certUri}). " +
                    "Run scripts/common cert-bootstrap helper OR verify H4 KV-seed handler ran cleanly. " +
                    $"Note: even after bootstrap, wait {minAgeHours}h for SPE replication before proceeding to H8.");
        }

        if (metadata is SecretCreatedOnResult.MissingCreatedOn)
        {
            var headroom = JsonSerializer.SerializeToElement(new Dictionary<string, object?>
            {
                ["keyVault"] = keyVaultName,
                ["secretName"] = certSecretName,
                ["certUri"] = certUri,
            });
            return new PreflightCheckResult(
                CheckName: PreflightCheckNames.SpeCertBootstrap,
                Passed: false,
                Headroom: headroom,
                Diagnostic:
                    $"Key Vault secret metadata for '{certSecretName}' in '{keyVaultName}' is missing CreatedOn — " +
                    "SDK response shape may have drifted. Escalate per root CLAUDE.md §6.");
        }

        var found = (SecretCreatedOnResult.Found)metadata;
        var ageHours = Math.Round((now - found.CreatedOn).TotalHours, 2);

        var headroomFound = JsonSerializer.SerializeToElement(new Dictionary<string, object?>
        {
            ["keyVault"] = keyVaultName,
            ["secretName"] = certSecretName,
            ["certUri"] = certUri,
            ["observedAgeHours"] = ageHours,
            ["requiredAgeHours"] = minAgeHours,
            ["createdUtc"] = found.CreatedOn.ToString("o"),
        });

        if (ageHours >= minAgeHours)
        {
            return new PreflightCheckResult(
                CheckName: PreflightCheckNames.SpeCertBootstrap,
                Passed: true,
                Headroom: headroomFound,
                Diagnostic: $"SPE cert-bootstrap OK: '{certSecretName}' in '{keyVaultName}' is {ageHours}h old (>= {minAgeHours}h replication requirement).");
        }

        var waitMore = Math.Ceiling(minAgeHours - ageHours);
        return new PreflightCheckResult(
            CheckName: PreflightCheckNames.SpeCertBootstrap,
            Passed: false,
            Headroom: headroomFound,
            Diagnostic:
                $"SPE cert-bootstrap NOT YET REPLICATED: '{certSecretName}' in '{keyVaultName}' is only {ageHours}h old (URI: {certUri}), " +
                $"replication requires {minAgeHours}h per FR-11 T6. Wait {waitMore}h more before proceeding, OR verify H4 cert-seed timestamp is intentional.");
    }

    private static PreflightCheckResult ConfigError(string diagnostic) => new(
        CheckName: PreflightCheckNames.SpeCertBootstrap,
        Passed: false,
        Headroom: JsonDocument.Parse("{}").RootElement.Clone(),
        Diagnostic: diagnostic);
}

/// <summary>Result of a secret-metadata lookup — mirrors the PS script's three outcome branches.</summary>
internal abstract record SecretCreatedOnResult
{
    /// <summary>Secret does not exist in the vault (404).</summary>
    public sealed record NotFound : SecretCreatedOnResult;

    /// <summary>Secret exists but its <c>CreatedOn</c> property is null — API shape drift.</summary>
    public sealed record MissingCreatedOn : SecretCreatedOnResult;

    /// <summary>Secret exists with a valid creation timestamp.</summary>
    public sealed record Found(DateTimeOffset CreatedOn) : SecretCreatedOnResult;
}

