using System.Text.Json;

namespace Sprk.Bff.Api.Services.Ai;

/// <summary>
/// THE single-source producer of the <c>## Input</c> prompt section (ADR-043 Move 1 / E-10).
/// </summary>
/// <remarks>
/// <para>
/// ADR-043 makes <c>## Input</c> a <b>single-source producer</b>: every completion consumer
/// (the dispatch engine <see cref="LinearConsumers.ActionRunner"/>, the playbook node engine
/// <c>AiCompletionNodeExecutor</c>, and the hand-replica in <c>DailyBriefingNarrator</c>) renders
/// the Action's structured operand through THIS one method so parity is <b>structural, not
/// conventional</b>. <see cref="PromptSchemaRenderer"/>'s JPS Layer-2 <c>## Input</c> block delegates
/// here, and the flat-text completion path (compose actions are flat-text) appends it directly — so
/// there is exactly ONE code path emitting the section, on both prompt formats.
/// </para>
/// <para>
/// <b>Frozen output format</b> (guarded by a golden-string assertion in
/// <c>tests/integration/seam/**</c> from E-10 onward): the section is the literal header
/// <c>## Input</c>, a blank line, the operand serialized with <see cref="JsonSerializerOptions.WriteIndented"/>,
/// then a single trailing newline. Newlines are LF-normalized so the contract is deterministic across
/// platforms (protects the <c>DailyBriefingNarrator</c> replica until E-12 retires it onto this producer).
/// Changing indentation, key order, casing, null handling, or the header string MUST fail that golden test.
/// </para>
/// </remarks>
public static class PromptInputSection
{
    /// <summary>The frozen section header.</summary>
    public const string Header = "## Input";

    /// <summary>
    /// The frozen serialization options: indented JSON. Key casing/order is preserved from the
    /// supplied <see cref="JsonElement"/> (the caller owns the operand's shape) — the renderer never
    /// re-cases it, so a camelCase element renders camelCase (matching the narrator replica).
    /// </summary>
    private static readonly JsonSerializerOptions IndentedOptions = new() { WriteIndented = true };

    /// <summary>
    /// True when <paramref name="input"/> carries a renderable operand (non-null, non-<c>Null</c>,
    /// non-<c>Undefined</c>). Mirrors the guard <see cref="PromptSchemaRenderer"/> applied inline before E-10.
    /// </summary>
    public static bool HasInput(JsonElement? input) =>
        input is { ValueKind: not (JsonValueKind.Null or JsonValueKind.Undefined) };

    /// <summary>
    /// Renders the frozen <c>## Input</c> section for <paramref name="input"/>, or
    /// <see cref="string.Empty"/> when there is no renderable operand.
    /// </summary>
    /// <remarks>
    /// Output shape (LF): <c>"## Input\n\n{indented-json}\n"</c>. Callers place their own separator
    /// before/after as needed (the JPS renderer appends a trailing blank line to match its pre-E-10 layout).
    /// </remarks>
    public static string Render(JsonElement? input)
    {
        if (!HasInput(input))
        {
            return string.Empty;
        }

        // LF-normalize so the frozen contract does not depend on the host platform's newline.
        var json = JsonSerializer.Serialize(input!.Value, IndentedOptions).Replace("\r\n", "\n");
        return $"{Header}\n\n{json}\n";
    }
}
