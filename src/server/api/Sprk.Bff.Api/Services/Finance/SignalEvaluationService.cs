using Microsoft.Extensions.Options;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Telemetry;

namespace Sprk.Bff.Api.Services.Finance;

/// <summary>
/// Evaluates spend snapshots against threshold-based rules and upserts spend signal records.
/// Pure deterministic rule evaluation -- no AI involved.
/// </summary>
/// <remarks>
/// Signal rules:
/// - BudgetExceeded (100000000): fires when spend/budget >= 1.0 (100%) -- always active, not configurable
/// - BudgetWarning (100000001): fires when spend/budget >= configurable % (default 80%)
/// - VelocitySpike (100000002): fires when MoM velocity >= configurable % (default 50%)
///
/// Signals are upserted (idempotent) -- re-evaluation updates existing signals rather than creating duplicates.
/// </remarks>
public interface ISignalEvaluationService
{
    /// <summary>
    /// Evaluate spend snapshots for a matter against threshold rules and upsert signals.
    /// </summary>
    /// <param name="matterId">The matter to evaluate.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The number of signals triggered.</returns>
    Task<int> EvaluateAsync(Guid matterId, CancellationToken ct = default);
}

/// <summary>
/// Threshold-based signal detection service.
/// Queries spend snapshots for a matter and evaluates them against configurable rules.
/// Upserts sprk_spendsignal records for any triggered signals.
/// </summary>
public class SignalEvaluationService : ISignalEvaluationService
{
    private readonly IDataverseService _dataverseService;
    private readonly FinanceOptions _options;
    private readonly FinanceTelemetry _telemetry;
    private readonly ILogger<SignalEvaluationService> _logger;

    /// <summary>
    /// Internal rules evaluated against each snapshot.
    /// Uses the strategy pattern for extensibility -- new rules can be added without modifying existing ones.
    /// </summary>
    private readonly IReadOnlyList<ISignalRule> _rules;

    // ═══════════════════════════════════════════════════════════════════════════
    // Dataverse Schema Constants
    // ═══════════════════════════════════════════════════════════════════════════

    // Entity names
    private const string SnapshotEntity = "sprk_spendsnapshot";
    private const string SignalEntity = "sprk_spendsignal";
    private const string MatterEntity = "sprk_matter";

    // Snapshot fields
    private const string SnapshotMatterLookup = "sprk_matter";
    private const string SnapshotPeriodType = "sprk_periodtype";
    private const string SnapshotInvoicedAmount = "sprk_invoicedamount";
    private const string SnapshotBudgetAmount = "sprk_budgetamount";
    private const string SnapshotVelocityPct = "sprk_velocitypct";
    private const string SnapshotPeriodKey = "sprk_periodkey";
    private const string SnapshotBucketKey = "sprk_bucketkey";

    // Signal fields
    private const string SignalMatterLookup = "sprk_matter";
    private const string SignalSignalType = "sprk_signaltype";
    private const string SignalSeverity = "sprk_severity";
    private const string SignalMessage = "sprk_message";
    private const string SignalSnapshot = "sprk_snapshot";
    private const string SignalIsActive = "sprk_isactive";
    private const string SignalGeneratedAt = "sprk_generatedat";

    // Period type option set values
    private const int PeriodTypeMonth = 100000000;
    private const int PeriodTypeToDate = 100000003;

    // Signal type option set values
    internal const int SignalTypeBudgetExceeded = 100000000;
    internal const int SignalTypeBudgetWarning = 100000001;
    internal const int SignalTypeVelocitySpike = 100000002;

    // Severity option set values
    internal const int SeverityInfo = 100000000;
    internal const int SeverityWarning = 100000001;
    internal const int SeverityCritical = 100000002;

    /// <summary>
    /// Fields to retrieve from snapshot records for evaluation.
    /// </summary>
    private static readonly string[] SnapshotSelectFields =
    [
        SnapshotPeriodType,
        SnapshotPeriodKey,
        SnapshotBucketKey,
        SnapshotInvoicedAmount,
        SnapshotBudgetAmount,
        SnapshotVelocityPct
    ];

