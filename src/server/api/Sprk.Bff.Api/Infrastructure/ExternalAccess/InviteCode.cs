// unified-access-control-r2 task 185 (owner round 98) — the invite code that proves a CIAM sign-in came from THIS
// environment's own invite email (closes provisioning's F1, account squatting). Design:
// projects/unified-access-control-r2/notes/185-invite-code-design.md §1, §2.1 and §5.1.
//
// Pure and static: no I/O, no key, no logging. The code itself is never stored (only StoredValue) and never logged; this
// type never writes either anywhere.

using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;

namespace Sprk.Bff.Api.Infrastructure.ExternalAccess;

/// <summary>
/// Creates an invite code and checks a presented one against the contact's stored value
/// (<c>contact.sprk_externalinvitecodehash</c>).
/// </summary>
/// <remarks>
/// <para><b>The code</b> is 32 bytes (256 bits) from the OS CSPRNG, base64url without padding: exactly
/// <see cref="CodeLength"/> characters from <c>[A-Za-z0-9_-]</c>, URL-safe without escaping.</para>
/// <para><b>The stored value</b> (design §2.1) is <c>v1.</c> + base64url(salt) + <c>.</c> + base64url(digest), where the
/// salt is 16 random bytes per invite and the digest is SHA-256 over the domain-separation prefix
/// <c>spaarke.invite-code.v1\n</c>, the contact id (<c>"D"</c> format) and <c>\n</c>, the salt, and the decoded code. The
/// contact id is in the input, so a stored value copied onto another contact never verifies. A fast hash is correct: a
/// 256-bit random code has no low entropy for a slow hash to protect.</para>
/// <para><b>The comparison</b> is <see cref="CryptographicOperations.FixedTimeEquals"/> over the two 32-byte digests
/// (pinned by the ArchTests guard <c>InviteCodeFixedTimeCompareGuardTests</c>). Parsing the stored value works only on
/// public framing (version and lengths), so its timing says nothing about the code.</para>
/// </remarks>
internal static class InviteCode
{
    /// <summary>The length of a code: 32 bytes as unpadded base64url.</summary>
    public const int CodeLength = 43;

    private const int CodeBytes = 32;
    private const int SaltBytes = 16;
    private const int DigestBytes = 32;
    private const string VersionPrefix = "v1.";
    private const int SaltChars = 22;    // 16 bytes, unpadded base64url
    private const int DigestChars = 43;  // 32 bytes, unpadded base64url

    /// <summary>The stored value's length: <c>v1.</c> + 22 + <c>.</c> + 43.</summary>
    public const int StoredValueLength = 3 + SaltChars + 1 + DigestChars;

    private static readonly byte[] DomainPrefix = Encoding.UTF8.GetBytes("spaarke.invite-code.v1\n");

    /// <summary>
    /// A new code for <paramref name="contactId"/> and the value to store on that contact. The code goes only into the
    /// invite email's link; it is never stored or logged.
    /// </summary>
    public static (string Code, string StoredValue) Create(Guid contactId)
    {
        if (contactId == Guid.Empty)
        {
            throw new ArgumentException("An invite code belongs to one contact.", nameof(contactId));
        }

        Span<byte> code = stackalloc byte[CodeBytes];
        Span<byte> salt = stackalloc byte[SaltBytes];
        Span<byte> digest = stackalloc byte[DigestBytes];
        RandomNumberGenerator.Fill(code);
        RandomNumberGenerator.Fill(salt);
        ComputeDigest(contactId, salt, code, digest);

        var stored = VersionPrefix + Base64Url.EncodeToString(salt) + "." + Base64Url.EncodeToString(digest);
        var text = Base64Url.EncodeToString(code);
        CryptographicOperations.ZeroMemory(code);
        return (text, stored);
    }

    /// <summary>True when <paramref name="code"/> is exactly <see cref="CodeLength"/> base64url characters.</summary>
    public static bool IsWellFormed(string? code)
    {
        if (code is null || code.Length != CodeLength)
        {
            return false;
        }

        foreach (var c in code)
        {
            if (!IsBase64UrlChar(c))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// True only when <paramref name="presentedCode"/> is the code <paramref name="storedValue"/> was created from, for
    /// <paramref name="contactId"/>. A malformed or absent stored value or code, an unknown version, or an empty contact id
    /// is false. Never throws.
    /// </summary>
    public static bool Matches(string? storedValue, string? presentedCode, Guid contactId)
    {
        if (contactId == Guid.Empty || !IsWellFormed(presentedCode))
        {
            return false;
        }

        Span<byte> salt = stackalloc byte[SaltBytes];
        Span<byte> expected = stackalloc byte[DigestBytes];
        if (!TryParseStoredValue(storedValue, salt, expected))
        {
            return false;
        }

        Span<byte> code = stackalloc byte[CodeBytes];
        if (!TryDecodeExact(presentedCode.AsSpan(), code))
        {
            return false;
        }

        Span<byte> actual = stackalloc byte[DigestBytes];
        ComputeDigest(contactId, salt, code, actual);
        CryptographicOperations.ZeroMemory(code);
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }

    private static bool TryParseStoredValue(string? storedValue, Span<byte> salt, Span<byte> digest)
    {
        if (storedValue is null
            || storedValue.Length != StoredValueLength
            || !storedValue.StartsWith(VersionPrefix, StringComparison.Ordinal)
            || storedValue[VersionPrefix.Length + SaltChars] != '.')
        {
            return false;
        }

        var saltText = storedValue.AsSpan(VersionPrefix.Length, SaltChars);
        var digestText = storedValue.AsSpan(VersionPrefix.Length + SaltChars + 1, DigestChars);
        return AllBase64Url(saltText) && AllBase64Url(digestText)
            && TryDecodeExact(saltText, salt) && TryDecodeExact(digestText, digest);
    }

    /// <summary>Decodes <paramref name="text"/> (already checked to be base64url characters) into exactly <paramref name="into"/>.</summary>
    private static bool TryDecodeExact(ReadOnlySpan<char> text, Span<byte> into)
    {
        try
        {
            return Base64Url.DecodeFromChars(text, into, out var consumed, out var written) == System.Buffers.OperationStatus.Done
                && consumed == text.Length
                && written == into.Length;
        }
        catch (Exception ex) when (ex is FormatException or ArgumentException)
        {
            return false;
        }
    }

    private static void ComputeDigest(Guid contactId, ReadOnlySpan<byte> salt, ReadOnlySpan<byte> code, Span<byte> digest)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(DomainPrefix);
        hash.AppendData(Encoding.UTF8.GetBytes(contactId.ToString("D") + "\n"));
        hash.AppendData(salt);
        hash.AppendData(code);
        if (!hash.TryGetHashAndReset(digest, out var written) || written != DigestBytes)
        {
            throw new CryptographicException("SHA-256 did not produce 32 bytes.");
        }
    }

    private static bool AllBase64Url(ReadOnlySpan<char> text)
    {
        foreach (var c in text)
        {
            if (!IsBase64UrlChar(c))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsBase64UrlChar(char c)
        => c is (>= 'A' and <= 'Z') or (>= 'a' and <= 'z') or (>= '0' and <= '9') or '-' or '_';
}
