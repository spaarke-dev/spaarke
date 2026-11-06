---
name: dataverse-duplicate-detection-api-default
description: Web API/SDK duplicate detection default is SUPPRESSED; only runs when MSCRM.SuppressDuplicateDetection=false / SuppressDuplicateDetection=false; error 0x80040333 (412 vs 500 doc conflict)
metadata:
  type: reference
---

## 2026-10-15: Duplicate detection on Web API / SDK create-update-upsert without the header
**Question**: Does Dataverse duplicate detection run/block for app-only Web API POST/PATCH/upsert and SDK CreateRequest when the suppress header/param is not sent?
**Findings**: Documented default = suppressed. create-entity-web-api: "By default, the system suppresses duplicate detection when you create records. To enable duplicate detection, include the `MSCRM.SuppressDuplicateDetection: false` header". manage-duplicate-detection-create-update: "By default, duplicate detection is suppressed when you are updating records using the Web API." With false + duplicate: error code `0x80040333` "A record was not created or updated because a duplicate of the current record already exists."; HTTP status doc-conflict: optional-parameters page and compose-http-requests table say 412 (DuplicateRecord); the manage-duplicate-detection page sample shows 500. SDK: error -2147220685 (0x80040333) FaultException<OrganizationServiceFault>; must use CreateRequest/UpdateRequest with Parameters["SuppressDuplicateDetection"]=false (IOrganizationService.Create/Update shortcuts cannot carry it). Detection requires all three: Organization.IsDuplicateDetectionEnabled, EntityMetadata.IsDuplicateDetectionEnabled, published rule (plus Organization.IsDuplicateDetectionEnabledForOnlineCreateUpdate for Create/UpdateRequest). Docs silent on: explicit SDK default value text (inferred suppressed), upsert/alternate-key (UpsertRequest/PATCH-by-key), application-user/S2S differences, CreateMultiple/UpdateMultiple. Training module and Plauti blog claim suppressed by default for SDK too and Multiple messages (blog only).
**Sources**:
- https://learn.microsoft.com/en-us/power-apps/developer/data-platform/webapi/create-entity-web-api#check-for-duplicate-records
- https://learn.microsoft.com/en-us/power-apps/developer/data-platform/webapi/manage-duplicate-detection-create-update
- https://learn.microsoft.com/en-us/power-apps/developer/data-platform/optional-parameters#suppress-duplicate-detection
- https://learn.microsoft.com/en-us/power-apps/developer/data-platform/enable-disable-duplicate-detection
- https://learn.microsoft.com/en-us/power-apps/developer/data-platform/org-service/detect-duplicate-data
- https://learn.microsoft.com/en-us/training/modules/audit-retain-dataverse-data/3-manage-duplicate-detection
**Open questions**: Live probe in dev org to confirm no-header POST/PATCH-by-alt-key/UpsertRequest are not blocked and to capture actual status with header=false (412 vs 500).
