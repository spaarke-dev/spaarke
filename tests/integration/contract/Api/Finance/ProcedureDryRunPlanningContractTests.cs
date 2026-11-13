using System.Runtime.CompilerServices;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Services.Finance;
using Sprk.Bff.Api.Services.Finance.Procedures;
using Sprk.Bff.Api.Services.Finance.Resolution;
using Sprk.Bff.Api.Services.Signals;
using Sprk.Bff.Api.Services.Signals.Billing;
using Sprk.Bff.Api.Tests.Services.Finance;
using Xunit;

namespace Sprk.Bff.Api.Tests.Api.Finance;

/// <summary>
/// ebilling-r1 task 084d-1 (design <c>notes/084d-dry-run-design.md</c> §1-2; ontology-r1 review F1 and F4): planning a proposed override
/// without writing. The planning service refuses every write; each <c>Plan*Async</c> runs the write's own checks, sends nothing, asks no
/// owner and closes nothing, and plans exactly the rows the write then creates.
/// </summary>
/// <remarks>
/// Maintain-class (ADR-038, KEEP contract path as 084's deviation 7): the dry run's no-write and its agreement with the write are the
/// contract 084d-2's route rests on. Dataverse is <see cref="RuleSetServiceTests.Harness"/> (real query evaluation, an all-or-nothing
/// transaction); the overlay's resolver test runs the REAL <see cref="ProcedureResolver"/> over <see cref="RecordProcedureRig"/>'s world.
/// </remarks>
public sealed class ProcedureDryRunPlanningContractTests
{
    private const string Matter = "sprk_matter";
    private static readonly Guid MatterId = Guid.Parse("084d0001-0000-4000-8000-000000000001");
    private static readonly Guid Caller = Guid.Parse("084d0001-0000-4000-8000-0000000000ca");
    private static readonly DateTime Now = new(2026, 11, 5, 14, 30, 0, DateTimeKind.Utc);
    private static readonly DateTime Stamp = new(2026, 11, 5, 14, 31, 0, DateTimeKind.Utc);
    private static readonly CancellationToken Ct = CancellationToken.None;

    // ═══════════════════════════════════════════════════════════════════════════
    // F1: the planning service refuses every write
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>The write members of the seam. A member added to the seam must be classified here (the next test fails until it is).</summary>
    private static readonly string[] WriteMembers =
    {
        nameof(IGenericEntityService.CreateAsync), nameof(IGenericEntityService.UpsertAsync), nameof(IGenericEntityService.UpdateAsync),
        nameof(IGenericEntityService.UpdateIfRowVersionMatchesAsync), nameof(IGenericEntityService.BulkUpdateAsync),
        nameof(IGenericEntityService.BulkUpdateIfRowVersionsMatchAsync), nameof(IGenericEntityService.ExecuteTransactionAsync),
        nameof(IGenericEntityService.DeleteAsync), nameof(IGenericEntityService.AssociateAsync),
    };

    private static readonly string[] ReadMembers =
    {
        nameof(IGenericEntityService.RetrieveAsync), nameof(IGenericEntityService.RetrieveByAlternateKeyAsync),
        nameof(IGenericEntityService.RetrieveMultipleAsync), nameof(IGenericEntityService.GetEntitySetNameAsync),
        nameof(IGenericEntityService.GetLookupNavigationAsync), nameof(IGenericEntityService.GetColumnMetadataAsync),
        nameof(IGenericEntityService.GetCollectionNavigationAsync), nameof(IGenericEntityService.GetCurrentIdentityIdAsync),
    };

    [Fact(DisplayName = "084d-1 F1: every member of IGenericEntityService is classified as a read or a write")]
    public void EveryMemberOfTheSeam_IsClassified()
    {
        var members = typeof(IGenericEntityService).GetMethods().Select(m => m.Name).Distinct().Order().ToList();

        members.Should().BeEquivalentTo(WriteMembers.Concat(ReadMembers),
            "a new member of the seam must be classified, and a write must be refused by WriteRefusingEntityService");
    }

    public static TheoryData<string, bool> EveryWrite()
    {
        var data = new TheoryData<string, bool>();
        foreach (var member in WriteMembers)
        {
            data.Add(member, false);
            data.Add(member, true);
        }

        return data;
    }

    [Theory(DisplayName = "084d-1 F1: each write member throws on the planning service and on the overlay, and reaches nothing")]
    [MemberData(nameof(EveryWrite))]
    public async Task EveryWrite_Throws_AndReachesNothing(string member, bool overlay)
    {
        var inner = new Mock<IGenericEntityService>(MockBehavior.Strict);
        IGenericEntityService service = overlay
            ? new ProcedureRowOverlay(inner.Object, EmptyPlan, Stamp)
            : new WriteRefusingEntityService(inner.Object);
        var row = new Entity(ProcedureAssignmentEntity.Entity, Guid.NewGuid());
        var fields = new Dictionary<string, object> { ["sprk_name"] = "x" };

        Func<Task> call = member switch
        {
            nameof(IGenericEntityService.CreateAsync) => () => service.CreateAsync(row, Ct),
            nameof(IGenericEntityService.UpsertAsync) => () => service.UpsertAsync(row, Ct),
            nameof(IGenericEntityService.UpdateAsync) => () => service.UpdateAsync(row.LogicalName, row.Id, fields, Ct),
            nameof(IGenericEntityService.UpdateIfRowVersionMatchesAsync) => () => service.UpdateIfRowVersionMatchesAsync(row.LogicalName, row.Id, "1", fields, Ct),
            nameof(IGenericEntityService.BulkUpdateAsync) => () => service.BulkUpdateAsync(row.LogicalName, new() { (row.Id, fields) }, Ct),
            nameof(IGenericEntityService.BulkUpdateIfRowVersionsMatchAsync) =>
                () => service.BulkUpdateIfRowVersionsMatchAsync(row.LogicalName, new() { (row.Id, fields, 1L) }, Ct),
            nameof(IGenericEntityService.ExecuteTransactionAsync) =>
                () => service.ExecuteTransactionAsync(new OrganizationRequest[] { new Microsoft.Xrm.Sdk.Messages.CreateRequest { Target = row } }, Ct),
            nameof(IGenericEntityService.DeleteAsync) => () => service.DeleteAsync(row.LogicalName, row.Id, Ct),
            nameof(IGenericEntityService.AssociateAsync) =>
                () => service.AssociateAsync(row.LogicalName, row.Id, "sprk_rel", new[] { new EntityReference(Matter, MatterId) }, Ct),
            _ => throw new InvalidOperationException($"Unclassified write member {member}."),
        };

        await call.Should().ThrowAsync<InvalidOperationException>().WithMessage("*A dry run never writes*");
        inner.Invocations.Should().BeEmpty("a refused write is refused before anything reaches the application service");
    }

