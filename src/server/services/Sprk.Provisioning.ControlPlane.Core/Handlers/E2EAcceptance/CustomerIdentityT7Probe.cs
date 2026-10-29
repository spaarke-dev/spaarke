// -----------------------------------------------------------------------------
// CustomerIdentityT7Probe.cs
//
// Task 238 (D-14; INCOMING-CUSTOMER-RUNTIME-IDENTITY §1.1–§1.2) — H13 trap T7.
// Task 255 (INCOMING-141 from unified-access-control-r2 task 141) — extended to the
// customer's WORKFORCE TENANT LIST on the same two reads.
// Task 264 (#2001 from SPA-r3 ISS-013) — extended to the BFF app registration's
// removeUnverifiedEmailClaim (one tenant-scoped Graph read).
// Task 265 (#2265 from uac-r2 / SPA-r3; R3 ISS-005 #1673) — extended to the customer business unit on the
// same two slot reads.
//
// THE TRAP (Customer__Id). The BFF answers "which customer am I serving?" from the
// app setting `Customer__Id`, and only when it is absent derives the id from
// WEBSITE_RESOURCE_GROUP, logging a WARNING (Sprk.Bff.Api CustomerIdResolver). A
// stamp running on the derived path "was never finished"; a stamp carrying a
// DIFFERENT customer's id is worse. Both are silent at boot. customer.bicep sets
// the value on the production site only, H4b writes it to BOTH slots, and H9
// swaps staging → production — so the slot that ends up serving traffic is the
// one that must carry it.
//
// THE TRAP (workforce tenants, T255). The BFF admits a first-sign-in email bind or
// contact creation only for a member of a tenant in WorkforceIdentity:CustomerTenantIds;
// an empty or absent list DENIES every customer employee — silently, at sign-in, never at
// boot. A list naming Spaarke's own tenant (a Model 1 stamp's AzureAd__TenantId) would
// bind Spaarke's staff into the customer's environment. H4b writes the run's list to
// both slots and removes stale indices; this probe proves it landed.
//
// THE TRAP (unverified email claim, T264; #2001 from SPA-r3 ISS-013). R3's email-bound
// contact registration (ExternalRegistrationEndpoints.cs / ContactIdentityBinder.cs) binds a
// workforce sign-in to a contact by the token `email` claim. That is safe only when Entra strips
// the claim for an unverified domain, i.e. the stamp's BFF app registration carries
// authenticationBehaviors.removeUnverifiedEmailClaim = true; otherwise a user in an allowed
// customer directory whose unverified-domain email equals a contact's can register as that
// contact — silently, at sign-in. H3 sets it; this clause reads the registration back
// (Graph v1.0 GET applications(appId='{BffAppRegId}') in request.TenantId, a tenant-scoped
// Graph client — invariant I5, never an ambient credential) so a registration H3 never
// reached, one edited since, or one adopted before T264 is caught. The L2 Worker can read it:
// H3 creates the registration and L2 owns it (Application.ReadWrite.OwnedBy) — no new Graph
// permission. The multitenant SPA client's own flag is an operator one-time setting, not read.
//
// THE TRAP (customer business unit, T265; #2265). The BFF's external registration
// (POST /api/v1/external/registration) owns every contact it creates by the app setting
// ExternalAccess__CustomerBusinessUnitId (config key ExternalAccess:CustomerBusinessUnitId): without it the
// endpoint answers 500 registration_owner_unconfigured, and R3's first-sign-in contact creates (#1673) are
// refused; a DIFFERENT unit would own the customer's contacts in the wrong place. Both are silent at boot.
// H10 creates the customer's unit (a direct child of the environment's root, sibling of the Secure Record
// unit — refused structurally at H10, so this clause does not look the unit up in Dataverse); H4b writes its
// id ("D"-format GUID, InterStepState.CustomerBusinessUnitId) to BOTH slots. This clause reads both back and
// compares them as GUIDs (hex case is irrelevant). Only the GUIDs — public identifiers — are ever echoed.
//
// OUTCOMES
//   Passed     — both slots: Customer__Id == request.CustomerId (ordinal), AND the
//                settings under WorkforceIdentity__CustomerTenantIds (every child, the
//                bare key included — .NET binds each child into the list) parse to exactly
//                the run's tenant ids (same set, no duplicate, no extra, no unparseable
//                value), none equal to the slot's AzureAd__TenantId on a Model 1 stamp;
//                AND the BFF app registration's removeUnverifiedEmailClaim is true;
//                AND both slots: ExternalAccess__CustomerBusinessUnitId parses as a GUID equal to the
//                run's InterStepState.CustomerBusinessUnitId (the name is matched case-insensitively,
//                like Customer__Id).
//   Failed     — either slot missing / blank / a different Customer__Id (a case-only
//                difference included: the value is canonical by the customerId standard
//                and is never normalised — INCOMING §1.1), two Customer__Id names differing
//                only in case with different values (ambiguous); a workforce list that is
//                missing or differs; or a run that carries no list (created before T255);
//                the BFF app registration's removeUnverifiedEmailClaim null / false / absent
//                (authenticationBehaviors itself null), or the registration not found (404);
//                either slot's ExternalAccess__CustomerBusinessUnitId missing / blank / not a GUID /
//                a different GUID, two names differing only in case with different values (ambiguous),
//                or a run that carries no H10 customer business unit (blank / not a GUID).
//   InfraFault — a request field (incl. BffAppRegId, TenantId) is missing or unparseable, an
//                ARM or Graph read fails (ODataError, 401/403/5xx, timeout), or the reads
//                exceed H13AcceptanceOptions.TrapVerifierTimeout. Never Passed on a read failure.
//
// What it is NOT: Customer__Id is defence in depth and an observability handle,
// not the customer boundary — that is the dedicated per-customer resource
// (INCOMING §5). Only Customer__Id, the workforce tenant ids and the customer business unit id (public identifiers)
// are ever logged or reported (length-capped, control characters escaped); no other
// app setting is (several are Key Vault references).
//
// Read pattern: KeyVaultReferenceIdentityT1Probe (ArmClient, site + "staging"
// slot). The two list operations (site and slot config/appsettings/list) need
// Microsoft.Web/sites/config/list/action — Website Contributor, not Reader — which
// the L2 identity holds on the customer BFF (customer-l2-bff-rbac.bicep); H9's
// ArmSlotStickyAppSettingWriter already issues the slot one.
// -----------------------------------------------------------------------------

