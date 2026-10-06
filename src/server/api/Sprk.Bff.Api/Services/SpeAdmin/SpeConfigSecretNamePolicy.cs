using System.Text.RegularExpressions;

namespace Sprk.Bff.Api.Services.SpeAdmin;

/// <summary>
/// THE allow-list for a SUPPLIED <c>sprk_specontainertypeconfig.sprk_keyvaultsecretname</c> (unified-access-control-r2
/// task 165, owner round 35 item 3; narrowed by main-session round 65 item 1).
/// </summary>
/// <remarks>
/// <para>
/// <b>Since round 65 the BFF reads no secret by this name.</b> SPE Admin authenticates as the BFF's own identity (master
/// <c>bb8ba7251</c>, 2026-10-04): <c>SpeAdminTokenProvider</c> and every Key Vault read of an owning-app secret are gone,
/// and the column is optional. So round 35's read guard and the filter's 409 were removed with what they guarded.
/// </para>
/// <para>
/// <b>Why the rule is still applied on write.</b> One reader of the name remains:
/// <c>scripts/Backfill-SpeContainerBusinessUnitStamp.ps1</c> fetches the named secret from the BFF's Key Vault to list a
/// config's containers as its owning app. That vault holds far more than owning-app secrets (the OpenAI key, the Redis
/// connection string, the webhook signing keys …), so a config must not be able to name any of them. Config POST/PUT
/// therefore answer 400 for a supplied name outside ONE pinned prefix, and the backfill refuses a stored one before it
/// reads the vault (its own copy of the rule, <c>scripts/common/SpeConfigSecretNamePolicy.ps1</c>).
/// </para>
/// <para>
/// <b>The prefix</b> is the existing configs' naming (<c>spe-owning-app-secret</c> on dev). A shorter <c>spe-</c> would
/// also admit <c>SPE-ContainerTypeId</c> (Key Vault names are case-insensitive). The remainder follows Key Vault's own name
/// rule (letters, digits, hyphens; 127 characters in all). The expression ends at <c>\z</c>, never at <c>$</c>, which in
/// .NET also matches just before a trailing newline (owner round 41 item 5).
/// </para>
/// </remarks>
public static class SpeConfigSecretNamePolicy
{
    /// <summary>The ONE pinned prefix every owning-app secret name must start with (compared case-insensitively).</summary>
    public const string RequiredPrefix = "spe-owning-app-";

    /// <summary>The reason code of the refusal under this rule (the 400 on config POST/PUT).</summary>
    public const string NotAllowedReasonCode = "spe.admin.deny.config_secret_name_not_allowed";

    /// <summary>Key Vault's own limit on a secret name.</summary>
    private const int KeyVaultNameMaxLength = 127;

    private static readonly Regex Allowed = new(
        "^" + Regex.Escape(RequiredPrefix) + "[A-Za-z0-9-]{1," + (KeyVaultNameMaxLength - RequiredPrefix.Length) + @"}\z",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase,
        TimeSpan.FromMilliseconds(100));

    /// <summary>Whether <paramref name="secretName"/> may be resolved. Blank, untrimmed and foreign names may not.</summary>
    public static bool IsAllowed(string? secretName) =>
        !string.IsNullOrEmpty(secretName) && Allowed.IsMatch(secretName);
}
