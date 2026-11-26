using System.Text.Json;
using System.Text.Json.Nodes;
using Azure.Messaging.ServiceBus;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Models;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Email;
using Sprk.Bff.Api.Services.Jobs;
using Sprk.Bff.Api.Services.Ai.Jobs;
using Sprk.Bff.Api.Tests.Auth.SpeAdmin;
using Sprk.Bff.Api.Tests.Infrastructure.Cache;
using Sprk.Bff.Api.Tests.TestInfrastructure;
using Sprk.Bff.Api.Workers.Office;
using Sprk.Bff.Api.Workers.Office.Messages;
using Xunit;
using SaveContentType = Sprk.Bff.Api.Models.Office.SaveContentType;

namespace Sprk.Bff.Api.Tests.Integration.DataMutation.OfficeFinalization;

/// <summary>
/// spaarkeai-word-add-in-r1 task 138 (#2209): the attachments-only finalization a re-save queues when it names attachments of
/// an email already saved that are not yet saved under its <c>.eml</c>. The worker creates exactly the missing attachment
/// children under the EXISTING <c>.eml</c>, through its one child create (linked to the communication, owned by the carried
/// team, parent link and SPE pointer in the create), and nothing else: no second <c>.eml</c>, no email artifact, no profile of
/// the <c>.eml</c>.
/// </summary>
/// <remarks>
/// <para>The real worker through <see cref="UploadFinalizationWorker.ProcessAsync"/>, doubled only at its module boundaries
/// (Dataverse document and job services, SPE, the .eml converter, the job queue), as task 130's suite. The Dataverse double
/// enforces <c>sprk_graphitemid_uk</c> (one document per drive item), which is what makes two add jobs that race past the
/// existing-child check create a missing child once. SPE's path-keyed upload gives the same path the same item.</para>
/// <para>The payload is sent as JSON with <c>AttachmentsOnly</c> set by name, as the save writes it.</para>
/// </remarks>
[Trait("status", "task-138-word-add-in-r1")]
public class UploadFinalizationAttachmentsOnlyTests
{
    private const string AlreadySaved = "Settlement terms.pdf";
    private const string Missing = "Exhibit A.pdf";
    private const string NotSelected = "Exhibit B.pdf";
    private static readonly Guid CommunicationId = Guid.Parse("13813813-0138-4138-8138-00000000c0c0");
    private static readonly Guid EmlDocumentId = Guid.Parse("13813813-0138-4138-8138-00000000d0c0");

    [Fact]
    public async Task AddReSave_CreatesOnlyTheMissingAttachment_UnderTheExistingEml_AndNothingElse()
    {
        var fixture = new Fixture();
        fixture.SeedChild(AlreadySaved);

        var outcome = await fixture.Worker.ProcessAsync(
            Message(Payload(new List<string> { AlreadySaved, Missing })), CancellationToken.None);

        outcome.IsSuccess.Should().BeTrue();
        var created = fixture.Created.Should().ContainSingle("only the attachment not yet saved becomes a document").Subject;
        created.Name.Should().Be(Missing);
        created.ParentDocumentLookup.Should().Be(EmlDocumentId, "it is a child of the existing .eml, not of a second one");
        created.RelatedCommunicationId.Should().Be(CommunicationId, "linked to the email's communication, as task 130 links them");
        created.OwningTeamId.Should().Be(RecordOwnershipResolverDouble.DefaultTeamId);
        created.FilePointer.Should().NotBeNull("the SPE pointer is in the create (030b)");
        fixture.ProcessingJobs.Verify(
            p => p.CreateEmailArtifactAsync(It.IsAny<object>(), It.IsAny<CancellationToken>()),
            Times.Never(), "the email artifact was made by the first save");
        fixture.Submitted.Should().NotContain(
            j => j.IdempotencyKey == AppOnlyDocumentAnalysisJobHandler.ProfileIdempotencyKey(EmlDocumentId, null),
            "the .eml was profiled by the first save; only the new child is");
    }

    [Fact]
    public async Task AddReSave_OfEveryAttachment_CreatesEachOneNotYetSaved_AndOnlyThose()
    {
        var fixture = new Fixture();
        fixture.SeedChild(AlreadySaved);

        var outcome = await fixture.Worker.ProcessAsync(Message(Payload(selection: null)), CancellationToken.None);

        outcome.IsSuccess.Should().BeTrue();
        fixture.Created.Select(c => c.Name).Should().BeEquivalentTo(new[] { Missing, NotSelected });
        fixture.CreateAttempts.Should().Be(2, "the saved attachment is found under the .eml and never re-created");
        fixture.ProcessingJobs.Verify(
            p => p.CreateEmailArtifactAsync(It.IsAny<object>(), It.IsAny<CancellationToken>()),
            Times.Never(), "the email artifact was made by the first save");
    }

