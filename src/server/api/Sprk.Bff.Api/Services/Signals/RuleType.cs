namespace Sprk.Bff.Api.Services.Signals;

/// <summary>
/// The closed set of <c>sprk_policyversion.sprk_ruletype</c> values (spec FR-05; design.md CM-7;
/// component model decision 7).
/// </summary>
/// <remarks>
/// <para>
/// <b>Mirrors the Dataverse choice column exactly</b> — <c>sprk_policyversion.sprk_ruletype</c> is a Choice
/// column with options <c>Threshold</c> (100000000), <c>Switch</c> (100000001), <c>Existence</c> (100000002)
/// (confirmed via <c>DESCRIBE TABLE sprk_policyversion</c>, 2026-10-03). The three numeric values below are
/// the Dataverse option-set values, not arbitrary — do not renumber.
/// </para>
/// <para>
/// <b>Closed by design, not data-driven.</b> Per design.md CM-7/CM-8 and project CLAUDE.md §3.4, this is
/// deliberately a plain C# enum rather than a lookup table or a generic "rule type registry" — adding a
/// fourth member (e.g. <c>Transition</c> or <c>Trend</c>, both explicitly deferred) is a code change with
/// review, never a configuration row. <see cref="RuleBodySchemaValidator"/> switches on this enum exhaustively;
/// extending the set means extending that switch, not adding data.
/// </para>
/// </remarks>
public enum RuleType
{
    /// <summary>A numeric comparison against a computed/field value (e.g. budget utilization ratio).</summary>
    Threshold = 100000000,

    /// <summary>A boolean kill-switch, e.g. suppressing a signal type tenant-wide.</summary>
    Switch = 100000001,

    /// <summary>
    /// An EXISTS / NOT EXISTS assertion over a related entity, expressed as independent ANDed clauses with
    /// no cross-clause variable binding (design.md §8.0.1(a); CM-7, added 2026-09-30). Without this type the
    /// project's one differentiated predicate — a classified communication AND no budget revision in the
    /// same window — has no body shape it can be saved as.
    /// </summary>
    Existence = 100000002,
}
