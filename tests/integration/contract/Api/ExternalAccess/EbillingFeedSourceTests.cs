using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Sprk.Bff.Api.Api.ExternalAccess;
using Sprk.Bff.Api.Infrastructure.DI;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Services.Finance;
using Xunit;

namespace Sprk.Bff.Api.Tests.Api.ExternalAccess;

/// <summary>
/// ebilling-r1 task 132 (FR-39, NFR-10, EB-D-207): the eBilling source of the external notification feed. The per-item invoice rule is the outcome
/// route's own (its decisions have their own suite, <c>FirmOutcomeScopeTests</c>); here it is a double, so these cases prove the SOURCE never lets
/// through what the rule did not clear, fails closed on a fault, decides the candidates in ONE batch, cuts only AFTER the decision, narrows the audience
/// to the invoice's submitter and the firm's billing contact, honours the kill switch, and flags what it could not decide. Also pins the rendered column
/// set (EB-D-207).
/// </summary>
public sealed class EbillingFeedSourceTests
{
    private static readonly DateTimeOffset Cutoff = new(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly Guid FirmA = Guid.Parse("13200000-0000-4000-8000-0000000000a1");
    private static readonly Guid FirmB = Guid.Parse("13200000-0000-4000-8000-0000000000b1");
    private static readonly Guid Me = Guid.Parse("13200000-0000-4000-8000-0000000000c1");
    private static readonly Guid Colleague = Guid.Parse("13200000-0000-4000-8000-0000000000c2");

    private static CallerPrincipal Contact(Guid? contactId = null, Guid? systemUser = null, bool unreadable = false) => new()
    {
        Plane = CallerPrincipalPlane.CiamContact,
        ContactId = contactId ?? Me,
        SystemUserId = systemUser,
        ContactUnreadable = unreadable,
        ProjectAccess = [],
    };

    /// <summary>A candidate row: by default the caller's own (submitted by <see cref="Me"/>) invoice of <see cref="FirmA"/>.</summary>
    private static Entity Candidate(
        Guid id, int status, DateTime? modifiedOn = null, Guid? submittedBy = null, Guid? firm = null, bool noSubmitter = false)
    {
        var row = new Entity("sprk_invoice", id)
        {
            ["statuscode"] = new OptionSetValue(status),
            ["modifiedon"] = modifiedOn ?? new DateTime(2026, 10, 10, 9, 0, 0, DateTimeKind.Utc),
            ["sprk_vendororg"] = new EntityReference("sprk_organization", firm ?? FirmA),
        };
        if (!noSubmitter)
        {
            row["sprk_submittedby"] = new EntityReference("contact", submittedBy ?? Me);
        }

        return row;
    }

    private static FirmOutcome Outcome(FirmOutcomeKind kind, bool degraded = false) =>
        new(kind, "TRK-1", "Status", 100m, 90m, [], degraded, 0);

    private sealed class Rig
    {
        public List<Entity> Candidates { get; } = [];
        public Dictionary<Guid, InvoiceFeedDecision> Decisions { get; } = [];
        public Dictionary<Guid, FirmOutcome?> Outcomes { get; } = [];
        public List<IReadOnlyList<Guid>> Batches { get; } = [];
        public List<Guid> OutcomeReads { get; } = [];
        public IReadOnlyList<Guid> Firms { get; set; } = [FirmA];
        public IReadOnlyList<Guid> BillingFirms { get; set; } = [];
        public bool Enabled { get; set; } = true;
        public bool FirmsUnreadable { get; set; }
        public bool FirmsThrow { get; set; }
        public bool BillingThrow { get; set; }
        public bool CandidatesThrow { get; set; }
        public bool DecideThrows { get; set; }
        public int FirmReads { get; private set; }
        public int BillingReads { get; private set; }
        public int CandidateReads { get; private set; }
        public IReadOnlyList<Guid>? FirmsQueried { get; private set; }
        public Guid? ContactQueried { get; private set; }
        public IReadOnlyList<Guid>? BillingFirmsQueried { get; private set; }

        public EbillingFeedSource Build() => new(
            () => Enabled,
            (_, _) =>
            {
                FirmReads++;
                return FirmsThrow
                    ? Task.FromException<(IReadOnlyList<Guid>, bool)>(new InvalidOperationException("firms down"))
                    : Task.FromResult((Firms, FirmsUnreadable));
            },
            (_, _, _) =>
            {
                BillingReads++;
                return BillingThrow
                    ? Task.FromException<IReadOnlyList<Guid>>(new InvalidOperationException("billing down"))
                    : Task.FromResult(BillingFirms);
            },
            (contact, firms, billing, _, _) =>
            {
                CandidateReads++;
                ContactQueried = contact;
                FirmsQueried = firms;
                BillingFirmsQueried = billing;
                return CandidatesThrow
                    ? Task.FromException<IReadOnlyList<Entity>>(new InvalidOperationException("candidates down"))
                    : Task.FromResult<IReadOnlyList<Entity>>(Candidates.ToList());
            },
            (_, _, rows, _) =>
            {
                Batches.Add(rows.Select(r => r.Id).ToList());
                return DecideThrows
                    ? Task.FromException<IReadOnlyDictionary<Guid, InvoiceFeedDecision>>(new InvalidOperationException("decision down"))
                    : Task.FromResult<IReadOnlyDictionary<Guid, InvoiceFeedDecision>>(
                        rows.ToDictionary(r => r.Id, r => Decisions.GetValueOrDefault(r.Id, InvoiceFeedDecision.Denied)));
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
        rig.ContactQueried.Should().Be(Me, "candidates are narrowed to the caller's own contact");
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
        rig.Batches.Should().ContainSingle().Which.Should().BeEquivalentTo([denied, visible], "every candidate is asked, none is trusted from the read");
    }

    [Fact(DisplayName = "132 feed F3: every candidate is decided in ONE batch, not one decision per invoice")]
    public async Task TheCandidates_AreDecidedInOneBatch()
    {
        var rig = new Rig();
        for (var i = 0; i < 12; i++)
        {
            var id = Guid.NewGuid();
            rig.Candidates.Add(Candidate(id, InvoiceStatusCodes.Rejected));
            rig.Decisions[id] = InvoiceFeedDecision.Visible;
        }

        await rig.Build().ReadAsync(Contact(), Cutoff, CancellationToken.None);

        rig.Batches.Should().ContainSingle("the access decision is one call for the whole read").Which.Should().HaveCount(12);
    }

    [Fact(DisplayName = "132 feed (must-fire): an invoice whose decision cannot be made drops that invoice, flags the feed truncated, and the others still come through")]
    public async Task UndecidableInvoice_IsDropped_AndTheFeedSaysTruncated()
    {
        var rig = new Rig();
        var unavailable = Guid.NewGuid();
        var unanswered = Guid.NewGuid();
        var visible = Guid.NewGuid();
        rig.Candidates.AddRange([
            Candidate(unavailable, InvoiceStatusCodes.Rejected), Candidate(unanswered, InvoiceStatusCodes.Rejected), Candidate(visible, InvoiceStatusCodes.Rejected)]);
        rig.Decisions[unavailable] = InvoiceFeedDecision.Unavailable;
        rig.Decisions[visible] = InvoiceFeedDecision.Visible;

        var result = await rig.Build().ReadAsync(Contact(), Cutoff, CancellationToken.None);

        result.Items.Select(i => i.InvoiceId).Should().Equal(visible);
        result.Truncated.Should().BeTrue();
    }

    [Fact(DisplayName = "132 feed (must-fire): a decision that faults shows nothing and says truncated")]
    public async Task DecisionThatFaults_ShowsNothing_AndTruncated()
    {
        var rig = new Rig { DecideThrows = true };
        var id = Guid.NewGuid();
        rig.Candidates.Add(Candidate(id, InvoiceStatusCodes.Rejected));
        rig.Decisions[id] = InvoiceFeedDecision.Visible;

        var result = await rig.Build().ReadAsync(Contact(), Cutoff, CancellationToken.None);

        result.Items.Should().BeEmpty();
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

    [Fact(DisplayName = "132 feed F8 (must-fire): with eBilling switched off the feed has no eBilling item and nothing is read")]
    public async Task KillSwitchOff_ReturnsNothing_AndReadsNothing()
    {
        var rig = new Rig { Enabled = false };
        var id = Guid.NewGuid();
        rig.Candidates.Add(Candidate(id, InvoiceStatusCodes.Rejected));
        rig.Decisions[id] = InvoiceFeedDecision.Visible;

        var result = await rig.Build().ReadAsync(Contact(), Cutoff, CancellationToken.None);

        result.Items.Should().BeEmpty();
        result.Truncated.Should().BeFalse();
        (rig.FirmReads + rig.BillingReads + rig.CandidateReads + rig.Batches.Count).Should().Be(0, "no projection or Dataverse read while eBilling is off");

        rig.Enabled = true;
        (await rig.Build().ReadAsync(Contact(), Cutoff, CancellationToken.None)).Items.Should().ContainSingle("the control: the same data is an item once it is on");
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

    // ── F3: the cap is applied AFTER the access decision ──

    [Fact(DisplayName = "132 feed F3: more VISIBLE items than the cap are cut, the cut is flagged, and the newest are kept; every candidate was decided")]
    public async Task MoreVisibleItemsThanTheCap_AreCut_AndFlagged()
    {
        var rig = new Rig();
        var ids = new List<Guid>();
        for (var i = 0; i < EbillingFeedSource.MaxItems + 1; i++)
        {
            var id = Guid.NewGuid();
            ids.Add(id);
            rig.Candidates.Add(Candidate(id, InvoiceStatusCodes.Rejected, new DateTime(2026, 10, 20, 0, 0, 0, DateTimeKind.Utc).AddMinutes(-i)));
            rig.Decisions[id] = InvoiceFeedDecision.Visible;
        }

        var result = await rig.Build().ReadAsync(Contact(), Cutoff, CancellationToken.None);

        result.Items.Select(i => i.InvoiceId).Should().Equal(ids.Take(EbillingFeedSource.MaxItems));
        result.Truncated.Should().BeTrue();
        rig.Batches.Should().ContainSingle().Which.Should().HaveCount(EbillingFeedSource.MaxItems + 1);
    }

    [Fact(DisplayName = "132 feed F3 (must-fire): invoices the caller may NOT see never fill the cap and never raise truncated; the visible ones past them still appear")]
    public async Task HiddenInvoices_DoNotUseUpTheCap_OrFlagTruncated()
    {
        var rig = new Rig();
        // The newest 30 are hidden from the caller; the 5 older ones are visible. A cap applied BEFORE the decision would show none of the 5.
        for (var i = 0; i < 30; i++)
        {
            rig.Candidates.Add(Candidate(Guid.NewGuid(), InvoiceStatusCodes.Rejected, new DateTime(2026, 10, 20, 0, 0, 0, DateTimeKind.Utc).AddMinutes(-i)));
        }

        var visible = new List<Guid>();
        for (var i = 0; i < 5; i++)
        {
            var id = Guid.NewGuid();
            visible.Add(id);
            rig.Candidates.Add(Candidate(id, InvoiceStatusCodes.Rejected, new DateTime(2026, 10, 10, 0, 0, 0, DateTimeKind.Utc).AddMinutes(-i)));
            rig.Decisions[id] = InvoiceFeedDecision.Visible;
        }

        var result = await rig.Build().ReadAsync(Contact(), Cutoff, CancellationToken.None);

        result.Items.Select(i => i.InvoiceId).Should().Equal(visible);
        result.Truncated.Should().BeFalse("only invoices the caller could open count toward 'there is more'");
    }

    [Fact(DisplayName = "132 feed F3: reaching the read bound is flagged, and only the bound is decided")]
    public async Task ReadBound_IsFlagged()
    {
        var rig = new Rig();
        for (var i = 0; i < EbillingFeedSource.MaxCandidatesRead + 1; i++)
        {
            rig.Candidates.Add(Candidate(Guid.NewGuid(), InvoiceStatusCodes.Rejected));
        }

        var result = await rig.Build().ReadAsync(Contact(), Cutoff, CancellationToken.None);

        result.Truncated.Should().BeTrue();
        rig.Batches.Should().ContainSingle().Which.Should().HaveCount(EbillingFeedSource.MaxCandidatesRead);
    }

    // ── F2: the audience is the invoice's submitter and the firm's billing contact, nobody else ──

    [Fact(DisplayName = "132 feed F2 (must-fire): a firm associate who is neither the submitter nor the billing contact gets no item, even when the query came back too wide; the row is never decided")]
    public async Task AFirmAssociate_WhoIsNeitherSubmitterNorBillingContact_GetsNoItem()
    {
        var rig = new Rig();
        var someoneElses = Guid.NewGuid();
        rig.Candidates.Add(Candidate(someoneElses, InvoiceStatusCodes.Rejected, submittedBy: Colleague));
        rig.Decisions[someoneElses] = InvoiceFeedDecision.Visible; // even a Visible decision cannot put it in the feed

        var result = await rig.Build().ReadAsync(Contact(), Cutoff, CancellationToken.None);

        result.Items.Should().BeEmpty();
        rig.Batches.Should().BeEmpty("a row outside the audience is dropped before it is decided");
    }

    [Fact(DisplayName = "132 feed F2 (must-not-fire): the submitter gets the item; so does the firm's billing contact for an invoice somebody else submitted; an invoice with no submitter reaches only the billing contact")]
    public async Task TheSubmitter_AndTheBillingContact_EachGetTheItem()
    {
        var own = Guid.NewGuid();
        var rig = new Rig();
        rig.Candidates.Add(Candidate(own, InvoiceStatusCodes.Rejected));
        rig.Decisions[own] = InvoiceFeedDecision.Visible;
        (await rig.Build().ReadAsync(Contact(), Cutoff, CancellationToken.None)).Items.Select(i => i.InvoiceId).Should().Equal(own);

        var theirs = Guid.NewGuid();
        var billing = new Rig { BillingFirms = [FirmA] };
        billing.Candidates.Add(Candidate(theirs, InvoiceStatusCodes.ReturnedToFirm, submittedBy: Colleague));
        billing.Decisions[theirs] = InvoiceFeedDecision.Visible;
        (await billing.Build().ReadAsync(Contact(), Cutoff, CancellationToken.None)).Items.Select(i => i.InvoiceId).Should().Equal(theirs);
        billing.BillingFirmsQueried.Should().BeEquivalentTo(new[] { FirmA }, "the candidate read is told which firms the caller bills for");

        var orphan = Guid.NewGuid();
        var none = new Rig { BillingFirms = [FirmA] };
        none.Candidates.Add(Candidate(orphan, InvoiceStatusCodes.Rejected, noSubmitter: true));
        none.Decisions[orphan] = InvoiceFeedDecision.Visible;
        (await none.Build().ReadAsync(Contact(), Cutoff, CancellationToken.None)).Items.Should().ContainSingle("the billing contact sees a firm invoice with no recorded submitter");
        (await new Rig { Candidates = { Candidate(orphan, InvoiceStatusCodes.Rejected, noSubmitter: true) } }.Build().ReadAsync(Contact(), Cutoff, CancellationToken.None))
            .Items.Should().BeEmpty("a caller who is not the billing contact does not see it");
    }

    [Fact(DisplayName = "132 feed F2 (must-fire): the billing contact of ANOTHER firm does not get this firm's invoice")]
    public async Task TheBillingContactOfAnotherFirm_GetsNothing()
    {
        var rig = new Rig { BillingFirms = [FirmB] };
        var invoice = Guid.NewGuid();
        rig.Candidates.Add(Candidate(invoice, InvoiceStatusCodes.Rejected, submittedBy: Colleague, firm: FirmA));
        rig.Decisions[invoice] = InvoiceFeedDecision.Visible;

        (await rig.Build().ReadAsync(Contact(), Cutoff, CancellationToken.None)).Items.Should().BeEmpty();
    }

    [Fact(DisplayName = "132 feed F2: a billing-firm read that faults leaves the caller's own submissions, and says truncated")]
    public async Task BillingFirmReadFault_KeepsOwnSubmissions_AndTruncated()
    {
        var rig = new Rig { BillingThrow = true };
        var own = Guid.NewGuid();
        var billed = Guid.NewGuid();
        rig.Candidates.Add(Candidate(own, InvoiceStatusCodes.Rejected));
        rig.Candidates.Add(Candidate(billed, InvoiceStatusCodes.Rejected, submittedBy: Colleague));
        rig.Decisions[own] = InvoiceFeedDecision.Visible;
        rig.Decisions[billed] = InvoiceFeedDecision.Visible;

        var result = await rig.Build().ReadAsync(Contact(), Cutoff, CancellationToken.None);

        result.Items.Select(i => i.InvoiceId).Should().Equal(own);
        result.Truncated.Should().BeTrue();
    }

    // ── F2: the QUERY itself is narrowed (evaluated here against rows, so the narrowing is proved, not assumed) ──

    private static IEnumerable<Guid> Matching(QueryExpression query, params Entity[] rows) =>
        rows.Where(r => Matches(r, query.Criteria)).Select(r => r.Id);

    private static bool Matches(Entity row, FilterExpression filter)
    {
        bool Test(ConditionExpression c)
        {
            row.Attributes.TryGetValue(c.AttributeName, out var raw);
            object? value = raw switch
            {
                EntityReference reference => reference.Id,
                OptionSetValue option => option.Value,
                _ => raw,
            };
            return c.Operator switch
            {
                ConditionOperator.Equal => value is not null && Equals(value, c.Values[0]),
                ConditionOperator.In => value is not null && c.Values.Any(v => Equals(value, v)),
                ConditionOperator.GreaterEqual => value is DateTime when && c.Values[0] is DateTime bound && when >= bound,
                _ => throw new NotSupportedException(c.Operator.ToString()),
            };
        }

        var results = filter.Conditions.Select(Test).Concat(filter.Filters.Select(f => Matches(row, f))).ToList();
        return filter.FilterOperator == LogicalOperator.And ? results.All(r => r) : results.Any(r => r);
    }

    [Fact(DisplayName = "132 feed F2 (must-fire): the candidate QUERY returns the caller's own submissions and its billing firms' invoices and nobody else's")]
    public void CandidateQuery_IsNarrowedToTheSubmitterAndTheBillingContact()
    {
        Entity Row(Guid? submittedBy, Guid firm, int status = InvoiceStatusCodes.Rejected, DateTime? at = null) =>
            Candidate(Guid.NewGuid(), status, at, submittedBy, firm, noSubmitter: submittedBy is null);

        var mine = Row(Me, FirmA);
        var associates = Row(Colleague, FirmA);
        var billedByMe = Row(Colleague, FirmB);
        var otherFirm = Row(Colleague, Guid.NewGuid());
        var noSubmitterBilled = Row(null, FirmB);
        var noSubmitterOther = Row(null, FirmA);
        var wrongStatus = Row(Me, FirmA, status: 1);
        var tooOld = Row(Me, FirmA, at: new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc));
        var elsewhere = Row(Me, Guid.NewGuid());
        Entity[] all = [mine, associates, billedByMe, otherFirm, noSubmitterBilled, noSubmitterOther, wrongStatus, tooOld, elsewhere];

        var asSubmitter = EbillingFeedSource.CandidateQuery(Me, [FirmA, FirmB], [], Cutoff);
        var asBillingContact = EbillingFeedSource.CandidateQuery(Me, [FirmA, FirmB], [FirmB], Cutoff);

        Matching(asSubmitter, all).Should().BeEquivalentTo([mine.Id], "a firm associate's invoices, and invoices of firms outside the caller's own, are not candidates");
        Matching(asBillingContact, all).Should().BeEquivalentTo([mine.Id, billedByMe.Id, noSubmitterBilled.Id]);
        asSubmitter.TopCount.Should().Be(EbillingFeedSource.MaxCandidatesRead + 1);
    }

    // ── EB-D-207: the rendered column set is pinned against the external invoice module's ReadableColumns ──

    /// <summary>
    /// Timestamp-only columns allowed beside ReadableColumns (EB-D-207): a change time carries no business value and is not a grid column. A column added to
    /// <see cref="EbillingFeedSource.RenderedInvoiceColumns"/> must be in the module's ReadableColumns or be named here, so adding one fails this test.
    /// </summary>
    private static readonly string[] TimestampOnlyColumns = ["modifiedon"];

    private static ExternalModuleDescriptor InvoiceModule()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<Microsoft.Extensions.Configuration.IConfiguration>(new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build());
        services.AddExternalAccess();
        return services.BuildServiceProvider().GetRequiredService<ExternalModuleRegistry>().FindByEntity("sprk_invoice")!;
    }

    [Fact(DisplayName = "132 EB-D-207 pin: every sprk_invoice column the feed renders (the invoice name, the item title, included) is in the external invoice module's ReadableColumns (a timestamp column named here aside)")]
    public void RenderedInvoiceColumns_AreReadableByTheExternalInvoiceModule()
    {
        var invoices = InvoiceModule();

        var outside = EbillingFeedSource.RenderedInvoiceColumns
            .Where(c => !invoices.ReadableColumns.Contains(c) && !TimestampOnlyColumns.Contains(c, StringComparer.OrdinalIgnoreCase))
            .ToList();

        outside.Should().BeEmpty("a firm-facing column must be one the external invoice module already releases (EB-D-207)");
        EbillingFeedSource.RenderedInvoiceColumns.Should().Contain("sprk_invoiceid").And.Contain("statuscode").And.Contain("sprk_name");
        EbillingFeedSource.RenderedInvoiceColumns.Should().Contain(ExternalDataService.InvoiceNameColumn, "the title read (ExternalDataService.GetRecordNamesAsync) is pinned too");
    }

    [Fact(DisplayName = "132 EB-D-207 pin (must-fire control): a column outside ReadableColumns and the timestamp list is caught by the pin's own rule")]
    public void ThePinRule_CatchesAColumnTheModuleDoesNotRelease()
    {
        var invoices = InvoiceModule();

        string[] withExtra = [.. EbillingFeedSource.RenderedInvoiceColumns, "sprk_billingexport", "sprk_trackingnumber"];
        var outside = withExtra
            .Where(c => !invoices.ReadableColumns.Contains(c) && !TimestampOnlyColumns.Contains(c, StringComparer.OrdinalIgnoreCase))
            .ToList();

        outside.Should().BeEquivalentTo("sprk_billingexport", "sprk_trackingnumber");
    }

    [Fact(DisplayName = "132 EB-D-207 pin: the candidate query reads only rendered columns (the name aside), the audience columns and the access rule's scope columns, so it cannot drift")]
    public void TheCandidateQuery_ReadsNoColumnOutsideThePinnedSets()
    {
        var read = EbillingFeedSource.CandidateQuery(Me, [FirmA], [], Cutoff).ColumnSet.Columns;
        string[] allowed =
        [
            .. EbillingFeedSource.RenderedInvoiceColumns,
            .. EbillingFeedSource.AudienceColumns,
            .. FirmOutcomeAuthorizationFilter.InvoiceScopeColumnNames,
        ];

        read.Should().OnlyContain(c => allowed.Contains(c, StringComparer.OrdinalIgnoreCase));
        FirmOutcomeAuthorizationFilter.InvoiceScopeColumnNames.Should().OnlyContain(c => read.Contains(c), "the access rule's own columns ride on the candidate read");
        read.Should().NotContain("sprk_name", "the title is read by the feed's own title step, not by the candidate query");
    }
}
