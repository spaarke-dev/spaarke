using FluentAssertions;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.RecordCreation;
using Xunit;

namespace Sprk.Bff.Api.Tests.Seam.RecordCreation;

/// <summary>
/// Task 067 (owner 2026-11-27): a mapped value from the filing parent is copied whatever the caller can read, but a mapping
/// never WRITES an invoice's firm, its firm hold or an amount, nor a user's own contact. As a create's mapping TARGET each is a
/// BFF-owned invariant column (<see cref="RecordCreationService.IsCopyInvariantColumn"/>): the rule is dropped with a warning
/// naming the column, and the other rules still fill. Must fire: each column. Must not fire: an ordinary invoice column.
/// </summary>
public sealed partial class RecordCreationServiceMappingSeamTests
{
    public static TheoryData<string> InvoiceColumnsAMappingNeverWrites() => new()
    {
        "sprk_vendororg",
        "sprk_firmhold",
        "sprk_totalamount",
    };

    [Theory]
    [MemberData(nameof(InvoiceColumnsAMappingNeverWrites))]
    public async Task CreateAsync_AnInvoiceRuleTargetingItsFirmHoldOrAmount_IsDroppedWithAWarning_AndAnOrdinaryRuleIsFilled(string column)
    {
        _user.Columns["sprk_invoice"][column] = ("String", true);
        Profile("sprk_matter", "sprk_invoice",
            Rule(null, column, Text, Default, "set-by-a-mapping"),
            Rule("sprk_matterdescription", "sprk_description", Memo));

        var outcome = await Service().CreateAsync(
            Request("sprk_invoice", new Dictionary<string, object?>
            {
                ["sprk_invoicenumber"] = "INV-1",
                ["sprk_Matter@odata.bind"] = $"/sprk_matters({OrdinaryMatter:D})",
            }, mapping: true),
            AsCaller(), CancellationToken.None);

        outcome.Succeeded.Should().BeTrue(outcome.Refusal?.Detail);
        var fields = _creates.Should().ContainSingle().Subject.Fields;
        fields.Keys.Should().NotContain(k => k.Contains(column, StringComparison.OrdinalIgnoreCase), $"{column} is never a mapping's to write (must fire)");
        fields.Values.Select(v => v?.ToString()).Should().NotContain("set-by-a-mapping");
        StringOf(fields, "sprk_description").Should().Be("From the matter", "an ordinary invoice column is still filled (must not fire)");
        outcome.MappingWarnings.Should().Contain(w => w.Code == RecordCreationWarning.MappingRuleSkippedCode && w.Column == column);
    }

    [Theory]
    [InlineData("sprk_invoice", "sprk_vendororg")]
    [InlineData("sprk_invoice", " SPRK_FirmHold ")]
    [InlineData("sprk_invoice", "sprk_totalamount")]
    [InlineData("systemuser", "sprk_primarycontact")]
    public void IsCopyInvariantColumn_TheInvoicesFirmHoldAndAmount_AndAUsersOwnContact(string table, string column)
    {
        RecordCreationService.IsCopyInvariantColumn(table, column).Should().BeTrue($"{column} of a {table} is the BFF's own (must fire)");
        InheritedAccessPermission.RefuseAppOnlyWrite(table, [column]).Should().NotBeNull();
    }

    [Theory]
    [InlineData("sprk_invoice", "sprk_description")]
    [InlineData("sprk_invoice", "sprk_invoicenumber")]
    [InlineData("sprk_billingevent", "sprk_vendororg")] // a line's firm is not the invoice's
    [InlineData("sprk_matter", "sprk_totalamount")]
    public void IsCopyInvariantColumn_AnOrdinaryColumn_OrTheSameNameOnAnotherTable_IsNot(string table, string column) =>
        RecordCreationService.IsCopyInvariantColumn(table, column).Should().BeFalse($"{column} of a {table} (must not fire)");
}
