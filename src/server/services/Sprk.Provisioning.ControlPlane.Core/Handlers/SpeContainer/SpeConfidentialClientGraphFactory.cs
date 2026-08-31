// -----------------------------------------------------------------------------
// SpeConfidentialClientGraphFactory.cs
//
// Shared T6 cert-loading + ClientCertificateCredential/GraphServiceClient
// construction helper for GraphContainerProvisioner + GraphAppOnlyContainerVerifier
// (H8) and for H13's T6SpeConfidentialClientTrapProbe /
// GraphContainerTypesListAppOnlyProbe. Extracted per CLAUDE.md §11 (one
// component that works exceptionally well, not two that partially overlap):
// all four call sites need the IDENTICAL cert-bootstrap + confidential-client
// credential recipe.
//
// TASK 214 CHANGE (2026-08-30): moved from Handlers/SpeContainerType/ to
// Handlers/SpeContainer/ + namespace renamed. Logic is unchanged — the KV
// cert-bootstrap mechanism, EphemeralKeySet posture, ClientCertificateCredential
// construction, and T6 delegated-token-trap phrase detection are IDENTICAL to
// the pre-rewrite version. H13's E2EAcceptance probes consume this exactly as
// before (only their `using` statements needed updating).
//
// T6 CERT-FROM-KV MECHANISM (ground-truthed against
// scripts/common/Get-SpeConfidentialClientToken.ps1, the helper both
// scripts/Create-NewContainerType.ps1 and scripts/Get-SpeContainerMetadata-
// AppOnly.ps1 already dot-source):
//   The cert is stored in Key Vault as a SECRET (base64-encoded PFX text),
//   NOT as a Key Vault Certificate object. Azure.Security.KeyVault.Secrets.
//   SecretClient.GetSecretAsync is used; .Value is base64-decoded to raw PFX
//   bytes, then loaded via the .NET 9+/10 recommended
//   <see cref="X509CertificateLoader.LoadPkcs12(byte[], string?, X509KeyStorageFlags)"/>
//   (NOT the SYSLIB0057-obsolete X509Certificate2(byte[], string, flags)
//   constructor) with <see cref="X509KeyStorageFlags.EphemeralKeySet"/> —
//   parity with the PS helper's private-key-never-persisted-to-disk posture.
//
// TENANT SCOPING: Azure.Identity.ClientCertificateCredential is inherently
// single-tenant, single-app by construction (unlike DefaultAzureCredential).
// §4D I5 (explicit per-tenant scope) is satisfied by construction, not by a
// special per-call credential-refresh pattern.
// -----------------------------------------------------------------------------

using System.Security.Cryptography.X509Certificates;
using Azure.Core;
using Azure.Identity;
using Azure.Security.KeyVault.Secrets;
using Microsoft.Graph;
using Microsoft.Graph.Models.ODataErrors;

namespace Sprk.Provisioning.ControlPlane.Handlers.SpeContainer;

/// <summary>
/// Shared T6 cert-bootstrap + confidential-client Graph client construction
/// for H8's Graph SDK collaborators AND H13's E2EAcceptance T6 probes.
/// Internal — consumed only within Sprk.Provisioning.ControlPlane.Core;
/// <c>InternalsVisibleTo Sprk.Provisioning.ControlPlane.Tests</c> exposes it
/// to the unit test project for the cert-path tests.
/// </summary>
internal static class SpeConfidentialClientGraphFactory
{
    private static readonly string[] GraphDefaultScope = { "https://graph.microsoft.com/.default" };

    /// <summary>
    /// T6 regression-detector phrase — parity with the historical PS-script-based
    /// stdout scan. Under exclusive ClientCertificateCredential usage this
    /// should never legitimately fire for container CREATE/GET; it exists as a
    /// defense-in-depth regression signal for H13's T6 acceptance gate.
    /// </summary>
    internal const string DelegatedTokenTrapPhrase = "public client not allowed";

