using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Spaarke.Core.Auth;
using Spaarke.Core.Auth.Rules;
using Spaarke.Dataverse;
using Xunit;

namespace Spe.Bff.Api.Tests;

public class AuthorizationTests
{
    private readonly AuthorizationService _authService;
    private readonly TestAccessDataSource _testDataSource;

    public AuthorizationTests()
    {
        _testDataSource = new TestAccessDataSource();
        var logger = new TestLogger<AuthorizationService>();
        var rules = new IAuthorizationRule[]
        {
            new ExplicitDenyRule(),
            new ExplicitGrantRule(),
            new TeamMembershipRule()
        };

        _authService = new AuthorizationService(_testDataSource, rules, logger);
    }

    [Fact]
    public async Task AuthorizeAsync_WithExplicitGrant_ShouldAllow()
    {
        // Arrange
        var context = new AuthorizationContext
        {
            UserId = "user1",
            ResourceId = "resource1",
            Operation = "read"
        };

        _testDataSource.SetUserAccess("user1", "resource1", AccessLevel.Grant);

        // Act
        var result = await _authService.AuthorizeAsync(context);

        // Assert
        result.IsAllowed.Should().BeTrue();
    }

    [Fact]
    public async Task AuthorizeAsync_WithExplicitDeny_ShouldDeny()
    {
        // Arrange
        var context = new AuthorizationContext
        {
            UserId = "user1",
            ResourceId = "resource1",
            Operation = "read"
        };

        _testDataSource.SetUserAccess("user1", "resource1", AccessLevel.Deny);

        // Act
        var result = await _authService.AuthorizeAsync(context);

        // Assert
        result.IsAllowed.Should().BeFalse();
        result.ReasonCode.Should().Be("sdap.access.deny.explicit");
    }

    private class TestAccessDataSource : IAccessDataSource
    {
        private readonly Dictionary<string, AccessLevel> _userAccess = new();

        public void SetUserAccess(string userId, string resourceId, AccessLevel level)
        {
            _userAccess[$"{userId}:{resourceId}"] = level;
        }

        public Task<AccessSnapshot> GetUserAccessAsync(string userId, string resourceId, CancellationToken ct = default)
        {
            var key = $"{userId}:{resourceId}";
            var level = _userAccess.TryGetValue(key, out var value) ? value : AccessLevel.None;

            var snapshot = new AccessSnapshot
            {
                UserId = userId,
                ResourceId = resourceId,
                AccessLevel = level,
                TeamMemberships = new[] { "team1" },
                Roles = new[] { "reader" }
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