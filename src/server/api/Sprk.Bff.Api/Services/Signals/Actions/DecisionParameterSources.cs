using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.Cache;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Services.Access;

namespace Sprk.Bff.Api.Services.Signals.Actions;

/// <summary>What a budget's currency resolved to: the currency and its ISO code, or why it could not be (a validation message).</summary>
public sealed record BudgetCurrency(Guid? Id, string? IsoCode, string? Refusal)
{
    public bool IsResolved => Id is not null && !string.IsNullOrWhiteSpace(IsoCode);
}

/// <summary>The data the plan route adds to a plan's parameters: the resolved option lists, keyed by their
/// <see cref="DecisionActionParameter.OptionsSource"/>, and the starting values, keyed by their
/// <see cref="DecisionActionParameter.DefaultSource"/>.</summary>
public sealed record DecisionParameterFacts(
    IReadOnlyDictionary<string, IReadOnlyList<DecisionParameterOption>> OptionsBySource,
    IReadOnlyDictionary<string, string> DefaultsBySource)
{
    public static readonly DecisionParameterFacts None = new(
        new Dictionary<string, IReadOnlyList<DecisionParameterOption>>(), new Dictionary<string, string>());
}

/// <summary>
/// Task 167 (D-193): the ONE resolver of a catalog parameter's named sources, used by the plan route (to give the wizard its option
/// lists and starting values) and by the Create budget executor (to check a value against the same lists and to resolve the same
/// default), so the wizard and the commit can never disagree.
/// </summary>
/// <remarks>
/// <para><b>Sources.</b> <c>metadata:{table}.{column}</c> is a picklist's CURRENT options and labels, read from live metadata (an owner's
/// relabel shows up within <see cref="OptionsTtl"/>; nothing is hard-coded); <c>currency:active</c> is the active
/// <c>transactioncurrency</c> rows. The default <c>matter-business-unit-currency</c> is the Default Currency of the business unit that
/// owns the core matter, else the organization base currency (logged). D-198 (task 167c): a SECURE matter is owned by the Secure Record
/// business unit, which has no Default Currency, so its default is the Default Currency of the deployment customer business unit
/// (<c>ExternalAccess:CustomerBusinessUnitId</c>), else the base currency when that unit is unset, invalid (absent, the Secure Record unit
/// or the root) or has none. "Secure" is the uac-r2 rule (<c>SecureChildShareSynchronizer.ResolveSecureBusinessUnitAsync</c> with
/// <c>SecureRecordOwnerTeam.IsInSecureBusinessUnit</c>); an ambiguous or unresolved Secure Record unit means not secure. A non-secure
/// matter never reads the customer unit.</para>
/// <para><b>Reads.</b> Metadata is read as the caller (the caller's own client, as the child-records route reads it). So is (d), the
/// owning business unit of the core matter: it is record data, and a caller who cannot read the matter gets the organization base currency,
/// never an app-only read of the matter (uac-r2: record data is read as the caller wherever the caller can read it). The reads (a)-(c) are
/// app-only by the BFF's own reader, because they are organization configuration, not record content: the active <c>transactioncurrency</c>
/// rows, <c>organization.basecurrencyid</c>, <c>businessunit.transactioncurrencyid</c> (a Core User's Read on <c>businessunit</c> is not a
/// fact the Create budget needs to hold) and, for D-198, the Secure Record unit id and the customer unit currency and parent. The binding of the currency onto the budget is still checked as the caller by the child-records
/// create core (AppendTo on the currency, which the Core User role holds on spaarkedev1).</para>
/// <para><b>Fail closed.</b> A list that cannot be read is <c>null</c> (never an empty list that would refuse every value or accept any); a
/// currency that cannot be determined is a refusal, never a blank <c>sprk_currency</c>. A fault in Dataverse throws, so the commit route
/// answers 503 before it writes anything.</para>
/// <para><b>Component justification (CLAUDE.md section 11).</b> Existing: <c>OptionsSource</c> was declared on the catalog and passed to the
/// client, but nothing resolved it; the AI <c>LookupChoicesResolver</c> returns labels only (no option values) for prompt enums, and
/// <c>FinanceBaseValues</c> reads no currency. Extension: neither returns what a wizard select and a commit check both need (value and
/// label, cached, with a refusal contract), and the AI resolver is not for CRUD code (ADR-013). Cost of doing nothing: Category and Period
/// would need hard-coded options that break when the owner relabels them, and the budget would carry no currency (owner UAT).</para>
/// </remarks>
public class DecisionParameterSources
{
    /// <summary>The prefix of a picklist source: <c>metadata:{table}.{column}</c>.</summary>
    public const string MetadataPrefix = "metadata:";

