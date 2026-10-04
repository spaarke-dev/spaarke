using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Spaarke.Dataverse;
using Spaarke.Scheduling;
using Sprk.Bff.Api.Services.Access;
using Sprk.Bff.Api.Tests.AccessControl;
using Sprk.Bff.Api.Tests.DataMutation.ExternalAccess;
using Xunit;

namespace Sprk.Bff.Api.Tests.Integration.DataMutation.RecordOwnership;

/// <summary>
/// unified-access-control-r2 task 147 (C10 part 2, client and non-product writers): the L4 net for every child written
/// where no BFF code runs. That covers out-of-the-box forms, quick create, editable grids, imports, flows, and every client
/// writer still on <c>Xrm.WebApi</c>. Each such child is created owned by its creator, in the creator's business unit. The
/// secure-child reconciliation job's RECENT-CHANGES pass lists the child rows changed since its watermark. It finds the
/// secure records they sit under (the resolver's own upward walk) and reconciles those records first, in the same run,
/// whether or not the capped sweep window reaches them.
/// </summary>
/// <remarks>
/// Driven through the REAL job, the REAL reconciler, the REAL ownership resolver and the REAL share synchronizer, over the
/// in-memory Dataverse (<see cref="SecureChildShareWorld"/>), which evaluates every query, the <c>modifiedon</c> filter
/// included. Every test that expects a write also seeds a decoy and asserts that it is never written.
/// </remarks>
public class ClientChildFixUpTests
{
    private static readonly Guid SecureTeam = SecureChildShareWorld.SecureTeam;
    private static readonly Guid GeneralTeam = SecureChildShareWorld.GeneralTeam;
    private static readonly Guid Sharee = Guid.Parse("b0000000-0000-0000-0000-0000000000b2");

    /// <summary>Collaborate on the record, as a Manage Access share writes it (Read|Write|Append|AppendTo, no Share).</summary>
    private const int CollaborateMask = 1 | 2 | 4 | 16;

    private static DateTime MinutesAgo(int minutes) => DateTime.UtcNow.AddMinutes(-minutes);

    /// <summary>Three secure work assignments, ordered by id, so the sweep window (cap 1) is predictable.</summary>
    private static Guid[] OrderedRoots(int count) =>
        Enumerable.Range(0, count).Select(_ => Guid.NewGuid()).OrderBy(g => g).ToArray();

