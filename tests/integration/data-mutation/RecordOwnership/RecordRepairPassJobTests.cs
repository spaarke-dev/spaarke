using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Spaarke.Dataverse;
using Spaarke.Scheduling;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.Jobs;
using Sprk.Bff.Api.Services.RecordCreation;
using Sprk.Bff.Api.Tests.AccessControl.IdentityBinding;
using Sprk.Bff.Api.Tests.Services.Communication;
using Xunit;
using Directory = Sprk.Bff.Api.Tests.TestInfrastructure.OwnershipDirectory;

namespace Sprk.Bff.Api.Tests.Integration.DataMutation.RecordOwnership;

/// <summary>
/// field-mapping-server-write-path-r1 task 052b (spec FR-25, NFR-01, NFR-03, NFR-04): the scheduled repair pass
/// (<see cref="RecordRepairPassJob"/>) over Repair mode. A row created outside the product after the go-live instant is repaired;
/// a row created before it, or by a product principal, is never written; task and account are never read; a contact is owned by
/// the customer business unit's default team or not re-owned at all; each table is claimed per run; a second run writes nothing.
/// </summary>
/// <remarks>
/// The job, Repair mode, the ownership resolver, the contact owner lookup and the claim store (<see cref="IdempotencyService"/>
/// over a memory cache) are the real code. Only the Dataverse boundary is doubled: Repair's reads and writes go to
/// <see cref="RepairWorld"/>'s directory, and the job's own reads (WhoAmI and the window scan) to <see cref="PassWorld.Scan"/>,
/// which applies the scan query's conditions to the seeded rows (or, for the defence-in-depth tests, ignores them).
/// </remarks>
[Trait("status", "new")]
public sealed class RecordRepairPassJobTests
{
    private readonly PassWorld _pass = new();

    private RepairWorld World => _pass.World;

    // =====================================================================================
    // Must not fire: a native row created after go-live is repaired
    // =====================================================================================

    [Fact]
    public async Task ANativeRowCreatedAfterGoLive_IsRepaired_MappingOwnerAndStamp_InOneWrite()
    {
        var id = _pass.NativeEvent();

        var result = await _pass.RunAsync();

        result.Success.Should().BeTrue(result.ErrorMessage);
        var write = World.Writes.Should().ContainSingle("one conditional write per row").Subject;
        write.Id.Should().Be(id);
        write.Fields["sprk_clientname"].Should().Be(RepairWorld.MatterClient, "the mapping");
        write.Fields["ownerid"].Should().Be(new EntityReference("team", Directory.ChildTeam), "the owner");
        PassWorld.Count(result, "repaired").Should().Be(1);
        PassWorld.Count(result, "scanned").Should().Be(1);
    }

    [Fact]
    public async Task ASystemUserOwnedContact_IsReOwnedToTheCustomerTeam_WithNothingMapped()
    {
        _pass.CustomerTeamResolves();
        var id = _pass.NativeContact();

        var result = await _pass.RunAsync();

        result.Success.Should().BeTrue(result.ErrorMessage);
        var write = World.Writes.Should().ContainSingle().Subject;
        write.Table.Should().Be("contact");
        write.Id.Should().Be(id);
        write.Fields.Should().ContainKey("ownerid").WhoseValue
            .Should().Be(new EntityReference("team", CustomerContactOwnerTests.CustomerTeam));
        write.Fields.Keys.Should().BeEquivalentTo(["ownerid"], "a contact is owner-only: nothing is mapped onto it");
    }

    // =====================================================================================
    // AC1 (must fire): forward-only
    // =====================================================================================

    [Fact]
    public async Task ARowCreatedBeforeGoLive_IsNeverWritten_EvenWhenTheScanReturnsIt()
    {
        _pass.ScanIgnoresItsConditions = true;
        _pass.NativeEvent(createdOn: PassWorld.GoLive.AddDays(-1));

        var result = await _pass.RunAsync();

        World.Writes.Should().BeEmpty("the job's own check refuses a row created before go-live");
        PassWorld.Count(result, "outsidePass").Should().Be(1);
        result.Success.Should().BeFalse("a scan that returned a row outside the pass is not a clean run");
    }

