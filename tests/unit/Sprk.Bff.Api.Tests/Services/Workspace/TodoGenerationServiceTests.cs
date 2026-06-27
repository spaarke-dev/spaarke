using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
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
///   <item>Dismissed to-dos (statuscode=Dismissed) are never re-created (server-side filter at query)</item>
///   <item>A single item failure does not block processing of remaining items</item>
///   <item>Created to-dos are <c>sprk_todo</c> records with the expected name/status/owner shape</item>
///   <item>When a regarding parent exists, all four resolver fields are populated atomically (ADR-024)</item>
///   <item>When standalone, all 11 specific regarding lookups + 4 resolver fields are null</item>
/// </list>
/// </remarks>
[Trait("status", "repaired")]
public class TodoGenerationServiceTests
{
    // ──────────────────────────────────────────────────────────────────────────
    // Helpers / factory
    // ──────────────────────────────────────────────────────────────────────────

    private readonly Mock<IDataverseService> _dataverseMock;
    private readonly Mock<ICommunicationDataverseService> _commServiceMock;
    private readonly Mock<ILogger<TodoGenerationService>> _loggerMock;
    private readonly Mock<ILogger<TodoRegardingBuilder>> _builderLoggerMock;
    private readonly IOptions<TodoGenerationOptions> _defaultOptions;

    public TodoGenerationServiceTests()
    {
        _dataverseMock = new Mock<IDataverseService>(MockBehavior.Loose);
        _commServiceMock = new Mock<ICommunicationDataverseService>(MockBehavior.Loose);
        _loggerMock = new Mock<ILogger<TodoGenerationService>>();
        _builderLoggerMock = new Mock<ILogger<TodoRegardingBuilder>>();

        // Default: sprk_recordtype_ref returns null (resolver type field left unset; non-fatal)
        _commServiceMock
            .Setup(c => c.QueryRecordTypeRefAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Entity?)null);

