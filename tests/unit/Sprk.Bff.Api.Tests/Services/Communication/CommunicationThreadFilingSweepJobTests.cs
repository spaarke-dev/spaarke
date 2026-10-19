// spaarkeai-word-add-in-r1 task 129 — the thread-filing backfill / safety-net sweep. The PRODUCTION job runs; the module
// boundaries are doubles: the Dataverse reads/writes (IGenericEntityService, an in-memory table store that honours the
// query's cursor, filters and bound) and the thread resolver (IThreadResolver — whose own rules ThreadResolverTests pin).

using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Spaarke.Dataverse;
using Spaarke.Scheduling;
using Sprk.Bff.Api.Infrastructure.DI;
using Sprk.Bff.Api.Services.Communication;
using Sprk.Bff.Api.Services.Communication.Engine;
using Xunit;
using DataverseEntity = Microsoft.Xrm.Sdk.Entity;

namespace Sprk.Bff.Api.Tests.Services.Communication;

public class CommunicationThreadFilingSweepJobTests
{
    private const int PrivacyOpen = 100000000;
    private const int PrivacyPrivate = 100000001;

    private readonly Mock<IGenericEntityService> _entity = new();
    private readonly Mock<IThreadResolver> _resolver = new();
    private readonly List<DataverseEntity> _emails = [];
    private readonly List<DataverseEntity> _threads = [];
    private readonly List<QueryExpression> _queries = [];
    private readonly Dictionary<string, string?> _settings = new();

