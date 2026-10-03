namespace Sprk.Bff.Api.Services.Dataverse.FetchXml;

/// <summary>
/// Parses FetchXML and returns every distinct entity referenced — the primary entity plus every
/// link-entity, recursively for nested link-entity elements (depth-N).
/// </summary>
/// <remarks>
/// <para>
/// Used by the external module seam's FetchXML guard
/// (<c>ExternalModuleDataEndpoints.EvaluateFetchXmlGuard</c>, <c>POST /api/v1/external/api/dataverse/fetch</c>)
/// to reject crafted FetchXML whose <c>&lt;link-entity&gt;</c> elements reach outside the module's record
/// entity. See <c>010-authorization-filter-shape.md §5</c> for the original security rationale.
/// </para>
/// <para>
/// Its first consumer, <c>DataverseAuthorizationFilter</c>'s <c>FromFetchXmlBody</c> mode on the internal
/// <c>POST /api/dataverse/fetch</c>, was DELETED with that route (unified-access-control-r2 task 160).
/// </para>
/// </remarks>
internal interface IFetchXmlEntityExtractor
{
    /// <summary>
    /// Returns the distinct set of entity logical names referenced by the FetchXML payload.
    /// </summary>
    /// <param name="fetchXml">A FetchXML document (e.g., <c>&lt;fetch&gt;&lt;entity name='sprk_matter'&gt;&lt;link-entity name='sprk_financialdetail'&gt;…&lt;/link-entity&gt;&lt;/entity&gt;&lt;/fetch&gt;</c>).</param>
    /// <returns>
    /// A case-insensitive set of entity logical names — primary entity plus every <c>&lt;link-entity&gt;</c>
    /// in tree order (depth-N traversal). Empty set if no entities can be parsed.
    /// </returns>
    /// <exception cref="FetchXmlParseException">
    /// Thrown when the XML is malformed or the FetchXML schema invariants are violated
    /// (e.g., missing root <c>&lt;entity&gt;</c> element with <c>name</c> attribute). The caller
    /// converts this to a 400 ProblemDetails.
    /// </exception>
    IReadOnlySet<string> ExtractEntities(string fetchXml);
}

/// <summary>
/// Thrown by <see cref="IFetchXmlEntityExtractor"/> when FetchXML parsing fails.
/// </summary>
/// <remarks>
/// Captured by <c>DataverseAuthorizationFilter</c> and converted to a 400 ProblemDetails with
/// <c>errorCode=DV_FETCHXML_MALFORMED</c>. The exception message is sanitised — the filter does NOT
/// echo the raw FetchXML body back to the client.
/// </remarks>
internal sealed class FetchXmlParseException : Exception
{
    public FetchXmlParseException(string message) : base(message)
    {
    }

    public FetchXmlParseException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
