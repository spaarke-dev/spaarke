using Sprk.Bff.Api.Models.SpeAdmin;

namespace Sprk.Bff.Api.Services.SpeAdmin;

/// <summary>
/// THE container → business-unit binding (unified-access-control-r2 task 165, owner round 20): every SharePoint
/// Embedded container carries the business unit that owns it as a <c>fileStorageContainer</c> custom property,
/// <see cref="PropertyName"/>, stamped by the BFF when it creates the container and backfilled for existing ones
/// (<c>scripts/Backfill-SpeContainerBusinessUnitStamp.ps1</c>).
/// </summary>
/// <remarks>
/// <para>
/// <b>Why it exists.</b> One container TYPE serves many customers (container-type topology §3, Model 1), and
/// Graph has no notion of which customer a container is "for". Routes bound only to a container type let one
/// customer's administrator reach another customer's containers of the same type. The stamp is the authoritative
/// binding the admin plane authorizes against, PER CONTAINER: a container bound to a unit an admin can reach (their
/// own unit or a descendant); an UNBOUND container by no admin route at all (owner round 35 item 2 — root-unit admins
/// included); an unreadable binding fails closed (<see cref="SpeAdminCallerScope.CanReach"/>).
/// </para>
/// <para>
/// <b>Server-owned, and written by EVERY creation path</b> (round 35 item 1): the BFF's two creation paths (SPE admin
/// plane, secure-record provisioning), the L2 control plane's H8 root container, and the operator scripts
/// (<c>New-BusinessUnitContainer.ps1</c>, <c>Provision-Customer.ps1</c>, <c>Create-NewContainerType.ps1</c>) — each
/// reads the stamp back and removes the container if it did not land — plus the backfill script. The SPE admin
/// custom-property route refuses to set or remove it (<see cref="IsReserved"/>), otherwise an administrator could
/// re-bind a container into another customer's view. The name is ONE C# constant shared with L2
/// (<c>Spaarke.Contracts.Spe.SpeContainerBusinessUnitBinding</c>) and ONE PowerShell constant
/// (<c>scripts/common/SpeContainerBinding.ps1</c>); <c>SpeAdminContainerBindingGuardTests</c> pins both.
/// </para>
/// <para>
/// <b>Reading it.</b> Graph returns <c>customProperties</c> on a single-container GET with <c>$select</c> only: on the
/// containers COLLECTION it is accepted, echoed in <c>@odata.context</c>, and silently dropped (measured read-only
/// on dev 2026-10-04, beta and v1.0, five containers — the same shape task 028 measured for <c>drive</c>). Every
/// binding decision therefore reads the container itself.
/// </para>
/// </remarks>
public static class SpeContainerBusinessUnitStamp
{
    /// <summary>
    /// The custom property that carries the owning business unit's id (canonical <c>D</c> GUID form) — the ONE C#
    /// constant, shared with the L2 control plane through the source-linked contract.
    /// </summary>
    public const string PropertyName = Spaarke.Contracts.Spe.SpeContainerBusinessUnitBinding.PropertyName;

    /// <summary>
    /// The binding a container's custom properties express: bound to a business unit, unbound (no stamp), or
    /// malformed (a stamp that is not one non-empty GUID — treated as unbound: no admin route reaches it).
    /// </summary>
    public static SpeContainerBinding Read(IReadOnlyList<CustomPropertyDto>? properties)
    {
        if (properties is null || properties.Count == 0)
        {
            return SpeContainerBinding.Unbound;
        }

        var stamps = properties.Where(p => IsReserved(p.Name)).ToList();
        if (stamps.Count == 0)
        {
            return SpeContainerBinding.Unbound;
        }

        var values = stamps
            .Select(p => Guid.TryParse(p.Value?.Trim(), out var unit) && unit != Guid.Empty ? unit : (Guid?)null)
            .ToList();

        // Two spellings of the property name carrying different values cannot be resolved to one owner.
        if (values.Any(v => v is null) || values.Distinct().Count() != 1)
        {
            return SpeContainerBinding.Malformed;
        }

        return SpeContainerBinding.BoundTo(values[0]!.Value);
    }

    /// <summary>The stamp for <paramref name="businessUnitId"/>, as written to the container.</summary>
    public static CustomPropertyDto ToProperty(Guid businessUnitId) =>
        new(PropertyName, businessUnitId.ToString("D"), IsSearchable: false);

    /// <summary>Whether <paramref name="propertyName"/> is the stamp (trimmed, case-insensitive).</summary>
    public static bool IsReserved(string? propertyName) =>
        string.Equals(propertyName?.Trim(), PropertyName, StringComparison.OrdinalIgnoreCase);
}

/// <summary>A container's business-unit binding, as read from its stamp.</summary>
/// <param name="BusinessUnitId">The owning business unit, or null when the container is unbound or malformed.</param>
/// <param name="IsMalformed">The stamp exists but does not name one business unit.</param>
public readonly record struct SpeContainerBinding(Guid? BusinessUnitId, bool IsMalformed)
{
    /// <summary>No stamp: the container predates stamping (the backfill binds it) or was created outside the BFF.</summary>
    public static SpeContainerBinding Unbound => default;

    /// <summary>A stamp that names no single business unit.</summary>
    public static SpeContainerBinding Malformed => new(null, true);

    /// <summary>Bound to <paramref name="businessUnitId"/>.</summary>
    public static SpeContainerBinding BoundTo(Guid businessUnitId) => new(businessUnitId, false);
}

/// <summary>
/// One single-container read of the binding (<c>GET …/containers/{id}?$select=id,containerTypeId,customProperties</c>,
/// or the same on <c>deletedContainers</c>).
/// </summary>
/// <param name="ContainerId">The container id Graph reported.</param>
/// <param name="ContainerTypeId">The container's type as Graph reported it, or null when not reported.</param>
/// <param name="Binding">The business-unit binding.</param>
public sealed record SpeContainerBindingRead(string ContainerId, string? ContainerTypeId, SpeContainerBinding Binding);
