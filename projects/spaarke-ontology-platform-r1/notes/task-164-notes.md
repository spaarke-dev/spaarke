# Task 164 notes: Deploy-Release.ps1 applies and verifies the worklist grid configuration (D-185)

Rigor FULL. Branch task/164-release-worklist-config. No environment was touched: Deploy-Release.ps1 and
Apply-WorklistGridConfiguration.ps1 were not run in any mode (the first deploys on load; its preflight and Phase 2 write).

## What changed

- `scripts/Deploy-Release.ps1`
  - New function `Invoke-OntologyConfigPhase` (apply, then `-Verify -RequireRow`; returns `$true` or `$false`).
  - New per-environment **Phase 2b: Ontology configuration**, between Phase 2 and Phase 3. Resets `$ontologyConfigFailed` for each environment.
  - Phase 3's first branch is now `if ($ontologyConfigFailed)`: skip, and print "the SpaarkeMaster import carries the D-185 role change and needs the worklist configuration first". No `-StopOnFailure` term.
  - `'OntologyConfig'` added to the `-SkipPhase` ValidateSet. Skipping prints a warning and sets nothing, so Phase 3 is not blocked.
  - `.DESCRIPTION` phase list and the `-SkipPhase` help updated.
- `tests/scripts/Deploy-Release.OntologyConfig.Tests.ps1` (new, 19 cases) and its entry in the `ontology-script-tests` job of `.github/workflows/ci-standalone-checks.yml` (plus the two doc lines that name the job's contents: `.github/WORKFLOWS.md`, `docs/procedures/ci-cd-workflow.md`).
- `docs/guides/SPAARKE-CUSTOMER-DEPLOYMENT-GUIDE.md` §7.8: the release does this; the manual command stays for an out-of-band fix; the policies gap is stated.

## Behaviour

| Case | Result |
|---|---|
| Normal run | Phase 2, then 2b apply, then 2b `-Verify -RequireRow`, then Phase 3 |
| `-WhatIf` | apply runs with `-WhatIf`; verify skipped; nothing written (the apply script only reads under `-WhatIf`) |
| 2b non-zero exit or exception (apply or verify) | `$ontologyConfigFailed` and `$envFailed` set; Phase 3 skipped for this environment even with `-StopOnFailure $false`; Phases 4 and 5 keep their `$envFailed`/`$StopOnFailure` rule |
| `-SkipPhase OntologyConfig` | warning; Phase 3 runs (must-not-fire) |
| Phase 2 failed with `-StopOnFailure $true` | 2b skipped like the later phases ("previous phase failed") |
| dev (`solutionPackageType none`) | 2b still runs; Phase 3 skips as before |

## Test proof (Pester 5.7.1, by path)

- Before the change (new test file against the unchanged script): **19 failed / 0 passed**.
- After: new file **19 passed / 0 failed**; `Apply-DoLanePolicies.Tests.ps1` 19 passed; `Invoke-OntologyStampBackfill.Tests.ps1` 10 passed.
- Must-fire: a failing apply, a failing verify, a thrown exception and a missing script each return `$false`; the Phase 3 chain's first branch is the flag and contains no `StopOnFailure`.
- Must-not-fire: the `-SkipPhase OntologyConfig` branch warns, and neither `ontologyConfigFailed` nor `envFailed` appears in it; a stale `$LASTEXITCODE` does not fail a good run.
- PSScriptAnalyzer 1.24.0 on `Deploy-Release.ps1`: identical findings before and after (43 PSAvoidUsingWriteHost, 1 PSUseBOMForUnicodeEncodedFile, 1 PSUseDeclaredVarsMoreThanAssignments). Nothing new.

## Test-scope justification

Beyond the listed cases: the "stale `$LASTEXITCODE`" case (Phase 2's `az` calls leave a code behind and the phase reads `$LASTEXITCODE`), and the "returns one boolean" case (the child script's output must not become the function's result).

## Deviations

1. The POML says `SPAARKE-CUSTOMER-DEPLOYMENT-GUIDE.md` §7.8 "documents the manual step". It did not; no doc named `Apply-WorklistGridConfiguration.ps1` outside the project notes. §7.8 now has the release behaviour and the manual command.
2. `.claude/skills/deploy-new-release/SKILL.md` may list the release phases. It sits under `.claude/`, which sub-agents cannot write, so it was not checked or edited; the main session should look.
3. `task-163-notes.md` (a knowledge file in the POML) does not exist on this base, so it was not read.

## Noticed, not changed (out of scope)

- With `-StopOnFailure $false`, a failed Phase 2 (BFF) does not stop Phase 2b or Phase 3. D-185 wants the BFF first, so a failed BFF plus an imported role change gives the same empty worklist. Not gated here because the task limits Phase 3's new gate to the configuration phase.
- Phase 3 does not check `$LASTEXITCODE` after the import script (it relies on a throw). Existing behaviour.

## Known gap: the Do-lane and know-promotion policies stay manual (issue text, NOT filed)

**Title:** Release pipeline never applies the Do-lane and know-promotion policies (Apply-DoLanePolicies.ps1) to customer environments

**Body:**
The ontology policies (the Do-lane policies in `config/do-lane.policies.json` and the know-promotion policies in `config/know-promotion.policies.json`) are written to Dataverse only by `scripts/Apply-DoLanePolicies.ps1`, which an operator runs by hand. `scripts/Deploy-Release.ps1` does not call it, the H12b seeder does not seed policies, and no workflow in `.github/workflows/` runs it. A customer environment that receives a release therefore has no policy rows, or has older versions than the repo definition, until someone runs the script against it.

Ontology task 164 added Phase 2b to `Deploy-Release.ps1`, which applies and verifies the worklist grid configuration (`scripts/Apply-WorklistGridConfiguration.ps1`) between the BFF and the SpaarkeMaster import. It deliberately left the policies out of scope.

What to do: decide where policies belong in the release order (policy rows are data read by the BFF, so after the BFF phase; they do not carry a role change, so they have no D-185 ordering constraint), then add a phase that runs `Apply-DoLanePolicies.ps1` (idempotent; supports `-WhatIf`; its `-Verify` is read-only and reports DRIFT) with the same shape as Phase 2b. `Invoke-OntologyConfigPhase` in `scripts/Deploy-Release.ps1` is the model, and `tests/scripts/Deploy-Release.OntologyConfig.Tests.ps1` shows how to test it against a fake script. Decide whether a policy failure should block later phases (probably not: it does not protect a security change). Confirm the operator's `az` sign-in can write the policy tables on a customer environment (NFR-11: no service principal or secret). Update `docs/guides/SPAARKE-CUSTOMER-DEPLOYMENT-GUIDE.md` §7.8, which now states the gap.

Acceptance: a release against a clean environment ends with the policy rows present and `Apply-DoLanePolicies.ps1 -Verify` reporting no drift; a Pester case covers the new phase's failure handling.
