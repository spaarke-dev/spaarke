namespace Sprk.Bff.Api.Services.Notifications;

/// <summary>
/// Options for the Layer-C SignalR delivery leg (spec FR-04 / NFR-04). Bound from the
/// <c>Notifications:SignalR</c> configuration section. Drives the ADR-032 kill-switch: when
/// <see cref="IsConfigured"/> is false (no connection string OR <see cref="Enabled"/> = false),
/// <c>NotificationsModule</c> registers the Null-Object delivery service instead of the real one.
/// </summary>
/// <remarks>
/// Azure SignalR is provisioned per-customer (ADR-027); the connection string is a Key Vault
/// reference in production (NEVER plaintext in appsettings — ADR-028). Serverless mode
/// (<c>Microsoft.Azure.SignalR.Management</c>, transient transport) per the FR-01 spike decision
/// (<c>notes/spikes/fr-01-signalr-footprint.md</c>) — no hosted hub, no boot-time service
/// connection, so an absent connection string means "no live push" (poll fallback, FR-06),
/// never a startup failure.
/// </remarks>
public sealed class SignalRDeliveryOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "Notifications:SignalR";

    /// <summary>
    /// Operational kill-switch. When false, the Null-Object delivery service is registered even if a
    /// connection string is present. Defaults to true (enabled when configured).
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Azure SignalR Service connection string (Serverless / Management SDK). Key Vault reference in
    /// production (ADR-028). Absent ⇒ Null-Object registered (poll fallback, FR-06).
    /// </summary>
    public string? ConnectionString { get; set; }

    /// <summary>
    /// Logical hub name the negotiate endpoint and producers share. A single hub for the whole
    /// platform (no per-consumer hub — root CLAUDE.md "one spine" constraint). Default: "notifications".
    /// </summary>
    public string HubName { get; set; } = "notifications";

    /// <summary>
    /// True only when the feature is enabled AND a connection string is present — the condition under
    /// which the REAL delivery service (not the Null-Object) is registered.
    /// </summary>
    public bool IsConfigured => Enabled && !string.IsNullOrWhiteSpace(ConnectionString);
}
