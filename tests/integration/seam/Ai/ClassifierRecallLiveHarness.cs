using System.Collections.Concurrent;
using System.Text.Json;
using Azure.Core;
using Azure.Identity;
using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Models.Ai.Communication;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.LinearConsumers;
using Sprk.Bff.Api.Services.Ai.PublicContracts;
using Sprk.Bff.Api.Services.Communication;
using Sprk.Bff.Api.Services.Communication.Engine;
using Sprk.Bff.Api.Services.Communication.Engine.Rungs;
using Sprk.Bff.Api.Services.Communication.Models;
using Xunit;
using Xunit.Abstractions;

namespace Sprk.Bff.Api.Tests.Seam.Ai;

/// <summary>
/// spaarke-ontology-platform-r1 task 074 (D-10 / D-64 / D-116): measures TRIAGE-EMAIL recall on the gated
/// categories against the labelled fixture <c>tests/fixtures/ontology-classifier-recall/labelled-set.json</c>,
/// through the REAL production chain, against live spaarkedev1 rows and the live model.
/// </summary>
/// <remarks>
/// <para>
/// <b>What is real.</b> Every class on the email-triage path is the production class, registered the way the
/// BFF modules register it: rung 5 (<see cref="AiClassificationRung"/> → <see cref="CommunicationClassificationAi"/>),
/// the association mapper that writes the provenance (<see cref="AssociationStatusMapper"/>), the provenance
/// JSON round trip and <see cref="PersistedClassificationSignalReader"/> (what enrichment reads back), then
/// <see cref="CommunicationTriageAi"/> → <see cref="ActionResolver"/> (live <c>sprk_playbookconsumer</c> routing)
/// → <see cref="ActionRunner"/> with the live <c>$choices</c> enum and task-072 guidance
/// (<see cref="LookupChoicesResolver"/>, enabled rows only). Nothing on that path is faked.
/// </para>
/// <para>
/// <b>What differs from a deployed capture</b> (all deliberate, D-64 decision 5): no <c>MatterId</c>, so no RAG
/// grounding; no Dataverse writes; the credential is the operator's <see cref="AzureCliCredential"/> instead of
/// the App Service managed identity (ADR-028 exception E-2: the OpenAI account accepts user tokens); and only
/// rung 5 runs, because the deterministic rungs need real matter/thread context the synthetic items do not have.
/// </para>
/// <para>
/// <b>Cost.</b> Two model calls per item (rung-5 classify + TRIAGE-EMAIL), plus any SDK retries. Opt-in only:
/// unless <see cref="RunEnvVar"/> is <c>1</c> nothing touches the network, so a pass in CI means "skipped".
/// The test fails when the RUN is invalid (a <c>$choices</c> resolution failure, a triage that failed rather than
/// classified, a missing tie-breaker in the live guidance). It does NOT assert the recall floor: the gate is a
/// project decision recorded in <c>notes/classifier-recall-measurement.md</c>, not a CI assertion.
/// </para>
/// </remarks>
[Trait("Category", "Live")]
public sealed class ClassifierRecallLiveHarness
{
    public const string RunEnvVar = "ONTOLOGY_RECALL_LIVE_RUN";
    public const string DataverseUrlEnvVar = "ONTOLOGY_RECALL_DATAVERSE_URL";
    public const string OpenAiEndpointEnvVar = "ONTOLOGY_RECALL_OPENAI_ENDPOINT";
    public const string TenantIdEnvVar = "ONTOLOGY_RECALL_TENANT_ID";
    public const string OutputEnvVar = "ONTOLOGY_RECALL_OUT";

    private static readonly JsonSerializerOptions ProvenanceJsonOptions = new()
    {
        // Same options IncomingAssociationResolver serializes sprk_associationprovenance with.
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
    };

    private readonly ITestOutputHelper _output;

    public ClassifierRecallLiveHarness(ITestOutputHelper output) => _output = output;

