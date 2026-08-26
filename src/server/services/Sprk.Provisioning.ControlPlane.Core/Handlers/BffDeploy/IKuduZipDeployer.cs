// -----------------------------------------------------------------------------
// IKuduZipDeployer.cs
//
// L2 abstraction over the Kudu zip-deploy REST call H9 uses to push the
// downloaded BFF artifact to the App Service STAGING slot (task 132, Wave
// G-3, DS-4 §5 re-scope). There is no ARM SDK primitive for zip-deploy — the
// documented + Microsoft-recommended automation path is a single
// authenticated POST to the Kudu SCM endpoint's `/api/zipdeploy` route
// (DS-4 §5 point 3). Auth is an Azure AD bearer token scoped to
// `https://management.azure.com/.default` acquired from the SAME shared
// TokenCredential singleton every other Class-A collaborator in this project
// uses (ADR-028 MI-outbound) — Kudu accepts ARM-scoped AAD tokens from a
// caller holding App Service RBAC, so no publish-profile / basic-auth
// credential is needed.
//
// SEAM JUSTIFICATION (ADR-010):
//   ≥2 implementations exist from day 1:
//     - Production: <see cref="KuduZipDeployer"/> — typed HttpClient POST.
//     - Test: stubs injected per unit test that construct
//       <see cref="KuduZipDeployResult"/> directly (see
//       H9BffDeployHandlerTests); KuduZipDeployerTests exercises the real
//       request-construction path against a hand-rolled fake
//       HttpMessageHandler (ADR-038 — NOT Mock&lt;HttpMessageHandler&gt;).
//   Interface earns its keep — no NIH.
//
// ESCALATION NOTE (POML `<escalation>`): Kudu's REST surface is less formally
// documented than the ARM SDK. If a live call returns an error shape this
// port's error-handling does not recognize, the handler's outer try/catch
// captures the raw HTTP status + body into the Failure diagnostic so an
// operator has real evidence to correct the error-mapping — see
// KuduZipDeployer.DeployAsync's Failure branch.
// -----------------------------------------------------------------------------

namespace Sprk.Provisioning.ControlPlane.Handlers.BffDeploy;

/// <summary>
/// POSTs the downloaded artifact zip to
/// <c>https://{app}-{slot}.scm.azurewebsites.net/api/zipdeploy</c>. Production
/// impl uses a typed <see cref="HttpClient"/> + MI-acquired bearer token; test
/// impls return canned results.
/// </summary>
public interface IKuduZipDeployer
{
    /// <summary>
    /// Deploys the zip at <see cref="KuduZipDeployRequest.LocalZipPath"/> to
    /// the named App Service slot. Domain failures (Kudu returns a non-2xx
    /// status) do NOT throw — carried in <see cref="KuduZipDeployResult.Failure"/>.
    /// Infrastructure faults (DNS failure, timeout, local zip missing) MAY
    /// throw.
    /// </summary>
    Task<KuduZipDeployResult> DeployAsync(KuduZipDeployRequest request, CancellationToken cancellationToken);
}

/// <summary>Inputs to a single Kudu zip-deploy invocation.</summary>
/// <param name="AppServiceName">BFF App Service name (production slot binds to <c>https://{name}.azurewebsites.net</c>).</param>
/// <param name="SlotName">Target deployment slot — H9 ALWAYS deploys to staging (never directly to production).</param>
/// <param name="LocalZipPath">Absolute path to the artifact zip <see cref="IBffArtifactDownloader"/> downloaded.</param>
public sealed record KuduZipDeployRequest(string AppServiceName, string SlotName, string LocalZipPath);

/// <summary>
/// Discriminated result of <see cref="IKuduZipDeployer.DeployAsync"/>.
/// </summary>
public abstract record KuduZipDeployResult
{
    private KuduZipDeployResult() { }

    /// <summary>Kudu returned a 2xx status — the zip contents are now live on the target slot.</summary>
    public sealed record Success(TimeSpan Duration) : KuduZipDeployResult;

    /// <summary>
    /// Kudu returned a non-2xx status. <paramref name="Diagnostic"/> carries
    /// the raw HTTP status + a truncated response body (evidence for the
    /// POML's escalation trigger if the shape is unrecognized).
    /// </summary>
    public sealed record Failure(string Diagnostic) : KuduZipDeployResult;
}
