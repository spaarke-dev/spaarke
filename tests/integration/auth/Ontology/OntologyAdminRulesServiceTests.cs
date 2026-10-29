using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Services.Ontology;
using Sprk.Bff.Api.Services.Signals;
using Sprk.Bff.Api.Services.Signals.Actions;
using Sprk.Bff.Api.Telemetry;
using Xunit;

namespace Sprk.Bff.Api.Tests.AccessControl.Ontology;

/// <summary>
/// Ontology platform R1 task 100 (spec FR-57; D-22; reconciliation M-3..M-5, H-7; #43, #44): the Rules service, against a Dataverse that
/// enforces what the service asks for (<see cref="OntologyAdminRulesWorld"/>). The authorization cases are the point: every read and
/// write is made as the caller; a Billing or Docketing rule is never listed and is the uniform 404 with ZERO writes; Off writes only
/// <c>sprk_enabled</c>; Retire needs a reason, writes the rule first and only then closes its Signals; a refused write never reaches the
/// closure. The HTTP mapping and the real pipeline are <c>OntologyAdminRulesEndpointTests</c>.
/// </summary>
/// <remarks>Placement: <c>tests/integration/auth/**</c> (the security-auth KEEP path).</remarks>
public class OntologyAdminRulesServiceTests
{
    private static readonly Guid AuthorId = Guid.Parse("10000000-0000-4000-8000-0000000000a1");
    private static readonly Guid ModifierId = Guid.Parse("10000000-0000-4000-8000-0000000000a2");

    private sealed class Harness
    {
        public required OntologyAdminRulesWorld World { get; init; }
        public required OntologyAdminRulesService Service { get; init; }
        public required List<Guid> ClosureCalls { get; init; }
    }

