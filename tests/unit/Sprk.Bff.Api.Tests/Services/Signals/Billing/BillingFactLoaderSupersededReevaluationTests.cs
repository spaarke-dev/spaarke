using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Sprk.Bff.Api.Services.Finance;
using Sprk.Bff.Api.Services.Signals;
using Sprk.Bff.Api.Services.Signals.Billing;
using Sprk.Bff.Api.Tests.Services.Finance;
using Xunit;
using W = Sprk.Bff.Api.Tests.Services.Finance.CorrectionWorld;

namespace Sprk.Bff.Api.Tests.Services.Signals.Billing;

/// <summary>
/// ebilling-r1 task 097c (097b review finding 2, its acceptance item): once a correction's commit has superseded the old invoice, a field
/// correction on the NEW invoice (101's <see cref="InvoiceCorrectionReevaluation"/>) is judged against the priors 072's real loader reads, and
/// the superseded invoice, which keeps the same firm invoice number (D-37), is no prior: no Duplicate. The re-evaluation names no replaced
/// invoice, so 097a's mapping (<c>BillingFactLoader.StatusOf</c>: Superseded is no prior) is what keeps it out.
/// </summary>
public partial class BillingFactLoaderTests
{
    [Theory(DisplayName = "097c: a field correction on the corrected invoice raises no Duplicate against the invoice it superseded (must fire); an In Review invoice of the same number still does (must not fire)")]
    [InlineData(InvoiceStatusCodes.Superseded, false)]
    [InlineData(InvoiceStatusCodes.InReview, true)]
    public async Task FieldCorrection_OnTheCorrectedInvoice_IsNoDuplicateOfTheSupersededOne(int oldStatus, bool duplicate)
    {
        DuplicateRule();
        _world.OrdinaryRoot("sprk_project", W.Core);
        PriorWithInvoice("INV-1001.1", ("sprk_project", "sprk_project", W.Core), invoiceStatus: oldStatus, firm: W.Firm, firmInvoiceNumber: "INV-1001");

        var correction = new W();
        InvoiceCorrectionReevaluationTests.Store(correction);
        var requests = new List<BillingEvaluationRequest>();
        var priors = new List<int>();
        var results = new List<BillingEvaluationResult>();
        var evaluator = Evaluator();
        var reevaluation = new InvoiceCorrectionReevaluation(
            correction.Entities.Object,
            async (request, ct) =>
            {
                requests.Add(request);
                var loaded = await Loader().LoadAsync(request, ct);
                priors.Add(loaded.PriorSubmissions.Count);
                return loaded;
            },
            (request, loaded) =>
            {
                var result = evaluator.Evaluate(request, loaded);
                results.Add(result);
                return result;
            },
            correction.WriteSignalAsync, correction.CloseSignalAsync, NullLogger<InvoiceCorrectionReevaluation>.Instance);
        correction.Seam = reevaluation.ApplyAsync;

        var applied = await correction.Service().CorrectAsync(
            new CorrectionCommand(W.InvoiceId, W.FeeLineId, "Hours", "2.5", false, W.Reviewer), CancellationToken.None);

        applied.IsApplied.Should().BeTrue();
        requests.Should().NotBeEmpty().And.OnlyContain(r => r.ReplacedInvoiceId == null && r.FirmId == W.Firm && r.FirmInvoiceNumber == "INV-1001",
            "the re-evaluation names no replaced invoice: the loader's own status mapping decides");
        results.Should().NotBeEmpty();
        if (duplicate)
        {
            priors.Should().OnlyContain(count => count == 1);
            results.Should().OnlyContain(r => r.Findings.Any(f => f.RuleType == RuleType.Duplicate && f.ReasonCode == BillingReasonCodes.DuplicateInvoiceNumber));
        }
        else
        {
            priors.Should().OnlyContain(count => count == 0, "a Superseded invoice is no prior");
            results.Should().OnlyContain(r => !r.Findings.Any(f => f.RuleType == RuleType.Duplicate));
        }
    }
}
