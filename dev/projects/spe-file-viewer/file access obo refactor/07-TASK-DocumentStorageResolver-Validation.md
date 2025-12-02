# Task 07: Add SPE Pointer Validation to DocumentStorageResolver

**Task ID**: `07-DocumentStorageResolver-Validation`
**Estimated Time**: 20 minutes
**Status**: Not Started (Optional)
**Dependencies**: 01-TASK-SdapProblemException

---

## 📋 Prompt

**OPTIONAL TASK**: Add SPE pointer validation to `DocumentStorageResolver` so that invalid documents are caught early. This is NOT required since FileAccessEndpoints (Task 05) already validate pointers, but provides defense-in-depth.

---

## ⚠️ Important Note

**This task is OPTIONAL**. FileAccessEndpoints (Task 05) already validates SPE pointers before calling Graph API:

```csharp
// FileAccessEndpoints.cs (Task 05)
ValidateSpePointers(document.GraphDriveId, document.GraphItemId, documentId);
```

**Consider skipping this task if**:
- You want to minimize scope and ship quickly
- Endpoint-level validation is sufficient
- You prefer flexibility (allow null pointers for documents not yet uploaded)

**Complete this task if**:
- You want early validation (fail fast)
- You want to prevent null pointers from propagating
- Other services besides FileAccessEndpoints use DocumentStorageResolver

---

## ✅ Todos

- [ ] Decide if this task is needed
- [ ] Open `src/api/Spe.Bff.Api/Infrastructure/Storage/DocumentStorageResolver.cs`
- [ ] Add `using Spe.Bff.Api.Infrastructure.Exceptions;`
- [ ] Add validation method (similar to FileAccessEndpoints)
- [ ] Call validation in `GetDocumentAsync` or `ResolveAsync` methods
- [ ] Build and test

---

## 📚 Required Knowledge

### Current DocumentStorageResolver Behavior
`DocumentStorageResolver` retrieves documents from Dataverse but does NOT validate SPE pointers:

```csharp
public async Task<DocumentEntity?> GetDocumentAsync(Guid documentId, CancellationToken ct)
{
    var document = await _dataverseService.GetDocumentAsync(documentId, ct);
    // Returns document even if GraphDriveId or GraphItemId are null
    return document;
}
```

**Problem**: Null pointers are only caught later when calling Graph API, resulting in less clear error messages.

### Proposed Changes
Validate pointers in `GetDocumentAsync` or create a separate method:

```csharp
public async Task<DocumentEntity> GetDocumentWithValidationAsync(Guid documentId, CancellationToken ct)
{
    var document = await GetDocumentAsync(documentId, ct);

    if (document == null)
    {
        throw new SdapProblemException(
            "document_not_found",
            "Document Not Found",
            $"Document with ID '{documentId}' does not exist",
            404
        );
    }

    ValidateSpePointers(document.GraphDriveId, document.GraphItemId, documentId.ToString());

    return document;
}
```

---

## 📂 Related Files

**File to Modify**:
- `src/api/Spe.Bff.Api/Infrastructure/Storage/DocumentStorageResolver.cs`

**Dependencies**:
- `SdapProblemException` (Task 01)

---

## 🎯 Implementation

### Option A: Validate in Existing Method (Breaking Change)

**Pros**: All callers get validation automatically
**Cons**: May break existing code that expects null pointers

