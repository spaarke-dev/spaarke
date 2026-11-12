using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Sprk.Bff.Api.Services.Ai.Handlers;
using Sprk.Bff.Api.Services.Finance;
using Sprk.Bff.Api.Services.RecordCreation;
using Xunit;

namespace Sprk.Bff.Api.Tests.Integration.DataMutation.RecordOwnership;

/// <summary>
/// ebilling-r1 task 165d fix round (EB-D-207; unified-access-control-r2 U1, option A): the writers that write AS the person ask the
/// writer-only COLUMN check alone (<see cref="RecordTableRules.RefuseWriterOnlyColumns"/>), never the full app-only refusal. A payload naming
/// the AP export claim (<c>sprk_invoice.sprk_billingexport</c>, plain or as its bind) is refused by the chat update tool (<c>ValidateChat</c> and
/// <c>ExecuteChatAsync</c>), the child-records update core and the re-file core, with the writer-only column code, before any read or write.
/// The same writer onto another column of the invoice proceeds past the check (must not fire). The work-product persister is pinned in
/// <c>WorkProductRecordPersisterTests</c>.
/// </summary>
public sealed partial class SecureChildOwnershipAiToolTests
{
    private const string BillingExportClaim = ApExportService.BatchLookup;
    private static readonly string ClaimColumnCode = RecordTableRules.ChangedOnlyByItsWriterColumnReasonCode;

    /// <summary>The claim as a person-token writer receives it: the plain column, and its bind (a lookup written by name).</summary>
    public static TheoryData<string> ClaimKeys => new() { BillingExportClaim, "sprk_BillingExport@odata.bind" };