    /// <summary>
    /// AC (job watermark pass): a to-do created outside the product (user-owned, in the creator's business unit) under a
    /// secure record is re-owned to the Secure team in the NEXT run, with the record's sharee mirrored onto it. This holds
    /// even though the capped sweep window (one record) covers a different record in that run. The run's report names the
    /// record under <c>recentChanges</c> and lists the change with the row's previous owner. An ordinary record's
    /// user-owned to-do and an unfiled user-owned document, both changed in the same window, are never written.
    /// </summary>
    [Fact]
    public async Task ANonProductChildOfASecureRecord_IsReownedAndMirrored_InTheNextRun_EvenOutsideTheSweepWindow()
    {
        var job = new JobHarness();
        var roots = OrderedRoots(2);
        var (first, target) = (roots[0], roots[1]);
        var (todo, ordinaryProject, ordinaryTodo, unfiled) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        job.World.SecureRoot("sprk_workassignment", first).SecureRoot("sprk_workassignment", target)
            .OrdinaryRoot("sprk_project", ordinaryProject)
            .UserOwnedChild("sprk_todo", todo, ("sprk_regardingworkassignment", "sprk_workassignment", target))
            .Modified("sprk_todo", todo, MinutesAgo(3))
            .UserOwnedChild("sprk_todo", ordinaryTodo, ("sprk_regardingproject", "sprk_project", ordinaryProject))
            .Modified("sprk_todo", ordinaryTodo, MinutesAgo(3))
            .UserOwnedChild("sprk_document", unfiled)
            .Modified("sprk_document", unfiled, MinutesAgo(3));
        job.Shares.Seed("sprk_workassignment", target, DataversePrincipalRef.User(Sharee), CollaborateMask);

        var report = await job.RunAsync(writesEnabled: "true", maxRootsPerRun: "1");

        report.GetProperty("rootsInRun").GetInt32().Should().Be(1, "the sweep window is the first record only");
        report.GetProperty("resumeAfter").GetString().Should().Be($"sprk_workassignment:{first:D}");
        var recent = report.GetProperty("recentChanges");
        recent.GetProperty("rowsChanged").GetInt32().Should().Be(3, "the three changed rows not owned by the Secure team");
        recent.GetProperty("roots").EnumerateArray().Select(r => r.GetString())
            .Should().Equal($"sprk_workassignment:{target:D}");

        job.World.OwnerOf("sprk_todo", todo).Should().Be(DataversePrincipalRef.Team(SecureTeam),
            "a child created outside the product under a secure record is isolated within one run");
        job.Shares.MaskOf("sprk_todo", todo, DataversePrincipalRef.User(Sharee)).Should().Be(CollaborateMask,
            "the record's sharee keeps sight of it (task 149's mirror, run by the same pass)");
        var listed = report.GetProperty("changes").EnumerateArray().Single(c => c.GetProperty("id").GetGuid() == todo);
        listed.GetProperty("outcome").GetString().Should().Be(nameof(SecureChildRowOutcome.Changed));
        listed.GetProperty("previousOwner").GetString().Should().Be($"systemusers({SecureChildShareWorld.SomeUser:D})");

        job.World.OwnerOf("sprk_todo", ordinaryTodo).Should().Be(DataversePrincipalRef.User(SecureChildShareWorld.SomeUser),
            "a child of an ORDINARY record keeps today's ownership");
        job.World.OwnerOf("sprk_document", unfiled).Should().Be(DataversePrincipalRef.User(SecureChildShareWorld.SomeUser),
            "an unfiled document is unchanged");
        job.World.OwnerWrites.Should().NotContain(w => w.Id == ordinaryTodo || w.Id == unfiled);
        job.LastResult!.Success.Should().BeTrue();
    }

    /// <summary>
    /// AC (memo through an event): a memo created in Notepad or the event side pane, as the user, regarding an EVENT of a
    /// secure project is re-owned to the Secure team. It is found through the event, a hop the memo's own lookups do not
    /// name.
    /// </summary>
    [Fact]
    public async Task AMemoRegardingAnEventOfASecureProject_IsReownedThroughTheEvent()
    {
        var job = new JobHarness();
        var (project, @event, memo) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        job.World.SecureRoot("sprk_project", project)
            .SecureChild("sprk_event", @event, ("sprk_regardingproject", "sprk_project", project))
            .UserOwnedChild("sprk_memo", memo, ("sprk_regardingevent", "sprk_event", @event))
            .Modified("sprk_memo", memo, MinutesAgo(1));

        var report = await job.RunAsync(writesEnabled: "true", webApiNeeded: true);

        job.World.OwnerOf("sprk_memo", memo).Should().Be(DataversePrincipalRef.Team(SecureTeam));
        report.GetProperty("recentChanges").GetProperty("roots").EnumerateArray().Select(r => r.GetString())
            .Should().Equal($"sprk_project:{project:D}");
    }

