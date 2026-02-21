using Microsoft.Extensions.Options;
using Microsoft.Graph;
using Microsoft.Graph.Models;
using Microsoft.Graph.Models.ODataErrors;
using Microsoft.Graph.Users.Item.SendMail;
using Microsoft.Xrm.Sdk;
using DataverseEntity = Microsoft.Xrm.Sdk.Entity;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Infrastructure.Exceptions;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Models;
using Sprk.Bff.Api.Services.Communication.Models;

namespace Sprk.Bff.Api.Services.Communication;

/// <summary>
/// Core communication service that validates requests, resolves approved senders,
/// builds Graph Message payloads, sends email via GraphClientFactory.ForApp(),
/// and creates a Dataverse sprk_communication tracking record (best-effort).
/// </summary>
public sealed class CommunicationService
{
    private readonly IGraphClientFactory _graphClientFactory;
    private readonly ApprovedSenderValidator _senderValidator;
    private readonly IDataverseService _dataverseService;
    private readonly EmlGenerationService _emlGenerationService;
    private readonly SpeFileStore _speFileStore;
    private readonly CommunicationOptions _options;
    private readonly ILogger<CommunicationService> _logger;

    public CommunicationService(
        IGraphClientFactory graphClientFactory,
        ApprovedSenderValidator senderValidator,
        IDataverseService dataverseService,
        EmlGenerationService emlGenerationService,
        SpeFileStore speFileStore,
        IOptions<CommunicationOptions> options,
        ILogger<CommunicationService> logger)
    {
        _graphClientFactory = graphClientFactory;
        _senderValidator = senderValidator;
        _dataverseService = dataverseService;
        _emlGenerationService = emlGenerationService;
        _speFileStore = speFileStore;
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>
    /// Sends a communication via Microsoft Graph sendMail API.
    /// Validates the request, resolves the sender, constructs a Graph Message, and sends.
    /// On failure, throws SdapProblemException immediately (no retry).
    /// </summary>
    /// <param name="request">The send communication request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>SendCommunicationResponse on success.</returns>
    /// <exception cref="SdapProblemException">Thrown for validation errors, invalid sender, or Graph failures.</exception>
    public async Task<SendCommunicationResponse> SendAsync(
        SendCommunicationRequest request,
        CancellationToken cancellationToken = default)
    {
        var correlationId = request.CorrelationId ?? Guid.NewGuid().ToString("N");

        _logger.LogInformation(
            "Sending communication | CorrelationId: {CorrelationId}, To: {RecipientCount}, Type: {Type}",
            correlationId,
            request.To.Length,
            request.CommunicationType);

        // Step 1: Validate request
        ValidateRequest(request, correlationId);

        // Step 1b: Download and validate attachments (if any)
        List<FileAttachment>? fileAttachments = null;
        if (request.AttachmentDocumentIds is { Length: > 0 })
        {
            fileAttachments = await DownloadAndBuildAttachmentsAsync(
                request.AttachmentDocumentIds, correlationId, cancellationToken);
        }

        // Step 2: Resolve sender
        var senderResult = _senderValidator.Resolve(request.FromMailbox);
        if (!senderResult.IsValid)
        {
            _logger.LogWarning(
                "Sender validation failed | CorrelationId: {CorrelationId}, ErrorCode: {ErrorCode}, FromMailbox: {FromMailbox}",
                correlationId,
                senderResult.ErrorCode,
                request.FromMailbox);

            throw new SdapProblemException(
                code: senderResult.ErrorCode!,
                title: "Invalid Sender",
                detail: senderResult.ErrorDetail,
                statusCode: 400,
                extensions: new Dictionary<string, object>
                {
                    ["correlationId"] = correlationId
                });
        }

        // Step 3: Build Graph message
        var message = BuildGraphMessage(request, senderResult);

        // Step 3b: Attach files to message (if any)
        if (fileAttachments is { Count: > 0 })
        {
            message.Attachments = new List<Attachment>(fileAttachments);
        }

        // Step 4: Send via Graph API (app-only)
        try
        {
            var graphClient = _graphClientFactory.ForApp();

            await graphClient.Users[senderResult.Email].SendMail.PostAsync(
                new SendMailPostRequestBody
                {
                    Message = message,
                    SaveToSentItems = true
                },
                cancellationToken: cancellationToken);

            _logger.LogInformation(
                "Communication sent successfully | CorrelationId: {CorrelationId}, From: {From}, To: {RecipientCount}",
                correlationId,
                senderResult.Email,
                request.To.Length);

            // Step 5: Create Dataverse communication record (best-effort)
            Guid? communicationId = null;
            try
            {
                communicationId = await CreateDataverseRecordAsync(request, senderResult, correlationId, cancellationToken);
            }
            catch (Exception dvEx)
            {
                _logger.LogWarning(
                    dvEx,
                    "Dataverse record creation failed (non-fatal) | CorrelationId: {CorrelationId}",
                    correlationId);
            }

            // Step 6: Archive to SPE if requested (best-effort)
            Guid? archivedDocumentId = null;
            string? archivalWarning = null;
            if (request.ArchiveToSpe && communicationId.HasValue)
            {
                // Build a partial response for .eml generation (before archival fields are known)
                var partialResponse = new SendCommunicationResponse
                {
                    CommunicationId = communicationId,
                    GraphMessageId = correlationId,
                    Status = CommunicationStatus.Send,
                    SentAt = DateTimeOffset.UtcNow,
                    From = senderResult.Email!,
                    CorrelationId = correlationId
                };

                try
                {
                    archivedDocumentId = await ArchiveToSpeAsync(request, partialResponse, communicationId.Value, cancellationToken);
                }
                catch (Exception archEx)
                {
                    archivalWarning = $"Email sent successfully but archival failed: {archEx.Message}";
                    _logger.LogWarning(
                        archEx,
                        "SPE archival failed (non-fatal) | CorrelationId: {CorrelationId}",
                        correlationId);
                }
            }
            else if (request.ArchiveToSpe && !communicationId.HasValue)
            {
                archivalWarning = "Email sent successfully but archival skipped: Dataverse communication record was not created.";
                _logger.LogWarning(
                    "SPE archival skipped because Dataverse record creation failed | CorrelationId: {CorrelationId}",
                    correlationId);
            }

            // Step 7: Create sprk_communicationattachment records (best-effort)
            string? attachmentRecordWarning = null;
            if (request.AttachmentDocumentIds is { Length: > 0 } && communicationId.HasValue)
            {
                // Extract file names from the downloaded attachments for display names
                var attachmentNames = fileAttachments?
                    .Select(fa => fa.Name ?? "Unknown")
                    .ToArray() ?? Array.Empty<string>();

                try
                {
                    await CreateAttachmentRecordsAsync(
                        communicationId.Value,
                        request.AttachmentDocumentIds,
                        attachmentNames,
                        correlationId,
                        cancellationToken);
                }
                catch (Exception attEx)
                {
                    attachmentRecordWarning = $"Email sent successfully but attachment record creation failed: {attEx.Message}";
                    _logger.LogWarning(
                        attEx,
                        "Attachment record creation failed (non-fatal) | CommunicationId: {CommunicationId}, CorrelationId: {CorrelationId}",
                        communicationId.Value,
                        correlationId);
                }
            }
            else if (request.AttachmentDocumentIds is { Length: > 0 } && !communicationId.HasValue)
            {
                attachmentRecordWarning = "Email sent successfully but attachment records skipped: Dataverse communication record was not created.";
                _logger.LogWarning(
                    "Attachment record creation skipped because Dataverse record creation failed | CorrelationId: {CorrelationId}",
                    correlationId);
            }

            return new SendCommunicationResponse
            {
                CommunicationId = communicationId,
                GraphMessageId = correlationId, // Graph sendMail doesn't return a message ID; using correlationId as tracking reference
                Status = CommunicationStatus.Send,
                SentAt = DateTimeOffset.UtcNow,
                From = senderResult.Email!,
                CorrelationId = correlationId,
                ArchivedDocumentId = archivedDocumentId,
                ArchivalWarning = archivalWarning,
                AttachmentCount = request.AttachmentDocumentIds?.Length ?? 0,
                AttachmentRecordWarning = attachmentRecordWarning
            };
        }
        catch (ODataError ex)
        {
            _logger.LogError(
                ex,
                "Graph sendMail failed | CorrelationId: {CorrelationId}, StatusCode: {StatusCode}, ErrorCode: {ErrorCode}",
                correlationId,
                ex.ResponseStatusCode,
                ex.Error?.Code);

            throw new SdapProblemException(
                code: "GRAPH_SEND_FAILED",
                title: "Email Send Failed",
                detail: $"Graph API error: {ex.Error?.Message ?? ex.Message}",
                statusCode: ex.ResponseStatusCode > 0 ? ex.ResponseStatusCode : 502,
                extensions: new Dictionary<string, object>
                {
                    ["correlationId"] = correlationId,
                    ["graphErrorCode"] = ex.Error?.Code ?? "unknown"
                });
        }
        catch (Exception ex) when (ex is not SdapProblemException)
        {
            _logger.LogError(
                ex,
                "Unexpected error sending communication | CorrelationId: {CorrelationId}",
                correlationId);

            throw new SdapProblemException(
                code: "GRAPH_SEND_FAILED",
                title: "Email Send Failed",
                detail: $"Unexpected error: {ex.Message}",
                statusCode: 500,
                extensions: new Dictionary<string, object>
                {
                    ["correlationId"] = correlationId
                });
        }
    }

    private static void ValidateRequest(SendCommunicationRequest request, string correlationId)
    {
        if (request.To.Length == 0)
        {
            throw new SdapProblemException(
                code: "VALIDATION_ERROR",
                title: "Validation Error",
                detail: "At least one recipient (To) is required.",
                statusCode: 400,
                extensions: new Dictionary<string, object>
                {
                    ["correlationId"] = correlationId
                });
        }

        if (string.IsNullOrWhiteSpace(request.Subject))
        {
            throw new SdapProblemException(
                code: "VALIDATION_ERROR",
                title: "Validation Error",
                detail: "Subject is required.",
                statusCode: 400,
                extensions: new Dictionary<string, object>
                {
                    ["correlationId"] = correlationId
                });
        }

        if (string.IsNullOrWhiteSpace(request.Body))
        {
            throw new SdapProblemException(
                code: "VALIDATION_ERROR",
                title: "Validation Error",
                detail: "Body is required.",
                statusCode: 400,
                extensions: new Dictionary<string, object>
                {
                    ["correlationId"] = correlationId
                });
        }
    }

    private static Message BuildGraphMessage(SendCommunicationRequest request, ApprovedSenderResult sender)
    {
        return new Message
        {
            Subject = request.Subject,
            Body = new ItemBody
            {
                ContentType = request.BodyFormat == BodyFormat.PlainText ? BodyType.Text : BodyType.Html,
                Content = request.Body
            },
            From = new Recipient
            {
                EmailAddress = new EmailAddress
                {
                    Address = sender.Email,
                    Name = sender.DisplayName
                }
            },
            ToRecipients = request.To.Select(email => new Recipient
            {
                EmailAddress = new EmailAddress { Address = email }
            }).ToList(),
            CcRecipients = (request.Cc ?? Array.Empty<string>()).Select(email => new Recipient
            {
                EmailAddress = new EmailAddress { Address = email }
            }).ToList(),
            BccRecipients = (request.Bcc ?? Array.Empty<string>()).Select(email => new Recipient
            {
                EmailAddress = new EmailAddress { Address = email }
            }).ToList()
        };
    }

    /// <summary>
    /// Creates a Dataverse sprk_communication record to track the sent email.
    /// </summary>
    private async Task<Guid> CreateDataverseRecordAsync(
        SendCommunicationRequest request,
        ApprovedSenderResult sender,
        string correlationId,
        CancellationToken ct)
    {
        var communication = new DataverseEntity("sprk_communication")
        {
            ["sprk_name"] = $"Email: {TruncateTo(request.Subject, 200)}",
            ["sprk_communiationtype"] = new OptionSetValue((int)request.CommunicationType), // NOTE: typo "communiation" is intentional (actual Dataverse schema)
            ["statuscode"] = new OptionSetValue((int)CommunicationStatus.Send),
            ["statecode"] = new OptionSetValue(0), // Active
            ["sprk_direction"] = new OptionSetValue((int)CommunicationDirection.Outgoing),
            ["sprk_bodyformat"] = new OptionSetValue((int)request.BodyFormat),
            ["sprk_to"] = string.Join("; ", request.To),
            ["sprk_from"] = sender.Email,
            ["sprk_subject"] = request.Subject,
            ["sprk_body"] = request.Body,
            ["sprk_graphmessageid"] = correlationId,
            ["sprk_sentat"] = DateTimeOffset.UtcNow.DateTime,
            ["sprk_correlationid"] = correlationId
        };

        // Only set CC/BCC if provided
        if (request.Cc is { Length: > 0 })
            communication["sprk_cc"] = string.Join("; ", request.Cc);
        if (request.Bcc is { Length: > 0 })
            communication["sprk_bcc"] = string.Join("; ", request.Bcc);

        // Set attachment fields if attachments were included
        if (request.AttachmentDocumentIds is { Length: > 0 })
        {
            communication["sprk_hasattachments"] = true;
            communication["sprk_attachmentcount"] = request.AttachmentDocumentIds.Length;
        }

        // Map primary association (regarding lookup + denormalized fields)
        MapAssociationFields(communication, request.Associations, _logger);

        var recordId = await _dataverseService.CreateAsync(communication, ct);

        _logger.LogInformation(
            "Dataverse communication record created | Id: {RecordId}, CorrelationId: {CorrelationId}",
            recordId,
            correlationId);

        return recordId;
    }

    /// <summary>
    /// Maps from EntityType to (Dataverse lookup field name, target entity set name).
    /// </summary>
    private static readonly Dictionary<string, (string LookupField, string EntitySetName)> RegardingLookupMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["sprk_matter"] = ("sprk_regardingmatter", "sprk_matters"),
        ["sprk_organization"] = ("sprk_regardingorganization", "sprk_organizations"),
        ["contact"] = ("sprk_regardingperson", "contacts"),
        ["sprk_project"] = ("sprk_regardingproject", "sprk_projects"),
        ["sprk_analysis"] = ("sprk_regardinganalysis", "sprk_analysises"),
        ["sprk_budget"] = ("sprk_regardingbudget", "sprk_budgets"),
        ["sprk_invoice"] = ("sprk_regardinginvoice", "sprk_invoices"),
        ["sprk_workassignment"] = ("sprk_regardingworkassignment", "sprk_workassignments"),
    };

