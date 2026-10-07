using System.Diagnostics;
using System.Globalization;
using Microsoft.Graph;
using Microsoft.Graph.Models;
using Microsoft.Graph.Models.ODataErrors;
using Sprk.Bff.Api.Models;

namespace Sprk.Bff.Api.Infrastructure.Graph;

/// <summary>
/// Handles file upload operations including small files and chunked uploads.
/// Responsible for upload session management and chunk processing.
/// </summary>
public class UploadSessionManager
{
    private readonly IGraphClientFactory _factory;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<UploadSessionManager> _logger;

    public UploadSessionManager(IGraphClientFactory factory, IHttpClientFactory httpClientFactory, ILogger<UploadSessionManager> logger)
    {
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
        _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// PUTs content to a path with an EXPLICIT <c>@microsoft.graph.conflictBehavior</c>.
    /// </summary>
    /// <remarks>
    /// <para>The Kiota-generated <c>Content.PutAsync</c> does not expose <c>conflictBehavior</c>, but
    /// the REST API honours it — so the query parameter is appended to the generated request rather
    /// than forking to a hand-rolled <c>HttpClient</c> (which would lose the SDK's auth, retry and
    /// error mapping). The error mapping is passed through deliberately: without it a 4XX/5XX surfaces
    /// as a generic Kiota <c>ApiException</c> instead of <c>ODataError</c>, which would silently
    /// dead-code every <c>catch (ODataError …)</c> filter in the callers — including the FR-08 412
    /// surface.</para>
    ///
    /// <para>Reuses the existing <see cref="Sprk.Bff.Api.Models.ConflictBehavior"/> enum and its
    /// <c>ToGraphString()</c> mapping (already used by <c>CreateUploadSessionAsUserAsync</c>) rather
    /// than introducing a second vocabulary for the same Graph concept.</para>
    ///
    /// <para>⚠️ The behaviour MUST always be stated explicitly. The <c>driveItem</c> docs say "the
    /// default for PUT is replace" while the createUploadSession docs say "fail (default)" — they
    /// disagree, so relying on the default is relying on undefined behaviour. Verified 2026-08-20
    /// against MS Learn + the docs source repos:
    /// <c>.claude/agent-memory/researcher/graph-driveitem-upload-facts.md</c>. Note it is a NAME
    /// collision control only, never a content or version comparison.</para>
    /// </remarks>
    private async Task<DriveItem?> PutContentWithConflictBehaviorAsync(
        GraphServiceClient graphClient,
        string driveOrContainerId,
        string path,
        Stream content,
        ConflictBehavior conflictBehavior,
        CancellationToken ct)
    {
        var requestInfo = graphClient.Drives[driveOrContainerId].Root
            .ItemWithPath(path)
            .Content
            .ToPutRequestInformation(content);

        var parameter =
            $"@microsoft.graph.conflictBehavior={Uri.EscapeDataString(conflictBehavior.ToGraphString())}";
        var uriBuilder = new UriBuilder(requestInfo.URI);
        uriBuilder.Query = string.IsNullOrEmpty(uriBuilder.Query)
            ? parameter
            : $"{uriBuilder.Query.TrimStart('?')}&{parameter}";
        requestInfo.URI = uriBuilder.Uri;

        // Fully qualified: unqualified `IParsable` binds to System.IParsable<TSelf>, not Kiota's.
        var errorMapping = new Dictionary<
            string,
            Microsoft.Kiota.Abstractions.Serialization.ParsableFactory<
                Microsoft.Kiota.Abstractions.Serialization.IParsable>>
        {
            { "XXX", ODataError.CreateFromDiscriminatorValue },
        };

        return await graphClient.RequestAdapter.SendAsync(
            requestInfo,
            DriveItem.CreateFromDiscriminatorValue,
            errorMapping,
            ct).ConfigureAwait(false);
    }

    /// <summary>
    /// App-only small upload, preserving the historical <see cref="ConflictBehavior.Replace"/> behaviour.
    /// </summary>
    /// <remarks>
    /// Kept as a delegating overload rather than changed in place: every existing app-only caller
    /// (Compose save, communication ingest, invoice extraction, …) writes to a path it derived itself
    /// and relies on replace-in-place. Flipping the default under them would turn a working save into a
    /// 409. New callers that must not clobber a same-named file pass
    /// <see cref="ConflictBehavior.Fail"/> explicitly.
    /// </remarks>
    public Task<FileHandleDto?> UploadSmallAsync(
        string driveId,
        string path,
        Stream content,
        CancellationToken ct = default)
        => UploadSmallAsync(driveId, path, content, ConflictBehavior.Replace, ct);

    /// <summary>
    /// App-only small upload with an EXPLICIT name-collision behaviour.
    /// </summary>
    /// <remarks>
    /// <para><see cref="ConflictBehavior.Fail"/> makes a collision a Graph 409, translated to
    /// <see cref="SpaarkeStorageException"/> (409) with the existing item left UNTOUCHED — the caller
    /// can then tell the user rather than silently overwriting. The external upload route requires this:
    /// an external participant must never be able to overwrite a document by uploading a same-named file.</para>
    /// <para><b>unified-access-control-r2 task 171 (owner round 69, broker-only).</b> This is now the ONLY small
    /// upload: the OBO twin (<c>UploadSmallAsUserAsync</c>) is DELETED, because under OBO SharePoint Embedded only
    /// lets a caller who holds a container ROLE write, and per-record secure containers have none by design (the
    /// 2026-10-06 upload403 defect). Every caller authorizes against Dataverse first and derives the container
    /// server-side; this method performs NO authorization. It inherits the OBO twin's typed error translation
    /// (403/409/412/413/423/429), so a caller that moved here sees the same exceptions it handled before.</para>
    /// </remarks>
    public async Task<FileHandleDto?> UploadSmallAsync(
        string driveId,
        string path,
        Stream content,
        ConflictBehavior conflictBehavior,
        CancellationToken ct = default)
    {
        // Task 093 (#1084 follow-on): tags go on the CALLER's request Activity, as always — `using` here
        // disposed it the moment this method returned, ending the request's own trace span early (resultCode
        // 0, success false, a truncated duration). This method did not start the Activity and must not end it.
        var activity = Activity.Current;
        activity?.SetTag("operation", "UploadSmall");
        activity?.SetTag("driveId", driveId);
        activity?.SetTag("filePath", path);

        _logger.LogInformation("Uploading small file to drive {DriveId} at path {Path} (app-only)",
            driveId, path);

        return await UploadSmallCoreAsync(_factory.ForApp(), driveId, path, content, conflictBehavior, ct)
            .ConfigureAwait(false);
    }

    // ---------------------------------------------------------------------------------------------
    // APP-ONLY CHUNKED UPLOAD (CreateUploadSessionAsync + UploadChunkAsync) — DELETED 2026-08-27
    // by unified-access-control-r2, following task 073.
    //
    // Their only caller was Api/UploadEndpoints.cs, which 073 deleted outright. They were reported as
    // "0 production callers" BEFORE 073 as well (merge plan follow-up #2) — 073 is what made that
    // provably true rather than nearly true, and the retirement regression guard
    // (tests/integration/regression/MiContainerKeyedWriteRouteRetirementTests.cs) is what keeps the
    // routes from coming back and reviving them.
    //
    // ⚠️ UPDATED 2026-10-06 (task 171). The OBO upload-session twin that this note used to call LIVE is replaced by the
    // app-only CreateUploadSessionAsync below (its one caller is the record-keyed upload-session route, which authorizes
    // the caller on the owning record first). UploadChunkAsUserAsync remains, uncalled: it never used the caller's
    // identity (the client PUTs chunks straight to Graph's pre-authenticated session URL).
    // ---------------------------------------------------------------------------------------------


    // =============================================================================
    // USER CONTEXT METHODS (OBO Flow)
    // =============================================================================

    /// <summary>
    /// Small upload as the CALLER (OBO), keeping the historical <see cref="ConflictBehavior.Replace"/>. ONLY for the
    /// STAGING writes (chat persist, chat Word export, workspace pre-fill): their container comes from configuration
    /// and their filters only check identity, so the user's own SPE write right on the staging container is the only
    /// population check. Converting them to app-only would let every signed-in user write there — task 171
    /// escalation trigger 2, reported to the owner, not converted. Every record-backed upload is app-only above.
    /// </summary>
    public async Task<FileHandleDto?> UploadSmallToStagingAsUserAsync(
        HttpContext ctx,
        string containerId,
        string path,
        Stream content,
        CancellationToken ct = default)
        => await UploadSmallCoreAsync(await _factory.ForUserAsync(ctx, ct), containerId, path, content, ConflictBehavior.Replace, ct)
            .ConfigureAwait(false);

    // =============================================================================
    // SHARED CORE (task 171): ONE upload body, so the typed error translation cannot drift between
    // identities.
    // =============================================================================

    /// <summary>
    /// Graph's SIMPLE upload — a single <c>PUT .../content</c> — through <paramref name="graphClient"/>. Named
    /// "small" for the R1-era 4 MB boundary that no longer applies: Graph raised the simple-upload limit to 250 MB in
    /// October 2023 and SharePoint Embedded confirms the same figure for containers. Callers enforce their own product
    /// limits (see <c>ComposeSaveLimits</c>); this method enforces none.
    /// </summary>
    /// <remarks>
    /// <see cref="ConflictBehavior.Fail"/> makes a name collision a Graph 409 with the existing item left untouched —
    /// the only value that is safe when the caller has not yet asked the user what to do.
    /// <see cref="ConflictBehavior.Rename"/> stores under a server-generated name; <see cref="ConflictBehavior.Replace"/>
    /// overwrites in place (SharePoint keeps the prior content as a version).
    /// </remarks>
    private async Task<FileHandleDto?> UploadSmallCoreAsync(
        GraphServiceClient graphClient,
        string containerId,
        string path,
        Stream content,
        ConflictBehavior conflictBehavior,
        CancellationToken ct)
    {
        try
        {
            // FR-S08 (spaarkeai-compose-r8 task 015): the 4 MB guard that stood here is DELETED — it
            // enforced a Graph limit that no longer exists. `PUT .../content` has accepted files up to
            // 250 MB since October 2023 (the 4 MB figure comes from the retired OneDrive REST docs, which
            // now redirect to the Graph page carrying the new number), and SharePoint Embedded confirms
            // the same 250 MB simple-upload boundary for containers. The guard's advice — "use chunked
            // upload instead" — therefore sent callers to a resumable session they do not need, and it
            // failed a Compose create-on-save of any document over 4 MB outright.
            //
            // It is NOT replaced with a 250 MB guard: the caller that cares (Compose) enforces its own
            // product limit at the endpoint, from ComposeSaveLimits, and a second threshold here would be
            // the "two constants" divergence that turns a stated limit into an unexplained failure. If a
            // future caller genuinely needs >250 MB, that caller routes to a resumable session — which is
            // a decision about that caller, not a guard on this method.

            // For SharePoint Embedded: Container ID = Drive ID (per Microsoft documentation).
            // Reference: https://learn.microsoft.com/en-us/sharepoint/dev/embedded/concepts/app-concepts/containertypes

            var uploadedItem = await PutContentWithConflictBehaviorAsync(
                graphClient, containerId, path, content, conflictBehavior, ct)
                .ConfigureAwait(false);

            if (uploadedItem == null)
            {
                _logger.LogError("Upload failed for path {Path} in container {ContainerId}", path, containerId);
                return null;
            }

            _logger.LogInformation("Successfully uploaded file to {Path} in container {ContainerId}, item ID: {ItemId}",
                path, containerId, uploadedItem.Id);

            // Map Graph SDK DriveItem to SDAP DTO (ADR-007 compliance)
            return new FileHandleDto(
                uploadedItem.Id!,
                uploadedItem.Name!,
                uploadedItem.ParentReference?.Id,
                uploadedItem.Size,
                uploadedItem.CreatedDateTime ?? DateTimeOffset.UtcNow,
                uploadedItem.LastModifiedDateTime ?? DateTimeOffset.UtcNow,
                uploadedItem.ETag,
                uploadedItem.Folder != null,
                uploadedItem.WebUrl,
                uploadedItem.ParentReference?.DriveId ?? containerId);
        }
        // ── FR-08 (task 051, spaarkeai-compose-r4): PRIMARY catch surface, mirroring the DEF-14 fix
        //    already applied to ReplaceFileContentAsUserAsync above. The Graph SDK v5 / Kiota `PutAsync`
        //    call above raises `Microsoft.Graph.Models.ODataErrors.ODataError`, NOT the legacy
        //    `Microsoft.Graph.ServiceException` — the ServiceException catches below this block are DEAD
        //    CODE for this call (Kiota never throws that type). Without these ODataError filters, ANY
        //    Graph error during create-on-save (incl. a create-time precondition/conflict whose message
        //    text reads "the resource has been changed since the caller last read it" — the deployed R3
        //    UAT eTag-mismatch surface, spec FR-08) fell through to the generic `catch (Exception ex) {
        //    throw; }` below, leaking the RAW ODataError type up through ISpeFileOperations into
        //    Services/Compose/ (an ADR-007 violation) and surfacing as an opaque 500 at the endpoint
        //    instead of the typed 412 ProblemDetails the caller already knows how to render. ADR-007: the
        //    Microsoft.Graph type is caught + translated HERE, inside Infrastructure.Graph — only the
        //    domain exceptions cross the ISpeFileOperations facade.
        catch (ODataError ex) when (ex.ResponseStatusCode == 403)
        {
            _logger.LogError(ex, "SPE create (upload-small): access denied for container={ContainerId} path={Path}",
                containerId, path);
            throw new UnauthorizedAccessException($"Access denied to container {containerId}: {ex.Error?.Message ?? ex.Message}", ex);
        }
        catch (ODataError ex) when (ex.ResponseStatusCode == 409)
        {
            // Reached only when the caller passed ConflictBehavior.Fail — i.e. it explicitly asked to
            // be told about a name collision rather than silently overwriting. The existing item is
            // UNTOUCHED, which is the whole point: the caller can now ask the user to rename or save a
            // new version. Surfaced as SpaarkeStorageException (409 + Graph's own code, typically
            // "nameAlreadyExists") because the OBO endpoints already translate that to ProblemDetails —
            // no new exception type for a case the existing one models exactly.
            _logger.LogInformation(
                "SPE create (upload-small): name collision on path {Path} in container {ContainerId}; " +
                "existing item left intact (conflictBehavior=fail)", path, containerId);
            throw new SpaarkeStorageException(
                $"A file named '{path}' already exists in this location.",
                statusCode: 409,
                errorCode: ex.Error?.Code ?? "nameAlreadyExists",
                innerException: ex);
        }
        catch (ODataError ex) when (ex.ResponseStatusCode == 413)
        {
            _logger.LogWarning("SPE create (upload-small): content too large for path {Path} in container {ContainerId}", path, containerId);
            throw new ArgumentException("Content size exceeds limit. Use chunked upload for large files.", ex);
        }
        catch (ODataError ex) when (ex.ResponseStatusCode == 412)
        {
            // FR-08 (task 051): the create-time precondition surface — "the resource has been changed
            // since the caller last read it" (or a genuine 412) — now surfaces as the SAME typed
            // EtagPreconditionFailedException ReplaceFileContentAsUserAsync throws on the equivalent
            // Graph response, already mapped by ComposeEndpoints.ExecuteSaveAsync to a clean 412
            // ProblemDetails ("This document changed since you opened it — reload and reapply"), never a
            // 500 and never a misleadingly-generic InvalidOperationException.
            _logger.LogWarning(ex, "SPE create (upload-small): precondition failed for container={ContainerId} path={Path}",
                containerId, path);
            throw new EtagPreconditionFailedException(path, ifMatch: null, ex);
        }
        catch (ODataError ex) when (ex.ResponseStatusCode == 423 || IsResourceLockedCode(ex.Error?.Code))
        {
            _logger.LogWarning(ex, "SPE create (upload-small): resource locked for container={ContainerId} path={Path} code={Code}",
                containerId, path, ex.Error?.Code);
            throw new DocumentLockedByWordException(path, ex);
        }
        catch (ODataError ex) when (ex.ResponseStatusCode == 429)
        {
            // FR-S09 item 6 (r8 task 016): a throttle is a TYPED, retryable refusal carrying Graph's own
            // back-off, not a generic InvalidOperationException that reaches the endpoint's catch-all and
            // becomes an HTTP 500. Nothing was written; the caller's document is intact.
            var retryAfter = ReadRetryAfter(ex.ResponseHeaders);
            _logger.LogWarning(ex,
                "SPE create (upload-small): Graph throttling for container={ContainerId} path={Path} retryAfter={RetryAfter}",
                containerId, path, retryAfter);
            throw new GraphThrottledException(path, retryAfter, ex);
        }
        catch (ODataError ex)
        {
            _logger.LogError(ex, "SPE create (upload-small) Graph error for container={ContainerId} path={Path}: {Message}",
                containerId, path, ex.Error?.Message ?? ex.Message);
            throw new InvalidOperationException($"Failed to upload file: {ex.Error?.Message ?? ex.Message}", ex);
        }
        // ── Belt-and-suspenders: the legacy ServiceException path is retained in case a non-Kiota code
        //    path (or a future SDK) raises it. Harmless; the ODataError filters above are the primary
        //    surface (mirrors ReplaceFileContentAsUserAsync's identical belt-and-suspenders comment).
        catch (ServiceException ex) when (ex.ResponseStatusCode == 403)
        {
            _logger.LogError(ex, "Access denied uploading to container {ContainerId}: HTTP {StatusCode} - {Message}",
                containerId, ex.ResponseStatusCode, ex.Message);
            throw new UnauthorizedAccessException($"Access denied to container {containerId}: {ex.Message}", ex);
        }
        catch (ServiceException ex) when (ex.ResponseStatusCode == 413)
        {
            _logger.LogWarning("Content too large for path {Path}", path);
            throw new ArgumentException("Content size exceeds limit. Use chunked upload for large files.", ex);
        }
        catch (ServiceException ex) when (ex.ResponseStatusCode == 429)
        {
            // FR-S09 item 6 (r8 task 016): same typed refusal as the ODataError filter above. Kept in
            // step with it so the belt-and-suspenders path cannot report a throttle differently.
            var retryAfter = ex.ResponseHeaders?.RetryAfter?.Delta;
            _logger.LogWarning(ex, "Graph API throttling, retry after {RetryAfter}", retryAfter);
            throw new GraphThrottledException(path, retryAfter, ex);
        }
        catch (ServiceException ex)
        {
            _logger.LogError(ex, "Graph API error uploading file: HTTP {StatusCode} - {Message}",
                ex.ResponseStatusCode, ex.Message);
            throw new InvalidOperationException($"Failed to upload file: HTTP {ex.ResponseStatusCode} - {ex.Message}", ex);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to upload to path {Path} in container {ContainerId}", path, containerId);
            throw;
        }
    }

    /// <summary>
    /// Replace the content of an existing drive-item by itemId (OBO flow).
    /// PUTs the stream to the drive-item's <c>/content</c> endpoint, committing a new
    /// SPE version. Used by document editors (Compose R1) that saved content back
    /// to an item they had already opened.
    /// </summary>
    public Task<FileHandleDto?> ReplaceFileContentAsUserAsync(
        HttpContext ctx,
        string driveId,
        string itemId,
        Stream content,
        CancellationToken ct = default)
        => ReplaceFileContentAsUserAsync(ctx, driveId, itemId, content, ifMatch: null, ct);

    public async Task<FileHandleDto?> ReplaceFileContentAsUserAsync(
        HttpContext ctx,
        string driveId,
        string itemId,
        Stream content,
        string? ifMatch,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(driveId)) throw new ArgumentException("driveId is required", nameof(driveId));
        if (string.IsNullOrWhiteSpace(itemId)) throw new ArgumentException("itemId is required", nameof(itemId));

        return await ReplaceFileContentCoreAsync(await _factory.ForUserAsync(ctx, ct), driveId, itemId, content, ifMatch, ct)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Replace the content of an existing drive-item APP-ONLY (broker), optionally under <c>If-Match</c> — the
    /// app-only twin of <see cref="ReplaceFileContentAsUserAsync(HttpContext, string, string, Stream, string?, CancellationToken)"/>,
    /// with the SAME typed translation (404 → null, 403, 412, 423/locked, 429).
    /// </summary>
    /// <remarks>
    /// unified-access-control-r2 task 171 (owner round 69). ⚠️ Performs NO authorization: every caller MUST have
    /// authorized the caller for WRITE on the <c>sprk_document</c> row whose pointer names this item (and verified that
    /// pointer) before calling it. Under the app identity a container role no longer stands behind the write.
    /// </remarks>
    public async Task<FileHandleDto?> ReplaceFileContentAsync(
        string driveId,
        string itemId,
        Stream content,
        string? ifMatch,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(driveId)) throw new ArgumentException("driveId is required", nameof(driveId));
        if (string.IsNullOrWhiteSpace(itemId)) throw new ArgumentException("itemId is required", nameof(itemId));

        return await ReplaceFileContentCoreAsync(_factory.ForApp(), driveId, itemId, content, ifMatch, ct)
            .ConfigureAwait(false);
    }

