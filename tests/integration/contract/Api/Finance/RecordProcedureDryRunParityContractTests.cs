using System.Text.Json.Nodes;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Finance;
using Sprk.Bff.Api.Services.Finance.Procedures;
using Sprk.Bff.Api.Services.Finance.Resolution;
using Sprk.Bff.Api.Services.Signals;
using Sprk.Bff.Api.Services.Signals.Billing;
using Xunit;
using static Sprk.Bff.Api.Tests.Api.Finance.RecordProcedureRig;
using static Sprk.Bff.Api.Tests.Api.Finance.RecordProcedureWriteRig;

namespace Sprk.Bff.Api.Tests.Api.Finance;

/// <summary>
/// ebilling-r1 task 084d-3 (design notes/084d-dry-run-design.md §5-§6; ontology-r1 review, parity conditions (a)-(c)): the dry run's After equals
/// the read model produced by REALLY writing the same change and reading again, for every override kind, seed, core record type and date. Both
/// sides run through the REAL <see cref="RecordProcedureReadService"/> and resolver; the write is the REAL <see cref="ProcedureAssignmentService"/>
/// over <see cref="RecordProcedureWriteRig"/>'s transaction interpreter. Also: a dry run writes nothing over a strict rig, and concurrent dry runs
/// (and a dry run beside a real write) keep their overlay per request.
/// </summary>
/// <remarks>
/// <para>Maintain-class (ADR-038, KEEP contract path as 084's deviation 7): the parity is the contract the Impact step rests on ("if saved now").</para>
/// <para><b>Normalisation (the only one).</b> A proposed row (an id in <c>proposal.proposedRowIds</c>) has no creator in After (ontology-r1 F3),
/// while the written row carries the writer's creator person. Its <c>stamp.byId</c> and <c>stamp.byName</c> are blanked on both sides; its
/// <c>stamp.on</c> is compared as is (the dry run's one stamp and the write's <c>createdon</c> come from the same instant). Nothing else is
/// normalised: the views' <c>permissions</c> are set by the route, so at the service both sides carry none, and they are compared.</para>
/// </remarks>
public sealed class RecordProcedureDryRunParityContractTests
{
    private const string Token = "caller-token";
    private static readonly CancellationToken Ct = CancellationToken.None;

    private static readonly (string Table, Guid Id, CoreRecordKind Kind)[] Records =
    {
        (Matter, M, CoreRecordKind.Matter), (Project, P, CoreRecordKind.Project), (WorkAssignment, W, CoreRecordKind.WorkAssignment),
    };

    /// <summary>Every override kind the UI writes: the two Procedure assignments, the Procedure exclude, the rule exclude, the change, the re-change, the switch, the plain remove and the remove of a changed rule.</summary>
    public static readonly string[] Kinds =
    {
        "assign-follow", "assign-pin", "exclude-procedure", "exclude-rule", "change", "re-change", "switch", "remove", "remove-changed",
    };

    /// <summary>plain: the kind's own rows; chain: other overrides on the chain, a firm-scoped rule exclude and a legacy rule; large: conditions (a) and (b).</summary>
    public static readonly string[] Seeds = { "plain", "chain", "large" };

    /// <summary>Before the change's in-force date (the dry run's instant, Feb 10) and after it.</summary>
    private static readonly DateOnly[] Dates = { Feb1, Mar15 };

    /// <summary>Filler Procedures of the large seed (parity condition (b)): more than one 500-id chunk of rule sets and of policies.</summary>
    private const int Fillers = 510;

    public static TheoryData<string, string, string, string> Matrix()
    {
        var data = new TheoryData<string, string, string, string>();
        foreach (var kind in Kinds)
            foreach (var seed in Seeds)
                foreach (var record in Records)
                    foreach (var date in Dates)
                        data.Add(kind, seed, record.Table, date.ToString("yyyy-MM-dd"));
        return data;
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Seeds and proposals
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>A proposed override: the dry run's request, and the same change as the write makes it (the wizards' two calls for a replacement).</summary>
    private sealed record Proposal(ProcedureDryRunRequest Request, Func<ProcedureAssignmentService, Task> Write, Guid? RecordRule = null, Guid? RecordSet = null);

    private static Guid IdOf(string table) => Records.Single(r => r.Table == table).Id;

    private static async Task<T> Done<T>(Task<RuleSetOutcome<T>> call) where T : class
    {
        var outcome = await call;
        outcome.Kind.Should().Be(RuleSetOutcomeKind.Done, $"{outcome.StatusCode} {outcome.ErrorCode} {outcome.Detail}");
        return outcome.Value!;
    }

