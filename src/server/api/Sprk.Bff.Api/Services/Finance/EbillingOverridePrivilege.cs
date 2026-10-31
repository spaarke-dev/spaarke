using Sprk.Bff.Api.Infrastructure.ExternalAccess;

namespace Sprk.Bff.Api.Services.Finance;

/// <summary>
/// The "eBilling Override" privilege check, declared once (ebilling-r1 task 105; FR-19, FR-37, D-24, D-39, D-76): who may accept an internal
/// submission that the rejection rule blocked. The override routes and <c>canOverride</c> (read by the Invoice Review wizard and page,
/// tasks 052 and 111) all ask here, so there is one definition of the right.
/// </summary>
/// <remarks>
/// <para><b>Mechanism (D-76).</b> A Dataverse TABLE privilege the CALLER holds, asked of Dataverse as the caller
/// (<see cref="CallerRecordAccessProbe.CallerHoldsPrivilegeAsync"/>: on-behalf-of, uncached, fail closed). No security-role name and no
/// Entra app role. Dataverse has no custom privilege type, so the privilege has a carrier: <c>sprk_ebillingoverride</c>, a small dedicated
/// organization-owned table (created by <c>scripts/ebilling/Deploy-EbillingOverridePrivilegeSchema.ps1</c>) that holds no rows. The
/// administrator grants <b>Write</b> on it to the people allowed to override, in a security role (the script creates the role
/// "eBilling Override"). The marker is Write, as the billing admin's is on <c>sprk_ruleset</c> (<c>BillingAdminAuthorization</c>).</para>
/// <para><b>Depth.</b> Any depth counts, as for the billing admin (D-108): the check asks whether the caller holds the privilege. An
/// organization-owned table offers only Global depth, so the question does not arise here.</para>
/// <para><b>Fail closed.</b> No token, no OBO, a failed exchange, an unresolved caller, a non-success answer, an unreadable answer and any
/// exception all answer <c>false</c> (the probe), and so does an exception thrown by the probe itself (here). Only a cancellation by the
/// caller propagates.</para>
/// <para><b>Component justification (CLAUDE.md section 11).</b> Existing: <see cref="CallerRecordAccessProbe.CallerHoldsPrivilegeAsync"/>
/// and <c>BillingAdminAuthorization</c> (the same mechanism for the rule-set admin). Extension: <c>BillingAdminAuthorization</c> builds
/// route-filter checks whose denial is the generic <c>sdap.access.deny.insufficient_privilege</c>; the override must answer
/// <c>ebilling.override.not_permitted</c> and must also be asked from a read route to report <c>canOverride</c>, which a filter cannot do,
/// so the privilege name and the one question live here. Cost of doing nothing: the three consumers (the override route, the read
/// route's <c>canOverride</c>, and the wizard/page through it) would each spell the privilege and its failure handling, and one that
/// treated a probe fault as "allowed" would let anyone accept a blocked invoice.</para>
/// </remarks>
public static class EbillingOverridePrivilege
{
    /// <summary>The carrier table: organization-owned, no rows, exists only so Dataverse has a privilege to grant.</summary>
    public const string CarrierTable = "sprk_ebillingoverride";

    /// <summary>
    /// Write on the carrier table, by its live name (privilege names are <c>prv{Verb}{SchemaName}</c> with the schema name's case;
    /// the script creates the table with an all-lower-case schema name, as <c>sprk_ruleset</c>). A misspelt name answers "not held" for
    /// every caller and refuses every override, which is why a test pins it to the script's table name.
    /// </summary>
    public const string PrivilegeName = "prvWrite" + CarrierTable;

    /// <summary>The security role the script creates and grants the privilege to.</summary>
    public const string RoleName = "eBilling Override";

    /// <summary>
    /// Whether the CALLER holds the override privilege. False for a missing token, an unresolvable caller, a probe fault and
    /// every other failure (the answer is "no", never "unknown"). Only the caller's own cancellation propagates.
    /// </summary>
    public static async Task<bool> HoldsAsync(
        CallerRecordAccessProbe probe, string? callerBearerToken, ILogger? logger = null, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(probe);

        if (string.IsNullOrWhiteSpace(callerBearerToken))
        {
            return false;
        }

        try
        {
            return await probe.CallerHoldsPrivilegeAsync(callerBearerToken, PrivilegeName, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger?.LogWarning(
                "The eBilling Override privilege check faulted ({ExceptionType}); answering not held (fail closed).", ex.GetType().Name);
            return false;
        }
    }
}
