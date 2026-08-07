using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Nodes;
using Xunit;

namespace Sprk.Bff.Api.Tests.Seam.Ai.Nodes;

/// <summary>
/// CHARACTERIZATION seam tests for <see cref="CreateTaskNodeExecutor"/> (task 030, FR-07 pre-req).
/// </summary>
/// <remarks>
/// Pins the CURRENT shipped behavior BEFORE the Layer-A extraction (task 031). Uses a REAL
/// <see cref="TemplateEngine"/>; only the Dataverse-boundary <see cref="IGenericEntityService"/> is a
/// Moq double. The existing unit tests (<c>CreateTaskNodeExecutorTests</c>) mock the template engine and
/// do not assert the exact <c>task</c> entity field set nor the degraded-success (Guid.Empty) contract —
/// this seam suite pins both across the production slice.
/// </remarks>
public class CreateTaskNodeExecutorSeamTests
{
    private readonly Mock<IGenericEntityService> _entityServiceMock;
    private readonly CreateTaskNodeExecutor _executor;

    private static readonly Guid CreatedTaskId = Guid.Parse("55555555-5555-5555-5555-555555555555");

    public CreateTaskNodeExecutorSeamTests()
    {
        _entityServiceMock = new Mock<IGenericEntityService>();
        _entityServiceMock
            .Setup(s => s.CreateAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreatedTaskId);

        _executor = new CreateTaskNodeExecutor(
            new TemplateEngine(NullLogger<TemplateEngine>.Instance),
            _entityServiceMock.Object,
            NullLogger<CreateTaskNodeExecutor>.Instance);
    }

    // ── Happy path — exact task field set (criterion 6) ───────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_WithFullConfig_BuildsExactSprkEventTaskFieldSet()
    {
        // A Spaarke "task" is a sprk_event with event type = Task — NOT the OOB `task` activity
        // (corrected 2026-08-06). Regarding maps to sprk_event's TYPED lookup for the target entity
        // (here sprk_matter → sprk_regardingmatter); sprk_event has no regarding lookup for a document.
        var regardingId = Guid.Parse("66666666-6666-6666-6666-666666666666");
        var ownerId = Guid.Parse("77777777-7777-7777-7777-777777777777");
        var config = $$"""
        {
          "subject": "Review contract",
          "description": "Please review the uploaded contract.",
          "dueDate": "2026-08-01T00:00:00Z",
          "regardingObjectId": "{{regardingId}}",
          "regardingObjectType": "sprk_matter",
          "ownerId": "{{ownerId}}"
        }
        """;
        var context = CreateContext(config);
        Entity? captured = null;
        _entityServiceMock
            .Setup(s => s.CreateAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>()))
            .Callback<Entity, CancellationToken>((e, _) => captured = e)
            .ReturnsAsync(CreatedTaskId);

        // Act
        var result = await _executor.ExecuteAsync(context, CancellationToken.None);

        // Assert — NodeOutput shape
        result.Success.Should().BeTrue();
        result.TextContent.Should().Be("Task created: Review contract");
        var data = result.GetData<TaskCreatedData>();
        data.Should().NotBeNull();
        data!.TaskId.Should().Be(CreatedTaskId);
        data.Subject.Should().Be("Review contract");
        data.Description.Should().Be("Please review the uploaded contract.");

        // Assert — EXACT sprk_event (type=Task) field set
        captured.Should().NotBeNull();
        captured!.LogicalName.Should().Be("sprk_event");
        captured.Attributes.Keys.Should().BeEquivalentTo(new[]
        {
            "sprk_eventname", "sprk_eventtype_ref", "sprk_description", "sprk_duedate", "sprk_regardingmatter", "ownerid"
        });
        captured.GetAttributeValue<string>("sprk_eventname").Should().Be("Review contract");
        captured.GetAttributeValue<string>("sprk_description").Should().Be("Please review the uploaded contract.");
        captured.GetAttributeValue<DateTime>("sprk_duedate").Should().Be(
            new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc));
        // Event type = Task discriminator.
        var eventType = captured.GetAttributeValue<EntityReference>("sprk_eventtype_ref");
        eventType.LogicalName.Should().Be("sprk_eventtype_ref");
        eventType.Id.Should().Be(Guid.Parse("124f5fc9-98ff-f011-8406-7c1e525abd8b"));
        var regarding = captured.GetAttributeValue<EntityReference>("sprk_regardingmatter");
        regarding.LogicalName.Should().Be("sprk_matter");
        regarding.Id.Should().Be(regardingId);
        var owner = captured.GetAttributeValue<EntityReference>("ownerid");
        owner.LogicalName.Should().Be("systemuser");
        owner.Id.Should().Be(ownerId);
    }

    // ── Degraded success when Dataverse rejects the create (criterion 7) ──────────────────────

    [Fact]
    public async Task ExecuteAsync_WhenCreateThrows_ReturnsDegradedSuccessWithEmptyTaskId()
    {
        // Arrange — CreateAsync throws; the current contract swallows it and returns Ok/taskId=Empty.
        var config = @"{""subject"":""Review contract"",""description"":""Body""}";
        var context = CreateContext(config);
        _entityServiceMock
            .Setup(s => s.CreateAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Dataverse rejected the create"));

        // Act
        var result = await _executor.ExecuteAsync(context, CancellationToken.None);

        // Assert — does NOT propagate; degraded success with Guid.Empty taskId (pinned as-is)
        result.Success.Should().BeTrue();
        var data = result.GetData<TaskCreatedData>();
        data.Should().NotBeNull();
        data!.TaskId.Should().Be(Guid.Empty);
        data.Subject.Should().Be("Review contract");
    }

    // ── Validate() exact message (part of criterion 6 closed set) ─────────────────────────────

    [Fact]
    public void Validate_WithMissingSubject_ReturnsSubjectRequiredMessage()
    {
        var result = _executor.Validate(CreateContext(@"{""description"":""no subject""}"));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain("Task subject is required");
    }

    // ── Helpers ────────────────────────────────────────────────────────────────────────────────

    private static NodeExecutionContext CreateContext(string? configJson)
    {
        var actionId = Guid.NewGuid();
        return new NodeExecutionContext
        {
            RunId = Guid.NewGuid(),
            PlaybookId = Guid.NewGuid(),
            Node = new PlaybookNodeDto
            {
                Id = Guid.NewGuid(),
                PlaybookId = Guid.NewGuid(),
                ActionId = actionId,
                Name = "Create Task Node",
                ExecutionOrder = 1,
                OutputVariable = "taskResult",
                ConfigJson = configJson,
                IsActive = true
            },
            Action = new AnalysisAction { Id = actionId, Name = "Create Task" },
            ExecutorType = ExecutorType.CreateTask,
            Scopes = new ResolvedScopes([], [], []),
            TenantId = "test-tenant"
        };
    }

    private sealed class TaskCreatedData
    {
        public Guid TaskId { get; set; }
        public string Subject { get; set; } = string.Empty;
        public string? Description { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
    }
}
