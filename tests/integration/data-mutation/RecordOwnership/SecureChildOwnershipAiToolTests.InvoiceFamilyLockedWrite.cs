using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Moq;
using Sprk.Bff.Api.Api;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Dataverse;
using Xunit;

namespace Sprk.Bff.Api.Tests.Integration.DataMutation.RecordOwnership;

/// <summary>
/// #2799 (unified-access-control-r2), chunk 3, C4-C6: the OBO writers — the re-file core's PATCH (<see cref="OwnedChildWrite.RefileAsync"/>,
/// reached by <c>PATCH /api/v1/child-records/sprk_invoice/{id}</c> and the chat update tool), the route's ordinary update and the
/// chat tool's ordinary update — write a LOCKED invoice-family column (<see cref="InvoiceFamilyLockedWrite.LockedColumns"/>) through
/// <see cref="InvoiceFamilyLockedWrite.PatchAsCallerAsync"/>: checked under the caller's own token (Write and Append on the row,
/// AppendTo on the record it names) and written by the application, the other columns still by the caller's own PATCH. Denied is
/// each path's existing denial (route 403 <c>child_record.denied</c>; tool <c>AccessDenied</c>); an unanswered check is the
/// invoice-family "could not be read" answer; nothing is written on either. The application's write failing after the caller's
/// PATCH is never success. A body with no locked column is the caller's single PATCH, unchanged.
/// </summary>
public sealed partial class SecureChildOwnershipAiToolTests
{
    private static readonly string LockedPartlySavedDetail = InvoiceFamilyLockedWrite.PartlySavedMessage;

    /// <summary>An invoice under the ordinary matter, and the invoice it may supersede (same matter), as the caller reads them.</summary>
    private void TwoInvoicesUnderTheOrdinaryMatter()
    {
        StoredInvoice(GuardInvoice, "sprk_matter", OrdinaryMatter);
        StoredInvoice(SupersededInvoice, "sprk_matter", OrdinaryMatter);
        ShareWorld.OrdinaryChild("sprk_invoice", GuardInvoice, ("sprk_matter", "sprk_matter", OrdinaryMatter));
    }

    private static readonly string SupersedesBind = "sprk_SupersedesInvoice@odata.bind";

    /// <summary>C5: the route's ordinary update (a supersedes link is no ownership parent), mixed with a plain column.</summary>
    private Task<IResult> RouteSupersedes(bool withName = true)
    {
        var body = new Dictionary<string, object?> { [SupersedesBind] = $"/sprk_invoices({SupersededInvoice:D})" };
        if (withName)
            body["sprk_name"] = "Renamed";
        return RefileChild("sprk_invoice", GuardInvoice, body);
    }

    /// <summary>C6: the chat tool's ordinary update of the same.</summary>
    private Task<ToolResult> ToolSupersedes() =>
        UpdateRecord("sprk_invoice", GuardInvoice,
            Lookup("sprk_supersedesinvoice", "sprk_invoice", SupersededInvoice),
            ("sprk_name", JsonSerializer.SerializeToElement("Renamed")));

    /// <summary>C4: a re-file through the route (the core lookup re-named, with a plain column).</summary>
    private Task<IResult> RouteRefile() =>
        RefileChild("sprk_invoice", GuardInvoice,
            new() { ["sprk_Matter@odata.bind"] = $"/sprk_matters({OrdinaryMatter:D})", ["sprk_name"] = "Renamed" });

    /// <summary>C4: the same re-file through the chat tool.</summary>
    private Task<ToolResult> ToolRefile() =>
        UpdateRecord("sprk_invoice", GuardInvoice,
            Lookup("sprk_matter", "sprk_matter", OrdinaryMatter), ("sprk_name", JsonSerializer.SerializeToElement("Renamed")));

    /// <summary>The caller's rights question on the invoice row answers <paramref name="status"/> (no answer about the caller).</summary>
    private void RowRightsQuestionAnswers(int status) =>
        _user.GetOverride = path =>
            path.Contains("RetrievePrincipalAccess", StringComparison.Ordinal)
            && path.Contains($"sprk_invoices({GuardInvoice:D})", StringComparison.Ordinal)
                ? DataverseUserResponse.Fail(status, DataverseUserClientErrorCodes.ServiceError, "Scripted.")
                : null;

    private void NothingLockedOrOtherWritten()
    {
        _user.Patches.Should().BeEmpty("nothing is written as the caller");
        _appLockedWrites.Should().BeEmpty("nothing is written as the application");
    }

    // ── Must not fire ─────────────────────────────────────────────────────────────────────────────────────────────────

    [Fact(DisplayName = "#2799 C5 (must not fire): an allowed ordinary update writes the other columns as the caller, then the locked one as the app")]
    public async Task Locked_Route_AllowedOrdinaryUpdate_Splits()
    {
        TwoInvoicesUnderTheOrdinaryMatter();

        var result = await RouteSupersedes();

        Status(result).Should().Be(StatusCodes.Status204NoContent, Detail(result));
        BodyOf(_user.Patches.Should().ContainSingle().Subject.Body).Keys.Should().BeEquivalentTo("sprk_name");
        _appLockedWrites.Should().ContainSingle().Which.Fields.Keys.Should().BeEquivalentTo(SupersedesBind);
    }

