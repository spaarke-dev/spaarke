// -----------------------------------------------------------------------------
// IEnvVarValuesWriter.cs
//
// L2 abstraction over the actual Dataverse environmentvariablevalue upsert
// invocation. The production implementation
// (<see cref="DataverseWebApiEnvVarValuesWriter"/>) issues direct Dataverse
// Web API REST calls (find-definition-by-schema-name, then PATCH-if-exists /
// POST-if-not on environmentvariablevalues) — the exact sequence
// scripts/Provision-Customer.ps1 Step 8 performs today. Unit tests inject
// stubs to avoid HTTP + auth round-trips.
//
// SEAM JUSTIFICATION (ADR-010):
//   ≥2 implementations exist from day 1:
//     - Production: <see cref="DataverseWebApiEnvVarValuesWriter"/> — issues
//       Dataverse Web API calls with an OAuth2 client-credentials token.
//     - Test: stubs injected per unit test that construct
//       <see cref="EnvVarValuesWriteOutcome"/> directly (see
//       <c>H7DataverseEnvVarValuesHandlerTests</c>).
//   Interface earns its keep — no NIH.
//
// DESIGN CHOICE (direct REST vs pac CLI shell-out):
//   Unlike H5 (pac admin create-environment) and H6 (pac solution import),
//   H7's reference implementation (scripts/Provision-Customer.ps1 Step 8) is
//   ALREADY a set of direct Dataverse Web API REST calls (Invoke-RestMethod
//   against environmentvariabledefinitions + environmentvariablevalues), not
//   a pac CLI subcommand. There is no `pac` verb for env-var value CRUD.
//   Re-implementing the SAME direct-REST approach in C# (rather than
//   shelling out to a wrapper script) avoids introducing an unnecessary
//   PowerShell process boundary for what is fundamentally a small number of
//   single-record HTTP upserts — parity with H5's DataverseWebApiHealthProbe,
//   which also calls the Web API directly rather than shelling out.
// -----------------------------------------------------------------------------

namespace Sprk.Provisioning.ControlPlane.Handlers.EnvVarValues;

/// <summary>
/// Upserts a set of Dataverse <c>environmentvariablevalue</c> records for
/// handler H7. Production impl issues direct Web API REST calls; test impls
/// return canned <see cref="EnvVarValuesWriteOutcome"/>s.
/// </summary>
public interface IEnvVarValuesWriter
{
    /// <summary>
    /// Upserts every (schemaName, value) pair in <paramref name="request"/>'s
    /// <see cref="EnvVarValuesWriteRequest.Values"/> against the target
    /// Dataverse environment. Returns a typed outcome — success carries the
    /// set of variables actually written; failure carries the FIRST failing
    /// schema name (if applicable) + a classified diagnostic. Domain failures
    /// do NOT throw; infrastructure faults MAY throw (parity with
    /// <see cref="Preflight.IPreflightQuotaProbe"/>).
    /// </summary>
    /// <param name="request">Target env + tenant + the resolved 7 canonical (schemaName, value) pairs.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<EnvVarValuesWriteOutcome> WriteAsync(
        EnvVarValuesWriteRequest request,
        CancellationToken cancellationToken);
}

