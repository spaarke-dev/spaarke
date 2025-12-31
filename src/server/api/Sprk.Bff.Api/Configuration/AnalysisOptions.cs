using System.ComponentModel.DataAnnotations;

namespace Sprk.Bff.Api.Configuration;

/// <summary>
/// Configuration options for the Analysis feature including AI-driven document analysis,
/// Prompt Flow orchestration, and hybrid RAG deployment.
/// Supports multi-tenant deployment via Dataverse Environment Variables.
/// </summary>
public class AnalysisOptions
{
    public const string SectionName = "Analysis";

    // === Feature Flags ===

    /// <summary>
    /// Master switch to enable/disable the Analysis feature.
    /// When false, all analysis endpoints return 503 Service Unavailable.
    /// Maps to Dataverse Environment Variable: sprk_EnableAiFeatures
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Enable multi-document analysis (Phase 2 feature).
    /// When false, analysis is limited to single documents.
    /// Maps to Dataverse Environment Variable: sprk_EnableMultiDocumentAnalysis
    /// </summary>
    public bool MultiDocumentEnabled { get; set; } = false;

    // === Azure AI Foundry / Prompt Flow ===

    /// <summary>
    /// Azure AI Foundry Prompt Flow endpoint URL.
    /// Example: https://{project}.{region}.inference.ml.azure.com/score
    /// Optional: When empty, uses direct Azure OpenAI calls instead.
    /// Maps to Dataverse Environment Variable: sprk_PromptFlowEndpoint
    /// </summary>
    public string? PromptFlowEndpoint { get; set; }

    /// <summary>
    /// API key for Prompt Flow endpoint authentication.
    /// Store in Key Vault (production) or user-secrets (development).
    /// </summary>
    public string? PromptFlowKey { get; set; }

    /// <summary>
    /// Prompt Flow deployment name for analysis-execute flow.
    /// </summary>
    public string ExecuteFlowName { get; set; } = "analysis-execute";

    /// <summary>
    /// Prompt Flow deployment name for analysis-continue (chat) flow.
    /// </summary>
    public string ContinueFlowName { get; set; } = "analysis-continue";

    // === RAG Configuration ===

    /// <summary>
    /// Default RAG deployment model for new customers.
    /// Options: "Shared" (multi-tenant), "Dedicated" (customer index), "CustomerOwned" (BYOK)
    /// Maps to configuration for sprk_knowledgedeployment entity.
    /// </summary>
    public RagDeploymentModel DefaultRagModel { get; set; } = RagDeploymentModel.Shared;

    /// <summary>
    /// Index name for shared RAG deployment (Model 1).
    /// All customers share this index with tenant filtering.
    /// </summary>
    public string SharedIndexName { get; set; } = "spaarke-knowledge-shared";

    /// <summary>
    /// Tenant filter field in shared index.
    /// Used to isolate customer data in shared index.
    /// </summary>
    public string TenantFilterField { get; set; } = "customerId";

    /// <summary>
    /// Maximum number of knowledge results to retrieve per query.
    /// </summary>
    [Range(1, 20, ErrorMessage = "Analysis:MaxKnowledgeResults must be between 1 and 20")]
    public int MaxKnowledgeResults { get; set; } = 5;

    /// <summary>
    /// Minimum relevance score (0.0-1.0) for knowledge results.
    /// Results below this threshold are filtered out.
    /// </summary>
    [Range(0.0, 1.0, ErrorMessage = "Analysis:MinRelevanceScore must be between 0.0 and 1.0")]
    public float MinRelevanceScore { get; set; } = 0.7f;

    // === Working Document Management ===

    /// <summary>
    /// Maximum number of working versions to keep per analysis session.
    /// Older versions are automatically deleted.
    /// </summary>
    [Range(1, 50, ErrorMessage = "Analysis:MaxWorkingVersions must be between 1 and 50")]
    public int MaxWorkingVersions { get; set; } = 10;

