using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Xrm.Sdk;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Infrastructure.Exceptions;
using Sprk.Bff.Api.Models;
using Xunit;
using World = TestRecordContainerResolver.DocumentPointerWorld;

namespace Sprk.Bff.Api.Tests.AccessControl;

/// <summary>
/// unified-access-control-r2 task 166 — the server-side check that runs before EVERY app-only read that follows a
/// <c>sprk_document</c> row's <c>sprk_graphdriveid</c> / <c>sprk_graphitemid</c> (owner round 21 item 1 part b), as
/// made the INTERIM check by owner round 23 item 1 (r2).
/// </summary>
/// <remarks>
/// <para><b>Why.</b> The BFF reads a document's bytes as the managed identity, from whatever drive and item the row names.
/// Any Write holder can re-point a row until the pointer columns are field-secured, and rows forged before that lock stay
/// forged — so the pointer is verified at the moment it is followed.</para>
/// <para><b>The rule under test</b> (round 23): (1) the ITEM — Graph's <c>createdBy</c> is the row's creator
/// (<c>createdby</c> when a person, else <c>sprk_createdbyperson</c>), or the BFF identity for an item the BFF uploaded
/// for a row the BFF created; (2) the CONTAINER — a secure record's own container only for that record's documents, the
/// archive only on the archive path (the row's communication's own item), otherwise a business-unit container in the
/// DOCUMENT OWNER's customer subtree. Anything undecidable refuses. The r1 rule checked neither the item nor the
/// customer, which the r1 verifier showed lets a Write holder read any item of any business unit — under Model 1,
/// another customer's.</para>
/// <para>Seam: the REAL <see cref="RecordContainerResolver"/> over a substituted registry, entity service and SPE item
/// reader (<see cref="TestRecordContainerResolver.DocumentPointerWorld"/>). No <c>Mock&lt;HttpMessageHandler&gt;</c>.</para>
/// </remarks>
public class DocumentPointerContainerCheckTests
{
    private const string Item = "01ITEMCREATEDBYTHEDOCUMENTCREATOR";
    private const string ArchiveContainer = "b!archive-container";
    private const string SecureMatterContainer = "b!secure-matter-container";
    private const string ForeignContainer = "b!a-container-no-unit-stamps";

    // Model 1 (owner round 20): two customers as top-level business units of one environment.
    private const string CustomerAContainer = "b!customer-a-container";
    private const string CustomerA1Container = "b!customer-a-child-container";
    private const string CustomerBContainer = "b!customer-b-container";
    private const string RootContainer = "b!root-unit-container";
    private static readonly Guid Root = TestRecordContainerResolver.PointerWorldRootBusinessUnit;
    private static readonly Guid CustomerA = Guid.Parse("a0000000-0000-4000-8000-000000000166");
    private static readonly Guid CustomerA1 = Guid.Parse("a1000000-0000-4000-8000-000000000166");
    private static readonly Guid CustomerB = Guid.Parse("b0000000-0000-4000-8000-0000000001b6");

    private static readonly Guid SecureMatterId = Guid.Parse("1a000000-0000-4000-8000-000000000166");
    private static readonly Guid OtherMatterId = Guid.Parse("1b000000-0000-4000-8000-000000000166");
    private static readonly Guid DocumentId = Guid.Parse("1d000000-0000-4000-8000-000000000166");
    private static readonly Guid ChildDocumentId = Guid.Parse("1e000000-0000-4000-8000-000000000166");
    private static readonly Guid CommunicationId = Guid.Parse("1f000000-0000-4000-8000-000000000166");
    private static readonly Guid OtherPerson = Guid.Parse("2a000000-0000-4000-8000-000000000166");
    private static readonly Guid OtherPersonObjectId = Guid.Parse("2b000000-0000-4000-8000-000000000166");
    private static readonly Guid AnotherApplication = Guid.Parse("2c000000-0000-4000-8000-000000000166");
    private static readonly Guid AnotherApplicationUser = Guid.Parse("2d000000-0000-4000-8000-000000000166");

    private static readonly string CreatorObjectId = TestRecordContainerResolver.PointerWorldCreatorObjectId.ToString("D");
    private static readonly string BffApplicationId = TestRecordContainerResolver.PointerWorldBffApplicationId.ToString("D");

