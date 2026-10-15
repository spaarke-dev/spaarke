// -----------------------------------------------------------------------------
// SpeContainerCustomerMarker.cs — a SOURCE-LINKED contract (not a project), the same mechanism as
// SpeContainerBusinessUnitBinding.cs next to it.
//
// customer-provisioning-orchestration-r1 task 227d (owner D28/D29) + 227e: every Model 1 stamp's identities hold
// application `full` on ONE shared container type, so an app-only token reaches every customer's containers. The BFF
// (SpeContainerOwnershipGuard) treats a container as its customer's only when this custom property equals its
// Customer:Id; L2's H8 writes it on the customer's container, and the BFF writes it on every container it creates.
// Two deployables, one name: this file is compiled into both with <Compile Include="..." Link="..." /> and is
// `internal` in each. tests/Spaarke.ArchTests/TenantIsolation/SpeContainerMarkerParityTests pins that the literal
// appears in exactly this one .cs file under src/.
// -----------------------------------------------------------------------------

namespace Spaarke.Contracts.Spe;

/// <summary>The SharePoint Embedded container → customer ownership marker shared by the BFF and L2.</summary>
internal static class SpeContainerCustomerMarker
{
    /// <summary>
    /// The <c>fileStorageContainer</c> custom property whose value is the owning customer's id (<c>Customer__Id</c> on
    /// the customer's BFF; the run's customerId in L2), written with <c>isSearchable: false</c>.
    /// </summary>
    public const string PropertyName = "spaarkeCustomerId";
}
