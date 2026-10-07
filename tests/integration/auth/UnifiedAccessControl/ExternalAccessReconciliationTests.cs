using System.Globalization;
using System.Text.Json;
using System.Xml.Linq;
using FluentAssertions;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Spaarke.Dataverse;
using Spaarke.Scheduling;
using Sprk.Bff.Api.Infrastructure.DI;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Services.ExternalAccess;
using Sprk.Bff.Api.Services.Jobs;
using Xunit;

namespace Sprk.Bff.Api.Tests.AccessControl;

/// <summary>
/// Task 117 — owner decision D-1 option B, ISS-026 / #1006 write half, ISS-020 / #999 part 4. The scheduled
/// pass that makes an external-access row's OWN state the truth about whether it confers access.
///
/// <para><b>What these tests pin, and why each one is here.</b> The three reconciliation rules and their
/// negatives (an acceptance criterion each); the boundary cases that decide access — a NULL statecode is
/// ACTIVE, a membership holds THROUGH its end date, R2 beats R1 on one row; and the four SAFETY properties the
/// task treats as provable rather than described: <b>(S1)</b> report-only is the DEFAULT and writes nothing,
/// <b>(S2)</b> the job ships DISABLED, <b>(S3)</b> every write goes through a per-chunk atomic claim, and
/// <b>(S4)</b> a chunk is one all-or-nothing transaction so a fault leaves its rows unchanged without stopping
/// later chunks. Then the run-shape contract: idempotence, truncation, cancellation, a failed scan recorded
/// FAILED rather than "nothing to reconcile", and one heartbeat per attempt.</para>
///
/// <para><b>Why the fake does not evaluate the FetchXML filter.</b> Deliberate, and it makes the tests
/// stronger rather than weaker. <see cref="FakeDataverse"/> is a small ROW STORE: it serves every row of the
/// scanned table, pages it, and applies <c>BulkUpdateAsync</c> back into itself. So each rule is proven by the
/// IN-CODE decision (<c>PlanGrantChange</c> / <c>PlanMembershipChange</c>) with the server-side filter removed
/// as a variable — a row the filter would have excluded is fed in anyway, and must still be left alone. The
/// filter is a bound on volume, not on correctness, and it is pinned separately by direct assertions on the
/// emitted FetchXML. Applying the store's writes back is also what makes the idempotence test real: the second
/// run sees the rows the first run wrote.</para>
///
/// <para>Real collaborator: <see cref="IdempotencyService"/> over a real distributed cache, so the claim and
/// the completion marker behave as they will in production. The one controllable seam is
/// <see cref="ControllableIdempotency"/>, used only to hold a claim.</para>
/// </summary>
public class ExternalAccessReconciliationTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 21, 5, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = new(2026, 9, 21);

    private const string GrantEntity = ExternalGrantLifecycle.EntityLogicalName;
    private const string JunctionEntity = ExternalAccessReconciliationJob.JunctionEntityLogicalName;

    private static readonly Guid ActiveOrg = Guid.Parse("d0000000-0000-0000-0000-00000000000a");
    private static readonly Guid InactiveOrg = Guid.Parse("d0000000-0000-0000-0000-00000000000b");

    private readonly FakeDataverse _dataverse = new();
    private readonly ContactGrantTable _grants = new();
    private readonly GrantPolicyTestDoubles.FlagStubParticipationService _participations = new(RootRecordFlags.None);
    private readonly CapturingLogger _log = new();
    private readonly FakeTimeProvider _time = new(Now);
    private readonly ControllableIdempotency _idempotency;

    public ExternalAccessReconciliationTests()
    {
        _idempotency = new ControllableIdempotency(new IdempotencyService(
            new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions())),
            NullLogger<IdempotencyService>.Instance));
    }

    // ── R1: stamp the ONE defined default expiry ────────────────────────────────────────────────────

    [Fact]
    public async Task R1_AnActiveUndatedGrant_IsStampedWithTheOneDefinedDefaultExpiry()
    {
        var id = SeedGrant(expires: null);

        await RunAsync(writes: true);

        // The same value /grant writes — ExternalGrantLifecycle.DefaultExpiry — never a re-derived 90.
        Grant(id).GetAttributeValue<DateTime>(ExternalAccessReconciliationJob.ExpiresDateAttribute)
            .Should().Be(ExternalGrantLifecycle.ToSdkDateOnly(ExternalGrantLifecycle.DefaultExpiry(Today)));
        Rule("R1")["Changed"].Should().Be(1);
    }

    [Theory]
    [InlineData(-400)]  // long past
    [InlineData(-1)]    // yesterday
    [InlineData(0)]     // today
    [InlineData(1)]     // tomorrow
    [InlineData(4000)]  // far future
    public async Task R1_AGrantThatAlreadyCarriesAnyExpiry_IsLeftUnchanged(int offsetDays)
    {
        var existing = Today.AddDays(offsetDays);
        var id = SeedGrant(expires: existing);

        await RunAsync(writes: true);

        _dataverse.Writes.Should().BeEmpty();
        Grant(id).GetAttributeValue<DateTime>(ExternalAccessReconciliationJob.ExpiresDateAttribute)
            .Should().Be(ExternalGrantLifecycle.ToSdkDateOnly(existing));
    }

    // ── R2: an inactive organization's grants stop conferring ───────────────────────────────────────

    [Fact]
    public async Task R2_AnActiveGrantOfAnInactiveOrganization_IsDeactivatedWithBothStateAndStatus()
    {
        var id = SeedGrant(expires: Today.AddDays(30), organization: InactiveOrg, organizationState: 1);

        await RunAsync(writes: true);

        // statecode alone would leave an inconsistent status reason — both, always.
        State(Grant(id)).Should().Be(1);
        Status(Grant(id)).Should().Be(2);
        Rule("R2")["Changed"].Should().Be(1);
    }

    [Fact]
    public async Task R2_AGrantOfAnActiveOrganizationOrWithNoOrganizationAtAll_IsLeftUnchanged()
    {
        SeedGrant(expires: Today.AddDays(30), organization: ActiveOrg, organizationState: 0);
        SeedGrant(expires: Today.AddDays(30)); // contact-keyed: no organization lookup at all

        await RunAsync(writes: true);

        _dataverse.Writes.Should().BeEmpty();
        Rule("R2")["Planned"].Should().Be(0);
    }

    [Fact]
    public async Task R2_BeatsR1_OnARowThatMatchesBoth_SoOneRowNeverGetsTwoUpdates()
    {
        var id = SeedGrant(expires: null, organization: InactiveOrg, organizationState: 1);

        await RunAsync(writes: true);

        Rule("R2")["Planned"].Should().Be(1);
        Rule("R1")["Planned"].Should().Be(0, "stamping an expiry onto a row this run deactivates is wasted work");
        _dataverse.Writes.Should().ContainSingle();
        Grant(id).Contains(ExternalAccessReconciliationJob.ExpiresDateAttribute).Should().BeFalse();
        State(Grant(id)).Should().Be(1);
    }

    // ── R3: a membership ended by date stops conferring ─────────────────────────────────────────────

    [Fact]
    public async Task R3_AnActiveMembershipWhoseEndDateHasPassed_IsDeactivatedWithBothStateAndStatus()
    {
        var id = SeedMembership(end: Today.AddDays(-1));

        await RunAsync(writes: true);

        State(Membership(id)).Should().Be(1);
        Status(Membership(id)).Should().Be(2);
        Rule("R3")["Changed"].Should().Be(1);
    }

    [Theory]
    [InlineData(null)]  // open-ended: not ended
    [InlineData(0)]     // ends TODAY — access holds THROUGH the end date
    [InlineData(1)]
    [InlineData(365)]
    public async Task R3_AMembershipWithNoEndDateOrOneNotYetPassed_IsLeftUnchanged(int? offsetDays)
    {
        SeedMembership(end: offsetDays is { } d ? Today.AddDays(d) : null);

        await RunAsync(writes: true);

        _dataverse.Writes.Should().BeEmpty();
        Rule("R3")["Planned"].Should().Be(0);
    }

    // ── R4: a grant whose record is gone stops reading as active (owner round 71) ─────────────────────
    //
    // Deleting a matter or work assignment does not delete its grants: the lookup's delete behaviour is RemoveLink, so the row
    // survives ACTIVE with every record lookup empty. R4 deactivates it through the same chunked, claimed write path as R2/R3.

    [Theory]
    [InlineData("dated")]                 // the ordinary case: a contact grant with an expiry, record deleted
    [InlineData("undated")]               // R4 beats R1 — a row with no record is ended, never stamped +90
    [InlineData("inactive-organization")] // R4 beats R2 — one row, one update
    public async Task R4_AnActiveGrantWhoseRecordIsGone_IsDeactivatedWithBothStateAndStatus(string shape)
    {
        var id = shape switch
        {
            "undated" => SeedGrant(expires: null, recordGone: true),
            "inactive-organization" => SeedGrant(expires: Today.AddDays(30), organization: InactiveOrg, organizationState: 1, recordGone: true),
            _ => SeedGrant(expires: Today.AddDays(30), recordGone: true),
        };

        var result = await RunAsync(writes: true);

        State(Grant(id)).Should().Be(1);
        Status(Grant(id)).Should().Be(2);
        Grant(id).Contains(ExternalAccessReconciliationJob.ExpiresDateAttribute).Should().Be(shape != "undated",
            "an ended row is not also stamped");
        Rule("R4")["Planned"].Should().Be(1);
        Rule("R4")["Changed"].Should().Be(1);
        Rule("R1")["Planned"].Should().Be(0);
        Rule("R2")["Planned"].Should().Be(0);
        _dataverse.Writes.Should().ContainSingle().Which.Updates.Should().ContainSingle()
            .Which.Fields.Keys.Should().BeEquivalentTo(new[] { "statecode", "statuscode" });
        Heartbeat()["R4Planned"].Should().Be(1);
        Heartbeat()["R4Changed"].Should().Be(1);
        result.Success.Should().BeTrue(result.ErrorMessage);
        result.ProcessedItems.Should().Be(1);
    }

    /// <summary>
    /// A CONTACT-issued undated row whose record is gone ends under R4 — it is never handed to R1's issuer resolution (whose
    /// "the issuer holds a grant there" has no "there" to read).
    /// </summary>
    [Fact]
    public async Task R4_AContactIssuedUndatedRowWhoseRecordIsGone_IsEndedByR4_WithNoIssuerRead()
    {
        var row = SeedContactIssued(issuer: Issuer, level: Collaborate);
        Grant(row).Attributes.Remove("sprk_project"); // the record was deleted (RemoveLink)

        await RunAsync(writes: true);

        State(Grant(row)).Should().Be(1);
        Rule("R4")["Changed"].Should().Be(1);
        Rule("R1")["Planned"].Should().Be(0);
        ContactIssued().GetProperty("rows").GetArrayLength().Should().Be(0);
        AssertIssuerKept(row);
    }

    [Fact]
    public async Task R4_InReportOnlyMode_IsCountedAndListed_ButNothingIsWritten()
    {
        var id = SeedGrant(expires: Today.AddDays(30), recordGone: true);

        var result = await RunAsync(writes: null);

        _dataverse.Writes.Should().BeEmpty();
        State(Grant(id)).Should().Be(0);
        Rule("R4")["Planned"].Should().Be(1);
        Rule("R4")["Changed"].Should().Be(0);
        result.ProcessedItems.Should().Be(0);
        BeforeStateFor(id).Should().ContainSingle().Which.Should().Contain("mode=report-only")
            .And.Contain("sprk_matter=(empty)")
            .And.Contain("its record is gone");
    }

    [Theory]
    [InlineData("sprk_project")]
    [InlineData("sprk_matter")]
    [InlineData("sprk_workassignment")]
    public async Task R4_AGrantThatNamesALiveRecordThroughAnyOfItsLookups_IsLeftUnchanged(string recordLookup)
    {
        var id = SeedGrant(expires: Today.AddDays(30), recordLookup: recordLookup);

        await RunAsync(writes: true);

        _dataverse.Writes.Should().BeEmpty();
        State(Grant(id)).Should().Be(0);
        Rule("R4")["Planned"].Should().Be(0);
    }

    [Fact]
    public async Task R4_AnInactiveGrantWhoseRecordIsGone_IsNotTouched()
    {
        SeedGrant(expires: Today.AddDays(30), state: 1, recordGone: true);

        await RunAsync(writes: true);

        _dataverse.Writes.Should().BeEmpty();
        Rule("R4")["Planned"].Should().Be(0);
    }

    /// <summary>A grant scan that fails reconciles nothing — never a mass deactivation on an error — and the run is recorded failed.</summary>
    [Fact]
    public async Task R4_WhenTheGrantScanFails_AnOrphanIsLeftActive_AndTheRunIsRecordedFailed()
    {
        var id = SeedGrant(expires: Today.AddDays(30), recordGone: true);
        _dataverse.ScanFailures[GrantEntity] = new InvalidOperationException("Dataverse is unavailable");

        var result = await RunAsync(writes: true);

        result.Success.Should().BeFalse();
        _dataverse.Writes.Should().BeEmpty();
        State(Grant(id)).Should().Be(0);
        Rule("R4")["ScanFailed"].Should().Be(true);
        Rule("R4")["Planned"].Should().Be(0);
    }

    /// <summary>
    /// The scan must bring an orphan back even though it carries a date and no organization (R1's and R2's disjuncts both
    /// miss it), and it must SELECT every record lookup — one left out would read as empty and end a live row.
    /// </summary>
    [Fact]
    public void TheGrantScan_SelectsEveryRecordLookup_AndAdmitsARowWhoseRecordLookupsAreAllEmpty()
    {
        var fetch = XDocument.Parse(ExternalAccessReconciliationJob.BuildGrantScanFetchXml(1, null));
        var entity = fetch.Descendants("entity").Single();
        var lookups = ExternalAccessReconciliationJob.RootLookupAttributes.Select(r => r.Attribute).ToArray();

        entity.Elements("attribute").Select(a => (string?)a.Attribute("name")).Should().Contain(lookups);

        var recordGone = entity.Descendants("filter").Single(f =>
            (string?)f.Attribute("type") == "and"
            && f.Elements("condition").Any()
            && f.Elements("condition").All(c => (string?)c.Attribute("operator") == "null"));
        recordGone.Elements("condition").Select(c => (string?)c.Attribute("attribute")).Should().BeEquivalentTo(lookups);
        ((string?)recordGone.Parent!.Attribute("type")).Should().Be("or", "it is one more way a row can need reconciling");
    }

    // ── The boundary every rule shares ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task ARowWhoseStatecodeIsNull_IsTreatedAsActiveByEveryRule()
    {
        // ExternalGrantLifecycle.IsActive => StateCode is null or 0. A rule that skipped a null-statecode
        // row would leave exactly the rows an external writer is likeliest to create.
        var undated = SeedGrant(expires: null, state: null);
        var orphaned = SeedGrant(expires: Today.AddDays(30), organization: InactiveOrg, organizationState: 1, state: null);
        var ended = SeedMembership(end: Today.AddDays(-1), state: null);

        await RunAsync(writes: true);

        Grant(undated).Contains(ExternalAccessReconciliationJob.ExpiresDateAttribute).Should().BeTrue();
        State(Grant(orphaned)).Should().Be(1);
        State(Membership(ended)).Should().Be(1);
    }

    [Fact]
    public async Task AnAlreadyInactiveRow_IsNotTouchedByAnyRule()
    {
        SeedGrant(expires: null, state: 1);
        SeedMembership(end: Today.AddDays(-1), state: 1);

        await RunAsync(writes: true);

        _dataverse.Writes.Should().BeEmpty();
    }

    // ── S1: report-only is the DEFAULT, and it writes nothing ───────────────────────────────────────

    [Fact]
    public async Task S1_WithNoConfigurationAtAll_TheRunIsReportOnly_CountsAndListsEveryRow_AndWritesNothing()
    {
        var undated = SeedGrant(expires: null);
        var orphaned = SeedGrant(expires: Today.AddDays(30), organization: InactiveOrg, organizationState: 1);
        var ended = SeedMembership(end: Today.AddDays(-1));

        var result = await RunAsync(writes: null); // no key set at all — the shipping state

        _dataverse.Writes.Should().BeEmpty("report-only is the default and it must not write");
        Report().GetProperty("mode").GetString().Should().Be("report-only");
        Report().GetProperty("writesEnabled").GetBoolean().Should().BeFalse();

        Rule("R1")["Planned"].Should().Be(1);
        Rule("R2")["Planned"].Should().Be(1);
        Rule("R3")["Planned"].Should().Be(1);
        Rule("R1")["Changed"].Should().Be(0);
        Rule("R2")["Changed"].Should().Be(0);
        Rule("R3")["Changed"].Should().Be(0);
        result.ProcessedItems.Should().Be(0);

        // "Counts AND LISTS every row each rule would change" — the complete list is the before-state log.
        foreach (var id in new[] { undated, orphaned, ended })
        {
            BeforeStateFor(id).Should().ContainSingle().Which.Should().Contain("mode=report-only");
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("no")]
    [InlineData("1")]
    [InlineData("TRUE-ish")]
    public async Task S1_AnyConfigurationValueThatIsNotTrue_MeansReportOnly(string? value)
    {
        SeedGrant(expires: null);

        await RunAsync(writes: null, rawWritesValue: value);

        _dataverse.Writes.Should().BeEmpty(
            "the flag is named positively so every way of getting it wrong lands on writing nothing");
    }

    [Fact]
    public async Task S1_TheBeforeStateIsRecordedForEveryChangedRowInWriteModeToo()
    {
        var id = SeedGrant(expires: null);

        await RunAsync(writes: true);

        BeforeStateFor(id).Should().ContainSingle()
            .Which.Should().Contain("before=[statecode=0 expiresDate=(null)]").And.Contain("mode=write");
    }

    // ── S2: the job ships SCHEDULED and REPORT-ONLY (owner round 7 item 1, task 137) ─────────────────

    /// <remarks>
    /// Not an ADR-038 B3 wiring test. It asserts no <c>GetRequiredService(...) is not null</c> and no "the
    /// right type came back" — <c>Single(r =&gt; r.Job.JobId == …)</c> already fails if it did not. The
    /// assertions are the SHIPPING POSTURE the owner decided (task 137, owner round 7 item 1, 2026-10-02:
    /// "enable the schedule in report-only mode now; enable writes only after the owner has reviewed one
    /// report"): the schedule ticks daily, and with the shipping configuration — no
    /// <c>ExternalAccess:Reconciliation:WritesEnabled</c> key — a tick writes nothing. Perturbing the
    /// registration back to <c>enabled: false</c>, or changing the schedule, reddens it; the write side of the
    /// same posture is pinned by S1 (every value but <c>true</c> is report-only).
    /// </remarks>
    [Fact]
    public void S2_TheJobShipsScheduled_AndItsShippingConfigurationIsReportOnly()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddExternalAccess();

        using var provider = services.BuildServiceProvider();
        var registration = provider.GetServices<ScheduledJobRegistration>()
            .Single(r => r.Job.JobId == ExternalAccessReconciliationJob.JobIdConstant);

        registration.Enabled.Should().BeTrue(
            "the owner enabled the schedule in report-only mode so a report exists to review (round 7 item 1)");
        registration.CronSchedule.Should().Be("0 5 * * *", "daily at 05:00 UTC, an hour before the reminder sweep");
        ((ExternalAccessReconciliationJob)registration.Job).WritesEnabled.Should().BeFalse(
            "with no WritesEnabled key the scheduled tick is report-only — writes wait for the owner's review");
    }

    // ── S3: every write is taken under a per-chunk atomic claim ─────────────────────────────────────

    [Fact]
    public async Task S3_AChunkWhoseClaimIsAlreadyHeld_IsNotWritten_AndIsCounted()
    {
        SeedGrant(expires: null);
        _idempotency.RefuseLocks = true;

        await RunAsync(writes: true);

        _dataverse.Writes.Should().BeEmpty();
        Rule("R1")["ClaimHeld"].Should().Be(1);
        Rule("R1")["Changed"].Should().Be(0);
        Heartbeat()["Status"].Should().Be(ExternalAccessReconciliationJob.StatusPartial);
    }

    [Fact]
    public async Task S3_AChunkWhoseCompletionMarkerIsAlreadyPresent_IsNotWrittenAgain()
    {
        var id = SeedGrant(expires: null);
        await _idempotency.MarkEventAsProcessedAsync(
            ExternalAccessReconciliationJob.ClaimKey(ExternalAccessReconciliationJob.RuleStampDefaultExpiry, new[] { id }),
            TimeSpan.FromHours(1));

        await RunAsync(writes: true);

        _dataverse.Writes.Should().BeEmpty();
        Rule("R1")["AlreadyApplied"].Should().Be(1);
    }

    // ── S4: one chunk is one all-or-nothing transaction ─────────────────────────────────────────────

    [Fact]
    public async Task S4_AChunkWhoseTransactionFails_LeavesEveryRowInItUnchanged_AndLaterChunksStillApply()
    {
        // 150 rows across two chunks of 100 + 50. The first chunk's transaction faults.
        var ids = Enumerable.Range(0, 150).Select(_ => SeedGrant(expires: null)).ToList();
        _dataverse.WriteFailures.Enqueue(new InvalidOperationException("transaction rolled back"));

        var result = await RunAsync(writes: true);

        var stamped = ids.Count(id => Grant(id).Contains(ExternalAccessReconciliationJob.ExpiresDateAttribute));
        stamped.Should().Be(50, "the faulted chunk rolled back whole; the later chunk still applied");
        Rule("R1")["Failed"].Should().Be(100);
        Rule("R1")["Changed"].Should().Be(50);
        result.Success.Should().BeFalse();
        _dataverse.Writes.Should().HaveCount(2, "each chunk is its own all-or-nothing transaction");
        _dataverse.Writes[0].Updates.Should().HaveCount(ExternalAccessReconciliationJob.ChunkSize);
        _dataverse.Writes[0].Updates.Select(u => u.Id).Should().OnlyHaveUniqueItems(
            "one row must never appear twice in one transaction");
    }

    // ── Run shape ───────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ASecondRunImmediatelyAfterASuccessfulFirstOne_FindsNothingToChange()
    {
        SeedGrant(expires: null);
        SeedGrant(expires: Today.AddDays(30), organization: InactiveOrg, organizationState: 1);
        SeedMembership(end: Today.AddDays(-1));

        await RunAsync(writes: true);
        _dataverse.Writes.Clear();
        _log.Entries.Clear();

        var second = await RunAsync(writes: true);

        _dataverse.Writes.Should().BeEmpty();
        second.Success.Should().BeTrue();
        second.ProcessedItems.Should().Be(0);
        foreach (var rule in new[] { "R1", "R2", "R3" })
        {
            Rule(rule)["Planned"].Should().Be(0);
        }
    }

    [Fact]
    public async Task ARunWithNothingToDo_EmitsItsHeartbeatWithTheAttempt_AndSucceedsWithZeroProcessed()
    {
        var result = await RunAsync(writes: true, attempt: 3);

        result.Success.Should().BeTrue();
        result.ProcessedItems.Should().Be(0);
        result.ErrorMessage.Should().BeNull();

        // "Nothing to do" and "the job died" must not look alike.
        Heartbeat()["Status"].Should().Be(ExternalAccessReconciliationJob.StatusOk);
        Heartbeat()["Attempt"].Should().Be(3);
        Heartbeat()["Mode"].Should().Be("write");
    }

    [Fact]
    public async Task AScanThatFails_RecordsTheRunFailed_AndIsNeverReportedAsNothingToReconcile()
    {
        SeedGrant(expires: null);
        _dataverse.ScanFailures[GrantEntity] = new InvalidOperationException("Dataverse is unavailable");

        var result = await RunAsync(writes: true);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("the scan failed");
        _dataverse.Writes.Should().BeEmpty("an empty read is fail-SAFE for a writer — it must write nothing");

        Heartbeat()["Status"].Should().Be(ExternalAccessReconciliationJob.StatusError);
        Rule("R1")["ScanFailed"].Should().Be(true);
        Rule("R1")["Planned"].Should().Be(0);
        Rule("R3")["ScanFailed"].Should().Be(false, "one rule's scan failing does not stop the others");
    }

    [Fact]
    public async Task AScanThatExceedsThePageCeiling_ReportsTruncated_RatherThanSilentlyReconcilingAPrefix()
    {
        SeedGrant(expires: null);
        _dataverse.AlwaysMoreRecords = true;

        var result = await RunAsync(writes: true);

        Rule("R1")["Truncated"].Should().Be(true);
        result.ErrorMessage.Should().Contain($"stopped after {ExternalAccessReconciliationJob.MaxPages} pages");
        result.Success.Should().BeFalse();
        _dataverse.FetchXml.Count(x => x.Contains($"name=\"{GrantEntity}\"", StringComparison.Ordinal))
            .Should().Be(ExternalAccessReconciliationJob.MaxPages, "it pages to the ceiling and stops");
    }

    [Fact]
    public async Task Cancellation_ReturnsPromptly_PerformsNoFurtherWrites_AndDoesNotThrow()
    {
        for (var i = 0; i < 150; i++)
        {
            SeedGrant(expires: null);
        }

        using var cts = new CancellationTokenSource();
        _dataverse.OnWrite = () => cts.Cancel();

        // "Does not throw" is asserted by this line itself: if ExecuteAsync let the cancellation escape, the
        // test fails here rather than at an assertion.
        var result = await RunAsync(writes: true, token: cts.Token);

        result.Success.Should().BeFalse();
        _dataverse.Writes.Should().ContainSingle("the run stopped at the next chunk boundary");
        Heartbeat()["Status"].Should().Be(ExternalAccessReconciliationJob.StatusCancelled);
    }

    [Fact]
    public async Task ResultJson_CarriesThePerRuleBreakdown_AndTheJobNeverSetsSkipped()
    {
        SeedGrant(expires: null);

        var result = await RunAsync(writes: true);

        result.Skipped.Should().BeFalse("Skipped belongs to the host (ADR-036 A1 rule 1), never to a job");

        var rules = Report().GetProperty("rules").EnumerateArray().ToList();
        rules.Should().HaveCount(4);
        rules.Select(r => r.GetProperty("rule").GetString()).Should().Equal(
            ExternalAccessReconciliationJob.RuleStampDefaultExpiry,
            ExternalAccessReconciliationJob.RuleDeactivateOrphanedOrgGrant,
            ExternalAccessReconciliationJob.RuleDeactivateEndedMembership,
            ExternalAccessReconciliationJob.RuleDeactivateRecordGone);

        foreach (var rule in rules)
        {
            foreach (var field in new[] { "scanned", "planned", "changed", "failed", "truncated" })
            {
                rule.TryGetProperty(field, out _).Should().BeTrue($"'{field}' is part of the per-rule breakdown");
            }
        }
    }

    // ── The scan filters (the bound on volume, pinned directly) ─────────────────────────────────────

    [Fact]
    public void TheGrantScan_TreatsANullStatecodeAsActive_AndJoinsTheOrganizationsStateWithAnOuterJoin()
    {
        var fetch = XDocument.Parse(ExternalAccessReconciliationJob.BuildGrantScanFetchXml(1, null));

        // A bare `statecode eq 0` would silently exclude every null-statecode row from every rule.
        var stateFilter = fetch.Descendants("filter")
            .Single(f => f.Elements("condition").Any(c => (string?)c.Attribute("attribute") == "statecode"));
        stateFilter.Attribute("type")!.Value.Should().Be("or");
        stateFilter.Elements("condition").Select(c => (string?)c.Attribute("operator"))
            .Should().BeEquivalentTo(new[] { "eq", "null" });

        // OUTER — an inner join would drop exactly the contact-keyed grants R1 exists for.
        var link = fetch.Descendants("link-entity").Single();
        link.Attribute("name")!.Value.Should().Be(ExternalAccessReconciliationJob.OrganizationEntityLogicalName);
        link.Attribute("link-type")!.Value.Should().Be("outer");
        link.Elements("attribute").Select(a => (string?)a.Attribute("name")).Should().Contain("statecode");

        fetch.Root!.Attribute("count")!.Value.Should().Be(ExternalAccessReconciliationJob.PageSize.ToString(CultureInfo.InvariantCulture));
    }

    [Fact]
    public void TheMembershipScan_SelectsOnlyMembershipsWhoseEndDateIsStrictlyBeforeToday()
    {
        var fetch = XDocument.Parse(ExternalAccessReconciliationJob.BuildMembershipScanFetchXml(Today, 1, null));

        var endDate = fetch.Descendants("condition")
            .Single(c => (string?)c.Attribute("attribute") == ExternalAccessReconciliationJob.EndDateAttribute);

        // `lt`, not `le`: access holds THROUGH the end date. sprk_enddate is Date Only in live metadata
        // (re-verified 2026-09-21), so it is compared as a bare yyyy-MM-dd.
        endDate.Attribute("operator")!.Value.Should().Be("lt");
        endDate.Attribute("value")!.Value.Should().Be("2026-09-21");
    }

    [Fact]
    public void TheScansPageWithTheCookieTheyAreGiven()
    {
        ExternalAccessReconciliationJob.BuildGrantScanFetchXml(1, null)
            .Should().NotContain("paging-cookie");
        ExternalAccessReconciliationJob.BuildGrantScanFetchXml(4, "<cookie/>")
            .Should().Contain("page=\"4\"").And.Contain("paging-cookie");
    }

    [Fact]
    public void TheChunkClaimKeyIsContentAddressed_SoItDoesNotDependOnEnumerationOrder()
    {
        var a = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var b = Guid.Parse("00000000-0000-0000-0000-000000000002");

        ExternalAccessReconciliationJob.ClaimKey("R1", new[] { a, b })
            .Should().Be(ExternalAccessReconciliationJob.ClaimKey("R1", new[] { b, a }));
        ExternalAccessReconciliationJob.ClaimKey("R1", new[] { a })
            .Should().NotBe(ExternalAccessReconciliationJob.ClaimKey("R2", new[] { a }));
    }

    // ── R1 on a CONTACT-issued undated row (task 140 · session 27 round 42 item 2) ──────────────────
    //
    // A contact-issued grant may never outlive its issuer's own access: R1 stamps the EARLIER of +90 and the issuing
    // contact's own grant on the record (at the row's level or above), ENDS the row when the issuer holds no such grant
    // there, and keeps the contact as issuer either way (no internal person acted). The issuer's own rows are read through
    // the REAL ExternalGrantLifecycle.ReadContactHeldGrantsAsync over the in-memory grant table task 140's tests use (it
    // interprets the production $filters); the scan and the SDK writes go through this class's row store, as for R1-R3.

    private static readonly Guid Issuer = Guid.Parse("c0420000-0000-0000-0000-000000000001");
    private static readonly Guid Grantee = Guid.Parse("c0420000-0000-0000-0000-000000000002");
    private static readonly Guid IssuersIssuer = Guid.Parse("c0420000-0000-0000-0000-000000000003");
    private static readonly Guid Project = Guid.Parse("14200000-0000-0000-0000-0000000000a1");
    private static readonly Guid OtherProject = Guid.Parse("14200000-0000-0000-0000-0000000000a2");
    private static readonly Guid IssuersFirm = Guid.Parse("f4200000-0000-0000-0000-0000000000f1");
    private static readonly DateOnly Default = ExternalGrantLifecycle.DefaultExpiry(Today);
    private const int Collaborate = (int)ExternalAccessLevel.Collaborate;
    private const int ViewOnly = (int)ExternalAccessLevel.ViewOnly;

    [Fact]
    public async Task R1_AContactIssuedUndatedRow_WhoseIssuerHoldsLongerThanTheDefault_IsStampedWithTheDefault_AndKeepsItsIssuer()
    {
        _grants.Seed(Issuer, Project, Collaborate, Today.AddDays(200), issuedByContact: null);
        var row = SeedContactIssued(issuer: Issuer, level: Collaborate);

        await RunAsync(writes: true);

        ExpiryOf(row).Should().Be(Default, "the earlier of +90 and the issuer's own date is +90");
        AssertWroteOnly(row, ExternalAccessReconciliationJob.ExpiresDateAttribute);
        AssertIssuerKept(row);
        ContactIssued().GetProperty("stampedDefault").GetInt32().Should().Be(1);
        ContactIssuedRow(row).GetProperty("outcome").GetString().Should().Be("StampedDefault");
        ContactIssuedRow(row).GetProperty("issuerContactId").GetString().Should().Be(Issuer.ToString());
    }

    [Fact]
    public async Task R1_AContactIssuedUndatedRow_IsCappedAtItsIssuersOwnExpiry()
    {
        _grants.Seed(Issuer, Project, Collaborate, Today.AddDays(30), issuedByContact: null);
        var row = SeedContactIssued(issuer: Issuer, level: Collaborate);

        await RunAsync(writes: true);

        ExpiryOf(row).Should().Be(Today.AddDays(30), "a contact-issued grant never outlives its issuer's own");
        AssertWroteOnly(row, ExternalAccessReconciliationJob.ExpiresDateAttribute);
        AssertIssuerKept(row);
        ContactIssued().GetProperty("cappedByIssuer").GetInt32().Should().Be(1);
        ContactIssuedRow(row).GetProperty("issuerHeldUntil").GetString().Should().Be(Today.AddDays(30).ToString("yyyy-MM-dd"));
        BeforeStateFor(row).Should().ContainSingle().Which.Should().Contain("grantedByContact=" + Issuer)
            .And.Contain("capped by issuing contact");
    }

    /// <summary>
    /// The issuing contact no longer holds an active grant there at the row's level — the row is ENDED, the issuer kept. The
    /// positive twins are the two tests above (the issuer still holds one).
    /// </summary>
    [Theory]
    [InlineData("revoked")]       // the issuer's own row was deactivated
    [InlineData("expired")]       // …or has lapsed
    [InlineData("lower-level")]   // …or is below the row's level
    [InlineData("none")]          // …or never existed
    [InlineData("other-record")]  // …or is on another record
    public async Task R1_AContactIssuedUndatedRow_WhoseIssuerHoldsNoActiveGrantThere_IsDeactivated_AndKeepsItsIssuer(string shape)
    {
        switch (shape)
        {
            case "revoked":
                _grants.Seed(Issuer, Project, Collaborate, Today.AddDays(200), issuedByContact: null).StateCode = 1;
                break;
            case "expired":
                _grants.Seed(Issuer, Project, Collaborate, Today.AddDays(-1), issuedByContact: null);
                break;
            case "lower-level":
                _grants.Seed(Issuer, Project, ViewOnly, Today.AddDays(200), issuedByContact: null);
                break;
            case "other-record":
                _grants.Seed(Issuer, OtherProject, Collaborate, Today.AddDays(200), issuedByContact: null);
                break;
        }

        var row = SeedContactIssued(issuer: Issuer, level: Collaborate);

        await RunAsync(writes: true);

        State(Grant(row)).Should().Be(1);
        Status(Grant(row)).Should().Be(2);
        Grant(row).Contains(ExternalAccessReconciliationJob.ExpiresDateAttribute).Should().BeFalse("an ended row is not also stamped");
        AssertWroteOnly(row, "statecode", "statuscode");
        AssertIssuerKept(row);
        ContactIssued().GetProperty("deactivated").GetInt32().Should().Be(1);
    }

    /// <summary>A fault never writes: the row is left exactly as it is, and the run says so (partial) for tomorrow's tick.</summary>
    [Theory]
    [InlineData("memberships")]
    [InlineData("grants")]
    public async Task R1_AContactIssuedUndatedRow_WhoseIssuersAccessCannotBeRead_IsLeftUnchanged_AndTheRunIsReportedPartial(string unreadable)
    {
        _grants.Seed(Issuer, Project, Collaborate, Today.AddDays(30), issuedByContact: null);
        if (unreadable == "memberships")
            _participations.UnreadableMembershipContacts[Issuer] = true;
        else
            _participations.ThrowOnRead = true; // the record's flags read — the first read of the issuer's grants
        var row = SeedContactIssued(issuer: Issuer, level: Collaborate);
        var plain = SeedGrant(expires: null); // an ordinary undated row is still stamped

        var result = await RunAsync(writes: true);

        _dataverse.Writes.SelectMany(w => w.Updates).Should().NotContain(u => u.Id == row, "never stamped on a guess, never ended on a fault");
        Grant(row).Contains(ExternalAccessReconciliationJob.ExpiresDateAttribute).Should().BeFalse();
        State(Grant(row)).Should().Be(0);
        Grant(plain).Contains(ExternalAccessReconciliationJob.ExpiresDateAttribute).Should().BeTrue();
        ContactIssued().GetProperty("unresolved").GetInt32().Should().Be(1);
        ContactIssuedRow(row).GetProperty("outcome").GetString().Should().Be("Unresolved");
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("contact-issued row(s) were left unchanged");
        Heartbeat()["Status"].Should().Be(ExternalAccessReconciliationJob.StatusPartial);
    }

    /// <summary>
    /// The issuer's OWN row is judged as this same run leaves it, not as the scan read it. Three shapes, one input each:
    /// <list type="bullet">
    /// <item><c>stamped-by-R1</c> — the issuer's own row is undated and internally issued, so plain R1 stamps it +90 this run:
    ///   the contact-issued row is judged at that date, not against "undated confers nothing".</item>
    /// <item><c>ended-by-R2-undated</c> — the issuer's own row is undated and R2 ends it this run (its organization is inactive):
    ///   a row this run DECIDES, so it is not "unknown" — it counts for nothing and the contact-issued row is ended.</item>
    /// <item><c>ended-by-R2-dated</c> — the issuer's own row carries a LATER date (+200) and R2 ends it this run. Its date alone
    ///   would cap the contact-issued row at +90; only knowing that R2 ends it decides the row is ended instead. This is the
    ///   case that pins the R2 check in <c>Effective()</c> (verifier v1c-v1 seed V6): an undated issuer row counts for nothing
    ///   whatever R2 does, so only a DATED one tells "R2 ends it" from "it confers until its date".</item>
    /// </list>
    /// </summary>
    [Theory]
    [InlineData("stamped-by-R1")]
    [InlineData("ended-by-R2-undated")]
    [InlineData("ended-by-R2-dated")]
    public async Task R1_AnIssuersOwnRow_IsJudgedAsThisRunLeavesIt(string shape)
    {
        DateOnly? issuersExpiry = shape == "ended-by-R2-dated" ? Today.AddDays(200) : null;
        var endedByR2 = shape != "stamped-by-R1";
        var issuersRow = endedByR2
            ? SeedGrant(expires: issuersExpiry, organization: InactiveOrg, organizationState: 1)
            : SeedGrant(expires: null);
        Mirror(issuersRow, Issuer, Collaborate, issuedByContact: null, firm: endedByR2 ? InactiveOrg : null, expires: issuersExpiry);
        // The issuer-access read finds the firm ACTIVE (it was read at another moment than the scan's join, which is what says
        // the firm is inactive), so the issuer's row reaches the decision — which must know that R2 ends that row in this run.
        _grants.OrganizationStates[InactiveOrg] = 0;
        var row = SeedContactIssued(issuer: Issuer, level: Collaborate);

        await RunAsync(writes: true);

        if (endedByR2)
        {
            State(Grant(issuersRow)).Should().Be(1, "R2 ends the issuer's own row in this run");
            State(Grant(row)).Should().Be(1, "the issuer's only grant ends in this run, so the row it issued ends too");
            Grant(row).Contains(ExternalAccessReconciliationJob.ExpiresDateAttribute).Should().BeFalse("an ended row is not also stamped");
            AssertWroteOnly(row, "statecode", "statuscode");
            ContactIssued().GetProperty("deactivated").GetInt32().Should().Be(1);
            ContactIssuedRow(row).GetProperty("outcome").GetString().Should().Be("Deactivated");
            ContactIssuedRow(row).GetProperty("issuerHeldUntil").ValueKind.Should().Be(JsonValueKind.Null);
        }
        else
        {
            ExpiryOf(issuersRow).Should().Be(Default, "plain R1 stamps the issuer's own undated row");
            ExpiryOf(row).Should().Be(Default, "…and the row it issued is judged at that date");
            ContactIssuedRow(row).GetProperty("issuerHeldUntil").GetString().Should().Be(Default.ToString("yyyy-MM-dd"));
        }

        AssertIssuerKept(row);
    }

    /// <summary>
    /// The issuer's ONLY grant is an undated row this run neither planned nor is deciding — it was created after the scan, or it
    /// lies beyond a truncated one (modelled the same way: the issuer-access read sees it, the scan never returned it). Its fate
    /// is unknown, so the row it vouches for is NOT decided on a guess: left unchanged, reported Unresolved, the run partial —
    /// tomorrow's tick decides it. Without the guard the unknown row reads as "confers nothing" and the dependent row is ENDED
    /// on a guess (verifier v1c-v1 seed V11). The positive twin is <c>stamped-by-R1</c> above: the same undated row, planned by
    /// this run, decides the dependent row at its date.
    /// </summary>
    [Theory]
    [InlineData("created-after-the-scan")]
    [InlineData("beyond-a-truncated-scan")]
    public async Task R1_AnIssuersUndatedRowThisRunDidNotPlan_LeavesTheRowItIssuedUnchanged_AndTheRunPartial(string shape)
    {
        _grants.Seed(Issuer, Project, Collaborate, Today, issuedByContact: null).ExpiresDate = null; // never scanned
        if (shape == "beyond-a-truncated-scan")
            _dataverse.AlwaysMoreRecords = true;
        var row = SeedContactIssued(issuer: Issuer, level: Collaborate);
        var plain = SeedGrant(expires: null); // an ordinary undated row is still stamped

        var result = await RunAsync(writes: true);

        _dataverse.Writes.SelectMany(w => w.Updates).Should().NotContain(u => u.Id == row, "never ended on a guess, never stamped on one");
        Grant(row).Contains(ExternalAccessReconciliationJob.ExpiresDateAttribute).Should().BeFalse();
        State(Grant(row)).Should().Be(0);
        ExpiryOf(plain).Should().Be(Default);
        ContactIssued().GetProperty("unresolved").GetInt32().Should().Be(1);
        ContactIssued().GetProperty("deactivated").GetInt32().Should().Be(0);
        ContactIssuedRow(row).GetProperty("outcome").GetString().Should().Be("Unresolved");
        _log.Entries.Should().Contain(e => e.Message.Contains("left UNCHANGED", StringComparison.Ordinal)
            && e.Message.Contains("whose own date could not be decided", StringComparison.Ordinal));
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("contact-issued row(s) were left unchanged");
        Heartbeat()["Status"].Should().Be(ExternalAccessReconciliationJob.StatusPartial);
    }

    /// <summary>
    /// A chain: the issuer's own row is itself contact-issued and undated (issued by someone holding a 30-day grant). It is
    /// decided first, and the row depending on it is judged at that decision — whatever order the scan returned them in.
    /// </summary>
    [Fact]
    public async Task R1_AChainOfContactIssuedUndatedRows_IsDecidedInDependencyOrder()
    {
        _grants.Seed(IssuersIssuer, Project, Collaborate, Today.AddDays(30), issuedByContact: null);
        var row = SeedContactIssued(issuer: Issuer, level: Collaborate);               // scanned FIRST
        var issuersRow = SeedContactIssued(issuer: IssuersIssuer, level: Collaborate, grantee: Issuer);
        Mirror(issuersRow, Issuer, Collaborate, issuedByContact: IssuersIssuer);

        await RunAsync(writes: true);

        ExpiryOf(issuersRow).Should().Be(Today.AddDays(30));
        ExpiryOf(row).Should().Be(Today.AddDays(30), "its issuer's own grant lasts until the date that grant was just given");
        AssertIssuerKept(row);
        AssertIssuerKept(issuersRow, IssuersIssuer);
        ContactIssued().GetProperty("cappedByIssuer").GetInt32().Should().Be(2);
    }

    /// <summary>Two contact-issued undated rows that only vouch for each other — no outside source holds them up: both end.</summary>
    [Fact]
    public async Task R1_ContactIssuedUndatedRowsThatOnlyVouchForEachOther_AreBothEnded()
    {
        var first = SeedContactIssued(issuer: IssuersIssuer, level: Collaborate, grantee: Issuer);
        var second = SeedContactIssued(issuer: Issuer, level: Collaborate, grantee: IssuersIssuer);
        Mirror(first, Issuer, Collaborate, issuedByContact: IssuersIssuer);
        Mirror(second, IssuersIssuer, Collaborate, issuedByContact: Issuer);

        await RunAsync(writes: true);

        State(Grant(first)).Should().Be(1);
        State(Grant(second)).Should().Be(1);
        ContactIssued().GetProperty("deactivated").GetInt32().Should().Be(2);
    }

    /// <summary>
    /// The issuer holds the record through its FIRM's organization-wide grant: on a Standard record that grant counts (its
    /// date caps the row); on a Secure record only direct grants count for contacts (FR-22), so the row ends.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task R1_AnIssuerHoldingTheRecordThroughItsFirmsGrant_CountsItOnlyWhereOrganizationGrantsConfer(bool secure)
    {
        _participations.ContactOrganizations[Issuer] = new[] { IssuersFirm };
        _grants.SeedOrganization(IssuersFirm, Project, Collaborate, Today.AddDays(40));
        if (secure)
            _participations.Flags[Project] = new RootRecordFlags(IsSecure: true, IsRestricted: false);
        var row = SeedContactIssued(issuer: Issuer, level: Collaborate);

        await RunAsync(writes: true);

        if (secure)
            State(Grant(row)).Should().Be(1);
        else
            ExpiryOf(row).Should().Be(Today.AddDays(40));
        AssertIssuerKept(row);
    }

    // ── R1 on a row whose issuing contact was DELETED (session 27 round 50 item 2) ─────────────────────────────────────
    //
    // The BFF writes the issuer's id as text (sprk_grantedbycontactid) beside the lookup, in the same write, every time; the
    // lookup's Delete cascade is RemoveLink, so deleting the contact empties the lookup alone. "Recorded but the lookup is
    // empty" therefore means the issuer was deleted — and an undated row in that shape ENDS (it may not outlive its issuer).

    /// <summary>
    /// One input apart: the issuing contact exists (lookup set) — R1 judges the row by that contact's own grant (+200, so the
    /// default) — or it was DELETED (lookup empty, provenance kept) — the row ends, decided with no read, even though the
    /// seeded issuer grant is still there, and is reported <c>IssuerDeleted</c>. A recorded value that is not even a GUID ends
    /// the row too: provenance without its lookup is never read as "no contact issuer" (fail closed).
    /// </summary>
    [Theory]
    [InlineData("issuer-exists")]
    [InlineData("issuer-deleted")]
    [InlineData("issuer-deleted-unparseable")]
    public async Task R1_AContactIssuedUndatedRow_WhoseIssuingContactWasDeleted_IsDeactivated_AndReported(string shape)
    {
        _grants.Seed(Issuer, Project, Collaborate, Today.AddDays(200), issuedByContact: null);
        var row = SeedContactIssued(issuer: Issuer, level: Collaborate,
            issuerDeleted: shape != "issuer-exists",
            provenance: shape == "issuer-deleted-unparseable" ? "not-a-guid" : null);

        var result = await RunAsync(writes: true);

        if (shape == "issuer-exists")
        {
            ExpiryOf(row).Should().Be(Default);
            ContactIssuedRow(row).GetProperty("outcome").GetString().Should().Be("StampedDefault");
            ContactIssued().GetProperty("issuerDeleted").GetInt32().Should().Be(0);
            return;
        }

        State(Grant(row)).Should().Be(1);
        Status(Grant(row)).Should().Be(2);
        Grant(row).Contains(ExternalAccessReconciliationJob.ExpiresDateAttribute).Should().BeFalse("an ended row is not also stamped");
        AssertWroteOnly(row, "statecode", "statuscode");
        Grant(row).GetAttributeValue<string>(ExternalAccessReconciliationJob.GrantedByContactIdAttribute)
            .Should().NotBeNullOrEmpty("the provenance is kept — it is the record of who issued the ended grant");
        ContactIssued().GetProperty("issuerDeleted").GetInt32().Should().Be(1);
        ContactIssued().GetProperty("deactivated").GetInt32().Should().Be(0, "a deleted issuer is reported as its own outcome");
        ContactIssuedRow(row).GetProperty("outcome").GetString().Should().Be("IssuerDeleted");
        BeforeStateFor(row).Should().ContainSingle().Which.Should().Contain("grantedByContact=(empty)")
            .And.Contain("grantedByContactId=")
            .And.Contain("was deleted");
        Heartbeat()["R1ContactIssuedIssuerDeleted"].Should().Be(1);
        result.Success.Should().BeTrue("a deleted issuer is a decided outcome, not a fault");
    }

    /// <summary>
    /// A DATED row whose issuing contact was deleted stands until its date — exactly as a dated row whose issuer lost access by
    /// any other route does (owner G2 (ii): no cascade). R1 decides undated rows only; it never ends a dated one.
    /// </summary>
    [Fact]
    public async Task R1_ADatedRowWhoseIssuingContactWasDeleted_IsLeftUnchanged()
    {
        var row = SeedContactIssued(issuer: Issuer, level: Collaborate, issuerDeleted: true, expires: Today.AddDays(30));

        await RunAsync(writes: true);

        _dataverse.Writes.Should().BeEmpty();
        State(Grant(row)).Should().Be(0);
        ExpiryOf(row).Should().Be(Today.AddDays(30));
    }

    /// <summary>
    /// A row ISSUED BY the grantee of a deleted issuer's row: its issuer's only grant is that row, which this run ends — so it
    /// ends too, decided in the same run rather than left unresolved. (Were the deleted issuer's row not part of the run's
    /// decisions, it would read as an undated row nobody planned, and this row would be left unchanged on a guess.)
    /// </summary>
    [Fact]
    public async Task R1_ARowIssuedOnTheStrengthOfADeletedIssuersRow_EndsInTheSameRun()
    {
        var issuersRow = SeedContactIssued(issuer: IssuersIssuer, level: Collaborate, grantee: Issuer, issuerDeleted: true);
        Mirror(issuersRow, Issuer, Collaborate, issuedByContact: IssuersIssuer);
        _grants.RowsFor(Issuer).Single().GrantedByContactId = null; // the issuer's issuer was deleted (RemoveLink)
        var row = SeedContactIssued(issuer: Issuer, level: Collaborate);

        var result = await RunAsync(writes: true);

        State(Grant(issuersRow)).Should().Be(1);
        State(Grant(row)).Should().Be(1, "its issuer's only grant ends in this run");
        ContactIssuedRow(issuersRow).GetProperty("outcome").GetString().Should().Be("IssuerDeleted");
        ContactIssuedRow(row).GetProperty("outcome").GetString().Should().Be("Deactivated");
        ContactIssued().GetProperty("unresolved").GetInt32().Should().Be(0);
        result.Success.Should().BeTrue();
    }

    /// <summary>Report-only: the contact-issued rows are decided and REPORTED — the same evidence a write pass would act on — and nothing is written.</summary>
    [Fact]
    public async Task R1_ContactIssued_InReportOnlyMode_IsDecidedAndReported_ButNothingIsWritten()
    {
        _grants.Seed(Issuer, Project, Collaborate, Today.AddDays(30), issuedByContact: null);
        var capped = SeedContactIssued(issuer: Issuer, level: Collaborate);
        var ended = SeedContactIssued(issuer: IssuersIssuer, level: Collaborate, grantee: Issuer);
        var orphaned = SeedContactIssued(issuer: IssuersIssuer, level: Collaborate, grantee: Grantee, issuerDeleted: true);

        await RunAsync(writes: null);

        _dataverse.Writes.Should().BeEmpty();
        ContactIssuedRow(capped).GetProperty("outcome").GetString().Should().Be("CappedByIssuer");
        ContactIssuedRow(capped).GetProperty("expiresDate").GetString().Should().Be(Today.AddDays(30).ToString("yyyy-MM-dd"));
        ContactIssuedRow(ended).GetProperty("outcome").GetString().Should().Be("Deactivated");
        ContactIssuedRow(orphaned).GetProperty("outcome").GetString().Should().Be("IssuerDeleted");
        BeforeStateFor(capped).Should().ContainSingle().Which.Should().Contain("mode=report-only")
            .And.Contain($"after=[expiresDate={Today.AddDays(30):yyyy-MM-dd}");
        BeforeStateFor(ended).Should().ContainSingle().Which.Should().Contain("after=[statecode=1 statuscode=2");
        BeforeStateFor(orphaned).Should().ContainSingle().Which.Should().Contain("after=[statecode=1 statuscode=2")
            .And.Contain("was deleted");
    }

    /// <summary>
    /// A contact-issued row (undated unless <paramref name="expires"/>): scanned by the job (SDK store), with its issuer, the
    /// issuer's recorded provenance (as the BFF writes both, round 50 item 2), record, level and grantee.
    /// <paramref name="issuerDeleted"/> models the contact's deletion: its RemoveLink cascade empties the lookup and leaves
    /// the provenance; <paramref name="provenance"/> overrides the recorded text.
    /// </summary>
    private Guid SeedContactIssued(Guid issuer, int level, Guid? grantee = null, Guid? project = null,
        bool issuerDeleted = false, string? provenance = null, DateOnly? expires = null)
    {
        var row = new Entity(GrantEntity, Guid.NewGuid());
        row["statecode"] = new OptionSetValue(0);
        if (!issuerDeleted)
            row[ExternalAccessReconciliationJob.GrantedByContactAttribute] = new EntityReference("contact", issuer);
        row[ExternalAccessReconciliationJob.GrantedByContactIdAttribute] = provenance ?? ExternalGrantLifecycle.ContactIssuerProvenance(issuer);
        row["sprk_contact"] = new EntityReference("contact", grantee ?? Grantee);
        row["sprk_project"] = new EntityReference("sprk_project", project ?? Project);
        row[ExternalAccessReconciliationJob.AccessLevelAttribute] = new OptionSetValue(level);
        if (expires is { } e)
            row[ExternalAccessReconciliationJob.ExpiresDateAttribute] = ExternalGrantLifecycle.ToSdkDateOnly(e);
        _dataverse.Add(row);
        return row.Id;
    }

    /// <summary>The same row as the Web API reads it (the issuer-access read), with the SAME id and expiry (default: none) as the scanned one.</summary>
    private void Mirror(Guid id, Guid contact, int level, Guid? issuedByContact, Guid? firm = null, DateOnly? expires = null)
    {
        var mirrored = _grants.Seed(contact, Project, level, Today, issuedByContact, id: id);
        mirrored.ExpiresDate = expires;
        mirrored.OrganizationId = firm;
    }

    private DateOnly? ExpiryOf(Guid id)
        => Grant(id).GetAttributeValue<DateTime?>(ExternalAccessReconciliationJob.ExpiresDateAttribute) is { } value
            ? DateOnly.FromDateTime(value)
            : null;

    private void AssertWroteOnly(Guid id, params string[] columns)
        => _dataverse.Writes.SelectMany(w => w.Updates).Where(u => u.Id == id).Should().ContainSingle()
            .Which.Fields.Keys.Should().BeEquivalentTo(columns, "no issuer column is written — the contact stays the issuer");

    private void AssertIssuerKept(Guid id, Guid? issuer = null)
        => Grant(id).GetAttributeValue<EntityReference>(ExternalAccessReconciliationJob.GrantedByContactAttribute)
            .Id.Should().Be(issuer ?? Issuer, "no internal person acted, so nothing is taken over");

    private JsonElement ContactIssued() => JsonDocument.Parse(_lastResultJson!).RootElement
        .GetProperty("rules").EnumerateArray()
        .Single(r => r.GetProperty("rule").GetString() == ExternalAccessReconciliationJob.RuleStampDefaultExpiry)
        .GetProperty("contactIssued").Clone();

    private JsonElement ContactIssuedRow(Guid id) => ContactIssued().GetProperty("rows").EnumerateArray()
        .Single(r => r.GetProperty("rowId").GetString() == id.ToString());

    // ── Harness ─────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// A grant row on a live MATTER, unless <paramref name="recordGone"/> — its record was deleted and the RemoveLink cascade
    /// emptied every record lookup (owner round 71, R4). <paramref name="recordLookup"/> picks which lookup names the record.
    /// </summary>
    private Guid SeedGrant(DateOnly? expires, Guid? organization = null, int? organizationState = null, int? state = 0,
        bool recordGone = false, string recordLookup = "sprk_matter")
    {
        var row = new Entity(GrantEntity, Guid.NewGuid());
        if (state is { } s)
        {
            row["statecode"] = new OptionSetValue(s);
        }

        if (!recordGone)
        {
            row[recordLookup] = new EntityReference(recordLookup, Guid.NewGuid());
        }

        if (expires is { } e)
        {
            row[ExternalAccessReconciliationJob.ExpiresDateAttribute] = ExternalGrantLifecycle.ToSdkDateOnly(e);
        }

        if (organization is { } org)
        {
            row[ExternalAccessReconciliationJob.OrganizationLookupAttribute] =
                new EntityReference(ExternalAccessReconciliationJob.OrganizationEntityLogicalName, org);
            row["og.statecode"] = new AliasedValue(
                ExternalAccessReconciliationJob.OrganizationEntityLogicalName, "statecode",
                new OptionSetValue(organizationState ?? 0));
        }

        _dataverse.Add(row);
        return row.Id;
    }

    private Guid SeedMembership(DateOnly? end, int? state = 0)
    {
        var row = new Entity(JunctionEntity, Guid.NewGuid());
        if (state is { } s)
        {
            row["statecode"] = new OptionSetValue(s);
        }

        if (end is { } e)
        {
            row[ExternalAccessReconciliationJob.EndDateAttribute] = ExternalGrantLifecycle.ToSdkDateOnly(e);
        }

        _dataverse.Add(row);
        return row.Id;
    }

    private async Task<JobRunResult> RunAsync(
        bool? writes, string? rawWritesValue = null, int attempt = 1, CancellationToken token = default)
    {
        var settings = new Dictionary<string, string?>();
        if (writes is { } w)
        {
            settings[ExternalAccessReconciliationJob.WritesEnabledConfigKey] = w ? "true" : "false";
        }
        else if (rawWritesValue is not null)
        {
            settings[ExternalAccessReconciliationJob.WritesEnabledConfigKey] = rawWritesValue;
        }

        var services = new ServiceCollection();
        services.AddSingleton<IGenericEntityService>(_dataverse);
        services.AddSingleton<IIdempotencyService>(_idempotency);
        // The issuer-access reads of R1's contact-issued rule (round 42 item 2) — resolved only when such a row exists.
        services.AddSingleton<DataverseWebApiClient>(_grants);
        services.AddSingleton<ExternalParticipationService>(_participations);
        using var provider = services.BuildServiceProvider();

        var job = new ExternalAccessReconciliationJob(
            provider.GetRequiredService<IServiceScopeFactory>(),
            _time,
            new ConfigurationBuilder().AddInMemoryCollection(settings).Build(),
            _log);

        var result = await job.ExecuteAsync(
            new JobRunContext(Guid.NewGuid(), "corr-117", JobRunTrigger.Scheduled, new Dictionary<string, object>(), attempt),
            token);

        _lastResultJson = result.ResultJson;
        return result;
    }

    private Entity Grant(Guid id) => _dataverse.Rows[GrantEntity].Single(r => r.Id == id);
    private Entity Membership(Guid id) => _dataverse.Rows[JunctionEntity].Single(r => r.Id == id);

    private static int? State(Entity row) => row.GetAttributeValue<OptionSetValue>("statecode")?.Value;
    private static int? Status(Entity row) => row.GetAttributeValue<OptionSetValue>("statuscode")?.Value;

    private IReadOnlyList<string> BeforeStateFor(Guid id) => _log.Entries
        .Where(e => e.Message.Contains("before-state", StringComparison.Ordinal)
                    && e.State.TryGetValue("RowId", out var rowId) && Equals(rowId, id))
        .Select(e => e.Message)
        .ToList();

    private IReadOnlyDictionary<string, object?> Heartbeat() => _log.Entries
        .Last(e => e.Message.Contains("heartbeat", StringComparison.Ordinal)).State;

    private JsonElement Report() => JsonDocument.Parse(_lastResultJson!).RootElement.Clone();

    private IReadOnlyDictionary<string, object?> Rule(string prefix)
    {
        var name = prefix switch
        {
            "R1" => ExternalAccessReconciliationJob.RuleStampDefaultExpiry,
            "R2" => ExternalAccessReconciliationJob.RuleDeactivateOrphanedOrgGrant,
            "R4" => ExternalAccessReconciliationJob.RuleDeactivateRecordGone,
            _ => ExternalAccessReconciliationJob.RuleDeactivateEndedMembership,
        };

        var rule = JsonDocument.Parse(_lastResultJson!).RootElement
            .GetProperty("rules").EnumerateArray()
            .Single(r => r.GetProperty("rule").GetString() == name);

        return new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["Scanned"] = rule.GetProperty("scanned").GetInt32(),
            ["Planned"] = rule.GetProperty("planned").GetInt32(),
            ["Changed"] = rule.GetProperty("changed").GetInt32(),
            ["Failed"] = rule.GetProperty("failed").GetInt32(),
            ["ClaimHeld"] = rule.GetProperty("claimHeld").GetInt32(),
            ["AlreadyApplied"] = rule.GetProperty("alreadyApplied").GetInt32(),
            ["Truncated"] = rule.GetProperty("truncated").GetBoolean(),
            ["ScanFailed"] = rule.GetProperty("scanFailed").GetBoolean(),
        };
    }

    private string? _lastResultJson;

    /// <summary>
    /// A small ROW STORE, not a FetchXML evaluator: it serves every row of the scanned table, pages it, and
    /// applies <c>BulkUpdateAsync</c> back into itself — so each rule is proven by the job's in-code decision
    /// and a second run sees what the first one wrote. It does honour the scan's <c>&lt;attribute&gt;</c> list: a row is
    /// served with only the columns the FetchXML selects, as Dataverse serves it.
    /// </summary>
    private sealed class FakeDataverse : IGenericEntityService
    {
        public readonly Dictionary<string, List<Entity>> Rows = new(StringComparer.Ordinal);
        public readonly List<string> FetchXml = new();
        public readonly List<(string Entity, List<(Guid Id, Dictionary<string, object> Fields)> Updates)> Writes = new();
        public readonly Dictionary<string, Exception> ScanFailures = new(StringComparer.Ordinal);
        public readonly Queue<Exception?> WriteFailures = new();
        public bool AlwaysMoreRecords;
        public Action? OnWrite;

        public void Add(Entity row)
        {
            if (!Rows.TryGetValue(row.LogicalName, out var list))
            {
                Rows[row.LogicalName] = list = new List<Entity>();
            }

            list.Add(row);
        }

        public Task<EntityCollection> RetrieveMultipleAsync(FetchExpression fetch, CancellationToken ct = default)
        {
            FetchXml.Add(fetch.Query);
            var doc = XDocument.Parse(fetch.Query);
            var entityName = doc.Descendants("entity").First().Attribute("name")!.Value;

            if (ScanFailures.TryGetValue(entityName, out var failure))
            {
                throw failure;
            }

            var page = int.Parse(doc.Root!.Attribute("page")!.Value, CultureInfo.InvariantCulture);
            var count = int.Parse(doc.Root!.Attribute("count")!.Value, CultureInfo.InvariantCulture);
            var all = Rows.TryGetValue(entityName, out var list) ? list : new List<Entity>();

            // Each row is served PROJECTED to the attributes the FetchXML names (a link-entity's as "alias.name"), as
            // Dataverse serves it — so a column the scan forgets to select is absent here too, and the rule that needs it
            // goes red (round 50 item 2: the deleted-issuer rule reads sprk_grantedbycontactid). Writes still land on the
            // stored rows.
            var entity = doc.Descendants("entity").First();
            var selected = entity.Elements("attribute").Select(a => a.Attribute("name")!.Value).ToHashSet(StringComparer.Ordinal);
            foreach (var link in entity.Elements("link-entity"))
            {
                var alias = (string?)link.Attribute("alias");
                foreach (var attribute in link.Elements("attribute"))
                    selected.Add($"{alias}.{attribute.Attribute("name")!.Value}");
            }

            var slice = all.Skip((page - 1) * count).Take(count).Select(row =>
            {
                var served = new Entity(row.LogicalName, row.Id);
                foreach (var attribute in row.Attributes.Where(a => selected.Contains(a.Key)))
                    served[attribute.Key] = attribute.Value;
                return served;
            }).ToList();

            var collection = new EntityCollection(slice)
            {
                MoreRecords = AlwaysMoreRecords || all.Count > page * count,
                PagingCookie = "<cookie/>",
            };

            return Task.FromResult(collection);
        }

        public Task BulkUpdateAsync(
            string entityLogicalName, List<(Guid id, Dictionary<string, object> fields)> updates, CancellationToken ct = default)
        {
            OnWrite?.Invoke();

            // Every ATTEMPTED transaction is recorded, faulted or not — so a test can tell "no write was
            // attempted" from "a write was attempted and rolled back".
            Writes.Add((entityLogicalName, updates.Select(u => (u.id, u.fields)).ToList()));

            if (WriteFailures.Count > 0 && WriteFailures.Dequeue() is { } failure)
            {
                // All-or-nothing (task 096): NOTHING is applied.
                throw failure;
            }

            foreach (var (id, fields) in updates)
            {
                var row = Rows[entityLogicalName].Single(r => r.Id == id);
                foreach (var field in fields)
                {
                    row[field.Key] = field.Value;
                }
            }

            return Task.CompletedTask;
        }

        public Task<Entity> RetrieveAsync(string e, Guid id, string[] c, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Guid> CreateAsync(Entity e, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<(Guid Id, bool Created)> UpsertAsync(Entity e, CancellationToken ct = default) => throw new NotSupportedException();
        public Task UpdateAsync(string e, Guid id, Dictionary<string, object> f, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Entity> RetrieveByAlternateKeyAsync(string e, KeyAttributeCollection k, string[]? c = null, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<string> GetEntitySetNameAsync(string e, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<LookupNavigationMetadata> GetLookupNavigationAsync(string c, string r, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<string> GetCollectionNavigationAsync(string p, string r, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<EntityCollection> RetrieveMultipleAsync(QueryExpression q, CancellationToken ct = default) => throw new NotSupportedException();
        public Task DeleteAsync(string e, Guid id, CancellationToken ct = default) => throw new NotSupportedException();
        public Task AssociateAsync(string e, Guid id, string r, IEnumerable<EntityReference> t, CancellationToken ct = default) => throw new NotSupportedException();
    }

    /// <summary>The real idempotency service, with one switch: refuse to hand out a claim.</summary>
    private sealed class ControllableIdempotency(IIdempotencyService inner) : IIdempotencyService
    {
        public bool RefuseLocks;

        public Task<bool> IsEventProcessedAsync(string id, CancellationToken ct = default)
            => inner.IsEventProcessedAsync(id, ct);

        public Task MarkEventAsProcessedAsync(string id, TimeSpan? expiration = null, CancellationToken ct = default)
            => inner.MarkEventAsProcessedAsync(id, expiration, ct);

        public Task<bool> TryAcquireProcessingLockAsync(string id, TimeSpan? duration = null, CancellationToken ct = default)
            => RefuseLocks ? Task.FromResult(false) : inner.TryAcquireProcessingLockAsync(id, duration, ct);

        public Task ReleaseProcessingLockAsync(string id, CancellationToken ct = default)
            => inner.ReleaseProcessingLockAsync(id, ct);
    }

    private sealed record LogEntry(string Message, IReadOnlyDictionary<string, object?> State);

    private sealed class CapturingLogger : ILogger<ExternalAccessReconciliationJob>
    {
        public readonly List<LogEntry> Entries = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            var values = state as IReadOnlyList<KeyValuePair<string, object?>>;
            Entries.Add(new LogEntry(
                formatter(state, exception),
                values?.ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase)
                    ?? new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)));
        }
    }
}
