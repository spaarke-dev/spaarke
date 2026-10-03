// unified-access-control-r2 task 143, criterion 12 — the No Access safety net (every 5 minutes, writes ON, owner R4 as
// answered in round 3). The PRODUCTION job and enforcer run; the doubles are NoAccessEnforcementTestDoubles' module
// boundaries (store reads, participation reads, identity rows, the POA share table, a recording tenant cache).

using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Spaarke.Dataverse;
using Spaarke.Scheduling;
using Sprk.Bff.Api.Infrastructure.DI;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Services.ExternalAccess;
using Xunit;
using static Sprk.Bff.Api.Tests.AccessControl.NoAccessEnforcementTestDoubles;

namespace Sprk.Bff.Api.Tests.Services.ExternalAccess;

public class NoAccessShareReconciliationJobTests
{
    private const string Project = "sprk_project";
    private const int CollaborateMask = 262167;
    private const string DeploymentTenant = "00000000-0000-0000-0000-0000000000dd";

    private static readonly Guid SecureProject = Guid.Parse("14314314-0000-4000-9000-0000000000a1");
    private static readonly Guid Walled = Guid.Parse("14314314-0000-4000-9000-0000000000b1");
    private static readonly Guid Colleague = Guid.Parse("14314314-0000-4000-9000-0000000000b2");
    private static readonly Guid Author = Guid.Parse("14314314-0000-4000-9000-0000000000b3");

    private readonly Harness _h = new();

    public NoAccessShareReconciliationJobTests()
    {
        _h.Participations.Flags[SecureProject] = new RootRecordFlags(IsSecure: true, IsRestricted: false);
        _h.Store.Person(Author);
        _h.Store.Person(Walled);
        _h.Store.Person(Colleague);
        _h.Store.Rights[(Author, SecureProject)] = AccessRights.Write;
        _h.Shares.Seed(Project, SecureProject, DataversePrincipalRef.User(Colleague), CollaborateMask);
    }

    private async Task<JobRunResult> RunAsync(string? tenant = DeploymentTenant)
    {
        var settings = new Dictionary<string, string?>();
        if (tenant is not null) settings["AzureAd:TenantId"] = tenant;
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();

        var services = new ServiceCollection();
        services.AddSingleton<NoAccessEnforcementStore>(_h.Store);
        services.AddScoped(_ => _h.Enforcer);
        using var provider = services.BuildServiceProvider();

        var job = new NoAccessShareReconciliationJob(
            provider.GetRequiredService<IServiceScopeFactory>(), new FakeTimeProvider(), configuration,
            NullLogger<NoAccessShareReconciliationJob>.Instance);
        return await job.ExecuteAsync(
            new JobRunContext(Guid.NewGuid(), "corr-143", JobRunTrigger.ManualAdmin, new Dictionary<string, object>()),
            CancellationToken.None);
    }

    private static JsonElement Result(JobRunResult result)
    {
        using var doc = JsonDocument.Parse(result.ResultJson!);
        return doc.RootElement.Clone();
    }

    [Fact]
    public async Task ARun_RemovesAShareMadeOutOfBandAfterTheEntry_AndClearsTheCacheUnderTheDeploymentTenant()
    {
        _h.Store.AddEntry(subjectUser: Walled, objectRecord: (Project, SecureProject), modifiedBy: Author);
        _h.Shares.Seed(Project, SecureProject, DataversePrincipalRef.User(Walled), CollaborateMask); // OOB MDA Share

        var result = await RunAsync();

        result.Success.Should().BeTrue(result.ErrorMessage);
        result.ProcessedItems.Should().Be(1);
        _h.Shares.MaskOf(Project, SecureProject, DataversePrincipalRef.User(Walled)).Should().BeNull();
        _h.Cache.Removed.Should().ContainSingle().Which.Should().Be(
            (DeploymentTenant, ImpersonatedRootSetSource.CacheResource, $"{Walled:D}:{Project}"));
        _h.Cache.Removed.Should().NotContain(r => r.Tenant == "anonymous", "no read ever writes under 'anonymous'");
    }

