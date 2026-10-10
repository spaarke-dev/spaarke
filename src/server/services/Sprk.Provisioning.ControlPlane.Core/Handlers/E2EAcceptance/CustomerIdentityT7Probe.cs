// -----------------------------------------------------------------------------
// CustomerIdentityT7Probe.cs
//
// Task 238 (D-14; INCOMING-CUSTOMER-RUNTIME-IDENTITY §1.1–§1.2) — H13 trap T7.
// Task 255 (INCOMING-141 from unified-access-control-r2 task 141) — extended to the
// customer's WORKFORCE TENANT LIST on the same two reads.
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
// OUTCOMES
//   Passed     — both slots: Customer__Id == request.CustomerId (ordinal), AND the
//                settings under WorkforceIdentity__CustomerTenantIds (every child, the
//                bare key included — .NET binds each child into the list) parse to exactly
//                the run's tenant ids (same set, no duplicate, no extra, no unparseable
//                value), none equal to the slot's AzureAd__TenantId on a Model 1 stamp.
//   Failed     — either slot missing / blank / a different Customer__Id (a case-only
//                difference included: the value is canonical by the customerId standard
//                and is never normalised — INCOMING §1.1), two Customer__Id names differing
//                only in case with different values (ambiguous); a workforce list that is
//                missing or differs; or a run that carries no list (created before T255).
//   InfraFault — a request field is missing, an ARM read fails, or the reads
//                exceed H13AcceptanceOptions.TrapVerifierTimeout.
//
// What it is NOT: Customer__Id is defence in depth and an observability handle,
// not the customer boundary — that is the dedicated per-customer resource
// (INCOMING §5). Only Customer__Id and the workforce tenant ids (public identifiers)
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
using Azure.ResourceManager;
using Azure.ResourceManager.AppService;
using Microsoft.Extensions.Options;
using Sprk.Provisioning.ControlPlane.Core.Models;
using Sprk.Provisioning.ControlPlane.Handlers.BulkAppSettings;

namespace Sprk.Provisioning.ControlPlane.Handlers.E2EAcceptance;

/// <summary>
/// H13 trap T7 — both App Service slots carry <c>Customer__Id</c> equal to the run's customerId and (task 255) exactly
/// the run's customer workforce tenant list.
/// </summary>
public sealed class CustomerIdentityT7Probe : ITrapProbe
{
    /// <summary>The app setting the BFF's CustomerIdResolver reads first (config key <c>Customer:Id</c>).</summary>
    internal const string SettingName = "Customer__Id";

    /// <summary>Task 255: the BFF registration's tenant (Spaarke's on a Model 1 stamp).</summary>
    internal const string AzureAdTenantIdSettingName = "AzureAd__TenantId";

    /// <summary>Staging slot name (parity with app-service.bicep and the T1 / T5 probes).</summary>
    internal const string DefaultStagingSlotName = "staging";

    /// <summary>Longest observed value echoed into a diagnostic (the customerId standard allows 8).</summary>
    private const int MaxEchoedValueLength = 64;

    /// <summary>At most this many observed workforce values are echoed per slot.</summary>
    private const int MaxEchoedWorkforceValues = 12;

    /// <inheritdoc/>
    public TrapKind Kind => TrapKind.T7CustomerIdentityExplicit;

    private readonly ArmClient _armClient;
    private readonly TimeSpan _timeout;
    private readonly ILogger<CustomerIdentityT7Probe> _logger;

    public CustomerIdentityT7Probe(
        ArmClient armClient,
        IOptions<H13AcceptanceOptions> options,
        ILogger<CustomerIdentityT7Probe> logger)
    {
        ArgumentNullException.ThrowIfNull(armClient);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);
        _armClient = armClient;
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
        }.Where(f => string.IsNullOrWhiteSpace(f.Value)).Select(f => f.Name).ToList();
        if (missing.Count > 0)
        {
            return new TrapVerificationOutcome.InfraFault(Kind,
                $"T7 probe: request field(s) {string.Join(", ", missing)} empty — cannot locate the App Service " +
                "or know which customerId to expect.");
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

        return Evaluate(request, productionSettings, stagingSettings);
    }

    /// <summary>
    /// The verdict over the two slots' app settings (pure — the ARM reads are above). Internal for the unit tests.
    /// </summary>
    internal TrapVerificationOutcome Evaluate(
        TrapVerificationRequest request,
        IDictionary<string, string>? productionSettings,
        IDictionary<string, string>? stagingSettings)
    {
        var production = FindSetting(productionSettings);
        var staging = FindSetting(stagingSettings);
        var productionShape = Classify(production, request.CustomerId);
        var stagingShape = Classify(staging, request.CustomerId);

        var isModel1 = TenancyModelParser.TryParse(request.TenancyModel, out var model) && model == TenancyModel.Model1;
        var expected = request.CustomerWorkforceTenantIds ?? [];
        var productionWorkforce = CheckWorkforceTenants(productionSettings, expected, isModel1);
        var stagingWorkforce = CheckWorkforceTenants(stagingSettings, expected, isModel1);

        if (productionShape == SlotShape.Ok && stagingShape == SlotShape.Ok
            && productionWorkforce is null && stagingWorkforce is null)
        {
            _logger.LogInformation(
                "T7 probe PASSED: both slots carry Customer__Id={CustomerId} and the {Count} workforce tenant(s). appService={AppService}",
                request.CustomerId, expected.Count, request.AppServiceName);
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
                      "first sign-in of a customer employee (workforce_tenant_list_empty). Start a new run with the value. "
                    : $"[{string.Join(", ", expected)}]. ") +
                $"Observed: production {productionWorkforce ?? "[OK]"}; {DefaultStagingSlotName} {stagingWorkforce ?? "[OK]"}. " +
                "H4b writes the list to both slots and removes stale indices — re-run H4b (a new run if its idempotency key " +
                "is already recorded), then H13.");
        }

        var diagnostic =
            $"T7 customer identity VIOLATED on App Service '{request.AppServiceName}' (rg '{request.ResourceGroupName}'): " +
            string.Join(" ALSO ", problems);
        _logger.LogWarning("T7 probe FAILED: {Diagnostic}", diagnostic);
        return new TrapVerificationOutcome.Failed(Kind, diagnostic);
    }

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
            return $"[MISSING — no {CustomerWorkforceTenantsRule.AppSettingBaseName}__N setting: every first sign-in is denied]";
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
