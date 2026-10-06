// -----------------------------------------------------------------------------
// DagAdvancer.cs
//
// L2 CONTROL-PLANE handler-dependency computation impl (task 058, Wave C5).
//
// ENCODES design.md §4.1 handler dependency DAG:
//
//   H0.5 (Model 2 consent-capture — BFF endpoint dispatches; never reconciler)
//     ↓ (Model 2 only, chained by H0.5 handler)
//   H0 (preflight — POST /api/runs dispatches; never reconciler)
//     ↓
//   H1 (subscription readiness)
//     ↓
//   H2a (Bicep infra deploy)
//     ↓
//     ├── H2b (AI Search indexes)
//     ├── H4 (per-tenant KV secrets + T1 patch)
//     │     │
//     │     ├──→ H4b (BulkAppSettings — task 201 / F20/F20a; needs H4)
//     │     │     │
//     │     │     └──→ (feeds H9 below)
//     │     │
//     │     ↓
//     │     H3 (Entra app-reg — needs KV for secret storage)
//     │       ├── H8 (SPE container CREATION — H8-B, task 214; container-type is a pre-existing operator prereq per SPAARKE-SPE-TOPOLOGY-SETUP-RUNBOOK.md;
//     │       │        also waits for H5 — it binds the container to the environment's root business unit; H7 waits for H8)
//     │       └── H9 (BFF deploy — needs H3 AND H4b so KV refs + batched
//     │                app-settings are landed before BFF boot / F20 chain)
//     └── H5 (Dataverse env create)
//           ↓
//           H6 (solution import)
//             ↓
//             H7 (env-var values — also waits for H8: it writes H8's BOUND root container)
//               ↓
//               H10 (Dataverse App User + Graph parity)
//                 ↓
//                 H11 (user provisioning)
//                   ├── H12a (AI seed chain)
//                   └── H12b (app-config seed)         # parallel with H12a
//   H12c (runtime references) needs H12a + H12b + H2a  # 3-way join
//     ↓
//   H14 (post-deploy integration wiring — parent of H14a/b/c parallel sub-steps)
//     ↓
//   H13 (E2E acceptance gate — final)
//
// The DAG map below MUST match the diagram exactly. Any addition/removal of a
// handler requires a paired update to the diagram above + tests in
// Reconciler/DagAdvancerTests.cs.
//
// ENTRY POINTS (H0, H0.5) — DELIBERATELY EXCLUDED from the reconciler's ready
// set (see IDagAdvancer.ComputeReadyHandlers remarks): both are dispatched by
// their own transport-layer trigger (POST /api/runs for H0; BFF consent-callback
// for H0.5) and never re-dispatched by the reconciler. Their empty dependency
// requirement would otherwise cause an infinite ready-loop on every fresh run.
// -----------------------------------------------------------------------------

using Sprk.Provisioning.ControlPlane.Handlers;
using Sprk.Provisioning.ControlPlane.Models;

namespace Sprk.Provisioning.ControlPlane.Reconciler;

/// <inheritdoc cref="IDagAdvancer"/>
public sealed class DagAdvancer : IDagAdvancer
{
    // Task 103: these consts now re-point to the canonical HandlerIds
    // catalog (Handlers/HandlerIds.cs) — mechanical refactor, values
    // unchanged. Names kept for source-compat with existing consumers.

    /// <summary>Handler identifier for H0 preflight — entry point, never reconciler-dispatched.</summary>
    public const string HandlerH0 = HandlerIds.H0;

    /// <summary>Handler identifier for H0.5 consent-capture — Model 2 entry, never reconciler-dispatched.</summary>
    public const string HandlerH05 = HandlerIds.H05;

    /// <summary>Handler identifier for H1 subscription-readiness.</summary>
    public const string HandlerH1 = HandlerIds.H1;

    /// <summary>Handler identifier for H2a Bicep infra deploy.</summary>
    public const string HandlerH2a = HandlerIds.H2a;

    /// <summary>Handler identifier for H2b AI Search index provisioning.</summary>
    public const string HandlerH2b = HandlerIds.H2b;

    /// <summary>Handler identifier for H3 Entra app-reg.</summary>
    public const string HandlerH3 = HandlerIds.H3;