    [Fact]
    public async Task TheScanQuery_BoundsCreatedOnFromGoLive_AndExcludesTheProductPrincipals()
    {
        _pass.NativeEvent();

        await _pass.RunAsync();

        var scan = _pass.ScanQueries.First(q => q.EntityName == "sprk_event");
        var conditions = scan.Criteria.Conditions;
        conditions.Should().ContainSingle(c => c.AttributeName == "createdon" && c.Operator == ConditionOperator.GreaterEqual)
            .Which.Values.Single().Should().Be(PassWorld.GoLive.UtcDateTime);
        conditions.Should().ContainSingle(c => c.AttributeName == "createdon" && c.Operator == ConditionOperator.LessThan)
            .Which.Values.Single().Should().Be((PassWorld.Now - RecordRepairPassJob.SettleTime).UtcDateTime);
        conditions.Should().ContainSingle(c => c.AttributeName == "createdby" && c.Operator == ConditionOperator.NotIn)
            .Which.Values.Should().BeEquivalentTo(new object[] { PassWorld.BffIdentity, PassWorld.RetiredIdentity });
    }

    [Theory]
    [InlineData("before go-live")]
    [InlineData("no createdon")]
    public async Task Repair_HandedARowOutsideThePass_ReadsItButDecidesAndWritesNothing(string shape)
    {
        var id = shape == "no createdon"
            ? _pass.NativeEvent(withoutCreatedOn: true)
            : _pass.NativeEvent(createdOn: PassWorld.GoLive.AddTicks(-1));

        var outcome = await World.RepairAsync("sprk_event", id, _pass.Context());

        outcome.Status.Should().Be(RecordRepairStatus.Skipped);
        outcome.ReasonCode.Should().Be(RecordRepairReason.RowCreatedBeforeGoLive);
        World.Writes.Should().BeEmpty();
        World.OwnerContexts.Should().BeEmpty("nothing is decided for a row outside the pass");
    }

    [Fact]
    public async Task Repair_WithoutAPass_IsUnchanged_ForARowCreatedBeforeGoLive()
    {
        // Control: the pass rule belongs to the pass; Repair on its own (task 016) still repairs.
        var id = _pass.NativeEvent(createdOn: PassWorld.GoLive.AddDays(-30));

        var outcome = await World.RepairAsync("sprk_event", id);

        outcome.Status.Should().Be(RecordRepairStatus.Repaired);
    }

    // =====================================================================================
    // AC2 (must fire): task 050's marker
    // =====================================================================================

    [Theory]
    [InlineData("the BFF identity")]
    [InlineData("a configured retired identity")]
    public async Task ARowCreatedByAProductPrincipal_IsNeverWritten_EvenWhenTheScanReturnsIt(string creator)
    {
        _pass.ScanIgnoresItsConditions = true;
        var principal = creator == "the BFF identity" ? PassWorld.BffIdentity : PassWorld.RetiredIdentity;
        var id = _pass.NativeEvent(createdBy: principal);

        var result = await _pass.RunAsync();
        var direct = await World.RepairAsync("sprk_event", id, _pass.Context());

        World.Writes.Should().BeEmpty();
        PassWorld.Count(result, "outsidePass").Should().Be(1);
        direct.Status.Should().Be(RecordRepairStatus.Skipped);
        direct.ReasonCode.Should().Be(RecordRepairReason.RowCreatedByProduct);
    }

    [Fact]
    public async Task TheOntologyWritersIdentity_IsAProductPrincipal_WhenTheWriterIsConfigured()
    {
        var writer = Guid.Parse("a052b000-0000-4000-8000-0000000000e1");
        _pass.Settings[RecordRepairPassJob.OntologyWriterIdentityConfigKey] = "writer-client-id";
        _pass.OntologyWriter = _ => Task.FromResult(writer);
        _pass.ScanIgnoresItsConditions = true;
        _pass.NativeEvent(createdBy: writer);

        var result = await _pass.RunAsync();

        World.Writes.Should().BeEmpty();
        PassWorld.Count(result, "outsidePass").Should().Be(1);
    }

