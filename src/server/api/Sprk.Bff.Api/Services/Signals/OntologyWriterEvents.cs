namespace Sprk.Bff.Api.Services.Signals;

/// <summary>
/// Stable <see cref="EventId"/>s for Signal-writer Error logs (task 030 rework — owner directive 2026-10-04:
/// a refused write must not look like "no conditions found"). One <see cref="EventId"/> covers every refusal
/// path; the log's structured <c>reason</c> property (one of
/// <see cref="Telemetry.OntologyWriterFailureReason"/>'s constants) distinguishes WHICH one — the same
/// vocabulary <see cref="Telemetry.OntologyWriterTelemetry.RecordFailure"/> uses for the matching metric
/// dimension, so a log query and a metric query always agree.
/// </summary>
public static class OntologyWriterEvents
{
    /// <summary>
    /// Logged at Error, exactly once per refusal, by <see cref="SignalWriter"/> or
    /// <see cref="OntologyWriterDataverseClient"/> — never swallowed; the exception is always rethrown after
    /// this is logged. Structured properties: <c>reason</c> (bounded-cardinality —
    /// <see cref="Telemetry.OntologyWriterFailureReason"/>) and <c>policyCode</c>. NEVER fact values or the
    /// rendered sentence (matter detail) — see each call site's own comment for why.
    /// </summary>
    public static readonly EventId WriteRefused = new(50300, nameof(WriteRefused));
}
