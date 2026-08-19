# Coord-PR Spec — Provisioning Sidecar Build/Push/Trivy Workflow

> **APPLIED 2026-08-19** — the § 0 escalation this note raised was resolved by the owner the same day: `ci-cd-unit-test-remediation-r1`'s window is confirmed closed + the worktree dormant, and `customer-provisioning-orchestration-r1` took direct ownership of `.github/workflows/**` for Phase C'' scope. The § 2 drafted YAML was committed verbatim (header comment updated to reflect direct-commit status) to `.github/workflows/build-provisioning-sidecar.yml`. § 4 open items #1/#2 (ACR resource + `SIDEcar_ACR_LOGIN_SERVER` var + RBAC) remain outstanding — pending task 101 — and do not block landing the workflow file itself (it will simply not push successfully until the ACR + var exist). § 4 open item #4 (real `actionlint`) remains outstanding — still unavailable in this environment; `python -c "import yaml"` re-validated clean at apply time.
>
> **Author**: customer-provisioning-orchestration-r1 task 115 (Phase C'' Wave G-1)
> **Date**: 2026-08-19
> **Target file**: `.github/workflows/build-provisioning-sidecar.yml` (NEW — content fully drafted below, NOT committed by this task)
> **Deps**: task 114 (Exchange sidecar Dockerfile — ✅ landed `762fe50e8`, `src/server/services/Sprk.Provisioning.ControlPlane.Sidecar/`)

---

## 0. Escalation trigger fired — coordination window status

Task 115's POML carries a binding escalation trigger:

> *"If the coordination window has already closed (28 days from ci-cd-unit-test-remediation-r1's start) OR that worktree no longer exists, STOP and escalate to determine the current owner of `.github/workflows/**` per CLAUDE.md §6 rather than committing directly — this rule is binding regardless of whether the original coordination artifact is easy to find."*

**This trigger fired.** Evidence gathered 2026-08-19:

