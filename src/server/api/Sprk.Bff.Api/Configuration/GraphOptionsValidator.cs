using Microsoft.Extensions.Options;

namespace Sprk.Bff.Api.Configuration;

/// <summary>
/// Validates GraphOptions with conditional requirements based on authentication mode.
/// </summary>
public class GraphOptionsValidator : IValidateOptions<GraphOptions>
{
    public ValidateOptionsResult Validate(string? name, GraphOptions options)
    {
        var errors = new List<string>();

        // If ManagedIdentity is enabled, ClientId is required
        if (options.ManagedIdentity.Enabled && string.IsNullOrWhiteSpace(options.ManagedIdentity.ClientId))
        {
            errors.Add("Graph:ManagedIdentity:ClientId is required when ManagedIdentity is enabled");
        }

        // If ManagedIdentity is disabled, ClientSecret is required
        if (!options.ManagedIdentity.Enabled && string.IsNullOrWhiteSpace(options.ClientSecret))
        {
            errors.Add("Graph:ClientSecret is required when ManagedIdentity is disabled (local development mode)");
        }

        return errors.Any()
            ? ValidateOptionsResult.Fail(errors)
            : ValidateOptionsResult.Success;
    }
}
