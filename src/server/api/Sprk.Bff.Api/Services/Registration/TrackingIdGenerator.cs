using System.Security.Cryptography;

namespace Sprk.Bff.Api.Services.Registration;

/// <summary>
/// Generates unique, human-readable tracking IDs for registration requests.
/// Format: REG-{YYYYMMDD}-{4 alphanumeric chars} (e.g., REG-20260403-A7K2).
/// Thread-safe: uses RandomNumberGenerator (no shared mutable state).
/// </summary>
public class TrackingIdGenerator
{
    // Uppercase letters + digits, excluding ambiguous chars (0/O, 1/I/L) for readability
    private const string AlphanumericChars = "ABCDEFGHJKMNPQRSTUVWXYZ23456789";

    /// <summary>
    /// Generates a new tracking ID with today's date.
    /// </summary>
    public string Generate()
    {
        return Generate(DateTimeOffset.UtcNow);
    }

    /// <summary>
    /// Generates a new tracking ID with the specified date.
    /// Exposed for testability.
    /// </summary>
    public string Generate(DateTimeOffset date)
    {
        var datePart = date.ToString("yyyyMMdd");
        var randomPart = GenerateRandomAlphanumeric(4);
        return $"REG-{datePart}-{randomPart}";
    }

    private static string GenerateRandomAlphanumeric(int length)
    {
        Span<char> result = stackalloc char[length];
        Span<byte> randomBytes = stackalloc byte[length];
        RandomNumberGenerator.Fill(randomBytes);

        for (var i = 0; i < length; i++)
        {
            result[i] = AlphanumericChars[randomBytes[i] % AlphanumericChars.Length];
        }

        return new string(result);
    }
}
