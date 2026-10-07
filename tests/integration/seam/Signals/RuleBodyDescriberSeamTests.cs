using Azure.Core;
using Azure.Identity;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk.Query;
using NSubstitute;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Signals;
using Xunit;

namespace Sprk.Bff.Api.Tests.Seam.Signals;

/// <summary>
/// Real-Dataverse pairing for <see cref="RuleBodyDescriber"/> (task 026; project testing rule: a test asserting
/// reference-row names must touch the real schema). The describer runs against the DEPLOYED <c>sprk_triagecategory</c>
/// and <c>sprk_eventtype_ref</c> rows through the same SDK call the BFF's <see cref="IGenericEntityService"/> makes
/// (<c>RetrieveMultipleAsync(QueryExpression)</c>), so a renamed category, or a wrong name column in
/// <see cref="RuleBodyDescriber.LookupColumns"/>, fails here and not in production. Read-only.
/// </summary>
/// <remarks>
/// Skip-via-return, opt-in, same convention as <see cref="SignalPredicateTests"/>: unless
/// <see cref="SignalPredicateTests.UrlEnvVar"/> is set every test returns immediately, so CI runs zero live operations.
/// Credential: <see cref="DefaultAzureCredential"/> (set <c>AZURE_TOKEN_CREDENTIALS=AzureCliCredential</c> locally).
/// </remarks>
[Trait("status", "new")]
[Trait("Category", "Live")]
public sealed class RuleBodyDescriberSeamTests
{
    private static string Fixture(string name)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "src", "server", "api", "Sprk.Bff.Api", "Program.cs")))
        {
            dir = dir.Parent;
        }

        return File.ReadAllText(Path.Combine(dir!.FullName, "tests", "fixtures", "signals", name));
    }

    private static RuleBodyDescriber LiveDescriber(ServiceClient client)
    {
        var entities = Substitute.For<IGenericEntityService>();
        entities.RetrieveMultipleAsync(Arg.Any<QueryExpression>(), Arg.Any<CancellationToken>())
            .Returns(call => client.RetrieveMultipleAsync(call.Arg<QueryExpression>(), call.Arg<CancellationToken>()));
        var schema = new RuleBodySchemaValidator();
        var compiler = new PredicateCompiler(schema, new FakeTimeProvider(new DateTimeOffset(2026, 10, 7, 0, 0, 0, TimeSpan.Zero)));
        return new RuleBodyDescriber(new PolicyVersionValidator(schema, compiler, NullLogger<PolicyVersionValidator>.Instance), entities, NullLogger<RuleBodyDescriber>.Instance);
    }

    private static ServiceClient? Connect()
    {
        var urlText = Environment.GetEnvironmentVariable(SignalPredicateTests.UrlEnvVar);
        if (string.IsNullOrWhiteSpace(urlText))
        {
            return null;
        }

        var url = new Uri(urlText.TrimEnd('/'));
        var scope = $"{url.GetLeftPart(UriPartial.Authority)}/.default";
        var credential = new DefaultAzureCredential();
        var client = new ServiceClient(
            instanceUrl: url,
            tokenProviderFunction: _ => Task.FromResult(credential.GetToken(new TokenRequestContext(new[] { scope }), CancellationToken.None).Token),
            useUniqueInstance: true);
        client.IsReady.Should().BeTrue(client.LastError);
        return client;
    }

    [Fact]
    public async Task PathBBody_NamesTheLiveFeeAndScopeCategories()
    {
        using var client = Connect();
        if (client is null) return;

        var result = await LiveDescriber(client).DescribeAsync(Fixture("pathb-existence.rulebody.json"), CancellationToken.None);

        result.IsRefused.Should().BeFalse(result.Refusal);
        result.Description!.Clauses[0].Text.Should().Contain("classified as Fee / rate change or Scope / budget change");
    }

    [Fact]
    public async Task OverdueTaskBody_NamesTheLiveTaskEventType()
    {
        using var client = Connect();
        if (client is null) return;

        var result = await LiveDescriber(client).DescribeAsync(Fixture("do-overdue-task.rulebody.json"), CancellationToken.None);

        result.IsRefused.Should().BeFalse(result.Refusal);
        result.Description!.Clauses.Single().Text.Should().StartWith("Each event where event type is Task,");
    }
}
