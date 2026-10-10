using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Spaarke.ArchTests.Adr032;

/// <summary>
/// <b>ADR-032 / CLAUDE.md §10 F.1, runtime half</b> (customer-provisioning-orchestration-r1 task 204e, punch row B01; the
/// "F.1-runtime" fixture <c>bff-extensions.md</c> §F.1 has listed as "queued" since 2026-06-05).
///
/// <para><b>The oracle.</b> Boot the REAL BFF (<see cref="BootedApp"/>, <c>Program.cs</c> end to end) once per feature-gate
/// combination with the service provider's <c>ValidateOnBuild</c> ON. The container then tries to build a call site for EVERY
/// registered descriptor — hosted services included (the boot re-registers them as plain singletons so they are validated but
/// never started) — and throws one <see cref="AggregateException"/> naming every descriptor whose constructor needs a service
/// that is not registered. That is exactly the <c>e3a15db91</c> failure (<c>IActionSeam</c> registered inside the DocIntel/Analysis
/// compound gate, its unconditional consumer <c>CommunicationRiActionService</c> not) detected before Azure does it at
/// <c>Host.StartAsync</c>.</para>
///
/// <para><b>Why it complements <see cref="AsymmetricRegistrationTests"/>.</b> The lexical scan sees registrations it can parse
/// (<c>AddX&lt;T&gt;</c>, <c>typeof</c> forms) and conditions it can read; it cannot see a service registered by
/// <c>AddChatClient</c> or an untyped factory (<c>SessionSummarizationService</c> → <c>IChatClient</c>,
/// <c>FilesIndexIngestDocumentSource</c> → <c>SearchIndexClient</c> were found ONLY here). This oracle sees the real container but
/// only for the gate combinations it boots; the scan sees every branch. Neither subsumes the other.</para>
///
/// <para><b>Result today.</b> Seven of the nine combinations fail: the BFF is not constructible with
/// <c>DocumentIntelligence:Enabled=false</c> (the greenfield stamp default), nor with <c>Analysis:Enabled=false</c>, nor with
/// DocIntel on but no AI Search endpoint. Those are CONFIRMED defects, listed in <see cref="KnownDefects"/> with their punch-list
/// ids — not exemptions. The test is a <b>ratchet</b>: a NEW unconstructible service fails it, and so does a ledgered defect that no
/// longer reproduces (delete the entry when you fix it).</para>
///
/// <para><b>Not covered (known limits):</b> only <c>Development</c> is booted (a <c>Production</c> boot needs the Redis handshake
/// listener and 3 Production-only validators — add a Production row here if a Production-only registration branch ever matters);
/// only gate combinations of the four flags below; only constructibility, not that the constructed service behaves. A
/// registration that is satisfiable but resolves to a Null-Object is by design (ADR-032 P2/P3).</para>
///
/// <para><b>KEEP path</b> (ADR-038 §7 / Amendment A1, eighth path): structural fitness function. <b>Maintenance:</b> new failure →
/// apply ADR-032 (P1 promote / P2-P3 Null-Object / gate the consumer on the same flag) in the module, do not add a ledger line;
/// a ledger line is only for a defect that is filed and not yet fixed (<see cref="DefectNotes"/> carries the row).</para>
/// </summary>
public class GateCombinationConstructibilityTests
{
    // ───────────────────────────────────── ledger ─────────────────────────────────────

    /// <summary>One descriptor that cannot be built, under the combinations where it cannot.</summary>
    internal sealed record Defect(string Id, string Impl, string Missing, Func<Combo, bool> Applies);

    internal sealed record Combo(bool DocIntel, bool Analysis, bool RecordMatching, bool AiSearchEndpoint)
    {
        public string Name => $"DocIntel={DocIntel} Analysis={Analysis} RecordMatching={RecordMatching} AiSearchEndpoint={(AiSearchEndpoint ? "set" : "empty")}";
    }

    private static bool AiOff(Combo c) => !(c.DocIntel && c.Analysis);

