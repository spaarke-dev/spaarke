using FluentAssertions;
using Microsoft.Extensions.Logging;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Services.Finance.Submission;
using Xunit;

namespace Sprk.Bff.Api.Tests.Infrastructure.ExternalAccess;

/// <summary>
/// ebilling-r1 task 165c: visibility counts only a submission's own invoice (<see cref="InvoiceBlockedHold.IsOwnSubmission"/>, the one rule).
/// Reader 1 (<see cref="ExternalInvoiceVisibility.DecideSubmissionsAsync"/>, the routes and the assistant tools): a submission whose invoice
/// link is not its own invoice is withheld and does not take the linked invoice's verdict. Reader 2 (the invoice predicate's submission
/// read): such a row adds nothing to the linked invoice. Each logs one event with the two ids and its code only.
/// </summary>
public sealed partial class ExternalInvoiceVisibilityTests
{
    private readonly EventLog _events = new();

    private Task<InvoiceVisibilityResult> DecideSubmissionsLogged(string caller, params InvoiceSubmissionView[] submissions) =>
        _world.NewService(_identity.Object, _events).HideSubmissionsForAsync(Caller(caller), submissions, _world.RootFlags);

    private Task<InvoiceVisibilityResult> DecideLogged(string caller, params Guid[] invoices) =>
        _world.NewService(_identity.Object, _events).HideInvoicesForAsync(Caller(caller), invoices, _world.RootFlags);

    private static string NotOwnCode => $"{InvoiceVisibilityResult.CodeOf(InvoiceHiddenReason.InvoiceLinkNotOwn)}=1";

    // ── Reader 1: the submission decision ─────────────────────────────────────────────────────────────────────────────

    [Theory(DisplayName = "165c reader 1 (seed: the own-invoice check dropped from DecideSubmissionsAsync): a submission linked to an invoice that is not its own is withheld, and that invoice is not asked")]
    [MemberData(nameof(ExternalCallers))]
    public async Task Reader1_ALinkThatIsNotItsOwnInvoice_IsWithheld_AndTheInvoiceIsNotAsked(string caller)
    {
        var linked = _world.NewInvoice();
        _world.SpaUpload(linked); // a visible invoice with its own submission
        var submission = Guid.NewGuid();
        var isolated = _world.Document(Guid.NewGuid(), permission: Restricted, matter: CleanMatter());

        var decision = await DecideSubmissionsLogged(caller,
            SubmissionView(submission, InvoiceWorld.InternalChannel, isolated, linked, InvoiceSubmissionStatus.Accepted));

        decision.IsVisible(submission).Should().BeFalse();
        decision.HiddenSummary().Should().Be(NotOwnCode);
        decision.Faulted.Should().BeFalse();
        _world.Reads("sprk_storedfiles").Should().BeEmpty("the linked invoice is not decided for it");
        _world.Reads("sprk_invoicesubmissions").Should().BeEmpty();
        _events.NotOwn.Should().ContainSingle().Which.Should().Be((submission, linked));
    }

    [Fact(DisplayName = "165c reader 1: the assistant tools decide a submission row linked to an invoice that is not its own as the routes do (hidden)")]
    public async Task Reader1_TheAssistantTools_HideARowLinkedToAnInvoiceThatIsNotItsOwn()
    {
        var linked = _world.NewInvoice();
        var own = _world.SpaUpload(linked);
        var other = _world.Submission(linked, document: null, channel: InvoiceWorld.SpaChannel, id: Guid.NewGuid());

        var hidden = await _world.NewService(_identity.Object, _events)
            .HideInvoiceFamilyRowsAsync("sprk_invoicesubmission", new[] { own, other }, _world.RootFlags);

        hidden.Should().BeEquivalentTo(new[] { other }, "control: the invoice's own submission takes its visible invoice's verdict");
        _events.NotOwn.Should().Contain((other, linked)).And.NotContain(e => e.Submission == own);
    }

    public static TheoryData<string, string> Reader1Unchanged()
    {
        var data = new TheoryData<string, string>();
        foreach (var caller in new[] { "contact", "flagged-systemuser" })
        {
            data.Add(caller, "agreeing-link");
            data.Add(caller, "unlinked-spa");
            data.Add(caller, "unlinked-internal");
        }

        return data;
    }