    /// <summary>
    /// Report-only is the default (no configuration): the recent-changes pass plans the change (<c>wouldChange</c>, the
    /// row's current owner listed) and writes nothing. That holds for owners and for shares alike.
    /// </summary>
    [Fact]
    public async Task ReportOnly_PlansTheRecentChange_AndWritesNothing()
    {
        var job = new JobHarness();
        var (root, todo) = (Guid.NewGuid(), Guid.NewGuid());
        job.World.SecureRoot("sprk_workassignment", root)
            .UserOwnedChild("sprk_todo", todo, ("sprk_regardingworkassignment", "sprk_workassignment", root))
            .Modified("sprk_todo", todo, MinutesAgo(2));
        job.Shares.Seed("sprk_workassignment", root, DataversePrincipalRef.User(Sharee), CollaborateMask);

        var report = await job.RunAsync(writesEnabled: null);

        report.GetProperty("mode").GetString().Should().Be(SecureChildReconciliationJob.ModeReportOnly);
        report.GetProperty("wouldChange").GetInt32().Should().Be(1);
        job.World.OwnerWrites.Should().BeEmpty();
        job.Shares.WriteLog.Should().BeEmpty();
        job.World.OwnerOf("sprk_todo", todo).Should().Be(DataversePrincipalRef.User(SecureChildShareWorld.SomeUser));

        // A report-only run corrects nothing, so it does not move the watermark: the first run with writes on still
        // sees the change and corrects it (the same window, not only the next full sweep).
        var again = await job.RunAsync(writesEnabled: null);
        again.GetProperty("recentChanges").GetProperty("rowsChanged").GetInt32().Should().Be(1);

        await job.RunAsync(writesEnabled: "true");
        job.World.OwnerOf("sprk_todo", todo).Should().Be(DataversePrincipalRef.Team(SecureTeam));
    }

