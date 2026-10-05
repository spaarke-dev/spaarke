using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Xrm.Sdk;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Models;
using Xunit;
using World = TestRecordContainerResolver.DocumentPointerWorld;

namespace Sprk.Bff.Api.Tests.AccessControl;

/// <summary>
/// unified-access-control-r2 task 166 f1 — the DERIVED container of a document (where its file belongs) and the STRICT
/// document-pointer rule waiting behind <c>DocumentPointer:StrictDerivedContainer</c> (owner round 21 item 1 (b)), plus
/// the relocation-source check of the legacy migration and the per-scope memo of the interim rule's directory reads.
/// </summary>
/// <remarks>
/// <para><b>Why.</b> Round 21 decided the strict check — the pointer must name the container derived for the document's
/// own record — once the client no longer writes pointers and the misplaced legacy files are moved; round 23's
/// item-and-subtree check is the INTERIM until then. The derivation is also what the pointer-attach route verifies and
/// what the migration and Make Secure move files into, so it is pinned on its own.</para>
/// <para>Seam: the REAL <see cref="RecordContainerResolver"/> over its substituted registry, entity service and SPE item
/// reader (<see cref="TestRecordContainerResolver.DocumentPointerWorld"/>). No <c>Mock&lt;HttpMessageHandler&gt;</c>.</para>
/// </remarks>
public class DocumentContainerStrictRuleTests
{
    private const string Item = "01ITEMOFTHEDOCUMENT";
    private const string ArchiveContainer = "b!archive-container";
    private const string SecureContainer = "b!secure-matter-own-container";
    private const string OtherSecureContainer = "b!other-secure-project-container";
    private const string CustomerAContainer = "b!customer-a-container";
    private const string CustomerA1Container = "b!customer-a-child-container";
    private const string CustomerBContainer = "b!customer-b-container";
    private const string ForeignContainer = "b!a-container-no-unit-stamps";

    private static readonly Guid Root = TestRecordContainerResolver.PointerWorldRootBusinessUnit;
    private static readonly Guid CustomerA = Guid.Parse("a0000000-0000-4000-8000-0000000016f1");
    private static readonly Guid CustomerA1 = Guid.Parse("a1000000-0000-4000-8000-0000000016f1");
    private static readonly Guid CustomerB = Guid.Parse("b0000000-0000-4000-8000-0000000016f1");

    private static readonly Guid SecureMatter = Guid.Parse("1a000000-0000-4000-8000-0000000016f1");
    private static readonly Guid PlainMatter = Guid.Parse("1b000000-0000-4000-8000-0000000016f1");
    private static readonly Guid SecureProject = Guid.Parse("1c000000-0000-4000-8000-0000000016f1");
    private static readonly Guid Communication = Guid.Parse("1f000000-0000-4000-8000-0000000016f1");
    private static readonly Guid DocumentId = Guid.Parse("1d000000-0000-4000-8000-0000000016f1");
    private static readonly Guid ParentDocumentId = Guid.Parse("1e000000-0000-4000-8000-0000000016f1");
    private static readonly Guid OtherPerson = Guid.Parse("2a000000-0000-4000-8000-0000000016f1");
    private static readonly Guid OtherPersonObjectId = Guid.Parse("2b000000-0000-4000-8000-0000000016f1");

