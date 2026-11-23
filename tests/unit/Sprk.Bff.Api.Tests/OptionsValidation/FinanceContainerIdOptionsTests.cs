using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Infrastructure.DI;
using Xunit;

namespace Sprk.Bff.Api.Tests.OptionsValidation;

/// <summary>
/// ebilling-r1 tasks 092 and 124 (EB-D-163): eBilling configures no SharePoint Embedded container. Export files (092) and Procedure
/// sources (124) are Stored Files in the Finance file store, so <c>Finance:EbillingOutputContainerId</c> and
/// <c>Finance:ProcedureLibraryContainerId</c> are retired: an App Service setting still carrying either (until task 079 removes it)
/// binds to nothing, the host starts, and the validator reports nothing about it, whatever the value is (a valid container id, a
/// malformed value, or the id of another feature's container) and also when no such setting exists.
/// </summary>
/// <remarks>
/// Runs through the REAL <see cref="FinanceModule.AddFinanceModule"/> options chain (ValidateOnStart), as
/// <c>EbillingEnabledFilterTests</c> does for <c>EbillingOptions</c>.
/// </remarks>
[Trait("Category", "Configuration")]
public class FinanceContainerIdOptionsTests
{
    private const string ProcedureLibraryKey = "Finance:ProcedureLibraryContainerId";
    private const string RetiredOutputKey = "Finance:EbillingOutputContainerId";
    private const string EmailDefaultKey = "EmailProcessing:DefaultContainerId";
    private const string ArchiveKey = "Communication:ArchiveContainerId";
    private const string IdA = "b!tqSgSM13R06R6O5ymHDynNNtZCRkudVMkCm7XnMdUkAEyFUFBjlmQJQzNXlrHp-y";
    private const string IdB = "b!1m_ZuA0VWUKCsWg44OvUtNNtZCRkudVMkCm7XnMdUkAEyFUFBjlmQJQzNXlrHp-y";

    private static IHost BuildHost(Dictionary<string, string?> settings) =>
        new HostBuilder()
            .ConfigureAppConfiguration(cb => cb.AddInMemoryCollection(settings))
            .ConfigureServices((ctx, services) => services.AddFinanceModule(ctx.Configuration))
            .Build();

    [Fact(DisplayName = "124: with no container setting at all the host starts and the validator reports nothing")]
    public async Task NoSetting_Boots()
    {
        using var host = BuildHost(new());

        await host.StartAsync();

        ValidatorResult(host).Succeeded.Should().BeTrue();
        await host.StopAsync();
    }

    // A leftover app setting under a retired key: a valid id, an empty or blank value, every malformed value the old key refused
    // at start-up, and an id equal to another feature's container (the old collision check). None is read.
    [Theory(DisplayName = "124: a leftover Finance__ProcedureLibraryContainerId binds to nothing, the host starts and the validator reports nothing")]
    [InlineData(IdA)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("8a6ce34c-6055-4681-8f87-2f4f9f921c06")]   // a container TYPE id
    [InlineData("#{SPE_PROCEDURE_LIBRARY_CONTAINER_ID}#")] // an unsubstituted template token
    [InlineData("b!")]
    [InlineData("b!two ids")]
    [InlineData("b!x;b!y")]
    [InlineData("b!x,b!y")]
    public async Task LeftoverProcedureLibraryKey_IsIgnored_WhateverItHolds(string value)
    {
        using var host = BuildHost(new() { [ProcedureLibraryKey] = value });

        await host.StartAsync();

        ValidatorResult(host).Succeeded.Should().BeTrue();
        await host.StopAsync();
    }

    [Theory(DisplayName = "124 must-not-fire: the old collision with the email default or the archive container no longer fails start-up")]
    [InlineData(EmailDefaultKey)]
    [InlineData(ArchiveKey)]
    public async Task LeftoverProcedureLibraryKey_EqualToAnotherContainer_Boots(string otherKey)
    {
        using var host = BuildHost(new() { [ProcedureLibraryKey] = IdA, [otherKey] = IdA });

        await host.StartAsync();

        ValidatorResult(host).Succeeded.Should().BeTrue();
        await host.StopAsync();
    }

    // Task 092: the other retired key is not read either.
    [Theory(DisplayName = "092: a leftover Finance__EbillingOutputContainerId binds to nothing, whatever it holds")]
    [InlineData("b!")]
    [InlineData("b!x,b!y")]
    [InlineData(IdA)]
    public async Task RetiredOutputKey_IsIgnored_WhateverItHolds(string value)
    {
        using var host = BuildHost(new() { [RetiredOutputKey] = value, [ProcedureLibraryKey] = IdA, [ArchiveKey] = IdB });

        await host.StartAsync();

        ValidatorResult(host).Succeeded.Should().BeTrue();
        await host.StopAsync();
    }

    [Fact(DisplayName = "124: FinanceOptions has no member that binds either retired key")]
    public void FinanceOptions_HasNoContainerIdMember()
    {
        typeof(FinanceOptions).GetProperties().Select(p => p.Name)
            .Should().NotContain(name => name.Contains("Container", StringComparison.OrdinalIgnoreCase));
    }

    // Must-fire control: the validator is still live. A malformed Finance:FileStore endpoint fails start-up, so a pass above is not
    // the validator being skipped.
    [Fact(DisplayName = "124 control: the validator still fails start-up on a bad Finance:FileStore endpoint")]
    public async Task Control_ABadFileStoreEndpoint_StillFailsStartup()
    {
        using var host = BuildHost(new() { ["Finance:FileStore:BlobEndpoint"] = "not-a-url", [ProcedureLibraryKey] = IdA });

        var act = async () => await host.StartAsync();

        (await act.Should().ThrowAsync<OptionsValidationException>())
            .Which.Message.Should().Contain("Finance:FileStore:BlobEndpoint");
    }

    private static ValidateOptionsResult ValidatorResult(IHost host)
    {
        var options = host.Services.GetRequiredService<IOptions<FinanceOptions>>().Value;
        return host.Services.GetServices<IValidateOptions<FinanceOptions>>()
            .Select(v => v.Validate(Options.DefaultName, options))
            .Aggregate(ValidateOptionsResult.Success, (all, next) => next.Failed ? next : all);
    }
}
