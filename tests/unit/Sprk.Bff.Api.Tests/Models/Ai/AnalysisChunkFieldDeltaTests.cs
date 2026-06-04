using System.Text.Json;
using FluentAssertions;
using Sprk.Bff.Api.Models.Ai;
using Xunit;

namespace Sprk.Bff.Api.Tests.Models.Ai;

/// <summary>
/// Tests for the R5 additive <see cref="FieldDelta"/> variant on <see cref="AnalysisChunk"/>.
/// <para>
/// Covers (per task 005 D1-05):
/// (a) <see cref="AnalysisChunk.FromDelta"/> factory shape;
/// (b) JSON round-trip preserves delta payload;
/// (c) byte-identity regression for non-delta variants (<c>FromContent</c>, <c>Completed</c>, <c>FromError</c>) —
///     no <c>"delta"</c> property emitted when <see cref="AnalysisChunk.Delta"/> is null;
/// (d) wizard-consumer parity (mimics <c>summarizeService.ts</c> <c>streamSummarize()</c> discriminant filter
///     at lines ~80–100 — confirms unknown <c>"delta"</c> events are silently ignored).
/// </para>
/// <para>
/// R5 binding contract (CLAUDE.md §3.1 "Specifically prohibited"): R5 MUST EXTEND <see cref="AnalysisChunk"/>;
/// MUST NOT introduce a parallel SSE envelope. Spec NFR-10 requires existing wizard consumers to keep
/// working unchanged. These tests lock in both contracts.
/// </para>
/// </summary>
public class AnalysisChunkFieldDeltaTests
{
    // Mirror the BuilderSseEventsTests serialization convention.
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    #region (a) FromDelta factory shape

    [Fact]
    public void FromDelta_SetsCorrectTypeAndDelta()
    {
        var chunk = AnalysisChunk.FromDelta("tldr", "Hello", 0);

        chunk.Type.Should().Be("delta");
        chunk.Content.Should().Be(string.Empty);
        chunk.Done.Should().BeFalse();
        chunk.Summary.Should().BeNull();
        chunk.Result.Should().BeNull();
        chunk.Error.Should().BeNull();

        chunk.Delta.Should().NotBeNull();
        chunk.Delta!.Path.Should().Be("tldr");
        chunk.Delta.Content.Should().Be("Hello");
        chunk.Delta.Sequence.Should().Be(0);
    }

    [Fact]
    public void FromDelta_SupportsNestedJsonPathSyntax()
    {
        // Path is a producer/consumer contract — model accepts any string.
        var chunk = AnalysisChunk.FromDelta("fileHighlights[0].summary", " token", 42);

        chunk.Delta.Should().NotBeNull();
        chunk.Delta!.Path.Should().Be("fileHighlights[0].summary");
        chunk.Delta.Content.Should().Be(" token");
        chunk.Delta.Sequence.Should().Be(42);
    }

    #endregion

    #region (b) JSON round-trip preserves delta payload

    [Fact]
    public void FromDelta_SerializesAndDeserializes_RoundTrip()
    {
        var original = AnalysisChunk.FromDelta("fileHighlights[0].summary", "partial token", 7);

        var json1 = JsonSerializer.Serialize(original, JsonOptions);
        var deserialized = JsonSerializer.Deserialize<AnalysisChunk>(json1, JsonOptions);
        var json2 = JsonSerializer.Serialize(deserialized, JsonOptions);

        deserialized.Should().NotBeNull();
        deserialized!.Type.Should().Be("delta");
        deserialized.Delta.Should().NotBeNull();
        deserialized.Delta!.Path.Should().Be("fileHighlights[0].summary");
        deserialized.Delta.Content.Should().Be("partial token");
        deserialized.Delta.Sequence.Should().Be(7);

        // Byte-identity through one full round trip.
        json2.Should().Be(json1);
    }

    [Fact]
    public void FromDelta_JsonContainsDeltaPropertyWithCamelCaseFields()
    {
        var chunk = AnalysisChunk.FromDelta("tldr", "Hello", 0);

        var json = JsonSerializer.Serialize(chunk, JsonOptions);

        json.Should().Contain("\"type\":\"delta\"");
        json.Should().Contain("\"delta\":");
        json.Should().Contain("\"path\":\"tldr\"");
        json.Should().Contain("\"content\":\"Hello\"");
        json.Should().Contain("\"sequence\":0");
    }

    #endregion

    #region (c) Byte-identity for non-delta variants — NFR-10 back-compat

    [Fact]
    public void FromContent_SerializesWithoutDeltaProperty()
    {
        // Locks existing v1.0 payload shape — no "delta" property when Delta is null.
        var chunk = AnalysisChunk.FromContent("partial text");

        var json = JsonSerializer.Serialize(chunk, JsonOptions);

        json.Should().NotContain("\"delta\"");
        json.Should().Contain("\"type\":\"text\"");
        json.Should().Contain("\"content\":\"partial text\"");
    }

