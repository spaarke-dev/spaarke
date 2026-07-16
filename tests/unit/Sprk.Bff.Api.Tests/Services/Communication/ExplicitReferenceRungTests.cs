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
/// Rung 0 (explicit reference) tests: caller-supplied regarding is authoritative across targets;
/// subject matter-tokens resolve to sprk_matter; caller-supplied wins over subject parsing. Includes
/// direction-symmetry (the rung reads only the envelope + context, so inbound == outbound).
/// </summary>
public class ExplicitReferenceRungTests
{
    private readonly Mock<ICommunicationDataverseService> _dv = new();
    private readonly ExplicitReferenceRung _rung;

    public ExplicitReferenceRungTests() => _rung = new ExplicitReferenceRung(_dv.Object);

    private static NormalizedMessage Envelope(CommunicationDirection direction, string? subject = null) =>
        new() { Direction = direction, Subject = subject };

    [Fact]
    public async Task Evaluate_CallerSuppliedRegarding_EmitsAuthoritativeMatchForEachTarget()
    {
        var matterId = Guid.NewGuid();
        var orgId = Guid.NewGuid();
        var ctx = new AssociationContext
        {
            CallerSuppliedRegarding = new[]
            {
                new CommunicationAssociation { EntityType = "sprk_matter", EntityId = matterId, EntityName = "Acme v Beta" },
                new CommunicationAssociation { EntityType = "sprk_organization", EntityId = orgId },
            },
        };

        var matches = await _rung.EvaluateAsync(Envelope(CommunicationDirection.Outgoing), ctx, CancellationToken.None);

        matches.Should().HaveCount(2);
        matches.Should().Contain(m => m.RegardingFieldName == "sprk_regardingmatter"
            && m.Target!.LogicalName == "sprk_matter" && m.Target!.Id == matterId && m.Confidence == 1.0);
        matches.Should().Contain(m => m.RegardingFieldName == "sprk_regardingorganization"
            && m.Target!.LogicalName == "sprk_organization" && m.Target!.Id == orgId);
    }

    [Fact]
    public async Task Evaluate_SubjectMatterToken_ResolvesToMatter()
    {
        var matterId = Guid.NewGuid();
        _dv.Setup(d => d.QueryMatterByReferenceNumberAsync("12345", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DataverseEntity("sprk_matter") { Id = matterId });

        var matches = await _rung.EvaluateAsync(
            Envelope(CommunicationDirection.Incoming, "Re: MAT-12345 contract"), new AssociationContext(), CancellationToken.None);

        matches.Should().ContainSingle()
            .Which.Should().Match<RungMatch>(m =>
                m.RegardingFieldName == "sprk_regardingmatter" && m.Target!.Id == matterId
                && m.Rung == RungKind.ExplicitReference);
    }

    [Fact]
    public async Task Evaluate_CallerSupplied_WinsOverSubjectToken_SubjectNotParsed()
    {
        var callerMatterId = Guid.NewGuid();
        var ctx = new AssociationContext
        {
            CallerSuppliedRegarding = new[]
            {
                new CommunicationAssociation { EntityType = "sprk_matter", EntityId = callerMatterId },
            },
        };

        var matches = await _rung.EvaluateAsync(
            Envelope(CommunicationDirection.Outgoing, "Re: MAT-99999 different matter"), ctx, CancellationToken.None);

        matches.Should().ContainSingle().Which.Target!.Id.Should().Be(callerMatterId);
        _dv.Verify(d => d.QueryMatterByReferenceNumberAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Evaluate_NoCallerAndNoSubjectToken_ReturnsEmpty()
    {
        var matches = await _rung.EvaluateAsync(
            Envelope(CommunicationDirection.Incoming, "just a subject"), new AssociationContext(), CancellationToken.None);

        matches.Should().BeEmpty();
    }

    [Fact]
    public async Task Evaluate_DirectionSymmetry_IdenticalMatchesInboundAndOutbound()
    {
        var matterId = Guid.NewGuid();
        _dv.Setup(d => d.QueryMatterByReferenceNumberAsync("12345", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DataverseEntity("sprk_matter") { Id = matterId });

        var inbound = await _rung.EvaluateAsync(
            Envelope(CommunicationDirection.Incoming, "SPRK-12345"), new AssociationContext(), CancellationToken.None);
        var outbound = await _rung.EvaluateAsync(
            Envelope(CommunicationDirection.Outgoing, "SPRK-12345"), new AssociationContext(), CancellationToken.None);

        inbound.Should().BeEquivalentTo(outbound);
        inbound.Should().ContainSingle().Which.Target!.Id.Should().Be(matterId);
    }
}
