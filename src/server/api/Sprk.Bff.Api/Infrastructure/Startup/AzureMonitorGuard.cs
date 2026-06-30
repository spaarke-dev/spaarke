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
///   <item>Deployed env (Staging, Production, Demo, etc.) + missing/empty conn
///   string → throw <see cref="InvalidOperationException"/> at startup with an
///   actionable message.</item>
///   <item>Development or Testing env + missing/empty conn string → return
///   <see langword="false"/> (caller skips wiring; preserves dev convenience
///   and avoids breaking CI integration-test fixtures that don't provide a
///   real connection string).</item>
///   <item>Any env + non-empty conn string → return <see langword="true"/>
///   (caller wires the exporter).</item>
/// </list>
/// </para>
/// <para>
/// <c>Testing</c> is allow-listed alongside <c>Development</c> because it is
/// the canonical ASP.NET Core test-runtime env name (used by
/// <c>WebApplicationFactory&lt;Program&gt;</c>-based integration tests).
/// CI doesn't deploy; CI fixtures don't have an App Insights pipeline to
/// validate; throwing in <c>Testing</c> breaks every WAF-based fixture across
/// the repo with no benefit. App Service uses <c>ASPNETCORE_ENVIRONMENT=Production</c>,
/// never <c>Testing</c>, so allow-listing it does not weaken the deployed-env
/// guarantee. (Added 2026-06-29 as a follow-on to FR-06 after CI breakage
/// surfaced via PR #520.)
/// </para>
/// </remarks>
public static class AzureMonitorGuard
{
    /// <summary>
    /// Validates whether <c>UseAzureMonitor()</c> should be wired.
    /// </summary>
    /// <param name="environmentName">
    /// The ASP.NET Core environment name (e.g., <c>Development</c>,
    /// <c>Testing</c>, <c>Production</c>, <c>Staging</c>).
    /// </param>
    /// <param name="connectionString">
    /// The value of <c>APPLICATIONINSIGHTS_CONNECTION_STRING</c>, resolved from
    /// either <see cref="IConfiguration"/> or the process environment variable.
    /// May be <see langword="null"/>, empty, or whitespace.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the caller should invoke
    /// <c>UseAzureMonitor()</c>; <see langword="false"/> when it should skip
    /// (Development or Testing env only).
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when <paramref name="environmentName"/> is neither
    /// <c>Development</c> nor <c>Testing</c> and
    /// <paramref name="connectionString"/> is missing or whitespace.
    /// </exception>
    public static bool ShouldWireExporter(string environmentName, string? connectionString)
    {
        var hasConnString = !string.IsNullOrWhiteSpace(connectionString);
        var isLocalLike =
            string.Equals(environmentName, "Development", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(environmentName, "Testing", StringComparison.OrdinalIgnoreCase);

        if (hasConnString)
        {
            // Any env + non-empty conn string → wire the exporter.
            return true;
        }

        if (isLocalLike)
        {
            // Development or Testing env + missing conn string → skip silently.
            // Dev convenience for local runs; CI safety for WAF-based integration tests.
            return false;
        }

        // Deployed env (Staging, Production, Demo, etc.) + missing conn string → fail fast.
        throw new InvalidOperationException(
            "APPLICATIONINSIGHTS_CONNECTION_STRING is required in deployed environments " +
            "(Staging, Production, etc.). " +
            $"ASPNETCORE_ENVIRONMENT={environmentName}. " +
            "Set it via App Service application settings or a Key Vault reference of the form " +
            "'@Microsoft.KeyVault(VaultName=<vault>;SecretName=<secret>)'. " +
            "Without this, the OpenTelemetry → Azure Monitor exporter is not wired, no Redis " +
            "dependency telemetry or Sprk.Bff.Api.* Meters reach App Insights, and the failure " +
            "is invisible until someone queries customMetrics and sees nothing. " +
            "See spaarke-redis-cache-remediation-r2 FR-06. " +
            "(Note: Development and Testing envs are explicitly allow-listed to support local " +
            "dev and CI integration-test fixtures.)");
    }
}
