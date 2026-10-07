// unified-access-control-r2 task 142, criterion 16 — the Assigned-To safety net (every 5 minutes; writes ON for create,
// convert and renew; revoke-on-change behind its own switch — owner R3/(g) as amended by round 3 R3/R4). The PRODUCTION
// job and materializer run; the doubles are AssignedAccessTestDoubles' module boundaries.

using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Spaarke.Dataverse;
using Spaarke.Scheduling;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Services.ExternalAccess;
using Xunit;
using static Sprk.Bff.Api.Tests.AccessControl.AssignedAccessTestDoubles;

namespace Sprk.Bff.Api.Tests.Services.ExternalAccess;

public class AssignedAccessReconciliationJobTests
{
    private const string Attorney1 = "sprk_assignedattorney1";
    private const string DeploymentTenant = "00000000-0000-0000-0000-0000000000dd";

    private readonly Harness _h = new();

    private async Task<JobRunResult> RunAsync(
        bool? revokeOnChange = null, string? tenant = DeploymentTenant, AssignedAccessReconciliationJob? job = null)
    {
        var settings = new Dictionary<string, string?>();
        if (tenant is not null) settings["AzureAd:TenantId"] = tenant;
        if (revokeOnChange is { } on) settings[AssignedAccessReconciliationJob.RevokeOnChangeConfigKey] = on.ToString();
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        _h.Configuration = configuration;

        var services = new ServiceCollection();
        services.AddSingleton<AssignedAccessStore>(_h.Store);
        services.AddScoped(_ => _h.Materializer);
        services.AddScoped(_ => _h.RestrictedRemover);
        using var provider = services.BuildServiceProvider();

        job ??= new AssignedAccessReconciliationJob(
            provider.GetRequiredService<IServiceScopeFactory>(), _h.Time, configuration,
            NullLogger<AssignedAccessReconciliationJob>.Instance);
        return await job.ExecuteAsync(
            new JobRunContext(Guid.NewGuid(), "corr-142", JobRunTrigger.ManualAdmin, new Dictionary<string, object>()),
            CancellationToken.None);
    }

    private static JsonElement Result(JobRunResult result)
    {
        using var doc = JsonDocument.Parse(result.ResultJson!);
        return doc.RootElement.Clone();
    }

    private Guid AssignedMatter(Guid? contact = null)
    {
        var matter = Guid.NewGuid();
        _h.Store.Assign(ExternalGrantRootType.Matter, matter, Attorney1, contact ?? _h.Contact());
        return matter;
    }

    [Fact]
    public async Task ARun_MaterializesEveryRootWithAnAssignedColumn_AWriteMadeOutsideTheProduct()
    {
        var c1 = _h.Contact();
        var c2 = _h.Contact();
        var m1 = AssignedMatter(c1);
        var project = Guid.NewGuid();
        _h.Store.Assign(ExternalGrantRootType.Project, project, Attorney1, c2);

        var result = await RunAsync();

        result.Success.Should().BeTrue(result.ErrorMessage);
        _h.Grants.ActiveRowsOf(m1, c1).Should().ContainSingle();
        _h.Grants.ActiveRowsOf(project, c2).Should().ContainSingle();
        Result(result).GetProperty("candidates").GetInt32().Should().Be(2);
    }

    /// <summary>
    /// Task 158 r1: the inherited-share provenance rows (owner round 30) live in the same ledger table but are not
    /// Assigned-To rows — a root that holds only such rows is not this job's candidate, so they never count toward its scan
    /// bound (an environment with many inherited shares would otherwise truncate, and fail, every run).
    /// </summary>
    [Fact]
    public async Task ARootHoldingOnlyInheritedShareProvenance_IsNotACandidate()
    {
        var assigned = AssignedMatter();
        var filed = Guid.NewGuid();
        await _h.Store.CreateInheritedLedgerAsync(ExternalGrantRootType.WorkAssignment, filed, "sprk_matter", Guid.NewGuid(),
            DataversePrincipalRef.User(Guid.NewGuid()), new AssignedAccessLedgerWrite(AssignedAccessState.Shared, null, GrantedLevel: 1),
            CancellationToken.None);

        var result = await RunAsync();

        result.Success.Should().BeTrue(result.ErrorMessage);
        Result(result).GetProperty("candidates").GetInt32().Should().Be(1, $"only {assigned} has an Assigned-To column or row");
    }