    [Fact]
    public async Task ARun_EnforcesOnARecordThatBecameSecureAfterTheEntry()
    {
        _h.Participations.Flags[SecureProject] = RootRecordFlags.None;
        _h.Store.AddEntry(subjectUser: Walled, objectRecord: (Project, SecureProject), modifiedBy: Author);
        _h.Shares.Seed(Project, SecureProject, DataversePrincipalRef.User(Walled), CollaborateMask);

        (await RunAsync()).ProcessedItems.Should().Be(0, "not secure yet: the internal wall does not apply (Q4)");

        _h.Participations.Flags[SecureProject] = new RootRecordFlags(IsSecure: true, IsRestricted: false);
        (await RunAsync()).ProcessedItems.Should().Be(1, "the next run enforces once the record is secure");
    }

    [Fact]
    public async Task ARun_EnforcesForAUserWhoseLinkAppearedAfterTheEntry()
    {
        var contact = Guid.NewGuid();
        _h.Identities.AddContact(contact);
        _h.Store.AddEntry(subjectContact: contact, objectRecord: (Project, SecureProject), modifiedBy: Author);
        _h.Shares.Seed(Project, SecureProject, DataversePrincipalRef.User(Walled), CollaborateMask);

        (await RunAsync()).ProcessedItems.Should().Be(0, "no user represents the contact yet");

        _h.Identities.AddSystemUser(Walled, oid: null, email: null, primaryContactId: contact); // the 141 link lands
        (await RunAsync()).ProcessedItems.Should().Be(1);
    }

    [Fact]
    public async Task ARun_PastItsCeiling_ReportsTruncated_AndIsNotASuccess()
    {
        _h.Store.AddEntry(subjectUser: Walled, objectRecord: (Project, SecureProject), modifiedBy: Author);
        _h.Store.AddEntry(subjectUser: Colleague, objectRecord: (Project, SecureProject), modifiedBy: Author);
        _h.Store.ScanCeilingOverride = 1;

        var result = await RunAsync();

        result.Success.Should().BeFalse("a truncated run is not clean");
        Result(result).GetProperty("truncated").GetBoolean().Should().BeTrue();
        result.ErrorMessage.Should().Contain("TRUNCATED");
    }

    [Fact]
    public async Task ARun_WhoseScanFaults_RecordsSuccessFalse_NeverNothingToReconcile()
    {
        _h.Store.FailEntryScan = true;

        var result = await RunAsync();

        result.Success.Should().BeFalse();
        Result(result).GetProperty("status").GetString().Should().Be(NoAccessShareReconciliationJob.StatusError);
    }

    [Fact]
    public async Task ARun_WithAnEntryItCouldNotFullyEnforce_RecordsSuccessFalse()
    {
        _h.Store.AddEntry(subjectUser: Walled, objectRecord: (Project, SecureProject), modifiedBy: Author);
        _h.Shares.Seed(Project, SecureProject, DataversePrincipalRef.User(Walled), CollaborateMask);
        _h.Shares.IgnoreWrites = true; // the revoke is not confirmed

        var result = await RunAsync();

        result.Success.Should().BeFalse();
        Result(result).GetProperty("incompleteTotal").GetInt32().Should().Be(1);
    }

    [Fact]
    public async Task ARun_WithNoDeploymentTenant_ClearsNoCacheKey_RatherThanTheAnonymousOne()
    {
        _h.Store.AddEntry(subjectUser: Walled, objectRecord: (Project, SecureProject), modifiedBy: Author);
        _h.Shares.Seed(Project, SecureProject, DataversePrincipalRef.User(Walled), CollaborateMask);

        var result = await RunAsync(tenant: null);

        _h.Cache.Removed.Should().BeEmpty("'anonymous' is a key no read uses; nothing is cleared rather than the wrong key");
        Result(result).GetProperty("cacheTenantConfigured").GetBoolean().Should().BeFalse();
        _h.Shares.MaskOf(Project, SecureProject, DataversePrincipalRef.User(Walled)).Should().BeNull("the share is still removed");
    }

