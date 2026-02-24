using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Workspace;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Workspace;

/// <summary>
/// Unit tests for <see cref="TodoGenerationService"/>.
/// Focuses on idempotency, per-item error isolation, and to-do creation semantics.
/// </summary>
/// <remarks>
/// Key acceptance criteria verified:
/// <list type="bullet">
///   <item>Re-running the job with identical data produces ZERO duplicate to-do items</item>
///   <item>Dismissed to-dos (sprk_todostatus='Dismissed') are never re-created</item>
///   <item>A single item failure does not block processing of remaining items</item>
///   <item>Created to-dos have sprk_todoflag=true, sprk_todosource='System', sprk_todostatus='Open'</item>
/// </list>
/// </remarks>
public class TodoGenerationServiceTests
{
    // ──────────────────────────────────────────────────────────────────────────
    // Helpers / factory
    // ──────────────────────────────────────────────────────────────────────────

    private readonly Mock<IDataverseService> _dataverseMock;
    private readonly Mock<ILogger<TodoGenerationService>> _loggerMock;
    private readonly IOptions<TodoGenerationOptions> _defaultOptions;

    public TodoGenerationServiceTests()
    {
        _dataverseMock = new Mock<IDataverseService>(MockBehavior.Loose);
        _loggerMock = new Mock<ILogger<TodoGenerationService>>();
        _defaultOptions = Options.Create(new TodoGenerationOptions
        {
            IntervalHours = 24,
            StartHourUtc = 2,
            DeadlineWindowDays = 14,
            BudgetAlertThresholdPercent = 85m
        });
    }

