using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Sprk.Bff.Api.Services.Communication.Engine.Detectors;
using Sprk.Bff.Api.Services.Communication.Models;

namespace Sprk.Bff.Api.Services.Communication.Engine.Rungs;

/// <summary>
/// Rung 3 — structural detectors. Runs every registered <see cref="IStructuralDetector"/> over the
/// envelope and lifts each <see cref="StructuralMatch"/> into a <see cref="RungMatch"/> in the
/// 0.70–0.95 band, carrying the resolved category + obligations (metadata that feeds task 015 / W5
/// Responsive Intelligence). Detectors that resolve a specific record set the regarding target;
/// category/obligation-only detectors emit metadata-only matches (the engine does not treat those as
/// resolving the association). Runs symmetrically inbound + outbound (detectors read only the envelope).
/// </summary>
/// <remarks>
/// Extensibility (NFR-04): the detectors are DI-injected; adding a new structural signal is a new
/// <see cref="IStructuralDetector"/> registration — no change to this rung or the engine.
/// </remarks>
public sealed class StructuralDetectorRung : IAssociationRung
{
    private readonly IReadOnlyList<IStructuralDetector> _detectors;
    private readonly ILogger<StructuralDetectorRung> _logger;

    public StructuralDetectorRung(
        IEnumerable<IStructuralDetector> detectors,
        ILogger<StructuralDetectorRung>? logger = null)
    {
        _detectors = detectors.ToArray();
        _logger = logger ?? NullLogger<StructuralDetectorRung>.Instance;
    }

    public RungKind Kind => RungKind.StructuralDetector;
    public int Order => 3;

    public Task<IReadOnlyList<RungMatch>> EvaluateAsync(
        NormalizedMessage message, AssociationContext context, CancellationToken ct)
    {
        var matches = new List<RungMatch>();

        foreach (var detector in _detectors)
        {
            var detection = detector.Detect(message);
            if (detection is null)
                continue;

            matches.Add(new RungMatch
            {
                RegardingFieldName = detection.RegardingFieldName,
                Target = detection.Target,
                Confidence = detection.Confidence,
                Provenance = detection.Provenance,
                Rung = RungKind.StructuralDetector,
                Category = detection.Category,
                Obligations = detection.Obligations,
            });
        }

        // Observability (task 014): the metadata-only signals are not yet persisted/consumed (W5/052 is
        // gated), and the cascade drops them when an earlier rung resolves — so log them here to keep the
        // detected category/obligations visible before a consumer exists.
        if (matches.Count > 0)
        {
            _logger.LogInformation(
                "Structural detectors fired | Direction: {Direction}, Signals: {Signals}",
                message.Direction,
                string.Join("; ", matches.Select(m =>
                    $"{m.Category}[{string.Join(",", m.Obligations)}]@{m.Confidence:F2} ({m.Provenance})")));
        }

        return Task.FromResult<IReadOnlyList<RungMatch>>(matches);
    }
}
