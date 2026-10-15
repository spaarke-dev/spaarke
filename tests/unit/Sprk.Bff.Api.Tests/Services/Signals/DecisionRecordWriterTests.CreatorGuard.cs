using FluentAssertions;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Sprk.Bff.Api.Services.Signals;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Signals;

/// <summary>
/// Ontology task 153, adversarial pass 2 F1: the writer's two "already recorded" lookups (task 149's gate lookup and 153's condition-cleared
/// lookup) match only rows the ontology writer itself created (<c>createdby</c> = the writer's own user, WhoAmI on its connection). A Console
/// user holds Deep create on <c>sprk_decisionrecord</c> and the keys are computable, so without that condition a user's row carrying a key
/// would stop the genuine record being written (and, for a closure, launder its confirmer onto the Signal). uac-r2's reconciler re-owns
/// records (ownerid), which leaves <c>createdby</c> alone, so a re-owned record is still found.
/// </summary>
public sealed partial class DecisionRecordWriterTests
{
    private static readonly Guid ConsoleUser = Guid.Parse("153f0123-0000-4000-8000-0000000000aa");
    private static readonly Guid Framed = Guid.Parse("153f0123-0000-4000-8000-0000000000bb");

    /// <summary>A row carrying the closure's key and step code, created by <paramref name="createdBy"/>.</summary>
    private static Entity KeyedClosureRow(Guid createdBy, Guid? confirmedBy = null)
    {
        var row = new Entity("sprk_decisionrecord", Guid.NewGuid())
        {
            ["sprk_regardingtodo"] = new EntityReference("sprk_todo", TodoId),
            ["sprk_steps"] = $"{{\"reviewId\":\"{ConditionClearedRecordKey.IdOf(ClosedSignal, 1):D}\",\"steps\":[{{\"code\":\"condition-cleared\"}}]}}",
            ["createdby"] = new EntityReference("systemuser", createdBy),
        };
        if (confirmedBy is { } person) row["sprk_confirmedby"] = new EntityReference("systemuser", person);
        return row;
    }

    [Fact]
    public async Task ConditionCleared_AUserCreatedRowWithTheKeyAndTheCode_IsIgnored_TheGenuineRecordIsWritten_NamingTheRealPerson()
    {
        var h = ChangedToDo(Colleague, SignalClosed.AddDays(-1));
        var forged = KeyedClosureRow(createdBy: ConsoleUser, confirmedBy: Framed);
        h.Seeded.Add(forged);

        var result = await h.Writer.WriteConditionClearedAsync(Cleared());

        result.AlreadyRecorded.Should().BeFalse("a row a person created is never the closure's record");
        result.RecordId.Should().NotBe(forged.Id);
        result.ConfirmedByUserId.Should().Be(Colleague, "the person who changed the To Do, not the forged row's confirmer");
        h.Row.GetAttributeValue<EntityReference>("sprk_confirmedby").Id.Should().Be(Colleague);
        h.Lookups.Where(q => q.EntityName == "sprk_decisionrecord").Should().ContainSingle().Which.Criteria.Conditions.Should().Contain(c =>
            c.AttributeName == "createdby" && c.Operator == ConditionOperator.Equal && c.Values.Single().Equals(WriterUser));
    }

    [Fact]
    public async Task ConditionCleared_AWriterCreatedRecordReOwnedToATeam_IsStillFound_AndNothingIsWritten()
    {
        // uac-r2's SecureChildReconciler re-owns Decision Records (ownerid to the Secure team or the communication owner). createdby stays.
        var h = ChangedToDo(Colleague, SignalClosed.AddDays(-1));
        var existing = KeyedClosureRow(createdBy: WriterUser, confirmedBy: Colleague);
        existing["ownerid"] = new EntityReference("team", SecureTeam);
        existing["owningteam"] = new EntityReference("team", SecureTeam);
        h.Seeded.Add(existing);

        var result = await h.Writer.WriteConditionClearedAsync(Cleared());

        result.AlreadyRecorded.Should().BeTrue();
        result.RecordId.Should().Be(existing.Id);
        result.ConfirmedByUserId.Should().Be(Colleague);
        h.Created.Should().BeEmpty();
    }

    [Fact]
    public async Task Gate_AUserCreatedRowWithTheKeyAndNoConfirmer_DoesNotBlockTheGatesRecord()
    {
        var h = MatterFiledCommunication();
        var forged = ExistingRecord(Guid.NewGuid(), CommunicationId, GateDecisionKey.IdOf(CommunicationId, authorize: true));
        forged["createdby"] = new EntityReference("systemuser", ConsoleUser);
        h.Seeded.Add(forged);

        var result = await h.Writer.WriteGateDecisionAsync(Gate(authorize: true));

        result.AlreadyRecorded.Should().BeFalse("a row a person created, even with no confirmer, is never the gate's record");
        result.RecordId.Should().NotBe(forged.Id);
        h.Created.Should().ContainSingle();
    }

