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
    // Review F1-a: an owner write is an Assign — never written where it would cascade
    // =====================================================================================

    private static readonly AssignCascadeRelationship ContactTasks = new("Contact_Tasks", "task", "Cascade");

    [Fact]
    public async Task AContactTableThatCascadesAssignToAChild_IsNotReOwned_IsReportedAsAnAnswer_AndLoggedOncePerPass()
    {
        _pass.CustomerTeamResolves();
        World.AssignCascades["contact"] = [ContactTasks];
        var first = _pass.NativeContact();
        var second = _pass.NativeContact();

        var result = await _pass.RunAsync();

        World.Writes.Should().NotContain(w => w.Fields.ContainsKey("ownerid"), "re-owning a contact would re-own its tasks too");
        World.Directory.Row("contact", first).GetAttributeValue<EntityReference>("ownerid")
            .Should().Be(new EntityReference("systemuser", RepairWorld.PersonInChild));
        World.Directory.Row("contact", second).GetAttributeValue<EntityReference>("ownerid")
            .Should().Be(new EntityReference("systemuser", RepairWorld.PersonInChild));
        PassWorld.Count(result, "ownerAssignCascades").Should().Be(2);
        PassWorld.Count(result, "pendingRetry").Should().Be(0, "an answer, not a fault: not asked again");
        World.AssignCascadeReads.Where(t => t == "contact").Should().ContainSingle("the relationships are read once per pass");
        World.Log.Entries.Where(e => e.Message.Contains("Contact_Tasks -> task")).Should().ContainSingle("logged once per pass");

        var direct = await World.RepairAsync("contact", first, _pass.Context());
        direct.Fields.Should().ContainSingle(f => f.Part == RecordRepairPart.Owner)
            .Which.ReasonCode.Should().Be(RecordRepairReason.OwnerAssignCascades);
        direct.NeedsRetry.Should().BeFalse();
    }

    [Fact]
    public async Task ATableThatCascadesAssign_StillGetsItsMapping_WithoutTheOwner()
    {
        World.AssignCascades["sprk_event"] = [new AssignCascadeRelationship("sprk_event_children", "sprk_memo", null)];
        var id = _pass.NativeEvent();

        var result = await _pass.RunAsync();

        var write = World.Writes.Should().ContainSingle().Subject;
        write.Id.Should().Be(id);
        write.Fields["sprk_clientname"].Should().Be(RepairWorld.MatterClient, "the mapping is still applied");
        write.Fields.Should().NotContainKey("ownerid", "an Assign cascade that the metadata does not state counts as cascading");
        PassWorld.Count(result, "ownerAssignCascades").Should().Be(1);
    }

    [Fact]
    public async Task AContactTableWithNoCascadingRelationship_IsReOwned()
    {
        _pass.CustomerTeamResolves();
        World.AssignCascades["contact"] = [];
        var id = _pass.NativeContact();

        var result = await _pass.RunAsync();

        World.Writes.Should().ContainSingle().Which.Fields["ownerid"]
            .Should().Be(new EntityReference("team", CustomerContactOwnerTests.CustomerTeam));
        PassWorld.Count(result, "ownerAssignCascades").Should().Be(0);
        World.AssignCascadeReads.Should().Contain("contact");
        World.Directory.Row("contact", id).GetAttributeValue<EntityReference>("ownerid")
            .Should().Be(new EntityReference("team", CustomerContactOwnerTests.CustomerTeam));
    }

    [Fact]
    public void TheAcceptedAssignCascadeList_StartsEmpty()
    {
        RecordCreationService.AcceptedAssignCascades.Should().BeEmpty("a child is allowed only by a reasoned entry");
    }

    [Fact]
    public async Task ATableWhoseRelationshipMetadataCannotBeRead_IsNotReOwned_AndIsAskedAgain()
    {
        _pass.CustomerTeamResolves();
        World.AssignCascadeFaults["contact"] = new TimeoutException("metadata read timed out");
        var id = _pass.NativeContact();

        var first = await _pass.RunAsync();
        World.AssignCascadeFaults.Clear();
        var second = await _pass.RunAsync();

        PassWorld.Count(first, "pendingRetry").Should().Be(1, "a fault is asked again");
        first.Success.Should().BeFalse("work is incomplete");
        PassWorld.Count(second, "retried").Should().Be(1);
        World.Directory.Row("contact", id).GetAttributeValue<EntityReference>("ownerid")
            .Should().Be(new EntityReference("team", CustomerContactOwnerTests.CustomerTeam), "the next run re-owned it");
    }

    [Fact]
    public async Task AFailedRelationshipMetadataRead_IsKeptAsUnknownForThePass_ReadAndLoggedOncePerTable()
    {
        _pass.CustomerTeamResolves();
        World.AssignCascadeFaults["contact"] = new TimeoutException("metadata read timed out");
        _pass.NativeContact();
        _pass.NativeContact();

        var result = await _pass.RunAsync();

        World.AssignCascadeReads.Where(t => t == "contact").Should().ContainSingle("a failed read is kept for the rest of the pass");
        World.Log.Entries.Where(e => e.Message.Contains("relationship metadata of contact")).Should().ContainSingle(
            "logged once per table per pass, not per row");
        PassWorld.Count(result, "pendingRetry").Should().Be(2, "unknown is a fault: both rows are asked again next run");
        World.Writes.Should().NotContain(w => w.Fields.ContainsKey("ownerid"));
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

    [Theory]
    [InlineData(CustomerContactOwnerTests.SecureUnitParent.OwnedByTheSecureTeam_FlagUnset)]
    [InlineData(CustomerContactOwnerTests.SecureUnitParent.OwnedByTheSecureUnitsDefaultTeam)]
    [InlineData(CustomerContactOwnerTests.SecureUnitParent.OwnedByAnotherSecureUnitTeam)]
    public async Task AContactUnderAParentSecureByOwnership_IsNeverReOwnedToTheCustomerTeam(CustomerContactOwnerTests.SecureUnitParent kind)
    {
        // UAC-r2 pin: the parent is secure by ownership only (its sprk_issecure unset); the contact stays in isolation.
        _pass.CustomerTeamResolves();
        var invoice = CustomerContactOwnerTests.InvoiceInTheSecureUnit(World.Directory, kind);
        var id = _pass.NativeContactUnder(invoice);

        var outcome = await World.RepairAsync("contact", id, _pass.Context());

        World.Writes.Should().NotContain(w => w.Fields.ContainsKey("ownerid"), "never re-owned out of the invoice's isolation");
        outcome.OwnerRefusal!.OwnerDecision!.IsSecureOwner.Should().BeTrue("the resolver named the Secure team");
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
    public async Task AContactWhoseCustomerTeamCannotBeResolved_KeepsItsOwner_AndWaits_UntilTheTeamResolves()
    {
        // Review F2-b: the table is not swept while the team cannot be resolved — its watermark does not move, so the contact is
        // repaired by the first run after the team resolves.
        _pass.CustomerTeamUnresolvable();
        var id = _pass.NativeContact();

        var first = await _pass.RunAsync();

        World.Writes.Should().BeEmpty("never given its creator's team in place of the customer team");
        World.Directory.Row("contact", id).GetAttributeValue<EntityReference>("ownerid")
            .Should().Be(new EntityReference("systemuser", RepairWorld.PersonInChild));
        first.Success.Should().BeFalse("the contact table waits");
        PassWorld.Status(first).Should().Be(RecordRepairPassJob.StatusPartial);
        PassWorld.Count(first, "tablesWaiting").Should().Be(1);
        PassWorld.Count(first, "ownerRefused").Should().Be(0, "no contact is asked");
        PassWorld.Count(first, "pendingRetry").Should().Be(0, "no attempt is counted");
        _pass.State("contact").Should().BeNull("the watermark does not move");
        _pass.ScanQueries.Should().NotContain(q => q.EntityName == "contact");
        _pass.Log.Entries.Where(e => e.Message.Contains("cannot be resolved") && e.Message.Contains("contact"))
            .Should().ContainSingle("one line per run");

        _pass.Time.Advance(TimeSpan.FromMinutes(5));
        _pass.CustomerTeamResolves();
        var second = await _pass.RunAsync();

        PassWorld.Count(second, "tablesWaiting").Should().Be(0);
        World.Directory.Row("contact", id).GetAttributeValue<EntityReference>("ownerid")
            .Should().Be(new EntityReference("team", CustomerContactOwnerTests.CustomerTeam), "repaired once the team resolves");
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
        PassWorld.Count(result, "tablesWaiting").Should().Be(1);
        result.Success.Should().BeFalse("the contact waits for the setting");
        var direct = await World.RepairAsync("contact", id, _pass.Context());
        direct.OwnerRefusal!.Code.Should().Be(RecordOwnerRefusal.CustomerContactTeamUnresolved, "Repair itself still refuses");
    }

    [Fact]
    public async Task ACustomerTeamLookupThatFaults_LeavesTheContactTableUnswept()
    {
        var store = new InMemoryContactIdentityStore { FailTeamLookup = true };
        store.DefaultTeams[CustomerContactOwnerTests.CustomerBu] = [CustomerContactOwnerTests.CustomerTeam];
        World.CustomerContactOwner = IdentityBindingTestKit.OwnerAwareBinder(store, CustomerContactOwnerTests.CustomerBu);
        _pass.NativeContact();

        var result = await _pass.RunAsync();

        PassWorld.Count(result, "tablesWaiting").Should().Be(1);
        _pass.State("contact").Should().BeNull();
        World.Writes.Should().BeEmpty();
    }

    [Fact]
    public async Task AContactRefusedForTheCustomerTeam_UsesNoAttempt_OnlyFaultsDo()
    {
        // The gate saw the team, the row's own answer did not (the setting changed between the two): the row waits in the retry
        // set without using up an attempt, so it is never abandoned for a configuration.
        var gateStore = new InMemoryContactIdentityStore();
        gateStore.DefaultTeams[CustomerContactOwnerTests.CustomerBu] = [CustomerContactOwnerTests.CustomerTeam];
        _pass.GateLookup = IdentityBindingTestKit.OwnerAwareBinder(gateStore, CustomerContactOwnerTests.CustomerBu);
        _pass.CustomerTeamUnresolvable();
        var id = _pass.NativeContact();
        var nearTheLimit = RecordRepairPassJob.MaxAttempts - 1;
        _pass.SeedState("contact", "{\"retry\":{\"" + id.ToString("D") + "\":" + nearTheLimit + "}}");

        var result = await _pass.RunAsync();

        PassWorld.Count(result, "abandoned").Should().Be(0, "a configuration wait is not a fault");
        PassWorld.Count(result, "pendingRetry").Should().Be(1);
        _pass.State("contact").Should().Contain($"\"{id:D}\":{nearTheLimit}", "the attempt count did not move");
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
    public async Task WithGoLiveUnset_TheJobReadsAndWritesNothing_AndLogsAWarning()
    {
        _pass.Settings.Remove(RecordRepairPassJob.GoLiveConfigKey);
        _pass.NativeEvent();

        var result = await _pass.RunAsync();

        _pass.WhoAmICalls.Should().Be(0);
        _pass.ScanQueries.Should().BeEmpty();
        World.Writes.Should().BeEmpty();
        _pass.Job().Tables.Should().AllSatisfy(t => _pass.State(t).Should().BeNull("no watermark or other state is written"));
        PassWorld.Status(result).Should().Be(RecordRepairPassJob.StatusNotConfigured);
        // Review K9: off is the default until go-live is chosen — a warning, never an error.
        _pass.Log.Entries.Should().Contain(e => e.Level == LogLevel.Warning && e.Message.Contains(RecordRepairPassJob.GoLiveConfigKey));
        _pass.Log.Entries.Should().NotContain(e => e.Level >= LogLevel.Error);
    }

    [Theory]
    [InlineData("next tuesday")]
    [InlineData("11/01/2026")]
    [InlineData("2026-11-01")]
    [InlineData("2026-11-01T00:00:00")]
    public async Task WithGoLiveNotAnIsoRoundTripInstant_TheJobReadsAndWritesNothing(string value)
    {
        // Review K7: only the "O" form, which carries its offset — never a culture's date or a local time read as UTC.
        _pass.Settings[RecordRepairPassJob.GoLiveConfigKey] = value;
        _pass.NativeEvent();

        var result = await _pass.RunAsync();

        result.Success.Should().BeFalse();
        _pass.ScanQueries.Should().BeEmpty();
        World.Writes.Should().BeEmpty();
    }

    [Fact]
    public async Task WithGoLiveAsAnIsoRoundTripInstant_InAnotherOffset_ThePassUsesThatInstant()
    {
        _pass.Settings[RecordRepairPassJob.GoLiveConfigKey] = PassWorld.GoLive.ToOffset(TimeSpan.FromHours(-5)).ToString("O");
        _pass.NativeEvent();

        var result = await _pass.RunAsync();

        result.Success.Should().BeTrue(result.ErrorMessage);
        _pass.ScanQueries.First(q => q.EntityName == "sprk_event").Criteria.Conditions
            .Single(c => c.Operator == ConditionOperator.GreaterEqual).Values[0]
            .Should().Be(PassWorld.GoLive.UtcDateTime);
    }

    // =====================================================================================
    // Review K5: each window overlaps the last by 15 minutes
    // =====================================================================================

    [Fact]
    public async Task ANewWindow_StartsAQuarterHourBeforeTheWatermark_AndARowReadTwiceIsWrittenOnce()
    {
        var id = _pass.NativeEvent();
        await _pass.RunAsync();
        var firstEnd = (DateTime)_pass.ScanQueries.Last(q => q.EntityName == "sprk_event").Criteria.Conditions
            .Single(c => c.Operator == ConditionOperator.LessThan).Values[0];
        _pass.Time.Advance(TimeSpan.FromMinutes(5));

        await _pass.RunAsync();

        var second = _pass.ScanQueries.Last(q => q.EntityName == "sprk_event").Criteria.Conditions
            .Single(c => c.Operator == ConditionOperator.GreaterEqual).Values[0];
        second.Should().Be(firstEnd - RecordRepairPassJob.WindowOverlap);
        World.Writes.Where(w => w.Id == id).Should().ContainSingle("Repair is fill-only: the second read writes nothing");
    }

    [Fact]
    public async Task TheOverlap_NeverReachesBeforeGoLive()
    {
        var goLive = PassWorld.Now - TimeSpan.FromMinutes(10);
        _pass.Settings[RecordRepairPassJob.GoLiveConfigKey] = goLive.ToString("O");
        await _pass.RunAsync();
        _pass.Time.Advance(TimeSpan.FromMinutes(5));

        await _pass.RunAsync();

        var sinces = _pass.ScanQueries.Where(q => q.EntityName == "sprk_event")
            .Select(q => (DateTime)q.Criteria.Conditions.Single(c => c.Operator == ConditionOperator.GreaterEqual).Values[0])
            .ToList();
        sinces.Should().HaveCount(2);
        sinces.Should().OnlyContain(since => since == goLive.UtcDateTime, "a watermark eight minutes after go-live less 15 is before it");
    }

    // =====================================================================================
    // Review K6: the Web API client's managed identity is a product principal
    // =====================================================================================

    [Fact]
    public async Task TheWebApiClientsManagedIdentity_IsAProductPrincipal_ItsRowsAreNotRepaired()
    {
        var clientId = Guid.Parse("a052b000-0000-4000-8000-0000000000d1");
        var webApiUser = Guid.Parse("a052b000-0000-4000-8000-0000000000d2");
        _pass.Settings[RecordRepairPassJob.WebApiIdentityConfigKey] = clientId.ToString("D");
        _pass.ApplicationUsers[clientId] = [webApiUser];
        _pass.NativeEvent(createdBy: webApiUser);
        var native = _pass.NativeEvent();

        var result = await _pass.RunAsync();

        result.Success.Should().BeTrue(result.ErrorMessage);
        World.Writes.Should().ContainSingle().Which.Id.Should().Be(native, "the Web API client's row is the product's");
        _pass.ScanQueries.First(q => q.EntityName == "sprk_event").Criteria.Conditions
            .Single(c => c.Operator == ConditionOperator.NotIn).Values.Should().Contain(webApiUser);
    }

    [Fact]
    public async Task TheFallbackKey_NamesTheWebApiClientsIdentity_WhenThePrimaryIsUnset()
    {
        var clientId = Guid.Parse("a052b000-0000-4000-8000-0000000000d3");
        var webApiUser = Guid.Parse("a052b000-0000-4000-8000-0000000000d4");
        _pass.Settings[RecordRepairPassJob.WebApiIdentityFallbackConfigKey] = clientId.ToString("D");
        _pass.ApplicationUsers[clientId] = [webApiUser];
        _pass.NativeEvent(createdBy: webApiUser);

        await _pass.RunAsync();

        World.Writes.Should().BeEmpty();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public async Task AWebApiIdentityThatIsNotOneApplicationUser_ReadsNoTable(int matches)
    {
        var clientId = Guid.Parse("a052b000-0000-4000-8000-0000000000d5");
        _pass.Settings[RecordRepairPassJob.WebApiIdentityConfigKey] = clientId.ToString("D");
        _pass.ApplicationUsers[clientId] = Enumerable.Range(0, matches).Select(_ => Guid.NewGuid()).ToList();
        _pass.NativeEvent();

        var result = await _pass.RunAsync();

        result.Success.Should().BeFalse("the product principals cannot be decided");
        _pass.ScanQueries.Should().BeEmpty();
        World.Writes.Should().BeEmpty();
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
            CustomerTeamResolves(); // the configured environment; a test that needs the team unresolvable says so
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

        /// <summary>Seeds <paramref name="table"/>'s state as an earlier run would have saved it.</summary>
        public void SeedState(string table, string json) =>
            _cache.SetString($"{RecordRepairPassJob.JobIdConstant}:{table}:state", json);

        /// <summary>Review F2-b: the customer-team lookup the job's gate asks, when it differs from the resolver's.</summary>
        public ContactIdentityBinder? GateLookup { get; set; }

        /// <summary>Review K6: the application users by <c>applicationid</c> (the Web API client's managed identity).</summary>
        public Dictionary<Guid, List<Guid>> ApplicationUsers { get; } = [];

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
            if (!_customerTeamSeeded)
            {
                World.Directory.WithTeam(CustomerContactOwnerTests.CustomerTeam, CustomerContactOwnerTests.CustomerBu, isDefault: true, teamType: 0);
                _customerTeamSeeded = true;
            }

            World.CustomerContactOwner = IdentityBindingTestKit.OwnerAwareBinder(store, CustomerContactOwnerTests.CustomerBu);
        }

        private bool _customerTeamSeeded;

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

            var collection = new ServiceCollection()
                .AddSingleton(entities.Object)
                .AddSingleton<IIdempotencyService>(Claims)
                .AddSingleton<IDistributedCache>(_cache)
                .AddScoped(_ => World.ServiceForAPass());
            if ((GateLookup ?? World.CustomerContactOwner) is { } lookup)
                collection.AddSingleton(lookup);
            var services = collection.BuildServiceProvider();

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
            if (query.EntityName == "systemuser")
            {
                var applicationId = (Guid)query.Criteria.Conditions.Single(c => c.AttributeName == "applicationid").Values[0];
                return new EntityCollection(ApplicationUsers.GetValueOrDefault(applicationId, [])
                    .Select(id => new Entity("systemuser", id)).ToList());
            }

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
