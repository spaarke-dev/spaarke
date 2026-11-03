using System.Diagnostics.Metrics;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Sprk.Bff.Api.Services.Finance;
using Sprk.Bff.Api.Services.Signals;
using Sprk.Bff.Api.Services.Signals.Billing;
using Sprk.Bff.Api.Telemetry;
using Xunit;
using static Sprk.Bff.Api.Tests.Domain.Signals.Billing.BillingEvaluationKit;

namespace Sprk.Bff.Api.Tests.Domain.Signals.Billing;

/// <summary>
/// ebilling-r1 task 073 (spec FR-26, I-5, D-89): the consequence a rule type may carry, enforced where a rule is saved
/// (<see cref="PolicyVersionValidator.ValidateForSave"/>, which publish, accept, edit and check-draft all go through) and where a
/// stored version is read for evaluation (<see cref="PolicyVersionValidator.TryPrepareBillingRule"/>). Reject is refused for
/// TimeIncrement, DailyHoursCap, NonBillableActivity and AiObservation (FR-26, I-5) and for ApprovedTimekeeper (D-89). The Ontology
/// save and evaluation paths are unchanged; their existing tests are in <c>PolicyVersionValidatorTests</c>.
/// </summary>
public class BillingConsequenceGateTests
{
    private const int Reject = (int)BillingConsequence.Reject;
    private const int Flag = (int)BillingConsequence.Flag;
    private const int Suggest = (int)BillingConsequence.SuggestAdjustment;

    private static readonly AsyncLocal<Guid> MetricScope = new();
    private static readonly Guid VersionId = Guid.Parse("07300000-0000-4000-8000-000000000073");

    private const string ValidExistenceBody = """
        {"type":"Existence","subject":"sprk_matter","all":[{"exists":"sprk_communication","path":"sprk_regardingmatter","filter":{"sprk_direction":1}}]}
        """;

    public static TheoryData<RuleType> EveryBillingType => new(BillingRuleCatalogue.Types);

    public static TheoryData<RuleType> RejectRefusedTypes => new()
    {
        RuleType.TimeIncrement, RuleType.DailyHoursCap, RuleType.NonBillableActivity, RuleType.AiObservation, RuleType.ApprovedTimekeeper,
    };

    private static PolicyVersionValidator Validator(ILogger<PolicyVersionValidator>? logger = null) =>
        new(new RuleBodySchemaValidator(),
            new PredicateCompiler(new RuleBodySchemaValidator(), new FakeTimeProvider(DateTimeOffset.UtcNow)),
            logger ?? NullLogger<PolicyVersionValidator>.Instance);

    private static PolicyVersionSnapshot Row(RuleType type, int? consequence) =>
        new(VersionId, "BILL-073", ((int)type).ToString(System.Globalization.CultureInfo.InvariantCulture), Bodies[type], null, consequence);

    // ── Save time ────────────────────────────────────────────────────────────────────────────────────────────────

    [Theory]
    [MemberData(nameof(EveryBillingType))]
    public void ValidateForSave_AcceptsExactlyTheConsequencesTheTypeMayCarry(RuleType type)
    {
        foreach (var consequence in new[] { Reject, Flag, Suggest })
        {
            var result = Validator().ValidateForSave(type.ToString(), Bodies[type], null, RuleDomain.Billing, consequence);

            var allowed = BillingRuleCatalogue.IsConsequenceAllowed(type, consequence);
            result.IsValid.Should().Be(allowed, $"{type} with consequence {(BillingConsequence)consequence}");
            if (!allowed)
            {
                result.Reason.Should().Be(OntologyWriterFailureReason.ConsequenceNotAllowed);
                result.Errors.Should().ContainSingle().Which.Should().Contain(type.ToString());
            }
        }
    }

    [Theory]
    [MemberData(nameof(RejectRefusedTypes))]
    public void ValidateForSave_RefusesReject_ForTimeIncrement_DailyHoursCap_NonBillableActivity_AiObservation_AndApprovedTimekeeper(RuleType type)
    {
        var result = Validator().ValidateForSave(type.ToString(), Bodies[type], null, RuleDomain.Billing, Reject);

        result.IsValid.Should().BeFalse();
        result.Reason.Should().Be("consequence_not_allowed", "the reason is a stable, documented code");
        result.Errors.Should().ContainSingle().Which.Should().Contain("Reject").And.Contain("Flag");
    }

