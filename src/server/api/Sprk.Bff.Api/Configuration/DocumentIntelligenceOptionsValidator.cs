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

        // auth-v4 task 054 (FR-E5): OpenAiKey is NO LONGER REQUIRED. OpenAiClient now falls forward
        // to managed identity when it is absent, so demanding it here would have blocked the very
        // migration this project exists to perform - startup would fail before the Entra path could
        // ever run. The endpoint stays required because nothing can infer it.
        //
        // The key is still the working choice for Azure OpenAI today (ADR-028 exception E-2,
        // re-affirmed 2026-08-21 by task 052); this only stops the VALIDATOR from making it
        // mandatory.

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

        // Validate Azure AI Search settings when record matching is enabled
        if (options.RecordMatchingEnabled)
        {
            if (string.IsNullOrWhiteSpace(options.AiSearchEndpoint))
            {
                failures.Add("DocumentIntelligence:AiSearchEndpoint is required when DocumentIntelligence:RecordMatchingEnabled=true");
            }

            // auth-v4 task 054 (FR-E5): AiSearchKey is NO LONGER REQUIRED. Task 053 migrated every
            // AI Search consumer onto SearchClientFactory, where an absent key selects Entra. Leaving
            // this check would have re-created 053's own defect one layer up: 053 fixed the DI block
            // that silently un-registered six services when the key went, but clearing the key would
            // STILL have failed startup here, at options validation, with a message pointing at the
            // key rather than at the migration. Endpoint + index name stay required.

            if (string.IsNullOrWhiteSpace(options.AiSearchIndexName))
            {
                failures.Add("DocumentIntelligence:AiSearchIndexName is required when DocumentIntelligence:RecordMatchingEnabled=true");
            }
        }

        return failures.Count > 0
            ? ValidateOptionsResult.Fail(failures)
            : ValidateOptionsResult.Success;
    }
}
