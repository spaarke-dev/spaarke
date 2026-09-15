using System.Globalization;
using System.ServiceModel;
using System.Text.Json;
using System.Xml.Linq;
using FluentAssertions;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Spaarke.Dataverse;
using Spaarke.Scheduling;
using Sprk.Bff.Api.Services;
using Sprk.Bff.Api.Services.ExternalAccess;
using Sprk.Bff.Api.Services.Jobs;
using Xunit;

namespace Sprk.Bff.Api.Tests.DataMutation.ExternalAccess;

/// <summary>
/// Expiry reminders — spec FR-33 (d), task 100: before an external grant lapses, the internal user who can renew it
/// gets an in-app notification at 30/14/7/3/1 days; the external grantee never does.
///
/// <para><b>In scope</b> (the task's closed acceptance set, the owner's 2026-09-11 recipient decision and the
/// 2026-09-14 catch-up decision): which grants are reminded (active, expiring from today through 30 days out), which
/// reminder (the most urgent threshold whose day has come, once per threshold — so a missed day is caught up and a
/// missed 1-day reminder still goes out on the expiry day), who is reminded (granter → record owner → record creator,
/// each only if an enabled interactive non-application user; otherwise unroutable; never the grantee), what it says,
/// once only (re-runs, a lost race for the claim, a held claim, a marker that did not stick, the marker's lifetime
/// across the whole window), one query per run with a paging cap, which failures make the attempt throw so the
/// scheduler retries (a failed query; a transient failure or held claim on the last day) and which do not (a permanent
/// rejection, an earlier day, a cancelled run), that one bad row does not stop the rest, and a heartbeat on every
/// attempt.</para>
///
/// <para><b>Why the fake is strict.</b> <see cref="FakeDataverse"/> evaluates the job's FetchXML the way Dataverse
/// would — its filter conditions, its joins (an inner join drops rows, an outer one does not), the columns it asks for,
/// and paging — against a model of the live schema (verified 2026-09-12 and 2026-09-15). It THROWS on a condition,
/// join or column it does not model, so a query that lost <c>statecode eq 0</c> hands back revoked grants and one that
/// names a missing column fails instead of passing.</para>
///
/// <para>Real collaborators: <see cref="NotificationService"/> and <see cref="IdempotencyService"/>, over a
/// distributed cache whose expiry runs on the SAME fake clock as the job — so a marker that expires too early is seen
/// as a repeated reminder. The one seam is <see cref="IGenericEntityService"/>, mocked STRICT.</para>
/// </summary>
public class GrantExpiryReminderJobTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 12, 6, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = new(2026, 9, 12);

    private static readonly Guid Granter = Guid.Parse("a0000000-0000-0000-0000-000000000001");
    private static readonly Guid RecordOwner = Guid.Parse("a0000000-0000-0000-0000-000000000002");
    private static readonly Guid RecordCreator = Guid.Parse("a0000000-0000-0000-0000-000000000003");
    private static readonly Guid DisabledUser = Guid.Parse("a0000000-0000-0000-0000-000000000004");
    private static readonly Guid ApplicationUser = Guid.Parse("a0000000-0000-0000-0000-000000000005");
    private static readonly Guid SupportUser = Guid.Parse("a0000000-0000-0000-0000-000000000006");
    private static readonly Guid DelegatedAdmin = Guid.Parse("a0000000-0000-0000-0000-000000000007");
    private static readonly Guid AdministrativeUser = Guid.Parse("a0000000-0000-0000-0000-000000000008");
    private static readonly Guid ReadAccessUser = Guid.Parse("a0000000-0000-0000-0000-000000000009");
    private static readonly Guid ContactId = Guid.Parse("c0000000-0000-0000-0000-000000000001");
    private static readonly Guid OrganizationId = Guid.Parse("d0000000-0000-0000-0000-000000000001");

    private readonly FakeDataverse _dataverse = new();
    private readonly Mock<IGenericEntityService> _entityService = new(MockBehavior.Strict);
    private readonly List<Entity> _notifications = new();
    private readonly Queue<Exception> _createFailures = new();
    private readonly CapturingLogger _log = new();
    private readonly FakeTimeProvider _time = new(Now);
    private readonly IDistributedCache _cache;
    private readonly GrantExpiryReminderJob _job;
    private int _queries;

    public GrantExpiryReminderJobTests()
    {
        _cache = new FakeClockDistributedCache(_time);

        _dataverse.Users[Granter] = new UserRow(IsDisabled: false, IsApplication: false);
        _dataverse.Users[RecordOwner] = new UserRow(IsDisabled: false, IsApplication: false);
        _dataverse.Users[RecordCreator] = new UserRow(IsDisabled: false, IsApplication: false);
        _dataverse.Users[DisabledUser] = new UserRow(IsDisabled: true, IsApplication: false);
        _dataverse.Users[ApplicationUser] = new UserRow(IsDisabled: false, IsApplication: true);
        _dataverse.Users[SupportUser] = new UserRow(IsDisabled: false, IsApplication: false, AccessMode: 3);
        _dataverse.Users[DelegatedAdmin] = new UserRow(IsDisabled: false, IsApplication: false, AccessMode: 5);
        _dataverse.Users[AdministrativeUser] = new UserRow(IsDisabled: false, IsApplication: false, AccessMode: 1);
        _dataverse.Users[ReadAccessUser] = new UserRow(IsDisabled: false, IsApplication: false, AccessMode: 2);
        _dataverse.Contacts[ContactId] = "Jane Doe";
        _dataverse.Organizations[OrganizationId] = "Morrison Foerster LLP";

        _entityService
            .Setup(s => s.RetrieveMultipleAsync(It.IsAny<FetchExpression>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((FetchExpression fetch, CancellationToken _) =>
            {
                _queries++;
                return _dataverse.Execute(fetch.Query);
            });
        SetUpCreate();

        _job = BuildJob(_ => new IdempotencyService(_cache, NullLogger<IdempotencyService>.Instance));
    }

    // ── Which grants, and which reminder ────────────────────────────────────────────────────────

    [Theory]
    [InlineData(30)]
    [InlineData(14)]
    [InlineData(7)]
    [InlineData(3)]
    [InlineData(1)]
    public async Task ExecuteAsync_GrantOnAThresholdDay_SendsOneReminderToTheGranter(int daysLeft)
    {
        Seed(daysLeft, grantedBy: Granter, owner: RecordOwner, creator: RecordCreator);

        await RunAsync();

        _notifications.Should().ContainSingle();
        _notifications[0].LogicalName.Should().Be("appnotification");
        RecipientOf(_notifications[0]).Should().Be(Granter);
        TitleOf(_notifications[0]).Should().Be(
            daysLeft == 1 ? "External access ends tomorrow" : $"External access ends in {daysLeft} days");
    }

    [Theory]
    [InlineData(31)]
    [InlineData(45)]
    [InlineData(-1)]
    public async Task ExecuteAsync_GrantOutsideTheWindowOrAlreadyExpired_IsNotEvenReturnedByTheQuery(int daysLeft)
    {
        Seed(daysLeft, grantedBy: Granter);

        await RunAsync();

        _notifications.Should().BeEmpty();
        Heartbeat()["InWindow"].Should().Be(0, "the query's date range — not the in-code check — excludes it");
        Heartbeat()["Skipped"].Should().Be(0);
    }

    [Fact]
    public async Task ExecuteAsync_InactiveGrantInTheWindow_SendsNothing()
    {
        Seed(7, grantedBy: Granter, stateCode: 1);

        await RunAsync();

        _notifications.Should().BeEmpty();
    }

    [Fact]
    public async Task ExecuteAsync_DailyRunsAcrossThirtyDays_SendEachThresholdExactlyOnce()
    {
        // The cache's expiry runs on the job's clock, so a marker that lived less than the window would show up here
        // as a repeated reminder on a later day.
        Seed(30, grantedBy: Granter);

        for (var day = 0; day <= 30; day++)
        {
            await RunAsync();
            _time.Advance(TimeSpan.FromDays(1));
        }

        _notifications.Select(TitleOf).Should().Equal(
            "External access ends in 30 days",
            "External access ends in 14 days",
            "External access ends in 7 days",
            "External access ends in 3 days",
            "External access ends tomorrow");
    }

    [Fact]
    public async Task ExecuteAsync_MissedThresholdDay_IsCaughtUpOnceWithTheDaysActuallyLeft()
    {
        Seed(13, grantedBy: Granter); // its 14-day reminder never went out

        await RunAsync();
        await RunAsync();

        _notifications.Select(TitleOf).Should().Equal("External access ends in 13 days");
    }

    [Fact]
    public async Task ExecuteAsync_SeveralThresholdsMissed_SendsOnlyTheMostUrgent()
    {
        Seed(5, grantedBy: Granter); // 30, 14 and 7 have all come and gone

        await RunAsync();
        _time.Advance(TimeSpan.FromDays(2));
        await RunAsync();

        _notifications.Select(TitleOf).Should().Equal("External access ends in 5 days", "External access ends in 3 days");
    }

    [Fact]
    public async Task ExecuteAsync_MissedLastDayReminder_IsSentOnTheExpiryDayOnly_Once()
    {
        Seed(0, grantedBy: Granter); // expires today; access holds through today

        await RunAsync();
        await RunAsync();

        _notifications.Should().ContainSingle();
        TitleOf(_notifications[0]).Should().Be("External access ends today");
    }

    // ── Who is reminded ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_ContactAndOrganizationGrants_NeverAddressTheGrantee()
    {
        Seed(7, grantedBy: null, creator: RecordCreator);
        Seed(7, grantedBy: null, creator: RecordCreator, organizationGrant: true);

        await RunAsync();

        _notifications.Should().HaveCount(2);
        _notifications.Select(n => n.GetAttributeValue<EntityReference>("ownerid"))
            .Should().OnlyContain(owner => owner.LogicalName == "systemuser" && owner.Id == RecordCreator);
        _notifications.Select(RecipientOf).Should().NotContain(new[] { ContactId, OrganizationId });
    }

    [Fact]
    public async Task ExecuteAsync_NoGranter_RemindsTheRecordOwner()
    {
        Seed(7, grantedBy: null, owner: RecordOwner, creator: RecordCreator);

        await RunAsync();

        _notifications.Select(RecipientOf).Should().Equal(RecordOwner);
    }

    [Fact]
    public async Task ExecuteAsync_NoGranterAndTeamOwnedRecord_RemindsTheRecordCreator()
    {
        // A team-owned record has no owninguser — the shape of 10 of 11 live root records (task 100 notes).
        Seed(7, grantedBy: null, owner: null, creator: RecordCreator);

        await RunAsync();

        _notifications.Select(RecipientOf).Should().Equal(RecordCreator);
    }

    [Theory]
    [InlineData("application user")]
    [InlineData("disabled user")]
    [InlineData("support user")]
    [InlineData("delegated admin")]
    public async Task ExecuteAsync_GranterIsNotAPersonToRemind_FallsThroughToTheRecordOwner(string granterKind)
    {
        var granter = granterKind switch
        {
            "application user" => ApplicationUser,
            "disabled user" => DisabledUser,
            "support user" => SupportUser,
            _ => DelegatedAdmin,
        };
        Seed(7, grantedBy: granter, owner: RecordOwner);

        await RunAsync();

        _notifications.Select(RecipientOf).Should().Equal(RecordOwner);
    }

    [Theory]
    [InlineData("administrative")]
    [InlineData("read")]
    public async Task ExecuteAsync_GranterWithAdministrativeOrReadAccessMode_IsReminded(string accessMode)
    {
        var granter = accessMode == "administrative" ? AdministrativeUser : ReadAccessUser;
        Seed(7, grantedBy: granter, owner: RecordOwner);

        await RunAsync();

        _notifications.Select(RecipientOf).Should().Equal(granter);
    }

    [Fact]
    public async Task ExecuteAsync_DisabledRecordOwner_RemindsTheRecordCreator()
    {
        Seed(7, grantedBy: null, owner: DisabledUser, creator: RecordCreator);

        await RunAsync();

        _notifications.Select(RecipientOf).Should().Equal(RecordCreator);
    }

    [Fact]
    public async Task ExecuteAsync_NoPersonAnywhereInTheChain_CountsUnroutableEveryRunAndLogsAnErrorOncePerThreshold()
    {
        Seed(7, grantedBy: ApplicationUser, owner: DisabledUser, creator: SupportUser);

        var first = await RunAsync();
        var second = await RunAsync();

        _notifications.Should().BeEmpty("an unroutable grant is never redirected — least of all to the grantee");
        _log.Entries.Where(e => e.Message.Contains("heartbeat")).Select(e => e.State["Unroutable"]).Should().Equal(1, 1);
        _log.Entries.Count(e => e.Level == LogLevel.Error && e.Message.Contains("UNROUTABLE")).Should().Be(1);
        first.Success.Should().BeTrue("the run itself worked; the unroutable grant is reported, not a run failure");
        second.Success.Should().BeTrue();
    }

    [Theory]
    [InlineData("sprk_project", "project")]
    [InlineData("sprk_matter", "matter")]
    [InlineData("sprk_workassignment", "work assignment")]
    public async Task ExecuteAsync_EachGrantableRoot_RemindsItsOwnerAndLinksToIt(string rootEntity, string label)
    {
        var rootId = Seed(7, grantedBy: null, owner: RecordOwner, rootEntity: rootEntity, rootName: "Harbor Tower");

        await RunAsync();

        _notifications.Select(RecipientOf).Should().Equal(RecordOwner);
        BodyOf(_notifications[0]).Should().StartWith($"Access for Jane Doe to the {label} \"Harbor Tower\" ends after ");
        _notifications[0].GetAttributeValue<string>("data").Should()
            .Contain($"/main.aspx?etn={rootEntity}\\u0026id={rootId}\\u0026pagetype=entityrecord");
    }

    // ── What it says ────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_Reminder_SaysWhoLosesAccessToWhichRecordAndThroughWhichDate()
    {
        Seed(14, grantedBy: Granter, rootName: "Smith v. Smith");
        Seed(14, grantedBy: Granter, rootName: "Smith v. Smith", organizationGrant: true);

        await RunAsync();

        var expires = Today.AddDays(14).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        _notifications.Select(BodyOf).Should().BeEquivalentTo(
            $"Access for Jane Doe to the matter \"Smith v. Smith\" ends after {expires}. To keep it, set a new expiration date in Manage Access on the matter.",
            $"Access for members of Morrison Foerster LLP to the matter \"Smith v. Smith\" ends after {expires}. To keep it, set a new expiration date in Manage Access on the matter.");
    }

    // ── Once, and cheaply ───────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_RunTwiceOnTheSameDay_SendsNoDuplicates()
    {
        Seed(7, grantedBy: Granter);

        await RunAsync();
        await RunAsync();

        _notifications.Should().ContainSingle();
        var second = Heartbeat(last: true);
        second["Sent"].Should().Be(0);
        second["AlreadySent"].Should().Be(1);
    }

    [Fact]
    public async Task ExecuteAsync_ClaimShowsTheReminderWasSentAfterTheFirstCheck_DoesNotSendIt()
    {
        // Another holder sends and marks the reminder between this run's first check and its claim.
        var job = BuildJob(_ => new LosesTheRaceIdempotency());
        Seed(7, grantedBy: Granter);

        await job.ExecuteAsync(Context(), CancellationToken.None);

        _notifications.Should().BeEmpty("the check under the claim sees the marker the other holder wrote");
        Heartbeat()["AlreadySent"].Should().Be(1);
    }

    [Fact]
    public async Task ExecuteAsync_MarkerDoesNotStick_SendsButCountsIt()
    {
        var job = BuildJob(_ => new ForgetfulIdempotency());
        Seed(7, grantedBy: Granter);

        var result = await job.ExecuteAsync(Context(), CancellationToken.None);

        _notifications.Should().ContainSingle();
        Heartbeat()["MarkFailed"].Should().Be(1, "the idempotency service swallows its own write failures, so the job reads the marker back");
        ResultOf(result).GetProperty("markFailed").GetInt32().Should().Be(1);
    }

    [Fact]
    public async Task ExecuteAsync_ManyGrantsInTheWindow_IssuesExactlyOneQuery()
    {
        foreach (var days in new[] { 30, 14, 7, 3, 1 })
        {
            Seed(days, grantedBy: Granter);
            Seed(days, grantedBy: null, owner: RecordOwner);
            Seed(days, grantedBy: null, creator: RecordCreator);
        }
        Seed(20, grantedBy: Granter);
        Seed(45, grantedBy: Granter);

        await RunAsync();

        _queries.Should().Be(1);
        _notifications.Should().HaveCount(16, "fifteen on their threshold day, one caught up at 20 days; 45 days out is outside the window");
    }

    [Fact]
    public async Task ExecuteAsync_MoreGrantsThanOnePage_PagesAndRemindsEveryGrant()
    {
        for (var i = 0; i <= GrantExpiryReminderJob.PageSize; i++)
        {
            Seed(7, grantedBy: Granter);
        }

        await RunAsync();

        _queries.Should().Be(2);
        _notifications.Should().HaveCount(GrantExpiryReminderJob.PageSize + 1);
    }

    [Fact]
    public async Task ExecuteAsync_PagingThatNeverEnds_StopsAtTheCapAndReportsTruncated()
    {
        _dataverse.AlwaysMoreRecords = true;

        var result = await RunAsync();

        _queries.Should().Be(GrantExpiryReminderJob.MaxPages);
        result.Success.Should().BeFalse();
        Heartbeat()["Truncated"].Should().Be(true);
        Heartbeat()["Status"].Should().Be("partial");
    }

    // ── Failures: which ones the scheduler retries ─────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_WriteFailsThenTheJobRunsAgainTheSameDay_SendsExactlyOnce()
    {
        Seed(7, grantedBy: Granter);
        _createFailures.Enqueue(new InvalidOperationException("Dataverse rejected the write."));

        var first = await RunAsync();
        await RunAsync();

        first.Success.Should().BeFalse();
        Heartbeat(index: 0)["Failed"].Should().Be(1);
        _notifications.Should().ContainSingle("the failed reminder released its claim and was not marked sent");
    }

    [Fact]
    public async Task ExecuteAsync_LastDayWriteFailsTransiently_EmitsTheHeartbeatThenThrowsSoTheSchedulerRetries()
    {
        Seed(0, grantedBy: Granter);
        _createFailures.Enqueue(new InvalidOperationException("Failed to create notification", new TimeoutException("Dataverse timed out.")));

        var act = async () => await RunAsync();

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*no later run can send them*");
        Heartbeat()["LastDayFailed"].Should().Be(1);
    }

    [Fact]
    public async Task ExecuteAsync_RetryAfterALastDayFailure_SendsOnlyWhatWasNotSent()
    {
        Seed(0, grantedBy: Granter);
        Seed(0, grantedBy: Granter);
        _createFailures.Enqueue(new InvalidOperationException("Failed to create notification", new TimeoutException("timed out")));

        var first = async () => await RunAsync(attempt: 1);
        await first.Should().ThrowAsync<InvalidOperationException>();
        await RunAsync(attempt: 2);

        _notifications.Should().HaveCount(2, "the retry sends the failed one and skips the one already sent");
        Heartbeat(last: true)["Sent"].Should().Be(1);
        Heartbeat(last: true)["AlreadySent"].Should().Be(1);
    }

    [Fact]
    public async Task ExecuteAsync_LastDayRejectionIsPermanent_IsCountedNotRetried()
    {
        Seed(0, grantedBy: Granter);
        _createFailures.Enqueue(new InvalidOperationException(
            "Failed to create notification",
            new FaultException<OrganizationServiceFault>(
                new OrganizationServiceFault { ErrorCode = -2147220960, Message = "Principal user is missing a privilege." },
                new FaultReason("Principal user is missing a privilege."))));

        var result = await RunAsync();

        result.Success.Should().BeFalse();
        Heartbeat()["Failed"].Should().Be(1);
        Heartbeat()["LastDayFailed"].Should().Be(0, "another attempt would be refused the same way");
    }

    [Fact]
    public async Task ExecuteAsync_LastDayClaimStillHeld_ThrowsSoTheSchedulerRetries()
    {
        var job = BuildJob(_ => new StuckClaimIdempotency());
        Seed(0, grantedBy: Granter);

        var act = async () => await job.ExecuteAsync(Context(), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
        Heartbeat()["ClaimHeld"].Should().Be(1);
        _notifications.Should().BeEmpty();
    }

    [Fact]
    public async Task ExecuteAsync_EarlierDayClaimStillHeld_IsReportedNotThrown()
    {
        var job = BuildJob(_ => new StuckClaimIdempotency());
        Seed(7, grantedBy: Granter);

        var result = await job.ExecuteAsync(Context(), CancellationToken.None);

        result.Success.Should().BeFalse("a held claim is not the same as 'already sent'");
        ResultOf(result).GetProperty("claimHeld").GetInt32().Should().Be(1);
        ResultOf(result).GetProperty("alreadySent").GetInt32().Should().Be(0);
    }

    [Fact]
    public async Task ExecuteAsync_EarlierDayWriteFails_CompletesWithoutThrowingForTomorrowToCatchUp()
    {
        Seed(3, grantedBy: Granter);
        _createFailures.Enqueue(new InvalidOperationException("Dataverse rejected the write."));

        var result = await RunAsync();

        result.Success.Should().BeFalse();
        Heartbeat()["Status"].Should().Be("partial");
        Heartbeat()["LastDayFailed"].Should().Be(0);
    }

    [Fact]
    public async Task ExecuteAsync_OneRowFailsUnexpectedly_TheOtherGrantsAreStillReminded()
    {
        var job = BuildJob(_ => new FailsFirstCheckIdempotency(new IdempotencyService(_cache, NullLogger<IdempotencyService>.Instance)));
        Seed(7, grantedBy: Granter);
        Seed(7, grantedBy: Granter);

        var result = await job.ExecuteAsync(Context(), CancellationToken.None);

        _notifications.Should().ContainSingle("the second grant is still processed");
        ResultOf(result).GetProperty("failed").GetInt32().Should().Be(1);
    }

    [Fact]
    public async Task ExecuteAsync_QueryFails_EmitsAnErrorHeartbeatThenThrows()
    {
        Seed(7, grantedBy: Granter);
        _dataverse.QueryFailure = new InvalidOperationException("Dataverse is unavailable.");

        var act = async () => await RunAsync(attempt: 2);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("Dataverse is unavailable.");
        var heartbeat = _log.Entries.Single(e => e.Message.Contains("heartbeat"));
        heartbeat.State["Status"].Should().Be("error");
        heartbeat.State["Attempt"].Should().Be(2);
        heartbeat.Level.Should().Be(LogLevel.Warning);
        _notifications.Should().BeEmpty();
    }

    [Fact]
    public async Task ExecuteAsync_CancelledDuringASend_ReportsCancelledAndLeavesTheReminderUnsent()
    {
        Seed(7, grantedBy: Granter);
        using var cts = new CancellationTokenSource();
        _entityService
            .Setup(s => s.CreateAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>()))
            .Returns((Entity _, CancellationToken _) =>
            {
                cts.Cancel();
                throw new OperationCanceledException(cts.Token);
            });

        var result = await _job.ExecuteAsync(Context(), cts.Token);

        ResultOf(result).GetProperty("status").GetString().Should().Be("cancelled");
        ResultOf(result).GetProperty("failed").GetInt32().Should().Be(0);

        // The claim was released and nothing was marked: the next run sends it.
        SetUpCreate();
        await RunAsync();
        _notifications.Should().ContainSingle();
    }

    [Fact]
    public async Task ExecuteAsync_CancelledAfterALastDayFailure_DoesNotThrow()
    {
        Seed(0, grantedBy: Granter);
        Seed(7, grantedBy: Granter);
        using var cts = new CancellationTokenSource();
        var calls = 0;
        _entityService
            .Setup(s => s.CreateAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>()))
            .Returns((Entity _, CancellationToken _) =>
            {
                if (++calls == 1)
                {
                    throw new TimeoutException("timed out");
                }

                cts.Cancel();
                throw new OperationCanceledException(cts.Token);
            });

        var result = await _job.ExecuteAsync(Context(), cts.Token);

        ResultOf(result).GetProperty("status").GetString().Should().Be("cancelled");
        ResultOf(result).GetProperty("lastDayFailed").GetInt32().Should().Be(1);
    }

    [Fact]
    public async Task ExecuteAsync_SendTimesOut_CountsAsFailedNotCancelled()
    {
        Seed(7, grantedBy: Granter);
        _createFailures.Enqueue(new TaskCanceledException("The request timed out."));

        var result = await RunAsync();

        ResultOf(result).GetProperty("status").GetString().Should().Be("partial");
        ResultOf(result).GetProperty("failed").GetInt32().Should().Be(1);
    }

    // ── Heartbeat and run record ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_NothingToSend_StillEmitsAHeartbeatThatSaysOk()
    {
        Seed(45, grantedBy: Granter);

        var result = await RunAsync();

        var heartbeat = Heartbeat();
        heartbeat["Status"].Should().Be("ok");
        heartbeat["InWindow"].Should().Be(0);
        heartbeat["Sent"].Should().Be(0);
        heartbeat["Today"].Should().Be("2026-09-12");
        result.Success.Should().BeTrue();
        result.ProcessedItems.Should().Be(0);
    }

    [Fact]
    public async Task ExecuteAsync_RunRecord_BreaksDownWhereTheRemindersWent()
    {
        Seed(7, grantedBy: Granter);
        Seed(7, grantedBy: null, owner: RecordOwner);
        Seed(7, grantedBy: null, creator: RecordCreator);
        Seed(7, grantedBy: ApplicationUser);

        var result = await RunAsync(attempt: 2);

        var run = ResultOf(result);
        run.GetProperty("status").GetString().Should().Be("ok");
        run.GetProperty("attempt").GetInt32().Should().Be(2);
        run.GetProperty("inWindow").GetInt32().Should().Be(4);
        run.GetProperty("sent").GetInt32().Should().Be(3);
        run.GetProperty("unroutable").GetInt32().Should().Be(1);
        var sentTo = run.GetProperty("sentTo");
        sentTo.GetProperty("granter").GetInt32().Should().Be(1);
        sentTo.GetProperty("recordOwner").GetInt32().Should().Be(1);
        sentTo.GetProperty("recordCreator").GetInt32().Should().Be(1);
        Heartbeat()["Attempt"].Should().Be(2);
    }

    // ── Helpers ─────────────────────────────────────────────────────────────────────────────────

    private void SetUpCreate()
        => _entityService
            .Setup(s => s.CreateAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Entity entity, CancellationToken _) =>
            {
                if (_createFailures.TryDequeue(out var failure))
                {
                    throw failure;
                }

                _notifications.Add(entity);
                return Guid.NewGuid();
            });

    private GrantExpiryReminderJob BuildJob(Func<IServiceProvider, IIdempotencyService> idempotency)
    {
        var services = new ServiceCollection();
        services.AddSingleton(_entityService.Object);
        services.AddSingleton(new NotificationService(_entityService.Object, NullLogger<NotificationService>.Instance));
        services.AddScoped(idempotency);

        return new GrantExpiryReminderJob(
            services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
            _time,
            _log);
    }

    private static JobRunContext Context(int attempt = 1)
        => new(Guid.NewGuid(), "test-correlation", JobRunTrigger.Scheduled, new Dictionary<string, object>(), attempt);

    private Task<JobRunResult> RunAsync(int attempt = 1)
        => _job.ExecuteAsync(Context(attempt), CancellationToken.None);

    /// <summary>Adds one grant on its own root record. Returns the root id.</summary>
    private Guid Seed(
        int daysLeft,
        Guid? grantedBy,
        Guid? owner = null,
        Guid? creator = null,
        int stateCode = 0,
        bool organizationGrant = false,
        string rootEntity = "sprk_matter",
        string rootName = "Real Estate Transaction Matter")
    {
        var rootId = Guid.NewGuid();
        _dataverse.Roots[(rootEntity, rootId)] = new RootRow(rootName, owner, creator);
        _dataverse.Grants.Add(new GrantRow(
            Id: Guid.NewGuid(),
            StateCode: stateCode,
            Expires: Today.AddDays(daysLeft),
            RootEntity: rootEntity,
            RootId: rootId,
            ContactId: organizationGrant ? null : ContactId,
            OrganizationId: organizationGrant ? OrganizationId : null,
            GrantedBy: grantedBy));
        return rootId;
    }

    private static Guid RecipientOf(Entity notification) => notification.GetAttributeValue<EntityReference>("ownerid").Id;

    private static string TitleOf(Entity notification) => notification.GetAttributeValue<string>("title");

    private static string BodyOf(Entity notification) => notification.GetAttributeValue<string>("body");

    private static JsonElement ResultOf(JobRunResult result) => JsonDocument.Parse(result.ResultJson!).RootElement;

    private IReadOnlyDictionary<string, object?> Heartbeat(bool last = false, int? index = null)
    {
        var heartbeats = _log.Entries.Where(e => e.Message.Contains("heartbeat")).ToList();
        heartbeats.Should().NotBeEmpty("every attempt must emit a heartbeat");
        return index is { } i ? heartbeats[i].State : last ? heartbeats[^1].State : heartbeats.Single().State;
    }

    /// <summary>
    /// Says "not sent" at the first check and "sent" at the check made under the claim — the interleaving where
    /// another holder sends between the two.
    /// </summary>
    private sealed class LosesTheRaceIdempotency : IIdempotencyService
    {
        private int _checks;

        public Task<bool> IsEventProcessedAsync(string eventId, CancellationToken cancellationToken = default)
            => Task.FromResult(Interlocked.Increment(ref _checks) > 1);

        public Task MarkEventAsProcessedAsync(string eventId, TimeSpan? expiration = null, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<bool> TryAcquireProcessingLockAsync(string eventId, TimeSpan? lockDuration = null, CancellationToken cancellationToken = default)
            => Task.FromResult(true);

        public Task ReleaseProcessingLockAsync(string eventId, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    /// <summary>A cache that lost its write — the way <see cref="IdempotencyService"/> behaves when Redis blips: the mark does nothing and says nothing.</summary>
    private sealed class ForgetfulIdempotency : IIdempotencyService
    {
        public Task<bool> IsEventProcessedAsync(string eventId, CancellationToken cancellationToken = default) => Task.FromResult(false);

        public Task MarkEventAsProcessedAsync(string eventId, TimeSpan? expiration = null, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<bool> TryAcquireProcessingLockAsync(string eventId, TimeSpan? lockDuration = null, CancellationToken cancellationToken = default) => Task.FromResult(true);

        public Task ReleaseProcessingLockAsync(string eventId, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    /// <summary>A claim left behind by a release that failed: never sent, but the claim cannot be taken.</summary>
    private sealed class StuckClaimIdempotency : IIdempotencyService
    {
        public Task<bool> IsEventProcessedAsync(string eventId, CancellationToken cancellationToken = default) => Task.FromResult(false);

        public Task MarkEventAsProcessedAsync(string eventId, TimeSpan? expiration = null, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<bool> TryAcquireProcessingLockAsync(string eventId, TimeSpan? lockDuration = null, CancellationToken cancellationToken = default) => Task.FromResult(false);

        public Task ReleaseProcessingLockAsync(string eventId, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    /// <summary>Throws on the very first check, then delegates — one row failing for a reason nobody anticipated.</summary>
    private sealed class FailsFirstCheckIdempotency(IIdempotencyService inner) : IIdempotencyService
    {
        private int _checks;

        public Task<bool> IsEventProcessedAsync(string eventId, CancellationToken cancellationToken = default)
            => Interlocked.Increment(ref _checks) == 1
                ? throw new InvalidOperationException("Unexpected cache state.")
                : inner.IsEventProcessedAsync(eventId, cancellationToken);

        public Task MarkEventAsProcessedAsync(string eventId, TimeSpan? expiration = null, CancellationToken cancellationToken = default)
            => inner.MarkEventAsProcessedAsync(eventId, expiration, cancellationToken);

        public Task<bool> TryAcquireProcessingLockAsync(string eventId, TimeSpan? lockDuration = null, CancellationToken cancellationToken = default)
            => inner.TryAcquireProcessingLockAsync(eventId, lockDuration, cancellationToken);

        public Task ReleaseProcessingLockAsync(string eventId, CancellationToken cancellationToken = default)
            => inner.ReleaseProcessingLockAsync(eventId, cancellationToken);
    }

    /// <summary>An <see cref="IDistributedCache"/> whose entries expire on the test's fake clock.</summary>
    private sealed class FakeClockDistributedCache(TimeProvider time) : IDistributedCache
    {
        private readonly Dictionary<string, (byte[] Value, DateTimeOffset? ExpiresAt)> _entries = new(StringComparer.Ordinal);

        public byte[]? Get(string key)
        {
            lock (_entries)
            {
                if (!_entries.TryGetValue(key, out var entry))
                {
                    return null;
                }

                if (entry.ExpiresAt is { } expiresAt && expiresAt <= time.GetUtcNow())
                {
                    _entries.Remove(key);
                    return null;
                }

                return entry.Value;
            }
        }

        public Task<byte[]?> GetAsync(string key, CancellationToken token = default) => Task.FromResult(Get(key));

        public void Set(string key, byte[] value, DistributedCacheEntryOptions options)
        {
            var now = time.GetUtcNow();
            DateTimeOffset? expiresAt = null;
            if (options.AbsoluteExpiration is { } absolute)
            {
                expiresAt = absolute;
            }
            else if (options.AbsoluteExpirationRelativeToNow is { } relative)
            {
                expiresAt = now + relative;
            }
            else if (options.SlidingExpiration is { } sliding)
            {
                expiresAt = now + sliding;
            }

            lock (_entries)
            {
                _entries[key] = (value, expiresAt);
            }
        }

        public Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options, CancellationToken token = default)
        {
            Set(key, value, options);
            return Task.CompletedTask;
        }

        public void Refresh(string key)
        {
        }

        public Task RefreshAsync(string key, CancellationToken token = default) => Task.CompletedTask;

        public void Remove(string key)
        {
            lock (_entries)
            {
                _entries.Remove(key);
            }
        }

        public Task RemoveAsync(string key, CancellationToken token = default)
        {
            Remove(key);
            return Task.CompletedTask;
        }
    }

    private sealed class CapturingLogger : ILogger<GrantExpiryReminderJob>
    {
        public List<(LogLevel Level, string Message, IReadOnlyDictionary<string, object?> State)> Entries { get; } = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            var values = state as IEnumerable<KeyValuePair<string, object?>> ?? Array.Empty<KeyValuePair<string, object?>>();
            Entries.Add((logLevel, formatter(state, exception), values.ToDictionary(kv => kv.Key, kv => kv.Value)));
        }
    }

    private sealed record GrantRow(
        Guid Id, int StateCode, DateOnly? Expires, string RootEntity, Guid RootId, Guid? ContactId, Guid? OrganizationId, Guid? GrantedBy);

    private sealed record RootRow(string Name, Guid? OwningUser, Guid? CreatedBy);

    /// <summary>A systemuser. <c>AccessMode</c>: 0 Read-Write, 1 Administrative, 2 Read, 3 Support, 5 Delegated Admin.</summary>
    private sealed record UserRow(bool IsDisabled, bool IsApplication, int AccessMode = 0);

    /// <summary>
    /// Evaluates the job's FetchXML against in-memory tables shaped like the live schema (task 100 notes), including
    /// <c>count</c>/<c>page</c> paging. Throws on anything it does not model.
    /// </summary>
    private sealed class FakeDataverse
    {
        private static readonly Dictionary<string, string> PrimaryIds = new(StringComparer.Ordinal)
        {
            ["systemuser"] = "systemuserid",
            ["contact"] = "contactid",
            ["sprk_organization"] = "sprk_organizationid",
            ["sprk_project"] = "sprk_projectid",
            ["sprk_matter"] = "sprk_matterid",
            ["sprk_workassignment"] = "sprk_workassignmentid",
        };

        private static readonly Dictionary<string, string> RootNameColumns = new(StringComparer.Ordinal)
        {
            ["sprk_project"] = "sprk_projectname",
            ["sprk_matter"] = "sprk_mattername",
            ["sprk_workassignment"] = "sprk_name",
        };

        public List<GrantRow> Grants { get; } = new();
        public Dictionary<(string Entity, Guid Id), RootRow> Roots { get; } = new();
        public Dictionary<Guid, UserRow> Users { get; } = new();
        public Dictionary<Guid, string> Contacts { get; } = new();
        public Dictionary<Guid, string> Organizations { get; } = new();
        public Exception? QueryFailure { get; set; }

        /// <summary>A server whose paging never ends — every page is empty and says there is more.</summary>
        public bool AlwaysMoreRecords { get; set; }

        public EntityCollection Execute(string fetchXml)
        {
            if (QueryFailure is not null)
            {
                throw QueryFailure;
            }

            var fetch = XElement.Parse(fetchXml);
            var pageSize = int.Parse(fetch.Attribute("count")!.Value, CultureInfo.InvariantCulture);
            var page = int.Parse(fetch.Attribute("page")!.Value, CultureInfo.InvariantCulture);

            if (AlwaysMoreRecords)
            {
                return new EntityCollection { EntityName = "sprk_externalrecordaccess", MoreRecords = true, PagingCookie = $"<cookie page=\"{page}\" />" };
            }

            var entity = fetch.Element("entity") ?? throw new InvalidOperationException("FetchXML has no <entity>.");
            if (entity.Attribute("name")?.Value != "sprk_externalrecordaccess")
            {
                throw new InvalidOperationException($"Unexpected entity '{entity.Attribute("name")?.Value}'.");
            }

            foreach (var order in entity.Elements("order"))
            {
                if (order.Attribute("attribute")?.Value != "sprk_externalrecordaccessid")
                {
                    throw new InvalidOperationException($"Ordering by '{order.Attribute("attribute")?.Value}' is not modelled.");
                }
            }

            IEnumerable<GrantRow> rows = Grants;
            foreach (var filter in entity.Elements("filter"))
            {
                if (filter.Attribute("type")?.Value != "and")
                {
                    throw new InvalidOperationException("Only an 'and' filter is modelled.");
                }

                foreach (var condition in filter.Elements())
                {
                    rows = ApplyCondition(rows, condition).ToList();
                }
            }

            var selected = entity.Elements("attribute").Select(a => a.Attribute("name")!.Value).ToList();
            var matched = new List<Entity>();

            foreach (var row in rows)
            {
                var output = new Entity("sprk_externalrecordaccess", row.Id);
                foreach (var column in selected)
                {
                    if (GrantValue(row, column) is { } value)
                    {
                        output[column] = value;
                    }
                }

                var keep = entity.Elements("link-entity")
                    .All(link => ApplyLink(output, link, GrantJoinValue(row, link.Attribute("to")!.Value)));
                if (keep)
                {
                    matched.Add(output);
                }
            }

            var result = new EntityCollection { EntityName = "sprk_externalrecordaccess" };
            result.Entities.AddRange(matched.Skip((page - 1) * pageSize).Take(pageSize));
            result.MoreRecords = matched.Count > page * pageSize;
            result.PagingCookie = result.MoreRecords ? $"<cookie page=\"{page}\" />" : null;
            return result;
        }

        private static IEnumerable<GrantRow> ApplyCondition(IEnumerable<GrantRow> rows, XElement condition)
        {
            if (condition.Name != "condition")
            {
                throw new InvalidOperationException($"Nested <{condition.Name}> is not modelled.");
            }

            var attribute = condition.Attribute("attribute")!.Value;
            var op = condition.Attribute("operator")!.Value;

            return (attribute, op) switch
            {
                ("statecode", "eq") => rows.Where(r => r.StateCode == int.Parse(condition.Attribute("value")!.Value, CultureInfo.InvariantCulture)),
                ("sprk_expiresdate", "ge") => rows.Where(r => r.Expires is { } expires && expires >= DateValue(condition)),
                ("sprk_expiresdate", "le") => rows.Where(r => r.Expires is { } expires && expires <= DateValue(condition)),
                _ => throw new InvalidOperationException(
                    $"This fake does not model the condition '{attribute} {op}' — extend it deliberately rather than let it pass unevaluated."),
            };
        }

        private static DateOnly DateValue(XElement condition)
            => DateOnly.ParseExact(condition.Attribute("value")!.Value, "yyyy-MM-dd", CultureInfo.InvariantCulture);

        private static object? GrantValue(GrantRow row, string column) => column switch
        {
            "sprk_externalrecordaccessid" => row.Id,
            "sprk_expiresdate" => row.Expires?.ToDateTime(TimeOnly.MinValue),
            "sprk_contact" => row.ContactId is { } c ? new EntityReference("contact", c) : null,
            "sprk_organization" => row.OrganizationId is { } o ? new EntityReference("sprk_organization", o) : null,
            "sprk_grantedby" => row.GrantedBy is { } g ? new EntityReference("systemuser", g) : null,
            "sprk_project" or "sprk_matter" or "sprk_workassignment" =>
                row.RootEntity == column ? new EntityReference(column, row.RootId) : null,
            _ => throw new InvalidOperationException($"sprk_externalrecordaccess has no column '{column}' in this fake's model."),
        };

        private static Guid? GrantJoinValue(GrantRow row, string to)
            => (GrantValue(row, to) as EntityReference)?.Id;

        /// <returns><c>false</c> when an INNER join finds no match — Dataverse drops the row.</returns>
        private bool ApplyLink(Entity output, XElement link, Guid? parentValue)
        {
            var name = link.Attribute("name")!.Value;
            var alias = link.Attribute("alias")?.Value
                ?? throw new InvalidOperationException($"link-entity '{name}' has no alias; the job reads aliased columns.");
            var outer = link.Attribute("link-type")?.Value == "outer";

            if (!PrimaryIds.TryGetValue(name, out var primaryId) || link.Attribute("from")?.Value != primaryId)
            {
                throw new InvalidOperationException($"link-entity '{name}' from '{link.Attribute("from")?.Value}' is not modelled.");
            }

            if (link.Elements("filter").Any())
            {
                throw new InvalidOperationException("A filter inside a link-entity is not modelled.");
            }

            if (parentValue is not { } id || !Exists(name, id))
            {
                return outer;
            }

            foreach (var column in link.Elements("attribute").Select(a => a.Attribute("name")!.Value))
            {
                if (TargetValue(name, id, column) is { } value)
                {
                    output[$"{alias}.{column}"] = new AliasedValue(name, column, value);
                }
            }

            foreach (var nested in link.Elements("link-entity"))
            {
                var nestedParent = (TargetValue(name, id, nested.Attribute("to")!.Value) as EntityReference)?.Id;
                if (!ApplyLink(output, nested, nestedParent))
                {
                    return false;
                }
            }

            return true;
        }

        private bool Exists(string entityName, Guid id) => entityName switch
        {
            "systemuser" => Users.ContainsKey(id),
            "contact" => Contacts.ContainsKey(id),
            "sprk_organization" => Organizations.ContainsKey(id),
            _ => Roots.ContainsKey((entityName, id)),
        };

        private object? TargetValue(string entityName, Guid id, string column)
        {
            switch (entityName)
            {
                case "systemuser":
                    var user = Users[id];
                    return column switch
                    {
                        "isdisabled" => user.IsDisabled,
                        "applicationid" => user.IsApplication ? Guid.Parse("b0000000-0000-0000-0000-00000000a991") : null,
                        "accessmode" => new OptionSetValue(user.AccessMode),
                        _ => throw new InvalidOperationException($"systemuser column '{column}' is not modelled."),
                    };
                case "contact":
                    return column == "fullname" ? Contacts[id] : throw new InvalidOperationException($"contact column '{column}' is not modelled.");
                case "sprk_organization":
                    return column == "sprk_organizationname" ? Organizations[id] : throw new InvalidOperationException($"sprk_organization column '{column}' is not modelled.");
                default:
                    var root = Roots[(entityName, id)];
                    if (column == RootNameColumns[entityName])
                    {
                        return root.Name;
                    }

                    return column switch
                    {
                        "owninguser" => root.OwningUser is { } owner ? new EntityReference("systemuser", owner) : null,
                        "createdby" => root.CreatedBy is { } creator ? new EntityReference("systemuser", creator) : null,
                        _ => throw new InvalidOperationException($"{entityName} column '{column}' is not modelled."),
                    };
            }
        }
    }
}
