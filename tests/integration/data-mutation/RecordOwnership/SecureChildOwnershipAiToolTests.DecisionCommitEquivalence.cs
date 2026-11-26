using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Moq;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Services.Signals;
using Xunit;

namespace Sprk.Bff.Api.Tests.Integration.DataMutation.RecordOwnership;

/// <summary>
/// ISS-010 (#3184): what the four-action decision commit DOES, pinned before any latency fix, so a fix that only asks a read
/// fewer times is proven to change nothing else: the writes (order and payloads, the Decision Record last), the response with
/// its warnings, the replay, a refusal before and after a write, and a fault before and after a write (never an allow). Ids are
/// replaced by tokens in order of first appearance. The world is <see cref="BuildFourActionWorld"/>.
/// </summary>
/// <remarks>
/// When a pinned value changes on purpose, set <c>ISS010_PIN_OUT</c> to a file path, run these tests, review the new values
/// there and paste them here; a latency fix must not need to.
/// </remarks>
public sealed partial class SecureChildOwnershipAiToolTests
{
    private static void Pinned(string name, string actual, string expected)
    {
        if (Environment.GetEnvironmentVariable("ISS010_PIN_OUT") is { Length: > 0 } path)
        {
            File.AppendAllText(path, $"== {name}\n{actual}\n== end\n");
            return; // recording new values, not checking them
        }
        actual.ReplaceLineEndings("\n").Should().Be(expected.ReplaceLineEndings("\n"), $"ISS-010 equivalence: {name} must not change");
    }

    private static string EmptyWrites => string.Empty;

