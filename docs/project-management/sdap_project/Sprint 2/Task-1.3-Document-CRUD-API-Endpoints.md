# Task 1.3: Document CRUD API Endpoints

**PHASE:** Foundation Setup (Days 1-5)
**STATUS:** 🔴 READY TO START
**DEPENDENCIES:** Task 1.1 (Entity Creation), Task 1.2 (DataverseService - COMPLETED)
**ESTIMATED TIME:** 8-12 hours
**PRIORITY:** HIGH - Core functionality implementation

---

## 📋 TASK OVERVIEW

### **Objective**
Create comprehensive REST API endpoints for document and file management that bridge Power Platform UI with backend services. This task implements the API layer following enterprise patterns for security, performance, and maintainability.

### **Business Context**
- Building on existing BFF API patterns with minimal APIs
- Need full CRUD operations for documents and their associated files
- All operations must include proper authorization checks
- File operations integrate with existing SharePoint Embedded functionality
- Must support async processing patterns with background jobs

### **Architecture Impact**
This task delivers:
- REST API endpoints for document lifecycle management
- File upload/download integration with SharePoint Embedded
- Proper authorization and security throughout
- Integration with background job processing
- Health checks and monitoring endpoints

---

## 🔍 PRIOR TASK REVIEW AND VALIDATION

### **Task 1.1 Results Review**
Before starting this task, verify the following from Task 1.1:

#### **Entity Validation Checklist**
- [ ] **sprk_document entity exists** with all required fields
- [ ] **sprk_container entity updated** with document count field
- [ ] **Relationship working** between containers and documents
- [ ] **Security roles configured** and tested
- [ ] **DataverseService integration confirmed** via test script

#### **Field Mapping Verification**
Confirm these field mappings work in DataverseService:
```csharp
// Verify these mappings work correctly:
document["sprk_name"] = request.Name;
document["sprk_containerid"] = new EntityReference("sprk_container", Guid.Parse(request.ContainerId));
document["sprk_documentdescription"] = request.Description;
document["sprk_hasfile"] = false;
document["sprk_status"] = new OptionSetValue(1); // Draft
```

#### **Task 1.2 Status Confirmation**
Verify DataverseService implementation is working:
- [ ] **IDataverseService interface** properly defined
- [ ] **DataverseService implementation** connects to Dataverse
- [ ] **Authentication working** with managed identity
- [ ] **Test endpoints responding** at `/healthz/dataverse`
- [ ] **Models aligned** with entity structure

### **Gaps and Corrections**
If any issues found in prior tasks:

1. **Entity Field Mismatches**: Update DataverseService field mappings
2. **Authentication Issues**: Verify managed identity configuration
3. **Missing Security**: Ensure proper role-based access is configured
4. **Performance Problems**: Address any slow entity operations

---

## 🎯 AI AGENT INSTRUCTIONS

### **CONTEXT FOR AI AGENT**
You are implementing enterprise-grade REST API endpoints that will be consumed by Power Platform UI and external integrations. The APIs must follow modern patterns for security, performance, and maintainability.

### **ENTERPRISE PATTERNS TO IMPLEMENT**

#### **Core Architectural Patterns**
- **Minimal APIs with route groups** for clean organization and discoverability
- **FluentValidation** for robust request validation and error messaging
- **OpenAPI/Swagger** with comprehensive documentation for API consumers
- **Correlation IDs** throughout the request pipeline for distributed tracing
- **Result pattern** for consistent error handling and response structure

#### **Performance Considerations**
- Implement response caching for read operations
- Use async streaming for large file uploads
- Add rate limiting to prevent abuse
- Include compression for API responses

### **TECHNICAL REQUIREMENTS**

#### **1. Document Management Endpoints (/api/v1/documents)**

Create comprehensive CRUD endpoints:

```csharp
// Route group configuration
var documentsGroup = app.MapGroup("/api/v1/documents")
    .WithTags("Document Management")
    .RequireAuthorization()
    .WithOpenApi();
```

**Required Endpoints:**

| Method | Route | Description | Request Model | Response Model |
|--------|-------|-------------|---------------|----------------|
| POST | `/` | Create new document record | CreateDocumentRequest | DocumentEntity |
| GET | `/{id}` | Get document with metadata | - | DocumentEntity |
| PUT | `/{id}` | Update document metadata | UpdateDocumentRequest | DocumentEntity |
| DELETE | `/{id}` | Delete document and file | - | 204 No Content |
| GET | `/` | List documents with filtering | QueryParameters | PagedResult<DocumentEntity> |
| GET | `/{id}/history` | Get document audit history | - | AuditHistoryResponse |
| POST | `/{id}/clone` | Clone document structure | CloneDocumentRequest | DocumentEntity |