    [Theory]
    [InlineData("WhoAmI faults")]
    [InlineData("WhoAmI names no user")]
    [InlineData("a configured id does not parse")]
    [InlineData("the writer's identity faults")]
    public async Task ARunThatCannotDecideItsPrincipals_ReadsNoTable_WritesNothing_AndFails(string cause)
    {
        _pass.NativeEvent();
        switch (cause)
        {
            case "WhoAmI faults":
                _pass.WhoAmI = () => throw new TimeoutException("Test: WhoAmI faulted.");
                break;
            case "WhoAmI names no user":
                _pass.WhoAmI = () => Guid.Empty;
                break;
            case "a configured id does not parse":
                _pass.Settings[RecordRepairPassJob.ProductPrincipalIdsConfigKey] = $"{PassWorld.RetiredIdentity:D}, not-a-guid";
                break;
            default:
                _pass.Settings[RecordRepairPassJob.OntologyWriterIdentityConfigKey] = "writer-client-id";
                _pass.OntologyWriter = _ => throw new TimeoutException("Test: the writer's WhoAmI faulted.");
                break;
        }

        var result = await _pass.RunAsync();

        result.Success.Should().BeFalse();
        _pass.ScanQueries.Should().BeEmpty("no table is read without the principal set");
        World.Writes.Should().BeEmpty();
    }

    // =====================================================================================
    // AC3 (must fire): task and account are excluded
    // =====================================================================================

    [Fact]
    public async Task TaskAndAccount_AreNeverReadOrWrittenByThePass()
    {
        _pass.NativeRow("task");
        _pass.NativeRow("account");

        var result = await _pass.RunAsync();

        _pass.ScanQueries.Select(q => q.EntityName).Should().NotContain(["task", "account"]);
        World.Writes.Should().BeEmpty();
        result.Success.Should().BeTrue(result.ErrorMessage);
        RecordRepairPassJob.ExcludedTables.Should().Contain(["task", "account"]);
    }

    [Theory]
    [InlineData("task")]
    [InlineData("account")]
    public async Task Repair_RefusesTaskAndAccount_WithoutReadingThem(string table)
    {
        var id = _pass.NativeRow(table);

        var withPass = await World.RepairAsync(table, id, _pass.Context());
        var alone = await World.RepairAsync(table, id);

        withPass.ReasonCode.Should().Be(RecordRepairReason.TableExcluded);
        alone.ReasonCode.Should().Be(RecordRepairReason.TableExcluded, "Repair refuses them whoever calls it");
        alone.Status.Should().Be(RecordRepairStatus.Skipped);
        World.Directory.QueriedEntities.Should().NotContain(table);
        World.Writes.Should().BeEmpty();
    }

    // =====================================================================================
    // Review F1-b: the secure decision comes first for a contact filed under an invoice
    // =====================================================================================

    [Fact]
    public async Task AContactUnderASecureInvoice_IsNotReOwnedToTheCustomerTeam_TodaysRefusalStands()
    {
        _pass.CustomerTeamResolves();
        CustomerContactOwnerTests.WithTheInvoices(World.Directory);
        var id = _pass.NativeContactUnder(CustomerContactOwnerTests.SecureInvoice);

        var outcome = await World.RepairAsync("contact", id, _pass.Context());

        World.Writes.Should().NotContain(w => w.Fields.ContainsKey("ownerid"), "never re-owned out of the invoice's isolation");
        outcome.OwnerRefusal!.OwnerDecision!.IsSecureOwner.Should().BeTrue("the resolver named the Secure team, as it does today");
        World.Directory.Row("contact", id).GetAttributeValue<EntityReference>("ownerid")
            .Should().Be(new EntityReference("systemuser", RepairWorld.PersonInChild));
    }

    [Fact]
    public async Task AContactUnderAnOrdinaryInvoice_IsReOwnedToTheCustomerTeam()
    {
        _pass.CustomerTeamResolves();
        CustomerContactOwnerTests.WithTheInvoices(World.Directory);
        var id = _pass.NativeContactUnder(CustomerContactOwnerTests.OrdinaryInvoice);

        var outcome = await World.RepairAsync("contact", id, _pass.Context());

        outcome.Status.Should().Be(RecordRepairStatus.Repaired, outcome.ReasonCode);
        World.Writes.Should().ContainSingle().Which.Fields["ownerid"]
            .Should().Be(new EntityReference("team", CustomerContactOwnerTests.CustomerTeam));
    }

