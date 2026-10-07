using FluentAssertions;
using Spaarke.Dataverse;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Access;

/// <summary>
/// The principal parameterization behind the consolidated POA share seam (unified-access-control-r2
/// task 060).
/// </summary>
/// <remarks>
/// This is the substitution that let two POA clients become one: instead of a systemuser-only method
/// and a team-only copy, one method takes a <see cref="DataversePrincipalRef"/>. Everything about
/// whether a share lands on the RIGHT principal now routes through the two mappings asserted here, so
/// they are behaviour, not wiring — a wrong entity set silently shares a record to a different
/// principal type, and a wrong type code silently mis-reads who holds a share.
/// </remarks>
public class DataversePrincipalRefTests
{
    [Theory]
    [InlineData(DataversePrincipalKind.SystemUser, "systemusers")]
    [InlineData(DataversePrincipalKind.Team, "teams")]
    public void ToEntitySet_MapsEachKindToItsWebApiEntitySet(DataversePrincipalKind kind, string expected)
    {
        kind.ToEntitySet().Should().Be(expected);
    }

    [Fact]
    public void ToEntitySet_UnmodelledKind_ThrowsRatherThanAddressingSomethingArbitrary()
    {
        var undefined = (DataversePrincipalKind)4; // 'account' — a real POA principal type this seam does not model

        var act = () => undefined.ToEntitySet();

        act.Should().Throw<ArgumentOutOfRangeException>(
            "failing loud is the point: silently defaulting to systemusers would grant a record to a "
            + "principal the caller never named");
    }

    [Fact]
    public void KindValues_AreDataversesOwnPrincipalTypeCodes()
    {
        // The enum doubles as principaltypecode, which is why the POA read needs no lookup table.
        // If these drift from Dataverse's numbers, GetPrincipalAccessAsync starts skipping real rows.
        ((int)DataversePrincipalKind.SystemUser).Should().Be(8);
        ((int)DataversePrincipalKind.Team).Should().Be(9);
    }

    [Theory]
    [InlineData(8, DataversePrincipalKind.SystemUser)]
    [InlineData(9, DataversePrincipalKind.Team)]
    public void FromPrincipalTypeCode_MapsTheKindsTheSeamModels(int code, DataversePrincipalKind expected)
    {
        DataversePrincipalRefExtensions.FromPrincipalTypeCode(code).Should().Be(expected);
    }

    [Theory]
    [InlineData(1)]    // account
    [InlineData(4)]    // contact
    [InlineData(0)]
    public void FromPrincipalTypeCode_UnmodelledCode_ReturnsNullSoTheRowIsSkippedNotGuessed(int code)
    {
        DataversePrincipalRefExtensions.FromPrincipalTypeCode(code).Should().BeNull(
            "a POA row whose principal type this seam does not model must be skipped — reporting it as a "
            + "user or a team is how the pre-060 reads mis-classified shares");
    }

    [Fact]
    public void FactoryHelpers_ProduceTheMatchingKind()
    {
        var userId = Guid.NewGuid();
        var teamId = Guid.NewGuid();

        DataversePrincipalRef.User(userId).Should().Be(
            new DataversePrincipalRef(DataversePrincipalKind.SystemUser, userId));
        DataversePrincipalRef.Team(teamId).Should().Be(
            new DataversePrincipalRef(DataversePrincipalKind.Team, teamId));
    }

    [Fact]
    public void PrincipalRef_IsValueEqual_SoGrantAndRevokeMatchOnTheSameKey()
    {
        // The A-13/FR-16 matcher lesson: a revoke keyed differently from its grant silently fails to
        // remove the row it was meant to. Value equality is what makes "the same key shape" checkable.
        var id = Guid.NewGuid();

        DataversePrincipalRef.User(id).Should().Be(DataversePrincipalRef.User(id));
        DataversePrincipalRef.User(id).Should().NotBe(DataversePrincipalRef.Team(id),
            "same id, different principal kind — these address different principals");
    }
}
