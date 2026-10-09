using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.Events;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Services.Communication.Models;
using Sprk.Bff.Api.Services.Signals;
using Sprk.Bff.Api.Services.Signals.Actions;
using Xunit;

namespace Sprk.Bff.Api.Tests.Domain.Signals;

/// <summary>
/// Unit tests for the decision action executors (task 044; spec FR-52, FR-61; D-18, D-19, D-27, D-54, D-55).
/// </summary>
/// <remarks>
/// <para>Maintain-class (ADR-038, <c>tests/unit/domain/**</c>): the executors are pure orchestration over narrow seams — the
/// caller-rights probe, the caller-identity Dataverse client, the writer's SDK service and <see cref="DecisionRouteCores"/>
/// (the adapter over the shipped event, child-record and communications cores, which have their own suites). What these
/// pin is the contract: <b>refuse before writing</b>, <b>exactly its write</b>, <b>the written ids come back</b>, and
/// <b>nothing ever names <c>sprk_finalduedate</c></b>. The live proof as a low-privilege user is NOT in this file (see the task 044 notes: it
/// is blocked on role grants and a low-privilege token).</para>
/// </remarks>
public class DecisionActionExecutorsTests
{
    private static readonly Guid SignalId = Guid.Parse("11111111-0000-0000-0000-000000000001");
    private static readonly Guid MatterId = Guid.Parse("22222222-0000-0000-0000-000000000001");
    private static readonly Guid BudgetId = Guid.Parse("33333333-0000-0000-0000-000000000001");
    private static readonly Guid OtherMatterId = Guid.Parse("22222222-0000-0000-0000-000000000002");
    private static readonly Guid CallerUserId = Guid.Parse("44444444-0000-0000-0000-000000000001");
    private static readonly Guid ItemId = Guid.Parse("55555555-0000-0000-0000-000000000001");
    private static readonly Guid ContactId = Guid.Parse("66666666-0000-0000-0000-000000000001");
    private static readonly Guid FirmId = Guid.Parse("77777777-0000-0000-0000-000000000001");
    private static readonly DateTimeOffset Now = new(2026, 10, 9, 12, 0, 0, TimeSpan.Zero);

    // ---------------------------------------------------------------------------------------------------------------
    // Fakes
    // ---------------------------------------------------------------------------------------------------------------

    /// <summary>The caller-identity client: scripted GETs, recorded PATCHes. Anything unscripted is a 404.</summary>
    private sealed class FakeUserClient : IDataverseUserClient
    {
        private readonly List<(string Prefix, DataverseUserResponse Response)> _gets = new();
        public List<string> Gets { get; } = new();
        public List<(string Path, string Body)> Patches { get; } = new();
        public int PatchStatus { get; set; } = 204;

        public FakeUserClient OnGet(string prefix, object body)
        {
            _gets.Add((prefix, DataverseUserResponse.Ok(200, JsonSerializer.SerializeToElement(body))));
            return this;
        }

        public Task<DataverseUserResponse> GetAsync(string relativePath, CancellationToken cancellationToken)
        {
            Gets.Add(relativePath);
            var hit = _gets.FirstOrDefault(g => relativePath.StartsWith(g.Prefix, StringComparison.Ordinal));
            return Task.FromResult(hit.Prefix is null ? DataverseUserResponse.Fail(404, "not_found", "no") : hit.Response);
        }

        public Task<DataverseUserResponse> PatchAsync(string relativePath, string jsonBody, CancellationToken cancellationToken)
        {
            Patches.Add((relativePath, jsonBody));
            return Task.FromResult(PatchStatus is >= 200 and < 300
                ? DataverseUserResponse.Ok(PatchStatus, null)
                : DataverseUserResponse.Fail(PatchStatus, "denied", "no"));
        }