    /// <summary>Handler identifier for H4 KV secrets population + T1 patch.</summary>
    public const string HandlerH4 = HandlerIds.H4;

    /// <summary>
    /// Handler identifier for H4b (task 201) — BulkAppSettings thin wrapper.
    /// Runs AFTER H4, BEFORE H9; kills the F20/F20a progressive
    /// fail-fast chain by landing ALL required BFF app-settings in ONE
    /// batched call → ONE App Service restart cycle before BFF zip-deploy.
    /// </summary>
    public const string HandlerH4b = HandlerIds.H4b;

    /// <summary>Handler identifier for H5 Dataverse env creation.</summary>
    public const string HandlerH5 = HandlerIds.H5;

    /// <summary>Handler identifier for H6 solution import.</summary>
    public const string HandlerH6 = HandlerIds.H6;

    /// <summary>Handler identifier for H7 Dataverse env-var values.</summary>
    public const string HandlerH7 = HandlerIds.H7;

    /// <summary>Handler identifier for H8 SPE container CREATION (H8-B semantics; container-type is a pre-existing operator prereq per docs/guides/SPAARKE-SPE-TOPOLOGY-SETUP-RUNBOOK.md).</summary>
    public const string HandlerH8 = HandlerIds.H8;

    /// <summary>Handler identifier for H9 BFF deploy.</summary>
    public const string HandlerH9 = HandlerIds.H9;

    /// <summary>Handler identifier for H10 Dataverse App User + Graph parity.</summary>
    public const string HandlerH10 = HandlerIds.H10;

    /// <summary>Handler identifier for H11 user provisioning.</summary>
    public const string HandlerH11 = HandlerIds.H11;

    /// <summary>Handler identifier for H12a AI seed chain.</summary>
    public const string HandlerH12a = HandlerIds.H12a;

    /// <summary>Handler identifier for H12b app-config seed.</summary>
    public const string HandlerH12b = HandlerIds.H12b;

    /// <summary>Handler identifier for H12c runtime references.</summary>
    public const string HandlerH12c = HandlerIds.H12c;

    /// <summary>Handler identifier for H13 E2E acceptance gate.</summary>
    public const string HandlerH13 = HandlerIds.H13;

    /// <summary>Handler identifier for H14 post-deploy integration wiring.</summary>
    public const string HandlerH14 = HandlerIds.H14;

