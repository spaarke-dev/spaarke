using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using Sprk.Bff.Api.Services.Finance;
using Sprk.Bff.Api.Services.Finance.Submission;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Finance.Submission;

/// <summary>
/// ebilling-r1 task 097f (EB-D-180): the read-time note (<see cref="SupersededSubmissionNote"/>) at the seams the routes cannot reach: a
/// gate that faults, and the exact instances it hands back. The route-level cases are in <c>InvoiceSubmissionEndpointsContractTests</c>.
/// </summary>
public sealed class SupersededSubmissionNoteTests
{
    private static readonly Guid OldSubmission = Guid.Parse("097f0000-0000-4000-8000-0000000000a1");
    private static readonly Guid OldInvoice = Guid.Parse("097f0000-0000-4000-8000-0000000000a2");
    private static readonly Guid NewSubmission = Guid.Parse("097f0000-0000-4000-8000-0000000000a3");
    private static readonly Guid NewInvoice = Guid.Parse("097f0000-0000-4000-8000-0000000000a4");

    private readonly InvoiceSubmissionServiceTests.Harness _h = new();

    private InvoiceSubmissionView Accepted(Guid? invoice = null, string status = "Accepted") => new()
    {
        SubmissionId = OldSubmission,
        Status = status,
        TrackingNumber = "INV-1.1",
        InvoiceId = invoice ?? OldInvoice,
    };

    private void Seed()
    {
        _h.Invoices[OldInvoice] = new Entity("sprk_invoice", OldInvoice) { ["statuscode"] = new OptionSetValue(InvoiceStatusCodes.Superseded) };
        _h.Invoices[NewInvoice] = new Entity("sprk_invoice", NewInvoice) { ["statuscode"] = new OptionSetValue(InvoiceStatusCodes.InReview) };
        _h.PriorSubmissions.Add(new Entity("sprk_invoicesubmission", NewSubmission)
        {
            ["sprk_trackingnumber"] = "INV-1.2",
            ["sprk_processingstatus"] = new OptionSetValue((int)InvoiceSubmissionStatus.Accepted),
            [InvoiceSubmissionService.InvoiceColumn] = new EntityReference("sprk_invoice", NewInvoice),
            [InvoiceSubmissionService.ResubmissionOfColumn] = new EntityReference("sprk_invoicesubmission", OldSubmission),
        });
    }

    private static readonly ReplacementReadGate AllowAll =
        (replacements, _) => Task.FromResult<IReadOnlySet<Guid>>(replacements.Select(r => r.SubmissionId).ToHashSet());

    private Task<IReadOnlyList<InvoiceSubmissionView>> ApplyAsync(IReadOnlyList<InvoiceSubmissionView> views, ReplacementReadGate gate, ILogger? logger = null) =>
        SupersededSubmissionNote.ApplyAsync(_h.Entities.Object, logger ?? NullLogger.Instance, views, gate, CancellationToken.None);

    [Fact(DisplayName = "097f: a Superseded invoice's Accepted submission gets the reason and the correcting submission (id and tracking number)")]
    public async Task Superseded_GetsTheNote()
    {
        Seed();

        var shown = (await ApplyAsync([Accepted()], AllowAll)).Single();

        shown.Status.Should().Be("Accepted");
        shown.OutcomeReasonCode.Should().Be(SubmissionReasonCodes.ReplacedByCorrection);
        shown.ReplacedBy.Should().Be(new SubmissionReplacement(NewSubmission, "INV-1.2"));
        shown.TrackingNumber.Should().Be("INV-1.1");
    }

    [Fact(DisplayName = "097f (deliberate): a gate that faults leaves the reason and no replacedBy, logs a Warning, and does not throw")]
    public async Task GateFault_KeepsTheReason_NamesNothing_AndDoesNotThrow()
    {
        Seed();
        var logger = new LevelLogger();

        var shown = (await ApplyAsync([Accepted()],
            (_, _) => throw new TimeoutException("rights read faulted"), logger)).Single();

        shown.OutcomeReasonCode.Should().Be(SubmissionReasonCodes.ReplacedByCorrection);
        shown.ReplacedBy.Should().BeNull();
        logger.Levels.Should().Contain(LogLevel.Warning);
    }

    [Fact(DisplayName = "097f: a gate that names no id names nothing; a gate is asked only about replacements that exist")]
    public async Task GateThatNamesNone_NamesNone()
    {
        Seed();
        var asked = new List<Guid>();

        var shown = (await ApplyAsync([Accepted()], (replacements, _) =>
        {
            asked.AddRange(replacements.Select(r => r.SubmissionId));
            return Task.FromResult<IReadOnlySet<Guid>>(new HashSet<Guid>());
        })).Single();

        asked.Should().Equal(NewSubmission);
        shown.ReplacedBy.Should().BeNull();
        shown.OutcomeReasonCode.Should().Be(SubmissionReasonCodes.ReplacedByCorrection);
    }

    [Fact(DisplayName = "097f: a Superseded invoice with no correcting submission found has the reason and no replacedBy, and the gate is not asked")]
    public async Task NoReplacementFound_HasTheReason_AndTheGateIsNotAsked()
    {
        Seed();
        _h.PriorSubmissions.Clear();
        var gateCalls = 0;

        var shown = (await ApplyAsync([Accepted()], (r, _) =>
        {
            gateCalls++;
            return Task.FromResult<IReadOnlySet<Guid>>(new HashSet<Guid>());
        })).Single();

        shown.OutcomeReasonCode.Should().Be(SubmissionReasonCodes.ReplacedByCorrection);
        shown.ReplacedBy.Should().BeNull();
        gateCalls.Should().Be(0);
    }

    [Fact(DisplayName = "097f: views that are not Accepted, or have no invoice, or whose invoice is not Superseded come back as the same instances, with no read for the first two")]
    public async Task OtherViews_AreTheSameInstances()
    {
        Seed();
        var rejected = Accepted(status: "Rejected");
        var noInvoice = new InvoiceSubmissionView { SubmissionId = Guid.NewGuid(), Status = "Accepted" };
        var views = new[] { rejected, noInvoice };

        var shown = await ApplyAsync(views, AllowAll);

        shown.Should().BeSameAs(views);
        _h.SupersededInvoiceReads.Should().BeEmpty("nothing to ask about");

        _h.Invoices[OldInvoice]["statuscode"] = new OptionSetValue(InvoiceStatusCodes.InReview);
        var inReview = new[] { Accepted() };
        (await ApplyAsync(inReview, AllowAll)).Should().BeSameAs(inReview);
    }

    private sealed class LevelLogger : ILogger
    {
        public List<LogLevel> Levels { get; } = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Levels.Add(logLevel);
    }
}