    [Fact(DisplayName = "#2799 C6 (must not fire): the chat tool's allowed ordinary update splits the same way")]
    public async Task Locked_Tool_AllowedOrdinaryUpdate_Splits()
    {
        TwoInvoicesUnderTheOrdinaryMatter();

        var result = await ToolSupersedes();

        result.Success.Should().BeTrue(result.ErrorMessage);
        BodyOf(_user.Patches.Should().ContainSingle().Subject.Body).Keys.Should().BeEquivalentTo("sprk_name");
        _appLockedWrites.Should().ContainSingle().Which.Fields.Keys.Should().ContainSingle()
            .Which.Should().EndWith("@odata.bind");
    }

    [Fact(DisplayName = "#2799 C4 (must not fire): a re-file through the route writes the core lookup as the app inside the re-file")]
    public async Task Locked_Route_AllowedRefile_Splits()
    {
        TwoInvoicesUnderTheOrdinaryMatter();

        var result = await RouteRefile();

        Status(result).Should().Be(StatusCodes.Status204NoContent, Detail(result));
        BodyOf(_user.Patches.Should().ContainSingle().Subject.Body).Keys.Should().BeEquivalentTo("sprk_name");
        _appLockedWrites.Should().ContainSingle().Which.Fields.Keys.Should().BeEquivalentTo("sprk_Matter@odata.bind");
    }

    [Fact(DisplayName = "#2799 C4 (must not fire): the chat tool's re-file writes the core lookup as the app")]
    public async Task Locked_Tool_AllowedRefile_Splits()
    {
        TwoInvoicesUnderTheOrdinaryMatter();

        var result = await ToolRefile();

        result.Success.Should().BeTrue(result.ErrorMessage);
        BodyOf(_user.Patches.Should().ContainSingle().Subject.Body).Keys.Should().BeEquivalentTo("sprk_name");
        _appLockedWrites.Should().ContainSingle();
    }

    [Fact(DisplayName = "#2799 (must not fire): an invoice update with no locked column is the caller's single PATCH, unchanged")]
    public async Task Locked_NoLockedColumn_IsTheCallersSinglePatch()
    {
        TwoInvoicesUnderTheOrdinaryMatter();
        var asked = new List<string>();
        _user.GetOverride = path =>
        {
            if (path.Contains("RetrievePrincipalAccess", StringComparison.Ordinal))
                asked.Add(path);
            return null;
        };

        var route = await RefileChild("sprk_invoice", GuardInvoice, new() { ["sprk_name"] = "Renamed" });
        var tool = await UpdateRecord("sprk_invoice", GuardInvoice, ("sprk_name", JsonSerializer.SerializeToElement("Renamed")));

        Status(route).Should().Be(StatusCodes.Status204NoContent, Detail(route));
        tool.Success.Should().BeTrue(tool.ErrorMessage);
        _user.Patches.Should().HaveCount(2).And.OnlyContain(p =>
            p.Path == $"sprk_invoices({GuardInvoice:D})" && p.Body == "{\"sprk_name\":\"Renamed\"}");
        _appLockedWrites.Should().BeEmpty();
        asked.Should().BeEmpty("nothing is asked for a write with no locked column");
    }

    // ── Must fire ─────────────────────────────────────────────────────────────────────────────────────────────────────

    [Fact(DisplayName = "#2799 C5/C6 (must fire): a caller without Write on the invoice is refused with the path's denial, nothing written")]
    public async Task Locked_NoWriteOnTheRow_IsDenied()
    {
        TwoInvoicesUnderTheOrdinaryMatter();
        _user.TableRights["sprk_invoices"] = "ReadAccess,AppendAccess,AppendToAccess";

        var route = await RouteSupersedes();
        var tool = await ToolSupersedes();

        Status(route).Should().Be(StatusCodes.Status403Forbidden);
        ReasonCode(route).Should().Be(ChildRecordEndpoints.DeniedCode);
        tool.Success.Should().BeFalse();
        tool.ErrorCode.Should().Be(DataverseUserClientErrorCodes.AccessDenied);
        NothingLockedOrOtherWritten();
    }

    [Fact(DisplayName = "#2799 C4 (must fire): a re-file by a caller without Write on the invoice is refused, nothing written or re-owned")]
    public async Task Locked_Refile_NoWriteOnTheRow_IsDenied()
    {
        TwoInvoicesUnderTheOrdinaryMatter();
        _user.TableRights["sprk_invoices"] = "ReadAccess,AppendAccess,AppendToAccess";

        var route = await RouteRefile();
        var tool = await ToolRefile();

        Status(route).Should().Be(StatusCodes.Status403Forbidden);
        ReasonCode(route).Should().Be(ChildRecordEndpoints.DeniedCode);
        tool.ErrorCode.Should().Be(DataverseUserClientErrorCodes.AccessDenied);
        NothingLockedOrOtherWritten();
        _world.Assignments.Should().BeEmpty("no owner is assigned");
    }