    [Theory(DisplayName = "165c reader 1 must not fire: an agreeing link, an unlinked Spa and an unlinked Internal submission are decided as before, with no event")]
    [MemberData(nameof(Reader1Unchanged))]
    public async Task Reader1_AnAgreeingLink_OrNoLink_IsDecidedAsBefore(string caller, string shape)
    {
        var readable = _world.Document(Guid.NewGuid(), permission: Standard, matter: CleanMatter());
        // Task 165f: the Internal row naming a document is the one derived from it, as the confirm route creates it.
        var submission = shape == "unlinked-internal" ? InvoiceWorld.SubmissionFor(readable) : Guid.NewGuid();
        var view = shape switch
        {
            "agreeing-link" => SubmissionView(submission, InvoiceWorld.SpaChannel,
                invoice: InvoiceSubmissionPersister.InvoiceIdFor(submission), status: InvoiceSubmissionStatus.Accepted),
            "unlinked-spa" => SubmissionView(submission, InvoiceWorld.SpaChannel),
            "unlinked-internal" => SubmissionView(submission, InvoiceWorld.InternalChannel, readable),
            _ => throw new ArgumentOutOfRangeException(nameof(shape)),
        };
        if (shape == "agreeing-link")
            _world.SpaUpload(view.InvoiceId!.Value, submission);

        var decision = await DecideSubmissionsLogged(caller, view);

        decision.IsVisible(submission).Should().BeTrue(decision.HiddenSummary());
        _events.NotOwn.Should().BeEmpty();
    }

    // ── Reader 2: the invoice predicate's submission read ───────────────────────────────────────────────────────────

    public static TheoryData<string, string> Reader2NotOwnShapes()
    {
        var data = new TheoryData<string, string>();
        foreach (var caller in new[] { "contact", "flagged-systemuser" })
        {
            data.Add(caller, "restricted-document");
            data.Add(caller, "empty-link");
            data.Add(caller, "unknown-channel");
        }

        return data;
    }

    [Theory(DisplayName = "165c reader 2 (seed: the own-invoice check dropped from ReadSubmissionSourcesAsync): a submission linked to an invoice that is not its own adds nothing to it; its document is not a source")]
    [MemberData(nameof(Reader2NotOwnShapes))]
    public async Task Reader2_ARowLinkedToAnInvoiceThatIsNotItsOwn_AddsNothingToIt(string caller, string shape)
    {
        var invoice = _world.NewInvoice();
        // Its own submission, Spa with no document and no Stored File: visible by that row alone, so an empty Internal link counted
        // against the invoice would hide it (no InvoicePdf to rely on), and the visibility assertion below proves every shape.
        _world.Submission(invoice, document: null, InvoiceWorld.SpaChannel);
        var restricted = _world.Document(Guid.NewGuid(), permission: Restricted, matter: CleanMatter());
        var other = shape switch
        {
            "restricted-document" => _world.Submission(invoice, restricted, InvoiceWorld.InternalChannel, id: Guid.NewGuid()),
            "empty-link" => _world.Submission(invoice, document: null, InvoiceWorld.InternalChannel, id: Guid.NewGuid()),
            "unknown-channel" => _world.Submission(invoice, restricted, channel: 999, id: Guid.NewGuid()),
            _ => throw new ArgumentOutOfRangeException(nameof(shape)),
        };

        var decision = await DecideLogged(caller, invoice);

        decision.IsVisible(invoice).Should().BeTrue(decision.HiddenSummary());
        _world.Reads("sprk_documents").Should().BeEmpty("the row's document is not a source of the invoice");
        _events.NotOwn.Should().ContainSingle().Which.Should().Be((other, invoice));
    }

