using FluentAssertions;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Infrastructure.Exceptions;
using Sprk.Bff.Api.Services.Communication;
using Sprk.Bff.Api.Services.Communication.Channels;
using Sprk.Bff.Api.Services.Communication.Engine;
using Sprk.Bff.Api.Services.Communication.Engine.Rungs;
using Sprk.Bff.Api.Services.Communication.Models;
using Xunit;
using DataverseEntity = Microsoft.Xrm.Sdk.Entity;

namespace Sprk.Bff.Api.Tests.Services.Communication;

/// <summary>
/// Behavior of the on-demand Association Engine suggestion path (task 074, Path C — UI-agnostic BFF
/// capability). Three protected contracts: (a) the evaluate-only engine path returns a decision WITHOUT
/// writing to Dataverse; (b) reconstruction of a missing communication maps to a 404 problem; (c) the
/// end-to-end suggest flow (stored record → envelope reconstruction → evaluate → projection) surfaces the
/// engine's candidates without writing. Mocks at the Dataverse boundary only (ADR-038 KEEP-path shape).
/// </summary>
public class SuggestAssociationsTests
{
    // ─────────────────────────────────────────────────────────────────────────
    // Construction helpers (mirror CommunicationServiceArchiveTests.BuildSut — the
    // reconstruction path only touches RetrieveAsync + SplitRecipients, so the
    // send-side collaborators are not reached and pass as null/mocks).
    // ─────────────────────────────────────────────────────────────────────────

    private static CommunicationOptions Options() => new()
    {
        ApprovedSenders = new[]
        {
            new ApprovedSenderConfig { Email = "noreply@contoso.com", DisplayName = "Contoso", IsDefault = true }
        },
        DefaultMailbox = "noreply@contoso.com",
        ArchiveContainerId = "drive-1"
    };

    private static CommunicationService BuildService(IGenericEntityService entityService)
    {
        var options = Options();
        var accountService = new CommunicationAccountService(
            Mock.Of<IDataverseService>(),
            Mock.Of<IDataverseService>(),
            Mock.Of<IDistributedCache>(),
            Mock.Of<ILogger<CommunicationAccountService>>());
        var senderValidator = new ApprovedSenderValidator(
            Microsoft.Extensions.Options.Options.Create(options),
            accountService,
            Mock.Of<IDistributedCache>(),
            Mock.Of<ILogger<ApprovedSenderValidator>>());
        var dispatcher = new CommunicationChannelDispatcher(
            Array.Empty<ICommunicationChannelSender>(),
            new ICommunicationArchiver[] { new EmailArchiver(new EmlGenerationService(Mock.Of<ILogger<EmlGenerationService>>())) });

        return new CommunicationService(
            dispatcher,
            senderValidator,
            Mock.Of<ICommunicationDataverseService>(),
            entityService,
            Mock.Of<IDocumentDataverseService>(),
            null!, // SpeFileStore — not reached on the reconstruction path
            null!, // CommunicationAccountService — not reached
            null!, // JobSubmissionService — not reached
            Mock.Of<ICommunicationEnrichmentService>(),
            Microsoft.Extensions.Options.Options.Create(options),
            Mock.Of<ILogger<CommunicationService>>());
    }

    private static IncomingAssociationResolver BuildResolver(IDataverseService dv) =>
        new(
            new IAssociationRung[]
            {
                new ExplicitReferenceRung(dv),
                new ThreadContinuityRung(dv),
                new ParticipantCorrelationRung(dv),
            },
            dv,
            dv,
            AssociationTestSupport.Mapper(),
            NullLogger<IncomingAssociationResolver>.Instance);

    // =========================================================================
    // (a) EvaluateAsync returns a decision WITHOUT writing
    // =========================================================================

