using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Models.Ai.Communication;
using Sprk.Bff.Api.Services.Ai.PublicContracts;
using Sprk.Bff.Api.Services.Communication.Models;

namespace Sprk.Bff.Api.Services.Communication.Engine.Rungs;

/// <summary>
/// Rung 5 — AI extract + classify (FR-15). The deepest, most expensive rung: when the deterministic rungs
/// (0–3) AND the semantic rung (4) did not auto-file, this rung runs an LLM extract+classify over the
/// normalized envelope (subject + body) via the <see cref="ICommunicationClassificationAi"/> facade and emits
/// the classification as <b>metadata-only</b> <see cref="RungMatch"/> signals (category / obligations /
/// rationale), NOT regarding writes.
/// </summary>
/// <remarks>
/// <para>
/// <b>Metadata-only signals — never a target, never auto-files.</b> An LLM cannot produce record GUIDs, so
/// this rung resolves no <see cref="RungMatch.Target"/> / <see cref="RungMatch.RegardingFieldName"/> (both
/// null). <see cref="AssociationStatusMapper"/> records such matches into <c>provenance.signals</c> and they
/// never enter the field-winner aggregation — so rung 5 forces no regarding write and can never raise the
/// status by itself. Its value is the W5 "Communication Triage" substrate (task 053), which reuses the
/// category / urgency / obligations / suggested-actions signal without a second LLM call. This mirrors how the
/// rung-3 structural detectors emit metadata-only signals.
/// </para>
/// <para>
/// <b>Privilege is a flag, never a decision (ADR-015).</b> When the classification flags privilege, the rung
/// emits an additional metadata-only signal (<c>Category="privilege-flag"</c>) — a signal for the reviewer,
/// never a filing/redaction decision.
/// </para>
/// <para>
/// <b>Facade + lifetime.</b> Consumes AI via the <see cref="ICommunicationClassificationAi"/> PublicContracts
/// facade (refined ADR-013) — never <see cref="IOpenAiClient"/> directly. The facade is scoped and this rung
/// is a singleton (the engine holds rungs as <c>IEnumerable&lt;IAssociationRung&gt;</c>), so the facade is
/// resolved from a per-evaluation scope via <see cref="IServiceScopeFactory"/> — exactly like
/// <see cref="SemanticMatchRung"/>.
/// </para>
/// <para>
/// Best-effort/non-fatal (NFR-06): a throw is treated by the engine as a non-match; the facade returns null
/// when disabled. Self-gated by <c>Communication:AiClassification:Enabled</c>. Registered as a concrete
/// <see cref="IAssociationRung"/> singleton in <c>CommunicationModule</c> (ADR-010).
/// </para>
/// </remarks>
public sealed class AiClassificationRung : IAssociationRung
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptions<AiClassificationOptions> _options;
    private readonly ILogger<AiClassificationRung> _logger;

    /// <summary>
    /// Fixed confidence stamped on the metadata-only signals — modest and well below any auto-file threshold
    /// (default 0.85). Signals do not enter field-winner aggregation, so this value only annotates the trace;
    /// it is deliberately conservative to reflect that this is the last-resort AI rung.
    /// </summary>
    private const double SignalConfidence = 0.60;

    public AiClassificationRung(
        IServiceScopeFactory scopeFactory,
        IOptions<AiClassificationOptions> options,
        ILogger<AiClassificationRung> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options;
        _logger = logger;
    }

    public RungKind Kind => RungKind.AiClassification;
    public int Order => 5;

    public async Task<IReadOnlyList<RungMatch>> EvaluateAsync(
        NormalizedMessage message, AssociationContext context, CancellationToken ct)
    {
        var opts = _options.Value;

        // Telemetry hooks (fired/skipped, latency, signal-count) — task 032 consumes these structured events.
        if (!opts.Enabled)
        {
            _logger.LogDebug("Rung 5 (AI classify) skipped — disabled via Communication:AiClassification:Enabled.");
            return Array.Empty<RungMatch>();
        }

        var subject = message.Subject?.Trim() ?? string.Empty;
        var body = BuildBody(message.BodyText, opts.MaxInputChars);

        if (subject.Length == 0 && body.Length == 0)
        {
            _logger.LogDebug("Rung 5 (AI classify) skipped — no input text (empty subject + body).");
            return Array.Empty<RungMatch>();
        }

        var startTs = Stopwatch.GetTimestamp();

        CommunicationClassificationResult? result;
        using (var scope = _scopeFactory.CreateScope())
        {
            var classifier = scope.ServiceProvider.GetRequiredService<ICommunicationClassificationAi>();
            result = await classifier.ClassifyAsync(subject, body, ct);
        }

        var matches = MapResult(result, opts);
        var elapsed = Stopwatch.GetElapsedTime(startTs);

        _logger.LogInformation(
            "Rung 5 (AI classify) fired | SubjectChars: {SubjectChars}, BodyChars: {BodyChars}, Signals: {SignalCount}, PrivilegeFlagged: {Privilege}, ElapsedMs: {ElapsedMs}",
            subject.Length, body.Length, matches.Count,
            result?.PrivilegeFlagged ?? false, (long)elapsed.TotalMilliseconds);

        return matches;
    }

    /// <summary>
    /// Composes the classify body from the plain-text body, bounded to <paramref name="maxChars"/> (ADR-016
    /// token budget). Whitespace-collapsed so formatting does not consume the char budget.
    /// </summary>
    private static string BuildBody(string? bodyText, int maxChars)
    {
        if (string.IsNullOrWhiteSpace(bodyText))
            return string.Empty;

        var collapsed = string.Join(' ', bodyText.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return collapsed.Length > maxChars ? collapsed[..maxChars] : collapsed;
    }

    /// <summary>
    /// Maps the classification into metadata-only <see cref="RungMatch"/> signals (no target, no regarding
    /// field). Emits the primary triage signal (category + obligations + rationale in provenance) plus, when
    /// flagged, a separate privilege-flag signal. Returns empty when the classification yields nothing useful.
    /// </summary>
    private IReadOnlyList<RungMatch> MapResult(CommunicationClassificationResult? result, AiClassificationOptions opts)
    {
        if (result is null || !HasUsefulSignal(result))
            return Array.Empty<RungMatch>();

        var matches = new List<RungMatch>(2);

        var candidateTypes = result.CandidateRecordTypes
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Take(opts.MaxCandidateTypes)
            .ToArray();

        var urgency = string.IsNullOrWhiteSpace(result.Urgency) ? "unspecified" : result.Urgency;
        var rationale = string.IsNullOrWhiteSpace(result.Rationale) ? "(none)" : result.Rationale;
        var actions = result.SuggestedActions is { Count: > 0 }
            ? string.Join(",", result.SuggestedActions)
            : "(none)";

        // Primary triage signal. Category is required for the mapper to record a signal; fall back to a
        // sentinel so a classification with useful obligations/types/rationale but no explicit category is
        // still captured as the W5 triage substrate. Metadata-only (Target/RegardingFieldName null) ⇒ this is
        // a SIGNAL, never a regarding write; it never auto-files.
        var category = string.IsNullOrWhiteSpace(result.Category) ? "communication-triage" : result.Category;
        matches.Add(new RungMatch
        {
            RegardingFieldName = null,
            Target = null,
            Confidence = SignalConfidence,
            Category = category,
            Obligations = result.Obligations,
            Provenance =
                $"ai-classify:category={category}:urgency={urgency}:types=[{string.Join(",", candidateTypes)}]:actions=[{actions}]:{rationale}",
            Rung = RungKind.AiClassification,
        });

        // Privilege is a SIGNAL only (ADR-015) — never a filing decision. Emitted as a separate metadata-only
        // signal so the reviewer sees it distinctly in provenance.signals.
        if (result.PrivilegeFlagged)
        {
            matches.Add(new RungMatch
            {
                RegardingFieldName = null,
                Target = null,
                Confidence = SignalConfidence,
                Category = "privilege-flag",
                Provenance = "ai-classify:privilege-flagged (signal only; ADR-015 — reviewer decides)",
                Rung = RungKind.AiClassification,
            });
        }

        return matches;
    }

    /// <summary>
    /// True when the classification carries at least one actionable signal. Guards against emitting an empty
    /// "communication-triage" signal for a message the model found nothing in.
    /// </summary>
    private static bool HasUsefulSignal(CommunicationClassificationResult r) =>
        r.CandidateRecordTypes.Count > 0
        || !string.IsNullOrWhiteSpace(r.Category)
        || r.Obligations.Count > 0
        || r.SuggestedActions.Count > 0
        || !string.IsNullOrWhiteSpace(r.Rationale)
        || r.PrivilegeFlagged;
}
