using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Infrastructure.Auth;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Xunit;

namespace Sprk.Bff.Api.Tests.Auth.Office;

/// <summary>
/// <see cref="CallerRecordAccessProbe.GetCallerRightsForRecordsAsync"/> (spaarkeai-word-add-in-r1 task 084): the
/// caller's rights on SEVERAL records, used by the Office picker to mark each row with whether the save would
/// accept it (#1037).
/// </summary>
/// <remarks>
/// <para>The shipped type is exercised with only its three Dataverse-facing steps substituted (the OBO
/// exchange, <c>WhoAmI</c>, <c>RetrievePrincipalAccess</c>, all <c>protected virtual</c>), so the orchestration
/// under test is the production code: it asks for the caller's identity ONCE, looks each record up once, never
/// runs more than <see cref="CallerRecordAccessProbe.MaxConcurrentRecordLookups"/> lookups at once, and fails
/// closed per record and for the whole set. No transport mock (ADR-038 B1).</para>
///
/// <para>Concurrency is observed with a barrier, not with timing: the lookups block until the test releases
/// them, so an unbounded fan-out would put every lookup in flight before the release and the maximum would
/// exceed the bound deterministically.</para>
/// </remarks>
[Trait("status", "new")]
public class CallerRecordAccessProbeRecordsTests
{
    private static readonly Guid Caller = new("cccccccc-0000-0000-0000-000000000084");

    [Fact]
    public async Task GetCallerRightsForRecords_AsksForTheCallersIdentityOnce_AndAnswersEachRecordInOrder()
    {
        var targets = Enumerable.Range(0, 6).Select(_ => ("sprk_matters", Guid.NewGuid())).ToList();
        var probe = new CountingProbe();
        // A different answer per record, so a misaligned result list cannot pass.
        for (var i = 0; i < targets.Count; i++)
            probe.RightsByRecord[targets[i].Item2] = i % 2 == 0 ? AccessRights.Read | AccessRights.AppendTo : AccessRights.Read;

        var rights = await probe.GetCallerRightsForRecordsAsync("caller-token", targets);

        probe.Exchanges.Should().Be(1, "the OBO exchange depends only on the caller, so it happens once per set");
        probe.WhoAmIs.Should().Be(1, "so does WhoAmI");
        probe.Lookups.Should().Be(targets.Count, "each record is asked about exactly once");
        probe.PrincipalsAskedAbout.Should().OnlyContain(p => p == Caller,
            "every lookup is about the caller WhoAmI named, never anyone else");
        rights.Should().Equal(targets.Select(t => probe.RightsByRecord[t.Item2]),
            "results come back in target order");
    }

    [Fact]
    public async Task GetCallerRightsForRecords_RunsAtMostTheBoundedNumberOfLookupsAtOnce()
    {
        var targets = Enumerable.Range(0, 10).Select(_ => ("sprk_projects", Guid.NewGuid())).ToList();
        var probe = new CountingProbe { HoldLookups = true };

        var call = probe.GetCallerRightsForRecordsAsync("caller-token", targets);
        await probe.Saturated.Task.WaitAsync(TimeSpan.FromSeconds(10));
        probe.ReleaseLookups();
        await call;

        probe.MaxInFlight.Should().Be(CallerRecordAccessProbe.MaxConcurrentRecordLookups,
            "a page of results must not fan out unboundedly against Dataverse");
        probe.Lookups.Should().Be(targets.Count);
    }

    [Fact]
    public async Task GetCallerRightsForRecords_WhenOneLookupFails_DeniesOnlyThatRecord()
    {
        var failing = Guid.NewGuid();
        var fine = Guid.NewGuid();
        var probe = new CountingProbe();
        probe.RightsByRecord[fine] = AccessRights.Read | AccessRights.AppendTo;
        probe.RightsByRecord[failing] = AccessRights.Read | AccessRights.AppendTo;
        probe.FailingRecords.Add(failing);

        var rights = await probe.GetCallerRightsForRecordsAsync(
            "caller-token", new[] { ("sprk_matters", failing), ("sprk_matters", fine) });

        rights[0].Should().Be(AccessRights.None, "a record whose rights could not be read is not authorized");
        rights[1].Should().Be(AccessRights.Read | AccessRights.AppendTo, "one failure does not take the others with it");
    }

    [Fact]
    public async Task GetCallerRightsForRecords_WhenWhoAmIFails_DeniesEveryRecordWithoutAskingAboutAny()
    {
        var probe = new CountingProbe { FailWhoAmI = true };

        var rights = await probe.GetCallerRightsForRecordsAsync(
            "caller-token", new[] { ("sprk_matters", Guid.NewGuid()), ("contacts", Guid.NewGuid()) });

        rights.Should().OnlyContain(r => r == AccessRights.None);
        probe.Lookups.Should().Be(0, "with no proven caller there is nobody to ask about");
    }

