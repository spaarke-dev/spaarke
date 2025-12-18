using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using Azure.Core;
using Azure.Identity;
using Microsoft.Extensions.Options;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Models;

namespace Sprk.Bff.Api.Services;

/// <summary>
/// Service for document checkout/checkin operations.
/// Coordinates between Dataverse (version tracking) and SharePoint Embedded (file access).
/// </summary>
public class DocumentCheckoutService
{
    private readonly HttpClient _httpClient;
    private readonly SpeFileStore _speFileStore;
    private readonly ILogger<DocumentCheckoutService> _logger;
    private readonly string _dataverseApiUrl;
    private readonly TokenCredential _credential;
    private AccessToken? _currentToken;

    // Dataverse entity set names
    private const string DocumentEntitySet = "sprk_documents";
    private const string FileVersionEntitySet = "sprk_fileversions";

    // Status codes matching Dataverse schema
    private const int StatusCheckedOut = 1;
    private const int StatusCheckedIn = 659490001;
    private const int StatusDiscarded = 2;

    public DocumentCheckoutService(
        HttpClient httpClient,
        SpeFileStore speFileStore,
        IConfiguration configuration,
        ILogger<DocumentCheckoutService> logger)
    {
        _httpClient = httpClient;
        _speFileStore = speFileStore;
        _logger = logger;

        var dataverseUrl = configuration["Dataverse:ServiceUrl"]
            ?? throw new InvalidOperationException("Dataverse:ServiceUrl configuration is required");
        var tenantId = configuration["TENANT_ID"]
            ?? throw new InvalidOperationException("TENANT_ID configuration is required");
        var clientId = configuration["API_APP_ID"]
            ?? throw new InvalidOperationException("API_APP_ID configuration is required");
        var clientSecret = configuration["Dataverse:ClientSecret"]
            ?? throw new InvalidOperationException("Dataverse:ClientSecret configuration is required");

        _dataverseApiUrl = $"{dataverseUrl.TrimEnd('/')}/api/data/v9.2/";
        _credential = new ClientSecretCredential(tenantId, clientId, clientSecret);

        _httpClient.BaseAddress = new Uri(_dataverseApiUrl);
        _httpClient.DefaultRequestHeaders.Add("OData-MaxVersion", "4.0");
        _httpClient.DefaultRequestHeaders.Add("OData-Version", "4.0");
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    private async Task EnsureAuthenticatedAsync(CancellationToken ct = default)
    {
        if (_currentToken == null || _currentToken.Value.ExpiresOn <= DateTimeOffset.UtcNow.AddMinutes(5))
        {
            var scope = $"{_dataverseApiUrl.Replace("/api/data/v9.2/", "")}/.default";
            _currentToken = await _credential.GetTokenAsync(
                new TokenRequestContext(new[] { scope }), ct);

            _httpClient.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", _currentToken.Value.Token);

            _logger.LogDebug("Refreshed Dataverse access token for checkout service");
        }
    }

    /// <summary>
    /// Check out a document for editing.
    /// Creates a new FileVersion record and locks the document.
    /// </summary>
    public async Task<CheckoutResult> CheckoutAsync(
        Guid documentId,
        ClaimsPrincipal user,
        string correlationId,
        CancellationToken ct = default)
    {
        await EnsureAuthenticatedAsync(ct);

        var userId = GetUserId(user);
        var userName = GetUserName(user);
        var userEmail = GetUserEmail(user);

        _logger.LogInformation(
            "Checkout requested for document {DocumentId} by user {UserId} [{CorrelationId}]",
            documentId, userId, correlationId);

        // 1. Get document and check current status
        var document = await GetDocumentAsync(documentId, ct);
        if (document == null)
        {
            return CheckoutResult.NotFound(correlationId);
        }

        // 2. Check if already checked out
        if (document.IsCheckedOut)
        {
            // Allow re-checkout by same user (idempotent)
            if (document.CheckedOutById == userId)
            {
                _logger.LogInformation(
                    "Document {DocumentId} already checked out by same user, returning existing checkout",
                    documentId);

                return CheckoutResult.Success(
                    new CheckoutResponse(
                        Success: true,
                        CheckedOutBy: new CheckoutUserInfo(userId, userName, userEmail),
                        CheckedOutAt: document.CheckedOutAt ?? DateTime.UtcNow,
                        EditUrl: await GetEditUrlAsync(document.DriveId, document.ItemId, ct),
                        DesktopUrl: GetDesktopUrl(document.DriveId, document.ItemId, document.FileName),
                        FileVersionId: document.CurrentVersionId ?? Guid.Empty,
                        VersionNumber: document.VersionNumber,
                        CorrelationId: correlationId
                    ),
                    correlationId
                );
            }

            // Locked by another user - return conflict
            _logger.LogWarning(
                "Document {DocumentId} is checked out by another user {CheckedOutBy}",
                documentId, document.CheckedOutByName);

            return CheckoutResult.Conflict(
                new DocumentLockedError(
                    Error: "document_locked",
                    Detail: $"Document is checked out by {document.CheckedOutByName} since {document.CheckedOutAt:g}",
                    CheckedOutBy: new CheckoutUserInfo(
                        document.CheckedOutById ?? Guid.Empty,
                        document.CheckedOutByName ?? "Unknown",
                        null
                    ),
                    CheckedOutAt: document.CheckedOutAt
                ),
                correlationId
            );
        }

        // 3. Create new FileVersion record
        var newVersionNumber = document.VersionNumber + 1;
        var now = DateTime.UtcNow;

        var fileVersionId = await CreateFileVersionAsync(
            documentId,
            userId,
            newVersionNumber,
            now,
            ct
        );

        // 4. Update Document with checkout status
        await UpdateDocumentCheckoutStatusAsync(
            documentId,
            userId,
            now,
            newVersionNumber,
            fileVersionId,
            ct
        );

        // 5. Get edit URL from SharePoint
        var editUrl = await GetEditUrlAsync(document.DriveId, document.ItemId, ct);
        var desktopUrl = GetDesktopUrl(document.DriveId, document.ItemId, document.FileName);

        _logger.LogInformation(
            "Document {DocumentId} checked out successfully. FileVersion: {FileVersionId}, Version: {VersionNumber}",
            documentId, fileVersionId, newVersionNumber);

        return CheckoutResult.Success(
            new CheckoutResponse(
                Success: true,
                CheckedOutBy: new CheckoutUserInfo(userId, userName, userEmail),
                CheckedOutAt: now,
                EditUrl: editUrl,
                DesktopUrl: desktopUrl,
                FileVersionId: fileVersionId,
                VersionNumber: newVersionNumber,
                CorrelationId: correlationId
            ),
            correlationId
        );
    }

    /// <summary>
    /// Check in a document, releasing the lock and creating a committed version.
    /// </summary>
    public async Task<CheckInResult> CheckInAsync(
        Guid documentId,
        string? comment,
        ClaimsPrincipal user,
        string correlationId,
        CancellationToken ct = default)
    {
        await EnsureAuthenticatedAsync(ct);

        var userId = GetUserId(user);

        _logger.LogInformation(
            "Check-in requested for document {DocumentId} by user {UserId} [{CorrelationId}]",
            documentId, userId, correlationId);

        // 1. Get document and verify checkout
        var document = await GetDocumentAsync(documentId, ct);
        if (document == null)
        {
            return CheckInResult.NotFound(correlationId);
        }

        if (!document.IsCheckedOut)
        {
            return CheckInResult.NotCheckedOut(correlationId);
        }

        // 2. Update FileVersion record with check-in info
        if (document.CurrentVersionId.HasValue)
        {
            await UpdateFileVersionCheckInAsync(
                document.CurrentVersionId.Value,
                userId,
                comment,
                document.FileSize,
                ct
            );
        }

        // 3. Clear checkout status on Document
        await ClearDocumentCheckoutStatusAsync(documentId, ct);

        // 4. Get preview URL
        var previewUrl = await GetPreviewUrlAsync(document.DriveId, document.ItemId, ct);

        // 5. TODO: Trigger AI analysis (fire-and-forget)
        var aiTriggered = false; // Will be implemented in task 052

        _logger.LogInformation(
            "Document {DocumentId} checked in successfully. Version: {VersionNumber}",
            documentId, document.VersionNumber);

        return CheckInResult.Success(
            new CheckInResponse(
                Success: true,
                VersionNumber: document.VersionNumber,
                VersionComment: comment,
                PreviewUrl: previewUrl,
                AiAnalysisTriggered: aiTriggered,
                CorrelationId: correlationId
            ),
            correlationId
        );
    }

    /// <summary>
    /// Discard checkout without saving changes.
    /// </summary>
    public async Task<DiscardResult> DiscardAsync(
        Guid documentId,
        ClaimsPrincipal user,
        string correlationId,
        CancellationToken ct = default)
    {
        await EnsureAuthenticatedAsync(ct);

        var userId = GetUserId(user);

        _logger.LogInformation(
            "Discard requested for document {DocumentId} by user {UserId} [{CorrelationId}]",
            documentId, userId, correlationId);

        // 1. Get document and verify checkout
        var document = await GetDocumentAsync(documentId, ct);
        if (document == null)
        {
            return DiscardResult.NotFound(correlationId);
        }

        if (!document.IsCheckedOut)
        {
            return DiscardResult.NotCheckedOut(correlationId);
        }

        // Only allow discard by the user who checked out (or admin - future)
        if (document.CheckedOutById != userId)
        {
            return DiscardResult.NotAuthorized(correlationId);
        }

        // 2. Update FileVersion record to Discarded status
        if (document.CurrentVersionId.HasValue)
        {
            await UpdateFileVersionStatusAsync(
                document.CurrentVersionId.Value,
                StatusDiscarded,
                ct
            );
        }

        // 3. Clear checkout status on Document
        await ClearDocumentCheckoutStatusAsync(documentId, ct);

        // 4. Get preview URL
        var previewUrl = await GetPreviewUrlAsync(document.DriveId, document.ItemId, ct);

        _logger.LogInformation(
            "Document {DocumentId} checkout discarded",
            documentId);

        return DiscardResult.Success(
            new DiscardResponse(
                Success: true,
                Message: "Checkout discarded. Document reverted to last committed version.",
                PreviewUrl: previewUrl,
                CorrelationId: correlationId
            ),
            correlationId
        );
    }

    /// <summary>
    /// Get checkout status for a document.
    /// </summary>
    public async Task<CheckoutStatusInfo?> GetCheckoutStatusAsync(
        Guid documentId,
        ClaimsPrincipal user,
        CancellationToken ct = default)
    {
        await EnsureAuthenticatedAsync(ct);

        var document = await GetDocumentAsync(documentId, ct);
        if (document == null)
        {
            return null;
        }

        var currentUserId = GetUserId(user);

        return new CheckoutStatusInfo(
            IsCheckedOut: document.IsCheckedOut,
            CheckedOutBy: document.IsCheckedOut
                ? new CheckoutUserInfo(
                    document.CheckedOutById ?? Guid.Empty,
                    document.CheckedOutByName ?? "Unknown",
                    null)
                : null,
            CheckedOutAt: document.CheckedOutAt,
            IsCurrentUser: document.CheckedOutById == currentUserId
        );
    }

    /// <summary>
    /// Delete a document (both SPE file and Dataverse record).
    /// </summary>
    public async Task<DeleteResult> DeleteAsync(
        Guid documentId,
        string correlationId,
        CancellationToken ct = default)
    {
        await EnsureAuthenticatedAsync(ct);

        _logger.LogInformation(
            "Delete requested for document {DocumentId} [{CorrelationId}]",
            documentId, correlationId);

        // 1. Get document and verify not checked out
        var document = await GetDocumentAsync(documentId, ct);
        if (document == null)
        {
            return DeleteResult.NotFound(correlationId);
        }

        if (document.IsCheckedOut)
        {
            _logger.LogWarning(
                "Cannot delete document {DocumentId} - checked out by {CheckedOutBy}",
                documentId, document.CheckedOutByName);

            return DeleteResult.CheckedOut(
                new DocumentLockedError(
                    Error: "document_locked",
                    Detail: $"Cannot delete document. It is checked out by {document.CheckedOutByName}.",
                    CheckedOutBy: new CheckoutUserInfo(
                        document.CheckedOutById ?? Guid.Empty,
                        document.CheckedOutByName ?? "Unknown",
                        null),
                    CheckedOutAt: document.CheckedOutAt
                ),
                correlationId
            );
        }

        // 2. Delete file from SPE first
        try
        {
            if (!string.IsNullOrEmpty(document.DriveId) && !string.IsNullOrEmpty(document.ItemId))
            {
                var deleted = await _speFileStore.DeleteFileAsync(document.DriveId, document.ItemId, ct);
                if (!deleted)
                {
                    _logger.LogWarning(
                        "SPE file not found for document {DocumentId} (may already be deleted)",
                        documentId);
                }
                else
                {
                    _logger.LogInformation(
                        "SPE file deleted for document {DocumentId}",
                        documentId);
                }
            }
        }
        catch (Exception ex)
        {
            // Log but continue - file might already be deleted
            _logger.LogWarning(ex,
                "Failed to delete SPE file for document {DocumentId} - continuing with Dataverse deletion",
                documentId);
        }

        // 3. Delete Dataverse record (cascade should handle FileVersions)
        try
        {
            var deleteUrl = $"{DocumentEntitySet}({documentId})";
            var response = await _httpClient.DeleteAsync(deleteUrl, ct);

            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                return DeleteResult.NotFound(correlationId);
            }

            response.EnsureSuccessStatusCode();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete Dataverse record for document {DocumentId}", documentId);
            return DeleteResult.Failed(ex.Message, correlationId);
        }

        _logger.LogInformation(
            "Document {DocumentId} deleted successfully [{CorrelationId}]",
            documentId, correlationId);

        return DeleteResult.Success(
            new DeleteDocumentResponse(
                Success: true,
                Message: "Document deleted successfully.",
                CorrelationId: correlationId
            ),
            correlationId
        );
    }