    public SignalEvaluationService(
        IDataverseService dataverseService,
        IOptions<FinanceOptions> options,
        FinanceTelemetry telemetry,
        ILogger<SignalEvaluationService> logger)
    {
        _dataverseService = dataverseService;
        _options = options.Value;
        _telemetry = telemetry;
        _logger = logger;

        // Initialize rules -- order matters for consistent evaluation
        _rules =
        [
            new BudgetExceededRule(),
            new BudgetWarningRule(),
            new VelocitySpikeRule()
        ];
    }

    /// <inheritdoc />
    public async Task<int> EvaluateAsync(Guid matterId, CancellationToken ct = default)
    {
        _logger.LogInformation(
            "Starting signal evaluation for matter {MatterId}. Rules={RuleCount}",
            matterId, _rules.Count);

        // 1. Query snapshot record IDs for this matter
        var snapshotIds = await _dataverseService.QueryChildRecordIdsAsync(
            SnapshotEntity,
            SnapshotMatterLookup,
            matterId,
            ct);

        if (snapshotIds.Length == 0)
        {
            _logger.LogInformation("No spend snapshots found for matter {MatterId}. Skipping evaluation.", matterId);
            return 0;
        }

        _logger.LogDebug("Found {SnapshotCount} snapshots for matter {MatterId}", snapshotIds.Length, matterId);

        // 2. Retrieve snapshot field values and evaluate rules
        var totalSignals = 0;

        foreach (var snapshotId in snapshotIds)
        {
            var fields = await _dataverseService.RetrieveRecordFieldsAsync(
                SnapshotEntity,
                snapshotId,
                SnapshotSelectFields,
                ct);

            if (fields.Count == 0)
            {
                _logger.LogWarning("Snapshot {SnapshotId} returned no fields. Skipping.", snapshotId);
                continue;
            }

            var snapshot = SpendSnapshotData.FromFieldDictionary(snapshotId, fields);

            // Evaluate each rule against the snapshot
            foreach (var rule in _rules)
            {
                if (rule.Evaluate(snapshot, _options, out var signal))
                {
                    await UpsertSignalAsync(matterId, snapshotId, signal, ct);
                    totalSignals++;

                    _telemetry.RecordSignalEmitted(signal.SignalTypeName, matterId.ToString());

                    _logger.LogInformation(
                        "Signal triggered: {SignalType} for matter {MatterId}. " +
                        "TriggerValue={TriggerValue:F2}, Threshold={ThresholdValue:F2}, Snapshot={SnapshotId}",
                        signal.SignalTypeName, matterId,
                        signal.TriggerValue, signal.ThresholdValue, snapshotId);
                }
            }
        }

        _logger.LogInformation(
            "Signal evaluation completed for matter {MatterId}. SignalsTriggered={SignalCount}",
            matterId, totalSignals);

        return totalSignals;
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Dataverse Signal Upsert
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Upsert a spend signal record. Uses a deterministic Guid derived from matter + signal type
    /// so re-evaluation updates the existing signal rather than creating duplicates.
    /// </summary>
    private async Task UpsertSignalAsync(
        Guid matterId,
        Guid snapshotId,
        SignalData signal,
        CancellationToken ct)
    {
        // Generate deterministic ID for idempotent upsert:
        // Same matter + signal type always produces the same record ID.
        var signalRecordId = GenerateDeterministicId(matterId, signal.SignalType);

        var fields = new Dictionary<string, object?>
        {
            // Lookup fields use Web API binding format: _fieldname_value
            [$"_{SignalMatterLookup}_value"] = matterId,
            [$"_{SignalSnapshot}_value"] = snapshotId,

            // Choice fields are plain int values in Web API
            [SignalSignalType] = signal.SignalType,
            [SignalSeverity] = signal.Severity,

            // Simple fields
            [SignalMessage] = signal.Message,
            [SignalIsActive] = true,
            [SignalGeneratedAt] = DateTime.UtcNow
        };

        await _dataverseService.UpdateRecordFieldsAsync(
            SignalEntity,
            signalRecordId,
            fields,
            ct);

        _logger.LogDebug(
            "Upserted signal {SignalId} (type={SignalType}) for matter {MatterId}",
            signalRecordId, signal.SignalTypeName, matterId);
    }

    /// <summary>
    /// Generate a deterministic Guid from matter ID and signal type.
    /// Ensures idempotent upsert: same inputs always produce the same record ID.
    /// </summary>
    internal static Guid GenerateDeterministicId(Guid matterId, int signalType)
    {
        // Use a namespace-based approach: XOR matter bytes with signal type hash
        var matterBytes = matterId.ToByteArray();
        var signalBytes = BitConverter.GetBytes(signalType);

        // Mix signal type into specific positions to differentiate signal types
        matterBytes[0] ^= signalBytes[0];
        matterBytes[1] ^= signalBytes[1];
        matterBytes[2] ^= signalBytes[2];
        matterBytes[3] ^= signalBytes[3];

        // Set version 4 (random) and variant bits to ensure valid GUID format
        matterBytes[7] = (byte)((matterBytes[7] & 0x0F) | 0x40); // Version 4
        matterBytes[8] = (byte)((matterBytes[8] & 0x3F) | 0x80); // Variant 1

        return new Guid(matterBytes);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Internal Signal Rule Abstraction
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Signal rule interface for extensibility. Each rule evaluates a snapshot against a threshold
    /// and produces a signal if the condition is met.
    /// </summary>
    private interface ISignalRule
    {
        /// <summary>
        /// Evaluate a snapshot against this rule's threshold.
        /// </summary>
        /// <param name="snapshot">The snapshot data to evaluate.</param>
        /// <param name="options">Finance configuration with threshold settings.</param>
        /// <param name="signal">The signal data if the rule fires; default if not.</param>
        /// <returns>True if the rule fires (signal should be created); false otherwise.</returns>
        bool Evaluate(SpendSnapshotData snapshot, FinanceOptions options, out SignalData signal);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Signal Rules
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// BudgetExceeded: fires when spend/budget >= 1.0 (100%).
    /// Always active, not configurable. Severity: Critical.
    /// Only evaluates ToDate period snapshots (cumulative budget check).
    /// </summary>
    private sealed class BudgetExceededRule : ISignalRule
    {
        public bool Evaluate(SpendSnapshotData snapshot, FinanceOptions options, out SignalData signal)
        {
            signal = default;

            // Budget checks only apply to ToDate snapshots
            if (snapshot.PeriodType != PeriodTypeToDate)
                return false;

            // Cannot evaluate without budget
            if (!snapshot.BudgetAmount.HasValue || snapshot.BudgetAmount.Value <= 0)
                return false;

            var ratio = snapshot.InvoicedAmount / snapshot.BudgetAmount.Value;

            if (ratio < 1.0m)
                return false;

            signal = new SignalData
            {
                SignalType = SignalTypeBudgetExceeded,
                SignalTypeName = "BudgetExceeded",
                Severity = SeverityCritical,
                TriggerValue = ratio * 100,
                ThresholdValue = 100m,
                Message = $"Budget exceeded: spend is {ratio:P1} of budget " +
                          $"(${snapshot.InvoicedAmount:N2} of ${snapshot.BudgetAmount.Value:N2})"
            };

            return true;
        }
    }

    /// <summary>
    /// BudgetWarning: fires when spend/budget >= configurable threshold (default 80%).
    /// Does NOT fire if BudgetExceeded would also fire (BudgetExceeded takes priority).
    /// Only evaluates ToDate period snapshots.
    /// </summary>
    private sealed class BudgetWarningRule : ISignalRule
    {
        public bool Evaluate(SpendSnapshotData snapshot, FinanceOptions options, out SignalData signal)
        {
            signal = default;

            // Budget checks only apply to ToDate snapshots
            if (snapshot.PeriodType != PeriodTypeToDate)
                return false;

            // Cannot evaluate without budget
            if (!snapshot.BudgetAmount.HasValue || snapshot.BudgetAmount.Value <= 0)
                return false;

            var ratio = snapshot.InvoicedAmount / snapshot.BudgetAmount.Value;
            var threshold = options.BudgetWarningPercentage / 100m;

            // Only fire warning if below 100% (BudgetExceeded handles >= 100%)
            if (ratio >= 1.0m || ratio < threshold)
                return false;

            signal = new SignalData
            {
                SignalType = SignalTypeBudgetWarning,
                SignalTypeName = "BudgetWarning",
                Severity = SeverityWarning,
                TriggerValue = ratio * 100,
                ThresholdValue = options.BudgetWarningPercentage,
                Message = $"Budget warning: spend is {ratio:P1} of budget " +
                          $"(${snapshot.InvoicedAmount:N2} of ${snapshot.BudgetAmount.Value:N2}, " +
                          $"threshold: {options.BudgetWarningPercentage}%)"
            };

            return true;
        }
    }

    /// <summary>
    /// VelocitySpike: fires when month-over-month velocity >= configurable threshold (default 50%).
    /// Only evaluates Month period snapshots with non-null velocity data.
    /// </summary>
    private sealed class VelocitySpikeRule : ISignalRule
    {
        public bool Evaluate(SpendSnapshotData snapshot, FinanceOptions options, out SignalData signal)
        {
            signal = default;

            // Velocity checks only apply to Month snapshots
            if (snapshot.PeriodType != PeriodTypeMonth)
                return false;

            // Cannot evaluate without velocity data
            if (!snapshot.VelocityPct.HasValue)
                return false;

            var velocityPct = snapshot.VelocityPct.Value;

            if (velocityPct < options.VelocitySpikePct)
                return false;

            signal = new SignalData
            {
                SignalType = SignalTypeVelocitySpike,
                SignalTypeName = "VelocitySpike",
                Severity = SeverityWarning,
                TriggerValue = velocityPct,
                ThresholdValue = options.VelocitySpikePct,
                Message = $"Velocity spike detected: spend increased {velocityPct:F1}% month-over-month " +
                          $"(threshold: {options.VelocitySpikePct}%) for period {snapshot.PeriodKey}"
            };

            return true;
        }
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Internal Data Transfer Objects
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Strongly-typed snapshot data extracted from Dataverse field dictionary.
    /// </summary>
    internal readonly struct SpendSnapshotData
    {
        public Guid Id { get; init; }
        public int PeriodType { get; init; }
        public string PeriodKey { get; init; }
        public string BucketKey { get; init; }
        public decimal InvoicedAmount { get; init; }
        public decimal? BudgetAmount { get; init; }
        public decimal? VelocityPct { get; init; }

        /// <summary>
        /// Parse snapshot data from a Dataverse field dictionary.
        /// Handles type coercion for Money (decimal/double), OptionSet (int), and Decimal fields.
        /// </summary>
        public static SpendSnapshotData FromFieldDictionary(Guid id, Dictionary<string, object?> fields)
        {
            return new SpendSnapshotData
            {
                Id = id,
                PeriodType = ConvertToInt(fields.GetValueOrDefault(SnapshotPeriodType)),
                PeriodKey = fields.GetValueOrDefault(SnapshotPeriodKey)?.ToString() ?? string.Empty,
                BucketKey = fields.GetValueOrDefault(SnapshotBucketKey)?.ToString() ?? string.Empty,
                InvoicedAmount = ConvertToDecimal(fields.GetValueOrDefault(SnapshotInvoicedAmount)),
                BudgetAmount = ConvertToNullableDecimal(fields.GetValueOrDefault(SnapshotBudgetAmount)),
                VelocityPct = ConvertToNullableDecimal(fields.GetValueOrDefault(SnapshotVelocityPct))
            };
        }

        private static int ConvertToInt(object? value) => value switch
        {
            int i => i,
            long l => (int)l,
            double d => (int)d,
            decimal m => (int)m,
            string s when int.TryParse(s, out var parsed) => parsed,
            System.Text.Json.JsonElement je when je.ValueKind == System.Text.Json.JsonValueKind.Number => je.GetInt32(),
            _ => 0
        };

        private static decimal ConvertToDecimal(object? value) => value switch
        {
            decimal m => m,
            double d => (decimal)d,
            float f => (decimal)f,
            int i => i,
            long l => l,
            string s when decimal.TryParse(s, out var parsed) => parsed,
            System.Text.Json.JsonElement je when je.ValueKind == System.Text.Json.JsonValueKind.Number => je.GetDecimal(),
            _ => 0m
        };

        private static decimal? ConvertToNullableDecimal(object? value) => value switch
        {
            null => null,
            System.Text.Json.JsonElement je when je.ValueKind == System.Text.Json.JsonValueKind.Null => null,
            _ => ConvertToDecimal(value)
        };
    }

    /// <summary>
    /// Signal data produced by a rule evaluation.
    /// </summary>
    internal struct SignalData
    {
        public int SignalType { get; init; }
        public string SignalTypeName { get; init; }
        public int Severity { get; init; }
        public decimal TriggerValue { get; init; }
        public decimal ThresholdValue { get; init; }
        public string Message { get; init; }
    }
}
