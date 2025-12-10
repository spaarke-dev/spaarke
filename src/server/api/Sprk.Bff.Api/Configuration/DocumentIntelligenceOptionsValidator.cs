using Microsoft.Extensions.Options;

namespace Sprk.Bff.Api.Configuration;

/// <summary>
/// Custom validator for DocumentIntelligenceOptions that only validates required fields when enabled.
/// This allows the application to start without Azure OpenAI configuration when DocumentIntelligence:Enabled=false.
/// </summary>
public class DocumentIntelligenceOptionsValidator : IValidateOptions<DocumentIntelligenceOptions>
{
    public ValidateOptionsResult Validate(string? name, DocumentIntelligenceOptions options)
    {
        // If Document Intelligence is disabled, skip all validation
        if (!options.Enabled)
        {
            return ValidateOptionsResult.Success;
        }

        var failures = new List<string>();

        // Validate required Azure OpenAI settings when enabled
        if (string.IsNullOrWhiteSpace(options.OpenAiEndpoint))
        {
            failures.Add("DocumentIntelligence:OpenAiEndpoint is required when DocumentIntelligence:Enabled=true");
        }

        if (string.IsNullOrWhiteSpace(options.OpenAiKey))
        {
            failures.Add("DocumentIntelligence:OpenAiKey is required when DocumentIntelligence:Enabled=true");
        }

        // Validate ranges (these still use DataAnnotations but good to double-check)
        if (options.MaxOutputTokens < 100 || options.MaxOutputTokens > 4000)
        {
            failures.Add("DocumentIntelligence:MaxOutputTokens must be between 100 and 4000");
        }

        if (options.Temperature < 0.0f || options.Temperature > 1.0f)
        {
            failures.Add("DocumentIntelligence:Temperature must be between 0.0 and 1.0");
        }

        if (options.MaxConcurrentStreams < 1 || options.MaxConcurrentStreams > 10)
        {
            failures.Add("DocumentIntelligence:MaxConcurrentStreams must be between 1 and 10");
        }

        return failures.Count > 0
            ? ValidateOptionsResult.Fail(failures)
            : ValidateOptionsResult.Success;
    }
}
