# 079 — WP3 deploy to dev: evidence

## 2026-11-24: read-only preparation (079 is still gated on 053)

| Step | Result |
|---|---|
| 2 Schema verify (`-WhatIf`) | Procedure schema, Stored File schema, AddedFee/Discount options and rejection-reason schema: nothing to create, exit 0. `Deploy-EbillingRuleTypeOptions.ps1` stopped with an escalation (exit 2) on 100000015 AddedFee and 100000016 Discount. Task 115 added those two on purpose, but the script tolerated only 100000014. That is fixed in the same commit as this note: the script now tolerates all three when each carries its own label, and escalates on a wrong label. After the fix the `-WhatIf` run exits 0 (created 0, changed 0, skipped 11). |
| 4 Secure family (`-Verify`) | `Set-SecureRecordOwnerRolePrivileges.ps1 -Verify`: PASS (Read at Basic on all 40 tables). `Set-ChildRecordCreatorPersonSchema.ps1 -Verify` (with the dev BFF and ontology-writer application ids, as in `docs/procedures/dev-bff-deploy-coordination.md`): PASS. |
| 3 Storage (az reads only) | Already in place on dev, so live step (b) has nothing to set. `spaarke-bff-dev` carries `Finance__FileStore__BlobEndpoint`, `Finance__FileStore__TenantId` and `Finance__FileStore__MalwareScanningEnabled` (false, as 091 allows on dev). The account named by the endpoint holds the containers `procedures`, `invoice-files` and `billing-exports`. Each container has one assignment of the custom role "Spaarke eBilling File Store Blob Data" (091 option A), held by the BFF's identity. The BFF's identity has no account-scope grant on that account. Names only; no values recorded. |

Step 10 also carries the Rejected portal check moved from 053: an InvoiceAge rule with maxAgeDays 180 and consequence Reject, then evaluation-set sample B-0082.
