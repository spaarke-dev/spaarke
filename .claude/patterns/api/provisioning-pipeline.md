# Pattern: Multi-Step Provisioning Pipeline

> **Last Reviewed**: 2026-04-05
> **Reviewed By**: ai-procedure-refactoring-r2
> **Status**: Current (added full path for RegistrationEndpoints.cs)

**Canonical implementation**: `src/server/api/Sprk.Bff.Api/Services/Registration/DemoProvisioningService.cs`

## What to look for

- **Idempotency check** (ADR-004): Before executing steps, checks if the work was already done
  (e.g., `sprk_demousername` already set). Returns cached result on retry.
- **completedSteps list**: Tracks each successful step by name. On failure, the list is
  included in `DemoProvisioningException` for diagnostics and partial-failure recovery.
- **DemoProvisioningException**: Custom exception carrying `CompletedSteps`, `EntraUserId`,
  `Upn`, `DataverseSystemUserId` — tells the caller exactly what was created before failure.
- **Non-fatal optional steps**: SPE container access (Step 8) is wrapped in its own try/catch.
  Failure logs a warning and appends `:SKIPPED` to completedSteps, but does not abort the pipeline.
- **Sequential orchestration**: 9 steps run in order. Each step depends on output from prior
  steps (e.g., Step 3 returns `entraUserId` used by Steps 4-8).

## When to use this pattern

Any multi-step provisioning or setup workflow where:
- Steps must run in a specific order with data flowing between them
- Partial failure needs to be diagnosable (which steps succeeded?)
- Some steps are optional / non-fatal
- The operation should be idempotent (safe to retry)

## Related

- `DemoExpirationService.cs` — Same per-step try/catch pattern for cleanup operations
- `src/server/api/Sprk.Bff.Api/Endpoints/RegistrationEndpoints.cs` — Catches `DemoProvisioningException` and returns step details in ProblemDetails
