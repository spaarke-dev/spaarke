# Multi-Container Multi-Index Routing — Operator Runbook

> **Status**: Active
> **Last Updated**: 2026-06-07
> **Owner**: Spaarke Operations / Platform Dev Team
> **Project**: `spaarke-multi-container-multi-index-r1`
> **Related Scripts**: `scripts/backfill-multi-container-multi-index/Backfill-MultiContainerMultiIndex-ParentRecords.ps1`, `Backfill-MultiContainerMultiIndex-Documents.ps1`, `Audit-MultiContainerMultiIndex-Drift.ps1`

---

## Overview

Spaarke documents are stored in **SharePoint Embedded (SPE) containers** and indexed in **Azure AI Search indexes** for semantic + keyword retrieval. Multi-container multi-index routing lets each record select its own `(container, index)` pair at create time, with the Business Unit cascading defaults and individual records able to override (for example, a "Protected Matter" stored in a separate access-controlled container + index).

This runbook is the **single self-serve resource for operators** managing routing concerns:
- Setting up BU defaults before a wizard ships
- Assigning a new index to a BU
- Marking a single record as Protected
- Reasoning about drift between BU values and existing-record values
- Onboarding a brand-new physical index
- Understanding the Document container reference convention
- Handling records that bypass the Spaarke create wizards

The binding **invariants (INV-1..INV-8)** that govern these scenarios live in [`projects/spaarke-multi-container-multi-index-r1/design.md`](../../projects/spaarke-multi-container-multi-index-r1/design.md) §3. When this runbook and the design disagree, design.md wins — see the "When in doubt" footer.

---

## Quick Reference

