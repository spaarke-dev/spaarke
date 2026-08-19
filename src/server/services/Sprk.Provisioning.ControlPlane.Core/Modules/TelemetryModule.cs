// -----------------------------------------------------------------------------
// TelemetryModule.cs
//
// L2 CONTROL-PLANE TELEMETRY composition (task 039).
//
// SPEC / ADR references:
//   - spec.md NFR-11:     Every mutating L2 endpoint audit-logs to App Insights
//                         with actor tid. This module wires the pipeline; the
//                         audit-log emitter is
//                         Middleware/AuditLogMiddleware.cs. Without this
//                         wiring the emitter's structured logs never reach
//                         Azure Monitor -> the audit trail is dead.
//   - spec.md §4.3a.2:    Operator authenticates via own AAD identity
//                         (`az login`), NOT a service principal. The actor
//                         claims (tid, oid, roles) flow into the audit trail
//                         emitted by AuditLogMiddleware -> customer support +
//                         compliance queries pivot on them.
//   - spec.md NFR-05:     Fail-fast configuration validation. Deployed L2 App
//                         Service missing APPLICATIONINSIGHTS_CONNECTION_STRING
//                         throws at startup via AzureMonitorGuard.
//   - ADR-010:            Feature-module extension method keeps Program.cs at
//                         ~15 non-framework DI lines (NFR-07 god-class ratchet).
//   - ADR-032:            UNCONDITIONAL registration — the audit trail is a
//                         compliance requirement, NOT a feature flag. No
//                         `if (flag)` branch inside this module. The exporter
//                         wiring IS conditional on environment + config
//                         presence via AzureMonitorGuard, but that is a
//                         fail-fast pre-condition, not a feature gate — dev
//                         + testing envs skip silently (no telemetry loss in
//                         local runs / no CI-fixture breakage), deployed envs
//                         throw on missing config (invisible-failure prevention).
//
// PATTERN parity with BFF Program.cs lines 31-36 (OpenTelemetry -> Azure
// Monitor via UseAzureMonitor() behind AzureMonitorGuard). Deliberately
// does NOT reference the BFF assembly (project MUST rule — L2 is a PEER
// service, not a BFF extension).
//
// SDK NOTE (D-036-1 deviation carried forward): the L2 project references
// `Azure.Monitor.OpenTelemetry.AspNetCore 1.6.0` (added by task 036 in place
// of the classic `Microsoft.ApplicationInsights.AspNetCore` SDK named in the
// task 039 POML). Task 036 D-036-1 documented this Path-A exception: the
// classic SDK was retired from the BFF by dotnet-10-upgrade-r1 task 014
// (FR-06); aligning L2 with the ecosystem-canonical modern telemetry package
// from birth avoids an immediate rework backlog item. This task honors that
// deviation by wiring via `UseAzureMonitor()` — NOT the classic
// `AddApplicationInsightsTelemetry()` -> `TelemetryClient.TrackEvent(...)`
// pattern from the POML step 4. The audit emitter uses structured `ILogger`
// calls which flow through the OTel Logs pipeline into Azure Monitor's
// `traces` table with structured properties; the event-name discriminator
// stays queryable via `traces | where message startswith "AuditableAction"`.
//
// SCOPE (task 039):
//   - AzureMonitorGuard.ShouldWireExporter(env, connString) fail-fast check.
//   - services.AddOpenTelemetry().UseAzureMonitor() when the guard permits.
//   - NO custom sampling policy — Azure Monitor exporter defaults to 100%
//     sampling in v1.6.0 (adaptive sampling is opt-in, not default). Audit
//     records (NFR-11) never sampled by construction.
//   - NO service registrations beyond the OTel wiring — the audit emitter
//     uses ILogger which is registered by the framework.
// -----------------------------------------------------------------------------

using Azure.Monitor.OpenTelemetry.AspNetCore;
using Sprk.Provisioning.ControlPlane.Infrastructure.Startup;

