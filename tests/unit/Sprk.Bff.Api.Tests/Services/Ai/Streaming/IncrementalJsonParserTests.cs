using System.Text.Json;
using FluentAssertions;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Services.Ai.Streaming;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai.Streaming;

/// <summary>
/// Unit tests for <see cref="IncrementalJsonParser"/>.
/// Per R5 task 006 acceptance criteria: parser emits FieldStart/Content/Complete events
/// at top-level field boundaries; final accumulated buffer parses cleanly to
/// <see cref="DocumentAnalysisResult"/>; tolerates partial JSON at every intermediate state.
///
/// Canned token sequences mirror the granularity observed in the live spike
/// (<c>notes/task-006-spike-results.md</c>): 1–8 char tokens, declaration-order field arrival,
/// content events with full intra-field text accumulation.
/// </summary>
public class IncrementalJsonParserTests
{
    private static readonly JsonSerializerOptions s_options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    // ─────────────────────────────────────────────────────────────
    // Field-boundary emission (FR-02 + FR-13)
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void Append_EmitsFieldStart_OnFirstTopLevelFieldKey()
    {
        var parser = new IncrementalJsonParser();
        var events = new List<FieldDeltaEvent>();

        foreach (var tok in new[] { "{\"", "tldr", "\":[", "\"hello\"" })
        {
            events.AddRange(parser.Append(tok));
        }

        events.Should().Contain(e => e.Kind == FieldDeltaEventKind.FieldStart && e.Path == "$.tldr");
    }

    [Fact]
    public void Append_EmitsFieldContent_AsTokensArriveWithinActiveField()
    {
        var parser = new IncrementalJsonParser();
        var events = new List<FieldDeltaEvent>();

        // Start tldr then accumulate content
        foreach (var tok in new[] { "{\"", "tldr", "\":[\"", "Jane", " Doe", " is", " hired" })
        {
            events.AddRange(parser.Append(tok));
        }

        // After the FieldStart, subsequent tokens should be FieldContent for $.tldr
        var contents = events.Where(e => e.Kind == FieldDeltaEventKind.FieldContent && e.Path == "$.tldr").ToList();
        contents.Should().NotBeEmpty();
        string.Concat(contents.Select(c => c.Content)).Should().Contain("Jane");
        string.Concat(contents.Select(c => c.Content)).Should().Contain("Doe");
    }

    [Fact]
    public void Append_EmitsFieldComplete_WhenNextTopLevelFieldKeyOpens()
    {
        var parser = new IncrementalJsonParser();
        var events = new List<FieldDeltaEvent>();

        // Full single-string field followed by next field key (covers the "next-key signals prior complete" path)
        var tokens = new[]
        {
            "{\"",
            "summary", "\":\"", "A short summary.", "\",",
            "\"keywords",  // next key opens
            "\":\"a, b\"",
            "}"
        };
        foreach (var t in tokens)
        {
            events.AddRange(parser.Append(t));
        }

        var summaryComplete = events.FirstOrDefault(e =>
            e.Kind == FieldDeltaEventKind.FieldComplete && e.Path == "$.summary");
        summaryComplete.Should().NotBeNull("summary should be marked complete when keywords field opens");

        var keywordsStart = events.FirstOrDefault(e =>
            e.Kind == FieldDeltaEventKind.FieldStart && e.Path == "$.keywords");
        keywordsStart.Should().NotBeNull();
        keywordsStart!.Sequence.Should().BeGreaterThan(summaryComplete!.Sequence);
    }

    [Fact]
    public void Append_EmitsFieldComplete_WhenStructuredValueClosesAtDepth1()
    {
        var parser = new IncrementalJsonParser();
        var events = new List<FieldDeltaEvent>();

        // tldr is an array — should complete when matching ] closes back to depth 1
        var tokens = new[] { "{\"", "tldr", "\":[\"", "one", "\",\"", "two", "\"]", "," };
        foreach (var t in tokens)
        {
            events.AddRange(parser.Append(t));
        }

        events.Should().Contain(e =>
            e.Kind == FieldDeltaEventKind.FieldComplete && e.Path == "$.tldr");
    }

    [Fact]
    public void Append_Sequence_IsMonotonicallyIncreasingAcrossAllEvents()
    {
        var parser = new IncrementalJsonParser();
        var events = new List<FieldDeltaEvent>();

        var tokens = new[]
        {
            "{\"", "tldr", "\":[\"", "a", "\",\"", "b", "\"]", ",",
            "\"summary", "\":\"", "Summary text.", "\",",
            "\"keywords", "\":\"", "a, b, c", "\",",
            "\"entities", "\":{\"", "organizations", "\":[],\"", "persons", "\":[]}",
            "}"
        };
        foreach (var t in tokens)
        {
            events.AddRange(parser.Append(t));
        }

        events.Should().NotBeEmpty();
        for (var i = 1; i < events.Count; i++)
        {
            events[i].Sequence.Should().BeGreaterThan(events[i - 1].Sequence,
                "Sequence must be strictly monotonically increasing");
        }
    }

