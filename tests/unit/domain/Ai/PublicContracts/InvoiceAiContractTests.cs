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
        {
            // ebilling-r1 task 043: reading is behind the same compound kill switch.
            "ReadInvoiceAsync", ai => ai.ReadInvoiceAsync(new InvoiceReadRequest
            {
                Content = new byte[] { 1 },
                DocumentId = Guid.NewGuid(),
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
    // be produced by the notice model (result) or reach it (request). Task 131 builds on this guarantee. The walk
    // recurses into generic type arguments (IReadOnlyList<decimal>, arrays, nullable) and into nested records from
    // the PublicContracts namespace, so a numeric member anywhere under these types fails the test.
    [Theory]
    [InlineData(typeof(InvoiceFirmNoticeDraft))]
    [InlineData(typeof(InvoiceFirmNoticeRequest))]
    [InlineData(typeof(InvoiceFirmNoticeFinding))]
    public void FirmNoticeContract_Type_HasNoNumericPropertyAnywhere(Type type)
    {
        NumericPaths(type, type.Name, new HashSet<Type>()).ToList().Should().BeEmpty();
    }

    // Must-fire control: the walk really does see numbers behind a generic argument, an array, a nullable and a
    // nested PublicContracts record. Without this a walk that silently stops at the first level would pass.
    [Theory]
    [InlineData(typeof(ProbeDirect), "ProbeDirect.Count")]
    [InlineData(typeof(ProbeGeneric), "ProbeGeneric.Amounts")]
    [InlineData(typeof(ProbeArray), "ProbeArray.Amounts")]
    [InlineData(typeof(ProbeNullable), "ProbeNullable.Count")]
    [InlineData(typeof(ProbeNested), "ProbeNested.Items.Inner.Total")]
    public void NumericPaths_FindsANumericMemberBehindGenericsArraysNullablesAndNestedRecords(Type type, string expectedPath)
    {
        NumericPaths(type, type.Name, new HashSet<Type>()).ToList().Should().Contain(expectedPath);
    }

    private static IEnumerable<string> NumericPaths(Type type, string path, HashSet<Type> visited)
    {
        if (!visited.Add(type))
        {
            yield break;
        }

        foreach (var property in type.GetProperties())
        {
            var propertyPath = $"{path}.{property.Name}";
            foreach (var found in NumericPathsOfType(property.PropertyType, propertyPath, visited))
            {
                yield return found;
            }
        }
    }

    private static IEnumerable<string> NumericPathsOfType(Type type, string path, HashSet<Type> visited)
    {
        type = Nullable.GetUnderlyingType(type) ?? type;
        if (NumericTypes.Contains(type))
        {
            yield return path;
            yield break;
        }

        if (type.IsArray)
        {
            foreach (var found in NumericPathsOfType(type.GetElementType()!, path, visited))
            {
                yield return found;
            }

            yield break;
        }

        if (type.IsGenericType)
        {
            foreach (var argument in type.GetGenericArguments())
            {
                foreach (var found in NumericPathsOfType(argument, path, visited))
                {
                    yield return found;
                }
            }

            yield break;
        }

        if (IsContractRecord(type))
        {
            foreach (var found in NumericPaths(type, path, visited))
            {
                yield return found;
            }
        }
    }

    // A nested record from the PublicContracts namespace (or one of this class's own probe records, which stand in
    // for one in the must-fire control).
    private static bool IsContractRecord(Type type) =>
        !type.IsEnum
        && (type.Namespace == typeof(IInvoiceAi).Namespace || type.DeclaringType == typeof(InvoiceAiContractTests));

    private sealed record ProbeDirect { public int Count { get; init; } }

    private sealed record ProbeGeneric { public IReadOnlyList<decimal> Amounts { get; init; } = []; }

    private sealed record ProbeArray { public int[] Amounts { get; init; } = []; }

    private sealed record ProbeNullable { public long? Count { get; init; } }

    private sealed record ProbeNested { public IReadOnlyList<ProbeItem> Items { get; init; } = []; }

    private sealed record ProbeItem { public ProbeInner Inner { get; init; } = new(); }

    private sealed record ProbeInner { public double Total { get; init; } }

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
