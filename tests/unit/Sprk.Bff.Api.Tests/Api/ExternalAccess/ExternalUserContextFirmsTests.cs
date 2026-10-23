using Azure.Core;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
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
/// real <see cref="ExternalUserContextEndpoint.HandleAsync"/> with the principal the group filter would have stored; the membership read,
/// the contact-state read and the Dataverse organization read are substituted at their narrow seams.
/// </summary>
/// <remarks>
/// Maintain-class (ADR-038): the SPA's firm step offers exactly this list, and the submission route validates <c>firmId</c> against the
/// same membership set, so "only the caller's own firms, only id and name" is the contract that keeps one firm's identity from another's caller.
/// The organization store below behaves like Dataverse where it matters: it REFUSES a column the table does not have (the defect that shipped
/// a null firm list on every call: <c>sprk_name</c> is not an organization column), and it honours the id and state conditions of the query.
/// </remarks>
public sealed class ExternalUserContextFirmsTests
{
    private static readonly Guid ContactId = Guid.NewGuid();
    private static readonly Guid OtherContactId = Guid.NewGuid();
    private static readonly Guid FirmA = Guid.NewGuid();
    private static readonly Guid FirmB = Guid.NewGuid();
    private static readonly Guid FirmC = Guid.NewGuid();
    private static readonly Guid OtherFirm = Guid.NewGuid();

    /// <summary>The columns sprk_organization really has among those this read could name (live: RegardingNameFields, task 161).</summary>
    private static readonly HashSet<string> OrganizationColumns = new(StringComparer.Ordinal)
    {
        "sprk_organizationid", "sprk_organizationname", "statecode",
    };

    private sealed class Fixture
    {
        public readonly InvoiceSubmissionServiceTests.Harness Harness = new();
        public readonly Mock<ExternalParticipationService> Participations;
        public readonly List<QueryExpression> Queries = new();
        public readonly Dictionary<Guid, Entity> Organizations = new();
        public bool Enabled = true;
        public Exception? NameReadFailure;
        public ContactRecordState ContactState = ContactRecordState.Active;