    public CommunicationThreadFilingSweepJobTests()
    {
        // A tiny in-memory table store: the cursor (id >), the bound (TopCount), the id order, and the thread-id `In`.
        _entity
            .Setup(e => e.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((QueryExpression q, CancellationToken _) =>
            {
                _queries.Add(q);
                if (FailEmailRead && q.EntityName == "sprk_communication")
                    throw new InvalidOperationException("simulated email read fault");

                // The `In` read is the job's thread read for an email (before or after a reconcile): it also sees the threads the
                // doubled reconcile "created". The owner part's slice (no `In`) does not, so its tests stay about the owner part.
                var isThreadIdRead = q.Criteria.Conditions.Any(c => c.Operator == ConditionOperator.In);
                var source = q.EntityName == "sprk_communication" ? _emails
                    : isThreadIdRead ? _threads.Concat(_reconcileThreads).ToList()
                    : _threads;
                return new EntityCollection(
                    source.Where(r => Matches(r, q.Criteria)).OrderBy(r => r.Id).Take(q.TopCount ?? int.MaxValue).ToList());
            });

        // The post-reconcile re-read of an email.
        _entity
            .Setup(e => e.RetrieveAsync("sprk_communication", It.IsAny<Guid>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string _, Guid id, string[] _, CancellationToken _) => _emails.FirstOrDefault(e => e.Id == id)!);

        // By default the doubled reconcile WORKS: it gives the email a thread anchored to the record it is filed to. A test
        // that wants a reconcile that silently does nothing overrides this (NoOpReconcile).
        _resolver
            .Setup(r => r.ReconcileThreadForFilingAsync(It.IsAny<Guid>(), FilingReconcileHost.Standalone, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask)
            .Callback<Guid, FilingReconcileHost, CancellationToken>((id, _, _) => FixEmail(id));

        _resolver
            .Setup(r => r.ReconcileAnchoredThreadOwnerAsync(It.IsAny<Guid>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ThreadOwnerReconcileOutcome.AlreadyCorrect);
    }

    private readonly List<DataverseEntity> _reconcileThreads = [];

    private bool FailEmailRead { get; set; }

    /// <summary>What a working reconcile leaves behind: the email's thread is anchored to a record the email is filed to.</summary>
    private void FixEmail(Guid emailId)
    {
        var email = _emails.Single(e => e.Id == emailId);
        var (_, field) = RegardingFieldMap.All.First(x => email.GetAttributeValue<EntityReference>(x.RegardingField) is not null);
        var thread = new DataverseEntity("sprk_communicationthread") { Id = Id(900 + _reconcileThreads.Count) };
        thread["sprk_privacystate"] = new OptionSetValue(PrivacyOpen);
        thread["statecode"] = new OptionSetValue(0);
        thread[field] = email.GetAttributeValue<EntityReference>(field);
        _reconcileThreads.Add(thread);
        email["sprk_communicationthread"] = new EntityReference("sprk_communicationthread", thread.Id);
    }

    /// <summary>A reconcile that returns without changing anything — as the real one does when a step fails (NFR-02).</summary>
    private void NoOpReconcile() =>
        _resolver
            .Setup(r => r.ReconcileThreadForFilingAsync(It.IsAny<Guid>(), FilingReconcileHost.Standalone, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

    /// <summary>
    /// Evaluates the query's filter against a row — only what the job's queries use: Equal, GreaterThan (the id cursor), In
    /// (thread ids) and NotNull, under AND / OR. A column the row lacks reads as null (a missing <c>statecode</c> as Active).
    /// An operator it does not know FAILS the test rather than matching everything.
    /// </summary>
    private static bool Matches(DataverseEntity row, FilterExpression filter)
    {
        var results = filter.Conditions.Select(c => Matches(row, c)).Concat(filter.Filters.Select(f => Matches(row, f))).ToList();
        return filter.FilterOperator == LogicalOperator.Or ? results.Any(r => r) : results.All(r => r);
    }

    private static bool Matches(DataverseEntity row, ConditionExpression c)
    {
        object? Read(string attribute)
        {
            var key = attribute is "sprk_communicationid" or "sprk_communicationthreadid" ? (object)row.Id : row.Contains(attribute) ? row[attribute] : null;
            return key switch
            {
                null when attribute == "statecode" => 0,
                OptionSetValue o => o.Value,
                _ => key,
            };
        }

        var value = Read(c.AttributeName);
        return c.Operator switch
        {
            ConditionOperator.Equal => Equals(value, c.Values[0]),
            ConditionOperator.GreaterThan => value is Guid g && g.CompareTo((Guid)c.Values[0]) > 0,
            ConditionOperator.In => value is Guid id && c.Values.Cast<Guid>().Contains(id),
            ConditionOperator.NotNull => value is not null,
            _ => throw new NotSupportedException($"The test store does not evaluate {c.Operator} (on {c.AttributeName})."),
        };
    }

    // ── builders ──────────────────────────────────────────────────────────────────────────────────────────────────

    private static Guid Id(int n) => Guid.Parse($"12900000-0000-4000-9000-{n:D12}");

    private DataverseEntity AddThread(
        int n, string? anchorType = null, Guid? anchorId = null, int privacy = PrivacyOpen, bool inactive = false, bool teamOwned = false)
    {
        var thread = new DataverseEntity("sprk_communicationthread") { Id = Id(n) };
        thread["sprk_privacystate"] = new OptionSetValue(privacy);
        thread["statecode"] = new OptionSetValue(inactive ? 1 : 0);
        thread["ownerid"] = teamOwned ? new EntityReference("team", Guid.NewGuid()) : new EntityReference("systemuser", Guid.NewGuid());
        if (teamOwned)
            thread["owningteam"] = thread["ownerid"];
        if (anchorType is not null && RegardingFieldMap.FieldFor(anchorType) is { } field)
            thread[field] = new EntityReference(anchorType, anchorId ?? Guid.NewGuid());
        _threads.Add(thread);
        return thread;
    }

    /// <summary>An email id 1xx filed to <paramref name="filedType"/>:<paramref name="filedId"/> (unfiled when null), in thread <paramref name="threadN"/>.</summary>
    private DataverseEntity AddEmail(int n, int? threadN, string? filedType, Guid? filedId = null)
    {
        var email = new DataverseEntity("sprk_communication") { Id = Id(100 + n) };
        email["sprk_communicationtype"] = new OptionSetValue((int)Sprk.Bff.Api.Services.Communication.Models.CommunicationType.Email);
        if (threadN is { } t)
            email["sprk_communicationthread"] = new EntityReference("sprk_communicationthread", Id(t));
        if (filedType is not null && RegardingFieldMap.FieldFor(filedType) is { } field)
            email[field] = new EntityReference(filedType, filedId ?? Guid.NewGuid());
        _emails.Add(email);
        return email;
    }

    private async Task<JobRunResult> RunAsync(bool? apply = false, int? maxEmails = null, int? maxThreads = null, int pageSize = 100,
        CommunicationThreadFilingSweepJob? job = null)
    {
        job ??= NewJob(apply, maxEmails, maxThreads, pageSize);
        return await job.ExecuteAsync(
            new JobRunContext(Guid.NewGuid(), "corr-129", JobRunTrigger.ManualAdmin, new Dictionary<string, object>()),
            CancellationToken.None);
    }

    private CommunicationThreadFilingSweepJob NewJob(bool? apply = false, int? maxEmails = null, int? maxThreads = null, int pageSize = 100)
    {
        if (apply is { } a) _settings[CommunicationThreadFilingSweepJob.ApplyKey] = a ? "true" : "false";
        if (maxEmails is { } me) _settings[CommunicationThreadFilingSweepJob.MaxEmailsPerRunKey] = me.ToString();
        if (maxThreads is { } mt) _settings[CommunicationThreadFilingSweepJob.MaxThreadsPerRunKey] = mt.ToString();
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(_settings).Build();

        var services = new ServiceCollection();
        services.AddScoped(_ => _entity.Object);
        services.AddScoped(_ => _resolver.Object);
        var provider = services.BuildServiceProvider();

        return new CommunicationThreadFilingSweepJob(
            provider.GetRequiredService<IServiceScopeFactory>(), configuration, new FakeTimeProvider(),
            NullLogger<CommunicationThreadFilingSweepJob>.Instance)
        { ScanPageSize = pageSize };
    }

    private static JsonElement Result(JobRunResult result)
    {
        using var doc = JsonDocument.Parse(result.ResultJson!);
        return doc.RootElement.Clone();
    }

    /// <summary>
    /// One email per verdict: 1 ok, 2 no thread, 3 unanchored, 4 anchored elsewhere, 5 inactive, 6 private. Candidates are
    /// 2, 3 and 4 (5 is a user's delete, 6 is private). The owner part sees only the active, Open, anchored threads: 1 and 4
    /// (3 is unanchored, 5 inactive, 6 private).
    /// </summary>
    private void SeedOneOfEach()
    {
        var matter = Guid.NewGuid();
        AddThread(1, "sprk_matter", matter);                         // ok: anchored to the matter the email is filed to
        AddEmail(1, 1, "sprk_matter", matter);

        AddEmail(2, threadN: null, "sprk_matter");                   // no thread

        AddThread(3);                                                // unanchored
        AddEmail(3, 3, "sprk_matter");

        AddThread(4, "sprk_project", Guid.NewGuid());                // anchored to a record the email is not filed to
        AddEmail(4, 4, "sprk_matter");

        AddThread(5, "sprk_matter", Guid.NewGuid(), inactive: true); // inactive
        AddEmail(5, 5, "sprk_matter");

        AddThread(6, privacy: PrivacyPrivate);                       // private: left alone
        AddEmail(6, 6, "sprk_matter");
    }

    // ── Dry run ───────────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task DryRun_IsTheDefault_CountsEachCategory_AndCallsNothingThatWrites()
    {
        SeedOneOfEach();

        var result = await RunAsync(apply: null); // no setting at all

        result.Success.Should().BeTrue(result.ErrorMessage);
        var json = Result(result);
        json.GetProperty("mode").GetString().Should().Be("dry-run");
        var filing = json.GetProperty("filing");
        filing.GetProperty("examined").GetInt32().Should().Be(6);
        filing.GetProperty("ok").GetInt32().Should().Be(1);
        filing.GetProperty("noThread").GetInt32().Should().Be(1);
        filing.GetProperty("unanchored").GetInt32().Should().Be(1);
        filing.GetProperty("anchoredElsewhere").GetInt32().Should().Be(1);
        filing.GetProperty("skippedInactiveThread").GetInt32().Should().Be(1);
        filing.GetProperty("privateThread").GetInt32().Should().Be(1);
        filing.GetProperty("candidates").GetInt32().Should().Be(3, "an inactive thread is a user's delete, not a candidate");
        filing.GetProperty("reconciled").GetInt32().Should().Be(0);
        filing.GetProperty("unresolved").GetInt32().Should().Be(0, "a dry run attempts nothing");
        filing.GetProperty("sample").GetArrayLength().Should().Be(3);

        _resolver.Verify(r => r.ReconcileThreadForFilingAsync(It.IsAny<Guid>(), It.IsAny<FilingReconcileHost>(), It.IsAny<CancellationToken>()), Times.Never);
        _resolver.Verify(r => r.ReconcileThreadForFilingAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        _resolver.Verify(r => r.ReconcileAnchoredThreadOwnerAsync(It.IsAny<Guid>(), false, It.IsAny<CancellationToken>()), Times.Never,
            "a dry run asks the owner question only as a dry run");
        _entity.Verify(e => e.UpdateAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<Dictionary<string, object>>(), It.IsAny<CancellationToken>()), Times.Never);
        _entity.Verify(e => e.CreateAsync(It.IsAny<DataverseEntity>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData("false")]
    [InlineData("nonsense")]
    [InlineData("")]
    public async Task AnythingButTrue_IsADryRun(string setting)
    {
        SeedOneOfEach();
        _settings[CommunicationThreadFilingSweepJob.ApplyKey] = setting;

        var result = await RunAsync(apply: null);

        Result(result).GetProperty("apply").GetBoolean().Should().BeFalse();
        _resolver.Verify(r => r.ReconcileThreadForFilingAsync(It.IsAny<Guid>(), It.IsAny<FilingReconcileHost>(), It.IsAny<CancellationToken>()), Times.Never);
        _resolver.Verify(r => r.ReconcileThreadForFilingAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DryRun_ReportsTheOwnerCategories_FromTheResolversDryRunAnswers()
    {
        var matter = Guid.NewGuid();
        AddThread(1, "sprk_matter", matter);                                  // owner is a user -> ownerNotTeam, would assign
        AddThread(2, "sprk_matter", matter, teamOwned: true);                 // owner is a team, secure
        AddThread(3, "sprk_matter", matter, teamOwned: true);                 // owner is a team, already right
        _resolver.Setup(r => r.ReconcileAnchoredThreadOwnerAsync(Id(1), true, It.IsAny<CancellationToken>())).ReturnsAsync(ThreadOwnerReconcileOutcome.Assigned);
        _resolver.Setup(r => r.ReconcileAnchoredThreadOwnerAsync(Id(2), true, It.IsAny<CancellationToken>())).ReturnsAsync(ThreadOwnerReconcileOutcome.SkippedSecure);

        var result = await RunAsync(apply: false);

        var owner = Result(result).GetProperty("owner");
        owner.GetProperty("examined").GetInt32().Should().Be(3);
        owner.GetProperty("ownerNotTeam").GetInt32().Should().Be(1);
        owner.GetProperty("wouldAssign").GetInt32().Should().Be(1);
        owner.GetProperty("assigned").GetInt32().Should().Be(0, "a dry run assigns nothing");
        owner.GetProperty("skippedSecure").GetInt32().Should().Be(1);
        owner.GetProperty("alreadyCorrect").GetInt32().Should().Be(1);
        _resolver.Verify(r => r.ReconcileAnchoredThreadOwnerAsync(It.IsAny<Guid>(), true, It.IsAny<CancellationToken>()), Times.Exactly(3));
        _resolver.Verify(r => r.ReconcileAnchoredThreadOwnerAsync(It.IsAny<Guid>(), false, It.IsAny<CancellationToken>()), Times.Never);
    }

    // ── Apply ─────────────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Apply_ReconcilesEachCandidateOnce_AndEachAnchoredThreadOnce_AndSkipsTheOnesThatAreFine()
    {
        SeedOneOfEach();

        var result = await RunAsync(apply: true);

        result.Success.Should().BeTrue(result.ErrorMessage);
        foreach (var candidate in new[] { 2, 3, 4 })
            _resolver.Verify(r => r.ReconcileThreadForFilingAsync(Id(100 + candidate), FilingReconcileHost.Standalone, It.IsAny<CancellationToken>()), Times.Once);
        foreach (var fine in new[] { 1, 5, 6 })
            _resolver.Verify(r => r.ReconcileThreadForFilingAsync(Id(100 + fine), It.IsAny<FilingReconcileHost>(), It.IsAny<CancellationToken>()), Times.Never,
                "an email already on its record's panel, in a user-deleted (inactive) thread, or in a private thread, is not handed to the reconcile");
        _resolver.Verify(r => r.ReconcileThreadForFilingAsync(It.IsAny<Guid>(), FilingReconcileHost.Standalone, It.IsAny<CancellationToken>()), Times.Exactly(3));

        // The owner part: the Open, active, anchored threads (1, 4) — 5 is inactive, 3 is unanchored, 6 is private.
        _resolver.Verify(r => r.ReconcileAnchoredThreadOwnerAsync(Id(1), false, It.IsAny<CancellationToken>()), Times.Once);
        _resolver.Verify(r => r.ReconcileAnchoredThreadOwnerAsync(Id(4), false, It.IsAny<CancellationToken>()), Times.Once);
        _resolver.Verify(r => r.ReconcileAnchoredThreadOwnerAsync(It.IsAny<Guid>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
        _resolver.Verify(r => r.ReconcileAnchoredThreadOwnerAsync(It.IsAny<Guid>(), true, It.IsAny<CancellationToken>()), Times.Never);

        var json = Result(result);
        json.GetProperty("mode").GetString().Should().Be("apply");
        json.GetProperty("filing").GetProperty("reconciled").GetInt32().Should().Be(3);
        json.GetProperty("filing").GetProperty("unresolved").GetInt32().Should().Be(0);
    }

    [Fact]
    public async Task Apply_CountsAnAssignedOwner_AsAssigned_NotWouldAssign()
    {
        AddThread(1, "sprk_matter", Guid.NewGuid());
        _resolver.Setup(r => r.ReconcileAnchoredThreadOwnerAsync(Id(1), false, It.IsAny<CancellationToken>())).ReturnsAsync(ThreadOwnerReconcileOutcome.Assigned);

        var owner = Result(await RunAsync(apply: true)).GetProperty("owner");

        owner.GetProperty("assigned").GetInt32().Should().Be(1);
        owner.GetProperty("wouldAssign").GetInt32().Should().Be(0);
    }

    [Fact]
    public async Task Apply_AnAssignedOwner_IsListedWithTheOwnerItReplaced_SoItCanBeReversed()
    {
        var previousOwner = Guid.NewGuid();
        var team = Guid.NewGuid();
        AddThread(1, "sprk_matter", Guid.NewGuid());
        _resolver.Setup(r => r.ReconcileAnchoredThreadOwnerAsync(Id(1), false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ThreadOwnerReconcileResult(ThreadOwnerReconcileOutcome.Assigned, "systemuser", previousOwner, team));

        var owner = Result(await RunAsync(apply: true)).GetProperty("owner");

        var sample = owner.GetProperty("sample").EnumerateArray().Should().ContainSingle().Subject;
        sample.GetProperty("threadId").GetGuid().Should().Be(Id(1));
        sample.GetProperty("previousOwnerType").GetString().Should().Be("systemuser");
        sample.GetProperty("previousOwnerId").GetGuid().Should().Be(previousOwner);
        sample.GetProperty("newOwnerTeamId").GetGuid().Should().Be(team);
    }

    [Fact]
    public async Task AThreadWithAnAmbiguousAnchor_IsCountedSeparately_NotAsFailed()
    {
        AddThread(1, "sprk_matter", Guid.NewGuid());
        _resolver.Setup(r => r.ReconcileAnchoredThreadOwnerAsync(Id(1), false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ThreadOwnerReconcileOutcome.SkippedAmbiguousAnchor);

        var result = await RunAsync(apply: true);

        result.Success.Should().BeTrue(result.ErrorMessage);
        var owner = Result(result).GetProperty("owner");
        owner.GetProperty("skippedAmbiguousAnchor").GetInt32().Should().Be(1);
        owner.GetProperty("failed").GetInt32().Should().Be(0);
        owner.GetProperty("assigned").GetInt32().Should().Be(0);
    }

    [Fact]
    public async Task ARunWithNothingToDo_IsASuccess_WithAFullyZeroReport()
    {
        var matter = Guid.NewGuid();
        AddThread(1, "sprk_matter", matter);
        AddEmail(1, 1, "sprk_matter", matter);

        var result = await RunAsync(apply: true);

        result.Success.Should().BeTrue();
        Result(result).GetProperty("filing").GetProperty("candidates").GetInt32().Should().Be(0);
        _resolver.Verify(r => r.ReconcileThreadForFilingAsync(It.IsAny<Guid>(), It.IsAny<FilingReconcileHost>(), It.IsAny<CancellationToken>()), Times.Never);
        _resolver.Verify(r => r.ReconcileThreadForFilingAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Apply_NeverReconcilesAnEmailInADeactivatedThread_ItIsAUserDelete()
    {
        // The user thread delete only deactivates the thread; its emails stay active and filed. The backfill must not
        // undo that by moving them into a live thread.
        AddThread(1, "sprk_matter", Guid.NewGuid(), inactive: true);
        AddEmail(1, 1, "sprk_matter");

        var result = await RunAsync(apply: true);

        result.Success.Should().BeTrue(result.ErrorMessage);
        var filing = Result(result).GetProperty("filing");
        filing.GetProperty("skippedInactiveThread").GetInt32().Should().Be(1, "still reported");
        filing.GetProperty("candidates").GetInt32().Should().Be(0);
        _resolver.Verify(r => r.ReconcileThreadForFilingAsync(It.IsAny<Guid>(), It.IsAny<FilingReconcileHost>(), It.IsAny<CancellationToken>()), Times.Never);
        _resolver.Verify(r => r.ReconcileThreadForFilingAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Apply_WhenTheReconcileSilentlyDoesNothing_TheEmailsAreUnresolved_NotReconciled_AndTheRunIsPartial()
    {
        // ReconcileThreadForFilingAsync never throws (NFR-02): a failed step returns normally. Counting calls as outcomes
        // reported reconciled=3 and status=ok here.
        for (var n = 1; n <= 3; n++)
            AddEmail(n, threadN: null, "sprk_matter");
        NoOpReconcile();

        var result = await RunAsync(apply: true);

        result.Success.Should().BeFalse();
        var json = Result(result);
        json.GetProperty("status").GetString().Should().Be(CommunicationThreadFilingSweepJob.StatusPartial);
        var filing = json.GetProperty("filing");
        filing.GetProperty("candidates").GetInt32().Should().Be(3);
        filing.GetProperty("unresolved").GetInt32().Should().Be(3, "every candidate is still a candidate after the reconcile");
        filing.GetProperty("reconciled").GetInt32().Should().Be(0);
        filing.GetProperty("failed").GetInt32().Should().Be(0);
    }

    [Fact]
    public async Task Apply_CountsOnlyTheEmailsTheReconcileFixed_AsReconciled()
    {
        AddEmail(1, threadN: null, "sprk_matter");
        AddEmail(2, threadN: null, "sprk_matter");
        _resolver.Setup(r => r.ReconcileThreadForFilingAsync(Id(102), FilingReconcileHost.Standalone, It.IsAny<CancellationToken>())).Returns(Task.CompletedTask); // does nothing

        var result = await RunAsync(apply: true);

        var filing = Result(result).GetProperty("filing");
        filing.GetProperty("reconciled").GetInt32().Should().Be(1);
        filing.GetProperty("unresolved").GetInt32().Should().Be(1);
        Result(result).GetProperty("status").GetString().Should().Be(CommunicationThreadFilingSweepJob.StatusPartial);
    }

    [Fact]
    public async Task Apply_WhenTheResultCannotBeVerified_TheEmailIsUnresolved_NeverReconciled()
    {
        AddEmail(1, threadN: null, "sprk_matter");
        _entity.Setup(e => e.RetrieveAsync("sprk_communication", It.IsAny<Guid>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("simulated re-read fault"));

        var result = await RunAsync(apply: true);

        var filing = Result(result).GetProperty("filing");
        filing.GetProperty("reconciled").GetInt32().Should().Be(0);
        filing.GetProperty("unresolved").GetInt32().Should().Be(1);
        result.Success.Should().BeFalse();
    }

    // ── Paging and the per-run cap ────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task TheCap_IsHonoured_TheNextRunContinuesAfterTheLastRow_AndAPassThatEndsStartsOver()
    {
        for (var n = 1; n <= 5; n++)
            AddEmail(n, threadN: null, "sprk_matter"); // five filed emails with no thread: five candidates
        var job = NewJob(apply: true, maxEmails: 3, pageSize: 2);

        var first = Result(await RunAsync(job: job)).GetProperty("filing");
        first.GetProperty("examined").GetInt32().Should().Be(3, "the cap");
        first.GetProperty("passComplete").GetBoolean().Should().BeFalse();
        _resolver.Verify(r => r.ReconcileThreadForFilingAsync(It.IsAny<Guid>(), FilingReconcileHost.Standalone, It.IsAny<CancellationToken>()), Times.Exactly(3));

        var second = Result(await RunAsync(job: job)).GetProperty("filing");
        second.GetProperty("startAfter").GetGuid().Should().Be(Id(103), "it continues after the last row of the first run");
        second.GetProperty("examined").GetInt32().Should().Be(2);
        second.GetProperty("passComplete").GetBoolean().Should().BeTrue();
        foreach (var n in new[] { 101, 102, 103, 104, 105 })
            _resolver.Verify(r => r.ReconcileThreadForFilingAsync(Id(n), FilingReconcileHost.Standalone, It.IsAny<CancellationToken>()), Times.Once, "every email, once");

        var third = Result(await RunAsync(job: job)).GetProperty("filing");
        third.GetProperty("startAfter").ValueKind.Should().Be(JsonValueKind.Null, "a finished pass starts at the top");
        third.GetProperty("examined").GetInt32().Should().Be(3);
    }

    [Fact]
    public async Task TheThreadCap_IsHonoured_AndPagesAcrossRuns()
    {
        for (var n = 1; n <= 4; n++)
            AddThread(n, "sprk_matter", Guid.NewGuid());
        var job = NewJob(apply: true, maxThreads: 3, pageSize: 2);

        var first = Result(await RunAsync(job: job)).GetProperty("owner");
        first.GetProperty("examined").GetInt32().Should().Be(3);
        first.GetProperty("passComplete").GetBoolean().Should().BeFalse();

        var second = Result(await RunAsync(job: job)).GetProperty("owner");
        second.GetProperty("startAfter").GetGuid().Should().Be(Id(3));
        second.GetProperty("examined").GetInt32().Should().Be(1);
        second.GetProperty("passComplete").GetBoolean().Should().BeTrue();
    }

    // ── Failures ──────────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AFailureOnOneRow_DoesNotStopTheRun_AndTheRunIsNotASuccess()
    {
        for (var n = 1; n <= 3; n++)
            AddEmail(n, threadN: null, "sprk_matter");
        _resolver.Setup(r => r.ReconcileThreadForFilingAsync(Id(102), FilingReconcileHost.Standalone, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("simulated"));

        var result = await RunAsync(apply: true);

        _resolver.Verify(r => r.ReconcileThreadForFilingAsync(Id(101), FilingReconcileHost.Standalone, It.IsAny<CancellationToken>()), Times.Once);
        _resolver.Verify(r => r.ReconcileThreadForFilingAsync(Id(103), FilingReconcileHost.Standalone, It.IsAny<CancellationToken>()), Times.Once, "the run went on past the failure");
        result.Success.Should().BeFalse();
        var json = Result(result);
        json.GetProperty("status").GetString().Should().Be(CommunicationThreadFilingSweepJob.StatusPartial);
        json.GetProperty("filing").GetProperty("failed").GetInt32().Should().Be(1);
        json.GetProperty("filing").GetProperty("reconciled").GetInt32().Should().Be(2);
    }

    [Fact]
    public async Task AFailedOwnerAnswer_IsCounted_AndTheRunIsPartial()
    {
        AddThread(1, "sprk_matter", Guid.NewGuid());
        AddThread(2, "sprk_matter", Guid.NewGuid());
        _resolver.Setup(r => r.ReconcileAnchoredThreadOwnerAsync(Id(1), false, It.IsAny<CancellationToken>())).ReturnsAsync(ThreadOwnerReconcileOutcome.Failed);

        var result = await RunAsync(apply: true);

        result.Success.Should().BeFalse();
        Result(result).GetProperty("owner").GetProperty("failed").GetInt32().Should().Be(1);
        _resolver.Verify(r => r.ReconcileAnchoredThreadOwnerAsync(Id(2), false, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AnOwnerCallThatThrows_DoesNotStopTheRun()
    {
        AddThread(1, "sprk_matter", Guid.NewGuid());
        AddThread(2, "sprk_matter", Guid.NewGuid());
        _resolver.Setup(r => r.ReconcileAnchoredThreadOwnerAsync(Id(1), false, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("simulated"));

        var result = await RunAsync(apply: true);

        _resolver.Verify(r => r.ReconcileAnchoredThreadOwnerAsync(Id(2), false, It.IsAny<CancellationToken>()), Times.Once);
        result.Success.Should().BeFalse();
        Result(result).GetProperty("owner").GetProperty("failed").GetInt32().Should().Be(1);
    }

    [Fact]
    public async Task AnEmailReadThatFaults_IsAFailedRun_NeverNothingToDo_AndTheOwnerPartStillRuns()
    {
        FailEmailRead = true;
        AddThread(1, "sprk_matter", Guid.NewGuid());

        var result = await RunAsync(apply: true);

        result.Success.Should().BeFalse();
        var json = Result(result);
        json.GetProperty("status").GetString().Should().Be(CommunicationThreadFilingSweepJob.StatusError);
        json.GetProperty("filing").GetProperty("enumerationFailed").GetBoolean().Should().BeTrue();
        result.ErrorMessage.Should().Contain("could not be read");
        _resolver.Verify(r => r.ReconcileAnchoredThreadOwnerAsync(Id(1), false, It.IsAny<CancellationToken>()), Times.Once);
    }

    // ── Candidate selection ───────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Classify_UsesTheReconcilesOwnTests_AndSeesAnyOfTheEmailsFiledRecords()
    {
        var matter = Guid.NewGuid();
        var project = Guid.NewGuid();
        var threads = new Dictionary<Guid, DataverseEntity>();
        DataverseEntity T(int n, string? type = null, Guid? id = null, int privacy = PrivacyOpen, bool inactive = false)
        {
            var thread = AddThread(n, type, id, privacy, inactive);
            threads[thread.Id] = thread;
            return thread;
        }

        var bothRecords = AddEmail(1, 1, "sprk_matter", matter);
        bothRecords["sprk_regardingproject"] = new EntityReference("sprk_project", project);
        T(1, "sprk_project", project); // anchored to the SECONDARY record the email is filed to: still on that panel (F1-a)

        AddEmail(2, 2, "sprk_matter", matter);
        T(2, "sprk_matter", matter);

        AddEmail(3, 3, "sprk_matter", matter);
        T(3, "sprk_matter", Guid.NewGuid()); // same TYPE, different record

        AddEmail(4, threadN: 99, "sprk_matter", matter); // names a thread nobody can read

        string Verdict(int n) => CommunicationThreadFilingSweepJob.Classify(_emails.Single(e => e.Id == Id(100 + n)), threads).ToString();

        Verdict(1).Should().Be(nameof(CommunicationThreadFilingSweepJob.FilingVerdict.Ok));
        Verdict(2).Should().Be(nameof(CommunicationThreadFilingSweepJob.FilingVerdict.Ok));
        Verdict(3).Should().Be(nameof(CommunicationThreadFilingSweepJob.FilingVerdict.AnchoredElsewhere));
        Verdict(4).Should().Be(nameof(CommunicationThreadFilingSweepJob.FilingVerdict.ThreadUnreadable));
    }

    [Fact]
    public void TheEmailQuery_IsActiveFiledEmails_AfterTheCursor_InIdOrder_Bounded()
    {
        var after = Id(150);

        var query = CommunicationThreadFilingSweepJob.BuildEmailQuery(after, 77);

        query.EntityName.Should().Be("sprk_communication");
        query.TopCount.Should().Be(77);
        query.Criteria.Conditions.Should().Contain(c => c.AttributeName == "statecode" && c.Operator == ConditionOperator.Equal && Equals(c.Values[0], 0));
        query.Criteria.Conditions.Should().Contain(c => c.AttributeName == "sprk_communicationtype" && c.Operator == ConditionOperator.Equal && Equals(c.Values[0], 100000000));
        query.Criteria.Conditions.Should().Contain(c => c.AttributeName == "sprk_communicationid" && c.Operator == ConditionOperator.GreaterThan && Equals(c.Values[0], after));
        var filed = query.Criteria.Filters.Should().ContainSingle().Subject;
        filed.FilterOperator.Should().Be(LogicalOperator.Or);
        filed.Conditions.Select(c => c.AttributeName).Should().BeEquivalentTo(RegardingFieldMap.AllRegardingFields);
        filed.Conditions.Should().OnlyContain(c => c.Operator == ConditionOperator.NotNull);
        query.Orders.Should().ContainSingle(o => o.AttributeName == "sprk_communicationid" && o.OrderType == OrderType.Ascending);
    }

    [Fact]
    public void TheThreadQuery_IsActiveOpenAnchoredThreads_ByTheThreadsOwnTypedLookups()
    {
        var query = CommunicationThreadFilingSweepJob.BuildAnchoredThreadQuery(null, 50);

        query.EntityName.Should().Be("sprk_communicationthread");
        query.Criteria.Conditions.Should().Contain(c => c.AttributeName == "statecode" && Equals(c.Values[0], 0));
        query.Criteria.Conditions.Should().Contain(c => c.AttributeName == "sprk_privacystate" && Equals(c.Values[0], PrivacyOpen));
        query.Criteria.Conditions.Should().NotContain(c => c.Operator == ConditionOperator.GreaterThan, "no cursor on the first slice");
        var anchored = query.Criteria.Filters.Should().ContainSingle().Subject;
        anchored.Conditions.Select(c => c.AttributeName).Should().BeEquivalentTo(ThreadResolver.ThreadRegardingColumns);
        query.ColumnSet.Columns.Should().Contain(new[] { "ownerid", "owningteam" });
    }

    // ── Registration ──────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void TheJob_IsRegisteredThroughAddScheduledJob_Disabled_OnTheDailyCron()
    {
        var configuration = new ConfigurationBuilder().Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddCommunicationModule(configuration);
        using var provider = services.BuildServiceProvider();

        var registration = provider.GetServices<ScheduledJobRegistration>()
            .Single(r => r.Job.JobId == CommunicationThreadFilingSweepJob.JobIdConstant);

        registration.Enabled.Should().BeFalse("it runs only when an admin triggers or enables it");
        registration.CronSchedule.Should().Be("30 3 * * *");
        registration.Job.Should().BeOfType<CommunicationThreadFilingSweepJob>();
    }
}
