using System.Text.RegularExpressions;
using NetArchTest.Rules;
using Spaarke.Scheduling;
using Xunit;

namespace Spaarke.ArchTests;

/// <summary>
/// ADR-052 structural rules (unified-access-control-r2 task 102), each with a negative and a positive control:
/// <list type="number">
///   <item><b>Timer ratchet</b> (ADR-052 §1) — no new hand-rolled timer <c>BackgroundService</c> in the BFF.
///   Scheduled work in the BFF is an <c>IScheduledJob</c> on <c>ScheduledJobHost</c> (ADR-036). The existing
///   timer services are listed with reasons and the list may only shrink.</item>
///   <item><b>Host-neutrality</b> (ADR-052 §5, ADR-036 A1 rule 7) — no <c>IScheduledJob</c> depends on
///   <c>ScheduledJobHost</c>, <c>IBackgroundJobStore</c> or <c>ScheduledJobRegistry</c>, so a job can move to
///   another host without being rewritten.</item>
///   <item><b>Functions projects</b> (ADR-052 §5–§6) — a project referencing the Functions worker SDK lives under
///   <c>src/server/functions/</c> (so every <c>src/server/**</c> ArchTest — credential guards, tenant-isolation
///   invariants — covers it), never references <c>Sprk.Bff.Api</c>, and authenticates app-only as the stamp's
///   managed identity: no MSAL confidential client and no managed-identity assertion, which is how a Function
///   would act as the BFF app registration.</item>
/// </list>
/// <para>Per <c>tests/CLAUDE.md</c> "Structural fitness functions" this file is MAINTAIN-class: it is the
/// mechanism, not scaffolding.</para>
/// </summary>
public class WorkloadPlacementGuardTests
{
    private const string BackgroundServiceFullName = "Microsoft.Extensions.Hosting.BackgroundService";

    // =============================================================================================
    // RULE 1 — THE BACKGROUNDSERVICE INVENTORY (timer ratchet)
    // ---------------------------------------------------------------------------------------------
    // MAINTENANCE PROCEDURE — read before changing a list here.
    //
    //   1. A NEW BackgroundService subclass fails NoBackgroundServiceOutsideTheInventory. If it drives its own
    //      timer (PeriodicTimer, a Task.Delay loop, a wait-until-midnight loop), it is scheduled work: write an
    //      IScheduledJob instead (ADR-036; .claude/patterns/api/scheduled-jobs.md). If it consumes a queue or
    //      topic, it is ADR-004 work: an IJobHandler behind ServiceBusJobProcessor. Only a genuinely different
    //      shape — once-at-startup work, a long-lived connection that is not a message consumer — belongs in
    //      OtherBackgroundServices, with a reason and an ADR citation.
    //
    //   2. ExistingTimerServices ONLY SHRINKS. When a timer service migrates to IScheduledJob (ADR-052 §1:
    //      "when next touched" — any PR changing its behaviour or its timer loop), delete its entry AND lower
    //      TimerServiceBaseline. Never add an entry; never raise the baseline.
    //
    //   3. An entry naming a class that no longer exists fails InventoryHasNoStaleEntries — delete it. A stale
    //      entry is a hole the next class of that name walks through.
    // =============================================================================================
    private const int TimerServiceBaseline = 14;

