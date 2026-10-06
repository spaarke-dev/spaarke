using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Chat;
using Xunit;

namespace Sprk.Bff.Api.Tests.Integration.Regression.Ai;

/// <summary>
/// Regression — issue #1295: Json.Schema.Net 7.3.4 <c>JsonSchema.Evaluate</c> is NOT thread-safe on a
/// shared schema instance, and both production tool-schema validation sites evaluated the process-wide
/// static <c>MetaSchemas.Draft202012</c> without synchronization:
/// <see cref="AnalysisToolService.MapJsonSchema"/> (Dataverse <c>sprk_jsonschema</c> mapping) and the
/// <see cref="ToolHandlerToAIFunctionAdapter"/> constructor (chat tool exposure). Both run on every chat
/// turn. Under concurrency a semantically INVALID tool schema evaluated as valid — a fail-open: a
/// malformed schema reaches the LLM tool list. Measured on a 32-core host: the first version of these
/// concurrency tests, run against unfixed master, accepted 252-439 of 1,000 invalid schemas each; this
/// version, with only the gate's lock removed, accepted 15-60 of its 100 invalid schemas each.
///
/// Fix: both sites evaluate through <see cref="Draft202012MetaSchemaValidator"/>, which serializes
/// every evaluation of the shared meta-schema behind ONE process-wide gate (one gate, not a lock per
/// call site, because both sites evaluate the SAME instance — the cross-site test pins that) and
/// caches the verdict per exact schema text so a repeated schema never re-enters the gate.
///
/// <para><b>Every concurrent iteration uses a UNIQUE schema text</b> (a per-iteration
/// <c>description</c>), so every call is a cache miss and genuinely contends on the gate. With
/// repeated texts the verdict cache would answer almost every call, and the tests would stop
/// measuring the race. The three concurrency tests add 600 distinct texts to the process-wide cache,
/// well inside its 1,024-entry bound, so they cannot fill it for the cache tests below.</para>
///
/// The invalid schema is chosen so that ONLY the meta-schema rejects it (<c>minLength</c> must be a
/// non-negative integer; <see cref="OpenAiFunctionSchemaValidator"/> does not inspect
/// <c>minLength</c>), so a false-valid evaluation is directly observable at each production site.
///
/// The concurrency assertions are deterministic under the fix (zero false accepts is the invariant,
/// not a rate). Deliberately NOT registered in <c>tests/.reliability-registry.json</c>: a pass-2 retry
/// would let a reintroduced race ship.
///
/// KEEP-path classification (ADR-038 §2 + tests/CLAUDE.md): regression — compiled into
/// Sprk.Bff.Api.Tests via the <c>..\..\integration\regression\**\*.cs</c> glob.
/// </summary>
public class Issue1295_JsonSchemaMetaSchemaConcurrentEvaluationTests
{
    private const int Workers = 8;
    private const int IterationsPerWorker = 25;

    // ─── Concurrency: no false "valid" under contention on the gate ──────────────────────────

    [Fact]
    public void MapJsonSchema_ConcurrentValidAndInvalidSchemas_NeverMapsInvalidSchemaThrough()
    {
        var outcome = RunConcurrently((isInvalid, text, _) =>
            AnalysisToolService.MapJsonSchema(text, Guid.NewGuid(), NullLogger.Instance) is not null);

        outcome.InvalidAccepted.Should().Be(0, "an invalid schema must never map through as valid");
        outcome.ValidRejected.Should().Be(0, "a valid schema must always map through");
        outcome.Unexpected.Should().BeEmpty();
    }

    [Fact]
    public void AdapterConstructor_ConcurrentValidAndInvalidSchemas_AlwaysRejectsInvalidSchema()
    {
        var outcome = RunConcurrently((_, text, _) => ConstructAdapter(text));

        outcome.InvalidAccepted.Should().Be(0, "the adapter must never expose an invalid schema to the LLM");
        outcome.ValidRejected.Should().Be(0, "the adapter must always accept a valid schema");
        outcome.Unexpected.Should().BeEmpty();
    }

    [Fact]
    public void MapJsonSchemaAndAdapterConstructor_ConcurrentAcrossBothSites_NeverAcceptInvalidSchema()
    {
        // Workers alternate between the two production sites, so the sites contend with EACH OTHER
        // on the shared meta-schema instance — the case a per-site lock would not cover.
        var outcome = RunConcurrently((_, text, worker) => worker % 2 == 0
            ? AnalysisToolService.MapJsonSchema(text, Guid.NewGuid(), NullLogger.Instance) is not null
            : ConstructAdapter(text));

        outcome.InvalidAccepted.Should().Be(0);
        outcome.ValidRejected.Should().Be(0);
        outcome.Unexpected.Should().BeEmpty();
    }

