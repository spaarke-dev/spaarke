using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Access;
using Sprk.Bff.Api.Services.Finance;
using Sprk.Bff.Api.Services.Finance.Procedures;
using Sprk.Bff.Api.Services.Finance.Resolution;
using Sprk.Bff.Api.Services.Finance.Submission;
using Sprk.Bff.Api.Services.Signals;
using Sprk.Bff.Api.Services.Signals.Billing;
using Sprk.Bff.Api.Tests.DataMutation.ExternalAccess;

namespace Sprk.Bff.Api.Tests.Services.Finance.Procedures;

/// <summary>
/// ebilling-r1 task 113: an in-memory Dataverse (<see cref="SecureChildShareWorld"/>, which evaluates each query) holding the rows the Procedure
/// resolver reads for an invoice on one matter, with the helpers the banner tests need: seed Procedures, versions, rules and override rows, build
/// the snapshot an invoice "was judged under" from the CURRENT rows (the production builder, <see cref="ProcedureSnapshot"/>), then change the rows
/// and ask the banner. Both banner test classes use it.
/// </summary>
internal sealed class ProcedureBannerWorld
{
    internal const string Matter = SecureRootInheritance.Matter;
    private const string RuleSet = "sprk_ruleset";
    private const string Policy = "sprk_policy";
    private const string PolicyVersion = "sprk_policyversion";

    /// <summary>The invoice's firm (F) and another firm (G).</summary>
    internal static readonly Guid F = Guid.Parse("11300000-0000-4000-8000-0000000000f1");

    internal static readonly Guid G = Guid.Parse("11300000-0000-4000-8000-0000000000f2");

    /// <summary>The invoice's matter, and an unrelated matter.</summary>
    internal static readonly Guid M = Guid.Parse("11300000-0000-4000-8000-0000000000a1");

    internal static readonly Guid OtherMatter = Guid.Parse("11300000-0000-4000-8000-0000000000a2");

    internal static readonly Guid InvoiceId = Guid.Parse("11300000-0000-4000-8000-0000000000b1");

    internal static readonly DateOnly Jan1 = new(2026, 1, 1);
    internal static readonly DateOnly Feb15 = new(2026, 2, 15);
    internal static readonly DateOnly Mar1 = new(2026, 3, 1);
    internal static readonly DateOnly Mar15 = new(2026, 3, 15);

    /// <summary>A sentinel the output of the banner must never carry: the name of a Procedure, the reason of an override.</summary>
    internal const string ProcedureNameSentinel = "SENTINEL-PROCEDURE-113 Outside Counsel Guidelines";

    internal const string ReasonSentinel = "SENTINEL-REASON-113 agreed with the partner";

    internal readonly SecureChildShareWorld World = SecureChildShareWorld.Standard();
    internal readonly Mock<IGenericEntityService> Entities;
    private readonly Dictionary<Guid, Guid> _setOf = new();
    private DateTime _created = new(2026, 1, 1, 8, 0, 0, DateTimeKind.Utc);

    internal ProcedureBannerWorld()
    {
        Entities = SecureChildShareWorld.EntitiesOver(() => World);
        World.Add(Matter, M, ("sprk_name", "Acme v Roe"));
        World.Add(Matter, OtherMatter, ("sprk_name", "Other matter"));
    }

    private static EntityReference Ref(string table, Guid id) => new(table, id);

    private static OptionSetValue Option(int value) => new(value);

    private static DateTime Stored(DateOnly date) => date.ToDateTime(TimeOnly.MinValue);

