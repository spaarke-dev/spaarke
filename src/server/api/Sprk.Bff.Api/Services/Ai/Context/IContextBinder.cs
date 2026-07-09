using System.Text.Json;

namespace Sprk.Bff.Api.Services.Ai.Context;

/// <summary>
/// THE platform input-resolution seam (ADR-043 Move 1 / E-10). Resolves an Action's declared inputs
/// into TWO architecturally distinct roles — grounding <b>CONTEXT</b> (a frozen
/// <see cref="Sprk.Bff.Api.Services.Ai.PublicContracts.ContextEnvelope"/>, task-015) and the Action's
/// <c>sprk_inputschema</c>-typed <b>OPERAND</b> (the <c>## Input</c> / <c>## Document</c> channel) — and
/// writes the ContextEnvelope fingerprint (ADR-040 store-before-render; task-038's dark writer seam).
/// </summary>
/// <remarks>
/// <para>
/// "One input-resolution model" (ADR-043): ONE binder resolves BOTH roles. The operand is NOT an
/// envelope slice (it is volatile + per-action-typed vs the envelope's stable platform-standard shape).
/// No engine reads hardcoded session state — the operand is resolved deterministically over the declared
/// schema (ADR-039) and, where declared, by reference to a prior ledger output (ADR-040 <c>ledger_resolution</c>).
/// </para>
/// <para>
/// <b>Anti-recurrence (ADR-043)</b>: the binder is designed against ALL THREE completion consumers upfront
/// — <see cref="LinearConsumers.ActionRunner"/> (E-10, this task), <c>AiCompletionNodeExecutor</c> and
/// <c>DailyBriefingNarrator</c> (E-12). Consumers 2 and 3 supply a pre-resolved operand via
/// <see cref="ContextBindingRequest.PreResolvedInput"/>; consumer 1 supplies args/ledger/file sources. All
/// three yield the same <see cref="BoundInputs"/> shape rendered through the single-source
/// <see cref="PromptInputSection"/> — so E-12 is a call-site swap, not an abstraction reshape.
/// </para>
/// </remarks>
public interface IContextBinder
{
    /// <summary>
    /// Deterministic predicate (ADR-039): does the declared <paramref name="inputSchemaJson"/> + the
    /// dispatch <paramref name="args"/> carry a STRUCTURED operand — a declared <c>selectionText</c> /
    /// <c>changesText</c> / <c>documentText</c> arg value, or a <c>ledger_resolution</c> reference — rather
    /// than resolving from session files? Drives the dispatch orchestrator's branch (structured-operand
    /// actions skip the session-file fetch; file actions keep the shipped path).
    /// </summary>
    bool HasStructuredOperand(string? inputSchemaJson, JsonElement? args);

    /// <summary>
    /// Resolve the request's inputs into <see cref="BoundInputs"/> (grounding context + operand) and, when
    /// session coordinates are supplied, write the ContextEnvelope fingerprint via
    /// <c>ChatSessionManager.AppendContextFingerprintAsync</c> (ADR-040 store-before-render; NFR-07 ids/counts
    /// only). Operand precedence: (1) <see cref="ContextBindingRequest.PreResolvedInput"/>, (2) a
    /// <c>ledger_resolution</c> reference in <see cref="ContextBindingRequest.Args"/>, (3) a declared operand
    /// field value in <c>Args</c>, (4) <see cref="ContextBindingRequest.FileDocument"/>, else
    /// <see cref="ResolvedOperand.None"/>.
    /// </summary>
    Task<BoundInputs> BindAsync(ContextBindingRequest request, CancellationToken ct);
}
