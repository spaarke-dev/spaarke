# Test diet report — spaarke-ai-architecture-redesign-r2

**Run date**: 2026-07-10
**Branch**: work/spaarke-ai-architecture-redesign-r2
**Scope**: tests added/modified by this project across its full span (2026-07-08..2026-07-10).
The project's work is almost entirely merged to master already (only 1 commit ahead of `origin/master`),
so scope was derived from the project's commits in master history — the `feat(ai-architecture-redesign-r2)`,
`feat(hardening) tasks 070-076`, memory-wave (`tasks 050-064`), Phase E (`E-10..E-42`), Wave J/K gate-engine,
and A0 walking-skeleton commits — **excluding** the interleaved sibling projects on the same master window
(`compose-r2`, `daily-briefing-r5` / `daily-update-r5`, `field-mapping`, `ai-redesign-r1`), which own their own diets.

**Classifier**: ADR-038 §7 (17-ban build-vs-maintain, B1-B17); 7 KEEP paths per `tests/CLAUDE.md`.
Note: this project **created** the `tests/integration/seam/**` KEEP category (E-40, ADR-038 §2 row added 2026-07-09).

---

## Summary

| Class | Count | Action |
|---|---|---|
| MAINTAIN (KEEP at canonical path) | 45 files / 369 test methods | confirmed — no action |
| SCAFFOLDING (DELETE candidate) | 0 | none |
| AMBIGUOUS (reviewer judgment) | 1 method (file stays MAINTAIN) | listed below |
| PATH-VIOLATION (wrong KEEP path) | 0 (see note) | none |
| **Already-reconciled deletions (deleted in-project)** | 8 files | recorded below, no action |
| **Total added test files touched** | **45** | — |

**Headline**: this project authored its test suite to the ADR-038 §7 standard from the start and
**deleted its own scaffolding as it went** (8 files across tasks 050/053/075). Zero net delete-candidates remain.
Ban-scan across all 45 added files: no `Mock<HttpMessageHandler>`/`Mock<IServiceClient>` (B1/B2), no
`GetRequiredService` wiring assertions (B3), no ctor `ArgumentNullException` tests (B4), no `Test1`/`_Works`/`_BugN`
names (B13). Every method carries a `{Method}_{Scenario}_{ExpectedResult}` name and a behavioral assertion.

---

## Delete commands (DO NOT auto-execute)

None. No file or method in this project's delta classifies as SCAFFOLDING.

---

## Path-move commands

None required.

