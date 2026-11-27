using System.Text.Json;
using FluentAssertions;
using Sprk.Bff.Api.Api;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Services.Ai.Handlers;
using Sprk.Bff.Api.Services.Signals;
using Xunit;

namespace Sprk.Bff.Api.Tests.Integration.DataMutation.RecordOwnership;

/// <summary>
/// spaarke-ontology-platform-r1 task 234 (#3279, D-233; #1984, D-111): the chat update tool (<c>dataverse.update_record</c>) refuses
/// completing or dismissing the "Record the outcome" To Do of an OPEN budget inquiry with the browser route's own guard
/// (<see cref="ChildRecordEndpoints.InquiryTodoCloseRefusalAsync"/>), and writes nothing. The refusal comes after the confirm dialog
/// (D-233 option B for the chat leg): the pre-confirm validation does no I/O, which the last test pins. Driven through the REAL handler
/// over the scripted caller Dataverse of this harness, with the same world as the route's tests.
/// </summary>
public sealed partial class SecureChildOwnershipAiToolTests
{
    private static (string Column, JsonElement Value)[] ChatItem(Dictionary<string, object?> body) =>
        body.Select(kv => (kv.Key, JsonSerializer.SerializeToElement(kv.Value))).ToArray();

    [Fact]
    public async Task ChatTool_CompletingAnOpenInquirysToDo_IsRefusedInPlainWords_AndWritesNothing()
    {
        InquiryTodoWorld($"serviceRequestId={OpenInquiry:D}", InquiryMatter);
        ScriptInquiryReads();

        var result = await UpdateRecord("sprk_todo", InquiryTodo, ChatItem(Completion()));

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(DecisionCommitReasons.InquiryDispositionRequired);
        result.ErrorMessage.Should().Contain("NOT written")
            .And.Contain("record the outcome on the inquiry first")
            .And.Contain(ChildRecordEndpoints.InquiryDispositionRequiredDetail, "the route's own words");
        _user.Patches.Should().BeEmpty("nothing is written");
        InquiryReads.Should().ContainSingle().Which.Should().StartWith($"sprk_servicerequests({OpenInquiry:D})");
    }

    [Theory]
    [InlineData(1, 659490002)] // Dismiss
    [InlineData(null, 2)]      // statuscode Completed alone
    [InlineData(1, null)]      // statecode Inactive alone
    public async Task ChatTool_DismissingOrClosingAnOpenInquirysToDo_AnyOtherWay_IsRefusedToo(int? state, int? status)
    {
        InquiryTodoWorld(notes: "typed over by the user", InquiryMatter); // found through the matter's open inquiries
        ScriptInquiryReads();
        var body = new Dictionary<string, object?>();
        if (state is { } s) body["statecode"] = s;
        if (status is { } c) body["statuscode"] = c;

        var result = await UpdateRecord("sprk_todo", InquiryTodo, ChatItem(body));

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(DecisionCommitReasons.InquiryDispositionRequired);
        _user.Patches.Should().BeEmpty();
    }