    [Fact]
    public async Task GetCallerRightsForRecords_WhenTheExchangeFails_DeniesEveryRecordWithoutWhoAmI()
    {
        var probe = new CountingProbe { FailExchange = true };

        var rights = await probe.GetCallerRightsForRecordsAsync(
            "caller-token", new[] { ("sprk_matters", Guid.NewGuid()) });

        rights.Should().OnlyContain(r => r == AccessRights.None);
        probe.WhoAmIs.Should().Be(0);
        probe.Lookups.Should().Be(0);
    }

    [Fact]
    public async Task GetCallerRightsForRecords_WithNoCallerToken_DeniesEveryRecordWithoutExchanging()
    {
        var probe = new CountingProbe();

        var rights = await probe.GetCallerRightsForRecordsAsync(
            null, new[] { ("sprk_matters", Guid.NewGuid()), ("sprk_matters", Guid.NewGuid()) });

        rights.Should().HaveCount(2).And.OnlyContain(r => r == AccessRights.None);
        probe.Exchanges.Should().Be(0);
    }

    [Fact]
    public async Task GetCallerRightsForRecords_WithNoTargets_AsksNothing()
    {
        var probe = new CountingProbe();

        var rights = await probe.GetCallerRightsForRecordsAsync(
            "caller-token", Array.Empty<(string, Guid)>());

        rights.Should().BeEmpty();
        probe.Exchanges.Should().Be(0);
    }

    /// <summary>The shipped probe, with its three Dataverse-facing steps counted and answered in memory.</summary>
    private sealed class CountingProbe : CallerRecordAccessProbe
    {
        private int _exchanges;
        private int _whoAmIs;
        private int _lookups;
        private int _inFlight;
        private int _maxInFlight;
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public CountingProbe()
            : base(
                new HttpClient(),
                new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["AzureAd:TenantId"] = "00000000-0000-0000-0000-0000000000aa",
                    ["AzureAd:ClientId"] = "00000000-0000-0000-0000-0000000000bb",
                    ["Dataverse:ServiceUrl"] = "https://test.crm.dynamics.com"
                }).Build(),
                NullLogger<CallerRecordAccessProbe>.Instance,
                // Never asked for a client: the exchange below is substituted. It only has to exist, because the
                // probe refuses to try OBO at all without a credential provider (fail closed).
                new OrderedCredentialClientProvider(
                    Options.Create(new CredentialSelectionOptions { Order = new List<string> { "ManagedIdentityFederated" } }),
                    new ConfigurationBuilder().Build(),
                    NullLogger<OrderedCredentialClientProvider>.Instance))
        {
        }

        public Dictionary<Guid, AccessRights> RightsByRecord { get; } = new();
        public HashSet<Guid> FailingRecords { get; } = new();
        public System.Collections.Concurrent.ConcurrentBag<Guid> PrincipalsAskedAbout { get; } = new();
        public bool FailExchange { get; init; }
        public bool FailWhoAmI { get; init; }
        public bool HoldLookups { get; init; }

        /// <summary>Completes once <see cref="MaxConcurrentRecordLookups"/> lookups are in flight together.</summary>
        public TaskCompletionSource Saturated { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int Exchanges => Volatile.Read(ref _exchanges);
        public int WhoAmIs => Volatile.Read(ref _whoAmIs);
        public int Lookups => Volatile.Read(ref _lookups);
        public int MaxInFlight => Volatile.Read(ref _maxInFlight);

        public void ReleaseLookups() => _release.TrySetResult();

        protected override Task<string?> ExchangeForDataverseTokenAsync(
            string callerBearerToken, string context, CancellationToken ct)
        {
            Interlocked.Increment(ref _exchanges);
            return Task.FromResult<string?>(FailExchange ? null : "dataverse-token");
        }

        protected override Task<Guid?> ResolveCallerSystemUserIdAsync(string dataverseToken, CancellationToken ct)
        {
            Interlocked.Increment(ref _whoAmIs);
            return Task.FromResult<Guid?>(FailWhoAmI ? null : Caller);
        }

        protected override async Task<AccessRights> RetrievePrincipalAccessAsync(
            string dataverseToken, Guid principalSystemUserId, string entitySet, Guid recordId, CancellationToken ct)
        {
            PrincipalsAskedAbout.Add(principalSystemUserId);
            var inFlight = Interlocked.Increment(ref _inFlight);
            int seen;
            while (inFlight > (seen = Volatile.Read(ref _maxInFlight))
                   && Interlocked.CompareExchange(ref _maxInFlight, inFlight, seen) != seen)
            {
            }

            if (HoldLookups)
            {
                if (inFlight >= MaxConcurrentRecordLookups)
                    Saturated.TrySetResult();
                await _release.Task;
            }

            Interlocked.Decrement(ref _inFlight);
            Interlocked.Increment(ref _lookups);

            return FailingRecords.Contains(recordId)
                ? AccessRights.None
                : RightsByRecord.GetValueOrDefault(recordId, AccessRights.Read);
        }
    }
}
