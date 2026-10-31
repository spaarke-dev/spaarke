using Microsoft.Extensions.Options;
using Sprk.Bff.Api.Configuration;

namespace Sprk.Bff.Api.Api.Finance;

/// <summary>
/// Endpoint filter that enforces the eBilling kill switch (<see cref="EbillingOptions.Enabled"/>,
/// ebilling-r1 FR-45, ADR-018). When the flag is off the route answers with the canonical
/// 503 "Feature Disabled" ProblemDetails (<see cref="FeatureDisabledResults.TypeUri"/>,
/// <c>errorCode</c> = <see cref="ErrorCode"/>) and the handler is not invoked.
/// </summary>
/// <remarks>
/// <para>Add it IN ADDITION to the route's authorization filter, after it: the flag never bypasses or
/// replaces authorization (ADR-018). Job handlers cannot use an endpoint filter and check
/// <see cref="EbillingOptions.Enabled"/> directly (ADR-018 "same flag in endpoints and handlers").</para>
/// <para>The flag is read per request from <see cref="IOptionsMonitor{TOptions}"/>, so a configuration
/// change applies without a restart wherever the host reloads configuration.</para>
/// </remarks>
public sealed class EbillingEnabledFilter(IOptionsMonitor<EbillingOptions> options) : IEndpointFilter
{
    /// <summary>Stable error code carried in the 503 ProblemDetails <c>errorCode</c> extension.</summary>
    public const string ErrorCode = "ebilling.disabled";

    private const string DisabledDetail = "eBilling is not enabled in this environment.";

    public ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);

        if (!options.CurrentValue.Enabled)
        {
            return ValueTask.FromResult<object?>(
                new FeatureDisabledException(ErrorCode, DisabledDetail).AsFeatureDisabled503());
        }

        return next(context);
    }
}

/// <summary>Route-builder extension that applies <see cref="EbillingEnabledFilter"/>.</summary>
public static class EbillingEnabledFilterExtensions
{
    /// <summary>
    /// Makes the route return 503 <c>ebilling.disabled</c> when <c>Ebilling:Enabled</c> is false.
    /// Chain it AFTER the route's authorization filter.
    /// </summary>
    public static TBuilder RequireEbillingEnabled<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.AddEndpointFilter<TBuilder, EbillingEnabledFilter>();
    }
}
