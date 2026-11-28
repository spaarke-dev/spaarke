# Task 003 — engine image workflow: deviations from the sidecar copy

Workflow: `.github/workflows/build-intake-engine-image.yml` (copied from `build-provisioning-sidecar.yml`).

## Deviations

1. **Two Trivy steps instead of one.** The sidecar workflow runs one scan with `ignore-unfixed: true` and uploads its SARIF, and its header says unfixed findings still reach the Security tab. They do not: `--ignore-unfixed` filters the output in every format. Here a reporting scan (`ignore-unfixed: false`, `exit-code: 0`, SARIF) feeds the `if: always()` upload, and a gate scan (`ignore-unfixed: true`, `exit-code: 1`, table) fails the job. The gate is identical to the sidecar's; the Security tab gets the unfixed findings the header promises.
2. **Python packages are in scope (decision 2026-11-25).** The image installs hash-pinned `azure-identity`, `azure-servicebus`, `azure-storage-blob` from `src/server/services/Sprk.Intake.EngineJob/requirements.txt`, plus the engines' `lxml`. Both Trivy steps set `TRIVY_PKG_TYPES: os,library` explicitly so the OS layer and the installed Python packages are scanned; the gate is not left to a default.
3. **Added `Verify requirements.txt is fully hash-pinned`** (runs before the build). Fails if the file is missing, empty, or any requirement lacks `==` or `--hash=sha256:<64 hex>`. Trivy reports CVEs in what is installed; it cannot see a requirement losing its hash. Exercised locally against good, unpinned, unhashed, empty and missing files.
4. **Dropped the sidecar's Pester step** (sidecar-specific). **Size ceiling is 400 MB compressed** (`MAX_COMPRESSED_MB`), not 250: no engine-image size budget exists in spec or design, so this is a runaway guard. Tighten it after the first measured size (printed in the step summary).
5. **Build context is the repo root** (`.`), because the Dockerfile COPYs `src/accelerators/core/engines/`. SARIF `category` is set (`trivy-intake-engine-image`).
6. **ACR variable:** `INTAKE_ENGINE_ACR_LOGIN_SERVER`, falling back to the sidecar's `SIDECAR_ACR_LOGIN_SERVER` (already populated; the same platform ACR). A variable, not a secret. OIDC secrets are the same three the sidecar uses (`AZURE_CLIENT_ID/TENANT_ID/SUBSCRIPTION_ID`).
7. **No engines version in the tag.** The engines expose none yet; tags are the commit SHA and the date-run semver, as in the sidecar. Never `latest`.

## Not verified here

- No `actionlint` or Docker on this machine. The YAML was parsed with PyYAML (triggers, path globs, step conditions, secret references checked). The first real run (PR once task 001's Dockerfile and `requirements.txt` exist) is acceptance criterion 5 and belongs in task 008's spike report.
- The push leg needs the owner actions in the workflow header (federated credential for the master subject, `AcrPush` on the ACR). The build and scan legs need none.

## Out-of-scope finding (reported, not fixed)

- `build-provisioning-sidecar.yml` claims unfixed findings reach the Security tab, but its single `ignore-unfixed: true` scan filters them out of the SARIF (see deviation 1). Fix would be the same two-scan split.

## Escalation triggers

- `projects/INDEX.md`: no active coordination window on `.github/workflows/**` (the window closed 2026-07-23, declared expired 2026-08-19). Not fired.
- First-scan trigger (fixable HIGH/CRITICAL a base bump cannot clear): not reachable until task 001's Dockerfile exists. Not fired.

rework: none task 003