    /// <summary>
    /// Maps the primary association (associations[0]) to Dataverse regarding lookup
    /// and denormalized text fields on the sprk_communication entity.
    /// </summary>
    private static void MapAssociationFields(
        DataverseEntity communication,
        CommunicationAssociation[]? associations,
        ILogger logger)
    {
        if (associations is not { Length: > 0 })
            return;

        communication["sprk_associationcount"] = associations.Length;

        var primary = associations[0];

        // Set the regarding lookup field for the primary association
        if (RegardingLookupMap.TryGetValue(primary.EntityType, out var mapping))
        {
            communication[mapping.LookupField] = new EntityReference(primary.EntityType, primary.EntityId);
        }
        else
        {
            logger.LogWarning(
                "Unknown entity type for association lookup mapping: {EntityType}. Regarding lookup will not be set.",
                primary.EntityType);
        }

        // Set denormalized regarding fields
        if (primary.EntityName is not null)
            communication["sprk_regardingrecordname"] = TruncateTo(primary.EntityName, 100);

        communication["sprk_regardingrecordid"] = TruncateTo(primary.EntityId.ToString(), 100);

        if (primary.EntityUrl is not null)
            communication["sprk_regardingrecordurl"] = TruncateTo(primary.EntityUrl, 200);
    }

