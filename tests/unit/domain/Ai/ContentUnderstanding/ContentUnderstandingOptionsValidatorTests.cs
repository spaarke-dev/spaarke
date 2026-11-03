using FluentAssertions;
using Sprk.Bff.Api.Configuration;
using Xunit;

namespace Sprk.Bff.Api.Tests.Domain.Ai.ContentUnderstanding;

/// <summary>
/// ebilling-r1 task 043: startup validation of the <c>ContentUnderstanding</c> section. An empty endpoint is valid
/// (reader not configured); everything that is set must be well formed.
/// </summary>
public sealed class ContentUnderstandingOptionsValidatorTests
{
    private static readonly ContentUnderstandingOptionsValidator Validator = new();

    private static ContentUnderstandingOptions Configured(Action<ContentUnderstandingOptions>? change = null)
    {
        var options = new ContentUnderstandingOptions { Endpoint = "https://cu.cognitiveservices.azure.com/" };
        change?.Invoke(options);
        return options;
    }

    [Fact]
    public void Defaults_AreThePreviewPrimaryOfD81()
    {
        var options = new ContentUnderstandingOptions();

        options.ApiVersion.Should().Be("2026-06-01-preview");
        options.AnalyzerId.Should().Be("sprk_invoice_v2p");
        options.IsConfigured.Should().BeFalse();
    }

    [Fact]
    public void Validate_NotConfigured_Succeeds()
    {
        Validator.Validate(null, new ContentUnderstandingOptions()).Succeeded.Should().BeTrue();
    }

    [Theory]
    [InlineData("2026-06-01-preview", "sprk_invoice_v2p")]
    [InlineData("2025-11-01", "sprk_invoice_v2")]
    public void Validate_PreviewAndGaPairs_Succeed(string apiVersion, string analyzerId)
    {
        Validator.Validate(null, Configured(o => { o.ApiVersion = apiVersion; o.AnalyzerId = analyzerId; }))
            .Succeeded.Should().BeTrue();
    }

    public static TheoryData<string, Action<ContentUnderstandingOptions>> Invalid() => new()
    {
        { "http endpoint", o => o.Endpoint = "http://cu.cognitiveservices.azure.com/" },
        { "relative endpoint", o => o.Endpoint = "cu.cognitiveservices.azure.com" },
        { "unreplaced token", o => o.Endpoint = "#{CONTENT_UNDERSTANDING_ENDPOINT}#" },
        { "api-version", o => o.ApiVersion = "2025-11" },
        { "api-version suffix", o => o.ApiVersion = "2025-11-01-beta" },
        { "analyzer hyphen", o => o.AnalyzerId = "sprk-invoice" },
        { "analyzer empty", o => o.AnalyzerId = "" },
        { "MaxPages 0", o => o.MaxPages = 0 },
        { "MaxPages over ceiling", o => o.MaxPages = 2001 },
        { "PageRangeSize 0", o => o.PageRangeSize = 0 },
        { "PageRangeSize over the service limit", o => o.PageRangeSize = 301 },
        { "timeout too short", o => o.OperationTimeoutSeconds = 29 },
        { "poll interval negative", o => o.PollIntervalMilliseconds = -1 },
        { "poll interval below the floor", o => o.PollIntervalMilliseconds = 249 },
        { "poll interval over 30 s", o => o.PollIntervalMilliseconds = 30_001 },
    };

    [Theory]
    [MemberData(nameof(Invalid))]
    public void Validate_MalformedValue_Fails(string why, Action<ContentUnderstandingOptions> change)
    {
        Validator.Validate(null, Configured(change)).Failed.Should().BeTrue(why);
    }

    [Fact]
    public void Validate_NotConfigured_StillChecksTheLimits()
    {
        Validator.Validate(null, new ContentUnderstandingOptions { PageRangeSize = 0 }).Failed.Should().BeTrue();
    }
}
