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
/// spaarkeai-word-add-in-r1 task 130 (#2126, UAC D7 T8): the documents <see cref="UploadFinalizationWorker"/> creates for
/// an email save — each attachment child, and the fallback create — are created linked to the communication the save
/// carried on its payload (<see cref="UploadFinalizationPayload.RelatedCommunicationId"/>), or Restricted when it carried
/// none, exactly as the save's own document was.
/// </summary>
/// <remarks>
/// <para>Before task 130 the children were created with no link and the default (Standard) Access Permission, whatever
/// their parent email was.</para>
/// <para>The real worker through <see cref="UploadFinalizationWorker.ProcessAsync"/>, doubled only at its module
/// boundaries (Dataverse document and job services, SPE, the .eml converter, the job and indexing queues); the attachment
/// filter is the real one. The arrange is the attachment pipeline's collaborators, inherent at this KEEP integration path
/// (B15).</para>
/// </remarks>
[Trait("status", "task-130-word-add-in-r1")]
public class UploadFinalizationEmailCommunicationLinkTests
{
    private const string RfcId = "<CAF0-130@mail.example.com>";
    private const int Restricted = 100000002;
    private const string WorkerCategory = "Sprk.Bff.Api.Workers.Office.UploadFinalizationWorker";
    private static readonly Guid CommunicationId = Guid.Parse("13013013-0130-4130-8130-00000000c0c0");
    private static readonly Guid ParentDocumentId = Guid.Parse("13013013-0130-4130-8130-00000000d0c0");

    [Fact]
    public async Task EmailAttachments_AreEachCreatedLinkedToTheParentEmailsCommunication()
    {
        var fixture = new Fixture();

        var outcome = await fixture.Worker.ProcessAsync(Message(Payload(CommunicationId)), CancellationToken.None);

        outcome.IsSuccess.Should().BeTrue();
        fixture.Created.Should().HaveCount(2, "both attachments become child documents")
            .And.OnlyContain(r => r.RelatedCommunicationId == CommunicationId && r.AccessPermission == null && r.AccessInheritanceRecord == null);
    }

    [Fact]
    public async Task EmailAttachments_OfAnUnidentifiedEmail_AreEachCreatedRestricted_AndEachLogsAnError()
    {
        var fixture = new Fixture();

        var outcome = await fixture.Worker.ProcessAsync(Message(Payload(relatedCommunicationId: null)), CancellationToken.None);

        outcome.IsSuccess.Should().BeTrue("a Restricted child is still created; the job still succeeds");
        fixture.Created.Should().HaveCount(2)
            .And.OnlyContain(r => r.RelatedCommunicationId == null && r.AccessPermission == Restricted)
            .And.OnlyContain(r => IsItsOwnRestriction(r), "task 189: each child's Restricted is written with its own-narrowing record");
        fixture.Logs.Lines.Where(l => l.Category == WorkerCategory && l.Level == LogLevel.Error)
            .Should().HaveCount(2).And.OnlyContain(l => l.Message.Contains("Restricted") && !l.Message.Contains(RfcId));
    }

    [Theory]
    [InlineData(SaveContentType.Email, true)]
    [InlineData(SaveContentType.Email, false)]
    [InlineData(SaveContentType.Attachment, true)]
    [InlineData(SaveContentType.Attachment, false)]
    public async Task FallbackCreate_ForAnEmailDerivedSave_IsLinked_OrRestricted(SaveContentType contentType, bool identified)
    {
        var fixture = new Fixture();
        var payload = Payload(identified ? CommunicationId : null, fallbackCreate: true) with { ContentType = contentType };

        (await fixture.Worker.ProcessAsync(Message(payload), CancellationToken.None)).IsSuccess.Should().BeTrue();

        var created = fixture.Created.Should().ContainSingle().Subject;
        created.RelatedCommunicationId.Should().Be(identified ? CommunicationId : null);
        created.AccessPermission.Should().Be(identified ? null : Restricted);
        if (identified)
            created.AccessInheritanceRecord.Should().BeNull("no own narrowing to record");
        else
            IsItsOwnRestriction(created).Should().BeTrue("task 189: the Restricted is written with its own-narrowing record");
    }

    /// <summary>UAC-r2 task 189 (D7 T7a): the record that marks a Restricted as the document's OWN narrowing. This fixture
    /// mocks the create, so the stager's refusal never runs here; the record is asserted on the request instead. Owner round
    /// 122: the save-time narrowing is PROVISIONAL (released once the document is linked to a communication).</summary>
    private static bool IsItsOwnRestriction(CreateDocumentRequest created)
    {
        var (record, unreadable) = Sprk.Bff.Api.Services.Access.AccessInheritance.Parse(created.AccessInheritanceRecord);
        return !unreadable && record?.OwnPermission == Restricted && record.OwnProvisional;
    }