    /// <summary>
    /// Archives the sent communication as a .eml file in SharePoint Embedded
    /// and creates a sprk_document record linking to the archived file.
    /// </summary>
    private async Task<Guid> ArchiveToSpeAsync(
        SendCommunicationRequest request,
        SendCommunicationResponse partialResponse,
        Guid communicationId,
        CancellationToken ct)
    {
        // 1. Generate .eml via EmlGenerationService
        var emlResult = _emlGenerationService.GenerateEml(request, partialResponse);

        // 2. Upload to SPE at /communications/{commId:N}/{fileName}.eml
        var driveId = _options.ArchiveContainerId;
        if (string.IsNullOrWhiteSpace(driveId))
        {
            throw new InvalidOperationException("ArchiveContainerId not configured for SPE archival");
        }

        var spePath = $"/communications/{communicationId:N}/{emlResult.FileName}";

        using var stream = new MemoryStream(emlResult.Content);
        var fileHandle = await _speFileStore.UploadSmallAsync(driveId, spePath, stream, ct);

        _logger.LogInformation(
            "Archived communication .eml to SPE | CommunicationId: {CommunicationId}, Path: {Path}",
            communicationId, spePath);

        // 3. Create sprk_document record linking to the archived file
        var document = new DataverseEntity("sprk_document")
        {
            ["sprk_name"] = $"Archived: {TruncateTo(request.Subject, 180)}",
            ["sprk_documenttype"] = new OptionSetValue(100000002), // Communication
            ["sprk_sourcetype"] = new OptionSetValue(100000001), // CommunicationArchive
            ["sprk_communication"] = new EntityReference("sprk_communication", communicationId),
            ["sprk_speitemid"] = fileHandle?.Id,
            ["sprk_spedriveid"] = driveId,
        };

        var documentId = await _dataverseService.CreateAsync(document, ct);

        _logger.LogInformation(
            "Created sprk_document record for archived .eml | DocumentId: {DocumentId}, CommunicationId: {CommunicationId}",
            documentId, communicationId);

        return documentId;
    }

