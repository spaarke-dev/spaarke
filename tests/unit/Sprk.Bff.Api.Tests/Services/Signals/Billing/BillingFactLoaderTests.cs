using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Access;
using Sprk.Bff.Api.Services.Finance;
using Sprk.Bff.Api.Services.Finance.Models;
using Sprk.Bff.Api.Services.Finance.Resolution;
using Sprk.Bff.Api.Services.Finance.Submission;
using Sprk.Bff.Api.Services.Signals;
using Sprk.Bff.Api.Services.Signals.Billing;
using Sprk.Bff.Api.Tests.DataMutation.ExternalAccess;
using Sprk.Bff.Api.Telemetry;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Signals.Billing;

/// <summary>
/// ebilling-r1 task 072: <see cref="BillingFactLoader"/> over an in-memory Dataverse that evaluates each query
/// (<see cref="SecureChildShareWorld"/>), with the REAL uac-r2 walker for the secure roots. Which rules are read (Published rule
/// sets of the client default and this firm; enabled billing policies; versions that can be in force, K3), the priors (D-112: every
/// partition, this submission excluded), the secure-root sets under uac-r2's four calling conditions, bounded reads, and no writes.
/// The D-112 cases run end to end through <see cref="InvoiceComplianceEvaluator.EvaluateAsync"/> with 071's Duplicate handler.
/// </summary>
public class BillingFactLoaderTests
{
    private const int Published = 659490000;
    private const int Billing = RuleDomains.PolicyDomainBilling;

    private static readonly Guid FirmId = Guid.Parse("07200000-0000-4000-8000-0000000000f1");
    private static readonly Guid OtherFirmId = Guid.Parse("07200000-0000-4000-8000-0000000000f2");
    private static readonly Guid SubmissionId = Guid.Parse("07200000-0000-4000-8000-0000000000b1");
    private static readonly Guid MatterA = Guid.Parse("07200000-0000-4000-8000-00000000a0a0");
    private static readonly Guid MatterB = Guid.Parse("07200000-0000-4000-8000-00000000b0b0");
    private static readonly Guid OrdinaryMatter = Guid.Parse("07200000-0000-4000-8000-00000000c0c0");
    private static readonly DateTime RulesFrom = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private SecureChildShareWorld _world = SecureChildShareWorld.Standard();
    private readonly Mock<IGenericEntityService> _entities;

    public BillingFactLoaderTests()
    {
        _entities = SecureChildShareWorld.EntitiesOver(() => _world);
    }

    // ── World builders ───────────────────────────────────────────────────────────────────────────────────────────

    private static EntityReference Ref(string table, Guid id) => new(table, id);

    private static EntityReference SecureTeam => new("team", SecureChildShareWorld.SecureTeam);

    private Guid RuleSet(RuleSetScope scope, Guid? firm = null, int status = Published, int? domain = Billing)
    {
        var id = Guid.NewGuid();
        var columns = new List<(string, object)> { ("sprk_scope", new OptionSetValue((int)scope)), ("statuscode", new OptionSetValue(status)) };
        if (domain is { } d) columns.Add((RuleDomains.PolicyDomainColumn, new OptionSetValue(d)));
        if (firm is { } f) columns.Add(("sprk_firm", Ref("sprk_organization", f)));
        _world.Add("sprk_ruleset", id, columns.ToArray());
        return id;
    }

    /// <summary>A policy with one current version of <paramref name="type"/>; returns (policy id, version id).</summary>
    private (Guid Policy, Guid Version) Policy(Guid ruleSet, string code, RuleType type, string body, bool enabled = true, int state = 0)
    {
        var policy = Guid.NewGuid();
        var version = Version(policy, 1, type, body, RulesFrom, null);
        _world.Add("sprk_policy", policy,
            (RuleDomains.PolicyDomainColumn, new OptionSetValue(Billing)), ("sprk_ruleset", Ref("sprk_ruleset", ruleSet)),
            ("sprk_enabled", enabled), ("statecode", new OptionSetValue(state)), ("sprk_policycode", code),
            ("sprk_currentversion", Ref("sprk_policyversion", version)), ("sprk_lane", new OptionSetValue(SignalWriter.LaneDecide)));
        return (policy, version);
    }

    private Guid Version(Guid policy, int number, RuleType type, string body, DateTime from, DateTime? to, int? proposalStatus = null)
    {
        var id = Guid.NewGuid();
        var columns = new List<(string, object)>
        {
            ("sprk_policy", Ref("sprk_policy", policy)), ("sprk_versionnumber", number), ("sprk_ruletype", new OptionSetValue((int)type)),
            ("sprk_rulebody", body), ("sprk_inforcefrom", from), ("sprk_clausereference", "Clause " + number),
        };
        if (to is { } end) columns.Add(("sprk_inforceto", end));
        if (proposalStatus is { } s) columns.Add(("sprk_proposalstatus", new OptionSetValue(s)));
        _world.Add("sprk_policyversion", id, columns.ToArray());
        return id;
    }

    private const string DuplicateByNumber = """{"type":"Duplicate","matchFields":["firmInvoiceNumber"]}""";

    private const string DuplicateByLine = """{"type":"Duplicate","matchFields":["firmInvoiceNumber","timekeeper","lineDate","hours"]}""";

    private void DuplicateRule(string body = DuplicateByNumber) => Policy(RuleSet(RuleSetScope.ClientDefault), "CD-DUPLICATE", RuleType.Duplicate, body);

    private static EntityReference OrdinaryTeam => new("team", SecureChildShareWorld.GeneralTeam);

