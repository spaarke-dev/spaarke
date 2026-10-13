using Xunit;

namespace Spaarke.ArchTests.Adr032;

/// <summary>
/// <b>ADR-032 / CLAUDE.md §10 F.1 — asymmetric registration</b>, static half (customer-provisioning-orchestration-r1 task 204e,
/// punch row B01; the missing forcing function named in the <c>e3a15db91</c> commit message).
///
/// <para><b>The failure.</b> <c>IActionSeam</c> was registered inside <c>if (analysisEnabled &amp;&amp; documentIntelligenceEnabled)</c>
/// (inside a helper that gate calls); <c>CommunicationRiActionService</c>, registered unconditionally, injects it. With
/// <c>DocumentIntelligence:Enabled=false</c> the container cannot build the consumer and the BFF dies at <c>Host.StartAsync</c>
/// (exit 134) — found live, on a Model 1 Prod stand-up, 2026-08-24. The existing ADR-032 guidance catches "kill switch declared";
/// nothing caught "consumer registered on a path where its dependency is not".</para>
///
/// <para><b>The rule</b> (<see cref="DiRegistrationScan"/> builds the model): for every registration, each constructor
/// dependency (reflected from the implementation type) or <c>GetRequiredService&lt;T&gt;</c> (inside a factory lambda) that the
/// BFF registers somewhere must be registered on EVERY execution path on which the consumer is registered. Paths are the
/// <c>if</c>/<c>else</c> branches reached from <c>Program.cs</c> through the module extension methods — a helper called inside a
/// gate inherits the gate, and <c>if (…) return</c> gates the rest of the block. A dependency registered in both branches (real +
/// Null-Object, ADR-032 P2/P3), in a branch that throws, or under a gate the consumer's own gate implies (<c>a &amp;&amp; b</c> ⇒
/// <c>a</c>) is satisfied. The task asked for "unconditional consumer"; the check is the strict generalisation (it also catches a
/// Null peer whose own dependency is gated — the PR #351 / <c>IInsightsAi</c> shape).</para>
///
/// <para><b>Result today.</b> The scan finds 16 real asymmetries (ledgered in <see cref="KnownDefects"/>, ids shared with the
/// runtime oracle <see cref="GateCombinationConstructibilityTests"/>) and 1 false positive (<see cref="FalsePositives"/>). They are
/// confirmed defects — see <see cref="GateCombinationConstructibilityTests.DefectNotes"/> — not exemptions: the test is a ratchet.</para>
///
/// <para><b>Known limits</b> (the runtime oracle covers the first two): (1) a provider the scanner cannot read — an untyped factory
/// (<c>AddSingleton(sp =&gt; …)</c>), <c>AddChatClient</c>, or a reflection scan such as <c>AddToolHandlersFromAssembly</c> — is
/// invisible, so a consumer of it is not checked; (2) two <c>if</c> statements are unrelated unless one's condition text implies the
/// other's, so equal flags spelled differently read as different; (3) an unconditionally-mapped ENDPOINT handler that injects a
/// conditional service is not a registration and is not seen (§F.1 steps 1-2 stay a reviewer duty, and the endpoint-resolution
/// failure is the one the runtime oracle does not exercise); (4) lifetimes are not compared.</para>
///
/// <para><b>KEEP path</b> (ADR-038 §7 / Amendment A1, eighth path): structural fitness function, like
/// <see cref="ADR010_DITests"/> and <see cref="CredentialGuardTests"/>.</para>
///
/// <para><b>Maintenance procedure — this test failed:</b></para>
/// <list type="bullet">
///   <item><b>NEW asymmetric registration</b> — apply ADR-032 in the module: P1 move the dependency out of the gate; P2/P3 register a
///   Null-Object in the <c>else</c>; or register the consumer under the SAME flag. Do NOT add a ledger line to make it pass.</item>
///   <item><b>Ledgered defect no longer reproduces</b> — you fixed it; delete the row from <see cref="KnownDefects"/> (and from the
///   runtime ledger if listed there).</item>
///   <item><b>False positive</b> — add a <see cref="FalsePositives"/> row with the reason it is not a defect and the evidence (a boot
///   in <see cref="GateCombinationConstructibilityTests"/> that proves it constructs). Improve the scanner first if the pattern is general.</item>
/// </list>
/// </summary>
public class AsymmetricRegistrationTests
{
    // ───────────────────────────────────── ledgers ─────────────────────────────────────

