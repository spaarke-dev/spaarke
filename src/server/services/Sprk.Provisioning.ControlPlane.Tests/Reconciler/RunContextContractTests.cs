// -----------------------------------------------------------------------------
// RunContextContractTests.cs
//
// Task 245a (G25) — the run-context contract's forcing function.
//
// INVARIANT: every value a provisioning handler reads has exactly one declared
// source, and that source exists before the handler runs:
//   - an Intake key must be an accepted IntakeParameterCatalog key (the only
//     thing in run.Parameters.NonSecret);
//   - a REQUIRED Output (typed InterStepState property) must be produced by a
//     handler that is a strict transitive ancestor of the reader in
//     DagAdvancer.HandlerDependencies;
//   - a handler's source reads nothing it has not declared in
//     Reconciler/HandlerRunInputs.cs, and writes only the InterStepState
//     properties it is the [ProducedBy] producer of.
//
// WHY: until task 245a about twenty required handler inputs had no producer —
// handlers read them from run parameters nothing wrote, and each handler's unit
// tests seeded the values by hand, so every handler passed alone while a real
// run could not get past H0 (notes/run-context-dataflow-gap.md). No test asked
// "who writes this?". This one does.
//
// SOURCE SCAN (rule e): regex over each handler folder's .cs files under
// Sprk.Provisioning.ControlPlane.Core/Handlers, comments stripped. It sees
// `InterStepState.{Property}` (reads; `= ` after it = a write), the same
// through an alias — a local bound to it (`var s = run.InterStepState;`) or a
// parameter typed as it (`Foo(InterStepState s)`) — plus `const string
// *ParameterKey = "..."` (intake keys) and `IntakeParameterCatalog.{Member}` (every
// catalog constant naming an accepted key, found by reflection).
// LIMITATION: state handed whole to another type (`helper.Use(run.InterStepState)`
// where the helper lives outside the folder) or read by reflection is not seen.
//
// MAINTENANCE:
//   - New handler input → declare it in HandlerRunInputs with its real source.
//     A failure here means the data flow is wrong; fix the flow, not the table.
//   - New handler folder → add it to FolderToHandler below.
//   - The KnownGaps list is empty since T245c (G25 closed); a new entry needs an owner-visible
//     reason in the owning task's notes.
//
// Fitness-function rules per tests/CLAUDE.md: each rule (a)–(e) and (g) runs
// through a checker that takes its tables as arguments, and has a negative
// control (a seeded violation is reported) and a positive control (the
// sanctioned shape is not). Rule (f) is a pinned-list equality, not a detector.
// PLACEMENT: this structural test lives in the L2 test project, not
// tests/Spaarke.ArchTests — ArchTests deliberately has no reference to the L2
// projects (Spaarke.ArchTests.csproj). spec.md ADR Tensions records this
// (ADR-038, Path A) and treats it as a KEEP fitness function.
// -----------------------------------------------------------------------------

using System.Reflection;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Sprk.Provisioning.ControlPlane.Handlers;
using Sprk.Provisioning.ControlPlane.Handlers.EntraAppReg;
using Sprk.Provisioning.ControlPlane.Handlers.KvSecretsPopulation;
using Sprk.Provisioning.ControlPlane.Models;
using Sprk.Provisioning.ControlPlane.Reconciler;
using Xunit;

namespace Sprk.Provisioning.ControlPlane.Tests.Reconciler;

public sealed class RunContextContractTests
{
    /// <summary>Handler folder under Core/Handlers → the handler it implements (null = shared helpers, must read nothing).</summary>
    private static readonly IReadOnlyDictionary<string, string?> FolderToHandler = new Dictionary<string, string?>(StringComparer.Ordinal)
    {
        ["Preflight"] = HandlerIds.H0,
        ["ConsentCapture"] = HandlerIds.H05,
        ["SubscriptionReadiness"] = HandlerIds.H1,
        ["BicepInfraDeploy"] = HandlerIds.H2a,
        ["AiSearchIndex"] = HandlerIds.H2b,
        ["EntraAppReg"] = HandlerIds.H3,
        ["KvSecretsPopulation"] = HandlerIds.H4,
        ["BulkAppSettings"] = HandlerIds.H4b,
        ["DataverseEnvCreation"] = HandlerIds.H5,
        ["SolutionImport"] = HandlerIds.H6,
        ["EnvVarValues"] = HandlerIds.H7,
        ["SecureRecordSetup"] = HandlerIds.H7b,
        ["SpeContainer"] = HandlerIds.H8,
        ["BffDeploy"] = HandlerIds.H9,
        ["DataverseAppUserGraphParity"] = HandlerIds.H10,
        ["UserProvisioning"] = HandlerIds.H11,
        ["AiSeedChain"] = HandlerIds.H12a,
        ["AppConfigSeed"] = HandlerIds.H12b,
        ["RuntimeReferences"] = HandlerIds.H12c,
        ["E2EAcceptance"] = HandlerIds.H13,
        ["IntegrationWiring"] = HandlerIds.H14,
        ["Credentials"] = null,
    };

    /// <summary>
    /// The pinned known gaps — inputs still read from intake whose correct source a named task owes.
    /// Removing an entry (the gap is fixed) or adding one is a deliberate edit here. These are tracked
    /// defects, not ADR exemptions: the record is projects/customer-provisioning-orchestration-r1/
    /// notes/run-context-dataflow-gap.md (G25) §5, and each owning task's POML.
    /// </summary>
    private static readonly string[] PinnedKnownGaps = [];   // T245c made the last five (H11 / H14) required intake

    // ------------------------------------------------------------------ (a)

    [Fact]
    public void InterStepStateProperties_EachDeclareExactlyOneProducerOrNone_WithARealHandlerId()
        => FindProducerAnnotationProblems(typeof(InterStepState), AllHandlerIds()).Should().BeEmpty();

    [Fact]
    public void ProducerAnnotationChecker_ReportsMissingDoubleAndUnknownAnnotations_NegativeControl()
    {
        var problems = FindProducerAnnotationProblems(typeof(SeededBadAnnotations), AllHandlerIds());

        problems.Should().Contain(p => p.StartsWith(nameof(SeededBadAnnotations.Unannotated) + ":", StringComparison.Ordinal));
        problems.Should().Contain(p => p.StartsWith(nameof(SeededBadAnnotations.BothAnnotations) + ":", StringComparison.Ordinal));
        problems.Should().Contain(p => p.StartsWith(nameof(SeededBadAnnotations.UnknownProducer) + ":", StringComparison.Ordinal)
            && p.Contains("not a HandlerIds constant"));
    }

    [Fact]
    public void ProducerAnnotationChecker_AcceptsExactlyOneRealAnnotation_PositiveControl()
        => FindProducerAnnotationProblems(typeof(SeededGoodAnnotations), AllHandlerIds()).Should().BeEmpty();

    // ------------------------------------------------------------------ (b)

    [Fact]
    public void RequiredOutputInputs_AreProducedByAStrictDagAncestorOfTheReader()
        => FindUnreachableRequiredOutputs(HandlerRunInputs.ByHandler, ProducerOf, DagAdvancer.HandlerDependencies)
            .Should().BeEmpty();