    /// <summary>
    /// Downloads the T6 cert (base64-PFX Key Vault SECRET — see file header)
    /// and loads it as an <see cref="X509Certificate2"/> with the private key
    /// resident only in memory (<see cref="X509KeyStorageFlags.EphemeralKeySet"/>).
    /// Caller owns disposal.
    /// </summary>
    internal static async Task<X509Certificate2> LoadCertificateAsync(
        TokenCredential sharedCredential,
        SecretClientOptions? clientOptions,
        string vaultName,
        string certSecretName,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var vaultUri = new Uri($"https://{vaultName}.vault.azure.net/");
        var client = clientOptions is null
            ? new SecretClient(vaultUri, sharedCredential)
            : new SecretClient(vaultUri, sharedCredential, clientOptions);

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeout);

        Azure.Response<KeyVaultSecret> response;
        try
        {
            response = await client.GetSecretAsync(certSecretName, version: null, timeoutCts.Token)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException(
                $"T6 cert secret '{certSecretName}' read from vault '{vaultName}' timed out after {timeout}.");
        }

        var base64Pfx = response.Value.Value;
        if (string.IsNullOrWhiteSpace(base64Pfx))
        {
            throw new InvalidOperationException(
                $"T6 cert secret '{certSecretName}' on vault '{vaultName}' is present but blank.");
        }

        byte[] pfxBytes;
        try
        {
            pfxBytes = Convert.FromBase64String(base64Pfx);
        }
        catch (FormatException ex)
        {
            throw new InvalidOperationException(
                $"T6 cert secret '{certSecretName}' on vault '{vaultName}' is not valid base64 PFX " +
                $"(expected contentType application/x-pkcs12, base64-encoded — same shape " +
                "scripts/common/Get-SpeConfidentialClientToken.ps1 already reads).", ex);
        }

        // EphemeralKeySet: private key resident in-process memory only — never
        // written to the machine/user cert store. Parity with the PS helper's
        // -EphemeralKeySet flag.
        var cert = X509CertificateLoader.LoadPkcs12(pfxBytes, password: null, X509KeyStorageFlags.EphemeralKeySet);
        if (!cert.HasPrivateKey)
        {
            cert.Dispose();
            throw new InvalidOperationException(
                $"T6 cert loaded from vault '{vaultName}' secret '{certSecretName}' has no private key — " +
                "cannot build a ClientCertificateCredential from it.");
        }

        return cert;
    }

    /// <summary>
    /// Builds the confidential-client (app-only, T6) credential — NEVER a
    /// secret-based credential. The certificate's private key is used to sign
    /// the client assertion (RS256, x5t header) per RFC 7523, identical to the
    /// PS helper's manual JWT construction, but performed by
    /// Azure.Identity/MSAL instead of hand-rolled code.
    /// </summary>
    internal static ClientCertificateCredential BuildCredential(
        string tenantId, string clientAppId, X509Certificate2 certificate)
        => new(tenantId, clientAppId, certificate);

    /// <summary>Builds a Graph client bound to the T6 confidential-client credential.</summary>
    internal static GraphServiceClient BuildGraphClient(
        string tenantId, string clientAppId, X509Certificate2 certificate)
        => new(BuildCredential(tenantId, clientAppId, certificate), GraphDefaultScope);

    /// <summary>
    /// T6 regression detector — checks a Graph ODataError for the delegated-
    /// token trap signature (case-insensitive). H13's T6SpeConfidentialClientTrapProbe
    /// consumes this to classify a probe result as "trap manifested" vs
    /// "generic error." H8-B does NOT call this (per task 214.4 Option A —
    /// H8's new shape doesn't participate in T6-trap detection).
    /// </summary>
    internal static bool IsDelegatedTokenTrapError(ODataError ex)
    {
        var message = ex.Error?.Message ?? ex.Message ?? string.Empty;
        return message.Contains(DelegatedTokenTrapPhrase, StringComparison.OrdinalIgnoreCase);
    }
}