    [Theory(DisplayName = "165c reader 2 must not fire: the invoice's own submission still decides it (its Restricted document hides the invoice), with no event")]
    [MemberData(nameof(ExternalCallers))]
    public async Task Reader2_TheOwnSubmissionsDocument_StillDecidesTheInvoice(string caller)
    {
        var invoice = _world.NewInvoice();
        _world.Submission(invoice, _world.Document(_world.OwnDocumentOf(invoice), permission: Restricted, matter: CleanMatter()));

        var decision = await DecideLogged(caller, invoice);

        decision.IsVisible(invoice).Should().BeFalse();
        decision.HiddenSummary().Should().Be($"{InvoiceVisibilityResult.CodeOf(InvoiceHiddenReason.SourceHidden)}=1");
        _events.NotOwn.Should().BeEmpty();
    }

    [Fact(DisplayName = "165c reader 2: a row whose own id cannot be read cannot be shown to be the invoice's own, so the invoice is hidden (fail closed)")]
    public async Task Reader2_ARowWhoseIdCannotBeRead_HidesTheInvoice()
    {
        var invoice = _world.NewInvoice();
        var own = _world.SpaUpload(invoice);
        _world.SetRow("sprk_invoicesubmissions", "sprk_invoicesubmissionid", own, "sprk_invoicesubmissionid", "not-a-guid");

        var decision = await DecideLogged("contact", invoice);

        decision.IsVisible(invoice).Should().BeFalse();
        decision.HiddenSummary().Should().Be($"{InvoiceVisibilityResult.CodeOf(InvoiceHiddenReason.SourceLinkUnreadable)}=1");
    }

    // ── The event ───────────────────────────────────────────────────────────────────────────────────────────────

    [Fact(DisplayName = "165c: the event carries only its code and the two ids")]
    public async Task TheEvent_CarriesOnlyItsCodeAndTheIds()
    {
        var linked = _world.NewInvoice();
        _world.SpaUpload(linked);
        var submission = Guid.NewGuid();

        await DecideSubmissionsLogged("contact", SubmissionView(submission, InvoiceWorld.SpaChannel, invoice: linked));

        var entry = _events.Entries.Should().ContainSingle(e => e.Values.ContainsKey("EventCode")).Subject;
        entry.Level.Should().Be(LogLevel.Warning);
        entry.Values.Keys.Should().BeEquivalentTo("EventCode", "SubmissionId", "InvoiceId", "{OriginalFormat}");
        entry.Values["EventCode"].Should().Be(ExternalInvoiceVisibility.InvoiceLinkNotOwnEvent);
        entry.Values["SubmissionId"].Should().Be(submission);
        entry.Values["InvoiceId"].Should().Be(linked);
    }

    [Fact(DisplayName = "165c: one decision call logs a pair once at Warning; the invoice read of the same call logs it again only at Debug")]
    public async Task TheEvent_IsOneWarningPerPairPerCall()
    {
        var linked = _world.NewInvoice();
        var own = _world.SpaUpload(linked);
        var other = _world.Submission(linked, document: null, channel: InvoiceWorld.SpaChannel, id: Guid.NewGuid());

        // Both rows on one page: reader 1 withholds the other row, and the own row's invoice read (reader 2) meets the other row again.
        var decision = await DecideSubmissionsLogged("contact",
            SubmissionView(own, InvoiceWorld.SpaChannel, invoice: linked, status: InvoiceSubmissionStatus.Accepted),
            SubmissionView(other, InvoiceWorld.SpaChannel, invoice: linked, status: InvoiceSubmissionStatus.Accepted));

        decision.IsVisible(own).Should().BeTrue(decision.HiddenSummary());
        decision.IsVisible(other).Should().BeFalse();
        _world.Reads("sprk_invoicesubmissions").Should().NotBeEmpty("the own row's invoice is decided, which reads its submissions");
        _events.NotOwnAt(LogLevel.Warning).Should().ContainSingle().Which.Should().Be((other, linked));
        _events.NotOwnAt(LogLevel.Debug).Should().ContainSingle().Which.Should().Be((other, linked));
    }