    [Fact]
    public void ValidateForSave_NamesTheReason_ForAiObservation_AndForApprovedTimekeeper()
    {
        Validator().ValidateForSave("AiObservation", Bodies[RuleType.AiObservation], null, RuleDomain.Billing, Reject)
            .Errors.Single().Should().Contain("I-5");
        Validator().ValidateForSave("ApprovedTimekeeper", Bodies[RuleType.ApprovedTimekeeper], null, RuleDomain.Billing, Reject)
            .Errors.Single().Should().Contain("D-89");
    }

    [Theory]
    [MemberData(nameof(EveryBillingType))]
    public void ValidateForSave_RefusesAConsequenceOutsideTheThreeDefined(RuleType type)
    {
        foreach (var undefined in new[] { 0, 7, -1, 100000003, int.MaxValue })
        {
            var result = Validator().ValidateForSave(type.ToString(), Bodies[type], null, RuleDomain.Billing, undefined);

            result.IsValid.Should().BeFalse($"{undefined} is not a consequence");
            result.Reason.Should().Be(OntologyWriterFailureReason.ConsequenceNotAllowed);
        }
    }

    [Theory]
    [MemberData(nameof(EveryBillingType))]
    public void ValidateForSave_WithNoConsequenceGiven_IsUnchanged(RuleType type)
    {
        Validator().ValidateForSave(type.ToString(), Bodies[type], null, RuleDomain.Billing).IsValid.Should().BeTrue();
        Validator().ValidateForSave(type.ToString(), Bodies[type], null).IsValid.Should().BeTrue();
        Validator().ValidateForSave(type.ToString(), Bodies[type], null, RuleDomain.Billing, billingConsequence: null).IsValid.Should().BeTrue();
    }

    [Fact]
    public void ValidateForSave_AnInvalidBody_IsRefusedForItsBody_NotItsConsequence()
    {
        var result = Validator().ValidateForSave("AiObservation", """{"type":"AiObservation"}""", null, RuleDomain.Billing, Reject);

        result.IsValid.Should().BeFalse();
        result.Reason.Should().NotBe(OntologyWriterFailureReason.ConsequenceNotAllowed, "the body is checked first");
    }

    [Fact]
    public void ValidateForSave_ForAnOntologyType_IgnoresTheConsequence()
    {
        foreach (var consequence in new int?[] { null, Reject, Flag, 7 })
        {
            Validator().ValidateForSave("Existence", ValidExistenceBody, null, RuleDomain.Ontology, consequence).IsValid.Should().BeTrue();
            Validator().ValidateForSave("Existence", ValidExistenceBody, null, billingConsequence: consequence).IsValid.Should().BeTrue();
        }
    }

    [Fact]
    public void ValidateForSave_AWrongDomain_IsStillRefusedAsWrongDomain()
    {
        var result = Validator().ValidateForSave("AiObservation", Bodies[RuleType.AiObservation], null, RuleDomain.Ontology, Reject);

        result.Reason.Should().Be(OntologyWriterFailureReason.RuleTypeWrongDomain);
    }

    // 073 review, owner question 3: a Duplicate rule that compares lines only may not Reject (a duplicate line is flagged, never rejected).

    private const string LinesOnlyDuplicate = """{"type":"Duplicate","matchFields":["timekeeper","lineDate","hours"]}""";
    private const string InvoiceDuplicate = """{"type":"Duplicate","matchFields":["firmInvoiceNumber"]}""";
    private const string MixedDuplicate = """{"type":"Duplicate","matchFields":["firmInvoiceNumber","timekeeper","lineDate"]}""";

    [Fact]
    public void ValidateForSave_RefusesReject_OnALineLevelDuplicate_WithAClearMessage()
    {
        var result = Validator().ValidateForSave("Duplicate", LinesOnlyDuplicate, null, RuleDomain.Billing, Reject);

        result.IsValid.Should().BeFalse();
        result.Reason.Should().Be(OntologyWriterFailureReason.ConsequenceNotAllowed);
        result.Errors.Should().ContainSingle().Which.Should().Contain("firmInvoiceNumber").And.Contain("Flag").And.Contain("never rejected");
    }

    [Theory]
    [InlineData(InvoiceDuplicate, Reject)]
    [InlineData(MixedDuplicate, Reject)]
    [InlineData(LinesOnlyDuplicate, Flag)]
    public void ValidateForSave_AllowsReject_WhenTheDuplicateComparesTheInvoiceNumber_AndFlagOnALineLevelOne(string body, int consequence)
    {
        Validator().ValidateForSave("Duplicate", body, null, RuleDomain.Billing, consequence).IsValid.Should().BeTrue();
    }

