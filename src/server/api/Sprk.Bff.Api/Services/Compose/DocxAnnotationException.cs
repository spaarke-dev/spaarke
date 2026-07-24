namespace Sprk.Bff.Api.Services.Compose;

/// <summary>The category of a <see cref="DocxAnnotationException"/>.</summary>
/// <remarks>
/// Formerly co-located with the retired <c>DocxAnnotationWriter</c> (task 036 retired the
/// text-anchored push-annotations WRITE surface per §6.5 Path B). These types survive because they
/// are the READ-direction error contract for <see cref="DocxAnnotationReader"/> and the
/// pull-annotations / reanchor-annotations endpoints (ComposeEndpoints), which map
/// <see cref="DocxAnnotationErrorKind.MalformedDocument"/> to a 400 ProblemDetails.
/// </remarks>
public enum DocxAnnotationErrorKind
{
    /// <summary>The supplied bytes are not a readable DOCX package → HTTP 400.</summary>
    MalformedDocument,

    /// <summary>An annotation's <c>target_text</c> was not found in the document → HTTP 422.</summary>
    TargetNotFound,
}

/// <summary>
/// A structured, mappable failure from the Compose Open XML annotation parse. Distinct from a bare
/// exception so the endpoint can turn it into the right ProblemDetails status
/// (<see cref="DocxAnnotationErrorKind.MalformedDocument"/> → 400,
/// <see cref="DocxAnnotationErrorKind.TargetNotFound"/> → 422) instead of an opaque 500.
/// </summary>
public sealed class DocxAnnotationException : Exception
{
    public DocxAnnotationException(DocxAnnotationErrorKind kind, string message, Exception? inner = null)
        : base(message, inner)
    {
        Kind = kind;
    }

    /// <summary>The failure category, used by the endpoint to select the ProblemDetails status.</summary>
    public DocxAnnotationErrorKind Kind { get; }
}
