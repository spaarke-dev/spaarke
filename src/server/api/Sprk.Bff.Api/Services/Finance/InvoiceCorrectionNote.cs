namespace Sprk.Bff.Api.Services.Finance;

/// <summary>
/// The correction note (ebilling-r1 task 095f, EB-D-225): <c>sprk_invoice.sprk_correctionnote</c>, the one place a reader finds why an
/// invoice changed. Two writers share it: an Outlook (from-email) Correction, whose note is saved on the NEW invoice in the persister's
/// create, and the Invoice Review adjust and decision steps. This type owns the column name, the length and the text rule, so the writers
/// cannot drift apart (one server-side owner per invariant).
/// </summary>
/// <remarks>
/// <para><b>Length.</b> 2000 characters, the length of every reason a reviewer already types (reject, return, line decision, adjustment,
/// override), taken from <see cref="InvoiceLineDecisionService.ReasonMaxLength"/> so the limit has one source. The column (Memo, maximum length
/// 4000) is created by <c>scripts/ebilling/Deploy-EbillingInvoiceCorrectionNoteColumn.ps1</c>; this constant must not exceed the column's length.</para>
/// <para><b>Text rule.</b> Trimmed; empty after trimming means no note. Control characters other than newline, carriage return (the second
/// half of a Windows newline) and tab are refused, never stripped, and a note over the length is refused, never truncated.</para>
/// </remarks>
/// <remarks>
/// <para><b>Component justification (CLAUDE.md section 11).</b>
/// (1) Existing: the reasons of the line decision, adjustment, add-line and assign-firm services share <see cref="InvoiceLineDecisionService.ReasonMaxLength"/>
/// but each validates its own text and none writes <c>sprk_correctionnote</c>; nothing else owns that column.
/// (2) Extension: EB-D-225 names two writers (from-email and Invoice Review) for one column, so the column name and the text rule must live in
/// one neutral place; putting them on either writer's service would make the other depend on it.
/// (3) Cost of doing nothing: the two writers would each carry their own length and control-character rule and drift, so the same note would be
/// accepted by one writer and refused (or truncated) by the other.</para>
/// </remarks>
public static class InvoiceCorrectionNote
{
    /// <summary>The <c>sprk_invoice</c> column.</summary>
    public const string Column = "sprk_correctionnote";

    /// <summary>The most characters a note may have, after trimming: the one reason limit every reviewer text shares (2000).</summary>
    public const int MaxLength = InvoiceLineDecisionService.ReasonMaxLength;

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
