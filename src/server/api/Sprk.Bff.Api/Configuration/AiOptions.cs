using System.ComponentModel.DataAnnotations;

namespace Sprk.Bff.Api.Configuration;

/// <summary>
/// Configuration options for AI services including Azure OpenAI and Document Intelligence.
/// Supports both Spaarke-hosted (Model 1) and Customer-hosted BYOK (Model 2) deployments.
/// Customers using BYOK can manage their Azure OpenAI resources via Microsoft Foundry (ai.azure.com).
/// </summary>
public class AiOptions
{
    public const string SectionName = "Ai";

    // === Feature Flags ===

    /// <summary>
    /// Master switch to enable/disable AI summarization.
    /// When false, all AI endpoints return 503 Service Unavailable.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Enable streaming (SSE) responses. If false, only background jobs available.
    /// </summary>
    public bool StreamingEnabled { get; set; } = true;

    // === Azure OpenAI Settings ===

    /// <summary>
    /// Azure OpenAI endpoint URL.
    /// Example: https://{resource}.openai.azure.com/
    /// Store in Key Vault for production.
    /// Required when Enabled=true (validated by AiOptionsValidator).
    /// </summary>
    public string OpenAiEndpoint { get; set; } = string.Empty;

    /// <summary>
    /// Azure OpenAI API key.
    /// Store in Key Vault (production) or user-secrets (development).
    /// Required when Enabled=true (validated by AiOptionsValidator).
    /// </summary>
    public string OpenAiKey { get; set; } = string.Empty;

    // === Model Configuration ===

    /// <summary>
    /// Model deployment name for text summarization.
    /// Options: "gpt-4o-mini" (fast, cheap), "gpt-4o" (better quality), "gpt-4" (highest quality)
    /// Note: Model names should match Azure OpenAI deployment names in Microsoft Foundry portal.
    /// </summary>
    public string SummarizeModel { get; set; } = "gpt-4o-mini";

    /// <summary>
    /// Model deployment name for image/vision summarization (Phase 2).
    /// Requires multimodal model like gpt-4o or gpt-4-vision.
    /// </summary>
    public string? ImageSummarizeModel { get; set; }

    /// <summary>
    /// Max tokens for summary output. Higher = longer summaries.
    /// Range: 100-4000. Default: 1000 (~750 words)
    /// </summary>
    [Range(100, 4000, ErrorMessage = "Ai:MaxOutputTokens must be between 100 and 4000")]
    public int MaxOutputTokens { get; set; } = 1000;

    /// <summary>
    /// Temperature for generation. Lower = more deterministic.
    /// Range: 0.0-1.0. Default: 0.3
    /// </summary>
    [Range(0.0, 1.0, ErrorMessage = "Ai:Temperature must be between 0.0 and 1.0")]
    public float Temperature { get; set; } = 0.3f;

    // === Document Intelligence Settings ===

    /// <summary>
    /// Azure Document Intelligence endpoint URL.
    /// Example: https://{resource}.cognitiveservices.azure.com/
    /// Required for PDF/DOCX extraction. Optional if only using native text files.
    /// </summary>
    public string? DocIntelEndpoint { get; set; }

    /// <summary>
    /// Azure Document Intelligence API key.
    /// Store in Key Vault (production) or user-secrets (development).
    /// </summary>
    public string? DocIntelKey { get; set; }

    // === File Type Configuration ===

    /// <summary>
    /// Enabled file extensions for summarization.
    /// Each extension maps to its extraction method.
    /// Allows enabling/disabling specific file types without code changes.
    /// </summary>
    public Dictionary<string, FileTypeConfig> SupportedFileTypes { get; set; } = new()
    {
        // Native text extraction (direct read)
        [".txt"] = new() { Enabled = true, Method = ExtractionMethod.Native },
        [".md"] = new() { Enabled = true, Method = ExtractionMethod.Native },
        [".json"] = new() { Enabled = true, Method = ExtractionMethod.Native },
        [".csv"] = new() { Enabled = true, Method = ExtractionMethod.Native },
        [".xml"] = new() { Enabled = true, Method = ExtractionMethod.Native },
        [".html"] = new() { Enabled = true, Method = ExtractionMethod.Native },

        // Document Intelligence extraction
        [".pdf"] = new() { Enabled = true, Method = ExtractionMethod.DocumentIntelligence },
        [".docx"] = new() { Enabled = true, Method = ExtractionMethod.DocumentIntelligence },
        [".doc"] = new() { Enabled = true, Method = ExtractionMethod.DocumentIntelligence },

        // Image Vision (requires ImageSummarizeModel to be configured)
        [".png"] = new() { Enabled = true, Method = ExtractionMethod.VisionOcr },
        [".jpg"] = new() { Enabled = true, Method = ExtractionMethod.VisionOcr },
        [".jpeg"] = new() { Enabled = true, Method = ExtractionMethod.VisionOcr },
        [".gif"] = new() { Enabled = true, Method = ExtractionMethod.VisionOcr },
        [".tiff"] = new() { Enabled = true, Method = ExtractionMethod.VisionOcr },
        [".bmp"] = new() { Enabled = true, Method = ExtractionMethod.VisionOcr },
        [".webp"] = new() { Enabled = true, Method = ExtractionMethod.VisionOcr },
    };

