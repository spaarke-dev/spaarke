namespace Sprk.Bff.Api.Services.Finance;

/// <summary>
/// The correction note (ebilling-r1 task 095f, EB-D-225): <c>sprk_invoice.sprk_correctionnote</c>, the one place a reader finds why an
/// invoice changed. Two writers share it: an Outlook (from-email) Correction, whose note is saved on the NEW invoice in the persister's
/// create, and the Invoice Review adjust and decision steps. This type owns the column name, the length and the text rule, so the writers
/// cannot drift apart (one server-side owner per invariant).
/// </summary>
/// <remarks>
/// <para><b>Length.</b> 2000 characters, the length of every reason a reviewer already types (reject, return, line decision, adjustment,
/// override). The column itself is not in solution source yet (the owner added it in dev); the deploy task's export confirms its maximum
/// length, and this constant must not exceed it.</para>
/// <para><b>Text rule.</b> Trimmed; empty after trimming means no note. Control characters other than newline, carriage return (the second
/// half of a Windows newline) and tab are refused, never stripped, and a note over the length is refused, never truncated.</para>
/// </remarks>
public static class InvoiceCorrectionNote
{
    /// <summary>The <c>sprk_invoice</c> column.</summary>
    public const string Column = "sprk_correctionnote";

    /// <summary>The most characters a note may have, after trimming.</summary>
    public const int MaxLength = 2000;

    /// <summary>Why a note was refused (the checks run in this order).</summary>
    public enum Refusal
    {
        /// <summary>The note is acceptable (or empty: see <see cref="Checked.Value"/>).</summary>
        None,

        /// <summary>Longer than <see cref="MaxLength"/> after trimming.</summary>
        TooLong,

        /// <summary>Holds a control character other than newline, carriage return or tab.</summary>
        ControlCharacter,
    }

    /// <summary>The result of <see cref="Check"/>: the trimmed note (null when empty or refused) or the reason it was refused.</summary>
    public readonly record struct Checked(string? Value, Refusal Refusal);

    /// <summary>Trims <paramref name="raw"/> and checks it. A null, empty or whitespace-only note is <c>Value</c> null with no refusal;
    /// whether a note is required is the caller's rule.</summary>
    public static Checked Check(string? raw)
    {
        var trimmed = raw?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return new Checked(null, Refusal.None);
        }

        if (trimmed.Length > MaxLength)
        {
            return new Checked(null, Refusal.TooLong);
        }

        return trimmed.Any(c => char.IsControl(c) && c is not ('\n' or '\r' or '\t'))
            ? new Checked(null, Refusal.ControlCharacter)
            : new Checked(trimmed, Refusal.None);
    }
}
