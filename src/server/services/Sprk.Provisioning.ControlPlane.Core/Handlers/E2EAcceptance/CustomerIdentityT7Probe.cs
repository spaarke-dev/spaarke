// -----------------------------------------------------------------------------
// CustomerIdentityT7Probe.cs
//
// Task 238 (D-14; INCOMING-CUSTOMER-RUNTIME-IDENTITY §1.1–§1.2) — H13 trap T7.
//
// THE TRAP. The BFF answers "which customer am I serving?" from the app setting
// `Customer__Id`, and only when it is absent derives the id from
// WEBSITE_RESOURCE_GROUP, logging a WARNING (Sprk.Bff.Api CustomerIdResolver). A
// stamp running on the derived path "was never finished"; a stamp carrying a
// DIFFERENT customer's id is worse. Both are silent at boot. customer.bicep sets
// the value on the production site only, H4b writes it to BOTH slots, and H9
// swaps staging → production — so the slot that ends up serving traffic is the
// one that must carry it. This probe reads both slots' app settings from ARM and
// passes only when each carries `Customer__Id` exactly equal to the run's
// customerId.
//
// OUTCOMES
//   Passed     — both slots: Customer__Id == request.CustomerId (ordinal).
//   Failed     — either slot missing it, blank, a different value (a case-only
//                difference included: the value is canonical by the customerId
//                standard and is never normalised — INCOMING §1.1), or two names
//                differing only in case with different values (ambiguous: which
//                one the BFF binds is not knowable from here).
//   InfraFault — a request field is missing, an ARM read fails, or the reads
//                exceed H13AcceptanceOptions.TrapVerifierTimeout.
//
// What it is NOT: Customer__Id is defence in depth and an observability handle,
// not the customer boundary — that is the dedicated per-customer resource
// (INCOMING §5). Only the Customer__Id value is ever logged or reported (length-
// capped, control characters escaped); no other app setting is (several are Key
// Vault references).
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

namespace Sprk.Provisioning.ControlPlane.Handlers.E2EAcceptance;

/// <summary>
/// H13 trap T7 — both App Service slots carry <c>Customer__Id</c> equal to the run's customerId.
/// </summary>
public sealed class CustomerIdentityT7Probe : ITrapProbe
{
    /// <summary>The app setting the BFF's CustomerIdResolver reads first (config key <c>Customer:Id</c>).</summary>
    internal const string SettingName = "Customer__Id";

    /// <summary>Staging slot name (parity with app-service.bicep and the T1 / T5 probes).</summary>
    internal const string DefaultStagingSlotName = "staging";

    /// <summary>Longest observed value echoed into a diagnostic (the customerId standard allows 8).</summary>
    private const int MaxEchoedValueLength = 64;

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

        SettingLookup production;
        SettingLookup staging;
        var site = _armClient.GetWebSiteResource(WebSiteResource.CreateResourceIdentifier(
            request.SubscriptionId, request.ResourceGroupName, request.AppServiceName));
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(_timeout);

            var productionSettings = await site.GetApplicationSettingsAsync(timeout.Token).ConfigureAwait(false);
            production = FindSetting(productionSettings.Value.Properties);

            var slot = await site.GetWebSiteSlotAsync(DefaultStagingSlotName, timeout.Token).ConfigureAwait(false);
            var stagingSettings = await slot.Value.GetApplicationSettingsSlotAsync(timeout.Token).ConfigureAwait(false);
            staging = FindSetting(stagingSettings.Value.Properties);
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

        var productionShape = Classify(production, request.CustomerId);
        var stagingShape = Classify(staging, request.CustomerId);
        if (productionShape == SlotShape.Ok && stagingShape == SlotShape.Ok)
        {
            _logger.LogInformation(
                "T7 probe PASSED: both slots carry Customer__Id={CustomerId}. appService={AppService}",
                request.CustomerId, request.AppServiceName);
            return new TrapVerificationOutcome.Passed(Kind);
        }

        var diagnostic =
            $"T7 customer identity VIOLATED on App Service '{request.AppServiceName}' (rg '{request.ResourceGroupName}'): " +
            $"expected both slots to carry {SettingName}='{request.CustomerId}' (the run's customerId, exact). " +
            $"Observed: production {Describe(productionShape, production)}; {DefaultStagingSlotName} {Describe(stagingShape, staging)}. " +
            "A slot without it runs the BFF on the derived-from-resource-group path (or refuses to start); a different " +
            "value names another customer. H4b writes it to both slots — check that H4b ran with this run's customerId. " +
            "A stamp whose H4b completed before T238 never received it: set it on the affected slot(s) with " +
            $"`az webapp config appsettings set -g {request.ResourceGroupName} -n {request.AppServiceName} " +
            $"[--slot {DefaultStagingSlotName}] --settings {SettingName}={request.CustomerId}`, then re-run H13.";
        _logger.LogWarning("T7 probe FAILED: {Diagnostic}", diagnostic);
        return new TrapVerificationOutcome.Failed(Kind, diagnostic);
    }

    /// <summary>
    /// Finds <c>Customer__Id</c> in an app-settings dictionary. The NAME is matched case-insensitively, as .NET
    /// configuration (and so the BFF) binds it; the VALUE is returned as-is. Two names that differ only in case
    /// and carry different values are <see cref="SettingLookup.Ambiguous"/> — which one the BFF binds cannot be
    /// told from here, so the probe must not pass on either.
    /// </summary>
    internal static SettingLookup FindSetting(IDictionary<string, string>? settings)
    {
        var values = (settings ?? new Dictionary<string, string>())
            .Where(kv => string.Equals(kv.Key, SettingName, StringComparison.OrdinalIgnoreCase))
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

    /// <summary>What <see cref="FindSetting"/> found on one slot.</summary>
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
