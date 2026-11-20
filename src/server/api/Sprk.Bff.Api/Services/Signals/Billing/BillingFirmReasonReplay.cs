// ebilling-r1 task 130e-1 (DEF-060, uac-r2 C1): the plan replay's firm reason. A billing rule Signal is written from the stored Signal plan
// (sprk_findingsjson signalPlan[]) by the after-commit steps, the re-run and the correction / Add line reconcile; the plan never carries the firm
// description, so each replay re-reads it from the plan entry's policy version and validates it here.
//
// Component Justification (CLAUDE.md section 11):
//   (1) Existing - BillingFindingSignalMapper.FirmReasonOf validates a description it already holds (the loader read it with the rule); the
//       replay paths hold only the plan entry, whose policy version id is the one input they have.
//   (2) Extension - the mapper is pure (no I/O) by contract, so the read cannot go there; persisting the text in the plan instead is what C1
//       forbids (a user-writable column would become the source of partner-visible text). One type shared by the three replay paths, not three
//       copies of the read.
//   (3) Cost of doing nothing - every billing rule Signal is written from the plan, so without this no Signal would ever carry the firm
//       description (DEF-060 stays open).

using Microsoft.Xrm.Sdk;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Finance;

namespace Sprk.Bff.Api.Services.Signals.Billing;

/// <summary>
/// Sets a replayed plan entry's <see cref="SignalWriteRequest.FirmReason"/> from its policy version as stored now: <c>sprk_firmdescription</c>,
/// validated by <see cref="FirmDescriptionRules.Validate(string?, string?, string?)"/> with the version's own rule set and policy names (C4).
/// Any firm reason the entry already carries is replaced (C1: a plan is never trusted for it). A text that fails is dropped: the request keeps
/// everything else and is written without a firm reason; the Warning carries the stable code and the version id only. Created per run; a
/// version is read once per run. A row that no longer exists drops the text (<see cref="RowGoneReason"/>); any other read fault propagates
/// (the step's own retry, as for every other Dataverse read in these steps).
/// </summary>
internal sealed class BillingFirmReasonReplay
{
    /// <summary>The drop reason when the version's policy cannot be named (its lookup is empty), so the name check cannot run: fail closed.</summary>
    internal const string NamesUnreadReason = "firm_description_names_unread";

    /// <summary>The drop reason when the version, its policy or its rule set no longer exists (Dataverse "object does not exist").</summary>
    internal const string RowGoneReason = "firm_description_row_gone";

    private readonly IGenericEntityService _entities;
    private readonly ILogger _logger;
    private readonly Dictionary<Guid, string?> _textByVersion = [];

    internal BillingFirmReasonReplay(IGenericEntityService entities, ILogger logger)
    {
        _entities = entities ?? throw new ArgumentNullException(nameof(entities));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>The columns of <c>sprk_policyversion</c> the replay reads (fix round 1 F2a: pinned against the solution by
    /// <c>PolicyVersionColumnsGuardTests</c>; F4: the rule type, so a Duplicate's stored text is never copied).</summary>
    internal static readonly string[] VersionColumns = [BillingFactLoader.FirmDescriptionColumn, "sprk_policy", "sprk_ruletype"];

    /// <summary>
    /// <paramref name="planned"/> with the firm reason its policy version allows now (or none). A held group's preflight fallback entry
    /// (<paramref name="heldFallback"/>, fix round 1 O2, ontology-r1 (a)) never carries one and reads nothing: the replay writes the same
    /// Signal the first write did.
    /// </summary>
    internal async Task<SignalWriteRequest> ApplyAsync(SignalWriteRequest planned, bool heldFallback, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(planned);
        if (heldFallback)
        {
            return planned with { FirmReason = null };
        }

        if (!_textByVersion.TryGetValue(planned.PolicyVersionId, out var text))
        {
            text = await ReadAsync(planned.PolicyVersionId, ct).ConfigureAwait(false);
            _textByVersion[planned.PolicyVersionId] = text;
        }

        return planned with { FirmReason = text };
    }

    private async Task<string?> ReadAsync(Guid versionId, CancellationToken ct)
    {
        try
        {
            return await ReadCheckedAsync(versionId, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (IsNotFound(ex))
        {
            // The version, its policy or its rule set is gone: there is no description to copy. The Signal is still written (C3).
            LogDropped(versionId, RowGoneReason);
            return null;
        }
    }

    private async Task<string?> ReadCheckedAsync(Guid versionId, CancellationToken ct)
    {
        var version = await _entities.RetrieveAsync(BillingFactLoader.VersionEntity, versionId, VersionColumns, ct).ConfigureAwait(false);
        var raw = version.GetAttributeValue<string>(BillingFactLoader.FirmDescriptionColumn);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        if (version.GetAttributeValue<EntityReference>("sprk_policy")?.Id is not { } policyId)
        {
            LogDropped(versionId, NamesUnreadReason);
            return null;
        }

        var policy = await _entities.RetrieveAsync(
            BillingFactLoader.PolicyEntity, policyId, [BillingFactLoader.NameColumn, "sprk_ruleset"], ct).ConfigureAwait(false);
        string? ruleSetName = null;
        if (policy.GetAttributeValue<EntityReference>("sprk_ruleset")?.Id is { } ruleSetId)
        {
            ruleSetName = (await _entities.RetrieveAsync(BillingFactLoader.RuleSetEntity, ruleSetId, [BillingFactLoader.NameColumn], ct).ConfigureAwait(false))
                .GetAttributeValue<string>(BillingFactLoader.NameColumn);
        }

        var ruleType = version.GetAttributeValue<OptionSetValue>("sprk_ruletype")?.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var check = FirmDescriptionRules.Validate(raw, ruleSetName, policy.GetAttributeValue<string>(BillingFactLoader.NameColumn), ruleType);
        if (!check.IsValid)
        {
            LogDropped(versionId, check.RefusalCode!);
        }

        return check.Text;
    }

    private static bool IsNotFound(Exception ex)
    {
        for (var e = ex; e is not null; e = e.InnerException)
        {
            if (Sprk.Bff.Api.Infrastructure.Dataverse.RecordContainerResolver.IsRecordNotFound(e))
            {
                return true;
            }
        }

        return false;
    }

    private void LogDropped(Guid versionId, string reason) =>
        _logger.LogWarning("[{Code}] The firm description of policy version {PolicyVersionId} was not copied to its Signal on replay ({Reason}); the Signal is written without one.",
            FirmDescriptionRules.DroppedLogCode, versionId, reason);
}
