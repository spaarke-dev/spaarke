// -----------------------------------------------------------------------------
// CustomerWorkforceTenantsRuleTests.cs — task 255 (INCOMING-141 from unified-access-control-r2 task 141).
//
// The ONE rule POST /api/runs, H4b and H13 apply to intake `customerWorkforceTenantIds`, and the L2-owned
// ReservedTenantsOptions it reads. The endpoint wiring is RunsEndpointsTests; H4b's guard is
// H4bBulkAppSettingsHandlerTests AC-21. Negative cases first: each value the BFF would refuse at startup, and each
// that would admit the wrong people, is refused with its own code.
// -----------------------------------------------------------------------------

using FluentAssertions;
using Sprk.Provisioning.ControlPlane.Core.Models;
using Xunit;

namespace Sprk.Provisioning.ControlPlane.Tests.Models;

public sealed class CustomerWorkforceTenantsRuleTests
{
    private const string Spaarke = "5a5a5a5a-0000-4000-8000-000000000001";
    private const string Ciam = "c1a0c1a0-0000-4000-8000-000000000002";
    private const string CustomerA = "d0e0c0a0-0000-4000-8000-000000000003";
    private const string CustomerB = "e1e1e1e1-0000-4000-8000-000000000004";
    private const string RunTenant = "11111111-1111-1111-1111-111111111111";

    private static readonly ReservedTenants Reserved = new(Guid.Parse(Spaarke), new HashSet<Guid> { Guid.Parse(Ciam) });

    [Theory]
    [InlineData(null, CustomerWorkforceTenantsRule.RequiredRejectionCode)]
    [InlineData("", CustomerWorkforceTenantsRule.RequiredRejectionCode)]
    [InlineData("   ", CustomerWorkforceTenantsRule.RequiredRejectionCode)]
    [InlineData("[]", CustomerWorkforceTenantsRule.InvalidRejectionCode)]
    [InlineData("{}", CustomerWorkforceTenantsRule.InvalidRejectionCode)]
    [InlineData("\"" + CustomerA + "\"", CustomerWorkforceTenantsRule.InvalidRejectionCode)]   // a JSON string, not an array
    [InlineData(CustomerA, CustomerWorkforceTenantsRule.InvalidRejectionCode)]                 // not JSON at all
    [InlineData("[\"" + CustomerA + "\"", CustomerWorkforceTenantsRule.InvalidRejectionCode)]  // truncated
    [InlineData("[null]", CustomerWorkforceTenantsRule.InvalidRejectionCode)]
    [InlineData("[[\"" + CustomerA + "\"]]", CustomerWorkforceTenantsRule.InvalidRejectionCode)]   // nested
    [InlineData("[\"00000000-0000-0000-0000-000000000000\"]", CustomerWorkforceTenantsRule.InvalidRejectionCode)]
    [InlineData("[\"" + CustomerA + "\",\"{" + CustomerA + "}\"]", CustomerWorkforceTenantsRule.InvalidRejectionCode)]   // duplicate, other spelling
    [InlineData("[\"" + Ciam + "\"]", CustomerWorkforceTenantsRule.CiamTenantRejectionCode)]
    [InlineData("[\"" + CustomerA + "\",\"" + Ciam + "\"]", CustomerWorkforceTenantsRule.CiamTenantRejectionCode)]
    [InlineData("[\"" + Spaarke + "\"]", CustomerWorkforceTenantsRule.SpaarkeTenantRejectionCode)]
    [InlineData("[\"" + RunTenant + "\"]", CustomerWorkforceTenantsRule.SpaarkeTenantRejectionCode)]   // Model 1: the run's tenantId
    public void Model1_Refuses(string? value, string expectedCode)
    {
        var outcome = CustomerWorkforceTenantsRule.Validate("Model1", RunTenant, value, Reserved);

        outcome.Should().BeOfType<CustomerWorkforceTenantsOutcome.Invalid>().Which.RejectionCode.Should().Be(expectedCode);
    }

    [Fact]
    public void Refuses_MoreThanTheCap_AndAnOverlongValue()
    {
        var eleven = "[" + string.Join(",", Enumerable.Range(1, CustomerWorkforceTenantsRule.MaxTenants + 1)
            .Select(i => $"\"{new Guid(i, 0, 0, new byte[8]):D}\"")) + "]";
        CustomerWorkforceTenantsRule.Validate("Model1", RunTenant, eleven, Reserved)
            .Should().BeOfType<CustomerWorkforceTenantsOutcome.Invalid>()
            .Which.RejectionCode.Should().Be(CustomerWorkforceTenantsRule.InvalidRejectionCode);

        var overlong = "[\"" + CustomerA + "\"" + new string(' ', CustomerWorkforceTenantsRule.MaxValueLength) + "]";
        CustomerWorkforceTenantsRule.Validate("Model1", RunTenant, overlong, Reserved)
            .Should().BeOfType<CustomerWorkforceTenantsOutcome.Invalid>()
            .Which.Diagnostic.Should().Contain("longer than");
    }

