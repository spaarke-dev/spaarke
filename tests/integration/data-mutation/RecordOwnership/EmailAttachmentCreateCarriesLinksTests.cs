using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Models;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.Email;
using Sprk.Bff.Api.Tests.Auth.SpeAdmin;
using Sprk.Bff.Api.Tests.TestInfrastructure;
using Xunit;

namespace Sprk.Bff.Api.Tests.Integration.DataMutation.RecordOwnership;

/// <summary>
/// field-mapping-server-write-path-r1 task 034: an inbound email attachment carries its parent link and its association in the
/// create of its <c>sprk_document</c>, not in the follow-up update, and is owned by the team the owner class decided over those
/// same two links.
/// </summary>
/// <remarks>
/// The writes under test are the real <see cref="EmailAttachmentProcessor"/>'s (it stays outside <c>RecordCreationService</c>, a
/// named exception under the W3 direction), doubled only at the Dataverse document seam, the owner class's module boundary and
/// SPE (KEEP path: tests/integration/data-mutation/**). The same association map the Office save uses
/// (<c>DocumentAssociationMap</c>) fills the create.
/// </remarks>
[Trait("status", "task-034-field-mapping")]
public class EmailAttachmentCreateCarriesLinksTests
{
    private static readonly Guid RecordId = Guid.Parse("034a034a-0000-4000-8000-0000000000a1");
    private static readonly Guid ParentId = Guid.Parse("034a034a-0000-4000-8000-0000000000b1");

    /// <summary>Every accepted spelling, and the column the create is made with.</summary>
    [Theory]
    [InlineData("matter", "sprk_matter", "sprk_matter")]
    [InlineData("sprk_matter", "sprk_matter", "sprk_matter")]
    [InlineData("project", "sprk_project", "sprk_project")]
    [InlineData("invoice", "sprk_invoice", "sprk_invoice")]
    [InlineData("workassignment", "sprk_workassignment", "sprk_workassignment")]
    [InlineData("event", "sprk_relatedevent", "sprk_event")]
    [InlineData("todo", "sprk_relatedtodo", "sprk_todo")]
    [InlineData("contact", "sprk_relatedcontact", "contact")]
    public async Task Attachment_FiledToARecord_IsCreatedWithItsParentAndAssociation_AndTheUpdateCarriesNeither(
        string entityType, string column, string targetTable)
    {
        var world = new AttachmentWorld();

        var result = await world.ProcessAsync(entityType, RecordId, ParentId);

        result.ProcessedCount.Should().Be(1);
        var created = world.Creates.Should().ContainSingle("one create call").Subject;
        created.ParentDocumentLookup.Should().Be(ParentId, "the parent link is a column of the create");
        created.Association.Should().NotBeNull("the association is a column of the create");
        created.Association!.Field.LogicalName.Should().Be(column);
        created.Association.Field.TargetEntityLogicalName.Should().Be(targetTable);
        created.Association.RecordId.Should().Be(RecordId);
        created.OwningTeamId.Should().Be(RecordOwnershipResolverDouble.DefaultTeamId, "the team the owner class returned");

        var update = world.Updates.Should().ContainSingle("one file-info update").Subject.Update;
        CarriesNoLink(update).Should().BeTrue("the update writes neither the parent link nor the association");
        update.GraphItemId.Should().Be("item1", "the file pointer stays in the update");
        update.HasFile.Should().BeTrue();
    }

    [Fact]
    public async Task Attachment_ThatIsOwnedOverItsLinks_IsAskedAboutBothOfThem()
    {
        var world = new AttachmentWorld();

        await world.ProcessAsync("matter", RecordId, ParentId);

        var question = world.Ownership.Requests.Should().ContainSingle().Subject;
        Named(question).Should().Contain(("sprk_document", ParentId)).And.Contain(("sprk_matter", RecordId));
    }

    [Fact]
    public async Task Attachment_WithNoParentDocument_IsCreatedWithNoParentLink()
    {
        var world = new AttachmentWorld();

        await world.ProcessAsync("matter", RecordId, Guid.Empty);

        world.Creates.Should().ContainSingle().Which.ParentDocumentLookup.Should().BeNull("an empty parent is no parent");
        CarriesNoLink(world.Updates.Should().ContainSingle().Subject.Update).Should().BeTrue();
    }

    [Theory]
    [InlineData("account")]
    [InlineData("sprk_unknown")]
    public async Task Attachment_FiledToATypeWithNoDocumentColumn_IsCreatedWithItsParentOnly_AndWarns(string entityType)
    {
        var world = new AttachmentWorld();

        await world.ProcessAsync(entityType, RecordId, ParentId);

        var created = world.Creates.Should().ContainSingle().Subject;
        created.Association.Should().BeNull();
        created.ParentDocumentLookup.Should().Be(ParentId);
        CarriesNoLink(world.Updates.Should().ContainSingle().Subject.Update).Should().BeTrue();
        world.Logs.Lines.Should().Contain(l => l.Level == LogLevel.Warning && l.Message.Contains("UNASSOCIATED"));
    }

