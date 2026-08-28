# U-CB-4 — SPE Container-Type Schema Change (Customer Communication Template)

> **Purpose**: Plain-text operator-facing template to notify a customer that an upcoming Spaarke upgrade includes a SharePoint Embedded (SPE) container-type schema change that requires the Microsoft-side up-to-24h replication window before the new features are usable end-to-end.
> **Applies when**: A Spaarke release depends on a NEW SPE container-type schema version (typically only on major Microsoft SPE SDK updates or when Spaarke adds a new file-level property to the container-type definition). Handler T6 owns the container-type registration + wait.
> **Owner**: Spaarke Platform Operations (release manager). Customer action is minimal — this is primarily an operator-and-Microsoft coordination event.
> **Delivery format**: Plain-text markdown — copy into the operator's chosen channel. No HTML, no branded styling.
> **Related**: `../version-compatibility-matrix.md` · `../../guides/SPAARKE-CUSTOMER-DEPLOYMENT-GUIDE.md` · `projects/customer-provisioning-orchestration-r1/design.md` §14A.4 U-CB-4 · §4B trap T6

---

## 1. Summary

Spaarke is preparing an upgrade for your environment (`{customerName}` / `{environmentName}`) that includes a **SharePoint Embedded container-type schema change**. Microsoft's SPE service applies container-type schema changes with an **up-to-24-hour replication window** across the SharePoint Embedded infrastructure serving your tenant.

Schema change in release `{targetBffVersion}`:

- Container type: `{containerTypeName}` (`{containerTypeId}`)
- Schema change: `{schemaChangeDescription}` (e.g. "adds `matterId` string property to file metadata", "raises max file size from 10GB to 100GB")
- Microsoft SDK dependency: `{msSpeSdkVersion}`

**This is a breaking change (U-CB-4)** only in the sense that the new file-level features gated on the schema change are **inconsistently available** across your tenant during the replication window. Existing files and existing features continue functioning unchanged.

## 2. Trigger conditions (why you are receiving this)

You are receiving this notice because ALL of the following are true:

- Release `{targetBffVersion}` requires SPE container-type schema version `{newSchemaVersion}`.
- Your environment's current SPE container-type schema version is `{currentSchemaVersion}`.
- Microsoft applies container-type schema changes tenant-by-tenant with a replication window per-region.

## 3. Customer impact

- **Existing files**: Unchanged. Continue to open, edit, and be searchable via existing Spaarke surfaces.
- **New files created during the window**: May be created against either the OLD or NEW schema depending on which SPE node in your region has replicated. Spaarke's application-level code handles both schemas gracefully (per T6 post-condition); no user-visible errors.
- **New features gated on the new schema**: `{listNewFeatures}` — inconsistently available. Users may see the feature working in one session and not in the next until replication completes for the SPE node handling their request.
- **Search / indexing**: Newly-created files may take an additional pass through the AI Search re-index cycle to pick up the new-schema properties. Delay: `{expectedReindexLagMinutes}` minutes.

## 4. Timeline

| Milestone | Target date/time (all times {timezone}) |
|---|---|
| This notice sent | `{noticeSentDate}` |
| Container-type registration (T6 apply) | `{registrationDate}` |
| Replication window START (Microsoft-controlled) | `{registrationDate}` |
| Replication window END (Microsoft SLA) | `{registrationDate}` + 24 hours |
| Post-replication verification report (T6 post-condition check) | `{verificationDate}` |
| Full new-feature GA in your tenant | `{gaDate}` (= verification-report date if all nodes replicated) |

Microsoft does not publish per-tenant replication progress. Spaarke's T6 post-condition probe samples the container-type API to confirm consistent behavior across nodes; when all sampled requests observe the new schema, GA is declared.

## 5. Required customer action

Customer action is minimal. Spaarke coordinates with Microsoft directly on your behalf. However:

1. **Awareness only** — no configuration change required on your side.
2. **User expectation setting** — optionally inform end users that `{listNewFeatures}` may have intermittent availability during the `{registrationDate}` + 24h window. Spaarke can supply a suggested end-user notice.
3. **Report anomalies** — if you observe file operations failing (not just new-feature availability, but existing-file operations), report to `{operatorEmail}` immediately. Spaarke's T6 monitoring should catch this; a customer-reported anomaly is a signal Spaarke's monitoring missed something.

## 6. Confirmation of receipt (required)

Please reply to `{operatorEmail}` (or acknowledge in `{acknowledgementChannel}`) with:

> "`{customerName}` acknowledges receipt of U-CB-4 notice for environment `{environmentName}` dated `{noticeSentDate}`. We understand that the SPE container-type schema change replicates over up to 24 hours from `{registrationDate}` and that new-feature availability may be intermittent during the window. No customer action required beyond awareness."

Spaarke records this reply in the ProvisioningRun record for the audit trail. Unlike U-CB-1/2/3, **absence of reply does NOT block apply** — the customer impact is limited to feature-availability during a bounded window that Microsoft controls, so Spaarke proceeds on the release-schedule cadence with a documented "awareness-only" notice.

## 7. Rollback semantics

SPE container-type schema changes are **additive-only** in Microsoft's model — they cannot be rolled back. Recovery options if a problem emerges:

1. **Application-level fallback**: Spaarke's code is written to handle both old-schema and new-schema files gracefully (T6 post-condition explicitly verifies dual-mode operation before declaring GA). If a defect is discovered, Spaarke ships a BFF hot-fix that routes around the new schema; no SPE-side change is required.
2. **New-feature disable**: the new features gated on the schema can be feature-flagged off via the standard Spaarke config surface. Files retain the new-schema properties (invisible to end users) but Spaarke does not consume them.
3. **Container-type re-migration**: in the extreme case of a container-type-level defect, Spaarke can register a NEW container-type at a new version and migrate files. This is a T6-scale event and requires a fresh U-CB-4 notice; recovery time = up to 24h replication window plus migration time.

Full rollback procedure: `../../guides/SPAARKE-CUSTOMER-DEPLOYMENT-GUIDE.md` → SPE Rollback section.

---

*Template last reviewed: 2026-08-17 · Author when editing: Spaarke Platform Operations · Change record: track in git.*
