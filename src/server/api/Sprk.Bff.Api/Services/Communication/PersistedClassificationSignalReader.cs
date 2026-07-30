using System.Text.Json;
using Sprk.Bff.Api.Models.Ai.Communication;
using Sprk.Bff.Api.Services.Communication.Engine;

namespace Sprk.Bff.Api.Services.Communication;

/// <summary>
/// Reconstructs the <see cref="CommunicationClassificationResult"/>
/// <see cref="Engine.Rungs.AiClassificationRung"/> already produced from the persisted
/// <c>sprk_communication.sprk_associationprovenance</c> JSON (<see cref="AssociationProvenance"/>) —
/// WITHOUT re-invoking classification (FR-05 "no second full LLM pass").
/// </summary>
/// <remarks>
/// <para>
/// <b>Why a reconstruction, not a live handoff.</b> Rung 5 runs inside the Association Engine (at the
/// capture boundary, via <see cref="IncomingAssociationResolver"/>) — a different call site + scope than
/// <see cref="CommunicationEnrichmentService.EnrichAsync"/>, which fires later in the pipeline (FR-08 step
/// order). <see cref="Engine.AssociationStatusMapper"/> (frozen for this task — task 021 owns it
/// concurrently) already threads the rung's <c>RungMatch</c> into <c>provenance.signals</c> and
/// <see cref="IncomingAssociationResolver"/> persists that provenance to
/// <c>sprk_communication.sprk_associationprovenance</c> before enrichment runs. Reading THAT back is the
/// non-invasive way to reuse the signal without touching either frozen file or adding a second live path.
/// </para>
/// <para>
/// <b>Format.</b> <see cref="Engine.Rungs.AiClassificationRung.MapResult"/> encodes the classification into
/// a <c>SignalTrace</c> whose <c>Provenance</c> string is
/// <c>"ai-classify:category={category}:urgency={urgency}:types=[{csv}]:actions=[{csv}]:{rationale}"</c>,
/// with <c>Obligations</c> carried structurally (no string-parsing needed for that field) and a SEPARATE
/// <c>SignalTrace</c> (<c>Category == "privilege-flag"</c>) when the classification flagged privilege.
/// </para>
/// <para>
/// <b>Best-effort by construction.</b> Every method here returns <c>null</c>/<c>false</c> rather than
/// throwing on any parse/shape mismatch — a communication with no persisted AI-classify signal (outbound
/// messages today; inbound messages where rung 5 was disabled or found nothing useful) simply yields no
/// reconstructed result, and the caller treats that as "nothing to triage yet" (NFR-04).
/// </para>
/// </remarks>
public static class PersistedClassificationSignalReader
{
    private const string AiClassifyPrefix = "ai-classify:category=";
    private const string UrgencyMarker = ":urgency=";
    private const string TypesMarker = ":types=[";
    private const string ActionsMarker = "]:actions=[";
    private const string RationaleMarker = "]:";
    private const string PrivilegeFlagCategory = "privilege-flag";

    /// <summary>
    /// The sentinel <see cref="Engine.Rungs.AiClassificationRung.MapResult"/> stores when the original
    /// classification carried no category — mapped back to <c>null</c> here so the reconstructed result
    /// matches the ORIGINAL signal shape (a null category is meaningful: the TRIAGE-EMAIL Action's own
    /// instruction treats it as "sparse signal — fall back to message text").
    /// </summary>
    private const string NoCategorySentinel = "communication-triage";

    /// <summary>
    /// Deserializes the raw <c>sprk_associationprovenance</c> JSON (as read from the
    /// <c>sprk_communication</c> record) and reconstructs the classification result. Returns <c>null</c>
    /// when the column is empty, malformed, or carries no AI-classify signal.
    /// </summary>
    public static CommunicationClassificationResult? TryReadFromProvenanceJson(string? provenanceJson) =>
        TryReconstruct(TryDeserializeProvenance(provenanceJson));

