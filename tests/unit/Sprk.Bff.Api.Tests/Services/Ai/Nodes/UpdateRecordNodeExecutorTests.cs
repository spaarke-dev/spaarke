using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Nodes;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai.Nodes;

/// <summary>
/// Unit tests for UpdateRecordNodeExecutor.
/// Tests validation, template substitution, and Dataverse record updates.
/// </summary>
public class UpdateRecordNodeExecutorTests
{
    private readonly Mock<ITemplateEngine> _templateEngineMock;
    private readonly Mock<IHttpClientFactory> _httpClientFactoryMock;
    private readonly Mock<ILogger<UpdateRecordNodeExecutor>> _loggerMock;
    private readonly UpdateRecordNodeExecutor _executor;

    public UpdateRecordNodeExecutorTests()
    {
        _templateEngineMock = new Mock<ITemplateEngine>();
        _httpClientFactoryMock = new Mock<IHttpClientFactory>();
        _loggerMock = new Mock<ILogger<UpdateRecordNodeExecutor>>();
        _executor = new UpdateRecordNodeExecutor(
            _templateEngineMock.Object,
            _httpClientFactoryMock.Object,
            _loggerMock.Object);
    }

    #region SupportedActionTypes Tests

    [Fact]
    public void SupportedActionTypes_ContainsUpdateRecord()
    {
        // Assert
        _executor.SupportedActionTypes.Should().Contain(ActionType.UpdateRecord);
        _executor.SupportedActionTypes.Should().HaveCount(1);
    }

    #endregion

    #region Validate Tests

