namespace Sprk.Bff.Api.Infrastructure.Startup;

/// <summary>
/// Fail-fast guard for the Azure Monitor OpenTelemetry exporter wiring in
/// <c>Program.cs</c>. Mirrors the 4-branch fail-fast pattern in
/// <see cref="Sprk.Bff.Api.Infrastructure.DI.CacheModule"/>
/// (R1 FR-03, spaarke-redis-cache-remediation-r1 task 003).
/// </summary>
/// <remarks>
/// <para>
/// Before R2 FR-06 (spaarke-redis-cache-remediation-r2 task 006), the
/// <c>UseAzureMonitor()</c> guard in <c>Program.cs</c> silently skipped
/// exporter registration when <c>APPLICATIONINSIGHTS_CONNECTION_STRING</c> was
/// missing or empty. In Production this is invisible failure: the App Insights
/// pipeline is dead, no telemetry reaches the workspace, and no one notices
/// until someone queries <c>customMetrics</c> and sees nothing.
/// </para>
/// <para>
/// R2 FR-06 changes the behavior:
/// <list type="bullet">
///   <item>Non-Development env + missing/empty conn string → throw
///   <see cref="InvalidOperationException"/> at startup with an actionable
///   message.</item>
///   <item>Development env + missing/empty conn string → return
///   <see langword="false"/> (caller skips wiring; preserves dev convenience).</item>
///   <item>Any env + non-empty conn string → return <see langword="true"/>
///   (caller wires the exporter).</item>
/// </list>
/// </para>
/// </remarks>
public static class AzureMonitorGuard
{
    /// <summary>
    /// Validates whether <c>UseAzureMonitor()</c> should be wired.
    /// </summary>
    /// <param name="environmentName">
    /// The ASP.NET Core environment name (e.g., <c>Development</c>,
    /// <c>Production</c>, <c>Staging</c>).
    /// </param>
    /// <param name="connectionString">
    /// The value of <c>APPLICATIONINSIGHTS_CONNECTION_STRING</c>, resolved from
    /// either <see cref="IConfiguration"/> or the process environment variable.
    /// May be <see langword="null"/>, empty, or whitespace.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the caller should invoke
    /// <c>UseAzureMonitor()</c>; <see langword="false"/> when it should skip
    /// (Development env only).
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when <paramref name="environmentName"/> is not
    /// <c>Development</c> and <paramref name="connectionString"/> is missing
    /// or whitespace.
    /// </exception>
    public static bool ShouldWireExporter(string environmentName, string? connectionString)
    {
        var hasConnString = !string.IsNullOrWhiteSpace(connectionString);
        var isDevelopment = string.Equals(environmentName, "Development", StringComparison.OrdinalIgnoreCase);

        if (hasConnString)
        {
            // Any env + non-empty conn string → wire the exporter.
            return true;
        }

        if (isDevelopment)
        {
            // Development env + missing conn string → skip silently (dev convenience).
            return false;
        }

        // Non-Development env + missing conn string → fail fast.
        throw new InvalidOperationException(
            "APPLICATIONINSIGHTS_CONNECTION_STRING is required in non-Development environments. " +
            $"ASPNETCORE_ENVIRONMENT={environmentName}. " +
            "Set it via App Service application settings or a Key Vault reference of the form " +
            "'@Microsoft.KeyVault(VaultName=<vault>;SecretName=<secret>)'. " +
            "Without this, the OpenTelemetry → Azure Monitor exporter is not wired, no Redis " +
            "dependency telemetry or Sprk.Bff.Api.* Meters reach App Insights, and the failure " +
            "is invisible until someone queries customMetrics and sees nothing. " +
            "See spaarke-redis-cache-remediation-r2 FR-06.");
    }
}
