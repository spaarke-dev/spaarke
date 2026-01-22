namespace Sprk.Bff.Api.Models;

/// <summary>
/// ProcessingJob entity model (sprk_processingjob).
/// Tracks async processing jobs for document uploads and email saves (ADR-004 compliant).
/// Created by: SDAP Office Integration project (tasks 010-012)
/// </summary>
public class ProcessingJob
{
    /// <summary>Processing Job ID (sprk_processingjobid - primary key)</summary>
    public Guid Id { get; set; }

    /// <summary>Job name (sprk_name - primary name field, auto-generated GUID)</summary>
    public string? Name { get; set; }

    /// <summary>Job type choice value (sprk_jobtype).
    /// Values: DocumentSave=0, EmailSave=1, ShareLinks=2, QuickCreate=3, ProfileSummary=4, Indexing=5, DeepAnalysis=6
    /// </summary>
    public int? JobType { get; set; }

    /// <summary>Job status choice value (sprk_status - required).
    /// Values: Pending=0, InProgress=1, Completed=2, Failed=3, Cancelled=4
    /// </summary>
    public int Status { get; set; }

    /// <summary>Ordered list of stage definitions (sprk_stages - JSON array)</summary>
    public string? Stages { get; set; }

    /// <summary>Currently executing stage name (sprk_currentstage)</summary>
    public string? CurrentStage { get; set; }

    /// <summary>Stage completion tracking (sprk_stagestatus - JSON object)</summary>
    public string? StageStatus { get; set; }

    /// <summary>Overall progress percentage 0-100 (sprk_progress)</summary>
    public int? Progress { get; set; }

    /// <summary>When job began processing (sprk_starteddate)</summary>
    public DateTime? StartedDate { get; set; }

    /// <summary>When job finished - success or failure (sprk_completeddate)</summary>
    public DateTime? CompletedDate { get; set; }

    /// <summary>Error code if failed (sprk_errorcode, e.g., OFFICE_001)</summary>
    public string? ErrorCode { get; set; }

    /// <summary>Detailed error message (sprk_errormessage)</summary>
    public string? ErrorMessage { get; set; }

    /// <summary>Number of retry attempts (sprk_retrycount)</summary>
    public int? RetryCount { get; set; }

    /// <summary>SHA256 hash for duplicate job prevention (sprk_idempotencykey - indexed)</summary>
    public string? IdempotencyKey { get; set; }

    /// <summary>GUID for distributed tracing (sprk_correlationid)</summary>
    public string? CorrelationId { get; set; }

    /// <summary>User who initiated the job (sprk_initiatedby - lookup to systemuser).
    /// Maps to _sprk_initiatedby_value in Dataverse Web API.
    /// </summary>
    public Guid? InitiatedBy { get; set; }

    /// <summary>Target document (sprk_document - lookup to sprk_document).
    /// Maps to _sprk_document_value in Dataverse Web API.
    /// </summary>
    public Guid? DocumentId { get; set; }

    /// <summary>JSON input data for the job (sprk_payload - up to 50K chars)</summary>
    public string? Payload { get; set; }

    /// <summary>JSON output data from the job (sprk_result - up to 50K chars)</summary>
    public string? Result { get; set; }

    /// <summary>Created date/time (system field)</summary>
    public DateTime CreatedOn { get; set; }

    /// <summary>Modified date/time (system field)</summary>
    public DateTime ModifiedOn { get; set; }

    /// <summary>Owner ID (system field - _ownerid_value)</summary>
    public Guid? OwnerId { get; set; }
}

/// <summary>
/// Request model for creating a new ProcessingJob
/// </summary>
public class CreateProcessingJobRequest
{
    public required string Name { get; set; }
    public int? JobType { get; set; }
    public int Status { get; set; } = 0; // Default to Pending
    public string? Stages { get; set; }
    public string? CurrentStage { get; set; }
    public string? StageStatus { get; set; }
    public int? Progress { get; set; } = 0;
    public DateTime? StartedDate { get; set; }
    public string? IdempotencyKey { get; set; }
    public string? CorrelationId { get; set; }
    public Guid? InitiatedBy { get; set; }
    public Guid? DocumentId { get; set; }
    public string? Payload { get; set; }
}

/// <summary>
/// Request model for updating an existing ProcessingJob
/// </summary>
public class UpdateProcessingJobRequest
{
    public int? Status { get; set; }
    public string? CurrentStage { get; set; }
    public string? StageStatus { get; set; }
    public int? Progress { get; set; }
    public DateTime? StartedDate { get; set; }
    public DateTime? CompletedDate { get; set; }
    public string? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
    public int? RetryCount { get; set; }
    public string? Result { get; set; }
}

/// <summary>
/// Job type enumeration (matches sprk_jobtype choice values)
/// </summary>
public enum ProcessingJobType
{
    DocumentSave = 0,
    EmailSave = 1,
    ShareLinks = 2,
    QuickCreate = 3,
    ProfileSummary = 4,
    Indexing = 5,
    DeepAnalysis = 6
}

/// <summary>
/// Job status enumeration (matches sprk_status choice values)
/// </summary>
public enum ProcessingJobStatus
{
    Pending = 0,
    InProgress = 1,
    Completed = 2,
    Failed = 3,
    Cancelled = 4
}
