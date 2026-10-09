using System.Diagnostics.Metrics;

namespace Sprk.Bff.Api.Telemetry;

/// <summary>
/// Makes <c>$choices</c> resolution degradation visible (issue #1049, spaarke-ontology-platform-r1 task 072).
/// </summary>
/// <remarks>
/// Resolution is best-effort (NFR-04): a Dataverse read failure degrades to "no taxonomy in the prompt/schema"
/// (category null on every capture) and, before this counter, left only a Warning log line nobody alerts on.
/// The counter rides the existing <c>Sprk.Bff.Api.Ai</c> meter (already exported to Application Insights), so
/// no new meter registration is needed. Query:
/// <c>customMetrics | where name == "ai_choices_resolution_failures_total" | summarize sum(valueSum) by tostring(customDimensions["reason"]), tostring(customDimensions["reference"])</c>.
/// </remarks>
public static class ChoicesResolutionTelemetry
{
    private static readonly Meter Meter = new("Sprk.Bff.Api.Ai", "1.0.0");

    private static readonly Counter<long> Failures = Meter.CreateCounter<long>(
        name: "ai_choices_resolution_failures_total",
        unit: "{failure}",
        description: "$choices resolutions that degraded (read failed, returned nothing, or guidance could not be read)");

    /// <summary>The Dataverse read threw.</summary>
    public const string ReasonReadFailed = "read_failed";

    /// <summary>The read returned no values (includes reads the scope resolver swallowed as an HTTP error).</summary>
    public const string ReasonNoValues = "no_values";

    /// <summary>Names resolved but guidance could not be read; the prompt carries bare names.</summary>
    public const string ReasonGuidanceReadFailed = "guidance_read_failed";

    public static void RecordFailure(string reference, string reason) =>
        Failures.Add(1,
            new KeyValuePair<string, object?>("reference", reference),
            new KeyValuePair<string, object?>("reason", reason));
}
