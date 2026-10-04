using FluentAssertions;
using Microsoft.Xrm.Sdk;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Infrastructure.Exceptions;
using Xunit;

namespace Sprk.Bff.Api.Tests.AccessControl;

/// <summary>
/// unified-access-control-r2 task 166 r1 — owner round 21 item 1 part (b): the server-side check that runs before
/// EVERY app-only download that follows a <c>sprk_document</c> row's <c>sprk_graphdriveid</c>.
/// </summary>
/// <remarks>
/// <para><b>Why.</b> The BFF reads a document's bytes as the managed identity, from whatever drive the row names. Any
/// Write holder can re-point a row until the pointer columns are field-secured, and rows forged before that lock stay
/// forged — so the pointer is verified at the moment it is followed.</para>
/// <para><b>The rule under test</b> (legacy-compatible, decided by the main session 2026-10-04): a pointer into a
/// SECURE record's own container is honoured only for a document that hangs off that record (directly, or as a child
/// of a document that does); any other pointer must name one of this environment's shared containers (a business
/// unit's, or the configured archive); anything undecidable refuses.</para>
/// <para>Seam: the REAL <see cref="RecordContainerResolver"/> over a substituted registry and entity service
/// (<see cref="TestRecordContainerResolver.ForDocumentPointerWorld"/>). No <c>Mock&lt;HttpMessageHandler&gt;</c>.</para>
/// </remarks>
public class DocumentPointerContainerCheckTests
{
    private const string BusinessUnitContainer = "b!bu-container";
    private const string ArchiveContainer = "b!archive-container";
    private const string SecureMatterContainer = "b!secure-matter-container";
    private const string ForeignContainer = "b!another-customers-container";

    private static readonly Guid SecureMatterId = Guid.Parse("1a000000-0000-4000-8000-000000000166");
    private static readonly Guid OtherMatterId = Guid.Parse("1b000000-0000-4000-8000-000000000166");
    private static readonly Guid BusinessUnitId = Guid.Parse("1c000000-0000-4000-8000-000000000166");
    private static readonly Guid DocumentId = Guid.Parse("1d000000-0000-4000-8000-000000000166");
    private static readonly Guid ChildDocumentId = Guid.Parse("1e000000-0000-4000-8000-000000000166");

    private static RecordContainerResolver World(
        Dictionary<(string, Guid), Entity>? rows = null, Exception? queryFault = null)
    {
        var allRows = new Dictionary<(string, Guid), Entity>(rows ?? new Dictionary<(string, Guid), Entity>())
        {
            [("sprk_matter", SecureMatterId)] = new Entity("sprk_matter", SecureMatterId)
            {
                ["sprk_issecure"] = true,
                ["sprk_containerid"] = SecureMatterContainer,
                ["owningbusinessunit"] = new EntityReference("businessunit", BusinessUnitId),
            },
            [("sprk_matter", OtherMatterId)] = new Entity("sprk_matter", OtherMatterId)
            {
                ["sprk_issecure"] = false,
                ["owningbusinessunit"] = new EntityReference("businessunit", BusinessUnitId),
            },
            [("businessunit", BusinessUnitId)] = new Entity("businessunit", BusinessUnitId)
            {
                ["sprk_containerid"] = BusinessUnitContainer,
            },
        };

        return TestRecordContainerResolver.ForDocumentPointerWorld(
            isBusinessUnitContainer: c => c == BusinessUnitContainer,
            secureClaims: new Dictionary<string, (string Entity, Guid Id)>(StringComparer.Ordinal)
            {
                [SecureMatterContainer] = ("sprk_matter", SecureMatterId),
            },
            rows: allRows,
            archiveContainerId: ArchiveContainer,
            retrieveMultipleFault: queryFault);
    }

    private static Entity Document(Guid id, string? linkColumn = null, Guid? linkedId = null, Guid? parent = null)
    {
        var row = new Entity("sprk_document", id);
        if (linkColumn is not null)
        {
            row[linkColumn] = new EntityReference("sprk_matter", linkedId!.Value);
        }
        if (parent is { } parentId)
        {
            row["sprk_parentdocument"] = new EntityReference("sprk_document", parentId);
        }

        return row;
    }

    // ── Shared environment containers ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task PointerIntoABusinessUnitContainer_IsAllowed()
    {
        var allowed = await World().IsDocumentPointerContainerAllowedAsync(DocumentId, BusinessUnitContainer);

        allowed.Should().BeTrue("447 of 530 live documents sit in their UPLOADER's business-unit container (pre-task-076)");
    }