    [Fact]
    public void ReachabilityChecker_ReportsLateAndMissingProducers_NegativeControl()
    {
        var inputs = new Dictionary<string, IReadOnlyList<RunInput>>(StringComparer.Ordinal)
        {
            [HandlerIds.H4] = [RunInput.Output("Late")],     // produced by a handler that runs AFTER H4
            [HandlerIds.H9] = [RunInput.Output("Orphan")],   // produced by nobody
        };
        var producers = new Dictionary<string, string>(StringComparer.Ordinal) { ["Late"] = HandlerIds.H13 };
        var dag = new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            [HandlerIds.H4] = [HandlerIds.H2a],
            [HandlerIds.H13] = [HandlerIds.H4],
        };

        var problems = FindUnreachableRequiredOutputs(inputs, p => producers.GetValueOrDefault(p), dag);

        problems.Should().Contain(p => p.Contains("InterStepState.Late") && p.Contains("not a DAG ancestor"));
        problems.Should().Contain(p => p.Contains("InterStepState.Orphan") && p.Contains("no [ProducedBy] producer"));
    }

    [Fact]
    public void ReachabilityChecker_AcceptsTransitiveAncestorsAndIgnoresOptionalReads_PositiveControl()
    {
        var inputs = new Dictionary<string, IReadOnlyList<RunInput>>(StringComparer.Ordinal)
        {
            // H9 ← H4 ← H2a: a producer two edges up is still an ancestor.
            [HandlerIds.H9] = [RunInput.Output("Early"), RunInput.Output("Late", required: false)],
        };
        var producers = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Early"] = HandlerIds.H2a,
            ["Late"] = HandlerIds.H13,
        };
        var dag = new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            [HandlerIds.H9] = [HandlerIds.H4],
            [HandlerIds.H4] = [HandlerIds.H2a],
        };

        FindUnreachableRequiredOutputs(inputs, p => producers.GetValueOrDefault(p), dag).Should().BeEmpty();
    }

    // ------------------------------------------------------------------ (c)

    [Fact]
    public void IntakeAndGapInputs_AreAcceptedIntakeKeys()
        => FindUnknownIntakeInputs(HandlerRunInputs.ByHandler, IntakeParameterCatalog.IsKnown).Should().BeEmpty();

    [Fact]
    public void IntakeKeyChecker_ReportsUnknownIntakeAndGapKeys_NegativeControl()
    {
        var inputs = new Dictionary<string, IReadOnlyList<RunInput>>(StringComparer.Ordinal)
        {
            [HandlerIds.H0] = [RunInput.Intake("tenant_id"), RunInput.Gap("bogusGapKey", "T999")],
        };

        var problems = FindUnknownIntakeInputs(inputs, IntakeParameterCatalog.IsKnown);

        problems.Should().Contain(p => p.Contains("'tenant_id'"));
        problems.Should().Contain(p => p.Contains("'bogusGapKey'"));
    }

    [Fact]
    public void IntakeKeyChecker_AcceptsCatalogKeysAndIgnoresOutputs_PositiveControl()
    {
        var inputs = new Dictionary<string, IReadOnlyList<RunInput>>(StringComparer.Ordinal)
        {
            [HandlerIds.H0] =
            [
                RunInput.Intake(IntakeParameterCatalog.TenantId),
                RunInput.Gap("identityPreset", "T999"),
                RunInput.Output(nameof(InterStepState.KeyVaultName)),   // an Output is not an intake key
            ],
        };

        FindUnknownIntakeInputs(inputs, IntakeParameterCatalog.IsKnown).Should().BeEmpty();
    }

    // ------------------------------------------------------------------ (d)

    [Fact]
    public void NoProducerProperties_AreNeverRequiredInputs()
        => FindRequiredUnproducedOutputs(HandlerRunInputs.ByHandler, NoProducerPropertyNames(typeof(InterStepState)))
            .Should().BeEmpty();

    [Fact]
    public void UnproducedChecker_ReportsARequiredReadOfANoProducerProperty_NegativeControl()
    {
        var inputs = new Dictionary<string, IReadOnlyList<RunInput>>(StringComparer.Ordinal)
        {
            [HandlerIds.H8] = [RunInput.Output(nameof(InterStepState.S2SAppRegId))],
        };

        FindRequiredUnproducedOutputs(inputs, NoProducerPropertyNames(typeof(InterStepState)))
            .Should().ContainSingle(p => p.Contains("InterStepState.S2SAppRegId"));
    }

    [Fact]
    public void UnproducedChecker_AcceptsAnOptionalReadOfANoProducerProperty_PositiveControl()
    {
        // H3's S2S-forbidden guard reads S2SAppRegId expecting it EMPTY — an optional read is sanctioned.
        var inputs = new Dictionary<string, IReadOnlyList<RunInput>>(StringComparer.Ordinal)
        {
            [HandlerIds.H3] = [RunInput.Output(nameof(InterStepState.S2SAppRegId), required: false)],
        };

        FindRequiredUnproducedOutputs(inputs, NoProducerPropertyNames(typeof(InterStepState))).Should().BeEmpty();
    }

    // ------------------------------------------------------------------ (e)

    [Fact]
    public void HandlerSources_ReadOnlyDeclaredInputs_AndWriteOnlyTheirOwnOutputs()
    {
        var handlersRoot = Path.Combine(RepoRoot(), "src", "server", "services",
            "Sprk.Provisioning.ControlPlane.Core", "Handlers");
        var problems = new List<string>();
        var writesByHandler = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);

        foreach (var folder in Directory.GetDirectories(handlersRoot).Select(Path.GetFileName))
        {
            if (!FolderToHandler.TryGetValue(folder!, out var handler))
            {
                problems.Add($"Handlers/{folder}: not in FolderToHandler — map it so its inputs are checked");
                continue;
            }
            // Scanned per FILE and unioned, so one file's comment-parsing state can never leak into
            // the next file.
            var fileScans = Directory.GetFiles(Path.Combine(handlersRoot, folder!), "*.cs")
                .Select(f => Scan(File.ReadAllText(f)))
                .ToList();
            var scan = new SourceScan(
                fileScans.SelectMany(s => s.InterStepStateReferences).ToHashSet(StringComparer.Ordinal),
                fileScans.SelectMany(s => s.InterStepStateWrites).ToHashSet(StringComparer.Ordinal),
                fileScans.SelectMany(s => s.IntakeKeys).ToHashSet(StringComparer.Ordinal),
                ReadsRunSecrets: fileScans.Any(s => s.ReadsRunSecrets),
                WritesRunSecrets: fileScans.Any(s => s.WritesRunSecrets));
            problems.AddRange(FindViolations(folder!, handler, scan));
            if (handler is not null)
            {
                writesByHandler[handler] = scan.InterStepStateWrites.ToHashSet(StringComparer.Ordinal);
                problems.AddRange(FindUnreadDeclarations(folder!, handler, scan,
                    HandlerRunInputs.ByHandler.TryGetValue(handler, out var declared) ? declared : Array.Empty<RunInput>()));
            }
        }

        // Producer truthfulness (the other direction): every [ProducedBy(X)] property is actually
        // written somewhere in X's folder. A false annotation would make rule (b) prove nothing —
        // and a scan that silently found no source would fail here.
        foreach (var property in typeof(InterStepState).GetProperties())
        {
            var producer = property.GetCustomAttribute<ProducedByAttribute>()?.HandlerId;
            if (producer is null) continue;
            if (!writesByHandler.TryGetValue(producer, out var writes) || !writes.Contains(property.Name))
            {
                problems.Add($"InterStepState.{property.Name} is [ProducedBy({producer})] but {producer}'s folder never writes it");
            }
        }
        problems.Should().BeEmpty();
    }

    [Fact]
    public void Checker_ReportsSeededUndeclaredReadsAndForeignWrites_NegativeControl()
    {
        // Seeded violations in an H9-shaped source: an undeclared InterStepState read, an
        // undeclared intake key, and a write of another handler's output.
        const string seeded = """
            public const string BogusParameterKey = "keyVaultUri";
            var x = run.InterStepState.CosmosEndpoint;
            run.InterStepState.KeyVaultName = "x";
            """;

        var violations = FindViolations("BffDeploy", HandlerIds.H9, Scan(seeded));

        violations.Should().Contain(v => v.Contains("InterStepState.CosmosEndpoint") && v.Contains("not declared"));
        violations.Should().Contain(v => v.Contains("'keyVaultUri'") && v.Contains("not declared"));
        violations.Should().Contain(v => v.Contains("writes InterStepState.KeyVaultName"));
    }

    [Fact]
    public void Checker_AcceptsDeclaredReadsOwnWritesAndIgnoresComments_PositiveControl()
    {
        const string sanctioned = """
            // InterStepState.CosmosEndpoint is mentioned in a comment only — not a read.
            /// <see cref="InterStepState.KeyVaultUri"/> — doc comment.
            /* block mention of InterStepState.MiClientId */
            public const string TenantIdParameterKey = "tenantId";
            var rg = run.InterStepState.ResourceGroupName;           // declared H9 input
            var url = "https://x"; // trailing comment InterStepState.CosmosEndpoint
            """;
        FindViolations("BffDeploy", HandlerIds.H9, Scan(sanctioned)).Should().BeEmpty();

        const string ownWrite = "run.InterStepState.DataverseEnvUrl = url; var u = run.InterStepState.DataverseEnvUrl;";
        FindViolations("DataverseEnvCreation", HandlerIds.H5, Scan(ownWrite)).Should().BeEmpty(
            "a handler may write — and re-read — the property it produces");
    }

    [Fact]
    public void Checker_ReportsReadsAndWritesThroughAnAlias_NegativeControl()
    {
        // H4, H7, H9, H10, H13 and H14 read through a local alias; a scan blind to aliases would let
        // any of them read an undeclared value unnoticed.
        const string seeded = """
            var interStep = run.InterStepState;
            var endpoint = interStep.CosmosEndpoint;
            interStep.KeyVaultName = "x";
            static bool Check(InterStepState s) => s.MiClientId is null;
            """;

        var scan = Scan(seeded);
        scan.InterStepStateReferences.Should().Contain(
            [nameof(InterStepState.CosmosEndpoint), nameof(InterStepState.KeyVaultName), nameof(InterStepState.MiClientId)]);
        scan.InterStepStateWrites.Should().Contain(nameof(InterStepState.KeyVaultName));

        var violations = FindViolations("BffDeploy", HandlerIds.H9, scan);
        violations.Should().Contain(v => v.Contains("InterStepState.CosmosEndpoint") && v.Contains("not declared"));
        violations.Should().Contain(v => v.Contains("writes InterStepState.KeyVaultName"));
    }

    [Fact]
    public void Scan_AliasMembersThatAreNotInterStepStateProperties_AreIgnored_PositiveControl()
    {
        const string source = """
            var state = run.InterStepState;
            var rg = state.ResourceGroupName;
            var other = state.NotAnInterStepStateProperty;
            """;

        Scan(source).InterStepStateReferences.Should().BeEquivalentTo([nameof(InterStepState.ResourceGroupName)]);
    }

    [Fact]
    public void Checker_ReportsRunSecretsWritesAndUnlistedReads_NegativeControl()
    {
        // run.Parameters.Secrets has no writer since task 245a; only H4 may still read it.
        const string seeded = """
            run.Parameters.Secrets["BFF-API-ClientId"] = reference;
            var secrets = run.Parameters?.Secrets;
            secrets!.Add("x", reference);
            var r = run.Parameters.Secrets.Count;
            """;

        var scan = Scan(seeded);
        scan.WritesRunSecrets.Should().BeTrue();
        scan.ReadsRunSecrets.Should().BeTrue();

        var violations = FindViolations("BffDeploy", HandlerIds.H9, scan);
        violations.Should().Contain(v => v.Contains("writes run.Parameters.Secrets"));
        violations.Should().Contain(v => v.Contains("reads run.Parameters.Secrets"));
    }

    [Fact]
    public void Checker_AcceptsTheListedRunSecretsReader_PositiveControl()
    {
        const string sanctioned = """
            var refs = new Dictionary<string, KeyVaultSecretRef>(run.Parameters.Secrets, StringComparer.Ordinal);
            var message = "No ref in run.Parameters.Secrets['x']";   // a string, not code
            """;

        var scan = Scan(sanctioned);
        scan.WritesRunSecrets.Should().BeFalse();
        FindViolations("KvSecretsPopulation", HandlerIds.H4, scan).Should().NotContain(v => v.Contains("Parameters.Secrets"));
    }

    [Fact]
    public void Scan_SeesLiteralIntakeKeysAndNullConditionalAccess_NegativeControl()
    {
        // Shapes a regex scan missed: literal keys beside the NonSecret map, `?.` / `!.` access.
        const string seeded = """
            var nonSecret = run.Parameters?.NonSecret ?? new Dictionary<string, string>();
            AddIfPresent(columns, nonSecret, "msalClientId", "sprk_bffversion");
            run.Parameters.NonSecret.TryGetValue("buildId", out var build);
            var region = run.Parameters.NonSecret["region"];
            var a = run.InterStepState?.CosmosEndpoint;
            var b = run.InterStepState!.MiClientId;
            """;

        var scan = Scan(seeded);

        scan.IntakeKeys.Should().BeEquivalentTo(["msalClientId", "buildId", "region"],
            "only literals that ARE intake keys count — the column name 'sprk_bffversion' does not");
        scan.InterStepStateReferences.Should().BeEquivalentTo(
            [nameof(InterStepState.CosmosEndpoint), nameof(InterStepState.MiClientId)]);
    }

    [Fact]
    public void Scan_IgnoresStringsRawStringsAndNameof_PositiveControl()
    {
        // Text inside literals and nameof(...) is not a read — the regex scan counted all three.
        const string sanctioned = """"
            var a = "InterStepState.ResourceGroupName is required";
            var b = @"InterStepState.KeyVaultName ""quoted""";
            var c = """
                /* InterStepState.CosmosEndpoint */
                """;
            var d = nameof(InterStepState.MiClientId);
            var e = run.InterStepState.KeyVaultUri;
            """";

        Scan(sanctioned).InterStepStateReferences.Should().BeEquivalentTo([nameof(InterStepState.KeyVaultUri)],
            "only the real member access on the last line is a read");
    }

    [Fact]
    public void DeclarationChecker_ReportsDeclaredInputsTheFolderNeverReads_NegativeControl()
    {
        RunInput[] declared =
        [
            RunInput.Output(nameof(InterStepState.CosmosEndpoint)),
            RunInput.Intake(IntakeParameterCatalog.TenantId),
            RunInput.Gap("usersJson", "T999"),
        ];

        var problems = FindUnreadDeclarations("BffDeploy", HandlerIds.H9, Scan("var x = 1;"), declared);

        problems.Should().HaveCount(3);
        problems.Should().Contain(p => p.Contains("InterStepState.CosmosEndpoint"));
        problems.Should().Contain(p => p.Contains("'tenantId'"));
        problems.Should().Contain(p => p.Contains("'usersJson'"));
    }

    [Fact]
    public void DeclarationChecker_AcceptsDeclaredInputsTheFolderReads_PositiveControl()
    {
        const string source = """
            public const string TenantIdParameterKey = "tenantId";
            var c = run.InterStepState.CosmosEndpoint;
            """;
        RunInput[] declared =
        [
            RunInput.Output(nameof(InterStepState.CosmosEndpoint)),
            RunInput.Intake(IntakeParameterCatalog.TenantId),
        ];

        FindUnreadDeclarations("BffDeploy", HandlerIds.H9, Scan(source), declared).Should().BeEmpty();
    }

    [Fact]
    public void Scan_SlashStarInsideALineComment_DoesNotHideTheCodeThatFollows()
    {
        // Regression for the scanner itself: "/*" inside a "//" comment (e.g. `stacks/*.bicep`) once
        // opened a phantom block comment that swallowed the rest of the file, so H2a's writes vanished.
        const string source = """
            // modules (`stacks/*.bicep`, `customer.bicep`) and any *.bicep in the folder
            run.InterStepState.KeyVaultName = outputs.KeyVaultName;
            """;

        Scan(source).InterStepStateWrites.Should().Contain(nameof(InterStepState.KeyVaultName));
    }

    // ------------------------------------------------------------------ (f)

    [Fact]
    public void KnownGaps_AreExactlyThePinnedList()
    {
        var actual = HandlerRunInputs.ByHandler
            .SelectMany(kv => kv.Value.Where(i => i.Source == RunInputSource.Gap)
                .Select(i => $"{kv.Key}:{i.Name}→{i.OwningTask}"))
            .ToList();

        actual.Should().BeEquivalentTo(PinnedKnownGaps,
            "the known-gap set changes only deliberately — a fixed gap is removed here, never added to make a test pass");
    }

    // ------------------------------------------------------------------ (g) H4's manifest-driven inputs

    /// <summary>
    /// Manifest secrets whose value_source still needs a KeyVaultSecretRef in run.Parameters.Secrets — a
    /// channel with NO writer since task 245a. Each is owned by the named task. Pinned like PinnedKnownGaps.
    /// </summary>
    private static readonly string[] PinnedManifestGaps =
    [
        // T245b: Dataverse-ServiceUrl moved to H4b per_env_settings (from-h5-output). T225b (owner D18): the
        // Spaarke-shared vendor keys (Bing Search, LlamaParse) left the catalog. T246 (plan G26): ContentSafety-ApiKey
        // left too — each stamp has its own keyless Content Safety account, reached with the UAMI.
        // T227c (G18): SPE-DefaultContainerId / SPE-CommunicationArchiveContainerId left the catalog — the container id is
        // a plain H4b setting from H8's output (from-h8-output:spe_container_id). No manifest gaps remain.
        // T225b (G21): Dataverse-ClientSecret / BFF-API-ClientSecret are no longer gaps — new stamps are secret-free
        // by default (KvSecretsPopulationOptions.RequireSecretFreeIdentity), so H4 omits both and never needs a value.
    ];

    [Fact]
    public async Task ManifestSecrets_EachHaveASourceH4CanReach_OrArePinnedGaps()
    {
        var manifest = new FileKvSecretManifest(
            Microsoft.Extensions.Logging.Abstractions.NullLogger<FileKvSecretManifest>.Instance,
            Microsoft.Extensions.Options.Options.Create(new KvSecretsPopulationOptions()));
        var read = await manifest.ReadAsync(CancellationToken.None);
        var entries = read.Should().BeOfType<KvSecretManifestReadResult.Success>().Subject.Entries;

        var (problems, gaps) = ClassifyManifestEntries(entries, H4KvSecretsPopulationHandler.IntakeValueParameterKeys,
            H3WrittenSecretNames, CustomerBicepKvSecretNames(), PinnedManifestGapOwners());

        problems.Should().BeEmpty();
        gaps.Should().BeEquivalentTo(PinnedManifestGaps,
            "a manifest secret with no producer H4 can reach — a Secrets ref nothing writes, or a from-bicep-output " +
            "value customer.bicep does not write — is a gap, and each gap is owned and pinned");
    }

    /// <summary>The secret names <c>customer.bicep</c>'s <c>kvSecretValues</c> map writes at H2a.</summary>
    private static IReadOnlySet<string> CustomerBicepKvSecretNames()
    {
        var bicep = File.ReadAllText(Path.Combine(RepoRoot(), "infrastructure", "bicep", "customer.bicep"));
        // The map closes on a line of its own; values may contain `${...}` interpolation braces.
        var block = System.Text.RegularExpressions.Regex.Match(bicep, @"var kvSecretValues = \{(?<body>.*?)^\}",
            System.Text.RegularExpressions.RegexOptions.Singleline | System.Text.RegularExpressions.RegexOptions.Multiline);
        block.Success.Should().BeTrue("customer.bicep must declare `var kvSecretValues = { ... }` (the H2a KV write set)");
        return System.Text.RegularExpressions.Regex.Matches(block.Groups["body"].Value, @"^\s*'([^']+)'\s*:", System.Text.RegularExpressions.RegexOptions.Multiline)
            .Select(m => m.Groups[1].Value)
            .ToHashSet(StringComparer.Ordinal);
    }

    [Fact]
    public void ManifestClassifier_ReportsUnmappedUnwrittenAndUnownedEntries_NegativeControl()
    {
        KvSecretEntry[] seeded =
        [
            new("Unmapped-Intake", KvSecretOperation.Upsert, KvSecretValueSource.FromIntakeParameter),
            new("Mapped-To-Unknown-Key", KvSecretOperation.Upsert, KvSecretValueSource.FromIntakeParameter),
            new("Not-Written-By-H3", KvSecretOperation.Upsert, KvSecretValueSource.WrittenByEntraAppReg),
            new("Needs-A-Secrets-Ref", KvSecretOperation.Upsert, KvSecretValueSource.FromRunParameters),
        ];
        var intakeMap = new Dictionary<string, string>(StringComparer.Ordinal) { ["Mapped-To-Unknown-Key"] = "tenant_id" };
        var bicepWrites = new HashSet<string>(StringComparer.Ordinal) { "Written-By-Bicep-But-Not-Labelled" };

        var (problems, gaps) = ClassifyManifestEntries(
            seeded.Append(new KvSecretEntry("Labelled-Bicep-Not-Written", KvSecretOperation.Upsert, KvSecretValueSource.FromBicepOutput)),
            intakeMap, H3WrittenSecretNames, bicepWrites, PinnedManifestGapOwners());

        problems.Should().Contain(p => p.StartsWith("Unmapped-Intake:", StringComparison.Ordinal));
        problems.Should().Contain(p => p.StartsWith("Mapped-To-Unknown-Key:", StringComparison.Ordinal));
        problems.Should().Contain(p => p.StartsWith("Not-Written-By-H3:", StringComparison.Ordinal));
        problems.Should().Contain(p => p.StartsWith("Written-By-Bicep-But-Not-Labelled:", StringComparison.Ordinal),
            "customer.bicep writing a secret the manifest does not label from-bicep-output is drift");
        gaps.Should().Contain("Needs-A-Secrets-Ref→UNOWNED", "an unpinned gap surfaces as UNOWNED and fails the pinned-list check");
        gaps.Should().Contain("Labelled-Bicep-Not-Written→UNOWNED", "a from-bicep-output label customer.bicep does not back is a gap");
    }

    [Fact]
    public void ManifestClassifier_AcceptsEveryReachableSourceShape_PositiveControl()
    {
        KvSecretEntry[] sanctioned =
        [
            new("From-Bicep", KvSecretOperation.Upsert, KvSecretValueSource.FromBicepOutput),
            new("Generated-One", KvSecretOperation.Upsert, KvSecretValueSource.Generated),
            new("TenantId", KvSecretOperation.Upsert, KvSecretValueSource.FromIntakeParameter),
            new(GraphAppRegistrationProvisioner.ClientIdSecretName, KvSecretOperation.Upsert, KvSecretValueSource.WrittenByEntraAppReg),
            new("Pinned-Gap", KvSecretOperation.Upsert, KvSecretValueSource.FromBicepOutput),   // a pinned, owned gap
        ];
        var intakeMap = new Dictionary<string, string>(StringComparer.Ordinal) { ["TenantId"] = IntakeParameterCatalog.TenantId };
        var bicepWrites = new HashSet<string>(StringComparer.Ordinal) { "From-Bicep" };
        var pinnedOwners = new Dictionary<string, string>(StringComparer.Ordinal) { ["Pinned-Gap"] = "T999" };

        var (problems, gaps) = ClassifyManifestEntries(sanctioned, intakeMap, H3WrittenSecretNames, bicepWrites, pinnedOwners);

        problems.Should().BeEmpty();
        gaps.Should().BeEquivalentTo(["Pinned-Gap→T999"]);
    }

    /// <summary>The KV secrets H3's provisioner commits itself; H4 skips their <c>written-by-h3</c> entries.</summary>
    private static readonly IReadOnlySet<string> H3WrittenSecretNames = new HashSet<string>(StringComparer.Ordinal)
    {
        GraphAppRegistrationProvisioner.ClientIdSecretName,
        GraphAppRegistrationProvisioner.AudienceSecretName,
    };

    private static Dictionary<string, string> PinnedManifestGapOwners()
        => PinnedManifestGaps.ToDictionary(g => g.Split('→')[0], g => g.Split('→')[1], StringComparer.Ordinal);

    /// <summary>
    /// Rule (g). H4 runs after H2a (Bicep) and before H3. A secret is reachable when customer.bicep
    /// actually writes it (from-bicep-output AND in <paramref name="bicepWrittenNames"/>), H4 generates
    /// it, H4 takes it from an intake value it maps (from-intake-parameter),
    /// or H3 writes it itself and H4 skips it. Anything else has no producer — it is a gap, named with
    /// its pinned owner (or UNOWNED). customer.bicep writing a name the manifest does not label
    /// from-bicep-output is a problem (the two disagree about who writes it).
    /// </summary>
    internal static (IReadOnlyList<string> Problems, IReadOnlyList<string> Gaps) ClassifyManifestEntries(
        IEnumerable<KvSecretEntry> entries,
        IReadOnlyDictionary<string, string> intakeValueParameterKeys,
        IReadOnlySet<string> h3WrittenSecretNames,
        IReadOnlySet<string> bicepWrittenNames,
        IReadOnlyDictionary<string, string> gapOwners)
    {
        var problems = new List<string>();
        var gaps = new List<string>();
        var entryList = entries.ToList();
        var labelledBicep = entryList.Where(e => e.ValueSource == KvSecretValueSource.FromBicepOutput)
            .Select(e => e.CanonicalName).ToHashSet(StringComparer.Ordinal);
        problems.AddRange(bicepWrittenNames.Where(n => !labelledBicep.Contains(n))
            .Select(n => $"{n}: customer.bicep writes it, but the manifest does not label it from-bicep-output"));

        foreach (var entry in entryList)
        {
            switch (entry.ValueSource)
            {
                case KvSecretValueSource.FromBicepOutput when bicepWrittenNames.Contains(entry.CanonicalName):
                case KvSecretValueSource.Generated:
                    break;
                case KvSecretValueSource.FromIntakeParameter:
                    if (!intakeValueParameterKeys.TryGetValue(entry.CanonicalName, out var intakeKey)
                        || !IntakeParameterCatalog.IsKnown(intakeKey))
                    {
                        problems.Add($"{entry.CanonicalName}: {entry.ValueSource} but H4 maps it to no accepted intake key");
                    }
                    break;
                case KvSecretValueSource.WrittenByEntraAppReg:
                    if (!h3WrittenSecretNames.Contains(entry.CanonicalName))
                    {
                        problems.Add($"{entry.CanonicalName}: written-by-h3, but H3's provisioner does not commit that name");
                    }
                    break;
                default:   // FromRunParameters / FromExistingKvSecret — needs a Secrets ref nothing writes
                    gaps.Add($"{entry.CanonicalName}→{(gapOwners.TryGetValue(entry.CanonicalName, out var owner) ? owner : "UNOWNED")}");
                    break;
            }
        }
        return (problems, gaps);
    }

    // ------------------------------------------------------------------ (h) H4b's per_env sources

    [Fact]
    public void GeneratorPerEnvSources_AreExactlyThePerEnvSourceCatalog()
    {
        // The generator validates per_env_source at authoring time (-Verify); the C# reader validates it at
        // H4b. Two copies of one closed set — this keeps them identical.
        var script = File.ReadAllText(Path.Combine(RepoRoot(), "scripts", "canonical-secret-catalog", "Invoke-CatalogGenerator.ps1"));
        var list = System.Text.RegularExpressions.Regex.Match(script, @"\$script:AllowedPerEnvSources = @\((?<body>[^)]*)\)");
        list.Success.Should().BeTrue("the generator must declare $script:AllowedPerEnvSources");
        var generatorSet = System.Text.RegularExpressions.Regex.Matches(list.Groups["body"].Value, "'([^']+)'").Select(m => m.Groups[1].Value);

        generatorSet.Should().BeEquivalentTo(PerEnvSourceCatalogExpressions());
    }

    private static IEnumerable<string> PerEnvSourceCatalogExpressions()
        => Sprk.Provisioning.ControlPlane.Handlers.BulkAppSettings.PerEnvSourceCatalog.All.Select(s => s.Expression);

    [Fact]
    public void EveryHandlerId_HasDeclaredInputs()
    {
        var undeclared = AllHandlerIds()
            .Where(id => id != HandlerIds.H14a && id != HandlerIds.H14m)   // in-process H14 sub-steps
            .Where(id => !HandlerRunInputs.ByHandler.ContainsKey(id))
            .ToList();
        undeclared.Should().BeEmpty("every handler's inputs are part of the contract (H14's sub-steps are covered by H14)");
    }

    // ------------------------------------------------------------------ scanner + checker

    internal sealed record SourceScan(
        IReadOnlySet<string> InterStepStateReferences,
        IReadOnlySet<string> InterStepStateWrites,
        IReadOnlySet<string> IntakeKeys,
        bool ReadsRunSecrets = false,
        bool WritesRunSecrets = false);

    /// <summary>
    /// Handlers allowed to READ run.Parameters.Secrets — a KeyVaultSecretRef channel with NO writer since
    /// task 245a. Nobody may write it. Each entry carries its reason.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> RunSecretsReaders = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        // Hands the whole map to the KV writer, which consults it only for manifest entries that still
        // need a ref (FromRunParameters / FromExistingKvSecret). Since T246 no manifest entry is
        // FromRunParameters; the two FromExistingKvSecret client secrets are omitted on secret-free stamps
        // (the default) and read a ref only under SecretFreeIdentityRollback (T225b).
        [HandlerIds.H4] = "client secrets under SecretFreeIdentityRollback only",
    };

    private static readonly IReadOnlySet<string> InterStepStatePropertyNames =
        typeof(InterStepState).GetProperties(BindingFlags.Public | BindingFlags.Instance).Select(p => p.Name).ToHashSet(StringComparer.Ordinal);

    /// <summary>
    /// IntakeParameterCatalog members that name an intake key a handler reads: every <c>const string</c> whose value
    /// is an accepted key (by reflection — a new catalog constant is seen without editing this test; until T245c the
    /// list was hand-kept, so a handler reading a newer constant was invisible to the scan), plus
    /// <c>ResolveEnvironmentName</c>.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> CatalogMemberToKey = BuildCatalogMemberToKey();

    private static Dictionary<string, string> BuildCatalogMemberToKey()
    {
        var map = typeof(IntakeParameterCatalog).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.IsLiteral && f.FieldType == typeof(string))
            .Select(f => (f.Name, Value: (string)f.GetRawConstantValue()!))
            .Where(f => IntakeParameterCatalog.IsKnown(f.Value))
            .ToDictionary(f => f.Name, f => f.Value, StringComparer.Ordinal);
        map[nameof(IntakeParameterCatalog.ResolveEnvironmentName)] = IntakeParameterCatalog.EnvironmentName;
        return map;
    }

    private static readonly IReadOnlySet<string> DictionaryKeyMethods =
        new HashSet<string>(StringComparer.Ordinal) { "TryGetValue", "ContainsKey", "GetValueOrDefault", "Remove" };

    private static readonly IReadOnlySet<string> DictionaryMutators =
        new HashSet<string>(StringComparer.Ordinal) { "Add", "TryAdd", "Remove", "Clear" };

    /// <summary>
    /// Parses <paramref name="source"/> as C# and records what it does with the run context. A syntax tree,
    /// not a regex: comments, string literals (incl. verbatim / raw / interpolated) and <c>?.</c> /
    /// <c>!.</c> access are told apart by the parser, and <c>nameof(...)</c> is not a read.
    /// </summary>
    internal static SourceScan Scan(string source)
    {
        var root = CSharpSyntaxTree.ParseText(source).GetRoot();
        var stateAliases = CollectAliases(root, (e, aliases) => DenotesInterStepState(e, aliases), includeTypedParameters: true);
        var nonSecretAliases = CollectAliases(root, (e, aliases) => DenotesMember(e, "NonSecret", aliases), includeTypedParameters: false);
        var secretsAliases = CollectAliases(root, (e, aliases) => DenotesRunSecrets(e, aliases), includeTypedParameters: false);

        var references = new HashSet<string>(StringComparer.Ordinal);
        var writes = new HashSet<string>(StringComparer.Ordinal);
        var keys = new HashSet<string>(StringComparer.Ordinal);
        var readsSecrets = false;
        var writesSecrets = false;

        foreach (var node in root.DescendantNodes())
        {
            switch (node)
            {
                case MemberAccessExpressionSyntax or ConditionalAccessExpressionSyntax when !IsInsideNameof(node):
                    if (TryGetInterStepStateProperty((ExpressionSyntax)node, stateAliases, out var property)) references.Add(property);
                    if (node is MemberAccessExpressionSyntax catalogAccess
                        && IsNamed(catalogAccess.Expression, "IntakeParameterCatalog")
                        && CatalogMemberToKey.TryGetValue(catalogAccess.Name.Identifier.ValueText, out var catalogKey))
                    {
                        keys.Add(catalogKey);
                    }
                    if (node is MemberAccessExpressionSyntax secretsAccess && DenotesRunSecrets(secretsAccess, secretsAliases)) readsSecrets = true;
                    break;

                case IdentifierNameSyntax identifier when secretsAliases.Contains(identifier.Identifier.ValueText):
                    readsSecrets = true;
                    break;

                case AssignmentExpressionSyntax assignment:
                    var target = Unwrap(assignment.Left);
                    if (TryGetInterStepStateProperty(target, stateAliases, out var written)) writes.Add(written);
                    if (DenotesRunSecrets(target, secretsAliases)
                        || (target is ElementAccessExpressionSyntax indexed && DenotesRunSecrets(indexed.Expression, secretsAliases)))
                    {
                        writesSecrets = true;
                    }
                    break;

                case InvocationExpressionSyntax invocation:
                    if (invocation.Expression is MemberAccessExpressionSyntax call
                        && DictionaryMutators.Contains(call.Name.Identifier.ValueText)
                        && DenotesRunSecrets(call.Expression, secretsAliases))
                    {
                        writesSecrets = true;
                    }
                    keys.UnionWith(LiteralIntakeKeys(invocation, nonSecretAliases));
                    break;

                case ElementAccessExpressionSyntax element when DenotesMember(element.Expression, "NonSecret", nonSecretAliases):
                    keys.UnionWith(element.ArgumentList.Arguments.Select(a => StringLiteral(a.Expression)).OfType<string>()
                        .Where(IntakeParameterCatalog.IsKnown));
                    break;

                case VariableDeclaratorSyntax { Initializer.Value: var value } declarator
                    when declarator.Identifier.ValueText.EndsWith("ParameterKey", StringComparison.Ordinal)
                        && StringLiteral(value) is { } constant:
                    keys.Add(constant);
                    break;
            }
        }
        return new SourceScan(references, writes, keys, readsSecrets, writesSecrets);
    }

    /// <summary>
    /// String-literal intake keys handed to a dictionary lookup (<c>nonSecret.TryGetValue("tenantId", …)</c>)
    /// or to a helper alongside the NonSecret map (<c>AddIfPresent(columns, nonSecret, "bffVersion", …)</c>).
    /// Only literals that ARE accepted intake keys count — a column name or a message in the same call is not one.
    /// </summary>
    private static IEnumerable<string> LiteralIntakeKeys(InvocationExpressionSyntax invocation, IReadOnlySet<string> nonSecretAliases)
    {
        var onNonSecret = invocation.Expression is MemberAccessExpressionSyntax call
            && DictionaryKeyMethods.Contains(call.Name.Identifier.ValueText)
            && DenotesMember(call.Expression, "NonSecret", nonSecretAliases);
        var takesNonSecret = invocation.ArgumentList.Arguments.Any(a => DenotesMember(a.Expression, "NonSecret", nonSecretAliases));
        if (!onNonSecret && !takesNonSecret) return [];
        return invocation.ArgumentList.Arguments.Select(a => StringLiteral(a.Expression)).OfType<string>()
            .Where(IntakeParameterCatalog.IsKnown);
    }

    /// <summary>Locals bound to an expression <paramref name="denotes"/> recognises (chained: <c>var b = a;</c>), plus — for the
    /// run state — parameters typed <c>InterStepState</c>.</summary>
    private static HashSet<string> CollectAliases(
        SyntaxNode root, Func<ExpressionSyntax, IReadOnlySet<string>, bool> denotes, bool includeTypedParameters)
    {
        var aliases = new HashSet<string>(StringComparer.Ordinal);
        if (includeTypedParameters)
        {
            foreach (var parameter in root.DescendantNodes().OfType<ParameterSyntax>())
            {
                if (parameter.Type is { } type && IsInterStepStateType(type)) aliases.Add(parameter.Identifier.ValueText);
            }
        }
        bool added;
        do
        {
            added = false;
            foreach (var declarator in root.DescendantNodes().OfType<VariableDeclaratorSyntax>())
            {
                if (declarator.Initializer?.Value is { } value && denotes(value, aliases) && aliases.Add(declarator.Identifier.ValueText))
                {
                    added = true;
                }
            }
        }
        while (added);
        return aliases;
    }

    private static bool IsInterStepStateType(TypeSyntax type) => type switch
    {
        NullableTypeSyntax nullable => IsInterStepStateType(nullable.ElementType),
        IdentifierNameSyntax name => name.Identifier.ValueText == nameof(InterStepState),
        QualifiedNameSyntax qualified => qualified.Right.Identifier.ValueText == nameof(InterStepState),
        _ => false,
    };

    /// <summary><c>run.InterStepState</c>, <c>run?.InterStepState</c>, <c>run.InterStepState!</c>, or an alias of it.</summary>
    private static bool DenotesInterStepState(ExpressionSyntax expression, IReadOnlySet<string> aliases)
        => DenotesMember(expression, nameof(InterStepState), aliases);

    /// <summary><c>run.Parameters.Secrets</c> (any null-conditional / null-forgiving shape) or an alias of it.</summary>
    private static bool DenotesRunSecrets(ExpressionSyntax expression, IReadOnlySet<string> aliases)
    {
        var e = Unwrap(expression);
        if (e is IdentifierNameSyntax alias && aliases.Contains(alias.Identifier.ValueText)) return true;
        return e switch
        {
            MemberAccessExpressionSyntax { Name.Identifier.ValueText: "Secrets" } access => IsNamed(access.Expression, "Parameters"),
            MemberBindingExpressionSyntax { Name.Identifier.ValueText: "Secrets" } binding
                => binding.Parent is ConditionalAccessExpressionSyntax parent && IsNamed(parent.Expression, "Parameters"),
            _ => false,
        };
    }

    /// <summary>An expression whose last member is <paramref name="memberName"/> (<c>x.Member</c>, <c>x?.Member</c>,
    /// <c>x.Member ?? fallback</c>), or one of <paramref name="aliases"/>.</summary>
    private static bool DenotesMember(ExpressionSyntax expression, string memberName, IReadOnlySet<string> aliases)
    {
        var e = Unwrap(expression);
        if (e is BinaryExpressionSyntax coalesce && coalesce.IsKind(SyntaxKind.CoalesceExpression))
        {
            return DenotesMember(coalesce.Left, memberName, aliases);
        }
        if (e is ConditionalAccessExpressionSyntax conditional)
        {
            return conditional.WhenNotNull is MemberBindingExpressionSyntax binding && binding.Name.Identifier.ValueText == memberName;
        }
        return e switch
        {
            IdentifierNameSyntax name => aliases.Contains(name.Identifier.ValueText),
            _ => IsNamed(e, memberName),
        };
    }

    /// <summary>The InterStepState property <paramref name="expression"/> reads (<c>s.X</c>, <c>s?.X</c>, <c>s!.X</c>).</summary>
    private static bool TryGetInterStepStateProperty(
        ExpressionSyntax expression, IReadOnlySet<string> aliases, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out string? property)
    {
        property = Unwrap(expression) switch
        {
            MemberAccessExpressionSyntax access when DenotesInterStepState(access.Expression, aliases) => access.Name.Identifier.ValueText,
            ConditionalAccessExpressionSyntax conditional when DenotesInterStepState(conditional.Expression, aliases)
                => LeftmostBinding(conditional.WhenNotNull)?.Name.Identifier.ValueText,
            _ => null,
        };
        return property is not null && InterStepStatePropertyNames.Contains(property);
    }

    private static MemberBindingExpressionSyntax? LeftmostBinding(ExpressionSyntax expression) => expression switch
    {
        MemberBindingExpressionSyntax binding => binding,
        MemberAccessExpressionSyntax access => LeftmostBinding(access.Expression),
        InvocationExpressionSyntax invocation => LeftmostBinding(invocation.Expression),
        ElementAccessExpressionSyntax element => LeftmostBinding(element.Expression),
        ConditionalAccessExpressionSyntax conditional => LeftmostBinding(conditional.Expression),
        PostfixUnaryExpressionSyntax postfix => LeftmostBinding(postfix.Operand),
        _ => null,
    };

    /// <summary>True for <c>…Name</c>, <c>…?.Name</c> (member binding) or a bare <c>Name</c>.</summary>
    private static bool IsNamed(ExpressionSyntax expression, string name) => Unwrap(expression) switch
    {
        MemberAccessExpressionSyntax access => access.Name.Identifier.ValueText == name,
        MemberBindingExpressionSyntax binding => binding.Name.Identifier.ValueText == name,
        IdentifierNameSyntax identifier => identifier.Identifier.ValueText == name,
        _ => false,
    };

    /// <summary>Strips parentheses and the null-forgiving <c>!</c>.</summary>
    private static ExpressionSyntax Unwrap(ExpressionSyntax expression) => expression switch
    {
        ParenthesizedExpressionSyntax parenthesized => Unwrap(parenthesized.Expression),
        PostfixUnaryExpressionSyntax postfix when postfix.IsKind(SyntaxKind.SuppressNullableWarningExpression) => Unwrap(postfix.Operand),
        _ => expression,
    };

    private static string? StringLiteral(ExpressionSyntax expression)
        => Unwrap(expression) is LiteralExpressionSyntax literal && literal.IsKind(SyntaxKind.StringLiteralExpression)
            ? literal.Token.ValueText
            : null;

    private static bool IsInsideNameof(SyntaxNode node)
        => node.Ancestors().OfType<InvocationExpressionSyntax>()
            .Any(i => i.Expression is IdentifierNameSyntax { Identifier.ValueText: "nameof" });

    internal static IReadOnlyList<string> FindViolations(string folder, string? handler, SourceScan scan)
    {
        var violations = new List<string>();
        if (scan.WritesRunSecrets)
        {
            violations.Add($"Handlers/{folder} writes run.Parameters.Secrets — that channel has no writer (task 245a); hand values on through InterStepState");
        }
        if (scan.ReadsRunSecrets && (handler is null || !RunSecretsReaders.ContainsKey(handler)))
        {
            violations.Add($"Handlers/{folder} ({handler ?? "shared"}) reads run.Parameters.Secrets — a channel nothing writes since task 245a");
        }
        if (handler is null)
        {
            violations.AddRange(scan.InterStepStateReferences.Select(p => $"Handlers/{folder} (shared, no handler) references InterStepState.{p}"));
            violations.AddRange(scan.IntakeKeys.Select(k => $"Handlers/{folder} (shared, no handler) reads intake key '{k}'"));
            return violations;
        }

        IReadOnlyList<RunInput> declared = HandlerRunInputs.ByHandler.TryGetValue(handler, out var inputs)
            ? inputs
            : Array.Empty<RunInput>();
        var declaredOutputs = declared.Where(i => i.Source == RunInputSource.Output).Select(i => i.Name).ToHashSet(StringComparer.Ordinal);
        var declaredKeys = declared.Where(i => i.Source is RunInputSource.Intake or RunInputSource.Gap).Select(i => i.Name).ToHashSet(StringComparer.Ordinal);

        foreach (var property in scan.InterStepStateReferences)
        {
            var producer = ProducerOf(property);
            if (producer != handler && !declaredOutputs.Contains(property))
            {
                violations.Add($"Handlers/{folder} ({handler}) reads InterStepState.{property}, not declared in HandlerRunInputs");
            }
        }
        foreach (var property in scan.InterStepStateWrites)
        {
            var producer = ProducerOf(property);
            if (producer != handler)
            {
                violations.Add($"Handlers/{folder} ({handler}) writes InterStepState.{property}, whose producer is {producer ?? "nobody ([NoProducer])"}");
            }
        }
        foreach (var key in scan.IntakeKeys)
        {
            if (!declaredKeys.Contains(key))
            {
                violations.Add($"Handlers/{folder} ({handler}) reads run parameter '{key}', not declared in HandlerRunInputs");
            }
        }
        return violations;
    }

    /// <summary>
    /// The other direction of rule (e): every input a handler declares is actually read somewhere in its
    /// folder. A stale declaration would let rules (b)–(d) vouch for a dependency that no longer exists.
    /// </summary>
    internal static IReadOnlyList<string> FindUnreadDeclarations(
        string folder, string handler, SourceScan scan, IReadOnlyList<RunInput> declared)
        => declared
            .Where(i => i.Source == RunInputSource.Output
                ? !scan.InterStepStateReferences.Contains(i.Name)
                : !scan.IntakeKeys.Contains(i.Name))
            .Select(i => i.Source == RunInputSource.Output
                ? $"Handlers/{folder} ({handler}) declares InterStepState.{i.Name} in HandlerRunInputs but never reads it"
                : $"Handlers/{folder} ({handler}) declares run parameter '{i.Name}' in HandlerRunInputs but never reads it")
            .ToList();

    // ------------------------------------------------------------------ table checkers (rules a–d)
    // Each takes its tables as arguments so the negative / positive controls can seed them.

    internal static IReadOnlyList<string> FindProducerAnnotationProblems(Type stateType, IReadOnlySet<string> knownHandlerIds)
    {
        var problems = new List<string>();
        foreach (var property in stateType.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            var producedBy = property.GetCustomAttribute<ProducedByAttribute>();
            var noProducer = property.GetCustomAttribute<NoProducerAttribute>();
            if ((producedBy is null) == (noProducer is null))
            {
                problems.Add($"{property.Name}: needs exactly one of [ProducedBy] / [NoProducer]");
            }
            if (producedBy is not null && !knownHandlerIds.Contains(producedBy.HandlerId))
            {
                problems.Add($"{property.Name}: [ProducedBy(\"{producedBy.HandlerId}\")] is not a HandlerIds constant");
            }
        }
        return problems;
    }

    internal static IReadOnlyList<string> FindUnreachableRequiredOutputs(
        IReadOnlyDictionary<string, IReadOnlyList<RunInput>> inputsByHandler,
        Func<string, string?> producerOf,
        IReadOnlyDictionary<string, string[]> dag)
    {
        var problems = new List<string>();
        foreach (var (handler, inputs) in inputsByHandler)
        {
            foreach (var input in inputs.Where(i => i.Source == RunInputSource.Output && i.Required))
            {
                var producer = producerOf(input.Name);
                if (producer is null)
                {
                    problems.Add($"{handler} requires InterStepState.{input.Name}, which has no [ProducedBy] producer");
                }
                else if (!AncestorsOf(handler, dag).Contains(producer))
                {
                    problems.Add($"{handler} requires InterStepState.{input.Name}, produced by {producer}, which is not a DAG ancestor of {handler}");
                }
            }
        }
        return problems;
    }

    /// <summary>Gaps are still read from intake today, so they must be accepted keys too.</summary>
    internal static IReadOnlyList<string> FindUnknownIntakeInputs(
        IReadOnlyDictionary<string, IReadOnlyList<RunInput>> inputsByHandler,
        Func<string, bool> isKnownIntakeKey)
        => inputsByHandler
            .SelectMany(kv => kv.Value
                .Where(i => i.Source is RunInputSource.Intake or RunInputSource.Gap)
                .Where(i => !isKnownIntakeKey(i.Name))
                .Select(i => $"{kv.Key}: '{i.Name}' is not an IntakeParameterCatalog key"))
            .ToList();

    internal static IReadOnlyList<string> FindRequiredUnproducedOutputs(
        IReadOnlyDictionary<string, IReadOnlyList<RunInput>> inputsByHandler,
        IReadOnlySet<string> noProducerProperties)
        => inputsByHandler
            .SelectMany(kv => kv.Value
                .Where(i => i.Source == RunInputSource.Output && i.Required && noProducerProperties.Contains(i.Name))
                .Select(i => $"{kv.Key} requires InterStepState.{i.Name}, which nothing produces"))
            .ToList();

    private static IReadOnlySet<string> NoProducerPropertyNames(Type stateType)
        => stateType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.GetCustomAttribute<NoProducerAttribute>() is not null)
            .Select(p => p.Name)
            .ToHashSet(StringComparer.Ordinal);

    /// <summary>Seeded violations for rule (a)'s negative control.</summary>
    private sealed class SeededBadAnnotations
    {
        public string? Unannotated { get; set; }

        [ProducedBy(HandlerIds.H2a)]
        [NoProducer("seeded")]
        public string? BothAnnotations { get; set; }

        [ProducedBy("H99")]
        public string? UnknownProducer { get; set; }
    }

    /// <summary>The sanctioned shapes for rule (a)'s positive control.</summary>
    private sealed class SeededGoodAnnotations
    {
        [ProducedBy(HandlerIds.H2a)]
        public string? Produced { get; set; }

        [NoProducer("seeded")]
        public string? Unproduced { get; set; }
    }

    // ------------------------------------------------------------------ helpers

    private static string? ProducerOf(string interStepStateProperty)
        => typeof(InterStepState).GetProperty(interStepStateProperty)?.GetCustomAttribute<ProducedByAttribute>()?.HandlerId;

    private static HashSet<string> AncestorsOf(string handler, IReadOnlyDictionary<string, string[]> dag)
    {
        var ancestors = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Stack<string>(dag.TryGetValue(handler, out var deps) ? deps : Array.Empty<string>());
        while (pending.Count > 0)
        {
            var next = pending.Pop();
            if (!ancestors.Add(next)) continue;
            if (dag.TryGetValue(next, out var more))
            {
                foreach (var d in more) pending.Push(d);
            }
        }
        return ancestors;
    }

    private static HashSet<string> AllHandlerIds()
        => typeof(HandlerIds).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.IsLiteral && f.FieldType == typeof(string))
            .Select(f => (string)f.GetRawConstantValue()!)
            .ToHashSet(StringComparer.Ordinal);

    private static string RepoRoot()
    {
        // A worktree's .git is a FILE; a regular checkout's is a directory.
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var gitMarker = Path.Combine(dir.FullName, ".git");
            if (Directory.Exists(gitMarker) || File.Exists(gitMarker)) return dir.FullName;
            dir = dir.Parent;
        }
        throw new InvalidOperationException($"Could not locate the repo root walking up from '{AppContext.BaseDirectory}'.");
    }
}