    /// <summary>
    /// Confirmed, filed, not yet fixed. Every row is a service the BFF cannot construct in a configuration a stamp can be in —
    /// the same class as <c>e3a15db91</c>. Fix per ADR-032 and DELETE the row; the test fails if a row stops reproducing.
    /// </summary>
    internal static readonly IReadOnlyList<Defect> KnownDefects = new[]
    {
        // DEF-FIN — Finance job handlers registered outside the gate of the services they inject (FinanceModule).
        new Defect("DEF-FIN", "InvoiceExtractionJobHandler", "IInvoiceAnalysisService", c => !c.DocIntel),
        new Defect("DEF-FIN", "AttachmentClassificationJobHandler", "IInvoiceAnalysisService", c => !c.DocIntel && c.RecordMatching),
        new Defect("DEF-FIN", "InvoiceSearchService", "SearchIndexClient", c => c.DocIntel && !c.Analysis),
        new Defect("DEF-FIN", "InvoiceIndexingJobHandler", "SearchIndexClient", c => c.DocIntel && !c.Analysis),

        // DEF-JOBS — AI job handlers and the embedding-migration hosted service registered unconditionally (JobProcessingModule).
        new Defect("DEF-JOBS", "AppOnlyDocumentAnalysisJobHandler", "IAppOnlyAnalysisService", AiOff),
        new Defect("DEF-JOBS", "EmailAnalysisJobHandler", "IAppOnlyAnalysisService", AiOff),
        new Defect("DEF-JOBS", "ProfileSummaryJobHandler", "IAppOnlyAnalysisService", c => c.DocIntel && !c.Analysis),
        new Defect("DEF-JOBS", "EmbeddingMigrationService", "IKnowledgeDeploymentService", c => AiOff(c) || !c.AiSearchEndpoint),

        // DEF-INS — Insights/session services that need AI Search or the chat client but are registered unconditionally.
        new Defect("DEF-INS", "FilesIndexIngestDocumentSource", "SearchIndexClient", c => AiOff(c) || !c.AiSearchEndpoint),
        new Defect("DEF-INS", "ObservationIndexUpserter", "SearchIndexClient", c => AiOff(c) || !c.AiSearchEndpoint),
        new Defect("DEF-INS", "ObservationEmitterNodeExecutor", "SearchIndexClient", c => AiOff(c) || !c.AiSearchEndpoint),
        new Defect("DEF-INS", "PrecedentProjectionSync", "SearchIndexClient", c => AiOff(c) || !c.AiSearchEndpoint),
        new Defect("DEF-INS", "SessionSummarizationService", "IChatClient", AiOff),

        // DEF-RAG — with DocIntel and Analysis ON but DocumentIntelligence:AiSearchEndpoint empty, SearchIndexClient,
        // IKnowledgeDeploymentService and IEmbeddingCache are not registered (AnalysisServicesModule.AddRagServices) while
        // everything on the wider analysis && docIntel gate that injects them is — ~35 services (the container reports only a
        // descriptor's FIRST missing parameter, so these are ledgered by the missing service: "*" matches any consumer).
        // Only IRagService, IFileIndexingService and IVisualizationService get a Null peer.
        new Defect("DEF-RAG", "*", "SearchIndexClient", c => c.DocIntel && c.Analysis && !c.AiSearchEndpoint),
        new Defect("DEF-RAG", "*", "IKnowledgeDeploymentService", c => c.DocIntel && c.Analysis && !c.AiSearchEndpoint),
        new Defect("DEF-RAG", "*", "IEmbeddingCache", c => c.DocIntel && c.Analysis && !c.AiSearchEndpoint),
    };

    private static bool Matches(Defect d, string failureKey)
        => d.Impl == "*"
            ? failureKey.EndsWith($" <- {d.Missing}", StringComparison.Ordinal)
            : failureKey == $"{d.Impl} <- {d.Missing}";

