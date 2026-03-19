namespace Sprk.Bff.Api.Models.Ai.Chat;

/// <summary>
/// Result of the <see cref="Services.Ai.Chat.PlaybookDispatcher"/> two-stage intent matching.
///
/// Contains the matched playbook identity, confidence score, extracted parameters from
/// the user message, output delivery type, and confirmation requirement.
///
/// <para>
/// <see cref="OutputType"/> and <see cref="RequiresConfirmation"/> are populated from the
/// playbook's JPS definition (DeliverOutput node) at match time, per spec FR-18.
/// They are NOT hardcoded or statically inferred.
/// </para>
/// </summary>
/// <param name="Matched">Whether a playbook was matched. When false, all other fields are default/empty.</param>
/// <param name="PlaybookId">Matched playbook identifier (sprk_aiplaybook GUID string). Null when not matched.</param>
/// <param name="PlaybookName">Display name of the matched playbook. Null when not matched.</param>
/// <param name="Confidence">Confidence score (0.0 to 1.0). Higher is more confident.</param>
/// <param name="OutputType">
/// How the playbook result should be delivered: text, dialog, navigation, download, insert.
/// Sourced from the playbook's DeliverOutput node in the JPS definition (FR-18).
/// </param>
/// <param name="RequiresConfirmation">
/// Whether the user must confirm before playbook execution begins.
/// Sourced from the playbook's DeliverOutput node RequiresConfirmation field (FR-18).
/// Defaults to true for dialog/navigation outputs, false for text outputs.
/// </param>
/// <param name="ExtractedParameters">
/// Parameter values extracted from the user message by the LLM refinement stage.
/// Keys are parameter names (e.g., "recipient", "date"); values are the extracted text.
/// Empty dictionary when no parameters are detected or Stage 2 was skipped.
/// </param>
/// <param name="TargetPage">
/// Code Page web resource name for dialog/navigation outputs (e.g., "sprk_emailcomposer").
/// Null for text/download/insert outputs. Sourced from JPS DeliverOutput node.
/// </param>
public sealed record DispatchResult(
    bool Matched,
    string? PlaybookId,
    string? PlaybookName,
    double Confidence,
    OutputType OutputType,
    bool RequiresConfirmation,
    Dictionary<string, string> ExtractedParameters,
    string? TargetPage)
{
    /// <summary>
    /// Returns a result indicating no playbook was matched.
    /// </summary>
    public static DispatchResult NoMatch { get; } = new(
        Matched: false,
        PlaybookId: null,
        PlaybookName: null,
        Confidence: 0,
        OutputType: OutputType.Text,
        RequiresConfirmation: false,
        ExtractedParameters: new Dictionary<string, string>(),
        TargetPage: null);
}