    #region Private Methods

    private async Task<DocumentRecord?> GetDocumentAsync(Guid documentId, CancellationToken ct)
    {
        var selectFields = "sprk_documentid,sprk_documentname,sprk_filename,sprk_filesize," +
                          "sprk_graphdriveid,sprk_graphitemid," +
                          "_sprk_checkedoutby_value,sprk_checkoutdate,sprk_checkindate," +
                          "_sprk_currentversionid_value,sprk_versionnumber";
        var expandFields = "sprk_CheckedOutBy($select=fullname)";

        var url = $"{DocumentEntitySet}({documentId})?$select={selectFields}&$expand={expandFields}";

        try
        {
            var response = await _httpClient.GetAsync(url, ct);

            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                return null;
            }

            response.EnsureSuccessStatusCode();

            var data = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
            return MapToDocumentRecord(data);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get document {DocumentId}", documentId);
            throw;
        }
    }

    private async Task<Guid> CreateFileVersionAsync(
        Guid documentId,
        Guid userId,
        int versionNumber,
        DateTime checkoutDate,
        CancellationToken ct)
    {
        var payload = new Dictionary<string, object>
        {
            ["sprk_versionnumber"] = $"v{versionNumber}",
            ["sprk_checkoutdate"] = checkoutDate.ToString("o"),
            ["sprk_CheckedOutBy@odata.bind"] = $"/systemusers({userId})",
            ["sprk_Document@odata.bind"] = $"/{DocumentEntitySet}({documentId})",
            ["statuscode"] = StatusCheckedOut
        };

        var response = await _httpClient.PostAsJsonAsync(FileVersionEntitySet, payload, ct);
        response.EnsureSuccessStatusCode();

        // Extract ID from OData-EntityId header
        var entityIdHeader = response.Headers.GetValues("OData-EntityId").FirstOrDefault();
        if (entityIdHeader != null)
        {
            var idString = entityIdHeader.Split('(', ')')[1];
            return Guid.Parse(idString);
        }

        throw new InvalidOperationException("Failed to extract FileVersion ID from create response");
    }

    private async Task UpdateDocumentCheckoutStatusAsync(
        Guid documentId,
        Guid userId,
        DateTime checkoutDate,
        int versionNumber,
        Guid fileVersionId,
        CancellationToken ct)
    {
        var payload = new Dictionary<string, object>
        {
            ["sprk_checkoutdate"] = checkoutDate.ToString("o"),
            ["sprk_CheckedOutBy@odata.bind"] = $"/systemusers({userId})",
            ["sprk_versionnumber"] = versionNumber,
            ["sprk_CurrentVersionId@odata.bind"] = $"/{FileVersionEntitySet}({fileVersionId})"
        };

        var response = await _httpClient.PatchAsJsonAsync(
            $"{DocumentEntitySet}({documentId})",
            payload,
            ct
        );
        response.EnsureSuccessStatusCode();
    }

    private async Task UpdateFileVersionCheckInAsync(
        Guid fileVersionId,
        Guid userId,
        string? comment,
        long? fileSize,
        CancellationToken ct)
    {
        var payload = new Dictionary<string, object?>
        {
            ["sprk_checkindate"] = DateTime.UtcNow.ToString("o"),
            ["sprk_CheckedInBy@odata.bind"] = $"/systemusers({userId})",
            ["statuscode"] = StatusCheckedIn
        };

        if (!string.IsNullOrEmpty(comment))
        {
            payload["sprk_comment"] = comment;
        }

        if (fileSize.HasValue)
        {
            payload["sprk_filesize"] = fileSize.Value;
        }

        var response = await _httpClient.PatchAsJsonAsync(
            $"{FileVersionEntitySet}({fileVersionId})",
            payload,
            ct
        );
        response.EnsureSuccessStatusCode();
    }

    private async Task UpdateFileVersionStatusAsync(
        Guid fileVersionId,
        int status,
        CancellationToken ct)
    {
        var payload = new Dictionary<string, object>
        {
            ["statuscode"] = status
        };

        var response = await _httpClient.PatchAsJsonAsync(
            $"{FileVersionEntitySet}({fileVersionId})",
            payload,
            ct
        );
        response.EnsureSuccessStatusCode();
    }

    private async Task ClearDocumentCheckoutStatusAsync(Guid documentId, CancellationToken ct)
    {
        // Clear checkout fields by setting to null
        var payload = new Dictionary<string, object?>
        {
            ["sprk_checkoutdate"] = null,
            ["sprk_checkindate"] = DateTime.UtcNow.ToString("o")
        };

        var response = await _httpClient.PatchAsJsonAsync(
            $"{DocumentEntitySet}({documentId})",
            payload,
            ct
        );
        response.EnsureSuccessStatusCode();

        // Disassociate checkout user (separate call for lookup null)
        await DisassociateLookupAsync(documentId, "sprk_CheckedOutBy", ct);
    }

    private async Task DisassociateLookupAsync(Guid documentId, string navigationProperty, CancellationToken ct)
    {
        var url = $"{DocumentEntitySet}({documentId})/{navigationProperty}/$ref";
        var response = await _httpClient.DeleteAsync(url, ct);

        // 404 is OK - means lookup was already null
        if (response.StatusCode != System.Net.HttpStatusCode.NotFound)
        {
            response.EnsureSuccessStatusCode();
        }
    }

    private async Task<string> GetEditUrlAsync(string driveId, string itemId, CancellationToken ct)
    {
        // Get the embedview URL for editing
        var preview = await _speFileStore.GetPreviewUrlAsync(driveId, itemId, null, ct);

        // Convert embed.aspx to embedview for editing
        // embed.aspx is read-only, embedview provides full editing
        var editUrl = preview.PreviewUrl?.Replace("/embed.aspx?", "/embedview?") ?? "";

        return editUrl;
    }

    private async Task<string> GetPreviewUrlAsync(string driveId, string itemId, CancellationToken ct)
    {
        var preview = await _speFileStore.GetPreviewUrlAsync(driveId, itemId, null, ct);
        return preview.PreviewUrl ?? "";
    }

    private static string? GetDesktopUrl(string driveId, string itemId, string? fileName)
    {
        if (string.IsNullOrEmpty(fileName))
            return null;

        var extension = Path.GetExtension(fileName)?.ToLowerInvariant();

        var protocol = extension switch
        {
            ".docx" or ".doc" => "ms-word",
            ".xlsx" or ".xls" => "ms-excel",
            ".pptx" or ".ppt" => "ms-powerpoint",
            _ => null
        };

        if (protocol == null)
            return null;

        // Desktop URL format: ms-word:ofe|u|https://...
        // The actual URL construction would need the web URL from Graph
        return null; // TODO: Implement when we have the full web URL from Graph
    }

    private DocumentRecord? MapToDocumentRecord(JsonElement data)
    {
        if (data.ValueKind == JsonValueKind.Undefined || data.ValueKind == JsonValueKind.Null)
            return null;

        var checkedOutByValue = data.TryGetProperty("_sprk_checkedoutby_value", out var coby) && coby.ValueKind != JsonValueKind.Null
            ? Guid.Parse(coby.GetString()!)
            : (Guid?)null;

        var checkedOutByName = data.TryGetProperty("sprk_CheckedOutBy", out var cobyExpand) && cobyExpand.ValueKind != JsonValueKind.Null
            ? cobyExpand.TryGetProperty("fullname", out var fn) ? fn.GetString() : null
            : null;

        return new DocumentRecord
        {
            DocumentId = data.TryGetProperty("sprk_documentid", out var id) ? Guid.Parse(id.GetString()!) : Guid.Empty,
            DocumentName = data.TryGetProperty("sprk_documentname", out var name) ? name.GetString() : null,
            FileName = data.TryGetProperty("sprk_filename", out var fn2) ? fn2.GetString() : null,
            FileSize = data.TryGetProperty("sprk_filesize", out var fs) && fs.ValueKind == JsonValueKind.Number ? fs.GetInt64() : 0,
            DriveId = data.TryGetProperty("sprk_graphdriveid", out var did) ? did.GetString()! : "",
            ItemId = data.TryGetProperty("sprk_graphitemid", out var iid) ? iid.GetString()! : "",
            CheckedOutById = checkedOutByValue,
            CheckedOutByName = checkedOutByName,
            CheckedOutAt = data.TryGetProperty("sprk_checkoutdate", out var codate) && codate.ValueKind != JsonValueKind.Null
                ? DateTime.Parse(codate.GetString()!)
                : null,
            CheckedInAt = data.TryGetProperty("sprk_checkindate", out var cidate) && cidate.ValueKind != JsonValueKind.Null
                ? DateTime.Parse(cidate.GetString()!)
                : null,
            CurrentVersionId = data.TryGetProperty("_sprk_currentversionid_value", out var cvid) && cvid.ValueKind != JsonValueKind.Null
                ? Guid.Parse(cvid.GetString()!)
                : null,
            VersionNumber = data.TryGetProperty("sprk_versionnumber", out var vn) && vn.ValueKind == JsonValueKind.Number
                ? vn.GetInt32()
                : 0
        };
    }

    private static Guid GetUserId(ClaimsPrincipal user)
    {
        var oid = user.FindFirst("oid")?.Value
            ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? throw new InvalidOperationException("User ID not found in claims");

        return Guid.Parse(oid);
    }

    private static string GetUserName(ClaimsPrincipal user)
    {
        return user.FindFirst("name")?.Value
            ?? user.FindFirst(ClaimTypes.Name)?.Value
            ?? "Unknown";
    }

    private static string? GetUserEmail(ClaimsPrincipal user)
    {
        return user.FindFirst("preferred_username")?.Value
            ?? user.FindFirst(ClaimTypes.Email)?.Value;
    }

    #endregion
}

