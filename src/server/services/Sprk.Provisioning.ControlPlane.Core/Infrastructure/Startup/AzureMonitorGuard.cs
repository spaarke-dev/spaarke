// -----------------------------------------------------------------------------
// AzureMonitorGuard.cs
//
// L2 CONTROL-PLANE APP INSIGHTS EXPORTER STARTUP GUARD (task 039).
//
// SPEC / ADR references:
//   - NFR-05:             Fail-fast configuration validation — a deployed L2
//                         App Service missing APPLICATIONINSIGHTS_CONNECTION_STRING
//                         MUST fail at startup, NOT silently skip telemetry
//                         wiring. Silent-skip is the r1 failure mode this
//                         guard prevents (see BFF equivalent in
//                         spaarke-redis-cache-remediation-r2 FR-06).
//   - NFR-11:             Every mutating L2 endpoint audit-logs to App Insights
//                         with actor tid. If the exporter never wires, the audit
//                         trail is dead — the guard's fail-fast is what
//                         guarantees the audit-log is reachable in production.
//   - ADR-010:            Feature-module extension composition. This guard is
//                         a pure static helper (no DI); TelemetryModule
//                         invokes it during registration.
//
// PATTERN parity with BFF `Sprk.Bff.Api.Infrastructure.Startup.AzureMonitorGuard`
// (added by spaarke-redis-cache-remediation-r2 task 006). Deliberately
// re-implemented in the L2 namespace — L2 MUST NOT reference the BFF assembly
// (project MUST rule; ADR-010 DI minimalism keeps L2 a peer service).
//
// SCOPE (task 039):
//   - Deployed env (Staging, Production, Demo, etc.) + missing conn string
//     -> throw at startup with actionable message.
//   - Development or Testing env + missing conn string -> return false
//     (caller skips wiring; dev convenience + CI-friendly for WAF-based
//     integration-test fixtures).
//   - Any env + non-empty conn string -> return true (caller wires the
//     exporter).
//
// The Testing env is allow-listed alongside Development to keep future
// WebApplicationFactory<Program>-based tests bootable without providing a
// real App Insights pipeline. App Service uses `ASPNETCORE_ENVIRONMENT=Production`
// so allow-listing Testing does not weaken the deployed-env guarantee.
// -----------------------------------------------------------------------------

namespace Sprk.Provisioning.ControlPlane.Infrastructure.Startup;

/// <summary>
/// Fail-fast guard for the Azure Monitor OpenTelemetry exporter wiring in
/// <see cref="Modules.TelemetryModule"/>. Mirrors the four-branch fail-fast
/// pattern in the BFF's <c>AzureMonitorGuard</c>
/// (spaarke-redis-cache-remediation-r2 FR-06).
/// </summary>
/// <remarks>
/// <para>
/// Without this guard, a deployed L2 App Service that is missing
/// <c>APPLICATIONINSIGHTS_CONNECTION_STRING</c> would boot successfully but with
/// the OpenTelemetry -&gt; Azure Monitor exporter silently unwired — no
/// <see cref="Middleware.AuditLogMiddleware"/> audit records, no dependency
/// telemetry, no Sprk.Provisioning.ControlPlane logs reach App Insights. The
/// failure is invisible until someone queries <c>traces</c> and sees nothing.
/// NFR-11 requires the audit trail to be reachable; NFR-05 requires fail-fast
/// on Tier-1 config misconfig — this guard is what wires the two together for
/// the telemetry path.
/// </para>
/// <para>
/// Behavior matrix:
/// <list type="bullet">
///   <item>Deployed env + missing/empty conn string -&gt;
///     <see cref="InvalidOperationException"/> at startup with actionable message.</item>
///   <item>Development or Testing env + missing/empty conn string -&gt;
///     <see langword="false"/> (caller skips wiring).</item>
///   <item>Any env + non-empty conn string -&gt; <see langword="true"/> (caller
///     wires the exporter).</item>
/// </list>
/// </para>
/// </remarks>
public static class AzureMonitorGuard
{
    /// <summary>
    /// Validates whether <c>UseAzureMonitor()</c> should be wired for the L2
    /// control plane.
    /// </summary>
    /// <param name="environmentName">
    /// The ASP.NET Core environment name (e.g., <c>Development</c>,
    /// <c>Testing</c>, <c>Production</c>, <c>Staging</c>).
    /// </param>
    /// <param name="connectionString">
    /// The value of <c>APPLICATIONINSIGHTS_CONNECTION_STRING</c>, resolved from
    /// either <see cref="Microsoft.Extensions.Configuration.IConfiguration"/> or
    /// the process environment variable. May be <see langword="null"/>, empty,
    /// or whitespace.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the caller should invoke
    /// <c>UseAzureMonitor()</c>; <see langword="false"/> when it should skip
    /// (Development or Testing env only).
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when <paramref name="environmentName"/> is neither
    /// <c>Development</c> nor <c>Testing</c> and <paramref name="connectionString"/>
    /// is missing or whitespace.
    /// </exception>
    public static bool ShouldWireExporter(string environmentName, string? connectionString)
    {
        var hasConnString = !string.IsNullOrWhiteSpace(connectionString);
        var isLocalLike =
            string.Equals(environmentName, "Development", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(environmentName, "Testing", StringComparison.OrdinalIgnoreCase);

        if (hasConnString)
        {
            // Any env + non-empty conn string -> wire the exporter.
            return true;
        }

        if (isLocalLike)
        {
            // Development or Testing env + missing conn string -> skip silently.
            // Dev convenience for local runs; CI safety for WAF-based integration tests.
            return false;
        }

        // Deployed env (Staging, Production, Demo, etc.) + missing conn string -> fail fast.
        throw new InvalidOperationException(
            "APPLICATIONINSIGHTS_CONNECTION_STRING is required in deployed environments " +
            "(Staging, Production, etc.) for the L2 control plane. " +
            $"ASPNETCORE_ENVIRONMENT={environmentName}. " +
            "Set it via App Service application settings or a Key Vault reference of the form " +
            "'@Microsoft.KeyVault(VaultName=<vault>;SecretName=<secret>)'. " +
            "Without this, the OpenTelemetry -> Azure Monitor exporter is not wired, no audit-log " +
            "records (NFR-11) or Sprk.Provisioning.ControlPlane traces reach App Insights, and " +
            "the failure is invisible until someone queries traces and sees nothing. " +
            "See customer-provisioning-orchestration-r1 spec NFR-05 (fail-fast config) + NFR-11 " +
            "(auditable operator action). (Note: Development and Testing envs are explicitly " +
            "allow-listed to support local dev and future CI integration-test fixtures.)");
    }
}
