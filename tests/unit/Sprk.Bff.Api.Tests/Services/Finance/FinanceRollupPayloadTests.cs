using System.Text.Json;
using FluentAssertions;
using Microsoft.Xrm.Sdk;
using Sprk.Bff.Api.Services.Finance;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Finance;

/// <summary>
/// Regression guard on the Finance rollup write payload.
/// </summary>
/// <remarks>
/// <para>
/// <b>The defect this pins.</b> <c>FinanceRollupService</c> writes derived financial fields via
/// <c>IFieldMappingDataverseService.UpdateRecordFieldsAsync</c>, which PATCHes the dictionary to the
/// Dataverse <b>Web API</b>. The Web API takes currency as a bare number; the SDK Entity model takes a
/// <see cref="Money"/> wrapper. The service was written against the Entity model and was left behind when
/// commit <c>b7b0d4011</c> (2026-03-03) converted the implementation to OData PATCH, so five currency
/// fields shipped as <c>Money</c> objects and every recalculate failed at the write with HTTP 400.
/// </para>
/// <para>
/// This is a MAINTAIN-class test per ADR-038 §7: it guards a live write contract with a real, shipped
/// failure mode, and it runs with no Dataverse connection.
/// </para>
/// </remarks>
public class FinanceRollupPayloadTests
{
    private static Dictionary<string, object?> Build() =>
        FinanceRollupService.BuildRollupFields(
            totalSpend: 1234.56m,
            invoiceCount: 3,
            currentMonthSpend: 200.00m,
            totalBudget: 5000m,
            remainingBudget: 3765.44m,
            utilization: 24.6912m,
            velocity: -12.5m,
            averageInvoice: 411.52m,
            timelineJson: """[{"month":"2026-08","spend":200.00}]""");

    [Fact(DisplayName = "Rollup payload carries NO SDK Entity-model wrappers (Money/OptionSetValue/EntityReference)")]
    public void BuildRollupFields_UsesWebApiPrimitives_NotEntityModelWrappers()
    {
        var fields = Build();

        fields.Should().NotBeEmpty();
        fields.Values.Should().NotContain(v => v is Money, "the Web API PATCH takes currency as a bare number, not a Money wrapper");
        fields.Values.Should().NotContain(v => v is OptionSetValue);
        fields.Values.Should().NotContain(v => v is EntityReference);
    }

    [Fact(DisplayName = "Every rollup value serializes to a JSON scalar — never an object")]
    public void BuildRollupFields_SerializesToScalars()
    {
        var json = JsonSerializer.Serialize(Build());

        using var doc = JsonDocument.Parse(json);
        foreach (var property in doc.RootElement.EnumerateObject())
        {
            property.Value.ValueKind.Should().BeOneOf(
                new[] { JsonValueKind.Number, JsonValueKind.String, JsonValueKind.True, JsonValueKind.False, JsonValueKind.Null },
                $"Dataverse rejects a non-scalar value for '{property.Name}' with HTTP 400");
        }
    }

    [Fact(DisplayName = "Negative control: a Money-wrapped currency value WOULD serialize to an object")]
    public void Money_SerializesToObject_ProvingTheGuardIsNotVacuous()
    {
        var json = JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["sprk_totalspendtodate"] = new Money(1234.56m)
        });

        using var doc = JsonDocument.Parse(json);
        doc.RootElement.GetProperty("sprk_totalspendtodate").ValueKind
            .Should().Be(JsonValueKind.Object, "this is the exact shape Dataverse rejected — the guard above must keep it out");
    }

    [Fact(DisplayName = "Currency fields carry their computed decimal values")]
    public void BuildRollupFields_PreservesComputedValues()
    {
        var fields = Build();

        fields["sprk_totalspendtodate"].Should().Be(1234.56m);
        fields["sprk_invoicecount"].Should().Be(3);
        fields["sprk_remainingbudget"].Should().Be(3765.44m);
        fields["sprk_monthovermonthvelocity"].Should().Be(-12.5m);
    }
}
