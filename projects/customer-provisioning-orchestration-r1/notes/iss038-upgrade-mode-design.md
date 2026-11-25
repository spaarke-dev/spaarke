# ISS-038 (#3018) — design for a working handler upgrade mode

> **Status**: DRAFT for owner review, 2026-11-24. Nothing implemented. Design by an Opus planning agent; code citations are
> `origin/master` `e70ff38894`. The live checks were read-only: registry GET, blob manifest GETs, and ARM what-if with no
> deployment created.

## 0. What was checked live (2026-11-24)

| Item | Value |
|---|---|
| ACME registry row (admin Dataverse `spaarkedev1`, `spaarke-constants.yaml:77-81`) | `sprk_bffversion` **2026.10.16-2**, `sprk_solutionversion` **"SpaarkeMaster Managed 1.3.2.1"**, `sprk_provisionedon` 2026-11-23 14:01:48Z, status Ready |
| `provisioning-artifacts/latest.json` | buildId 2026.10.16-2. There is no requirements field. |
| `dataverse-solutions-latest.json` | SpaarkeMaster **1.3.2.1** (buildId 2026.11.22-333) |
| `provisioning-arm-latest.json` | `customer-arm-2026.11.25-23.json` |
| ARM history, subscription `2ff9ee48…` | Newest Succeeded deployment is `customer-acme-20261120182522` (Incremental) |

## 1. Version vocabulary: keep what H13 writes, and parse it

| Concept | Canonical form | Producer | Registry column |
|---|---|---|---|
| BFF version | CI build id `yyyy.MM.dd-<run_number>`. It is ordered by run_number, which only increases within the one workflow (`publish-bff-artifact.yml:159-165`). | H9 (`InterStepState.BffBuildId`) | `sprk_bffversion` (`H13E2EAcceptanceGateHandler.cs:1049`) |
| Solution version | SpaarkeMaster 4-part version (e.g. `1.3.2.1`) plus the package type | H6 (`ImportedSolutions`) | `sprk_solutionversion` = `"SpaarkeMaster {Managed\|Unmanaged} x.y.z.w"` (`ImportedSolutionSet.cs:38`, `H13…:1050`). The skill's Step 6a fallback writes the same value. |

The registry columns stay as they are, so ACME's row needs no migration. Add the inverse parsers:
- `BffBuildId.TryParse`.
- `SpaarkeMasterVersion.TryParse`. It also accepts the old v3 form `SpaarkeMaster 1.2.0.0 (managed)`.

Retire the obsolete vocabulary:
- The single matrix cell `1.0.0-net10|S2026.08`.
- The semver and `S<YYYY>.<MM>` schemes in `docs/deployment/version-compatibility-matrix.md:70-112`.

## 2. L2 works out current and target versions itself; `upgradeOfProvisionedOn` becomes the trigger

**Today:**
- `provisionedOn` switches on upgrade mode (`H0PreflightHandler.cs:268-275`, `H2a…:399/435`, `H4…:407`, `H4b…:401`).
- In upgrade mode H0 requires four intake keys that nothing produces (`H0…:494-510`; `IntakeParameterCatalog.cs:212-216`).
- The skill sends only `upgradeOfProvisionedOn`. CreateRun checks it (`RunsEndpoints.cs:688-712`), but if the registry lookup fails it lets the run through unchecked (`:740-751`).

**Design:**
1. **One trigger.** `upgradeOfProvisionedOn` is the only operator input. Remove `provisionedOn` and the four version keys from `IntakeParameterCatalog` and `HandlerRunInputs`. CreateRun answers 400 `intake.retired_key` if a client still sends them (ADR-020 A, an approved tension).
2. **H0 reads the registry row on every run.** Extend `DataverseEnvironmentRegistrySnapshot` with `BffVersion` and `SolutionVersion`. H0 re-runs `RegistryRunAdmissionRule.Evaluate`, which closes the fail-open gap at `RunsEndpoints.cs:740-751`:
   - A row with `sprk_provisionedon` and no matching acknowledgement fails Resumable with `upgrade-baseline-mismatch`.
   - A registry fault fails Resumable with `upgrade-baseline-unavailable`. H0 has no side effects, so a Resumable failure costs nothing.
3. **H0 resolves the target.** A shared `IReleaseArtifactResolver` replaces the duplicate readers in `ArtifactManifestVerifier` and `DataverseWebApiSolutionImporter.ParsePackageEntry`. It reads `latest.json` and `dataverse-solutions-latest.json`. If the intake carries `buildId`, H0 checks it against latest.json, so a mismatch fails at H0 instead of at H9.
4. **New InterStepState outputs, produced by H0:**
   - `Upgrade {ProvisionedOn, CurrentBffBuildId, CurrentSolution}`, null on a fresh run.
   - `ReleaseTarget {BffBuildId, BffSha, SolutionVersion, ManagedSha256}`, set on every run.
