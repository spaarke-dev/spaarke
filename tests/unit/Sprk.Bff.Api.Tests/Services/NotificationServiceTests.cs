using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Xrm.Sdk;
using Moq;
using Sprk.Bff.Api.Services;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services;

/// <summary>
/// Unit tests for NotificationService.
/// Tests notification creation with correct fields, parameter validation, and error handling.
/// </summary>
public class NotificationServiceTests
{
    private readonly Mock<Spaarke.Dataverse.IGenericEntityService> _entityServiceMock;
    private readonly Mock<ILogger<NotificationService>> _loggerMock;
    private readonly NotificationService _sut;

    public NotificationServiceTests()
    {
        _entityServiceMock = new Mock<Spaarke.Dataverse.IGenericEntityService>();
        _loggerMock = new Mock<ILogger<NotificationService>>();
        _sut = new NotificationService(_entityServiceMock.Object, _loggerMock.Object);
    }

    #region CreateNotificationAsync — Happy Path

    [Fact]
    public async Task CreateNotificationAsync_WithRequiredFields_CreatesAppNotification()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var expectedNotificationId = Guid.NewGuid();
        var title = "New document uploaded";

        _entityServiceMock
            .Setup(s => s.CreateAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedNotificationId);

        // Act
        var result = await _sut.CreateNotificationAsync(userId, title);

        // Assert
        result.Should().Be(expectedNotificationId);
        _entityServiceMock.Verify(
            s => s.CreateAsync(
                It.Is<Entity>(e =>
                    e.LogicalName == "appnotification" &&
                    (string)e["title"] == title &&
                    ((EntityReference)e["ownerid"]).Id == userId &&
                    ((EntityReference)e["ownerid"]).LogicalName == "systemuser"),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task CreateNotificationAsync_WithAllOptionalFields_SetsAllFieldsCorrectly()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var notificationId = Guid.NewGuid();
        var regardingId = Guid.NewGuid();
        var title = "Analysis complete";
        var body = "Your document analysis is ready";
        var category = "analysis";
        var priority = 200000001; // Warning
        var actionUrl = "/main.aspx?pagetype=entityrecord&etn=sprk_document&id=123";
        var aiMetadata = new Dictionary<string, object?> { ["confidence"] = 0.95 };

        Entity? capturedEntity = null;
        _entityServiceMock
            .Setup(s => s.CreateAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>()))
            .Callback<Entity, CancellationToken>((e, _) => capturedEntity = e)
            .ReturnsAsync(notificationId);

        // Act
        var result = await _sut.CreateNotificationAsync(
            userId, title, body, category, priority, actionUrl, regardingId, aiMetadata);

        // Assert
        result.Should().Be(notificationId);
        capturedEntity.Should().NotBeNull();
        capturedEntity!.LogicalName.Should().Be("appnotification");
        capturedEntity["title"].Should().Be(title);
        capturedEntity["body"].Should().Be(body);
        ((EntityReference)capturedEntity["ownerid"]).Id.Should().Be(userId);
        ((OptionSetValue)capturedEntity["priority"]).Value.Should().Be(priority);
        capturedEntity["icontype"].Should().NotBeNull(); // Category maps to icon type
        capturedEntity["data"].Should().NotBeNull(); // Action data JSON
        capturedEntity["ttlindays"].Should().Be(7);
    }

    [Fact]
    public async Task CreateNotificationAsync_WithBody_SetsBodyField()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var title = "Notification";
        var body = "Some body text";

        _entityServiceMock
            .Setup(s => s.CreateAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Guid.NewGuid());

        // Act
        await _sut.CreateNotificationAsync(userId, title, body);

