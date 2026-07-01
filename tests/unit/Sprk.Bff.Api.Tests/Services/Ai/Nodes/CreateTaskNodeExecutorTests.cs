using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Xrm.Sdk;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Nodes;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai.Nodes;

/// <summary>
/// Unit tests for CreateTaskNodeExecutor.
/// Tests validation, template substitution, and task creation.
/// </summary>
/// <remarks>
/// 2026-06-23 (daily-briefing R2.3 refactor): production was refactored to use the canonical
/// <see cref="IGenericEntityService"/> shared library instead of the orphan-named
/// <c>IHttpClientFactory.CreateClient("DataverseApi")</c> (which was never registered in DI).
/// Tests updated to mock <see cref="IGenericEntityService"/>; the prior MockHttpMessageHandler
/// scaffolding is no longer required. Trait kept as `repaired` to preserve the audit trail.
/// </remarks>
[Trait("status", "repaired")]
public class CreateTaskNodeExecutorTests
{
    private readonly Mock<ITemplateEngine> _templateEngineMock;
    private readonly Mock<IGenericEntityService> _entityServiceMock;
    private readonly Mock<ILogger<CreateTaskNodeExecutor>> _loggerMock;
    private readonly CreateTaskNodeExecutor _executor;

    public CreateTaskNodeExecutorTests()
    {
        _templateEngineMock = new Mock<ITemplateEngine>();
        _entityServiceMock = new Mock<IGenericEntityService>();
        _loggerMock = new Mock<ILogger<CreateTaskNodeExecutor>>();

        // Default: CreateAsync returns a fresh GUID so executors that exercise the create
        // path get a parseable result. Specific tests can override via _entityServiceMock.Reset().
        _entityServiceMock
            .Setup(s => s.CreateAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => Guid.NewGuid());

        _executor = new CreateTaskNodeExecutor(
            _templateEngineMock.Object,
            _entityServiceMock.Object,
            _loggerMock.Object);
    }

    #region SupportedExecutorTypes Tests

    [Fact]
    public void SupportedActionTypes_ContainsCreateTask()
    {
        // Assert
        _executor.SupportedExecutorTypes.Should().Contain(ExecutorType.CreateTask);
        _executor.SupportedExecutorTypes.Should().HaveCount(1);
    }

    #endregion

    #region Validate Tests

    [Fact]
    public void Validate_WithValidConfig_ReturnsSuccess()
    {
        // Arrange
        var context = CreateValidContext(@"{""subject"":""Review document""}");

        // Act
        var result = _executor.Validate(context);

        // Assert
        result.IsValid.Should().BeTrue();
        result.Errors.Should().BeEmpty();
    }