        public Task<DataverseUserResponse> PostAsync(string absoluteApiPath, string jsonBody, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("An executor posted through the user client.");

        public Task<DataverseUserResponse> PostAsync(string absoluteApiPath, string jsonBody, bool preferRepresentation, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("An executor posted through the user client.");

        public Task<DataverseUserResponse> DeleteAsync(string relativePath, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("An executor deleted through the user client.");
    }

    /// <summary>The probe: reports <paramref name="rights"/> on every record.</summary>
    private static Mock<CallerRecordAccessProbe> Probe(AccessRights rights, Guid? userId = null)
    {
        var probe = new Mock<CallerRecordAccessProbe>(
            new HttpClient(), new ConfigurationBuilder().Build(), NullLogger<CallerRecordAccessProbe>.Instance, null!)
        { CallBase = false };
        probe.Setup(p => p.GetCallerRightsAsync(It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(rights);
        probe.Setup(p => p.GetCallerSystemUserIdAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(userId ?? CallerUserId);
        return probe;
    }

    private const AccessRights CanWrite = AccessRights.Read | AccessRights.Write;
    private const AccessRights ReadOnly = AccessRights.Read;

    private sealed class WriterHarness
    {
        public Mock<IOrganizationServiceAsync2> Org { get; } = new();
        public List<Entity> Created { get; } = new();
        public Guid RevisionId { get; } = Guid.Parse("88888888-0000-0000-0000-000000000001");
        public bool Throw { get; set; }

        public OntologyWriterDataverseClient Build()
        {
            Org.Setup(o => o.CreateAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>()))
                .Returns<Entity, CancellationToken>((e, _) =>
                {
                    if (Throw) throw new InvalidOperationException("writer refused");
                    Created.Add(e);
                    return Task.FromResult(RevisionId);
                });
            return new OntologyWriterDataverseClient(() => Org.Object, NullLogger<OntologyWriterDataverseClient>.Instance);
        }
    }

    private static DecisionActionRequest Request(
        Dictionary<string, string?> parameters,
        DecisionRecordRef? subject = null,
        DecisionCoreRecord? core = null,
        Guid? matter = null) =>
        new(new DefaultHttpContext(), SignalId, subject, core, matter ?? MatterId, parameters);

    private static Mock<DecisionRouteCores> Cores() => new() { CallBase = false };

    private static RouteReply Reply(int status, string? reason = null, string? detail = null, object? body = null) =>
        new(status, body is null ? null : JsonSerializer.SerializeToElement(body), reason, detail);

    // ---------------------------------------------------------------------------------------------------------------
    // revise-budget (D-18, D-55)
    // ---------------------------------------------------------------------------------------------------------------

    private static FakeUserClient BudgetUser(Guid matter, decimal current = 100_000m) =>
        new FakeUserClient().OnGet("sprk_budgets(", new Dictionary<string, object?>
        {
            ["sprk_totalbudget"] = current,
            ["_sprk_matter_value"] = matter.ToString("D"),
        });

    private static Dictionary<string, string?> ReviseParams(string amount = "125000.50") => new()
    {
        ["budget"] = BudgetId.ToString("D"),
        ["amount"] = amount,
        ["reason"] = "Scope grew after the second amendment",
    };

    [Fact]
    public async Task ReviseBudget_CallerCanWriteBudget_WriterCreatesTheRevision_AndTheUserWritesTheAmount()
    {
        var user = BudgetUser(MatterId);
        var writer = new WriterHarness();
        var executor = new ReviseBudgetExecutor(Probe(CanWrite).Object, user, writer.Build(), new FakeTimeProvider(Now),
            NullLogger<ReviseBudgetExecutor>.Instance);

        var outcome = await executor.ExecuteAsync(Request(ReviseParams()), CancellationToken.None);

        outcome.Status.Should().Be(DecisionActionStatus.Done);
        writer.Created.Should().ContainSingle();
        var revision = writer.Created[0];
        revision.LogicalName.Should().Be("sprk_budgetrevision");
        revision.GetAttributeValue<EntityReference>("sprk_budget").Id.Should().Be(BudgetId);
        revision.GetAttributeValue<EntityReference>("sprk_matter").Id.Should().Be(MatterId);
        revision.GetAttributeValue<DateTime>("sprk_revisedon").Should().Be(Now.UtcDateTime);
        revision.GetAttributeValue<Money>("sprk_newamount").Value.Should().Be(125000.50m);
        revision.GetAttributeValue<Money>("sprk_prioramount").Value.Should().Be(100_000m);
        revision.GetAttributeValue<EntityReference>("sprk_revisedby").Id.Should().Be(CallerUserId);

        // D-55: the amount is the SIGNED-IN USER's write, on sprk_budget, through the caller-identity client.
        user.Patches.Should().ContainSingle();
        user.Patches[0].Path.Should().Be($"sprk_budgets({BudgetId:D})");
        JsonDocument.Parse(user.Patches[0].Body).RootElement.GetProperty("sprk_totalbudget").GetDecimal().Should().Be(125000.50m);

        // The writer never writes sprk_budget: it was asked to create exactly the revision and nothing else.
        writer.Org.Verify(o => o.UpdateAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>()), Times.Never);
        writer.Org.Verify(o => o.CreateAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>()), Times.Once);

        outcome.Written.Should().Equal(
            new DecisionRecordRef("sprk_budgetrevision", writer.RevisionId),
            new DecisionRecordRef("sprk_budget", BudgetId));
    }

    [Fact]
    public async Task ReviseBudget_CallerWithoutWriteOnTheBudget_WritesNothing_AndRefuses()
    {
        var user = BudgetUser(MatterId);
        var writer = new WriterHarness();
        var executor = new ReviseBudgetExecutor(Probe(ReadOnly).Object, user, writer.Build(), new FakeTimeProvider(Now),
            NullLogger<ReviseBudgetExecutor>.Instance);

        var outcome = await executor.ExecuteAsync(Request(ReviseParams()), CancellationToken.None);

        outcome.Status.Should().Be(DecisionActionStatus.Refused);
        outcome.ReasonCode.Should().Be(DecisionActionReasons.NotAuthorized);
        outcome.Written.Should().BeEmpty();
        writer.Created.Should().BeEmpty("a refused revise must not create the revision");
        user.Patches.Should().BeEmpty();
        user.Gets.Should().BeEmpty("the caller's right is asked before anything about the budget is read");
    }

    [Fact]
    public async Task ReviseBudget_BudgetOfAnotherMatter_WritesNothing()
    {
        var user = BudgetUser(OtherMatterId);
        var writer = new WriterHarness();
        var executor = new ReviseBudgetExecutor(Probe(CanWrite).Object, user, writer.Build(), new FakeTimeProvider(Now),
            NullLogger<ReviseBudgetExecutor>.Instance);

        var outcome = await executor.ExecuteAsync(Request(ReviseParams()), CancellationToken.None);

        outcome.Status.Should().Be(DecisionActionStatus.Refused);
        outcome.ReasonCode.Should().Be(DecisionActionReasons.BudgetNotOnMatter);
        writer.Created.Should().BeEmpty();
        user.Patches.Should().BeEmpty();
    }

    [Fact]
    public async Task ReviseBudget_UserCannotWriteTheAmount_StopsAndReportsTheRevision_WithoutAnotherIdentity()
    {
        var user = BudgetUser(MatterId);
        user.PatchStatus = 403;
        var writer = new WriterHarness();
        var executor = new ReviseBudgetExecutor(Probe(CanWrite).Object, user, writer.Build(), new FakeTimeProvider(Now),
            NullLogger<ReviseBudgetExecutor>.Instance);

        var outcome = await executor.ExecuteAsync(Request(ReviseParams()), CancellationToken.None);

        outcome.Status.Should().Be(DecisionActionStatus.Failed);
        outcome.ReasonCode.Should().Be(DecisionActionReasons.BudgetAmountNotWritten);
        outcome.Written.Should().Equal(new DecisionRecordRef("sprk_budgetrevision", writer.RevisionId));
        user.Patches.Should().ContainSingle("one attempt, as the user, no retry");
        writer.Org.Verify(o => o.UpdateAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ReviseBudget_WriterFails_NothingIsPatched()
    {
        var user = BudgetUser(MatterId);
        var writer = new WriterHarness { Throw = true };
        var executor = new ReviseBudgetExecutor(Probe(CanWrite).Object, user, writer.Build(), new FakeTimeProvider(Now),
            NullLogger<ReviseBudgetExecutor>.Instance);

        var outcome = await executor.ExecuteAsync(Request(ReviseParams()), CancellationToken.None);

        outcome.Status.Should().Be(DecisionActionStatus.Failed);
        outcome.Written.Should().BeEmpty();
        user.Patches.Should().BeEmpty("the amount follows the revision, never precedes it");
    }

    [Theory]
    [InlineData("budget", null)]
    [InlineData("budget", "not-a-guid")]
    [InlineData("amount", "-5")]
    [InlineData("amount", "abc")]
    [InlineData("reason", " ")]
    public async Task ReviseBudget_BadInput_RefusesBeforeAnyCall(string parameter, string? value)
    {
        var parameters = ReviseParams();
        parameters[parameter] = value;
        var probe = Probe(CanWrite);
        var user = BudgetUser(MatterId);
        var writer = new WriterHarness();
        var executor = new ReviseBudgetExecutor(probe.Object, user, writer.Build(), new FakeTimeProvider(Now),
            NullLogger<ReviseBudgetExecutor>.Instance);

        var outcome = await executor.ExecuteAsync(Request(parameters), CancellationToken.None);

        outcome.Status.Should().Be(DecisionActionStatus.Refused);
        outcome.ReasonCode.Should().Be(DecisionActionReasons.InvalidParameter);
        writer.Created.Should().BeEmpty();
        user.Gets.Should().BeEmpty();
        user.Patches.Should().BeEmpty();
    }

    // ---------------------------------------------------------------------------------------------------------------
    // approve-variance (D-19)
    // ---------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task ApproveVariance_IsRecordOnly_ItHasNothingToWriteWith()
    {
        typeof(ApproveVarianceExecutor).GetConstructors().Should().ContainSingle()
            .Which.GetParameters().Should().BeEmpty("record-only means no collaborator that could write a row");

        var outcome = await new ApproveVarianceExecutor().ExecuteAsync(
            Request(new Dictionary<string, string?> { ["amount"] = "5000" }), CancellationToken.None);

        outcome.Status.Should().Be(DecisionActionStatus.Done);
        outcome.Written.Should().BeEmpty();
    }

    // ---------------------------------------------------------------------------------------------------------------
    // Do lane
    // ---------------------------------------------------------------------------------------------------------------

    private static readonly DecisionRecordRef EventItem = new("sprk_event", ItemId);
    private static readonly DecisionRecordRef TodoItem = new("sprk_todo", ItemId);
    private static readonly DecisionRecordRef WorkAssignmentItem = new("sprk_workassignment", ItemId);

    [Fact]
    public async Task MarkComplete_Event_UsesTheEventsCompleteCore_AndReturnsTheEvent()
    {
        var cores = Cores();
        cores.Setup(c => c.CompleteEventAsync(It.IsAny<HttpContext>(), ItemId, It.IsAny<CancellationToken>())).ReturnsAsync(Reply(200));
        var executor = new MarkCompleteExecutor(Probe(CanWrite).Object, cores.Object, new FakeTimeProvider(Now));

        var outcome = await executor.ExecuteAsync(Request([], EventItem), CancellationToken.None);

        outcome.Status.Should().Be(DecisionActionStatus.Done);
        outcome.Written.Should().Equal(EventItem);
        cores.Verify(c => c.CompleteEventAsync(It.IsAny<HttpContext>(), ItemId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task MarkComplete_Todo_IsTheChildRecordsUpdate_WithTheSameColumnsAsTheTodoDetail()
    {
        var cores = Cores();
        JsonElement? sent = null;
        cores.Setup(c => c.UpdateChildAsync(It.IsAny<HttpContext>(), "sprk_todo", ItemId, It.IsAny<JsonElement>(), It.IsAny<CancellationToken>()))
            .Callback<HttpContext, string, Guid, JsonElement, CancellationToken>((_, _, _, body, _) => sent = body)
            .ReturnsAsync(Reply(204));
        var executor = new MarkCompleteExecutor(Probe(CanWrite).Object, cores.Object, new FakeTimeProvider(Now));

        var outcome = await executor.ExecuteAsync(Request([], TodoItem), CancellationToken.None);

        outcome.Status.Should().Be(DecisionActionStatus.Done);
        outcome.Written.Should().Equal(TodoItem);
        sent!.Value.GetProperty("statecode").GetInt32().Should().Be(1);
        sent.Value.GetProperty("statuscode").GetInt32().Should().Be(2);
        sent.Value.GetProperty("sprk_completedon").GetString().Should().Be("2026-10-09T12:00:00Z");
        sent.Value.EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo("statecode", "statuscode", "sprk_completedon");
    }

    [Theory]
    [InlineData("sprk_event")]
    [InlineData("sprk_todo")]
    public async Task MarkComplete_CallerWithoutWrite_WritesNothing(string entity)
    {
        var cores = Cores();
        var executor = new MarkCompleteExecutor(Probe(ReadOnly).Object, cores.Object, new FakeTimeProvider(Now));

        var outcome = await executor.ExecuteAsync(Request([], new DecisionRecordRef(entity, ItemId)), CancellationToken.None);

        outcome.Status.Should().Be(DecisionActionStatus.Refused);
        outcome.ReasonCode.Should().Be(DecisionActionReasons.NotAuthorized);
        cores.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task MarkComplete_WorkAssignment_IsNotSupported_AndWritesNothing()
    {
        var cores = Cores();
        var outcome = await new MarkCompleteExecutor(Probe(CanWrite).Object, cores.Object, new FakeTimeProvider(Now))
            .ExecuteAsync(Request([], WorkAssignmentItem), CancellationToken.None);

        outcome.Status.Should().Be(DecisionActionStatus.Refused);
        outcome.ReasonCode.Should().Be(DecisionActionReasons.SubjectUnsupported);
        cores.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task MarkComplete_TheCoreRefusesAnInvalidTransition_IsRefused_NotFailed()
    {
        var cores = Cores();
        cores.Setup(c => c.CompleteEventAsync(It.IsAny<HttpContext>(), ItemId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Reply(400, null, "Cannot complete event with status 'Closed'."));
        var outcome = await new MarkCompleteExecutor(Probe(CanWrite).Object, cores.Object, new FakeTimeProvider(Now))
            .ExecuteAsync(Request([], EventItem), CancellationToken.None);

        outcome.Status.Should().Be(DecisionActionStatus.Refused);
        outcome.Written.Should().BeEmpty();
    }

    [Fact]
    public async Task Reschedule_Event_WritesDueDateThroughTheNarrowEventWrite_NeverFinalDueDate()
    {
        var cores = Cores();
        UpdateEventDueAssigneeRequest? sent = null;
        cores.Setup(c => c.WriteEventDueAssigneeAsync(It.IsAny<HttpContext>(), ItemId, It.IsAny<UpdateEventDueAssigneeRequest>(), It.IsAny<CancellationToken>()))
            .Callback<HttpContext, Guid, UpdateEventDueAssigneeRequest, CancellationToken>((_, _, r, _) => sent = r)
            .ReturnsAsync(new EventDueAssigneeResult(EventDueAssigneeOutcome.Written));
        var executor = new RescheduleExecutor(Probe(CanWrite).Object, cores.Object);

        var outcome = await executor.ExecuteAsync(
            Request(new() { ["dueDate"] = "2026-11-02", ["reason"] = "Client asked" }, EventItem), CancellationToken.None);

        outcome.Status.Should().Be(DecisionActionStatus.Done);
        outcome.Written.Should().Equal(EventItem);
        sent.Should().Be(new UpdateEventDueAssigneeRequest(new DateOnly(2026, 11, 2), null));
    }

    [Fact]
    public async Task Reschedule_Todo_WritesOnlyTheDueDate_NeverFinalDueDate()
    {
        var cores = Cores();
        JsonElement? sent = null;
        cores.Setup(c => c.UpdateChildAsync(It.IsAny<HttpContext>(), "sprk_todo", ItemId, It.IsAny<JsonElement>(), It.IsAny<CancellationToken>()))
            .Callback<HttpContext, string, Guid, JsonElement, CancellationToken>((_, _, _, body, _) => sent = body)
            .ReturnsAsync(Reply(204));

        var outcome = await new RescheduleExecutor(Probe(CanWrite).Object, cores.Object).ExecuteAsync(
            Request(new() { ["dueDate"] = "2026-11-02", ["reason"] = "x" }, TodoItem), CancellationToken.None);

        outcome.Status.Should().Be(DecisionActionStatus.Done);
        sent!.Value.EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo("sprk_duedate");
        sent.Value.GetProperty("sprk_duedate").GetString().Should().Be("2026-11-02");
    }

    [Theory]
    [InlineData("11/02/2026")]
    [InlineData("2026-11-02T00:00:00Z")]
    [InlineData("")]
    [InlineData(null)]
    public async Task Reschedule_MalformedDate_RefusesBeforeAnyWrite(string? date)
    {
        var cores = Cores();
        var outcome = await new RescheduleExecutor(Probe(CanWrite).Object, cores.Object)
            .ExecuteAsync(Request(new() { ["dueDate"] = date }, EventItem), CancellationToken.None);

        outcome.Status.Should().Be(DecisionActionStatus.Refused);
        cores.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("sprk_event")]
    [InlineData("sprk_todo")]
    public async Task Reschedule_CallerWithoutWrite_WritesNothing(string entity)
    {
        var cores = Cores();
        var outcome = await new RescheduleExecutor(Probe(ReadOnly).Object, cores.Object)
            .ExecuteAsync(Request(new() { ["dueDate"] = "2026-11-02" }, new DecisionRecordRef(entity, ItemId)), CancellationToken.None);

        outcome.Status.Should().Be(DecisionActionStatus.Refused);
        outcome.ReasonCode.Should().Be(DecisionActionReasons.NotAuthorized);
        cores.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Reassign_Event_ReassignsToAContact_ThroughTheNarrowEventWrite()
    {
        var cores = Cores();
        UpdateEventDueAssigneeRequest? sent = null;
        cores.Setup(c => c.WriteEventDueAssigneeAsync(It.IsAny<HttpContext>(), ItemId, It.IsAny<UpdateEventDueAssigneeRequest>(), It.IsAny<CancellationToken>()))
            .Callback<HttpContext, Guid, UpdateEventDueAssigneeRequest, CancellationToken>((_, _, r, _) => sent = r)
            .ReturnsAsync(new EventDueAssigneeResult(EventDueAssigneeOutcome.Written));

        var outcome = await new ReassignExecutor(Probe(CanWrite).Object, cores.Object).ExecuteAsync(
            Request(new() { ["assignee"] = ContactId.ToString("D") }, EventItem), CancellationToken.None);

        outcome.Status.Should().Be(DecisionActionStatus.Done);
        sent.Should().Be(new UpdateEventDueAssigneeRequest(null, ContactId));
    }

    [Fact]
    public async Task Reassign_Todo_BindsTheAssigneeContact_AndNothingElse()
    {
        var cores = Cores();
        JsonElement? sent = null;
        cores.Setup(c => c.UpdateChildAsync(It.IsAny<HttpContext>(), "sprk_todo", ItemId, It.IsAny<JsonElement>(), It.IsAny<CancellationToken>()))
            .Callback<HttpContext, string, Guid, JsonElement, CancellationToken>((_, _, _, body, _) => sent = body)
            .ReturnsAsync(Reply(204));

        var outcome = await new ReassignExecutor(Probe(CanWrite).Object, cores.Object).ExecuteAsync(
            Request(new() { ["assignee"] = ContactId.ToString("D") }, TodoItem), CancellationToken.None);

        outcome.Status.Should().Be(DecisionActionStatus.Done);
        sent!.Value.EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo("sprk_assignedto@odata.bind");
        sent.Value.GetProperty("sprk_assignedto@odata.bind").GetString().Should().Be($"/contacts({ContactId:D})");
    }

    [Fact]
    public async Task Reassign_NoAssignee_Refuses_AndCallerWithoutWriteRefuses()
    {
        var cores = Cores();
        (await new ReassignExecutor(Probe(CanWrite).Object, cores.Object).ExecuteAsync(
            Request(new() { ["assignee"] = "nobody" }, EventItem), CancellationToken.None)).Status.Should().Be(DecisionActionStatus.Refused);
        (await new ReassignExecutor(Probe(ReadOnly).Object, cores.Object).ExecuteAsync(
            Request(new() { ["assignee"] = ContactId.ToString("D") }, EventItem), CancellationToken.None)).ReasonCode
            .Should().Be(DecisionActionReasons.NotAuthorized);
        cores.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ExtendResponseDate_WritesTheResponseDueDateAsTheCaller_OnTheWorkAssignment()
    {
        var user = new FakeUserClient();
        var outcome = await new ExtendResponseDateExecutor(Probe(CanWrite).Object, user).ExecuteAsync(
            Request(new() { ["responseDate"] = "2026-10-30", ["reason"] = "Awaiting counsel" }, WorkAssignmentItem), CancellationToken.None);

        outcome.Status.Should().Be(DecisionActionStatus.Done);
        outcome.Written.Should().Equal(WorkAssignmentItem);
        user.Patches.Should().ContainSingle();
        user.Patches[0].Path.Should().Be($"sprk_workassignments({ItemId:D})");
        JsonDocument.Parse(user.Patches[0].Body).RootElement.EnumerateObject().Select(p => p.Name)
            .Should().BeEquivalentTo("sprk_responseduedate");
    }

    [Fact]
    public async Task ExtendResponseDate_WithoutWrite_OrOnAnEvent_WritesNothing()
    {
        var user = new FakeUserClient();
        (await new ExtendResponseDateExecutor(Probe(ReadOnly).Object, user).ExecuteAsync(
            Request(new() { ["responseDate"] = "2026-10-30" }, WorkAssignmentItem), CancellationToken.None)).ReasonCode
            .Should().Be(DecisionActionReasons.NotAuthorized);
        (await new ExtendResponseDateExecutor(Probe(CanWrite).Object, user).ExecuteAsync(
            Request(new() { ["responseDate"] = "2026-10-30" }, EventItem), CancellationToken.None)).ReasonCode
            .Should().Be(DecisionActionReasons.SubjectUnsupported);
        user.Patches.Should().BeEmpty();
    }

    [Fact]
    public async Task RecordTheResponse_WritesTheD54ResponseColumns_AsTheCaller()
    {
        var user = new FakeUserClient();
        var cores = Cores();
        cores.Setup(c => c.TodayForCallerAsync(It.IsAny<HttpContext>(), It.IsAny<CancellationToken>())).ReturnsAsync(new DateOnly(2026, 10, 9));

        var outcome = await new RecordTheResponseExecutor(Probe(CanWrite).Object, user, cores.Object).ExecuteAsync(
            Request(new() { ["response"] = "delivered-on-the-matter", ["note"] = "Sent by courier" }, WorkAssignmentItem), CancellationToken.None);

        outcome.Status.Should().Be(DecisionActionStatus.Done);
        outcome.Written.Should().Equal(WorkAssignmentItem);
        user.Patches.Should().ContainSingle();
        user.Patches[0].Path.Should().Be($"sprk_workassignments({ItemId:D})");
        var body = JsonDocument.Parse(user.Patches[0].Body).RootElement;
        body.EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo("sprk_respondedon", "sprk_responseoutcome");
        body.GetProperty("sprk_respondedon").GetString().Should().Be("2026-10-09");
        body.GetProperty("sprk_responseoutcome").GetInt32().Should().Be(100000001);
    }

    [Theory]
    [InlineData("received-outside-spaarke", 100000000)]
    [InlineData("delivered-on-the-matter", 100000001)]
    [InlineData("no-longer-needed", 100000002)]
    public void RecordTheResponse_OutcomeCodes_MapToTheLiveOptionValues(string code, int value) =>
        RecordTheResponseExecutor.OutcomeValues[code].Should().Be(value);

    [Fact]
    public void RecordTheResponse_EveryCatalogOptionHasAValue()
    {
        DecisionActionCatalog.TryGet("record-the-response", out var definition).Should().BeTrue();
        var options = definition.Parameters.Single(p => p.Code == "response").Options!.Select(o => o.Value);
        options.Should().BeEquivalentTo(RecordTheResponseExecutor.OutcomeValues.Keys);
    }

    [Fact]
    public async Task RecordTheResponse_BadOutcome_WithoutWrite_OrOnAnEvent_WritesNothing()
    {
        var user = new FakeUserClient();
        var cores = Cores();
        var executor = (Func<AccessRights, RecordTheResponseExecutor>)(r => new RecordTheResponseExecutor(Probe(r).Object, user, cores.Object));

        (await executor(CanWrite).ExecuteAsync(Request(new() { ["response"] = "shrugged" }, WorkAssignmentItem), CancellationToken.None))
            .ReasonCode.Should().Be(DecisionActionReasons.InvalidParameter);
        (await executor(ReadOnly).ExecuteAsync(Request(new() { ["response"] = "no-longer-needed" }, WorkAssignmentItem), CancellationToken.None))
            .ReasonCode.Should().Be(DecisionActionReasons.NotAuthorized);
        (await executor(CanWrite).ExecuteAsync(Request(new() { ["response"] = "no-longer-needed" }, EventItem), CancellationToken.None))
            .ReasonCode.Should().Be(DecisionActionReasons.SubjectUnsupported);
        user.Patches.Should().BeEmpty();
    }

    // ---------------------------------------------------------------------------------------------------------------
    // Messages: send-reminder and send-email (#30)
    // ---------------------------------------------------------------------------------------------------------------

    private static Dictionary<string, string?> Message(string? to) => new()
    {
        ["to"] = to,
        ["subject"] = "Reminder: response due",
        ["body"] = "Please respond by Friday.",
    };

    private static Mock<DecisionRouteCores> SendingCores(List<SendCommunicationRequest> sent, Guid? communicationId = null)
    {
        var cores = Cores();
        cores.Setup(c => c.SendCommunicationAsync(It.IsAny<HttpContext>(), It.IsAny<SendCommunicationRequest>(), It.IsAny<CancellationToken>()))
            .Callback<HttpContext, SendCommunicationRequest, CancellationToken>((_, r, _) => sent.Add(r))
            .ReturnsAsync((Reply(200), communicationId ?? Guid.Parse("99999999-0000-0000-0000-000000000001")));
        return cores;
    }

    [Fact]
    public async Task SendReminder_TypedAddress_SendsOnce_AndReturnsTheCommunicationId()
    {
        var sent = new List<SendCommunicationRequest>();
        var cores = SendingCores(sent);
        var executor = new SendReminderExecutor(Probe(CanWrite | AccessRights.AppendTo).Object, new FakeUserClient(), cores.Object);

        var outcome = await executor.ExecuteAsync(Request(Message("counsel@firm.example"), WorkAssignmentItem), CancellationToken.None);

        outcome.Status.Should().Be(DecisionActionStatus.Done);
        sent.Should().ContainSingle().Which.To.Should().Equal("counsel@firm.example");
        outcome.Written.Should().ContainSingle().Which.Entity.Should().Be("sprk_communication");
    }

    [Fact]
    public async Task SendReminder_LawFirmId_ResolvesToThePrimaryContactWithAnEmailAddress()
    {
        var user = new FakeUserClient()
            .OnGet($"sprk_organizations({FirmId:D})", new Dictionary<string, object?>
            {
                ["_sprk_primarycontact1_value"] = "aaaaaaaa-0000-0000-0000-000000000001",
                ["_sprk_primarycontact2_value"] = "aaaaaaaa-0000-0000-0000-000000000002",
            })
            .OnGet("contacts(aaaaaaaa-0000-0000-0000-000000000002)", new Dictionary<string, object?> { ["emailaddress1"] = "second@firm.example" });
        var sent = new List<SendCommunicationRequest>();
        var executor = new SendReminderExecutor(Probe(CanWrite | AccessRights.AppendTo).Object, user, SendingCores(sent).Object);

        var outcome = await executor.ExecuteAsync(Request(Message(FirmId.ToString("D")), WorkAssignmentItem), CancellationToken.None);

        outcome.Status.Should().Be(DecisionActionStatus.Done);
        sent.Single().To.Should().Equal("second@firm.example");
    }

    [Fact]
    public async Task SendReminder_NoRecipientTyped_UsesTheSubjectsAssignedLawFirm()
    {
        var user = new FakeUserClient()
            .OnGet($"sprk_workassignments({ItemId:D})", new Dictionary<string, object?> { ["_sprk_assignedlawfirm1_value"] = FirmId.ToString("D") })
            .OnGet($"sprk_organizations({FirmId:D})", new Dictionary<string, object?> { ["_sprk_primarycontact1_value"] = ContactId.ToString("D") })
            .OnGet($"contacts({ContactId:D})", new Dictionary<string, object?> { ["emailaddress1"] = "lead@firm.example" });
        var sent = new List<SendCommunicationRequest>();
        var executor = new SendReminderExecutor(Probe(CanWrite | AccessRights.AppendTo).Object, user, SendingCores(sent).Object);

        var outcome = await executor.ExecuteAsync(Request(Message(null), WorkAssignmentItem), CancellationToken.None);

        outcome.Status.Should().Be(DecisionActionStatus.Done);
        sent.Single().To.Should().Equal("lead@firm.example");
    }

    [Fact]
    public async Task SendReminder_LawFirmWithNoContactEmail_RefusesAndSendsNothing()
    {
        var user = new FakeUserClient()
            .OnGet($"sprk_organizations({FirmId:D})", new Dictionary<string, object?> { ["_sprk_primarycontact1_value"] = ContactId.ToString("D") })
            .OnGet($"contacts({ContactId:D})", new Dictionary<string, object?> { ["emailaddress1"] = null });
        var sent = new List<SendCommunicationRequest>();
        var executor = new SendReminderExecutor(Probe(CanWrite | AccessRights.AppendTo).Object, user, SendingCores(sent).Object);

        var outcome = await executor.ExecuteAsync(Request(Message($"good@firm.example {FirmId:D}"), WorkAssignmentItem), CancellationToken.None);

        outcome.Status.Should().Be(DecisionActionStatus.Refused);
        outcome.ReasonCode.Should().Be(DecisionActionReasons.RecipientUnresolved);
        sent.Should().BeEmpty("one unresolvable recipient stops the whole send, including the address that was fine");
    }

    [Fact]
    public async Task SendReminder_CallerCannotAppendToTheCoreRecord_SendsNothing()
    {
        var sent = new List<SendCommunicationRequest>();
        var core = new DecisionCoreRecord("sprk_matter", MatterId, null, "Acme v. Roe");
        var executor = new SendReminderExecutor(Probe(ReadOnly).Object, new FakeUserClient(), SendingCores(sent).Object);

        var outcome = await executor.ExecuteAsync(Request(Message("a@b.example"), WorkAssignmentItem, core), CancellationToken.None);

        outcome.Status.Should().Be(DecisionActionStatus.Refused);
        outcome.ReasonCode.Should().Be(DecisionActionReasons.NotAuthorized);
        sent.Should().BeEmpty();
    }

    [Fact]
    public async Task SendEmail_FilesTheMessageAgainstTheCoreRecord_ForAnAuthorizedCaller()
    {
        var sent = new List<SendCommunicationRequest>();
        var core = new DecisionCoreRecord("sprk_matter", MatterId, null, "Acme v. Roe");
        var executor = new SendEmailExecutor(Probe(AccessRights.Read | AccessRights.AppendTo).Object, new FakeUserClient(), SendingCores(sent).Object);

        var outcome = await executor.ExecuteAsync(Request(Message("a@b.example"), null, core), CancellationToken.None);

        outcome.Status.Should().Be(DecisionActionStatus.Done);
        var request = sent.Single();
        request.Associations.Should().ContainSingle().Which.Should().Match<CommunicationAssociation>(a =>
            a.EntityType == "sprk_matter" && a.EntityId == MatterId);
        request.BodyFormat.Should().Be(BodyFormat.PlainText);
    }

    [Fact]
    public async Task SendEmail_TheSendRefuses_IsReportedWithoutAWrittenId()
    {
        var cores = Cores();
        cores.Setup(c => c.SendCommunicationAsync(It.IsAny<HttpContext>(), It.IsAny<SendCommunicationRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Reply(400, "INVALID_SENDER", "no"), (Guid?)null));
        var outcome = await new SendEmailExecutor(Probe(CanWrite | AccessRights.AppendTo).Object, new FakeUserClient(), cores.Object)
            .ExecuteAsync(Request(Message("a@b.example")), CancellationToken.None);

        outcome.Status.Should().Be(DecisionActionStatus.Refused);
        outcome.Written.Should().BeEmpty();
    }

    // ---------------------------------------------------------------------------------------------------------------
    // Next-step creators (#23)
    // ---------------------------------------------------------------------------------------------------------------

    private static readonly DecisionCoreRecord MatterCore = new("sprk_matter", MatterId, Guid.Parse("bbbbbbbb-0000-0000-0000-000000000001"), "Acme v. Roe");

    private static IGenericEntityService Entities()
    {
        var entities = new Mock<IGenericEntityService>();
        entities.Setup(e => e.GetEntitySetNameAsync("sprk_matter", It.IsAny<CancellationToken>())).ReturnsAsync("sprk_matters");
        return entities.Object;
    }

    [Fact]
    public async Task AddTodo_CreatesThroughTheChildRecordsCore_FiledUnderTheCoreRecord_AndReturnsTheId()
    {
        var created = Guid.Parse("cccccccc-0000-0000-0000-000000000001");
        var cores = Cores();
        string? table = null;
        JsonElement? sent = null;
        cores.Setup(c => c.CreateChildAsync(It.IsAny<HttpContext>(), It.IsAny<string>(), It.IsAny<JsonElement>(), It.IsAny<CancellationToken>()))
            .Callback<HttpContext, string, JsonElement, CancellationToken>((_, t, b, _) => { table = t; sent = b; })
            .ReturnsAsync(Reply(201, body: new { id = created }));

        var outcome = await new AddTodoExecutor(cores.Object, Entities()).ExecuteAsync(
            Request(new() { ["title"] = "Chase the revised budget", ["assignee"] = ContactId.ToString("D"), ["dueDate"] = "2026-10-20" }, null, MatterCore),
            CancellationToken.None);

        outcome.Status.Should().Be(DecisionActionStatus.Done);
        outcome.Written.Should().Equal(new DecisionRecordRef("sprk_todo", created));
        table.Should().Be("sprk_todo");
        var body = sent!.Value;
        body.GetProperty("sprk_name").GetString().Should().Be("Chase the revised budget");
        body.GetProperty("sprk_duedate").GetString().Should().Be("2026-10-20");
        body.GetProperty("sprk_assignedto@odata.bind").GetString().Should().Be($"/contacts({ContactId:D})");
        body.GetProperty("sprk_regardingmatter@odata.bind").GetString().Should().Be($"/sprk_matters({MatterId:D})");
        body.GetProperty("sprk_regardingrecordid").GetString().Should().Be(MatterId.ToString("D"));
        body.GetProperty("sprk_regardingrecordname").GetString().Should().Be("Acme v. Roe");
    }

    [Fact]
    public async Task CreateEvent_CreatesThroughTheChildRecordsCore_WithTheDateAsDueDate()
    {
        var created = Guid.Parse("cccccccc-0000-0000-0000-000000000002");
        var cores = Cores();
        string? table = null;
        JsonElement? sent = null;
        cores.Setup(c => c.CreateChildAsync(It.IsAny<HttpContext>(), It.IsAny<string>(), It.IsAny<JsonElement>(), It.IsAny<CancellationToken>()))
            .Callback<HttpContext, string, JsonElement, CancellationToken>((_, t, b, _) => { table = t; sent = b; })
            .ReturnsAsync(Reply(201, body: new { id = created }));

        var outcome = await new CreateEventExecutor(cores.Object, Entities()).ExecuteAsync(
            Request(new() { ["title"] = "Budget call", ["date"] = "2026-10-22", ["attendees"] = "A. Smith, B. Jones" }, null, MatterCore),
            CancellationToken.None);

        outcome.Status.Should().Be(DecisionActionStatus.Done);
        outcome.Written.Should().Equal(new DecisionRecordRef("sprk_event", created));
        table.Should().Be("sprk_event");
        sent!.Value.GetProperty("sprk_eventname").GetString().Should().Be("Budget call");
        sent.Value.GetProperty("sprk_duedate").GetString().Should().Be("2026-10-22");
        sent.Value.GetProperty("sprk_description").GetString().Should().Be("Attendees: A. Smith, B. Jones");
        sent.Value.GetProperty("sprk_regardingmatter@odata.bind").GetString().Should().Be($"/sprk_matters({MatterId:D})");
    }

    [Theory]
    [InlineData(403, DecisionActionReasons.NotAuthorized)]
    [InlineData(404, DecisionActionReasons.NotFound)]
    [InlineData(409, DecisionActionReasons.Conflict)]
    public async Task NextStep_TheCoreRefusesTheCaller_IsRefusedWithNoId(int status, string reason)
    {
        var cores = Cores();
        cores.Setup(c => c.CreateChildAsync(It.IsAny<HttpContext>(), It.IsAny<string>(), It.IsAny<JsonElement>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Reply(status, "child_record.denied", "no"));

        var outcome = await new AddTodoExecutor(cores.Object, Entities()).ExecuteAsync(
            Request(new() { ["title"] = "t", ["assignee"] = ContactId.ToString("D"), ["dueDate"] = "2026-10-20" }, null, MatterCore),
            CancellationToken.None);

        outcome.Status.Should().Be(DecisionActionStatus.Refused);
        outcome.ReasonCode.Should().Be(reason);
        outcome.Written.Should().BeEmpty();
    }

    [Fact]
    public async Task NextStep_BadInput_RefusesBeforeTheCore()
    {
        var cores = Cores();
        (await new AddTodoExecutor(cores.Object, Entities()).ExecuteAsync(
            Request(new() { ["title"] = "t", ["assignee"] = "x", ["dueDate"] = "2026-10-20" }), CancellationToken.None)).Status
            .Should().Be(DecisionActionStatus.Refused);
        (await new CreateEventExecutor(cores.Object, Entities()).ExecuteAsync(
            Request(new() { ["title"] = "t", ["date"] = "soon" }), CancellationToken.None)).Status
            .Should().Be(DecisionActionStatus.Refused);
        cores.VerifyNoOtherCalls();
    }

    // ---------------------------------------------------------------------------------------------------------------
    // The set as a whole
    // ---------------------------------------------------------------------------------------------------------------

    private static IDecisionActionExecutor[] AllExecutors()
    {
        var probe = Probe(CanWrite).Object;
        var user = new FakeUserClient();
        var cores = Cores().Object;
        var entities = Entities();
        return
        [
            new ReviseBudgetExecutor(probe, user, new WriterHarness().Build(), new FakeTimeProvider(Now), NullLogger<ReviseBudgetExecutor>.Instance),
            new ApproveVarianceExecutor(),
            new MarkCompleteExecutor(probe, cores, new FakeTimeProvider(Now)),
            new RescheduleExecutor(probe, cores),
            new ReassignExecutor(probe, cores),
            new SendReminderExecutor(probe, user, cores),
            new ExtendResponseDateExecutor(probe, user),
            new RecordTheResponseExecutor(probe, user, cores),
            new AddTodoExecutor(cores, entities),
            new CreateEventExecutor(cores, entities),
            new SendEmailExecutor(probe, user, cores),
        ];
    }

    [Fact]
    public void Registry_EveryCatalogActionHasAnExecutor_ExceptTheTwoOwnedByOtherTasks()
    {
        var registry = new DecisionActionExecutors(AllExecutors());

        registry.MissingCodes().Should().BeEmpty("a catalog action without an executor cannot be committed");
        DecisionActionExecutors.OwnedElsewhere.Should().BeEquivalentTo("send-budget-inquiry", "assign-work");
        foreach (var owned in DecisionActionExecutors.OwnedElsewhere)
            registry.TryGet(owned, out _).Should().BeFalse($"{owned} belongs to another task");
    }

    [Fact]
    public void Registry_RefusesADuplicateCode()
    {
        var executors = AllExecutors().Append(new ApproveVarianceExecutor());
        FluentActions.Invoking(() => new DecisionActionExecutors(executors)).Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Registry_RefusesACodeThatIsNotInTheCatalog()
    {
        var rogue = new Mock<IDecisionActionExecutor>();
        rogue.SetupGet(e => e.Code).Returns("delete-everything");
        FluentActions.Invoking(() => new DecisionActionExecutors([rogue.Object])).Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public async Task NoExecutor_EverNamesFinalDueDate()
    {
        // Every write body any executor produces is captured, then searched for the informational column (D-27/D-63).
        var bodies = new List<string>();
        var user = new FakeUserClient();
        var cores = Cores();
        cores.Setup(c => c.UpdateChildAsync(It.IsAny<HttpContext>(), It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<JsonElement>(), It.IsAny<CancellationToken>()))
            .Callback<HttpContext, string, Guid, JsonElement, CancellationToken>((_, _, _, b, _) => bodies.Add(b.GetRawText())).ReturnsAsync(Reply(204));
        cores.Setup(c => c.CreateChildAsync(It.IsAny<HttpContext>(), It.IsAny<string>(), It.IsAny<JsonElement>(), It.IsAny<CancellationToken>()))
            .Callback<HttpContext, string, JsonElement, CancellationToken>((_, _, b, _) => bodies.Add(b.GetRawText())).ReturnsAsync(Reply(201, body: new { id = ItemId }));
        cores.Setup(c => c.WriteEventDueAssigneeAsync(It.IsAny<HttpContext>(), It.IsAny<Guid>(), It.IsAny<UpdateEventDueAssigneeRequest>(), It.IsAny<CancellationToken>()))
            .Callback<HttpContext, Guid, UpdateEventDueAssigneeRequest, CancellationToken>((_, _, r, _) =>
                bodies.Add(EventDueAssigneeWrite.BuildPatchJson(r, ContactId, Now)))
            .ReturnsAsync(new EventDueAssigneeResult(EventDueAssigneeOutcome.Written));
        cores.Setup(c => c.CompleteEventAsync(It.IsAny<HttpContext>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(Reply(200));
        cores.Setup(c => c.TodayForCallerAsync(It.IsAny<HttpContext>(), It.IsAny<CancellationToken>())).ReturnsAsync(new DateOnly(2026, 10, 9));
        var probe = Probe(CanWrite | AccessRights.AppendTo).Object;

        var due = new Dictionary<string, string?> { ["dueDate"] = "2026-11-02", ["responseDate"] = "2026-11-02", ["date"] = "2026-11-02", ["assignee"] = ContactId.ToString("D"), ["title"] = "t", ["response"] = "no-longer-needed" };
        foreach (var subject in new[] { EventItem, TodoItem, WorkAssignmentItem })
        {
            await new RescheduleExecutor(probe, cores.Object).ExecuteAsync(Request(due, subject), CancellationToken.None);
            await new ReassignExecutor(probe, cores.Object).ExecuteAsync(Request(due, subject), CancellationToken.None);
            await new MarkCompleteExecutor(probe, cores.Object, new FakeTimeProvider(Now)).ExecuteAsync(Request(due, subject), CancellationToken.None);
            await new ExtendResponseDateExecutor(probe, user).ExecuteAsync(Request(due, subject), CancellationToken.None);
            await new RecordTheResponseExecutor(probe, user, cores.Object).ExecuteAsync(Request(due, subject), CancellationToken.None);
        }

        await new AddTodoExecutor(cores.Object, Entities()).ExecuteAsync(Request(due, null, MatterCore), CancellationToken.None);
        await new CreateEventExecutor(cores.Object, Entities()).ExecuteAsync(Request(due, null, MatterCore), CancellationToken.None);

        (bodies.Count + user.Patches.Count).Should().BeGreaterThan(8, "the sweep must actually have written something to search");
        bodies.Concat(user.Patches.Select(p => p.Body)).Should().NotContain(b => b.Contains("finalduedate", StringComparison.OrdinalIgnoreCase));
    }

    // ---------------------------------------------------------------------------------------------------------------
    // The narrow event write (#29) and the reply reader
    // ---------------------------------------------------------------------------------------------------------------

    [Fact]
    public void EventDueAssigneeWrite_Reschedule_WritesDueDateAndRescheduleAudit_Only()
    {
        var json = EventDueAssigneeWrite.BuildPatchJson(new UpdateEventDueAssigneeRequest(new DateOnly(2026, 11, 2), null), ContactId, Now);

        var names = JsonDocument.Parse(json).RootElement.EnumerateObject().Select(p => p.Name).ToList();
        names.Should().BeEquivalentTo("sprk_duedate", "sprk_rescheduleddate", "sprk_rescheduledby@odata.bind");
        JsonDocument.Parse(json).RootElement.GetProperty("sprk_duedate").GetString().Should().Be("2026-11-02");
    }

    [Fact]
    public void EventDueAssigneeWrite_Reassign_WritesContactStatusReassignedAndAudit()
    {
        var json = EventDueAssigneeWrite.BuildPatchJson(new UpdateEventDueAssigneeRequest(null, ContactId), FirmId, Now);

        var root = JsonDocument.Parse(json).RootElement;
        root.EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo(
            "sprk_assignedto@odata.bind", "statuscode", "sprk_reassigneddate", "sprk_reassignedby@odata.bind");
        root.GetProperty("sprk_assignedto@odata.bind").GetString().Should().Be($"/contacts({ContactId:D})");
        root.GetProperty("statuscode").GetInt32().Should().Be(659490007);
        root.GetProperty("sprk_reassignedby@odata.bind").GetString().Should().Be($"/contacts({FirmId:D})");
    }

    [Theory]
    [InlineData(659490001, true)]  // Open
    [InlineData(1, true)]          // Draft
    [InlineData(659490006, true)]  // On Hold
    [InlineData(659490007, true)]  // Reassigned
    [InlineData(659490002, false)] // Completed
    [InlineData(659490004, false)] // Cancelled
    public async Task EventDueAssigneeWrite_OnlyOpenWorkIsChanged(int statusCode, bool written)
    {
        var user = new FakeUserClient().OnGet("sprk_events(", new Dictionary<string, object?> { ["statuscode"] = statusCode });

        var result = await EventDueAssigneeWrite.ApplyAsync(
            user, ItemId, new UpdateEventDueAssigneeRequest(new DateOnly(2026, 11, 2), null), ContactId, Now, CancellationToken.None);

        result.IsWritten.Should().Be(written);
        user.Patches.Count.Should().Be(written ? 1 : 0);
        if (!written) result.Outcome.Should().Be(EventDueAssigneeOutcome.InvalidState);
    }

    [Fact]
    public async Task EventDueAssigneeWrite_UnreadableEvent_IsNotFound_AndNothingIsPatched()
    {
        var user = new FakeUserClient(); // every GET is a 404
        var result = await EventDueAssigneeWrite.ApplyAsync(
            user, ItemId, new UpdateEventDueAssigneeRequest(null, ContactId), ContactId, Now, CancellationToken.None);

        result.Outcome.Should().Be(EventDueAssigneeOutcome.NotFound);
        user.Patches.Should().BeEmpty();
    }

    [Fact]
    public async Task EventDueAssigneeWrite_ReadButNotWritable_IsDenied()
    {
        var user = new FakeUserClient { PatchStatus = 403 }.OnGet("sprk_events(", new Dictionary<string, object?> { ["statuscode"] = 659490001 });
        var result = await EventDueAssigneeWrite.ApplyAsync(
            user, ItemId, new UpdateEventDueAssigneeRequest(new DateOnly(2026, 11, 2), null), null, Now, CancellationToken.None);

        result.Outcome.Should().Be(EventDueAssigneeOutcome.Denied);
    }

    [Fact]
    public void EventDueAssigneeWrite_ShapeNeedsADateOrAnAssignee()
    {
        EventDueAssigneeWrite.ShapeProblem(null).Should().NotBeNull();
        EventDueAssigneeWrite.ShapeProblem(new UpdateEventDueAssigneeRequest(null, null)).Should().NotBeNull();
        EventDueAssigneeWrite.ShapeProblem(new UpdateEventDueAssigneeRequest(null, Guid.Empty)).Should().NotBeNull();
        EventDueAssigneeWrite.ShapeProblem(new UpdateEventDueAssigneeRequest(new DateOnly(2026, 11, 2), null)).Should().BeNull();
        EventDueAssigneeWrite.ShapeProblem(new UpdateEventDueAssigneeRequest(null, ContactId)).Should().BeNull();
    }

    [Fact]
    public async Task RouteReply_ReadsStatusReasonAndCreatedId_FromTheShippedHandlersResults()
    {
        var http = new DefaultHttpContext { RequestServices = new ServiceCollection().AddLogging().BuildServiceProvider() };
        var created = Guid.Parse("dddddddd-0000-0000-0000-000000000001");

        var ok = await DecisionRouteCores.ReadAsync(Results.Created("/x", new { id = created }), http);
        ok.IsSuccess.Should().BeTrue();
        ok.CreatedId.Should().Be(created);

        var problem = await DecisionRouteCores.ReadAsync(
            Results.Problem(statusCode: 403, detail: "no", extensions: new Dictionary<string, object?> { ["reasonCode"] = "child_record.denied" }), http);
        problem.Status.Should().Be(403);
        problem.ReasonCode.Should().Be("child_record.denied");
        problem.Detail.Should().Be("no");

        (await DecisionRouteCores.ReadAsync(Results.NoContent(), http)).Status.Should().Be(204);
    }
}
