// REGRESSION — #3198: HandoffUrlBuilder never falls back to a hard-coded dev Dataverse environment (spaarkeai-mcp-bff-api-r2 task 030).
// KEEP path: regression.
//
// WHAT WAS WRONG: AddHandoffUrlBuilder built the deep-link base from Dataverse:EnvironmentUrl, then DataverseServiceClient:Url, then the
// literal https://spaarkedev1.crm.dynamics.com. A customer BFF with the URL under another key silently sent every server-built deep link
// (chat refusal links, the MCP run tools' console link) to Spaarke's dev environment.
//
// WHAT RUNS: the REAL AddToolFramework registration, resolved from a service collection; nothing is substituted.

using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Handoff;
using Xunit;

namespace Sprk.Bff.Api.Tests.Integration.Regression.Ai;

public sealed class Issue3198_HandoffUrlBuilderNoDefaultEnvironmentTests
{
    private const string CustomerUrl = "https://customer-a.crm4.dynamics.com";

    private static HandoffUrlBuilder Resolve(Dictionary<string, string?> settings, Action<IServiceCollection>? more = null)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var services = new ServiceCollection().AddLogging();
        services.AddSingleton<IConfiguration>(configuration);
        more?.Invoke(services);
        services.AddToolFramework(configuration);
        return services.BuildServiceProvider().GetRequiredService<HandoffUrlBuilder>();
    }

    [Theory(DisplayName = "#3198: with no Dataverse:EnvironmentUrl the builder refuses (options validation), never the dev environment")]
    [InlineData(null)]
    [InlineData("https://other-key.crm.dynamics.com")]
    public void NoEnvironmentUrl_IsAnOptionsValidationFailure_NotTheDevEnvironment(string? serviceClientUrl)
    {
        var act = () => Resolve(new Dictionary<string, string?> { ["DataverseServiceClient:Url"] = serviceClientUrl });

        act.Should().Throw<OptionsValidationException>()
            .Which.Failures.Should().ContainSingle(f => f.Contains("Dataverse:EnvironmentUrl", StringComparison.Ordinal));
    }

    [Fact(DisplayName = "#3198: the validated Dataverse:EnvironmentUrl options are the base of every link")]
    public void ValidatedOptions_AreTheBase()
    {
        var builder = Resolve(
            new Dictionary<string, string?>(),
            services => services.AddOptions<DataverseOptions>().Configure(o => o.EnvironmentUrl = CustomerUrl + "/"));

        builder.BuildConsoleUrl(null).Should().StartWith(CustomerUrl + "/main.aspx?").And.NotContain("spaarkedev1");
        builder.BuildAnalysisWorkspaceUrl(Guid.NewGuid(), Guid.Empty).Should().StartWith(CustomerUrl + "/main.aspx?");
    }

    [Fact(DisplayName = "#3198: a host that binds no options still reads the same key from configuration")]
    public void ConfigurationKey_IsTheSameKey()
    {
        var builder = Resolve(new Dictionary<string, string?> { ["Dataverse:EnvironmentUrl"] = CustomerUrl });

        builder.BuildConsoleUrl(null).Should().StartWith(CustomerUrl + "/main.aspx?");
    }
}
