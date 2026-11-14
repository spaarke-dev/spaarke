using System.Diagnostics.Metrics;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Sprk.Bff.Api.Services.Signals;
using Sprk.Bff.Api.Services.Signals.Billing;
using Sprk.Bff.Api.Telemetry;
using Sprk.Bff.Api.Tests.Services.Communication; // CapturingLogger<T> (internal, same assembly)
using Xunit;

namespace Sprk.Bff.Api.Tests.Domain.Signals.Billing;

/// <summary>
/// ebilling-r1 084e fix round 1 (ontology-r1 design): <see cref="PolicyVersionValidator.TryPrepareBillingRuleForRead"/>, the read-side entry of the
/// billing gate. It refuses exactly what <see cref="PolicyVersionValidator.TryPrepareBillingRule"/> refuses (one shared core), and a refusal
/// is at most a Debug log: no <see cref="OntologyWriterEvents.PolicyVersionInvalid"/> error and no <c>billing.rule.invalid</c> meter, so a
/// rules-in-force page view does not drive the gate's alerting. Maintain-class (ADR-038): pure logic.
/// </summary>
public class BillingGateForReadTests
{
    /// <summary>The metric scope of the current test (shared with the read-service test in RecordProcedureOutrankedContractTests).</summary>
    internal static readonly AsyncLocal<Guid> MetricScope = new();
    private static readonly Guid VersionId = Guid.Parse("084e0000-0000-4000-8000-0000000000a1");
    private const string Code = "BILL-084E";
    private const int Reject = (int)Sprk.Bff.Api.Services.Finance.BillingConsequence.Reject;

    private static PolicyVersionValidator Validator(ILogger<PolicyVersionValidator>? logger = null) =>
        new(new RuleBodySchemaValidator(),
            new PredicateCompiler(new RuleBodySchemaValidator(), new FakeTimeProvider(DateTimeOffset.UtcNow)),
            logger ?? NullLogger<PolicyVersionValidator>.Instance);

    private static PolicyVersionSnapshot Row(string? ruleType, string? body, string? template, int? consequence = null) =>
        new(VersionId, Code, ruleType, body, template, consequence);

    /// <summary>The gate's refusal cases: <see cref="BillingRuleCatalogueTests.GateRefusals"/> (type, schema, size, template, form) plus a system
    /// type (D-125) and a stored consequence the type may not carry (task 073).</summary>
    public static TheoryData<string?, string?, string?, int?, string> Refusals()
    {
        var data = new TheoryData<string?, string?, string?, int?, string>();
        foreach (var row in BillingRuleCatalogueTests.GateRefusals)
        {
            data.Add((string?)row[0], (string?)row[1], (string?)row[2], null, (string)row[3]);
        }

        data.Add(((int)RuleType.SubmissionIntegrity).ToString(System.Globalization.CultureInfo.InvariantCulture), """{"type":"SubmissionIntegrity"}""", null, null,
            OntologyWriterFailureReason.RuleTypeUnsupported);
        data.Add("TimeIncrement", """{"type":"TimeIncrement","incrementHours":0.1}""", null, Reject, OntologyWriterFailureReason.ConsequenceNotAllowed);
        return data;
    }

    [Theory(DisplayName = "084e fix 1 (A): the read entry refuses exactly what the billing gate refuses, case by case")]
    [MemberData(nameof(Refusals))]
    public void ForRead_RefusesExactlyWhatTheGateRefuses(string? ruleType, string? body, string? template, int? consequence, string reason)
    {
        var row = Row(ruleType, body, template, consequence);
        var gateLog = new CapturingLogger<PolicyVersionValidator>();

        var gate = Validator(gateLog).TryPrepareBillingRule(row, out var gateDefinition);
        var read = Validator().TryPrepareBillingRuleForRead(row, out var readDefinition);

        gate.Should().BeFalse();
        gateLog.Entries.Should().ContainSingle().Which.Message.Should().Contain(reason, "the case is the gate refusal it names");
        read.Should().Be(gate);
        readDefinition.Should().BeNull();
        gateDefinition.Should().BeNull();
    }

    [Theory(DisplayName = "084e fix 1 (A): the read entry accepts exactly what the billing gate accepts, with the same definition")]
    [MemberData(nameof(BillingRuleCatalogueTests.Valid), MemberType = typeof(BillingRuleCatalogueTests))]
    public void ForRead_AcceptsWhatTheGateAccepts(RuleType type, int optionValue, string body)
    {
        var row = Row(optionValue.ToString(System.Globalization.CultureInfo.InvariantCulture), body, null);

        Validator().TryPrepareBillingRule(row, out var gateDefinition).Should().BeTrue();
        Validator().TryPrepareBillingRuleForRead(row, out var readDefinition).Should().BeTrue();

        readDefinition!.RuleType.Should().Be(type);
        readDefinition.PrecedenceKey.Should().Be(gateDefinition!.PrecedenceKey);
        readDefinition.MaxAmountPerItem.Should().Be(gateDefinition.MaxAmountPerItem);
        readDefinition.PolicyVersionId.Should().Be(gateDefinition.PolicyVersionId);
    }

    [Theory(DisplayName = "084e fix 1 (A): a read-side refusal logs no Error and meters nothing (control: the gate on the same row logs and meters)")]
    [MemberData(nameof(Refusals))]
    public void ForRead_Refusal_NoErrorLog_NoMeter(string? ruleType, string? body, string? template, int? consequence, string reason)
    {
        var row = Row(ruleType, body, template, consequence);
        var scope = Guid.NewGuid();
        MetricScope.Value = scope;
        using var billing = Listen(scope, out var billingReasons, "billing.rule.invalid");
        using var ontology = Listen(scope, out var ontologyReasons, "ontology.policy.invalid");
        var logger = new CapturingLogger<PolicyVersionValidator>();

        Validator(logger).TryPrepareBillingRuleForRead(row, out _).Should().BeFalse();

        logger.Entries.Should().NotContain(e => e.Level > LogLevel.Debug, "a read never logs the gate's error");
        logger.Entries.Should().ContainSingle().Which.Message.Should().Contain(reason, "at most one Debug line naming the reason");
        billingReasons().Should().BeEmpty("a read never counts billing.rule.invalid");
        ontologyReasons().Should().BeEmpty();

        // Must-fire control: the evaluation entry on the same row, in the same scope, does log and meter.
        var control = new CapturingLogger<PolicyVersionValidator>();
        Validator(control).TryPrepareBillingRule(row, out _).Should().BeFalse();
        control.Entries.Should().ContainSingle(e => e.Level == LogLevel.Error);
        billingReasons().Should().Equal(reason);
    }

    [Fact(DisplayName = "084e fix 1 (A): a null snapshot is a caller bug on the read entry too")]
    public void ForRead_NullSnapshot_Throws()
    {
        var act = () => Validator().TryPrepareBillingRuleForRead(null!, out _);

        act.Should().Throw<ArgumentNullException>();
    }

    /// <summary>The K3 listener seam: the <c>reason</c> tags of one instrument of <see cref="OntologyWriterTelemetry.MeterName"/>, in this scope.</summary>
    internal static MeterListener Listen(Guid scope, out Func<IReadOnlyList<string>> reasons, string instrumentName)
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
