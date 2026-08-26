using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Sprk.Bff.Api.Configuration;

/// <summary>
/// Env-aware startup validator for <see cref="PublicConfigOptions"/>
/// (customer-provisioning-orchestration-r1 task 087; mirrors the r3 task 061
/// custom-<see cref="IValidateOptions{TOptions}"/> pattern used by
/// <c>AgentServiceOptionsValidator</c>).
///
/// <para>
/// PublicConfig is Tier-1 fail-fast in deployed environments — a missing key
/// on Production / Staging is a real defect and MUST crash startup so the
/// operator sees it immediately, not on the first anonymous <c>/api/config</c>
/// call. The endpoint would otherwise emit empty strings for msalClientId or
/// tenantId, silently breaking every browser client's MSAL bootstrap.
/// </para>
///
/// <para>
/// In Development / Testing env the validator short-circuits to
/// <see cref="ValidateOptionsResult.Success"/> — the same policy as
/// <c>appsettings.Testing.json</c>'s CI Redis fallback (per PR #521 /
/// spaarke-redis-cache-remediation-r2 FR-06). This avoids drift across the
/// 30+ per-endpoint test fixtures that each maintain their own
/// <c>ConfigureHostConfiguration</c> dictionary — adding <c>PublicConfig:*</c>
/// entries to every one of them (per §F.2 fixture-config-first) would be a
/// mechanical sweep with high review cost and no safety benefit (the fail-fast
/// still catches every deployed-env misconfiguration).
/// </para>
///
/// <para>
/// If a specific fixture wants to exercise the config-bound path of
/// <c>GET /api/config</c>, it can still populate <c>PublicConfig:*</c> keys —
/// this validator only removes the boot-time hard requirement, it does not
/// change how the options are consumed.
/// </para>
/// </summary>
public sealed class PublicConfigOptionsValidator : IValidateOptions<PublicConfigOptions>
{
    private readonly IHostEnvironment _environment;

    public PublicConfigOptionsValidator(IHostEnvironment environment)
    {
        _environment = environment;
    }

    public ValidateOptionsResult Validate(string? name, PublicConfigOptions options)
    {
        // Env-aware short-circuit: mirror PR #521's Testing allow-list stance
        // (per .claude/constraints/bff-extensions.md §F.2.1). Local dev and
        // integration-test fixtures don't need to set PublicConfig:* for the
        // host to boot; the endpoint just returns empty strings if actually
        // hit without config. Deployed envs (Production / Staging / Demo / QA)
        // still fail-fast on missing config.
        var isLocalLike = _environment.IsDevelopment() ||
            string.Equals(_environment.EnvironmentName, "Testing", StringComparison.OrdinalIgnoreCase);

        if (isLocalLike)
        {
            return ValidateOptionsResult.Success;
        }

        var failures = new List<string>();

        if (string.IsNullOrWhiteSpace(options.BffUrl))
        {
            failures.Add("PublicConfig:BffUrl is required (Tier-1) in deployed environments");
        }

        if (string.IsNullOrWhiteSpace(options.MsalClientId))
        {
            failures.Add("PublicConfig:MsalClientId is required (Tier-1) in deployed environments");
        }

        if (string.IsNullOrWhiteSpace(options.TenantId))
        {
            failures.Add("PublicConfig:TenantId is required (Tier-1) in deployed environments");
        }

        return failures.Count > 0
            ? ValidateOptionsResult.Fail(failures)
            : ValidateOptionsResult.Success;
    }
}
