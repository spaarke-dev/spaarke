using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Sprk.Bff.Api.Services.Signals;
using Xunit;
using static Sprk.Bff.Api.Tests.TestInfrastructure.OwnershipDirectory;

namespace Sprk.Bff.Api.Tests.Services.Signals;

/// <summary>
/// Ontology task 149 (D-139): <see cref="DecisionRecordWriter.WriteGateDecisionAsync"/> writes exactly ONE Decision Record per
/// (communication, gate outcome). The record's key is deterministic from those two values (<see cref="GateDecisionKey"/>), stored in
/// <c>sprk_steps.reviewId</c> (an existing column: no schema change), and looked up before the create. The harness evaluates the lookup's
/// conditions against what it holds, and a record it created is found by the next lookup, as Dataverse would.
/// </summary>
public sealed partial class DecisionRecordWriterTests
{
    /// <summary>A communication filed under a matter, with the mocked resolver (so <c>OwnerAsks</c> counts every owner question).</summary>
    private static Harness MatterFiledCommunication()
    {
        var h = new Harness();
        h.Rows[("sprk_communication", CommunicationId)] = new Entity("sprk_communication", CommunicationId)
        {
            ["sprk_regardingmatter"] = new EntityReference("sprk_matter", MatterId),
        };
        return h;
    }

    private static string StepsOf(Entity record) => record.GetAttributeValue<string>("sprk_steps");

    private static string ReviewIdOf(Entity record) =>
        JsonDocument.Parse(StepsOf(record)).RootElement.GetProperty("reviewId").GetString()!;

    /// <summary>A Decision Record that already exists for <paramref name="communication"/> under <paramref name="key"/> (Dataverse holds it; this harness did not create it).</summary>
    private static Entity ExistingRecord(Guid id, Guid communication, Guid key) => new("sprk_decisionrecord", id)
    {
        ["sprk_regardingcommunication"] = new EntityReference("sprk_communication", communication),
        ["sprk_steps"] = $"{{\"reviewId\":\"{key:D}\",\"resolvedSignalIds\":[],\"steps\":[]}}",
        ["createdby"] = new EntityReference("systemuser", WriterUser), // the writer created it (adversarial pass 2 F1)
    };

    [Fact]
    public async Task Gate_TheSameDecisionTwice_WritesExactlyOneRecord_AndTheSecondReportsTheExistingOne()
    {
        var h = MatterFiledCommunication();

        var first = await h.Writer.WriteGateDecisionAsync(Gate(authorize: true));
        var second = await h.Writer.WriteGateDecisionAsync(Gate(authorize: true));

        h.Created.Should().ContainSingle("the second call finds the first call's record and writes nothing");
        first.AlreadyRecorded.Should().BeFalse();
        second.AlreadyRecorded.Should().BeTrue();
        second.RecordId.Should().Be(first.RecordId, "the existing record is reported, not a new one");
        h.OwnerAsks.Should().ContainSingle("a repeat resolves no owner: it stopped at the lookup");
    }

    [Fact]
    public async Task Gate_ARetryWithANewRunId_IsTheSameDecision_NotASecondRecord()
    {
        // CommunicationRuleGate makes a new evaluation id on every run, so an enrichment retry carries a different one. The key is the
        // (communication, outcome) pair and nothing else.
        var h = MatterFiledCommunication();

        await h.Writer.WriteGateDecisionAsync(Gate(authorize: false) with { EvaluationId = Guid.NewGuid() });
        var retry = await h.Writer.WriteGateDecisionAsync(Gate(authorize: false) with { EvaluationId = Guid.NewGuid() });

        h.Created.Should().ContainSingle();
        retry.AlreadyRecorded.Should().BeTrue();
    }

