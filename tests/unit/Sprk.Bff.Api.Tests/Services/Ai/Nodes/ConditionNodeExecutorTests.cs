using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Nodes;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai.Nodes;

/// <summary>
/// Unit tests for ConditionNodeExecutor.
/// Tests condition expression parsing, evaluation, and branching decisions.
/// </summary>
public class ConditionNodeExecutorTests
{
    private readonly Mock<ITemplateEngine> _templateEngineMock;
    private readonly Mock<ILogger<ConditionNodeExecutor>> _loggerMock;
    private readonly ConditionNodeExecutor _executor;

    public ConditionNodeExecutorTests()
    {
        _templateEngineMock = new Mock<ITemplateEngine>();
        _loggerMock = new Mock<ILogger<ConditionNodeExecutor>>();
        _executor = new ConditionNodeExecutor(
            _templateEngineMock.Object,
            _loggerMock.Object);
    }

    #region SupportedActionTypes Tests

    [Fact]
    public void SupportedActionTypes_ContainsCondition()
    {
        // Assert
        _executor.SupportedActionTypes.Should().Contain(ActionType.Condition);
        _executor.SupportedActionTypes.Should().HaveCount(1);
    }

    #endregion

    #region Validate Tests

    [Fact]
    public void Validate_WithValidEqualityCondition_ReturnsSuccess()
    {
        // Arrange
        var config = @"{
            ""condition"": { ""operator"": ""eq"", ""left"": ""{{a.output.value}}"", ""right"": ""high"" },
            ""trueBranch"": ""path1"",
            ""falseBranch"": ""path2""
        }";
        var context = CreateValidContext(config);

        // Act
        var result = _executor.Validate(context);

