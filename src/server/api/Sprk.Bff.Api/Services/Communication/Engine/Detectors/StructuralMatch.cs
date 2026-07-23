using Microsoft.Xrm.Sdk;

namespace Sprk.Bff.Api.Services.Communication.Engine.Detectors;

/// <summary>
/// The result of a structural detector: a resolved content <see cref="Category"/>, optional
/// <see cref="Obligations"/> (feed W5 Responsive Intelligence, FR-19), an optional resolved regarding
/// association (<see cref="RegardingFieldName"/> + <see cref="Target"/> when the detector matches a
/// specific record), plus confidence + provenance. Rung 3 lifts this into a <see cref="RungMatch"/>.
/// </summary>
/// <remarks>
/// In task 014 the detectors resolve category + obligations deterministically from message structure;
/// specific entity-target resolution (which <c>sprk_invoice</c> / <c>sprk_event</c>) needs
/// number/UID lookup queries that are a documented follow-up, so <see cref="Target"/> is null for now.
/// </remarks>
public sealed record StructuralMatch
{
    /// <summary>Resolved content category — e.g. <c>event</c>, <c>invoice</c>, <c>esign-completion</c>, <c>court-notice</c>.</summary>
    public required string Category { get; init; }

    /// <summary>Regarding lookup field when a specific record is resolved; null for a category/obligation-only signal.</summary>
    public string? RegardingFieldName { get; init; }

    /// <summary>Typed regarding target when a specific record is resolved; null otherwise.</summary>
    public EntityReference? Target { get; init; }

    /// <summary>Resolved obligations — e.g. <c>executed-document</c>, <c>deadline-response</c>, <c>calendar-response</c>.</summary>
    public IReadOnlyList<string> Obligations { get; init; } = Array.Empty<string>();

    /// <summary>Detection confidence in the 0.70–0.95 rung-3 band.</summary>
    public required double Confidence { get; init; }

    /// <summary>Human-readable provenance (detector name + matched signal).</summary>
    public required string Provenance { get; init; }
}
