using System.Security.Claims;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Ai;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai;

/// <summary>
/// Unit tests for AiAuthorizationService.
/// Tests FullUAC authorization flow via IAccessDataSource.
/// </summary>
public class AiAuthorizationServiceTests
{
    private readonly Mock<IAccessDataSource> _accessDataSourceMock;
    private readonly Mock<ILogger<AiAuthorizationService>> _loggerMock;
    private readonly AiAuthorizationService _service;

    public AiAuthorizationServiceTests()
    {
        _accessDataSourceMock = new Mock<IAccessDataSource>();
        _loggerMock = new Mock<ILogger<AiAuthorizationService>>();
        _service = new AiAuthorizationService(_accessDataSourceMock.Object, _loggerMock.Object);
    }

    #region Test Helpers

    private static ClaimsPrincipal CreateUser(string userId = "test-user-123")
    {
        var claims = new List<Claim>
        {
            new("oid", userId)
        };
        var identity = new ClaimsIdentity(claims, "TestAuth");
        return new ClaimsPrincipal(identity);
    }

    private static ClaimsPrincipal CreateUserWithNameIdentifier(string userId = "test-user-123")
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, userId)
        };
        var identity = new ClaimsIdentity(claims, "TestAuth");
        return new ClaimsPrincipal(identity);
    }

    private static ClaimsPrincipal CreateAnonymousUser()
    {
        return new ClaimsPrincipal(new ClaimsIdentity());
    }

    private static Microsoft.AspNetCore.Http.HttpContext CreateMockHttpContext()
    {
        var context = new Microsoft.AspNetCore.Http.DefaultHttpContext();
        context.Request.Headers["Authorization"] = "Bearer mock-token";
        return context;
    }

    private static AccessSnapshot CreateAccessSnapshot(
        string userId,
        string resourceId,
        AccessRights accessRights)
    {
        return new AccessSnapshot
        {
            UserId = userId,
            ResourceId = resourceId,
            AccessRights = accessRights,
            CachedAt = DateTimeOffset.UtcNow
        };
    }

    #endregion

    #region Constructor Tests

    [Fact]
    public void Constructor_NullAccessDataSource_ThrowsArgumentNullException()
    {
        // Act & Assert
        var act = () => new AiAuthorizationService(null!, _loggerMock.Object);
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("accessDataSource");
    }

    [Fact]
    public void Constructor_NullLogger_ThrowsArgumentNullException()
    {
        // Act & Assert
        var act = () => new AiAuthorizationService(_accessDataSourceMock.Object, null!);
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("logger");
    }

    #endregion

    #region Authorization Success Tests

    [Fact]
    public async Task AuthorizeAsync_SingleDocumentWithReadAccess_ReturnsAuthorized()
    {
        // Arrange
        var userId = "user-123";
        var documentId = Guid.NewGuid();
        var user = CreateUser(userId);

        _accessDataSourceMock
            .Setup(x => x.GetUserAccessAsync(userId, documentId.ToString(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateAccessSnapshot(userId, documentId.ToString(), AccessRights.Read));

        // Act
        var result = await _service.AuthorizeAsync(user, new[] { documentId }, CreateMockHttpContext());

        // Assert
        result.Success.Should().BeTrue();
        result.Reason.Should().BeNull();
        result.AuthorizedDocumentIds.Should().ContainSingle().Which.Should().Be(documentId);
    }

    [Fact]
    public async Task AuthorizeAsync_MultipleDocumentsWithReadAccess_ReturnsAllAuthorized()
    {
        // Arrange
        var userId = "user-123";
        var docId1 = Guid.NewGuid();
        var docId2 = Guid.NewGuid();
        var docId3 = Guid.NewGuid();
        var user = CreateUser(userId);

        _accessDataSourceMock
            .Setup(x => x.GetUserAccessAsync(userId, It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string uid, string rid, CancellationToken _) =>
                CreateAccessSnapshot(uid, rid, AccessRights.Read));

        // Act
        var result = await _service.AuthorizeAsync(user, new[] { docId1, docId2, docId3 }, CreateMockHttpContext());

        // Assert
        result.Success.Should().BeTrue();
        result.AuthorizedDocumentIds.Should().HaveCount(3);
        result.AuthorizedDocumentIds.Should().Contain(new[] { docId1, docId2, docId3 });
    }

    [Fact]
    public async Task AuthorizeAsync_DocumentWithReadWriteAccess_ReturnsAuthorized()
    {
        // Arrange
        var userId = "user-123";
        var documentId = Guid.NewGuid();
        var user = CreateUser(userId);

        _accessDataSourceMock
            .Setup(x => x.GetUserAccessAsync(userId, documentId.ToString(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateAccessSnapshot(userId, documentId.ToString(), AccessRights.Read | AccessRights.Write));

        // Act
        var result = await _service.AuthorizeAsync(user, new[] { documentId }, CreateMockHttpContext());

        // Assert
        result.Success.Should().BeTrue();
    }

    [Fact]
    public async Task AuthorizeAsync_UserWithNameIdentifierClaim_ReturnsAuthorized()
    {
        // Arrange
        var userId = "user-123";
        var documentId = Guid.NewGuid();
        var user = CreateUserWithNameIdentifier(userId);

        _accessDataSourceMock
            .Setup(x => x.GetUserAccessAsync(userId, documentId.ToString(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateAccessSnapshot(userId, documentId.ToString(), AccessRights.Read));

        // Act
        var result = await _service.AuthorizeAsync(user, new[] { documentId }, CreateMockHttpContext());

        // Assert
        result.Success.Should().BeTrue();
    }

    #endregion

    #region Authorization Denied Tests

    [Fact]
    public async Task AuthorizeAsync_NoUserIdentity_ReturnsDenied()
    {
        // Arrange
        var documentId = Guid.NewGuid();
        var user = CreateAnonymousUser();

        // Act
        var result = await _service.AuthorizeAsync(user, new[] { documentId }, CreateMockHttpContext());

        // Assert
        result.Success.Should().BeFalse();
        result.Reason.Should().Contain("identity not found");
        result.AuthorizedDocumentIds.Should().BeEmpty();
    }

    [Fact]
    public async Task AuthorizeAsync_SingleDocumentNoReadAccess_ReturnsDenied()
    {
        // Arrange
        var userId = "user-123";
        var documentId = Guid.NewGuid();
        var user = CreateUser(userId);

        _accessDataSourceMock
            .Setup(x => x.GetUserAccessAsync(userId, documentId.ToString(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateAccessSnapshot(userId, documentId.ToString(), AccessRights.None));

        // Act
        var result = await _service.AuthorizeAsync(user, new[] { documentId }, CreateMockHttpContext());

        // Assert
        result.Success.Should().BeFalse();
        result.Reason.Should().NotBeNullOrEmpty();
        result.AuthorizedDocumentIds.Should().BeEmpty();
    }

    [Fact]
    public async Task AuthorizeAsync_WriteOnlyAccess_ReturnsDenied()
    {
        // Arrange - Write without Read should be denied (AI requires Read)
        var userId = "user-123";
        var documentId = Guid.NewGuid();
        var user = CreateUser(userId);

        _accessDataSourceMock
            .Setup(x => x.GetUserAccessAsync(userId, documentId.ToString(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateAccessSnapshot(userId, documentId.ToString(), AccessRights.Write));

        // Act
        var result = await _service.AuthorizeAsync(user, new[] { documentId }, CreateMockHttpContext());

        // Assert
        result.Success.Should().BeFalse();
        result.Reason.Should().Contain("Read access required");
    }

    [Fact]
    public async Task AuthorizeAsync_AllDocumentsDenied_ReturnsDenied()
    {
        // Arrange
        var userId = "user-123";
        var docId1 = Guid.NewGuid();
        var docId2 = Guid.NewGuid();
        var user = CreateUser(userId);

        _accessDataSourceMock
            .Setup(x => x.GetUserAccessAsync(userId, It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string uid, string rid, CancellationToken _) =>
                CreateAccessSnapshot(uid, rid, AccessRights.None));

        // Act
        var result = await _service.AuthorizeAsync(user, new[] { docId1, docId2 }, CreateMockHttpContext());

        // Assert
        result.Success.Should().BeFalse();
        result.AuthorizedDocumentIds.Should().BeEmpty();
    }

    #endregion

    #region Partial Authorization Tests

    [Fact]
    public async Task AuthorizeAsync_PartialAccess_ReturnsPartialWithAuthorizedIds()
    {
        // Arrange
        var userId = "user-123";
        var authorizedDocId = Guid.NewGuid();
        var deniedDocId = Guid.NewGuid();
        var user = CreateUser(userId);

        _accessDataSourceMock
            .Setup(x => x.GetUserAccessAsync(userId, authorizedDocId.ToString(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateAccessSnapshot(userId, authorizedDocId.ToString(), AccessRights.Read));

        _accessDataSourceMock
            .Setup(x => x.GetUserAccessAsync(userId, deniedDocId.ToString(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateAccessSnapshot(userId, deniedDocId.ToString(), AccessRights.None));

        // Act
        var result = await _service.AuthorizeAsync(user, new[] { authorizedDocId, deniedDocId }, CreateMockHttpContext());

        // Assert
        result.Success.Should().BeFalse();
        result.Reason.Should().Contain("denied to");
        result.AuthorizedDocumentIds.Should().ContainSingle().Which.Should().Be(authorizedDocId);
    }

    [Fact]
    public async Task AuthorizeAsync_PartialAccess_MixedPermissions_ReturnsCorrectSubset()
    {
        // Arrange
        var userId = "user-123";
        var docId1 = Guid.NewGuid(); // Read access
        var docId2 = Guid.NewGuid(); // No access
        var docId3 = Guid.NewGuid(); // Read+Write access
        var docId4 = Guid.NewGuid(); // Write-only (no read)
        var user = CreateUser(userId);

        _accessDataSourceMock
            .Setup(x => x.GetUserAccessAsync(userId, docId1.ToString(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateAccessSnapshot(userId, docId1.ToString(), AccessRights.Read));
        _accessDataSourceMock
            .Setup(x => x.GetUserAccessAsync(userId, docId2.ToString(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateAccessSnapshot(userId, docId2.ToString(), AccessRights.None));
        _accessDataSourceMock
            .Setup(x => x.GetUserAccessAsync(userId, docId3.ToString(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateAccessSnapshot(userId, docId3.ToString(), AccessRights.Read | AccessRights.Write));
        _accessDataSourceMock
            .Setup(x => x.GetUserAccessAsync(userId, docId4.ToString(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateAccessSnapshot(userId, docId4.ToString(), AccessRights.Write));

        // Act
        var result = await _service.AuthorizeAsync(user, new[] { docId1, docId2, docId3, docId4 }, CreateMockHttpContext());

        // Assert
        result.Success.Should().BeFalse();
        result.AuthorizedDocumentIds.Should().HaveCount(2);
        result.AuthorizedDocumentIds.Should().Contain(docId1);
        result.AuthorizedDocumentIds.Should().Contain(docId3);
        result.AuthorizedDocumentIds.Should().NotContain(docId2);
        result.AuthorizedDocumentIds.Should().NotContain(docId4);
    }

    #endregion

    #region Error Handling Tests (Fail-Closed)

    [Fact]
    public async Task AuthorizeAsync_AccessDataSourceThrows_ReturnsDenied()
    {
        // Arrange - Fail-closed: errors result in denial
        var userId = "user-123";
        var documentId = Guid.NewGuid();
        var user = CreateUser(userId);

        _accessDataSourceMock
            .Setup(x => x.GetUserAccessAsync(userId, documentId.ToString(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Database connection failed"));

        // Act
        var result = await _service.AuthorizeAsync(user, new[] { documentId }, CreateMockHttpContext());

        // Assert - Fail-closed security: errors result in denial
        result.Success.Should().BeFalse();
        result.Reason.Should().Contain("failed");
    }

    [Fact]
    public async Task AuthorizeAsync_PartialExceptionDuringCheck_DeniesFailedDocument()
    {
        // Arrange
        var userId = "user-123";
        var successDocId = Guid.NewGuid();
        var failDocId = Guid.NewGuid();
        var user = CreateUser(userId);

        _accessDataSourceMock
            .Setup(x => x.GetUserAccessAsync(userId, successDocId.ToString(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateAccessSnapshot(userId, successDocId.ToString(), AccessRights.Read));

        _accessDataSourceMock
            .Setup(x => x.GetUserAccessAsync(userId, failDocId.ToString(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Document not found"));

        // Act
        var result = await _service.AuthorizeAsync(user, new[] { successDocId, failDocId }, CreateMockHttpContext());

        // Assert - Failed document should be denied, successful one should be authorized
        result.Success.Should().BeFalse();
        result.AuthorizedDocumentIds.Should().ContainSingle().Which.Should().Be(successDocId);
    }

    #endregion

    #region Input Validation Tests

    [Fact]
    public async Task AuthorizeAsync_NullUser_ThrowsArgumentNullException()
    {
        // Arrange
        var documentId = Guid.NewGuid();

        // Act & Assert
        var act = () => _service.AuthorizeAsync(null!, new[] { documentId }, CreateMockHttpContext());
        await act.Should().ThrowAsync<ArgumentNullException>()
            .WithParameterName("user");
    }

    [Fact]
    public async Task AuthorizeAsync_NullDocumentIds_ThrowsArgumentNullException()
    {
        // Arrange
        var user = CreateUser();

        // Act & Assert
        var act = () => _service.AuthorizeAsync(user, null!, CreateMockHttpContext());
        await act.Should().ThrowAsync<ArgumentNullException>()
            .WithParameterName("documentIds");
    }

    [Fact]
    public async Task AuthorizeAsync_EmptyDocumentIds_ThrowsArgumentException()
    {
        // Arrange
        var user = CreateUser();

        // Act & Assert
        var act = () => _service.AuthorizeAsync(user, Array.Empty<Guid>(), CreateMockHttpContext());
        await act.Should().ThrowAsync<ArgumentException>()
            .WithParameterName("documentIds");
    }

    #endregion

    #region Cancellation Tests

    [Fact]
    public async Task AuthorizeAsync_DataSourceThrowsException_ReturnsDenied()
    {
        // Arrange - fail-closed security: any exception results in denied access
        var userId = "user-123";
        var documentId = Guid.NewGuid();
        var user = CreateUser(userId);

        _accessDataSourceMock
            .Setup(x => x.GetUserAccessAsync(userId, documentId.ToString(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());

        // Act
        var result = await _service.AuthorizeAsync(user, new[] { documentId }, CreateMockHttpContext());

        // Assert - fail-closed: exception causes denied result, not propagation
        result.Success.Should().BeFalse();
        result.Reason.Should().Contain("Authorization check failed");
    }

    #endregion

    #region Claim Extraction Tests

    [Fact]
    public async Task AuthorizeAsync_UserWithOidClaim_UsesOidForAuthorization()
    {
        // Arrange
        var userId = "oid-claim-user";
        var documentId = Guid.NewGuid();
        var claims = new List<Claim>
        {
            new("oid", userId),
            new(ClaimTypes.NameIdentifier, "different-id") // Should prefer 'oid'
        };
        var user = new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth"));

        _accessDataSourceMock
            .Setup(x => x.GetUserAccessAsync(userId, documentId.ToString(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateAccessSnapshot(userId, documentId.ToString(), AccessRights.Read));

        // Act
        var result = await _service.AuthorizeAsync(user, new[] { documentId }, CreateMockHttpContext());

        // Assert
        result.Success.Should().BeTrue();
        _accessDataSourceMock.Verify(
            x => x.GetUserAccessAsync(userId, documentId.ToString(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task AuthorizeAsync_UserWithObjectIdentifierClaim_UsesObjectIdentifier()
    {
        // Arrange
        var userId = "objectidentifier-user";
        var documentId = Guid.NewGuid();
        var claims = new List<Claim>
        {
            new("http://schemas.microsoft.com/identity/claims/objectidentifier", userId)
        };
        var user = new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth"));

        _accessDataSourceMock
            .Setup(x => x.GetUserAccessAsync(userId, documentId.ToString(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateAccessSnapshot(userId, documentId.ToString(), AccessRights.Read));

        // Act
        var result = await _service.AuthorizeAsync(user, new[] { documentId }, CreateMockHttpContext());

        // Assert
        result.Success.Should().BeTrue();
    }

    #endregion
}
