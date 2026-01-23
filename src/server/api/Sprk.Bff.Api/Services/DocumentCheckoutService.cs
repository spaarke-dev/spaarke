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

    // State codes matching Dataverse schema
    private const int StateActive = 0;
    private const int StateInactive = 1;

    // Status codes matching Dataverse schema
    private const int StatusCheckedOut = 1;       // State: Active
    private const int StatusCheckedIn = 659490001; // State: Active
    private const int StatusDiscarded = 2;        // State: Inactive

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
            ?? configuration["Dataverse:EnvironmentUrl"]
            ?? throw new InvalidOperationException("Dataverse:ServiceUrl or Dataverse:EnvironmentUrl configuration is required");
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

        var azureAdOid = GetUserId(user);
        var userName = GetUserName(user);
        var userEmail = GetUserEmail(user);

        _logger.LogInformation(
            "Checkout requested for document {DocumentId} by user {AzureAdOid} [{CorrelationId}]",
            documentId, azureAdOid, correlationId);

        // Map Azure AD OID to Dataverse systemuserid
        var dataverseUserId = await LookupDataverseUserIdAsync(azureAdOid, ct);
        if (!dataverseUserId.HasValue)
        {
            _logger.LogError("Cannot checkout: User {AzureAdOid} not found in Dataverse", azureAdOid);
            throw new InvalidOperationException($"User not found in Dataverse. Azure AD OID: {azureAdOid}");
        }

        var userId = dataverseUserId.Value;

        // 1. Get document and check current status
        var document = await GetDocumentAsync(documentId, ct);
        if (document == null)
        {
            return CheckoutResult.NotFound(correlationId);
        }

        _logger.LogInformation(
            "Document {DocumentId} retrieved. Name='{DocumentName}', DriveId='{DriveId}', ItemId='{ItemId}'",
            documentId, document.DocumentName, document.DriveId, document.ItemId);

        // 2. Check if already checked out
        if (document.IsCheckedOut)
        {
            // Allow re-checkout by same user (idempotent)
            if (document.CheckedOutById == userId)
            {
                _logger.LogInformation(
                    "Document {DocumentId} already checked out by same user, returning existing checkout",
                    documentId);

                string existingEditUrl = "";
                try
                {
                    existingEditUrl = await GetEditUrlAsync(document.DriveId, document.ItemId, ct);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to get edit URL for already-checked-out document {DocumentId}", documentId);
                }

                return CheckoutResult.Success(
                    new CheckoutResponse(
                        Success: true,
                        CheckedOutBy: new CheckoutUserInfo(userId, userName, userEmail),
                        CheckedOutAt: document.CheckedOutAt ?? DateTime.UtcNow,
                        EditUrl: existingEditUrl,
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

        // 5. Get edit URL from SharePoint (non-critical - checkout succeeds without it)
        string editUrl = "";
        try
        {
            editUrl = await GetEditUrlAsync(document.DriveId, document.ItemId, ct);
        }
        catch (Exception ex)
        {
            // Log but don't fail checkout - the Dataverse state update is what matters
            _logger.LogWarning(ex, "Failed to get edit URL for document {DocumentId}, checkout will succeed without it", documentId);
        }

        var desktopUrl = GetDesktopUrl(document.DriveId, document.ItemId, document.FileName);

        _logger.LogInformation(
            "Document {DocumentId} checked out successfully. FileVersion: {FileVersionId}, Version: {VersionNumber}, HasEditUrl: {HasEditUrl}",
            documentId, fileVersionId, newVersionNumber, !string.IsNullOrEmpty(editUrl));

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

        var azureAdOid = GetUserId(user);

        _logger.LogInformation(
            "Check-in requested for document {DocumentId} by user {AzureAdOid} [{CorrelationId}]",
            documentId, azureAdOid, correlationId);

        // Map Azure AD OID to Dataverse systemuserid
        var dataverseUserId = await LookupDataverseUserIdAsync(azureAdOid, ct);
        if (!dataverseUserId.HasValue)
        {
            _logger.LogError("Cannot check-in: User {AzureAdOid} not found in Dataverse", azureAdOid);
            throw new InvalidOperationException($"User not found in Dataverse. Azure AD OID: {azureAdOid}");
        }

        var userId = dataverseUserId.Value;

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

        // 3. Clear checkout status on Document and set CheckedInBy
        await ClearDocumentCheckoutStatusAsync(documentId, userId, ct);

        // 4. Get preview URL (non-critical - don't fail check-in if this fails)
        string previewUrl = "";
        try
        {
            previewUrl = await GetPreviewUrlAsync(document.DriveId, document.ItemId, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get preview URL after check-in for document {DocumentId}", documentId);
        }

        // 5. Re-indexing is triggered in the endpoint after successful check-in
        // See DocumentOperationsEndpoints.CheckInDocument for job enqueueing
        var aiTriggered = false; // Set to true by endpoint after enqueueing job

        _logger.LogInformation(
            "Document {DocumentId} checked in successfully. Version: {VersionNumber}",
            documentId, document.VersionNumber);

        // Build file info for potential re-indexing
        DocumentFileInfo? fileInfo = null;
        if (!string.IsNullOrEmpty(document.DriveId) && !string.IsNullOrEmpty(document.ItemId))
        {
            fileInfo = new DocumentFileInfo(
                DriveId: document.DriveId,
                ItemId: document.ItemId,
                FileName: document.FileName ?? document.DocumentName ?? "unknown",
                DocumentId: documentId
            );
        }

        return new SuccessCheckInResult(
            new CheckInResponse(
                Success: true,
                VersionNumber: document.VersionNumber,
                VersionComment: comment,
                PreviewUrl: previewUrl,
                AiAnalysisTriggered: aiTriggered,
                CorrelationId: correlationId
            ),
            correlationId
        )
        {
            FileInfo = fileInfo
        };
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

        var azureAdOid = GetUserId(user);

        _logger.LogInformation(
            "Discard requested for document {DocumentId} by user {AzureAdOid} [{CorrelationId}]",
            documentId, azureAdOid, correlationId);

        // Map Azure AD OID to Dataverse systemuserid
        var dataverseUserId = await LookupDataverseUserIdAsync(azureAdOid, ct);
        if (!dataverseUserId.HasValue)
        {
            _logger.LogError("Cannot discard: User {AzureAdOid} not found in Dataverse", azureAdOid);
            throw new InvalidOperationException($"User not found in Dataverse. Azure AD OID: {azureAdOid}");
        }

        var userId = dataverseUserId.Value;

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

        // 2. Update FileVersion record to Discarded status (Inactive state)
        if (document.CurrentVersionId.HasValue)
        {
            try
            {
                await UpdateFileVersionStatusAsync(
                    document.CurrentVersionId.Value,
                    StatusDiscarded,
                    StateInactive,  // Discarded is in Inactive state
                    ct
                );
            }
            catch (Exception ex)
            {
                // Log but don't fail - the Document status clear is what matters
                _logger.LogWarning(ex, "Failed to update FileVersion status to Discarded for {FileVersionId}. " +
                    "This may indicate StatusDiscarded ({StatusCode}) is not a valid status option.",
                    document.CurrentVersionId.Value, StatusDiscarded);
            }
        }

        // 3. Clear checkout status on Document (no checkin info for discard)
        await ClearDocumentCheckoutStatusAsync(documentId, null, ct);

        // 4. Get preview URL (non-critical - don't fail discard if this fails)
        string previewUrl = "";
        try
        {
            previewUrl = await GetPreviewUrlAsync(document.DriveId, document.ItemId, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get preview URL after discard for document {DocumentId}", documentId);
        }

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

        // Map Azure AD OID to Dataverse systemuserid for comparison
        var azureAdOid = GetUserId(user);
        var currentUserId = await LookupDataverseUserIdAsync(azureAdOid, ct);

        return new CheckoutStatusInfo(
            IsCheckedOut: document.IsCheckedOut,
            CheckedOutBy: document.IsCheckedOut
                ? new CheckoutUserInfo(
                    document.CheckedOutById ?? Guid.Empty,
                    document.CheckedOutByName ?? "Unknown",
                    null)
                : null,
            CheckedOutAt: document.CheckedOutAt,
            IsCurrentUser: currentUserId.HasValue && document.CheckedOutById == currentUserId.Value
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

    /// <summary>
    /// Looks up the Dataverse systemuserid for a given Azure AD Object ID.
    /// Dataverse systemuserid != Azure AD oid; they're linked via azureactivedirectoryobjectid.
    /// </summary>
    private async Task<Guid?> LookupDataverseUserIdAsync(Guid azureAdObjectId, CancellationToken ct)
    {
        try
        {
            var url = $"systemusers?$filter=azureactivedirectoryobjectid eq '{azureAdObjectId}'&$select=systemuserid";
            var response = await _httpClient.GetAsync(url, ct);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Failed to lookup Dataverse user for Azure AD OID {AzureAdOid}: {StatusCode}",
                    azureAdObjectId, response.StatusCode);
                return null;
            }

            var result = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
            var values = result.GetProperty("value");

            if (values.GetArrayLength() == 0)
            {
                _logger.LogWarning("No Dataverse user found for Azure AD OID {AzureAdOid}", azureAdObjectId);
                return null;
            }

            var systemUserId = values[0].GetProperty("systemuserid").GetString();
            _logger.LogDebug("Mapped Azure AD OID {AzureAdOid} to Dataverse systemuserid {SystemUserId}",
                azureAdObjectId, systemUserId);

            return Guid.Parse(systemUserId!);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error looking up Dataverse user for Azure AD OID {AzureAdOid}", azureAdObjectId);
            return null;
        }
    }

    private async Task<DocumentRecord?> GetDocumentAsync(Guid documentId, CancellationToken ct)
    {
        var selectFields = "sprk_documentid,sprk_documentname,sprk_filename,sprk_filesize," +
                          "sprk_graphdriveid,sprk_graphitemid," +
                          "_sprk_checkedoutby_value,sprk_checkedoutdate,sprk_checkedindate," +
                          "_sprk_currentversionid_value,versionnumber";
        var expandFields = "sprk_CheckedOutBy($select=fullname)";

        var url = $"{DocumentEntitySet}({documentId})?$select={selectFields}&$expand={expandFields}";

        try
        {
            _logger.LogDebug("GetDocumentAsync: Requesting {Url}", url);
            var response = await _httpClient.GetAsync(url, ct);

            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                return null;
            }

            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync(ct);
                _logger.LogError("Dataverse returned {StatusCode} for document {DocumentId}. Response: {ErrorBody}",
                    response.StatusCode, documentId, errorBody);
                throw new HttpRequestException($"Dataverse error ({response.StatusCode}): {errorBody}");
            }

            var data = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
            return MapToDocumentRecord(data);
        }
        catch (HttpRequestException)
        {
            throw; // Re-throw with the detailed error
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
        // FileVersion stores version metadata including who checked out
        var payload = new Dictionary<string, object>
        {
            ["sprk_versionnumber"] = $"v{versionNumber}",
            ["sprk_Document@odata.bind"] = $"/{DocumentEntitySet}({documentId})",
            ["sprk_checkedoutdate"] = checkoutDate.ToString("o"),
            ["sprk_CheckedOutBy@odata.bind"] = $"/systemusers({userId})",
            ["statuscode"] = StatusCheckedOut
        };

        _logger.LogDebug("CreateFileVersionAsync: POST to {EntitySet} with payload: {Payload}",
            FileVersionEntitySet, System.Text.Json.JsonSerializer.Serialize(payload));

        var response = await _httpClient.PostAsJsonAsync(FileVersionEntitySet, payload, ct);

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(ct);
            _logger.LogError("CreateFileVersion failed with {StatusCode}. Response: {ErrorBody}",
                response.StatusCode, errorBody);
            throw new HttpRequestException($"CreateFileVersion error ({response.StatusCode}): {errorBody}");
        }

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
        var payload = new Dictionary<string, object?>
        {
            ["sprk_checkedoutdate"] = checkoutDate.ToString("o"),
            ["sprk_CheckedOutBy@odata.bind"] = $"/systemusers({userId})",
            ["versionnumber"] = versionNumber,
            ["sprk_CurrentVersionId@odata.bind"] = $"/{FileVersionEntitySet}({fileVersionId})",
            // Clear checkin fields when checking out
            ["sprk_checkedindate"] = null
        };

        _logger.LogDebug("UpdateDocumentCheckoutStatusAsync: PATCH {EntitySet}({DocumentId}) with payload: {Payload}",
            DocumentEntitySet, documentId, System.Text.Json.JsonSerializer.Serialize(payload));

        var response = await _httpClient.PatchAsJsonAsync(
            $"{DocumentEntitySet}({documentId})",
            payload,
            ct
        );

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(ct);
            _logger.LogError("UpdateDocumentCheckoutStatus failed with {StatusCode}. Response: {ErrorBody}",
                response.StatusCode, errorBody);
            throw new HttpRequestException($"UpdateDocumentCheckoutStatus error ({response.StatusCode}): {errorBody}");
        }

        // Disassociate CheckedInBy lookup (separate call for lookup null)
        await DisassociateLookupAsync(documentId, "sprk_CheckedInBy", ct);
    }

    private async Task UpdateFileVersionCheckInAsync(
        Guid fileVersionId,
        Guid userId,
        string? comment,
        long? fileSize,
        CancellationToken ct)
    {
        // FileVersion stores statuscode only - user info stored on Document entity
        var payload = new Dictionary<string, object>
        {
            ["sprk_checkedindate"] = DateTime.UtcNow.ToString("o"),
            ["statuscode"] = StatusCheckedIn
        };

        _logger.LogDebug("UpdateFileVersionCheckInAsync: PATCH {EntitySet}({FileVersionId}) with payload: {Payload}",
            FileVersionEntitySet, fileVersionId, System.Text.Json.JsonSerializer.Serialize(payload));

        var response = await _httpClient.PatchAsJsonAsync(
            $"{FileVersionEntitySet}({fileVersionId})",
            payload,
            ct
        );

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(ct);
            _logger.LogError("UpdateFileVersionCheckIn failed with {StatusCode}. Response: {ErrorBody}",
                response.StatusCode, errorBody);
            throw new HttpRequestException($"UpdateFileVersionCheckIn error ({response.StatusCode}): {errorBody}");
        }
    }

    private async Task UpdateFileVersionStatusAsync(
        Guid fileVersionId,
        int status,
        int? stateCode,
        CancellationToken ct)
    {
        var payload = new Dictionary<string, object>
        {
            ["statuscode"] = status
        };

        // If stateCode provided, set it (required when changing to a status in a different state)
        if (stateCode.HasValue)
        {
            payload["statecode"] = stateCode.Value;
        }

        _logger.LogDebug("UpdateFileVersionStatusAsync: PATCH {EntitySet}({FileVersionId}) with payload: {Payload}",
            FileVersionEntitySet, fileVersionId, System.Text.Json.JsonSerializer.Serialize(payload));

        var response = await _httpClient.PatchAsJsonAsync(
            $"{FileVersionEntitySet}({fileVersionId})",
            payload,
            ct
        );

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(ct);
            _logger.LogError("UpdateFileVersionStatus failed with {StatusCode}. Response: {ErrorBody}",
                response.StatusCode, errorBody);
            throw new HttpRequestException($"UpdateFileVersionStatus error ({response.StatusCode}): {errorBody}");
        }
    }

    private async Task ClearDocumentCheckoutStatusAsync(Guid documentId, Guid? checkInUserId, CancellationToken ct)
    {
        // Clear checkout fields
        var payload = new Dictionary<string, object?>
        {
            ["sprk_checkedoutdate"] = null
        };

        // If checking in (not discarding), set checkin info
        if (checkInUserId.HasValue)
        {
            payload["sprk_checkedindate"] = DateTime.UtcNow.ToString("o");
            payload["sprk_CheckedInBy@odata.bind"] = $"/systemusers({checkInUserId.Value})";
        }

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
        // Validate DriveId and ItemId before calling Graph API
        if (string.IsNullOrEmpty(driveId) || string.IsNullOrEmpty(itemId))
        {
            _logger.LogWarning("Cannot get edit URL: DriveId='{DriveId}', ItemId='{ItemId}' - one or both are empty",
                driveId, itemId);
            return "";
        }

        _logger.LogDebug("Getting edit URL for DriveId='{DriveId}', ItemId='{ItemId}'", driveId, itemId);

        try
        {
            // Get the embedview URL for editing
            var preview = await _speFileStore.GetPreviewUrlAsync(driveId, itemId, null, ct);

            // Convert embed.aspx to embedview for editing
            // embed.aspx is read-only, embedview provides full editing
            var editUrl = preview.PreviewUrl?.Replace("/embed.aspx?", "/embedview?") ?? "";

            return editUrl;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get edit URL from SPE. DriveId='{DriveId}', ItemId='{ItemId}'",
                driveId, itemId);
            throw;
        }
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
            CheckedOutAt = data.TryGetProperty("sprk_checkedoutdate", out var codate) && codate.ValueKind != JsonValueKind.Null
                ? DateTime.Parse(codate.GetString()!)
                : null,
            CheckedInAt = data.TryGetProperty("sprk_checkedindate", out var cidate) && cidate.ValueKind != JsonValueKind.Null
                ? DateTime.Parse(cidate.GetString()!)
                : null,
            CurrentVersionId = data.TryGetProperty("_sprk_currentversionid_value", out var cvid) && cvid.ValueKind != JsonValueKind.Null
                ? Guid.Parse(cvid.GetString()!)
                : null,
            VersionNumber = data.TryGetProperty("versionnumber", out var vn) && vn.ValueKind == JsonValueKind.Number
                ? vn.GetInt32()
                : 0
        };
    }

    private static Guid GetUserId(ClaimsPrincipal user)
    {
        // Try multiple claim types for Azure AD object ID
        var oid = user.FindFirst("oid")?.Value
            ?? user.FindFirst("http://schemas.microsoft.com/identity/claims/objectidentifier")?.Value
            ?? user.FindFirst("objectidentifier")?.Value;

        if (string.IsNullOrEmpty(oid))
        {
            // Log all claims for debugging
            var claimsList = string.Join("; ", user.Claims.Select(c => $"{c.Type}={c.Value}"));
            throw new InvalidOperationException($"Azure AD Object ID (oid) not found in token claims. Available claims: {claimsList}");
        }

        if (!Guid.TryParse(oid, out var userId))
        {
            throw new InvalidOperationException($"User ID claim value '{oid}' is not a valid GUID");
        }

        return userId;
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

    /// <summary>
    /// Document file info for re-indexing (populated after successful check-in).
    /// </summary>
    public DocumentFileInfo? FileInfo { get; init; }
}

/// <summary>
/// Document file information needed for re-indexing after check-in.
/// </summary>
public record DocumentFileInfo(
    string DriveId,
    string ItemId,
    string FileName,
    Guid DocumentId
);

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
