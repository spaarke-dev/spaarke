using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Xrm.Sdk;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Services.Communication;
using Sprk.Bff.Api.Services.Communication.Channels;
using Sprk.Bff.Api.Services.Communication.Models;

namespace Sprk.Bff.Api.Tests.TestInfrastructure;

/// <summary>
/// A REAL <see cref="CommunicationService"/> for a send-as-user email, with a recording channel sender at the Graph boundary and a recording
/// <see cref="IGenericEntityService"/> at the Dataverse boundary (ebilling-r1 task 151c-1a). Everything between is production code: the dispatcher, the
/// sender-side validation, the record build, the ownership question (answered by <see cref="RecordOwnershipResolverDouble"/>), the thread step and
/// the enrichment gate.
/// </summary>
internal sealed class RecordingUserEmailRig
{
    public const string UserEmail = "finance.user@contoso.com";
    public const string UserObjectId = "55555555-5555-5555-5555-555555555555";

    public RecordingChannel Channel { get; } = new();

    public RecordOwnershipResolverDouble Owners { get; } = new();

    public Mock<ICommunicationEnrichmentService> Enrichment { get; } = new();

    public Mock<IThreadResolver> Threads { get; } = new();

    /// <summary>Every entity the service created, in order.</summary>
    public List<Entity> Created { get; } = [];

    public Mock<IGenericEntityService> Entities { get; } = new();

    public CommunicationService Service { get; }

    /// <param name="signer">The tracking-token signer the service signs a server-send marker with (null: none, as in a bare host).</param>
    public RecordingUserEmailRig(Sprk.Bff.Api.Services.Communication.Tracking.ITrackingTokenSigner? signer = null)
    {
        Entities.Setup(e => e.CreateAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>()))
            .Callback<Entity, CancellationToken>((entity, _) => Created.Add(entity))
            .ReturnsAsync(() => Guid.NewGuid());
        Entities.Setup(e => e.UpdateAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<Dictionary<string, object>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        Threads.Setup(t => t.ResolveAndAssignThreadAsync(It.IsAny<ThreadResolutionRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Guid.NewGuid());

        var options = new CommunicationOptions
        {
            ApprovedSenders = [new ApprovedSenderConfig { Email = "noreply@contoso.com", DisplayName = "Contoso", IsDefault = true }],
            DefaultMailbox = "noreply@contoso.com",
        };
        var senderValidator = new ApprovedSenderValidator(
            Microsoft.Extensions.Options.Options.Create(options),
            null!,
            Mock.Of<Microsoft.Extensions.Caching.Distributed.IDistributedCache>(),
            Mock.Of<ILogger<ApprovedSenderValidator>>());
        var dispatcher = new CommunicationChannelDispatcher(
            [Channel],
            [new EmailArchiver(new EmlGenerationService(Mock.Of<ILogger<EmlGenerationService>>()))]);

        Service = new CommunicationService(
            dispatcher,
            senderValidator,
            Mock.Of<ICommunicationDataverseService>(),
            Entities.Object,
            Mock.Of<IDocumentDataverseService>(),
            null!, // CommunicationAccountService: not reached (no archive, no document attachments)
            null!, // JobSubmissionService: not reached
            Enrichment.Object,
            Microsoft.Extensions.Options.Options.Create(options),
            CoreAncestorResolverFixtures.Inert(),
            Owners,
            Mock.Of<ILogger<CommunicationService>>(),
            Threads.Object,
            trackingTokenSigner: signer);
    }

    /// <summary>An authenticated OBO context carrying the email and oid claims <c>SendAsUserAsync</c> reads.</summary>
    public static HttpContext UserContext()
    {
        var identity = new ClaimsIdentity(
            [new Claim("preferred_username", UserEmail), new Claim("oid", UserObjectId), new Claim("name", "Finance User")], "test");
        return new DefaultHttpContext { User = new ClaimsPrincipal(identity) };
    }

    /// <summary>Records each channel send; answers with a canned provider message id.</summary>
    internal sealed class RecordingChannel : ICommunicationChannelSender
    {
        public CommunicationType SupportedType => CommunicationType.Email;

        public List<ChannelSendRequest> Sent { get; } = [];

        public Exception? Fault { get; set; }

        public Task<ChannelSendResult> SendAsync(ChannelSendRequest request, CancellationToken cancellationToken = default)
        {
            if (Fault is { } fault)
            {
                return Task.FromException<ChannelSendResult>(fault);
            }

            Sent.Add(request);
            return Task.FromResult(new ChannelSendResult { FromAddress = request.FromAddress, ProviderMessageId = "msg-id@example.com" });
        }
    }
}