    /// <summary>Root → customer A (→ A1) and customer B; a secure and a plain matter; a communication; the archive.</summary>
    private static World Environment(bool strict = false, string? unfiledDefault = null)
    {
        var world = new World { ArchiveContainerId = ArchiveContainer, Strict = strict, UnfiledDefaultContainer = unfiledDefault };
        world.BusinessUnits[CustomerA] = (Root, CustomerAContainer);
        world.BusinessUnits[CustomerA1] = (CustomerA, CustomerA1Container);
        world.BusinessUnits[CustomerB] = (Root, CustomerBContainer);
        world.SecureClaims[SecureContainer] = ("sprk_matter", SecureMatter);
        world.SecureClaims[OtherSecureContainer] = ("sprk_project", SecureProject);
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
        world.Rows[("sprk_project", SecureProject)] = new Entity("sprk_project", SecureProject)
        {
            ["sprk_issecure"] = true,
            ["sprk_containerid"] = OtherSecureContainer,
            ["owningbusinessunit"] = new EntityReference("businessunit", CustomerA),
        };
        world.Rows[("sprk_communication", Communication)] = new Entity("sprk_communication", Communication)
        {
            ["owningbusinessunit"] = new EntityReference("businessunit", CustomerA),
        };
        world.Rows[("systemuser", OtherPerson)] = new Entity("systemuser", OtherPerson)
        {
            ["azureactivedirectoryobjectid"] = OtherPersonObjectId,
        };
        return world;
    }

    private static Entity Doc(Guid id, Guid? owner = null, Guid? createdBy = null, params (string Column, string Target, Guid Id)[] links)
    {
        var row = World.Document(id, createdBy, owner ?? CustomerA);
        foreach (var (column, target, linked) in links)
        {
            row[column] = new EntityReference(target, linked);
        }

        return row;
    }

    // ═════════════════════════════════════════════════════════════════════════════════════════════════════════════
    // The derivation
    // ═════════════════════════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ADocumentOfANonSecureMatter_BelongsInThatMattersBusinessUnitContainer()
    {
        var world = Environment();
        world.Rows[("sprk_document", DocumentId)] = Doc(DocumentId, links: ("sprk_matter", "sprk_matter", PlainMatter));

        var derived = await world.Build().DeriveDocumentContainersAsync(DocumentId);

        derived.Decided.Should().BeTrue(derived.Reason);
        derived.AllowedContainers.Should().Equal(CustomerA1Container);
        derived.PrimaryContainer.Should().Be(CustomerA1Container);
        derived.IsSecure.Should().BeFalse();
    }

    [Fact]
    public async Task ADocumentOfASecureMatter_BelongsOnlyInThatMattersOwnContainer()
    {
        var world = Environment();
        world.Rows[("sprk_document", DocumentId)] = Doc(DocumentId, links: ("sprk_matter", "sprk_matter", SecureMatter));

        var derived = await world.Build().DeriveDocumentContainersAsync(DocumentId);

        derived.AllowedContainers.Should().Equal(SecureContainer);
        derived.IsSecure.Should().BeTrue();
    }

    [Fact]
    public async Task ASecureAnswer_Dominates_ANonSecureLinkAddsNoContainer()
    {
        // A document filed to a secure matter AND a plain one must never be allowed in the plain matter's shared
        // container: SPE permissions are additive-only, so the shared container is the exposure.
        var world = Environment();
        world.Rows[("sprk_document", DocumentId)] = Doc(DocumentId, links:
        [
            ("sprk_matter", "sprk_matter", SecureMatter), ("sprk_relatedmatter", "sprk_matter", PlainMatter),
        ]);

        var derived = await world.Build().DeriveDocumentContainersAsync(DocumentId);

        derived.AllowedContainers.Should().Equal(SecureContainer);
    }

    [Fact]
    public async Task ADocumentOfTwoDifferentSecureRecords_IsUndecidable()
    {
        var world = Environment();
        world.Rows[("sprk_document", DocumentId)] = Doc(DocumentId, links:
        [
            ("sprk_matter", "sprk_matter", SecureMatter), ("sprk_project", "sprk_project", SecureProject),
        ]);

        var derived = await world.Build().DeriveDocumentContainersAsync(DocumentId);

        derived.Decided.Should().BeFalse("two secure roots are ambiguous — refuse, never pick one");
        derived.Allows(SecureContainer).Should().BeFalse();
    }

