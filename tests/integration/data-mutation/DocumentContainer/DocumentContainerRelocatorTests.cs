// KEEP path classification (ADR-038 §2 + tests/CLAUDE.md):
//   - Category: `data-mutation`
//   - Path:     `tests/integration/data-mutation/DocumentContainer/**`
//   - Justification: the relocator is the ONE writer of a sprk_document's SharePoint Embedded pointer outside a path
//     that uploads the bytes itself (unified-access-control-r2 task 166 f1; owner round 21 item 1 (i)-(ii), round 26
//     item 3, round 37). Each test pins a write contract: what is stamped, in which order the copy / verify / re-point /
//     settle steps run, what a move re-keys, and what is NEVER written (a forged pointer is never copied; a source is
//     never deleted before its copy is verified, while a row still uses it, or — on a repeat call — unless it is
//     byte-identical to the document's file).
//
// Doubles are module boundaries only (ADR-038 §4): the REAL RecordContainerResolver over its substituted registry,
// entity service and item reader (TestRecordContainerResolver.DocumentPointerWorld — the relocator shares that entity
// service, as in production), SpeFileStore at its virtual facade methods (the codebase idiom), and the
// IRelocatedFileIndexing facade (the PublicContracts boundary, ADR-013) as a recording fake. No
// Mock<HttpMessageHandler>, no DI-registration assertion, no constructor null-check.

