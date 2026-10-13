namespace Sprk.Bff.Api.Services.Ai.Metering;

/// <summary>
/// The optional monthly Azure OpenAI spend limit of this stamp (customer-provisioning-orchestration-r1 task 254, owner
/// G37: "no cap, but allow for a per customer spend limit if desired"). Bound from configuration section
/// <c>AiSpendLimit</c>.
/// </summary>
/// <remarks>
/// <para>
/// ONE limit per stamp, not per tenant: under D-12 a stamp serves one customer, so every OpenAI call this BFF makes is
/// that customer's spend — whoever signs in (the customer's workforce tenants, External Access users) and whatever runs
/// in the background. The retired shared tier keyed budgets by the caller's <c>tid</c>; on a dedicated stamp that key
/// matches no caller (see <c>projects/customer-provisioning-orchestration-r1/notes/t254-ai-spend-limit-decisions.md</c>).
/// </para>
/// <para>
/// Unset is the default and means no limit. Provisioning writes <c>AiSpendLimit__MonthlyLimitUsd</c> only when the
/// operator supplies one (intake <c>openAiMonthlyLimitUsd</c>); <c>scripts/Set-AiSpendLimit.ps1</c> adds, changes or
/// removes it later. On App Service the settings are environment variables, so a change applies on the restart App Service
/// performs for any app-setting change (environment variables are not reloaded); <see cref="AiSpendLimit"/> reads them
/// through <see cref="Microsoft.Extensions.Options.IOptionsMonitor{TOptions}"/> each call, so reloadable sources (local
/// files) apply at once.
/// </para>
/// </remarks>
public sealed class AiSpendLimitOptions
{
    /// <summary>Configuration section — <c>AiSpendLimit</c>.</summary>
    public const string SectionName = "AiSpendLimit";

    /// <summary>
    /// Monthly limit in USD for the whole stamp (UTC calendar month). Null, zero or negative = no limit.
    /// </summary>
    public decimal? MonthlyLimitUsd { get; set; }

    /// <summary>
    /// Estimated price of one million input (prompt) tokens, USD. Default 2.50 — the gpt-4o list rate task 077 chose,
    /// at or above the list rate of the chat models stamps deploy, so the estimate errs towards stopping early.
    /// </summary>
    public decimal InputUsdPer1MTokens { get; set; } = 2.50m;

    /// <summary>Estimated price of one million output (completion) tokens, USD. Default 10.00 (see <see cref="InputUsdPer1MTokens"/>).</summary>
    public decimal OutputUsdPer1MTokens { get; set; } = 10.00m;

    /// <summary>The configured limit, or null when there is none.</summary>
    public decimal? EffectiveLimitUsd => MonthlyLimitUsd is { } limit && limit > 0m ? limit : null;
}
