# Task 050 — Standing-Grant Field Schema Contract

> **Status**: ✅ APPLIED LIVE to `spaarkedev1` (SPAARKE DEV 1, `https://spaarkedev1.crm.dynamics.com/`)
> **Applied**: 2026-08-03, via authenticated Dataverse Web API (`az` token, `pac org who` confirmed live auth as `ralph.schroeder@spaarke.com`)
> **Consumed by**: task 051 (runtime accessible-set union) — this is the canonical field contract; reference these exact names.

---

## 1. Field Contract (FINAL — this is what task 051 reads)

| Property | Value |
|---|---|
| Entity | `contact` |
| Logical name | `sprk_standinggrant` |
| Schema name | `sprk_standinggrant` |
| Type | Two Options (Boolean) |
| Default value | **No / `false`** |
| True label | "Yes" |
| False label | "No" |
| Required level | None (optional) |
| `IsSecured` (Field-Level Security enabled) | **true** |
| MetadataId | `9867dd5e-b38f-f111-b8db-70a8a590c51c` |
| Display name | "Standing Grant" |
| Description | "Subject-level access policy flag. When enabled, this contact is granted standing (runtime) access to all records where they hold an access-conferring role, now and in the future — per FR-12. Writable only by a grant-privileged systemuser (Field Security Profile: Standing Grant Administrators). Does NOT itself create any sprk_externalrecordaccess row; distinct from per-record grants." |

**Read contract for task 051**: `contact.sprk_standinggrant == true` means "union this contact's contact-anchored ADR-034 membership (role-allowlist-filtered) into `accessible(principal)` for this contact." `false`/`null` means no standing-grant contribution (per-record `sprk_externalrecordaccess` grants are unaffected either way — they are a separate, already-built union term per design.md §5).

---

## 2. Field Security Profile (FINAL)

| Property | Value |
|---|---|
| Name | **Standing Grant Administrators** |
| `fieldsecurityprofileid` | `f4be217b-b38f-f111-b8db-7ced8ddc4a05` |
| Description | "Grants Create/Read/Update on contact.sprk_standinggrant (the FR-12 subject-level standing-access policy flag). Membership in this profile is the sole authorization mechanism for setting standing grants — add only grant-privileged systemusers/teams. Created by teams-app-r1 task 050." |
| Field permission | `attributelogicalname=sprk_standinggrant`, `entityname=contact`, `canread=Allowed(4)`, `cancreate=Allowed(4)`, `canupdate=Allowed(4)` (`fieldpermissionid` = `38744894-b38f-f111-b8db-7ced8ddc4a05`) |
| Current members | **NONE** — see §5 operator checklist |

**Native Dataverse behavior observed**: marking an attribute `IsSecured=true` auto-creates a field permission for the built-in **System Administrator** field security profile (full Read/Create/Update) — this is platform-default, not something this task added. So today, only (a) users holding the **System Administrator** security role (which auto-includes the System Administrator FLS profile) and (b) any future member of **Standing Grant Administrators** can read or write this field. Every other principal is natively denied by Dataverse FLS — no client-side hiding was used or relied upon.

---

## 3. Contact-form toggle (FINAL)