    [Fact]
    public async Task PointerIntoTheConfiguredArchiveContainer_IsAllowed()
    {
        var allowed = await World().IsDocumentPointerContainerAllowedAsync(DocumentId, ArchiveContainer);

        allowed.Should().BeTrue();
    }

    [Fact]
    public async Task PointerIntoAContainerThisEnvironmentDoesNotOwn_IsRefused()
    {
        var allowed = await World().IsDocumentPointerContainerAllowedAsync(DocumentId, ForeignContainer);

        allowed.Should().BeFalse("a forged pointer must not reach a container no business unit, archive or secure record here owns");
    }

    [Fact]
    public async Task Ensure_OnARefusedPointer_Throws409DocumentStorageUnverified()
    {
        var act = () => World().EnsureDocumentPointerContainerAsync(DocumentId, ForeignContainer);

        var thrown = await act.Should().ThrowAsync<SdapProblemException>();
        thrown.Which.StatusCode.Should().Be(409);
        thrown.Which.Code.Should().Be(RecordContainerResolver.DocumentStorageUnverifiedCode);
    }

    // ── A secure record's own container ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task PointerIntoASecureMattersContainer_ForADocumentOfThatMatter_IsAllowed()
    {
        var resolver = World(new() { [("sprk_document", DocumentId)] = Document(DocumentId, "sprk_matter", SecureMatterId) });

        var allowed = await resolver.IsDocumentPointerContainerAllowedAsync(DocumentId, SecureMatterContainer);

        allowed.Should().BeTrue();
    }

    [Fact]
    public async Task PointerIntoASecureMattersContainer_ForADocumentOfAnotherRecord_IsRefused()
    {
        // The F0 attack: re-point a document the attacker may write (filed under a matter they can see) at the secure
        // matter's container, then download it through the BFF.
        var resolver = World(new() { [("sprk_document", DocumentId)] = Document(DocumentId, "sprk_matter", OtherMatterId) });

        var allowed = await resolver.IsDocumentPointerContainerAllowedAsync(DocumentId, SecureMatterContainer);

        allowed.Should().BeFalse();
    }

    [Fact]
    public async Task PointerIntoASecureMattersContainer_ForAnAttachmentOfADocumentOfThatMatter_IsAllowed()
    {
        var resolver = World(new()
        {
            [("sprk_document", DocumentId)] = Document(DocumentId, "sprk_matter", SecureMatterId),
            [("sprk_document", ChildDocumentId)] = Document(ChildDocumentId, parent: DocumentId),
        });

        var allowed = await resolver.IsDocumentPointerContainerAllowedAsync(ChildDocumentId, SecureMatterContainer);

        allowed.Should().BeTrue("an email attachment's file lives where its email's does");
    }

    [Fact]
    public async Task PointerIntoASecureMattersContainer_ForADocumentWithNoLink_IsRefused()
    {
        var resolver = World(new() { [("sprk_document", DocumentId)] = Document(DocumentId) });

        var allowed = await resolver.IsDocumentPointerContainerAllowedAsync(DocumentId, SecureMatterContainer);

        allowed.Should().BeFalse();
    }

    // ── Undecidable → refuse ────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task WhenTheContainerQueriesFault_TheCheckRefuses()
    {
        var resolver = World(queryFault: new TimeoutException("Dataverse unavailable"));

        var allowed = await resolver.IsDocumentPointerContainerAllowedAsync(DocumentId, BusinessUnitContainer);

        allowed.Should().BeFalse("a pointer that cannot be verified is not followed (fail closed)");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ABlankPointer_IsRefused(string? drive)
    {
        var allowed = await World().IsDocumentPointerContainerAllowedAsync(DocumentId, drive);

        allowed.Should().BeFalse();
    }

    [Fact]
    public async Task AnEmptyDocumentId_IsRefused()
    {
        var allowed = await World().IsDocumentPointerContainerAllowedAsync(Guid.Empty, BusinessUnitContainer);

        allowed.Should().BeFalse();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("not-a-document-id")]
    public async Task ATextDocumentIdThatIsNotAGuid_IsRefused(string? documentId)
    {
        var allowed = await World().IsDocumentPointerContainerAllowedAsync(documentId, BusinessUnitContainer);

        allowed.Should().BeFalse("an id that names no sprk_document row has no pointer that can be verified");
    }

    [Fact]
    public async Task ATextDocumentIdThatIsAGuid_IsCheckedLikeTheGuidOverload()
    {
        var resolver = World();

        (await resolver.IsDocumentPointerContainerAllowedAsync(DocumentId.ToString("D"), BusinessUnitContainer)).Should().BeTrue();
        (await resolver.IsDocumentPointerContainerAllowedAsync(DocumentId.ToString("D"), ForeignContainer)).Should().BeFalse();
    }
}
