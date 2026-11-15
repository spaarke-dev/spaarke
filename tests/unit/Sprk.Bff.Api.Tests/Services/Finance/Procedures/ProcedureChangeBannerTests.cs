using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.Filters;
using Sprk.Bff.Api.Services.Finance;
using Sprk.Bff.Api.Services.Finance.Procedures;
using Xunit;
using static Sprk.Bff.Api.Tests.Services.Finance.Procedures.ProcedureBannerWorld;

namespace Sprk.Bff.Api.Tests.Services.Finance.Procedures;

/// <summary>
/// ebilling-r1 task 113 (model 8.2, 15 answer 6): <see cref="ProcedureChangeBanner"/> over an in-memory Dataverse that evaluates each query
/// (<see cref="ProcedureBannerWorld"/>). The stored side is the snapshot the production builder writes for the rows as they stood; each test then
/// changes the rows and asks the banner. In scope: every trigger of 8.2 that must fire; the changes that must not; the four states and their exact
/// texts; who gets a banner at all; faults, a 429 and a timeout; the work dates the comparison resolves (the evaluator's rule); the cost and the
/// no-write rule; and the shape of what is serialised. The review route's wiring is in <c>InvoiceReviewReadServiceTests</c>.
/// </summary>
public sealed class ProcedureChangeBannerTests
{
    private static ProcedureChangeBanner BannerOver(IGenericEntityService entities, ILogger<ProcedureChangeBanner>? logger = null, TimeProvider? clock = null) =>
        new(entities, logger ?? NullLogger<ProcedureChangeBanner>.Instance, clock);

    private static Task<ProcedureChangeBannerDto?> Ask(
        ProcedureChangeBanner banner, string? findings, Entity? invoice = null, List<Entity>? lines = null,
        FinanceStepZeroVerdict? verdict = FinanceStepZeroVerdict.Internal, bool open = true, CancellationToken ct = default) =>
        banner.ComputeAsync(invoice ?? Invoice(), lines ?? Lines(), findings, verdict, open, ct);

    private static async Task<ProcedureChangeBannerDto?> Ask(ProcedureBannerWorld w, string? findings, Entity? invoice = null, List<Entity>? lines = null) =>
        await Ask(BannerOver(w.Entities.Object), findings, invoice, lines);

    private static void ShouldBeChanged(ProcedureChangeBannerDto? banner) =>
        banner.Should().Be(new ProcedureChangeBannerDto("Changed", "The procedures that apply to this invoice have changed; re-check to apply them."));

    private static void ShouldBeNotChanged(ProcedureChangeBannerDto? banner) =>
        banner.Should().Be(new ProcedureChangeBannerDto("NotChanged", null), "no change has no sentence");

    private static void ShouldBeNotYetChecked(ProcedureChangeBannerDto? banner) =>
        banner.Should().Be(new ProcedureChangeBannerDto("NotYetChecked", "Not yet checked"));

    private static void ShouldBeCouldNotBeChecked(ProcedureChangeBannerDto? banner) =>
        banner.Should().Be(new ProcedureChangeBannerDto("CouldNotBeChecked", "Procedure changes could not be checked"));

    // ── Changed: each trigger of 8.2 must fire, against a snapshot that cited the old outcome ──

    [Fact(DisplayName = "113 Changed: an override removed after the invoice was judged under it")]
    public async Task OverrideRemoved_IsChanged()
    {
        var w = new ProcedureBannerWorld();
        var (_, _, rule, _) = w.Simple("RATE-1");
        var row = w.ExcludeRule(M, rule, reason: ReasonSentinel);
        var findings = await w.StoredFindingsAsync();

        w.World.Delete(ProcedureAssignmentEntity.Entity, row);

        ShouldBeChanged(await Ask(w, findings));
    }

    [Fact(DisplayName = "113 Changed: an override added after the invoice was judged")]
    public async Task OverrideAdded_IsChanged()
    {
        var w = new ProcedureBannerWorld();
        var (_, _, rule, _) = w.Simple("RATE-1");
        var findings = await w.StoredFindingsAsync();

        w.ExcludeRule(M, rule, reason: ReasonSentinel);

        ShouldBeChanged(await Ask(w, findings));
    }