    /// <summary>
    /// Creates sprk_communicationattachment records in Dataverse to link each attached
    /// document to the communication record. Records are created sequentially.
    /// </summary>
    /// <param name="communicationId">The Dataverse sprk_communication record ID.</param>
    /// <param name="attachmentDocumentIds">SPE document IDs that were attached.</param>
    /// <param name="attachmentNames">File names corresponding to each document ID (from metadata fetched during download).</param>
    /// <param name="correlationId">Correlation ID for tracing.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Number of attachment records successfully created.</returns>
    private async Task<int> CreateAttachmentRecordsAsync(
        Guid communicationId,
        string[] attachmentDocumentIds,
        string[] attachmentNames,
        string correlationId,
        CancellationToken ct)
    {
        var createdCount = 0;

        for (var i = 0; i < attachmentDocumentIds.Length; i++)
        {
            var documentId = attachmentDocumentIds[i];
            var documentName = i < attachmentNames.Length ? attachmentNames[i] : $"Attachment {i + 1}";

            var attachment = new DataverseEntity("sprk_communicationattachment")
            {
                ["sprk_name"] = TruncateTo(documentName, 200),
                ["sprk_communication"] = new EntityReference("sprk_communication", communicationId),
                ["sprk_document"] = new EntityReference("sprk_document", Guid.Parse(documentId)),
                ["sprk_attachmenttype"] = new OptionSetValue(100000000) // File
            };

            var attachmentId = await _dataverseService.CreateAsync(attachment, ct);
            createdCount++;

            _logger.LogDebug(
                "Created sprk_communicationattachment record | Id: {AttachmentId}, CommunicationId: {CommunicationId}, DocumentId: {DocumentId}, Name: {Name}, CorrelationId: {CorrelationId}",
                attachmentId, communicationId, documentId, documentName, correlationId);
        }

        _logger.LogInformation(
            "Attachment records created | Count: {Count}, CommunicationId: {CommunicationId}, CorrelationId: {CorrelationId}",
            createdCount, communicationId, correlationId);

        return createdCount;
    }

