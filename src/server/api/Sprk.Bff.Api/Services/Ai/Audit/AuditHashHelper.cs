using System.Security.Cryptography;
using System.Text;

namespace Sprk.Bff.Api.Services.Ai.Audit;

/// <summary>
/// Provides deterministic SHA-256 hashing for AI response content.
///
/// ADR-015 Tier 2 compliance: verbatim AI responses must never be stored in the audit log.
/// Only the SHA-256 hex digest is persisted, enabling tamper detection without retaining content.
/// </summary>
public static class AuditHashHelper
{
    /// <summary>
    /// Computes the SHA-256 hex digest of an AI response string.
    /// The result is a 64-character lowercase hex string.
    /// </summary>
    /// <param name="responseText">The raw AI response text. Must not be null.</param>
    /// <returns>Lowercase hex-encoded SHA-256 hash of the UTF-8 encoded input.</returns>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="responseText"/> is null.</exception>
    public static string HashResponse(string responseText)
    {
        ArgumentNullException.ThrowIfNull(responseText);

        var bytes = Encoding.UTF8.GetBytes(responseText);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
