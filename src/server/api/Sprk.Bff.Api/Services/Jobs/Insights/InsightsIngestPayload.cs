namespace Sprk.Bff.Api.Services.Jobs.Insights;

/// <summary>
/// Payload structure for <c>InsightsUniversalIngest</c> jobs (D-P8, task 050).
/// Carries the minimum tenant-scoped triple required by
/// <see cref="Models.Ai.PublicContracts.InsightsIngestRequest"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Zone B DTO</b> per <c>SPEC §3.5</c>. The payload only includes primitive fields;
/// it imports no AI internals. The consumer (<see cref="InsightsIngestJobHandler"/>)
/// is responsible for translating this payload into the public-facade request shape
/// before dispatching to <c>IInsightsAi.RunIngestAsync</c>.
/// </para>
/// <para>
/// <b>Wire format</b>: this record is serialized into <see cref="JobContract.Payload"/>
/// (the standard ADR-004 envelope) by <c>UploadFinalizationWorker.QueueNextStageAsync</c>
/// using the camelCase contract that <see cref="JobSubmissionService"/> already enforces.
/// Deserialization uses <c>PropertyNameCaseInsensitive = true</c> for robustness
/// against future capitalization drift.
/// </para>
/// </remarks>
public sealed class InsightsIngestPayload
{
    /// <summary>
    /// Document identifier. Stored as <see cref="string"/> because the public-facade
    /// <see cref="Models.Ai.PublicContracts.InsightsIngestRequest.DocumentId"/> is a
    /// string (the Insights Engine treats document ids as opaque tokens addressable
    /// in <c>spaarke-files-index</c>; SPE upload events deliver them as strings).
    /// </summary>
    public string? DocumentId { get; set; }

    /// <summary>
    /// Matter identifier the document belongs to. Resolved by
    /// <c>UploadFinalizationWorker.QueueNextStageAsync</c> via
    /// <c>IDocumentDataverseService.GetDocumentAsync</c> against
    /// <c>DocumentEntity.MatterId</c>. May be <c>null</c> when the Document record
    /// has no Matter lookup — see <see cref="InsightsIngestJobHandler"/> for the
    /// behavior contract (dead-letter, not retry).
    /// </summary>
    public string? MatterId { get; set; }

    /// <summary>
    /// Tenant identifier (D-52 single-tenant Phase 1). Currently sourced from the
    /// <c>OfficeJobMessage.UserId</c>'s tenant claim (resolved at queue time so the
    /// background handler does not need user context). Phase 1.5+ will centralize
    /// tenant resolution as part of D-P15 endpoint work (task 061).
    /// </summary>
    public string? TenantId { get; set; }

    /// <summary>
    /// Source tag for telemetry / audit (e.g., <c>"OfficeAddinUpload"</c>,
    /// <c>"SmokeTestFixture"</c>). Not used for routing.
    /// </summary>
    public string? Source { get; set; }

    /// <summary>
    /// When the job was enqueued (for end-to-end latency telemetry).
    /// </summary>
    public DateTimeOffset? EnqueuedAt { get; set; }
}
