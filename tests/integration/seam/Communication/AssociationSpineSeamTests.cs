using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Microsoft.Xrm.Sdk;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Services.Communication;
using Sprk.Bff.Api.Services.Communication.Engine;
using Sprk.Bff.Api.Services.Communication.Engine.Rungs;
using Sprk.Bff.Api.Services.Communication.Models;
using Xunit;
using DataverseEntity = Microsoft.Xrm.Sdk.Entity;

namespace Sprk.Bff.Api.Tests.Seam.Communication;

/// <summary>
/// Vertical-slice seam (ADR-038, task 017) for the association dispatch spine:
/// rungs → <see cref="AssociationStatusMapper"/> → <see cref="AutoFileGate"/> →
/// <see cref="IncomingAssociationResolver"/>. Wires the REAL decision composition with production types
/// (real rungs, real mapper, real gate reading real <see cref="AutoFileOptions"/> via a real
/// <see cref="IOptionsMonitor{TOptions}"/>) and fakes only the Dataverse boundary. Proves the FR-11
/// status + provenance write is produced end-to-end — a router-unit test alone would not exercise the
/// real wiring.
/// </summary>
public sealed class AssociationSpineSeamTests
{
    private static readonly Guid CommId = Guid.NewGuid();

    private static IncomingAssociationResolver BuildRealSpine(
        Mock<IDataverseService> dv, bool autoFileEnabled, double threshold,
        out List<Dictionary<string, object>> writes)
    {
        var captured = new List<Dictionary<string, object>>();
        writes = captured;
        dv.Setup(d => d.UpdateAsync("sprk_communication", CommId,
                It.IsAny<Dictionary<string, object>>(), It.IsAny<CancellationToken>()))
            .Callback<string, Guid, Dictionary<string, object>, CancellationToken>(
                (_, _, f, _) => captured.Add(new Dictionary<string, object>(f)))
            .Returns(Task.CompletedTask);

        var options = new AutoFileOptions { Enabled = autoFileEnabled, Threshold = threshold };
        var monitor = Mock.Of<IOptionsMonitor<AutoFileOptions>>(m => m.CurrentValue == options);
        var mapper = new AssociationStatusMapper(new AutoFileGate(monitor), NullLogger<AssociationStatusMapper>.Instance);

        var rungs = new IAssociationRung[]
        {
            new ExplicitReferenceRung(dv.Object),
            new ThreadContinuityRung(dv.Object),
            new ParticipantCorrelationRung(dv.Object),
        };
        return new IncomingAssociationResolver(rungs, dv.Object, dv.Object, mapper, NullLogger<IncomingAssociationResolver>.Instance);
    }

    private static NormalizedMessage ThreadEnvelope(string parentMsgId)
        => new() { Direction = CommunicationDirection.Incoming, Subject = "Re: Deal", From = "jane@external.com", InReplyTo = parentMsgId };

    private static void SetupThreadParent(Mock<IDataverseService> dv, string parentMsgId, Guid matterId)
    {
        var parent = new DataverseEntity("sprk_communication");
        parent["sprk_regardingmatter"] = new EntityReference("sprk_matter", matterId);
        dv.Setup(d => d.GetCommunicationByGraphMessageIdAsync(parentMsgId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(parent);
    }

    [Fact]
    public async Task Spine_ThreadMatch_KillSwitchOn_AutoFilesResolvedAndPersistsProvenance()
    {
        var dv = new Mock<IDataverseService>();
        var matterId = Guid.NewGuid();
        SetupThreadParent(dv, "<p@x.com>", matterId);
        var resolver = BuildRealSpine(dv, autoFileEnabled: true, threshold: 0.85, out var writes);

        await resolver.ResolveAsync(CommId, ThreadEnvelope("<p@x.com>"), new AssociationContext(), CancellationToken.None);

        var fields = writes.Should().ContainSingle().Subject;
        ((OptionSetValue)fields["sprk_associationstatus"]).Value.Should().Be(AssociationStatusCodes.Resolved);
        ((EntityReference)fields["sprk_regardingmatter"]).Id.Should().Be(matterId);

        var prov = JsonDocument.Parse((string)fields["sprk_associationprovenance"]).RootElement;
        prov.GetProperty("decision").GetProperty("autoFiled").GetBoolean().Should().BeTrue();
        prov.GetProperty("decision").GetProperty("status").GetString().Should().Be("Resolved");
        prov.GetProperty("rungsFired").EnumerateArray().Select(e => e.GetString())
            .Should().Contain("ThreadContinuity");
    }

    [Fact]
    public async Task Spine_ThreadMatch_KillSwitchOff_DowngradesToSuggested()
    {
        var dv = new Mock<IDataverseService>();
        var matterId = Guid.NewGuid();
        SetupThreadParent(dv, "<p2@x.com>", matterId);
        var resolver = BuildRealSpine(dv, autoFileEnabled: false, threshold: 0.85, out var writes);

        await resolver.ResolveAsync(CommId, ThreadEnvelope("<p2@x.com>"), new AssociationContext(), CancellationToken.None);

        var fields = writes.Should().ContainSingle().Subject;
        ((OptionSetValue)fields["sprk_associationstatus"]).Value.Should().Be(AssociationStatusCodes.Suggested);
        ((EntityReference)fields["sprk_regardingmatter"]).Id.Should().Be(matterId);

        var prov = JsonDocument.Parse((string)fields["sprk_associationprovenance"]).RootElement;
        prov.GetProperty("decision").GetProperty("autoFiled").GetBoolean().Should().BeFalse();
        prov.GetProperty("decision").GetProperty("killSwitchEnabled").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task Spine_NoMatch_PersistsPendingReviewWithNoRegarding()
    {
        var dv = new Mock<IDataverseService>();
        dv.Setup(d => d.QueryContactByEmailAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync((DataverseEntity?)null);
        dv.Setup(d => d.QueryOrganizationByDomainAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync((DataverseEntity?)null);
        dv.Setup(d => d.QueryAccountByDomainAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync((DataverseEntity?)null);
        var resolver = BuildRealSpine(dv, autoFileEnabled: true, threshold: 0.85, out var writes);

        var envelope = new NormalizedMessage { Direction = CommunicationDirection.Incoming, Subject = "no patterns here", From = "nobody@gmail.com" };
        await resolver.ResolveAsync(CommId, envelope, new AssociationContext(), CancellationToken.None);

        var fields = writes.Should().ContainSingle().Subject;
        ((OptionSetValue)fields["sprk_associationstatus"]).Value.Should().Be(AssociationStatusCodes.PendingReview);
        fields.Keys.Should().NotContain(k => k.StartsWith("sprk_regarding") && fields[k] is EntityReference);
    }
}
