# Text Extraction Pattern

## When
Use when implementing document text extraction for AI analysis — selecting the correct extraction method based on file type and handling OBO authentication for SPE file downloads.

## Read These Files
1. `src/server/api/Sprk.Bff.Api/Services/Ai/TextExtractorService.cs` — extension-based routing and all extraction method implementations
2. `src/server/api/Sprk.Bff.Api/Services/Ai/ITextExtractor.cs` — interface definition and ExtractionResult model
3. `src/server/api/Sprk.Bff.Api/Services/Ai/AnalysisOrchestrationService.cs` — OBO file download pattern using HttpContext

## Constraints
- **ADR-013**: AI features extend BFF; no separate extraction service

## Key Rules
- MUST use OBO authentication (`DownloadFileAsUserAsync(httpContext, ...)`) — app-only tokens get 403 from SPE containers
- All analysis methods that access files MUST accept and propagate `HttpContext`
- Images (`png`, `jpg`, `gif`, `tiff`, `bmp`, `webp`) return `IsVisionRequired=true` with empty text — route to GPT-4 Vision
- Unsupported extensions return `Success=false`, never throw
- Four extraction methods: Native (text/md/json/csv), DocumentIntelligence (pdf/docx/doc), VisionOcr (images), Email (eml/msg)
