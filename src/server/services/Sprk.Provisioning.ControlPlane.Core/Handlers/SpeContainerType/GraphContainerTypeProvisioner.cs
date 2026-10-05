// -----------------------------------------------------------------------------
// GraphContainerTypeProvisioner.cs
//
// Task 131 (Wave G-3) — production ISpeContainerTypeProvisioner. Ports the
// retired CreateNewContainerTypeScriptProvisioner.cs (shelled out to the
// task-011-hardened scripts/Create-NewContainerType.ps1 -CreateTestContainer)
// to Microsoft.Graph 6.5.0 (already an L2 dependency as of task 130/H3) under
// ClientCertificateCredential (T6 — confidential-client, cert sourced from KV
// via SpeConfidentialClientGraphFactory; see that file's header for the KV
// cert-bootstrap mechanism + why SecretClient, not CertificateClient, is the
// correct typed client).
//
// GRAPH SDK SHAPES GROUND-TRUTHED VIA REFLECTION (per Wave G-2/G-3 discipline
// — task 123/125/130 precedent) against the installed Microsoft.Graph 6.5.0
// package (lib/net10.0/Microsoft.Graph.dll) BEFORE writing this file. TWO real
// gotchas caught that change the shape of this port versus the retired
// beta-era PS script:
//
//   GOTCHA 1 — `/storage/fileStorage/containerTypes` + `/storage/fileStorage/
//   containers` are GA (v1.0) in the installed SDK (Storage.FileStorage.
//   ContainerTypes / .Containers request builders exist on the DEFAULT
//   GraphServiceClient — no separate Microsoft.Graph.Beta package needed).
//   The retired script called `https://graph.microsoft.com/beta/...` because
//   it predates the GA rollout; the v1.0 SDK surface is used here instead.
//   HOWEVER the GA model.FileStorageContainerType shape DIFFERS from the
//   script's beta JSON body: the beta body used `displayName`/`description`;
//   the GA `Microsoft.Graph.Models.FileStorageContainerType` model exposes
//   only `Name` (no `Description`, no `DisplayName`) alongside `OwningAppId`
//   + `Settings`. `Name` is used here as the sole human-readable label. A
//   SECOND, smaller shape gotcha in the same model: `OwningAppId` is typed
//   `Guid?`, not `string` (unlike Microsoft.Graph.Models.Application.AppId,
//   which IS a string on the SAME package's Applications surface — H3's
//   GraphAppRegistrationProvisioner.cs uses `app.AppId!` as a string
//   directly). `Guid.Parse(request.OwningAppId)` is required at both call
//   sites (FileStorageContainerType.OwningAppId +
//   FileStorageContainerTypeRegistration.OwningAppId) — caught at compile
//   time (CS0029), not via reflection, but worth flagging as a real
//   cross-surface inconsistency within the same SDK version.
//
//   GOTCHA 2 (the bigger one) — the retired script's Step 4 ("register the
//   owning app with FULL delegated+appOnly permissions") called a SEPARATE
//   SharePoint REST endpoint (`PUT https://{spDomain}/_api/v2.1/
//   storageContainerTypes/{id}/applicationPermissions`) under a DIFFERENT
//   token audience (`https://{spDomain}/.default`, not Graph). Reflection
//   confirms the GA Graph SDK does NOT expose that shape on
//   FileStorageContainerType itself (FileStorageContainerTypeAppPermission is
//   an ENUM — None/ReadContent/WriteContent/ManageContent/Create/Delete/Read/
//   Write/Enumerate.../Full/UnknownFutureValue — not a settable permission
//   object). Instead, Graph GA models a NATIVE REPLACEMENT for that exact
//   step: `POST /storage/fileStorage/containerTypeRegistrations` with a
//   `FileStorageContainerTypeRegistration` body carrying
//   `ApplicationPermissionGrants: [{ AppId, ApplicationPermissions: [Full],
//   DelegatedPermissions: [Full] }]`. This means the SharePoint-domain-scoped
//   REST call — and its separate token audience — is GONE in the GA port:
//   the ENTIRE H8 provisioning flow (container-type + owning-app permission
//   grant + root container) now runs through ONE Graph client under ONE T6
//   confidential-client credential. `request.SharePointDomain` is retained on
//   the request record (the handler's parameter guard still requires it — out
//   of this task's scope to remove) but is no longer used by this
//   implementation; kept for audit-log continuity only.
//
// IDEMPOTENCY PARITY NOTE: this port does NOT add a get-or-create existence
// check before either POST (container-type or registration or root
// container) — it mirrors the retired script's own behavior exactly (always
// create). Speculative $filter-based existence lookups against these very
// new (containerTypes/containerTypeRegistrations) GA endpoints are NOT
// ground-truthed (no live tenant available this session) and risk
// introducing an untested new failure mode; adding one is out of this task's
// scope (H8's target state is "port the T6 auth model to the SDK," not
// "redesign H8's resume-after-partial-failure idempotency," which the
// retired script never had either — see H8SpeContainerTypeHandler.cs's own
// Level-3 idempotency, which already no-ops a FULLY successful prior run).
// The ONE targeted exception: the registration POST tolerates an ODataError
// 409 Conflict (treated as "already registered," not a failure) — a
// zero-new-call-shape defensive addition, not a speculative GET.
// RESUME (unified-access-control-r2 task 165, owner round 41 item 1): the
// handler now RECORDS what this call created in the run, immediately, and a
// re-entry never calls ProvisionAsync for a recorded root container. When only
// the container type is recorded (the root container was removed after a
// failed bind, or creation stopped after the type), the request carries
// ExistingContainerTypeId and CreateAsync creates only the root container in
// it; a Failure reports the type it created (CreatedContainerTypeId) so the
// handler records that too. A resume therefore never makes a second container
// type (undeletable, capped per tenant) or a second, orphaned root container.
//
// BUSINESS-UNIT STAMP (unified-access-control-r2 task 165, owner round 35 item 1):
// BindRootContainerAsync / BindNewContainerAsync stamp the root container with
// the customer environment's ROOT business unit (custom property
// Spaarke.Contracts.Spe.SpeContainerBusinessUnitBinding.PropertyName — the one
// C# constant the BFF shares), read it back, and DELETE the container when the
// stamp did not land. The handler calls it after verification. That bind step
// IS unit-tested (GraphContainerTypeProvisionerBindTests: a GraphServiceClient
// over a hand-written fake transport — never Mock<HttpMessageHandler>).
//
// NOT UNIT-TESTED IN THE CI SUITE (real Microsoft.Graph HTTP calls) — parity
// with the established project precedent (GraphAppRegistrationProvisioner.cs,
// GraphRestAppRoleGranter.cs, the retired CreateNewContainerTypeScriptProvisioner.cs
// — each documents this same posture). H8SpeContainerTypeHandlerTests.cs
// substitutes a fake ISpeContainerTypeProvisioner — that is the real coverage
// surface for H8's orchestration logic. The T6 cert-path itself (KV load +
// confidential-client cert-based credential construction — NEVER a secret-
// based credential) IS unit-tested via SpeConfidentialClientGraphFactoryTests.cs against a fake
// SecretClient transport (Azure.Core.Pipeline.HttpClientTransport fake — NOT
// Mock&lt;HttpMessageHandler&gt;, per ADR-038 + Batch G-2/G-3 fake-transport
// discipline).
// -----------------------------------------------------------------------------

