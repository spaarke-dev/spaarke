// KEEP path classification (ADR-038 §2 + tests/CLAUDE.md):
//   - Category: `data-mutation`
//   - Path:     `tests/integration/data-mutation/DocumentContainer/**`
//   - Justification: the relocator is the ONE writer of a sprk_document's SharePoint Embedded pointer outside a path
//     that uploads the bytes itself (unified-access-control-r2 task 166 f1; owner round 21 item 1 (i)-(ii), round 26
//     item 3). Each test pins a write contract: what is stamped, in which order the copy / verify / re-point / delete
//     steps run, and what is NEVER written (a forged pointer is never copied; a source is never deleted before its copy
//     is verified or while another row points at it).
//
// Doubles are module boundaries only (ADR-038 §4): the REAL RecordContainerResolver over its substituted registry,
// entity service and item reader (TestRecordContainerResolver.DocumentPointerWorld — the relocator shares that entity
// service, as in production), and SpeFileStore at its virtual facade methods (the codebase idiom). No
// Mock<HttpMessageHandler>, no DI-registration assertion, no constructor null-check.

using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using Moq;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Models;
using Sprk.Bff.Api.Services.Documents;
using Xunit;
using World = TestRecordContainerResolver.DocumentPointerWorld;

namespace Sprk.Bff.Api.Tests.DataMutation.DocumentContainer;

public class DocumentContainerRelocatorTests
{
    internal const string Item = "01ITEMOFTHEDOCUMENT";
    internal const string ArchiveContainer = "b!archive-container";
    internal const string SecureContainer = "b!secure-matter-own-container";
    internal const string CustomerAContainer = "b!customer-a-container";
    internal const string CustomerA1Container = "b!customer-a-child-container";
    internal const string CustomerBContainer = "b!customer-b-container";

    internal static readonly Guid Root = TestRecordContainerResolver.PointerWorldRootBusinessUnit;
    internal static readonly Guid CustomerA = Guid.Parse("a0000000-0000-4000-8000-00000000f1a0");
    internal static readonly Guid CustomerA1 = Guid.Parse("a1000000-0000-4000-8000-00000000f1a0");
    internal static readonly Guid CustomerB = Guid.Parse("b0000000-0000-4000-8000-00000000f1a0");
    internal static readonly Guid SecureMatter = Guid.Parse("1a000000-0000-4000-8000-00000000f1a0");
    internal static readonly Guid PlainMatter = Guid.Parse("1b000000-0000-4000-8000-00000000f1a0");
    internal static readonly Guid DocumentId = Guid.Parse("1d000000-0000-4000-8000-00000000f1a0");
    internal static readonly Guid OtherPerson = Guid.Parse("2a000000-0000-4000-8000-00000000f1a0");
    internal static readonly Guid OtherPersonObjectId = Guid.Parse("2b000000-0000-4000-8000-00000000f1a0");

    internal static readonly string Creator = TestRecordContainerResolver.PointerWorldCreatorObjectId.ToString("D");

    internal static World Environment()
    {
        var world = new World { ArchiveContainerId = ArchiveContainer };
        world.BusinessUnits[CustomerA] = (Root, CustomerAContainer);
        world.BusinessUnits[CustomerA1] = (CustomerA, CustomerA1Container);
        world.BusinessUnits[CustomerB] = (Root, CustomerBContainer);
        world.SecureClaims[SecureContainer] = ("sprk_matter", SecureMatter);
        world.Rows[("sprk_matter", SecureMatter)] = new Entity("sprk_matter", SecureMatter)
        {
            ["sprk_issecure"] = true,
            ["sprk_containerid"] = SecureContainer,
            ["owningbusinessunit"] = new EntityReference("businessunit", CustomerA),
        };
        world.Rows[("sprk_matter", PlainMatter)] = new Entity("sprk_matter", PlainMatter)
        {
            ["sprk_issecure"] = false,
            ["owningbusinessunit"] = new EntityReference("businessunit", CustomerA1),
        };
        world.Rows[("systemuser", OtherPerson)] = new Entity("systemuser", OtherPerson)
        {
            ["azureactivedirectoryobjectid"] = OtherPersonObjectId,
        };
        return world;
    }

