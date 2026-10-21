using System.Diagnostics.Metrics;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Signals;
using Sprk.Bff.Api.Services.Signals.Billing;
using Sprk.Bff.Api.Telemetry;
using Sprk.Bff.Api.Tests.Services.Communication; // CapturingLogger<T> / LogEntry (internal, same assembly)
using Xunit;

namespace Sprk.Bff.Api.Tests.Domain.Signals.Billing;

/// <summary>
/// ebilling-r1 task 070 (spec FR-24, D-23, D-27): the eleven billing rule types in the shared closed <see cref="RuleType"/>
/// set — values, domain, schema per type, save-time validation, the billing gate, and fail-closed refusal by every Ontology
/// consumer (compiler, evaluation gate, describer). Maintain-class (ADR-038, <c>tests/unit/domain/**</c>): pure logic.
/// The nightly-job and reader refusals are in <c>SignalReevaluationJobTests.BillingDomain.cs</c> and
/// <c>tests/integration/seam/Signals/PolicyDomainAllowlistSeamTests.cs</c>.
/// </summary>
public class BillingRuleCatalogueTests
{
    private static readonly AsyncLocal<Guid> MetricScope = new();
    private static readonly Guid VersionId = Guid.Parse("70070070-0000-4000-8000-000000000070");
    private const string Code = "BILL-070";

    private const string ValidExistenceBody = """
        {"type":"Existence","subject":"sprk_matter","all":[{"exists":"sprk_communication","path":"sprk_regardingmatter","filter":{"sprk_direction":1}}]}
        """;

    /// <summary>FR-24 order: name, option value, a valid body.</summary>
    public static readonly TheoryData<RuleType, int, string> Valid = new()
    {
        { RuleType.ApprovedTimekeeper, 100000003, """{"type":"ApprovedTimekeeper"}""" },
        { RuleType.ApprovedRate, 100000004, """{"type":"ApprovedRate","tolerancePercent":5}""" },
        { RuleType.TimeIncrement, 100000005, """{"type":"TimeIncrement","incrementHours":0.1}""" },
        { RuleType.DailyHoursCap, 100000006, """{"type":"DailyHoursCap","maxHoursPerDay":10}""" },
        { RuleType.NonBillableActivity, 100000007, """{"type":"NonBillableActivity","codes":["A101","L110"]}""" },
        { RuleType.ExpenseRule, 100000008, """{"type":"ExpenseRule","nonReimbursableCodes":["E101"],"maxAmountPerItem":500}""" },
        { RuleType.InvoiceAge, 100000009, """{"type":"InvoiceAge","maxAgeDays":90}""" },
        { RuleType.CoreRecordWindow, 100000010, """{"type":"CoreRecordWindow","graceDays":30}""" },
        { RuleType.Duplicate, 100000011, """{"type":"Duplicate","matchFields":["firmInvoiceNumber","timekeeper","lineDate","hours"]}""" },
        { RuleType.RequiredData, 100000012, """{"type":"RequiredData","fields":["billingPeriodStart","taskCode"]}""" },
        { RuleType.AiObservation, 100000013, """{"type":"AiObservation","observation":"blockBilling","minConfidence":0.85}""" },
        { RuleType.AddedFee, 100000015, """{"type":"AddedFee","feeKind":"lateFee","allowance":"cap","feeCodes":["E120"],"capAmount":100}""" },
        { RuleType.Discount, 100000016, """{"type":"Discount","discountPercent":10,"tolerancePercent":1}""" },
    };

    /// <summary>Missing required parameter (ApprovedTimekeeper has none, so it has no row); the field the error names.</summary>
    public static readonly TheoryData<RuleType, string, string> MissingParameter = new()
    {
        { RuleType.ApprovedRate, """{"type":"ApprovedRate"}""", "tolerancePercent" },
        { RuleType.TimeIncrement, """{"type":"TimeIncrement"}""", "incrementHours" },
        { RuleType.DailyHoursCap, """{"type":"DailyHoursCap"}""", "maxHoursPerDay" },
        { RuleType.NonBillableActivity, """{"type":"NonBillableActivity"}""", "codes" },
        { RuleType.ExpenseRule, """{"type":"ExpenseRule"}""", "nonReimbursableCodes" },
        { RuleType.InvoiceAge, """{"type":"InvoiceAge"}""", "maxAgeDays" },
        { RuleType.CoreRecordWindow, """{"type":"CoreRecordWindow"}""", "graceDays" },
        { RuleType.Duplicate, """{"type":"Duplicate"}""", "matchFields" },
        { RuleType.RequiredData, """{"type":"RequiredData"}""", "fields" },
        { RuleType.AiObservation, """{"type":"AiObservation","observation":"blockBilling"}""", "minConfidence" },
        { RuleType.AddedFee, """{"type":"AddedFee","feeKind":"lateFee","feeCodes":["E120"],"capAmount":100}""", "allowance" },
        { RuleType.AddedFee, """{"type":"AddedFee","feeKind":"lateFee","allowance":"cap","capAmount":100}""", "feeCodes" },
        { RuleType.Discount, """{"type":"Discount","tolerancePercent":1}""", "discountPercent" },
    };

