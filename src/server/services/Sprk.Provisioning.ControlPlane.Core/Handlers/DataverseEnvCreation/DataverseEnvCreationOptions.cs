// -----------------------------------------------------------------------------
// DataverseEnvCreationOptions.cs
//
// Bound options for the H5 handler's collaborators (pac CLI creator + Web
// API health probe). Loaded from the "DataverseEnvCreationOptions"
// configuration section by Program.cs — runtime-configurable so the linux-x64
// App Service publish layout can be honored without recompiling.
// -----------------------------------------------------------------------------

namespace Sprk.Provisioning.ControlPlane.Handlers.DataverseEnvCreation;

/// <summary>
/// Bound options for <see cref="H5DataverseEnvCreationHandler"/> collaborators.
/// Configuration key: <c>DataverseEnvCreationOptions</c>.
/// </summary>
public sealed class DataverseEnvCreationOptions
{
    /// <summary>
    /// Path to the pac CLI executable. Defaults to <c>pac</c> (resolved via
    /// PATH). Parity with <see cref="Preflight.PreflightModuleOptions.PwshExecutable"/>.
    /// On Linux App Service the pac binary is installed alongside pwsh via
    /// the deployment scripts.
    /// </summary>
    public string PacCliExecutable { get; set; } = "pac";

    /// <summary>
    /// Maximum wall-clock time to wait for Dataverse environment creation to
    /// reach a terminal <c>provisioningState</c> (<c>Succeeded</c> /
    /// <c>Failed</c> / <c>Deleted</c>). Defaults to 45 minutes — env creation
    /// is documented at 15–30 min per design.md § 4.2; the ceiling absorbs
    /// slower tenants without truncating a slow-but-progressing operation.
    /// Task 140 (BAP-REST port): this bounds the ENTIRE create+poll sequence
    /// (the initial creation POST plus every subsequent async-operation-status
    /// GET against <see cref="AsyncOperationPollInterval"/>) — the pac CLI's
    /// blocking single invocation is now an explicit polling loop inside
    /// <c>BapRestEnvironmentCreator</c>. If the deadline elapses before a
    /// terminal state is observed, the creator returns
    /// <see cref="DataverseEnvCreationFailureKind.Timeout"/> which the handler
    /// maps to <see cref="DataverseEnvCreationRejectionCodes.EnvCreationTimeout"/>.
    /// </summary>
    public TimeSpan CreationTimeout { get; set; } = TimeSpan.FromMinutes(45);

    /// <summary>
    /// Interval between BAP admin REST async-operation-status polls (<c>GET
    /// .../environments/{envId}</c>) while waiting for
    /// <c>provisioningState</c> to reach a terminal value. Defaults to 30
    /// seconds — ported verbatim from Provision-Customer.ps1 STEP 6's
    /// <c>$pollIntervalSeconds</c>. Task 140 (BAP-REST env-create +
    /// async-operation-polling port).
    /// </summary>
    public TimeSpan AsyncOperationPollInterval { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Interval between Dataverse Web API health probes after
    /// <see cref="IDataverseEnvCreator"/> reports success. Defaults to
    /// 15 seconds — env replication typically completes within
    /// 1–2 minutes of pac reporting completion; sub-minute polls minimize
    /// operator visible latency without hammering the endpoint.
    /// </summary>
    public TimeSpan HealthProbeInterval { get; set; } = TimeSpan.FromSeconds(15);

    /// <summary>
    /// Maximum total wall-clock time spent polling
    /// <see cref="IDataverseHealthProbe"/> after
    /// <see cref="IDataverseEnvCreator"/> reports success. Defaults to
    /// 10 minutes. If exceeded, the handler returns
    /// <see cref="DataverseEnvCreationRejectionCodes.EnvHealthCheckFailed"/>.
    /// (Env replication that outlasts 10 minutes after pac completion is
    /// itself a diagnostic signal the operator wants to see, not a longer
    /// wait to burn through.)
    /// </summary>
    public TimeSpan HealthProbeTotalTimeout { get; set; } = TimeSpan.FromMinutes(10);

    /// <summary>
    /// Maximum per-request timeout for a single <c>GET /WhoAmI</c> HTTP call
    /// made by <see cref="DataverseWebApiHealthProbe"/>. Defaults to
    /// 30 seconds. Distinct from the total polling window — one probe may
    /// timeout while the polling loop continues.
    /// </summary>
    public TimeSpan HealthProbeRequestTimeout { get; set; } = TimeSpan.FromSeconds(30);
}