    [Fact]
    public async Task Gate_AWriterCreatedRecordReOwnedToATeam_IsStillFound_AndNothingIsWritten()
    {
        var h = MatterFiledCommunication();
        var existing = ExistingRecord(Guid.NewGuid(), CommunicationId, GateDecisionKey.IdOf(CommunicationId, authorize: true));
        existing["ownerid"] = new EntityReference("team", SecureTeam);
        existing["owningteam"] = new EntityReference("team", SecureTeam);
        h.Seeded.Add(existing);

        var result = await h.Writer.WriteGateDecisionAsync(Gate(authorize: true));

        result.AlreadyRecorded.Should().BeTrue();
        result.RecordId.Should().Be(existing.Id);
        h.Created.Should().BeEmpty();
    }

    [Theory]
    [InlineData("gate")]
    [InlineData("conditionCleared")]
    public async Task ALookup_WhenTheWritersIdentityCannotBeRead_RefusesAndWritesNothing_NeverMatchingOnAnythingWeaker(string which)
    {
        var h = which == "gate" ? MatterFiledCommunication() : ChangedToDo(Colleague, SignalClosed.AddDays(-1));
        h.WhoAmIFault = new TimeoutException("whoami");

        Func<Task> act = which == "gate"
            ? () => h.Writer.WriteGateDecisionAsync(Gate(authorize: true))
            : () => h.Writer.WriteConditionClearedAsync(Cleared());

        var thrown = await act.Should().ThrowAsync<DecisionRecordRefusedException>();
        thrown.Which.Reason.Should().Be(which == "gate"
            ? DecisionRecordRefusalReason.GateRecordLookupFailed
            : DecisionRecordRefusalReason.ConditionClearedLookupFailed);
        thrown.Which.InnerException.Should().BeOfType<TimeoutException>();
        h.Lookups.Should().BeEmpty("no lookup runs without the writer's identity");
        h.Created.Should().BeEmpty();
    }

    [Fact]
    public async Task TheWritersIdentity_IsAskedOnce_AndCachedForTheConnectionsLife()
    {
        var h = ChangedToDo(Colleague, SignalClosed.AddDays(-1));

        await h.Writer.WriteConditionClearedAsync(Cleared(episode: 1));
        await h.Writer.WriteConditionClearedAsync(Cleared(episode: 2));
        await h.Writer.WriteConditionClearedAsync(Cleared(episode: 2));

        h.WhoAmICalls.Should().Be(1);
    }

    [Theory]
    [InlineData("""{"steps":[{"code":"send-reminder","taken":true},{"code":"mark-complete","taken":false}]}""", null, true)]
    [InlineData("""{"steps":[{"code":"send-budget-inquiry","taken":true}]}""", null, true)]
    [InlineData("""{"steps":[{"code":"send-reminder","taken":true},{"code":"mark-complete","taken":true}]}""", null, false)]
    [InlineData("""{"steps":[{"code":"mark-complete","taken":true}]}""", null, false)]
    [InlineData("""{"steps":[{"code":"send-reminder","taken":false}]}""", null, false)]
    [InlineData("""{"steps":[{"code":"send-reminder"}]}""", null, false)]
    [InlineData("""{"steps":[{"code":"condition-cleared","taken":true}]}""", null, false)]
    [InlineData("""{"steps":[]}""", null, false)]
    [InlineData("""{"steps":[]}""", "[]", false)]
    [InlineData("not json", null, false)]
    [InlineData(null, null, false)]
    // uac-r2 re-check K2: a review that took no action but created a Next step (DecisionCommitService's InquirySent) leaves the item open.
    [InlineData("""{"steps":[{"code":"mark-complete","taken":false}]}""", """[{"actionCode":"add-todo","entity":"sprk_todo","id":"c0430000-0000-4000-8000-000000000001"}]""", true)]
    [InlineData("""{"steps":[{"code":"mark-complete","taken":false}]}""", """[{"actionCode":"add-todo","entity":null,"id":null}]""", true)]
    [InlineData("""{"steps":[{"code":"mark-complete","taken":true}]}""", """[{"actionCode":"add-todo","entity":null,"id":null}]""", false)]
    [InlineData("""{"steps":[{"code":"mark-complete","taken":false}]}""", "not json", false)]
    public void IsNonResolvingReviewRecord_IsTrueOnlyForARecordThatTookNothingButActionsAndNextStepsLeavingTheItemOpen(string? steps, string? followOns, bool expected) =>
        DecisionRecordWriter.IsNonResolvingReviewRecord(steps, followOns).Should().Be(expected);

    [Theory]
    [InlineData(new string[0], 0, false)]
    [InlineData(new string[0], 1, true)]
    [InlineData(new[] { "send-reminder" }, 0, true)]
    [InlineData(new[] { "send-reminder" }, 2, true)]
    [InlineData(new[] { "mark-complete" }, 0, false)]
    [InlineData(new[] { "send-reminder", "mark-complete" }, 1, false)]
    public void LeavesSignalOpen_IsTheOneRuleBehindTheCommitsInquirySent_AndTheRetrysNonResolvingLink(string[] taken, int followOns, bool expected) =>
        DecisionRecordWriter.LeavesSignalOpen(taken, followOns).Should().Be(expected);
}
