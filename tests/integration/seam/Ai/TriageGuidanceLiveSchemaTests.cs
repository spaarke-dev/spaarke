using System.Net.Http.Headers;
using System.Text.Json;
using Azure.Core;
using Azure.Identity;
using FluentAssertions;
using Sprk.Bff.Api.Services.Ai;
using Xunit;

namespace Sprk.Bff.Api.Tests.Seam.Ai;

/// <summary>
/// Real-schema pairing (ADR-038, project CLAUDE.md section 5) for the task 072 guidance read: the unit and seam
/// tests double <see cref="IScopeResolverService"/> and so pin <c>sprk_triagecategories</c> /
/// <c>sprk_name</c> / <c>sprk_classifierguidance</c> without ever asking Dataverse whether they exist. This runs
/// the exact URL production sends (<see cref="ScopeResolverService.BuildLookupGuidanceUrl"/>) against the live
/// environment and checks that every taxonomy row behind the enum carries guidance.
/// </summary>
/// <remarks>
/// Opt-in, skip-via-return: unless <see cref="UrlEnvVar"/> is set nothing touches the network, so a pass in plain
/// CI means "skipped", not "verified". Credential is <c>DefaultAzureCredential</c>
/// (<c>AZURE_TOKEN_CREDENTIALS=AzureCliCredential</c> locally), same convention as the Signals live seams.
/// </remarks>
[Trait("Category", "Live")]
public sealed class TriageGuidanceLiveSchemaTests
{
    public const string UrlEnvVar = "SIGNALS_LIVE_DATAVERSE_URL";

    [Fact]
    public async Task ProductionGuidanceUrl_AgainstLiveSchema_ReturnsGuidanceForEveryActiveCategory()
    {
        var envUrl = Environment.GetEnvironmentVariable(UrlEnvVar);
        if (string.IsNullOrWhiteSpace(envUrl)) return;

        var authority = new Uri(envUrl.TrimEnd('/')).GetLeftPart(UriPartial.Authority);
        var token = await new DefaultAzureCredential().GetTokenAsync(
            new TokenRequestContext(new[] { $"{authority}/.default" }), CancellationToken.None);
        using var http = new HttpClient { BaseAddress = new Uri($"{authority}/api/data/v9.2/") };
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);

        // The very URLs production sends: the shared builders plus the resolver's per-taxonomy filter, so this
        // exercises the real sprk_enabled predicate (a missing column or option would be a 400 here).
        var filter = LookupChoicesResolver.AdditionalFilterFor("sprk_triagecategory");
        filter.Should().Be("sprk_enabled eq true");
        var namesUrl = ScopeResolverService.BuildLookupValuesUrl("sprk_triagecategories", "sprk_name", filter);
        var guidanceUrl = ScopeResolverService.BuildLookupGuidanceUrl(
            "sprk_triagecategories", "sprk_name", "sprk_classifierguidance", filter);

        var names = await RowsAsync(http, namesUrl, "sprk_name");
        var guidance = await RowsAsync(http, guidanceUrl, "sprk_name", "sprk_classifierguidance");

        names.Should().NotBeEmpty();
        guidance.Select(g => g.Name).Should().BeEquivalentTo(names.Select(n => n.Name),
            "every active category behind the enum has authored guidance (the ten rows)");
        guidance.Should().OnlyContain(g => !string.IsNullOrWhiteSpace(g.Guidance));
        guidance.Should().Contain(g => g.Name == "Fee / rate change" && g.Guidance!.Contains("Scope / budget change"),
            "the tie-breaker text that separates the two gated categories is what the prompt now carries");
    }

    private static async Task<List<(string Name, string? Guidance)>> RowsAsync(
        HttpClient http, string url, string nameField, string? guidanceField = null)
    {
        using var response = await http.GetAsync(url);
        response.IsSuccessStatusCode.Should().BeTrue($"the live schema must accept {url}");
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.GetProperty("value").EnumerateArray()
            .Select(r => (r.GetProperty(nameField).GetString()!,
                guidanceField is null ? null : r.GetProperty(guidanceField).GetString()))
            .ToList();
    }
}
