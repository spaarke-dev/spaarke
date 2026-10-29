using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Xrm.Sdk;
using Moq;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.Signals;
using Sprk.Bff.Api.Services.Signals.Actions;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Signals;

/// <summary>
/// Ontology task 153 (D-145b): <see cref="DecisionRecordWriter.WriteConditionClearedAsync"/> writes ONE Routine Decision Record for a
/// Do-lane Signal the nightly pass closed as ConditionCleared, naming (best effort) the person who last changed the item. Keyed by
/// <see cref="ConditionClearedRecordKey"/> (Signal, episode) in <c>sprk_steps.reviewId</c>, as 149's gate key is, with a different prefix.
/// The harness evaluates the lookup's conditions against what it holds (task 149's harness).
/// </summary>
public sealed partial class DecisionRecordWriterTests
{
    private static readonly Guid ClosedSignal = Guid.Parse("153c1ea2-0000-4000-8000-000000000001");
    private static readonly Guid Colleague = Guid.Parse("153c1ea2-0000-4000-8000-0000000000c2");
    private static readonly DateTime SignalRaised = new(2026, 10, 1, 3, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime SignalClosed = new(2026, 10, 10, 3, 0, 0, DateTimeKind.Utc);

    /// <summary>A no-core To Do last changed by <paramref name="by"/> at <paramref name="on"/>; the user's eligibility columns come back
    /// through the "changedby" link as aliased values (absent: the link found no user).</summary>
    private static Harness ChangedToDo(Guid by, DateTime on, Entity? user = null, IRecordOwnershipResolver? realResolver = null)
    {
        var h = new Harness(realResolver: realResolver);
        var todo = new Entity("sprk_todo", TodoId)
        {
            ["modifiedby"] = new EntityReference("systemuser", by),
            ["modifiedon"] = on,
        };
        foreach (var attribute in (user ?? PersonRow(by)).Attributes)
            todo[$"changedby.{attribute.Key}"] = new AliasedValue("systemuser", attribute.Key, attribute.Value);
        h.Rows[("sprk_todo", TodoId)] = todo;
        return h;
    }

    private static Entity PersonRow(Guid id) => new("systemuser", id)
    {
        ["isdisabled"] = false,
        ["accessmode"] = new OptionSetValue(0),
        ["sprk_isexternal"] = false,
    };

    private static ConditionClearedRecordRequest Cleared(int episode = 1) => new(
        ClosedSignal, episode, "POL-TODO-OVERDUE", PolicyVersion, new DecisionRecordRef("sprk_todo", TodoId), Core: null,
        ClosedOn: SignalClosed, SignalRaisedOn: SignalRaised);

    private static JsonElement StepsJson(Entity record) => JsonDocument.Parse(record.GetAttributeValue<string>("sprk_steps")).RootElement;

    [Fact]
    public async Task ConditionCleared_ChangedByAColleagueWhileOpen_IsOneRoutineRecordConfirmedByThem_KeyedOnTheSignalAndEpisode()
    {
        var h = ChangedToDo(Colleague, SignalClosed.AddDays(-1));

        var result = await h.Writer.WriteConditionClearedAsync(Cleared());

        result.AlreadyRecorded.Should().BeFalse();
        result.ConfirmedByUserId.Should().Be(Colleague);
        result.RecordClass.Should().Be(RecordedDecisionClass.Routine);
        Option(h.Row, "sprk_recordclass").Should().Be(100000001, "Routine");
        Option(h.Row, "sprk_decisionoutcome").Should().Be(100000000, "Authorized: the work was done");
        h.Row.GetAttributeValue<EntityReference>("sprk_confirmedby").Id.Should().Be(Colleague);
        h.Row.GetAttributeValue<EntityReference>("sprk_regardingtodo").Id.Should().Be(TodoId, "filed under the closed Signal's subject");
        h.Row.GetAttributeValue<string>("sprk_reason").Should().StartWith("condition_cleared - ");
        var steps = StepsJson(h.Row);
        steps.GetProperty("reviewId").GetString().Should().Be(ConditionClearedRecordKey.IdOf(ClosedSignal, 1).ToString("D"));
        steps.GetProperty("resolvedSignalIds").EnumerateArray().Select(s => s.GetString()).Should().Equal(ClosedSignal.ToString("D"));
        h.Row.GetAttributeValue<string>("sprk_steps").Should().Contain("\"code\":\"condition-cleared\"");
    }

    [Fact]
    public async Task ConditionCleared_TwiceForTheSameSignalAndEpisode_WritesOneRecord_AndTheSecondReportsItsConfirmer()
    {
        var h = ChangedToDo(Colleague, SignalClosed.AddDays(-1));

        var first = await h.Writer.WriteConditionClearedAsync(Cleared());
        var second = await h.Writer.WriteConditionClearedAsync(Cleared());

        h.Created.Should().ContainSingle("D-139: the second call finds the first call's record by its key");
        second.AlreadyRecorded.Should().BeTrue();
        second.RecordId.Should().Be(first.RecordId);
        second.ConfirmedByUserId.Should().Be(Colleague, "the stored confirmer, so the Signal can still be linked to the person");
        h.OwnerAsks.Should().ContainSingle("a repeat stops at the lookup");
    }

    [Fact]
    public async Task ConditionCleared_AnotherEpisode_IsItsOwnRecord()
    {
        var h = ChangedToDo(Colleague, SignalClosed.AddDays(-1));

        await h.Writer.WriteConditionClearedAsync(Cleared(episode: 1));
        var next = await h.Writer.WriteConditionClearedAsync(Cleared(episode: 2));

        next.AlreadyRecorded.Should().BeFalse();
        h.Created.Should().HaveCount(2);
    }

    [Fact]
    public void ConditionCleared_Key_HasItsOwnPrefix_SoItNeverEqualsTheGateKey()
    {
        ConditionClearedRecordKey.Of(ClosedSignal, 1).Should().StartWith("cleared-dr-");
        GateDecisionKey.Of(ClosedSignal, authorize: true).Should().StartWith("gate-dr-");
        ConditionClearedRecordKey.IdOf(ClosedSignal, 1).Should().NotBe(GateDecisionKey.IdOf(ClosedSignal, authorize: true))
            .And.NotBe(GateDecisionKey.IdOf(ClosedSignal, authorize: false));
    }

    [Fact]
    public async Task ConditionCleared_ARecordWithTheSameReviewIdButNotWrittenByThisEntryPoint_IsNotThisClosuresRecord()
    {
        // A review's client-chosen review id could equal the key; the lookup also requires the condition-cleared step, which a review's
        // record can never carry (its steps are catalog codes).
        var h = ChangedToDo(Colleague, SignalClosed.AddDays(-1));
        h.Seeded.Add(new Entity("sprk_decisionrecord", Guid.NewGuid())
        {
            ["sprk_regardingtodo"] = new EntityReference("sprk_todo", TodoId),
            ["sprk_steps"] = $"{{\"reviewId\":\"{ConditionClearedRecordKey.IdOf(ClosedSignal, 1):D}\",\"steps\":[{{\"code\":\"mark-complete\"}}]}}",
            ["createdby"] = new EntityReference("systemuser", WriterUser), // a review's record: the writer creates those too
        });

        var result = await h.Writer.WriteConditionClearedAsync(Cleared());

        result.AlreadyRecorded.Should().BeFalse();
        h.Created.Should().ContainSingle();
    }

    public static TheoryData<string> UnknownPersons => new()
    {
        "applicationUser", "disabledUser", "noUserRow", "changedAfterTheClose", "changedBeforeTheSignalWasRaised", "readFaulted",
    };

    [Theory]
    [MemberData(nameof(UnknownPersons))]
    public async Task ConditionCleared_WhenThePersonIsUnknown_IsStillOneRecord_WithNoConfirmer_AndTheReasonSaysWhy(string why)
    {
        var on = why switch
        {
            "changedAfterTheClose" => SignalClosed.AddMinutes(5),
            "changedBeforeTheSignalWasRaised" => SignalRaised.AddDays(-1),
            _ => SignalClosed.AddDays(-1),
        };
        Entity? user = why switch
        {
            "applicationUser" => new Entity("systemuser", Colleague)
            {
                ["isdisabled"] = false,
                ["accessmode"] = new OptionSetValue(4),
                ["applicationid"] = Guid.NewGuid(),
            },
            "disabledUser" => new Entity("systemuser", Colleague) { ["isdisabled"] = true, ["accessmode"] = new OptionSetValue(0) },
            "noUserRow" => new Entity("systemuser", Colleague),
            _ => null,
        };
        var h = ChangedToDo(Colleague, on, user);
        if (why == "readFaulted")
        {
            // Only the attribution read (the subject table) faults; the key lookup on sprk_decisionrecord answers.
            h.Entities.Setup(e => e.RetrieveMultipleAsync(
                    It.Is<Microsoft.Xrm.Sdk.Query.QueryExpression>(q => q.EntityName == "sprk_todo"), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new TimeoutException("dataverse"));
        }

        var result = await h.Writer.WriteConditionClearedAsync(Cleared());

        result.ConfirmedByUserId.Should().BeNull();
        h.Created.Should().ContainSingle();
        h.Row.Contains("sprk_confirmedby").Should().BeFalse("nobody is guessed: not the assignee, not the owner");
        h.Row.GetAttributeValue<string>("sprk_reason").Should().StartWith("condition_cleared_person_unknown - ");
        Option(h.Row, "sprk_recordclass").Should().Be(100000001);
    }

    [Fact]
    public async Task ConditionCleared_ARecordWithNoConfirmerUnderACommunication_IsNotFoundByTheGatesLookup()
    {
        // 149's gate lookup filters the communication, its key and sprk_confirmedby Null. A condition-cleared record whose person is unknown
        // also has no confirmer; filed under the same communication it must still not satisfy the gate's lookup (its key differs).
        var h = MatterFiledCommunication(); // the communication row names no modifiedby: the person is unknown
        var cleared = await h.Writer.WriteConditionClearedAsync(Cleared() with { Subject = new DecisionRecordRef("sprk_communication", CommunicationId) });
        h.Row.Contains("sprk_confirmedby").Should().BeFalse("precondition: no confirmer, like a gate record");

        var gate = await h.Writer.WriteGateDecisionAsync(Gate(authorize: true));

        gate.AlreadyRecorded.Should().BeFalse();
        h.Created.Should().HaveCount(2, "the gate decision is written as its own record");
        cleared.AlreadyRecorded.Should().BeFalse();
    }

    [Fact]
    public async Task ConditionCleared_WithNoConfirmer_IsOwnedLikeAReview_NeverThroughTheGatesCommunicationRule()
    {
        // Before task 153 the gate was recognized by an empty confirmer; a record whose person is unknown also has none, and must not be
        // asked as the content of a communication (KeepCreatorUnlessTargetIsTeamOwned), whose Unchanged answer takes the D-144 path.
        var h = ChangedToDo(Colleague, SignalClosed.AddDays(-1), new Entity("systemuser", Colleague) { ["isdisabled"] = true });

        await h.Writer.WriteConditionClearedAsync(Cleared());

        var asked = h.OwnerAsks.Should().ContainSingle().Subject;
        asked.KeepCreatorUnlessTargetIsTeamOwned.Should().BeFalse();
        asked.Parents.Should().Contain(new RecordOwnershipParent("sprk_todo", TodoId));
    }

    [Fact]
    public async Task ConditionCleared_UnderASecureProject_IsASecureChild_WithNoCreatedByPerson_TheColleagueOnlyInConfirmedBy()
    {
        // Adversarial pass 1 F3: sprk_createdbyperson feeds uac-r2's "the creator may move a secure child out" rule. The nightly job writes
        // this record for nobody, so it names no creator person (RecordCreatorPerson: background-job writers leave it empty); the person
        // who did the work is named only in the record's attribution, sprk_confirmedby.
        var project = Guid.NewGuid();
        var directory = Tests.TestInfrastructure.OwnershipDirectory.Standard()
            .WithSecureRoot("sprk_project", project)
            .WithRecord("sprk_todo", TodoId, Tests.TestInfrastructure.OwnershipDirectory.GeneralBu,
                extra: new() { ["sprk_regardingproject"] = new EntityReference("sprk_project", project) });
        var h = ChangedToDo(Colleague, SignalClosed.AddDays(-1), realResolver: directory.Resolver());
        h.ReadBackTeam = Tests.TestInfrastructure.OwnershipDirectory.SecureNamedTeam;
        h.Rows[("sprk_todo", TodoId)]["sprk_regardingproject"] = new EntityReference("sprk_project", project);

        var result = await h.Writer.WriteConditionClearedAsync(Cleared());

        h.Row.GetAttributeValue<EntityReference>("ownerid").Id.Should().Be(Tests.TestInfrastructure.OwnershipDirectory.SecureNamedTeam,
            "precondition: a secure child, owned as the other entry points own it");
        result.SecureOwnerTeamId.Should().Be(Tests.TestInfrastructure.OwnershipDirectory.SecureNamedTeam);
        h.Row.Contains(RecordCreatorPerson.Column).Should().BeFalse("the job asked for nobody: no creator person on a condition-cleared record");
        h.Row.GetAttributeValue<EntityReference>("sprk_confirmedby").Id.Should().Be(Colleague, "the person is named in the attribution only");
    }

    [Fact]
    public async Task ConditionCleared_WhenTheSubjectNoLongerExists_IsRefusedAsAPermanentSkip_NotAFailure_AndNothingIsWritten()
    {
        // Adversarial pass 1 F1: the attribution read by id finds no row (the To Do was deleted). The record can never be filed under it, so
        // the refusal is a counted skip (Warning, ontology.writer.skips), never ontology.writer.failures, and no filing or owner read is made.
        var h = new Harness();

        var act = () => h.Writer.WriteConditionClearedAsync(Cleared());

        (await act.Should().ThrowAsync<DecisionRecordRefusedException>())
            .Which.Reason.Should().Be(DecisionRecordRefusalReason.ConditionClearedSubjectMissing);
        DecisionRecordWriter.ConditionClearedSkipTag(DecisionRecordRefusalReason.ConditionClearedSubjectMissing)
            .Should().Be(Sprk.Bff.Api.Telemetry.OntologyWriterFailureReason.ClosureRecordSkippedSubjectMissing);
        h.Created.Should().BeEmpty();
        h.OwnerAsks.Should().BeEmpty();
        h.Log.Entries.Should().ContainSingle(e => e.EventId == DecisionRecordEvents.ConditionClearedSkipped).Which.Level.Should().Be(LogLevel.Warning);
        h.Log.Entries.Should().NotContain(e => e.Level >= LogLevel.Error, "a permanent skip is not a failure");
    }
}