    internal sealed record Entry(string Consumer, string Dependency, string Id, string Reason);

    /// <summary>Confirmed defects (ids and notes: <see cref="GateCombinationConstructibilityTests.DefectNotes"/>).</summary>
    internal static readonly IReadOnlyList<Entry> KnownDefects = new Entry[]
    {
        new("InvoiceExtractionJobHandler", "IInvoiceAnalysisService", "DEF-FIN", "registered unconditionally; IInvoiceAnalysisService only under DocumentIntelligence:Enabled"),
        new("InvoiceExtractionJobHandler", "TextExtractorService", "DEF-FIN", "registered unconditionally; the concrete TextExtractorService only under DocumentIntelligence:Enabled (the else branch registers only the ITextExtractor Null-Object)"),
        new("AttachmentClassificationJobHandler", "IInvoiceAnalysisService", "DEF-FIN", "gated on RecordMatchingEnabled alone; IInvoiceAnalysisService needs DocumentIntelligence:Enabled"),
        new("AttachmentClassificationJobHandler", "TextExtractorService", "DEF-FIN", "gated on RecordMatchingEnabled alone; TextExtractorService needs DocumentIntelligence:Enabled"),

        new("AppOnlyDocumentAnalysisJobHandler", "IAppOnlyAnalysisService", "DEF-JOBS", "registered unconditionally; IAppOnlyAnalysisService only under analysis && docIntel"),
        new("EmailAnalysisJobHandler", "IAppOnlyAnalysisService", "DEF-JOBS", "registered unconditionally; IAppOnlyAnalysisService only under analysis && docIntel"),
        new("ProfileSummaryJobHandler", "IAppOnlyAnalysisService", "DEF-JOBS", "gated on docIntel; IAppOnlyAnalysisService needs analysis && docIntel"),
        new("EmbeddingMigrationService", "IKnowledgeDeploymentService", "DEF-JOBS", "hosted service registered unconditionally; needs AI Search (analysis && docIntel && AiSearchEndpoint)"),
        new("EmbeddingMigrationService", "IOpenAiClient", "DEF-JOBS", "hosted service registered unconditionally; IOpenAiClient only under docIntel"),

        new("ObservationIndexUpserter", "IOpenAiClient", "DEF-INS", "registered unconditionally (InsightsIngestModule); IOpenAiClient only under docIntel"),

        new("PinnedContextRecallService", "IEmbeddingCache", "DEF-RAG", "analysis && docIntel; IEmbeddingCache additionally needs a non-empty AiSearchEndpoint and has no Null peer"),
        new("SemanticSearchService", "IKnowledgeDeploymentService", "DEF-RAG", "as above"),
        new("SemanticSearchService", "IEmbeddingCache", "DEF-RAG", "as above"),
        new("RecordSearchService", "IEmbeddingCache", "DEF-RAG", "as above"),
        new("RecordSearchService", "IKnowledgeDeploymentService", "DEF-RAG", "as above"),
        new("ReferenceRetrievalService", "IEmbeddingCache", "DEF-RAG", "as above"),
    };

    /// <summary>Reported by the scan, not a defect.</summary>
    internal static readonly IReadOnlyList<Entry> FalsePositives = new Entry[]
    {
        new("AttachmentClassificationJobHandler", "IRecordMatchService", "FP-1",
            "Both gates read the SAME flag, DocumentIntelligence:RecordMatchingEnabled — FinanceModule inline, AddRecordMatchingServices through a local variable — so the scanner (which compares condition text) sees two unrelated conditions. " +
            "Proven by the runtime oracle: with RecordMatching on and DocIntel off, Attachment…Handler fails only on IInvoiceAnalysisService (DEF-FIN), never IRecordMatchService."),
    };