        // Assert
        _entityServiceMock.Verify(
            s => s.CreateAsync(
                It.Is<Entity>(e => (string)e["body"] == body),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task CreateNotificationAsync_WithoutBody_OmitsBodyField()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var title = "Title only notification";

        _entityServiceMock
            .Setup(s => s.CreateAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Guid.NewGuid());

        // Act
        await _sut.CreateNotificationAsync(userId, title);

        // Assert
        _entityServiceMock.Verify(
            s => s.CreateAsync(
                It.Is<Entity>(e => !e.Contains("body")),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    #endregion

    #region CreateNotificationAsync — Category to IconType Mapping

    [Theory]
    [InlineData("documents", 100000001)] // Success
    [InlineData("upload", 100000001)]    // Success
    [InlineData("analysis", 100000000)]  // Info
    [InlineData("ai", 100000000)]        // Info
    [InlineData("email", 100000004)]     // Mention
    [InlineData("tasks", 100000003)]     // Warning
    [InlineData("error", 100000002)]     // Failure
    [InlineData("unknown", 100000000)]   // Info (default)
    public async Task CreateNotificationAsync_WithCategory_MapsToCorrectIconType(
        string category, int expectedIconType)
    {
        // Arrange
        var userId = Guid.NewGuid();

        Entity? capturedEntity = null;
        _entityServiceMock
            .Setup(s => s.CreateAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>()))
            .Callback<Entity, CancellationToken>((e, _) => capturedEntity = e)
            .ReturnsAsync(Guid.NewGuid());

        // Act
        await _sut.CreateNotificationAsync(userId, "Test", category: category);

        // Assert
        capturedEntity.Should().NotBeNull();
        ((OptionSetValue)capturedEntity!["icontype"]).Value.Should().Be(expectedIconType);
    }

    #endregion

    #region CreateNotificationAsync — Parameter Validation

    [Fact]
    public async Task CreateNotificationAsync_WithEmptyUserId_ThrowsArgumentNullException()
    {
        // Arrange & Act
        var act = () => _sut.CreateNotificationAsync(Guid.Empty, "Title");

        // Assert
        await act.Should().ThrowAsync<ArgumentNullException>()
            .WithParameterName("userId");
    }

    [Fact]
    public async Task CreateNotificationAsync_WithNullTitle_ThrowsArgumentNullException()
    {
        // Arrange & Act
        var act = () => _sut.CreateNotificationAsync(Guid.NewGuid(), null!);

        // Assert
        await act.Should().ThrowAsync<ArgumentNullException>()
            .WithParameterName("title");
    }

    [Fact]
    public async Task CreateNotificationAsync_WithEmptyTitle_ThrowsArgumentNullException()
    {
        // Arrange & Act
        var act = () => _sut.CreateNotificationAsync(Guid.NewGuid(), "   ");

        // Assert
        await act.Should().ThrowAsync<ArgumentNullException>()
            .WithParameterName("title");
    }

    #endregion

    #region CreateNotificationAsync — Error Handling

    [Fact]
    public async Task CreateNotificationAsync_WhenDataverseFails_ThrowsInvalidOperationException()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var title = "Test notification";

        _entityServiceMock
            .Setup(s => s.CreateAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Dataverse connection failed"));

        // Act
        var act = () => _sut.CreateNotificationAsync(userId, title);

        // Assert
        var ex = await act.Should().ThrowAsync<InvalidOperationException>();
        ex.WithMessage($"*{userId}*");
        ex.And.InnerException.Should().NotBeNull();
        ex.And.InnerException!.Message.Should().Be("Dataverse connection failed");
    }

    [Fact]
    public async Task CreateNotificationAsync_WhenArgumentNullExceptionThrown_DoesNotWrap()
    {
        // Arrange — ArgumentNullException from parameter validation should propagate directly
        // Act
        var act = () => _sut.CreateNotificationAsync(Guid.Empty, "Title");

        // Assert — should be ArgumentNullException, NOT InvalidOperationException
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    #endregion

    #region CreateNotificationAsync — Action Data

    [Fact]
    public async Task CreateNotificationAsync_WithActionUrl_IncludesUrlInDataJson()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var actionUrl = "/main.aspx?pagetype=entityrecord&etn=sprk_document&id=abc";

        Entity? capturedEntity = null;
        _entityServiceMock
            .Setup(s => s.CreateAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>()))
            .Callback<Entity, CancellationToken>((e, _) => capturedEntity = e)
            .ReturnsAsync(Guid.NewGuid());

        // Act
        await _sut.CreateNotificationAsync(userId, "Title", actionUrl: actionUrl);

        // Assert
        capturedEntity.Should().NotBeNull();
        capturedEntity!.Contains("data").Should().BeTrue();
        var dataJson = (string)capturedEntity["data"];
        dataJson.Should().Contain("actionUrl");
    }

    [Fact]
    public async Task CreateNotificationAsync_WithNoOptionalActionFields_OmitsDataField()
    {
        // Arrange
        var userId = Guid.NewGuid();

        Entity? capturedEntity = null;
        _entityServiceMock
            .Setup(s => s.CreateAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>()))
            .Callback<Entity, CancellationToken>((e, _) => capturedEntity = e)
            .ReturnsAsync(Guid.NewGuid());

        // Act
        await _sut.CreateNotificationAsync(userId, "Simple notification");

        // Assert
        capturedEntity.Should().NotBeNull();
        capturedEntity!.Contains("data").Should().BeFalse();
    }

    #endregion

    #region Constructor Validation

    [Fact]
    public void Constructor_WithNullEntityService_ThrowsArgumentNullException()
    {
        // Act
        var act = () => new NotificationService(null!, _loggerMock.Object);

        // Assert
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("entityService");
    }

    [Fact]
    public void Constructor_WithNullLogger_ThrowsArgumentNullException()
    {
        // Act
        var act = () => new NotificationService(_entityServiceMock.Object, null!);

        // Assert
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("logger");
    }

    #endregion
}