    /// <summary>Defect id → what is wrong and the filed row. The fix is ADR-032 per service; none was applied by task 204e (see the task report).</summary>
    internal static readonly IReadOnlyDictionary<string, string> DefectNotes = new Dictionary<string, string>
    {
        ["DEF-FIN"] = "task-202 punch list row 204e-F1. FinanceModule: InvoiceExtractionJobHandler is registered unconditionally but injects IInvoiceAnalysisService (registered only under DocumentIntelligence:Enabled); AttachmentClassificationJobHandler is gated on RecordMatchingEnabled alone; InvoiceIndexingJobHandler/InvoiceSearchService are gated on DocIntel but need SearchIndexClient (analysis && docIntel && AiSearchEndpoint). IJobHandler enumeration (ServiceBusJobProcessor) then throws on the first job.",
        ["DEF-JOBS"] = "task-202 punch list row 204e-F2. JobProcessingModule: AppOnlyDocumentAnalysisJobHandler/EmailAnalysisJobHandler are registered unconditionally and ProfileSummaryJobHandler under DocIntel only, but IAppOnlyAnalysisService exists only under analysis && docIntel; EmbeddingMigrationService (a hosted service, so it crashes Host.StartAsync) injects IKnowledgeDeploymentService/IOpenAiClient.",
        ["DEF-INS"] = "task-202 punch list row 204e-F3. InsightsIngestModule/InsightsModule/session services inject SearchIndexClient (registered only under analysis && docIntel && AiSearchEndpoint) or IChatClient (AiModule, same gate) without a Null peer.",
        ["DEF-RAG"] = "task-202 punch list row 204e-F4. AddRagServices registers SearchIndexClient/IKnowledgeDeploymentService/IEmbeddingCache only when DocumentIntelligence:AiSearchEndpoint is non-empty and gives only IRagService/IFileIndexingService/IVisualizationService a Null peer; ~35 services on the wider analysis && docIntel gate (SemanticSearch, RecordSearch, PinnedContextRecall, ReferenceRetrieval, ActionRunner, the *Ai facades, ComposeService, OfficeService…) inject them, so a stamp with DocIntel on and AI Search unset cannot start.",
    };

    public static IEnumerable<object[]> Combos()
    {
        foreach (var di in new[] { true, false })
        foreach (var an in new[] { true, false })
        foreach (var rm in new[] { true, false })
            yield return new object[] { di, an, rm, true };

        // DocIntel and Analysis on, AI Search endpoint empty (RecordMatching must stay off: its validator demands the endpoint).
        yield return new object[] { true, true, false, false };
    }

    // ───────────────────────────────────── the oracle ─────────────────────────────────────

    private static readonly Regex DescriptorFailure = new(
        @"service descriptor 'ServiceType: (?<svc>\S+) Lifetime: \w+ (?:ImplementationType|ImplementationFactory|ImplementationInstance): (?<impl>[^':]*)': Unable to resolve service for type '(?<missing>[^']+)'",
        RegexOptions.Compiled);

    private static string Short(string typeName)
    {
        var t = typeName.Trim();
        var lt = t.IndexOf('<');
        if (lt >= 0) t = t[..lt];
        var tick = t.IndexOf('`');
        if (tick >= 0) t = t[..tick];
        var cut = Math.Max(t.LastIndexOf('.'), t.LastIndexOf('+')); // namespace separator or nested-type separator
        return cut < 0 ? t : t[(cut + 1)..];
    }

    /// <summary>The "Impl &lt;- Missing" keys an <c>AggregateException</c> from a <c>ValidateOnBuild</c> boot reports.</summary>
    internal static HashSet<string> FailuresOf(Exception e)
    {
        var messages = e is AggregateException ag ? ag.InnerExceptions.Select(x => x.Message) : new[] { e.Message };
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var message in messages)
        {
            var m = DescriptorFailure.Match(message);
            keys.Add(m.Success
                ? $"{Short(m.Groups["impl"].Value.Length > 0 ? m.Groups["impl"].Value : m.Groups["svc"].Value)} <- {Short(m.Groups["missing"].Value)}"
                : "UNPARSED: " + message);
        }

