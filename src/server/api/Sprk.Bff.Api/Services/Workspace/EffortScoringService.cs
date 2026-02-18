using System.Text;

namespace Sprk.Bff.Api.Services.Workspace;

public record EffortScoreInput(
    string EventType,
    bool HasMultipleParties,
    bool IsCrossJurisdiction,
    bool IsRegulatory,
    bool IsHighValue,
    bool IsTimeSensitive);

public record EffortScoreResult(
    int Score,
    string Level,
    int BaseEffort,
    string EventType,
    IReadOnlyList<AppliedMultiplier> AppliedMultipliers,
    string ReasonString);

public record AppliedMultiplier(
    string Name,
    decimal Value);

/// <summary>
/// Deterministic effort scoring engine for Legal Operations Workspace events.
/// Calculates effort scores using a base effort table and multiplicative complexity
/// multipliers. All scoring is table-driven; same inputs always produce identical outputs.
/// Registered as a concrete type per ADR-010 (DI minimalism).
/// </summary>
public class EffortScoringService
{
    // ──────────────────────────────────────────────────────────────────────────
    // Base effort table (case-insensitive lookup)
    // ──────────────────────────────────────────────────────────────────────────

    private static readonly Dictionary<string, int> BaseEffortTable = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Email"] = 15,
        ["DocumentReview"] = 25,
        ["Task"] = 20,
        ["Invoice"] = 30,
        ["Meeting"] = 20,
        ["Analysis"] = 35,
        ["AlertResponse"] = 10
    };

    private const int DefaultBaseEffort = 20;

    // ──────────────────────────────────────────────────────────────────────────
    // Public API
    // ──────────────────────────────────────────────────────────────────────────

    public EffortScoreResult CalculateEffortScore(EffortScoreInput input)
    {
        var baseEffort = BaseEffortTable.GetValueOrDefault(input.EventType, DefaultBaseEffort);
        var appliedMultipliers = new List<AppliedMultiplier>();

        if (input.HasMultipleParties) appliedMultipliers.Add(new("Multiple parties", 1.3m));
        if (input.IsCrossJurisdiction) appliedMultipliers.Add(new("Cross-jurisdiction", 1.2m));
        if (input.IsRegulatory) appliedMultipliers.Add(new("Regulatory", 1.1m));
        if (input.IsHighValue) appliedMultipliers.Add(new("High value", 1.2m));
        if (input.IsTimeSensitive) appliedMultipliers.Add(new("Time-sensitive", 1.3m));

        var multiplierProduct = appliedMultipliers.Aggregate(1.0m, (acc, m) => acc * m.Value);
        var rawScore = baseEffort * multiplierProduct;
        var finalScore = Math.Min((int)Math.Round(rawScore, MidpointRounding.AwayFromZero), 100);
        var level = GetEffortLevel(finalScore);
        var reasonString = BuildReasonString(input.EventType, baseEffort, appliedMultipliers, finalScore, level);

        return new EffortScoreResult(finalScore, level, baseEffort, input.EventType, appliedMultipliers, reasonString);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Private helpers
    // ──────────────────────────────────────────────────────────────────────────

    private static string GetEffortLevel(int score) => score switch
    {
        >= 70 => "High",
        >= 40 => "Med",
        _ => "Low"
    };

    private static string BuildReasonString(
        string eventType,
        int baseEffort,
        IReadOnlyList<AppliedMultiplier> appliedMultipliers,
        int finalScore,
        string level)
    {
        // Format event type for display: convert camelCase/PascalCase to spaced words
        var displayEventType = FormatEventTypeForDisplay(eventType);

        var sb = new StringBuilder();
        sb.Append($"Base: {displayEventType} ({baseEffort})");

        foreach (var multiplier in appliedMultipliers)
        {
            sb.Append($" \u00d7 {multiplier.Name} ({multiplier.Value:0.#}x)");
        }

        sb.Append($" = {finalScore} ({level} effort)");
        return sb.ToString();
    }

    private static string FormatEventTypeForDisplay(string eventType)
    {
        if (string.IsNullOrEmpty(eventType))
            return "Unknown";

        // Insert spaces before uppercase letters to split PascalCase/camelCase
        var sb = new StringBuilder();
        for (var i = 0; i < eventType.Length; i++)
        {
            if (i > 0 && char.IsUpper(eventType[i]))
                sb.Append(' ');
            sb.Append(eventType[i]);
        }

        return sb.ToString();
    }
}