#region Result Types

public abstract record CheckoutResult(bool IsSuccess, string CorrelationId)
{
    public CheckoutResponse? Response { get; init; }
    public DocumentLockedError? ConflictError { get; init; }
    public int StatusCode { get; init; }

    public static CheckoutResult Success(CheckoutResponse response, string correlationId)
        => new SuccessCheckoutResult(response, correlationId);

    public static CheckoutResult NotFound(string correlationId)
        => new NotFoundCheckoutResult(correlationId);

    public static CheckoutResult Conflict(DocumentLockedError error, string correlationId)
        => new ConflictCheckoutResult(error, correlationId);
}

public record SuccessCheckoutResult(CheckoutResponse Response, string CorrelationId)
    : CheckoutResult(true, CorrelationId)
{
    public new CheckoutResponse Response { get; } = Response;
    public new int StatusCode => 200;
}

public record NotFoundCheckoutResult(string CorrelationId)
    : CheckoutResult(false, CorrelationId)
{
    public new int StatusCode => 404;
}

public record ConflictCheckoutResult(DocumentLockedError Error, string CorrelationId)
    : CheckoutResult(false, CorrelationId)
{
    public new DocumentLockedError ConflictError => Error;
    public new int StatusCode => 409;
}

public abstract record CheckInResult(bool IsSuccess, string CorrelationId)
{
    public CheckInResponse? Response { get; init; }
    public int StatusCode { get; init; }

