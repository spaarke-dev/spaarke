# Microsoft Guidance on Streaming Uploads/Downloads for Spaarke

## Overview
This guide provides Microsoft's recommended patterns for implementing streaming file uploads and downloads in ASP.NET Core, specifically tailored for SharePoint Embedded (SPE) integration in the Spaarke platform.

## Key Principles

### Memory Management
- **Never buffer entire files in memory** - Stream directly to/from storage
- **Use minimal buffer sizes** - Default to 64KB, adjust based on profiling
- **Dispose streams properly** - Use `using` statements or `IAsyncDisposable`
- **Avoid `byte[]` arrays** - Use `Stream`, `Memory<T>`, or `ArrayPool<T>`

### Security Considerations
- **Validate file size limits** before processing
- **Scan content during streaming** for malware/threats
- **Enforce allowed file types** via content inspection, not just extension
- **Use antiforgery tokens** for browser-based uploads

## Streaming Upload Patterns

### Basic Streaming Upload
```csharp
app.MapPost("/api/documents/upload", async (HttpContext context) =>
{
    // Enforce size limit
    context.Features.Get<IHttpMaxRequestBodySizeFeature>()!.MaxRequestBodySize = 100_000_000; // 100MB
    
    // Disable request buffering
    context.Request.Body = new BufferingReadStream(
        context.Request.Body, 
        ArrayPool<byte>.Shared);
    
    var boundary = context.Request.GetMultipartBoundary();
    var reader = new MultipartReader(boundary, context.Request.Body);
    
    var section = await reader.ReadNextSectionAsync();
    while (section != null)
    {
        if (ContentDispositionHeaderValue.TryParse(
            section.ContentDisposition, 
            out var contentDisposition))
        {
            if (contentDisposition.IsFileDisposition())
            {
                var fileName = contentDisposition.FileName.Value;
                var trustedFileName = Path.GetRandomFileName();
                
                // Stream to temporary storage
                using var targetStream = File.Create(
                    Path.Combine(Path.GetTempPath(), trustedFileName));
                
                await section.Body.CopyToAsync(targetStream);
                
                // Process file after upload
                await ProcessUploadedFileAsync(trustedFileName, fileName);
            }
        }
        
        section = await reader.ReadNextSectionAsync();
    }
    
    return Results.Ok(new { message = "Upload successful" });
})
.DisableAntiforgery(); // Required for streaming
```

### Chunked Upload with Resume Support
```csharp
public class ChunkedUploadService
{
    private readonly IDistributedCache _cache;
    private readonly ILogger<ChunkedUploadService> _logger;
    private const int ChunkSize = 1024 * 1024 * 5; // 5MB chunks
    
    public async Task<UploadSession> InitiateUploadAsync(
        string fileName, 
        long fileSize,
        string contentType)
    {
        var sessionId = Guid.NewGuid().ToString();
        var tempPath = Path.Combine(Path.GetTempPath(), $"{sessionId}.tmp");
        
        var session = new UploadSession
        {
            SessionId = sessionId,
            FileName = fileName,
            FileSize = fileSize,
            ContentType = contentType,
            TempPath = tempPath,
            ChunkSize = ChunkSize,
            TotalChunks = (int)Math.Ceiling((double)fileSize / ChunkSize),
            UploadedChunks = new List<int>(),
            CreatedAt = DateTimeOffset.UtcNow,
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(24)
        };
        
        // Store session in cache
        await _cache.SetAsync(
            $"upload:{sessionId}",
            JsonSerializer.SerializeToUtf8Bytes(session),
            new DistributedCacheEntryOptions
            {
                AbsoluteExpiration = session.ExpiresAt
            });
        
        // Create sparse file to reserve space
        using var fs = new FileStream(tempPath, FileMode.Create, FileAccess.Write);
        fs.SetLength(fileSize);
        
        return session;
    }
    
    public async Task<ChunkUploadResult> UploadChunkAsync(
        string sessionId,
        int chunkIndex,
        Stream chunkStream,
        string contentRange)
    {
        // Retrieve session
        var sessionData = await _cache.GetAsync($"upload:{sessionId}");
        if (sessionData == null)
            throw new InvalidOperationException("Upload session not found or expired");
        
        var session = JsonSerializer.Deserialize<UploadSession>(sessionData)!;
        
        // Validate chunk index
        if (chunkIndex < 0 || chunkIndex >= session.TotalChunks)
            throw new ArgumentOutOfRangeException(nameof(chunkIndex));
        
        // Calculate positions
        var startPosition = (long)chunkIndex * ChunkSize;
        var expectedSize = Math.Min(ChunkSize, session.FileSize - startPosition);
        
        // Write chunk to file
        using (var fileStream = new FileStream(
            session.TempPath, 
            FileMode.Open, 
            FileAccess.Write, 
            FileShare.None))
        {
            fileStream.Position = startPosition;
            
            var buffer = ArrayPool<byte>.Shared.Rent(81920); // 80KB buffer
            try
            {
                var totalWritten = 0L;
                int bytesRead;
                
                while ((bytesRead = await chunkStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                {
                    if (totalWritten + bytesRead > expectedSize)
                    {
                        // Prevent writing beyond expected chunk size
                        bytesRead = (int)(expectedSize - totalWritten);
                    }
                    
                    await fileStream.WriteAsync(buffer, 0, bytesRead);
                    totalWritten += bytesRead;
                    
                    if (totalWritten >= expectedSize)
                        break;
                }
                
                await fileStream.FlushAsync();
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }
        
        // Update session
        if (!session.UploadedChunks.Contains(chunkIndex))
        {
            session.UploadedChunks.Add(chunkIndex);
        }
        
        await _cache.SetAsync(
            $"upload:{sessionId}",
            JsonSerializer.SerializeToUtf8Bytes(session),
            new DistributedCacheEntryOptions
            {
                AbsoluteExpiration = session.ExpiresAt
            });
        
        // Check if upload is complete
        if (session.UploadedChunks.Count == session.TotalChunks)
        {
            await FinalizeUploadAsync(session);
            return new ChunkUploadResult
            {
                IsComplete = true,
                NextExpectedRanges = Array.Empty<string>()
            };
        }
        
        // Calculate missing chunks
        var missingChunks = Enumerable.Range(0, session.TotalChunks)
            .Except(session.UploadedChunks)
            .Select(i => $"{i * ChunkSize}-{Math.Min((i + 1) * ChunkSize - 1, session.FileSize - 1)}")
            .ToArray();
        
        return new ChunkUploadResult
        {
            IsComplete = false,
            NextExpectedRanges = missingChunks
        };
    }
}
```

