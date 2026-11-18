using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Spaarke.Core.Auth;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.Filters;
using Sprk.Bff.Api.Api.Finance;
using Sprk.Bff.Api.Services.Finance;
using Sprk.Bff.Api.Services.Finance.Files;
using Sprk.Bff.Api.Services.Finance.Procedures;
using Sprk.Bff.Api.Services.Jobs;
using Sprk.Bff.Api.Services.Jobs.Handlers;
using Sprk.Bff.Api.Services.Signals;
using Sprk.Bff.Api.Tests.Finance.Files;
using Sprk.Bff.Api.Tests.Services.Finance;
using Xunit;
using FinAuth = Sprk.Bff.Api.Tests.Api.Finance.FinanceEndpointsAuthorizationContractTests;
using Harness = Sprk.Bff.Api.Tests.Services.Finance.RuleSetServiceTests.Harness;

namespace Sprk.Bff.Api.Tests.Api.Finance;

/// <summary>
/// ebilling-r1 task 116 fix rounds 4 and 5 (EB-D-216, EB-D-216a): a redraft deactivates the drafts it supersedes and never deletes them, after
/// the caller's reject rights (DecideRule's privileges on the route's declaration, then Read on the version's rule set and Write on its
/// Procedure, as the caller). The <c>/draft</c> handler runs over the REAL rule set service (the 076 store double: real queries, row versions,
/// all-or-nothing transactions), the REAL file store (its rights probe double), the REAL caller-rights evaluator (over a recording data source)
/// and the REAL lock store (over an in-memory cache).
/// </summary>
/// <remarks>Maintain-class (ADR-038): the conditions uac-r2 set on EB-D-216 as revised by EB-D-216a, round 3's K1 and round 5's re-check items.</remarks>
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
        public readonly FinAuth.RecordingAccessDataSource Access = new();
        public readonly IdempotencyService Locks = ProcedureEndpointsTests.Host.NewLocks();
        public readonly Mock<JobSubmissionService> Jobs = ProcedureEndpointsTests.Host.NewJobs();
        public Harness.Drafting D = null!;
        public Guid NewFile;
        public readonly List<JobContract> Queued = new();
        public readonly List<CancellationToken> QueuedTokens = new();

        /// <summary>When set, the queue refuses the job (a Service Bus outage).</summary>
        public Exception? QueueFault;

        /// <param name="replace">Replace the first file with a second one.</param>
        /// <param name="beforeReplace">Runs after the first run's drafts are stored and reviewed, before the second file is uploaded.</param>
        public static async Task<World> DraftedAndReplacedAsync(bool replace = true, Action<World>? beforeReplace = null)
        {
            var w = new World();
            w.D = w.H.DraftVersion();
            var added = await w.H.Service().AddDraftsAsync(
                w.D.Version, w.D.File, new[] { Increment(), Cap(10, Q2), Cap(8, Q3) }, Array.Empty<NotConvertedClause>(), CallerOid, CancellationToken.None);
            added.Kind.Should().Be(RuleSetOutcomeKind.Done);

            // A reviewer accepts one draft (an in-place write: modifiedon moves off createdon).
            w.H.ConcurrentWrite("sprk_policyversion", w.VersionOf(Q2), new Dictionary<string, object> { ["sprk_proposalstatus"] = new OptionSetValue((int)ProposalState.Accepted) });
            beforeReplace?.Invoke(w);

            if (replace)
            {
                w.NewFile = w.H.SourceFile(w.D.Version, StoredFileStatus.Servable, "guidelines-v2.pdf");
                w.H.Row(StoredFileSchema.Entity, w.D.File)[StoredFileSchema.Status] = new OptionSetValue((int)StoredFileStatus.Removed);
            }

            w.GrantTheRejectRights();
            w.Jobs.Setup(j => j.SubmitJobAsync(It.IsAny<JobContract>(), It.IsAny<CancellationToken>()))
                .Returns((JobContract job, CancellationToken token) =>
                {
                    token.ThrowIfCancellationRequested(); // the real sender honours the caller's token
                    if (w.QueueFault is { } fault)
                    {
                        return Task.FromException(fault);
                    }

                    w.Queued.Add(job);
                    w.QueuedTokens.Add(token);
                    return Task.CompletedTask;
                });
            return w;
        }

        /// <summary>What a reject of the drafts needs, as the caller: Read and Write on the version, Read on the set, Write on the Procedure.</summary>
        public void GrantTheRejectRights()
        {
            Rig.Probe.Grant(StoredFileSchema.ProcedureVersionEntitySet, D.Version, ReadWrite);
            Access.Grant("sprk_rulesets", D.RuleSet, AccessRights.Read);
            Access.Grant(BillingAdminAuthorization.ProcedureEntitySet, D.Procedure, ReadWrite);
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

        /// <summary>Queries of draft state (policies, policy versions, the Procedure Version and its files) the service sent.</summary>
        public IEnumerable<QueryExpression> DraftStateReads() => H.Queries.Where(q =>
            q.EntityName is "sprk_policy" or "sprk_policyversion" or ProcedureEntity.VersionEntity or StoredFileSchema.Entity);

        public async Task<IResult> DraftAsync(Guid? version = null, CancellationToken ct = default)
        {
            var http = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim("oid", CallerOid.ToString()) }, "test")),
                TraceIdentifier = "trace-redraft", // one trace id, so two answers can be compared byte for byte
            };
            http.Request.Headers.Authorization = "Bearer caller-token";
            return await ProcedureEndpoints.DraftVersionRules(
                D.Procedure, version ?? D.Version, H.Service(), Jobs.Object, Rig.Service, ProcedureEndpointsTests.Host.NewAuthorization(Access), Locks,
                http, NullLogger<RuleSetService>.Instance, ct);
        }

        /// <summary><paramref name="count"/> untouched drafts of this version, seeded as a run's output (the application created them, nobody changed them).</summary>
        public void SeedUntouchedDrafts(int count)
        {
            for (var i = 0; i < count; i++)
            {
                var policy = new Entity("sprk_policy", Guid.NewGuid())
                {
                    ["sprk_name"] = $"Seeded {i}",
                    ["sprk_policycode"] = $"{RuleSetService.DraftCodePrefix}{D.RuleSet:N}-{100 + i:D3}",
                    ["sprk_ruleset"] = new EntityReference("sprk_ruleset", D.RuleSet),
                    ["sprk_enabled"] = false,
                    [RuleDomains.PolicyDomainColumn] = new OptionSetValue(RuleDomains.PolicyDomainBilling),
                };
                H.Seed(policy);
                H.SetCreatedBy(policy.Id, H.Identity);
                var version = new Entity("sprk_policyversion", Guid.NewGuid())
                {
                    ["sprk_policy"] = new EntityReference("sprk_policy", policy.Id),
                    ["sprk_versionnumber"] = 1,
                    ["sprk_proposalstatus"] = new OptionSetValue((int)ProposalState.Proposed),
                    [RuleSetService.ProcedureVersionColumn] = new EntityReference(ProcedureEntity.VersionEntity, D.Version),
                    [RuleSetService.PublishStateColumn] = new OptionSetValue(RuleSetService.PublishStateDraft),
                };
                H.Seed(version);
                H.SetCreatedBy(version.Id, H.Identity);
            }
        }
    }

    private static int StatusOf(IResult result) => result is IStatusCodeHttpResult s ? s.StatusCode ?? 0 : 0;

    private static string? ErrorCodeOf(IResult result) =>
        result is ProblemHttpResult p && p.ProblemDetails.Extensions.TryGetValue("errorCode", out var code) ? code?.ToString() : null;

    private static string? ReasonCodeOf(IResult result) =>
        result is ProblemHttpResult p && p.ProblemDetails.Extensions.TryGetValue("reasonCode", out var code) ? code?.ToString() : null;

    // ── EB-D-216a: the reject rights, as the caller; no per-row Write ────────────────────────────────────────────

    [Fact(DisplayName = "116 fix 5 (EB-D-216a) must-not-fire, realistic shape: the store REFUSES row-level Write on every draft and the redraft still succeeds through the reject rights")]
    public async Task Redraft_RowLevelWriteRefusedOnEveryDraft_StillSucceeds_ThroughTheRejectRights()
    {
        var w = await World.DraftedAndReplacedAsync();
        var rows = w.DraftRows();
        foreach (var (table, id) in rows)
        {
            // Row-level rights on draft rows are not the gate: the store answers Read at most, never Write, and the redraft still goes ahead.
            var set = table == "sprk_policy" ? "sprk_policies" : "sprk_policyversions";
            w.Rig.Probe.Grant(set, id, AccessRights.Read);
            w.Access.Grant(set, id, AccessRights.Read);
        }

        var result = await w.DraftAsync();

        StatusOf(result).Should().Be(StatusCodes.Status202Accepted);
        foreach (var (table, id) in rows)
        {
            w.State(table, id).Should().Be(RuleSetService.StateInactive, $"{table} {id} is superseded");
            w.Status(table, id).Should().Be(RuleSetService.StatusInactive);
        }

        w.H.Writes.Should().NotContain(x => x.Kind == RuleSetServiceTests.Write.Delete, "a redraft never deletes");
        w.H.DeleteAttempts.Should().BeEmpty();
        w.Access.Calls.Select(c => (c.Set, c.Id)).Should().BeEquivalentTo(new[]
        {
            ("sprk_rulesets", w.D.RuleSet), (BillingAdminAuthorization.ProcedureEntitySet, w.D.Procedure),
        }, "the reject route's two questions, as the caller; none per draft row");
        w.Access.Calls.Should().OnlyContain(c => c.HasToken);
        w.Rig.Probe.Calls.Select(c => c.Set).Should().Equal(new[] { StoredFileSchema.ProcedureVersionEntitySet }, "only the version's own rights");
        w.Queued.Should().ContainSingle();
    }

    private static async Task<(int Status, string Body)> ExecuteAsync(IResult result)
    {
        var http = new DefaultHttpContext
        {
            RequestServices = new ServiceCollection().AddLogging().BuildServiceProvider(),
        };
        var body = new MemoryStream();
        http.Response.Body = body;
        await result.ExecuteAsync(http);
        return (http.Response.StatusCode, System.Text.Encoding.UTF8.GetString(body.ToArray()));
    }

    [Fact(DisplayName = "116 fix 5/6 (EB-D-216a, NFR-01) must-fire: no Read on the version's rule set is the MISSING-VERSION 404, byte for byte; no draft state read, nothing deactivated or queued")]
    public async Task Redraft_NoReadOnTheRuleSet_Is404_AndTouchesNothing()
    {
        var w = await World.DraftedAndReplacedAsync();
        var missing = await ExecuteAsync(await w.DraftAsync(Guid.NewGuid()));
        w.Access.Grant("sprk_rulesets", w.D.RuleSet, AccessRights.None);
        var writes = w.H.Writes.Count;
        w.H.Queries.Clear();

        var result = await w.DraftAsync();

        StatusOf(result).Should().Be(StatusCodes.Status404NotFound);
        ErrorCodeOf(result).Should().Be(ProcedureService.CodeVersionNotFound);
        missing.Status.Should().Be(StatusCodes.Status404NotFound);
        (await ExecuteAsync(result)).Body.Should().Be(missing.Body, "denied is the same as unknown for the id in the URL (the version's)");
        w.DraftStateReads().Should().BeEmpty("a caller without the rights learns nothing about the version's drafts");
        w.H.Writes.Count.Should().Be(writes);
        w.H.Transactions.Should().BeEmpty();
        w.DraftRows().Should().OnlyContain(r => w.State(r.Table, r.Id) == null, "every draft is still active");
        w.Queued.Should().BeEmpty();
    }

    [Fact(DisplayName = "116 fix 5 (EB-D-216a) must-fire: Read but no Write on the rule set's Procedure is 403; no draft state read, nothing deactivated or queued")]
    public async Task Redraft_NoWriteOnTheProcedure_Is403_AndTouchesNothing()
    {
        var w = await World.DraftedAndReplacedAsync();
        w.Access.Grant(BillingAdminAuthorization.ProcedureEntitySet, w.D.Procedure, AccessRights.Read);
        var writes = w.H.Writes.Count;
        w.H.Queries.Clear();

        var result = await w.DraftAsync();

        StatusOf(result).Should().Be(StatusCodes.Status403Forbidden);
        ReasonCodeOf(result).Should().Be(FinanceAuthorizationFilter.InsufficientRightsReasonCode);
        w.DraftStateReads().Should().BeEmpty();
        w.H.Writes.Count.Should().Be(writes);
        w.H.Transactions.Should().BeEmpty();
        w.DraftRows().Should().OnlyContain(r => w.State(r.Table, r.Id) == null);
        w.Queued.Should().BeEmpty();
    }

    [Fact(DisplayName = "116 fix 5 (EB-D-216a, rights first) must-fire: without the rights, a draft that changed in between is not reported (no draft_changed), only the 404")]
    public async Task Redraft_WithoutTheRights_LearnsNothingAboutAChangedDraft()
    {
        var w = await World.DraftedAndReplacedAsync();
        w.Access.Grant("sprk_rulesets", w.D.RuleSet, AccessRights.None);
        w.H.AfterQuery = q =>
        {
            if (q.EntityName == "sprk_policy")
            {
                w.H.ConcurrentWrite("sprk_policyversion", w.VersionOf(Q1),
                    new Dictionary<string, object> { [RuleSetService.PublishStateColumn] = new OptionSetValue(RuleSetService.PublishStatePublished) });
            }
        };

        var result = await w.DraftAsync();

        StatusOf(result).Should().Be(StatusCodes.Status404NotFound);
        ErrorCodeOf(result).Should().NotBe(RuleSetService.CodeDraftChanged);
    }

    [Fact(DisplayName = "116 fix 4 (c1) must-not-fire: a first draft supersedes nothing (202); only the reject rights and the version's own are asked")]
    public async Task FirstDraft_SupersedesNothing()
    {
        var w = new World();
        w.D = w.H.DraftVersion();
        w.GrantTheRejectRights();
        w.Jobs.Setup(j => j.SubmitJobAsync(It.IsAny<JobContract>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var result = await w.DraftAsync();

        StatusOf(result).Should().Be(StatusCodes.Status202Accepted);
        w.Rig.Probe.Calls.Select(c => c.Set).Should().Equal(new[] { StoredFileSchema.ProcedureVersionEntitySet }, "only the version's own rights");
        w.H.Transactions.Should().BeEmpty();
    }

    [Fact(DisplayName = "116 fix 4 (c1/c2) must-not-fire: a copy-forward draft and a published rule are not deactivated")]
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

    // ── Round 5 F2: a run still storing its rules ────────────────────────────────────────────────────────────────

    /// <summary>A run of the CURRENT file (the first one) that has stored its rules, and one policy whose version it has not created yet, but no marker.</summary>
    private static async Task<(World W, Guid VersionlessPolicy)> ARunStillStoringAsync()
    {
        var w = await World.DraftedAndReplacedAsync(replace: false);
        // The run's list and marker are not written yet.
        var version = w.H.Row(ProcedureEntity.VersionEntity, w.D.Version);
        version.Attributes.Remove(RuleSetService.VersionDraftedColumn);
        version.Attributes.Remove(RuleSetService.VersionNotConvertedListColumn);
        // ...and it is between the two creates of its next rule: the policy exists, its version does not.
        var policy = new Entity("sprk_policy", Guid.NewGuid())
        {
            ["sprk_name"] = "Next rule",
            ["sprk_policycode"] = $"{RuleSetService.DraftCodePrefix}{w.D.RuleSet:N}-099",
            ["sprk_ruleset"] = new EntityReference("sprk_ruleset", w.D.RuleSet),
            ["sprk_enabled"] = false,
            [RuleDomains.PolicyDomainColumn] = new OptionSetValue(RuleDomains.PolicyDomainBilling),
        };
        w.H.Seed(policy);
        w.H.SetCreatedBy(policy.Id, w.H.Identity);
        return (w, policy.Id);
    }

    [Fact(DisplayName = "116 fix 5 (F2, fix a) must-fire: drafts of a run of the CURRENT file (marker not yet written) are never planned or deactivated by /draft, nor a policy whose version is not created yet")]
    public async Task Redraft_DuringARunOfTheCurrentFile_DeactivatesNoneOfItsOutput()
    {
        var (w, versionless) = await ARunStillStoringAsync();
        var untouched = new (string Table, Guid Id)[] { ("sprk_policyversion", w.VersionOf(Q1)), ("sprk_policy", w.PolicyOf(Q1)), ("sprk_policyversion", w.VersionOf(Q3)), ("sprk_policy", w.PolicyOf(Q3)) };

        var plan = await w.H.Service().PlanRedraftAsync(w.D.Version, w.D.File, CancellationToken.None);
        var result = await w.DraftAsync();

        plan.Value!.Drafts.Should().BeEmpty("a run of the current file owns its output; its compensation runs under its lock");
        StatusOf(result).Should().Be(StatusCodes.Status202Accepted);
        foreach (var (table, id) in untouched)
        {
            w.State(table, id).Should().BeNull($"{table} {id} is the running job's output");
        }

        w.State("sprk_policy", versionless).Should().BeNull("a policy is never deactivated before its version is created");
        w.H.Transactions.Should().BeEmpty();
    }

    [Fact(DisplayName = "116 fix 5 (F2, fix b) must-fire: while that run holds the version's lock, /draft is 409 drafting_in_progress and reads no draft, deactivates nothing, queues nothing")]
    public async Task Redraft_WhileTheRunHoldsTheLock_Is409()
    {
        var (w, _) = await ARunStillStoringAsync();
        (await w.Locks.TryAcquireProcessingLockAsync(BillingRuleDraftJobHandler.LockKeyFor(w.D.Version), BillingRuleDraftJobHandler.LockDuration))
            .Should().BeTrue();
        w.H.Queries.Clear();

        var result = await w.DraftAsync();

        StatusOf(result).Should().Be(StatusCodes.Status409Conflict);
        ErrorCodeOf(result).Should().Be(ProcedureEndpoints.CodeDraftingInProgress);
        w.H.Queries.Should().NotContain(q => q.EntityName == "sprk_policy" || q.EntityName == "sprk_policyversion", "nothing is planned under a running job");
        w.H.Transactions.Should().BeEmpty();
        w.Queued.Should().BeEmpty();
    }

    // ── Round 5 F1: a deactivation whose outcome is unknown ──────────────────────────────────────────────────────

    [Theory(DisplayName = "116 fix 5 (F1) must-fire: a deactivation transaction with no answer is 503 'may have been applied', no job; a retry converges (each rule inactive once, one run queued)")]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Redraft_TransactionOutcomeUnknown_Is503_AndARetryConverges(bool committed)
    {
        var w = await World.DraftedAndReplacedAsync();
        var once = true;
        if (committed)
        {
            w.H.TransactionCommitsButAnswersUnknown = _ => once && !(once = false);
        }
        else
        {
            w.H.TransactionGivesNoAnswer = _ => once && !(once = false);
        }

        var first = await w.DraftAsync();

        StatusOf(first).Should().Be(StatusCodes.Status503ServiceUnavailable);
        ErrorCodeOf(first).Should().Be(RuleSetService.CodeRedraftOutcomeUnknown);
        ((ProblemHttpResult)first).ProblemDetails.Detail.Should().Contain("may already have been replaced").And.Contain("Try again");
        w.Queued.Should().BeEmpty();

        var retry = await w.DraftAsync();

        StatusOf(retry).Should().Be(StatusCodes.Status202Accepted);
        w.DraftRows().Should().OnlyContain(r => w.State(r.Table, r.Id) == RuleSetService.StateInactive);
        w.H.Transactions.Should().HaveCount(committed ? 1 : 2, committed ? "the retry has nothing left to deactivate" : "the retry deactivates them");
        w.Queued.Should().ContainSingle();
    }

    // ── Round 5 K2: an inactive version on a planned rule ────────────────────────────────────────────────────────

    [Fact(DisplayName = "116 fix 5 (K2) must-not-fire: a planned rule that also has an INACTIVE version is still deactivated (only active versions are compared)")]
    public async Task Redraft_APlannedRuleWithAnInactiveVersion_IsDeactivated()
    {
        var w = await World.DraftedAndReplacedAsync();
        var inactive = w.H.AddVersion(w.PolicyOf(Q2), 2, ProposalState.Proposed, quote: "an older wording");
        w.H.Row("sprk_policyversion", inactive)["statecode"] = new OptionSetValue(RuleSetService.StateInactive);
        w.H.Row("sprk_policyversion", inactive)[RuleSetService.ProcedureVersionColumn] = new EntityReference(ProcedureEntity.VersionEntity, w.D.Version);

        var result = await w.DraftAsync();

        StatusOf(result).Should().Be(StatusCodes.Status202Accepted);
        w.DraftRows().Should().OnlyContain(r => w.State(r.Table, r.Id) == RuleSetService.StateInactive);
        w.Queued.Should().ContainSingle();
    }

    [Fact(DisplayName = "116 fix 5 (K2) must-fire: an ACTIVE version added to a planned rule since the plan is still a 409")]
    public async Task Deactivate_AnActiveVersionAddedSinceThePlan_Is409()
    {
        var w = await World.DraftedAndReplacedAsync();
        var service = w.H.Service();
        var plan = (await service.PlanRedraftAsync(w.D.Version, w.NewFile, CancellationToken.None)).Value!;
        w.H.AddVersion(w.PolicyOf(Q2), 2, ProposalState.Proposed, quote: "a new wording");

        var outcome = await service.DeactivateSupersededDraftsAsync(w.D.Version, plan, CancellationToken.None);

        outcome.ErrorCode.Should().Be(RuleSetService.CodeDraftChanged);
        w.H.Transactions.Should().BeEmpty();
    }

    // ── Round 5 K2: the transaction cap ──────────────────────────────────────────────────────────────────────────

    [Theory(DisplayName = "116 fix 5 (K2) the transaction cap: a plan of more rows than one transaction holds is 422 redraft_too_large with nothing written; one of exactly the cap goes through")]
    [InlineData(498, StatusCodes.Status422UnprocessableEntity)] // 3 rules + 498 = 501 rules, 1,002 rows
    [InlineData(497, StatusCodes.Status202Accepted)] // 500 rules, 1,000 rows
    public async Task Redraft_OverTheTransactionCap_Is422_AndWritesNothing(int seeded, int expected)
    {
        var w = await World.DraftedAndReplacedAsync(beforeReplace: x => x.SeedUntouchedDrafts(seeded));
        var writes = w.H.Writes.Count;

        var result = await w.DraftAsync();

        StatusOf(result).Should().Be(expected);
        if (expected == StatusCodes.Status422UnprocessableEntity)
        {
            ErrorCodeOf(result).Should().Be(RuleSetService.CodeRedraftTooLarge);
            ((ProblemHttpResult)result).ProblemDetails.Detail.Should().Contain(RuleSetService.MaxPerTransaction.ToString(System.Globalization.CultureInfo.InvariantCulture));
            w.H.Writes.Count.Should().Be(writes);
            w.H.Transactions.Should().BeEmpty();
            w.DraftRows().Should().OnlyContain(r => w.State(r.Table, r.Id) == null);
            w.Queued.Should().BeEmpty();
        }
        else
        {
            w.H.Transactions.Should().ContainSingle().Which.Should().HaveCount(RuleSetService.MaxPerTransaction);
        }
    }

    // ── Round 5 (uac-r2 addition 3): the queue fails after the deactivation ──────────────────────────────────────

    [Fact(DisplayName = "116 fix 5 (addition 3) must-fire: the queue fails after the deactivation committed: 503 redraft_not_queued, never a 2xx; a retry plans nothing, the job's check does not block, and the run is queued")]
    public async Task Redraft_QueueFailsAfterTheDeactivation_Is503_AndARetryQueues()
    {
        var w = await World.DraftedAndReplacedAsync();
        w.QueueFault = new InvalidOperationException("simulated Service Bus outage");

        var first = await w.DraftAsync();

        StatusOf(first).Should().Be(StatusCodes.Status503ServiceUnavailable);
        ErrorCodeOf(first).Should().Be(ProcedureEndpoints.CodeRedraftNotQueued);
        ((ProblemHttpResult)first).ProblemDetails.Detail.Should().Contain("already replaced").And.Contain("again");
        w.DraftRows().Should().OnlyContain(r => w.State(r.Table, r.Id) == RuleSetService.StateInactive);

        w.QueueFault = null;
        var service = w.H.Service();
        (await service.PlanRedraftAsync(w.D.Version, w.NewFile, CancellationToken.None)).Value!.Drafts.Should().BeEmpty();
        (await service.RemoveUntouchedDraftsAsync(w.D.Version, w.NewFile, CancellationToken.None)).Kind.Should().Be(RuleSetOutcomeKind.Done,
            "no superseded draft remains active, so the job's check does not block");
        var retry = await w.DraftAsync();

        StatusOf(retry).Should().Be(StatusCodes.Status202Accepted);
        w.H.Transactions.Should().ContainSingle("the retry has nothing to deactivate");
        w.Queued.Should().ContainSingle();
    }

    [Fact(DisplayName = "116 fix 5 (addition 3) must-fire: a client that disconnects once the deactivation is sent still gets the run queued (the commit and the queueing ignore its token)")]
    public async Task Redraft_ClientDisconnectsDuringTheCommit_TheRunIsStillQueued()
    {
        var w = await World.DraftedAndReplacedAsync();
        using var cts = new CancellationTokenSource();
        w.H.TransactionCommitsButAnswersUnknown = _ =>
        {
            cts.Cancel(); // the client goes away while the transaction commits
            return false;
        };

        var result = await w.DraftAsync(ct: cts.Token);

        StatusOf(result).Should().Be(StatusCodes.Status202Accepted);
        w.DraftRows().Should().OnlyContain(r => w.State(r.Table, r.Id) == RuleSetService.StateInactive);
        w.Queued.Should().ContainSingle("inactive drafts are never left with no run queued");
        w.QueuedTokens.Single().CanBeCanceled.Should().BeFalse();
        w.H.Entities.Invocations.Where(i => i.Method.Name == nameof(IGenericEntityService.ExecuteTransactionAsync))
            .Select(i => (CancellationToken)i.Arguments[1]).Should().ContainSingle().Which.CanBeCanceled.Should().BeFalse();
    }

    // ── Round 3 K1: a probe fault answers every id alike ─────────────────────────────────────────────────────────

    [Fact(DisplayName = "116 fix 4 (K1) must-fire: when the version rights probe faults, an unknown id and an existing version give the same answer from the same reads")]
    public async Task ProbeFault_UnknownAndExistingVersion_AnswerAlike()
    {
        async Task<(int Status, string? Code, string[] Reads, int RightsAsked)> Run(bool exists)
        {
            var w = await World.DraftedAndReplacedAsync(replace: false);
            w.Rig.Probe.Fault = new HttpRequestException("simulated Dataverse fault");
            w.H.Entities.Invocations.Clear();
            var result = await w.DraftAsync(exists ? w.D.Version : Guid.NewGuid());
            var reads = w.H.Entities.Invocations
                .Select(i => i.Method.Name + ":" + (i.Arguments.FirstOrDefault() is QueryExpression q ? q.EntityName : i.Arguments.FirstOrDefault())).ToArray();
            w.Queued.Should().BeEmpty();
            var problem = result is ProblemHttpResult p ? p.ProblemDetails : null;
            return (StatusOf(result), problem is null ? null : $"{problem.Detail}|{string.Join(",", problem.Extensions.Select(e => $"{e.Key}={e.Value}").Where(e => !e.StartsWith("trace", StringComparison.OrdinalIgnoreCase)))}", reads, w.Access.Calls.Count);
        }

        var unknown = await Run(exists: false);
        var existing = await Run(exists: true);

        unknown.Status.Should().Be(StatusCodes.Status403Forbidden);
        existing.Status.Should().Be(unknown.Status);
        existing.Code.Should().Be(unknown.Code);
        existing.Reads.Should().Equal(unknown.Reads, "the version is not read before the fault is answered");
        existing.Reads.Should().BeEmpty();
        existing.RightsAsked.Should().Be(0, "nothing else is asked either");
    }
}