    [Fact]
    public void Model1_AcceptsTheCustomersTenants_StoredCanonicalInOrder()
    {
        var outcome = CustomerWorkforceTenantsRule.Validate(
            "Model1", RunTenant, " [ \"{" + CustomerB.ToUpperInvariant() + "}\" , \"" + CustomerA + "\" ] ", Reserved);

        var valid = outcome.Should().BeOfType<CustomerWorkforceTenantsOutcome.Valid>().Subject;
        valid.TenantIds.Should().Equal(CustomerB, CustomerA);
        valid.CanonicalValue.Should().Be("[\"" + CustomerB + "\",\"" + CustomerA + "\"]");
        CustomerWorkforceTenantsRule.ParseStored(valid.CanonicalValue).Should().Equal(CustomerB, CustomerA);
    }

    [Fact]
    public void Model2_MayListTheRunsTenant_ButNeverSpaarkesOrTheCiamTenant()
    {
        // Model 2: the registration lives in the customer's tenant, so the two coincide (hand-off §3).
        CustomerWorkforceTenantsRule.Validate("Model2", RunTenant, "[\"" + RunTenant + "\"]", Reserved)
            .Should().BeOfType<CustomerWorkforceTenantsOutcome.Valid>();
        CustomerWorkforceTenantsRule.Validate("Model2", RunTenant, "[\"" + Spaarke + "\"]", Reserved)
            .Should().BeOfType<CustomerWorkforceTenantsOutcome.Invalid>()
            .Which.RejectionCode.Should().Be(CustomerWorkforceTenantsRule.SpaarkeTenantRejectionCode);
        CustomerWorkforceTenantsRule.Validate("Model2", RunTenant, "[\"" + Ciam + "\"]", Reserved)
            .Should().BeOfType<CustomerWorkforceTenantsOutcome.Invalid>()
            .Which.RejectionCode.Should().Be(CustomerWorkforceTenantsRule.CiamTenantRejectionCode);
    }

    [Fact]
    public void Diagnostic_EchoesAtMostAShortPrefix_WithControlCharactersReplaced()
    {
        var outcome = CustomerWorkforceTenantsRule.Validate("Model1", RunTenant, "\u0007" + new string('x', 200), Reserved);

        var invalid = outcome.Should().BeOfType<CustomerWorkforceTenantsOutcome.Invalid>().Subject;
        invalid.Diagnostic.Should().NotContain("\u0007").And.NotContain(new string('x', 65));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("[\"not-a-guid\"]")]
    public void ParseStored_ReturnsNullForAnAbsentOrMalformedValue(string? value)
        => CustomerWorkforceTenantsRule.ParseStored(value).Should().BeNull();

    // ---------- ReservedTenantsOptions (validated at Api and Worker start) ----------

    [Fact]
    public void ReservedTenantsOptions_Valid_ParsesBothKinds()
    {
        var options = new ReservedTenantsOptions { SpaarkeTenantId = $" {Spaarke} ", CiamTenantIds = [Ciam] };

        options.Validate();
        options.Parsed().Should().BeEquivalentTo(Reserved);
    }

    [Theory]
    [InlineData("", Ciam, "SpaarkeTenantId")]
    [InlineData("00000000-0000-0000-0000-000000000000", Ciam, "SpaarkeTenantId")]
    [InlineData(Spaarke, null, "CiamTenantIds must list")]
    [InlineData(Spaarke, "not-a-guid", "CiamTenantIds:0")]
    [InlineData(Spaarke, Spaarke, "equals ReservedTenants:SpaarkeTenantId")]
    public void ReservedTenantsOptions_Invalid_FailsStartupNamingTheSetting(string spaarke, string? ciam, string expected)
    {
        var options = new ReservedTenantsOptions { SpaarkeTenantId = spaarke, CiamTenantIds = ciam is null ? [] : [ciam] };

        var validate = () => options.Validate();

        validate.Should().Throw<InvalidOperationException>().WithMessage($"*{expected}*");
    }
}
