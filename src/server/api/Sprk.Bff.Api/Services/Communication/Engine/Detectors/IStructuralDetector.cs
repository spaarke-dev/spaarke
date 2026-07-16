using Sprk.Bff.Api.Services.Communication.Models;

namespace Sprk.Bff.Api.Services.Communication.Engine.Detectors;

/// <summary>
/// A structural detector (rung 3): inspects the NORMALIZED envelope (subject, body, attachments,
/// headers) for ONE deterministic structural signal — a calendar invite, an e-sign completion, an
/// invoice number, a court/e-filing notice — and returns a <see cref="StructuralMatch"/> when the
/// signal is present. Channel-neutral and pure (no I/O). Adding a new detector MUST NOT require any
/// engine change (NFR-04): the rung-3 aggregator injects all registered detectors.
/// </summary>
public interface IStructuralDetector
{
    /// <summary>Stable detector name (used in provenance + logging).</summary>
    string Name { get; }

    /// <summary>Returns a structural match when the signal is present; null otherwise.</summary>
    StructuralMatch? Detect(NormalizedMessage message);
}
