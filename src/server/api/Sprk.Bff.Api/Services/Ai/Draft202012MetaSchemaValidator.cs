using System.Collections.Concurrent;
using System.Text.Json.Nodes;
using Json.Schema;

namespace Sprk.Bff.Api.Services.Ai;

/// <summary>
/// The single entry point for evaluating a candidate JSON Schema against the shared JSON Schema
/// Draft 2020-12 meta-schema (<see cref="MetaSchemas.Draft202012"/>). Serializes every evaluation
/// behind one process-wide gate, and memoizes the verdict per exact schema text so a repeated schema
/// never re-enters the gate.
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
/// Serialized, the same workload produced zero wrong results (issue #1295).
/// </para>
/// <para>
/// <b>Component Justification (CLAUDE.md §11).</b> (1) <i>Existing</i> — the two callers,
/// <see cref="AnalysisToolService.MapJsonSchema"/> and
/// <c>ToolHandlerToAIFunctionAdapter.ValidateAgainstMetaSchema</c>, each called
/// <c>MetaSchemas.Draft202012.Evaluate</c> directly with identical options; nothing else in the
/// BFF evaluates JSON Schema. (2) <i>Extension</i> — a lock at each call site is NOT sufficient:
/// both sites evaluate the same static instance, so two independent locks would still let one
/// site race the other (measured: 75-111 false-valid per 1,000). The gate must be one object both
/// sites share, which is this class. (3) <i>Cost-of-doing-nothing</i> — concurrent chat turns accept
/// invalid tool schemas (see <c>Issue1295_JsonSchemaMetaSchemaConcurrentEvaluationTests</c>).
/// </para>
/// <para>
/// <b>Rule for future callers.</b> Any code that evaluates <see cref="MetaSchemas.Draft202012"/>
/// (or any other <see cref="MetaSchemas"/> member, or a schema with
/// <c>EvaluationOptions.ValidateAgainstMetaSchema = true</c>, which evaluates the meta-schema
/// internally) MUST go through this class rather than call <c>Evaluate</c> itself. Enforced by
/// <c>tests/Spaarke.ArchTests/MetaSchemaEvaluationGuardTests.cs</c>.
/// </para>
/// <para>
/// <b>Cost, and why the verdict is cached.</b> Both callers are on the PER-CHAT-MESSAGE path, not
/// once per session: every send-message call builds a new agent
/// (<c>SprkChatAgentFactory.CreateAgentAsync</c> → <c>AgentToolCatalogProjector</c>), which runs
/// <see cref="AnalysisToolService.MapJsonSchema"/> for every row <c>ListToolsAsync</c> returns (up to
/// 200, playbook-only rows included) and constructs a <c>ToolHandlerToAIFunctionAdapter</c> per
/// chat-available row, so a chat-available schema is evaluated twice per turn. Measured per
/// evaluation: about 0.12-0.19 ms for a small tool schema, but 15-17 ms (warm, idle host) to 108 ms
/// (independent review) for a 32 KB / 300-property schema. Uncached, that is up to ~216 ms of
/// EXCLUSIVE gate time per turn for one such schema, and N concurrent turns queue on the gate with
/// thread-pool threads blocked. So the verdict is memoized: a hit is a lock-free dictionary lookup
/// (about 0.1 µs for a small schema, 7 µs for 32 KB — the cost of hashing and comparing the key)
/// and never takes the gate; only the first evaluation of a given text does.
/// </para>
/// <para>
/// <b>Cache design.</b> Keyed by the EXACT schema text with ordinal comparison — the dictionary
/// compares the full key on every hit, so a hash collision cannot return another schema's verdict.
/// The verdict is a pure function of that text (and of the package version, fixed per process), so
/// there is no staleness and no invalidation: negative verdicts are cached too. An exception from
/// parsing or evaluation is NOT cached; that text is retried on its next call. Bounded at
/// <see cref="MaxCachedEntries"/> entries and <see cref="MaxCachedCharacters"/> characters of key
/// text in total (about 4 MB of UTF-16 worst case); once either bound is reached, new texts are
/// evaluated under the gate on every call, which is the uncached behaviour and still correct.
/// Entries are added only inside the gate, so both bounds are exact.
/// </para>
/// <para>
/// <b>ADR-009 (Redis-first caching).</b> This is a process-local memo of a deterministic CPU
/// computation, not a cache of data read from a store: there is nothing to keep coherent between
/// instances, nothing tenant- or principal-specific in the verdict, and a Redis round trip would cost
/// more than the small-schema evaluation it replaces. The ADR-009 profiling proof for an in-process
/// cache is the measurement above. It is a plain bounded <see cref="ConcurrentDictionary{TKey, TValue}"/>
/// (the BFF's existing convention for process-local sets, e.g. <c>InsightsActionRouter</c>), not
/// <c>IMemoryCache</c>: entries never expire, so a time-based cache would add configuration with
/// nothing to configure.
/// </para>
/// <para>
/// <b>Package.</b> A newer JsonSchema.Net was considered and rejected: 8.0 is a breaking rewrite
/// (JsonNode → JsonElement evaluation, build/evaluate split) and 9.x adds an Open Source Maintenance
/// Fee; neither documents concurrent-Evaluate safety on a shared instance.
/// </para>
/// </remarks>
internal static class Draft202012MetaSchemaValidator
{
    /// <summary>Upper bound on cached verdicts. A tool catalog is at most a few hundred rows.</summary>
    internal const int MaxCachedEntries = 1024;

