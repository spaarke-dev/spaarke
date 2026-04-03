using System.Security.Cryptography;

namespace Sprk.Bff.Api.Services.Registration;

/// <summary>
/// Generates cryptographically secure temporary passwords for demo user accounts.
/// Uses System.Security.Cryptography.RandomNumberGenerator for secure randomness.
/// Registered as concrete type per ADR-010.
/// </summary>
public sealed class PasswordGenerator
{
    private const int DefaultLength = 16;
    private const string UppercaseChars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";
    private const string LowercaseChars = "abcdefghijklmnopqrstuvwxyz";
    private const string DigitChars = "0123456789";
    private const string SymbolChars = "!@#$%^&*()-_=+[]{}|;:,.<>?";

    private static readonly string AllChars =
        UppercaseChars + LowercaseChars + DigitChars + SymbolChars;

    /// <summary>
    /// Generates a cryptographically secure random password.
    /// Guarantees at least one character from each required category:
    /// uppercase, lowercase, digit, and symbol.
    /// </summary>
    /// <param name="length">Password length (minimum 16, default 16).</param>
    /// <returns>A secure random password string.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Length is less than 16.</exception>
    public string Generate(int length = DefaultLength)
    {
        if (length < DefaultLength)
            throw new ArgumentOutOfRangeException(
                nameof(length),
                length,
                $"Password length must be at least {DefaultLength} characters.");

        // Guarantee at least one character from each required category
        var password = new char[length];
        var position = 0;

        password[position++] = PickRandom(UppercaseChars);
        password[position++] = PickRandom(LowercaseChars);
        password[position++] = PickRandom(DigitChars);
        password[position++] = PickRandom(SymbolChars);

        // Fill remaining positions from the full character set
        for (; position < length; position++)
        {
            password[position] = PickRandom(AllChars);
        }

        // Shuffle the array to avoid predictable positions for required characters
        Shuffle(password);

        return new string(password);
    }

    /// <summary>
    /// Picks a cryptographically random character from the given character set.
    /// Uses rejection sampling to avoid modulo bias.
    /// </summary>
    private static char PickRandom(string chars)
    {
        // RandomNumberGenerator.GetInt32 handles rejection sampling internally
        var index = RandomNumberGenerator.GetInt32(chars.Length);
        return chars[index];
    }

    /// <summary>
    /// Fisher-Yates shuffle using cryptographic randomness.
    /// </summary>
    private static void Shuffle(char[] array)
    {
        for (var i = array.Length - 1; i > 0; i--)
        {
            var j = RandomNumberGenerator.GetInt32(i + 1);
            (array[i], array[j]) = (array[j], array[i]);
        }
    }
}
