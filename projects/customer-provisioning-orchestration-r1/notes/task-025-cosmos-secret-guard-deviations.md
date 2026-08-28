# Task 025 — CosmosProvisioningSecretGuardTests Deviations

**Date**: 2026-08-17
**Task**: `025-archtest-cleartext-secret-scan.poml`
**File under review**: `tests/Spaarke.ArchTests/CosmosProvisioningSecretGuardTests.cs`

## Deviations from POML

### 1. Runtime `Assembly.LoadFrom` instead of `ProjectReference`

**POML expectation** (implicit — canonical exemplar `GodClassGuardTests` uses static file IO, `ADR013_ComposeFacadeTests` uses NetArchTest with a compile-time ProjectReference).

**Actual choice**: `CosmosProvisioningSecretGuardTests` loads the L2 assembly at test runtime via `Assembly.LoadFrom`, resolving the newest `Sprk.Provisioning.ControlPlane.dll` under `src/server/services/Sprk.Provisioning.ControlPlane/bin/**`. No `ProjectReference` in `Spaarke.ArchTests.csproj`.

**Reasons** (documented in the `.csproj` itself + the test class XML doc):

1. **`Program`-class collision.** `Sprk.Bff.Api` AND `Sprk.Provisioning.ControlPlane` are both Web SDK projects; each generates a top-level `public partial class Program` at compile time. When both are referenced, `typeof(Program)` in existing tests (`ADR001_MinimalApiTests`, `ADR007_GraphIsolationTests`, `ADR008_AuthorizationTests`, `ADR009_CachingTests`, `ADR010_DITests`, `ADR013_AiBoundaryTests`, `ADR013_ComposeFacadeTests`, `ADR013_LinearConsumerBoundaryTests`, `ADR002_PluginTests`, `DailyBriefingGroundednessGuardrailTests`) fires CS0433 "The type 'Program' exists in both …". An `extern alias` would fix the collision, but option 2 is a stronger reason for runtime loading regardless.

2. **Sibling-task NuGet churn** (the operative concern). L2 is a POCO surface in Wave 2 of this project. Its NuGet stack evolves through parallel Wave 2 sibling tasks (Cosmos SDK via task 037, Service Bus via 038, App Insights via 039, others). During the Wave 2 Batch 2A dispatch that owned task 025, sibling task 037 added `Microsoft.Azure.Cosmos 3.62.1` to the L2 `.csproj` in the shared working tree — which triggers a "Newtonsoft.Json must be explicitly referenced" transitive-check error unless the L2 csproj also adds either the `Newtonsoft.Json` reference or `<AzureCosmosDisableNewtonsoftJsonCheck>true</AzureCosmosDisableNewtonsoftJsonCheck>`. A compile-time `ProjectReference` from `Spaarke.ArchTests` would have made 025's test build fail whenever L2's package graph was mid-transition. Runtime reflection over the last-published DLL keeps this ArchTest robust across that churn — the invariant it enforces (POCO shape) is against the **published artifact**, so `Assembly.LoadFrom` is the honest inspection surface.

**Consequences**:

- L2 must have been built at least once before `dotnet test` on this suite. CI's full-sln `dotnet build` satisfies this automatically; local dev is guided by an explicit `FileNotFoundException` diagnostic in the test loader if the DLL is missing.
- No compile-time verification that L2 types exist by name — instead, `L2Assembly.Value.GetType(..., throwOnError: true)` in the positive control fails loudly if the L2 shape has moved.
- The test names L2 types by string full-name (`"Sprk.Provisioning.ControlPlane.Models.KeyVaultSecretRef"`, `"…RunParameters"`) — kept in named `const string` fields at the top of the file so a namespace rename is a one-line edit.

### 2. Regex catalog extended beyond the POML-listed prefixes

**POML** (line 42) named:
> `sk_`, `pat_`, `ghp_`, `xoxb-`, `xoxp-`, connection-string format (`AccountKey=…`, `SharedAccessKey=…`, `Server=… Password=…`), Azure primary/secondary key shape (44+ char base64), and any string property named `/^(Secret|Password|Key|ClientSecret|ApiKey|ConnectionString)/i` on a type in the `Sprk.Provisioning.ControlPlane` namespace.

