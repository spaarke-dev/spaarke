// -----------------------------------------------------------------------------
// InvariantProbeDeferralMessages.cs
//
// Static messages used by <see cref="CompositeInvariantVerifier"/> when a given
// <see cref="InvariantKind"/> has no registered <see cref="IInvariantProbe"/>.
// Preserved effectively equivalent to the retired
// <see cref="PlaceholderInvariantVerifier"/>'s deferral diagnostic so
// log-scraping tooling behaviour is unchanged pre/post migration to the
// composite pattern.
//
// Added by task 174 (Wave G-7) alongside <see cref="CompositeInvariantVerifier"/>.
// -----------------------------------------------------------------------------

namespace Sprk.Provisioning.ControlPlane.Handlers.E2EAcceptance;

/// <summary>
/// Static holder for the deferral diagnostic shared between
/// <see cref="CompositeInvariantVerifier"/> and any probe that needs to surface
/// the same "not-yet-wired" message.
/// </summary>
internal static class InvariantProbeDeferralMessages
{
    /// <summary>
    /// The deferral diagnostic used when an <see cref="InvariantKind"/> has no
    /// registered <see cref="IInvariantProbe"/>.
    /// </summary>
    public const string DeferralDiagnostic =
        "H13 invariant live-probe not yet wired in L2 for this kind (composite verifier has no IInvariantProbe registered). " +
        "Handler classifies Resumable per §4C. The per-invariant real probe is landing under its own Wave G-7 sibling task " +
        "(170 I1 / 173 I2 / 174 I3 / 176 I4 / 179 I5); once every probe is registered, this diagnostic disappears.";
}
