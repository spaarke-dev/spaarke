using Microsoft.Extensions.Options;

namespace Sprk.Bff.Api.Configuration;

/// <summary>
/// Custom validator for AiOptions that only validates required fields when AI is enabled.
/// This allows the application to start without Azure OpenAI configuration when Ai:Enabled=false.
/// </summary>
public class AiOptionsValidator : IValidateOptions<AiOptions>
{
    public ValidateOptionsResult Validate(string? name, AiOptions options)
    {
        // If AI is disabled, skip all validation
        if (!options.Enabled)
        {
            return ValidateOptionsResult.Success;
        }

        var failures = new List<string>();

        // Validate required Azure OpenAI settings when enabled
        if (string.IsNullOrWhiteSpace(options.OpenAiEndpoint))
        {
            failures.Add("Ai:OpenAiEndpoint is required when Ai:Enabled=true");
        }

        if (string.IsNullOrWhiteSpace(options.OpenAiKey))
        {
            failures.Add("Ai:OpenAiKey is required when Ai:Enabled=true");
        }

        // Validate ranges (these still use DataAnnotations but good to double-check)
        if (options.MaxOutputTokens < 100 || options.MaxOutputTokens > 4000)
        {
            failures.Add("Ai:MaxOutputTokens must be between 100 and 4000");
        }

        if (options.Temperature < 0.0f || options.Temperature > 1.0f)
        {
            failures.Add("Ai:Temperature must be between 0.0 and 1.0");
        }

        if (options.MaxConcurrentStreams < 1 || options.MaxConcurrentStreams > 10)
        {
            failures.Add("Ai:MaxConcurrentStreams must be between 1 and 10");
        }

        return failures.Count > 0
            ? ValidateOptionsResult.Fail(failures)
            : ValidateOptionsResult.Success;
    }
}