    /// <summary>Root → customer A (→ A1) and customer B, each stamping its own container; one secure matter.</summary>
    private static World Environment(
        Exception? queryFault = null, Exception? itemFault = null, bool createdByPersonColumnMissing = false, bool noItemReader = false)
    {
        var world = new World
        {
            ArchiveContainerId = ArchiveContainer,
            RetrieveMultipleFault = queryFault,
            ItemReadFault = itemFault,
            CreatedByPersonColumnMissing = createdByPersonColumnMissing,
            NoItemReader = noItemReader,
        };
        world.BusinessUnits[CustomerA] = (Root, CustomerAContainer);
        world.BusinessUnits[CustomerA1] = (CustomerA, CustomerA1Container);
        world.BusinessUnits[CustomerB] = (Root, CustomerBContainer);
        world.SecureClaims[SecureMatterContainer] = ("sprk_matter", SecureMatterId);
        world.Rows[("sprk_matter", SecureMatterId)] = new Entity("sprk_matter", SecureMatterId)
        {
            ["sprk_issecure"] = true,
            ["sprk_containerid"] = SecureMatterContainer,
            ["owningbusinessunit"] = new EntityReference("businessunit", CustomerA),
        };
        world.Rows[("sprk_matter", OtherMatterId)] = new Entity("sprk_matter", OtherMatterId)
        {
            ["sprk_issecure"] = false,
            ["owningbusinessunit"] = new EntityReference("businessunit", CustomerA),
        };
        world.Rows[("systemuser", OtherPerson)] = new Entity("systemuser", OtherPerson)
        {
            ["azureactivedirectoryobjectid"] = OtherPersonObjectId,
        };
        world.Rows[("systemuser", AnotherApplicationUser)] = new Entity("systemuser", AnotherApplicationUser)
        {
            ["applicationid"] = AnotherApplication,
        };
        return world;
    }

    /// <summary>A document owned in <paramref name="owner"/> (default customer A), created by the world's person.</summary>
    private static Entity Doc(Guid id, Guid? owner = null, Guid? createdBy = null) =>
        World.Document(id, createdBy, owner ?? CustomerA);

    private static Task<bool> Check(World world, string drive, string? item = Item, Guid? documentId = null) =>
        world.Build().IsDocumentPointerContainerAllowedAsync(documentId ?? DocumentId, drive, item);

    // ── The CONTAINER: business units, by the document owner's customer subtree (Model 1) ──────────────

    [Fact]
    public async Task PointerIntoTheOwnersOwnBusinessUnitContainer_IsAllowed()
    {
        var world = Environment();
        world.Rows[("sprk_document", DocumentId)] = Doc(DocumentId, owner: CustomerA);

        (await Check(world, CustomerAContainer)).Should().BeTrue();
    }

    [Fact]
    public async Task PointerIntoAnotherBusinessUnitOfTheSameCustomer_IsAllowed()
    {
        // A document owned in the customer's child unit may sit in the customer's container, and vice versa.
        var world = Environment();
        world.Rows[("sprk_document", DocumentId)] = Doc(DocumentId, owner: CustomerA1);
        world.Rows[("sprk_document", ChildDocumentId)] = Doc(ChildDocumentId, owner: CustomerA);

        (await Check(world, CustomerAContainer)).Should().BeTrue();
        (await Check(world, CustomerA1Container, documentId: ChildDocumentId)).Should().BeTrue();
    }

    [Fact]
    public async Task PointerIntoAnotherCustomersBusinessUnitContainer_IsRefused()
    {
        // The r1 verifier's case (item 7): a Write holder on a customer-A document re-points it at customer B's
        // container. r1 accepted ANY business unit's container; round 23 confines it to the owner's customer subtree.
        var world = Environment();
        world.Rows[("sprk_document", DocumentId)] = Doc(DocumentId, owner: CustomerA1);

        (await Check(world, CustomerBContainer)).Should().BeFalse();
    }

    [Fact]
    public async Task ARootOwnedDocument_MayUseOnlyAContainerTheRootItselfStamps()
    {
        // Owner round 25 item 6 (task 166 f1; verifier item 9): before f1 a root-owned row's subtree was the whole
        // environment ("the operator level"), so under Model 1 a Write holder on a root-owned row the BFF created could
        // re-point it at any item the BFF uploaded in ANY customer's container. Now only the root's own container.
        var world = Environment();
        world.BusinessUnits[Root] = (null, RootContainer);
        world.Rows[("sprk_document", DocumentId)] = Doc(DocumentId, owner: Root);

        (await Check(world, RootContainer)).Should().BeTrue("the root unit's own container");
        (await Check(world, CustomerBContainer)).Should().BeFalse("a customer's container is not the root's");
        (await Check(world, CustomerA1Container)).Should().BeFalse("nor is a customer's child unit's");
    }