    public static CheckInResult Success(CheckInResponse response, string correlationId)
        => new SuccessCheckInResult(response, correlationId);

    public static CheckInResult NotFound(string correlationId)
        => new NotFoundCheckInResult(correlationId);

    public static CheckInResult NotCheckedOut(string correlationId)
        => new NotCheckedOutCheckInResult(correlationId);
}

public record SuccessCheckInResult(CheckInResponse Response, string CorrelationId)
    : CheckInResult(true, CorrelationId)
{
    public new CheckInResponse Response { get; } = Response;
    public new int StatusCode => 200;
}

public record NotFoundCheckInResult(string CorrelationId)
    : CheckInResult(false, CorrelationId)
{
    public new int StatusCode => 404;
}

public record NotCheckedOutCheckInResult(string CorrelationId)
    : CheckInResult(false, CorrelationId)
{
    public new int StatusCode => 400;
}

public abstract record DiscardResult(bool IsSuccess, string CorrelationId)
{
    public DiscardResponse? Response { get; init; }
    public int StatusCode { get; init; }

    public static DiscardResult Success(DiscardResponse response, string correlationId)
        => new SuccessDiscardResult(response, correlationId);

    public static DiscardResult NotFound(string correlationId)
        => new NotFoundDiscardResult(correlationId);