    [Fact(DisplayName = "#2799 C5/C6 (must fire): no AppendTo on the invoice it would supersede is refused, nothing written")]
    public async Task Locked_NoAppendToOnTheNewInvoice_IsDenied()
    {
        TwoInvoicesUnderTheOrdinaryMatter();
        _user.NoAppendTo.Add(SupersededInvoice);

        var route = await RouteSupersedes();
        var tool = await ToolSupersedes();

        // The path's existing answer for a record the caller may not append to (its own lookup check asks first; the
        // locked-column check asks the same question again before the app writes, InvoiceFamilyLockedWriteTests).
        Status(route).Should().Be(StatusCodes.Status404NotFound);
        ReasonCode(route).Should().Be(ChildRecordEndpoints.NotFoundCode);
        tool.Success.Should().BeFalse();
        tool.ErrorCode.Should().Be(DataverseUserClientErrorCodes.AccessDenied);
        NothingLockedOrOtherWritten();
    }

    [Theory(DisplayName = "#2799 C4-C6 (must fire): an unanswered rights question is the could-not-be-read answer, nothing written")]
    [InlineData("route ordinary")]
    [InlineData("tool ordinary")]
    [InlineData("route re-file")]
    [InlineData("tool re-file")]
    public async Task Locked_UnansweredQuestion_IsUnavailable(string path)
    {
        TwoInvoicesUnderTheOrdinaryMatter();
        RowRightsQuestionAnswers(StatusCodes.Status503ServiceUnavailable);

        switch (path)
        {
            case "route ordinary":
            case "route re-file":
                var route = path == "route ordinary" ? await RouteSupersedes() : await RouteRefile();
                Status(route).Should().Be(StatusCodes.Status500InternalServerError);
                ReasonCode(route).Should().Be(InvoiceFamilyCoreRecordGuard.ReadFailedCode);
                break;
            default:
                var tool = path == "tool ordinary" ? await ToolSupersedes() : await ToolRefile();
                tool.Success.Should().BeFalse();
                tool.ErrorCode.Should().Be(InvoiceFamilyCoreRecordGuard.ReadFailedCode);
                break;
        }

        NothingLockedOrOtherWritten();
    }

    [Fact(DisplayName = "#2799 C5 (must fire): an unknown invoice answers byte-for-byte as one the caller may not use")]
    public async Task Locked_UnknownInvoice_IsByteIdenticalToDenied()
    {
        TwoInvoicesUnderTheOrdinaryMatter();
        _user.Missing.Add(SupersededInvoice); // RetrievePrincipalAccess: 404
        var unknown = await RouteSupersedes();
        _user.Missing.Clear();
        _user.NoAppendTo.Add(SupersededInvoice);
        var forbidden = await RouteSupersedes();

        ProblemOf(unknown).Should().Be(ProblemOf(forbidden));
        Status(unknown).Should().Be(StatusCodes.Status404NotFound, "the path's existing answer for a record it may not use");
        NothingLockedOrOtherWritten();
    }

    [Theory(DisplayName = "#2799 C4-C6 (must fire): the app's write failing after the caller's PATCH is partly saved, never success")]
    [InlineData("route ordinary")]
    [InlineData("tool ordinary")]
    [InlineData("route re-file")]
    [InlineData("tool re-file")]
    public async Task Locked_AppWriteFailsAfterTheCallers_IsPartlySaved(string path)
    {
        TwoInvoicesUnderTheOrdinaryMatter();
        _appOnly
            .Setup(a => a.UpdateExistingRecordFieldsAsync(
                It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<Dictionary<string, object?>>(), null, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("503 at 10.0.0.9"));

        switch (path)
        {
            case "route ordinary":
            case "route re-file":
                var route = path == "route ordinary" ? await RouteSupersedes() : await RouteRefile();
                Status(route).Should().Be(StatusCodes.Status500InternalServerError);
                ReasonCode(route).Should().Be(InvoiceFamilyLockedWrite.LockedWritePartlySavedCode);
                Detail(route).Should().Be(LockedPartlySavedDetail);
                break;
            default:
                var tool = path == "tool ordinary" ? await ToolSupersedes() : await ToolRefile();
                tool.Success.Should().BeFalse();
                tool.ErrorCode.Should().Be(DataverseUserClientErrorCodes.ServiceError);
                tool.ErrorMessage.Should().Be(LockedPartlySavedDetail).And.NotContain("10.0.0.9");
                break;
        }

        BodyOf(_user.Patches.Should().ContainSingle("the caller's PATCH of the other columns landed").Subject.Body)
            .Keys.Should().BeEquivalentTo("sprk_name");
    }
}
