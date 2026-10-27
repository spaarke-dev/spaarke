using System.Text.Json;
using System.Text.RegularExpressions;
using Azure.Core;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.Filters;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Xunit;

namespace Sprk.Bff.Api.Tests.AccessControl;

/// <summary>
/// Slice C3: the external-access administrator authority (<see cref="ExternalAccessAdministratorAuthority"/>) and the
/// filter that gates <c>/remove-person</c> and <c>/reinstate-person</c> on it. The caller is named only by WhoAmI; the
/// profile is resolved by its pinned name to exactly one id; ONE membership query decides direct or team membership; every
/// fault, timeout, empty WhoAmI, missing or ambiguous profile refuses; nothing is cached.
/// </summary>
/// <remarks>The Dataverse double INTERPRETS the production filters (the profile name, the profile id, each membership leg),
/// so a filter that drops the team leg or names another profile changes the answer. KEEP path: <c>tests/integration/auth/**</c>.</remarks>
public class ExternalAccessAdministratorAuthorityTests
{
    private static readonly Guid Profile = Guid.Parse("f5f5f5f5-0000-4000-8000-0000000000c3");
    private static readonly Guid Admin = Guid.Parse("a0a0a0a0-0000-4000-8000-0000000000c3");
    private static readonly Guid TeamAdmin = Guid.Parse("a1a1a1a1-0000-4000-8000-0000000000c3");
    private static readonly Guid Staff = Guid.Parse("a2a2a2a2-0000-4000-8000-0000000000c3");

    private readonly ProfileDirectory _dataverse = new();
    private readonly WhoAmIProbe _probe = new();

    public ExternalAccessAdministratorAuthorityTests()
    {
        _dataverse.Profiles.Add((Profile, ExternalAccessAdministratorAuthority.StandingGrantProfileName));
        _dataverse.DirectMembers.Add(Admin);
        _dataverse.TeamMembers.Add(TeamAdmin);
    }

    private ExternalAccessAdministratorAuthority Authority()
        => new(_probe, _dataverse, NullLogger.Instance);

    private Task<AdministratorAuthorityDecision> Decide(Guid? whoAmI)
    {
        _probe.Answer = whoAmI;
        return Authority().DecideAsync("staff-token", CancellationToken.None);
    }

    // ── Must not fire ─────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ADirectMember_IsAnAdministrator_Direct()
    {
        var decision = await Decide(Admin);

        decision.IsAdministrator.Should().BeTrue();
        decision.Leg.Should().Be(AdministratorAuthorityLeg.Direct);
        decision.SystemUserId.Should().Be(Admin, "the audit names the caller WhoAmI answered");
    }

    [Fact]
    public async Task AMemberOfATeamThatHoldsTheProfile_IsAnAdministrator_Team()
    {
        var decision = await Decide(TeamAdmin);

        decision.IsAdministrator.Should().BeTrue();
        decision.Leg.Should().Be(AdministratorAuthorityLeg.Team);
    }

    // ── Must fire ─────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ANonMember_IsRefused()
    {
        var decision = await Decide(Staff);

        decision.IsAdministrator.Should().BeFalse();
        decision.Reason.Should().Be(AdministratorAuthorityReason.NotAMember);
    }

    [Fact]
    public async Task AnEmptyWhoAmI_IsRefused_AndNothingIsReadForIt()
    {
        (await Decide(null)).Reason.Should().Be(AdministratorAuthorityReason.CallerUnresolved);
        (await Decide(Guid.Empty)).Reason.Should().Be(AdministratorAuthorityReason.CallerUnresolved);
        _dataverse.Queries.Should().BeEmpty("no profile is read for a caller WhoAmI did not name");
    }

    [Fact]
    public async Task AWhoAmIFault_IsRefused()
    {
        _probe.Failure = new HttpRequestException("WhoAmI down");

        var decision = await Authority().DecideAsync("staff-token", CancellationToken.None);

        decision.IsAdministrator.Should().BeFalse();
        decision.Reason.Should().Be(AdministratorAuthorityReason.ReadFault);
    }

    [Theory]
    [InlineData(0, AdministratorAuthorityReason.ProfileMissing)]
    [InlineData(2, AdministratorAuthorityReason.ProfileAmbiguous)]
    public async Task AMissingOrAmbiguousProfile_IsRefused_EvenForADirectMember(int profiles, AdministratorAuthorityReason expected)
    {
        _dataverse.Profiles.Clear();
        for (var i = 0; i < profiles; i++)
            _dataverse.Profiles.Add((i == 0 ? Profile : Guid.NewGuid(), ExternalAccessAdministratorAuthority.StandingGrantProfileName));

        var decision = await Decide(Admin);

        decision.IsAdministrator.Should().BeFalse();
        decision.Reason.Should().Be(expected);
    }

