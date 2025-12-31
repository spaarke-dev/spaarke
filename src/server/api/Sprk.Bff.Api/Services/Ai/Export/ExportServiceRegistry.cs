using Sprk.Bff.Api.Models.Ai;

namespace Sprk.Bff.Api.Services.Ai.Export;

/// <summary>
/// Registry for resolving export services by format.
/// Follows the same pattern as ToolHandlerRegistry for consistency.
/// </summary>
public class ExportServiceRegistry
{
    private readonly IReadOnlyDictionary<ExportFormat, IExportService> _services;

    public ExportServiceRegistry(IEnumerable<IExportService> services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // Use last registered service for each format (allows overriding)
        var dict = new Dictionary<ExportFormat, IExportService>();
        foreach (var service in services)
        {
            dict[service.Format] = service;
        }
        _services = dict;
    }

    /// <summary>
    /// Gets the export service for the specified format.
    /// </summary>
    /// <param name="format">Export format.</param>
    /// <returns>Export service or null if not found.</returns>
    public IExportService? GetService(ExportFormat format)
        => _services.TryGetValue(format, out var service) ? service : null;

    /// <summary>
    /// Gets all registered export formats.
    /// </summary>
    public IEnumerable<ExportFormat> SupportedFormats => _services.Keys;

    /// <summary>
    /// Checks if a format is supported.
    /// </summary>
    public bool IsSupported(ExportFormat format)
        => _services.ContainsKey(format);
}