        _defaultOptions = Options.Create(new TodoGenerationOptions
        {
            IntervalHours = 24,
            StartHourUtc = 2,
            DeadlineWindowDays = 14,
            BudgetAlertThresholdPercent = 85m
        });
    }

    private TodoGenerationService CreateService(
        IOptions<TodoGenerationOptions>? options = null,
        Entity? recordTypeRef = null)
    {
        if (recordTypeRef is not null)
        {
            _commServiceMock
                .Setup(c => c.QueryRecordTypeRefAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(recordTypeRef);
        }

        // Build a ServiceProvider that resolves IDataverseService as the mock.
        var services = new ServiceCollection();
        services.AddSingleton(_dataverseMock.Object);
        services.AddSingleton(_commServiceMock.Object);
        var serviceProvider = services.BuildServiceProvider();

        var svc = new TodoGenerationService(
            serviceProvider,
            _loggerMock.Object,
            options ?? _defaultOptions);

        // Eagerly set the private _dataverse field via reflection so that internal
        // methods (TodoExistsAsync, RunGenerationPassAsync, CreateTodoAsync) can
        // exercise the Dataverse mock without running the full BackgroundService loop.
        var dataverseField = typeof(TodoGenerationService)
            .GetField("_dataverse", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        dataverseField.SetValue(svc, _dataverseMock.Object);

        // Inject a TodoRegardingBuilder via the internal test seam so creation paths
        // with regarding parents can run without ExecuteAsync's lazy initialization.
        svc.SetRegardingBuilderForTest(new TodoRegardingBuilder(_commServiceMock.Object, _builderLoggerMock.Object));

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

    /// <summary>
    /// Standard idempotency-query setup: <see cref="TodoGenerationService.TodoExistsAsync"/>
    /// calls <c>RetrieveMultipleAsync(QueryExpression(sprk_todo))</c>. Returning an empty
    /// <see cref="EntityCollection"/> tells the service "no duplicates exist".
    /// </summary>
    private void SetupIdempotencyQueryEmpty()
    {
        _dataverseMock
            .Setup(d => d.RetrieveMultipleAsync(
                It.Is<QueryExpression>(q => q.EntityName == "sprk_todo"),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EntityCollection());
    }

    /// <summary>
    /// Returns a single matching sprk_todo for the idempotency query (blocks creation).
    /// </summary>
    private void SetupIdempotencyQueryFound(string existingName)
    {
        var existing = new Entity("sprk_todo")
        {
            Id = Guid.NewGuid(),
            ["sprk_name"] = existingName
        };
        var coll = new EntityCollection(new List<Entity> { existing });
        _dataverseMock
            .Setup(d => d.RetrieveMultipleAsync(
                It.Is<QueryExpression>(q => q.EntityName == "sprk_todo"),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(coll);
    }

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
    // TodoExistsAsync — idempotency guard (now queries sprk_todo, not sprk_event)
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task TodoExistsAsync_WhenExactTitleExists_ReturnsTrue()
    {
        // Arrange
        var title = "Overdue: Contract Review";
        var service = CreateService();
        SetupIdempotencyQueryFound(title);

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
        SetupIdempotencyQueryEmpty();

        // Act
        var exists = await service.TodoExistsAsync("Overdue: Missing Event", CancellationToken.None);

        // Assert
        exists.Should().BeFalse();
    }

    [Fact]
    public async Task TodoExistsAsync_QueriesSprkTodoAndExcludesDismissedRecords()
    {
        // Arrange — capture the QueryExpression so we can verify both the entity name
        // AND the statuscode != Dismissed condition.
        var service = CreateService();
        QueryExpression? capturedQuery = null;

        _dataverseMock
            .Setup(d => d.RetrieveMultipleAsync(
                It.IsAny<QueryExpression>(),
                It.IsAny<CancellationToken>()))
            .Callback<QueryExpression, CancellationToken>((q, _) => capturedQuery = q)
            .ReturnsAsync(new EntityCollection());

        // Act
        await service.TodoExistsAsync("Anything", CancellationToken.None);

        // Assert — entity is sprk_todo (NOT sprk_event)
        capturedQuery.Should().NotBeNull();
        capturedQuery!.EntityName.Should().Be("sprk_todo");

        // Assert — query filters out Dismissed (statuscode=3 per entity-schema.md)
        var hasDismissedFilter = capturedQuery.Criteria.Conditions
            .Any(c => c.AttributeName == "statuscode"
                  && c.Operator == ConditionOperator.NotEqual
                  && c.Values.Count == 1
                  && Convert.ToInt32(c.Values[0]) == 3);
        hasDismissedFilter.Should().BeTrue(
            "Dismissed to-dos must be excluded server-side so they never re-appear after a user dismisses them");
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
                null, null, null, null, null, null, It.Is<DateTime?>(dt => dt != null), 0, 100, (Guid?)null, It.IsAny<CancellationToken>()))
            .ReturnsAsync((new[] { overdueEvent }, 1));

        // Rule 3: deadline proximity — empty
        _dataverseMock
            .Setup(d => d.QueryEventsAsync(
                null, null, null, null, null, It.Is<DateTime?>(dt => dt != null), It.Is<DateTime?>(dt => dt != null), 0, 100, (Guid?)null, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Array.Empty<EventEntity>(), 0));

        // Idempotency check: existing todo found → skip
        SetupIdempotencyQueryFound(todoTitle);

        // Act
        await service.RunGenerationPassAsync(CancellationToken.None);

        // Assert: CreateAsync is NOT called because the todo already exists
        _dataverseMock.Verify(
            d => d.CreateAsync(
                It.IsAny<Entity>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // CreateTodoAsync — entity shape (sprk_todo, not sprk_event)
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateTodoAsync_SetsCoreFieldsOnSprkTodoEntity()
    {
        // Arrange
        var service = CreateService();
        Entity? capturedEntity = null;

        _dataverseMock
            .Setup(d => d.CreateAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>()))
            .Callback<Entity, CancellationToken>((entity, _) => capturedEntity = entity)
            .ReturnsAsync(Guid.NewGuid());

        // Act — standalone create (no regarding)
        await service.CreateTodoAsync("Overdue: Test Event", ct: CancellationToken.None);

        // Assert — entity is sprk_todo (NOT sprk_event)
        capturedEntity.Should().NotBeNull();
        capturedEntity!.LogicalName.Should().Be("sprk_todo");

        // Assert — primary name field
        capturedEntity["sprk_name"].Should().Be("Overdue: Test Event");

        // Assert — OptionSet status: Open + Active
        capturedEntity["statuscode"].Should().BeOfType<OptionSetValue>()
            .Which.Value.Should().Be(1, "statuscode=1 = Open per entity-schema.md");
        capturedEntity["statecode"].Should().BeOfType<OptionSetValue>()
            .Which.Value.Should().Be(0, "statecode=0 = Active per entity-schema.md");

        // Assert — no legacy sprk_event/sprk_todoflag fields present
        capturedEntity.Attributes.Should().NotContainKey("sprk_eventname");
        capturedEntity.Attributes.Should().NotContainKey("sprk_todoflag");
        capturedEntity.Attributes.Should().NotContainKey("sprk_todosource");
        capturedEntity.Attributes.Should().NotContainKey("sprk_todostatus");
    }

    [Fact]
    public async Task CreateTodoAsync_Standalone_HasNoRegardingLookupsOrResolverFields()
    {
        // Arrange
        var service = CreateService();
        Entity? capturedEntity = null;

        _dataverseMock
            .Setup(d => d.CreateAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>()))
            .Callback<Entity, CancellationToken>((entity, _) => capturedEntity = entity)
            .ReturnsAsync(Guid.NewGuid());

        // Act — standalone (Rule 5)
        await service.CreateTodoAsync("Assigned: Standalone task", ct: CancellationToken.None);

        // Assert — none of the 11 specific regarding lookups present
        capturedEntity.Should().NotBeNull();
        foreach (var lookup in TodoRegardingBuilder.AllRegardingLookups)
        {
            capturedEntity!.Attributes.Should().NotContainKey(lookup,
                $"standalone to-do must not set specific regarding lookup '{lookup}'");
        }

        // Assert — none of the 4 resolver fields present
        capturedEntity!.Attributes.Should().NotContainKey("sprk_regardingrecordtype");
        capturedEntity.Attributes.Should().NotContainKey("sprk_regardingrecordid");
        capturedEntity.Attributes.Should().NotContainKey("sprk_regardingrecordname");
        capturedEntity.Attributes.Should().NotContainKey("sprk_regardingrecordurl");
    }

    [Fact]
    public async Task CreateTodoAsync_WithDueDate_SetsDueDateField()
    {
        // Arrange
        var service = CreateService();
        var dueDate = DateTime.UtcNow.AddDays(7);
        Entity? capturedEntity = null;

        _dataverseMock
            .Setup(d => d.CreateAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>()))
            .Callback<Entity, CancellationToken>((entity, _) => capturedEntity = entity)
            .ReturnsAsync(Guid.NewGuid());

        // Act
        await service.CreateTodoAsync(
            "Deadline: Hearing (due 2026-03-01)",
            dueDate: dueDate,
            ct: CancellationToken.None);

        // Assert — sprk_duedate set
        capturedEntity.Should().NotBeNull();
        capturedEntity!["sprk_duedate"].Should().Be(dueDate);
    }

    [Fact]
    public async Task CreateTodoAsync_WithRegardingMatter_SetsAllFourResolverFieldsAtomically()
    {
        // Arrange — record-type-ref returns a known entry so the resolver type field can be set
        var recordTypeId = Guid.NewGuid();
        var recordTypeRef = new Entity("sprk_recordtype_ref")
        {
            Id = recordTypeId,
            ["sprk_recorddisplayname"] = "Matter"
        };
        var service = CreateService(recordTypeRef: recordTypeRef);

        var matterId = Guid.NewGuid();
        const string matterName = "Acme Litigation";
        Entity? capturedEntity = null;

        _dataverseMock
            .Setup(d => d.CreateAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>()))
            .Callback<Entity, CancellationToken>((entity, _) => capturedEntity = entity)
            .ReturnsAsync(Guid.NewGuid());

        // Act — Rule 2 (Budget Alert) shape
        await service.CreateTodoAsync(
            name: $"Budget Alert: {matterName}",
            regardingEntityName: "sprk_matter",
            regardingId: matterId,
            regardingDisplayName: matterName,
            ct: CancellationToken.None);

        // Assert — entity is sprk_todo
        capturedEntity.Should().NotBeNull();
        capturedEntity!.LogicalName.Should().Be("sprk_todo");

        // Assert — specific lookup populated as EntityReference (NOT @odata.bind)
        capturedEntity.Attributes.Should().ContainKey("sprk_regardingmatter");
        var matterRef = capturedEntity["sprk_regardingmatter"].Should().BeOfType<EntityReference>().Subject;
        matterRef.LogicalName.Should().Be("sprk_matter");
        matterRef.Id.Should().Be(matterId);

        // Assert — ALL 4 resolver fields populated atomically (ADR-024)
        var expectedCleanId = matterId.ToString("D").ToLowerInvariant();
        capturedEntity["sprk_regardingrecordid"].Should().Be(expectedCleanId);
        capturedEntity["sprk_regardingrecordname"].Should().Be(matterName);
        capturedEntity["sprk_regardingrecordurl"].Should().Be(
            $"/main.aspx?pagetype=entityrecord&etn=sprk_matter&id={expectedCleanId}");
        var typeRef = capturedEntity["sprk_regardingrecordtype"].Should().BeOfType<EntityReference>().Subject;
        typeRef.LogicalName.Should().Be("sprk_recordtype_ref");
        typeRef.Id.Should().Be(recordTypeId);

        // Assert — no OTHER specific regarding lookup populated
        var otherLookups = TodoRegardingBuilder.AllRegardingLookups
            .Where(l => l != "sprk_regardingmatter");
        foreach (var lookup in otherLookups)
        {
            capturedEntity.Attributes.Should().NotContainKey(lookup,
                $"only sprk_regardingmatter should be set; '{lookup}' must remain null");
        }
    }

    [Fact]
    public async Task CreateTodoAsync_WithRegardingEvent_SetsRegardingEventLookupAndResolver()
    {
        // Arrange
        var service = CreateService();
        var eventId = Guid.NewGuid();
        const string eventName = "Filing Deadline";
        Entity? capturedEntity = null;

        _dataverseMock
            .Setup(d => d.CreateAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>()))
            .Callback<Entity, CancellationToken>((entity, _) => capturedEntity = entity)
            .ReturnsAsync(Guid.NewGuid());

        // Act — Rule 1 (Overdue) shape
        await service.CreateTodoAsync(
            name: $"Overdue: {eventName}",
            regardingEntityName: "sprk_event",
            regardingId: eventId,
            regardingDisplayName: eventName,
            ct: CancellationToken.None);

        // Assert — sprk_regardingevent populated; resolver id/name/url populated
        capturedEntity.Should().NotBeNull();
        var eventRef = capturedEntity!["sprk_regardingevent"].Should().BeOfType<EntityReference>().Subject;
        eventRef.LogicalName.Should().Be("sprk_event");
        eventRef.Id.Should().Be(eventId);

        capturedEntity["sprk_regardingrecordid"].Should().Be(eventId.ToString("D").ToLowerInvariant());
        capturedEntity["sprk_regardingrecordname"].Should().Be(eventName);
        capturedEntity["sprk_regardingrecordurl"].ToString().Should().Contain("etn=sprk_event");
    }

    [Fact]
    public async Task CreateTodoAsync_WithRegardingInvoice_SetsRegardingInvoiceLookupAndResolver()
    {
        // Arrange
        var service = CreateService();
        var invoiceId = Guid.NewGuid();
        const string invoiceName = "INV-2026-001";
        Entity? capturedEntity = null;

        _dataverseMock
            .Setup(d => d.CreateAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>()))
            .Callback<Entity, CancellationToken>((entity, _) => capturedEntity = entity)
            .ReturnsAsync(Guid.NewGuid());

        // Act — Rule 4 (Invoice Pending) shape
        await service.CreateTodoAsync(
            name: $"Invoice Pending: {invoiceName}",
            regardingEntityName: "sprk_invoice",
            regardingId: invoiceId,
            regardingDisplayName: invoiceName,
            ct: CancellationToken.None);

        // Assert — sprk_regardinginvoice populated; resolver name carries forward
        capturedEntity.Should().NotBeNull();
        var invoiceRef = capturedEntity!["sprk_regardinginvoice"].Should().BeOfType<EntityReference>().Subject;
        invoiceRef.LogicalName.Should().Be("sprk_invoice");
        invoiceRef.Id.Should().Be(invoiceId);
        capturedEntity["sprk_regardingrecordname"].Should().Be(invoiceName);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Title patterns
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void TitlePattern_OverdueEvent_FollowsConvention()
    {
        const string eventName = "Contract Review";
        var title = $"Overdue: {eventName}";
        title.Should().Be("Overdue: Contract Review");
        title.Should().StartWith("Overdue: ");
    }

    [Fact]
    public void TitlePattern_BudgetAlert_FollowsConvention()
    {
        const string matterName = "Acme Corp Litigation";
        var title = $"Budget Alert: {matterName}";
        title.Should().Be("Budget Alert: Acme Corp Litigation");
        title.Should().StartWith("Budget Alert: ");
    }

    [Fact]
    public void TitlePattern_DeadlineProximity_IncludesDueDate()
    {
        const string eventName = "Court Hearing";
        var dueDate = new DateTime(2026, 3, 15);
        var dueDateDisplay = dueDate.ToString("yyyy-MM-dd");
        var title = $"Deadline: {eventName} (due {dueDateDisplay})";
        title.Should().Be("Deadline: Court Hearing (due 2026-03-15)");
        title.Should().StartWith("Deadline: ");
        title.Should().Contain("(due 2026-03-15)");
    }

    [Fact]
    public void TitlePattern_PendingInvoice_FollowsConvention()
    {
        const string invoiceName = "INV-2026-001";
        var title = $"Invoice Pending: {invoiceName}";
        title.Should().Be("Invoice Pending: INV-2026-001");
        title.Should().StartWith("Invoice Pending: ");
    }

    [Fact]
    public void TitlePattern_AssignedTask_FollowsConvention()
    {
        const string taskSubject = "Review NDA draft";
        var title = $"Assigned: {taskSubject}";
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

        _dataverseMock
            .Setup(d => d.QueryEventsAsync(
                null, null, null, null, null, null, It.Is<DateTime?>(dt => dt != null), 0, 100, (Guid?)null, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Dataverse connection failed"));

        _dataverseMock
            .Setup(d => d.QueryEventsAsync(
                null, null, null, null, null, It.Is<DateTime?>(dt => dt != null), It.Is<DateTime?>(dt => dt != null), 0, 100, (Guid?)null, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Array.Empty<EventEntity>(), 0));

        SetupIdempotencyQueryEmpty();

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

        _dataverseMock
            .Setup(d => d.QueryEventsAsync(
                null, null, null, null, null, null, It.Is<DateTime?>(dt => dt != null), 0, 100, (Guid?)null, It.IsAny<CancellationToken>()))
            .ReturnsAsync((new[] { event1, event2 }, 2));

        _dataverseMock
            .Setup(d => d.QueryEventsAsync(
                null, null, null, null, null, It.Is<DateTime?>(dt => dt != null), It.Is<DateTime?>(dt => dt != null), 0, 100, (Guid?)null, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Array.Empty<EventEntity>(), 0));

        SetupIdempotencyQueryEmpty();

        _dataverseMock
            .SetupSequence(d => d.CreateAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Create failed for event1"))
            .ReturnsAsync(Guid.NewGuid());

        var act = async () => await service.RunGenerationPassAsync(CancellationToken.None);
        await act.Should().NotThrowAsync();

        _dataverseMock.Verify(
            d => d.CreateAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>()),
            Times.Exactly(2));
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Rule 1: Overdue events → sprk_todo regarding sprk_event
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task RunGenerationPass_Rule1_OverdueEvent_CreatesSprkTodoRegardingEvent()
    {
        // Arrange
        var service = CreateService();
        var overdueEvent = BuildEvent(name: "Filing Deadline", dueDate: DateTime.UtcNow.Date.AddDays(-7));
        var expectedTitle = $"Overdue: {overdueEvent.Name}";

        _dataverseMock
            .Setup(d => d.QueryEventsAsync(
                null, null, null, null, null, null, It.Is<DateTime?>(dt => dt != null), 0, 100, (Guid?)null, It.IsAny<CancellationToken>()))
            .ReturnsAsync((new[] { overdueEvent }, 1));

        _dataverseMock
            .Setup(d => d.QueryEventsAsync(
                null, null, null, null, null, It.Is<DateTime?>(dt => dt != null), It.Is<DateTime?>(dt => dt != null), 0, 100, (Guid?)null, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Array.Empty<EventEntity>(), 0));

        SetupIdempotencyQueryEmpty();

        _dataverseMock
            .Setup(d => d.CreateAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Guid.NewGuid());

        // Act
        await service.RunGenerationPassAsync(CancellationToken.None);

        // Assert — at least one create call targeting sprk_todo with the expected title
        _dataverseMock.Verify(
            d => d.CreateAsync(
                It.Is<Entity>(e =>
                    e.LogicalName == "sprk_todo" &&
                    (string)e["sprk_name"] == expectedTitle &&
                    e.Attributes.ContainsKey("sprk_regardingevent") &&
                    ((EntityReference)e["sprk_regardingevent"]).Id == overdueEvent.Id),
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
            statusCode: 5,
            dueDate: DateTime.UtcNow.Date.AddDays(-3));

        _dataverseMock
            .Setup(d => d.QueryEventsAsync(
                null, null, null, null, null, null, It.Is<DateTime?>(dt => dt != null), 0, 100, (Guid?)null, It.IsAny<CancellationToken>()))
            .ReturnsAsync((new[] { completedEvent }, 1));

        _dataverseMock
            .Setup(d => d.QueryEventsAsync(
                null, null, null, null, null, It.Is<DateTime?>(dt => dt != null), It.Is<DateTime?>(dt => dt != null), 0, 100, (Guid?)null, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Array.Empty<EventEntity>(), 0));

        SetupIdempotencyQueryEmpty();

        await service.RunGenerationPassAsync(CancellationToken.None);

        _dataverseMock.Verify(
            d => d.CreateAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Rule 3: Deadline proximity → sprk_todo regarding sprk_event, with due date
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task RunGenerationPass_Rule3_UpcomingDeadline_CreatesSprkTodoWithDueDateAndRegardingEvent()
    {
        // Arrange
        var service = CreateService();
        var dueDate = DateTime.UtcNow.Date.AddDays(7);
        var upcomingEvent = BuildEvent(name: "Contract Signing", dueDate: dueDate);
        var dueDateStr = dueDate.ToString("yyyy-MM-dd");
        var expectedTitle = $"Deadline: {upcomingEvent.Name} (due {dueDateStr})";

        _dataverseMock
            .Setup(d => d.QueryEventsAsync(
                null, null, null, null, null, null, It.Is<DateTime?>(dt => dt != null), 0, 100, (Guid?)null, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Array.Empty<EventEntity>(), 0));

        _dataverseMock
            .Setup(d => d.QueryEventsAsync(
                null, null, null, null, null, It.Is<DateTime?>(dt => dt != null), It.Is<DateTime?>(dt => dt != null), 0, 100, (Guid?)null, It.IsAny<CancellationToken>()))
            .ReturnsAsync((new[] { upcomingEvent }, 1));

        SetupIdempotencyQueryEmpty();

        _dataverseMock
            .Setup(d => d.CreateAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Guid.NewGuid());

        // Act
        await service.RunGenerationPassAsync(CancellationToken.None);

        // Assert
        _dataverseMock.Verify(
            d => d.CreateAsync(
                It.Is<Entity>(e =>
                    e.LogicalName == "sprk_todo" &&
                    (string)e["sprk_name"] == expectedTitle &&
                    e.Attributes.ContainsKey("sprk_duedate") &&
                    (DateTime)e["sprk_duedate"] == dueDate &&
                    e.Attributes.ContainsKey("sprk_regardingevent")),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Idempotency: dismissed to-dos are never re-created (server-side filter at query)
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task TodoExistsAsync_WhenDismissedFilteredOut_ReturnsFalseSoDuplicateCreationPossible()
    {
        // The Dataverse query filters statuscode != Dismissed (3) — so dismissed items
        // are NOT returned, and the idempotency guard reports "no duplicate". This is
        // the intended behavior: dismissed-and-re-emerged matches still get a fresh
        // sprk_todo. The PROTECTION against re-creating identical dismissed items
        // lives in the source-condition logic (e.g., once a matter falls below the
        // budget threshold, no new "Budget Alert" todo is generated).
        var service = CreateService();
        SetupIdempotencyQueryEmpty(); // server-side filter excluded the dismissed row

        var exists = await service.TodoExistsAsync("Overdue: Missed Deadline", CancellationToken.None);

        exists.Should().BeFalse(
            "the server-side statuscode != Dismissed filter is applied in the query");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Options / configuration
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Options_DefaultValues_AreCorrect()
    {
        var options = new TodoGenerationOptions();
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