```csharp
public async Task<DocumentEntity?> GetDocumentAsync(Guid documentId, CancellationToken ct)
{
    var document = await _dataverseService.GetDocumentAsync(documentId, ct);

    if (document == null)
    {
        return null; // Allow null documents
    }

    // Validate SPE pointers if document exists
    ValidateSpePointers(document.GraphDriveId, document.GraphItemId, documentId.ToString());

    return document;
}

private static void ValidateSpePointers(string? driveId, string? itemId, string documentId)
{
    // Same implementation as FileAccessEndpoints (Task 05)
    if (string.IsNullOrWhiteSpace(driveId))
    {
        throw new SdapProblemException(
            "mapping_missing_drive",
            "SPE Drive ID Missing",
            $"Document {documentId} does not have a Graph Drive ID (sprk_graphdriveid field is empty)",
            409
        );
    }

    if (!driveId.StartsWith("b!", StringComparison.Ordinal))
    {
        throw new SdapProblemException(
            "invalid_drive_id",
            "Invalid SPE Drive ID Format",
            $"Drive ID '{driveId}' does not start with 'b!'",
            400
        );
    }

    if (string.IsNullOrWhiteSpace(itemId))
    {
        throw new SdapProblemException(
            "mapping_missing_item",
            "SPE Item ID Missing",
            $"Document {documentId} does not have a Graph Item ID (sprk_graphitemid field is empty)",
            409
        );
    }

    if (itemId.Length < 20)
    {
        throw new SdapProblemException(
            "invalid_item_id",
            "Invalid SPE Item ID Format",
            $"Item ID '{itemId}' is too short (expected at least 20 characters)",
            400
        );
    }
}
```

### Option B: Create New Method (Non-Breaking)

**Pros**: Doesn't break existing callers
**Cons**: Callers must explicitly opt-in to validation

```csharp
public async Task<DocumentEntity> GetDocumentWithSpeValidationAsync(Guid documentId, CancellationToken ct)
{
    var document = await GetDocumentAsync(documentId, ct);

    if (document == null)
    {
        throw new SdapProblemException(
            "document_not_found",
            "Document Not Found",
            $"Document with ID '{documentId}' does not exist",
            404
        );
    }

    ValidateSpePointers(document.GraphDriveId, document.GraphItemId, documentId.ToString());

    return document;
}
```

Then update FileAccessEndpoints (Task 05) to use new method:

```csharp
// FileAccessEndpoints.cs
var document = await documentStorageResolver.GetDocumentWithSpeValidationAsync(docGuid, ct);
// No need for manual validation - already done
```

---

## ✅ Acceptance Criteria

### Build Success
- [ ] Project builds without errors

### Code Quality
- [ ] Validation logic matches FileAccessEndpoints (Task 05)
- [ ] Error codes consistent (`mapping_missing_drive`, `invalid_drive_id`, etc.)
- [ ] XML documentation explains when validation occurs

### Testing
1. Build: `dotnet build src/api/Spe.Bff.Api/Spe.Bff.Api.csproj`
2. Test with document missing SPE pointers
3. Verify appropriate SdapProblemException is thrown

---

## 📝 Notes

### Recommendation: Skip This Task

Since FileAccessEndpoints (Task 05) already validates SPE pointers, adding validation to DocumentStorageResolver provides minimal benefit:

**FileAccessEndpoints validation** (already implemented in Task 05):
```csharp
var document = await documentStorageResolver.GetDocumentAsync(docGuid, ct);
ValidateSpePointers(document.GraphDriveId, document.GraphItemId, documentId);
```

**DocumentStorageResolver validation** (this task):
```csharp
var document = await documentStorageResolver.GetDocumentWithSpeValidationAsync(docGuid, ct);
// Validation already done internally
```

**Trade-off**:
- **Duplication**: Validation logic exists in both places
- **Flexibility**: Some callers may want documents without SPE pointers (e.g., documents not yet uploaded)

**Recommendation**: Skip this task unless other services need validated documents.

---

## 🔗 Decision Point

**Before starting this task**, ask:

1. Do any services besides FileAccessEndpoints use DocumentStorageResolver?
2. Do those services need SPE pointer validation?
3. Is endpoint-level validation (Task 05) sufficient?

**If answers are No, No, Yes**: Skip this task.

---

**Previous Task**: [06-TASK-SpeFileStore-Update.md](./06-TASK-SpeFileStore-Update.md)
**Next Task**: [08-TASK-Program-DI-Update.md](./08-TASK-Program-DI-Update.md)
