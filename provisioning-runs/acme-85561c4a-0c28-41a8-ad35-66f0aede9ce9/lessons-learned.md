# Lessons Learned — acme-85561c4a-0c28-41a8-ad35-66f0aede9ce9

> **MANDATORY** — written by `/provision-environment` Step 7. Drafted by Claude for the operator from the run record,
> `notes/defer-issues.md` (ISS-044, ISS-045) and the handoff report `runs/85561c4a-0c28-41a8-ad35-66f0aede9ce9.md`.

- **Outcome**: Completed 2026-11-25 23:52:02 UTC (started 22:32:07 UTC; 1 h 20 min wall clock)
- **Operator**: `ralph.schroeder@spaarke.com`
- **Customer / kind**: acme, Model 1 dedicated stamp — second UPGRADE run (SpaarkeMaster managed 1.3.2.1 → 1.3.3.0;
  BFF pinned to `2026.10.16-2`, the build it already ran)

## What went right

- **No stop, no retry, no intervention**: 22 handlers, 0 retries, all 23 gates Verified. The previous upgrade
  (3b6f7b65) took 2 d 19 h and five fixes; every one of those fixes held:
  - ISS-031 `upgradeOfProvisionedOn`: admitted against the Ready row, no hand reset.
  - ISS-036 `buildId`: in the schema and checked against `latest.json`.
  - ISS-032: the H6 import took ~55 min, inside the 120-min limit.
  - PR #2956 slot swap: H9 swapped first time.
  - ISS-034: H13 passed first time despite post-swap CPU load.
  - ISS-035: no stale `Pending` failure gates on the completed run.
- **H12b seeded the new product data**: 37 reference rows (ISS-039), 87 `legal-standard` taxonomy rows and the
  `sprk_communication` record type — no hand seeding on the stamp.
- **Role expectations match 1.3.3.0**: `Test-RolePrivilegeExpectations.ps1` all 8 roles OK, including ISS-042/ISS-043.
- **The ebilling pre-upgrade gate ran**: `Remove-EbillingRateCardCoreLookups.ps1` report mode said READY before H6.

## What went wrong

### Lesson L01 — PRQ-C-03 recipe fails on Windows

- **Symptom**: PRQ-C-03 (name ownership) failed with curl exit 23 for every name.
- **Root cause**: `curl -o /dev/null` under `MSYS_NO_PATHCONV=1` (SKILL Step 0.5b) passes `/dev/null` to the native
  `curl.exe` as a literal path it cannot write.
- **Fix applied**: yes — ISS-044 #3183, PR #3187 (recipe reads the status line from `curl -D -`; `validate.ps1` lint f
  refuses a `/dev/null` argument). Re-run live against ACME: OWNED ×4, exit 0. Merge pending.
- **Landing spot**: `scripts/provisioning-prereqs/prereqs.yaml`, `scripts/provisioning-prereqs/validate.ps1`.
- **Blocks future runs**: until #3187 merges, the operator must confirm the names by hand.
- **Punch-list class**: A

### Lesson L02 — Dataverse MCP disconnected for the whole run

- **Symptom**: the MCP server would not connect.
- **Root cause**: client-side connection state, not Dataverse.
- **Fix applied**: none needed — `mcpDisconnectPolicy = proceedWithFallback` and the registry read and checks used the
  Web API (Fallback F1). H13 wrote the registry itself, so Step 6a had nothing to patch.
- **Landing spot**: none.
- **Blocks future runs**: no.
- **Punch-list class**: C

### Lesson L03 — Staging slot unavailable after the swap

- **Symptom**: production `/healthz` 200; staging slot 503 / timeout for at least 25 min after the 23:49 swap. The S1
  plan ran 80–99 % CPU 23:50–00:15.
- **Root cause**: both slots share one S1 plan; the post-swap warm-up saturates it (same as 3b6f7b65 U-L08).
- **Fix applied**: no — open owner decision on stamp plan sizing.
- **Landing spot**: stamp Bicep App Service plan SKU, or a post-swap scale-down of the staging slot.
- **Blocks future runs**: no (production unaffected; H13 now tolerates it).
- **Punch-list class**: C

### Lesson L04 — The project worktree's skill and schema are stale

- **Symptom**: the project worktree is ~1030 commits behind master; its skill and `intake.schema.json` predate
  `buildId` and `upgradeOfProvisionedOn`.
- **Root cause**: the project branch has not merged master since those landed.
- **Fix applied**: ran from a temp worktree at master (`wtcp2`, `af4cfb4527`).
- **Landing spot**: operator practice — run `/provision-environment` from master.
- **Blocks future runs**: no, if the operator runs from master.
- **Punch-list class**: B

## New prereqs to codify

- None. PRQ-C-03 is corrected by #3187.

## Recommendations for next run

- Run from master. Pin `buildId` when the BFF must not change.
- Owner decision: stamp plan sizing.
- Step 6e probe items (2b, 6, 7, 8, 13) need owner OK.
- ISS-045 #3189: stamps lack 17 grid configurations, 2 workspace layouts and 1 playbook that product code finds by dev id.

## Cross-run pattern

- Post-swap S1 saturation: runs 3b6f7b65 and 85561c4a. Evidence for the plan-sizing decision.

## Sign-off

- Author: Claude (drafted for the operator)
- Reviewer: pending — ralph.schroeder@spaarke.com
- git-sha: af4cfb4527 (master used by the run)
