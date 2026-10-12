using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Sprk.Bff.Api.Configuration;

/// <summary>
/// Env-aware startup validator for the <see cref="CommunicationOptions"/> members the system sender depends on:
/// <see cref="CommunicationOptions.DefaultMailbox"/> must be set AND be one of <see cref="CommunicationOptions.ApprovedSenders"/> (owner decision 2026-10-11: <c>RegistrationEmailService</c>'s sender
/// is configuration, no longer the literal <c>demo@demo.spaarke.com</c>).
/// </summary>
/// <remarks>
/// <para><see cref="CommunicationOptions"/> as a whole is deliberately NOT validated at start (it carries four
/// <c>[Required]</c> webhook members that exist only where the Graph-webhook feature is provisioned; see
/// <c>CommunicationModule</c>), so this validator checks <c>DefaultMailbox</c> and its membership in <c>ApprovedSenders</c> and nothing else. It runs when the
/// options are first read; <c>StartupValidationService</c> reads them at host start, so a deployed host with no system
/// sender refuses to start rather than failing on its first grant email.</para>
/// <para>Like <see cref="PublicConfigOptionsValidator"/>, it short-circuits in Development and Testing so the dozens of
/// per-endpoint test fixtures need no new key; every other environment (Production, Staging, Demo, QA) fails fast on an
/// empty value or a default mailbox that is not an approved sender (the send path would reject it as InvalidSender). Stamps already receive the setting (<c>Communication__DefaultMailbox</c>, manifest secret
/// <c>Communication-DefaultMailbox</c>, supplied from the intake) and, since the F3 fix round, the matching
/// <c>Communication__ApprovedSenders__0__*</c> entry.</para>
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

        if (string.IsNullOrWhiteSpace(options.DefaultMailbox))
        {
            return ValidateOptionsResult.Fail(
                "Communication:DefaultMailbox is required outside Development/Testing: it is the system sender mailbox for "
                + "registration and grant-notification email (Communication__DefaultMailbox; the Spaarke template value is "
                + "mailbox-central@spaarke.com). The mailbox must exist in the tenant and appear in Communication:ApprovedSenders.");
        }

        // The send path (CommunicationService -> ApprovedSenderValidator.ResolveExplicit) rejects an explicit FromMailbox
        // that is not an approved sender with InvalidSender, and GrantAccessNotifier then logs and drops the email. So a
        // non-blank DefaultMailbox is not enough: it must also BE an approved sender.
        var defaultMailbox = options.DefaultMailbox.Trim();
        var isApproved = options.ApprovedSenders is { Length: > 0 }
            && options.ApprovedSenders.Any(s => s is not null
                && string.Equals(s.Email, defaultMailbox, StringComparison.OrdinalIgnoreCase));
        return isApproved
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(
                "Communication:DefaultMailbox must also be listed in Communication:ApprovedSenders outside Development/Testing: "
                + "no entry of Communication:ApprovedSenders has an Email equal to Communication:DefaultMailbox, so every "
                + "registration and grant-notification email would be rejected as InvalidSender and dropped. Set "
                + "Communication__ApprovedSenders__N__Email to the same mailbox as Communication__DefaultMailbox "
                + "(with Communication__ApprovedSenders__N__IsDefault=true).");
    }
}
