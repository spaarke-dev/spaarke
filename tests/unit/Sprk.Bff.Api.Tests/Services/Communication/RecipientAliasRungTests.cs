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
/// Rung 0-tier recipient-alias tests (FR-A2): a per-record intake address (matter-{ref}@) in any recipient
/// field resolves deterministically to its matter as an explicit-reference-tier match. The KEY case is
/// Bcc-only delivery (the common mail-flow-rule pattern). Malformed / absent aliases must never misfire.
/// Reads only the normalized envelope (no Graph round-trip), so inbound == outbound.
/// </summary>
public class RecipientAliasRungTests
{
    private readonly Mock<ICommunicationDataverseService> _dv = new();
    private readonly RecipientAliasRung _rung;

    public RecipientAliasRungTests() => _rung = new RecipientAliasRung(_dv.Object);

    private void MatterResolves(string referenceNumber, Guid matterId) =>
        _dv.Setup(d => d.QueryMatterByReferenceNumberAsync(referenceNumber, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DataverseEntity("sprk_matter") { Id = matterId });

    private static NormalizedMessage Envelope(
        CommunicationDirection direction = CommunicationDirection.Incoming,
        string[]? to = null, string[]? cc = null, string[]? bcc = null) =>
        new()
        {
            Direction = direction,
            To = to ?? Array.Empty<string>(),
            Cc = cc ?? Array.Empty<string>(),
            Bcc = bcc ?? Array.Empty<string>(),
        };

    [Fact]
    public async Task Evaluate_ToAlias_EmitsExplicitReferenceTierMatchToMatter()
    {
        var matterId = Guid.NewGuid();
        MatterResolves("12345", matterId);

        var matches = await _rung.EvaluateAsync(
            Envelope(to: new[] { "matter-12345@intake.example.com" }), new AssociationContext(), CancellationToken.None);

        matches.Should().ContainSingle().Which.Should().Match<RungMatch>(m =>
            m.RegardingFieldName == "sprk_regardingmatter"
            && m.Target!.LogicalName == "sprk_matter" && m.Target!.Id == matterId
            && m.Rung == RungKind.RecipientAlias
            && m.Confidence == 1.0);
    }

    [Fact]
    public async Task Evaluate_CcAlias_EmitsMatch()
    {
        var matterId = Guid.NewGuid();
        MatterResolves("777", matterId);

        var matches = await _rung.EvaluateAsync(
            Envelope(cc: new[] { "matter-777@filing.example.com" }), new AssociationContext(), CancellationToken.None);

        matches.Should().ContainSingle().Which.Target!.Id.Should().Be(matterId);
    }

    [Fact]
    public async Task Evaluate_BccOnlyAlias_AssociatesDeterministically()
    {
        // KEY acceptance: the mail-flow rule Bcc's the per-record alias, so a Bcc-ONLY delivery
        // (nothing in To/Cc) must still associate.
        var matterId = Guid.NewGuid();
        MatterResolves("12345", matterId);

        var matches = await _rung.EvaluateAsync(
            Envelope(
                to: new[] { "client@acme.com" },
                bcc: new[] { "matter-12345@intake.example.com" }),
            new AssociationContext(), CancellationToken.None);

        matches.Should().ContainSingle().Which.Should().Match<RungMatch>(m =>
            m.Target!.Id == matterId && m.Rung == RungKind.RecipientAlias && m.Confidence == 1.0);
    }

    [Fact]
    public async Task Evaluate_MatReferenceForm_ResolvesViaFallback()
    {
        // The matter number may be stored as "MAT-88" while the alias carries the bare ref; the rung tries
        // the bare ref then the MAT- form (mirrors ExplicitReferenceRung).
        var matterId = Guid.NewGuid();
        MatterResolves("MAT-88", matterId);

        var matches = await _rung.EvaluateAsync(
            Envelope(bcc: new[] { "matter-88@intake.example.com" }), new AssociationContext(), CancellationToken.None);

        matches.Should().ContainSingle().Which.Target!.Id.Should().Be(matterId);
    }

    [Fact]
    public async Task Evaluate_SameMatterAliasedInMultipleFields_CollapsesToOneMatch()
    {
        var matterId = Guid.NewGuid();
        MatterResolves("12345", matterId);

        var matches = await _rung.EvaluateAsync(
            Envelope(
                to: new[] { "matter-12345@intake.example.com" },
                bcc: new[] { "matter-12345@filing.example.com" }),
            new AssociationContext(), CancellationToken.None);

        matches.Should().ContainSingle().Which.Target!.Id.Should().Be(matterId);
    }

    [Fact]
    public async Task Evaluate_TwoDistinctMatterAliases_EmitsBothForConflictResolution()
    {
        // Two different matter aliases → two matches on the same field; the mapper (not the rung) surfaces
        // this as Ambiguous. The rung never guesses which is primary.
        var matterA = Guid.NewGuid();
        var matterB = Guid.NewGuid();
        MatterResolves("111", matterA);
        MatterResolves("222", matterB);

        var matches = await _rung.EvaluateAsync(
            Envelope(
                to: new[] { "matter-111@intake.example.com" },
                cc: new[] { "matter-222@intake.example.com" }),
            new AssociationContext(), CancellationToken.None);

        matches.Should().HaveCount(2);
        matches.Select(m => m.Target!.Id).Should().BeEquivalentTo(new[] { matterA, matterB });
        matches.Should().OnlyContain(m => m.RegardingFieldName == "sprk_regardingmatter");
    }

    [Fact]
    public async Task Evaluate_NoAliasInAnyField_ReturnsEmpty()
    {
        var matches = await _rung.EvaluateAsync(
            Envelope(
                to: new[] { "client@acme.com" },
                cc: new[] { "counsel@firm.com" },
                bcc: new[] { "assistant@firm.com" }),
            new AssociationContext(), CancellationToken.None);

        matches.Should().BeEmpty();
        _dv.Verify(d => d.QueryMatterByReferenceNumberAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData("matter-@intake.example.com")]   // empty reference token
    [InlineData("notmatter-12345@intake.example.com")] // not a matter- prefix
    [InlineData("matterreport@intake.example.com")]    // no dash after matter
    [InlineData("re: matter-12345")]                   // not an address (no @ after the token)
    public async Task Evaluate_MalformedAlias_ReturnsEmpty(string recipient)
    {
        var matches = await _rung.EvaluateAsync(
            Envelope(to: new[] { recipient }), new AssociationContext(), CancellationToken.None);

        matches.Should().BeEmpty();
    }

    [Fact]
    public async Task Evaluate_AliasResolvesToNoMatter_ReturnsEmpty()
    {
        // Well-formed alias, but no matching matter — no misfire (the query returns null for both forms).
        var matches = await _rung.EvaluateAsync(
            Envelope(bcc: new[] { "matter-99999@intake.example.com" }), new AssociationContext(), CancellationToken.None);

        matches.Should().BeEmpty();
    }

    [Fact]
    public async Task Evaluate_DirectionSymmetry_IdenticalInboundAndOutbound()
    {
        var matterId = Guid.NewGuid();
        MatterResolves("12345", matterId);

        var inbound = await _rung.EvaluateAsync(
            Envelope(CommunicationDirection.Incoming, bcc: new[] { "matter-12345@intake.example.com" }),
            new AssociationContext(), CancellationToken.None);
        var outbound = await _rung.EvaluateAsync(
            Envelope(CommunicationDirection.Outgoing, bcc: new[] { "matter-12345@intake.example.com" }),
            new AssociationContext(), CancellationToken.None);

        inbound.Should().BeEquivalentTo(outbound);
        inbound.Should().ContainSingle().Which.Target!.Id.Should().Be(matterId);
    }
}