        public Fixture(ActiveOrgMemberships memberships)
        {
            var config = new ConfigurationBuilder().AddInMemoryCollection().Build();
            Participations = new Mock<ExternalParticipationService>(
                new HttpClient(), Mock.Of<ITenantCache>(), config, Mock.Of<TokenCredential>(),
                Mock.Of<IHttpContextAccessor>(), NullLogger<ExternalParticipationService>.Instance, null!);
            Participations.Setup(p => p.ReadOrganizationMembershipsAsync(ContactId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(memberships);
            Participations.Setup(p => p.QueryContactStateAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => ContactState);
        }

        public Fixture(Exception membershipFault) : this(ActiveOrgMemberships.None)
        {
            Participations.Setup(p => p.ReadOrganizationMembershipsAsync(ContactId, It.IsAny<CancellationToken>()))
                .ThrowsAsync(membershipFault);
        }

        /// <summary>Another contact with its own memberships (it is never the caller of these tests unless they say so).</summary>
        public Fixture OtherContact(Guid contactId, ActiveOrgMemberships memberships)
        {
            Participations.Setup(p => p.ReadOrganizationMembershipsAsync(contactId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(memberships);
            return this;
        }

        public Fixture Org(Guid id, string? name, int state = 0)
        {
            var e = new Entity("sprk_organization", id) { ["statecode"] = new OptionSetValue(state) };
            if (name is not null)
            {
                e["sprk_organizationname"] = name;
            }

            Organizations[id] = e;
            return this;
        }

        /// <summary>Every firm id any organization query asked for.</summary>
        public IReadOnlyList<Guid> FirmIdsRead => Queries
            .SelectMany(q => q.Criteria.Conditions.Where(c => c.AttributeName == "sprk_organizationid").SelectMany(c => c.Values))
            .Cast<Guid>().ToList();

        public IServiceProvider Services()
        {
            var services = new ServiceCollection();
            services.AddSingleton(Participations.Object);
            services.AddSingleton(Harness.Build());
            return services.BuildServiceProvider();
        }

        public async Task<IResult> CallAsync(CallerPrincipal? caller = null, IServiceProvider? services = null)
        {
            Harness.Options.Enabled = Enabled;
            Harness.Entities.Setup(e => e.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((QueryExpression query, CancellationToken _) =>
                {
                    Queries.Add(query);
                    if (NameReadFailure is { } failure)
                    {
                        throw failure;
                    }

                    // Dataverse answers 400 for a column the table does not have.
                    if (query.ColumnSet.Columns.FirstOrDefault(c => !OrganizationColumns.Contains(c)) is { } unknown)
                    {
                        throw new InvalidOperationException($"400: Could not find a property named '{unknown}' on type sprk_organization.");
                    }

                    var ids = query.Criteria.Conditions
                        .Where(c => c.AttributeName == "sprk_organizationid" && c.Operator == ConditionOperator.In)
                        .SelectMany(c => c.Values).Cast<Guid>().ToHashSet();
                    var activeOnly = query.Criteria.Conditions.Any(c => c.AttributeName == "statecode" && c.Operator == ConditionOperator.Equal);
                    var rows = Organizations.Values
                        .Where(o => ids.Contains(o.Id))
                        .Where(o => !activeOnly || o.GetAttributeValue<OptionSetValue>("statecode").Value == 0)
                        .Select(o =>
                        {
                            var projected = new Entity("sprk_organization", o.Id);
                            foreach (var column in query.ColumnSet.Columns.Where(o.Contains))
                            {
                                projected[column] = o[column];
                            }

                            return projected;
                        })
                        .ToList();
                    return new EntityCollection(rows);
                });

            var http = new DefaultHttpContext { RequestServices = services ?? Services() };
            http.Items[CallerPrincipal.HttpContextItemsKey] = caller ?? Caller();
            return await ExternalUserContextEndpoint.HandleAsync(http, NullLogger<Program>.Instance, Harness.Monitor, CancellationToken.None);
        }
    }

    private static CallerPrincipal Caller(Guid? contactId = null) => new()
    {
        Plane = CallerPrincipalPlane.CiamContact,
        ContactId = contactId ?? ContactId,
        Email = "ann@firm.example",
        ProjectAccess = Array.Empty<CallerProjectAccess>(),
    };

    private static ActiveOrgMemberships Member(params Guid[] firms) => new(firms, firms, false);

    private static ExternalUserContextResponse Ok(IResult result) =>
        result.Should().BeOfType<Ok<ExternalUserContextResponse>>().Subject.Value!;

    private static readonly System.Text.Json.JsonSerializerOptions Web = new(System.Text.Json.JsonSerializerDefaults.Web);

    [Fact]
    public async Task Several_firms_are_listed_with_id_and_name_and_nothing_else()
    {
        var f = new Fixture(Member(FirmA, FirmB)).Org(FirmA, "Alpha LLP").Org(FirmB, "Beta & Co");

        var me = Ok(await f.CallAsync());

        me.Firms.Should().Equal(new ExternalFirmEntry(FirmA, "Alpha LLP"), new ExternalFirmEntry(FirmB, "Beta & Co"));
        typeof(ExternalFirmEntry).GetProperties().Select(p => p.Name).Should().BeEquivalentTo("FirmId", "Name");
    }

    [Fact]
    public async Task The_names_are_read_in_one_query_that_selects_exactly_the_real_columns()
    {
        // The defect this pins: the read selected sprk_name, which sprk_organization does not have (its primary name is
        // sprk_organizationname), so every call answered a 400 that the endpoint swallowed into "firms unknown".
        var f = new Fixture(Member(FirmA, FirmB, FirmC)).Org(FirmA, "Alpha LLP").Org(FirmB, "Beta & Co").Org(FirmC, "Gamma");

        var me = Ok(await f.CallAsync());

        me.Firms.Should().HaveCount(3);
        var query = f.Queries.Should().ContainSingle("the firm names are one batched read, not one read per firm").Subject;
        query.EntityName.Should().Be("sprk_organization");
        query.ColumnSet.Columns.Should().Equal(new[] { "statecode", "sprk_organizationname" });
        f.FirmIdsRead.Should().BeEquivalentTo(new[] { FirmA, FirmB, FirmC });
        f.Harness.Entities.Verify(e => e.RetrieveAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()), Times.Never);
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
        f.Queries.Should().BeEmpty();
    }

    [Fact]
    public async Task Only_the_callers_own_firms_are_read_and_another_firm_never_leaks()
    {
        var f = new Fixture(Member(FirmA)).Org(FirmA, "Alpha LLP").Org(OtherFirm, "Other Firm (not the caller's)");

        var me = Ok(await f.CallAsync());

        me.Firms.Should().ContainSingle().Which.FirmId.Should().Be(FirmA);
        f.FirmIdsRead.Should().Equal(FirmA);
        System.Text.Json.JsonSerializer.Serialize(me).Should().NotContain("Other Firm").And.NotContain(OtherFirm.ToString());
    }

    [Fact]
    public async Task A_second_contact_with_another_firm_never_appears_in_the_callers_firms()
    {
        // Must-not-fire: another contact's membership (including a firm the caller does not belong to) is neither read nor listed.
        var f = new Fixture(Member(FirmA))
            .OtherContact(OtherContactId, Member(FirmB, OtherFirm))
            .Org(FirmA, "Alpha LLP").Org(FirmB, "Beta & Co").Org(OtherFirm, "Other Firm (not the caller's)");

        var mine = Ok(await f.CallAsync());

        mine.Firms.Should().Equal(new ExternalFirmEntry(FirmA, "Alpha LLP"));
        f.FirmIdsRead.Should().Equal(FirmA);
        f.Participations.Verify(p => p.ReadOrganizationMembershipsAsync(OtherContactId, It.IsAny<CancellationToken>()), Times.Never);
        System.Text.Json.JsonSerializer.Serialize(mine).Should().NotContain("Beta").And.NotContain("Other Firm");

        // Must-fire: the same fixture answers the other contact with ITS firms, so the control above is not an artefact of the setup.
        f.Queries.Clear();
        var theirs = Ok(await f.CallAsync(Caller(OtherContactId)));
        theirs.Firms!.Select(x => x.FirmId).Should().BeEquivalentTo(new[] { FirmB, OtherFirm });
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
    public async Task Ebilling_off_returns_no_firms_reads_nothing_and_builds_no_firm_service()
    {
        var f = new Fixture(Member(FirmA)).Org(FirmA, "Alpha LLP");
        f.Enabled = false;

        // A provider that refuses every resolution: with the flag off the handler must not ask for the participation or submission service.
        var me = Ok(await f.CallAsync(services: new RefusingServiceProvider()));

        me.Firms.Should().BeNull();
        f.Queries.Should().BeEmpty();
        f.Participations.Verify(p => p.ReadOrganizationMembershipsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        f.Participations.Verify(p => p.QueryContactStateAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Ebilling_on_resolves_the_firm_services_after_the_flag_check()
    {
        var f = new Fixture(Member(FirmA)).Org(FirmA, "Alpha LLP");
        var provider = new CountingServiceProvider(f.Services());

        Ok(await f.CallAsync(services: provider)).Firms.Should().ContainSingle();

        provider.Resolved.Should().Contain(typeof(ExternalParticipationService)).And.Contain(typeof(InvoiceSubmissionService));
    }

    [Fact]
    public async Task A_workforce_systemuser_gets_an_empty_firm_list_even_with_a_linked_contact_that_has_firms()
    {
        var f = new Fixture(Member(FirmA, FirmB)).Org(FirmA, "Alpha LLP").Org(FirmB, "Beta & Co");
        var caller = new CallerPrincipal
        {
            Plane = CallerPrincipalPlane.Workforce,
            ContactId = ContactId, // the systemuser's LINKED contact (task 141), which belongs to two firms
            SystemUserId = Guid.NewGuid(),
            Email = "staff@spaarke.example",
            ProjectAccess = Array.Empty<CallerProjectAccess>(),
        };

        var me = Ok(await f.CallAsync(caller));

        me.Firms.Should().NotBeNull().And.BeEmpty();
        f.Queries.Should().BeEmpty();
        f.Participations.Verify(p => p.ReadOrganizationMembershipsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        me.SystemUserId.Should().Be(caller.SystemUserId);
    }

    [Fact]
    public async Task A_deactivated_contact_confers_no_firms()
    {
        // UAC task 137: a contact that is not Active confers nothing; read live, never from the membership read alone.
        var f = new Fixture(Member(FirmA)).Org(FirmA, "Alpha LLP");
        f.ContactState = ContactRecordState.Inactive;

        var me = Ok(await f.CallAsync());

        me.Firms.Should().NotBeNull().And.BeEmpty();
        f.Queries.Should().BeEmpty();
        f.Participations.Verify(p => p.ReadOrganizationMembershipsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task A_contact_whose_state_cannot_be_read_is_null_not_an_empty_list()
    {
        var f = new Fixture(Member(FirmA)).Org(FirmA, "Alpha LLP");
        f.ContactState = ContactRecordState.Unreadable;

        Ok(await f.CallAsync()).Firms.Should().BeNull();
    }

    [Fact]
    public async Task An_active_contact_is_the_control_for_the_deactivated_one()
    {
        var f = new Fixture(Member(FirmA)).Org(FirmA, "Alpha LLP");
        f.ContactState = ContactRecordState.Active;

        Ok(await f.CallAsync()).Firms.Should().ContainSingle();
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
    public async Task A_thrown_membership_read_is_null_and_me_still_succeeds_with_its_own_log_message()
    {
        var f = new Fixture(new TimeoutException("throttled")).Org(FirmA, "Alpha LLP");
        var logger = new RecordingLogger();

        var me = Ok(await ExternalUserContextEndpointCall(f, logger));

        me.Firms.Should().BeNull();
        // The /me path logs what happened on /me (not "[CONTACT-GRANT] ... refusing (fail closed)", which is the grant route's wording).
        logger.Entries.Should().Contain(e => e.Contains("[EBILLING-FIRMS]") && e.Contains("firms are unknown"));
        logger.Entries.Should().NotContain(e => e.Contains("CONTACT-GRANT") || e.Contains("refusing"));
    }

    private static async Task<IResult> ExternalUserContextEndpointCall(Fixture f, ILogger<Program> logger)
    {
        f.Harness.Options.Enabled = true;
        var http = new DefaultHttpContext { RequestServices = f.Services() };
        http.Items[CallerPrincipal.HttpContextItemsKey] = Caller();
        return await ExternalUserContextEndpoint.HandleAsync(http, logger, f.Harness.Monitor, CancellationToken.None);
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
        var json = System.Text.Json.JsonSerializer.Serialize(me, Web);
        json.Should().Contain("\"contactId\"").And.Contain("\"email\"").And.Contain("\"projects\"").And.Contain("\"systemUserId\"")
            .And.Contain("\"firms\":[{\"firmId\":\"" + FirmA + "\",\"name\":\"Alpha LLP\"}]");
    }

    [Fact]
    public void Unknown_firms_are_omitted_from_the_json_and_an_empty_list_is_not()
    {
        var unknown = new ExternalUserContextResponse(ContactId, "ann@firm.example", Array.Empty<ProjectAccessEntry>());
        var none = unknown with { Firms = Array.Empty<ExternalFirmEntry>() };

        System.Text.Json.JsonSerializer.Serialize(unknown, Web).Should().NotContain("firms");
        System.Text.Json.JsonSerializer.Serialize(none, Web).Should().Contain("\"firms\":[]");
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
        var http = new DefaultHttpContext { RequestServices = f.Services() };

        var result = await ExternalUserContextEndpoint.HandleAsync(
            http, NullLogger<Program>.Instance, f.Harness.Monitor, CancellationToken.None);

        result.Should().NotBeOfType<Ok<ExternalUserContextResponse>>();
        f.Participations.Verify(p => p.ReadOrganizationMembershipsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ── Doubles ──

    private sealed class RefusingServiceProvider : IServiceProvider
    {
        public object? GetService(Type serviceType) =>
            throw new InvalidOperationException($"{serviceType.Name} was resolved although eBilling is off.");
    }

    private sealed class CountingServiceProvider(IServiceProvider inner) : IServiceProvider
    {
        public List<Type> Resolved { get; } = new();

        public object? GetService(Type serviceType)
        {
            Resolved.Add(serviceType);
            return inner.GetService(serviceType);
        }
    }

    private sealed class RecordingLogger : ILogger<Program>
    {
        public List<string> Entries { get; } = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Entries.Add(formatter(state, exception));
    }
}
