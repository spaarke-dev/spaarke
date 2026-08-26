// -----------------------------------------------------------------------------
// SpeConfidentialClientGraphFactory.cs
//
// Task 131 (Wave G-3) — shared T6 cert-loading + ClientCertificateCredential/
// GraphServiceClient construction helper for GraphContainerTypeProvisioner +
// GraphAppOnlyContainerVerifier. Extracted per CLAUDE.md §11 (one component
// that works exceptionally well, not two that partially overlap) — BOTH new
// H8 collaborators need the IDENTICAL cert-bootstrap + confidential-client
// credential recipe; duplicating it would violate the reuse-over-new rule.
//
// T6 CERT-FROM-KV MECHANISM (ground-truthed against
// scripts/common/Get-SpeConfidentialClientToken.ps1, the helper both
// scripts/Create-NewContainerType.ps1 and scripts/Get-SpeContainerMetadata-
// AppOnly.ps1 already dot-source):
//   The cert is stored in Key Vault as a SECRET (base64-encoded PFX text),
//   NOT as a Key Vault Certificate object. The PS helper uses
//   `az keyvault secret download --encoding base64` (decodes base64 -> raw
//   PFX bytes written to a scratch file), then
//   `X509Certificate2::new(path, '', EphemeralKeySet)`.
//
//   Azure.Security.KeyVault.CERTIFICATES' CertificateClient was considered
//   (per task directive) and REJECTED: CertificateClient only exposes
//   metadata + the PUBLIC certificate (DownloadCertificateAsync never returns
//   private-key material — this is a KV platform constraint, not an SDK gap).
//   The PRIVATE KEY needed to build a ClientCertificateCredential is only
//   obtainable via the PAIRED SECRET (same name), exactly as the already-
//   hardened PS helper does. SecretClient (already used by task 125/130's KV
//   writers/readers) is therefore the CORRECT — and only — typed client for
//   this read; Azure.Security.KeyVault.Secrets.SecretClient.GetSecretAsync is
//   used, .Value is base64-decoded to raw PFX bytes, then loaded via the
//   .NET 9+/10 recommended <see cref="X509CertificateLoader.LoadPkcs12(byte[], string?, X509KeyStorageFlags)"/>
//   (NOT the SYSLIB0057-obsolete X509Certificate2(byte[], string, flags)
//   constructor) with <see cref="X509KeyStorageFlags.EphemeralKeySet"/> —
//   parity with the PS helper's private-key-never-persisted-to-disk posture.
//
// TENANT SCOPING (parity with GraphAppRegistrationProvisioner.cs GOTCHA 1 /
// GraphRestAppRoleGranter.cs — H10): unlike H3/H10's DefaultAzureCredential
// (which needs a FRESH instance per tenant because it has no per-request
// tenant override), Azure.Identity.ClientCertificateCredential is ALREADY
// constructed with an explicit tenantId + clientId at the call site — it is
// inherently single-tenant, single-app by construction. No equivalent gotcha
// applies here; §4D I5 (explicit per-tenant scope) is satisfied by
// construction, not by a special per-call credential-refresh pattern.
// -----------------------------------------------------------------------------

using System.Security.Cryptography.X509Certificates;
using Azure.Core;
using Azure.Identity;
using Azure.Security.KeyVault.Secrets;
using Microsoft.Graph;
using Microsoft.Graph.Models.ODataErrors;

namespace Sprk.Provisioning.ControlPlane.Handlers.SpeContainerType;

/// <summary>
/// Shared T6 cert-bootstrap + confidential-client Graph client construction
/// for H8's Graph SDK collaborators. Internal — consumed only within this
/// folder; <c>InternalsVisibleTo Sprk.Provisioning.ControlPlane.Tests</c>
/// (Core.csproj) exposes it to the unit test project for the AC#3 cert-path
/// test (constructs a <see cref="ClientCertificateCredential"/> from a fake-KV-
/// sourced certificate, asserts the credential TYPE — never a secret-based
/// credential).
/// </summary>
internal static class SpeConfidentialClientGraphFactory
{
    private static readonly string[] GraphDefaultScope = { "https://graph.microsoft.com/.default" };

    /// <summary>
    /// T6 regression-detector phrase — parity with the retired PS-script-based
    /// collaborators' identical check (CreateNewContainerTypeScriptProvisioner.cs
    /// / SpeContainerAppOnlyVerifier.cs). Under exclusive ClientCertificateCredential
    /// usage this should never legitimately fire; it exists as a defense-in-depth
    /// regression signal, not a routine code path.
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
    /// secret-based credential. The certificate's private key is
    /// used to sign the client assertion (RS256, x5t header) per RFC 7523,
    /// identical to the PS helper's manual JWT construction, but performed by
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
    /// token trap signature (case-insensitive), parity with the retired
    /// scripts' stdout-scanning check.
    /// </summary>
    internal static bool IsDelegatedTokenTrapError(ODataError ex)
    {
        var message = ex.Error?.Message ?? ex.Message ?? string.Empty;
        return message.Contains(DelegatedTokenTrapPhrase, StringComparison.OrdinalIgnoreCase);
    }
}
