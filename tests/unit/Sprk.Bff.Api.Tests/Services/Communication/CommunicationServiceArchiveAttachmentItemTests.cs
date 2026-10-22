using Azure.Messaging.ServiceBus;
using FluentAssertions;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Models;
using Sprk.Bff.Api.Services.Communication;
using Sprk.Bff.Api.Services.Communication.Channels;
using Sprk.Bff.Api.Services.Jobs;
using Sprk.Bff.Api.Tests.TestInfrastructure;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Communication;

/// <summary>
/// #2246: the on-demand archive (<see cref="CommunicationService.ArchiveExistingAsync"/>) creates a <c>sprk_document</c> for
/// an attachment row that has an item but no document. The row's <c>sprk_graphitemid</c> / <c>sprk_graphdriveid</c> are
/// writable by a user or an app-only generic writer, so the archive must verify the item against server-trusted facts
/// (Graph's own record of the item, the communication's derived container) before binding a document's pointer and its
/// field-secured bound copy to it. Otherwise a foreign item is laundered through the binding check.
/// </summary>
/// <remarks>
/// Module-boundary doubles only (ADR-038 §4): the Dataverse writer (<see cref="IGenericEntityService"/>), the job queue and
/// the Graph item read behind the REAL <see cref="RecordContainerResolver"/> (task 166's pointer world). The communication
/// is already archived, so only the attachment step runs.
/// </remarks>
public class CommunicationServiceArchiveAttachmentItemTests
{
    private const string ArchiveContainer = "drive-archive";
    private const string AttachmentEntity = "sprk_communicationattachment";
    private const string DocumentEntity = "sprk_document";
    private const string SecureContainer = "drive-secure-matter";

    private static readonly Guid SecureMatter = Guid.Parse("2246f100-0000-4000-8000-000000002248");

    private static readonly string BffApp = TestRecordContainerResolver.PointerWorldBffApplicationId.ToString("D");

    private sealed class Harness
    {
        public required CommunicationService Sut { get; init; }
        public required Guid CommunicationId { get; init; }
        public required List<Entity> Created { get; init; }
        public required List<(string Entity, Guid Id, Dictionary<string, object> Fields)> Updates { get; init; }
        public required List<JobContract> Jobs { get; init; }

        /// <summary>How many "does a document already name this item?" lookups the archive made.</summary>
        public Func<int> BoundLookups { get; init; } = () => 0;

        /// <summary>How many times the resolver read the communication row (its container derivation).</summary>
        public Func<int> CommunicationReads { get; init; } = () => 0;
    }

    /// <summary>An item the archive wrote for <paramref name="communicationId"/>: its name prefix, uploaded by the BFF.</summary>
    private static SpeItemCreator ArchiveItem(Guid communicationId, string name)
        => new($"{communicationId:N}_{name}", UserObjectId: null, ApplicationId: BffApp);

    private static Entity AttachmentRow(string name, string? drive, string item)
    {
        var row = new Entity(AttachmentEntity, Guid.NewGuid()) { ["sprk_name"] = name, ["sprk_graphitemid"] = item };
        if (drive is not null)
        {
            row["sprk_graphdriveid"] = drive;
        }

        return row;
    }

    [Fact]
    public async Task AGenuineAttachmentItem_IsArchivedAsBefore()
    {
        // must-not-fire: the item is in the communication's container, uploaded by the BFF, named for THIS communication.
        var h = Build((world, c) => world.Items[(ArchiveContainer, "item-own")] = ArchiveItem(c, "contract.pdf"),
            AttachmentRow("contract.pdf", ArchiveContainer, "item-own"));

        var result = await h.Sut.ArchiveExistingAsync(h.CommunicationId, CancellationToken.None);

        result.AttachmentDocumentsCreated.Should().Be(1);
        result.AttachmentsRefusedUnverified.Should().Be(0);
        var doc = h.Created.Should().ContainSingle().Subject;
        doc.GetAttributeValue<string>("sprk_graphitemid").Should().Be("item-own");
        doc.GetAttributeValue<string>(DocumentPointerBinding.BoundItemIdColumn).Should().Be("item-own");
        doc.GetAttributeValue<string>("sprk_graphdriveid").Should().Be(ArchiveContainer);
        doc.GetAttributeValue<EntityReference>("ownerid").Should().NotBeNull("task 146 owner resolution still applies");
        h.Updates.Should().ContainSingle(u => u.Entity == AttachmentEntity, "the document is linked back for idempotency");
        h.Jobs.Should().ContainSingle("the Document Profile job is still enqueued");
    }

