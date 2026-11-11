using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.Finance;
using Sprk.Bff.Api.Services.Finance;
using Sprk.Bff.Api.Services.Finance.Files;
using Sprk.Bff.Api.Services.Finance.Procedures;
using Sprk.Bff.Api.Services.Jobs;
using Sprk.Bff.Api.Services.Signals;
using Sprk.Bff.Api.Tests.Finance.Files;
using Sprk.Bff.Api.Tests.Services.Finance;
using Xunit;
using Harness = Sprk.Bff.Api.Tests.Services.Finance.RuleSetServiceTests.Harness;

namespace Sprk.Bff.Api.Tests.Api.Finance;

/// <summary>
/// ebilling-r1 task 116 fix round 4 (EB-D-216): a redraft deactivates the drafts it supersedes and never deletes them. The <c>/draft</c> handler
/// runs over the REAL rule set service (the 076 store double: real queries, row versions, all-or-nothing transactions) and the REAL file store
/// (its rights probe double), as fix round 3 did.
/// </summary>
/// <remarks>Maintain-class (ADR-038): the five conditions uac-r2 set on EB-D-216, and round 3's K1.</remarks>
public sealed class ProcedureRedraftDeactivationTests
{
    private static readonly Guid CallerOid = Guid.NewGuid();
    private const string Q1 = "Time must be billed in tenth-hour increments.";
    private const string Q2 = "No timekeeper may bill more than ten hours a day.";
    private const string Q3 = "Travel time is billed at half rate.";
    private const AccessRights ReadWrite = AccessRights.Read | AccessRights.Write;

    private static DraftedRule Increment(string quote = Q1) =>
        Drafted(RuleType.TimeIncrement, """{"type":"TimeIncrement","incrementHours":0.1}""", quote);

    private static DraftedRule Cap(int hours, string quote) =>
        Drafted(RuleType.DailyHoursCap, $$"""{"type":"DailyHoursCap","maxHoursPerDay":{{hours}}}""", quote);

    private static DraftedRule Drafted(RuleType type, string body, string quote)
    {
        var (headline, template) = BillingRuleDraftTranslator.Wording(type);
        return new DraftedRule(type.ToString(), body, template, headline, "Flag", quote, "Section 4");
    }

    /// <summary>A version drafted from its first file (three rules), one of them reviewed (Accepted), then its file replaced.</summary>
    private sealed class World
    {
        public readonly Harness H = new();
        public readonly StoredFileRig Rig = new();
        public readonly Mock<JobSubmissionService> Jobs = ProcedureEndpointsTests.Host.NewJobs();
        public Harness.Drafting D = null!;
        public Guid NewFile;
        public readonly List<JobContract> Queued = new();

        public static async Task<World> DraftedAndReplacedAsync(bool replace = true, bool grantRows = true)
        {
            var w = new World();
            w.D = w.H.DraftVersion();
            var added = await w.H.Service().AddDraftsAsync(
                w.D.Version, w.D.File, new[] { Increment(), Cap(10, Q2), Cap(8, Q3) }, Array.Empty<NotConvertedClause>(), CallerOid, CancellationToken.None);
            added.Kind.Should().Be(RuleSetOutcomeKind.Done);

            // A reviewer accepts one draft (an in-place write: modifiedon moves off createdon).
            w.H.ConcurrentWrite("sprk_policyversion", w.VersionOf(Q2), new Dictionary<string, object> { ["sprk_proposalstatus"] = new OptionSetValue((int)ProposalState.Accepted) });

            if (replace)
            {
                w.NewFile = w.H.SourceFile(w.D.Version, StoredFileStatus.Servable, "guidelines-v2.pdf");
                w.H.Row(StoredFileSchema.Entity, w.D.File)[StoredFileSchema.Status] = new OptionSetValue((int)StoredFileStatus.Removed);
            }

            w.Rig.Probe.Grant(StoredFileSchema.ProcedureVersionEntitySet, w.D.Version, ReadWrite);
            if (grantRows)
            {
                foreach (var (table, id) in w.DraftRows())
                {
                    w.Rig.Probe.Grant(table == "sprk_policy" ? ProcedureEndpoints.PolicyEntitySet : ProcedureEndpoints.PolicyVersionEntitySet, id, ReadWrite);
                }
            }

            w.Jobs.Setup(j => j.SubmitJobAsync(It.IsAny<JobContract>(), It.IsAny<CancellationToken>()))
                .Callback((JobContract job, CancellationToken _) => w.Queued.Add(job))
                .Returns(Task.CompletedTask);
            return w;
        }

