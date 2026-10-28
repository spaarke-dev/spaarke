// The invite stamp's email hash: the salted SHA-256 of the address an invite was sent to, stored on the contact
// (contact.sprk_externalinvitedemailhash) so an email bind can require that the signing-in address, and the contact's current
// emailaddress1, are the address this environment invited. The address itself is never stored by this type.
//
// Pure and static: no I/O, no key, no logging. Same shape as InviteCode (salt per stamp, fixed-time compare).

using System.Security.Cryptography;
using System.Text;

namespace Sprk.Bff.Api.Infrastructure.ExternalAccess;

/// <summary>
/// Creates the stored value of an invite's email hash and checks an address against it.
/// </summary>
/// <remarks>
/// <para><b>The stored value</b> is <c>v1:</c> + base64(salt) + <c>:</c> + base64(digest), where the salt is
/// <see cref="SaltBytes"/> bytes from <see cref="RandomNumberGenerator"/>, new for every stamp, and the digest is SHA-256 over
/// the salt followed by the UTF-8 bytes of the normalised address.</para>
/// <para><b>Normalisation</b> is <see cref="string.Trim()"/> then <see cref="string.ToLowerInvariant"/>, and nothing else: no
/// plus-address or dot stripping, so two addresses a mail system may treat as one are still different addresses here.</para>
/// <para><b>The comparison</b> is <see cref="CryptographicOperations.FixedTimeEquals"/> over the two digests. A stored value
/// with another version prefix, or that does not parse, is no stamp: it matches nothing.</para>
/// </remarks>
internal static class InviteEmailStamp
{
    /// <summary>The salt length: 16 bytes, new for every stamp.</summary>
    public const int SaltBytes = 16;

    private const int DigestBytes = 32;
    private const string VersionPrefix = "v1:";

    /// <summary>The value to store for an invite sent to <paramref name="email"/>.</summary>
    public static string Create(string email)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(email);
        var salt = RandomNumberGenerator.GetBytes(SaltBytes);
        var digest = Digest(salt, email);
        return VersionPrefix + Convert.ToBase64String(salt) + ":" + Convert.ToBase64String(digest);
    }

    /// <summary>True when <paramref name="storedValue"/> is a value this version writes (it parses).</summary>
    public static bool IsWellFormed(string? storedValue) => TryParse(storedValue, out _, out _);

    /// <summary>
    /// True only when <paramref name="email"/>, normalised, is the address <paramref name="storedValue"/> was created for. A
    /// blank address, or a stored value that is absent, malformed or of another version, is false. Never throws.
    /// </summary>
    public static bool Matches(string? storedValue, string? email)
    {
        if (string.IsNullOrWhiteSpace(email) || !TryParse(storedValue, out var salt, out var expected))
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(Digest(salt, email), expected);
    }

    private static byte[] Digest(byte[] salt, string email)
    {
        var address = Encoding.UTF8.GetBytes(email.Trim().ToLowerInvariant());
        var input = new byte[salt.Length + address.Length];
        salt.CopyTo(input, 0);
        address.CopyTo(input, salt.Length);
        return SHA256.HashData(input);
    }

    private static bool TryParse(string? storedValue, out byte[] salt, out byte[] digest)
    {
        salt = Array.Empty<byte>();
        digest = Array.Empty<byte>();
        if (storedValue is null || !storedValue.StartsWith(VersionPrefix, StringComparison.Ordinal))
        {
            return false;
        }

        var parts = storedValue[VersionPrefix.Length..].Split(':');
        if (parts.Length != 2)
        {
            return false;
        }

        try
        {
            salt = Convert.FromBase64String(parts[0]);
            digest = Convert.FromBase64String(parts[1]);
        }
        catch (FormatException)
        {
            return false;
        }

        return salt.Length >= SaltBytes && digest.Length == DigestBytes;
    }
}
