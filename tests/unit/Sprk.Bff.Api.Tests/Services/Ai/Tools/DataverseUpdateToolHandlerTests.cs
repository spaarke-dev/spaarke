using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Tools;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai.Tools;

public class DataverseUpdateToolHandlerTests
{
    private readonly IDataverseService _dataverseService;
    private readonly ILogger<DataverseUpdateToolHandler> _logger;
    private readonly DataverseUpdateToolHandler _handler;

    public DataverseUpdateToolHandlerTests()
    {
        _dataverseService = Substitute.For<IDataverseService>();
        _logger = Substitute.For<ILogger<DataverseUpdateToolHandler>>();
        _handler = new DataverseUpdateToolHandler(_dataverseService, _logger);
    }

    [Fact]
    public void ToolName_ShouldReturnDataverseUpdate()
    {
        // Act
        var toolName = _handler.ToolName;

        // Assert
        toolName.Should().Be("DataverseUpdate");
    }

    [Fact]
    public async Task ExecuteAsync_ValidParameters_UpdatesRecord()
    {
        // Arrange
        var recordId = Guid.NewGuid();
        var fields = new Dictionary<string, object>
        {
            ["sprk_name"] = "Updated Name",
            ["sprk_status"] = 1
        };

        var parameters = new ToolParameters(new Dictionary<string, object>
        {
            ["entity"] = "sprk_invoice",
            ["recordId"] = recordId,
            ["fields"] = fields
        });

        _dataverseService.UpdateRecordAsync(
            "sprk_invoice",
            recordId,
            Arg.Any<Dictionary<string, object>>(),
            Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        // Act
        var result = await _handler.ExecuteAsync(parameters, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        result.Data.Should().NotBeNull();

        await _dataverseService.Received(1).UpdateRecordAsync(
            "sprk_invoice",
            recordId,
            Arg.Is<Dictionary<string, object>>(d =>
                d["sprk_name"].ToString() == "Updated Name" &&
                (int)d["sprk_status"] == 1),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_MoneyFields_ConvertsToMoneyType()
    {
        // Arrange
        var recordId = Guid.NewGuid();
        var fields = new Dictionary<string, object>
        {
            ["sprk_totalamount"] = 15000.50m,
            ["sprk_budget"] = 100000m
        };

        var parameters = new ToolParameters(new Dictionary<string, object>
        {
            ["entity"] = "sprk_invoice",
            ["recordId"] = recordId,
            ["fields"] = fields
        });

        _dataverseService.UpdateRecordAsync(Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<Dictionary<string, object>>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        // Act
        var result = await _handler.ExecuteAsync(parameters, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();

        await _dataverseService.Received(1).UpdateRecordAsync(
            Arg.Any<string>(),
            Arg.Any<Guid>(),
            Arg.Is<Dictionary<string, object>>(d =>
                d["sprk_totalamount"] is Money amount && amount.Value == 15000.50m &&
                d["sprk_budget"] is Money budget && budget.Value == 100000m),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_EntityReferenceField_ConvertsToEntityReference()
    {
        // Arrange
        var recordId = Guid.NewGuid();
        var matterId = Guid.NewGuid();
        var fields = new Dictionary<string, object>
        {
            ["sprk_matter"] = new Dictionary<string, object>
            {
                ["logicalName"] = "sprk_matter",
                ["id"] = matterId.ToString()
            }
        };

        var parameters = new ToolParameters(new Dictionary<string, object>
        {
            ["entity"] = "sprk_invoice",
            ["recordId"] = recordId,
            ["fields"] = fields
        });

        _dataverseService.UpdateRecordAsync(Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<Dictionary<string, object>>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        // Act
        var result = await _handler.ExecuteAsync(parameters, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();

        await _dataverseService.Received(1).UpdateRecordAsync(
            Arg.Any<string>(),
            Arg.Any<Guid>(),
            Arg.Is<Dictionary<string, object>>(d =>
                d["sprk_matter"] is EntityReference entityRef &&
                entityRef.LogicalName == "sprk_matter" &&
                entityRef.Id == matterId),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_MissingEntityName_ReturnsError()
    {
        // Arrange
        var parameters = new ToolParameters(new Dictionary<string, object>
        {
            ["recordId"] = Guid.NewGuid(),
            ["fields"] = new Dictionary<string, object> { ["sprk_name"] = "Test" }
        });

        // Act
        var result = await _handler.ExecuteAsync(parameters, CancellationToken.None);

        // Assert
        result.Success.Should().BeFalse();
        result.Error.Should().Contain("Entity name is required");
    }

    [Fact]
    public async Task ExecuteAsync_EmptyRecordId_ReturnsError()
    {
        // Arrange
        var parameters = new ToolParameters(new Dictionary<string, object>
        {
            ["entity"] = "sprk_invoice",
            ["recordId"] = Guid.Empty,
            ["fields"] = new Dictionary<string, object> { ["sprk_name"] = "Test" }
        });

        // Act
        var result = await _handler.ExecuteAsync(parameters, CancellationToken.None);

        // Assert
        result.Success.Should().BeFalse();
        result.Error.Should().Contain("RecordId cannot be empty");
    }

    [Fact]
    public async Task ExecuteAsync_EmptyFieldsDictionary_ReturnsError()
    {
        // Arrange
        var parameters = new ToolParameters(new Dictionary<string, object>
        {
            ["entity"] = "sprk_invoice",
            ["recordId"] = Guid.NewGuid(),
            ["fields"] = new Dictionary<string, object>()
        });

        // Act
        var result = await _handler.ExecuteAsync(parameters, CancellationToken.None);

        // Assert
        result.Success.Should().BeFalse();
        result.Error.Should().Contain("must contain at least one field");
    }

    [Fact]
    public async Task ExecuteAsync_NullFieldValue_PassesThroughAsNull()
    {
        // Arrange
        var recordId = Guid.NewGuid();
        var fields = new Dictionary<string, object>
        {
            ["sprk_name"] = "Test",
            ["sprk_description"] = null!
        };

        var parameters = new ToolParameters(new Dictionary<string, object>
        {
            ["entity"] = "sprk_invoice",
            ["recordId"] = recordId,
            ["fields"] = fields
        });

        _dataverseService.UpdateRecordAsync(Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<Dictionary<string, object>>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        // Act
        var result = await _handler.ExecuteAsync(parameters, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();

        await _dataverseService.Received(1).UpdateRecordAsync(
            Arg.Any<string>(),
            Arg.Any<Guid>(),
            Arg.Is<Dictionary<string, object>>(d =>
                d["sprk_name"].ToString() == "Test" &&
                d["sprk_description"] == null),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_IntegerAsMoneyField_ConvertsToMoney()
    {
        // Arrange
        var recordId = Guid.NewGuid();
        var fields = new Dictionary<string, object>
        {
            ["sprk_totalamount"] = 15000 // int, not decimal
        };

        var parameters = new ToolParameters(new Dictionary<string, object>
        {
            ["entity"] = "sprk_invoice",
            ["recordId"] = recordId,
            ["fields"] = fields
        });

        _dataverseService.UpdateRecordAsync(Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<Dictionary<string, object>>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        // Act
        var result = await _handler.ExecuteAsync(parameters, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();

        await _dataverseService.Received(1).UpdateRecordAsync(
            Arg.Any<string>(),
            Arg.Any<Guid>(),
            Arg.Is<Dictionary<string, object>>(d =>
                d["sprk_totalamount"] is Money money && money.Value == 15000m),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_DataverseException_ReturnsError()
    {
        // Arrange
        var recordId = Guid.NewGuid();
        var fields = new Dictionary<string, object>
        {
            ["sprk_name"] = "Test"
        };

        var parameters = new ToolParameters(new Dictionary<string, object>
        {
            ["entity"] = "sprk_invoice",
            ["recordId"] = recordId,
            ["fields"] = fields
        });

        _dataverseService.UpdateRecordAsync(Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<Dictionary<string, object>>(), Arg.Any<CancellationToken>())
            .Throws(new DataverseException("Update failed", 500));

        // Act
        var result = await _handler.ExecuteAsync(parameters, CancellationToken.None);

        // Assert
        result.Success.Should().BeFalse();
        result.Error.Should().Contain("Update failed");
    }
}
