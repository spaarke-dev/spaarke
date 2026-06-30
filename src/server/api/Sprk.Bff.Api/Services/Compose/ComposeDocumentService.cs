using System.Diagnostics;
using System.Security.Claims;
using Microsoft.Graph;
using Sprk.Bff.Api.Infrastructure.Graph;

namespace Sprk.Bff.Api.Services.Compose;

/// <summary>
/// Compose SPE plumbing — load + save DOCX bytes via Microsoft Graph against a SPE drive-item.
/// Isolates the Graph SDK at the boundary so <see cref="IComposeDocumentService"/> consumers
/// (the higher-level <c>ComposeService</c> per task 021, the <c>ComposeEndpoints</c> per task 024)
/// stay testable per ADR-038 (mock-at-boundary, no <c>Mock&lt;HttpMessageHandler&gt;</c>).
/// </summary>
/// <remarks>
/// <para>
/// <b>Dependencies (per CLAUDE.md §11 reuse-default + ADR-013 refined)</b>: only
/// <see cref="IGraphClientFactory"/> + <see cref="ILogger{T}"/>. No AI-internal injections
/// (no <c>IOpenAiClient</c>, no <c>IPlaybookService</c>). No HTTP transport mocking surface.
/// </para>
/// <para>
/// <b>Writer-identity rule</b>: every Graph call goes through
/// <see cref="IGraphClientFactory.ForUserAsync"/> (OBO). In R1 both load and save are user-initiated
/// (user opens the editor; user clicks Save) — so OBO is the only path. See
/// <c>.claude/patterns/auth/spe-writer-identity-matching.md</c>. App-only Graph (<c>ForApp</c>) is
/// deliberately NOT used by this service in R1 and would be a write-identity-mismatch failure mode
/// for any save path.
/// </para>
/// <para>
/// <b>Check-out methods</b>: per spike #3 §2.4 (locked decision, post-Wave-0 Path A approval), R1
/// reuses the existing <c>DocumentCheckoutService</c> Dataverse-side lock substrate rather than
/// building a parallel SPE-native <c>checkOut</c>/<c>checkIn</c> wrapper. The interface members
/// here throw <see cref="NotImplementedException"/> in R1; Phase 5 task 050 wires the React layer
/// directly to the existing <c>/api/documents/{id}/checkout</c> + <c>/checkin</c> + <c>/discard</c>
/// endpoints. If a Phase 5 design later prefers an in-process method seam, it can replace the
/// stubs without re-shaping the interface.
/// </para>
/// </remarks>
public sealed class ComposeDocumentService : IComposeDocumentService
{
    private readonly IGraphClientFactory _graphClientFactory;
    private readonly ILogger<ComposeDocumentService> _logger;

