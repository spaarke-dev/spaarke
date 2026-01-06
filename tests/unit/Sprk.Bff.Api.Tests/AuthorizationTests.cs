using FluentAssertions;
using Microsoft.Extensions.Logging;
using Spaarke.Core.Auth;
using Spaarke.Core.Auth.Rules;
using Spaarke.Dataverse;
using Xunit;

namespace Sprk.Bff.Api.Tests;

/// <summary>
/// Tests for the authorization system using the granular AccessRights model.
/// These tests verify that OperationAccessRule correctly evaluates user permissions
/// against operations defined in OperationAccessPolicy.
/// </summary>
public class AuthorizationTests
{
    private readonly AuthorizationService _authService;
    private readonly TestAccessDataSource _testDataSource;

    public AuthorizationTests()
    {
        _testDataSource = new TestAccessDataSource();
        var logger = new TestLogger<AuthorizationService>();
        // Single rule: OperationAccessRule handles all authorization via OperationAccessPolicy
        var rules = new IAuthorizationRule[]
        {
            new OperationAccessRule(new TestLogger<OperationAccessRule>())
        };

        _authService = new AuthorizationService(_testDataSource, rules, logger);
    }

    [Fact]
    public async Task AuthorizeAsync_WithReadAccess_ShouldAllow()
    {
        // Arrange - use "read_metadata" which requires AccessRights.Read
        var context = new AuthorizationContext
        {
            UserId = "user1",
            ResourceId = "resource1",
            Operation = "read_metadata"
        };

        _testDataSource.SetUserAccess("user1", "resource1", AccessRights.Read);

        // Act
        var result = await _authService.AuthorizeAsync(context);

        // Assert
        result.IsAllowed.Should().BeTrue();
    }

    [Fact]
    public async Task AuthorizeAsync_WithNoAccess_ShouldDeny()
    {
        // Arrange - use "read_metadata" which requires AccessRights.Read
        var context = new AuthorizationContext
        {
            UserId = "user1",
            ResourceId = "resource1",
            Operation = "read_metadata"
        };

        _testDataSource.SetUserAccess("user1", "resource1", AccessRights.None);

        // Act
        var result = await _authService.AuthorizeAsync(context);

        // Assert
        result.IsAllowed.Should().BeFalse();
    }

    [Fact]
    public async Task AuthorizeAsync_WithCombinedRights_ShouldAllow()
    {
        // Arrange - use "driveitem.update" which requires AccessRights.Write
        var context = new AuthorizationContext
        {
            UserId = "user1",
            ResourceId = "resource1",
            Operation = "driveitem.update"
        };

        _testDataSource.SetUserAccess("user1", "resource1", AccessRights.Read | AccessRights.Write);

        // Act
        var result = await _authService.AuthorizeAsync(context);

        // Assert
        result.IsAllowed.Should().BeTrue();
    }

    private class TestAccessDataSource : IAccessDataSource
    {
        private readonly Dictionary<string, AccessRights> _userAccess = new();

        public void SetUserAccess(string userId, string resourceId, AccessRights rights)
        {
            _userAccess[$"{userId}:{resourceId}"] = rights;
        }

        public Task<AccessSnapshot> GetUserAccessAsync(string userId, string resourceId, CancellationToken ct = default)
        {
            var key = $"{userId}:{resourceId}";
            var rights = _userAccess.TryGetValue(key, out var value) ? value : AccessRights.None;

            var snapshot = new AccessSnapshot
            {
                UserId = userId,
                ResourceId = resourceId,
                AccessRights = rights,
                TeamMemberships = Array.Empty<string>(),
                Roles = Array.Empty<string>()
            };

            return Task.FromResult(snapshot);
        }
    }

    private class TestLogger<T> : ILogger<T>
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => false;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) { }
    }
}
