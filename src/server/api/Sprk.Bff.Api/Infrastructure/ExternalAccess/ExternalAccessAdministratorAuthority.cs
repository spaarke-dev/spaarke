using System.Text.Json.Serialization;
using Spaarke.Dataverse;

namespace Sprk.Bff.Api.Infrastructure.ExternalAccess;

/// <summary>How the caller holds the external-access administrator authority (recorded in the audit trace).</summary>
public enum AdministratorAuthorityLeg
{
    /// <summary>Not held, or not established.</summary>
    None = 0,

    /// <summary>The caller is a direct member of the standing-grant administrators profile.</summary>
    Direct = 1,

    /// <summary>The caller is a member of a team that holds the standing-grant administrators profile.</summary>
    Team = 2,
}

/// <summary>
/// Why the authority was not established. OPERATOR-ONLY: logged with the decision, never sent to the caller (the caller
/// gets one uniform 403).
/// </summary>
public enum AdministratorAuthorityReason
{
    /// <summary>The caller holds the authority.</summary>
    Granted = 0,

    /// <summary>The caller's systemuserid could not be resolved over the staff OBO token (no token, a failed exchange, an empty WhoAmI).</summary>
    CallerUnresolved = 1,

    /// <summary>No field security profile carries the pinned name.</summary>
    ProfileMissing = 2,

    /// <summary>More than one field security profile carries the pinned name, so which one confers the authority is unknown.</summary>
    ProfileAmbiguous = 3,

    /// <summary>The caller is neither a direct member of the profile nor a member of a team that holds it.</summary>
    NotAMember = 4,

    /// <summary>A Dataverse read faulted (or answered something that is not a profile id).</summary>
    ReadFault = 5,

    /// <summary>The check did not finish within <see cref="ExternalAccessAdministratorAuthority.CheckTimeout"/>.</summary>
    Timeout = 6,
}

/// <summary>The outcome of one administrator-authority check.</summary>
/// <param name="SystemUserId">The caller's systemuserid as WhoAmI answered it over the staff OBO token; null when unresolved.</param>
/// <param name="Leg">Direct or team membership when held; <see cref="AdministratorAuthorityLeg.None"/> otherwise.</param>
/// <param name="Reason">The operator-only reason; <see cref="AdministratorAuthorityReason.Granted"/> when held.</param>
public sealed record AdministratorAuthorityDecision(
    Guid? SystemUserId, AdministratorAuthorityLeg Leg, AdministratorAuthorityReason Reason)
{
    /// <summary>The caller holds the authority.</summary>
    public bool IsAdministrator => Reason == AdministratorAuthorityReason.Granted && Leg != AdministratorAuthorityLeg.None
                                   && SystemUserId is { } id && id != Guid.Empty;

    internal static AdministratorAuthorityDecision Refused(Guid? systemUserId, AdministratorAuthorityReason reason)
        => new(systemUserId, AdministratorAuthorityLeg.None, reason);
}

/// <summary>
/// THE external-access administrator check, shared by every person-level administration route (remove a person, reinstate a
/// person): the caller holds the authority when they are a member of the standing-grant administrators field security profile,
/// directly or through a team that holds it.
/// </summary>
/// <remarks>
/// <para><b>Who the caller is.</b> Only <c>WhoAmI()</c> over the caller's own staff OBO token
/// (<see cref="CallerRecordAccessProbe.GetCallerSystemUserIdAsync"/>) names the caller: never a request field and never a
/// claim this code maps. An empty or failed WhoAmI refuses.</para>
/// <para><b>What the caller holds.</b> Read with the BFF's app credential, keyed only on that systemuserid: the profile is
/// resolved by its pinned name (<see cref="StandingGrantProfileName"/>) to exactly one id (none or several refuse), then ONE
/// server-side query asks whether that profile has the caller as a direct member or holds a team the caller belongs to (a
/// nested <c>any()</c> over the team's membership, so the request never carries a client-side team list). When it does, a
/// direct-membership read of the same profile labels the leg for the audit trace.</para>
/// <para><b>Fail closed.</b> Any fault, an answer that is not a profile id, or a check that runs past
/// <see cref="CheckTimeout"/> refuses, with an operator-only <see cref="AdministratorAuthorityReason"/>. The caller's own
/// cancellation propagates.</para>
/// <para><b>No cache.</b> Every check reads live, so a membership removed in Dataverse takes effect on the next request.</para>
/// <para><b>CLAUDE.md §11.</b> Existing: <see cref="CallerRecordAccessProbe"/> answers record rights and table privileges, and
/// the delegation filter gates on a record; neither answers profile membership, and these routes name no record. Extension:
/// the probe's WhoAmI is reused as is; the membership read is new because nothing reads a profile's members on a request path.
/// Cost of doing nothing: removing or reinstating a person would have no authority check, so any signed-in staff user could
/// end or restore another person's external access.</para>
/// </remarks>
public class ExternalAccessAdministratorAuthority
{
    /// <summary>
    /// The field security profile whose membership is the authority. Pinned to provisioning's
    /// <c>SecureRecordSetupProcedure.StandingGrantProfileName</c> by <c>StandingGrantProfileNamePinTests</c> (ArchTests); the
    /// two must never differ.
    /// </summary>
    public const string StandingGrantProfileName = "Standing Grant Administrators";

