using System.Text.Json;
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
using Sprk.Bff.Api.Tests.Auth.SpeAdmin;
using Sprk.Bff.Api.Tests.Infrastructure.Cache;
using Sprk.Bff.Api.Tests.TestInfrastructure;
using Sprk.Bff.Api.Workers.Office;
using Sprk.Bff.Api.Workers.Office.Messages;
using Xunit;
using SaveContentType = Sprk.Bff.Api.Models.Office.SaveContentType;

namespace Sprk.Bff.Api.Tests.Integration.DataMutation.OfficeFinalization;

/// <summary>
/// spaarkeai-word-add-in-r1 task 141 review (F2): an attachments-only finalization (task 138's key-matched add and task 141's
/// named add) records whether it created every attachment it was queued with. The create loop logs and swallows a failed
/// attachment, so the job used to end Completed ("AttachmentsAdded") while a child was missing, and the pane said "saved".
/// Now a job that left any name without a child ends Failed at stage <c>AttachmentsIncomplete</c>, naming them.
/// </summary>
/// <remarks>
/// The real worker through <see cref="UploadFinalizationWorker.ProcessAsync"/>, doubled at its module boundaries as in
/// <c>UploadFinalizationAttachmentsOnlyTests</c> (whose fixture this copies). The job row writes are recorded from
/// <see cref="IProcessingJobService.UpdateProcessingJobAsync"/>.
/// </remarks>
[Trait("status", "task-141-word-add-in-r1")]
public class UploadFinalizationAttachmentsAddOutcomeTests
{
    private const string Created = "Exhibit A.pdf";
    private const string FailsToUpload = "Exhibit B.pdf";
    private const string FailsToCreate = "Exhibit C.pdf";
    private static readonly Guid EmlDocumentId = Guid.Parse("14114114-0141-4141-8141-00000000d0c1");

    [Fact]
    public async Task Add_WhoseUploadOrRowCreateFailsForAName_EndsFailed_AtAttachmentsIncomplete_NamingWhatWasNotAdded()
    {
        var fixture = new Fixture();

        var outcome = await fixture.Worker.ProcessAsync(
            Message(Payload(new List<string> { Created, FailsToUpload, FailsToCreate })), CancellationToken.None);

        outcome.IsSuccess.Should().BeTrue("the message is done: a new add creates only what is still missing");
        fixture.CreatedNames.Should().Equal(Created);
        var last = fixture.JobUpdates.Last();
        last.Status.Should().Be(3, "a job that did not create every attachment does not end Completed");
        last.Stage.Should().Be("AttachmentsIncomplete");
        last.ErrorCode.Should().Be("OFFICE_ATTACHMENTS_NOT_ADDED");
        last.ErrorMessage.Should().Contain(FailsToUpload).And.Contain(FailsToCreate).And.NotContain(Created);
    }

    [Fact]
    public async Task Add_WhenTheOwnerTeamCannotBeResolved_CreatesNothing_AndEndsFailed()
    {
        var fixture = new Fixture(unresolvableOwner: true);

        await fixture.Worker.ProcessAsync(
            Message(Payload(new List<string> { Created }, carryOwnerTeam: false)), CancellationToken.None);

        fixture.CreatedNames.Should().BeEmpty();
        var last = fixture.JobUpdates.Last();
        last.Status.Should().Be(3);
        last.Stage.Should().Be("AttachmentsIncomplete");
        last.ErrorMessage.Should().Contain(Created);
    }

    [Fact]
    public async Task Add_ThatCreatesEveryName_EndsCompleted_AtAttachmentsAdded()
    {
        var fixture = new Fixture();

        await fixture.Worker.ProcessAsync(Message(Payload(new List<string> { Created })), CancellationToken.None);

        fixture.CreatedNames.Should().Equal(Created);
        var last = fixture.JobUpdates.Last();
        last.Status.Should().Be(2);
        last.Stage.Should().Be("AttachmentsAdded");
        last.ErrorCode.Should().BeNull();
    }

    // ── Helpers ────────────────────────────────────────────────────────────────────────────────────────────────────

