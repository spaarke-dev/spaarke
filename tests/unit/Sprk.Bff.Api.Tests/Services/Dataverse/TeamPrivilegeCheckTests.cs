using FluentAssertions;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.Cache;
using Sprk.Bff.Api.Services.Dataverse.Privileges;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Dataverse;

/// <summary>
/// unified-access-control-r2 task 099b review round 2: <see cref="UserPrivilegeChecker.TeamHasReadPrivilegeAsync"/> — whether
/// an OWNER TEAM's roles hold Read on a table (the grant routes ask before a grant row is created for, or assigned to, a
/// team). Only a YES is cached per team for a short time (review round 3, K2: an administrator's fix is seen on the next
/// question, and a fresh question bypasses the cache); a fault propagates and is NEVER cached, so "could not be read" (503) is
/// never turned into "lacks it" or remembered. The team fetch is the seam (a ServiceClient cannot be faked); the cache
/// is the production <see cref="TenantCache"/> over an in-memory distributed cache.
/// </summary>
public class TeamPrivilegeCheckTests
{
    private static readonly Guid SecureTeam = Guid.Parse("6eabc7f9-13be-f111-a05b-0022482913fc");
    private static readonly Guid DefaultTeam = Guid.Parse("cf15f587-baa0-f111-aaac-000d3a99d1d7");
    private const string GrantTable = "sprk_externalrecordaccess";