    [Fact]
    public async Task EvaluateAsync_WithThreadMatch_ReturnsResolvedDecisionAndNeverWrites()
    {
        // Arrange — envelope carries an In-Reply-To parent that is already filed to a matter.
        var parentMatterId = Guid.NewGuid();
        var parentComm = new DataverseEntity("sprk_communication");
        parentComm["sprk_regardingmatter"] = new EntityReference("sprk_matter", parentMatterId);

        var dv = new Mock<IDataverseService>();
        dv.Setup(d => d.GetCommunicationByInternetMessageIdAsync("<parent@contoso.com>", It.IsAny<CancellationToken>()))
            .ReturnsAsync(parentComm);

        var resolver = BuildResolver(dv.Object);
        var envelope = new NormalizedMessage
        {
            Direction = CommunicationDirection.Incoming,
            Subject = "Re: Contract review",
            From = "jane@external.com",
            InReplyTo = "<parent@contoso.com>",
        };

        // Act
        var decision = await resolver.EvaluateAsync(envelope, new AssociationContext(), CancellationToken.None);

        // Assert — engine would auto-file the parent's matter, but nothing was written.
        decision.Status.Should().Be(AssociationStatusCodes.Resolved);
        decision.AutoFiled.Should().BeTrue();
        decision.Provenance.Candidates.Should().Contain(c =>
            c.Field == "sprk_regardingmatter" && c.Written);

        dv.Verify(d => d.UpdateAsync(
                "sprk_communication", It.IsAny<Guid>(),
                It.IsAny<Dictionary<string, object>>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // =========================================================================
    // (b) Missing communication → 404 problem
    // =========================================================================

    [Fact]
    public async Task ReconstructEnvelopeAsync_WhenCommunicationNotFound_ThrowsProblem404()
    {
        // Arrange — RetrieveAsync throws (record does not exist).
        var communicationId = Guid.NewGuid();
        var entityService = new Mock<IGenericEntityService>();
        entityService
            .Setup(s => s.RetrieveAsync("sprk_communication", communicationId, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("not found"));

        var service = BuildService(entityService.Object);

        // Act
        var act = () => service.ReconstructEnvelopeAsync(communicationId, CancellationToken.None);

        // Assert — mapped to a 404 problem (mirrors the archive/status not-found contract).
        var ex = await act.Should().ThrowAsync<SdapProblemException>();
        ex.Which.StatusCode.Should().Be(404);
        ex.Which.Code.Should().Be("COMMUNICATION_NOT_FOUND");
    }

    // =========================================================================
    // (c) End-to-end: stored record → reconstruct → evaluate → project
    // =========================================================================

    [Fact]
    public async Task SuggestAssociations_ForStoredThreadReply_ReturnsProjectedCandidatesWithoutWriting()
    {
        // Arrange — a stored inbound reply whose In-Reply-To points at a filed parent.
        var communicationId = Guid.NewGuid();
        var parentMatterId = Guid.NewGuid();

        var dv = new Mock<IDataverseService>();

        var stored = new DataverseEntity("sprk_communication", communicationId);
        stored["sprk_subject"] = "Re: Contract review";
        stored["sprk_from"] = "jane@external.com";
        stored["sprk_to"] = "intake@contoso.com";
        stored["sprk_direction"] = new OptionSetValue((int)CommunicationDirection.Incoming);
        stored["sprk_inreplyto"] = "<parent@contoso.com>";
        dv.Setup(d => d.RetrieveAsync("sprk_communication", communicationId, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(stored);

        var parentComm = new DataverseEntity("sprk_communication");
        parentComm["sprk_regardingmatter"] = new EntityReference("sprk_matter", parentMatterId);
        dv.Setup(d => d.GetCommunicationByInternetMessageIdAsync("<parent@contoso.com>", It.IsAny<CancellationToken>()))
            .ReturnsAsync(parentComm);

        var service = BuildService(dv.Object);
        var resolver = BuildResolver(dv.Object);

        // Act — the exact endpoint composition: reconstruct → evaluate → project.
        var (message, context) = await service.ReconstructEnvelopeAsync(communicationId, CancellationToken.None);
        var decision = await resolver.EvaluateAsync(message, context, CancellationToken.None);
        var response = SuggestAssociationsResponse.FromDecision(communicationId, decision);

        // Assert — envelope reconstructed, matter surfaced, projected onto the wire contract, no write.
        message.InReplyTo.Should().Be("<parent@contoso.com>");
        message.From.Should().Be("jane@external.com");

        response.CommunicationId.Should().Be(communicationId);
        response.Status.Should().Be("Resolved");
        response.AutoFileEligible.Should().BeTrue();
        response.Candidates.Should().Contain(c =>
            c.Field == "sprk_regardingmatter" &&
            c.TargetEntity == "sprk_matter" &&
            c.Written);
        Guid.Parse(response.Candidates.First(c => c.Field == "sprk_regardingmatter").TargetId)
            .Should().Be(parentMatterId);

        dv.Verify(d => d.UpdateAsync(
                "sprk_communication", It.IsAny<Guid>(),
                It.IsAny<Dictionary<string, object>>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
