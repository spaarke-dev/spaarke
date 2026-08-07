namespace Sprk.Bff.Api.Services.Communication.Models;

/// <summary>
/// FR-A4 affinity confirmation-write request (email-communication-intelligence-r2, R-1). The single ADR-024
/// regarding target a human just confirmed the communication belongs to. The confirm surface POSTs this
/// (fire-and-forget) AFTER its client-side <c>Xrm.WebApi</c> regarding write succeeds, once per confirmed
/// selection. The signals are computed server-side from the reconstructed envelope — the client sends only the
/// target, never the signals.
/// </summary>
/// <param name="TargetEntityType">Dataverse logical name of the confirmed parent (e.g. <c>sprk_matter</c>). Must
/// be an ADR-024 regarding target (in <c>RegardingFieldMap</c>) or the write is a no-op.</param>
/// <param name="TargetRecordId">GUID of the confirmed parent record.</param>
public sealed record RecordAffinityConfirmationRequest(string TargetEntityType, string TargetRecordId);

/// <summary>
/// FR-A4 affinity confirmation-write result. <see cref="RecordedSignals"/> is the number of affinity signals
/// (sender / sender-domain / subject-keyword / participant-set) incremented for the confirmed target — 0 when the
/// write was a best-effort no-op (invalid/unmapped target, affinity disabled for the tenant, or a store failure).
/// </summary>
public sealed record RecordAffinityConfirmationResult(int RecordedSignals)
{
    /// <summary>The no-op result (0 signals recorded) — returned for every best-effort skip.</summary>
    public static readonly RecordAffinityConfirmationResult None = new(0);
}
