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

    // Default: a RESOLVED (confirmed) parent — the good case where a reply legitimately inherits at 1.0 and
    // auto-files (P3). Use ParentWithStatus to model an unconfirmed parent.
    private static DataverseEntity ParentWith(params (string field, string entity, Guid id)[] regarding)
        => ParentWithStatus(100000000, regarding);

    private static DataverseEntity ParentWithStatus(int status, params (string field, string entity, Guid id)[] regarding)
    {
        var parent = new DataverseEntity("sprk_communication") { Id = Guid.NewGuid() };
        parent["sprk_associationstatus"] = new OptionSetValue(status);
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
    public async Task Evaluate_UnconfirmedParent_InheritsAtSuggestBand_NotAutoFileStrength()
    {
        // P3 (FR-12 UAT): a parent whose OWN association is not Resolved (here Suggested) is unconfirmed —
        // inheriting it at 1.0 would auto-file a weak/unconfirmed association across every reply in the thread.
        // The rung must inherit it at a suggest-band confidence (0.65 < the 0.85 auto-file threshold) instead,
        // so the reply surfaces it for review rather than auto-filing off an unconfirmed parent.
        var matterId = Guid.NewGuid();
        var parent = ParentWithStatus(100000003, ("sprk_regardingmatter", "sprk_matter", matterId)); // Suggested
        _dv.Setup(d => d.GetCommunicationByInternetMessageIdAsync("<parent@x.com>", It.IsAny<CancellationToken>()))
            .ReturnsAsync(parent);

        var matches = await _rung.EvaluateAsync(
            Envelope(inReplyTo: "<parent@x.com>"), new AssociationContext(), CancellationToken.None);

        var match = matches.Should().ContainSingle().Subject;
        match.Target!.Id.Should().Be(matterId, "the inherited matter is still surfaced");
        match.Confidence.Should().Be(0.65);
        match.Confidence.Should().BeLessThan(0.85, "a reply must not auto-file off an unconfirmed parent");
        match.Provenance.Should().Contain("parent=unconfirmed");
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
