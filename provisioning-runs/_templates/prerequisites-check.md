# Prerequisites Check — {customerId}-{runId}

> **Created by**: `customer-provisioning-orchestration-r1` task 203a per punch list row A06 (template).
> **Source-of-truth**: [`docs/guides/PROVISIONING-PREREQUISITES.md`](../../docs/guides/PROVISIONING-PREREQUISITES.md) + machine-parseable [`scripts/provisioning-prereqs/prereqs.yaml`](../../scripts/provisioning-prereqs/prereqs.yaml).
> Written by `/provision-environment` skill Step 0.5. **Any FAIL row HARD STOPs the run before Step 2 preflight.**

## Filter

Filtered by `scope` and `tenancyModel = {tenancyModel}` for this run.

## Results

| prereq_id | name | scope | status | evidence | remediation-if-failed |
|-----------|------|-------|--------|----------|-----------------------|
| — | — | — | ⬜ pending | — | — |

Example rows:

| PRQ-S-03 | Resource-provider registration | once_per_subscription | ✅ OK | 12 of 12 namespaces `Registered` (`az provider show` all pass) | — |
| PRQ-T-04 | SPE container-type published | once_per_tenant | ✅ OK | `Get-SPEContainerType` returns `spaarke-canonical-v1` | — |
| PRQ-C-01 | OpenAI TPM headroom (frontier tiers) | once_per_customer | ❌ FAIL | `gpt-5.4 GlobalStandard: limit 0, current 0` | Support ticket via `az support tickets create` (PRQ-S-02 present ✓); ETA 2-5 business days |

## Summary

- Total prereqs checked: {N}
- ✅ Pass: {P}
- ❌ Fail: {F}
- ⚠️ Warning: {W} (advisory — does not block)
- Overall: `{PASS_ALL | FAIL — {N_FAILED_IDS}}`

If any FAIL: STOP — handler-log.md remains empty; run enters `PrereqsFailed` state; operator remediates per remediation column; re-run Step 0.5 before advancing.
