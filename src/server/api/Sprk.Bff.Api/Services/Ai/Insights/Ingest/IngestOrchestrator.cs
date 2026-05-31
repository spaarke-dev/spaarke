using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using OpenAI.Chat;
using Sprk.Bff.Api.Models.Ai.PublicContracts;
using Sprk.Bff.Api.Models.Insights;
using Sprk.Bff.Api.Services.Ai.CitationVerification;
using Sprk.Bff.Api.Services.Ai.Insights.Extraction;
using Sprk.Bff.Api.Services.Ai.Insights.Prompts;
using Sprk.Bff.Api.Services.Ai.Insights.Sanitization;
using Sprk.Bff.Api.Services.Ai.PublicContracts;

namespace Sprk.Bff.Api.Services.Ai.Insights.Ingest;

/// <summary>
/// D-P7 universal ingest orchestrator. Realizes the layered extraction pipeline per
/// <c>SPEC-phase-1-minimum.md §3</c>: Sanitizer → Layer 1 → conditional Layer 2 →
/// GroundingVerifier → ConfidenceThreshold → ObservationEmitter → IndexUpsert → Mirror.
/// </summary>
/// <remarks>
/// <para>
/// <b>Sequence</b> (deterministic; same for every document):
/// <list type="number">
///   <item><b>Fetch</b> document content from <c>spaarke-files-index</c> via
///   <see cref="IIngestDocumentSource"/>. Null result → early return with empty result
///   (non-indexable upload — not an error).</item>
///   <item><b>Sanitize</b> the concatenated text via <see cref="IInsightsContentSanitizer"/>
///   (D-50). Sanitized text feeds both Layer 1 and Layer 2 prompts; the original chunks
///   feed <see cref="IGroundingVerifier"/> (which needs raw text to verify quotes).</item>
///   <item><b>Layer 1 classification</b>: structured-completion call to
///   <see cref="IOpenAiClient.GetStructuredCompletionRawAsync"/> with the
///   <c>classification@v1</c> prompt + schema; deserialize to
///   <see cref="Layer1ClassificationResult"/>.</item>
///   <item><b>Emit Layer 1 Observation</b> via <see cref="ILayer1ClassificationEmitter"/>
///   (D-P5 task 030). Upsert to <c>spaarke-insights-index</c> + mirror to Dataverse.</item>
///   <item><b>Gate Layer 2</b>: only run Layer 2 if Layer 1's classification is
///   outcome-bearing (per <see cref="DocumentClassificationExtensions.IsOutcomeBearing"/>)
///   AND confidence ≥ 0.7. Otherwise return with Layer 1 Observation only.</item>
///   <item><b>Layer 2 outcome extraction</b>: structured-completion call with the
///   <c>outcome-extraction@v1</c> prompt + schema.</item>
///   <item><b>Validate</b> via <see cref="OutcomeExtractionResponseValidator"/>.
///   Validation failure → log + return with Layer 1 Observation only (no retry —
///   the IOpenAiClient call already has constrained-decoding + retry).</item>
///   <item><b>Project</b> the validated response into <see cref="ExtractionResult"/>.</item>
///   <item><b>Ground-verify</b> each field's quote via <see cref="IGroundingVerifier"/>
///   against the original chunks. Drop fields whose quotes failed verification
///   (Verdict ≠ Verified ∧ ≠ VerifiedApproximate).</item>
///   <item><b>Emit Layer 2 Observations</b> via <see cref="IObservationEmitter"/>
///   (D-P10 task 021) with confidence-threshold gating. Each surviving Observation is
///   upserted to <c>spaarke-insights-index</c> + mirrored to Dataverse via the
///   per-Observation upsert callback.</item>
/// </list>
/// </para>
/// <para>
/// <b>Caching</b>: ingest is one-shot (per SPEC §3 + the task brief — "cache TTL = 0
/// for ingest per POML §step 3; don't cache one-shot ingest"). We deliberately do NOT
/// wrap this in <c>IInsightsPlaybookExecutionCache</c>; the same document arriving
/// twice on SPE-upload events would be a separate concern (idempotency comes from
/// Observation.Id determinism + MergeOrUpload semantics in the upserter).
/// </para>
/// <para>
/// <b>Mirror failures are non-fatal</b>: the orchestrator catches mirror exceptions
/// per-Observation and logs them but does NOT propagate. Per
/// <see cref="IObservationMirror"/> semantics, the system-of-record is
/// <c>spaarke-insights-index</c>; a Dataverse mirror failure can be recovered by
/// re-projecting later.
/// </para>
/// <para>
/// <b>Index-upsert failures ARE fatal</b>: if writing to
/// <c>spaarke-insights-index</c> fails, the orchestrator propagates so the D-P8
/// consumer (task 050) can dead-letter the message. The substrate IS the system-of-record.
/// </para>
/// </remarks>
internal sealed class IngestOrchestrator : IIngestOrchestrator
{
    /// <summary>Layer 1 prompt + schema basename.</summary>
    private const string Layer1PromptBasename = "classification.v1";