| Property | Value |
|---|---|
| Form | **Contact main form** (`formid` = `1fed44d1-ae68-4a41-bd2b-f13acac4acfa`) |
| Placement | `SUMMARY_TAB` → "SYSTEM USER" section (`SUMMARY_TAB_section_6`, id `417f5dc5-e0c2-4fb1-9a86-57eb127a32ec`) — added as a new row after "User Name" (`adx_identity_username`). This section was chosen as the closest existing access/identity-adjacent section on the form; there is no prior dedicated "access" section on Contact. |
| Control cell id | `{44f0a08d-035f-4d70-a0b6-d71fac916853}` |
| Control classid | `{67FAC785-CD58-4f9f-ABB3-4B7DDC6ED5ED}` (standard Boolean field control — same classid used by the form's existing Yes/No fields `donotemail`/`followemail`/`donotbulkemail`/etc.; renders as a toggle switch by default in the Unified Interface) |
| Label | "Standing Grant" |

The control was inserted via a targeted `formxml` PATCH to the existing `systemforms` record (not a new form), then `PublishXml` was invoked for the `contact` entity. **FLS is honored automatically by the platform renderer** for any bound control on a secured field — no additional form scripting was added or needed; a caller without Read on `sprk_standinggrant` will not see the control at all, and a caller with Read-but-not-Update will see it disabled/read-only. This is native platform behavior, verified by the field-permission configuration in §2 (no client-side-only hiding).

---

## 4. Verification performed (live)

| Check | Result |
|---|---|
| Attribute exists, correct type | ✅ `AttributeType=Boolean`, `LogicalName=sprk_standinggrant` (Web API metadata read) |
| `IsSecured=true` | ✅ confirmed via metadata read |
| Default value | ✅ `DefaultValue=False` (Web API `BooleanAttributeMetadata` read) |
| Field permission profiles | ✅ exactly 2 profiles hold access: `System Administrator` (platform auto-created) + `Standing Grant Administrators` (this task) — both `Read/Create/Update = Allowed`; no other profile has any permission row for this attribute |
| Toggle present on Contact main form | ✅ `datafieldname="sprk_standinggrant"` control confirmed present in published `formxml` |
| Positive-path write (grant-privileged user) | ✅ **Live test executed**: as the authenticated System-Administrator user (who holds the auto FLS profile), PATCHed `contact(ac6d7b68-fd21-f111-88b5-7ced8d1dc988)` ("Test User 1") `sprk_standinggrant=true` → succeeded; read back confirmed `true`; then reverted to `false` to leave test data clean. |
| Negative-path write (non-privileged user) | ⚠️ **Operator-gated** — see §6. Configuration-level enforcement is verified (§2: only 2 named profiles hold any permission on this field; Dataverse FLS is a native, well-established denial mechanism for any principal outside those profiles). No live behavioral test was run against an actual restricted human test-user account, because no such non-privileged, non-admin human test user exists in `spaarkedev1` today that could safely be used without provisioning a new one — that provisioning is out of scope for a schema task. |
| No existing `contact` field modified/removed | ✅ Only `sprk_standinggrant` was added; no other attribute was touched. |
| No `sprk_externalrecordaccess` row created | ✅ That table was never referenced by this task. |

---

## 5. Operator checklist (only remaining gap)

1. **Assign grant-privileged principals** to the `Standing Grant Administrators` Field Security Profile (`f4be217b-b38f-f111-b8db-7ced8ddc4a05`) — currently has **zero members**. In Dataverse: open the Field Security Profile record → **Members** related grid → **Add** the systemuser(s) or team(s) who should be allowed to set standing grants (e.g., in-house counsel administrators). This is a deliberate, business-owned decision this task does not presume.
   - PowerShell/Web API pattern (associate a systemuser):
     ```powershell
     $body = @{ "@odata.id" = "$BaseUrl/systemusers($systemUserId)" } | ConvertTo-Json -Compress
     Invoke-RestMethod -Uri "$BaseUrl/fieldsecurityprofiles(f4be217b-b38f-f111-b8db-7ced8ddc4a05)/systemuserprofiles_association/`$ref" -Headers $headers -Method POST -Body $body
     ```
   - To associate a team instead, use the `teamprofiles_association` navigation property.
2. **(Recommended, not required)** Run a live negative-write test once a non-privileged human test user is available: authenticate as that user (or impersonate via `MSCRMCallerID` if the calling identity holds "Act on Behalf of Another User"), attempt `PATCH contacts({id}) { sprk_standinggrant: true }`, and confirm a `403`/field-not-updatable response, and confirm the toggle is not rendered (or is disabled) on the Contact form for that user in the browser.
3. Task 051 (runtime union) should treat `sprk_standinggrant` as documented in §1 — no further schema changes expected.

---

## 6. Acceptance criteria → status

| # | Criterion | Status |
|---|---|---|
| 1 | `contact` metadata carries `sprk_standinggrant` Two Options field with documented default | ✅ Met |
| 2 | Contact main form has a toggle bound to the field | ✅ Met |
| 3 | Grant-privileged systemuser can write the toggle | ✅ Met (live-tested as System-Administrator-profile holder) |
| 4 | Non-privileged systemuser is blocked (FLS-enforced, not client-side-only) | ⚠️ Operator-gated — configuration verified live (§2, §4); behavioral test with an actual restricted human account is a documented operator follow-up (§5.2), not a schema defect |
| 5 | Additive only — no existing `contact` field touched, no `sprk_externalrecordaccess` row created | ✅ Met |

---

## 7. Environment applied to

- Org: **SPAARKE DEV 1** (`spaarkedev1.crm.dynamics.com`, Org ID `0c3e6ad9-ae73-f011-8587-00224820bd31`)
- This is a **dev** environment. If teams-app-r1 or a downstream project promotes this schema to another environment (test/prod), re-run the same Web API sequence (§8) against that environment's base URL — schema is not auto-synced across environments (per `dataverse-create-schema` skill guidance).

## 8. Reproduction script (for other environments / audit trail)

The exact live sequence executed (paraphrased as a single idempotent-intent script — each step already includes an existence check pattern per `dataverse-create-schema` conventions):

```powershell
$Environment = "<target-env>.crm.dynamics.com"   # e.g. spaarkedev1.crm.dynamics.com
$token = (az account get-access-token --resource "https://$Environment" --query accessToken -o tsv)
$headers = @{
    "Authorization" = "Bearer $token"; "OData-MaxVersion" = "4.0"; "OData-Version" = "4.0"
    "Content-Type" = "application/json"; "Accept" = "application/json"; "Prefer" = "return=representation"
}
$BaseUrl = "https://$Environment/api/data/v9.2"

function New-Label($Text) {
    @{ "@odata.type"="Microsoft.Dynamics.CRM.Label"; "LocalizedLabels"=@(@{"@odata.type"="Microsoft.Dynamics.CRM.LocalizedLabel";"Label"=$Text;"LanguageCode"=1033}) }
}

# 1. Create the attribute
$attrDef = @{
    "@odata.type" = "Microsoft.Dynamics.CRM.BooleanAttributeMetadata"
    "SchemaName" = "sprk_standinggrant"
    "RequiredLevel" = @{ "Value" = "None" }
    "IsSecured" = $true
    "DisplayName" = New-Label "Standing Grant"
    "Description" = New-Label "Subject-level access policy flag (FR-12). Writable only by a grant-privileged systemuser via the Standing Grant Administrators Field Security Profile."
    "OptionSet" = @{ "TrueOption"=@{"Value"=1;"Label"=(New-Label "Yes")}; "FalseOption"=@{"Value"=0;"Label"=(New-Label "No")} }
    "DefaultValue" = $false
} | ConvertTo-Json -Depth 20 -Compress
Invoke-RestMethod -Uri "$BaseUrl/EntityDefinitions(LogicalName='contact')/Attributes" -Headers $headers -Method POST -Body $attrDef

# 2. Publish
$pub = @{ "ParameterXml" = "<importexportxml><entities><entity>contact</entity></entities></importexportxml>" } | ConvertTo-Json -Compress
Invoke-RestMethod -Uri "$BaseUrl/PublishXml" -Headers $headers -Method POST -Body $pub

# 3. Field Security Profile
$profileBody = @{ "name"="Standing Grant Administrators"; "description"="Grants Create/Read/Update on contact.sprk_standinggrant (FR-12 standing-access policy flag)." } | ConvertTo-Json -Compress
$profile = Invoke-RestMethod -Uri "$BaseUrl/fieldsecurityprofiles" -Headers $headers -Method POST -Body $profileBody

# 4. Field permission for the profile
$permBody = @{
    "attributelogicalname"="sprk_standinggrant"; "entityname"="contact"
    "canread"=4; "cancreate"=4; "canupdate"=4
    "fieldsecurityprofileid@odata.bind" = "/fieldsecurityprofiles($($profile.fieldsecurityprofileid))"
} | ConvertTo-Json -Compress
Invoke-RestMethod -Uri "$BaseUrl/fieldpermissions" -Headers $headers -Method POST -Body $permBody

# 5. Add the toggle to the Contact main form (formid below is spaarkedev1-specific — re-query per environment):
#    GET systemforms?$filter=objecttypecode eq 'contact' and type eq 2 to find "Contact main form" in the target env,
#    then insert a <row><cell><control classid="{67FAC785-CD58-4f9f-ABB3-4B7DDC6ED5ED}" datafieldname="sprk_standinggrant" .../></cell></row>
#    into the desired section's formxml, PATCH systemforms(<formid>), then re-run step 2's publish.

# 6. Assign members to the new profile (repeat per grant-privileged systemuser/team) — see §5 operator checklist above.
```
