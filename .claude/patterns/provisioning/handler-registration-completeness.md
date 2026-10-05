# Handler Registration Completeness Pattern

> **Last Reviewed**: 2026-09-30
> **Reviewed By**: customer-provisioning-orchestration-r1 T226 — corrected against the code (registration call, handler contract, result shapes, the `Dispatchable` + DAG steps, failure behaviour, file paths). Content from task 203a (punch list row A07).
> **Status**: Current.

## When

Load this pattern when:
- Adding a new `IProvisioningHandler` implementation.
- Debugging a run message dead-lettered as `NoHandler` or `HandlerResolutionFailed`, or a handler the reconciler never dispatches.
- Reviewing a PR that adds/removes/renames a handler.
- Adding a handler that is feature-gated (may not always be registered).

## Read These Files (canonical source)

1. `src/server/services/Sprk.Provisioning.ControlPlane.Core/Handlers/HandlerIds.cs` — id constants (`public const string H4 = "H4";`) and `Dispatchable`, the ids the dispatcher may receive (20 since T226 retired H4-shared, 2026-09-30). H14a/H14b/H14c are deliberately NOT in it — H14 runs them in-process.
2. `src/server/services/Sprk.Provisioning.ControlPlane.Core/Handlers/HandlerDispatchRegistrationModule.cs` — one keyed factory forwarder per dispatchable id: `services.AddKeyedScoped<IProvisioningHandler>(HandlerIds.H4, (sp, _) => sp.GetRequiredService<H4KvSecretsPopulationHandler>());`
3. Concrete registrations — `AddScoped<THandler>()` in `Core/Modules/HandlersModule.cs` or `Worker/Program.cs`, plus every constructor dependency (`IKvSecretsWriter`, `IOperatorKvRbacBootstrapper`, …).
4. `src/server/services/Sprk.Provisioning.ControlPlane.Core/Reconciler/DagAdvancer.cs` — `HandlerDependencies`: each handler's upstream handlers. A dispatchable id with no entry is never dispatched by the reconciler.
5. `src/server/services/Sprk.Provisioning.ControlPlane.Tests/Dispatch/HandlerRegistrationCompletenessTests.cs` — the forcing function: the `Dispatchable` count, every dispatchable id resolves to a keyed handler whose `HandlerId` matches the key, H14 sub-steps are NOT keyed-registered. Its partner: the HANDLER-12 parity test in `Tests/Reconciler/DagAdvancerTests.cs` (every dispatchable id except the entry points has a `HandlerDependencies` entry).
6. `.claude/adr/ADR-032-bff-nullobject-kill-switch.md` — the P1/P2/P3 null-object kill-switch pattern for feature-gated handlers.
7. `.claude/constraints/bff-extensions.md` § F.1 — asymmetric-registration Tier 1.5 anti-pattern (BINDING). Applies analogously here: an unconditional consumer of a handler must not depend on a conditionally-registered handler.

## Constraints

- A new handler touches **five places**: `HandlerIds` (const + `Dispatchable`), the handler class, its concrete + dependency registrations, the keyed forwarder, and `DagAdvancer.HandlerDependencies`. What a gap does at runtime: no keyed forwarder → the Worker dead-letters the message as `NoHandler`; a constructor dependency missing → `HandlerResolutionFailed`; no DAG entry → the reconciler never dispatches it (the HANDLER-01 incident: H4-shared + H4b, tasks 200/201).
- `HandlerRegistrationCompletenessTests` and the DAG parity test MUST pass on every PR. Update the count to the real number; never loosen a test to make it pass.
- Handler contract: `IProvisioningHandler` exposes `string HandlerId` and `Task<HandlerResult> HandleAsync(HandlerEnvelope envelope, CancellationToken cancellationToken)`. `HandlerResult` is closed: `Success(IdempotencyKey)` or `Failure(FailureClass, RejectionCode, Diagnostic)`, with `FailureClass` = `Resumable` | `RetryableWithCleanup` | `QuarantineRequired` | `SuccessfulButDrifted` (design.md §4C).
- Feature-gated handlers (per ADR-032): register a null-object impl UNCONDITIONALLY (outside the gate); register the real impl CONDITIONALLY (inside the gate). The dispatcher resolves the interface either way.

## Key Rules (walk this for every new handler)