    /// <summary>Upper bound on the summed length of cached schema texts (~4 MB of UTF-16).</summary>
    internal const long MaxCachedCharacters = 2_000_000;

    private static readonly object EvaluationGate = new();

    private static readonly ConcurrentDictionary<string, MetaSchemaEvaluation> Verdicts =
        new(StringComparer.Ordinal);

    /// <summary>Summed key length of <see cref="Verdicts"/>; read and written only inside the gate.</summary>
    private static long _cachedCharacters;

    /// <summary>
    /// Evaluates <paramref name="schemaText"/> against the Draft 2020-12 meta-schema, returning the
    /// cached verdict when this exact text has been evaluated before. Exceptions from parsing or from
    /// the library propagate unchanged and are not cached — each caller keeps its own error contract.
    /// </summary>
    /// <returns>
    /// Validity plus every error as <c>"{instanceLocation}: {message}"</c>, in evaluation order. The
    /// library's <see cref="EvaluationResults"/> is read INSIDE the gate and never handed out, so no
    /// caller can observe library state while another evaluation is running. A repeated text returns
    /// the same immutable instance.
    /// </returns>
    public static MetaSchemaEvaluation Evaluate(string schemaText)
    {
        ArgumentNullException.ThrowIfNull(schemaText);

        if (Verdicts.TryGetValue(schemaText, out var cached))
        {
            return cached;
        }

        // Parse outside the gate: System.Text.Json parsing is thread-safe and needs no serialization.
        var candidate = JsonNode.Parse(schemaText);

        lock (EvaluationGate)
        {
            // Another thread may have evaluated the same text while this one waited for the gate.
            if (Verdicts.TryGetValue(schemaText, out cached))
            {
                return cached;
            }

            var verdict = candidate is null
                ? new MetaSchemaEvaluation(false, Array.AsReadOnly(new[] { ": the schema document is the JSON literal null" }))
                : EvaluateUnderGate(candidate);

            if (Verdicts.Count < MaxCachedEntries
                && _cachedCharacters + schemaText.Length <= MaxCachedCharacters
                && Verdicts.TryAdd(schemaText, verdict))
            {
                _cachedCharacters += schemaText.Length;
            }

            return verdict;
        }
    }

    /// <summary>Caller MUST hold <see cref="EvaluationGate"/>.</summary>
    private static MetaSchemaEvaluation EvaluateUnderGate(JsonNode candidate)
    {
        var options = new EvaluationOptions
        {
            OutputFormat = OutputFormat.List,
            ValidateAgainstMetaSchema = false // this IS the meta-schema evaluation
        };

        var results = MetaSchemas.Draft202012.Evaluate(candidate, options);
        if (results.IsValid)
        {
            return new MetaSchemaEvaluation(true, Array.Empty<string>());
        }

        var errors = results.Details
            .Where(d => d.HasErrors && d.Errors is not null)
            .SelectMany(d => d.Errors!.Select(kv => $"{d.InstanceLocation}: {kv.Value}"))
            .ToArray();

        return new MetaSchemaEvaluation(false, Array.AsReadOnly(errors));
    }
}

/// <summary>Outcome of <see cref="Draft202012MetaSchemaValidator.Evaluate"/>. Immutable, because a
/// cached instance is shared by every caller that evaluates the same schema text.</summary>
internal sealed record MetaSchemaEvaluation(bool IsValid, IReadOnlyList<string> Errors);
