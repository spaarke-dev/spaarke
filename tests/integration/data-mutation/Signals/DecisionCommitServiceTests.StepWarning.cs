using FluentAssertions;
using Sprk.Bff.Api.Services.Signals;
using Sprk.Bff.Api.Services.Signals.Actions;
using Xunit;

namespace Sprk.Bff.Api.Tests.Integration.DataMutation.Signals;

/// <summary>
/// Ontology task 176 (D-216) review 2: a step that finishes Done with a warning of its own (the budget inquiry that was sent but whose
/// .eml was not archived) reaches the commit answer's <c>Warnings</c> - the channel the client already words
/// (<c>describeWarning</c>) - and the decision still records.
/// </summary>
public sealed partial class DecisionCommitServiceTests
{
    [Fact]
    public async Task InquirySentButNotArchived_TheStepsWarning_ReachesTheResult_AndTheDecisionIsRecorded()
    {
        var rig = new Rig();
        rig.Executors["send-budget-inquiry"].Result = DecisionActionOutcome.Done(
            "send-budget-inquiry",
            new DecisionRecordRef("sprk_servicerequest", ServiceRequestId), new DecisionRecordRef("sprk_communication", CommunicationId))
            with
        { Warning = "inquiry_email_not_archived" };

        var result = await rig.CommitAsync(PathBReview());

        result.Status.Should().Be(DecisionCommitStatus.Recorded, result.Detail);
        result.Warnings.Should().Contain("inquiry_email_not_archived");
        result.Steps.Should().Contain(s => s.Code == "send-budget-inquiry" && s.Warning == "inquiry_email_not_archived");
    }

    [Fact]
    public async Task InquirySentAndArchived_NoStepWarning_MustNotFire()
    {
        var rig = new Rig();

        var result = await rig.CommitAsync(PathBReview());

        result.Status.Should().Be(DecisionCommitStatus.Recorded, result.Detail);
        result.Warnings.Should().NotContain("inquiry_email_not_archived");
        result.Steps.Should().OnlyContain(s => s.Warning == null);
    }
}