    [Theory(DisplayName = "165d U1 (option A, must fire): the chat update tool refuses an invoice item naming the AP export claim, at validation and on execute, with nothing read or written")]
    [MemberData(nameof(ClaimKeys))]
    public async Task ChatUpdate_TheApExportClaim_IsRefused_AtValidationAndOnExecute(string key)
    {
        var reads = new List<string>();
        _user.GetOverride = path =>
        {
            reads.Add(path);
            return null;
        };
        var claim = Text("/sprk_billingexports(165d0000-0000-0000-0000-00000000ba7c)");

        var validation = ChatValidateOf("sprk_invoice", PairRow, ("sprk_description", Text("x")), (key, claim));
        var result = await ChatUpdateOf("sprk_invoice", PairRow, ("sprk_description", Text("x")), (key, claim));

        validation.IsValid.Should().BeFalse();
        validation.Errors.Should().ContainSingle().Which.Should().Contain(BillingExportClaim).And.Contain(ClaimColumnCode);
        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ClaimColumnCode);
        result.ErrorMessage.Should().Be(validation.Errors.Single(), "the same words in both legs");
        _user.Patches.Should().BeEmpty();
        _user.Posts.Should().BeEmpty();
        reads.Should().BeEmpty("the refusal is decided on the item's shape alone, before any read");
    }

    [Fact(DisplayName = "165d U1 (option A, must not fire): the chat update tool still validates an invoice item that names another column, and the claim's name on another table")]
    public void ChatUpdate_AnotherColumn_StillValidates()
    {
        ChatValidateOf("sprk_invoice", PairRow, ("sprk_description", Text("x"))).IsValid.Should().BeTrue();
        ChatValidateOf("sprk_todo", PairRow, (BillingExportClaim, Text("x"))).IsValid.Should().BeTrue("only the invoice carries the claim");
    }

    [Theory(DisplayName = "165d U1 (option A, must fire): the child-records update core refuses a body naming the AP export claim, before any read or write")]
    [MemberData(nameof(ClaimKeys))]
    public async Task ChildRecordUpdateCore_TheApExportClaim_IsRefused_BeforeAnyReadOrWrite(string key)
    {
        var reads = new List<string>();
        _user.GetOverride = path =>
        {
            reads.Add(path);
            return null;
        };

        var result = await Sprk.Bff.Api.Api.ChildRecordEndpoints.UpdateAsync(
            "sprk_invoice", PairRow,
            JsonSerializer.SerializeToElement(new Dictionary<string, object?>
            {
                ["sprk_description"] = "x",
                [key] = "/sprk_billingexports(165d0000-0000-0000-0000-00000000ba7c)",
            }),
            _user, _world.Resolver(),
            new Sprk.Bff.Api.Tests.Integration.DataMutation.CoreAncestorStamping.StampWorld().Restamper, Children(),
            new DefaultHttpContext(), Microsoft.Extensions.Logging.Abstractions.NullLogger<Program>.Instance, CancellationToken.None);

        var problem = result.Should().BeOfType<Microsoft.AspNetCore.Http.HttpResults.ProblemHttpResult>().Subject;
        problem.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
        problem.ProblemDetails.Extensions["reasonCode"].Should().Be(ClaimColumnCode);
        problem.ProblemDetails.Detail.Should().Contain(BillingExportClaim).And.Contain("changed only by Spaarke itself");
        _user.Patches.Should().BeEmpty();
        _user.Posts.Should().BeEmpty();
        reads.Should().BeEmpty("the refusal is decided on the body's shape alone, before any read");
    }

    [Fact(DisplayName = "165d U1 (option A, must not fire): the child-records update core still updates an ordinary table past the column check")]
    public async Task ChildRecordUpdateCore_AnotherColumn_IsStillWritten()
    {
        var result = await Sprk.Bff.Api.Api.ChildRecordEndpoints.UpdateAsync(
            "sprk_todo", PairRow, JsonSerializer.SerializeToElement(new Dictionary<string, object?> { ["sprk_name"] = "Renamed" }),
            _user, _world.Resolver(),
            new Sprk.Bff.Api.Tests.Integration.DataMutation.CoreAncestorStamping.StampWorld().Restamper, Children(),
            new DefaultHttpContext(), Microsoft.Extensions.Logging.Abstractions.NullLogger<Program>.Instance, CancellationToken.None);

        result.Should().BeOfType<Microsoft.AspNetCore.Http.HttpResults.NoContent>();
        PatchedKeys().Should().BeEquivalentTo("sprk_name");
    }

    [Theory(DisplayName = "165d U1 (option A, must fire): the re-file core refuses an item naming the AP export claim before any read or write, with the code in its text")]
    [MemberData(nameof(ClaimKeys))]
    public async Task RefileCore_TheApExportClaim_IsRefused_AndNothingIsReadOrWritten(string key)
    {
        var reads = new List<string>();
        _user.GetOverride = path =>
        {
            reads.Add(path);
            return null;
        };

        var outcome = await RefileOf("sprk_invoice", "sprk_description", key);

        outcome.Invalid.Should().Contain(BillingExportClaim).And.Contain(ClaimColumnCode);
        (outcome.Written, outcome.NotARefile).Should().Be((false, false));
        _user.Patches.Should().BeEmpty();
        _user.Posts.Should().BeEmpty();
        reads.Should().BeEmpty("the refusal is decided on the item's shape alone, before any read");
    }

    [Fact(DisplayName = "165d U1 (option A, must not fire): the re-file core does not refuse an item naming another column (or the claim's name on another table)")]
    public async Task RefileCore_AnotherColumn_IsNotRefusedByTheColumnCheck()
    {
        var invoice = await RefileOf("sprk_invoice", "sprk_description");
        var todo = await RefileOf("sprk_todo", "sprk_name", BillingExportClaim);

        invoice.Invalid.Should().BeNull();
        todo.Invalid.Should().BeNull();
    }

    private Task<Sprk.Bff.Api.Services.Dataverse.OwnedChildWrite.RefileOutcome> RefileOf(string table, params string[] columns) =>
        Sprk.Bff.Api.Services.Dataverse.OwnedChildWrite.RefileAsync(
            _user, _world.Resolver(), table, PairRow, table + "s", table + "id",
            new Sprk.Bff.Api.Services.Dataverse.DataverseWriteItemMapper.MappedItem(
                JsonSerializer.Serialize(columns.ToDictionary(c => c, _ => (object?)"x")), columns),
            callerObjectId: null, CancellationToken.None);
}
