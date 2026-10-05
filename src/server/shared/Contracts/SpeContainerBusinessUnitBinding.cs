// -----------------------------------------------------------------------------
// SpeContainerBusinessUnitBinding.cs — a SOURCE-LINKED contract (not a project).
//
// unified-access-control-r2 task 165, owner round 35 item 1: "The property name is ONE constant on each side (C# and
// PowerShell)." Every C# path that creates a SharePoint Embedded container stamps it with its owning business unit
// under this ONE custom-property name, and the BFF authorizes every admin route against it. Two deployables create
// containers — the BFF (Sprk.Bff.Api) and the L2 control plane (Sprk.Provisioning.ControlPlane.Core, handler H8) —
// and neither may reference the other (ADR-010 / customer-provisioning-orchestration-r1 MUST rule), nor can L2 take a
// ProjectReference on Spaarke.Core (that would drag Spaarke.Dataverse into the L2 publish). So this ONE file is
// compiled into both with <Compile Include="..." Link="..." />, the way L2 already embeds repo-root content files
// rather than duplicating them. It is `internal` in each assembly, so no assembly ever sees two public copies.
//
// The PowerShell side's ONE constant is $SpeContainerStampProperty in scripts/common/SpeContainerBinding.ps1.
// tests/Spaarke.ArchTests/SpeAdminContainerBindingGuardTests pins that this literal appears in exactly one .cs file
// under src/ and exactly one script under scripts/, and that the two are equal.
// -----------------------------------------------------------------------------

namespace Spaarke.Contracts.Spe;

/// <summary>
/// The SharePoint Embedded container → business-unit binding contract shared by every C# container-creation path.
/// </summary>
internal static class SpeContainerBusinessUnitBinding
{
    /// <summary>
    /// The <c>fileStorageContainer</c> custom property that carries the owning business unit's id (canonical <c>D</c>
    /// GUID form, <c>isSearchable: false</c>).
    /// </summary>
    public const string PropertyName = "spaarkeBusinessUnitId";
}
