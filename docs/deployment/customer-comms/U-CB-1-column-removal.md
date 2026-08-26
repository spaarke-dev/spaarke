# U-CB-1 — Column Removal or Type Change (Customer Communication Template)

> **Purpose**: Plain-text operator-facing template to notify a customer that an upcoming Spaarke solution upgrade will remove a column (or coerce a column's type) on a `sprk_*` Dataverse entity.
> **Applies when**: `--allow-destructive` flag is set for a `Deploy-DataverseSolutions.ps1` upgrade AND the target solution's changeset includes a column drop or breaking type change on a `sprk_*` entity.
> **Owner**: Spaarke Platform Operations (release manager for the affected environment).
> **Delivery format**: Plain-text markdown — copy into the operator's chosen channel (email / customer portal / Slack / etc.). No HTML, no branded styling, no attached logos. Operator adapts wording per channel norms.
> **Related**: `../version-compatibility-matrix.md` · `../../guides/SPAARKE-CUSTOMER-DEPLOYMENT-GUIDE.md` (Upgrade — U-CB flow) · `projects/customer-provisioning-orchestration-r1/design.md` §14A.4 U-CB-1

---

## 1. Summary

Spaarke is preparing a solution upgrade for your environment (`{customerName}` / environment `{environmentName}`) that **removes or changes the type of one or more columns** on the following `sprk_*` entity/entities:

- Entity: `{entityLogicalName}` — Column: `{columnLogicalName}` — Change: `{"removed" | "type changed from {oldType} to {newType}"}`
- (repeat per affected column)

**This is a breaking change (U-CB-1)**. Data currently held in the affected column(s) will be either **permanently deleted** (removal) or **coerced with possible precision/format loss** (type change). Dataverse permits the change but does not preserve the prior data outside of the pre-migration export Spaarke will take on your behalf.

## 2. Trigger conditions (why you are receiving this)

You are receiving this notice because ALL of the following are true for the upcoming release `{targetSolutionVersion}`:

- The upgrade includes a `sprk_*` column drop OR breaking type change (U-CB-1 class per Spaarke design §14A.4).
- Spaarke's release procedure requires the operator to invoke `Deploy-DataverseSolutions.ps1` with the `--allow-destructive` flag; the flag is NEVER set silently.
- Your environment's Setup Status is currently `Ready` and eligible for the upgrade wave.

## 3. Customer impact

- **Data**: Records or field values in `{entityLogicalName}.{columnLogicalName}` will no longer be accessible after upgrade. If the column is used in customer-authored views, personal views, Power Automate flows, Power BI reports, or custom code — those references will fail or return null.
- **UX**: End-user forms that displayed the column will render without it. If the column was required, dependent forms may need re-publishing.
- **Integrations**: Any external system reading `{entityLogicalName}.{columnLogicalName}` via the Dataverse Web API will need to be updated (typically returns a 404 field-not-found after upgrade).
- **Reversibility**: The change is NOT reversible in-place after upgrade. Recovery requires restoring from the pre-migration export (see §7 Rollback) into a new column or a rolled-back solution version.

## 4. Timeline

| Milestone | Target date/time (all times {timezone}) |
|---|---|
| This notice sent | `{noticeSentDate}` |
| Pre-migration data export delivered to `{customerContact}` for review | `{exportDeliveryDate}` |
| Customer sign-off deadline (see §5) | `{signoffDeadline}` |
| Maintenance window START | `{windowStart}` |
| Upgrade apply (`--allow-destructive` invoked) | `{applyDate}` |
| Maintenance window END | `{windowEnd}` |
| Post-upgrade validation report delivered | `{validationReportDate}` |

Expected downtime: `{expectedDowntime}` (BFF slot-swap window plus solution-import elapsed time). End-user visible URL is uninterrupted; in-flight requests may retry.

## 5. Required customer action

Before Spaarke can proceed with the destructive apply, the customer MUST:

1. **Review the pre-migration data export**. Spaarke will deliver a CSV/JSON export of all rows in `{entityLogicalName}` containing the affected column(s) to `{customerContact}` by `{exportDeliveryDate}`. Confirm the export is complete and accessible.
2. **Inventory downstream dependencies** on `{entityLogicalName}.{columnLogicalName}`: personal views, dashboards, flows, Power BI reports, custom code, external integrations. Notify Spaarke of any dependencies you want to preserve (Spaarke can rewrite most Dataverse-native artifacts as part of the upgrade).
3. **Provide explicit sign-off** authorizing the destructive change (see §6 Confirmation of receipt). Absent sign-off by `{signoffDeadline}`, Spaarke will **defer the upgrade** and re-schedule.

## 6. Confirmation of receipt (required)

Please reply to `{operatorEmail}` (or acknowledge in `{acknowledgementChannel}`) with the following exact confirmation text before `{signoffDeadline}`:

> "`{customerName}` acknowledges receipt of U-CB-1 notice for environment `{environmentName}` dated `{noticeSentDate}`. We have reviewed the pre-migration export and authorize the destructive column change per §3–§5. Named authoriser: `{customerAuthoriserName}` (`{customerAuthoriserRole}`)."

Spaarke records this reply in the environment's ProvisioningRun record (Cosmos) as the audit trail for the destructive apply. **No reply = no apply.**

## 7. Rollback semantics

If a defect is discovered after apply, rollback options are (in order of preference):

1. **Restore from pre-migration export** into a NEW column (recommended). Spaarke can re-populate a new column `{columnLogicalName}_restored` from the delivered export within `{restoreSlaHours}` hours. Original column name/type is not reused because the solution version has already advanced.
2. **Solution rollback** to `{previousSolutionVersion}`. Only viable within `{rollbackWindowHours}` hours of apply and only if no dependent solutions have already advanced. Requires a second maintenance window.
3. **Data recovery from Dataverse database backup**. Only Microsoft can restore the environment; RPO/RTO per your Microsoft-side agreement. Spaarke can coordinate but has no direct control over timeline.

Full rollback procedure and criteria: `../../guides/SPAARKE-CUSTOMER-DEPLOYMENT-GUIDE.md` → Rollback section.

---

*Template last reviewed: 2026-08-17 · Author when editing: Spaarke Platform Operations · Change record: track in git.*
