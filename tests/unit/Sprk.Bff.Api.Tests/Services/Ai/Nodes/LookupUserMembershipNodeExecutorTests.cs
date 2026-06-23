// R3 Part 1 — Tests for LookupUserMembershipNodeExecutor (task 041)
// Covers: validation (entityType, outputVariable, malformed JSON), execute happy path
// (binds IDs to OutputVariable), empty response handling, missing user context, scope
// usage (verifies IServiceScopeFactory.CreateScope() pattern), and resolver-error mapping.
//
// AAA pattern + Moq + FluentAssertions — mirrors CreateNotificationNodeExecutorTests +
// AiAnalysisNodeExecutorTests.

using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Membership;
using Sprk.Bff.Api.Services.Ai.Membership.Models;
using Sprk.Bff.Api.Services.Ai.Nodes;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai.Nodes;

/// <summary>
/// Unit tests for <see cref="LookupUserMembershipNodeExecutor"/>.
/// Validates config parsing, output binding, Singleton+Scoped DI pattern (CreateScope
/// per execution), and resolver-error mapping.
/// </summary>
public class LookupUserMembershipNodeExecutorTests
{
    private readonly Mock<IMembershipResolverService> _resolverMock;
    private readonly Mock<IServiceScopeFactory> _scopeFactoryMock;
    private readonly Mock<IServiceScope> _scopeMock;
    private readonly Mock<ILogger<LookupUserMembershipNodeExecutor>> _loggerMock;
    private readonly LookupUserMembershipNodeExecutor _executor;

    public LookupUserMembershipNodeExecutorTests()
    {
        _resolverMock = new Mock<IMembershipResolverService>();
        _loggerMock = new Mock<ILogger<LookupUserMembershipNodeExecutor>>();

        // Wire the IServiceScopeFactory → IServiceScope → scoped IServiceProvider chain
        // so the executor can resolve IMembershipResolverService via CreateScope().
        var scopedProviderMock = new Mock<IServiceProvider>();
        scopedProviderMock
            .Setup(sp => sp.GetService(typeof(IMembershipResolverService)))
            .Returns(_resolverMock.Object);

        _scopeMock = new Mock<IServiceScope>();
        _scopeMock.Setup(s => s.ServiceProvider).Returns(scopedProviderMock.Object);

        _scopeFactoryMock = new Mock<IServiceScopeFactory>();
        _scopeFactoryMock.Setup(f => f.CreateScope()).Returns(_scopeMock.Object);

        _executor = new LookupUserMembershipNodeExecutor(
            _scopeFactoryMock.Object,
            _loggerMock.Object);
    }

    #region SupportedActionTypes

    [Fact]
    public void SupportedActionTypes_ContainsLookupUserMembership()
    {
        // Assert
        _executor.SupportedActionTypes.Should().Contain(ActionType.LookupUserMembership);
        _executor.SupportedActionTypes.Should().HaveCount(1);
    }

    #endregion

    #region Validate

    [Fact]
    public void Validate_WithValidConfig_ReturnsSuccess()
    {
        // Arrange
        var context = CreateContext(
            configJson: """{"entityType":"sprk_matter"}""",
            outputVariable: "myMemberships");

        // Act
        var result = _executor.Validate(context);

        // Assert
        result.IsValid.Should().BeTrue();
        result.Errors.Should().BeEmpty();
    }

