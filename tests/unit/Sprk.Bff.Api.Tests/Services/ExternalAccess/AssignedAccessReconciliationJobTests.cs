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