    [Fact]
    public async Task AnAttachmentRowWithNoDrive_FallsBackToTheArchiveContainer_AndIsStillVerified()
    {
        var h = Build((world, c) => world.Items[(ArchiveContainer, "item-own")] = ArchiveItem(c, "scan.pdf"),
            AttachmentRow("scan.pdf", drive: null, "item-own"));

        var result = await h.Sut.ArchiveExistingAsync(h.CommunicationId, CancellationToken.None);

        result.AttachmentDocumentsCreated.Should().Be(1);
        h.Created.Single().GetAttributeValue<string>("sprk_graphdriveid").Should().Be(ArchiveContainer);
    }

    [Fact]
    public async Task AnotherCommunicationsAttachmentItem_InTheSharedArchiveContainer_IsNotArchived_AndIsCounted()
    {
        // must-fire: the #2246 scenario. The row was re-pointed at communication D's attachment in the shared container.
        var other = Guid.NewGuid();
        var h = Build((world, c) =>
            {
                world.Items[(ArchiveContainer, "item-own")] = ArchiveItem(c, "mine.pdf");
                world.Items[(ArchiveContainer, "item-foreign")] = ArchiveItem(other, "privileged.pdf");
            },
            AttachmentRow("mine.pdf", ArchiveContainer, "item-own"),
            AttachmentRow("privileged.pdf", ArchiveContainer, "item-foreign"));

        var result = await h.Sut.ArchiveExistingAsync(h.CommunicationId, CancellationToken.None);

        result.AttachmentDocumentsCreated.Should().Be(1);
        result.AttachmentsRefusedUnverified.Should().Be(1);
        h.Created.Should().NotContain(d => d.GetAttributeValue<string>("sprk_graphitemid") == "item-foreign");
        h.Created.Should().NotContain(d => d.GetAttributeValue<string>(DocumentPointerBinding.BoundItemIdColumn) == "item-foreign",
            "a foreign item is never bound to a new document");
        h.Updates.Should().ContainSingle("only the verified attachment is linked back");
        h.Jobs.Should().ContainSingle();
    }

    [Fact]
    public async Task AnItemInADriveThatIsNotTheCommunicationsContainer_IsNotArchived_AndIsCounted()
    {
        // must-fire: right name, uploaded by the BFF, but in another container than the one derived for the communication.
        var h = Build((world, c) => world.Items[("drive-elsewhere", "item-x")] = ArchiveItem(c, "contract.pdf"),
            AttachmentRow("contract.pdf", "drive-elsewhere", "item-x"));

        var result = await h.Sut.ArchiveExistingAsync(h.CommunicationId, CancellationToken.None);

        result.AttachmentDocumentsCreated.Should().Be(0);
        result.AttachmentsRefusedUnverified.Should().Be(1);
        h.Created.Should().BeEmpty();
    }

    [Fact]
    public async Task AnItemThatDoesNotExistInTheDrive_IsNotArchived_AndIsCounted()
    {
        // must-fire: Graph reports no such item in the drive the row names.
        var h = Build((world, _) => world.Items[(ArchiveContainer, "item-gone")] = null,
            AttachmentRow("gone.pdf", ArchiveContainer, "item-gone"));

        var result = await h.Sut.ArchiveExistingAsync(h.CommunicationId, CancellationToken.None);

        result.AttachmentDocumentsCreated.Should().Be(0);
        result.AttachmentsRefusedUnverified.Should().Be(1);
        h.Created.Should().BeEmpty();
    }

    [Fact]
    public async Task AnItemAPersonUploaded_WithThisCommunicationsPrefix_IsNotArchived()
    {
        // must-fire: a person with access to the container named a file like the archive would; the archive did not write it.
        var h = Build((world, c) => world.Items[(ArchiveContainer, "item-planted")] =
                new SpeItemCreator($"{c:N}_planted.pdf", TestRecordContainerResolver.PointerWorldCreatorObjectId.ToString("D"), BffApp),
            AttachmentRow("planted.pdf", ArchiveContainer, "item-planted"));

        var result = await h.Sut.ArchiveExistingAsync(h.CommunicationId, CancellationToken.None);

        result.AttachmentDocumentsCreated.Should().Be(0);
        result.AttachmentsRefusedUnverified.Should().Be(1);
        h.Created.Should().BeEmpty();
    }

    [Fact]
    public async Task WhenTheVerificationReadFails_NothingIsCreated()
    {
        // fail-closed: Graph is unavailable.
        var h = Build((world, c) => world.Items[(ArchiveContainer, "item-own")] = ArchiveItem(c, "contract.pdf"),
            itemReadFault: new HttpRequestException("Graph unavailable"),
            rows: AttachmentRow("contract.pdf", ArchiveContainer, "item-own"));

        var result = await h.Sut.ArchiveExistingAsync(h.CommunicationId, CancellationToken.None);

        result.AttachmentDocumentsCreated.Should().Be(0);
        result.AttachmentsRefusedUnverified.Should().Be(1);
        h.Created.Should().BeEmpty();
        h.Updates.Should().BeEmpty();
        h.Jobs.Should().BeEmpty();
    }

