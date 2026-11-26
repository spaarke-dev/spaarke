using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.Signals.Actions;
using Sprk.Bff.Api.Services.Workspace;
using Sprk.Bff.Api.Tests.DataMutation.ExternalAccess;
using Sprk.Bff.Api.Tests.TestInfrastructure;
using Xunit;
using EntityReference = Microsoft.Xrm.Sdk.EntityReference;

namespace Sprk.Bff.Api.Tests.Integration.DataMutation.RegardingBuilder;

/// <summary>
/// field-mapping-server-write-path-r1 task 045c (gap G5, writers W11/W12; coordinator decision 2026-11-08): the shared regarding
/// builder writes the regarded record's reference number beside the id, name and url when its caller already holds it, and
/// writes no number column otherwise. <see cref="InquiryReplyTodoCreator"/> already reads the matter row, so it passes the
/// matter's number from that read (no extra read).
/// </summary>
[Trait("status", "new")]
public class RegardingRecordNumberTests
{
    private static TodoRegardingBuilder Builder()
    {
        var catalog = new Mock<ICommunicationDataverseService>(MockBehavior.Loose);
        catalog.Setup(c => c.QueryRecordTypeRefAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync((Entity?)null);
        return new TodoRegardingBuilder(catalog.Object, SecureChildShareWorld.CoreAncestorsOver(() => SecureChildShareWorld.Standard()),
            NullLogger<TodoRegardingBuilder>.Instance);
    }

    [Fact]
    public async Task ApplyResolverPair_WithANumber_WritesItBesideTheIdNameAndUrl()
    {
        var host = new Entity("sprk_todo");
        var matter = Guid.NewGuid();

        await Builder().ApplyResolverPairAsync(host, "sprk_matter", matter, "Acme Corp", regardingRecordNumber: " MAT-000123 ");

        host.GetAttributeValue<string>("sprk_regardingrecordnumber").Should().Be("MAT-000123");
        host.GetAttributeValue<string>("sprk_regardingrecordid").Should().Be(matter.ToString("D"));
        host.GetAttributeValue<string>("sprk_regardingrecordname").Should().Be("Acme Corp");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ApplyResolverPair_WithoutANumber_WritesNoNumberColumn(string? number)
    {
        var host = new Entity("sprk_todo");

        await Builder().ApplyResolverPairAsync(host, "sprk_matter", Guid.NewGuid(), "Acme Corp", regardingRecordNumber: number);

        host.Contains("sprk_regardingrecordnumber").Should().BeFalse("an unknown number is left unset, never written empty");
    }

    [Fact]
    public async Task ApplyResolverPair_CalledAsBefore_WritesNoNumberColumn()
    {
        var host = new Entity("sprk_todo");

        await Builder().ApplyResolverPairAsync(host, "sprk_matter", Guid.NewGuid(), "Acme Corp");

        host.Contains("sprk_regardingrecordnumber").Should().BeFalse();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task InquiryOutcomeToDo_CarriesTheMattersNumber_FromTheRowItAlreadyReads(bool matterHasANumber)
    {
        var (matter, serviceRequest, communication) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var world = SecureChildShareWorld.Standard().OrdinaryRoot("sprk_matter", matter);
        var created = new List<Entity>();
        var entities = new Mock<IGenericEntityService>(MockBehavior.Loose);
        entities.Setup(e => e.RetrieveAsync("sprk_communication", communication, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Entity("sprk_communication", communication)
            {
                ["sprk_direction"] = new OptionSetValue(InquiryDispositionService.CommunicationIncoming),
                ["sprk_regardingservicerequest"] = new EntityReference(InquiryDispositionService.ServiceRequest, serviceRequest),
            });
        entities.Setup(e => e.RetrieveAsync(InquiryDispositionService.ServiceRequest, serviceRequest, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Entity(InquiryDispositionService.ServiceRequest, serviceRequest)
            {
                ["sprk_direction"] = new OptionSetValue(BudgetInquiryExecutor.DirectionOutbound),
                ["statecode"] = new OptionSetValue(0),
                ["sprk_regardingmatter"] = new EntityReference("sprk_matter", matter),
            });

        // Dataverse returns only the columns asked for: the number comes back only if the creator's one matter read asks for it.
        var matterReads = 0;
        entities.Setup(e => e.RetrieveAsync("sprk_matter", matter, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string table, Guid id, string[] columns, CancellationToken _) =>
            {
                matterReads++;
                var row = new Entity(table, id);
                if (matterHasANumber && columns.Contains("sprk_matternumber"))
                    row["sprk_matternumber"] = "MAT-000123";
                return row;
            });
        entities.Setup(e => e.RetrieveAsync("sprk_todo", It.IsAny<Guid>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("no such To Do"));
        entities.Setup(e => e.CreateAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>()))
            .Callback<Entity, CancellationToken>((e, _) => created.Add(e))
            .ReturnsAsync(Guid.NewGuid());
        var catalog = new Mock<ICommunicationDataverseService>(MockBehavior.Loose);
        catalog.Setup(c => c.QueryRecordTypeRefAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync((Entity?)null);
        var creator = new InquiryReplyTodoCreator(
            entities.Object, SecureChildShareWorld.CoreAncestorsOver(() => world), new RecordOwnershipResolverDouble(), catalog.Object,
            TimeProvider.System, NullLogger<InquiryReplyTodoCreator>.Instance);

        (await creator.OnReplyArrivedAsync(communication, CancellationToken.None)).Should().Be(InquiryReplyTodoOutcome.Created);

        var todo = created.Should().ContainSingle().Subject;
        matterReads.Should().Be(1, "the number rides on the read the creator already makes");
        if (matterHasANumber)
            todo.GetAttributeValue<string>("sprk_regardingrecordnumber").Should().Be("MAT-000123");
        else
            todo.Contains("sprk_regardingrecordnumber").Should().BeFalse();
    }
}
