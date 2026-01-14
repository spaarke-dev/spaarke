# Task 001 Implementation Notes

## Date: December 4, 2025

## Implementation Deviation

The task specified file locations that didn't match the existing project structure:

**Task specified:**
- `src/shared/Spaarke.SharePointEmbedded/Utilities/DesktopUrlBuilder.cs`
- `tests/unit/Spaarke.SharePointEmbedded.Tests/DesktopUrlBuilderTests.cs`

**Actual implementation:**
- `src/server/shared/Spaarke.Core/Utilities/DesktopUrlBuilder.cs`
- `tests/unit/Spaarke.Core.Tests/DesktopUrlBuilderTests.cs`

**Rationale:**
1. No `src/shared/` directory exists - shared libraries are in `src/server/shared/`
2. `Spaarke.Core` already exists as the shared utility library
3. Created new `Spaarke.Core.Tests` project for testing shared utilities
4. Follows ADR-010: utility is a static class with no DI registration

## Additional Features

Added `IsSupported(string? mimeType)` method to allow checking if a MIME type can be opened in desktop mode without constructing the URL.

## Test Coverage

32 unit tests covering:
- Word (OpenXML + legacy) MIME type mapping
- Excel (OpenXML + legacy) MIME type mapping
- PowerPoint (OpenXML + legacy) MIME type mapping
- Unsupported MIME types (PDF, images, etc.)
- Null/empty input handling
- URL encoding (special characters, query strings)
- Case-insensitive MIME type matching
- IsSupported() helper method
