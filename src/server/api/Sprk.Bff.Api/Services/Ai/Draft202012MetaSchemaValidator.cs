using System.Text.Json.Nodes;
using Json.Schema;

namespace Sprk.Bff.Api.Services.Ai;

/// <summary>
/// The single entry point for evaluating a candidate JSON Schema against the shared JSON Schema
/// Draft 2020-12 meta-schema (<see cref="MetaSchemas.Draft202012"/>). Serializes every evaluation
/// behind one process-wide gate.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why a gate.</b> Json.Schema.Net (JsonSchema.Net 7.3.4) <c>JsonSchema.Evaluate</c> is NOT
/// thread-safe for concurrent calls on the SAME schema instance, and
/// <see cref="MetaSchemas.Draft202012"/> is a process-wide static (it also reaches the shared
/// vocabulary meta-schemas through <c>$ref</c>/<c>$dynamicRef</c>). Measured on this package
/// version: with 1,000 invalid and 1,000 valid documents evaluated concurrently, roughly 55-65% of
/// the invalid documents came back <c>IsValid = true</c> on a 32-core host (0.6-2.5% when pinned
/// to 2 cores). A false "valid" here is a fail-open: a malformed tool schema reaches the LLM.
/// Serialized, the same workload produced zero wrong results.
/// </para>
/// <para>
/// <b>Component Justification (CLAUDE.md §11).</b> (1) <i>Existing</i> — the two callers,
/// <see cref="AnalysisToolService.MapJsonSchema"/> and
/// <c>ToolHandlerToAIFunctionAdapter.ValidateAgainstMetaSchema</c>, each called
/// <c>MetaSchemas.Draft202012.Evaluate</c> directly with identical options; nothing else in the
/// BFF evaluates JSON Schema. (2) <i>Extension</i> — a lock at each call site is NOT sufficient:
/// both sites evaluate the same static instance, so two independent locks would still let one
/// site race the other. The gate must be one object both sites share, which is this class.
/// (3) <i>Cost-of-doing-nothing</i> — concurrent chat-session starts / tool loads accept invalid
/// tool schemas (see <c>JsonSchemaMetaSchemaConcurrentEvaluationTests</c>).
/// </para>
/// <para>
/// <b>Rule for future callers.</b> Any code that evaluates <see cref="MetaSchemas.Draft202012"/>
/// (or any other <see cref="MetaSchemas"/> member, or a schema with
/// <c>EvaluationOptions.ValidateAgainstMetaSchema = true</c>, which evaluates the meta-schema
/// internally) MUST go through this class rather than call <c>Evaluate</c> itself.
/// </para>
/// <para>
/// <b>Cost.</b> One evaluation of a small tool schema takes well under a millisecond; both callers
/// run at tool-load / chat-session-start time, not per token, so serialization is not a
/// throughput concern. A newer JsonSchema.Net was considered and rejected: 8.0 is a breaking
/// rewrite (JsonNode → JsonElement evaluation, build/evaluate split) and 9.x adds an Open Source
/// Maintenance Fee; neither documents concurrent-Evaluate safety on a shared instance.
/// </para>
/// </remarks>
internal static class Draft202012MetaSchemaValidator
{
    private static readonly object EvaluationGate = new();

    /// <summary>
    /// Evaluates <paramref name="candidateSchema"/> against the Draft 2020-12 meta-schema.
    /// Exceptions from the library propagate unchanged — each caller keeps its own error contract.
    /// </summary>
    /// <returns>
    /// Validity plus every error as <c>"{instanceLocation}: {message}"</c>, in evaluation order.
    /// The library's <see cref="EvaluationResults"/> is read INSIDE the gate and never handed out,
    /// so no caller can observe library state while another evaluation is running.
    /// </returns>
    public static MetaSchemaEvaluation Evaluate(JsonNode candidateSchema)
    {
        var options = new EvaluationOptions
        {
            OutputFormat = OutputFormat.List,
            ValidateAgainstMetaSchema = false // this IS the meta-schema evaluation
        };

        lock (EvaluationGate)
        {
            var results = MetaSchemas.Draft202012.Evaluate(candidateSchema, options);
            if (results.IsValid)
            {
                return MetaSchemaEvaluation.Valid;
            }

            var errors = results.Details
                .Where(d => d.HasErrors && d.Errors is not null)
                .SelectMany(d => d.Errors!.Select(kv => $"{d.InstanceLocation}: {kv.Value}"))
                .ToArray();

            return new MetaSchemaEvaluation(false, errors);
        }
    }
}

/// <summary>Outcome of <see cref="Draft202012MetaSchemaValidator.Evaluate"/>.</summary>
internal sealed record MetaSchemaEvaluation(bool IsValid, IReadOnlyList<string> Errors)
{
    public static MetaSchemaEvaluation Valid { get; } = new(true, Array.Empty<string>());
}
