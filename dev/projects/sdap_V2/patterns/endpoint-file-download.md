# Pattern: File Download Endpoint

**Use For**: Implementing file download from SPE containers
**Task**: Creating GET endpoints that return file streams
**Time**: 10-15 minutes

---

## Quick Copy-Paste

```csharp
group.MapGet("containers/{containerId}/files/{fileId}", DownloadFile)
    .WithName("DownloadFileOBO")
    .RequireAuthorization("candownloadfiles")
    .WithRateLimiting("graph-read")
    .Produces<FileContentHttpResult>(200)
    .Produces(401)
    .Produces(403)
    .Produces(404);

private static async Task<IResult> DownloadFile(
    [FromRoute] string containerId,
    [FromRoute] string fileId,
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

        logger.LogInformation(
            "Downloading file {FileId} from container {ContainerId}",
            fileId, containerId);

        var stream = await fileStore.DownloadFileAsync(
            containerId,
            fileId,
            token,
            cancellationToken);

        var metadata = await fileStore.GetFileMetadataAsync(
            containerId,
            fileId,
            token,
            cancellationToken);

        logger.LogInformation("File downloaded successfully: {FileId}", fileId);

        return Results.File(
            stream,
            contentType: metadata.MimeType ?? "application/octet-stream",
            fileDownloadName: metadata.Name,
            enableRangeProcessing: true);
    }
    catch (ServiceException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
    {
        logger.LogWarning("File not found: {FileId}", fileId);
        return Results.NotFound(new { error = "File not found" });
    }
    catch (ServiceException ex) when (ex.StatusCode == HttpStatusCode.Forbidden)
    {
        logger.LogWarning("Access denied: {FileId}", fileId);
        return Results.Problem("Access denied", statusCode: 403);
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Download failed for {FileId}", fileId);
        return Results.Problem("Download failed", statusCode: 500);
    }
}
```

---

## Key Points

- Use `Results.File()` for streaming
- Set `contentType` from metadata (fallback to octet-stream)
- Set `fileDownloadName` for proper filename in browser
- Enable `enableRangeProcessing` for large files (resumable downloads)
- Get metadata separately for content type

---

## Checklist

- [ ] Route uses GET verb
- [ ] Authorization policy specified
- [ ] Content type from metadata
- [ ] File download name set
- [ ] Range processing enabled
- [ ] 404 handler for missing files
- [ ] Stream returned (not read into memory)

---

## Related Files

- Apply to: `src/api/Spe.Bff.Api/Api/OBOEndpoints.cs`
- Uses: `SpeFileStore.DownloadFileAsync()` and `GetFileMetadataAsync()`