    [Fact]
    public async Task AProfileWithAnotherName_IsNotTheAuthority()
    {
        _dataverse.Profiles.Clear();
        _dataverse.Profiles.Add((Profile, "Standing Grant Admins"));

        (await Decide(Admin)).Reason.Should().Be(AdministratorAuthorityReason.ProfileMissing);
    }

    [Fact]
    public async Task AProfileReadFault_IsRefused()
    {
        _dataverse.Failure = new HttpRequestException("Dataverse down");

        var decision = await Decide(Admin);

        decision.IsAdministrator.Should().BeFalse();
        decision.Reason.Should().Be(AdministratorAuthorityReason.ReadFault);
    }

    [Fact]
    public async Task ACheckThatTimesOut_IsRefused_TheCallersOwnCancellationPropagates()
    {
        _dataverse.Failure = new TaskCanceledException("the check's own deadline");
        (await Decide(Admin)).Reason.Should().Be(AdministratorAuthorityReason.Timeout);

        _dataverse.Failure = null;
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        _probe.Answer = Admin;
        _probe.HonourCancellation = true;
        await FluentActions.Awaiting(() => Authority().DecideAsync("staff-token", cancelled.Token))
            .Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task NothingIsCached_AMembershipRemovedInDataverse_RefusesTheNextCheck()
    {
        (await Decide(Admin)).IsAdministrator.Should().BeTrue();

        _dataverse.DirectMembers.Remove(Admin);

        (await Decide(Admin)).Reason.Should().Be(AdministratorAuthorityReason.NotAMember);
    }

    [Fact]
    public void TheMembershipQuery_AsksBothLegsServerSide_ForThatProfileAndCaller()
    {
        var filter = ExternalAccessAdministratorAuthority.MembershipFilter(Profile, Admin);

        filter.Should().StartWith($"fieldsecurityprofileid eq {Profile:D} and (");
        filter.Should().Contain($"systemuserprofiles_association/any(u:u/systemuserid eq {Admin:D})");
        filter.Should().Contain($"teamprofiles_association/any(t:t/teammembership_association/any(u:u/systemuserid eq {Admin:D}))");
        ExternalAccessAdministratorAuthority.ProfileByNameFilter().Should().Be("name eq 'Standing Grant Administrators'");
    }

    // ── The filter: one uniform 403, the handler is never reached ────────────────────────────────────────────

    [Theory]
    [InlineData("not-a-member")]
    [InlineData("whoami-empty")]
    [InlineData("profile-fault")]
    public async Task TheFilter_RefusesEveryNonAdministrator_WithTheSame403_AndDoesNotRunTheHandler(string why)
    {
        switch (why)
        {
            case "not-a-member": _probe.Answer = Staff; break;
            case "whoami-empty": _probe.Answer = null; break;
            default: _probe.Answer = Admin; _dataverse.Failure = new HttpRequestException("down"); break;
        }

        var http = new DefaultHttpContext { TraceIdentifier = "trace-c3" };
        var ran = false;
        var result = await new ExternalAccessAdministratorAuthorizationFilter(Authority()).InvokeAsync(
            new DefaultEndpointFilterInvocationContext(http),
            _ => { ran = true; return ValueTask.FromResult<object?>(Results.Ok()); });

        ran.Should().BeFalse("a refused caller never reaches /remove-person or /reinstate-person");
        var problem = result.Should().BeOfType<ProblemHttpResult>().Subject;
        problem.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
        problem.ProblemDetails.Extensions["reasonCode"].Should().Be(
            ExternalAccessAdministratorAuthorizationFilter.AdministratorRequiredReasonCode);
        problem.ProblemDetails.Detail.Should().NotContain(why, "the operator-only reason is never sent to the caller");
        ExternalAccessAdministratorAuthorizationFilter.DecisionOf(http).Should().BeNull();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task TheFilter_AdmitsAnAdministrator_AndLeavesTheDecisionForTheAudit(bool direct)
    {
        _probe.Answer = direct ? Admin : TeamAdmin;
        var http = new DefaultHttpContext();
        var ran = false;

        await new ExternalAccessAdministratorAuthorizationFilter(Authority()).InvokeAsync(
            new DefaultEndpointFilterInvocationContext(http),
            _ => { ran = true; return ValueTask.FromResult<object?>(Results.Ok()); });

        ran.Should().BeTrue();
        var decision = ExternalAccessAdministratorAuthorizationFilter.DecisionOf(http)!;
        decision.SystemUserId.Should().Be(direct ? Admin : TeamAdmin);
        decision.Leg.Should().Be(direct ? AdministratorAuthorityLeg.Direct : AdministratorAuthorityLeg.Team);
    }

    // ── Doubles ─────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>WhoAmI over the caller's token: answers <see cref="Answer"/>, or throws <see cref="Failure"/>.</summary>
    private sealed class WhoAmIProbe : CallerRecordAccessProbe
    {
        public WhoAmIProbe()
            : base(new HttpClient(), new ConfigurationBuilder().Build(), NullLogger<CallerRecordAccessProbe>.Instance)
        {
        }

        public Guid? Answer { get; set; }
        public Exception? Failure { get; set; }
        public bool HonourCancellation { get; set; }

        public override Task<Guid?> GetCallerSystemUserIdAsync(string? callerBearerToken, CancellationToken ct = default)
        {
            if (HonourCancellation)
                ct.ThrowIfCancellationRequested();
            if (Failure is { } failure)
                throw failure;
            return Task.FromResult(Answer);
        }
    }

    /// <summary>
    /// <c>fieldsecurityprofiles</c>, interpreting the production filters: by name; by id AND each membership leg the filter
    /// names (the direct leg against <see cref="DirectMembers"/>, the nested team leg against <see cref="TeamMembers"/>).
    /// </summary>
    private sealed class ProfileDirectory : DataverseWebApiClient
    {
        public ProfileDirectory()
            : base(new ConfigurationBuilder()
                    .AddInMemoryCollection(new Dictionary<string, string?> { ["Dataverse:ServiceUrl"] = "https://test.crm.dynamics.com" })
                    .Build(),
                NullLogger<DataverseWebApiClient>.Instance,
                new NoCredential())
        {
        }

        public List<(Guid Id, string Name)> Profiles { get; } = new();
        public HashSet<Guid> DirectMembers { get; } = new();
        public HashSet<Guid> TeamMembers { get; } = new();
        public Exception? Failure { get; set; }
        public List<string> Queries { get; } = new();

        public override Task<List<T>> QueryAsync<T>(string entitySetName, string? filter = null, string? select = null,
            int? top = null, int? skip = null, CancellationToken cancellationToken = default)
        {
            entitySetName.Should().Be("fieldsecurityprofiles");
            Queries.Add(filter!);
            if (Failure is { } failure)
                throw failure;

            IEnumerable<Guid> ids;
            var byName = Regex.Match(filter!, @"^name eq '(?<name>(?:[^']|'')*)'$");
            if (byName.Success)
            {
                var name = byName.Groups["name"].Value.Replace("''", "'");
                ids = Profiles.Where(p => p.Name == name).Select(p => p.Id);
            }
            else
            {
                var profileId = Guid.Parse(Regex.Match(filter!, @"^fieldsecurityprofileid eq ([0-9a-f-]{36}) and ").Groups[1].Value);
                var directLeg = Regex.Match(filter!, @"systemuserprofiles_association/any\(u:u/systemuserid eq ([0-9a-f-]{36})\)");
                var teamLeg = Regex.Match(filter!,
                    @"teamprofiles_association/any\(t:t/teammembership_association/any\(u:u/systemuserid eq ([0-9a-f-]{36})\)\)");
                var member = (directLeg.Success && DirectMembers.Contains(Guid.Parse(directLeg.Groups[1].Value)))
                             || (teamLeg.Success && TeamMembers.Contains(Guid.Parse(teamLeg.Groups[1].Value)));
                ids = member ? Profiles.Where(p => p.Id == profileId).Select(p => p.Id) : Enumerable.Empty<Guid>();
            }

            var rows = ids.Take(top ?? int.MaxValue)
                .Select(id => new Dictionary<string, object?> { ["fieldsecurityprofileid"] = id })
                .ToList();
            return Task.FromResult(JsonSerializer.Deserialize<List<T>>(JsonSerializer.Serialize(rows))!);
        }
    }

    private sealed class NoCredential : TokenCredential
    {
        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken)
            => throw new NotSupportedException("This double issues no HTTP.");

        public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken)
            => throw new NotSupportedException("This double issues no HTTP.");
    }
}
