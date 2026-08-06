using FluentAssertions;
using Microsoft.Xrm.Sdk;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Communication.Engine;
using Sprk.Bff.Api.Services.Communication.Engine.Rungs;
using Sprk.Bff.Api.Services.Communication.Models;
using Xunit;
using DataverseEntity = Microsoft.Xrm.Sdk.Entity;

namespace Sprk.Bff.Api.Tests.Seam.Communication;

/// <summary>
/// FR-A3 first-class guarantee (email-communication-intelligence-r2 task 015), ADR-038
/// <c>tests/integration/seam/Communication/**</c> KEEP path — the golden-regression home the D3 suite
/// (task 032) will absorb. <b>Guarantee</b>: an EXTERNAL reply to any email Spaarke sent self-associates
/// back to the parent's regarding via <see cref="ThreadContinuityRung"/> on the standard RFC-2822
/// <c>In-Reply-To</c> / <c>References</c> ancestry — <b>even when every Spaarke-proprietary header
/// (X-Spaarke-*) has been stripped</b> by the external mail system. The rung reads ONLY the normalized
/// envelope's threading headers (never a custom header), so the association survives header stripping by
/// construction; these regressions pin that contract.
///
/// <para>This complements the rung's mechanics unit tests (<c>ThreadContinuityRungTests</c>) by pinning the
/// end-to-end FR-A3 <i>guarantee</i> (external-reply + stripped-headers framing) as a CI-guarded regression.
/// No production rung code is exercised beyond the real <see cref="ThreadContinuityRung.EvaluateAsync"/>;
/// only the module boundary <see cref="ICommunicationDataverseService"/> is doubled.</para>
/// </summary>
public sealed class ThreadSelfAssociationRegressionTests
{
    // Resolved (auto-filed / human-confirmed) — a reply legitimately inherits from a trustworthy parent at
    // full auto-file strength (P3). Raw option value mirrors ThreadContinuityRungTests' proven convention.
    private const int ResolvedStatus = 100000000;

    private readonly Mock<ICommunicationDataverseService> _dv = new();
    private readonly ThreadContinuityRung _rung;

    public ThreadSelfAssociationRegressionTests() => _rung = new ThreadContinuityRung(_dv.Object);

    /// <summary>A parent sprk_communication Spaarke previously sent + filed onto matter M (Resolved).</summary>
    private static DataverseEntity ResolvedParentRegardingMatter(Guid matterId)
    {
        var parent = new DataverseEntity("sprk_communication") { Id = Guid.NewGuid() };
        parent["sprk_associationstatus"] = new OptionSetValue(ResolvedStatus);
        parent["sprk_regardingmatter"] = new EntityReference("sprk_matter", matterId);
        return parent;
    }

    /// <summary>
    /// An inbound external reply carrying ONLY standard RFC-2822 threading headers — no X-Spaarke-* custom
    /// headers (they were stripped in transit). NormalizedMessage models exactly the envelope the rung reads.
    /// </summary>
    private static NormalizedMessage ExternalReply(string? inReplyTo = null, params string[] references) =>
        new()
        {
            Direction = CommunicationDirection.Incoming,
            From = "opposing.counsel@external-firm.com",
            To = new[] { "matter-team@spaarke-client.com" },
            Subject = "RE: settlement terms",
            InReplyTo = inReplyTo,
            References = references,
            // No custom headers: NormalizedMessage carries only standard fields, and the rung reads only
            // InReplyTo/References — proving self-association needs no Spaarke-proprietary header.
        };

    [Fact]
    public async Task ExternalReply_InReplyToSpaarkeSentParent_StrippedCustomHeaders_InheritsMatterAtAutoFile()
    {
        var matterId = Guid.NewGuid();
        var parentMessageId = "<spaarke-sent-parent@spaarke-client.com>";
        _dv.Setup(d => d.GetCommunicationByInternetMessageIdAsync(parentMessageId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ResolvedParentRegardingMatter(matterId));

        var matches = await _rung.EvaluateAsync(
            ExternalReply(inReplyTo: parentMessageId), new AssociationContext(), CancellationToken.None);

        var match = matches.Should().ContainSingle(
            "the external reply self-associates to the parent's matter via In-Reply-To alone").Subject;
        match.RegardingFieldName.Should().Be("sprk_regardingmatter");
        match.Target!.LogicalName.Should().Be("sprk_matter");
        match.Target!.Id.Should().Be(matterId);
        match.Rung.Should().Be(RungKind.ThreadContinuity);
        match.Confidence.Should().Be(1.0, "inheritance from a Resolved parent auto-files (P3)");
    }

    [Fact]
    public async Task ExternalReply_ReferencesChainOnly_NoInReplyTo_StrippedCustomHeaders_InheritsMatter()
    {
        // Some external mail systems drop In-Reply-To but keep References. The guarantee must still hold via
        // the References ancestry (nearest ancestor = last entry), with no custom headers.
        var matterId = Guid.NewGuid();
        var parentMessageId = "<spaarke-sent-parent@spaarke-client.com>";
        _dv.Setup(d => d.GetCommunicationByInternetMessageIdAsync(parentMessageId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ResolvedParentRegardingMatter(matterId));

        var matches = await _rung.EvaluateAsync(
            ExternalReply(references: new[] { "<thread-root@external-firm.com>", parentMessageId }),
            new AssociationContext(), CancellationToken.None);

        matches.Should().ContainSingle(
            "self-association survives on the References chain alone when In-Reply-To is absent")
            .Which.Target!.Id.Should().Be(matterId);
    }
}