    [Fact(DisplayName = "113 Changed: an override for the invoice's own firm added (the control of the other-firm case)")]
    public async Task OverrideForTheInvoicesFirmAdded_IsChanged()
    {
        var w = new ProcedureBannerWorld();
        var (_, _, rule, _) = w.Simple("RATE-1");
        var findings = await w.StoredFindingsAsync();

        w.ExcludeRule(M, rule, firm: F);

        ShouldBeChanged(await Ask(w, findings));
    }

    [Fact(DisplayName = "113 Changed: the reason of an override that decided the outcome edited (its content is compared)")]
    public async Task OverrideReasonEdited_IsChanged()
    {
        var w = new ProcedureBannerWorld();
        var (procedure, _, _, _) = w.Simple("ASSIGNED-1", ProcedureAppliesTo.AssignedRecordsOnly);
        var row = w.Assign(M, procedure, reason: "agreed with the partner");
        var findings = await w.StoredFindingsAsync();

        w.Set(ProcedureAssignmentEntity.Entity, row, ProcedureAssignmentEntity.ReasonColumn, "changed my mind");

        ShouldBeChanged(await Ask(w, findings));
    }

    [Fact(DisplayName = "113 Changed: a record rule switched Off after the invoice was judged with it")]
    public async Task RecordRuleSwitchedOff_IsChanged()
    {
        var w = new ProcedureBannerWorld();
        var (_, _, rule, ruleVersion) = w.Simple("RATE-1");
        var recordRule = w.ChangeRule(M, rule, ruleVersion, Jan1);
        var findings = await w.StoredFindingsAsync();

        w.Set("sprk_policy", recordRule, "sprk_enabled", false);

        ShouldBeChanged(await Ask(w, findings));
    }

    [Fact(DisplayName = "113 Changed: a Procedure retired after the invoice was judged under it")]
    public async Task ProcedureRetired_IsChanged()
    {
        var w = new ProcedureBannerWorld();
        var (procedure, _, _, _) = w.Simple("RATE-1");
        var findings = await w.StoredFindingsAsync();

        w.Set(ProcedureEntity.Entity, procedure, ProcedureEntity.StatusColumn, new OptionSetValue(ProcedureEntity.StatusRetired));

        ShouldBeChanged(await Ask(w, findings));
    }

    [Fact(DisplayName = "113 Changed: a Procedure newly assigned to the record after the invoice was judged")]
    public async Task ProcedureNewlyAssigned_IsChanged()
    {
        var w = new ProcedureBannerWorld();
        var (procedure, _, _, _) = w.Simple("ASSIGNED-1", ProcedureAppliesTo.AssignedRecordsOnly);
        var findings = await w.StoredFindingsAsync();

        w.Assign(M, procedure);

        ShouldBeChanged(await Ask(w, findings));
    }

    [Fact(DisplayName = "113 Changed: a version ended with no successor, so a judged date now has none")]
    public async Task VersionEnded_IsChanged()
    {
        var w = new ProcedureBannerWorld();
        var (_, version, _, _) = w.Simple("RATE-1");
        var findings = await w.StoredFindingsAsync();

        w.Set(ProcedureEntity.VersionEntity, version, ProcedureEntity.VersionEffectiveToColumn, new DateTime(2026, 3, 1));

        ShouldBeChanged(await Ask(w, findings));
    }

    [Fact(DisplayName = "113 Changed: a version retired after the invoice was judged under it")]
    public async Task VersionRetired_IsChanged()
    {
        var w = new ProcedureBannerWorld();
        var (_, version, _, _) = w.Simple("RATE-1");
        var findings = await w.StoredFindingsAsync();

        w.Set(ProcedureEntity.VersionEntity, version, ProcedureEntity.VersionStatusColumn, new OptionSetValue(ProcedureEntity.VersionStatusRetired));

        ShouldBeChanged(await Ask(w, findings));
    }