    // =====================================================================================
    // AC4 (must fire): a contact whose customer team cannot be resolved
    // =====================================================================================

    [Fact]
    public async Task AContactWhoseCustomerTeamCannotBeResolved_KeepsItsOwner_IsReported_AndIsAskedAgain()
    {
        _pass.CustomerTeamUnresolvable();
        var id = _pass.NativeContact();

        var first = await _pass.RunAsync();
        var second = await _pass.RunAsync();

        World.Writes.Should().BeEmpty("never given its creator's team in place of the customer team");
        World.Directory.Row("contact", id).GetAttributeValue<EntityReference>("ownerid")
            .Should().Be(new EntityReference("systemuser", RepairWorld.PersonInChild));
        first.Success.Should().BeFalse("the contact is kept for the next run");
        PassWorld.Count(first, "ownerRefused").Should().Be(1);
        PassWorld.Count(first, "pendingRetry").Should().Be(1);
        PassWorld.Count(second, "retried").Should().Be(1, "the next run asks it again");
        var direct = await World.RepairAsync("contact", id, _pass.Context());
        direct.OwnerRefusal!.Code.Should().Be(RecordOwnerRefusal.CustomerContactTeamUnresolved);
        direct.NeedsRetry.Should().BeTrue();
    }

    [Fact]
    public async Task WithTheCustomerUnitSettingUnset_AContactIsNotReOwned_NeitherToItsCreatorsTeamNorToAnyDefaultTeam()
    {
        // ExternalAccess:CustomerBusinessUnitId is unset on dev today: the rule must refuse cleanly. The store HAS a default
        // team for the unit, so only the setting decides.
        var store = new InMemoryContactIdentityStore();
        store.DefaultTeams[CustomerContactOwnerTests.CustomerBu] = [CustomerContactOwnerTests.CustomerTeam];
        World.CustomerContactOwner = IdentityBindingTestKit.OwnerAwareBinder(store, customerBusinessUnitId: null);
        var id = _pass.NativeContact();

        var result = await _pass.RunAsync();

        World.Writes.Should().BeEmpty("nothing is written");
        World.Directory.Row("contact", id).GetAttributeValue<EntityReference>("ownerid")
            .Should().Be(new EntityReference("systemuser", RepairWorld.PersonInChild), "the row keeps the owner it had");
        store.Reads.Should().NotContain("owner-team", "no default team is looked up without the setting");
        PassWorld.Count(result, "ownerRefused").Should().Be(1);
        result.Success.Should().BeFalse("the contact waits for the setting");
    }

    // =====================================================================================
    // AC5 (must fire): the per-table claim
    // =====================================================================================

    [Fact]
    public async Task WhileAnotherRunnerHoldsATable_TheRunWritesNothingToIt_AndFails()
    {
        _pass.NativeEvent();
        (await _pass.Claims.TryAcquireProcessingLockAsync(
            $"{RecordRepairPassJob.JobIdConstant}:sprk_event", "another-runner", TimeSpan.FromMinutes(15))).Should().BeTrue();

        var result = await _pass.RunAsync();

        World.Writes.Should().BeEmpty();
        _pass.ScanQueries.Should().NotContain(q => q.EntityName == "sprk_event");
        PassWorld.Count(result, "claimHeld").Should().Be(1);
        result.Success.Should().BeFalse();
    }

    // =====================================================================================
    // AC6 (must fire): idempotent
    // =====================================================================================

    [Fact]
    public async Task ASecondRun_ReadsNoRowTheFirstFinished_AndWritesNothing()
    {
        _pass.NativeEvent();
        await _pass.RunAsync();

        var second = await _pass.RunAsync();

        World.Writes.Should().ContainSingle("the second run writes nothing");
        PassWorld.Count(second, "scanned").Should().Be(0, "the watermark moved past the first run's window");
        second.Success.Should().BeTrue(second.ErrorMessage);
    }

    [Fact]
    public async Task AHostRetryOfTheSameRun_ReadsNothingForATableItMarkedDone()
    {
        _pass.NativeEvent();
        var run = Guid.NewGuid();
        await _pass.RunAsync(run);
        var scansBefore = _pass.ScanQueries.Count;
        _pass.Time.Advance(TimeSpan.FromMinutes(1));

        var retry = await _pass.RunAsync(run, attempt: 2);

        _pass.ScanQueries.Count.Should().Be(scansBefore, "every table is marked done for the run");
        PassWorld.Count(retry, "alreadyDone").Should().Be(_pass.Job().Tables.Count);
        World.Writes.Should().ContainSingle();
    }

