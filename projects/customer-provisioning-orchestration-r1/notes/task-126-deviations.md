# Task 126 — Deviations Report

> **Task**: 126-h4-real-value-sourcing-per-kvsecretvaluesource-generate-copy-reference
> **Date**: 2026-08-19
> **Rigor**: FULL (sonnet/xhigh per POML metadata)
> **Status**: ✅ Complete (with one flagged operational gap — see "FromBicepOutput gap" below)

## Summary

Implemented real value-sourcing behind all four `KvSecretValueSource` enum branches, replacing the deterministic `{name}-interim-placeholder-{customerId}` string. Performed the task-084 canonical manifest DI-swap (turned out to be genuinely new work, not a 1-line change — see below). Added the FR-39 FIC-omit pluggability seam. All acceptance criteria met except for an honest, tracked operational gap on `FromBicepOutput` entries (documented, not silently swallowed).

## Deviation #1 — manifest DI-swap scope correction ("1-line" estimate did not hold)

**Baseline expectation** (POML + dispatch brief): "the task-084 canonical manifest DI-swap (C2.2, a 1-line change per DS-4)."

**Actual state**: task 084's own deviations note (`notes/task-084-deviations.md` § "Downstream consumers") explicitly states *"the file-backed reader lands (post-084 follow-on, out of scope for this task)."* No `.NET`-consumable reader for `scripts/canonical-secret-catalog/manifest.yaml` existed anywhere in the codebase — `StaticKvSecretManifest` (7 hardcoded entries) was the only `IKvSecretManifest` implementation.

