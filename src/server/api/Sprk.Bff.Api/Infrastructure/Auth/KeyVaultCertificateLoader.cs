using System.Security.Cryptography.X509Certificates;
using Azure.Security.KeyVault.Secrets;

namespace Sprk.Bff.Api.Infrastructure.Auth;

/// <summary>
/// Loads an X.509 certificate <b>with its private key</b> from Azure Key Vault, for use as a
/// confidential-client credential (ADR-028 Amendment <b>A4</b>'s sanctioned alternative to MI-FIC).
///
/// <para><b>Why this class exists.</b> It is an extraction, not a new mechanism. The logic below was a
/// <c>private</c> instance method on <c>CiamGraphClientFactory</c>, closing over that class's own
/// <c>_secretClient</c> and <c>_certificateName</c> fields — so a second consumer could not call it.
/// Task 021 needs exactly this behaviour for the certificate branch of ordered credential selection,
/// and its constraints say both "reuse the proven <c>CiamGraphClientFactory</c> load" and "do not write
/// a second certificate loader". Those can only both hold by lifting the method to a place both callers
/// can reach. <c>CiamGraphClientFactory</c> now delegates here and is otherwise unchanged — the
/// extraction is behaviour-preserving for it by construction, because this IS its code.</para>
///
/// <para><b>The two properties that had to survive the move verbatim.</b> They are the reason the
/// original is described as proven rather than merely present:</para>
/// <list type="number">
/// <item><description><see cref="X509KeyStorageFlags.EphemeralKeySet"/> — the private key is
/// materialised in memory only and never persisted to disk. Dropping this flag would silently start
/// writing key material to the machine key store, which on a shared App Service plan is a real
/// exposure rather than a style preference.</description></item>
/// <item><description>The <see cref="FormatException"/> translation. A Key Vault <i>certificate</i>
/// surfaces its PKCS#12 through a <i>secret</i> of the same name; a plain secret with the same name
/// decodes to garbage and fails deep inside base64 decoding with a message that names neither Key Vault
/// nor the certificate. The rewrite is a genuine diagnostic and the single most likely misconfiguration
/// on this path.</description></item>
/// </list>
///
/// <para>Extracted by <c>spaarke-auth-v4-dataverse-MI</c> task 021 (FR-B2).</para>
/// </summary>
internal static class KeyVaultCertificateLoader
{
    /// <summary>
    /// Fetches <paramref name="certificateName"/> from Key Vault and materialises it as an
    /// <see cref="X509Certificate2"/> with an in-memory-only private key.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// The named secret is not a base64-encoded PFX — almost always because it is a plain Key Vault
    /// secret rather than a Key Vault certificate, or a certificate without an exportable private key.
    /// </exception>
    public static async Task<X509Certificate2> LoadAsync(
        SecretClient secretClient,
        string certificateName,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(secretClient);
        ArgumentException.ThrowIfNullOrWhiteSpace(certificateName);

        try
        {
            KeyVaultSecret secret = await secretClient
                .GetSecretAsync(certificateName, version: null, ct)
                .ConfigureAwait(false);

            var pfxBytes = Convert.FromBase64String(secret.Value);

            // EphemeralKeySet: keep the private key in memory only (no on-disk key persistence).
            // Target runtime is Linux App Service; ephemeral RSA signing is supported there and on
            // modern Windows for local dev.
            // X509CertificateLoader.LoadPkcs12 replaces the obsolete X509Certificate2(byte[], string?,
            // X509KeyStorageFlags) constructor (SYSLIB0057, .NET 9+); semantics/flags unchanged.
            return X509CertificateLoader.LoadPkcs12(pfxBytes, (string?)null, X509KeyStorageFlags.EphemeralKeySet);
        }
        catch (FormatException ex)
        {
            throw new InvalidOperationException(
                $"Key Vault secret '{certificateName}' is not a base64-encoded PFX. " +
                "Verify it is a Key Vault certificate (not a plain secret) with an exportable private key.", ex);
        }
    }
}
