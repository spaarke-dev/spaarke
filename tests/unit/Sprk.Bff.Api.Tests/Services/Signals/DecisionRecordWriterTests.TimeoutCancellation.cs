using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.Signals;
using Xunit;
using static Sprk.Bff.Api.Tests.TestInfrastructure.OwnershipDirectory;

namespace Sprk.Bff.Api.Tests.Services.Signals;

/// <summary>
/// ebilling-r1 task 084h-5, review round 2: every catch in the writer that turns a Dataverse fault into an answer uses the clause
/// <c>ex is not OperationCanceledException || !ct.IsCancellationRequested</c>. A timeout that surfaces as a <see cref="TaskCanceledException"/>
/// while the caller's token is live is that catch's existing fault answer (a lookup-failed or owner-refusal reason, an unknown person, a logged
/// verify failure), never an escape; a cancelled caller token still propagates. One theory case per catch, both ways.
/// </summary>
public sealed partial class DecisionRecordWriterTests
{
    public enum TimeoutSite
    {
        GateLookup,
        ConditionClearedLookup,
        ConditionClearedSubjectRead,
        GateOwnerRead,
        OwnerResolver,
        SecureOwnerReadBack,
    }

    public static TheoryData<TimeoutSite> TimeoutSites() => new(Enum.GetValues<TimeoutSite>());

    /// <summary>One catch as a test drives it: its harness, its write, and how a fault is injected where the catch's own call is made. The
    /// fault is built from the token the faulting call received, so a cancelled-token case can cancel inside the call.</summary>
    private sealed record TimeoutCase(Harness H, Func<CancellationToken, Task<DecisionRecordWriteResult>> Write, Action<Func<CancellationToken, Exception>> Arm);

    /// <summary>The one fault the writer still lets escape, for the job tests' "an exception that is not a refusal": the writer's own log call
    /// after a successful create throws <paramref name="fault"/> (nothing in the writer wraps it, and the record has landed).</summary>
    internal static void FaultTheWritersLogAfterTheCreate(Harness h, Exception fault) =>
        h.Log.Intercept = (level, message) =>
        {
            if (level == LogLevel.Information && message.StartsWith("Decision Record ", StringComparison.Ordinal) && message.Contains(" written for review ", StringComparison.Ordinal))
            {
                throw fault;
            }
        };

    private static bool ReadsCommunicationOwner(QueryExpression q) => q.EntityName == "sprk_communication" && q.LinkEntities.Count > 0;

    private static TimeoutCase TimeoutCaseOf(TimeoutSite site)
    {
        switch (site)
        {
            case TimeoutSite.GateLookup:
                {
                    var h = MatterFiledCommunication();
                    return new(h, ct => h.Writer.WriteGateDecisionAsync(Gate(), ct),
                        fault => h.QueryFault = (q, t) => q.EntityName == "sprk_decisionrecord" ? fault(t) : null);
                }

            case TimeoutSite.ConditionClearedLookup:
                {
                    var h = ChangedToDo(Colleague, SignalClosed.AddDays(-1));
                    return new(h, ct => h.Writer.WriteConditionClearedAsync(Cleared(), ct),
                        fault => h.QueryFault = (q, t) => q.EntityName == "sprk_decisionrecord" ? fault(t) : null);
                }

            case TimeoutSite.ConditionClearedSubjectRead:
                {
                    var h = ChangedToDo(Colleague, SignalClosed.AddDays(-1));
                    return new(h, ct => h.Writer.WriteConditionClearedAsync(Cleared(), ct),
                        fault => h.QueryFault = (q, t) => q.EntityName == "sprk_todo" && q.LinkEntities.Count > 0 ? fault(t) : null);
                }

            case TimeoutSite.GateOwnerRead:
                {
                    var (h, _) = PrincipalOwnedGateHarness();
                    return new(h, ct => h.Writer.WriteGateDecisionAsync(Gate(), ct),
                        fault => h.QueryFault = (q, t) => ReadsCommunicationOwner(q) ? fault(t) : null);
                }

            case TimeoutSite.OwnerResolver:
                {
                    var h = MatterFiledCommunication();
                    return new(h, ct => h.Writer.WriteGateDecisionAsync(Gate(), ct),
                        fault => h.Ownership
                            .Setup(r => r.ResolveOwnerAsync(It.IsAny<RecordOwnershipContext>(), It.IsAny<CancellationToken>()))
                            .Returns((RecordOwnershipContext _, CancellationToken t) => Task.FromException<RecordOwnerResolution>(fault(t))));
                }

            case TimeoutSite.SecureOwnerReadBack:
                {
                    var h = new Harness(Secure());
                    return new(h, ct => h.Writer.WriteAsync(Request(), ct),
                        fault => h.Org
                            .Setup(o => o.RetrieveAsync("sprk_decisionrecord", It.IsAny<Guid>(), It.IsAny<ColumnSet>(), It.IsAny<CancellationToken>()))
                            .Returns((string _, Guid _, ColumnSet _, CancellationToken t) => Task.FromException<Entity>(fault(t))));
                }

            default:
                throw new ArgumentOutOfRangeException(nameof(site));
        }
    }

