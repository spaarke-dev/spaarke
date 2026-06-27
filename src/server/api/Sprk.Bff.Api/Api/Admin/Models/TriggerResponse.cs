namespace Sprk.Bff.Api.Api.Admin.Models;

/// <summary>
/// Response DTO returned by <c>POST /api/admin/jobs/{jobId}/trigger</c> (R3 task 021).
/// Wire-format projection of <see cref="Spaarke.Scheduling.TriggerResult"/> — exposed as a BFF-side
/// DTO so the public API surface is owned by the BFF (not the shared library) and so OpenAPI /
/// admin clients see a single stable shape rooted in the <c>Sprk.Bff.Api.Api.Admin</c> namespace
/// alongside the sibling list/detail DTOs (<see cref="JobStatusSummary"/>, <see cref="JobStatusDetail"/>,
/// <see cref="JobRunDetail"/>).
/// </summary>
/// <remarks>
/// <para>
/// Returned with HTTP 202 Accepted + a <c>Location</c> header pointing at
/// <c>/api/admin/jobs/{jobId}/runs/{runId}</c> (per Microsoft Learn admin-API guidance). The
/// admin client polls the existing <c>GET /api/admin/jobs/{jobId}/status</c> (task 020) for the
/// up-to-date run record — the trigger endpoint does NOT wait on the job's completion.
/// </para>
/// <para>
/// <see cref="Status"/> is always <c>"Running"</c> at the moment of dispatch. Real-time status
/// (<c>"Succeeded"</c> / <c>"Failed"</c>) appears on subsequent <c>GET .../status</c> polls.
/// </para>
/// </remarks>
/// <param name="RunId">Persistent run id (matches the run record subsequently visible via <c>GET .../status</c>).</param>
/// <param name="Status">Canonical status at dispatch — always <c>"Running"</c>.</param>
/// <param name="StartedAt">UTC timestamp captured immediately before the background dispatch.</param>
public sealed record TriggerResponse(
    Guid RunId,
    string Status,
    DateTimeOffset StartedAt);
