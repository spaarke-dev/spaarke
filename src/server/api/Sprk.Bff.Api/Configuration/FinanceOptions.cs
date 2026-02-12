namespace Sprk.Bff.Api.Configuration;

/// <summary>
/// Configuration for the Finance Intelligence Module.
/// Bound to the "Finance" configuration section.
/// </summary>
public class FinanceOptions
{
    /// <summary>
    /// Configuration section name.
    /// </summary>
    public const string SectionName = "Finance";

    /// <summary>
    /// Below this confidence, classify as Unknown instead of InvoiceCandidate.
    /// </summary>
    public decimal ClassificationConfidenceThreshold { get; set; } = 0.5m;

    /// <summary>
    /// Fire BudgetWarning signal at this percentage of budget consumed.
    /// </summary>
    public decimal BudgetWarningPercentage { get; set; } = 80m;

    /// <summary>
    /// Fire VelocitySpike signal when spend increases by this percentage month-over-month.
    /// </summary>
    public decimal VelocitySpikePct { get; set; } = 50m;

    /// <summary>
    /// Finance summary cache TTL in minutes. Explicit invalidation is primary; TTL is safety net.
    /// </summary>
    public int FinanceSummaryCacheTtlMinutes { get; set; } = 5;

    /// <summary>
    /// Azure OpenAI deployment name for classification (Playbook A). gpt-4o-mini for speed/cost.
    /// </summary>
    public string ClassificationDeploymentName { get; set; } = "gpt-4o-mini";

    /// <summary>
    /// Azure OpenAI deployment name for extraction (Playbook B). gpt-4o for accuracy.
    /// </summary>
    public string ExtractionDeploymentName { get; set; } = "gpt-4o";
}
