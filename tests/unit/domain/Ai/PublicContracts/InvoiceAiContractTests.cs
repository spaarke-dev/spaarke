using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Services.Ai.PublicContracts;
using Xunit;

namespace Sprk.Bff.Api.Tests.Domain.Ai.PublicContracts;

/// <summary>
/// ebilling-r1 task 041: the kill-switch peer and the FR-50 / FR-22 shape rules of the <see cref="IInvoiceAi"/>
/// capability contracts. The run-path behaviour is in <c>tests/integration/seam/Finance/InvoiceAiActionSeamTests.cs</c>.
/// </summary>
public sealed class InvoiceAiContractTests
{
    private static readonly HashSet<Type> NumericTypes =
    [
        typeof(byte), typeof(sbyte), typeof(short), typeof(ushort), typeof(int), typeof(uint),
        typeof(long), typeof(ulong), typeof(float), typeof(double), typeof(decimal),
    ];

    public delegate Task Capability(IInvoiceAi ai);

    public static TheoryData<string, Capability> Capabilities() => new()
    {
        { "ClassifyAsync", ai => ai.ClassifyAsync(new InvoiceClassifyRequest { DocumentText = "x" }) },
        {
            "CodeLinesAsync", ai => ai.CodeLinesAsync(new InvoiceCodeLinesRequest
            {
                Lines = [new InvoiceLineToCode { LineKey = "L1", LineType = InvoiceLineType.Fee }],
                AllowedTaskCodes = [],
                AllowedActivityCodes = [],
                AllowedExpenseCodes = [],
            })
        },
        {
            "DraftRulesAsync", ai => ai.DraftRulesAsync(new BillingRuleDraftRequest
            {
                DocumentText = "x",
                DocumentId = Guid.NewGuid(),
                RuleTypes = [new BillingRuleTypeOption("rate-cap", "d", "{}")],
            })
        },
        {
            "DraftFirmNoticeAsync", ai => ai.DraftFirmNoticeAsync(new InvoiceFirmNoticeRequest
            {
                Outcome = InvoiceFirmNoticeOutcome.SubmissionRejected,
                Findings = [],
            })
        },
    };

    // ADR-032 P3: with the compound AI gate off, every capability method fails fast with the facade's own
    // error code, so Finance / eBilling callers map it to 503 instead of reading an empty result.
    [Theory]
    [MemberData(nameof(Capabilities))]
    public async Task NullInvoiceAi_CapabilityMethod_ThrowsFeatureDisabledWithInvoiceErrorCode(string method, Capability call)
    {
        var sut = new NullInvoiceAi(NullLogger<NullInvoiceAi>.Instance);

        var ex = await FluentActions.Awaiting(() => call(sut)).Should().ThrowAsync<FeatureDisabledException>(method);

        ex.Which.ErrorCode.Should().Be("ai.invoice.disabled");
    }

    // FR-50: amounts, counts and line references are inserted by the notice template from data, so no number may
    // be produced by the notice model (result) or reach it (request). Task 131 builds on this guarantee.
    [Theory]
    [InlineData(typeof(InvoiceFirmNoticeDraft))]
    [InlineData(typeof(InvoiceFirmNoticeRequest))]
    [InlineData(typeof(InvoiceFirmNoticeFinding))]
    public void FirmNoticeContract_Type_HasNoNumericProperty(Type type)
    {
        type.GetProperties()
            .Where(p => NumericTypes.Contains(Nullable.GetUnderlyingType(p.PropertyType) ?? p.PropertyType))
            .Select(p => p.Name)
            .Should().BeEmpty();
    }

    // FR-22: the coding result carries codes, confidence and observations only; amounts stay with the
    // extracted line and can never be overwritten by the coding model.
    [Fact]
    public void CodeLinesResult_LineCodingAndObservation_CarryOnlyCodesConfidenceAndObservations()
    {
        typeof(InvoiceLineCoding).GetProperties().Select(p => p.Name).Should().BeEquivalentTo(
            "LineKey", "TaskCode", "ActivityCode", "ExpenseCode", "Confidence", "Observations");
        typeof(InvoiceLineObservation).GetProperties().Select(p => p.Name).Should().BeEquivalentTo(
            "Type", "Confidence", "QuotedText");
    }
}
