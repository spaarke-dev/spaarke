using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sprk.Bff.Api.Models.Insights;

namespace Sprk.Bff.Api.Services.Ai.Insights.Extraction;

/// <summary>
/// Default implementation of <see cref="IObservationEmitter"/> — the D-P10 third mechanical
/// post-processing gate. Realizes <c>SPEC-phase-1-minimum.md §3.4</c> "Per-field Observation emission"
/// + D-63 (per-field thresholds admin-tunable via <c>IOptionsMonitor</c>).
/// <para>
/// Stateless and deterministic: no LLM calls, no IO beyond optional substrate upsert + structured
/// logging. Singleton lifetime (registered by <c>InsightsExtractionModule</c>).
/// </para>
/// </summary>
internal sealed class ObservationEmitter : IObservationEmitter
{
    private readonly IOptionsMonitor<ConfidenceThresholdOptions> _options;
    private readonly ILogger<ObservationEmitter> _logger;

    /// <summary>
    /// Structured event id for App Insights queries. Stable id lets the D-P11 review-surface
    /// drift dashboard query precisely (KQL: <c>traces | where customDimensions.EventId == 8021</c>).
    /// Chosen as 8021 = "8" (this project's wave-3-ish bucket) + "021" (this task id).
    /// </summary>
    private static readonly EventId DroppedBelowThresholdEvent = new(8021, "ObservationDroppedBelowThreshold");

    public ObservationEmitter(
        IOptionsMonitor<ConfidenceThresholdOptions> options,
        ILogger<ObservationEmitter> logger)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<ObservationArtifact>> EmitFromExtractionAsync(
        ExtractionResult extraction,
        Func<ObservationArtifact, CancellationToken, Task>? upsertAsync,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(extraction);

        // Snapshot the options ONCE per call — admin edits between calls take effect on the
        // next call, but a single emission run sees a consistent threshold set across all
        // fields (avoids the rare race where appsettings reloads mid-loop).
        var thresholds = _options.CurrentValue;

        // ProducedBy / Scope are stamped from extraction inputs (deterministic, no clock dep
        // beyond extraction.AsOf which the caller supplies).
        var producedBy = new ProducedBy
        {
            Kind = extraction.ProducedBy.Kind,
            Id = extraction.ProducedBy.Id,
            Version = extraction.ProducedBy.Version
        };
        var scope = BuildScope(extraction);
        var playbookRunRef = BuildPlaybookRunRef(extraction);

        // Order-preserving iteration: pre-size to upper bound, append survivors only.
        var survivors = new List<ObservationArtifact>(capacity: extraction.Fields.Count);

        foreach (var (fieldName, field) in extraction.Fields)
        {
            ct.ThrowIfCancellationRequested();

            var threshold = thresholds.GetThresholdFor(fieldName);
            if (field.Confidence < threshold)
            {
                // Below threshold — drop and log. Structured logging so App Insights queries
                // can group by field name and compute drop rate per field (the calibration
                // signal D-P11 review surface needs).
                _logger.LogInformation(
                    DroppedBelowThresholdEvent,
                    "Observation dropped: field={FieldName} confidence={Confidence:0.000} " +
                    "threshold={Threshold:0.000} subject={Subject} document={DocumentRef} " +
                    "producedBy={ProducedById}@{ProducedByVersion}",
                    fieldName,
                    field.Confidence,
                    threshold,
                    extraction.Subject,
                    extraction.DocumentRef,
                    extraction.ProducedBy.Id,
                    extraction.ProducedBy.Version);
                continue;
            }

            // Above threshold — emit one Observation per field per D-P10. Subject = matter the
            // document belongs to; predicate = field name; value = extracted typed value;
            // evidence = the verbatim quote + document reference + playbook-run ref.
            var observation = new ObservationArtifact
            {
                Id = BuildObservationId(extraction.Subject, fieldName, extraction.DocumentRef),
                Subject = extraction.Subject,
                Predicate = fieldName,
                Value = new Value
                {
                    Raw = field.Value,
                    DisplayHint = field.DisplayHint
                },
                Confidence = field.Confidence,
                Evidence = new EvidenceRef[]
                {
                    new()
                    {
                        RefType = "document",
                        Ref = extraction.DocumentRef,
                        Quote = field.Quote
                    },
                    new()
                    {
                        RefType = "playbook-run",
                        Ref = playbookRunRef
                    }
                },
                AsOf = extraction.AsOf,
                ProducedBy = producedBy,
                Scope = scope,
                TenantId = extraction.TenantId
            };

            survivors.Add(observation);

            if (upsertAsync is not null)
            {
                await upsertAsync(observation, ct).ConfigureAwait(false);
            }
        }

        return survivors;
    }