    /// <summary>
    /// A prior of the firm with an invoice filed through <paramref name="filing"/> (column, target table, target id). The invoice is
    /// owned as the ownership resolver would own it: by the Secure team when <paramref name="secureOwned"/> (filed under a secure
    /// root), else by an ordinary team (072 review A1 (c): an ordinary prior is never Secure-owned).
    /// </summary>
    private Guid PriorWithInvoice(string? trackingNumber, (string Column, string Table, Guid Id)? filing, int invoiceStatus = 1,
        int processing = (int)InvoiceSubmissionStatus.Accepted, Guid? firm = null, Guid? submissionId = null, string firmInvoiceNumber = "INV-100",
        bool secureOwned = false, (string Column, string Table, Guid Id)? submissionFiling = null)
    {
        var invoice = Guid.NewGuid();
        var invoiceColumns = new List<(string, object)>
        {
            ("statuscode", new OptionSetValue(invoiceStatus)), ("owningteam", secureOwned ? SecureTeam : OrdinaryTeam),
        };
        if (filing is { } f) invoiceColumns.Add((f.Column, Ref(f.Table, f.Id)));
        _world.Add("sprk_invoice", invoice, invoiceColumns.ToArray());
        return Submission(trackingNumber, invoice, processing, firm, submissionId, firmInvoiceNumber, submissionFiling);
    }

    private Guid Submission(string? trackingNumber, Guid? invoice, int processing = (int)InvoiceSubmissionStatus.Accepted,
        Guid? firm = null, Guid? id = null, string firmInvoiceNumber = "INV-100", (string Column, string Table, Guid Id)? filing = null)
    {
        var submission = id ?? Guid.NewGuid();
        var columns = new List<(string, object)>
        {
            ("sprk_vendororg", Ref("sprk_organization", firm ?? FirmId)),
            ("sprk_firminvoicenumber", firmInvoiceNumber), ("sprk_submissionsequence", 1),
            ("sprk_processingstatus", new OptionSetValue(processing)),
        };
        if (trackingNumber is not null) columns.Add(("sprk_trackingnumber", trackingNumber));
        if (invoice is { } i) columns.Add(("sprk_invoice", Ref("sprk_invoice", i)));
        if (filing is { } f) columns.Add((f.Column, Ref(f.Table, f.Id)));
        _world.Add("sprk_invoicesubmission", submission, columns.ToArray());
        return submission;
    }

    private static BillingEvaluationRequest Request(Guid? coreMatter, int lines = 1) => new()
    {
        SubmissionId = SubmissionId,
        FirmId = FirmId,
        CoreRecord = coreMatter is { } m ? new CoreRecordReference(CoreRecordKind.Matter, m) : null,
        TrackingNumber = "INV-100.2",
        FirmInvoiceNumber = "INV-100",
        InvoiceDate = new DateOnly(2026, 3, 31),
        ReceivedOn = new DateOnly(2026, 4, 2),
        Currency = "USD",
        Lines = Enumerable.Range(0, lines).Select(i => new BillingLineFacts
        {
            Index = i,
            LineType = Sprk.Bff.Api.Services.Ai.PublicContracts.InvoiceLineType.Fee,
            Date = new DateOnly(2026, 3, 2),
            TimekeeperName = "Jane Doe",
            Hours = 1m,
        }).ToList(),
    };

    private ILogger<BillingFactLoader> _loaderLogger = NullLogger<BillingFactLoader>.Instance;

    private BillingFactLoader Loader()
    {
        var settings = new Mock<IEbillingSettingsReader>(MockBehavior.Strict);
        settings.Setup(s => s.GetAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new EbillingSettings { ReliabilityThreshold = 0.85m });
        var configuration = SecureChildShareWorld.Configuration();
        var walker = new SecureChildShareSynchronizer(_entities.Object, null!, null!, configuration, NullLogger<SecureChildShareSynchronizer>.Instance);
        return new BillingFactLoader(_entities.Object, settings.Object, walker, configuration, new FinanceTelemetry(), _loaderLogger);
    }

    private InvoiceComplianceEvaluator Evaluator() =>
        new(Loader(), Sprk.Bff.Api.Tests.Domain.Signals.Billing.Handlers.BillingHandlerKit.Gate(), NullLogger<InvoiceComplianceEvaluator>.Instance);