    [Fact]
    public async Task MeasureRecall_OnTheLabelledSet_ThroughTheRealTriagePath()
    {
        if (Environment.GetEnvironmentVariable(RunEnvVar) != "1") return;

        var dataverseUrl = Required(DataverseUrlEnvVar);
        var openAiEndpoint = Required(OpenAiEndpointEnvVar);
        var tenantId = Required(TenantIdEnvVar);
        var outPath = Required(OutputEnvVar);

        var set = RecallFixture.Load();
        var logs = new CapturingLoggerProvider();
        await using var root = BuildServices(dataverseUrl, openAiEndpoint, tenantId, logs);

        // Pre-flight (no model cost): the live taxonomy and guidance the run will see. The enum must hold every
        // label in the key, and the guidance must carry the Fee/Scope tie-breakers (task 072).
        await using (var preScope = root.CreateAsyncScope())
        {
            var action = await preScope.ServiceProvider.GetRequiredService<IActionResolver>()
                .ResolveAsync(ConsumerTypes.EmailTriage, CancellationToken.None);
            var choices = await preScope.ServiceProvider.GetRequiredService<LookupChoicesResolver>()
                .ResolveFromJpsAsync(action.SystemPrompt, CancellationToken.None);
            const string choicesRef = "lookup:sprk_triagecategory.sprk_name";
            choices.Should().ContainKey(choicesRef);
            choices[choicesRef].Should().Contain(set.Items.Select(i => i.Label).Distinct(),
                "every key label must be a live, enabled category the classifier can emit");
            var guidance = choices[LookupChoicesResolver.GuidanceKey(choicesRef)];
            guidance.Should().Contain(l => l.StartsWith("Fee / rate change — ", StringComparison.Ordinal) && l.Contains("Scope / budget change"));
            guidance.Should().Contain(l => l.StartsWith("Scope / budget change — ", StringComparison.Ordinal) && l.Contains("Fee / rate change"));
            _output.WriteLine($"Action {action.Id} ({action.Name}) modelTier={action.ModelTier} temp={action.Temperature}; enum={choices[choicesRef].Length} categories");
        }

        var rung = root.GetRequiredService<AiClassificationRung>();
        var mapper = root.GetRequiredService<AssociationStatusMapper>();
        var results = new List<RecallItemResult>(set.Items.Count);

        foreach (var item in set.Items)
        {
            var message = new NormalizedMessage
            {
                Direction = CommunicationDirection.Incoming,
                From = item.From,
                Subject = item.Subject,
                BodyText = item.Body,
            };

            // Rung 5 → provenance → JSON → read back: the exact hand-off enrichment depends on.
            var matches = await rung.EvaluateAsync(message, new AssociationContext(), CancellationToken.None);
            var decision = mapper.Decide(matches, CommunicationDirection.Incoming, tenantKey: null);
            var provenanceJson = JsonSerializer.Serialize(decision.Provenance, ProvenanceJsonOptions);
            var classification = PersistedClassificationSignalReader.TryReadFromProvenanceJson(provenanceJson);

            CommunicationTriageResult? triage = null;
            if (classification is not null)
            {
                await using var scope = root.CreateAsyncScope();
                triage = await scope.ServiceProvider.GetRequiredService<ICommunicationTriageAi>().TriageAsync(
                    new CommunicationTriageRequest
                    {
                        Classification = classification,
                        Subject = message.Subject ?? string.Empty,
                        BodyText = message.BodyText ?? string.Empty,
                        MatterId = null,
                        TenantId = tenantId,
                    },
                    CancellationToken.None);
            }

            results.Add(new RecallItemResult(
                item.Id, item.Label, item.LabelSource,
                Rung5Category: classification?.Category,
                SignalPresent: classification is not null,
                Predicted: triage?.Category,
                Priority: triage?.Priority,
                ReviewOutcome: triage?.ReviewOutcome,
                Summary: triage?.Summary));
            _output.WriteLine($"{item.Id} label={item.Label} rung5={classification?.Category ?? "(none)"} predicted={triage?.Category ?? "(none)"}");
        }

        var metrics = RecallScorer.Score(results, RecallFixture.GatedCategories);
        var warnings = logs.Entries.Where(e => e.Level >= LogLevel.Warning).ToList();
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outPath))!);
        await File.WriteAllTextAsync(outPath, JsonSerializer.Serialize(new
        {
            runAtUtc = DateTimeOffset.UtcNow,
            fixture = RecallFixture.RelativePath,
            dataverseUrl,
            modelCallsExpected = results.Count + results.Count(r => r.SignalPresent),
            warnings = warnings.Select(w => new { w.Category, level = w.Level.ToString(), w.Message, w.ExceptionType }),
            linearRuns = logs.Entries.Where(e => e.Category.EndsWith(nameof(ActionRunner), StringComparison.Ordinal)
                                                  && e.Message.StartsWith("Linear run:", StringComparison.Ordinal))
                                     .Select(e => e.Message).Distinct().Take(3),
            metrics,
            items = results,
        }, new JsonSerializerOptions { WriteIndented = true }));

        _output.WriteLine(JsonSerializer.Serialize(metrics, new JsonSerializerOptions { WriteIndented = true }));

        // Run validity, not the gate: a run with degraded plumbing measures the plumbing, not the classifier.
        warnings.Where(w => w.Category.Contains("LookupChoicesResolver", StringComparison.Ordinal)
                            || w.Message.Contains("$choices", StringComparison.Ordinal))
            .Should().BeEmpty("a $choices/guidance resolution failure means the prompt was not the production prompt");
        warnings.Where(w => w.Category.Contains(nameof(CommunicationTriageAi), StringComparison.Ordinal))
            .Should().BeEmpty("a triage that failed (as opposed to classified) is a harness/plumbing failure, not a miss");
        warnings.Where(w => w.Level >= LogLevel.Error)
            .Should().BeEmpty("an error anywhere on the path (Dataverse, routing, OpenAI) means the run did not measure the classifier");
        results.Where(r => r.SignalPresent && r.Predicted is null)
            .Should().BeEmpty("TriageAsync returned no category for an item that had a classification signal");
    }

    [Fact]
    public void Scorer_ComputesStrictAndPredicateRecallAndPrecision()
    {
        // Hand-computed: Fee 2 (1 caught as Fee, 1 caught as Scope); Scope 2 (1 caught, 1 missed as Invoice);
        // negatives 2 (1 correct, 1 false positive as Fee).
        var rows = new[]
        {
            Row("a", "Fee / rate change", "Fee / rate change"),
            Row("b", "Fee / rate change", "Scope / budget change"),
            Row("c", "Scope / budget change", "Scope / budget change"),
            Row("d", "Scope / budget change", "Invoice / Billing"),
            Row("e", "Invoice / Billing", "Invoice / Billing"),
            Row("f", "Administrative", "Fee / rate change"),
        };

        var m = RecallScorer.Score(rows, RecallFixture.GatedCategories);

        m.Positives.Should().Be(4);
        m.PredicateRecall.Should().Be(0.75);       // a, b, c of 4
        m.PredicatePrecision.Should().Be(0.75);    // a, b, c of {a, b, c, f}
        m.PerCategory["Fee / rate change"].StrictRecall.Should().Be(0.5);
        m.PerCategory["Fee / rate change"].StrictPrecision.Should().Be(0.5); // a of {a, f}
        m.PerCategory["Scope / budget change"].StrictRecall.Should().Be(0.5);
        m.PerCategory["Scope / budget change"].StrictPrecision.Should().Be(0.5); // c of {b, c}
        m.ExactAccuracy.Should().BeApproximately(3.0 / 6, 1e-9);
        m.PredicateMisses.Should().Equal("d");

        static RecallItemResult Row(string id, string label, string predicted) =>
            new(id, label, "test", null, true, predicted, null, null, null);
    }

    private static string Required(string name) =>
        Environment.GetEnvironmentVariable(name) is { Length: > 0 } v
            ? v
            : throw new InvalidOperationException($"{RunEnvVar}=1 requires {name}.");

    /// <summary>
    /// The production registrations for exactly the triage path (AnalysisServicesModule, GraphModule,
    /// RoutingModule, LinearConsumersModule, ToolFrameworkExtensions, CommunicationModule), bound to an
    /// in-memory configuration that mirrors the dev App Service's non-secret settings.
    /// </summary>
    private static ServiceProvider BuildServices(string dataverseUrl, string openAiEndpoint, string tenantId, CapturingLoggerProvider logs)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Dataverse:ServiceUrl"] = dataverseUrl,
            ["Graph:ManagedIdentity:Enabled"] = "true", // DataverseServiceClientImpl uses the injected TokenCredential
            ["DocumentIntelligence:Enabled"] = "true",
            ["DocumentIntelligence:OpenAiEndpoint"] = openAiEndpoint,
            ["DocumentIntelligence:SummarizeModel"] = "gpt-4o-mini",
            ["DocumentIntelligence:ReasoningModel"] = "gpt-5-reasoning",
            ["Communication:AiClassification:Enabled"] = "true",
            ["TENANT_ID"] = tenantId,
        }).Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddLogging(b => b.SetMinimumLevel(LogLevel.Information).AddProvider(logs));
        services.AddMemoryCache();
        services.AddSingleton<IHostEnvironment>(new HarnessHostEnvironment("Production")); // dev BFF ASPNETCORE_ENVIRONMENT
        services.AddSingleton<TokenCredential>(new AzureCliCredential(new AzureCliCredentialOptions { TenantId = tenantId }));
        services.Configure<DocumentIntelligenceOptions>(configuration.GetSection("DocumentIntelligence"));
        services.Configure<AiClassificationOptions>(configuration.GetSection("Communication:AiClassification"));
        services.Configure<AutoFileOptions>(_ => { });

        // AnalysisServicesModule (DocumentIntelligence:Enabled branch) + ToolFrameworkExtensions.
        services.AddSingleton<OpenAiClient>();
        services.AddSingleton<IOpenAiClient>(sp => sp.GetRequiredService<OpenAiClient>());
        services.AddSingleton<PromptSchemaRenderer>();
        services.AddScoped<LookupChoicesResolver>();
        services.AddHttpClient<AnalysisActionService>();
        services.AddHttpClient<AnalysisSkillService>();
        services.AddHttpClient<AnalysisKnowledgeService>();
        services.AddHttpClient<AnalysisToolService>();
        services.AddHttpClient<AnalysisPersonaService>();
        services.AddSingleton<IPlaybookService, NullPlaybookService>(); // ScopeResolverService ctor dep; unused on this path
        services.AddHttpClient<IScopeResolverService, ScopeResolverService>();
        services.AddSingleton<IRagService, NullRagService>(); // never called: MatterId is null
        services.AddScoped<ICommunicationClassificationAi, CommunicationClassificationAi>();
        services.AddScoped<ICommunicationTriageAi, CommunicationTriageAi>();

        // GraphModule: the Dataverse client behind IGenericEntityService.
        services.AddSingleton<IDataverseService>(sp => new DataverseServiceClientImpl(
            sp.GetRequiredService<IConfiguration>(),
            sp.GetRequiredService<ILogger<DataverseServiceClientImpl>>(),
            confidentialClients: null,
            managedIdentityCredential: sp.GetRequiredService<TokenCredential>()));
        services.AddSingleton<IGenericEntityService>(sp => sp.GetRequiredService<IDataverseService>());

        // RoutingModule + LinearConsumersModule.
        services.AddScoped<IConsumerRoutingService, ConsumerRoutingService>();
        services.AddScoped<IActionResolver, ActionResolver>();
        services.AddSingleton<IActionRunner, ActionRunner>();

        // CommunicationModule: rung 5 and the mapper that writes the provenance.
        services.AddSingleton<AutoFileGate>();
        services.AddSingleton<AssociationStatusMapper>();
        services.AddSingleton<AiClassificationRung>();

        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }

    private sealed class HarnessHostEnvironment(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;
        public string ApplicationName { get; set; } = "Sprk.Bff.Api";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } =
            new Microsoft.Extensions.FileProviders.NullFileProvider();
    }
}