    [Fact]
    public async Task ASecondRun_OverUnchangedRoots_MakesZeroWrites()
    {
        AssignedMatter();
        await RunAsync();
        var after = _h.TotalWrites;

        var second = await RunAsync();

        second.Success.Should().BeTrue(second.ErrorMessage);
        _h.TotalWrites.Should().Be(after);
        Result(second).GetProperty("writes").GetInt32().Should().Be(0);
    }

    [Fact]
    public async Task AFieldClearedOutsideTheProduct_IsRevisitedThroughItsLedgerRow_AndRevokedWhenTheSwitchIsOn()
    {
        var contact = _h.Contact();
        var matter = AssignedMatter(contact);
        await RunAsync();
        _h.Store.Assign(ExternalGrantRootType.Matter, matter, Attorney1, null); // a grid edit

        var result = await RunAsync(revokeOnChange: true);

        result.Success.Should().BeTrue(result.ErrorMessage);
        _h.Grants.ActiveRowsOf(matter, contact).Should().BeEmpty();
    }

    [Fact]
    public async Task WithTheSwitchOff_TheRemovalIsReportOnly_WouldRevoke_AndTheRunIsStillClean()
    {
        var contact = _h.Contact();
        var matter = AssignedMatter(contact);
        await RunAsync();
        _h.Store.Assign(ExternalGrantRootType.Matter, matter, Attorney1, null);

        var result = await RunAsync(revokeOnChange: null);

        result.Success.Should().BeTrue(result.ErrorMessage);
        _h.Grants.ActiveRowsOf(matter, contact).Should().ContainSingle("report-only keeps the access");
        var json = Result(result);
        json.GetProperty("wouldRevoke").GetInt32().Should().Be(1);
        json.GetProperty("revokeOnChange").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task TheRevokeSwitch_IsReportOnlyUnlessExplicitlyTrue()
    {
        var job = new AssignedAccessReconciliationJob(
            new ServiceCollection().BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(), _h.Time,
            new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                [AssignedAccessReconciliationJob.RevokeOnChangeConfigKey] = "yes",
            }).Build(),
            NullLogger<AssignedAccessReconciliationJob>.Instance);

