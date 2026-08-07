namespace Sprk.Bff.Api.Services.Compose;

/// <summary>
/// Task 040 Step-9.5 fix (HIGH-1): the PDF-intake failure signal <see cref="ComposeService"/> throws and
/// the Compose load/save endpoints map to an HONEST ProblemDetails — 503 when the intake capability is
/// unavailable (<see cref="Unavailable"/> true: compound AI gate OFF, parse service down/corrupt-source
/// collapse at the facade's null boundary), 422 when the document itself is not projectable
/// (<see cref="Unavailable"/> false: nothing editable, or a save baseline resolved to PDF bytes).
/// Without this type the carefully-worded messages fell into the endpoints' catch-all and surfaced as a
/// generic 500, contradicting the honest-lossiness contract (FR-06).
/// </summary>
public sealed class ComposePdfIntakeException : InvalidOperationException
{
    /// <summary>True = the intake capability is unavailable (map to 503, retryable); false = this
    /// document cannot be projected/saved as requested (map to 422, not retryable as-is).</summary>
    public bool Unavailable { get; }

    public ComposePdfIntakeException(string message, bool unavailable)
        : base(message)
    {
        Unavailable = unavailable;
    }
}
