namespace Sprk.Bff.Api.Services.Insights.LiveFacts;

/// <summary>
/// Configuration-bound catalog of subject schemes recognized by the Insights engine
/// subject parser (r2 Wave D5 task 034 per design-a6 §2.3 + A6-D1).
/// </summary>
/// <remarks>
/// <para>
/// <b>Why a catalog</b>: hard-coding scheme names in C# would force a code deploy to add
/// <c>client:</c> or <c>contract:</c> later. The catalog lets ops register new schemes by
/// editing per-environment <c>appsettings.json</c> — provided a matching
/// <see cref="ILiveFactResolver"/> implementation has been registered in DI for the
/// scheme's <see cref="SubjectSchemeOptions.ResolverKey"/>.
/// </para>
/// <para>
/// <b>Config shape</b> (section <c>Insights:Subject</c>):
/// </para>
/// <code>
/// {
///   "Insights": {
///     "Subject": {
///       "Schemes": [
///         { "name": "matter",  "dataverseEntity": "sprk_matter",  "resolverKey": "matter"  },
///         { "name": "project", "dataverseEntity": "sprk_project", "resolverKey": "project" },
///         { "name": "invoice", "dataverseEntity": "sprk_invoice", "resolverKey": "invoice" }
///       ]
///     }
///   }
/// }
/// </code>
/// <para>
/// <b>Phase 1.5 default</b>: if the section is unbound, <see cref="ISubjectParser"/>
/// applies a built-in default catalog covering <c>matter</c>, <c>project</c>, and
/// <c>invoice</c>. This matches the three resolvers registered in <c>InsightsModule</c>.
/// </para>
/// </remarks>
public sealed class SubjectSchemeCatalogOptions
{
    /// <summary>Configuration section path (<c>Insights:Subject</c>).</summary>
    public const string SectionName = "Insights:Subject";

    /// <summary>
    /// Registered subject schemes. Empty / null means "use the built-in default catalog"
    /// per <see cref="DefaultSchemes"/>.
    /// </summary>
    public List<SubjectSchemeOptions> Schemes { get; set; } = new();

    /// <summary>
    /// Built-in default schemes per design-a6 §2.3. Used by
    /// <see cref="ISubjectParser"/> when no <c>Insights:Subject:Schemes</c> section is
    /// bound. Matches the three resolvers registered unconditionally in
    /// <c>InsightsModule</c> per design-a6 §3.4.
    /// </summary>
    public static IReadOnlyList<SubjectSchemeOptions> DefaultSchemes { get; } = new[]
    {
        new SubjectSchemeOptions { Name = "matter",  DataverseEntity = "sprk_matter",  ResolverKey = "matter"  },
        new SubjectSchemeOptions { Name = "project", DataverseEntity = "sprk_project", ResolverKey = "project" },
        new SubjectSchemeOptions { Name = "invoice", DataverseEntity = "sprk_invoice", ResolverKey = "invoice" }
    };
}

/// <summary>
/// A single registered subject scheme. Bound from
/// <c>Insights:Subject:Schemes[]</c> per <see cref="SubjectSchemeCatalogOptions"/>.
/// </summary>
public sealed class SubjectSchemeOptions
{
    /// <summary>
    /// Scheme name (the prefix that appears before the <c>:</c> in a subject, e.g.,
    /// <c>"matter"</c>). Lower-case ASCII per design-a6 §2.1.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Dataverse logical entity name the resolver reads (e.g., <c>sprk_matter</c>).
    /// Informational — the resolver itself owns the actual entity name; this field
    /// supports diagnostics and future schema-validation passes.
    /// </summary>
    public string DataverseEntity { get; set; } = string.Empty;

    /// <summary>
    /// Dictionary key under which the matching <see cref="ILiveFactResolver"/> is
    /// registered in the resolver registry (<c>IReadOnlyDictionary&lt;string,
    /// ILiveFactResolver&gt;</c>). Typically equal to <see cref="Name"/>; allowed to
    /// differ when a single resolver impl serves multiple scheme names.
    /// </summary>
    public string ResolverKey { get; set; } = string.Empty;
}