    private static readonly IReadOnlyDictionary<string, string> ExistingTimerServices = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["ScheduledRagIndexingService"] = "PeriodicTimer RAG re-index loop. Migrates to IScheduledJob when next touched (ADR-052 §1, ADR-036).",
        ["RecordSyncJob"] = "PeriodicTimer record-sync loop. Migrates to IScheduledJob when next touched (ADR-052 §1, ADR-036).",
        ["TodoGenerationService"] = "Initial delay + PeriodicTimer to-do generation loop. Migrates when next touched (ADR-052 §1, ADR-036).",
        ["SpeDashboardSyncService"] = "PeriodicTimer SPE dashboard sync loop. Migrates when next touched (ADR-052 §1, ADR-036).",
        ["DemoExpirationService"] = "Wait-until-midnight Task.Delay loop expiring demo tenants. Migrates when next touched (ADR-052 §1, ADR-036).",
        ["SessionFileRetentionJob"] = "PeriodicTimer (TimeProvider) session-file retention loop. Migrates when next touched (ADR-052 §1, ADR-036).",
        ["SessionFilesCleanupJob"] = "PeriodicTimer session-files cleanup loop. Migrates when next touched (ADR-052 §1, ADR-036).",
        ["MembershipReconcileSweepService"] = "Task.Delay sweep loop for communication membership. Migrates when next touched (ADR-052 §1, ADR-036).",
        ["MailboxDeltaReconciliationService"] = "PeriodicTimer mailbox delta reconciliation. Migrates when next touched (ADR-052 §1, ADR-036).",
        ["InboundPollingBackupService"] = "PeriodicTimer inbound-mail polling backup. Migrates when next touched (ADR-052 §1, ADR-036).",
        ["GraphSubscriptionManager"] = "PeriodicTimer Graph subscription renewal. Migrates when next touched (ADR-052 §1, ADR-036).",
        ["StaleCheckoutSweeperHostedService"] = "Task.Delay (TimeProvider) stale-checkout sweep loop. Migrates when next touched (ADR-052 §1, ADR-036).",
        ["SpeWebhookRenewalHostedService"] = "Task.Delay (TimeProvider) SPE webhook renewal loop. Migrates when next touched (ADR-052 §1, ADR-036).",
        ["DailySendCountResetService"] = "Wait-until-midnight Task.Delay loop resetting daily send counts. Migrates when next touched (ADR-052 §1, ADR-036).",
    };

    private static readonly IReadOnlyDictionary<string, string> OtherBackgroundServices = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["ServiceBusJobProcessor"] = "Queue consumer — the ADR-004 host itself; Task.Delay(Infinite) only parks ExecuteAsync while the processor runs.",
        ["CommunicationJobProcessor"] = "Queue consumer for communication jobs (ADR-004 shape); parks on Task.Delay(Infinite).",
        ["UploadFinalizationWorker"] = "Office queue consumer with a bespoke message shape — a named non-conforming consumer (ADR-004 A1 §6).",
        ["ProfileSummaryWorker"] = "Office queue consumer with a bespoke message shape — a named non-conforming consumer (ADR-004 A1 §6).",
        ["IndexingWorkerHostedService"] = "Office queue consumer with a bespoke message shape — a named non-conforming consumer (ADR-004 A1 §6).",
        ["MembershipJunctionUpdaterHost"] = "Topic consumer for sprk-membership-changes — a named non-conforming consumer (ADR-004 A1 §6, ADR-034).",
        ["NullMembershipJunctionUpdaterHost"] = "Null-Object stand-in for MembershipJunctionUpdaterHost when the topic is not configured; does no work (ADR-032).",
        ["BulkOperationService"] = "Processes SPE bulk operations submitted through BulkOperationEndpoints; not a periodic timer (ADR-052 §1).",
        ["EmbeddingMigrationService"] = "One-shot embedding migration started at host startup, batched with back-off delays; not a periodic timer (ADR-052 §1).",
    };

    [Fact(DisplayName = "ADR-052 §1: no BackgroundService in the BFF outside the reasoned inventory (timer ratchet)")]
    public void NoBackgroundServiceOutsideTheInventory()
    {
        var discovered = BackgroundServiceTypes(ADR001_MinimalApiTests.LoadableTypes(typeof(Program).Assembly), BackgroundServiceFullName)
            .Select(t => t.Name)
            .ToList();

        // Non-vacuity: a base-type name that stopped matching would report nothing and pass.
        Assert.True(
            discovered.Count >= ExistingTimerServices.Count + OtherBackgroundServices.Count,
            $"Found only {discovered.Count} BackgroundService subclasses in the BFF — the detector is not seeing them.");

        var unlisted = FindUnlisted(discovered, ExistingTimerServices, OtherBackgroundServices);
        Assert.True(
            unlisted.Count == 0,
            "New BackgroundService subclasses in Sprk.Bff.Api: " + string.Join(", ", unlisted) + ". Scheduled work in " +
            "the BFF is an IScheduledJob on ScheduledJobHost (ADR-036); queue work is an IJobHandler behind " +
            "ServiceBusJobProcessor (ADR-004); where the work runs at all is ADR-052. No new hand-rolled timer " +
            "BackgroundService (ADR-052 §1). See the maintenance procedure in WorkloadPlacementGuardTests.cs.");
    }

    [Fact(DisplayName = "ADR-052 §1: the timer-service list only shrinks, and names no class that no longer exists")]
    public void InventoryHasNoStaleEntries()
    {
        var discovered = BackgroundServiceTypes(ADR001_MinimalApiTests.LoadableTypes(typeof(Program).Assembly), BackgroundServiceFullName)
            .Select(t => t.Name)
            .ToHashSet(StringComparer.Ordinal);

        var stale = ExistingTimerServices.Keys.Concat(OtherBackgroundServices.Keys).Where(n => !discovered.Contains(n)).ToList();
        Assert.True(stale.Count == 0, "Inventory entries naming no BFF BackgroundService — delete them (and lower TimerServiceBaseline for a timer): " + string.Join(", ", stale));

        Assert.True(
            ExistingTimerServices.Count <= TimerServiceBaseline,
            $"ExistingTimerServices grew to {ExistingTimerServices.Count} (baseline {TimerServiceBaseline}). The list only shrinks — write an IScheduledJob instead.");
    }

    [Fact(DisplayName = "ADR-052 §1: every inventory entry carries a written reason and an ADR citation")]
    public void EveryInventoryEntryIsExplained()
    {
        var unexplained = ExistingTimerServices.Concat(OtherBackgroundServices)
            .Where(e => e.Value.Length < 40 || !e.Value.Contains("ADR-", StringComparison.Ordinal))
            .Select(e => e.Key)
            .ToList();

        Assert.True(unexplained.Count == 0, "Inventory entries without a substantive reason and an ADR citation: " + string.Join(", ", unexplained));
    }

    [Fact(DisplayName = "ADR-052 §1: negative control — a seeded BackgroundService, direct or indirect, is flagged")]
    public void Ratchet_NegativeControl_FlagsASeededService()
    {
        var types = new[] { typeof(Adr052SeededTimerService), typeof(Adr052SeededDerivedService), typeof(Adr052NotAHostedService) };
        var found = BackgroundServiceTypes(types, typeof(Adr052FakeBackgroundService).FullName!).Select(t => t.Name).ToList();

        Assert.Contains(nameof(Adr052SeededTimerService), found);
        Assert.Contains(nameof(Adr052SeededDerivedService), found);

        var unlisted = FindUnlisted(found, ExistingTimerServices, OtherBackgroundServices);
        Assert.Contains(nameof(Adr052SeededTimerService), unlisted);
        Assert.Contains(nameof(Adr052SeededDerivedService), unlisted);
    }

    [Fact(DisplayName = "ADR-052 §1: positive control — a listed service and a non-hosted type are not flagged")]
    public void Ratchet_PositiveControl_DoesNotFlagTheSanctionedShapes()
    {
        var types = new[] { typeof(Adr052SeededTimerService), typeof(Adr052NotAHostedService) };
        var found = BackgroundServiceTypes(types, typeof(Adr052FakeBackgroundService).FullName!).Select(t => t.Name).ToList();

        Assert.DoesNotContain(nameof(Adr052NotAHostedService), found);

        var listed = new Dictionary<string, string>(StringComparer.Ordinal) { [nameof(Adr052SeededTimerService)] = "listed (ADR-052 test)" };
        Assert.Empty(FindUnlisted(found, listed, new Dictionary<string, string>()));
    }

    internal static IEnumerable<Type> BackgroundServiceTypes(IEnumerable<Type> types, string baseTypeFullName)
        => types.Where(t => t.IsClass && !t.IsAbstract && DerivesFrom(t, baseTypeFullName));

    internal static IReadOnlyList<string> FindUnlisted(
        IEnumerable<string> discovered,
        IReadOnlyDictionary<string, string> timers,
        IReadOnlyDictionary<string, string> others)
        => discovered.Where(n => !timers.ContainsKey(n) && !others.ContainsKey(n)).Distinct(StringComparer.Ordinal).ToList();

    private static bool DerivesFrom(Type type, string baseTypeFullName)
    {
        for (var current = type.BaseType; current is not null; current = current.BaseType)
        {
            if (string.Equals(current.FullName, baseTypeFullName, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    // =============================================================================================
    // RULE 2 — IScheduledJob HOST-NEUTRALITY
    // =============================================================================================
    private static readonly string[] SchedulerHostTypes =
    {
        "Spaarke.Scheduling.ScheduledJobHost",
        "Spaarke.Scheduling.IBackgroundJobStore",
        "Spaarke.Scheduling.InMemoryBackgroundJobStore",
        "Spaarke.Scheduling.ScheduledJobRegistry",
    };

    [Fact(DisplayName = "ADR-052 §5 / ADR-036 A1: no IScheduledJob depends on the scheduler host, store or registry")]
    public void ScheduledJobsAreHostNeutral()
    {
        var assembly = typeof(Program).Assembly;

        var jobs = Types.InAssembly(assembly).That().ImplementInterface(typeof(IScheduledJob)).GetTypes().ToList();
        Assert.True(jobs.Count >= 3, $"Found {jobs.Count} IScheduledJob implementations in the BFF — expected at least 3; the rule would pass vacuously.");

        var result = Types.InAssembly(assembly)
            .That().ImplementInterface(typeof(IScheduledJob))
            .ShouldNot().HaveDependencyOnAny(SchedulerHostTypes)
            .GetResult();

        Assert.True(
            result.IsSuccessful,
            "IScheduledJob implementations must not depend on ScheduledJobHost, IBackgroundJobStore or " +
            "ScheduledJobRegistry (ADR-036 A1 rule 7, ADR-052 §5). Registration belongs in AddScheduledJob<TJob>, " +
            "not in the job. Failing: " + string.Join(", ", result.FailingTypeNames ?? Array.Empty<string>()));
    }

    [Fact(DisplayName = "ADR-052 §5: negative control — a job coupled to the registry is flagged")]
    public void HostNeutrality_NegativeControl_FlagsACoupledJob()
    {
        var selected = Types.InAssembly(typeof(WorkloadPlacementGuardTests).Assembly).That().HaveName(nameof(Adr052SeededCoupledJob)).GetTypes().Count();
        Assert.Equal(1, selected);

        var result = Types.InAssembly(typeof(WorkloadPlacementGuardTests).Assembly)
            .That().HaveName(nameof(Adr052SeededCoupledJob))
            .ShouldNot().HaveDependencyOnAny(SchedulerHostTypes)
            .GetResult();

        Assert.False(result.IsSuccessful);
    }

    [Fact(DisplayName = "ADR-052 §5: positive control — a host-neutral job is not flagged")]
    public void HostNeutrality_PositiveControl_PassesAHostNeutralJob()
    {
        var selected = Types.InAssembly(typeof(WorkloadPlacementGuardTests).Assembly).That().HaveName(nameof(Adr052SanctionedJob)).GetTypes().Count();
        Assert.Equal(1, selected);

        var result = Types.InAssembly(typeof(WorkloadPlacementGuardTests).Assembly)
            .That().HaveName(nameof(Adr052SanctionedJob))
            .ShouldNot().HaveDependencyOnAny(SchedulerHostTypes)
            .GetResult();

        Assert.True(result.IsSuccessful);
    }

    // =============================================================================================
    // RULE 3 — FUNCTIONS PROJECTS: location, references, identity
    // =============================================================================================
    private const string FunctionsRoot = "src/server/functions/";

    private static readonly Regex FunctionsWorkerPackage =
        new(@"<PackageReference\s+Include=""Microsoft\.Azure\.Functions\.Worker", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex BffProjectReference =
        new(@"<ProjectReference\s+Include=""[^""]*Sprk\.Bff\.Api\.csproj""", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>
    /// The types a Function would need to act as an app registration rather than app-only as the managed
    /// identity (ADR-028 A4's confidential-client row). The stamp's UAMI can mint the MI-FIC assertion for the BFF
    /// app registration, so the rule is enforced here rather than trusted.
    /// </summary>
    private static readonly Regex ConfidentialClientUse =
        new(@"\b(?:ConfidentialClientApplicationBuilder|ManagedIdentityClientAssertion|WithClientAssertion|AcquireTokenOnBehalfOf|OnBehalfOfCredential)\b", RegexOptions.CultureInvariant);

    [Fact(DisplayName = "ADR-052 §5-§6: every Functions project lives under src/server/functions/ and does not reference the BFF")]
    public void FunctionsProjectsAreWhereTheGuardsCanSeeThem()
    {
        var csprojs = PlacementRepoFiles.Walk(Path.Combine(SourceScan.RepoRoot, "src"))
            .Where(f => f.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
            .Select(f => (Path: PlacementRepoFiles.Relative(f), Content: File.ReadAllText(f)))
            .ToList();

        Assert.True(csprojs.Count > 5, $"Found only {csprojs.Count} projects under src/ — the walk is broken.");

        var findings = CheckFunctionsProjects(csprojs);
        Assert.True(findings.Count == 0, string.Join("\n", findings));
    }

    [Fact(DisplayName = "ADR-052 §6: code under src/server/functions/ authenticates app-only — no confidential client, no assertion, no OBO")]
    public void FunctionsAuthenticateAppOnly()
    {
        // Until the first Function exists this scans nothing; the controls below are what prove the rule.
        var sources = PlacementRepoFiles.Walk(Path.Combine(SourceScan.RepoRoot, "src", "server", "functions"))
            .Where(f => f.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
            .Select(f => (Path: PlacementRepoFiles.Relative(f), Content: File.ReadAllText(f)));

        var findings = CheckFunctionsIdentity(sources);
        Assert.True(findings.Count == 0, string.Join("\n", findings));
    }

    [Fact(DisplayName = "ADR-052 §5-§6: negative control — a misplaced project, a BFF reference and a confidential client are each flagged")]
    public void FunctionsGuard_NegativeControl_FiresOnEachViolation()
    {
        var misplaced = CheckFunctionsProjects(new[] { ("src/server/api/Sprk.Sync/Sprk.Sync.csproj", WorkerProject(string.Empty)) });
        Assert.Contains(misplaced, f => f.Contains("must live under src/server/functions/", StringComparison.Ordinal));

        var bffReference = CheckFunctionsProjects(new[]
        {
            ("src/server/functions/Sprk.Sync/Sprk.Sync.csproj",
             WorkerProject(@"<ProjectReference Include=""..\..\api\Sprk.Bff.Api\Sprk.Bff.Api.csproj"" />")),
        });
        Assert.Contains(bffReference, f => f.Contains("must not reference Sprk.Bff.Api", StringComparison.Ordinal));

        var identity = CheckFunctionsIdentity(new[]
        {
            ("src/server/functions/Sprk.Sync/Auth.cs",
             "var app = ConfidentialClientApplicationBuilder.Create(id)\n    .WithClientAssertion(GetAssertionAsync)\n    .Build();\n" +
             "var obo = await app.AcquireTokenOnBehalfOf(scopes, userAssertion).ExecuteAsync();\n"),
        });
        Assert.Contains(identity, f => f.Contains("ConfidentialClientApplicationBuilder", StringComparison.Ordinal));
        Assert.Contains(identity, f => f.Contains("WithClientAssertion", StringComparison.Ordinal));
        Assert.Contains(identity, f => f.Contains("AcquireTokenOnBehalfOf", StringComparison.Ordinal));
    }

    [Fact(DisplayName = "ADR-052 §5-§6: positive control — the sanctioned Functions project and app-only credential pass")]
    public void FunctionsGuard_PositiveControl_PassesTheSanctionedShape()
    {
        Assert.Empty(CheckFunctionsProjects(new[]
        {
            ("src/server/functions/Sprk.Sync/Sprk.Sync.csproj",
             WorkerProject(@"<ProjectReference Include=""..\..\shared\Spaarke.Core\Spaarke.Core.csproj"" />")),
            // Not a Functions project: referencing the BFF is fine for, e.g., a test project.
            ("tests/unit/Sprk.Bff.Api.Tests/Sprk.Bff.Api.Tests.csproj",
             @"<Project Sdk=""Microsoft.NET.Sdk""><ItemGroup><ProjectReference Include=""..\..\..\src\server\api\Sprk.Bff.Api\Sprk.Bff.Api.csproj"" /></ItemGroup></Project>"),
        }));

        Assert.Empty(CheckFunctionsIdentity(new[]
        {
            ("src/server/functions/Sprk.Sync/Credentials.cs",
             "var credential = new DefaultAzureCredential(new DefaultAzureCredentialOptions { ManagedIdentityClientId = clientId });\n" +
             "// Never ConfidentialClientApplicationBuilder here — a comment is not a use.\n"),
            // Outside the Functions root the rule does not apply (the BFF's own provider is covered by CredentialCensusTests).
            ("src/server/api/Sprk.Bff.Api/Infrastructure/Auth/OrderedCredentialClientProvider.cs",
             "var app = ConfidentialClientApplicationBuilder.Create(id).Build();\n"),
        }));
    }

    internal static IReadOnlyList<string> CheckFunctionsProjects(IEnumerable<(string Path, string Content)> projects)
    {
        var findings = new List<string>();
        foreach (var (path, content) in projects)
        {
            if (!FunctionsWorkerPackage.IsMatch(content))
            {
                continue;
            }

            if (!path.StartsWith(FunctionsRoot, StringComparison.Ordinal))
            {
                findings.Add($"{path}: a Functions project must live under src/server/functions/<Name>/ so every src/server/** ArchTest covers it (ADR-052 §5)");
            }

            if (BffProjectReference.IsMatch(content))
            {
                findings.Add($"{path}: a Functions project must not reference Sprk.Bff.Api — shared logic lives in src/server/shared/* (ADR-052 §6)");
            }
        }

        return findings;
    }

    internal static IReadOnlyList<string> CheckFunctionsIdentity(IEnumerable<(string Path, string Content)> sources)
    {
        var findings = new List<string>();
        foreach (var (path, content) in sources)
        {
            if (!path.StartsWith(FunctionsRoot, StringComparison.Ordinal))
            {
                continue;
            }

            var code = SourceScan.CodeText(content.Split('\n'));
            foreach (Match match in ConfidentialClientUse.Matches(code))
            {
                findings.Add(
                    $"{path}:{SourceScan.LineOf(code, match.Index)}: {match.Value} — a Function authenticates app-only as the " +
                    "stamp's managed identity (ADR-028 A4 app-only row); it never builds a confidential client, presents a " +
                    "managed-identity assertion or performs OBO, which is how it would act as the BFF app registration (ADR-052 §6)");
            }
        }

        return findings;
    }

    private static string WorkerProject(string extraItems)
        => @"<Project Sdk=""Microsoft.NET.Sdk""><ItemGroup>" +
           @"<PackageReference Include=""Microsoft.Azure.Functions.Worker"" Version=""2.0.0"" />" +
           extraItems +
           "</ItemGroup></Project>";
}

// ---------------------------------------------------------------------------------------------------
// Control fixtures for WorkloadPlacementGuardTests. Top-level so NetArchTest can select them by name.
// ---------------------------------------------------------------------------------------------------

/// <summary>Stand-in base type for the ratchet controls, so they need no reference to the real BackgroundService.</summary>
internal abstract class Adr052FakeBackgroundService
{
}

internal class Adr052SeededTimerService : Adr052FakeBackgroundService
{
}

internal sealed class Adr052SeededDerivedService : Adr052SeededTimerService
{
}

internal sealed class Adr052NotAHostedService
{
}

internal sealed class Adr052SeededCoupledJob : IScheduledJob
{
    private readonly ScheduledJobRegistry _registry;

    public Adr052SeededCoupledJob(ScheduledJobRegistry registry) => _registry = registry;

    public string JobId => "adr052-seeded-coupled";

    public string DisplayName => "Seeded job coupled to the scheduler registry";

    public string Description => _registry.GetType().Name;

    public Task<JobRunResult> ExecuteAsync(JobRunContext context, CancellationToken cancellationToken)
        => Task.FromResult(new JobRunResult(true, null, 0, TimeSpan.Zero));
}

internal sealed class Adr052SanctionedJob : IScheduledJob
{
    public string JobId => "adr052-sanctioned";

    public string DisplayName => "Host-neutral job";

    public string Description => "Depends on nothing from the scheduler host.";

    public Task<JobRunResult> ExecuteAsync(JobRunContext context, CancellationToken cancellationToken)
        => Task.FromResult(new JobRunResult(true, null, 0, TimeSpan.Zero));
}