    [Theory]
    [InlineData(RfcId, "True")]
    [InlineData("AAMkAGI2TG93-130=", "False")] // an Exchange item id is not a Message-ID: nothing could have identified the email
    [InlineData(null, "False")]
    public async Task FallbackCreate_Restricted_LogsMessageIdPresentFromAUsableMessageIdOnly(string? internetMessageId, string expected)
    {
        var fixture = new Fixture();
        var payload = Payload(relatedCommunicationId: null, fallbackCreate: true);
        payload = payload with { EmailMetadata = payload.EmailMetadata! with { InternetMessageId = internetMessageId } };

        (await fixture.Worker.ProcessAsync(Message(payload), CancellationToken.None)).IsSuccess.Should().BeTrue();

        var error = fixture.Logs.Lines.Should().ContainSingle(l => l.Category == WorkerCategory && l.Level == LogLevel.Error).Subject;
        error.Message.Should().Contain($"Message-ID present: {expected}");
    }

    [Fact]
    public async Task FallbackCreate_ForADocumentSave_GetsNeitherALinkNorRestricted()
    {
        var fixture = new Fixture();
        var payload = Payload(CommunicationId, fallbackCreate: true) with
        {
            ContentType = SaveContentType.Document,
            FileName = "Brief.docx",
            EmailMetadata = null,
        };

        (await fixture.Worker.ProcessAsync(Message(payload), CancellationToken.None)).IsSuccess.Should().BeTrue();

        var created = fixture.Created.Should().ContainSingle().Subject;
        created.RelatedCommunicationId.Should().BeNull();
        created.AccessPermission.Should().BeNull();
    }

    // ── Helpers ────────────────────────────────────────────────────────────────────────────────────────────────────

    /// <param name="fallbackCreate">No document id on the payload, so the worker creates the save's document itself (and,
    /// to keep that create the only one, the email has no attachments).</param>
    private static UploadFinalizationPayload Payload(Guid? relatedCommunicationId, bool fallbackCreate = false) => new()
    {
        ContentType = SaveContentType.Email,
        AssociationType = "matter",
        AssociationId = Guid.NewGuid(),
        ContainerId = "b!drive",
        TempFileLocation = "spe://b!drive/item-eml",
        FileName = "Status.eml",
        FileSize = 2048,
        DocumentId = fallbackCreate ? null : ParentDocumentId,
        OwningTeamId = RecordOwnershipResolverDouble.DefaultTeamId,
        TriggerAiProcessing = false,
        RelatedCommunicationId = relatedCommunicationId,
        EmailMetadata = new EmailArtifactPayload
        {
            Subject = "Status",
            SenderEmail = "counsel@test.com",
            InternetMessageId = RfcId,
            HasAttachments = !fallbackCreate,
        },
    };

    private static OfficeJobMessage Message(UploadFinalizationPayload payload) => new()
    {
        JobId = Guid.NewGuid(),
        JobType = OfficeJobType.UploadFinalization,
        CorrelationId = "corr-130",
        IdempotencyKey = $"idem-130-{Guid.NewGuid():N}",
        UserId = "5a5a5a5a-0000-4000-8000-00000000cafe",
        Attempt = 1,
        MaxAttempts = 3,
        Payload = JsonSerializer.SerializeToElement(payload),
    };

    private sealed class Fixture
    {
        public Fixture()
        {
            var documents = new Mock<IDocumentDataverseService>();
            documents
                .Setup(d => d.CreateDocumentAsync(It.IsAny<CreateDocumentRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((CreateDocumentRequest request, CancellationToken _) =>
                {
                    lock (Created)
                    {
                        Created.Add(request);
                    }

                    return Guid.NewGuid().ToString();
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
            spe.Setup(s => s.UploadSmallAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((string _, string path, Stream _, CancellationToken _) => new FileHandleDto(
                    $"item-{Guid.NewGuid():N}", path, null, 10, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, null, false,
                    $"https://contoso.sharepoint.com/{path}"));

            var converter = new Mock<IEmailToEmlConverter>();
            converter.Setup(c => c.ExtractAttachments(It.IsAny<Stream>())).Returns(() => new[]
            {
                Attachment("Settlement terms.pdf"),
                Attachment("Exhibit A.pdf"),
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

            Worker = new UploadFinalizationWorker(
                new LoggerFactory(new ILoggerProvider[] { Logs }).CreateLogger<UploadFinalizationWorker>(),
                new InMemoryTenantCache(),
                new Mock<ServiceBusClient>().Object,
                provider.GetRequiredService<IServiceScopeFactory>(),
                Options.Create(new ServiceBusOptions()),
                Options.Create(new GraphOptions()),
                documents.Object,
                Mock.Of<IProcessingJobService>(),
                jobSubmission.Object,
                new ConfigurationBuilder().Build(),
                new RecordOwnershipResolverDouble());
        }

        public UploadFinalizationWorker Worker { get; }

        public List<CreateDocumentRequest> Created { get; } = new();

        public RecordingLoggerProvider Logs { get; } = new();

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