5. **Consumers.**
   - H2a, H4 and H4b read `InterStepState.Upgrade`.
   - H9 deploys `ReleaseTarget.BffBuildId`; the existing check refuses if latest.json moved after H0.
   - H6 refuses, Resumable `solution-target-moved`, when the manifest version differs from `ReleaseTarget.SolutionVersion`, before the import.

   Both manifests are single mutable pointers, so without this pinning H0's verdict might not describe what actually gets deployed.

**Why it matters now:** every upgrade so far has run in fresh mode.
- H4 never reaches its rotation-safe skip (`SecretClientKvWriter.cs:253`).
- H4b does not poll `/healthz` on a live BFF it has just restarted (`H4bBulkAppSettingsHandler.cs:401`).

## 3. Compatibility: rules shipped with each artifact, not a pair matrix

**Today:**
- The verdict comes from the target cell alone (`JsonFileVersionCompatMatrix.cs:100-110`), so upgrade-path rules are never evaluated.
- Yellow records a Pending gate and carries on (`H0…:555-575`). Nothing else reads that gate.

**Design:** each artifact declares its own requirements in its source tree, in the same PR as the code that creates the dependency. CI copies them into the published manifest. The PR author knows the solution version they need, and a forgotten rule shows up in review.

`src/server/api/Sprk.Bff.Api/release-requirements.json` is published into `latest.json` and `bff-api-{buildId}.json` as `requires`:
```json
{ "minSpaarkeMaster": "1.3.1.0",
  "upgradeNotes": [ { "whenCurrentBelowBuild": "<first #2128 build>", "verdict": "Yellow", "ucb": ["U-CB-3"],
      "precondition": "A9 home sign-in + Set-ExternalAccessContactSecuritySchema.ps1 -Apply/-Verify", "ref": "CLAUDE.md §3 R3" } ] }
```

The SpaarkeMaster requirements file is published into the `solutions.SpaarkeMaster.requires` entry:
```json
{ "minBffBuild": "2026.10.16-2",
  "upgradeNotes": [ { "whenCurrentAtOrBelow": "1.3.2.1", "verdict": "Yellow", "ucb": ["U-CB-1"],
      "precondition": "Remove-EbillingRateCardCoreLookups.ps1 report mode", "ref": "ebilling-r1 task 193" } ] }
```

**How H0 evaluates.** A pure `ReleaseCompatEvaluator` replaces `IVersionCompatMatrix`. C is the current pair, T the target pair; the worst verdict wins.

| Check | Verdict |
|---|---|
| Target cannot be parsed; T.sol < C.sol; package type switch | Red |
| Final pair T.bff × T.sol breaks `minSpaarkeMaster` or `minBffBuild` | Red |
| Mid-run pair C.bff × T.sol breaks the target solution's `minBffBuild`. H9 runs after H6, and H6 took 73 minutes on ACME. | Yellow |
| `upgradeNotes` crossed between C and T | The note's verdict |
| Current pair cannot be parsed or is missing | Yellow `baseline-unknown` |
| A manifest has no `requires` block (every artifact today) | Yellow `requirements-undeclared` |
| T.bff < C.bff | Yellow |
| Otherwise, including an equal pair | Green |

There is no "unknown pair" any more: any pair that parses gets evaluated.

**Yellow must really stop the run.** It becomes `WaitingOnGate`, following the H3 pattern. Add acknowledgement gates: advancing one sets it Cleared and records who and when. The handler then proceeds only while the evidence digest is unchanged.

**Live ordering rules** (`current-task.md` "Ordering rules") map onto this:
- 092+/095a/#2724 become BFF `minSpaarkeMaster: 1.3.1.0`.
- The rate-card rule becomes a SpaarkeMaster upgradeNote.
- The A9 and ExternalAccess-schema rules become BFF upgradeNotes.
- The H7b procedure-10 rule is already enforced at run time by S27. The 091 storage rule is met by the order H2a and H4b run in, before H9.

## 4. H2a drift

**Today:**
- H2a runs what-if on the *new* template (`ArmWhatIfDriftDetector.cs:77-87`) and counts every Modify/Deploy/Unsupported as drift (`:134-138`).
- Drift is `QuarantineRequired`, even though nothing has been changed yet.
- The report goes to the Worker's local disk, where operators cannot read it.

**Calibration, ACME, read-only.** What-if ran twice:
- with the exported template and parameters of `customer-acme-20261120182522`;
- with the published `customer-arm-2026.11.25-23.json`.

Both gave **32 Modify, 14 NoChange, 0 Create/Deploy/Unsupported, 140 property deltas**. Today's detector would quarantine every ACME upgrade. Only two deltas carry meaning:
- **KV `tags.spaarke-secret-free-identity` Delete.** H4 writes this tag after every H2a run, so it is an expected change, not drift.
- **Cosmos `enableAutomaticFailover`: live `true`, template `false`**, still different four days after a deploy. **Owner decision:** most likely set it to `true` in Bicep.

