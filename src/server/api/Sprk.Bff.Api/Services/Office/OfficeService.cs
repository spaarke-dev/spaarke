using Microsoft.Extensions.Logging;
using Sprk.Bff.Api.Models.Office;

namespace Sprk.Bff.Api.Services.Office;

/// <summary>
/// Implementation of <see cref="IOfficeService"/> for Office add-in operations.
/// </summary>
/// <remarks>
/// <para>
/// This service implements the Office add-in backend workflows:
/// - Task 021: Save endpoint (implemented) - creates ProcessingJob and returns tracking URLs
/// - Task 022: Job status endpoint (stub) - query Dataverse for job progress
/// - Task 023: SSE streaming (pending) - real-time job status updates
/// </para>
/// <para>
/// Per ADR-001, heavy processing (SPE upload, AI processing) is delegated to background workers.
/// This service focuses on fast job creation (target: &lt;3 seconds response time).
/// </para>
/// </remarks>
public class OfficeService : IOfficeService
{
    private readonly IJobStatusService _jobStatusService;
    private readonly ILogger<OfficeService> _logger;

    public OfficeService(
        IJobStatusService jobStatusService,
        ILogger<OfficeService> logger)
    {
        _jobStatusService = jobStatusService;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<SaveResponse> SaveAsync(
        SaveRequest request,
        string userId,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Save requested for {ContentType} by user {UserId}",
            request.ContentType,
            userId);

        try
        {
            // Step 1: Generate or use provided idempotency key
            var idempotencyKey = request.IdempotencyKey ?? GenerateIdempotencyKey(request);

            // Step 2: Check for existing job with this idempotency key
            // TODO: Replace with actual Dataverse query for ProcessingJob by IdempotencyKey
            var existingJob = await CheckForExistingJobAsync(idempotencyKey, cancellationToken);

            if (existingJob is not null)
            {
                _logger.LogInformation(
                    "Duplicate save detected, returning existing job {JobId}",
                    existingJob.JobId);

                return new SaveResponse
                {
                    Success = true,
                    Duplicate = true,
                    JobId = existingJob.JobId,
                    StatusUrl = $"/office/jobs/{existingJob.JobId}",
                    StreamUrl = $"/office/jobs/{existingJob.JobId}/stream"
                };
            }

            // Step 3: Determine job type based on content type
            var jobType = request.ContentType switch
            {
                SaveContentType.Email => JobType.EmailSave,
                SaveContentType.Attachment => JobType.AttachmentSave,
                SaveContentType.Document => JobType.DocumentSave,
                _ => throw new ArgumentOutOfRangeException(nameof(request.ContentType))
            };

            // Step 4: Create a new ProcessingJob record in Dataverse
            // TODO: Replace with actual Dataverse SDK call when ProcessingJob table exists
            // The record should include:
            // - sprk_jobtype (option set)
            // - sprk_status = Queued (option set)
            // - sprk_payload (JSON blob with request)
            // - sprk_idempotencykey (indexed string)
            // - sprk_createdby (system user lookup)
            // - sprk_association (lookup to target entity)

            var jobId = Guid.NewGuid();

            _logger.LogInformation(
                "Creating ProcessingJob {JobId} for {ContentType} save with association {AssociationType}:{AssociationId}",
                jobId,
                request.ContentType,
                request.TargetEntity?.EntityType,
                request.TargetEntity?.EntityId);

            // Simulate Dataverse record creation
            await Task.Delay(10, cancellationToken);

            // Step 5: Queue background job for processing
            // TODO: Implement Service Bus message publishing (ADR-001)
            // Workers will:
            // - Upload content to SPE
            // - Create EmailArtifact/AttachmentArtifact/Document record
            // - Trigger AI processing if enabled
            // - Update job status via SSE

            _logger.LogInformation(
                "ProcessingJob {JobId} created and queued for background processing",
                jobId);

            // Step 6: Return success response with job tracking URLs
            return new SaveResponse
            {
                Success = true,
                Duplicate = false,
                JobId = jobId,
                StatusUrl = $"/office/jobs/{jobId}",
                StreamUrl = $"/office/jobs/{jobId}/stream"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to create save job for {ContentType} by user {UserId}",
                request.ContentType,
                userId);

            return new SaveResponse
            {
                Success = false,
                Error = new SaveError
                {
                    Code = "OFFICE_INTERNAL",
                    Message = "An unexpected error occurred while processing the save request.",
                    Details = ex.Message,
                    Retryable = true
                }
            };
        }
    }

    /// <summary>
    /// Generates an idempotency key based on the request content.
    /// Uses SHA256 hash of the canonical payload.
    /// </summary>
    private static string GenerateIdempotencyKey(SaveRequest request)
    {
        // Create a canonical representation of the request for hashing
        var canonical = $"{request.ContentType}|" +
                       $"{request.TargetEntity?.EntityType}|" +
                       $"{request.TargetEntity?.EntityId}|" +
                       $"{request.Email?.InternetMessageId ?? request.Email?.Subject}|" +
                       $"{request.Attachment?.AttachmentId}|" +
                       $"{request.Document?.FileName}|" +
                       $"{request.Document?.ExistingDocumentId}";

        using var sha256 = System.Security.Cryptography.SHA256.Create();
        var hashBytes = sha256.ComputeHash(System.Text.Encoding.UTF8.GetBytes(canonical));
        return Convert.ToBase64String(hashBytes);
    }

    /// <summary>
    /// Checks for an existing ProcessingJob with the given idempotency key.
    /// </summary>
    private async Task<JobStatusResponse?> CheckForExistingJobAsync(
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        // TODO: Replace with actual Dataverse query
        // FetchXML would be:
        // <fetch top="1">
        //   <entity name="sprk_processingjob">
        //     <attribute name="sprk_processingjobid" />
        //     <attribute name="sprk_status" />
        //     <attribute name="sprk_jobtype" />
        //     <filter>
        //       <condition attribute="sprk_idempotencykey" operator="eq" value="{idempotencyKey}" />
        //       <condition attribute="createdon" operator="last-x-hours" value="24" />
        //     </filter>
        //   </entity>
        // </fetch>

        await Task.CompletedTask;

        // For now, always return null (no duplicate found)
        _logger.LogDebug("Checking for existing job with idempotency key");
        return null;
    }

    /// <inheritdoc />
    public async Task<JobStatusResponse?> GetJobStatusAsync(
        Guid jobId,
        string? userId,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Job status requested for {JobId} by user {UserId}",
            jobId,
            userId);

        // TODO: Replace with actual Dataverse query once ProcessingJob table exists
        // The query should:
        // 1. Look up job by sprk_processingjobid = jobId
        // 2. Verify sprk_createdby (owner) matches userId
        // 3. Map Dataverse entity to JobStatusResponse

        // For now, return a stub response for testing with a known test job ID
        // This allows end-to-end testing of the endpoint structure
        var testJobId = Guid.Parse("00000000-0000-0000-0000-000000000001");

