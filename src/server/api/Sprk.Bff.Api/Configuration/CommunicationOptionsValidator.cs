using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Sprk.Bff.Api.Configuration;

/// <summary>
/// Env-aware startup validator for the ONE <see cref="CommunicationOptions"/> member the system sender depends on,
/// <see cref="CommunicationOptions.DefaultMailbox"/> (owner decision 2026-10-11: <c>RegistrationEmailService</c>'s sender
/// is configuration, no longer the literal <c>demo@demo.spaarke.com</c>).
/// </summary>
/// <remarks>
/// <para><see cref="CommunicationOptions"/> as a whole is deliberately NOT validated at start (it carries four
/// <c>[Required]</c> webhook members that exist only where the Graph-webhook feature is provisioned; see
/// <c>CommunicationModule</c>), so this validator checks <c>DefaultMailbox</c> alone and nothing else. It runs when the
/// options are first read; <c>StartupValidationService</c> reads them at host start, so a deployed host with no system
/// sender refuses to start rather than failing on its first grant email.</para>
/// <para>Like <see cref="PublicConfigOptionsValidator"/>, it short-circuits in Development and Testing so the dozens of
/// per-endpoint test fixtures need no new key; every other environment (Production, Staging, Demo, QA) fails fast on an
/// empty value. Stamps already receive the setting (<c>Communication__DefaultMailbox</c>, manifest secret
/// <c>Communication-DefaultMailbox</c>, supplied from the intake).</para>
/// </remarks>
public sealed class CommunicationOptionsValidator : IValidateOptions<CommunicationOptions>
{
    private readonly IHostEnvironment _environment;

    public CommunicationOptionsValidator(IHostEnvironment environment)
    {
        _environment = environment;
    }

    public ValidateOptionsResult Validate(string? name, CommunicationOptions options)
    {
        var isLocalLike = _environment.IsDevelopment()
            || string.Equals(_environment.EnvironmentName, "Testing", StringComparison.OrdinalIgnoreCase);
        if (isLocalLike)
        {
            return ValidateOptionsResult.Success;
        }

        return string.IsNullOrWhiteSpace(options.DefaultMailbox)
            ? ValidateOptionsResult.Fail(
                "Communication:DefaultMailbox is required outside Development/Testing: it is the system sender mailbox for "
                + "registration and grant-notification email (Communication__DefaultMailbox; the Spaarke template value is "
                + "mailbox-central@spaarke.com). The mailbox must exist in the tenant and appear in Communication:ApprovedSenders.")
            : ValidateOptionsResult.Success;
    }
}
