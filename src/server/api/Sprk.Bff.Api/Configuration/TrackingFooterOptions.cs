namespace Sprk.Bff.Api.Configuration;

/// <summary>
/// Operator-managed configuration for the outbound tracking footer (FR-A1, ADR-018). Bound from the
/// <c>Communication:TrackingFooter</c> section and consumed via
/// <see cref="Microsoft.Extensions.Options.IOptionsMonitor{TOptions}"/> so an operator can flip the footer
/// on/off or edit its wording via App Service config (or Azure App Configuration / Key Vault reference)
/// WITHOUT a redeploy — the ADR-018 guarantee. Per-tenant override supported.
/// </summary>
/// <remarks>
/// <para>
/// Mirrors the <see cref="AutoFileOptions"/> global+per-tenant IOptionsMonitor shape (do not invent a new
/// config mechanism). Disabled by default: a firm opts IN to stamping outbound mail (DLP/policy control).
/// When disabled, no footer is injected and thread matching relies on the rest of the ladder (NFR-04).
/// </para>
/// <para>
/// <b>No key material here (ADR-028 / NFR-07):</b> the HMAC signing key lives in Key Vault. This config
/// carries only <see cref="SigningKeySecretName"/> — the Key Vault secret NAME. The signer (task 010)
/// resolves the key from Key Vault via <c>DefaultAzureCredential</c>; the key is never bound into config.
/// </para>
/// </remarks>
public class TrackingFooterOptions
{
    public const string SectionName = "Communication:TrackingFooter";

    /// <summary>
    /// Global footer kill-switch. <c>false</c> (default) = no footer is injected on the send path.
    /// <c>true</c> = outbound mail is stamped with <see cref="MessageTemplate"/> + a signed tracking token.
    /// Flip without a redeploy (ADR-018).
    /// </summary>
    public bool Enabled { get; set; } = false;

    /// <summary>
    /// The footer disclosure wording appended to outbound mail. Supports the <c>{record-ref}</c> (the
    /// human-readable record reference) and <c>{signed-token}</c> (the HMAC-signed tracking token)
    /// placeholders the send-path injection (task 012) fills. Editable per firm / DLP policy without a
    /// redeploy.
    /// </summary>
    public string MessageTemplate { get; set; } =
        "This message is filed with Spaarke regarding {record-ref}. Ref: {signed-token}";

    /// <summary>
    /// The Key Vault SECRET NAME of the HMAC signing key — never the key material itself (ADR-028 / NFR-07).
    /// The signer (task 010) resolves the secret from Key Vault via <c>DefaultAzureCredential</c>.
    /// </summary>
    public string SigningKeySecretName { get; set; } = string.Empty;

    /// <summary>
    /// Optional per-tenant overrides, keyed by an opaque tenant key. A present override replaces the global
    /// fields for that tenant only. Most single-org deployments never populate this and simply flip the
    /// global <see cref="Enabled"/>.
    /// </summary>
    public Dictionary<string, TrackingFooterTenantOverride> Tenants { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// Per-tenant override of the global <see cref="TrackingFooterOptions"/>. Each field is independently
/// optional: a null field falls back to the global value.
/// </summary>
public class TrackingFooterTenantOverride
{
    /// <summary>Per-tenant kill-switch override; null = inherit global <see cref="TrackingFooterOptions.Enabled"/>.</summary>
    public bool? Enabled { get; set; }

    /// <summary>Per-tenant template override; null = inherit global <see cref="TrackingFooterOptions.MessageTemplate"/>.</summary>
    public string? MessageTemplate { get; set; }

    /// <summary>
    /// Per-tenant signing-key secret-name override; null = inherit global
    /// <see cref="TrackingFooterOptions.SigningKeySecretName"/>. Still a Key Vault secret NAME, never the key.
    /// </summary>
    public string? SigningKeySecretName { get; set; }
}
