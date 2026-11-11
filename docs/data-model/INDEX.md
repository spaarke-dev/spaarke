# Data Model

> **Purpose**: Dataverse entity schemas, field references, and relationship documentation for Spaarke
> **Audience**: Developers, AI coding agents, data modelers

## Overview

This directory contains authoritative documentation for Dataverse entity schemas, field definitions, alternate keys, JSON field payloads, and entity relationships used throughout the Spaarke platform.

## Data Model Index

### Cross-Entity References

| Document | Description | Last Updated | Last Reviewed | Status |
|----------|-------------|--------------|---------------|--------|
| **[entity-relationship-model.md](entity-relationship-model.md)** | Complete ERD across all `sprk_*` entities: relationships (1:N, N:1, N:N), cascade rules, dependency graph | 2026-04-05 | 2026-04-05 | New |
| **[field-mapping-reference.md](field-mapping-reference.md)** | Field mapping reference: Dataverse logical names to DTO property names to PCF/Code Page bindings, with type conversions | 2026-04-05 | 2026-04-05 | New |
| **[json-field-schemas.md](json-field-schemas.md)** | JSON field payload schemas for fields that store structured JSON (scope metadata, event config, AI results, workspace layouts) | 2026-04-05 | 2026-04-05 | New |
| [schema-additions-alternate-keys.md](schema-additions-alternate-keys.md) | Alternate key additions and schema enhancements | 2026-04-05 | 2026-04-05 | Current |
| [schema-corrections.md](schema-corrections.md) | Schema corrections applied during refactoring | 2026-02-12 | — | — |

### Entity Documentation — Communication