    [Fact(DisplayName = "084d-1 F1: reads pass through; one layer of refusal only")]
    public async Task Reads_PassThrough_AndTheServiceWrapsOnce()
    {
        var inner = new Mock<IGenericEntityService>(MockBehavior.Strict);
        var answer = new EntityCollection(new List<Entity> { new(Matter, MatterId) });
        inner.Setup(e => e.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), Ct)).ReturnsAsync(answer);
        var service = new WriteRefusingEntityService(inner.Object);

        (await service.RetrieveMultipleAsync(new QueryExpression(Matter), Ct)).Should().BeSameAs(answer);
        FluentActions.Invoking(() => new WriteRefusingEntityService(service)).Should().Throw<ArgumentException>();
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // F1: planning runs the write's checks, sends nothing and plans what the write creates
    // ═══════════════════════════════════════════════════════════════════════════

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class World
    {
        public readonly RuleSetServiceTests.Harness H = new();
        public readonly Mock<SignalClosureService> Closure;
        public readonly Mock<CallerRecordAccessProbe> Probe = new(
            MockBehavior.Strict, new HttpClient(), new ConfigurationBuilder().Build(), NullLogger<CallerRecordAccessProbe>.Instance, null!, null!);

        public World()
        {
            H.Seed(new Entity(Matter, MatterId));
            var writer = (SignalWriter)RuntimeHelpers.GetUninitializedObject(typeof(SignalWriter)); // reached only by a close with Signals; there are none
            Closure = new Mock<SignalClosureService>(MockBehavior.Loose, writer, H.Entities.Object, TimeProvider.System,
                NullLogger<SignalClosureService>.Instance)
            { CallBase = true };
        }

        public ProcedureAssignmentService Service() =>
            new(H.Entities.Object, H.Ownership.Object, H.Service(), Closure.Object,
                new AffectedInvoiceCounter(H.Entities.Object, Probe.Object, NullLogger<AffectedInvoiceCounter>.Instance),
                NullLogger<ProcedureAssignmentService>.Instance)
            { Clock = new FixedClock(new DateTimeOffset(Now)) };

        public WriteRefusingEntityService Reads() => new(H.Entities.Object);

        /// <summary>An in-force rule (Accepted, current, enabled) in a Published client-default Billing set.</summary>
        public Guid Rule(string code = "BILL-A")
        {
            var set = H.RuleSet(RuleSetService.RuleSetPublished, scope: (int)RuleSetScope.ClientDefault);
            return H.Rule(set, code, ProposalState.Accepted, current: true).Policy;
        }

        public Guid Procedure(int status = ProcedureEntity.StatusActive, int area = RuleDomains.PolicyDomainBilling, int? state = null)
        {
            var p = new Entity(ProcedureEntity.Entity, Guid.NewGuid())
            {
                [ProcedureEntity.NameColumn] = "Acme OCG",
                [ProcedureEntity.StatusColumn] = new OptionSetValue(status),
                [ProcedureEntity.AreaColumn] = new OptionSetValue(area),
            };
            if (state is { } statecode)
            {
                p["statecode"] = new OptionSetValue(statecode);
            }

            H.Seed(p);
            return p.Id;
        }

        /// <summary>A mark to check writes against: what has been written, sent, owned and closed so far.</summary>
        public (int Writes, int Transactions, int Owners, int Closes, int EntityWrites) Mark() => (
            H.Writes.Count, H.Transactions.Count, H.Ownership.Invocations.Count, Closure.Invocations.Count,
            H.Entities.Invocations.Count(i => WriteMembers.Contains(i.Method.Name)));

        public void NothingSince((int, int, int, int, int) mark) =>
            Mark().Should().Be(mark, "planning writes nothing, sends no transaction, asks no owner and closes nothing");
    }

    private static RuleChangeRequest Change(Guid policy, Guid? firm = null) =>
        new(policy, "Agreed with the firm for this matter", firm, null, null, "Daily hours cap (this matter)", null);

    [Fact(DisplayName = "084d-1 F1: a change is planned without a send, an owner or a close, and its images are the rows the write then creates")]
    public async Task Change_IsPlanned_WithoutWriting_AndItsImagesAreWhatTheWriteCreates()
    {
        var w = new World();
        var rule = w.Rule();
        var service = w.Service();
        var mark = w.Mark();

        var planned = await service.PlanChangeRuleAsync(w.Reads(), Matter, MatterId, Change(rule), Now, Ct);

        planned.Kind.Should().Be(RuleSetOutcomeKind.Done);
        w.NothingSince(mark);
        var plan = planned.Value!;
        plan.Write.Should().Be(ProcedureOverridePlan.WriteChangeRule);
        plan.Deleted.Should().BeEmpty();
        plan.Created.Select(c => c.LogicalName).Should().BeEquivalentTo(
            new[] { "sprk_ruleset", "sprk_policy", ProcedureAssignmentEntity.Entity, "sprk_policyversion" });
        plan.AfterImages.Select(i => i.ToEntityReference()).Should().BeEquivalentTo(plan.Created);
        plan.AfterImages.Should().OnlyContain(i => !i.Contains("ownerid"), "the dry run asks no owner");
        var policy = plan.AfterImages.Single(i => i.LogicalName == "sprk_policy");
        var version = plan.AfterImages.Single(i => i.LogicalName == "sprk_policyversion");
        policy.GetAttributeValue<bool>("sprk_enabled").Should().BeTrue("the plan applies stage 3's switch-on to the Off image");
        policy.GetAttributeValue<EntityReference>("sprk_currentversion").Id.Should().Be(version.Id);

        // The same change, written: every row it creates is a planned row, and holds each planned column's value.
        var before = w.H.Writes.Count;
        (await service.ChangeRuleAsync(Matter, MatterId, Change(rule), Caller, null, Ct)).Kind.Should().Be(RuleSetOutcomeKind.Done);
        w.H.Writes.Skip(before).Where(x => x.Kind == RuleSetServiceTests.Write.Create).Select(x => new EntityReference(x.Entity, x.Id))
            .Should().BeEquivalentTo(plan.Created);
        foreach (var image in plan.AfterImages)
        {
            var written = w.H.Row(image.LogicalName, image.Id);
            foreach (var (column, value) in image.Attributes.Where(a => a.Key != "sprk_effectivefrom")) // the set's date is the build day (UtcNow.Date)
            {
                written.Attributes.Should().ContainKey(column);
                written[column].Should().BeEquivalentTo(value, $"{image.LogicalName}.{column} is planned as the write writes it");
            }
        }
    }

    [Fact(DisplayName = "084d-1 F1: a change on a record whose record rule is Off plans the REAL policy read whole and patched on, and creates no set or policy")]
    public async Task Change_OverAnOffRecordRule_PlansTheRealRowPatchedOn()
    {
        var w = new World();
        var rule = w.Rule();
        var service = w.Service();
        var first = await service.ChangeRuleAsync(Matter, MatterId, Change(rule), Caller, null, Ct);
        (await service.RemoveAsync(Matter, MatterId, first.Value!.Override.Id, null, Ct)).Value!.RuleSwitchedOff.Should().BeTrue();
        var recordRule = first.Value.RecordRule.PolicyId;
        var mark = w.Mark();

        var plan = (await service.PlanChangeRuleAsync(w.Reads(), Matter, MatterId, Change(rule), Now, Ct)).Value!;

        w.NothingSince(mark);
        plan.Created.Select(c => c.LogicalName).Should().BeEquivalentTo(new[] { ProcedureAssignmentEntity.Entity, "sprk_policyversion" });
        var policy = plan.AfterImages.Single(i => i.LogicalName == "sprk_policy");
        policy.Id.Should().Be(recordRule);
        plan.Created.Should().NotContain(policy.ToEntityReference(), "an existing record rule stays real (it is asked, not exempt)");
        policy.GetAttributeValue<bool>("sprk_enabled").Should().BeTrue();
        policy.Contains("sprk_name").Should().BeTrue("a real row's after-image is the row read whole");
        plan.AfterImages.Single(i => i.LogicalName == "sprk_policyversion").GetAttributeValue<int>("sprk_versionnumber").Should().Be(2);
    }

    [Fact(DisplayName = "084d-1 F1: removing a changed rule plans the delete and the record rule read whole and switched off")]
    public async Task Remove_OfAChangedRule_PlansTheDeleteAndTheSwitchOff()
    {
        var w = new World();
        var rule = w.Rule();
        var service = w.Service();
        var changed = (await service.ChangeRuleAsync(Matter, MatterId, Change(rule), Caller, null, Ct)).Value!;
        var mark = w.Mark();

        var plan = (await service.PlanRemoveAsync(w.Reads(), Matter, MatterId, changed.Override.Id, Ct)).Value!;

        w.NothingSince(mark);
        plan.Kind.Should().Be(ProcedureAssignmentService.KindRuleChanged);
        plan.Deleted.Should().Equal(new EntityReference(ProcedureAssignmentEntity.Entity, changed.Override.Id));
        plan.Created.Should().BeEmpty();
        var policy = plan.AfterImages.Should().ContainSingle().Subject;
        policy.Id.Should().Be(changed.RecordRule.PolicyId);
        policy.GetAttributeValue<bool>("sprk_enabled").Should().BeFalse();
        policy.GetAttributeValue<EntityReference>("sprk_currentversion").Id.Should().Be(changed.RecordRule.VersionId, "read whole, only the switch-off changed");
    }

    [Fact(DisplayName = "084d-1 F1: assign and a plain Exclude plan one created row each; removing a plain Exclude plans the delete only")]
    public async Task AssignAndExclude_PlanOneRow_AndAPlainRemovePlansTheDeleteOnly()
    {
        var w = new World();
        var procedure = w.Procedure();
        var rule = w.Rule();
        var service = w.Service();
        var mark = w.Mark();

        var assign = (await service.PlanAssignProcedureAsync(w.Reads(), Matter, MatterId, new ProcedureAssignmentRequest(procedure, null, null, null), Ct)).Value!;
        var exclude = (await service.PlanExcludeRuleAsync(w.Reads(), Matter, MatterId, new RuleExcludeRequest(rule, "Waived", null), Ct)).Value!;

        w.NothingSince(mark);
        assign.Created.Should().Equal(new EntityReference(ProcedureAssignmentEntity.Entity,
            ProcedureAssignmentService.AssignmentIdFor(Matter, MatterId, ProcedureAssignmentEntity.ProcedureColumn, procedure)));
        assign.AfterImages.Single().GetAttributeValue<EntityReference>(ProcedureAssignmentEntity.ProcedureColumn).Id.Should().Be(procedure);
        exclude.Created.Should().ContainSingle().Which.Id.Should().Be(
            ProcedureAssignmentService.AssignmentIdFor(Matter, MatterId, ProcedureAssignmentEntity.ExcludedPolicyColumn, rule));

        var written = (await service.ExcludeRuleAsync(Matter, MatterId, new RuleExcludeRequest(rule, "Waived", null), Caller, Ct)).Value!;
        mark = w.Mark();
        var remove = (await service.PlanRemoveAsync(w.Reads(), Matter, MatterId, written.Id, Ct)).Value!;
        w.NothingSince(mark);
        remove.Kind.Should().Be(ProcedureAssignmentService.KindRuleExcluded);
        remove.Deleted.Should().ContainSingle().Which.Id.Should().Be(written.Id);
        remove.AfterImages.Should().BeEmpty();
    }

    public static TheoryData<string> Refusals() => new() { "retired-procedure", "non-billing-procedure", "inactive-procedure", "duplicate-exclude", "not-in-force", "no-reason", "set-conflict", "no-override" };

    [Theory(DisplayName = "084d-1 F1: the plan refuses with the write's own status and code (it runs the write's checks)")]
    [MemberData(nameof(Refusals))]
    public async Task ThePlan_RefusesAsTheWriteDoes(string refusal)
    {
        var w = new World();
        var service = w.Service();
        var rule = w.Rule();
        Func<Task<(RuleSetOutcomeKind, int, string?)>> plan, write;
        switch (refusal)
        {
            case "retired-procedure":
                var retired = w.Procedure(ProcedureEntity.StatusRetired);
                var assign = new ProcedureAssignmentRequest(retired, null, null, null);
                plan = async () => Of(await service.PlanAssignProcedureAsync(w.Reads(), Matter, MatterId, assign, Ct));
                write = async () => Of(await service.AssignProcedureAsync(Matter, MatterId, assign, Caller, Ct));
                break;
            case "non-billing-procedure":
                // EB-D-228: the plan refuses a Procedure of another area as the write does, so a preview never shows an assign the write refuses.
                var otherArea = new ProcedureAssignmentRequest(w.Procedure(area: RuleDomains.PolicyDomainBilling + 1), null, null, null);
                plan = async () => Of(await service.PlanAssignProcedureAsync(w.Reads(), Matter, MatterId, otherArea, Ct));
                write = async () => Of(await service.AssignProcedureAsync(Matter, MatterId, otherArea, Caller, Ct));
                break;
            case "inactive-procedure":
                // EB-D-232 (ISS-021): Status Active but statecode 1 - the plan refuses as the write does, so a preview never shows an assign the write refuses.
                var inactive = new ProcedureAssignmentRequest(w.Procedure(state: 1), null, null, null);
                plan = async () => Of(await service.PlanAssignProcedureAsync(w.Reads(), Matter, MatterId, inactive, Ct));
                write = async () => Of(await service.AssignProcedureAsync(Matter, MatterId, inactive, Caller, Ct));
                break;
            case "duplicate-exclude":
                (await service.ExcludeRuleAsync(Matter, MatterId, new RuleExcludeRequest(rule, "Waived", null), Caller, Ct)).Kind.Should().Be(RuleSetOutcomeKind.Done);
                plan = async () => Of(await service.PlanChangeRuleAsync(w.Reads(), Matter, MatterId, Change(rule), Now, Ct));
                write = async () => Of(await service.ChangeRuleAsync(Matter, MatterId, Change(rule), Caller, null, Ct));
                break;
            case "not-in-force":
                w.H.Row("sprk_policy", rule)["sprk_enabled"] = false;
                plan = async () => Of(await service.PlanChangeRuleAsync(w.Reads(), Matter, MatterId, Change(rule), Now, Ct));
                write = async () => Of(await service.ChangeRuleAsync(Matter, MatterId, Change(rule), Caller, null, Ct));
                break;
            case "no-reason":
                var noReason = new RuleExcludeRequest(rule, " ", null);
                plan = async () => Of(await service.PlanExcludeRuleAsync(w.Reads(), Matter, MatterId, noReason, Ct));
                write = async () => Of(await service.ExcludeRuleAsync(Matter, MatterId, noReason, Caller, Ct));
                break;
            case "set-conflict":
                // A row at the record's derived set id that is not the record's set: the write refuses it at stage 1, before any send.
                w.H.Seed(new Entity("sprk_ruleset", ProcedureAssignmentService.RecordRuleSetIdFor(Matter, MatterId, null))
                {
                    ["sprk_domain"] = new OptionSetValue(RuleDomains.PolicyDomainBilling),
                    ["sprk_scope"] = new OptionSetValue((int)RuleSetScope.ClientDefault),
                });
                plan = async () => Of(await service.PlanChangeRuleAsync(w.Reads(), Matter, MatterId, Change(rule), Now, Ct));
                write = async () => Of(await service.ChangeRuleAsync(Matter, MatterId, Change(rule), Caller, null, Ct));
                break;
            default:
                plan = async () => Of(await service.PlanRemoveAsync(w.Reads(), Matter, MatterId, Guid.NewGuid(), Ct));
                write = async () => Of(await service.RemoveAsync(Matter, MatterId, Guid.NewGuid(), null, Ct));
                break;
        }

        var mark = w.Mark();
        var planned = await plan();
        w.NothingSince(mark);
        var written = await write();

        planned.Item1.Should().NotBe(RuleSetOutcomeKind.Done);
        planned.Should().Be(written);
        if (refusal == "non-billing-procedure")
        {
            planned.Should().Be((RuleSetOutcomeKind.Refused, 409, ProcedureAssignmentService.CodeProcedureNotBilling));
        }

        if (refusal == "inactive-procedure")
        {
            planned.Should().Be((RuleSetOutcomeKind.Refused, 409, ProcedureAssignmentService.CodeProcedureInactive));
        }
    }

    private static (RuleSetOutcomeKind, int, string?) Of<T>(RuleSetOutcome<T> outcome) where T : class =>
        (outcome.Kind, outcome.StatusCode, outcome.ErrorCode);

    [Fact(DisplayName = "084d-1 F1: a read that faults during planning is 503 assignment.read_failed, and nothing is written")]
    public async Task APlanningReadFault_Is503()
    {
        var w = new World();
        var rule = w.Rule();
        var service = w.Service();
        w.H.AfterQuery = q =>
        {
            if (q.EntityName == "sprk_ruleset" && q.Criteria.Conditions.Any(c => c.AttributeName == "sprk_rulesetid"
                    && c.Values.Contains(ProcedureAssignmentService.RecordRuleSetIdFor(Matter, MatterId, null))))
            {
                throw new InvalidOperationException("simulated throttling");
            }
        };
        var mark = w.Mark();

        var planned = await service.PlanChangeRuleAsync(w.Reads(), Matter, MatterId, Change(rule), Now, Ct);

        Of(planned).Should().Be((RuleSetOutcomeKind.Refused, StatusCodes.Status503ServiceUnavailable, "assignment.read_failed"));
        w.NothingSince(mark);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // F4: the overlay fails closed
    // ═══════════════════════════════════════════════════════════════════════════

    private static readonly ProcedureOverridePlan EmptyPlan = new("Test", "Test", Array.Empty<Entity>(), Array.Empty<EntityReference>(), Array.Empty<EntityReference>());

    private static ProcedureOverridePlan PlanOf(IEnumerable<Entity> created, IEnumerable<Entity>? updated = null, IEnumerable<EntityReference>? deleted = null)
    {
        var made = created.ToList();
        return new("Test", "Test", made.Concat(updated ?? Array.Empty<Entity>()).ToList(), made.Select(m => m.ToEntityReference()).ToList(),
            (deleted ?? Array.Empty<EntityReference>()).ToList());
    }

    /// <summary>A strict inner service that answers each query on a table from <paramref name="pages"/> in turn, and records the queries.</summary>
    private static Mock<IGenericEntityService> Inner(List<QueryExpression> asked, params EntityCollection[] pages)
    {
        var queue = new Queue<EntityCollection>(pages);
        var inner = new Mock<IGenericEntityService>(MockBehavior.Strict);
        inner.Setup(e => e.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((QueryExpression q, CancellationToken _) =>
            {
                asked.Add(q);
                return queue.Dequeue();
            });
        return inner;
    }

    private static Entity Row(string table, Guid id, params (string Column, object Value)[] columns)
    {
        var row = new Entity(table, id);
        foreach (var (column, value) in columns)
        {
            row[column] = value;
        }

        return row;
    }

    [Fact(DisplayName = "084d-1 F4: a replaced real row and a deleted row are dropped; the image comes once, on the last page, cut to the query's columns")]
    public async Task ReplacedAndDeletedRows_AreDropped_AndTheImageComesOnceOnTheLastPage()
    {
        var table = ProcedureAssignmentEntity.Entity;
        var (replaced, deleted, kept) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var image = Row(table, replaced, ("sprk_name", "planned"), ("sprk_reason", "why"));
        var asked = new List<QueryExpression>();
        var inner = Inner(asked,
            new EntityCollection(new List<Entity> { Row(table, replaced, ("sprk_name", "real")), Row(table, kept) }) { MoreRecords = true, PagingCookie = "c" },
            new EntityCollection(new List<Entity> { Row(table, deleted), Row(table, replaced, ("sprk_name", "real")) }));
        var overlay = new ProcedureRowOverlay(inner.Object, PlanOf(new[] { image }, deleted: new[] { new EntityReference(table, deleted) }), Stamp);
        var query = new QueryExpression(table) { ColumnSet = new ColumnSet("sprk_name", "createdon") };

        var first = await overlay.RetrieveMultipleAsync(query, Ct);
        var last = await overlay.RetrieveMultipleAsync(query, Ct);

        first.Entities.Select(e => e.Id).Should().Equal(kept);
        first.MoreRecords.Should().BeTrue();
        last.Entities.Select(e => e.Id).Should().Equal(replaced);
        var returned = last.Entities.Single();
        returned.Attributes.Keys.Should().BeEquivalentTo("sprk_name", "createdon");
        returned["sprk_name"].Should().Be("planned");
        returned["createdon"].Should().Be(Stamp, "a created row carries the request's one stamp");
        returned["sprk_name"] = "changed by a reader";
        inner.Setup(e => e.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>())).ReturnsAsync(new EntityCollection());
        (await overlay.RetrieveMultipleAsync(query, Ct)).Entities.Single()["sprk_name"].Should().Be("planned", "images are copied out");
    }

    [Fact(DisplayName = "084d-1 F4: created images get statecode 0 and the one stamp; a real row's image is left as read")]
    public async Task CreatedImages_AreStamped_AndRealImagesAreLeftAsRead()
    {
        var (created, real) = (Guid.NewGuid(), Guid.NewGuid());
        var ruleSet = Guid.NewGuid();
        var asked = new List<QueryExpression>();
        var inner = Inner(asked, new EntityCollection());
        var overlay = new ProcedureRowOverlay(inner.Object, PlanOf(
            new[] { Row("sprk_policy", created, ("sprk_ruleset", new EntityReference("sprk_ruleset", ruleSet))) },
            new[] { Row("sprk_policy", real, ("sprk_ruleset", new EntityReference("sprk_ruleset", ruleSet)), ("sprk_enabled", false)) }), Stamp);
        var query = new QueryExpression("sprk_policy") { ColumnSet = new ColumnSet(true) };
        query.Criteria.AddCondition("sprk_ruleset", ConditionOperator.In, ruleSet);

        var rows = (await overlay.RetrieveMultipleAsync(query, Ct)).Entities.ToDictionary(e => e.Id);

        rows[created].GetAttributeValue<OptionSetValue>("statecode").Value.Should().Be(0);
        rows[created]["createdon"].Should().Be(Stamp);
        rows[real].Contains("createdon").Should().BeFalse();
        rows[real].Contains("statecode").Should().BeFalse();
    }

    [Fact(DisplayName = "084d-1 F4: the chunk rule appends a policy or version image only to the chunk that holds its parent; a primary-key read gets only its own image")]
    public async Task TheChunkRule_PlacesEachImageInOneChunk()
    {
        var (setA, setB) = (Guid.NewGuid(), Guid.NewGuid());
        var (policy, version, assignment) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var asked = new List<QueryExpression>();
        var inner = new Mock<IGenericEntityService>(MockBehavior.Strict);
        inner.Setup(e => e.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>())).ReturnsAsync(new EntityCollection());
        var overlay = new ProcedureRowOverlay(inner.Object, PlanOf(new[]
        {
            Row("sprk_policy", policy, ("sprk_ruleset", new EntityReference("sprk_ruleset", setB))),
            Row("sprk_policyversion", version, ("sprk_policy", new EntityReference("sprk_policy", policy))),
            Row(ProcedureAssignmentEntity.Entity, assignment),
        }), Stamp);

        async Task<IReadOnlyList<Guid>> Ask(string table, string column, params Guid[] values)
        {
            var query = new QueryExpression(table) { ColumnSet = new ColumnSet(true) };
            query.Criteria.AddCondition(column, ConditionOperator.In, values.Cast<object>().ToArray());
            return (await overlay.RetrieveMultipleAsync(query, Ct)).Entities.Select(e => e.Id).ToList();
        }

        (await Ask("sprk_policy", "sprk_ruleset", setA)).Should().BeEmpty("the image's set is in another chunk");
        (await Ask("sprk_policy", "sprk_ruleset", setB)).Should().Equal(policy);
        (await Ask("sprk_policyversion", "sprk_policy", Guid.NewGuid())).Should().BeEmpty();
        (await Ask("sprk_policyversion", "sprk_policy", policy)).Should().Equal(version);
        (await Ask("sprk_policy", "sprk_policyid", policy)).Should().Equal(policy);
        (await Ask("sprk_policy", "sprk_policyid", Guid.NewGuid())).Should().BeEmpty();
        (await Ask(ProcedureAssignmentEntity.Entity, "sprk_procedureassignmentid", Guid.NewGuid())).Should().BeEmpty();
        (await Ask(ProcedureAssignmentEntity.Entity, "sprk_matter", MatterId)).Should().Equal(new[] { assignment },
            "an assignment query without a key condition gets every image; the resolver re-checks each row");
    }

    public static TheoryData<string> UnplaceableQueries() => new() { "policy-no-chunk", "version-no-chunk", "policy-two-chunks", "policy-or-filter" };

    [Theory(DisplayName = "084d-1 F4: a policy or version query the chunk rule cannot place throws before it reads")]
    [MemberData(nameof(UnplaceableQueries))]
    public async Task AnUnplaceableQuery_Throws_BeforeReading(string shape)
    {
        var inner = new Mock<IGenericEntityService>(MockBehavior.Strict);
        var overlay = new ProcedureRowOverlay(inner.Object, PlanOf(new[]
        {
            Row("sprk_policy", Guid.NewGuid(), ("sprk_ruleset", new EntityReference("sprk_ruleset", Guid.NewGuid()))),
            Row("sprk_policyversion", Guid.NewGuid(), ("sprk_policy", new EntityReference("sprk_policy", Guid.NewGuid()))),
        }), Stamp);
        var query = new QueryExpression(shape.StartsWith("version", StringComparison.Ordinal) ? "sprk_policyversion" : "sprk_policy");
        switch (shape)
        {
            case "policy-two-chunks":
                query.Criteria.AddCondition("sprk_ruleset", ConditionOperator.In, Guid.NewGuid());
                query.Criteria.AddCondition("sprk_ruleset", ConditionOperator.In, Guid.NewGuid());
                break;
            case "policy-or-filter":
                query.Criteria.FilterOperator = LogicalOperator.Or;
                query.Criteria.AddCondition("sprk_ruleset", ConditionOperator.In, Guid.NewGuid());
                break;
            default:
                query.Criteria.AddCondition("statecode", ConditionOperator.Equal, 0);
                break;
        }

        await FluentActions.Invoking(() => overlay.RetrieveMultipleAsync(query, Ct)).Should().ThrowAsync<InvalidOperationException>();
        inner.Invocations.Should().BeEmpty("the overlay refuses before it reads");
    }

    [Fact(DisplayName = "084d-2a K-3: a key read is answered from the images only when the key is the sole condition; with other conditions beside a key that names an image it throws before it reads; a key that names no image reads on")]
    public async Task AKeyReadWithOtherConditions_ThrowsWhenTheKeyNamesAnImage()
    {
        var (policy, elsewhere, set) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var asked = new List<QueryExpression>();
        var inner = Inner(asked, new EntityCollection(), new EntityCollection());
        var overlay = new ProcedureRowOverlay(inner.Object, PlanOf(new[]
        {
            Row("sprk_policy", policy, ("sprk_ruleset", new EntityReference("sprk_ruleset", set)), ("sprk_enabled", false)),
        }), Stamp);

        QueryExpression Keyed(Guid key, bool withOther)
        {
            var query = new QueryExpression("sprk_policy") { ColumnSet = new ColumnSet(true) };
            query.Criteria.AddCondition("sprk_policyid", ConditionOperator.Equal, key);
            if (withOther)
            {
                query.Criteria.AddCondition("sprk_enabled", ConditionOperator.Equal, true);
            }

            return query;
        }

        (await overlay.RetrieveMultipleAsync(Keyed(policy, withOther: false), Ct)).Entities.Select(e => e.Id).Should().Equal(new[] { policy }, "the key is the sole condition");
        asked.Should().HaveCount(1);

        await FluentActions.Invoking(() => overlay.RetrieveMultipleAsync(Keyed(policy, withOther: true), Ct)).Should().ThrowAsync<InvalidOperationException>(
            "the image is switched off and the query asks for enabled rows: the overlay cannot judge that, so it must not return the image");
        asked.Should().HaveCount(1, "refused before it read");

        (await overlay.RetrieveMultipleAsync(Keyed(elsewhere, withOther: true), Ct)).Entities.Should().BeEmpty("no image is named, so nothing is returned unjudged");
        asked.Should().HaveCount(2);
    }

    [Fact(DisplayName = "084d-2a K-3: a key named inside an Or or a nested filter cannot be placed, so the query throws before it reads")]
    public async Task AKeyInsideAnOrOrANestedFilter_Throws()
    {
        var (policy, set) = (Guid.NewGuid(), Guid.NewGuid());
        var inner = new Mock<IGenericEntityService>(MockBehavior.Strict);
        var overlay = new ProcedureRowOverlay(inner.Object, PlanOf(new[]
        {
            Row("sprk_policy", policy, ("sprk_ruleset", new EntityReference("sprk_ruleset", set))),
        }), Stamp);

        var nested = new QueryExpression("sprk_policy");
        nested.Criteria.AddCondition("sprk_ruleset", ConditionOperator.In, set);
        var either = nested.Criteria.AddFilter(LogicalOperator.Or);
        either.AddCondition("sprk_policyid", ConditionOperator.Equal, policy);
        either.AddCondition("sprk_enabled", ConditionOperator.Equal, true);

        await FluentActions.Invoking(() => overlay.RetrieveMultipleAsync(nested, Ct)).Should().ThrowAsync<InvalidOperationException>();
        inner.Invocations.Should().BeEmpty("the overlay refuses before it reads");
    }

    public static TheoryData<string> LinkedQueries() => new()
    {
        "other-table-links-policy", "link-name-in-another-case", "nested-link-to-version", "query-of-the-four-links-anything",
    };

    [Theory(DisplayName = "084d-2a K-4: a query that joins one of the four tables (any depth, any case), or joins anything from one of them, throws before it reads")]
    [MemberData(nameof(LinkedQueries))]
    public async Task AQueryThatLinksAnOverrideTable_Throws_BeforeReading(string shape)
    {
        var inner = new Mock<IGenericEntityService>(MockBehavior.Strict);
        var overlay = new ProcedureRowOverlay(inner.Object, PlanOf(new[]
        {
            Row("sprk_policy", Guid.NewGuid(), ("sprk_ruleset", new EntityReference("sprk_ruleset", Guid.NewGuid()))),
        }), Stamp);
        QueryExpression query;
        switch (shape)
        {
            case "other-table-links-policy":
                query = new QueryExpression("sprk_signal");
                query.AddLink("sprk_policy", "sprk_policy", "sprk_policyid");
                break;
            case "link-name-in-another-case":
                query = new QueryExpression("sprk_signal");
                query.AddLink("SPRK_RuleSet", "sprk_ruleset", "sprk_rulesetid");
                break;
            case "nested-link-to-version":
                query = new QueryExpression("sprk_signal");
                query.AddLink("sprk_matter", "sprk_matter", "sprk_matterid").AddLink("sprk_policyversion", "a", "b");
                break;
            default:
                query = new QueryExpression("sprk_policy");
                query.Criteria.AddCondition("sprk_ruleset", ConditionOperator.In, Guid.NewGuid());
                query.AddLink("sprk_ruleset", "sprk_ruleset", "sprk_rulesetid");
                break;
        }

        await FluentActions.Invoking(() => overlay.RetrieveMultipleAsync(query, Ct)).Should().ThrowAsync<InvalidOperationException>();
        inner.Invocations.Should().BeEmpty("the overlay refuses before it reads");
    }

    [Fact(DisplayName = "084d-2a K-4 (must-not-fire): a query of another table that links only other tables passes through untouched")]
    public async Task AQueryThatLinksOtherTables_PassesThrough()
    {
        var asked = new List<QueryExpression>();
        var answer = new EntityCollection(new List<Entity> { Row("sprk_signal", Guid.NewGuid()) });
        var inner = Inner(asked, answer);
        var overlay = new ProcedureRowOverlay(inner.Object, EmptyPlan, Stamp);
        var query = new QueryExpression("sprk_signal");
        query.AddLink("sprk_matter", "sprk_matter", "sprk_matterid");

        (await overlay.RetrieveMultipleAsync(query, Ct)).Should().BeSameAs(answer);
        asked.Should().ContainSingle();
    }

    public static TheoryData<string> OtherReadsOfTheFourTables() => new()
    {
        "retrieve-policy", "retrieve-assignment", "alternate-key-policy", "fetch-entity", "fetch-link-entity", "fetch-malformed",
    };

    [Theory(DisplayName = "084d-1 F4: a by-id, alternate-key or FetchXML read of the four tables throws rather than passing real rows through")]
    [MemberData(nameof(OtherReadsOfTheFourTables))]
    public async Task OtherReadsOfTheFourTables_Throw(string read)
    {
        var inner = new Mock<IGenericEntityService>(MockBehavior.Strict);
        var overlay = new ProcedureRowOverlay(inner.Object, EmptyPlan, Stamp);
        Func<Task> call = read switch
        {
            "retrieve-policy" => () => overlay.RetrieveAsync("sprk_policy", Guid.NewGuid(), new[] { "sprk_name" }, Ct),
            "retrieve-assignment" => () => overlay.RetrieveAsync(ProcedureAssignmentEntity.Entity, Guid.NewGuid(), new[] { "sprk_name" }, Ct),
            "alternate-key-policy" => () => overlay.RetrieveByAlternateKeyAsync("sprk_policy", new KeyAttributeCollection { { "sprk_policycode", "BRR-1" } }, null, Ct),
            "fetch-entity" => () => overlay.RetrieveMultipleAsync(new FetchExpression("<fetch><entity name='sprk_policyversion'/></fetch>"), Ct),
            "fetch-link-entity" => () => overlay.RetrieveMultipleAsync(
                new FetchExpression("<fetch><entity name='sprk_signal'><link-entity name='sprk_ruleset' from='a' to='b'/></entity></fetch>"), Ct),
            _ => () => overlay.RetrieveMultipleAsync(new FetchExpression("<fetch><entity name='sprk_signal'>"), Ct),
        };

        await call.Should().ThrowAsync<InvalidOperationException>();
        inner.Invocations.Should().BeEmpty();
    }

    [Fact(DisplayName = "084d-1 F4: reads of every other table pass through unchanged")]
    public async Task OtherTables_PassThrough()
    {
        var inner = new Mock<IGenericEntityService>(MockBehavior.Strict);
        var matter = new Entity(Matter, MatterId);
        var page = new EntityCollection(new List<Entity> { matter });
        inner.Setup(e => e.RetrieveAsync(Matter, MatterId, It.IsAny<string[]>(), Ct)).ReturnsAsync(matter);
        inner.Setup(e => e.RetrieveMultipleAsync(It.Is<FetchExpression>(f => f.Query.Contains("sprk_signal")), Ct)).ReturnsAsync(page);
        inner.Setup(e => e.RetrieveMultipleAsync(It.Is<QueryExpression>(q => q.EntityName == Matter), Ct)).ReturnsAsync(page);
        var overlay = new ProcedureRowOverlay(inner.Object, PlanOf(new[] { Row(ProcedureAssignmentEntity.Entity, Guid.NewGuid()) }), Stamp);

        (await overlay.RetrieveAsync(Matter, MatterId, new[] { "sprk_mattername" }, Ct)).Should().BeSameAs(matter);
        (await overlay.RetrieveMultipleAsync(new FetchExpression("<fetch><entity name='sprk_signal'/></fetch>"), Ct)).Should().BeSameAs(page);
        (await overlay.RetrieveMultipleAsync(new QueryExpression(Matter), Ct)).Should().BeSameAs(page);
    }

    public static TheoryData<string> BadPlans() => new() { "other-table-image", "other-table-delete", "created-without-image", "repeated-image", "local-stamp" };

    [Theory(DisplayName = "084d-1 F4: the overlay refuses a plan outside the four tables, a created row without an image, a repeated image and a non-UTC stamp")]
    [MemberData(nameof(BadPlans))]
    public void ABadPlan_IsRefused(string bad)
    {
        var inner = new Mock<IGenericEntityService>(MockBehavior.Strict).Object;
        var policy = Row("sprk_policy", Guid.NewGuid());
        Action build = bad switch
        {
            "other-table-image" => () => _ = new ProcedureRowOverlay(inner, PlanOf(new[] { Row(ProcedureEntity.Entity, Guid.NewGuid()) }), Stamp),
            "other-table-delete" => () => _ = new ProcedureRowOverlay(inner, PlanOf(Array.Empty<Entity>(), deleted: new[] { new EntityReference(ProcedureEntity.VersionEntity, Guid.NewGuid()) }), Stamp),
            "created-without-image" => () => _ = new ProcedureRowOverlay(inner,
                new ProcedureOverridePlan("Test", "Test", Array.Empty<Entity>(), new[] { policy.ToEntityReference() }, Array.Empty<EntityReference>()), Stamp),
            "repeated-image" => () => _ = new ProcedureRowOverlay(inner, PlanOf(new[] { policy }, new[] { Row("sprk_policy", policy.Id) }), Stamp),
            _ => () => _ = new ProcedureRowOverlay(inner, EmptyPlan, new DateTime(2026, 11, 5, 14, 31, 0, DateTimeKind.Local)),
        };

        build.Should().Throw<ArgumentException>();
    }

    [Fact(DisplayName = "084d-1 F4: through the REAL resolver, past one chunk of rule sets and of policies, each image is read exactly once and a replaced real policy is gone")]
    public async Task ThroughTheRealResolver_EachImageIsReadOnce_AcrossChunks()
    {
        var rig = new RecordProcedureRig();
        var first = rig.Simple("RULE-0000");
        for (var i = 1; i <= 600; i++)
        {
            rig.Simple($"RULE-{i:D4}");
        }

        var second = rig.Simple("RULE-0601");
        var firstSet = rig.RuleSetOf(first.Procedure);
        var (newPolicy, newVersion, firstRuleVersion2) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var published = (RuleSetService.PublishStateColumn, (object)new OptionSetValue(RuleSetService.PublishStatePublished));
        var plan = PlanOf(
            created: new[]
            {
                // A new policy in the FIRST Procedure's set (the first chunk of 500 sets), its version, and a second version of the first rule.
                Row("sprk_policy", newPolicy, ("sprk_policycode", "NEW-RULE"), ("sprk_ruleset", new EntityReference("sprk_ruleset", firstSet)),
                    ("sprk_enabled", true), (RuleDomains.PolicyDomainColumn, new OptionSetValue(RuleDomains.PolicyDomainBilling))),
                Row("sprk_policyversion", newVersion, ("sprk_policy", new EntityReference("sprk_policy", newPolicy)), ("sprk_versionnumber", 1), published),
                Row("sprk_policyversion", firstRuleVersion2, ("sprk_policy", new EntityReference("sprk_policy", first.Rule)), ("sprk_versionnumber", 2), published),
            },
            updated: new[]
            {
                // The last rule switched off (a removal's switch-off of a real row): the resolver's own re-check drops it.
                Row("sprk_policy", second.Rule, ("sprk_policycode", "RULE-0601"), ("sprk_ruleset", new EntityReference("sprk_ruleset", rig.RuleSetOf(second.Procedure))),
                    ("sprk_enabled", false), ("statecode", new OptionSetValue(0)),
                    (RuleDomains.PolicyDomainColumn, new OptionSetValue(RuleDomains.PolicyDomainBilling))),
            });
        var overlay = new ProcedureRowOverlay(rig.Entities.Object, plan, Stamp);

        var rows = await ProcedureResolver.ReadRowsAsync(
            overlay, NullLogger.Instance, new CoreRecordReference(CoreRecordKind.Matter, RecordProcedureRig.M), RecordProcedureRig.F, Ct);

        rig.World.Queries.Count(q => q.EntityName == "sprk_policy").Should().BeGreaterThan(1, "the rule sets span more than one chunk");
        rig.World.Queries.Count(q => q.EntityName == "sprk_policyversion").Should().BeGreaterThan(1, "the policies span more than one chunk");
        rows.Policies.Count(p => p.Id == newPolicy).Should().Be(1);
        rows.Policies.Should().NotContain(p => p.Id == second.Rule, "a replaced row is dropped, and its Off image fails the resolver's own re-check");
        rows.Policies.Should().HaveCount(602, "601 real rules plus the new one, less the one switched off");
        rows.PolicyVersions.Count(v => v.PolicyVersionId == newVersion).Should().Be(1);
        rows.PolicyVersions.Count(v => v.PolicyVersionId == firstRuleVersion2).Should().Be(1);
        rows.PolicyVersions.Single(v => v.PolicyVersionId == newVersion).CreatedOn.Should().Be(Stamp);
        rig.NoWrites();
    }
}