    [Fact]
    public void Validate_WithMissingEntityType_ReturnsError()
    {
        // Arrange
        var context = CreateContext(
            configJson: """{"roles":["owner"]}""",
            outputVariable: "myMemberships");

        // Act
        var result = _executor.Validate(context);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("entityType"));
    }

    [Fact]
    public void Validate_WithBlankEntityType_ReturnsError()
    {
        // Arrange
        var context = CreateContext(
            configJson: """{"entityType":"   "}""",
            outputVariable: "myMemberships");

        // Act
        var result = _executor.Validate(context);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("entityType"));
    }

    [Fact]
    public void Validate_WithMissingOutputVariable_ReturnsError()
    {
        // Arrange
        var context = CreateContext(
            configJson: """{"entityType":"sprk_matter"}""",
            outputVariable: null);

        // Act
        var result = _executor.Validate(context);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("OutputVariable"));
    }

    [Fact]
    public void Validate_WithNoConfigJson_ReturnsError()
    {
        // Arrange
        var context = CreateContext(
            configJson: null,
            outputVariable: "myMemberships");

        // Act
        var result = _executor.Validate(context);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("ConfigJson"));
    }

    [Fact]
    public void Validate_WithMalformedJson_ReturnsError()
    {
        // Arrange
        var context = CreateContext(
            configJson: "{not-json}",
            outputVariable: "myMemberships");

        // Act
        var result = _executor.Validate(context);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("Invalid"));
    }

    #endregion

    #region ExecuteAsync — happy path

    [Fact]
    public async Task ExecuteAsync_HappyPath_BindsIdsToOutputVariable()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var matterId1 = Guid.NewGuid();
        var matterId2 = Guid.NewGuid();
        var matterId3 = Guid.NewGuid();

        var response = BuildResponse(
            entityType: "sprk_matter",
            systemUserId: userId,
            ids: new[] { matterId1, matterId2, matterId3 },
            byRole: new Dictionary<string, IReadOnlyList<Guid>>
            {
                ["owner"] = new[] { matterId1 },
                ["assignedAttorney"] = new[] { matterId2, matterId3 }
            });

        _resolverMock
            .Setup(r => r.ResolveAsync(
                userId,
                "sprk_matter",
                It.IsAny<MembershipResolveOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);

        var context = CreateContext(
            configJson: """{"entityType":"sprk_matter"}""",
            outputVariable: "myMatters",
            userId: userId);

        // Act
        var result = await _executor.ExecuteAsync(context, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        result.OutputVariable.Should().Be("myMatters");
        result.NodeId.Should().Be(context.Node.Id);
        result.TextContent.Should().Contain("3");
        result.TextContent.Should().Contain("sprk_matter");

        // Verify StructuredData binding contract — ids[], count, byRole.
        result.StructuredData.Should().NotBeNull();
        var data = result.StructuredData!.Value;
        data.GetProperty("entityType").GetString().Should().Be("sprk_matter");
        data.GetProperty("count").GetInt32().Should().Be(3);

        var idsArr = data.GetProperty("ids");
        idsArr.GetArrayLength().Should().Be(3);
        var emittedIds = idsArr.EnumerateArray()
            .Select(e => Guid.Parse(e.GetString()!))
            .ToHashSet();
        emittedIds.Should().BeEquivalentTo(new[] { matterId1, matterId2, matterId3 });

        var byRole = data.GetProperty("byRole");
        byRole.GetProperty("owner").GetArrayLength().Should().Be(1);
        byRole.GetProperty("assignedAttorney").GetArrayLength().Should().Be(2);
    }

    [Fact]
    public async Task ExecuteAsync_PassesRolesAndIncludeRelatedToResolver()
    {
        // Arrange
        var userId = Guid.NewGuid();
        MembershipResolveOptions? capturedOptions = null;
        _resolverMock
            .Setup(r => r.ResolveAsync(
                userId,
                "sprk_matter",
                It.IsAny<MembershipResolveOptions>(),
                It.IsAny<CancellationToken>()))
            .Callback<Guid, string, MembershipResolveOptions?, CancellationToken>(
                (_, _, opts, _) => capturedOptions = opts)
            .ReturnsAsync(BuildEmptyResponse("sprk_matter", userId));

        var context = CreateContext(
            configJson: """{"entityType":"sprk_matter","roles":["owner","assignedAttorney"],"includeRelated":true}""",
            outputVariable: "myMatters",
            userId: userId);

        // Act
        var result = await _executor.ExecuteAsync(context, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        capturedOptions.Should().NotBeNull();
        capturedOptions!.Roles.Should().BeEquivalentTo(new[] { "owner", "assignedAttorney" });
        capturedOptions!.IncludeRelated.Should().NotBeNull();
        capturedOptions!.IncludeRelated!.Should().Contain("*");
    }

    #endregion

    #region ExecuteAsync — edge cases

    [Fact]
    public async Task ExecuteAsync_EmptyResolverResponse_BindsEmptyArray()
    {
        // Arrange
        var userId = Guid.NewGuid();
        _resolverMock
            .Setup(r => r.ResolveAsync(
                userId,
                "sprk_matter",
                It.IsAny<MembershipResolveOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildEmptyResponse("sprk_matter", userId));

        var context = CreateContext(
            configJson: """{"entityType":"sprk_matter"}""",
            outputVariable: "myMatters",
            userId: userId);

        // Act
        var result = await _executor.ExecuteAsync(context, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        result.StructuredData.Should().NotBeNull();
        var data = result.StructuredData!.Value;
        data.GetProperty("count").GetInt32().Should().Be(0);
        data.GetProperty("ids").GetArrayLength().Should().Be(0); // empty, NOT null
    }

    [Fact]
    public async Task ExecuteAsync_MissingUserId_ReturnsValidationError()
    {
        // Arrange — context has no UserId set, no previous outputs with userId
        var context = CreateContext(
            configJson: """{"entityType":"sprk_matter"}""",
            outputVariable: "myMatters",
            userId: null);

        // Act
        var result = await _executor.ExecuteAsync(context, CancellationToken.None);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(NodeErrorCodes.ValidationFailed);
        result.ErrorMessage.Should().Contain("UserId");
        _resolverMock.Verify(
            r => r.ResolveAsync(It.IsAny<Guid>(), It.IsAny<string>(),
                It.IsAny<MembershipResolveOptions>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "resolver should NOT be called when UserId is missing");
    }

    [Fact]
    public async Task ExecuteAsync_ValidationFails_ReturnsValidationError()
    {
        // Arrange — config missing entityType
        var context = CreateContext(
            configJson: """{"roles":["owner"]}""",
            outputVariable: "myMatters",
            userId: Guid.NewGuid());

        // Act
        var result = await _executor.ExecuteAsync(context, CancellationToken.None);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(NodeErrorCodes.ValidationFailed);
        _resolverMock.Verify(
            r => r.ResolveAsync(It.IsAny<Guid>(), It.IsAny<string>(),
                It.IsAny<MembershipResolveOptions>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_WhenResolverThrowsArgumentException_ReturnsValidationError()
    {
        // Arrange — resolver rejects (e.g., blank entityType caught at resolver layer)
        var userId = Guid.NewGuid();
        _resolverMock
            .Setup(r => r.ResolveAsync(
                userId,
                It.IsAny<string>(),
                It.IsAny<MembershipResolveOptions>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ArgumentException("entityType must not be null/empty"));

        var context = CreateContext(
            configJson: """{"entityType":"sprk_matter"}""",
            outputVariable: "myMatters",
            userId: userId);

        // Act
        var result = await _executor.ExecuteAsync(context, CancellationToken.None);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(NodeErrorCodes.ValidationFailed);
        result.ErrorMessage.Should().Contain("rejected");
    }

    [Fact]
    public async Task ExecuteAsync_WhenResolverThrowsUnexpected_ReturnsInternalError()
    {
        // Arrange
        var userId = Guid.NewGuid();
        _resolverMock
            .Setup(r => r.ResolveAsync(
                userId,
                It.IsAny<string>(),
                It.IsAny<MembershipResolveOptions>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Redis unavailable"));

        var context = CreateContext(
            configJson: """{"entityType":"sprk_matter"}""",
            outputVariable: "myMatters",
            userId: userId);

        // Act
        var result = await _executor.ExecuteAsync(context, CancellationToken.None);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(NodeErrorCodes.InternalError);
        result.ErrorMessage.Should().Contain("Redis unavailable");
    }

    [Fact]
    public async Task ExecuteAsync_WhenCancelled_ReturnsCancelledError()
    {
        // Arrange
        var userId = Guid.NewGuid();
        _resolverMock
            .Setup(r => r.ResolveAsync(
                It.IsAny<Guid>(), It.IsAny<string>(),
                It.IsAny<MembershipResolveOptions>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());

        var context = CreateContext(
            configJson: """{"entityType":"sprk_matter"}""",
            outputVariable: "myMatters",
            userId: userId);

        // Act
        var result = await _executor.ExecuteAsync(context, CancellationToken.None);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(NodeErrorCodes.Cancelled);
    }

    #endregion

    #region DI pattern verification

    [Fact]
    public async Task ExecuteAsync_UsesScopePerInvocation()
    {
        // Arrange — verifies the Singleton+Scoped DI pattern: CreateScope must be called
        // exactly once per ExecuteAsync invocation, and the scope must be disposed
        // (via the using-var) so that the scoped IMembershipResolverService and its
        // transitive Scoped dependencies (Dataverse client, Redis) are not leaked.
        var userId = Guid.NewGuid();
        _resolverMock
            .Setup(r => r.ResolveAsync(
                userId, It.IsAny<string>(),
                It.IsAny<MembershipResolveOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildEmptyResponse("sprk_matter", userId));

        var context = CreateContext(
            configJson: """{"entityType":"sprk_matter"}""",
            outputVariable: "myMatters",
            userId: userId);

        // Act
        await _executor.ExecuteAsync(context, CancellationToken.None);

        // Assert
        _scopeFactoryMock.Verify(f => f.CreateScope(), Times.Once,
            "executor must create exactly one scope per ExecuteAsync invocation");
        _scopeMock.Verify(s => s.Dispose(), Times.Once,
            "executor must dispose the scope via the using-var pattern");
    }

    [Fact]
    public async Task ExecuteAsync_ScopeNotCreated_WhenValidationFails()
    {
        // Arrange — invalid config should fail-fast BEFORE any scope is created.
        // This protects against unnecessary Scoped-service activation cost on bad input.
        var context = CreateContext(
            configJson: null,
            outputVariable: "myMatters",
            userId: Guid.NewGuid());

        // Act
        var result = await _executor.ExecuteAsync(context, CancellationToken.None);

        // Assert
        result.Success.Should().BeFalse();
        _scopeFactoryMock.Verify(f => f.CreateScope(), Times.Never,
            "scope should not be created when validation fails");
    }

    #endregion

    #region Helpers

    private static NodeExecutionContext CreateContext(
        string? configJson,
        string? outputVariable,
        Guid? userId = null)
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
                Name = "Lookup User Membership",
                ExecutionOrder = 1,
                OutputVariable = outputVariable!,
                ConfigJson = configJson,
                IsActive = true
            },
            Action = new AnalysisAction
            {
                Id = actionId,
                Name = "Lookup User Membership"
            },
            ActionType = ActionType.LookupUserMembership,
            Scopes = new ResolvedScopes([], [], []),
            TenantId = "test-tenant",
            UserId = userId
        };
    }

    private static MembershipResponse BuildResponse(
        string entityType,
        Guid systemUserId,
        IReadOnlyList<Guid> ids,
        IReadOnlyDictionary<string, IReadOnlyList<Guid>> byRole)
    {
        return new MembershipResponse(
            EntityType: entityType,
            PersonIdentity: new PersonIdentity(systemUserId),
            Ids: ids,
            ByRole: byRole,
            Count: ids.Count,
            CacheExpiresAt: DateTimeOffset.UtcNow.AddMinutes(5),
            ContinuationToken: null);
    }

    private static MembershipResponse BuildEmptyResponse(string entityType, Guid systemUserId)
    {
        return new MembershipResponse(
            EntityType: entityType,
            PersonIdentity: new PersonIdentity(systemUserId),
            Ids: Array.Empty<Guid>(),
            ByRole: new Dictionary<string, IReadOnlyList<Guid>>(),
            Count: 0,
            CacheExpiresAt: DateTimeOffset.UtcNow.AddMinutes(5),
            ContinuationToken: null);
    }

    #endregion
}