    [Fact]
    public async Task PointerIntoAContainerNoBusinessUnitStamps_IsRefused()
    {
        var world = Environment();
        world.Rows[("sprk_document", DocumentId)] = Doc(DocumentId, owner: Root);

        (await Check(world, ForeignContainer)).Should().BeFalse();
    }

    [Fact]
    public async Task ADocumentWhoseOwningBusinessUnitIsNotInTheHierarchy_IsRefused()
    {
        var world = Environment();
        world.Rows[("sprk_document", DocumentId)] = Doc(DocumentId, owner: Guid.NewGuid());

        (await Check(world, CustomerAContainer)).Should().BeFalse();
    }

    [Fact]
    public async Task Ensure_OnARefusedPointer_Throws409DocumentStorageUnverified()
    {
        var world = Environment();
        world.Rows[("sprk_document", DocumentId)] = Doc(DocumentId, owner: CustomerA);

        var act = () => world.Build().EnsureDocumentPointerContainerAsync(DocumentId, CustomerBContainer, Item);

        var thrown = await act.Should().ThrowAsync<SdapProblemException>();
        thrown.Which.StatusCode.Should().Be(409);
        thrown.Which.Code.Should().Be(RecordContainerResolver.DocumentStorageUnverifiedCode);
    }

    // ── The CONTAINER: the archive, only on the archive path ──────────────────────────────────────────

    private static World ArchiveWorld(Guid? linkedCommunication, string itemName)
    {
        var world = Environment();
        var row = Doc(DocumentId, createdBy: TestRecordContainerResolver.PointerWorldBffUser);
        if (linkedCommunication is { } c)
        {
            row["sprk_relatedcommunication"] = new EntityReference("sprk_communication", c);
        }

        world.Rows[("sprk_document", DocumentId)] = row;
        world.Items[(ArchiveContainer, Item)] = new SpeItemCreator(itemName, null, BffApplicationId);
        return world;
    }

    [Fact]
    public async Task PointerIntoTheArchive_ForTheArchivesOwnRecordOfThatCommunicationsItem_IsAllowed()
    {
        var world = ArchiveWorld(CommunicationId, $"{CommunicationId:N}_Re- Q4 numbers.eml");

        (await Check(world, ArchiveContainer)).Should().BeTrue();
    }

    [Fact]
    public async Task PointerIntoTheArchive_FromARowNotLinkedToACommunication_IsRefused()
    {
        var world = ArchiveWorld(null, $"{CommunicationId:N}_Re- Q4 numbers.eml");

        (await Check(world, ArchiveContainer)).Should().BeFalse("the archive container is not an ordinary document container");
    }

    [Fact]
    public async Task PointerIntoTheArchive_AtAnotherCommunicationsItem_IsRefused()
    {
        // The row is linked to communication C, but the item was archived for communication D — another customer's mail,
        // possibly: the archive container is shared by the whole environment.
        var world = ArchiveWorld(CommunicationId, $"{Guid.NewGuid():N}_someone else's mail.eml");

        (await Check(world, ArchiveContainer)).Should().BeFalse();
    }

    [Fact]
    public async Task WhenTheArchiveIsAlsoABusinessUnitsContainer_EitherRuleAdmits_AndNeitherWidens()
    {
        // Live dev (2026-10-04): Communication:ArchiveContainerId IS the "Spaarke Demo" unit's container. An ordinary
        // document of that customer is admitted by the subtree rule; another customer's is not, archive or no archive.
        var world = new World { ArchiveContainerId = CustomerBContainer };
        world.BusinessUnits[CustomerA] = (Root, CustomerAContainer);
        world.BusinessUnits[CustomerB] = (Root, CustomerBContainer);
        world.Rows[("sprk_document", DocumentId)] = Doc(DocumentId, owner: CustomerB);
        world.Rows[("sprk_document", ChildDocumentId)] = Doc(ChildDocumentId, owner: CustomerA);

        (await Check(world, CustomerBContainer)).Should().BeTrue("customer B's own document in customer B's container");
        (await Check(world, CustomerBContainer, documentId: ChildDocumentId)).Should().BeFalse(
            "a customer-A document that is not the archive's own record gains nothing from the container also being the archive");
    }

