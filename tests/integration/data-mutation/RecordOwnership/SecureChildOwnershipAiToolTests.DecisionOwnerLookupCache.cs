using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Sprk.Bff.Api.Api;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.RecordCreation;
using Sprk.Bff.Api.Services.Signals;
using Sprk.Bff.Api.Tests.Integration.DataMutation.Signals;
using Sprk.Bff.Api.Tests.TestInfrastructure;
using Xunit;
using Directory = Sprk.Bff.Api.Tests.TestInfrastructure.OwnershipDirectory;

namespace Sprk.Bff.Api.Tests.Integration.DataMutation.RecordOwnership;

/// <summary>
/// ISS-010 (#3184) part D1: the child-records route's per-request <see cref="RecordOwnerLookupCache"/> on the four-action decision
/// (<see cref="BuildFourActionWorld"/>: the real executors, the real route core, the real <c>RecordCreationService</c>, owner
/// resolver and share synchronizer), and the same answers with and without it.
/// </summary>
/// <remarks>
/// <para><b>Must fire.</b> One commit reads the Secure Record business unit (R1), the Secure Record owner team (R2) and the
/// matter's business unit's default owner team (R4) once each, across its four creates and their share syncs; R3 and R7 at most
/// once. A cache per create, or none, reads R1 once per create and per share sync.</para>
/// <para><b>Same answers.</b> The same writes in the same order and the same response with and without the cache (the request
/// refuses it), and, through the service, the same owner and the same refusal for a team-owned root, a secure child and the
/// <see cref="RecordOwnerRefusal.NoOwnerSource"/> refusal.</para>
/// <para><b>Must not fire.</b> Two requests share nothing, and a create that names no cache reads as before
/// (<c>RecordOwnerLookupCacheTests</c> pins faults, concurrency and the keep rules).</para>
/// </remarks>
public sealed partial class SecureChildOwnershipAiToolTests
{
    /// <summary>The business-unit and team configuration reads of one commit (R1, R2, R3, R4, R7), counted at the wire.</summary>
    private sealed record ConfigurationReads(int SecureUnit, int SecureTeam, int CanBeSecureTeam, int DefaultTeam, int ActingUser, int Total)
    {
        public static ConfigurationReads Of(DecisionCommitCallProfile profile)
        {
            var calls = profile.Calls;
            int At(string table, string site) => calls.Count(c => c.Target == $"{table} (query)" && c.Site.EndsWith(site, StringComparison.Ordinal));
            return new ConfigurationReads(
                At("businessunit", "SecureRecordOwnerTeam.ReadSecureBusinessUnitAsync"),
                At("team", "SecureChildShareSynchronizer.ReadSecureOwnerTeamAsync"),
                At("team", "SecureChildShareSynchronizer.ReadCannotBeSecureOwnerTeamAsync"),
                At("team", "RecordOwnershipResolver.ReadDefaultOwnerTeamAsync"),
                At("systemuser", "RecordOwnershipResolver.ReadActingUserAsync"),
                calls.Count);
        }
    }

    [Fact]
    public async Task Iss010D1_MustFire_OneCommit_ReadsEachBusinessUnitAndTeamOnce()
    {
        var w = BuildFourActionWorld();

        var result = await w.CommitAsync();

        result.Status.Should().Be(DecisionCommitStatus.Recorded, result.Detail);
        var reads = ConfigurationReads.Of(w.Profile);
        WriteProfile("four-action commit WITH the request's owner lookups (D1)", w.Profile);
        (reads.SecureUnit, reads.SecureTeam, reads.DefaultTeam).Should().Be((1, 1, 1),
            $"R1, R2 and R4 once per request across four creates and their share syncs: {reads}");
        reads.CanBeSecureTeam.Should().BeLessThanOrEqualTo(1, $"R3 at most once per team: {reads}");
        reads.ActingUser.Should().BeLessThanOrEqualTo(1, $"R7 at most once: {reads}");
        reads.Total.Should().Be(103, "part C's 114 less the 11 repeated reads");
    }

    [Fact]
    public async Task Iss010D1_MustFire_WithoutTheRequestsCache_EachCreateReadsThemAgain()
    {
        var w = BuildFourActionWorld(withoutOwnerLookups: true);

        var result = await w.CommitAsync();

        result.Status.Should().Be(DecisionCommitStatus.Recorded, result.Detail);
        var reads = ConfigurationReads.Of(w.Profile);
        (reads.SecureUnit, reads.SecureTeam, reads.DefaultTeam, reads.Total).Should().Be((7, 3, 4, 114),
            $"as part C measured: R1 per create and per share sync, R2 per share sync, R4 per create: {reads}");
    }

