using Microsoft.Extensions.Options;
using Sprk.Bff.Api.Configuration;

namespace Sprk.Bff.Api.Services.Communication.Engine;

/// <summary>
/// The ADR-018 per-tenant tracking-footer resolver (FR-A1). Resolves the effective footer settings
/// (<see cref="TrackingFooterSettings.Enabled"/> + <see cref="TrackingFooterSettings.MessageTemplate"/> +
/// <see cref="TrackingFooterSettings.SigningKeySecretName"/>) for a given tenant, re-reading
/// <see cref="IOptionsMonitor{TOptions}.CurrentValue"/> on every call so an operator's config flip takes
/// effect WITHOUT a redeploy — the ADR-018 guarantee.
/// </summary>
/// <remarks>
/// Mirrors <see cref="AutoFileGate"/> field-for-field. Registered unconditionally (ADR-010); it is pure
/// config resolution with no external dependency, so no Null-Object peer is required. When
/// <see cref="TrackingFooterSettings.Enabled"/> is false, the send-path injection (task 012) stamps no
/// footer — footer absence MUST NOT fail send (NFR-04 / ADR-045).
/// </remarks>
public sealed class TrackingFooterGate
{
    private readonly IOptionsMonitor<TrackingFooterOptions> _options;

    public TrackingFooterGate(IOptionsMonitor<TrackingFooterOptions> options)
    {
        _options = options;
    }

    /// <summary>
    /// Resolve the effective footer settings for <paramref name="tenantKey"/>. A per-tenant override (when
    /// present) replaces the global default field-by-field; a null/unknown tenant key uses the global
    /// default. Read fresh from <see cref="IOptionsMonitor{TOptions}.CurrentValue"/> each call.
    /// </summary>
    public TrackingFooterSettings Resolve(string? tenantKey)
    {
        var o = _options.CurrentValue;
        var enabled = o.Enabled;
        var template = o.MessageTemplate;
        var secretName = o.SigningKeySecretName;

        if (!string.IsNullOrWhiteSpace(tenantKey) &&
            o.Tenants.TryGetValue(tenantKey, out var overrideEntry) &&
            overrideEntry is not null)
        {
            if (overrideEntry.Enabled.HasValue) enabled = overrideEntry.Enabled.Value;
            if (overrideEntry.MessageTemplate is not null) template = overrideEntry.MessageTemplate;
            if (overrideEntry.SigningKeySecretName is not null) secretName = overrideEntry.SigningKeySecretName;
        }

        return new TrackingFooterSettings(enabled, template, secretName);
    }
}

/// <summary>Effective tracking-footer policy for one send.</summary>
/// <param name="Enabled">Whether a footer is injected on the outbound send path.</param>
/// <param name="MessageTemplate">The disclosure wording (<c>{record-ref}</c>/<c>{signed-token}</c> placeholders).</param>
/// <param name="SigningKeySecretName">The Key Vault secret name of the HMAC signing key (never the key material).</param>
public readonly record struct TrackingFooterSettings(
    bool Enabled,
    string MessageTemplate,
    string SigningKeySecretName);