    [Fact(DisplayName = "113 Changed: a first version newly published for a Procedure that had only a Draft (the case a citation test misses)")]
    public async Task FirstVersionNewlyPublished_IsChanged()
    {
        var w = new ProcedureBannerWorld();
        var procedure = w.AddProcedure("Draft only", ProcedureAppliesTo.AllRecords);
        var version = w.AddVersion(procedure, 1, Jan1, null, ProcedureEntity.VersionStatusDraft);
        var rule = w.AddRule(procedure, "NEW-1");
        var findings = await w.StoredFindingsAsync();

        w.Set(ProcedureEntity.VersionEntity, version, ProcedureEntity.VersionStatusColumn, new OptionSetValue(ProcedureEntity.VersionStatusActive));
        w.Set(ProcedureEntity.VersionEntity, version, ProcedureEntity.VersionPublishedOnColumn, new DateTime(2026, 3, 5, 8, 0, 0, DateTimeKind.Utc));
        w.AddRuleVersion(rule, version, Jan1);

        ShouldBeChanged(await Ask(w, findings));
    }

    [Fact(DisplayName = "113 Changed: a version's effective date moved so a judged date falls before it")]
    public async Task VersionDateMoved_IsChanged()
    {
        var w = new ProcedureBannerWorld();
        var (_, version, _, _) = w.Simple("RATE-1");
        var findings = await w.StoredFindingsAsync();

        w.Set(ProcedureEntity.VersionEntity, version, ProcedureEntity.VersionEffectiveFromColumn, new DateTime(2026, 3, 1));

        ShouldBeChanged(await Ask(w, findings));
    }

    [Fact(DisplayName = "113 Changed: a line's work date now past a version boundary the stored resolution did not cover")]
    public async Task LineDateMovedAcrossAVersionBoundary_IsChanged()
    {
        var w = new ProcedureBannerWorld();
        var procedure = w.AddProcedure("Two versions", ProcedureAppliesTo.AllRecords);
        w.AddVersion(procedure, 1, Jan1, Mar1, ProcedureEntity.VersionStatusSuperseded);
        w.AddVersion(procedure, 2, Mar1, null);
        var findings = await w.StoredFindingsAsync(F, Feb15, Mar15);

        // The Feb-15 line is now dated after the boundary, so the judged dates are {Mar-10, Mar-15}: version 1's resolution no longer judges any line.
        var lines = new List<Entity> { Line(new DateOnly(2026, 3, 10)), Line(Mar15) };

        ShouldBeChanged(await Ask(w, findings, lines: lines));
    }

    // ── NotChanged: must not fire ──

    [Fact(DisplayName = "113 NotChanged: the current resolution equals the snapshot (overrides, a record rule and a pinned Procedure included), and nothing is written")]
    public async Task SameResolution_IsNotChanged_AndWritesNothing()
    {
        var w = new ProcedureBannerWorld();
        var (_, _, rule, ruleVersion) = w.Simple("RATE-1");
        var (assigned, _, _, _) = w.Simple("ASSIGNED-1", ProcedureAppliesTo.AssignedRecordsOnly);
        w.Assign(M, assigned, reason: ReasonSentinel);
        w.ChangeRule(M, rule, ruleVersion, Jan1);
        var findings = await w.StoredFindingsAsync();

        ShouldBeNotChanged(await Ask(w, findings));

        w.Wrote.Should().BeFalse("nothing is stored: no column, marker or cache entry");
    }

    [Fact(DisplayName = "113 NotChanged: an override for firm F does not change firm G's invoice")]
    public async Task OverrideForAnotherFirm_IsNotChanged()
    {
        var w = new ProcedureBannerWorld();
        var (_, _, rule, _) = w.Simple("RATE-1");
        var findings = await w.StoredFindingsAsync(G, Feb15, Mar15);

        w.ExcludeRule(M, rule, firm: F);

        ShouldBeNotChanged(await Ask(BannerOver(w.Entities.Object), findings, Invoice(firm: G)));
    }

