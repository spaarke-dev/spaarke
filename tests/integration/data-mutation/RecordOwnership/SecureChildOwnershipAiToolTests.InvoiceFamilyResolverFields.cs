using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Services.Dataverse;
using Xunit;
using Directory = Sprk.Bff.Api.Tests.TestInfrastructure.OwnershipDirectory;

namespace Sprk.Bff.Api.Tests.Integration.DataMutation.RecordOwnership;

/// <summary>
/// field-mapping-server-write-path-r1 task 045d (owner ruling Q8 (a)): a caller update that changes an invoice-family row's core
/// lookup writes, in the same PATCH, the five resolver fields of the record it now names — derived by the server as the
/// caller, all five or none. A derive that cannot complete them refuses through the route's existing invoice-family mapping
/// (<c>ChildRecordEndpoints.InvoiceFamilyRefused</c>), so the row is never re-filed without them. An update that touches no
/// core lookup reads nothing more and writes none of the five. Driven through the REAL handlers and core over the scripted
/// caller's Dataverse. Task 045g (owner, EB-D-231): an invoice's core record is fixed at create, so the derive is driven on an
/// approval through the chat tool; an invoice move is refused on both caller paths and derives nothing.
/// </summary>
public sealed partial class SecureChildOwnershipAiToolTests
{
    private static readonly Guid RederiveMatter = Guid.Parse("a045d000-0000-4000-8000-0000000000a1");

    private static readonly string[] ResolverColumns =
    [
        "sprk_regardingrecordid", "sprk_regardingrecordname", "sprk_regardingrecordurl", "sprk_regardingrecordnumber",
    ];

    /// <summary>The five, as the patched body carries them for <paramref name="core"/> (the scripted name and number).</summary>
    private static void ShouldCarryTheFiveOf(Dictionary<string, JsonElement> body, string core, Guid id, string typeNavigation)
    {
        var lowerId = id.ToString("D").ToLowerInvariant();
        body[$"{typeNavigation}@odata.bind"].GetString().Should()
            .Be($"/sprk_recordtype_refs({ScriptedUserClient.CatalogTypeIdOf(core):D})");
        body["sprk_regardingrecordid"].GetString().Should().Be(lowerId);
        body["sprk_regardingrecordname"].GetString().Should().Be($"Record {id.ToString("N")[..6]}");
        body["sprk_regardingrecordurl"].GetString().Should().Be($"/main.aspx?pagetype=entityrecord&etn={core}&id={lowerId}");
        body["sprk_regardingrecordnumber"].GetString().Should().Be($"N-{id.ToString("N")[..6]}");
    }

    private void InvoiceOnTheMatter_MovingToTheProject()
    {
        StoredInvoice(GuardInvoice, "sprk_matter", OrdinaryMatter);
        _world.WithRecord("sprk_project", OrdinaryProject, Directory.ChildBu, owningTeam: Directory.ChildTeam);
        ShareWorld.OrdinaryChild("sprk_invoice", GuardInvoice, ("sprk_matter", "sprk_matter", OrdinaryMatter));
        AppLine(GuardInvoice, "sprk_project", "sprk_project", OrdinaryProject);
    }

    /// <summary>Task 045g: the derive's subject on the chat tool — an approval under the ordinary matter with no invoice.</summary>
    private void ApprovalOnTheMatter_MovingToTheProject()
    {
        StoredFamilyRowUnderTheMatter("sprk_invoiceapproval", "sprk_regardingmatter", PairFamilyRow);
        _user.RecordTypes.Remove(PairProjectType); // the catalog's own project row, as the invoice tests read it
        _world.WithRecord("sprk_project", OrdinaryProject, Directory.ChildBu, owningTeam: Directory.ChildTeam);
    }

    private Task<Sprk.Bff.Api.Services.Ai.ToolResult> MoveTheApprovalToTheProject() =>
        UpdateRecord("sprk_invoiceapproval", PairFamilyRow,
            ("sprk_regardingmatter", JsonSerializer.SerializeToElement<object?>(null)),
            Lookup("sprk_regardingproject", "sprk_project", OrdinaryProject));