    public static DiscardResult NotCheckedOut(string correlationId)
        => new NotCheckedOutDiscardResult(correlationId);

    public static DiscardResult NotAuthorized(string correlationId)
        => new NotAuthorizedDiscardResult(correlationId);
}

public record SuccessDiscardResult(DiscardResponse Response, string CorrelationId)
    : DiscardResult(true, CorrelationId)
{
    public new DiscardResponse Response { get; } = Response;
    public new int StatusCode => 200;
}

public record NotFoundDiscardResult(string CorrelationId)
    : DiscardResult(false, CorrelationId)
{
    public new int StatusCode => 404;
}

public record NotCheckedOutDiscardResult(string CorrelationId)
    : DiscardResult(false, CorrelationId)
{
    public new int StatusCode => 400;
}

public record NotAuthorizedDiscardResult(string CorrelationId)
    : DiscardResult(false, CorrelationId)
{
    public new int StatusCode => 403;
}

public abstract record DeleteResult(bool IsSuccess, string CorrelationId)
{
    public DeleteDocumentResponse? Response { get; init; }
    public DocumentLockedError? CheckedOutError { get; init; }
    public int StatusCode { get; init; }

    public static DeleteResult Success(DeleteDocumentResponse response, string correlationId)
        => new SuccessDeleteResult(response, correlationId);