        if (jobId == testJobId)
        {
            await Task.CompletedTask; // Simulate async operation

            return new JobStatusResponse
            {
                JobId = jobId,
                Status = JobStatus.Running,
                JobType = JobType.EmailSave,
                Progress = 50,
                CurrentPhase = "FileUploaded",
                CompletedPhases = new List<CompletedPhase>
                {
                    new CompletedPhase
                    {
                        Name = "RecordsCreated",
                        CompletedAt = DateTimeOffset.UtcNow.AddSeconds(-5),
                        DurationMs = 250
                    },
                    new CompletedPhase
                    {
                        Name = "FileUploaded",
                        CompletedAt = DateTimeOffset.UtcNow.AddSeconds(-2),
                        DurationMs = 1500
                    }
                },
                CreatedAt = DateTimeOffset.UtcNow.AddSeconds(-10),
                CreatedBy = userId, // Set for ownership verification by filters
                StartedAt = DateTimeOffset.UtcNow.AddSeconds(-8)
            };
        }

        // Job not found
        _logger.LogDebug("Job {JobId} not found in store", jobId);
        return null;
    }

    /// <inheritdoc />
    public Task<JobStatusResponse?> GetJobStatusAsync(
        Guid jobId,
        CancellationToken cancellationToken = default)
    {
        // Delegate to the main method without ownership validation
        // This overload is used by authorization filters to verify job existence
        return GetJobStatusAsync(jobId, userId: null, cancellationToken);
    }

    /// <inheritdoc />
    public Task<bool> IsHealthyAsync(CancellationToken cancellationToken = default)
    {
        // Basic health check - always returns true for now
        // Will be expanded to check dependencies (Dataverse, SPE, etc.)
        return Task.FromResult(true);
    }

    /// <inheritdoc />
    public async Task<EntitySearchResponse> SearchEntitiesAsync(
        EntitySearchRequest request,
        string userId,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Entity search requested: Query='{Query}', Types={EntityTypes}, Skip={Skip}, Top={Top}, User={UserId}",
            request.Query,
            request.EntityTypes != null ? string.Join(",", request.EntityTypes) : "all",
            request.Skip,
            request.Top,
            userId);

        // Determine which entity types to search
        var typesToSearch = GetEntityTypesToSearch(request.EntityTypes);

        // TODO: Replace with actual Dataverse queries once tables exist
        // The implementation should:
        // 1. Build FetchXML queries for each entity type with 'contains' filter on name fields
        // 2. Execute queries in parallel for performance
        // 3. Combine and sort results by relevance + recency
        // 4. Apply pagination (skip/top) to combined results
        // 5. Filter by user permissions (Dataverse handles this via security roles)

        // For now, return stub data for testing the endpoint structure
        var results = GenerateStubResults(request.Query, typesToSearch, request.Top);
        var totalCount = results.Count + (request.Skip > 0 ? request.Skip : 0);

        await Task.CompletedTask; // Simulate async operation

        return new EntitySearchResponse
        {
            Results = results.Skip(request.Skip).Take(request.Top).ToList(),
            TotalCount = totalCount,
            HasMore = totalCount > request.Skip + request.Top
        };
    }

    /// <summary>
    /// Determines which entity types to search based on the request.
    /// </summary>
    private static HashSet<AssociationEntityType> GetEntityTypesToSearch(string[]? requestedTypes)
    {
        // If no types specified, search all
        if (requestedTypes == null || requestedTypes.Length == 0)
        {
            return new HashSet<AssociationEntityType>(Enum.GetValues<AssociationEntityType>());
        }

        var typesToSearch = new HashSet<AssociationEntityType>();
        foreach (var typeStr in requestedTypes)
        {
            if (Enum.TryParse<AssociationEntityType>(typeStr, ignoreCase: true, out var entityType))
            {
                typesToSearch.Add(entityType);
            }
        }

        // If no valid types were specified, search all
        return typesToSearch.Count > 0
            ? typesToSearch
            : new HashSet<AssociationEntityType>(Enum.GetValues<AssociationEntityType>());
    }

    /// <summary>
    /// Generates stub results for testing. Will be replaced with actual Dataverse queries.
    /// </summary>
    private static List<EntitySearchResult> GenerateStubResults(
        string query,
        HashSet<AssociationEntityType> entityTypes,
        int maxResults)
    {
        var results = new List<EntitySearchResult>();
        var queryLower = query.ToLowerInvariant();

        // Generate test data that matches the query
        var testData = new[]
        {
            new { Type = AssociationEntityType.Matter, Name = "Smith vs Jones Matter", Info = "Client: Acme Corp | Status: Active", Primary = "SMJ-2024-001" },
            new { Type = AssociationEntityType.Matter, Name = "Acme Contract Dispute", Info = "Client: Acme Corp | Status: Open", Primary = "ACD-2024-002" },
            new { Type = AssociationEntityType.Project, Name = "Acme Implementation Project", Info = "Phase: Development | Due: 2026-06-01", Primary = "PROJ-001" },
            new { Type = AssociationEntityType.Project, Name = "Smith Foundation Audit", Info = "Phase: Planning | Due: 2026-03-15", Primary = "PROJ-002" },
            new { Type = AssociationEntityType.Invoice, Name = "INV-2024-0001", Info = "Amount: $15,000 | Status: Pending", Primary = "Acme Corp" },
            new { Type = AssociationEntityType.Invoice, Name = "INV-2024-0002", Info = "Amount: $8,500 | Status: Paid", Primary = "Smith Foundation" },
            new { Type = AssociationEntityType.Account, Name = "Acme Corporation", Info = "Industry: Manufacturing | City: Chicago", Primary = "acme@acmecorp.com" },
            new { Type = AssociationEntityType.Account, Name = "Smith Foundation", Info = "Industry: Non-Profit | City: Boston", Primary = "info@smithfoundation.org" },
            new { Type = AssociationEntityType.Contact, Name = "John Smith", Info = "Company: Acme Corp | Title: CEO", Primary = "john.smith@acmecorp.com" },
            new { Type = AssociationEntityType.Contact, Name = "Jane Acme", Info = "Company: Acme Corp | Title: CFO", Primary = "jane.acme@acmecorp.com" }
        };

        foreach (var item in testData)
        {
            // Only include if type is requested
            if (!entityTypes.Contains(item.Type))
                continue;

            // Only include if query matches name, info, or primary field
            var matchesQuery = item.Name.ToLowerInvariant().Contains(queryLower) ||
                               item.Info.ToLowerInvariant().Contains(queryLower) ||
                               item.Primary.ToLowerInvariant().Contains(queryLower);

            if (!matchesQuery)
                continue;

            results.Add(new EntitySearchResult
            {
                Id = Guid.NewGuid(),
                EntityType = item.Type,
                LogicalName = GetLogicalName(item.Type),
                Name = item.Name,
                DisplayInfo = item.Info,
                PrimaryField = item.Primary,
                IconUrl = $"/icons/{item.Type.ToString().ToLowerInvariant()}.svg",
                ModifiedOn = DateTimeOffset.UtcNow.AddDays(-Random.Shared.Next(1, 30))
            });

            if (results.Count >= maxResults)
                break;
        }

        // Sort by relevance (exact match first) then by recency
        return results
            .OrderByDescending(r => r.Name.ToLowerInvariant().StartsWith(queryLower))
            .ThenByDescending(r => r.ModifiedOn)
            .ToList();
    }

    /// <summary>
    /// Gets the Dataverse logical name for an entity type.
    /// </summary>
    private static string GetLogicalName(AssociationEntityType entityType) => entityType switch
    {
        AssociationEntityType.Matter => "sprk_matter",
        AssociationEntityType.Project => "sprk_project",
        AssociationEntityType.Invoice => "sprk_invoice",
        AssociationEntityType.Account => "account",
        AssociationEntityType.Contact => "contact",
        _ => throw new ArgumentOutOfRangeException(nameof(entityType))
    };

    /// <inheritdoc />
    public async Task<DocumentSearchResponse> SearchDocumentsAsync(
        DocumentSearchRequest request,
        string userId,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Document search requested: Query='{Query}', EntityType={EntityType}, EntityId={EntityId}, ContentType={ContentType}, Skip={Skip}, Top={Top}, User={UserId}",
            request.Query,
            request.EntityType?.ToString() ?? "any",
            request.EntityId?.ToString() ?? "none",
            request.ContentType ?? "any",
            request.Skip,
            request.Top,
            userId);

        // TODO: Replace with actual Dataverse/SpeFileStore queries once document entity exists
        // The implementation should:
        // 1. Build FetchXML query for sprk_document with filters:
        //    - Name/filename contains query (case-insensitive)
        //    - sprk_matter/sprk_project/etc. filter by EntityType + EntityId
        //    - sprk_contenttype contains ContentType if specified
        //    - modifiedon date range if ModifiedAfter/ModifiedBefore specified
        //    - sprk_graphdriveid = ContainerId if specified
        // 2. Check user permissions via Dataverse security roles
        // 3. Get thumbnail URLs from SPE via SpeFileStore (batch Graph API call)
        // 4. Determine CanShare for each document based on user's permissions
        // 5. Map to DocumentSearchResult DTOs

        // Generate stub data for testing the endpoint structure
        var results = GenerateStubDocumentResults(request);
        var totalCount = results.Count + (request.Skip > 0 ? request.Skip : 0);

        await Task.CompletedTask; // Simulate async operation

        return new DocumentSearchResponse
        {
            Results = results.Skip(request.Skip).Take(request.Top).ToList(),
            TotalCount = totalCount,
            HasMore = totalCount > request.Skip + request.Top
        };
    }

    /// <summary>
    /// Generates stub document results for testing. Will be replaced with actual Dataverse/SPE queries.
    /// </summary>
    private static List<DocumentSearchResult> GenerateStubDocumentResults(DocumentSearchRequest request)
    {
        var results = new List<DocumentSearchResult>();
        var queryLower = request.Query.ToLowerInvariant();

        // Generate test data that matches the query
        var testDocuments = new[]
        {
            new
            {
                Name = "Contract Agreement v2",
                FileName = "Contract Agreement v2.docx",
                ContentType = "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
                Size = 245678L,
                AssocType = AssociationEntityType.Matter,
                AssocName = "Smith vs Jones",
                Description = "Final version of the service contract",
                ModifiedBy = "John Doe"
            },
            new
            {
                Name = "Financial Report Q4",
                FileName = "Financial Report Q4 2025.xlsx",
                ContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                Size = 1024567L,
                AssocType = AssociationEntityType.Account,
                AssocName = "Acme Corporation",
                Description = "Q4 2025 financial summary",
                ModifiedBy = "Jane Smith"
            },
            new
            {
                Name = "Project Proposal",
                FileName = "Project Proposal - Acme.pdf",
                ContentType = "application/pdf",
                Size = 512000L,
                AssocType = AssociationEntityType.Project,
                AssocName = "Acme Implementation Project",
                Description = "Initial project proposal document",
                ModifiedBy = "Bob Wilson"
            },
            new
            {
                Name = "Invoice INV-2024-0001",
                FileName = "Invoice INV-2024-0001.pdf",
                ContentType = "application/pdf",
                Size = 89000L,
                AssocType = AssociationEntityType.Invoice,
                AssocName = "INV-2024-0001",
                Description = "Invoice for consulting services",
                ModifiedBy = "Jane Smith"
            },
            new
            {
                Name = "Meeting Notes",
                FileName = "Meeting Notes 2026-01-15.docx",
                ContentType = "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
                Size = 45000L,
                AssocType = AssociationEntityType.Contact,
                AssocName = "John Smith",
                Description = "Notes from client meeting",
                ModifiedBy = "John Doe"
            }
        };

        var baseDate = DateTimeOffset.UtcNow;
        var index = 0;

        foreach (var doc in testDocuments)
        {
            // Apply query filter - search name, filename, description
            var matchesQuery = doc.Name.ToLowerInvariant().Contains(queryLower) ||
                               doc.FileName.ToLowerInvariant().Contains(queryLower) ||
                               doc.Description.ToLowerInvariant().Contains(queryLower);

            if (!matchesQuery)
                continue;

            // Apply EntityType filter
            if (request.EntityType.HasValue && doc.AssocType != request.EntityType.Value)
                continue;

            // Apply ContentType filter (partial match)
            if (!string.IsNullOrEmpty(request.ContentType) &&
                !doc.ContentType.Contains(request.ContentType, StringComparison.OrdinalIgnoreCase))
                continue;

            var documentId = Guid.NewGuid();
            var modifiedDate = baseDate.AddDays(-index - 1);

            // Apply date range filters
            if (request.ModifiedAfter.HasValue && modifiedDate < request.ModifiedAfter.Value)
                continue;

            if (request.ModifiedBefore.HasValue && modifiedDate > request.ModifiedBefore.Value)
                continue;

            results.Add(new DocumentSearchResult
            {
                Id = documentId,
                Name = doc.Name,
                FileName = doc.FileName,
                WebUrl = $"https://spaarke.com/documents/{documentId}",
                ContentType = doc.ContentType,
                Size = doc.Size,
                ModifiedDate = modifiedDate,
                ModifiedBy = doc.ModifiedBy,
                ThumbnailUrl = null, // Thumbnails would be fetched from SPE in real implementation
                AssociationType = doc.AssocType,
                AssociationId = Guid.NewGuid(),
                AssociationName = doc.AssocName,
                ContainerId = Guid.NewGuid(),
                Description = doc.Description,
                CanShare = true // In real implementation, check user permissions
            });

            index++;
        }

        // Sort by modification date (most recent first)
        return results.OrderByDescending(r => r.ModifiedDate).ToList();
    }

    /// <inheritdoc />
    public async Task<ShareLinksResponse> CreateShareLinksAsync(
        ShareLinksRequest request,
        string userId,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Share links requested for {DocumentCount} documents by user {UserId}",
            request.DocumentIds.Count,
            userId);

        var links = new List<DocumentLink>();
        var errors = new List<ShareLinkError>();
        var invitations = new List<ShareInvitation>();

        // Generate share links for each document
        foreach (var documentId in request.DocumentIds)
        {
            // Simulate permission check - in real implementation, query Dataverse
            var hasSharePermission = await SimulateSharePermissionCheckAsync(documentId, userId, cancellationToken);

            if (!hasSharePermission)
            {
                errors.Add(new ShareLinkError
                {
                    DocumentId = documentId,
                    Code = "OFFICE_009",
                    Message = "Access denied. User lacks share permission for this document."
                });
                continue;
            }

            // Get document metadata - in real implementation, from Dataverse query
            var documentMetadata = await GetDocumentMetadataForLinkAsync(documentId, cancellationToken);

            if (documentMetadata == null)
            {
                errors.Add(new ShareLinkError
                {
                    DocumentId = documentId,
                    Code = "OFFICE_007",
                    Message = "Document not found."
                });
                continue;
            }

            // Generate shareable URL
            var shareUrl = GenerateShareLinkUrl(documentId);

            links.Add(new DocumentLink
            {
                DocumentId = documentId,
                Url = shareUrl,
                DisplayName = documentMetadata.DisplayName,
                FileName = documentMetadata.FileName,
                ContentType = documentMetadata.ContentType,
                Size = documentMetadata.Size,
                IconUrl = GetDocumentIconUrl(documentMetadata.ContentType)
            });
        }

        // Process invitations if grantAccess is true and recipients are provided
        if (request.GrantAccess && request.Recipients?.Count > 0)
        {
            invitations = await ProcessShareInvitationsAsync(
                request.Recipients,
                request.DocumentIds,
                request.Role,
                userId,
                cancellationToken);
        }

        return new ShareLinksResponse
        {
            Links = links,
            Invitations = invitations.Count > 0 ? invitations : null,
            Errors = errors.Count > 0 ? errors : null,
            CorrelationId = Guid.NewGuid().ToString()
        };
    }

    /// <summary>
    /// Simulates permission check for share access.
    /// </summary>
    private Task<bool> SimulateSharePermissionCheckAsync(
        Guid documentId,
        string userId,
        CancellationToken cancellationToken)
    {
        // TODO: Replace with actual Dataverse security role check
        _logger.LogDebug(
            "Permission check for document {DocumentId} by user {UserId}",
            documentId,
            userId);

        return Task.FromResult(true);
    }

    /// <summary>
    /// Gets document metadata for link generation.
    /// </summary>
    private Task<ShareLinkDocumentMetadata?> GetDocumentMetadataForLinkAsync(
        Guid documentId,
        CancellationToken cancellationToken)
    {
        // TODO: Replace with actual Dataverse query
        var shortId = documentId.ToString("N").Substring(0, 8);
        return Task.FromResult<ShareLinkDocumentMetadata?>(new ShareLinkDocumentMetadata
        {
            DisplayName = $"Document {shortId}",
            FileName = $"document-{shortId}.docx",
            ContentType = "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            Size = 245678
        });
    }

    /// <summary>
    /// Generates a shareable URL for a document.
    /// </summary>
    private static string GenerateShareLinkUrl(Guid documentId)
    {
        // TODO: Make base URL configurable via appsettings
        const string baseUrl = "https://spaarke.app/doc";
        return $"{baseUrl}/{documentId}";
    }

    /// <summary>
    /// Gets an icon URL based on content type.
    /// </summary>
    private static string? GetDocumentIconUrl(string? contentType)
    {
        if (string.IsNullOrEmpty(contentType))
            return "/icons/document.svg";

        return contentType switch
        {
            var t when t.Contains("word") => "/icons/word.svg",
            var t when t.Contains("excel") || t.Contains("spreadsheet") => "/icons/excel.svg",
            var t when t.Contains("powerpoint") || t.Contains("presentation") => "/icons/powerpoint.svg",
            var t when t.Contains("pdf") => "/icons/pdf.svg",
            var t when t.StartsWith("image/") => "/icons/image.svg",
            var t when t.StartsWith("video/") => "/icons/video.svg",
            var t when t.StartsWith("audio/") => "/icons/audio.svg",
            var t when t.StartsWith("text/") => "/icons/text.svg",
            _ => "/icons/document.svg"
        };
    }

    /// <summary>
    /// Processes external sharing invitations.
    /// </summary>
    private async Task<List<ShareInvitation>> ProcessShareInvitationsAsync(
        IReadOnlyList<string> recipients,
        IReadOnlyList<Guid> documentIds,
        ShareLinkRole role,
        string userId,
        CancellationToken cancellationToken)
    {
        var invitations = new List<ShareInvitation>();

        foreach (var email in recipients)
        {
            var isExternal = !email.EndsWith("@spaarke.com", StringComparison.OrdinalIgnoreCase);

            if (!isExternal)
            {
                invitations.Add(new ShareInvitation
                {
                    Email = email,
                    Status = InvitationStatus.AlreadyHasAccess
                });
                continue;
            }

            _logger.LogInformation(
                "Creating invitation for external user {Email} to share {DocumentCount} documents with role {Role}",
                email,
                documentIds.Count,
                role);

            invitations.Add(new ShareInvitation
            {
                Email = email,
                Status = InvitationStatus.Created,
                InvitationId = Guid.NewGuid()
            });
        }

        await Task.CompletedTask;
        return invitations;
    }

    /// <summary>
    /// Internal record for document metadata used in link generation.
    /// </summary>
    private record ShareLinkDocumentMetadata
    {
        public required string DisplayName { get; init; }
        public required string FileName { get; init; }
        public string? ContentType { get; init; }
        public long? Size { get; init; }
    }

    /// <inheritdoc />
    public async Task<QuickCreateResponse?> QuickCreateAsync(
        QuickCreateEntityType entityType,
        QuickCreateRequest request,
        string userId,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Quick create requested for {EntityType} by user {UserId}",
            entityType,
            userId);

        // Get the display name for the created entity
        var displayName = entityType == QuickCreateEntityType.Contact
            ? $"{request.FirstName} {request.LastName}".Trim()
            : request.Name ?? "Unnamed";

        // TODO: Implement actual Dataverse record creation
        // The implementation should:
        // 1. Verify user has create permission for the entity type
        // 2. Build the entity record with appropriate fields based on entity type:
        //    - Matter: sprk_name, sprk_description, sprk_account (lookup)
        //    - Project: sprk_name, sprk_description, sprk_account (lookup)
        //    - Invoice: sprk_name, sprk_description, sprk_account (lookup)
        //    - Account: name, description, industrycode, address1_city
        //    - Contact: firstname, lastname, emailaddress1, parentcustomerid (lookup)
        // 3. Create the record via Dataverse SDK
        // 4. Return the created record ID and URL

        // Simulate async Dataverse operation
        await Task.Delay(100, cancellationToken);

        // Generate stub response for testing
        var createdId = Guid.NewGuid();
        var logicalName = QuickCreateFieldRequirements.GetLogicalName(entityType);

        _logger.LogInformation(
            "Quick create completed: EntityType={EntityType}, Id={Id}, Name={Name}",
            entityType,
            createdId,
            displayName);

        return new QuickCreateResponse
        {
            Id = createdId,
            EntityType = entityType,
            LogicalName = logicalName,
            Name = displayName,
            Url = $"https://spaarkedev1.crm.dynamics.com/main.aspx?etn={logicalName}&id={createdId}&pagetype=entityrecord"
        };
    }

    /// <inheritdoc />
    public async Task<RecentDocumentsResponse> GetRecentDocumentsAsync(
        string userId,
        int top = 10,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Recent items requested by user {UserId} with limit {Top}",
            userId,
            top);

        // TODO: Replace with actual Redis + Dataverse queries
        // The full implementation should:
        // 1. Query Redis sorted set for recent associations: "recent:associations:{userId}"
        // 2. Query Redis sorted set for recent documents: "recent:documents:{userId}"
        // 3. Query Dataverse for user favorites (sprk_userfavorite table)
        // 4. Validate user still has access to each item (parallel Dataverse permission checks)
        // 5. Filter out items user no longer has access to
        // 6. Return top N items per category sorted by most recently used

        // For now, return stub data for testing the endpoint structure
        var recentAssociations = GenerateStubRecentAssociations(top);
        var recentDocuments = GenerateStubRecentDocuments(top);
        var favorites = GenerateStubFavorites(top);

        await Task.CompletedTask; // Simulate async operation

        return new RecentDocumentsResponse
        {
            RecentAssociations = recentAssociations,
            RecentDocuments = recentDocuments,
            Favorites = favorites
        };
    }

    /// <summary>
    /// Generates stub recent associations for testing. Will be replaced with Redis queries.
    /// </summary>
    private static List<RecentAssociation> GenerateStubRecentAssociations(int top)
    {
        var associations = new List<RecentAssociation>
        {
            new()
            {
                Id = Guid.NewGuid(),
                EntityType = AssociationType.Matter,
                LogicalName = "sprk_matter",
                Name = "Smith vs Jones Matter",
                DisplayInfo = "Client: Acme Corp | Status: Active",
                LastUsed = DateTimeOffset.UtcNow.AddHours(-2),
                UseCount = 15
            },
            new()
            {
                Id = Guid.NewGuid(),
                EntityType = AssociationType.Project,
                LogicalName = "sprk_project",
                Name = "Acme Implementation Project",
                DisplayInfo = "Phase: Development | Due: 2026-06-01",
                LastUsed = DateTimeOffset.UtcNow.AddHours(-5),
                UseCount = 8
            },
            new()
            {
                Id = Guid.NewGuid(),
                EntityType = AssociationType.Account,
                LogicalName = "account",
                Name = "Acme Corporation",
                DisplayInfo = "Industry: Manufacturing | City: Chicago",
                LastUsed = DateTimeOffset.UtcNow.AddDays(-1),
                UseCount = 23
            },
            new()
            {
                Id = Guid.NewGuid(),
                EntityType = AssociationType.Contact,
                LogicalName = "contact",
                Name = "John Smith",
                DisplayInfo = "Company: Acme Corp | Title: CEO",
                LastUsed = DateTimeOffset.UtcNow.AddDays(-2),
                UseCount = 5
            },
            new()
            {
                Id = Guid.NewGuid(),
                EntityType = AssociationType.Invoice,
                LogicalName = "sprk_invoice",
                Name = "INV-2024-0001",
                DisplayInfo = "Amount: $15,000 | Status: Pending",
                LastUsed = DateTimeOffset.UtcNow.AddDays(-3),
                UseCount = 3
            }
        };

        return associations
            .OrderByDescending(a => a.LastUsed)
            .Take(top)
            .ToList();
    }

    /// <summary>
    /// Generates stub recent documents for testing. Will be replaced with Redis + Dataverse queries.
    /// </summary>
    private static List<RecentDocument> GenerateStubRecentDocuments(int top)
    {
        var documents = new List<RecentDocument>
        {
            new()
            {
                Id = Guid.NewGuid(),
                Name = "Contract Agreement v2.docx",
                WebUrl = "https://spaarke.com/documents/contract-agreement-v2",
                ModifiedDate = DateTimeOffset.UtcNow.AddHours(-1),
                ContentType = "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
                FileSize = 245678,
                EntityReference = new EntityReference
                {
                    Id = Guid.NewGuid(),
                    EntityType = AssociationType.Matter,
                    LogicalName = "sprk_matter",
                    Name = "Smith vs Jones Matter"
                }
            },
            new()
            {
                Id = Guid.NewGuid(),
                Name = "Financial Report Q4 2025.xlsx",
                WebUrl = "https://spaarke.com/documents/financial-report-q4",
                ModifiedDate = DateTimeOffset.UtcNow.AddHours(-3),
                ContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                FileSize = 1024567,
                EntityReference = new EntityReference
                {
                    Id = Guid.NewGuid(),
                    EntityType = AssociationType.Account,
                    LogicalName = "account",
                    Name = "Acme Corporation"
                }
            },
            new()
            {
                Id = Guid.NewGuid(),
                Name = "Project Proposal - Acme.pdf",
                WebUrl = "https://spaarke.com/documents/project-proposal-acme",
                ModifiedDate = DateTimeOffset.UtcNow.AddDays(-1),
                ContentType = "application/pdf",
                FileSize = 512000,
                EntityReference = new EntityReference
                {
                    Id = Guid.NewGuid(),
                    EntityType = AssociationType.Project,
                    LogicalName = "sprk_project",
                    Name = "Acme Implementation Project"
                }
            },
            new()
            {
                Id = Guid.NewGuid(),
                Name = "Meeting Notes 2026-01-15.docx",
                WebUrl = "https://spaarke.com/documents/meeting-notes-20260115",
                ModifiedDate = DateTimeOffset.UtcNow.AddDays(-5),
                ContentType = "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
                FileSize = 45000,
                EntityReference = new EntityReference
                {
                    Id = Guid.NewGuid(),
                    EntityType = AssociationType.Contact,
                    LogicalName = "contact",
                    Name = "John Smith"
                }
            }
        };

        return documents
            .OrderByDescending(d => d.ModifiedDate)
            .Take(top)
            .ToList();
    }

    /// <summary>
    /// Generates stub favorites for testing. Will be replaced with Dataverse queries.
    /// </summary>
    private static List<FavoriteEntity> GenerateStubFavorites(int top)
    {
        var favorites = new List<FavoriteEntity>
        {
            new()
            {
                Id = Guid.NewGuid(),
                EntityType = AssociationType.Matter,
                LogicalName = "sprk_matter",
                Name = "Smith vs Jones Matter",
                FavoritedAt = DateTimeOffset.UtcNow.AddDays(-30)
            },
            new()
            {
                Id = Guid.NewGuid(),
                EntityType = AssociationType.Account,
                LogicalName = "account",
                Name = "Acme Corporation",
                FavoritedAt = DateTimeOffset.UtcNow.AddDays(-25)
            },
            new()
            {
                Id = Guid.NewGuid(),
                EntityType = AssociationType.Project,
                LogicalName = "sprk_project",
                Name = "Acme Implementation Project",
                FavoritedAt = DateTimeOffset.UtcNow.AddDays(-15)
            }
        };

        return favorites
            .OrderByDescending(f => f.FavoritedAt)
            .Take(top)
            .ToList();
    }

    /// <inheritdoc />
    public async Task<ShareAttachResponse> GetAttachmentsAsync(
        ShareAttachRequest request,
        string userId,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Attachment packaging requested for {DocumentCount} documents by user {UserId}, DeliveryMode={DeliveryMode}, CorrelationId={CorrelationId}",
            request.DocumentIds.Length,
            userId,
            request.DeliveryMode,
            correlationId);

        var attachments = new List<AttachmentPackage>();
        var errors = new List<AttachmentError>();
        long totalSize = 0;

        // Process each document
        foreach (var documentId in request.DocumentIds)
        {
            try
            {
                // TODO: Replace with actual implementation once dependencies are available:
                // 1. Get document from Dataverse via IDataverseService
                // 2. Verify user has share permission via UAC
                // 3. Check size limits (25MB per file, 100MB total)
                // 4. For URL mode: Generate pre-signed download URL
                // 5. For Base64 mode: Download content from SPE and encode

                // Generate stub attachment for testing
                var attachment = await PackageAttachmentAsync(
                    documentId,
                    request.DeliveryMode,
                    totalSize,
                    cancellationToken);

                if (attachment != null)
                {
                    // Check if adding this file would exceed total size limit (100MB per spec NFR-03)
                    const long maxTotalAttachmentSizeBytes = 100 * 1024 * 1024; // 100MB
                    if (totalSize + attachment.Size > maxTotalAttachmentSizeBytes)
                    {
                        _logger.LogWarning(
                            "Total attachment size would exceed limit. DocumentId={DocumentId}, CurrentTotal={CurrentTotal}, FileSize={FileSize}, Limit={Limit}",
                            documentId,
                            totalSize,
                            attachment.Size,
                            maxTotalAttachmentSizeBytes);

                        errors.Add(new AttachmentError
                        {
                            DocumentId = documentId,
                            ErrorCode = "OFFICE_005",
                            Message = $"Adding this file ({attachment.Size / (1024 * 1024):F1}MB) would exceed the total attachment limit of {maxTotalAttachmentSizeBytes / (1024 * 1024)}MB."
                        });
                        continue;
                    }

                    attachments.Add(attachment);
                    totalSize += attachment.Size;

                    _logger.LogDebug(
                        "Packaged attachment: DocumentId={DocumentId}, FileName={FileName}, Size={Size}",
                        documentId,
                        attachment.FileName,
                        attachment.Size);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Failed to package attachment for DocumentId={DocumentId}, CorrelationId={CorrelationId}",
                    documentId,
                    correlationId);

                errors.Add(new AttachmentError
                {
                    DocumentId = documentId,
                    ErrorCode = "OFFICE_012",
                    Message = "Failed to retrieve document from storage."
                });
            }
        }

        _logger.LogInformation(
            "Attachment packaging completed: {SuccessCount} succeeded, {ErrorCount} failed, TotalSize={TotalSize} bytes, CorrelationId={CorrelationId}",
            attachments.Count,
            errors.Count,
            totalSize,
            correlationId);

        return new ShareAttachResponse
        {
            Attachments = attachments.ToArray(),
            Errors = errors.Count > 0 ? errors.ToArray() : null,
            CorrelationId = correlationId,
            TotalSize = totalSize
        };
    }

    /// <summary>
    /// Packages a single document for attachment.
    /// Stub implementation - will be replaced with actual SPE and Dataverse calls.
    /// </summary>
    /// <remarks>
    /// Size limits per spec NFR-03:
    /// - Single file: 25MB max
    /// - Total attachments: 100MB max
    /// - Recommended base64 threshold: 1MB (URL preferred for larger files)
    /// </remarks>
    private async Task<AttachmentPackage?> PackageAttachmentAsync(
        Guid documentId,
        AttachmentDeliveryMode deliveryMode,
        long currentTotalSize,
        CancellationToken cancellationToken)
    {
        const long maxAttachmentSizeBytes = 25 * 1024 * 1024; // 25MB per file
        const long recommendedBase64ThresholdBytes = 1 * 1024 * 1024; // 1MB

        // TODO: Replace with actual implementation:
        // 1. Look up document in Dataverse by ID
        // 2. Verify SPE pointers exist (GraphDriveId, GraphItemId)
        // 3. Check user share permission
        // 4. Validate size constraints
        // 5. Generate download URL or base64 content based on delivery mode

        // Simulate async operation
        await Task.Delay(10, cancellationToken);

        // Generate stub data for testing
        // Use document ID to generate consistent test data
        var hash = documentId.GetHashCode();
        var testFiles = new[]
        {
            ("Contract.docx", "application/vnd.openxmlformats-officedocument.wordprocessingml.document", 245678L),
            ("Report.pdf", "application/pdf", 512000L),
            ("Data.xlsx", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", 1024567L),
            ("Presentation.pptx", "application/vnd.openxmlformats-officedocument.presentationml.presentation", 3145728L),
            ("Notes.docx", "application/vnd.openxmlformats-officedocument.wordprocessingml.document", 45000L)
        };

        var (filename, contentType, size) = testFiles[Math.Abs(hash) % testFiles.Length];

        // Check single file size limit (25MB per file per spec NFR-03)
        if (size > maxAttachmentSizeBytes)
        {
            _logger.LogWarning(
                "Attachment exceeds size limit: DocumentId={DocumentId}, Size={Size}, Limit={Limit}",
                documentId,
                size,
                maxAttachmentSizeBytes);

            // In real implementation, this would throw or return an error
            // For stub, we'll just return a smaller file
            size = 1024000; // 1MB
        }

        // URL expiry - 5 minutes per spec
        var urlExpiry = DateTimeOffset.UtcNow.AddMinutes(5);

        // Generate pre-signed download URL (always required)
        // In real implementation, this would generate a cryptographic token
        var downloadToken = Convert.ToBase64String(
            System.Text.Encoding.UTF8.GetBytes($"{documentId}:{urlExpiry:o}"));
        var downloadUrl = $"/office/share/attach/{Uri.EscapeDataString(downloadToken)}";

        // For base64 mode, include base64 content for small files
        string? contentBase64 = null;
        if (deliveryMode == AttachmentDeliveryMode.Base64)
        {
            if (size > recommendedBase64ThresholdBytes)
            {
                _logger.LogWarning(
                    "File exceeds recommended base64 threshold: DocumentId={DocumentId}, Size={Size}, Threshold={Threshold}",
                    documentId,
                    size,
                    recommendedBase64ThresholdBytes);
            }

            // Generate stub base64 content (placeholder - real implementation would encode actual file)
            contentBase64 = Convert.ToBase64String(
                System.Text.Encoding.UTF8.GetBytes($"Stub content for {documentId}"));
        }

        return new AttachmentPackage
        {
            DocumentId = documentId,
            FileName = filename,
            ContentType = contentType,
            Size = size,
            DownloadUrl = downloadUrl,
            UrlExpiry = urlExpiry,
            ContentBase64 = contentBase64
        };
    }

    /// <inheritdoc />
    public IAsyncEnumerable<byte[]> StreamJobStatusAsync(
        Guid jobId,
        string? lastEventId,
        CancellationToken cancellationToken = default)
    {
        // Use Channel to produce events - avoids yield-inside-try-catch limitation
        var channel = System.Threading.Channels.Channel.CreateUnbounded<byte[]>(
            new System.Threading.Channels.UnboundedChannelOptions
            {
                SingleReader = true,
                SingleWriter = true
            });

        // Start the producer task
        _ = ProduceJobStatusEventsAsync(jobId, lastEventId, channel.Writer, cancellationToken);

        return channel.Reader.ReadAllAsync(cancellationToken);
    }

    /// <summary>
    /// Produces SSE events for job status streaming and writes them to the channel.
    /// </summary>
    private async Task ProduceJobStatusEventsAsync(
        Guid jobId,
        string? lastEventId,
        System.Threading.Channels.ChannelWriter<byte[]> writer,
        CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation(
                "SSE stream started for job {JobId}, LastEventId={LastEventId}",
                jobId,
                lastEventId ?? "none");

            // Parse last event ID for reconnection support
            long startSequence = 0;
            if (SseHelper.TryParseLastEventId(lastEventId, out var parsedJobId, out var parsedSequence))
            {
                if (parsedJobId == jobId)
                {
                    startSequence = parsedSequence;
                    _logger.LogInformation(
                        "SSE reconnection detected for job {JobId}, resuming from sequence {Sequence}",
                        jobId,
                        startSequence);
                }
            }

            long sequence = startSequence;
            var heartbeatInterval = TimeSpan.FromSeconds(15); // Per spec.md
            var pollInterval = TimeSpan.FromMilliseconds(500); // Internal poll frequency
            var lastHeartbeat = DateTimeOffset.UtcNow;

            // Send initial connected event
            sequence++;
            var eventId = SseHelper.GenerateEventId(jobId, sequence);
            await writer.WriteAsync(SseHelper.FormatConnected(jobId, eventId), cancellationToken);

            // Get initial job status and send it
            var currentStatus = await GetJobStatusAsync(jobId, cancellationToken);
            if (currentStatus is null)
            {
                // Job not found - send error and close
                _logger.LogWarning("SSE stream: Job {JobId} not found", jobId);
                await writer.WriteAsync(SseHelper.FormatError(
                    "OFFICE_008",
                    "Job not found or has expired",
                    jobId.ToString()), cancellationToken);
                return;
            }

            // Send initial status
            sequence++;
            eventId = SseHelper.GenerateEventId(jobId, sequence);
            await writer.WriteAsync(SseHelper.FormatProgress(
                currentStatus.Progress,
                currentStatus.CurrentPhase,
                eventId), cancellationToken);

            // Send completed phases if any
            if (currentStatus.CompletedPhases?.Count > 0)
            {
                foreach (var phase in currentStatus.CompletedPhases)
                {
                    // Only send phases after the reconnection point
                    sequence++;
                    if (sequence <= startSequence)
                        continue;

                    eventId = SseHelper.GenerateEventId(jobId, sequence);
                    await writer.WriteAsync(SseHelper.FormatStageUpdate(
                        phase.Name,
                        "Completed",
                        phase.CompletedAt,
                        eventId), cancellationToken);
                }
            }

            // Check if job is already in terminal state
            if (currentStatus.Status is JobStatus.Completed or JobStatus.Failed or JobStatus.Cancelled)
            {
                sequence++;
                eventId = SseHelper.GenerateEventId(jobId, sequence);

                if (currentStatus.Status == JobStatus.Completed)
                {
                    _logger.LogInformation("SSE stream: Job {JobId} already completed", jobId);
                    await writer.WriteAsync(SseHelper.FormatJobComplete(
                        jobId,
                        currentStatus.Result?.Artifact?.Id,
                        currentStatus.Result?.Artifact?.WebUrl,
                        eventId), cancellationToken);
                }
                else
                {
                    _logger.LogInformation("SSE stream: Job {JobId} already failed/cancelled", jobId);
                    await writer.WriteAsync(SseHelper.FormatJobFailed(
                        jobId,
                        currentStatus.Error?.Code ?? "OFFICE_INTERNAL",
                        currentStatus.Error?.Message ?? "Job failed",
                        currentStatus.Error?.Retryable ?? false,
                        eventId), cancellationToken);
                }

                return;
            }

            // Main streaming loop using Redis pub/sub via JobStatusService
            // Falls back to polling if Redis subscription fails
            var useRedisSubscription = await _jobStatusService.IsHealthyAsync(cancellationToken);

            if (useRedisSubscription)
            {
                _logger.LogInformation(
                    "SSE stream using Redis pub/sub for job {JobId}",
                    jobId);

                // Start heartbeat task
                using var heartbeatCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                var heartbeatTask = SendHeartbeatsAsync(
                    jobId,
                    writer,
                    heartbeatInterval,
                    heartbeatCts.Token,
                    () => sequence);

                try
                {
                    // Subscribe to job status updates via Redis pub/sub
                    await foreach (var update in _jobStatusService.SubscribeToJobAsync(jobId, cancellationToken))
                    {
                        // Skip updates we've already sent (based on sequence)
                        if (update.Sequence <= startSequence)
                        {
                            _logger.LogDebug(
                                "SSE stream: Skipping update with sequence {Sequence} (already sent) for job {JobId}",
                                update.Sequence,
                                jobId);
                            continue;
                        }

                        // Update our sequence tracker
                        sequence = Math.Max(sequence, update.Sequence);
                        eventId = SseHelper.GenerateEventId(jobId, sequence);

                        // Format and send the SSE event based on update type
                        var sseEvent = update.UpdateType switch
                        {
                            JobStatusUpdateType.Progress => SseHelper.FormatProgress(
                                update.Progress,
                                update.CurrentPhase,
                                eventId),

                            JobStatusUpdateType.StageComplete when update.CompletedPhase is not null =>
                                SseHelper.FormatStageUpdate(
                                    update.CompletedPhase.Name,
                                    "Completed",
                                    update.CompletedPhase.CompletedAt,
                                    eventId),

                            JobStatusUpdateType.StageStarted when update.CurrentPhase is not null =>
                                SseHelper.FormatStageUpdate(
                                    update.CurrentPhase,
                                    "Running",
                                    update.Timestamp,
                                    eventId),

                            JobStatusUpdateType.JobCompleted => SseHelper.FormatJobComplete(
                                jobId,
                                update.Result?.Artifact?.Id,
                                update.Result?.Artifact?.WebUrl,
                                eventId),

                            JobStatusUpdateType.JobFailed or JobStatusUpdateType.JobCancelled =>
                                SseHelper.FormatJobFailed(
                                    jobId,
                                    update.Error?.Code ?? "OFFICE_INTERNAL",
                                    update.Error?.Message ?? "Job failed",
                                    update.Error?.Retryable ?? false,
                                    eventId),

                            _ => SseHelper.FormatProgress(update.Progress, update.CurrentPhase, eventId)
                        };

                        await writer.WriteAsync(sseEvent, cancellationToken);

                        _logger.LogDebug(
                            "SSE event sent for job {JobId}: Type={UpdateType}, Progress={Progress}",
                            jobId,
                            update.UpdateType,
                            update.Progress);

                        // Terminal states end the stream
                        if (update.UpdateType is JobStatusUpdateType.JobCompleted
                            or JobStatusUpdateType.JobFailed
                            or JobStatusUpdateType.JobCancelled)
                        {
                            _logger.LogInformation(
                                "SSE stream ending for job {JobId} due to terminal state {State}",
                                jobId,
                                update.UpdateType);
                            return;
                        }
                    }
                }
                finally
                {
                    // Cancel heartbeat task
                    heartbeatCts.Cancel();
                    try { await heartbeatTask; } catch (OperationCanceledException) { }
                }
            }
            else
            {
                // Fallback to polling when Redis is unavailable
                _logger.LogWarning(
                    "SSE stream falling back to polling for job {JobId} (Redis unavailable)",
                    jobId);

                var fallbackPollInterval = TimeSpan.FromMilliseconds(500);
                var previousStatus = currentStatus.Status;
                var previousProgress = currentStatus.Progress;
                var previousPhase = currentStatus.CurrentPhase;
                var previousCompletedPhaseCount = currentStatus.CompletedPhases?.Count ?? 0;
                var fallbackLastHeartbeat = DateTimeOffset.UtcNow;

                while (!cancellationToken.IsCancellationRequested)
                {
                    // Check if heartbeat is needed
                    var now = DateTimeOffset.UtcNow;
                    if (now - fallbackLastHeartbeat >= heartbeatInterval)
                    {
                        sequence++;
                        eventId = SseHelper.GenerateEventId(jobId, sequence);
                        await writer.WriteAsync(SseHelper.FormatHeartbeat(now, eventId), cancellationToken);
                        fallbackLastHeartbeat = now;
                        _logger.LogDebug("SSE heartbeat sent for job {JobId}", jobId);
                    }

                    await Task.Delay(fallbackPollInterval, cancellationToken);

                    currentStatus = await GetJobStatusAsync(jobId, cancellationToken);
                    if (currentStatus is null)
                    {
                        _logger.LogWarning("SSE stream: Job {JobId} was deleted during streaming", jobId);
                        await writer.WriteAsync(SseHelper.FormatError(
                            "OFFICE_008",
                            "Job no longer exists",
                            jobId.ToString()), cancellationToken);
                        return;
                    }

                    // Send progress updates
                    if (currentStatus.Progress != previousProgress)
                    {
                        sequence++;
                        eventId = SseHelper.GenerateEventId(jobId, sequence);
                        await writer.WriteAsync(SseHelper.FormatProgress(
                            currentStatus.Progress,
                            currentStatus.CurrentPhase,
                            eventId), cancellationToken);
                        previousProgress = currentStatus.Progress;
                    }

                    // Send completed phase updates
                    var currentCompletedPhaseCount = currentStatus.CompletedPhases?.Count ?? 0;
                    if (currentCompletedPhaseCount > previousCompletedPhaseCount)
                    {
                        for (var i = previousCompletedPhaseCount; i < currentCompletedPhaseCount; i++)
                        {
                            var phase = currentStatus.CompletedPhases![i];
                            sequence++;
                            eventId = SseHelper.GenerateEventId(jobId, sequence);
                            await writer.WriteAsync(SseHelper.FormatStageUpdate(
                                phase.Name,
                                "Completed",
                                phase.CompletedAt,
                                eventId), cancellationToken);
                        }
                        previousCompletedPhaseCount = currentCompletedPhaseCount;
                    }

                    // Send current phase change
                    if (currentStatus.CurrentPhase != previousPhase && !string.IsNullOrEmpty(currentStatus.CurrentPhase))
                    {
                        sequence++;
                        eventId = SseHelper.GenerateEventId(jobId, sequence);
                        await writer.WriteAsync(SseHelper.FormatStageUpdate(
                            currentStatus.CurrentPhase,
                            "Running",
                            DateTimeOffset.UtcNow,
                            eventId), cancellationToken);
                        previousPhase = currentStatus.CurrentPhase;
                    }

                    // Check for terminal state
                    if (currentStatus.Status != previousStatus &&
                        currentStatus.Status is JobStatus.Completed or JobStatus.Failed or JobStatus.Cancelled)
                    {
                        sequence++;
                        eventId = SseHelper.GenerateEventId(jobId, sequence);

                        if (currentStatus.Status == JobStatus.Completed)
                        {
                            await writer.WriteAsync(SseHelper.FormatJobComplete(
                                jobId,
                                currentStatus.Result?.Artifact?.Id,
                                currentStatus.Result?.Artifact?.WebUrl,
                                eventId), cancellationToken);
                        }
                        else
                        {
                            await writer.WriteAsync(SseHelper.FormatJobFailed(
                                jobId,
                                currentStatus.Error?.Code ?? "OFFICE_INTERNAL",
                                currentStatus.Error?.Message ?? $"Job {currentStatus.Status.ToString().ToLowerInvariant()}",
                                currentStatus.Error?.Retryable ?? false,
                                eventId), cancellationToken);
                        }
                        return;
                    }
                    previousStatus = currentStatus.Status;
                }
            }

            _logger.LogInformation(
                "SSE stream ended for job {JobId} (cancellation requested)",
                jobId);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation(
                "SSE stream cancelled for job {JobId} (client disconnected)",
                jobId);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "SSE stream error for job {JobId}",
                jobId);

            // Send terminal error event per ADR-019
            try
            {
                await writer.WriteAsync(SseHelper.FormatError(
                    "OFFICE_INTERNAL",
                    "Internal server error during job status streaming",
                    jobId.ToString()), CancellationToken.None);
            }
            catch
            {
                // Ignore errors when writing final error event
            }
        }
        finally
        {
            writer.Complete();
        }
    }

    /// <summary>
    /// Sends heartbeat events at regular intervals to keep the SSE connection alive.
    /// </summary>
    private async Task SendHeartbeatsAsync(
        Guid jobId,
        System.Threading.Channels.ChannelWriter<byte[]> writer,
        TimeSpan interval,
        CancellationToken cancellationToken,
        Func<long> getCurrentSequence)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(interval, cancellationToken);

                var sequence = getCurrentSequence() + 1;
                var eventId = SseHelper.GenerateEventId(jobId, sequence);
                var heartbeatEvent = SseHelper.FormatHeartbeat(DateTimeOffset.UtcNow, eventId);

                await writer.WriteAsync(heartbeatEvent, cancellationToken);

                _logger.LogDebug("SSE heartbeat sent for job {JobId}", jobId);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected when cancellation is requested
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Error sending heartbeat for job {JobId}",
                jobId);
        }
    }
}
