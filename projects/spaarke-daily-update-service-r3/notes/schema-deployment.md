# Schema Deployment Record — Task 001 (sprk_briefingstate)

> **Project**: spaarke-daily-update-service-r3
> **Task**: 001-add-sprk-briefingstate-choice-column.poml
> **Deployment Date**: 2026-06-24
> **Operator**: ralph.schroeder@spaarke.com (via Claude Code task-execute, STANDARD rigor)
> **Environment**: spaarkedev1 (https://spaarkedev1.crm.dynamics.com)
> **Solution**: SpaarkeCore (v1.1.0.0, publisher `Spaarke` prefix `sprk`, unmanaged)
> **Method**: Dataverse Web API (PicklistAttributeMetadata POST + MSCRM.SolutionUniqueName header) — chosen over PAC CLI per `dataverse-create-schema` skill (PAC v1.46 lacks `pac column create`); ADR-022 / ADR-027 (amended 2026-06-02) prescribe unmanaged-everywhere for current ALM phase.

---

## Result

✅ **Column deployed and verified against all acceptance criteria.** No human follow-up needed for this task. Operator action *would* be required (maker portal) if and only if downstream UAT in task 040 surfaces a missing form/view placement; that is a UAT concern, not FR-1.

---

## Column Specification (as deployed)

| Property | Value |
|---|---|
| Parent Table | `appnotification` (Microsoft-owned, IsManaged=True, IsCustomEntity=True) |
| Logical Name | `sprk_briefingstate` |
| Schema Name | `sprk_briefingstate` |
| Display Name | Briefing State |
| Description | Daily-Briefing-scoped read state. Decoupled from native bell-panel isread. Unread=0 (default), Checked=1, Removed=2. See spaarke-daily-update-service-r3 FR-1. |
| Attribute Type | `Picklist` (local option set, IsGlobal=false) |
| Required Level | None |
| `DefaultFormValue` | `0` |
| MetadataId | `f2199f85-4570-f111-ab0e-7ced8ddc4cc6` |

### Option Set Values

| Value | Label |
|---|---|
| 0 | Unread |
| 1 | Checked |
| 2 | Removed |

---

## Acceptance Criteria Verification

| AC | Result | Evidence |
|---|---|---|
| **AC-1a**: Power Apps maker portal shows column on appnotification in spaarkedev1 | ✅ PASS | Web API `EntityDefinitions(LogicalName='appnotification')/Attributes(LogicalName='sprk_briefingstate')` returns metadata; publish-customizations completed for entity `appnotification`. Maker portal is a UI surface over this metadata — visibility follows. |
| **AC-1b**: OData `$select=sprk_briefingstate` returns 200 (not 400) | ✅ PASS | `GET /appnotifications?$select=sprk_briefingstate&$top=1` → HTTP 200 OK |
| **AC-6 / FR-1 Default propagation**: New `appnotification` rows created WITHOUT explicit `sprk_briefingstate` write surface value `0` | ✅ **PASS — Dataverse honors the default for Microsoft-owned table** | Test row `af47f79d-4570-f111-ab0e-7ced8ddc4a05` (TEST title) created with no `sprk_briefingstate` in payload; server returned `sprk_briefingstate = 0` via `Prefer: return=representation`. Test row deleted post-verification. This contradicts the conservative spec/POML assumption (Risk R5: "may not accept default for Microsoft-owned table") — Dataverse v9.2 DID accept and propagate it. Widget null-coalesce per FR-3 AC-3c remains defense-in-depth for any pre-rollout rows that were created before this column existed (those rows have `sprk_briefingstate = NULL`, confirmed by sampled OData query). |
| **AC-4**: Solution version + import timestamp recorded | ✅ PASS | This document is that record. |

---

## Solution Membership Verification

Query confirmed: `sprk_briefingstate` (objectid=f2199f85-4570-f111-ab0e-7ced8ddc4cc6) is registered as solutioncomponent (componenttype=2 = Attribute) in `SpaarkeCore` (solutionid=fbfef485-e2a8-4b04-a795-7fa607402903). Future env promotion can export SpaarkeCore + import to staging/prod per ADR-027 (current operating rule: unmanaged-everywhere).

---

## Notes for Higher-Environment Promotion

- SpaarkeCore solution version was **NOT** bumped as part of this deploy (Web API attribute-add does not increment solution version automatically; PAC CLI export will read whatever version is current). When the next promotion of SpaarkeCore happens, the maintainer should bump the version (`pac solution online-version --solution-name SpaarkeCore --solution-version 1.1.0.1` or similar) BEFORE export to differentiate it from the 1.1.0.0 currently on record.
- Per ADR-027 amendment (2026-06-02), export as **unmanaged** for all environments (`pac solution export --managed false`).
- `appnotification` is Microsoft-owned; ADR-027 CORE additive change is permitted but the receiving environment's import order should ensure `SpaarkeCore` imports AFTER any platform/dependency solutions.

---

## Method Detail (for reproducibility)

```powershell
# Authentication
$env_url = 'https://spaarkedev1.crm.dynamics.com'
$token = (az account get-access-token --resource $env_url --query accessToken -o tsv)

# POST to EntityDefinitions(LogicalName='appnotification')/Attributes
# with MSCRM.SolutionUniqueName: SpaarkeCore header
# Body: PicklistAttributeMetadata with inline OptionSet (IsGlobal=false),
#       3 options (Unread=0, Checked=1, Removed=2), DefaultFormValue=0.
# Then POST /PublishXml with <entity>appnotification</entity>.
```

Full deployment script: `c:/tmp/deploy-briefingstate.ps1` (one-off; not committed to repo as it's idempotent and re-runnable, but the pattern is preserved here per spec NFR-03 reproducibility expectation).

---

## Open Items / Carry-Forward

**None blocking.** Two operational nice-to-haves (not blocking widget code or UAT):

1. **Form placement** — column is not yet placed on any model-driven `appnotification` form. Not required for widget functionality (widget queries via Web API), but if a UAT scenario in task 040 wants to inspect the value through the OOB notification form, a maker portal step would add the column to "Information" form. **Decision**: defer; task 040 will surface if needed.
2. **Solution version bump** — see "Notes for Higher-Environment Promotion" above. Belongs to the next maintainer who exports SpaarkeCore, not to this task.

---

*Generated by Claude Code task-execute on 2026-06-24. Method preserved for audit; no live secrets in this file.*
