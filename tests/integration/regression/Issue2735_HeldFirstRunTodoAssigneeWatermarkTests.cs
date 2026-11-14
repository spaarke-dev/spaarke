using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Spaarke.Scheduling;
using Sprk.Bff.Api.Services.Access;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.Jobs;
using Sprk.Bff.Api.Services.Signals;
using Sprk.Bff.Api.Tests.AccessControl;
using Sprk.Bff.Api.Tests.DataMutation.ExternalAccess;
using Xunit;
using Job = Sprk.Bff.Api.Services.Signals.TodoAssigneeOwnerReconciliationJob;

namespace Sprk.Bff.Api.Tests.Integration.Regression;

/// <summary>
/// #2735 (the #2709 pattern): <see cref="Job"/> computed its first window as <c>runStart - InitialLookback</c> (six hours)
/// and never stored it. A first run whose listing failed kept the watermark unset, so the next run looked back six hours from
/// its OWN start: the window slid forward, and a To Do changed between the two starts was never listed (only the nightly
/// recompute reached its rows). The fix stores the first window's start before the listing can fail.
/// <para>Over the in-memory Dataverse (<see cref="SecureChildShareWorld"/>) with the REAL job, ownership and core-ancestor
/// resolvers and reconciler, composed as <c>TodoAssigneeOwnerReconciliationJobTests</c> composes them, report-only. The
/// listing's <c>modifiedon</c> bound is read from the query the job sends. The arrange is a personal To Do and its assignee
/// link: a multi-record setup at a KEEP integration path.</para>
/// </summary>
public class Issue2735_HeldFirstRunTodoAssigneeWatermarkTests
{
    private static readonly Guid Creator = Guid.Parse("27350000-0000-4000-8000-0000000000a1");
    private static readonly Guid AssigneeContact = Guid.Parse("27350000-0000-4000-8000-0000000000c1");
    private static readonly Guid Todo = Guid.Parse("27350000-0000-4000-8000-000000000101");

    /// <summary>01:00 UTC: before the nightly hour, so a scheduled run is the watermark pass alone.</summary>
    private static readonly DateTimeOffset T0 = new(2026, 11, 12, 1, 0, 0, TimeSpan.Zero);

    private static readonly TimeSpan Gap = TimeSpan.FromMinutes(10);

    private readonly SecureChildShareWorld _world = SecureChildShareWorld.Standard().User(Creator, SecureChildShareWorld.GeneralBu);
    private readonly AssignedAccessTestDoubles.FakeAssignedAccessStore _links = new();
    private readonly FakeTimeProvider _clock = new(T0);

    [Fact]
    public async Task ExecuteAsync_AfterAFirstRunWhoseListingFailed_ListsFromTheFirstRunsWindowStart_AndReachesAToDoChangedInIt()
    {
        // Changed inside the first run's window, but before the second run's own look-back (T0 + Gap - 6 h).
        PersonalToDo(modified: T0 - Job.InitialLookback + Gap / 2);
        var job = NewJob();
        _world.FailingQueriesOf("sprk_todo");

        var first = await job.ExecuteAsync(Context(), CancellationToken.None);
        first.Success.Should().BeFalse("the listing failed, so the first run kept the watermark");
        Result(first).GetProperty("recentToDos").GetInt32().Should().Be(0);

        _world.ClearQueryFaults();
        _clock.Advance(Gap);
        var second = await job.ExecuteAsync(Context(), CancellationToken.None);

        LastListedSince().Should().Be((T0 - Job.InitialLookback).UtcDateTime,
            "the window the first run could not list is listed from its start, not from the second run's own look-back");
        Result(second).GetProperty("recentToDos").GetInt32().Should().Be(1, "the listing reaches the To Do changed in that window");
    }

    [Fact]
    public async Task ExecuteAsync_AfterAFirstRunThatListed_ListsFromThatRunsStartLessTheOverlap()
    {
        var job = NewJob();
        await job.ExecuteAsync(Context(), CancellationToken.None);
        _clock.Advance(Gap);

        await job.ExecuteAsync(Context(), CancellationToken.None);

        LastListedSince().Should().Be((T0 - Job.WatermarkOverlap).UtcDateTime, "a run that listed still moves the watermark forward");
    }

    // ── The world ──

    /// <summary>A personal To Do (filed under nothing), owned by its creator, assigned to the contact.</summary>
    private void PersonalToDo(DateTimeOffset modified)
    {
        _world.Add("sprk_todo", Todo, ("owninguser", new EntityReference("systemuser", Creator)),
            ("sprk_assignedto", new EntityReference("contact", AssigneeContact)));
        _world.Modified("sprk_todo", Todo, modified.UtcDateTime);
    }

    /// <summary>The <c>modifiedon</c> bound of the last To Do listing the job sent.</summary>
    private DateTime LastListedSince() =>
        (DateTime)_world.Queries
            .Last(q => q.EntityName == "sprk_todo" && q.Criteria.Conditions.Any(c => c.AttributeName == "modifiedon"))
            .Criteria.Conditions.Single(c => c.AttributeName == "modifiedon" && c.Operator == ConditionOperator.GreaterEqual)
            .Values.Single();

    // ── The job, as TodoAssigneeOwnerReconciliationJobTests composes it (report-only: no owner write is reached) ──

    private Job NewJob()
    {
        var entities = SecureChildShareWorld.EntitiesOver(() => _world);
        var configuration = SecureChildShareWorld.Configuration();
        var ownership = new RecordOwnershipResolver(entities.Object, configuration, NullLogger<RecordOwnershipResolver>.Instance);
        var synchronizer = new SecureChildShareSynchronizer(entities.Object, new FakeRecordShareTable(),
            SecureChildShareWorld.NobodyWalled(), configuration, NullLogger<SecureChildShareSynchronizer>.Instance);
        var reconciler = new SecureChildReconciler(entities.Object, ownership, synchronizer, null!, configuration,
            NullLogger<SecureChildReconciler>.Instance, new Sprk.Bff.Api.Tests.Services.Signals.GateSharingWorld().Sharing);
        var cores = new CoreAncestorResolver(entities.Object, SecureChildShareWorld.ColumnProbe(),
            NullLogger<CoreAncestorResolver>.Instance);

        var services = new ServiceCollection();
        services.AddSingleton(entities.Object);
        services.AddScoped(_ => AssignedAccessTestDoubles.Representation(_links));
        services.AddScoped(_ => reconciler);
        services.AddSingleton<IIdempotencyService>(new IdempotencyService(
            new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions())), NullLogger<IdempotencyService>.Instance));
        services.AddSingleton<IDistributedCache>(new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions())));

        return new Job(services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(), cores, ownership, _clock,
            configuration, NullLogger<Job>.Instance);
    }

    private static JobRunContext Context() =>
        new(Guid.NewGuid(), "corr-2735", JobRunTrigger.Scheduled, new Dictionary<string, object>());

    private static JsonElement Result(JobRunResult run) => JsonDocument.Parse(run.ResultJson!).RootElement;
}
