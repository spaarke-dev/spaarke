namespace Sprk.Bff.Api.Models;

/// <summary>
/// AttachmentArtifact entity model (sprk_attachmentartifact).
/// Tracks email attachments saved as separate documents.
/// Created by: SDAP Office Integration project (tasks 010-012)
/// </summary>
public class AttachmentArtifact
{
    /// <summary>Attachment Artifact ID (sprk_attachmentartifactid - primary key)</summary>
    public Guid Id { get; set; }

    /// <summary>Attachment artifact name (sprk_name - primary name field, original filename)</summary>
    public required string Name { get; set; }

    /// <summary>Original filename from email (sprk_originalfilename - up to 260 chars, searchable)</summary>
    public string? OriginalFilename { get; set; }

    /// <summary>MIME type (sprk_contenttype - up to 100 chars, e.g., application/pdf)</summary>
    public string? ContentType { get; set; }

    /// <summary>File size in bytes (sprk_size)</summary>
    public int? Size { get; set; }

    /// <summary>Content ID for inline attachments (sprk_contentid - up to 256 chars)</summary>
    public string? ContentId { get; set; }

    /// <summary>True for embedded images in HTML (sprk_isinline)</summary>
    public bool? IsInline { get; set; }

    /// <summary>Parent email artifact ID (sprk_emailartifact - lookup to sprk_emailartifact).
    /// Maps to _sprk_emailartifact_value in Dataverse Web API.
    /// </summary>
    public Guid? EmailArtifactId { get; set; }

    /// <summary>Document ID for the saved file (sprk_document - lookup to sprk_document).
    /// Maps to _sprk_document_value in Dataverse Web API.
    /// </summary>
    public Guid? DocumentId { get; set; }

    /// <summary>Created date/time (system field)</summary>
    public DateTime CreatedOn { get; set; }

    /// <summary>Modified date/time (system field)</summary>
    public DateTime ModifiedOn { get; set; }

    /// <summary>Owner ID (system field - _ownerid_value)</summary>
    public Guid? OwnerId { get; set; }
}

/// <summary>
/// Request model for creating a new AttachmentArtifact
/// </summary>
public class CreateAttachmentArtifactRequest
{
    public required string Name { get; set; }
    public string? OriginalFilename { get; set; }
    public string? ContentType { get; set; }
    public int? Size { get; set; }
    public string? ContentId { get; set; }
    public bool? IsInline { get; set; }
    public Guid? EmailArtifactId { get; set; }
    public Guid? DocumentId { get; set; }
}

/// <summary>
/// Request model for updating an existing AttachmentArtifact
/// </summary>
public class UpdateAttachmentArtifactRequest
{
    public string? OriginalFilename { get; set; }
    public string? ContentType { get; set; }
    public int? Size { get; set; }
    public string? ContentId { get; set; }
    public bool? IsInline { get; set; }
    public Guid? EmailArtifactId { get; set; }
    public Guid? DocumentId { get; set; }
}