    [Fact]
    public void TheBillingGate_DoesNotRefuseAStoredLineLevelDuplicateReject_TheDeciderHoldsItsFindings()
    {
        // Save only: refusing it at the gate would stop a rule that can still flag duplicate lines; the decider holds them (073).
        var stored = new PolicyVersionSnapshot(VersionId, "BILL-073", ((int)RuleType.Duplicate).ToString(System.Globalization.CultureInfo.InvariantCulture), LinesOnlyDuplicate, null, Reject);

        Validator().TryPrepareBillingRule(stored, out var definition).Should().BeTrue();
        definition.Should().NotBeNull();
    }

    // ── Evaluation time: the billing gate ────────────────────────────────────────────────────────────────────────

    [Theory]
    [MemberData(nameof(RejectRefusedTypes))]
    public void BillingGate_RefusesAStoredReject_LogsIt_AndMetersItWithTheSharedReason(RuleType type)
    {
        var scope = Guid.NewGuid();
        MetricScope.Value = scope;
        using var billing = ListenPolicyInvalid(scope, out var reasons, "billing.rule.invalid");
        using var ontology = ListenPolicyInvalid(scope, out var ontologyReasons, "ontology.policy.invalid");
        var logger = new CapturingLogger<PolicyVersionValidator>();

        var prepared = Validator(logger).TryPrepareBillingRule(Row(type, Reject), out var definition);

        prepared.Should().BeFalse();
        definition.Should().BeNull();
        reasons().Should().Equal(OntologyWriterFailureReason.ConsequenceNotAllowed);
        ontologyReasons().Should().BeEmpty("a billing refusal is never counted on ontology.policy.invalid");
        var entry = logger.Entries.Should().ContainSingle().Subject;
        entry.Level.Should().Be(LogLevel.Error);
        entry.EventId.Id.Should().Be(OntologyWriterEvents.PolicyVersionInvalid.Id);
        entry.Message.Should().Contain("consequence_not_allowed").And.NotContain(Bodies[type], "the rule body is never logged");
    }

    [Theory]
    [MemberData(nameof(EveryBillingType))]
    public void BillingGate_PassesExactlyTheConsequencesTheTypeMayCarry_AndAStatedNone(RuleType type)
    {
        foreach (var consequence in new int?[] { null, Reject, Flag, Suggest, 7 })
        {
            var prepared = Validator().TryPrepareBillingRule(Row(type, consequence), out var definition);

            var expected = consequence is not { } stated || BillingRuleCatalogue.IsConsequenceAllowed(type, stated);
            prepared.Should().Be(expected, $"{type} with {consequence?.ToString() ?? "no consequence"}");
            (definition is not null).Should().Be(expected);
        }
    }

    [Fact]
    public void BillingGate_AVersionThatStatesNoConsequence_IsNotRefused_072sFixturesCarryNone()
    {
        var snapshot = new PolicyVersionSnapshot(VersionId, "BILL-073", "100000003", Bodies[RuleType.ApprovedTimekeeper], null);

        snapshot.Consequence.Should().BeNull();
        Validator().TryPrepareBillingRule(snapshot, out var definition).Should().BeTrue();
        definition.Should().NotBeNull();
    }

    [Fact]
    public void TheOntologyGate_IgnoresTheConsequenceOnASnapshot()
    {
        var snapshot = new PolicyVersionSnapshot(VersionId, "ONT-1", "Existence", ValidExistenceBody, null, Reject);

        Validator().TryPrepareForEvaluation(snapshot, subjectId: null, out var compiled).Should().BeTrue();
        compiled.Should().NotBeNull();
    }

    [Fact]
    public void TheNewReason_IsAStableDocumentedCode()
    {
        OntologyWriterFailureReason.ConsequenceNotAllowed.Should().Be("consequence_not_allowed");
    }

    // ── helpers ──────────────────────────────────────────────────────────────────────────────────────────────────

    private static MeterListener ListenPolicyInvalid(Guid scope, out Func<IReadOnlyList<string>> reasons, string instrumentName)
    {
        var seen = new List<string>();
        var listener = new MeterListener
        {
            InstrumentPublished = (instrument, l) =>
            {
                if (instrument.Meter.Name == OntologyWriterTelemetry.MeterName && instrument.Name == instrumentName)
                {
                    l.EnableMeasurementEvents(instrument);
                }
            },
        };
        listener.SetMeasurementEventCallback<long>((_, _, tags, _) =>
        {
            if (MetricScope.Value != scope) return;
            foreach (var tag in tags)
            {
                if (tag.Key == "reason" && tag.Value is string reason)
                {
                    lock (seen) seen.Add(reason);
                }
            }
        });
        listener.Start();
        reasons = () =>
        {
            lock (seen) return seen.ToArray();
        };
        return listener;
    }
}