    [Fact]
    public async Task TwoAddJobs_RacingPastTheCheck_CreateTheMissingAttachmentOnce_AndALaterOneCreatesNothing()
    {
        var fixture = new Fixture(holdChildReadsUntil: 2);
        fixture.SeedChild(AlreadySaved);
        var selection = new List<string> { AlreadySaved, Missing };

        // Both jobs read the children before either creates: both see the attachment as missing.
        var outcomes = await Task.WhenAll(
            fixture.Worker.ProcessAsync(Message(Payload(selection)), CancellationToken.None),
            fixture.Worker.ProcessAsync(Message(Payload(selection)), CancellationToken.None));

        outcomes.Should().OnlyContain(o => o.IsSuccess, "an attachment refused by the alternate key never fails the job");
        fixture.Created.Should().ContainSingle("the second create of the same drive item is refused by sprk_graphitemid_uk")
            .Which.Name.Should().Be(Missing);

        fixture.ReleaseChildReads();
        (await fixture.Worker.ProcessAsync(Message(Payload(selection)), CancellationToken.None)).IsSuccess.Should().BeTrue();
        fixture.CreateAttempts.Should().Be(2, "a later add job finds the child and does not try to create it");
    }

    [Fact]
    public async Task AttachmentsOnly_WithoutTheEmlDocument_FailsAndCreatesNothing()
    {
        var fixture = new Fixture();

        var outcome = await fixture.Worker.ProcessAsync(
            Message(Payload(new List<string> { Missing }, withDocumentId: false)), CancellationToken.None);

        outcome.IsSuccess.Should().BeFalse("an add never falls back to creating a document, which would be a second .eml");
        fixture.CreateAttempts.Should().Be(0);
    }

    // ── Helpers ────────────────────────────────────────────────────────────────────────────────────────────────────

    private static JsonElement Payload(List<string>? selection, bool withDocumentId = true)
    {
        var payload = new UploadFinalizationPayload
        {
            ContentType = SaveContentType.Email,
            AssociationType = "matter",
            AssociationId = Guid.Parse("13813813-0138-4138-8138-0000000000aa"),
            ContainerId = "b!drive",
            TempFileLocation = "spe://b!drive/item-eml",
            FileName = "Status.eml",
            FileSize = 2048,
            DocumentId = withDocumentId ? EmlDocumentId : null,
            OwningTeamId = RecordOwnershipResolverDouble.DefaultTeamId,
            TriggerAiProcessing = true,
            RelatedCommunicationId = CommunicationId,
            EmailMetadata = new EmailArtifactPayload
            {
                Subject = "Status",
                SenderEmail = "counsel@test.com",
                InternetMessageId = "<CAF0-138@mail.example.com>",
                HasAttachments = true,
                SelectedAttachmentFileNames = selection,
            },
        };

        var node = JsonSerializer.SerializeToNode(payload)!.AsObject();
        node["AttachmentsOnly"] = true;
        return JsonSerializer.SerializeToElement(node);
    }

    private static OfficeJobMessage Message(JsonElement payload) => new()
    {
        JobId = Guid.NewGuid(),
        JobType = OfficeJobType.UploadFinalization,
        CorrelationId = "corr-138",
        IdempotencyKey = $"idem-138-{Guid.NewGuid():N}",
        UserId = "5a5a5a5a-0000-4000-8000-00000000cafe",
        Attempt = 1,
        MaxAttempts = 3,
        Payload = payload,
    };

    private sealed class Fixture
    {
        private readonly object _gate = new();
        private readonly List<DocumentEntity> _documents = new();
        private readonly int _holdChildReadsUntil;
        private readonly TaskCompletionSource _readsReleased = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _childReads;