    private async Task<FileHandleDto?> ReplaceFileContentCoreAsync(
        GraphServiceClient graphClient,
        string driveId,
        string itemId,
        Stream content,
        string? ifMatch,
        CancellationToken ct)
    {
        try
        {
            // FR-24 / Spike 7 G-1: send If-Match for optimistic concurrency when the caller
            // supplied the load-time ETag. Absent an ETag this remains the R1 blind PUT.
            var saved = await graphClient.Drives[driveId].Items[itemId].Content
                .PutAsync(content, requestConfiguration =>
                {
                    if (!string.IsNullOrEmpty(ifMatch))
                    {
                        requestConfiguration.Headers.Add("If-Match", ifMatch);
                    }
                }, cancellationToken: ct);

            if (saved == null)
            {
                _logger.LogWarning("SPE replace-content returned null for drive={DriveId} item={ItemId}", driveId, itemId);
                return null;
            }

            _logger.LogInformation(
                "SPE replace-content succeeded for drive={DriveId} item={ItemId} etag={ETag} size={Size}",
                driveId, itemId, saved.ETag, saved.Size);

            return new FileHandleDto(
                saved.Id!,
                saved.Name!,
                saved.ParentReference?.Id,
                saved.Size,
                saved.CreatedDateTime ?? DateTimeOffset.UtcNow,
                saved.LastModifiedDateTime ?? DateTimeOffset.UtcNow,
                saved.ETag,
                saved.Folder != null,
                saved.WebUrl,
                saved.ParentReference?.DriveId ?? driveId);
        }
        // ── DEF-14: PRIMARY catch surface. The Graph SDK v5 / Kiota `PutAsync` above raises
        //    `Microsoft.Graph.Models.ODataErrors.ODataError`, NOT the legacy
        //    `Microsoft.Graph.ServiceException`. The ServiceException catches below were
        //    therefore DEAD CODE (Kiota never throws that type), so a 423/412 leaked to the
        //    endpoint as an opaque 500. These ODataError filters revive the intended typed
        //    translation. ADR-007: the Microsoft.Graph type is caught + translated here, inside
        //    Infrastructure.Graph — only the domain exceptions cross the ISpeFileOperations facade.
        catch (ODataError ex) when (ex.ResponseStatusCode == 404)
        {
            _logger.LogWarning("SPE replace-content: drive-item not found drive={DriveId} item={ItemId}", driveId, itemId);
            return null;
        }
        catch (ODataError ex) when (ex.ResponseStatusCode == 403)
        {
            _logger.LogError(ex, "SPE replace-content: access denied drive={DriveId} item={ItemId}", driveId, itemId);
            throw new UnauthorizedAccessException($"Access denied to drive-item {itemId} on drive {driveId}", ex);
        }
        catch (ODataError ex) when (ex.ResponseStatusCode == 412)
        {
            // FR-24 / Spike 7 C′ (G-1): the ETag moved under us — reject instead of clobbering.
            _logger.LogWarning(ex, "SPE replace-content: If-Match precondition failed drive={DriveId} item={ItemId} ifMatch={IfMatch}",
                driveId, itemId, ifMatch);
            throw new EtagPreconditionFailedException(itemId, ifMatch, ex);
        }
        catch (ODataError ex) when (ex.ResponseStatusCode == 423 || IsResourceLockedCode(ex.Error?.Code))
        {
            // FR-24 / Spike 7 C (G-2) + DEF-14: the drive-item is open in Word for Web / checked
            // out at the SPE level (HTTP 423, or a SharePoint "resourceLocked"/"locked" error code
            // that can ride on a different status) — surface a typed 423 rather than an opaque 500.
            _logger.LogWarning(ex, "SPE replace-content: drive-item locked drive={DriveId} item={ItemId} code={Code}",
                driveId, itemId, ex.Error?.Code);
            throw new DocumentLockedByWordException(itemId, ex);
        }
        catch (ODataError ex) when (ex.ResponseStatusCode == 429)
        {
            // FR-S09 item 6 (r8 task 016) — see the create-path throttle catch above.
            var retryAfter = ReadRetryAfter(ex.ResponseHeaders);
            _logger.LogWarning(ex,
                "SPE replace-content: Graph throttling drive={DriveId} item={ItemId} retryAfter={RetryAfter}",
                driveId, itemId, retryAfter);
            throw new GraphThrottledException(itemId, retryAfter, ex);
        }
        catch (ODataError ex)
        {
            _logger.LogError(ex, "SPE replace-content Graph error drive={DriveId} item={ItemId}: {Message}",
                driveId, itemId, ex.Error?.Message ?? ex.Message);
            throw new InvalidOperationException($"Failed to replace drive-item content: {ex.Error?.Message ?? ex.Message}", ex);
        }
        // ── Belt-and-suspenders: the legacy ServiceException path is retained in case a non-Kiota
        //    code path (or a future SDK) raises it. Harmless; the ODataError filters above are the
        //    ones that fire in production today.
        catch (ServiceException ex) when (ex.ResponseStatusCode == 404)
        {
            _logger.LogWarning("SPE replace-content: drive-item not found drive={DriveId} item={ItemId}", driveId, itemId);
            return null;
        }
        catch (ServiceException ex) when (ex.ResponseStatusCode == 403)
        {
            _logger.LogError(ex, "SPE replace-content: access denied drive={DriveId} item={ItemId}", driveId, itemId);
            throw new UnauthorizedAccessException($"Access denied to drive-item {itemId} on drive {driveId}", ex);
        }
        catch (ServiceException ex) when (ex.ResponseStatusCode == 412)
        {
            // FR-24 / Spike 7 C′ (G-1): the ETag moved under us — reject instead of clobbering.
            _logger.LogWarning(ex, "SPE replace-content: If-Match precondition failed drive={DriveId} item={ItemId} ifMatch={IfMatch}",
                driveId, itemId, ifMatch);
            throw new EtagPreconditionFailedException(itemId, ifMatch, ex);
        }
        catch (ServiceException ex) when (ex.ResponseStatusCode == 423)
        {
            // FR-24 / Spike 7 C (G-2): the drive-item is open in Word for Web (locked co-authoring
            // session) — surface a typed 423 rather than an opaque 500.
            _logger.LogWarning(ex, "SPE replace-content: drive-item locked by Word drive={DriveId} item={ItemId}", driveId, itemId);
            throw new DocumentLockedByWordException(itemId, ex);
        }
        catch (ServiceException ex) when (ex.ResponseStatusCode == 429)
        {
            // FR-S09 item 6 (r8 task 016) — see the ODataError filter above.
            var retryAfter = ex.ResponseHeaders?.RetryAfter?.Delta;
            _logger.LogWarning(ex,
                "SPE replace-content: Graph throttling drive={DriveId} item={ItemId} retryAfter={RetryAfter}",
                driveId, itemId, retryAfter);
            throw new GraphThrottledException(itemId, retryAfter, ex);
        }
        catch (ServiceException ex)
        {
            _logger.LogError(ex, "SPE replace-content Graph error drive={DriveId} item={ItemId}: {Message}",
                driveId, itemId, ex.Message);
            throw new InvalidOperationException($"Failed to replace drive-item content: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// DEF-14: true when a Graph/SharePoint error code denotes a locked/checked-out drive-item.
    /// SPE surfaces document locks as HTTP 423, but the SharePoint back-end sometimes rides the
    /// lock signal on a differently-numbered status with a <c>resourceLocked</c> / <c>locked</c>
    /// error code, so we match the code defensively as well as the status.
    /// </summary>
    private static bool IsResourceLockedCode(string? code) =>
        !string.IsNullOrEmpty(code) &&
        code.Contains("locked", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// FR-S09 item 6 (r8 task 016): read Graph's <c>Retry-After</c> off a Kiota error's response headers.
    /// </summary>
    /// <remarks>
    /// Graph sends <c>Retry-After</c> as delta-seconds on a 429. It is the ONE piece of information that
    /// makes a throttle actionable, and every throttle site used to discard it. Returns null when the
    /// header is absent or unparseable — callers state a conservative default rather than invent a number.
    /// An HTTP-date form (RFC 9110 permits it, Graph does not send it) is deliberately NOT parsed: a wrong
    /// number would be worse than none.
    /// </remarks>
    private static TimeSpan? ReadRetryAfter(IDictionary<string, IEnumerable<string>>? headers)
    {
        if (headers is null) return null;
        foreach (var (key, values) in headers)
        {
            if (!string.Equals(key, "Retry-After", StringComparison.OrdinalIgnoreCase)) continue;
            foreach (var value in values)
            {
                if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var seconds)
                    && seconds is > 0 and <= 3600)
                {
                    return TimeSpan.FromSeconds(seconds);
                }
            }
            return null;
        }
        return null;
    }

    /// <summary>
    /// Creates an upload session for large files APP-ONLY (broker).
    /// </summary>
    /// <remarks>
    /// unified-access-control-r2 task 171 (owner round 69): this was <c>CreateUploadSessionAsUserAsync</c> (OBO), and
    /// it was the call that 403'd on 2026-10-06 — a creator uploading into its own newly provisioned SECURE project,
    /// whose container (by design) has no members. The OBO twin is replaced, not kept beside it: its one caller, the
    /// record-keyed <c>POST /api/obo/records/{entity}/{id}/upload-session</c> route, authorizes the caller on the
    /// owning record and resolves the container from that record before calling this. ⚠️ Performs NO authorization.
    /// The returned <c>uploadUrl</c> is Graph's pre-authenticated session URL; the client PUTs chunks to it directly.
    /// </remarks>
    public async Task<UploadSessionResponse?> CreateUploadSessionAsync(
        string driveId,
        string path,
        ConflictBehavior conflictBehavior,
        CancellationToken ct = default)
    {
        try
        {
            var graphClient = _factory.ForApp();

            // Create upload session request
            var uploadSessionRequest = new Microsoft.Graph.Drives.Item.Items.Item.CreateUploadSession.CreateUploadSessionPostRequestBody
            {
                Item = new DriveItemUploadableProperties
                {
                    AdditionalData = new Dictionary<string, object>
                    {
                        ["@microsoft.graph.conflictBehavior"] = conflictBehavior.ToString().ToLowerInvariant()
                    }
                }
            };

            // Create upload session via Graph API
            var session = await graphClient.Drives[driveId].Root
                .ItemWithPath(path)
                .CreateUploadSession
                .PostAsync(uploadSessionRequest, cancellationToken: ct);

            if (session == null || string.IsNullOrEmpty(session.UploadUrl))
            {
                _logger.LogError("Failed to create upload session for path {Path}", path);
                return null;
            }

            _logger.LogInformation("Created upload session for drive {DriveId}, path {Path}, conflict behavior {ConflictBehavior}, expires at {ExpirationDateTime}",
                driveId, path, conflictBehavior, session.ExpirationDateTime);

            return new UploadSessionResponse(
                session.UploadUrl,
                session.ExpirationDateTime ?? DateTimeOffset.UtcNow.AddHours(1)
            );
        }
        catch (Exception ex)
        {
            // An ODataError propagates unchanged: the route runs this inside GraphCallScope, which translates it to
            // SpaarkeStorageException with Graph's own status and code (the dead ServiceException-403 filter that
            // stood here never fired under Kiota, which raises ODataError).
            _logger.LogError(ex, "Failed to create upload session for drive {DriveId}, path {Path}", driveId, path);
            throw;
        }
    }

    /// <summary>
    /// Uploads a chunk to an upload session as the user (OBO flow).
    /// </summary>
    public async Task<ChunkUploadResponse> UploadChunkAsUserAsync(
        string userToken,
        string uploadSessionUrl,
        string contentRange,
        byte[] chunkData,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(userToken))
            throw new ArgumentException("User access token required", nameof(userToken));

        try
        {
            var range = ContentRangeHeader.Parse(contentRange);
            if (range == null || !range.IsValid)
            {
                _logger.LogWarning("Invalid Content-Range header: {ContentRange}", contentRange);
                return new ChunkUploadResponse(400);
            }

            // Validate chunk size (8-10 MiB as per Graph API requirements)
            const long minChunkSize = 8 * 1024 * 1024;
            const long maxChunkSize = 10 * 1024 * 1024;

            if (chunkData.Length < minChunkSize && (!range.Total.HasValue || range.End + 1 < range.Total.Value))
            {
                _logger.LogWarning("Chunk size {Size} below minimum {MinSize} (not final chunk)", chunkData.Length, minChunkSize);
                return new ChunkUploadResponse(400);
            }

            if (chunkData.Length > maxChunkSize)
            {
                _logger.LogWarning("Chunk size {Size} exceeds maximum {MaxSize}", chunkData.Length, maxChunkSize);
                return new ChunkUploadResponse(413);
            }

            if (chunkData.Length != range.ChunkSize)
            {
                _logger.LogWarning("Chunk data length {ActualSize} does not match Content-Range size {ExpectedSize}",
                    chunkData.Length, range.ChunkSize);
                return new ChunkUploadResponse(400);
            }

            // Upload chunk to Graph API using raw HTTP (SDK doesn't expose chunked upload directly)
            using var httpClient = _httpClientFactory.CreateClient("GraphUploadSession");
            using var content = new ByteArrayContent(chunkData);
            content.Headers.Add("Content-Range", contentRange);
            content.Headers.ContentLength = chunkData.Length;

            var response = await httpClient.PutAsync(uploadSessionUrl, content, ct);

            // Handle response based on status code
            if (response.StatusCode == System.Net.HttpStatusCode.Accepted) // 202 - more chunks expected
            {
                _logger.LogInformation("Uploaded chunk {Start}-{End} for session {UploadUrl}",
                    range.Start, range.End, uploadSessionUrl);
                return new ChunkUploadResponse(202);
            }
            else if (response.StatusCode == System.Net.HttpStatusCode.Created ||
                     response.StatusCode == System.Net.HttpStatusCode.OK) // 201/200 - upload complete
            {
                var responseContent = await response.Content.ReadAsStringAsync(ct);
                var driveItem = System.Text.Json.JsonSerializer.Deserialize<DriveItem>(responseContent, new System.Text.Json.JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (driveItem == null)
                {
                    _logger.LogError("Failed to deserialize completed upload response");
                    return new ChunkUploadResponse(500);
                }

                var completedItemDto = new DriveItemDto(
                    Id: driveItem.Id!,
                    Name: driveItem.Name!,
                    Size: driveItem.Size,
                    ETag: driveItem.ETag,
                    LastModifiedDateTime: driveItem.LastModifiedDateTime ?? DateTimeOffset.UtcNow,
                    ContentType: driveItem.File?.MimeType,
                    Folder: null
                );

                _logger.LogInformation("Completed upload session {UploadUrl}, item ID: {ItemId}",
                    uploadSessionUrl, completedItemDto.Id);

                return new ChunkUploadResponse(201, completedItemDto);
            }
            else
            {
                _logger.LogWarning("Unexpected response from chunked upload: {StatusCode}", response.StatusCode);
                return new ChunkUploadResponse((int)response.StatusCode);
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Upload chunk operation was cancelled");
            return new ChunkUploadResponse(499);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to upload chunk for session {UploadUrl}", uploadSessionUrl);
            return new ChunkUploadResponse(500);
        }
    }
}