    /// <summary>Layer 2 prompt + schema basename.</summary>
    private const string Layer2PromptBasename = "outcome-extraction.v1";

    /// <summary>Layer 1 deployment per <c>layer1-classification.node.json</c> (gpt-4o-mini for cheap-gates-expensive D-59).</summary>
    private const string Layer1Deployment = "gpt-4o-mini";

    /// <summary>Layer 2 deployment per <c>layer2-outcome-extraction.node.json</c>.</summary>
    private const string Layer2Deployment = "gpt-4o";

    /// <summary>Per SPEC-phase-1-minimum.md §3.3 / D-59: Layer 2 gate confidence threshold.</summary>
    private const double Layer2GateMinConfidence = 0.7;

    /// <summary>Display-hint conventions per <see cref="ExtractionField.DisplayHint"/>.</summary>
    private const string DisplayHintEnum = "enum";
    private const string DisplayHintCurrency = "currency-usd";
    private const string DisplayHintDate = "date";
    private const string DisplayHintDurationDays = "duration-days";

    /// <summary>Producer identity for Layer 2 Observations per SPEC §3.4.</summary>
    private static readonly ProducerIdentity Layer2Producer = new()
    {
        Kind = "playbook",
        Id = "playbook://outcome-extraction@v1",
        Version = "v1"
    };

    private static readonly EventId IngestCompletedEvent = new(8047, "UniversalIngestCompleted");
    private static readonly EventId IngestSkippedEvent = new(8048, "UniversalIngestSkipped");
    private static readonly EventId Layer2GatedOffEvent = new(8049, "UniversalIngestLayer2GatedOff");
    private static readonly EventId Layer2ValidationFailedEvent = new(8050, "UniversalIngestLayer2ValidationFailed");
    private static readonly EventId GroundingDroppedFieldEvent = new(8051, "UniversalIngestFieldDroppedAfterGrounding");
    private static readonly EventId MirrorFailedEvent = new(8052, "UniversalIngestMirrorFailed");

    private readonly IIngestDocumentSource _documentSource;
    private readonly IInsightsContentSanitizer _sanitizer;
    private readonly IOpenAiClient _openAiClient;
    private readonly IInsightsPromptLoader _promptLoader;
    private readonly ILayer1ClassificationEmitter _layer1Emitter;
    private readonly IGroundingVerifier _groundingVerifier;
    private readonly IObservationEmitter _observationEmitter;
    private readonly IObservationIndexUpserter _indexUpserter;
    private readonly IObservationMirror _mirror;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<IngestOrchestrator> _logger;

