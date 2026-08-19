// -----------------------------------------------------------------------------
// HandlerIds.cs
//
// L2 CONTROL-PLANE canonical HandlerId string-constant catalog (task 103,
// Phase C'' Wave G-1 -- closes DS-2 §3.2 gap C1.2).
//
// PURPOSE:
//   Single source of truth for every HandlerId literal used across the L2
//   control plane: DagAdvancer's dependency-DAG map, each of the 22 handler
//   classes' own HandlerId-backing const, the keyed-DI registration surface
//   (HandlerDispatchRegistrationModule), and (task 102) the dispatcher's
//   envelope-driven GetKeyedService<IProvisioningHandler> resolution.
//
//   An enum was rejected (DS-2 §3.2): "H0.5" and "H2a" are not valid enum
//   identifiers without attribute mapping, the wire format (HandlerEnvelope
//   JSON + Cosmos ProvisioningRun.CurrentPhase) is already string, and
//   DagAdvancer's dependency map is string-keyed.
//
// DISPATCHABLE VS SUB-STEP:
//   19 of the 22 handler classes are independently enqueued via
//   HandlerEnvelope.HandlerId and therefore need a keyed DI registration --
//   these are <see cref="Dispatchable"/>. The remaining 3 (H14a/H14b/H14c)
//   are orchestrated IN-PROCESS by H14IntegrationWiringHandler (ONE
//   ReplaceRunAsync call per H14IntegrationWiringHandler.cs:45) and are
//   NEVER independently enqueued -- they are deliberately EXCLUDED from
//   Dispatchable and MUST NOT be keyed-registered
//   (HandlerRegistrationCompletenessTests asserts their absence).
// -----------------------------------------------------------------------------

namespace Sprk.Provisioning.ControlPlane.Handlers;

/// <summary>
/// Canonical HandlerId catalog. Values match design.md §4.1's handler-catalog
/// column verbatim. DagAdvancer's <c>HandlerH*</c> consts and each handler's
/// own <c>HandlerIdentifier</c> (or <c>HandlerIdConstant</c> for H0.5) const
/// re-point here (mechanical refactor, no behavior change).
/// </summary>
public static class HandlerIds
{
    /// <summary>H0 -- preflight quota/readiness gate. Entry point (POST /api/runs); never reconciler-dispatched.</summary>
    public const string H0 = "H0";

    /// <summary>H0.5 -- Model 2 consent-capture. Entry point (BFF consent-callback); never reconciler-dispatched.</summary>
    public const string H05 = "H0.5";

    /// <summary>H1 -- subscription readiness (ARM reachability + Lighthouse delegation).</summary>
    public const string H1 = "H1";

    /// <summary>H2a -- Bicep infrastructure deploy.</summary>
    public const string H2a = "H2a";

    /// <summary>H2b -- AI Search index provisioning.</summary>
    public const string H2b = "H2b";

    /// <summary>H3 -- Entra app-reg (14 grants, admin consent, KV writes).</summary>
    public const string H3 = "H3";

    /// <summary>H4 -- KV secrets population + T1 keyVaultReferenceIdentity PATCH.</summary>
    public const string H4 = "H4";

    /// <summary>H5 -- Dataverse environment creation.</summary>
    public const string H5 = "H5";

    /// <summary>H6 -- Dataverse solution import (8 solutions, dependency-ordered).</summary>
    public const string H6 = "H6";

    /// <summary>H7 -- Dataverse environment-variable values.</summary>
    public const string H7 = "H7";

    /// <summary>H8 -- SPE container-type creation.</summary>
    public const string H8 = "H8";

    /// <summary>H9 -- BFF artifact-based deploy (CI-published blob).</summary>
    public const string H9 = "H9";

    /// <summary>H10 -- Dataverse App User + Graph app-role parity.</summary>
    public const string H10 = "H10";

    /// <summary>H11 -- end-user provisioning (Graph B2B invite + consent).</summary>
    public const string H11 = "H11";

    /// <summary>H12a -- AI seed chain.</summary>
    public const string H12a = "H12a";

    /// <summary>H12b -- App Configuration seed.</summary>
    public const string H12b = "H12b";

    /// <summary>H12c -- runtime model-deployment references (3-way join: H12a + H12b + H2a).</summary>
    public const string H12c = "H12c";

    /// <summary>H13 -- E2E acceptance gate (final handler).</summary>
    public const string H13 = "H13";

    /// <summary>H14 -- post-deploy integration wiring (parent of H14a/b/c in-process sub-steps).</summary>
    public const string H14 = "H14";

    /// <summary>
    /// H14a -- Exchange ApplicationAccessPolicy sub-step. In-process only
    /// (see class remarks) -- NOT in <see cref="Dispatchable"/>.
    /// </summary>
    public const string H14a = "H14a";

    /// <summary>
    /// H14b -- Graph webhook-subscription sub-step. In-process only
    /// (see class remarks) -- NOT in <see cref="Dispatchable"/>.
    /// </summary>
    public const string H14b = "H14b";

    /// <summary>
    /// H14c -- Dataverse service-endpoint webhook sub-step. In-process only
    /// (see class remarks) -- NOT in <see cref="Dispatchable"/>.
    /// </summary>
    public const string H14c = "H14c";

    /// <summary>
    /// The 19 envelope-dispatchable ids (design.md §4.1 handler catalog).
    /// H14a/H14b/H14c are deliberately EXCLUDED -- orchestrated in-process
    /// by the H14 parent handler, never independently enqueued (DS-2 §3.2).
    /// This is the completeness surface task 102's dispatcher + this
    /// project's HandlerRegistrationCompletenessTests both consume.
    /// </summary>
    public static readonly IReadOnlyList<string> Dispatchable =
    [
        H0, H05, H1, H2a, H2b, H3, H4, H5, H6, H7, H8, H9, H10, H11, H12a, H12b, H12c, H13, H14,
    ];
}