    // ─── Verdict cache: a repeated text is answered from the cache, with the same verdict ───
    // Observed through the returned instance: a fresh evaluation always allocates a new
    // MetaSchemaEvaluation, so the SAME instance on the second call means it was not re-evaluated.
    // No production seam was added for this — instance identity is the documented contract of
    // Draft202012MetaSchemaValidator.Evaluate ("a repeated text returns the same immutable instance").

    [Fact]
    public void Evaluate_RepeatedValidSchemaText_ReturnsTheCachedVerdictWithoutReEvaluating()
    {
        var text = SchemaText(isInvalid: false, unique: Guid.NewGuid().ToString("N"));

        var first = Draft202012MetaSchemaValidator.Evaluate(text);
        var second = Draft202012MetaSchemaValidator.Evaluate(new string(text.AsSpan()));

        first.IsValid.Should().BeTrue();
        first.Errors.Should().BeEmpty();
        second.Should().BeSameAs(first, "the second call for identical text must be served from the cache");
    }

    [Fact]
    public void Evaluate_RepeatedInvalidSchemaText_ReturnsTheCachedVerdictWithItsErrors()
    {
        var text = SchemaText(isInvalid: true, unique: Guid.NewGuid().ToString("N"));

        var first = Draft202012MetaSchemaValidator.Evaluate(text);
        var second = Draft202012MetaSchemaValidator.Evaluate(new string(text.AsSpan()));

        first.IsValid.Should().BeFalse("a negative verdict is cached exactly like a positive one");
        first.Errors.Should().Contain(e => e.StartsWith("/properties/query/minLength", StringComparison.Ordinal));
        second.Should().BeSameAs(first, "the second call for identical text must be served from the cache");
    }

    [Fact]
    public void Evaluate_TextsDifferingInOneValue_EachGetTheirOwnVerdict()
    {
        // The cache is keyed by the full text, not a digest of it: two texts that differ only in the
        // one value that decides validity must never share a verdict.
        var unique = Guid.NewGuid().ToString("N");
        var valid = SchemaText(isInvalid: false, unique);
        var invalid = SchemaText(isInvalid: true, unique);

        Draft202012MetaSchemaValidator.Evaluate(valid).IsValid.Should().BeTrue();
        Draft202012MetaSchemaValidator.Evaluate(invalid).IsValid.Should().BeFalse();
        Draft202012MetaSchemaValidator.Evaluate(valid).IsValid.Should().BeTrue();
    }

    // ─── Machinery ───────────────────────────────────────────────────────────────────────────

    /// <summary>Well-formed JSON, object root, object <c>properties</c>, passes the OpenAI-subset
    /// validator. The invalid form's <c>minLength</c> is a string, which Draft 2020-12 rejects; the
    /// valid form's is the integer 3. <paramref name="unique"/> lands in a description so the text is
    /// distinct per call without affecting validity.</summary>
    private static string SchemaText(bool isInvalid, string unique)
        => "{\"type\":\"object\",\"properties\":{\"query\":{\"type\":\"string\",\"description\":\"probe "
           + unique + "\",\"minLength\":" + (isInvalid ? "\"three\"" : "3") + "}},\"required\":[\"query\"]}";

    /// <summary>Returns true when the adapter accepted the schema, false when it rejected it with
    /// the meta-schema's <see cref="ArgumentException"/>.</summary>
    private static bool ConstructAdapter(string schemaText)
    {
        var tool = new AnalysisTool
        {
            Id = Guid.NewGuid(),
            Name = "ConcurrencyProbeTool",
            Description = "Concurrency regression probe.",
            Type = ToolType.Custom,
            HandlerClass = "ProbeHandler",
            AvailableInContexts = ToolAvailabilityContext.Chat,
            JsonSchema = schemaText
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

    /// <summary>Runs <see cref="Workers"/> dedicated threads released together by a barrier, each
    /// alternating invalid/valid schemas, every one with a unique text.
    /// <paramref name="accepts"/>(isInvalid, schemaText, worker) returns whether the site accepted the
    /// schema; any other exception is recorded as unexpected.</summary>
    private static Outcome RunConcurrently(Func<bool, string, int, bool> accepts)
    {
        var run = Guid.NewGuid().ToString("N");
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
                var text = SchemaText(isInvalid, $"{run}-{worker}-{i}");
                try
                {
                    var accepted = accepts(isInvalid, text, worker);
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
