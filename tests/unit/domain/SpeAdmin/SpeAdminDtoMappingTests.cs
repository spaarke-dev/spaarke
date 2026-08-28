using FluentAssertions;
using Sprk.Bff.Api.Api.SpeAdmin;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Models.SpeAdmin;
using Xunit;

namespace Sprk.Bff.Api.Tests.Domain.SpeAdmin;

/// <summary>
/// Pure-logic tests (ADR-038 §2 KEEP path #7 — <c>tests/unit/domain/**</c>) for the two hand-written
/// <c>FromDomain</c> mappers on the SPE Admin surface.
/// </summary>
/// <remarks>
/// <para>
/// <b>Relocated 2026-08-27 by task 042</b> from
/// <c>tests/unit/Sprk.Bff.Api.Tests/Integration/SpeAdmin/Phase2IntegrationTests.cs</c>, which was retired
/// as scaffolding. These four were the only tests in that 1,320-line file that called a real production
/// mapper rather than constructing a record and asserting the values it had just been handed. They are
/// kept, not deleted, because ADR-038 deletion-safety requires a replacement and neither task 040's
/// WireMock contract tier nor task 041's live seam tier covers either mapper.
/// </para>
/// <para>
/// <b>What breaks if these are deleted</b>: both mappers are hand-written field-by-field copies. A field
/// added to the domain record but forgotten in the mapper compiles cleanly and silently returns null to
/// the client — the exact defect shape this project exists to remove (a lower layer collapsing a real
/// value into an absent one the layer above reads as benign). It has already happened repeatedly here:
/// task 022 (<c>deletedDateTime</c> dropped on a type check), task 024 (storage discarded at four
/// sites), task 029 (<c>billingStatus</c> never requested at all).
/// </para>
/// <para>
/// Distinct from <c>SpeAdminContainerTypeOwnerTests</c> in the contract tier: that covers container-TYPE
/// owner grants (which PEOPLE own a type). <see cref="ContainerPermissionEndpoints.ContainerPermissionDto"/>
/// covers per-CONTAINER role grants (reader/writer/manager/owner). Task 027 established that these are
/// different collections in Graph, not two names for one thing.
/// </para>
/// </remarks>
public class SpeAdminDtoMappingTests
{
    // ── ContainerColumnDto.FromDomain ────────────────────────────────────────────────────────────

    [Fact]
    public void ContainerColumnDto_FromDomain_MapsEveryField()
    {
        // Every field gets a DISTINCT value — a mapper that transposes two fields, or drops one and
        // leaves the default, fails here. Uniform values would let both bugs through.
        var domainColumn = new SpeAdminGraphService.SpeContainerColumn(
            Id: "col-001",
            Name: "DocumentType",
            DisplayName: "Document Type",
            Description: "Classification of the document",
            ColumnType: "choice",
            Required: true,
            Indexed: true,
            ReadOnly: false);

        var dto = ContainerColumnDto.FromDomain(domainColumn);

        dto.Id.Should().Be("col-001");
        dto.Name.Should().Be("DocumentType");
        dto.DisplayName.Should().Be("Document Type");
        dto.Description.Should().Be("Classification of the document");
        dto.ColumnType.Should().Be("choice");
        dto.Required.Should().BeTrue();
        dto.Indexed.Should().BeTrue();
        dto.ReadOnly.Should().BeFalse();
    }

    [Fact]
    public void ContainerColumnDto_FromDomain_PropagatesNull_RatherThanSubstitutingADefault()
    {
        // Absent must stay absent. Substituting "" for a null description would make an unset field
        // indistinguishable from one deliberately set to empty.
        var systemColumn = new SpeAdminGraphService.SpeContainerColumn(
            Id: "sys-001",
            Name: "Title",
            DisplayName: "Title",
            Description: null,
            ColumnType: "text",
            Required: false,
            Indexed: true,
            ReadOnly: true);

        var dto = ContainerColumnDto.FromDomain(systemColumn);

        dto.Description.Should().BeNull("an absent description must not become an empty string");
        dto.ReadOnly.Should().BeTrue("system-managed columns must stay flagged read-only through the mapper");
    }

    // ── ContainerPermissionDto.FromDomain ────────────────────────────────────────────────────────

    [Fact]
    public void ContainerPermissionDto_FromDomain_MapsEveryField()
    {
        var domain = new SpeAdminGraphService.SpeContainerPermission(
            Id: "perm-001",
            Role: "writer",
            DisplayName: "Jane Smith",
            Email: "jane@contoso.com",
            PrincipalId: "aad-obj-001",
            PrincipalType: "user");

        var dto = ContainerPermissionEndpoints.ContainerPermissionDto.FromDomain(domain);

        dto.Id.Should().Be("perm-001");
        dto.Role.Should().Be("writer");
        dto.DisplayName.Should().Be("Jane Smith");
        dto.Email.Should().Be("jane@contoso.com");
        dto.PrincipalId.Should().Be("aad-obj-001");
        dto.PrincipalType.Should().Be("user");
    }

    [Fact]
    public void ContainerPermissionDto_FromDomain_LeavesServicePrincipalFieldsNull_NotEmptyStrings()
    {
        // A service principal genuinely has no email and may have no display name. Rendering "" for
        // those would make the grid show a blank cell that reads as "we looked and found nothing"
        // rather than "this principal type has no such field".
        var domain = new SpeAdminGraphService.SpeContainerPermission(
            Id: "perm-sp-001",
            Role: "reader",
            DisplayName: null,
            Email: null,
            PrincipalId: "sp-obj-001",
            PrincipalType: "application");

        var dto = ContainerPermissionEndpoints.ContainerPermissionDto.FromDomain(domain);

        dto.DisplayName.Should().BeNull("service principals may have no display name");
        dto.Email.Should().BeNull("service principals do not have email addresses");
        dto.PrincipalType.Should().Be("application");
    }
}