    /// <summary>A value outside the parameter's range or closed vocabulary; the field the error names.</summary>
    public static readonly TheoryData<RuleType, string, string> OutOfRange = new()
    {
        { RuleType.ApprovedRate, """{"type":"ApprovedRate","tolerancePercent":101}""", "/tolerancePercent" },
        { RuleType.ApprovedRate, """{"type":"ApprovedRate","tolerancePercent":-1}""", "/tolerancePercent" },
        { RuleType.TimeIncrement, """{"type":"TimeIncrement","incrementHours":0}""", "/incrementHours" },
        { RuleType.DailyHoursCap, """{"type":"DailyHoursCap","maxHoursPerDay":25}""", "/maxHoursPerDay" },
        { RuleType.NonBillableActivity, """{"type":"NonBillableActivity","codes":[]}""", "/codes" },
        { RuleType.NonBillableActivity, """{"type":"NonBillableActivity","codes":["$ref"]}""", "/codes/0" },
        { RuleType.ExpenseRule, """{"type":"ExpenseRule","maxAmountPerItem":0}""", "/maxAmountPerItem" },
        { RuleType.InvoiceAge, """{"type":"InvoiceAge","maxAgeDays":0}""", "/maxAgeDays" },
        { RuleType.InvoiceAge, """{"type":"InvoiceAge","maxAgeDays":1.5}""", "/maxAgeDays" },
        { RuleType.CoreRecordWindow, """{"type":"CoreRecordWindow","graceDays":-1}""", "/graceDays" },
        { RuleType.Duplicate, """{"type":"Duplicate","matchFields":["colour"]}""", "/matchFields/0" },
        { RuleType.Duplicate, """{"type":"Duplicate","matchFields":["hours","hours"]}""", "/matchFields" },
        { RuleType.RequiredData, """{"type":"RequiredData","fields":["shoeSize"]}""", "/fields/0" },
        { RuleType.AiObservation, """{"type":"AiObservation","observation":"blockBilling","minConfidence":1.5}""", "/minConfidence" },
        { RuleType.AiObservation, """{"type":"AiObservation","observation":"rudeTone","minConfidence":0.9}""", "/observation" },
        { RuleType.AddedFee, """{"type":"AddedFee","feeKind":"parkingFee","allowance":"cap","feeCodes":["E120"],"capAmount":100}""", "/feeKind" },
        { RuleType.AddedFee, """{"type":"AddedFee","feeKind":"lateFee","allowance":"sometimes"}""", "/allowance" },
        { RuleType.AddedFee, """{"type":"AddedFee","feeKind":"lateFee","allowance":"cap","feeCodes":["E120"],"capAmount":0}""", "/capAmount" },
        { RuleType.AddedFee, """{"type":"AddedFee","feeKind":"lateFee","allowance":"cap","feeCodes":["E120"],"capPercent":101}""", "/capPercent" },
        { RuleType.AddedFee, """{"type":"AddedFee","feeKind":"lateFee","allowance":"afterDays","afterDays":0}""", "/afterDays" },
        { RuleType.Discount, """{"type":"Discount","discountPercent":100}""", "/discountPercent" },
        { RuleType.Discount, """{"type":"Discount","discountPercent":0}""", "/discountPercent" },
        { RuleType.Discount, """{"type":"Discount","discountPercent":10,"tolerancePercent":-1}""", "/tolerancePercent" },
    };

    private static PolicyVersionValidator Validator(ILogger<PolicyVersionValidator>? logger = null) =>
        new(new RuleBodySchemaValidator(),
            new PredicateCompiler(new RuleBodySchemaValidator(), new FakeTimeProvider(DateTimeOffset.UtcNow)),
            logger ?? NullLogger<PolicyVersionValidator>.Instance);

    private static PolicyVersionSnapshot Row(string? ruleType, string? body, string? template = null) =>
        new(VersionId, Code, ruleType, body, template);

    private static string With(string body, string member) => body[..^1] + "," + member + "}";

    // ── The closed set: 14 members, values, domain, catalogue position ─────────────────────────────────────────────

    [Fact]
    public void RuleType_HasSeventeenMembers_ThreeOntologyThenElevenBillingInFr24OrderThenTheSystemTypeThenAddedFeeAndDiscount()
    {
        Enum.GetValues<RuleType>().Should().HaveCount(17);
        Enum.GetValues<RuleType>().Where(RuleDomains.IsOntology)
            .Should().BeEquivalentTo(new[] { RuleType.Threshold, RuleType.Switch, RuleType.Existence });
        // D-125: the system type is a Billing member that is not in the (authorable) catalogue, with the next free option value.
        // Task 115: AddedFee and Discount take the next two values after it and are positions 12 and 13 of the catalogue.
        Enum.GetValues<RuleType>().Where(RuleDomains.IsBilling)
            .Should().Equal(BillingRuleCatalogue.Types.Take(11).Append(RuleType.SubmissionIntegrity).Append(RuleType.AddedFee).Append(RuleType.Discount));
        ((int)RuleType.SubmissionIntegrity).Should().Be(100000014);
        ((int)RuleType.AddedFee).Should().Be(100000015);
        ((int)RuleType.Discount).Should().Be(100000016);
        BillingRuleCatalogue.Types.Should().NotContain(RuleType.SubmissionIntegrity);
        BillingRuleCatalogue.Types.Select(t => t.ToString()).Should().Equal(
            "ApprovedTimekeeper", "ApprovedRate", "TimeIncrement", "DailyHoursCap", "NonBillableActivity", "ExpenseRule",
            "InvoiceAge", "CoreRecordWindow", "Duplicate", "RequiredData", "AiObservation", "AddedFee", "Discount");
    }

    [Theory]
    [MemberData(nameof(Valid))]
    public void BillingType_NameAndOptionValueAndDomain_ArePinnedBothWays(RuleType type, int optionValue, string _)
    {
        ((int)type).Should().Be(optionValue);
        ((RuleType)optionValue).Should().Be(type);
        RuleBodySchemaValidator.TryParseRuleType(type.ToString(), out var byName).Should().BeTrue();
        byName.Should().Be(type);
        RuleBodySchemaValidator.TryParseRuleType(optionValue.ToString(System.Globalization.CultureInfo.InvariantCulture), out var byValue)
            .Should().BeTrue();
        byValue.Should().Be(type);
        RuleDomains.DomainOf(type).Should().Be(RuleDomain.Billing);
        // FR-26 numbering = option value - 100000002 for 1-11; 100000014 is the system type (no position), so AddedFee and Discount
        // (100000015, 100000016) are positions 12 and 13.
        BillingRuleCatalogue.CataloguePosition(type).Should().Be(
            optionValue <= 100000013 ? optionValue - 100000002 : optionValue - 100000003, "catalogue position by option value");
    }

