# Text Extraction Pattern

> **Domain**: AI / Document Processing
> **Last Validated**: 2025-01-03
> **Source ADRs**: ADR-013

---

## Canonical Implementations

| File | Purpose |
|------|---------|
| `src/server/api/Sprk.Bff.Api/Services/Ai/TextExtractorService.cs` | Extraction logic |
| `src/server/api/Sprk.Bff.Api/Services/Ai/ITextExtractor.cs` | Interface definition |
| `src/server/api/Sprk.Bff.Api/Services/Ai/AnalysisOrchestrationService.cs` | File download + extraction orchestration |

---

## File Download (OBO Authentication Required)

Before text extraction, files must be downloaded from SharePoint Embedded (SPE). **This requires On-Behalf-Of (OBO) authentication** using the user's token, not app-only authentication.

```csharp
// ✅ CORRECT: OBO authentication via HttpContext
private async Task<string> ExtractDocumentTextAsync(
    DocumentEntity document,
    HttpContext httpContext,  // Required for OBO token exchange
    CancellationToken cancellationToken)
{
    // Download using user's delegated permissions
    using var fileStream = await _speFileStore.DownloadFileAsUserAsync(
        httpContext,
        document.GraphDriveId!,
        document.GraphItemId!,
        cancellationToken);

    // Extract text from stream
    var result = await _textExtractor.ExtractTextAsync(
        fileStream,
        document.FileName ?? "document",
        cancellationToken);

    return result.Text;
}

// ❌ WRONG: App-only authentication returns 403 Access Denied
// var fileStream = await _speFileStore.DownloadFileAsync(driveId, itemId, ct);
```

**Why OBO is required:**
- SPE containers grant file access based on user permissions, not app permissions
- App-only tokens (`_factory.ForApp()`) don't inherit container-level access
- The user's token must be exchanged via OBO (`_factory.ForUserAsync(httpContext, ct)`)

**HttpContext propagation:**
All analysis methods that need file access must accept and propagate `HttpContext`:
- `ExecuteAnalysisAsync(request, httpContext, ct)`
- `ContinueAnalysisAsync(analysisId, message, httpContext, ct)`
- `ResumeAnalysisAsync(analysisId, request, httpContext, ct)`

---

## Extraction Method Selection

```csharp
public async Task<ExtractionResult> ExtractTextAsync(
    Stream fileStream,
    string fileName,
    CancellationToken ct = default)
{
    var extension = Path.GetExtension(fileName).ToLowerInvariant();

    return extension switch
    {
        ".txt" or ".md" or ".json" or ".csv" or ".xml" or ".html"
            => await ExtractNativeAsync(fileStream, ct),

        ".pdf" or ".docx" or ".doc"
            => await ExtractWithDocumentIntelligenceAsync(fileStream, ct),

        ".png" or ".jpg" or ".jpeg" or ".gif" or ".tiff" or ".bmp" or ".webp"
            => CreateVisionResult(),

        ".eml" or ".msg"
            => await ExtractEmailAsync(fileStream, extension, ct),

        _ => new ExtractionResult { Success = false, Text = $"Unsupported: {extension}" }
    };
}
```

---

## Extraction Result Model

```csharp
public class ExtractionResult
{
    public bool Success { get; set; }
    public string Text { get; set; } = string.Empty;
    public int CharacterCount { get; set; }
    public ExtractionMethod Method { get; set; }
    public bool IsVisionRequired { get; set; }  // Needs GPT-4 Vision
    public bool IsEmail { get; set; }
    public EmailMetadata? EmailMetadata { get; set; }
}

public enum ExtractionMethod
{
    Native,
    DocumentIntelligence,
    VisionOcr,
    Email
}
```

---

## Native Text Extraction

```csharp
private async Task<ExtractionResult> ExtractNativeAsync(Stream stream, CancellationToken ct)
{
    using var reader = new StreamReader(stream);
    var text = await reader.ReadToEndAsync(ct);

    return new ExtractionResult
    {
        Success = true,
        Text = text,
        CharacterCount = text.Length,
        Method = ExtractionMethod.Native
    };
}
```

---

## Document Intelligence Extraction

For PDFs and Office documents via Azure AI Document Intelligence:

```csharp
private async Task<ExtractionResult> ExtractWithDocumentIntelligenceAsync(
    Stream stream,
    CancellationToken ct)
{
    var operation = await _documentClient.AnalyzeDocumentAsync(
        WaitUntil.Completed,
        "prebuilt-read",
        stream,
        cancellationToken: ct);

    var result = operation.Value;
    var text = string.Join("\n", result.Pages.SelectMany(p => p.Lines).Select(l => l.Content));

    return new ExtractionResult
    {
        Success = true,
        Text = text,
        CharacterCount = text.Length,
        Method = ExtractionMethod.DocumentIntelligence
    };
}
```

---

## Vision Route (Images)

Images skip text extraction and go directly to GPT-4 Vision:

```csharp
private ExtractionResult CreateVisionResult()
{
    return new ExtractionResult
    {
        Success = true,
        Text = string.Empty,  // No text - will use vision
        Method = ExtractionMethod.VisionOcr,
        IsVisionRequired = true
    };
}
```

---

## Email Extraction

```csharp
private async Task<ExtractionResult> ExtractEmailAsync(Stream stream, string ext, CancellationToken ct)
{
    var email = ext == ".eml"
        ? await ParseEmlAsync(stream, ct)
        : await ParseMsgAsync(stream, ct);

    return new ExtractionResult
    {
        Success = true,
        Text = email.Body,
        CharacterCount = email.Body.Length,
        Method = ExtractionMethod.Email,
        IsEmail = true,
        EmailMetadata = new EmailMetadata
        {
            Subject = email.Subject,
            From = email.From,
            To = email.To,
            Date = email.Date,
            AttachmentCount = email.Attachments.Count
        }
    };
}
```

---

## Key Points

1. **Extension-based routing** - Determine method from file extension
2. **Vision fallback** - Images skip extraction, use GPT-4V directly
3. **Email metadata** - Extract sender/recipient/subject separately
4. **Graceful failures** - Return success=false, don't throw

---

## Related Patterns

- [Streaming Endpoints](streaming-endpoints.md) - Use extracted text
- [Analysis Scopes](analysis-scopes.md) - Context building

---

**Lines**: ~100
