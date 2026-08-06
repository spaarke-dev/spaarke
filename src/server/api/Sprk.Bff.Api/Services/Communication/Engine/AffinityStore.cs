using System.Security.Cryptography;
using System.Text;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Spaarke.Dataverse;

namespace Sprk.Bff.Api.Services.Communication.Engine;

/// <summary>
/// The affinity signal type — a per-tenant confirmation-frequency key type (FR-A4). Integer values match the
/// <c>sprk_affinity.sprk_signaltype</c> CHOICE option set exactly (Dataverse-authored).
/// </summary>
public enum AffinitySignalType
{
    /// <summary>Full sender address → record.</summary>
    Sender = 100000000,

    /// <summary>Sender email domain → record.</summary>
    SenderDomain = 100000001,

    /// <summary>A significant subject keyword → record.</summary>
    SubjectKeyword = 100000002,

    /// <summary>Canonical participant set (from + to + cc, sorted) → record.</summary>
    ParticipantSet = 100000003,
}

/// <summary>One affinity signal computed from a message: a signal type + its raw value.</summary>
public readonly record struct AffinitySignal(AffinitySignalType Type, string Value);

/// <summary>The highest-frequency affinity row matched for a message's signal set.</summary>
public sealed record AffinityHit(
    string TargetEntity,
    string TargetId,
    int ConfirmationCount,
    AffinitySignalType MatchedSignalType,
    string MatchedSignalValue);

/// <summary>
/// Read + increment-on-confirmation access to the per-tenant <c>sprk_affinity</c> learning store (FR-A4).
/// Backed entirely by <see cref="IGenericEntityService"/> (no new Dataverse seam). The store records how often
/// a human has confirmed an email carrying a given signal (sender / sender-domain / subject-keyword /
/// participant-set) to a given record, so a later untagged email carrying the same signal can surface that
/// record as a SUGGEST-ONLY candidate. The store is an ADR-040 Path A exception: filing-history metadata,
/// distinct from the ADR-040 session ledger and the ADR-048 participant index.
/// </summary>
/// <remarks>
/// Both operations are best-effort (NFR-04): the read returns null on any query failure (a throwing rung is a
/// non-match), and the increment writer never throws (a confirmation must never fail because the learning
/// write did). Registered as a concrete singleton in <c>CommunicationModule</c> (ADR-010).
/// </remarks>
public sealed class AffinityStore
{
    private const string EntityName = "sprk_affinity";

    // NVARCHAR bounds of the target columns (Dataverse-authored). A signal value longer than the column is
    // reduced to a deterministic hash so READ (query) and WRITE (upsert) canonicalize identically — the whole
    // point is that the same participant-set/keyword hashes to the same stored value on both paths.
    private const int SignalValueMax = 1000;
    private const int NameMax = 850;

    private readonly IGenericEntityService _genericEntityService;
    private readonly ILogger<AffinityStore> _logger;

    public AffinityStore(IGenericEntityService genericEntityService, ILogger<AffinityStore> logger)
    {
        _genericEntityService = genericEntityService;
        _logger = logger;
    }

    /// <summary>
    /// Returns the single highest-<c>sprk_confirmationcount</c> active affinity row (≥
    /// <paramref name="minConfirmations"/>) whose (signal type, signal value) matches ANY of
    /// <paramref name="signals"/> within <paramref name="tenantKey"/>, in one round-trip; null when none
    /// qualify or on any failure (best-effort). Ties break arbitrarily on the highest count.
    /// </summary>
    public async Task<AffinityHit?> GetTopAffinityAsync(
        IReadOnlyCollection<AffinitySignal> signals,
        string? tenantKey,
        int minConfirmations,
        CancellationToken ct)
    {
        if (signals is null || signals.Count == 0)
            return null;

        try
        {
            var query = new QueryExpression(EntityName)
            {
                ColumnSet = new ColumnSet(
                    "sprk_targetentity", "sprk_targetid", "sprk_confirmationcount",
                    "sprk_signaltype", "sprk_signalvalue"),
                TopCount = 1,
                Orders = { new OrderExpression("sprk_confirmationcount", OrderType.Descending) },
            };

            // Active rows, in-tenant, with enough confirmations …
            query.Criteria.AddCondition("statecode", ConditionOperator.Equal, 0);
            query.Criteria.AddCondition("sprk_confirmationcount", ConditionOperator.GreaterEqual, minConfirmations);
            if (string.IsNullOrWhiteSpace(tenantKey))
                query.Criteria.AddCondition("sprk_tenantkey", ConditionOperator.Null);
            else
                query.Criteria.AddCondition("sprk_tenantkey", ConditionOperator.Equal, tenantKey);

            // … matching ANY (signaltype AND signalvalue) pair.
            var orFilter = new FilterExpression(LogicalOperator.Or);
            foreach (var signal in signals)
            {
                if (string.IsNullOrWhiteSpace(signal.Value))
                    continue;
                var pair = new FilterExpression(LogicalOperator.And);
                pair.AddCondition("sprk_signaltype", ConditionOperator.Equal, (int)signal.Type);
                pair.AddCondition("sprk_signalvalue", ConditionOperator.Equal, BoundSignalValue(signal.Value));
                orFilter.AddFilter(pair);
            }
            if (orFilter.Filters.Count == 0)
                return null;
            query.Criteria.AddFilter(orFilter);

            var result = await _genericEntityService.RetrieveMultipleAsync(query, ct);
            if (result.Entities.Count == 0)
                return null;

            var row = result.Entities[0];
            var targetEntity = row.GetAttributeValue<string>("sprk_targetentity");
            var targetId = row.GetAttributeValue<string>("sprk_targetid");
            if (string.IsNullOrWhiteSpace(targetEntity) || string.IsNullOrWhiteSpace(targetId))
                return null; // a dirty row (no target) is a non-match, never a throw (NFR-04)

            var count = row.GetAttributeValue<int>("sprk_confirmationcount");
            var signalType = (AffinitySignalType)(row.GetAttributeValue<OptionSetValue>("sprk_signaltype")?.Value
                ?? (int)AffinitySignalType.Sender);
            var signalValue = row.GetAttributeValue<string>("sprk_signalvalue") ?? string.Empty;

            return new AffinityHit(targetEntity.Trim(), targetId.Trim(), count, signalType, signalValue);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Affinity lookup failed (non-fatal); treating as no affinity match.");
            return null;
        }
    }