/// <summary>
/// Inputs to a single env-var-values upsert invocation. Immutable record; the
/// caller (<see cref="H7DataverseEnvVarValuesHandler"/>) constructs one per
/// run from resolved <see cref="Models.InterStepState"/> +
/// <see cref="Models.RunParameters"/> values.
/// </summary>
/// <param name="TargetDataverseUrl">
/// Target customer Dataverse env URL (H5 output —
/// <see cref="Models.InterStepState.DataverseEnvUrl"/>).
/// </param>
/// <param name="TenantId">
/// Entra tenant id (§4D I1 — MUST be explicit, never default). Drives the
/// OAuth2 client-credentials token request's tenant authority.
/// </param>
/// <param name="ClientId">
/// BFF Entra app registration id (H3 output —
/// <see cref="Models.InterStepState.BffAppRegId"/>) used as the
/// client-credentials confidential-client identity.
/// </param>
/// <param name="ClientSecret">
/// Client secret for <paramref name="ClientId"/> (from
/// <see cref="EnvVarValuesOptions.ClientSecret"/>). A44.5 (task 205i): MAY be
/// <c>null</c>/empty on secret-free environments — the production writer then
/// resolves its credential from the FR-39 ordered chain
/// (<see cref="Credentials.WorkerDataverseCredentialFactory"/>, MI-FIC first).
/// Empty is the SIGNAL (auth-v4 §9.1); never pass a sentinel value.
/// </param>
/// <param name="Values">
/// The resolved 7 canonical (schemaName, value) pairs, in the fixed
/// declaration order from <see cref="H7DataverseEnvVarValuesHandler.CanonicalSchemaNamesInOrder"/>.
/// Values MAY be empty string (e.g. <c>sprk_ShareLinkBaseUrl</c> when the
/// operator did not supply <c>shareLinkBaseUrl</c>) but are never null.
/// </param>
public sealed record EnvVarValuesWriteRequest(
    string TargetDataverseUrl,
    string TenantId,
    string ClientId,
    string? ClientSecret,
    IReadOnlyList<KeyValuePair<string, string>> Values);

/// <summary>
/// Discriminated result of <see cref="IEnvVarValuesWriter.WriteAsync"/>.
/// Exhaustive: <see cref="Success"/> | <see cref="Failure"/>.
/// </summary>
public abstract record EnvVarValuesWriteOutcome
{
    private EnvVarValuesWriteOutcome() { }

    /// <summary>All (schemaName, value) pairs were successfully upserted.</summary>
    /// <param name="WrittenVariables">The schema names + values that were written, in write order.</param>
    public sealed record Success(
        IReadOnlyList<KeyValuePair<string, string>> WrittenVariables) : EnvVarValuesWriteOutcome;

    /// <summary>
    /// The upsert failed on one variable (writer stops at the first failure —
    /// every write is a natural upsert so a full handler retry is always
    /// safe; there is no need to continue past the first failure).
    /// </summary>
    /// <param name="FailureKind">Classified failure mode (see <see cref="EnvVarValuesWriteFailureKind"/>).</param>
    /// <param name="SchemaName">The schema name being written when the failure occurred, if known.</param>
    /// <param name="Diagnostic">Operator-facing message.</param>
    public sealed record Failure(
        EnvVarValuesWriteFailureKind FailureKind,
        string? SchemaName,
        string Diagnostic) : EnvVarValuesWriteOutcome;
}

/// <summary>
/// Classified failure kinds surfaced by <see cref="IEnvVarValuesWriter"/>.
/// The handler maps each to an <see cref="EnvVarValuesRejectionCodes"/> value.
/// Every kind classifies §4C Resumable (see file header) — there is no
/// QuarantineRequired mapping for H7.
/// </summary>
public enum EnvVarValuesWriteFailureKind
{
    /// <summary>
    /// Token acquisition failed OR Dataverse Web API returned 401/403.
    /// Handler maps to <see cref="EnvVarValuesRejectionCodes.DataverseAuthFailure"/>.
    /// </summary>
    AuthFailure = 1,

    /// <summary>
    /// Dataverse Web API returned 429 (rate-limited). Handler maps to
    /// <see cref="EnvVarValuesRejectionCodes.RateLimited"/>.
    /// </summary>
    RateLimited = 2,

    /// <summary>
    /// The <c>environmentvariabledefinition</c> lookup by schema name
    /// returned zero rows. Handler maps to
    /// <see cref="EnvVarValuesRejectionCodes.EnvVarDefinitionNotFound"/>.
    /// </summary>
    DefinitionNotFound = 3,

    /// <summary>
    /// Unclassified HTTP/transport failure. Handler maps to
    /// <see cref="EnvVarValuesRejectionCodes.WriterInvocationFailed"/>.
    /// </summary>
    UnknownInvocationFailure = 4,
}