    /// <summary>
    /// Maximum number of attachments allowed per email (Graph API limit).
    /// </summary>
    private const int MaxAttachmentCount = 150;

    /// <summary>
    /// Maximum total size of all attachments in bytes (35 MB).
    /// </summary>
    private const long MaxTotalAttachmentSizeBytes = 36_700_160;

    /// <summary>
    /// Downloads files from SPE, validates attachment count and total size limits,
    /// and builds a list of Graph FileAttachment objects for sendMail.
    /// </summary>
    /// <exception cref="SdapProblemException">
    /// Thrown when attachment count exceeds 150, total size exceeds 35MB,
    /// or an individual file download fails.
    /// </exception>
    private async Task<List<FileAttachment>> DownloadAndBuildAttachmentsAsync(
        string[] attachmentDocumentIds,
        string correlationId,
        CancellationToken ct)
    {
        // Validate count limit
        if (attachmentDocumentIds.Length > MaxAttachmentCount)
        {
            throw new SdapProblemException(
                code: "ATTACHMENT_LIMIT_EXCEEDED",
                title: "Too Many Attachments",
                detail: $"Maximum {MaxAttachmentCount} attachments allowed. Received {attachmentDocumentIds.Length}.",
                statusCode: 400,
                extensions: new Dictionary<string, object>
                {
                    ["correlationId"] = correlationId,
                    ["maxAttachments"] = MaxAttachmentCount,
                    ["requestedAttachments"] = attachmentDocumentIds.Length
                });
        }

        var driveId = _options.ArchiveContainerId;
        if (string.IsNullOrWhiteSpace(driveId))
        {
            throw new SdapProblemException(
                code: "ATTACHMENT_CONFIG_ERROR",
                title: "Attachment Configuration Error",
                detail: "ArchiveContainerId is not configured. Cannot download attachments from SPE.",
                statusCode: 500,
                extensions: new Dictionary<string, object>
                {
                    ["correlationId"] = correlationId
                });
        }

        var attachments = new List<FileAttachment>(attachmentDocumentIds.Length);
        long totalSize = 0;

        for (var i = 0; i < attachmentDocumentIds.Length; i++)
        {
            var itemId = attachmentDocumentIds[i];

            _logger.LogDebug(
                "Downloading attachment {Index}/{Total} | ItemId: {ItemId}, CorrelationId: {CorrelationId}",
                i + 1, attachmentDocumentIds.Length, itemId, correlationId);

            // Get file metadata for name and size
            FileHandleDto? metadata;
            try
            {
                metadata = await _speFileStore.GetFileMetadataAsync(driveId, itemId, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Failed to get metadata for attachment | ItemId: {ItemId}, CorrelationId: {CorrelationId}",
                    itemId, correlationId);

                throw new SdapProblemException(
                    code: "ATTACHMENT_DOWNLOAD_FAILED",
                    title: "Attachment Download Failed",
                    detail: $"Failed to retrieve metadata for document '{itemId}': {ex.Message}",
                    statusCode: 502,
                    extensions: new Dictionary<string, object>
                    {
                        ["correlationId"] = correlationId,
                        ["failedDocumentId"] = itemId,
                        ["attachmentIndex"] = i
                    });
            }

            if (metadata is null)
            {
                throw new SdapProblemException(
                    code: "ATTACHMENT_NOT_FOUND",
                    title: "Attachment Not Found",
                    detail: $"Document '{itemId}' was not found in SPE.",
                    statusCode: 404,
                    extensions: new Dictionary<string, object>
                    {
                        ["correlationId"] = correlationId,
                        ["failedDocumentId"] = itemId,
                        ["attachmentIndex"] = i
                    });
            }

            // Track total size and validate before downloading content
            totalSize += metadata.Size ?? 0;
            if (totalSize > MaxTotalAttachmentSizeBytes)
            {
                throw new SdapProblemException(
                    code: "ATTACHMENT_LIMIT_EXCEEDED",
                    title: "Attachments Too Large",
                    detail: $"Total attachment size exceeds {MaxTotalAttachmentSizeBytes / (1024 * 1024)}MB limit. " +
                            $"Total size so far: {totalSize} bytes (at attachment {i + 1} of {attachmentDocumentIds.Length}).",
                    statusCode: 400,
                    extensions: new Dictionary<string, object>
                    {
                        ["correlationId"] = correlationId,
                        ["maxTotalSizeBytes"] = MaxTotalAttachmentSizeBytes,
                        ["currentTotalSizeBytes"] = totalSize,
                        ["failedAtIndex"] = i
                    });
            }

            // Download file content
            Stream? contentStream;
            try
            {
                contentStream = await _speFileStore.DownloadFileAsync(driveId, itemId, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Failed to download attachment content | ItemId: {ItemId}, CorrelationId: {CorrelationId}",
                    itemId, correlationId);

                throw new SdapProblemException(
                    code: "ATTACHMENT_DOWNLOAD_FAILED",
                    title: "Attachment Download Failed",
                    detail: $"Failed to download document '{itemId}': {ex.Message}",
                    statusCode: 502,
                    extensions: new Dictionary<string, object>
                    {
                        ["correlationId"] = correlationId,
                        ["failedDocumentId"] = itemId,
                        ["attachmentIndex"] = i
                    });
            }

            if (contentStream is null)
            {
                throw new SdapProblemException(
                    code: "ATTACHMENT_DOWNLOAD_FAILED",
                    title: "Attachment Download Failed",
                    detail: $"Download returned no content for document '{itemId}'.",
                    statusCode: 502,
                    extensions: new Dictionary<string, object>
                    {
                        ["correlationId"] = correlationId,
                        ["failedDocumentId"] = itemId,
                        ["attachmentIndex"] = i
                    });
            }

            // Read stream to byte array for base64 encoding
            byte[] contentBytes;
            await using (contentStream)
            {
                using var ms = new MemoryStream();
                await contentStream.CopyToAsync(ms, ct);
                contentBytes = ms.ToArray();
            }

            attachments.Add(new FileAttachment
            {
                OdataType = "#microsoft.graph.fileAttachment",
                Name = metadata.Name,
                ContentType = InferContentType(metadata.Name),
                ContentBytes = contentBytes
            });

            _logger.LogDebug(
                "Attachment {Index}/{Total} prepared | Name: {Name}, Size: {Size} bytes, CorrelationId: {CorrelationId}",
                i + 1, attachmentDocumentIds.Length, metadata.Name, contentBytes.Length, correlationId);
        }

        _logger.LogInformation(
            "All attachments prepared | Count: {Count}, TotalSize: {TotalSize} bytes, CorrelationId: {CorrelationId}",
            attachments.Count, totalSize, correlationId);

        return attachments;
    }

    /// <summary>
    /// Infers a MIME content type from a file name's extension.
    /// Falls back to application/octet-stream for unknown extensions.
    /// </summary>
    private static string InferContentType(string fileName)
    {
        var extension = Path.GetExtension(fileName)?.ToLowerInvariant();
        return extension switch
        {
            ".pdf" => "application/pdf",
            ".doc" => "application/msword",
            ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            ".xls" => "application/vnd.ms-excel",
            ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            ".ppt" => "application/vnd.ms-powerpoint",
            ".pptx" => "application/vnd.openxmlformats-officedocument.presentationml.presentation",
            ".txt" => "text/plain",
            ".csv" => "text/csv",
            ".html" or ".htm" => "text/html",
            ".xml" => "application/xml",
            ".json" => "application/json",
            ".zip" => "application/zip",
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".gif" => "image/gif",
            ".bmp" => "image/bmp",
            ".svg" => "image/svg+xml",
            ".eml" => "message/rfc822",
            ".msg" => "application/vnd.ms-outlook",
            _ => "application/octet-stream"
        };
    }

    private static string TruncateTo(string value, int maxLength)
        => value.Length <= maxLength ? value : value[..maxLength];
}