    [Fact]
    public async Task Iss010_Equivalence_Recorded_SameWritesInOrder_SameDecisionRecordLast_SameResponseAndWarnings()
    {
        var w = BuildFourActionWorld();
        // The follow-up's send step answers a not-sent outcome: the warning reaches the response unchanged.
        w.Rig.FollowUps.Setup(f => f.NotifyAssigneesAsync(It.IsAny<HttpContext>(), It.IsAny<IReadOnlyList<AssignTaskNotice>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { "assignee_email_not_sent" });

        var result = await w.CommitAsync();

        result.Status.Should().Be(DecisionCommitStatus.Recorded, result.Detail);
        Pinned("recorded writes", w.Profile.WriteTranscript(), PinRecordedWrites);
        Pinned("recorded response", Normalized(result), PinRecordedResponse);
    }

    [Fact]
    public async Task Iss010_Equivalence_ASecondCommitWithTheSameReviewId_WritesNothing_AndAnswersTheSame()
    {
        var w = BuildFourActionWorld();
        var first = await w.CommitAsync();
        first.Status.Should().Be(DecisionCommitStatus.Recorded, first.Detail);
        w.Profile.Clear();

        var replay = await w.CommitAsync();

        w.Profile.WriteTranscript().Should().BeEmpty("a replay writes nothing");
        Pinned("replay response", Normalized(replay), PinReplayResponse);
    }

    [Fact]
    public async Task Iss010_Equivalence_AnAccessCheckSaysNoBeforeAnyWrite_IsRefused_AndWritesNothing()
    {
        var w = BuildFourActionWorld();
        w.Probe.NotHeld.Add("prvCreatesprk_WorkAssignment"); // the last step's preflight, before any write

        var result = await w.CommitAsync();

        result.Status.Should().NotBe(DecisionCommitStatus.Recorded);
        Pinned("refused-before writes", w.Profile.WriteTranscript(), EmptyWrites);
        Pinned("refused-before response", Normalized(result), PinRefusedBeforeResponse);
    }

    [Fact]
    public async Task Iss010_Equivalence_AnAccessCheckSaysNoAfterThreeWrites_IsRefused_WithTheLandedWritesListed_AndNoRecord()
    {
        var w = BuildFourActionWorld();
        w.User.Held.Remove("prvCreatesprk_workassignment"); // the create core's own check of the fourth step, after three creates

        var result = await w.CommitAsync();

        result.Status.Should().NotBe(DecisionCommitStatus.Recorded);
        w.Rig.Records.Should().BeEmpty("no Decision Record when a step is refused");
        Pinned("refused-after writes", w.Profile.WriteTranscript(), PinRefusedAfterWrites);
        Pinned("refused-after response", Normalized(result), PinRefusedAfterResponse);
    }

    [Fact]
    public async Task Iss010_Equivalence_ACheckThatFaultsBeforeAnyWrite_IsNeverAnAllow()
    {
        var w = BuildFourActionWorld();
        w.Probe.Faults.Add("prvCreatesprk_Todo");

        var result = await w.CommitAsync();

        result.Status.Should().NotBe(DecisionCommitStatus.Recorded);
        Pinned("fault-before writes", w.Profile.WriteTranscript(), EmptyWrites);
        Pinned("fault-before response", Normalized(result), PinFaultBeforeResponse);
    }

    [Fact]
    public async Task Iss010_Equivalence_ACheckThatFaultsAfterThreeWrites_IsNeverAnAllow()
    {
        var w = BuildFourActionWorld();
        w.User.PrivilegesFault = ("prvCreatesprk_workassignment", 503);

        var result = await w.CommitAsync();

        result.Status.Should().NotBe(DecisionCommitStatus.Recorded);
        w.Rig.Records.Should().BeEmpty();
        Pinned("fault-after writes", w.Profile.WriteTranscript(), PinFaultAfterWrites);
        Pinned("fault-after response", Normalized(result), PinFaultAfterResponse);
    }

    [Fact]
    public async Task Iss010_Equivalence_ATableMetadataReadThatFaults_FailsTheStep_AndIsNotRemembered()
    {
        var w = BuildFourActionWorld();
        var faulted = 0;
        w.User.GetOverride = path =>
            path.StartsWith("EntityDefinitions(LogicalName='sprk_budget')", StringComparison.Ordinal) && Interlocked.Increment(ref faulted) == 1
                ? DataverseUserResponse.Fail(503, DataverseUserClientErrorCodes.ServiceError, "Scripted fault.")
                : null;

        var result = await w.CommitAsync();

        result.Status.Should().NotBe(DecisionCommitStatus.Recorded);
        Pinned("metadata-fault writes", w.Profile.WriteTranscript(), EmptyWrites);
        Pinned("metadata-fault response", Normalized(result), PinMetadataFaultResponse);
    }

    private const string PinRecordedWrites = """
        app-only writer (IFieldMappingDataverseService) UpdateRecordFieldsAsync sprk_budget sprk_budget <id1> {ownerid@odata.bind=/teams(<id2>);sprk_CreatedByPerson@odata.bind=/systemusers(<id3>);sprk_Matter@odata.bind="/sprk_matters(<id4>)";sprk_currency="EUR";sprk_name="Budget 2026-10-11";sprk_totalbudget=90000;transactioncurrencyid@odata.bind="/transactioncurrencies(<id5>)"} null
        app-only writer (IFieldMappingDataverseService) UpdateRecordFieldsAsync sprk_event sprk_event <id6> {ownerid@odata.bind=/teams(<id2>);sprk_AssignedTo@odata.bind="/contacts(<id7>)";sprk_CreatedByPerson@odata.bind=/systemusers(<id3>);sprk_EventType_Ref@odata.bind="/sprk_eventtype_refs(<id8>)";sprk_RegardingMatter@odata.bind="/sprk_matters(<id4>)";sprk_RegardingRecordType@odata.bind="/sprk_recordtype_refs(<id9>)";sprk_description="Call the client about the budget";sprk_duedate="2026-11-15";sprk_eventname="Call the client about the budget";sprk_regardingrecordid="<id4>";statuscode=659490001} null
        app-only writer (IFieldMappingDataverseService) UpdateRecordFieldsAsync sprk_todo sprk_todo <id10> {ownerid@odata.bind=/teams(<id2>);sprk_AssignedTo@odata.bind="/contacts(<id7>)";sprk_CreatedByPerson@odata.bind=/systemusers(<id3>);sprk_RegardingMatter@odata.bind="/sprk_matters(<id4>)";sprk_RegardingRecordType@odata.bind="/sprk_recordtype_refs(<id9>)";sprk_duedate="2026-10-20";sprk_name="Chase the revision";sprk_regardingrecordid="<id4>"} null
        app-only writer (IFieldMappingDataverseService) UpdateRecordFieldsAsync sprk_workassignment sprk_workassignment <id11> {ownerid@odata.bind=/teams(<id2>);sprk_AssignedTo@odata.bind="/contacts(<id7>)";sprk_AssignedToInternal@odata.bind="/contacts(<id7>)";sprk_CreatedByPerson@odata.bind=/systemusers(<id3>);sprk_RegardingMatter@odata.bind="/sprk_matters(<id4>)";sprk_RegardingRecordType@odata.bind="/sprk_recordtype_refs(<id9>)";sprk_description="Review the budget with the firm";sprk_name="Review the budget with the firm";sprk_regardingrecordid="<id4>"} null
        ontology writer (IOrganizationServiceAsync2) CreateAsync sprk_decisionrecord sprk_decisionrecord(<id12>){ownerid=team:<id13>;sprk_actioncode=create-budget;sprk_assignedtointernal=contact:<id14>;sprk_confirmedby=systemuser:<id3>;sprk_corerecordid=<id4>;sprk_corerecordtype=sprk_recordtype_ref:<id9>;sprk_decidedon=10/11/2026 12:00:00;sprk_decisionoutcome=100000000;sprk_factsnapshot={"facts":{"<id15>":{"variancePct":12.5}},"decidedBy":{"role":null},"because":"The matter never had a budget"};sprk_followons=[{"actionCode":"add-todo","entity":"sprk_todo","id":"<id10>"},{"actionCode":"assign-work","entity":"sprk_workassignment","id":"<id11>"}];sprk_gatetier=Tier3;sprk_matter=sprk_matter:<id4>;sprk_name=Judgement: Create budget, Assign Task;sprk_policyversion=sprk_policyversion:<id16>;sprk_privilegeflagged=False;sprk_proposedaction=Create budget; Assign Task;sprk_recordclass=100000000;sprk_regardingmatter=sprk_matter:<id4>;sprk_regardingrecordid=<id4>;sprk_regardingrecordname=;sprk_regardingrecordtype=sprk_recordtype_ref:<id17>;sprk_regardingrecordurl=/main.aspx?pagetype=entityrecord&etn=sprk_matter&id=<id4>;sprk_steps={"reviewId":"<id18>","reviewHash":"16442D3F47AA830AB5ACE53EEEAB11CAEA2BFFFCC7216FF051F3839CEC15496B","resolvedSignalIds":["<id15>"],"steps":[{"code":"create-budget","label":"Create budget","taken":true,"values":{"amount":"90000","reason":"No budget was ever set"},"outcome":"Done","written":[{"entity":"sprk_budget","id":"<id1>"}]},{"code":"assign-task","label":"Assign Task","taken":true,"values":{"assignee":"<id7>","description":"Call the client about the budget","dueDate":"2026-11-15","notify":"true"},"outcome":"Done","written":[{"entity":"sprk_event","id":"<id6>"}]}]}}
        ontology writer (IOrganizationServiceAsync2) Update sprk_signal Update{ConcurrencyBehavior=IfRowVersionMatches;Target=sprk_signal(<id15>){sprk_decisionrecord=sprk_decisionrecord:<id19>;sprk_lastevaluated=10/11/2026 12:00:00;sprk_resolutiontype=100000000;sprk_resolvedby=systemuser:<id3>;sprk_resolvedon=10/11/2026 12:00:00;sprk_signalstatus=100000002}}
        """;
    private const string PinRecordedResponse = """
        {"Status":0,"ReviewId":"<id1>","ReasonCode":null,"Detail":null,"ActionCode":null,"DecisionRecordId":"<id2>","RecordClass":"Judgement","GateTier":"Tier3","Steps":[{"Key":"action:create-budget","Code":"create-budget","Phase":2,"Status":"Done","Written":[{"Entity":"sprk_budget","Id":"<id3>"}],"PossiblyWritten":[],"ReasonCode":null,"Detail":null,"Warning":null},{"Key":"action:assign-task","Code":"assign-task","Phase":2,"Status":"Done","Written":[{"Entity":"sprk_event","Id":"<id4>"}],"PossiblyWritten":[],"ReasonCode":null,"Detail":null,"Warning":null},{"Key":"next-step:add-todo#0","Code":"add-todo","Phase":3,"Status":"Done","Written":[{"Entity":"sprk_todo","Id":"<id5>"}],"PossiblyWritten":[],"ReasonCode":null,"Detail":null,"Warning":null},{"Key":"next-step:assign-work#1","Code":"assign-work","Phase":3,"Status":"Done","Written":[{"Entity":"sprk_workassignment","Id":"<id6>"}],"PossiblyWritten":[],"ReasonCode":null,"Detail":null,"Warning":null}],"FollowOns":[{"Entity":"sprk_todo","Id":"<id5>"},{"Entity":"sprk_workassignment","Id":"<id6>"}],"Signals":[{"SignalId":"<id7>","State":"Acted","Warning":null}],"FailedStep":null,"Landed":[],"PossiblyLanded":[],"RecordMayExist":false,"SentButNotRecorded":false,"Resumable":false,"SendChoiceRequired":false,"Warnings":["assignee_email_not_sent"],"Replayed":false}
        """;
    private const string PinReplayResponse = """
        {"Status":0,"ReviewId":"<id1>","ReasonCode":null,"Detail":null,"ActionCode":null,"DecisionRecordId":"<id2>","RecordClass":"Judgement","GateTier":"Tier3","Steps":[{"Key":"action:create-budget","Code":"create-budget","Phase":2,"Status":"Done","Written":[{"Entity":"sprk_budget","Id":"<id3>"}],"PossiblyWritten":[],"ReasonCode":null,"Detail":null,"Warning":null},{"Key":"action:assign-task","Code":"assign-task","Phase":2,"Status":"Done","Written":[{"Entity":"sprk_event","Id":"<id4>"}],"PossiblyWritten":[],"ReasonCode":null,"Detail":null,"Warning":null},{"Key":"next-step:add-todo#0","Code":"add-todo","Phase":3,"Status":"Done","Written":[{"Entity":"sprk_todo","Id":"<id5>"}],"PossiblyWritten":[],"ReasonCode":null,"Detail":null,"Warning":null},{"Key":"next-step:assign-work#1","Code":"assign-work","Phase":3,"Status":"Done","Written":[{"Entity":"sprk_workassignment","Id":"<id6>"}],"PossiblyWritten":[],"ReasonCode":null,"Detail":null,"Warning":null}],"FollowOns":[{"Entity":"sprk_todo","Id":"<id5>"},{"Entity":"sprk_workassignment","Id":"<id6>"}],"Signals":[{"SignalId":"<id7>","State":"Acted","Warning":null}],"FailedStep":null,"Landed":[],"PossiblyLanded":[],"RecordMayExist":false,"SentButNotRecorded":false,"Resumable":false,"SendChoiceRequired":false,"Warnings":[],"Replayed":true}
        """;
    private const string PinRefusedBeforeResponse = """
        {"Status":2,"ReviewId":"<id1>","ReasonCode":"decision.action.not_authorized","Detail":"You do not have permission to create this.","ActionCode":"assign-work","DecisionRecordId":null,"RecordClass":null,"GateTier":null,"Steps":[],"FollowOns":[],"Signals":[],"FailedStep":null,"Landed":[],"PossiblyLanded":[],"RecordMayExist":false,"SentButNotRecorded":false,"Resumable":false,"SendChoiceRequired":false,"Warnings":[],"Replayed":false}
        """;
    private const string PinRefusedAfterWrites = """
        app-only writer (IFieldMappingDataverseService) UpdateRecordFieldsAsync sprk_budget sprk_budget <id1> {ownerid@odata.bind=/teams(<id2>);sprk_CreatedByPerson@odata.bind=/systemusers(<id3>);sprk_Matter@odata.bind="/sprk_matters(<id4>)";sprk_currency="EUR";sprk_name="Budget 2026-10-11";sprk_totalbudget=90000;transactioncurrencyid@odata.bind="/transactioncurrencies(<id5>)"} null
        app-only writer (IFieldMappingDataverseService) UpdateRecordFieldsAsync sprk_event sprk_event <id6> {ownerid@odata.bind=/teams(<id2>);sprk_AssignedTo@odata.bind="/contacts(<id7>)";sprk_CreatedByPerson@odata.bind=/systemusers(<id3>);sprk_EventType_Ref@odata.bind="/sprk_eventtype_refs(<id8>)";sprk_RegardingMatter@odata.bind="/sprk_matters(<id4>)";sprk_RegardingRecordType@odata.bind="/sprk_recordtype_refs(<id9>)";sprk_description="Call the client about the budget";sprk_duedate="2026-11-15";sprk_eventname="Call the client about the budget";sprk_regardingrecordid="<id4>";statuscode=659490001} null
        app-only writer (IFieldMappingDataverseService) UpdateRecordFieldsAsync sprk_todo sprk_todo <id10> {ownerid@odata.bind=/teams(<id2>);sprk_AssignedTo@odata.bind="/contacts(<id7>)";sprk_CreatedByPerson@odata.bind=/systemusers(<id3>);sprk_RegardingMatter@odata.bind="/sprk_matters(<id4>)";sprk_RegardingRecordType@odata.bind="/sprk_recordtype_refs(<id9>)";sprk_duedate="2026-10-20";sprk_name="Chase the revision";sprk_regardingrecordid="<id4>"} null
        """;
    private const string PinRefusedAfterResponse = """
        {"Status":6,"ReviewId":"<id1>","ReasonCode":"decision.action.not_authorized","Detail":"You do not have permission to create records in \u0027sprk_workassignment\u0027 (prvCreatesprk_workassignment).","ActionCode":"assign-work","DecisionRecordId":null,"RecordClass":null,"GateTier":"Tier3","Steps":[{"Key":"action:create-budget","Code":"create-budget","Phase":2,"Status":"Done","Written":[{"Entity":"sprk_budget","Id":"<id2>"}],"PossiblyWritten":[],"ReasonCode":null,"Detail":null,"Warning":null},{"Key":"action:assign-task","Code":"assign-task","Phase":2,"Status":"Done","Written":[{"Entity":"sprk_event","Id":"<id3>"}],"PossiblyWritten":[],"ReasonCode":null,"Detail":null,"Warning":null},{"Key":"next-step:add-todo#0","Code":"add-todo","Phase":3,"Status":"Done","Written":[{"Entity":"sprk_todo","Id":"<id4>"}],"PossiblyWritten":[],"ReasonCode":null,"Detail":null,"Warning":null},{"Key":"next-step:assign-work#1","Code":"assign-work","Phase":3,"Status":"Refused","Written":[],"PossiblyWritten":[],"ReasonCode":"decision.action.not_authorized","Detail":"You do not have permission to create records in \u0027sprk_workassignment\u0027 (prvCreatesprk_workassignment).","Warning":null}],"FollowOns":[],"Signals":[],"FailedStep":3,"Landed":[{"Entity":"sprk_budget","Id":"<id2>"},{"Entity":"sprk_event","Id":"<id3>"},{"Entity":"sprk_todo","Id":"<id4>"}],"PossiblyLanded":[],"RecordMayExist":false,"SentButNotRecorded":false,"Resumable":true,"SendChoiceRequired":false,"Warnings":[],"Replayed":false}
        """;
    private const string PinFaultBeforeResponse = """
        {"Status":2,"ReviewId":"<id1>","ReasonCode":"decision.action.not_authorized","Detail":"You do not have permission to create this.","ActionCode":"add-todo","DecisionRecordId":null,"RecordClass":null,"GateTier":null,"Steps":[],"FollowOns":[],"Signals":[],"FailedStep":null,"Landed":[],"PossiblyLanded":[],"RecordMayExist":false,"SentButNotRecorded":false,"Resumable":false,"SendChoiceRequired":false,"Warnings":[],"Replayed":false}
        """;
    private const string PinFaultAfterWrites = """
        app-only writer (IFieldMappingDataverseService) UpdateRecordFieldsAsync sprk_budget sprk_budget <id1> {ownerid@odata.bind=/teams(<id2>);sprk_CreatedByPerson@odata.bind=/systemusers(<id3>);sprk_Matter@odata.bind="/sprk_matters(<id4>)";sprk_currency="EUR";sprk_name="Budget 2026-10-11";sprk_totalbudget=90000;transactioncurrencyid@odata.bind="/transactioncurrencies(<id5>)"} null
        app-only writer (IFieldMappingDataverseService) UpdateRecordFieldsAsync sprk_event sprk_event <id6> {ownerid@odata.bind=/teams(<id2>);sprk_AssignedTo@odata.bind="/contacts(<id7>)";sprk_CreatedByPerson@odata.bind=/systemusers(<id3>);sprk_EventType_Ref@odata.bind="/sprk_eventtype_refs(<id8>)";sprk_RegardingMatter@odata.bind="/sprk_matters(<id4>)";sprk_RegardingRecordType@odata.bind="/sprk_recordtype_refs(<id9>)";sprk_description="Call the client about the budget";sprk_duedate="2026-11-15";sprk_eventname="Call the client about the budget";sprk_regardingrecordid="<id4>";statuscode=659490001} null
        app-only writer (IFieldMappingDataverseService) UpdateRecordFieldsAsync sprk_todo sprk_todo <id10> {ownerid@odata.bind=/teams(<id2>);sprk_AssignedTo@odata.bind="/contacts(<id7>)";sprk_CreatedByPerson@odata.bind=/systemusers(<id3>);sprk_RegardingMatter@odata.bind="/sprk_matters(<id4>)";sprk_RegardingRecordType@odata.bind="/sprk_recordtype_refs(<id9>)";sprk_duedate="2026-10-20";sprk_name="Chase the revision";sprk_regardingrecordid="<id4>"} null
        """;
    private const string PinFaultAfterResponse = """
        {"Status":6,"ReviewId":"<id1>","ReasonCode":"decision.action.write_failed","Detail":"Scripted fault. It may have been applied.","ActionCode":"assign-work","DecisionRecordId":null,"RecordClass":null,"GateTier":"Tier3","Steps":[{"Key":"action:create-budget","Code":"create-budget","Phase":2,"Status":"Done","Written":[{"Entity":"sprk_budget","Id":"<id2>"}],"PossiblyWritten":[],"ReasonCode":null,"Detail":null,"Warning":null},{"Key":"action:assign-task","Code":"assign-task","Phase":2,"Status":"Done","Written":[{"Entity":"sprk_event","Id":"<id3>"}],"PossiblyWritten":[],"ReasonCode":null,"Detail":null,"Warning":null},{"Key":"next-step:add-todo#0","Code":"add-todo","Phase":3,"Status":"Done","Written":[{"Entity":"sprk_todo","Id":"<id4>"}],"PossiblyWritten":[],"ReasonCode":null,"Detail":null,"Warning":null},{"Key":"next-step:assign-work#1","Code":"assign-work","Phase":3,"Status":"Failed","Written":[],"PossiblyWritten":[],"ReasonCode":"decision.action.write_failed","Detail":"Scripted fault. It may have been applied.","Warning":null}],"FollowOns":[],"Signals":[],"FailedStep":3,"Landed":[{"Entity":"sprk_budget","Id":"<id2>"},{"Entity":"sprk_event","Id":"<id3>"},{"Entity":"sprk_todo","Id":"<id4>"}],"PossiblyLanded":[],"RecordMayExist":false,"SentButNotRecorded":false,"Resumable":false,"SendChoiceRequired":false,"Warnings":[],"Replayed":false}
        """;
    private const string PinMetadataFaultResponse = """
        {"Status":6,"ReviewId":"<id1>","ReasonCode":"decision.action.write_failed","Detail":"Scripted fault. It may have been applied.","ActionCode":"create-budget","DecisionRecordId":null,"RecordClass":null,"GateTier":"Tier3","Steps":[{"Key":"action:create-budget","Code":"create-budget","Phase":2,"Status":"Failed","Written":[],"PossiblyWritten":[],"ReasonCode":"decision.action.write_failed","Detail":"Scripted fault. It may have been applied.","Warning":null}],"FollowOns":[],"Signals":[],"FailedStep":2,"Landed":[],"PossiblyLanded":[],"RecordMayExist":false,"SentButNotRecorded":false,"Resumable":false,"SendChoiceRequired":false,"Warnings":[],"Replayed":false}
        """;
}