    [Fact]
    public async Task ChatTool_ALookupFault_FailsClosed_WithTheRoutesCode_AndWritesNothing()
    {
        InquiryTodoWorld($"serviceRequestId={OpenInquiry:D}", InquiryMatter);
        ScriptInquiryReads(_ => DataverseUserResponse.Fail(500, DataverseUserClientErrorCodes.BadRequest, "Scripted fault."));

        var result = await UpdateRecord("sprk_todo", InquiryTodo, ChatItem(Completion()));

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ChildRecordEndpoints.InquiryCheckUnavailableCode);
        _user.Patches.Should().BeEmpty("a fault never reads as 'no inquiry'");
    }

    /// <summary>Fix round 1 (K4): the write mapper trims column names (MapCoreAsync), so the check must too: a key with
    /// padding still closes the To Do and is still refused, on the chat tool and on the browser route.</summary>
    [Theory]
    [InlineData(" statecode", " statuscode")]
    [InlineData("statecode ", "\tstatuscode")]
    public async Task APaddedStatusKey_StillClosesTheToDo_AndIsRefused_OnTheChatToolAndTheRoute(string stateKey, string statusKey)
    {
        InquiryTodoWorld($"serviceRequestId={OpenInquiry:D}", InquiryMatter);
        ScriptInquiryReads();
        var body = new Dictionary<string, object?> { [stateKey] = 1, [statusKey] = 2 };

        var chat = await UpdateRecord("sprk_todo", InquiryTodo, ChatItem(body));
        var route = await RefileChild("sprk_todo", InquiryTodo, body);

        chat.Success.Should().BeFalse();
        chat.ErrorCode.Should().Be(DecisionCommitReasons.InquiryDispositionRequired);
        Status(route).Should().Be(Microsoft.AspNetCore.Http.StatusCodes.Status409Conflict);
        _user.Patches.Should().BeEmpty("nothing is written on either path");
    }

    // ── Must not fire ─────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ChatTool_CompletingAToDoWithNoInquiry_CompletesAsBefore_WithTheSameColumns()
    {
        InquiryTodoWorld(notes: null, matter: null);
        ScriptInquiryReads();

        var result = await UpdateRecord("sprk_todo", InquiryTodo, ChatItem(Completion()));

        result.Success.Should().BeTrue(result.ErrorMessage);
        var patch = _user.Patches.Should().ContainSingle().Subject;
        patch.Path.Should().Be($"sprk_todos({InquiryTodo:D})");
        patch.Body.Should().Contain("\"statecode\":1").And.Contain("\"statuscode\":2");
        InquiryReads.Should().BeEmpty();
    }

    [Theory]
    [InlineData(1, 100000001)] // the inquiry is closed
    [InlineData(0, 100000000)] // an inbound service request is not an inquiry
    public async Task ChatTool_CompletingTheToDoOfAClosedInquiry_OrOfANonInquiry_CompletesAsBefore(int state, int direction)
    {
        InquiryTodoWorld($"serviceRequestId={OpenInquiry:D}", matter: null, inquiryState: state, direction: direction);
        ScriptInquiryReads();

        var result = await UpdateRecord("sprk_todo", InquiryTodo, ChatItem(Completion()));

        result.Success.Should().BeTrue(result.ErrorMessage);
        _user.Patches.Should().ContainSingle();
    }

    [Fact]
    public async Task ChatTool_AnUpdateThatDoesNotCloseTheToDo_IsNotChecked()
    {
        InquiryTodoWorld($"serviceRequestId={OpenInquiry:D}", InquiryMatter);
        ScriptInquiryReads();

        var result = await UpdateRecord("sprk_todo", InquiryTodo,
            ("sprk_name", JsonSerializer.SerializeToElement("renamed")), ("statecode", JsonSerializer.SerializeToElement(0)));

        result.Success.Should().BeTrue(result.ErrorMessage);
        _user.Patches.Should().ContainSingle();
        InquiryReads.Should().BeEmpty("only a write that closes a To Do is checked");
    }

    [Fact]
    public void ChatTool_ThePreConfirmValidation_DoesNoIo_SoTheInquiryRefusalComesAfterTheConfirm_KnownLimit()
    {
        InquiryTodoWorld($"serviceRequestId={OpenInquiry:D}", InquiryMatter);
        ScriptInquiryReads();
        var args = JsonSerializer.Serialize(new
        {
            tablename = "sprk_todo",
            recordId = InquiryTodo,
            item = new Dictionary<string, object> { ["statecode"] = 1, ["statuscode"] = 2 },
        });

        var validation = new DataverseUpdateRecordHandler(
                _user, new Sprk.Bff.Api.Tests.Integration.DataMutation.CoreAncestorStamping.StampWorld().AfterWriteRestamp,
                CreateLogger<DataverseUpdateRecordHandler>(), _world.Resolver(),
                Sprk.Bff.Api.Tests.TestInfrastructure.SecureRootFilingGateFixtures.NothingSecure())
            .ValidateChat(BuildChatInvocationContext(toolArgumentsJson: args), BuildAnalysisTool(nameof(DataverseUpdateRecordHandler)));

        validation.IsValid.Should().BeTrue("ValidateChat does no I/O (D-233: the chat refusal comes after the confirm dialog)");
        InquiryReads.Should().BeEmpty();
    }
}
