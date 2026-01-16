namespace Sprk.Bff.Api.Configuration;

/// <summary>
/// Configuration options for email-to-document processing.
/// Loaded from appsettings.json "EmailProcessing" section.
/// </summary>
public class EmailProcessingOptions
{
    /// <summary>
    /// Configuration section name.
    /// </summary>
    public const string SectionName = "EmailProcessing";

    /// <summary>
    /// Whether automatic email processing is enabled.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// SPE container ID where email documents are stored.
    /// </summary>
    public string? DefaultContainerId { get; set; }

    /// <summary>
    /// Whether to process inbound (received) emails.
    /// </summary>
    public bool ProcessInbound { get; set; } = true;

    /// <summary>
    /// Whether to process outbound (sent) emails.
    /// </summary>
    public bool ProcessOutbound { get; set; } = true;

    /// <summary>
    /// Maximum size per attachment in megabytes.
    /// Attachments larger than this are skipped.
    /// </summary>
    public int MaxAttachmentSizeMB { get; set; } = 25;

    /// <summary>
    /// Maximum total email size (including attachments) in megabytes.
    /// </summary>
    public int MaxTotalSizeMB { get; set; } = 100;

    /// <summary>
    /// File extensions that are blocked from processing.
    /// Emails with these attachment types will have attachments stripped.
    /// </summary>
    public List<string> BlockedAttachmentExtensions { get; set; } =
    [
        ".exe", ".dll", ".bat", ".ps1", ".vbs", ".js", ".cmd", ".com", ".scr"
    ];

    /// <summary>
    /// TTL in minutes for filter rule cache in Redis.
    /// </summary>
    public int FilterRuleCacheTtlMinutes { get; set; } = 5;

    /// <summary>
    /// Default action when no filter rules match.
    /// Options: AutoSave, Ignore, ReviewRequired
    /// </summary>
    public string DefaultAction { get; set; } = "Ignore";

    /// <summary>
    /// Whether to enable webhook-based email processing.
    /// </summary>
    public bool EnableWebhook { get; set; } = true;

    /// <summary>
    /// Whether to enable polling-based email processing (backup).
    /// </summary>
    public bool EnablePolling { get; set; } = true;

    /// <summary>
    /// Polling interval in minutes for backup processing.
    /// </summary>
    public int PollingIntervalMinutes { get; set; } = 5;

    /// <summary>
    /// How far back in hours to look for unprocessed emails.
    /// Default is 24 hours.
    /// </summary>
    public int PollingLookbackHours { get; set; } = 24;

    /// <summary>
    /// Maximum number of emails to process per polling cycle.
    /// Prevents overwhelming the system with a large backlog.
    /// </summary>
    public int PollingBatchSize { get; set; } = 100;

    /// <summary>
    /// Filename patterns that indicate a signature image (to be skipped).
    /// Regex patterns.
    /// </summary>
    public List<string> SignatureImagePatterns { get; set; } =
    [
        @"^image\d{3}\.(png|gif|jpg|jpeg)$",
        @"^spacer\.(gif|png)$",
        @"^logo.*\.(png|gif|jpg|jpeg)$",
        @"^signature.*\.(png|gif|jpg|jpeg)$"
    ];

    /// <summary>
    /// Minimum file size in KB for images to be considered meaningful.
    /// Images smaller than this are likely signature/spacer images.
    /// </summary>
    public int MinImageSizeKB { get; set; } = 5;

    /// <summary>
    /// Whether to filter out calendar attachments (.ics, .vcs).
    /// Default: true (exclude calendar files from document creation).
    /// </summary>
    public bool FilterCalendarFiles { get; set; } = true;

    /// <summary>
    /// Calendar file extensions to filter when FilterCalendarFiles is true.
    /// </summary>
    public List<string> CalendarFileExtensions { get; set; } =
    [
        ".ics", ".vcs"
    ];

    /// <summary>
    /// Whether to filter inline attachments (embedded in HTML body).
    /// When true, inline images are excluded from document creation.
    /// </summary>
    public bool FilterInlineAttachments { get; set; } = true;

    /// <summary>
    /// Known tracking pixel patterns (domains or filenames).
    /// These are filtered out regardless of size.
    /// </summary>
    public List<string> TrackingPixelPatterns { get; set; } =
    [
        @"\.gif\?.*tracking",
        @"pixel\.(gif|png)",
        @"^spacer\d*\.(gif|png)$",
        @"beacon\.(gif|png)",
        @"^1x1\.(gif|png)$",
        @"tracker\.(gif|png)",
        @"open\.(gif|png)"
    ];

    /// <summary>
    /// Maximum filename length for generated .eml files.
    /// </summary>
    public int MaxEmlFileNameLength { get; set; } = 100;

    /// <summary>
    /// Whether to automatically queue documents for AI processing.
    /// </summary>
    public bool AutoEnqueueAi { get; set; } = true;

    /// <summary>
    /// Whether to automatically index email documents to RAG knowledge base.
    /// When enabled, processed emails are queued for RAG indexing after document creation.
    /// Default is false for safety - RAG indexing is opt-in.
    /// </summary>
    public bool AutoIndexToRag { get; set; } = false;

    /// <summary>
    /// Shared secret for Dataverse webhook validation.
    /// Must match the secret configured in Dataverse Service Endpoint.
    /// </summary>
    public string? WebhookSecret { get; set; }

    /// <summary>
    /// Maximum concurrent emails to process in a batch job.
    /// Controls bounded concurrency to prevent throttling.
    /// Default: 5
    /// </summary>
    public int BatchMaxConcurrency { get; set; } = 5;

    /// <summary>
    /// Batch size for batch processing - how many emails to fetch per query.
    /// Default: 50
    /// </summary>
    public int BatchProcessingBatchSize { get; set; } = 50;

    /// <summary>
    /// Computed max attachment size in bytes.
    /// </summary>
    public long MaxAttachmentSizeBytes => MaxAttachmentSizeMB * 1024L * 1024L;

    /// <summary>
    /// Computed max total size in bytes.
    /// </summary>
    public long MaxTotalSizeBytes => MaxTotalSizeMB * 1024L * 1024L;
}
