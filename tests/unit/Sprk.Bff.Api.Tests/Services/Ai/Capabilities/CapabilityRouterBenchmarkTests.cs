using System.Diagnostics;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Sprk.Bff.Api.Services.Ai.Capabilities;
using Xunit;
using Xunit.Abstractions;

namespace Sprk.Bff.Api.Tests.Services.Ai.Capabilities;

/// <summary>
/// Benchmark tests for <see cref="CapabilityRouter"/> Layer 1 keyword classifier.
///
/// Validates the router against a 105-message corpus defined in
/// <c>notes/routing-benchmark-corpus.json</c>. All tests run offline (no LLM needed)
/// because they only exercise the synchronous Layer 1 keyword matching path.
///
/// Key invariants verified:
///   - Layer 1 hit rate >= 60% of messages with expectedLayer=1
///   - Layer 1 never confidently routes to the wrong capability
///   - Messages with expectedLayer=2 or 3 should NOT be confidently routed by Layer 1
///   - Layer 1 completes in under 50ms per message (NFR-03)
///   - Single-LLM-call invariant: Layer 1 makes zero LLM calls
/// </summary>
public sealed class CapabilityRouterBenchmarkTests
{
    private readonly ITestOutputHelper _output;
    private readonly CapabilityRouter _router;
    private readonly List<CorpusEntry> _corpus;

    // ── Test manifest matching the corpus definition ──────────────────────────

    private static readonly CapabilityManifestEntry[] ManifestEntries =
    [
        MakeEntry("legal_research",
            ["case law", "legal precedent", "court decision", "statute", "regulation", "jurisdiction"],
            "Search legal databases for case law and precedents"),
        MakeEntry("document_search",
            ["find document", "search files", "locate file", "document lookup"],
            "Search and retrieve documents from SharePoint Embedded"),
        MakeEntry("document_analysis",
            ["analyze document", "document review", "extract from document", "parse document"],
            "Analyze document content using AI extraction"),
        MakeEntry("invoice_processing",
            ["invoice", "payment", "billing", "expense", "receipt", "vendor payment"],
            "Process and classify financial invoices"),
        MakeEntry("email_processing",
            ["email", "message", "inbox", "outbound email", "correspondence"],
            "Process incoming and outgoing email communications"),
        MakeEntry("knowledge_retrieval",
            ["knowledge base", "knowledge source", "FAQ", "help article"],
            "Retrieve answers from configured knowledge sources"),
        MakeEntry("summarize_content",
            ["summarize", "summary", "brief", "overview", "recap", "digest"],
            "Generate concise summaries of documents or text"),
        MakeEntry("semantic_search",
            ["semantic search", "meaning search", "concept search", "similar documents"],
            "AI-powered semantic similarity search across documents"),
        MakeEntry("entity_lookup",
            ["lookup entity", "find record", "search entities", "query records", "CRM lookup"],
            "Query Dataverse entities by name or criteria"),
        MakeEntry("write_back",
            ["update record", "save changes", "write back", "modify entity", "edit record"],
            "Write data back to Dataverse records"),
    ];

    public CapabilityRouterBenchmarkTests(ITestOutputHelper output)
    {
        _output = output;

        var manifest = new CapabilityManifest(NullLogger<CapabilityManifest>.Instance);
        manifest.Refresh(ManifestEntries.ToList());

        var options = Options.Create(new CapabilityRouterOptions
        {
            ConfidenceThreshold = 0.8,
            PlaybookBiasThreshold = 0.65,
        });

        _router = new CapabilityRouter(manifest, options, NullLogger<CapabilityRouter>.Instance);

        _corpus = LoadCorpus();
    }

    // ── Benchmark: Layer 1 hit rate on expectedLayer=1 messages ──────────────

