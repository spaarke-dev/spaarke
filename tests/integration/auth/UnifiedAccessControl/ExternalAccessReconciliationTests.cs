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

    // ── S2: the job ships DISABLED ──────────────────────────────────────────────────────────────────

    /// <remarks>
    /// Not an ADR-038 B3 wiring test. It asserts no <c>GetRequiredService(...) is not null</c> and no "the
    /// right type came back" — <c>Single(r =&gt; r.Job.JobId == …)</c> already fails if it did not. The one
    /// assertion is the SHIPPING STATE the owner's D-2 part 3 decision turns on: this job, which removes live
    /// access, must arrive switched off. Perturbing the registration to <c>enabled: true</c> reddens it.
    /// </remarks>
    [Fact]
    public void S2_TheJobShipsDisabled_SoEnablingItIsAnOwnerAction()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddExternalAccess();

        using var provider = services.BuildServiceProvider();
        var registration = provider.GetServices<ScheduledJobRegistration>()
            .Single(r => r.Job.JobId == ExternalAccessReconciliationJob.JobIdConstant);

        registration.Enabled.Should().BeFalse(
            "R2 and R3 remove access that exists today; enabling the job is an owner action (D-2 part 3)");
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
        rules.Should().HaveCount(3);
        rules.Select(r => r.GetProperty("rule").GetString()).Should().Equal(
            ExternalAccessReconciliationJob.RuleStampDefaultExpiry,
            ExternalAccessReconciliationJob.RuleDeactivateOrphanedOrgGrant,
            ExternalAccessReconciliationJob.RuleDeactivateEndedMembership);

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

    // ── Harness ─────────────────────────────────────────────────────────────────────────────────────

    private Guid SeedGrant(DateOnly? expires, Guid? organization = null, int? organizationState = null, int? state = 0)
    {
        var row = new Entity(GrantEntity, Guid.NewGuid());
        if (state is { } s)
        {
            row["statecode"] = new OptionSetValue(s);
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
    /// and a second run sees what the first one wrote.
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
            var slice = all.Skip((page - 1) * count).Take(count).ToList();

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