| Scenario | Section | Key invariant |
|---|---|---|
| Pre-deploy BU value setup | [§1](#1-pre-deploy-bu-value-setup) | INV-1 (BU values are defaults at create time) |
| Assign a new index to a BU | [§2](#2-how-to-assign-a-new-index-to-a-bu) | INV-1 + INV-3 (no propagation to existing records) |
| Mark a single record as Protected | [§3](#3-how-to-mark-a-single-record-as-protected) | INV-2 + INV-5 (record fields authoritative; overrides sacred) |
| Drift coexistence model | [§4](#4-drift-coexistence-model) | INV-3 (BU change does NOT propagate) |
| Add a new physical index | [§5](#5-adding-a-new-physical-index) | INV-7 (resolution chain) |
| Document container reference clarification | [§6](#6-document-container-reference-clarification) | INV-6 (container ref + index travel together) |
| Non-wizard creates caveat | [§7](#7-non-wizard-creates-caveat) | INV-7 (server tenant default is last resort) |

---

## Table of Contents

1. [Pre-deploy BU value setup](#1-pre-deploy-bu-value-setup)
2. [How to assign a new index to a BU](#2-how-to-assign-a-new-index-to-a-bu)
3. [How to mark a single record as Protected](#3-how-to-mark-a-single-record-as-protected)
4. [Drift coexistence model](#4-drift-coexistence-model)
5. [Adding a new physical index](#5-adding-a-new-physical-index)
6. [Document container reference clarification](#6-document-container-reference-clarification)
7. [Non-wizard creates caveat](#7-non-wizard-creates-caveat)
8. [References](#references)

---

## 1. Pre-deploy BU value setup

**When**: BEFORE the extended Spaarke create wizards (Matter, Project, Invoice, WorkAssignment, Event, DocumentUploadWizard) ship to users.

**Why**: The wizards read `sprk_searchindexname` from the user's owning BU and write it into the new record's create payload. If the BU value is NULL when a wizard runs, the new record persists NULL for that field — and the BFF will silently fall back to the tenant default at search time. Populating the BU values first means new records get the correct routing from day one.

**Authoritative source**: [design.md §5.0](../../projects/spaarke-multi-container-multi-index-r1/design.md#50-operator-setup--populate-bu-sprk_searchindexname-values-prerequisite) — single source of truth for the value table. Spec covers this as **FR-OPS-01**.

### Canonical BU value table

| Business Unit | `sprk_containerid` (verified via MCP) | `sprk_searchindexname` (set this) |
|---|---|---|
| Spaarke Demo | `b!yLRdWEOAdkaWXskuRfByIRiz1S9kb_xPveFbearu6y9k1_PqePezTIDObGJTYq50` | `spaarke-knowledge-index-v2` |
| Spaarke | `b!vzGDfDpd7km_-_H38Q6ZfbotQXLPXF9Ci71VoQmIOHUKlvxOqBsHQLrROZ5KySLh` | `spaarke-file-index` |
| Spaarke Dev 1 | (NULL today) | operator-determined (R1: leave NULL → tenant default applies) |
| Spaarke Test 1 | (NULL today) | operator-determined (R1: leave NULL → tenant default applies) |

### Procedure

1. Open the Power Apps maker portal at https://make.powerapps.com/ and select the **Spaarke Production** environment.
2. Navigate to **Tables → Business Unit** (search "businessunit").
3. For each BU in the table above:
   1. Open the BU record.
   2. Set `sprk_searchindexname` to the value in column 3 of the table.
   3. Save.
4. Repeat for any other operator-determined BUs (e.g., Spaarke Dev 1, Spaarke Test 1) if non-default routing is desired.

### Verification

Run the following MCP query to confirm BU values are populated:

```
read_query
  entity: businessunit
  select: name, sprk_containerid, sprk_searchindexname
  filter: <none — list all>
```

Expected: at minimum, Spaarke Demo and Spaarke show non-NULL `sprk_searchindexname` matching the canonical table.

### Cross-references

- **Spec**: [spec.md FR-OPS-01](../../projects/spaarke-multi-container-multi-index-r1/spec.md) — operator pre-deploy BU value setup
- **Design**: [design.md §5.0](../../projects/spaarke-multi-container-multi-index-r1/design.md), [§3 INV-1](../../projects/spaarke-multi-container-multi-index-r1/design.md#3-invariants-binding-contracts)
- **Container → index map**: [design.md §5.1](../../projects/spaarke-multi-container-multi-index-r1/design.md#51-container--index-name-mapping-the-table)

---

## 2. How to assign a new index to a BU

**Scenario**: A BU should default new records to a different physical index — e.g., the Spaarke BU has migrated and new records should land in `spaarke-file-index` (instead of the prior `spaarke-knowledge-index-v2`).

**Effect**: NEW records created via the Spaarke wizards after this change inherit the new value. EXISTING records are unaffected — this is intentional per **INV-3** (BU change does NOT propagate). See [§4 Drift coexistence model](#4-drift-coexistence-model).

### Procedure

1. Confirm the new index name appears in the BFF allow-list (`appsettings.AiSearch.AllowedIndexes`). If not, follow [§5 Adding a new physical index](#5-adding-a-new-physical-index) FIRST — otherwise the BFF will return `400 INDEX_NOT_ALLOWED` on every search routed to that index.
2. Open the Power Apps maker portal, navigate to **Tables → Business Unit**.
3. Open the target BU record.
4. Set `sprk_searchindexname` to the new index name (e.g., `spaarke-file-index`).
5. Optionally update `sprk_containerid` to match the new SPE container (the two are paired per INV-6).
6. Save.

### Verification

1. **BU value persisted**: MCP query the BU and confirm both fields show the new values.
2. **Wizard inheritance verified**: As a user owned by that BU, create a new record (Matter / Project / Invoice / WorkAssignment / Event) via the appropriate Spaarke create wizard. MCP query the new record:

   ```
   read_query
     entity: sprk_matter (or sprk_project / sprk_invoice / etc.)
     select: sprk_matterid, sprk_containerid, sprk_searchindexname
     filter: sprk_matterid eq '{new-record-id}'
   ```

   Expected: both fields match the BU's new values.
3. **Existing records unchanged**: MCP-spot-check 2-3 existing records under the same BU — their `sprk_searchindexname` should NOT have changed.

### Cross-references

- **Spec**: [spec.md FR-WIZ-01..05](../../projects/spaarke-multi-container-multi-index-r1/spec.md) — wizards read BU `sprk_searchindexname`
- **Design**: [§3 INV-1, INV-3](../../projects/spaarke-multi-container-multi-index-r1/design.md#3-invariants-binding-contracts), [§4.2.1](../../projects/spaarke-multi-container-multi-index-r1/design.md#421-extend-the-5-parent-record-create-wizards)

---

## 3. How to mark a single record as Protected

**Scenario**: A specific Matter (or Project / Invoice / WorkAssignment / Event) must store its documents in a separate, access-controlled container + index — distinct from the BU's defaults. Common case: "Protected Matter" stored in `spaarke-file-index` while the BU defaults to `spaarke-knowledge-index-v2`.

**Why explicit override works**: Per **INV-2** (record's own fields are authoritative after create) and **INV-5** (explicit overrides are sacred), once you set non-empty values on the record itself, no subsequent default-fill logic (wizards, backfill) will overwrite them. A Document uploaded under that Matter inherits the Matter's explicit values via **INV-4** (Document inherits from immediate parent), not the user's BU.

### Procedure

1. Identify the target record and the protected container + index. Example:
   - Matter: "Protected Client X — Litigation 2026"
   - Target container: `b!vzGDfDpd7km_-_H38Q6ZfbotQXLPXF9Ci71VoQmIOHUKlvxOqBsHQLrROZ5KySLh` (Spaarke production SPE container)
   - Target index: `spaarke-file-index`
2. Open the record (via Power Apps maker portal or the model-driven app) in form-edit mode.
3. Set BOTH fields together (per **INV-6** — container ref + index travel together):
   - `sprk_containerid` = (the SPE container id)
   - `sprk_searchindexname` = (the index name)
4. Save.
5. For records that already had Documents uploaded under the OLD routing, run the Document backfill script if you want their `sprk_searchindexname` field updated — note that the existing documents will REMAIN indexed where they were originally indexed (no automatic re-index). Production re-indexing is a future epic — out of scope for R1.

### Verification

1. **Override persisted**: MCP query the record, confirm both fields show the explicit values.
2. **Wizard respects override**: As a user owned by the BU (whose defaults DIFFER from the override), upload a new Document under that record via the DocumentUploadWizard. MCP query the new Document:

   ```
   read_query
     entity: sprk_document
     select: sprk_documentid, sprk_graphdriveid, sprk_searchindexname
     filter: sprk_documentid eq '{new-doc-id}'
   ```

   Expected: `sprk_graphdriveid` and `sprk_searchindexname` match the Matter's override values — NOT the user's BU defaults.
3. **PCF search returns protected results**: On the form where SemanticSearchControl is hosted, run a search. BFF log should show the resolved index URL pointing to `spaarke-file-index`.

### Cross-references

- **Spec**: [spec.md FR-WIZ-06..08](../../projects/spaarke-multi-container-multi-index-r1/spec.md) — DocumentUploadWizard resolution chain + INV-5 preservation
- **Design**: [§3 INV-2, INV-4, INV-5, INV-6](../../projects/spaarke-multi-container-multi-index-r1/design.md#3-invariants-binding-contracts), [§1 motivation](../../projects/spaarke-multi-container-multi-index-r1/design.md#1-background--motivation) (Protected Matter use case)

---

## 4. Drift coexistence model

**Scenario**: A BU's `sprk_containerid` or `sprk_searchindexname` was changed (typical migration scenario). Existing records under that BU still hold the OLD values. The drift audit report flags these as "possible data drift". This is **CORRECT and intentional** behavior — not a bug.

**Why**: Per **INV-3** (BU change does NOT propagate), updating a BU's defaults does NOT cascade to existing records. The migration scenario depends on this: old documents stay indexed in the old index, and new documents flow into the new index. No sync engine, no re-indexing job.

### What "drift" means in this system

The drift audit script (`Audit-MultiContainerMultiIndex-Drift.ps1`, see [spec.md FR-BF-03](../../projects/spaarke-multi-container-multi-index-r1/spec.md)) emits three classifications:

| Classification | Meaning | Operator action |
|---|---|---|
| **Intentional override** | Record's value differs from its BU's value because someone explicitly set it (e.g., Protected Matter — see [§3](#3-how-to-mark-a-single-record-as-protected)) | None — by design |
| **Possible data drift** | Record's value differs because the BU was changed AFTER the record was created (migration) | None — INV-3 says old records should keep old routing |
| **Anomaly** | Unmapped container; parent-Document container mismatch | Investigate manually; may need correction or §5.1 map extension |

Only **anomalies** require operator action. Intentional overrides and BU drift are expected post-migration state.

### Diagnostic procedure

If you want to verify the system is correctly applying INV-3:

1. Run the drift audit script (no writes, just a report):

   ```powershell
   .\scripts\backfill-multi-container-multi-index\Audit-MultiContainerMultiIndex-Drift.ps1
   ```

2. Review the output CSV/Markdown. Confirm:
   - Records flagged "intentional override" match known Protected Matters.
   - Records flagged "possible data drift" align with the migration timeline (created before the BU change).
   - Records flagged "anomaly" are zero or are investigated individually.

3. **Do NOT** attempt to "fix" drift by running a sync — that would violate INV-3 and re-index documents unnecessarily.

### Cross-references

- **Spec**: [spec.md FR-BF-03](../../projects/spaarke-multi-container-multi-index-r1/spec.md) — drift audit script
- **Design**: [§3 INV-3](../../projects/spaarke-multi-container-multi-index-r1/design.md#3-invariants-binding-contracts), [§5.4 Drift audit](../../projects/spaarke-multi-container-multi-index-r1/design.md#54-drift-audit-informational-no-writes), [§7 Alternative B rejection](../../projects/spaarke-multi-container-multi-index-r1/design.md#7-trade-offs--alternatives-considered)

---

## 5. Adding a new physical index

**Scenario**: A new SharePoint Embedded container + Azure AI Search index has been provisioned and Spaarke should be able to route records to it. Examples: per-tenant index for a partner customer, a discovery-scoped index, a new dev environment.

**Sequence matters**: the BFF allow-list MUST be extended and redeployed BEFORE you set any BU `sprk_searchindexname` to the new value — otherwise users will hit `400 INDEX_NOT_ALLOWED` on every search.

### Procedure

1. **Confirm Azure provisioning**. The index must already exist in the Azure AI Search service in the target tenant. (Index provisioning is operator-side in Azure portal — not automated by Spaarke in R1.)
2. **Extend the BFF allow-list**:
   - Edit `src/server/api/Sprk.Bff.Api/appsettings.template.json` (and any per-environment overrides such as `appsettings.Production.json`).
   - In `AiSearch.AllowedIndexes`, add the new index name. Example:

     ```json
     "AiSearch": {
       "AllowedIndexes": [
         "spaarke-knowledge-index-v2",
         "spaarke-file-index",
         "discovery-index",
         "spaarke-rag-references",
         "your-new-index-name"
       ]
     }
     ```

3. **Redeploy the BFF** via the `/bff-deploy` skill (production-mode publish, framework-dependent linux-x64 — see [spec.md NFR-12](../../projects/spaarke-multi-container-multi-index-r1/spec.md)).
4. **Verify the allow-list at runtime**: BFF startup logs the allow-list at INFO level (per [FR-BFF-06](../../projects/spaarke-multi-container-multi-index-r1/spec.md)). Confirm the new value appears.
5. **Extend the §5.1 container → index map** in the backfill scripts at `scripts/backfill-multi-container-multi-index/` — add the row `(new-container-id, new-index-name)`. Without this, future backfill runs will halt loud on the new container.
6. **Optionally extend this runbook** — add the new container + index to the canonical BU value table in [§1](#1-pre-deploy-bu-value-setup) if a BU will use it as a default.
7. **Set the BU `sprk_searchindexname`** to the new value per [§2 How to assign a new index to a BU](#2-how-to-assign-a-new-index-to-a-bu).
8. **Smoke test**: create one new record via the wizard; verify the new record persists the new index value; run a search; confirm the BFF log shows the issued Azure Search URL pointing to the new index.

### Verification

```
read_query
  entity: businessunit
  select: name, sprk_searchindexname
  filter: name eq '{your-bu-name}'
```

Then create a new record via the appropriate Spaarke create wizard owned by that BU, and MCP query the new record to confirm the field flows through.

### Cross-references

- **Spec**: [spec.md FR-BFF-06](../../projects/spaarke-multi-container-multi-index-r1/spec.md) — `AiSearch.AllowedIndexes` static appsettings allow-list, [FR-BFF-02](../../projects/spaarke-multi-container-multi-index-r1/spec.md) — `400 INDEX_NOT_ALLOWED` ProblemDetails
- **Design**: [§4.3.1 Index allow-list validation](../../projects/spaarke-multi-container-multi-index-r1/design.md#431-index-allow-list-validation-per-design-review-decision), [§5.1 Container → index name mapping](../../projects/spaarke-multi-container-multi-index-r1/design.md#51-container--index-name-mapping-the-table), [§3 INV-7](../../projects/spaarke-multi-container-multi-index-r1/design.md#3-invariants-binding-contracts)

---

## 6. Document container reference clarification

**Important**: Across Spaarke entities, the "container id" field is **named differently on `sprk_document` than on every other entity**, and this is intentional.

| Entity | Container reference field | Notes |
|---|---|---|
| `businessunit` | `sprk_containerid` | Default for new records owned here |
| `sprk_matter`, `sprk_project`, `sprk_invoice`, `sprk_workassignment`, `sprk_event` | `sprk_containerid` | Authoritative for this record's documents |
| **`sprk_document`** | **`sprk_graphdriveid`** | **Canonical. `sprk_containerid` on Document is intentionally NULL.** |

Both fields hold the same kind of value (an SPE container id like `b!yLRdWEOAdkaWXskuRfByIRiz1S9kb_xPveFbearu6y9k1_PqePezTIDObGJTYq50`). The naming difference is historical convention. **Do NOT attempt to populate `sprk_containerid` on `sprk_document`** — Spaarke convention treats `sprk_graphdriveid` as the single canonical Document container reference. Backfill, wizards, and BFF code all rely on this.

**Practical consequence**: if you are debugging document routing or writing a query that joins parent record container to document container, use `sprk_document.sprk_graphdriveid`, NOT `sprk_document.sprk_containerid`.

### Cross-references

- **Spec**: [spec.md Out of Scope — "Populating `sprk_containerid` on `sprk_document`"](../../projects/spaarke-multi-container-multi-index-r1/spec.md), [FR-WIZ-07](../../projects/spaarke-multi-container-multi-index-r1/spec.md) — Document payload contains `sprk_graphdriveid` (not `sprk_containerid`)
- **Design**: [§3 INV-6](../../projects/spaarke-multi-container-multi-index-r1/design.md#3-invariants-binding-contracts), [§4.1 Schema](../../projects/spaarke-multi-container-multi-index-r1/design.md#41-schema-done-by-user-documenting-for-completeness)

---

## 7. Non-wizard creates caveat

**Scenario**: A Matter / Project / Invoice / WorkAssignment / Event / Document record is created OUTSIDE the Spaarke create wizards — for example, via:
- A raw Dataverse Web API call from a custom integration
- A Power Automate flow that creates records
- The Dynamics 365 mobile app
- A future data import job

These paths **bypass the wizard's default-fill logic**. The record will have empty `sprk_searchindexname` (and possibly empty `sprk_containerid`) — the operator must either set the fields manually OR rely on the one-time backfill to populate them from existing-data evidence.

**Why this is acceptable in R1**: the BFF resolver chain (per **INV-7**) falls back to the tenant default when a record's field is empty, so search still works — it just routes to the tenant default index instead of the BU-preferred index. Acceptable in dev/test environments. Production handling (e.g., backfill-on-create plugin or scheduled reconciliation job) is a future epic — explicitly out of scope for R1.

### What to do if you discover such a record

1. **Option A — set the fields manually** (one-off): edit the record in Power Apps maker, set `sprk_containerid` + `sprk_searchindexname` per the BU's defaults (or an explicit override).
2. **Option B — run the backfill** (batch): execute `Backfill-MultiContainerMultiIndex-ParentRecords.ps1` and `Backfill-MultiContainerMultiIndex-Documents.ps1`. The scripts derive correct values from existing-data evidence (child Documents' `sprk_graphdriveid` for parent records; the Document's own `sprk_graphdriveid` for documents) and NEVER overwrite explicit non-null values (**INV-5**).

### Detection

Periodically run the drift audit script. Records with empty `sprk_searchindexname` will appear in the audit output and can be remediated via either path above.

### Cross-references

- **Spec**: [spec.md FR-BF-01..04](../../projects/spaarke-multi-container-multi-index-r1/spec.md) — backfill scripts cover non-wizard-create gaps, [Out of Scope: Re-indexing API](../../projects/spaarke-multi-container-multi-index-r1/spec.md)
- **Design**: [§4.2.4 Non-wizard create paths](../../projects/spaarke-multi-container-multi-index-r1/design.md#424-non-wizard-create-paths), [§3 INV-5, INV-7, INV-8](../../projects/spaarke-multi-container-multi-index-r1/design.md#3-invariants-binding-contracts)

---

## References

### Project sources (authoritative)

- [`projects/spaarke-multi-container-multi-index-r1/design.md`](../../projects/spaarke-multi-container-multi-index-r1/design.md) — **single source of truth for INV-1..INV-8** (§3), BU value table (§5.0), container → index map (§5.1), operator scenarios outline (§6), 4 rounds of design review (§13)
- [`projects/spaarke-multi-container-multi-index-r1/spec.md`](../../projects/spaarke-multi-container-multi-index-r1/spec.md) — AI-implementation spec; FR-OPS-01, FR-WIZ-01..08, FR-BFF-01..07, FR-BF-01..04, FR-DOC-01
- [`projects/spaarke-multi-container-multi-index-r1/README.md`](../../projects/spaarke-multi-container-multi-index-r1/README.md) — project overview

### Backfill + audit scripts

- `scripts/backfill-multi-container-multi-index/Backfill-MultiContainerMultiIndex-ParentRecords.ps1`
- `scripts/backfill-multi-container-multi-index/Backfill-MultiContainerMultiIndex-Documents.ps1`
- `scripts/backfill-multi-container-multi-index/Audit-MultiContainerMultiIndex-Drift.ps1`

### Applicable ADRs

| ADR | Relevance to this runbook |
|---|---|
| **ADR-013** | AI architecture / semantic search foundation |
| **ADR-019** | ProblemDetails on BFF errors (`400 INDEX_NOT_ALLOWED` returned when allow-list rejects a value) |
| **ADR-028** | Spaarke Auth v2 (`@spaarke/auth.authenticatedFetch` for all client → BFF calls) |
| **ADR-029** | BFF publish hygiene (BFF redeploys for allow-list changes must follow framework-dependent + size baseline rules) |

### Related guides

- [`HOW-TO-SETUP-CONTAINERTYPES-AND-CONTAINERS.md`](HOW-TO-SETUP-CONTAINERTYPES-AND-CONTAINERS.md) — SPE container provisioning prerequisite
- [`AI-DEPLOYMENT-GUIDE.md`](AI-DEPLOYMENT-GUIDE.md) — overall AI pipeline deployment
- [`CUSTOMER-ONBOARDING-RUNBOOK.md`](CUSTOMER-ONBOARDING-RUNBOOK.md) — adjacent runbook style + tone reference

---

## When in doubt

[`projects/spaarke-multi-container-multi-index-r1/design.md`](../../projects/spaarke-multi-container-multi-index-r1/design.md) is the single source of truth for invariants (INV-1..INV-8), the canonical BU value table (§5.0), and the container → index mapping (§5.1). If this runbook diverges from design.md, **design.md wins** — please file a correction PR to this runbook citing the design.md section.

For implementation questions (which file does what, how to extend a wizard), use the corresponding ADR and the project's `tasks/` folder.

For production-incident triage involving wrong routing, escalate to the Spaarke Operations team and capture:
- The record id + entity logical name
- The expected vs. actual `sprk_searchindexname` value
- The owning BU's current value
- BFF log excerpt showing the resolved Azure Search URL
