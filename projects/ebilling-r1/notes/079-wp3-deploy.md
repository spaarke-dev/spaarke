# 079 — WP3 deploy to dev: evidence

## 2026-11-24: read-only preparation (079 is still gated on 053)

| Step | Result |
|---|---|
| 2 Schema verify (`-WhatIf`) | Procedure schema, Stored File schema, AddedFee/Discount options and rejection-reason schema: nothing to create, exit 0. `Deploy-EbillingRuleTypeOptions.ps1` stopped with an escalation (exit 2) on 100000015 AddedFee and 100000016 Discount. Task 115 added those two on purpose, but the script tolerated only 100000014. That is fixed in the same commit as this note: the script now tolerates all three when each carries its own label, and escalates on a wrong label. After the fix the `-WhatIf` run exits 0 (created 0, changed 0, skipped 11). |
| 4 Secure family (`-Verify`) | `Set-SecureRecordOwnerRolePrivileges.ps1 -Verify`: PASS (Read at Basic on all 40 tables). `Set-ChildRecordCreatorPersonSchema.ps1 -Verify` (with the dev BFF and ontology-writer application ids, as in `docs/procedures/dev-bff-deploy-coordination.md`): PASS. |
| 3 Storage | Not run yet. |

Step 10 also carries the Rejected portal check moved from 053: an InvoiceAge rule with maxAgeDays 180 and consequence Reject, then evaluation-set sample B-0082.
