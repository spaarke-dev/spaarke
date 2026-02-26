using System.ComponentModel;
using Sprk.Bff.Api.Services.Ai;

namespace Sprk.Bff.Api.Services.Ai.Chat.Tools;

/// <summary>
/// AI tool class providing access to document analysis results for the SprkChatAgent.
///
/// Exposes two retrieval methods:
///   - <see cref="GetAnalysisResultAsync"/> — retrieves full analysis detail including chat history
///   - <see cref="GetAnalysisSummaryAsync"/> — retrieves an executive summary extracted from analysis output
///
/// Both methods call <see cref="IAnalysisOrchestrationService.GetAnalysisAsync"/> and project
/// the result to a compact text representation suitable for agent context injection.
/// The tenant ID is captured at construction time (not exposed as an LLM tool parameter)
/// to support future multi-tenant analysis store lookups per ADR-014.
///
/// Instantiated by <see cref="SprkChatAgentFactory"/>. Not registered in DI — the factory
/// creates instances and registers methods as <see cref="Microsoft.Extensions.AI.AIFunction"/>
/// objects via <see cref="Microsoft.Extensions.AI.AIFunctionFactory.Create"/>.
/// </summary>
public sealed class AnalysisQueryTools
{
    private readonly IAnalysisOrchestrationService _analysisService;
    private readonly string _tenantId;

    public AnalysisQueryTools(IAnalysisOrchestrationService analysisService, string tenantId)
    {
        _analysisService = analysisService ?? throw new ArgumentNullException(nameof(analysisService));
        _tenantId = tenantId ?? throw new ArgumentNullException(nameof(tenantId));
    }

    /// <summary>
    /// Retrieves the full analysis results for a specific document, including the working document,
    /// final output, chat history summary, and token usage.
    /// Use this when the user asks about a previous analysis or wants to refer to analysis findings.
    /// </summary>
    /// <param name="documentId">Get analysis results for a specific document</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Formatted analysis result string, or a message if not found.</returns>
    public async Task<string> GetAnalysisResultAsync(
        [Description("Get analysis results for a specific document")] string documentId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(documentId, nameof(documentId));

        if (!Guid.TryParse(documentId, out var analysisGuid))
        {
            return $"Invalid analysis ID format: '{documentId}'. Expected a GUID.";
        }

        try
        {
            var result = await _analysisService.GetAnalysisAsync(analysisGuid, cancellationToken);

            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"## Analysis Result: {result.DocumentName}");
            sb.AppendLine($"**Status**: {result.Status}");
            sb.AppendLine($"**Document ID**: {result.DocumentId}");

            if (result.StartedOn.HasValue)
            {
                sb.AppendLine($"**Analyzed**: {result.StartedOn.Value:yyyy-MM-dd HH:mm} UTC");
            }

            if (result.TokenUsage != null)
            {
                sb.AppendLine($"**Token Usage**: {result.TokenUsage.Input} input, {result.TokenUsage.Output} output");
            }

            sb.AppendLine();

            // Include working document (final analysis output)
            if (!string.IsNullOrWhiteSpace(result.WorkingDocument))
            {
                sb.AppendLine("### Analysis Output");
                sb.AppendLine(result.WorkingDocument);
            }
            else if (!string.IsNullOrWhiteSpace(result.FinalOutput))
            {
                sb.AppendLine("### Analysis Output");
                sb.AppendLine(result.FinalOutput);
            }
            else
            {
                sb.AppendLine("*No analysis output available.*");
            }

            return sb.ToString().TrimEnd();
        }
        catch (KeyNotFoundException)
        {
            return $"Analysis '{documentId}' not found. The analysis may not exist or may have been removed.";
        }
    }

    /// <summary>
    /// Retrieves the executive summary of a document analysis.
    /// Use this when the user wants a brief overview of what was found in a previous analysis,
    /// without the full detailed output.
    /// </summary>
    /// <param name="documentId">Get executive summary of document analysis</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Extracted executive summary, or a condensed first section of the analysis.</returns>
    public async Task<string> GetAnalysisSummaryAsync(
        [Description("Get executive summary of document analysis")] string documentId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(documentId, nameof(documentId));

        if (!Guid.TryParse(documentId, out var analysisGuid))
        {
            return $"Invalid analysis ID format: '{documentId}'. Expected a GUID.";
        }

        try
        {
            var result = await _analysisService.GetAnalysisAsync(analysisGuid, cancellationToken);

            var output = result.WorkingDocument ?? result.FinalOutput;

            if (string.IsNullOrWhiteSpace(output))
            {
                return $"No summary available for analysis '{documentId}'.";
            }

            // Try to extract an Executive Summary section
            var summaryMatch = System.Text.RegularExpressions.Regex.Match(
                output,
                @"(?:##?\s*(?:Executive\s+)?Summary[\s:]*\n)([\s\S]*?)(?=\n##|\z)",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);

            if (summaryMatch.Success)
            {
                var summaryText = summaryMatch.Groups[1].Value.Trim();
                return $"## Executive Summary: {result.DocumentName}\n\n{summaryText}";
            }

            // Fall back to first 600 characters of output
            var preview = output.Length > 600 ? output[..600] + "\n\n*(Summary truncated — use GetAnalysisResultAsync for full output)*" : output;
            return $"## Summary: {result.DocumentName}\n\n{preview}";
        }
        catch (KeyNotFoundException)
        {
            return $"Analysis '{documentId}' not found. The analysis may not exist or may have been removed.";
        }
    }
}