/// <summary>One item's outcome. <c>Predicted</c> is the category TRIAGE-EMAIL emitted, or null when no triage ran.</summary>
public sealed record RecallItemResult(
    string Id,
    string Label,
    string LabelSource,
    string? Rung5Category,
    bool SignalPresent,
    string? Predicted,
    string? Priority,
    string? ReviewOutcome,
    string? Summary);

public sealed record CategoryMetrics(int Labelled, int Predicted, int Correct, double StrictRecall, double? StrictPrecision);

public sealed record RecallMetrics(
    int Items,
    int Positives,
    int Negatives,
    double PredicateRecall,
    double PredicateRecallWilsonLow95,
    double PredicateRecallWilsonHigh95,
    double? PredicatePrecision,
    double ExactAccuracy,
    IReadOnlyDictionary<string, CategoryMetrics> PerCategory,
    IReadOnlyList<string> PredicateMisses,
    IReadOnlyList<string> PredicateFalsePositives,
    IReadOnlyDictionary<string, IReadOnlyDictionary<string, int>> Confusion);

/// <summary>
/// Pure scoring. Predicate = "labelled Fee OR Scope → predicted Fee OR Scope" (D-64 gate); strict = same category.
/// No triage result counts as a miss, exactly as production leaves the category unset.
/// </summary>
public static class RecallScorer
{
    public static RecallMetrics Score(IReadOnlyList<RecallItemResult> rows, IReadOnlyList<string> gated)
    {
        bool IsGated(string? c) => c is not null && gated.Contains(c);

        var positives = rows.Where(r => IsGated(r.Label)).ToList();
        var predictedPositive = rows.Where(r => IsGated(r.Predicted)).ToList();
        var caught = positives.Count(r => IsGated(r.Predicted));

        var per = gated.ToDictionary(g => g, g =>
        {
            var labelled = rows.Count(r => r.Label == g);
            var predicted = rows.Count(r => r.Predicted == g);
            var correct = rows.Count(r => r.Label == g && r.Predicted == g);
            return new CategoryMetrics(labelled, predicted, correct,
                labelled == 0 ? 0 : (double)correct / labelled,
                predicted == 0 ? null : (double)correct / predicted);
        });

        var recall = positives.Count == 0 ? 0 : (double)caught / positives.Count;
        var (lo, hi) = Wilson(caught, positives.Count);

        var confusion = rows
            .GroupBy(r => r.Label)
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyDictionary<string, int>)g
                    .GroupBy(r => r.Predicted ?? "(none)")
                    .OrderBy(x => x.Key, StringComparer.Ordinal)
                    .ToDictionary(x => x.Key, x => x.Count()));

        return new RecallMetrics(
            Items: rows.Count,
            Positives: positives.Count,
            Negatives: rows.Count - positives.Count,
            PredicateRecall: recall,
            PredicateRecallWilsonLow95: lo,
            PredicateRecallWilsonHigh95: hi,
            PredicatePrecision: predictedPositive.Count == 0 ? null : (double)predictedPositive.Count(r => IsGated(r.Label)) / predictedPositive.Count,
            ExactAccuracy: rows.Count == 0 ? 0 : (double)rows.Count(r => r.Label == r.Predicted) / rows.Count,
            PerCategory: per,
            PredicateMisses: positives.Where(r => !IsGated(r.Predicted)).Select(r => r.Id).ToList(),
            PredicateFalsePositives: predictedPositive.Where(r => !IsGated(r.Label)).Select(r => r.Id).ToList(),
            Confusion: confusion);
    }

    private static (double Low, double High) Wilson(int successes, int n)
    {
        if (n == 0) return (0, 0);
        const double z = 1.959963984540054;
        var p = (double)successes / n;
        var denom = 1 + z * z / n;
        var centre = (p + z * z / (2 * n)) / denom;
        var half = z * Math.Sqrt(p * (1 - p) / n + z * z / (4.0 * n * n)) / denom;
        return (Math.Max(0, centre - half), Math.Min(1, centre + half));
    }
}

