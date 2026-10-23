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
/// ebilling-r1 task 028 (FR-16, D-55): the Procedure Library and E-Billing Output Files container ids are OPTIONAL at
/// start-up — a stamp without them boots (no deployment channel writes them yet; <c>IOptionsDriftTests</c>) — but a value
/// that is not a container id fails host start-up with a message naming the key.
/// </summary>
/// <remarks>
/// Runs through the REAL <see cref="FinanceModule.AddFinanceModule"/> options chain (ValidateOnStart), as
/// <c>EbillingEnabledFilterTests</c> does for <c>EbillingOptions</c>.
/// </remarks>
[Trait("Category", "Configuration")]
public class FinanceContainerIdOptionsTests
{
    private const string ProcedureLibraryKey = "Finance:ProcedureLibraryContainerId";
    private const string OutputKey = "Finance:EbillingOutputContainerId";

    private static IHost BuildHost(Dictionary<string, string?> settings) =>
        new HostBuilder()
            .ConfigureAppConfiguration(cb => cb.AddInMemoryCollection(settings))
            .ConfigureServices((ctx, services) => services.AddFinanceModule(ctx.Configuration))
            .Build();

    [Fact]
    public async Task WellFormedIds_Boot_AndBind()
    {
        using var host = BuildHost(new()
        {
            [ProcedureLibraryKey] = "b!tqSgSM13R06R6O5ymHDynNNtZCRkudVMkCm7XnMdUkAEyFUFBjlmQJQzNXlrHp-y",
            [OutputKey] = "b!1m_ZuA0VWUKCsWg44OvUtNNtZCRkudVMkCm7XnMdUkAEyFUFBjlmQJQzNXlrHp-y",
        });

        await host.StartAsync();

        var options = host.Services.GetRequiredService<IOptions<FinanceOptions>>().Value;
        options.ProcedureLibraryContainerId.Should().StartWith("b!tqSg");
        options.EbillingOutputContainerId.Should().StartWith("b!1m_Z");
        await host.StopAsync();
    }

    [Fact]
    public async Task BothAbsentOrEmpty_Boot()
    {
        using var absent = BuildHost(new());
        await absent.StartAsync();
        await absent.StopAsync();

        using var empty = BuildHost(new() { [ProcedureLibraryKey] = "", [OutputKey] = "" });
        await empty.StartAsync();
        empty.Services.GetRequiredService<IOptions<FinanceOptions>>().Value.EbillingOutputContainerId.Should().BeEmpty();
        await empty.StopAsync();
    }

    [Theory]
    [InlineData(ProcedureLibraryKey, "8a6ce34c-6055-4681-8f87-2f4f9f921c06")]   // a container TYPE id, not a container
    [InlineData(ProcedureLibraryKey, "#{SPE_PROCEDURE_LIBRARY_CONTAINER_ID}#")] // an unsubstituted template token
    [InlineData(OutputKey, "b!")]                                               // the prefix alone
    [InlineData(OutputKey, "b!two ids")]                                        // whitespace inside
    [InlineData(OutputKey, "b!x,b!y")]                                          // a list of ids, not one id
    [InlineData(ProcedureLibraryKey, "b!x;b!y")]                                // a list of ids, not one id
    public async Task MalformedId_FailsStartup_NamingTheKey(string key, string value)
    {
        using var host = BuildHost(new() { [key] = value });

        var act = async () => await host.StartAsync();

        (await act.Should().ThrowAsync<OptionsValidationException>())
            .Which.Message.Should().Contain(key);
    }

    private const string EmailDefaultKey = "EmailProcessing:DefaultContainerId";
    private const string ArchiveKey = "Communication:ArchiveContainerId";
    private const string IdA = "b!tqSgSM13R06R6O5ymHDynNNtZCRkudVMkCm7XnMdUkAEyFUFBjlmQJQzNXlrHp-y";
    private const string IdB = "b!1m_ZuA0VWUKCsWg44OvUtNNtZCRkudVMkCm7XnMdUkAEyFUFBjlmQJQzNXlrHp-y";
    private const string IdC = "b!9zQ_aBcDeFgHiJkLmNoPqRsTuVwXyZ0123456789abcdefghijklmnopqrstuvwxyz-_";

    // Refusal 1: the two eBilling containers are the same container.
    [Fact]
    public async Task ProcedureLibraryEqualsOutput_FailsStartup_NamingBothKeys()
    {
        using var host = BuildHost(new() { [ProcedureLibraryKey] = IdA, [OutputKey] = IdA });

        var act = async () => await host.StartAsync();

        var message = (await act.Should().ThrowAsync<OptionsValidationException>()).Which.Message;
        message.Should().Contain(ProcedureLibraryKey).And.Contain(OutputKey);
    }

    // Refusals 2-5: either eBilling key pasted with the email default or the communication archive container.
    [Theory]
    [InlineData(ProcedureLibraryKey, EmailDefaultKey)]
    [InlineData(OutputKey, EmailDefaultKey)]
    [InlineData(ProcedureLibraryKey, ArchiveKey)]
    [InlineData(OutputKey, ArchiveKey)]
    public async Task EqualsAnotherIndexedContainer_FailsStartup_NamingBothKeys(string financeKey, string otherKey)
    {
        using var host = BuildHost(new() { [financeKey] = IdA, [otherKey] = IdA });

        var act = async () => await host.StartAsync();

        var message = (await act.Should().ThrowAsync<OptionsValidationException>()).Which.Message;
        message.Should().Contain(financeKey).And.Contain(otherKey);
    }

    // Must-pass control: four distinct ids boot; and an empty Finance key never collides with an empty other key.
    [Fact]
    public async Task DistinctIds_Boot()
    {
        using var distinct = BuildHost(new()
        {
            [ProcedureLibraryKey] = IdA,
            [OutputKey] = IdB,
            [EmailDefaultKey] = IdC,
            [ArchiveKey] = "b!archiveContainerIdThatIsDifferent",
        });
        await distinct.StartAsync();
        await distinct.StopAsync();

        using var emptyEverywhere = BuildHost(new()
        {
            [ProcedureLibraryKey] = "",
            [OutputKey] = "",
            [EmailDefaultKey] = "",
            [ArchiveKey] = "",
        });
        await emptyEverywhere.StartAsync();
        await emptyEverywhere.StopAsync();
    }
}
