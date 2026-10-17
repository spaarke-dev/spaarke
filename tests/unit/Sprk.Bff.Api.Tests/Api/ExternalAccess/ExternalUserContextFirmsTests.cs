using Azure.Core;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using Moq;
using Sprk.Bff.Api.Infrastructure.Cache;
using Sprk.Bff.Api.Api.ExternalAccess;
using Sprk.Bff.Api.Api.ExternalAccess.Dtos;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Services.Finance.Submission;
using Sprk.Bff.Api.Tests.Services.Finance.Submission;
using Xunit;

namespace Sprk.Bff.Api.Tests.Api.ExternalAccess;

/// <summary>
/// GET /api/v1/external/me, the additive <c>Firms</c> list (ebilling-r1 task 051, owner decision 2026-10-17: add a firm step). Drives the
/// real <see cref="ExternalUserContextEndpoint.HandleAsync"/> with the principal the group filter would have stored; the membership read
/// and the Dataverse organization read are substituted at their narrow seams.
/// </summary>
/// <remarks>
/// Maintain-class (ADR-038): the SPA's firm step offers exactly this list, and the submission route validates <c>firmId</c> against the
/// same membership set, so "only the caller's own firms, only id and name" is the contract that keeps one firm's identity from another's caller.
/// </remarks>
public sealed class ExternalUserContextFirmsTests
{
    private static readonly Guid ContactId = Guid.NewGuid();
    private static readonly Guid FirmA = Guid.NewGuid();
    private static readonly Guid FirmB = Guid.NewGuid();
    private static readonly Guid OtherFirm = Guid.NewGuid();

    private sealed class Fixture
    {
        public readonly InvoiceSubmissionServiceTests.Harness Harness = new();
        public readonly Mock<ExternalParticipationService> Participations;
        public readonly List<Guid> FirmReads = new();
        public bool Enabled = true;
        public readonly Dictionary<Guid, Entity> Organizations = new();
        public Exception? NameReadFailure;