    [Fact(DisplayName = "113 NotChanged: a change on an unrelated record (an assignment and an exclusion on another matter) is not this invoice's")]
    public async Task ChangeOnAnUnrelatedRecord_IsNotChanged()
    {
        var w = new ProcedureBannerWorld();
        var (_, _, rule, _) = w.Simple("RATE-1");
        var (assigned, _, _, _) = w.Simple("ASSIGNED-1", ProcedureAppliesTo.AssignedRecordsOnly);
        var findings = await w.StoredFindingsAsync();

        w.Assign(OtherMatter, assigned);
        w.ExcludeRule(OtherMatter, rule);

        ShouldBeNotChanged(await Ask(w, findings));
    }

    [Fact(DisplayName = "113 NotChanged: an Adjustment line is never evaluated, so its date does not change the judged dates")]
    public async Task AdjustmentLineDate_IsIgnored()
    {
        var w = new ProcedureBannerWorld();
        w.Simple("RATE-1", from: Feb15);
        var findings = await w.StoredFindingsAsync();
        var lines = Lines();
        lines.Add(Line(new DateOnly(2026, 1, 5), InvoiceLineCostType.Adjustment)); // before the version: it would add a "not applicable" date

        ShouldBeNotChanged(await Ask(w, findings, lines: lines));
    }

    [Fact(DisplayName = "113 NotChanged: an undated line is judged on the invoice date, as the evaluator does")]
    public async Task UndatedLine_IsJudgedOnTheInvoiceDate()
    {
        var w = new ProcedureBannerWorld();
        w.Simple("RATE-1");
        var findings = await w.StoredFindingsAsync(F, Mar15);

        ShouldBeNotChanged(await Ask(w, findings, lines: [Line(null)]));
    }

    [Fact(DisplayName = "113 NotChanged: with no invoice date the stored received date is the fallback, as the evaluator's")]
    public async Task NoInvoiceDate_FallsBackToTheStoredReceivedDate()
    {
        var w = new ProcedureBannerWorld();
        w.Simple("RATE-1");
        var findings = await w.StoredFindingsAsync(F, new DateOnly(2026, 3, 20)); // receivedOn in the stored findings
        var invoice = Invoice();
        invoice.Attributes.Remove("sprk_invoicedate");

        ShouldBeNotChanged(await Ask(BannerOver(w.Entities.Object), findings, invoice, lines: [Line(null)]));
    }

    // ── NotYetChecked: never "no change" ──

    [Fact(DisplayName = "113 NotYetChecked: an invoice with no findings yet (no submission, or one still processing) is Not yet checked, and nothing is read")]
    public async Task NoFindings_IsNotYetChecked_AndReadsNothing()
    {
        var w = new ProcedureBannerWorld();
        w.Simple("RATE-1");
        var before = w.Reads;

        ShouldBeNotYetChecked(await Ask(w, null));
        ShouldBeNotYetChecked(await Ask(w, "  "));

        w.Reads.Should().Be(before);
    }

    [Theory(DisplayName = "113 NotYetChecked: a snapshot without the 7.4 Procedure members (before v5, dropped, damaged, unknown version) is Not yet checked, never NotChanged")]
    [InlineData("""{"schema":1,"receivedOn":"2026-03-20","findings":[]}""")]
    [InlineData("""{"schema":1,"receivedOn":"2026-03-20","procedureSnapshot":null}""")]
    [InlineData("""{"schema":1,"receivedOn":"2026-03-20","procedureSnapshot":{"version":2,"applications":[],"resolutions":[]}}""")]
    [InlineData("""{"schema":1,"receivedOn":"2026-03-20","procedureSnapshot":{"version":1,"chain":[],"overrides":[]}}""")]
    [InlineData("""{"schema":1,"procedureSnapshot":"damaged"}""")]
    [InlineData("""{not json""")]
    [InlineData("""[]""")]
    public async Task UnusableSnapshot_IsNotYetChecked(string findings)
    {
        var w = new ProcedureBannerWorld();
        w.Simple("RATE-1");

        ShouldBeNotYetChecked(await Ask(w, findings));
    }

    // ── CouldNotBeChecked: a fault is never "no change", and never fails the page ──

