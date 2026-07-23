using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Xrm.Sdk;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Communication;
using Sprk.Bff.Api.Services.Communication.Engine;
using Sprk.Bff.Api.Services.Communication.Engine.Rungs;
using Sprk.Bff.Api.Services.Communication.Models;
using Xunit;
using DataverseEntity = Microsoft.Xrm.Sdk.Entity;

namespace Sprk.Bff.Api.Tests.Services.Communication;

/// <summary>
/// Direction-symmetry suite (task 017 / NFR) — THE load-bearing R4 invariant: the Association Engine
/// treats the normalized envelope uniformly, so an inbound and an outbound envelope with EQUIVALENT
/// content produce IDENTICAL regarding writes + status + provenance (the only permitted difference is
/// the recorded <c>direction</c> token in the provenance JSON). If any scenario were asymmetric it would
/// be a real bug in the engine/rungs (a rung branching on direction), not a test to weaken.
/// </summary>
public class AssociationDirectionSymmetryTests
{
    private static readonly Guid CommId = Guid.NewGuid();

    private static (IncomingAssociationResolver resolver, List<Dictionary<string, object>> writes)
        BuildResolver(Mock<IDataverseService> dv)
    {
        var captured = new List<Dictionary<string, object>>();
        dv.Setup(d => d.UpdateAsync("sprk_communication", CommId,
                It.IsAny<Dictionary<string, object>>(), It.IsAny<CancellationToken>()))
            .Callback<string, Guid, Dictionary<string, object>, CancellationToken>(
                (_, _, fields, _) => captured.Add(new Dictionary<string, object>(fields)))
            .Returns(Task.CompletedTask);

        var rungs = new IAssociationRung[]
        {
            new ExplicitReferenceRung(dv.Object),
            new ThreadContinuityRung(dv.Object),
            new ParticipantCorrelationRung(dv.Object),
        };
        var resolver = new IncomingAssociationResolver(
            rungs, dv.Object, dv.Object, AssociationTestSupport.Mapper(),
            Mock.Of<ILogger<IncomingAssociationResolver>>());
        return (resolver, captured);
    }

    private static NormalizedMessage Envelope(CommunicationDirection direction, string subject, string from, string? inReplyTo = null)
        => new() { Direction = direction, Subject = subject, From = from, InReplyTo = inReplyTo };

    /// <summary>
    /// Runs the same envelope content in both directions and asserts the regarding writes + status are
    /// identical, and the provenance differs only by the direction token.
    /// </summary>
    private static async Task AssertSymmetric(Action<Mock<IDataverseService>> arrange, string subject, string from, string? inReplyTo)
    {
        var dvIn = new Mock<IDataverseService>();
        arrange(dvIn);
        var (inResolver, inWrites) = BuildResolver(dvIn);
        await inResolver.ResolveAsync(CommId, Envelope(CommunicationDirection.Incoming, subject, from, inReplyTo), new AssociationContext(), CancellationToken.None);

        var dvOut = new Mock<IDataverseService>();
        arrange(dvOut);
        var (outResolver, outWrites) = BuildResolver(dvOut);
        await outResolver.ResolveAsync(CommId, Envelope(CommunicationDirection.Outgoing, subject, from, inReplyTo), new AssociationContext(), CancellationToken.None);

        inWrites.Should().ContainSingle();
        outWrites.Should().ContainSingle();

        var inFields = inWrites[0];
        var outFields = outWrites[0];

        // Status identical.
        ((OptionSetValue)inFields["sprk_associationstatus"]).Value
            .Should().Be(((OptionSetValue)outFields["sprk_associationstatus"]).Value);

        // Regarding writes identical (compare all keys except the direction-bearing provenance).
        var inRegarding = RegardingOnly(inFields);
        var outRegarding = RegardingOnly(outFields);
        inRegarding.Keys.Should().BeEquivalentTo(outRegarding.Keys);
        foreach (var key in inRegarding.Keys)
        {
            RefId(inRegarding[key]).Should().Be(RefId(outRegarding[key]), $"regarding field {key} must match across directions");
        }

        // Provenance differs ONLY by the direction token.
        var inProv = (string)inFields["sprk_associationprovenance"];
        var outProv = (string)outFields["sprk_associationprovenance"];
        inProv.Should().Contain("\"direction\":\"Incoming\"");
        outProv.Should().Contain("\"direction\":\"Outgoing\"");
        inProv.Replace("\"direction\":\"Incoming\"", "X")
            .Should().Be(outProv.Replace("\"direction\":\"Outgoing\"", "X"),
                "the engine must not branch on direction — provenance is identical apart from the direction token");
    }

    private static Dictionary<string, object> RegardingOnly(Dictionary<string, object> fields) =>
        fields.Where(kv => kv.Key.StartsWith("sprk_regarding") && fields[kv.Key] is EntityReference)
              .ToDictionary(kv => kv.Key, kv => kv.Value);

    private static Guid RefId(object v) => ((EntityReference)v).Id;

    [Fact]
    public async Task Engine_ThreadMatch_IsDirectionSymmetric()
    {
        var matterId = Guid.NewGuid();
        await AssertSymmetric(dv =>
        {
            var parent = new DataverseEntity("sprk_communication");
            parent["sprk_regardingmatter"] = new EntityReference("sprk_matter", matterId);
            dv.Setup(d => d.GetCommunicationByGraphMessageIdAsync("<p@x.com>", It.IsAny<CancellationToken>()))
                .ReturnsAsync(parent);
        }, subject: "Re: Deal", from: "jane@external.com", inReplyTo: "<p@x.com>");
    }

    [Fact]
    public async Task Engine_SubjectTokenMatch_IsDirectionSymmetric()
    {
        var matterId = Guid.NewGuid();
        await AssertSymmetric(dv =>
        {
            dv.Setup(d => d.QueryContactByEmailAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync((DataverseEntity?)null);
            dv.Setup(d => d.QueryOrganizationByDomainAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync((DataverseEntity?)null);
            dv.Setup(d => d.QueryAccountByDomainAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync((DataverseEntity?)null);
            dv.Setup(d => d.QueryMatterByReferenceNumberAsync("12345", It.IsAny<CancellationToken>()))
                .ReturnsAsync(new DataverseEntity("sprk_matter") { Id = matterId });
        }, subject: "Update on MAT-12345", from: "unknown@external.com", inReplyTo: null);
    }

    [Fact]
    public async Task Engine_ParticipantContactMatch_IsDirectionSymmetric()
    {
        var contactId = Guid.NewGuid();
        await AssertSymmetric(dv =>
        {
            dv.Setup(d => d.QueryContactByEmailAsync("jane@acme.com", It.IsAny<CancellationToken>()))
                .ReturnsAsync(new DataverseEntity("contact") { Id = contactId });
            dv.Setup(d => d.QueryOrganizationByDomainAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync((DataverseEntity?)null);
            dv.Setup(d => d.QueryAccountByDomainAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync((DataverseEntity?)null);
        }, subject: "Question", from: "jane@acme.com", inReplyTo: null);
    }

    [Fact]
    public async Task Engine_NoMatch_IsDirectionSymmetric()
    {
        await AssertSymmetric(dv =>
        {
            dv.Setup(d => d.QueryContactByEmailAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync((DataverseEntity?)null);
            dv.Setup(d => d.QueryOrganizationByDomainAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync((DataverseEntity?)null);
            dv.Setup(d => d.QueryAccountByDomainAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync((DataverseEntity?)null);
        }, subject: "Random noise", from: "nobody@gmail.com", inReplyTo: null);
    }
}
