# Task 012 notes: FR-08 requestType routing key + intake harness contract and registry

> 2026-11-26. Rigor FULL, opus @ high, steps directional. Branch `work/spaarke-intake-pipeline-r1`. Rework line is at the end.

## What changed

- `IRoutingContext.RequestType`: a default interface member returning `null`; `RoutingContext.RequestType` (init). The only implementer in the repo is `RoutingContext`, and no production code constructs one today (every caller passes `context: null`), so no implementer or caller changes (FR-08b).
- `ConsumerRoutingService.ResolveContextValue`: `"requestType" => context.RequestType`.
- `ConsumerRoutingService.BuildCacheKey`: the request type is now part of the routing cache key. **This was a latent defect the new key would have triggered**: the key held only MIME type and document type, so with a `requestType` condition the first resolution (say `nda`) would have been served for a different request type (`invoice`) for the 5-minute TTL. A test pins it (fails with the old key; see Tests).
- `ConsumerTypes.IntakeHarness = "intake-harness"` and its `All` entry; one Binding row in `infra/dataverse/sprk_playbookconsumer-rows.json` (`consumerCode` `nda`, `matchConditions` `{"requestType":"nda"}`), and the GU-150 coverage entry in `tests/integration/contract/Eval/golden-utterances.json` (needed by `P2LoopInjectionEvalSuiteTests.FullCatalog_EveryClosedCatalogConsumerType_HasAnEvalFamily`, same shape as GU-142..149).
- New `src/server/api/Sprk.Bff.Api/Services/Intake/Harnesses/`:
  - `IIntakeHarness`: `Key`, `CanHandle(IntakeHarnessRoute)`, `RunAsync(request) → IntakeHarnessResult`, `PersistAsync(request, result) → IntakeHarnessPersistResult` (the persist hook).
  - `IntakeHarnessContracts.cs`: `IntakeHarnessRoute` (request type + document type), `IntakeContentReference` (document id / staged blob path / file name / MIME type, never bytes), `IntakeHarnessRequest` (request id, run id, route, content, correlation id), `IntakeHarnessDocument` (role, staged blob path, file name), `IntakeHarnessResultReference` (kind, reference), `IntakeHarnessReturnPayload` (document roles), `IntakeHarnessResult` (documents, results, `ApprovalRequired` default **true**, return payload), `IntakeHarnessPersistResult` (ids by role / kind), `IntakeHarnessResolutionOutcome`, `IntakeHarnessResolution`.
  - `IntakeHarnessRegistry` (concrete, scoped, registered in `JobProcessingModule`): built from `IEnumerable<IIntakeHarness>`, case-insensitive; a blank or duplicate key throws `InvalidOperationException` naming the key and both types. `ResolveAsync(requestType, documentType)` resolves the `intake-harness` Binding and returns the harness its consumer code names; `Resolve(key, route)` looks up by key (for a resumed run that stored its key, task 011). No harness → an `IntakeHarnessResolution` without one (`InvalidRequestType` / `NoBinding` / `UnregisteredKey` / `Declined`), never an exception (FR-08c, FR-10).
- Lockstep doc updates the `IRoutingContext` remark asks for: `projects/spaarke-ai-platform-chat-routing-redesign-r1/architecture/playbookconsumer-matchconditions.schema.json` (requestType examples + comment), `docs/architecture/ai-architecture-playbook-consumer-routing.md:181`.
- Comment fix (F2, found in passing in the files touched): the `IRoutingContext` remark, `TryMatchConditions` summary and the schema `$comment` said unknown match keys are "IGNORED"; the code fails the match for them (`TryMatchConditions_UnknownKey_TreatedAsNoMatch_False` pins it). All three now say so. Class grep (`unknown keys.{0,40}ignored` over `src/server`, `docs`): no other match.

## Binding-field choice: `ConsumerCode` (no escalation trigger fired)

