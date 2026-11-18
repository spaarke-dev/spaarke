using System.Buffers;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Services.Ai.PublicContracts;

namespace Sprk.Bff.Api.Services.Finance;

// ═══════════════════════════════════════════════════════════════════════════════════════════════
// Firm notice (ebilling-r1 task 131; FR-50, D-60, D-66, NFR-10, ADR-013, ADR-015, ADR-032, ADR-043). The notice a firm receives when its
// submission is rejected, its invoice is returned or rejected in full, or its invoice is approved with adjustments.
// ═══════════════════════════════════════════════════════════════════════════════════════════════

/// <summary>The outcome a firm notice explains. Mirrors <see cref="InvoiceFirmNoticeOutcome"/> (the model's input) one for one.</summary>
public enum FirmNoticeKind
{
    SubmissionRejected,
    ReturnedToFirm,
    InvoiceRejected,
    ApprovedWithAdjustments,
}

/// <summary>Who wrote the prose of a draft.</summary>
public enum FirmNoticeSource
{
    /// <summary>The <c>invoice-firm-notice</c> Action (INVOICE-FIRM-NOTICE@v1), through <see cref="IInvoiceAi.DraftFirmNoticeAsync"/>.</summary>
    Action,

    /// <summary>The fixed wording of <see cref="FirmNoticeFallbackText"/>: the Action failed, is disabled, is not bound, produced prose that failed the checks, or the settings toggle is off.</summary>
    Template,
}

/// <summary>What <see cref="FirmNoticeComposer"/> could do.</summary>
public enum FirmNoticeStatus
{
    Composed,

    /// <summary>The invoice or submission does not exist.</summary>
    NotFound,

    /// <summary>The record is in a state this notice does not describe (for example a Return notice for a Paid invoice).</summary>
    NotApplicable,

    /// <summary>A read of the firm's outcome faulted, so nothing may be told right now (fail closed).</summary>
    Unavailable,
}

/// <summary>
/// A draft firm notice. <paramref name="Subject"/> is the subject line with the tracking number appended by code; <paramref name="Prose"/> is the
/// editable plain text (paragraphs separated by a blank line); <paramref name="Html"/> is the whole notice rendered from that prose and the firm
/// outcome. <paramref name="ActionVersion"/> is set only when <paramref name="Source"/> is <see cref="FirmNoticeSource.Action"/>;
/// <paramref name="FallbackReason"/> (a stable code, see <see cref="FirmNoticeFallbackReasons"/>) only when it is <see cref="FirmNoticeSource.Template"/>.
/// </summary>
public sealed record FirmNoticeDraft(
    FirmNoticeKind Kind,
    string Subject,
    string Prose,
    string Html,
    FirmNoticeSource Source,
    string? ActionVersion,
    string? FallbackReason);

/// <summary>The result of composing a notice: a draft when <see cref="Status"/> is <see cref="FirmNoticeStatus.Composed"/>, else none.</summary>
public sealed record FirmNoticeResult(FirmNoticeStatus Status, FirmNoticeDraft? Draft);

/// <summary>Stable codes for why the fixed template was used instead of the Action's prose (logged, and shown to the reviewer; never prose).</summary>
public static class FirmNoticeFallbackReasons
{
    /// <summary><c>sprk_firmnoticeaienabled</c> is off.</summary>
    public const string SettingOff = "ai.setting_off";

    /// <summary>The settings row could not be read, so whether AI drafting is allowed is unknown.</summary>
    public const string SettingsUnavailable = "ai.settings_unavailable";

    /// <summary>The Null peer is active (the AI kill switch is off).</summary>
    public const string AiDisabled = "ai.disabled";

    /// <summary>The Action did not answer within the composer's time limit.</summary>
    public const string AiTimeout = "ai.timeout";

    /// <summary>Any other failure of the call (the exception type is logged, never its message).</summary>
    public const string AiError = "ai.error";

    // Binding missing, completion failed and output invalid reuse InvoiceAiException's own reason codes (ai.invoice.*).

    public const string ProseEmpty = "prose.empty";
    public const string ProseTooLong = "prose.too_long";
    public const string ProseControlCharacter = "prose.control_character";

    /// <summary>The prose has a digit (an amount, a date, a line number, a percentage): FR-50.</summary>
    public const string ProseDigit = "prose.digit";

    public const string ProseCurrency = "prose.currency";
    public const string ProseAmountWord = "prose.amount_word";
    public const string ProseLink = "prose.link";
}

