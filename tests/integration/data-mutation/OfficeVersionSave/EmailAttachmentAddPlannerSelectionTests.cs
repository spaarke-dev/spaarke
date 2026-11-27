using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Xrm.Sdk;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Infrastructure.Cache;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Services.Email;
using Sprk.Bff.Api.Services.Office;
using Sprk.Bff.Api.Tests.Auth.SpeAdmin;
using Sprk.Bff.Api.Tests.TestInfrastructure;
using Xunit;

namespace Sprk.Bff.Api.Tests.Integration.DataMutation.OfficeVersionSave;

/// <summary>
/// spaarkeai-word-add-in-r1 task 138 (#2209), fix round 1b: what <see cref="EmailAttachmentAddPlanner"/> reads, and which
/// attachments it plans, from the stored <c>.eml</c> and its existing children.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>An attachment the worker cannot create (no content) is never "missing": it would be planned, queued and counted
/// "added" on every re-save while nothing is ever created.</item>
/// <item>A re-save whose ticked names ALL already have a child reads no SPE bytes (the children are read first); a selection
/// naming a file with no child, or naming every attachment, downloads the <c>.eml</c> once.</item>
/// </list>
/// The planner is the real class over doubles at its module boundaries (Dataverse, SPE, the converter); the filter is real.
/// </remarks>
[Trait("status", "task-138-word-add-in-r1")]
public class EmailAttachmentAddPlannerSelectionTests
{
    private const string A = "Settlement terms.pdf";
    private const string B = "Exhibit A.pdf";
    private const string Empty = "placeholder.dat";
    private static readonly Guid EmlId = Guid.Parse("13813813-0138-4138-8138-0000000000e1");
    private static readonly Guid MatterId = Guid.Parse("13813813-0138-4138-8138-0000000000a1");

    [Fact]
    public async Task ZeroByteAttachment_IsNeverMissing_SoTheSelectionOfItAddsNothing_AndReportsItNotSaveable()
    {
        var fixture = new Fixture(attachments: new[] { Attachment(A, 20_000), Attachment(Empty, 0, "application/octet-stream") });
        fixture.Children.Add(Child(A));

        var plan = await fixture.PlanAsync(selection: new List<string> { A, Empty });

        // The worker skips an attachment with no content, so it can never become a child: nothing is added. Task 141 review
        // (finding 3): the name is reported as not saveable instead of being answered as "nothing to add".
        plan.Should().NotBeNull();
        plan!.AttachmentNames.Should().BeEmpty("the worker skips an attachment with no content, so it can never become a child");
        plan.NotSaveable.Should().Equal(Empty);
        plan.NotStored.Should().BeEmpty();
    }

    [Fact]
    public async Task ZeroByteAttachment_WithEveryAttachmentSelected_IsLeftOutOfThePlan_AndTheOthersAreStillPlanned()
    {
        var fixture = new Fixture(attachments: new[] { Attachment(A, 20_000), Attachment(B, 20_000), Attachment(Empty, 0, "application/octet-stream") });
        fixture.Children.Add(Child(A));

        var plan = await fixture.PlanAsync(selection: null);

        plan.Should().NotBeNull();
        plan!.AttachmentNames.Should().Equal(B);
    }

    [Fact]
    public async Task ReSave_WhoseTickedNamesAllHaveAChild_DownloadsNothing()
    {
        var fixture = new Fixture(attachments: new[] { Attachment(A, 20_000), Attachment(B, 20_000) });
        fixture.Children.Add(Child(A));

        var plan = await fixture.PlanAsync(selection: new List<string> { A.ToUpperInvariant() });

        plan.Should().BeNull();
        fixture.Downloads.Should().Be(0, "the children answer it; no SPE bytes are read for a plain re-save");
        fixture.Extractions.Should().Be(0);
    }

    [Fact]
    public async Task ReSave_NamingAFileWithNoChild_DownloadsTheEmlOnce_AndPlansIt()
    {
        var fixture = new Fixture(attachments: new[] { Attachment(A, 20_000), Attachment(B, 20_000) });
        fixture.Children.Add(Child(A));

        var plan = await fixture.PlanAsync(selection: new List<string> { A, B });

        plan.Should().NotBeNull();
        plan!.AttachmentNames.Should().Equal(B);
        fixture.Downloads.Should().Be(1);
    }

