using Azure.Core;
using Microsoft.Extensions.Options;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Infrastructure.Storage;

namespace Sprk.Bff.Api.Services.Intake.Engine;

/// <summary>
/// The stamp-private staging container the intake engine job reads its inputs from and writes its outputs to
/// (spaarke-intake-pipeline-r1 task 014, FR-07, NFR-03; ADR-004 claim-check, ADR-005 flat deterministic paths): one
/// managed-identity <see cref="BlobGateway"/> over the configured endpoint and container. Document bytes travel by blob only,
/// never in a message, and outputs are copied into SharePoint Embedded by the harness (task 015), never served from here.
/// </summary>
/// <remarks>
/// <para>Every name is built by <see cref="IntakeEngineDispatchId"/> from a run id; this class takes a blob name from no message.
/// Staged inputs are written with <c>If-None-Match: *</c>, so a repeat dispatch never rewrites them.</para>
/// <para>Not configured (an empty <see cref="IntakeEngineOptions.StagingBlobEndpoint"/>) is a state, not a DI failure: the gateway
/// is built on first use, so a stamp without an engine starts normally and its dispatch holds the run.</para>
/// <para>§11: <i>Existing</i> - <c>BlobGateway</c> (reused as is) and <c>StoredFileBlobs</c> (the Finance file store: tenant-scoped,
/// scanning and an environment check this store does not need). <i>Extension</i> - a second store over the same gateway keeps the
/// engine's path contract in one file; the Finance store's rules do not apply to an engine's staging. <i>Cost of doing nothing</i> -
/// the dispatcher and the harness would each hand-build the engine's paths and blob calls.</para>
/// </remarks>
public class IntakeEngineStaging
{
    internal const string DocxContentType = "application/vnd.openxmlformats-officedocument.wordprocessingml.document";
    internal const string JsonContentType = "application/json";

    private static readonly IReadOnlyDictionary<string, string> NoMetadata = new Dictionary<string, string>(StringComparer.Ordinal);

    private readonly IOptionsMonitor<IntakePipelineOptions>? _options;
    private readonly TokenCredential? _credential;
    private readonly ILogger<IntakeEngineStaging> _logger;
    private readonly object _gate = new();
    private BlobGateway? _gateway;

    /// <summary>Production constructor: the bound options and the shared managed-identity credential.</summary>
    public IntakeEngineStaging(IOptionsMonitor<IntakePipelineOptions> options, TokenCredential credential, ILogger<IntakeEngineStaging> logger)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _credential = credential ?? throw new ArgumentNullException(nameof(credential));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>Test constructor: a substitute gateway over <paramref name="container"/>.</summary>
    internal IntakeEngineStaging(BlobGateway gateway, string container, ILogger<IntakeEngineStaging> logger)
    {
        _gateway = gateway ?? throw new ArgumentNullException(nameof(gateway));
        Container = string.IsNullOrWhiteSpace(container) ? throw new ArgumentException("A container is required.", nameof(container)) : container;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>The staging container name.</summary>
    public string Container { get; } = string.Empty;

    /// <summary>True when an engine is configured on this stamp (a staging endpoint is set, or a test gateway was supplied).</summary>
    public virtual bool IsConfigured => _gateway is not null || (_options?.CurrentValue.Engine.IsConfigured ?? false);

    /// <summary>
    /// Stages <paramref name="open"/>'s content at <paramref name="blobName"/> unless a blob of that name exists: true when it was
    /// written, false when it was already there (content is then neither opened nor compared: the name is the run's own).
    /// </summary>
    public virtual async Task<bool> StageIfAbsentAsync(
        string blobName, Func<CancellationToken, Task<Stream>> open, string contentType, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(blobName);
        ArgumentNullException.ThrowIfNull(open);
        var gateway = Gateway(out var container);
        if (await gateway.GetPropertiesAsync(container, blobName, ct).ConfigureAwait(false) is not null)
        {
            return false;
        }

        var content = await open(ct).ConfigureAwait(false);
        await using (content.ConfigureAwait(false))
        {
            var created = await gateway.CreateIfNotExistsAsync(container, blobName, content, contentType, NoMetadata, ct).ConfigureAwait(false);
            return created.Outcome == BlobCreateOutcome.Created;
        }
    }

    /// <summary>True when the blob exists.</summary>
    public virtual async Task<bool> ExistsAsync(string blobName, CancellationToken ct)
    {
        var gateway = Gateway(out var container);
        return await gateway.GetPropertiesAsync(container, blobName, ct).ConfigureAwait(false) is not null;
    }

    /// <summary>Writes a small blob only when none of that name exists (the dispatch marker): true when this call wrote it.</summary>
    public virtual async Task<bool> CreateIfAbsentAsync(string blobName, BinaryData content, string contentType, CancellationToken ct)
    {
        var gateway = Gateway(out var container);
        using var stream = content.ToStream();
        var created = await gateway.CreateIfNotExistsAsync(container, blobName, stream, contentType, NoMetadata, ct).ConfigureAwait(false);
        return created.Outcome == BlobCreateOutcome.Created;
    }

    /// <summary>
    /// One output of a dispatch (task 015 reads <c>clauses.json</c>, <c>check-report.json</c>, <c>refused-report.json</c> and the
    /// .docx files through this): null when the engine wrote none. The name is derived from the dispatch id, never from a message.
    /// </summary>
    public virtual async Task<BinaryData?> ReadOutputAsync(IntakeEngineDispatchId dispatch, string name, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (!dispatch.IsValid || name.Contains('/') || name.Contains('\\') || name.Contains(".."))
        {
            throw new ArgumentException("An output is a plain file name under a valid dispatch.", nameof(name));
        }

        var gateway = Gateway(out var container);
        var blob = await gateway.DownloadContentAsync(container, dispatch.Output(name), ct).ConfigureAwait(false);
        return blob?.Content;
    }

    private BlobGateway Gateway(out string container)
    {
        if (_gateway is { } existing)
        {
            container = Container.Length > 0 ? Container : _options!.CurrentValue.Engine.StagingContainer;
            return existing;
        }

        lock (_gate)
        {
            var engine = _options!.CurrentValue.Engine;
            if (!engine.IsConfigured)
            {
                throw new InvalidOperationException("The intake engine staging endpoint is not configured (IntakePipeline:Engine:StagingBlobEndpoint).");
            }

            var endpoint = BlobEndpointRules.ParseManagedIdentityEndpoint(engine.StagingBlobEndpoint, "IntakePipeline:Engine:StagingBlobEndpoint");
            _gateway = new AzureBlobGateway(endpoint, _credential!);
            _logger.LogInformation("Intake engine staging uses container {Container}", engine.StagingContainer);
            container = engine.StagingContainer;
            return _gateway;
        }
    }
}