/// <summary>
/// THE fixed wording used when the Action cannot be (or is not allowed to be) used: plain outcome facts, one short paragraph per outcome kind,
/// no legal language. This is firm-facing correspondence, so the text here needs the owner's approval before it ships (131 notes, "Fallback
/// wording for owner approval"); it is a closed table in code so a change is reviewed as a change.
/// </summary>
public static class FirmNoticeFallbackText
{
    public static readonly IReadOnlyDictionary<FirmNoticeKind, string> Subjects = new Dictionary<FirmNoticeKind, string>
    {
        [FirmNoticeKind.SubmissionRejected] = "Invoice submission rejected",
        [FirmNoticeKind.ReturnedToFirm] = "Invoice returned to firm",
        [FirmNoticeKind.InvoiceRejected] = "Invoice rejected",
        [FirmNoticeKind.ApprovedWithAdjustments] = "Invoice approved with adjustments",
    };

    public static readonly IReadOnlyDictionary<FirmNoticeKind, string> Prose = new Dictionary<FirmNoticeKind, string>
    {
        [FirmNoticeKind.SubmissionRejected] = "Your invoice submission was rejected and was not accepted for review.",
        [FirmNoticeKind.ReturnedToFirm] = "Your invoice has been returned to you and is now closed. A corrected invoice can be submitted.",
        [FirmNoticeKind.InvoiceRejected] = "Your invoice has been rejected in full.",
        [FirmNoticeKind.ApprovedWithAdjustments] = "Your invoice has been approved with adjustments.",
    };

    /// <summary>The fixed words around the data, also part of the wording for owner approval.</summary>
    public const string Greeting = "Hello,";

    public const string FindingsHeading = "Findings";
}

/// <summary>
/// The checks every piece of model prose passes before it is used (FR-50). The model is never given an amount, so a figure in its prose is an
/// invention: any digit (in any script), currency symbol or amount word, any link or address, a control or invisible character, an empty text or
/// one far longer than asked for sends the notice to the fixed template. This is the second wall; the first is that the input carries no number
/// and the output schema has no numeric property.
/// </summary>
internal static class FirmNoticeProse
{
    internal const int MaxSubjectLength = 120;
    internal const int MaxProseLength = 1500;