#### **2. File Management Endpoints (/api/v1/documents/{documentId}/files)**

Integrate with SharePoint Embedded:

| Method | Route | Description | Request Model | Response Model |
|--------|-------|-------------|---------------|----------------|
| POST | `/` | Initiate file upload | FileUploadRequest | FileUploadResponse |
| GET | `/` | Get secure download URL | - | FileDownloadResponse |
| PUT | `/` | Replace existing file | FileReplaceRequest | FileOperationResponse |
| DELETE | `/` | Delete file | - | 204 No Content |
| GET | `/versions` | List file version history | - | FileVersionsResponse |
| POST | `/versions/{versionId}/restore` | Restore file version | - | FileOperationResponse |

#### **3. Container Integration Endpoints (/api/v1/containers)**

| Method | Route | Description | Request Model | Response Model |
|--------|-------|-------------|---------------|----------------|
| GET | `/{containerId}/documents` | List documents in container | QueryParameters | PagedResult<DocumentEntity> |
| GET | `/{containerId}/statistics` | Get container usage stats | - | ContainerStatisticsResponse |
| POST | `/{containerId}/bulk-upload` | Initiate bulk upload session | BulkUploadRequest | BulkUploadResponse |

#### **4. Health and Diagnostics Endpoints (/api/v1/health)**

| Method | Route | Description | Response |
|--------|-------|-------------|----------|
| GET | `/documents` | Document service health | HealthCheckResponse |
| GET | `/files` | File storage health | HealthCheckResponse |
| GET | `/detailed` | Comprehensive health status | DetailedHealthResponse |

### **MODEL DESIGN PATTERNS**

#### **Request Models with Validation**
```csharp
public class CreateDocumentRequest
{
    [Required, StringLength(255)]
    public string Name { get; set; } = string.Empty;

    [Required]
    public string ContainerId { get; set; } = string.Empty;

    [StringLength(2000)]
    public string? Description { get; set; }

    [FileExtensions(Extensions = ".pdf,.docx,.xlsx,.pptx,.txt")]
    public IFormFile? InitialFile { get; set; }
}

public class UpdateDocumentRequest
{
    [StringLength(255)]
    public string? Name { get; set; }

    [StringLength(2000)]
    public string? Description { get; set; }

    public DocumentStatus? Status { get; set; }
}

public class QueryParameters
{
    public string? Filter { get; set; }
    public string? Search { get; set; }
    public string? OrderBy { get; set; }
    public int Skip { get; set; } = 0;
    public int Take { get; set; } = 50;
    public string? ContainerId { get; set; }
    public DocumentStatus? Status { get; set; }
}
```

#### **Response Models**
```csharp
public class ApiResponse<T>
{
    public T Data { get; set; } = default!;
    public ApiMetadata Metadata { get; set; } = new();
    public IEnumerable<string>? Errors { get; set; }
}

public class PagedResult<T>
{
    public IEnumerable<T> Items { get; set; } = Enumerable.Empty<T>();
    public int TotalCount { get; set; }
    public string? NextCursor { get; set; }
    public string? PreviousCursor { get; set; }
    public bool HasNextPage { get; set; }
    public bool HasPreviousPage { get; set; }
}

public class ApiMetadata
{
    public string RequestId { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public string Version { get; set; } = "v1";
}
```

#### **File Operation Models**
```csharp
public class FileUploadRequest
{
    [Required]
    public IFormFile File { get; set; } = null!;

    public bool ReplaceExisting { get; set; } = false;
    public string? VersionComment { get; set; }
}

public class FileUploadResponse
{
    public string UploadSessionId { get; set; } = string.Empty;
    public string UploadUrl { get; set; } = string.Empty;
    public TimeSpan ExpiresIn { get; set; }
    public long MaxFileSize { get; set; }
    public string[] AllowedMimeTypes { get; set; } = Array.Empty<string>();
}

public class FileDownloadResponse
{
    public string DownloadUrl { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public long FileSize { get; set; }
    public string MimeType { get; set; } = string.Empty;
    public TimeSpan ExpiresIn { get; set; }
}
```

### **AUTHORIZATION REQUIREMENTS**

