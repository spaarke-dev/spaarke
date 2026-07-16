// spaarkeai-assistant-enhancements-r1 task 030 (FR-E2): deterministic renderer for the User-scope
// STATED-profile block folded into ContextEnvelope.userFragment. Read (StatedProfileReader) ≠ render
// (this). Fields render in a FIXED order and multi-selects in a stable ordinal order so the block is
// deterministic turn-to-turn. Byte-stability pinning + the EnvelopeBudget.User amendment + the eval
// re-baseline are TASK 032 — this renderer only guarantees deterministic (not byte-frozen) output.

using System.Text;

namespace Sprk.Bff.Api.Services.Ai.Context;

/// <summary>
/// Renders a <see cref="StatedProfile"/> into a compact User-slice prompt block. Pure function of its
/// input (no clock, no I/O, no runtime model judgement) — the same profile always renders the same string.
/// Returns <c>null</c> when the profile carries no stated facts (<see cref="StatedProfile.HasAny"/> false),
/// so an empty profile folds to nothing in the User fragment.
/// </summary>
public static class StatedProfileRenderer
{
    /// <summary>The stated-profile block heading (clearly user-scoped, distinct from the memory-recall heading).</summary>
    private const string Heading = "### Your Profile (stated)";

    /// <summary>
    /// Renders the stated-profile block. Fixed field order: Role → Practice Areas → Focus Areas → Office →
    /// Assistant Preferences. Only populated fields render. Practice-area names render in ordinal order (the
    /// reader already sorts them). Returns <c>null</c> for a null or empty profile.
    /// </summary>
    public static string? Render(StatedProfile? profile)
    {
        if (profile is null || !profile.HasAny)
        {
            return null;
        }

        var sb = new StringBuilder();
        sb.Append(Heading);

        if (!string.IsNullOrWhiteSpace(profile.RoleLabel))
        {
            sb.Append("\n- Role: ").Append(profile.RoleLabel!.Trim());
        }

        if (profile.PracticeAreaNames.Count > 0)
        {
            sb.Append("\n- Practice Areas: ").Append(string.Join(", ", profile.PracticeAreaNames));
        }

        if (!string.IsNullOrWhiteSpace(profile.FocusAreas))
        {
            sb.Append("\n- Focus Areas: ").Append(profile.FocusAreas!.Trim());
        }

        if (!string.IsNullOrWhiteSpace(profile.OfficeLocation))
        {
            sb.Append("\n- Office: ").Append(profile.OfficeLocation!.Trim());
        }

        if (!string.IsNullOrWhiteSpace(profile.AssistantPreferences))
        {
            sb.Append("\n- Assistant Preferences: ").Append(profile.AssistantPreferences!.Trim());
        }

        return sb.ToString();
    }
}