    /// <summary>
    /// Validates that Layer 1 keyword matching confidently routes at least 60%
    /// of messages that are designed to be routable by keywords.
    /// </summary>
    [Fact]
    public void Layer1_HitRate_MeetsTargetForKeywordMessages()
    {
        var layer1Messages = _corpus.Where(c => c.ExpectedLayer == 1).ToList();
        layer1Messages.Should().HaveCountGreaterOrEqualTo(60,
            "corpus must have >= 60 messages with expectedLayer=1");

        var confidentCount = 0;
        var misroutedCount = 0;

        foreach (var entry in layer1Messages)
        {
            var result = _router.RouteSync(entry.Message, activePlaybookName: null);

            if (result.IsConfident)
            {
                confidentCount++;

                if (entry.ExpectedCapability is not null &&
                    !result.SelectedCapabilities.Contains(entry.ExpectedCapability))
                {
                    misroutedCount++;
                    _output.WriteLine(
                        $"MISROUTE id={entry.Id}: expected={entry.ExpectedCapability}, " +
                        $"got=[{string.Join(",", result.SelectedCapabilities)}], " +
                        $"confidence={result.Confidence:F4}, message=\"{entry.Message}\"");
                }
            }
            else
            {
                _output.WriteLine(
                    $"MISS id={entry.Id}: expected Layer 1 confident for '{entry.ExpectedCapability}', " +
                    $"got uncertain (confidence={result.Confidence:F4}), " +
                    $"category={entry.Category}, message=\"{entry.Message}\"");
            }
        }

        var hitRate = (double)confidentCount / layer1Messages.Count;
        _output.WriteLine($"\nLayer 1 hit rate: {confidentCount}/{layer1Messages.Count} ({hitRate:P1})");
        _output.WriteLine($"Misrouted: {misroutedCount}");

        hitRate.Should().BeGreaterOrEqualTo(0.60,
            $"Layer 1 must route >= 60% of keyword-targetable messages confidently; actual: {hitRate:P1}");

        misroutedCount.Should().Be(0,
            "Layer 1 must never confidently route a message to the wrong capability");
    }

    // ── Benchmark: Layer 2/3 messages should NOT be confident at Layer 1 ─────

    /// <summary>
    /// Messages designed for Layer 2 (ambiguous/paraphrased) or Layer 3 (off-topic)
    /// should not produce a confident Layer 1 result. A confident result here would
    /// mean false-positive routing.
    /// </summary>
    [Fact]
    public void Layer1_DoesNotFalsePositive_OnNonKeywordMessages()
    {
        var nonLayer1Messages = _corpus.Where(c => c.ExpectedLayer >= 2).ToList();
        var falsePositiveCount = 0;

        foreach (var entry in nonLayer1Messages)
        {
            var result = _router.RouteSync(entry.Message, activePlaybookName: null);

            if (result.IsConfident)
            {
                // Some Layer 2 messages might incidentally match keywords -- that is
                // acceptable if the capability matches the expected one. True false
                // positives are when Layer 1 confidently selects the WRONG capability,
                // or confidently selects ANY capability for a Layer 3 (off-topic) message.
                if (entry.ExpectedLayer == 3)
                {
                    falsePositiveCount++;
                    _output.WriteLine(
                        $"FALSE_POSITIVE id={entry.Id}: Layer 3 message got Layer 1 confident " +
                        $"routing to [{string.Join(",", result.SelectedCapabilities)}], " +
                        $"confidence={result.Confidence:F4}, message=\"{entry.Message}\"");
                }
                else if (entry.ExpectedCapability is not null &&
                         !result.SelectedCapabilities.Contains(entry.ExpectedCapability))
                {
                    falsePositiveCount++;
                    _output.WriteLine(
                        $"FALSE_POSITIVE id={entry.Id}: expected={entry.ExpectedCapability ?? "(any)"}, " +
                        $"got=[{string.Join(",", result.SelectedCapabilities)}], " +
                        $"confidence={result.Confidence:F4}, message=\"{entry.Message}\"");
                }
                else
                {
                    // Incidental keyword match that happens to be correct -- log but not a failure.
                    _output.WriteLine(
                        $"BONUS_HIT id={entry.Id}: Layer 2 message routed correctly at Layer 1 " +
                        $"to [{string.Join(",", result.SelectedCapabilities)}], " +
                        $"confidence={result.Confidence:F4}, category={entry.Category}");
                }
            }
        }

        falsePositiveCount.Should().Be(0,
            "Layer 1 must not produce false-positive confident results for off-topic or ambiguous messages");
    }

    // ── Benchmark: Layer 1 latency under 50ms ────────────────────────────────

    /// <summary>
    /// Every single corpus message must complete Layer 1 routing in under 50ms.
    /// Additionally reports P50/P95/P99 percentiles.
    /// </summary>
    [Fact]
    public void Layer1_Latency_Under50ms_ForAllCorpusMessages()
    {
        // Warm-up pass to eliminate JIT.
        foreach (var entry in _corpus)
        {
            _router.RouteSync(entry.Message, activePlaybookName: null);
        }

        var latencies = new List<double>(_corpus.Count);

        foreach (var entry in _corpus)
        {
            var sw = Stopwatch.StartNew();
            _router.RouteSync(entry.Message, activePlaybookName: null);
            sw.Stop();

            var ms = sw.Elapsed.TotalMilliseconds;
            latencies.Add(ms);

            ms.Should().BeLessThan(50,
                $"Message id={entry.Id} took {ms:F3}ms (limit: 50ms)");
        }

        latencies.Sort();
        var p50 = latencies[(int)(latencies.Count * 0.50)];
        var p95 = latencies[(int)(latencies.Count * 0.95)];
        var p99 = latencies[(int)(latencies.Count * 0.99)];
        var max = latencies.Last();

        _output.WriteLine($"Layer 1 latency across {latencies.Count} messages:");
        _output.WriteLine($"  P50: {p50:F3}ms");
        _output.WriteLine($"  P95: {p95:F3}ms");
        _output.WriteLine($"  P99: {p99:F3}ms");
        _output.WriteLine($"  Max: {max:F3}ms");
    }