    // =====================================================================================
    // AC8: the per-pass metadata cache
    // =====================================================================================

    [Fact]
    public async Task WithinOnePass_ATablesColumnMetadataIsReadOnce_HoweverManyRowsNeedIt()
    {
        _pass.NativeEvent();
        _pass.NativeEvent();
        _pass.NativeEvent();

        var result = await _pass.RunAsync();

        World.Writes.Should().HaveCount(3);
        World.ColumnMetadataReads.Count(t => t == "sprk_event").Should().Be(1);
        World.ColumnMetadataReads.Count(t => t == "sprk_matter").Should().Be(1);
        PassWorld.Count(result, "metadataReads").Should().Be(2);
    }

    [Fact]
    public async Task AFaultedMetadataRead_IsNotKept_AndIsAskedAgainByTheNextRow()
    {
        _pass.NativeEvent();
        _pass.NativeEvent();
        World.ColumnMetadataFaults["sprk_event"] = new TimeoutException("Test: metadata read faulted.");

        var result = await _pass.RunAsync();

        World.ColumnMetadataReads.Count(t => t == "sprk_event").Should().Be(2, "a fault is never kept");
        PassWorld.Count(result, "pendingRetry").Should().Be(2, "nothing was mapped, so both rows are asked again");
        result.Success.Should().BeFalse();
    }

    // =====================================================================================
    // AC9: off until configured
    // =====================================================================================

    [Fact]
    public async Task WithGoLiveUnset_TheJobReadsAndWritesNothing_AndLogsAnError()
    {
        _pass.Settings.Remove(RecordRepairPassJob.GoLiveConfigKey);
        _pass.NativeEvent();

        var result = await _pass.RunAsync();

        _pass.WhoAmICalls.Should().Be(0);
        _pass.ScanQueries.Should().BeEmpty();
        World.Writes.Should().BeEmpty();
        _pass.Job().Tables.Should().AllSatisfy(t => _pass.State(t).Should().BeNull("no watermark or other state is written"));
        PassWorld.Status(result).Should().Be(RecordRepairPassJob.StatusNotConfigured);
        _pass.Log.Entries.Should().Contain(e => e.Level == LogLevel.Error && e.Message.Contains(RecordRepairPassJob.GoLiveConfigKey));
    }

    [Fact]
    public async Task WithGoLiveUnparseable_TheJobReadsAndWritesNothing_AndTheRunFails()
    {
        _pass.Settings[RecordRepairPassJob.GoLiveConfigKey] = "next tuesday";
        _pass.NativeEvent();

        var result = await _pass.RunAsync();

        result.Success.Should().BeFalse();
        _pass.WhoAmICalls.Should().Be(0);
        _pass.ScanQueries.Should().BeEmpty();
        World.Writes.Should().BeEmpty();
        _pass.Log.Entries.Should().Contain(e => e.Level == LogLevel.Error && e.Message.Contains(RecordRepairPassJob.GoLiveConfigKey));
    }

    // =====================================================================================
    // AC10: retry, bounded; observable
    // =====================================================================================

    [Fact]
    public async Task ARowWhoseRepairFailed_IsAskedAgainEachRun_ThenLoggedAsAbandoned()
    {
        _pass.NativeEvent();
        World.WriteFault = new TimeoutException("Test: the write faulted.");

        var first = await _pass.RunAsync();
        first.Success.Should().BeFalse();
        PassWorld.Count(first, "failed").Should().Be(1);
        PassWorld.Count(first, "pendingRetry").Should().Be(1);

        JobRunResult last = first;
        for (var run = 2; run <= RecordRepairPassJob.MaxAttempts; run++)
            last = await _pass.RunAsync();

        PassWorld.Count(last, "abandoned").Should().Be(1);
        PassWorld.Count(last, "pendingRetry").Should().Be(0);
        _pass.Log.Entries.Should().Contain(e => e.Level == LogLevel.Error && e.Message.Contains("no longer asked"));
        World.Writes.Should().HaveCount(RecordRepairPassJob.MaxAttempts, "asked once per run, never more");

        var after = await _pass.RunAsync();
        PassWorld.Count(after, "retried").Should().Be(0, "an abandoned row is not asked again");
    }