    [Fact(DisplayName = "113 CouldNotBeChecked: a resolver read fault")]
    public async Task ResolverReadFault_IsCouldNotBeChecked()
    {
        var w = new ProcedureBannerWorld();
        w.Simple("RATE-1");
        var findings = await w.StoredFindingsAsync();

        w.World.FailingQueriesOf(ProcedureEntity.Entity);

        ShouldBeCouldNotBeChecked(await Ask(w, findings));
    }

    [Fact(DisplayName = "113 CouldNotBeChecked: a 429 from Dataverse, and the log names no row or reason")]
    public async Task TooManyRequests_IsCouldNotBeChecked_AndTheLogCarriesNoDetail()
    {
        var entities = new Mock<IGenericEntityService>(MockBehavior.Strict);
        entities.Setup(e => e.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException(ReasonSentinel + " " + ProcedureNameSentinel, null, HttpStatusCode.TooManyRequests));
        var log = new CapturingLogger<ProcedureChangeBanner>();
        var w = new ProcedureBannerWorld();
        w.Simple("RATE-1");
        var findings = await w.StoredFindingsAsync();

        ShouldBeCouldNotBeChecked(await Ask(BannerOver(entities.Object, log), findings));

        log.Messages.Should().ContainSingle();
        log.Messages[0].Should().Contain(InvoiceId.ToString()).And.NotContain("SENTINEL").And.NotContain(nameof(HttpStatusCode.TooManyRequests));
    }

    [Fact(DisplayName = "113 CouldNotBeChecked: a resolution that outlasts the timeout, with the page still answered")]
    public async Task Timeout_IsCouldNotBeChecked()
    {
        var entered = new TaskCompletionSource();
        var never = new TaskCompletionSource<EntityCollection>();
        var entities = new Mock<IGenericEntityService>(MockBehavior.Strict);
        entities.Setup(e => e.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()))
            .Returns((QueryExpression _, CancellationToken ct) =>
            {
                entered.TrySetResult();
                return never.Task.WaitAsync(ct);
            });
        var clock = new FakeTimeProvider();
        var w = new ProcedureBannerWorld();
        w.Simple("RATE-1");
        var findings = await w.StoredFindingsAsync();

        var answer = Ask(BannerOver(entities.Object, clock: clock), findings);
        await entered.Task;
        answer.IsCompleted.Should().BeFalse("the read is still waiting");
        clock.Advance(ProcedureChangeBanner.ResolutionTimeout);

