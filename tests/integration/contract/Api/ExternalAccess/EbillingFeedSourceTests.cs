using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using Sprk.Bff.Api.Api.ExternalAccess;
using Sprk.Bff.Api.Infrastructure.DI;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Services.Finance;
using Xunit;

namespace Sprk.Bff.Api.Tests.Api.ExternalAccess;

/// <summary>
/// ebilling-r1 task 132 (FR-39, NFR-10, EB-D-207): the eBilling source of the external notification feed. The per-item invoice rule is the outcome
/// route's own (its decisions have their own suite, <c>FirmOutcomeScopeTests</c>); here it is a double, so these cases prove the SOURCE never lets
/// through what the rule did not clear, fails closed on a fault, and flags what it could not decide. Also pins the rendered column set (EB-D-207).
/// </summary>
public sealed class EbillingFeedSourceTests
{
    private static readonly DateTimeOffset Cutoff = new(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly Guid FirmA = Guid.Parse("13200000-0000-4000-8000-0000000000a1");

    private static CallerPrincipal Contact(Guid? contactId = null, Guid? systemUser = null, bool unreadable = false) => new()
    {
        Plane = CallerPrincipalPlane.CiamContact,
        ContactId = contactId ?? Guid.NewGuid(),
        SystemUserId = systemUser,
        ContactUnreadable = unreadable,
        ProjectAccess = [],
    };

    private static Entity Candidate(Guid id, int status, DateTime? modifiedOn = null) => new("sprk_invoice", id)
    {
        ["statuscode"] = new OptionSetValue(status),
        ["modifiedon"] = modifiedOn ?? new DateTime(2026, 10, 10, 9, 0, 0, DateTimeKind.Utc),
    };

    private static FirmOutcome Outcome(FirmOutcomeKind kind, bool degraded = false) =>
        new(kind, "TRK-1", "Status", 100m, 90m, [], degraded, 0);

    private sealed class Rig
    {
        public List<Entity> Candidates { get; } = [];
        public Dictionary<Guid, InvoiceFeedDecision> Decisions { get; } = [];
        public HashSet<Guid> Throwing { get; } = [];
        public Dictionary<Guid, FirmOutcome?> Outcomes { get; } = [];
        public List<Guid> Decided { get; } = [];
        public List<Guid> OutcomeReads { get; } = [];
        public IReadOnlyList<Guid> Firms { get; set; } = [FirmA];
        public bool FirmsUnreadable { get; set; }
        public bool FirmsThrow { get; set; }
        public bool CandidatesThrow { get; set; }
        public int FirmReads { get; private set; }
        public int CandidateReads { get; private set; }
        public IReadOnlyList<Guid>? FirmsQueried { get; private set; }

        public EbillingFeedSource Build() => new(
            (_, _) =>
            {
                FirmReads++;
                return FirmsThrow
                    ? Task.FromException<(IReadOnlyList<Guid>, bool)>(new InvalidOperationException("firms down"))
                    : Task.FromResult((Firms, FirmsUnreadable));
            },
            (firms, _, _) =>
            {
                CandidateReads++;
                FirmsQueried = firms;
                return CandidatesThrow
                    ? Task.FromException<IReadOnlyList<Entity>>(new InvalidOperationException("candidates down"))
                    : Task.FromResult<IReadOnlyList<Entity>>(Candidates.ToList());
            },
            (_, _, invoice, _) =>
            {
                Decided.Add(invoice);
                return Throwing.Contains(invoice)
                    ? Task.FromException<InvoiceFeedDecision>(new InvalidOperationException("decision down"))
                    : Task.FromResult(Decisions.GetValueOrDefault(invoice, InvoiceFeedDecision.Denied));
            },
            (invoice, _) =>
            {
                OutcomeReads.Add(invoice);
                return Outcomes.TryGetValue(invoice, out var outcome) ? Task.FromResult(outcome) : Task.FromResult<FirmOutcome?>(null);
            },
            NullLogger<EbillingFeedSource>.Instance);
    }

    [Fact(DisplayName = "132 feed (must-not-fire): a Rejected and a Returned invoice the rule clears are items, with the invoice id and its change time")]
    public async Task RejectedAndReturned_ClearedByTheRule_AreItems()
    {
        var rig = new Rig();
        var rejected = Guid.NewGuid();
        var returned = Guid.NewGuid();
        rig.Candidates.Add(Candidate(rejected, InvoiceStatusCodes.Rejected));
        rig.Candidates.Add(Candidate(returned, InvoiceStatusCodes.ReturnedToFirm, new DateTime(2026, 10, 9, 8, 0, 0, DateTimeKind.Utc)));
        rig.Decisions[rejected] = InvoiceFeedDecision.Visible;
        rig.Decisions[returned] = InvoiceFeedDecision.Visible;

        var result = await rig.Build().ReadAsync(Contact(), Cutoff, CancellationToken.None);

        result.Items.Select(i => i.InvoiceId).Should().Equal(rejected, returned);
        result.Items[0].OccurredOn.Should().Be(new DateTimeOffset(2026, 10, 10, 9, 0, 0, TimeSpan.Zero));
        result.Truncated.Should().BeFalse();
        rig.FirmsQueried.Should().BeEquivalentTo(new[] { FirmA }, "candidates are read only for the caller's own firms");
        rig.OutcomeReads.Should().BeEmpty("a Rejected or Returned invoice needs no outcome read");
    }

    [Fact(DisplayName = "132 feed (must-fire): another firm's invoice, a contact without access to the core record and a hidden invoice (Denied) are never items")]
    public async Task DeniedInvoices_AreDropped()
    {
        var rig = new Rig();
        var denied = Guid.NewGuid();
        var visible = Guid.NewGuid();
        rig.Candidates.Add(Candidate(denied, InvoiceStatusCodes.Rejected));
        rig.Candidates.Add(Candidate(visible, InvoiceStatusCodes.Rejected));
        rig.Decisions[visible] = InvoiceFeedDecision.Visible;
        // `denied` has no decision entry: the double answers Denied, as for a foreign firm, no core access or a hidden invoice.

        var result = await rig.Build().ReadAsync(Contact(), Cutoff, CancellationToken.None);

        result.Items.Select(i => i.InvoiceId).Should().Equal(visible);
        result.Truncated.Should().BeFalse("a Denied invoice is not an error");
        rig.Decided.Should().BeEquivalentTo([denied, visible], "every candidate is asked, none is trusted from the read");
    }

    [Fact(DisplayName = "132 feed (must-fire): a decision that faults or cannot be made drops that invoice, flags the feed truncated, and the others still come through")]
    public async Task UndecidableInvoice_IsDropped_AndTheFeedSaysTruncated()
    {
        var rig = new Rig();
        var thrown = Guid.NewGuid();
        var unavailable = Guid.NewGuid();
        var visible = Guid.NewGuid();
        rig.Candidates.AddRange([
            Candidate(thrown, InvoiceStatusCodes.Rejected), Candidate(unavailable, InvoiceStatusCodes.Rejected), Candidate(visible, InvoiceStatusCodes.Rejected)]);
        rig.Throwing.Add(thrown);
        rig.Decisions[unavailable] = InvoiceFeedDecision.Unavailable;
        rig.Decisions[visible] = InvoiceFeedDecision.Visible;

        var result = await rig.Build().ReadAsync(Contact(), Cutoff, CancellationToken.None);

        result.Items.Select(i => i.InvoiceId).Should().Equal(visible);
        result.Truncated.Should().BeTrue();
    }

    [Fact(DisplayName = "132 feed: an Approved invoice is an item only when the projection says it carries an adjustment; not otherwise")]
    public async Task Approved_IsNewsOnlyWithAnAdjustment()
    {
        var rig = new Rig();
        var adjusted = Guid.NewGuid();
        var plain = Guid.NewGuid();
        rig.Candidates.AddRange([Candidate(adjusted, InvoiceStatusCodes.Approved), Candidate(plain, InvoiceStatusCodes.Approved)]);
        rig.Decisions[adjusted] = InvoiceFeedDecision.Visible;
        rig.Decisions[plain] = InvoiceFeedDecision.Visible;
        rig.Outcomes[adjusted] = Outcome(FirmOutcomeKind.ApprovedWithAdjustments);
        rig.Outcomes[plain] = Outcome(FirmOutcomeKind.Approved);

        var result = await rig.Build().ReadAsync(Contact(), Cutoff, CancellationToken.None);

        result.Items.Select(i => i.InvoiceId).Should().Equal(adjusted);
        result.Truncated.Should().BeFalse();
    }

    [Fact(DisplayName = "132 feed (must-fire): an Approved invoice whose outcome cannot be read, or is degraded or unavailable, is dropped and the feed says truncated")]
    public async Task Approved_WithAnOutcomeThatCannotBeRead_IsDropped_AndTruncated()
    {
        var rig = new Rig();
        var missing = Guid.NewGuid();
        var degraded = Guid.NewGuid();
        var unavailable = Guid.NewGuid();
        rig.Candidates.AddRange([
            Candidate(missing, InvoiceStatusCodes.Approved), Candidate(degraded, InvoiceStatusCodes.Approved), Candidate(unavailable, InvoiceStatusCodes.Approved)]);
        foreach (var id in new[] { missing, degraded, unavailable })
        {
            rig.Decisions[id] = InvoiceFeedDecision.Visible;
        }

        rig.Outcomes[degraded] = Outcome(FirmOutcomeKind.ApprovedWithAdjustments, degraded: true);
        rig.Outcomes[unavailable] = Outcome(FirmOutcomeKind.Unavailable);

        var result = await rig.Build().ReadAsync(Contact(), Cutoff, CancellationToken.None);

        result.Items.Should().BeEmpty();
        result.Truncated.Should().BeTrue();
    }

    [Fact(DisplayName = "132 feed (must-fire): an Approved invoice the rule denied is never projected (the outcome is not read for it)")]
    public async Task Approved_Denied_IsNeverProjected()
    {
        var rig = new Rig();
        var invoice = Guid.NewGuid();
        rig.Candidates.Add(Candidate(invoice, InvoiceStatusCodes.Approved));
        rig.Outcomes[invoice] = Outcome(FirmOutcomeKind.ApprovedWithAdjustments);

        var result = await rig.Build().ReadAsync(Contact(), Cutoff, CancellationToken.None);

        result.Items.Should().BeEmpty();
        rig.OutcomeReads.Should().BeEmpty();
    }

    [Fact(DisplayName = "132 feed (must-fire): a workforce systemuser, an empty contact and an unreadable contact get nothing, and nothing is read")]
    public async Task NotAFirmCaller_GetsNothing_AndNothingIsRead()
    {
        var rig = new Rig();
        rig.Candidates.Add(Candidate(Guid.NewGuid(), InvoiceStatusCodes.Rejected));
        var source = rig.Build();

        var systemUser = await source.ReadAsync(Contact(systemUser: Guid.NewGuid()), Cutoff, CancellationToken.None);
        var empty = await source.ReadAsync(Contact(Guid.Empty), Cutoff, CancellationToken.None);
        var unreadable = await source.ReadAsync(Contact(unreadable: true), Cutoff, CancellationToken.None);

        systemUser.Items.Should().BeEmpty();
        empty.Items.Should().BeEmpty();
        unreadable.Items.Should().BeEmpty();
        rig.FirmReads.Should().Be(0);
        rig.CandidateReads.Should().Be(0);
    }

    [Fact(DisplayName = "132 feed: a caller with no firm gets nothing and no candidates are read; unreadable or faulting firms are truncated, never silent")]
    public async Task Firms_NoneOrUndecidable()
    {
        var none = new Rig { Firms = [] };
        var unreadable = new Rig { FirmsUnreadable = true };
        var faulting = new Rig { FirmsThrow = true };

        var noFirm = await none.Build().ReadAsync(Contact(), Cutoff, CancellationToken.None);
        var unread = await unreadable.Build().ReadAsync(Contact(), Cutoff, CancellationToken.None);
        var fault = await faulting.Build().ReadAsync(Contact(), Cutoff, CancellationToken.None);

        noFirm.Items.Should().BeEmpty();
        noFirm.Truncated.Should().BeFalse();
        none.CandidateReads.Should().Be(0);
        unread.Items.Should().BeEmpty();
        unread.Truncated.Should().BeTrue();
        unreadable.CandidateReads.Should().Be(0);
        fault.Items.Should().BeEmpty();
        fault.Truncated.Should().BeTrue();
        faulting.CandidateReads.Should().Be(0);
    }

    [Fact(DisplayName = "132 feed: a candidate read that faults gives nothing and says truncated")]
    public async Task CandidatesThatFault_GiveNothing_AndTruncated()
    {
        var rig = new Rig { CandidatesThrow = true };

        var result = await rig.Build().ReadAsync(Contact(), Cutoff, CancellationToken.None);

        result.Items.Should().BeEmpty();
        result.Truncated.Should().BeTrue();
    }

    [Fact(DisplayName = "132 feed: more candidates than the cap are cut and the cut is flagged; only the cap are decided")]
    public async Task MoreCandidatesThanTheCap_AreCut_AndFlagged()
    {
        var rig = new Rig();
        for (var i = 0; i < EbillingFeedSource.MaxCandidates + 1; i++)
        {
            var id = Guid.NewGuid();
            rig.Candidates.Add(Candidate(id, InvoiceStatusCodes.Rejected));
            rig.Decisions[id] = InvoiceFeedDecision.Visible;
        }

        var result = await rig.Build().ReadAsync(Contact(), Cutoff, CancellationToken.None);

        result.Items.Should().HaveCount(EbillingFeedSource.MaxCandidates);
        result.Truncated.Should().BeTrue();
        rig.Decided.Should().HaveCount(EbillingFeedSource.MaxCandidates);
    }

    // ── EB-D-207: the rendered column set is pinned against the external invoice module's ReadableColumns ──

    /// <summary>
    /// Timestamp-only columns allowed beside ReadableColumns (EB-D-207): a change time carries no business value and is not a grid column. A column added to
    /// <see cref="EbillingFeedSource.RenderedInvoiceColumns"/> must be in the module's ReadableColumns or be named here, so adding one fails this test.
    /// </summary>
    private static readonly string[] TimestampOnlyColumns = ["modifiedon"];

    [Fact(DisplayName = "132 EB-D-207 pin: every sprk_invoice column the feed renders is in the external invoice module's ReadableColumns (a timestamp column named here aside)")]
    public void RenderedInvoiceColumns_AreReadableByTheExternalInvoiceModule()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<Microsoft.Extensions.Configuration.IConfiguration>(new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build());
        services.AddExternalAccess();
        var registry = services.BuildServiceProvider().GetRequiredService<ExternalModuleRegistry>();
        var invoices = registry.FindByEntity("sprk_invoice")!;

        var outside = EbillingFeedSource.RenderedInvoiceColumns
            .Where(c => !invoices.ReadableColumns.Contains(c) && !TimestampOnlyColumns.Contains(c, StringComparer.OrdinalIgnoreCase))
            .ToList();

        outside.Should().BeEmpty("a firm-facing column must be one the external invoice module already releases (EB-D-207)");
        EbillingFeedSource.RenderedInvoiceColumns.Should().Contain("sprk_invoiceid").And.Contain("statuscode");
    }

    [Fact(DisplayName = "132 EB-D-207 pin (must-fire control): a column outside ReadableColumns and the timestamp list is caught by the pin's own rule")]
    public void ThePinRule_CatchesAColumnTheModuleDoesNotRelease()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<Microsoft.Extensions.Configuration.IConfiguration>(new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build());
        services.AddExternalAccess();
        var invoices = services.BuildServiceProvider().GetRequiredService<ExternalModuleRegistry>().FindByEntity("sprk_invoice")!;

        string[] withExtra = [.. EbillingFeedSource.RenderedInvoiceColumns, "sprk_billingexport", "sprk_trackingnumber"];
        var outside = withExtra
            .Where(c => !invoices.ReadableColumns.Contains(c) && !TimestampOnlyColumns.Contains(c, StringComparer.OrdinalIgnoreCase))
            .ToList();

        outside.Should().BeEquivalentTo("sprk_billingexport", "sprk_trackingnumber");
    }
}