**Additions** (all secret shapes equally CATASTROPHIC if persisted to Cosmos):

- Property-name regex extended with: `Token|PrivateKey|Certificate|Credential|Bearer|AccessKey|AuthToken`.
- Value-pattern list matches the POML verbatim (no additions to secret-value patterns).

**Rationale**: The POML explicitly frames the regex catalog as "a LIVING list — new secret prefixes discovered in the wild should be added here rather than in ad-hoc PR reviews" (task 025 `<notes>`). The property-name additions are the well-known peers (an `AccessToken`, `PrivateKey`, or `BearerToken` string on a Cosmos POCO is exactly as bad as a `ClientSecret` string). Keeping the additions in the catalog (with two `NegativeControl` Facts asserting the flag/no-flag boundary) means a future violation matching any of them is caught the same way.

### 3. Fixture-scan discriminators (Fact b)

The POML says the fixture-scan test should trigger on fixtures "targeting the runs container". To keep the guard narrow and false-positive-free (a Compose payload with a legitimate base64 body should NOT trip this test), Fact (b) requires the fixture file to contain at least one runs-container discriminator:

- `ProvisioningRun` (type name)
- `spaarke-provisioning` (Cosmos DB name per design.md §6.2)
- `"currentPhase"`, `"gateStates"`, `"interStepState"`, `"tenancyModel"` (canonical run-doc JSON fields)

Any fixture that references NONE of these is not in-scope for this guard. This is a common false-positive-reduction pattern in structural guards and is spelled out in the code comment above the needle array.

### 4. Negative-verification cycle exercised

Task 025 step 5 requires exercising the negative-verification pathway (proving the test FAILS when a violation is added, then reverting). Executed manually:

1. Added `src/server/services/Sprk.Provisioning.ControlPlane/Models/_BadShape_NegativeVerification.cs` with a synthetic `public sealed class BadShape_CleartextClientSecret_ShouldFailGuard { public string ClientSecret { get; set; } = string.Empty; }` inside `Sprk.Provisioning.ControlPlane.Models.NegativeVerification`.
2. Rebuilt L2 (`dotnet build src/server/services/Sprk.Provisioning.ControlPlane/`) — succeeded (0 warnings, 0 errors).
3. Re-ran the ArchTest filter — `L2Types_HaveNoStringTypedSecretShapedProperties` FAILED as designed with the diagnostic:

   > `Sprk.Provisioning.ControlPlane.Models.NegativeVerification.BadShape_CleartextClientSecret_ShouldFailGuard.ClientSecret : string — secret-shaped name on a Cosmos-persisted POCO. Use KeyVaultSecretRef (URI-only reference) instead. See RunParameters.Secrets for the compliant pattern.`

4. Deleted the synthetic file, rebuilt L2, re-ran the ArchTest filter — 5 of 5 tests PASSED.

The commented-out `#if false` block at the bottom of `CosmosProvisioningSecretGuardTests.cs` documents what a violating type would look like for future readers.

## No other deviations

- Test framework: xUnit + raw reflection (no NetArchTest for this test; NetArchTest is dependency-substring-oriented and less clean for property-name-scanning). Consistent with `GodClassGuardTests` pattern.
- Test location: `tests/Spaarke.ArchTests/` (correct ArchTest suite location per ADR-038 KEEP-path discipline).
- No touch to `.github/workflows/**` (Phase H coordinated PR owns that per root CLAUDE.md §10 ci-workflows=Y rule).
- No mock of HttpMessageHandler, no runtime-behavior test, no DI-registration test (ADR-038 §7 clean).

## Follow-on

The test is READY for Phase H CI-workflow gating as a candidate forcing-function alongside the existing `GodClassGuardTests` + ADR-013 guards. Coordination with `ci-cd-unit-test-remediation-r1` happens through Phase H PR per task `042-063-ci-gate-wiring-deferral.md`; nothing to do in this task.