    // ─────────────────────────────────────────────────────────────
    // Declaration-order arrival (matches spike findings)
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void Append_FieldStart_FiresInJsonDeclarationOrder()
    {
        var parser = new IncrementalJsonParser();
        var events = new List<FieldDeltaEvent>();

        // Schema declares tldr, summary, keywords, entities — Azure streams in this order per spike
        var tokens = new[]
        {
            "{\"tldr", "\":[\"a\"],", "\"summary", "\":\"S\",",
            "\"keywords", "\":\"K\",", "\"entities", "\":{}}"
        };
        foreach (var t in tokens)
        {
            events.AddRange(parser.Append(t));
        }

        var startOrder = events
            .Where(e => e.Kind == FieldDeltaEventKind.FieldStart)
            .Select(e => e.Path)
            .ToList();

        startOrder.Should().Equal("$.tldr", "$.summary", "$.keywords", "$.entities");
    }

    // ─────────────────────────────────────────────────────────────
    // Partial-JSON tolerance (intermediate buffer is NEVER valid JSON)
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void Append_ToleratesPartialJsonInIntermediateStates()
    {
        var parser = new IncrementalJsonParser();
        // Feed the json one char at a time
        var json = "{\"tldr\":[\"hello\",\"world\"],\"summary\":\"S\",\"keywords\":\"K\",\"entities\":{\"organizations\":[],\"persons\":[]}}";
        var allEvents = new List<FieldDeltaEvent>();
        foreach (var c in json)
        {
            // Should not throw at any intermediate char (partial JSON is invalid mid-stream)
            allEvents.AddRange(parser.Append(c.ToString()));
        }
        allEvents.Should().NotBeEmpty();
        // Final buffer must parse
        parser.TryParseFinal(s_options).Should().NotBeNull();
    }

    [Fact]
    public void Append_TolerantsEscapedQuotesInsideStringValues()
    {
        var parser = new IncrementalJsonParser();
        var events = new List<FieldDeltaEvent>();
        // String contains an escaped quote — must NOT be mistaken for value-close
        var tokens = new[] { "{\"", "summary", "\":\"", @"He said \", @"""hello\""", " world", "\",", "\"keywords", "\":\"x\"}" };
        foreach (var t in tokens) events.AddRange(parser.Append(t));

        // summary must complete only when the next key opens, NOT mid-string
        var summaryStart = events.FirstOrDefault(e => e.Kind == FieldDeltaEventKind.FieldStart && e.Path == "$.summary");
        var summaryComplete = events.FirstOrDefault(e => e.Kind == FieldDeltaEventKind.FieldComplete && e.Path == "$.summary");
        var keywordsStart = events.FirstOrDefault(e => e.Kind == FieldDeltaEventKind.FieldStart && e.Path == "$.keywords");

        summaryStart.Should().NotBeNull();
        summaryComplete.Should().NotBeNull();
        keywordsStart.Should().NotBeNull();
        summaryComplete!.Sequence.Should().BeLessThan(keywordsStart!.Sequence);
    }

    // ─────────────────────────────────────────────────────────────
    // Final result reconstruction (accumulated buffer → DocumentAnalysisResult)
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void TryParseFinal_ReturnsDocumentAnalysisResult_OnValidAccumulatedJson()
    {
        var parser = new IncrementalJsonParser();
        // Stream a full valid Summarize response
        var json = """
        {
          "summary": "Jane Doe employment contract summary.",
          "tldr": ["Senior Engineer hire", "Salary $180K", "12-month non-compete"],
          "keywords": "employment, contract, non-compete",
          "entities": { "organizations": ["ACME Corp"], "persons": ["Jane Doe"] }
        }
        """;
        // Feed in arbitrary chunks
        for (var i = 0; i < json.Length; i += 5)
        {
            var chunk = json.Substring(i, Math.Min(5, json.Length - i));
            parser.Append(chunk);
        }
        var result = parser.TryParseFinal(s_options);
        result.Should().NotBeNull();
        result!.ParsedSuccessfully.Should().BeTrue();
        result.Summary.Should().Contain("Jane Doe");
        result.TlDr.Should().HaveCount(3);
        result.Keywords.Should().Contain("contract");
        result.Entities.Organizations.Should().Contain("ACME Corp");
        result.RawResponse.Should().NotBeNull();
    }

    [Fact]
    public void TryParseFinal_ReturnsNull_OnMalformedAccumulatedJson()
    {
        var parser = new IncrementalJsonParser();
        parser.Append("{\"summary\": \"broken"); // never closes
        var result = parser.TryParseFinal(s_options);
        result.Should().BeNull("Malformed JSON should not deserialize; caller falls back to DocumentAnalysisResult.Fallback");
    }

    [Fact]
    public void GetAccumulatedJson_ReturnsFullBuffer()
    {
        var parser = new IncrementalJsonParser();
        parser.Append("{\"a\":");
        parser.Append("1}");
        parser.GetAccumulatedJson().Should().Be("{\"a\":1}");
    }

    // ─────────────────────────────────────────────────────────────
    // Idempotency / empty-token handling
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void Append_EmptyToken_ReturnsNoEvents()
    {
        var parser = new IncrementalJsonParser();
        parser.Append("").Should().BeEmpty();
        parser.GetAccumulatedJson().Should().BeEmpty();
    }

    [Fact]
    public void Append_NullSafeWithEmptyStream()
    {
        var parser = new IncrementalJsonParser();
        parser.TryParseFinal(s_options).Should().BeNull();
        parser.GetAccumulatedJson().Should().BeEmpty();
    }
}