    [Theory]
    [MemberData(nameof(TimeoutSites))]
    public async Task Timeout_ATaskCanceledException_WithAnUncancelledToken_IsTheCatchesExistingFaultAnswer_NotAnEscape(TimeoutSite site)
    {
        var c = TimeoutCaseOf(site);
        c.Arm(_ => new TaskCanceledException("The request timed out."));

        DecisionRecordWriteResult? result = null;
        DecisionRecordRefusedException? refused = null;
        try
        {
            result = await c.Write(CancellationToken.None);
        }
        catch (DecisionRecordRefusedException ex)
        {
            refused = ex;
        }

        switch (site)
        {
            case TimeoutSite.GateLookup:
                refused.Should().NotBeNull().And.Subject.As<DecisionRecordRefusedException>().Reason.Should().Be(DecisionRecordRefusalReason.GateRecordLookupFailed);
                c.H.Created.Should().BeEmpty();
                break;
            case TimeoutSite.ConditionClearedLookup:
                refused.Should().NotBeNull().And.Subject.As<DecisionRecordRefusedException>().Reason.Should().Be(DecisionRecordRefusalReason.ConditionClearedLookupFailed);
                c.H.Created.Should().BeEmpty();
                break;
            case TimeoutSite.GateOwnerRead:
            case TimeoutSite.OwnerResolver:
                refused.Should().NotBeNull().And.Subject.As<DecisionRecordRefusedException>().Reason.Should().Be(DecisionRecordRefusalReason.OwnerResolutionFailed);
                c.H.Created.Should().BeEmpty();
                break;
            case TimeoutSite.ConditionClearedSubjectRead:
                refused.Should().BeNull("an unreadable last change is 'person unknown', and the record is still written");
                result!.ConfirmedByUserId.Should().BeNull();
                c.H.Created.Should().ContainSingle();
                c.H.Log.Entries.Should().Contain(e => e.Level == LogLevel.Warning && e.Message.Contains("could not be read for its last change"));
                break;
            case TimeoutSite.SecureOwnerReadBack:
                refused.Should().BeNull("a failed read-back is logged and metered, and the record id is still returned");
                result!.SecureOwnerTeamId.Should().Be(SecureTeam);
                c.H.Created.Should().ContainSingle();
                c.H.Log.Entries.Should().Contain(e => e.EventId == DecisionRecordEvents.SecureOwnerMismatch && e.Level == LogLevel.Error);
                break;
        }
    }

    [Theory]
    [MemberData(nameof(TimeoutSites))]
    public async Task Timeout_MustNotFire_ACancelledCallerToken_StillPropagatesTheCancellation(TimeoutSite site)
    {
        var c = TimeoutCaseOf(site);
        using var cts = new CancellationTokenSource();
        c.Arm(t =>
        {
            cts.Cancel(); // the caller cancels while the call is in flight
            return new TaskCanceledException("cancelled", null, t);
        });

        await c.H.Writer.Invoking(_ => c.Write(cts.Token)).Should().ThrowAsync<OperationCanceledException>();
    }
}
