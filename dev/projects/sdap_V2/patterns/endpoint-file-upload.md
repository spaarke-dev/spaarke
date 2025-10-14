# Pattern: File Upload Endpoint

**Use For**: Implementing file upload to SPE containers with OBO flow
**Task**: Creating PUT endpoints for file upload
**Time**: 15-20 minutes

---

## Quick Copy-Paste

```csharp
group.MapPut("containers/{containerId}/files/{fileName}", UploadFile)
    .WithName("UploadFileOBO")
    .RequireAuthorization("canuploadfiles")
    .WithRateLimiting("upload-heavy")
    .Produces<FileUploadResult>(200)
    .Produces(401)
    .Produces(403)
    .Produces(429);

private static async Task<IResult> UploadFile(
    [FromRoute] string containerId,
    [FromRoute] string fileName,
    HttpRequest request,
    SpeFileStore fileStore,
    ILogger<OBOEndpoints> logger,
    CancellationToken cancellationToken)
{
    try
    {
        var token = ExtractBearerToken(request);
        if (string.IsNullOrEmpty(token))
            return Results.Unauthorized();

        if (!IsValidFileName(fileName))
            return Results.BadRequest(new { error = "Invalid file name" });

        logger.LogInformation(
            "Uploading file {FileName} to container {ContainerId}",
            fileName, containerId);

        var result = await fileStore.UploadFileAsync(
            containerId,
            fileName,
            request.Body,
            token,
            cancellationToken);

        logger.LogInformation("File uploaded successfully: {ItemId}", result.ItemId);

        return Results.Ok(result);
    }
    catch (ServiceException ex) when (ex.StatusCode == HttpStatusCode.Forbidden)
    {
        logger.LogWarning("Access denied: {Message}", ex.Message);
        return Results.Problem("Access denied", statusCode: 403);
    }
    catch (ServiceException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
    {
        logger.LogWarning("Container not found: {ContainerId}", containerId);
        return Results.NotFound(new { error = "Container not found" });
    }
    catch (ServiceException ex) when (ex.StatusCode == HttpStatusCode.TooManyRequests)
    {
        var retryAfter = ex.ResponseHeaders?.RetryAfter?.Delta ?? TimeSpan.FromSeconds(60);
        logger.LogWarning("Throttled by Graph API, retry after {RetryAfter}", retryAfter);
        return Results.StatusCode(429);
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Upload failed for {FileName}", fileName);
        return Results.Problem("An unexpected error occurred", statusCode: 500);
    }
}

private static string? ExtractBearerToken(HttpRequest request)
{
    var authHeader = request.Headers.Authorization.ToString();
    return authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
        ? authHeader.Substring("Bearer ".Length).Trim()
        : null;
}

private static bool IsValidFileName(string fileName)
{
    if (string.IsNullOrWhiteSpace(fileName)) return false;
    var blockedExtensions = new[] { ".exe", ".dll", ".bat", ".cmd", ".ps1", ".vbs" };
    return !blockedExtensions.Any(ext => fileName.EndsWith(ext, StringComparison.OrdinalIgnoreCase));
}
```

---

## Checklist

- [ ] Route uses PUT verb
- [ ] Authorization policy specified
- [ ] Rate limiting applied
- [ ] All status codes documented (.Produces)
- [ ] Token extraction
- [ ] File name validation
- [ ] All ServiceException cases handled
- [ ] Structured logging (Info, Warning, Error)
- [ ] CancellationToken parameter

---

## Related Files

- Apply to: `src/api/Spe.Bff.Api/Api/OBOEndpoints.cs`
- Returns: `FileUploadResult` DTO (see `dto-file-upload-result.md`)
- Uses: `SpeFileStore` (see `service-spe-file-store.md`)