    private static DateTime Utc(DateOnly date) => date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);

    internal DateTime Tick() => _created = _created.AddMinutes(1);

    // ── Seeding (the shapes the resolver's own tests use) ──

    internal Guid AddProcedure(string name, ProcedureAppliesTo appliesTo, Guid? firm = null, int status = ProcedureEntity.StatusActive)
    {
        var id = Guid.NewGuid();
        var columns = new List<(string, object)>
        {
            (ProcedureEntity.AreaColumn, Option(RuleDomains.PolicyDomainBilling)),
            (ProcedureEntity.StatusColumn, Option(status)),
            ("statecode", Option(0)),
            (ProcedureEntity.NameColumn, name),
            (ProcedureEntity.AppliesToColumn, Option((int)appliesTo)),
        };
        if (firm is { } f) columns.Add((ProcedureEntity.FirmColumn, Ref("sprk_organization", f)));
        World.Add(ProcedureEntity.Entity, id, columns.ToArray());

        var set = Guid.NewGuid();
        World.Add(RuleSet, set,
            ("sprk_procedure", Ref(ProcedureEntity.Entity, id)), (RuleDomains.PolicyDomainColumn, Option(RuleDomains.PolicyDomainBilling)),
            ("statecode", Option(0)));
        _setOf[id] = set;
        return id;
    }

    internal Guid AddVersion(Guid procedure, int number, DateOnly? from, DateOnly? to, int status = ProcedureEntity.VersionStatusActive)
    {
        var id = Guid.NewGuid();
        var columns = new List<(string, object)>
        {
            (ProcedureEntity.VersionProcedureColumn, Ref(ProcedureEntity.Entity, procedure)),
            (ProcedureEntity.VersionNumberColumn, number),
            (ProcedureEntity.VersionStatusColumn, Option(status)),
        };
        if (status is ProcedureEntity.VersionStatusActive or ProcedureEntity.VersionStatusSuperseded)
            columns.Add((ProcedureEntity.VersionPublishedOnColumn, new DateTime(2026, 1, 1, 8, 0, 0, DateTimeKind.Utc)));
        if (from is { } f) columns.Add((ProcedureEntity.VersionEffectiveFromColumn, Stored(f)));
        if (to is { } t) columns.Add((ProcedureEntity.VersionEffectiveToColumn, Stored(t)));
        World.Add(ProcedureEntity.VersionEntity, id, columns.ToArray());
        return id;
    }

    internal Guid AddRule(Guid procedure, string code, Guid? id = null, Guid? set = null)
    {
        id ??= Guid.NewGuid();
        World.Add(Policy, id.Value,
            ("sprk_policycode", code), ("sprk_ruleset", Ref(RuleSet, set ?? _setOf[procedure])), ("sprk_enabled", true), ("statecode", Option(0)),
            (RuleDomains.PolicyDomainColumn, Option(RuleDomains.PolicyDomainBilling)));
        return id.Value;
    }

    internal Guid AddRuleVersion(Guid policy, Guid? procedureVersion, DateOnly from, Guid? basedOn = null, DateTime? created = null)
    {
        var id = Guid.NewGuid();
        var columns = new List<(string, object)>
        {
            ("sprk_policy", Ref(Policy, policy)),
            ("sprk_versionnumber", 1),
            ("sprk_ruletype", Option((int)RuleType.ApprovedRate)),
            ("sprk_inforcefrom", Utc(from)),
            (RuleSetService.PublishStateColumn, Option(RuleSetService.PublishStatePublished)),
        };
        if (procedureVersion is { } pv) columns.Add((RuleSetService.ProcedureVersionColumn, Ref(ProcedureEntity.VersionEntity, pv)));
        if (basedOn is { } b) columns.Add((ProcedureAssignmentService.BasedOnVersionColumn, Ref(PolicyVersion, b)));
        if (created is { } c) columns.Add(("createdon", c));
        World.Add(PolicyVersion, id, columns.ToArray());
        return id;
    }

    /// <summary>A Procedure with one version [<paramref name="from"/>, open) and one rule <paramref name="code"/> published under it.</summary>
    internal (Guid Procedure, Guid Version, Guid Rule, Guid RuleVersion) Simple(
        string code, ProcedureAppliesTo appliesTo = ProcedureAppliesTo.AllRecords, Guid? firm = null, DateOnly? from = null, string? name = null)
    {
        var procedure = AddProcedure(name ?? "Procedure " + code, appliesTo, firm);
        var version = AddVersion(procedure, 1, from ?? Jan1, null);
        var rule = AddRule(procedure, code);
        return (procedure, version, rule, AddRuleVersion(rule, version, from ?? Jan1));
    }

    private Guid AddOverrideRow(string table, Guid record, params (string Column, object Value)[] columns)
    {
        var id = Guid.NewGuid();
        World.Add(ProcedureAssignmentEntity.Entity, id, [.. columns, ("statecode", Option(0)), (table, Ref(table, record))]);
        return id;
    }

    internal Guid Assign(Guid record, Guid procedure, string? reason = null, string table = Matter)
    {
        var columns = new List<(string, object)>
        {
            (ProcedureAssignmentEntity.ProcedureColumn, Ref(ProcedureEntity.Entity, procedure)),
            (ProcedureAssignmentEntity.ModeColumn, Option(ProcedureAssignmentEntity.ModeFollowLatest)),
        };
        if (reason is not null) columns.Add((ProcedureAssignmentEntity.ReasonColumn, reason));
        return AddOverrideRow(table, record, columns.ToArray());
    }

    internal Guid ExcludeRule(Guid record, Guid policy, Guid? firm = null, string? reason = null, DateTime? created = null, string table = Matter)
    {
        var columns = new List<(string, object)>
        {
            (ProcedureAssignmentEntity.ExcludedPolicyColumn, Ref(Policy, policy)),
            (ProcedureAssignmentEntity.ModeColumn, Option(ProcedureAssignmentEntity.ModeExcluded)),
        };
        if (firm is { } f) columns.Add((ProcedureAssignmentEntity.FirmColumn, Ref("sprk_organization", f)));
        if (reason is not null) columns.Add((ProcedureAssignmentEntity.ReasonColumn, reason));
        if (created is { } c) columns.Add(("createdon", c));
        return AddOverrideRow(table, record, columns.ToArray());
    }

    /// <summary>A changed rule (089c): the Exclude row of the base rule plus the record rule's first version, written as the BFF writes it. Returns the record rule's policy id.</summary>
    internal Guid ChangeRule(Guid record, Guid basePolicy, Guid baseVersion, DateOnly from, string code = "BRR-1")
    {
        var at = Tick();
        ExcludeRule(record, basePolicy, firm: null, reason: null, created: at);
        var set = ProcedureAssignmentService.RecordRuleSetIdFor(Matter, record, null);
        World.Add(RuleSet, set,
            ("sprk_scope", Option(RecordRuleSet.ScopeRecord)), (RuleDomains.PolicyDomainColumn, Option(RuleDomains.PolicyDomainBilling)),
            ("statecode", Option(0)), (Matter, Ref(Matter, record)));
        var recordRule = AddRule(Guid.Empty, code, ProcedureAssignmentService.RecordRulePolicyIdFor(set, basePolicy), set);
        AddRuleVersion(recordRule, null, from, basedOn: baseVersion, created: at);
        return recordRule;
    }

    internal void Set(string table, Guid id, string column, object? value) => World.Set(table, id, column, value);

    // ── The invoice and its stored snapshot ──

    internal static Entity Invoice(Guid? firm = null, DateOnly? date = null, Guid? matter = null) => new("sprk_invoice", InvoiceId)
    {
        ["sprk_invoicedate"] = Utc(date ?? Mar15),
        ["sprk_vendororg"] = Ref("sprk_organization", firm ?? F),
        ["sprk_matter"] = Ref(Matter, matter ?? M),
    };

    internal static Entity Line(DateOnly? date, InvoiceLineCostType type = InvoiceLineCostType.Fee)
    {
        var line = new Entity("sprk_billingevent", Guid.NewGuid()) { ["sprk_costtype"] = Option((int)type) };
        if (date is { } d) line["sprk_eventdate"] = Utc(d);
        return line;
    }

    /// <summary>The lines of an invoice judged on <see cref="Feb15"/> and <see cref="Mar15"/>.</summary>
    internal static List<Entity> Lines() => [Line(Feb15), Line(Mar15)];

    /// <summary>
    /// The findings JSON an evaluation stored for the invoice: the snapshot the production builder writes for the CURRENT rows over
    /// <paramref name="dates"/> (the distinct subject dates), plus the received date.
    /// </summary>
    internal Task<string> StoredFindingsAsync() => StoredFindingsAsync(F, Feb15, Mar15);

    /// <summary>As <see cref="StoredFindingsAsync()"/> for an invoice of <paramref name="firm"/> judged on <paramref name="dates"/>.</summary>
    internal async Task<string> StoredFindingsAsync(Guid firm, params DateOnly[] dates)
    {
        var resolution = await ProcedureResolver.ReadAsync(
            Entities.Object, NullLogger.Instance, new CoreRecordReference(CoreRecordKind.Matter, M), firm, CancellationToken.None);
        var snapshot = ProcedureSnapshot.Build(new BillingProcedureBasis(
            resolution.Chain, dates.Distinct().Order().Select(resolution.RulesOn).ToList(), []));
        // "decision": the stored 073 decision, present exactly when an evaluation ran (its absence is what exempts a held, never-evaluated invoice).
        return new JsonObject
        {
            ["schema"] = 1,
            ["receivedOn"] = "2026-03-20",
            ["decision"] = new JsonObject { ["outcome"] = "Accepted" },
            [ProcedureSnapshot.Member] = snapshot,
        }.ToJsonString();
    }

    /// <summary>The number of Dataverse reads made so far (the resolver's reads and nothing else: the banner reads through no other seam).</summary>
    internal int Reads => World.Queries.Count + World.RowReads;

    /// <summary>True when any member of the Dataverse seam other than a read was called.</summary>
    internal bool Wrote => Entities.Invocations.Any(i => i.Method.Name is not (nameof(IGenericEntityService.RetrieveMultipleAsync) or nameof(IGenericEntityService.RetrieveAsync)));
}
