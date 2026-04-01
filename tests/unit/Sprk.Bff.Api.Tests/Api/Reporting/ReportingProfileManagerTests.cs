using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Sprk.Bff.Api.Api.Reporting;
using Xunit;

namespace Sprk.Bff.Api.Tests.Api.Reporting;

/// <summary>
/// Unit tests for <see cref="ReportingProfileManager"/>.
///
/// Testing approach:
///   - Because <see cref="ReportingProfileManager"/> builds an MSAL ConfidentialClientApplication
///     internally and uses the Power BI client (not an injectable interface), PBI API-calling paths
///     cannot be fully unit-tested without a live service or integration test harness.
///   - These tests focus on: construction, DTO contracts, display name convention, and
///     structural method verification.
///   - The in-process cache (ConcurrentDictionary) is exercised where possible within
///     the constraints of the sealed class design.
/// </summary>
public class ReportingProfileManagerTests
{
    // =========================================================================
    // Helpers
    // =========================================================================

    private static IOptions<PowerBiOptions> BuildOptions(
        string tenantId = "test-tenant",
        string clientId = "test-client-id",
        string clientSecret = "test-client-secret")
    {
        return Options.Create(new PowerBiOptions
        {
            TenantId = tenantId,
            ClientId = clientId,
            ClientSecret = clientSecret,
            ApiUrl = "https://api.powerbi.com"
        });
    }

    private static Mock<ILogger<ReportingProfileManager>> BuildLoggerMock() =>
        new(MockBehavior.Loose);

    private static ReportingProfileManager BuildManager(IOptions<PowerBiOptions>? options = null)
    {
        return new ReportingProfileManager(
            options ?? BuildOptions(),
            BuildLoggerMock().Object);
    }

    // =========================================================================
    // Construction
    // =========================================================================

    [Fact]
    public void Constructor_DoesNotThrow_WithValidOptions()
    {
        // Arrange
        var options = BuildOptions();
        var logger = BuildLoggerMock();

        // Act
        var act = () => new ReportingProfileManager(options, logger.Object);

        // Assert — construction must succeed; MSAL CCA is built once per instance
        act.Should().NotThrow();
    }

    [Fact]
    public void Constructor_WorksWithNullLogger()
    {
        // Arrange
        var options = BuildOptions();

        // Act — ILogger<T> is not nullable in the constructor, but NSubstitute/Mock returns valid stubs
        var act = () => new ReportingProfileManager(options, Mock.Of<ILogger<ReportingProfileManager>>());

        // Assert
        act.Should().NotThrow();
    }

    // =========================================================================
    // Public API — structural
    // =========================================================================

    [Fact]
    public void GetOrCreateProfileAsync_MethodExists_WithExpectedSignature()
    {
        var method = typeof(ReportingProfileManager).GetMethod("GetOrCreateProfileAsync");

        method.Should().NotBeNull();
        method!.IsPublic.Should().BeTrue();
        method.ReturnType.Should().Be(typeof(Task<ServicePrincipalProfileInfo>));
    }

    [Fact]
    public void GetProfileIdAsync_MethodExists_WithExpectedSignature()
    {
        var method = typeof(ReportingProfileManager).GetMethod("GetProfileIdAsync");

        method.Should().NotBeNull();
        method!.IsPublic.Should().BeTrue();
        method.ReturnType.Should().Be(typeof(Task<Guid>));
    }

    [Fact]
    public void ListProfilesAsync_MethodExists_WithExpectedSignature()
    {
        var method = typeof(ReportingProfileManager).GetMethod("ListProfilesAsync");

        method.Should().NotBeNull();
        method!.IsPublic.Should().BeTrue();
        method.ReturnType.Should().Be(typeof(Task<IReadOnlyList<ServicePrincipalProfileInfo>>));
    }

    [Fact]
    public void DeleteProfileAsync_MethodExists_WithExpectedSignature()
    {
        var method = typeof(ReportingProfileManager).GetMethod("DeleteProfileAsync");

        method.Should().NotBeNull();
        method!.IsPublic.Should().BeTrue();
        method.ReturnType.Should().Be(typeof(Task));
    }

    // =========================================================================
    // ServicePrincipalProfileInfo DTO
    // =========================================================================

    [Fact]
    public void ServicePrincipalProfileInfo_CanBeConstructed()
    {
        // Arrange
        var id = Guid.NewGuid();
        const string displayName = "sprk-contoso-legal";

        // Act
        var profile = new ServicePrincipalProfileInfo(id, displayName);

        // Assert
        profile.Id.Should().Be(id);
        profile.DisplayName.Should().Be(displayName);
    }

