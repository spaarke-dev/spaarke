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
///   another host without being rewritten — nor on the dispatch lease, which belongs to the host.</item>
///   <item><b>One registration path</b> (ADR-036 A1 rule 6, task 103) — only the scheduling module and the admin
///   surface touch <c>ScheduledJobRegistry</c> / <c>InMemoryBackgroundJobStore</c>; every job registers with
///   <c>AddScheduledJob&lt;TJob&gt;</c>, so no per-job bootstrap class comes back.</item>
///   <item><b>Functions projects</b> (ADR-052 §5–§6) — a project that hosts Azure Functions or a Durable Task
///   worker lives under <c>src/server/functions/</c> (so the <c>src/server/**</c> ArchTests — credential guards,
///   tenant-isolation invariants — cover it), never references <c>Sprk.Bff.Api</c>, and authenticates app-only as
///   the stamp's managed identity: no confidential client, client-assertion or certificate credential, or OBO — the
///   ways a Function would act as the BFF app registration. Dataverse impersonation of the user who started the work
///   is permitted (owner-accepted 2026-09-15, ADR-052 §6) but only through the shared <c>DataverseImpersonation</c>
///   helper, so the raw headers stay banned.</item>
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
    //      OtherBackgroundServices, with a reason and an ADR citation, and OtherServiceBaseline raised in the
    //      same PR so the reviewer sees the addition.
    //
    //   2. ExistingTimerServices ONLY SHRINKS. When a timer service migrates to IScheduledJob (ADR-052 §1:
    //      "when next touched" — any PR changing its behaviour or its timer loop), delete its entry AND lower
    //      TimerServiceBaseline to match. Never add an entry; never raise the baseline.
    //
    //   3. An entry naming a class that no longer exists fails InventoryHasNoStaleEntries — delete it and lower
    //      its baseline. A stale entry is a hole the next class of that name walks through.
    //
    //   4. Keys are simple type names; two BackgroundService classes sharing a simple name fail the inventory test
    //      rather than silently sharing an entry.
    //
    //   Known limits: a timer on a direct IHostedService (System.Threading.Timer), or a BackgroundService in a
    //   shared library the BFF registers, is not inventoried — none exists today (task-102 code review). Review
    //   catches those; widen the scan if one appears.
    // =============================================================================================
    private const int TimerServiceBaseline = 14;

    private const int OtherServiceBaseline = 9;

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
        ["UploadFinalizationWorker"] = "Office queue consumer with a bespoke message shape — a named non-conforming consumer (ADR-004 A1 §6, #977).",
        ["ProfileSummaryWorker"] = "Office queue consumer with a bespoke message shape — a named non-conforming consumer (ADR-004 A1 §6, #977).",
        ["IndexingWorkerHostedService"] = "Office queue consumer with a bespoke message shape — a named non-conforming consumer (ADR-004 A1 §6, #977, #978).",
        ["MembershipJunctionUpdaterHost"] = "Topic consumer for sprk-membership-changes — a named non-conforming consumer (ADR-004 A1 §6, ADR-034).",
        ["NullMembershipJunctionUpdaterHost"] = "Null-Object stand-in for MembershipJunctionUpdaterHost when the topic is not configured; does no work (ADR-032).",
        ["BulkOperationService"] = "In-process work processor for SPE bulk operations queued by BulkOperationEndpoints — not a timer, so outside the ADR-052 §1 ban; listed so a new processor needs a reason.",
        ["EmbeddingMigrationService"] = "One-shot embedding migration started at host startup, batched with back-off delays — ADR-052 §1's once-at-startup shape, not a timer.",
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

        var shared = discovered.GroupBy(n => n, StringComparer.Ordinal).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        Assert.True(
            shared.Count == 0,
            "Two BackgroundService classes share a simple name, so they would share one inventory entry: " + string.Join(", ", shared));

        var unlisted = FindUnlisted(discovered, ExistingTimerServices, OtherBackgroundServices);
        Assert.True(
            unlisted.Count == 0,
            "New BackgroundService subclasses in Sprk.Bff.Api: " + string.Join(", ", unlisted) + ". Scheduled work in " +
            "the BFF is an IScheduledJob on ScheduledJobHost (ADR-036); queue work is an IJobHandler behind " +
            "ServiceBusJobProcessor (ADR-004); where the work runs at all is ADR-052. No new hand-rolled timer " +
            "BackgroundService (ADR-052 §1). See the maintenance procedure in WorkloadPlacementGuardTests.cs.");
    }

    [Fact(DisplayName = "ADR-052 §1: the inventory only shrinks, matches its baselines, and names no class that no longer exists")]
    public void InventoryHasNoStaleEntries()
    {
        var discovered = BackgroundServiceTypes(ADR001_MinimalApiTests.LoadableTypes(typeof(Program).Assembly), BackgroundServiceFullName)
            .Select(t => t.Name)
            .ToHashSet(StringComparer.Ordinal);

        var stale = ExistingTimerServices.Keys.Concat(OtherBackgroundServices.Keys).Where(n => !discovered.Contains(n)).ToList();
        Assert.True(stale.Count == 0, "Inventory entries naming no BFF BackgroundService — delete them and lower the matching baseline: " + string.Join(", ", stale));

        // Equality, not <=: deleting an entry without lowering the baseline would leave headroom for a future add.
        Assert.True(
            ExistingTimerServices.Count == TimerServiceBaseline,
            $"ExistingTimerServices has {ExistingTimerServices.Count} entries but TimerServiceBaseline is {TimerServiceBaseline}. " +
            "The list only shrinks: after a migration, lower the baseline to match; never add a timer — write an IScheduledJob.");
        Assert.True(
            OtherBackgroundServices.Count == OtherServiceBaseline,
            $"OtherBackgroundServices has {OtherBackgroundServices.Count} entries but OtherServiceBaseline is {OtherServiceBaseline}. " +
            "Change both in the same PR, with a reason for the entry.");
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
    // ---------------------------------------------------------------------------------------------
    // MAINTENANCE PROCEDURE: a failure means a job took a dependency on the scheduler's host, store or registry.
    // Move that dependency out — registration belongs in AddScheduledJob<TJob> (ADR-036 A1 rule 6), run state in
    // JobRunContext / JobRunResult. Do not add an exemption: host-neutrality is what lets a job move to another host
    // (ADR-052 §5). The ">= 3 jobs" floor exists so the rule cannot pass by selecting nothing; if jobs are
    // legitimately removed below three, lower the floor in the same PR and say why.
    // =============================================================================================
    private static readonly string[] SchedulerHostTypes =
    {
        "Spaarke.Scheduling.ScheduledJobHost",
        "Spaarke.Scheduling.ScheduledJobHostOptions",
        "Spaarke.Scheduling.IBackgroundJobStore",
        "Spaarke.Scheduling.InMemoryBackgroundJobStore",
        "Spaarke.Scheduling.ScheduledJobRegistry",
        "Spaarke.Scheduling.ScheduledJobRegistration",
        // The lease belongs to the host, never the job (task 103; ADR-036 A1 rule 1).
        "Spaarke.Scheduling.IScheduledJobLease",
        "Spaarke.Scheduling.ProcessLocalScheduledJobLease",
        "Sprk.Bff.Api.Infrastructure.Scheduling.RedisScheduledJobLease",
    };

    [Fact(DisplayName = "ADR-052 §5 / ADR-036 A1: no IScheduledJob depends on the scheduler host, store or registry")]
    public void ScheduledJobsAreHostNeutral()
    {
        var assembly = typeof(Program).Assembly;

        var jobs = Types.InAssembly(assembly).That().ImplementInterface(typeof(IScheduledJob)).GetTypes().ToList();
        Assert.True(
            jobs.Count >= 3,
            $"Found {jobs.Count} IScheduledJob implementations in the BFF — expected at least 3 (PlaybookSchedulerJob, " +
            "MembershipReconciliationJob, GrantExpiryReminderJob), so the rule would pass by selecting nothing. If a job " +
            "was legitimately removed, lower the floor in the same PR.");

        var result = Types.InAssembly(assembly)
            .That().ImplementInterface(typeof(IScheduledJob))
            .ShouldNot().HaveDependencyOnAny(SchedulerHostTypes)
            .GetResult();

        Assert.True(
            result.IsSuccessful,
            "IScheduledJob implementations must not depend on ScheduledJobHost, IBackgroundJobStore, " +
            "ScheduledJobRegistry, ScheduledJobRegistration or the dispatch lease (ADR-036 A1 rule 7, ADR-052 §5). Registration belongs in AddScheduledJob<TJob>, " +
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
    // RULE 2b — ONE REGISTRATION PATH: AddScheduledJob<TJob> (ADR-036 A1 rule 6, task 103)
    // ---------------------------------------------------------------------------------------------
    // MAINTENANCE PROCEDURE: a failure names a BFF type that depends on ScheduledJobRegistry or
    // InMemoryBackgroundJobStore. A per-job bootstrap needs one of them — to register its handler or seed its
    // definition — which is exactly what task 103 deleted. Register the job with
    // services.AddScheduledJob<TJob>(cron, enabled) instead; the registry and store fill themselves from it.
    // Add a type to RegistrationSurfaceUsers only if it is part of the scheduling framework or the admin surface,
    // with a reason. A stale entry (the type no longer touches the surface) also fails: delete it.
    // =============================================================================================
    private static readonly string[] SchedulerRegistrationSurface =
    {
        "Spaarke.Scheduling.ScheduledJobRegistry",
        "Spaarke.Scheduling.InMemoryBackgroundJobStore",
    };

    private static readonly IReadOnlyDictionary<string, string> RegistrationSurfaceUsers =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Sprk.Bff.Api.Infrastructure.DI.SchedulingModule"] =
                "Registers the framework singletons that AddScheduledJob feeds (ADR-036).",
            ["Sprk.Bff.Api.Api.Admin.JobsEndpoints"] =
                "The admin surface lists, inspects and triggers registered jobs (ADR-036 admin endpoints).",
        };

    [Fact(DisplayName = "ADR-036 A1 rule 6: no per-job bootstrap — only the scheduling module and the admin surface touch the registry or store")]
    public void ScheduledJobsRegisterThroughAddScheduledJobOnly()
    {
        var users = RegistrationSurfaceUsersIn(typeof(Program).Assembly);

        var unsanctioned = users.Where(u => !RegistrationSurfaceUsers.ContainsKey(u)).ToList();
        Assert.True(
            unsanctioned.Count == 0,
            "These BFF types depend on ScheduledJobRegistry or InMemoryBackgroundJobStore — a per-job bootstrap. Register " +
            "the job with services.AddScheduledJob<TJob>(cron, enabled) instead (ADR-036 A1 rule 6): " +
            string.Join(", ", unsanctioned));

        var stale = RegistrationSurfaceUsers.Keys.Where(k => !users.Contains(k)).ToList();
        Assert.True(
            stale.Count == 0,
            "Stale RegistrationSurfaceUsers entries — these types no longer touch the registry or store; delete them: " +
            string.Join(", ", stale));
    }

    [Fact(DisplayName = "ADR-036 A1 rule 6: negative control — a per-job bootstrap is flagged")]
    public void RegistrationPath_NegativeControl_FlagsAPerJobBootstrap()
    {
        var users = RegistrationSurfaceUsersIn(typeof(WorkloadPlacementGuardTests).Assembly, nameof(Adr036SeededPerJobBootstrap));

        Assert.Equal(new[] { typeof(Adr036SeededPerJobBootstrap).FullName! }, users);
        Assert.False(RegistrationSurfaceUsers.ContainsKey(users.Single()));
    }

    [Fact(DisplayName = "ADR-036 A1 rule 6: positive control — a job that only implements IScheduledJob is not flagged")]
    public void RegistrationPath_PositiveControl_PassesAPlainJob()
    {
        var users = RegistrationSurfaceUsersIn(typeof(WorkloadPlacementGuardTests).Assembly, nameof(Adr052SanctionedJob));

        Assert.Empty(users);
    }

    /// <summary>Top-level names of the types in <paramref name="assembly"/> that depend on the registration surface
    /// (nested and compiler-generated types — lambdas, async state machines — count as their declaring type).</summary>
    private static IReadOnlyList<string> RegistrationSurfaceUsersIn(System.Reflection.Assembly assembly, string? onlyTypeName = null)
        => Types.InAssembly(assembly)
            .That().HaveDependencyOnAny(SchedulerRegistrationSurface)
            .GetTypes()
            .Where(t => onlyTypeName is null || t.Name == onlyTypeName)
            .Select(t => (t.FullName ?? t.Name).Split('+')[0])
            .Distinct(StringComparer.Ordinal)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();

    // =============================================================================================
    // RULE 3 — FUNCTIONS PROJECTS: location, references, identity
    // ---------------------------------------------------------------------------------------------
    // MAINTENANCE PROCEDURE
    //   1. A location failure: move the project under src/server/functions/<Name>/ — that is what brings it under
    //      the src/server/** ArchTests (ADR-052 §5). Do not special-case another path.
    //   2. A BFF-reference failure: extract the shared logic to src/server/shared/* (ADR-052 §6 Code row).
    //   3. An identity failure: a Function authenticates app-only as the stamp's managed identity (ADR-052 §6).
    //      The ONLY exception is an owner-approved dedicated identity, or access bound to another app registration
    //      (ADR-052 §6, §10). Record it in OwnerApprovedIdentityExceptions, keyed by the file's repo-relative path,
    //      with the approval date, the approver and the reason. Never widen the banned-type list's escape hatch in
    //      any other way.
    //   4. Detection covers the isolated worker, the in-process SDK, WebJobs and a standalone Durable Task worker,
    //      in any .csproj or Directory.*.props / .targets under the repository. A new hosting SDK means a new
    //      prefix in FunctionsHostPackage, with a control sample.
    // =============================================================================================
    private const string FunctionsRoot = "src/server/functions/";

    private static readonly Regex FunctionsHostPackage = new(
        @"<PackageReference\b[^>]*\b(?:Include|Update)\s*=\s*[""'](?:Microsoft\.Azure\.Functions\.Worker|Microsoft\.NET\.Sdk\.Functions|Microsoft\.Azure\.WebJobs|Microsoft\.DurableTask\.Worker)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex BffProjectReference = new(
        @"<(?:ProjectReference|Reference)\b[^>]*\bInclude\s*=\s*[""'][^""']*Sprk\.Bff\.Api(?:\.csproj)?[""']",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>
    /// The types a Function would need to act as an app registration (ADR-028 A4's confidential-client row), and the
    /// raw impersonation headers and SDK properties that would let it act as a user WITHOUT the shared fail-closed
    /// helper. The stamp's UAMI can mint the MI-FIC assertion for the BFF app registration and can send the headers,
    /// so the rule is enforced here rather than trusted. <c>CredentialCensusTests</c> would also see a new
    /// confidential client, but a reasoned census entry could admit it without anyone noticing that ADR-052 §6
    /// forbids it in a Function.
    /// </summary>
    /// <remarks>
    /// Impersonation itself is permitted in a Function since 2026-09-15 (owner-accepted, ADR-052 §6 / ADR-028 A5):
    /// only for work a user started through the BFF, and only through <c>Spaarke.Dataverse.DataverseImpersonation</c>.
    /// That helper is therefore not banned — the raw <c>MSCRMCallerID</c> / <c>CallerObjectId</c> headers and the
    /// ServiceClient's <c>CallerAADObjectId</c> are, because each bypasses the helper's refusal of an empty id.
    /// </remarks>
    private static readonly Regex ConfidentialClientUse = new(
        @"\b(?:ConfidentialClientApplicationBuilder|ManagedIdentityClientAssertion|WithClientAssertion|AcquireTokenOnBehalfOf|OnBehalfOfCredential|ClientAssertionCredential|ClientCertificateCredential|ClientSecretCredential|MSCRMCallerID|CallerObjectId|CallerAADObjectId)\b",
        RegexOptions.CultureInvariant);

    /// <summary>
    /// Owner-approved exceptions to the identity rule (ADR-052 §6 / §10), keyed by repo-relative path. Each value
    /// must name the approval (date, approver) and the reason. Empty: no Function exists yet.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> OwnerApprovedIdentityExceptions =
        new Dictionary<string, string>(StringComparer.Ordinal);

    [Fact(DisplayName = "ADR-052 §5-§6: every Functions project lives under src/server/functions/ and does not reference the BFF")]
    public void FunctionsProjectsAreWhereTheGuardsCanSeeThem()
    {
        var projectFiles = PlacementRepoFiles.Walk(SourceScan.RepoRoot)
            .Where(IsProjectOrBuildFile)
            .Select(f => (Path: PlacementRepoFiles.Relative(f), Content: File.ReadAllText(f)))
            .ToList();

        Assert.True(
            projectFiles.Count(p => p.Path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase)) > 5,
            "Found almost no .csproj files — the repository walk is broken.");

        var findings = CheckFunctionsProjects(projectFiles);
        Assert.True(findings.Count == 0, string.Join("\n", findings));
    }

    [Fact(DisplayName = "ADR-052 §6: code under src/server/functions/ authenticates app-only — no confidential client, credential or OBO; impersonation only through the shared helper")]
    public void FunctionsAuthenticateAppOnly()
    {
        // Until the first Function exists this scans nothing; the controls below are what prove the rule.
        var sources = PlacementRepoFiles.Walk(Path.Combine(SourceScan.RepoRoot, "src", "server", "functions"))
            .Where(f => f.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
            .Select(f => (Path: PlacementRepoFiles.Relative(f), Content: File.ReadAllText(f)));

        var findings = CheckFunctionsIdentity(sources, OwnerApprovedIdentityExceptions);
        Assert.True(findings.Count == 0, string.Join("\n", findings));
    }

    [Fact(DisplayName = "ADR-052 §6: every owner-approved identity exception names its approval and reason")]
    public void EveryIdentityExceptionIsExplained()
    {
        var unexplained = OwnerApprovedIdentityExceptions
            .Where(e => e.Value.Length < 40 || !e.Value.Contains("owner", StringComparison.OrdinalIgnoreCase) || !e.Value.Contains("ADR-052", StringComparison.Ordinal))
            .Select(e => e.Key)
            .ToList();

        Assert.True(unexplained.Count == 0, "Identity exceptions without an owner approval, a reason and an ADR-052 citation: " + string.Join(", ", unexplained));
    }

    [Fact(DisplayName = "ADR-052 §5-§6: negative control — misplaced projects, BFF references and forbidden credentials are each flagged")]
    public void FunctionsGuard_NegativeControl_FiresOnEachViolation()
    {
        var misplaced = CheckFunctionsProjects(new[]
        {
            ("src/server/api/Sprk.Sync/Sprk.Sync.csproj", HostProject(@"<PackageReference Include=""Microsoft.Azure.Functions.Worker"" Version=""2.0.0"" />", string.Empty)),
            ("functions/Sprk.Sync/Sprk.Sync.csproj", HostProject(@"<PackageReference Version=""4.4.0"" Include=""Microsoft.NET.Sdk.Functions"" />", string.Empty)),
            ("tools/Sprk.Orchestrator/Sprk.Orchestrator.csproj", HostProject(@"<PackageReference Include='Microsoft.DurableTask.Worker' Version='1.5.0' />", string.Empty)),
            ("src/server/api/Directory.Build.props", HostProject(@"<PackageReference Update=""Microsoft.Azure.WebJobs.Extensions.ServiceBus"" Version=""5.0.0"" />", string.Empty)),
        });
        Assert.Equal(4, misplaced.Count(f => f.Contains("must live under src/server/functions/", StringComparison.Ordinal)));

        var bffReference = CheckFunctionsProjects(new[]
        {
            ("src/server/functions/Sprk.Sync/Sprk.Sync.csproj",
             HostProject(@"<PackageReference Include=""Microsoft.Azure.Functions.Worker"" Version=""2.0.0"" />", @"<ProjectReference Include=""..\..\api\Sprk.Bff.Api\Sprk.Bff.Api.csproj"" />")),
            ("src/server/functions/Sprk.Other/Sprk.Other.csproj",
             HostProject(@"<PackageReference Include=""Microsoft.Azure.Functions.Worker"" Version=""2.0.0"" />", @"<Reference Include=""Sprk.Bff.Api"" />")),
        });
        Assert.Equal(2, bffReference.Count(f => f.Contains("must not reference Sprk.Bff.Api", StringComparison.Ordinal)));

        var identity = CheckFunctionsIdentity(new[]
        {
            ("src/server/functions/Sprk.Sync/Auth.cs",
             "var app = ConfidentialClientApplicationBuilder.Create(id)\n    .WithClientAssertion(GetAssertionAsync)\n    .Build();\n" +
             "var obo = await app.AcquireTokenOnBehalfOf(scopes, userAssertion).ExecuteAsync();\n" +
             "var asApp = new ClientAssertionCredential(tenantId, bffAppId, ct => GetMiTokenAsync(ct));\n" +
             "var cert = new ClientCertificateCredential(tenantId, clientId, certificate);\n" +
             "request.Headers.Add(\"MSCRMCallerID\", systemUserId.ToString());\n" +
             "request.Headers.Add(\"CallerObjectId\", oid.ToString());\n" +
             "serviceClient.CallerAADObjectId = oid;\n"),
        });
        foreach (var banned in new[] { "ConfidentialClientApplicationBuilder", "WithClientAssertion", "AcquireTokenOnBehalfOf", "ClientAssertionCredential", "ClientCertificateCredential", "MSCRMCallerID", "CallerObjectId", "CallerAADObjectId" })
        {
            Assert.Contains(identity, f => f.Contains(banned, StringComparison.Ordinal));
        }
    }

    [Fact(DisplayName = "ADR-052 §6: positive control — impersonation through the shared fail-closed helper is not flagged")]
    public void FunctionsGuard_PositiveControl_ImpersonationThroughTheSharedHelperPasses()
    {
        // The owner-accepted rule (2026-09-15): a Function MAY impersonate the user who started the work, only
        // through Spaarke.Dataverse.DataverseImpersonation. The raw headers stay banned (negative control above).
        var findings = CheckFunctionsIdentity(new[]
        {
            ("src/server/functions/Sprk.Sync/Handler.cs",
             "using Spaarke.Dataverse;\n" +
             "DataverseImpersonation.Apply(request, message.Requester.ObjectId);\n"),
        });

        Assert.Empty(findings);
    }

    [Fact(DisplayName = "ADR-052 §5-§6: positive control — the sanctioned Functions project and app-only credential pass")]
    public void FunctionsGuard_PositiveControl_PassesTheSanctionedShape()
    {
        Assert.Empty(CheckFunctionsProjects(new[]
        {
            ("src/server/functions/Sprk.Sync/Sprk.Sync.csproj",
             HostProject(@"<PackageReference Include=""Microsoft.Azure.Functions.Worker"" Version=""2.0.0"" />", @"<ProjectReference Include=""..\..\shared\Spaarke.Core\Spaarke.Core.csproj"" />")),
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

        // An owner-approved exception exempts exactly its own file.
        var approved = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["src/server/functions/Sprk.Ciam/CiamGraph.cs"] = "Owner approval 2026-01-01 (test fixture): dedicated identity for CIAM Graph, ADR-052 §6.",
        };
        Assert.Empty(CheckFunctionsIdentity(
            new[] { ("src/server/functions/Sprk.Ciam/CiamGraph.cs", "var cert = new ClientCertificateCredential(t, c, x509);\n") },
            approved));
    }

    internal static IReadOnlyList<string> CheckFunctionsProjects(IEnumerable<(string Path, string Content)> projects)
    {
        var findings = new List<string>();
        foreach (var (path, content) in projects)
        {
            if (!FunctionsHostPackage.IsMatch(content))
            {
                continue;
            }

            if (!path.StartsWith(FunctionsRoot, StringComparison.Ordinal))
            {
                findings.Add($"{path}: a Functions / Durable Task host project must live under src/server/functions/<Name>/ so the src/server/** ArchTests cover it (ADR-052 §5)");
            }

            if (BffProjectReference.IsMatch(content))
            {
                findings.Add($"{path}: a Functions project must not reference Sprk.Bff.Api — shared logic lives in src/server/shared/* (ADR-052 §6)");
            }
        }

        return findings;
    }

    internal static IReadOnlyList<string> CheckFunctionsIdentity(
        IEnumerable<(string Path, string Content)> sources,
        IReadOnlyDictionary<string, string>? approvedExceptions = null)
    {
        var findings = new List<string>();
        foreach (var (path, content) in sources)
        {
            if (!path.StartsWith(FunctionsRoot, StringComparison.Ordinal)
                || (approvedExceptions is not null && approvedExceptions.ContainsKey(path)))
            {
                continue;
            }

            var code = SourceScan.CodeText(content.Split('\n'));
            foreach (Match match in ConfidentialClientUse.Matches(code))
            {
                findings.Add(
                    $"{path}:{SourceScan.LineOf(code, match.Index)}: {match.Value} — a Function authenticates app-only as the " +
                    "stamp's managed identity (ADR-028 A4 app-only row); it never builds a confidential client or credential " +
                    "for an app registration, performs OBO, or impersonates a Dataverse caller (ADR-052 §6). An owner-approved " +
                    "exception goes in OwnerApprovedIdentityExceptions.");
            }
        }

        return findings;
    }

    private static bool IsProjectOrBuildFile(string file)
    {
        var name = Path.GetFileName(file);
        return name.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase)
               || (name.StartsWith("Directory.", StringComparison.OrdinalIgnoreCase)
                   && (name.EndsWith(".props", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".targets", StringComparison.OrdinalIgnoreCase)));
    }

    private static string HostProject(string packageItem, string extraItems)
        => @"<Project Sdk=""Microsoft.NET.Sdk""><ItemGroup>" + packageItem + extraItems + "</ItemGroup></Project>";
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

/// <summary>What task 103 deleted three of: a startup hook that registers one job with the scheduler by hand.</summary>
internal sealed class Adr036SeededPerJobBootstrap
{
    private readonly ScheduledJobRegistry _registry;

    public Adr036SeededPerJobBootstrap(ScheduledJobRegistry registry) => _registry = registry;

    public void Register(IScheduledJob job) => _registry.Register(job);
}

internal sealed class Adr052SanctionedJob : IScheduledJob
{
    public string JobId => "adr052-sanctioned";

    public string DisplayName => "Host-neutral job";

    public string Description => "Depends on nothing from the scheduler host.";

    public Task<JobRunResult> ExecuteAsync(JobRunContext context, CancellationToken cancellationToken)
        => Task.FromResult(new JobRunResult(true, null, 0, TimeSpan.Zero));
}