    [Fact]
    public async Task WhenNoVerifierIsAvailable_NothingIsCreated()
    {
        // fail-closed: a host that wires no scope factory cannot verify, so it archives nothing.
        var h = Build((world, c) => world.Items[(ArchiveContainer, "item-own")] = ArchiveItem(c, "contract.pdf"),
            noScopeFactory: true,
            rows: AttachmentRow("contract.pdf", ArchiveContainer, "item-own"));

        var result = await h.Sut.ArchiveExistingAsync(h.CommunicationId, CancellationToken.None);

        result.AttachmentDocumentsCreated.Should().Be(0);
        result.AttachmentsRefusedUnverified.Should().Be(1);
        h.Created.Should().BeEmpty();
    }

    [Fact]
    public async Task AnItemAnotherApplicationWrote_WithThisCommunicationsPrefix_IsNotArchived()
    {
        // must-fire: right container, right name, but app-only by an application that is not the BFF.
        var otherApp = Guid.NewGuid().ToString("D");
        var h = Build((world, c) => world.Items[(ArchiveContainer, "item-otherapp")] =
                new SpeItemCreator($"{c:N}_contract.pdf", UserObjectId: null, ApplicationId: otherApp),
            AttachmentRow("contract.pdf", ArchiveContainer, "item-otherapp"));

        var result = await h.Sut.ArchiveExistingAsync(h.CommunicationId, CancellationToken.None);

        result.AttachmentDocumentsCreated.Should().Be(0);
        result.AttachmentsRefusedUnverified.Should().Be(1);
        h.Created.Should().BeEmpty();
    }

    [Fact]
    public async Task ACommunicationOfASecureRecord_ArchivesItsItemInTheSecureContainer_AndRefusesOneInTheSharedArchive()
    {
        // The derivation is the secure record's own container, not Communication:ArchiveContainerId.
        var h = Build((world, c) =>
            {
                world.SecureClaims[SecureContainer] = ("sprk_matter", SecureMatter);
                world.Rows[("sprk_matter", SecureMatter)] = new Entity("sprk_matter", SecureMatter)
                {
                    ["sprk_issecure"] = true,
                    ["sprk_containerid"] = SecureContainer,
                    ["owningbusinessunit"] = new EntityReference("businessunit", TestRecordContainerResolver.PointerWorldRootBusinessUnit),
                };
                world.Rows[("sprk_communication", c)]["sprk_regardingmatter"] = new EntityReference("sprk_matter", SecureMatter);
                world.Items[(SecureContainer, "item-secure")] = ArchiveItem(c, "privileged.pdf");
                world.Items[(ArchiveContainer, "item-shared")] = ArchiveItem(c, "copy.pdf");
            },
            AttachmentRow("privileged.pdf", SecureContainer, "item-secure"),
            AttachmentRow("copy.pdf", ArchiveContainer, "item-shared"));

        var result = await h.Sut.ArchiveExistingAsync(h.CommunicationId, CancellationToken.None);

        result.AttachmentDocumentsCreated.Should().Be(1);
        result.AttachmentsRefusedUnverified.Should().Be(1, "the shared archive is not a secure record's communication's container");
        h.Created.Should().ContainSingle().Which.GetAttributeValue<string>("sprk_graphdriveid").Should().Be(SecureContainer);
    }

    // ── Fix round 1 (K3): an item a document already names is never bound again ───────────────────────────────────

    [Fact]
    public async Task AnItemAlreadyBoundToTheCommunicationsEml_IsNotArchived_AndIsCounted()
    {
        // must-fire: the row points at C's own .eml item. It passes the item check (C's container, the BFF, C's prefix),
        // but the .eml document already names it: a second document on it would delete the .eml from under the first.
        // Fix round 2: the .eml document is C's own, unlinked, pointer and bound copy both naming the item; only its source
        // type (Email Archive) keeps the repair path from linking the row to it.
        var h = Build((world, c) => world.Items[(ArchiveContainer, "item-eml")] = ArchiveItem(c, "Re- Q4 numbers.eml"),
            existingDocuments: c => new[] { Document(c, "item-eml", "item-eml", EmailArchiveSourceType) },
            rows: AttachmentRow("Re- Q4 numbers.eml", ArchiveContainer, "item-eml"));

        var result = await h.Sut.ArchiveExistingAsync(h.CommunicationId, CancellationToken.None);

        result.AttachmentDocumentsCreated.Should().Be(0);
        result.AttachmentsRefusedUnverified.Should().Be(1);
        h.Created.Should().BeEmpty();
        h.Updates.Should().BeEmpty();
        h.Jobs.Should().BeEmpty();
    }