    [Fact]
    public void Fr26RejectEligiblePositions_MapToTheExpectedTypes()
    {
        // FR-26: types 1, 2, 6-10 may reject; 3-5 and 11 may not (eligibility itself is task 073; D-89 holds type 1 through
        // the reference-missing confidence cap, not through eligibility).
        new[] { 1, 2, 6, 7, 8, 9, 10 }.Select(n => BillingRuleCatalogue.Types[n - 1]).Should().Equal(
            RuleType.ApprovedTimekeeper, RuleType.ApprovedRate, RuleType.ExpenseRule, RuleType.InvoiceAge,
            RuleType.CoreRecordWindow, RuleType.Duplicate, RuleType.RequiredData);
    }

    [Theory]
    [InlineData(RuleType.Threshold)]
    [InlineData(RuleType.Switch)]
    [InlineData(RuleType.Existence)]
    public void OntologyType_IsNotInTheBillingCatalogue(RuleType type)
    {
        RuleDomains.DomainOf(type).Should().Be(RuleDomain.Ontology);
        var act = () => BillingRuleCatalogue.CataloguePosition(type);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    // ── Save-time validation (ValidateForSave) per type ─────────────────────────────────────────────────────────────

    [Theory]
    [MemberData(nameof(Valid))]
    public void ValidBody_PassesValidateForSave_ByNameAndByOptionValue(RuleType type, int optionValue, string body)
    {
        Validator().ValidateForSave(type.ToString(), body, null).IsValid.Should().BeTrue();
        Validator().ValidateForSave(optionValue.ToString(System.Globalization.CultureInfo.InvariantCulture), body, null)
            .IsValid.Should().BeTrue();
    }

    [Theory]
    [MemberData(nameof(MissingParameter))]
    public void MissingParameter_IsRefused_SchemaInvalid_NamingTheField(RuleType type, string body, string field)
    {
        var result = Validator().ValidateForSave(type.ToString(), body, null);

        result.IsValid.Should().BeFalse();
        result.Reason.Should().Be(OntologyWriterFailureReason.SchemaInvalid);
        result.Errors.Should().Contain(e => e.Contains(field, StringComparison.Ordinal));
    }

    [Theory]
    [MemberData(nameof(OutOfRange))]
    public void OutOfRangeValue_IsRefused_SchemaInvalid_AtTheFieldsPointer(RuleType type, string body, string pointer)
    {
        var result = Validator().ValidateForSave(type.ToString(), body, null);

        result.IsValid.Should().BeFalse();
        result.Reason.Should().Be(OntologyWriterFailureReason.SchemaInvalid);
        result.Errors.Should().Contain(e => e.StartsWith(pointer + ":", StringComparison.Ordinal));
    }

    [Theory]
    [MemberData(nameof(Valid))]
    public void ExtraProperty_IsRefused_AtItsPointer(RuleType type, int _, string body)
    {
        var result = Validator().ValidateForSave(type.ToString(), With(body, "\"rate\":250"), null);

        result.IsValid.Should().BeFalse();
        result.Reason.Should().Be(OntologyWriterFailureReason.SchemaInvalid);
        result.Errors.Should().Contain(e => e.StartsWith("/rate:", StringComparison.Ordinal),
            "a copied rate (or any other member) is refused, never ignored (design.md 4: parameters only)");
    }

    [Theory]
    [MemberData(nameof(Valid))]
    public void WrongTypeConst_IsRefused_AtTypePointer(RuleType type, int _, string body)
    {
        // The body is valid for its own type but saved under the next billing type, and under Existence.
        var other = BillingRuleCatalogue.Types[BillingRuleCatalogue.CataloguePosition(type) % BillingRuleCatalogue.Types.Count];

        var asOther = Validator().ValidateForSave(other.ToString(), body, null);
        asOther.IsValid.Should().BeFalse();
        asOther.Errors.Should().Contain(e => e.StartsWith("/type:", StringComparison.Ordinal));

        var asExistence = Validator().ValidateForSave("Existence", body, null);
        asExistence.IsValid.Should().BeFalse();
        asExistence.Reason.Should().Be(OntologyWriterFailureReason.SchemaInvalid);
    }

    [Theory]
    [MemberData(nameof(Valid))]
    public void MessageTemplateOrThenProperty_IsRefused_AtItsPointer(RuleType type, int _, string body)
    {
        var template = Validator().ValidateForSave(type.ToString(), With(body, "\"messageTemplate\":\"Rate {{line.rate}}\""), null);
        template.IsValid.Should().BeFalse();
        template.Reason.Should().Be(OntologyWriterFailureReason.SchemaInvalid);
        template.Errors.Should().Contain(e => e.StartsWith("/messageTemplate:", StringComparison.Ordinal));

        var then = Validator().ValidateForSave(type.ToString(), With(body, "\"then\":{\"consequence\":\"Reject\"}"), null);
        then.IsValid.Should().BeFalse();
        then.Reason.Should().Be(OntologyWriterFailureReason.SchemaInvalid);
        then.Errors.Should().Contain(e => e.StartsWith("/then:", StringComparison.Ordinal));
    }

    [Fact]
    public void ExpenseRule_WithOnlyCodesOrOnlyCap_IsValid()
    {
        Validator().ValidateForSave("ExpenseRule", """{"type":"ExpenseRule","nonReimbursableCodes":["E101"]}""", null).IsValid.Should().BeTrue();
        Validator().ValidateForSave("ExpenseRule", """{"type":"ExpenseRule","maxAmountPerItem":250.5}""", null).IsValid.Should().BeTrue();
    }

    [Fact]
    public void OversizedBillingBody_IsRefused_RuleBodyTooLarge_BeforeParsing()
    {
        var body = "{\"type\":\"ApprovedTimekeeper\"" + new string(' ', RuleBodySchemaValidator.MaxRuleBodyLength) + "}";

        Validator().ValidateForSave("ApprovedTimekeeper", body, null).Reason.Should().Be(OntologyWriterFailureReason.RuleBodyTooLarge);
    }

    [Theory]
    [MemberData(nameof(Valid))]
    public void Template_AllowsExactlyTheTypesFactFields(RuleType type, int _, string body)
    {
        var allowed = BillingRuleCatalogue.TemplateFactFields(type);
        allowed.Should().NotBeEmpty();
        var template = string.Join(" ", allowed.Select(f => "{{" + f + "}}"));
        Validator().ValidateForSave(type.ToString(), body, template).IsValid.Should().BeTrue();

        var outside = Validator().ValidateForSave(type.ToString(), body, "Billed {{sprk_totalbudget}}.");
        outside.IsValid.Should().BeFalse();
        outside.Reason.Should().Be(OntologyWriterFailureReason.TemplateTokenOutsideReadSet);
        outside.Errors.Should().ContainSingle().Which.Should().Contain("sprk_totalbudget");

        Validator().ValidateForSave(type.ToString(), body, "Billed {{line.rate").Reason
            .Should().Be(OntologyWriterFailureReason.TemplateMalformed);
    }

    [Fact]
    public void Template_ParameterTokenOfAnotherType_IsRefused()
    {
        // rule.maxAgeDays belongs to InvoiceAge; an ApprovedRate rule does not read it.
        Validator().ValidateForSave("ApprovedRate", """{"type":"ApprovedRate","tolerancePercent":5}""", "Older than {{rule.maxAgeDays}} days.")
            .Reason.Should().Be(OntologyWriterFailureReason.TemplateTokenOutsideReadSet);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("Bogus")]
    [InlineData("approvedrate,InvoiceAge")]
    [InlineData("ApprovedRate,Threshold")]
    [InlineData("+100000004")]
    [InlineData(" 100000004")]
    [InlineData("-100000004")]
    [InlineData("100000014")]
    [InlineData("100000099")]
    public void UnknownBlankCombinedOrSignedType_IsRefused_RuleTypeUnsupported_Everywhere(string? raw)
    {
        const string body = """{"type":"ApprovedRate","tolerancePercent":5}""";

        Validator().ValidateForSave(raw, body, null).Reason.Should().Be(OntologyWriterFailureReason.RuleTypeUnsupported);
        Validator().TryPrepareForEvaluation(Row(raw, body), null, out var compiled).Should().BeFalse();
        compiled.Should().BeNull();
        Validator().TryPrepareBillingRule(Row(raw, body), out var definition).Should().BeFalse();
        definition.Should().BeNull();
    }

    // ── Ontology consumers refuse every billing body ────────────────────────────────────────────────────────────────

    [Theory]
    [MemberData(nameof(Valid))]
    public void PredicateCompiler_RefusesEveryBillingBody_ByItsTypeGuard(RuleType type, int _, string body)
    {
        var compiler = new PredicateCompiler(new RuleBodySchemaValidator(), new FakeTimeProvider(DateTimeOffset.UtcNow));

        var named = () => compiler.Compile(body, null, type);
        named.Should().Throw<PredicateCompilationException>().WithMessage($"Rule type '{type}' has no compiler*");

        var asDefault = () => compiler.Compile(body);
        asDefault.Should().Throw<PredicateCompilationException>("the default (Existence) compile refuses a billing body's shape");
    }

    [Theory]
    [MemberData(nameof(Valid))]
    public void OntologyEvaluationGate_RefusesEveryBillingRow_RuleTypeUnsupported_LoggedAndMetered(RuleType type, int optionValue, string body)
    {
        var scope = Guid.NewGuid();
        MetricScope.Value = scope;
        using var listener = ListenPolicyInvalid(scope, out var reasons);
        using var billingListener = ListenPolicyInvalid(scope, out var billingReasons, BillingInvalidInstrument);
        var logger = new CapturingLogger<PolicyVersionValidator>();

        var byValue = Validator(logger).TryPrepareForEvaluation(
            Row(optionValue.ToString(System.Globalization.CultureInfo.InvariantCulture), body), null, out var compiled);

        byValue.Should().BeFalse("a billing type is never compiled or treated as true by the Ontology evaluator");
        compiled.Should().BeNull();
        logger.Entries.Should().ContainSingle(e => e.Level == LogLevel.Error).Which.EventId.Id
            .Should().Be(OntologyWriterEvents.PolicyVersionInvalid.Id);
        reasons().Should().Equal(OntologyWriterFailureReason.RuleTypeUnsupported);
        billingReasons().Should().BeEmpty("the Ontology gate keeps metering on ontology.policy.invalid only");

        Validator().TryPrepareForEvaluation(Row(type.ToString(), body), Guid.NewGuid(), out _).Should().BeFalse();
    }

    // ── The billing gate ────────────────────────────────────────────────────────────────────────────────────────────

    [Theory]
    [MemberData(nameof(Valid))]
    public void BillingGate_ReturnsADefinition_ForAValidBillingRow(RuleType type, int optionValue, string body)
    {
        var logger = new CapturingLogger<PolicyVersionValidator>();

        Validator(logger).TryPrepareBillingRule(
            Row(optionValue.ToString(System.Globalization.CultureInfo.InvariantCulture), body), out var definition).Should().BeTrue();

        definition!.RuleType.Should().Be(type);
        definition.PolicyVersionId.Should().Be(VersionId);
        definition.PolicyCode.Should().Be(Code);
        definition.CataloguePosition.Should().Be(optionValue <= 100000013 ? optionValue - 100000002 : optionValue - 100000003);
        definition.TemplateFactFields.Should().BeEquivalentTo(BillingRuleCatalogue.TemplateFactFields(type));
        logger.Entries.Should().BeEmpty();
    }

    [Fact]
    public void BillingGate_ParsesEachTypesParameters()
    {
        BillingRuleDefinition Parse(string type, string body)
        {
            Validator().TryPrepareBillingRule(Row(type, body), out var d).Should().BeTrue();
            return d!;
        }

        Parse("ApprovedRate", """{"type":"ApprovedRate","tolerancePercent":2.5}""").TolerancePercent.Should().Be(2.5m);
        Parse("TimeIncrement", """{"type":"TimeIncrement","incrementHours":0.1}""").IncrementHours.Should().Be(0.1m);
        Parse("DailyHoursCap", """{"type":"DailyHoursCap","maxHoursPerDay":10}""").MaxHoursPerDay.Should().Be(10m);
        Parse("NonBillableActivity", """{"type":"NonBillableActivity","codes":["A101","L110"]}""").Codes.Should().Equal("A101", "L110");
        var expense = Parse("ExpenseRule", """{"type":"ExpenseRule","nonReimbursableCodes":["E101"],"maxAmountPerItem":500}""");
        expense.NonReimbursableCodes.Should().Equal("E101");
        expense.MaxAmountPerItem.Should().Be(500m);
        Parse("ExpenseRule", """{"type":"ExpenseRule","maxAmountPerItem":500}""").NonReimbursableCodes.Should().BeEmpty();
        Parse("InvoiceAge", """{"type":"InvoiceAge","maxAgeDays":90}""").MaxAgeDays.Should().Be(90);
        Parse("InvoiceAge", """{"type":"InvoiceAge","maxAgeDays":90.0}""").MaxAgeDays.Should().Be(90, "JSON Schema 'integer' admits 90.0");
        Parse("CoreRecordWindow", """{"type":"CoreRecordWindow","graceDays":0}""").GraceDays.Should().Be(0);
        Parse("Duplicate", """{"type":"Duplicate","matchFields":["firmInvoiceNumber"]}""").MatchFields.Should().Equal("firmInvoiceNumber");
        Parse("RequiredData", """{"type":"RequiredData","fields":["taskCode"]}""").Fields.Should().Equal("taskCode");
        var ai = Parse("AiObservation", """{"type":"AiObservation","observation":"vagueDescription","minConfidence":0.9}""");
        ai.Observation.Should().Be("vagueDescription");
        ai.MinConfidence.Should().Be(0.9m);
        var timekeeper = Parse("ApprovedTimekeeper", """{"type":"ApprovedTimekeeper"}""");
        timekeeper.TolerancePercent.Should().BeNull();
        timekeeper.Codes.Should().BeEmpty();
        var fee = Parse("AddedFee", """{"type":"AddedFee","feeKind":"interest","allowance":"cap","feeCodes":["E120","E121"],"capPercent":1.5}""");
        fee.FeeKind.Should().Be("interest");
        fee.Allowance.Should().Be("cap");
        fee.FeeCodes.Should().Equal("E120", "E121");
        fee.CapPercent.Should().Be(1.5m);
        fee.CapAmount.Should().BeNull();
        var discount = Parse("Discount", """{"type":"Discount","discountPercent":12.5}""");
        discount.DiscountPercent.Should().Be(12.5m);
        discount.TolerancePercent.Should().BeNull("absent = 0 in the handler");
    }

    // ── Task 115: AddedFee forms the facts cannot judge yet are refused at save and at the gate ──────────────────

    public static readonly TheoryData<string, string> UnsupportedAddedFeeForms = new()
    {
        { """{"type":"AddedFee","feeKind":"lateFee","allowance":"notAllowed"}""", BillingRuleCatalogue.AddedFeeNotAllowedRefusal },
        { """{"type":"AddedFee","feeKind":"interest","allowance":"afterDays","afterDays":30}""", BillingRuleCatalogue.AddedFeeAfterDaysRefusal },
    };

    [Theory]
    [MemberData(nameof(UnsupportedAddedFeeForms))]
    public void AddedFee_NotAllowedAndAfterDays_AreSchemaValid_ButRefusedAtSave_RuleFormNotSupported(string body, string refusal)
    {
        new RuleBodySchemaValidator().Validate(RuleType.AddedFee, body).IsValid.Should().BeTrue("the schema describes all three forms");

        var result = Validator().ValidateForSave("AddedFee", body, null, RuleDomain.Billing, (int)Sprk.Bff.Api.Services.Finance.BillingConsequence.Flag);

        result.IsValid.Should().BeFalse();
        result.Reason.Should().Be(OntologyWriterFailureReason.RuleFormNotSupported);
        result.Errors.Should().ContainSingle().Which.Should().Be(refusal);
    }

    [Fact]
    public void AddedFee_ACapBody_PassesSaveAndTheGate_AndAWrongCapShapeIsSchemaInvalid()
    {
        const string amount = """{"type":"AddedFee","feeKind":"lateFee","allowance":"cap","feeCodes":["E120"],"capAmount":100}""";
        const string percent = """{"type":"AddedFee","feeKind":"other","allowance":"cap","feeCodes":["E120"],"capPercent":5}""";
        foreach (var body in new[] { amount, percent })
        {
            Validator().ValidateForSave("AddedFee", body, null).IsValid.Should().BeTrue(body);
            Validator().TryPrepareBillingRule(Row("AddedFee", body), out _).Should().BeTrue(body);
        }

        // A cap with both limits or neither, a cap with afterDays, and another form carrying a cap's members are refused by the schema.
        foreach (var body in new[]
        {
            """{"type":"AddedFee","feeKind":"lateFee","allowance":"cap","feeCodes":["E120"],"capAmount":100,"capPercent":5}""",
            """{"type":"AddedFee","feeKind":"lateFee","allowance":"cap","feeCodes":["E120"]}""",
            """{"type":"AddedFee","feeKind":"lateFee","allowance":"cap","feeCodes":["E120"],"capAmount":100,"afterDays":3}""",
            """{"type":"AddedFee","feeKind":"lateFee","allowance":"notAllowed","feeCodes":["E120"]}""",
            """{"type":"AddedFee","feeKind":"lateFee","allowance":"afterDays","afterDays":3,"capAmount":100}""",
            """{"type":"AddedFee","feeKind":"lateFee","allowance":"afterDays"}""",
        })
        {
            Validator().ValidateForSave("AddedFee", body, null).Reason.Should().Be(OntologyWriterFailureReason.SchemaInvalid, body);
        }
    }

    [Theory]
    [InlineData(RuleType.AddedFee, """{"type":"AddedFee","feeKind":"lateFee","allowance":"cap","feeCodes":["E120"],"capAmount":100}""")]
    [InlineData(RuleType.Discount, """{"type":"Discount","discountPercent":10}""")]
    public void AddedFeeAndDiscount_PublishingWithRejectOrSuggestAdjustment_IsRefused_FlagIsAccepted(RuleType type, string body)
    {
        var reject = Validator().ValidateForSave(type.ToString(), body, null, RuleDomain.Billing, (int)Sprk.Bff.Api.Services.Finance.BillingConsequence.Reject);
        reject.IsValid.Should().BeFalse();
        reject.Reason.Should().Be(OntologyWriterFailureReason.ConsequenceNotAllowed);

        Validator().ValidateForSave(type.ToString(), body, null, RuleDomain.Billing, (int)Sprk.Bff.Api.Services.Finance.BillingConsequence.SuggestAdjustment)
            .Reason.Should().Be(OntologyWriterFailureReason.ConsequenceNotAllowed);
        Validator().ValidateForSave(type.ToString(), body, null, RuleDomain.Billing, (int)Sprk.Bff.Api.Services.Finance.BillingConsequence.Flag)
            .IsValid.Should().BeTrue();
    }

    public static readonly TheoryData<string?, string?, string?, string> GateRefusals = new()
    {
        { "Existence", ValidExistenceBody, null, OntologyWriterFailureReason.RuleTypeUnsupported },
        { "100000002", ValidExistenceBody, null, OntologyWriterFailureReason.RuleTypeUnsupported },
        { "Threshold", """{"type":"Threshold","subject":"sprk_matter","threshold":{"field":"sprk_budgetutilizationpercent","operator":">=","value":90}}""", null, OntologyWriterFailureReason.RuleTypeUnsupported },
        { "Switch", "{}", null, OntologyWriterFailureReason.RuleTypeUnsupported },
        { "Bogus", """{"type":"ApprovedTimekeeper"}""", null, OntologyWriterFailureReason.RuleTypeUnsupported },
        { null, """{"type":"ApprovedTimekeeper"}""", null, OntologyWriterFailureReason.RuleTypeUnsupported },
        { "ApprovedRate", """{"type":"ApprovedRate"}""", null, OntologyWriterFailureReason.SchemaInvalid },
        { "ApprovedRate", """{"type":"ApprovedTimekeeper"}""", null, OntologyWriterFailureReason.SchemaInvalid },
        { "ApprovedRate", null, null, OntologyWriterFailureReason.SchemaInvalid },
        { "ApprovedRate", "{ not json", null, OntologyWriterFailureReason.SchemaInvalid },
        { "ApprovedRate", """{"type":"ApprovedRate","tolerancePercent":5,"tolerancePercent":6}""", null, OntologyWriterFailureReason.SchemaInvalid },
        { "ApprovedRate", """{"type":"ApprovedRate","tolerancePercent":1e999999}""", null, OntologyWriterFailureReason.SchemaInvalid },
        { "ApprovedRate", """{"type":"ApprovedRate","tolerancePercent":5}""", "{{sprk_totalbudget}}", OntologyWriterFailureReason.TemplateTokenOutsideReadSet },
        { "ApprovedRate", "{\"type\":\"ApprovedRate\"" + new string(' ', RuleBodySchemaValidator.MaxRuleBodyLength) + "}", null, OntologyWriterFailureReason.RuleBodyTooLarge },
        // Task 115: AddedFee forms the facts cannot judge are refused at the evaluation gate too, never evaluated as "holds".
        { "AddedFee", """{"type":"AddedFee","feeKind":"lateFee","allowance":"notAllowed"}""", null, OntologyWriterFailureReason.RuleFormNotSupported },
        { "AddedFee", """{"type":"AddedFee","feeKind":"interest","allowance":"afterDays","afterDays":30}""", null, OntologyWriterFailureReason.RuleFormNotSupported },
    };

    [Theory]
    [MemberData(nameof(GateRefusals))]
    public void BillingGate_RefusesOntologyUnknownInvalidAndOversizedRows_LogsMetersNeverThrows(
        string? ruleType, string? body, string? template, string expectedReason)
    {
        var scope = Guid.NewGuid();
        MetricScope.Value = scope;
        using var listener = ListenPolicyInvalid(scope, out var ontologyReasons);
        using var billingListener = ListenPolicyInvalid(scope, out var reasons, BillingInvalidInstrument);
        var logger = new CapturingLogger<PolicyVersionValidator>();
        BillingRuleDefinition? definition = null;

        var act = () => Validator(logger).TryPrepareBillingRule(Row(ruleType, body, template), out definition);

        act.Should().NotThrow().Which.Should().BeFalse();
        definition.Should().BeNull();
        reasons().Should().Equal(expectedReason);
        ontologyReasons().Should().BeEmpty("K3: a billing refusal is never counted on ontology.policy.invalid");
        var entry = logger.Entries.Should().ContainSingle().Subject;
        entry.EventId.Id.Should().Be(OntologyWriterEvents.PolicyVersionInvalid.Id);
        if (body is { Length: > 0 } && body.Length < 200)
        {
            entry.Message.Should().NotContain(body, "the rule body is never logged");
        }
    }

    // ── Expected-domain save validation (fix-round-1 F4) ────────────────────────────────────────────────────────────

    [Theory]
    [MemberData(nameof(Valid))]
    public void ValidateForSave_ExpectingOntology_RefusesEveryBillingType_WrongDomain(RuleType type, int optionValue, string body)
    {
        var byName = Validator().ValidateForSave(type.ToString(), body, null, RuleDomain.Ontology);
        var byValue = Validator().ValidateForSave(
            optionValue.ToString(System.Globalization.CultureInfo.InvariantCulture), body, null, RuleDomain.Ontology);

        byName.IsValid.Should().BeFalse();
        byName.Reason.Should().Be(OntologyWriterFailureReason.RuleTypeWrongDomain);
        byValue.Reason.Should().Be(OntologyWriterFailureReason.RuleTypeWrongDomain);
        // The same row with no expectation, or the matching one, is valid: the parameter changes nothing else.
        Validator().ValidateForSave(type.ToString(), body, null).IsValid.Should().BeTrue();
        Validator().ValidateForSave(type.ToString(), body, null, RuleDomain.Billing).IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData("Existence")]
    [InlineData("Threshold")]
    [InlineData("Switch")]
    [InlineData("100000002")]
    public void ValidateForSave_ExpectingBilling_RefusesEveryOntologyType_WrongDomain(string ruleType)
    {
        var result = Validator().ValidateForSave(ruleType, ValidExistenceBody, null, RuleDomain.Billing);

        result.IsValid.Should().BeFalse();
        result.Reason.Should().Be(OntologyWriterFailureReason.RuleTypeWrongDomain);
    }

    [Fact]
    public void ValidateForSave_ExpectingOntology_StillValidatesAnOntologyType_AndKeepsUnknownAsUnsupported()
    {
        Validator().ValidateForSave("Existence", ValidExistenceBody, null, RuleDomain.Ontology).IsValid.Should().BeTrue();
        Validator().ValidateForSave("Existence", ValidExistenceBody, null).IsValid.Should().BeTrue("no expectation = unchanged");

        foreach (var domain in new RuleDomain?[] { RuleDomain.Ontology, RuleDomain.Billing })
        {
            Validator().ValidateForSave("Bogus", ValidExistenceBody, null, domain).Reason
                .Should().Be(OntologyWriterFailureReason.RuleTypeUnsupported, "an unknown type is not a wrong-domain type");
            Validator().ValidateForSave(null, ValidExistenceBody, null, domain).Reason
                .Should().Be(OntologyWriterFailureReason.RuleTypeUnsupported);
        }
    }

    /// <summary>fix-round-2 (ontology-r1 paired change): ValidateForAuthoring is the Ontology admin's one validation (validate, dry run,
    /// save, publish), so it expects the Ontology domain: a billing type, by name or option value and even with a valid billing body,
    /// is refused as RuleTypeWrongDomain before any body check, and offers no template fields (nothing was compiled).</summary>
    [Theory]
    [MemberData(nameof(Valid))]
    public void ValidateForAuthoring_RefusesEveryBillingType_WrongDomain_WithNoTemplateFields(RuleType type, int optionValue, string body)
    {
        foreach (var raw in new[] { type.ToString(), optionValue.ToString(System.Globalization.CultureInfo.InvariantCulture) })
        {
            var result = Validator().ValidateForAuthoring(raw, body, null);

            result.Validation.IsValid.Should().BeFalse(raw);
            result.Validation.Reason.Should().Be(OntologyWriterFailureReason.RuleTypeWrongDomain, raw);
            result.TemplateEligibleFields.Should().BeEmpty(raw);
        }

        // Control: an Ontology type is still validated (and compiled) by the same call.
        Validator().ValidateForAuthoring("Existence", """{"type":"Existence","subject":"sprk_matter","when":{"statecode":0},"all":[]}""", null)
            .Validation.IsValid.Should().BeTrue();
    }

    [Fact]
    public void BillingGate_NullSnapshot_IsACallerBug()
    {
        var act = () => Validator().TryPrepareBillingRule(null!, out _);
        act.Should().Throw<ArgumentNullException>();
    }

    // ── The describer ───────────────────────────────────────────────────────────────────────────────────────────────

    public static readonly TheoryData<string, string> Descriptions = new()
    {
        { """{"type":"ApprovedTimekeeper"}""", "ApprovedTimekeeper()" },
        { """{"type":"ApprovedRate","tolerancePercent":5}""", "ApprovedRate(tolerancePercent=5)" },
        { """{"type":"TimeIncrement","incrementHours":0.1}""", "TimeIncrement(incrementHours=0.1)" },
        { """{"type":"DailyHoursCap","maxHoursPerDay":10}""", "DailyHoursCap(maxHoursPerDay=10)" },
        { """{"type":"NonBillableActivity","codes":["A101","L110"]}""", "NonBillableActivity(codes=[A101, L110])" },
        { """{"type":"ExpenseRule","nonReimbursableCodes":["E101"],"maxAmountPerItem":500}""", "ExpenseRule(nonReimbursableCodes=[E101], maxAmountPerItem=500)" },
        { """{"type":"ExpenseRule","maxAmountPerItem":500}""", "ExpenseRule(maxAmountPerItem=500)" },
        { """{"type":"InvoiceAge","maxAgeDays":90}""", "InvoiceAge(maxAgeDays=90)" },
        { """{"type":"CoreRecordWindow","graceDays":30}""", "CoreRecordWindow(graceDays=30)" },
        { """{"type":"Duplicate","matchFields":["firmInvoiceNumber","hours"]}""", "Duplicate(matchFields=[firmInvoiceNumber, hours])" },
        { """{"type":"RequiredData","fields":["billingPeriodStart","taskCode"]}""", "RequiredData(fields=[billingPeriodStart, taskCode])" },
        { """{"type":"AiObservation","observation":"blockBilling","minConfidence":0.85}""", "AiObservation(observation=blockBilling, minConfidence=0.85)" },
        { """{"type":"AddedFee","feeKind":"lateFee","allowance":"cap","feeCodes":["E120"],"capAmount":100}""", "AddedFee(feeKind=lateFee, allowance=cap, feeCodes=[E120], capAmount=100)" },
        { """{"type":"AddedFee","feeKind":"interest","allowance":"cap","feeCodes":["E120","E121"],"capPercent":2}""", "AddedFee(feeKind=interest, allowance=cap, feeCodes=[E120, E121], capPercent=2)" },
        { """{"type":"Discount","discountPercent":10}""", "Discount(discountPercent=10, tolerancePercent=0)" },
        { """{"type":"Discount","discountPercent":10,"tolerancePercent":1.5}""", "Discount(discountPercent=10, tolerancePercent=1.5)" },
    };

    private static RuleBodyDescriber Describer(Mock<IGenericEntityService>? entities = null) =>
        new(Validator(), (entities ?? new Mock<IGenericEntityService>(MockBehavior.Strict)).Object, NullLogger<RuleBodyDescriber>.Instance);

    [Theory]
    [MemberData(nameof(Descriptions))]
    public async Task Describer_DescribesEachBillingType_Deterministically_WithoutAnyRead(string body, string formal)
    {
        // Strict mock: any Dataverse read throws, so a passing test proves no lookup is read.
        var first = await Describer().DescribeAsync(body, default);
        var second = await Describer().DescribeAsync(body, default);

        first.IsRefused.Should().BeFalse(first.Refusal);
        var clause = first.Description!.Clauses.Should().ContainSingle().Subject;
        clause.Formal.Should().Be(formal);
        clause.Kind.Should().Be(formal[..formal.IndexOf('(')]);
        clause.Text.Should().NotBeNullOrWhiteSpace().And.EndWith(".");
        second.Description!.Clauses.Single().Should().Be(clause);
    }

    [Fact]
    public async Task Describer_CoreRecordWindow_SaysWhatTheHandlerDoes_D111()
    {
        var result = await Describer().DescribeAsync("""{"type":"CoreRecordWindow","graceDays":30}""", default);

        var text = result.Description!.Clauses.Single().Text;
        text.Should().Contain("after its close date").And.Contain("before the core record's open date");
        text.Should().Contain("billed later is not", "earlier work billed later is fine");
        text.Should().Contain("closed core record with no close date flags every charge line (credits excluded)");
        text.Should().NotContain("is not closed", "closure alone does not flag a line dated inside the window");
    }

    [Fact]
    public async Task Describer_CoreRecordWindow_SaysWhatEachBillingStatusDoes_AndExcludesCredits_D116_A8()
    {
        var result = await Describer().DescribeAsync("""{"type":"CoreRecordWindow","graceDays":30}""", default);

        var text = result.Description!.Clauses.Single().Text;
        text.Should().Contain("billing status").And.Contain("30 grace days");
        text.Should().Contain("Open: a line dated before the core record's open date is flagged; the close date and closed state are ignored.");
        text.Should().Contain("Final: also a line dated after its close date is flagged")
            .And.Contain("with no close date, every charge line (credits excluded) is flagged.");
        text.Should().Contain("Closed: every charge line (credits excluded) is flagged as closed to billing.");
        text.Should().Contain("credit lines are never flagged");
        text.Should().NotContain("every line", "every reason that flags all lines flags charge lines only (D-105)");
    }

    [Fact]
    public void TheCoreRecordWindowSchemaDescription_MatchesTheHandler_D116_A8()
    {
        using var schema = System.Text.Json.JsonDocument.Parse(BillingRuleCatalogue.SchemaText(RuleType.CoreRecordWindow));
        var description = schema.RootElement.GetProperty("description").GetString();

        description.Should().Contain("billing status").And.Contain("every charge line (credits excluded)")
            .And.Contain("closed to billing").And.Contain("credit lines are never flagged");
        description.Should().NotContain("every line.");
    }

    [Fact]
    public async Task Describer_ApprovedTimekeeper_SaysFirmLevel_BecauseTheRosterHasNoCoreRecordScope_D111()
    {
        var result = await Describer().DescribeAsync("""{"type":"ApprovedTimekeeper"}""", default);

        var text = result.Description!.Clauses.Single().Text;
        text.Should().Contain("approved for the firm on the line date");
        text.Should().NotContainEquivalentOf("core record");
    }

    [Theory]
    [InlineData("""{"type":"Bogus","subject":"sprk_matter","all":[]}""")]
    [InlineData("""{"type":"existence","subject":"sprk_matter","all":[]}""")]
    [InlineData("""{"type":"ApprovedRate,Threshold","tolerancePercent":5}""")]
    [InlineData("""{"type":100000004,"tolerancePercent":5}""")]
    [InlineData("""{"type":null,"subject":"sprk_matter","all":[]}""")]
    public async Task Describer_RefusesAnUnrecognisedType_NeverDescribesItAsExistence(string body)
    {
        var result = await Describer().DescribeAsync(body, default);

        result.IsRefused.Should().BeTrue();
        result.Refusal.Should().Contain(OntologyWriterFailureReason.RuleTypeUnsupported);
    }

    [Fact]
    public async Task Describer_RefusesAnInvalidBillingBody()
    {
        var result = await Describer().DescribeAsync("""{"type":"ApprovedRate","tolerancePercent":500}""", default);

        result.IsRefused.Should().BeTrue();
        result.Refusal.Should().Contain(OntologyWriterFailureReason.SchemaInvalid);
    }

    // ── helpers ─────────────────────────────────────────────────────────────────────────────────────────────────────

    private const string OntologyInvalidInstrument = "ontology.policy.invalid";
    private const string BillingInvalidInstrument = "billing.rule.invalid";

    private static MeterListener ListenPolicyInvalid(
        Guid scope, out Func<IReadOnlyList<string>> reasons, string instrumentName = OntologyInvalidInstrument)
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