    [Fact]
    public async Task TheHeartbeatAndTheResult_CarryTheCountsAndTheCorrelationId()
    {
        _pass.NativeEvent();

        var result = await _pass.RunAsync();

        using var json = JsonDocument.Parse(result.ResultJson!);
        json.RootElement.GetProperty("correlationId").GetString().Should().Be(PassWorld.CorrelationId);
        foreach (var count in new[] { "scanned", "repaired", "noOp", "skipped", "failed" })
            json.RootElement.TryGetProperty(count, out _).Should().BeTrue(count);
        _pass.Log.Entries.Should().ContainSingle(e => e.Message.Contains("heartbeat"))
            .Which.Message.Should().Contain(PassWorld.CorrelationId).And.Contain("scanned=1").And.Contain("repaired=1");
    }

    [Fact]
    public async Task AWindowLargerThanTheCap_ContinuesNextRun_AndIsPartialUntilDone()
    {
        _pass.Settings[RecordRepairPassJob.MaxRowsPerTableConfigKey] = "1";
        _pass.NativeEvent();
        _pass.NativeEvent();

        var first = await _pass.RunAsync();
        _pass.Time.Advance(TimeSpan.FromMinutes(5));
        var second = await _pass.RunAsync();
        var third = await _pass.RunAsync();

        first.Success.Should().BeFalse("the window was not finished");
        PassWorld.Count(first, "truncated").Should().Be(1);
        second.Success.Should().BeTrue(second.ErrorMessage);
        World.Writes.Should().HaveCount(2);
        PassWorld.Count(third, "scanned").Should().Be(0);
    }

    [Fact]
    public void TheJob_RunsEveryFiveMinutes_AndSweepsNoExcludedTable()
    {
        RecordRepairPassJob.DefaultCronSchedule.Should().Be("*/5 * * * *");
        RecordRepairPassJob.ExcludedTables.Should().Contain(
            ["task", "account", "sprk_signal", "sprk_decisionrecord", "sprk_budgetrevision", "sprk_procedureversion"]);
        _pass.Job().Tables.Should().NotIntersectWith(RecordRepairPassJob.ExcludedTables).And.Contain(["sprk_event", "contact"]);
    }

    // =====================================================================================
    // Harness
    // =====================================================================================

    /// <summary>The job over a <see cref="RepairWorld"/>: its scope, its scan, its claim store, its clock and its settings.</summary>
    private sealed class PassWorld
    {
        public static readonly DateTimeOffset Now = new(2026, 11, 12, 12, 0, 0, TimeSpan.Zero);
        public static readonly DateTimeOffset GoLive = new(2026, 11, 1, 0, 0, 0, TimeSpan.Zero);
        public static readonly Guid BffIdentity = Guid.Parse("a052b000-0000-4000-8000-0000000000f1");
        public static readonly Guid RetiredIdentity = Guid.Parse("a052b000-0000-4000-8000-0000000000f2");
        public const string CorrelationId = "corr-052b";

        private readonly List<(string Table, Guid Id)> _seeded = [];
        private readonly MemoryDistributedCache _cache = new(Options.Create(new MemoryDistributedCacheOptions()));

        public PassWorld()
        {
            Claims = new IdempotencyService(_cache, NullLogger<IdempotencyService>.Instance, Time);
        }

        public RepairWorld World { get; } = new();

        public FakeTimeProvider Time { get; } = new(Now);

        public IdempotencyService Claims { get; }

        public CapturingLogger<RecordRepairPassJob> Log { get; } = new();

        public Dictionary<string, string?> Settings { get; } = new()
        {
            [RecordRepairPassJob.GoLiveConfigKey] = GoLive.ToString("O"),
            [RecordRepairPassJob.ProductPrincipalIdsConfigKey] = RetiredIdentity.ToString("D"),
        };

        /// <summary>The job's WhoAmI.</summary>
        public Func<Guid> WhoAmI { get; set; } = () => BffIdentity;

