// unified-access-control-r2 task 152 — TaskActionCore (the one write point for sprk_event type=Task, used by
// CreateTaskNodeExecutor and IActionSeam.CreateTaskAsync) names the PERSON the task is for in sprk_event.sprk_assignedto.
// The create is app-only (Created By = the BFF app user), so without this a playbook task reaches no one's briefing.

using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Xrm.Sdk;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Nodes;
using Sprk.Bff.Api.Tests.Services.Communication;
using Sprk.Bff.Api.Tests.TestInfrastructure;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai.Nodes;

[Trait("status", "task-152-uac-r2")]
public class TaskActionCoreAssignedToTests
{
    private static readonly Guid ActingUser = Guid.Parse("aaaaaaaa-0000-0000-0000-00000000000a");
    private static readonly Guid ActingUsersContact = Guid.Parse("cccccccc-0000-0000-0000-00000000000c");
    private static readonly Guid MatterId = Guid.Parse("11111111-0000-0000-0000-000000000001");
    private static readonly Guid MattersInternalContact = Guid.Parse("44444444-0000-0000-0000-000000000004");
    private static readonly Guid Supplied = Guid.Parse("77777777-0000-0000-0000-000000000007");

    private readonly Mock<IGenericEntityService> _entities = new();
    private readonly List<Entity> _created = new();
    private readonly CapturingLogger<TaskActionCoreAssignedToTests> _logger = new();

    public TaskActionCoreAssignedToTests()
    {
        _entities.Setup(e => e.CreateAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>()))
            .Callback<Entity, CancellationToken>((e, _) => _created.Add(e))
            .ReturnsAsync(Guid.NewGuid());
        var matter = new Entity("sprk_matter", MatterId) { ["sprk_assignedtointernal"] = new EntityReference("contact", MattersInternalContact) };
        _entities.Setup(e => e.RetrieveAsync("sprk_matter", MatterId, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(matter);
    }

    private TaskActionCore Core(Guid? linkedContact) => new(
        _entities.Object,
        CoreAncestorResolverFixtures.Inert(),
        new RecordOwnershipResolverDouble(),
        IdentityNormalizationFixtures.WithContact(linkedContact).Object,
        Mock.Of<ICommunicationDataverseService>(),
        _logger);

    private static Guid? AssignedTo(Entity e) =>
        e.Contains("sprk_assignedto") && e["sprk_assignedto"] is EntityReference r ? r.Id : null;

    [Fact]
    public async Task ActingUsersLinkedContact_IsTheAssignee()
    {
        await Core(ActingUsersContact).CreateAsync(
            new TaskActionInput("Follow up", null, null, MatterId, "sprk_matter", null, ActingUserId: ActingUser), CancellationToken.None);

        _created.Single().LogicalName.Should().Be("sprk_event");
        AssignedTo(_created.Single()).Should().Be(ActingUsersContact);
    }

    [Fact]
    public async Task SuppliedAssignee_IsNeverOverwritten()
    {
        await Core(ActingUsersContact).CreateAsync(
            new TaskActionInput("Follow up", null, null, MatterId, "sprk_matter", null, ActingUserId: ActingUser, AssignedToContactId: Supplied),
            CancellationToken.None);

        AssignedTo(_created.Single()).Should().Be(Supplied);
    }

    [Fact]
    public async Task ActingUserWithoutALink_FallsBackToTheParentsResponsibleContact()
    {
        await Core(linkedContact: null).CreateAsync(
            new TaskActionInput("Follow up", null, null, MatterId, "sprk_matter", null, ActingUserId: ActingUser), CancellationToken.None);

        AssignedTo(_created.Single()).Should().Be(MattersInternalContact);
    }

    [Fact]
    public async Task NoActingUserNoParent_LeftBlank_TodoUnassignedLogged()
    {
        await Core(linkedContact: null).CreateAsync(
            new TaskActionInput("Standalone", null, null, null, null, null), CancellationToken.None);

        _created.Single().Contains("sprk_assignedto").Should().BeFalse();
        _logger.Entries.Should().Contain(e => e.Level == LogLevel.Warning && e.Message.Contains("todo_unassigned"));
    }

    [Fact]
    public async Task Executor_PassesThePlaybooksActingUser()
    {
        var executor = new CreateTaskNodeExecutor(
            new TemplateEngine(Microsoft.Extensions.Logging.Abstractions.NullLogger<TemplateEngine>.Instance),
            _entities.Object,
            CoreAncestorResolverFixtures.Inert(),
            new RecordOwnershipResolverDouble(),
            IdentityNormalizationFixtures.WithContact(ActingUsersContact).Object,
            Mock.Of<ICommunicationDataverseService>(),
            Mock.Of<ILogger<CreateTaskNodeExecutor>>());

        var actionId = Guid.NewGuid();
        var result = await executor.ExecuteAsync(new NodeExecutionContext
        {
            RunId = Guid.NewGuid(),
            PlaybookId = Guid.NewGuid(),
            UserId = ActingUser,
            Node = new PlaybookNodeDto
            {
                Id = Guid.NewGuid(),
                PlaybookId = Guid.NewGuid(),
                ActionId = actionId,
                Name = "Create Task",
                ExecutionOrder = 1,
                OutputVariable = "task",
                ConfigJson = """{"subject":"Review"}""",
                IsActive = true,
            },
            Action = new AnalysisAction { Id = actionId, Name = "Create Task" },
            ExecutorType = ExecutorType.CreateTask,
            Scopes = new ResolvedScopes([], [], []),
            TenantId = "t",
        }, CancellationToken.None);

        result.Success.Should().BeTrue();
        AssignedTo(_created.Single()).Should().Be(ActingUsersContact);
    }
}