        public Guid VersionOf(string quote) =>
            H.Rows["sprk_policyversion"].Single(v => v.GetAttributeValue<string>("sprk_clausereference") == quote).Id;

        public Guid PolicyOf(string quote) =>
            H.Row("sprk_policyversion", VersionOf(quote)).GetAttributeValue<EntityReference>("sprk_policy").Id;

        /// <summary>The drafted rows of the first file, versions first, in the order the plan names them.</summary>
        public List<(string Table, Guid Id)> DraftRows() =>
            new[] { Q1, Q2, Q3 }.Select(q => ("sprk_policyversion", VersionOf(q)))
                .Concat(new[] { Q1, Q2, Q3 }.Select(q => ("sprk_policy", PolicyOf(q)))).ToList();

        public int? State(string table, Guid id) => H.Row(table, id).GetAttributeValue<OptionSetValue>("statecode")?.Value;

        public int? Status(string table, Guid id) => H.Row(table, id).GetAttributeValue<OptionSetValue>("statuscode")?.Value;

        public async Task<IResult> DraftAsync(Guid? version = null)
        {
            var http = new DefaultHttpContext();
            http.Request.Headers.Authorization = "Bearer caller-token";
            return await ProcedureEndpoints.DraftVersionRules(
                D.Procedure, version ?? D.Version, H.Service(), Jobs.Object, Rig.Service, http, NullLogger<RuleSetService>.Instance, CancellationToken.None);
        }
    }

    private static int StatusOf(IResult result) => result is IStatusCodeHttpResult s ? s.StatusCode ?? 0 : 0;

    private static string? ErrorCodeOf(IResult result) =>
        result is ProblemHttpResult p && p.ProblemDetails.Extensions.TryGetValue("errorCode", out var code) ? code?.ToString() : null;

    // ── Condition 1: per-row Write, as the user, all or nothing; no Delete ──────────────────────────────────────

    [Fact(DisplayName = "116 fix 4 (EB-D-216 c1) must-fire: a redraft deactivates every superseded draft (reviewed and untouched), deletes nothing, after asking the caller's Write on each row")]
    public async Task Redraft_DeactivatesEverySupersededDraft_AfterThePerRowChecks()
    {
        var w = await World.DraftedAndReplacedAsync();
        var rows = w.DraftRows();

        var result = await w.DraftAsync();

        StatusOf(result).Should().Be(StatusCodes.Status202Accepted);
        foreach (var (table, id) in rows)
        {
            w.State(table, id).Should().Be(RuleSetService.StateInactive, $"{table} {id} is superseded");
            w.Status(table, id).Should().Be(RuleSetService.StatusInactive);
        }

        w.H.Writes.Should().NotContain(x => x.Kind == RuleSetServiceTests.Write.Delete, "a redraft never deletes");
        w.H.DeleteAttempts.Should().BeEmpty();
        var asked = w.Rig.Probe.Calls.Select(c => (c.Set, c.Id, c.Token)).ToList();
        foreach (var (table, id) in rows)
        {
            var set = table == "sprk_policy" ? ProcedureEndpoints.PolicyEntitySet : ProcedureEndpoints.PolicyVersionEntitySet;
            asked.Should().Contain((set, id, "caller-token"), "the caller's own Write on every row it deactivates, asked as the user");
        }

        w.Queued.Should().ContainSingle();
    }

    [Fact(DisplayName = "116 fix 4 (c1, peer check) must-fire: no Write on the SECOND of several rows: 403 and nothing at all written, no job")]
    public async Task Redraft_NoWriteOnTheSecondRow_Is403_AndWritesNothing()
    {
        var w = await World.DraftedAndReplacedAsync();
        var second = w.DraftRows()[1];
        w.Rig.Probe.Grant(ProcedureEndpoints.PolicyVersionEntitySet, second.Id, AccessRights.Read);
        var writes = w.H.Writes.Count;

        var result = await w.DraftAsync();

        StatusOf(result).Should().Be(StatusCodes.Status403Forbidden);
        w.H.Writes.Count.Should().Be(writes, "nothing at all is written");
        w.H.Transactions.Should().BeEmpty();
        w.DraftRows().Should().OnlyContain(r => w.State(r.Table, r.Id) == null, "every draft is still active");
        w.Queued.Should().BeEmpty();
    }