    /// <summary>
    /// Handler-dependency map per design.md §4.1 DAG. Key = handler that
    /// becomes ready; Value = handlers whose completion is required first.
    ///
    /// H0 + H0.5 are documented for completeness but the reconciler never
    /// dispatches them (see class summary and <see cref="EntryPointHandlers"/>).
    /// </summary>
    internal static readonly IReadOnlyDictionary<string, string[]> HandlerDependencies =
        new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            [HandlerH0] = Array.Empty<string>(),                            // Entry point (POST /api/runs).
            [HandlerH05] = Array.Empty<string>(),                            // Model 2 entry (BFF consent-callback).
            [HandlerH1] = new[] { HandlerH0 },
            [HandlerH2a] = new[] { HandlerH1 },
            [HandlerH2b] = new[] { HandlerH2a },
            [HandlerH4] = new[] { HandlerH2a },
            [HandlerH5] = new[] { HandlerH2a },
            [HandlerH3] = new[] { HandlerH4 },                              // Needs KV for secret storage.
            [HandlerH4b] = new[] { HandlerH4, HandlerH3, HandlerH5 },       // Task 201 / F20 — batched app-settings needs the customer KV populated. (T226 2026-09-30: H4-shared retired.) T245a: + H3 — AzureAd__ClientId is H3's InterStepState.BffAppRegId. T245b: + H5 — Dataverse__ServiceUrl/EnvironmentUrl are H5's InterStepState.DataverseEnvUrl.
            [HandlerH6] = new[] { HandlerH5, HandlerH3 },                   // T245a: + H3 — H6 reads InterStepState.BffAppRegId (H3 output).
            [HandlerH7] = new[] { HandlerH6, HandlerH8, HandlerH9 },        // T245a: + H8 — H7 writes the SPE container id env var from InterStepState.SpeContainerId (H8 output). H8 hands the container off only once it is BOUND to its business unit (task 165, owner round 41 item 1). T245b: + H9 — sprk_BffApiBaseUrl is InterStepState.BffApiUrl (H9 output).
            // H5 too (unified-access-control-r2 task 165, owner round 35 item 1): H8 binds the container it creates to the
            // customer environment's ROOT business unit, which exists only once H5 has created the environment — a container
            // nobody can bind is never created.
            [HandlerH8] = new[] { HandlerH3, HandlerH5 },                   // H8 is Graph-based SPE container CREATION (per-customer; H8-B rewrite per task 214, 2026-08-30). Container-TYPE is a pre-existing per-model operator prereq (docs/guides/SPAARKE-SPE-TOPOLOGY-SETUP-RUNBOOK.md steps 3+7). H3 dep kept for ordering, not data (T245b): H8 authenticates as the container type's OWNING app (SpeContainerOptions.ContainerTypeOwners), no longer as H3's BffAppRegId; T227's per-customer container-type grant for the BFF app (G9) will make it a data edge again.
            [HandlerH9] = new[] { HandlerH3, HandlerH4b },                  // EXEC-01: BFF boot needs KV refs + batched app-settings; gate on H4b (which transitively gates on H4).
            [HandlerH10] = new[] { HandlerH7 },
            [HandlerH11] = new[] { HandlerH10 },
            [HandlerH12a] = new[] { HandlerH11 },
            [HandlerH12b] = new[] { HandlerH11 },                             // Parallel with H12a.
            [HandlerH12c] = new[] { HandlerH12a, HandlerH12b, HandlerH2a },   // Join per task 072 + H14 handler code.
            [HandlerH14] = new[] { HandlerH12c, HandlerH9 },                  // T245b: + H9 — webhook receivers are InterStepState.BffApiUrl (H9 output); H13 ← H14 so H13 also follows H9.
            [HandlerH13] = new[] { HandlerH14 },
        };

    /// <summary>
    /// Handlers the reconciler MUST NEVER dispatch — dispatched by the
    /// endpoint layer / BFF instead. Any transitive resume needed for these
    /// is task 060 (I6 crash recovery) territory, not task 058.
    /// </summary>
    internal static readonly IReadOnlySet<string> EntryPointHandlers =
        new HashSet<string>(StringComparer.Ordinal) { HandlerH0, HandlerH05 };

    /// <inheritdoc/>
    public IReadOnlyList<string> ComputeReadyHandlers(ProvisioningRun run)
    {
        ArgumentNullException.ThrowIfNull(run);

        // Terminal statuses -> no advancement. The scanner should not return
        // these anyway (its filter is status ∈ {Running, WaitingOnGate}) but
        // defense-in-depth: if a caller invokes ComputeReadyHandlers directly
        // (e.g. a unit test constructing a Completed run), we still return
        // an empty set rather than accidentally re-dispatching a completed
        // pipeline.
        if (run.Status is RunStatus.Completed
            or RunStatus.Failed
            or RunStatus.Cancelled
            or RunStatus.Quarantined)
        {
            return Array.Empty<string>();
        }

        // Snapshot completed phases into a set for O(1) lookup during dep checks.
        var completed = new HashSet<string>(
            run.CompletedPhases.Select(cp => cp.Phase),
            StringComparer.Ordinal);

        var ready = new List<string>();
        foreach (var (handler, deps) in HandlerDependencies)
        {
            if (EntryPointHandlers.Contains(handler))
            {
                continue; // Never dispatched by the reconciler.
            }
            if (completed.Contains(handler))
            {
                continue; // Already done.
            }
            // Handler is ready when every dep is in completedPhases.
            // Uses .All() rather than early-exit for readability at this
            // small (max ~6-per-handler) dep count.
            var allDepsSatisfied = true;
            foreach (var dep in deps)
            {
                if (!completed.Contains(dep))
                {
                    allDepsSatisfied = false;
                    break;
                }
            }
            if (allDepsSatisfied)
            {
                ready.Add(handler);
            }
        }

        // Deterministic order — helps log inspection + test assertion.
        // Semantically all returned handlers are parallel-safe.
        ready.Sort(StringComparer.Ordinal);
        return ready;
    }
}