    /// <summary>The evaluator and loader read only: the strict entity double has no Create, and no Update or Delete was made.</summary>
    private void AssertWroteNothing()
    {
        _entities.Verify(e => e.UpdateAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<Dictionary<string, object>>(), It.IsAny<CancellationToken>()), Times.Never());
        _entities.Verify(e => e.DeleteAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never());
        _world.Deletes.Should().BeEmpty();
    }

    private async Task<BillingFinding> TheDuplicateFindingAsync(Guid? coreMatter)
    {
        var result = await Evaluator().EvaluateAsync(Request(coreMatter), CancellationToken.None);
        AssertWroteNothing();
        return result.Findings.Should().ContainSingle(f => f.RuleType == RuleType.Duplicate).Subject;
    }

    // ── Rules ────────────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Rules_AreThePublishedBillingRuleSets_OfTheClientDefaultAndThisFirm_Only()
    {
        const string body = """{"type":"TimeIncrement","incrementHours":0.5}""";
        Policy(RuleSet(RuleSetScope.ClientDefault), "CD", RuleType.TimeIncrement, body);
        Policy(RuleSet(RuleSetScope.Firm, FirmId), "FIRM", RuleType.TimeIncrement, body);
        Policy(RuleSet(RuleSetScope.Firm, OtherFirmId), "OTHER-FIRM", RuleType.TimeIncrement, body);
        Policy(RuleSet(RuleSetScope.ClientDefault, status: 1), "DRAFT-SET", RuleType.TimeIncrement, body);
        Policy(RuleSet(RuleSetScope.ClientDefault, status: 2), "RETIRED-SET", RuleType.TimeIncrement, body);
        Policy(RuleSet(RuleSetScope.ClientDefault, domain: RuleDomains.PolicyDomainOntology), "ONTOLOGY-SET", RuleType.TimeIncrement, body);
        Policy(RuleSet(RuleSetScope.ClientDefault, domain: null), "NO-DOMAIN-SET", RuleType.TimeIncrement, body);
        var set = RuleSet(RuleSetScope.ClientDefault);
        Policy(set, "DISABLED", RuleType.TimeIncrement, body, enabled: false);
        Policy(set, "INACTIVE", RuleType.TimeIncrement, body, state: 1);

        var loaded = await Loader().LoadAsync(Request(null), CancellationToken.None);

        loaded.Rules.Select(r => (r.PolicyCode, r.Scope)).Should().Equal(("CD", RuleSetScope.ClientDefault), ("FIRM", RuleSetScope.Firm));
        var rule = loaded.Rules[0];
        rule.RuleType.Should().Be("100000005", "the gate reads the option value as invariant text");
        rule.RuleBody.Should().Be(body);
        rule.IsCurrent.Should().BeTrue();
        rule.InForceFrom.Should().Be(new DateOnly(2026, 1, 1));
        rule.Lane.Should().Be(SignalWriter.LaneDecide);
        loaded.ReliabilityThreshold.Should().Be(0.85m);
        AssertWroteNothing();
    }

    [Fact]
    public async Task APublishedProceduresRuleSet_IsNotEvaluatedHere_UntilTheResolverReadsProcedureVersions_Task081()
    {
        // ebilling-r1 task 081: this set-level reader cannot honour a Procedure Version's window, Applies to or dropped rules, so a
        // Procedure's set (sprk_procedure set) is skipped whatever its scope; an ordinary set beside it still loads.
        const string body = """{"type":"TimeIncrement","incrementHours":0.5}""";
        Policy(RuleSet(RuleSetScope.ClientDefault), "CD", RuleType.TimeIncrement, body);
        var procedureDefault = RuleSet(RuleSetScope.ClientDefault);
        _world.Set("sprk_ruleset", procedureDefault, "sprk_procedure", Ref("sprk_procedure", Guid.NewGuid()));
        Policy(procedureDefault, "PROC-ALL", RuleType.TimeIncrement, body);
        var procedureFirm = RuleSet(RuleSetScope.Firm, FirmId);
        _world.Set("sprk_ruleset", procedureFirm, "sprk_procedure", Ref("sprk_procedure", Guid.NewGuid()));
        Policy(procedureFirm, "PROC-FIRM", RuleType.TimeIncrement, body);

        var loaded = await Loader().LoadAsync(Request(null), CancellationToken.None);

        loaded.Rules.Select(r => r.PolicyCode).Should().Equal("CD");
        AssertWroteNothing();
    }

    [Fact]
    public async Task DraftAndRejectedVersionsWithNoEndDate_AreNotReturned_SupersededAndCurrentAre_K3()
    {
        const string body = """{"type":"TimeIncrement","incrementHours":0.5}""";
        var (policy, current) = Policy(RuleSet(RuleSetScope.ClientDefault), "CD", RuleType.TimeIncrement, body);
        var superseded = Version(policy, 0, RuleType.TimeIncrement, body, RulesFrom.AddMonths(-6), RulesFrom);
        Version(policy, 2, RuleType.TimeIncrement, body, RulesFrom, null);                                     // a draft
        Version(policy, 3, RuleType.TimeIncrement, body, RulesFrom, null, proposalStatus: (int)ProposalState.Rejected); // rejected

        var loaded = await Loader().LoadAsync(Request(null), CancellationToken.None);

        loaded.Rules.Select(r => r.PolicyVersionId).Should().BeEquivalentTo([superseded, current]);
        loaded.Rules.Single(r => r.PolicyVersionId == superseded).InForceTo.Should().Be(new DateOnly(2026, 1, 1));
    }

    [Fact]
    public async Task WithNoDuplicateRule_NoPriorIsRead_AndTheSecureRootsAreNotWalked()
    {
        Policy(RuleSet(RuleSetScope.ClientDefault), "CD", RuleType.TimeIncrement, """{"type":"TimeIncrement","incrementHours":0.5}""");
        PriorWithInvoice("INV-100.1", ("sprk_matter", "sprk_matter", OrdinaryMatter));

        var loaded = await Loader().LoadAsync(Request(null), CancellationToken.None);

        loaded.PriorSubmissions.Should().BeEmpty();
        loaded.InvoiceSecureRoots.Should().BeNull();
        _world.QueriedTables.Should().NotContain(["sprk_invoicesubmission", "sprk_invoice", "sprk_billingevent", "team"]);
    }

    // ── Core record ──────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task TheCoreRecord_ReadsItsDatesAndState_AndAMissingOne_IsNull()
    {
        var closed = Guid.NewGuid();
        _world.Add("sprk_matter", closed,
            ("sprk_openeddate", new DateTime(2025, 6, 1)), ("sprk_closeddate", new DateTime(2026, 2, 28)), ("statecode", new OptionSetValue(1)));

        var found = await Loader().LoadAsync(Request(closed), CancellationToken.None);
        var missing = await Loader().LoadAsync(Request(Guid.NewGuid()), CancellationToken.None);

        found.CoreRecord.Should().Be(new BillingCoreRecordFacts { OpenDate = new DateOnly(2025, 6, 1), CloseDate = new DateOnly(2026, 2, 28), IsClosed = true });
        missing.CoreRecord.Should().BeNull();
    }

    [Theory]
    [InlineData(CoreRecordKind.Matter, "sprk_matter")]
    [InlineData(CoreRecordKind.Project, "sprk_project")]
    [InlineData(CoreRecordKind.WorkAssignment, "sprk_workassignment")]
    public async Task TheCoreRecord_ReadsItsBillingStatusAndBothDates_OnEveryCoreTable_AWorkAssignmentIncluded_D116(CoreRecordKind kind, string table)
    {
        var id = Guid.NewGuid();
        _world.Add(table, id,
            ("sprk_openeddate", new DateTime(2026, 1, 10)), ("sprk_closeddate", new DateTime(2026, 3, 20)),
            ("statecode", new OptionSetValue(0)), ("sprk_billingstatus", new OptionSetValue(CoreRecordBillingStatus.Final)));
        var request = Request(null) with { CoreRecord = new CoreRecordReference(kind, id) };

        var loaded = await Loader().LoadAsync(request, CancellationToken.None);

        loaded.CoreRecord.Should().Be(new BillingCoreRecordFacts
        {
            OpenDate = new DateOnly(2026, 1, 10),
            CloseDate = new DateOnly(2026, 3, 20),
            IsClosed = false,
            BillingStatus = CoreRecordBillingStatus.Final,
        });
        AssertWroteNothing();
    }

    [Fact]
    public async Task AWorkAssignmentClosedToBilling_FlagsEveryChargeLine_EndToEnd_D116()
    {
        var assignment = Guid.NewGuid();
        _world.Add("sprk_workassignment", assignment,
            ("sprk_openeddate", new DateTime(2026, 1, 10)), ("sprk_closeddate", new DateTime(2026, 12, 31)),
            ("statecode", new OptionSetValue(0)), ("sprk_billingstatus", new OptionSetValue(CoreRecordBillingStatus.Closed)));
        Policy(RuleSet(RuleSetScope.ClientDefault), "CD-WINDOW", RuleType.CoreRecordWindow, """{"type":"CoreRecordWindow","graceDays":0}""");
        var request = Request(null, lines: 2) with { CoreRecord = new CoreRecordReference(CoreRecordKind.WorkAssignment, assignment) };

        var result = await Evaluator().EvaluateAsync(request, CancellationToken.None);

        result.Findings.Select(f => (f.Subject.LineIndex, f.ReasonCode)).Should().Equal(
            (0, BillingReasonCodes.CoreRecordClosedToBilling), (1, BillingReasonCodes.CoreRecordClosedToBilling));
        AssertWroteNothing();
    }

    [Fact]
    public async Task AnUnknownBillingStatus_IsLoadedAsRead_AndLoggedAsAWarning_D116()
    {
        var matter = Guid.NewGuid();
        _world.Add("sprk_matter", matter, ("statecode", new OptionSetValue(0)), ("sprk_billingstatus", new OptionSetValue(100000099)));
        var logger = new Sprk.Bff.Api.Tests.Domain.Signals.Billing.BillingEvaluationKit.CapturingLogger<BillingFactLoader>();
        _loaderLogger = logger;

        var loaded = await Loader().LoadAsync(Request(matter), CancellationToken.None);

        loaded.CoreRecord!.BillingStatus.Should().Be(100000099, "the handler treats a value outside the set as none (D-111)");
        logger.Entries.Should().Contain(e => e.Level == LogLevel.Warning && e.Message.Contains("sprk_billingstatus 100000099"));
    }

    // ── Priors (D-112, FR-36) ────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Priors_AreEverySubmissionOfThisFirm_ThisOneExcluded_WithTheirStatusAndLines()
    {
        DuplicateRule(DuplicateByLine);
        Submission("INV-100.2", invoice: null, id: SubmissionId);                                                  // this one
        var active = PriorWithInvoice("INV-100.1", ("sprk_matter", "sprk_matter", OrdinaryMatter));
        var rejected = PriorWithInvoice("INV-101.1", null, invoiceStatus: InvoiceStatusCodes.Rejected);
        var returned = PriorWithInvoice("INV-102.1", null, invoiceStatus: InvoiceStatusCodes.ReturnedToFirm);
        PriorWithInvoice("INV-103.1", null, invoiceStatus: InvoiceStatusCodes.Cancelled);                         // never payable
        Submission("INV-104.1", invoice: null, processing: (int)InvoiceSubmissionStatus.Failed);                // never payable
        var rejectedAtIntake = Submission("INV-105.1", invoice: null, processing: (int)InvoiceSubmissionStatus.Rejected);
        var received = Submission("INV-106.1", invoice: null, processing: (int)InvoiceSubmissionStatus.Received);
        PriorWithInvoice("INV-100.1", null, firm: OtherFirmId);                                                    // another firm

        var activeInvoice = _world.ValueOf<EntityReference>("sprk_invoicesubmission", active, "sprk_invoice")!.Id;
        _world.Add("sprk_billingevent", Guid.NewGuid(), ("sprk_invoice", Ref("sprk_invoice", activeInvoice)), ("sprk_linesequence", 2),
            ("sprk_costtype", new OptionSetValue(100000001)), ("sprk_quantity", 3m), ("sprk_amount", new Money(40m)), ("sprk_eventdate", new DateTime(2026, 2, 3)));
        _world.Add("sprk_billingevent", Guid.NewGuid(), ("sprk_invoice", Ref("sprk_invoice", activeInvoice)), ("sprk_linesequence", 1),
            ("sprk_costtype", new OptionSetValue(100000000)), ("sprk_quantity", 1.5m), ("sprk_timekeeper", "Jane Doe"),
            ("sprk_amount", new Money(862.5m)), ("sprk_eventdate", new DateTime(2026, 2, 2)), ("sprk_description", "Draft motion"));

        var rejectedInvoice = _world.ValueOf<EntityReference>("sprk_invoicesubmission", rejected, "sprk_invoice")!.Id;
        _world.Add("sprk_billingevent", Guid.NewGuid(), ("sprk_invoice", Ref("sprk_invoice", rejectedInvoice)), ("sprk_linesequence", 1));

        var loaded = await Loader().LoadAsync(Request(null), CancellationToken.None);

        loaded.PriorSubmissions.Single(p => p.SubmissionId == rejected).Lines.Should().BeEmpty("B1: Duplicate compares only Active priors' lines");
        loaded.PriorSubmissions.Select(p => (p.SubmissionId, p.Status)).Should().BeEquivalentTo(new[]
        {
            (active, BillingPriorStatus.Active), (rejected, BillingPriorStatus.Rejected), (returned, BillingPriorStatus.ReturnedToFirm),
            (rejectedAtIntake, BillingPriorStatus.Rejected), (received, BillingPriorStatus.Active),
        });
        loaded.PriorSubmissions.Should().NotContain(p => p.SubmissionId == SubmissionId, "K1: never its own prior");
        // D-176 / #2118: the submission's invoice lookup is carried, so a visible Duplicate can cite the earlier invoice; none when it has none.
        loaded.PriorSubmissions.Single(p => p.SubmissionId == active).InvoiceId.Should().Be(activeInvoice);
        loaded.PriorSubmissions.Single(p => p.SubmissionId == received).InvoiceId.Should().BeNull();
        loaded.PriorSubmissions.Single(p => p.SubmissionId == active).Lines.Should().Equal(
            new BillingPriorLine { Date = new DateOnly(2026, 2, 2), TimekeeperName = "Jane Doe", Hours = 1.5m, Narrative = "Draft motion", Amount = 862.5m },
            new BillingPriorLine { Date = new DateOnly(2026, 2, 3), Hours = null, Amount = 40m });
        AssertWroteNothing();
    }

    // Task 097a (ontology-r1 088 gate F1): a corrected invoice keeps the firm's number (D-37) and supersedes the invoice it replaced, so that
    // invoice must not count as an earlier use of the number. Like Cancelled, a Superseded invoice is no prior at all.

    [Fact]
    public async Task MustFire_ASupersededPredecessor_IsNoPrior_SoTheCorrectedInvoiceRaisesNoDuplicate_Task097a()
    {
        DuplicateRule();
        _world.OrdinaryRoot("sprk_matter", OrdinaryMatter);
        PriorWithInvoice("INV-100.1", ("sprk_matter", "sprk_matter", OrdinaryMatter), invoiceStatus: InvoiceStatusCodes.Superseded);

        var loaded = await Loader().LoadAsync(Request(OrdinaryMatter), CancellationToken.None);
        var result = await Evaluator().EvaluateAsync(Request(OrdinaryMatter), CancellationToken.None);

        loaded.PriorSubmissions.Should().BeEmpty("the replaced invoice is not an earlier use of the firm's number");
        result.Findings.Should().NotContain(f => f.RuleType == RuleType.Duplicate);
        AssertWroteNothing();
    }

    [Theory]
    [InlineData(InvoiceStatusCodes.InReview)]
    [InlineData(InvoiceStatusCodes.Approved)]
    public async Task MustNotFire_AnInReviewOrApprovedPrior_OfTheSameNumber_IsStillADuplicate_Task097a(int priorStatus)
    {
        DuplicateRule();
        _world.OrdinaryRoot("sprk_matter", OrdinaryMatter);
        var prior = PriorWithInvoice("INV-100.1", ("sprk_matter", "sprk_matter", OrdinaryMatter), invoiceStatus: priorStatus);

        var loaded = await Loader().LoadAsync(Request(OrdinaryMatter), CancellationToken.None);
        var finding = await TheDuplicateFindingAsync(OrdinaryMatter);

        loaded.PriorSubmissions.Should().ContainSingle(p => p.SubmissionId == prior && p.Status == BillingPriorStatus.Active);
        finding.ReasonCode.Should().Be(BillingReasonCodes.DuplicateInvoiceNumber);
    }

    [Fact]
    public async Task TheReads_AreBounded_WhateverTheNumberOfLines()
    {
        DuplicateRule(DuplicateByLine);
        var prior = PriorWithInvoice("INV-100.1", ("sprk_matter", "sprk_matter", OrdinaryMatter));
        var priorInvoice = _world.ValueOf<EntityReference>("sprk_invoicesubmission", prior, "sprk_invoice")!.Id;
        _world.SecureRoot("sprk_matter", MatterA);
        _world.OrdinaryRoot("sprk_matter", OrdinaryMatter);
        _world.Add("sprk_billingevent", Guid.NewGuid(), ("sprk_invoice", Ref("sprk_invoice", priorInvoice)));

        await Loader().LoadAsync(Request(MatterA, lines: 1), CancellationToken.None);
        var few = _world.QueriedTables.ToList();
        _world.QueriedTables.Clear();
        for (var i = 0; i < 149; i++)
        {
            _world.Add("sprk_billingevent", Guid.NewGuid(), ("sprk_invoice", Ref("sprk_invoice", priorInvoice)), ("sprk_linesequence", i));
        }

        var loaded = await Loader().LoadAsync(Request(MatterA, lines: 150), CancellationToken.None);

        _world.QueriedTables.Should().Equal(few, "150 lines on the invoice and 150 on a prior take the same reads as one");
        loaded.PriorSubmissions.Single().Lines.Should().HaveCount(150);
    }

    [Fact]
    public async Task PriorLines_AreReadOnlyWhenADuplicateRuleComparesLines_AndOnlyForActivePriors_B1()
    {
        DuplicateRule(); // firm invoice number only: no line is compared
        var active = PriorWithInvoice("INV-100.1", null);
        var rejected = PriorWithInvoice("INV-101.1", null, invoiceStatus: InvoiceStatusCodes.Rejected);
        foreach (var submission in new[] { active, rejected })
        {
            var invoice = _world.ValueOf<EntityReference>("sprk_invoicesubmission", submission, "sprk_invoice")!.Id;
            _world.Add("sprk_billingevent", Guid.NewGuid(), ("sprk_invoice", Ref("sprk_invoice", invoice)), ("sprk_linesequence", 1));
        }

        var byNumber = await Loader().LoadAsync(Request(null), CancellationToken.None);
        var byNumberLineReads = _world.QueriedTables.Count(t => t == "sprk_billingevent");
        _world.QueriedTables.Clear();
        Policy(RuleSet(RuleSetScope.Firm, FirmId), "FIRM-DUPLICATE", RuleType.Duplicate, DuplicateByLine);
        var byLine = await Loader().LoadAsync(Request(null), CancellationToken.None);

        byNumberLineReads.Should().Be(0, "no Duplicate rule names a line field");
        byNumber.PriorSubmissions.Should().HaveCount(2).And.OnlyContain(p => p.Lines.Count == 0);
        _world.QueriedTables.Count(t => t == "sprk_billingevent").Should().Be(1, "one chunk of Active invoices");
        byLine.PriorSubmissions.Single(p => p.SubmissionId == active).Lines.Should().ContainSingle();
        byLine.PriorSubmissions.Single(p => p.SubmissionId == rejected).Lines.Should().BeEmpty();
    }

    [Fact]
    public async Task APriorsRemovedAdjustment_IsNotAPriorLine_ButItsDeactivatedFeeLineIs_Task102()
    {
        // Task 102 (NFR-09): a removed invoice-level adjustment is deactivated, never deleted. It stays out of every count of the invoice's lines,
        // the prior lines a Duplicate rule compares included. A deactivated Fee line is still a line the firm billed.
        Policy(RuleSet(RuleSetScope.Firm, FirmId), "FIRM-DUPLICATE", RuleType.Duplicate, DuplicateByLine);
        var prior = PriorWithInvoice("INV-100.1", null);
        var invoice = _world.ValueOf<EntityReference>("sprk_invoicesubmission", prior, "sprk_invoice")!.Id;
        void Line(int sequence, int costType, int state) => _world.Add("sprk_billingevent", Guid.NewGuid(),
            ("sprk_invoice", Ref("sprk_invoice", invoice)), ("sprk_linesequence", sequence), ("sprk_costtype", new OptionSetValue(costType)),
            ("sprk_amount", new Money(10m * sequence)), ("statecode", new OptionSetValue(state)));
        Line(1, 100000000, 0);   // an active Fee line
        Line(2, 100000000, 1);   // a deactivated Fee line
        Line(3, 100000002, 0);   // an active Adjustment
        Line(4, 100000002, 1);   // a removed Adjustment

        var loaded = await Loader().LoadAsync(Request(null), CancellationToken.None);

        loaded.PriorSubmissions.Single().Lines.Select(l => l.Amount).Should().Equal(10m, 20m, 30m);
    }

    [Theory]
    [InlineData("""{"type":"Duplicate","matchFields":["firmInvoiceNumber"]}""", false)]
    [InlineData("""{"type":"Duplicate","matchFields":["narrative"]}""", true)]
    [InlineData("""{"type":"Duplicate","matchFields":[]}""", false)]
    [InlineData("""{"type":"Duplicate"}""", true)]
    [InlineData("not json", true)]
    public void ComparesLines_IsTrueWhenADuplicateBodyNamesALineField_OrCannotBeRead_B1(string body, bool expected)
    {
        var rule = new BillingRuleCandidate
        {
            PolicyId = Guid.NewGuid(), PolicyCode = "D", PolicyVersionId = Guid.NewGuid(), RuleSetId = Guid.NewGuid(),
            Scope = RuleSetScope.ClientDefault, RuleType = ((int)RuleType.Duplicate).ToString(System.Globalization.CultureInfo.InvariantCulture),
            RuleBody = body, IsCurrent = true,
        };

        BillingFactLoader.ComparesLines([rule]).Should().Be(expected);
        BillingFactLoader.ComparesLines([rule with { RuleType = ((int)RuleType.TimeIncrement).ToString(System.Globalization.CultureInfo.InvariantCulture) }])
            .Should().BeFalse("only a Duplicate rule compares prior lines");
    }

    // ── D-112 secure roots, end to end ───────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task APriorUnderAnotherSecureRoot_ShowsNoDetail_D112()
    {
        DuplicateRule();
        _world.SecureRoot("sprk_matter", MatterA).SecureRoot("sprk_matter", MatterB);
        PriorWithInvoice("INV-100.1", ("sprk_matter", "sprk_matter", MatterB), secureOwned: true);

        var finding = await TheDuplicateFindingAsync(MatterA);

        finding.DetailWithheld.Should().BeTrue();
        finding.FactValues.Should().BeEmpty();
    }

    [Fact]
    public async Task APriorUnderTheSameSecureRoot_KeepsItsDetail_D112()
    {
        DuplicateRule();
        _world.SecureRoot("sprk_matter", MatterA);
        PriorWithInvoice("INV-100.1", ("sprk_matter", "sprk_matter", MatterA), secureOwned: true);

        var finding = await TheDuplicateFindingAsync(MatterA);

        finding.DetailWithheld.Should().BeFalse();
        finding.FactValues["duplicateOf.trackingNumber"].Should().Be("INV-100.1");
    }

    [Fact]
    public async Task AnOrdinaryPrior_OnASecureInvoice_KeepsItsDetail_D112()
    {
        DuplicateRule();
        _world.SecureRoot("sprk_matter", MatterA).OrdinaryRoot("sprk_matter", OrdinaryMatter);
        PriorWithInvoice("INV-100.1", ("sprk_matter", "sprk_matter", OrdinaryMatter));

        var finding = await TheDuplicateFindingAsync(MatterA);

        finding.DetailWithheld.Should().BeFalse();
    }

    [Fact]
    public async Task ASecureOwnedPrior_WhoseInvoiceLookupWasCleared_ButWhoseSubmissionNamesAnotherSecureMatter_IsWithheld_A1()
    {
        // 072 review A1: a grid edit cleared sprk_matter on a secure prior invoice; its submission still names secure matter B. Walked
        // through the invoice alone the prior had the empty set (ordinary), a subset of {A}, and its detail was shown.
        DuplicateRule();
        _world.SecureRoot("sprk_matter", MatterA).SecureRoot("sprk_matter", MatterB);
        PriorWithInvoice("INV-100.1", filing: null, secureOwned: true, submissionFiling: ("sprk_regardingmatter", "sprk_matter", MatterB));

        var loaded = await Loader().LoadAsync(Request(MatterA), CancellationToken.None);
        var finding = await TheDuplicateFindingAsync(MatterA);

        // The Secure-owned invoice reaches no root itself, so the prior is unresolved (A1 (b)); were the invoice's own set ignored,
        // the union with its submission's would still be {B}, not a subset of {A}.
        loaded.PriorSubmissions.Should().ContainSingle().Which.SecureRoots.Should().BeNull();
        finding.DetailWithheld.Should().BeTrue();
        finding.FactValues.Should().BeEmpty();
    }

    [Fact]
    public async Task ASecureOwnedPrior_WhoseLookupsReachNoSecureRoot_IsUnresolved_AndWithheld_A1()
    {
        // 072 review A1 (b): the Secure team owns the prior, so it IS secure, but no lookup says under which root. Not the empty set.
        DuplicateRule();
        _world.SecureRoot("sprk_matter", MatterA);
        PriorWithInvoice("INV-100.1", filing: null, secureOwned: true);

        var loaded = await Loader().LoadAsync(Request(MatterA), CancellationToken.None);
        var finding = await TheDuplicateFindingAsync(MatterA);

        loaded.InvoiceSecureRoots.Should().BeEquivalentTo([MatterA]);
        loaded.PriorSubmissions.Should().ContainSingle().Which.SecureRoots.Should().BeNull();
        finding.DetailWithheld.Should().BeTrue();
    }

    [Fact]
    public async Task APriorWhoseSubmissionIsUnderASecureRoot_AndWhoseInvoiceIsUnderAnOrdinaryOne_IsWithheld_A1()
    {
        DuplicateRule();
        _world.OrdinaryRoot("sprk_matter", OrdinaryMatter).SecureRoot("sprk_matter", MatterB);
        PriorWithInvoice("INV-100.1", ("sprk_matter", "sprk_matter", OrdinaryMatter),
            submissionFiling: ("sprk_regardingmatter", "sprk_matter", MatterB));

        var loaded = await Loader().LoadAsync(Request(OrdinaryMatter), CancellationToken.None);
        var finding = await TheDuplicateFindingAsync(OrdinaryMatter);

        loaded.PriorSubmissions.Should().ContainSingle().Which.SecureRoots.Should().BeEquivalentTo([MatterB]);
        finding.DetailWithheld.Should().BeTrue();
    }

    [Fact]
    public async Task AnAmbiguousSecureOwnerTeam_LeavesEverySetUnresolved_AndTheDuplicateIsWithheld_UacR2Condition3_A2()
    {
        // Two owner teams of the Secure Record BU carry the configured name: the walk cannot tell which rows are secure (Failed).
        // Empty sets here (the mutation that survived review) would read every record as ordinary and show the prior's detail.
        _world.Team(Guid.NewGuid(), SecureChildShareWorld.SecureBu, SecureChildShareWorld.SecureOwnerTeamName, isDefault: false, teamType: 0);
        DuplicateRule();
        _world.OrdinaryRoot("sprk_matter", OrdinaryMatter);
        PriorWithInvoice("INV-100.1", ("sprk_matter", "sprk_matter", OrdinaryMatter));
        PriorWithInvoice("INV-100.3", null);

        var loaded = await Loader().LoadAsync(Request(OrdinaryMatter), CancellationToken.None);
        var finding = await TheDuplicateFindingAsync(OrdinaryMatter);

        loaded.InvoiceSecureRoots.Should().BeNull();
        loaded.PriorSubmissions.Should().HaveCount(2).And.OnlyContain(p => p.SecureRoots == null);
        finding.DetailWithheld.Should().BeTrue();
        finding.FactValues.Should().BeEmpty();
    }

    [Fact]
    public async Task AVisiblePriorWithNoTrackingNumber_IsCitedWithTheGenericText_NeverAnEmptyNumber_B4()
    {
        DuplicateRule(DuplicateByLine);
        _world.OrdinaryRoot("sprk_matter", OrdinaryMatter);
        var prior = PriorWithInvoice(trackingNumber: null, ("sprk_matter", "sprk_matter", OrdinaryMatter));
        var priorInvoice = _world.ValueOf<EntityReference>("sprk_invoicesubmission", prior, "sprk_invoice")!.Id;
        _world.Add("sprk_billingevent", Guid.NewGuid(), ("sprk_invoice", Ref("sprk_invoice", priorInvoice)), ("sprk_linesequence", 1),
            ("sprk_costtype", new OptionSetValue(100000000)), ("sprk_quantity", 1m), ("sprk_timekeeper", "Jane Doe"),
            ("sprk_eventdate", new DateTime(2026, 3, 2)));

        var result = await Evaluator().EvaluateAsync(Request(OrdinaryMatter), CancellationToken.None);

        var duplicates = result.Findings.Where(f => f.RuleType == RuleType.Duplicate).ToList();
        duplicates.Select(f => f.ReasonCode).Should().BeEquivalentTo(
            [BillingReasonCodes.DuplicateInvoiceNumber, BillingReasonCodes.DuplicateOfPriorSubmissionLine]);
        duplicates.Should().OnlyContain(f => f.DetailWithheld && f.FactValues.Count == 0);
        duplicates.SelectMany(f => f.ValuesRead).Should().NotContain(v => v.Name == "duplicateOf.trackingNumber");
    }

    [Fact]
    public async Task ASecurePrior_OnAnOrdinaryInvoice_IsHidden_D112()
    {
        DuplicateRule();
        _world.SecureRoot("sprk_matter", MatterB).OrdinaryRoot("sprk_matter", OrdinaryMatter);
        PriorWithInvoice("INV-100.1", ("sprk_matter", "sprk_matter", MatterB), secureOwned: true);

        var ordinaryInvoice = await TheDuplicateFindingAsync(OrdinaryMatter);
        var noCoreRecord = await TheDuplicateFindingAsync(null);

        ordinaryInvoice.DetailWithheld.Should().BeTrue();
        noCoreRecord.DetailWithheld.Should().BeTrue("an invoice with no core record has the empty set");
    }

    [Fact]
    public async Task ACoreRecordSecureThroughItsParent_TakesTheWalksRoot_NotItsOwnFlag_D112()
    {
        // The work assignment carries sprk_issecure = false, but it is owned by the Secure team because the matter above it is
        // secure. Read through its flag it would look ordinary (empty set) and a prior filed under it would be hidden.
        var assignment = Guid.NewGuid();
        DuplicateRule();
        _world.SecureRoot("sprk_matter", MatterA);
        _world.Add("sprk_workassignment", assignment, ("owningteam", SecureTeam), ("sprk_issecure", false), ("sprk_matter", Ref("sprk_matter", MatterA)));
        PriorWithInvoice("INV-100.1", ("sprk_regardingworkassignment", "sprk_workassignment", assignment), secureOwned: true);
        var request = Request(null) with { CoreRecord = new CoreRecordReference(CoreRecordKind.WorkAssignment, assignment) };

        var loaded = await Loader().LoadAsync(request, CancellationToken.None);
        var result = await Evaluator().EvaluateAsync(request, CancellationToken.None);

        loaded.InvoiceSecureRoots.Should().BeEquivalentTo([assignment]);
        result.Findings.Should().ContainSingle().Which.DetailWithheld.Should().BeFalse();
    }

    [Fact]
    public async Task ARootSetReadFailure_HidesTheDetail_AndTheFindingStillFires_D112()
    {
        var project = Guid.NewGuid();
        DuplicateRule();
        _world.SecureRoot("sprk_matter", MatterA).SecureRoot("sprk_project", project).FailingQueriesOf("sprk_project");
        PriorWithInvoice("INV-100.1", ("sprk_project", "sprk_project", project), secureOwned: true);

        var loaded = await Loader().LoadAsync(Request(MatterA), CancellationToken.None);
        var finding = await TheDuplicateFindingAsync(MatterA);

        loaded.InvoiceSecureRoots.Should().BeNull("one fault leaves every set unresolved");
        loaded.PriorSubmissions.Should().ContainSingle().Which.SecureRoots.Should().BeNull();
        finding.DetailWithheld.Should().BeTrue();
    }

    [Fact]
    public async Task ARootSetReadFailure_IsLoggedAsAnError_AndCounted_B3()
    {
        var project = Guid.NewGuid();
        DuplicateRule();
        _world.SecureRoot("sprk_matter", MatterA).SecureRoot("sprk_project", project).FailingQueriesOf("sprk_project");
        PriorWithInvoice("INV-100.1", ("sprk_project", "sprk_project", project), secureOwned: true);
        var logger = new Mock<ILogger<BillingFactLoader>>();
        _loaderLogger = logger.Object;
        long faults = 0;
        using var listener = new System.Diagnostics.Metrics.MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == "Sprk.Bff.Api.Finance" && instrument.Name == "finance.billing.secure_root_walk.faults")
            {
                l.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((_, value, _, _) => Interlocked.Add(ref faults, value));
        listener.Start();

        await Loader().LoadAsync(Request(MatterA), CancellationToken.None);

        logger.Verify(l => l.Log(LogLevel.Error, It.IsAny<EventId>(), It.IsAny<It.IsAnyType>(), It.IsAny<Exception?>(),
            It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.Once());
        logger.Verify(l => l.Log(LogLevel.Warning, It.IsAny<EventId>(), It.IsAny<It.IsAnyType>(), It.IsAny<Exception?>(),
            It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.Never());
        Interlocked.Read(ref faults).Should().BeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public async Task APriorFiledThroughALookupOtherThanTheMatter_StillHasItsRoot_SoItShowsNoDetail_UacR2Condition1()
    {
        // If the loader read the prior without every lineage lookup, this prior would look ordinary and its detail would show.
        var assignment = Guid.NewGuid();
        DuplicateRule();
        _world.OrdinaryRoot("sprk_matter", OrdinaryMatter).SecureRoot("sprk_workassignment", assignment);
        PriorWithInvoice("INV-100.1", ("sprk_regardingworkassignment", "sprk_workassignment", assignment), secureOwned: true);

        var loaded = await Loader().LoadAsync(Request(OrdinaryMatter), CancellationToken.None);
        var finding = await TheDuplicateFindingAsync(OrdinaryMatter);

        loaded.PriorSubmissions.Should().ContainSingle().Which.SecureRoots.Should().BeEquivalentTo([assignment]);
        finding.DetailWithheld.Should().BeTrue();
    }

    [Fact]
    public async Task APriorUnderARootFlaggedButNotIsolated_IsUndetermined_SoItShowsNoDetail_UacR2Condition3()
    {
        DuplicateRule();
        _world.OrdinaryRoot("sprk_matter", OrdinaryMatter).FlaggedNotIsolatedRoot("sprk_matter", MatterB);
        PriorWithInvoice("INV-100.1", ("sprk_matter", "sprk_matter", MatterB));

        var loaded = await Loader().LoadAsync(Request(OrdinaryMatter), CancellationToken.None);
        var finding = await TheDuplicateFindingAsync(OrdinaryMatter);

        loaded.InvoiceSecureRoots.Should().BeEmpty("the invoice itself resolved");
        loaded.PriorSubmissions.Should().ContainSingle().Which.SecureRoots.Should().BeNull();
        finding.DetailWithheld.Should().BeTrue();
    }

    [Fact]
    public async Task WithNoSecureRecordOwnerTeam_EverySetIsEmpty_SoDetailIsShown_UacR2Condition4()
    {
        _world = SecureChildShareWorld.WithoutSecureBusinessUnit(); // the entity double reads whichever world the field holds
        _world.Add("sprk_matter", MatterB, ("sprk_issecure", true));
        DuplicateRule();
        PriorWithInvoice("INV-100.1", ("sprk_matter", "sprk_matter", MatterB));

        var loaded = await Loader().LoadAsync(Request(OrdinaryMatter), CancellationToken.None);
        var finding = await TheDuplicateFindingAsync(OrdinaryMatter);

        loaded.InvoiceSecureRoots.Should().BeEmpty();
        loaded.PriorSubmissions.Should().ContainSingle().Which.SecureRoots.Should().BeEmpty();
        finding.DetailWithheld.Should().BeFalse();
    }
}
