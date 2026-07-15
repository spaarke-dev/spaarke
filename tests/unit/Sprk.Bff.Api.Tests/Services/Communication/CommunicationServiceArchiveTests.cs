using FluentAssertions;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Infrastructure.Exceptions;
using Sprk.Bff.Api.Services.Communication;
using Sprk.Bff.Api.Services.Communication.Channels;
using Sprk.Bff.Api.Services.Communication.Models;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Communication;

/// <summary>
/// Behavior of <see cref="CommunicationService.ArchiveExistingAsync"/> — the on-demand "Save to
/// SharePoint" path (task 044). Two protected contracts: idempotency (a communication already archived
/// returns AlreadyArchived without re-creating Documents) and not-found mapping (missing record →
/// SdapProblemException 404). The happy-path .eml generation is the same reused ArchiveToSpeAsync the
/// send-side tests already exercise, so it is not re-asserted here.
/// </summary>
public class CommunicationServiceArchiveTests
{
    private static CommunicationOptions Options() => new()
    {
        ApprovedSenders = new[]
        {
            new ApprovedSenderConfig { Email = "noreply@contoso.com", DisplayName = "Contoso", IsDefault = true }
        },
        DefaultMailbox = "noreply@contoso.com",
        ArchiveContainerId = "drive-1"
    };

    private static CommunicationService BuildSut(IGenericEntityService entityService)
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
            null!, // SpeFileStore — not reached on the idempotent / not-found paths
            null!, // CommunicationAccountService — not reached
            null!, // JobSubmissionService — not reached
            Mock.Of<ICommunicationEnrichmentService>(),
            Microsoft.Extensions.Options.Options.Create(options),
            Mock.Of<ILogger<CommunicationService>>());
    }

    [Fact]
    public async Task ArchiveExistingAsync_WhenAlreadyArchived_ReturnsAlreadyArchivedWithoutCreatingDocuments()
    {
        // Arrange — the communication exists AND an email-archive Document already exists for it.
        var communicationId = Guid.NewGuid();
        var existingArchiveId = Guid.NewGuid();

        var entityService = new Mock<IGenericEntityService>();
        entityService
            .Setup(s => s.RetrieveAsync("sprk_communication", communicationId, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Entity("sprk_communication", communicationId));
        // No attachments to archive.
        entityService
            .Setup(s => s.RetrieveMultipleAsync(It.Is<QueryExpression>(q => q.EntityName == "sprk_communicationattachment"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EntityCollection(new List<Entity>()));
        // An email-archive Document already exists.
        entityService
            .Setup(s => s.RetrieveMultipleAsync(It.Is<QueryExpression>(q => q.EntityName == "sprk_document"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EntityCollection(new List<Entity> { new("sprk_document", existingArchiveId) }));

        var sut = BuildSut(entityService.Object);

        // Act
        var result = await sut.ArchiveExistingAsync(communicationId, CancellationToken.None);

        // Assert — idempotent: reports the existing archive, creates nothing new.
        result.AlreadyArchived.Should().BeTrue();
        result.ArchiveDocumentId.Should().Be(existingArchiveId);
        result.AttachmentDocumentsCreated.Should().Be(0);
        entityService.Verify(s => s.CreateAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ArchiveExistingAsync_WhenCommunicationNotFound_ThrowsProblem404()
    {
        // Arrange — RetrieveAsync throws (record does not exist).
        var communicationId = Guid.NewGuid();
        var entityService = new Mock<IGenericEntityService>();
        entityService
            .Setup(s => s.RetrieveAsync("sprk_communication", communicationId, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("not found"));

        var sut = BuildSut(entityService.Object);

        // Act
        var act = () => sut.ArchiveExistingAsync(communicationId, CancellationToken.None);

        // Assert — mapped to a 404 problem, no archival attempted.
        var ex = await act.Should().ThrowAsync<SdapProblemException>();
        ex.Which.StatusCode.Should().Be(404);
        ex.Which.Code.Should().Be("COMMUNICATION_NOT_FOUND");
    }
}
