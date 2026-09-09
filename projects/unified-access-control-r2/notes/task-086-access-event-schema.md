# Task 086 — `sprk_accessevent` schema (FR-32) · execution notes

> **Date**: 2026-09-09 · **Task**: `tasks/086-access-event-log-schema.poml` · **Rigor**: FULL
> **Deliverables**: [`scripts/Deploy-AccessEventEntity.ps1`](../../../scripts/Deploy-AccessEventEntity.ps1) ·
> [`docs/data-model/sprk_accessevent.md`](../../../docs/data-model/sprk_accessevent.md)
> **Live Dataverse mutations made: NONE.** See §4.

---

## 1. 🔔 ESCALATION — the POML's `<escalation><trigger>` FIRED

> **Trigger text**: *"If Dataverse audit configuration on the flag columns (`sprk_issecure`,
> `sprk_accesspermission`, standing-grant columns) is found DISABLED in dev, record it as a blocking
> prerequisite for [task 088] replay in notes and escalate — replay over field audit needs audit
> enabled BEFORE history accrues."*

It fired. Three gaps, verified against live metadata on **2026-09-09** (environment
`spaarkedev1.crm.dynamics.com`; org-level `isauditenabled = True`).

| # | Object | Entity audit | Attribute audit | Impact on task 088 replay |
|---|---|---|---|---|
| **GAP 1** | `sprk_workassignment.sprk_accesspermission` | ✅ True | ❌ **False** | A Restricted flip on a work assignment is recorded **nowhere**. There is no BFF writer for that column today either, so neither replay source covers it. |
| **GAP 2** | `sprk_externalrecordaccess` (whole table) | ❌ **False** | (`sprk_accesslevel`, `statecode` are True — **inert** while entity audit is off) | A grant deactivated **directly in the MDA**, bypassing `RevokeExternalAccessEndpoint`, is invisible to both sources. Replay would report the grant as still live after revocation — a **confidently wrong** answer, exactly what task 088's "honest completeness" constraint exists to prevent. |
| **GAP 3** | `sprk_noaccessentry` (whole table) | ❌ **False** | (`statecode` True — inert) | Same failure for deny entries deactivated in the MDA. |

**Why this is blocking and time-sensitive, not a backlog item**: Dataverse audit history **does not
backfill**. Turning entity audit on records changes from that moment forward; every change before it
is permanently unrecoverable. If UAT generates the grant/deny history that task 088 is meant to
replay *before* these are enabled, that history is gone.

**Recommended fix (owner decision — NOT applied by this task, per the binding no-live-mutation
directive):**

1. Enable **entity-level** audit on `sprk_externalrecordaccess` and `sprk_noaccessentry`
   (`IsAuditEnabled = true`). Their attribute-level flags are already on, so nothing else is needed.
2. Enable **attribute-level** audit on `sprk_workassignment.sprk_accesspermission` to match the
   other two roots.
3. Do it **before** UAT, and before task 088 starts its historical-source work.

There is a second, independent reading of GAPs 2/3 worth putting to the owner: once task 087's
appender is live, BFF-mediated grant/deny changes are covered by the event log regardless of audit
config. Audit on those two tables covers only the **MDA-direct** path. If the owner decides
MDA-direct edits are out of scope (e.g. the forms are locked down), GAPs 2/3 close by policy rather
than by configuration — but that decision must be **recorded**, because task 088's completeness
statement has to say which it is.

### Verification commands used

```powershell
$DV = "https://spaarkedev1.crm.dynamics.com"
$t  = az account get-access-token --resource $DV --query accessToken -o tsv
$h  = @{ Authorization = "Bearer $t"; Accept = "application/json"
         "OData-MaxVersion" = "4.0"; "OData-Version" = "4.0" }

# Entity-level audit
Invoke-RestMethod -Headers $h -Method GET `
  -Uri "$DV/api/data/v9.2/EntityDefinitions(LogicalName='sprk_externalrecordaccess')?`$select=LogicalName,EntitySetName,IsAuditEnabled,OwnershipType"

# Attribute-level audit
Invoke-RestMethod -Headers $h -Method GET `
  -Uri "$DV/api/data/v9.2/EntityDefinitions(LogicalName='sprk_workassignment')/Attributes(LogicalName='sprk_accesspermission')?`$select=LogicalName,AttributeType,IsAuditEnabled"

# Org master switch + retention
Invoke-RestMethod -Headers $h -Method GET `
  -Uri "$DV/api/data/v9.2/organizations?`$select=name,isauditenabled,auditretentionperiodv2"