    /// <summary>
    /// Deserializes the raw <c>sprk_associationprovenance</c> JSON into the full <see cref="AssociationProvenance"/>
    /// document (not just the classification-signal slice <see cref="TryReadFromProvenanceJson"/> reconstructs).
    /// Used by task 024's RI-confidence scorer to read
    /// <see cref="AssociationDecisionTrace.TopDeterministicConfidence"/> — the deterministic-rung agreement
    /// factor — from the SAME persisted document, without a second parse of the JSON or a second read helper.
    /// Returns <c>null</c> when the column is empty or malformed (best-effort, per NFR-04).
    /// </summary>
    public static AssociationProvenance? TryDeserializeProvenance(string? provenanceJson)
    {
        if (string.IsNullOrWhiteSpace(provenanceJson))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<AssociationProvenance>(
                provenanceJson,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Reconstructs the classification result from an already-deserialized
    /// <see cref="AssociationProvenance"/>. Returns <c>null</c> when no AI-classify signal is present.
    /// </summary>
    public static CommunicationClassificationResult? TryReconstruct(AssociationProvenance? provenance)
    {
        if (provenance?.Signals is not { Count: > 0 } signals)
        {
            return null;
        }

        var privilegeFlagged = signals.Any(s =>
            string.Equals(s.Category, PrivilegeFlagCategory, StringComparison.Ordinal));

        var primary = signals.FirstOrDefault(s =>
            !string.Equals(s.Category, PrivilegeFlagCategory, StringComparison.Ordinal)
            && s.Provenance.StartsWith(AiClassifyPrefix, StringComparison.Ordinal));

        if (primary is null)
        {
            return null;
        }

        if (!TryParseAiClassifyProvenance(primary.Provenance, out var category, out var urgency, out var types, out var actions, out var rationale))
        {
            return null;
        }

        return new CommunicationClassificationResult
        {
            CandidateRecordTypes = types,
            Category = string.Equals(category, NoCategorySentinel, StringComparison.Ordinal) ? null : category,
            Urgency = string.Equals(urgency, "unspecified", StringComparison.Ordinal) ? null : urgency,
            Obligations = primary.Obligations,
            SuggestedActions = actions,
            PrivilegeFlagged = privilegeFlagged,
            Rationale = string.Equals(rationale, "(none)", StringComparison.Ordinal) ? null : rationale,
        };
    }

    /// <summary>
    /// Parses <c>Engine.Rungs.AiClassificationRung.MapResult</c>'s exact provenance string format. Pure +
    /// side-effect-free — the mechanical unit-test target for this reconstruction (no live model needed).
    /// </summary>
    internal static bool TryParseAiClassifyProvenance(
        string provenance,
        out string category,
        out string urgency,
        out IReadOnlyList<string> types,
        out IReadOnlyList<string> actions,
        out string rationale)
    {
        category = string.Empty;
        urgency = string.Empty;
        types = Array.Empty<string>();
        actions = Array.Empty<string>();
        rationale = string.Empty;

        if (!provenance.StartsWith(AiClassifyPrefix, StringComparison.Ordinal))
        {
            return false;
        }

        var rest = provenance[AiClassifyPrefix.Length..];

        var urgencyIdx = rest.IndexOf(UrgencyMarker, StringComparison.Ordinal);
        if (urgencyIdx < 0) return false;
        category = rest[..urgencyIdx];
        rest = rest[(urgencyIdx + UrgencyMarker.Length)..];

        var typesIdx = rest.IndexOf(TypesMarker, StringComparison.Ordinal);
        if (typesIdx < 0) return false;
        urgency = rest[..typesIdx];
        rest = rest[(typesIdx + TypesMarker.Length)..];

        var actionsIdx = rest.IndexOf(ActionsMarker, StringComparison.Ordinal);
        if (actionsIdx < 0) return false;
        var typesRaw = rest[..actionsIdx];
        rest = rest[(actionsIdx + ActionsMarker.Length)..];

        var rationaleIdx = rest.IndexOf(RationaleMarker, StringComparison.Ordinal);
        if (rationaleIdx < 0) return false;
        var actionsRaw = rest[..rationaleIdx];
        rationale = rest[(rationaleIdx + RationaleMarker.Length)..];

        types = SplitCsv(typesRaw);
        actions = actionsRaw == "(none)" ? Array.Empty<string>() : SplitCsv(actionsRaw);

        return true;
    }

    private static IReadOnlyList<string> SplitCsv(string raw) =>
        string.IsNullOrEmpty(raw)
            ? Array.Empty<string>()
            : raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