    // ── Benchmark: full corpus summary ───────────────────────────────────────

    /// <summary>
    /// Runs the full corpus and emits a distribution summary compatible with the
    /// results template in the benchmark report.
    /// </summary>
    [Fact]
    public void Layer1_FullCorpus_DistributionSummary()
    {
        var confidentCorrect = 0;
        var confidentWrong = 0;
        var uncertain = 0;

        var confidenceRanges = new int[5]; // [0.95-1], [0.8-0.95], [0.65-0.8], [0.4-0.65], [0-0.4]
        var categoryStats = new Dictionary<string, (int Total, int Confident, int CorrectCapability)>();

        foreach (var entry in _corpus)
        {
            var result = _router.RouteSync(entry.Message, activePlaybookName: null);

            // Track confidence distribution.
            var ci = result.Confidence switch
            {
                >= 0.95 => 0,
                >= 0.80 => 1,
                >= 0.65 => 2,
                >= 0.40 => 3,
                _ => 4
            };
            confidenceRanges[ci]++;

            // Track category stats.
            if (!categoryStats.TryGetValue(entry.Category, out var stat))
            {
                stat = (0, 0, 0);
            }

            var isCorrectCapability = entry.ExpectedCapability is not null &&
                                      result.SelectedCapabilities.Contains(entry.ExpectedCapability);

            categoryStats[entry.Category] = (
                stat.Total + 1,
                stat.Confident + (result.IsConfident ? 1 : 0),
                stat.CorrectCapability + (isCorrectCapability ? 1 : 0));

            if (result.IsConfident)
            {
                if (entry.ExpectedCapability is null || result.SelectedCapabilities.Contains(entry.ExpectedCapability))
                    confidentCorrect++;
                else
                    confidentWrong++;
            }
            else
            {
                uncertain++;
            }
        }

        _output.WriteLine("=== LAYER 1 DISTRIBUTION SUMMARY ===");
        _output.WriteLine($"Total messages:     {_corpus.Count}");
        _output.WriteLine($"Confident correct:  {confidentCorrect}");
        _output.WriteLine($"Confident wrong:    {confidentWrong}");
        _output.WriteLine($"Uncertain:          {uncertain}");
        _output.WriteLine($"Layer 1 hit rate:   {(double)confidentCorrect / _corpus.Count:P1}");
        _output.WriteLine(" ");

        _output.WriteLine("=== CONFIDENCE DISTRIBUTION ===");
        _output.WriteLine($"  0.95 - 1.00:  {confidenceRanges[0]}");
        _output.WriteLine($"  0.80 - 0.95:  {confidenceRanges[1]}");
        _output.WriteLine($"  0.65 - 0.80:  {confidenceRanges[2]}");
        _output.WriteLine($"  0.40 - 0.65:  {confidenceRanges[3]}");
        _output.WriteLine($"  0.00 - 0.40:  {confidenceRanges[4]}");
        _output.WriteLine(" ");

        _output.WriteLine("=== PER-CATEGORY RESULTS ===");
        _output.WriteLine($"{"Category",-30} {"Total",5} {"Confident",9} {"Correct",7} {"Accuracy",8}");
        foreach (var (cat, stat) in categoryStats.OrderBy(kv => kv.Key))
        {
            var accuracy = stat.Total > 0 ? (double)stat.CorrectCapability / stat.Total : 0;
            _output.WriteLine($"{cat,-30} {stat.Total,5} {stat.Confident,9} {stat.CorrectCapability,7} {accuracy,8:P0}");
        }

        confidentWrong.Should().Be(0,
            "Layer 1 must never confidently route to the wrong capability");
    }

    // ── Benchmark: 50-capability manifest performance ────────────────────────