**Path note (no violation):** the 24 new BFF unit files live under `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/**`,
not literally under `tests/unit/domain/**`. This is the established BFF unit-suite location; the ADR-038
`domain-logic` KEEP category (line 62) covers "handler-internal orchestration" and these test exactly that
substance (gate resolution, context binding, memory envelope migration-safety, outcome projection). The
`tests/CLAUDE.md` running-tests section itself notes the KEEP-category path reorg is future ("after task 050
path reorganization completes"). Classified MAINTAIN by substance; not flagged as PATH-VIOLATION.

---

## Ambiguous — reviewer judgment

| File:Method | Ambiguity reason | Suggestion |
|---|---|---|
| `NullOrganizationalContextProviderTests.cs`:`IOrganizationalContextProvider_ExposesOnlyTheInboundReadNoOutboundPushMethod` | Asserts interface *shape* (inbound-read-only, no outbound-push) rather than runtime behavior — brushes B8/B16 territory. But it protects a deliberate architectural boundary (org-context is read-in-only), so it reads as a contract-anchor, not a mirror. | KEEP. Contract-anchor for a stated invariant. The other 2 methods in this file are plainly behavioral (Null-Object returns empty-not-failure). |

---

## Maintain — confirmed (no action)

All 45 files below are maintain-class. Grouped by KEEP category.

### `tests/integration/seam/**` — vertical-slice-seam (the E-40 category, DoD for dispatch-spine changes)
| File | Why maintain |
|---|---|
| `Ai/ContextBinderActionRunnerSeamTests.cs` | E-10 dispatch→binder→runner slice with production types |
| `Ai/ContextBinderResolutionTests.cs` | ContextBinder resolution across the seam |
| `Ai/CompletionEngineSharedInputSeamTests.cs` | E-12 shared-input convergence end-to-end |
| `Ai/DispositionRoutabilitySeamTests.cs` | E-42 admission==routability invariant (the "green-contract ≠ working-slice" guard) |
| `Ai/CodedWorkflowDispatchSeamTests.cs` | E-30 chat-loop launches a Coded Action end-to-end |
| `Ai/CallerContactResolverSeamTests.cs` | org-context caller resolution slice (memory wave 055/060/061/063) |
| `Ai/SemanticScopeProviderSeamTests.cs` | semantic-scope provider slice |
| `Ai/Memory/MemoryWriteHandlerSeamTests.cs` | memory-write handler dispatch→store→recall slice (M2) |

### `tests/integration/contract/**` — endpoint-contract + eval
| File | Why maintain |
|---|---|
| `Api/Ai/ComposeDispositionContractTests.cs` | A0 walking-skeleton contract (route+status+shape) |
| `Api/Ai/ContextEnvelopeContractTests.cs` | A0 contract anchor |
| `Api/Ai/GateDecisionV2ContractTests.cs` | A0 gate-v2 contract anchor |
| `Api/Ai/JobAwareCompletionStateContractTests.cs` | A0 completion-state contract anchor |
| `Api/Ai/OutcomeCardContractTests.cs` | A0 OutcomeCard contract anchor (extended in 062) |
| `Api/Ai/TraceEventContractTests.cs` | A0 trace-event contract anchor |
| `Api/Ai/MemoryItemContractTests.cs` | A0 MemoryItem contract (task 016) |
| `Api/Ai/CapabilityDiscoveryEndpointContractTests.cs` | Wave J new endpoint contract |
| `Api/Ai/ChatAckEndpointsContractTests.cs` | Wave J ack endpoints contract |
| `Catalog/CatalogToolDescriptionParityContractTests.cs` | triple-twin parity contract (task 020) |
| `Catalog/CreateMatterCapabilityContractTests.cs` | Wave J capability contract |
| `Eval/ResourcefulnessEvalSuiteTests.cs` | resourcefulness eval family (task 031) |
| `Eval/OriginClassificationEvalSuiteTests.cs` | origin-classification eval family (task 033); asserts net-new-coverage |
| `Eval/MemoryWriteCaptureRecallEvalTests.cs` | memory capture/recall eval (M2) |
| `Eval/ContextBudgetBreachEvalTests.cs` | budget-breach-fails-eval (tasks 054/056) |

### `tests/unit/Sprk.Bff.Api.Tests/**` — domain-logic / handler-internal orchestration
| File | Why maintain |
|---|---|
| `Services/Ai/Chat/Gate/ConfirmationPolicyEngineTests.cs` | Policy-v2 gate decision branches (task 032) |
| `Services/Ai/Chat/Gate/RequestOriginClassifierTests.cs` | origin classification branches |
| `Services/Ai/Chat/Gate/RiskTierResolverTests.cs` | risk-tier resolution + never-down-rank invariant |
| `Services/Ai/Chat/ProgressiveRenderGuardTests.cs` | write-timestamp invariant (throws when absent) |
| `Services/Ai/Chat/UiActionAckCoordinatorTests.cs` | async ack/timeout keyed correctness, duplicate handling |
| `Services/Ai/Chat/SideEffectGatePreSuspendValidationTests.cs` | pre-suspend gate validation (task 034) |
| `Services/Ai/Chat/ConfirmationPolicyGateLiveDecisionTests.cs` | live gate decision on core gate (task 044) |
| `Services/Ai/CompletionEngineTests.cs` | next-step chip derivation, no-invention invariants (task 035/062) |
| `Services/Ai/PublicContracts/JobAwareOutcomeProjectionTests.cs` | partial-never-succeeded projection invariants (17 tests) |
| `Services/Ai/PublicContracts/SessionTraceReaderTests.cs` | trace round-trip + field-redaction behavior (task 038) |
| `Services/Ai/PublicContracts/NullOrganizationalContextProviderTests.cs` | Null-Object empty-not-failure contract (+1 ambiguous method above) |
| `Services/Ai/Context/ContextBinderOrganizationalSliceTests.cs` | org-slice binding behavior |
| `Services/Ai/Context/ContextBinderSliceProductionTests.cs` | context-binder production slice folding six R1 primitives (task 053) |
| `Services/Ai/Context/ContextEnvelopeRendererTests.cs` | `## Input` renderer output behavior (task 053) |
| `Services/Ai/Context/ContextSliceProducersTests.cs` | per-slice producer behavior (task 053) |
| `Services/Ai/Context/AggregateFreshnessPolicyTests.cs` | aggregate-vs-point-lookup detection + byte-identical pre-056 shape (task 056) |
| `Services/Ai/Memory/MemoryItemStoreTests.cs` | Record+User-scope memory store behavior (task 050) |
| `Services/Ai/Memory/MemoryItemEnvelopeTests.cs` | envelope migration-safety + additive-field tolerance (task 051) |
| `Services/Ai/Memory/MemoryItemStoreGovernanceTests.cs` | governance envelope enforcement (M2) |
| `Services/Ai/Memory/MemoryRetentionPolicyTests.cs` | retention-policy branches (M2) |
| `Api/Memory/MemoryGovernanceEndpointsTests.cs` | governance endpoint behavior (M2) |
| `Services/Ai/Audit/AuditPartitionKeyTests.cs` | tenant+monthly-bucket partition-key composition (task 074) |

**Support artifacts (not classified tests — helpers/data, retained with their suites):**
`tests/integration/seam/Ai/Memory/FakeMemoryItemStore.cs`,
`tests/integration/contract/Eval/ResourcefulnessFabricationOracle.cs`,
`*.json` eval corpora (`resourcefulness-eval-family.json`, `origin-classification-eval-family.json`),
`tests/integration/seam/README.md`.

**Modified existing maintain-class files (out of delete-scope — additions/regressions to established suites):**
`OutputRouterTests.cs`, `BusinessSliceDeterminismContractTests.cs`, `RoutingConsumerTypeHealthCheckTests.cs`,
`SessionDispatchManifestProbeTests.cs`, `MembershipResolverServiceTests.cs`,
`IdentityNormalizationServiceTests.cs` (membership bugfix regressions), `ContextEnvelopeContractTests.cs`,
`OutcomeCardContractTests.cs`, and the Chat playbook-context provider suite. No new scaffolding introduced.

---

## Already-reconciled deletions (this project deleted its own scaffolding in-flight)

| File(s) deleted | Commit / task | Reason (reconciled) |
|---|---|---|
| `Services/Ai/Chat/OrchestratorPromptBuilderTests.cs`, `OrchestratorPromptBuilderBudgetTests.cs` | `e4889189f` / task 053 | Dead code — `OrchestratorPromptBuilder` (+interface, DI) removed by ContextBinder convergence; tests deleted with it |
| `Services/Ai/Handlers/CloseWorkspaceTabHandlerTests.cs`, `GetWorkspaceTabContentHandlerTests.cs`, `UpdateWorkspaceTabHandlerTests.cs`, `Spe.Integration.Tests/PhaseC/CrossPillarIntegrationTests.cs`, `Spe.Integration.Tests/Workspace/ConflictResolutionTests.cs` | `b8f53df12` / task 075 | 5 files — legacy workspace-tab tools retired; tests deleted with the feature |
| `Services/Ai/Memory/MatterMemoryServiceTests.cs` | `3cd5cc6a4` / task 050 | Superseded — `MatterMemoryService` generalized to `MemoryItemStore` (Record+User scopes); replacement is `MemoryItemStoreTests.cs` (same PR) |

All three deletions satisfy the KEEP-path deletion-safety rule (same-PR replacement where the scenario survives;
dead-code/feature-retirement where the scenario no longer exists).

---

## Count delta

- Test files added during project: **45** (369 `[Fact]`/`[Theory]` methods)
- Classified MAINTAIN: **45 files** (369 methods)
- Classified SCAFFOLDING (delete): **0**
- Classified AMBIGUOUS: **1 method** (reviewer judgment; recommend KEEP)
- Scaffolding already deleted in-project: **8 files** (across tasks 050/053/075)
- **Net post-diet expected count: unchanged (45 files kept, 0 further deletes)**

---

## Industry citation

Build-vs-maintain criteria per ADR-038 §7 (Beck "delete the scaffolding"; Feathers characterization-vs-behavior;
Google test-sizes; DHH less-tests). 17-ban classifier B1-B17. This project is a positive exemplar: scaffolding
was deleted at the point the code it scaffolded was removed, and the surviving suite is regression-/contract-/
seam-anchored.