    // ── Task 045g rule 1: an invoice's core record never moves, so nothing is derived for it on either caller path ──────

    [Theory]
    [InlineData("to a project")]
    [InlineData("to another matter")]
    public async Task ChildRefile_AnInvoiceMoved_Is400CoreRecordFixed_ReadsNoCatalog_AndNothingIsPatched(string move)
    {
        InvoiceOnTheMatter_MovingToTheProject();
        _world.WithRecord("sprk_matter", RederiveMatter, Directory.ChildBu, owningTeam: Directory.ChildTeam);
        var reads = new List<string>();
        _user.GetOverride = path =>
        {
            reads.Add(path);
            return null;
        };

        var result = await RefileChild("sprk_invoice", GuardInvoice, move == "to a project"
            ? MoveGuardInvoiceToTheProject()
            : new() { ["sprk_Matter@odata.bind"] = $"/sprk_matters({RederiveMatter:D})" });

        Status(result).Should().Be(StatusCodes.Status400BadRequest, Detail(result));
        ReasonCode(result).Should().Be(InvoiceFamilyCoreRecordGuard.InvoiceCoreRecordFixedCode);
        Detail(result).Should().Contain(InvoiceFamilyCoreRecordGuard.ReturnAndResubmitHint).And.NotContain(OrdinaryMatter.ToString("D"));
        reads.Should().NotContain(p => p.StartsWith("sprk_recordtype_refs?", StringComparison.Ordinal), "nothing is derived for an invoice");
        _user.Patches.Should().BeEmpty();
        _world.Assignments.Should().BeEmpty();
    }

    [Fact]
    public async Task UpdateRecordTool_AnInvoiceMovedToAProject_IsRefusedCoreRecordFixed_AndNothingIsPatched()
    {
        InvoiceOnTheMatter_MovingToTheProject();

        var result = await UpdateRecord("sprk_invoice", GuardInvoice,
            ("sprk_matter", JsonSerializer.SerializeToElement<object?>(null)), Lookup("sprk_project", "sprk_project", OrdinaryProject));

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(InvoiceFamilyCoreRecordGuard.InvoiceCoreRecordFixedCode, result.ErrorMessage);
        _user.Patches.Should().BeEmpty();
    }

    // ── Must fire: the core lookup changes, the five follow it in the same PATCH ────────────────────────────────────

    [Fact]
    public async Task UpdateRecordTool_AnApprovalMovedToAProject_IsPatchedWithTheProjectsFiveResolverFields()
    {
        ApprovalOnTheMatter_MovingToTheProject();

        var result = await MoveTheApprovalToTheProject();

        result.Success.Should().BeTrue(result.ErrorMessage);
        ShouldCarryTheFiveOf(BodyOf(_user.Patches.Should().ContainSingle().Subject.Body), "sprk_project", OrdinaryProject, "sprk_RegardingRecordType");
    }

    // ── Fail closed: a derive that cannot complete the five refuses through the existing mapping ───────────────────

    [Fact]
    public async Task UpdateRecordTool_WhenTheProjectHasNoCatalogRow_IsRefusedResolverFieldsIncomplete()
    {
        ApprovalOnTheMatter_MovingToTheProject();
        _user.GetOverride = path => path.StartsWith("sprk_recordtype_refs?", StringComparison.Ordinal)
            ? DataverseUserResponse.Ok(200, JsonSerializer.SerializeToElement(new { value = Array.Empty<object>() }))
            : null;

        var result = await MoveTheApprovalToTheProject();

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(InvoiceFamilyCoreRecordGuard.ResolverFieldsIncompleteCode);
        _user.Patches.Should().BeEmpty();
    }

    [Fact]
    public async Task UpdateRecordTool_WhenTheCatalogReadFails_IsRefusedReadFailed_AndNothingIsPatched()
    {
        ApprovalOnTheMatter_MovingToTheProject();
        _user.GetOverride = path => path.StartsWith("sprk_recordtype_refs?", StringComparison.Ordinal)
            ? DataverseUserResponse.Fail(503, DataverseUserClientErrorCodes.BadRequest, "Service unavailable.")
            : null;

        var result = await MoveTheApprovalToTheProject();

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(InvoiceFamilyCoreRecordGuard.ReadFailedCode);
        _user.Patches.Should().BeEmpty();
    }