    // ── The CONTAINER: a secure record's own container ────────────────────────────────────────────────

    [Fact]
    public async Task PointerIntoASecureMattersContainer_ForADocumentOfThatMatter_IsAllowed()
    {
        var world = Environment();
        var row = Doc(DocumentId);
        row["sprk_matter"] = new EntityReference("sprk_matter", SecureMatterId);
        world.Rows[("sprk_document", DocumentId)] = row;

        (await Check(world, SecureMatterContainer)).Should().BeTrue();
    }

    [Fact]
    public async Task PointerIntoASecureMattersContainer_ForADocumentOfAnotherRecord_IsRefused()
    {
        // The F0 attack: re-point a document the attacker may write (filed under a matter they can see) at the secure
        // matter's container, then download it through the BFF.
        var world = Environment();
        var row = Doc(DocumentId);
        row["sprk_matter"] = new EntityReference("sprk_matter", OtherMatterId);
        world.Rows[("sprk_document", DocumentId)] = row;

        (await Check(world, SecureMatterContainer)).Should().BeFalse();
    }

    [Fact]
    public async Task PointerIntoASecureMattersContainer_ForAnAttachmentOfADocumentOfThatMatter_IsAllowed()
    {
        var world = Environment();
        var parent = Doc(DocumentId);
        parent["sprk_matter"] = new EntityReference("sprk_matter", SecureMatterId);
        var child = Doc(ChildDocumentId);
        child["sprk_parentdocument"] = new EntityReference("sprk_document", DocumentId);
        world.Rows[("sprk_document", DocumentId)] = parent;
        world.Rows[("sprk_document", ChildDocumentId)] = child;

        (await Check(world, SecureMatterContainer, documentId: ChildDocumentId)).Should().BeTrue(
            "an email attachment's file lives where its email's does");
    }

    [Fact]
    public async Task PointerIntoASecureMattersContainer_ForADocumentWithNoLink_IsRefused()
    {
        var world = Environment();
        world.Rows[("sprk_document", DocumentId)] = Doc(DocumentId);

        (await Check(world, SecureMatterContainer)).Should().BeFalse();
    }

    // ── The ITEM: created by the row's creator (owner round 23 item 1) ───────────────────────────────

    [Fact]
    public async Task AnItemUploadedByAnotherPerson_IsRefused_EvenInTheOwnersOwnContainer()
    {
        // The r1 verifier's case (item 7): r1 never looked at sprk_graphitemid, so a Write holder could re-point a row at
        // ANY item in an accepted container. The item must be the row creator's own upload.
        var world = Environment();
        world.Rows[("sprk_document", DocumentId)] = Doc(DocumentId, owner: CustomerA);
        world.Items[(CustomerAContainer, Item)] = new SpeItemCreator("payroll.xlsx", OtherPersonObjectId.ToString("D"), BffApplicationId);

        (await Check(world, CustomerAContainer)).Should().BeFalse();
    }

    [Fact]
    public async Task AnItemThatIsNotInTheNamedDrive_IsRefused()
    {
        var world = Environment();
        world.Rows[("sprk_document", DocumentId)] = Doc(DocumentId, owner: CustomerA);
        world.Items[(CustomerAContainer, Item)] = null;

        (await Check(world, CustomerAContainer)).Should().BeFalse();
    }

    [Fact]
    public async Task WhenTheItemCannotBeRead_TheCheckRefuses()
    {
        var world = Environment(itemFault: new HttpRequestException("Graph unavailable"));
        world.Rows[("sprk_document", DocumentId)] = Doc(DocumentId, owner: CustomerA);

        (await Check(world, CustomerAContainer)).Should().BeFalse();
    }

    [Fact]
    public async Task WithNoItemReader_TheCheckRefuses()
    {
        var world = Environment(noItemReader: true);
        world.Rows[("sprk_document", DocumentId)] = Doc(DocumentId, owner: CustomerA);

        (await Check(world, CustomerAContainer)).Should().BeFalse("an item that cannot be verified is never followed");
    }

