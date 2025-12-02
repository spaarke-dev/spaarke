# Task 01: Create SdapProblemException Class

**Task ID**: `01-SdapProblemException`
**Estimated Time**: 15 minutes
**Status**: Not Started
**Dependencies**: None

---

## 📋 Prompt

Create a custom exception class `SdapProblemException` that maps to RFC 7807 Problem Details format. This exception will be used throughout the BFF API to provide structured error responses with correlation IDs.

---

## ✅ Todos

- [ ] Create new file `SdapProblemException.cs` in `Spe.Bff.Api/Infrastructure/Exceptions/`
- [ ] Implement exception with properties: Code, Title, Detail, StatusCode
- [ ] Add XML documentation comments
- [ ] Verify namespace is `Spe.Bff.Api.Infrastructure.Exceptions`
- [ ] Build project to verify no compilation errors

---

## 📚 Required Knowledge

### RFC 7807 Problem Details Format
RFC 7807 defines a standard JSON structure for HTTP error responses:

```json
{
  "type": "https://example.com/probs/invalid-id",
  "title": "Invalid Document ID",
  "detail": "Document ID 'abc' is not a valid GUID format",
  "status": 400,
  "correlationId": "trace-12345"
}
```

### Exception Properties
- **Code** (string): Machine-readable error code (e.g., `"invalid_id"`, `"mapping_missing_drive"`)
- **Title** (string): Human-readable summary (e.g., `"Invalid Document ID"`)
- **Detail** (string): Specific error details (e.g., `"Document ID 'abc' is not a valid GUID"`)
- **StatusCode** (int): HTTP status code (400, 404, 409, etc.)

### Common Error Codes
From technical review:
- `invalid_id` - Invalid GUID format (400)
- `document_not_found` - Document doesn't exist in Dataverse (404)
- `mapping_missing_drive` - SPE driveId field is null/empty (409)
- `mapping_missing_item` - SPE itemId field is null/empty (409)
- `invalid_drive_id` - driveId doesn't start with "b!" (400)
- `invalid_item_id` - itemId is less than 20 characters (400)

---

## 📂 Related Files

**New File to Create**:
- `src/api/Spe.Bff.Api/Infrastructure/Exceptions/SdapProblemException.cs`

**Files That Will Use This Exception** (in later tasks):
- `src/api/Spe.Bff.Api/Api/FileAccessEndpoints.cs`
- `src/api/Spe.Bff.Api/Infrastructure/Storage/DocumentStorageResolver.cs`
- `src/api/Spe.Bff.Api/Program.cs` (global exception handler)

---

## 🎯 Implementation

### File: `src/api/Spe.Bff.Api/Infrastructure/Exceptions/SdapProblemException.cs`

```csharp
namespace Spe.Bff.Api.Infrastructure.Exceptions;

/// <summary>
/// Custom exception for SDAP validation and business logic errors.
/// Maps to RFC 7807 Problem Details format with structured error codes.
/// </summary>
/// <remarks>
/// Used to throw structured errors that the global exception handler converts to
/// Problem Details JSON responses with correlation IDs.
///
/// Common error codes:
/// - invalid_id: Invalid GUID format
/// - document_not_found: Document doesn't exist
/// - mapping_missing_drive: SPE driveId field is null/empty
/// - mapping_missing_item: SPE itemId field is null/empty
/// - invalid_drive_id: driveId doesn't start with "b!"
/// - invalid_item_id: itemId is less than 20 characters
/// </remarks>
public class SdapProblemException : Exception
{
    /// <summary>
    /// Machine-readable error code (e.g., "invalid_id", "mapping_missing_drive").
    /// </summary>
    public string Code { get; }

    /// <summary>
    /// Human-readable error summary (e.g., "Invalid Document ID").
    /// </summary>
    public string Title { get; }

    /// <summary>
    /// Specific error details (e.g., "Document ID 'abc' is not a valid GUID format").
    /// </summary>
    public string Detail { get; }

    /// <summary>
    /// HTTP status code (400, 404, 409, etc.).
    /// </summary>
    public int StatusCode { get; }

    /// <summary>
    /// Creates a new SdapProblemException.
    /// </summary>
    /// <param name="code">Machine-readable error code</param>
    /// <param name="title">Human-readable error summary</param>
    /// <param name="detail">Specific error details</param>
    /// <param name="statusCode">HTTP status code (default: 400)</param>
    public SdapProblemException(string code, string title, string detail, int statusCode = 400)
        : base(detail)
    {
        Code = code ?? throw new ArgumentNullException(nameof(code));
        Title = title ?? throw new ArgumentNullException(nameof(title));
        Detail = detail ?? throw new ArgumentNullException(nameof(detail));
        StatusCode = statusCode;
    }
}
```

---

## ✅ Acceptance Criteria

### Build Success
- [ ] Project builds without errors
- [ ] No compiler warnings

### Code Quality
- [ ] All properties are public and read-only
- [ ] Constructor validates non-null parameters
- [ ] XML documentation is complete
- [ ] Namespace matches project structure

### Verification Steps
1. Create the file in correct location
2. Build the solution: `dotnet build src/api/Spe.Bff.Api/Spe.Bff.Api.csproj`
3. Verify no errors or warnings
4. Commit the file with message: `"Add SdapProblemException for RFC 7807 error handling"`

---

## 📝 Notes

- This exception class is intentionally simple - complexity is in the global exception handler
- StatusCode defaults to 400 (Bad Request) for validation errors
- The `Detail` property is passed to base `Exception` for stack trace integration
- No need for custom serialization - the global exception handler will construct the JSON response

---

**Next Task**: [02-TASK-GlobalExceptionHandler.md](./02-TASK-GlobalExceptionHandler.md)
