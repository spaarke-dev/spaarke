# deploy-bff-api.yml — Master Trigger Audit

> **Task**: CICD-044 Deliverable 1
> **Date**: 2026-06-26
> **Auditor**: Claude (autonomous task execution)
> **Spec reference**: FR-C05 (audit confirms `deploy-bff-api.yml` triggers from master; fix if not)

---

## Result: PASS — No fix required.

The workflow at `.github/workflows/deploy-bff-api.yml` triggers correctly per spec FR-C05.

## Evidence (lines 20–35)

```yaml
on:
  push:
    branches:
      - master
    paths:
      - 'src/server/api/**'
  workflow_dispatch:
    inputs:
      environment:
        description: 'Target environment'
        required: true
        type: choice
        options:
          - dev
          - production
        default: 'production'
```

## Audit checklist

| Check | Expected | Actual | Pass |
|---|---|---|---|
| Push trigger | `master` only | `master` only | YES |
| Path filter | `src/server/api/**` | `src/server/api/**` | YES |
| Manual dispatch | Present with env input | Present (dev/production) | YES |
| No `pull_request` trigger on deploy | Absent | Absent | YES |
| No wildcard branch trigger | Absent | Absent | YES |
| Concurrency group | Prevents parallel deploys | `deploy-bff-api-${env}` with `cancel-in-progress: false` | YES |

## Pipeline shape (no changes recommended)

Build (parallel with Test) → Deploy staging slot → Verify staging /healthz → Swap to production (gated by `environment: production`) → Verify production /healthz → Rollback on failure (auto swap-back). The pattern is the canonical reference being mirrored by Deliverable 2 (`deploy-spaarke-ai.yml`).

## Conclusion

**No code change required.** Master-only push trigger is confirmed, `workflow_dispatch` honors environment choice with `production` default, and the zero-downtime slot-swap + auto-rollback pattern is intact. The hot-path spec rule "deploy-bff-api.yml trigger audit only" (no functional changes) is satisfied by this audit document; the workflow is untouched.

## SC-09 contribution

This audit closes the `deploy-bff-api.yml confirmed master-triggered` half of SC-09. The other half (`deploy-spaarke-ai.yml exists with ≥1 successful CD-from-master deploy`) is delivered by the new workflow authored alongside this audit.
