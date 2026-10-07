using Microsoft.Extensions.Options;

namespace Sprk.Bff.Api.Services.Ai.Metering;

/// <summary>
/// The stamp's optional monthly OpenAI spend limit (task 254, owner G37). Called by both AI client seams — every public
/// method of <see cref="OpenAiClient"/> and <see cref="AiSpendLimitChatClient"/> in the <c>IChatClient</c> pipeline:
/// <see cref="EnsureUnderLimitAsync"/> before the model call, <see cref="RecordUsage"/> after it.
/// </summary>
/// <remarks>
/// <para>No limit configured (the default) → the check is a no-op; usage is still recorded, so a limit an operator adds
/// mid-month starts from the month's real figure.</para>
/// <para>Fails OPEN on the store (task 077 §11 rationale): if the ledger cannot be read the call proceeds and a warning
/// is logged — a Redis outage must not stop every AI feature. A known over-limit month refuses.</para>
/// <para>Application Insights <c>ai.metering.tokens</c> remains the authoritative usage record; this is a list-price
/// estimate (<see cref="AiSpendLimitOptions.InputUsdPer1MTokens"/> / <see cref="AiSpendLimitOptions.OutputUsdPer1MTokens"/>).</para>
/// </remarks>
public sealed class AiSpendLimit
{
    private const decimal TokensPerMillion = 1_000_000m;

    /// <summary>
    /// Longest wait for the month-to-date figure before the call proceeds without it (fail-open). A degraded Redis would
    /// otherwise hold every model call for the client's 5 s timeout.
    /// </summary>
    internal static readonly TimeSpan LedgerReadTimeout = TimeSpan.FromMilliseconds(250);

    private int _unreadableSettingsLogged;

    private readonly IOptionsMonitor<AiSpendLimitOptions> _options;
    private readonly IAiSpendLedger _ledger;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<AiSpendLimit> _logger;

    public AiSpendLimit(
        IOptionsMonitor<AiSpendLimitOptions> options,
        IAiSpendLedger ledger,
        TimeProvider timeProvider,
        ILogger<AiSpendLimit> logger)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _ledger = ledger ?? throw new ArgumentNullException(nameof(ledger));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Throws <see cref="AiSpendLimitExceededException"/> when a limit is configured and the month-to-date estimate has
    /// reached it. Otherwise returns.
    /// </summary>
    public async ValueTask EnsureUnderLimitAsync(CancellationToken cancellationToken)
    {
        if (ReadOptions()?.EffectiveLimitUsd is not { } limit)
        {
            return;
        }

        var now = _timeProvider.GetUtcNow();
        decimal spent;
        try
        {
            spent = await _ledger.GetMonthToDateUsdAsync(now, cancellationToken).AsTask()
                .WaitAsync(LedgerReadTimeout, _timeProvider, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (TimeoutException)
        {
            _logger.LogWarning(
                "AI spend limit: the month-to-date figure was not read within {TimeoutMs} ms; the call proceeds (fail-open).",
                LedgerReadTimeout.TotalMilliseconds);
            return;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "AI spend limit: the month-to-date figure could not be read; the call proceeds (fail-open).");
            return;
        }

        if (spent < limit)
        {
            return;
        }

        _logger.LogWarning(
            "AI spend limit reached: estimated month-to-date ${Spent:F2} vs limit ${Limit:F2}. Refusing the model call.",
            spent, limit);
        throw new AiSpendLimitExceededException(limit, spent, UntilNextUtcMonth(now));
    }

    /// <summary>Adds one model call's estimated cost to the month. Never throws.</summary>
    public void RecordUsage(long inputTokens, long outputTokens)
    {
        if (inputTokens <= 0 && outputTokens <= 0)
        {
            return;
        }

        if (ReadOptions() is not { } options)
        {
            return;
        }

        var usd = (Math.Max(0L, inputTokens) / TokensPerMillion * options.InputUsdPer1MTokens)
                  + (Math.Max(0L, outputTokens) / TokensPerMillion * options.OutputUsdPer1MTokens);
        if (usd <= 0m)
        {
            return;
        }

        try
        {
            _ledger.Add(usd, _timeProvider.GetUtcNow());
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "AI spend limit: usage could not be recorded (fail-open).");
        }
    }

    /// <summary>
    /// The current options, or null when they cannot be bound — e.g. <c>AiSpendLimit__MonthlyLimitUsd</c> set by hand to
    /// something that is not a number. That is logged and read as no limit: a bad setting must not stop every AI call.
    /// </summary>
    private AiSpendLimitOptions? ReadOptions()
    {
        try
        {
            return _options.CurrentValue;
        }
        catch (InvalidOperationException ex)
        {
            // Once per process: the options cache does not keep a failed bind, so this runs on every model call.
            if (Interlocked.Exchange(ref _unreadableSettingsLogged, 1) == 0)
            {
                _logger.LogError(ex, "AI spend limit: the AiSpendLimit settings cannot be read; no limit is applied until they are fixed.");
            }
            return null;
        }
    }

    /// <summary>Time from <paramref name="nowUtc"/> to the next UTC month start.</summary>
    internal static TimeSpan UntilNextUtcMonth(DateTimeOffset nowUtc)
    {
        var utc = nowUtc.UtcDateTime;
        var nextMonth = new DateTime(utc.Year, utc.Month, 1, 0, 0, 0, DateTimeKind.Utc).AddMonths(1);
        return nextMonth - utc;
    }
}
