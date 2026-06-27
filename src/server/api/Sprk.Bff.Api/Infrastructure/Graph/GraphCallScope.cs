using Microsoft.Graph;
using Microsoft.Graph.Models.ODataErrors;

namespace Sprk.Bff.Api.Infrastructure.Graph;

/// <summary>
/// Helper that wraps Graph SDK calls so callers (especially *Endpoints classes)
/// can catch a Spaarke-domain <see cref="SpaarkeStorageException"/> WITHOUT
/// importing the Graph SDK namespace.
///
/// This helper lives in <c>Infrastructure.Graph</c> so the import of
/// <c>Microsoft.Graph.Models.ODataErrors</c> stays inside the allowed namespace
/// per ADR-007 §1. Callers in <c>Api/</c> (endpoints) only import this helper.
///
/// Usage pattern in endpoints:
/// <code>
/// // Before (catches ODataError directly — ADR-007 violation):
/// try { var x = await service.MethodAsync(args); }
/// catch (ODataError ex) { return Results.Problem(...); }
///
/// // After (catches SpaarkeStorageException — compliant):
/// try { var x = await GraphCallScope.Run(() => service.MethodAsync(args), "context"); }
/// catch (SpaarkeStorageException ex) { return Results.Problem(...); }
/// </code>
///
/// Added 2026-06-26 by ci-cd-unit-test-remediation-r1 task CICD-088 per spec FR-A06.
/// </summary>
public static class GraphCallScope
{
    /// <summary>
    /// Run a Graph SDK call returning <typeparamref name="T"/>; translate
    /// <see cref="ODataError"/> into <see cref="SpaarkeStorageException"/>.
    /// </summary>
    public static async Task<T> Run<T>(Func<Task<T>> graphCall, string? context = null)
    {
        try { return await graphCall().ConfigureAwait(false); }
        catch (ODataError ex) { throw ex.ToSpaarkeStorageException(context); }
    }

    /// <summary>
    /// Run a Graph SDK call returning <see cref="Task"/>; translate
    /// <see cref="ODataError"/> into <see cref="SpaarkeStorageException"/>.
    /// </summary>
    public static async Task Run(Func<Task> graphCall, string? context = null)
    {
        try { await graphCall().ConfigureAwait(false); }
        catch (ODataError ex) { throw ex.ToSpaarkeStorageException(context); }
    }

    /// <summary>
    /// Resolve a <see cref="GraphServiceClient"/> for the given
    /// <see cref="SpeAdminGraphService.ContainerTypeConfig"/> and invoke a Graph
    /// SDK call returning <typeparamref name="T"/>. Endpoints use this overload
    /// so the <see cref="GraphServiceClient"/> local variable lives inside
    /// <c>Infrastructure.Graph</c> (allowed per ADR-007 §1) rather than in the
    /// endpoint method body (forbidden, surfaced by NetArchTest
    /// <c>EndpointsShouldNotReferenceGraphSdk</c>).
    /// </summary>
    /// <remarks>
    /// Added 2026-06-26 by ci-cd-unit-test-remediation-r1 task CICD-088b to make the
    /// ADR-007 endpoint fact PASS at the IL level — previously endpoints held a
    /// <c>var graphClient = await graphService.GetClientForConfigAsync(...)</c>
    /// local whose inferred type was <c>Microsoft.Graph.GraphServiceClient</c>,
    /// which NetArchTest's <c>HaveDependencyOn</c> inspects.
    /// </remarks>
    public static async Task<T> RunForConfig<T>(
        SpeAdminGraphService graphService,
        SpeAdminGraphService.ContainerTypeConfig config,
        Func<GraphServiceClient, CancellationToken, Task<T>> graphCall,
        CancellationToken ct,
        string? context = null)
    {
        var graphClient = await graphService.GetClientForConfigAsync(config, ct).ConfigureAwait(false);
        try { return await graphCall(graphClient, ct).ConfigureAwait(false); }
        catch (ODataError ex) { throw ex.ToSpaarkeStorageException(context); }
    }

    /// <summary>
    /// Resolve a <see cref="GraphServiceClient"/> for the given
    /// <see cref="SpeAdminGraphService.ContainerTypeConfig"/> and invoke a Graph
    /// SDK call returning <see cref="Task"/>. Endpoints use this overload so the
    /// <see cref="GraphServiceClient"/> local variable lives inside
    /// <c>Infrastructure.Graph</c> per ADR-007 §1.
    /// </summary>
    /// <remarks>
    /// Added 2026-06-26 by ci-cd-unit-test-remediation-r1 task CICD-088b.
    /// </remarks>
    public static async Task RunForConfig(
        SpeAdminGraphService graphService,
        SpeAdminGraphService.ContainerTypeConfig config,
        Func<GraphServiceClient, CancellationToken, Task> graphCall,
        CancellationToken ct,
        string? context = null)
    {
        var graphClient = await graphService.GetClientForConfigAsync(config, ct).ConfigureAwait(false);
        try { await graphCall(graphClient, ct).ConfigureAwait(false); }
        catch (ODataError ex) { throw ex.ToSpaarkeStorageException(context); }
    }
}