    public IngestOrchestrator(
        IIngestDocumentSource documentSource,
        IInsightsContentSanitizer sanitizer,
        IOpenAiClient openAiClient,
        IInsightsPromptLoader promptLoader,
        ILayer1ClassificationEmitter layer1Emitter,
        IGroundingVerifier groundingVerifier,
        IObservationEmitter observationEmitter,
        IObservationIndexUpserter indexUpserter,
        IObservationMirror mirror,
        TimeProvider timeProvider,
        ILogger<IngestOrchestrator> logger)
    {
        _documentSource = documentSource ?? throw new ArgumentNullException(nameof(documentSource));
        _sanitizer = sanitizer ?? throw new ArgumentNullException(nameof(sanitizer));
        _openAiClient = openAiClient ?? throw new ArgumentNullException(nameof(openAiClient));
        _promptLoader = promptLoader ?? throw new ArgumentNullException(nameof(promptLoader));
        _layer1Emitter = layer1Emitter ?? throw new ArgumentNullException(nameof(layer1Emitter));
        _groundingVerifier = groundingVerifier ?? throw new ArgumentNullException(nameof(groundingVerifier));
        _observationEmitter = observationEmitter ?? throw new ArgumentNullException(nameof(observationEmitter));
        _indexUpserter = indexUpserter ?? throw new ArgumentNullException(nameof(indexUpserter));
        _mirror = mirror ?? throw new ArgumentNullException(nameof(mirror));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<InsightsIngestResult> RunAsync(
        InsightsIngestRequest request,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.DocumentId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.MatterId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.TenantId);

        var sw = Stopwatch.StartNew();

        // Step 1: Fetch document content from spaarke-files-index.
        var content = await _documentSource.FetchAsync(request.DocumentId, request.TenantId, ct);
        if (content is null)
        {
            _logger.Log(
                LogLevel.Information,
                IngestSkippedEvent,
                "UniversalIngest skipped: documentId={DocumentId} matterId={MatterId} tenantId={TenantId} reason=document-not-indexable",
                request.DocumentId, request.MatterId, request.TenantId);
            return new InsightsIngestResult(
                ObservationsEmitted: 0,
                Layer1Classification: null,
                Layer2Triggered: false);
        }

        // Step 2: Sanitize the concatenated text (D-50). Original chunks remain raw —
        // GroundingVerifier needs them unmodified to verify verbatim quotes.
        var sanitization = await _sanitizer.SanitizeAsync(content.FullText, ct);
        if (string.IsNullOrWhiteSpace(sanitization.SanitizedText))
        {
            _logger.Log(
                LogLevel.Information,
                IngestSkippedEvent,
                "UniversalIngest skipped: documentId={DocumentId} reason=sanitized-empty (originalLength={OriginalLength})",
                request.DocumentId, sanitization.OriginalLength);
            return new InsightsIngestResult(
                ObservationsEmitted: 0,
                Layer1Classification: null,
                Layer2Triggered: false);
        }

        // The subject for Layer 2 Observations is the matter; the subject for the
        // Layer 1 Classification Observation is the document. Scope is shared.
        var matterSubject = $"matter:{request.MatterId}";
        var scope = new ExtractionScope { MatterId = request.MatterId };
        var asOf = _timeProvider.GetUtcNow();

        // Step 3: Layer 1 classification.
        var layer1Result = await RunLayer1Async(sanitization.SanitizedText, ct);

        // Step 4: Emit Layer 1 Classification Observation. Upsert + mirror.
        var layer1Observation = await _layer1Emitter.EmitAsync(
            classification: layer1Result,
            documentRef: content.DocumentRef,
            tenantId: request.TenantId,
            scope: scope,
            asOf: asOf,
            upsertAsync: async (obs, innerCt) =>
            {
                await _indexUpserter.UpsertAsync(obs, innerCt).ConfigureAwait(false);
                await TryMirrorAsync(obs, innerCt).ConfigureAwait(false);
            },
            ct: ct);

        var totalObservationsEmitted = 1; // Layer 1 always emits exactly one.
        var layer2Triggered = false;

        // Step 5: Gate Layer 2 per D-59 / SPEC §3.3.
        var classifiedEnum = DocumentClassificationExtensions.TryParseClassification(
            layer1Result.Classification, out var classification);
        var passesGate =
            classifiedEnum
            && classification.IsOutcomeBearing()
            && layer1Result.Confidence >= Layer2GateMinConfidence;

        if (!passesGate)
        {
            _logger.Log(
                LogLevel.Information,
                Layer2GatedOffEvent,
                "UniversalIngest Layer 2 gated off: documentId={DocumentId} classification={Classification} confidence={Confidence:0.000} threshold={Threshold:0.000} isOutcomeBearing={IsOutcomeBearing}",
                request.DocumentId, layer1Result.Classification, layer1Result.Confidence,
                Layer2GateMinConfidence, classifiedEnum && classification.IsOutcomeBearing());
        }
        else
        {
            layer2Triggered = true;

            // Step 6: Layer 2 outcome extraction.
            OutcomeExtractionResponse? layer2Response = null;
            try
            {
                var rawJson = await RunLayer2Async(sanitization.SanitizedText, ct);
                var validation = OutcomeExtractionResponseValidator.Validate(rawJson);
                if (!validation.IsValid)
                {
                    _logger.Log(
                        LogLevel.Warning,
                        Layer2ValidationFailedEvent,
                        "UniversalIngest Layer 2 validation failed: documentId={DocumentId} errors={Errors}",
                        request.DocumentId, string.Join("; ", validation.Errors));
                }
                else
                {
                    layer2Response = validation.Response;
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                // Layer 2 LLM call or parsing threw. Don't fail the whole ingest — the
                // Layer 1 Observation has already shipped. Log + continue.
                _logger.LogWarning(
                    ex,
                    "UniversalIngest Layer 2 failed (Layer 1 Observation already emitted; returning with Layer 1 only): documentId={DocumentId}",
                    request.DocumentId);
            }

            if (layer2Response is not null)
            {
                // Step 7-8: Build ExtractionResult, run grounding verification, project to fields.
                var extraction = await BuildExtractionResultAsync(
                    layer2Response, content, matterSubject, request.TenantId, scope, asOf, ct);

                // Step 9-10: Emit Layer 2 per-field Observations with confidence-threshold gating,
                // upserting + mirroring each.
                var emitted = await _observationEmitter.EmitFromExtractionAsync(
                    extraction: extraction,
                    upsertAsync: async (obs, innerCt) =>
                    {
                        await _indexUpserter.UpsertAsync(obs, innerCt).ConfigureAwait(false);
                        await TryMirrorAsync(obs, innerCt).ConfigureAwait(false);
                    },
                    ct: ct);

                totalObservationsEmitted += emitted.Count;
            }
        }

        sw.Stop();

        _logger.Log(
            LogLevel.Information,
            IngestCompletedEvent,
            "UniversalIngest completed: documentId={DocumentId} matterId={MatterId} tenantId={TenantId} layer1Classification={Layer1Classification} layer2Triggered={Layer2Triggered} observationsEmitted={ObservationsEmitted} elapsedMs={ElapsedMs}",
            request.DocumentId, request.MatterId, request.TenantId,
            layer1Result.Classification, layer2Triggered, totalObservationsEmitted, sw.ElapsedMilliseconds);

        return new InsightsIngestResult(
            ObservationsEmitted: totalObservationsEmitted,
            Layer1Classification: layer1Result.Classification,
            Layer2Triggered: layer2Triggered);
    }

    // -------------------------------------------------------------------------
    // Layer 1 / Layer 2 LLM invocation helpers
    // -------------------------------------------------------------------------

    private async Task<Layer1ClassificationResult> RunLayer1Async(string documentText, CancellationToken ct)
    {
        var prompt = _promptLoader.Get(Layer1PromptBasename);
        var fullPrompt = BuildPromptWithDocument(prompt.Template, documentText);

        var rawJson = await _openAiClient.GetStructuredCompletionRawAsync(
            prompt: fullPrompt,
            jsonSchema: BinaryData.FromString(prompt.SchemaJson),
            schemaName: prompt.SchemaName,
            model: Layer1Deployment,
            maxOutputTokens: 200,
            cancellationToken: ct);

        var result = JsonSerializer.Deserialize<Layer1ClassificationResult>(rawJson)
            ?? throw new InvalidOperationException(
                $"Layer 1 classification returned null after constrained decoding " +
                $"(rawJson length={rawJson.Length}). Constrained decoding should have prevented this.");
        return result;
    }

    private async Task<string> RunLayer2Async(string documentText, CancellationToken ct)
    {
        var prompt = _promptLoader.Get(Layer2PromptBasename);
        var fullPrompt = BuildPromptWithDocument(prompt.Template, documentText);

        return await _openAiClient.GetStructuredCompletionRawAsync(
            prompt: fullPrompt,
            jsonSchema: BinaryData.FromString(prompt.SchemaJson),
            schemaName: prompt.SchemaName,
            model: Layer2Deployment,
            maxOutputTokens: 1500,
            cancellationToken: ct);
    }

    /// <summary>
    /// Appends document text to a prompt template after a separator. The templates
    /// end with "Document content follows below." so we simply append.
    /// </summary>
    private static string BuildPromptWithDocument(string template, string documentText)
    {
        var sb = new StringBuilder(template.Length + documentText.Length + 16);
        sb.Append(template);
        if (!template.EndsWith('\n')) sb.Append('\n');
        sb.Append('\n');
        sb.Append(documentText);
        return sb.ToString();
    }

    // -------------------------------------------------------------------------
    // Layer 2 projection: response → ExtractionResult (with grounding drop)
    // -------------------------------------------------------------------------

    /// <summary>
    /// Projects the validated Layer 2 response into an <see cref="ExtractionResult"/>:
    /// only fields that (a) have a non-null value AND (b) have a quote that ground-verifies
    /// against the source chunks make it into the result. Per SPEC §3.4 mechanical gate 1
    /// (grounding) runs BEFORE confidence-threshold gating.
    /// </summary>
    private async Task<ExtractionResult> BuildExtractionResultAsync(
        OutcomeExtractionResponse response,
        IngestDocumentContent content,
        string matterSubject,
        string tenantId,
        ExtractionScope scope,
        DateTimeOffset asOf,
        CancellationToken ct)
    {
        // Collect candidate fields (non-null values with quotes).
        var candidates = new List<(string Name, JsonElement Value, string Quote, double Confidence, string DisplayHint)>();

        if (response.OutcomeCategory is not null && !string.IsNullOrWhiteSpace(response.Evidence.OutcomeCategory))
        {
            candidates.Add((
                "outcomeCategory",
                ToJsonElement(response.OutcomeCategory),
                response.Evidence.OutcomeCategory!,
                response.Confidence.OutcomeCategory,
                DisplayHintEnum));
        }
        if (response.SettlementAmount is not null && !string.IsNullOrWhiteSpace(response.Evidence.SettlementAmount))
        {
            candidates.Add((
                "settlementAmount",
                ToJsonElement(response.SettlementAmount.Value),
                response.Evidence.SettlementAmount!,
                response.Confidence.SettlementAmount,
                DisplayHintCurrency));
        }
        if (!string.IsNullOrEmpty(response.OutcomeDate) && !string.IsNullOrWhiteSpace(response.Evidence.OutcomeDate))
        {
            candidates.Add((
                "outcomeDate",
                ToJsonElement(response.OutcomeDate),
                response.Evidence.OutcomeDate!,
                response.Confidence.OutcomeDate,
                DisplayHintDate));
        }
        if (response.MatterDurationDays is not null && !string.IsNullOrWhiteSpace(response.Evidence.MatterDurationDays))
        {
            candidates.Add((
                "matterDurationDays",
                ToJsonElement(response.MatterDurationDays.Value),
                response.Evidence.MatterDurationDays!,
                response.Confidence.MatterDurationDays,
                DisplayHintDurationDays));
        }

        // Run grounding verification on all quotes in one call (mirrors GroundingVerifyNode pattern).
        var citations = candidates
            .Select(c => new EvidenceRef
            {
                RefType = "document",
                Ref = content.DocumentRef,
                Quote = c.Quote
            })
            .ToList();

        var verifications = candidates.Count == 0
            ? Array.Empty<VerificationResult>()
            : await _groundingVerifier.VerifyAsync(citations, content.Chunks, ct);

        // Build the survivor dictionary: include only fields whose quote is Verified or VerifiedApproximate.
        var survivors = new Dictionary<string, ExtractionField>(capacity: candidates.Count);
        for (int i = 0; i < candidates.Count; i++)
        {
            var (name, value, quote, confidence, displayHint) = candidates[i];
            var verdict = verifications.Count > i ? verifications[i].Verdict : VerificationVerdict.NotFound;
            var grounded = verdict is VerificationVerdict.Verified or VerificationVerdict.VerifiedApproximate;

            if (!grounded)
            {
                _logger.Log(
                    LogLevel.Information,
                    GroundingDroppedFieldEvent,
                    "UniversalIngest dropped field after grounding: field={FieldName} verdict={Verdict} documentRef={DocumentRef}",
                    name, verdict, content.DocumentRef);
                continue;
            }

            survivors[name] = new ExtractionField
            {
                Value = value,
                Quote = quote,
                Confidence = confidence,
                DisplayHint = displayHint
            };
        }

        return new ExtractionResult
        {
            Subject = matterSubject,
            DocumentRef = content.DocumentRef,
            TenantId = tenantId,
            ProducedBy = Layer2Producer,
            AsOf = asOf,
            Fields = survivors,
            Scope = scope
        };
    }

    /// <summary>Serializes any value to a <see cref="JsonElement"/> via parse-of-serialized.</summary>
    private static JsonElement ToJsonElement(object value)
    {
        var json = JsonSerializer.Serialize(value);
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }

    // -------------------------------------------------------------------------
    // Mirror helpers — failures are logged, not propagated
    // -------------------------------------------------------------------------

    private async Task TryMirrorAsync(ObservationArtifact observation, CancellationToken ct)
    {
        try
        {
            await _mirror.MirrorAsync(observation, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            // Per IObservationMirror contract: failures are non-fatal.
            _logger.Log(
                LogLevel.Warning,
                MirrorFailedEvent,
                ex,
                "ObservationMirror failed (non-fatal; substrate write was successful): observationId={ObservationId} predicate={Predicate}",
                observation.Id, observation.Predicate);
        }
    }
}