    private static RuleChangeRequest Change(Guid policy, string headline) =>
        new(policy, "Agreed for this record", null, null, "The billed rate differs from the rate agreed for this record.", headline, "Flag");

    private static void Seed(RecordProcedureWriteRig w, string seed)
    {
        var rig = w.Rig;
        switch (seed)
        {
            case "plain":
                break;
            case "chain":
                var other = rig.Simple("CHAIN-OTHER");
                rig.Assign(Matter, M, other.Procedure, ProcedureAssignmentEntity.ModeExcluded, reason: "Not on the matter", by: Reviewer);
                var ruled = rig.Simple("CHAIN-RULE");
                rig.ExcludeRule(Project, P, ruled.Rule, firm: G, reason: "Firm G only", by: Reviewer);
                var pinned = rig.Simple("CHAIN-PIN", ProcedureAppliesTo.AssignedRecordsOnly);
                rig.Assign(Matter, M, pinned.Procedure, ProcedureAssignmentEntity.ModePinned, pinned: pinned.Version, by: Reviewer);
                rig.AddLegacyRule("LEGACY-CHAIN", RuleSetScope.ClientDefault);
                break;
            case "large":
                // (a) the rig's page: one row per page on assignments and versions, 50 on sets and policies, so every read of the four tables
                // spans pages and the overlay's last-page append runs after earlier pages.
                w.PageSizes[ProcedureAssignmentEntity.Entity] = 1;
                w.PageSizes[PolicyVersion] = 1;
                w.PageSizes[RuleSet] = 50;
                w.PageSizes[Policy] = 50;
                // Three rules with a version each, so a version chunk always spans pages.
                var paged = rig.AddProcedure("Paged", ProcedureAppliesTo.AllRecords);
                var pagedVersion = rig.AddVersion(paged, 1, Jan1, null);
                for (var n = 1; n <= 3; n++)
                {
                    rig.AddRuleVersion(rig.AddProcedureRule(paged, $"PAGED-{n}"), pagedVersion, Jan1);
                }
                // (b) filler Procedures applying to every record, with ids that sort after every derived id: the record's set and its record
                // rule sit in the FIRST chunk of 500, and later chunks exist.
                for (var n = 1; n <= Fillers; n++)
                {
                    var procedure = Filler(0xf1, n);
                    var set = Filler(0xf2, n);
                    rig.World.Add(ProcedureEntity.Entity, procedure,
                        (ProcedureEntity.AreaColumn, Option(RuleDomains.PolicyDomainBilling)), (ProcedureEntity.StatusColumn, Option(ProcedureEntity.StatusActive)),
                        (ProcedureEntity.NameColumn, $"Filler {n}"), (ProcedureEntity.AppliesToColumn, Option((int)ProcedureAppliesTo.AllRecords)));
                    rig.World.Add(ProcedureEntity.VersionEntity, Filler(0xf3, n),
                        (ProcedureEntity.VersionProcedureColumn, Ref(ProcedureEntity.Entity, procedure)), (ProcedureEntity.VersionNumberColumn, 1),
                        (ProcedureEntity.VersionStatusColumn, Option(ProcedureEntity.VersionStatusActive)),
                        (ProcedureEntity.VersionPublishedOnColumn, new DateTime(2026, 1, 1, 8, 0, 0, DateTimeKind.Utc)),
                        (ProcedureEntity.VersionEffectiveFromColumn, Jan1.ToDateTime(TimeOnly.MinValue)));
                    rig.World.Add(RuleSet, set,
                        ("sprk_procedure", Ref(ProcedureEntity.Entity, procedure)), (RuleDomains.PolicyDomainColumn, Option(RuleDomains.PolicyDomainBilling)),
                        ("statecode", Option(0)));
                    // An enabled rule with no version: it is read (it fills the chunks) and runs nowhere.
                    rig.World.Add(Policy, Filler(0xf4, n),
                        ("sprk_policycode", $"FILLER-{n}"), ("sprk_ruleset", Ref(RuleSet, set)), ("sprk_enabled", true), ("statecode", Option(0)),
                        (RuleDomains.PolicyDomainColumn, Option(RuleDomains.PolicyDomainBilling)));
                }

                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(seed));
        }
    }

    private static Guid Filler(int table, int n) => new($"fffffff{table & 0xf:x}-0000-4000-8000-{n:x12}");

    /// <summary>The kind's own rows, and its proposal. Existing overrides a kind needs (switch, re-change, the removals) are written by the REAL writer at <see cref="SeedInstant"/>.</summary>
    private static async Task<Proposal> Arrange(RecordProcedureWriteRig w, string kind, string table)
    {
        var rig = w.Rig;
        var record = IdOf(table);
        w.Clock.SetUtcNow(SeedInstant);
        try
        {
            switch (kind)
            {
                case "assign-follow":
                    {
                        var s = rig.Simple("ASSIGN", ProcedureAppliesTo.AssignedRecordsOnly);
                        var body = new ProcedureAssignmentRequest(s.Procedure, "FollowLatest", null, null);
                        return new(new(ProcedureDryRunRequest.KindAssign, Assign: body),
                            p => Done(p.AssignProcedureAsync(table, record, body, CallerObjectId, Ct)));
                    }

                case "assign-pin":
                    {
                        var procedure = rig.AddProcedure("Pinnable", ProcedureAppliesTo.AllRecords);
                        var v1 = rig.AddVersion(procedure, 1, Jan1, Mar1, ProcedureEntity.VersionStatusSuperseded);
                        var v2 = rig.AddVersion(procedure, 2, Mar1, null);
                        var rule = rig.AddProcedureRule(procedure, "PINNED");
                        rig.AddRuleVersion(rule, v1, Jan1);
                        rig.AddRuleVersion(rule, v2, Mar1, number: 2, headline: "Approved rate, version 2");
                        var body = new ProcedureAssignmentRequest(procedure, "Pinned", v1, "Grandfathered");
                        return new(new(ProcedureDryRunRequest.KindAssign, Assign: body),
                            p => Done(p.AssignProcedureAsync(table, record, body, CallerObjectId, Ct)));
                    }

                case "exclude-procedure":
                    {
                        var s = rig.Simple("EXCL-P");
                        var body = new ProcedureAssignmentRequest(s.Procedure, "Excluded", null, "Not for this record");
                        return new(new(ProcedureDryRunRequest.KindAssign, Assign: body),
                            p => Done(p.AssignProcedureAsync(table, record, body, CallerObjectId, Ct)));
                    }

                case "exclude-rule":
                    {
                        var s = rig.Simple("EXCL-R");
                        var body = new RuleExcludeRequest(s.Rule, "Agreed for this record", null);
                        return new(new(ProcedureDryRunRequest.KindExcludeRule, ExcludeRule: body),
                            p => Done(p.ExcludeRuleAsync(table, record, body, CallerObjectId, Ct)));
                    }

                case "change":
                    {
                        var c = rig.Changeable("CHG");
                        var body = Change(c.Rule, "Rate agreed for this record");
                        var set = ProcedureAssignmentService.RecordRuleSetIdFor(table, record, null);
                        return new(new(ProcedureDryRunRequest.KindChangeRule, ChangeRule: body),
                            p => Done(p.ChangeRuleAsync(table, record, body, CallerObjectId, Token, Ct)),
                            ProcedureAssignmentService.RecordRulePolicyIdFor(set, c.Rule), set);
                    }

                case "re-change":
                    {
                        var c = rig.Changeable("RECHG");
                        var first = await Done(w.Writer.ChangeRuleAsync(table, record, Change(c.Rule, "First agreed rate"), CallerObjectId, Token, Ct));
                        var x = first.Override.Id;
                        var body = Change(c.Rule, "Second agreed rate");
                        return new(new(ProcedureDryRunRequest.KindChangeRule, ReplacesOverrideId: x, ChangeRule: body),
                            async p =>
                            {
                                await Done(p.RemoveAsync(table, record, x, Token, Ct));
                                await Done(p.ChangeRuleAsync(table, record, body, CallerObjectId, Token, Ct));
                            },
                            first.RecordRule.PolicyId, first.RecordRule.RuleSetId);
                    }

                case "switch":
                    {
                        var s = rig.Simple("SWITCH", ProcedureAppliesTo.AssignedRecordsOnly);
                        var x = (await Done(w.Writer.AssignProcedureAsync(table, record, new(s.Procedure, "FollowLatest", null, null), CallerObjectId, Ct))).Id;
                        var body = new ProcedureAssignmentRequest(s.Procedure, "Pinned", s.Version, null);
                        return new(new(ProcedureDryRunRequest.KindAssign, ReplacesOverrideId: x, Assign: body),
                            async p =>
                            {
                                await Done(p.RemoveAsync(table, record, x, Token, Ct));
                                await Done(p.AssignProcedureAsync(table, record, body, CallerObjectId, Ct));
                            });
                    }

                case "remove":
                    {
                        var s = rig.Simple("REMOVE", ProcedureAppliesTo.AssignedRecordsOnly);
                        var x = (await Done(w.Writer.AssignProcedureAsync(table, record, new(s.Procedure, "FollowLatest", null, null), CallerObjectId, Ct))).Id;
                        return new(new(ProcedureDryRunRequest.KindRemove, OverrideId: x), p => Done(p.RemoveAsync(table, record, x, Token, Ct)));
                    }

                case "remove-changed":
                    {
                        var c = rig.Changeable("RMCHG");
                        var first = await Done(w.Writer.ChangeRuleAsync(table, record, Change(c.Rule, "Agreed rate"), CallerObjectId, Token, Ct));
                        var x = first.Override.Id;
                        return new(new(ProcedureDryRunRequest.KindRemove, OverrideId: x), p => Done(p.RemoveAsync(table, record, x, Token, Ct)),
                            first.RecordRule.PolicyId, first.RecordRule.RuleSetId);
                    }

                default:
                    throw new ArgumentOutOfRangeException(nameof(kind));
            }
        }
        finally
        {
            w.Clock.SetUtcNow(Instant);
        }
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Reading and comparing
    // ═══════════════════════════════════════════════════════════════════════════

    private sealed record Lists(string Procedures, string Rules);

    private static async Task<Lists> ReadListsAsync(RecordProcedureWriteRig w, string table, DateOnly date, IReadOnlyCollection<Guid> proposed)
    {
        var record = IdOf(table);
        var procedures = await w.Rig.Service.ListProceduresAsync(table, record, null, date, Token, Ct);
        var rules = await w.Rig.Service.ListRulesInForceAsync(table, record, null, date, Token, Ct);
        procedures.Status.Should().Be(RecordProcedureReadStatus.Done);
        rules.Status.Should().Be(RecordProcedureReadStatus.Done);
        return new(Normalised(procedures.Value!, proposed), Normalised(rules.Value!, proposed));
    }

    private static async Task<ProcedureDryRunView> DryRunAsync(RecordProcedureWriteRig w, string table, ProcedureDryRunRequest request, DateOnly date)
    {
        var result = await w.Rig.Service.DryRunAsync(w.Planner, table, IdOf(table), request, null, date, Token, Ct);
        result.Status.Should().Be(RecordProcedureReadStatus.Done, result.Refusal is { } r ? $"{r.StatusCode} {r.ErrorCode} {r.Detail}" : "the dry run answers");
        return result.Value!;
    }

    private static Lists SideOf(ProcedureDryRunSideView side, IReadOnlyCollection<Guid> proposed) =>
        new(Normalised(side.Procedures, proposed), Normalised(side.Rules, proposed));

    /// <summary>The one normalisation: a proposed row's creator (<c>stamp.byId</c>, <c>stamp.byName</c>) is blanked. <c>stamp.on</c> is kept.</summary>
    private static string Normalised(object view, IReadOnlyCollection<Guid> proposed)
    {
        var node = JsonNode.Parse(JsonOf(view))!;
        Blank(node, proposed);
        return node.ToJsonString();
    }

    private static void Blank(JsonNode? node, IReadOnlyCollection<Guid> proposed)
    {
        switch (node)
        {
            case JsonObject o:
                var rowId = (o["rowId"] ?? o["overrideRowId"]) is JsonValue v && v.TryGetValue<Guid>(out var id) ? id : (Guid?)null;
                if (rowId is { } row && proposed.Contains(row) && o["stamp"] is JsonObject stamp)
                {
                    stamp["byId"] = null;
                    stamp["byName"] = null;
                }

                foreach (var (_, child) in o.ToList())
                {
                    Blank(child, proposed);
                }

                break;
            case JsonArray a:
                foreach (var child in a)
                {
                    Blank(child, proposed);
                }

                break;
        }
    }

    /// <summary>Parity condition (c): every <c>createdon</c> seeded in the four tables is earlier than the clock of the dry run and the write.</summary>
    private static void EverySeededCreatedOn_IsBeforeTheClock(RecordProcedureWriteRig w)
    {
        var stamps = OverrideTables.SelectMany(w.RowsOf).Select(r => r.GetAttributeValue<DateTime?>("createdon")).OfType<DateTime>().ToList();
        stamps.Where(c => c >= Instant.UtcDateTime).Should().BeEmpty("parity condition (c): the clock is later than every seeded createdon");
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Parity
    // ═══════════════════════════════════════════════════════════════════════════

    [Theory(DisplayName = "084d-3 parity: the dry run's Now equals the read before the write, its After equals the read after REALLY writing the same change, and its proposed rows are the rows the write created")]
    [MemberData(nameof(Matrix))]
    public async Task DryRun_EqualsTheReadAroundTheRealWrite(string kind, string seed, string table, string date)
    {
        var w = new RecordProcedureWriteRig();
        Seed(w, seed);
        var proposal = await Arrange(w, kind, table);
        var on = DateOnly.Parse(date, System.Globalization.CultureInfo.InvariantCulture);
        EverySeededCreatedOn_IsBeforeTheClock(w);
        var pagesBefore = w.Pages.Count;

        var dry = await DryRunAsync(w, table, proposal.Request, on);
        var dryPages = w.Pages.Skip(pagesBefore).ToList();
        var proposed = dry.Proposal.ProposedRowIds.ToList();
        var before = await ReadListsAsync(w, table, on, proposed);
        var transactions = w.Transactions.Count;
        await proposal.Write(w.Writer);
        var after = await ReadListsAsync(w, table, on, proposed);

        SideOf(dry.Now, proposed).Should().Be(before, "Now is the record's two lists as they read before the write");
        var created = w.Transactions.Skip(transactions).SelectMany(t => t).OfType<CreateRequest>().Select(c => c.Target.Id).ToList();
        proposed.Should().BeEquivalentTo(created, "proposedRowIds are exactly the rows the write created");
        var afterSide = SideOf(dry.After, proposed);
        afterSide.Procedures.Should().Be(after.Procedures, "After's Procedures list is the list read after the write");
        afterSide.Rules.Should().Be(after.Rules, "After's rules in force are the rules read after the write");
        dry.Now.Procedures.Permissions.Should().Be(RecordWritePermissions.None);
        dry.After.Rules.Permissions.Should().Be(RecordWritePermissions.None);

        // Not vacuous: the proposal changes the record's lists, and the one normalisation has something to blank (the written override row
        // carries the writer's creator person) whenever the write creates an override row that shows on the date (a change's row shows once the
        // record rule is in force, so on the later date).
        if (kind == "change" && on < DateOnly.FromDateTime(Instant.UtcDateTime))
        {
            afterSide.Should().Be(SideOf(dry.Now, proposed), "a first change is in force from the save, so on an earlier date the lists do not change");
        }
        else
        {
            afterSide.Should().NotBe(SideOf(dry.Now, proposed), "the proposal changes what the record's lists show");
        }

        if (kind is not ("remove" or "remove-changed") && on == Mar15)
        {
            (await ReadListsAsync(w, table, on, [])).Should().NotBe(after, "the written row's creator is what the normalisation blanks");
        }

        if (seed == "large")
        {
            LargeSeedConditionsHeld(dryPages, kind, proposal);
        }
    }

    /// <summary>
    /// Parity conditions (a) and (b), on the pages the dry run read: the four tables were read over several pages, and the parent of a policy
    /// or version after-image sits in a chunk that is not the last (a later chunk of the same query exists).
    /// </summary>
    private static void LargeSeedConditionsHeld(
        IReadOnlyList<(string Table, int Page, bool More, int Rows, IReadOnlyDictionary<string, HashSet<Guid>> Lists)> pages, string kind, Proposal proposal)
    {
        pages.Should().Contain(p => p.Table == Policy && p.Page > 1, "(a) the policies are read over several pages");
        pages.Should().Contain(p => p.Table == RuleSet && p.Page > 1, "(a) the rule sets are read over several pages");
        pages.Should().Contain(p => p.Table == PolicyVersion && p.Page > 1, "(a) the versions are read over several pages");
        if (proposal.RecordSet is { } set)
        {
            var chunks = pages.Where(p => p.Table == Policy && p.Lists.ContainsKey("sprk_ruleset")).Select(p => p.Lists["sprk_ruleset"]).ToList();
            chunks.Should().Contain(l => l.Contains(set), "the record rule's set is in a chunk of the policies read");
            chunks.Should().Contain(l => !l.Contains(set) && l.All(id => id.CompareTo(set) > 0),
                $"(b) {kind}: the policy image's parent (the record's set) is in a chunk before the last");
        }

        if (proposal.RecordRule is { } rule && kind != "remove-changed")
        {
            var chunks = pages.Where(p => p.Table == PolicyVersion && p.Lists.ContainsKey("sprk_policy")).Select(p => p.Lists["sprk_policy"]).ToList();
            chunks.Should().Contain(l => l.Contains(rule), "the record rule is in a chunk of the versions read");
            chunks.Should().Contain(l => !l.Contains(rule) && l.All(id => id.CompareTo(rule) > 0),
                $"(b) {kind}: the version image's parent (the record rule) is in a chunk before the last");
        }
    }

    public static TheoryData<string, string> AppendedOnceCases()
    {
        var data = new TheoryData<string, string>();
        foreach (var kind in new[] { "change", "change-over-an-off-rule", "remove-changed" })
            foreach (var record in Records)
                data.Add(kind, record.Table);
        return data;
    }

    [Theory(DisplayName = "084d-3 (b), ontology-r1 F4: over the large seed (several chunks, several pages), each policy and version after-image is appended exactly once to the resolver's reads, and its parent sits in a chunk before the last")]
    [MemberData(nameof(AppendedOnceCases))]
    public async Task EachPolicyAndVersionImage_IsAppendedExactlyOnce(string kind, string table)
    {
        var w = new RecordProcedureWriteRig();
        Seed(w, "large");
        var record = Records.Single(r => r.Table == table);
        var c = w.Rig.Changeable("ONCE");
        var reads = new WriteRefusingEntityService(w.Rig.Entities.Object);
        RuleSetOutcome<ProcedureOverridePlan> planned;
        if (kind == "change")
        {
            planned = await w.Planner.PlanChangeRuleAsync(reads, table, record.Id, Change(c.Rule, "Agreed"), Instant.UtcDateTime, Ct);
        }
        else
        {
            w.Clock.SetUtcNow(SeedInstant);
            var first = await Done(w.Writer.ChangeRuleAsync(table, record.Id, Change(c.Rule, "Agreed"), CallerObjectId, Token, Ct));
            w.Clock.SetUtcNow(Instant);
            if (kind == "remove-changed")
            {
                planned = await w.Planner.PlanRemoveAsync(reads, table, record.Id, first.Override.Id, Ct);
            }
            else
            {
                await Done(w.Writer.RemoveAsync(table, record.Id, first.Override.Id, Token, Ct)); // the record rule is now Off and real
                planned = await w.Planner.PlanChangeRuleAsync(reads, table, record.Id, Change(c.Rule, "Agreed again"), Instant.UtcDateTime, Ct);
            }
        }

        planned.Kind.Should().Be(RuleSetOutcomeKind.Done, planned.Detail);
        var plan = planned.Value!;
        var images = plan.AfterImages.Where(i => i.LogicalName is Policy or PolicyVersion).Select(i => (i.LogicalName, i.Id)).ToList();
        images.Should().NotBeEmpty();

        var overlay = new ProcedureRowOverlay(w.Rig.Entities.Object, plan, Instant.UtcDateTime);
        var counts = new Dictionary<(string, Guid), int>();
        var counting = new Mock<IGenericEntityService>(MockBehavior.Strict);
        counting.Setup(e => e.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()))
            .Returns(async (QueryExpression q, CancellationToken token) =>
            {
                var page = await overlay.RetrieveMultipleAsync(q, token);
                foreach (var row in page.Entities)
                {
                    counts[(q.EntityName, row.Id)] = counts.GetValueOrDefault((q.EntityName, row.Id)) + 1;
                }

                return page;
            });
        var pagesBefore = w.Pages.Count;

        await ProcedureResolver.ReadAsync(counting.Object, NullLogger.Instance, new CoreRecordReference(record.Kind, record.Id), F, Ct);

        foreach (var image in images)
        {
            counts.GetValueOrDefault(image).Should().Be(1, $"{image.LogicalName} {image.Id} is appended to exactly one page of one chunk");
        }

        var set = ProcedureAssignmentService.RecordRuleSetIdFor(table, record.Id, null);
        var rule = ProcedureAssignmentService.RecordRulePolicyIdFor(set, c.Rule);
        var proposal = new Proposal(new(null), _ => Task.CompletedTask, kind == "remove-changed" ? null : rule, set);
        LargeSeedConditionsHeld(w.Pages.Skip(pagesBefore).ToList(), kind, proposal);
        Fillers.Should().BeGreaterThan(BillingFactLoader.MaxInValues, "more ids than one chunk");
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // No write (design §6)
    // ═══════════════════════════════════════════════════════════════════════════

    public static TheoryData<string, string> EveryKindOnEveryRecord()
    {
        var data = new TheoryData<string, string>();
        foreach (var kind in Kinds)
            foreach (var record in Records)
                data.Add(kind, record.Table);
        return data;
    }

    [Theory(DisplayName = "084d-3 no-write: over a STRICT rig (every write member of the seam and the transaction throw; ownership and closure strict), every kind's dry run answers 200 and calls no write, asks no owner, closes nothing, and leaves the world unchanged")]
    [MemberData(nameof(EveryKindOnEveryRecord))]
    public async Task DryRun_OverAStrictRig_WritesNothing(string kind, string table)
    {
        var w = new RecordProcedureWriteRig();
        Seed(w, "chain");
        var proposal = await Arrange(w, kind, table);
        w.Strict();
        var snapshot = w.Snapshot();
        var writes = w.WriteInvocations().Count;
        var transactions = w.Transactions.Count;
        var owners = w.Ownership.Invocations.Count;
        var writerCloses = w.WriterClosure.Invocations.Count;

        await DryRunAsync(w, table, proposal.Request, Mar15);

        w.WriteInvocations().Should().HaveCount(writes, "the dry run called no write member of the seam");
        w.Transactions.Should().HaveCount(transactions, "the dry run sent no transaction");
        w.Snapshot().Should().Be(snapshot, "the world is unchanged");
        w.PlannerOwnership.Invocations.Should().BeEmpty("the dry run asks no owner (R5)");
        w.PlannerClosure.Invocations.Select(i => i.Method.Name)
            .Where(m => m != nameof(SignalClosureService.SelectOpenSignalsOfPolicyAsync)
                && m != nameof(SignalClosureService.SelectOpenSignalsOfPolicyOnSubjectsAsync))
            .Should().BeEmpty("the dry run selects, it never closes");
        w.Ownership.Invocations.Should().HaveCount(owners);
        w.WriterClosure.Invocations.Should().HaveCount(writerCloses);
    }

    [Fact(DisplayName = "084d-3 no-write must-fire: the strict rig refuses a real write (so a dry run that wrote could not pass), and the world is unchanged")]
    public async Task TheStrictRig_RefusesARealWrite()
    {
        var w = new RecordProcedureWriteRig();
        var s = w.Rig.Simple("STRICT", ProcedureAppliesTo.AssignedRecordsOnly);
        w.Strict();
        var snapshot = w.Snapshot();

        var outcome = await w.Writer.AssignProcedureAsync(Project, P, new(s.Procedure, "FollowLatest", null, null), CallerObjectId, Ct);

        outcome.Kind.Should().NotBe(RuleSetOutcomeKind.Done);
        w.WriteInvocations().Should().Contain(nameof(IGenericEntityService.ExecuteTransactionAsync));
        w.Snapshot().Should().Be(snapshot);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Concurrency (design §6): the overlay is per request
    // ═══════════════════════════════════════════════════════════════════════════

    private static readonly AsyncLocal<string?> Flow = new();

    private static string Whole(ProcedureDryRunView view) => JsonOf(view);

    [Theory(DisplayName = "084d-3 concurrency: two different dry runs on one record and a plain read, interleaved on one service, each equal their sequential answer; nothing leaks between overlays and the world is unchanged")]
    [InlineData(Matter)]
    [InlineData(Project)]
    [InlineData(WorkAssignment)]
    public async Task InterleavedDryRuns_EachEqualTheirSequentialAnswer(string table)
    {
        var w = new RecordProcedureWriteRig();
        Seed(w, "chain");
        var a = await Arrange(w, "assign-follow", table);
        var b = await Arrange(w, "exclude-rule", table);
        var c = await Arrange(w, "re-change", table);
        var refA = Whole(await DryRunAsync(w, table, a.Request, Mar15));
        var refB = Whole(await DryRunAsync(w, table, b.Request, Mar15));
        var refC = Whole(await DryRunAsync(w, table, c.Request, Mar15));
        var refRead = await ReadListsAsync(w, table, Mar15, []);
        refA.Should().NotBe(refB);
        var snapshot = w.Snapshot();
        var writes = w.WriteInvocations().Count;

        var trace = new List<string>();
        w.BeforeQuery = async _ =>
        {
            lock (trace)
            {
                trace.Add(Flow.Value ?? "?");
            }

            await Task.Yield();
        };

        async Task<string> Run(string tag, Proposal p)
        {
            Flow.Value = tag;
            await Task.Yield();
            return Whole(await DryRunAsync(w, table, p.Request, Mar15));
        }

        async Task<Lists> Read()
        {
            Flow.Value = "read";
            await Task.Yield();
            return await ReadListsAsync(w, table, Mar15, []);
        }

        for (var round = 0; round < 3; round++)
        {
            trace.Clear();
            var runs = new[] { Run("A", a), Run("B", b), Run("C", c), Run("A2", a), Run("B2", b) };
            var read = Read();
            var answers = await Task.WhenAll(runs);

            answers[0].Should().Be(refA);
            answers[1].Should().Be(refB);
            answers[2].Should().Be(refC);
            answers[3].Should().Be(refA);
            answers[4].Should().Be(refB);
            (await read).Should().Be(refRead, "a concurrent plain read equals Now");
            Interleaved(trace, "A", "B").Should().BeTrue("the premise: the dry runs really interleave between their reads");
        }

        w.Snapshot().Should().Be(snapshot, "the world is unchanged");
        w.WriteInvocations().Should().HaveCount(writes);
    }

    /// <summary>True when the trace switches between the two flows at least three times (A..B..A..B).</summary>
    private static bool Interleaved(IReadOnlyList<string> trace, string x, string y)
    {
        var switches = 0;
        string? last = null;
        foreach (var tag in trace.Where(t => t == x || t == y))
        {
            if (last is not null && tag != last) switches++;
            last = tag;
        }

        return switches >= 3;
    }

    [Theory(DisplayName = "084d-3 concurrency: a dry run with a REAL write landing between any two of its reads is self-consistent (Now and After are each the answer before or after the write, never a mix within a side), the write is unaffected by the overlay, and the proposal is never written")]
    [InlineData(Project)]
    [InlineData(WorkAssignment)]
    public async Task ADryRun_WithARealWriteBetweenItsReads_IsSelfConsistent(string table)
    {
        // References, sequentially, on rigs seeded alike (ids are a counter, so the rigs hold the same ids).
        async Task<(RecordProcedureWriteRig W, Proposal Dry, Proposal Write)> Fresh()
        {
            var fresh = new RecordProcedureWriteRig();
            Seed(fresh, "chain");
            var dry = await Arrange(fresh, "exclude-rule", table);
            var write = await Arrange(fresh, "assign-follow", table);
            return (fresh, dry, write);
        }

        var (r, rDry, rWrite) = await Fresh();
        var counted = 0;
        r.BeforeQuery = _ =>
        {
            Interlocked.Increment(ref counted);
            return Task.CompletedTask;
        };
        var pre = await DryRunAsync(r, table, rDry.Request, Mar15);
        var queries = counted;
        r.BeforeQuery = null;
        await rWrite.Write(r.Writer);
        var postRead = await ReadListsAsync(r, table, Mar15, []);
        var post = await DryRunAsync(r, table, rDry.Request, Mar15);
        var nowPre = JsonOf(pre.Now);
        var nowPost = JsonOf(post.Now);
        var afterPre = JsonOf(pre.After);
        var afterPost = JsonOf(post.After);
        nowPre.Should().NotBe(nowPost, "the write changes the record's lists");
        queries.Should().BeGreaterThan(10);

        var seen = new HashSet<(bool NowPost, bool AfterPost)>();
        for (var k = 1; k <= queries; k++)
        {
            var (w, dry, write) = await Fresh();
            var n = 0;
            w.BeforeQuery = async _ =>
            {
                if (Flow.Value != "D" || Interlocked.Increment(ref n) != k)
                {
                    return;
                }

                Flow.Value = "W";
                await write.Write(w.Writer);
            };

            Flow.Value = "D";
            var view = await DryRunAsync(w, table, dry.Request, Mar15);
            Flow.Value = null;

            var now = JsonOf(view.Now);
            var after = JsonOf(view.After);
            now.Should().BeOneOf(new[] { nowPre, nowPost }, $"write at query {k}: Now is the answer before or after the write");
            after.Should().BeOneOf(new[] { afterPre, afterPost }, $"write at query {k}: After is the answer before or after the write");
            seen.Add((now == nowPost, after == afterPost));
            (await ReadListsAsync(w, table, Mar15, [])).Should().Be(postRead, "the real write is unaffected by the dry run's overlay");
            w.RowsOf(ProcedureAssignmentEntity.Entity).Select(x => x.Id).Should().NotContain(view.Proposal.ProposedRowIds, "the proposal is never written");
        }

        seen.Should().Contain((false, true), "the premise: some write landed between the Now and After reads (R10: separate reads)");
        seen.Should().Contain((true, true));
    }
}
