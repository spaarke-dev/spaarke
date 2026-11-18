using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Xrm.Sdk;
using Spaarke.Scheduling;
using Sprk.Bff.Api.Services.RecordCreation;
using Xunit;
using Directory = Sprk.Bff.Api.Tests.TestInfrastructure.OwnershipDirectory;

namespace Sprk.Bff.Api.Tests.Integration.DataMutation.RecordOwnership;

/// <summary>
/// field-mapping-server-write-path-r1 task 052c (spec FR-25, NFR-01): the repair pass ON DEMAND — one table, one <c>createdon</c>
/// window, started by an administrator. The same job, per-table claim, forward-only and product-principal rules and Repair mode as
/// the scheduled pass; the job checks the window against go-live itself; the scheduled pass's watermark, sweep cursor and retry
/// set are never touched; a scheduled tick or a persisted <c>ConfigJson</c> never switches the job into on-demand mode.
/// </summary>
/// <remarks>Same harness as the scheduled-pass tests (<c>PassWorld</c>): the real job, Repair, resolver and claim store; only the
/// Dataverse boundary is doubled. The admin route and its refusals are pinned in <c>RecordRepairPassTriggerAuthTests</c>.</remarks>
public sealed partial class RecordRepairPassJobTests
{
    private static readonly DateTimeOffset WindowFrom = PassWorld.GoLive;
    /// <summary>Ends before the scheduled pass's settle point (now minus two minutes), so the run is not clamped.</summary>
    private static readonly DateTimeOffset WindowTo = PassWorld.Now.AddMinutes(-10);

    // =====================================================================================
    // AC7 (must not fire): a valid run repairs, leaves the product's rows alone, and is idempotent
    // =====================================================================================

    [Fact]
    public async Task AnOnDemandRun_RepairsANativeRowInItsWindow_AndLeavesAProductPrincipalsRowAlone()
    {
        var native = _pass.NativeEvent(createdOn: PassWorld.GoLive.AddDays(2));
        var product = _pass.NativeEvent(createdOn: PassWorld.GoLive.AddDays(3), createdBy: PassWorld.BffIdentity);

        var result = await RunOnDemandAsync(OnDemand("sprk_event"));

        result.Success.Should().BeTrue(result.ErrorMessage);
        PassWorld.Count(result, "settleClamped").Should().Be(0, "a window that ends before the settle point is read whole");
        var write = World.Writes.Should().ContainSingle("one conditional write, for the native row only").Subject;
        write.Id.Should().Be(native);
        write.Fields["sprk_clientname"].Should().Be(RepairWorld.MatterClient, "the mapping");
        write.Fields["ownerid"].Should().Be(new EntityReference("team", Directory.ChildTeam), "the owner");
        World.Writes.Should().NotContain(w => w.Id == product);
        var scan = _pass.ScanQueries.Should().ContainSingle("one table, one scan").Subject;
        scan.EntityName.Should().Be("sprk_event");
        scan.Criteria.Conditions.Should().ContainSingle(c => c.AttributeName == "createdon" && c.Operator == Microsoft.Xrm.Sdk.Query.ConditionOperator.GreaterEqual)
            .Which.Values.Single().Should().Be(WindowFrom.UtcDateTime);
        scan.Criteria.Conditions.Should().ContainSingle(c => c.AttributeName == "createdon" && c.Operator == Microsoft.Xrm.Sdk.Query.ConditionOperator.LessThan)
            .Which.Values.Single().Should().Be(WindowTo.UtcDateTime);
        scan.Criteria.Conditions.Should().ContainSingle(c => c.AttributeName == "createdby" && c.Operator == Microsoft.Xrm.Sdk.Query.ConditionOperator.NotIn)
            .Which.Values.Should().Contain(PassWorld.BffIdentity);
    }

    [Fact]
    public async Task ASecondIdenticalOnDemandRun_WritesNothing()
    {
        _pass.NativeEvent(createdOn: PassWorld.GoLive.AddDays(2));
        var run = OnDemand("sprk_event");
        await RunOnDemandAsync(run);

        var second = await RunOnDemandAsync(run);

        second.Success.Should().BeTrue(second.ErrorMessage);
        World.Writes.Should().ContainSingle("Repair is fill-only: the second run finds nothing to repair");
        PassWorld.Count(second, "noOp").Should().Be(1);
    }

