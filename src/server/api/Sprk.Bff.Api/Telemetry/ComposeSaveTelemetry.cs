using System.Diagnostics.Metrics;
using Sprk.Bff.Api.Services.Compose;

namespace Sprk.Bff.Api.Telemetry;

/// <summary>
/// FR-S10 (spaarkeai-compose-r8 task 013) — OpenTelemetry counter for Compose SAVE OUTCOMES, one
/// increment per terminal state of the save path, tagged by outcome and a bounded cause.
///
/// <para><b>Why this exists.</b> Compose shipped three consecutive releases (R5, R6, R7) in which the
/// save button was, for some document class, dead — and each time the discovery mechanism was owner
/// UAT, not a signal. There was no metric that could have gone red: a refused save returned a status
/// nobody aggregated, and the worst case (the container-failure path) returned HTTP 200, so it was not
/// even distinguishable from success in request telemetry. This counter makes a save-failure spike
/// visible without waiting for someone to notice they cannot save.</para>
///
/// <para>Alert suggestion: any sustained nonzero rate on the non-success outcomes —
/// <c>refused-stale</c>, <c>refused-locked</c>, <c>refused-invalid</c>, <c>storage-failed</c>,
/// <c>partially-recorded</c> — and in particular <c>storage-failed</c>, which should be ~0 in a healthy
/// environment.</para>
///
/// <para>Deliberately modelled on <see cref="CosmosPersistenceTelemetry"/> (the r7 R-5 precedent for
/// exactly this failure mode: a silent outage with no failing request). Static, so it is callable from
/// the endpoint's catch blocks without threading a DI singleton through them, following the same
/// <c>Sprk.Bff.Api.&lt;Feature&gt;</c> meter convention. Registered in
/// <see cref="Infrastructure.DI.TelemetryModule"/> via <c>metrics.AddMeter</c> — without that
/// registration the metric is silently dropped from the App Insights export, which is the trap task 054
/// of the AI redesign found for the Event Rules meter.</para>
///
/// <para><b>Component justification (root CLAUDE.md §11).</b>
/// <i>Existing overlap:</i> <see cref="CosmosPersistenceTelemetry"/> counts Cosmos write failures on the
/// AI-persistence layer — a different subject (Cosmos, not SPE) with a different dimension (container).
/// <see cref="DocumentTelemetry"/> covers document operations generally, not the Compose save contract.
/// <i>Can it extend one?</i> No: Cosmos telemetry's single dimension is a Cosmos container name, and
/// widening it to carry save outcomes would make one counter mean two unrelated things — the reason its
/// own doc-comment insists on a bounded dimension set. <i>Cost of doing nothing:</i> a total Compose
/// save outage stays invisible until an owner reports it by hand, which is the literal history of R5–R7.
/// </para>
///
/// <para>App Insights / Kusto:
/// <c>customMetrics | where name == "compose.save_outcomes" | summarize sum(value) by bin(timestamp, 5m), customDimensions["outcome"]</c></para>
///
/// <para><b>ADR-015 BINDING:</b> dimensions are enum-like discriminators ONLY — never document names,
/// never ids, never request text. Both dimensions here are drawn from closed sets.</para>
/// </summary>
public static class ComposeSaveTelemetry
{
    /// <summary>Meter name for OpenTelemetry registration. Stable downstream contract.</summary>
    public const string MeterName = "Sprk.Bff.Api.ComposeSave";

    private static readonly Meter Meter = new(MeterName, "1.0.0");

    private static readonly Counter<long> SaveOutcomes = Meter.CreateCounter<long>(
        name: "compose.save_outcomes",
        unit: "{save}",
        description: "Count of Compose save attempts by terminal outcome (persisted, persisted-with-warnings, refused-stale, refused-locked, refused-invalid, storage-failed, partially-recorded) and bounded cause. A sustained nonzero rate on any non-persisted outcome indicates users cannot reliably save.");

    // ── Bounded `cause` discriminators (ADR-015: enum-like, never free text) ──────────────────────
    // A cause names WHICH terminal state produced the outcome, so a spike is diagnosable without
    // reading logs. Keep this list closed; adding one is a deliberate act, not a side effect.

    /// <summary>The save completed through the normal path.</summary>
    public const string CauseNone = "none";
    /// <summary>Render-side degradations, a stale-base re-anchor, or a superseded concurrent version.</summary>
    public const string CauseWarnings = "warnings";
    /// <summary>Best-effort recovery placed some edits and could not place others.</summary>
    public const string CausePartialApply = "partial-apply";
    /// <summary>The create-on-save container step failed (no container, or SPE create returned null).</summary>
    public const string CauseContainerStep = "container-step";
    /// <summary>The If-Match precondition failed and the single rebase retry also lost (FR-S02).</summary>
    public const string CausePrecondition = "precondition";
    /// <summary>
    /// FR-S07: the stale-base re-anchor could not re-download the current bytes, so the save had no valid
    /// basis and was refused before any write (task 014). Paired with <c>refused-stale</c>: the OUTCOME is
    /// a refusal (nothing written, nothing overwritten) while the CAUSE is a failed storage read — which
    /// is exactly the distinction this second dimension exists to carry. A sustained nonzero rate here
    /// means users are being blocked by SPE read failures, not by each other.
    /// </summary>
    public const string CauseBaselineDownload = "baseline-download";
    /// <summary>A Word-for-the-web co-authoring lock (HTTP 423).</summary>
    public const string CauseWordLock = "word-lock";
    /// <summary>A malformed or unsupported request (missing field, PDF replace target).</summary>
    public const string CauseBadRequest = "bad-request";
    /// <summary>The patch engine refused the operation log or comments.</summary>
    public const string CausePatchRefusal = "patch-refusal";
    /// <summary>The caller lacks permission on the target.</summary>
    public const string CauseForbidden = "forbidden";
    /// <summary>The drive-item or a required record was not found.</summary>
    public const string CauseNotFound = "not-found";
    /// <summary>A Dataverse key/duplicate-record conflict or an inactive alternate key.</summary>
    public const string CauseRecordConflict = "record-conflict";
    /// <summary>An unclassified fault reaching the endpoint's final catch.</summary>
    public const string CauseUnhandled = "unhandled";

    /// <summary>
    /// Record one terminal save outcome. Called at the endpoint — the single choke point that sees BOTH
    /// the returned results and every mapped exception, so no terminal state can be counted twice or
    /// missed.
    /// </summary>
    /// <param name="outcome">The terminal outcome; emitted as its stable wire string.</param>
    /// <param name="cause">One of the <c>Cause*</c> constants above. Bounded by contract — never free text.</param>
    public static void RecordSaveOutcome(ComposeSaveOutcome outcome, string cause)
    {
        SaveOutcomes.Add(
            1,
            new KeyValuePair<string, object?>("outcome", outcome.ToWireValue()),
            new KeyValuePair<string, object?>("cause", cause));
    }
}