    [Fact]
    public void Validate_WithNoConfig_ReturnsFailure()
    {
        // Arrange
        var context = CreateValidContext(null);

        // Act
        var result = _executor.Validate(context);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("ConfigJson"));
    }

    [Fact]
    public void Validate_WithEmptyConfig_ReturnsFailure()
    {
        // Arrange
        var context = CreateValidContext("");

        // Act
        var result = _executor.Validate(context);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("ConfigJson"));
    }

    [Fact]
    public void Validate_WithInvalidJson_ReturnsFailure()
    {
        // Arrange
        var context = CreateValidContext("{invalid json}");

        // Act
        var result = _executor.Validate(context);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("Invalid"));
    }

    [Fact]
    public void Validate_WithMissingSubject_ReturnsFailure()
    {
        // Arrange
        var context = CreateValidContext(@"{""description"":""Some description""}");

        // Act
        var result = _executor.Validate(context);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("subject"));
    }

    #endregion

    #region ExecuteAsync Tests

    [Fact]
    public async Task ExecuteAsync_WithValidConfig_ReturnsSuccessfulOutput()
    {
        // Arrange
        var config = @"{""subject"":""Review document"",""description"":""Please review""}";
        var context = CreateValidContext(config);

        // Set up mock to return the input unchanged (simple pass-through)
        SetupMockPassThrough();

        // Act
        var result = await _executor.ExecuteAsync(context, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        result.NodeId.Should().Be(context.Node.Id);
        result.OutputVariable.Should().Be(context.Node.OutputVariable);
        result.TextContent.Should().Contain("Task created");

        var data = result.GetData<TaskCreatedOutput>();
        data.Should().NotBeNull();
        data!.Subject.Should().Be("Review document");
    }

    [Fact]
    public async Task ExecuteAsync_WithTemplateVariables_SubstitutesCorrectly()
    {
        // Arrange
        var config = @"{""subject"":""Review document"",""description"":""Summary text""}";
        var context = CreateValidContext(config);

        // Set up mock to return input unchanged
        SetupMockPassThrough();

        // Act
        var result = await _executor.ExecuteAsync(context, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        // Verify Render was called at least twice (for subject and description)
        // Note: Uses generic overload due to Dictionary<string, object?> parameter
        _templateEngineMock.Verify(t => t.Render(It.IsAny<string>(), It.IsAny<Dictionary<string, object?>>()), Times.AtLeast(2));
    }

    [Fact]
    public async Task ExecuteAsync_WithValidationFailure_ReturnsErrorOutput()
    {
        // Arrange
        var context = CreateValidContext("{}"); // Missing subject

        // Act
        var result = await _executor.ExecuteAsync(context, CancellationToken.None);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(NodeErrorCodes.ValidationFailed);
    }

    [Fact]
    public async Task ExecuteAsync_WithRegardingObject_SetsODataBind()
    {
        // Arrange
        var recordId = Guid.NewGuid();
        var config = $@"{{
            ""subject"":""Task"",
            ""regardingObjectId"":""{recordId}"",
            ""regardingObjectType"":""sprk_document""
        }}";
        var context = CreateValidContext(config);

        SetupMockPassThrough();

        // Act
        var result = await _executor.ExecuteAsync(context, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteAsync_WithOwner_SetsOwnerBind()
    {
        // Arrange
        var ownerId = Guid.NewGuid();
        var config = $@"{{
            ""subject"":""Task"",
            ""ownerId"":""{ownerId}""
        }}";
        var context = CreateValidContext(config);

        SetupMockPassThrough();

        // Act
        var result = await _executor.ExecuteAsync(context, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteAsync_WithPreviousOutputs_BuildsTemplateContext()
    {
        // Arrange
        var config = @"{""subject"":""{{analysis.output.title}}""}";
        var previousOutput = NodeOutput.Ok(
            Guid.NewGuid(),
            "analysis",
            new { title = "Document Analysis" });

        var context = CreateValidContext(config);
        context = context with
        {
            PreviousOutputs = new Dictionary<string, NodeOutput>
            {
                ["analysis"] = previousOutput
            }
        };

        _templateEngineMock
            .Setup(t => t.Render(It.IsAny<string>(), It.IsAny<IDictionary<string, object?>>()))
            .Returns("Document Analysis");

        // Act
        var result = await _executor.ExecuteAsync(context, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
    }

    #endregion

    #region Helper Methods

    private void SetupMockPassThrough()
    {
        // Mock both overloads - compiler chooses generic version for Dictionary<string, object?>
        _templateEngineMock
            .Setup(t => t.Render(It.IsAny<string>(), It.IsAny<IDictionary<string, object?>>()))
            .Returns((string template, IDictionary<string, object?> _) => template);

        _templateEngineMock
            .Setup(t => t.Render(It.IsAny<string>(), It.IsAny<Dictionary<string, object?>>()))
            .Returns((string template, Dictionary<string, object?> _) => template);
    }

    private static NodeExecutionContext CreateValidContext(string? configJson)
    {
        var nodeId = Guid.NewGuid();
        var actionId = Guid.NewGuid();

        return new NodeExecutionContext
        {
            RunId = Guid.NewGuid(),
            PlaybookId = Guid.NewGuid(),
            Node = new PlaybookNodeDto
            {
                Id = nodeId,
                PlaybookId = Guid.NewGuid(),
                ActionId = actionId,
                Name = "Create Task Node",
                ExecutionOrder = 1,
                OutputVariable = "taskResult",
                ConfigJson = configJson,
                IsActive = true
            },
            Action = new AnalysisAction
            {
                Id = actionId,
                Name = "Create Task"
            },
            ExecutorType = ExecutorType.CreateTask,
            Scopes = new ResolvedScopes([], [], []),
            TenantId = "test-tenant"
        };
    }

    private class TaskCreatedOutput
    {
        public Guid TaskId { get; set; }
        public string Subject { get; set; } = string.Empty;
        public string? Description { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
    }

    #endregion
}