The harness key rides `sprk_consumercode` (`Binding.ConsumerCode`). The registry calls `ResolveBindingAsync("intake-harness", consumerCode: requestType, context: { RequestType, DocumentType })`; the matched row's consumer code is the key.

Why not `WorkflowClass`, and why `ConsumerCode` is not a repurpose (every reader checked with Grep):
- `WorkflowClass` is Action-side (`sprk_analysisaction.sprk_workflowclass`) and means "the registered `ICodedWorkflow` class". Readers: `SessionDispatchOrchestrator.cs:396` (resolves it through the coded-workflow registry for any Coded Binding dispatched by session) and `DailyBriefingCompositeService.cs:250-308`. Using it would need a new Coded Action row whose workflow class is not an `ICodedWorkflow`: a repurpose, so the escalation trigger would fire. Rejected.
- `ConsumerCode` is documented as "sub-discriminator within a consumer type", and callers already pass a dynamic value and treat the row's code as the target's canonical name (`InsightEndpoints.cs:266-279`, `AssistantToolCallHandler.cs:621-629`, `InsightsOrchestrator.cs:640-646`, `TopicRegistryTtlLookup.cs:113-145`). A request type as the sub-discriminator of `intake-harness` is that same meaning.
- Other `ConsumerCode` readers never see an `intake-harness` row: `CapabilityDiscoveryEndpoints.cs:149` lists only text-projectable rows (this row has no tool description); `WorkProductRecordPersister.cs:268-276` names records of session-dispatched outputs (the row is not session-dispatchable: no tool description, chips or event bindings); `TopicRegistryTtlLookup` reverse-looks-up by **playbook** id and this row targets an Action, not a playbook.
- No new column (the second trigger did not fire either).

Consequence to know (not a defect): with `ConsumerCode` as the key, a row's key must equal the request type it serves (resolution only matches a row whose code equals the requested code or is `default`). So the request type selects the harness, and `requestType`/`documentType` match conditions **gate** it (for example `{"requestType":"nda","documentType":"nda"}` sends an nda request whose content classified as something else to triage). One request type mapping to different harnesses by document type would need a different design; nothing in r1 needs it. A `default`-coded `intake-harness` row would match every request type and resolve key `default`, which is unregistered → triage; do not seed one.

Target lookup: `ConsumerRoutingService` skips rows with neither `sprk_playbook` nor `sprk_action` (`QueryCandidatesAsync`), so the row must target something. It targets `agreement-classify` (catalogued, `infra/dataverse/actions/agreement-classify.action.json`), the Action whose result gates this harness (FR-10 0.85). Nothing dispatches the Binding's Action. Task 015 may repoint it to `agreement-edit-plan` once task 004 seeds that Action (the seed fails on an unknown action code, so it cannot point there yet).

## Deviations from the POML

- **Duplicate keys fail at registry construction, not host startup.** The registry is scoped (its dependencies `IConsumerRoutingService` and the harnesses are scoped), so the throw happens on the first construction in a scope (the first intake run), and `ValidateOnBuild` does not construct it. The acceptance criterion says "registry construction" and that is what is tested. A startup check would mean a hosted validator or a singleton key index; not built (over-engineering check: duplicates come only from a developer registering two harnesses with one key, and task 013/015 tests construct the real registry).
- **Contract shape vs tasks 013/015.** The POML goal fixes the contract as one `RunAsync` plus a persist hook. Task 013 delegates Analyse and Build to the harness, and task 015 says "Analyse = edit plan (persisted before Build)". With one `RunAsync` the orchestrator cannot persist between Analyse and Build; the harness would checkpoint the edit plan itself, or task 013 splits `RunAsync` into `AnalyseAsync`/`BuildAsync`. **Main session / task 013 to decide.** The records are plain, serializable and reference-only, so a split does not change them.
- **Two test files added beyond the POML list**, both at KEEP paths:
  - `tests/integration/contract/Catalog/IntakeHarnessBindingContractTests.cs` (4 tests) feeds the **committed** mirror row through the real routing service and registry, so a row edit that stops nda requests reaching the nda harness fails in CI. The domain tests use hand-built rows and cannot catch that.
  - The cache-key regression test went into `ConsumerRoutingServiceTests.cs` (grandfathered; it already has the Dataverse-boundary plumbing), not `ConsumerRoutingServiceContextFilterTests.cs`, which holds only pure `TryMatchConditions`/filter tests.
