using System.Text.Json;
using FluentAssertions;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Sprk.Bff.Api.Services.Signals;
using Xunit;

namespace Sprk.Bff.Api.Tests.Integration.DataMutation.Signals;

/// <summary>
/// Ontology task 223 (DI-144, #2632): the commit's "already recorded" review-id lookup trusts a Decision Record only when
/// <see cref="WriterCreatedRecord"/> holds: <c>createdby</c> the writer AND <c>createdonbehalfby</c> empty. A row stamped with the writer as
/// creator but created on its behalf by another identity is not the writer's own.
/// </summary>
public sealed partial class DecisionCommitServiceTests
{
    [Fact]
    public async Task FindRecord_ARowCreatedOnTheWritersBehalfByAnotherIdentity_IsNotAnEarlierRun_TheFullPathRuns()
    {
        var rig = new Rig();
        var review = PathBReview(inquiry: false, todo: false);
        var onBehalf = new Entity("sprk_decisionrecord", Guid.NewGuid())
        {
            ["sprk_confirmedby"] = new EntityReference("systemuser", Caller),
            ["createdby"] = new EntityReference("systemuser", WriterUser),
            ["createdonbehalfby"] = new EntityReference("systemuser", ConsoleCreator),
            ["sprk_steps"] = JsonSerializer.Serialize(new Dictionary<string, object?>
            {
                ["reviewId"] = review.ReviewId.ToString("D"),
                ["reviewHash"] = DecisionCommitService.ContentHash(Primary, review),
                ["resolvedSignalIds"] = new[] { Primary.ToString("D") },
                ["steps"] = new[] { new { code = "approve-variance", taken = true } },
            }),
            ["sprk_followons"] = "[]",
        };
        rig.ExistingRecords.Add(onBehalf);

        var result = await rig.CommitAsync(review);

        result.Status.Should().Be(DecisionCommitStatus.Recorded, result.Detail);
        result.Replayed.Should().BeFalse("a row created on the writer's behalf is not an earlier run of this review");
        result.DecisionRecordId.Should().Be(RecordId).And.NotBe(onBehalf.Id);
        rig.Log.Should().Contain(l => l.StartsWith("execute:", StringComparison.Ordinal), "the executors ran");
        rig.LinkedRecord(Primary).Should().Be(RecordId, "the Signal links the genuine record");
        rig.QueriesAsked.Last(q => q.EntityName == "sprk_decisionrecord").Criteria.Conditions.Should().Contain(
            c => c.AttributeName == WriterCreatedRecord.CreatedOnBehalfByColumn && c.Operator == ConditionOperator.Null);
    }
}
