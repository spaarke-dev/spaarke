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
/// ebilling-r1 task 028 (FR-16, D-55): the Procedure Library container id is OPTIONAL at start-up — a stamp without it boots
/// (no deployment channel writes it yet; <c>IOptionsDriftTests</c>) — but a value that is not a container id fails host start-up
/// with a message naming the key. Task 092 retired <c>Finance:EbillingOutputContainerId</c>: eBilling files are Stored Files in the
/// Finance file store, so a value left under that key binds to nothing and is never validated.
/// </summary>
/// <remarks>
/// Runs through the REAL <see cref="FinanceModule.AddFinanceModule"/> options chain (ValidateOnStart), as
/// <c>EbillingEnabledFilterTests</c> does for <c>EbillingOptions</c>.
/// </remarks>
[Trait("Category", "Configuration")]
public class FinanceContainerIdOptionsTests
{
    private const string ProcedureLibraryKey = "Finance:ProcedureLibraryContainerId";

    /// <summary>Retired by ebilling-r1 task 092 (an App Service setting may still carry it until 079 removes it).</summary>
    private const string RetiredOutputKey = "Finance:EbillingOutputContainerId";

    private static IHost BuildHost(Dictionary<string, string?> settings) =>
        new HostBuilder()
            .ConfigureAppConfiguration(cb => cb.AddInMemoryCollection(settings))
            .ConfigureServices((ctx, services) => services.AddFinanceModule(ctx.Configuration))
            .Build();

    [Fact]
    public async Task WellFormedId_Boots_AndBinds()
    {
        using var host = BuildHost(new()
        {
            [ProcedureLibraryKey] = "b!tqSgSM13R06R6O5ymHDynNNtZCRkudVMkCm7XnMdUkAEyFUFBjlmQJQzNXlrHp-y",
        });

        await host.StartAsync();

        var options = host.Services.GetRequiredService<IOptions<FinanceOptions>>().Value;
        options.ProcedureLibraryContainerId.Should().StartWith("b!tqSg");
        await host.StopAsync();
    }

    [Fact]
    public async Task AbsentOrEmpty_Boots()
    {
        using var absent = BuildHost(new());
        await absent.StartAsync();
        await absent.StopAsync();

        using var empty = BuildHost(new() { [ProcedureLibraryKey] = "" });
        await empty.StartAsync();
        empty.Services.GetRequiredService<IOptions<FinanceOptions>>().Value.ProcedureLibraryContainerId.Should().BeEmpty();
        await empty.StopAsync();
    }

    [Theory]
    [InlineData("8a6ce34c-6055-4681-8f87-2f4f9f921c06")]   // a container TYPE id, not a container
    [InlineData("#{SPE_PROCEDURE_LIBRARY_CONTAINER_ID}#")] // an unsubstituted template token
    [InlineData("b!")]                                     // the prefix alone
    [InlineData("b!two ids")]                              // whitespace inside
    [InlineData("b!x;b!y")]                                // a list of ids, not one id
    public async Task MalformedId_FailsStartup_NamingTheKey(string value)
    {
        using var host = BuildHost(new() { [ProcedureLibraryKey] = value });

        var act = async () => await host.StartAsync();

        (await act.Should().ThrowAsync<OptionsValidationException>())
            .Which.Message.Should().Contain(ProcedureLibraryKey);
    }

    // Task 092: the retired key is not read. A malformed value, or one equal to another container key, still boots.
    [Theory]
    [InlineData("b!")]
    [InlineData("b!x,b!y")]
    [InlineData(IdA)]
    public async Task RetiredOutputKey_IsIgnored_WhateverItHolds(string value)
    {
        using var host = BuildHost(new() { [RetiredOutputKey] = value, [ProcedureLibraryKey] = IdA, [ArchiveKey] = IdB });

        await host.StartAsync();
        await host.StopAsync();
    }

    private const string EmailDefaultKey = "EmailProcessing:DefaultContainerId";
    private const string ArchiveKey = "Communication:ArchiveContainerId";
    private const string IdA = "b!tqSgSM13R06R6O5ymHDynNNtZCRkudVMkCm7XnMdUkAEyFUFBjlmQJQzNXlrHp-y";
    private const string IdB = "b!1m_ZuA0VWUKCsWg44OvUtNNtZCRkudVMkCm7XnMdUkAEyFUFBjlmQJQzNXlrHp-y";
    private const string IdC = "b!9zQ_aBcDeFgHiJkLmNoPqRsTuVwXyZ0123456789abcdefghijklmnopqrstuvwxyz-_";

    // The Procedure Library key pasted with the email default or the communication archive container (indexed containers).
    [Theory]
    [InlineData(EmailDefaultKey)]
    [InlineData(ArchiveKey)]
    public async Task EqualsAnotherIndexedContainer_FailsStartup_NamingBothKeys(string otherKey)
    {
        using var host = BuildHost(new() { [ProcedureLibraryKey] = IdA, [otherKey] = IdA });

        var act = async () => await host.StartAsync();

        var message = (await act.Should().ThrowAsync<OptionsValidationException>()).Which.Message;
        message.Should().Contain(ProcedureLibraryKey).And.Contain(otherKey);
    }

    // Must-pass control: distinct ids boot; and an empty Finance key never collides with an empty other key.
    [Fact]
    public async Task DistinctIds_Boot()
    {
        using var distinct = BuildHost(new()
        {
            [ProcedureLibraryKey] = IdA,
            [EmailDefaultKey] = IdC,
            [ArchiveKey] = IdB,
        });
        await distinct.StartAsync();
        await distinct.StopAsync();

        using var emptyEverywhere = BuildHost(new()
        {
            [ProcedureLibraryKey] = "",
            [EmailDefaultKey] = "",
            [ArchiveKey] = "",
        });
        await emptyEverywhere.StartAsync();
        await emptyEverywhere.StopAsync();
    }
}