    [Fact]
    public async Task AnUnfiledDocument_BelongsInItsOwningBusinessUnitsContainer()
    {
        var world = Environment();
        world.Rows[("sprk_document", DocumentId)] = Doc(DocumentId, owner: CustomerA1);

        var derived = await world.Build().DeriveDocumentContainersAsync(DocumentId);

        derived.AllowedContainers.Should().Equal(CustomerA1Container);
    }

    [Fact]
    public async Task AnUnfiledDocument_WhoseBusinessUnitStampsNone_UsesTheOfficeSavesDefault_ElseIsUndecidable()
    {
        var configured = Environment(unfiledDefault: "b!email-processing-default");
        configured.Rows[("sprk_document", DocumentId)] = Doc(DocumentId, owner: Root);
        var unconfigured = Environment();
        unconfigured.Rows[("sprk_document", DocumentId)] = Doc(DocumentId, owner: Root);

        (await configured.Build().DeriveDocumentContainersAsync(DocumentId)).AllowedContainers
            .Should().Equal("b!email-processing-default");
        (await unconfigured.Build().DeriveDocumentContainersAsync(DocumentId)).Decided
            .Should().BeFalse("no container is derivable, and 'none' is never 'any'");
    }

    [Fact]
    public async Task AnArchivedDocument_BelongsWhereItsCommunicationArchives()
    {
        var world = Environment();
        world.Rows[("sprk_document", DocumentId)] = Doc(DocumentId,
            links: ("sprk_relatedcommunication", "sprk_communication", Communication));

        var derived = await world.Build().DeriveDocumentContainersAsync(DocumentId);

        derived.AllowedContainers.Should().Equal(ArchiveContainer);
    }

    [Fact]
    public async Task AnAttachment_BelongsWhereItsParentDocumentDoes()
    {
        var world = Environment();
        world.Rows[("sprk_document", ParentDocumentId)] = Doc(ParentDocumentId, links: ("sprk_matter", "sprk_matter", PlainMatter));
        world.Rows[("sprk_document", DocumentId)] = Doc(DocumentId,
            links: ("sprk_parentdocument", "sprk_document", ParentDocumentId));

        var derived = await world.Build().DeriveDocumentContainersAsync(DocumentId);

        derived.AllowedContainers.Should().Equal(CustomerA1Container);
    }

    [Fact]
    public async Task APartyLink_OwnsNoContent_TheDocumentIsPlacedAsUnfiled()
    {
        // The contact row is not modelled: consulting it would fault. A party is referenced, never an owner of content.
        var world = Environment();
        world.Rows[("sprk_document", DocumentId)] = Doc(DocumentId, owner: CustomerB,
            links: ("sprk_relatedcontact", "contact", Guid.NewGuid()));

        var derived = await world.Build().DeriveDocumentContainersAsync(DocumentId);

        derived.AllowedContainers.Should().Equal(CustomerBContainer);
    }

    [Fact]
    public async Task ALinkWhoseRecordCannotBeRead_MakesTheDocumentUndecidable()
    {
        var world = Environment();
        world.Rows[("sprk_document", DocumentId)] = Doc(DocumentId, links: ("sprk_matter", "sprk_matter", Guid.NewGuid()));

        var derived = await world.Build().DeriveDocumentContainersAsync(DocumentId);

        derived.Decided.Should().BeFalse("an unreadable record might be secure — unknown is never 'not secure'");
    }

    // ═════════════════════════════════════════════════════════════════════════════════════════════════════════════
    // The STRICT rule (behind the flag)
    // ═════════════════════════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Strict_APointerIntoTheDerivedContainer_WhoseItemExists_IsAllowed()
    {
        var world = Environment(strict: true);
        world.Rows[("sprk_document", DocumentId)] = Doc(DocumentId, links: ("sprk_matter", "sprk_matter", PlainMatter));

        var resolver = world.Build();

        resolver.StrictDerivedContainerMode.Should().BeTrue();
        (await resolver.IsDocumentPointerContainerAllowedAsync(DocumentId, CustomerA1Container, Item)).Should().BeTrue();
    }