        public Fixture(ActiveOrgMemberships memberships)
        {
            var config = new ConfigurationBuilder().AddInMemoryCollection().Build();
            Participations = new Mock<ExternalParticipationService>(
                new HttpClient(), Mock.Of<ITenantCache>(), config, Mock.Of<TokenCredential>(),
                Mock.Of<IHttpContextAccessor>(), NullLogger<ExternalParticipationService>.Instance, null!);
            Participations.Setup(p => p.ReadOrganizationMembershipsAsync(ContactId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(memberships);
        }

        public Fixture(Exception membershipFault) : this(ActiveOrgMemberships.None)
        {
            Participations.Setup(p => p.ReadOrganizationMembershipsAsync(ContactId, It.IsAny<CancellationToken>()))
                .ThrowsAsync(membershipFault);
        }

        public Fixture Org(Guid id, string? name, int state = 0)
        {
            var e = new Entity("sprk_organization", id) { ["statecode"] = new OptionSetValue(state) };
            if (name is not null)
            {
                e["sprk_name"] = name;
            }

            Organizations[id] = e;
            return this;
        }

        public async Task<IResult> CallAsync(CallerPrincipal? caller = null)
        {
            Harness.Options.Enabled = Enabled;
            var service = Harness.Build();
            Harness.Entities.Setup(e => e.RetrieveAsync("sprk_organization", It.IsAny<Guid>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((string _, Guid id, string[] _, CancellationToken _) =>
                {
                    FirmReads.Add(id);
                    if (NameReadFailure is { } failure)
                    {
                        throw failure;
                    }

                    return Organizations.TryGetValue(id, out var org) ? org : null!;
                });

            var http = new DefaultHttpContext();
            http.Items[CallerPrincipal.HttpContextItemsKey] = caller ?? Caller();
            return await ExternalUserContextEndpoint.HandleAsync(
                http, NullLogger<Program>.Instance, Harness.Monitor, Participations.Object, service, CancellationToken.None);
        }
    }

    private static CallerPrincipal Caller() => new()
    {
        Plane = CallerPrincipalPlane.CiamContact,
        ContactId = ContactId,
        Email = "ann@firm.example",
        ProjectAccess = Array.Empty<CallerProjectAccess>(),
    };

    private static ActiveOrgMemberships Member(params Guid[] firms) => new(firms, firms, false);

    private static ExternalUserContextResponse Ok(IResult result) =>
        result.Should().BeOfType<Ok<ExternalUserContextResponse>>().Subject.Value!;

    [Fact]
    public async Task Several_firms_are_listed_with_id_and_name_and_nothing_else()
    {
        var f = new Fixture(Member(FirmA, FirmB)).Org(FirmA, "Alpha LLP").Org(FirmB, "Beta & Co");

        var me = Ok(await f.CallAsync());

        me.Firms.Should().Equal(new ExternalFirmEntry(FirmA, "Alpha LLP"), new ExternalFirmEntry(FirmB, "Beta & Co"));
        typeof(ExternalFirmEntry).GetProperties().Select(p => p.Name).Should().BeEquivalentTo("FirmId", "Name");
    }

    [Fact]
    public async Task One_firm_is_listed()
    {
        var f = new Fixture(Member(FirmA)).Org(FirmA, "Alpha LLP");

        Ok(await f.CallAsync()).Firms.Should().Equal(new ExternalFirmEntry(FirmA, "Alpha LLP"));
    }

    [Fact]
    public async Task No_firm_is_an_empty_list_not_null_and_reads_no_organization()
    {
        var f = new Fixture(ActiveOrgMemberships.None);

        var me = Ok(await f.CallAsync());

        me.Firms.Should().NotBeNull().And.BeEmpty();
        f.FirmReads.Should().BeEmpty();
    }

    [Fact]
    public async Task Only_the_callers_own_firms_are_read_and_another_firm_never_leaks()
    {
        var f = new Fixture(Member(FirmA)).Org(FirmA, "Alpha LLP").Org(OtherFirm, "Other Firm (not the caller's)");

        var me = Ok(await f.CallAsync());

        me.Firms.Should().ContainSingle().Which.FirmId.Should().Be(FirmA);
        f.FirmReads.Should().Equal(FirmA);
        System.Text.Json.JsonSerializer.Serialize(me).Should().NotContain("Other Firm").And.NotContain(OtherFirm.ToString());
    }

    [Fact]
    public async Task A_firm_listed_twice_by_the_membership_read_is_listed_once()
    {
        var f = new Fixture(new ActiveOrgMemberships(new[] { FirmA, FirmA }, new[] { FirmA }, false)).Org(FirmA, "Alpha LLP");

        Ok(await f.CallAsync()).Firms.Should().ContainSingle();
    }

    [Fact]
    public async Task An_inactive_or_missing_firm_is_not_offered()
    {
        var f = new Fixture(Member(FirmA, FirmB)).Org(FirmA, "Alpha LLP", state: 1);

        Ok(await f.CallAsync()).Firms.Should().BeEmpty();
    }

    [Fact]
    public async Task A_firm_without_a_name_is_listed_under_its_id()
    {
        var f = new Fixture(Member(FirmA)).Org(FirmA, name: null);

        Ok(await f.CallAsync()).Firms.Should().Equal(new ExternalFirmEntry(FirmA, FirmA.ToString()));
    }

    [Fact]
    public async Task Ebilling_off_returns_no_firms_and_reads_nothing()
    {
        var f = new Fixture(Member(FirmA)).Org(FirmA, "Alpha LLP");
        f.Enabled = false;

        var me = Ok(await f.CallAsync());

        me.Firms.Should().BeNull();
        f.FirmReads.Should().BeEmpty();
        f.Participations.Verify(p => p.ReadOrganizationMembershipsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task An_unreadable_membership_is_null_not_an_empty_list_and_me_still_succeeds()
    {
        var f = new Fixture(ActiveOrgMemberships.Failed);

        var me = Ok(await f.CallAsync());

        me.Firms.Should().BeNull();
        me.ContactId.Should().Be(ContactId);
    }

    [Fact]
    public async Task A_thrown_membership_read_is_null_and_me_still_succeeds()
    {
        var f = new Fixture(new TimeoutException("throttled")).Org(FirmA, "Alpha LLP");

        Ok(await f.CallAsync()).Firms.Should().BeNull();
    }

    [Fact]
    public async Task A_faulted_name_read_is_null_not_a_partial_list()
    {
        var f = new Fixture(Member(FirmA, FirmB)).Org(FirmA, "Alpha LLP").Org(FirmB, "Beta & Co");
        f.NameReadFailure = new TimeoutException("throttled");

        Ok(await f.CallAsync()).Firms.Should().BeNull();
    }

    [Fact]
    public async Task An_unreadable_contact_is_null()
    {
        var f = new Fixture(Member(FirmA)).Org(FirmA, "Alpha LLP");
        var caller = new CallerPrincipal
        {
            Plane = CallerPrincipalPlane.Workforce,
            ContactId = Guid.Empty,
            ContactUnreadable = true,
            Email = "w@firm.example",
            ProjectAccess = Array.Empty<CallerProjectAccess>(),
        };

        Ok(await f.CallAsync(caller)).Firms.Should().BeNull();
    }

    [Fact]
    public async Task A_caller_with_no_contact_has_no_firms()
    {
        var f = new Fixture(Member(FirmA)).Org(FirmA, "Alpha LLP");
        var caller = new CallerPrincipal
        {
            Plane = CallerPrincipalPlane.Workforce,
            ContactId = Guid.Empty,
            Email = "w@firm.example",
            ProjectAccess = Array.Empty<CallerProjectAccess>(),
        };

        Ok(await f.CallAsync(caller)).Firms.Should().BeEmpty();
        f.Participations.Verify(p => p.ReadOrganizationMembershipsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Existing_fields_keep_their_shape_and_values()
    {
        var f = new Fixture(Member(FirmA)).Org(FirmA, "Alpha LLP");
        var projectId = Guid.NewGuid();
        var caller = new CallerPrincipal
        {
            Plane = CallerPrincipalPlane.CiamContact,
            ContactId = ContactId,
            Email = "ann@firm.example",
            ProjectAccess = new[] { CallerProjectAccess.FromLevel(projectId, ExternalAccessLevel.Collaborate) },
        };

        var me = Ok(await f.CallAsync(caller));

        me.ContactId.Should().Be(ContactId);
        me.Email.Should().Be("ann@firm.example");
        me.Projects.Should().Equal(new ProjectAccessEntry(projectId, "Collaborate"));
        me.SystemUserId.Should().BeNull();
        // The wire names the SPA reads: the old four are unchanged, the new one is "firms" (camelCase), each entry {firmId, name}.
        var json = System.Text.Json.JsonSerializer.Serialize(me, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
        json.Should().Contain("\"contactId\"").And.Contain("\"email\"").And.Contain("\"projects\"").And.Contain("\"systemUserId\"")
            .And.Contain("\"firms\":[{\"firmId\":\"" + FirmA + "\",\"name\":\"Alpha LLP\"}]");
    }

    [Fact]
    public void The_synchronous_Handle_is_unchanged_and_carries_no_firms()
    {
        var http = new DefaultHttpContext();
        http.Items[CallerPrincipal.HttpContextItemsKey] = Caller();

        var result = ExternalUserContextEndpoint.Handle(http, NullLogger<Program>.Instance);

        Ok(result).Firms.Should().BeNull();
    }

    [Fact]
    public async Task A_missing_principal_is_still_the_500_problem()
    {
        var f = new Fixture(Member(FirmA));
        f.Harness.Options.Enabled = true;
        var http = new DefaultHttpContext();

        var result = await ExternalUserContextEndpoint.HandleAsync(
            http, NullLogger<Program>.Instance, f.Harness.Monitor, f.Participations.Object, f.Harness.Build(), CancellationToken.None);

        result.Should().NotBeOfType<Ok<ExternalUserContextResponse>>();
        f.Participations.Verify(p => p.ReadOrganizationMembershipsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
