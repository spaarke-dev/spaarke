using FluentAssertions;
using Microsoft.Xrm.Sdk;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Models;
using Sprk.Bff.Api.Services.ExternalAccess;
using Sprk.Bff.Api.Services.Intake;
using Sprk.Bff.Api.Services.Signals.Actions;
using Sprk.Bff.Api.Tests.Integration.DataMutation.Intake;
using Xunit;
using static Sprk.Bff.Api.Tests.Integration.DataMutation.Intake.IntakeApprovalTestBed;

namespace Sprk.Bff.Api.Tests.Integration.Auth.Intake;

/// <summary>
/// The intake approval's authorization (spaarke-intake-pipeline-r1 task 017; uac-r2 (a)–(f), owner "Team, then claimer"): the record
/// check, the owning-team membership and the claimer, in that order, as the caller, failing closed, at claim AND again at approve and
/// reject; self-approval allowed; a claim grants nothing. Real <see cref="IntakeApprovalService"/>, store and executors over
/// <see cref="IntakeApprovalTestBed"/>; the access answers are doubles at the probe, identity and contact boundaries.
/// </summary>
/// <remarks>Maintain-class (ADR-038, security path). Every refusal is checked to have written nothing.</remarks>
public sealed class IntakeApprovalAuthorizationTests
{
    private static Task<IntakeApprovalResult> Act(IntakeApprovalService service, IntakeApprovalAction action, IntakeApprovalTestBed.Caller? caller) =>
        action switch
        {
            IntakeApprovalAction.Claim => service.ClaimAsync(Http(caller), IntakeRunTestBed.RequestId, CancellationToken.None),
            IntakeApprovalAction.Approve => service.ApproveAsync(Http(caller), IntakeRunTestBed.RequestId, CancellationToken.None),
            _ => service.RejectAsync(Http(caller), IntakeRunTestBed.RequestId, CancellationToken.None),
        };

    private static async Task<IntakeApprovalTestBed> ClaimedByAliceAsync()
    {
        var bed = await new IntakeApprovalTestBed().AwaitingApprovalAsync();
        bed.SetClaimer(Alice.ContactId);
        return bed;
    }

    // ── (1) The record check: one 404 for every failure ────────────────────────────────────────

    public static TheoryData<string, IntakeApprovalAction> RecordCheckFailures()
    {
        var data = new TheoryData<string, IntakeApprovalAction>();
        foreach (var action in new[] { IntakeApprovalAction.Claim, IntakeApprovalAction.Approve, IntakeApprovalAction.Reject })
            foreach (var failure in new[] { "no-read", "read-only", "write-only", "probe-fault", "unknown-id", "not-an-intake-row", "no-token" })
                data.Add(failure, action);
        return data;
    }

    [Theory]
    [MemberData(nameof(RecordCheckFailures))]
    public async Task RecordCheckFails_IsTheSameNotFound_ForEveryCause_AndWritesNothing(string failure, IntakeApprovalAction action)
    {
        var bed = await ClaimedByAliceAsync();
        var requestId = IntakeRunTestBed.RequestId;
        IntakeApprovalTestBed.Caller? caller = Alice;
        switch (failure)
        {
            case "no-read": bed.Rights[Alice.Token] = AccessRights.None; break;
            case "read-only": bed.Rights[Alice.Token] = AccessRights.Read; break;
            case "write-only": bed.Rights[Alice.Token] = AccessRights.Write; break;
            case "probe-fault": bed.RightsFault.Add(Alice.Token); break;
            case "unknown-id": requestId = Guid.Parse("01700000-0000-4000-8000-0000000000ee"); break;
            case "not-an-intake-row": bed.SetRequestColumn(IntakeRunStore.RequestIntakeStage, null); break; // an outbound inquiry row
            case "no-token": caller = null; break;
        }

        var before = bed.Run.Dataverse.Writes.Count;
        var service = bed.Service();

        var result = action switch
        {
            IntakeApprovalAction.Claim => await service.ClaimAsync(Http(caller), requestId, CancellationToken.None),
            IntakeApprovalAction.Approve => await service.ApproveAsync(Http(caller), requestId, CancellationToken.None),
            _ => await service.RejectAsync(Http(caller), requestId, CancellationToken.None),
        };

        result.Should().Be(new IntakeApprovalResult(IntakeApprovalOutcome.NotFound, Detail: "The requested record was not found."),
            "every record-check failure is the same answer, so the route is not an existence oracle");
        bed.WritesSince(before).Should().BeEmpty();
        bed.Run.Enqueued.Should().BeEmpty();
    }