- `golden-utterances.json` GU-150: required by the full-catalog eval guard for every `All` member (same as task 005).
- `docs/data-model/sprk-playbookconsumer.md:50` lists the match keys (`mimeType`, `documentType`) and should gain `requestType`. **Not edited**: task 011's agent owns `docs/data-model/*` in this worktree right now. Main session: add `requestType` to that line.
- POML step 7 (TASK-INDEX) not done: the main session owns it.

## Tests

New behaviour tests (fail before, pass after; proven by copying the old file back):
- Old `ConsumerRoutingService.cs` (HEAD): 10 of the new tests fail (all requestType-match cases, registry resolve/unregistered/declined/case, the committed-row contract test, the cache test); the rest pass because they assert "no harness" outcomes that also hold without the key.
- New file with only the old cache key restored: `ResolveBindingAsync_OtherRequestTypeAfterACachedMatch_DoesNotServeTheCachedBinding` fails (1 of 40); with the fix, 40/40.

Suite results (after the change):

| Suite | Result |
|---|---|
| `IntakeHarnessRegistryTests` (tests/unit/domain/Intake) | 20 pass, 0 fail |
| `IntakeHarnessBindingContractTests` | 4 pass, 0 fail |
| `ConsumerRoutingServiceContextFilterTests` | 20 pass, 0 fail (12 existing + 8 new) |
| `ConsumerRoutingServiceTests` | 40 pass, 0 fail (39 existing + 1 new) |
| `ConsumerRoutingServiceBindingContractTests` | 25 pass, 0 fail (unchanged) |
| `ConsumerRoutingServiceEventBindingsTests` | 5 pass, 0 fail (unchanged) |
| `RoutingConsumerTypeHealthCheckTests` | 32 pass, 0 fail |
| `DiGraphValidationTests` | 1 pass |
| Wide filter (Eval, Catalog, ConsumerType, Routing, IntakeHarness, DiGraph, JobProcessing) | 2913 pass, 0 fail |
| `tests/Spaarke.ArchTests` | 2831 pass, 0 fail |

Not run: the full `Sprk.Bff.Api.Tests` suite (two other sessions were building in this worktree; the task-005 notes record that a full run under that load times out WebApplicationFactory tests at random). CI should confirm it.

Test scope beyond the stated contract (one line each): `ResolveAsync_RoutingReadFails_ReturnsNoBindingInsteadOfThrowing` proves "never throws" through a Dataverse failure; the non-token request-type theory proves user text never reaches routing, its cache key or logs (ADR-015); the committed-row contract tests are justified above.

Build: `dotnet build` of the BFF and the test project: 0 errors; no warnings in changed files.

## BFF hygiene (root §10)