        public int WhoAmICalls { get; private set; }

        /// <summary>The ontology writer's identity, when the writer is configured.</summary>
        public Func<CancellationToken, Task<Guid>> OntologyWriter { get; set; } =
            _ => throw new InvalidOperationException("Test: the writer is not configured.");

        /// <summary>When set, the scan returns every seeded row of the table (a query that did not hold).</summary>
        public bool ScanIgnoresItsConditions { get; set; }

        /// <summary>Every scan query the job sent.</summary>
        public List<QueryExpression> ScanQueries { get; } = [];

        /// <summary>A table's saved state (watermark, sweep, retries), or <c>null</c> when none was written.</summary>
        public string? State(string table) => _cache.GetString($"{RecordRepairPassJob.JobIdConstant}:{table}:state");

        public RecordRepairPassContext Context() =>
            new(GoLive, new HashSet<Guid> { BffIdentity, RetiredIdentity });

        // ── Rows ──────────────────────────────────────────────────────────────────────────────────────────────────

        /// <summary>A native event under the matter with nothing mapped, owned by its creator (a person).</summary>
        public Guid NativeEvent(DateTimeOffset? createdOn = null, Guid? createdBy = null, bool withoutCreatedOn = false) =>
            Seed("sprk_event", World.EventUnderTheMatter(clientName: null, practiceArea: null, priority: null, indexed: true),
                createdOn, createdBy, withoutCreatedOn);

        /// <summary>A native contact owned by its creator (a person).</summary>
        public Guid NativeContact() =>
            NativeRow("contact");

        /// <summary>A native contact filed under <paramref name="invoice"/>, owned by its creator (a person).</summary>
        public Guid NativeContactUnder(Guid invoice) =>
            Seed("contact", World.RowOf("contact", Directory.ChildBu, new Dictionary<string, object>
            {
                ["ownerid"] = new EntityReference("systemuser", RepairWorld.PersonInChild),
                ["sprk_invoice"] = new EntityReference("sprk_invoice", invoice),
            }), createdOn: null, createdBy: null, withoutCreatedOn: false);

        /// <summary>A native row of <paramref name="table"/>, filed under nothing, owned by its creator (a person).</summary>
        public Guid NativeRow(string table) =>
            Seed(table, World.RowOf(table, Directory.ChildBu,
                new Dictionary<string, object> { ["ownerid"] = new EntityReference("systemuser", RepairWorld.PersonInChild) }),
                createdOn: null, createdBy: null, withoutCreatedOn: false);

        /// <summary>Gives the row its <c>createdon</c> (an hour before the run, unless given or left out) and <c>createdby</c>.</summary>
        private Guid Seed(string table, Guid id, DateTimeOffset? createdOn, Guid? createdBy, bool withoutCreatedOn)
        {
            var row = World.Directory.Row(table, id);
            if (!withoutCreatedOn)
                row["createdon"] = (createdOn ?? Now.AddHours(-1)).UtcDateTime;
            row["createdby"] = new EntityReference("systemuser", createdBy ?? RepairWorld.PersonInChild);
            _seeded.Add((table, id));
            return id;
        }

        public void CustomerTeamResolves()
        {
            var store = new InMemoryContactIdentityStore();
            store.DefaultTeams[CustomerContactOwnerTests.CustomerBu] = [CustomerContactOwnerTests.CustomerTeam];
            World.Directory.WithTeam(CustomerContactOwnerTests.CustomerTeam, CustomerContactOwnerTests.CustomerBu, isDefault: true, teamType: 0);
            World.CustomerContactOwner = IdentityBindingTestKit.OwnerAwareBinder(store, CustomerContactOwnerTests.CustomerBu);
        }

        public void CustomerTeamUnresolvable() =>
            World.CustomerContactOwner = IdentityBindingTestKit.OwnerAwareBinder(new InMemoryContactIdentityStore(), CustomerContactOwnerTests.CustomerBu);

        // ── The job ───────────────────────────────────────────────────────────────────────────────────────────────

        public RecordRepairPassJob Job()
        {
            var entities = new Mock<IGenericEntityService>(MockBehavior.Strict);
            entities.Setup(e => e.GetCurrentIdentityIdAsync(It.IsAny<CancellationToken>()))
                .Returns(() =>
                {
                    WhoAmICalls++;
                    return Task.FromResult(WhoAmI());
                });
            entities.Setup(e => e.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()))
                .Returns((QueryExpression query, CancellationToken _) => Task.FromResult(Scan(query)));

