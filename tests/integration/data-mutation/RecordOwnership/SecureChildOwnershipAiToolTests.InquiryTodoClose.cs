using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Services.Signals;
using Sprk.Bff.Api.Services.Signals.Actions;
using Sprk.Bff.Api.Tests.Services.Communication;
using Xunit;
using Directory = Sprk.Bff.Api.Tests.TestInfrastructure.OwnershipDirectory;

namespace Sprk.Bff.Api.Tests.Integration.DataMutation.RecordOwnership;

/// <summary>
/// spaarke-ontology-platform-r1 task 228 (#1984, D-111): the "Record the outcome" To Do of an OPEN budget inquiry is closed only by
/// recording the inquiry's disposition. <c>PATCH /api/v1/child-records/sprk_todo/{id}</c> (and the core the decision executors call)
/// refuses an update that completes or dismisses it with the commit route's 409 <c>inquiry.disposition_required</c>, using the
/// worklist's own identification (<see cref="InquiryDispositionService.FindOpenInquiryOfTodoAsync"/>). Driven through the REAL handler
/// over the scripted caller Dataverse of this harness.
/// </summary>
public sealed partial class SecureChildOwnershipAiToolTests
{
    private static readonly Guid OpenInquiry = Guid.Parse("a2280000-0000-4000-8000-0000000000a1");
    private static readonly Guid InquiryMatter = Guid.Parse("a2280000-0000-4000-8000-0000000000a2");

    /// <summary>The inquiry's To Do: its id is derived from the inquiry (task 071), as the real one is.</summary>
    private static Guid InquiryTodo => InquiryDispositionService.TodoIdFor(OpenInquiry);

    private static Dictionary<string, object?> Completion() => new()
    {
        ["statecode"] = 1,
        ["statuscode"] = 2,
        ["sprk_completedon"] = "2026-11-26T10:00:00Z",
    };

    /// <summary>The To Do as the caller reads it, and the inquiry it names (<paramref name="inquiryState"/>: 0 open, 1 closed).</summary>
    private void InquiryTodoWorld(string? notes, Guid? matter, int inquiryState = 0, int direction = 100000001)
    {
        _world.WithRecord("sprk_todo", InquiryTodo, Directory.ChildBu, owningTeam: Directory.ChildTeam);
        _user.RowValues[InquiryTodo] = new() { ["sprk_notes"] = notes, ["_sprk_regardingmatter_value"] = matter?.ToString("D") };
        _user.RowValues[OpenInquiry] = new() { ["statecode"] = inquiryState, ["sprk_direction"] = direction };
    }

    private List<string> InquiryReads { get; } = new();

    /// <summary>Records every read of a service request (the inquiry check's only reads) and answers the matter list.</summary>
    private void ScriptInquiryReads(Func<string, DataverseUserResponse?>? answer = null) =>
        _user.GetOverride = path =>
        {
            if (!path.StartsWith("sprk_servicerequests", StringComparison.Ordinal))
                return null;
            InquiryReads.Add(path);
            if (answer?.Invoke(path) is { } scripted)
                return scripted;
            return path.StartsWith("sprk_servicerequests?", StringComparison.Ordinal)
                ? DataverseUserResponse.Ok(200, System.Text.Json.JsonSerializer.SerializeToElement(new
                {
                    value = new[] { new { sprk_servicerequestid = OpenInquiry.ToString("D") } },
                }))
                : null;
        };

    private Task<IResult> RefileChildLogged(string table, Guid id, Dictionary<string, object?> payload, ILogger<Program> logger) =>
        ChildRecordEndpoints.RefileAsync(
            table, id, Payload(payload), _user, _world.Resolver(), Restamper(), Children(), AppEntities(), HttpContextOfCaller(),
            logger, CancellationToken.None);

    [Fact]
    public async Task CompletingAnOpenInquirysToDo_NamedByItsNotesHint_Is409DispositionRequired_AndWritesNothing()
    {
        InquiryTodoWorld($"serviceRequestId={OpenInquiry:D}", InquiryMatter);
        ScriptInquiryReads();

        var result = await RefileChild("sprk_todo", InquiryTodo, Completion());

        Status(result).Should().Be(StatusCodes.Status409Conflict);
        ReasonCode(result).Should().Be(DecisionCommitReasons.InquiryDispositionRequired);
        Detail(result).Should().Be(ChildRecordEndpoints.InquiryDispositionRequiredDetail);
        _user.Patches.Should().BeEmpty("nothing is written");
        _world.Assignments.Should().BeEmpty();
        InquiryReads.Should().ContainSingle().Which.Should().StartWith($"sprk_servicerequests({OpenInquiry:D})",
            "the hint's inquiry is read once, as the caller");
    }

    [Fact]
    public async Task CompletingAnOpenInquirysToDo_WithNoHint_IsFoundThroughItsMattersOpenInquiries_And409()
    {
        InquiryTodoWorld(notes: "typed over by the user", InquiryMatter);
        ScriptInquiryReads();

        var result = await RefileChild("sprk_todo", InquiryTodo, Completion());

        Status(result).Should().Be(StatusCodes.Status409Conflict);
        ReasonCode(result).Should().Be(DecisionCommitReasons.InquiryDispositionRequired);
        _user.Patches.Should().BeEmpty();
        InquiryReads.Should().ContainSingle().Which.Should().Contain($"_sprk_regardingmatter_value eq {InquiryMatter:D}");
    }