    private static JsonElement Payload(List<string> selection, bool carryOwnerTeam = true)
    {
        var payload = new UploadFinalizationPayload
        {
            ContentType = SaveContentType.Email,
            AssociationType = "matter",
            AssociationId = Guid.Parse("14114114-0141-4141-8141-0000000000ab"),
            ContainerId = "b!drive",
            TempFileLocation = "spe://b!drive/item-eml",
            FileName = "Status.eml",
            FileSize = 2048,
            DocumentId = EmlDocumentId,
            OwningTeamId = carryOwnerTeam ? RecordOwnershipResolverDouble.DefaultTeamId : null,
            TriggerAiProcessing = false,
            EmailMetadata = new EmailArtifactPayload
            {
                Subject = "Status",
                SenderEmail = "counsel@test.com",
                InternetMessageId = "<CAF0-141-outcome@mail.example.com>",
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
        CorrelationId = "corr-141",
        IdempotencyKey = $"idem-141-{Guid.NewGuid():N}",
        UserId = "5a5a5a5a-0000-4000-8000-00000000cafe",
        Attempt = 1,
        MaxAttempts = 3,
        Payload = payload,
    };

    private sealed record JobUpdate(int? Status, string? Stage, string? ErrorCode, string? ErrorMessage);

    private sealed class Fixture
    {
        private readonly object _gate = new();
        private readonly List<DocumentEntity> _documents = new();

        public Fixture(bool unresolvableOwner = false)
        {
            var documents = new Mock<IDocumentDataverseService>();
            documents
                .Setup(d => d.CreateDocumentAsync(It.IsAny<CreateDocumentRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((CreateDocumentRequest request, CancellationToken _) =>
                {
                    lock (_gate)
                    {
                        if (request.Name == FailsToCreate)
                            throw new InvalidOperationException("Test: Dataverse refused the child create.");
                        var id = Guid.NewGuid().ToString();
                        CreatedNames.Add(request.Name);
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
                .ReturnsAsync((Guid parentId, CancellationToken _) =>
                {
                    lock (_gate)
                    {
                        return (IEnumerable<DocumentEntity>)_documents
                            .Where(d => d.ParentDocumentId == parentId.ToString()).ToList();
                    }
                });

            ProcessingJobs
                .Setup(p => p.UpdateProcessingJobAsync(It.IsAny<Guid>(), It.IsAny<object>(), It.IsAny<CancellationToken>()))
                .Callback((Guid _, object update, CancellationToken _) =>
                {
                    var type = update.GetType();
                    JobUpdates.Add(new JobUpdate(
                        type.GetProperty("Status")?.GetValue(update) as int?,
                        type.GetProperty("CurrentStage")?.GetValue(update) as string,
                        type.GetProperty("ErrorCode")?.GetValue(update) as string,
                        type.GetProperty("ErrorMessage")?.GetValue(update) as string));
                })
                .Returns(Task.CompletedTask);

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
            spe.Setup(s => s.UploadSmallAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((string _, string path, Stream _, CancellationToken _) => path.EndsWith(FailsToUpload, StringComparison.Ordinal)
                    ? null
                    : new FileHandleDto(
                        $"item-{Guid.NewGuid():N}", path, null, 10, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, null, false,
                        $"https://contoso.sharepoint.com/{path}"));

            var converter = new Mock<IEmailToEmlConverter>();
            converter.Setup(c => c.ExtractAttachments(It.IsAny<Stream>())).Returns(() => new[]
            {
                Attachment(Created),
                Attachment(FailsToUpload),
                Attachment(FailsToCreate),
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
                .Returns(Task.CompletedTask);

            // No team for the record: the worker creates no child rather than an app-owned one (task 080).
            var ownership = new RecordOwnershipResolverDouble { TeamId = unresolvableOwner ? null : RecordOwnershipResolverDouble.DefaultTeamId };
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
                ownership);
        }

        public UploadFinalizationWorker Worker { get; }

        public Mock<IProcessingJobService> ProcessingJobs { get; } = new();

        public List<string> CreatedNames { get; } = new();

        public List<JobUpdate> JobUpdates { get; } = new();

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