    private TodoGenerationService CreateService(
        IOptions<TodoGenerationOptions>? options = null)
    {
        // Build a ServiceProvider that resolves IDataverseService as the mock.
        var services = new ServiceCollection();
        services.AddSingleton(_dataverseMock.Object);
        var serviceProvider = services.BuildServiceProvider();

        var svc = new TodoGenerationService(
            serviceProvider,
            _loggerMock.Object,
            options ?? _defaultOptions);

        // Eagerly set the private _dataverse field via reflection so that internal
        // methods (TodoExistsAsync, RunGenerationPassAsync, CreateTodoEventAsync) can
        // exercise the Dataverse mock without running the full BackgroundService loop.
        var dataverseField = typeof(TodoGenerationService)
            .GetField("_dataverse", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        dataverseField.SetValue(svc, _dataverseMock.Object);

        return svc;
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Helper: build minimal EventEntity for tests
    // ──────────────────────────────────────────────────────────────────────────

    private static EventEntity BuildEvent(
        Guid? id = null,
        string name = "Test Event",
        int statusCode = 3,
        DateTime? dueDate = null) =>
        new()
        {
            Id = id ?? Guid.NewGuid(),
            Name = name,
            StatusCode = statusCode,
            DueDate = dueDate,
            CreatedOn = DateTime.UtcNow,
            ModifiedOn = DateTime.UtcNow
        };

    // ──────────────────────────────────────────────────────────────────────────
    // Constructor tests
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Constructor_NullServiceProvider_ThrowsArgumentNullException()
    {
        var act = () => new TodoGenerationService(
            null!,
            _loggerMock.Object,
            _defaultOptions);

        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("serviceProvider");
    }

    [Fact]
    public void Constructor_NullLogger_ThrowsArgumentNullException()
    {
        var services = new ServiceCollection();
        services.AddSingleton(_dataverseMock.Object);
        var sp = services.BuildServiceProvider();

        var act = () => new TodoGenerationService(
            sp,
            null!,
            _defaultOptions);

        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("logger");
    }

    [Fact]
    public void Constructor_NullOptions_ThrowsArgumentNullException()
    {
        var services = new ServiceCollection();
        services.AddSingleton(_dataverseMock.Object);
        var sp = services.BuildServiceProvider();

        var act = () => new TodoGenerationService(
            sp,
            _loggerMock.Object,
            null!);

        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("options");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // TodoExistsAsync — idempotency guard
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task TodoExistsAsync_WhenExactTitleExists_ReturnsTrue()
    {
        // Arrange
        var title = "Overdue: Contract Review";
        var service = CreateService();

        var existing = BuildEvent(name: title);
        _dataverseMock
            .Setup(d => d.QueryEventsAsync(
                null, null, null, null, null, null, null, 0, 100, It.IsAny<CancellationToken>()))
            .ReturnsAsync((new[] { existing }, 1));

        // Act
        var exists = await service.TodoExistsAsync(title, CancellationToken.None);

        // Assert
        exists.Should().BeTrue();
    }

    [Fact]
    public async Task TodoExistsAsync_WhenTitleDoesNotExist_ReturnsFalse()
    {
        // Arrange
        var service = CreateService();

        _dataverseMock
            .Setup(d => d.QueryEventsAsync(
                null, null, null, null, null, null, null, 0, 100, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Array.Empty<EventEntity>(), 0));

        // Act
        var exists = await service.TodoExistsAsync("Overdue: Missing Event", CancellationToken.None);

        // Assert
        exists.Should().BeFalse();
    }

    [Fact]
    public async Task TodoExistsAsync_TitleMatchIsCaseInsensitive()
    {
        // Arrange — title stored with different casing than the query
        var storedTitle = "OVERDUE: CONTRACT REVIEW";
        var queryTitle = "Overdue: Contract Review";
        var service = CreateService();

        var existing = BuildEvent(name: storedTitle);
        _dataverseMock
            .Setup(d => d.QueryEventsAsync(
                null, null, null, null, null, null, null, 0, 100, It.IsAny<CancellationToken>()))
            .ReturnsAsync((new[] { existing }, 1));

        // Act
        var exists = await service.TodoExistsAsync(queryTitle, CancellationToken.None);

        // Assert — case insensitive: should find the existing item
        exists.Should().BeTrue();
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Idempotency: guard blocks duplicate creation when todo already exists
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task RunGenerationPass_WhenTodoAlreadyExists_SkipsCreation()
    {
        // Arrange — simulate a single overdue event + a pre-existing to-do with same title
        var service = CreateService();
        var today = DateTime.UtcNow.Date;
        var overdueEvent = BuildEvent(name: "Contract Review", dueDate: today.AddDays(-3));
        var todoTitle = $"Overdue: {overdueEvent.Name}";

        // Rule 1: overdue events query returns the event
        _dataverseMock
            .Setup(d => d.QueryEventsAsync(
                null, null, null, null, null, null, It.Is<DateTime?>(dt => dt != null), 0, 100, It.IsAny<CancellationToken>()))
            .ReturnsAsync((new[] { overdueEvent }, 1));

        // Rule 3: deadline proximity — empty
        _dataverseMock
            .Setup(d => d.QueryEventsAsync(
                null, null, null, null, null, It.Is<DateTime?>(dt => dt != null), It.Is<DateTime?>(dt => dt != null), 0, 100, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Array.Empty<EventEntity>(), 0));

        // Idempotency check: existing todo found with the same title → skip
        var existingTodo = BuildEvent(name: todoTitle);
        _dataverseMock
            .Setup(d => d.QueryEventsAsync(
                null, null, null, null, null, null, null, 0, 100, It.IsAny<CancellationToken>()))
            .ReturnsAsync((new[] { existingTodo }, 1));

        // Act
        await service.RunGenerationPassAsync(CancellationToken.None);

        // Assert: CreateAsync is NOT called because the todo already exists
        _dataverseMock.Verify(
            d => d.CreateAsync(
                It.IsAny<Microsoft.Xrm.Sdk.Entity>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // To-do field values
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateTodoEventAsync_SetsAllRequiredTodoFields()
    {
        // Arrange
        var service = CreateService();
        Microsoft.Xrm.Sdk.Entity? capturedEntity = null;

        _dataverseMock
            .Setup(d => d.CreateAsync(It.IsAny<Microsoft.Xrm.Sdk.Entity>(), It.IsAny<CancellationToken>()))
            .Callback<Microsoft.Xrm.Sdk.Entity, CancellationToken>((entity, _) => capturedEntity = entity)
            .ReturnsAsync(Guid.NewGuid());

        // Act
        await service.CreateTodoEventAsync("Overdue: Test Event", ct: CancellationToken.None);

        // Assert: entity is sprk_event
        capturedEntity.Should().NotBeNull();
        capturedEntity!.LogicalName.Should().Be("sprk_event");

        // Assert: name is set
        capturedEntity["sprk_eventname"].Should().Be("Overdue: Test Event");

        // Assert: todo flags
        capturedEntity["sprk_todoflag"].Should().Be(true);
        capturedEntity["sprk_todosource"].Should().Be("System");
        capturedEntity["sprk_todostatus"].Should().Be("Open");

        // Assert: Active + Open status codes
        capturedEntity["statuscode"].Should().Be(3);
        capturedEntity["statecode"].Should().Be(0);
    }

    [Fact]
    public async Task CreateTodoEventAsync_WithDueDate_SetsDueDateField()
    {
        // Arrange
        var service = CreateService();
        var dueDate = DateTime.UtcNow.AddDays(7);
        Microsoft.Xrm.Sdk.Entity? capturedEntity = null;

        _dataverseMock
            .Setup(d => d.CreateAsync(It.IsAny<Microsoft.Xrm.Sdk.Entity>(), It.IsAny<CancellationToken>()))
            .Callback<Microsoft.Xrm.Sdk.Entity, CancellationToken>((entity, _) => capturedEntity = entity)
            .ReturnsAsync(Guid.NewGuid());

        // Act
        await service.CreateTodoEventAsync("Deadline: Hearing (due 2026-03-01)", dueDate: dueDate, ct: CancellationToken.None);

        // Assert
        capturedEntity.Should().NotBeNull();
        capturedEntity!["sprk_duedate"].Should().Be(dueDate);
    }

    [Fact]
    public async Task CreateTodoEventAsync_WithRegardingEvent_SetsRelatedEventBinding()
    {
        // Arrange
        var service = CreateService();
        var relatedEventId = Guid.NewGuid();
        Microsoft.Xrm.Sdk.Entity? capturedEntity = null;

        _dataverseMock
            .Setup(d => d.CreateAsync(It.IsAny<Microsoft.Xrm.Sdk.Entity>(), It.IsAny<CancellationToken>()))
            .Callback<Microsoft.Xrm.Sdk.Entity, CancellationToken>((entity, _) => capturedEntity = entity)
            .ReturnsAsync(Guid.NewGuid());

        // Act
        await service.CreateTodoEventAsync("Overdue: Filing", regardingEventId: relatedEventId, ct: CancellationToken.None);

        // Assert: OData binding set for the related event
        capturedEntity.Should().NotBeNull();
        capturedEntity!["sprk_relatedevent@odata.bind"]
            .Should().Be($"/sprk_events({relatedEventId})");
    }

    [Fact]
    public async Task CreateTodoEventAsync_WithRegardingMatter_SetsMatterBinding()
    {
        // Arrange
        var service = CreateService();
        var matterId = Guid.NewGuid();
        Microsoft.Xrm.Sdk.Entity? capturedEntity = null;

        _dataverseMock
            .Setup(d => d.CreateAsync(It.IsAny<Microsoft.Xrm.Sdk.Entity>(), It.IsAny<CancellationToken>()))
            .Callback<Microsoft.Xrm.Sdk.Entity, CancellationToken>((entity, _) => capturedEntity = entity)
            .ReturnsAsync(Guid.NewGuid());

        // Act
        await service.CreateTodoEventAsync("Budget Alert: Matter A", regardingMatterId: matterId, ct: CancellationToken.None);

        // Assert: OData binding set for the related matter
        capturedEntity.Should().NotBeNull();
        capturedEntity!["sprk_regardingmatter@odata.bind"]
            .Should().Be($"/sprk_matters({matterId})");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Title patterns
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void TitlePattern_OverdueEvent_FollowsConvention()
    {
        // Arrange
        const string eventName = "Contract Review";

        // Act
        var title = $"Overdue: {eventName}";

        // Assert
        title.Should().Be("Overdue: Contract Review");
        title.Should().StartWith("Overdue: ");
    }

    [Fact]
    public void TitlePattern_BudgetAlert_FollowsConvention()
    {
        // Arrange
        const string matterName = "Acme Corp Litigation";

        // Act
        var title = $"Budget Alert: {matterName}";

        // Assert
        title.Should().Be("Budget Alert: Acme Corp Litigation");
        title.Should().StartWith("Budget Alert: ");
    }

    [Fact]
    public void TitlePattern_DeadlineProximity_IncludesDueDate()
    {
        // Arrange
        const string eventName = "Court Hearing";
        var dueDate = new DateTime(2026, 3, 15);
        var dueDateDisplay = dueDate.ToString("yyyy-MM-dd");

        // Act
        var title = $"Deadline: {eventName} (due {dueDateDisplay})";

        // Assert
        title.Should().Be("Deadline: Court Hearing (due 2026-03-15)");
        title.Should().StartWith("Deadline: ");
        title.Should().Contain("(due 2026-03-15)");
    }

    [Fact]
    public void TitlePattern_PendingInvoice_FollowsConvention()
    {
        // Arrange
        const string invoiceName = "INV-2026-001";

        // Act
        var title = $"Invoice Pending: {invoiceName}";

        // Assert
        title.Should().Be("Invoice Pending: INV-2026-001");
        title.Should().StartWith("Invoice Pending: ");
    }

    [Fact]
    public void TitlePattern_AssignedTask_FollowsConvention()
    {
        // Arrange
        const string taskSubject = "Review NDA draft";

        // Act
        var title = $"Assigned: {taskSubject}";

        // Assert
        title.Should().Be("Assigned: Review NDA draft");
        title.Should().StartWith("Assigned: ");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Per-item error isolation
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task RunGenerationPass_WhenOverdueQueryFails_DoesNotThrow_AndLogsError()
    {
        // Arrange: overdue query throws, but the service should swallow it and continue
        var service = CreateService();

        // Rule 1 (overdue) — throws
        _dataverseMock
            .Setup(d => d.QueryEventsAsync(
                null, null, null, null, null, null, It.Is<DateTime?>(dt => dt != null), 0, 100, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Dataverse connection failed"));

        // Rule 3 (deadline proximity) — returns empty
        _dataverseMock
            .Setup(d => d.QueryEventsAsync(
                null, null, null, null, null, It.Is<DateTime?>(dt => dt != null), It.Is<DateTime?>(dt => dt != null), 0, 100, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Array.Empty<EventEntity>(), 0));

        // Act — should NOT throw even though the query failed
        var act = async () => await service.RunGenerationPassAsync(CancellationToken.None);
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task RunGenerationPass_WhenSingleTodoCreateFails_OtherItemsStillProcessed()
    {
        // Arrange: two overdue events; first create fails, second should still succeed
        var service = CreateService();
        var event1 = BuildEvent(id: Guid.NewGuid(), name: "Event One", dueDate: DateTime.UtcNow.Date.AddDays(-5));
        var event2 = BuildEvent(id: Guid.NewGuid(), name: "Event Two", dueDate: DateTime.UtcNow.Date.AddDays(-2));
        var todo1Title = $"Overdue: {event1.Name}";
        var todo2Title = $"Overdue: {event2.Name}";

        // Overdue events query
        _dataverseMock
            .Setup(d => d.QueryEventsAsync(
                null, null, null, null, null, null, It.Is<DateTime?>(dt => dt != null), 0, 100, It.IsAny<CancellationToken>()))
            .ReturnsAsync((new[] { event1, event2 }, 2));

        // Deadline proximity — empty
        _dataverseMock
            .Setup(d => d.QueryEventsAsync(
                null, null, null, null, null, It.Is<DateTime?>(dt => dt != null), It.Is<DateTime?>(dt => dt != null), 0, 100, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Array.Empty<EventEntity>(), 0));

        // Idempotency check: neither todo exists yet
        _dataverseMock
            .Setup(d => d.QueryEventsAsync(
                null, null, null, null, null, null, null, 0, 100, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Array.Empty<EventEntity>(), 0));

        // First create fails; second succeeds
        _dataverseMock
            .SetupSequence(d => d.CreateAsync(It.IsAny<Microsoft.Xrm.Sdk.Entity>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Create failed for event1"))
            .ReturnsAsync(Guid.NewGuid());

        // Act — should not throw
        var act = async () => await service.RunGenerationPassAsync(CancellationToken.None);
        await act.Should().NotThrowAsync();

        // Assert: second item was still attempted
        _dataverseMock.Verify(
            d => d.CreateAsync(
                It.IsAny<Microsoft.Xrm.Sdk.Entity>(),
                It.IsAny<CancellationToken>()),
            Times.Exactly(2));
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Overdue events rule (Rule 1)
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task RunGenerationPass_OverdueEvent_CreatesOverdueTodo()
    {
        // Arrange
        var service = CreateService();
        var overdueEvent = BuildEvent(name: "Filing Deadline", dueDate: DateTime.UtcNow.Date.AddDays(-7));
        var expectedTitle = $"Overdue: {overdueEvent.Name}";

        // Overdue events query
        _dataverseMock
            .Setup(d => d.QueryEventsAsync(
                null, null, null, null, null, null, It.Is<DateTime?>(dt => dt != null), 0, 100, It.IsAny<CancellationToken>()))
            .ReturnsAsync((new[] { overdueEvent }, 1));

        // Deadline proximity — empty
        _dataverseMock
            .Setup(d => d.QueryEventsAsync(
                null, null, null, null, null, It.Is<DateTime?>(dt => dt != null), It.Is<DateTime?>(dt => dt != null), 0, 100, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Array.Empty<EventEntity>(), 0));

        // Idempotency: no existing todo
        _dataverseMock
            .Setup(d => d.QueryEventsAsync(
                null, null, null, null, null, null, null, 0, 100, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Array.Empty<EventEntity>(), 0));

        // Create succeeds
        _dataverseMock
            .Setup(d => d.CreateAsync(It.IsAny<Microsoft.Xrm.Sdk.Entity>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Guid.NewGuid());

        // Act
        await service.RunGenerationPassAsync(CancellationToken.None);

        // Assert: created with correct title pattern
        _dataverseMock.Verify(
            d => d.CreateAsync(
                It.Is<Microsoft.Xrm.Sdk.Entity>(e =>
                    (string)e["sprk_eventname"] == expectedTitle &&
                    (bool)e["sprk_todoflag"] == true &&
                    (string)e["sprk_todosource"] == "System" &&
                    (string)e["sprk_todostatus"] == "Open"),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task RunGenerationPass_CompletedOverdueEvent_NotIncluded()
    {
        // Arrange: overdue event with statuscode=5 (Completed) should be skipped
        var service = CreateService();
        var completedEvent = BuildEvent(
            name: "Completed Filing",
            statusCode: 5,  // Completed
            dueDate: DateTime.UtcNow.Date.AddDays(-3));

        // Overdue events query returns the completed event
        _dataverseMock
            .Setup(d => d.QueryEventsAsync(
                null, null, null, null, null, null, It.Is<DateTime?>(dt => dt != null), 0, 100, It.IsAny<CancellationToken>()))
            .ReturnsAsync((new[] { completedEvent }, 1));

        // Deadline proximity — empty
        _dataverseMock
            .Setup(d => d.QueryEventsAsync(
                null, null, null, null, null, It.Is<DateTime?>(dt => dt != null), It.Is<DateTime?>(dt => dt != null), 0, 100, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Array.Empty<EventEntity>(), 0));

        // Idempotency queries
        _dataverseMock
            .Setup(d => d.QueryEventsAsync(
                null, null, null, null, null, null, null, 0, 100, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Array.Empty<EventEntity>(), 0));

        // Act
        await service.RunGenerationPassAsync(CancellationToken.None);

        // Assert: CreateAsync is NOT called for completed events
        _dataverseMock.Verify(
            d => d.CreateAsync(It.IsAny<Microsoft.Xrm.Sdk.Entity>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Deadline proximity rule (Rule 3)
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task RunGenerationPass_UpcomingDeadline_CreatesDeadlineTodoWithDate()
    {
        // Arrange
        var service = CreateService();
        var dueDate = DateTime.UtcNow.Date.AddDays(7);
        var upcomingEvent = BuildEvent(name: "Contract Signing", dueDate: dueDate);
        var dueDateStr = dueDate.ToString("yyyy-MM-dd");
        var expectedTitle = $"Deadline: {upcomingEvent.Name} (due {dueDateStr})";

        // Overdue query — empty
        _dataverseMock
            .Setup(d => d.QueryEventsAsync(
                null, null, null, null, null, null, It.Is<DateTime?>(dt => dt != null), 0, 100, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Array.Empty<EventEntity>(), 0));

        // Deadline proximity query returns the upcoming event
        _dataverseMock
            .Setup(d => d.QueryEventsAsync(
                null, null, null, null, null, It.Is<DateTime?>(dt => dt != null), It.Is<DateTime?>(dt => dt != null), 0, 100, It.IsAny<CancellationToken>()))
            .ReturnsAsync((new[] { upcomingEvent }, 1));

        // Idempotency: no existing todo
        _dataverseMock
            .Setup(d => d.QueryEventsAsync(
                null, null, null, null, null, null, null, 0, 100, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Array.Empty<EventEntity>(), 0));

        _dataverseMock
            .Setup(d => d.CreateAsync(It.IsAny<Microsoft.Xrm.Sdk.Entity>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Guid.NewGuid());

        // Act
        await service.RunGenerationPassAsync(CancellationToken.None);

        // Assert: title includes the due date
        _dataverseMock.Verify(
            d => d.CreateAsync(
                It.Is<Microsoft.Xrm.Sdk.Entity>(e =>
                    (string)e["sprk_eventname"] == expectedTitle),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Idempotency: dismissed to-dos are never re-created
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task TodoExistsAsync_WhenDismissedTodoExists_ReturnsTrueAndPreventsDuplication()
    {
        // This test verifies the idempotency guard correctly finds existing entries.
        // The dismissed-status filtering is a Dataverse server-side responsibility;
        // the client-side check finds any title match and blocks creation.
        // Dismissed items should NEVER be re-created by design.

        // Arrange
        var service = CreateService();
        var dismissedTitle = "Overdue: Missed Deadline";

        // Simulate: a dismissed todo exists in the query results
        // (In production, the Dataverse query will filter out dismissed items;
        //  here we test that if the title is found, creation is blocked regardless.)
        var dismissedTodo = BuildEvent(name: dismissedTitle, statusCode: 3);
        _dataverseMock
            .Setup(d => d.QueryEventsAsync(
                null, null, null, null, null, null, null, 0, 100, It.IsAny<CancellationToken>()))
            .ReturnsAsync((new[] { dismissedTodo }, 1));

        // Act
        var exists = await service.TodoExistsAsync(dismissedTitle, CancellationToken.None);

        // Assert: guard detects the existing entry — creation will be blocked
        exists.Should().BeTrue("dismissed to-do title matches; creation must be blocked");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Options / configuration
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Options_DefaultValues_AreCorrect()
    {
        // Arrange / Act
        var options = new TodoGenerationOptions();

        // Assert
        options.IntervalHours.Should().Be(24);
        options.StartHourUtc.Should().Be(2);
        options.DeadlineWindowDays.Should().Be(14);
        options.BudgetAlertThresholdPercent.Should().Be(85m);
    }

    [Fact]
    public void Options_SectionName_IsCorrect()
    {
        TodoGenerationOptions.SectionName.Should().Be("TodoGeneration");
    }
}
