using Microsoft.Xrm.Sdk;

namespace Sprk.Bff.Api.Services.Communication.Access;

/// <summary>
/// Applies the two Spaarke message business rules that Dataverse impersonation does NOT cover — INTERNAL-ONLY
/// (D-05) and PRIVILEGE (ADR-015) — on top of an ALREADY-IMPERSONATED <c>sprk_communication</c> set (FR-08 /
/// NFR-06). See <see cref="ICommunicationAccessFilter"/> for the full model.
/// <para>
/// <b>Record-level access is Dataverse's job</b> (owner decision 2026-07-16): the read endpoint (task 050) issues
/// the thread-message query with the <c>MSCRMCallerID</c> impersonation header, so the rows this filter receives
/// are already exactly what the caller may see. This type therefore reads ONLY two human-owned message flags and
/// takes NO Dataverse / membership / grant / AI dependency — "no AI at read time" (ADR-015) and "no re-computed
/// access at read time" (design §5) are both STRUCTURAL, not runtime checks. Every unresolved flag denies for a
/// non-internal caller (fail closed, NFR-06).
/// </para>
/// </summary>
public sealed class CommunicationAccessFilter : ICommunicationAccessFilter
{
    // Message (sprk_communication) fields — as-built names (notes/messaging-schema-spec.md).
    private const string MessageIsInternalOnlyField = "sprk_isinternalonly";
    private const string MessagePrivilegeField = "sprk_privilegeclassification";

    private readonly ILogger<CommunicationAccessFilter> _logger;

    public CommunicationAccessFilter(ILogger<CommunicationAccessFilter> logger)
    {
        _logger = logger;
    }

    public CommunicationAccessDecision EvaluateMessage(CommunicationAccessContext caller, Entity message)
    {
        ArgumentNullException.ThrowIfNull(caller);
        ArgumentNullException.ThrowIfNull(message);

        // ── Rule 1: internal-only (D-05). An sprk_isinternalonly message is hidden from a non-internal caller.
        // Default-deny (NFR-06): a non-internal caller is denied when the flag cannot be evaluated (missing /
        // wrong type → treated as internal-only). Internal callers are past this gate regardless of the flag.
        if (!caller.IsInternalUser && IsInternalOnlyOrUnknown(message))
            return CommunicationAccessDecision.Deny("internal-only");

        // ── Rule 2: privilege — composed metadata only; it did NOT gate this read (ADR-015, owner decision
        // 2026-07-16). The filter reads a human-owned field and NEVER calls AI.
        var privilege = ReadPrivilege(message);
        return CommunicationAccessDecision.Allow(privilege);
    }

    public CommunicationAccessResult FilterMessages(
        CommunicationAccessContext caller,
        IReadOnlyCollection<Entity> messages)
    {
        ArgumentNullException.ThrowIfNull(caller);
        ArgumentNullException.ThrowIfNull(messages);

        var visible = new List<Entity>(messages.Count);
        var decisions = new List<(Entity, CommunicationAccessDecision)>(messages.Count);

        foreach (var message in messages)
        {
            var decision = EvaluateMessage(caller, message);
            decisions.Add((message, decision));
            if (decision.IsVisible)
                visible.Add(message);
        }

        return new CommunicationAccessResult(visible, decisions);
    }

    /// <summary>
    /// True when the message is internal-only OR its internal-only flag cannot be evaluated (fail closed). Only
    /// consulted for a non-internal caller — for whom "unknown" must hide the row rather than expose it.
    /// </summary>
    private bool IsInternalOnlyOrUnknown(Entity message)
    {
        // Dataverse two-option surfaces as bool; a missing/absent attribute yields null here.
        var flag = message.GetAttributeValue<bool?>(MessageIsInternalOnlyField);
        if (flag is null)
        {
            _logger.LogWarning(
                "[COMM-ACCESS] Message {MessageId} has no readable '{Field}' value — treating as internal-only for the non-internal caller (fail closed).",
                message.Id, MessageIsInternalOnlyField);
            return true;
        }

        return flag.Value;
    }

    private static CommunicationPrivilegeClassification ReadPrivilege(Entity message)
    {
        var osv = message.GetAttributeValue<OptionSetValue>(MessagePrivilegeField);
        if (osv is null)
            return CommunicationPrivilegeClassification.None;
        return Enum.IsDefined(typeof(CommunicationPrivilegeClassification), osv.Value)
            ? (CommunicationPrivilegeClassification)osv.Value
            : CommunicationPrivilegeClassification.None;
    }
}
