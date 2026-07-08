using System.Text.Json;

namespace Sprk.Bff.Api.Services.Ai.LinearConsumers;

/// <summary>
/// Executes a resolved <see cref="AnalysisAction"/> against extracted document
/// text and returns the raw structured-completion JSON as a <see cref="JsonElement"/>.
/// </summary>
/// <remarks>
/// <para>
/// R7 Wave 12 (2026-07-02). The linear path's LLM call site — sits between
/// <see cref="IDocumentTextSource"/> and the consumer service's typed
/// deserialization.
/// </para>
/// <para>
/// Deliberately minimal: no template engine, no dispatch registry, no
/// context-shape adaptation. The Action row's SystemPrompt is used verbatim
/// as the prompt (with a single <c>{{document.extractedText}}</c> substitution),
/// its OutputSchemaJson drives structured decoding, its Temperature (nullable
/// → 0.0f default) and any per-consumer ModelDeployment override drive
/// generation params.
/// </para>
/// </remarks>
public interface IActionRunner
{
    /// <summary>
    /// Run the action's prompt over the document text and return the raw
    /// structured-completion JSON as a <see cref="JsonElement"/>. Consumer
    /// services parse this via <see cref="JsonElement.GetProperty(string)"/>
    /// into their typed intermediate.
    /// </summary>
    Task<JsonElement> RunAsync(
        AnalysisAction action,
        DocumentText documentText,
        LinearRunContext context,
        CancellationToken cancellationToken);
}
