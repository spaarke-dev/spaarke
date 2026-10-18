using Microsoft.Extensions.Caching.Memory;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Finance.Models;

namespace Sprk.Bff.Api.Services.Finance;

/// <summary>
/// Dataverse-backed <see cref="IEbillingSettingsReader"/>: one FetchXml read of the active <c>sprk_ebillingsetting</c> row mapped to
/// <see cref="EbillingSettings"/>, cached in <see cref="IMemoryCache"/> for <see cref="CacheTtl"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>ADR-009 exception (IMemoryCache).</b> Same justification as <see cref="Ai.DataverseAllowedIndexesProvider"/>: the row is
/// environment-wide configuration (one row per environment, not tenant data) that background jobs read without a tenant context, and a
/// few-second in-process copy per instance is enough because a stale copy only delays a changed threshold by at most
/// <see cref="CacheTtl"/>. No authorization decision is cached; the settings carry no per-user data. The TTL is deliberately short
/// (at most 60 seconds, the task 025 constraint) and there is no cross-instance invalidation.
/// </para>
/// <para>
/// <b>Lifetime.</b> Singleton. <see cref="IGenericEntityService"/> is scoped, so each cache-miss load takes a fresh scope (no captive
/// dependency), as <see cref="Ai.DataverseAllowedIndexesProvider"/> does.
/// </para>
/// <para>
/// <b>No row, or a cleared value.</b> No active row yields <see cref="EbillingSettings"/> defaults (cached for the TTL, one warning per
/// cycle). A cleared column on an existing row keeps that property's default, so approver resolution never ends up with no roles.
/// A failed read is not cached and propagates.
/// </para>
/// </remarks>
public sealed class EbillingSettingsReader : IEbillingSettingsReader
{
    /// <summary>The Dataverse table.</summary>
    internal const string EntityName = "sprk_ebillingsetting";

    /// <summary>Cache key of the settings.</summary>
    internal const string CacheKey = "sprk_ebillingsetting:current";

    /// <summary>Absolute TTL of the cached settings (task 025: at most 60 seconds).</summary>
    internal static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(30);

    // top="2": the alternate key keeps the table to one row; a second row would be a platform fault worth a warning.
    private const string FetchXml = """
        <fetch top="2">
          <entity name="sprk_ebillingsetting">
            <attribute name="sprk_ebillingsettingid" />
            <attribute name="sprk_matterapproverroles" />
            <attribute name="sprk_projectapproverroles" />
            <attribute name="sprk_workassignmentapproverroles" />
            <attribute name="sprk_reliabilitythreshold" />
            <attribute name="sprk_weightanalyzer" />
            <attribute name="sprk_weightarithmetic" />
            <attribute name="sprk_weightreferencematch" />
            <attribute name="sprk_weightselfreport" />
            <attribute name="sprk_arithmeticfailcap" />
            <attribute name="sprk_referencemissingcap" />
            <attribute name="sprk_relatedwindowdays" />
            <attribute name="sprk_codingchunksize" />
            <attribute name="sprk_firmnoticeaienabled" />
            <attribute name="sprk_defaultcodeset" />
            <attribute name="sprk_apexportmapping" />
            <filter>
              <condition attribute="statecode" operator="eq" value="0" />
            </filter>
            <order attribute="createdon" />
          </entity>
        </fetch>
        """;

    private readonly IServiceProvider _serviceProvider;
    private readonly IMemoryCache _cache;
    private readonly ILogger<EbillingSettingsReader> _logger;

    public EbillingSettingsReader(
        IServiceProvider serviceProvider,
        IMemoryCache cache,
        ILogger<EbillingSettingsReader> logger)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<EbillingSettings> GetAsync(CancellationToken ct = default)
    {
        var settings = await _cache.GetOrCreateAsync(CacheKey, async entry =>
        {
            var loaded = await LoadAsync(ct);
            // Set after the load: a failed load throws before an entry is stored, so a failure is never cached.
            entry.AbsoluteExpirationRelativeToNow = CacheTtl;
            return loaded;
        });

        return settings!;
    }