    // === Processing Limits ===

    /// <summary>
    /// Maximum file size in bytes for summarization.
    /// Default: 10MB. Files larger than this are rejected.
    /// </summary>
    public int MaxFileSizeBytes { get; set; } = 10 * 1024 * 1024;

    /// <summary>
    /// Maximum input tokens to send to the model.
    /// Documents exceeding this are truncated with a notice.
    /// Default: 100,000 (~75K words)
    /// </summary>
    public int MaxInputTokens { get; set; } = 100_000;

    /// <summary>
    /// Maximum concurrent SSE streams per user.
    /// Prevents resource exhaustion from multi-file uploads.
    /// Default: 3
    /// </summary>
    [Range(1, 10, ErrorMessage = "Ai:MaxConcurrentStreams must be between 1 and 10")]
    public int MaxConcurrentStreams { get; set; } = 3;

    // === Prompt Templates ===

    /// <summary>
    /// Prompt template for document summarization.
    /// Use {documentText} placeholder for the extracted document text.
    /// </summary>
    public string SummarizePromptTemplate { get; set; } = """
        You are a document summarization assistant. Generate a clear, concise summary of the following document. The summary should:
        - Be 2-4 paragraphs (approximately 200-400 words)
        - Capture the main points and key information
        - Be written in professional business language
        - Not start with "This document" or "The document"

        Document:
        {documentText}

        Summary:
        """;

    /// <summary>
    /// Prompt template for image/vision summarization.
    /// Used with GPT-4 Vision for analyzing image content directly.
    /// </summary>
    public string VisionPromptTemplate { get; set; } = """
        Analyze this image and provide a comprehensive summary. Your summary should:
        - Be 2-4 paragraphs (approximately 200-400 words)
        - If the image contains text, extract and summarize the key points
        - If it contains charts, diagrams, or visual information, describe what is shown and any insights
        - Be written in professional business language
        - Focus on the most important information visible in the image
        """;

    // === Helper Methods ===

    /// <summary>
    /// Check if a file extension is supported for summarization.
    /// </summary>
    public bool IsFileTypeSupported(string extension)
    {
        var ext = extension.StartsWith('.') ? extension : $".{extension}";
        return SupportedFileTypes.TryGetValue(ext.ToLowerInvariant(), out var config)
               && config.Enabled;
    }

    /// <summary>
    /// Get the extraction method for a file extension.
    /// Returns null if extension is not supported or disabled.
    /// </summary>
    public ExtractionMethod? GetExtractionMethod(string extension)
    {
        var ext = extension.StartsWith('.') ? extension : $".{extension}";
        if (SupportedFileTypes.TryGetValue(ext.ToLowerInvariant(), out var config) && config.Enabled)
        {
            return config.Method;
        }
        return null;
    }
}

/// <summary>
/// Configuration for a specific file type's summarization support.
/// </summary>
public class FileTypeConfig
{
    /// <summary>
    /// Whether this file type is enabled for summarization.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// The extraction method to use for this file type.
    /// </summary>
    public ExtractionMethod Method { get; set; }
}

/// <summary>
/// Methods for extracting text from files before summarization.
/// </summary>
public enum ExtractionMethod
{
    /// <summary>
    /// Direct text read for plain text files (TXT, MD, JSON, CSV, etc.)
    /// </summary>
    Native,

    /// <summary>
    /// Azure Document Intelligence for PDFs and Office documents.
    /// </summary>
    DocumentIntelligence,

    /// <summary>
    /// Azure Vision / GPT-4 Vision for images (Phase 2).
    /// Requires multimodal model configuration.
    /// </summary>
    VisionOcr
}