**Effect on task**: Authored `FileKvSecretManifest.cs` (new file, ~240 lines) — a real YAML reader over the manifest, embedded as a build-time resource (parity with task 124's `IndexSchemas/*.json` embedding pattern) and parsed via a new `YamlDotNet` 18.1.0 package reference (zero-CVE, verified via `dotnet list package --vulnerable`). The DI swap itself (`Program.cs` line ~409) IS one line; the reader it points at is genuinely new work. Verified: 26 real canonical entries load correctly (vs the interim's 7), `Dataverse-ClientSecret` + `BFF-API-ClientSecret` BINDING invariant enforced at read time (mirrors the PowerShell generator's own write-time guard), entries sorted alphabetically (determinism contract parity).

## Deviation #2 — generate/copy/reference vocabulary mismatch (dispatch brief's 3-bucket model vs the real 4-member enum)

**Baseline expectation**: POML `<goal>`/`<steps>`/acceptance criteria describe three buckets — generate / copy / reference (the last explicitly: "no value-copy call made").

**Actual state**: `KvSecretValueSource` has FOUR members (unchanged since task 047): `Generated`, `FromExistingKvSecret`, `FromBicepOutput`, `FromRunParameters`. None of the four's original doc comments describe a pure "write-a-pointer-no-value" case — all four ultimately resolve to a real `SetSecretAsync` call when they succeed.

**Resolution taken** (grounded in concrete evidence, not invention):
- `Generated` → `RandomNumberGenerator`-based 256-bit hex value. Maps cleanly to "generate."
- `FromExistingKvSecret` **and** `FromRunParameters` → BOTH resolve via the SAME mechanism: look up a `KeyVaultSecretRef` in a new `KvSecretWriteRequest.SecretParameters` dictionary (keyed by canonical name — an additive convention on top of the existing `RunParameters.Secrets: IDictionary<string, KeyVaultSecretRef>` contract, which task 025's own doc comment already establishes as "runtime handlers resolve the referenced secret via Managed Identity at the moment of use"), then read the REAL cleartext via `SecretClient.GetSecretAsync` against the referenced vault. Both map to "copy." **Why not treat `FromRunParameters` as a no-copy "write the reference string" case**: App Service's `keyVaultReferenceIdentity` resolves KV references exactly ONE hop — writing a nested `@Microsoft.KeyVault(...)` pointer as a KV secret's VALUE would return that literal string (unresolved) to any consumer reading it via a KV-ref app setting, silently breaking every such consumer. Several `FromRunParameters` entries (`TenantId`, `Dataverse-ServiceUrl`) are non-secret plain values whose manifest `purpose` text explicitly says "stored in KV for uniform reference-resolution semantics" — confirming the REAL value, not a nested pointer, belongs there. A reference-string-write would have been architecturally wrong, not just a vocabulary mismatch.
- `FromBicepOutput` → see Deviation #3 below (the genuine gap).

**Test coverage for AC4's literal wording** ("a Reference-type entry... no value-copy call made"): mapped onto `SecretClientKvWriterTests.WriteAsync_FromBicepOutputEntryAlreadyExists_SkipsWithoutInvokingResolverOrPut` — proves the writer never calls the resolver (hence never performs a value-copy round-trip) when a `FromBicepOutput` entry already exists on the target vault. This is the closest evidence-grounded analog to AC4's intent given the real enum doesn't have a pure reference-only member.

## Deviation #3 — FromBicepOutput gap (flagged, not silently swallowed — READ THIS)

**This is the single most important finding from this task and needs owner/orchestrator attention before Wave G-2 is considered deployable.**

**The gap**: `KvSecretValueResolver.ResolveAsync` ALWAYS returns `Failed` for `KvSecretValueSource.FromBicepOutput` entries when the writer determines the secret does NOT already exist on the target vault (the writer skips calling the resolver at all when the secret DOES already exist — see `SecretClientKvWriter.ExecuteEntryAsync`'s `entry.ValueSource == FromBicepOutput && exists` guard).

**Why**: `scripts/canonical-secret-catalog/kv-secrets.generated.bicep` (task 084's generated Bicep module) is the INTENDED writer for these ~15 entries — it takes a `secretValues` object param "resolved from module params, Bicep outputs of upstream resource modules, or existing KV resource references" per its own header comment. Task 086 (IaC alignment, ✅ complete) did NOT wire this module into `customer.bicep` / `model1-shared.bicep` / `model2-full.bicep` — confirmed via grep: `kv-secrets.generated.bicep` is referenced only in comments across `infrastructure/bicep/`, never as an actual `module` call. `InterStepState` (design.md §6.2's locked, enumerated POCO) does not carry per-secret slots for these entries either (only `ContainerTypeId`/`SpeContainerId` partially overlap two of the ~15).

**Operational consequence**: on a FRESH customer provisioning run, H4 will receive per-entry `Failed` results for every `FromBicepOutput` entry that isn't already present on the target vault (roughly 15 of manifest.yaml's 26 entries: `AiSearch--AdminKey`, `AiSearch-Endpoint`, `AppInsights-ConnectionString`, `AzureOpenAI-Endpoint`, `BFF-API-Audience`, `BFF-API-ClientId`, `Communication-WebhookUrl`, `DocumentIntelligence-ApiKey`, `DocumentIntelligence-Endpoint`, `Redis-ConnectionString`, `ServiceBus-ConnectionString`, `SPE-CommunicationArchiveContainerId`, `SPE-ContainerTypeId`, `SPE-DefaultContainerId`, `Storage-ConnectionString`). Per H4's EXISTING, unchanged logic, ANY per-entry `Failed` result triggers `QuarantineRequired` (`KvWritePartialFailure`) — meaning H4 will not cleanly succeed on a fresh customer today.

**Why this is still the correct fix, not a regression**: the alternative (what task 125 shipped, unchanged until now) was H4 reporting `Success` while writing a non-functional placeholder string for these SAME 15 entries — DS-4's own words: "a successful H4 leaves every downstream secret consumer broken." Failing loudly with `QuarantineRequired` (visible, actionable, blocks the pipeline) is strictly better than succeeding silently with garbage values (invisible, breaks H3/H6/H7/H14 downstream with no signal). This task's whole mandate was to eliminate exactly the "wrong-but-silent" failure mode — applying that same discipline to `FromBicepOutput` when no real source exists is consistent, not a scope failure.

**Recommended follow-on** (NOT built here — outside this task's declared scope of `KvSecretValueResolver.cs` + the manifest DI-swap): either (a) a new task wires `kv-secrets.generated.bicep` into the three stack Bicep files (the natural completion of task 086's own stated intent), or (b) H2a's ARM deployment runner is extended to surface its outputs into a structure H4 can read (an `InterStepState` schema extension, following the established "controlled schema extension" pattern already used 3 times in this file for `ImportedSolutions`/`SpeContainerId`/`ProvisionedUsers`), or (c) operators pre-seed these secrets via `Seed-CustomerKeyVault.generated.ps1` (task 084's OWN generated seeder script) before H4 runs, treating H4 as rotation-safe idempotent (which it already is — a pre-seeded secret is skipped, not overwritten). Recommend escalating this choice to the project owner rather than a subagent picking one unilaterally, since it has real deployment-sequencing implications across H2a/H4/H7.

## Deviation #4 — FIC-omit seam design (spec.md FR-39, "already-migrated-to-FIC" condition)

**Implemented**: `KvSecretWriteRequest.OmitCanonicalNames` (a `IReadOnlySet<string>`, defaults empty) + a new non-secret run-parameter key `H4KvSecretsPopulationHandler.FicOmitSecretNamesParameterKey = "ficOmitSecretNames"` (comma-separated canonical names). When a canonical name is present in this set, the writer OMITS it entirely — zero KV calls, zero resolver calls (`KvSecretWriteAction.Omitted`, a new enum member).

**Why OMIT only, not the "documented sentinel" alternative**: spec.md FR-39's own coordination note (`notes/AUTH-V4-CHANGE-REQUEST-RESPONSE.md` § "already-migrated-to-FIC sentinel contract") states the exact sentinel format is unresolved and explicitly deferred to auth-v4: *"Left this open in both amended POMLs as an explicit coordination point rather than inventing a format unilaterally — please advise which shape your BFF-side code will actually check for (or ignore)."* No response has landed as of this task's execution. The POML's own goal text pre-authorizes OMIT as one of two acceptable outcomes without requiring further coordination ("it either OMITS the KV secret entirely or writes a documented sentinel value... do not invent the sentinel format unilaterally"). OMIT needs no invented contract; the sentinel path does. Implementing only OMIT is therefore the safe, pre-authorized subset — NOT a partial implementation of the requirement.

**No special-casing preserved** (parity with task 125's FR-39 commitment): the omit mechanism is entirely DATA-driven (a run-parameter-supplied set), never a hardcoded `canonicalName == "BFF-API-ClientSecret"` check in writer code. Verified by test: `WriteAsync_EntryInOmitCanonicalNames_OmitsEntirelyWithoutAnyKvOrResolverCall` is parameterized over BOTH `BFF-API-ClientSecret` AND an unrelated name (`AiSearch--AdminKey`) — proving the mechanism has no built-in awareness of which name is "special." Escalation trigger #2 in the POML (auth-v4 Phase 5 secret retirement landing before this task executes) did NOT fire — no evidence found that `BFF-API-ClientSecret` provisioning has been dropped from the auth-v4 rollout as of 2026-08-19.

## Deviation #5 — test file location (flat, not nested)

**Baseline expectation** (POML `<outputs>`): `src/server/services/Sprk.Provisioning.ControlPlane.Tests/Handlers/KvSecretsPopulation/KvSecretValueResolverTests.cs` (a nested subfolder).

**Actual**: placed at `src/server/services/Sprk.Provisioning.ControlPlane.Tests/Handlers/KvSecretValueResolverTests.cs` — flat, matching the ACTUAL existing convention for every sibling test file in this handler family (`SecretClientKvWriterTests.cs`, `H4KvSecretsPopulationHandlerTests.cs`; no `Handlers/KvSecretsPopulation/` subfolder exists anywhere in the test project). Also added `FileKvSecretManifestTests.cs` at the same flat location (not explicitly named in POML `<outputs>` but required to exercise the new manifest reader).

## Deviation #6 — commit-scope note (dispatch brief vs POML file list)

The dispatch brief's "do NOT touch `SecretClientKvWriter.cs`" instruction conflicts with the POML itself, which explicitly lists `SecretClientKvWriter.cs` as `role="modify"` ("this task fixes ResolveValueForEntry inside it") and states the goal in terms of `AzCliKvSecretsWriter.ResolveValueForEntry` (task 125's SDK-ported equivalent of that same method). Treated the POML as authoritative (it is the task's own contract) and made a surgical, minimal change to `SecretClientKvWriter.cs`: ONLY the value-resolution call site + a resolver-injection constructor parameter + the FIC-omit / FromBicepOutput-skip control-flow additions. Did NOT touch the T1/T5 mechanics, the SDK plumbing, the never-delete guard, or the ARM preflight probe — all of task 125's actual mechanics are byte-for-byte unchanged.

## No ADR tensions surfaced

Path C (comply) throughout: cryptographically secure RNG (root CLAUDE.md §9), no cleartext logging (ADR-028, verified zero `Log*` calls in `KvSecretValueResolver.cs`), DI via constructor injection with ≥2 implementations per new seam (ADR-010, parity with 3 sibling interfaces in this same folder), MI-outbound `TokenCredential`/`DefaultAzureCredential` (ADR-028). Zero new HIGH-severity CVEs (`dotnet list package --vulnerable --include-transitive` on the Core project — clean). No BFF-touching files (this is L2 Provisioning ControlPlane, a separate service) — CLAUDE.md §10 BFF Hygiene checklist does not apply.

## Quality gates

- `dotnet build` — 0 errors, 0 warnings across Core / Worker / Tests.
- `dotnet test` (full L2 suite) — 903/903 passing (baseline 883 + 20 new tests: 4 in `SecretClientKvWriterTests.cs`, 7 in `KvSecretValueResolverTests.cs`, 9 in `FileKvSecretManifestTests.cs`).
- Self-review (code-review + adr-check dimensions applied manually, task-execute Step 9.5): zero Critical findings, zero unresolved ADR violations. One design decision (Deviation #4's OMIT-only choice) and one operational gap (Deviation #3) are surfaced explicitly above rather than silently resolved — consistent with root CLAUDE.md §6.5's anti-silent-compliance principle even though neither is a strict ADR conflict.
