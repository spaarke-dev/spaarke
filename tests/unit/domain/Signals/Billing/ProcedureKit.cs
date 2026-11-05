using System.Globalization;
using Sprk.Bff.Api.Services.Finance.Procedures;
using Sprk.Bff.Api.Services.Signals;
using Sprk.Bff.Api.Services.Signals.Billing;
using static Sprk.Bff.Api.Tests.Domain.Signals.Billing.BillingEvaluationKit;

namespace Sprk.Bff.Api.Tests.Domain.Signals.Billing;

/// <summary>
/// ebilling-r1 082c: the rows of one <see cref="ProcedureResolution"/>, built in memory (the resolver's own reads are 082b's tests). Each
/// Procedure has one Active version from <see cref="BillingEvaluationKit.RulesFrom"/> (or the window given) and one rule set; each rule is one
/// Published policy version under that Procedure Version. Ids come from labels, prefixed so they never equal a legacy candidate's.
/// </summary>
internal sealed class ProcedureKit
{
    /// <summary>The invoice's core record (distance 0). A manual assignment sits on it.</summary>
    internal static readonly CoreRecordLink Matter = new("sprk_matter", Id("procedure-kit:matter"), 0);

    private readonly List<ProcedureOverrideRow> _overrides = [];
    private readonly List<ProcedureRow> _procedures = [];
    private readonly List<ProcedureVersionRow> _versions = [];
    private readonly List<RuleSetRow> _sets = [];
    private readonly List<PolicyRow> _policies = [];
    private readonly List<ResolvedPolicyVersion> _policyVersions = [];

    internal static Guid ProcedureId(string name) => Id("procedure-kit:procedure:" + name);

    internal static Guid ProcedureVersionId(string name, int number = 1) =>
        Id("procedure-kit:procedure-version:" + name + (number == 1 ? string.Empty : ":" + number.ToString(CultureInfo.InvariantCulture)));

    /// <summary>
    /// A Procedure. Its rank: <see cref="ProcedureAppliesTo.AllRecords"/> with no firm is (AllRecords, 0, false), the legacy client default's;
    /// with <paramref name="firm"/> it is (AllRecords, 0, true); <see cref="ProcedureAppliesTo.Firm"/> for the kit's firm is (Firm, 0, true), the
    /// legacy firm's; <paramref name="assigned"/> adds a Follow-latest row on <see cref="Matter"/>: (ManualOwn, 0, firm-specific).
    /// </summary>
    internal ProcedureKit Procedure(
        string name, ProcedureAppliesTo appliesTo = ProcedureAppliesTo.AllRecords, Guid? firm = null, bool assigned = false,
        DateOnly? from = null, DateOnly? to = null)
    {
        var id = ProcedureId(name);
        _procedures.Add(new ProcedureRow(id, name, appliesTo, firm));
        _versions.Add(new ProcedureVersionRow(ProcedureVersionId(name), id, 1, ProcedureEntity.VersionStatusActive, from ?? RulesFrom, to));
        _sets.Add(new RuleSetRow(Id("procedure-kit:set:" + name), id, null, null));
        if (assigned)
        {
            _overrides.Add(new ProcedureOverrideRow(
                Id("procedure-kit:assignment:" + name), ProcedureOverrideMode.FollowLatest, Matter, id, null, null, null, null));
        }

        return this;
    }

    /// <summary>A further Active version of <paramref name="name"/> (number 2 or more) over its own window: a gap between two windows is a hole.</summary>
    internal ProcedureKit NextVersion(string name, int number, DateOnly from, DateOnly? to = null)
    {
        _versions.Add(new ProcedureVersionRow(ProcedureVersionId(name, number), ProcedureId(name), number, ProcedureEntity.VersionStatusActive, from, to));
        return this;
    }

    /// <summary>A rule of <paramref name="procedure"/>: one Published policy version under its version (number <paramref name="versionNumber"/>), in force from its window's start.</summary>
    internal ResolvedPolicyVersion Rule(string procedure, string code, RuleType type, string? body = null, int? consequence = null, int versionNumber = 1)
    {
        var policyId = Id("procedure-kit:policy:" + code);
        var setId = Id("procedure-kit:set:" + procedure);
        _policies.Add(new PolicyRow(policyId, code, setId, null, null));
        var version = new ResolvedPolicyVersion
        {
            PolicyId = policyId,
            PolicyCode = code,
            PolicyVersionId = Id("procedure-kit:policy-version:" + code),
            RuleSetId = setId,
            VersionNumber = 1,
            RuleType = ((int)type).ToString(CultureInfo.InvariantCulture),
            RuleBody = body ?? Bodies[type],
            ClauseReference = $"Clause of {code}",
            ClauseLocation = $"Procedure {procedure}",
            InForceFrom = _versions.Single(v => v.Id == ProcedureVersionId(procedure, versionNumber)).From,
            Consequence = consequence,
            ProcedureVersionId = ProcedureVersionId(procedure, versionNumber),
        };
        _policyVersions.Add(version);
        return version;
    }

    internal ProcedureResolution Build() =>
        new(new ProcedureRows(FirmId, [Matter], _overrides, _procedures, _versions, _sets, _policies, _policyVersions));

    /// <summary>The loaded facts: <paramref name="legacy"/> as the legacy stream, this kit's rows as the Procedure stream.</summary>
    internal BillingLoadedFacts Loaded(params BillingRuleCandidate[] legacy) =>
        BillingEvaluationKit.Loaded(legacy) with { Procedures = Build() };
}