    [Fact]
    public async Task Iss010D1_TheCommit_WritesAndAnswersTheSame_WithAndWithoutTheRequestsCache()
    {
        var with = BuildFourActionWorld();
        var withResult = await with.CommitAsync();
        var withWrites = with.Profile.WriteTranscript();
        _appCreates.Clear();

        var without = BuildFourActionWorld(withoutOwnerLookups: true);
        var withoutResult = await without.CommitAsync();

        withResult.Status.Should().Be(DecisionCommitStatus.Recorded, withResult.Detail);
        without.Profile.WriteTranscript().Should().Be(withWrites, "the cache changes how often a row is read, never what is written");
        Normalized(withoutResult).Should().Be(Normalized(withResult));
    }

    // ── The same answers through the service: a team-owned root, a secure child, the NoOwnerSource refusal ─────────────

    public static TheoryData<string> OwnerScenarios => new() { "team-owned root", "secure child", "no owner source" };

    /// <summary>
    /// field-mapping-r1 condition 2: each scenario created twice in ONE request (the second create answered from the request's
    /// cache) gives exactly the owner, the refusal and the row the same create gives with no cache.
    /// </summary>
    [Theory]
    [MemberData(nameof(OwnerScenarios))]
    public async Task Iss010D1_TheSameOwnerAndTheSameRefusal_WithAndWithoutTheRequestsCache(string scenario)
    {
        ChatPlatformCallerMayCreate();
        var request = RecordOwnerLookupCache.ForRequest(new DefaultHttpContext());

        var first = await CreateFor(scenario, request);
        var second = await CreateFor(scenario, request);
        var uncached = await CreateFor(scenario, cache: null);

        second.Should().Be(first, "the second create of the request is answered from its cache");
        uncached.Should().Be(first, "no cache: the same owner, the same refusal, the same row");
        switch (scenario)
        {
            case "team-owned root":
                first.Owner.Should().Be(Directory.ChildTeam.ToString("D"), "an unfiled account: the acting user's unit's team (R1, R4)");
                break;
            case "secure child":
                first.Owner.Should().Be(Directory.SecureNamedTeam.ToString("D"), "a to-do under a secure matter: the named Secure team");
                break;
            default:
                first.Refusal.Should().StartWith(RecordOwnerRefusal.NoOwnerSource);
                first.Owner.Should().BeNull();
                break;
        }
    }

    private async Task<(string? Owner, string? Refusal, string? Row)> CreateFor(string scenario, RecordOwnerLookupCache? cache)
    {
        _appCreates.Clear();
        var (table, payload) = scenario switch
        {
            "team-owned root" => ("account", (RecordCreationPayload)new RecordCreationPayload.Mapped(ChatAccount())),
            "secure child" => ("sprk_todo", new RecordCreationPayload.WebApi(JsonSerializer.SerializeToElement(new Dictionary<string, object?>
            {
                ["sprk_name"] = "Call back",
                ["sprk_RegardingMatter@odata.bind"] = $"/sprk_matters({SecureMatter:D})",
            }))),
            _ => ("task", new RecordCreationPayload.Mapped(ChatTask(SecureMatter))),
        };

        var outcome = await RouteRecordCreation
            .Over(_world.Resolver(), _appOnly.Object, Restamper(), Shares(), SecureRootFilingGateFixtures.NothingSecure())
            .CreateAsync(
                new RecordCreationRequest(table, payload) { OwnerConfigurationReads = cache },
                RecordCreationPrincipal.ForCaller(_user, CallerOid), CancellationToken.None);

        if (outcome.Refusal is { } refusal)
            return (null, $"{refusal.Code} {refusal.Kind} {refusal.Detail} secure={refusal.OwnerDecision?.IsSecureOwner}", null);

        var (_, _, fields) = _appCreates.Should().ContainSingle().Subject;
        return (Owner(fields).ToString("D"), null, DecisionCommitCallProfile.Tokenize(Written(fields)));
    }

    /// <summary>A request's <c>Items</c> that refuse the business-unit and team configuration cache (as an ended request does), so a
    /// test can run the commit exactly as before ISS-010 D1 with no switch in production code.</summary>
    private sealed class NoOwnerLookupsItems : Dictionary<object, object?>, IDictionary<object, object?>
    {
        object? IDictionary<object, object?>.this[object key]
        {
            get => this[key];
            set
            {
                if (value is RecordOwnerLookupCache)
                    throw new InvalidOperationException("The request refuses the owner lookup cache (test).");
                this[key] = value;
            }
        }
    }
}
