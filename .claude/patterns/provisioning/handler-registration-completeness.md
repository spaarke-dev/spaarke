# Handler Registration Completeness Pattern

> **Last Reviewed**: 2026-08-25
> **Reviewed By**: customer-provisioning-orchestration-r1 task 203a per punch list row A07
> **Status**: Content filled (task 203a). Was skeleton from task 202.

## When

Load this pattern when:
- Adding a new `IProvisioningHandler` implementation.
- Debugging `Unable to resolve handler ID X` at dispatch.
- Reviewing a PR that adds/removes/renames a handler.
- Adding a handler that is feature-gated (may not always be registered).

## Read These Files (canonical source)

1. `src/server/services/Sprk.Provisioning.ControlPlane.Core/Handlers/HandlerIds.cs` — const list of dispatchable IDs. Every ID here MUST resolve to a concrete `IProvisioningHandler` at DI resolution time.
2. `src/server/services/Sprk.Provisioning.ControlPlane.Core/DI/HandlerDispatchRegistrationModule.cs` — keyed forwarders. Every `HandlerIds.*` const gets a matching `AddKeyedTransient<IProvisioningHandler, TConcrete>(handlerId)` entry.
3. `src/server/services/Sprk.Provisioning.ControlPlane.Worker/Program.cs` (or the equivalent module DI file) — concrete DI registrations that provide the underlying dependencies each handler needs (`IDataverseClient`, `IServiceBusClient`, `ISharedKvSecretAccessor`, etc.).
4. `tests/unit/Sprk.Provisioning.ControlPlane.Core.Tests/DI/HandlerRegistrationCompletenessTests.cs` — the ArchTest that asserts every `HandlerIds.*` const resolves to a keyed `IProvisioningHandler`. This is the forcing function — it fires on `dotnet test` locally + in CI.
5. `.claude/adr/ADR-032-bff-nullobject-kill-switch.md` — the P1/P2/P3 null-object kill-switch pattern for feature-gated handlers.
6. `.claude/constraints/bff-extensions.md` § F.1 — asymmetric-registration Tier 1.5 anti-pattern (BINDING). Applies analogously here: an unconditional consumer of a handler must not depend on a conditionally-registered handler.

## Constraints

- Every new handler is a **3-file dance**: `HandlerIds.cs` + `HandlerDispatchRegistrationModule.cs` + `Worker/Program.cs`. Missing any of the three → dispatch throws at runtime with `Unable to resolve handler for ID: {handlerId}`.
- `HandlerRegistrationCompletenessTests` MUST pass on every PR. The ArchTest scans `HandlerIds.*` via reflection + attempts keyed resolution via a test ServiceProvider built from the production `HandlerDispatchRegistrationModule` + `Worker/Program.cs` composition.
- Handler contract: `IProvisioningHandler.ExecuteAsync(HandlerEnvelope envelope, CancellationToken ct)` returns `HandlerResult` (Success | Failure | Deferred | Rollback). Any handler that returns another shape breaks the L2 dispatcher.
- Feature-gated handlers (per ADR-032): register a null-object impl UNCONDITIONALLY (outside the gate); register the real impl CONDITIONALLY (inside the gate). Dispatcher receives the interface either way.

## Key Rules (walk this for every new handler)

1. **Add the const first**. Open `HandlerIds.cs`; add `public const string H15_MyNewHandler = "h15";`. Save + build → compile succeeds (no consumer of the new ID yet).
2. **Add the keyed forwarder**. Open `HandlerDispatchRegistrationModule.cs`; add `services.AddKeyedTransient<IProvisioningHandler, H15MyNewHandler>(HandlerIds.H15_MyNewHandler);`. Save + build → compiler surfaces missing `H15MyNewHandler` type.
3. **Author the concrete handler**. Create `Handlers/MyNewHandler/H15MyNewHandler.cs` implementing `IProvisioningHandler`. Wire its own dependencies (via ctor injection). Return `HandlerResult.Success(...)` or `HandlerResult.Failed(rejectionCode, message)`.
4. **Register concrete dependencies**. Open `Worker/Program.cs` (or the appropriate module). Add `AddTransient<IMyNewHandlerDep, MyNewHandlerDepImpl>()` for every ctor dep that isn't already in DI. Save + build.
5. **Run the forcing function**: `dotnet test tests/unit/Sprk.Provisioning.ControlPlane.Core.Tests --filter "HandlerRegistrationCompletenessTests"` → 21/21 → 22/22. If it fails, a keyed forwarder is missing or a ctor dep is unregistered; the failure message names the ID.
6. **Feature-gated handler** (ADR-032 P1/P2/P3):
   - **P1** — declare `INullMyNewHandler` interface + register `NullMyNewHandler` UNCONDITIONALLY inside `HandlerDispatchRegistrationModule` (outside the feature gate).
   - **P2** — register the REAL `H15MyNewHandler` inside the feature gate (`if (options.EnableH15) { services.AddKeyedTransient<IProvisioningHandler, H15MyNewHandler>(HandlerIds.H15_MyNewHandler); }`).
   - **P3** — the kill-switch is on the `HandlerOptions` (or equivalent); on runtime decision, DI resolves whichever was registered LAST for the same key.
   - Note: for provisioning handlers, the simpler pattern is: register the NULL impl outside the gate under the handler's key; register the REAL impl inside the gate under the same key. Last-write-wins.