### Direct SharePoint Embedded Upload
```csharp
public class SpeStreamingUpload
{
    private readonly GraphServiceClient _graphClient;
    private const int MaxSliceSize = 320 * 1024 * 10; // 3.2 MB per Graph docs
    
    public async Task<DriveItem> UploadLargeFileAsync(
        string driveId,
        string fileName,
        Stream fileStream,
        IProgress<long>? progress = null)
    {
        // Create upload session
        var uploadSession = await _graphClient
            .Drives[driveId]
            .Root
            .ItemWithPath(fileName)
            .CreateUploadSession
            .PostAsync(new CreateUploadSessionPostRequestBody
            {
                Item = new DriveItemUploadableProperties
                {
                    AdditionalData = new Dictionary<string, object>
                    {
                        ["@microsoft.graph.conflictBehavior"] = "rename",
                        ["name"] = fileName
                    }
                }
            });
        
        // Upload using SDK's LargeFileUploadTask
        var uploadTask = new LargeFileUploadTask<DriveItem>(
            uploadSession, 
            fileStream, 
            MaxSliceSize);
        
        // Track progress
        IProgress<long> uploadProgress = progress ?? new Progress<long>();
        
        var uploadResult = await uploadTask.UploadAsync(uploadProgress);
        
        if (!uploadResult.UploadSucceeded)
        {
            if (uploadResult.ItemResponse == null && uploadResult.UploadStatusCode.HasValue)
            {
                throw new HttpRequestException(
                    $"Upload failed with status: {uploadResult.UploadStatusCode}");
            }
        }
        
        return uploadResult.ItemResponse!;
    }
    
    public async Task<DriveItem> UploadSmallFileAsync(
        string driveId,
        string fileName,
        Stream fileStream)
    {
        // For files < 4MB, use direct upload
        if (fileStream.Length > 4 * 1024 * 1024)
        {
            throw new ArgumentException("File too large for simple upload");
        }
        
        return await _graphClient
            .Drives[driveId]
            .Root
            .ItemWithPath(fileName)
            .Content
            .PutAsync(fileStream);
    }
}
```

## Streaming Download Patterns

### Basic Streaming Download
```csharp
app.MapGet("/api/documents/{documentId}/download", async (
    Guid documentId,
    SpeFileStore fileStore,
    HttpContext context) =>
{
    var document = await fileStore.GetDocumentMetadataAsync(documentId);
    if (document == null)
        return Results.NotFound();
    
    // Set response headers
    context.Response.ContentType = document.ContentType ?? "application/octet-stream";
    context.Response.Headers.ContentDisposition = 
        $"attachment; filename=\"{document.FileName}\"";
    context.Response.Headers.ContentLength = document.FileSize;
    
    // Stream file directly to response
    await fileStore.StreamToResponseAsync(documentId, context.Response.Body);
    
    return Results.Empty;
})
.RequireAuthorization();

// Implementation in SpeFileStore
public async Task StreamToResponseAsync(Guid documentId, Stream outputStream)
{
    var driveItem = await GetDriveItemAsync(documentId);
    
    using var downloadStream = await _graphClient
        .Drives[driveItem.ParentReference.DriveId]
        .Items[driveItem.Id]
        .Content
        .GetAsync();
    
    await downloadStream.CopyToAsync(outputStream);
}