using System.Text.Json;
using Sprk.Bff.Api.Services.Ai.Context;

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
    /// <remarks>
    /// Pre-ADR-043 overload retained for the linear-consumer callers that already resolve a
    /// <see cref="DocumentText"/> (Doc Upload / File Summarize / Prefills / Event rules / etc.).
    /// It is a thin adapter over <see cref="RunAsync(AnalysisAction, BoundInputs, LinearRunContext, CancellationToken)"/>:
    /// the document is wrapped as a <see cref="OperandChannel.Document"/> operand with an empty context
    /// envelope, so behavior is byte-identical to the pre-E-10 path (renders under <c>## Document</c>).
    /// </remarks>
    Task<JsonElement> RunAsync(
        AnalysisAction action,
        DocumentText documentText,
        LinearRunContext context,
        CancellationToken cancellationToken);

    /// <summary>
    /// Run the action over a <see cref="BoundInputs"/> resolved by <see cref="IContextBinder"/> — the
    /// canonical completion contract (ADR-043 Move 1 / E-10). Consumes grounding context via the
    /// <see cref="BoundInputs.Context"/> envelope + the typed operand via the single-source
    /// <c>## Input</c> producer (<see cref="OperandChannel.Input"/>) or the <c>## Document</c> channel
    /// (<see cref="OperandChannel.Document"/>), instead of raw <see cref="DocumentText"/>. The no-file
    /// hard stop is relaxed: an <see cref="OperandChannel.Input"/> or <see cref="OperandChannel.None"/>
    /// operand runs without a session file.
    /// </summary>
    Task<JsonElement> RunAsync(
        AnalysisAction action,
        BoundInputs inputs,
        LinearRunContext context,
        CancellationToken cancellationToken);
}