1. **Id first.** In `HandlerIds.cs` add `public const string H15 = "H15";` and add `H15` to `Dispatchable`.
2. **Author the handler.** `Handlers/WelcomeEmail/H15WelcomeEmailHandler.cs` implements `IProvisioningHandler` (`HandlerId => HandlerIds.H15`; returns `HandlerResult.Success` / `HandlerResult.Failure`).
3. **Register the concrete type and its dependencies** (`AddScoped<H15WelcomeEmailHandler>()` + any new dependency).
4. **Add the keyed forwarder** in `HandlerDispatchRegistrationModule.cs`.
5. **Add the DAG entry** in `DagAdvancer.HandlerDependencies` (upstream ids), plus the matching shape test in `DagAdvancerTests`.
5b. **Declare its inputs** in `Reconciler/HandlerRunInputs.cs` and map its folder in `RunContextContractTests` — every value it reads needs a producer that runs first ([run-context-contract.md](run-context-contract.md), T245a).
6. **Run the forcing functions**: `dotnet test src/server/services/Sprk.Provisioning.ControlPlane.Tests --filter "FullyQualifiedName~HandlerRegistrationCompletenessTests|FullyQualifiedName~DagAdvancerTests"` — update the dispatchable count (20 → 21). A failure names the id; fix the gap.
7. **Feature-gated handler** (ADR-032 P1/P2/P3):
   - **P1** — register the null-object handler under the key UNCONDITIONALLY (outside the feature gate).
   - **P2** — register the REAL handler's forwarder inside the gate (`if (options.EnableH15) { services.AddKeyedScoped<IProvisioningHandler>(HandlerIds.H15, (sp, _) => sp.GetRequiredService<H15WelcomeEmailHandler>()); }`).
   - **P3** — the kill-switch lives on the handler's options; DI resolves whichever forwarder was registered LAST for the key.
8. **Test update obligation** (bff-extensions.md § F test-update-obligation): PRs touching handler DI update tests in `src/server/services/Sprk.Provisioning.ControlPlane.Tests/Handlers/**` — happy path + rejection paths + feature-gate-off path.

## Anti-patterns this catches

- ❌ Registering the concrete handler but no `HandlerIds` const / `Dispatchable` entry → nothing can enqueue it.
- ❌ Const + `Dispatchable` + keyed forwarder but no `HandlerDependencies` entry → never dispatched (HANDLER-01). The DAG parity test catches it.
- ❌ A constructor dependency left unregistered → `HandlerResolutionFailed` at first dispatch. The completeness test resolves every handler, so it catches this.
- ❌ A forwarder pointing at the wrong concrete type → the completeness test's `HandlerId` check catches it.
- ❌ Feature-gating a handler by omitting registration entirely (no null impl) → asymmetric-registration Tier 1.5 anti-pattern. See [null-object-kill-switch-anti-pattern.md](null-object-kill-switch-anti-pattern.md).

## Recovery recipes

- **Message dead-lettered `NoHandler`**: the id has no keyed forwarder — or the message carries a retired id (e.g. `H4-shared`, retired by T226), which is harmless.
- **`HandlerResolutionFailed`**: a constructor dependency is not registered in the composition the Worker uses.
- **A handler never runs**: check its `HandlerDependencies` entry and whether its upstream handlers completed.
- **Test fails**: the message names the id or the missing piece. Fix the gap; do NOT weaken the test.
- **CI green but production dispatch fails**: confirm CI ran both tests (not skipped) and that the Worker's composition matches the one the test builds.

## Worked example — H15 (post-provision welcome email)

1. **`HandlerIds.cs`**:
   ```csharp
   public const string H15 = "H15";
   // …and append H15 to Dispatchable
   ```
2. **`Handlers/WelcomeEmail/H15WelcomeEmailHandler.cs`**:
   ```csharp
   public sealed class H15WelcomeEmailHandler : IProvisioningHandler
   {
       private readonly IGraphMailClient _mailClient;

       public H15WelcomeEmailHandler(IGraphMailClient mailClient) => _mailClient = mailClient;

       public string HandlerId => HandlerIds.H15;

       public async Task<HandlerResult> HandleAsync(HandlerEnvelope envelope, CancellationToken cancellationToken)
       {
           // read the run (IProvisioningRunRepository) for parameters, send, then:
           return new HandlerResult.Success($"welcome-email-{envelope.CustomerId}");
       }
   }
   ```
3. **Registrations** (module or `Worker/Program.cs`):
   ```csharp
   services.AddScoped<H15WelcomeEmailHandler>();
   services.AddSingleton<IGraphMailClient, GraphMailClient>();   // new dependency
   ```
4. **`HandlerDispatchRegistrationModule.cs`**:
   ```csharp
   services.AddKeyedScoped<IProvisioningHandler>(
       HandlerIds.H15, (sp, _) => sp.GetRequiredService<H15WelcomeEmailHandler>());
   ```
5. **`DagAdvancer.HandlerDependencies`**: add `H15` with its upstream ids (e.g. after H13).
6. **Test**: run the step-6 filter — expect a dispatchable count of 21 (was 20).

## Cross-refs

- Related pattern: [null-object-kill-switch-anti-pattern.md](null-object-kill-switch-anti-pattern.md) (feature-gating via ADR-032)
- Related pattern: [manifest-driven-secret-catalog.md](manifest-driven-secret-catalog.md) (handlers that consume the secret manifest)
- Related ADR: ADR-032 (Null-Object Kill-Switch Pattern) — canonical P1/P2/P3
- Related ADR: ADR-036 (Background Job Infrastructure) — the L2 `ProvisioningHandlerDispatcher` follows the same shape as BFF `IJobHandler` dispatch