    // Money words, number words (cardinals two to ninety, the big-number words, fractions and multipliers) and ordinals (first to tenth), as whole
    // words, case-insensitively, after NFKC folding. "one" is deliberately absent (it is ordinary prose; a numbered reference such as "line one" is
    // refused by NumberedReference). A false positive only sends the notice to the fixed template.
    private static readonly Regex AmountWords = new(
        @"\b(dollars?|cents?|euros?|pounds?\s+sterling|per\s?cent|percent|percentage|usd|eur|gbp"
        + @"|zero|two|three|four|five|six|seven|eight|nine|tens?|eleven|twelve|thirteen|fourteen|fifteen|sixteen|seventeen|eighteen|nineteen"
        + @"|twenty|thirty|forty|fifty|sixty|seventy|eighty|ninety|(twent|thirt|fort|fift|sixt|sevent|eight|ninet)ies"
        + @"|hundreds?|thousands?|millions?|billions?|dozens?|half|halves|quarters?|twice|double|triple"
        + @"|(hundred|thousand|million|billion)ths?"
        + @"|first|second|third|fourth|fifth|sixth|seventh|eighth|ninth|tenth|eleventh|twelfth|thirteenth|fourteenth|fifteenth|sixteenth|seventeenth|eighteenth|nineteenth"
        + @"|(twent|thirt|fort|fift|sixt|sevent|eight|ninet)ieth)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    private static readonly Regex NumberedReference = new(
        @"\b(line|lines|entry|entries|row|rows|item|items|invoice|invoices|page|pages|section|sections)\s+(no\.?|number|#|zero|one|two|three|four|five|six|seven|eight|nine|ten|eleven|twelve|thirteen|fourteen|fifteen|sixteen|seventeen|eighteen|nineteen|twenty)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    private static readonly Regex Link = new(
        @"(https?:|ftp:|www\.|@|\b[\p{L}\p{N}][\p{L}\p{N}-]*(\.[\p{L}\p{N}][\p{L}\p{N}-]*)*\.\p{L}{2,24}\b)", // any bare domain shape, any script: label.label with a 2-24 letter TLD
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    /// <summary>The reason code of the first check the text fails, or null when it passes. Never throws.</summary>
    internal static string? Check(string? subject, string? body)
    {
        if (string.IsNullOrWhiteSpace(subject) || string.IsNullOrWhiteSpace(body))
        {
            return FirmNoticeFallbackReasons.ProseEmpty;
        }

        if (subject.Length > MaxSubjectLength || body.Length > MaxProseLength)
        {
            return FirmNoticeFallbackReasons.ProseTooLong;
        }

        return CheckText(subject, allowLineBreaks: false) ?? CheckText(body, allowLineBreaks: true);
    }

    /// <summary>True when the text carries a figure, a currency, an amount word or a numbered reference (used to screen a firm reason before the model sees it).</summary>
    internal static bool ContainsFigure(string text) => CheckText(text, allowLineBreaks: true) is not null;

    private static string? CheckText(string text, bool allowLineBreaks)
    {
        // The code points are walked as runes, so a character outside the BMP (a mathematical digit, an emoji digit) is seen whole and not as two
        // surrogates; a lone surrogate is not valid text at all.
        var scanned = ScanRunes(text, allowLineBreaks);
        if (scanned is not null)
        {
            return scanned;
        }

        // NFKC folds compatibility forms (mathematical alphanumerics, full-width letters, ligatures) to plain letters and digits, so every word
        // check below sees what a reader sees. Folding can only fail on invalid text, which is refused.
        string folded;
        try
        {
            folded = text.Normalize(NormalizationForm.FormKC);
        }
        catch (ArgumentException)
        {
            return FirmNoticeFallbackReasons.ProseControlCharacter;
        }

        if (!string.Equals(folded, text, StringComparison.Ordinal))
        {
            scanned = ScanRunes(folded, allowLineBreaks);
            if (scanned is not null)
            {
                return scanned;
            }
        }

        // Three views of what a reader sees: the raw text; the NFKC fold with the ideographic and full-width full stops read as dots; and that with
        // every combining mark removed (NFKD first, so an accent on a precomposed letter goes too). A mark inside a word ("th" + U+0301 + "ree")
        // leaves NFKC as it was and would otherwise split a number word or a domain past the regexes.
        string dotted;
        string stripped;
        try
        {
            dotted = folded.Replace('。', '.').Replace('．', '.').Replace('｡', '.');
            stripped = StripMarks(dotted.Normalize(NormalizationForm.FormKD));
        }
        catch (ArgumentException)
        {
            return FirmNoticeFallbackReasons.ProseControlCharacter;
        }

        if (MatchesSafely(Link, text) || MatchesSafely(Link, dotted) || MatchesSafely(Link, stripped))
        {
            return FirmNoticeFallbackReasons.ProseLink;
        }

        return MatchesSafely(AmountWords, dotted) || MatchesSafely(NumberedReference, dotted)
            || MatchesSafely(AmountWords, stripped) || MatchesSafely(NumberedReference, stripped)
            ? FirmNoticeFallbackReasons.ProseAmountWord
            : null;
    }

    /// <summary>The text without its nonspacing and enclosing marks (Mn, Me).</summary>
    private static string StripMarks(string text)
    {
        var builder = new StringBuilder(text.Length);
        foreach (var rune in text.EnumerateRunes())
        {
            if (Rune.GetUnicodeCategory(rune) is not (UnicodeCategory.NonSpacingMark or UnicodeCategory.EnclosingMark))
            {
                builder.Append(rune.ToString());
            }
        }

        return builder.ToString();
    }

    private static string? ScanRunes(string text, bool allowLineBreaks)
    {
        var index = 0;
        while (index < text.Length)
        {
            if (Rune.DecodeFromUtf16(text.AsSpan(index), out var rune, out var consumed) != OperationStatus.Done)
            {
                return FirmNoticeFallbackReasons.ProseControlCharacter;
            }

            index += consumed;
            if (Rune.IsNumber(rune))
            {
                return FirmNoticeFallbackReasons.ProseDigit;
            }

            var category = Rune.GetUnicodeCategory(rune);
            if (category == UnicodeCategory.CurrencySymbol)
            {
                return FirmNoticeFallbackReasons.ProseCurrency;
            }

            var lineBreak = rune.Value is 10 or 13 or 9;
            if ((Rune.IsControl(rune) && !(allowLineBreaks && lineBreak)) ||
                category is UnicodeCategory.Format or UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator
                    or UnicodeCategory.Surrogate or UnicodeCategory.PrivateUse or UnicodeCategory.OtherNotAssigned)
            {
                return FirmNoticeFallbackReasons.ProseControlCharacter;
            }
        }

        return null;
    }

    // A regex timeout is a refusal, never a pass: nothing is unchecked.
    private static bool MatchesSafely(Regex regex, string text)
    {
        try
        {
            return regex.IsMatch(text);
        }
        catch (RegexMatchTimeoutException)
        {
            return true;
        }
    }
}

/// <summary>
/// Composes the firm notice (ebilling-r1 task 131; FR-50, D-60). Every amount, line reference, clause quote and the findings table is inserted BY CODE
/// from <see cref="FirmFindingsProjection"/> (task 130): the ONLY data source, so nothing the projection withholds (an override reason, a Procedure
/// name or id, a clause of a rule set whose flag is off) can reach a notice (D-66, NFR-10). The model writes only the subject and the explanatory
/// prose, through <see cref="IInvoiceAi.DraftFirmNoticeAsync"/> to <c>IActionRunner</c> (ADR-013, ADR-043); its input carries the outcome name, rule
/// names and a fixed non-numeric sentence per finding, never an amount, a line number, a reviewer's reason or a clause. Its prose is checked
/// (<see cref="FirmNoticeProse"/>) and HTML-encoded; any failure, and the settings toggle <c>sprk_firmnoticeaienabled</c> being off, gives the fixed
/// wording of <see cref="FirmNoticeFallbackText"/> instead, with <see cref="FirmNoticeSource.Template"/> and a reason code on the draft (FR-50, ADR-032).
/// </summary>
/// <remarks>
/// <para><b>No caller check.</b> Like the projection, the caller has already established that the caller may see this invoice or submission (the draft
/// route's Write-on-the-invoice filter; 132's send path). Nothing here decides access.</para>
/// <para><b>Logs</b> carry ids, counts, timings, the kind, the source and a reason code, never prose, a finding or a table cell (ADR-015, NFR-11).</para>
/// <para><b>Component justification (CLAUDE.md section 11).</b> Existing: <c>RegistrationEmailService</c> (embedded HTML template, encoded placeholders;
/// it sends mail and is registration-specific); the 130 projection (data); the 041 facade (prose). Extension: none of them composes a notice from a
/// projection; this reuses all three and the embedded-resource technique. Cost of doing nothing: FR-50 fails and 132 has no notice body.</para>
/// </remarks>
public sealed class FirmNoticeComposer
{
    /// <summary>The Action's display name and version, recorded on a draft whose prose it wrote.</summary>
    public const string ActionVersion = "INVOICE-FIRM-NOTICE@v1";