    [Fact(DisplayName = "165c pass 2: an invoice decision on its own logs a row linked to an invoice that is not its own at Information (the submission reader owns the Warning)")]
    public async Task TheEvent_OnAnInvoiceDecisionAlone_IsInformation()
    {
        var invoice = _world.NewInvoice();
        _world.SpaUpload(invoice);
        var other = _world.Submission(invoice, document: null, InvoiceWorld.SpaChannel, id: Guid.NewGuid());

        await DecideLogged("contact", invoice);

        _events.NotOwnAt(LogLevel.Information).Should().ContainSingle().Which.Should().Be((other, invoice));
        _events.NotOwnAt(LogLevel.Warning).Should().BeEmpty();
        _events.NotOwnAt(LogLevel.Debug).Should().BeEmpty();
    }

    /// <summary>Captures each log entry's level and structured values.</summary>
    private sealed class EventLog : ILogger<ExternalDataService>
    {
        public List<(LogLevel Level, IReadOnlyDictionary<string, object?> Values)> Entries { get; } = new();

        /// <summary>The (submission, invoice) pairs of the 165c event, at any level.</summary>
        public IReadOnlyList<(Guid Submission, Guid Invoice)> NotOwn => NotOwnWhere(_ => true);

        /// <summary>The (submission, invoice) pairs of the 165c event logged at <paramref name="level"/>.</summary>
        public IReadOnlyList<(Guid Submission, Guid Invoice)> NotOwnAt(LogLevel level) => NotOwnWhere(l => l == level);

        /// <summary>Task 165e: the (submission, invoice) pairs of the own-submission-missing event, at any level (no submission id: the invoice read's).</summary>
        public IReadOnlyList<(Guid? Submission, Guid Invoice)> OwnMissing => OwnMissingWhere(_ => true);

        /// <summary>Task 165e: the same, logged at <paramref name="level"/>.</summary>
        public IReadOnlyList<(Guid? Submission, Guid Invoice)> OwnMissingAt(LogLevel level) => OwnMissingWhere(l => l == level);

        private IReadOnlyList<(Guid? Submission, Guid Invoice)> OwnMissingWhere(Func<LogLevel, bool> level)
        {
            lock (Entries)
            {
                return Entries
                    .Where(e => level(e.Level)
                        && e.Values.TryGetValue("EventCode", out var code) && Equals(code, ExternalInvoiceVisibility.OwnSubmissionMissingEvent))
                    .Select(e => (e.Values.TryGetValue("SubmissionId", out var s) ? (Guid?)s : null, (Guid)e.Values["InvoiceId"]!))
                    .ToList();
            }
        }

        /// <summary>Task 165f: the (submission, document) pairs of the document-link-not-own event, at any level.</summary>
        public IReadOnlyList<(Guid Submission, Guid Document)> DocumentNotOwn => DocumentNotOwnWhere(_ => true);

        /// <summary>Task 165f: the same, logged at <paramref name="level"/>.</summary>
        public IReadOnlyList<(Guid Submission, Guid Document)> DocumentNotOwnAt(LogLevel level) => DocumentNotOwnWhere(l => l == level);

        private IReadOnlyList<(Guid Submission, Guid Document)> DocumentNotOwnWhere(Func<LogLevel, bool> level)
        {
            lock (Entries)
            {
                return Entries
                    .Where(e => level(e.Level)
                        && e.Values.TryGetValue("EventCode", out var code) && Equals(code, ExternalInvoiceVisibility.DocumentLinkNotOwnEvent))
                    .Select(e => ((Guid)e.Values["SubmissionId"]!, (Guid)e.Values["DocumentId"]!))
                    .ToList();
            }
        }

        private IReadOnlyList<(Guid Submission, Guid Invoice)> NotOwnWhere(Func<LogLevel, bool> level)
        {
            lock (Entries)
            {
                return Entries
                    .Where(e => level(e.Level)
                        && e.Values.TryGetValue("EventCode", out var code) && Equals(code, ExternalInvoiceVisibility.InvoiceLinkNotOwnEvent))
                    .Select(e => ((Guid)e.Values["SubmissionId"]!, (Guid)e.Values["InvoiceId"]!))
                    .ToList();
            }
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            var values = state is IEnumerable<KeyValuePair<string, object?>> pairs
                ? pairs.ToDictionary(p => p.Key, p => p.Value)
                : new Dictionary<string, object?>();
            lock (Entries) Entries.Add((logLevel, values));
        }
    }
}