    [Fact]
    public async Task Attachment_WhoseOwnerIsRefused_CreatesNoRow_AndWritesNothing()
    {
        var world = new AttachmentWorld();
        world.Ownership.TeamId = null;

        var result = await world.ProcessAsync("matter", RecordId, ParentId);

        result.ProcessedCount.Should().Be(0);
        result.FailedCount.Should().Be(1);
        world.Creates.Should().BeEmpty("a refused owner creates no row, so no unfiled row exists");
        world.Updates.Should().BeEmpty();
    }

    [Fact]
    public async Task Attachment_WhoseCreateFaults_IsNotUpdated()
    {
        var world = new AttachmentWorld { CreateFault = new InvalidOperationException("create refused") };

        var result = await world.ProcessAsync("matter", RecordId, ParentId);

        result.FailedCount.Should().Be(1);
        world.Updates.Should().BeEmpty("a failed create leaves nothing to update");
    }

    private static bool CarriesNoLink(UpdateDocumentRequest update) =>
        update.ParentDocumentLookup is null
        && update.MatterLookup is null && update.ProjectLookup is null && update.InvoiceLookup is null
        && update.WorkAssignmentLookup is null && update.EventLookup is null && update.TodoLookup is null
        && update.ContactLookup is null;

    private static IEnumerable<(string Table, Guid Id)> Named(RecordOwnershipContext question)
    {
        if (question.TargetEntityLogicalName is { } table && question.TargetRecordId is { } id)
            yield return (table, id);
        foreach (var parent in question.Parents)
            yield return (parent.EntityLogicalName, parent.RecordId);
    }

    /// <summary>The real processor, doubled at the document seam, the owner class and SPE.</summary>
    private sealed class AttachmentWorld
    {
        private readonly EmailAttachmentProcessor _processor;

        public AttachmentWorld()
        {
            var documents = new Mock<IDocumentDataverseService>();
            documents.Setup(d => d.CreateDocumentAsync(It.IsAny<CreateDocumentRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((CreateDocumentRequest r, CancellationToken _) =>
                {
                    if (CreateFault is not null)
                        throw CreateFault;
                    Creates.Add(r);
                    return Guid.NewGuid().ToString();
                });
            documents.Setup(d => d.UpdateDocumentAsync(It.IsAny<string>(), It.IsAny<UpdateDocumentRequest>(), It.IsAny<CancellationToken>()))
                .Callback<string, UpdateDocumentRequest, CancellationToken>((id, u, _) => Updates.Add((id, u)))
                .Returns(Task.CompletedTask);

            var graphClientFactory = Mock.Of<IGraphClientFactory>();
            var spe = new Mock<SpeFileStore>(
                MockBehavior.Loose,
                new ContainerOperations(graphClientFactory, TestSpeOwnership.AllowAll(graphClientFactory), Mock.Of<ILogger<ContainerOperations>>()),
                new DriveItemOperations(graphClientFactory, TestSpeOwnership.AllowAll(graphClientFactory), Mock.Of<ILogger<DriveItemOperations>>()),
                new UploadSessionManager(graphClientFactory, TestSpeOwnership.AllowAll(graphClientFactory), Mock.Of<IHttpClientFactory>(), Mock.Of<ILogger<UploadSessionManager>>()),
                new UserOperations(graphClientFactory, Mock.Of<ILogger<UserOperations>>()),
                null!);
            spe.Setup(s => s.UploadSmallAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((string _, string path, Stream _, CancellationToken _) => new FileHandleDto(
                    "item1", path, null, 10, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, null, false,
                    $"https://contoso.sharepoint.com/{path}"));

            _processor = new EmailAttachmentProcessor(
                spe.Object,
                documents.Object,
                Ownership,
                Options.Create(new EmailProcessingOptions()),
                new LoggerFactory(new ILoggerProvider[] { Logs }).CreateLogger<EmailAttachmentProcessor>());
        }

        public RecordOwnershipResolverDouble Ownership { get; } = new();
        public Exception? CreateFault { get; init; }
        public List<CreateDocumentRequest> Creates { get; } = new();
        public List<(string Id, UpdateDocumentRequest Update)> Updates { get; } = new();
        public RecordingLoggerProvider Logs { get; } = new();

        public Task<AttachmentProcessingResult> ProcessAsync(string associatedEntityType, Guid associatedEntityId, Guid parentDocumentId) =>
            _processor.ProcessAttachmentsAsync(
                new ProcessAttachmentsRequest
                {
                    EmailId = Guid.NewGuid(),
                    ParentDocumentId = parentDocumentId,
                    ContainerId = "container1",
                    DriveId = "drive1",
                    AssociatedEntityType = associatedEntityType,
                    AssociatedEntityId = associatedEntityId,
                    Attachments =
                    [
                        new EmailAttachmentDto
                        {
                            FileName = "Settlement terms.pdf",
                            ContentType = "application/pdf",
                            SizeBytes = 20_000,
                            Content = new MemoryStream(new byte[20_000]),
                        },
                    ],
                },
                CancellationToken.None);
    }
}