    private static Harness Build(Func<int, SignalClosureOutcome>? closure = null)
    {
        var world = new OntologyAdminRulesWorld();
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 10, 16, 12, 0, 0, TimeSpan.Zero));
        var schema = new RuleBodySchemaValidator();
        var validator = new PolicyVersionValidator(schema, new PredicateCompiler(schema, clock), NullLogger<PolicyVersionValidator>.Instance);
        var entities = new Mock<IGenericEntityService>();
        entities.Setup(e => e.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EntityCollection());
        var describer = new RuleBodyDescriber(validator, entities.Object, NullLogger<RuleBodyDescriber>.Instance);
        var rates = new PolicyActionRateService(world, clock, NullLogger<PolicyActionRateService>.Instance);

        var calls = new List<Guid>();
        var count = 0;
        PolicyRetiredClosure close = (policyId, _) =>
        {
            calls.Add(policyId);
            world.RecordClosure(policyId);
            count++;
            return Task.FromResult(closure?.Invoke(count) ?? new SignalClosureOutcome(Closed: 3, ChangedSinceRead: 0, Failed: 0));
        };

        var service = new OntologyAdminRulesService(world, rates, describer, close, NullLogger<OntologyAdminRulesService>.Instance);
        return new Harness { World = world, Service = service, ClosureCalls = calls };
    }

    // ================================================================================================================
    // The list
    // ================================================================================================================

    [Fact]
    public async Task List_ShowsOntologyRules_AndNeverABillingOrDocketingRule()
    {
        var h = Build();
        h.World.AddPolicy("POL-NO-DOMAIN", domain: null);
        h.World.AddPolicy("POL-ONTOLOGY", domain: OntologyAdminRulesWorld.Ontology);
        h.World.AddPolicy("POL-BILLING", domain: OntologyAdminRulesWorld.Billing);
        h.World.AddPolicy("POL-DOCKETING", domain: OntologyAdminRulesWorld.Docketing);

        var result = await h.Service.ListAsync(default);

        result.Outcome.Should().Be(OntologyAdminRulesOutcome.Ok);
        result.Value!.Rules.Select(r => r.Code).Should().BeEquivalentTo("POL-NO-DOMAIN", "POL-ONTOLOGY");
        h.World.Requests.Should().Contain(r => r.Contains(OntologyAdminRulesWorld.DomainCondition, StringComparison.Ordinal) && r.StartsWith("GET sprk_policies?", StringComparison.Ordinal),
            "the list query itself carries the Ontology-only condition");
        h.World.WriteCount.Should().Be(0);
    }

    [Fact]
    public async Task List_CarriesLaneState_VersionInForce_AndTheHeadline()
    {
        var h = Build();
        var on = h.World.AddPolicy("POL-ON", configure: p => p.Lane = 100000001);
        var off = h.World.AddPolicy("POL-OFF", enabled: false, configure: p => p.Lane = 100000000);
        var retired = h.World.AddPolicy("POL-RETIRED", configure: p => { p.StateCode = 1; p.StatusCode = 100000000; p.RetiredReason = "Replaced"; });
        h.World.AddVersion(on, number: 2);
        h.World.AddVersion(off, number: 1);
        h.World.AddVersion(retired, number: 3);

        var rules = (await h.Service.ListAsync(default)).Value!.Rules.ToDictionary(r => r.Code);

        rules["POL-ON"].Should().Match<RuleSummary>(r => r.Lane == "Do" && r.State == "On" && r.Enabled && r.VersionNumber == 2 && r.Headline == "Open task past its due date");
        rules["POL-OFF"].Should().Match<RuleSummary>(r => r.Lane == "Decide" && r.State == "Off" && !r.Enabled && r.VersionNumber == 1);
        rules["POL-RETIRED"].State.Should().Be("Retired", "Retired wins over the On/Off column");
        h.World.Requests.Where(r => r.Contains("sprk_policyversions", StringComparison.Ordinal)).Should().ContainSingle(
            "the versions in force come in ONE batched read");
    }

    [Fact]
    public async Task List_Numbers_AreOpenItemsLastFiredAndTheActionRateOverTheVersionInForce()
    {
        var h = Build();
        var rule = h.World.AddPolicy("POL-RATE");
        var old = h.World.AddVersion(rule, number: 1, inForceTo: new DateTime(2026, 10, 7, 0, 0, 0, DateTimeKind.Utc), current: false);
        var current = h.World.AddVersion(rule, number: 2);
        h.World.OpenSignals[rule.Id] = 4;
        h.World.LastFired[rule.Id] = new DateTime(2026, 10, 15, 8, 30, 0, DateTimeKind.Utc);
        h.World.Resolved.Add(("POL-RATE", current.Id, 100000001, PolicyActionRateService.Acted, 2));
        h.World.Resolved.Add(("POL-RATE", current.Id, 100000001, PolicyActionRateService.Dismissed, 6));
        // the OLD version's 100 acted items are not the version in force: they must not lift the rate
        h.World.Resolved.Add(("POL-RATE", old.Id, 100000001, PolicyActionRateService.Acted, 100));

        var response = (await h.Service.ListAsync(default)).Value!;

        response.MetricsAvailable.Should().BeTrue();
        response.MetricsAvailability.Should().Be(new RuleMetricsAvailability(OpenItems: true, LastFired: true, ActionRate: true));
        response.ActionRateWindowDays.Should().Be(90);
        response.LastFiredWindowDays.Should().Be(90);
        response.MinimumJudged.Should().Be(5);
        var metrics = response.Rules.Single().Metrics;
        metrics.OpenItems.Should().Be(4);
        metrics.LastFiredOn.Should().Be(new DateTime(2026, 10, 15, 8, 30, 0, DateTimeKind.Utc));
        metrics.ActionRate.Should().Be(new RuleActionRate(Acted: 2, Dismissed: 6, Rate: 0.25, TooFewToJudge: false));
        metrics.NeedsALook.Should().BeTrue("2 / (2 + 6) = 0.25 is below one half");
    }

    [Theory]
    [InlineData(5, 5, false)] // exactly one half is not below it
    [InlineData(6, 5, false)]
    [InlineData(4, 6, true)]
    [InlineData(1, 3, false)] // 4 judged: too few to judge, no rate, never "needs a look"
    [InlineData(0, 4, false)]
    [InlineData(0, 0, false)]
    public async Task NeedsALook_IsARateBelowOneHalf_WithAtLeastFiveJudged(int acted, int dismissed, bool needsALook)
    {
        var h = Build();
        var rule = h.World.AddPolicy("POL-NAL");
        var version = h.World.AddVersion(rule);
        if (acted > 0) h.World.Resolved.Add(("POL-NAL", version.Id, 100000000, PolicyActionRateService.Acted, acted));
        if (dismissed > 0) h.World.Resolved.Add(("POL-NAL", version.Id, 100000000, PolicyActionRateService.Dismissed, dismissed));

        var metrics = (await h.Service.ListAsync(default)).Value!.Rules.Single().Metrics;

        metrics.NeedsALook.Should().Be(needsALook);
        metrics.ActionRate!.TooFewToJudge.Should().Be(acted + dismissed < 5);
        (metrics.ActionRate.Rate is null).Should().Be(acted + dismissed < 5, "no rate is shown below five judged");
    }

    [Fact]
    public async Task List_WhenTheSignalNumbersCannotBeRead_StillListsEveryRule_NeverAShorterList()
    {
        var h = Build();
        h.World.AddPolicy("POL-A");
        h.World.AddPolicy("POL-B");
        h.World.SignalReadsFail = true;

        var result = await h.Service.ListAsync(default);

        result.Outcome.Should().Be(OntologyAdminRulesOutcome.Ok);
        result.Value!.MetricsAvailable.Should().BeFalse();
        result.Value.MetricsAvailability.Should().Be(new RuleMetricsAvailability(OpenItems: false, LastFired: false, ActionRate: false));
        result.Value.Rules.Should().HaveCount(2).And.OnlyContain(r => r.Metrics.OpenItems == null && r.Metrics.ActionRate == null && !r.Metrics.NeedsALook);
    }

    [Theory]
    [InlineData("open")]
    [InlineData("last")]
    [InlineData("rate")]
    public async Task List_OneSignalNumberFailing_LeavesTheOtherTwoIntact(string failing)
    {
        // An AggregateQueryRecordLimit fault on one number must leave the other two intact (they were all-or-nothing before).
        var h = Build();
        var rule = h.World.AddPolicy("POL-ONE");
        var current = h.World.AddVersion(rule);
        h.World.OpenSignals[rule.Id] = 4;
        h.World.LastFired[rule.Id] = new DateTime(2026, 10, 15, 8, 30, 0, DateTimeKind.Utc);
        h.World.Resolved.Add(("POL-ONE", current.Id, 100000001, PolicyActionRateService.Acted, 2));
        h.World.Resolved.Add(("POL-ONE", current.Id, 100000001, PolicyActionRateService.Dismissed, 6));
        h.World.FailedSignalNumbers.Add(failing);

        var result = await h.Service.ListAsync(default);

        result.Outcome.Should().Be(OntologyAdminRulesOutcome.Ok);
        var response = result.Value!;
        response.MetricsAvailable.Should().BeFalse();
        response.MetricsAvailability.Should().Be(new RuleMetricsAvailability(failing != "open", failing != "last", failing != "rate"));
        var metrics = response.Rules.Single().Metrics;
        metrics.OpenItems.Should().Be(failing == "open" ? null : 4);
        metrics.LastFiredOn.Should().Be(failing == "last" ? null : new DateTime(2026, 10, 15, 8, 30, 0, DateTimeKind.Utc));
        if (failing == "rate")
        {
            metrics.ActionRate.Should().BeNull();
            metrics.NeedsALook.Should().BeFalse("with no rate there is nothing to look at");
        }
        else
        {
            metrics.ActionRate.Should().Be(new RuleActionRate(Acted: 2, Dismissed: 6, Rate: 0.25, TooFewToJudge: false));
        }
    }

    [Fact]
    public async Task List_LastFired_LooksBackOnlyNinetyDays_SoATenantOverFiftyThousandSignalsStillReadsIt()
    {
        // The unwindowed max(createdon) scanned every Signal ever written and failed tenant-wide above 50,000 rows.
        var h = Build();
        var rule = h.World.AddPolicy("POL-BIG");
        h.World.AddVersion(rule);
        h.World.OpenSignals[rule.Id] = 7;
        h.World.LastFired[rule.Id] = new DateTime(2026, 10, 15, 8, 30, 0, DateTimeKind.Utc);
        h.World.TotalSignals = OntologyAdminRulesWorld.AggregateRecordLimit + 10_000;
        h.World.RecentSignals = 12_000;

        var response = (await h.Service.ListAsync(default)).Value!;

        response.MetricsAvailable.Should().BeTrue("the last-fired aggregate is bounded to the last 90 days");
        response.Rules.Single().Metrics.LastFiredOn.Should().Be(new DateTime(2026, 10, 15, 8, 30, 0, DateTimeKind.Utc));
        response.Rules.Single().Metrics.OpenItems.Should().Be(7);
        h.World.LastFiredFetches.Should().ContainSingle().Which.Should().Contain("operator=\"last-x-days\" value=\"90\"");
    }

    [Fact]
    public async Task Get_OneSignalNumberFailing_StillShowsThePageAndTheOtherNumbers()
    {
        var h = Build();
        var rule = h.World.AddPolicy("POL-PAGE");
        h.World.AddVersion(rule);
        h.World.OpenSignals[rule.Id] = 4;
        h.World.FailedSignalNumbers.Add("last");

        var detail = (await h.Service.GetAsync(rule.Id, default)).Value!;

        detail.MetricsAvailable.Should().BeFalse();
        detail.MetricsAvailability.Should().Be(new RuleMetricsAvailability(OpenItems: true, LastFired: false, ActionRate: true));
        detail.Summary.Metrics.OpenItems.Should().Be(4);
        detail.Summary.Metrics.LastFiredOn.Should().BeNull();
    }

    [Theory]
    [InlineData(DataverseUserClientErrorCodes.UserContextRequired, OntologyAdminRulesOutcome.CallerUnresolved)]
    [InlineData(DataverseUserClientErrorCodes.OboNotConfigured, OntologyAdminRulesOutcome.CallerUnresolved)]
    [InlineData(DataverseUserClientErrorCodes.OboExchangeFailed, OntologyAdminRulesOutcome.CallerUnresolved)]
    [InlineData(DataverseUserClientErrorCodes.AccessDenied, OntologyAdminRulesOutcome.Forbidden)]
    [InlineData(DataverseUserClientErrorCodes.ServiceError, OntologyAdminRulesOutcome.Unavailable)]
    [InlineData(DataverseUserClientErrorCodes.RateLimited, OntologyAdminRulesOutcome.Unavailable)]
    public async Task List_ADataverseRefusal_IsMappedByItsCode(string code, OntologyAdminRulesOutcome expected)
    {
        var h = Build();
        h.World.AddPolicy("POL-A");
        h.World.ReadError = code;

        (await h.Service.ListAsync(default)).Outcome.Should().Be(expected);
    }

    [Fact]
    public async Task List_AResultAsLongAsThePageLimit_IsRefused_NotShownAsTheWholeList()
    {
        var h = Build();
        h.World.AddPolicy("POL-A");
        h.World.PadPolicyListTo = OntologyAdminRulesService.MaxPolicies;

        (await h.Service.ListAsync(default)).Outcome.Should().Be(OntologyAdminRulesOutcome.Unavailable);
    }

    // ================================================================================================================
    // The rule page (read-only)
    // ================================================================================================================

    [Fact]
    public async Task Get_ABillingRule_IsTheUniform404_AndNothingElseIsRead()
    {
        var h = Build();
        var billing = h.World.AddPolicy("POL-BILLING", domain: OntologyAdminRulesWorld.Billing);
        h.World.AddVersion(billing);

        var result = await h.Service.GetAsync(billing.Id, default);

        result.Outcome.Should().Be(OntologyAdminRulesOutcome.NotFound);
        h.World.Requests.Should().ContainSingle("only the domain-filtered policy read ran; no versions, names or numbers");
        h.World.Requests[0].Should().Contain(OntologyAdminRulesWorld.DomainCondition);
    }

    [Theory]
    [InlineData(OntologyAdminRulesWorld.Billing)]
    [InlineData(OntologyAdminRulesWorld.Docketing)]
    public async Task Get_AnyNonOntologyDomain_IsNotFound(int domain)
    {
        var h = Build();
        var other = h.World.AddPolicy("POL-OTHER", domain: domain);

        (await h.Service.GetAsync(other.Id, default)).Outcome.Should().Be(OntologyAdminRulesOutcome.NotFound);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Get_AMissingRule_AndAnEmptyId_AreNotFound(bool emptyId)
    {
        var h = Build();
        h.World.AddPolicy("POL-A");

        var result = await h.Service.GetAsync(emptyId ? Guid.Empty : Guid.NewGuid(), default);

        result.Outcome.Should().Be(OntologyAdminRulesOutcome.NotFound);
        if (emptyId) h.World.Requests.Should().BeEmpty("an empty id is refused before any read");
    }

    [Fact]
    public async Task Get_TheRulePage_CarriesEverySectionTheOwnerListed()
    {
        var h = Build();
        h.World.Users[AuthorId] = "Ada Author";
        h.World.Users[ModifierId] = "Max Modifier";
        var matter = Guid.NewGuid();
        h.World.Matters[matter] = "Acme v Beta";
        var rule = h.World.AddPolicy("POL-PAGE", configure: p =>
        {
            p.Tenant = "tenant-1";
            p.Matter = matter;
            p.Priority = 300;
            p.CountsTowardSuppression = false;
            p.ModifiedBy = ModifierId;
            p.ModifiedOn = new DateTime(2026, 10, 16, 1, 55, 56, DateTimeKind.Utc);
        });
        h.World.AddVersion(rule, number: 1, inForceTo: new DateTime(2026, 10, 7, 12, 0, 0, DateTimeKind.Utc), authoredBy: AuthorId, current: false);
        h.World.AddVersion(rule, number: 2, authoredBy: AuthorId,
            ruleBody: "{\"type\":\"Existence\",\"subject\":\"sprk_event\",\"quietWindowDays\":7,\"when\":{\"statuscode\":659490001,\"sprk_duedate\":{\"<=\":\"now-1d\"}},\"all\":[]}");

        var page = (await h.Service.GetAsync(rule.Id, default)).Value!;

        page.Summary.Code.Should().Be("POL-PAGE");
        page.Summary.VersionNumber.Should().Be(2);

        page.Condition.RuleType.Should().Be("Existence");
        page.Condition.Refusal.Should().BeNull();
        page.Condition.Clauses.Should().NotBeEmpty();
        page.Condition.Clauses.Should().OnlyContain(c => c.Text.Length > 0 && c.Formal.Length > 0, "words AND the formal form, one pair per clause");

        page.Wording.Should().Be(new RuleWordingView(
            "Open task past its due date", "This task is open and its due date has passed.", "mark-complete: finish the task, or reschedule or reassign it."));

        page.Plan.Refusal.Should().BeNull();
        page.Plan.Actions.Select(a => a.Code).Should().Equal("mark-complete", "reschedule", "reassign");
        page.Plan.Actions.Should().OnlyContain(a => a.Label.Length > 0);
        page.Plan.DismissalReasons.Should().NotBeEmpty();

        page.Scope.Should().Be(new RuleScopeView("tenant-1", matter, "Acme v Beta", 300));
        page.Noise.Should().Be(new RuleNoiseView(CountsTowardSuppression: false, SuppressAfterDismissals: 3, SuppressionWindowDays: 30, QuietWindowDays: 7));
        page.Behaviour.Should().Be(new RuleBehaviourView("Do", "Warning", "Finish or reschedule", "Event", "On", null));

        page.Versions.Select(v => (v.Number, v.InForce)).Should().Equal([(2, true), (1, false)]);
        page.Versions.Should().OnlyContain(v => v.AuthoredBy == "Ada Author");

        page.History.Select(e => e.Event).Should().BeEquivalentTo(
            "Version 1 authored", "Version 1 superseded", "Version 2 authored", "Rule record last changed (On/Off, Retire or an edit)");
        page.History.Single(e => e.Event.StartsWith("Rule record last changed", StringComparison.Ordinal)).By.Should().Be("Max Modifier");
        page.History.Where(e => e.Event.EndsWith("authored", StringComparison.Ordinal)).Should().OnlyContain(e => e.By == "Ada Author");
        page.History.Select(e => e.When).Should().BeInDescendingOrder("newest first");
        h.World.WriteCount.Should().Be(0, "the page is read-only");
    }

    [Fact]
    public async Task Get_TheQuietWindow_IsTheDefaultWhenTheBodyHasNoKnob()
    {
        var h = Build();
        var rule = h.World.AddPolicy("POL-DEFAULT");
        h.World.AddVersion(rule);

        (await h.Service.GetAsync(rule.Id, default)).Value!.Noise.QuietWindowDays.Should().Be(PredicateCompiler.DefaultQuietWindowDays);
    }

    [Fact]
    public async Task Get_ABodyTheDescriberRefuses_ShowsTheRefusal_NotAGuess_AndStillTheRestOfThePage()
    {
        var h = Build();
        var rule = h.World.AddPolicy("POL-BAD");
        h.World.AddVersion(rule, ruleBody: "{\"type\":\"Existence\",\"subject\":\"sprk_event\",\"when\":{\"nope\":1}}");

        var page = (await h.Service.GetAsync(rule.Id, default)).Value!;

        page.Condition.Clauses.Should().BeEmpty();
        page.Condition.Refusal.Should().NotBeNullOrWhiteSpace();
        page.Wording.Headline.Should().Be("Open task past its due date");
    }

    [Fact]
    public async Task Get_APlanOutsideTheClosedCatalog_ShowsTheRefusal()
    {
        var h = Build();
        var rule = h.World.AddPolicy("POL-PLAN");
        h.World.AddVersion(rule, decisionPlan: "{\"actions\":[\"not-in-the-catalog\"]}");

        var plan = (await h.Service.GetAsync(rule.Id, default)).Value!.Plan;

        plan.Actions.Should().BeEmpty();
        plan.Refusal.Should().Contain(DecisionPlanRefusalReason.UnknownAction);
    }

    [Fact]
    public async Task Get_ARuleWithNoVersionInForce_IsStillShown()
    {
        var h = Build();
        var rule = h.World.AddPolicy("POL-NEW");

        var page = (await h.Service.GetAsync(rule.Id, default)).Value!;

        page.Summary.VersionNumber.Should().BeNull();
        page.Condition.Refusal.Should().Contain("no version");
        page.Versions.Should().BeEmpty();
    }

    [Fact]
    public async Task Get_WithoutThePointer_TheNewestVersionWithNoInForceToIsTheOneInForce()
    {
        var h = Build();
        var rule = h.World.AddPolicy("POL-NOPTR");
        h.World.AddVersion(rule, number: 1, inForceTo: new DateTime(2026, 10, 7, 0, 0, 0, DateTimeKind.Utc), current: false);
        h.World.AddVersion(rule, number: 2, current: false);

        var page = (await h.Service.GetAsync(rule.Id, default)).Value!;

        page.Summary.VersionNumber.Should().Be(2);
        page.Versions.Single(v => v.InForce).Number.Should().Be(2);
    }

    // ================================================================================================================
    // On / Off
    // ================================================================================================================

    [Fact]
    public async Task Off_WritesOnlySprkEnabled_AsTheCaller_AndTouchesNoSignal()
    {
        var h = Build();
        var rule = h.World.AddPolicy("POL-OFF");

        var result = await h.Service.SetEnabledAsync(rule.Id, false, default);

        result.Outcome.Should().Be(OntologyAdminRulesOutcome.Ok);
        result.Value.Should().Be(new RuleEnabledResult(rule.Id, false));
        h.World.Patches.Should().ContainSingle().Which.Should().Be(($"sprk_policies({rule.Id:D})", "{\"sprk_enabled\":false}"));
        rule.Enabled.Should().BeFalse();
        h.World.Order.Should().NotContain(o => o.StartsWith("CLOSE", StringComparison.Ordinal), "Off leaves the open items for the next pass");
    }

    [Fact]
    public async Task On_WritesOnlySprkEnabledTrue()
    {
        var h = Build();
        var rule = h.World.AddPolicy("POL-ON", enabled: false);

        (await h.Service.SetEnabledAsync(rule.Id, true, default)).Outcome.Should().Be(OntologyAdminRulesOutcome.Ok);

        h.World.Patches.Should().ContainSingle().Which.Body.Should().Be("{\"sprk_enabled\":true}");
        rule.Enabled.Should().BeTrue();
    }

    [Fact]
    public async Task OnOff_WithNoValue_IsRefused_BeforeAnyRequest()
    {
        var h = Build();
        var rule = h.World.AddPolicy("POL-X");

        var result = await h.Service.SetEnabledAsync(rule.Id, null, default);

        result.Outcome.Should().Be(OntologyAdminRulesOutcome.Invalid);
        result.ReasonCode.Should().Be(OntologyAdminRulesService.EnabledRequired);
        h.World.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task OnOff_ByAConsoleUser_IsRefusedByDataverse_AsForbidden()
    {
        var h = Build();
        var rule = h.World.AddPolicy("POL-X");
        h.World.WriteRefusal = DataverseUserClientErrorCodes.AccessDenied;

        var result = await h.Service.SetEnabledAsync(rule.Id, false, default);

        result.Outcome.Should().Be(OntologyAdminRulesOutcome.Forbidden);
        rule.Enabled.Should().BeTrue("Dataverse refused the write, so nothing changed");
    }

    [Fact]
    public async Task OnOff_ToTheCurrentValue_StillIssuesTheWrite_SoDataverseDecidesEveryCall()
    {
        var h = Build();
        var rule = h.World.AddPolicy("POL-X", enabled: true);

        await h.Service.SetEnabledAsync(rule.Id, true, default);

        h.World.WriteCount.Should().Be(1);
    }

    [Theory]
    [InlineData(OntologyAdminRulesWorld.Billing)]
    [InlineData(OntologyAdminRulesWorld.Docketing)]
    public async Task OnOff_ABillingOrDocketingRule_IsTheUniform404_WithZeroWrites(int domain)
    {
        var h = Build();
        var rule = h.World.AddPolicy("POL-OTHER", domain: domain);

        var result = await h.Service.SetEnabledAsync(rule.Id, false, default);

        result.Outcome.Should().Be(OntologyAdminRulesOutcome.NotFound);
        h.World.WriteCount.Should().Be(0);
        rule.Enabled.Should().BeTrue();
    }

    [Fact]
    public async Task OnOff_ARetiredRule_IsRefused_WithZeroWrites()
    {
        var h = Build();
        var rule = h.World.AddPolicy("POL-R", configure: p => { p.StateCode = 1; p.StatusCode = 100000000; });

        var result = await h.Service.SetEnabledAsync(rule.Id, true, default);

        result.Outcome.Should().Be(OntologyAdminRulesOutcome.Conflict);
        result.ReasonCode.Should().Be(OntologyAdminRulesService.NotActive);
        h.World.WriteCount.Should().Be(0);
    }

    [Fact]
    public async Task OnOff_AnInactiveRuleThatIsNotRetired_IsRefused_WithTheInactiveWording()
    {
        var h = Build();
        var rule = h.World.AddPolicy("POL-I", configure: p => p.StateCode = 1);

        var result = await h.Service.SetEnabledAsync(rule.Id, true, default);

        result.Outcome.Should().Be(OntologyAdminRulesOutcome.Conflict);
        result.Detail.Should().Be("An inactive rule cannot be switched.");
        h.World.WriteCount.Should().Be(0);
    }

    [Fact]
    public async Task OnOff_AnUnresolvedCaller_GetsNoWrite()
    {
        var h = Build();
        var rule = h.World.AddPolicy("POL-X");
        h.World.ReadError = DataverseUserClientErrorCodes.OboExchangeFailed;

        (await h.Service.SetEnabledAsync(rule.Id, false, default)).Outcome.Should().Be(OntologyAdminRulesOutcome.CallerUnresolved);
        h.World.WriteCount.Should().Be(0);
    }

    // ================================================================================================================
    // Retire
    // ================================================================================================================

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\r\n")]
    public async Task Retire_WithoutAReason_IsRefused_BeforeAnyReadWriteOrClosure(string? reason)
    {
        var h = Build();
        var rule = h.World.AddPolicy("POL-X");

        var result = await h.Service.RetireAsync(rule.Id, reason, default);

        result.Outcome.Should().Be(OntologyAdminRulesOutcome.Invalid);
        result.ReasonCode.Should().Be(OntologyAdminRulesService.ReasonRequired);
        h.World.Order.Should().BeEmpty("nothing was read, written or closed");
        rule.StatusCode.Should().Be(1);
    }

    [Fact]
    public async Task Retire_AReasonOverTheColumnLength_IsRefused_BeforeAnyRequest()
    {
        var h = Build();
        var rule = h.World.AddPolicy("POL-X");

        var result = await h.Service.RetireAsync(rule.Id, new string('x', OntologyAdminRulesService.MaxReasonLength + 1), default);

        result.Outcome.Should().Be(OntologyAdminRulesOutcome.Invalid);
        result.ReasonCode.Should().Be(OntologyAdminRulesService.ReasonTooLong);
        h.World.Order.Should().BeEmpty();
    }

    [Fact]
    public async Task Retire_SetsRetiredWithTheReason_InOneWrite_ThenClosesTheOpenSignals_InThatOrder()
    {
        var h = Build();
        var rule = h.World.AddPolicy("POL-RETIRE");

        var result = await h.Service.RetireAsync(rule.Id, "  Superseded by the new fee rule  ", default);

        result.Outcome.Should().Be(OntologyAdminRulesOutcome.Ok);
        var retired = result.Value!;
        retired.PolicyId.Should().Be(rule.Id);
        retired.State.Should().Be("Retired");
        retired.Reason.Should().Be("Superseded by the new fee rule", "the reason is trimmed");
        retired.Closure.Should().Be(new RuleClosureView(Closed: 3, ChangedSinceRead: 0, Failed: 0, PolicyFailures: 0, Complete: true, Unavailable: false));

        h.World.Patches.Should().ContainSingle();
        h.World.Patches[0].Path.Should().Be($"sprk_policies({rule.Id:D})");
        h.World.Patches[0].Body.Should().Be(
            "{\"statecode\":1,\"statuscode\":100000000,\"sprk_retiredreason\":\"Superseded by the new fee rule\"}",
            "state, status and reason together; sprk_enabled is not touched");
        rule.StatusCode.Should().Be(100000000);
        rule.RetiredReason.Should().Be("Superseded by the new fee rule");

        h.World.Order.Should().EndWith(
            [$"PATCH sprk_policies({rule.Id:D})", $"CLOSE {rule.Id:D}"],
            "the rule is Retired BEFORE its Signals are closed (the closure does not check)");
    }

    [Fact]
    public async Task Retire_IsConditionalOnTheRowVersionItRead()
    {
        var h = Build();
        var rule = h.World.AddPolicy("POL-V");
        var read = rule.Version;

        (await h.Service.RetireAsync(rule.Id, "because", default)).Outcome.Should().Be(OntologyAdminRulesOutcome.Ok);

        h.World.ExpectedVersions.Should().Equal(new long?[] { read }, "the PATCH carries the versionnumber just read as its If-Match");
    }

    [Fact]
    public async Task Retire_WhenTheRuleChangedSinceItWasRead_Is409_WritesNothing_AndRunsNoClosure()
    {
        // Another admin writes the rule between this admin's read and write: Dataverse answers 412.
        var h = Build();
        var rule = h.World.AddPolicy("POL-RACE");
        h.World.BeforePatch = p => p.Version++;

        var result = await h.Service.RetireAsync(rule.Id, "mine", default);

        result.Outcome.Should().Be(OntologyAdminRulesOutcome.Conflict);
        result.ReasonCode.Should().Be(OntologyAdminRulesService.ChangedSinceRead);
        result.Detail.Should().Contain("changed since you opened it");
        h.World.PreconditionFailures.Should().ContainSingle();
        h.World.WriteCount.Should().Be(0);
        rule.StatusCode.Should().Be(1, "the loser write never landed");
        rule.RetiredReason.Should().BeNull();
        h.ClosureCalls.Should().BeEmpty("the closure runs only after the caller own write succeeded");
    }

    [Fact]
    public async Task Retire_WithNoRowVersionRead_WritesNothing()
    {
        var h = Build();
        var rule = h.World.AddPolicy("POL-NOVERSION");
        h.World.OmitVersionNumber = true;

        var result = await h.Service.RetireAsync(rule.Id, "because", default);

        result.Outcome.Should().Be(OntologyAdminRulesOutcome.Unavailable);
        h.World.WriteCount.Should().Be(0);
        h.ClosureCalls.Should().BeEmpty();
    }

    [Fact]
    public async Task Retire_WritesNoDecisionRecord()
    {
        var h = Build();
        var rule = h.World.AddPolicy("POL-RETIRE");

        // The world throws on any POST (a create), so reaching here at all proves no Decision Record was written.
        (await h.Service.RetireAsync(rule.Id, "no longer needed", default)).Outcome.Should().Be(OntologyAdminRulesOutcome.Ok);

        h.World.Requests.Should().NotContain(r => r.Contains("sprk_decisionrecord", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Retire_ByAConsoleUser_IsRefusedByDataverse_AndTheClosureNeverRuns()
    {
        var h = Build();
        var rule = h.World.AddPolicy("POL-X");
        h.World.WriteRefusal = DataverseUserClientErrorCodes.AccessDenied;

        var result = await h.Service.RetireAsync(rule.Id, "because", default);

        result.Outcome.Should().Be(OntologyAdminRulesOutcome.Forbidden);
        h.World.Order.Should().NotContain(o => o.StartsWith("CLOSE", StringComparison.Ordinal), "the rule was not retired, so no Signal is closed");
        rule.StatusCode.Should().Be(1);
    }

    [Theory]
    [InlineData(OntologyAdminRulesWorld.Billing)]
    [InlineData(OntologyAdminRulesWorld.Docketing)]
    public async Task Retire_ABillingOrDocketingRule_IsTheUniform404_WithZeroWritesAndNoClosure(int domain)
    {
        var h = Build();
        var rule = h.World.AddPolicy("POL-OTHER", domain: domain);

        var result = await h.Service.RetireAsync(rule.Id, "because", default);

        result.Outcome.Should().Be(OntologyAdminRulesOutcome.NotFound);
        h.World.WriteCount.Should().Be(0);
        h.World.Order.Should().NotContain(o => o.StartsWith("CLOSE", StringComparison.Ordinal));
        rule.StatusCode.Should().Be(1);
    }

    [Fact]
    public async Task Retire_AnAlreadyRetiredRule_IsRefused_WithZeroWritesAndNoClosure()
    {
        var h = Build();
        var rule = h.World.AddPolicy("POL-R", configure: p => { p.StateCode = 1; p.StatusCode = 100000000; p.RetiredReason = "first reason"; });

        var result = await h.Service.RetireAsync(rule.Id, "second reason", default);

        result.Outcome.Should().Be(OntologyAdminRulesOutcome.Conflict);
        result.ReasonCode.Should().Be(OntologyAdminRulesService.AlreadyRetired);
        h.World.WriteCount.Should().Be(0);
        rule.RetiredReason.Should().Be("first reason", "the original reason is kept");
        h.World.Order.Should().NotContain(o => o.StartsWith("CLOSE", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Retire_AMissingRule_IsNotFound_WithNoClosure()
    {
        var h = Build();

        (await h.Service.RetireAsync(Guid.NewGuid(), "because", default)).Outcome.Should().Be(OntologyAdminRulesOutcome.NotFound);
        h.World.Order.Should().NotContain(o => o.StartsWith("CLOSE", StringComparison.Ordinal));
    }

    // ---- the closure result (task 033 hand-off) ----------------------------------------------------------------------------

    [Fact]
    public async Task Closure_IncompleteOnlyBecauseAPersonActedMeanwhile_IsReRunOnce_AndReportedComplete()
    {
        var h = Build(call => call == 1
            ? new SignalClosureOutcome(Closed: 2, ChangedSinceRead: 1, Failed: 0)
            : new SignalClosureOutcome(Closed: 0, ChangedSinceRead: 0, Failed: 0));
        var rule = h.World.AddPolicy("POL-X");

        var closure = (await h.Service.RetireAsync(rule.Id, "because", default)).Value!.Closure;

        h.World.Order.Count(o => o.StartsWith("CLOSE", StringComparison.Ordinal)).Should().Be(2);
        closure.Should().Be(new RuleClosureView(Closed: 2, ChangedSinceRead: 0, Failed: 0, PolicyFailures: 0, Complete: true, Unavailable: false));
    }

    [Fact]
    public async Task Closure_StillIncompleteAfterTheReRun_IsReported_AndNotRunAThirdTime()
    {
        var h = Build(_ => new SignalClosureOutcome(Closed: 1, ChangedSinceRead: 1, Failed: 0));
        var rule = h.World.AddPolicy("POL-X");

        var closure = (await h.Service.RetireAsync(rule.Id, "because", default)).Value!.Closure;

        h.World.Order.Count(o => o.StartsWith("CLOSE", StringComparison.Ordinal)).Should().Be(2, "once, then one re-run");
        closure.Complete.Should().BeFalse();
        closure.ChangedSinceRead.Should().Be(1);
        closure.Closed.Should().Be(2);
    }

    [Theory]
    [InlineData(2, 0)]
    [InlineData(0, 1)]
    public async Task Closure_WithAFailure_IsReportedToTheAdmin_AndNotReRun(int failed, int policyFailures)
    {
        var h = Build(_ => new SignalClosureOutcome(Closed: 1, ChangedSinceRead: 1, Failed: failed, PolicyFailures: policyFailures));
        var rule = h.World.AddPolicy("POL-X");

        var result = await h.Service.RetireAsync(rule.Id, "because", default);

        result.Outcome.Should().Be(OntologyAdminRulesOutcome.Ok, "the rule IS retired");
        h.World.Order.Count(o => o.StartsWith("CLOSE", StringComparison.Ordinal)).Should().Be(1);
        result.Value!.Closure.Should().Be(new RuleClosureView(Closed: 1, ChangedSinceRead: 1, Failed: failed, PolicyFailures: policyFailures, Complete: false, Unavailable: false));
    }

    [Fact]
    public async Task Closure_ThatThrows_ReportsUnavailable_TheRuleStaysRetired()
    {
        var world = new OntologyAdminRulesWorld();
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 10, 16, 12, 0, 0, TimeSpan.Zero));
        var schema = new RuleBodySchemaValidator();
        var validator = new PolicyVersionValidator(schema, new PredicateCompiler(schema, clock), NullLogger<PolicyVersionValidator>.Instance);
        var describer = new RuleBodyDescriber(validator, new Mock<IGenericEntityService>().Object, NullLogger<RuleBodyDescriber>.Instance);
        var service = new OntologyAdminRulesService(
            world, new PolicyActionRateService(world, clock, NullLogger<PolicyActionRateService>.Instance), describer,
            (_, _) => throw new InvalidOperationException("Dataverse timed out."), NullLogger<OntologyAdminRulesService>.Instance);
        var rule = world.AddPolicy("POL-X");

        var result = await service.RetireAsync(rule.Id, "because", default);

        result.Outcome.Should().Be(OntologyAdminRulesOutcome.Ok);
        result.Value!.Closure.Should().Be(new RuleClosureView(0, 0, 0, 0, Complete: false, Unavailable: true));
        rule.StatusCode.Should().Be(100000000);
    }
}