    [Fact(DisplayName = "116 fix 4 (c1) must-fire: a row Dataverse gives no answer for: 503 and nothing written, no job")]
    public async Task Redraft_AnUnansweredRow_Is503_AndWritesNothing()
    {
        var w = await World.DraftedAndReplacedAsync();
        w.Rig.Probe.Unknown.Add((ProcedureEndpoints.PolicyEntitySet, w.PolicyOf(Q3)));
        var writes = w.H.Writes.Count;

        var result = await w.DraftAsync();

        StatusOf(result).Should().Be(StatusCodes.Status503ServiceUnavailable);
        ErrorCodeOf(result).Should().Be(ProcedureEndpoints.CodeRedraftUnverified);
        w.H.Writes.Count.Should().Be(writes);
        w.Queued.Should().BeEmpty();
    }

    [Fact(DisplayName = "116 fix 4 (c1) must-not-fire: a first draft supersedes nothing and asks no per-row question (202)")]
    public async Task FirstDraft_AsksNoPerRowQuestion()
    {
        var w = new World();
        w.D = w.H.DraftVersion();
        w.Rig.Probe.Grant(StoredFileSchema.ProcedureVersionEntitySet, w.D.Version, ReadWrite);
        w.Jobs.Setup(j => j.SubmitJobAsync(It.IsAny<JobContract>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var result = await w.DraftAsync();

        StatusOf(result).Should().Be(StatusCodes.Status202Accepted);
        w.Rig.Probe.Calls.Select(c => c.Set).Should().Equal(new[] { StoredFileSchema.ProcedureVersionEntitySet }, "only the version's own rights");
        w.H.Transactions.Should().BeEmpty();
    }

    [Fact(DisplayName = "116 fix 4 (c1/c2) must-not-fire: a copy-forward draft and a published rule are neither asked about nor deactivated")]
    public async Task CopyForwardAndPublishedRules_AreUntouched()
    {
        var w = await World.DraftedAndReplacedAsync();
        // A copy-forward draft of this version (its policy has a current version) and a published rule of the set.
        var (copyPolicy, copyVersion) = w.H.Rule(w.D.RuleSet, code: "BILL-COPY", current: true);
        var draftOfIt = w.H.AddVersion(copyPolicy, 2, ProposalState.Proposed);
        w.H.Row("sprk_policyversion", draftOfIt)[RuleSetService.ProcedureVersionColumn] = new EntityReference(ProcedureEntity.VersionEntity, w.D.Version);
        w.H.Row("sprk_policyversion", draftOfIt)[RuleSetService.PublishStateColumn] = new OptionSetValue(RuleSetService.PublishStateDraft);
        var (publishedPolicy, publishedVersion) = w.H.Rule(w.D.RuleSet, code: "BILL-PUB", current: true);
        w.H.Row("sprk_policyversion", publishedVersion)[RuleSetService.PublishStateColumn] = new OptionSetValue(RuleSetService.PublishStatePublished);

        var result = await w.DraftAsync();

        StatusOf(result).Should().Be(StatusCodes.Status202Accepted);
        foreach (var id in new[] { copyPolicy, publishedPolicy })
        {
            w.State("sprk_policy", id).Should().BeNull();
        }

        foreach (var id in new[] { copyVersion, draftOfIt, publishedVersion })
        {
            w.State("sprk_policyversion", id).Should().BeNull();
        }

        w.Rig.Probe.Calls.Select(c => c.Id).Should().NotContain(new[] { copyPolicy, copyVersion, draftOfIt, publishedPolicy, publishedVersion });
    }

    // ── Condition 2: only drafts still in Draft; 409 on a publish in between ────────────────────────────────────

    [Fact(DisplayName = "116 fix 4 (c2) must-fire: a draft published between the plan and the write: 409, nothing deactivated (the other drafts neither)")]
    public async Task ADraftPublishedAfterThePlan_Is409_AndNothingIsDeactivated()
    {
        var w = await World.DraftedAndReplacedAsync();
        var service = w.H.Service();
        var plan = await service.PlanRedraftAsync(w.D.Version, w.NewFile, CancellationToken.None);
        plan.Value!.Drafts.Should().HaveCount(3);
        w.H.ConcurrentWrite("sprk_policyversion", w.VersionOf(Q2),
            new Dictionary<string, object> { [RuleSetService.PublishStateColumn] = new OptionSetValue(RuleSetService.PublishStatePublished) });

        var outcome = await service.DeactivateSupersededDraftsAsync(w.D.Version, plan.Value, CancellationToken.None);

        outcome.StatusCode.Should().Be(StatusCodes.Status409Conflict);
        outcome.ErrorCode.Should().Be(RuleSetService.CodeDraftChanged);
        w.DraftRows().Should().OnlyContain(r => w.State(r.Table, r.Id) == null, "no row is written until every row is re-checked");
        w.H.Transactions.Should().BeEmpty();
    }

    [Fact(DisplayName = "116 fix 4 (c2) must-fire: a publish lands after the re-read and before the write: the transaction's row-version check rolls everything back, 409")]
    public async Task APublishBetweenTheReReadAndTheWrite_RollsBack_409()
    {
        var w = await World.DraftedAndReplacedAsync();
        var service = w.H.Service();
        var plan = (await service.PlanRedraftAsync(w.D.Version, w.NewFile, CancellationToken.None)).Value!;
        var target = w.VersionOf(Q3);
        var fired = false;
        w.H.AfterQuery = q =>
        {
            // The deactivation's re-read of the versions (it selects statecode and has no statecode condition).
            if (!fired && q.EntityName == "sprk_policyversion" && q.ColumnSet.Columns.Contains("statecode")
                && !q.Criteria.Conditions.Any(c => c.AttributeName == "statecode"))
            {
                fired = true;
                w.H.ConcurrentWrite("sprk_policyversion", target,
                    new Dictionary<string, object> { [RuleSetService.PublishStateColumn] = new OptionSetValue(RuleSetService.PublishStatePublished) });
            }
        };

        var outcome = await service.DeactivateSupersededDraftsAsync(w.D.Version, plan, CancellationToken.None);

        fired.Should().BeTrue();
        outcome.StatusCode.Should().Be(StatusCodes.Status409Conflict);
        outcome.ErrorCode.Should().Be(RuleSetService.CodeDraftChanged);
        w.DraftRows().Should().OnlyContain(r => w.State(r.Table, r.Id) == null, "the transaction rolled back: no half-changed rule");
        w.State("sprk_policyversion", target).Should().BeNull("a published version is never deactivated");
    }

    [Fact(DisplayName = "116 fix 4 (c2) must-fire: through the route, a version published in between answers 409 and queues nothing")]
    public async Task Route_ADraftPublishedInBetween_Is409()
    {
        var w = await World.DraftedAndReplacedAsync();
        var target = w.VersionOf(Q1);
        w.H.AfterQuery = q =>
        {
            if (q.EntityName == "sprk_policy" && q.ColumnSet.Columns.Contains("statecode"))
            {
                w.H.ConcurrentWrite("sprk_policyversion", target,
                    new Dictionary<string, object> { [RuleSetService.PublishStateColumn] = new OptionSetValue(RuleSetService.PublishStatePublished) });
                w.H.AfterQuery = null;
            }
        };

        var result = await w.DraftAsync();

        StatusOf(result).Should().Be(StatusCodes.Status409Conflict);
        ErrorCodeOf(result).Should().Be(RuleSetService.CodeDraftChanged);
        w.State("sprk_policyversion", target).Should().BeNull();
        w.Queued.Should().BeEmpty();
    }

    // ── Condition 3: deactivated drafts are invisible to /draft, the readers and publish ─────────────────────────

    [Fact(DisplayName = "116 fix 4 (c3, peer check) must-fire: a deactivated draft is not returned by the rule set read, not offered by publish, refused 409 asked for by id, and not re-planned by /draft")]
    public async Task ADeactivatedDraft_IsInvisible_AndRefusedById()
    {
        var w = await World.DraftedAndReplacedAsync();
        var accepted = w.PolicyOf(Q2);
        var acceptedVersion = w.VersionOf(Q2);
        (StatusOf(await w.DraftAsync())).Should().Be(StatusCodes.Status202Accepted);
        var service = w.H.Service();

        var detail = await service.GetAsync(w.D.RuleSet, CancellationToken.None);
        detail.Value!.Proposals.Select(p => p.PolicyId).Should().NotContain(w.DraftRows().Select(r => r.Id));

        var publish = await service.PlanProcedureVersionPublishAsync(w.D.RuleSet, w.D.Version, new DateOnly(2026, 12, 1), CancellationToken.None);
        publish.Published.Select(p => p.VersionId).Should().NotContain(acceptedVersion, "an accepted draft that was deactivated is not published");

        var decided = await service.DecideAsync(w.D.RuleSet, accepted, true, w.H.RowVersions[acceptedVersion], CancellationToken.None);
        decided.StatusCode.Should().Be(StatusCodes.Status409Conflict);
        decided.ErrorCode.Should().Be(RuleSetService.CodeDraftInactive);

        var replan = await service.PlanRedraftAsync(w.D.Version, w.NewFile, CancellationToken.None);
        replan.Value!.Drafts.Should().BeEmpty("a deactivated draft is not a draft any more");
    }

    [Fact(DisplayName = "116 fix 4 (c3) must-fire: the run after a redraft stores a rule equal to a deactivated draft (not skipped as a repeat)")]
    public async Task TheRunAfterARedraft_StoresARuleEqualToADeactivatedOne()
    {
        var w = await World.DraftedAndReplacedAsync();
        (StatusOf(await w.DraftAsync())).Should().Be(StatusCodes.Status202Accepted);
        var service = w.H.Service();

        (await service.RemoveUntouchedDraftsAsync(w.D.Version, w.NewFile, CancellationToken.None)).Kind.Should().Be(RuleSetOutcomeKind.Done);
        var added = await service.AddDraftsAsync(w.D.Version, w.NewFile, new[] { Increment() }, Array.Empty<NotConvertedClause>(), CallerOid, CancellationToken.None);

        added.Value!.Stored.Should().Be(1);
        added.Value.AlreadyPresent.Should().Be(0);
    }

    [Fact(DisplayName = "116 fix 4 must-fire: the job never deactivates or deletes a reviewed superseded draft no request deactivated: 409, nothing removed")]
    public async Task Job_AReviewedSupersededDraftNoRequestDeactivated_Refuses()
    {
        var w = await World.DraftedAndReplacedAsync();
        var writes = w.H.Writes.Count;

        var outcome = await w.H.Service().RemoveUntouchedDraftsAsync(w.D.Version, w.NewFile, CancellationToken.None);

        outcome.StatusCode.Should().Be(StatusCodes.Status409Conflict);
        outcome.ErrorCode.Should().Be(RuleSetService.CodeSupersededDraftsRemain);
        w.H.Writes.Count.Should().Be(writes);
        w.H.DeleteAttempts.Should().BeEmpty();
    }

    // ── Round 3 K1: a probe fault answers every id alike ─────────────────────────────────────────────────────────

    [Fact(DisplayName = "116 fix 4 (K1) must-fire: when the version rights probe faults, an unknown id and an existing version give the same answer from the same reads")]
    public async Task ProbeFault_UnknownAndExistingVersion_AnswerAlike()
    {
        async Task<(int Status, string? Code, string[] Reads)> Run(bool exists)
        {
            var w = await World.DraftedAndReplacedAsync(replace: false, grantRows: false);
            w.Rig.Probe.Fault = new HttpRequestException("simulated Dataverse fault");
            w.H.Entities.Invocations.Clear();
            var result = await w.DraftAsync(exists ? w.D.Version : Guid.NewGuid());
            var reads = w.H.Entities.Invocations
                .Select(i => i.Method.Name + ":" + (i.Arguments.FirstOrDefault() is QueryExpression q ? q.EntityName : i.Arguments.FirstOrDefault())).ToArray();
            w.Queued.Should().BeEmpty();
            var problem = result is ProblemHttpResult p ? p.ProblemDetails : null;
            return (StatusOf(result), problem is null ? null : $"{problem.Detail}|{string.Join(",", problem.Extensions.Select(e => $"{e.Key}={e.Value}").Where(e => !e.StartsWith("trace", StringComparison.OrdinalIgnoreCase)))}", reads);
        }

        var unknown = await Run(exists: false);
        var existing = await Run(exists: true);

        unknown.Status.Should().Be(StatusCodes.Status403Forbidden);
        existing.Status.Should().Be(unknown.Status);
        existing.Code.Should().Be(unknown.Code);
        existing.Reads.Should().Equal(unknown.Reads, "the version is not read before the fault is answered");
        existing.Reads.Should().BeEmpty();
    }
}