| Check | Finding |
|---|---|
| `ci-cd-unit-test-remediation-r1` project start | `git log` shows `docs(ci-cd-unit-test-remediation-r1): initial design.md and spec.md` at `e52f5792` on **2026-06-25** |
| 28-day window (per this project's CLAUDE.md Coordination table + spec.md) | Would have closed **~2026-07-23** |
| Today | **2026-08-19** — 55 days past project start, ~27 days past the nominal window close |
| Worktree existence | `C:/code_files/spaarke-wt-ci-cd-unit-test-remediation-r1` still exists (`git worktree list`), branch `work/ci-cd-unit-test-remediation-r1` |
| Worktree HEAD activity | HEAD = `d4538dfde` "Merge pull request #505 from spaarke-dev/test/084-relocate-contract-tests", dated **2026-06-28** — **no commits in that worktree in the ~52 days since**, i.e. the worktree is dormant, not actively exercising its `.github/workflows/**` ownership window |
| Precedent | Two other r1 tasks (067, 088) already routed CI-wiring through this exact coord-note pattern rather than committing directly — see `graph-app-role-parity-coord-pr.md` and `phase-h-ci-wiring-coord-pr.md` in this same `notes/` directory. Neither of those PRs has been reported as applied by ci-cd-r1 yet either. |

**Conclusion**: the coordination window has closed AND the nominal owner worktree shows no activity reclaiming or exercising that ownership. Per the POML's binding instruction, task 115 does **NOT** commit `.github/workflows/build-provisioning-sidecar.yml` directly, even though the window's expiry could arguably be read as "ownership has lapsed, r1 may now self-serve." The trigger text is explicit that closure of the window is itself grounds to escalate, not grounds to self-authorize.

**Escalation for the human/orchestrator**: three prior CI-wiring coord-notes from this project (067, 088, and now 115) are queued against a worktree that has been dormant for ~7.5 weeks. Recommend one of:
1. Confirm `ci-cd-unit-test-remediation-r1` is still the intended owner and someone actively picks up the 3 queued coord-notes (067 + 088 + this one) in a single batch, or
2. Formally re-designate ownership of `.github/workflows/**` (e.g. back to no single owner, or to this project for its remaining CI-touching tasks 116/117), updating `projects/customer-provisioning-orchestration-r1/CLAUDE.md` Coordination table + `projects/INDEX.md` hot-path registry accordingly, or
3. Explicitly extend/renew the 28-day window if `ci-cd-unit-test-remediation-r1` work is still planned.

This is a coordination/process decision, not a code change — flagging per CLAUDE.md §6 rather than picking a path unilaterally.

---

## 1. What task 115 verified before drafting (offline, no live workflow run)

- Task 114's Dockerfile exists at `src/server/services/Sprk.Provisioning.ControlPlane.Sidecar/Dockerfile` (base `mcr.microsoft.com/powershell:7.4-mariner-2.0`, `ExchangeOnlineManagement` pinned at `3.5.1`, `EXPOSE 8091`, `ENTRYPOINT` runs `Listener.ps1`). The Dockerfile's own header comment explicitly defers CI authorship to "task 115."
- No existing workflow in `.github/workflows/**` builds/pushes a container image to ACR — grepped the full `.github/workflows/` tree for `azurecr.io`, `docker/build-push-action`, `az acr`: zero hits. This is a genuinely new pattern for the repo, not a duplicate of an existing image-build workflow.
- No ACR resource is yet defined anywhere in `infrastructure/bicep/**` (grepped for `ContainerRegistry`/`acr`) — task 101 (Wave G-1, running in parallel with this task) owns authoring the worker App Service Bicep + sitecontainer wiring that will eventually reference an ACR image. **This workflow's `ACR_LOGIN_SERVER` is a placeholder repo variable** (`vars.SIDECAR_ACR_LOGIN_SERVER`) until task 101 lands a concrete ACR resource name. See § 4 "Open items."
- Existing OIDC pattern reused verbatim from `.github/workflows/deploy-bff-api.yml`: `permissions: id-token: write` + `azure/login@v2` with `secrets.AZURE_CLIENT_ID` / `AZURE_TENANT_ID` / `AZURE_SUBSCRIPTION_ID` — no stored ACR password/secret anywhere in this workflow.
- Trivy severity value `HIGH,CRITICAL` matches the literal value already used by `.github/workflows/nightly-health.yml`'s `trivy-fs` job (line ~540) — satisfies the "same severity-gate convention" acceptance criterion. This workflow's usage is stricter than that advisory job: `exit-code: '1'` + `ignore-unfixed: false` (hard gate; the nightly job is `exit-code: '0'` + `ignore-unfixed: true`, deliberately advisory/counting-only). The stricter posture matches the outer task's explicit instruction ("no dodging" on a brand-new image that has never shipped) and the repo's `ci-tier1-blocking.yml`/`sdap-ci.yml` Trivy job pattern (`fs`-scan, `continue-on-error: true` there is a workflow-in-progress note dated 2026-06-24, not a permanent posture).
- **Offline YAML validation**: `actionlint` is not installed in this environment; used `python -c "import yaml; yaml.safe_load(...)"` as the "or equivalent" fallback per the POML's step 2 language. Result: **parses cleanly**, 1 job (`build-scan-push`), 11 steps, all `uses:`/`run:` keys well-formed. One cosmetic note: PyYAML's `safe_load` resolves the bare `on:` key as the boolean `True` under YAML 1.1 scalar-resolution rules (a well-known GitHub-Actions-YAML quirk, NOT a real defect — GitHub's own workflow parser always treats the top-level `on` key as the literal trigger key regardless of this ambiguity, and every workflow in this repo's `.github/workflows/` already uses the identical unquoted `on:` form). No functional issue.

---

## 2. Drafted workflow — full content

```yaml
# ============================================================================
# build-provisioning-sidecar.yml — Build, Trivy-scan, and push the
# Exchange-policy provisioning sidecar image to the platform ACR.
# ============================================================================
#
# Scope: src/server/services/Sprk.Provisioning.ControlPlane.Sidecar/** only.
# This is the ONE Microsoft-platform-forced PowerShell-only residual in the
# r1 provisioning pipeline (design.md DS-1b §0/§3) -- a minimal pwsh + one
# pinned EXO module image, NOT a general tools container. Cadence: monthly
# rebuild (module-patch cycle), not per-PR.
#
# COORDINATION (READ BEFORE APPLYING): this project's CLAUDE.md names
# ci-cd-unit-test-remediation-r1 as the 28-day owner of .github/workflows/**
# (window started ~2026-06-25). That window closed weeks ago and the owning
# worktree shows no commits since 2026-06-28 -- task 115's escalation trigger
# fired (see the coord note this file ships alongside). This file is authored
# CONTENT only; it is NOT committed to .github/workflows/** by task 115.
# See: projects/customer-provisioning-orchestration-r1/notes/sidecar-ci-workflow-coord-pr.md
#
# Trivy gate: fails the build on any HIGH or CRITICAL finding, fixed or
# unfixed (ignore-unfixed: false) -- new image, zero grandfathered debt, per
# the repo's r3-era "no dodging" CVE posture. The severity value (HIGH,CRITICAL)
# matches the existing nightly-health.yml trivy-fs job for gate consistency;
# unlike that advisory job this one is a hard build-time gate (exit-code: 1).
# ============================================================================

name: Build Provisioning Sidecar

on:
  push:
    branches: [master]
    paths:
      - 'src/server/services/Sprk.Provisioning.ControlPlane.Sidecar/**'
      - '.github/workflows/build-provisioning-sidecar.yml'
  workflow_dispatch:
    inputs:
      image_tag_suffix:
        description: 'Optional extra tag suffix (e.g. a manual patch build)'
        required: false
        type: string
        default: ''

permissions:
  contents: read
  id-token: write        # OIDC federated credential for azure/login -- no stored ACR password/secret
  security-events: write # Upload Trivy SARIF to the GitHub Security tab

concurrency:
  group: build-provisioning-sidecar-${{ github.ref }}
  cancel-in-progress: false  # Never cancel an in-flight image push (matches deploy-bff-api.yml convention)

env:
  IMAGE_NAME: provisioning-sidecar
  # ACR_LOGIN_SERVER: FQDN of the platform ACR (e.g. sprkplatformacr.azurecr.io).
  # NOT YET PROVISIONED as of this workflow's authoring -- task 101 (Wave G-1,
  # in flight alongside this task) authors the ACR resource + sitecontainer
  # Bicep wiring. This is a repo VARIABLE (not a secret -- a login-server FQDN
  # is not confidential), populated once task 101 lands. See coord note
  # "Open items" section.
  ACR_LOGIN_SERVER: ${{ vars.SIDECAR_ACR_LOGIN_SERVER }}

jobs:
  build-scan-push:
    name: Build, Trivy scan, push
    runs-on: ubuntu-latest

    steps:
      - name: Checkout
        uses: actions/checkout@v6

      - name: Compute image tags
        id: tags
        shell: bash
        run: |
          set -euo pipefail
          sha_tag="${{ github.sha }}"
          semver_tag="$(date -u +%Y.%m.%d)-${{ github.run_number }}"
          suffix="${{ github.event.inputs.image_tag_suffix }}"
          if [ -n "$suffix" ]; then
            semver_tag="${semver_tag}-${suffix}"
          fi
          echo "sha_tag=${sha_tag}" >> "$GITHUB_OUTPUT"
          echo "semver_tag=${semver_tag}" >> "$GITHUB_OUTPUT"

      - name: Set up Docker Buildx
        uses: docker/setup-buildx-action@v3

      - name: Build image (local only -- Trivy scans BEFORE any push)
        uses: docker/build-push-action@v6
        with:
          context: src/server/services/Sprk.Provisioning.ControlPlane.Sidecar
          file: src/server/services/Sprk.Provisioning.ControlPlane.Sidecar/Dockerfile
          push: false
          load: true
          tags: |
            ${{ env.IMAGE_NAME }}:${{ steps.tags.outputs.sha_tag }}

      - name: Run Trivy vulnerability scanner (image)
        uses: aquasecurity/trivy-action@master
        with:
          scan-type: 'image'
          image-ref: '${{ env.IMAGE_NAME }}:${{ steps.tags.outputs.sha_tag }}'
          format: 'sarif'
          output: 'trivy-image-results.sarif'
          severity: 'HIGH,CRITICAL'
          ignore-unfixed: false
          exit-code: '1'   # FAIL the build on any HIGH/CRITICAL -- hard gate, not advisory

      - name: Upload Trivy scan results to GitHub Security tab
        uses: github/codeql-action/upload-sarif@v4
        if: always()
        with:
          sarif_file: 'trivy-image-results.sarif'

      - name: Verify compressed size ceiling (design.md §4.2a target ~200-230 MB, ceiling 250 MB)
        shell: bash
        run: |
          set -euo pipefail
          size_bytes=$(docker image inspect "${{ env.IMAGE_NAME }}:${{ steps.tags.outputs.sha_tag }}" --format='{{.Size}}')
          size_mb=$(( size_bytes / 1024 / 1024 ))
          echo "Local (uncompressed layer) image size: ${size_mb} MB"
          echo "Note: design.md's 250 MB ceiling is on the COMPRESSED registry size; this is an" \
               "early sanity check, not the authoritative measurement -- verify against" \
               "'az acr repository show-manifests' post-push if this step ever needs to gate."
          echo "Sidecar image size: ${size_mb} MB (uncompressed)" >> "$GITHUB_STEP_SUMMARY"
          if [ "$size_mb" -gt 350 ]; then
            echo "::error::Uncompressed size ${size_mb} MB is implausibly large relative to the 250 MB compressed ceiling -- investigate before push."
            exit 1
          fi

      - name: Azure login (OIDC)
        uses: azure/login@v2
        with:
          client-id: ${{ secrets.AZURE_CLIENT_ID }}
          tenant-id: ${{ secrets.AZURE_TENANT_ID }}
          subscription-id: ${{ secrets.AZURE_SUBSCRIPTION_ID }}

      - name: ACR login (via OIDC-authenticated az session -- no ACR password/secret)
        run: az acr login --name "${ACR_LOGIN_SERVER%%.*}"

      - name: Push image (SHA tag + date-versioned semver tag -- NEVER `latest`)
        uses: docker/build-push-action@v6
        with:
          context: src/server/services/Sprk.Provisioning.ControlPlane.Sidecar
          file: src/server/services/Sprk.Provisioning.ControlPlane.Sidecar/Dockerfile
          push: true
          tags: |
            ${{ env.ACR_LOGIN_SERVER }}/${{ env.IMAGE_NAME }}:${{ steps.tags.outputs.sha_tag }}
            ${{ env.ACR_LOGIN_SERVER }}/${{ env.IMAGE_NAME }}:${{ steps.tags.outputs.semver_tag }}

      - name: Summary
        if: always()
        shell: bash
        run: |
          {
            echo "### Provisioning sidecar build"
            echo "- SHA tag: \`${{ steps.tags.outputs.sha_tag }}\`"
            echo "- Semver tag: \`${{ steps.tags.outputs.semver_tag }}\`"
            echo "- Pushed to: \`${{ env.ACR_LOGIN_SERVER }}/${{ env.IMAGE_NAME }}\`"
          } >> "$GITHUB_STEP_SUMMARY"
```

---

## 3. Design decisions worth flagging to the applying reviewer

1. **Trigger scope** — `push: branches: [master], paths: [.../Sidecar/**, this-workflow-file]` + `workflow_dispatch`. No `pull_request` trigger: per DS-1b §3, this is a low-churn monthly-rebuild artifact, not a per-PR build. A PR that touches the Dockerfile gets normal repo CI (build/test/lint) but not an image push — consistent with "no image published until it lands on `master`."
2. **Two-phase build/push** — the image is built and Trivy-scanned locally (`push: false, load: true`) BEFORE any push step runs. A HIGH/CRITICAL finding fails the job at the scan step, so nothing ever reaches the ACR that hasn't passed the gate. This is stricter than a single `docker/build-push-action` call with `push: true` (which would push before/regardless of a separate scan step).
3. **Never `latest`** — two tags only: the immutable `github.sha` and a human-legible `YYYY.MM.DD-{run_number}` calendar-semver. Per outer-task instruction and design.md's "pin, never `latest`" convention already used for the `ExchangeOnlineManagement` module pin inside the Dockerfile itself.
4. **Size-ceiling step is a sanity check, not the authoritative gate** — `docker image inspect --format='{{.Size}}'` reports the local uncompressed layer sum, not the registry-side compressed size design.md §4.2a's 250 MB ceiling actually targets. Set the local threshold generously high (350 MB) purely to catch a gross regression (e.g. someone accidentally adds `az` CLI back in) without false-failing on the expected uncompressed-vs-compressed gap. A precise compressed-size check would need a post-push `az acr repository show-manifests` call; left as a documented follow-up rather than adding push-then-maybe-fail complexity to a first version.
5. **`ACR_LOGIN_SERVER` is a placeholder** — see § 4.

---

## 4. Open items for whoever applies this coordinated PR

| # | Item | Status | Owner |
|---|---|---|---|
| 1 | **ACR resource does not exist yet.** No `Microsoft.ContainerRegistry` resource anywhere in `infrastructure/bicep/**` as of 2026-08-19. This workflow references `vars.SIDECAR_ACR_LOGIN_SERVER` (a GH Actions repo/environment **variable**, not secret) as a placeholder. | Pending task 101 (Wave G-1, in flight) | task 101 / platform Bicep owner |
| 2 | **`vars.SIDECAR_ACR_LOGIN_SERVER` must be populated** once the ACR FQDN is known, and the UAMI/service principal behind `AZURE_CLIENT_ID` must be granted `AcrPush` (or equivalent) on that registry — this workflow assumes RBAC is already in place; it does not create it. | Pending task 101 | task 101 / whoever seeds GH Actions vars for this repo |
| 3 | **Coordination window has closed** (§ 0 above) — recommend a decision on how `.github/workflows/**` ownership proceeds before applying ANY of the 3 queued r1 coord-notes (067, 088, 115). | Needs owner decision | Project owner / orchestrator |
| 4 | **actionlint not run** (unavailable in this environment) — recommend running real `actionlint` once this file is staged in a worktree that has it, before merge. | Recommended follow-up | Whoever applies the PR |

---

## 5. Coord message body (paste-ready, once § 0's ownership question is resolved)

> **What**: add `.github/workflows/build-provisioning-sidecar.yml` with the exact content in § 2 of `projects/customer-provisioning-orchestration-r1/notes/sidecar-ci-workflow-coord-pr.md` (customer-provisioning-orchestration-r1 branch, task 115).
>
> **Why**: task 114 landed the Exchange-policy sidecar Dockerfile (`src/server/services/Sprk.Provisioning.ControlPlane.Sidecar/`) with no CI to build/scan/push it — task 101's sitecontainer Bicep needs a real ACR-hosted image reference, not a manual `docker push`, per this project's "eliminate ad-hoc-deploy drift" charter (spec.md).
>
> **Prereqs before merge**:
> - Resolve § 0's ownership-window question (worktree has been dormant since 2026-06-28; two other r1 coord-notes — 067, 088 — are also queued and unapplied).
> - Task 101 must land first (or land alongside) so `vars.SIDECAR_ACR_LOGIN_SERVER` has a real value and the ACR RBAC grant exists.
> - Run real `actionlint` (this coord-note only did a `python -c "import yaml"` parse check — syntactically valid, not GitHub-Actions-schema-validated).
>
> **Cross-references**:
> - r1 spec: `projects/customer-provisioning-orchestration-r1/spec.md` §4.2a (design.md pointer), NFR-02 (Trivy gate)
> - r1 design: `projects/customer-provisioning-orchestration-r1/design.md` §4.2a + DS-1b §0/§3 (`notes/design-study-ds1b-option-d-hybrid-deep-dive.md`)
> - Prior coord-notes from this same project, same target worktree, same unresolved-window situation: `notes/graph-app-role-parity-coord-pr.md` (task 067), `notes/phase-h-ci-wiring-coord-pr.md` (task 088)
> - Dependency: task 101 (ACR + sitecontainer Bicep, Wave G-1, in flight)

---

## 6. Acceptance checklist (task 115's own POML criteria)

- [x] A coordination note exists at `notes/sidecar-ci-workflow-coord-pr.md` containing the full drafted workflow YAML (§ 2 above).
- [x] The drafted YAML passes offline validation (`python -c "import yaml; yaml.safe_load(...)"` — clean parse, 1 job / 11 steps; `actionlint` itself unavailable in this environment, documented as a follow-up in § 4).
- [x] The Trivy step uses the same severity-gate value (`HIGH,CRITICAL`) as an existing repo workflow (`nightly-health.yml` `trivy-fs` job).
- [x] This note explicitly cites this project's CLAUDE.md Coordination table entry for `ci-cd-unit-test-remediation-r1` (§ 0 above).
- [x] Escalation trigger (closed window / stale worktree) surfaced per CLAUDE.md §6, not silently bypassed in either direction (did not commit directly; did not silently drop the task either).