using System.Security.Cryptography.X509Certificates;
using Azure.Core;
using Azure.Security.KeyVault.Secrets;
using Microsoft.Extensions.Options;
using Microsoft.Graph;
using Microsoft.Graph.Models;
using Microsoft.Graph.Models.ODataErrors;

namespace Sprk.Provisioning.ControlPlane.Handlers.SpeContainerType;

/// <inheritdoc cref="ISpeContainerTypeProvisioner"/>
public sealed class GraphContainerTypeProvisioner : ISpeContainerTypeProvisioner
{
    private readonly TokenCredential _sharedCredential;
    private readonly SecretClientOptions? _clientOptions;
    private readonly SpeContainerTypeOptions _options;
    private readonly ILogger<GraphContainerTypeProvisioner> _logger;

    /// <summary>Constructs the production provisioner. <paramref name="sharedCredential"/> is L2's own platform UAMI-pinned credential — used ONLY for the T6 cert read from the customer's KV; the Graph calls themselves use a per-request T6 ClientCertificateCredential (see file header).</summary>
    public GraphContainerTypeProvisioner(
        TokenCredential sharedCredential,
        IOptions<SpeContainerTypeOptions> options,
        ILogger<GraphContainerTypeProvisioner> logger)
        : this(sharedCredential, clientOptions: null, options, logger)
    {
    }