    [Fact]
    public void Completed_String_SerializesWithoutDeltaProperty()
    {
        var chunk = AnalysisChunk.Completed("Final summary text.");

        var json = JsonSerializer.Serialize(chunk, JsonOptions);

        json.Should().NotContain("\"delta\"");
        json.Should().Contain("\"type\":\"complete\"");
        json.Should().Contain("\"done\":true");
        json.Should().Contain("\"summary\":\"Final summary text.\"");
    }

    [Fact]
    public void Completed_Result_SerializesWithoutDeltaProperty()
    {
        var result = new DocumentAnalysisResult
        {
            Summary = "Summary"
        };

        var chunk = AnalysisChunk.Completed(result);

        var json = JsonSerializer.Serialize(chunk, JsonOptions);

        json.Should().NotContain("\"delta\"");
        json.Should().Contain("\"type\":\"complete\"");
    }

    [Fact]
    public void FromError_SerializesWithoutDeltaProperty()
    {
        var chunk = AnalysisChunk.FromError("Something failed.");

        var json = JsonSerializer.Serialize(chunk, JsonOptions);

        json.Should().NotContain("\"delta\"");
        json.Should().Contain("\"type\":\"error\"");
        json.Should().Contain("\"error\":\"Something failed.\"");
    }

    #endregion

    #region (d) Wizard-consumer parity — mimics streamSummarize() discriminant filter

    /// <summary>
    /// Mirrors the discriminant chain in
    /// <c>src/solutions/LegalWorkspace/src/components/SummarizeFiles/summarizeService.ts</c>
    /// (lines ~80–100):
    /// <code>
    /// if (chunk.type === 'progress' &amp;&amp; chunk.step) { onProgress(chunk.step); }
    /// else if (chunk.type === 'result' &amp;&amp; chunk.content) { rawResult = JSON.parse(chunk.content); }
    /// else if (chunk.type === 'error') { throw new Error(chunk.error ?? chunk.content); }
    /// else if (chunk.done) { break; }
    /// // implicit no-op for any unknown discriminant
    /// </code>
    /// This test confirms an unknown <c>"delta"</c> discriminant falls through every branch
    /// and the loop continues without exception — the NFR-10 back-compat contract.
    /// </summary>
    [Fact]
    public void WizardConsumer_IgnoresDeltaDiscriminant()
    {
        // A representative stream: two delta events sandwiched around a final result.
        var stream = new[]
        {
            AnalysisChunk.FromDelta("tldr", "Hello ", 0),
            AnalysisChunk.FromDelta("tldr", "world", 1),
            // Final structured result (mirrors how the BFF emits a "result" event with a
            // JSON string in content — the wizard parses it back into ISummarizeResult).
            new AnalysisChunk(
                Type: "result",
                Content: "{\"tldr\":\"Hello world\"}",
                Done: false),
            new AnalysisChunk(Type: "progress", Content: string.Empty, Done: true)
        };

        var progressSteps = new List<string>();
        string? rawResult = null;
        var loopExitedNormally = false;
        var didThrow = false;

        try
        {
            foreach (var raw in stream)
            {
                // Mimic the JS round-trip: serialize then re-parse to a discriminated shape.
                var json = JsonSerializer.Serialize(raw, JsonOptions);
                var chunk = JsonSerializer.Deserialize<WizardChunkShape>(json, JsonOptions);
                chunk.Should().NotBeNull();

                // The exact discriminant chain from summarizeService.ts lines ~87–98:
                if (chunk!.Type == "progress" && !string.IsNullOrEmpty(chunk.Step))
                {
                    progressSteps.Add(chunk.Step!);
                }
                else if (chunk.Type == "result" && !string.IsNullOrEmpty(chunk.Content))
                {
                    rawResult = chunk.Content;
                }
                else if (chunk.Type == "error")
                {
                    throw new InvalidOperationException(chunk.Error ?? chunk.Content ?? "Summarization failed");
                }
                else if (chunk.Done)
                {
                    loopExitedNormally = true;
                    break;
                }
                // implicit no-op for unknown discriminants like "delta"
            }
        }
        catch
        {
            didThrow = true;
        }

        didThrow.Should().BeFalse("delta events must NEVER throw in a v1.0 wizard consumer");
        loopExitedNormally.Should().BeTrue("the final done=true chunk must terminate the loop");
        rawResult.Should().Be("{\"tldr\":\"Hello world\"}", "the result event must still surface");
        progressSteps.Should().BeEmpty("delta events must NOT be consumed as progress events");
    }

    /// <summary>
    /// Minimal projection of the wizard's anonymous <c>chunk</c> shape from
    /// <c>summarizeService.ts</c> line 80:
    /// <c>{ type?: string; step?: string; content?: string; error?: string; done?: boolean }</c>.
    /// Used only inside <see cref="WizardConsumer_IgnoresDeltaDiscriminant"/> to round-trip
    /// JSON through a structurally-equivalent C# shape.
    /// </summary>
    private sealed record WizardChunkShape(
        string? Type = null,
        string? Step = null,
        string? Content = null,
        string? Error = null,
        bool Done = false);

    #endregion
}