    /// <summary>
    /// Builds a stable Observation id matching the SPEC §3.4.1 worked-example shape:
    /// <c>obs:&lt;subjectLocalPart&gt;:&lt;fieldName&gt;:&lt;documentLocalPart&gt;</c>.
    /// Strips scheme prefixes so the id is grep-friendly and review-surface-friendly.
    /// </summary>
    private static string BuildObservationId(string subject, string fieldName, string documentRef)
    {
        var subjectLocal = LocalPart(subject);
        var docLocal = LocalPart(documentRef);
        return $"obs:{subjectLocal}:{fieldName}:{docLocal}";
    }

    /// <summary>
    /// Extracts the local part of a scheme-prefixed identifier. Examples:
    /// <c>matter:M-2024-0341</c> → <c>M-2024-0341</c>;
    /// <c>spe://drive/x/item/closing-letter-M-2024-0341.docx</c> → <c>closing-letter-M-2024-0341.docx</c>.
    /// Falls back to the input if no scheme is present.
    /// </summary>
    private static string LocalPart(string schemed)
    {
        if (string.IsNullOrEmpty(schemed)) return schemed;
        var slash = schemed.LastIndexOf('/');
        if (slash >= 0 && slash < schemed.Length - 1) return schemed[(slash + 1)..];
        var colon = schemed.IndexOf(':');
        if (colon >= 0 && colon < schemed.Length - 1) return schemed[(colon + 1)..];
        return schemed;
    }

    /// <summary>
    /// Builds the <c>playbook-run</c> evidence ref. Format matches SPEC §3.4.1 worked example:
    /// <c>playbook://outcome-extraction@v1/run-2026-04-12T08:30:00Z</c>.
    /// </summary>
    private static string BuildPlaybookRunRef(ExtractionResult extraction)
    {
        // Strip the leading "playbook://" if present so we don't double-up.
        var producerId = extraction.ProducedBy.Id;
        const string scheme = "playbook://";
        var localProducer = producerId.StartsWith(scheme, StringComparison.OrdinalIgnoreCase)
            ? producerId[scheme.Length..]
            : producerId;

        var stamp = extraction.AsOf.ToString("yyyy-MM-ddTHH:mm:ssZ");
        return $"{scheme}{localProducer}/run-{stamp}";
    }

    /// <summary>
    /// Builds the <c>Scope</c> envelope for the emitted Observation. <see cref="ExtractionResult.TenantId"/>
    /// is required at the top level and propagates to both the envelope-level <c>TenantId</c>
    /// (already stamped above) and the nested <c>Scope.TenantId</c>; optional fields come from
    /// <see cref="ExtractionResult.Scope"/>.
    /// </summary>
    private static Scope BuildScope(ExtractionResult extraction) => new()
    {
        TenantId = extraction.TenantId,
        MatterId = extraction.Scope?.MatterId,
        ClientId = extraction.Scope?.ClientId,
        PracticeArea = extraction.Scope?.PracticeArea,
        Jurisdiction = extraction.Scope?.Jurisdiction,
        Year = extraction.Scope?.Year
    };
}
