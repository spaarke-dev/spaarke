using FluentAssertions;
using Sprk.Bff.Api.Services.Ai.Context;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai.Context;

/// <summary>
/// spaarkeai-assistant-enhancements-r1 FR-E5 BU/team (un-defer D-032-01): byte-stability PIN for the org
/// (business-unit + team) block rendered by <see cref="UserOrgContextRenderer"/>. The block folds into the
/// User slice's stable-prefix fragment as its OWN block, so its render MUST be a pure, byte-stable function
/// of the context — fixed field order (Business Unit → Teams), team names in the reader's Ordinal order
/// (the reader owns the sort). A drift in the rendered bytes moves the prompt-cache prefix (NFR-04), so
/// this test freezes the exact string. Cheap byte-equality assertions (the same discipline as
/// <see cref="StatedProfileRendererTests"/>), not cache machinery.
/// </summary>
public sealed class UserOrgContextRendererTests
{
    private const string Heading = "### Your Organization";

    private static UserOrgContext FullContext() => new()
    {
        BusinessUnitName = "Litigation Group",
        TeamNames = new[] { "Corporate", "Employment", "Litigation" },
    };

    [Fact]
    public void Render_FullContext_ProducesTheByteFrozenBlock()
    {
        // Byte-frozen golden. Fixed field order: Business Unit → Teams. A change to this string is a
        // prompt-cache-prefix change (NFR-04) and MUST be paired with an eval prefix re-baseline.
        const string expected =
            Heading +
            "\n- Business Unit: Litigation Group" +
            "\n- Teams: Corporate, Employment, Litigation";

        UserOrgContextRenderer.Render(FullContext()).Should().Be(expected);
    }

    [Fact]
    public void Render_SameContextTwice_IsByteIdentical()
    {
        // Determinism pin: pure function of the input — no clock/GUID/map-order variance across renders.
        UserOrgContextRenderer.Render(FullContext())
            .Should().Be(UserOrgContextRenderer.Render(FullContext()),
                "the org block is a pure, byte-stable function of the context (NFR-02)");
    }

    [Fact]
    public void Render_PreservesGivenTeamOrder_DoesNotReSort()
    {
        // The renderer joins team names in the ORDER PROVIDED — the reader (UserOrgContextReader) owns the
        // Ordinal sort. Passing a deliberately non-alphabetical order proves the renderer does not re-order.
        var context = new UserOrgContext { TeamNames = new[] { "Zoning", "Antitrust" } };

        UserOrgContextRenderer.Render(context)
            .Should().Be(Heading + "\n- Teams: Zoning, Antitrust");
    }

    [Fact]
    public void Render_BusinessUnitOnly_OmitsTeamsLine()
    {
        UserOrgContextRenderer.Render(new UserOrgContext { BusinessUnitName = "Corporate BU" })
            .Should().Be(Heading + "\n- Business Unit: Corporate BU");
    }

    [Fact]
    public void Render_TeamsOnly_OmitsBusinessUnitLine()
    {
        UserOrgContextRenderer.Render(new UserOrgContext { TeamNames = new[] { "Litigation" } })
            .Should().Be(Heading + "\n- Teams: Litigation");
    }

    [Fact]
    public void Render_NullContext_ReturnsNull()
    {
        UserOrgContextRenderer.Render(null).Should().BeNull();
    }

    [Fact]
    public void Render_EmptyContext_ReturnsNull()
    {
        // A context with no BU and no teams folds to nothing (HasAny == false).
        UserOrgContextRenderer.Render(new UserOrgContext()).Should().BeNull();
    }
}