    /// <summary>
    /// Session timeout in minutes.
    /// Working documents older than this are eligible for cleanup.
    /// </summary>
    [Range(5, 1440, ErrorMessage = "Analysis:SessionTimeoutMinutes must be between 5 and 1440")]
    public int SessionTimeoutMinutes { get; set; } = 60;

    /// <summary>
    /// SPE folder path for storing working documents.
    /// Pattern: analysis/{userId}/{analysisId}/
    /// </summary>
    public string WorkingDocumentFolder { get; set; } = "analysis";

    // === Chat Settings ===

    /// <summary>
    /// Maximum chat history messages to include in context.
    /// Older messages are truncated to fit token limits.
    /// </summary>
    [Range(1, 50, ErrorMessage = "Analysis:MaxChatHistoryMessages must be between 1 and 50")]
    public int MaxChatHistoryMessages { get; set; } = 20;

    /// <summary>
    /// Maximum input tokens for chat refinement requests.
    /// Includes working document + history + user message.
    /// </summary>
    public int MaxChatInputTokens { get; set; } = 50_000;

    // === Export Settings ===

    /// <summary>
    /// Enable export to DOCX format.
    /// </summary>
    public bool EnableDocxExport { get; set; } = true;

    /// <summary>
    /// Enable export to PDF format.
    /// Uses QuestPDF for in-process PDF generation (ADR-001 compliant).
    /// </summary>
    public bool EnablePdfExport { get; set; } = true;

    /// <summary>
    /// Enable email integration via Power Apps email entity.
    /// </summary>
    public bool EnableEmailExport { get; set; } = true;

    /// <summary>
    /// Enable Teams integration via Graph API.
    /// </summary>
    public bool EnableTeamsExport { get; set; } = false;

    // === Performance Settings ===

    /// <summary>
    /// Maximum concurrent analysis streams per user.
    /// Prevents resource exhaustion.
    /// </summary>
    [Range(1, 10, ErrorMessage = "Analysis:MaxConcurrentStreams must be between 1 and 10")]
    public int MaxConcurrentStreams { get; set; } = 3;

    /// <summary>
    /// Streaming chunk delay in milliseconds.
    /// Adds slight delay between SSE chunks for smoother UX.
    /// </summary>
    [Range(0, 100, ErrorMessage = "Analysis:StreamChunkDelayMs must be between 0 and 100")]
    public int StreamChunkDelayMs { get; set; } = 10;

    // === Multi-Tenant Settings ===

    /// <summary>
    /// Deployment environment name.
    /// Used for logging, telemetry, and environment-specific behavior.
    /// Maps to Dataverse Environment Variable: sprk_DeploymentEnvironment
    /// </summary>
    public string DeploymentEnvironment { get; set; } = "Development";

    /// <summary>
    /// Customer tenant ID for cross-tenant scenarios.
    /// Required for CustomerOwned RAG deployment model.
    /// Maps to Dataverse Environment Variable: sprk_CustomerTenantId
    /// </summary>
    public string? CustomerTenantId { get; set; }

    /// <summary>
    /// Key Vault URL for secret resolution.
    /// Used by BFF API to retrieve secrets at runtime.
    /// Maps to Dataverse Environment Variable: sprk_KeyVaultUrl
    /// </summary>
    public string? KeyVaultUrl { get; set; }
}

/// <summary>
/// RAG deployment models for knowledge retrieval.
/// </summary>
public enum RagDeploymentModel
{
    /// <summary>
    /// Shared index with tenant filtering (Model 1).
    /// Cost-effective for small to mid-size customers.
    /// </summary>
    Shared,

    /// <summary>
    /// Dedicated index per customer in Spaarke tenant (Model 2).
    /// Better isolation and performance.
    /// </summary>
    Dedicated,

    /// <summary>
    /// Customer-owned index in customer's Azure tenant (Model 3).
    /// Full data sovereignty and compliance.
    /// </summary>
    CustomerOwned
}
