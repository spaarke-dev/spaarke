using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Models.Office;
using Sprk.Bff.Api.Services.Documents;
using Sprk.Bff.Api.Services.Office;
using Sprk.Bff.Api.Tests.Auth.SpeAdmin;
using Sprk.Bff.Api.Tests.TestInfrastructure;
using Xunit;

namespace Sprk.Bff.Api.Tests.Integration.DataMutation.OfficeVersionSave;

/// <summary>
/// spaarkeai-word-add-in-r1 task 130 (#2126): <see cref="OfficeDocumentPersistence"/> built WITHOUT its communication seams
/// (the internal test constructor — production DI can only choose the public one, which requires both) still fails
/// closed: an email document it cannot link is created Restricted and the gap is logged as an Error, and a communication
/// the save already captured is still linked, since only the Message-ID lookup needs the seams.
/// </summary>
/// <remarks>The sprk_document create is the write under test (KEEP path: tests/integration/data-mutation/**); the
/// constructor is reached through InternalsVisibleTo (ADR-038 A2).</remarks>
[Trait("status", "task-130-word-add-in-r1")]
public class OfficeDocumentPersistenceWithoutCommunicationSeamsTests
{
    private const string RfcId = "<CAF0-130@mail.example.com>";

    [Fact]
    public async Task EmailSave_WithNoCapturedCommunication_IsCreatedRestricted_AndLogsTheWiringGapAsAnError()
    {
        var (persistence, created, logs) = Bare();

        var (_, _, related) = await persistence.CreateDocumentWithSpePointersAsync(
            EmailSave(), "drive1", "item1", "https://spe/web", "Status.eml", 100, "user-oid", CancellationToken.None,
            owningTeamId: RecordOwnershipResolverDouble.DefaultTeamId);

        created().AccessPermission.Should().Be(100000002, "Restricted");
        created().RelatedCommunicationId.Should().BeNull();
        related.Should().BeNull("nothing is handed to the worker, so its children are Restricted too");
        logs.Lines.Where(l => l.Level == LogLevel.Error).Should().HaveCount(2, "the missing seam, and the Restricted create");
    }

    [Fact]
    public async Task EmailSave_WithACapturedCommunication_IsLinkedToIt()
    {
        var (persistence, created, _) = Bare();
        var communicationId = Guid.NewGuid();

        var (_, _, related) = await persistence.CreateDocumentWithSpePointersAsync(
            EmailSave(), "drive1", "item1", "https://spe/web", "Status.eml", 100, "user-oid", CancellationToken.None,
            owningTeamId: RecordOwnershipResolverDouble.DefaultTeamId, emailCommunicationId: communicationId);

        created().RelatedCommunicationId.Should().Be(communicationId);
        created().AccessPermission.Should().BeNull();
        related.Should().Be(communicationId);
    }

    private static SaveRequest EmailSave() => new()
    {
        ContentType = SaveContentType.Email,
        Email = new EmailMetadata { Subject = "Status", SenderEmail = "counsel@test.com", InternetMessageId = RfcId },
    };

    private static (OfficeDocumentPersistence Persistence, Func<CreateDocumentRequest> Created, RecordingLoggerProvider Logs) Bare()
    {
        CreateDocumentRequest? created = null;
        var documents = new Mock<IDocumentDataverseService>();
        documents.Setup(d => d.CreateDocumentAsync(It.IsAny<CreateDocumentRequest>(), It.IsAny<CancellationToken>()))
            .Callback<CreateDocumentRequest, CancellationToken>((r, _) => created = r)
            .ReturnsAsync(Guid.NewGuid().ToString());

        var dedup = new Mock<ContentDedupDetector>(MockBehavior.Loose, null!, null!, null!, NullLogger<ContentDedupDetector>.Instance);
        dedup.Setup(d => d.ReconcileAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DedupDecision("h", IsDuplicate: false, CanonicalDocumentId: null));

        var logs = new RecordingLoggerProvider();
        var persistence = new OfficeDocumentPersistence(
            documents.Object, dedup.Object, new LoggerFactory(new ILoggerProvider[] { logs }).CreateLogger<OfficeDocumentPersistence>());

        return (persistence, () => created ?? throw new InvalidOperationException("no sprk_document was created"), logs);
    }
}
