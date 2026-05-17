using Microsoft.Extensions.Hosting;

namespace Sprk.Bff.Api.Services.Ai.Capabilities;

/// <summary>
/// Hosted service that loads the <see cref="CapabilityManifest"/> from Dataverse
/// during BFF startup, before the HTTP pipeline begins serving AI requests.
///
/// Lifecycle:
///   StartAsync is called by the .NET host after all singletons are resolved but
///   before the HTTP pipeline opens. If Dataverse is unreachable and the manifest
///   is empty, startup fails fast (InvalidOperationException propagates to the host).
///
/// Startup failure policy:
///   The manifest is foundational — capability routing, tool gating, and playbook
///   selection all depend on it. A BFF that starts without a manifest would silently
///   grant or deny capabilities incorrectly. Fast-fail is the correct policy.
///
/// ADR-001: Uses <see cref="IHostedService"/> (no Azure Functions).
/// ADR-010: Not registered as a factory-instantiated object — uses hosted service
///          because it must run before the first HTTP request.
/// </summary>
public sealed class CapabilityManifestInitializer : IHostedService
{
    private readonly CapabilityManifest _manifest;
    private readonly ICapabilityManifestLoader _loader;
    private readonly ILogger<CapabilityManifestInitializer> _logger;

    public CapabilityManifestInitializer(
        CapabilityManifest manifest,
        ICapabilityManifestLoader loader,
        ILogger<CapabilityManifestInitializer> logger)
    {
        _manifest = manifest;
        _loader = loader;
        _logger = logger;
    }

    /// <summary>
    /// Loads capability entries from Dataverse and populates the singleton manifest.
    /// Throws <see cref="InvalidOperationException"/> when Dataverse is unreachable,
    /// causing the host to abort startup.
    /// </summary>
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("CapabilityManifestInitializer: loading capability manifest from Dataverse");

        try
        {
            var entries = await _loader.LoadAsync(cancellationToken);
            _manifest.Refresh(entries);

            _logger.LogInformation(
                "CapabilityManifestInitializer: manifest populated with {Count} enabled capabilities",
                _manifest.GetAll().Count);
        }
        catch (Exception ex)
        {
            _logger.LogCritical(ex,
                "CapabilityManifestInitializer: failed to load capability manifest — " +
                "BFF cannot start without this data. Check Dataverse connectivity and configuration.");
            throw;
        }
    }

    /// <summary>
    /// No-op: the manifest is stateless with respect to shutdown.
    /// </summary>
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