    [Fact]
    public async Task AnOnDemandRun_ReadsOnlyItsTable_AndOnlyItsWindow()
    {
        var inside = _pass.NativeEvent(createdOn: PassWorld.GoLive.AddDays(3));
        _pass.NativeEvent(createdOn: PassWorld.GoLive.AddDays(1));
        _pass.NativeRow("sprk_matter");
        var run = OnDemand("sprk_event", from: PassWorld.GoLive.AddDays(2), to: PassWorld.GoLive.AddDays(4));

        var result = await RunOnDemandAsync(run);

        result.Success.Should().BeTrue(result.ErrorMessage);
        _pass.ScanQueries.Select(q => q.EntityName).Should().Equal("sprk_event");
        World.Writes.Should().ContainSingle().Which.Id.Should().Be(inside);
    }

    [Fact]
    public async Task TheResultAndTheHeartbeat_NameTheModeTheTableAndTheWindow()
    {
        _pass.NativeEvent(createdOn: PassWorld.GoLive.AddDays(2));

        var result = await RunOnDemandAsync(OnDemand("sprk_event"));

        using var json = JsonDocument.Parse(result.ResultJson!);
        json.RootElement.GetProperty("mode").GetString().Should().Be(RecordRepairPassJob.ModeOnDemand);
        json.RootElement.GetProperty("table").GetString().Should().Be("sprk_event");
        json.RootElement.GetProperty("createdFrom").GetString().Should().Be(WindowFrom.ToString("O"));
        json.RootElement.GetProperty("createdTo").GetString().Should().Be(WindowTo.ToString("O"));
        json.RootElement.GetProperty("correlationId").GetString().Should().Be(PassWorld.CorrelationId);
        _pass.Log.Entries.Should().ContainSingle(e => e.Message.Contains("heartbeat"))
            .Which.Message.Should().Contain("mode=on_demand").And.Contain("table=sprk_event").And.Contain("repaired=1");
    }

    // =====================================================================================
    // AC3 (must fire): forward-only, asked by the job itself
    // =====================================================================================

    [Fact]
    public async Task AnOnDemandRunWhoseWindowStartsBeforeGoLive_ReadsAndWritesNothing_AndFails()
    {
        // Built against an earlier go-live than the job's (the route is not the only guard).
        _pass.NativeEvent(createdOn: PassWorld.GoLive.AddDays(-2));
        _pass.NativeEvent(createdOn: PassWorld.GoLive.AddHours(12));
        var run = OnDemand("sprk_event", from: PassWorld.GoLive.AddDays(-5), to: PassWorld.GoLive.AddDays(1),
            goLive: PassWorld.GoLive.AddDays(-10));

        var result = await RunOnDemandAsync(run);

        result.Success.Should().BeFalse();
        PassWorld.Status(result).Should().Be(RecordRepairPassJob.StatusError);
        World.Writes.Should().BeEmpty();
        _pass.ScanQueries.Should().BeEmpty("nothing is read for a window that starts before go-live");
        _pass.WhoAmICalls.Should().Be(0);
        _pass.Log.Entries.Should().Contain(e => e.Level == LogLevel.Error && e.Message.Contains(RecordRepairRunReason.BeforeGoLive));
    }

    [Fact]
    public async Task ARowInTheWindowWhoseCreatedOnReReadsBeforeGoLive_IsNotWritten()
    {
        // The scan reports the row inside the window; the row Repair reads was created before go-live.
        _pass.ScanIgnoresItsConditions = true;
        var id = _pass.NativeEvent(createdOn: PassWorld.GoLive.AddDays(-1));
        _pass.ScanReportsCreatedOn[id] = PassWorld.GoLive.AddDays(2);

        var result = await RunOnDemandAsync(OnDemand("sprk_event"));

        World.Writes.Should().BeEmpty();
        World.OwnerContexts.Should().BeEmpty("nothing is decided for a row outside the pass");
        PassWorld.Count(result, "skipped").Should().Be(1, "Repair's own re-check over the row it read refuses it");
    }