    /// <summary>The source of the active currencies (value: the <c>transactioncurrencyid</c>).</summary>
    public const string ActiveCurrencies = "currency:active";

    /// <summary>The default source: the Default Currency of the core matter's owning business unit (D-198: the customer business unit's, for a secure matter).</summary>
    public const string MatterCurrencyDefault = "matter-business-unit-currency";

    /// <summary>How long a metadata or currency list is reused, so an owner relabel shows up within minutes (ADR-009: Redis, tenant-scoped).</summary>
    public static readonly TimeSpan OptionsTtl = TimeSpan.FromMinutes(5);

    internal const string CacheResource = "decision-options";
    private const int CacheVersion = 1;

    private static readonly Regex TableColumn = new("^[a-z0-9_]+\\.[a-z0-9_]+$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly IDataverseUserClient _user;
    private readonly IGenericEntityService _entities;
    private readonly ITenantCache _cache;
    private readonly IConfiguration _configuration;
    private readonly IOptionsMonitor<ExternalAccessOptions> _externalAccess;
    private readonly ILogger<DecisionParameterSources> _logger;
    private readonly System.Collections.Concurrent.ConcurrentDictionary<(Guid MatterId, string Requested), BudgetCurrency> _resolvedCurrencies = new();

    public DecisionParameterSources(
        IDataverseUserClient user, IGenericEntityService entities, ITenantCache cache, IConfiguration configuration,
        IOptionsMonitor<ExternalAccessOptions> externalAccess, ILogger<DecisionParameterSources> logger)
    {
        _user = user ?? throw new ArgumentNullException(nameof(user));
        _entities = entities ?? throw new ArgumentNullException(nameof(entities));
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _externalAccess = externalAccess ?? throw new ArgumentNullException(nameof(externalAccess));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    // ---------------------------------------------------------------------------------------------------------------
    // Option lists
    // ---------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// The options of <paramref name="source"/>, or <c>null</c> when the source is unknown or cannot be read right now. Read once per
    /// (table, column) inside <see cref="OptionsTtl"/> (the tenant-scoped cache; no tenant claim means no cache, never a shared key).
    /// </summary>
    public virtual async Task<IReadOnlyList<DecisionParameterOption>?> OptionsAsync(HttpContext http, string source, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(http);
        var isCurrencies = string.Equals(source, ActiveCurrencies, StringComparison.Ordinal);
        var isMetadata = source.StartsWith(MetadataPrefix, StringComparison.Ordinal) && TableColumn.IsMatch(source[MetadataPrefix.Length..]);
        if (!isCurrencies && !isMetadata)
        {
            _logger.LogWarning("Decision options: '{Source}' is not a known source; no options.", source.Length > 80 ? source[..80] : source);
            return null;
        }

        var tenantId = DecisionPlanReadStamp.TenantId(http);
        if (!string.IsNullOrWhiteSpace(tenantId))
        {
            try
            {
                if (await _cache.GetAsync<DecisionParameterOption[]>(tenantId, CacheResource, source, CacheVersion, ct: ct).ConfigureAwait(false) is { } hit)
                    return hit;
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                _logger.LogWarning("Decision options: the cache read failed ({ExceptionType}); reading Dataverse.", ex.GetType().Name);
            }
        }

        DecisionParameterOption[]? options;
        try
        {
            options = isCurrencies ? await ReadActiveCurrenciesAsync(ct).ConfigureAwait(false) : await ReadPicklistAsync(source[MetadataPrefix.Length..], ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogWarning("Decision options: reading '{Source}' failed ({ExceptionType}); no options.", source, ex.GetType().Name);
            return null;
        }

        if (options is not null && !string.IsNullOrWhiteSpace(tenantId))
        {
            try
            {
                await _cache.SetAsync(tenantId, CacheResource, source, CacheVersion, options, OptionsTtl, ct: ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                _logger.LogWarning("Decision options: the cache write failed ({ExceptionType}); the list is still served.", ex.GetType().Name);
            }
        }

        return options;
    }

    private async Task<DecisionParameterOption[]?> ReadPicklistAsync(string tableColumn, CancellationToken ct)
    {
        var parts = tableColumn.Split('.');
        var response = await _user.GetAsync(
            $"EntityDefinitions(LogicalName='{parts[0]}')/Attributes(LogicalName='{parts[1]}')/Microsoft.Dynamics.CRM.PicklistAttributeMetadata" +
            "?$select=LogicalName&$expand=OptionSet($select=Options)", ct).ConfigureAwait(false);
        if (!response.IsSuccess
            || response.Body is not { ValueKind: JsonValueKind.Object } body
            || !body.TryGetProperty("OptionSet", out var optionSet) || optionSet.ValueKind != JsonValueKind.Object
            || !optionSet.TryGetProperty("Options", out var rows) || rows.ValueKind != JsonValueKind.Array)
        {
            _logger.LogWarning("Decision options: the metadata of {TableColumn} could not be read ({ErrorCode}).", tableColumn, response.ErrorCode);
            return null;
        }

        var options = new List<DecisionParameterOption>();
        foreach (var row in rows.EnumerateArray())
        {
            if (row.TryGetProperty("IsHidden", out var hidden) && hidden.ValueKind == JsonValueKind.True)
                continue;
            if (!row.TryGetProperty("Value", out var value) || value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out var number))
                continue;
            var label = LabelOf(row);
            options.Add(new DecisionParameterOption(number.ToString(System.Globalization.CultureInfo.InvariantCulture), label ?? number.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        }

        return options.ToArray();
    }

    private static string? LabelOf(JsonElement option)
    {
        if (!option.TryGetProperty("Label", out var label) || label.ValueKind != JsonValueKind.Object)
            return null;
        if (label.TryGetProperty("UserLocalizedLabel", out var user) && user.ValueKind == JsonValueKind.Object
            && user.TryGetProperty("Label", out var text) && text.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(text.GetString()))
            return text.GetString();
        if (label.TryGetProperty("LocalizedLabels", out var all) && all.ValueKind == JsonValueKind.Array)
        {
            foreach (var one in all.EnumerateArray())
            {
                if (one.TryGetProperty("Label", out var t) && t.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(t.GetString()))
                    return t.GetString();
            }
        }

        return null;
    }

    private async Task<DecisionParameterOption[]> ReadActiveCurrenciesAsync(CancellationToken ct)
    {
        var query = new QueryExpression("transactioncurrency") { ColumnSet = new ColumnSet("currencyname", "isocurrencycode") };
        query.Criteria.AddCondition("statecode", ConditionOperator.Equal, 0);
        query.AddOrder("currencyname", OrderType.Ascending);
        var rows = await _entities.RetrieveMultipleAsync(query, ct).ConfigureAwait(false);
        return rows.Entities
            .Where(e => !string.IsNullOrWhiteSpace(e.GetAttributeValue<string>("isocurrencycode")))
            .Select(e => new DecisionParameterOption(e.Id.ToString("D"), $"{e.GetAttributeValue<string>("currencyname")} ({e.GetAttributeValue<string>("isocurrencycode")})"))
            .ToArray();
    }

    // ---------------------------------------------------------------------------------------------------------------
    // The budget's currency
    // ---------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// The currency a budget under <paramref name="matterId"/> gets: <paramref name="requested"/> when the user chose one, else the
    /// default (<see cref="DefaultCurrencyAsync"/>). Either way it must be an existing, ACTIVE currency with an ISO code, so
    /// <c>sprk_currency</c> is never blank. A refusal carries the text for the wizard; a fault in Dataverse throws.
    /// A RESOLVED answer is remembered for the life of this (scoped, per-request) instance, keyed by (matter, requested currency), because
    /// the commit asks the same question from the preflight, the executor's re-run of it and the write; that is a within-request de-dupe of
    /// data, not an authorization decision, and nothing outlives the request. A refusal or a fault is never remembered.
    /// </summary>
    public virtual async Task<BudgetCurrency> ResolveBudgetCurrencyAsync(Guid matterId, string? requested, CancellationToken ct)
    {
        var key = (matterId, requested?.Trim().ToLowerInvariant() ?? string.Empty);
        if (_resolvedCurrencies.TryGetValue(key, out var known))
            return known;

        var resolved = await ResolveBudgetCurrencyCoreAsync(matterId, requested, ct).ConfigureAwait(false);
        if (resolved.IsResolved)
            _resolvedCurrencies[key] = resolved;
        return resolved;
    }

    private async Task<BudgetCurrency> ResolveBudgetCurrencyCoreAsync(Guid matterId, string? requested, CancellationToken ct)
    {
        var defaulted = string.IsNullOrWhiteSpace(requested);
        Guid currencyId;
        if (!string.IsNullOrWhiteSpace(requested))
        {
            if (!Guid.TryParse(requested, out currencyId) || currencyId == Guid.Empty)
                return new BudgetCurrency(null, null, "Choose one of the listed currencies.");
        }
        else
        {
            if (await DefaultCurrencyAsync(matterId, ct).ConfigureAwait(false) is not { } fallback)
                return new BudgetCurrency(null, null, "No currency could be determined for this matter's business unit, so the budget was not created. Choose a currency.");
            currencyId = fallback;
        }

        var query = new QueryExpression("transactioncurrency") { ColumnSet = new ColumnSet("isocurrencycode"), TopCount = 1 };
        query.Criteria.AddCondition("transactioncurrencyid", ConditionOperator.Equal, currencyId);
        query.Criteria.AddCondition("statecode", ConditionOperator.Equal, 0);
        var rows = await _entities.RetrieveMultipleAsync(query, ct).ConfigureAwait(false);
        if (rows.Entities.FirstOrDefault() is not { } row)
            return new BudgetCurrency(null, null, defaulted
                ? "This matter's business-unit currency is inactive; choose a currency."
                : "That is not an existing, active currency. Choose one of the listed currencies.");

        var iso = row.GetAttributeValue<string>("isocurrencycode")?.Trim();
        return string.IsNullOrWhiteSpace(iso)
            ? new BudgetCurrency(null, null, "That currency has no ISO code, so the budget was not created. Choose another currency.")
            : new BudgetCurrency(currencyId, iso, null);
    }

    /// <summary>The Default Currency of the business unit that owns <paramref name="matterId"/>, else the organization base currency
    /// (logged); <c>null</c> when neither exists. The matter's owning business unit is read AS THE CALLER; a caller read that is refused
    /// (401/403), finds nothing (404) or returns no value is treated as "the business unit has no Default Currency" (logged), never retried
    /// app-only. A Dataverse fault on that read (5xx, throttling, OBO failure, an exception) throws, like every other fault here, so the
    /// commit answers 503 before it writes anything. D-198: when that unit IS the Secure Record unit (which has no currency of its own), the
    /// currency is the deployment's customer business unit's (<see cref="CustomerBusinessUnitCurrencyAsync"/>), else the base currency.</summary>
    internal async Task<Guid?> DefaultCurrencyAsync(Guid matterId, CancellationToken ct)
    {
        if (await ReadOwningBusinessUnitAsCallerAsync(matterId, ct).ConfigureAwait(false) is { } unitId)
        {
            if (await SecureRecordBusinessUnitIdAsync(unitId, ct).ConfigureAwait(false) is { } secureUnitId)
            {
                if (await CustomerBusinessUnitCurrencyAsync(secureUnitId, ct).ConfigureAwait(false) is { } fromCustomer)
                    return fromCustomer;
            }
            else
            {
                var businessUnit = await _entities.RetrieveAsync("businessunit", unitId, ["transactioncurrencyid"], ct).ConfigureAwait(false);
                if (businessUnit.GetAttributeValue<EntityReference>("transactioncurrencyid") is { } fromUnit)
                    return fromUnit.Id;
            }
        }

        var org = await _entities.RetrieveMultipleAsync(
            new QueryExpression("organization") { ColumnSet = new ColumnSet("basecurrencyid"), TopCount = 1 }, ct).ConfigureAwait(false);
        if (org.Entities.FirstOrDefault()?.GetAttributeValue<EntityReference>("basecurrencyid") is { } baseCurrency)
        {
            _logger.LogWarning("Decision budget currency: the business unit of matter {MatterId} has no Default Currency; using the organization base currency.", matterId);
            return baseCurrency.Id;
        }

        return null;
    }

    /// <summary>The id of the Secure Record business unit when <paramref name="owningBusinessUnitId"/> IS it, else <c>null</c>. uac-r2 shared
    /// resolver and predicate (D-191; not <c>RecordOwnershipResolver</c> private copy). An ambiguous or unresolved Secure Record unit, or a
    /// predicate that cannot tell, means undeterminable: the matter is treated as not secure. A Dataverse fault throws.</summary>
    private async Task<Guid?> SecureRecordBusinessUnitIdAsync(Guid owningBusinessUnitId, CancellationToken ct)
    {
        var (secureId, ambiguous) = await SecureChildShareSynchronizer.ResolveSecureBusinessUnitAsync(_entities, _configuration, ct).ConfigureAwait(false);
        if (ambiguous)
        {
            _logger.LogWarning("Decision budget currency: more than one business unit carries the Secure Record name; the matter is treated as not secure.");
            return null;
        }

        return secureId is { } secure && SecureRecordOwnerTeam.IsInSecureBusinessUnit(owningBusinessUnitId, secure) == true ? secure : null;
    }

    /// <summary>The Default Currency of <c>ExternalAccess:CustomerBusinessUnitId</c>, the deployment single customer business unit, for a
    /// secure matter (D-198); <c>null</c> (logged as a Warning) when it is unset, names no business unit, is the Secure Record unit
    /// (<paramref name="secureBusinessUnitId"/>) or the root (no parent), or has no currency: the caller then uses the base currency. The
    /// Secure Record unit and root currencies are never taken as the customer. App-only: business-unit configuration, not record data.</summary>
    private async Task<Guid?> CustomerBusinessUnitCurrencyAsync(Guid secureBusinessUnitId, CancellationToken ct)
    {
        if (_externalAccess.CurrentValue.CustomerBusinessUnitId is not { } customerId || customerId == Guid.Empty)
        {
            _logger.LogWarning("Decision budget currency: ExternalAccess:CustomerBusinessUnitId is not configured; a secure matter uses the organization base currency.");
            return null;
        }

        if (customerId == secureBusinessUnitId)
        {
            _logger.LogWarning("Decision budget currency: ExternalAccess:CustomerBusinessUnitId is the Secure Record unit; a secure matter uses the organization base currency.");
            return null;
        }

        var query = new QueryExpression("businessunit") { ColumnSet = new ColumnSet("transactioncurrencyid", "parentbusinessunitid"), TopCount = 1, NoLock = true };
        query.Criteria.AddCondition("businessunitid", ConditionOperator.Equal, customerId);
        var unit = (await _entities.RetrieveMultipleAsync(query, ct).ConfigureAwait(false)).Entities.FirstOrDefault();
        if (unit is null)
        {
            _logger.LogWarning("Decision budget currency: ExternalAccess:CustomerBusinessUnitId {BusinessUnitId} names no business unit; a secure matter uses the organization base currency.", customerId);
            return null;
        }

        if (unit.GetAttributeValue<EntityReference>("parentbusinessunitid") is null)
        {
            _logger.LogWarning("Decision budget currency: ExternalAccess:CustomerBusinessUnitId {BusinessUnitId} is the root business unit; a secure matter uses the organization base currency.", customerId);
            return null;
        }

        if (unit.GetAttributeValue<EntityReference>("transactioncurrencyid") is { } currency)
            return currency.Id;

        _logger.LogWarning("Decision budget currency: the customer business unit {BusinessUnitId} has no Default Currency; a secure matter uses the organization base currency.", customerId);
        return null;
    }

    /// <summary>The owning business unit of the matter as the CALLER sees it (<c>sprk_matters(id)?$select=_owningbusinessunit_value</c>), or
    /// <c>null</c> when the caller cannot read the matter or the value is absent. Throws on a Dataverse fault.</summary>
    private async Task<Guid?> ReadOwningBusinessUnitAsCallerAsync(Guid matterId, CancellationToken ct)
    {
        var response = await _user.GetAsync($"sprk_matters({matterId:D})?$select=_owningbusinessunit_value", ct).ConfigureAwait(false);
        if (!response.IsSuccess)
        {
            if (response.ErrorCode is DataverseUserClientErrorCodes.AccessDenied or DataverseUserClientErrorCodes.NotFound
                or DataverseUserClientErrorCodes.UserContextRequired)
            {
                _logger.LogWarning(
                    "Decision budget currency: the caller's read of matter {MatterId} failed ({ErrorCode}); treating its business unit as having no Default Currency.",
                    matterId, response.ErrorCode);
                return null;
            }

            throw new HttpRequestException($"The caller's read of the matter's business unit failed ({response.ErrorCode ?? "unknown"}, HTTP {response.StatusCode}).");
        }

        if (response.Body is { ValueKind: JsonValueKind.Object } body
            && body.TryGetProperty("_owningbusinessunit_value", out var value) && value.ValueKind == JsonValueKind.String
            && Guid.TryParse(value.GetString(), out var unitId) && unitId != Guid.Empty)
            return unitId;

        _logger.LogWarning(
            "Decision budget currency: the caller's read of matter {MatterId} returned no business unit; treating it as having no Default Currency.", matterId);
        return null;
    }

    // ---------------------------------------------------------------------------------------------------------------
    // The plan route
    // ---------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// What the plan route adds for <paramref name="plan"/>: every parameter's option list, once per source, and every starting value.
    /// Never throws and never fails the plan read: a list that cannot be read is empty (the wizard then offers nothing to pick, and the
    /// commit still validates), a default that cannot be read is absent (the commit then resolves it again). A plan whose parameters name
    /// no source asks for nothing.
    /// </summary>
    public virtual async Task<DecisionParameterFacts> ForPlanAsync(
        HttpContext http, ResolvedDecisionPlan plan, Guid? matterId, CancellationToken ct)
    {
        var parameters = plan.Actions.Concat(plan.NextSteps).SelectMany(a => a.Parameters).ToList();
        var sources = parameters.Where(p => p.Options is null && p.OptionsSource is not null).Select(p => p.OptionsSource!).Distinct(StringComparer.Ordinal).ToList();
        var defaults = parameters.Where(p => p.DefaultSource is not null).Select(p => p.DefaultSource!).Distinct(StringComparer.Ordinal).ToList();
        if (sources.Count == 0 && defaults.Count == 0)
            return DecisionParameterFacts.None;

        var options = new Dictionary<string, IReadOnlyList<DecisionParameterOption>>(StringComparer.Ordinal);
        foreach (var source in sources)
            options[source] = await OptionsAsync(http, source, ct).ConfigureAwait(false) ?? [];

        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var source in defaults)
        {
            if (source != MatterCurrencyDefault || matterId is not { } matter)
                continue;
            try
            {
                // A default the wizard cannot show among its options (an inactive currency, an unreadable list) would block the step on
                // a value the user never chose, so it is left out; the commit then resolves an omitted currency the same way.
                if (await DefaultCurrencyAsync(matter, ct).ConfigureAwait(false) is { } currency
                    && options.TryGetValue(ActiveCurrencies, out var offered)
                    && offered.Any(o => string.Equals(o.Value, currency.ToString("D"), StringComparison.OrdinalIgnoreCase)))
                    values[source] = currency.ToString("D");
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                _logger.LogWarning("Decision plan: the matter's default currency could not be read ({ExceptionType}); no default.", ex.GetType().Name);
            }
        }

        return new DecisionParameterFacts(options, values);
    }
}