| Document | Description | Last Updated | Last Reviewed | Status |
|----------|-------------|--------------|---------------|--------|
| [sprk_communication.md](sprk_communication.md) | Communication entity | 2026-04-05 | 2026-04-05 | Current |
| [access-inheritance.md](access-inheritance.md) | `sprk_accessinheritance` on work assignment and project: the BFF-written JSON record of what the stored Secure / Access Permission were derived from (the parents' floor, the parents, what was set on the record) — owner round 87: the parent sets a floor, a child may be stricter, a re-file never loosens | 2026-10-09 | 2026-10-09 | Current |
| [child-access-permission.md](child-access-permission.md) | `sprk_accesspermission` on To Do, Event, Communication and Document: a display copy of the parent's value (most restrictive across parents), written by the BFF, locked on the form while a parent exists; a parentless record keeps its own (owner round 81). Includes `sprk_document.sprk_accesspermission` | 2026-10-08 | 2026-10-08 | Current |
| [sprk_createdbycontact.md](sprk_createdbycontact.md) | `sprk_createdbycontact` on Document, To Do, Event and Communication: the contact-creator lookup the BFF stamps from the resolved principal (FLS: BFF writers only); read by the R3 delete-own check (FR-25/FR-26). Service requests use `sprk_requestedby` | 2026-10-10 | 2026-10-10 | Current |
| [sprk_communicationaccount.md](sprk_communicationaccount.md) | Communication account entity | 2026-04-05 | 2026-04-05 | Current |

### Entity Documentation — Notification Spine

| Document | Description | Last Updated | Last Reviewed | Status |
|----------|-------------|--------------|---------------|--------|
| [sprk_notificationoutbox.md](sprk_notificationoutbox.md) | Notification Outbox (Layer B) — durable, per-user, `kind`-typed pending-notification outbox; write/pending/delivered/dismissed/expired lifecycle; ADR-024 MINIMAL regarding pattern | 2026-07-21 | 2026-07-21 | Current |

### Entity Documentation — Financial

| Document | Description | Last Updated | Last Reviewed | Status |
|----------|-------------|--------------|---------------|--------|
| [sprk_billingevent.md](sprk_billingevent.md) | Billing event entity | 2026-02-12 | — | — |
| [sprk_billingcode.md](sprk_billingcode.md) | UTBMS code sets: `sprk_billingcodeset` + `sprk_billingcode`, alternate key, default-code-set lookups on the matter-type / practice-area tables, the eight seeded sets (eBilling R1 task 018) | 2026-10-15 | 2026-10-15 | Current |
| [sprk_ratecard.md](sprk_ratecard.md) | Rate cards: `sprk_ratecard` + `sprk_ratecardrate`, the `sprk_timekeeper` roster additions (firm, effective dates, Approved / Pending / Inactive), the LEDES-only `sprk_timekeeperrole` choice (eBilling R1 task 017, D-84) and the separate `sprk_userprofilerole` choice behind `sprk_userprofile.sprk_primaryrole` | 2026-10-15 | 2026-10-15 | Current |
| [sprk_ruleset.md](sprk_ruleset.md) | Rule sets and the billing extensions to the shared policy store: `sprk_ruleset`, `sprk_policydomain`, `sprk_policy` domain / rule set, `sprk_policyversion` clause / consequence / proposal status / rejection reason, visible-to-firms flag (eBilling R1 tasks 019, 077) | 2026-10-15 | 2026-10-18 | Current |
| [sprk_invoicesubmission.md](sprk_invoicesubmission.md) | Invoice Submission — one row per submission attempt (accepted, rejected, failed): tracking number `{firm invoice number}.{n}`, channel, processing status, findings, resubmission-of, ADR-024 regarding lookups, firm-scoped alternate key (eBilling R1 task 020) | 2026-10-15 | 2026-10-15 | Current |
| [sprk_invoiceapproval.md](sprk_invoiceapproval.md) | Invoice Approval — the approval portion (invoice x core record): status Pending / Approved / Rejected / Returned, approved amount, Decision Record link, ADR-024 regarding lookups, eligible approvers as an N:N to `contact` for the "Invoices to review" view (eBilling R1 task 021) | 2026-10-15 | 2026-10-15 | Current |
| [sprk_billingexport.md](sprk_billingexport.md) | Billing Export — one AP export batch: invoice count, total, formats (CSV / JSON / XML), status Generating / Completed / Failed, mapping snapshot, CSV and JSON Document lookups, and the single-valued `sprk_invoice.sprk_billingexport` batch lookup (eBilling R1 task 023) | 2026-10-15 | 2026-10-15 | Current |
| [sprk_externalidentifier.md](sprk_externalidentifier.md) | External Identifier — typed identifiers quoted by an issuer (firm invoice number, firm client-matter number, law-department matter number, PO as quoted; AP / VAT / e-invoice / Peppol reserved), normalized matching value, ADR-024 regarding lookups, alternate key (firm, type, normalized value, core record) per D-68 (eBilling R1 task 022) | 2026-10-15 | 2026-10-15 | Current |
| [sprk_invoicecorrection.md](sprk_invoicecorrection.md) | Invoice Correction — the structured correction record (D-64): one row per reviewer decision on a value (field, AI value, final value, AI confidence, outcome type Corrected / Confirmed individually / Accepted in bulk / Unreviewed, stored label flag, Action / analyzer code and version, reviewer and role, when), the SystemRequired invoice parent, optional line, ADR-024 regarding lookups; queryable per Action version (`ebilling-r1` task 027) | 2026-10-15 | 2026-10-15 | Current |
| [sprk_ebillingsetting.md](sprk_ebillingsetting.md) | eBilling Settings — the one audited singleton settings record (alternate-key singleton): default approver roles per core-record type, rejection threshold 0.90, provisional confidence weights and caps (D-69), related-work window, coding chunk size, AP export mapping, firm-notice AI toggle, default code set; range-checked columns; BFF reader `IEbillingSettingsReader` with a 30 s cache (eBilling R1 task 025) | 2026-10-15 | 2026-10-15 | Current |
| [sprk_budget.md](sprk_budget.md) | Budget entity | 2026-02-12 | — | — |
| [sprk_invoice-matter-project.md](sprk_invoice-matter-project.md) | Invoice / matter / project entities | 2026-02-13 | — | — |
| [sprk_financial-related-entities.md](sprk_financial-related-entities.md) | Includes `sprk_budgetbucket`, `sprk_invoice`, `sprk_spendsignal`, `sprk_spendsnapshot` (stub entity pages archived under `archive/`) | 2026-02-12 | — | — |
| [sprk_workassignment.md](sprk_workassignment.md) | Work assignment entity | 2026-03-12 | — | — |
| [sprk_kpiassessment.md](sprk_kpiassessment.md) | KPI assessment entity | 2026-02-13 | — | — |

### Entity Documentation — AI Analysis

> The 2026-02 ERD docs (`sprk_ERD-ai-analysis-entities.md`, `sprk_ai-analysis-related-entities.md`) were **deleted 2026-07-05** (stale snapshots showed pre-R7 schema) and replaced by the four live-schema docs below per FR-P4-03 (`spaarke-ai-architecture-redesign-r1` task 052).

| Document | Description | Last Updated | Last Reviewed | Status |
|----------|-------------|--------------|---------------|--------|
| **[sprk_analysisaction.md](sprk_analysisaction.md)** | Action (execution unit): `prompted`/`coded` kinds, `sprk_inputschema` JSON contract (OpenAI function-parameters; object-level `required` array ONLY), model tiers | 2026-07-07 | 2026-07-07 | Current |
| **[sprk-playbookconsumer.md](sprk-playbookconsumer.md)** | Binding (invocation unit) — THE single routing surface (ADR-039): resolution algorithm, dispositions + Output Router legs, `sprk_tooldescription` intent surface, chips, event bindings, live rows | 2026-07-07 | 2026-07-07 | Current |
| **[sprk_analysistool.md](sprk_analysistool.md)** | Closed tool catalog: namespaced `sprk_toolid`, row↔handler bijection health check, `side_effect_class` confirmation gate, seed mirrors | 2026-07-07 | 2026-07-07 | Current |
| **[sprk_playbooknode.md](sprk_playbooknode.md)** | FROZEN playbook-engine node table (Insights family only; no new capability — new composites are `coded` workflows) | 2026-07-07 | 2026-07-07 | Frozen engine (as-is) |

### Entity Documentation — Access Control

> The three access-control tables are split across two locations for historical reasons: the two *current-state* tables are documented next to their solution under `src/solutions/SpaarkeCore/entities/`, the *history* table here. Listed together so a reader finds all three.

| Document | Description | Last Updated | Last Reviewed | Status |
|----------|-------------|--------------|---------------|--------|
| **[sprk_accessevent.md](sprk_accessevent.md)** | Append-only access event log (FR-32): grant/revoke, POA share/unshare, deny add/remove, secure/restricted/standing-grant flag flips. Create-only posture, event-kind × column coverage matrix, replay coverage boundary (BFF hooks vs Dataverse field audit), retention inheritance + GDPR known gap. **No derived-access columns, by rule** | 2026-09-09 | 2026-09-09 | Authored, not deployed |
| [`src/solutions/SpaarkeCore/entities/sprk_externalrecordaccess/entity-schema.md`](../../src/solutions/SpaarkeCore/entities/sprk_externalrecordaccess/entity-schema.md) | Current-state external grants (contact/org → project/matter/work assignment + access level). No history | 2026-08-20 | 2026-08-20 | Current |
| [`src/solutions/SpaarkeCore/entities/sprk_noaccessentry/entity-schema.md`](../../src/solutions/SpaarkeCore/entities/sprk_noaccessentry/entity-schema.md) | Current-state deny list (FR-23): ethical wall + per-child revocation. A veto, never a level. No history | 2026-09-04 | 2026-09-04 | Current |

### Entity Documentation — Matter / Event

| Document | Description | Last Updated | Last Reviewed | Status |
|----------|-------------|--------------|---------------|--------|
| [sprk_matter-related-tables.md](sprk_matter-related-tables.md) | Matter-related tables | 2026-02-20 | — | — |
| [sprk_event-forms-guids.md](sprk_event-forms-guids.md) | Event form GUID reference | 2026-02-12 | — | — |
| [sprk_event-views-guids.md](sprk_event-views-guids.md) | Event view GUID reference | 2026-02-12 | — | — |
| [sprk_event-related-tables.md](sprk_event-related-tables.md) | Event-related tables | 2026-02-18 | — | — |

## For AI Agents

**Loading strategy**: Load specific entity docs when implementing features that touch those entities. For cross-entity work, start with `entity-relationship-model.md` and `field-mapping-reference.md`.

**Common scenarios**:
- Implementing a new field on an entity → Load the entity doc and `field-mapping-reference.md`
- Writing code that reads/writes JSON fields → Load `json-field-schemas.md`
- Understanding entity relationships and cascade behavior → Load `entity-relationship-model.md`
- Adding alternate keys → Load `schema-additions-alternate-keys.md`
