namespace Sprk.Bff.Api.Services.Finance;

/// <summary>
/// The invoice notes (ebilling-r1 task 095f, EB-D-225; task 095f-2a, EB-D-236, EB-D-240). <c>sprk_invoice.sprk_correctionnote</c> is the firm's
/// own "why corrected" text, written only by the submission persister in a Correction's create (never by a decision). <c>sprk_invoice.sprk_decisionnote</c>
/// (<see cref="DecisionColumn"/>) is the INTERNAL note of the latest invoice-level decision, never shown to a firm; it is written only in the same
/// conditional update as the decision's own invoice change. This type owns both column names, the length and the text rule, so the writers
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
/// but each validates its own text. The only writer of <c>sprk_correctionnote</c> is the submission persister (guarded by
/// <c>InvoiceDecisionNoteWritersGuardTests</c>); the decision note's writers are the lifecycle transition (095f-2a), and later the override release
/// (095f-2c) and the totals path (095f-2b), listed in that same guard.
/// (2) Extension: the decision routes and the submission route check notes by one rule, and EB-D-240 adds a second column with the same text
/// rule, so the column names, the rule and the decision routes' shared 400 codes are extended here rather than copied into each service.
/// (3) Cost of doing nothing: each writer would carry its own length and control-character rule and drift, so the same note would be accepted
/// by one route and refused (or truncated) by another.</para>
/// </remarks>
public static class InvoiceCorrectionNote
{
    /// <summary>The <c>sprk_invoice</c> column the firm's Correction note is saved in (the submission persister only).</summary>
    public const string Column = "sprk_correctionnote";

    /// <summary>
    /// Task 095f-2a (EB-D-240): the <c>sprk_invoice</c> column holding the internal note of the latest invoice-level decision. It takes the
    /// decision's note, or for a decision with a required reason and no note, that reason; an approve with no note leaves it unchanged. Never
    /// added to a firm-facing reader or allow-list. Added by <c>scripts/ebilling/Deploy-EbillingDecisionNoteColumns.ps1</c> (Memo 2000).
    /// </summary>
    public const string DecisionColumn = "sprk_decisionnote";

    /// <summary>Task 095f-2a (design 1.1): the decision routes' 400 for a note over <see cref="MaxLength"/>.</summary>
    public const string NoteTooLongCode = "ebilling.note_too_long";

    /// <summary>Task 095f-2a (design 1.1): the decision routes' 400 for a note holding a refused control character.</summary>
    public const string NoteInvalidCharactersCode = "ebilling.note_invalid_characters";

    /// <summary>
    /// The coded refusal a DECISION route answers for <paramref name="refusal"/> (design 1.1: coded 400s, not field errors); null for
    /// <see cref="Refusal.None"/>. The detail never echoes the note.
    /// </summary>
    public static (string Code, string Detail)? DecisionRefusalOf(Refusal refusal) => refusal switch
    {
        Refusal.TooLong => (NoteTooLongCode, $"The note may be at most {MaxLength} characters."),
        Refusal.ControlCharacter => (NoteInvalidCharactersCode, "The note contains a control character that cannot be saved. Remove it and try again."),
        _ => null,
    };

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