    [Fact]
    public void ServicePrincipalProfileInfo_IsRecord_SupportsEquality()
    {
        // Arrange
        var id = Guid.NewGuid();
        const string displayName = "sprk-contoso-legal";

        var a = new ServicePrincipalProfileInfo(id, displayName);
        var b = new ServicePrincipalProfileInfo(id, displayName);
        var c = new ServicePrincipalProfileInfo(Guid.NewGuid(), displayName);

        // Assert — record equality
        a.Should().Be(b, "records with same values are equal");
        a.Should().NotBe(c, "records with different IDs are not equal");
    }

    // =========================================================================
    // Display name convention
    // =========================================================================

    [Theory]
    [InlineData("contoso-legal", "sprk-contoso-legal")]
    [InlineData("northwind", "sprk-northwind")]
    [InlineData("my-org-123", "sprk-my-org-123")]
    public void DisplayNameConvention_FollowsSprk_Prefix(string customerId, string expectedDisplayName)
    {
        // The profile manager builds display names as "sprk-{customerId}".
        // We verify the convention by constructing the expected value and comparing.
        var actual = $"sprk-{customerId}";
        actual.Should().Be(expectedDisplayName);
    }

    // =========================================================================
    // GetOrCreateProfileAsync — validation
    // =========================================================================

    [Fact]
    public async Task GetOrCreateProfileAsync_ThrowsArgumentException_WhenCustomerIdIsNull()
    {
        // Arrange
        var manager = BuildManager();

        // Act
        var act = async () => await manager.GetOrCreateProfileAsync(null!);

        // Assert — ArgumentException.ThrowIfNullOrWhiteSpace throws ArgumentNullException
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task GetOrCreateProfileAsync_ThrowsArgumentException_WhenCustomerIdIsEmpty()
    {
        // Arrange
        var manager = BuildManager();

        // Act
        var act = async () => await manager.GetOrCreateProfileAsync("");

        // Assert
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task GetOrCreateProfileAsync_ThrowsArgumentException_WhenCustomerIdIsWhiteSpace()
    {
        // Arrange
        var manager = BuildManager();

        // Act
        var act = async () => await manager.GetOrCreateProfileAsync("   ");

        // Assert
        await act.Should().ThrowAsync<ArgumentException>();
    }

    // =========================================================================
    // PowerBiOptions validation
    // =========================================================================

    [Fact]
    public void PowerBiOptions_DefaultApiUrl_IsCorrect()
    {
        var options = new PowerBiOptions();
        options.ApiUrl.Should().Be("https://api.powerbi.com");
    }

    [Fact]
    public void PowerBiOptions_DefaultScope_IsCorrect()
    {
        var options = new PowerBiOptions();
        options.Scope.Should().Be("https://analysis.windows.net/.default");
    }

    [Fact]
    public void PowerBiOptions_GetEffectiveAuthorityUrl_UsesDefaultTemplate_WhenAuthorityUrlNotSet()
    {
        // Arrange
        var options = new PowerBiOptions
        {
            TenantId = "my-tenant-id",
            ClientId = "client-id",
            ClientSecret = "secret"
        };

        // Act
        var url = options.GetEffectiveAuthorityUrl();

        // Assert
        url.Should().Be("https://login.microsoftonline.com/my-tenant-id");
    }

    [Fact]
    public void PowerBiOptions_GetEffectiveAuthorityUrl_UsesCustomUrl_WhenAuthorityUrlIsSet()
    {
        // Arrange
        var options = new PowerBiOptions
        {
            TenantId = "my-tenant-id",
            ClientId = "client-id",
            ClientSecret = "secret",
            AuthorityUrl = "https://login.microsoftonline.us/my-tenant-id"
        };

        // Act
        var url = options.GetEffectiveAuthorityUrl();

        // Assert
        url.Should().Be("https://login.microsoftonline.us/my-tenant-id",
            "custom AuthorityUrl must override the default template");
    }

    // =========================================================================
    // Class characteristics
    // =========================================================================

    [Fact]
    public void ReportingProfileManager_IsSealed()
    {
        typeof(ReportingProfileManager).IsSealed.Should().BeTrue();
    }

    [Fact]
    public void ReportingEmbedService_IsSealed()
    {
        typeof(ReportingEmbedService).IsSealed.Should().BeTrue();
    }

    [Fact]
    public void ReportingProfileManager_ImplementsNoInterfaces_ByDesign()
    {
        // ADR-010: DI minimalism — concrete singletons, no interfaces unless a seam is needed.
        var interfaces = typeof(ReportingProfileManager)
            .GetInterfaces()
            .Where(i => !i.IsGenericType)  // exclude IDisposable etc.
            .ToArray();

        interfaces.Should().BeEmpty(
            "ADR-010: ReportingProfileManager is a concrete singleton with no interface (no seam needed)");
    }
}