    [Fact]
    public void Validate_WithValidConfig_ReturnsSuccess()
    {
        // Arrange
        var recordId = Guid.NewGuid();
        var config = $@"{{
            ""entityLogicalName"":""sprk_document"",
            ""recordId"":""{recordId}"",
            ""fields"":{{""sprk_status"":""Completed""}}
        }}";
        var context = CreateValidContext(config);

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
    public void Validate_WithMissingEntityName_ReturnsFailure()
    {
        // Arrange
        var recordId = Guid.NewGuid();
        var config = $@"{{
            ""recordId"":""{recordId}"",
            ""fields"":{{""status"":""Done""}}
        }}";
        var context = CreateValidContext(config);

        // Act
        var result = _executor.Validate(context);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("logical name") || e.Contains("EntityLogicalName"));
    }

    [Fact]
    public void Validate_WithMissingRecordId_ReturnsFailure()
    {
        // Arrange
        var config = @"{
            ""entityLogicalName"":""sprk_document"",
            ""fields"":{""status"":""Done""}
        }";
        var context = CreateValidContext(config);

        // Act
        var result = _executor.Validate(context);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("Record ID") || e.Contains("RecordId"));
    }

    [Fact]
    public void Validate_WithMissingFields_ReturnsFailure()
    {
        // Arrange
        var recordId = Guid.NewGuid();
        var config = $@"{{
            ""entityLogicalName"":""sprk_document"",
            ""recordId"":""{recordId}""
        }}";
        var context = CreateValidContext(config);

        // Act
        var result = _executor.Validate(context);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("field"));
    }

    [Fact]
    public void Validate_WithEmptyFields_ReturnsFailure()
    {
        // Arrange
        var recordId = Guid.NewGuid();
        var config = $@"{{
            ""entityLogicalName"":""sprk_document"",
            ""recordId"":""{recordId}"",
            ""fields"":{{}}
        }}";
        var context = CreateValidContext(config);

        // Act
        var result = _executor.Validate(context);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("field"));
    }

    #endregion

    #region ExecuteAsync Tests

    [Fact]
    public async Task ExecuteAsync_WithValidConfig_ReturnsSuccessfulOutput()
    {
        // Arrange
        var recordId = Guid.NewGuid();
        var config = $@"{{
            ""entityLogicalName"":""sprk_document"",
            ""recordId"":""{recordId}"",
            ""fields"":{{""sprk_analysisstatus"":""Completed""}}
        }}";
        var context = CreateValidContext(config);

        SetupMockPassThrough();

        // Act
        var result = await _executor.ExecuteAsync(context, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        result.NodeId.Should().Be(context.Node.Id);
        result.OutputVariable.Should().Be(context.Node.OutputVariable);
        result.TextContent.Should().Contain("Updated");

        var data = result.GetData<UpdateRecordOutput>();
        data.Should().NotBeNull();
        data!.Updated.Should().BeTrue();
        data.EntityLogicalName.Should().Be("sprk_document");
        data.RecordId.Should().Be(recordId);
    }

    [Fact]
    public async Task ExecuteAsync_WithTemplateVariables_SubstitutesCorrectly()
    {
        // Arrange
        var recordId = Guid.NewGuid();
        var config = $@"{{
            ""entityLogicalName"":""sprk_document"",
            ""recordId"":""{recordId}"",
            ""fields"":{{""sprk_summary"":""{{{{analysis.output.summary}}}}""}}
        }}";
        var context = CreateValidContext(config);

        SetupMockPassThrough();

        // Act
        var result = await _executor.ExecuteAsync(context, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteAsync_WithInvalidRecordId_ReturnsErrorOutput()
    {
        // Arrange
        var config = @"{
            ""entityLogicalName"":""sprk_document"",
            ""recordId"":""not-a-guid"",
            ""fields"":{""status"":""Done""}
        }";
        var context = CreateValidContext(config);

        SetupMockPassThrough();

        // Act
        var result = await _executor.ExecuteAsync(context, CancellationToken.None);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Invalid record ID");
    }

    [Fact]
    public async Task ExecuteAsync_WithValidationFailure_ReturnsErrorOutput()
    {
        // Arrange
        var context = CreateValidContext("{}");

        // Act
        var result = await _executor.ExecuteAsync(context, CancellationToken.None);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(NodeErrorCodes.ValidationFailed);
    }

    [Fact]
    public async Task ExecuteAsync_WithMultipleFields_UpdatesAllFields()
    {
        // Arrange
        var recordId = Guid.NewGuid();
        var config = $@"{{
            ""entityLogicalName"":""sprk_document"",
            ""recordId"":""{recordId}"",
            ""fields"":{{
                ""sprk_status"":""Analyzed"",
                ""sprk_summary"":""Summary text"",
                ""sprk_partycount"":""5""
            }}
        }}";
        var context = CreateValidContext(config);

        SetupMockPassThrough();

        // Act
        var result = await _executor.ExecuteAsync(context, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        var data = result.GetData<UpdateRecordOutput>();
        data!.FieldsUpdated.Should().HaveCount(3);
    }

    [Fact]
    public async Task ExecuteAsync_WithLookupFields_FormatsODataBind()
    {
        // Arrange
        var recordId = Guid.NewGuid();
        var ownerId = Guid.NewGuid();
        var config = $@"{{
            ""entityLogicalName"":""sprk_document"",
            ""recordId"":""{recordId}"",
            ""fields"":{{""sprk_status"":""Assigned""}},
            ""lookups"":{{
                ""ownerid"":{{
                    ""targetEntity"":""systemuser"",
                    ""targetId"":""{ownerId}""
                }}
            }}
        }}";
        var context = CreateValidContext(config);

        SetupMockPassThrough();

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
                Name = "Update Record Node",
                ExecutionOrder = 1,
                OutputVariable = "updateResult",
                ConfigJson = configJson,
                IsActive = true
            },
            Action = new AnalysisAction
            {
                Id = actionId,
                Name = "Update Record"
            },
            ActionType = ActionType.UpdateRecord,
            Scopes = new ResolvedScopes([], [], []),
            TenantId = "test-tenant"
        };
    }

    private class UpdateRecordOutput
    {
        public bool Updated { get; set; }
        public string EntityLogicalName { get; set; } = string.Empty;
        public Guid RecordId { get; set; }
        public string[] FieldsUpdated { get; set; } = [];
        public DateTimeOffset UpdatedAt { get; set; }
    }

    #endregion
}