    /// <summary>
    /// Validates the NFR that Layer 1 completes in under 50ms even with a
    /// 50-capability manifest (stress test).
    /// </summary>
    [Fact]
    public void Layer1_Latency_Under50ms_With50CapabilityManifest()
    {
        // Build a 50-capability manifest.
        var entries = Enumerable.Range(1, 50)
            .Select(i => MakeEntry(
                $"capability_{i:D2}",
                [$"keyword{i}a", $"keyword{i}b", $"keyword{i}c", $"keyword{i}d", $"keyword{i}e"],
                $"Description for capability {i}"))
            .ToList();

        var manifest = new CapabilityManifest(NullLogger<CapabilityManifest>.Instance);
        manifest.Refresh(entries);

        var options = Options.Create(new CapabilityRouterOptions());
        var router = new CapabilityRouter(manifest, options, NullLogger<CapabilityRouter>.Instance);

        // Warm-up.
        router.RouteSync("warm up the classifier", activePlaybookName: null);

        // Run a representative subset of the corpus.
        var sampleMessages = _corpus.Take(20).Select(c => c.Message).ToList();
        var latencies = new List<double>(sampleMessages.Count);

        foreach (var msg in sampleMessages)
        {
            var sw = Stopwatch.StartNew();
            router.RouteSync(msg, activePlaybookName: null);
            sw.Stop();

            latencies.Add(sw.Elapsed.TotalMilliseconds);
            sw.Elapsed.TotalMilliseconds.Should().BeLessThan(50,
                $"50-capability manifest routing took {sw.Elapsed.TotalMilliseconds:F3}ms");
        }

        latencies.Sort();
        _output.WriteLine($"50-capability manifest latency (20 messages):");
        _output.WriteLine($"  P50: {latencies[(int)(latencies.Count * 0.50)]:F3}ms");
        _output.WriteLine($"  P95: {latencies[(int)(latencies.Count * 0.95)]:F3}ms");
        _output.WriteLine($"  Max: {latencies.Last():F3}ms");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static CapabilityManifestEntry MakeEntry(
        string name,
        string[] keywordHints,
        string description)
    {
        return new CapabilityManifestEntry(
            CapabilityName: name,
            Description: description,
            KeywordHints: keywordHints,
            PlaybookId: null,
            ToolNames: [],
            IsEnabled: true,
            TenantRestrictions: []);
    }

    private static List<CorpusEntry> LoadCorpus()
    {
        // Resolve path relative to the test assembly location, walking up to repo root.
        var assemblyDir = Path.GetDirectoryName(typeof(CapabilityRouterBenchmarkTests).Assembly.Location)!;
        var repoRoot = FindRepoRoot(assemblyDir)
            ?? throw new InvalidOperationException(
                "Could not find repository root (looked for notes/routing-benchmark-corpus.json)");
        var corpusPath = Path.Combine(repoRoot, "notes", "routing-benchmark-corpus.json");

        if (!File.Exists(corpusPath))
        {
            throw new FileNotFoundException(
                $"Benchmark corpus not found at {corpusPath}. " +
                "Ensure notes/routing-benchmark-corpus.json exists at the repository root.",
                corpusPath);
        }

        var json = File.ReadAllText(corpusPath);
        var doc = JsonDocument.Parse(json);
        var corpusArray = doc.RootElement.GetProperty("corpus");

        var entries = new List<CorpusEntry>();
        foreach (var item in corpusArray.EnumerateArray())
        {
            entries.Add(new CorpusEntry
            {
                Id = item.GetProperty("id").GetInt32(),
                Message = item.GetProperty("message").GetString()!,
                ExpectedLayer = item.GetProperty("expectedLayer").GetInt32(),
                ExpectedCapability = item.TryGetProperty("expectedCapability", out var cap) &&
                                     cap.ValueKind != JsonValueKind.Null
                    ? cap.GetString()
                    : null,
                Category = item.GetProperty("category").GetString()!,
            });
        }

        return entries;
    }

    private static string? FindRepoRoot(string startDir)
    {
        var dir = startDir;
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir, "notes", "routing-benchmark-corpus.json")))
                return dir;
            // Also check for .git as a fallback indicator of repo root.
            if (Directory.Exists(Path.Combine(dir, ".git")) &&
                File.Exists(Path.Combine(dir, "notes", "routing-benchmark-corpus.json")))
                return dir;
            dir = Path.GetDirectoryName(dir);
        }

        return null;
    }

    private sealed class CorpusEntry
    {
        public int Id { get; init; }
        public string Message { get; init; } = string.Empty;
        public int ExpectedLayer { get; init; }
        public string? ExpectedCapability { get; init; }
        public string Category { get; init; } = string.Empty;
    }
}
