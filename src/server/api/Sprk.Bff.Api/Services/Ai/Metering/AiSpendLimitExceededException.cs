namespace Sprk.Bff.Api.Services.Ai.Metering;

/// <summary>
/// Thrown before an Azure OpenAI call when the stamp has reached its configured monthly spend limit (task 254). The
/// global exception handler maps it to 429 ProblemDetails with <c>Retry-After</c>; streaming endpoints that have
/// already started their response report <see cref="ErrorCode"/> in-band.
/// </summary>
/// <remarks>
/// Derives from <see cref="Exception"/>, not <see cref="InvalidOperationException"/> (task 077's choice): endpoints
/// catch <see cref="InvalidOperationException"/> to map their own 400/409 cases, which would swallow the 429.
/// The message is shown to end users, so it names no amounts — they are Spaarke's cost data; the log carries them.
/// </remarks>
public sealed class AiSpendLimitExceededException : Exception
{
    /// <summary>Stable error code, in the ProblemDetails <c>code</c> extension and in-band stream errors.</summary>
    public const string ErrorCode = "ai_spend_limit_exceeded";

    /// <summary>The configured monthly limit (USD).</summary>
    public decimal MonthlyLimitUsd { get; }

    /// <summary>The estimated month-to-date spend (USD) when the call was refused.</summary>
    public decimal MonthToDateUsd { get; }

    /// <summary>Time until the limit resets — the next UTC month start.</summary>
    public TimeSpan RetryAfter { get; }

    public AiSpendLimitExceededException(decimal monthlyLimitUsd, decimal monthToDateUsd, TimeSpan retryAfter)
        : base("This environment has reached its monthly AI usage limit. AI features resume at the start of next " +
               "month (UTC), or sooner if an administrator raises the limit.")
    {
        MonthlyLimitUsd = monthlyLimitUsd;
        MonthToDateUsd = monthToDateUsd;
        RetryAfter = retryAfter;
    }

    /// <summary>Whole seconds for the <c>Retry-After</c> header (at least 1).</summary>
    public long RetryAfterSeconds => Math.Max(1L, (long)Math.Ceiling(RetryAfter.TotalSeconds));
}