    /// <summary>
    /// Records a human confirmation of a (signal → target) association: increments the matching
    /// <c>sprk_affinity</c> row's <c>sprk_confirmationcount</c> (and stamps <c>sprk_lastconfirmed</c>), or
    /// creates it with count 1 when none exists. Best-effort (NFR-04): any failure is logged and swallowed — a
    /// confirmation MUST NOT fail because the learning write did.
    /// </summary>
    /// <remarks>
    /// The intended caller is the human-confirmation site on the r5 review surface
    /// (<c>applyRegardingSelection</c>); that surface is r5-owned (client-side additive write, bypassing the
    /// BFF), so wiring the CALL is escalated to r5 coordination (FR-E6) rather than performed here. This method
    /// is the ready seam.
    /// </remarks>
    public async Task RecordConfirmationAsync(
        AffinitySignalType type,
        string value,
        string targetEntity,
        string targetId,
        string? tenantKey,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(value) || string.IsNullOrWhiteSpace(targetEntity) || string.IsNullOrWhiteSpace(targetId))
            return;

        try
        {
            var boundedValue = BoundSignalValue(value);
            var name = BuildName(tenantKey, type, boundedValue, targetEntity, targetId);

            var existing = await FindByNameAsync(name, ct);
            if (existing is not null)
            {
                var current = existing.GetAttributeValue<int>("sprk_confirmationcount");
                await _genericEntityService.UpdateAsync(EntityName, existing.Id, new Dictionary<string, object>
                {
                    ["sprk_confirmationcount"] = current + 1,
                    ["sprk_lastconfirmed"] = DateTime.UtcNow,
                }, ct);
                return;
            }

            var row = new Entity(EntityName)
            {
                ["sprk_name"] = name,
                ["sprk_signaltype"] = new OptionSetValue((int)type),
                ["sprk_signalvalue"] = boundedValue,
                ["sprk_targetentity"] = targetEntity.Trim(),
                ["sprk_targetid"] = targetId.Trim(),
                ["sprk_confirmationcount"] = 1,
                ["sprk_lastconfirmed"] = DateTime.UtcNow,
            };
            if (!string.IsNullOrWhiteSpace(tenantKey))
                row["sprk_tenantkey"] = tenantKey;

            await _genericEntityService.CreateAsync(row, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Recording affinity confirmation failed (non-fatal) | Type: {Type}, Target: {Entity}:{Id}",
                type, targetEntity, targetId);
        }
    }

    private async Task<Entity?> FindByNameAsync(string name, CancellationToken ct)
    {
        var query = new QueryExpression(EntityName)
        {
            ColumnSet = new ColumnSet("sprk_affinityid", "sprk_confirmationcount"),
            TopCount = 1,
        };
        query.Criteria.AddCondition("sprk_name", ConditionOperator.Equal, name);
        var result = await _genericEntityService.RetrieveMultipleAsync(query, ct);
        return result.Entities.Count > 0 ? result.Entities[0] : null;
    }

    /// <summary>
    /// The deterministic <c>sprk_name</c> upsert key for a (tenant, type, value, target) tuple. Readable when
    /// it fits the column; otherwise a stable hash so distinct tuples never collide on a truncated name.
    /// </summary>
    private static string BuildName(string? tenantKey, AffinitySignalType type, string boundedValue, string targetEntity, string targetId)
    {
        var composite = $"{tenantKey}|{(int)type}|{boundedValue}|{targetEntity}:{targetId}";
        return composite.Length <= NameMax ? composite : $"aff:{(int)type}:{Sha256Hex(composite)}";
    }

    /// <summary>
    /// Bounds a signal value to the <c>sprk_signalvalue</c> column: pass-through when it fits, otherwise a
    /// stable <c>sha256:</c> hash. Applied identically on the read and write paths so the same logical value
    /// canonicalizes to the same stored value on both.
    /// </summary>
    private static string BoundSignalValue(string value)
    {
        var trimmed = value.Trim();
        return trimmed.Length <= SignalValueMax ? trimmed : "sha256:" + Sha256Hex(trimmed);
    }

    private static string Sha256Hex(string input)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
