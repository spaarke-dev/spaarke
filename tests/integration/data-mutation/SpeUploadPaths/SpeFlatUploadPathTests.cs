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
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Models;
using Sprk.Bff.Api.Services.Communication;
using Sprk.Bff.Api.Services.Communication.Channels;
using Sprk.Bff.Api.Services.Communication.Engine;
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
            Sprk.Bff.Api.Tests.TestInfrastructure.CoreAncestorResolverFixtures.Inert(),
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
        SpeUploadPath.SanitizeFileName(typedName).Should().Be(expected);
        SpeUploadPath.SanitizeFileName(typedName).Should().NotContain("/");
    }

    [Fact]
    public void SanitizeFileName_WhenNameIsOnlySeparators_ReturnsUntitledRatherThanEmpty()
    {
        // Fail-safe: an empty path would make the Graph PUT target the drive ROOT itself. "untitled" is a
        // bad file name; an empty one is an unpredictable API call.
        SpeUploadPath.SanitizeFileName("///").Should().Be("untitled");
    }

    // =============================================================================================
    // THE ONE SURFACE THAT REJECTS INSTEAD OF SANITIZING
    // =============================================================================================

    [Theory]
    // Rejected — each was accepted by ValidatePathForOBO before 2026-08-29.
    [InlineData("", false)]
    [InlineData(".", false)]
    [InlineData("..", false)]
    [InlineData("back\\slash.docx", false)]
    [InlineData("has:colon.docx", false)]
    [InlineData("wild*card.docx", false)]
    [InlineData("quote\".docx", false)]
    // Accepted — ordinary names, including the characters SharePoint is fine with.
    [InlineData("Report 2026.docx", true)]
    [InlineData("RE Invoice #123.eml", true)]
    [InlineData("a-b_c(1).pdf", true)]
    public void IsSafeSegment_ForOneSegmentOfACallerSuppliedPath_AcceptsNamesAndRejectsNavigationAndInvalidChars(
        string segment, bool expected)
    {
        // The OBO upload routes are the surfaces that do not sanitize: their {*path} is a wildcard where a
        // caller may legitimately address a location inside the destination, and silently rewriting a
        // caller's path would move their bytes without telling them. So they REJECT per segment instead —
        // which is why this returns a verdict, not a clean string. (Named PUT /api/obo/containers/{id}/...
        // until 2026-09-03; that route was deleted by task 076 and the rule moved with its siblings, which
        // all call the same ValidatePathForOBO.)
        //
        // The four gaps this closed in ValidatePathForOBO: a LEADING '/', EMPTY segments ("a//b"), a bare
        // "." segment, and invalid characters. '\\' is the one that mattered most: several SharePoint
        // surfaces read it as a separator, and Path.GetInvalidFileNameChars() does NOT report it on the
        // linux-x64 runtime the BFF publishes to.
        SpeUploadPath.IsSafeSegment(segment).Should().Be(expected);
    }

    [Fact]
    public void IsSafeSegment_ForEverySegmentOfALegitimateSubPath_AcceptsAllOfThem()
    {
        // The capability that must SURVIVE the hardening. A multi-segment path is still legal on the OBO
        // routes — this rule tightens each segment, it does not forbid having several. (Reported separately:
        // that capability is currently dormant; every client caller sends a single file name.)
        "folder/sub folder/Report 2026.docx"
            .Split('/')
            .Should().OnlyContain(segment => SpeUploadPath.IsSafeSegment(segment));
    }

    // =============================================================================================
    // SANITIZATION AT THE SINK — the runtime half of the 2026-08-29 sweep
    // ---------------------------------------------------------------------------------------------
    // WHY ONLY THREE SITES HAVE A BEHAVIOURAL TEST HERE, AND THAT IS NOT AN OVERSIGHT. Fourteen call
    // sites now sanitize. Fourteen behavioural tests asserting one string each — most needing a
    // WebApplicationFactory, an IFormFileCollection, or a text extractor to reach one interpolation —
    // is exactly the setup-to-assertion shape tests/CLAUDE.md B15 bans, and it would rot within a
    // release. Blanket coverage of the SITES is the job of the source rule
    // (tests/Spaarke.ArchTests/SpeUploadPathIsFlatGuardTests.cs Rule 2), which also covers sites nobody
    // has written yet. What a source rule CANNOT do is prove that a real value flowing through real
    // collaborators comes out flat, so the three sites below are chosen for exactly that: one per
    // distinct mechanism.
    //
    //   · OfficeStorageUploader — the last mile of the Office save path, i.e. the ACTUAL route the
    //     reported folders were minted through.
    //   · CommunicationService — a slash arriving from DATA (the subject line) rather than from a
    //     parameter, flowing through the real EmlGenerationService.
    //   · MessageAttachmentMaterializer — the restored site, where flat + sanitized + the id-carrying
    //     uniqueness all have to hold at once.
    // =============================================================================================

    /// <summary>The exact string a user typed into the Word add-in's "Document Name" box, which minted the
    /// folders an operator found in SPE Admin. Every test below feeds this same value, so a reader can see
    /// the one production incident travelling through each mechanism.</summary>
    private const string TheNameThatMintedFolders = "New Word Document from Word Web Add In 8/24/2026";

    [Fact]
    public async Task OfficeSave_WhenTheTypedDocumentNameContainsSlashes_UploadsOneFlatSegmentAndMintsNoFolder()
    {
        // The reported incident, end to end through the uploader. Before 2026-08-28 this exact value
        // reached Graph verbatim and produced folder "…Add In 8" / folder "24" / file "2026".
        var drive = new FakeSpeDrive();
        var uploader = new OfficeStorageUploader(
            BuildSpeMock(drive).Object, Mock.Of<ILogger<OfficeStorageUploader>>());

        var result = await uploader.UploadToSpeAsync(
            "b!drive-1", TheNameThatMintedFolders, new MemoryStream(new byte[] { 1, 2, 3 }), CancellationToken.None);

        result.Success.Should().BeTrue();

        var written = drive.WrittenPaths.Should().ContainSingle().Subject;
        written.Should().NotContain("/", "each '/' Graph sees in an upload path becomes a FOLDER it creates");
        written.Should().Be("New Word Document from Word Web Add In 8242026");

        // The drive holds ONE item and it is a file at the root — no intermediate folder was addressed.
        drive.DistinctItemCount.Should().Be(1);
    }

    [Fact]
    public async Task ArchiveExisting_WhenTheSubjectContainsSlashes_UploadsOneFlatSegmentAndMintsNoFolder()
    {
        // The same defect arriving from DATA rather than from a parameter: the subject line is
        // sender-controlled, EmlGenerationService derives the .eml file name from it, and that name becomes
        // the upload path. A date in a subject line is entirely ordinary.
        var drive = new FakeSpeDrive();
        var speMock = BuildSpeMock(drive);
        var communicationId = Guid.NewGuid();

        await BuildSut(BuildEntityService(communicationId, TheNameThatMintedFolders).Object, speMock.Object)
            .ArchiveExistingAsync(communicationId, CancellationToken.None);

        var written = drive.WrittenPaths.Should().ContainSingle().Subject;
        written.Should().NotContain("/");
        written.Should().NotStartWith("/");
        written.Should().StartWith($"{communicationId:N}_", "uniqueness still lives in the FILE NAME");
        written.Should().EndWith(".eml");
    }

    [Fact]
    public async Task MaterializeAttachment_WhenTheFileNameContainsSlashes_UploadsOneFlatSegmentCarryingTheCommunicationId()
    {
        // The RESTORED site (deleted 2026-08-28, restored 2026-08-29). Its path was
        // "/communications/{id:N}/attachments/{fileName}" — three implicit folder levels AND an
        // unsanitized name. Both halves are asserted here, because fixing either alone leaves a defect:
        // dropping the folders without sanitizing keeps the name-as-path bug, and sanitizing without
        // folding the id in keeps the silent-overwrite bug.
        var drive = new FakeSpeDrive();
        var communicationId = Guid.NewGuid();

        var spe = new Mock<ISpeFileOperations>();
        spe.Setup(s => s.UploadSmallAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string _, string path, Stream content, CancellationToken _) =>
            {
                using var ms = new MemoryStream();
                content.Position = 0;
                content.CopyTo(ms);
                drive.Put(path, ms.ToArray());
                return (FileHandleDto?)new FileHandleDto(
                    Id: "spe-item-1", Name: path, ParentId: null, Size: ms.Length,
                    CreatedDateTime: DateTimeOffset.UtcNow, LastModifiedDateTime: DateTimeOffset.UtcNow,
                    ETag: null, IsFolder: false, WebUrl: null);
            });

        var generic = new Mock<IGenericEntityService>();
        generic.Setup(g => g.CreateAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Guid.NewGuid());

        var sut = new MessageAttachmentMaterializer(
            spe.Object,
            generic.Object,
            Microsoft.Extensions.Options.Options.Create(Options()),
            Mock.Of<ILogger<MessageAttachmentMaterializer>>(),
            NonSecureContainerResolver(communicationId));

        var result = await sut.MaterializeAsync(new MaterializeAttachmentRequest
        {
            CommunicationId = communicationId,
            FileName = $"{TheNameThatMintedFolders}.pdf",
            ContentType = "application/pdf",
            Content = new MemoryStream(new byte[] { 1, 2, 3 }),
        });

        result.Succeeded.Should().BeTrue();

        var written = drive.WrittenPaths.Should().ContainSingle().Subject;
        written.Should().NotContain("/", "the old path had THREE folder segments and an unsanitized name");
        written.Should().NotStartWith("/", "the old path also carried a leading slash");
        written.Should().Be($"{communicationId:N}_New Word Document from Word Web Add In 8242026.pdf");
    }

    [Fact]
    public async Task MaterializeAttachment_WhenTheCallerSuppliesADriveId_StillResolvesThroughTheRecordAwareResolver()
    {
        // The restored DriveId is a FALLBACK, not an override — restoring it as a caller-authoritative
        // container would reinstate the exact defect this project exists to close. For a NON-secure
        // communication (this one) the fallback IS the answer, which is what makes the property
        // observable at all: the value the caller supplied is used, but only because the resolver chose
        // to use it. Read with MessageAttachmentMaterializerTests, which pins the refuse-when-absent half.
        var drive = new FakeSpeDrive();
        var communicationId = Guid.NewGuid();
        var driveIdsSeen = new List<string>();

        var spe = new Mock<ISpeFileOperations>();
        spe.Setup(s => s.UploadSmallAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string driveId, string path, Stream content, CancellationToken _) =>
            {
                driveIdsSeen.Add(driveId);
                drive.Put(path, Array.Empty<byte>());
                return (FileHandleDto?)new FileHandleDto(
                    Id: "spe-item-1", Name: path, ParentId: null, Size: 0,
                    CreatedDateTime: DateTimeOffset.UtcNow, LastModifiedDateTime: DateTimeOffset.UtcNow,
                    ETag: null, IsFolder: false, WebUrl: null);
            });

        var generic = new Mock<IGenericEntityService>();
        generic.Setup(g => g.CreateAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Guid.NewGuid());

        var sut = new MessageAttachmentMaterializer(
            spe.Object,
            generic.Object,
            Microsoft.Extensions.Options.Options.Create(Options()),
            Mock.Of<ILogger<MessageAttachmentMaterializer>>(),
            NonSecureContainerResolver(communicationId));

        var result = await sut.MaterializeAsync(new MaterializeAttachmentRequest
        {
            CommunicationId = communicationId,
            FileName = "brief.pdf",
            ContentType = "application/pdf",
            Content = new MemoryStream(new byte[] { 1 }),
            DriveId = "b!caller-named-drive",
        });

        result.Succeeded.Should().BeTrue();
        driveIdsSeen.Should().ContainSingle().Which.Should().Be("b!caller-named-drive");
        result.Reference!.GraphDriveId.Should().Be("b!caller-named-drive");
    }

    /// <summary>
    /// A REAL <see cref="CommunicationContainerResolver"/> answering "this communication regards nothing
    /// secure", so the fallback container is used. Real rather than mocked because
    /// <see cref="CommunicationContainerResolver"/> and <see cref="RecordContainerResolver"/> are
    /// concrete-by-ADR-010 with non-virtual members — there is nothing to mock. Its two collaborators ARE
    /// interfaces and are stubbed at that module boundary. The securable-entity set must be NON-EMPTY: the
    /// resolver treats an empty set as "securability could not be determined" and refuses, which is its
    /// fail-closed contract and not something to work around.
    /// </summary>
    private static CommunicationContainerResolver NonSecureContainerResolver(Guid communicationId)
    {
        var securableEntities = new Mock<ISecurableEntityRegistry>();
        securableEntities
            .Setup(r => r.GetSecurableEntitiesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "sprk_matter" });

        // The communication row exists but carries NO regarding, so no securable regarding is found.
        var resolverEntities = new Mock<IGenericEntityService>();
        resolverEntities
            .Setup(s => s.RetrieveAsync(
                CommunicationEntity, communicationId, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Entity(CommunicationEntity) { Id = communicationId });

        return new CommunicationContainerResolver(
            new RecordContainerResolver(
                securableEntities.Object,
                resolverEntities.Object,
                Mock.Of<ILogger<RecordContainerResolver>>()),
            resolverEntities.Object,
            securableEntities.Object,
            Mock.Of<ILogger<CommunicationContainerResolver>>());
    }
}