    internal const int MaxFindings = 20;

    private const string TemplateResource = "Sprk.Bff.Api.Services.Finance.EmailTemplates.FirmNoticeTemplate.html";
    private const string NotShownHtml = "<p style=\"margin:8px 0 0;color:#616161;font-size:13px;\">{0} more findings are not shown here.</p>";

    private static readonly Regex Placeholder = new(@"\{\{(?<key>[A-Za-z]+)\}\}", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
    private static readonly Regex ParagraphBreak = new(@"\n[ \t]*\n\s*", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
    private static readonly Lazy<string> Template = new(LoadTemplate);

    private readonly Func<Guid, CancellationToken, Task<FirmOutcome?>> _invoiceOutcome;
    private readonly Func<Guid, bool, CancellationToken, Task<FirmOutcome?>> _submissionOutcome;
    private readonly IEbillingSettingsReader _settings;
    private readonly IInvoiceAi _ai;
    private readonly ILogger<FirmNoticeComposer> _logger;

    /// <summary>How long the Action may take before the fixed template is used. Replaceable by the test assembly.</summary>
    internal TimeSpan AiTimeout { get; init; } = TimeSpan.FromSeconds(30);

    public FirmNoticeComposer(
        FirmFindingsProjection projection, IEbillingSettingsReader settings, IInvoiceAi ai, ILogger<FirmNoticeComposer> logger)
        : this(Required(projection).GetForInvoiceAsync, Required(projection).GetForSubmissionAsync, settings, ai, logger)
    {
    }

    /// <summary>The seam the unit tests use: the two projection reads as delegates (the projection is sealed over a Dataverse client).</summary>
    internal FirmNoticeComposer(
        Func<Guid, CancellationToken, Task<FirmOutcome?>> invoiceOutcome,
        Func<Guid, bool, CancellationToken, Task<FirmOutcome?>> submissionOutcome,
        IEbillingSettingsReader settings,
        IInvoiceAi ai,
        ILogger<FirmNoticeComposer> logger)
    {
        _invoiceOutcome = invoiceOutcome ?? throw new ArgumentNullException(nameof(invoiceOutcome));
        _submissionOutcome = submissionOutcome ?? throw new ArgumentNullException(nameof(submissionOutcome));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _ai = ai ?? throw new ArgumentNullException(nameof(ai));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    private static FirmFindingsProjection Required(FirmFindingsProjection projection) =>
        projection ?? throw new ArgumentNullException(nameof(projection));

    /// <summary>
    /// The notice for an INVOICE: <see cref="FirmNoticeKind.ReturnedToFirm"/> or <see cref="FirmNoticeKind.InvoiceRejected"/> (shown to the reviewer
    /// before the decision, while the invoice is In Review, or after it) or <see cref="FirmNoticeKind.ApprovedWithAdjustments"/>.
    /// </summary>
    /// <exception cref="ArgumentException"><paramref name="kind"/> is <see cref="FirmNoticeKind.SubmissionRejected"/> (use <see cref="ComposeForSubmissionAsync"/>).</exception>
    /// <exception cref="OperationCanceledException">The request was cancelled.</exception>
    public async Task<FirmNoticeResult> ComposeForInvoiceAsync(FirmNoticeKind kind, Guid invoiceId, CancellationToken ct)
    {
        if (kind == FirmNoticeKind.SubmissionRejected)
        {
            throw new ArgumentException("A rejected submission's notice is composed from the submission.", nameof(kind));
        }

        var outcome = await _invoiceOutcome(invoiceId, ct).ConfigureAwait(false);
        return await ComposeAsync(kind, invoiceId, outcome, ct).ConfigureAwait(false);
    }

    /// <summary>The notice for a REJECTED SUBMISSION (<see cref="FirmNoticeKind.SubmissionRejected"/>).</summary>
    /// <param name="invoiceDecided">Passed through to <see cref="FirmFindingsProjection.GetForSubmissionAsync"/>: whether the caller decided the submission's derived invoice (false: no citation and no Signal-written firm reason).</param>
    /// <exception cref="OperationCanceledException">The request was cancelled.</exception>
    public async Task<FirmNoticeResult> ComposeForSubmissionAsync(Guid submissionId, bool invoiceDecided, CancellationToken ct)
    {
        var outcome = await _submissionOutcome(submissionId, invoiceDecided, ct).ConfigureAwait(false);
        return await ComposeAsync(FirmNoticeKind.SubmissionRejected, submissionId, outcome, ct).ConfigureAwait(false);
    }

    private async Task<FirmNoticeResult> ComposeAsync(FirmNoticeKind kind, Guid id, FirmOutcome? outcome, CancellationToken ct)
    {
        var started = Stopwatch.GetTimestamp();
        if (outcome is null)
        {
            _logger.LogInformation("Firm notice not composed | Kind {Kind}, Id {Id}, Status {Status}", kind, id, FirmNoticeStatus.NotFound);
            return new FirmNoticeResult(FirmNoticeStatus.NotFound, null);
        }

        if (outcome.Degraded || outcome.Kind == FirmOutcomeKind.Unavailable)
        {
            // A read faulted: a notice with a partial table would mislead the firm, so nothing is drafted.
            _logger.LogWarning("Firm notice not composed | Kind {Kind}, Id {Id}, Status {Status}", kind, id, FirmNoticeStatus.Unavailable);
            return new FirmNoticeResult(FirmNoticeStatus.Unavailable, null);
        }

        if (!Applies(kind, outcome.Kind))
        {
            _logger.LogInformation(
                "Firm notice not composed | Kind {Kind}, Id {Id}, Status {Status}, OutcomeKind {OutcomeKind}",
                kind, id, FirmNoticeStatus.NotApplicable, outcome.Kind);
            return new FirmNoticeResult(FirmNoticeStatus.NotApplicable, null);
        }

        var (subject, prose, source, fallbackReason) = await DraftProseAsync(kind, outcome, id, ct).ConfigureAwait(false);
        var tracking = CleanTrackingNumber(outcome.TrackingNumber);
        var finalSubject = tracking.Length == 0 ? subject : $"{subject} ({tracking})";
        var draft = new FirmNoticeDraft(
            kind, finalSubject, prose, RenderHtml(outcome, prose, kind), source,
            source == FirmNoticeSource.Action ? ActionVersion : null, fallbackReason);

        _logger.LogInformation(
            "Firm notice composed | Kind {Kind}, Id {Id}, Source {Source}, FallbackReason {FallbackReason}, Items {Items}, ElapsedMs {ElapsedMs}",
            kind, id, source, fallbackReason, outcome.Items.Count, (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds);
        return new FirmNoticeResult(FirmNoticeStatus.Composed, draft);
    }

    /// <summary>
    /// The tracking number as it is written into the subject and the details: control, format, private-use and unassigned characters
    /// removed and every whitespace character (a line break in a subject header is a header-injection shape) made one space, collapsed, trimmed. Empty when nothing is left.
    /// </summary>
    internal static string CleanTrackingNumber(string? trackingNumber)
    {
        if (string.IsNullOrWhiteSpace(trackingNumber))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(trackingNumber.Length);
        foreach (var rune in trackingNumber.EnumerateRunes())
        {
            var category = Rune.GetUnicodeCategory(rune);
            if (Rune.IsWhiteSpace(rune))
            {
                builder.Append(' '); // a tab, a line break or a separator becomes one space, never a line break
            }
            else if (!Rune.IsControl(rune)
                && category is not (UnicodeCategory.Format or UnicodeCategory.Surrogate or UnicodeCategory.PrivateUse or UnicodeCategory.OtherNotAssigned)
                && rune != Rune.ReplacementChar)
            {
                builder.Append(rune.ToString());
            }
        }

        return Regex.Replace(builder.ToString(), @" {2,}", " ", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)).Trim();
    }

    /// <summary>Which projected states a notice kind may describe. A Return or Reject notice is shown before the decision (In Review) and after it.</summary>
    private static bool Applies(FirmNoticeKind kind, FirmOutcomeKind outcome) => kind switch
    {
        FirmNoticeKind.SubmissionRejected => outcome == FirmOutcomeKind.SubmissionRejected,
        FirmNoticeKind.ReturnedToFirm => outcome is FirmOutcomeKind.InvoiceInReview or FirmOutcomeKind.ReturnedToFirm,
        FirmNoticeKind.InvoiceRejected => outcome is FirmOutcomeKind.InvoiceInReview or FirmOutcomeKind.Rejected,
        FirmNoticeKind.ApprovedWithAdjustments => outcome == FirmOutcomeKind.ApprovedWithAdjustments,
        _ => false,
    };

    // ── The prose: the Action's, or the fixed template's ──

    private async Task<(string Subject, string Prose, FirmNoticeSource Source, string? FallbackReason)> DraftProseAsync(
        FirmNoticeKind kind, FirmOutcome outcome, Guid id, CancellationToken ct)
    {
        try
        {
            if (!(await _settings.GetAsync(ct).ConfigureAwait(false)).FirmNoticeAiEnabled)
            {
                return Fallback(kind, FirmNoticeFallbackReasons.SettingOff);
            }
        }
        catch (Exception ex) when (IsFault(ex, ct))
        {
            // Whether AI drafting is allowed is unknown: the template is always safe to use.
            _logger.LogWarning("Firm notice settings could not be read | Kind {Kind}, Id {Id}, ExceptionType {ExceptionType}", kind, id, ex.GetType().Name);
            return Fallback(kind, FirmNoticeFallbackReasons.SettingsUnavailable);
        }

        InvoiceFirmNoticeDraft draft;
        using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct))
        {
            timeout.CancelAfter(AiTimeout);
            try
            {
                draft = await _ai.DraftFirmNoticeAsync(BuildRequest(kind, outcome), timeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (OperationCanceledException)
            {
                return Fallback(kind, FirmNoticeFallbackReasons.AiTimeout);
            }
            catch (FeatureDisabledException)
            {
                return Fallback(kind, FirmNoticeFallbackReasons.AiDisabled);
            }
            catch (InvoiceAiException ex)
            {
                // The code says where it failed (binding missing, completion failed, output invalid); never the message.
                return Fallback(kind, ex.ReasonCode);
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Firm notice Action failed | Kind {Kind}, Id {Id}, ExceptionType {ExceptionType}", kind, id, ex.GetType().Name);
                return Fallback(kind, FirmNoticeFallbackReasons.AiError);
            }
        }

        var refusal = FirmNoticeProse.Check(draft?.Subject, draft?.Body);
        if (refusal is not null)
        {
            _logger.LogWarning("Firm notice prose refused | Kind {Kind}, Id {Id}, Reason {Reason}", kind, id, refusal);
            return Fallback(kind, refusal);
        }

        return (NormalizeSubject(draft!.Subject), NormalizeProse(draft.Body), FirmNoticeSource.Action, null);
    }

    private (string Subject, string Prose, FirmNoticeSource Source, string? FallbackReason) Fallback(FirmNoticeKind kind, string reason)
    {
        _logger.LogInformation("Firm notice uses the fixed template | Kind {Kind}, Reason {Reason}", kind, reason);
        return (FirmNoticeFallbackText.Subjects[kind], FirmNoticeFallbackText.Prose[kind], FirmNoticeSource.Template, reason);
    }

    private static bool IsFault(Exception ex, CancellationToken ct) => !(ex is OperationCanceledException && ct.IsCancellationRequested);

    private static string NormalizeSubject(string subject) => Regex.Replace(subject.Trim(), @"\s+", " ", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    private static string NormalizeProse(string body) => body.Replace("\r\n", "\n").Replace('\r', '\n').Trim();

    // ── What the model is told ──

    /// <summary>
    /// The model's input: the outcome name and a short summary of the findings. Per finding: the rule's name (only when the projection cited the rule,
    /// i.e. the firm may be told it), the kind of finding and ONE sentence: the firm-safe reason when it carries no figure, else a fixed sentence.
    /// Never an amount, a line, a date, a reviewer's reason, a clause or a rule set's name (<c>RuleSetName</c> is always blank).
    /// </summary>
    internal static InvoiceFirmNoticeRequest BuildRequest(FirmNoticeKind kind, FirmOutcome outcome)
    {
        var findings = outcome.Items
            .Select(item => new InvoiceFirmNoticeFinding(
                RuleSetName: string.Empty,
                RuleName: Screened(item.Citations.Select(c => c.RuleName).FirstOrDefault(n => !string.IsNullOrWhiteSpace(n))) ?? string.Empty,
                Consequence: item.Decision.ToString(),
                Issue: Screened(item.FirmReasons.FirstOrDefault(r => !string.IsNullOrWhiteSpace(r))) ?? FixedIssue(item.Decision)))
            .Take(MaxFindings) // one entry per item: no collapsing, so the model is not led to say "a line" for several (a count is not a figure it may write)
            .ToList();

        return new InvoiceFirmNoticeRequest { Outcome = ToAiOutcome(kind), Findings = findings };
    }

    private static string? Screened(string? text) =>
        string.IsNullOrWhiteSpace(text) || FirmNoticeProse.ContainsFigure(text) ? null : text.Trim();

    private static string FixedIssue(FirmOutcomeDecision decision) => decision switch
    {
        FirmOutcomeDecision.Reduced => "A billed line was reduced.",
        FirmOutcomeDecision.Disallowed => "A billed line was not allowed.",
        FirmOutcomeDecision.Added => "A line was added to the invoice.",
        FirmOutcomeDecision.Adjustment => "An adjustment was applied to the invoice.",
        _ => "A finding led to the rejection.",
    };

    private static InvoiceFirmNoticeOutcome ToAiOutcome(FirmNoticeKind kind) => kind switch
    {
        FirmNoticeKind.SubmissionRejected => InvoiceFirmNoticeOutcome.SubmissionRejected,
        FirmNoticeKind.ReturnedToFirm => InvoiceFirmNoticeOutcome.ReturnedToFirm,
        FirmNoticeKind.InvoiceRejected => InvoiceFirmNoticeOutcome.InvoiceRejected,
        _ => InvoiceFirmNoticeOutcome.ApprovedWithAdjustments,
    };

    // ── The HTML: every value is encoded; every figure is the projection's ──

    /// <summary>
    /// Renders the notice from the firm outcome and the (possibly reviewer-edited) plain-text prose. Public so the send path (132) can render the
    /// reviewer's edited prose against the same data. The template is substituted in ONE pass, so a placeholder written inside the prose or a cell is
    /// text, never expanded. The Approved column is left out when the notice is a Reject or a Return, or the invoice already is (130f): the reviewer's
    /// preview matches what the firm receives. The send path passes <paramref name="kind"/>.
    /// </summary>
    public static string RenderHtml(FirmOutcome outcome, string prose, FirmNoticeKind? kind = null)
    {
        ArgumentNullException.ThrowIfNull(outcome);
        var values = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Greeting"] = Encode(FirmNoticeFallbackText.Greeting),
            ["Prose"] = ProseHtml(prose),
            ["Details"] = DetailsHtml(outcome),
            ["Findings"] = FindingsHtml(outcome, kind),
        };

        return Placeholder.Replace(Template.Value, m =>
            values.TryGetValue(m.Groups["key"].Value, out var value)
                ? value
                : throw new InvalidOperationException($"The firm notice template names an unknown placeholder '{m.Value}'."));
    }

    private static string ProseHtml(string prose)
    {
        var paragraphs = ParagraphBreak.Split(prose.Replace("\r\n", "\n").Replace('\r', '\n').Trim())
            .Select(p => p.Trim())
            .Where(p => p.Length > 0);
        return string.Join(
            "\n",
            paragraphs.Select(p => "<p style=\"margin:0 0 16px;color:#242424;font-size:15px;line-height:1.6;\">" +
                Encode(p).Replace("\n", "<br />") + "</p>"));
    }

    private static string DetailsHtml(FirmOutcome outcome)
    {
        var lines = new List<string>();
        var tracking = CleanTrackingNumber(outcome.TrackingNumber);
        if (tracking.Length > 0)
        {
            lines.Add("Tracking number: " + Encode(tracking));
        }

        if (outcome.BilledTotal is { } billed)
        {
            lines.Add("Billed total: " + Money(billed));
        }

        if (outcome.ApprovedTotal is { } approved)
        {
            lines.Add("Approved total: " + Money(approved));
        }

        return lines.Count == 0
            ? string.Empty
            : "<p style=\"margin:0 0 16px;color:#242424;font-size:14px;line-height:1.6;\">" + string.Join("<br />", lines) + "</p>";
    }

    private static string FindingsHtml(FirmOutcome outcome, FirmNoticeKind? kind)
    {
        if (outcome.Items.Count == 0)
        {
            return string.Empty;
        }

        const string cell = "padding:6px 8px;border:1px solid #d1d1d1;vertical-align:top;text-align:left;font-size:13px;";
        var sb = new StringBuilder();
        sb.Append("<h2 style=\"margin:8px 0 8px;color:#242424;font-size:16px;\">").Append(Encode(FirmNoticeFallbackText.FindingsHeading)).Append("</h2>");
        sb.Append("<table role=\"presentation\" cellspacing=\"0\" cellpadding=\"0\" border=\"0\" style=\"border-collapse:collapse;width:100%;\"><thead><tr>");
        // 130f (EB-D-259, K4): nothing is approved by a Reject or a Return (the notice, or the invoice already), so the table has no Approved column (same labels, one column fewer).
        var showApproved = outcome.Kind is not (FirmOutcomeKind.Rejected or FirmOutcomeKind.ReturnedToFirm)
            && kind is not (FirmNoticeKind.InvoiceRejected or FirmNoticeKind.ReturnedToFirm);
        foreach (var heading in new[] { "Item", "Decision", "Billed", "Approved", "Reason", "Rule" }.Where(h => showApproved || h != "Approved"))
        {
            sb.Append("<th style=\"").Append(cell).Append("background-color:#f4f6f8;\">").Append(Encode(heading)).Append("</th>");
        }

        sb.Append("</tr></thead><tbody>");
        foreach (var item in outcome.Items)
        {
            sb.Append("<tr>");
            var cells = new List<string> { ItemCell(item), Encode(DecisionLabel(item.Decision)), Money(item.Line?.BilledAmount) };
            if (showApproved)
            {
                cells.Add(Money(item.Line?.ApprovedAmount));
            }

            cells.Add(ReasonCell(item));
            cells.Add(RuleCell(item));
            foreach (var html in cells)
            {
                sb.Append("<td style=\"").Append(cell).Append("\">").Append(html).Append("</td>");
            }

            sb.Append("</tr>");
        }

        sb.Append("</tbody></table>");
        if (outcome.NotShownCount is > 0 and var notShown)
        {
            sb.AppendFormat(CultureInfo.InvariantCulture, NotShownHtml, notShown);
        }

        return sb.ToString();
    }

    private static string DecisionLabel(FirmOutcomeDecision decision) => decision switch
    {
        FirmOutcomeDecision.Reduced => "Reduced",
        FirmOutcomeDecision.Disallowed => "Not allowed",
        FirmOutcomeDecision.Added => "Line added",
        FirmOutcomeDecision.Adjustment => "Invoice adjustment",
        _ => "Rejection finding",
    };

    private static string ItemCell(FirmOutcomeItem item)
    {
        if (item.Decision == FirmOutcomeDecision.RejectionFinding)
        {
            return Encode(item.LineNumber is { } n ? "Line " + n.ToString(CultureInfo.InvariantCulture) : "Invoice");
        }

        if (item.Adjustment is { } adjustment)
        {
            var value = adjustment.Value is { } v ? " " + v.ToString("0.##", CultureInfo.InvariantCulture) : string.Empty;
            return Encode($"{adjustment.Method}{value}") + "<br />" + Money(adjustment.Amount);
        }

        if (item.Line is { } line)
        {
            return Join(
                line.Date?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), line.TimekeeperAsBilled, line.DescriptionExcerpt);
        }

        return string.Empty;
    }

    private static string ReasonCell(FirmOutcomeItem item)
    {
        var lines = new List<string?> { item.Reason };
        lines.AddRange(item.FirmReasons);
        lines.AddRange(item.ValuesRead?.Select(v => $"{v.Name}: {v.Value}") ?? []);
        return Join(lines.ToArray());
    }

    private static string RuleCell(FirmOutcomeItem item) =>
        string.Join("<br />", item.Citations.Select(c =>
        {
            var detail = string.Join(", ", new[] { c.Label, c.Version is { } v ? "version " + v.ToString(CultureInfo.InvariantCulture) : null, c.SectionReference }
                .Where(s => !string.IsNullOrWhiteSpace(s)));
            var head = Join(c.RuleName) + (detail.Length > 0 ? " (" + Encode(detail) + ")" : string.Empty);
            return string.IsNullOrWhiteSpace(c.Quote) ? head : head + "<br />&ldquo;" + Encode(c.Quote.Trim()) + "&rdquo;";
        }));

    private static string Join(params string?[] parts) =>
        string.Join("<br />", parts.Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => Encode(p!.Trim())));

    /// <summary>An amount as the projection has it: two decimals, no currency (130's contract carries none). Blank for no amount.</summary>
    private static string Money(decimal? amount) =>
        amount is { } a ? Encode(a.ToString("N2", CultureInfo.InvariantCulture)) : string.Empty;

    private static string Encode(string value) => WebUtility.HtmlEncode(value);

    private static string LoadTemplate()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(TemplateResource)
            ?? throw new InvalidOperationException($"Firm notice template not found as embedded resource '{TemplateResource}'.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