        public Fixture(int holdChildReadsUntil = 0)
        {
            _holdChildReadsUntil = holdChildReadsUntil;
            if (holdChildReadsUntil == 0)
                _readsReleased.TrySetResult();

            var documents = new Mock<IDocumentDataverseService>();
            documents
                .Setup(d => d.CreateDocumentAsync(It.IsAny<CreateDocumentRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((CreateDocumentRequest request, CancellationToken _) =>
                {
                    lock (_gate)
                    {
                        CreateAttempts++;
                        // sprk_graphitemid_uk: one document per drive item.
                        if (request.FilePointer is { } pointer && _documents.Any(d => d.GraphItemId == pointer.ItemId))
                            throw new InvalidOperationException("Test: a record with the same sprk_graphitemid already exists.");

                        var id = Guid.NewGuid().ToString();
                        Created.Add(request);
                        _documents.Add(new DocumentEntity
                        {
                            Id = id,
                            Name = request.Name,
                            GraphItemId = request.FilePointer?.ItemId,
                            ParentDocumentId = request.ParentDocumentLookup?.ToString(),
                        });
                        return id;
                    }
                });
            documents
                .Setup(d => d.GetDocumentsByParentAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .Returns(async (Guid parentId, CancellationToken _) =>
                {
                    // The read is answered as of NOW; the job then waits (when held) until the other job has read too, so
                    // both act on a read taken before either created anything.
                    List<DocumentEntity> children;
                    lock (_gate)
                    {
                        children = _documents.Where(d => d.ParentDocumentId == parentId.ToString()).ToList();
                    }

                    if (Interlocked.Increment(ref _childReads) >= _holdChildReadsUntil)
                        _readsReleased.TrySetResult();
                    await _readsReleased.Task.WaitAsync(TimeSpan.FromSeconds(30));
                    return (IEnumerable<DocumentEntity>)children;
                });

            var graphClientFactory = Mock.Of<IGraphClientFactory>();
            var spe = new Mock<SpeFileStore>(
                MockBehavior.Loose,
                new ContainerOperations(graphClientFactory, TestSpeOwnership.AllowAll(graphClientFactory), Mock.Of<ILogger<ContainerOperations>>()),
                new DriveItemOperations(graphClientFactory, TestSpeOwnership.AllowAll(graphClientFactory), Mock.Of<ILogger<DriveItemOperations>>()),
                new UploadSessionManager(graphClientFactory, TestSpeOwnership.AllowAll(graphClientFactory), Mock.Of<IHttpClientFactory>(), Mock.Of<ILogger<UploadSessionManager>>()),
                new UserOperations(graphClientFactory, Mock.Of<ILogger<UserOperations>>()),
                null!);
            spe.Setup(s => s.DownloadFileAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => new MemoryStream(new byte[] { 1, 2, 3 }));
            // Graph's path-keyed PUT: the same path is the same drive item.
            spe.Setup(s => s.UploadSmallAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((string _, string path, Stream _, CancellationToken _) => new FileHandleDto(
                    ItemIdOf(path), path, null, 10, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, null, false,
                    $"https://contoso.sharepoint.com/{path}"));

            var converter = new Mock<IEmailToEmlConverter>();
            converter.Setup(c => c.ExtractAttachments(It.IsAny<Stream>())).Returns(() => new[]
            {
                Attachment(AlreadySaved),
                Attachment(Missing),
                Attachment(NotSelected),
            });

            var services = new ServiceCollection();
            services.AddScoped(_ => spe.Object);
            services.AddScoped(_ => converter.Object);
            services.AddScoped(_ => new AttachmentFilterService(
                Options.Create(new EmailProcessingOptions()), NullLogger<AttachmentFilterService>.Instance));
            services.AddScoped(_ => Mock.Of<IPostUploadIndexingEnqueuer>());
            var provider = services.BuildServiceProvider();

            var jobSubmission = new Mock<JobSubmissionService>(
                MockBehavior.Loose,
                Options.Create(new ServiceBusOptions()),
                NullLogger<JobSubmissionService>.Instance,
                new Mock<ServiceBusClient>().Object);
            jobSubmission
                .Setup(j => j.SubmitJobAsync(It.IsAny<JobContract>(), It.IsAny<CancellationToken>()))
                .Callback((JobContract job, CancellationToken _) =>
                {
                    lock (_gate)
                    {
                        Submitted.Add(job);
                    }
                })
                .Returns(Task.CompletedTask);

            Worker = new UploadFinalizationWorker(
                NullLogger<UploadFinalizationWorker>.Instance,
                new InMemoryTenantCache(),
                new Mock<ServiceBusClient>().Object,
                provider.GetRequiredService<IServiceScopeFactory>(),
                Options.Create(new ServiceBusOptions()),
                Options.Create(new GraphOptions()),
                documents.Object,
                ProcessingJobs.Object,
                jobSubmission.Object,
                new ConfigurationBuilder().Build(),
                new RecordOwnershipResolverDouble());
        }

        public UploadFinalizationWorker Worker { get; }

        public Mock<IProcessingJobService> ProcessingJobs { get; } = new();

        public List<CreateDocumentRequest> Created { get; } = new();

        public List<JobContract> Submitted { get; } = new();

        public int CreateAttempts { get; private set; }

        /// <summary>The child the first save created: named by the attachment, on the item its upload path gives.</summary>
        public void SeedChild(string attachmentName) => _documents.Add(new DocumentEntity
        {
            Id = Guid.NewGuid().ToString(),
            Name = attachmentName,
            FileName = attachmentName,
            GraphItemId = ItemIdOf($"{EmlDocumentId:N}_{attachmentName}"),
            ParentDocumentId = EmlDocumentId.ToString(),
        });

        public void ReleaseChildReads() => _readsReleased.TrySetResult();

        private static string ItemIdOf(string path) =>
            "item-" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(path)))[..16];

        private static EmailAttachmentInfo Attachment(string fileName)
        {
            var bytes = new byte[20_000];
            return new EmailAttachmentInfo
            {
                AttachmentId = Guid.NewGuid(),
                FileName = fileName,
                MimeType = "application/pdf",
                Content = new MemoryStream(bytes),
                SizeBytes = bytes.Length,
            };
        }
    }
}