    /// <summary>The longest the whole check may take before it refuses.</summary>
    internal static readonly TimeSpan CheckTimeout = TimeSpan.FromSeconds(10);

    private const string ProfileEntitySet = "fieldsecurityprofiles";
    private const string ProfileIdColumn = "fieldsecurityprofileid";

    private readonly CallerRecordAccessProbe _probe;
    private readonly DataverseWebApiClient _dataverse;
    private readonly ILogger _logger;

    public ExternalAccessAdministratorAuthority(CallerRecordAccessProbe probe, DataverseWebApiClient dataverse, ILogger logger)
    {
        _probe = probe ?? throw new ArgumentNullException(nameof(probe));
        _dataverse = dataverse ?? throw new ArgumentNullException(nameof(dataverse));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>The profile read by name: every profile carrying <see cref="StandingGrantProfileName"/>. Pure.</summary>
    internal static string ProfileByNameFilter() => $"name eq '{StandingGrantProfileName.Replace("'", "''")}'";

    /// <summary>
    /// The ONE membership query: the profile, kept only when <paramref name="systemUserId"/> is a direct member OR a member
    /// of a team that holds it — the team leg joined server-side. Pure.
    /// </summary>
    internal static string MembershipFilter(Guid profileId, Guid systemUserId)
        => $"{ProfileIdColumn} eq {profileId:D} and ({DirectLeg(systemUserId)} or {TeamLeg(systemUserId)})";

    /// <summary>The direct-membership read that labels the leg for the audit trace. Pure.</summary>
    internal static string DirectMembershipFilter(Guid profileId, Guid systemUserId)
        => $"{ProfileIdColumn} eq {profileId:D} and {DirectLeg(systemUserId)}";

    private static string DirectLeg(Guid systemUserId)
        => $"systemuserprofiles_association/any(u:u/systemuserid eq {systemUserId:D})";

    private static string TeamLeg(Guid systemUserId)
        => $"teamprofiles_association/any(t:t/teammembership_association/any(u:u/systemuserid eq {systemUserId:D}))";

    /// <summary>
    /// Decides whether the caller behind <paramref name="callerBearerToken"/> holds the authority. Never throws but for the
    /// caller's own cancellation.
    /// </summary>
    public virtual async Task<AdministratorAuthorityDecision> DecideAsync(string? callerBearerToken, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(CheckTimeout);
        Guid? caller = null;
        try
        {
            caller = await _probe.GetCallerSystemUserIdAsync(callerBearerToken, timeout.Token).ConfigureAwait(false);
            if (caller is not { } systemUserId || systemUserId == Guid.Empty)
            {
                return Refused(null, AdministratorAuthorityReason.CallerUnresolved);
            }

            var profiles = await _dataverse.QueryAsync<ProfileIdRow>(
                ProfileEntitySet, filter: ProfileByNameFilter(), select: ProfileIdColumn, top: 2,
                cancellationToken: timeout.Token).ConfigureAwait(false);
            if (profiles.Count == 0)
            {
                return Refused(systemUserId, AdministratorAuthorityReason.ProfileMissing);
            }

            if (profiles.Count > 1)
            {
                return Refused(systemUserId, AdministratorAuthorityReason.ProfileAmbiguous);
            }

            if (profiles[0].Id is not { } profileId || profileId == Guid.Empty)
            {
                return Refused(systemUserId, AdministratorAuthorityReason.ReadFault);
            }

            var held = await _dataverse.QueryAsync<ProfileIdRow>(
                ProfileEntitySet, filter: MembershipFilter(profileId, systemUserId), select: ProfileIdColumn, top: 1,
                cancellationToken: timeout.Token).ConfigureAwait(false);
            if (!held.Any(p => p.Id == profileId))
            {
                return Refused(systemUserId, AdministratorAuthorityReason.NotAMember);
            }

            var direct = await _dataverse.QueryAsync<ProfileIdRow>(
                ProfileEntitySet, filter: DirectMembershipFilter(profileId, systemUserId), select: ProfileIdColumn, top: 1,
                cancellationToken: timeout.Token).ConfigureAwait(false);
            var leg = direct.Any(p => p.Id == profileId) ? AdministratorAuthorityLeg.Direct : AdministratorAuthorityLeg.Team;

            _logger.LogInformation(
                "[EXT-ADMIN] Systemuser {SystemUserId} holds the external-access administrator authority ({Leg}).",
                systemUserId, leg);
            return new AdministratorAuthorityDecision(systemUserId, leg, AdministratorAuthorityReason.Granted);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            return Refused(caller, AdministratorAuthorityReason.Timeout);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "[EXT-ADMIN] The external-access administrator check faulted for systemuser {SystemUserId}; refusing (fail closed).",
                caller);
            return Refused(caller, AdministratorAuthorityReason.ReadFault);
        }
    }

    private AdministratorAuthorityDecision Refused(Guid? systemUserId, AdministratorAuthorityReason reason)
    {
        _logger.LogWarning(
            "[EXT-ADMIN] The external-access administrator authority was refused for systemuser {SystemUserId}: {Reason}.",
            systemUserId, reason);
        return AdministratorAuthorityDecision.Refused(systemUserId, reason);
    }

    /// <summary>One <c>fieldsecurityprofile</c>'s id.</summary>
    internal sealed class ProfileIdRow
    {
        [JsonPropertyName("fieldsecurityprofileid")]
        public Guid? Id { get; set; }
    }
}
