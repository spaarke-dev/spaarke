using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Sprk.Bff.Api.Api;
using Xunit;
using Directory = Sprk.Bff.Api.Tests.TestInfrastructure.OwnershipDirectory;

namespace Sprk.Bff.Api.Tests.Integration.DataMutation.RecordOwnership;

/// <summary>
/// spaarke-ontology-platform-r1 task 136: the Create Analysis wizard's Finish (<c>CreateAnalysisWizardWidget.onFinish</c> →
/// <c>POST /api/v1/child-records/sprk_analysis</c>), driven through the REAL route handler and G5 core with the payloads the
/// wizard sends. Owner checklist r1 (spaarkedev1, 2026-10-08T14:07Z): every Finish answered 404 "A record this analysis is
/// filed under was not found" — the wizard always binds an Agreement Type (it pre-selects the registry's fallback row), and
/// <c>sprk_agreementtype</c> is ORGANIZATION-OWNED, so the AppendTo question (RetrievePrincipalAccess) was refused with 400
/// and read as "no AppendTo". Every entry point (hub card, record ribbon, Console record context, chat) reaches this one
/// Finish, so these payloads stand for all of them.
/// </summary>
public sealed partial class SecureChildOwnershipAiToolTests
{
    private static readonly Guid WizardAttorney = Guid.Parse("a1360000-0000-4000-8000-0000000000c1");
    private static readonly Guid AgreementTypeNda = Guid.Parse("a1360000-0000-4000-8000-0000000000a1");
    private static readonly Guid RecordTypeOfMatter = Guid.Parse("a1360000-0000-4000-8000-0000000000e1");

    /// <summary>
    /// The payload the wizard builds before any Associate-To (Step 1 skipped — the hub canary). Its document lookup is an
    /// ownership parent, so even this analysis is filed under the document it reviews (the resolver's own definition).
    /// </summary>
    private static Dictionary<string, object?> WizardAnalysisPayload() => new()
    {
        ["sprk_name"] = "NDA review",
        ["sprk_description"] = null,
        ["sprk_worktype"] = 100000000,
        ["sprk_analysisstatus"] = 100000001,
        ["sprk_documentid@odata.bind"] = $"/sprk_documents({OrdinaryDocument:D})",
        ["sprk_AssignedAttorney1@odata.bind"] = $"/contacts({WizardAttorney:D})",
        ["sprk_AgreementType@odata.bind"] = $"/sprk_agreementtypes({AgreementTypeNda:D})",
    };

    [Fact]
    public async Task ChildCreate_TheCreateAnalysisWizardsHubPayload_NoAssociation_WithAnAgreementType_IsCreated()
    {
        var result = await CreateChild("sprk_analysis", WizardAnalysisPayload());

        Status(result).Should().Be(StatusCodes.Status201Created, Detail(result));
        var (table, _, fields) = _appCreates.Should().ContainSingle().Subject;
        table.Should().Be("sprk_analysis");
        Owner(fields).Should().Be(Directory.ChildTeam, "the document's business-unit team");
        Bind(fields, "sprk_AgreementType@odata.bind").Should().Be($"/sprk_agreementtypes({AgreementTypeNda:D})");
        Bind(fields, "sprk_documentid@odata.bind").Should().Be($"/sprk_documents({OrdinaryDocument:D})");
    }

    [Fact]
    public async Task ChildCreate_TheCreateAnalysisWizardsPayload_FiledUnderAMatter_WithItsResolverFields_IsCreated()
    {
        // Launched from a matter (ribbon / Console record context): applyResolverFields adds the regarding lookup, the ADR-024
        // record-type lookup (also organization-owned) and the resolver text fields.
        var payload = WizardAnalysisPayload();
        payload["sprk_RegardingMatter@odata.bind"] = $"/sprk_matters({OrdinaryMatter:D})";
        payload["sprk_RegardingRecordType@odata.bind"] = $"/sprk_recordtype_refs({RecordTypeOfMatter:D})";
        payload["sprk_regardingrecordid"] = OrdinaryMatter.ToString("D");
        payload["sprk_regardingrecordname"] = "Acme v. Example";

        var result = await CreateChild("sprk_analysis", payload);

        Status(result).Should().Be(StatusCodes.Status201Created, Detail(result));
        var fields = _appCreates.Should().ContainSingle().Subject.Fields;
        Owner(fields).Should().Be(Directory.ChildTeam, "the ordinary matter's business-unit team");
        Bind(fields, "sprk_RegardingRecordType@odata.bind").Should().Be($"/sprk_recordtype_refs({RecordTypeOfMatter:D})");
        Bind(fields, "sprk_AgreementType@odata.bind").Should().Be($"/sprk_agreementtypes({AgreementTypeNda:D})");
    }

    [Fact]
    public async Task ChildCreate_TheCreateAnalysisWizardsPayload_WithoutAppendToOnAgreementTypes_IsTheUniform404_AndNothingIsCreated()
    {
        // Negative case: the organization-owned path still asks the caller's own AppendTo privilege, so the fix grants
        // nothing a run-as-user create would have refused.
        _user.Held.Remove("prvAppendTosprk_agreementtype");

        var result = await CreateChild("sprk_analysis", WizardAnalysisPayload());

        Status(result).Should().Be(StatusCodes.Status404NotFound);
        ReasonCode(result).Should().Be(ChildRecordEndpoints.NotFoundCode);
        Detail(result).Should().Be("A record this analysis is filed under was not found. The analysis was not saved.");
        _appCreates.Should().BeEmpty();
    }
}