    [Fact]
    public async Task Gate_ADifferentOutcomeForTheSameCommunication_IsItsOwnRecord()
    {
        // A deny, then an authorize after a rule change: two decisions, two records, two keys.
        var h = MatterFiledCommunication();

        var denied = await h.Writer.WriteGateDecisionAsync(Gate(authorize: false, reason: "rule-matched-confidence-below-threshold"));
        var authorized = await h.Writer.WriteGateDecisionAsync(Gate(authorize: true));

        h.Created.Should().HaveCount(2);
        denied.AlreadyRecorded.Should().BeFalse();
        authorized.AlreadyRecorded.Should().BeFalse();
        ReviewIdOf(h.Created[0]).Should().Be(GateDecisionKey.IdOf(CommunicationId, authorize: false).ToString("D"));
        ReviewIdOf(h.Created[1]).Should().Be(GateDecisionKey.IdOf(CommunicationId, authorize: true).ToString("D"));
        ReviewIdOf(h.Created[0]).Should().NotBe(ReviewIdOf(h.Created[1]));
    }

    [Fact]
    public async Task Gate_AFirstDecision_StoresTheDeterministicKeyAsTheReviewId_NotTheRunId()
    {
        var h = MatterFiledCommunication();

        await h.Writer.WriteGateDecisionAsync(Gate(authorize: true));

        ReviewIdOf(h.Row).Should().Be(GateDecisionKey.IdOf(CommunicationId, authorize: true).ToString("D"));
        ReviewIdOf(h.Row).Should().NotBe(GateEvaluation.ToString("D"));
    }

    [Fact]
    public async Task Gate_TheLookup_AsksForTheCommunicationsRecordCarryingTheDecisionKey_ThroughTheSharedReader()
    {
        var h = MatterFiledCommunication();

        await h.Writer.WriteGateDecisionAsync(Gate(authorize: true));

        var query = h.Lookups.Should().ContainSingle().Subject;
        query.EntityName.Should().Be("sprk_decisionrecord");
        query.TopCount.Should().Be(1);
        query.Criteria.Conditions.Should().Contain(c =>
            c.AttributeName == "sprk_regardingcommunication" && c.Operator == ConditionOperator.Equal && c.Values.Single().Equals(CommunicationId),
            "narrowed by the indexed lookup the record is filed under");
        query.Criteria.Conditions.Should().Contain(c =>
            c.AttributeName == "sprk_steps" && c.Operator == ConditionOperator.Like
            && ((string)c.Values.Single()).Contains($"\"reviewId\":\"{GateDecisionKey.IdOf(CommunicationId, true):D}\""),
            "and by the key in sprk_steps, the way DecisionCommitService finds a review's record");
        query.Criteria.Conditions.Should().Contain(c => c.AttributeName == "sprk_confirmedby" && c.Operator == ConditionOperator.Null,
            "and only a record no person confirmed: a gate record has no confirmer, a human review's always does");
        query.Criteria.Conditions.Should().Contain(c =>
            c.AttributeName == "createdby" && c.Operator == ConditionOperator.Equal && c.Values.Single().Equals(WriterUser),
            "adversarial pass 2 F1: and only a record the writer itself created");
        query.Criteria.Conditions.Should().HaveCount(4);
    }

    [Fact]
    public async Task Gate_AnExistingRecordOwnedByAnotherTeam_IsFound_AndNothingIsWritten()
    {
        // The lookup is the shared reader's (any team's record is visible to it), so a record a business-unit or secure team owns is found.
        var h = MatterFiledCommunication();
        var existing = Guid.NewGuid();
        h.Seeded.Add(ExistingRecord(existing, CommunicationId, GateDecisionKey.IdOf(CommunicationId, authorize: true)));

        var result = await h.Writer.WriteGateDecisionAsync(Gate(authorize: true));

        result.AlreadyRecorded.Should().BeTrue();
        result.RecordId.Should().Be(existing);
        h.Created.Should().BeEmpty();
        h.OwnerAsks.Should().BeEmpty();
        h.Log.Entries.Should().NotContain(e => e.Level >= LogLevel.Warning && e.EventId.Id != 0);
    }

