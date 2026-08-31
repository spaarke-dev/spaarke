using FluentAssertions;
using Spaarke.Dataverse;
using Xunit;

namespace Sprk.Bff.Api.Tests.Domain.Dataverse;

/// <summary>
/// The rights-mapping half of finding A-20 / spec FR-04 (unified-access-control-r2 task 005).
///
/// <para>Before task 005, <c>DataverseAccessDataSource.QueryUserPermissionsAsync</c> returned a
/// hard-coded <see cref="AccessRights.Read"/> on success — it probed "can this principal retrieve the
/// record?" and reasoned "yes, therefore Read". Every policy requiring more than Read was therefore
/// unsatisfiable in production, however privileged the caller. It now calls Dataverse's
/// <c>RetrievePrincipalAccess</c> and maps the answer through
/// <see cref="DataverseAccessRightsMapper"/>.</para>
///
/// <para>This suite is FR-04's acceptance criterion 5 — "the snapshot never exceeds the caller's actual
/// Dataverse rights, asserted by test with a mocked Dataverse answer". The mapper is the point where a
/// Dataverse answer becomes rights, so it is the only place that criterion can be asserted directly:
/// the surrounding call is HTTP (mocking that transport is ADR-038 ban B1) and the logic was private
/// (reflecting into it is ban B8). Pure function, no I/O — hence <c>tests/unit/domain/**</c>.</para>
/// </summary>
public class DataverseAccessRightsMapperTests
{
    // ─────────────────────────────────────────────────────────────────────────────
    // Each right maps to its own flag, and ONLY its own flag.
    // ─────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("ReadAccess", AccessRights.Read)]
    [InlineData("WriteAccess", AccessRights.Write)]
    [InlineData("DeleteAccess", AccessRights.Delete)]
    [InlineData("CreateAccess", AccessRights.Create)]
    [InlineData("AppendAccess", AccessRights.Append)]
    [InlineData("AppendToAccess", AccessRights.AppendTo)]
    [InlineData("ShareAccess", AccessRights.Share)]
    public void FromAccessRightsString_ForSingleRight_MapsToExactlyThatFlag(string dataverseRight, AccessRights expected)
    {
        var result = DataverseAccessRightsMapper.FromAccessRightsString(dataverseRight);

        result.Should().Be(expected,
            "'{0}' must map to exactly {1} — no more (that would over-grant) and no less (that would " +
            "deny a legitimately authorized caller)", dataverseRight, expected);
    }

    /// <summary>
    /// <c>AppendAccess</c> and <c>AppendToAccess</c> are one character apart and mean opposite things:
    /// Append is "this record can be attached to others", AppendTo is "others can be attached to this
    /// record". Task 003 gated <c>entity.associate_document</c> (POST /api/office/save) on AppendTo and
    /// recorded the transposition as the specific failure mode to guard — it would leave that route
    /// permanently 403 while looking fixed, because the denial reads as legitimate insufficient_rights.
    /// </summary>
    [Fact]
    public void FromAccessRightsString_ForAppendVersusAppendTo_DoesNotConflateThem()
    {
        var append = DataverseAccessRightsMapper.FromAccessRightsString("AppendAccess");
        var appendTo = DataverseAccessRightsMapper.FromAccessRightsString("AppendToAccess");

        append.Should().Be(AccessRights.Append);
        append.Should().NotHaveFlag(AccessRights.AppendTo,
            "AppendAccess must NOT confer AppendTo — POST /api/office/save is gated on AppendTo (task 003)");

        appendTo.Should().Be(AccessRights.AppendTo);
        appendTo.Should().NotHaveFlag(AccessRights.Append);
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Combinations — the shape Dataverse actually returns.
    // ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void FromAccessRightsString_ForTypicalDataverseAnswer_MapsEveryRight()
    {
        // The shape RetrievePrincipalAccess returns for a user with a Write-capable role.
        var result = DataverseAccessRightsMapper.FromAccessRightsString(
            "ReadAccess,WriteAccess,AppendAccess,AppendToAccess,ShareAccess");

        result.Should().Be(
            AccessRights.Read | AccessRights.Write | AccessRights.Append
            | AccessRights.AppendTo | AccessRights.Share);

        result.Should().NotHaveFlag(AccessRights.Delete, "DeleteAccess was not in the answer");
        result.Should().NotHaveFlag(AccessRights.Create, "CreateAccess was not in the answer");
    }

    [Fact]
    public void FromAccessRightsString_WithSurroundingWhitespace_StillMaps()
    {
        var result = DataverseAccessRightsMapper.FromAccessRightsString("ReadAccess, WriteAccess ,DeleteAccess");

        result.Should().Be(AccessRights.Read | AccessRights.Write | AccessRights.Delete);
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Fail closed — the half that matters most for a security mapping.
    // ─────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void FromAccessRightsString_WhenAnswerIsAbsent_ReturnsNone(string? answer)
    {
        DataverseAccessRightsMapper.FromAccessRightsString(answer).Should().Be(AccessRights.None,
            "an absent answer is an authoritative 'no rights' — it must never default to a grant");
    }

    /// <summary>
    /// An unrecognised right name contributes nothing. Dataverse can add rights we do not model, and a
    /// name we cannot interpret must never widen a decision — nor throw, which on this path would turn
    /// an authorization question into a 500.
    /// </summary>
    [Fact]
    public void FromAccessRightsString_WithUnrecognisedRight_IgnoresItWithoutThrowing()
    {
        var result = DataverseAccessRightsMapper.FromAccessRightsString("ReadAccess,SomeFutureAccess");

        result.Should().Be(AccessRights.Read, "the unknown name contributes nothing");
    }

    /// <summary>
    /// FR-04 acceptance criterion 5, stated directly: the mapped rights are always a subset of what
    /// Dataverse named. Expressed as a property over every subset of the seven rights rather than as a
    /// handful of examples, so a future edit that ORs in an extra flag anywhere fails here.
    /// </summary>
    [Fact]
    public void FromAccessRightsString_NeverReturnsRightsDataverseDidNotName()
    {
        var names = new[]
        {
            ("ReadAccess", AccessRights.Read),
            ("WriteAccess", AccessRights.Write),
            ("DeleteAccess", AccessRights.Delete),
            ("CreateAccess", AccessRights.Create),
            ("AppendAccess", AccessRights.Append),
            ("AppendToAccess", AccessRights.AppendTo),
            ("ShareAccess", AccessRights.Share)
        };

        // All 128 subsets of the seven rights.
        for (var mask = 0; mask < 1 << 7; mask++)
        {
            var selected = names.Where((_, i) => (mask & (1 << i)) != 0).ToList();

            var expected = selected.Aggregate(AccessRights.None, (acc, n) => acc | n.Item2);
            var actual = DataverseAccessRightsMapper.FromAccessRightsString(
                string.Join(',', selected.Select(n => n.Item1)));

            actual.Should().Be(expected,
                "the mapping must be exactly the named rights — subset {0}", string.Join('|', selected.Select(n => n.Item1)));
        }
    }
}