#### **Document-Level Authorization**
```csharp
// Custom authorization handler for document access
public class DocumentAccessRequirement : IAuthorizationRequirement
{
    public DocumentOperation Operation { get; }
    public DocumentAccessRequirement(DocumentOperation operation)
    {
        Operation = operation;
    }
}

public enum DocumentOperation
{
    Read,
    Create,
    Update,
    Delete,
    FileUpload,
    FileDownload,
    FileDelete
}
```

#### **Authorization Implementation Pattern**
```csharp
// In endpoint implementation
app.MapGet("/api/v1/documents/{id}", async (
    string id,
    HttpContext context,
    IAuthorizationService authService,
    IDataverseService dataverseService,
    ILogger<Program> logger) =>
{
    var authResult = await authService.AuthorizeAsync(
        context.User,
        id,
        new DocumentAccessRequirement(DocumentOperation.Read));

    if (!authResult.Succeeded)
    {
        return Results.Forbid();
    }

    // Implementation continues...
});
```

### **ERROR HANDLING PATTERNS**

#### **Global Exception Handling**
```csharp
public class DocumentApiExceptionHandler : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var (statusCode, title, detail) = exception switch
        {
            DocumentNotFoundException => (404, "Document Not Found", exception.Message),
            UnauthorizedDocumentAccessException => (403, "Access Denied", "Insufficient permissions"),
            FileUploadException => (400, "File Upload Failed", exception.Message),
            DataverseException => (500, "Data Service Error", "An error occurred while processing your request"),
            _ => (500, "Internal Server Error", "An unexpected error occurred")
        };

        var problemDetails = new ProblemDetails
        {
            Status = statusCode,
            Title = title,
            Detail = detail,
            Instance = httpContext.Request.Path
        };

        httpContext.Response.StatusCode = statusCode;
        await httpContext.Response.WriteAsJsonAsync(problemDetails, cancellationToken);
        return true;
    }
}
```

#### **Validation Error Responses**
```csharp
public static class ValidationHelper
{
    public static IResult CreateValidationProblem(IEnumerable<ValidationFailure> failures)
    {
        var errors = failures
            .GroupBy(f => f.PropertyName)
            .ToDictionary(
                g => g.Key,
                g => g.Select(f => f.ErrorMessage).ToArray());

        return Results.ValidationProblem(errors);
    }
}
```

### **PERFORMANCE OPTIMIZATION**

#### **Response Caching**
```csharp
// Configure caching for read operations
app.MapGet("/api/v1/documents/{id}", async (...) => {...})
   .CacheOutput(builder => builder
       .Expire(TimeSpan.FromMinutes(5))
       .Tag("documents")
       .VaryByValue("id"));
```

#### **Rate Limiting**
```csharp
// Configure rate limiting
services.AddRateLimiter(options =>
{
    options.AddFixedWindowLimiter("DocumentsApi", limiterOptions =>
    {
        limiterOptions.PermitLimit = 100;
        limiterOptions.Window = TimeSpan.FromMinutes(1);
        limiterOptions.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
        limiterOptions.QueueLimit = 10;
    });
});

// Apply to endpoints
app.MapGroup("/api/v1/documents")
   .RequireRateLimiting("DocumentsApi");
```

### **MONITORING AND TELEMETRY**

#### **Custom Metrics**
```csharp
public static class DocumentMetrics
{
    private static readonly Counter<int> DocumentsCreated =
        Meter.CreateCounter<int>("documents.created.total");

    private static readonly Histogram<double> DocumentOperationDuration =
        Meter.CreateHistogram<double>("documents.operation.duration");

    public static void IncrementDocumentsCreated() => DocumentsCreated.Add(1);

    public static void RecordOperationDuration(double milliseconds, string operation)
        => DocumentOperationDuration.Record(milliseconds, KeyValuePair.Create("operation", operation));
}
```

#### **Structured Logging**
```csharp
// Use structured logging throughout
logger.LogInformation("Creating document {DocumentName} in container {ContainerId} for user {UserId}",
    request.Name, request.ContainerId, context.User.Identity?.Name);
```

---

## ✅ VALIDATION STEPS

### **Functional Validation**

#### **1. Document CRUD Operations**
```bash
# Test document creation
curl -X POST "https://localhost:7034/api/v1/documents" \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{
    "name": "Test Document",
    "containerId": "test-container-id",
    "description": "Test document description"
  }'

# Test document retrieval
curl -X GET "https://localhost:7034/api/v1/documents/{id}" \
  -H "Authorization: Bearer $TOKEN"

# Test document update
curl -X PUT "https://localhost:7034/api/v1/documents/{id}" \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{
    "name": "Updated Document Name",
    "description": "Updated description"
  }'

# Test document deletion
curl -X DELETE "https://localhost:7034/api/v1/documents/{id}" \
  -H "Authorization: Bearer $TOKEN"
```

