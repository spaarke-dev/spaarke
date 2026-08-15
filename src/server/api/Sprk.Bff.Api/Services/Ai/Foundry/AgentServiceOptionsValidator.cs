using Microsoft.Extensions.Options;

namespace Sprk.Bff.Api.Services.Ai.Foundry;

/// <summary>
/// Cross-property (Enabled→Endpoint) startup validator for <see cref="AgentServiceOptions"/>
/// (task 061 — uniform fail-fast configuration validation).
///
/// <para>
/// <see cref="AgentServiceOptions"/> is a kill-switch-gated option (ADR-018): the app MUST boot
/// with <c>AgentService:Enabled=false</c> and no Foundry configuration present. Bare
/// <c>[Required]</c> on <see cref="AgentServiceOptions.Endpoint"/>/<see cref="AgentServiceOptions.AgentId"/>
/// is therefore forbidden — it would evaluate eagerly and crash the Enabled=false boot path
/// (the 2026-06-09 BingGrounding incident class). This validator expresses the
/// "required-WHEN-enabled" invariant safely: it returns <see cref="ValidateOptionsResult.Success"/>
/// whenever the feature is off, so a disabled BFF starts cleanly; when the feature is ON it
/// fails AT STARTUP (via <c>ValidateOnStart()</c>) naming every missing key, rather than
/// deferring the failure to the first agent call.
/// </para>
/// </summary>
public sealed class AgentServiceOptionsValidator : IValidateOptions<AgentServiceOptions>
{
    public ValidateOptionsResult Validate(string? name, AgentServiceOptions options)
    {
        // Kill switch off → the feature's config is not required. Boot cleanly (ADR-018).
        if (!options.Enabled)
        {
            return ValidateOptionsResult.Success;
        }

        var failures = new List<string>();

        if (options.Endpoint is null)
        {
            failures.Add("AgentService:Endpoint is required when AgentService:Enabled=true");
        }

        if (string.IsNullOrWhiteSpace(options.AgentId))
        {
            failures.Add("AgentService:AgentId is required when AgentService:Enabled=true");
        }

        return failures.Count > 0
            ? ValidateOptionsResult.Fail(failures)
            : ValidateOptionsResult.Success;
    }
}