    [Fact]
    public async Task TwoRowsNamingTheSameItem_BindOneDocument_AndTheSiblingIsRefused()
    {
        // must-fire: a sibling row re-pointed at an item the first row's new document now names.
        var h = Build((world, c) => world.Items[(ArchiveContainer, "item-own")] = ArchiveItem(c, "contract.pdf"),
            AttachmentRow("contract.pdf", ArchiveContainer, "item-own"),
            AttachmentRow("contract (copy).pdf", ArchiveContainer, "item-own"));

        var result = await h.Sut.ArchiveExistingAsync(h.CommunicationId, CancellationToken.None);

        result.AttachmentDocumentsCreated.Should().Be(1);
        result.AttachmentsRefusedUnverified.Should().Be(1);
        h.Created.Should().ContainSingle("one file, one document");
    }

    [Fact]
    public async Task ASiblingRowAlreadyLinkedBeforeTheArchive_KeepsItsDocument_AndARowRepointedAtItIsRefused()
    {
        // must-fire (re-check F1): row A1 was linked to D1 (item-own) by an EARLIER archive; sibling A2 is re-pointed at
        // item-own. D1 otherwise passes every repair condition, so only the rows linked at load time keep A2 off it.
        var d1Id = Guid.NewGuid();
        var linkedRow = AttachmentRow("contract.pdf", ArchiveContainer, "item-own");
        linkedRow["sprk_document"] = new EntityReference(DocumentEntity, d1Id);
        var h = Build((world, c) => world.Items[(ArchiveContainer, "item-own")] = ArchiveItem(c, "contract.pdf"),
            existingDocuments: c =>
            {
                var d1 = Document(c, "item-own", "item-own", EmailAttachmentSourceType);
                d1.Id = d1Id;
                return new[] { d1 };
            },
            rows: new[] { linkedRow, AttachmentRow("contract (copy).pdf", ArchiveContainer, "item-own") });

        var result = await h.Sut.ArchiveExistingAsync(h.CommunicationId, CancellationToken.None);

        result.AttachmentDocumentsCreated.Should().Be(0);
        result.AttachmentsRefusedUnverified.Should().Be(1);
        h.Created.Should().BeEmpty();
        h.Updates.Should().BeEmpty("A2 is not linked to the document A1 already owns");
    }

    [Fact]
    public async Task TwoDocumentsNamingTheItem_AreNotARepair_AndTheRowIsRefused()
    {
        // must-fire (re-check F2): C's own clean attachment document AND a second document whose bound copy names the item.
        // Only exactly one match is a repair.
        var h = Build((world, c) => world.Items[(ArchiveContainer, "item-own")] = ArchiveItem(c, "contract.pdf"),
            existingDocuments: c => new[]
            {
                Document(c, "item-own", "item-own", EmailAttachmentSourceType),
                Document(Guid.NewGuid(), "item-elsewhere", "item-own", EmailAttachmentSourceType),
            },
            rows: AttachmentRow("contract.pdf", ArchiveContainer, "item-own"));

        var result = await h.Sut.ArchiveExistingAsync(h.CommunicationId, CancellationToken.None);

        result.AttachmentDocumentsCreated.Should().Be(0);
        result.AttachmentsRefusedUnverified.Should().Be(1);
        h.Created.Should().BeEmpty();
        h.Updates.Should().BeEmpty();
    }

    [Fact]
    public async Task ADeactivatedDocumentOnTheItem_IsNotARepair_AndTheRowIsRefused()
    {
        // must-fire: C's own attachment document on the item, but Inactive. A row is never re-linked to it.
        var h = Build((world, c) => world.Items[(ArchiveContainer, "item-own")] = ArchiveItem(c, "contract.pdf"),
            existingDocuments: c => new[] { Document(c, "item-own", "item-own", EmailAttachmentSourceType, state: 1) },
            rows: AttachmentRow("contract.pdf", ArchiveContainer, "item-own"));

        var result = await h.Sut.ArchiveExistingAsync(h.CommunicationId, CancellationToken.None);

        result.AttachmentDocumentsCreated.Should().Be(0);
        result.AttachmentsRefusedUnverified.Should().Be(1);
        h.Updates.Should().BeEmpty();
    }

    [Fact]
    public async Task WhenTheAlreadyBoundLookupFails_NothingIsCreated()
    {
        // fail-closed: Dataverse cannot say whether a document already names the item.
        var h = Build((world, c) => world.Items[(ArchiveContainer, "item-own")] = ArchiveItem(c, "contract.pdf"),
            boundLookupFault: new InvalidOperationException("Dataverse unavailable"),
            rows: AttachmentRow("contract.pdf", ArchiveContainer, "item-own"));

        var result = await h.Sut.ArchiveExistingAsync(h.CommunicationId, CancellationToken.None);

        result.AttachmentDocumentsCreated.Should().Be(0);
        result.AttachmentsRefusedUnverified.Should().Be(1);
        h.Created.Should().BeEmpty();
    }

