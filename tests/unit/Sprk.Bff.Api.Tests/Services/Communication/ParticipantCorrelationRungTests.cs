using FluentAssertions;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Communication.Engine;
using Sprk.Bff.Api.Services.Communication.Engine.Rungs;
using Sprk.Bff.Api.Services.Communication.Models;
using Xunit;
using DataverseEntity = Microsoft.Xrm.Sdk.Entity;

namespace Sprk.Bff.Api.Tests.Services.Communication;

/// <summary>
/// Rung 2 (participant correlation) tests: sender/recipient → contact + junction memberships +
/// sender-domain org/account, in the 0.60–0.85 band. Includes the FR-04/DEC-3 guard that the org
/// target is sprk_organization (never account) and direction-symmetry.
/// </summary>
public class ParticipantCorrelationRungTests
{
    private readonly Mock<ICommunicationDataverseService> _dv = new();
    private readonly ParticipantCorrelationRung _rung;

    public ParticipantCorrelationRungTests()
    {
        _rung = new ParticipantCorrelationRung(_dv.Object);
        // Default: no memberships for any contact (tests opt in explicitly).
        _dv.Setup(d => d.QueryContactMembershipsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<DataverseEntity>());
    }

    private static NormalizedMessage Envelope(string? from = null, string[]? to = null, string[]? cc = null) =>
        new()
        {
            Direction = CommunicationDirection.Incoming,
            From = from,
            To = to ?? Array.Empty<string>(),
            Cc = cc ?? Array.Empty<string>(),
        };

    private static DataverseEntity Membership(string entityName, Guid recordId, string? role) =>
        new("sprk_userentityassociation")
        {
            ["sprk_entitylogicalname"] = entityName,
            ["sprk_entityrecordid"] = recordId.ToString("D").ToLowerInvariant(),
            ["sprk_role"] = role,
        };