#### **2. File Operations**
```bash
# Test file upload initiation
curl -X POST "https://localhost:7034/api/v1/documents/{id}/files" \
  -H "Authorization: Bearer $TOKEN" \
  -F "file=@test-file.pdf"

# Test file download URL generation
curl -X GET "https://localhost:7034/api/v1/documents/{id}/files" \
  -H "Authorization: Bearer $TOKEN"
```

#### **3. List and Query Operations**
```bash
# Test document listing with filtering
curl -X GET "https://localhost:7034/api/v1/documents?containerId=test-container&status=Active&take=10" \
  -H "Authorization: Bearer $TOKEN"

# Test search functionality
curl -X GET "https://localhost:7034/api/v1/documents?search=test&orderBy=modifiedOn desc" \
  -H "Authorization: Bearer $TOKEN"
```

### **Authorization Validation**

#### **1. Unauthorized Access Tests**
```bash
# Test without authentication token
curl -X GET "https://localhost:7034/api/v1/documents/{id}"
# Expected: 401 Unauthorized

# Test with invalid token
curl -X GET "https://localhost:7034/api/v1/documents/{id}" \
  -H "Authorization: Bearer invalid-token"
# Expected: 401 Unauthorized

# Test accessing document without permission
curl -X DELETE "https://localhost:7034/api/v1/documents/{restricted-id}" \
  -H "Authorization: Bearer $LIMITED_USER_TOKEN"
# Expected: 403 Forbidden
```

#### **2. Role-Based Access Tests**
```bash
# Test different user roles accessing same document
# Document Manager should have full access
# Document User should have limited access
# Document Reader should have read-only access
```

### **Performance Validation**

#### **1. Response Time Tests**
```bash
# Measure response times for critical operations
# Target: < 500ms for simple CRUD operations
# Target: < 2s for file upload initiation
# Target: < 1s for list operations
```

#### **2. Load Testing**
```bash
# Use tools like Apache Bench or NBomber
# Test concurrent document creation
# Test concurrent file uploads
# Test high-frequency read operations
```

### **Error Handling Validation**

#### **1. Validation Error Tests**
```bash
# Test missing required fields
curl -X POST "https://localhost:7034/api/v1/documents" \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{}'
# Expected: 400 Bad Request with validation errors

# Test field length violations
curl -X POST "https://localhost:7034/api/v1/documents" \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{
    "name": "' + 'A'.repeat(300) + '",
    "containerId": "test-container"
  }'
# Expected: 400 Bad Request with field length error
```

#### **2. Service Dependency Error Tests**
```bash
# Test behavior when Dataverse is unavailable
# Test behavior when SharePoint Embedded is unavailable
# Test behavior when Service Bus is unavailable
# Verify graceful degradation and proper error messages
```

---

## 🔍 TROUBLESHOOTING GUIDE

### **Common Issues and Solutions**

#### **Issue: Authentication Failures**
**Symptoms**: 401 Unauthorized responses for valid requests
**Diagnosis Steps**:
1. Check JWT token validity and expiration
2. Verify bearer token format in Authorization header
3. Confirm authentication middleware is configured correctly
4. Check user claims and role assignments

**Solutions**:
```csharp
// Add detailed authentication logging
services.AddAuthentication().AddJwtBearer(options =>
{
    options.Events = new JwtBearerEvents
    {
        OnAuthenticationFailed = context =>
        {
            logger.LogWarning("JWT authentication failed: {Error}", context.Exception.Message);
            return Task.CompletedTask;
        }
    };
});
```

#### **Issue: Validation Errors Not Returning Proper Details**
**Symptoms**: Generic 400 errors without specific field information
**Solutions**:
1. Ensure FluentValidation is properly configured
2. Check validation error mapping in middleware
3. Verify ProblemDetails configuration

#### **Issue: File Upload Failures**
**Symptoms**: File uploads time out or fail with unclear errors
**Diagnosis Steps**:
1. Check file size limits in configuration
2. Verify SharePoint Embedded connectivity
3. Check upload timeout settings
4. Verify CORS configuration for file uploads

