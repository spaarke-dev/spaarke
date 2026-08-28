using FluentAssertions;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Azure.Messaging.ServiceBus;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Models;
using Sprk.Bff.Api.Services.Communication;
using Sprk.Bff.Api.Services.Communication.Channels;
using Sprk.Bff.Api.Services.Jobs;
using Sprk.Bff.Api.Services.Office;
using Xunit;

namespace Sprk.Bff.Api.Tests.Integration.DataMutation.SpeUploadPaths;

/// <summary>
/// Protects the SPE write contract established on 2026-08-28: <b>every server-constructed upload path is a
/// single flat segment in the container root, and per-record uniqueness lives in the FILE NAME.</b>
///
/// <para><b>The failure mode these tests exist to prevent, stated exactly.</b> In SharePoint Embedded,
/// uploading to a <i>path</i> makes Graph implicitly create every folder segment of that path — that is how
/// <c>communications</c>, <c>emails</c>, <c>exports</c> and friends appeared in SPE Admin without anyone
/// clicking "New Folder". Removing those prefixes is safe ONLY if the uniqueness they were accidentally
/// providing is preserved, because <c>SpeFileStore.UploadSmallAsync</c> resolves to
/// <c>graphClient.Drives[id].Root.ItemWithPath(path).Content.PutAsync(...)</c> — Graph's <b>path-keyed simple
/// PUT</b>, which accepts NO <c>@microsoft.graph.conflictBehavior</c> (contrast
/// <c>CreateUploadSessionAsUserAsync</c>, which does). Two uploads to the same path are therefore a
/// <b>silent, unconditional REPLACE</b>: never a rename, never an error, no trace in any log that a document
/// was destroyed. Flattening <c>communications/{id}/image001.png</c> to a bare <c>image001.png</c> would have
/// been silent DATA LOSS across every pair of emails sharing an attachment name — and <c>image001.png</c> is
/// the single most common attachment name in existence (Outlook names inline signature images that way).
///
/// <para><b>Why the fake drive is a <see cref="Dictionary{TKey,TValue}"/> keyed by path.</b> That IS the
/// production semantic being protected. A mock that merely records calls would happily report "two uploads
/// happened" while production silently kept one file; keying by path reproduces the overwrite, so the
/// collision test can actually observe survival rather than intent. This is a module-boundary double of the
/// <c>SpeFileStore</c> facade (ADR-007, ADR-038 §4) — the same <c>Mock&lt;SpeFileStore&gt;</c> idiom the
/// sibling <c>CommunicationServiceArchiveEmbedTests</c> uses — not a transport-level mock (ADR-038 B1).</para>
///
/// <para><b>Perturbation-verified.</b> On 2026-08-28 <c>CommunicationService.ArchiveToSpeAsync</c>'s path was
/// temporarily reverted to a naive flat <c>{emlResult.FileName}</c>; the collision test failed with
/// <c>Expected drive to contain 2 item(s), but found 1</c>, and the two no-separator tests still passed.
/// That asymmetry is the point: a no-slash assertion alone would have GREENLIT the data-loss version, which
/// is precisely why the collision test is the one that matters.</para>
/// </summary>
public class SpeFlatUploadPathTests
{
    private const string CommunicationEntity = "sprk_communication";
    private const string AttachmentEntity = "sprk_communicationattachment";
    private const string DocumentEntity = "sprk_document";

    /// <summary>
    /// An in-memory stand-in for an SPE drive that reproduces the ONE behaviour under test: the drive is
    /// keyed by path, and writing to an existing path REPLACES it silently, exactly as Graph's path-keyed
    /// simple PUT does.
    /// </summary>
    private sealed class FakeSpeDrive
    {
        private readonly Dictionary<string, byte[]> _itemsByPath = new(StringComparer.OrdinalIgnoreCase);

        public List<string> WrittenPaths { get; } = new();

        public int DistinctItemCount => _itemsByPath.Count;

        public IReadOnlyCollection<string> Paths => _itemsByPath.Keys;

        public void Put(string path, byte[] content)
        {
            WrittenPaths.Add(path);
            _itemsByPath[path] = content; // silent, unconditional replace — the production semantic
        }
    }

    private static CommunicationOptions Options() => new()
    {
        ApprovedSenders = new[]
        {
            new ApprovedSenderConfig { Email = "noreply@contoso.com", DisplayName = "Contoso", IsDefault = true }
        },
        DefaultMailbox = "noreply@contoso.com",
        ArchiveContainerId = "drive-archive"
    };