    [Fact]
    public async Task Evaluate_SenderContact_EmitsPersonMatchInBand()
    {
        var contactId = Guid.NewGuid();
        _dv.Setup(d => d.QueryContactByEmailAsync("jane@acme.com", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DataverseEntity("contact") { Id = contactId });

        var matches = await _rung.EvaluateAsync(Envelope(from: "jane@acme.com"), new AssociationContext(), CancellationToken.None);

        matches.Should().ContainSingle(m => m.RegardingFieldName == "sprk_regardingperson"
            && m.Target.Id == contactId && m.Confidence >= 0.60 && m.Confidence <= 0.85);
    }

    [Fact]
    public async Task Evaluate_SenderMembership_EmitsMatterMatchWithRoleProvenance()
    {
        var contactId = Guid.NewGuid();
        var matterId = Guid.NewGuid();
        _dv.Setup(d => d.QueryContactByEmailAsync("counsel@firm.com", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DataverseEntity("contact") { Id = contactId });
        _dv.Setup(d => d.QueryContactMembershipsAsync(contactId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { Membership("sprk_matter", matterId, "assignedAttorney") });

        var matches = await _rung.EvaluateAsync(Envelope(from: "counsel@firm.com"), new AssociationContext(), CancellationToken.None);

        var matterMatch = matches.Should().ContainSingle(m => m.RegardingFieldName == "sprk_regardingmatter").Subject;
        matterMatch.Target.Id.Should().Be(matterId);
        matterMatch.Target.LogicalName.Should().Be("sprk_matter");
        matterMatch.Confidence.Should().BeInRange(0.60, 0.85);
        matterMatch.Provenance.Should().Contain("assignedAttorney");
    }

    [Fact]
    public async Task Evaluate_RecipientMembership_EmitsMatch()
    {
        var recipientContactId = Guid.NewGuid();
        var projectId = Guid.NewGuid();
        // Sender has no contact; a recipient is on a project.
        _dv.Setup(d => d.QueryContactByEmailAsync("external@vendor.com", It.IsAny<CancellationToken>()))
            .ReturnsAsync((DataverseEntity?)null);
        _dv.Setup(d => d.QueryContactByEmailAsync("teammate@firm.com", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DataverseEntity("contact") { Id = recipientContactId });
        _dv.Setup(d => d.QueryContactMembershipsAsync(recipientContactId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { Membership("sprk_project", projectId, "teamMember") });

        var matches = await _rung.EvaluateAsync(
            Envelope(from: "external@vendor.com", to: new[] { "teammate@firm.com" }), new AssociationContext(), CancellationToken.None);

        matches.Should().ContainSingle(m => m.RegardingFieldName == "sprk_regardingproject" && m.Target.Id == projectId);
    }

    [Fact]
    public async Task Evaluate_SenderDomain_WritesOrgToSprkOrganization_NeverAccount()
    {
        var orgId = Guid.NewGuid();
        var accountId = Guid.NewGuid();
        _dv.Setup(d => d.QueryContactByEmailAsync("ap@acme.com", It.IsAny<CancellationToken>()))
            .ReturnsAsync((DataverseEntity?)null);
        _dv.Setup(d => d.QueryOrganizationByDomainAsync("acme.com", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DataverseEntity("sprk_organization") { Id = orgId });
        _dv.Setup(d => d.QueryAccountByDomainAsync("acme.com", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DataverseEntity("account") { Id = accountId });

        var matches = await _rung.EvaluateAsync(Envelope(from: "ap@acme.com"), new AssociationContext(), CancellationToken.None);

        // Org association MUST target sprk_organization (DEC-3/FR-04) — never account.
        var org = matches.Should().ContainSingle(m => m.RegardingFieldName == "sprk_regardingorganization").Subject;
        org.Target.LogicalName.Should().Be("sprk_organization");
        org.Target.Id.Should().Be(orgId);
        // The account association is a SEPARATE, intentional lookup.
        var account = matches.Should().ContainSingle(m => m.RegardingFieldName == "sprk_regardingaccount").Subject;
        account.Target.LogicalName.Should().Be("account");
        account.Target.Id.Should().Be(accountId);
        // No org-lookup match ever carries an account target.
        matches.Should().NotContain(m => m.RegardingFieldName == "sprk_regardingorganization" && m.Target.LogicalName == "account");
    }

    [Fact]
    public async Task Evaluate_CommonProviderDomain_SkipsOrgAndAccountQueries()
    {
        _dv.Setup(d => d.QueryContactByEmailAsync("someone@gmail.com", It.IsAny<CancellationToken>()))
            .ReturnsAsync((DataverseEntity?)null);

        await _rung.EvaluateAsync(Envelope(from: "someone@gmail.com"), new AssociationContext(), CancellationToken.None);

        _dv.Verify(d => d.QueryOrganizationByDomainAsync("gmail.com", It.IsAny<CancellationToken>()), Times.Never);
        _dv.Verify(d => d.QueryAccountByDomainAsync("gmail.com", It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Evaluate_AllMatches_LandInConfidenceBand()
    {
        var contactId = Guid.NewGuid();
        _dv.Setup(d => d.QueryContactByEmailAsync("jane@acme.com", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DataverseEntity("contact") { Id = contactId });
        _dv.Setup(d => d.QueryContactMembershipsAsync(contactId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { Membership("sprk_matter", Guid.NewGuid(), "paralegal") });
        _dv.Setup(d => d.QueryOrganizationByDomainAsync("acme.com", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DataverseEntity("sprk_organization") { Id = Guid.NewGuid() });

        var matches = await _rung.EvaluateAsync(Envelope(from: "jane@acme.com"), new AssociationContext(), CancellationToken.None);

        matches.Should().NotBeEmpty();
        matches.Should().OnlyContain(m => m.Confidence >= 0.60 && m.Confidence <= 0.85);
    }

    [Fact]
    public async Task Evaluate_DirectionSymmetry_IdenticalMatchesInboundAndOutbound()
    {
        var contactId = Guid.NewGuid();
        _dv.Setup(d => d.QueryContactByEmailAsync("jane@acme.com", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DataverseEntity("contact") { Id = contactId });

        var inbound = await _rung.EvaluateAsync(
            Envelope(from: "jane@acme.com") with { Direction = CommunicationDirection.Incoming },
            new AssociationContext(), CancellationToken.None);
        var outbound = await _rung.EvaluateAsync(
            Envelope(from: "jane@acme.com") with { Direction = CommunicationDirection.Outgoing },
            new AssociationContext(), CancellationToken.None);

        inbound.Should().BeEquivalentTo(outbound);
        inbound.Should().ContainSingle(m => m.RegardingFieldName == "sprk_regardingperson");
    }
}