using System.Text;
using Azure;
using Azure.Identity;
using Azure.ResourceManager;
using Azure.ResourceManager.AppService;
using Microsoft.Extensions.Options;
using Microsoft.Graph;
using Microsoft.Graph.Models;
using Microsoft.Graph.Models.ODataErrors;
using Sprk.Provisioning.ControlPlane.Core.Models;
using Sprk.Provisioning.ControlPlane.Handlers.BulkAppSettings;

namespace Sprk.Provisioning.ControlPlane.Handlers.E2EAcceptance;

/// <summary>
/// H13 trap T7 — both App Service slots carry <c>Customer__Id</c> equal to the run's customerId and (task 255) exactly
/// the run's customer workforce tenant list, and (task 264) the stamp's BFF app registration has
/// <c>authenticationBehaviors.removeUnverifiedEmailClaim = true</c>, and (task 265) both slots carry
/// <c>ExternalAccess__CustomerBusinessUnitId</c> equal to H10's customer business unit.
/// </summary>
public sealed class CustomerIdentityT7Probe : ITrapProbe
{
    /// <summary>The app setting the BFF's CustomerIdResolver reads first (config key <c>Customer:Id</c>).</summary>
    internal const string SettingName = "Customer__Id";

    /// <summary>Task 255: the BFF registration's tenant (Spaarke's on a Model 1 stamp).</summary>
    internal const string AzureAdTenantIdSettingName = "AzureAd__TenantId";

    /// <summary>Task 265: the app setting the BFF binds to <c>ExternalAccess:CustomerBusinessUnitId</c> (H4b writes it from H10's output).</summary>
    internal const string CustomerBusinessUnitSettingName = "ExternalAccess__CustomerBusinessUnitId";

    /// <summary>Staging slot name (parity with app-service.bicep and the T1 / T5 probes).</summary>
    internal const string DefaultStagingSlotName = "staging";

    /// <summary>Longest observed value echoed into a diagnostic (the customerId standard allows 8).</summary>
    private const int MaxEchoedValueLength = 64;