        ShouldBeCouldNotBeChecked(await answer);
    }

    [Fact(DisplayName = "113 CouldNotBeChecked: two versions of one Procedure in force (a publish in progress) is a visible state, not a false NotChanged")]
    public async Task TwoVersionsInForce_IsCouldNotBeChecked()
    {
        var w = new ProcedureBannerWorld();
        var (procedure, _, _, _) = w.Simple("RATE-1");
        var findings = await w.StoredFindingsAsync();

        w.AddVersion(procedure, 2, Jan1, null); // a second version in force on the same dates

        ShouldBeCouldNotBeChecked(await Ask(w, findings));
    }

    [Fact(DisplayName = "113 CouldNotBeChecked: an invoice that names no core record, or two, cannot be resolved")]
    public async Task InvoiceWithoutASingleCoreRecord_IsCouldNotBeChecked()
    {
        var w = new ProcedureBannerWorld();
        w.Simple("RATE-1");
        var findings = await w.StoredFindingsAsync();
        var none = Invoice();
        none.Attributes.Remove("sprk_matter");
        var two = Invoice();
        two["sprk_project"] = new EntityReference("sprk_project", Guid.NewGuid());

        ShouldBeCouldNotBeChecked(await Ask(BannerOver(w.Entities.Object), findings, none));
        ShouldBeCouldNotBeChecked(await Ask(BannerOver(w.Entities.Object), findings, two));
    }

    [Fact(DisplayName = "113: a cancelled REQUEST propagates; it is not turned into a banner state")]
    public async Task CancelledRequest_Propagates()
    {
        var entities = new Mock<IGenericEntityService>(MockBehavior.Strict);
        entities.Setup(e => e.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()))
            .Returns((QueryExpression _, CancellationToken ct) =>
            {
                ct.ThrowIfCancellationRequested();
                return Task.FromResult(new EntityCollection());
            });
        var w = new ProcedureBannerWorld();
        w.Simple("RATE-1");
        var findings = await w.StoredFindingsAsync();
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        var act = () => Ask(BannerOver(entities.Object), findings, ct: cancelled.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    // ── Who gets a banner at all: absent, and nothing is read ──

    [Fact(DisplayName = "113 absent: a request without the Internal verdict, a decided invoice, and an invoice with no firm get no member and cost no read")]
    public async Task NoVerdict_DecidedInvoice_NoFirm_AreAbsent_AndReadNothing()
    {
        var w = new ProcedureBannerWorld();
        var (_, _, rule, _) = w.Simple("RATE-1");
        var findings = await w.StoredFindingsAsync();
        w.ExcludeRule(M, rule); // it would be Changed for an internal caller of an open invoice
        var banner = BannerOver(w.Entities.Object);
        var noFirm = Invoice();
        noFirm.Attributes.Remove("sprk_vendororg");
        var before = w.Reads;

        (await Ask(banner, findings, verdict: null)).Should().BeNull("step 0 did not class the caller internal");
        (await Ask(banner, findings, open: false)).Should().BeNull("a decided invoice keeps its snapshot and gets no banner");
        (await Ask(banner, findings, noFirm)).Should().BeNull("judged only by the system finding, no Procedure applies");
        (await Ask(banner, null, noFirm)).Should().BeNull("no firm and no snapshot: still exempt, not 'not yet checked'");

        w.Reads.Should().Be(before);
        (await Ask(banner, findings))!.State.Should().Be("Changed", "the same request for an internal caller of an open invoice does fire");
    }

    // ── Cost ──

    [Theory(DisplayName = "113 cost: one resolution per invoice; the number of Dataverse reads is the same for 1 and 150 lines")]
    [InlineData(1)]
    [InlineData(150)]
    public async Task ReadCount_DoesNotDependOnTheLineCount(int lineCount)
    {
        var w = new ProcedureBannerWorld();
        var (_, _, rule, ruleVersion) = w.Simple("RATE-1");
        w.ChangeRule(M, rule, ruleVersion, Jan1);
        var findings = await w.StoredFindingsAsync(F, Feb15, Mar15);
        var lines = Enumerable.Range(0, lineCount).Select(i => Line(new DateOnly(2026, 2, 1).AddDays(i % 40))).ToList();
        var before = w.World.Queries.Count;

        await Ask(w, findings, lines: lines);

        // Matter core, a Procedure All-records: the resolver's reads (overrides, Procedures, versions, rule sets, record sets, policies, policy
        // versions), each once. The same fixed number for every line count.
        (w.World.Queries.Count - before).Should().Be(7);
    }

    // ── What is serialised ──

    [Fact(DisplayName = "113 shape: the member is a state and its text, never a diff, name, id, count or reason (serialised)")]
    public async Task Serialised_CarriesTheStateAndTheTextOnly()
    {
        var w = new ProcedureBannerWorld();
        var (procedure, _, rule, _) = w.Simple("RATE-1", name: ProcedureNameSentinel);
        w.Assign(M, procedure, reason: ReasonSentinel);
        var findings = await w.StoredFindingsAsync();
        w.ExcludeRule(M, rule, reason: ReasonSentinel);
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);

        var changed = JsonSerializer.Serialize(await Ask(w, findings), options);
        var notChanged = JsonSerializer.Serialize(await Ask(BannerOver(w.Entities.Object), await w.StoredFindingsAsync()), options);

        changed.Should().Be("""{"state":"Changed","text":"The procedures that apply to this invoice have changed; re-check to apply them."}""");
        notChanged.Should().Be("""{"state":"NotChanged"}""");
    }

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<string> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Messages.Add(formatter(state, exception) + (exception is null ? string.Empty : " | " + exception));
    }
}
