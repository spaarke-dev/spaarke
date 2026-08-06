using Sprk.Bff.Api.Services.Ai.NdaStandard;

namespace Sprk.Bff.Api.Api.Ai;

/// <summary>
/// UAT round-3 D3 — serves the Spaarke Baseline NDA Standard (KNW-011, Part B) clause text by ref, so
/// the NDA review's in-document comment can reveal the standard behind each <c>standardRef</c> citation
/// (e.g. "B5 - Use &amp; disclosure obligations") on demand.
/// </summary>
/// <remarks>
/// ADR-001 (Minimal API). Registered in <c>EndpointMappingExtensions</c> as
/// <c>app.MapNdaStandardEndpoints()</c>. The content is STATIC and org-wide (KNW-011 is
/// <c>tenant=system</c>), so <c>RequireAuthorization()</c> (authN) is sufficient — no tenant scoping is
/// needed for correctness, and no AI-batch rate limiting (a trivial in-memory dictionary lookup, not a
/// model/search call). See <see cref="NdaStandardClauseProvider"/> for the clause source + ref
/// normalization.
/// <para><b>Placement justification (CLAUDE.md §10):</b> a thin, read-only lookup over static NDA-review
/// reference content, consumed by the NDA-review Compose UI. Extends the existing <c>/api/ai</c> surface
/// rather than introducing a new service tier; no new package, no external I/O, negligible publish-size.
/// Cost-of-doing-nothing: the in-document comment can only show the bare standard ref (e.g. "B5"), never
/// the clause the finding is measured against — the D3 hover feature fails.</para>
/// </remarks>
public static class NdaStandardEndpoints
{
    public static IEndpointRouteBuilder MapNdaStandardEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/ai/nda-standard")
            .RequireAuthorization()
            .WithTags("AI NDA Standard");

        group.MapGet("/clauses/{clauseRef}", GetClause)
            .WithName("NdaStandardGetClause")
            .WithSummary("Get the standard text of an NDA clause by ref")
            .WithDescription("Returns the Spaarke Baseline NDA Standard (KNW-011) clause for a ref like 'B5' or 'B5 - Use & disclosure obligations'. Keys on the leading B{n} token; unknown ref → 404.")
            .Produces<NdaStandardClause>()
            .ProducesProblem(400)
            .ProducesProblem(401)
            .ProducesProblem(403)
            .ProducesProblem(404);

        group.MapGet("/clauses", GetAllClauses)
            .WithName("NdaStandardListClauses")
            .WithSummary("List all NDA-standard clauses (B1..B16)")
            .Produces<IReadOnlyList<NdaStandardClause>>()
            .ProducesProblem(401)
            .ProducesProblem(403);

        return app;
    }

    private static IResult GetClause(string clauseRef, NdaStandardClauseProvider provider)
    {
        if (string.IsNullOrWhiteSpace(clauseRef))
        {
            return Results.Problem(detail: "A clause ref is required.", statusCode: 400, title: "Bad Request");
        }

        var clause = provider.TryResolve(clauseRef);
        return clause is null
            ? Results.Problem(
                detail: $"No NDA-standard clause matches '{clauseRef}'. Expected a B1..B16 ref (e.g. 'B5').",
                statusCode: 404, title: "Not Found")
            : Results.Ok(clause);
    }

    private static IResult GetAllClauses(NdaStandardClauseProvider provider) => Results.Ok(provider.AllClauses);
}