    [Fact]
    public async Task RecordCheck_AsksTheEqualisedGateQuestion_OnTheRequestsEntitySet()
    {
        var bed = await ClaimedByAliceAsync();

        await bed.Service().CheckAsync(Http(Alice), IntakeRunTestBed.RequestId, IntakeApprovalAction.Approve, CancellationToken.None);

        bed.Probe.Verify(p => p.GetCallerRightsAsync(Alice.Token, "sprk_servicerequests", IntakeRunTestBed.RequestId, It.IsAny<CancellationToken>()), Moq.Times.Once);
    }

    // ── (2) Owning-team membership ─────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("not-a-member", IntakeApprovalAction.Claim)]
    [InlineData("not-a-member", IntakeApprovalAction.Approve)]
    [InlineData("not-a-member", IntakeApprovalAction.Reject)]
    [InlineData("teams-fault", IntakeApprovalAction.Claim)]
    [InlineData("teams-fault", IntakeApprovalAction.Approve)]
    [InlineData("user-owned-row", IntakeApprovalAction.Claim)]
    [InlineData("caller-unresolved", IntakeApprovalAction.Claim)]
    public async Task NotProvablyInTheOwningTeam_IsForbidden_AndWritesNothing(string failure, IntakeApprovalAction action)
    {
        var bed = await ClaimedByAliceAsync();
        switch (failure)
        {
            case "not-a-member": bed.Teams[Alice.UserId] = [OtherTeamId]; break;
            case "teams-fault": bed.TeamsFault.Add(Alice.UserId); break;
            case "user-owned-row": bed.SetRequestColumn("ownerid", new EntityReference("systemuser", Alice.UserId)); break;
            case "caller-unresolved":
                bed.Probe.Setup(p => p.GetCallerSystemUserIdAsync(Moq.It.IsAny<string?>(), Moq.It.IsAny<CancellationToken>())).ReturnsAsync((Guid?)null);
                break;
        }

        if (action == IntakeApprovalAction.Claim) bed.SetClaimer(null);
        var before = bed.Run.Dataverse.Writes.Count;

        var result = await Act(bed.Service(), action, Alice);

        result.Outcome.Should().Be(IntakeApprovalOutcome.Forbidden);
        bed.WritesSince(before).Should().BeEmpty();
        bed.ClaimedBy().Should().Be(action == IntakeApprovalAction.Claim ? null : Alice.ContactId);
    }

    // ── (3) The caller's own contact, and the claimer ──────────────────────────────────────────

    [Theory]
    [InlineData(nameof(UserContactStatus.None))]
    [InlineData(nameof(UserContactStatus.Several))]
    [InlineData(nameof(UserContactStatus.Unreadable))]
    public async Task NoSingleLinkedContact_IsForbidden_NeverABlankClaim(string statusName)
    {
        // The status type is internal, so the theory carries its name.
        var status = Enum.Parse<UserContactStatus>(statusName);
        var bed = await new IntakeApprovalTestBed().AwaitingApprovalAsync();
        bed.Contacts[Alice.UserId] = new UserContactResolution(status, null, false, null, null);
        var before = bed.Run.Dataverse.Writes.Count;

        var result = await bed.Service().ClaimAsync(Http(Alice), IntakeRunTestBed.RequestId, CancellationToken.None);

        result.Outcome.Should().Be(IntakeApprovalOutcome.Forbidden);
        bed.WritesSince(before).Should().BeEmpty();
        bed.ClaimedBy().Should().BeNull();
    }

    [Theory]
    [InlineData(IntakeApprovalAction.Claim)]
    [InlineData(IntakeApprovalAction.Approve)]
    [InlineData(IntakeApprovalAction.Reject)]
    public async Task AContactThatAlsoRepresentsAnotherUser_IsForbidden_SoTwoUsersNeverShareAClaim(IntakeApprovalAction action)
    {
        // Alice's one linked contact is ALSO linked from another user. Bob holds the same contact and claimed with it; Alice must not
        // pass the claimer check as him (claim: must not answer "already yours").
        var bed = await new IntakeApprovalTestBed().AwaitingApprovalAsync();
        bed.SetClaimer(Bob.ContactId);
        bed.Contacts[Alice.UserId] = new UserContactResolution(UserContactStatus.One, Bob.ContactId, true, null, null);
        var before = bed.Run.Dataverse.Writes.Count;
        var service = bed.Service();

        var result = action switch
        {
            IntakeApprovalAction.Claim => await service.ClaimAsync(Http(Alice), IntakeRunTestBed.RequestId, CancellationToken.None),
            IntakeApprovalAction.Approve => await service.ApproveAsync(Http(Alice), IntakeRunTestBed.RequestId, CancellationToken.None),
            _ => await service.RejectAsync(Http(Alice), IntakeRunTestBed.RequestId, CancellationToken.None),
        };

        result.Outcome.Should().Be(IntakeApprovalOutcome.Forbidden);
        bed.WritesSince(before).Should().BeEmpty();
        bed.Run.Enqueued.Should().BeEmpty();
    }