            var services = new ServiceCollection()
                .AddSingleton(entities.Object)
                .AddSingleton<IIdempotencyService>(Claims)
                .AddSingleton<IDistributedCache>(_cache)
                .AddScoped(_ => World.ServiceForAPass())
                .BuildServiceProvider();

            return new RecordRepairPassJob(
                services.GetRequiredService<IServiceScopeFactory>(), Time,
                new ConfigurationBuilder().AddInMemoryCollection(Settings).Build(), Log)
            {
                OntologyWriterIdentity = (_, ct) => OntologyWriter(ct),
            };
        }

        public Task<JobRunResult> RunAsync(Guid? runId = null, int attempt = 1) =>
            Job().ExecuteAsync(
                new JobRunContext(runId ?? Guid.NewGuid(), CorrelationId, JobRunTrigger.Scheduled, new Dictionary<string, object>(), attempt),
                CancellationToken.None);

        /// <summary>The job's window scan over the seeded rows: each condition of the query applied, ordered by id, capped.</summary>
        private EntityCollection Scan(QueryExpression query)
        {
            ScanQueries.Add(query);
            var idColumn = query.EntityName + "id";
            var rows = _seeded
                .Where(s => s.Table == query.EntityName)
                .Select(s => World.Directory.Row(s.Table, s.Id))
                .Where(row => query.Criteria.Conditions.All(c =>
                    (ScanIgnoresItsConditions && c.AttributeName != idColumn) || Holds(row, c, idColumn)))
                .OrderBy(row => row.Id)
                .ToList();

            var cap = query.PageInfo?.Count is > 0 and var count ? count : int.MaxValue;
            var page = rows.Take(cap).Select(row =>
            {
                var copy = new Entity(row.LogicalName, row.Id) { [idColumn] = row.Id };
                foreach (var column in new[] { "createdon", "createdby" })
                {
                    if (row.Attributes.TryGetValue(column, out var value))
                        copy[column] = value;
                }

                return copy;
            }).ToList();

            return new EntityCollection(page) { EntityName = query.EntityName, MoreRecords = rows.Count > cap };
        }

        private static bool Holds(Entity row, ConditionExpression condition, string idColumn)
        {
            row.Attributes.TryGetValue(condition.AttributeName, out var value);
            return condition.Operator switch
            {
                ConditionOperator.GreaterEqual => value is DateTime at && at >= (DateTime)condition.Values[0],
                ConditionOperator.LessThan => value is DateTime at && at < (DateTime)condition.Values[0],
                ConditionOperator.NotNull => value is not null,
                ConditionOperator.NotIn => value is EntityReference by && !condition.Values.Cast<Guid>().Contains(by.Id),
                ConditionOperator.GreaterThan when condition.AttributeName == idColumn => row.Id.CompareTo((Guid)condition.Values[0]) > 0,
                _ => throw new NotSupportedException($"Test: the scan does not model {condition.AttributeName} {condition.Operator}."),
            };
        }

        public static int Count(JobRunResult result, string name)
        {
            using var json = JsonDocument.Parse(result.ResultJson!);
            return json.RootElement.GetProperty(name).GetInt32();
        }

        public static string? Status(JobRunResult result)
        {
            using var json = JsonDocument.Parse(result.ResultJson!);
            return json.RootElement.GetProperty("status").GetString();
        }
    }
}

/// <summary>Task 052b: the repair world's seams for the scheduled pass.</summary>
internal sealed partial class RepairWorld
{
    /// <summary>The contact owner lookup the resolver composes (none by default: every contact's owner is refused).</summary>
    public ContactIdentityBinder? CustomerContactOwner { get; set; }

    /// <summary>Repair as the pass calls it.</summary>
    public Task<RecordRepairOutcome> RepairAsync(string table, Guid id, RecordRepairPassContext pass) =>
        Service().RepairAsync(table, id, CancellationToken.None, pass);

    /// <summary>The service the job resolves per run.</summary>
    public RecordCreationService ServiceForAPass() => Service();
}
