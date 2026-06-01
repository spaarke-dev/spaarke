# D-05 — deploy-platform.yml Disposition

> **Date**: 2026-06-01
> **Project**: github-actions-rationalization-r1
> **Phase**: 2 (Rationalization) — task 020
> **Related**: spec FR-06 (≤8 workflow target), NFR-04 (`git rm` for deletes), NFR-06 (per-decision record), design.md D-03 (delete-by-default)
> **Recommendation**: **DELETE**

## Context

`deploy-platform.yml` is a manual-only (`workflow_dispatch` only) wrapper around `./scripts/Deploy-Platform.ps1`. Its declared purpose is to provision shared platform infrastructure (Resource Group, Key Vault, App Service, OpenAI, AI Search, Document Intelligence) per environment, with `mode: what-if | deploy` and `environment: dev | staging | prod` inputs. The workflow is 221 lines and was created 2026-03-13 alongside `deploy-bff-api.yml` and `provision-customer.yml` as the initial CI/CD scaffolding.

## Evidence

**Wave A inventory data**:
- Last-30-day runs: **0 total** — never invoked in last 30 days
- Triggers: `workflow_dispatch` only (no push or PR triggers)
- Pattern observed: N/A (no runs to analyze)
- Last meaningful commit: `8fa46c60` 2026-03-13 (spaarke-dev) — "feat(ci): add CI/CD workflow files for platform, BFF API, and customer provisioning"

**Recent contributor check (live)**: `git log --since='90 days ago' --format='%h %an %ad %s' -- .github/workflows/deploy-platform.yml` returns the single commit `8fa46c60` from 2026-03-13 (>2 months ago). No edits in the last 90 days.

**Value assessment**: The underlying provisioning logic lives entirely in `./scripts/Deploy-Platform.ps1`. The workflow only adds (a) auditability of who-ran-platform-deploy-when via GitHub Actions history, and (b) OIDC-scoped Azure login. Neither value has been realized: the team runs `Deploy-Platform.ps1` locally per `docs/procedures/production-release.md`, and there is no documented dependency on the workflow record. The platform-deploy operation is rare (Bicep stacks are largely stable) and high-stakes — operator preference is to run scripts locally with full terminal output and Azure CLI context, not via opaque CI runner. The workflow is also entirely orthogonal to `deploy-infrastructure.yml` (which DOES auto-run on `infrastructure/bicep/**` changes; see Wave B fix) — that workflow covers automated Bicep what-if/deploy and is the canonical infra automation surface.

## Recommendation

**DELETE** per design.md D-03 (delete-by-default for never-used workflows). Burden of proof on retention has not been met: zero invocations in 30 days, last touched 2026-03-13, full functional equivalence available via local `Deploy-Platform.ps1` execution. Comment-out is explicitly forbidden by NFR-04; the workflow file must be removed via `git rm` with a commit message referencing this decision record. If a future project requires CI-based platform deployment (e.g., for audit-trail compliance in a regulated environment), the workflow can be recreated from git history (`git show 8fa46c60:.github/workflows/deploy-platform.yml`) or rebuilt with a clearer requirements basis.

## Execution plan (for task 021 or 022)

**Task 021 (deploy-* dispositions)** executes:

```bash
git rm .github/workflows/deploy-platform.yml
git commit -m "$(cat <<'EOF'
chore(workflows): delete deploy-platform.yml (D-05)

Zero invocations in last 30 days; functionality fully covered by
./scripts/Deploy-Platform.ps1 run locally per docs/procedures/production-release.md.

Per design.md D-03 (delete-by-default for never-used workflows) +
NFR-04 (git rm, not comment-out).

See projects/github-actions-rationalization-r1/decisions/D-05-disposition-deploy-platform.md
EOF
)"
```

No consolidation; clean delete. Net workflow count change: -1.

## Owner sign-off note

Recent contributors (last 90 days): NONE. The only commit (`8fa46c60`) is from 2026-03-13, >2 months ago. No "speak now" trigger window applies (D-03 explicitly handles this case as default-delete). The original author (spaarke-dev, same identity authoring deploy-bff-api + provision-customer in the same commit) has had ≥2 months without invoking the workflow — strong signal that it is not load-bearing.