    [Theory]
    [InlineData(IntakeApprovalAction.Approve)]
    [InlineData(IntakeApprovalAction.Reject)]
    public async Task NotTheClaimer_IsForbidden_FromThePreflight_AndNothingIsWritten(IntakeApprovalAction action)
    {
        var bed = await ClaimedByAliceAsync();
        var before = bed.Run.Dataverse.Writes.Count;
        IDecisionActionExecutor executor = action == IntakeApprovalAction.Approve
            ? new ApproveIntakeExecutor(bed.Service())
            : new RejectIntakeExecutor(bed.Service());

        var preflight = await executor.PreflightAsync(ActionRequest(Bob), CancellationToken.None);
        var execute = await executor.ExecuteAsync(ActionRequest(Bob), CancellationToken.None);

        preflight!.ReasonCode.Should().Be(DecisionActionReasons.NotAuthorized, "the commit route maps it to 403");
        execute.Status.Should().Be(DecisionActionStatus.Refused);
        execute.ReasonCode.Should().Be(DecisionActionReasons.NotAuthorized);
        bed.WritesSince(before).Should().BeEmpty();
        bed.Run.Enqueued.Should().BeEmpty();
    }

    [Theory]
    [InlineData(IntakeApprovalAction.Approve)]
    [InlineData(IntakeApprovalAction.Reject)]
    public async Task TheClaimChangesBetweenPreflightAndExecute_TheExecuteRefuses_AndNothingIsWritten(IntakeApprovalAction action)
    {
        var bed = await ClaimedByAliceAsync();
        IDecisionActionExecutor executor = action == IntakeApprovalAction.Approve
            ? new ApproveIntakeExecutor(bed.Service())
            : new RejectIntakeExecutor(bed.Service());
        (await executor.PreflightAsync(ActionRequest(Alice), CancellationToken.None)).Should().BeNull("Alice is the claimer at preflight");

        bed.SetClaimer(Bob.ContactId); // Bob took it (the form, or a race) before the execute
        var before = bed.Run.Dataverse.Writes.Count;
        var outcome = await executor.ExecuteAsync(ActionRequest(Alice), CancellationToken.None);

        outcome.Status.Should().Be(DecisionActionStatus.Refused);
        outcome.ReasonCode.Should().Be(DecisionActionReasons.NotAuthorized);
        bed.WritesSince(before).Should().BeEmpty();
        bed.Run.Enqueued.Should().BeEmpty();
    }

    [Theory]
    [InlineData("lost-membership", IntakeApprovalAction.Approve)]
    [InlineData("lost-membership", IntakeApprovalAction.Reject)]
    [InlineData("lost-write", IntakeApprovalAction.Approve)]
    [InlineData("lost-write", IntakeApprovalAction.Reject)]
    public async Task TheClaimerLostTheirRightsAfterClaiming_IsRefusedAtApproveAndReject(string lost, IntakeApprovalAction action)
    {
        var bed = await new IntakeApprovalTestBed().AwaitingApprovalAsync();
        var service = bed.Service();
        (await service.ClaimAsync(Http(Alice), IntakeRunTestBed.RequestId, CancellationToken.None)).IsDone.Should().BeTrue();
        if (lost == "lost-membership") bed.Teams[Alice.UserId] = [];
        else bed.Rights[Alice.Token] = AccessRights.Read;
        var before = bed.Run.Dataverse.Writes.Count;

        var result = await Act(service, action, Alice);

        result.Outcome.Should().Be(lost == "lost-membership" ? IntakeApprovalOutcome.Forbidden : IntakeApprovalOutcome.NotFound);
        bed.WritesSince(before).Should().BeEmpty();
        bed.Run.Enqueued.Should().BeEmpty();
        bed.Run.RunStatus(bed.RunId).Should().Be((int)ProcessingJobStatus.InProgress);
    }

    // ── Self-approval (owner "Allow it", ontology D-237) ───────────────────────────────────────

    [Fact]
    public async Task SelfApproval_MustNotFire_TheRequesterInTheTeamWithReadAndWrite_ClaimsAndApproves()
    {
        var bed = await new IntakeApprovalTestBed().AwaitingApprovalAsync();
        bed.SetRequestColumn("sprk_requestedby", new EntityReference("contact", Alice.ContactId));
        var service = bed.Service();

        (await service.ClaimAsync(Http(Alice), IntakeRunTestBed.RequestId, CancellationToken.None)).IsDone.Should().BeTrue();
        (await new ApproveIntakeExecutor(service).ExecuteAsync(ActionRequest(Alice), CancellationToken.None)).Status
            .Should().Be(DecisionActionStatus.Done, "being the requester neither bars nor grants anything");
        bed.Run.Enqueued.Should().ContainSingle();
    }

