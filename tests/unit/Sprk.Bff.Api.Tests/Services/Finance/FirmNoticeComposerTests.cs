using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Services.Ai.PublicContracts;
using Sprk.Bff.Api.Services.Finance;
using Sprk.Bff.Api.Services.Finance.Models;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Finance;

/// <summary>
/// ebilling-r1 task 131 (FR-50, D-60, D-66): <see cref="FirmNoticeComposer"/>. The projection is a delegate (the composer's test seam), so every
/// amount, line, clause and table row is a value the test put into the <see cref="FirmOutcome"/>; the model is a mock <see cref="IInvoiceAi"/> that
/// records the request it is sent. Anti-stub: the rendered HTML is checked against the projection's values both ways (everything shown is
/// the projection's; everything the projection says to show is shown).
/// </summary>
public sealed class FirmNoticeComposerTests
{
    private static readonly Guid InvoiceId = Guid.Parse("31000000-0000-4000-8000-000000000001");
    private static readonly Guid SubmissionId = Guid.Parse("31000000-0000-4000-8000-000000000002");

    private const string Quote = "No timekeeper may bill beyond the daily cap.";

    /// <summary>How the request reaches the model: the facade sends the outcome as its name, so enums are written as strings here.</summary>
    private static readonly JsonSerializerOptions Json = new() { Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } };

    // ── Fixtures ──

    private static FirmOutcomeItem Reduced() => new(
        FirmOutcomeDecision.Reduced, Guid.NewGuid(),
        new FirmOutcomeLine(new DateOnly(2026, 3, 4), "J. Smith", "Research on the motion", 1234.50m, 800.00m),
        null, "Too many hours for this task", ["Hours exceed the daily cap"],
        [new FirmOutcomeCitation("Daily cap rule", "Acme Billing Guidelines", 3, "Section 4.2", Quote)]);

    private static FirmOutcomeItem Disallowed() => new(
        FirmOutcomeDecision.Disallowed, Guid.NewGuid(),
        new FirmOutcomeLine(new DateOnly(2026, 3, 5), "A. Jones", "Clerical work", 250.00m, 0.00m),
        null, null, [], []);

    private static FirmOutcomeItem Added() => new(
        FirmOutcomeDecision.Added, Guid.NewGuid(),
        new FirmOutcomeLine(null, null, "Court filing fee", 100.00m, null), null, null, [], []);

    private static FirmOutcomeItem Adjustment() => new(
        FirmOutcomeDecision.Adjustment, null, null, new FirmOutcomeAdjustment("Percent", 10m, 55.55m), "Volume discount", [], []);

    private static FirmOutcome InReview(params FirmOutcomeItem[] items) =>
        new(FirmOutcomeKind.InvoiceInReview, "TRK-1.2", "In review", 9876.54m, null, items.Length == 0 ? new[] { Reduced(), Disallowed(), Added() } : items, false);

    private static FirmOutcome Of(FirmOutcomeKind kind, params FirmOutcomeItem[] items) =>
        new(kind, "TRK-1.2", kind.ToString(), 9876.54m, kind is FirmOutcomeKind.ApprovedWithAdjustments ? 9000.00m : null, items, false);

    private sealed class CapturingLogger : ILogger<FirmNoticeComposer>
    {
        public readonly List<string> Messages = [];
        public readonly List<Exception?> Exceptions = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            Messages.Add(formatter(state, exception));
            Exceptions.Add(exception);
        }
    }

    private sealed class Rig
    {
        public readonly Mock<IInvoiceAi> Ai = new(MockBehavior.Strict);
        public readonly Mock<IEbillingSettingsReader> Settings = new(MockBehavior.Strict);
        public readonly CapturingLogger Logger = new();
        public readonly List<InvoiceFirmNoticeRequest> Requests = [];
        public readonly List<(Guid Id, bool Decided)> SubmissionReads = [];
        public readonly List<Guid> InvoiceReads = [];
        public FirmOutcome? Outcome = InReview();
        public TimeSpan? AiTimeout;

        public Rig(InvoiceFirmNoticeDraft? draft = null, bool aiEnabled = true)
        {
            Settings.Setup(s => s.GetAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new EbillingSettings { FirmNoticeAiEnabled = aiEnabled });
            Ai.Setup(a => a.DraftFirmNoticeAsync(It.IsAny<InvoiceFirmNoticeRequest>(), It.IsAny<CancellationToken>()))
                .Callback((InvoiceFirmNoticeRequest r, CancellationToken _) => Requests.Add(r))
                .ReturnsAsync(draft ?? Prose("Invoice returned for correction", "Your invoice has been returned to you.\n\nYou can submit a corrected invoice."));
        }

        public static InvoiceFirmNoticeDraft Prose(string subject, string body) => new() { Subject = subject, Body = body };

        public void AiThrows(Exception ex) =>
            Ai.Setup(a => a.DraftFirmNoticeAsync(It.IsAny<InvoiceFirmNoticeRequest>(), It.IsAny<CancellationToken>())).ThrowsAsync(ex);

        public void AiReturns(string subject, string body) =>
            Ai.Setup(a => a.DraftFirmNoticeAsync(It.IsAny<InvoiceFirmNoticeRequest>(), It.IsAny<CancellationToken>()))
                .Callback((InvoiceFirmNoticeRequest r, CancellationToken _) => Requests.Add(r))
                .ReturnsAsync(Prose(subject, body));

        public FirmNoticeComposer Composer() => new(
            (id, _) =>
            {
                InvoiceReads.Add(id);
                return Task.FromResult(Outcome);
            },
            (id, decided, _) =>
            {
                SubmissionReads.Add((id, decided));
                return Task.FromResult(Outcome);
            },
            Settings.Object, Ai.Object, Logger)
        {
            AiTimeout = AiTimeout ?? TimeSpan.FromSeconds(30),
        };

        public async Task<FirmNoticeDraft> ComposeAsync(FirmNoticeKind kind = FirmNoticeKind.ReturnedToFirm)
        {
            var result = await Composer().ComposeForInvoiceAsync(kind, InvoiceId, CancellationToken.None);
            result.Status.Should().Be(FirmNoticeStatus.Composed);
            return result.Draft!;
        }
    }

    // ═══════════════════════════════════════════════════════════════════════════════════════════
    // The Action's prose
    // ═══════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ActionProse_IsUsed_WithTheActionVersion_AndTheTrackingNumberInTheSubject()
    {
        var rig = new Rig();

        var draft = await rig.ComposeAsync();

        draft.Source.Should().Be(FirmNoticeSource.Action);
        draft.ActionVersion.Should().Be("INVOICE-FIRM-NOTICE@v1");
        draft.FallbackReason.Should().BeNull();
        draft.Subject.Should().Be("Invoice returned for correction (TRK-1.2)");
        draft.Prose.Should().Be("Your invoice has been returned to you.\n\nYou can submit a corrected invoice.");
        draft.Html.Should().Contain("Your invoice has been returned to you.").And.Contain("You can submit a corrected invoice.");
        draft.Kind.Should().Be(FirmNoticeKind.ReturnedToFirm);
        rig.Ai.Verify(a => a.DraftFirmNoticeAsync(It.IsAny<InvoiceFirmNoticeRequest>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task TheModelIsSentTheOutcomeAndASummary_WithNoDigitAtAll_AndNoAmountLineClauseReasonOrRuleSetName()
    {
        var rig = new Rig();

        await rig.ComposeAsync(FirmNoticeKind.InvoiceRejected);

        var request = rig.Requests.Should().ContainSingle().Subject;
        request.Outcome.Should().Be(InvoiceFirmNoticeOutcome.InvoiceRejected);
        request.Findings.Select(f => f.Consequence).Should().Equal("Reduced", "Disallowed", "Added");
        request.Findings[0].RuleName.Should().Be("Daily cap rule");
        request.Findings[0].Issue.Should().Be("Hours exceed the daily cap", "a firm-safe reason without a figure may reach the model");
        request.Findings[1].Issue.Should().Be("A billed line was not allowed.", "no reason is on record: a fixed sentence");
        request.Findings.Should().OnlyContain(f => f.RuleSetName == string.Empty);

        var json = JsonSerializer.Serialize(request, Json);
        json.Should().NotMatchRegex(@"\d", "FR-50: no number of any kind reaches the model");
        foreach (var secret in new[] { "1234", "800", "250", "9876", "55.55", Quote, "Too many hours", "Acme Billing Guidelines", "J. Smith", "Section 4.2" })
        {
            json.Should().NotContain(secret);
        }
    }

    [Fact]
    public async Task AFirmReasonWithAFigure_NeverReachesTheModel_AFixedSentenceTakesItsPlace()
    {
        var rig = new Rig();
        var item = Reduced() with { FirmReasons = ["The rate is 20% above the approved rate"], Citations = [] };
        rig.Outcome = InReview(item);

        await rig.ComposeAsync();

        var finding = rig.Requests.Single().Findings.Should().ContainSingle().Subject;
        finding.Issue.Should().Be("A billed line was reduced.");
        finding.RuleName.Should().BeEmpty("the rule is not cited, so the firm is not told its name");
        JsonSerializer.Serialize(rig.Requests.Single(), Json).Should().NotMatchRegex(@"\d|%");
    }

    [Fact]
    public async Task TheFindingsSummary_IsOnePerItem_AndCapped()
    {
        var rig = new Rig();
        var many = Enumerable.Range(0, 60).Select(i => Disallowed()).Concat(Enumerable.Range(0, 60).Select(i =>
            Reduced() with { Citations = [new FirmOutcomeCitation("Rule " + new string((char)('a' + i % 26), 1) + new string((char)('a' + i / 26), 1), null, null, null, null)], FirmReasons = [] }));
        rig.Outcome = InReview(many.ToArray());

        await rig.ComposeAsync();

        var findings = rig.Requests.Single().Findings;
        findings.Should().HaveCount(FirmNoticeComposer.MaxFindings);
        findings.Count(f => f.Consequence == "Disallowed").Should().Be(FirmNoticeComposer.MaxFindings, "review round 1: identical items are not collapsed, so the first twenty are twenty Disallowed findings");
        findings[0].Consequence.Should().Be("Disallowed");
    }

    // ═══════════════════════════════════════════════════════════════════════════════════════════
    // Data insertion: every figure is the projection's
    // ═══════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task EveryAmountInTheHtml_IsAnAmountOfTheProjection_AndEveryProjectedAmountIsShown()
    {
        var rig = new Rig();
        rig.Outcome = InReview(Reduced(), Disallowed(), Added(), Adjustment());

        var draft = await rig.ComposeAsync();

        var shown = Regex.Matches(VisibleText(draft.Html), @"\d[\d,]*\.\d{2}").Select(m => m.Value).ToList();
        var projected = new[] { "1,234.50", "800.00", "250.00", "0.00", "100.00", "55.55", "9,876.54" };
        // 130f (K4): the notice is the default Return notice, which has no Approved column, so the two approved line amounts (800.00, 0.00) are not shown.
        var shownAmounts = projected.Except(["800.00", "0.00"]).ToArray();
        shown.Should().BeSubsetOf(projected, "no figure appears that the projection did not give");
        shown.Should().Contain(shownAmounts, "every projected billed amount is shown").And.NotContain(["800.00", "0.00"]);
        draft.Html.Should().Contain("Tracking number: TRK-1.2").And.Contain("Billed total: 9,876.54");
        draft.Html.Should().NotContain("Approved total", "an In Review invoice has no approved total");
        draft.Html.Should().Contain("2026-03-04").And.Contain("J. Smith").And.Contain("Research on the motion").And.Contain("Court filing fee");
        draft.Html.Should().Contain("Percent 10");
    }

    [Fact]
    public async Task ApprovedWithAdjustments_ShowsTheApprovedTotal()
    {
        var rig = new Rig();
        rig.Outcome = Of(FirmOutcomeKind.ApprovedWithAdjustments, Reduced());
        rig.AiReturns("Invoice approved with adjustments", "Your invoice has been approved with some adjustments.");

        var draft = await rig.ComposeAsync(FirmNoticeKind.ApprovedWithAdjustments);

        draft.Html.Should().Contain("Approved total: 9,000.00");
        rig.Requests.Should().ContainSingle().Which.Outcome.Should().Be(InvoiceFirmNoticeOutcome.ApprovedWithAdjustments);
    }

    [Fact]
    public async Task AClauseIsShownOnlyWhenTheProjectionCitesIt_WithItsLabelVersionAndSection()
    {
        var rig = new Rig();
        rig.Outcome = InReview(Reduced());

        var cited = await rig.ComposeAsync();

        cited.Html.Should().Contain("Daily cap rule").And.Contain("Acme Billing Guidelines, version 3, Section 4.2").And.Contain("&ldquo;" + Quote + "&rdquo;");

        // The projection withholds the citation (the rule set's flag is off): the notice has the line and the reason, and no rule text of any kind.
        rig.Outcome = InReview(Reduced() with { Citations = [] });
        var withheld = await rig.ComposeAsync();

        withheld.Html.Should().NotContain(Quote).And.NotContain("Daily cap rule").And.NotContain("Acme Billing Guidelines").And.NotContain("Section 4.2");
        withheld.Html.Should().Contain("Hours exceed the daily cap", "the firm reason is not a citation");
        withheld.Html.Should().Contain("Too many hours for this task");
    }

    [Fact]
    public async Task ARejectedSubmission_ShowsItsFindingsWithLineNumberValuesAndTheReasonCode()
    {
        var rig = new Rig();
        rig.Outcome = new FirmOutcome(
            FirmOutcomeKind.SubmissionRejected, "TRK-9", "Rejected", null, null,
            [
                new FirmOutcomeItem(
                    FirmOutcomeDecision.RejectionFinding, null, null, null, null, ["The hourly rate is above the approved rate"], [],
                    LineNumber: 7, ValuesRead: [new FirmOutcomeValueRead("Billed rate", "450.00")], ReasonCode: "approved_rate.above_approved"),
                new FirmOutcomeItem(FirmOutcomeDecision.RejectionFinding, null, null, null, null, ["The invoice repeats an earlier invoice"], []),
            ],
            false, NotShownCount: 3);
        rig.AiReturns("Invoice submission rejected", "Your invoice submission was not accepted for review.");

        var result = await rig.Composer().ComposeForSubmissionAsync(SubmissionId, invoiceDecided: true, CancellationToken.None);

        result.Status.Should().Be(FirmNoticeStatus.Composed);
        var html = result.Draft!.Html;
        html.Should().Contain("Line 7").And.Contain("Billed rate: 450.00").And.Contain("The hourly rate is above the approved rate");
        html.Should().Contain("The invoice repeats an earlier invoice");
        html.Should().Contain("3 more findings are not shown here.");
        html.Should().NotContain("approved_rate.above_approved", "the reason code is the projection's, not the firm's wording");
        rig.SubmissionReads.Should().Equal([(SubmissionId, true)]);
        rig.Requests.Single().Findings.Should().OnlyContain(f => f.Consequence == "RejectionFinding");
    }

    [Fact]
    public async Task TheDecidedFlag_IsPassedToTheProjectionUnchanged()
    {
        var rig = new Rig();
        rig.Outcome = new FirmOutcome(FirmOutcomeKind.SubmissionRejected, "TRK-9", "Rejected", null, null, [], false);

        await rig.Composer().ComposeForSubmissionAsync(SubmissionId, invoiceDecided: false, CancellationToken.None);

        rig.SubmissionReads.Should().Equal([(SubmissionId, false)]);
    }

    [Fact]
    public async Task ARejectedInvoice_WithNoItems_HasNoFindingsHeading_AndStillCarriesTheTotals()
    {
        var rig = new Rig();
        rig.Outcome = Of(FirmOutcomeKind.Rejected);
        rig.AiReturns("Invoice rejected", "Your invoice has been rejected in full.");

        var draft = await rig.ComposeAsync(FirmNoticeKind.InvoiceRejected);

        draft.Html.Should().NotContain(FirmNoticeFallbackText.FindingsHeading).And.NotContain("<table");
        draft.Html.Should().Contain("Billed total: 9,876.54").And.Contain("Tracking number: TRK-1.2");
    }

    // ═══════════════════════════════════════════════════════════════════════════════════════════
    // Prose is refused when it carries a figure; the template is used instead
    // ═══════════════════════════════════════════════════════════════════════════════════════════

    public static IEnumerable<object[]> BadProse() =>
    [
        ["Invoice returned", "Your invoice total of 1234 was reduced.", FirmNoticeFallbackReasons.ProseDigit],
        ["Invoice returned", "Line 4 was reduced.", FirmNoticeFallbackReasons.ProseDigit],
        ["Invoice 7 returned", "Your invoice was returned.", FirmNoticeFallbackReasons.ProseDigit],
        ["Invoice returned", "Reduced by ten percent.", FirmNoticeFallbackReasons.ProseAmountWord],
        ["Invoice returned", "A charge of five hundred was reduced.", FirmNoticeFallbackReasons.ProseAmountWord],
        ["Invoice returned", "See line three.", FirmNoticeFallbackReasons.ProseAmountWord],
        ["Invoice returned", "Reduced by € five.", FirmNoticeFallbackReasons.ProseCurrency],
        ["Invoice returned", "The amount is $five.", FirmNoticeFallbackReasons.ProseCurrency],
        ["Invoice returned", "Reduced by ٣ units.", FirmNoticeFallbackReasons.ProseDigit], // an Arabic-Indic digit
        ["Invoice returned", "Reduced by ½.", FirmNoticeFallbackReasons.ProseDigit], // a vulgar fraction
        ["Invoice returned", "Read the guidelines at https://example.org/guide.", FirmNoticeFallbackReasons.ProseLink],
        ["Invoice returned", "Write to billing@example.org.", FirmNoticeFallbackReasons.ProseLink],
        ["Invoice returned", "Your invoice‮was returned.", FirmNoticeFallbackReasons.ProseControlCharacter],
        ["Invoice returned", "   ", FirmNoticeFallbackReasons.ProseEmpty],
        ["  ", "Your invoice was returned.", FirmNoticeFallbackReasons.ProseEmpty],
        ["Invoice returned", new string('a', FirmNoticeProse.MaxProseLength + 1), FirmNoticeFallbackReasons.ProseTooLong],
        [new string('a', FirmNoticeProse.MaxSubjectLength + 1), "Your invoice was returned.", FirmNoticeFallbackReasons.ProseTooLong],
        ["Invoice\nreturned", "Your invoice was returned.", FirmNoticeFallbackReasons.ProseControlCharacter],

        // Review round 1 (F1-b): digits outside the BMP are two UTF-16 chars each; they are numbers all the same.
        ["Invoice returned", "Your invoice was reduced by \U0001D7CF\U0001D7D0\U0001D7D1.", FirmNoticeFallbackReasons.ProseDigit], // mathematical bold digits 1 2 3
        ["Invoice returned", "Line \U0001D7DC was reduced.", FirmNoticeFallbackReasons.ProseDigit], // a mathematical double-struck digit 4
        ["Invoice returned", "Your invoice was  returned.", FirmNoticeFallbackReasons.ProseControlCharacter], // private use
        ["Invoice returned", "Your invoice was ͸ returned.", FirmNoticeFallbackReasons.ProseControlCharacter], // unassigned
        ["Invoice returned", "Your invoice was \U000E0041 returned.", FirmNoticeFallbackReasons.ProseControlCharacter], // a tag character (format)

        // Review round 1 (F1-c): spelled-out figures, whole words, any case, after NFKC folding.
        ["Invoice returned", "Seven lines were reduced.", FirmNoticeFallbackReasons.ProseAmountWord],
        ["Invoice returned", "Twelve of your lines were not allowed.", FirmNoticeFallbackReasons.ProseAmountWord],
        ["Invoice returned", "A fifty hour charge was disallowed.", FirmNoticeFallbackReasons.ProseAmountWord],
        ["Invoice returned", "Half of the fees were not allowed.", FirmNoticeFallbackReasons.ProseAmountWord],
        ["Invoice returned", "The fees were reduced twice.", FirmNoticeFallbackReasons.ProseAmountWord],
        ["Invoice returned", "A dozen entries were not allowed.", FirmNoticeFallbackReasons.ProseAmountWord],
        ["Invoice returned", "The THIRD entry was reduced.", FirmNoticeFallbackReasons.ProseAmountWord],
        ["Invoice returned", "Reduced by twenty-five.", FirmNoticeFallbackReasons.ProseAmountWord],
        ["Invoice returned", "A charge of \U0001D42D\U0001D430\U0001D428 \U0001D421\U0001D42E\U0001D427\U0001D41D\U0001D42B\U0001D41E\U0001D41D was reduced.", FirmNoticeFallbackReasons.ProseAmountWord], // bold "two hundred"
        ["Invoice returned", "Reduced by ｔｗｏ.", FirmNoticeFallbackReasons.ProseAmountWord], // full-width "two"

        // Review round 1 (K4): any bare domain shape, not six top-level domains.
        ["Invoice returned", "Visit acme-billing.co.uk for details.", FirmNoticeFallbackReasons.ProseLink],
        ["Invoice returned", "See evilsite.xyz.", FirmNoticeFallbackReasons.ProseLink],
        ["Invoice returned", "Go to billing.example.firm for help.", FirmNoticeFallbackReasons.ProseLink],

        // Review round 2 (F1): a combining mark inside a word leaves NFKC as it was; the marks are removed before the word checks.
        ["Invoice returned", "Th́ree lines were reduced.", FirmNoticeFallbackReasons.ProseAmountWord],
        ["Invoice returned", "th́ree lines were reduced.", FirmNoticeFallbackReasons.ProseAmountWord],
        ["Invoice returned", "A charge of one huńdred was reduced.", FirmNoticeFallbackReasons.ProseAmountWord],
        ["Invoice returned", "Reduced by sévén.", FirmNoticeFallbackReasons.ProseAmountWord], // accents on precomposed letters
        ["Invoice returned", "Visit exaḿple.com for details.", FirmNoticeFallbackReasons.ProseLink],

        // Review round 2 (hardening): domains in any script and with ideographic dots.
        ["Invoice returned", "Visit пример.рф for details.", FirmNoticeFallbackReasons.ProseLink], // a Cyrillic domain
        ["Invoice returned", "Visit example。com for details.", FirmNoticeFallbackReasons.ProseLink], // ideographic full stop
        ["Invoice returned", "Visit example．com for details.", FirmNoticeFallbackReasons.ProseLink], // full-width full stop
        ["Invoice returned", "Visit example｡com for details.", FirmNoticeFallbackReasons.ProseLink], // halfwidth ideographic full stop

        // Review round 2 (hardening): zero, tens, plurals and the longer ordinals.
        ["Invoice returned", "Zero lines were allowed.", FirmNoticeFallbackReasons.ProseAmountWord],
        ["Invoice returned", "Tens of fees were reduced.", FirmNoticeFallbackReasons.ProseAmountWord],
        ["Invoice returned", "Hundreds of fees were reduced.", FirmNoticeFallbackReasons.ProseAmountWord],
        ["Invoice returned", "Thousands of fees were reduced.", FirmNoticeFallbackReasons.ProseAmountWord],
        ["Invoice returned", "Millions of fees were reduced.", FirmNoticeFallbackReasons.ProseAmountWord],
        ["Invoice returned", "Dozens of fees were reduced.", FirmNoticeFallbackReasons.ProseAmountWord],
        ["Invoice returned", "Charges from the twenties were reduced.", FirmNoticeFallbackReasons.ProseAmountWord],
        ["Invoice returned", "The eleventh entry was reduced.", FirmNoticeFallbackReasons.ProseAmountWord],
        ["Invoice returned", "The twentieth entry was reduced.", FirmNoticeFallbackReasons.ProseAmountWord],
        ["Invoice returned", "The ninetieth entry was reduced.", FirmNoticeFallbackReasons.ProseAmountWord],
        ["Invoice returned", "The hundredth entry was reduced.", FirmNoticeFallbackReasons.ProseAmountWord],
        ["Invoice returned", "The thousandth entry was reduced.", FirmNoticeFallbackReasons.ProseAmountWord],
    ];

    [Theory]
    [MemberData(nameof(BadProse))]
    public async Task ProseWithAFigureALinkOrAnInvisibleCharacter_IsRefused_AndTheTemplateIsUsed(string subject, string body, string reason)
    {
        var rig = new Rig();
        rig.AiReturns(subject, body);

        var draft = await rig.ComposeAsync();

        draft.Source.Should().Be(FirmNoticeSource.Template);
        draft.FallbackReason.Should().Be(reason);
        draft.ActionVersion.Should().BeNull();
        draft.Prose.Should().Be(FirmNoticeFallbackText.Prose[FirmNoticeKind.ReturnedToFirm]);
        draft.Subject.Should().Be("Invoice returned to firm (TRK-1.2)");
        if (body.Trim().Length > 0 && body.Length < 100)
        {
            draft.Html.Should().NotContain(body.Trim(), "the refused text is nowhere in the notice");
        }

        rig.Logger.Messages.Should().Contain(m => m.Contains(reason));
    }

    [Fact]
    public void ALoneSurrogate_IsRefused_NeverReadAsAReplacementCharacter()
    {
        // Not a theory row: a lone surrogate is not valid text, and a test runner may not carry it through a display name.
        FirmNoticeProse.Check("Invoice returned", "Your invoice was \uD800 returned.").Should().Be(FirmNoticeFallbackReasons.ProseControlCharacter);
        FirmNoticeProse.Check("Invoice \uDC00 returned", "Your invoice was returned.").Should().Be(FirmNoticeFallbackReasons.ProseControlCharacter);
        FirmNoticeProse.Check("Invoice returned", "Your invoice was returned.").Should().BeNull("the control: clean text passes");
    }

    [Fact]
    public void OrdinaryProseWithNoNumberWord_StillPasses_SoABroaderListCannotSilentlyRefuseEverything()
    {
        // Must-not-fire control for the screen: the wording the Action is told to write, with accents, another script and ordinary punctuation.
        foreach (var body in new[]
        {
            "Your invoice has been returned to you for correction. Some billed lines were reduced and some were not allowed under the billing guidelines that apply to your firm.\n\nA corrected invoice can be submitted when you are ready.",
            "We reviewed the invoice against the agreed rates. The adjustments are listed in the table below, with the reason for each one.",
            "Café résumé naïve. The invoice, e.g. the revised one, was rejected; please review the table.",
            "Привет, the invoice was returned.",
            "Th́e invoice was returned.", // a stray mark on an ordinary word is not a figure
        })
        {
            FirmNoticeProse.Check("Invoice returned for correction", body).Should().BeNull($"'{body[..Math.Min(30, body.Length)]}' is ordinary prose");
        }
    }

    [Fact]
    public void OneIsOrdinaryProse_ButAFigureWordWithinAWordIsNotAFigure()
    {
        // Whole words only: the screen is not a substring match, so ordinary words that contain a number word still pass.
        FirmNoticeProse.Check("Invoice returned", "Your invoice was returned for one reason: the stone fence tent was dependent on others.").Should().BeNull();
        FirmNoticeProse.Check("Invoice returned", "Your invoice was returned for another reason.").Should().BeNull();
    }

    [Fact]
    public async Task ATrackingNumberWithControlCharacters_IsCleanedInTheSubjectAndTheDetails()
    {
        var rig = new Rig();
        rig.Outcome = InReview(Reduced()) with { TrackingNumber = "TRK-1\r\nBcc: x\u0000​.2\t  3" };

        var draft = await rig.ComposeAsync();

        draft.Subject.Should().Be("Invoice returned for correction (TRK-1 Bcc: x.2 3)");
        draft.Subject.Should().NotContainAny("\r", "\n", "\u0000", "​", "\t");
        draft.Html.Should().Contain("Tracking number: TRK-1 Bcc: x.2 3").And.NotContain("​");
        FirmNoticeComposer.CleanTrackingNumber("  \u0007 ").Should().BeEmpty("nothing visible is left");
        FirmNoticeComposer.CleanTrackingNumber(null).Should().BeEmpty();
    }

    [Fact]
    public void TheRequest_HasOneFindingPerItem_NotACollapsedSet()
    {
        var outcome = InReview(Reduced(), Reduced(), Reduced());

        var request = FirmNoticeComposer.BuildRequest(FirmNoticeKind.ReturnedToFirm, outcome);

        request.Findings.Should().HaveCount(3, "identical findings are not collapsed, so the model is not led to say 'a line' for several");
    }

    [Fact]
    public async Task CleanProse_WithMarkupCharacters_IsHtmlEncoded_NeverInterpreted()
    {
        var rig = new Rig();
        rig.AiReturns("Invoice returned", "Your <b>invoice</b> was returned & closed.\n\n<script>alert('x')</script> ok");

        var draft = await rig.ComposeAsync();

        draft.Source.Should().Be(FirmNoticeSource.Action);
        draft.Html.Should().Contain("Your &lt;b&gt;invoice&lt;/b&gt; was returned &amp; closed.");
        draft.Html.Should().Contain("&lt;script&gt;alert(&#39;x&#39;)&lt;/script&gt; ok");
        draft.Html.Should().NotContain("<script").And.NotContain("<b>");
    }

    [Fact]
    public async Task APlaceholderWrittenInTheProse_IsText_NeverExpanded()
    {
        var rig = new Rig();
        rig.AiReturns("Invoice returned", "Your invoice was returned {{Findings}} {{Details}} {{Prose}}.");

        var draft = await rig.ComposeAsync();

        draft.Html.Should().Contain("{{Findings}} {{Details}} {{Prose}}", "one pass: a value is never rescanned for placeholders");
        Regex.Matches(draft.Html, "Tracking number:").Should().HaveCount(1, "the details block appears once, where the template puts it");
    }

    [Fact]
    public async Task PlaceholderTextInsideATableCell_IsAlsoText()
    {
        var rig = new Rig();
        rig.Outcome = InReview(Reduced() with { Reason = "see {{Details}} {{Prose}}" });

        var draft = await rig.ComposeAsync();

        draft.Html.Should().Contain("see {{Details}} {{Prose}}");
        Regex.Matches(draft.Html, "Tracking number:").Should().HaveCount(1);
    }

    [Fact]
    public async Task EveryValueInTheTableIsEncoded()
    {
        var rig = new Rig();
        var item = Reduced() with
        {
            Line = new FirmOutcomeLine(null, "<i>Jo</i>", "A & B \"quoted\"", 1m, 1m),
            Reason = "<img src=x onerror=alert(1)>",
            Citations = [new FirmOutcomeCitation("<b>Rule</b>", "<u>Label</u>", 1, "<s>Sec</s>", "<q>Quote</q>")],
        };
        rig.Outcome = InReview(item);

        var draft = await rig.ComposeAsync();

        draft.Html.Should().NotContain("<i>Jo").And.NotContain("<img").And.NotContain("<b>Rule").And.NotContain("<u>Label").And.NotContain("<s>Sec").And.NotContain("<q>Quote");
        draft.Html.Should().Contain("&lt;i&gt;Jo&lt;/i&gt;").And.Contain("A &amp; B &quot;quoted&quot;").And.Contain("&lt;img src=x onerror=alert(1)&gt;");
    }

    // ═══════════════════════════════════════════════════════════════════════════════════════════
    // The fixed template: every trigger
    // ═══════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task SettingOff_UsesTheTemplate_AndNeverCallsTheAction()
    {
        var rig = new Rig(aiEnabled: false);

        var draft = await rig.ComposeAsync();

        draft.Source.Should().Be(FirmNoticeSource.Template);
        draft.FallbackReason.Should().Be(FirmNoticeFallbackReasons.SettingOff);
        draft.Prose.Should().Be(FirmNoticeFallbackText.Prose[FirmNoticeKind.ReturnedToFirm]);
        rig.Ai.Verify(a => a.DraftFirmNoticeAsync(It.IsAny<InvoiceFirmNoticeRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ASettingsReadThatFaults_UsesTheTemplate_AndNeverCallsTheAction()
    {
        var rig = new Rig();
        rig.Settings.Setup(s => s.GetAsync(It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("dataverse down"));

        var draft = await rig.ComposeAsync();

        draft.FallbackReason.Should().Be(FirmNoticeFallbackReasons.SettingsUnavailable);
        rig.Ai.Verify(a => a.DraftFirmNoticeAsync(It.IsAny<InvoiceFirmNoticeRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task TheNullPeer_UsesTheTemplate_WithTheDisabledReason()
    {
        var logger = new CapturingLogger();
        var settings = new Mock<IEbillingSettingsReader>();
        settings.Setup(s => s.GetAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new EbillingSettings());
        var composer = new FirmNoticeComposer(
            (_, _) => Task.FromResult<FirmOutcome?>(InReview()), (_, _, _) => Task.FromResult<FirmOutcome?>(InReview()),
            settings.Object, new NullInvoiceAi(NullLogger<NullInvoiceAi>.Instance), logger);

        var result = await composer.ComposeForInvoiceAsync(FirmNoticeKind.InvoiceRejected, InvoiceId, CancellationToken.None);

        result.Status.Should().Be(FirmNoticeStatus.Composed, "an AI kill switch never makes the notice a 500");
        result.Draft!.Source.Should().Be(FirmNoticeSource.Template);
        result.Draft.FallbackReason.Should().Be(FirmNoticeFallbackReasons.AiDisabled);
        result.Draft.Prose.Should().Be(FirmNoticeFallbackText.Prose[FirmNoticeKind.InvoiceRejected]);
        result.Draft.Html.Should().Contain("Billed total: 9,876.54", "the template carries the same data");
    }

    public static IEnumerable<object[]> ActionFailures() =>
    [
        [new InvoiceAiException(InvoiceAiException.BindingMissing, ConsumerTypes.InvoiceFirmNotice, "no binding"), InvoiceAiException.BindingMissing],
        [new InvoiceAiException(InvoiceAiException.CompletionFailed, ConsumerTypes.InvoiceFirmNotice, "boom"), InvoiceAiException.CompletionFailed],
        [new InvoiceAiException(InvoiceAiException.OutputInvalid, ConsumerTypes.InvoiceFirmNotice, "bad json"), InvoiceAiException.OutputInvalid],
        [new FeatureDisabledException("ai.disabled", "kill switch engaged"), FirmNoticeFallbackReasons.AiDisabled],
        [new InvalidOperationException("anything else"), FirmNoticeFallbackReasons.AiError],
        [new TimeoutException("slow"), FirmNoticeFallbackReasons.AiError],
    ];

    [Theory]
    [MemberData(nameof(ActionFailures))]
    public async Task AnActionFailure_UsesTheTemplate_WithAStableReason(Exception failure, string reason)
    {
        var rig = new Rig();
        rig.AiThrows(failure);

        var draft = await rig.ComposeAsync(FirmNoticeKind.InvoiceRejected);

        draft.Source.Should().Be(FirmNoticeSource.Template);
        draft.FallbackReason.Should().Be(reason);
        draft.Prose.Should().Be(FirmNoticeFallbackText.Prose[FirmNoticeKind.InvoiceRejected]);
        rig.Logger.Messages.Should().NotContain(m => m.Contains(failure.Message), "a failure's message is never logged (ADR-015)");
    }

    [Fact]
    public async Task AnActionThatDoesNotAnswerInTime_UsesTheTemplate()
    {
        var rig = new Rig { AiTimeout = TimeSpan.FromMilliseconds(50) };
        rig.Ai.Setup(a => a.DraftFirmNoticeAsync(It.IsAny<InvoiceFirmNoticeRequest>(), It.IsAny<CancellationToken>()))
            .Returns(async (InvoiceFirmNoticeRequest _, CancellationToken token) =>
            {
                await Task.Delay(TimeSpan.FromSeconds(30), token);
                return Rig.Prose("late", "late");
            });

        var draft = await rig.ComposeAsync();

        draft.Source.Should().Be(FirmNoticeSource.Template);
        draft.FallbackReason.Should().Be(FirmNoticeFallbackReasons.AiTimeout);
    }

    [Fact]
    public async Task ACallerWhoCancels_IsNotAFallback_TheCancellationPropagates()
    {
        var rig = new Rig();
        using var cts = new CancellationTokenSource();
        rig.Ai.Setup(a => a.DraftFirmNoticeAsync(It.IsAny<InvoiceFirmNoticeRequest>(), It.IsAny<CancellationToken>()))
            .Returns(async (InvoiceFirmNoticeRequest _, CancellationToken token) =>
            {
                cts.Cancel();
                await Task.Delay(TimeSpan.FromSeconds(30), token);
                return Rig.Prose("late", "late");
            });

        var act = () => rig.Composer().ComposeForInvoiceAsync(FirmNoticeKind.ReturnedToFirm, InvoiceId, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    // ═══════════════════════════════════════════════════════════════════════════════════════════
    // Which states a notice describes
    // ═══════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ANoticeForARecordThatDoesNotExist_IsNotFound()
    {
        var rig = new Rig { Outcome = null };

        (await rig.Composer().ComposeForInvoiceAsync(FirmNoticeKind.ReturnedToFirm, InvoiceId, CancellationToken.None)).Status.Should().Be(FirmNoticeStatus.NotFound);
        (await rig.Composer().ComposeForSubmissionAsync(SubmissionId, true, CancellationToken.None)).Status.Should().Be(FirmNoticeStatus.NotFound);
        rig.Ai.Verify(a => a.DraftFirmNoticeAsync(It.IsAny<InvoiceFirmNoticeRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(FirmOutcomeKind.Unavailable, false)]
    [InlineData(FirmOutcomeKind.InvoiceInReview, true)]
    public async Task AFaultedProjection_IsUnavailable_NeverAPartialNotice(FirmOutcomeKind kind, bool degraded)
    {
        var rig = new Rig { Outcome = new FirmOutcome(kind, null, "x", null, null, [], Degraded: kind == FirmOutcomeKind.Unavailable || degraded) };

        var result = await rig.Composer().ComposeForInvoiceAsync(FirmNoticeKind.ReturnedToFirm, InvoiceId, CancellationToken.None);

        result.Status.Should().Be(FirmNoticeStatus.Unavailable);
        result.Draft.Should().BeNull();
        rig.Ai.Verify(a => a.DraftFirmNoticeAsync(It.IsAny<InvoiceFirmNoticeRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(FirmNoticeKind.ReturnedToFirm, FirmOutcomeKind.InvoiceInReview, true)]
    [InlineData(FirmNoticeKind.ReturnedToFirm, FirmOutcomeKind.ReturnedToFirm, true)]
    [InlineData(FirmNoticeKind.ReturnedToFirm, FirmOutcomeKind.Rejected, false)]
    [InlineData(FirmNoticeKind.ReturnedToFirm, FirmOutcomeKind.Paid, false)]
    [InlineData(FirmNoticeKind.ReturnedToFirm, FirmOutcomeKind.Closed, false)]
    [InlineData(FirmNoticeKind.InvoiceRejected, FirmOutcomeKind.InvoiceInReview, true)]
    [InlineData(FirmNoticeKind.InvoiceRejected, FirmOutcomeKind.Rejected, true)]
    [InlineData(FirmNoticeKind.InvoiceRejected, FirmOutcomeKind.ReturnedToFirm, false)]
    [InlineData(FirmNoticeKind.InvoiceRejected, FirmOutcomeKind.Approved, false)]
    [InlineData(FirmNoticeKind.ApprovedWithAdjustments, FirmOutcomeKind.ApprovedWithAdjustments, true)]
    [InlineData(FirmNoticeKind.ApprovedWithAdjustments, FirmOutcomeKind.Approved, false)]
    [InlineData(FirmNoticeKind.ApprovedWithAdjustments, FirmOutcomeKind.InvoiceInReview, false)]
    public async Task ANotice_DescribesOnlyTheStatesItsKindFits(FirmNoticeKind kind, FirmOutcomeKind outcome, bool applies)
    {
        var rig = new Rig { Outcome = Of(outcome) };

        var result = await rig.Composer().ComposeForInvoiceAsync(kind, InvoiceId, CancellationToken.None);

        result.Status.Should().Be(applies ? FirmNoticeStatus.Composed : FirmNoticeStatus.NotApplicable);
        (result.Draft is not null).Should().Be(applies);
    }

    [Theory]
    [InlineData(FirmOutcomeKind.SubmissionRejected, true)]
    [InlineData(FirmOutcomeKind.SubmissionInProgress, false)]
    [InlineData(FirmOutcomeKind.Approved, false)]
    public async Task ASubmissionNotice_IsForARejectedSubmissionOnly(FirmOutcomeKind outcome, bool applies)
    {
        var rig = new Rig { Outcome = Of(outcome) };

        var result = await rig.Composer().ComposeForSubmissionAsync(SubmissionId, true, CancellationToken.None);

        result.Status.Should().Be(applies ? FirmNoticeStatus.Composed : FirmNoticeStatus.NotApplicable);
    }

    [Fact]
    public async Task TheInvoiceOverload_RefusesTheSubmissionKind()
    {
        var rig = new Rig();

        var act = () => rig.Composer().ComposeForInvoiceAsync(FirmNoticeKind.SubmissionRejected, InvoiceId, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    // ═══════════════════════════════════════════════════════════════════════════════════════════
    // The fixed wording, the allow-list and the logs
    // ═══════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void TheFixedWording_HasOneSubjectAndOneParagraphPerKind_AndPassesItsOwnProseChecks()
    {
        var kinds = Enum.GetValues<FirmNoticeKind>();

        FirmNoticeFallbackText.Subjects.Keys.Should().BeEquivalentTo(kinds);
        FirmNoticeFallbackText.Prose.Keys.Should().BeEquivalentTo(kinds);
        foreach (var kind in kinds)
        {
            FirmNoticeProse.Check(FirmNoticeFallbackText.Subjects[kind], FirmNoticeFallbackText.Prose[kind]).Should().BeNull(
                "the fixed wording carries no figure, link or invisible character, so it meets the rule it enforces on the model");
            FirmNoticeFallbackText.Prose[kind].Should().NotContain("\n", "one short paragraph per kind");
        }

        Enum.GetNames<FirmNoticeKind>().Should().Equal(Enum.GetNames<InvoiceFirmNoticeOutcome>(), "the notice kind and the model's outcome are one list");
    }

    [Fact]
    public void TheNoticeConsumes_OnlyTheAllowListFieldsOfTheFirmOutcome()
    {
        // The notice takes only the fields of 130's allow-list DTO (model 7.4, R3, task 083). If a member is added to one of these types, this
        // test fails until the member is reviewed here: no override reason, Procedure name or id, rule-set name, document link or reviewer
        // identity may become something a notice could show.
        Names<FirmOutcome>().Should().BeEquivalentTo("Kind", "TrackingNumber", "StatusLabel", "BilledTotal", "ApprovedTotal", "Items", "Degraded", "NotShownCount");
        Names<FirmOutcomeItem>().Should().BeEquivalentTo(
            "Decision", "LineId", "Line", "Adjustment", "Reason", "FirmReasons", "Citations", "LineNumber", "ValuesRead", "ReasonCode");
        Names<FirmOutcomeLine>().Should().BeEquivalentTo("Date", "TimekeeperAsBilled", "DescriptionExcerpt", "BilledAmount", "ApprovedAmount");
        Names<FirmOutcomeAdjustment>().Should().BeEquivalentTo("Method", "Value", "Amount");
        Names<FirmOutcomeCitation>().Should().BeEquivalentTo("RuleName", "Label", "Version", "SectionReference", "Quote");
        Names<FirmOutcomeValueRead>().Should().BeEquivalentTo("Name", "Value");

        static IEnumerable<string> Names<T>() => typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.Name != "EqualityContract").Select(p => p.Name);
    }

    [Fact]
    public async Task EveryStringTheProjectionGives_IsInTheNotice_AndNothingElseIsAString_ThatTheNoticeInvents()
    {
        var rig = new Rig();
        var item = new FirmOutcomeItem(
            FirmOutcomeDecision.Reduced, Guid.Parse("31000000-0000-4000-8000-0000000000aa"),
            new FirmOutcomeLine(new DateOnly(2026, 5, 6), "SENT-timekeeper", "SENT-description", 11.11m, 22.22m),
            null, "SENT-reason", ["SENT-firmreason"],
            [new FirmOutcomeCitation("SENT-rule", "SENT-label", 9, "SENT-section", "SENT-quote")]);
        rig.Outcome = InReview(item);
        rig.AiReturns("Invoice returned", "Your invoice was returned.");

        var draft = await rig.ComposeAsync();

        foreach (var sentinel in new[] { "SENT-timekeeper", "SENT-description", "SENT-reason", "SENT-firmreason", "SENT-rule", "SENT-label", "SENT-section", "SENT-quote", "2026-05-06", "11.11", "version 9" })
        {
            draft.Html.Should().Contain(sentinel);
        }

        draft.Html.Should().NotContain("22.22", "130f (K4): the default Return notice has no Approved column, so the approved line amount is not shown");

        draft.Html.Should().NotContain("31000000-0000-4000-8000-0000000000aa", "no id of a line, record, rule set or Procedure is ever shown");
        // A hand-made outcome cannot carry an override reason or a Procedure name or id, so no assertion about them is made here; that proof runs through
        // the REAL projection in FirmFindingsProjectionTests.FirmNotice_NoOverrideReasonProcedureNameOrProcedureId_ReachesTheModelOrTheNotice (review round 1).
        draft.Html.ToLowerInvariant().Should().NotContain("http").And.NotContain("<a ").And.NotContain("<img");
    }

    [Fact]
    public async Task Logs_CarryIdsKindSourceAndReasonCodes_NeverProseOrFindings()
    {
        var rig = new Rig();
        rig.AiReturns("Secret subject words", "Secret prose words that mention 5 dollars.");
        rig.Outcome = InReview(Reduced() with { Reason = "SECRET-REVIEWER-REASON" });

        await rig.ComposeAsync();

        var logs = string.Join("\n", rig.Logger.Messages);
        logs.Should().Contain(InvoiceId.ToString()).And.Contain(FirmNoticeFallbackReasons.ProseDigit);
        logs.Should().NotContain("Secret").And.NotContain("SECRET-REVIEWER-REASON").And.NotContain(Quote).And.NotContain("TRK-1.2").And.NotContain("1,234.50");
        rig.Logger.Exceptions.Should().OnlyContain(e => e == null);
    }

    [Fact]
    public void TheTemplateIsEmbedded_AndNamesOnlyKnownPlaceholders()
    {
        var html = FirmNoticeComposer.RenderHtml(Of(FirmOutcomeKind.Rejected), "Your invoice was rejected.");

        html.Should().NotContain("{{", "every placeholder of the template was substituted");
        html.Should().Contain("Hello,").And.Contain("Your invoice was rejected.");
    }

    [Fact]
    public void ReviewerEditedProse_RendersAgainstTheSameData()
    {
        var html = FirmNoticeComposer.RenderHtml(InReview(), "First paragraph.\r\n\r\nSecond paragraph\nwith a line break.");

        html.Should().Contain(">First paragraph.</p>").And.Contain("Second paragraph<br />with a line break.");
        html.Should().Contain("Billed total: 9,876.54");
    }

    // ── Helpers ──

    /// <summary>The text a reader sees: the markup removed and the entities decoded.</summary>
    private static string VisibleText(string html) =>
        System.Net.WebUtility.HtmlDecode(Regex.Replace(html, "<[^>]*>", " "));
}