    [Theory]
    [InlineData(CustomerAContainer, true)]    // the unfiled document's derived container (its owner unit's)
    [InlineData(CustomerA1Container, false)]  // another container of the same customer: the container half admits it, the
                                              // derived-container test does not
    public async Task APersonsRow_WhoseItemTheBffUploadedAppOnly_IsServedOnlyInItsDerivedContainer(string drive, bool served)
    {
        // Round 23 accepted the BFF identity only for rows the BFF created. Owner round 37 item 3 (task 166 f1-v1, F1):
        // the interim rule ALSO serves a BFF-identity item on a person's row when the pointer passes the STRICT derived-
        // container test — a file the relocator placed — and nothing wider.
        var world = Environment();
        world.Rows[("sprk_document", DocumentId)] = Doc(DocumentId, owner: CustomerA);
        world.Items[(drive, Item)] = new SpeItemCreator("file.pdf", null, BffApplicationId);

        (await Check(world, drive)).Should().Be(served);
    }

    [Fact]
    public async Task ABffRow_WhoseItemTheBffUploadedAppOnly_IsAllowed()
    {
        var world = Environment();
        world.Rows[("sprk_document", DocumentId)] = Doc(DocumentId, owner: CustomerA, createdBy: TestRecordContainerResolver.PointerWorldBffUser);
        world.Items[(CustomerAContainer, Item)] = new SpeItemCreator("file.pdf", null, BffApplicationId);

        (await Check(world, CustomerAContainer)).Should().BeTrue();
    }

    [Fact]
    public async Task ABffRow_WhoseItemAnotherApplicationUploaded_IsRefused()
    {
        var world = Environment();
        world.Rows[("sprk_document", DocumentId)] = Doc(DocumentId, owner: CustomerA, createdBy: TestRecordContainerResolver.PointerWorldBffUser);
        world.Items[(CustomerAContainer, Item)] = new SpeItemCreator("file.pdf", null, AnotherApplication.ToString("D"));

        (await Check(world, CustomerAContainer)).Should().BeFalse();
    }

    [Fact]
    public async Task ARowAnotherApplicationCreated_IsRefused_EvenForThatApplicationsOwnUpload()
    {
        // "The BFF identity for BFF-created rows" — not any application's.
        var world = Environment();
        world.Rows[("sprk_document", DocumentId)] = Doc(DocumentId, owner: CustomerA, createdBy: AnotherApplicationUser);
        world.Items[(CustomerAContainer, Item)] = new SpeItemCreator("file.pdf", null, AnotherApplication.ToString("D"));

        (await Check(world, CustomerAContainer)).Should().BeFalse();
    }

    [Fact]
    public async Task ABffRow_WhoseItemItsRecordedPersonUploaded_IsAllowed()
    {
        // createdby is the BFF, so the row's creator is sprk_createdbyperson (tasks 133 / 146).
        var world = Environment();
        var row = Doc(DocumentId, owner: CustomerA, createdBy: TestRecordContainerResolver.PointerWorldBffUser);
        row[RecordContainerResolver.CreatedByPersonColumn] = new EntityReference("systemuser", OtherPerson);
        world.Rows[("sprk_document", DocumentId)] = row;
        world.Items[(CustomerAContainer, Item)] = new SpeItemCreator("file.pdf", OtherPersonObjectId.ToString("D"), BffApplicationId);

        (await Check(world, CustomerAContainer)).Should().BeTrue();
    }

    [Fact]
    public async Task ABffRow_WithNoRecordedPerson_WhoseItemAPersonUploaded_IsRefused()
    {
        var world = Environment();
        world.Rows[("sprk_document", DocumentId)] = Doc(DocumentId, owner: CustomerA, createdBy: TestRecordContainerResolver.PointerWorldBffUser);
        world.Items[(CustomerAContainer, Item)] = new SpeItemCreator("file.pdf", CreatorObjectId, BffApplicationId);

        (await Check(world, CustomerAContainer)).Should().BeFalse("whose upload it should be cannot be verified");
    }

    [Fact]
    public async Task WhereTheCreatedByPersonColumnDoesNotExist_ABffRowsPersonIsUnverifiable_ButAPersonsRowStillVerifies()
    {
        // Owner round 17: a missing creator column is unverifiable. It must refuse only the answer that needs it.
        var world = Environment(createdByPersonColumnMissing: true);
        world.Rows[("sprk_document", DocumentId)] = Doc(DocumentId, owner: CustomerA, createdBy: TestRecordContainerResolver.PointerWorldBffUser);
        world.Rows[("sprk_document", ChildDocumentId)] = Doc(ChildDocumentId, owner: CustomerA);
        world.Items[(CustomerAContainer, Item)] = new SpeItemCreator("file.pdf", CreatorObjectId, BffApplicationId);

        (await Check(world, CustomerAContainer)).Should().BeFalse();
        (await Check(world, CustomerAContainer, documentId: ChildDocumentId)).Should().BeTrue();
    }