    /// <summary>A document of <paramref name="matter"/> (default: the plain matter, derived container A1).</summary>
    internal static Entity Doc(Guid? matter = null, string? drive = null, string? item = null, Guid? createdBy = null)
    {
        var row = World.Document(DocumentId, createdBy, CustomerA);
        row["sprk_matter"] = new EntityReference("sprk_matter", matter ?? PlainMatter);
        if (drive is not null)
        {
            row["sprk_graphdriveid"] = drive;
        }

        if (item is not null)
        {
            row["sprk_graphitemid"] = item;
        }

        return row;
    }

    /// <summary>The relocator over the world (sharing its entity service) and a recording SPE facade.</summary>
    internal sealed class Rig
    {
        public Rig(World world, Func<SpeItemCreator, SpeItemCreator>? copyFacts = null)
        {
            World = world;
            var resolver = world.Build();
            Resolver = resolver;
            var gcf = Mock.Of<IGraphClientFactory>();
            Spe = new Mock<SpeFileStore>(MockBehavior.Loose,
                new ContainerOperations(gcf, Mock.Of<ILogger<ContainerOperations>>()),
                new DriveItemOperations(gcf, Mock.Of<ILogger<DriveItemOperations>>()),
                new UploadSessionManager(gcf, Mock.Of<IHttpClientFactory>(), Mock.Of<ILogger<UploadSessionManager>>()),
                new UserOperations(gcf, Mock.Of<ILogger<UserOperations>>()),
                null!);
            Spe.Setup(s => s.GetItemCreatorAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((string drive, string item, CancellationToken _) => world.ItemFacts(drive, item));
            Spe.Setup(s => s.DownloadFileAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((string drive, string item, CancellationToken _) =>
                {
                    Steps.Add($"download {drive}/{item}");
                    return new MemoryStream(new byte[1234]);
                });
            Spe.Setup(s => s.UploadSmallAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<ConflictBehavior>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((string drive, string name, Stream _, ConflictBehavior conflict, CancellationToken _) =>
                {
                    Steps.Add($"upload {drive} ({conflict})");
                    var source = world.ItemFacts(SourceDrive ?? drive, SourceItem ?? Item)!;
                    world.Items[(drive, CopyItem)] = copyFacts?.Invoke(source) ?? source with { UserObjectId = null };
                    return new FileHandleDto(CopyItem, name, null, source.Size, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, null, false,
                        "https://contoso.sharepoint.com/copy", drive);
                });
            Spe.Setup(s => s.DeleteFileAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((string drive, string item, CancellationToken _) =>
                {
                    // Records whether the row had already been re-pointed when the file was deleted.
                    Steps.Add($"delete {drive}/{item}{(world.Updates.Count > 0 ? " (after re-point)" : string.Empty)}");
                    return true;
                });
            Relocator = new DocumentContainerRelocator(
                resolver, world.EntityService!, Spe.Object, NullLogger<DocumentContainerRelocator>.Instance);
        }

        public const string CopyItem = "01COPYINTHETARGET";
        public World World { get; }
        public Sprk.Bff.Api.Infrastructure.Dataverse.RecordContainerResolver Resolver { get; }
        public Mock<SpeFileStore> Spe { get; }
        public DocumentContainerRelocator Relocator { get; }
        public List<string> Steps { get; } = new();
        public string? SourceDrive { get; init; }
        public string? SourceItem { get; init; }
    }

    // ═════════════════════════════════════════════════════════════════════════════════════════════════════════════
    // (i) The pointer attach — the client's first file (owner round 21 item 1 (i))
    // ═════════════════════════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Attach_TheCreatorsOwnUpload_InTheDerivedContainer_IsStampedAppOnly_WithTheFileFlag()
    {
        var world = Environment();
        world.Rows[("sprk_document", DocumentId)] = Doc();
        world.Items[(CustomerA1Container, Item)] = new SpeItemCreator("brief.docx", Creator, null, 10, null, "https://contoso/brief.docx");
        var rig = new Rig(world);

        var result = await rig.Relocator.AttachFileAsync(DocumentId, Creator, CustomerA1Container, Item);

        result.Outcome.Should().Be(PointerAttachOutcome.Attached);
        result.AlreadyAttached.Should().BeFalse();
        var stamp = world.Updates.Should().ContainSingle().Subject;
        stamp.Entity.Should().Be("sprk_document");
        stamp.Id.Should().Be(DocumentId);
        stamp.Fields["sprk_graphdriveid"].Should().Be(CustomerA1Container);
        stamp.Fields["sprk_graphitemid"].Should().Be(Item);
        stamp.Fields["sprk_hasfile"].Should().Be(true, "'has a file' and 'points at a file' are written together");
        stamp.Fields["sprk_filepath"].Should().Be("https://contoso/brief.docx", "the web URL is read from Graph, not taken from the client");
    }

    [Fact]
    public async Task Attach_TheSameFileAgain_IsIdempotent_AndWritesNothing()
    {
        var world = Environment();
        world.Rows[("sprk_document", DocumentId)] = Doc(drive: CustomerA1Container, item: Item);
        var rig = new Rig(world);

        var result = await rig.Relocator.AttachFileAsync(DocumentId, Creator, CustomerA1Container, Item);

        result.Outcome.Should().Be(PointerAttachOutcome.Attached);
        result.AlreadyAttached.Should().BeTrue();
        world.Updates.Should().BeEmpty();
    }

    [Fact]
    public async Task Attach_ToARowThatAlreadyHasAnotherFile_IsRefused_ReattachingIsNeverAClientsJob()
    {
        var world = Environment();
        world.Rows[("sprk_document", DocumentId)] = Doc(drive: CustomerA1Container, item: "01THEORIGINALFILE");
        var rig = new Rig(world);

        var result = await rig.Relocator.AttachFileAsync(DocumentId, Creator, CustomerA1Container, Item);

        result.Outcome.Should().Be(PointerAttachOutcome.AlreadyAttached);
        world.Updates.Should().BeEmpty();
    }

    [Fact]
    public async Task Attach_ByAWriterWhoDidNotCreateTheRow_IsRefused()
    {
        var world = Environment();
        world.Rows[("sprk_document", DocumentId)] = Doc();
        var rig = new Rig(world);

        var result = await rig.Relocator.AttachFileAsync(DocumentId, OtherPersonObjectId.ToString("D"), CustomerA1Container, Item);

        result.Outcome.Should().Be(PointerAttachOutcome.NotTheCreator);
        world.Updates.Should().BeEmpty();
    }

    [Fact]
    public async Task Attach_ToARowTheBffCreated_IsRefused_OnlyAPersonsOwnRowTakesAClientFile()
    {
        var world = Environment();
        world.Rows[("sprk_document", DocumentId)] = Doc(createdBy: TestRecordContainerResolver.PointerWorldBffUser);
        var rig = new Rig(world);

        var result = await rig.Relocator.AttachFileAsync(DocumentId, Creator, CustomerA1Container, Item);

        result.Outcome.Should().Be(PointerAttachOutcome.NotTheCreator);
        world.Updates.Should().BeEmpty();
    }

    [Theory]
    [InlineData(CustomerAContainer)]   // the same customer, but not the document's derived container
    [InlineData(CustomerBContainer)]   // another customer's
    [InlineData(SecureContainer)]      // a secure record's
    public async Task Attach_AFileOutsideTheDerivedContainer_IsRefused(string drive)
    {
        var world = Environment();
        world.Rows[("sprk_document", DocumentId)] = Doc();
        var rig = new Rig(world);

        var result = await rig.Relocator.AttachFileAsync(DocumentId, Creator, drive, Item);

        result.Outcome.Should().Be(PointerAttachOutcome.WrongContainer);
        world.Updates.Should().BeEmpty();
    }

    [Fact]
    public async Task Attach_AFileSomeoneElseUploaded_IsRefused()
    {
        var world = Environment();
        world.Rows[("sprk_document", DocumentId)] = Doc();
        world.Items[(CustomerA1Container, Item)] = new SpeItemCreator("payroll.xlsx", OtherPersonObjectId.ToString("D"), null);
        var rig = new Rig(world);

        var result = await rig.Relocator.AttachFileAsync(DocumentId, Creator, CustomerA1Container, Item);

        result.Outcome.Should().Be(PointerAttachOutcome.NotTheUploader);
        world.Updates.Should().BeEmpty();
    }

    [Fact]
    public async Task Attach_AnItemThatIsNotInTheDrive_IsRefused()
    {
        var world = Environment();
        world.Rows[("sprk_document", DocumentId)] = Doc();
        world.Items[(CustomerA1Container, Item)] = null;
        var rig = new Rig(world);

        var result = await rig.Relocator.AttachFileAsync(DocumentId, Creator, CustomerA1Container, Item);

        result.Outcome.Should().Be(PointerAttachOutcome.NotTheUploader);
        world.Updates.Should().BeEmpty();
    }

    [Fact]
    public async Task Attach_WhenTheDocumentsContainerCannotBeDerived_IsRefused()
    {
        var world = Environment();
        world.Rows[("sprk_document", DocumentId)] = Doc(matter: Guid.NewGuid()); // an unreadable matter
        var rig = new Rig(world);

        var result = await rig.Relocator.AttachFileAsync(DocumentId, Creator, CustomerA1Container, Item);

        result.Outcome.Should().Be(PointerAttachOutcome.ContainerUndetermined);
        world.Updates.Should().BeEmpty();
    }

    [Theory]
    [InlineData(null, Item)]
    [InlineData(CustomerA1Container, " ")]
    public async Task Attach_WithoutAFile_IsAnInvalidRequest(string? drive, string item)
    {
        var rig = new Rig(Environment());

        var result = await rig.Relocator.AttachFileAsync(DocumentId, Creator, drive, item);

        result.Outcome.Should().Be(PointerAttachOutcome.InvalidRequest);
    }

    // ═════════════════════════════════════════════════════════════════════════════════════════════════════════════
    // (ii) Relocation (owner round 26 item 3)
    // ═════════════════════════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Relocate_AFileAlreadyInItsDerivedContainer_IsInPlace_AndNothingIsTouched()
    {
        var world = Environment();
        world.Rows[("sprk_document", DocumentId)] = Doc(drive: CustomerA1Container, item: Item);
        var rig = new Rig(world);

        var outcome = await rig.Relocator.RelocateIfMisplacedAsync(DocumentId, apply: true);

        outcome.State.Should().Be(RelocationState.InPlace);
        rig.Steps.Should().BeEmpty();
        world.Updates.Should().BeEmpty();
    }

    [Fact]
    public async Task Relocate_ReportOnly_PlansTheMove_AndReadsOrWritesNoBytes()
    {
        var world = Environment();
        world.Rows[("sprk_document", DocumentId)] = Doc(drive: CustomerBContainer, item: Item);
        var rig = new Rig(world);

        var outcome = await rig.Relocator.RelocateIfMisplacedAsync(DocumentId, apply: false);

        outcome.State.Should().Be(RelocationState.WouldRelocate);
        outcome.TargetDrive.Should().Be(CustomerA1Container);
        rig.Steps.Should().BeEmpty("a dry run downloads, uploads and deletes nothing");
        world.Updates.Should().BeEmpty();
    }

    [Fact]
    public async Task Relocate_CopiesThenVerifiesThenRepoints_AndOnlyThenDeletesTheSource()
    {
        var world = Environment();
        world.Rows[("sprk_document", DocumentId)] = Doc(drive: CustomerBContainer, item: Item);
        var rig = new Rig(world) { SourceDrive = CustomerBContainer };

        var outcome = await rig.Relocator.RelocateIfMisplacedAsync(DocumentId, apply: true);

        outcome.State.Should().Be(RelocationState.Relocated);
        outcome.TargetItem.Should().Be(Rig.CopyItem);
        var update = world.Updates.Should().ContainSingle().Subject;
        update.Fields["sprk_graphdriveid"].Should().Be(CustomerA1Container);
        update.Fields["sprk_graphitemid"].Should().Be(Rig.CopyItem);
        rig.Steps.Should().Equal(
            $"download {CustomerBContainer}/{Item}",
            $"upload {CustomerA1Container} (Rename)",
            $"delete {CustomerBContainer}/{Item} (after re-point)");
        world.Rows[("sprk_document", DocumentId)].GetAttributeValue<string>("sprk_graphitemid").Should().Be(Rig.CopyItem,
            "by the time the source is deleted, the row already names the verified copy");
    }

    [Fact]
    public async Task Relocate_WhenTheCopyDoesNotVerify_RemovesTheCopy_AndLeavesTheRowAndTheSource()
    {
        var world = Environment();
        world.Rows[("sprk_document", DocumentId)] = Doc(drive: CustomerBContainer, item: Item);
        var rig = new Rig(world, copyFacts: source => source with { Size = source.Size - 1 }) { SourceDrive = CustomerBContainer };

        var outcome = await rig.Relocator.RelocateIfMisplacedAsync(DocumentId, apply: true);

        outcome.State.Should().Be(RelocationState.Failed);
        world.Updates.Should().BeEmpty("nothing points at a copy that did not verify");
        rig.Spe.Verify(s => s.DeleteFileAsync(CustomerA1Container, Rig.CopyItem, It.IsAny<CancellationToken>()), Times.Once);
        rig.Spe.Verify(s => s.DeleteFileAsync(CustomerBContainer, Item, It.IsAny<CancellationToken>()), Times.Never,
            "the source is never deleted before its copy is verified");
    }

    [Fact]
    public async Task Relocate_WhenTheHashDiffers_TheCopyDoesNotVerify()
    {
        var world = Environment();
        world.Rows[("sprk_document", DocumentId)] = Doc(drive: CustomerBContainer, item: Item);
        var rig = new Rig(world, copyFacts: source => source with { QuickXorHash = "another-hash" }) { SourceDrive = CustomerBContainer };

        var outcome = await rig.Relocator.RelocateIfMisplacedAsync(DocumentId, apply: true);

        outcome.State.Should().Be(RelocationState.Failed);
        world.Updates.Should().BeEmpty();
    }

    [Fact]
    public async Task Relocate_WhenTheRepointFails_RemovesTheCopy_AndKeepsTheSource()
    {
        var world = Environment();
        world.Rows[("sprk_document", DocumentId)] = Doc(drive: CustomerBContainer, item: Item);
        world.UpdateFault = new HttpRequestException("Dataverse unavailable");
        var rig = new Rig(world) { SourceDrive = CustomerBContainer };

        var outcome = await rig.Relocator.RelocateIfMisplacedAsync(DocumentId, apply: true);

        outcome.State.Should().Be(RelocationState.Failed);
        rig.Spe.Verify(s => s.DeleteFileAsync(CustomerA1Container, Rig.CopyItem, It.IsAny<CancellationToken>()), Times.Once);
        rig.Spe.Verify(s => s.DeleteFileAsync(CustomerBContainer, Item, It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Relocate_WhenAnotherDocumentStillPointsAtTheSource_KeepsTheSource()
    {
        var world = Environment();
        world.Rows[("sprk_document", DocumentId)] = Doc(drive: CustomerBContainer, item: Item);
        world.OtherDocumentsReferencingTheFile = 1;
        world.ReferencedDrive = CustomerBContainer;
        var rig = new Rig(world) { SourceDrive = CustomerBContainer };

        var outcome = await rig.Relocator.RelocateIfMisplacedAsync(DocumentId, apply: true);

        outcome.State.Should().Be(RelocationState.RelocatedSourceKept);
        world.Updates.Should().ContainSingle("the document itself was re-pointed");
        rig.Spe.Verify(s => s.DeleteFileAsync(CustomerBContainer, Item, It.IsAny<CancellationToken>()), Times.Never,
            "deleting it would break the other document");
    }

    [Fact]
    public async Task Relocate_AFileThatIsNotVerifiablyTheRowsOwn_IsNeverCopied()
    {
        // A forged pointer to another person's file: the migration reports it for an administrator, and never copies
        // it into the document's own container (that would serve someone else's file through this row).
        var world = Environment();
        world.Rows[("sprk_document", DocumentId)] = Doc(drive: CustomerBContainer, item: Item);
        world.Items[(CustomerBContainer, Item)] = new SpeItemCreator("payroll.xlsx", OtherPersonObjectId.ToString("D"), null, 10);
        var rig = new Rig(world);

        var outcome = await rig.Relocator.RelocateIfMisplacedAsync(DocumentId, apply: true);

        outcome.State.Should().Be(RelocationState.SourceUnverified);
        rig.Steps.Should().BeEmpty();
        world.Updates.Should().BeEmpty();
    }

    [Fact]
    public async Task Relocate_MakeSecure_MovesTheFileTheDocumentShows_IntoTheSecureContainer_WhateverItsUploader()
    {
        // Round 26 item 3: a record just made secure moves its files into its own container. The move only NARROWS who
        // can reach the bytes, so it is not gated on the uploader (a colleague's upload on the record moves too).
        var world = Environment();
        world.Rows[("sprk_document", DocumentId)] = Doc(matter: SecureMatter, drive: CustomerAContainer, item: Item);
        world.Items[(CustomerAContainer, Item)] = new SpeItemCreator("memo.docx", OtherPersonObjectId.ToString("D"), null, 10, "h");
        var rig = new Rig(world) { SourceDrive = CustomerAContainer };

        var legacy = await rig.Relocator.RelocateIfMisplacedAsync(DocumentId, apply: false);
        var makeSecure = await rig.Relocator.RelocateIfMisplacedAsync(DocumentId, apply: true, RelocationPurpose.MakeSecure);

        legacy.State.Should().Be(RelocationState.SourceUnverified, "the migration never copies another person's file");
        makeSecure.State.Should().Be(RelocationState.Relocated);
        world.Updates.Should().ContainSingle().Which.Fields["sprk_graphdriveid"].Should().Be(SecureContainer);
    }

    [Fact]
    public async Task Relocate_MakeSecure_DoesNotWidenToANonSecureTarget()
    {
        // The MakeSecure exemption applies only when the derived container IS the secure one.
        var world = Environment();
        world.Rows[("sprk_document", DocumentId)] = Doc(drive: CustomerBContainer, item: Item);
        world.Items[(CustomerBContainer, Item)] = new SpeItemCreator("memo.docx", OtherPersonObjectId.ToString("D"), null, 10);
        var rig = new Rig(world);

        var outcome = await rig.Relocator.RelocateIfMisplacedAsync(DocumentId, apply: true, RelocationPurpose.MakeSecure);

        outcome.State.Should().Be(RelocationState.SourceUnverified);
        rig.Steps.Should().BeEmpty();
    }

    [Fact]
    public async Task Relocate_ToARequestedTargetThatIsNotTheDerivedContainer_MovesNothing()
    {
        var world = Environment();
        world.Rows[("sprk_document", DocumentId)] = Doc(drive: CustomerBContainer, item: Item);
        var rig = new Rig(world);

        var outcome = await rig.Relocator.RelocateIfMisplacedAsync(
            DocumentId, apply: true, RelocationPurpose.MakeSecure, expectedTargetContainer: SecureContainer);

        outcome.State.Should().Be(RelocationState.Undecidable, "the relocator never puts a file where the strict rule would refuse it");
        rig.Steps.Should().BeEmpty();
    }

    [Fact]
    public async Task Relocate_AFileLargerThanASingleRequestCopy_IsReported_NotHalfCopied()
    {
        var world = Environment();
        world.Rows[("sprk_document", DocumentId)] = Doc(drive: CustomerBContainer, item: Item);
        world.Items[(CustomerBContainer, Item)] = new SpeItemCreator("video.mp4", Creator, null, DocumentContainerRelocator.MaxRelocatableBytes + 1);
        var rig = new Rig(world);

        var outcome = await rig.Relocator.RelocateIfMisplacedAsync(DocumentId, apply: true);

        outcome.State.Should().Be(RelocationState.Failed);
        rig.Steps.Should().BeEmpty();
    }

    [Fact]
    public async Task Relocate_ARowWithNoFile_AndAnUndecidableRow_AndAMissingItem_AreEachReported()
    {
        var noFile = Environment();
        noFile.Rows[("sprk_document", DocumentId)] = Doc();
        var undecidable = Environment();
        undecidable.Rows[("sprk_document", DocumentId)] = Doc(matter: Guid.NewGuid(), drive: CustomerBContainer, item: Item);
        var missing = Environment();
        missing.Rows[("sprk_document", DocumentId)] = Doc(drive: CustomerBContainer, item: Item);
        missing.Items[(CustomerBContainer, Item)] = null;

        (await new Rig(noFile).Relocator.RelocateIfMisplacedAsync(DocumentId, apply: true)).State.Should().Be(RelocationState.NoFile);
        (await new Rig(undecidable).Relocator.RelocateIfMisplacedAsync(DocumentId, apply: true)).State.Should().Be(RelocationState.Undecidable);
        (await new Rig(missing).Relocator.RelocateIfMisplacedAsync(DocumentId, apply: true)).State.Should().Be(RelocationState.FileMissing);
    }

    // The batch entry point for the second caller (task 150's Make Secure): every outcome counted, the incomplete listed.
    [Fact]
    public async Task RelocateDocuments_CountsEveryOutcome_AndListsTheIncomplete_SoARepeatCallCompletes()
    {
        var world = Environment();
        var inPlace = Guid.Parse("3a000000-0000-4000-8000-00000000f1a0");
        world.Rows[("sprk_document", inPlace)] = new Entity("sprk_document", inPlace)
        {
            ["createdby"] = new EntityReference("systemuser", TestRecordContainerResolver.PointerWorldCreator),
            ["owningbusinessunit"] = new EntityReference("businessunit", CustomerA),
            ["sprk_matter"] = new EntityReference("sprk_matter", SecureMatter),
            ["sprk_graphdriveid"] = SecureContainer,
            ["sprk_graphitemid"] = "01INPLACE",
        };
        world.Rows[("sprk_document", DocumentId)] = Doc(matter: SecureMatter, drive: CustomerAContainer, item: Item);
        world.OtherDocumentsReferencingTheFile = 1;
        world.ReferencedDrive = CustomerAContainer;
        var rig = new Rig(world) { SourceDrive = CustomerAContainer };

        var result = await rig.Relocator.RelocateDocumentsAsync(
            [inPlace, DocumentId], SecureContainer, RelocationPurpose.MakeSecure, apply: true);

        result.Counts[RelocationState.InPlace].Should().Be(1);
        result.Counts[RelocationState.RelocatedSourceKept].Should().Be(1);
        result.Complete.Should().BeFalse(
            "for Make Secure a source left behind in the shared container is the exposure the move exists to end");
        result.Incomplete.Should().ContainSingle().Which.DocumentId.Should().Be(DocumentId);
    }

    [Theory]
    [InlineData(RelocationState.Relocated, RelocationPurpose.LegacyMigration, true)]
    [InlineData(RelocationState.RelocatedSourceKept, RelocationPurpose.LegacyMigration, true)]
    [InlineData(RelocationState.RelocatedSourceKept, RelocationPurpose.MakeSecure, false)]
    [InlineData(RelocationState.InPlace, RelocationPurpose.MakeSecure, true)]
    [InlineData(RelocationState.WouldRelocate, RelocationPurpose.LegacyMigration, false)]
    [InlineData(RelocationState.SourceUnverified, RelocationPurpose.LegacyMigration, false)]
    [InlineData(RelocationState.Undecidable, RelocationPurpose.MakeSecure, false)]
    public void ABatchIsComplete_OnlyWhenEveryFileIsSettled(RelocationState state, RelocationPurpose purpose, bool settled)
        => DocumentRelocationBatchResult.IsSettled(state, purpose).Should().Be(settled);

    [Fact]
    public void CopyVerification_ComparesSizeAndHash()
    {
        var source = new SpeItemCreator("f", null, null, 10, "h");

        DocumentContainerRelocator.CopyMismatch(source, source).Should().BeNull();
        DocumentContainerRelocator.CopyMismatch(source, source with { Size = 11 }).Should().NotBeNull();
        DocumentContainerRelocator.CopyMismatch(source, source with { QuickXorHash = "x" }).Should().NotBeNull();
        DocumentContainerRelocator.CopyMismatch(source, source with { QuickXorHash = null }).Should().BeNull(
            "a hash Graph has not computed yet is not a mismatch; the size still is checked");
        DocumentContainerRelocator.CopyMismatch(source with { Size = null }, source).Should().NotBeNull(
            "an unknown source size cannot verify anything");
        DocumentContainerRelocator.CopyMismatch(source, null).Should().NotBeNull();
    }
}