    private static Mock<SpeFileStore> BuildSpeMock(FakeSpeDrive drive)
    {
        var gcf = Mock.Of<IGraphClientFactory>();
        var containerOps = new ContainerOperations(gcf, Mock.Of<ILogger<ContainerOperations>>());
        var driveItemOps = new DriveItemOperations(gcf, Mock.Of<ILogger<DriveItemOperations>>());
        var uploadMgr = new UploadSessionManager(gcf, Mock.Of<IHttpClientFactory>(), Mock.Of<ILogger<UploadSessionManager>>());
        var userOps = new UserOperations(gcf, Mock.Of<ILogger<UserOperations>>());

        var speMock = new Mock<SpeFileStore>(MockBehavior.Loose, containerOps, driveItemOps, uploadMgr, userOps, null!);

        speMock
            .Setup(s => s.UploadSmallAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string _, string path, Stream content, CancellationToken _) =>
            {
                using var ms = new MemoryStream();
                content.Position = 0;
                content.CopyTo(ms);
                drive.Put(path, ms.ToArray());
                return (FileHandleDto?)new FileHandleDto(
                    Id: $"item-{drive.WrittenPaths.Count}", Name: path, ParentId: null, Size: ms.Length,
                    CreatedDateTime: DateTimeOffset.UtcNow, LastModifiedDateTime: DateTimeOffset.UtcNow,
                    ETag: null, IsFolder: false, WebUrl: null);
            });