    public static DeleteResult NotFound(string correlationId)
        => new NotFoundDeleteResult(correlationId);

    public static DeleteResult CheckedOut(DocumentLockedError error, string correlationId)
        => new CheckedOutDeleteResult(error, correlationId);

    public static DeleteResult Failed(string message, string correlationId)
        => new FailedDeleteResult(message, correlationId);
}

public record SuccessDeleteResult(DeleteDocumentResponse Response, string CorrelationId)
    : DeleteResult(true, CorrelationId)
{
    public new DeleteDocumentResponse Response { get; } = Response;
    public new int StatusCode => 200;
}

public record NotFoundDeleteResult(string CorrelationId)
    : DeleteResult(false, CorrelationId)
{
    public new int StatusCode => 404;
}

public record CheckedOutDeleteResult(DocumentLockedError Error, string CorrelationId)
    : DeleteResult(false, CorrelationId)
{
    public new DocumentLockedError CheckedOutError => Error;
    public new int StatusCode => 409;
}

public record FailedDeleteResult(string Message, string CorrelationId)
    : DeleteResult(false, CorrelationId)
{
    public new int StatusCode => 500;
}

#endregion

#region Internal Models

internal record DocumentRecord
{
    public Guid DocumentId { get; init; }
    public string? DocumentName { get; init; }
    public string? FileName { get; init; }
    public long FileSize { get; init; }
    public string DriveId { get; init; } = "";
    public string ItemId { get; init; } = "";
    public Guid? CheckedOutById { get; init; }
    public string? CheckedOutByName { get; init; }
    public DateTime? CheckedOutAt { get; init; }
    public DateTime? CheckedInAt { get; init; }
    public Guid? CurrentVersionId { get; init; }
    public int VersionNumber { get; init; }

    public bool IsCheckedOut => CheckedOutById.HasValue;
}

#endregion