    /// <summary>At most this many observed workforce values are echoed per slot.</summary>
    private const int MaxEchoedWorkforceValues = 12;

    /// <inheritdoc/>
    public TrapKind Kind => TrapKind.T7CustomerIdentityExplicit;

    private static readonly string[] GraphDefaultScope = ["https://graph.microsoft.com/.default"];

    private readonly ArmClient _armClient;
    private readonly Func<string, GraphServiceClient> _tenantScopedGraphFactory;
    private readonly TimeSpan _timeout;
    private readonly ILogger<CustomerIdentityT7Probe> _logger;

    /// <summary>
    /// Production constructor (the only public one, so DI needs no hand-built registration). The Graph client is built per
    /// call against the run's tenant — <c>new DefaultAzureCredential(new DefaultAzureCredentialOptions { TenantId = tenantId })</c>,
    /// the pattern GraphRestAppRoleGranter (H10), GraphAppRegistrationProvisioner (H3) and the T3 probe establish (I5).
    /// </summary>
    public CustomerIdentityT7Probe(
        ArmClient armClient,
        IOptions<H13AcceptanceOptions> options,
        ILogger<CustomerIdentityT7Probe> logger)
        : this(armClient, options, logger, tenantId => new GraphServiceClient(
            new DefaultAzureCredential(new DefaultAzureCredentialOptions { TenantId = tenantId }), GraphDefaultScope))
    {
    }

    /// <summary>
    /// Testing seam (internal so DI keeps a single public constructor): a Graph client factory, so unit tests can supply a
    /// client over a hand-rolled <see cref="HttpMessageHandler"/> and assert the factory received the run's tenant id.
    /// </summary>
    internal CustomerIdentityT7Probe(
        ArmClient armClient,
        IOptions<H13AcceptanceOptions> options,
        ILogger<CustomerIdentityT7Probe> logger,
        Func<string, GraphServiceClient> tenantScopedGraphFactory)
    {
        ArgumentNullException.ThrowIfNull(armClient);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(tenantScopedGraphFactory);
        _armClient = armClient;
        _tenantScopedGraphFactory = tenantScopedGraphFactory;
        _timeout = options.Value.TrapVerifierTimeout;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<TrapVerificationOutcome> ProbeAsync(
        TrapVerificationRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var missing = new[]
        {
            (Name: nameof(request.CustomerId), Value: request.CustomerId),
            (Name: nameof(request.SubscriptionId), Value: request.SubscriptionId),
            (Name: nameof(request.ResourceGroupName), Value: request.ResourceGroupName),
            (Name: nameof(request.AppServiceName), Value: request.AppServiceName),
            (Name: nameof(request.TenantId), Value: request.TenantId),
            (Name: nameof(request.BffAppRegId), Value: request.BffAppRegId),
        }.Where(f => string.IsNullOrWhiteSpace(f.Value)).Select(f => f.Name).ToList();
        if (missing.Count > 0)
        {
            return new TrapVerificationOutcome.InfraFault(Kind,
                $"T7 probe: request field(s) {string.Join(", ", missing)} empty — cannot locate the App Service, " +
                "the BFF app registration (and its tenant), or know which customerId to expect.");
        }
        if (!Guid.TryParse(request.BffAppRegId, out _))
        {
            return new TrapVerificationOutcome.InfraFault(Kind,
                $"T7 probe: request field {nameof(request.BffAppRegId)} is not a GUID — cannot read the BFF app registration.");
        }

        IDictionary<string, string> productionSettings;
        IDictionary<string, string> stagingSettings;
        var site = _armClient.GetWebSiteResource(WebSiteResource.CreateResourceIdentifier(
            request.SubscriptionId, request.ResourceGroupName, request.AppServiceName));
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(_timeout);

            var production = await site.GetApplicationSettingsAsync(timeout.Token).ConfigureAwait(false);
            productionSettings = production.Value.Properties;

            var slot = await site.GetWebSiteSlotAsync(DefaultStagingSlotName, timeout.Token).ConfigureAwait(false);
            var staging = await slot.Value.GetApplicationSettingsSlotAsync(timeout.Token).ConfigureAwait(false);
            stagingSettings = staging.Value.Properties;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw; // Caller-driven cancellation propagates.
        }
        catch (OperationCanceledException)
        {
            return new TrapVerificationOutcome.InfraFault(Kind,
                $"T7 verdict deferred: reading app settings of App Service '{request.AppServiceName}' exceeded " +
                $"the trap probe timeout {_timeout}. Re-run H13 once ARM responds.");
        }
        catch (RequestFailedException ex)
        {
            _logger.LogWarning(ex,
                "T7 probe InfraFault — ARM app-settings read failed: customerId={CustomerId} appService={AppService} status={Status}",
                request.CustomerId, request.AppServiceName, ex.Status);
            return new TrapVerificationOutcome.InfraFault(Kind,
                $"T7 verdict deferred: reading app settings of App Service '{request.AppServiceName}' " +
                $"(rg '{request.ResourceGroupName}') failed with HTTP {ex.Status} {ex.ErrorCode ?? "(no code)"}. " +
                "The list operation needs Website Contributor (Reader is not enough) on the App Service, and the " +
                $"'{DefaultStagingSlotName}' slot must exist. Re-run H13 after fixing.");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "T7 probe InfraFault — unexpected exception reading app settings: customerId={CustomerId} type={ExType}",
                request.CustomerId, ex.GetType().Name);
            return new TrapVerificationOutcome.InfraFault(Kind,
                $"T7 verdict deferred: unexpected {ex.GetType().Name} reading App Service '{request.AppServiceName}' app settings.");
        }

