using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.Auth;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;

namespace Sprk.Bff.Api.Services.Access;

/// <summary>
/// Who may END a secure record's protection: owner decision F3 (round 3b), extended to children by owner round 10 item 7
/// ("moving a CHILD out of a secure root is an un-secure, so F3's limit applies"). Only a <b>Full Access holder on the
/// secure record</b>, or <b>the person who created the record whose protection ends</b>, may do it. Any other Write holder
/// is refused before anything is written.
/// </summary>
/// <remarks>
/// <para><b>One check, two callers.</b> Removing the designation from a ROOT (the unsecure endpoint: the root is both the
/// secure record and the record whose protection ends) and moving a CHILD out of a secure root
/// (<see cref="Sprk.Bff.Api.Services.Dataverse.IRecordOwnershipResolver.ReparentAsync"/>: Full Access is asked on the
/// secure root the child leaves, the creator is the CHILD's own). The substance and the two reason codes are task 150's
/// <c>UnsecureProjectEndpoint</c> F3 check, extracted here so the integrated tree has exactly one (task 146 c1).</para>
///
/// <para><b>Full Access</b> is Dataverse's own answer about the caller, asked AS THE CALLER (<c>RetrievePrincipalAccess</c>
/// under their own token — <see cref="CallerRecordAccessProbe"/> on an HTTP route): Write AND Delete on the secure record.
/// The Full Access share level is Collaborate plus Delete (<see cref="RecordShareLevels.FullAccessRights"/>), and a secure
/// record is owned by a memberless team in a user-free business unit, so Delete on it comes from a Full Access share — or
/// from an administrator's role. When the protection ends for several secure records at once (a child filed under two),
/// Full Access is needed on EACH.</para>
///
/// <para><b>The creator</b> is <c>createdby</c>, or the server-stamped <c>sprk_createdbyperson</c> (task 133) for a record
/// the BFF created app-only, whose <c>createdby</c> is the application user. The caller's identity is <c>WhoAmI</c> on
/// their own credential, never the request.</para>
///
/// <para><b>Order, and fail closed (ADR-003).</b> The creator recorded on the row is checked first (it needs no probe),
/// then Full Access, then — only when neither has admitted — a recorded creator person that must be read separately.
/// Positive evidence from either branch admits. A definite "no" from both refuses as
/// <see cref="NotPermittedReasonCode"/>. Anything unanswered — the caller's identity, a rights probe that threw, a creator
/// read that failed, a creator column this environment lacks, a secure record that cannot be named — refuses as
/// <see cref="PermissionUnverifiableReasonCode"/>: never "allowed". A failure is never read as a "no" either, so the
/// caller can tell a refusal from a check that could not run.</para>
///
/// <para><b>Placement (CLAUDE.md §10/§11).</b> A static rule beside <see cref="RecordShareLevels"/>, the share-level
/// vocabulary it reads; no service, no interface, no DI registration. The caller's side is a pair of delegates
/// (<see cref="SecureRemovalCaller"/>) so an HTTP route (the probe) and the chat tool plane (its user client) ask the same
/// question with their own credential.</para>
/// </remarks>
public static class SecureDesignationRemoval
{
    /// <summary>
    /// The caller is neither a Full Access holder on the secure record nor the creator. Task 150's code, kept verbatim: it
    /// is a machine-readable contract.
    /// </summary>
    public const string NotPermittedReasonCode = "sdap.unsecure.not_permitted";

    /// <summary>
    /// Whether the caller may end the protection could not be established — the caller's identity, their rights, or the
    /// record's creator could not be read. Nothing was written; the same caller may retry.
    /// </summary>
    public const string PermissionUnverifiableReasonCode = "sdap.unsecure.permission_unverifiable";

    /// <summary>Full Access, as rights on a record: Write AND Delete (the Full Access share is Collaborate plus Delete).</summary>
    public const AccessRights FullAccess = AccessRights.Write | AccessRights.Delete;

    /// <summary>True for the two reason codes this rule answers with.</summary>
    public static bool IsReasonCode(string? code) =>
        string.Equals(code, NotPermittedReasonCode, StringComparison.Ordinal)
        || string.Equals(code, PermissionUnverifiableReasonCode, StringComparison.Ordinal);

