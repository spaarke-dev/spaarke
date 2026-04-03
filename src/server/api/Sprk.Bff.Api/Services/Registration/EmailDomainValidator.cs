namespace Sprk.Bff.Api.Services.Registration;

/// <summary>
/// Validates email domains against a blocklist of known disposable email providers.
/// Used by the demo request submission endpoint to prevent abuse.
/// ADR-010: Registered as concrete type (no interface).
/// </summary>
public class EmailDomainValidator
{
    /// <summary>
    /// Known disposable/temporary email provider domains.
    /// Checked during demo request submission to block throwaway addresses.
    /// </summary>
    private static readonly HashSet<string> DisposableDomains = new(StringComparer.OrdinalIgnoreCase)
    {
        // Popular disposable email services
        "mailinator.com",
        "guerrillamail.com",
        "guerrillamail.net",
        "guerrillamail.org",
        "guerrillamail.de",
        "grr.la",
        "guerrillamailblock.com",
        "tempmail.com",
        "temp-mail.org",
        "temp-mail.io",
        "throwaway.email",
        "throwaway.com",
        "yopmail.com",
        "yopmail.fr",
        "yopmail.net",
        "sharklasers.com",
        "guerrillamail.info",
        "dispostable.com",
        "maildrop.cc",
        "mailnesia.com",
        "mailcatch.com",
        "trashmail.com",
        "trashmail.me",
        "trashmail.net",
        "fakeinbox.com",
        "tempail.com",
        "tempr.email",
        "discard.email",
        "discardmail.com",
        "discardmail.de",
        "10minutemail.com",
        "10minutemail.net",
        "minutemail.com",
        "getairmail.com",
        "mohmal.com",
        "burnermail.io",
        "mailsac.com",
        "mytemp.email",
        "emailondeck.com",
        "tempinbox.com",
        "getnada.com",
        "nada.email",
        "inboxbear.com",
        "spamgourmet.com",
        "mailnull.com",
        "mailexpire.com",
        "tmpmail.org",
        "tmpmail.net",
        "harakirimail.com",
        "mailforspam.com",
        "safetymail.info",
        "trashymail.com",
        "wegwerfmail.de",
        "wegwerfmail.net",
        "binkmail.com",
        "spamavert.com",
        "filzmail.com",
        "mailmoat.com",
        "incognitomail.org",
        "jetable.org",
        "trash-mail.com",
    };

    /// <summary>
    /// Checks whether the email address belongs to a known disposable email provider.
    /// </summary>
    /// <param name="email">The email address to check.</param>
    /// <returns>True if the email domain is a known disposable provider; otherwise false.</returns>
    public bool IsDisposableDomain(string email)
    {
        if (string.IsNullOrWhiteSpace(email))
            return false;

        var atIndex = email.LastIndexOf('@');
        if (atIndex < 0 || atIndex >= email.Length - 1)
            return false;

        var domain = email[(atIndex + 1)..].Trim().ToLowerInvariant();
        return DisposableDomains.Contains(domain);
    }
}