    [Fact]
    public async Task ReSave_OfEveryAttachment_DownloadsTheEml_BecauseTheNamesAreNotKnownWithoutIt()
    {
        var fixture = new Fixture(attachments: new[] { Attachment(A, 20_000), Attachment(B, 20_000) });
        fixture.Children.Add(Child(A));

        var plan = await fixture.PlanAsync(selection: null);

        plan!.AttachmentNames.Should().Equal(B);
        fixture.Downloads.Should().Be(1);
    }

    // ── Helpers ────────────────────────────────────────────────────────────────────────────────────────────────────

    private static DocumentEntity Child(string name) => new() { Id = Guid.NewGuid().ToString(), Name = name, FileName = name };

    private static EmailAttachmentInfo Attachment(string name, int size, string mime = "application/pdf") => new()
    {
        AttachmentId = Guid.NewGuid(),
        FileName = name,
        MimeType = mime,
        Content = new MemoryStream(new byte[size]),
        SizeBytes = size,
    };

    private sealed class Fixture
    {
        private readonly Mock<IDocumentDataverseService> _documents = new();
        private readonly EmailAttachmentAddPlanner _planner;

        public Fixture(EmailAttachmentInfo[] attachments)
        {
            _documents
                .Setup(d => d.GetDocumentAsync(EmlId.ToString("D"), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new DocumentEntity
                {
                    Id = EmlId.ToString("D"),
                    Name = "Status.eml",
                    FileName = "Status.eml",
                    FileSize = 2048,
                    GraphDriveId = "b!drive",
                    GraphItemId = "item-eml",
                });
            _documents
                .Setup(d => d.GetDocumentsByParentAsync(EmlId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => (IEnumerable<DocumentEntity>)Children.ToList());

            var filing = new Entity("sprk_document", EmlId);
            filing["sprk_matter"] = new EntityReference("sprk_matter", MatterId);
            var entities = new Mock<IGenericEntityService>();
            entities
                .Setup(e => e.RetrieveAsync("sprk_document", EmlId, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(filing);

            var graphClientFactory = Mock.Of<IGraphClientFactory>();
            var spe = new Mock<SpeFileStore>(
                MockBehavior.Loose,
                new ContainerOperations(graphClientFactory, TestSpeOwnership.AllowAll(graphClientFactory), Mock.Of<ILogger<ContainerOperations>>()),
                new DriveItemOperations(graphClientFactory, TestSpeOwnership.AllowAll(graphClientFactory), Mock.Of<ILogger<DriveItemOperations>>()),
                new UploadSessionManager(graphClientFactory, TestSpeOwnership.AllowAll(graphClientFactory), Mock.Of<IHttpClientFactory>(), Mock.Of<ILogger<UploadSessionManager>>()),
                new UserOperations(graphClientFactory, Mock.Of<ILogger<UserOperations>>()),
                null!);
            spe.Setup(s => s.DownloadFileAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(() =>
                {
                    Downloads++;
                    return new MemoryStream(new byte[] { 1, 2, 3 });
                });

            var converter = new Mock<IEmailToEmlConverter>();
            converter.Setup(c => c.ExtractAttachments(It.IsAny<Stream>())).Returns(() =>
            {
                Extractions++;
                return attachments;
            });

            _planner = new EmailAttachmentAddPlanner(
                _documents.Object,
                entities.Object,
                spe.Object,
                converter.Object,
                new AttachmentFilterService(Options.Create(new EmailProcessingOptions()), NullLogger<AttachmentFilterService>.Instance),
                Mock.Of<ITenantCache>(),
                new ConfigurationBuilder().Build(),
                TimeProvider.System,
                NullLogger<EmailAttachmentAddPlanner>.Instance);
        }

        public List<DocumentEntity> Children { get; } = new();

        public int Downloads { get; private set; }

        public int Extractions { get; private set; }

        public Task<EmailAttachmentAddPlanner.Plan?> PlanAsync(IReadOnlyCollection<string>? selection) =>
            _planner.PlanAsync(EmlId, "item-eml", selection, "sprk_matter", MatterId, CancellationToken.None);
    }
}
