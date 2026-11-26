# Lessons Learned — acme-7a7cbda1-325e-4cad-a1a5-351bb866d414

> **MANDATORY** — written by `/provision-environment` Step 7. Drafted by Claude for the operator from the run record and
> the handoff report `runs/7a7cbda1-325e-4cad-a1a5-351bb866d414.md`.

- **Outcome**: Completed 2026-11-26 12:50:12 UTC (first end Failed 05:52:22 UTC at H13; resumed once)
- **Operator**: `ralph.schroeder@spaarke.com`
- **Customer / kind**: acme, Model 1 dedicated stamp — third UPGRADE run (no version change; H3 Demo-only client)

## What went right

- **The H3 Demo gate worked on the first live run**: the operator client `ec3c52c1-…` was added to the ACME BFF app's
  pre-authorized clients, beside the two platform clients (PR #3217).
- **Fast no-op upgrade**: H0–H14 in about 15 min; H6 took seconds with SpaarkeMaster already at 1.3.3.0.
- **Prerequisites 36/36**, including PRQ-C-03 now that PR #3187 fixed the recipe.
- **Upgrade admission without hand edits** (ISS-031 `upgradeOfProvisionedOn`), as on 85561c4a.

## What went wrong

### Lesson L01 — Cost Management throttling fails H13

- **Symptom**: H13 failed `h13-cost-query-infra-fault` on HTTP 429 from Azure Cost Management at 05:51:52Z.
- **Root cause**: the cost-envelope query is attempted once; a 429 is classified as an infrastructure fault with no
  bounded retry (H10 has one).
- **Fix applied**: none in code yet; the run was resumed with the owner's OK and H13 passed.
- **Landing spot**: ISS-048 #3222 (`ArmCostEnvelopeChecker` bounded retry honouring Retry-After).
- **Blocks future runs**: no (Resumable), but it costs a manual resume.
- **Punch-list class**: transient-fault handling.

## New prereqs to codify

None.

## New patterns to add

None.

## Recommendations for next run

- Until ISS-048 lands, expect an occasional H13 429 after back-to-back runs on one subscription; resume after a few
  minutes rather than starting a new run.

## Cross-run pattern

- First observed here (Cost Management 429). Occurrence count: 1.

## Sign-off

- Author: Claude (operator session), 2026-11-26
- Reviewer: pending (operator)