    [Fact]
    public async Task Strict_RefusesAnotherContainerOfTheSameCustomer_ThatTheInterimRuleAllows()
    {
        // The interim rule's stated residual (round 23): any container in the owner's customer subtree. The strict rule
        // narrows it to THE derived container.
        var interim = Environment();
        interim.Rows[("sprk_document", DocumentId)] = Doc(DocumentId, links: ("sprk_matter", "sprk_matter", PlainMatter));
        var strict = Environment(strict: true);
        strict.Rows[("sprk_document", DocumentId)] = Doc(DocumentId, links: ("sprk_matter", "sprk_matter", PlainMatter));

        (await interim.Build().IsDocumentPointerContainerAllowedAsync(DocumentId, CustomerAContainer, Item)).Should().BeTrue();
        (await strict.Build().IsDocumentPointerContainerAllowedAsync(DocumentId, CustomerAContainer, Item)).Should().BeFalse();
    }

    [Fact]
    public async Task Strict_AnItemNotInTheDerivedDrive_IsRefused()
    {
        var world = Environment(strict: true);
        world.Rows[("sprk_document", DocumentId)] = Doc(DocumentId, links: ("sprk_matter", "sprk_matter", PlainMatter));
        world.Items[(CustomerA1Container, Item)] = null;

        (await world.Build().IsDocumentPointerContainerAllowedAsync(DocumentId, CustomerA1Container, Item)).Should().BeFalse();
    }

    [Fact]
    public async Task Strict_AnUndecidableDocument_IsRefused()
    {
        var world = Environment(strict: true);
        world.Rows[("sprk_document", DocumentId)] = Doc(DocumentId, links:
        [
            ("sprk_matter", "sprk_matter", SecureMatter), ("sprk_project", "sprk_project", SecureProject),
        ]);

        (await world.Build().IsDocumentPointerContainerAllowedAsync(DocumentId, SecureContainer, Item)).Should().BeFalse();
    }

    [Fact]
    public async Task Strict_AnotherCustomersContainer_IsRefused_AndSoIsARootOwnedRowsCustomerContainer()
    {
        var world = Environment(strict: true);
        world.BusinessUnits[Root] = (null, "b!root-container");
        world.Rows[("sprk_document", DocumentId)] = Doc(DocumentId, owner: Root);

        var resolver = world.Build();

        (await resolver.IsDocumentPointerContainerAllowedAsync(DocumentId, "b!root-container", Item)).Should().BeTrue();
        (await resolver.IsDocumentPointerContainerAllowedAsync(DocumentId, CustomerBContainer, Item)).Should().BeFalse();
    }

    [Fact]
    public async Task Strict_DecidesByContainer_NotByUploader_ARelocatedOrBffSavedFileInItsDerivedContainerIsServed()
    {
        // Round 21's strict rule is the CONTAINER check: once the pointer columns are locked, only the BFF writes them.
        // A file the BFF relocated (uploaded app-only) for a PERSON's row is in the derived container and is served. Owner
        // round 37 item 3 (task 166 f1-v1, F1): the INTERIM rule serves it too — a BFF-identity item that passes the strict
        // derived-container test — so a relocated file stays downloadable before the strict flip.
        var interim = Environment();
        interim.Rows[("sprk_document", DocumentId)] = Doc(DocumentId, links: ("sprk_matter", "sprk_matter", PlainMatter));
        interim.Items[(CustomerA1Container, Item)] = new SpeItemCreator(
            "file.pdf", null, TestRecordContainerResolver.PointerWorldBffApplicationId.ToString("D"));
        var strict = Environment(strict: true);
        strict.Rows[("sprk_document", DocumentId)] = Doc(DocumentId, links: ("sprk_matter", "sprk_matter", PlainMatter));
        strict.Items[(CustomerA1Container, Item)] = new SpeItemCreator(
            "file.pdf", null, TestRecordContainerResolver.PointerWorldBffApplicationId.ToString("D"));

        (await interim.Build().IsDocumentPointerContainerAllowedAsync(DocumentId, CustomerA1Container, Item)).Should().BeTrue(
            "round 37 item 3: the interim rule also serves a BFF-identity item in the document's derived container");
        (await strict.Build().IsDocumentPointerContainerAllowedAsync(DocumentId, CustomerA1Container, Item)).Should().BeTrue();
    }