    [Fact]
    public async Task UpdateRecordTool_WhenTheProjectCannotBeReadForItsName_IsRefused_AndNothingIsPatched()
    {
        ApprovalOnTheMatter_MovingToTheProject();
        _user.GetOverride = path => path.StartsWith($"sprk_projects({OrdinaryProject:D})?$select=", StringComparison.Ordinal)
                                    && path.Contains("sprk_projectname", StringComparison.Ordinal)
            ? DataverseUserResponse.Fail(404, DataverseUserClientErrorCodes.BadRequest, "Not found.")
            : null;

        var result = await MoveTheApprovalToTheProject();

        result.Success.Should().BeFalse();
        (result.ErrorMessage ?? string.Empty).Should().NotContain(OrdinaryProject.ToString("D"));
        _user.Patches.Should().BeEmpty();
    }

    // ── Must not fire: an update that leaves every core lookup as stored ───────────────────────────────────────────

    [Fact]
    public async Task ChildRefile_AnInvoiceRenamedOnly_ReadsNoCatalog_AndWritesNoneOfTheFive()
    {
        StoredInvoice(GuardInvoice, "sprk_matter", OrdinaryMatter);
        ShareWorld.OrdinaryChild("sprk_invoice", GuardInvoice, ("sprk_matter", "sprk_matter", OrdinaryMatter));
        var reads = new List<string>();
        _user.GetOverride = path =>
        {
            reads.Add(path);
            return null;
        };

        var result = await RefileChild("sprk_invoice", GuardInvoice, new() { ["sprk_name"] = "Renamed" });

        Status(result).Should().Be(StatusCodes.Status204NoContent, Detail(result));
        reads.Should().NotContain(p => p.StartsWith("sprk_recordtype_refs?", StringComparison.Ordinal));
        reads.Should().NotContain(p => p.Contains("sprk_mattername", StringComparison.Ordinal));
        var body = BodyOf(_user.Patches.Should().ContainSingle().Subject.Body);
        body.Keys.Should().NotContain(ResolverColumns).And.NotContain("sprk_regardingrecordtype@odata.bind");
    }

    // ── Fix round 5: an update that re-names the matter the row already names derives nothing ────────────────────────

    [Theory]
    [InlineData("child re-file")]
    [InlineData("update tool")]
    public async Task AnInvoiceGivenTheMatterItAlreadyNames_ReadsNoCatalog_AndWritesNoneOfTheFive(string route)
    {
        // The row keeps the fields it has, as the pair planner keeps a pair that does not move (045b rule 4).
        StoredInvoice(GuardInvoice, "sprk_matter", OrdinaryMatter);
        ShareWorld.OrdinaryChild("sprk_invoice", GuardInvoice, ("sprk_matter", "sprk_matter", OrdinaryMatter));
        var reads = new List<string>();
        _user.GetOverride = path =>
        {
            reads.Add(path);
            return null;
        };

        if (route == "child re-file")
        {
            var result = await RefileChild("sprk_invoice", GuardInvoice,
                new() { ["sprk_Matter@odata.bind"] = $"/sprk_matters({OrdinaryMatter:D})" });
            Status(result).Should().Be(StatusCodes.Status204NoContent, Detail(result));
        }
        else
        {
            var result = await UpdateRecord("sprk_invoice", GuardInvoice, Lookup("sprk_matter", "sprk_matter", OrdinaryMatter));
            result.Success.Should().BeTrue(result.ErrorMessage);
        }

        reads.Should().NotContain(p => p.StartsWith("sprk_recordtype_refs?", StringComparison.Ordinal), "nothing is derived");
        var body = BodyOf(_user.Patches.Should().ContainSingle().Subject.Body);
        body.Keys.Should().NotContain(ResolverColumns).And.NotContain("sprk_regardingrecordtype@odata.bind");
    }
}