    /// <summary>Methods that take an <c>IServiceCollection</c> and register, but are not reachable from <c>Program.cs</c> by a call the scanner can read.</summary>
    internal static readonly IReadOnlyDictionary<string, string> UnreachableRegistrars = new Dictionary<string, string>
    {
        ["ToolFrameworkExtensions.cs::AddToolHandler"] =
            "Public library helper (AddToolHandler<T>/AddToolHandler(Type)); the BFF registers its handlers by reflection through AddToolHandlersFromAssembly inside AddToolFramework, whose gate (analysis && docIntel) is the only one that matters and is covered by the runtime oracle.",
    };

    /// <summary>Type names that exist more than once in the BFF, so a registration of that name cannot be resolved to one constructor.</summary>
    internal static readonly IReadOnlyDictionary<string, string> AmbiguousTypeNames = new Dictionary<string, string>
    {
        ["FinancialCalculationToolHandler"] =
            "Two types share the name (a Finance tool handler and an AI tool handler); FinanceModule registers it by an unqualified name. Its constructor dependencies are skipped by the scan; the runtime oracle constructs the real one.",
    };

    // ───────────────────────────────────── the real tree ─────────────────────────────────────

    private static readonly Lazy<DiRegistrationScan.Result> ProductionScan = new(() =>
    {
        var sources = new List<(string File, string Source)>();
        foreach (var root in new[]
                 {
                     Path.Combine(SourceScan.RepoRoot, "src", "server", "api", "Sprk.Bff.Api"),
                     Path.Combine(SourceScan.RepoRoot, "src", "server", "shared"),
                 })
        {
            foreach (var f in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
            {
                if (f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                    || f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
                {
                    continue;
                }

                var text = File.ReadAllText(f);
                var isEntry = Path.GetFileName(f) == "Program.cs" && f.Contains("Sprk.Bff.Api", StringComparison.Ordinal);
                if (isEntry || text.Contains("IServiceCollection", StringComparison.Ordinal))
                    sources.Add((SourceScan.Relative(f).Replace('\\', '/'), text));
            }
        }

        var assemblies = new[] { typeof(Program).Assembly }
            .Concat(typeof(Program).Assembly.GetReferencedAssemblies()
                .Where(a => a.Name!.StartsWith("Spaarke", StringComparison.Ordinal))
                .Select(System.Reflection.Assembly.Load));
        return DiRegistrationScan.Analyze(sources, new DiRegistrationScan.TypeLookup(assemblies));
    });

    private static string Simple(string implOrKey)
    {
        var t = implOrKey;
        var arrow = t.IndexOf("->", StringComparison.Ordinal);
        if (arrow >= 0) t = t[(arrow + 2)..];
        var dot = t.LastIndexOf('.');
        return dot < 0 ? t : t[(dot + 1)..];
    }

    private static string ConsumerName(DiRegistrationScan.Violation v) => Simple(v.Consumer.Impl ?? v.Consumer.Key);

    /// <summary>The message a developer reads: names the pair, the gates and the fix.</summary>
    internal static string Describe(DiRegistrationScan.Violation v)
        => $"{ConsumerName(v)} (registered {v.Consumer.File}:{v.Consumer.Line} in {v.Consumer.Method}, {v.Consumer.Gate}) constructor-injects {v.Dependency}, " +
           $"which is registered only under: {string.Join(" | ", v.DependencyRegs.Select(r => $"{r.File}:{r.Line} {r.Gate}").Distinct())}. " +
           "Hoist the dependency out of the gate (ADR-032 P1), register a Null-Object in the else (P2/P3), or register the consumer under the same flag.";

    [Fact(DisplayName = "ADR-032 F.1: a registered service's dependencies are registered on every path the service is (static scan, ratcheted ledger)")]
    public void UnconditionalConsumerMustHaveUnconditionalDependency()
    {
        var scan = ProductionScan.Value;

        var known = KnownDefects.Concat(FalsePositives).ToDictionary(e => $"{e.Consumer} <- {e.Dependency}");
        var observed = scan.Violations.ToDictionary(v => $"{ConsumerName(v)} <- {v.Dependency}", v => v);

        var unexpected = observed.Where(kv => !known.ContainsKey(kv.Key)).Select(kv => "    - " + Describe(kv.Value)).OrderBy(x => x).ToList();
        var stale = known.Keys.Where(k => !observed.ContainsKey(k)).OrderBy(k => k).Select(k => $"    - {k} ({known[k].Id})").ToList();

        Assert.True(unexpected.Count == 0 && stale.Count == 0,
            (unexpected.Count == 0 ? "" : "NEW asymmetric registration(s) (CLAUDE.md §10 F.1 / ADR-032) — fix the module, do not ledger:\n" + string.Join("\n", unexpected) + "\n") +
            (stale.Count == 0 ? "" : "Ledgered entries the scan no longer reports — delete them from AsymmetricRegistrationTests.KnownDefects / FalsePositives:\n" + string.Join("\n", stale)));
    }

    [Fact(DisplayName = "ADR-032 F.1 scan: the model covers the composition root (registrations found, every registrar reachable, no unexplained ambiguity)")]
    public void ScanCoversTheCompositionRoot()
    {
        var scan = ProductionScan.Value;

        Assert.True(scan.Registrations.Count > 500, $"Only {scan.Registrations.Count} registrations modelled; the BFF registers ~700 — the walker regressed.");
        Assert.True(scan.Registrations.Count(r => r.Path.Count > 0) > 150, "Too few conditional registrations modelled — the if/else walker regressed.");

        var unexplainedUnreachable = scan.UnreachableMethodsWithRegistrations
            .Where(u => !UnreachableRegistrars.Keys.Any(k => u.Replace('\\', '/').Contains(k, StringComparison.Ordinal)))
            .ToList();
        Assert.True(unexplainedUnreachable.Count == 0,
            "Registrar method(s) the walk from Program.cs never reaches — their registrations are NOT checked. Fix the call-following, or add a reasoned row to UnreachableRegistrars:\n  " +
            string.Join("\n  ", unexplainedUnreachable));

        var unexplainedAmbiguous = scan.AmbiguousTypes.Where(a => !AmbiguousTypeNames.ContainsKey(a)).ToList();
        Assert.True(unexplainedAmbiguous.Count == 0,
            "Registered type name(s) that match more than one type, so their constructors are skipped: " + string.Join(", ", unexplainedAmbiguous) +
            ". Qualify the name at the registration or add a reasoned row to AmbiguousTypeNames.");

        // Every registration token the files contain is accounted for: either reached by the walk or inside an explained-unreachable method.
        var unreachableTokens = scan.UnreachableMethodsWithRegistrations.Sum(u =>
            int.Parse(System.Text.RegularExpressions.Regex.Match(u, @"\((\d+) registration tokens\)").Groups[1].Value));
        // Exact on purpose: a registration inside a method the walk cannot reach (not an IServiceCollection extension, or never called from
        // Program.cs) is a registration nobody checks, and that is precisely what this assertion exists to surface.
        Assert.True(scan.ReachableRegistrationTokens + unreachableTokens == scan.RawRegistrationTokens,
            $"{scan.RawRegistrationTokens} registration tokens in the scanned files but {scan.ReachableRegistrationTokens} reached (+{unreachableTokens} in explained-unreachable methods): " +
            "some registration sits where the walk from Program.cs does not go (a helper that does not take IServiceCollection, a method nothing calls, or a statement the walker skipped).");
    }

    // ───────────────────────────────────── controls (negative + positive) ─────────────────────────────────────
    // Fixture implementation types live in this assembly under an Fx prefix; the fixture MODULES are source text the scanner reads.

    public interface IFxActionSeam { }
    public sealed class FxActionSeam : IFxActionSeam { }
    public sealed class FxRiActionService { public FxRiActionService(IFxActionSeam seam) { } }

    public interface IFxDep { }
    public sealed class FxDep : IFxDep { }
    public sealed class FxNullDep : IFxDep { }
    public sealed class FxConsumer { public FxConsumer(IFxDep dep) { } }

    public interface IFxPeer { }
    public sealed class FxPeer : IFxPeer { public FxPeer(IFxDep dep) { } }
    public sealed class FxNullPeer : IFxPeer { public FxNullPeer(IFxDep dep) { } }

    public interface IFxProduct { }
    public sealed class FxProduct : IFxProduct { public FxProduct(IFxDep dep) { } }

    public sealed class FxCacheConsumer { public FxCacheConsumer(Microsoft.Extensions.Caching.Distributed.IDistributedCache cache) { } }

    private const string ProgramSource = "builder.Services.AddFxModule(builder.Configuration);";

    private static DiRegistrationScan.Result ScanFixture(string moduleSource)
        => DiRegistrationScan.Analyze(
            new[] { ("Program.cs", ProgramSource), ("FxModule.cs", moduleSource) },
            new DiRegistrationScan.TypeLookup(new[] { typeof(AsymmetricRegistrationTests).Assembly }));

    private static string Module(string body, string extraMethods = "")
        => "public static class FxModule {\n" +
           "  public static IServiceCollection AddFxModule(this IServiceCollection services, IConfiguration configuration) {\n" + body + "\n    return services;\n  }\n" + extraMethods + "\n}";

    [Fact(DisplayName = "control (negative): the e3a15db91 shape — a helper called inside a compound gate registers the dependency, the consumer is unconditional")]
    public void Control_Negative_E3a15db91_HelperInsideCompoundGate()
    {
        var scan = ScanFixture(Module("""
            services.AddSingleton<FxRiActionService>();
            var documentIntelligenceEnabled = configuration.GetValue<bool>("DocumentIntelligence:Enabled");
            var analysisEnabled = configuration.GetValue<bool>("Analysis:Enabled", true);
            if (analysisEnabled && documentIntelligenceEnabled)
            {
                AddFxOrchestration(services);
            }
            """, """
            private static void AddFxOrchestration(IServiceCollection services)
            {
                services.AddSingleton<IFxActionSeam, FxActionSeam>();
            }
            """));

        var v = Assert.Single(scan.Violations);
        Assert.Equal("FxRiActionService", ConsumerName(v));
        Assert.Equal("IFxActionSeam", v.Dependency);
        var message = Describe(v);
        Assert.Contains("FxRiActionService", message);
        Assert.Contains("IFxActionSeam", message);
        Assert.Contains("analysisEnabled && documentIntelligenceEnabled", message);
        Assert.Contains("ADR-032", message);
    }

    [Fact(DisplayName = "control (negative): an early `if (!flag) return` gates the registrations after it")]
    public void Control_Negative_EarlyReturnGate()
    {
        var scan = ScanFixture(Module("""
            services.AddSingleton<FxConsumer>();
            if (!configuration.GetValue<bool>("Fx:Enabled")) { return services; }
            services.AddSingleton<IFxDep, FxDep>();
            """));

        Assert.Equal("FxConsumer", ConsumerName(Assert.Single(scan.Violations)));
    }

    [Fact(DisplayName = "control (negative): a Null-Object peer whose own dependency is registered only in the other branch (PR #351 / IInsightsAi)")]
    public void Control_Negative_NullPeerWithGatedDependency()
    {
        var scan = ScanFixture(Module("""
            if (configuration.GetValue<bool>("Fx:Enabled"))
            {
                services.AddSingleton<IFxDep, FxDep>();
                services.AddSingleton<IFxPeer, FxPeer>();
            }
            else
            {
                services.AddSingleton<IFxPeer, FxNullPeer>();
            }
            """));

        var v = Assert.Single(scan.Violations);
        Assert.Equal("FxNullPeer", ConsumerName(v));
        Assert.Equal("IFxDep", v.Dependency);
    }

    [Fact(DisplayName = "control (negative): a factory-lambda registration's GetRequiredService<T> is a dependency")]
    public void Control_Negative_FactoryLambdaDependency()
    {
        var scan = ScanFixture(Module("""
            services.AddSingleton<IFxProduct>(sp => new FxProduct(sp.GetRequiredService<IFxDep>()));
            if (configuration.GetValue<bool>("Fx:Enabled")) { services.AddSingleton<IFxDep, FxDep>(); }
            """));

        var v = Assert.Single(scan.Violations);
        Assert.Equal("IFxProduct", ConsumerName(v));
        Assert.Equal("IFxDep", v.Dependency);
    }

    [Fact(DisplayName = "control (negative): the framework cache helpers are modelled — one branch registering IDistributedCache is not enough")]
    public void Control_Negative_FrameworkProviderInOnlyOneBranch()
    {
        var scan = ScanFixture(Module("""
            services.AddSingleton<FxCacheConsumer>();
            if (configuration.GetValue<bool>("Redis:Enabled")) { services.AddStackExchangeRedisCache(o => { }); }
            """));

        Assert.Equal("FxCacheConsumer", ConsumerName(Assert.Single(scan.Violations)));
    }

    [Fact(DisplayName = "control (positive): hoisted, real-or-Null-Object, same-gate, implied-gate, throwing-else and framework-in-every-branch are all clean")]
    public void Control_Positive_SanctionedShapesAreClean()
    {
        var shapes = new Dictionary<string, string>
        {
            ["hoisted (ADR-032 P1)"] = """
                services.AddSingleton<FxConsumer>();
                services.AddSingleton<IFxDep, FxDep>();
                """,
            ["real or Null-Object in the else (ADR-032 P2/P3)"] = """
                services.AddSingleton<FxConsumer>();
                if (configuration.GetValue<bool>("Fx:Enabled")) { services.AddSingleton<IFxDep, FxDep>(); }
                else { services.AddSingleton<IFxDep, FxNullDep>(); }
                """,
            ["consumer under the same gate"] = """
                if (configuration.GetValue<bool>("Fx:Enabled"))
                {
                    services.AddSingleton<IFxDep, FxDep>();
                    services.AddSingleton<FxConsumer>();
                }
                """,
            ["consumer's gate implies the dependency's (a && b ⇒ a)"] = """
                var a = configuration.GetValue<bool>("Fx:A");
                var b = configuration.GetValue<bool>("Fx:B");
                if (a) { services.AddSingleton<IFxDep, FxDep>(); }
                if (a && b) { services.AddSingleton<FxConsumer>(); }
                """,
            ["else-if chain whose last arm throws (fail-fast)"] = """
                services.AddSingleton<FxConsumer>();
                if (configuration.GetValue<bool>("Fx:On")) { services.AddSingleton<IFxDep, FxDep>(); }
                else if (configuration.GetValue<bool>("Fx:Fallback")) { services.AddSingleton<IFxDep, FxNullDep>(); }
                else { throw new InvalidOperationException("Fx:On or Fx:Fallback must be set"); }
                """,
            ["consumer and dependency both after an early return"] = """
                if (!configuration.GetValue<bool>("Fx:Enabled")) { return services; }
                services.AddSingleton<IFxDep, FxDep>();
                services.AddSingleton<FxConsumer>();
                """,
            ["framework cache helper in every non-throwing branch"] = """
                services.AddSingleton<FxCacheConsumer>();
                if (configuration.GetValue<bool>("Redis:Enabled")) { services.AddStackExchangeRedisCache(o => { }); }
                else { services.AddDistributedMemoryCache(); }
                """,
        };

        foreach (var (name, body) in shapes)
        {
            var scan = ScanFixture(Module(body));
            Assert.True(scan.Violations.Count == 0, $"{name}: expected clean, got: {string.Join("; ", scan.Violations.Select(Describe))}");
            Assert.True(scan.Registrations.Count >= 2, $"{name}: the fixture was not read (no registrations modelled)");
        }
    }

    [Fact(DisplayName = "control: comments and string literals do not hide or fake registrations")]
    public void Control_BlankingIgnoresCommentsAndStrings()
    {
        var scan = ScanFixture(Module("""
            // services.AddSingleton<IFxDep, FxDep>();   <- commented out: not a registration
            var note = "services.AddSingleton<IFxDep, FxDep>();"; /* services.AddSingleton<IFxDep, FxDep>(); */
            services.AddSingleton<FxConsumer>();
            """));

        // the dependency is genuinely never registered, so it is "not ours" and nothing is reported; only the consumer is modelled
        Assert.Empty(scan.Violations);
        Assert.Single(scan.Registrations);
    }
}