    [Theory]
    [InlineData(1, 659490002)] // Dismiss: statecode Inactive, statuscode Dismissed
    [InlineData(null, 2)]      // statuscode Completed alone (Dataverse moves the state with it)
    [InlineData(1, null)]      // statecode Inactive alone
    public async Task DismissingOrClosingAnOpenInquirysToDo_AnyOtherWay_IsRefusedToo(int? state, int? status)
    {
        InquiryTodoWorld($"serviceRequestId={OpenInquiry:D}", InquiryMatter);
        ScriptInquiryReads();
        var body = new Dictionary<string, object?>();
        if (state is { } s) body["statecode"] = s;
        if (status is { } c) body["statuscode"] = c;

        var result = await RefileChild("sprk_todo", InquiryTodo, body);

        Status(result).Should().Be(StatusCodes.Status409Conflict);
        ReasonCode(result).Should().Be(DecisionCommitReasons.InquiryDispositionRequired);
        _user.Patches.Should().BeEmpty();
    }

    // ── Must not fire ─────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task CompletingAToDoWithNoInquiry_CompletesAsBefore_AndReadsNoInquiry()
    {
        InquiryTodoWorld(notes: null, matter: null);
        ScriptInquiryReads();

        var result = await RefileChild("sprk_todo", InquiryTodo, Completion());

        Status(result).Should().Be(StatusCodes.Status204NoContent);
        _user.Patches.Should().ContainSingle().Which.Path.Should().Be($"sprk_todos({InquiryTodo:D})");
        InquiryReads.Should().BeEmpty("no hint and no matter: the check costs nothing beyond the To Do read");
    }

    [Fact]
    public async Task CompletingAnOrdinaryToDoOnAMatterWithNoOpenInquiry_CompletesAsBefore()
    {
        InquiryTodoWorld(notes: null, InquiryMatter);
        ScriptInquiryReads(path => path.StartsWith("sprk_servicerequests?", StringComparison.Ordinal)
            ? DataverseUserResponse.Ok(200, System.Text.Json.JsonSerializer.SerializeToElement(new { value = Array.Empty<object>() }))
            : null);

        var result = await RefileChild("sprk_todo", InquiryTodo, Completion());

        Status(result).Should().Be(StatusCodes.Status204NoContent);
        _user.Patches.Should().ContainSingle();
    }

    [Theory]
    [InlineData(1, 100000001)] // the inquiry is closed (its disposition was recorded)
    [InlineData(0, 100000000)] // an inbound service request is not an inquiry
    public async Task CompletingTheToDoOfAClosedInquiry_OrOfAServiceRequestThatIsNoInquiry_CompletesAsBefore(int state, int direction)
    {
        InquiryTodoWorld($"serviceRequestId={OpenInquiry:D}", matter: null, inquiryState: state, direction: direction);
        ScriptInquiryReads();

        var result = await RefileChild("sprk_todo", InquiryTodo, Completion());

        Status(result).Should().Be(StatusCodes.Status204NoContent);
        _user.Patches.Should().ContainSingle();
    }

    [Fact]
    public async Task AnUpdateThatDoesNotCloseTheToDo_IsNotChecked()
    {
        InquiryTodoWorld($"serviceRequestId={OpenInquiry:D}", InquiryMatter);
        ScriptInquiryReads();

        var result = await RefileChild("sprk_todo", InquiryTodo, new() { ["sprk_name"] = "renamed", ["statecode"] = 0 });

        Status(result).Should().Be(StatusCodes.Status204NoContent);
        _user.Patches.Should().ContainSingle();
        InquiryReads.Should().BeEmpty("only a write that closes a To Do is checked");
    }

    [Fact]
    public async Task ANonToDoChildRecord_IsUntouchedByTheCheck()
    {
        var memo = Guid.Parse("a2280000-0000-4000-8000-0000000000b1");
        _world.WithRecord("sprk_memo", memo, Directory.ChildBu, owningTeam: Directory.ChildTeam);
        _user.RowValues[memo] = new() { ["sprk_notes"] = $"serviceRequestId={OpenInquiry:D}" };
        ScriptInquiryReads();

        var result = await RefileChild("sprk_memo", memo, new() { ["statecode"] = 1, ["statuscode"] = 2 });

        Status(result).Should().Be(StatusCodes.Status204NoContent);
        _user.Patches.Should().ContainSingle();
        InquiryReads.Should().BeEmpty();
    }

    [Theory]
    [InlineData(500)]
    [InlineData(429)]
    public async Task ALookupFault_Refuses503_FailClosed_Logged_AndWritesNothing(int status)
    {
        InquiryTodoWorld($"serviceRequestId={OpenInquiry:D}", InquiryMatter);
        ScriptInquiryReads(_ => DataverseUserResponse.Fail(status, DataverseUserClientErrorCodes.BadRequest, "Scripted fault."));
        var log = new CapturingLogger<Program>();

        var result = await RefileChildLogged("sprk_todo", InquiryTodo, Completion(), log);

        Status(result).Should().Be(StatusCodes.Status503ServiceUnavailable);
        ReasonCode(result).Should().Be(ChildRecordEndpoints.InquiryCheckUnavailableCode);
        _user.Patches.Should().BeEmpty("a fault never reads as 'no inquiry'");
        log.Entries.Should().Contain(e => e.Level == LogLevel.Warning && e.Message.Contains(InquiryTodo.ToString("D")),
            "the refusal is logged with the To Do");
    }

    [Fact]
    public async Task AMatterListFault_Refuses503_Too()
    {
        InquiryTodoWorld(notes: null, InquiryMatter);
        ScriptInquiryReads(path => path.StartsWith("sprk_servicerequests?", StringComparison.Ordinal)
            ? DataverseUserResponse.Fail(503, DataverseUserClientErrorCodes.BadRequest, "Scripted fault.")
            : null);

        var result = await RefileChild("sprk_todo", InquiryTodo, Completion());

        Status(result).Should().Be(StatusCodes.Status503ServiceUnavailable);
        _user.Patches.Should().BeEmpty();
    }
}