using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using Moq;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Models;
using Sprk.Bff.Api.Services.Ai.PublicContracts;
using Sprk.Bff.Api.Services.Documents;
using Xunit;
using Resolver = Sprk.Bff.Api.Infrastructure.Dataverse.RecordContainerResolver;
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
    internal const string Ledger = DocumentContainerRelocator.RelocationLedgerColumn;

    internal static readonly Guid Root = TestRecordContainerResolver.PointerWorldRootBusinessUnit;
    internal static readonly Guid CustomerA = Guid.Parse("a0000000-0000-4000-8000-00000000f1a0");
    internal static readonly Guid CustomerA1 = Guid.Parse("a1000000-0000-4000-8000-00000000f1a0");
    internal static readonly Guid CustomerB = Guid.Parse("b0000000-0000-4000-8000-00000000f1a0");
    internal static readonly Guid SecureMatter = Guid.Parse("1a000000-0000-4000-8000-00000000f1a0");
    internal static readonly Guid PlainMatter = Guid.Parse("1b000000-0000-4000-8000-00000000f1a0");
    internal static readonly Guid DocumentId = Guid.Parse("1d000000-0000-4000-8000-00000000f1a0");
    internal static readonly Guid OtherDocumentId = Guid.Parse("1e000000-0000-4000-8000-00000000f1a0");
    internal static readonly Guid OtherPerson = Guid.Parse("2a000000-0000-4000-8000-00000000f1a0");
    internal static readonly Guid OtherPersonObjectId = Guid.Parse("2b000000-0000-4000-8000-00000000f1a0");

    internal static readonly string Creator = TestRecordContainerResolver.PointerWorldCreatorObjectId.ToString("D");
    internal static readonly string BffApplication = TestRecordContainerResolver.PointerWorldBffApplicationId.ToString("D");

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
    internal static Entity Doc(Guid? matter = null, string? drive = null, string? item = null, Guid? createdBy = null, Guid? id = null)
    {
        var row = World.Document(id ?? DocumentId, createdBy, CustomerA);
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

    /// <summary>An unfiled document owned in <paramref name="owner"/> that names the given file.</summary>
    internal static Entity UnfiledDoc(Guid id, Guid owner, string drive, string item)
    {
        var row = World.Document(id, owningBusinessUnit: owner);
        row["sprk_graphdriveid"] = drive;
        row["sprk_graphitemid"] = item;
        return row;
    }

    /// <summary>The <see cref="IRelocatedFileIndexing"/> facade at its boundary: records every call, answers as told.</summary>
    internal sealed class RecordingIndexing : IRelocatedFileIndexing
    {
        public List<RelocatedFileIndexRequest> Calls { get; } = new();

        public Func<RelocatedFileIndexRequest, RelocatedFileIndexOutcome> Answer { get; set; }
            = _ => RelocatedFileIndexOutcome.Done("indexed");

        public Task<RelocatedFileIndexOutcome> ReindexRelocatedFileAsync(
            RelocatedFileIndexRequest request, CancellationToken cancellationToken = default)
        {
            Calls.Add(request);
            return Task.FromResult(Answer(request));
        }
    }

    /// <summary>The relocator over the world (sharing its entity service), a recording SPE facade and indexing facade.</summary>
    internal sealed class Rig
    {
        private (string Drive, string Item)? _lastDownload;

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
                    _lastDownload = (drive, item);
                    return new MemoryStream(new byte[1234]);
                });
            Spe.Setup(s => s.UploadSmallAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<ConflictBehavior>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((string drive, string name, Stream _, ConflictBehavior conflict, CancellationToken _) =>
                {
                    Steps.Add($"upload {drive} ({conflict})");
                    var copyId = Copies.Count == 0 ? CopyItem : $"{CopyItem}-{Copies.Count + 1}";
                    Copies.Add((drive, copyId));
                    var (sourceDrive, sourceItem) = _lastDownload ?? (drive, Item);
                    var source = world.ItemFacts(sourceDrive, sourceItem)!;
                    // The copy is uploaded APP-ONLY by the BFF identity: no user, the BFF's application id.
                    world.Items[(drive, copyId)] = copyFacts?.Invoke(source)
                        ?? source with { UserObjectId = null, ApplicationId = BffApplication, WebUrl = $"https://contoso.sharepoint.com/{copyId}" };
                    return new FileHandleDto(copyId, name, TargetRoot, source.Size, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow,
                        $"\"{{{copyId}}},1\"", false, $"https://contoso.sharepoint.com/{copyId}", drive);
                });
            Spe.Setup(s => s.DeleteFileAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((string drive, string item, CancellationToken _) =>
                {
                    if (DeleteFails(drive, item))
                    {
                        Steps.Add($"delete FAILED {drive}/{item}");
                        return false;
                    }

                    // Records whether the row had already been re-pointed when the file was deleted.
                    Steps.Add($"delete {drive}/{item}{(world.Updates.Count > 0 ? " (after re-point)" : string.Empty)}");
                    world.Items[(drive, item)] = null; // gone: the item reader no longer finds it
                    return true;
                });
            Relocator = new DocumentContainerRelocator(
                resolver, world.EntityService!, Spe.Object, Indexing, Locks, NullLogger<DocumentContainerRelocator>.Instance);
        }

        /// <summary>The ADR-004 processing lock, in memory (the Redis-backed store's contract: one holder per key).</summary>
        internal sealed class InMemoryLocks : Sprk.Bff.Api.Services.Jobs.IIdempotencyService
        {
            public HashSet<string> Held { get; } = new(StringComparer.Ordinal);
            public List<string> Released { get; } = new();

            public Task<bool> IsEventProcessedAsync(string eventId, CancellationToken cancellationToken = default) => Task.FromResult(false);

            public Task MarkEventAsProcessedAsync(string eventId, TimeSpan? expiration = null, CancellationToken cancellationToken = default)
                => Task.CompletedTask;

            public Task<bool> TryAcquireProcessingLockAsync(string eventId, TimeSpan? lockDuration = null, CancellationToken cancellationToken = default)
                => Task.FromResult(Held.Add(eventId));

            public Task ReleaseProcessingLockAsync(string eventId, CancellationToken cancellationToken = default)
            {
                Held.Remove(eventId);
                Released.Add(eventId);
                return Task.CompletedTask;
            }
        }

        public InMemoryLocks Locks { get; } = new();

        public const string CopyItem = "01COPYINTHETARGET";
        public const string TargetRoot = "01ROOTOFTHETARGETDRIVE";
        public World World { get; }
        public Resolver Resolver { get; }
        public Mock<SpeFileStore> Spe { get; }
        public RecordingIndexing Indexing { get; } = new();
        public DocumentContainerRelocator Relocator { get; }
        public List<string> Steps { get; } = new();
        public List<(string Drive, string Item)> Copies { get; } = new();
        public Func<string, string, bool> DeleteFails { get; set; } = (_, _) => false;

        public string? LedgerOf(Guid id) => World.Rows[("sprk_document", id)].GetAttributeValue<string>(Ledger);
        public string? ItemOf(Guid id) => World.Rows[("sprk_document", id)].GetAttributeValue<string>("sprk_graphitemid");
        public string? DriveOf(Guid id) => World.Rows[("sprk_document", id)].GetAttributeValue<string>("sprk_graphdriveid");
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
        stamp.Fields.Should().NotContainKey(Ledger, "a first attach owes nothing");
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
    public async Task Attach_ToARowTheBffCreated_WithNoRecordedPerson_IsRefused()
    {
        var world = Environment();
        world.Rows[("sprk_document", DocumentId)] = Doc(createdBy: TestRecordContainerResolver.PointerWorldBffUser);
        var rig = new Rig(world);

        var result = await rig.Relocator.AttachFileAsync(DocumentId, Creator, CustomerA1Container, Item);

        result.Outcome.Should().Be(PointerAttachOutcome.NotTheCreator);
        world.Updates.Should().BeEmpty();
    }

    [Theory]
    [InlineData(true)]    // the recorded person is the caller
    [InlineData(false)]   // the recorded person is someone else
    public async Task Attach_ToARowTheBffCreated_FollowsTheRecordedPerson_TheSameCreatorTheItemCheckReads(bool callerIsRecorded)
    {
        // createdby is the BFF, so the row's creator is sprk_createdbyperson (the round-23 definition, one reader).
        var world = Environment();
        var row = Doc(createdBy: TestRecordContainerResolver.PointerWorldBffUser);
        row[Resolver.CreatedByPersonColumn] =
            new EntityReference("systemuser", callerIsRecorded ? TestRecordContainerResolver.PointerWorldCreator : OtherPerson);
        world.Rows[("sprk_document", DocumentId)] = row;
        world.Items[(CustomerA1Container, Item)] = new SpeItemCreator("brief.docx", Creator, null, 10);
        var rig = new Rig(world);

        var result = await rig.Relocator.AttachFileAsync(DocumentId, Creator, CustomerA1Container, Item);

        if (callerIsRecorded)
        {
            result.Outcome.Should().Be(PointerAttachOutcome.Attached);
            world.Updates.Should().ContainSingle();
            (await rig.Resolver.IsDocumentPointerContainerAllowedAsync(DocumentId, CustomerA1Container, Item))
                .Should().BeTrue("the attached pointer is one the pointer check then honours");
        }
        else
        {
            result.Outcome.Should().Be(PointerAttachOutcome.NotTheCreator);
            world.Updates.Should().BeEmpty();
        }
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
        rig.Indexing.Calls.Should().BeEmpty();
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
        var rig = new Rig(world);

        var outcome = await rig.Relocator.RelocateIfMisplacedAsync(DocumentId, apply: true);

        outcome.State.Should().Be(RelocationState.Relocated);
        outcome.TargetItem.Should().Be(Rig.CopyItem);
        outcome.Pending.Should().BeEmpty();
        var repoint = world.Updates.First();
        repoint.Fields["sprk_graphdriveid"].Should().Be(CustomerA1Container);
        repoint.Fields["sprk_graphitemid"].Should().Be(Rig.CopyItem);
        repoint.Fields[Ledger].Should().BeOfType<string>().Which.Should().Contain(Item,
            "the re-point records the old item in the row's ledger in the SAME update");
        rig.Steps.Should().Equal(
            $"download {CustomerBContainer}/{Item}",
            $"upload {CustomerA1Container} (Rename)",
            $"delete {CustomerBContainer}/{Item} (after re-point)");
        rig.ItemOf(DocumentId).Should().Be(Rig.CopyItem, "by the time the source is deleted, the row already names the verified copy");
        rig.LedgerOf(DocumentId).Should().BeNull("everything the move owed is settled, so the ledger is cleared");
    }

    [Fact]
    public async Task Relocate_WhenTheCopyDoesNotVerify_RemovesTheCopy_AndLeavesTheRowAndTheSource()
    {
        var world = Environment();
        world.Rows[("sprk_document", DocumentId)] = Doc(drive: CustomerBContainer, item: Item);
        var rig = new Rig(world, copyFacts: source => source with { Size = source.Size - 1 });

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
        var rig = new Rig(world, copyFacts: source => source with { QuickXorHash = "another-hash" });

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
        var rig = new Rig(world);

        var outcome = await rig.Relocator.RelocateIfMisplacedAsync(DocumentId, apply: true);

        outcome.State.Should().Be(RelocationState.Failed);
        rig.Spe.Verify(s => s.DeleteFileAsync(CustomerA1Container, Rig.CopyItem, It.IsAny<CancellationToken>()), Times.Once);
        rig.Spe.Verify(s => s.DeleteFileAsync(CustomerBContainer, Item, It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Relocate_WhenARowOfAnotherRecordStillUsesTheSource_KeepsIt_ForThatRecord_AndIsComplete()
    {
        // Round 37 item 2: a row OUTSIDE the document's container keeps the source; it is that record's file now.
        var world = Environment();
        world.Rows[("sprk_document", DocumentId)] = Doc(drive: CustomerBContainer, item: Item);
        world.Rows[("sprk_document", OtherDocumentId)] = UnfiledDoc(OtherDocumentId, CustomerB, CustomerBContainer, Item);
        var rig = new Rig(world);

        var result = await rig.Relocator.RelocateDocumentsAsync([DocumentId], CustomerA1Container, RelocationPurpose.LegacyMigration, apply: true);

        var outcome = result.Outcomes.Should().ContainSingle().Subject;
        outcome.State.Should().Be(RelocationState.RelocatedSourceKeptForOtherRecords);
        result.Complete.Should().BeTrue("a source kept for another record is never incomplete");
        result.SourceKeptForOtherRecords.Should().ContainSingle().Which.KeptFor.Should().ContainSingle($"sprk_document:{OtherDocumentId}");
        rig.Spe.Verify(s => s.DeleteFileAsync(CustomerBContainer, Item, It.IsAny<CancellationToken>()), Times.Never,
            "deleting it would break the other record's document");
        rig.Indexing.Calls.Should().ContainSingle().Which.OldItemRemoved.Should().BeFalse(
            "the old item stays the other record's file, so only THIS document's chunks of it are removed");
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
        var rig = new Rig(world);

        var legacy = await rig.Relocator.RelocateIfMisplacedAsync(DocumentId, apply: false);
        var makeSecure = await rig.Relocator.RelocateIfMisplacedAsync(DocumentId, apply: true, RelocationPurpose.MakeSecure);

        legacy.State.Should().Be(RelocationState.SourceUnverified, "the migration never copies another person's file");
        makeSecure.State.Should().Be(RelocationState.Relocated);
        world.Updates.First().Fields["sprk_graphdriveid"].Should().Be(SecureContainer);
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

    // ═════════════════════════════════════════════════════════════════════════════════════════════════════════════
    // F1 / owner round 37 item 3 — a relocated file is SERVED under the interim rule (the default until the strict flip)
    // ═════════════════════════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ARelocatedFile_IsServedUnderTheInterimRule_BeforeTheStrictFlip()
    {
        // Probe P1 of the f1 verification, on the REAL resolver and relocator: the creator's own upload sits in a
        // container of the owner's customer (served by the interim rule), but not in the document's derived container.
        var world = Environment();
        world.Rows[("sprk_document", DocumentId)] = Doc(drive: CustomerAContainer, item: Item);
        var rig = new Rig(world);
        rig.Resolver.StrictDerivedContainerMode.Should().BeFalse("the interim rule is the default until gate 25");
        (await rig.Resolver.IsDocumentPointerContainerAllowedAsync(DocumentId, CustomerAContainer, Item))
            .Should().BeTrue("precondition: the interim rule serves the file before the move");

        var outcome = await rig.Relocator.RelocateIfMisplacedAsync(DocumentId, apply: true);

        outcome.State.Should().Be(RelocationState.Relocated);
        world.ItemFacts(CustomerA1Container, Rig.CopyItem)!.UserObjectId.Should().BeNull("the copy is uploaded app-only by the BFF identity");
        (await rig.Resolver.IsDocumentPointerContainerAllowedAsync(DocumentId, CustomerA1Container, Rig.CopyItem))
            .Should().BeTrue("round 37 item 3: a BFF-placed file in the document's derived container is served by the interim rule");
    }

    [Fact]
    public async Task AMakeSecureRelocation_IsServedUnderTheInterimRule()
    {
        var world = Environment();
        world.Rows[("sprk_document", DocumentId)] = Doc(matter: SecureMatter, drive: CustomerAContainer, item: Item);
        var rig = new Rig(world);

        var outcome = await rig.Relocator.RelocateIfMisplacedAsync(DocumentId, apply: true, RelocationPurpose.MakeSecure, expectedTargetContainer: SecureContainer);

        outcome.State.Should().Be(RelocationState.Relocated);
        (await rig.Resolver.IsDocumentPointerContainerAllowedAsync(DocumentId, SecureContainer, Rig.CopyItem))
            .Should().BeTrue("every Make Secure move must stay downloadable while the interim rule is in force");
    }

    // ═════════════════════════════════════════════════════════════════════════════════════════════════════════════
    // F2 / owner round 37 item 2 — re-entry settles what a move left owing; a source kept for another record is complete
    // ═════════════════════════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task MakeSecure_WhenTheSourceDeleteFails_IsIncomplete_AndARepeatCallDeletesIt()
    {
        // Probe P3: the first source delete fails. The row is already re-pointed, so the repeat call reads it as in place —
        // and must still delete the secure file's source from the shared container.
        var world = Environment();
        world.Rows[("sprk_document", DocumentId)] = Doc(matter: SecureMatter, drive: CustomerAContainer, item: Item);
        var rig = new Rig(world) { DeleteFails = (drive, item) => drive == CustomerAContainer && item == Item };

        var first = await rig.Relocator.RelocateDocumentsAsync([DocumentId], SecureContainer, RelocationPurpose.MakeSecure, apply: true);

        first.Complete.Should().BeFalse("the secure record's bytes are still in the shared container");
        var pending = first.Incomplete.Should().ContainSingle().Subject;
        pending.State.Should().Be(RelocationState.RelocationPending);
        pending.Pending.Should().Contain(p => p.StartsWith("source-pending", StringComparison.Ordinal));
        rig.LedgerOf(DocumentId).Should().Contain(Item, "the row records the source it still owes");
        rig.ItemOf(DocumentId).Should().Be(Rig.CopyItem);

        rig.DeleteFails = (_, _) => false;
        var second = await rig.Relocator.RelocateDocumentsAsync([DocumentId], SecureContainer, RelocationPurpose.MakeSecure, apply: true);

        second.Complete.Should().BeTrue();
        second.Outcomes.Should().ContainSingle().Which.State.Should().Be(RelocationState.InPlace);
        rig.Steps.Should().Contain($"delete {CustomerAContainer}/{Item} (after re-point)", "the repeat call deleted the source");
        world.ItemFacts(CustomerAContainer, Item).Should().BeNull();
        rig.LedgerOf(DocumentId).Should().BeNull("settled: the ledger is cleared");
        rig.Copies.Should().ContainSingle("the repeat call never copies again");
    }

    [Fact]
    public async Task MakeSecure_ASourceKeptForAnotherRecord_IsComplete_AndARepeatCallDeletesItOnceThatRowIsGone()
    {
        // Probe P2: the source was kept because another record's row used it; that row is then gone.
        var world = Environment();
        world.Rows[("sprk_document", DocumentId)] = Doc(matter: SecureMatter, drive: CustomerAContainer, item: Item);
        world.Rows[("sprk_document", OtherDocumentId)] = UnfiledDoc(OtherDocumentId, CustomerB, CustomerAContainer, Item);
        var rig = new Rig(world);

        var first = await rig.Relocator.RelocateDocumentsAsync([DocumentId], SecureContainer, RelocationPurpose.MakeSecure, apply: true);

        first.Complete.Should().BeTrue("round 37: a source kept for a row OUTSIDE the secure record's subtree is that record's file");
        first.Outcomes.Should().ContainSingle().Which.State.Should().Be(RelocationState.RelocatedSourceKeptForOtherRecords);
        first.SourceKeptForOtherRecords.Should().ContainSingle().Which.KeptFor.Should().Contain($"sprk_document:{OtherDocumentId}");
        world.ItemFacts(CustomerAContainer, Item).Should().NotBeNull("never deleted while a row uses it");
        rig.LedgerOf(DocumentId).Should().NotBeNull("the kept source stays recorded so a later pass can delete it");

        world.Rows.Remove(("sprk_document", OtherDocumentId));
        var second = await rig.Relocator.RelocateDocumentsAsync([DocumentId], SecureContainer, RelocationPurpose.MakeSecure, apply: true);

        second.Complete.Should().BeTrue();
        world.ItemFacts(CustomerAContainer, Item).Should().BeNull("no row uses the source any more, so the repeat call deleted it");
        rig.LedgerOf(DocumentId).Should().BeNull();
        rig.Indexing.Calls.Select(c => c.OldItemRemoved).Should().Equal(false, true);
    }

    [Fact]
    public async Task ASourceKeptForAnotherRecord_IsNeverReportedIncomplete_OnARepeatCall()
    {
        var world = Environment();
        world.Rows[("sprk_document", DocumentId)] = Doc(matter: SecureMatter, drive: CustomerAContainer, item: Item);
        world.Rows[("sprk_document", OtherDocumentId)] = UnfiledDoc(OtherDocumentId, CustomerB, CustomerAContainer, Item);
        var rig = new Rig(world);
        await rig.Relocator.RelocateDocumentsAsync([DocumentId], SecureContainer, RelocationPurpose.MakeSecure, apply: true);
        var writesAfterFirst = world.Updates.Count;

        var again = await rig.Relocator.RelocateDocumentsAsync([DocumentId], SecureContainer, RelocationPurpose.MakeSecure, apply: true);

        again.Complete.Should().BeTrue("round 37: it never loops on, and never reports incomplete for, a source kept for another record");
        again.Outcomes.Should().ContainSingle().Which.State.Should().Be(RelocationState.InPlace);
        again.SourceKeptForOtherRecords.Should().ContainSingle();
        world.Updates.Count.Should().Be(writesAfterFirst, "nothing changed, so nothing is rewritten");
        rig.Copies.Should().ContainSingle();
    }

    [Fact]
    public async Task MakeSecure_ARowInTheSecureSubtreeNamingTheSameFile_IsMovedAlong_WithItsOwnCopy()
    {
        // Round 37 item 2: every referencing row INSIDE the secure record's subtree moves to the secure container. Each
        // gets its own copy — sprk_graphitemid_uk is a unique key on the item id, so two rows can never share one.
        var world = Environment();
        world.Rows[("sprk_document", DocumentId)] = Doc(matter: SecureMatter, drive: CustomerAContainer, item: Item);
        world.Rows[("sprk_document", OtherDocumentId)] = Doc(matter: SecureMatter, drive: CustomerAContainer, item: Item, id: OtherDocumentId);
        var rig = new Rig(world);

        var result = await rig.Relocator.RelocateDocumentsAsync([DocumentId], SecureContainer, RelocationPurpose.MakeSecure, apply: true);

        result.Complete.Should().BeTrue();
        var outcome = result.Outcomes.Should().ContainSingle().Subject;
        outcome.State.Should().Be(RelocationState.Relocated);
        outcome.MovedAlong.Should().ContainSingle().Which.DocumentId.Should().Be(OtherDocumentId);
        rig.DriveOf(OtherDocumentId).Should().Be(SecureContainer);
        rig.ItemOf(OtherDocumentId).Should().NotBe(rig.ItemOf(DocumentId), "one item per row");
        rig.Copies.Should().HaveCount(2);
        world.ItemFacts(CustomerAContainer, Item).Should().BeNull("once both rows left it, nothing uses the source");
        rig.LedgerOf(DocumentId).Should().BeNull();
        rig.LedgerOf(OtherDocumentId).Should().BeNull();
    }

    [Fact]
    public async Task ARepeatCall_NeverDeletesASourceThatIsNotByteIdenticalToTheDocumentsFile()
    {
        // The ledger is the only witness on a repeat call: a ledger entry naming another file (forged before the lock, or
        // corrupted) must never become a delete of that file.
        var world = Environment();
        var row = Doc(matter: SecureMatter, drive: SecureContainer, item: Rig.CopyItem);
        row[Ledger] = LedgerJson(CustomerAContainer, "01SOMEONEELSESFILE");
        world.Rows[("sprk_document", DocumentId)] = row;
        world.Items[(SecureContainer, Rig.CopyItem)] = new SpeItemCreator("memo.docx", null, BffApplication, 1234, "hash-1234");
        world.Items[(CustomerAContainer, "01SOMEONEELSESFILE")] = new SpeItemCreator("payroll.xlsx", OtherPersonObjectId.ToString("D"), null, 1234, "another-hash");
        var rig = new Rig(world);

        var result = await rig.Relocator.RelocateDocumentsAsync([DocumentId], SecureContainer, RelocationPurpose.MakeSecure, apply: true);

        result.Complete.Should().BeFalse();
        result.Incomplete.Should().ContainSingle().Which.Pending.Should().Contain(p => p.Contains("byte-identical", StringComparison.Ordinal));
        rig.Spe.Verify(s => s.DeleteFileAsync(CustomerAContainer, "01SOMEONEELSESFILE", It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ARepeatCall_WhenGraphReturnsNoHash_DoesNotDeleteTheSource()
    {
        var world = Environment();
        var row = Doc(matter: SecureMatter, drive: SecureContainer, item: Rig.CopyItem);
        row[Ledger] = LedgerJson(CustomerAContainer, Item);
        world.Rows[("sprk_document", DocumentId)] = row;
        world.Items[(SecureContainer, Rig.CopyItem)] = new SpeItemCreator("memo.docx", null, BffApplication, 1234, null);
        world.Items[(CustomerAContainer, Item)] = new SpeItemCreator("memo.docx", Creator, null, 1234, null);
        var rig = new Rig(world);

        var result = await rig.Relocator.RelocateDocumentsAsync([DocumentId], SecureContainer, RelocationPurpose.MakeSecure, apply: true);

        result.Complete.Should().BeFalse("size alone is not proof that the two are the same file");
        rig.Spe.Verify(s => s.DeleteFileAsync(CustomerAContainer, Item, It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task AnUnreadableLedger_IsNeverActedOn_AndTheFileIsNotMoved()
    {
        var world = Environment();
        var row = Doc(drive: CustomerBContainer, item: Item);
        row[Ledger] = "{ this is not a relocation ledger";
        world.Rows[("sprk_document", DocumentId)] = row;
        var rig = new Rig(world);

        var outcome = await rig.Relocator.RelocateIfMisplacedAsync(DocumentId, apply: true);

        outcome.State.Should().Be(RelocationState.Failed, "moving would overwrite a ledger nobody can read");
        outcome.Pending.Should().Contain(p => p.StartsWith("ledger-unreadable", StringComparison.Ordinal));
        rig.Steps.Should().BeEmpty();
        world.Updates.Should().BeEmpty();
    }

    [Fact]
    public async Task WithoutTheLedgerColumn_NothingIsMoved_FailClosed()
    {
        // The schema gate (Set-DocumentRelocationSchema.ps1) comes before any relocation: without a place to record what a
        // move owes, no file is moved.
        var world = new World { ArchiveContainerId = ArchiveContainer, RelocationLedgerColumnMissing = true };
        world.BusinessUnits[CustomerA] = (Root, CustomerAContainer);
        world.BusinessUnits[CustomerA1] = (CustomerA, CustomerA1Container);
        world.BusinessUnits[CustomerB] = (Root, CustomerBContainer);
        world.Rows[("sprk_matter", PlainMatter)] = Environment().Rows[("sprk_matter", PlainMatter)];
        world.Rows[("sprk_document", DocumentId)] = Doc(drive: CustomerBContainer, item: Item);
        var rig = new Rig(world);

        var result = await rig.Relocator.RelocateDocumentsAsync([DocumentId], CustomerA1Container, RelocationPurpose.LegacyMigration, apply: true);

        result.Complete.Should().BeFalse();
        result.Outcomes.Should().ContainSingle().Which.State.Should().Be(RelocationState.Failed);
        rig.Steps.Should().BeEmpty();
        world.Updates.Should().BeEmpty();
    }

    [Fact]
    public async Task ARelocationOfADocumentAnotherRelocationHolds_MovesNothing_AndIsIncomplete()
    {
        // One writer per document: a double-clicked Make Secure (or Make Secure meeting the migration pass) must not copy
        // and re-point twice — the loser's copy and ledger entry would be lost.
        var world = Environment();
        world.Rows[("sprk_document", DocumentId)] = Doc(matter: SecureMatter, drive: CustomerAContainer, item: Item);
        var rig = new Rig(world);
        rig.Locks.Held.Add(DocumentContainerRelocator.RelocationLockKey(DocumentId));

        var result = await rig.Relocator.RelocateDocumentsAsync([DocumentId], SecureContainer, RelocationPurpose.MakeSecure, apply: true);

        result.Complete.Should().BeFalse();
        result.Outcomes.Should().ContainSingle().Which.State.Should().Be(RelocationState.Failed);
        rig.Steps.Should().BeEmpty();
        world.Updates.Should().BeEmpty();
        rig.Locks.Released.Should().BeEmpty("a lock this call did not take is never released by it");
    }

    [Fact]
    public async Task TheRelocationLock_IsReleased_EvenWhenTheMoveFails()
    {
        var world = Environment();
        world.Rows[("sprk_document", DocumentId)] = Doc(drive: CustomerBContainer, item: Item);
        world.UpdateFault = new HttpRequestException("Dataverse unavailable");
        var rig = new Rig(world);

        await rig.Relocator.RelocateIfMisplacedAsync(DocumentId, apply: true);

        rig.Locks.Held.Should().BeEmpty();
        rig.Locks.Released.Should().Equal(DocumentContainerRelocator.RelocationLockKey(DocumentId));
    }

    [Fact]
    public async Task ARowThatCannotBeMovedAlong_BecauseAnotherRelocationHoldsIt_KeepsTheSourcePending()
    {
        var world = Environment();
        world.Rows[("sprk_document", DocumentId)] = Doc(matter: SecureMatter, drive: CustomerAContainer, item: Item);
        world.Rows[("sprk_document", OtherDocumentId)] = Doc(matter: SecureMatter, drive: CustomerAContainer, item: Item, id: OtherDocumentId);
        var rig = new Rig(world);
        rig.Locks.Held.Add(DocumentContainerRelocator.RelocationLockKey(OtherDocumentId));

        var result = await rig.Relocator.RelocateDocumentsAsync([DocumentId], SecureContainer, RelocationPurpose.MakeSecure, apply: true);

        result.Complete.Should().BeFalse("a row of the secure subtree still names the source in the shared container");
        world.ItemFacts(CustomerAContainer, Item).Should().NotBeNull("never deleted while a row uses it");
        rig.ItemOf(OtherDocumentId).Should().Be(Item);
    }

    [Fact]
    public async Task ReportOnly_WithALedgerStillOwing_ReportsItPending_AndWritesNothing()
    {
        var world = Environment();
        var row = Doc(matter: SecureMatter, drive: SecureContainer, item: Rig.CopyItem);
        row[Ledger] = LedgerJson(CustomerAContainer, Item);
        world.Rows[("sprk_document", DocumentId)] = row;
        var rig = new Rig(world);

        var outcome = await rig.Relocator.RelocateIfMisplacedAsync(DocumentId, apply: false);

        outcome.State.Should().Be(RelocationState.RelocationPending);
        outcome.Pending.Should().Contain(p => p.StartsWith("source-pending", StringComparison.Ordinal));
        world.Updates.Should().BeEmpty();
        rig.Steps.Should().BeEmpty();
        rig.Indexing.Calls.Should().BeEmpty();
    }

    // ═════════════════════════════════════════════════════════════════════════════════════════════════════════════
    // F4 / owner round 37 item 1 — every reference to the old item is re-keyed, and the index follows the move
    // ═════════════════════════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Relocate_ReKeysTheRowsOwnColumnsThatHeldTheOldIds_InTheSameUpdateAsThePointer()
    {
        var world = Environment();
        var row = Doc(drive: CustomerBContainer, item: Item);
        row["sprk_driveitemid"] = Item;
        row["spk_fileviewerid"] = Item;
        row["sprk_containerid"] = CustomerBContainer;
        row["sprk_parentfolderid"] = "01OLDFOLDERINB";
        row["sprk_etag"] = "\"{OLD},3\"";
        world.Rows[("sprk_document", DocumentId)] = row;
        var rig = new Rig(world);

        await rig.Relocator.RelocateIfMisplacedAsync(DocumentId, apply: true);

        var repoint = world.Updates.First().Fields;
        repoint["sprk_graphitemid"].Should().Be(Rig.CopyItem);
        repoint["sprk_driveitemid"].Should().Be(Rig.CopyItem);
        repoint["spk_fileviewerid"].Should().Be(Rig.CopyItem);
        repoint["sprk_containerid"].Should().Be(CustomerA1Container);
        repoint["sprk_parentfolderid"].Should().Be(Rig.TargetRoot);
        repoint["sprk_etag"].Should().Be($"\"{{{Rig.CopyItem}}},1\"");
    }

    [Fact]
    public async Task Relocate_LeavesAnOwnColumnThatHeldSomethingElse()
    {
        var world = Environment();
        var row = Doc(drive: CustomerBContainer, item: Item);
        row["sprk_driveitemid"] = "01ANUNRELATEDITEM";
        row["sprk_containerid"] = "b!an-unrelated-container";
        world.Rows[("sprk_document", DocumentId)] = row;
        var rig = new Rig(world);

        await rig.Relocator.RelocateIfMisplacedAsync(DocumentId, apply: true);

        world.Updates.First().Fields.Should().NotContainKeys("sprk_driveitemid", "sprk_containerid");
    }

    [Fact]
    public async Task Relocate_ReKeysAChildAttachmentsParentItem_AndTheCommunicationAttachmentRow()
    {
        var world = Environment();
        world.Rows[("sprk_document", DocumentId)] = Doc(drive: CustomerBContainer, item: Item);
        var child = Guid.Parse("3c000000-0000-4000-8000-00000000f1a0");
        var strangersChild = Guid.Parse("3d000000-0000-4000-8000-00000000f1a0");
        var linkedAttachment = Guid.Parse("3e000000-0000-4000-8000-00000000f1a0");
        world.Rows[("sprk_document", child)] = new Entity("sprk_document", child)
        {
            ["sprk_parentdocument"] = new EntityReference("sprk_document", DocumentId),
            ["sprk_parentgraphitemid"] = Item,
        };
        world.Rows[("sprk_document", strangersChild)] = new Entity("sprk_document", strangersChild)
        {
            ["sprk_parentdocument"] = new EntityReference("sprk_document", Guid.NewGuid()),
            ["sprk_parentgraphitemid"] = Item,
        };
        world.Rows[("sprk_communicationattachment", linkedAttachment)] = new Entity("sprk_communicationattachment", linkedAttachment)
        {
            ["sprk_document"] = new EntityReference("sprk_document", DocumentId),
            ["sprk_graphdriveid"] = CustomerBContainer,
            ["sprk_graphitemid"] = Item,
        };
        var rig = new Rig(world);

        var outcome = await rig.Relocator.RelocateIfMisplacedAsync(DocumentId, apply: true);

        outcome.State.Should().Be(RelocationState.Relocated);
        world.Rows[("sprk_document", child)].GetAttributeValue<string>("sprk_parentgraphitemid").Should().Be(Rig.CopyItem);
        world.Rows[("sprk_document", strangersChild)].GetAttributeValue<string>("sprk_parentgraphitemid").Should().Be(Item,
            "another parent's child is not this document's reference");
        var attachment = world.Rows[("sprk_communicationattachment", linkedAttachment)];
        attachment.GetAttributeValue<string>("sprk_graphdriveid").Should().Be(CustomerA1Container);
        attachment.GetAttributeValue<string>("sprk_graphitemid").Should().Be(Rig.CopyItem);
        world.ItemFacts(CustomerBContainer, Item).Should().BeNull("the linked attachment row moved with the document, so nothing uses the source");
    }

    [Fact]
    public async Task ACommunicationsOwnAttachmentRecord_KeepsTheSource_ForThatCommunication()
    {
        var world = Environment();
        world.Rows[("sprk_document", DocumentId)] = Doc(drive: CustomerBContainer, item: Item);
        var unlinked = Guid.Parse("3f000000-0000-4000-8000-00000000f1a0");
        world.Rows[("sprk_communicationattachment", unlinked)] = new Entity("sprk_communicationattachment", unlinked)
        {
            ["sprk_graphdriveid"] = CustomerBContainer,
            ["sprk_graphitemid"] = Item,
        };
        var rig = new Rig(world);

        var result = await rig.Relocator.RelocateDocumentsAsync([DocumentId], CustomerA1Container, RelocationPurpose.LegacyMigration, apply: true);

        result.Complete.Should().BeTrue();
        result.SourceKeptForOtherRecords.Should().ContainSingle().Which.KeptFor.Should().Contain($"sprk_communicationattachment:{unlinked}");
        world.ItemFacts(CustomerBContainer, Item).Should().NotBeNull();
    }

    [Fact]
    public async Task Relocate_WhenAReKeyFails_IsPending_AndARepeatCallCompletesIt()
    {
        var world = Environment();
        world.Rows[("sprk_document", DocumentId)] = Doc(drive: CustomerBContainer, item: Item);
        var linkedAttachment = Guid.Parse("3e000000-0000-4000-8000-00000000f1a0");
        world.Rows[("sprk_communicationattachment", linkedAttachment)] = new Entity("sprk_communicationattachment", linkedAttachment)
        {
            ["sprk_document"] = new EntityReference("sprk_document", DocumentId),
            ["sprk_graphdriveid"] = CustomerBContainer,
            ["sprk_graphitemid"] = Item,
        };
        world.FaultQueriesOf = "sprk_communicationattachment";
        var rig = new Rig(world);

        var first = await rig.Relocator.RelocateDocumentsAsync([DocumentId], CustomerA1Container, RelocationPurpose.LegacyMigration, apply: true);

        first.Complete.Should().BeFalse();
        first.Incomplete.Should().ContainSingle().Which.Pending.Should().Contain(p => p.StartsWith("rekey-pending", StringComparison.Ordinal));
        world.ItemFacts(CustomerBContainer, Item).Should().NotBeNull("an unknown reference keeps the source");

        world.FaultQueriesOf = null;
        var second = await rig.Relocator.RelocateDocumentsAsync([DocumentId], CustomerA1Container, RelocationPurpose.LegacyMigration, apply: true);

        second.Complete.Should().BeTrue();
        world.Rows[("sprk_communicationattachment", linkedAttachment)].GetAttributeValue<string>("sprk_graphitemid").Should().Be(Rig.CopyItem);
        world.ItemFacts(CustomerBContainer, Item).Should().BeNull();
        rig.LedgerOf(DocumentId).Should().BeNull();
    }

    [Fact]
    public async Task Relocate_ReindexesTheNewItem_AndRemovesAllTheOldItemsChunks_OnceTheSourceIsGone()
    {
        var world = Environment();
        var row = Doc(drive: CustomerBContainer, item: Item);
        row["sprk_searchindexname"] = "spaarke-files-matter-index";
        world.Rows[("sprk_document", DocumentId)] = row;
        var rig = new Rig(world);

        await rig.Relocator.RelocateIfMisplacedAsync(DocumentId, apply: true);

        var call = rig.Indexing.Calls.Should().ContainSingle().Subject;
        call.DocumentId.Should().Be(DocumentId);
        call.DriveId.Should().Be(CustomerA1Container);
        call.ItemId.Should().Be(Rig.CopyItem);
        call.OldItemId.Should().Be(Item);
        call.OldItemRemoved.Should().BeTrue();
        call.PreviousSearchIndexName.Should().Be("spaarke-files-matter-index", "the old chunks are where the document was last indexed");
    }

    [Fact]
    public async Task Relocate_WhenTheIndexStepFails_IsIndexPending_Incomplete_AndARepeatCallRetries()
    {
        var world = Environment();
        world.Rows[("sprk_document", DocumentId)] = Doc(matter: SecureMatter, drive: CustomerAContainer, item: Item);
        var rig = new Rig(world);
        rig.Indexing.Answer = _ => RelocatedFileIndexOutcome.Pending("the re-index of the new item could not be enqueued (ServiceBusException)");

        var first = await rig.Relocator.RelocateDocumentsAsync([DocumentId], SecureContainer, RelocationPurpose.MakeSecure, apply: true);

        first.Complete.Should().BeFalse("round 37 item 1: an indexing failure counts as incomplete");
        first.Incomplete.Should().ContainSingle().Which.Pending.Should().Contain(p => p.StartsWith("index-pending", StringComparison.Ordinal));
        rig.ItemOf(DocumentId).Should().Be(Rig.CopyItem, "an indexing failure does not undo the move");

        rig.Indexing.Answer = _ => RelocatedFileIndexOutcome.Done("indexed");
        var second = await rig.Relocator.RelocateDocumentsAsync([DocumentId], SecureContainer, RelocationPurpose.MakeSecure, apply: true);

        second.Complete.Should().BeTrue();
        rig.Indexing.Calls.Should().HaveCount(2);
        rig.LedgerOf(DocumentId).Should().BeNull();
    }

    [Theory]
    [InlineData(RelocationState.Relocated, true)]
    [InlineData(RelocationState.RelocatedSourceKeptForOtherRecords, true)]
    [InlineData(RelocationState.InPlace, true)]
    [InlineData(RelocationState.NoFile, true)]
    [InlineData(RelocationState.RelocationPending, false)]
    [InlineData(RelocationState.WouldRelocate, false)]
    [InlineData(RelocationState.SourceUnverified, false)]
    [InlineData(RelocationState.Undecidable, false)]
    [InlineData(RelocationState.FileMissing, false)]
    [InlineData(RelocationState.Failed, false)]
    public void ABatchIsComplete_OnlyWhenEveryFileIsSettled(RelocationState state, bool settled)
    {
        DocumentRelocationBatchResult.IsSettledState(state).Should().Be(settled);
        DocumentRelocationBatchResult.IsSettled(DocumentRelocationOutcome.Of(DocumentId, state, null, null, null, null, "x")
            with { Pending = ["index-pending: x"] }).Should().BeFalse("a row that still owes anything is never settled");
    }

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

    [Fact]
    public void TheRepeatCallsDeletionTest_NeedsTheSameSizeAndHash_BothPresent()
    {
        var file = new SpeItemCreator("f", null, null, 10, "h");

        DocumentContainerRelocator.IsByteIdentical(file, file).Should().BeTrue();
        DocumentContainerRelocator.IsByteIdentical(file, file with { QuickXorHash = null }).Should().BeFalse();
        DocumentContainerRelocator.IsByteIdentical(file with { QuickXorHash = null }, file with { QuickXorHash = null }).Should().BeFalse();
        DocumentContainerRelocator.IsByteIdentical(file, file with { Size = 11 }).Should().BeFalse();
        DocumentContainerRelocator.IsByteIdentical(file, file with { QuickXorHash = "x" }).Should().BeFalse();
        DocumentContainerRelocator.IsByteIdentical(file, null).Should().BeFalse();
    }

    /// <summary>A stored ledger owing one source (pending delete, re-key and index).</summary>
    internal static string LedgerJson(string sourceDrive, string sourceItem)
        => $$"""{"v":1,"entries":[{"sourceDrive":"{{sourceDrive}}","sourceItem":"{{sourceItem}}","source":"pending","rekeyPending":true,"indexed":"none","at":"2026-10-05T00:00:00+00:00"}]}""";
}