- Publish size: `dotnet publish -c Release src/server/api/Sprk.Bff.Api/ -o deploy/api-publish/` (framework-dependent linux-x64), each side from a fresh worktree at a short path, zipped with `Compress-Archive -CompressionLevel Optimal` over `deploy/api-publish/*`, PDBs included.

  | Side | Commit | Worktree | Files | Zip bytes | MB |
  |---|---|---|---|---|---|
  | master | origin/master `1459a14916` | `C:\wt012m` | 194 | 43,633,395 | 41.612 |
  | branch before task | `07d67bb095` | `C:\wt012p` | 194 | 43,631,854 | 41.611 |
  | branch after task | `2201c2ce6c` | `C:\wt012b` | 194 | 43,639,537 | 41.618 |

  Delta vs fresh master: **+6,142 bytes (+0.006 MB)**. Vs the pre-task branch commit: +7,683 bytes (+0.007 MB; that side also lacks task 011's `647f04a785`). 41.62 MB total, under the 55 MB review and 60 MB stop thresholds. File counts match. The three measurement worktrees were removed with `scripts/worktree/Remove-WorktreeSafe.ps1`.
- CVE: `dotnet list src/server/api/Sprk.Bff.Api/ package --vulnerable --include-transitive`: "has no vulnerable packages given the current sources".
- No package reference changed.

### Placement Justification (for the PR)

- **Placement: in the BFF.** The registry runs inside the intake orchestrator, a BFF `IJobHandler` (task 013; ADR-052 Path C, design §4), and resolves through `IConsumerRoutingService`, which is in-process. Decision criteria: latency budget against BFF state: no (a job); writes BFF session/audit state in the same request: no; retroactive stream annotation: no; ADR-052 places it outside the BFF: no (it is a pure in-memory lookup over a BFF routing read, with no F-signal: no long run, no isolation need, no separate scale). A separate deployable would duplicate the routing service.
- **ADRs:** ADR-010 (concrete scoped registry; `IIntakeHarness` is the only new interface and a real seam), ADR-013 (Services/Intake consumes only `PublicContracts` types: `IConsumerRoutingService`, `Binding`, `RoutingContext`, `ConsumerTypes`), ADR-015 (request type is a validated token; values that are not tokens are refused and not logged), ADR-039 (harness choice is Binding data; the constant, `All` entry and row land together; deterministic, no intent mechanism), ADR-038 (tests at KEEP paths).
- **Config home (bff-extensions §A.6 / §G):** no new config field. The harness key rides the existing `sprk_consumercode`; the routing key is a `sprk_matchconditions` key.
- **Component justification (§11):** Existing: `ToolHandlerRegistry` / `NodeExecutorRegistry` key node executors and tools, not request work; `RoutingContext` extended rather than replaced. Extension: `RoutingContext`/`IRoutingContext` extended (one default member); the registry is new (different inputs and outputs), pattern copied. Cost of doing nothing: the orchestrator cannot route by request type and an unknown type has no triage path (FR-08a/c, FR-10 fail).
- **Hot path:** `Services/Ai/PublicContracts/` and `Infrastructure/DI/JobProcessingModule.cs` are BFF hot paths; `/conflict-check` is left to the PR step.

### Proposed CAPABILITY-INDEX row (main session applies; `.claude/` is read-only to agents)

`| Intake harness selection (request type → harness) | src/server/api/Sprk.Bff.Api/Services/Intake/Harnesses/IntakeHarnessRegistry.cs (contract: IIntakeHarness.cs, IntakeHarnessContracts.cs) | Add a harness = one IIntakeHarness class + one AddScoped line in JobProcessingModule + one intake-harness Binding row (consumerCode = key). No harness → resolution without a harness (manual triage), never an exception. |`

## Step 9.5 quality gates

Self-review against `code-review` and `adr-check` criteria on the diff.

Fixed (F-class):
- F1: routing cache key did not include the new match dimension (cross-request-type cache poisoning). Fixed and pinned by a test.
- F2: three comments said unknown match keys are ignored; the code fails them. Fixed.
- F2: an unused public `RegisteredKeys` member on the registry. Removed.

Known limits (K-class):
- K2: duplicate harness keys throw at first registry construction in a scope, not at host start (see Deviations); fails closed (the run cannot route).
- K4: a harness whose `CanHandle` throws makes `Resolve`/`ResolveAsync` throw; the contract says `CanHandle` is deterministic with no I/O, and no harness exists yet.
- K4: a `default`-coded `intake-harness` row would match every request type; it resolves the unregistered key `default` (→ triage) unless someone also registers a harness keyed `default`. Documented on the row and above.

ADR check: ADR-010, -013, -015, -038, -039 compliant as stated in the Placement Justification; no ADR conflict, no §6.5 path needed.

rework: fix-round-1 task 012
