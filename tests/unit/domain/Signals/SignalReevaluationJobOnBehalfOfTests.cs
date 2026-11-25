using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Xrm.Sdk;
using Sprk.Bff.Api.Services.Signals;
using Sprk.Bff.Api.Telemetry;
using Xunit;

namespace Sprk.Bff.Api.Tests.Domain.Signals;

/// <summary>
/// Ontology task 223 (DI-144, #2632): a closure's retry reads only the prior records <see cref="WriterCreatedRecord"/> calls the writer's
/// (<c>createdby</c> the writer AND <c>createdonbehalfby</c> empty). A D-128 reminder stamped with the writer as creator but created on its
/// behalf by another identity is foreign: its link is never replaced. The must-not-fire side (a writer-created reminder with
/// <c>createdonbehalfby</c> empty is replaced) is <c>Run_ANextStepsOnlyReviewRecordLinked_IsReplacedByTheClosuresRecord_LikeAReminder</c>.
/// </summary>
public partial class SignalReevaluationJobTests
{
    [Fact]
    public async Task Run_AClosuresRetryWhosePriorLinkWasCreatedOnTheWritersBehalf_IsNotReplaced_AndTheSkipIsLogged()
    {
        var (h, signalId) = ToDoCompletedByAColleague();
        var prior = Guid.NewGuid();
        var reminder = ForeignReminderRecord(prior, createdBy: WriterOrg.WriterUserId);
        reminder["createdonbehalfby"] = new EntityReference("systemuser", ConsoleUserId);
        h.Sysadmin.DecisionRecords[prior] = reminder;
        h.Org.CreateFaultFor = entity =>
        {
            if (entity.LogicalName == "sprk_decisionrecord") h.Org.Rows[signalId]["sprk_decisionrecord"] = new EntityReference("sprk_decisionrecord", prior);
            return null;
        };
        (await h.RunAsync()).Success.Should().BeTrue();
        h.Org.CreateFaultFor = null;
        await h.Cache.RemoveAsync(SignalReevaluationJob.ClosureRecordSkippedCacheKey(signalId, 1));
        h.Org.ResetCounts();
        h.Clock.Advance(TimeSpan.FromDays(1));

        var retry = await h.RunAsync();

        retry.Success.Should().BeTrue();
        h.Org.WriteCalls.Should().Be(0, "a reminder created on the writer's behalf is not the writer's D-128 link");
        h.Org.Rows[signalId].GetAttributeValue<EntityReference>("sprk_decisionrecord")!.Id.Should().Be(prior);
        h.Log.Should().ContainSingle(e => e.Message.Contains(OntologyWriterFailureReason.ClosureRecordSkippedUnrecognisedPriorLink)
                                          && e.Message.Contains(signalId.ToString()) && e.Message.Contains(prior.ToString()))
            .Which.Level.Should().Be(LogLevel.Warning);
    }
}