    [Theory]
    [InlineData("otherCommunication")]
    [InlineData("otherOutcome")]
    [InlineData("reviewWithTheSameKey")]
    public async Task Gate_ARecordForAnotherCommunicationOrAnotherOutcome_IsNotThisDecisionsRecord(string which)
    {
        // Both halves of the key matter: a record keyed for the other outcome, or filed under another communication, does not satisfy
        // this decision (a lookup that dropped either condition would call it a duplicate and lose the record). Nor does a human
        // review's record that carries a client-chosen review id equal to the key: it has a confirmer, a gate record never does.
        var h = MatterFiledCommunication();
        var key = GateDecisionKey.IdOf(CommunicationId, authorize: true);
        var seeded = which switch
        {
            "otherCommunication" => ExistingRecord(Guid.NewGuid(), Guid.NewGuid(), key),
            "otherOutcome" => ExistingRecord(Guid.NewGuid(), CommunicationId, GateDecisionKey.IdOf(CommunicationId, authorize: false)),
            _ => ExistingRecord(Guid.NewGuid(), CommunicationId, key),
        };
        if (which == "reviewWithTheSameKey")
        {
            seeded["sprk_confirmedby"] = new EntityReference("systemuser", Guid.NewGuid());
        }

        h.Seeded.Add(seeded);

        var result = await h.Writer.WriteGateDecisionAsync(Gate(authorize: true));

        result.AlreadyRecorded.Should().BeFalse();
        h.Created.Should().ContainSingle();
    }

    [Fact]
    public async Task Gate_ALookupThatFaults_WritesNothing_RefusesWithItsOwnReason_AndMetersTheFailure()
    {
        // Guessing "none" on a read fault would risk a second record. A refusal is retryable (the job is redelivered).
        var scope = Guid.NewGuid();
        MetricScope.Value = scope;
        var (listener, reasons) = ListenReasons(scope);
        using var _ = listener;
        var h = MatterFiledCommunication();
        h.LookupFault = new TimeoutException("dataverse");

        var act = () => h.Writer.WriteGateDecisionAsync(Gate());

        var thrown = await act.Should().ThrowAsync<DecisionRecordRefusedException>();
        thrown.Which.Reason.Should().Be(DecisionRecordRefusalReason.GateRecordLookupFailed);
        thrown.Which.InnerException.Should().BeOfType<TimeoutException>();
        h.Created.Should().BeEmpty();
        h.OwnerAsks.Should().BeEmpty("the lookup precedes the owner question");
        reasons.Should().Equal(DecisionRecordRefusalReason.GateRecordLookupFailed);
        h.Log.Entries.Should().ContainSingle(e => e.EventId == DecisionRecordEvents.WriteRefused).Which.Level.Should().Be(LogLevel.Error);
    }

    [Fact]
    public async Task Gate_ACreateThatTimedOutButLanded_IsFoundOnTheRetry_NotWrittenTwice()
    {
        // CreateFailed documents that "after a timeout the row may exist". The retry's lookup is what keeps it from being a duplicate.
        var h = MatterFiledCommunication();
        var attempt = 0;
        h.Org.Setup(o => o.CreateAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>()))
            .Returns<Entity, CancellationToken>((entity, _) =>
            {
                h.Created.Add(entity); // the row lands...
                return ++attempt == 1 ? Task.FromException<Guid>(new TimeoutException("response lost")) : Task.FromResult(h.NewId);
            });

        var firstAttempt = () => h.Writer.WriteGateDecisionAsync(Gate());
        (await firstAttempt.Should().ThrowAsync<DecisionRecordRefusedException>()).Which.Reason.Should().Be(DecisionRecordRefusalReason.CreateFailed);
        var retry = await h.Writer.WriteGateDecisionAsync(Gate());

        retry.AlreadyRecorded.Should().BeTrue();
        h.Created.Should().ContainSingle("one row exists: the retry created nothing");
    }

    [Fact]
    public void GateDecisionKey_IsDeterministic_AndDiffersByCommunicationAndOutcomeOnly()
    {
        var other = Guid.NewGuid();

        GateDecisionKey.IdOf(CommunicationId, true).Should().Be(GateDecisionKey.IdOf(CommunicationId, true));
        GateDecisionKey.IdOf(CommunicationId, true).Should().NotBe(GateDecisionKey.IdOf(CommunicationId, false));
        GateDecisionKey.IdOf(CommunicationId, true).Should().NotBe(GateDecisionKey.IdOf(other, true));
        GateDecisionKey.IdOf(CommunicationId, true).Should().NotBe(Guid.Empty);
        GateDecisionKey.Of(CommunicationId, true).Should().Be($"gate-dr-{CommunicationId:D}-authorized");
        GateDecisionKey.Of(CommunicationId, false).Should().Be($"gate-dr-{CommunicationId:D}-denied");
    }
}
