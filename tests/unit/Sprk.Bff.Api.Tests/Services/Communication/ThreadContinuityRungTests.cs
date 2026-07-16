using FluentAssertions;
using Microsoft.Xrm.Sdk;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Communication.Engine;
using Sprk.Bff.Api.Services.Communication.Engine.Rungs;
using Sprk.Bff.Api.Services.Communication.Models;
using Xunit;
using DataverseEntity = Microsoft.Xrm.Sdk.Entity;

namespace Sprk.Bff.Api.Tests.Services.Communication;

/// <summary>
/// Rung 1 (thread continuity) tests: walk In-Reply-To + the References ancestor chain to the nearest
/// existing parent communication and copy its regarding across targets. Includes direction-symmetry.
/// </summary>
public class ThreadContinuityRungTests
{
    private readonly Mock<ICommunicationDataverseService> _dv = new();
    private readonly ThreadContinuityRung _rung;

    public ThreadContinuityRungTests() => _rung = new ThreadContinuityRung(_dv.Object);

    private static NormalizedMessage Envelope(
        CommunicationDirection direction = CommunicationDirection.Incoming,
        string? inReplyTo = null,
        params string[] references) =>
        new() { Direction = direction, InReplyTo = inReplyTo, References = references };

    private static DataverseEntity ParentWith(params (string field, string entity, Guid id)[] regarding)
    {
        var parent = new DataverseEntity("sprk_communication") { Id = Guid.NewGuid() };
        foreach (var (field, entity, id) in regarding)
            parent[field] = new EntityReference(entity, id);
        return parent;
    }

    [Fact]
    public async Task Evaluate_InReplyToParent_CopiesAllParentRegarding()
    {
        var matterId = Guid.NewGuid();
        var orgId = Guid.NewGuid();
        var parent = ParentWith(
            ("sprk_regardingmatter", "sprk_matter", matterId),
            ("sprk_regardingorganization", "sprk_organization", orgId));
        _dv.Setup(d => d.GetCommunicationByInternetMessageIdAsync("<parent@x.com>", It.IsAny<CancellationToken>()))
            .ReturnsAsync(parent);

        var matches = await _rung.EvaluateAsync(
            Envelope(inReplyTo: "<parent@x.com>"), new AssociationContext(), CancellationToken.None);

        matches.Should().HaveCount(2);
        matches.Should().Contain(m => m.RegardingFieldName == "sprk_regardingmatter" && m.Target!.Id == matterId);
        matches.Should().Contain(m => m.RegardingFieldName == "sprk_regardingorganization" && m.Target!.Id == orgId);
        matches.Should().OnlyContain(m => m.Rung == RungKind.ThreadContinuity && m.Confidence == 1.0);
    }

    [Fact]
    public async Task Evaluate_ReferencesChain_UsesNearestExistingAncestor()
    {
        // References are oldest→newest; the nearest ancestor (last entry) should be tried first.
        var nearMatterId = Guid.NewGuid();
        _dv.Setup(d => d.GetCommunicationByInternetMessageIdAsync("<near@x.com>", It.IsAny<CancellationToken>()))
            .ReturnsAsync(ParentWith(("sprk_regardingmatter", "sprk_matter", nearMatterId)));
        // <old@x.com> also exists but must NOT be used (nearest wins).
        _dv.Setup(d => d.GetCommunicationByInternetMessageIdAsync("<old@x.com>", It.IsAny<CancellationToken>()))
            .ReturnsAsync(ParentWith(("sprk_regardingmatter", "sprk_matter", Guid.NewGuid())));

        var matches = await _rung.EvaluateAsync(
            Envelope(references: new[] { "<old@x.com>", "<near@x.com>" }), new AssociationContext(), CancellationToken.None);

        matches.Should().ContainSingle().Which.Target!.Id.Should().Be(nearMatterId);
        _dv.Verify(d => d.GetCommunicationByInternetMessageIdAsync("<old@x.com>", It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Evaluate_NoThreadHeaders_ReturnsEmpty()
    {
        var matches = await _rung.EvaluateAsync(Envelope(), new AssociationContext(), CancellationToken.None);
        matches.Should().BeEmpty();
    }

    [Fact]
    public async Task Evaluate_ParentExistsButHasNoRegarding_ReturnsEmpty()
    {
        _dv.Setup(d => d.GetCommunicationByInternetMessageIdAsync("<parent@x.com>", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DataverseEntity("sprk_communication") { Id = Guid.NewGuid() });

        var matches = await _rung.EvaluateAsync(
            Envelope(inReplyTo: "<parent@x.com>"), new AssociationContext(), CancellationToken.None);

        matches.Should().BeEmpty();
    }

    [Fact]
    public async Task Evaluate_DirectionSymmetry_IdenticalMatchesInboundAndOutbound()
    {
        var matterId = Guid.NewGuid();
        _dv.Setup(d => d.GetCommunicationByInternetMessageIdAsync("<parent@x.com>", It.IsAny<CancellationToken>()))
            .ReturnsAsync(ParentWith(("sprk_regardingmatter", "sprk_matter", matterId)));

        var inbound = await _rung.EvaluateAsync(
            Envelope(CommunicationDirection.Incoming, "<parent@x.com>"), new AssociationContext(), CancellationToken.None);
        var outbound = await _rung.EvaluateAsync(
            Envelope(CommunicationDirection.Outgoing, "<parent@x.com>"), new AssociationContext(), CancellationToken.None);

        inbound.Should().BeEquivalentTo(outbound);
        inbound.Should().ContainSingle().Which.Target!.Id.Should().Be(matterId);
    }
}