#### **Issue: Performance Problems**
**Symptoms**: Slow response times for API calls
**Diagnosis Steps**:
1. Check database query performance
2. Monitor Dataverse response times
3. Verify caching is working correctly
4. Check for N+1 query problems

**Solutions**:
- Implement database query optimization
- Add response caching for read operations
- Use async/await properly throughout
- Implement connection pooling

#### **Issue: Authorization Not Working Correctly**
**Symptoms**: Users can access documents they shouldn't
**Solutions**:
1. Verify authorization policies are configured correctly
2. Check document ownership and permission logic
3. Test with different user roles
4. Add comprehensive authorization logging

---

## 📚 KNOWLEDGE REFERENCES

### **Existing Patterns to Reference**
- `src/api/Spe.Bff.Api/Api/DocumentsEndpoints.cs` - Current minimal API patterns
- `src/api/Spe.Bff.Api/Api/UploadEndpoints.cs` - File handling patterns
- `src/api/Spe.Bff.Api/Infrastructure/Errors/ProblemDetailsHelper.cs` - Error handling
- `src/api/Spe.Bff.Api/Program.cs` - Endpoint registration patterns

### **Integration Points**
- `src/shared/Spaarke.Dataverse/IDataverseService.cs` - Service interface to implement
- `src/shared/Spaarke.Dataverse/Models.cs` - Entity models to use
- Existing SharePoint Embedded file operations
- Background job processing for async operations

### **Configuration References**
- Authentication and authorization configuration
- CORS policy configuration
- Rate limiting configuration
- Health check configuration

---

## 🎯 SUCCESS CRITERIA

This task is complete when:

### **Functional Criteria**
- ✅ All document CRUD endpoints implemented and tested
- ✅ File upload/download operations working end-to-end
- ✅ Authorization enforced at all endpoints
- ✅ Validation errors return clear, actionable messages
- ✅ List operations support filtering, sorting, and pagination
- ✅ Health check endpoints provide accurate service status

### **Technical Criteria**
- ✅ Response times meet performance targets (< 500ms for CRUD)
- ✅ Proper error handling for all failure scenarios
- ✅ OpenAPI documentation is complete and accurate
- ✅ Rate limiting prevents abuse
- ✅ Logging provides sufficient debugging information
- ✅ Integration with existing services works correctly

### **Security Criteria**
- ✅ All endpoints require proper authentication
- ✅ Authorization checks prevent unauthorized access
- ✅ File operations respect document permissions
- ✅ Sensitive data is not exposed in error messages
- ✅ CORS policies configured appropriately

### **Quality Criteria**
- ✅ Code follows existing project patterns and conventions
- ✅ Unit tests cover all endpoint logic
- ✅ Integration tests validate end-to-end functionality
- ✅ Documentation is comprehensive and up-to-date

---

## 🔄 CONCLUSION AND NEXT STEP

### **Impact of Completion**
Completing this task delivers:
1. **Complete REST API** for document and file management
2. **Power Platform integration points** ready for UI development
3. **Security framework** for authorization and access control
4. **Monitoring foundation** for production support
5. **Extensible architecture** for future enhancements

### **Quality Validation**
Before moving to the next task:
1. Execute comprehensive API testing using Swagger UI
2. Validate authorization works with different user roles
3. Test file upload/download operations end-to-end
4. Verify performance meets established targets
5. Confirm error handling provides clear user guidance

### **Integration Validation**
Ensure integration points are working:
1. DataverseService operations complete successfully
2. SharePoint Embedded file operations function correctly
3. Background job queuing works for async operations
4. Health checks report accurate service status

### **Immediate Next Action**
Upon successful completion of this task:

**🎯 PROCEED TO: [Task-2.1-Thin-Plugin-Implementation.md](./Task-2.1-Thin-Plugin-Implementation.md)**

The API foundation is now complete and ready for event-driven processing. The thin plugin will capture Dataverse events and queue them for background processing.

### **Handoff Information**
Provide this information to the next task:
- API endpoint URLs and authentication requirements
- Document event types that need to be captured
- Service Bus integration patterns to follow
- Performance requirements for plugin execution
- Error handling patterns established in the API layer

---

**📋 TASK COMPLETION CHECKLIST**
- [ ] All document CRUD endpoints implemented
- [ ] File management endpoints working
- [ ] Authorization and validation implemented
- [ ] Error handling comprehensive
- [ ] Performance targets met
- [ ] OpenAPI documentation complete
- [ ] Unit and integration tests passing
- [ ] Security validation complete
- [ ] Next task team briefed on API capabilities