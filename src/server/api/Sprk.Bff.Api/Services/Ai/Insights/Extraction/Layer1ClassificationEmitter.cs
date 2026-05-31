using System.Text.Json;
using Microsoft.Extensions.Logging;
using Sprk.Bff.Api.Models.Insights;

namespace Sprk.Bff.Api.Services.Ai.Insights.Extraction;

/// <summary>
/// Default implementation of <see cref="ILayer1ClassificationEmitter"/> — the D-P5 Classification
/// Observation emitter (per <c>SPEC-phase-1-minimum.md §3.3</c>).
/// <para>
/// Stateless and deterministic: no LLM calls, no IO beyond optional substrate upsert + structured
/// logging. Singleton lifetime (registered by <c>InsightsExtractionModule</c>).
/// </para>
/// <para>
/// Producer identity is FIXED on this emitter — <c>playbook://classification@v1</c>, version
/// <c>v1</c> — because the emitter is bound to the v1 prompt template and one-to-one with it
/// (D-62 version-driven re-extraction will ship <c>Layer1ClassificationEmitterV2</c> when
/// <c>classification@v2</c> ships, alongside the new prompt). Hard-coding the producer here is
/// intentional: changing the prompt without changing the producer breaks D-62 re-extraction
/// queries.
/// </para>
/// </summary>
internal sealed class Layer1ClassificationEmitter : ILayer1ClassificationEmitter
{
    /// <summary>Producer kind per <c>InsightArtifact.ProducedBy</c> conventions — Layer 1 is a playbook.</summary>
    internal const string ProducerKind = "playbook";

    /// <summary>Producer id per SPEC §3.3 — bound to the v1 prompt template; do NOT change without a v2 emitter.</summary>
    internal const string ProducerId = "playbook://classification@v1";

    /// <summary>Producer version per D-05 — mandatory on every Observation; drives D-62 re-extraction.</summary>
    internal const string ProducerVersion = "v1";

    /// <summary>Observation predicate per SPEC §3.3 — every Classification Observation uses this exact value.</summary>
    internal const string Predicate = "classification";

    /// <summary>Display hint per design.md §2.2 — classification is an enum.</summary>
    internal const string DisplayHint = "enum";

    /// <summary>
    /// Structured event id for App Insights queries. Stable id lets the D-P11 review-surface
    /// dashboard query precisely (KQL: <c>traces | where customDimensions.EventId == 8030</c>).
    /// Chosen as 8030 = "8" (wave-3-ish bucket the extraction primitives sit in) + "030" (this task id).
    /// </summary>
    private static readonly EventId ClassificationEmittedEvent = new(8030, "Layer1ClassificationEmitted");

    private readonly ILogger<Layer1ClassificationEmitter> _logger;

