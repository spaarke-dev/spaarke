using System.Net;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Graph;
using Microsoft.Xrm.Sdk;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Services.Communication;
using Sprk.Bff.Api.Services.Communication.Models;
using Sprk.Bff.Api.Tests.Services.Communication;
using Sprk.Bff.Api.Tests.TestInfrastructure;

namespace Sprk.Bff.Api.Tests.AccessControl;

/// <summary>
/// unified-access-control-r2 task 155 f5 — the <c>sprk_communication</c> row the OUTBOUND sender ACTUALLY writes.
/// </summary>
/// <remarks>
/// <para>Runs the REAL <see cref="CommunicationService.SendAsync"/> (and so the real
/// <c>MapAssociationFieldsAsync</c>) with only Dataverse and Graph doubled, and returns the entity it handed to
/// <c>CreateAsync</c>. The container-resolution tests then resolve THAT row, so the shape they defend is the writer's,
/// not a hand-built approximation of it: the f4 round refused every email regarding a person, organization or account
/// because a hand-built row carried a pair TYPE the writer never writes.</para>
///
/// <para>The writer's collaborators are built the way <c>AssociationMappingTests</c> builds them (the existing
/// writer-shape suite). Graph answers 202 through a hand-written handler — a fake transport, not a
/// <c>Mock&lt;HttpMessageHandler&gt;</c>.</para>
/// </remarks>
internal static class OutboundCommunicationRow
{
    private const string Mailbox = "noreply@contoso.com";

    /// <summary>Send one outbound email whose primary association is (<paramref name="entityType"/>, <paramref name="entityId"/>).</summary>
    public static Task<Entity> WriteAsync(string entityType, Guid entityId) =>
        WriteAsync(new SendCommunicationRequest
        {
            To = ["recipient@example.com"],
            Subject = "Task 155 f5",
            Body = "<p>body</p>",
            BodyFormat = BodyFormat.HTML,
            CommunicationType = CommunicationType.Email,
            Associations = [new CommunicationAssociation { EntityType = entityType, EntityId = entityId, EntityName = "Primary" }],
            CorrelationId = "task-155-f5",
        });

    /// <summary>
    /// Ontology task 168 fix round (review findings 2 and 4): send <paramref name="request"/> exactly as a caller built it, through the
    /// REAL shared-mailbox path, and return the row it wrote. The request's own <c>FromMailbox</c> is the approved sender (or the
    /// default here); <paramref name="sender"/> is the send's HttpContext and <paramref name="dataverse"/> answers the oid -> systemuser
    /// read behind <c>sprk_sentby</c>.
    /// </summary>
    public static async Task<Entity> WriteAsync(
        SendCommunicationRequest request,
        Microsoft.AspNetCore.Http.HttpContext? sender = null,
        ICommunicationDataverseService? dataverse = null)
    {
        Entity? written = null;
        var mailbox = request.FromMailbox ?? Mailbox;

        var entityService = new Mock<IGenericEntityService>();
        entityService
            .Setup(s => s.CreateAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>()))
            .Callback<Entity, CancellationToken>((entity, _) =>
            {
                if (entity.LogicalName == "sprk_communication")
                {
                    written = entity;
                }
            })
            .ReturnsAsync(Guid.NewGuid());

        var graph = new Mock<IGraphClientFactory>();
        graph.Setup(f => f.ForApp()).Returns(new GraphServiceClient(new HttpClient(new AcceptedHandler())));

        var options = new CommunicationOptions
        {
            ApprovedSenders = [new ApprovedSenderConfig { Email = mailbox, DisplayName = "Contoso", IsDefault = true }],
            DefaultMailbox = mailbox,
        };

        var senderValidator = new ApprovedSenderValidator(
            Options.Create(options),
            new CommunicationAccountService(
                Mock.Of<IDataverseService>(),
                Mock.Of<IDataverseService>(),
                Mock.Of<IDistributedCache>(),
                NullLogger<CommunicationAccountService>.Instance),
            Mock.Of<IDistributedCache>(),
            NullLogger<ApprovedSenderValidator>.Instance);

        var service = new CommunicationService(
            CommunicationChannelTestFactory.CreateDispatcher(graph.Object),
            senderValidator,
            dataverse ?? Mock.Of<ICommunicationDataverseService>(),
            entityService.Object,
            Mock.Of<IDocumentDataverseService>(),
            null!, // CommunicationAccountService — not reached by a shared-mailbox send without attachments
            null!, // JobSubmissionService — not reached
            Mock.Of<ICommunicationEnrichmentService>(),
            Options.Create(options),
            CoreAncestorResolverFixtures.Inert(),
            new RecordOwnershipResolverDouble(), // task 146: the row's owner (not under test here)
            NullLogger<CommunicationService>.Instance);

        await service.SendAsync(request, sender);

        return written ?? throw new InvalidOperationException("The outbound sender wrote no sprk_communication row.");
    }

    private sealed class AcceptedHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Accepted));
    }
}
