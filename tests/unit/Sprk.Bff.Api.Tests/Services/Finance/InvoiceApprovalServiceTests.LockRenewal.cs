using FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using Microsoft.Xrm.Sdk;
using Moq;
using Sprk.Bff.Api.Services.Ai.Membership.Models;
using Sprk.Bff.Api.Services.Finance;
using Xunit;
using static Sprk.Bff.Api.Tests.Services.Finance.ApprovalWorld;

namespace Sprk.Bff.Api.Tests.Services.Finance;

/// <summary>
/// ebilling-r1 ISS-050 (#3059), the approval path: approve holds the line-decision lock (at <see cref="InvoiceApprovalService.LockDuration"/>) while
/// its Accept all Clean writes, so the bulk accept renews THAT lock as the approval's owner, and stops the same way when it is lost: the approval's
/// existing answers (503 incomplete at <c>accept-clean</c>, or 503 unavailable before the first write), and the claimant's repeat completes it.
/// </summary>
public sealed partial class InvoiceApprovalServiceTests
{
    /// <summary>An approval world on a fake line-service clock with the expiring lock; three Clean lines plus one decided line.</summary>
    private static (ApprovalWorld W, FakeTimeProvider Clock, InvoiceLineDecisionServiceTests.LeaseLocks Locks, List<Entity> Clean) RenewalWorld()
    {
        var w = new ApprovalWorld();
        var clock = new FakeTimeProvider(Now);
        var locks = new InvoiceLineDecisionServiceTests.LeaseLocks(clock);
        w.LineClock = clock;
        w.LockService = locks;
        var clean = Enumerable.Range(0, 3).Select(_ => w.AddLine(InvoiceLineStatus.Clean, 100m, extraction: Extraction)).ToList();
        w.AddLine(InvoiceLineStatus.Accepted, 700m, approved: 700m);

        // The clock moves 40 s inside each record (the recorder resolves the decider's identity once per record).
        w.Identity.Setup(i => i.ResolveAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid user, CancellationToken _) =>
            {
                clock.Advance(TimeSpan.FromSeconds(40));
                return new PersonIdentity(user, ContactId: w.ContactOfUser.TryGetValue(user, out var c) ? c : Guid.NewGuid());
            });
        return (w, clock, locks, clean);
    }

    private static string LineLockOwner(ApprovalWorld w, InvoiceLineDecisionServiceTests.LeaseLocks locks) =>
        locks.Acquires.Where(a => a.Key == InvoiceLineDecisionService.LockKeyFor(w.InvoiceId)).Should().ContainSingle().Subject.Owner;

    [Fact(DisplayName = "ISS-050 (5): approve's Accept all Clean renews the line-decision lock it holds, as the approval's owner and at the approval's lock duration, never lets it lapse, and approves")]
    public async Task Approve_AcceptClean_RenewsTheHeldLineLock_AndApproves()
    {
        var (w, _, locks, clean) = RenewalWorld();

        var result = await w.ApproveAs(AliceUser);

        result.Outcome.Should().Be(ApprovalOutcome.Approved);
        var key = InvoiceLineDecisionService.LockKeyFor(w.InvoiceId);
        var owner = LineLockOwner(w, locks);
        locks.Renewals.Should().HaveCountGreaterThanOrEqualTo(2, "once at the first checkpoint (the approval's time under it is unknown), then as the records run")
            .And.OnlyContain(r => r.Key == key && r.Owner == owner && r.Duration == InvoiceApprovalService.LockDuration,
                "the approval's own line lock, renewed for the approval's own duration (never shortened to the line route's 2 minutes)");
        locks.Gaps(key, owner).Should().OnlyContain(gap => gap < InvoiceApprovalService.LockDuration);
        locks.Releases.Should().Contain(r => r.Key == key && r.Owner == owner, "released owner-checked");
        w.RecordsOf("accept-line").Should().HaveCount(3);
        w.ApproveRecords.Should().ContainSingle();
    }

    [Theory(DisplayName = "ISS-050 (5): when the held line lock is lost the bulk accept writes nothing more and approve answers its existing 503; the claimant's repeat approves")]
    [InlineData(0, "false")]
    [InlineData(0, "throws")]
    [InlineData(1, "false")]
    [InlineData(1, "throws")]
    public async Task Approve_LostLineLock_StopsTheBulkAccept_AndTheRepeatApproves(int failAt, string renewal)
    {
        var (w, _, locks, clean) = RenewalWorld();
        (int Writes, int Records, int Corrections, int Transitions)? atLoss = null;
        locks.OnRenew = n =>
        {
            if (n == failAt)
            {
                atLoss = (w.Writes, w.H.Created.Count, w.Corrections.Count, w.Transitions);
            }
        };
        locks.RenewAnswer = n => n == failAt && renewal == "false" ? false : null;
        locks.RenewFault = n => n == failAt && renewal == "throws" ? new TimeoutException("the lock store timed out") : null;

        var failed = await w.ApproveAs(AliceUser);

        locks.Renewals.Should().HaveCount(failAt + 1, "the bulk accept stopped at the failed renewal");
        atLoss.Should().NotBeNull();
        (w.Writes, w.H.Created.Count, w.Corrections.Count, w.Transitions).Should().Be(atLoss!.Value, "nothing at all was written after the renewal failed");
        w.InvoiceStatus.Should().Be(InvoiceStatusCodes.InReview);
        w.ApproveRecords.Should().BeEmpty();
        w.Transitions.Should().Be(0);
        if (failAt == 0)
        {
            // The first checkpoint is before the bulk accept's line write: nothing was written.
            failed.Outcome.Should().Be(ApprovalOutcome.Unavailable);
            failed.Code.Should().Be(InvoiceApprovalCodes.Unavailable);
            w.Writes.Should().Be(0);
            w.H.Created.Should().BeEmpty();
        }
        else
        {
            failed.Outcome.Should().Be(ApprovalOutcome.Incomplete, "503 incomplete, never 409, never continued unlocked");
            failed.Code.Should().Be(LineDecisionCodes.Incomplete);
            failed.FailedStep.Should().Be("accept-clean");
            failed.CommittedLineIds.Should().BeEquivalentTo(clean.Select(l => l.Id));
            w.RecordsOf("accept-line").Should().HaveCount(1, "the call stopped after the first line's record");
        }

        var owner = LineLockOwner(w, locks);
        locks.Releases.Should().Contain(r => r.Key == InvoiceLineDecisionService.LockKeyFor(w.InvoiceId) && r.Owner == owner);

        var retried = await w.ApproveAs(AliceUser);

        retried.Outcome.Should().Be(ApprovalOutcome.Approved);
        w.RecordsOf("accept-line").Should().HaveCount(3, "one record per Clean line, each written once");
        clean.Should().OnlyContain(l => w.Row("sprk_billingevent", l.Id).GetAttributeValue<OptionSetValue>("sprk_linestatus").Value == (int)InvoiceLineStatus.Accepted);
        w.ApproveRecords.Should().ContainSingle();
    }
}
