using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;

namespace Sprk.Bff.Api.Configuration;

/// <summary>
/// Startup validation of <see cref="ContentUnderstandingOptions"/> (ebilling-r1 task 043). An empty endpoint is
/// valid (reader not configured); a set endpoint must be an absolute https URL, and the api-version and analyzer
/// id must be well formed. The numeric limits are checked in every case. Registered with ValidateOnStart, so a
/// malformed value fails host startup instead of the first invoice read.
/// </summary>
public sealed partial class ContentUnderstandingOptionsValidator : IValidateOptions<ContentUnderstandingOptions>
{
    public ValidateOptionsResult Validate(string? name, ContentUnderstandingOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var failures = new List<string>();

        if (options.IsConfigured)
        {
            if (!Uri.TryCreate(options.Endpoint!.Trim(), UriKind.Absolute, out var endpoint)
                || endpoint.Scheme != Uri.UriSchemeHttps)
            {
                failures.Add($"{ContentUnderstandingOptions.SectionName}:Endpoint must be an absolute https URL.");
            }

            if (string.IsNullOrWhiteSpace(options.ApiVersion) || !ApiVersionPattern().IsMatch(options.ApiVersion))
            {
                failures.Add(
                    $"{ContentUnderstandingOptions.SectionName}:ApiVersion must be a GA date (yyyy-MM-dd) or a date followed by -preview.");
            }

            if (string.IsNullOrWhiteSpace(options.AnalyzerId) || !AnalyzerIdPattern().IsMatch(options.AnalyzerId))
            {
                failures.Add(
                    $"{ContentUnderstandingOptions.SectionName}:AnalyzerId must be 1-64 letters, digits, '.' or '_' (the service rejects '-').");
            }
        }

        if (options.MaxPages is < 1 or > ContentUnderstandingOptions.MaxPagesCeiling)
        {
            failures.Add($"{ContentUnderstandingOptions.SectionName}:MaxPages must be within 1..{ContentUnderstandingOptions.MaxPagesCeiling}.");
        }

        if (options.PageRangeSize is < 1 or > ContentUnderstandingOptions.PageRangeSizeCeiling)
        {
            failures.Add($"{ContentUnderstandingOptions.SectionName}:PageRangeSize must be within 1..{ContentUnderstandingOptions.PageRangeSizeCeiling}.");
        }

        if (options.OperationTimeoutSeconds is < 30 or > 3600)
        {
            failures.Add($"{ContentUnderstandingOptions.SectionName}:OperationTimeoutSeconds must be within 30..3600.");
        }

        if (options.PollIntervalMilliseconds is < 250 or > 30_000)
        {
            failures.Add($"{ContentUnderstandingOptions.SectionName}:PollIntervalMilliseconds must be within 250..30000.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }

    [GeneratedRegex(@"^\d{4}-\d{2}-\d{2}(-preview)?$", RegexOptions.CultureInvariant)]
    private static partial Regex ApiVersionPattern();

    [GeneratedRegex(@"^[A-Za-z0-9._]{1,64}$", RegexOptions.CultureInvariant)]
    private static partial Regex AnalyzerIdPattern();
}