    /// <summary>
    /// The SHIPPING STATE (owner R4 as answered in round 3: enabled, writes on, at 5 minutes or less) — not an ADR-038
    /// B3 wiring test: perturb the cron or the enabled flag and it reddens (the IdentityLinkReconciliation precedent).
    /// </summary>
    [Fact]
    public void TheJobShipsEnabled_EveryFiveMinutes()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddExternalAccess();

        using var provider = services.BuildServiceProvider();
        var registration = provider.GetServices<ScheduledJobRegistration>()
            .Single(r => r.Job.JobId == NoAccessShareReconciliationJob.JobIdConstant);

        registration.CronSchedule.Should().Be("*/5 * * * *");
        registration.Enabled.Should().BeTrue();
    }

    [Fact]
    public async Task MoreActiveEntriesThanOneRunEnforces_RotateFromRunToRun_SoEveryEntryIsEnforced()
    {
        // Verifier finding 7: one run enforces at most MaxEntriesPerRun. Before r1 that was the newest 500 every run, so
        // an entry past them was never enforced by the job. Now each run continues after the last entry the previous run
        // reached: two runs of the SAME job cover every one of 501 entries.
        var total = NoAccessShareReconciliationJob.MaxEntriesPerRun + 1;
        var ids = Enumerable.Range(0, total)
            .Select(_ => _h.Store.AddEntry(subjectUser: Walled, objectRecord: (Project, SecureProject), modifiedBy: Author))
            .ToList();
        var job = NewJob();

        var first = await RunAsync(job);
        var afterFirst = _h.Store.EntryReads.ToList();
        var second = await RunAsync(job);
        var all = _h.Store.EntryReads.ToList();

        afterFirst.Should().HaveCount(NoAccessShareReconciliationJob.MaxEntriesPerRun);
        all.Distinct().Should().BeEquivalentTo(ids, "two runs reach every active entry");
        first.Success.Should().BeFalse("a run that could not enforce every entry is not clean");
        Result(first).GetProperty("rotating").GetBoolean().Should().BeTrue();
        Result(first).GetProperty("activeEntries").GetInt32().Should().Be(total);
        first.ErrorMessage.Should().Contain("ROTATING");
        Result(second).GetProperty("rotating").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task WhenEveryActiveEntryFitsInOneRun_ARunEnforcesThemAll_AndIsNotRotating()
    {
        var ids = Enumerable.Range(0, 3)
            .Select(_ => _h.Store.AddEntry(subjectUser: Walled, objectRecord: (Project, SecureProject), modifiedBy: Author))
            .ToList();

        var result = await RunAsync();

        _h.Store.EntryReads.Should().BeEquivalentTo(ids);
        Result(result).GetProperty("rotating").GetBoolean().Should().BeFalse();
    }

    private NoAccessShareReconciliationJob NewJob(string? tenant = DeploymentTenant)
    {
        var settings = new Dictionary<string, string?>();
        if (tenant is not null) settings["AzureAd:TenantId"] = tenant;
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();

        var services = new ServiceCollection();
        services.AddSingleton<NoAccessEnforcementStore>(_h.Store);
        services.AddScoped(_ => _h.Enforcer);
        var provider = services.BuildServiceProvider();

        return new NoAccessShareReconciliationJob(
            provider.GetRequiredService<IServiceScopeFactory>(), new FakeTimeProvider(), configuration,
            NullLogger<NoAccessShareReconciliationJob>.Instance);
    }

    private static Task<JobRunResult> RunAsync(NoAccessShareReconciliationJob job) => job.ExecuteAsync(
        new JobRunContext(Guid.NewGuid(), "corr-143-r1", JobRunTrigger.ManualAdmin, new Dictionary<string, object>()),
        CancellationToken.None);
}
