using System.Text;

namespace Sprk.Bff.Api.Services.Ai.Classification;

/// <summary>
/// Assembles the REGISTRY-DRIVEN candidate key set into the <c>agreement-classify</c> Action prompt at
/// dispatch time. Given the live <c>sprk_agreementtype</c> rows, it formats a deterministic registry
/// block (each key + display label + classification cue, with the fallback row flagged) and substitutes
/// it into the Action's flat <c>systemPrompt</c> in place of the <see cref="PlaceholderToken"/> — so the
/// classifier's valid <c>subDomainKey</c> set is supplied as PROMPT DATA, never hardcoded.
/// </summary>
/// <remarks>
/// <para>
/// ai-advanced-capabilities-agreements-r1 task 020 (FR-07 / design Lens 3d). This is the "assembly path"
/// the registry-driven acceptance criterion tests: adding a <c>sprk_agreementtype</c> row extends the
/// injected key set with ZERO code change (proven with a stub row in
/// <c>AgreementTypeRegistryPromptAssemblerTests</c>).
/// </para>
/// <para>
/// The classifier Action is dispatched via <see cref="LinearConsumers.ActionRunner"/> (the ADR-016
/// Reasoning-tier path). ActionRunner renders the flat <c>systemPrompt</c> and uses the static
/// <c>OutputSchemaJson</c> for constrained decoding — it does not resolve <c>$choices</c> or apply
/// template parameters at runtime — so the registry-driven set is injected HERE, before dispatch, by
/// materializing the Action's prompt. ActionRunner / PromptSchemaRenderer are unchanged (ADR-013 reuse;
/// no new executor, no new endpoint).
/// </para>
/// <para>
/// Stateless and pure (no I/O); registered as a singleton. The Dataverse read is
/// <see cref="IAgreementTypeRegistryReader"/>'s responsibility — kept separate so this formatting logic
/// is unit-testable with stub rows.
/// </para>
/// </remarks>
public sealed class AgreementTypeRegistryPromptAssembler
{
    /// <summary>
    /// The token in the <c>agreement-classify</c> Action's <c>systemPrompt</c> that
    /// <see cref="Materialize"/> replaces with the assembled registry block.
    /// </summary>
    public const string PlaceholderToken = "{{agreementTypeRegistry}}";

    private readonly ILogger<AgreementTypeRegistryPromptAssembler>? _logger;

    public AgreementTypeRegistryPromptAssembler(ILogger<AgreementTypeRegistryPromptAssembler>? logger = null)
    {
        _logger = logger;
    }

    /// <summary>
    /// Returns a copy of <paramref name="action"/> whose <c>SystemPrompt</c> has the
    /// <see cref="PlaceholderToken"/> replaced by the registry block built from
    /// <paramref name="rows"/>. All other Action fields (model tier, temperature, output schema) are
    /// unchanged, so the materialized Action dispatches through the existing
    /// <see cref="LinearConsumers.ActionRunner"/> path on the Reasoning tier.
    /// </summary>
    public AnalysisAction Materialize(AnalysisAction action, IReadOnlyList<AgreementTypeRow> rows)
    {
        ArgumentNullException.ThrowIfNull(action);
        ArgumentNullException.ThrowIfNull(rows);

        var block = BuildRegistryBlock(rows);

        if (!action.SystemPrompt.Contains(PlaceholderToken, StringComparison.Ordinal))
        {
            // Misconfiguration guard: the classifier Action is expected to carry the placeholder.
            // Do NOT append the block unanchored — that would place it after the output instructions.
            _logger?.LogWarning(
                "agreement-classify systemPrompt does not contain the {Token} placeholder; " +
                "the registry block was NOT injected. Verify the deployed Action prompt.",
                PlaceholderToken);
            return action;
        }

        var materialized = action.SystemPrompt.Replace(PlaceholderToken, block, StringComparison.Ordinal);
        return action with { SystemPrompt = materialized };
    }

    /// <summary>
    /// Builds the deterministic registry block: specific (non-fallback) types first, then the fallback
    /// row, each sorted by key. Every row emits its key, a display label (from <c>sprk_name</c>, or
    /// derived from the key when absent), and a classification cue (from <c>sprk_classificationcue</c>,
    /// or a key-derived guidance line when null — task 001 populated cues only on nda + general). The
    /// fallback row is flagged and named in a trailing summary line, so the model selects it via the
    /// registry flag, never a hardcoded key.
    /// </summary>
    public string BuildRegistryBlock(IReadOnlyList<AgreementTypeRow> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);

        var sb = new StringBuilder();
        sb.AppendLine(
            "AGREEMENT TYPE REGISTRY (the closed set of sub-domain keys you may use for subDomainKey — " +
            "copy the key verbatim; never emit a key that is not listed here):");
        sb.AppendLine();

        if (rows.Count == 0)
        {
            sb.AppendLine("(No agreement types are currently registered.)");
            return sb.ToString().TrimEnd();
        }

        // Deterministic order: specific types (by key) first, then fallback row(s) (by key).
        var ordered = rows
            .OrderBy(r => r.IsFallback)
            .ThenBy(r => r.Key, StringComparer.Ordinal)
            .ToList();

        foreach (var row in ordered)
        {
            var label = ResolveLabel(row);
            sb.Append("- key: \"").Append(row.Key).Append("\" — ").Append(label);
            if (row.IsFallback)
            {
                sb.Append("  [FALLBACK: emit this key when the document is an agreement but matches no specific type above]");
            }
            sb.AppendLine();
            sb.Append("  cue: ").AppendLine(ResolveCue(row, label));
        }

        sb.AppendLine();
        var fallbackKey = ordered.FirstOrDefault(r => r.IsFallback)?.Key;
        sb.Append("Registered type count: ").Append(ordered.Count).Append('.');
        sb.Append(fallbackKey is not null
            ? $" The fallback key is \"{fallbackKey}\"."
            : " No fallback key is registered.");
        sb.Append(" Use ONLY the keys listed above.");

        return sb.ToString().TrimEnd();
    }

    private static string ResolveLabel(AgreementTypeRow row)
        => !string.IsNullOrWhiteSpace(row.Name) ? row.Name!.Trim() : DeriveTitleFromKey(row.Key);

    private static string ResolveCue(AgreementTypeRow row, string label)
        => !string.IsNullOrWhiteSpace(row.ClassificationCue)
            ? CollapseWhitespace(row.ClassificationCue!)
            : $"No specific classification cue is registered for this type yet; classify a document as " +
              $"\"{row.Key}\" only when its primary subject matter matches the ordinary meaning of \"{label}\".";

    /// <summary>Derives a title-cased label from a hyphenated key (e.g. "asset-purchase" -> "Asset Purchase").</summary>
    private static string DeriveTitleFromKey(string key)
    {
        var parts = key.Split(new[] { '-', '_', ' ' }, StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 0
            ? key
            : string.Join(' ', parts.Select(p => char.ToUpperInvariant(p[0]) + p[1..]));
    }

    /// <summary>Collapses internal newlines/runs of whitespace so a multi-line cue renders as one prompt line.</summary>
    private static string CollapseWhitespace(string text)
    {
        var sb = new StringBuilder(text.Length);
        var lastWasSpace = false;
        foreach (var ch in text.Trim())
        {
            if (char.IsWhiteSpace(ch))
            {
                if (!lastWasSpace) sb.Append(' ');
                lastWasSpace = true;
            }
            else
            {
                sb.Append(ch);
                lastWasSpace = false;
            }
        }
        return sb.ToString();
    }
}