        // Assert
        result.IsValid.Should().BeTrue();
        result.Errors.Should().BeEmpty();
    }

    [Fact]
    public void Validate_WithValidAndCondition_ReturnsSuccess()
    {
        // Arrange
        var config = @"{
            ""condition"": {
                ""operator"": ""and"",
                ""conditions"": [
                    { ""operator"": ""eq"", ""left"": ""{{a.output.x}}"", ""right"": ""y"" },
                    { ""operator"": ""gt"", ""left"": ""{{a.output.score}}"", ""right"": 0.5 }
                ]
            },
            ""trueBranch"": ""path1""
        }";
        var context = CreateValidContext(config);

        // Act
        var result = _executor.Validate(context);

        // Assert
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_WithValidNotCondition_ReturnsSuccess()
    {
        // Arrange
        var config = @"{
            ""condition"": {
                ""operator"": ""not"",
                ""condition"": { ""operator"": ""eq"", ""left"": ""{{a.success}}"", ""right"": true }
            },
            ""falseBranch"": ""errorPath""
        }";
        var context = CreateValidContext(config);

        // Act
        var result = _executor.Validate(context);

        // Assert
        result.IsValid.Should().BeTrue();
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
    public void Validate_WithMissingCondition_ReturnsFailure()
    {
        // Arrange
        var config = @"{ ""trueBranch"": ""path1"" }";
        var context = CreateValidContext(config);

        // Act
        var result = _executor.Validate(context);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("condition") || e.Contains("Condition"));
    }

    [Fact]
    public void Validate_WithMissingOperator_ReturnsFailure()
    {
        // Arrange
        var config = @"{
            ""condition"": { ""left"": ""{{a.value}}"", ""right"": ""x"" },
            ""trueBranch"": ""path1""
        }";
        var context = CreateValidContext(config);

        // Act
        var result = _executor.Validate(context);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("operator"));
    }

    [Fact]
    public void Validate_WithUnknownOperator_ReturnsFailure()
    {
        // Arrange
        var config = @"{
            ""condition"": { ""operator"": ""unknown"", ""left"": ""{{a.value}}"", ""right"": ""x"" },
            ""trueBranch"": ""path1""
        }";
        var context = CreateValidContext(config);

        // Act
        var result = _executor.Validate(context);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("unknown operator"));
    }

    [Fact]
    public void Validate_WithMissingLeftOperand_ReturnsFailure()
    {
        // Arrange
        var config = @"{
            ""condition"": { ""operator"": ""eq"", ""right"": ""x"" },
            ""trueBranch"": ""path1""
        }";
        var context = CreateValidContext(config);

        // Act
        var result = _executor.Validate(context);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("left"));
    }

    [Fact]
    public void Validate_WithNoBranches_ReturnsFailure()
    {
        // Arrange
        var config = @"{
            ""condition"": { ""operator"": ""eq"", ""left"": ""{{a.value}}"", ""right"": ""x"" }
        }";
        var context = CreateValidContext(config);

        // Act
        var result = _executor.Validate(context);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("branch"));
    }

    [Fact]
    public void Validate_WithAndOperator_RequiresMultipleConditions()
    {
        // Arrange
        var config = @"{
            ""condition"": {
                ""operator"": ""and"",
                ""conditions"": [
                    { ""operator"": ""eq"", ""left"": ""{{a.x}}"", ""right"": ""y"" }
                ]
            },
            ""trueBranch"": ""path1""
        }";
        var context = CreateValidContext(config);

        // Act
        var result = _executor.Validate(context);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("at least 2 conditions"));
    }

    [Fact]
    public void Validate_WithNotOperator_RequiresNestedCondition()
    {
        // Arrange
        var config = @"{
            ""condition"": { ""operator"": ""not"" },
            ""trueBranch"": ""path1""
        }";
        var context = CreateValidContext(config);

        // Act
        var result = _executor.Validate(context);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("nested condition"));
    }

    #endregion

    #region ExecuteAsync - Equality Tests

    [Fact]
    public async Task ExecuteAsync_EqualStrings_ReturnsTrue()
    {
        // Arrange
        var config = @"{
            ""condition"": { ""operator"": ""eq"", ""left"": ""{{a.output.level}}"", ""right"": ""high"" },
            ""trueBranch"": ""highPath"",
            ""falseBranch"": ""lowPath""
        }";
        var context = CreateContextWithPreviousOutput(config, "a", new { level = "high" });
        SetupTemplateEngine("high");

        // Act
        var result = await _executor.ExecuteAsync(context, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        var conditionResult = result.GetData<ConditionResult>();
        conditionResult.Should().NotBeNull();
        conditionResult!.Result.Should().BeTrue();
        conditionResult.SelectedBranch.Should().Be("highPath");
    }

    [Fact]
    public async Task ExecuteAsync_UnequalStrings_ReturnsFalse()
    {
        // Arrange
        var config = @"{
            ""condition"": { ""operator"": ""eq"", ""left"": ""{{a.output.level}}"", ""right"": ""high"" },
            ""trueBranch"": ""highPath"",
            ""falseBranch"": ""lowPath""
        }";
        var context = CreateContextWithPreviousOutput(config, "a", new { level = "low" });
        SetupTemplateEngine("low");

        // Act
        var result = await _executor.ExecuteAsync(context, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        var conditionResult = result.GetData<ConditionResult>();
        conditionResult!.Result.Should().BeFalse();
        conditionResult.SelectedBranch.Should().Be("lowPath");
    }

    [Fact]
    public async Task ExecuteAsync_NotEqual_ReturnsCorrectResult()
    {
        // Arrange
        var config = @"{
            ""condition"": { ""operator"": ""ne"", ""left"": ""{{a.output.status}}"", ""right"": ""error"" },
            ""trueBranch"": ""continuePath""
        }";
        var context = CreateContextWithPreviousOutput(config, "a", new { status = "success" });
        SetupTemplateEngine("success");

        // Act
        var result = await _executor.ExecuteAsync(context, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        var conditionResult = result.GetData<ConditionResult>();
        conditionResult!.Result.Should().BeTrue();
        conditionResult.SelectedBranch.Should().Be("continuePath");
    }

    #endregion

    #region ExecuteAsync - Numeric Comparison Tests

    [Fact]
    public async Task ExecuteAsync_GreaterThan_ReturnsCorrectResult()
    {
        // Arrange
        var config = @"{
            ""condition"": { ""operator"": ""gt"", ""left"": ""{{a.output.score}}"", ""right"": 0.7 },
            ""trueBranch"": ""highScore"",
            ""falseBranch"": ""lowScore""
        }";
        var context = CreateContextWithPreviousOutput(config, "a", new { score = 0.85 });
        SetupTemplateEngine("0.85");

        // Act
        var result = await _executor.ExecuteAsync(context, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        var conditionResult = result.GetData<ConditionResult>();
        conditionResult!.Result.Should().BeTrue();
        conditionResult.SelectedBranch.Should().Be("highScore");
    }

    [Fact]
    public async Task ExecuteAsync_LessThan_ReturnsCorrectResult()
    {
        // Arrange
        var config = @"{
            ""condition"": { ""operator"": ""lt"", ""left"": ""{{a.output.count}}"", ""right"": 10 },
            ""trueBranch"": ""fewItems""
        }";
        var context = CreateContextWithPreviousOutput(config, "a", new { count = 5 });
        SetupTemplateEngine("5");

        // Act
        var result = await _executor.ExecuteAsync(context, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        var conditionResult = result.GetData<ConditionResult>();
        conditionResult!.Result.Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteAsync_GreaterThanOrEqual_ReturnsCorrectResult()
    {
        // Arrange
        var config = @"{
            ""condition"": { ""operator"": ""gte"", ""left"": ""{{a.output.value}}"", ""right"": 100 },
            ""trueBranch"": ""pass""
        }";
        var context = CreateContextWithPreviousOutput(config, "a", new { value = 100 });
        SetupTemplateEngine("100");

        // Act
        var result = await _executor.ExecuteAsync(context, CancellationToken.None);

        // Assert
        var conditionResult = result.GetData<ConditionResult>();
        conditionResult!.Result.Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteAsync_LessThanOrEqual_ReturnsCorrectResult()
    {
        // Arrange
        var config = @"{
            ""condition"": { ""operator"": ""lte"", ""left"": ""{{a.output.value}}"", ""right"": 50 },
            ""trueBranch"": ""pass""
        }";
        var context = CreateContextWithPreviousOutput(config, "a", new { value = 50 });
        SetupTemplateEngine("50");

        // Act
        var result = await _executor.ExecuteAsync(context, CancellationToken.None);

        // Assert
        var conditionResult = result.GetData<ConditionResult>();
        conditionResult!.Result.Should().BeTrue();
    }

    #endregion

    #region ExecuteAsync - String Operations Tests

    [Fact]
    public async Task ExecuteAsync_Contains_ReturnsCorrectResult()
    {
        // Arrange
        var config = @"{
            ""condition"": { ""operator"": ""contains"", ""left"": ""{{a.output.text}}"", ""right"": ""error"" },
            ""trueBranch"": ""hasError""
        }";
        var context = CreateContextWithPreviousOutput(config, "a", new { text = "This is an error message" });
        SetupTemplateEngine("This is an error message");

        // Act
        var result = await _executor.ExecuteAsync(context, CancellationToken.None);

        // Assert
        var conditionResult = result.GetData<ConditionResult>();
        conditionResult!.Result.Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteAsync_StartsWith_ReturnsCorrectResult()
    {
        // Arrange
        var config = @"{
            ""condition"": { ""operator"": ""startswith"", ""left"": ""{{a.output.code}}"", ""right"": ""ERR"" },
            ""trueBranch"": ""isError""
        }";
        var context = CreateContextWithPreviousOutput(config, "a", new { code = "ERR_404" });
        SetupTemplateEngine("ERR_404");

        // Act
        var result = await _executor.ExecuteAsync(context, CancellationToken.None);

        // Assert
        var conditionResult = result.GetData<ConditionResult>();
        conditionResult!.Result.Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteAsync_EndsWith_ReturnsCorrectResult()
    {
        // Arrange
        var config = @"{
            ""condition"": { ""operator"": ""endswith"", ""left"": ""{{a.output.filename}}"", ""right"": "".pdf"" },
            ""trueBranch"": ""isPdf""
        }";
        var context = CreateContextWithPreviousOutput(config, "a", new { filename = "document.pdf" });
        SetupTemplateEngine("document.pdf");

        // Act
        var result = await _executor.ExecuteAsync(context, CancellationToken.None);

        // Assert
        var conditionResult = result.GetData<ConditionResult>();
        conditionResult!.Result.Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteAsync_Exists_ReturnsTrueForNonEmptyValue()
    {
        // Arrange
        var config = @"{
            ""condition"": { ""operator"": ""exists"", ""left"": ""{{a.output.value}}"" },
            ""trueBranch"": ""hasValue""
        }";
        var context = CreateContextWithPreviousOutput(config, "a", new { value = "something" });
        SetupTemplateEngine("something");

        // Act
        var result = await _executor.ExecuteAsync(context, CancellationToken.None);

        // Assert
        var conditionResult = result.GetData<ConditionResult>();
        conditionResult!.Result.Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteAsync_Exists_ReturnsFalseForEmptyValue()
    {
        // Arrange
        var config = @"{
            ""condition"": { ""operator"": ""exists"", ""left"": ""{{a.output.value}}"" },
            ""trueBranch"": ""hasValue"",
            ""falseBranch"": ""noValue""
        }";
        var context = CreateContextWithPreviousOutput(config, "a", new { value = "" });
        SetupTemplateEngine("");

        // Act
        var result = await _executor.ExecuteAsync(context, CancellationToken.None);

        // Assert
        var conditionResult = result.GetData<ConditionResult>();
        conditionResult!.Result.Should().BeFalse();
        conditionResult.SelectedBranch.Should().Be("noValue");
    }

    #endregion

    #region ExecuteAsync - Logical Operators Tests

    [Fact]
    public async Task ExecuteAsync_AndOperator_AllTrue_ReturnsTrue()
    {
        // Arrange
        var config = @"{
            ""condition"": {
                ""operator"": ""and"",
                ""conditions"": [
                    { ""operator"": ""eq"", ""left"": ""{{a.output.status}}"", ""right"": ""approved"" },
                    { ""operator"": ""gt"", ""left"": ""{{a.output.score}}"", ""right"": 0.5 }
                ]
            },
            ""trueBranch"": ""proceed""
        }";
        var context = CreateContextWithPreviousOutput(config, "a", new { status = "approved", score = 0.8 });
        SetupTemplateEngineMultiple(new Dictionary<string, string>
        {
            ["{{a.output.status}}"] = "approved",
            ["{{a.output.score}}"] = "0.8"
        });

        // Act
        var result = await _executor.ExecuteAsync(context, CancellationToken.None);

        // Assert
        var conditionResult = result.GetData<ConditionResult>();
        conditionResult!.Result.Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteAsync_AndOperator_OneFalse_ReturnsFalse()
    {
        // Arrange
        var config = @"{
            ""condition"": {
                ""operator"": ""and"",
                ""conditions"": [
                    { ""operator"": ""eq"", ""left"": ""{{a.output.status}}"", ""right"": ""approved"" },
                    { ""operator"": ""gt"", ""left"": ""{{a.output.score}}"", ""right"": 0.9 }
                ]
            },
            ""trueBranch"": ""proceed"",
            ""falseBranch"": ""reject""
        }";
        var context = CreateContextWithPreviousOutput(config, "a", new { status = "approved", score = 0.5 });
        SetupTemplateEngineMultiple(new Dictionary<string, string>
        {
            ["{{a.output.status}}"] = "approved",
            ["{{a.output.score}}"] = "0.5"
        });

        // Act
        var result = await _executor.ExecuteAsync(context, CancellationToken.None);

        // Assert
        var conditionResult = result.GetData<ConditionResult>();
        conditionResult!.Result.Should().BeFalse();
        conditionResult.SelectedBranch.Should().Be("reject");
    }

    [Fact]
    public async Task ExecuteAsync_OrOperator_OneTrue_ReturnsTrue()
    {
        // Arrange
        var config = @"{
            ""condition"": {
                ""operator"": ""or"",
                ""conditions"": [
                    { ""operator"": ""eq"", ""left"": ""{{a.output.type}}"", ""right"": ""urgent"" },
                    { ""operator"": ""eq"", ""left"": ""{{a.output.type}}"", ""right"": ""critical"" }
                ]
            },
            ""trueBranch"": ""prioritize""
        }";
        var context = CreateContextWithPreviousOutput(config, "a", new { type = "critical" });
        SetupTemplateEngine("critical");

        // Act
        var result = await _executor.ExecuteAsync(context, CancellationToken.None);

        // Assert
        var conditionResult = result.GetData<ConditionResult>();
        conditionResult!.Result.Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteAsync_OrOperator_AllFalse_ReturnsFalse()
    {
        // Arrange
        var config = @"{
            ""condition"": {
                ""operator"": ""or"",
                ""conditions"": [
                    { ""operator"": ""eq"", ""left"": ""{{a.output.type}}"", ""right"": ""urgent"" },
                    { ""operator"": ""eq"", ""left"": ""{{a.output.type}}"", ""right"": ""critical"" }
                ]
            },
            ""trueBranch"": ""prioritize"",
            ""falseBranch"": ""normal""
        }";
        var context = CreateContextWithPreviousOutput(config, "a", new { type = "standard" });
        SetupTemplateEngine("standard");

        // Act
        var result = await _executor.ExecuteAsync(context, CancellationToken.None);

        // Assert
        var conditionResult = result.GetData<ConditionResult>();
        conditionResult!.Result.Should().BeFalse();
        conditionResult.SelectedBranch.Should().Be("normal");
    }

    [Fact]
    public async Task ExecuteAsync_NotOperator_InvertsResult()
    {
        // Arrange
        var config = @"{
            ""condition"": {
                ""operator"": ""not"",
                ""condition"": { ""operator"": ""eq"", ""left"": ""{{a.output.status}}"", ""right"": ""failed"" }
            },
            ""trueBranch"": ""proceed""
        }";
        var context = CreateContextWithPreviousOutput(config, "a", new { status = "success" });
        SetupTemplateEngine("success");

        // Act
        var result = await _executor.ExecuteAsync(context, CancellationToken.None);

        // Assert
        var conditionResult = result.GetData<ConditionResult>();
        conditionResult!.Result.Should().BeTrue(); // not(false) = true
    }

    #endregion

    #region ExecuteAsync - Boolean Value Tests

    [Fact]
    public async Task ExecuteAsync_BooleanTrue_MatchesCorrectly()
    {
        // Arrange
        var config = @"{
            ""condition"": { ""operator"": ""eq"", ""left"": ""{{a.success}}"", ""right"": true },
            ""trueBranch"": ""successPath""
        }";
        var context = CreateContextWithSuccess(config, "a", true);
        SetupTemplateEngine("true");

        // Act
        var result = await _executor.ExecuteAsync(context, CancellationToken.None);

        // Assert
        var conditionResult = result.GetData<ConditionResult>();
        conditionResult!.Result.Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteAsync_BooleanFalse_MatchesCorrectly()
    {
        // Arrange
        var config = @"{
            ""condition"": { ""operator"": ""eq"", ""left"": ""{{a.success}}"", ""right"": false },
            ""trueBranch"": ""failurePath""
        }";
        var context = CreateContextWithSuccess(config, "a", false);
        SetupTemplateEngine("false");

        // Act
        var result = await _executor.ExecuteAsync(context, CancellationToken.None);

        // Assert
        var conditionResult = result.GetData<ConditionResult>();
        conditionResult!.Result.Should().BeTrue();
    }

    #endregion

    #region ExecuteAsync - Error Handling Tests

    [Fact]
    public async Task ExecuteAsync_WithInvalidConfig_ReturnsError()
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
    public async Task ExecuteAsync_WithNumericComparisonOnNonNumeric_ReturnsError()
    {
        // Arrange
        var config = @"{
            ""condition"": { ""operator"": ""gt"", ""left"": ""{{a.output.text}}"", ""right"": 10 },
            ""trueBranch"": ""high""
        }";
        var context = CreateContextWithPreviousOutput(config, "a", new { text = "not a number" });
        SetupTemplateEngine("not a number");

        // Act
        var result = await _executor.ExecuteAsync(context, CancellationToken.None);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(NodeErrorCodes.ConditionError);
    }

    #endregion

    #region ExecuteAsync - Output Structure Tests

    [Fact]
    public async Task ExecuteAsync_ReturnsCorrectOutputStructure()
    {
        // Arrange
        var config = @"{
            ""condition"": { ""operator"": ""eq"", ""left"": ""{{a.output.x}}"", ""right"": ""y"" },
            ""trueBranch"": ""truePath"",
            ""falseBranch"": ""falsePath""
        }";
        var context = CreateContextWithPreviousOutput(config, "a", new { x = "y" });
        SetupTemplateEngine("y");

        // Act
        var result = await _executor.ExecuteAsync(context, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        result.NodeId.Should().Be(context.Node.Id);
        result.OutputVariable.Should().Be(context.Node.OutputVariable);
        result.TextContent.Should().Contain("true");
        result.TextContent.Should().Contain("truePath");
        result.Metrics.Should().NotBeNull();

        var conditionResult = result.GetData<ConditionResult>();
        conditionResult.Should().NotBeNull();
        conditionResult!.Result.Should().BeTrue();
        conditionResult.SelectedBranch.Should().Be("truePath");
        conditionResult.TrueBranch.Should().Be("truePath");
        conditionResult.FalseBranch.Should().Be("falsePath");
    }

    [Fact]
    public async Task ExecuteAsync_WithOnlyTrueBranch_SetsNullForFalseBranch()
    {
        // Arrange
        var config = @"{
            ""condition"": { ""operator"": ""eq"", ""left"": ""{{a.output.x}}"", ""right"": ""y"" },
            ""trueBranch"": ""truePath""
        }";
        var context = CreateContextWithPreviousOutput(config, "a", new { x = "z" });
        SetupTemplateEngine("z");

        // Act
        var result = await _executor.ExecuteAsync(context, CancellationToken.None);

        // Assert
        var conditionResult = result.GetData<ConditionResult>();
        conditionResult!.Result.Should().BeFalse();
        conditionResult.SelectedBranch.Should().BeNull(); // No false branch configured
    }

    #endregion

    #region Helper Methods

    private void SetupTemplateEngine(string returnValue)
    {
        _templateEngineMock
            .Setup(t => t.HasVariables(It.IsAny<string>()))
            .Returns(true);

        _templateEngineMock
            .Setup(t => t.Render(It.IsAny<string>(), It.IsAny<Dictionary<string, object?>>()))
            .Returns(returnValue);
    }

    private void SetupTemplateEngineMultiple(Dictionary<string, string> mappings)
    {
        _templateEngineMock
            .Setup(t => t.HasVariables(It.IsAny<string>()))
            .Returns(true);

        _templateEngineMock
            .Setup(t => t.Render(It.IsAny<string>(), It.IsAny<Dictionary<string, object?>>()))
            .Returns((string template, Dictionary<string, object?> _) =>
                mappings.TryGetValue(template, out var value) ? value : template);
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
                Name = "Condition Node",
                ExecutionOrder = 1,
                OutputVariable = "conditionResult",
                ConfigJson = configJson,
                IsActive = true
            },
            Action = new AnalysisAction
            {
                Id = actionId,
                Name = "Condition",
                ActionType = ActionType.Condition
            },
            ActionType = ActionType.Condition,
            Scopes = new ResolvedScopes([], [], []),
            TenantId = "test-tenant"
        };
    }

    private static NodeExecutionContext CreateContextWithPreviousOutput(
        string configJson,
        string outputVariable,
        object outputData)
    {
        var context = CreateValidContext(configJson);
        var previousOutput = NodeOutput.Ok(
            Guid.NewGuid(),
            outputVariable,
            outputData);

        return context with
        {
            PreviousOutputs = new Dictionary<string, NodeOutput>
            {
                [outputVariable] = previousOutput
            }
        };
    }

    private static NodeExecutionContext CreateContextWithSuccess(
        string configJson,
        string outputVariable,
        bool success)
    {
        var context = CreateValidContext(configJson);
        var previousOutput = success
            ? NodeOutput.Ok(Guid.NewGuid(), outputVariable, null)
            : NodeOutput.Error(Guid.NewGuid(), outputVariable, "Failed");

        return context with
        {
            PreviousOutputs = new Dictionary<string, NodeOutput>
            {
                [outputVariable] = previousOutput
            }
        };
    }

    #endregion
}