7. **Test update obligation** (bff-extensions.md § F test-update-obligation): PR touching handler DI MUST update tests in `tests/unit/Sprk.Provisioning.ControlPlane.Core.Tests/Handlers/**` — happy path + rejection paths + feature-gate off path.

## Anti-patterns this catches

- ❌ Adding a handler in `Worker/Program.cs` DI but forgetting the `HandlerIds` const → dispatcher can't find it because the const doesn't exist to key on. `HandlerRegistrationCompletenessTests` catches this at build time.
- ❌ Adding the const + keyed forwarder but forgetting to register concrete dependencies → `Unable to resolve service for type 'IMyNewHandlerDep' while attempting to activate 'H15MyNewHandler'` at first dispatch. `HandlerRegistrationCompletenessTests` catches this too (it builds the full ServiceProvider).
- ❌ Feature-gating a handler by omitting registration entirely (no null impl) → asymmetric-registration Tier 1.5 anti-pattern. Any unconditional consumer of the handler ID (like the dispatcher itself, if it enumerates all IDs) fails at boot. See [null-object-kill-switch-anti-pattern.md](null-object-kill-switch-anti-pattern.md).
- ❌ Handler that returns a fabricated result shape (e.g., `Task<bool>`) instead of `Task<HandlerResult>` → dispatcher can't interpret. Contract is binding.

## Recovery recipes

- **Runtime dispatch error `Unable to resolve handler ID X`**: check the 3-file dance — `HandlerIds` has `X`? `HandlerDispatchRegistrationModule` has keyed forwarder for `X`? Concrete class + deps registered in `Worker/Program.cs`? If all three present, run `HandlerRegistrationCompletenessTests` to reproduce.
- **`HandlerRegistrationCompletenessTests` fails**: the test message names the missing key or missing ctor dep. Fix the specific gap; do NOT weaken the test.
- **CI green but production dispatch fails**: check that CI actually runs `HandlerRegistrationCompletenessTests` (not skipped/excluded). Also verify prod's `Worker/Program.cs` composition matches what the test composes.

## Worked example — the 3-file dance for a new handler

Suppose we're adding H15 (Post-provision customer welcome email). Steps end-to-end:

1. **`HandlerIds.cs`** — add the const:
   ```csharp
   public static class HandlerIds
   {
       public const string H0_Preflight = "h0";
       // ... existing IDs ...
       public const string H15_WelcomeEmail = "h15";  // NEW
   }
   ```

2. **`HandlerDispatchRegistrationModule.cs`** — add the keyed forwarder:
   ```csharp
   public static IServiceCollection AddHandlerDispatch(this IServiceCollection services, ...)
   {
       // ... existing handlers ...
       services.AddKeyedTransient<IProvisioningHandler, H15WelcomeEmailHandler>(HandlerIds.H15_WelcomeEmail);  // NEW
       return services;
   }
   ```

3. **`Handlers/WelcomeEmail/H15WelcomeEmailHandler.cs`** — the concrete impl:
   ```csharp
   public sealed class H15WelcomeEmailHandler : IProvisioningHandler
   {
       private readonly IGraphMailClient _mailClient;
       private readonly ILogger<H15WelcomeEmailHandler> _logger;
       public H15WelcomeEmailHandler(IGraphMailClient mailClient, ILogger<H15WelcomeEmailHandler> logger)
       { _mailClient = mailClient; _logger = logger; }

       public async Task<HandlerResult> ExecuteAsync(HandlerEnvelope envelope, CancellationToken ct)
       {
           var to = envelope.Parameters.NonSecret["operator_upn"].ToString();
           await _mailClient.SendAsync(to, "Welcome to Spaarke", "...", ct);
           return HandlerResult.Success();
       }
   }
   ```

4. **`Worker/Program.cs`** — register concrete deps (if new):
   ```csharp
   builder.Services.AddSingleton<IGraphMailClient, GraphMailClient>();  // NEW dep
   ```

5. **Test**: run `dotnet test tests/unit/Sprk.Provisioning.ControlPlane.Core.Tests --filter "HandlerRegistrationCompletenessTests"` — expect 22/22 (was 21/21). If failure, the message names the missing key or ctor dep.

6. **Feature-gate variant** (H15 optional per profile): register `NullWelcomeEmailHandler` UNCONDITIONALLY + real `H15WelcomeEmailHandler` inside `if (options.EnableWelcomeEmail)` gate. Last-write-wins for the same key resolves correctly at runtime.

## Cross-refs

- Related pattern: [null-object-kill-switch-anti-pattern.md](null-object-kill-switch-anti-pattern.md) (feature-gating via ADR-032)
- Related pattern: [manifest-driven-secret-catalog.md](manifest-driven-secret-catalog.md) (handlers that consume the secret manifest)
- Related ADR: ADR-032 (Null-Object Kill-Switch Pattern) — canonical P1/P2/P3
- Related ADR: ADR-036 (Background Job Infrastructure) — the L2 `ProvisioningHandlerDispatcher` follows the same shape as BFF `IJobHandler` dispatch
