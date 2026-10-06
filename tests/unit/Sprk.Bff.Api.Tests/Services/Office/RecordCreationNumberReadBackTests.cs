// spaarkeai-word-add-in-r1 task 076: after a Matter/Project create commits, RecordCreationService reads the number the
// platform's autonumber assigned and warns when it is blank. The row already exists by then, so the read-back must never
// turn a committed create into an error — a cancelled request included (code review W3, 2026-10-03). The HTTP contract
// tests cannot cancel a request and still observe its response, so this pins it at the service.

using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Models.Office;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.Office;
using Sprk.Bff.Api.Tests.TestInfrastructure;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Office;

[Trait("status", "task-076-word-add-in-r1")]
public class RecordCreationNumberReadBackTests
{
    private static readonly Guid Caller = Guid.Parse("aaaaaaaa-0000-0000-0000-0000000000a1");
    private static readonly Guid Team = Guid.Parse("dddddddd-0000-0000-0000-0000000000d1");
    private static readonly Guid Created = Guid.Parse("00000000-0000-0000-0000-0000000c0de7");

    [Theory]
    [InlineData(QuickCreateEntityType.Matter, "sprk_matter")]
    [InlineData(QuickCreateEntityType.Project, "sprk_project")]
    public async Task WhenTheRequestIsCancelledAfterTheCreateCommits_TheCreateStillSucceeds(QuickCreateEntityType type, string logicalName)
    {
        using var cts = new CancellationTokenSource();
        var entities = new Mock<IGenericEntityService>(MockBehavior.Loose);
        entities.Setup(e => e.CreateAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>()))
            .Callback(() => cts.Cancel()) // the client goes away just after the row is written
            .ReturnsAsync(Created);
        entities.Setup(e => e.RetrieveAsync(logicalName, Created, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException(cts.Token));
        var ownership = new Mock<IRecordOwnershipResolver>();
        ownership.Setup(o => o.ResolveOwningTeamAsync(It.IsAny<RecordOwnershipContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Team);
        var sut = new RecordCreationService(
            entities.Object,
            new Mock<IFieldMappingDataverseService>().Object,
            ownership.Object,
            IdentityNormalizationFixtures.WithContact(null).Object,
            Sprk.Bff.Api.Tests.TestInfrastructure.SecureRootFilingGateFixtures.NothingSecure(),
            Sprk.Bff.Api.Tests.AccessControl.AssignedAccessTestDoubles.InertMaterializer(),
            NullLogger<RecordCreationService>.Instance);

        var result = await sut.CreateAsync(
            new RecordCreationRequest { EntityType = type, Name = "Cancelled late", CallerUserId = "oid", OwnerSystemUserId = Caller.ToString("D") },
            cts.Token);

        result.Succeeded.Should().BeTrue("the row exists; a cancelled confirmation read must not hide it behind an error");
        result.RecordId.Should().Be(Created);
        result.Warnings.Should().NotContain(w => w.Contains("without a number"), "a read that did not complete says nothing about the number");
    }
}