    // ── Undecidable → refuse ────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task WhenTheContainerQueriesFault_TheCheckRefuses()
    {
        var world = Environment(queryFault: new TimeoutException("Dataverse unavailable"));
        world.Rows[("sprk_document", DocumentId)] = Doc(DocumentId, owner: CustomerA);

        (await Check(world, CustomerAContainer)).Should().BeFalse("a pointer that cannot be verified is not followed (fail closed)");
    }

    [Theory]
    [InlineData(null, Item)]
    [InlineData("", Item)]
    [InlineData("   ", Item)]
    [InlineData(CustomerAContainer, null)]
    [InlineData(CustomerAContainer, "")]
    [InlineData(CustomerAContainer, "  ")]
    public async Task ABlankPointer_IsRefused(string? drive, string? item)
    {
        var allowed = await Environment().Build().IsDocumentPointerContainerAllowedAsync(DocumentId, drive, item);

        allowed.Should().BeFalse();
    }

    [Fact]
    public async Task AnEmptyDocumentId_IsRefused()
    {
        var allowed = await Environment().Build().IsDocumentPointerContainerAllowedAsync(Guid.Empty, CustomerAContainer, Item);

        allowed.Should().BeFalse();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("not-a-document-id")]
    public async Task ATextDocumentIdThatIsNotAGuid_IsRefused(string? documentId)
    {
        var allowed = await Environment().Build().IsDocumentPointerContainerAllowedAsync(documentId, CustomerAContainer, Item);

        allowed.Should().BeFalse("an id that names no sprk_document row has no pointer that can be verified");
    }

    [Fact]
    public async Task ATextDocumentIdThatIsAGuid_IsCheckedLikeTheGuidOverload()
    {
        var world = Environment();
        world.Rows[("sprk_document", DocumentId)] = Doc(DocumentId, owner: CustomerA);
        var resolver = world.Build();

        (await resolver.IsDocumentPointerContainerAllowedAsync(DocumentId.ToString("D"), CustomerAContainer, Item)).Should().BeTrue();
        (await resolver.IsDocumentPointerContainerAllowedAsync(DocumentId.ToString("D"), CustomerBContainer, Item)).Should().BeFalse();
    }

    // ── The two pure pieces ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void CustomerSubtree_IsTheOwnersTopLevelUnitAndEverythingBeneathIt()
    {
        var hierarchy = new Dictionary<Guid, Guid?>
        {
            [Root] = null, [CustomerA] = Root, [CustomerA1] = CustomerA, [CustomerB] = Root,
        };

        RecordContainerResolver.CustomerSubtree(CustomerA1, hierarchy).Should().BeEquivalentTo(new[] { CustomerA, CustomerA1 });
        RecordContainerResolver.CustomerSubtree(CustomerA, hierarchy).Should().BeEquivalentTo(new[] { CustomerA, CustomerA1 });
        RecordContainerResolver.CustomerSubtree(CustomerB, hierarchy).Should().BeEquivalentTo(new[] { CustomerB });
        RecordContainerResolver.CustomerSubtree(Root, hierarchy).Should().BeEquivalentTo(new[] { Root },
            "a ROOT-owned row may use only the root's own container (owner round 25 item 6) — never a customer's");
        RecordContainerResolver.CustomerSubtree(Guid.NewGuid(), hierarchy).Should().BeNull("an unknown unit has no subtree");

        var cycle = new Dictionary<Guid, Guid?> { [CustomerA] = CustomerA1, [CustomerA1] = CustomerA };
        RecordContainerResolver.CustomerSubtree(CustomerA, cycle).Should().BeNull("a cyclic hierarchy is unverifiable");
    }

    [Fact]
    public void TheBffIdentity_IsEveryApplicationIdTheBffAuthenticatesAs()
    {
        var api = Guid.NewGuid();
        var managedIdentity = Guid.NewGuid();
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["API_APP_ID"] = api.ToString(),
            ["AzureAd:ClientId"] = api.ToString(),
            ["Graph:ManagedIdentity:ClientId"] = managedIdentity.ToString(),
            ["Dataverse:ClientId"] = "#{not-a-guid}#",
        }).Build();

        RecordContainerResolver.BffApplicationIdsFrom(configuration).Should().BeEquivalentTo(new[] { api, managedIdentity });
        RecordContainerResolver.BffApplicationIdsFrom(null).Should().BeEmpty();
    }
}