        // Task 264: the BFF app registration's removeUnverifiedEmailClaim (tenant-scoped Graph read, I5).
        string? unverifiedEmailProblem;
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(_timeout);

            var graph = _tenantScopedGraphFactory(request.TenantId);
            var application = await graph.ApplicationsWithAppId(request.BffAppRegId)
                .GetAsync(rc => rc.QueryParameters.Select = ["id", "appId", "authenticationBehaviors"], timeout.Token)
                .ConfigureAwait(false);
            unverifiedEmailProblem = application is null
                ? $"Graph returned nothing for the BFF app registration (appId '{request.BffAppRegId}')."
                : EvaluateUnverifiedEmailClaim(application.AuthenticationBehaviors);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw; // Caller-driven cancellation propagates.
        }
        catch (OperationCanceledException)
        {
            return SettingsVerdictOrDeferral(request, productionSettings, stagingSettings,
                $"T7 verdict deferred: reading the BFF app registration '{request.BffAppRegId}' exceeded the trap " +
                $"probe timeout {_timeout}. Re-run H13 once Graph responds.");
        }
        catch (ODataError ex) when (ex.ResponseStatusCode == 404)
        {
            // The registration H3 should have created is not in the run's tenant: a definite defect, not a read fault.
            unverifiedEmailProblem =
                $"the BFF app registration (appId '{request.BffAppRegId}') was not found in tenant '{request.TenantId}' " +
                "(HTTP 404) — H3's registration is missing or the run's BffAppRegId is stale. Start a new run (H3's idempotency key is already " +
                "recorded on this one), then H13.";
        }
        catch (ODataError ex)
        {
            _logger.LogWarning(ex,
                "T7 probe InfraFault — Graph application read failed: customerId={CustomerId} appId={AppId} status={Status}",
                request.CustomerId, request.BffAppRegId, ex.ResponseStatusCode);
            return SettingsVerdictOrDeferral(request, productionSettings, stagingSettings,
                $"T7 verdict deferred: reading the BFF app registration '{request.BffAppRegId}' in tenant " +
                $"'{request.TenantId}' failed with HTTP {ex.ResponseStatusCode} {ex.Error?.Code ?? "(no code)"}. " +
                "The L2 identity needs to read applications it owns (Application.ReadWrite.OwnedBy). Re-run H13 after fixing.");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "T7 probe InfraFault — unexpected exception reading the BFF app registration: customerId={CustomerId} type={ExType}",
                request.CustomerId, ex.GetType().Name);
            return SettingsVerdictOrDeferral(request, productionSettings, stagingSettings,
                $"T7 verdict deferred: unexpected {ex.GetType().Name} reading the BFF app registration '{request.BffAppRegId}' in tenant '{request.TenantId}'.");
        }

        return Evaluate(request, productionSettings, stagingSettings, unverifiedEmailProblem);
    }

    /// <summary>
    /// Task 264: the app-registration read failed, so the claim clause has no verdict. A definite settings failure is still
    /// reported (Failed, QuarantineRequired — noting the unread clause); otherwise the trap is deferred (InfraFault).
    /// </summary>
    private TrapVerificationOutcome SettingsVerdictOrDeferral(
        TrapVerificationRequest request,
        IDictionary<string, string> productionSettings,
        IDictionary<string, string> stagingSettings,
        string deferral)
        => Evaluate(request, productionSettings, stagingSettings, unverifiedEmailProblem: null, logPass: false) is TrapVerificationOutcome.Failed failed
            ? failed with { Diagnostic = failed.Diagnostic + " (The BFF app registration clause was not read: " + deferral + ")" }
            : new TrapVerificationOutcome.InfraFault(Kind, deferral);

    /// <summary>
    /// Task 264: null when the BFF app registration strips unverified-domain emails
    /// (<c>authenticationBehaviors.removeUnverifiedEmailClaim == true</c>); otherwise what is wrong. A null
    /// <c>authenticationBehaviors</c> (the property Graph returns for a registration that never set it), a false value and
    /// an absent flag all count as not set. Pure and internal for the unit tests.
    /// </summary>
    internal static string? EvaluateUnverifiedEmailClaim(AuthenticationBehaviors? behaviors)
        => behaviors?.RemoveUnverifiedEmailClaim == true
            ? null
            : behaviors is null
                ? "the BFF app registration has no authenticationBehaviors (removeUnverifiedEmailClaim never set)."
                : $"the BFF app registration has authenticationBehaviors.removeUnverifiedEmailClaim = " +
                  $"{(behaviors.RemoveUnverifiedEmailClaim is null ? "absent" : "false")}.";

    /// <summary>
    /// The verdict over the two slots' app settings and the app registration's claim problem (pure — the ARM and Graph
    /// reads are above). Internal for the unit tests.
    /// </summary>
    internal TrapVerificationOutcome Evaluate(
        TrapVerificationRequest request,
        IDictionary<string, string>? productionSettings,
        IDictionary<string, string>? stagingSettings,
        string? unverifiedEmailProblem,
        bool logPass = true)
    {
        var production = FindSetting(productionSettings);
        var staging = FindSetting(stagingSettings);
        var productionShape = Classify(production, request.CustomerId);
        var stagingShape = Classify(staging, request.CustomerId);

        var isModel1 = TenancyModelParser.TryParse(request.TenancyModel, out var model) && model == TenancyModel.Model1;
        var expected = request.CustomerWorkforceTenantIds ?? [];
        var productionWorkforce = CheckWorkforceTenants(productionSettings, expected, isModel1);
        var stagingWorkforce = CheckWorkforceTenants(stagingSettings, expected, isModel1);

        var productionBusinessUnit = CheckCustomerBusinessUnit(productionSettings, request.CustomerBusinessUnitId);
        var stagingBusinessUnit = CheckCustomerBusinessUnit(stagingSettings, request.CustomerBusinessUnitId);

        if (productionShape == SlotShape.Ok && stagingShape == SlotShape.Ok
            && productionWorkforce is null && stagingWorkforce is null
            && productionBusinessUnit is null && stagingBusinessUnit is null
            && unverifiedEmailProblem is null)
        {
            if (logPass)
            {
                _logger.LogInformation(
                    "T7 probe PASSED: both slots carry Customer__Id={CustomerId}, the {Count} workforce tenant(s) and the customer business unit {BusinessUnitId}, and the BFF app registration removes unverified emails. appService={AppService}",
                    request.CustomerId, expected.Count, request.CustomerBusinessUnitId.Trim(), request.AppServiceName);
            }
            return new TrapVerificationOutcome.Passed(Kind);
        }

        var problems = new List<string>();
        if (productionShape != SlotShape.Ok || stagingShape != SlotShape.Ok)
        {
            problems.Add(
                $"expected both slots to carry {SettingName}='{request.CustomerId}' (the run's customerId, exact). " +
                $"Observed: production {Describe(productionShape, production)}; {DefaultStagingSlotName} {Describe(stagingShape, staging)}. " +
                "A slot without it runs the BFF on the derived-from-resource-group path (or refuses to start); a different " +
                "value names another customer. H4b writes it to both slots — check that H4b ran with this run's customerId. " +
                "A stamp whose H4b completed before T238 never received it: set it on the affected slot(s) with " +
                $"`az webapp config appsettings set -g {request.ResourceGroupName} -n {request.AppServiceName} " +
                $"[--slot {DefaultStagingSlotName}] --settings {SettingName}={request.CustomerId}`, then re-run H13.");
        }
        if (productionWorkforce is not null || stagingWorkforce is not null)
        {
            problems.Add(
                $"expected both slots to carry exactly the run's customer workforce tenants as " +
                $"{CustomerWorkforceTenantsRule.AppSettingBaseName}__N " +
                (expected.Count == 0
                    ? "— but this run carries no customerWorkforceTenantIds (it predates T255): the stamp denies every " +
                      "sign-in of a customer employee without an enabled systemuser (workforce_tenant_list_empty). Start a new run with the value. "
                    : $"[{string.Join(", ", expected)}]. ") +
                $"Observed: production {productionWorkforce ?? "[OK]"}; {DefaultStagingSlotName} {stagingWorkforce ?? "[OK]"}. " +
                "H4b writes the list to both slots and removes stale indices — re-run H4b (a new run if its idempotency key " +
                "is already recorded), then H13.");
        }
        if (productionBusinessUnit is not null || stagingBusinessUnit is not null)
        {
            var runHasUnit = TryParseBusinessUnit(request.CustomerBusinessUnitId, out var expectedUnit);
            problems.Add(
                $"expected both slots to carry {CustomerBusinessUnitSettingName} = " +
                (runHasUnit
                    ? $"'{expectedUnit:D}' (H10's customer business unit). "
                    : "H10's customer business unit — but this run carries no InterStepState.CustomerBusinessUnitId (blank or not a GUID): " +
                      "H10 did not record it, or the run predates T259. ") +
                $"Observed: production {productionBusinessUnit ?? "[OK]"}; {DefaultStagingSlotName} {stagingBusinessUnit ?? "[OK]"}. " +
                "Without it the BFF's external registration answers 500 registration_owner_unconfigured and first-sign-in " +
                "contact creates are refused (R3 #1673); a different unit would own the customer's contacts in the wrong place. " +
                "H4b writes it from H10's output to both slots — start a new run (a resume does not re-run a completed H4b)" +
                (runHasUnit
                    ? $", or set it on the affected slot(s) with `az webapp config appsettings set -g {request.ResourceGroupName} " +
                      $"-n {request.AppServiceName} [--slot {DefaultStagingSlotName}] --settings {CustomerBusinessUnitSettingName}={expectedUnit:D}`, " +
                      "then re-run H13."
                    : ". H10 never recorded the unit on this run, so only a new run fixes it."));
        }
        if (unverifiedEmailProblem is not null)
        {
            problems.Add(
                $"expected the BFF app registration (appId '{request.BffAppRegId}', tenant '{request.TenantId}') to carry " +
                $"authenticationBehaviors.removeUnverifiedEmailClaim = true, but {unverifiedEmailProblem} Without it a user in an " +
                "allowed customer directory whose unverified-domain email equals a contact's can register as that contact " +
                "(email-bound contact registration). Set it by hand per docs/guides/SPAARKE-CUSTOMER-DEPLOYMENT-GUIDE.md (H3 " +
                "section; a resume does not re-run H3 once its idempotency key is recorded), or start a new run (H3 sets it on " +
                "new and existing registrations), then re-run H13.");
        }

        var diagnostic =
            $"T7 customer identity VIOLATED on App Service '{request.AppServiceName}' (rg '{request.ResourceGroupName}'): " +
            string.Join(" ALSO ", problems);
        _logger.LogWarning("T7 probe FAILED: {Diagnostic}", diagnostic);
        return new TrapVerificationOutcome.Failed(Kind, diagnostic);
    }

    /// <summary>
    /// Task 265: null when the slot carries <see cref="CustomerBusinessUnitSettingName"/> as a GUID equal to
    /// <paramref name="expected"/> (the run's H10 customer business unit, compared as GUIDs — hex case is irrelevant);
    /// otherwise a bracketed description of what is wrong. A blank or unparseable <paramref name="expected"/> is itself a
    /// problem (the run carries no H10 unit). The setting NAME is matched like <c>Customer__Id</c> (case-insensitive; two
    /// case-variant names with different values are ambiguous). Only this setting's value is echoed.
    /// </summary>
    internal static string? CheckCustomerBusinessUnit(IDictionary<string, string>? settings, string? expected)
    {
        if (!TryParseBusinessUnit(expected, out var expectedUnit))
        {
            return "[NO EXPECTED UNIT — the run carries no H10 customer business unit to compare against]";
        }

        var lookup = FindSetting(settings, CustomerBusinessUnitSettingName);
        if (lookup.Ambiguous)
        {
            return $"[AMBIGUOUS — several {CustomerBusinessUnitSettingName} names differing only in case, with different values]";
        }
        if (lookup.Value is null)
        {
            return $"[MISSING — no {CustomerBusinessUnitSettingName} setting]";
        }
        if (string.IsNullOrWhiteSpace(lookup.Value))
        {
            return $"[BLANK — {CustomerBusinessUnitSettingName} is set but empty]";
        }
        if (!Guid.TryParse(lookup.Value.Trim(), out var observed))
        {
            return $"[UNPARSEABLE — {CustomerBusinessUnitSettingName}='{Sanitize(lookup.Value)}' is not a GUID]";
        }
        return observed == expectedUnit ? null : $"[MISMATCH — {CustomerBusinessUnitSettingName}='{observed:D}']";
    }

    private static bool TryParseBusinessUnit(string? value, out Guid unit)
        => Guid.TryParse(value?.Trim(), out unit) && unit != Guid.Empty;

    /// <summary>
    /// Task 255: null when the slot's workforce tenant settings are exactly <paramref name="expected"/>; otherwise a
    /// bracketed description of what is wrong. Every setting under the list key counts (the bare key or any child,
    /// case-insensitive, <c>:</c> read as <c>__</c> — <see cref="ArmAppServiceSettingsWriter.IsUnderListKey"/>, the rule
    /// H4b removes stale indices by), because .NET configuration binds each one into the BFF's list.
    /// </summary>
    internal static string? CheckWorkforceTenants(
        IDictionary<string, string>? settings, IReadOnlyList<string> expected, bool isModel1)
    {
        ArgumentNullException.ThrowIfNull(expected);
        var values = (settings ?? new Dictionary<string, string>())
            .Where(kv => ArmAppServiceSettingsWriter.IsUnderListKey(kv.Key, CustomerWorkforceTenantsRule.AppSettingBaseName))
            .OrderBy(kv => kv.Key, StringComparer.Ordinal)
            .Select(kv => kv.Value)
            .ToList();

        if (values.Count == 0)
        {
            return $"[MISSING — no {CustomerWorkforceTenantsRule.AppSettingBaseName}__N setting: every customer-employee sign-in without an enabled systemuser is denied]";
        }

        var parsed = new List<Guid>(values.Count);
        foreach (var value in values)
        {
            if (!Guid.TryParse(value?.Trim(), out var tenant) || tenant == Guid.Empty)
            {
                return $"[UNPARSEABLE — {Echo(values)}: the BFF refuses to start on a non-GUID or all-zero value]";
            }
            parsed.Add(tenant);
        }

        if (isModel1)
        {
            // ISS-021: the guard needs the slot's registration tenant. H4b writes AzureAd__TenantId as a plain GUID (the
            // manifest's per_env_settings entry overrides the TenantId secret's KV reference); a Key Vault reference,
            // blank or missing value cannot be read from ARM app settings (resolving it would need Key Vault data-plane
            // access this probe does not have), so the guard cannot run. Never a silent pass: fail closed.
            var registration = FindSetting(settings, AzureAdTenantIdSettingName);
            if (registration.Ambiguous
                || !Guid.TryParse(registration.Value?.Trim(), out var registrationTenant)
                || registrationTenant == Guid.Empty)
            {
                return $"[UNRESOLVABLE — {AzureAdTenantIdSettingName} is " +
                       (registration.Ambiguous ? "set under several case-variant names" :
                        registration.Value is null ? "missing" :
                        $"not a plain tenant GUID ('{Sanitize(registration.Value)}' — a Key Vault reference?)") +
                       ", so the Model 1 check that the list does not name Spaarke's own tenant cannot run. H4b writes a plain " +
                       $"GUID; re-run H4b so the slot carries {AzureAdTenantIdSettingName}=<the run's tenantId>]";
            }
            if (parsed.Contains(registrationTenant))
            {
                return $"[SPAARKE TENANT — {Echo(values)} lists the slot's {AzureAdTenantIdSettingName} {registrationTenant:D}: " +
                       "on a Model 1 stamp that is Spaarke's tenant, and it would bind Spaarke's staff into this customer's environment]";
            }
        }

        var expectedIds = expected
            .Select(e => Guid.TryParse(e?.Trim(), out var g) ? g : Guid.Empty)
            .ToList();
        var sameSet = parsed.Count == expectedIds.Count
            && parsed.Distinct().Count() == parsed.Count
            && parsed.ToHashSet().SetEquals(expectedIds);
        return sameSet ? null : $"[MISMATCH — {Echo(values)}]";
    }

    /// <summary>
    /// Finds <c>Customer__Id</c> in an app-settings dictionary. The NAME is matched case-insensitively, as .NET
    /// configuration (and so the BFF) binds it; the VALUE is returned as-is. Two names that differ only in case
    /// and carry different values are <see cref="SettingLookup.Ambiguous"/> — which one the BFF binds cannot be
    /// told from here, so the probe must not pass on either.
    /// </summary>
    internal static SettingLookup FindSetting(IDictionary<string, string>? settings)
        => FindSetting(settings, SettingName);

    private static SettingLookup FindSetting(IDictionary<string, string>? settings, string name)
    {
        var values = (settings ?? new Dictionary<string, string>())
            .Where(kv => string.Equals(kv.Key, name, StringComparison.OrdinalIgnoreCase))
            .Select(kv => kv.Value)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        return values.Count switch
        {
            0 => new SettingLookup(null, Ambiguous: false),
            1 => new SettingLookup(values[0], Ambiguous: false),
            _ => new SettingLookup(null, Ambiguous: true),
        };
    }

    /// <summary>What <see cref="FindSetting(IDictionary{string, string}?)"/> found on one slot.</summary>
    internal sealed record SettingLookup(string? Value, bool Ambiguous);

    private static SlotShape Classify(SettingLookup lookup, string expectedCustomerId) => lookup switch
    {
        { Ambiguous: true } => SlotShape.Ambiguous,
        { Value: null } => SlotShape.Missing,
        { Value: var v } when string.IsNullOrWhiteSpace(v) => SlotShape.Blank,
        { Value: var v } when string.Equals(v, expectedCustomerId, StringComparison.Ordinal) => SlotShape.Ok,
        _ => SlotShape.Mismatch,
    };

    private static string Describe(SlotShape shape, SettingLookup lookup) => shape switch
    {
        SlotShape.Ok => "[OK]",
        SlotShape.Missing => $"[MISSING — no {SettingName} setting]",
        SlotShape.Blank => $"[BLANK — {SettingName} is set but empty]",
        SlotShape.Mismatch => $"[MISMATCH — {SettingName}='{Sanitize(lookup.Value)}']",
        SlotShape.Ambiguous => $"[AMBIGUOUS — several {SettingName} names differing only in case, with different values]",
        _ => "[unknown]",
    };

    private static string Echo(IReadOnlyList<string> values)
        => string.Join(", ", values.Take(MaxEchoedWorkforceValues).Select(v => $"'{Sanitize(v)}'"))
           + (values.Count > MaxEchoedWorkforceValues ? $" (+{values.Count - MaxEchoedWorkforceValues} more)" : string.Empty);

    /// <summary>The observed value is free text from ARM: cap its length and escape control characters before echoing it.</summary>
    private static string Sanitize(string? value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        var builder = new StringBuilder();
        foreach (var c in value.Length > MaxEchoedValueLength ? value[..MaxEchoedValueLength] : value)
        {
            builder.Append(char.IsControl(c) ? $"\\u{(int)c:x4}" : c.ToString());
        }
        return value.Length > MaxEchoedValueLength ? builder.Append("…").ToString() : builder.ToString();
    }

    private enum SlotShape
    {
        Ok = 0,
        Missing = 1,
        Blank = 2,
        Mismatch = 3,
        Ambiguous = 4,
    }
}
