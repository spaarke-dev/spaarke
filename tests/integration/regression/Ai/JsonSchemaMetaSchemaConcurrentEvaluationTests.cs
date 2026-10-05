using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Chat;
using Xunit;

namespace Sprk.Bff.Api.Tests.Integration.Regression.Ai;

/// <summary>
/// Regression — Json.Schema.Net 7.3.4 <c>JsonSchema.Evaluate</c> is NOT thread-safe on a shared
/// schema instance, and both production tool-schema validation sites evaluated the process-wide
/// static <c>MetaSchemas.Draft202012</c> without synchronization:
/// <see cref="AnalysisToolService.MapJsonSchema"/> (Dataverse <c>sprk_jsonschema</c> mapping) and the
/// <see cref="ToolHandlerToAIFunctionAdapter"/> constructor (chat-session tool exposure). Under
/// concurrency a semantically INVALID tool schema evaluated as valid — these exact tests, run against
/// master before the fix on a 32-core host, accepted 252-439 of their 1,000 invalid schemas each — a
/// fail-open: a malformed schema reaches the LLM tool list.
///
/// Fix: both sites evaluate through <see cref="Draft202012MetaSchemaValidator"/>, which serializes
/// every evaluation of the shared meta-schema behind ONE process-wide gate. One gate — not a lock per
/// call site — because both sites evaluate the SAME instance; two independent locks would still let
/// the two sites race each other (the third test below pins that).
///
/// The invalid schema is chosen so that ONLY the meta-schema rejects it (<c>minLength</c> must be a
/// non-negative integer; <see cref="OpenAiFunctionSchemaValidator"/> does not inspect
/// <c>minLength</c>), so a false-valid evaluation is directly observable at each production site.
///
/// The assertions are deterministic under the fix (zero false accepts is the invariant, not a
/// rate); without the fix they fail with near certainty. Deliberately NOT registered in
/// <c>tests/.reliability-registry.json</c>: a pass-2 retry would let a reintroduced race ship.
///
/// KEEP-path classification (ADR-038 §2 + tests/CLAUDE.md): regression — compiled into
/// Sprk.Bff.Api.Tests via the <c>..\..\integration\regression\**\*.cs</c> glob.
/// </summary>
public class JsonSchemaMetaSchemaConcurrentEvaluationTests
{
    private const int Workers = 8;
    private const int IterationsPerWorker = 250;

    /// <summary>Well-formed JSON, object root, object <c>properties</c>, passes the OpenAI-subset
    /// validator — but <c>minLength</c> is a string, which Draft 2020-12 rejects.</summary>
    private const string InvalidSchema =
        """{"type":"object","properties":{"query":{"type":"string","minLength":"three"}},"required":["query"]}""";

    private const string ValidSchema =
        """{"type":"object","properties":{"query":{"type":"string","minLength":3}},"required":["query"]}""";

    [Fact]
    public void MapJsonSchema_ConcurrentValidAndInvalidSchemas_NeverMapsInvalidSchemaThrough()
    {
        var outcome = RunConcurrently(isInvalid =>
            AnalysisToolService.MapJsonSchema(
                isInvalid ? InvalidSchema : ValidSchema, Guid.NewGuid(), NullLogger.Instance) is not null);

        outcome.InvalidAccepted.Should().Be(0, "an invalid schema must never map through as valid");
        outcome.ValidRejected.Should().Be(0, "a valid schema must always map through");
        outcome.Unexpected.Should().BeEmpty();
    }

    [Fact]
    public void AdapterConstructor_ConcurrentValidAndInvalidSchemas_AlwaysRejectsInvalidSchema()
    {
        var outcome = RunConcurrently(ConstructAdapter);

        outcome.InvalidAccepted.Should().Be(0, "the adapter must never expose an invalid schema to the LLM");
        outcome.ValidRejected.Should().Be(0, "the adapter must always accept a valid schema");
        outcome.Unexpected.Should().BeEmpty();
    }

    [Fact]
    public void MapJsonSchemaAndAdapterConstructor_ConcurrentAcrossBothSites_NeverAcceptInvalidSchema()
    {
        // Workers alternate between the two production sites, so the sites contend with EACH OTHER
        // on the shared meta-schema instance — the case a per-site lock would not cover.
        var outcome = RunConcurrently((isInvalid, worker) => worker % 2 == 0
            ? AnalysisToolService.MapJsonSchema(
                isInvalid ? InvalidSchema : ValidSchema, Guid.NewGuid(), NullLogger.Instance) is not null
            : ConstructAdapter(isInvalid));

        outcome.InvalidAccepted.Should().Be(0);
        outcome.ValidRejected.Should().Be(0);
        outcome.Unexpected.Should().BeEmpty();
    }

    /// <summary>Returns true when the adapter accepted the schema, false when it rejected it with
    /// the meta-schema's <see cref="ArgumentException"/>.</summary>
    private static bool ConstructAdapter(bool isInvalid)
    {
        var tool = new AnalysisTool
        {
            Id = Guid.NewGuid(),
            Name = "ConcurrencyProbeTool",
            Description = "Concurrency regression probe.",
            Type = ToolType.Custom,
            HandlerClass = "ProbeHandler",
            AvailableInContexts = ToolAvailabilityContext.Chat,
            JsonSchema = isInvalid ? InvalidSchema : ValidSchema
        };

        var handler = new Mock<IToolHandler>();
        handler.SetupGet(h => h.SupportedInvocationContexts).Returns(InvocationContextKind.Both);

        try
        {
            _ = new ToolHandlerToAIFunctionAdapter(
                tool, handler.Object, () => new ChatInvocationContext { ChatSessionId = Guid.NewGuid(), TenantId = "t" });
            return true;
        }
        catch (ArgumentException ex) when (ex.Message.Contains("R6-audit-1", StringComparison.Ordinal)
                                           && ex.Message.Contains("not a valid JSON Schema", StringComparison.Ordinal))
        {
            return false;
        }
    }

    private static Outcome RunConcurrently(Func<bool, bool> accepts)
        => RunConcurrently((isInvalid, _) => accepts(isInvalid));

    /// <summary>Runs <see cref="Workers"/> dedicated threads released together by a barrier, each
    /// alternating invalid/valid schemas. <paramref name="accepts"/> returns whether the site
    /// accepted the schema; any other exception is recorded as unexpected.</summary>
    private static Outcome RunConcurrently(Func<bool, int, bool> accepts)
    {
        var invalidAccepted = 0;
        var validRejected = 0;
        var unexpected = new System.Collections.Concurrent.ConcurrentBag<string>();
        using var start = new Barrier(Workers);

        var threads = Enumerable.Range(0, Workers).Select(worker => new Thread(() =>
        {
            start.SignalAndWait();
            for (var i = 0; i < IterationsPerWorker; i++)
            {
                var isInvalid = (i + worker) % 2 == 0;
                try
                {
                    var accepted = accepts(isInvalid, worker);
                    if (isInvalid && accepted) Interlocked.Increment(ref invalidAccepted);
                    if (!isInvalid && !accepted) Interlocked.Increment(ref validRejected);
                }
                catch (Exception ex)
                {
                    unexpected.Add($"{ex.GetType().Name}: {ex.Message}");
                }
            }
        })).ToList();

        threads.ForEach(t => t.Start());
        threads.ForEach(t => t.Join());

        return new Outcome(invalidAccepted, validRejected, unexpected.ToArray());
    }

    private sealed record Outcome(int InvalidAccepted, int ValidRejected, IReadOnlyList<string> Unexpected);
}