namespace Sprk.Provisioning.ControlPlane.Modules;

/// <summary>
/// DI registration for the L2 control-plane OpenTelemetry -&gt; Azure Monitor
/// pipeline. Wires the exporter behind
/// <see cref="AzureMonitorGuard.ShouldWireExporter"/> so a deployed L2 App
/// Service missing <c>APPLICATIONINSIGHTS_CONNECTION_STRING</c> throws at
/// startup (NFR-05) while Development / Testing envs skip silently.
/// </summary>
public static class TelemetryModule
{
    /// <summary>
    /// Configuration key carrying the App Insights connection string. Matches
    /// the BFF convention and the Azure App Service canonical env-var name so
    /// KV references of the form
    /// <c>@Microsoft.KeyVault(VaultName=...;SecretName=...)</c> flow through
    /// unchanged.
    /// </summary>
    public const string ConnectionStringConfigKey = "APPLICATIONINSIGHTS_CONNECTION_STRING";

    /// <summary>
    /// Wires the OpenTelemetry -&gt; Azure Monitor exporter for the L2
    /// control plane when a connection string is present, or throws at startup
    /// when a deployed env is missing it (NFR-05 fail-fast contract).
    /// </summary>
    /// <param name="services">Service collection.</param>
    /// <param name="configuration">
    /// Bound configuration. Reads <see cref="ConnectionStringConfigKey"/>
    /// (falls back to the process environment variable so App Service
    /// KV references + local <c>set APPLICATIONINSIGHTS_CONNECTION_STRING=...</c>
    /// both work).
    /// </param>
    /// <param name="environmentName">
    /// The ASP.NET Core environment name (e.g., <c>Development</c>,
    /// <c>Testing</c>, <c>Production</c>). Passed through to
    /// <see cref="AzureMonitorGuard.ShouldWireExporter"/> for the fail-fast
    /// vs skip-silently decision.
    /// </param>
    /// <returns>The same service collection for chaining.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown at startup by <see cref="AzureMonitorGuard.ShouldWireExporter"/>
    /// when <paramref name="environmentName"/> is a deployed env (neither
    /// <c>Development</c> nor <c>Testing</c>) and the connection string is
    /// missing or whitespace — NFR-05 fail-fast contract for the audit-trail
    /// pipeline (NFR-11).
    /// </exception>
    public static IServiceCollection AddTelemetryModule(
        this IServiceCollection services,
        IConfiguration configuration,
        string environmentName)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(environmentName);

        // Read from IConfiguration first (App Service application settings +
        // KV references land here), fall back to the process env var so an
        // operator running `dotnet run` locally can `set APPLICATIONINSIGHTS_CONNECTION_STRING=...`
        // without editing appsettings. Matches the BFF Program.cs pattern.
        var connString = configuration[ConnectionStringConfigKey]
            ?? Environment.GetEnvironmentVariable(ConnectionStringConfigKey);

        if (AzureMonitorGuard.ShouldWireExporter(environmentName, connString))
        {
            // UseAzureMonitor() wires the Logs + Traces + Metrics pipelines to
            // the App Insights resource pointed at by the connection string.
            // Audit-log records emitted by AuditLogMiddleware via
            // ILogger.LogInformation flow through the Logs pipeline to the
            // Azure Monitor `traces` table with structured properties (Actor.*,
            // HttpMethod, Path, StatusCode, TraceId). Queryable via:
            //
            //   traces
            //   | where message startswith "AuditableAction"
            //   | project timestamp, message, customDimensions
            //
            // The Azure Monitor exporter's default sampling is 100% in
            // v1.6.0 (adaptive sampling is opt-in via SamplingRatio option);
            // audit records satisfy NFR-11's "never sampled" requirement by
            // construction without a custom TelemetryProcessor.
            services.AddOpenTelemetry().UseAzureMonitor();
        }

        return services;
    }
}