Everything else is noise: property `Delete` of values the server fills in or defaults, and property `Create` of write-only values.

**Design:**
1. **Two passes.**
   - Pass A (drift) runs what-if on the *baseline*: the newest Succeeded `customer-{id}-*` deployment's exported template and parameters, which contain no secrets.
   - Pass B (plan) runs what-if on the target template.
   - Expected change = B minus A.
2. **Classification.** A pure `WhatIfDriftClassifier`, driven by an embedded `whatif-drift-rules.json`:
   - Ignore: NoChange, NoEffect, Ignore, property `Create` with before=null, and property `Delete` with after=null outside `tags.*`.
   - Allowed: handler-owned paths (the KV tag).
   - Drift: a property `Modify` where before and after differ and neither is null; a tag `Delete`; a change to an array element's value.
   - Deploy/Unsupported: "unverifiable" in the evidence, never blocking.
3. **Pass B is evidence only,** except a short destructive list that raises a Yellow acknowledgement gate: `sku.name` on data-bearing resources, and the OpenAI embedding deployment's `properties.model.*`.
4. **Outcomes.**
   - Drift becomes the acknowledgement gate `h2a-upgrade-drift`, with the classified report carried in the gate, not on disk. Advancing it means apply and reset (design §14A.5 option A); cancelling is option B.
   - No baseline in ARM history means pass B is classified alone and the gate is required.
   - A what-if infrastructure fault stays Resumable.

## 5. Rollout

**ACME:** no data migration. The first run under the new code resolves baseline = target = (2026.10.16-2, 1.3.2.1). H0 gives Yellow `requirements-undeclared` until the next BFF and solution publishes carry `requires`. H2a should find no drift once the KV tag is allowed and the Cosmos question is settled.

**Skill:**
- Steps 1a and 4.0 keep their code; remove the "fresh mode" caveats.
- Keep `buildId` as an assertion that H0 now checks.
- Teach the execute loop the two acknowledgement gates: show the evidence, then advance or cancel.

**Touch points:**
- spec FR-34, design §14A.3/§14A.5.
- `docs/deployment/version-compatibility-matrix.md` v5, which becomes the rules contract.
- `.claude/constraints/provisioning.md` "Run-context contract" (H0 as the registry producer).
- ADR-020 A (retired keys rejected); ADR-027 §3. No new exception.
- The `current-task.md` ordering rules move into the requirement files.

**Tests:**

| Test file | Coverage |
|---|---|
| `RunContextContractTests` | `Upgrade` and `ReleaseTarget` are produced by H0 only |
| `IntakeSchemaProfileParityTests` | Retired keys answer 400 |
| `H0PreflightHandlerTests` | Acknowledgement mismatch; registry fault; Yellow sets `WaitingOnGate` |
| `ReleaseCompatEvaluatorTests` | Every row of the §3 table |
| Parser tests | v3/v4 forms and legacy fingerprint |
| `H13BuildPromotedColumnsTests` | Round-trips through the parser |
| `WhatIfDriftClassifierTests` | A sanitised ACME what-if fixture; expect 0 drift and 1 unresolved (Cosmos) |
| H9 and H6 | Target moved refuses with Resumable |
| H2a | The gate replaces Quarantine |

**Tasks (about 38 h):**
1. Parsers and snapshot columns (2 h).
2. `IReleaseArtifactResolver`; H0 baseline, target and outputs (6 h).
3. Retire intake keys; move H2a/H4/H4b to `InterStepState.Upgrade` (3 h).
4. H9 and H6 pinned to `ReleaseTarget` (3 h).
5. Acknowledgement gate kind and Yellow becoming `WaitingOnGate` (4 h).
6. `ReleaseCompatEvaluator` and requirement files; CI (`publish-bff-artifact.yml`, `publish-dataverse-solutions-manifest.yml`, the artifact manifest schema) gets `requires` (6 h).
7. Two-pass drift detection, classifier and rules file (8 h).
8. Docs, skill and ordering-rule migration (3 h).
9. Owner-OK live steps: Worker and API deploy, the Cosmos failover decision, publish one BFF and one solution with `requires`, then an ACME no-op upgrade run as acceptance (3 h plus the run).

## 6. Owner decisions

1. Approve the design (one trigger key; L2 derives versions; requirement files per artifact; acknowledgement gates; two-pass drift).
2. Cosmos `enableAutomaticFailover`: set it to `true` in Bicep (recommended), or change the live value to match the template.
3. Task 6 changes CI workflows (`.github/workflows/**`, a hot path). Check `projects/INDEX.md` for an active CI-governance project first.