    [Theory]
    [InlineData(CustomerAContainer)]   // the owner's own customer — the interim rule's container half admits it
    [InlineData(CustomerBContainer)]   // another customer's
    public async Task Interim_ABffIdentityItemOutsideTheDerivedContainer_IsRefused(string drive)
    {
        // The round-37 disjunct is never wider than the strict rule: a BFF-placed item in ANY other container — even one
        // the interim container half admits — is refused (a forged pointer to another record's BFF-saved file).
        var world = Environment();
        world.Rows[("sprk_document", DocumentId)] = Doc(DocumentId, links: ("sprk_matter", "sprk_matter", PlainMatter));
        world.Items[(drive, Item)] = new SpeItemCreator(
            "another-records-office-save.docx", null, TestRecordContainerResolver.PointerWorldBffApplicationId.ToString("D"));

        (await world.Build().IsDocumentPointerContainerAllowedAsync(DocumentId, drive, Item)).Should().BeFalse();
    }

    [Fact]
    public async Task Interim_AnAppOnlyItemOfAnotherApplication_InTheDerivedContainer_IsRefused()
    {
        var world = Environment();
        world.Rows[("sprk_document", DocumentId)] = Doc(DocumentId, links: ("sprk_matter", "sprk_matter", PlainMatter));
        world.Items[(CustomerA1Container, Item)] = new SpeItemCreator("file.pdf", null, Guid.NewGuid().ToString("D"));

        (await world.Build().IsDocumentPointerContainerAllowedAsync(DocumentId, CustomerA1Container, Item)).Should().BeFalse(
            "only the BFF identity's own uploads are 'placed by the BFF'");
    }

