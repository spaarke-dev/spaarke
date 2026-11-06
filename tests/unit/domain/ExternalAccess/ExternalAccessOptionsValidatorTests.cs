using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sprk.Bff.Api.Infrastructure.DI;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Tests.AccessControl.IdentityBinding;
using Xunit;

namespace Sprk.Bff.Api.Tests.Infrastructure.ExternalAccess;

/// <summary>
/// ISS-005 (#1673 review K3) - a startup WARNING (never a failure) when <c>ExternalAccess:CustomerBusinessUnitId</c> is unset,
/// empty or the all-zero GUID, because every contact this BFF creates is refused while it is.
/// </summary>
public class ExternalAccessOptionsValidatorTests
{
    private static BindingLogCapture<ExternalAccessOptionsValidator> Build(string? value)
    {
        var settings = new Dictionary<string, string?>();
        if (value is not null) settings["ExternalAccess:CustomerBusinessUnitId"] = value;
        var log = new BindingLogCapture<ExternalAccessOptionsValidator>();
        _ = new ExternalAccessOptionsValidator(new ConfigurationBuilder().AddInMemoryCollection(settings).Build(), log);
        return log;
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    [InlineData("not-a-guid")]
    public void AnUnsetEmptyZeroOrMalformedValue_WarnsOnce(string? value)
    {
        var entry = Build(value).Entries.Should().ContainSingle().Subject;

        entry.Level.Should().Be(LogLevel.Warning);
        entry.Message.Should().Contain("ExternalAccess:CustomerBusinessUnitId").And.Contain("registration_owner_unconfigured");
    }

    [Fact]
    public void AConfiguredValue_DoesNotWarn()
        => Build("cb15f587-1111-4111-8111-00000000000a").Entries.Should().BeEmpty();

    [Fact]
    public void Validate_NeverFails_SoStartupIsNotBlocked()
        => new ExternalAccessOptionsValidator(
                new ConfigurationBuilder().Build(), new BindingLogCapture<ExternalAccessOptionsValidator>())
            .Validate(null, new ExternalAccessOptions()).Succeeded.Should().BeTrue();

    [Fact]
    public void TheModule_RegistersIt_AndResolvingTheOptionsWithTheSettingUnsetDoesNotThrow()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddExternalAccess();

        using var provider = services.BuildServiceProvider();

        provider.GetServices<IValidateOptions<ExternalAccessOptions>>().Should().ContainSingle(v => v is ExternalAccessOptionsValidator);
        var options = () => provider.GetRequiredService<IOptions<ExternalAccessOptions>>().Value;
        options.Should().NotThrow("an unset CustomerBusinessUnitId is a warning, not a startup failure");
        provider.GetRequiredService<IOptions<ExternalAccessOptions>>().Value.CustomerBusinessUnitId.Should().BeNull();
    }
}