```

> **Tooling note**: the Dataverse MCP server was **down** for this session
> (`dataverse (CONNECTION_CLOSED)`). All verification above went through the raw Web API with
> PowerShell `Invoke-RestMethod` — Python is a Microsoft Store stub on this machine and cannot be
> used for these checks.

---

## 2. Everything verified against LIVE metadata (2026-09-09)

Nothing below was inferred from a naming convention. Each row is a query that **succeeded**
(or, for `sprk_accessevent`, definitively returned 404).

| Object | Query | Result |
|---|---|---|
| `sprk_accessevent` | `EntityDefinitions(LogicalName='sprk_accessevent')` | **404 NotFound** — the table does not exist. Deployment is an operator step. |
| `SpaarkeCore` solution | `solutions?$filter=uniquename eq 'SpaarkeCore'&$expand=publisherid` | id `fbfef485-e2a8-4b04-a795-7fa607402903`, `ismanaged=False`, v1.1.0.0, publisher **Spaarke**, prefix **`sprk`** |
| `sprk` publishers | `publishers?$filter=customizationprefix eq 'sprk'` | **TWO**: `Spaarke` (`6aeef721-…`) and `PowerAppsToolsPublisher_sprk` (`3e5835b7-…`). Both carry prefix `sprk`, so filtering by prefix alone is ambiguous — the script resolves the publisher **through the solution**, which is unambiguous. |
| `sprk_project` | `EntityDefinitions(...)` | set `sprk_projects`, audit **True**, **UserOwned** |
| `sprk_matter` | same | set `sprk_matters`, audit True, UserOwned |
| `sprk_workassignment` | same | set `sprk_workassignments`, audit True, UserOwned |
| `contact` | same | set `contacts`, audit True |
| `sprk_organization` | same | set `sprk_organizations`, audit True, UserOwned |
| `sprk_recordtype_ref` | same | set `sprk_recordtype_refs`, audit False, OrganizationOwned |
| `sprk_contactorganization` | same | set `sprk_contactorganizations`, audit **True** |
| `sprk_externalrecordaccess` | same | set `sprk_externalrecordaccesses`, audit **False**, **UserOwned** ← see §3 |
| `sprk_noaccessentry` | same | set `sprk_noaccessentries`, audit **False**, OrganizationOwned |
| flag/level attributes | `EntityDefinitions(...)/Attributes(LogicalName='…')` | table in §1 + `sprk_externalrecordaccess.sprk_accesslevel` = Picklist, `statecode` = State |
| org audit master switch | `organizations?$select=isauditenabled,auditretentionperiodv2` | `isauditenabled = True`; `auditretentionperiodv2` **unset** (platform default) |

The `-DryRun` pass of the deployment script was executed against live dev and completed its
reconnaissance successfully — it is a **read-only** path (GETs only) and issued no writes.

---

## 3. Doc defect found in passing (not fixed by this task)

[`src/solutions/SpaarkeCore/entities/sprk_externalrecordaccess/entity-schema.md`](../../../src/solutions/SpaarkeCore/entities/sprk_externalrecordaccess/entity-schema.md)
states **Ownership Type = Organization**. Live metadata says **UserOwned** (verified 2026-09-09 via
`EntityDefinitions(LogicalName='sprk_externalrecordaccess')?$select=OwnershipType`). That is a
seventh stale-column-class defect for this project's tally. Not corrected here — it is outside this
task's declared outputs, and the doc carries a 2026-08-20 correction banner whose author should own
the follow-up.

It also matters for the doc's own reasoning: `sprk_noaccessentry`'s schema doc justifies its
Organization ownership by saying it *"mirrors `sprk_externalrecordaccess` (the sibling grant
table)"* — a premise that is false live. `sprk_noaccessentry` really is Organization-owned; the
sibling it claims to mirror is not.

---

## 4. Live-mutation statement

**No table, column, relationship, option set, solution, publisher, security role, audit setting or
data row was created, altered or deleted in any Dataverse environment by this task.** Every live
interaction was a `GET` against `EntityDefinitions`, `Attributes`, `solutions`, `publishers` or
`organizations`, plus the deployment script's `-DryRun` path, which is GET-only by construction.
Table creation is an explicit operator step (root directive; `.claude/FAILURE-MODES.md` AP-13).

---

## 5. Design decisions and why

| Decision | Reasoning |
|---|---|
| Subject/target as `(kind + id-as-string + name-snapshot)`, **not lookups** | (a) polymorphic across contact/organization/systemuser/team and any target record type — one nullable lookup per type does not scale (ADR-024's dual-field strategy targets *small closed* sets); (b) a `RemoveLink` lookup **nulls itself out** when the referenced record is deleted, destroying the history the table exists to hold. Cost, stated in the doc: no referential integrity — the appender's tests are the enforcement. |
| **Local** option sets, not global | Exactly one consumer (this table). A global option set is tenant-wide shared surface with no second reader — CLAUDE.md §11. `sprk_accesslevel` **mirrors** `sprk_externalrecordaccess`'s values rather than reusing its option set, so a future change to the live grant vocabulary cannot silently rewrite the meaning of historical rows. |
| `sprk_occurredon` **separate from** `createdon` | `createdon` is row-insert time; the two diverge on retried writes and batched cascades. Replay orders on domain time. |
| Table-level **audit ON** for the log itself | Rows are never legitimately updated or deleted, so any audit entry — or any row with `modifiedon ≠ createdon` — is evidence of tampering. Cheap tamper-evidence for a table whose value is being legally defensible. |
| **No** anti-update plugin | A plugin that blocks System Administrator also blocks emergency remediation and any future GDPR erasure; one that does not block SysAdmin adds ceremony without a guarantee. Enforcement is layered instead: one create-only appender + a privilege model with no Write/Delete for the BFF app user + audit as evidence. |
| `sprk_accesslevel` + `sprk_sharedrights` are **not** derived-access columns | Both record what the *operation requested* at the moment it was requested — not an evaluator output. The script enforces the ban mechanically: it re-reads the attribute list post-create and throws on any name matching `effective\|computed\|derived\|resolvedaccess`. |
| Flag-flip events do **not** enumerate affected principals | Enumerating them *is* materializing derived access (spec FR-32 MUST). The row names the record; replay answers "who". |
| Doc at `docs/data-model/`, siblings at `src/solutions/SpaarkeCore/entities/` | The POML declares `docs/data-model/` and CLAUDE.md §14 agrees. The split from the two sibling docs is pre-existing; called out explicitly in the doc so a reader finds all three. |
| Deploy script does **not** touch security roles | An incorrect privilege grant on this table is a security defect. The create-only posture is documented as an operator step and reviewed deliberately. |

---

## 6. Stale premises in the POML (reality wins)

1. **Cross-task numbering is wrong throughout Phase 5.** POML 086 calls the write hooks "task 081"
   and replay "task 082"; POML 087 calls this table "task 080" and 088 calls the hooks "081". The
   actual files are **086** (this table) → **087** (append hooks) → **088** (versioning + replay).
   Task 081 is `scope-tenant-container-resolver-diagnostic`, 082 is
   `caller-identity-primitive-census`, 080 is `authorize-cross-record-search` — unrelated work.
   The deliverables here use the **real** numbers.
2. **Step 2 says "run against dev".** Overridden by the binding owner directive: schema work on this
   project is CODE + DOCS ONLY; live creation is an operator step. Delivered as an idempotent script
   plus a `-DryRun` reconnaissance mode, exercised read-only against dev.
3. **Step 4 says "Update TASK-INDEX.md".** Not done — `TASK-INDEX.md` and `current-task.md` are owned
   by the main session in this parallel run. Status reported back instead.
4. `deps` lists **032** and **038**. 038 (`sprk_noaccessentry`) is landed and was used as the format
   and deployment-pattern reference. 032 (the evaluator version anchor) is **not** landed — hence
   `sprk_evaluatorversion` is specified as a plain `String(50)` whose constant and bump policy task
   088 owns, rather than being pinned to a shape that does not exist yet.
5. The hook sites 087 enumerates are **partly unbuilt**: `DenyListEndpoints.cs` (task 064) and the
   consolidated POA seam under `Services/Communication/Access/` (task 060) do not exist yet. The
   event vocabulary covers them anyway — that is the point of authoring the schema first.

---

## 7. What this task deliberately did NOT do

- No live Dataverse mutation of any kind (§4).
- No security-role changes — documented as an operator step.
- No views or forms — none are needed for the appender, which writes via the Web API.
- No audit-configuration changes to fix GAPs 1–3 — owner decision, escalated in §1.
- No erasure / retention mechanism — explicitly out of scope; recorded as a known gap in the doc.
- No edit to `TASK-INDEX.md` or `current-task.md` (main session owns both).
- No edit to `scripts/README.md` — the other entity-deployment scripts
  (`Deploy-PrecedentEntity.ps1`, `Deploy-NotificationOutboxEntity.ps1`) are not listed there either,
  so adding only this one would be inconsistent; flagged rather than done unilaterally.
- No correction of the `sprk_externalrecordaccess` ownership-type defect (§3) — outside declared
  outputs.