    [Fact]
    public async Task Interim_ABffIdentityItemOfAnUndecidableDocument_IsRefused()
    {
        // No derived container, no strict pass: the disjunct cannot serve it.
        var world = Environment();
        world.Rows[("sprk_document", DocumentId)] = Doc(DocumentId, links:
        [
            ("sprk_matter", "sprk_matter", SecureMatter), ("sprk_project", "sprk_project", SecureProject),
        ]);
        world.Items[(SecureContainer, Item)] = new SpeItemCreator(
            "file.pdf", null, TestRecordContainerResolver.PointerWorldBffApplicationId.ToString("D"));

        (await world.Build().IsDocumentPointerContainerAllowedAsync(DocumentId, SecureContainer, Item)).Should().BeFalse();
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("false", false)]
    [InlineData("yes", false)]
    [InlineData("true", true)]
    [InlineData("True", true)]
    public void TheFlag_DefaultsToTheInterimRule(string? value, bool strict)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { [RecordContainerResolver.StrictDerivedContainerKey] = value })
            .Build();

        RecordContainerResolver.StrictDerivedContainerFrom(configuration).Should().Be(strict);
        RecordContainerResolver.StrictDerivedContainerFrom(null).Should().BeFalse();
    }

    // ═════════════════════════════════════════════════════════════════════════════════════════════════════════════
    // The relocation source check (the legacy migration moves only a verifiably-own file)
    // ═════════════════════════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task RelocationSource_ThePersonsOwnUploadInABusinessUnitContainer_IsVerified()
    {
        var world = Environment();
        world.Rows[("sprk_document", DocumentId)] = Doc(DocumentId, links: ("sprk_matter", "sprk_matter", PlainMatter));

        (await world.Build().IsRelocationSourceVerifiedAsync(DocumentId, CustomerBContainer, Item)).Should().BeTrue(
            "a misplaced file the row's creator uploaded is theirs to move — even from another customer's container");
    }

    [Fact]
    public async Task RelocationSource_AnotherPersonsUpload_IsNotVerified()
    {
        var world = Environment();
        world.Rows[("sprk_document", DocumentId)] = Doc(DocumentId, links: ("sprk_matter", "sprk_matter", PlainMatter));
        world.Items[(CustomerBContainer, Item)] = new SpeItemCreator("payroll.xlsx", OtherPersonObjectId.ToString("D"), null);

        (await world.Build().IsRelocationSourceVerifiedAsync(DocumentId, CustomerBContainer, Item)).Should().BeFalse(
            "a forged pointer to someone else's file is never copied into the document's container");
    }

    [Fact]
    public async Task RelocationSource_AContainerOfNoBusinessUnit_IsNotVerified()
    {
        var world = Environment();
        world.Rows[("sprk_document", DocumentId)] = Doc(DocumentId);

        (await world.Build().IsRelocationSourceVerifiedAsync(DocumentId, ForeignContainer, Item)).Should().BeFalse();
    }

    // ═════════════════════════════════════════════════════════════════════════════════════════════════════════════
    // The per-scope memo (verifier item 13) and the business-unit subtree question (reporting, round 25 item 6)
    // ═════════════════════════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task OneScope_ReadsTheHierarchyAndEachContainersClaimantsOnce_ForManyChecks()
    {
        var world = Environment();
        var ids = Enumerable.Range(0, 5).Select(i => Guid.Parse($"3{i}000000-0000-4000-8000-0000000016f1")).ToList();
        foreach (var id in ids)
        {
            world.Rows[("sprk_document", id)] = Doc(id, owner: CustomerA1);
        }

        var resolver = world.Build();
        foreach (var id in ids)
        {
            (await resolver.IsDocumentPointerContainerAllowedAsync(id, CustomerAContainer, Item)).Should().BeTrue();
        }

        world.HierarchyReads.Should().Be(1, "a bulk download of N documents reads the hierarchy once per request, not N times");
        world.ClaimantReads.Should().Be(1, "the same container's claimants are read once per request");
    }

    [Fact]
    public async Task AFailedHierarchyRead_IsNotRemembered_TheNextCheckAsksAgain()
    {
        var world = Environment();
        world.FailFirstHierarchyReads = 1;
        world.Rows[("sprk_document", DocumentId)] = Doc(DocumentId, owner: CustomerA1);
        var resolver = world.Build();

        (await resolver.IsDocumentPointerContainerAllowedAsync(DocumentId, CustomerAContainer, Item)).Should().BeFalse("fail closed");
        (await resolver.IsDocumentPointerContainerAllowedAsync(DocumentId, CustomerAContainer, Item)).Should().BeTrue(
            "a fault is never cached for the scope — the second check reads again and succeeds");
        world.HierarchyReads.Should().Be(2);
    }

    [Fact]
    public async Task IsBusinessUnitInSubtree_IsTheUnitOrABeneathIt_AndRefusesTheUnknown()
    {
        var resolver = Environment().Build();

        (await resolver.IsBusinessUnitInSubtreeAsync(CustomerA1, CustomerA)).Should().BeTrue();
        (await resolver.IsBusinessUnitInSubtreeAsync(CustomerA, CustomerA)).Should().BeTrue();
        (await resolver.IsBusinessUnitInSubtreeAsync(CustomerB, CustomerA)).Should().BeFalse();
        (await resolver.IsBusinessUnitInSubtreeAsync(CustomerA, CustomerA1)).Should().BeFalse("a parent is not beneath its child");
        (await resolver.IsBusinessUnitInSubtreeAsync(Guid.NewGuid(), CustomerA)).Should().BeFalse("an unknown unit");
        (await resolver.IsBusinessUnitInSubtreeAsync(Guid.Empty, CustomerA)).Should().BeFalse();
    }
}