    public Layer1ClassificationEmitter(ILogger<Layer1ClassificationEmitter> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc/>
    public async Task<ObservationArtifact> EmitAsync(
        Layer1ClassificationResult classification,
        string documentRef,
        string tenantId,
        ExtractionScope? scope,
        DateTimeOffset asOf,
        Func<ObservationArtifact, CancellationToken, Task>? upsertAsync,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(classification);
        ArgumentException.ThrowIfNullOrWhiteSpace(documentRef);
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);

        ct.ThrowIfCancellationRequested();

        // Defensive enum check — the constrained-decoding schema rejects out-of-enum values
        // upstream, but a misconfigured / bypassed validator could still pass garbage. We
        // refuse to emit an Observation with a non-canonical classification because the
        // value lives in the substrate forever and the D-P11 review surface filters by it.
        if (!DocumentClassificationExtensions.TryParseClassification(
                classification.Classification, out _))
        {
            throw new InvalidOperationException(
                $"Layer 1 classification '{classification.Classification}' is not one of the " +
                $"8 enum values published in SPEC-phase-1-minimum.md §3.3. The constrained-" +
                $"decoding schema should have rejected this upstream — investigate the prompt " +
                $"output validator before re-running.");
        }

        if (classification.Confidence is < 0.0 or > 1.0)
        {
            throw new InvalidOperationException(
                $"Layer 1 classification confidence {classification.Confidence:0.000} is outside " +
                $"the [0.0, 1.0] range required by the classification@v1 schema. The constrained-" +
                $"decoding validator should have caught this upstream.");
        }

        // Build the playbook-run evidence ref using the same format as ObservationEmitter
        // (D-P10) so the D-P11 review surface can render Layer 1 + Layer 2 Observations
        // uniformly. Format: "playbook://classification@v1/run-{ISO8601-asOf-Z}".
        var playbookRunRef = $"{ProducerId}/run-{asOf:yyyy-MM-ddTHH:mm:ssZ}";

        // Subject is the SOURCE DOCUMENT per SPEC §3.3 (NOT the matter — that's Layer 2's
        // subject). The same documentRef is also the document evidence ref; the duplication
        // is intentional so consumers can resolve subject and evidence with a single lookup.
        var observation = new ObservationArtifact
        {
            Id = BuildObservationId(documentRef),
            Subject = documentRef,
            Predicate = Predicate,
            Value = new Value
            {
                // Wire shape per SPEC §3.3 — the enum string, NOT the enum index. The
                // classification@v1 schema validates this is one of the 8 canonical values.
                Raw = SerializeEnumValue(classification.Classification),
                DisplayHint = DisplayHint
            },
            Confidence = classification.Confidence,
            Evidence = new EvidenceRef[]
            {
                new()
                {
                    RefType = "document",
                    Ref = documentRef,
                    // Layer 1 does not produce a verbatim quote (the entire document is the
                    // "quote" for a type classification). The reasoning string is carried on
                    // the Observation but not in the evidence quote field per parity with
                    // outcome-extraction Observations whose Quote is the supporting sentence.
                    Quote = null
                },
                new()
                {
                    RefType = "playbook-run",
                    Ref = playbookRunRef
                }
            },
            AsOf = asOf,
            ProducedBy = new ProducedBy
            {
                Kind = ProducerKind,
                Id = ProducerId,
                Version = ProducerVersion
            },
            Scope = BuildScope(tenantId, scope),
            TenantId = tenantId
        };

        // Structured log — App Insights queries can compute classification distribution +
        // confidence histograms across the corpus (the calibration signal D-P11 needs).
        // Reasoning is included at Information level because it's small (one sentence) and
        // the review surface needs it; per ADR-014 this is OUR derived content, not retrieved
        // customer document content, so the "do not log retrieved content" rule does not apply.
        _logger.LogInformation(
            ClassificationEmittedEvent,
            "Layer 1 classification emitted: document={DocumentRef} classification={Classification} " +
            "confidence={Confidence:0.000} producedBy={ProducerId}@{ProducerVersion} reasoning={Reasoning}",
            documentRef,
            classification.Classification,
            classification.Confidence,
            ProducerId,
            ProducerVersion,
            classification.Reasoning);

        if (upsertAsync is not null)
        {
            await upsertAsync(observation, ct).ConfigureAwait(false);
        }

        return observation;
    }

    /// <summary>
    /// Builds a stable Observation id following the same shape as the per-field ObservationEmitter
    /// (D-P10): <c>obs:&lt;documentLocalPart&gt;:classification</c>. Per-document not per-matter
    /// because Layer 1's subject is the document itself.
    /// </summary>
    private static string BuildObservationId(string documentRef)
    {
        var docLocal = LocalPart(documentRef);
        return $"obs:{docLocal}:classification";
    }

    /// <summary>
    /// Extracts the local part of a scheme-prefixed identifier. Mirrors
    /// <c>ObservationEmitter.LocalPart</c> exactly so Observation ids round-trip across the
    /// two emitters with consistent rules.
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
    /// Serializes the classification enum string into a <see cref="JsonElement"/> for the
    /// Observation's <see cref="Value.Raw"/> field. We pre-parse a JSON-quoted string so
    /// consumers get a string-valued JsonElement (not a number or object). Matches the
    /// outcomeCategory pattern from <c>ObservationEmitterTests</c> worked example.
    /// </summary>
    private static JsonElement SerializeEnumValue(string classification)
    {
        // JsonDocument.Parse on a single JSON-quoted string yields a JsonValueKind.String element.
        // We must Clone() because JsonDocument is IDisposable; the cloned element is heap-owned
        // and survives the using block.
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(classification));
        return doc.RootElement.Clone();
    }

    /// <summary>
    /// Builds the <see cref="Models.Insights.Scope"/> envelope for the emitted Observation.
    /// <paramref name="tenantId"/> is mirrored to <c>Scope.TenantId</c> (also stamped at the
    /// envelope level); optional fields come from <paramref name="extractionScope"/>.
    /// </summary>
    private static Scope BuildScope(string tenantId, ExtractionScope? extractionScope) => new()
    {
        TenantId = tenantId,
        MatterId = extractionScope?.MatterId,
        ClientId = extractionScope?.ClientId,
        PracticeArea = extractionScope?.PracticeArea,
        Jurisdiction = extractionScope?.Jurisdiction,
        Year = extractionScope?.Year
    };
}