    // ── Fix round 1 (K4): cost and cancellation ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task TheCommunicationsContainer_IsDerivedOnce_ForSeveralAttachments()
    {
        var h = Build((world, c) =>
            {
                world.Items[(ArchiveContainer, "item-1")] = ArchiveItem(c, "one.pdf");
                world.Items[(ArchiveContainer, "item-2")] = ArchiveItem(c, "two.pdf");
                world.Items[(ArchiveContainer, "item-3")] = ArchiveItem(c, "three.pdf");
            },
            AttachmentRow("one.pdf", ArchiveContainer, "item-1"),
            AttachmentRow("two.pdf", ArchiveContainer, "item-2"),
            AttachmentRow("three.pdf", ArchiveContainer, "item-3"));

        var result = await h.Sut.ArchiveExistingAsync(h.CommunicationId, CancellationToken.None);

        result.AttachmentDocumentsCreated.Should().Be(3);
        h.CommunicationReads().Should().Be(1, "the container is the communication's, the same for every attachment");
    }

    [Fact]
    public async Task WithNothingToArchive_TheContainerIsNotDerived()
    {
        var linked = AttachmentRow("done.pdf", ArchiveContainer, "item-done");
        linked["sprk_document"] = new EntityReference(DocumentEntity, Guid.NewGuid());
        var h = Build((_, _) => { }, linked);

        var result = await h.Sut.ArchiveExistingAsync(h.CommunicationId, CancellationToken.None);

        result.AttachmentDocumentsCreated.Should().Be(0);
        result.AttachmentsRefusedUnverified.Should().Be(0);
        h.CommunicationReads().Should().Be(0);
    }

