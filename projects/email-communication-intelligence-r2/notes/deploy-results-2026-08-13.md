# R2 Deploy Results — 2026-08-13 (DEV)

Target environment: **dev** (`spaarkedev1` Dataverse + `spaarke-bff-dev` App Service). Operator-confirmed dev, not production.

## Shipped

| Task(s) | Surface | Mechanism | Result |
|---|---|---|---|
| CI hygiene A/B/C | sdap-ci.yml LFS + external-access test drift + PCF lint | commit `fb1db3b23`, merged via PR #755 | Tier-1 blocking CI green; Format/Lint/ADR/Arch all green |
| — | Merge PR #755 → master | `gh pr merge --merge` | master `f9a1e0eb9`; main repo synced |
| 017/026/035/045 | **BFF** (all pillars' backend, one app) | `/bff-deploy` → `scripts/Deploy-BffApi.ps1` → `spaarke-bff-dev` | 48.48 MB, **4/4 critical files SHA-256 verified**, healthz 200; new `POST /api/ai/chat/sessions/{id}/documents/from-document` returns 401 (registered); `eml-render` 401 |
| 059 (widget half) | **SpaarkeAi reconciliation widget** | `Deploy SpaarkeAi` workflow (auto on merge) → Dataverse dev | completed/success (prod step skipped by design) |
| 059 (seed) | 2× `sprk_gridconfiguration` | `scripts/seed-reconciliation-gridconfig.ps1` (idempotent) | needs-review = `00000000-0000-4000-8000-000000005001` (== `NEEDS_REVIEW_CONFIG_ID`, no code change); per-team = `d68c8b50-ca96-f111-b8dc-7ced8ddc4a05` |
| 059 (code page) | **`sprk_communicationreconciliation`** web resource | `/code-page-deploy` → Vite build (surface type-errors 0) → `Deploy-WebResourceInline.ps1` | CREATED `1e191e05-cc96-f111-b8dc-7ced8ddc4a05` + published; bundle verified to contain config id + ReconciliationWorkspace |

## Deliberately NOT run

- **`Deploy-AllDataGridConsumers`** — SKIPPED. Verified the R2 PR did **not** modify the shared DataGrid framework (`Spaarke.UI.Components/src/components/DataGrid/**`); it only adds the new ReconciliationGrid consumer. There is no additive framework change for existing consumers to pick up, so redeploying all ~18 consumers would be pure risk (potential clobber of `dataset-grid-framework-r2`'s out-of-band deploys) for zero benefit. Task 059's step-4 assumption (task 050 changed the framework) does not hold in the as-built R2.
- **Production deploys** — not this cycle (dev UAT). `spaarke-bff-prod` is Stopped; `deploy-bff-api.yml` is `disabled_manually`.

## Remaining / operator-gated

- **044 add-in (Azure SWA)** — the R2 PR had **0** `src/client/office-addins/**` changes, so the merge did not trigger `deploy-office-addins`. Task 040 flagged runtime NAA sign-in + dark-mode live-render as **operator-gated (needs a live Office host)**. Dispatch `deploy-office-addins.yml` only when doing the live Office UAT.
- **SDAP CI (informational)** — Class A LFS fix is on master; the `build-test` job should now go green (mirrors the already-passing `compose-fidelity-gate` job). Non-blocking (no branch protection; workflow is `continue-on-error`).
- **UAT** then **090 wrap-up**.
