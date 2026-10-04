using System.Diagnostics.Metrics;

namespace Sprk.Bff.Api.Telemetry;

/// <summary>
/// <c>customMetrics/ontology.writer.failures</c> (task 030 rework — owner directive 2026-10-04: "the writer
/// fails closed by design; a broken credential or a refused write must not look like 'no conditions found'").
/// Dimensioned by <c>reason</c> (see <see cref="OntologyWriterFailureReason"/>) so a future alert rule (task
/// 035) can break down WHY the writer failed without a second metric.
/// </summary>
/// <remarks>
/// Follows the repo's EXISTING Meter-per-feature convention — <c>CacheMetrics</c>, <c>FinanceTelemetry</c>,
/// <c>CircuitBreakerRegistry</c> — a single static <see cref="Meter"/> owning this feature's instruments, no
/// new telemetry package. MUST be registered via <c>TelemetryModule.AddMeter(MeterName)</c> or the metric is
/// silently dropped from the App Insights export — the exact trap <c>TelemetryModule.cs</c>'s own comments
/// record for the Event Rules and Compose-save meters (both shipped once, unregistered, before being caught).
/// </remarks>
public static class OntologyWriterTelemetry
{
    /// <summary>Meter name registered in <c>TelemetryModule.AddMeter(...)</c>.</summary>
    public const string MeterName = "Sprk.Bff.Api.Ontology";

    private static readonly Meter Meter = new(MeterName, "1.0.0");

    private static readonly Counter<long> FailuresCounter = Meter.CreateCounter<long>(
        name: "ontology.writer.failures",
        unit: "{failure}",
        description: "Count of Signal-writer refusals/failures by reason. A fail-closed writer with zero " +
                     "observed failures is not evidence of health — it may mean the failures are silent.");

    /// <summary>
    /// Records one Signal-writer refusal/failure. <paramref name="reason"/> MUST be one of
    /// <see cref="OntologyWriterFailureReason"/>'s bounded-cardinality constants — never a raw exception
    /// message, a policy code, or any fact/sentence content.
    /// </summary>
    public static void RecordFailure(string reason) =>
        FailuresCounter.Add(1, new KeyValuePair<string, object?>("reason", reason));
}

/// <summary>
/// Bounded-cardinality reason tags shared by <see cref="OntologyWriterTelemetry.RecordFailure"/>'s metric
/// dimension and the matching Error log's structured <c>reason</c> property (task 030 rework), so a log query
/// and a metric query always agree on vocabulary. Adding a new writer failure mode means adding a constant
/// here, not inventing an ad hoc string at the call site.
/// </summary>
public static class OntologyWriterFailureReason
{
    /// <summary>The writer's dedicated managed-identity credential is unset, or the dedicated
    /// <see cref="Sprk.Bff.Api.Services.Signals.OntologyWriterDataverseClient"/> connection could not be
    /// established.</summary>
    public const string CredentialUnresolvable = "credential_unresolvable";

    /// <summary>The pinned credential resolved, but acquiring a token from it failed (e.g. a transient AAD/MI
    /// token-endpoint error).</summary>
    public const string TokenAcquisitionFailed = "token_acquisition_failed";

    /// <summary>The credential resolved and a token was acquired, but the <see cref="Microsoft.PowerPlatform.Dataverse.Client.ServiceClient"/>
    /// connect handshake itself failed (network, org lookup, etc.) — distinct from
    /// <see cref="TokenAcquisitionFailed"/> (R7, second independent review).</summary>
    public const string ConnectFailed = "connect_failed";

    /// <summary>Dataverse refused the writer's own operation (403 / <c>0x80040220</c> / <c>0x80040299</c> —
    /// a privilege the writer's role union does not grant).</summary>
    public const string DataverseAccessDenied = "dataverse_access_denied";

    /// <summary>The grouping matter's subject type has no verified matter-derivation path
    /// (<see cref="Sprk.Bff.Api.Services.Signals.SignalWriter.VerifiedMatterDerivation"/>).</summary>
    public const string MatterDerivationUnverified = "matter_derivation_unverified";

    /// <summary>The subject's own matter-lookup field (e.g. <c>sprk_communication.sprk_regardingmatter</c>)
    /// is empty.</summary>
    public const string MatterLookupEmpty = "matter_lookup_empty";

    /// <summary>The grouping matter has no <c>owningbusinessunit</c>.</summary>
    public const string OwningBusinessUnitNotFound = "owning_business_unit_not_found";

    /// <summary>After create, the row's <c>owningbusinessunit</c> did not match the intended business unit —
    /// most likely <c>EnableOwnershipAcrossBusinessUnits</c> is disabled in this environment, OR (R1, second
    /// independent review) the row was found to have drifted on a RECONCILE (re-evaluation) read, not only on
    /// the post-create read-back.</summary>
    public const string OwningBusinessUnitMismatch = "owning_business_unit_mismatch";

    /// <summary>The Signal was created, but reading its <c>owningbusinessunit</c> back to verify it (or, on
    /// reconcile, re-reading it) itself failed (R1, second independent review) — distinct from
    /// <see cref="OwningBusinessUnitMismatch"/>, which means the read SUCCEEDED and disagreed.</summary>
    public const string OwningBusinessUnitReadBackFailed = "owning_business_unit_read_back_failed";

    /// <summary>The message template could not be rendered strictly from the detection-time fact snapshot — a
    /// referenced token is absent, null, or a leftover placeholder survived rendering (§0.3; R2, second
    /// independent review). This is a refused write under the owner's no-silent-failure rule, logged and
    /// metered WITHOUT the rendered content (R2 removed the rendered string from the exception message).</summary>
    public const string SentenceTemplateInvalid = "sentence_template_invalid";
}