    private readonly List<Guid> _fetches = new();
    private Func<Guid, IReadOnlySet<string>> _answer = _ => new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    private UserPrivilegeChecker Checker() => new(
        new Mock<IDataverseService>().Object,
        new TenantCache(new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions())), NullLogger<TenantCache>.Instance),
        NullLogger<UserPrivilegeChecker>.Instance,
        (team, _) =>
        {
            _fetches.Add(team);
            return Task.FromResult(_answer(team));
        });

    private static IReadOnlySet<string> Readable(params string[] tables) =>
        new HashSet<string>(tables, StringComparer.OrdinalIgnoreCase);

    [Fact]
    public async Task ATeamWhoseRolesGrantRead_HoldsIt_AndOneWhoseRolesDoNot_DoesNot()
    {
        _answer = team => team == DefaultTeam ? Readable("sprk_ExternalRecordAccess", "sprk_matter") : Readable("sprk_matter");
        var checker = Checker();

        (await checker.TeamHasReadPrivilegeAsync(DefaultTeam, GrantTable, fresh: false, CancellationToken.None)).Should().BeTrue();
        (await checker.TeamHasReadPrivilegeAsync(SecureTeam, GrantTable, fresh: false, CancellationToken.None)).Should().BeFalse(
            "the Secure Record Owner role before it holds prvReadsprk_ExternalRecordAccess");
    }

    [Fact]
    public async Task AYes_IsCachedPerTeam_SoASecondQuestionReadsNothing()
    {
        _answer = _ => Readable(GrantTable);
        var checker = Checker();

        await checker.TeamHasReadPrivilegeAsync(SecureTeam, GrantTable, fresh: false, CancellationToken.None);
        (await checker.TeamHasReadPrivilegeAsync(SecureTeam, GrantTable, fresh: false, CancellationToken.None)).Should().BeTrue();
        await checker.TeamHasReadPrivilegeAsync(DefaultTeam, GrantTable, fresh: false, CancellationToken.None);

        _fetches.Should().Equal(new[] { SecureTeam, DefaultTeam }, "one read per team, then the cached YES answers");
        UserPrivilegeChecker.TeamPrivilegeTtl.Should().BeLessThanOrEqualTo(TimeSpan.FromMinutes(5),
            "short: a privilege an administrator removes stops being answered as held within minutes");
    }

    [Fact]
    public async Task ANo_IsNotCached_SoAnAdministratorsFixIsSeenOnTheNextQuestion()
    {
        // Review round 3 (K2): before, a NO was remembered for the TTL, so a role fixed by an administrator kept refusing.
        var holds = false;
        _answer = _ => holds ? Readable(GrantTable) : Readable("sprk_matter");
        var checker = Checker();

        (await checker.TeamHasReadPrivilegeAsync(SecureTeam, GrantTable, fresh: false, CancellationToken.None)).Should().BeFalse();
        holds = true; // the administrator adds the privilege
        (await checker.TeamHasReadPrivilegeAsync(SecureTeam, GrantTable, fresh: false, CancellationToken.None)).Should().BeTrue(
            "the NO was not remembered");
        _fetches.Should().HaveCount(2);
    }

    [Fact]
    public async Task ACachedSetThatLacksTheTable_IsNotANo_TheTeamIsReadAgain()
    {
        // A YES for one table cached the team's set; a table that set lacks is read again, never answered NO from it.
        var withMatter = false;
        _answer = _ => withMatter ? Readable(GrantTable, "sprk_matter") : Readable(GrantTable);
        var checker = Checker();

        await checker.TeamHasReadPrivilegeAsync(SecureTeam, GrantTable, fresh: false, CancellationToken.None);
        withMatter = true;
        (await checker.TeamHasReadPrivilegeAsync(SecureTeam, "sprk_matter", fresh: false, CancellationToken.None)).Should().BeTrue();
        _fetches.Should().HaveCount(2);
    }

    [Fact]
    public async Task AFreshQuestion_SeesAPrivilegeRemovedAfterAYesWasCached_AndDropsThatYes()
    {
        // The grant core's 403 classifier asks fresh: a remembered YES must not turn a real owner refusal into the generic 500.
        var holds = true;
        _answer = _ => holds ? Readable(GrantTable) : Readable("sprk_matter");
        var checker = Checker();

        (await checker.TeamHasReadPrivilegeAsync(SecureTeam, GrantTable, fresh: false, CancellationToken.None)).Should().BeTrue();
        holds = false; // the administrator removes the privilege

        (await checker.TeamHasReadPrivilegeAsync(SecureTeam, GrantTable, fresh: false, CancellationToken.None)).Should().BeTrue(
            "a cached YES may answer a question that is not fresh (within the TTL)");
        (await checker.TeamHasReadPrivilegeAsync(SecureTeam, GrantTable, fresh: true, CancellationToken.None)).Should().BeFalse(
            "a fresh question reads Dataverse, whatever the cache holds");
        (await checker.TeamHasReadPrivilegeAsync(SecureTeam, GrantTable, fresh: false, CancellationToken.None)).Should().BeFalse(
            "the NO the fresh read found dropped the cached YES, so no caller is answered from it again");
        _fetches.Should().HaveCount(3);
    }

    /// <summary>
    /// Review round 3 (K3): the privilege-name mapping both privilege reads use. The live read on spaarkedev1 (2026-10-17)
    /// returned <c>prvReadsprk_ExternalRecordAccess</c> for the default team of "Spaarke Business Unit 1", and none for
    /// Secure Record Owners; the name must map to the grant table the grant routes ask about.
    /// </summary>
    [Theory]
    [InlineData("prvReadsprk_ExternalRecordAccess", GrantTable)]
    [InlineData("prvReadAccount", "account")]
    [InlineData("prvReadsprk_analysis", "sprk_analysis")]
    [InlineData("prvWritesprk_ExternalRecordAccess", null)]
    [InlineData("prvreadsprk_ExternalRecordAccess", null)]
    [InlineData("prvRead", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void AReadPrivilegesName_MapsToItsTable_AndAnyOtherNameToNone(string? privilegeName, string? table) =>
        UserPrivilegeChecker.ReadPrivilegeEntity(privilegeName).Should().Be(table);

    [Fact]
    public async Task AFault_Propagates_AndIsNeverCached()
    {
        var calls = 0;
        _answer = _ => ++calls == 1 ? throw new HttpRequestException("Simulated Dataverse 503.") : Readable(GrantTable);
        var checker = Checker();

        var first = () => checker.TeamHasReadPrivilegeAsync(SecureTeam, GrantTable, fresh: false, CancellationToken.None);
        await first.Should().ThrowAsync<HttpRequestException>("a fault is not 'lacks it' — the caller refuses 503");

        (await checker.TeamHasReadPrivilegeAsync(SecureTeam, GrantTable, fresh: false, CancellationToken.None)).Should().BeTrue(
            "the fault was not cached: the next question reads Dataverse again");
        _fetches.Should().HaveCount(2);
    }
}