    [Theory]
    [InlineData("not-in-team")]
    [InlineData("no-write")]
    public async Task SelfApproval_MustFire_TheRequesterWithoutApprovalRights_IsRefusedBeforeAnyWrite(string missing)
    {
        var bed = await new IntakeApprovalTestBed().AwaitingApprovalAsync();
        bed.SetRequestColumn("sprk_requestedby", new EntityReference("contact", Alice.ContactId));
        bed.SetClaimer(Alice.ContactId);
        if (missing == "not-in-team") bed.Teams[Alice.UserId] = [OtherTeamId];
        else bed.Rights[Alice.Token] = AccessRights.Read;
        var before = bed.Run.Dataverse.Writes.Count;

        var outcome = await new ApproveIntakeExecutor(bed.Service()).ExecuteAsync(ActionRequest(Alice), CancellationToken.None);

        outcome.Status.Should().Be(DecisionActionStatus.Refused);
        outcome.ReasonCode.Should().Be(DecisionActionReasons.NotAuthorized);
        bed.WritesSince(before).Should().BeEmpty();
        bed.Run.Enqueued.Should().BeEmpty();
    }

    // ── ADR-032: the switch is checked AFTER authorization ─────────────────────────────────────

    [Theory]
    [InlineData(IntakeApprovalAction.Claim)]
    [InlineData(IntakeApprovalAction.Approve)]
    [InlineData(IntakeApprovalAction.Reject)]
    public async Task HarnessOff_AnUnauthorisedCallerStillGetsTheRecordCheckOrTeamAnswer_NotDisabled(IntakeApprovalAction action)
    {
        var bed = await ClaimedByAliceAsync();
        bed.Run.Options.CurrentValue = new IntakePipelineOptions { Harnesses = { ["nda"] = false } };
        bed.Rights[Bob.Token] = AccessRights.None;
        bed.Teams[Alice.UserId] = [OtherTeamId];

        (await Act(bed.Service(), action, Bob)).Outcome.Should().Be(IntakeApprovalOutcome.NotFound);
        (await Act(bed.Service(), action, Alice)).Outcome.Should().Be(IntakeApprovalOutcome.Forbidden);
    }

    // ── uac-r2 (e): a claim grants nothing ─────────────────────────────────────────────────────

    [Fact]
    public async Task Claim_CreatesNoAssignedAccessRow_AndNoGrant_AndTouchesOnlyTheClaimerColumn()
    {
        var bed = await new IntakeApprovalTestBed().AwaitingApprovalAsync();
        var before = bed.Run.Dataverse.Writes.Count;

        (await bed.Service().ClaimAsync(Http(Alice), IntakeRunTestBed.RequestId, CancellationToken.None)).IsDone.Should().BeTrue();

        var writes = bed.WritesSince(before);
        writes.Should().ContainSingle().Which.Entity.Should().Be(IntakeRunStore.RequestEntity);
        writes.Should().NotContain(w => w.Entity == "sprk_assignedaccess" || w.Entity == "principalobjectaccess");
        writes.Single().Fields.Keys.Should().Equal(IntakeRunStore.RequestClaimedBy);
        bed.Run.Dataverse.Mock.Verify(d => d.CreateAsync(Moq.It.Is<Entity>(e => e.LogicalName != IntakeRunStore.RunEntity), Moq.It.IsAny<CancellationToken>()), Moq.Times.Never);
    }

    [Fact]
    public void AServiceRequest_IsNeverAnAssignedAccessRoot()
    {
        // uac-r2 (1)/(e): ADR-034 A4 is not extended to service requests; a request is never grantable to a contact.
        AssignedAccessMaterializer.TryParseRootLogicalName("sprk_servicerequest", out _).Should().BeFalse();
        AssignedAccessMaterializer.TryParseRootLogicalName("sprk_matter", out _).Should().BeTrue("control: a real root parses");
    }

    [Fact]
    public void TheClaimerColumn_IsNotTheRequestsForPersonColumn()
    {
        // uac-r2 (2): the create paths stamp a ForPerson column with the creator's contact, so registering the claimer there would make a
        // new request arrive already claimed.
        // OwnedChildWrite.ForPersonColumns is built from RecordTableRules, the one registry both create paths read.
        Sprk.Bff.Api.Services.Dataverse.OwnedChildWrite.ForPersonColumns.Should().NotContainKey(IntakeRunStore.RequestEntity);
        Sprk.Bff.Api.Services.Dataverse.OwnedChildWrite.ForPersonColumns.Should().ContainKey("sprk_workassignment", "control: the registry is the one read");
    }
}
