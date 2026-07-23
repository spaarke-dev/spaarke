// spaarkeai-assistant-enhancements-r1 FR-E5 BU/team enrichment (un-defer D-032-01): deterministic renderer
// for the User-scope ORG (business-unit + team) block folded into ContextEnvelope.userFragment. Read
// (UserOrgContextReader) ≠ render (this). A DIFFERENT source than the stated profile (systemuser org
// membership, not sprk_userprofile) — it renders as its OWN deterministic block. Fields render in a FIXED
// order and team names in the reader's Ordinal order so the block is byte-stable turn-to-turn (NFR-02/04).
//
// BU/team are system-owned names (not user-authored free text), so — unlike StatedProfileRenderer's
// «...»-wrapped Focus Areas / Assistant Preferences — no untrusted-content guard line / guillemet wrapping
// is needed here. The hard preference≠permission guarantee (ADR-039) is enforced upstream: this block only
// ever reaches the User fragment, never AgentToolFilterContext / grounding / dispatch.

using System.Text;

namespace Sprk.Bff.Api.Services.Ai.Context;

/// <summary>
/// Renders a <see cref="UserOrgContext"/> into a compact User-slice prompt block. Pure function of its
/// input (no clock, no I/O, no runtime model judgement) — the same context always renders the same string.
/// Returns <c>null</c> when the context carries no org facts (<see cref="UserOrgContext.HasAny"/> false),
/// so an org-less user folds to nothing in the User fragment.
/// </summary>
public static class UserOrgContextRenderer
{
    /// <summary>The org block heading (clearly user-scoped, distinct from the stated-profile + memory-recall headings).</summary>
    private const string Heading = "### Your Organization";

    /// <summary>
    /// Renders the org block. Fixed field order: Business Unit → Teams. Only populated fields render. Team
    /// names render in the reader's Ordinal order (the reader owns the sort). Returns <c>null</c> for a null
    /// or empty context.
    /// </summary>
    public static string? Render(UserOrgContext? context)
    {
        if (context is null || !context.HasAny)
        {
            return null;
        }

        var sb = new StringBuilder();
        sb.Append(Heading);

        if (!string.IsNullOrWhiteSpace(context.BusinessUnitName))
        {
            sb.Append("\n- Business Unit: ").Append(context.BusinessUnitName!.Trim());
        }

        if (context.TeamNames.Count > 0)
        {
            sb.Append("\n- Teams: ").Append(string.Join(", ", context.TeamNames));
        }

        return sb.ToString();
    }
}