    private async Task<EbillingSettings> LoadAsync(CancellationToken ct)
    {
        // A fresh scope so the scoped IGenericEntityService can be used from this singleton.
        using var scope = _serviceProvider.CreateScope();
        var entityService = scope.ServiceProvider.GetRequiredService<IGenericEntityService>();

        var result = await entityService.RetrieveMultipleAsync(new FetchExpression(FetchXml), ct);

        if (result.Entities.Count == 0)
        {
            _logger.LogWarning(
                "eBilling settings: no active {Entity} row; using the built-in defaults until one exists",
                EntityName);
            return new EbillingSettings();
        }

        if (result.Entities.Count > 1)
        {
            _logger.LogWarning(
                "eBilling settings: more than one active {Entity} row (the singleton key should prevent this); using the oldest",
                EntityName);
        }

        var settings = Map(result.Entities[0]);

        // Task 073 (FR-26, I-5, D-89): the platform checks each column's range, not the pair "reference-missing cap below the threshold".
        // A row that breaks it is still returned (the other settings are unaffected), but loudly: one Error per cache cycle, and the
        // submission rejection rule rejects nothing while it stands, so an out-of-range save cannot make a submission be rejected.
        if (settings.RejectionInvariantProblem is { } problem)
        {
            _logger.LogError(
                "eBilling settings: {Entity} breaks the rejection rule's invariant, so no submission can be rejected until it is fixed: {Problem}",
                EntityName,
                problem);
        }

        return settings;
    }

    private EbillingSettings Map(Entity row)
    {
        var defaults = new EbillingSettings();
        var mapping = row.GetAttributeValue<string>("sprk_apexportmapping");

        return new EbillingSettings
        {
            MatterApproverRoles = MapRoles(row, "sprk_matterapproverroles", defaults.MatterApproverRoles),
            ProjectApproverRoles = MapRoles(row, "sprk_projectapproverroles", defaults.ProjectApproverRoles),
            WorkAssignmentApproverRoles = MapRoles(row, "sprk_workassignmentapproverroles", defaults.WorkAssignmentApproverRoles),
            ReliabilityThreshold = row.GetAttributeValue<decimal?>("sprk_reliabilitythreshold") ?? defaults.ReliabilityThreshold,
            WeightAnalyzer = row.GetAttributeValue<decimal?>("sprk_weightanalyzer") ?? defaults.WeightAnalyzer,
            WeightArithmetic = row.GetAttributeValue<decimal?>("sprk_weightarithmetic") ?? defaults.WeightArithmetic,
            WeightReferenceMatch = row.GetAttributeValue<decimal?>("sprk_weightreferencematch") ?? defaults.WeightReferenceMatch,
            WeightSelfReport = row.GetAttributeValue<decimal?>("sprk_weightselfreport") ?? defaults.WeightSelfReport,
            ArithmeticFailCap = row.GetAttributeValue<decimal?>("sprk_arithmeticfailcap") ?? defaults.ArithmeticFailCap,
            ReferenceMissingCap = row.GetAttributeValue<decimal?>("sprk_referencemissingcap") ?? defaults.ReferenceMissingCap,
            RelatedWorkWindowDays = row.GetAttributeValue<int?>("sprk_relatedwindowdays") ?? defaults.RelatedWorkWindowDays,
            CodingChunkSize = row.GetAttributeValue<int?>("sprk_codingchunksize") ?? defaults.CodingChunkSize,
            FirmNoticeAiEnabled = row.GetAttributeValue<bool?>("sprk_firmnoticeaienabled") ?? defaults.FirmNoticeAiEnabled,
            DefaultCodeSetId = row.GetAttributeValue<EntityReference>("sprk_defaultcodeset")?.Id,
            ApExportMappingJson = string.IsNullOrWhiteSpace(mapping) ? null : mapping,
        };
    }

    /// <summary>
    /// Maps a multi-select column to roles. A missing or empty value keeps the default; a value that is not a known
    /// <see cref="BillingApproverRole"/> (an option added later) is skipped with a warning.
    /// </summary>
    private IReadOnlyList<BillingApproverRole> MapRoles(Entity row, string column, IReadOnlyList<BillingApproverRole> fallback)
    {
        var values = row.GetAttributeValue<OptionSetValueCollection>(column);
        if (values is null || values.Count == 0)
        {
            return fallback;
        }

        var roles = new List<BillingApproverRole>(values.Count);
        foreach (var value in values)
        {
            if (Enum.IsDefined(typeof(BillingApproverRole), value.Value))
            {
                var role = (BillingApproverRole)value.Value;
                if (!roles.Contains(role))
                {
                    roles.Add(role);
                }
            }
            else
            {
                _logger.LogWarning(
                    "eBilling settings: {Column} holds the unknown role value {Value}; it is ignored",
                    column,
                    value.Value);
            }
        }

        return roles.Count == 0 ? fallback : roles.AsReadOnly();
    }
}