    /// <summary>
    /// The watermark moves: a run that completed sets it to its own start (minus the overlap), so the next run does not list
    /// a change the previous run already handled. A row already owned by the Secure team is never listed: it needs no move,
    /// and its shares belong to task 149's job, so active editing of isolated rows triggers no extra pass.
    /// </summary>
    [Fact]
    public async Task TheWatermarkAdvancesPastACompletedRun_AndIsolatedRowsAreNeverListed()
    {
        var job = new JobHarness();
        var (root, todo, isolated, ordinaryProject, ordinaryTodo, untouched) =
            (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        job.World.SecureRoot("sprk_workassignment", root).OrdinaryRoot("sprk_project", ordinaryProject)
            .UserOwnedChild("sprk_todo", todo, ("sprk_regardingworkassignment", "sprk_workassignment", root))
            .Modified("sprk_todo", todo, MinutesAgo(10))
            .SecureChild("sprk_event", isolated, ("sprk_regardingworkassignment", "sprk_workassignment", root))
            .Modified("sprk_event", isolated, MinutesAgo(10))
            // Stays user-owned (an ordinary record's child), so it is still listable on the second run if the
            // watermark did not move.
            .UserOwnedChild("sprk_todo", ordinaryTodo, ("sprk_regardingproject", "sprk_project", ordinaryProject))
            .Modified("sprk_todo", ordinaryTodo, MinutesAgo(10))
            // Never modified (no modifiedon): never listed.
            .UserOwnedChild("sprk_todo", untouched, ("sprk_regardingproject", "sprk_project", ordinaryProject));

        var first = await job.RunAsync(writesEnabled: "true");
        var second = await job.RunAsync(writesEnabled: "true");

        first.GetProperty("recentChanges").GetProperty("rowsChanged").GetInt32().Should().Be(2,
            "the two user-owned to-dos changed in the window; the Secure-team-owned event needs no move, and the to-do " +
            "that did not change is not listed");
        second.GetProperty("recentChanges").GetProperty("rowsChanged").GetInt32().Should().Be(0,
            "a change before the previous run's watermark is not listed again");
        job.World.OwnerOf("sprk_todo", todo).Should().Be(DataversePrincipalRef.Team(SecureTeam));
    }

    /// <summary>
    /// Fail closed (ADR-003): when the changed rows cannot be listed, the run reports the failure and is not a success, and
    /// its watermark stays where it was. The next run, with the fault cleared, looks at the SAME window and corrects the
    /// child. A record outside the sweep window is used, so only the recent-changes pass can reach it.
    /// </summary>
    [Fact]
    public async Task AListingThatFails_IsReported_AndTheWindowIsLookedAtAgainNextRun()
    {
        var job = new JobHarness();
        var roots = OrderedRoots(3);
        var target = roots[2];
        var todo = Guid.NewGuid();
        foreach (var root in roots)
            job.World.SecureRoot("sprk_workassignment", root);
        job.World.UserOwnedChild("sprk_todo", todo, ("sprk_regardingworkassignment", "sprk_workassignment", target))
            .Modified("sprk_todo", todo, MinutesAgo(10))
            .FailingQueriesOf("sprk_agreement");

        var failed = await job.RunAsync(writesEnabled: "true", maxRootsPerRun: "1");

        failed.GetProperty("recentChanges").GetProperty("failure").GetString().Should().NotBeNullOrEmpty();
        job.LastResult!.Success.Should().BeFalse();
        job.World.OwnerOf("sprk_todo", todo).Should().Be(DataversePrincipalRef.User(SecureChildShareWorld.SomeUser));

        job.World.ClearQueryFaults();
        var retried = await job.RunAsync(writesEnabled: "true", maxRootsPerRun: "1");

        retried.GetProperty("rootsInRun").GetInt32().Should().Be(1);
        retried.GetProperty("resumeAfter").GetString().Should().Be($"sprk_workassignment:{roots[1]:D}",
            "the sweep window is the second record, so the target is reached by the recent-changes pass alone");
        retried.GetProperty("recentChanges").GetProperty("failure").ValueKind.Should().Be(JsonValueKind.Null);
        job.World.OwnerOf("sprk_todo", todo).Should().Be(DataversePrincipalRef.Team(SecureTeam),
            "the failed run's window was looked at again, not skipped");
    }

    /// <summary>
    /// ADR-003, never a silent skip: a changed child under a record flagged secure whose provisioning did not complete
    /// (still owned by an ordinary team) cannot be placed. It is listed under <c>recentChanges.undetermined</c>, the run is
    /// not a success, and the child is not written. The flagged record lies OUTSIDE this run's sweep window, so only the
    /// recent-changes report can make the run fail.
    /// </summary>
    [Fact]
    public async Task AChangedChildUnderAFlaggedButNotIsolatedRecord_IsReportedUndetermined_AndNotWritten()
    {
        var job = new JobHarness();
        var roots = OrderedRoots(2);
        var (secure, flagged, todo) = (roots[0], roots[1], Guid.NewGuid());
        job.World.SecureRoot("sprk_workassignment", secure).FlaggedNotIsolatedRoot("sprk_workassignment", flagged)
            .UserOwnedChild("sprk_todo", todo, ("sprk_regardingworkassignment", "sprk_workassignment", flagged))
            .Modified("sprk_todo", todo, MinutesAgo(2));

        var report = await job.RunAsync(writesEnabled: "true", maxRootsPerRun: "1");

        report.GetProperty("incompleteRoots").GetArrayLength().Should().Be(0, "the sweep window held only the isolated record");
        var undetermined = report.GetProperty("recentChanges").GetProperty("undetermined").EnumerateArray()
            .Select(u => u.GetString()).ToList();
        undetermined.Should().ContainSingle().Which.Should().Contain(todo.ToString("D"));
        job.LastResult!.Success.Should().BeFalse();
        job.LastResult.ErrorMessage.Should().Contain(todo.ToString("D"));
        job.World.OwnerWrites.Should().NotContain(w => w.Id == todo);
    }

    /// <summary>
    /// A changed child whose re-own Dataverse refuses is reported: its record is incomplete, the row is listed Failed with
    /// its previous owner, and the run is not a success. It is never left user-owned without appearing in the report.
    /// </summary>
    [Fact]
    public async Task AChangedChildWhoseReownIsRefused_IsReportedFailed_AndTheRunIsNotASuccess()
    {
        var job = new JobHarness();
        var (root, todo) = (Guid.NewGuid(), Guid.NewGuid());
        job.World.SecureRoot("sprk_workassignment", root)
            .UserOwnedChild("sprk_todo", todo, ("sprk_regardingworkassignment", "sprk_workassignment", root))
            .Modified("sprk_todo", todo, MinutesAgo(2))
            .RefusingOwnerWritesOf(todo);

        var report = await job.RunAsync(writesEnabled: "true");

        report.GetProperty("failed").GetInt32().Should().Be(1);
        report.GetProperty("incompleteRoots").GetArrayLength().Should().Be(1);
        report.GetProperty("changes").EnumerateArray().Single(c => c.GetProperty("id").GetGuid() == todo)
            .GetProperty("outcome").GetString().Should().Be(nameof(SecureChildRowOutcome.Failed));
        job.LastResult!.Success.Should().BeFalse();
    }

    /// <summary>
    /// The job over a world, built as the scheduler builds it: ONE job instance (it keeps its cursor and its watermark),
    /// with the real reconciler and the real synchronizer resolved per run from a scope. Work-assignment roots cascade
    /// nothing on Assign, so the platform-cascade Web API client is never called. A project or matter root needs a Web
    /// API client that answers "no cascade rows" (<c>webApiNeeded</c>).
    /// </summary>
    private sealed class JobHarness
    {
        public SecureChildShareWorld World { get; } = SecureChildShareWorld.Standard();
        public FakeRecordShareTable Shares { get; } = new();
        public JobRunResult? LastResult { get; private set; }
        private readonly IConfigurationRoot _configuration = new ConfigurationBuilder().AddInMemoryCollection().Build();
        private SecureChildReconciliationJob? _job;
        private ServiceProvider? _provider;

        public async Task<JsonElement> RunAsync(string? writesEnabled, string? maxRootsPerRun = null, bool webApiNeeded = false)
        {
            if (_job is null)
            {
                var services = new ServiceCollection();
                services.AddSingleton(SecureChildShareWorld.EntitiesOver(() => World).Object);
                var webApi = webApiNeeded ? NoCascadeRowsWebApi.Create() : null;
                services.AddScoped(_ => SecureChildShareWorld.ReconcilerOver(() => World, Shares, webApi!));
                services.AddScoped(_ => SecureChildShareWorld.SynchronizerOver(() => World, Shares));
                _provider = services.BuildServiceProvider();
                _job = new SecureChildReconciliationJob(
                    _provider.GetRequiredService<IServiceScopeFactory>(), TimeProvider.System, _configuration,
                    NullLogger<SecureChildReconciliationJob>.Instance);
            }

            _configuration[SecureChildReconciliationJob.WritesEnabledConfigKey] = writesEnabled;
            _configuration[SecureChildReconciliationJob.MaxRootsPerRunConfigKey] = maxRootsPerRun;
            LastResult = await _job.ExecuteAsync(
                new JobRunContext(Guid.NewGuid(), "test", JobRunTrigger.ManualAdmin, new Dictionary<string, object>()),
                CancellationToken.None);

            using var doc = JsonDocument.Parse(LastResult.ResultJson!);
            return doc.RootElement.Clone();
        }
    }

    /// <summary>
    /// A Web API client double for a project or matter root: the platform's Assign-cascade tables (SharePoint document
    /// locations and documents) answer with no rows. Any write through it throws, because this test expects none.
    /// </summary>
    private static class NoCascadeRowsWebApi
    {
        public static DataverseWebApiClient Create()
        {
            var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Dataverse:ServiceUrl"] = "https://test.crm.dynamics.com",
                ["Graph:ManagedIdentity:Enabled"] = "true",
                ["API_APP_ID"] = "00000000-0000-0000-0000-0000000000aa",
                ["API_CLIENT_SECRET"] = "test-secret",
                ["TENANT_ID"] = "00000000-0000-0000-0000-0000000000bb",
            }).Build();
            var client = new Moq.Mock<DataverseWebApiClient>(
                Moq.MockBehavior.Strict, config, NullLogger<DataverseWebApiClient>.Instance, null!, null!);
            client
                .Setup(c => c.QueryAsync<Moq.It.IsAnyType>(
                    Moq.It.IsAny<string>(), Moq.It.IsAny<string>(), Moq.It.IsAny<string>(),
                    Moq.It.IsAny<int?>(), Moq.It.IsAny<int?>(), Moq.It.IsAny<CancellationToken>()))
                .Returns(new Moq.InvocationFunc(invocation =>
                {
                    var listType = typeof(List<>).MakeGenericType(invocation.Method.GetGenericArguments()[0]);
                    return typeof(Task).GetMethod(nameof(Task.FromResult))!.MakeGenericMethod(listType)
                        .Invoke(null, new[] { Activator.CreateInstance(listType) })!;
                }));
            return client.Object;
        }
    }
}
