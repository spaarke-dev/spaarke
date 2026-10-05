using System.Text.RegularExpressions;

namespace Sprk.Bff.Api.Services.SpeAdmin;

/// <summary>
/// THE allow-list for <c>sprk_specontainertypeconfig.sprk_keyvaultsecretname</c> — the Key Vault secret the BFF reads,
/// app-only, to authenticate as a config's owning app (unified-access-control-r2 task 165, owner round 35 item 3).
/// </summary>
/// <remarks>
/// <para>
/// <b>Why.</b> The BFF's Key Vault holds far more than owning-app secrets (dev 2026-10-04: 19 secrets — the OpenAI key,
/// the Redis connection string, the webhook signing keys, the footer HMAC key …). Before this rule a config could name
/// ANY of them, and since round 20 item 3 the secret name is no longer exclusive to one customer, so any SPE admin who
/// can write a config could point it at another secret and have the BFF read it. Now only names under ONE pinned prefix
/// are ever resolved.
/// </para>
/// <para>
/// <b>The prefix</b> is taken from the existing configs' naming, read live and read-only on dev 2026-10-04: the one
/// working config stores <c>spe-owning-app-secret</c> (the other stores the literal <c>"null"</c>, which conforms to
/// nothing), and the SPE admin app's own placeholder is the same name. <c>spe-owning-app-</c> admits exactly that secret
/// in the dev vault; a shorter <c>spe-</c> would also admit <c>SPE-ContainerTypeId</c> (Key Vault names are
/// case-insensitive). The remainder follows Key Vault's own name rule (letters, digits, hyphens; 127 characters in all),
/// so no path or query character can ever reach the vault request.
/// </para>
/// <para>
/// <b>Where it is enforced.</b> (1) Config POST/PUT answer 400 for any other name. (2) Every configId route that uses
/// the config's credential is refused by <c>SpeAdminTenantScopeFilter</c> with 409 and <see cref="NotAllowedReasonCode"/>
/// before any handler runs. (3) At read — <c>SpeAdminGraphService.GetClientForConfigAsync</c>,
/// <c>GetClientForOwningAppAsync</c>, the register path, and <c>SpeAdminTokenProvider</c> — a non-conforming name throws
/// <see cref="SpeConfigSecretNameNotAllowedException"/> BEFORE the vault is called and before any cached client is
/// reused: the secret is never read. <c>scripts/Test-SpeConfigSecretNames.ps1 -Verify</c> lists the live configs that do
/// not conform; renaming them is an operator's manual gate.
/// </para>
/// </remarks>
public static class SpeConfigSecretNamePolicy
{
    /// <summary>The ONE pinned prefix every owning-app secret name must start with (compared case-insensitively).</summary>
    public const string RequiredPrefix = "spe-owning-app-";

    /// <summary>The reason code of every refusal under this rule (400 on write, 409 at the filter, the read guard).</summary>
    public const string NotAllowedReasonCode = "spe.admin.deny.config_secret_name_not_allowed";

    /// <summary>Key Vault's own limit on a secret name.</summary>
    private const int KeyVaultNameMaxLength = 127;

    private static readonly Regex Allowed = new(
        "^" + Regex.Escape(RequiredPrefix) + "[A-Za-z0-9-]{1," + (KeyVaultNameMaxLength - RequiredPrefix.Length) + "}$",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase,
        TimeSpan.FromMilliseconds(100));

    /// <summary>Whether <paramref name="secretName"/> may be resolved. Blank, untrimmed and foreign names may not.</summary>
    public static bool IsAllowed(string? secretName) =>
        !string.IsNullOrEmpty(secretName) && Allowed.IsMatch(secretName);

    /// <summary>
    /// Refuses (throws <see cref="SpeConfigSecretNameNotAllowedException"/>) unless <paramref name="secretName"/> may be
    /// resolved. Call BEFORE any Key Vault read and before any cached credential is reused.
    /// </summary>
    public static void EnsureAllowed(string? secretName, Guid configId)
    {
        if (!IsAllowed(secretName))
        {
            throw new SpeConfigSecretNameNotAllowedException(configId, secretName);
        }
    }
}

/// <summary>
/// A config names a Key Vault secret outside <see cref="SpeConfigSecretNamePolicy"/> — refused before the vault was
/// called (round 35 item 3). The message leads with <see cref="SpeConfigSecretNamePolicy.NotAllowedReasonCode"/>, so a
/// handler that reports it through <c>ProblemDetailsHelper.Explain</c> carries the code verbatim.
/// </summary>
public sealed class SpeConfigSecretNameNotAllowedException : InvalidOperationException
{
    /// <summary>The config whose stored secret name was refused.</summary>
    public Guid ConfigId { get; }

    /// <summary>The refused name (a NAME, never a value).</summary>
    public string? SecretName { get; }

    /// <summary>The refusal's reason code.</summary>
    public string ReasonCode => SpeConfigSecretNamePolicy.NotAllowedReasonCode;

    public SpeConfigSecretNameNotAllowedException(Guid configId, string? secretName)
        : base($"[{SpeConfigSecretNamePolicy.NotAllowedReasonCode}] Container type config '{configId}' names the Key Vault " +
               $"secret '{secretName}', which is outside the allowed '{SpeConfigSecretNamePolicy.RequiredPrefix}' prefix. " +
               "The secret was not read; an operator must rename it.")
    {
        ConfigId = configId;
        SecretName = secretName;
    }
}
