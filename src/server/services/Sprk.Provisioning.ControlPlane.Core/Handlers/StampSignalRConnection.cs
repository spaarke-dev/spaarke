// -----------------------------------------------------------------------------
// StampSignalRConnection.cs
//
// ISS-022 (plan D36, owner 2026-10-14). The BFF's Notifications:SignalR settings for a stamp.
//
// A stamp's Azure SignalR Service has local auth disabled (signalr.bicep `disableLocalAuth: true`), so it has no
// access key and a key-bearing connection string can never work. The Management SDK the BFF uses
// (Microsoft.Azure.SignalR.Management) accepts a keyless managed-identity string:
//   Endpoint=https://{name}.service.signalr.net;AuthType=azure.msi;ClientId={uami client id};Version=1.0;
// https://learn.microsoft.com/azure/azure-signalr/signalr-howto-authorize-managed-identity
//
// The stamp UAMI holds "SignalR App Server" on the resource (signalr.bicep, passed `uami.outputs.principalId` by
// customer.bicep), which is what lets it acquire the access key through Entra.
//
// Both values are plain per-env settings (PerEnvSourceCatalog `from-h2a-output:signalr_msi_connection_string` and
// `signalr_enabled`) and are written ONLY when SignalR was deployed: H2a persists the host name only then, and every
// builder here returns null for a blank or malformed input, so a stamp without SignalR keeps the BFF's Null-Object
// delivery service (clients poll; ADR-032). Nothing here ever takes, builds or returns an access key.
// -----------------------------------------------------------------------------

using System.Text.RegularExpressions;

namespace Sprk.Provisioning.ControlPlane.Handlers;

/// <summary>Builds the stamp BFF's keyless SignalR connection string (ISS-022).</summary>
public static class StampSignalRConnection
{
    /// <summary>App setting carrying the connection string (BFF option <c>Notifications:SignalR:ConnectionString</c>).</summary>
    public const string ConnectionStringSettingKey = "Notifications__SignalR__ConnectionString";

    /// <summary>App setting for the BFF kill-switch (<c>Notifications:SignalR:Enabled</c>; the BFF defaults it to true).</summary>
    public const string EnabledSettingKey = "Notifications__SignalR__Enabled";

    // A DNS host name only (letters, digits, dots, hyphens): the value is spliced into a ';'-delimited string, so a
    // ';' or '=' in it would inject another connection-string field. The service host is {name}.service.signalr.net.
    private static readonly Regex HostPattern = new(
        @"^[A-Za-z0-9]([A-Za-z0-9\-]*[A-Za-z0-9])?(\.[A-Za-z0-9]([A-Za-z0-9\-]*[A-Za-z0-9])?)+$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled,
        matchTimeout: TimeSpan.FromSeconds(1));

    /// <summary>
    /// <c>Endpoint=https://{host};AuthType=azure.msi;ClientId={clientId};Version=1.0;</c>, or <c>null</c> when SignalR
    /// was not deployed (blank <paramref name="signalrHostName"/>) or an input is not a plain host / GUID.
    /// <paramref name="signalrHostName"/> may carry an <c>https://</c> prefix or a trailing slash.
    /// </summary>
    public static string? ManagedIdentityConnectionString(string? signalrHostName, string? uamiClientId)
    {
        if (string.IsNullOrWhiteSpace(signalrHostName) || string.IsNullOrWhiteSpace(uamiClientId)) return null;

        var host = signalrHostName.Trim();
        if (host.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) host = host["https://".Length..];
        host = host.TrimEnd('/');

        bool validHost;
        try { validHost = HostPattern.IsMatch(host); }
        catch (RegexMatchTimeoutException) { validHost = false; }
        if (!validHost || !Guid.TryParse(uamiClientId.Trim(), out var clientId)) return null;

        return $"Endpoint=https://{host};AuthType=azure.msi;ClientId={clientId:D};Version=1.0;";
    }

    /// <summary><c>"true"</c> when SignalR was deployed (a host name is present), else <c>null</c> (setting not written).</summary>
    public static string? EnabledValue(string? signalrHostName)
        => string.IsNullOrWhiteSpace(signalrHostName) ? null : "true";
}