    [Fact]
    public async Task ARowTheScanReturnsFromBeforeGoLive_IsNotRepaired_AndTheRunIsNotClean()
    {
        _pass.ScanIgnoresItsConditions = true;
        _pass.NativeEvent(createdOn: PassWorld.GoLive.AddDays(-1));

        var result = await RunOnDemandAsync(OnDemand("sprk_event"));

        World.Writes.Should().BeEmpty();
        PassWorld.Count(result, "outsidePass").Should().Be(1);
        result.Success.Should().BeFalse();
    }

    [Fact]
    public async Task AnOnDemandRun_WithGoLiveUnset_ReadsNothing_AndFails()
    {
        _pass.NativeEvent(createdOn: PassWorld.GoLive.AddDays(2));
        var run = OnDemand("sprk_event");
        _pass.Settings[RecordRepairPassJob.GoLiveConfigKey] = null;

        var result = await RunOnDemandAsync(run);

        result.Success.Should().BeFalse("the administrator's run could not do what was asked");
        PassWorld.Status(result).Should().Be(RecordRepairPassJob.StatusNotConfigured);
        _pass.ScanQueries.Should().BeEmpty();
        World.Writes.Should().BeEmpty();
    }

    // =====================================================================================
    // AC4 (must fire): mode isolation — only an administrator's run carrying the route's value is on demand
    // =====================================================================================

    [Theory]
    [InlineData("a scheduled tick carrying the route's value")]
    [InlineData("a manual run whose ConfigJson names on-demand keys")]
    [InlineData("a manual run whose key holds text")]
    public async Task ARunThatIsNotAnAdministratorsOnDemandRun_IsTheScheduledPass(string shape)
    {
        var native = _pass.NativeEvent();
        var run = OnDemand("contact", from: PassWorld.GoLive.AddDays(1), to: PassWorld.GoLive.AddDays(2));
        var configJson = JsonSerializer.Serialize(new Dictionary<string, object>
        {
            [RecordRepairOnDemandRun.ParameterKey] = new { table = "contact", createdFrom = "2026-11-02T00:00:00Z", createdTo = "2026-11-03T00:00:00Z" },
            ["table"] = "contact",
            ["createdFrom"] = "2026-11-02T00:00:00Z",
            ["createdTo"] = "2026-11-03T00:00:00Z",
        });
        var (trigger, parameters) = shape switch
        {
            "a scheduled tick carrying the route's value" => (JobRunTrigger.Scheduled,
                new Dictionary<string, object> { [RecordRepairOnDemandRun.ParameterKey] = run, ["configJson"] = configJson }),
            "a manual run whose ConfigJson names on-demand keys" => (JobRunTrigger.ManualAdmin,
                new Dictionary<string, object> { ["configJson"] = configJson, ["jobId"] = RecordRepairPassJob.JobIdConstant }),
            _ => (JobRunTrigger.ManualAdmin,
                new Dictionary<string, object> { [RecordRepairOnDemandRun.ParameterKey] = configJson }),
        };

        var result = await _pass.Job().ExecuteAsync(
            new JobRunContext(Guid.NewGuid(), PassWorld.CorrelationId, trigger, parameters), CancellationToken.None);

        using var json = JsonDocument.Parse(result.ResultJson!);
        json.RootElement.GetProperty("mode").GetString().Should().Be(RecordRepairPassJob.ModeScheduled);
        _pass.ScanQueries.Select(q => q.EntityName).Should().Contain(["sprk_event", "contact"], "the scheduled pass sweeps every table");
        World.Writes.Should().ContainSingle().Which.Id.Should().Be(native, "the scheduled pass's own row, nothing outside it");
        _pass.State("sprk_event").Should().Contain("watermark", "the scheduled pass keeps its state");
    }