    /// <summary>
    /// Decides whether the caller may end the protection <paramref name="question"/> describes. Nothing is written here;
    /// the caller writes only on <see cref="SecureRemovalDecision.IsPermitted"/>.
    /// </summary>
    /// <exception cref="ArgumentException">The question names no secure record.</exception>
    public static async Task<SecureRemovalDecision> DecideAsync(SecureRemovalQuestion question, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(question);
        if (question.SecuredRecords.Count == 0 && !question.IncludesUnidentifiedSecureRecord)
        {
            throw new ArgumentException("A secure-removal question names at least one secure record.", nameof(question));
        }

        // 1. Who is asking: WhoAmI on the caller's own credential. Unknown → nobody can be matched to the creator or
        //    shown to hold Full Access (a background writer has no caller at all).
        Guid caller;
        try
        {
            var id = question.Caller is null
                ? null
                : await question.Caller.SystemUserIdAsync(ct).ConfigureAwait(false);
            if (id is not { } known || known == Guid.Empty)
                return SecureRemovalDecision.Refused(SecureRemovalOutcome.Unverifiable, SecureRemovalBasis.CallerUnknown, Guid.Empty);

            caller = known;
        }
        catch (Exception ex) when (!IsCancellation(ex, ct))
        {
            return SecureRemovalDecision.Refused(SecureRemovalOutcome.Unverifiable, SecureRemovalBasis.CallerUnknown, Guid.Empty);
        }

        // 2. The creator recorded on the row — no rights probe needed.
        if (question.CreatedBy == caller || question.CreatedByPerson == caller)
            return SecureRemovalDecision.Permitted(SecureRemovalBasis.Creator, caller);

        // 3. Full Access on EVERY secure record whose protection ends. One definite lack settles the branch.
        var fullAccessLacking = false;
        var rightsFault = false;
        foreach (var record in question.SecuredRecords)
        {
            AccessRights rights;
            try
            {
                rights = await question.Caller!.RightsAsync(record, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (!IsCancellation(ex, ct))
            {
                rightsFault = true;
                continue;
            }

            if ((rights & FullAccess) != FullAccess)
            {
                fullAccessLacking = true;
                break;
            }
        }

        if (!fullAccessLacking && !rightsFault && !question.IncludesUnidentifiedSecureRecord)
            return SecureRemovalDecision.Permitted(SecureRemovalBasis.FullAccess, caller);

        // 4. The recorded creator person, read only now (a record without the column passes no reader).
        var creatorFault = false;
        var creatorColumnAbsent = false;
        if (question.ReadCreatedByPersonAsync is { } readCreatorPerson)
        {
            try
            {
                var answer = await readCreatorPerson(ct).ConfigureAwait(false);
                if (!answer.ColumnPresent)
                    creatorColumnAbsent = true;
                else if (answer.PersonId == caller)
                    return SecureRemovalDecision.Permitted(SecureRemovalBasis.Creator, caller);
            }
            catch (Exception ex) when (!IsCancellation(ex, ct))
            {
                creatorFault = true;
            }
        }

        // 5. Nothing admitted. Both branches a definite "no" → not permitted; anything unanswered → unverifiable.
        if (!fullAccessLacking && rightsFault)
            return SecureRemovalDecision.Refused(SecureRemovalOutcome.Unverifiable, SecureRemovalBasis.RightsUnreadable, caller);
        if (creatorFault)
            return SecureRemovalDecision.Refused(SecureRemovalOutcome.Unverifiable, SecureRemovalBasis.CreatorUnreadable, caller);
        if (!fullAccessLacking && question.IncludesUnidentifiedSecureRecord)
            return SecureRemovalDecision.Refused(SecureRemovalOutcome.Unverifiable, SecureRemovalBasis.SecureRecordUnidentified, caller);
        if (creatorColumnAbsent)
            return SecureRemovalDecision.Refused(SecureRemovalOutcome.Unverifiable, SecureRemovalBasis.CreatorColumnAbsent, caller);

        return SecureRemovalDecision.Refused(SecureRemovalOutcome.NotPermitted, SecureRemovalBasis.NotFullAccessOrCreator, caller);
    }

    /// <summary>
    /// The Web API entity set of a secure-root table — the three that carry <c>sprk_issecure</c>, from the ONE root table
    /// (<see cref="ExternalGrantRoot"/>). Full Access is only ever asked of a secure root.
    /// </summary>
    /// <exception cref="ArgumentException">The table is not a secure root — the rights question then counts as unanswered.</exception>
    public static string EntitySetFor(string rootEntityLogicalName)
    {
        foreach (var type in Enum.GetValues<ExternalGrantRootType>())
        {
            if (string.Equals(ExternalGrantRoot.LogicalNameFor(type), rootEntityLogicalName?.Trim(), StringComparison.OrdinalIgnoreCase))
                return ExternalGrantRoot.BindFor(type).EntitySet;
        }

        throw new ArgumentException($"'{rootEntityLogicalName}' is not a secure-root table.", nameof(rootEntityLogicalName));
    }

    private static bool IsCancellation(Exception ex, CancellationToken ct) =>
        ex is OperationCanceledException && ct.IsCancellationRequested;
}

/// <summary>What ending a protection would end, and who created the record that loses it.</summary>
public sealed record SecureRemovalQuestion
{
    /// <summary>The person asking, asked AS THEMSELVES. <c>null</c>: the write has no caller (a background job), so nobody
    /// can be shown to be permitted.</summary>
    public SecureRemovalCaller? Caller { get; init; }

    /// <summary>The secure records whose protection ends — Full Access is asked on EACH.</summary>
    public IReadOnlyList<SecuredRecordRef> SecuredRecords { get; init; } = Array.Empty<SecuredRecordRef>();

    /// <summary>
    /// True when the record is owned in the Secure Record business unit but the secure record it belongs to cannot be named
    /// from its own filing (a message secured by the record thread it joined): nobody's Full Access can be checked, so only
    /// the creator is admitted.
    /// </summary>
    public bool IncludesUnidentifiedSecureRecord { get; init; }

    /// <summary><c>createdby</c> of the record whose protection ends.</summary>
    public Guid? CreatedBy { get; init; }

    /// <summary>The recorded creator person (<c>sprk_createdbyperson</c>) when it was read with the row.</summary>
    public Guid? CreatedByPerson { get; init; }

    /// <summary>
    /// Reads the recorded creator person, only when Full Access has not admitted. <c>null</c>: the record carries no such
    /// column, so <see cref="CreatedBy"/> alone is its creator. A failed read throws (unverifiable); a column this
    /// environment lacks answers <see cref="CreatorPersonAnswer.ColumnAbsent"/> (unverifiable too).
    /// </summary>
    public Func<CancellationToken, Task<CreatorPersonAnswer>>? ReadCreatedByPersonAsync { get; init; }
}

/// <summary>One secure record whose protection ends.</summary>
public sealed record SecuredRecordRef(string EntityLogicalName, Guid RecordId);

/// <summary>The recorded creator person, or the fact that the column does not exist in this environment.</summary>
public readonly record struct CreatorPersonAnswer(bool ColumnPresent, Guid? PersonId)
{
    /// <summary>The column exists; <paramref name="personId"/> is its value (<c>null</c>: nobody recorded).</summary>
    public static CreatorPersonAnswer Recorded(Guid? personId) => new(true, personId);

    /// <summary>The column does not exist here (its schema step has not run).</summary>
    public static CreatorPersonAnswer ColumnAbsent => new(false, null);
}

/// <summary>
/// The caller's side of the question, asked with their own credential: who they are (<c>WhoAmI</c>) and their rights on a
/// record (<c>RetrievePrincipalAccess</c>). An HTTP route builds it with <see cref="ForRequest"/>; the chat tool plane from
/// its user client.
/// </summary>
public sealed class SecureRemovalCaller
{
    private readonly Func<CancellationToken, Task<Guid?>> _systemUserIdAsync;
    private readonly Func<SecuredRecordRef, CancellationToken, Task<AccessRights>> _rightsAsync;

    public SecureRemovalCaller(
        Func<CancellationToken, Task<Guid?>> systemUserIdAsync,
        Func<SecuredRecordRef, CancellationToken, Task<AccessRights>> rightsAsync)
    {
        _systemUserIdAsync = systemUserIdAsync ?? throw new ArgumentNullException(nameof(systemUserIdAsync));
        _rightsAsync = rightsAsync ?? throw new ArgumentNullException(nameof(rightsAsync));
    }

    /// <summary>The caller's <c>systemuserid</c>; <c>null</c> when it cannot be established.</summary>
    public Task<Guid?> SystemUserIdAsync(CancellationToken ct) => _systemUserIdAsync(ct);

    /// <summary>The caller's rights on <paramref name="record"/>, as Dataverse reports them.</summary>
    public Task<AccessRights> RightsAsync(SecuredRecordRef record, CancellationToken ct) => _rightsAsync(record, ct);

    /// <summary>
    /// The caller of an HTTP request, through <see cref="CallerRecordAccessProbe"/> on their own bearer token — the same
    /// probe the unsecure endpoint and the route filters use. The probe answers <c>null</c> / <see cref="AccessRights.None"/>
    /// when it cannot reach Dataverse, which this rule reads as "unknown caller" / "no Full Access" (fail closed).
    /// </summary>
    public static SecureRemovalCaller ForRequest(CallerRecordAccessProbe probe, HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(probe);
        ArgumentNullException.ThrowIfNull(httpContext);
        var token = TokenHelper.ExtractBearerTokenOrNull(httpContext);

        return new SecureRemovalCaller(
            ct => probe.GetCallerSystemUserIdAsync(token, ct),
            (record, ct) => probe.GetCallerRightsAsync(
                token, SecureDesignationRemoval.EntitySetFor(record.EntityLogicalName), record.RecordId, ct));
    }

    /// <summary>
    /// The user a writer ACTS AS by impersonation (<c>MSCRMCallerID</c>) — a playbook update the confirming user approved
    /// (task 146 c1-r1, owner round 13 item 8: "a playbook that impersonates a user is checked under F3 as that user; only
    /// truly person-less writers are refused"). The identity is the one the write itself runs as; the rights are
    /// <c>RetrievePrincipalAccess</c> asked AS that user (<see cref="IDataverseRecordShareService.GetPrincipalRightsAsync"/>).
    /// A missing seam, or a read that fails, FAULTS the rights question — the rule answers it "could not be checked",
    /// never "allowed".
    /// </summary>
    public static SecureRemovalCaller ForImpersonatedUser(Guid systemUserId, IDataverseRecordShareService? access) =>
        new(
            _ => Task.FromResult<Guid?>(systemUserId == Guid.Empty ? null : systemUserId),
            (record, ct) => access is null
                ? Task.FromException<AccessRights>(new InvalidOperationException(
                    "No record-access seam is registered, so the impersonated user's rights cannot be read."))
                : access.GetPrincipalRightsAsync(
                    systemUserId, SecureDesignationRemoval.EntitySetFor(record.EntityLogicalName), record.RecordId, ct));
}

/// <summary>How a <see cref="SecureDesignationRemoval"/> question was answered.</summary>
public enum SecureRemovalOutcome
{
    Permitted,
    NotPermitted,
    Unverifiable,
}

/// <summary>Why — for the message a caller shows, and the log.</summary>
public enum SecureRemovalBasis
{
    /// <summary>The caller created the record (<c>createdby</c> or <c>sprk_createdbyperson</c>).</summary>
    Creator,

    /// <summary>The caller holds Full Access (Write + Delete) on every secure record.</summary>
    FullAccess,

    /// <summary>A definite "no": not the creator, and not a Full Access holder.</summary>
    NotFullAccessOrCreator,

    /// <summary>The caller's identity could not be established (or the write has no caller).</summary>
    CallerUnknown,

    /// <summary>A rights probe threw.</summary>
    RightsUnreadable,

    /// <summary>The recorded creator person could not be read.</summary>
    CreatorUnreadable,

    /// <summary>The creator-person column does not exist in this environment.</summary>
    CreatorColumnAbsent,

    /// <summary>The secure record the protection comes from cannot be named, so nobody's Full Access can be checked.</summary>
    SecureRecordUnidentified,
}

/// <summary>The answer: permitted, or refused with a stable reason code and the ProblemDetails the unsecure endpoint uses.</summary>
public sealed record SecureRemovalDecision(SecureRemovalOutcome Outcome, SecureRemovalBasis Basis, Guid CallerSystemUserId)
{
    public bool IsPermitted => Outcome == SecureRemovalOutcome.Permitted;

    /// <summary><c>null</c> when permitted; otherwise one of the two <see cref="SecureDesignationRemoval"/> codes.</summary>
    public string? ReasonCode => Outcome switch
    {
        SecureRemovalOutcome.NotPermitted => SecureDesignationRemoval.NotPermittedReasonCode,
        SecureRemovalOutcome.Unverifiable => SecureDesignationRemoval.PermissionUnverifiableReasonCode,
        _ => null,
    };

    /// <summary>
    /// The refusal's HTTP status, as task 150 answers it: 500 when a read FAILED (a retry may succeed), 403 otherwise —
    /// a definite "no", an unknown caller, a creator this environment does not record, a secure record that cannot be named.
    /// </summary>
    public int StatusCode => IsPermitted
        ? StatusCodes.Status200OK
        : Basis is SecureRemovalBasis.RightsUnreadable or SecureRemovalBasis.CreatorUnreadable
            ? StatusCodes.Status500InternalServerError
            : StatusCodes.Status403Forbidden;

    internal static SecureRemovalDecision Permitted(SecureRemovalBasis basis, Guid caller) =>
        new(SecureRemovalOutcome.Permitted, basis, caller);

    internal static SecureRemovalDecision Refused(SecureRemovalOutcome outcome, SecureRemovalBasis basis, Guid caller) =>
        new(outcome, basis, caller);

    /// <summary>
    /// The refusal as the unsecure endpoint's ProblemDetails: <c>title</c> by status, the <paramref name="detail"/> shown to
    /// the user as is, and the extensions <c>traceId</c> and <c>reasonCode</c>.
    /// </summary>
    /// <exception cref="InvalidOperationException">The decision is a permission, not a refusal.</exception>
    public IResult ToProblem(string detail, string? traceId)
    {
        if (IsPermitted)
            throw new InvalidOperationException("A permitted secure-removal decision has no ProblemDetails.");

        var extensions = new Dictionary<string, object?> { ["traceId"] = traceId, ["reasonCode"] = ReasonCode };
        return Results.Problem(
            statusCode: StatusCode,
            title: StatusCode == StatusCodes.Status500InternalServerError ? "Internal Server Error" : "Forbidden",
            detail: detail,
            extensions: extensions);
    }

    /// <summary>
    /// The message for a CHILD moved out of a secure record (owner round 10 item 7). It names no record: the caller can see
    /// the child, not necessarily the secure record it is filed under.
    /// </summary>
    public string MoveOutDetail(string childNoun) => Basis switch
    {
        SecureRemovalBasis.NotFullAccessOrCreator =>
            $"Moving this {childNoun} out of the secure record it is filed under ends its secure protection. Only someone with "
            + $"Full Access to that secure record, or the person who created this {childNoun}, can do that. It was not moved, "
            + "and nothing was changed.",
        SecureRemovalBasis.CallerUnknown =>
            $"Whether this {childNoun} may be moved out of the secure record it is filed under could not be checked, because "
            + "the person making the change could not be confirmed. Nothing was changed.",
        SecureRemovalBasis.RightsUnreadable =>
            $"Whether you may move this {childNoun} out of the secure record it is filed under could not be checked, because "
            + "your access to that secure record could not be read. Nothing was changed.",
        SecureRemovalBasis.CreatorUnreadable =>
            $"Whether you may move this {childNoun} out of the secure record it is filed under could not be checked, because "
            + $"the person who created this {childNoun} could not be looked up. Nothing was changed.",
        SecureRemovalBasis.CreatorColumnAbsent =>
            $"Whether you may move this {childNoun} out of the secure record it is filed under could not be checked, because "
            + $"who created this {childNoun} is not recorded in this environment. Nothing was changed.",
        SecureRemovalBasis.SecureRecordUnidentified =>
            $"This {childNoun} is secure, but the secure record it belongs to could not be determined, so only the person who "
            + $"created it can move it out. Nothing was changed.",
        _ => $"This {childNoun} was not moved, and nothing was changed.",
    };
}
