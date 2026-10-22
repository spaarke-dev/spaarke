// ISS-005 (#1673, TENANT-ISOLATION) — the owner policy of the contacts ContactIdentityBinder creates on the Workforce plane
// (first sign-in and the licensed-user link; registration resolves its own owner from the request).
//
// Why a type rather than two optional constructor arguments (UAC review): the binder used to take the customer-owner inputs as
// nullable, defaulted parameters and fall back to "no owner" when they were absent. A binder built without them created
// ownerless contacts without anyone having said so, and the compiler could not tell a deliberate omission from a forgotten
// argument (that is how a registration approval in this BFF's own environment once created an app-owned contact in root).
// Now the constructor REQUIRES one of these, and the only way to get "no owner" is a method whose name says why.
//
// Enforced by: the required, non-nullable constructor parameter (compiler), and ContactCreateOwnerPolicyGuardTests, which
// fails the build when NoneForForeignEnvironment is used anywhere in server code except ContactIdentityBinderFactory.

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace Sprk.Bff.Api.Infrastructure.ExternalAccess;

/// <summary>
/// Who owns the contacts a <see cref="ContactIdentityBinder"/> creates on the Workforce plane. Build one with
/// <see cref="FromCustomerBusinessUnit"/> (the binder over THIS BFF's own environment) or
/// <see cref="NoneForForeignEnvironment"/> (a binder over another one); there is no other way to make one, and no default.
/// </summary>
public abstract class ContactCreateOwner
{
    // Private: the two variants below are the whole hierarchy, so the binder's switch over them is complete.
    private ContactCreateOwner()
    {
    }

    /// <summary>
    /// Every contact the binder creates is owned by the default owner team of <c>ExternalAccess:CustomerBusinessUnitId</c> and
    /// the create is refused (<c>sdap.access.registration_owner_unconfigured</c>) when that team cannot be resolved. This is
    /// the policy of the DI-registered binder, the one over this BFF's own Dataverse environment.
    /// </summary>
    /// <param name="options">Read on every create, so a changed setting applies without a restart.</param>
    /// <param name="configuration">Read for the Secure Record business unit's name (<c>SecureRecordOwnerTeam.BusinessUnitName</c>).</param>
    public static ContactCreateOwner FromCustomerBusinessUnit(
        IOptionsMonitor<ExternalAccessOptions> options, IConfiguration configuration)
        => new CustomerBusinessUnit(
            options ?? throw new ArgumentNullException(nameof(options)),
            configuration ?? throw new ArgumentNullException(nameof(configuration)));

    /// <summary>
    /// The binder creates contacts with NO owner. Only for a binder over a Dataverse environment other than this BFF's own,
    /// where <c>ExternalAccess:CustomerBusinessUnitId</c> names a business unit of a different environment and cannot apply.
    /// Used only by <see cref="ContactIdentityBinderFactory"/> (guarded by <c>ContactCreateOwnerPolicyGuardTests</c>); every
    /// other binder takes <see cref="FromCustomerBusinessUnit"/>. The gap this leaves is tracked in #2270.
    /// </summary>
    /// <param name="reason">Why no owner applies, logged at Debug with each create. Required.</param>
    public static ContactCreateOwner NoneForForeignEnvironment(string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        return new ForeignEnvironment(reason);
    }

    /// <summary>The customer business unit's default owner team owns every create; unresolved refuses it.</summary>
    internal sealed class CustomerBusinessUnit : ContactCreateOwner
    {
        internal CustomerBusinessUnit(IOptionsMonitor<ExternalAccessOptions> options, IConfiguration configuration)
        {
            Options = options;
            Configuration = configuration;
        }

        internal IOptionsMonitor<ExternalAccessOptions> Options { get; }

        internal IConfiguration Configuration { get; }
    }

    /// <summary>No owner is bound: the binder is over an environment the setting does not name.</summary>
    internal sealed class ForeignEnvironment : ContactCreateOwner
    {
        internal ForeignEnvironment(string reason) => Reason = reason;

        internal string Reason { get; }
    }
}