    [Fact]
    public void OnlyAManualRunCarryingTheTypedValue_IsOnDemand()
    {
        var run = OnDemand("sprk_event");
        var parameters = new Dictionary<string, object> { [RecordRepairOnDemandRun.ParameterKey] = run };

        RecordRepairPassJob.OnDemandRunOf(new JobRunContext(Guid.NewGuid(), "c", JobRunTrigger.ManualAdmin, parameters))
            .Should().BeSameAs(run);
        RecordRepairPassJob.OnDemandRunOf(new JobRunContext(Guid.NewGuid(), "c", JobRunTrigger.Scheduled, parameters)).Should().BeNull();
        RecordRepairPassJob.OnDemandRunOf(new JobRunContext(Guid.NewGuid(), "c", JobRunTrigger.OnStartup, parameters)).Should().BeNull();
    }

    // =====================================================================================
    // AC5 (must fire): the scheduled pass's state is untouched
    // =====================================================================================

    [Fact]
    public async Task AnOnDemandRun_LeavesTheScheduledPasssWatermarkSweepAndRetrySetUnchanged()
    {
        var kept = _pass.NativeEvent(createdOn: PassWorld.GoLive.AddDays(1));
        var native = _pass.NativeEvent(createdOn: PassWorld.GoLive.AddDays(2));
        var seeded = JsonSerializer.Serialize(new
        {
            watermark = PassWorld.GoLive.AddDays(5),
            sweepUntil = PassWorld.GoLive.AddDays(6),
            sweepAfter = Guid.Parse("a052c000-0000-4000-8000-000000000001"),
            retry = new Dictionary<Guid, int> { [kept] = 3 },
        }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        _pass.SeedState("sprk_event", seeded);

        var result = await RunOnDemandAsync(OnDemand("sprk_event"));

        result.Success.Should().BeTrue(result.ErrorMessage);
        World.Writes.Select(w => w.Id).Should().BeEquivalentTo([kept, native], "the run repaired its window");
        _pass.State("sprk_event").Should().Be(seeded, "the watermark, sweep cursor and retry set are not the on-demand run's");
        _pass.State("contact").Should().BeNull("an on-demand run writes no table's state");
    }

    [Fact]
    public async Task ARowAnOnDemandRunCannotFinish_IsReported_NotKept()
    {
        _pass.NativeEvent(createdOn: PassWorld.GoLive.AddDays(2));
        World.WriteFault = new TimeoutException("Test: the write faulted.");

        var result = await RunOnDemandAsync(OnDemand("sprk_event"));

        result.Success.Should().BeFalse();
        PassWorld.Count(result, "needsRetry").Should().Be(1);
        PassWorld.Count(result, "pendingRetry").Should().Be(0);
        _pass.State("sprk_event").Should().BeNull("the on-demand run keeps no retry set");
    }

    // =====================================================================================
    // AC6 (must fire): the per-table claim
    // =====================================================================================

    [Fact]
    public async Task WhileAnotherRunnerHoldsTheTable_AnOnDemandRunReadsAndWritesNothing_AndFails()
    {
        _pass.NativeEvent(createdOn: PassWorld.GoLive.AddDays(2));
        (await _pass.Claims.TryAcquireProcessingLockAsync(
            $"{RecordRepairPassJob.JobIdConstant}:sprk_event", "another-runner", TimeSpan.FromMinutes(15))).Should().BeTrue();

        var result = await RunOnDemandAsync(OnDemand("sprk_event"));

        World.Writes.Should().BeEmpty();
        _pass.ScanQueries.Should().BeEmpty();
        PassWorld.Count(result, "claimHeld").Should().Be(1);
        result.Success.Should().BeFalse();
    }

    // =====================================================================================
    // AC8: the row budget, paging, and continuing a truncated window (review F4)
    // =====================================================================================

    [Fact]
    public async Task AWindowWithMoreRowsThanOnePage_IsPagedInIdOrder_UntilTheWindowIsExhausted()
    {
        _pass.Settings[RecordRepairPassJob.MaxRowsPerTableConfigKey] = "2";
        var ids = Enumerable.Range(2, 5).Select(day => _pass.NativeEvent(createdOn: PassWorld.GoLive.AddDays(day))).Order().ToList();

        var result = await RunOnDemandAsync(OnDemand("sprk_event"));

        result.Success.Should().BeTrue(result.ErrorMessage);
        PassWorld.Count(result, "scanned").Should().Be(5, "every row of the window is reached, not just the first page");
        PassWorld.Count(result, "truncated").Should().Be(0);
        World.Writes.Select(w => w.Id).Should().BeEquivalentTo(ids);
        var queries = _pass.ScanQueries.Where(q => q.EntityName == "sprk_event").ToList();
        queries.Should().HaveCount(3, "pages of two, two and one");
        AfterIdOf(queries[0]).Should().BeNull();
        AfterIdOf(queries[1]).Should().Be(ids[1], "the second page continues after the last id of the first");
        AfterIdOf(queries[2]).Should().Be(ids[3]);
        _pass.State("sprk_event").Should().BeNull("paging an on-demand window keeps no state");
    }

    [Fact]
    public async Task AWindowWithMoreRowsThanTheBudget_StopsAtTheBudget_AndNamesTheLastIdCovered()
    {
        _pass.Settings[RecordRepairPassJob.MaxRowsPerTableConfigKey] = "2";
        _pass.OnDemandRowBudget = 3;
        var ids = Enumerable.Range(2, 5).Select(day => _pass.NativeEvent(createdOn: PassWorld.GoLive.AddDays(day))).Order().ToList();

        var result = await RunOnDemandAsync(OnDemand("sprk_event"));

        PassWorld.Count(result, "truncated").Should().Be(1);
        PassWorld.Count(result, "scanned").Should().Be(3, "one run reads at most its budget, across pages");
        result.Success.Should().BeFalse("the administrator continues the window");
        World.Writes.Select(w => w.Id).Should().BeEquivalentTo(ids.Take(3));
        using var json = JsonDocument.Parse(result.ResultJson!);
        json.RootElement.GetProperty("lastIdCovered").GetString().Should().Be(ids[2].ToString("D"));
        result.ErrorMessage.Should().Contain("Narrow the window").And.Contain($"afterId {ids[2]:D}");
        _pass.Log.Entries.Should().ContainSingle(e => e.Message.Contains("heartbeat"))
            .Which.Message.Should().Contain($"lastIdCovered={ids[2]:D}");
        _pass.ScanQueries.Count(q => q.EntityName == "sprk_event").Should().Be(2, "a page of two, then the one row the budget leaves");
    }

    [Fact]
    public async Task AWindowWithMoreRowsThanTheBudgetOnOnePage_ReportsTruncated_AndFails()
    {
        _pass.Settings[RecordRepairPassJob.MaxRowsPerTableConfigKey] = "1";
        _pass.OnDemandRowBudget = 1;
        _pass.NativeEvent(createdOn: PassWorld.GoLive.AddDays(2));
        _pass.NativeEvent(createdOn: PassWorld.GoLive.AddDays(3));

        var result = await RunOnDemandAsync(OnDemand("sprk_event"));

        PassWorld.Count(result, "truncated").Should().Be(1);
        PassWorld.Count(result, "scanned").Should().Be(1, "one run reads at most the budget");
        result.Success.Should().BeFalse("the administrator narrows the window");
        result.ErrorMessage.Should().Contain("Narrow the window");
    }

    [Fact]
    public async Task ARunContinuedWithTheLastIdCovered_ReadsOnlyTheRowsAfterIt_AndFinishesTheWindow()
    {
        _pass.Settings[RecordRepairPassJob.MaxRowsPerTableConfigKey] = "2";
        var ids = Enumerable.Range(2, 5).Select(day => _pass.NativeEvent(createdOn: PassWorld.GoLive.AddDays(day))).Order().ToList();
        _pass.OnDemandRowBudget = 3;
        await RunOnDemandAsync(OnDemand("sprk_event"));
        World.Writes.Select(w => w.Id).Should().BeEquivalentTo(ids.Take(3));
        _pass.ScanQueries.Clear();
        _pass.OnDemandRowBudget = null;

        var second = await RunOnDemandAsync(OnDemand("sprk_event", afterId: ids[2]));

        second.Success.Should().BeTrue(second.ErrorMessage);
        PassWorld.Count(second, "scanned").Should().Be(2, "only the rows after the id");
        World.Writes.Select(w => w.Id).Should().BeEquivalentTo(ids, "the whole window is repaired across the two runs, each row once");
        AfterIdOf(_pass.ScanQueries.First(q => q.EntityName == "sprk_event")).Should().Be(ids[2]);
        using var json = JsonDocument.Parse(second.ResultJson!);
        json.RootElement.GetProperty("afterId").GetString().Should().Be(ids[2].ToString("D"));
    }

    // =====================================================================================
    // Review F4: the scan stops where the scheduled pass stops (now minus the settle time)
    // =====================================================================================

    [Fact]
    public async Task AWindowEndingAtNow_ScansOnlyToTheSettlePoint_AndSaysSo()
    {
        var settled = _pass.NativeEvent(createdOn: PassWorld.Now.AddMinutes(-3));
        var settling = _pass.NativeEvent(createdOn: PassWorld.Now.AddSeconds(-90));
        var run = OnDemand("sprk_event", from: PassWorld.Now.AddHours(-1), to: PassWorld.Now);

        var result = await RunOnDemandAsync(run);

        World.Writes.Should().ContainSingle().Which.Id.Should().Be(settled);
        World.Writes.Should().NotContain(w => w.Id == settling, "a row still being written by its creator is left");
        var scan = _pass.ScanQueries.Should().ContainSingle().Subject;
        var until = PassWorld.Now - RecordRepairPassJob.SettleTime;
        scan.Criteria.Conditions.Should().ContainSingle(c => c.AttributeName == "createdon" && c.Operator == Microsoft.Xrm.Sdk.Query.ConditionOperator.LessThan)
            .Which.Values.Single().Should().Be(until.UtcDateTime);
        PassWorld.Count(result, "settleClamped").Should().Be(1);
        result.Success.Should().BeFalse("the window's last minutes were not read; the administrator runs them later");
        result.ErrorMessage.Should().Contain("run the rest of the window again later");
        using var json = JsonDocument.Parse(result.ResultJson!);
        json.RootElement.GetProperty("scanUntil").GetString().Should().Be(until.ToString("O"));
    }

    [Fact]
    public async Task AWindowEntirelyInsideTheSettleTime_ReadsNothing_AndFails()
    {
        _pass.NativeEvent(createdOn: PassWorld.Now.AddSeconds(-90));
        var run = OnDemand("sprk_event", from: PassWorld.Now.AddSeconds(-100), to: PassWorld.Now);

        var result = await RunOnDemandAsync(run);

        World.Writes.Should().BeEmpty();
        _pass.ScanQueries.Should().BeEmpty();
        PassWorld.Count(result, "settleClamped").Should().Be(1);
        result.Success.Should().BeFalse();
    }

    private static Guid? AfterIdOf(Microsoft.Xrm.Sdk.Query.QueryExpression query) =>
        query.Criteria.Conditions
            .SingleOrDefault(c => c.AttributeName == "sprk_eventid" && c.Operator == Microsoft.Xrm.Sdk.Query.ConditionOperator.GreaterThan)
            ?.Values.Single() as Guid?;

    // =====================================================================================
    // The contact table waits for the customer team, as in the scheduled pass
    // =====================================================================================

    [Fact]
    public async Task AnOnDemandContactRun_WhoseCustomerTeamCannotBeResolved_ReadsNothing_AndFails()
    {
        _pass.CustomerTeamUnresolvable();
        _pass.NativeContact();

        var result = await RunOnDemandAsync(OnDemand("contact"));

        World.Writes.Should().BeEmpty();
        _pass.ScanQueries.Should().BeEmpty();
        PassWorld.Count(result, "tablesWaiting").Should().Be(1);
        result.Success.Should().BeFalse();
    }

    // =====================================================================================
    // Helpers
    // =====================================================================================

    /// <summary>A run built as the admin route builds it (against the pass's go-live unless another is given).</summary>
    private RecordRepairOnDemandRun OnDemand(
        string table, DateTimeOffset? from = null, DateTimeOffset? to = null, DateTimeOffset? goLive = null, Guid? afterId = null)
    {
        var run = RecordRepairOnDemandRun.TryCreate(
            table, (from ?? WindowFrom).ToString("O"), (to ?? WindowTo).ToString("O"), afterId?.ToString("D"), goLive ?? PassWorld.GoLive, PassWorld.Now,
            _pass.Job().Tables, out var refusal);
        run.Should().NotBeNull(refusal?.Detail);
        return run!;
    }

    private Task<JobRunResult> RunOnDemandAsync(RecordRepairOnDemandRun run) =>
        _pass.Job().ExecuteAsync(
            new JobRunContext(Guid.NewGuid(), PassWorld.CorrelationId, JobRunTrigger.ManualAdmin,
                new Dictionary<string, object> { [RecordRepairOnDemandRun.ParameterKey] = run }),
            CancellationToken.None);
}

/// <summary>
/// Task 052c (review F4): the rules of <see cref="RecordRepairOnDemandRun"/> at their boundaries, asked directly (the route and the
/// job ask the same ones): the window's end may be now; the window may be exactly 31 days; its start may be exactly go-live; the
/// table name is matched ignoring case and padding and normalised to the pass's spelling; offsets and fractions of a second parse;
/// a bound that does not parse, or has no offset, is refused with its own reason.
/// </summary>
public sealed class RecordRepairOnDemandRunValidationTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 12, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset GoLive = new(2026, 9, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly string[] Tables = ["sprk_event", "contact"];

    private static RecordRepairOnDemandRun? Create(
        string? table, string? from, string? to, out RecordRepairRunRefusal? refusal, string? afterId = null) =>
        RecordRepairOnDemandRun.TryCreate(table, from, to, afterId, GoLive, Now, Tables, out refusal);

    [Theory]
    // createdTo == now is accepted; a second later is the future.
    [InlineData("sprk_event", "2026-11-11T12:00:00Z", "2026-11-12T12:00:00Z", "sprk_event", null)]
    [InlineData("sprk_event", "2026-11-12T11:00:00Z", "2026-11-12T12:00:01Z", null, RecordRepairRunReason.WindowInFuture)]
    // The window may be exactly 31 days; one second more is too long.
    [InlineData("sprk_event", "2026-10-12T12:00:00Z", "2026-11-12T12:00:00Z", "sprk_event", null)]
    [InlineData("sprk_event", "2026-10-12T11:59:59Z", "2026-11-12T12:00:00Z", null, RecordRepairRunReason.WindowTooLong)]
    // createdFrom == go-live is accepted; one second earlier is refused (never clamped).
    [InlineData("sprk_event", "2026-09-01T00:00:00Z", "2026-09-02T00:00:00Z", "sprk_event", null)]
    [InlineData("sprk_event", "2026-08-31T23:59:59Z", "2026-09-02T00:00:00Z", null, RecordRepairRunReason.BeforeGoLive)]
    // The table: any case, padded; normalised to the pass's own spelling. Unlisted and empty tables are refused.
    [InlineData("SPRK_EVENT", "2026-11-10T00:00:00Z", "2026-11-11T00:00:00Z", "sprk_event", null)]
    [InlineData("  Contact  ", "2026-11-10T00:00:00Z", "2026-11-11T00:00:00Z", "contact", null)]
    [InlineData("task", "2026-11-10T00:00:00Z", "2026-11-11T00:00:00Z", null, RecordRepairRunReason.TableNotRepaired)]
    [InlineData("", "2026-11-10T00:00:00Z", "2026-11-11T00:00:00Z", null, RecordRepairRunReason.TableNotRepaired)]
    [InlineData(null, "2026-11-10T00:00:00Z", "2026-11-11T00:00:00Z", null, RecordRepairRunReason.TableNotRepaired)]
    // An empty window.
    [InlineData("sprk_event", "2026-11-10T00:00:00Z", "2026-11-10T00:00:00Z", null, RecordRepairRunReason.WindowEmpty)]
    // A bound that is not an instant, is absent, or has no offset.
    [InlineData("sprk_event", "last tuesday", "2026-11-11T00:00:00Z", null, RecordRepairRunReason.TimestampInvalid)]
    [InlineData("sprk_event", "2026-11-10T00:00:00Z", "not a date", null, RecordRepairRunReason.TimestampInvalid)]
    [InlineData("sprk_event", "2026-11-10", "2026-11-11T00:00:00Z", null, RecordRepairRunReason.TimestampInvalid)]
    [InlineData("sprk_event", "2026-13-45T00:00:00Z", "2026-11-11T00:00:00Z", null, RecordRepairRunReason.TimestampInvalid)]
    [InlineData("sprk_event", null, "2026-11-11T00:00:00Z", null, RecordRepairRunReason.TimestampInvalid)]
    [InlineData("sprk_event", "2026-11-10T00:00:00Z", "", null, RecordRepairRunReason.TimestampInvalid)]
    [InlineData("sprk_event", "2026-11-10T00:00:00", "2026-11-11T00:00:00Z", null, RecordRepairRunReason.TimestampOffsetMissing)]
    [InlineData("sprk_event", "2026-11-10T00:00:00Z", "2026-11-11T00:00:00.5", null, RecordRepairRunReason.TimestampOffsetMissing)]
    public void TheRunRules_AtTheirBoundaries(string? table, string? from, string? to, string? expectedTable, string? expectedReason)
    {
        var run = Create(table, from, to, out var refusal);

        if (expectedReason is null)
        {
            run.Should().NotBeNull(refusal?.Detail);
            refusal.Should().BeNull();
            run!.Table.Should().Be(expectedTable);
        }
        else
        {
            run.Should().BeNull();
            refusal!.Code.Should().Be(expectedReason);
        }
    }

    [Fact]
    public void AnOffsetBound_IsNormalisedToUtc()
    {
        var run = Create("sprk_event", "2026-11-10T10:30:00+05:30", "2026-11-11T10:30:00+05:30", out var refusal);

        run.Should().NotBeNull(refusal?.Detail);
        run!.CreatedFrom.Should().Be(new DateTimeOffset(2026, 11, 10, 5, 0, 0, TimeSpan.Zero));
        run.CreatedFrom.Offset.Should().Be(TimeSpan.Zero);
        run.CreatedTo.Should().Be(new DateTimeOffset(2026, 11, 11, 5, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public void FractionalSeconds_AreAccepted_AndKept()
    {
        var run = Create("sprk_event", "2026-11-10T00:00:00.1234567Z", "2026-11-10T00:00:00.5Z", out var refusal);

        run.Should().NotBeNull(refusal?.Detail);
        run!.CreatedFrom.Ticks.Should().Be(new DateTimeOffset(2026, 11, 10, 0, 0, 0, TimeSpan.Zero).Ticks + 1234567);
        run.CreatedTo.Ticks.Should().Be(new DateTimeOffset(2026, 11, 10, 0, 0, 0, TimeSpan.Zero).Ticks + 5000000);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void NoAfterId_MeansTheStartOfTheWindow(string? afterId)
    {
        var run = Create("sprk_event", "2026-11-10T00:00:00Z", "2026-11-11T00:00:00Z", out var refusal, afterId);

        run.Should().NotBeNull(refusal?.Detail);
        run!.AfterId.Should().BeNull();
    }

    [Fact]
    public void AWellFormedAfterId_IsKept()
    {
        var id = Guid.Parse("a052c000-0000-4000-8000-0000000000aa");

        var run = Create("sprk_event", "2026-11-10T00:00:00Z", "2026-11-11T00:00:00Z", out var refusal, " " + id.ToString("D").ToUpperInvariant() + " ");

        run.Should().NotBeNull(refusal?.Detail);
        run!.AfterId.Should().Be(id);
    }

    [Theory]
    [InlineData("not-a-guid")]
    [InlineData("a052c000000040008000000000000aa")]
    [InlineData("{a052c000-0000-4000-8000-0000000000aa}")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public void AMalformedOrEmptyAfterId_IsRefused(string afterId)
    {
        var run = Create("sprk_event", "2026-11-10T00:00:00Z", "2026-11-11T00:00:00Z", out var refusal, afterId);

        run.Should().BeNull();
        refusal!.Code.Should().Be(RecordRepairRunReason.AfterIdInvalid);
    }
}
