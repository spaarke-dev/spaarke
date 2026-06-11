using Microsoft.Extensions.Options;
using Sprk.Bff.Api.Configuration;

namespace Sprk.Bff.Api.Infrastructure.DI;

/// <summary>
/// Startup diagnostics for verifying critical service dependencies at application start.
/// </summary>
public static class StartupDiagnostics
{
    /// <summary>
    /// Logs registered job handlers and tests critical service dependencies for troubleshooting.
    /// </summary>
    public static void RunStartupDiagnostics(this WebApplication app)
    {
        using var startupScope = app.Services.CreateScope();
        var startupLogger = startupScope.ServiceProvider.GetRequiredService<ILogger<Program>>();

        startupLogger.LogInformation("\ud83d\udd0d Testing critical service dependencies...");

        // multi-container-multi-index-r1 (FR-BFF-06, task 012): emit the AiSearch allow-list
        // at INFO level so operators can verify the deployed per-environment config without
        // inspecting source. Empty allow-list is logged as "(none)" to make misconfiguration
        // visible. The resolver consumes this list to reject unknown indexName values with
        // INDEX_NOT_ALLOWED (ADR-019 + NFR-08).
        try
        {
            var aiSearchOptions = startupScope.ServiceProvider.GetRequiredService<IOptions<AiSearchOptions>>().Value;
            var allowed = aiSearchOptions.AllowedIndexes;
            startupLogger.LogInformation(
                "AiSearch allow-list: {Count} indexes \u2014 {Indexes}",
                allowed?.Length ?? 0,
                allowed is { Length: > 0 } a ? string.Join(", ", a) : "(none)");
        }
        catch (Exception ex)
        {
            startupLogger.LogError(ex,
                "Failed to resolve AiSearchOptions for allow-list startup log: {Error}",
                ex.Message);
        }

        // Test IToolHandlerRegistry
        try
        {
            var toolRegistry = startupScope.ServiceProvider.GetService<Sprk.Bff.Api.Services.Ai.IToolHandlerRegistry>();
            startupLogger.LogInformation("  \u2705 IToolHandlerRegistry: {Status}", toolRegistry != null ? "Available" : "NULL");
        }
        catch (Exception ex)
        {
            startupLogger.LogError(ex, "  \u274c IToolHandlerRegistry resolution failed: {Error}", ex.Message);
        }

        // Test IAppOnlyAnalysisService
        try
        {
            var analysisService = startupScope.ServiceProvider.GetService<Sprk.Bff.Api.Services.Ai.IAppOnlyAnalysisService>();
            startupLogger.LogInformation("  \u2705 IAppOnlyAnalysisService: {Status}", analysisService != null ? "Available" : "NULL");
        }
        catch (Exception ex)
        {
            startupLogger.LogError(ex, "  \u274c IAppOnlyAnalysisService resolution failed: {Error}", ex.Message);
        }

        // Test AppOnlyDocumentAnalysisJobHandler
        try
        {
            var handler = startupScope.ServiceProvider.GetService<Sprk.Bff.Api.Services.Ai.Jobs.AppOnlyDocumentAnalysisJobHandler>();
            startupLogger.LogInformation("  \u2705 AppOnlyDocumentAnalysisJobHandler: {Status}", handler != null ? "Available" : "NULL");
        }
        catch (Exception ex)
        {
            startupLogger.LogError(ex, "  \u274c AppOnlyDocumentAnalysisJobHandler resolution failed: {Error}", ex.Message);
        }

        // Enumerate all handlers
        try
        {
            var handlers = startupScope.ServiceProvider.GetServices<Sprk.Bff.Api.Services.Jobs.IJobHandler>().ToList();
            var handlerTypes = string.Join(", ", handlers.Select(h => h.JobType));
            startupLogger.LogInformation(
                "\ud83d\udccb Job handlers registered: {Count} handlers available: [{HandlerTypes}]",
                handlers.Count, handlerTypes);

            var expectedHandlers = new[] { "AppOnlyDocumentAnalysis", "DocumentProcessing", "ProcessEmailToDocument", "ProfileSummary", "RagIndexing", "BatchProcessEmails", "BulkRagIndexing", "EmailAnalysis" };
            var missingHandlers = expectedHandlers.Where(e => !handlers.Any(h => h.JobType == e)).ToList();
            if (missingHandlers.Any())
            {
                startupLogger.LogWarning(
                    "\u26a0\ufe0f Missing expected job handlers: [{MissingHandlers}]. Check DI registration and dependency chain.",
                    string.Join(", ", missingHandlers));
            }
        }
        catch (Exception ex)
        {
            startupLogger.LogError(ex,
                "\u274c Failed to enumerate job handlers at startup. This may cause 'NoHandler' dead-letter errors. Error: {Error}",
                ex.Message);

            var inner = ex.InnerException;
            while (inner != null)
            {
                startupLogger.LogError("  \u2192 Inner: {InnerError}", inner.Message);
                inner = inner.InnerException;
            }
        }
    }
}
