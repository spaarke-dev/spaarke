---
name: dataverse-audit-defaults-2026-10-16
description: Dataverse org-level audit defaults, retention semantics, share/unshare action codes, Web API PATCH of organization audit columns
metadata:
  type: reference
---

## 2026-10-16: Dataverse auditing defaults and org-row settings
**Question**: New-env defaults of isauditenabled/isuseraccessauditenabled/auditretentionperiodv2; retention semantics; org-level prerequisite; share audit codes; PATCH via app user.
**Findings**: No Learn page documents a new-environment default for isauditenabled (training module says disabled by default; no evidence of a 2026 change to default-on). auditretentionperiodv2 dev doc: default 30, 1..365000, -1 = forever; PPAC says default Forever and custom max 24,855 days (conflict, null undocumented). Table IsAuditEnabled only writes when org auditing on (configure page). Share=14 (GrantAccess), Modify Share=48, Unshare=49, Associate=33/Disassociate=34, role-to-team 53/54, role-to-user 55/56; Org PATCH supported per organization-table page (SysAdmin/Customizer). Repo task-086 note: spaarkedev1 had isauditenabled True and auditretentionperiodv2 null.
**Sources**:
- https://learn.microsoft.com/en-us/power-apps/developer/data-platform/auditing/configure
- https://learn.microsoft.com/en-us/power-apps/developer/data-platform/auditing/retrieve-audit-data
- https://learn.microsoft.com/en-us/power-platform/admin/manage-dataverse-auditing
- https://learn.microsoft.com/en-us/power-apps/developer/data-platform/organization-table
**Open questions**: actual default on a fresh env (needs live probe); null retention meaning; app-user PATCH never live-tested here.
