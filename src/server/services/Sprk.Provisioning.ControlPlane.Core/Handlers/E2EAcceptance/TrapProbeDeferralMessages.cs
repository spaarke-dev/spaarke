// -----------------------------------------------------------------------------
// TrapProbeDeferralMessages.cs
//
// Static messages used by <see cref="CompositeTrapVerifier"/> when a given
// <see cref="TrapKind"/> has no registered <see cref="ITrapProbe"/>. Preserved
// effectively equivalent to the retired <see cref="PlaceholderTrapVerifier"/>'s
// deferral diagnostic so log-scraping tooling behaviour is unchanged pre/post
// migration to the composite pattern.
//
// Added by task 185 (Phase C'' Wave G-7 Batch G-7D) alongside
// <see cref="CompositeTrapVerifier"/>.
// -----------------------------------------------------------------------------

namespace Sprk.Provisioning.ControlPlane.Handlers.E2EAcceptance;

/// <summary>
/// Static holder for the deferral diagnostic shared between
/// <see cref="CompositeTrapVerifier"/> and any probe that needs to surface the
/// same "not-yet-wired" message.
/// </summary>
internal static class TrapProbeDeferralMessages
{
    /// <summary>
    /// The deferral diagnostic used when a <see cref="TrapKind"/> has no
    /// registered <see cref="ITrapProbe"/>.
    /// </summary>
    public const string DeferralDiagnostic =
        "H13 trap live-probe not yet wired in L2 for this kind (composite verifier has no ITrapProbe registered). " +
        "Handler classifies Resumable per §4C. The per-trap real probe is landing under its own Wave G-7 sibling task " +
        "(171 T1 / 177 T2 / 178 T3 / 180 T4 / 172 T5 / 175 T6); once every probe is registered, this diagnostic disappears.";
}
