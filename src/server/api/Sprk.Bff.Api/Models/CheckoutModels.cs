namespace Sprk.Bff.Api.Models;

/// <summary>
/// Response returned when a document is successfully checked out.
/// </summary>
public record CheckoutResponse(
    bool Success,
    CheckoutUserInfo CheckedOutBy,
    DateTime CheckedOutAt,
    string EditUrl,
    string? DesktopUrl,
    Guid FileVersionId,
    int VersionNumber,
    string CorrelationId
);

/// <summary>
/// Response returned when a document is successfully checked in.
/// </summary>
public record CheckInResponse(
    bool Success,
    int VersionNumber,
    string? VersionComment,
    string PreviewUrl,
    bool AiAnalysisTriggered,
    string CorrelationId
);

/// <summary>
/// Request body for check-in operation.
/// </summary>
public record CheckInRequest(
    string? Comment
);

/// <summary>
/// Response returned when checkout is discarded.
/// </summary>
public record DiscardResponse(
    bool Success,
    string Message,
    string PreviewUrl,
    string CorrelationId
);

/// <summary>
/// Error response when document is locked by another user.
/// </summary>
public record DocumentLockedError(
    string Error,
    string Detail,
    CheckoutUserInfo? CheckedOutBy,
    DateTime? CheckedOutAt
);

/// <summary>
/// User information for checkout operations.
/// </summary>
public record CheckoutUserInfo(
    Guid Id,
    string Name,
    string? Email
);

/// <summary>
/// Checkout status information included in preview-url response.
/// </summary>
public record CheckoutStatusInfo(
    bool IsCheckedOut,
    CheckoutUserInfo? CheckedOutBy,
    DateTime? CheckedOutAt,
    bool IsCurrentUser
);

/// <summary>
/// Response returned from the checkout-status endpoint.
/// </summary>
public record CheckoutStatusResponse(
    bool IsCheckedOut,
    CheckoutUserInfo? CheckedOutBy,
    DateTime? CheckedOutAt,
    bool IsCurrentUser,
    string CorrelationId
);

/// <summary>
/// Version information included in preview-url response.
/// </summary>
public record VersionInfo(
    int VersionNumber,
    DateTime? LastModified,
    string? LastModifiedBy
);

/// <summary>
/// Extended preview URL response with checkout status.
/// </summary>
public record PreviewUrlWithStatusResponse(
    string PreviewUrl,
    DocumentInfo DocumentInfo,
    CheckoutStatusInfo CheckoutStatus,
    VersionInfo? VersionInfo,
    string CorrelationId
);

/// <summary>
/// Document info in preview response.
/// </summary>
public record DocumentInfo(
    string Name,
    string FileExtension,
    long Size
);

/// <summary>
/// Delete document response.
/// </summary>
public record DeleteDocumentResponse(
    bool Success,
    string Message,
    string CorrelationId
);