        return keys;
    }

    private static HashSet<string> Boot(Combo c)
    {
        var settings = new Dictionary<string, string?>
        {
            ["DocumentIntelligence:Enabled"] = c.DocIntel ? "true" : "false",
            ["Analysis:Enabled"] = c.Analysis ? "true" : "false",
            ["DocumentIntelligence:RecordMatchingEnabled"] = c.RecordMatching ? "true" : "false",
        };
        if (!c.AiSearchEndpoint)
        {
            settings["DocumentIntelligence:AiSearchEndpoint"] = string.Empty;
            settings["DocumentIntelligence:AiSearchKey"] = string.Empty;
        }

        try
        {
            using var app = new BootedApp(Environments.Development, settings, validateOnBuild: true);
            return new HashSet<string>(StringComparer.Ordinal);
        }
        catch (AggregateException e)
        {
            return FailuresOf(e);
        }
    }

    [Theory(DisplayName = "ADR-032 runtime: every registration is constructible under every feature-gate combination (ratchet against the ledger)")]
    [MemberData(nameof(Combos))]
    public void EveryRegistrationIsConstructibleUnderEveryGateCombination(bool docIntel, bool analysis, bool recordMatching, bool aiSearchEndpoint)
    {
        var combo = new Combo(docIntel, analysis, recordMatching, aiSearchEndpoint);
        var actual = Boot(combo);
        var applicable = KnownDefects.Where(d => d.Applies(combo)).ToList();

        var unexpected = actual.Where(k => !applicable.Any(d => Matches(d, k))).OrderBy(k => k).ToList();
        var stale = applicable.Where(d => !actual.Any(k => Matches(d, k))).Select(d => $"{d.Impl} <- {d.Missing}").OrderBy(k => k).ToList();
        var expected = applicable.ToDictionary(d => $"{d.Impl} <- {d.Missing}", d => d.Id);

        Assert.True(unexpected.Count == 0 && stale.Count == 0,
            $"[{combo.Name}]\n" +
            (unexpected.Count == 0 ? "" :
                "  NEW unconstructible registrations (the e3a15db91 class — a service registered on a path where its dependency is not). Fix per ADR-032 (P1 promote / P2-P3 Null-Object / gate the consumer on the SAME flag); do not add a ledger line:\n" +
                string.Join("\n", unexpected.Select(k => "    - " + k)) + "\n") +
            (stale.Count == 0 ? "" :
                "  Ledgered defects that no longer reproduce here — delete them from GateCombinationConstructibilityTests.KnownDefects (or fix their Applies predicate):\n" +
                string.Join("\n", stale.Select(k => $"    - {k} ({expected[k]})")) + "\n"));
    }

    [Fact(DisplayName = "ADR-032 runtime: every ledgered defect id has a written note and the all-gates-on boot has none (harness cannot false-flag)")]
    public void LedgerIsDocumentedAndTheAllOnBootIsClean()
    {
        Assert.All(KnownDefects, d => Assert.True(DefectNotes.ContainsKey(d.Id), $"{d.Impl}: defect id {d.Id} has no entry in DefectNotes"));
        Assert.All(DefectNotes, kv => Assert.True(kv.Value.Length > 60 && kv.Value.Contains("punch list", StringComparison.Ordinal), $"{kv.Key}: note must say what is wrong and cite its punch-list row"));
        Assert.All(KnownDefects, d => Assert.False(d.Applies(new Combo(true, true, true, true)) || d.Applies(new Combo(true, true, false, true)),
            $"{d.Impl} <- {d.Missing} is ledgered for an all-gates-on combination; that boot must be clean"));
        Assert.Empty(Boot(new Combo(true, true, true, true)));
    }

    // ───────────────────────────────────── controls: the oracle itself ─────────────────────────────────────

    public interface IRtDep { }
    public sealed class RtDep : IRtDep { }
    public sealed class RtNullDep : IRtDep { }
    public sealed class RtConsumer
    {
        public RtConsumer(IRtDep dep) { }
    }

    private static HashSet<string> Validate(Action<IServiceCollection> register)
    {
        var services = new ServiceCollection();
        register(services);
        try
        {
            services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true }).Dispose();
            return new HashSet<string>();
        }
        catch (AggregateException e)
        {
            return FailuresOf(e);
        }
    }

    [Fact(DisplayName = "control (negative): the e3a15db91 shape — unconditional consumer, dependency behind a flag that is off — is reported by name")]
    public void Control_Negative_AsymmetricRegistrationIsReported()
    {
        var flagOff = bool.Parse("false"); // not a constant, so the compiler does not fold the branch
        var failures = Validate(s =>
        {
            s.AddSingleton<RtConsumer>();
            if (flagOff) s.AddSingleton<IRtDep, RtDep>();
        });

        Assert.Equal(new[] { "RtConsumer <- IRtDep" }, failures.ToArray());
    }

    [Fact(DisplayName = "control (positive): hoisted, and real-or-Null-Object, registrations are both constructible")]
    public void Control_Positive_SymmetricRegistrationsAreClean()
    {
        var flagOff = bool.Parse("false");
        Assert.Empty(Validate(s => { s.AddSingleton<RtConsumer>(); s.AddSingleton<IRtDep, RtDep>(); }));
        Assert.Empty(Validate(s =>
        {
            s.AddSingleton<RtConsumer>();
            if (flagOff) s.AddSingleton<IRtDep, RtDep>(); else s.AddSingleton<IRtDep, RtNullDep>();
        }));
    }

    // Hosted services: the real boot removes every IHostedService (they would connect out) and re-registers each hosted TYPE as a
    // plain singleton so it is still validated. That is proven by the ledger itself — EmbeddingMigrationService is a hosted service,
    // and its row goes stale (failing this theory) the moment the re-registration stops reaching it.
}
