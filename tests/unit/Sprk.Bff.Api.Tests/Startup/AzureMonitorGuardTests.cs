using FluentAssertions;
using Sprk.Bff.Api.Infrastructure.Startup;
using Xunit;

namespace Sprk.Bff.Api.Tests.Startup;

/// <summary>
/// Unit tests for <see cref="AzureMonitorGuard"/> (R2 FR-06,
/// spaarke-redis-cache-remediation-r2 task 006).
///
/// Mirrors the contract enforced in <c>Program.cs</c>:
/// <list type="bullet">
///   <item>Non-Development env + missing/empty conn string → throw.</item>
///   <item>Development env + missing/empty conn string → return <c>false</c>.</item>
///   <item>Any env + non-empty conn string → return <c>true</c>.</item>
/// </list>
///
/// Authoring note: this is pure domain logic (a single static method with no
/// collaborators, no I/O, no DI). Placement under <c>tests/unit/</c> is correct
/// per <c>tests/CLAUDE.md</c> "Authoring Template — Unit (DOMAIN LOGIC ONLY)".
/// </summary>
public sealed class AzureMonitorGuardTests
{
    private const string ValidConnString =
        "InstrumentationKey=11111111-2222-3333-4444-555555555555;IngestionEndpoint=https://example.in.applicationinsights.azure.com/";

    [Fact]
    public void ShouldWireExporter_InProductionWithNullConnString_ThrowsInvalidOperationException()
    {
        var act = () => AzureMonitorGuard.ShouldWireExporter("Production", connectionString: null);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*APPLICATIONINSIGHTS_CONNECTION_STRING is required*")
            .WithMessage("*ASPNETCORE_ENVIRONMENT=Production*")
            .WithMessage("*App Service*")
            .WithMessage("*Key Vault reference*");
    }

    [Fact]
    public void ShouldWireExporter_InProductionWithEmptyConnString_Throws()
    {
        var act = () => AzureMonitorGuard.ShouldWireExporter("Production", connectionString: string.Empty);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*APPLICATIONINSIGHTS_CONNECTION_STRING is required*");
    }

    [Fact]
    public void ShouldWireExporter_InProductionWithWhitespaceConnString_Throws()
    {
        var act = () => AzureMonitorGuard.ShouldWireExporter("Production", connectionString: "   ");

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*APPLICATIONINSIGHTS_CONNECTION_STRING is required*");
    }

    [Fact]
    public void ShouldWireExporter_InStagingWithNullConnString_Throws()
    {
        // Staging is also "non-Development" per the spec contract — fail-fast
        // applies to every env that is NOT Development, not just Production.
        var act = () => AzureMonitorGuard.ShouldWireExporter("Staging", connectionString: null);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*ASPNETCORE_ENVIRONMENT=Staging*");
    }

    [Fact]
    public void ShouldWireExporter_InDevelopmentWithNullConnString_ReturnsFalse()
    {
        var result = AzureMonitorGuard.ShouldWireExporter("Development", connectionString: null);

        result.Should().BeFalse(
            "Development env preserves dev-convenience pass-through when conn string is missing");
    }

    [Fact]
    public void ShouldWireExporter_InDevelopmentWithEmptyConnString_ReturnsFalse()
    {
        var result = AzureMonitorGuard.ShouldWireExporter("Development", connectionString: string.Empty);

        result.Should().BeFalse();
    }

    [Fact]
    public void ShouldWireExporter_InDevelopmentWithValidConnString_ReturnsTrue()
    {
        var result = AzureMonitorGuard.ShouldWireExporter("Development", ValidConnString);

        result.Should().BeTrue(
            "even Development should wire the exporter when a conn string is explicitly provided");
    }

    [Fact]
    public void ShouldWireExporter_InProductionWithValidConnString_ReturnsTrue()
    {
        var result = AzureMonitorGuard.ShouldWireExporter("Production", ValidConnString);

        result.Should().BeTrue();
    }

    [Theory]
    [InlineData("development")]    // lowercase
    [InlineData("DEVELOPMENT")]    // uppercase
    [InlineData("Development")]    // canonical
    public void ShouldWireExporter_DevelopmentEnvNameIsCaseInsensitive(string envName)
    {
        // ASP.NET Core's IsDevelopment() is case-insensitive; the guard must match.
        var result = AzureMonitorGuard.ShouldWireExporter(envName, connectionString: null);

        result.Should().BeFalse();
    }
}