    public ComposeDocumentService(
        IGraphClientFactory graphClientFactory,
        ILogger<ComposeDocumentService> logger)
    {
        _graphClientFactory = graphClientFactory ?? throw new ArgumentNullException(nameof(graphClientFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<ComposeLoadResult> LoadDocxAsync(
        HttpContext httpContext,
        string driveId,
        string itemId,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(driveId)) throw new ArgumentException("driveId is required", nameof(driveId));
        if (string.IsNullOrWhiteSpace(itemId)) throw new ArgumentException("itemId is required", nameof(itemId));

        using var activity = Activity.Current;
        activity?.SetTag("operation", "ComposeLoadDocx");
        activity?.SetTag("driveId", driveId);
        activity?.SetTag("itemId", itemId);

        _logger.LogInformation(
            "Compose load DOCX requested for drive {DriveId}, item {ItemId} (OBO)", driveId, itemId);

        try
        {
            // OBO: user is the file's writer-identity on SPE ACL (user opened the doc in Compose).
            var graphClient = await _graphClientFactory.ForUserAsync(httpContext, ct);

            // First fetch metadata to surface FileName / Size / ETag in the response shape.
            var item = await graphClient.Drives[driveId].Items[itemId]
                .GetAsync(cancellationToken: ct);

            if (item == null)
            {
                _logger.LogWarning(
                    "Compose load: drive-item not found {DriveId}/{ItemId}", driveId, itemId);
                return ComposeLoadResult.NotFound;
            }

            // Then stream content. SPE returns null for missing content; we surface that as NotFound.
            var stream = await graphClient.Drives[driveId].Items[itemId].Content
                .GetAsync(cancellationToken: ct);

            if (stream == null)
            {
                _logger.LogWarning(
                    "Compose load: drive-item {DriveId}/{ItemId} metadata exists but content stream is null",
                    driveId, itemId);
                return ComposeLoadResult.NotFound;
            }

            _logger.LogInformation(
                "Compose load DOCX succeeded for {DriveId}/{ItemId}, size={Size}",
                driveId, itemId, item.Size);

            return new ComposeLoadResult(
                Found: true,
                Content: stream,
                FileName: item.Name,
                ETag: item.ETag,
                Size: item.Size);
        }
        catch (ServiceException ex) when (ex.ResponseStatusCode == (int)System.Net.HttpStatusCode.NotFound)
        {
            _logger.LogWarning(
                "Compose load: drive-item not found via Graph 404 for {DriveId}/{ItemId}", driveId, itemId);
            return ComposeLoadResult.NotFound;
        }
        catch (ServiceException ex) when (ex.ResponseStatusCode == (int)System.Net.HttpStatusCode.Forbidden)
        {
            _logger.LogWarning(ex,
                "Compose load: access denied by SPE ACL for {DriveId}/{ItemId}", driveId, itemId);
            throw new UnauthorizedAccessException(
                $"Access denied to drive-item {itemId} on drive {driveId}", ex);
        }
        catch (ServiceException ex) when (ex.ResponseStatusCode == (int)System.Net.HttpStatusCode.TooManyRequests)
        {
            _logger.LogWarning(ex, "Compose load: Graph throttling for {DriveId}/{ItemId}", driveId, itemId);
            throw new InvalidOperationException(
                "Service temporarily unavailable due to Graph rate limiting", ex);
        }
        catch (ServiceException ex)
        {
            _logger.LogError(ex,
                "Compose load: Graph API error for {DriveId}/{ItemId}: {Error}", driveId, itemId, ex.Message);
            throw new InvalidOperationException(
                $"Failed to load DOCX from SPE: {ex.Message}", ex);
        }
    }

    /// <inheritdoc />
    public async Task<ComposeSaveResult> SaveDocxAsync(
        HttpContext httpContext,
        string driveId,
        string itemId,
        Stream content,
        ClaimsPrincipal user,
        string correlationId,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(driveId)) throw new ArgumentException("driveId is required", nameof(driveId));
        if (string.IsNullOrWhiteSpace(itemId)) throw new ArgumentException("itemId is required", nameof(itemId));
        if (string.IsNullOrWhiteSpace(correlationId)) throw new ArgumentException("correlationId is required", nameof(correlationId));

        using var activity = Activity.Current;
        activity?.SetTag("operation", "ComposeSaveDocx");
        activity?.SetTag("driveId", driveId);
        activity?.SetTag("itemId", itemId);
        activity?.SetTag("correlationId", correlationId);

        _logger.LogInformation(
            "Compose save DOCX requested for {DriveId}/{ItemId} [{CorrelationId}] (OBO)",
            driveId, itemId, correlationId);

        try
        {
            // OBO: user wrote → user can re-write. Per writer-identity rule, R1 always-OBO for save.
            var graphClient = await _graphClientFactory.ForUserAsync(httpContext, ct);

            // PUT to existing drive-item content endpoint — commits a new SPE version.
            // (SPE / SharePoint auto-versions drive-items; no explicit "create version" call needed.)
            var saved = await graphClient.Drives[driveId].Items[itemId].Content
                .PutAsync(content, cancellationToken: ct);

            if (saved == null)
            {
                _logger.LogWarning(
                    "Compose save: Graph PUT returned null for {DriveId}/{ItemId} [{CorrelationId}]",
                    driveId, itemId, correlationId);
                return ComposeSaveResult.NotFound;
            }

            // SPE versions are exposed via item.PublicationFacet or the /versions endpoint.
            // The PUT response returns the updated DriveItem with its current ETag — that ETag is
            // the canonical version pointer for the just-committed content.
            _logger.LogInformation(
                "Compose save DOCX succeeded for {DriveId}/{ItemId} [{CorrelationId}], new ETag={ETag}, size={Size}",
                driveId, itemId, correlationId, saved.ETag, saved.Size);

            return new ComposeSaveResult(
                Found: true,
                VersionId: saved.Id,
                ETag: saved.ETag,
                Size: saved.Size);
        }
        catch (ServiceException ex) when (ex.ResponseStatusCode == (int)System.Net.HttpStatusCode.NotFound)
        {
            _logger.LogWarning(
                "Compose save: drive-item not found via Graph 404 for {DriveId}/{ItemId} [{CorrelationId}]",
                driveId, itemId, correlationId);
            return ComposeSaveResult.NotFound;
        }
        catch (ServiceException ex) when (ex.ResponseStatusCode == (int)System.Net.HttpStatusCode.Forbidden)
        {
            _logger.LogWarning(ex,
                "Compose save: access denied by SPE ACL for {DriveId}/{ItemId} [{CorrelationId}]",
                driveId, itemId, correlationId);
            throw new UnauthorizedAccessException(
                $"Access denied to drive-item {itemId} on drive {driveId}", ex);
        }
        catch (ServiceException ex) when (ex.ResponseStatusCode == (int)System.Net.HttpStatusCode.TooManyRequests)
        {
            _logger.LogWarning(ex,
                "Compose save: Graph throttling for {DriveId}/{ItemId} [{CorrelationId}]",
                driveId, itemId, correlationId);
            throw new InvalidOperationException(
                "Service temporarily unavailable due to Graph rate limiting", ex);
        }
        catch (ServiceException ex)
        {
            _logger.LogError(ex,
                "Compose save: Graph API error for {DriveId}/{ItemId} [{CorrelationId}]: {Error}",
                driveId, itemId, correlationId, ex.Message);
            throw new InvalidOperationException(
                $"Failed to save DOCX to SPE: {ex.Message}", ex);
        }
    }

    /// <inheritdoc />
    public Task AcquireCheckOutAsync(
        Guid documentId,
        ClaimsPrincipal user,
        CancellationToken ct = default)
        => throw new NotImplementedException(
            "Phase 5 task 050: Compose acquires check-out by calling the existing " +
            "/api/documents/{id}/checkout endpoint from the React layer (per spike #3 §9 Phase 5 " +
            "task table). If a server-side seam is preferred later, replace this stub with a " +
            "delegate to DocumentCheckoutService.CheckoutAsync.");

    /// <inheritdoc />
    public Task ReleaseCheckOutAsync(
        Guid documentId,
        ClaimsPrincipal user,
        CancellationToken ct = default)
        => throw new NotImplementedException(
            "Phase 5 task 050: Compose releases check-out by calling the existing " +
            "/api/documents/{id}/checkin or /discard endpoints (per spike #3 §8 endpoint table). " +
            "If a server-side seam is preferred later, replace this stub with a delegate to " +
            "DocumentCheckoutService.CheckInAsync / DiscardAsync.");
}