    /// <summary>Test seam constructor — injects a fake-transport <see cref="SecretClientOptions"/> for the KV cert read (parity with SecretClientKvWriter.cs's internal test ctor).</summary>
    internal GraphContainerTypeProvisioner(
        TokenCredential sharedCredential,
        SecretClientOptions? clientOptions,
        IOptions<SpeContainerTypeOptions> options,
        ILogger<GraphContainerTypeProvisioner> logger)
    {
        ArgumentNullException.ThrowIfNull(sharedCredential);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);
        _sharedCredential = sharedCredential;
        _clientOptions = clientOptions;
        _options = options.Value;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<SpeContainerTypeProvisionOutcome> ProvisionAsync(
        SpeContainerTypeProvisionRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.CustomerId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.TenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.OwningAppId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.SharePointDomain);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.VaultName);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.CertSecretName);

        // Cert load + Graph client construction happen BEFORE any Graph call —
        // a failure here (KV unreachable, secret missing, bad PFX) has NO
        // external SPE side effect. Left uncaught so it propagates to
        // H8SpeContainerTypeHandler's provisioner-infra-fault catch (Resumable).
        using var cert = await SpeConfidentialClientGraphFactory.LoadCertificateAsync(
            _sharedCredential, _clientOptions, request.VaultName, request.CertSecretName,
            _options.CertLoadTimeout, cancellationToken).ConfigureAwait(false);

        var graph = SpeConfidentialClientGraphFactory.BuildGraphClient(request.TenantId, request.OwningAppId, cert);
        return await CreateAsync(graph, request, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// The Graph part of <see cref="ProvisionAsync"/>: create the container type — UNLESS
    /// <see cref="SpeContainerTypeProvisionRequest.ExistingContainerTypeId"/> names the one this run already created
    /// (unified-access-control-r2 task 165, owner round 41 item 1: a resume never creates a second, undeletable type) —
    /// register the owning app on it, then create the root container in it. A failure after the type exists reports it
    /// (<see cref="SpeContainerTypeProvisionOutcome.Failure.CreatedContainerTypeId"/>) so the handler records it.
    /// Internal so a test can drive it with a <c>GraphServiceClient</c> over a fake transport.
    /// </summary>
    internal async Task<SpeContainerTypeProvisionOutcome> CreateAsync(
        GraphServiceClient graph,
        SpeContainerTypeProvisionRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(request);

        _logger.LogInformation(
            "H8 Graph SPE container-type provisioning starting: customerId={CustomerId} tenantId={TenantId} " +
            "owningAppId={OwningAppId} existingContainerTypeId={ExistingContainerTypeId}",
            request.CustomerId, request.TenantId, request.OwningAppId, request.ExistingContainerTypeId ?? "(none)");

        // The type this call created or reused — reported on every failure after it exists.
        string? containerTypeId = string.IsNullOrWhiteSpace(request.ExistingContainerTypeId)
            ? null
            : request.ExistingContainerTypeId.Trim();
        try
        {
            if (containerTypeId is null)
            {
                // (1) Create the container type (GOTCHA 1 — v1.0 GA shape).
                using var createTypeTimeout = LinkedTimeout(cancellationToken);
                var containerType = await graph.Storage.FileStorage.ContainerTypes.PostAsync(
                    new FileStorageContainerType
                    {
                        Name = request.DisplayName,
                        OwningAppId = Guid.Parse(request.OwningAppId),
                    },
                    cancellationToken: createTypeTimeout.Token).ConfigureAwait(false);

                if (containerType is null || string.IsNullOrWhiteSpace(containerType.Id))
                {
                    return new SpeContainerTypeProvisionOutcome.Failure(
                        $"Graph POST /storage/fileStorage/containerTypes returned no usable Id for customerId " +
                        $"'{request.CustomerId}'.", IsDelegatedTokenTrap: false);
                }

                containerTypeId = containerType.Id;
                _logger.LogInformation(
                    "T6 cleared: container-type ID {ContainerTypeId} created via confidential-client cert-based auth.",
                    containerTypeId);
            }
            else
            {
                _logger.LogInformation(
                    "H8 reuses the container type {ContainerTypeId} this run already created — only a root container is created.",
                    containerTypeId);
            }

            // (2) Register the owning app's FULL permissions on the container
            // type (GOTCHA 2 — replaces the retired script's SharePoint REST
            // applicationPermissions PUT under a DIFFERENT token audience).
            // Non-409 failures propagate to the outer catch (ODataError) below
            // for uniform T6-trap classification. Repeated on a reused type: a
            // 409 is "already registered", and the first attempt may be the one
            // that failed.
            await EnsureRegistrationAsync(graph, containerTypeId, request, cancellationToken)
                .ConfigureAwait(false);

            // (3) Create the root/test container within the container type.
            using var createContainerTimeout = LinkedTimeout(cancellationToken);
            var container = await graph.Storage.FileStorage.Containers.PostAsync(
                new FileStorageContainer
                {
                    DisplayName = $"{request.DisplayName} - Root",
                    Description = "Root container for document storage - owned by BFF API app",
                    ContainerTypeId = Guid.Parse(containerTypeId),
                },
                cancellationToken: createContainerTimeout.Token).ConfigureAwait(false);

            if (container is null || string.IsNullOrWhiteSpace(container.Id))
            {
                return new SpeContainerTypeProvisionOutcome.Failure(
                    $"Graph POST /storage/fileStorage/containers returned no usable Id for containerTypeId " +
                    $"'{containerTypeId}' (customerId '{request.CustomerId}').", IsDelegatedTokenTrap: false,
                    CreatedContainerTypeId: containerTypeId);
            }

            _logger.LogInformation(
                "T6 cleared: container ID {ContainerId} created via confidential-client cert-based auth.",
                container.Id);

            return new SpeContainerTypeProvisionOutcome.Success(new SpeContainerTypeProvisionOutputs(
                ContainerTypeId: containerTypeId,
                RootContainerId: container.Id));
        }
        catch (ODataError ex)
        {
            var isTrap = SpeConfidentialClientGraphFactory.IsDelegatedTokenTrapError(ex);
            _logger.LogError(ex,
                "H8 Graph SPE container-type provisioning ODataError: customerId={CustomerId} status={Status} " +
                "isDelegatedTokenTrap={IsDelegatedTokenTrap} containerTypeId={ContainerTypeId}",
                request.CustomerId, ex.ResponseStatusCode, isTrap, containerTypeId ?? "(none)");
            var diagnostic = isTrap
                ? $"T6 silent-fail trap detected: Graph ODataError {ex.ResponseStatusCode} contains " +
                  $"'{SpeConfidentialClientGraphFactory.DelegatedTokenTrapPhrase}' for customerId " +
                  $"'{request.CustomerId}'. Confidential-client cert-based auth MUST be used for SPE " +
                  $"container-type creation (spec.md FR-33 / T6). {ex.Error?.Code} {ex.Error?.Message}"
                : $"Graph ODataError {ex.ResponseStatusCode}: {ex.Error?.Code} {ex.Error?.Message ?? ex.Message} " +
                  $"(customerId '{request.CustomerId}').";
            return new SpeContainerTypeProvisionOutcome.Failure(
                diagnostic, IsDelegatedTokenTrap: isTrap, CreatedContainerTypeId: containerTypeId);
        }
    }

    /// <inheritdoc/>
    public async Task<SpeContainerBindOutcome> BindRootContainerAsync(
        SpeContainerBindRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.TenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.OwningAppId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.VaultName);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.CertSecretName);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ContainerId);

        // Same T6 confidential-client identity that created the container (cert from the customer KV). A cert-load
        // failure propagates: it happens before any Graph call, so nothing was changed.
        using var cert = await SpeConfidentialClientGraphFactory.LoadCertificateAsync(
            _sharedCredential, _clientOptions, request.VaultName, request.CertSecretName,
            _options.CertLoadTimeout, cancellationToken).ConfigureAwait(false);

        var graph = SpeConfidentialClientGraphFactory.BuildGraphClient(request.TenantId, request.OwningAppId, cert);
        return await BindNewContainerAsync(graph, request.ContainerId, request.BusinessUnitId, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// THE bind step for a container this handler created (unified-access-control-r2 task 165, owner round 35 item 1 —
    /// the same rule the BFF's creation paths follow): <c>PATCH /storage/fileStorage/containers/{id}/customProperties</c>
    /// with the property map as the BODY ROOT (Graph merges, so nothing else is touched), a single-container read-back
    /// (<c>$select=id,customProperties</c> — the collection drops custom properties), and — when the stamp did not land,
    /// for ANY reason — <c>DELETE /storage/fileStorage/containers/{id}</c>, so no unbound container is left behind.
    /// Internal so a test can drive it with a <c>GraphServiceClient</c> over a fake transport.
    /// </summary>
    internal async Task<SpeContainerBindOutcome> BindNewContainerAsync(
        GraphServiceClient graph,
        string containerId,
        Guid businessUnitId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentException.ThrowIfNullOrWhiteSpace(containerId);

        string failure;
        try
        {
            if (businessUnitId == Guid.Empty)
            {
                throw new InvalidOperationException("No owning business unit was resolved (Guid.Empty) — the container cannot be bound.");
            }

            await WriteStampAsync(graph, containerId, businessUnitId, cancellationToken).ConfigureAwait(false);

            using var readTimeout = LinkedTimeout(cancellationToken);
            var container = await graph.Storage.FileStorage.Containers[containerId]
                .GetAsync(config => config.QueryParameters.Select = new[] { "id", "customProperties" }, readTimeout.Token)
                .ConfigureAwait(false);

            var stamped = ReadStamp(container);
            if (stamped == businessUnitId)
            {
                _logger.LogInformation(
                    "H8 root container {ContainerId} bound to business unit {BusinessUnitId} (stamp read back).",
                    containerId, businessUnitId);
                return new SpeContainerBindOutcome.Bound();
            }

            failure = $"The business-unit stamp did not read back on container '{containerId}' (read: " +
                      $"'{stamped?.ToString() ?? "none"}', expected '{businessUnitId}').";
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            failure = ex is ODataError odata
                ? $"Graph ODataError {odata.ResponseStatusCode} binding container '{containerId}': {odata.Error?.Code} {odata.Error?.Message ?? odata.Message}"
                : $"Binding container '{containerId}' failed: {ex.GetType().Name}: {ex.Message}";
        }

        _logger.LogError(
            "H8 root container {ContainerId} could not be bound to business unit {BusinessUnitId} — removing it. {Failure}",
            containerId, businessUnitId, failure);

        try
        {
            using var deleteTimeout = LinkedTimeout(CancellationToken.None);
            await graph.Storage.FileStorage.Containers[containerId].DeleteAsync(cancellationToken: deleteTimeout.Token)
                .ConfigureAwait(false);
            return new SpeContainerBindOutcome.NotBound(failure + " The container was removed.", Removed: true);
        }
        catch (Exception deleteEx)
        {
            _logger.LogCritical(deleteEx,
                "H8 root container {ContainerId} is UNBOUND and could not be removed; no SPE admin route reaches it until " +
                "the backfill binds it (-Bind) or an operator removes it.", containerId);
            return new SpeContainerBindOutcome.NotBound(
                failure + $" Removing it ALSO failed ({deleteEx.GetType().Name}: {deleteEx.Message}); container " +
                $"'{containerId}' is left unbound.", Removed: false);
        }
    }

    /// <summary>
    /// <c>PATCH …/containers/{id}/customProperties</c> with <c>{"&lt;stamp&gt;": {"value": "&lt;unit&gt;", "isSearchable": false}}</c>
    /// as the body root — the shape the BFF sends (<c>SpeAdminGraphService.WriteBusinessUnitStampAsync</c>), sent through
    /// the SDK's request adapter so Graph failures arrive as <see cref="ODataError"/>.
    /// </summary>
    private async Task WriteStampAsync(
        GraphServiceClient graph, string containerId, Guid businessUnitId, CancellationToken cancellationToken)
    {
        var payload = System.Text.Json.JsonSerializer.Serialize(new Dictionary<string, object>
        {
            [Spaarke.Contracts.Spe.SpeContainerBusinessUnitBinding.PropertyName] = new Dictionary<string, object>
            {
                ["value"] = businessUnitId.ToString("D"),
                ["isSearchable"] = false,
            },
        });

        var baseUrl = (graph.RequestAdapter.BaseUrl ?? "https://graph.microsoft.com/v1.0").TrimEnd('/');
        var requestInfo = new Microsoft.Kiota.Abstractions.RequestInformation
        {
            HttpMethod = Microsoft.Kiota.Abstractions.Method.PATCH,
            URI = new Uri($"{baseUrl}/storage/fileStorage/containers/{Uri.EscapeDataString(containerId)}/customProperties"),
        };
        requestInfo.Headers.Add("Accept", "application/json");
        requestInfo.SetStreamContent(new MemoryStream(System.Text.Encoding.UTF8.GetBytes(payload)), "application/json");

        var errorMapping = new Dictionary<string, Microsoft.Kiota.Abstractions.Serialization.ParsableFactory<Microsoft.Kiota.Abstractions.Serialization.IParsable>>
        {
            { "XXX", ODataError.CreateFromDiscriminatorValue },
        };

        using var timeout = LinkedTimeout(cancellationToken);
        await graph.RequestAdapter.SendNoContentAsync(requestInfo, errorMapping, timeout.Token).ConfigureAwait(false);
    }

    /// <summary>The business unit the container's stamp names, or null when it carries none (or not one GUID).</summary>
    private static Guid? ReadStamp(FileStorageContainer? container)
    {
        var properties = container?.CustomProperties?.AdditionalData;
        if (properties is null)
        {
            return null;
        }

        foreach (var (name, raw) in properties)
        {
            if (!string.Equals(name?.Trim(), Spaarke.Contracts.Spe.SpeContainerBusinessUnitBinding.PropertyName,
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (raw is Microsoft.Kiota.Abstractions.Serialization.UntypedObject node
                && node.GetValue().TryGetValue("value", out var valueNode)
                && valueNode is Microsoft.Kiota.Abstractions.Serialization.UntypedString text
                && Guid.TryParse(text.GetValue()?.Trim(), out var unit))
            {
                return unit;
            }

            return null;
        }

        return null;
    }

    /// <summary>
    /// POSTs the owning-app FULL permission grant (GOTCHA 2). Tolerates a 409
    /// Conflict as "already registered" (idempotency parity note — see file
    /// header). Any OTHER ODataError propagates to the caller's outer catch
    /// for uniform T6-trap classification.
    /// </summary>
    private async Task EnsureRegistrationAsync(
        GraphServiceClient graph, string containerTypeId, SpeContainerTypeProvisionRequest request, CancellationToken ct)
    {
        try
        {
            using var timeoutCts = LinkedTimeout(ct);
            await graph.Storage.FileStorage.ContainerTypeRegistrations.PostAsync(
                new FileStorageContainerTypeRegistration
                {
                    Id = containerTypeId,
                    OwningAppId = Guid.Parse(request.OwningAppId),
                    ApplicationPermissionGrants = new List<FileStorageContainerTypeAppPermissionGrant>
                    {
                        new()
                        {
                            AppId = request.OwningAppId,
                            ApplicationPermissions = new List<FileStorageContainerTypeAppPermission?>
                            {
                                FileStorageContainerTypeAppPermission.Full,
                            },
                            DelegatedPermissions = new List<FileStorageContainerTypeAppPermission?>
                            {
                                FileStorageContainerTypeAppPermission.Full,
                            },
                        },
                    },
                },
                cancellationToken: timeoutCts.Token).ConfigureAwait(false);

            _logger.LogInformation(
                "T6 cleared: containerTypeRegistration for {ContainerTypeId} (owning app full permissions) " +
                "created via confidential-client cert-based auth.", containerTypeId);
        }
        catch (ODataError ex) when (ex.ResponseStatusCode == 409)
        {
            _logger.LogInformation(
                "H8 containerTypeRegistration for {ContainerTypeId} already exists (409) — treating as " +
                "already-registered, not a failure.", containerTypeId);
        }
    }

    private CancellationTokenSource LinkedTimeout(CancellationToken ct)
    {
        var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(_options.GraphRequestTimeout);
        return cts;
    }
}