        job.RevokeOnChangeEnabled.Should().BeFalse("an unparseable value is report-only");
    }

    [Fact]
    public void TheJobShipsEnabled_EveryFiveMinutes_PerOwnerR3R4()
    {
        AssignedAccessReconciliationJob.DefaultCronSchedule.Should().Be("*/5 * * * *");
        AssignedAccessReconciliationJob.JobIdConstant.Should().Be("assigned-access-reconciliation");
    }

    [Fact]
    public async Task ARunWhoseScanFaults_RecordsSuccessFalse_NeverNothingToReconcile()
    {
        AssignedMatter();
        _h.Store.FailScan = true;

        var result = await RunAsync();

        result.Success.Should().BeFalse();
        Result(result).GetProperty("status").GetString().Should().Be(AssignedAccessReconciliationJob.StatusError);
        result.ErrorMessage.Should().Contain("could not be read");
    }

    [Fact]
    public async Task ATruncatedScan_IsReportedTruncated_NotClean()
    {
        AssignedMatter();
        AssignedMatter();
        _h.Store.ScanCeiling = 1;

        var result = await RunAsync();

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("TRUNCATED");
        Result(result).GetProperty("truncated").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task MoreRootsThanOneRunTakes_Rotate_AndEveryRootIsVisitedWithinTwoRuns()
    {
        var matters = Enumerable.Range(0, AssignedAccessReconciliationJob.MaxRootsPerRun + 5).Select(_ => AssignedMatter()).ToList();
        var services = new ServiceCollection();
        services.AddSingleton<AssignedAccessStore>(_h.Store);
        services.AddScoped(_ => _h.Materializer);
        services.AddScoped(_ => _h.RestrictedRemover);
        using var provider = services.BuildServiceProvider();
        var job = new AssignedAccessReconciliationJob(
            provider.GetRequiredService<IServiceScopeFactory>(), _h.Time,
            new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["AzureAd:TenantId"] = DeploymentTenant }).Build(),
            NullLogger<AssignedAccessReconciliationJob>.Instance);

        var first = await RunAsync(job: job);
        var granted = matters.Count(m => _h.Grants.ActiveRowsOf(m).Count == 1);
        await RunAsync(job: job);

        first.ErrorMessage.Should().Contain("ROTATING");
        Result(first).GetProperty("rotating").GetBoolean().Should().BeTrue();
        granted.Should().Be(AssignedAccessReconciliationJob.MaxRootsPerRun);
        matters.Should().OnlyContain(m => _h.Grants.ActiveRowsOf(m).Count == 1, "the second run reaches the rest");
    }

    [Fact]
    public async Task ARecentlyModifiedRoot_IsTakenFirst_WhenTheRunRotates()
    {
        var old = Enumerable.Range(0, AssignedAccessReconciliationJob.MaxRootsPerRun + 5).Select(_ => AssignedMatter()).ToList();
        var recent = AssignedMatter();
        _h.Store.Modified[recent] = _h.Time.GetUtcNow().AddMinutes(-1);

        await RunAsync();

        _h.Grants.ActiveRowsOf(recent).Should().ContainSingle("a grid edit is picked up on the next tick");
    }

    [Fact]
    public async Task AShareWrittenByTheJob_ClearsTheUsersRootSet_UnderTheDeploymentTenant_NeverAnonymous()
    {
        var (contact, user) = _h.LinkedContact();
        var matter = AssignedMatter(contact);

        var result = await RunAsync();

        result.Success.Should().BeTrue(result.ErrorMessage);
        _h.Cache.Removed.Should().ContainSingle().Which.Should().Be(
            (DeploymentTenant, ImpersonatedRootSetSource.CacheResource, ImpersonatedRootSetSource.CacheId(user, "sprk_matter")),
            "the key ImpersonatedRootSetSource.GetAsync reads for that user");
    }

    [Fact]
    public async Task WithNoDeploymentTenant_NothingIsCleared_AndTheRunSaysSo()
    {
        var (contact, _) = _h.LinkedContact();
        AssignedMatter(contact);

        var result = await RunAsync(tenant: null);

        _h.Cache.Removed.Should().BeEmpty("never the 'anonymous' key");
        result.ErrorMessage.Should().Contain("AzureAd:TenantId");
        Result(result).GetProperty("cacheTenantConfigured").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task ARootTheMaterializerCouldNotComplete_FailsTheRun()
    {
        var (contact, _) = _h.LinkedContact();
        AssignedMatter(contact);
        _h.Shares.IgnoreWrites = true; // the share is never confirmed

        var result = await RunAsync();

        result.Success.Should().BeFalse();
        Result(result).GetProperty("incompleteTotal").GetInt32().Should().Be(1);
    }

    /// <summary>
    /// Task 142 r3 (verifier r2 finding 4): a No Access check that THROWS is a deny-list read FAULT — the run fails and counts
    /// it by name, so a sustained outage of that read is visible to monitoring. The twin: an ENTRY on the list is the
    /// record's policy (a skip, nothing granted either way), and the run stays clean.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ANoAccessCheckThatThrows_FailsTheRun_CountedAsADenyListFault_WhileAnEntryOnTheListDoesNot(bool throws)
    {
        var contact = _h.Contact();
        var matter = AssignedMatter(contact);
        if (throws)
            _h.DenyList.Throws = new TaskCanceledException("Simulated HttpClient timeout (the caller did not cancel).");
        else
            _h.DenyList.DenyContactOnRecord(contact, matter);

        var result = await RunAsync();

        _h.Grants.ActiveRowsOf(matter, contact).Should().BeEmpty("fail closed either way");
        var json = Result(result);
        if (throws)
        {
            result.Success.Should().BeFalse();
            result.ErrorMessage.Should().Contain("DENY-LIST-UNREADABLE");
            json.GetProperty("denyListUnreadable").GetInt32().Should().Be(1);
            json.GetProperty("incompleteTotal").GetInt32().Should().Be(1);
        }
        else
        {
            result.Success.Should().BeTrue(result.ErrorMessage);
            json.GetProperty("denyListUnreadable").GetInt32().Should().Be(0);
        }
    }

    /// <summary>
    /// Task 142 r4 (owner round 13 items 4 and 5): the faults that never THREW now fail the run too, counted by name —
    /// the deny-veto check's Unverifiable answer (memberships unreadable, a fail-closed deny-list read: before r4 a clean
    /// run with a <c>no-access</c> skip) and task 143's wall guard answering Unverifiable on a secure record's share path
    /// (before r4 a clean run with a quiet skip). The clean twin is the "entry on the list" row of the theory above.
    /// </summary>
    [Theory]
    [InlineData("memberships-unreadable")]
    [InlineData("deny-list-fails-closed")]
    [InlineData("wall-unverifiable-on-a-secure-share")]
    public async Task AnUnverifiableNoAccessAnswer_FailsTheRun_CountedAsADenyListFault(string fault)
    {
        Guid matter;
        switch (fault)
        {
            case "memberships-unreadable":
                matter = AssignedMatter();
                _h.Participations.MembershipsUnreadable = true;
                break;
            case "deny-list-fails-closed":
                matter = AssignedMatter();
                _h.DenyList.Faults = true;
                break;
            default:
                var (contact, _) = _h.LinkedContact();
                matter = AssignedMatter(contact);
                _h.Participations.Flags[matter] = new RootRecordFlags(IsSecure: true, IsRestricted: false);
                _h.DenyList.Faults = true;
                break;
        }

        var result = await RunAsync();

        result.Success.Should().BeFalse("a No Access read fault is reported — the job goes red");
        result.ErrorMessage.Should().Contain("DENY-LIST-UNREADABLE");
        var json = Result(result);
        json.GetProperty("denyListUnreadable").GetInt32().Should().Be(1);
        json.GetProperty("incompleteTotal").GetInt32().Should().Be(1);
        _h.Grants.Rows.Should().BeEmpty("fail closed");
        _h.Shares.Writes.Should().BeEmpty("fail closed");
    }

    // ── Owner round 71: ledger rows whose record was deleted ──────────────────────────────────────────

    /// <summary>A matter is deleted after its auto grant was made: its ledger row is marked Revoked (root-deleted), and no
    /// access is touched by this job — the grant row is ExternalAccessReconciliationJob R4's.</summary>
    [Fact]
    public async Task ALedgerRowWhoseRecordWasDeleted_IsMarkedRevoked_RootDeleted_AndNoAccessIsTouched()
    {
        var contact = _h.Contact();
        var matter = AssignedMatter(contact);
        await RunAsync();
        var row = _h.Store.RowsOf(matter, contact).Single();
        var grantsBefore = _h.Grants.Rows.Count;
        _h.Store.DeleteRoot(ExternalGrantRootType.Matter, matter);

        var result = await RunAsync();

        result.Success.Should().BeTrue(result.ErrorMessage);
        var after = _h.Store.Ledger.Single(r => r.Id == row.Id);
        after.State.Should().Be(AssignedAccessState.Revoked);
        after.Reason.Should().Be(AssignedAccessReason.RootDeleted);
        _h.Grants.Rows.Count.Should().Be(grantsBefore, "the job changes no access for a deleted record");
        var rootless = Result(result).GetProperty("rootlessLedger");
        rootless.GetProperty("found").GetInt32().Should().Be(1);
        rootless.GetProperty("revoked").GetInt32().Should().Be(1);
        result.ProcessedItems.Should().Be(1);

        var again = await RunAsync();
        Result(again).GetProperty("rootlessLedger").GetProperty("found").GetInt32().Should().Be(0, "a Revoked row is not revisited");
    }

    /// <summary>The lookup is empty but the record its key names is still there: not a deletion — left exactly as it is.</summary>
    [Fact]
    public async Task ALedgerRowWithNoRecordLookup_WhoseRecordStillExists_IsLeftUnchanged()
    {
        var contact = _h.Contact();
        var matter = AssignedMatter(contact);
        await RunAsync();
        var row = _h.Store.RowsOf(matter, contact).Single();
        var state = row.State;
        _h.Store.Assign(ExternalGrantRootType.Matter, matter, Attorney1, null); // no longer a candidate root
        _h.Store.ClearRootLookup(matter);

        var result = await RunAsync();

        result.Success.Should().BeTrue(result.ErrorMessage);
        _h.Store.Ledger.Single(r => r.Id == row.Id).State.Should().Be(state);
        Result(result).GetProperty("rootlessLedger").GetProperty("recordExists").GetInt32().Should().Be(1);
    }

    /// <summary>A read of the record that FAILS is never read as "deleted": nothing is written and the run is not clean.</summary>
    [Fact]
    public async Task ALedgerRowWhoseRecordCannotBeRead_IsLeftUnchanged_AndTheRunIsNotClean()
    {
        var contact = _h.Contact();
        var matter = AssignedMatter(contact);
        await RunAsync();
        var row = _h.Store.RowsOf(matter, contact).Single();
        var state = row.State;
        _h.Store.DeleteRoot(ExternalGrantRootType.Matter, matter);
        _h.Store.FailRootRead = true;

        var result = await RunAsync();

        result.Success.Should().BeFalse();
        _h.Store.Ledger.Single(r => r.Id == row.Id).State.Should().Be(state);
        Result(result).GetProperty("rootlessLedger").GetProperty("unresolved").GetInt32().Should().Be(1);
        Result(result).GetProperty("rootlessLedger").GetProperty("revoked").GetInt32().Should().Be(0);
    }

    [Fact]
    public async Task ARootlessLedgerScanThatFails_RecordsTheRunFailed_AndWritesNothing()
    {
        var contact = _h.Contact();
        var matter = AssignedMatter(contact);
        await RunAsync();
        var row = _h.Store.RowsOf(matter, contact).Single();
        var state = row.State;
        _h.Store.DeleteRoot(ExternalGrantRootType.Matter, matter);
        _h.Store.FailRootlessScan = true;

        var result = await RunAsync();

        result.Success.Should().BeFalse();
        Result(result).GetProperty("status").GetString().Should().Be(AssignedAccessReconciliationJob.StatusError);
        Result(result).GetProperty("rootlessLedger").GetProperty("scanFailed").GetBoolean().Should().BeTrue();
        _h.Store.Ledger.Single(r => r.Id == row.Id).State.Should().Be(state);
    }

    [Fact]
    public void TheLedgerKey_NamesItsRecord_SoADeletedRecordCanBeConfirmedByARead()
    {
        var matter = Guid.NewGuid();
        var key = AssignedAccessStore.LedgerKey(ExternalGrantRootType.Matter, matter, Attorney1,
            new AssignedSubject(AssignedSubjectKind.Contact, Guid.NewGuid()));

        AssignedAccessStore.RootFromLedgerKey(key).Should().Be(new AssignedRootRef(ExternalGrantRootType.Matter, matter, null));
        AssignedAccessStore.RootFromLedgerKey(null).Should().BeNull();
        AssignedAccessStore.RootFromLedgerKey("not-a-key").Should().BeNull();
        AssignedAccessStore.RootFromLedgerKey($"account:{matter:D}:x").Should().BeNull("only a project, matter or work assignment");
    }

    // ── Task 114 (owner round 67 amendment 4(b)): the backstop for a share made on a Restricted record afterwards ──

    /// <summary>
    /// A Restricted record with no Assigned column is still a candidate: an external-flagged user's share made out of band
    /// (the platform's Share dialog) is removed, and the run says so. An internal user's share stays.
    /// </summary>
    [Fact]
    public async Task ARestrictedRoot_WithAShareOfAUserFlaggedExternal_HasItRemoved_AndTheRunReportsIt()
    {
        var matter = Guid.NewGuid();
        _h.Store.RestrictedRoots[(ExternalGrantRootType.Matter, matter)] = true;
        _h.Participations.Flags[matter] = new RootRecordFlags(IsSecure: false, IsRestricted: true);
        var external = _h.SystemUser(isExternal: true);
        var internalUser = _h.SystemUser(isExternal: null);
        _h.Shares.Seed("sprk_matter", matter, DataversePrincipalRef.User(external), 1);
        _h.Shares.Seed("sprk_matter", matter, DataversePrincipalRef.User(internalUser), 1);

        var result = await RunAsync();

        result.Success.Should().BeTrue(result.ErrorMessage);
        _h.Shares.MaskOf("sprk_matter", matter, DataversePrincipalRef.User(external)).Should().BeNull();
        _h.Shares.MaskOf("sprk_matter", matter, DataversePrincipalRef.User(internalUser)).Should().Be(1);
        var json = Result(result);
        json.GetProperty("restrictedCandidates").GetInt32().Should().Be(1);
        json.GetProperty("externalSharesRemoved").GetInt32().Should().Be(1);
        json.GetProperty("materialized").GetInt32().Should().Be(0, "a Restricted root with no Assigned column is not materialized");
    }

    /// <summary>
    /// Restricted wins over the last-reader rule (owner round 67 item 3): the only reader, flagged external, is removed; the
    /// record is counted noInternalReader for an administrator, and the run stays ok.
    /// </summary>
    [Fact]
    public async Task ARestrictedSecureRoot_WhoseOnlyReaderIsFlaggedExternal_LosesTheShare_IsCountedNoInternalReader_AndTheRunStaysOk()
    {
        var matter = Guid.NewGuid();
        _h.Store.RestrictedRoots[(ExternalGrantRootType.Matter, matter)] = true;
        _h.Participations.Flags[matter] = new RootRecordFlags(IsSecure: true, IsRestricted: true);
        var external = _h.SystemUser(isExternal: true);
        _h.Shares.Seed("sprk_matter", matter, DataversePrincipalRef.User(external), 1);

        var result = await RunAsync();

        result.Success.Should().BeTrue(result.ErrorMessage);
        _h.Shares.MaskOf("sprk_matter", matter, DataversePrincipalRef.User(external)).Should().BeNull();
        Result(result).GetProperty("noInternalReader").GetInt32().Should().Be(1);
        Result(result).GetProperty("externalSharesRemoved").GetInt32().Should().Be(1);
    }

    /// <summary>
    /// A Restricted record OWNED by a user flagged external: counted (ownerIsExternal) and logged by the remover for an
    /// administrator to reassign — never revoked, and it does not keep the run partial (follow-up item 2).
    /// </summary>
    [Fact]
    public async Task ARestrictedRootOwnedByAUserFlaggedExternal_IsCounted_AndTheRunStaysOk()
    {
        var matter = Guid.NewGuid();
        _h.Store.RestrictedRoots[(ExternalGrantRootType.Matter, matter)] = true;
        _h.Participations.Flags[matter] = new RootRecordFlags(IsSecure: false, IsRestricted: true);
        var owner = _h.SystemUser(isExternal: true);
        _h.Shares.Seed("sprk_matter", matter, DataversePrincipalRef.User(owner), 1);
        _h.Grants.RootOwners[matter] = owner;

        var result = await RunAsync();

        result.Success.Should().BeTrue(result.ErrorMessage);
        Result(result).GetProperty("ownerIsExternal").GetInt32().Should().Be(1);
        _h.Shares.MaskOf("sprk_matter", matter, DataversePrincipalRef.User(owner)).Should().Be(1, "ownership is never touched");
    }

    /// <summary>A Restricted record's removal that cannot be confirmed fails the run (the root is incomplete).</summary>
    [Fact]
    public async Task ARestrictedRoot_WhoseExternalShareCannotBeRemoved_FailsTheRun()
    {
        var matter = Guid.NewGuid();
        _h.Store.RestrictedRoots[(ExternalGrantRootType.Matter, matter)] = true;
        _h.Participations.Flags[matter] = new RootRecordFlags(IsSecure: false, IsRestricted: true);
        _h.Shares.Seed("sprk_matter", matter, DataversePrincipalRef.User(_h.SystemUser(isExternal: true)), 1);
        _h.Shares.IgnoreWrites = true;

        var result = await RunAsync();

        result.Success.Should().BeFalse();
        Result(result).GetProperty("incompleteTotal").GetInt32().Should().Be(1);
    }

    [Fact]
    public async Task ALinkThatAppearsBetweenRuns_ConvertsTheGrantToAShare()
    {
        var contact = _h.Contact();
        var matter = AssignedMatter(contact);
        await RunAsync();
        var user = _h.LinkLater(contact);

        await RunAsync();

        _h.Grants.ActiveRowsOf(matter, contact).Should().BeEmpty();
        _h.Shares.MaskOf("sprk_matter", matter, DataversePrincipalRef.User(user)).Should().NotBeNull();
    }
}