/// <summary>Loads the committed labelled set (the repeatable fixture).</summary>
public static class RecallFixture
{
    public const string RelativePath = "tests/fixtures/ontology-classifier-recall/labelled-set.json";
    public static readonly IReadOnlyList<string> GatedCategories = ["Fee / rate change", "Scope / budget change"];

    public sealed record Item(string Id, string Subject, string From, string Body, string Label, string LabelSource);
    public sealed record Set(IReadOnlyList<Item> Items);

    public static Set Load()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Spaarke.sln"))) dir = dir.Parent;
        if (dir is null) throw new InvalidOperationException("Repo root (Spaarke.sln) not found from the test output directory.");

        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(dir.FullName, RelativePath)));
        var items = doc.RootElement.GetProperty("items").EnumerateArray().Select(e => new Item(
            e.GetProperty("id").GetString()!,
            e.GetProperty("subject").GetString()!,
            e.GetProperty("from").GetString()!,
            e.GetProperty("body").GetString()!,
            e.GetProperty("label").GetString()!,
            e.GetProperty("labelSource").GetString()!)).ToList();
        return new Set(items);
    }
}

/// <summary>Records log entries so the harness can tell a degraded run from a measured one.</summary>
internal sealed class CapturingLoggerProvider : ILoggerProvider
{
    public sealed record Entry(string Category, LogLevel Level, string Message, string? ExceptionType);

    private readonly ConcurrentQueue<Entry> _entries = new();
    public IReadOnlyList<Entry> Entries => _entries.ToArray();

    public ILogger CreateLogger(string categoryName) => new Logger(categoryName, _entries);
    public void Dispose() { }

    private sealed class Logger(string category, ConcurrentQueue<Entry> sink) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel)) return;
            sink.Enqueue(new Entry(category, logLevel, formatter(state, exception), exception?.GetType().FullName));
        }
    }
}