    [Fact]
    public async Task TheCallersCancellation_StopsTheLoop_AndPropagates()
    {
        // The caller aborts while the first attachment is being archived; the rest are not attempted.
        using var cts = new CancellationTokenSource();
        var h = Build((world, c) =>
            {
                world.Items[(ArchiveContainer, "item-1")] = ArchiveItem(c, "one.pdf");
                world.Items[(ArchiveContainer, "item-2")] = ArchiveItem(c, "two.pdf");
                world.Items[(ArchiveContainer, "item-3")] = ArchiveItem(c, "three.pdf");
            },
            onCreate: cts.Cancel,
            rows: new[]
            {
                AttachmentRow("one.pdf", ArchiveContainer, "item-1"),
                AttachmentRow("two.pdf", ArchiveContainer, "item-2"),
                AttachmentRow("three.pdf", ArchiveContainer, "item-3"),
            });

        var act = () => h.Sut.ArchiveExistingAsync(h.CommunicationId, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        h.Created.Should().ContainSingle("the loop stops at the caller's cancellation");
        h.BoundLookups().Should().Be(2, "the second attachment's lookup observed the cancellation; the third was never tried");

        // Fix round 2 (F1, F3): the link-back after the create is not the caller's to cancel (the Update double honours
        // its token, as Dataverse does), so the created document is linked, not orphaned.
        h.Updates.Should().ContainSingle().Which.Fields["sprk_document"].Should().BeOfType<EntityReference>()
            .Which.Id.Should().Be(h.Created.Single().Id);
    }

    // ── Fix round 2 (F1): the row's ids are normalised once, trimmed ────────────────────────────────────────────

    [Theory]
    [InlineData(" item-own")]
    [InlineData("item-own\t")]
    public async Task APaddedItemId_OfAnItemADocumentAlreadyNames_IsRefused(string paddedItem)
    {
        // must-fire: the verifier trims, so a padded id verifies on item-own; the lookup must look for item-own too.
        var h = Build((world, c) => world.Items[(ArchiveContainer, "item-own")] = ArchiveItem(c, "contract.pdf"),
            existingDocuments: _ => new[] { Document(Guid.NewGuid(), "item-own", "item-own", EmailAttachmentSourceType) },
            rows: AttachmentRow("contract.pdf", ArchiveContainer, paddedItem));

        var result = await h.Sut.ArchiveExistingAsync(h.CommunicationId, CancellationToken.None);

        result.AttachmentDocumentsCreated.Should().Be(0);
        result.AttachmentsRefusedUnverified.Should().Be(1);
        h.Created.Should().BeEmpty("a second document on item-own would delete it from under the first");
        h.Updates.Should().BeEmpty();
    }

    [Theory]
    [InlineData(" item-own", " " + ArchiveContainer)]
    [InlineData("item-own\t", ArchiveContainer + "\t")]
    public async Task APaddedItemId_OfAGenuineUnboundItem_IsArchived_WithTheTrimmedPointer(string paddedItem, string paddedDrive)
    {
        // must-not-fire: a genuine item is still archived, and the stored pointer, bound copy and drive are trimmed.
        var h = Build((world, c) => world.Items[(ArchiveContainer, "item-own")] = ArchiveItem(c, "contract.pdf"),
            AttachmentRow("contract.pdf", paddedDrive, paddedItem));

        var result = await h.Sut.ArchiveExistingAsync(h.CommunicationId, CancellationToken.None);

        result.AttachmentDocumentsCreated.Should().Be(1);
        result.AttachmentsRefusedUnverified.Should().Be(0);
        var doc = h.Created.Should().ContainSingle().Subject;
        doc.GetAttributeValue<string>("sprk_graphitemid").Should().Be("item-own");
        doc.GetAttributeValue<string>(DocumentPointerBinding.BoundItemIdColumn).Should().Be("item-own");
        doc.GetAttributeValue<string>("sprk_graphdriveid").Should().Be(ArchiveContainer);
    }

    [Fact]
    public async Task AnItemIdOfOnlyWhitespace_IsRefused_AndCounted()
    {
        var h = Build((_, _) => { }, AttachmentRow("blank.pdf", ArchiveContainer, " \t "));

        var result = await h.Sut.ArchiveExistingAsync(h.CommunicationId, CancellationToken.None);

        result.AttachmentDocumentsCreated.Should().Be(0);
        result.AttachmentsRefusedUnverified.Should().Be(1);
        h.Created.Should().BeEmpty();
    }

    // ── Fix round 2 (F1): a partial archive is repaired, not refused ─────────────────────────────────────────────

    [Fact]
    public async Task ARetryAfterAFailedLinkBack_LinksTheExistingDocument_AndIsNotRefused()
    {
        // The first archive creates the document; its link-back to the row fails (a transient fault, non-fatal).
        var updateCalls = 0;
        var h = Build((world, c) => world.Items[(ArchiveContainer, "item-own")] = ArchiveItem(c, "contract.pdf"),
            updateFault: () => ++updateCalls == 1 ? new InvalidOperationException("Dataverse transient fault") : null,
            rows: AttachmentRow("contract.pdf", ArchiveContainer, "item-own"));

        var first = await h.Sut.ArchiveExistingAsync(h.CommunicationId, CancellationToken.None);
        first.AttachmentDocumentsCreated.Should().Be(0, "the link-back failed");
        h.Created.Should().ContainSingle();
        h.Updates.Should().BeEmpty();

        var retry = await h.Sut.ArchiveExistingAsync(h.CommunicationId, CancellationToken.None);

        retry.AttachmentDocumentsCreated.Should().Be(1, "the row is linked to the document the first archive created");
        retry.AttachmentsRefusedUnverified.Should().Be(0);
        h.Created.Should().ContainSingle("nothing new is created");
        h.Updates.Should().ContainSingle().Which.Fields["sprk_document"].Should().BeOfType<EntityReference>()
            .Which.Id.Should().Be(h.Created.Single().Id);
        h.Jobs.Should().ContainSingle("the first archive stopped before its enqueue; the repair profiles the document")
            .Which.SubjectId.Should().Be(h.Created.Single().Id.ToString());
    }

    [Fact]
    public async Task AMatchOnAnotherCommunicationsAttachmentDocument_IsStillRefused()
    {
        // must-fire: same item, attachment source type, pointer and bound copy intact, but another communication's.
        var h = Build((world, c) => world.Items[(ArchiveContainer, "item-own")] = ArchiveItem(c, "contract.pdf"),
            existingDocuments: _ => new[] { Document(Guid.NewGuid(), "item-own", "item-own", EmailAttachmentSourceType) },
            rows: AttachmentRow("contract.pdf", ArchiveContainer, "item-own"));

        var result = await h.Sut.ArchiveExistingAsync(h.CommunicationId, CancellationToken.None);

        result.AttachmentDocumentsCreated.Should().Be(0);
        result.AttachmentsRefusedUnverified.Should().Be(1);
        h.Created.Should().BeEmpty();
        h.Updates.Should().BeEmpty("the row is not linked to another communication's document");
    }

    // ── Fix round 2 (K): the lookup also matches the bound copy ──────────────────────────────────────────────────

    [Fact]
    public async Task ADocumentWhosePointerWasMovedButWhoseBoundCopyNamesTheItem_IsCaught_AndRefused()
    {
        // must-fire: C's own attachment document, its pointer re-pointed elsewhere, its field-secured bound copy still
        // naming item-own. It names the item, so the item is not bound again; and it is not a clean partial archive.
        var h = Build((world, c) => world.Items[(ArchiveContainer, "item-own")] = ArchiveItem(c, "contract.pdf"),
            existingDocuments: c => new[] { Document(c, "item-elsewhere", "item-own", EmailAttachmentSourceType) },
            rows: AttachmentRow("contract.pdf", ArchiveContainer, "item-own"));

        var result = await h.Sut.ArchiveExistingAsync(h.CommunicationId, CancellationToken.None);

        result.AttachmentDocumentsCreated.Should().Be(0);
        result.AttachmentsRefusedUnverified.Should().Be(1);
        h.Created.Should().BeEmpty();
        h.Updates.Should().BeEmpty();
    }

    // ── harness ─────────────────────────────────────────────────────────────────────────────────────────────────

    private static Harness Build(
        Action<TestRecordContainerResolver.DocumentPointerWorld, Guid> arrange, params Entity[] rows)
        => Build(arrange, itemReadFault: null, noScopeFactory: false, rows: rows);

    private static Harness Build(
        Action<TestRecordContainerResolver.DocumentPointerWorld, Guid> arrange,
        Exception? itemReadFault = null,
        bool noScopeFactory = false,
        Func<Guid, Entity[]>? existingDocuments = null,
        Exception? boundLookupFault = null,
        Action? onCreate = null,
        Func<Exception?>? updateFault = null,
        params Entity[] rows)
    {
        var communicationId = Guid.NewGuid();

        // The REAL resolver: a communication regarding nothing derives to Communication:ArchiveContainerId.
        var world = new TestRecordContainerResolver.DocumentPointerWorld
        {
            ArchiveContainerId = ArchiveContainer,
            ItemReadFault = itemReadFault,
        };
        world.Rows[("sprk_communication", communicationId)] = new Entity("sprk_communication", communicationId);
        arrange(world, communicationId);
        var communicationReads = 0;
        world.RetrieveFault = (entity, _) =>
        {
            if (entity == "sprk_communication")
            {
                communicationReads++;
            }

            return null;
        };
        var resolver = world.Build();

        var created = new List<Entity>();
        var updates = new List<(string, Guid, Dictionary<string, object>)>();
        var entityService = new Mock<IGenericEntityService>();
        entityService
            .Setup(s => s.RetrieveAsync("sprk_communication", communicationId, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Entity("sprk_communication", communicationId));
        entityService
            .Setup(s => s.RetrieveMultipleAsync(It.Is<QueryExpression>(q => q.EntityName == AttachmentEntity), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EntityCollection(rows.ToList()));
        // Already archived: the .eml step is skipped, so only the attachment step runs.
        entityService
            .Setup(s => s.RetrieveMultipleAsync(
                It.Is<QueryExpression>(q => q.EntityName == DocumentEntity && !NamesItem(q)), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EntityCollection(new List<Entity> { new(DocumentEntity, Guid.NewGuid()) }));
        // Fix round 1 (K3): "does a document already name this item?" — the seeded documents plus the documents created,
        // evaluated as Dataverse would: the query's conditions under its filter operator, projected to its column set.
        var seeded = existingDocuments?.Invoke(communicationId) ?? Array.Empty<Entity>();
        var boundLookups = 0;
        entityService
            .Setup(s => s.RetrieveMultipleAsync(
                It.Is<QueryExpression>(q => q.EntityName == DocumentEntity && NamesItem(q)), It.IsAny<CancellationToken>()))
            .ReturnsAsync((QueryExpression q, CancellationToken token) =>
            {
                boundLookups++;
                token.ThrowIfCancellationRequested();
                if (boundLookupFault is not null)
                {
                    throw boundLookupFault;
                }

                var hits = seeded.Concat(created)
                    .Where(d => Satisfies(q.Criteria, d))
                    .Take(q.TopCount ?? int.MaxValue)
                    .Select(d => Project(q.ColumnSet, d))
                    .ToList();
                return new EntityCollection(hits);
            });
        entityService
            .Setup(s => s.CreateAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Entity e, CancellationToken token) =>
            {
                token.ThrowIfCancellationRequested();
                e.Id = Guid.NewGuid();
                e["statecode"] = new OptionSetValue(0); // Dataverse creates a row Active
                created.Add(e);
                onCreate?.Invoke();
                return e.Id;
            });
        // Fix round 2 (F3): the Update double honours its token, as Dataverse does, and a written link is kept on the row
        // so a later archive skips it.
        entityService
            .Setup(s => s.UpdateAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<Dictionary<string, object>>(), It.IsAny<CancellationToken>()))
            .Returns((string entity, Guid id, Dictionary<string, object> fields, CancellationToken token) =>
            {
                token.ThrowIfCancellationRequested();
                if (updateFault?.Invoke() is { } fault)
                {
                    throw fault;
                }

                updates.Add((entity, id, fields));
                if (entity == AttachmentEntity && rows.FirstOrDefault(r => r.Id == id) is { } row)
                {
                    foreach (var (column, value) in fields)
                    {
                        row[column] = value;
                    }
                }

                return Task.CompletedTask;
            });

        var options = new CommunicationOptions
        {
            ApprovedSenders = new[]
            {
                new ApprovedSenderConfig { Email = "noreply@contoso.com", DisplayName = "Contoso", IsDefault = true }
            },
            DefaultMailbox = "noreply@contoso.com",
            ArchiveContainerId = ArchiveContainer,
        };
        var accountService = new CommunicationAccountService(
            Mock.Of<IDataverseService>(), Mock.Of<IDataverseService>(), Mock.Of<IDistributedCache>(),
            Mock.Of<ILogger<CommunicationAccountService>>());
        var senderValidator = new ApprovedSenderValidator(
            Microsoft.Extensions.Options.Options.Create(options), accountService, Mock.Of<IDistributedCache>(),
            Mock.Of<ILogger<ApprovedSenderValidator>>());
        var dispatcher = new CommunicationChannelDispatcher(
            Array.Empty<ICommunicationChannelSender>(), Array.Empty<ICommunicationArchiver>());

        var jobs = new List<JobContract>();
        var sut = new CommunicationService(
            dispatcher,
            senderValidator,
            Mock.Of<ICommunicationDataverseService>(),
            entityService.Object,
            Mock.Of<IDocumentDataverseService>(),
            accountService,
            BuildJobQueue(jobs),
            Mock.Of<ICommunicationEnrichmentService>(),
            Microsoft.Extensions.Options.Options.Create(options),
            CoreAncestorResolverFixtures.Inert(),
            new RecordOwnershipResolverDouble(),
            Mock.Of<ILogger<CommunicationService>>(),
            scopeFactory: noScopeFactory ? null : ScopeServing(resolver));

        return new Harness
        {
            Sut = sut, CommunicationId = communicationId, Created = created, Updates = updates, Jobs = jobs,
            CommunicationReads = () => communicationReads,
            BoundLookups = () => boundLookups,
        };
    }

    private static bool NamesItem(QueryExpression query)
        => query.Criteria.Conditions.Any(c => c.AttributeName == "sprk_graphitemid");

    private const int EmailArchiveSourceType = 659490003;
    private const int EmailAttachmentSourceType = 659490004;

    /// <summary>An existing <c>sprk_document</c>: its communication, pointer, field-secured bound copy and source type.</summary>
    private static Entity Document(Guid communicationId, string pointer, string boundCopy, int sourceType, int state = 0)
        => new(DocumentEntity, Guid.NewGuid())
        {
            ["statecode"] = new OptionSetValue(state),
            ["sprk_relatedcommunication"] = new EntityReference("sprk_communication", communicationId),
            ["sprk_graphitemid"] = pointer,
            [DocumentPointerBinding.BoundItemIdColumn] = boundCopy,
            ["sprk_sourcetype"] = new OptionSetValue(sourceType),
        };

    /// <summary>A flat filter of string Equal conditions, evaluated under its own operator (And / Or).</summary>
    private static bool Satisfies(FilterExpression filter, Entity document)
    {
        filter.Filters.Should().BeEmpty("the lookup is one flat filter");
        var matches = filter.Conditions.Select(c =>
        {
            c.Operator.Should().Be(ConditionOperator.Equal);
            return string.Equals(document.GetAttributeValue<string>(c.AttributeName), (string)c.Values[0], StringComparison.Ordinal);
        }).ToList();
        return filter.FilterOperator == LogicalOperator.Or ? matches.Any(m => m) : matches.All(m => m);
    }

    /// <summary>Only the columns the query asked for, as Dataverse returns them.</summary>
    private static Entity Project(ColumnSet columns, Entity document)
    {
        var projected = new Entity(DocumentEntity, document.Id);
        foreach (var attribute in document.Attributes.Where(a => columns.AllColumns || columns.Columns.Contains(a.Key)))
        {
            projected[attribute.Key] = attribute.Value;
        }

        return projected;
    }

    private static IServiceScopeFactory ScopeServing(RecordContainerResolver resolver)
    {
        var provider = new Mock<IServiceProvider>();
        provider.Setup(p => p.GetService(typeof(RecordContainerResolver))).Returns(resolver);
        var scope = new Mock<IServiceScope>();
        scope.Setup(s => s.ServiceProvider).Returns(provider.Object);
        var factory = new Mock<IServiceScopeFactory>();
        factory.Setup(f => f.CreateScope()).Returns(scope.Object);
        return factory.Object;
    }

    private static JobSubmissionService BuildJobQueue(List<JobContract> jobs)
    {
        var sbOptions = new Mock<IOptions<ServiceBusOptions>>();
        sbOptions.Setup(o => o.Value).Returns(new ServiceBusOptions
        {
            QueueName = "test-jobs",
            ConnectionString = "Endpoint=sb://test.servicebus.windows.net/;SharedAccessKeyName=k;SharedAccessKey=v",
        });
        var queue = new Mock<JobSubmissionService>(
            MockBehavior.Loose, sbOptions.Object, Mock.Of<ILogger<JobSubmissionService>>(), new Mock<ServiceBusClient>().Object);
        queue
            .Setup(j => j.SubmitJobAsync(It.IsAny<JobContract>(), It.IsAny<CancellationToken>()))
            .Callback<JobContract, CancellationToken>((job, _) => jobs.Add(job))
            .Returns(Task.CompletedTask);
        return queue.Object;
    }
}