        // NOTE: ResolveDriveIdAsync is deliberately NOT set up — it is non-virtual, so Moq cannot
        // intercept it. It does not need to be: the real implementation returns its argument unchanged
        // when the id already starts with "b!" (SharePoint drive ids do), short-circuiting before any
        // Graph call. Tests that reach it therefore pass a "b!"-prefixed id. See SpeFileStore.cs:188.
        return speMock;
    }

    private static Mock<JobSubmissionService> BuildJobMock()
    {
        var sbOptions = new Mock<IOptions<ServiceBusOptions>>();
        sbOptions.Setup(o => o.Value).Returns(new ServiceBusOptions
        {
            QueueName = "test-jobs",
            ConnectionString = "Endpoint=sb://test.servicebus.windows.net/;SharedAccessKeyName=k;SharedAccessKey=v",
        });

        var jobMock = new Mock<JobSubmissionService>(
            MockBehavior.Loose,
            sbOptions.Object,
            Mock.Of<ILogger<JobSubmissionService>>(),
            new Mock<ServiceBusClient>().Object);

        jobMock
            .Setup(j => j.SubmitJobAsync(It.IsAny<JobContract>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        return jobMock;
    }

    private static CommunicationService BuildSut(IGenericEntityService entityService, SpeFileStore speFileStore)
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
            accountService,
            BuildJobMock().Object,
            Mock.Of<ICommunicationEnrichmentService>(),
            Microsoft.Extensions.Options.Options.Create(options),
            Mock.Of<ILogger<CommunicationService>>(),
            scopeFactory: Sprk.Bff.Api.Tests.Services.Communication.SpeScopeFactoryStub.Create(speFileStore));
    }

    /// <summary>
    /// A never-yet-archived communication whose SUBJECT is fixed by the caller. The subject is what
    /// <c>EmlGenerationService</c> derives the .eml file name from, so two communications sharing a subject
    /// produce the SAME file name — which is the collision this suite is about.
    /// </summary>
    private static Mock<IGenericEntityService> BuildEntityService(Guid communicationId, string subject)
    {
        var entityService = new Mock<IGenericEntityService>();

        var communication = new Entity(CommunicationEntity, communicationId);
        communication["sprk_subject"] = subject;
        communication["sprk_from"] = "sender@contoso.com";
        communication["sprk_to"] = "recipient@example.com";
        communication["sprk_direction"] = new OptionSetValue(100000000); // Incoming
        communication["sprk_graphmessageid"] = $"AAMkAG{communicationId:N}@contoso.com";

        entityService
            .Setup(s => s.RetrieveAsync(CommunicationEntity, communicationId, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(communication);

        entityService
            .Setup(s => s.RetrieveMultipleAsync(It.Is<QueryExpression>(q => q.EntityName == AttachmentEntity), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EntityCollection(new List<Entity>()));

        // Not yet archived — FindExistingArchiveDocumentAsync returns empty, so the .eml is written.
        entityService
            .Setup(s => s.RetrieveMultipleAsync(It.Is<QueryExpression>(q => q.EntityName == DocumentEntity), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EntityCollection(new List<Entity>()));

        entityService
            .Setup(s => s.CreateAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Entity _, CancellationToken _) => Guid.NewGuid());

        entityService
            .Setup(s => s.UpdateAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<Dictionary<string, object>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        return entityService;
    }

    // =============================================================================================
    // THE TEST THAT MATTERS — collision survival
    // =============================================================================================

    [Fact]
    public async Task ArchiveExisting_ForTwoCommunicationsWithTheSameSubject_PersistsBothAndOverwritesNeither()
    {
        // Arrange — two DIFFERENT communications whose subjects are identical, so EmlGenerationService
        // produces the same "{date}_{subject}.eml" file name for both. Before the flattening change the
        // {communicationId:N} FOLDER segment kept them apart; it now has to be the FILE NAME doing it.
        var drive = new FakeSpeDrive();
        var speMock = BuildSpeMock(drive);

        var firstId = Guid.NewGuid();
        var secondId = Guid.NewGuid();
        const string SharedSubject = "Quarterly Filing";

        // Act
        await BuildSut(BuildEntityService(firstId, SharedSubject).Object, speMock.Object)
            .ArchiveExistingAsync(firstId, CancellationToken.None);
        await BuildSut(BuildEntityService(secondId, SharedSubject).Object, speMock.Object)
            .ArchiveExistingAsync(secondId, CancellationToken.None);

        // Assert — BOTH survive. This is the assertion that fails if a future edit "simplifies" the path
        // to a bare file name: the drive would hold one item, and the first email's archive would be gone
        // with no error anywhere.
        drive.WrittenPaths.Should().HaveCount(2, "both archives were attempted");
        drive.DistinctItemCount.Should().Be(
            2,
            "two different communications must not overwrite each other — Graph's path-keyed PUT replaces "
            + "silently, so an identical path means the first .eml is destroyed without any error");

        // ...and each carries its own communication id, which is WHERE the uniqueness now lives.
        drive.Paths.Should().Contain(p => p.StartsWith($"{firstId:N}_", StringComparison.Ordinal));
        drive.Paths.Should().Contain(p => p.StartsWith($"{secondId:N}_", StringComparison.Ordinal));
    }

    // =============================================================================================
    // FLATNESS — no upload path may contain a folder separator
    // =============================================================================================

    [Fact]
    public async Task ArchiveExisting_UploadPath_IsASingleFlatSegmentInTheContainerRoot()
    {
        var drive = new FakeSpeDrive();
        var speMock = BuildSpeMock(drive);
        var communicationId = Guid.NewGuid();

        await BuildSut(BuildEntityService(communicationId, "Quarterly Filing").Object, speMock.Object)
            .ArchiveExistingAsync(communicationId, CancellationToken.None);

        drive.WrittenPaths.Should().ContainSingle();
        drive.WrittenPaths[0].Should().NotContain(
            "/",
            "a '/' in an SPE upload path makes Graph implicitly CREATE that folder — which is the whole "
            + "defect being fixed; the path must be one flat segment");
        drive.WrittenPaths[0].Should().NotStartWith(
            "/",
            "a leading slash is a folder separator too, and several of these paths used to carry one");
        drive.WrittenPaths[0].Should().EndWith(".eml", "Graph/SPE infers message/rfc822 from the extension");
    }

    [Fact]
    public async Task OfficeSave_UploadPath_IsTheBareFileNameWithNoFolderSegment()
    {
        // OfficeStorageUploader is the ONLY live Office-save upload site (the worker's own upload path was
        // deleted as unreachable on 2026-08-28). Its dormant folderPath parameter is gone, so the file name
        // IS the path — this test pins that it stays that way.
        var drive = new FakeSpeDrive();
        var speMock = BuildSpeMock(drive);
        var uploader = new OfficeStorageUploader(speMock.Object, Mock.Of<ILogger<OfficeStorageUploader>>());

        // "b!"-prefixed so the real (non-virtual) ResolveDriveIdAsync short-circuits — see BuildSpeMock.
        var result = await uploader.UploadToSpeAsync(
            "b!drive-1", "Quarterly Filing.docx", new MemoryStream(new byte[] { 1, 2, 3 }), CancellationToken.None);

        result.Success.Should().BeTrue();
        drive.WrittenPaths.Should().ContainSingle().Which.Should().Be("Quarterly Filing.docx");
    }

    // =============================================================================================
    // THE ROOT CAUSE OF THE REPORTED FOLDERS — a filename IS a path
    // =============================================================================================

    [Theory]
    [InlineData("New Word Document from Word Web Add In 8/24/2026", "New Word Document from Word Web Add In 8242026")]
    [InlineData("Word Document Office Add  In 3/4/2026", "Word Document Office Add  In 342026")]
    [InlineData("Report 2026.docx", "Report 2026.docx")]
    [InlineData("back\\slash.docx", "backslash.docx")]
    public void SanitizeFileName_WhenNameContainsPathSeparators_StripsThemSoNoFolderCanBeMinted(
        string typedName, string expected)
    {
        // THE ACTUAL CAUSE of the folders the operator found in SPE Admin. The Word add-in's "Document
        // Name" box is free text; its value became the upload path verbatim, and Graph created one folder
        // per '/' segment. "…Add In 8/24/2026" is a DATE — it produced a folder "…Add In 8" containing a
        // folder "24" containing an extension-less file "2026", which is why the folder name looked like a
        // truncated document title. Removing hardcoded folder prefixes does NOT cover this: a file name is
        // a path, so it needs its own guard.
        OfficeEmailEnricher.SanitizeFileName(typedName).Should().Be(expected);
        OfficeEmailEnricher.SanitizeFileName(typedName).Should().NotContain("/");
    }

    [Fact]
    public void SanitizeFileName_WhenNameIsOnlySeparators_ReturnsUntitledRatherThanEmpty()
    {
        // Fail-safe: an empty path would make the Graph PUT target the drive ROOT itself. "untitled" is a
        // bad file name; an empty one is an unpredictable API call.
        OfficeEmailEnricher.SanitizeFileName("///").Should().Be("untitled");
    }
}
